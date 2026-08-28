using Microsoft.AspNetCore.Mvc;
using UrlShortener.Core.Services;

namespace UrlShortener.Api.Controllers;

/// <summary>
/// The redirect path is the highest-traffic, latency-sensitive endpoint in
/// the whole service — it's on the critical path for every person who
/// clicks a shortened link. It does exactly two things: resolve the code,
/// and (best-effort, non-blocking) record a click. Nothing else.
/// </summary>
[ApiController]
public sealed class RedirectController(
    UrlShortenerService urlShortenerService,
    AnalyticsService analyticsService) : ControllerBase
{
    [HttpGet("/{shortCode}")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status410Gone)]
    public async Task<IActionResult> RedirectToLongUrl(string shortCode, CancellationToken ct)
    {
        var record = await urlShortenerService.ResolveAsync(shortCode, ct);

        var refererHost = Request.Headers.Referer.Count > 0 && Uri.TryCreate(Request.Headers.Referer.ToString(), UriKind.Absolute, out var refUri)
            ? refUri.Host
            : null;
        var deviceCategory = ClassifyDevice(Request.Headers.UserAgent.ToString());

        // Fire-and-forget: never let analytics recording add latency to, or
        // fail, the redirect itself.
        analyticsService.TryRecordClick(shortCode, refererHost, deviceCategory);

        return Redirect(record.LongUrl);
    }

    private static string ClassifyDevice(string userAgent)
    {
        if (string.IsNullOrEmpty(userAgent)) return "unknown";
        var ua = userAgent.ToLowerInvariant();
        if (ua.Contains("bot") || ua.Contains("crawl") || ua.Contains("spider")) return "bot";
        if (ua.Contains("mobile") || ua.Contains("android") || ua.Contains("iphone")) return "mobile";
        return "desktop";
    }
}
