using UrlShortener.Core.Models;

namespace UrlShortener.Core.Abstractions;

/// <summary>
/// Persistence seam for ShortUrl records. Swappable: the prototype ships a
/// JSON-file-backed implementation (see Infrastructure), but production would
/// implement this against SQL Server/Postgres via EF Core without touching
/// any caller of this interface.
/// </summary>
public interface IShortUrlRepository
{
    Task<ShortUrl?> GetByCodeAsync(string shortCode, CancellationToken ct = default);
    Task<bool> ExistsAsync(string shortCode, CancellationToken ct = default);
    Task AddAsync(ShortUrl shortUrl, CancellationToken ct = default);
    Task<bool> SoftDeleteAsync(string shortCode, CancellationToken ct = default);
    Task<(IReadOnlyList<ShortUrl> Items, int TotalCount)> ListAsync(
        int page, int pageSize, CancellationToken ct = default);
}
