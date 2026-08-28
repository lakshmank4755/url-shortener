using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace UrlShortener.IntegrationTests;

/// <summary>Points each test run at an isolated temp directory so integration
/// tests never read/write the developer's real data/ folder and can run
/// concurrently without interfering with each other.</summary>
public class UrlShortenerApiFactory : WebApplicationFactory<Program>
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), "urlshortener-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storage:DataDirectory"] = _dataDir,
            });
        });
    }
}
