using UrlShortener.Core.Models;

namespace UrlShortener.Core.Abstractions;

public interface IClickEventStore
{
    Task RecordAsync(ClickEvent clickEvent, CancellationToken ct = default);
    Task<ClickAnalytics> GetAnalyticsAsync(string shortCode, CancellationToken ct = default);
}
