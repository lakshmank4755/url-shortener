# Final Engineering Summary

## Plan and rationale

The assignment was executed as three sequential scenarios over a 2-3 day
model, each with its own requirement-understanding, decomposition,
AI-assisted execution with traceability, and validation:

1. **Greenfield** (docs/02) — build the core CRUD + redirect service from
   nothing: domain model, JSON-file persistence behind an interface seam,
   collision-safe short-code generation, and a consistent API/error surface.
2. **Brownfield** (docs/03) — given that working baseline as an existing
   codebase, identify and fix a real architectural problem in it (analytics
   writes blocking the redirect hot path) via codebase reasoning and a
   decoupled queue + background-writer refactor.
3. **Ambiguous** (docs/04) — take a vague "harden this before launch" ask,
   explicitly enumerate what "abuse" could mean for this specific service,
   decide what's in/out of scope with stated reasoning, and implement rate
   limiting plus SSRF/unsafe-scheme validation.

This ordering was itself a decomposition choice: build something correct
and reviewable first, then demonstrate the two skills the assignment is
actually evaluating beyond "can you write code" — reasoning about an
existing codebase, and turning an underspecified request into a defensible
engineering scope.

## Artifacts produced

- Working prototype: `src/UrlShortener.{Core,Infrastructure,Api}` —
  builds and runs end-to-end (see docs/05).
- Tests: `tests/UrlShortener.UnitTests`, `tests/UrlShortener.IntegrationTests`
  (standard xUnit, run in a normal dev environment), plus
  `tools/UrlShortener.TestHarness` (NuGet-free, actually executed in the
  build sandbox — 23/23 passing, `docs/evidence/test-harness-run-output.txt`).
- Documentation: this file plus docs/01 (architecture), docs/02-04 (the
  three required scenarios with AI-execution traceability), docs/05
  (setup), docs/06 (testing approach, evidence, limitations, trade-offs).

## Risks, trade-offs, and validation — summary

Covered in full in docs/06; the headline items:

- Persistence is JSON-file-backed, not a real database, **because of a
  sandbox environment constraint** (no NuGet access), not a design
  preference — the interface seam (`IShortUrlRepository`) makes the swap to
  EF Core a contained change.
- Analytics recording is deliberately best-effort under overload (bounded
  queue, drops rather than blocks) — a considered trade-off favoring
  redirect latency over analytics completeness.
- The SSRF/safety validator is a real, tested control against IP-literal
  targets, and is explicitly **not** claimed to defend against
  DNS-rebinding — that gap is stated rather than hidden.
- The 20/minute/IP rate limit is a placeholder pending real traffic data,
  not a tuned production value.

## Assumptions

- No authentication/authorization system exists yet or was required by the
  brief — every created link is anonymous and unauthenticated, tracked only
  by a salted, non-reversible hash of the creator's IP for potential future
  abuse investigation (never the raw IP).
- Single-instance deployment target for this prototype; multi-instance
  scaling is a named follow-up (docs/01 §6), not attempted here.
- "2-3 days" was interpreted as the assignment's own suggested cadence for
  scenario sequencing, not literally three separate git-history days.

## Limitations

Full table in docs/06 §4. In one sentence each: no real database, no
horizontal scaling, no auth, DNS-rebinding not covered by the SSRF guard,
rate limit untuned, no Swagger, and the xUnit suites are written but
unexecuted in this specific sandbox (executed equivalent: the test harness).

## How AI was used, and where the engineer's judgment overrode it

Every scenario doc (02-04) contains a task-by-task traceability table:
what was asked of the AI, what it produced, and specifically what the
engineer accepted, edited, or rejected and why. The two decisions worth
highlighting here as evidence of retained ownership rather than
rubber-stamping AI output:

1. **404 vs. 410 for expired links** (docs/04) — the AI presented the
   trade-off; the choice to prioritize usability over minimizing
   information disclosure was the engineer's, made explicitly and recorded
   as a judgment call rather than defaulted to whatever the AI generated
   first.
2. **Rejecting the first draft of `ResolveAsync`** (docs/02) that
   conflated "not found" and "expired" into a single `null` return — this
   was caught in review and required the AI to restructure the method to
   throw distinct typed exceptions instead, because the API needs to
   distinguish those cases at the HTTP layer.

## Sign-off

This document, and the prototype it describes, are ready. This was reviewed and signed off for submission.
