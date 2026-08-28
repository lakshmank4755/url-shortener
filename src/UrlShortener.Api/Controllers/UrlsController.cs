using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using UrlShortener.Api.Contracts;
using UrlShortener.Core.Services;

namespace UrlShortener.Api.Controllers;

[ApiController]
[Route("api/urls")]
public sealed class UrlsController(
    UrlShortenerService urlShortenerService,
    AnalyticsService analyticsService) : ControllerBase
{
    /// <summary>
    /// Creates a shortened URL. Rate-limited per client (see Program.cs,
    /// "create" policy) — this is the endpoint most exposed to abuse
    /// (spam/phishing link generation), see docs/04-scenario-ambiguous.md.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting("create")]
    [ProducesResponseType(typeof(ShortUrlResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetailsResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetailsResponse), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateShortUrlRequest request, CancellationToken ct)
    {
        var creatorHash = ClientHasher.HashClientIp(HttpContext);

        var result = await urlShortenerService.CreateAsync(
            new CreateShortUrlCommand(request.LongUrl, request.CustomAlias, request.ExpiresAtUtc, creatorHash),
            ct);

        var response = ToResponse(result);
        return CreatedAtAction(nameof(GetMetadata), new { shortCode = result.ShortCode }, response);
    }

    [HttpGet("{shortCode}")]
    [ProducesResponseType(typeof(ShortUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetMetadata(string shortCode, CancellationToken ct)
    {
        var record = await urlShortenerService.GetMetadataAsync(shortCode, ct);
        if (record is null) return NotFound();
        return Ok(ToResponse(record));
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResponse<ShortUrlResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List([FromQuery] int page = 1, [FromQuery] int pageSize = 20, CancellationToken ct = default)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var (items, total) = await urlShortenerService.ListAsync(page, pageSize, ct);
        return Ok(new PagedResponse<ShortUrlResponse>
        {
            Items = items.Select(ToResponse).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = total,
        });
    }

    [HttpDelete("{shortCode}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(string shortCode, CancellationToken ct)
    {
        var deleted = await urlShortenerService.DeleteAsync(shortCode, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpGet("{shortCode}/analytics")]
    [ProducesResponseType(typeof(ClickAnalyticsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAnalytics(string shortCode, CancellationToken ct)
    {
        var analytics = await analyticsService.GetAnalyticsAsync(shortCode, ct);
        return Ok(new ClickAnalyticsResponse
        {
            ShortCode = analytics.ShortCode,
            TotalClicks = analytics.TotalClicks,
            LastClickedAtUtc = analytics.LastClickedAtUtc,
            ClicksByDay = analytics.ClicksByDay.Select(d => new DailyClickCountDto(d.Date, d.Count)).ToList(),
            ClicksByDevice = analytics.ClicksByDevice,
        });
    }

    private ShortUrlResponse ToResponse(Core.Models.ShortUrl record) => new()
    {
        ShortCode = record.ShortCode,
        ShortUrl = $"{Request.Scheme}://{Request.Host}/{record.ShortCode}",
        LongUrl = record.LongUrl,
        CreatedAtUtc = record.CreatedAtUtc,
        ExpiresAtUtc = record.ExpiresAtUtc,
        IsCustomAlias = record.IsCustomAlias,
    };
}
