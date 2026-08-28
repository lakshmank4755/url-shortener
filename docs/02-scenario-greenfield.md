# Scenario 1 — Greenfield: Building the URL Shortener Core

**Day 1 of the assignment window.** Starting point: nothing exists yet.

## 1. Requirement understanding

The assignment brief asks for "core APIs, analytics, and reliability
features" with no further specification. Normalized into concrete scope for
Day 1 (analytics and hardening deliberately deferred to Days 2-3 — see
docs/03 and docs/04):

- Create a shortened URL from a long URL, optionally with a caller-chosen
  alias and/or an expiration timestamp.
- Redirect a short code to its long URL.
- Retrieve metadata for a short code, list all short URLs (paginated), and
  delete (soft-delete) one.
- Durable storage that survives a process restart.
- A basic click counter, so analytics has something to build on in Day 2.

Ambiguities identified and resolved (with rationale) before writing code:

| Ambiguity | Resolution | Why |
|---|---|---|
| Are short codes reused after delete? | No — soft delete, code is never reissued. | Reissuing a deleted code risks sending someone to content the original creator no longer intends to point at; soft-delete keeps history auditable. |
| What happens on custom-alias collision? | `409 Conflict`, not silent overwrite. | Overwriting an existing alias could redirect existing traffic to different, potentially malicious content without warning. |
| Fixed-length vs. variable-length codes? | Fixed 7-char Base62. | Predictable response shape for API consumers; keyspace (~3.5T) is generous for a prototype without needing variable length. |

## 2. Task decomposition

1. Domain model (`ShortUrl`, exceptions) — no dependencies.
2. Repository interface + JSON-file implementation.
3. Short code generator (interface + CSPRNG Base62 implementation).
4. `UrlShortenerService` (create/resolve/list/delete orchestration).
5. API layer: DTOs, controllers, global exception-handling middleware, DI
   wiring in `Program.cs`.
6. Manual end-to-end smoke test against the running service.

Dependencies: 1 blocks 2-4; 2-4 must exist before 5; 6 requires 5 built and
running. No step here required another team/service — self-contained.

## 3. AI-assisted execution (traceability)

| # | Task given to AI | What AI produced | Engineer review outcome |
|---|---|---|---|
| 1 | "Design the `ShortUrl` domain model and a small exception hierarchy for not-found / expired / alias-conflict / generation-exhausted cases." | `Models/ShortUrl.cs`, `Exceptions/DomainExceptions.cs` | **Accepted as-is.** `IsAccessible(now)` helper on the model was a good call — keeps the deleted/expired check in one place instead of duplicated across callers. |
| 2 | "Implement `IShortUrlRepository` against a JSON file, since we have no DB access in this environment; must survive process restart and be safe under concurrent requests." | `JsonFileShortUrlRepository` — `ConcurrentDictionary` + write-then-rename JSON snapshot per mutation | **Accepted, with the write-then-rename detail specifically checked** — confirmed this avoids a torn file if the process is killed mid-write. Flagged as a documented prototype-scale limitation (docs/06), not something to silently present as production-grade. |
| 3 | "Generate a short-code generator: not sequential, not predictable." | `Base62ShortCodeGenerator` using `RandomNumberGenerator` | **Accepted.** Verified it wasn't using `Guid`/`Random` (which would be weaker/predictable or non-uniform over the Base62 alphabet). |
| 4 | "Write the orchestration service: create with alias/collision handling, resolve with expiry check, list, delete." | `UrlShortenerService` | **Edited**: the initial draft returned `null` from `ResolveAsync` for both "not found" and "expired" cases. Rejected that — the API needs to tell a caller a link *expired* (410) versus never *existed* (404), so this was changed to throw distinct typed exceptions instead, mapped centrally in the API's exception middleware. |
| 5 | "Wire this up as an ASP.NET Core Web API with DI, and a middleware that maps domain exceptions to HTTP status codes." | Controllers, DTOs, `ExceptionHandlingMiddleware`, `Program.cs` | **Accepted**, with one change: moved the middleware registration to the very top of the pipeline (before rate limiting) so that rate-limiter rejections also flow through the same consistent JSON error shape. |

Quality gates applied at every step: `dotnet build` clean (0 warnings after
step 5's initial unused-parameter warning was fixed), then a full manual
`curl` pass against the running service (see docs/06 for the transcript)
before moving to Day 2.

## 4. Output

- `src/UrlShortener.Core/*`, `src/UrlShortener.Infrastructure/*`,
  `src/UrlShortener.Api/*` (baseline, pre-brownfield/ambiguous changes).
- Working endpoints: `POST /api/urls`, `GET /{shortCode}`,
  `GET /api/urls/{shortCode}`, `GET /api/urls`, `DELETE /api/urls/{shortCode}`,
  `GET /health`.

## 5. Validation

Ran manually against the live service (`dotnet run`):

- Create → 201 with correct short URL shape.
- Create with taken alias → 409.
- Redirect → 302 with correct `Location` header.
- Redirect for unknown/deleted code → 404.
- Metadata, list, delete → all correct status codes and payloads.
- Restarted the process and confirmed previously created links were still
  resolvable — proves the JSON persistence actually persists, not just
  survives within a single run.

Risks/trade-offs accepted at this stage, carried into docs/06:
- Single-process storage (no horizontal scaling yet — acceptable for a
  prototype, called out explicitly rather than silently ignored).
- Click counting was still a placeholder at this point — build out in Day 2
  (docs/03) rather than rushed alongside the core CRUD surface.
