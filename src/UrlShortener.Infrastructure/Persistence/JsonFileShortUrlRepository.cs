using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Models;

namespace UrlShortener.Infrastructure.Persistence;

/// <summary>
/// Prototype persistence: an in-memory ConcurrentDictionary as the source of
/// truth for reads (so the redirect hot path never touches disk), with every
/// mutation flushed to a JSON file so state survives a restart.
///
/// This is a deliberate simplification, not the target production design —
/// see docs/01-architecture.md "Persistence" section for what changes
/// (EF Core + relational store, indexed lookups, real transactions) and why
/// this was an acceptable trade-off for a 2-3 day prototype. The interface
/// (IShortUrlRepository) is the seam: nothing above this class needs to
/// change when that swap happens.
/// </summary>
public sealed class JsonFileShortUrlRepository : IShortUrlRepository
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly ConcurrentDictionary<string, ShortUrl> _store = new(StringComparer.Ordinal);
    private readonly string _filePath;
    private readonly SemaphoreSlim _fileLock = new(1, 1);
    private readonly ILogger<JsonFileShortUrlRepository> _logger;

    public JsonFileShortUrlRepository(StorageSettings settings, ILogger<JsonFileShortUrlRepository> logger)
    {
        _filePath = settings.ShortUrlsFilePath;
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

            var items = JsonSerializer.Deserialize<List<ShortUrl>>(json, JsonOpts) ?? [];
            foreach (var item in items)
                _store[item.ShortCode] = item;

            _logger.LogInformation("Loaded {Count} short URLs from {Path}", items.Count, _filePath);
        }
        catch (Exception ex)
        {
            // A corrupt/unreadable store file should not prevent the service
            // from starting — it should start empty and be observable via
            // logs, not fail closed on a prototype-grade persistence file.
            _logger.LogError(ex, "Failed to load short URL store from {Path}; starting empty.", _filePath);
        }
    }

    public Task<ShortUrl?> GetByCodeAsync(string shortCode, CancellationToken ct = default) =>
        Task.FromResult(_store.TryGetValue(shortCode, out var value) ? value : null);

    public Task<bool> ExistsAsync(string shortCode, CancellationToken ct = default) =>
        Task.FromResult(_store.ContainsKey(shortCode));

    public async Task AddAsync(ShortUrl shortUrl, CancellationToken ct = default)
    {
        _store[shortUrl.ShortCode] = shortUrl;
        await PersistAsync(ct);
    }

    public async Task<bool> SoftDeleteAsync(string shortCode, CancellationToken ct = default)
    {
        if (!_store.TryGetValue(shortCode, out var record))
            return false;

        record.IsDeleted = true;
        await PersistAsync(ct);
        return true;
    }

    public Task<(IReadOnlyList<ShortUrl> Items, int TotalCount)> ListAsync(
        int page, int pageSize, CancellationToken ct = default)
    {
        var ordered = _store.Values
            .Where(s => !s.IsDeleted)
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToList();

        var pageItems = ordered.Skip((page - 1) * pageSize).Take(pageSize).ToList();
        return Task.FromResult(((IReadOnlyList<ShortUrl>)pageItems, ordered.Count));
    }

    private async Task PersistAsync(CancellationToken ct)
    {
        await _fileLock.WaitAsync(ct);
        try
        {
            var snapshot = _store.Values.ToList();
            var json = JsonSerializer.Serialize(snapshot, JsonOpts);
            var tmpPath = _filePath + ".tmp";
            await File.WriteAllTextAsync(tmpPath, json, ct);
            File.Move(tmpPath, _filePath, overwrite: true); // write-then-rename avoids a torn/partial file on crash
        }
        finally
        {
            _fileLock.Release();
        }
    }
}
