using System.Threading.Channels;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Models;

namespace UrlShortener.Infrastructure.Analytics;

/// <summary>
/// Bounded in-memory queue between the redirect hot path (producer) and the
/// background writer (single consumer). Bounded + DropWrite means a burst of
/// traffic degrades analytics completeness before it ever adds latency to a
/// redirect — a deliberate trade-off, see docs/03-scenario-brownfield.md.
/// </summary>
public sealed class ClickEventChannelQueue : IClickEventQueue
{
    private const int Capacity = 2000;
    private readonly Channel<ClickEvent> _channel = Channel.CreateBounded<ClickEvent>(
        new BoundedChannelOptions(Capacity)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });

    private long _droppedCount;
    public long DroppedCount => Interlocked.Read(ref _droppedCount);

    public bool TryEnqueue(ClickEvent clickEvent)
    {
        var enqueued = _channel.Writer.TryWrite(clickEvent);
        if (!enqueued) Interlocked.Increment(ref _droppedCount);
        return enqueued;
    }

    public IAsyncEnumerable<ClickEvent> ReadAllAsync(CancellationToken ct) =>
        _channel.Reader.ReadAllAsync(ct);
}
