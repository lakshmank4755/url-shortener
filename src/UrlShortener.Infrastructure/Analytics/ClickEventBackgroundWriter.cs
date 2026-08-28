using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using UrlShortener.Core.Abstractions;

namespace UrlShortener.Infrastructure.Analytics;

/// <summary>
/// Drains the click event queue and persists events, entirely off the
/// request path. A failure to persist one event is logged and does not stop
/// the drain loop — one bad/unwritable event must not take analytics down
/// for every request after it.
/// </summary>
public sealed class ClickEventBackgroundWriter(
    IClickEventQueue queue,
    IClickEventStore store,
    ILogger<ClickEventBackgroundWriter> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var evt in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await store.RecordAsync(evt, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to persist click event for {ShortCode}", evt.ShortCode);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during graceful shutdown.
        }
    }
}
