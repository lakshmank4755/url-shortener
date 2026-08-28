# Setup Instructions

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- No database required — the prototype persists to local JSON files.
- Internet access to nuget.org **only if you want to run the xUnit test
  projects** (see "Running tests" below). The API itself has zero
  third-party NuGet dependencies and builds fully offline.

## Running the API

```bash
cd src/UrlShortener.Api
dotnet restore
dotnet run
```

By default this listens on the URL(s) configured by the ASP.NET Core
defaults (typically `https://localhost:7xxx` and `http://localhost:5xxx` —
check the console output for the exact port). To pin a specific port:

```bash
ASPNETCORE_URLS="http://127.0.0.1:5080" dotnet run
```

Data is written to `src/UrlShortener.Api/data/` (`short-urls.json`,
`click-events.json`), created automatically on first run. Delete that
folder to reset all state.

## Trying it out

```bash
# Create a short URL
curl -X POST http://127.0.0.1:5080/api/urls \
  -H "Content-Type: application/json" \
  -d '{"longUrl":"https://www.anthropic.com"}'
# => { "shortCode": "abc1234", "shortUrl": "http://127.0.0.1:5080/abc1234", ... }

# Create with a custom alias and an expiry
curl -X POST http://127.0.0.1:5080/api/urls \
  -H "Content-Type: application/json" \
  -d '{"longUrl":"https://docs.claude.com","customAlias":"claude-docs","expiresAtUtc":"2027-01-01T00:00:00Z"}'

# Follow the redirect
curl -i http://127.0.0.1:5080/abc1234

# Metadata / analytics / list / delete
curl http://127.0.0.1:5080/api/urls/abc1234
curl http://127.0.0.1:5080/api/urls/abc1234/analytics
curl "http://127.0.0.1:5080/api/urls?page=1&pageSize=20"
curl -X DELETE http://127.0.0.1:5080/api/urls/abc1234

curl http://127.0.0.1:5080/health
```

## Configuration reference (`appsettings.json`)

```json
{
  "Storage": { "DataDirectory": "data" },
  "UrlSafety": {
    "OwnHostNames": [ "localhost", "short.ly" ],
    "BlockedHostNames": [ "known-malware-example.test" ]
  }
}
```

- `Storage:DataDirectory` — where the JSON persistence files live.
- `UrlSafety:OwnHostNames` — hosts that may not be shortened (prevents
  redirect loops); set to your real deployed domain(s) in production.
- `UrlSafety:BlockedHostNames` — a static blocklist; a placeholder for a
  real threat-intel feed (see docs/01 §6).
- The per-IP create-endpoint rate limit (20/minute) is currently a constant
  in `Program.cs` rather than externalized to config — noted as a follow-up
  in docs/06, not done here to avoid adding config surface without a real
  tuning need yet.

## Running tests

### Unit and integration tests (xUnit) — standard `.NET` dev environment

```bash
cd tests/UrlShortener.UnitTests && dotnet test
cd ../UrlShortener.IntegrationTests && dotnet test
```

These require NuGet access (xUnit, `Microsoft.AspNetCore.Mvc.Testing`,
Moq) and will restore/run normally on a machine or CI runner with internet
access. **They could not be executed inside the sandbox this prototype was
assembled in** — see docs/06 for why, and for the alternative evidence that
was captured instead.

### Test harness (no NuGet required) — runs anywhere the API itself runs

```bash
cd tools/UrlShortener.TestHarness
dotnet run
```

This exercises the same behaviors as the xUnit suites (generator format,
safety-validator rules, service orchestration, the async analytics
pipeline) against real, non-mocked implementations, printing PASS/FAIL per
assertion. Its actual output from this build is captured at
`docs/evidence/test-harness-run-output.txt`.

## Building everything

```bash
dotnet build UrlShortener.sln
```
