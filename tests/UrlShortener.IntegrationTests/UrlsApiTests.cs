using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using UrlShortener.Api.Contracts;
using Xunit;

namespace UrlShortener.IntegrationTests;

/// <summary>
/// Full-stack tests through WebApplicationFactory: real DI container, real
/// routing/middleware pipeline, real (temp-directory) JSON file storage.
/// Each test gets its own isolated data directory via a custom factory so
/// tests can run in parallel without clobbering each other's state.
/// </summary>
public class UrlsApiTests : IClassFixture<UrlShortenerApiFactory>
{
    private readonly HttpClient _client;

    public UrlsApiTests(UrlShortenerApiFactory factory)
    {
        _client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    [Fact]
    public async Task CreateThenRedirect_FullRoundTrip_ReturnsExpectedLocation()
    {
        var createResponse = await _client.PostAsJsonAsync("/api/urls", new CreateShortUrlRequest
        {
            LongUrl = "https://example.com/integration-test",
        });
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<ShortUrlResponse>();
        Assert.NotNull(created);

        var redirectResponse = await _client.GetAsync($"/{created!.ShortCode}");

        Assert.Equal(HttpStatusCode.Found, redirectResponse.StatusCode);
        Assert.Equal("https://example.com/integration-test", redirectResponse.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Create_WithUnsafeUrl_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/api/urls", new CreateShortUrlRequest
        {
            LongUrl = "javascript:alert(1)",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithDuplicateCustomAlias_Returns409()
    {
        var alias = $"dup-{Guid.NewGuid():N}"[..12];
        var first = await _client.PostAsJsonAsync("/api/urls", new CreateShortUrlRequest
        {
            LongUrl = "https://example.com/one", CustomAlias = alias,
        });
        first.EnsureSuccessStatusCode();

        var second = await _client.PostAsJsonAsync("/api/urls", new CreateShortUrlRequest
        {
            LongUrl = "https://example.com/two", CustomAlias = alias,
        });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Redirect_ForUnknownCode_Returns404() =>
        Assert.Equal(HttpStatusCode.NotFound, (await _client.GetAsync("/does-not-exist-xyz")).StatusCode);

    [Fact]
    public async Task DeleteThenRedirect_Returns404()
    {
        var create = await _client.PostAsJsonAsync("/api/urls", new CreateShortUrlRequest
        {
            LongUrl = "https://example.com/to-delete",
        });
        var created = await create.Content.ReadFromJsonAsync<ShortUrlResponse>();

        var deleteResponse = await _client.DeleteAsync($"/api/urls/{created!.ShortCode}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var redirectResponse = await _client.GetAsync($"/{created.ShortCode}");
        Assert.Equal(HttpStatusCode.NotFound, redirectResponse.StatusCode);
    }

    [Fact]
    public async Task Analytics_AfterTwoClicks_ReportsCorrectTotal()
    {
        var create = await _client.PostAsJsonAsync("/api/urls", new CreateShortUrlRequest
        {
            LongUrl = "https://example.com/analytics-test",
        });
        var created = await create.Content.ReadFromJsonAsync<ShortUrlResponse>();

        await _client.GetAsync($"/{created!.ShortCode}");
        await _client.GetAsync($"/{created.ShortCode}");

        // The background writer drains asynchronously; poll briefly rather
        // than sleep-and-hope, keeping the test fast on the common path
        // while still reliable under CI jitter.
        ClickAnalyticsResponse? analytics = null;
        for (var i = 0; i < 20 && (analytics is null || analytics.TotalClicks < 2); i++)
        {
            var resp = await _client.GetAsync($"/api/urls/{created.ShortCode}/analytics");
            analytics = await resp.Content.ReadFromJsonAsync<ClickAnalyticsResponse>();
            if (analytics!.TotalClicks < 2) await Task.Delay(100);
        }

        Assert.Equal(2, analytics!.TotalClicks);
    }

    [Fact]
    public async Task HealthEndpoint_Returns200() =>
        Assert.Equal(HttpStatusCode.OK, (await _client.GetAsync("/health")).StatusCode);
}
