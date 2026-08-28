# Testing Approach, Evidence, Limitations, and Trade-offs

## 1. A note on this environment, up front

The prototype was assembled inside a sandboxed build environment with **no
outbound network access to nuget.org** (only a small allowlist of domains
like GitHub and the Ubuntu package archive). This shaped two real decisions,
documented here rather than glossed over:

1. **EF Core + SQLite could not be used** for persistence — those packages
   couldn't be restored. A JSON-file-backed repository was used instead,
   entirely behind the same `IShortUrlRepository`/`IClickEventStore`
   interfaces a real database implementation would use (docs/01 §6).
2. **The xUnit test projects could not be executed** in this environment —
   `Microsoft.NET.Test.Sdk`, `xunit`, and `Microsoft.AspNetCore.Mvc.Testing`
   are all NuGet packages. They are written to standard conventions and
   will run with `dotnet test` on any machine or CI runner with normal
   internet access (docs/05). To have *actual, executed* evidence of
   correctness rather than only source code that looks plausible, a second,
   NuGet-free test harness (`tools/UrlShortener.TestHarness`) was built and
   run for real inside this sandbox — see §3.

## 2. Testing approach (three layers)

| Layer | Tool | What it covers |
|---|---|---|
| Unit | xUnit + Moq (`tests/UrlShortener.UnitTests`) | `UrlShortenerService` orchestration logic against mocked repository/generator/validator; `UrlSafetyValidator` rule-by-rule; `Base62ShortCodeGenerator` format/uniqueness. |
| Integration | xUnit + `WebApplicationFactory` (`tests/UrlShortener.IntegrationTests`) | Full HTTP stack — real DI container, real routing/middleware, isolated temp-directory storage per test run — covering create→redirect round trips, error status codes, and the async analytics pipeline end-to-end. |
| Executable evidence (this sandbox) | `tools/UrlShortener.TestHarness` (BCL only) | The same behavioral claims as the unit/integration suites above, run for real against non-mocked implementations. 23/23 assertions passing — see `docs/evidence/test-harness-run-output.txt`. |

Manual smoke testing was also run against the live service via `curl` at
the end of each scenario (docs/02-04). Representative transcript:

```
== create (happy path) ==
{"shortCode":"qzVRmKd","shortUrl":"http://127.0.0.1:5080/qzVRmKd", ...}

== create with duplicate alias (expect 409) ==
HTTP 409

== create with unsafe scheme (expect 400) ==
{"Title":"Invalid Request","Status":400,"Detail":"The submitted URL was
rejected: Scheme 'javascript' is not allowed; only http/https are
permitted.", ...}

== create targeting private/metadata IP (expect 400, SSRF guard) ==
{"Title":"Invalid Request","Status":400,"Detail":"The submitted URL was
rejected: URLs targeting private, loopback, or link-local addresses are
not allowed.", ...}

== redirect (expect 302 + Location header) ==
HTTP/1.1 302 Found
Location: https://www.anthropic.com/claude-code

== analytics (after 3 redirects) ==
{"shortCode":"qzVRmKd","totalClicks":3,"clicksByDay":[{"date":"2026-08-27","count":3}],"clicksByDevice":{"desktop":3}}

== rate limit test: 25 rapid creates (limit=20/min) ==
201 201 201 201 201 201 201 201 201 201 201 201 201 201 201 429 429 429 429 429 429 429 429 429 429

== restart process, then re-fetch a link created before restart ==
{"shortCode":"claude-docs","longUrl":"https://docs.claude.com", ...}   ← proves persistence survives a restart

== expiry test (2-second TTL) ==
redirect immediately: HTTP 302
[wait 3s]
redirect after expiry: HTTP 410 Gone
```

## 3. Test harness output (actual, from this build)

