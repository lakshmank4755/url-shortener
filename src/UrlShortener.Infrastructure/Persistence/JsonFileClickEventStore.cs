using System.Text.Json;
using Microsoft.Extensions.Logging;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Models;

namespace UrlShortener.Infrastructure.Persistence;

/// <summary>
/// Append-only click event log, periodically snapshotted to disk. Reads for
/// analytics scan the in-memory list — acceptable at prototype scale; a
/// production store would aggregate incrementally (e.g. per-shortcode/day
/// counters) instead of scanning raw events. See docs/03 (brownfield) for
/// how this store is fed asynchronously off the redirect hot path.
/// </summary>
public sealed class JsonFileClickEventStore : IClickEventStore
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly List<ClickEvent> _events = [];
    private readonly object _lock = new();
    private readonly string _filePath;
    private readonly ILogger<JsonFileClickEventStore> _logger;

    public JsonFileClickEventStore(StorageSettings settings, ILogger<JsonFileClickEventStore> logger)
    {
        _filePath = settings.ClickEventsFilePath;
        _logger = logger;
        LoadFromDisk();
    }

    private void LoadFromDisk()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (!File.Exists(_filePath)) return;

            var json = File.ReadAllText(_filePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var items = JsonSerializer.Deserialize<List<ClickEvent>>(json, JsonOpts) ?? [];
            _events.AddRange(items);
            _logger.LogInformation("Loaded {Count} click events from {Path}", items.Count, _filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load click event store from {Path}; starting empty.", _filePath);
        }
    }

    public Task RecordAsync(ClickEvent clickEvent, CancellationToken ct = default)
    {
        lock (_lock)
        {
            _events.Add(clickEvent);
        }
        // Snapshot write-behind: this is a background-writer call (already
        // off the request hot path), so a synchronous file write here is an
        // acceptable trade-off for prototype durability. See docs/03.
        return PersistAsync(ct);
    }

    public Task<ClickAnalytics> GetAnalyticsAsync(string shortCode, CancellationToken ct = default)
    {
        List<ClickEvent> matching;
        lock (_lock)
        {
            matching = _events.Where(e => e.ShortCode == shortCode).ToList();
        }

        var byDay = matching
            .GroupBy(e => DateOnly.FromDateTime(e.TimestampUtc.UtcDateTime))
            .OrderBy(g => g.Key)
            .Select(g => new DailyClickCount(g.Key, g.Count()))
            .ToList();

        var byDevice = matching
            .GroupBy(e => e.DeviceCategory ?? "unknown")
            .ToDictionary(g => g.Key, g => (long)g.Count());

        var analytics = new ClickAnalytics
        {
            ShortCode = shortCode,
            TotalClicks = matching.Count,
            LastClickedAtUtc = matching.Count > 0 ? matching.Max(e => e.TimestampUtc) : null,
            ClicksByDay = byDay,
            ClicksByDevice = byDevice,
        };
        return Task.FromResult(analytics);
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        List<ClickEvent> snapshot;
        lock (_lock)
        {
            snapshot = [.. _events];
        }

        var json = JsonSerializer.Serialize(snapshot, JsonOpts);
        var tmpPath = _filePath + ".tmp";
        await File.WriteAllTextAsync(tmpPath, json, ct);
        File.Move(tmpPath, _filePath, overwrite: true);
    }
}
