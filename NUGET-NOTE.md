# NuGet note

The `Core`, `Infrastructure`, `Api`, and `TestHarness` projects have **zero**
third-party NuGet package references by design (see docs/06). They restore
and build fine offline.

`tests/UrlShortener.UnitTests` and `tests/UrlShortener.IntegrationTests` do
reference standard packages (xUnit, Moq, `Microsoft.AspNetCore.Mvc.Testing`)
and need normal internet access to `nuget.org` the first time you
`dotnet restore` them — this is completely standard for any .NET test
project and needs no special configuration on a normal machine or CI runner.