```
UrlShortener.TestHarness — executable evidence suite
(mirrors tests/UrlShortener.UnitTests + IntegrationTests; see docs/06)

-- Base62ShortCodeGenerator --
  PASS  Generate returns 7-character code
  PASS  Generate returns only alphanumeric characters
  PASS  Generate produces varied output across 500 draws

-- UrlSafetyValidator --
  PASS  Accepts well-formed https URL
  PASS  Rejects javascript: scheme
  PASS  Rejects data: scheme
  PASS  Rejects loopback 127.0.0.1 (SSRF guard)
  PASS  Rejects cloud metadata IP 169.254.169.254 (SSRF guard)
  PASS  Rejects private range 10.x.x.x (SSRF guard)
  PASS  Rejects own domain (redirect-loop guard)
  PASS  Rejects blocklisted host
  PASS  Accepts public IP address
  PASS  Rejects malformed input

-- UrlShortenerService (against real JSON-file repository, temp dir) --
  PASS  CreateAsync persists a resolvable short URL
  PASS  ResolveAsync returns the same long URL
  PASS  CreateAsync with custom alias uses that exact code
  PASS  CreateAsync with duplicate alias throws AliasAlreadyInUseException
  PASS  CreateAsync with unsafe URL throws InvalidLongUrlException
  PASS  DeleteAsync soft-deletes; ResolveAsync then throws NotFound
  PASS  Expired link throws ShortUrlExpiredException, not NotFound
  PASS  Persistence survives a fresh repository instance over the same file

-- Analytics pipeline (channel queue -> background writer -> store) --
  PASS  TryRecordClick enqueues without throwing or blocking
  PASS  Background writer drains queue and analytics reflects all clicks

====== 23 passed, 0 failed ======
```

(Full file: `docs/evidence/test-harness-run-output.txt`.)

## 4. Known limitations (stated plainly, not hidden)

| Limitation | Why it exists | What production needs instead |
|---|---|---|
| JSON-file persistence, not a real database | No NuGet/DB access in the build sandbox | EF Core + SQL Server/PostgreSQL, behind the same `IShortUrlRepository` interface |
| Single-process only — in-memory store, in-memory rate limiter | Prototype scope, and the constraint above | Shared DB + distributed rate limiter (e.g. Redis) for multi-instance deployment |
| Click-event analytics reads scan the full in-memory event list | Acceptable at prototype scale | Pre-aggregated counters or a time-series store at real traffic volumes |
| No authentication/authorization | Out of scope per the assignment's focus on core APIs/analytics/reliability | API keys or OAuth2, plus per-user link ownership, before any real launch |
| SSRF validator checks IP literals only, not DNS-rebinding | An application-layer check can't safely resolve-then-recheck without adding its own SSRF surface | Egress-proxy-level network policy (block private ranges at the network, not just the app) |
| Rate limit (20/min/IP) is an unvalidated placeholder | No real traffic data available to tune it | Set from observed legitimate usage patterns post-launch |
| No Swagger/OpenAPI UI | `Swashbuckle.AspNetCore` is a NuGet package unavailable in this sandbox | Add it back — zero code impact, purely additive |
| xUnit suites not executed in this environment | Same NuGet constraint | Run `dotnet test` in a normal environment/CI — see docs/05 |
| Dropped click events under sustained overload (bounded queue, `DropWrite`) | Deliberate trade-off — see docs/03 §4 | Wire `ClickEventChannelQueue.DroppedCount` to real metrics/alerting |

## 5. Trade-offs made under time pressure (2-3 day scope)

- Chose to spend the limited time on **correctness of the abuse/SSRF
  guards** (docs/04) over building out a caching layer for the redirect
  path — reasoned that a wrong redirect (SSRF) is a security incident,
  while a slightly slower redirect (no cache yet) is a performance
  footnote, at this traffic scale.
- Chose **soft-delete over hard-delete** everywhere, trading a small amount
  of storage growth for an audit trail and the ability to distinguish
  "never existed" from "existed, then removed" if that distinction is ever
  needed operationally.
- Chose **not** to add an OpenAPI/Swagger UI given the sandbox constraint,
  rather than hand-writing a static spec that would immediately drift from
  the code — judged a maintained-but-missing doc as better than an
  unmaintained-and-wrong one.
