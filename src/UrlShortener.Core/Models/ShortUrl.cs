namespace UrlShortener.Core.Models;

/// <summary>
/// Represents a shortened URL record. Immutable after creation except for
/// soft-delete / expiry state, which is intentional: rewriting a live short
/// code's target would silently break every link already shared with it.
/// </summary>
public sealed class ShortUrl
{
    public required string ShortCode { get; init; }
    public required string LongUrl { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public bool IsCustomAlias { get; init; }
    public bool IsDeleted { get; set; }
    public string? CreatedByHash { get; init; } // salted hash of creator IP, never raw IP

    public bool IsExpired(DateTimeOffset nowUtc) =>
        ExpiresAtUtc is not null && ExpiresAtUtc.Value <= nowUtc;

    public bool IsAccessible(DateTimeOffset nowUtc) => !IsDeleted && !IsExpired(nowUtc);
}
