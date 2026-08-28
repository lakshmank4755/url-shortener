using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using UrlShortener.Api.Middleware;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Services;
using UrlShortener.Infrastructure.Analytics;
using UrlShortener.Infrastructure.Generation;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Validation;

var builder = WebApplication.CreateBuilder(args);

// ---- Configuration --------------------------------------------------------

var dataDir = builder.Configuration["Storage:DataDirectory"] ?? "data";
var storageSettings = new StorageSettings
{
    ShortUrlsFilePath = Path.Combine(dataDir, "short-urls.json"),
    ClickEventsFilePath = Path.Combine(dataDir, "click-events.json"),
};

var ownHosts = builder.Configuration.GetSection("UrlSafety:OwnHostNames").Get<string[]>()
    ?? ["localhost", "localhost:5000", "localhost:5080"];
var blockedHosts = builder.Configuration.GetSection("UrlSafety:BlockedHostNames").Get<string[]>()
    ?? [];
var safetyOptions = new UrlSafetyOptions
{
    OwnHostNames = ownHosts,
    BlockedHostNames = blockedHosts,
};

// ---- Services ---------------------------------------------------------------

builder.Services.AddControllers();

builder.Services.AddSingleton(storageSettings);
builder.Services.AddSingleton(safetyOptions);

builder.Services.AddSingleton<IShortUrlRepository, JsonFileShortUrlRepository>();
builder.Services.AddSingleton<IClickEventStore, JsonFileClickEventStore>();
builder.Services.AddSingleton<IShortCodeGenerator, Base62ShortCodeGenerator>();
builder.Services.AddSingleton<IUrlSafetyValidator, UrlSafetyValidator>();

// Analytics queue: single shared instance so the controller (producer) and
// background service (consumer) are talking to the same channel.
builder.Services.AddSingleton<ClickEventChannelQueue>();
builder.Services.AddSingleton<IClickEventQueue>(sp => sp.GetRequiredService<ClickEventChannelQueue>());
builder.Services.AddHostedService<ClickEventBackgroundWriter>();

builder.Services.AddScoped<UrlShortenerService>();
builder.Services.AddScoped<AnalyticsService>();

// Rate limiting: protects the creation endpoint from being used to mass-
// generate spam/phishing links. Partitioned per client IP so one abusive
// caller can't exhaust the limit for everyone else. See docs/04.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("create", httpContext =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

// ---- Middleware pipeline ----------------------------------------------------

app.UseMiddleware<ExceptionHandlingMiddleware>();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Exposed for WebApplicationFactory-based integration tests.
public partial class Program;
