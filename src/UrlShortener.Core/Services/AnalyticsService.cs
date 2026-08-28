using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Models;

namespace UrlShortener.Core.Services;

public sealed class AnalyticsService(
    IShortUrlRepository repository,
    IClickEventStore clickEventStore,
    IClickEventQueue clickEventQueue)
{
    /// <summary>
    /// Fire-and-forget enqueue from the redirect hot path. Never throws:
    /// analytics must never be able to break a redirect. If the queue is
    /// saturated, the click is dropped and counted in a drop metric rather
    /// than blocking or crashing the request (see docs/03 brownfield scenario).
    /// </summary>
    public bool TryRecordClick(string shortCode, string? refererHost, string? deviceCategory)
    {
        var evt = new ClickEvent
        {
            ShortCode = shortCode,
            TimestampUtc = DateTimeOffset.UtcNow,
            RefererHost = refererHost,
            DeviceCategory = deviceCategory,
        };
        return clickEventQueue.TryEnqueue(evt);
    }

    public async Task<ClickAnalytics> GetAnalyticsAsync(string shortCode, CancellationToken ct = default)
    {
        var exists = await repository.GetByCodeAsync(shortCode, ct)
            ?? throw new ShortUrlNotFoundException(shortCode);

        return await clickEventStore.GetAnalyticsAsync(exists.ShortCode, ct);
    }
}
