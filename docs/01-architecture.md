# Architecture Overview

## 1. Objective recap

A URL shortener with core CRUD APIs, click analytics, and reliability
features (rate limiting, SSRF/abuse guards, expiration, durable storage),
built AI-assisted over a 2-3 day window with the engineer retaining
ownership of every accepted change.

## 2. Component map

```
UrlShortener.sln
├── src/
│   ├── UrlShortener.Core            domain models, service logic, interfaces
│   │                                 (zero dependency on Infrastructure or Api)
│   ├── UrlShortener.Infrastructure  concrete implementations of Core's interfaces:
│   │                                 JSON-file persistence, Base62 generator,
│   │                                 SSRF/safety validator, analytics queue+writer
│   └── UrlShortener.Api             ASP.NET Core Web API: controllers, DTOs,
│                                     exception-handling middleware, DI wiring,
│                                     rate limiting
├── tests/
│   ├── UrlShortener.UnitTests        xUnit, Core logic against mocked interfaces
│   └── UrlShortener.IntegrationTests xUnit + WebApplicationFactory, full stack
├── tools/
│   └── UrlShortener.TestHarness      NuGet-free runnable test suite (see docs/06)
└── docs/                             this documentation set
```

This is a conventional layered/"clean" architecture, chosen specifically
because `Core` depends on nothing else — `IShortUrlRepository`,
`IClickEventStore`, `IShortCodeGenerator`, `IUrlSafetyValidator`, and
`IClickEventQueue` are the seams. Swapping JSON-file storage for EF Core +
SQL Server later means writing a new class in `Infrastructure` and changing
one line of DI registration in `Program.cs` — no change to `Core` or to any
controller.

## 3. Request flow

**Create** (`POST /api/urls`):
```
Controller → UrlShortenerService.CreateAsync
  → IUrlSafetyValidator.Validate        (reject unsafe URLs before anything else)
  → IShortCodeGenerator.Generate        (only if no custom alias given)
  → IShortUrlRepository.ExistsAsync     (collision check, retry up to 5x)
  → IShortUrlRepository.AddAsync        (persist)
```

**Redirect** (`GET /{shortCode}`) — the highest-traffic, latency-sensitive path:
```
Controller → UrlShortenerService.ResolveAsync   (repository lookup, expiry/deleted check)
           → 302 response returned immediately
           → AnalyticsService.TryRecordClick     (non-blocking enqueue, fire-and-forget)
                → ClickEventChannelQueue.TryEnqueue
                                                   [background, off request path]
                ClickEventBackgroundWriter drains the channel
                  → IClickEventStore.RecordAsync
```
The redirect never awaits the analytics write. This split is the subject of
the brownfield scenario (docs/03) — the "before" state had this write
in-line and blocking.

## 4. Key architectural decisions

| Decision | Rationale |
|---|---|
| Layered architecture, interfaces owned by `Core` | Persistence/generation/validation are all replaceable without touching business logic or controllers. |
| JSON-file-backed repository, not EF Core/SQL | **Environment constraint, not a design preference** — the build sandbox has no outbound access to nuget.org, so EF Core/SQLite packages could not be restored. The repository is entirely behind `IShortUrlRepository`, so this is a one-file swap in a networked environment. See §6. |
| CSPRNG Base62 codes, not sequential IDs | Sequential/incrementing codes let anyone enumerate every link on the service (`/1`, `/2`, `/3`...). Random 7-char codes over a 62-character alphabet give a ~3.5 trillion keyspace. |
| Async analytics via bounded `Channel<T>` + `BackgroundService` | Decouples the user-facing redirect latency from analytics durability. Bounded + `DropWrite` means a traffic burst degrades analytics completeness, never redirect latency — a deliberate trade-off (docs/03). |
| Centralized exception-handling middleware | Domain exceptions (`ShortUrlNotFoundException`, `ShortUrlExpiredException`, etc.) map to specific status codes in one place, so controllers stay free of try/catch and every error response has a consistent shape. |
| Rate limiting on `POST /api/urls` only | The creation endpoint is what turns this service into a spam/phishing-link generator if abused; redirect traffic is comparatively low-risk to rate-limit at this layer (see docs/04 for the fuller reasoning). |
| No auth/authz in this prototype | Out of scope per the assignment's focus on core APIs/analytics/reliability; called out explicitly as a limitation in docs/06 rather than silently omitted. |

## 5. AI-assisted execution approach

Claude (Sonnet) was used as an in-editor/in-session pair: given a scoped
task with explicit intent, constraints, and acceptance criteria, it produced
an implementation, which was then compiled, run, and smoke-tested against a
live instance of the service before being accepted. Every scenario doc
(docs/02–04) includes a traceability table recording what was generated,
what was reviewed/edited/rejected, and why — the pattern required by the
assignment's "AI-Assisted Execution" criterion. Quality gates applied at
each step: `dotnet build` (0 warnings/errors required before proceeding),
targeted `curl` smoke tests against the running service, and (for the
ambiguous scenario) explicit security-reasoning review of each validation
rule before it was accepted.

## 6. What changes for a real production deployment

This prototype optimizes for "runnable, reviewable, correct" within a 2-3
day / offline-sandbox constraint. A production rollout would change:

- **Persistence**: `JsonFileShortUrlRepository` → EF Core against SQL Server
  or PostgreSQL, with a unique index on `ShortCode` (the JSON file has no
  real concurrency control beyond an in-process lock — see docs/06).
- **Horizontal scaling**: the in-memory `ConcurrentDictionary` and the
  in-memory rate limiter both assume a single process. Multi-instance
  deployment needs a shared store (the same DB migration above) and a
  distributed rate limiter (e.g. Redis-backed).
- **Redirect-path caching**: add a read-through cache (e.g. `IMemoryCache`
  or Redis) in front of the repository lookup on the redirect path once
  request volume justifies it — not added here to avoid caching correctness
  bugs that the assignment's timeframe didn't allow time to fully test.
- **Threat intel**: `UrlSafetyValidator`'s static blocklist is a placeholder
  for a real feed (e.g. Google Safe Browsing) — the interface
  (`IUrlSafetyValidator`) is already the seam for that swap.
- **AuthN/AuthZ, HTTPS enforcement, Swagger/OpenAPI**: intentionally out of
  scope for this exercise; Swagger specifically was dropped because
  Swashbuckle is a NuGet package this sandbox couldn't restore (see docs/05).
