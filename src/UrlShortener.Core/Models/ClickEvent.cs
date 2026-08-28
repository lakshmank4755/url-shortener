namespace UrlShortener.Core.Models;

/// <summary>
/// A single redirect/click event. Deliberately minimal fields: we record
/// enough to power analytics without storing anything that identifies a
/// visiting person (no raw IP, no full user-agent string, no query params
/// from the referrer).
/// </summary>
public sealed class ClickEvent
{
    public required string ShortCode { get; init; }
    public required DateTimeOffset TimestampUtc { get; init; }
    public string? RefererHost { get; init; }
    public string? DeviceCategory { get; init; } // "desktop" | "mobile" | "bot" | "other"
}

public sealed class ClickAnalytics
{
    public required string ShortCode { get; init; }
    public required long TotalClicks { get; init; }
    public DateTimeOffset? LastClickedAtUtc { get; init; }
    public required IReadOnlyList<DailyClickCount> ClicksByDay { get; init; }
    public required IReadOnlyDictionary<string, long> ClicksByDevice { get; init; }
}

public sealed record DailyClickCount(DateOnly Date, long Count);
