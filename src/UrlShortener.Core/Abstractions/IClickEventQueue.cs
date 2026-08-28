using UrlShortener.Core.Models;

namespace UrlShortener.Core.Abstractions;

/// <summary>
/// Decouples the redirect hot path from analytics persistence. Introduced in
/// the brownfield scenario (see docs/03) to remove a synchronous write from
/// the request that end users are actually waiting on.
/// </summary>
public interface IClickEventQueue
{
    bool TryEnqueue(ClickEvent clickEvent);
    IAsyncEnumerable<ClickEvent> ReadAllAsync(CancellationToken ct);
}
