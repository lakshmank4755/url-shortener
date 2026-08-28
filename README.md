# URL Shortener — AI-Assisted Engineering Assignment

A working URL shortener prototype (ASP.NET Core / .NET 8) built as three
sequential engineering scenarios — greenfield, brownfield, and an ambiguous
requirement — each with full requirement decomposition, AI-assisted
execution traceability, and validation. Built for the *AI-Proficient
Software Engineer* interview assignment.

## Start here

| If you want to... | Read / run |
|---|---|
| Run the service and try it out | [`docs/05-setup-instructions.md`](docs/05-setup-instructions.md) |
| Understand the architecture and key decisions | [`docs/01-architecture.md`](docs/01-architecture.md) |
| See the three required scenarios (with AI-execution traceability) | [`docs/02-scenario-greenfield.md`](docs/02-scenario-greenfield.md), [`docs/03-scenario-brownfield.md`](docs/03-scenario-brownfield.md), [`docs/04-scenario-ambiguous.md`](docs/04-scenario-ambiguous.md) |
| See test evidence, limitations, and trade-offs | [`docs/06-testing-validation.md`](docs/06-testing-validation.md) |
| Read the overall summary/sign-off | [`docs/07-final-engineering-summary.md`](docs/07-final-engineering-summary.md) |

## Quick start

```bash
cd src/UrlShortener.Api
dotnet run
# then, in another terminal:
curl -X POST http://localhost:5080/api/urls -H "Content-Type: application/json" \
  -d '{"longUrl":"https://www.anthropic.com"}'
```

## Solution layout

```
src/UrlShortener.Core            domain models + service logic + interfaces
src/UrlShortener.Infrastructure  JSON-file persistence, Base62 generator, SSRF validator, analytics queue
src/UrlShortener.Api             ASP.NET Core Web API, controllers, middleware, DI wiring
tests/UrlShortener.UnitTests           xUnit unit tests (run in a normal internet-connected environment)
tests/UrlShortener.IntegrationTests    xUnit + WebApplicationFactory integration tests (same)
tools/UrlShortener.TestHarness         NuGet-free executable test suite — actually run during development,
                                        see docs/evidence/test-harness-run-output.txt
docs/                             all required deliverable documentation
```

## One important note

This prototype was built in a sandboxed environment with no access to
nuget.org, which ruled out EF Core/SQLite and the xUnit runtime packages.
The service itself has **zero third-party NuGet dependencies** and runs
fully offline; persistence uses a JSON-file-backed repository sitting
behind the same interface a real database implementation would use. The
xUnit test projects are written to normal conventions and will run with
`dotnet test` in any standard environment — full explanation and the
executed-evidence alternative are in
[`docs/06-testing-validation.md`](docs/06-testing-validation.md).
