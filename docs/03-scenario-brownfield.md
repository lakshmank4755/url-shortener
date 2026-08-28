# Scenario 2 — Brownfield: Decoupling Analytics From the Redirect Hot Path

**Day 2 of the assignment window.** Starting point: the greenfield baseline
from docs/02, where `GET /{shortCode}` recorded a click synchronously,
in-line, before returning the redirect.

## 1. Codebase reasoning

Before touching anything, the impacted surface was mapped:

- **`RedirectController.RedirectToLongUrl`** — the only caller of whatever
  analytics API exists; this is the request users are actually waiting on.
- **`AnalyticsService`** (didn't exist yet in this form) — needs to expose a
  way to record a click *and* a way to query aggregated analytics; these are
  different consumers (write from the hot path, read from
  `GET /api/urls/{code}/analytics`) and don't need to share a code path.
- **Storage** — whatever persists click events must not be on the same
  critical path as the redirect's response, or every redirect pays the cost
  of a disk/DB write before the user's browser gets a `Location` header.

**Before** (what existed at the start of Day 2 — reconstructed here for
clarity, since this prototype's history doesn't ship separate before/after
commits):

```csharp
[HttpGet("/{shortCode}")]
public async Task<IActionResult> RedirectToLongUrl(string shortCode, CancellationToken ct)
{
    var record = await urlShortenerService.ResolveAsync(shortCode, ct);
    await clickEventStore.RecordAsync(new ClickEvent { ShortCode = shortCode, TimestampUtc = DateTimeOffset.UtcNow }, ct);
    return Redirect(record.LongUrl);
}
```

Problem: `clickEventStore.RecordAsync` is awaited before `Redirect(...)` is
returned. Every redirect's latency now includes a full file write. Under
load, or if the store is temporarily slow/unavailable, redirects — the one
thing this whole service exists to do fast — get slow or fail right along
with analytics, which is the wrong failure coupling: **analytics should
never be able to break or delay a redirect.**

## 2. Task decomposition

1. Introduce a queue abstraction (`IClickEventQueue`) between "something
   happened" (a click) and "something got persisted" — the redirect only
   needs to do the first part.
2. Implement it as a bounded, in-memory `Channel<ClickEvent>` — bounded so a
   traffic spike can't grow this queue without limit and exhaust memory.
3. Add a `BackgroundService` that drains the channel and calls the existing
   `IClickEventStore.RecordAsync` — this is where the write actually happens
   now, off the request path entirely.
4. Change `AnalyticsService.TryRecordClick` to enqueue (synchronous,
   non-blocking `TryWrite`) instead of awaiting a store write.
5. Change `RedirectController` to call the non-blocking `TryRecordClick`
   and not await anything analytics-related.
6. Decide and document the overflow behavior: what happens if the queue is
   full? (see §4 below — this was a deliberate risk decision, not an
   afterthought.)

## 3. AI-assisted execution (traceability)

| # | Task given to AI | What AI produced | Engineer review outcome |
|---|---|---|---|
| 1-2 | "Design a queue between click-happened and click-persisted that a redirect can write to without ever blocking or throwing." | `IClickEventQueue` interface + `ClickEventChannelQueue` using `System.Threading.Channels`, bounded at 2000, `BoundedChannelFullMode.DropWrite` | **Accepted**, but the capacity and drop-mode were explicitly interrogated rather than taken as given — see §4. `DropWrite` was the correct choice over `Wait` (which would reintroduce blocking, defeating the whole point) or unbounded (which would defeat the memory-safety goal). |
| 3 | "Add a background service that drains this and writes to the existing click store, without one bad event stopping the drain loop for everything after it." | `ClickEventBackgroundWriter : BackgroundService` with a per-event try/catch inside the drain loop | **Accepted.** Specifically checked that the catch is *inside* the `await foreach` loop body, not wrapping the whole loop — confirmed one failed write logs and moves on rather than killing the background service for the rest of the process lifetime. |
| 4-5 | "Update the redirect path to use this instead of awaiting a store write directly." | `AnalyticsService.TryRecordClick` (sync, returns bool), `RedirectController` calling it without `await` | **Accepted.** Reviewed the method signature specifically to confirm it does *not* return a `Task` that a future refactor could accidentally start awaiting again and reintroduce the coupling this change exists to remove. |

Quality gate: after wiring, ran the live service and issued 3 rapid
redirects to the same code, then polled `/api/urls/{code}/analytics` — all
3 clicks appeared within ~100ms of being enqueued (see docs/06 for the
harness assertion that verifies this same behavior deterministically:
*"Background writer drains queue and analytics reflects all clicks"*).

## 4. Validation and risk control

**Risk accepted, explicitly:** under sustained traffic exceeding the
background writer's drain rate, the bounded channel will fill and
`DropWrite` means new click events are silently dropped rather than queued
indefinitely or blocking the caller.

- **Why this is the right trade-off here**: click analytics is a
  nice-to-have signal, not a source of truth the business depends on for
  correctness (unlike, say, a financial ledger). Losing a small percentage
  of click *counts* under an extreme traffic spike is vastly preferable to
  slowing down or failing the redirect itself.
- **Mitigation for observability**: `ClickEventChannelQueue.DroppedCount` is
  tracked (`Interlocked` counter) so this failure mode is measurable, not
  silent, in a production deployment with metrics wired up — noted here as
  a gap this prototype doesn't wire to an actual metrics sink (see docs/06
  limitations).
- **Alternative considered and rejected**: an unbounded queue. Rejected
  because it converts a traffic spike into unbounded memory growth — a
  worse failure mode (process OOM, taking down redirects too) than a few
  dropped analytics events.

## 5. Output

- `src/UrlShortener.Core/Abstractions/IClickEventQueue.cs`
- `src/UrlShortener.Infrastructure/Analytics/ClickEventChannelQueue.cs`
- `src/UrlShortener.Infrastructure/Analytics/ClickEventBackgroundWriter.cs`
- `AnalyticsService.TryRecordClick` (non-blocking), updated
  `RedirectController`
