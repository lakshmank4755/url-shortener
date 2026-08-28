using Microsoft.Extensions.Logging.Abstractions;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Models;
using UrlShortener.Core.Services;
using UrlShortener.Infrastructure.Analytics;
using UrlShortener.Infrastructure.Generation;
using UrlShortener.Infrastructure.Persistence;
using UrlShortener.Infrastructure.Validation;
using UrlShortener.TestHarness;

Console.WriteLine("UrlShortener.TestHarness — executable evidence suite");
Console.WriteLine("(mirrors tests/UrlShortener.UnitTests + IntegrationTests; see docs/06)");
Console.WriteLine();

// ---------------------------------------------------------------------------
Console.WriteLine("-- Base62ShortCodeGenerator --");
{
    var gen = new Base62ShortCodeGenerator();

    MiniTest.Run("Generate returns 7-character code", () =>
        MiniTest.Equal(7, gen.Generate().Length, "code length"));

    MiniTest.Run("Generate returns only alphanumeric characters", () =>
    {
        var code = gen.Generate();
        MiniTest.True(code.All(char.IsLetterOrDigit), $"code '{code}' has non-alphanumeric chars");
    });

    MiniTest.Run("Generate produces varied output across 500 draws", () =>
    {
        var codes = Enumerable.Range(0, 500).Select(_ => gen.Generate()).ToHashSet();
        MiniTest.True(codes.Count > 495, $"expected high uniqueness, got {codes.Count}/500");
    });
}

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("-- UrlSafetyValidator --");
{
    var validator = new UrlSafetyValidator(new UrlSafetyOptions
    {
        OwnHostNames = ["short.ly"],
        BlockedHostNames = ["known-bad.test"],
    });

    MiniTest.Run("Accepts well-formed https URL", () =>
        MiniTest.True(validator.Validate("https://example.com/path").IsValid, "should be valid"));

    MiniTest.Run("Rejects javascript: scheme", () =>
        MiniTest.False(validator.Validate("javascript:alert(1)").IsValid, "should be rejected"));

    MiniTest.Run("Rejects data: scheme", () =>
        MiniTest.False(validator.Validate("data:text/html,<script>1</script>").IsValid, "should be rejected"));

    MiniTest.Run("Rejects loopback 127.0.0.1 (SSRF guard)", () =>
        MiniTest.False(validator.Validate("http://127.0.0.1/admin").IsValid, "should be rejected"));

    MiniTest.Run("Rejects cloud metadata IP 169.254.169.254 (SSRF guard)", () =>
        MiniTest.False(validator.Validate("http://169.254.169.254/latest/meta-data/").IsValid, "should be rejected"));

    MiniTest.Run("Rejects private range 10.x.x.x (SSRF guard)", () =>
        MiniTest.False(validator.Validate("http://10.1.2.3/internal").IsValid, "should be rejected"));

    MiniTest.Run("Rejects own domain (redirect-loop guard)", () =>
        MiniTest.False(validator.Validate("https://short.ly/abc123").IsValid, "should be rejected"));

    MiniTest.Run("Rejects blocklisted host", () =>
        MiniTest.False(validator.Validate("https://known-bad.test/x").IsValid, "should be rejected"));

    MiniTest.Run("Accepts public IP address", () =>
        MiniTest.True(validator.Validate("http://8.8.8.8/").IsValid, "should be valid"));

    MiniTest.Run("Rejects malformed input", () =>
        MiniTest.False(validator.Validate("not a url").IsValid, "should be rejected"));
}

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("-- UrlShortenerService (against real JSON-file repository, temp dir) --");
{
    var tempDir = Path.Combine(Path.GetTempPath(), "harness-" + Guid.NewGuid().ToString("N"));
    var settings = new StorageSettings
    {
        ShortUrlsFilePath = Path.Combine(tempDir, "short-urls.json"),
        ClickEventsFilePath = Path.Combine(tempDir, "click-events.json"),
    };
    var repo = new JsonFileShortUrlRepository(settings, NullLogger<JsonFileShortUrlRepository>.Instance);
    var generator = new Base62ShortCodeGenerator();
    var validator = new UrlSafetyValidator(new UrlSafetyOptions { OwnHostNames = [], BlockedHostNames = [] });
    var service = new UrlShortenerService(repo, generator, validator, NullLogger<UrlShortenerService>.Instance);

    string? createdCode = null;

    await MiniTest.RunAsync("CreateAsync persists a resolvable short URL", async () =>
    {
        var result = await service.CreateAsync(new CreateShortUrlCommand("https://example.com/harness", null, null, null));
        createdCode = result.ShortCode;
        MiniTest.Equal(7, result.ShortCode.Length, "generated code length");
    });

    await MiniTest.RunAsync("ResolveAsync returns the same long URL", async () =>
    {
        var resolved = await service.ResolveAsync(createdCode!);
        MiniTest.Equal("https://example.com/harness", resolved.LongUrl, "long URL");
    });

    await MiniTest.RunAsync("CreateAsync with custom alias uses that exact code", async () =>
    {
        var result = await service.CreateAsync(new CreateShortUrlCommand("https://example.com/aliased", "harness-alias", null, null));
        MiniTest.Equal("harness-alias", result.ShortCode, "short code");
    });

    await MiniTest.RunAsync("CreateAsync with duplicate alias throws AliasAlreadyInUseException", async () =>
    {
        try
        {
            await service.CreateAsync(new CreateShortUrlCommand("https://example.com/other", "harness-alias", null, null));
            throw new Exception("expected AliasAlreadyInUseException, none was thrown");
        }
        catch (AliasAlreadyInUseException) { /* expected */ }
    });

    await MiniTest.RunAsync("CreateAsync with unsafe URL throws InvalidLongUrlException", async () =>
    {
        try
        {
            await service.CreateAsync(new CreateShortUrlCommand("javascript:alert(1)", null, null, null));
            throw new Exception("expected InvalidLongUrlException, none was thrown");
        }
        catch (InvalidLongUrlException) { /* expected */ }
    });

    await MiniTest.RunAsync("DeleteAsync soft-deletes; ResolveAsync then throws NotFound", async () =>
    {
        var deleted = await service.DeleteAsync(createdCode!);
        MiniTest.True(deleted, "delete should report success");
        try
        {
            await service.ResolveAsync(createdCode!);
            throw new Exception("expected ShortUrlNotFoundException, none was thrown");
        }
        catch (ShortUrlNotFoundException) { /* expected */ }
    });

    await MiniTest.RunAsync("Expired link throws ShortUrlExpiredException, not NotFound", async () =>
    {
        var result = await service.CreateAsync(new CreateShortUrlCommand(
            "https://example.com/expiring", null, DateTimeOffset.UtcNow.AddMilliseconds(200), null));
        await Task.Delay(400);
        try
        {
            await service.ResolveAsync(result.ShortCode);
            throw new Exception("expected ShortUrlExpiredException, none was thrown");
        }
        catch (ShortUrlExpiredException) { /* expected */ }
    });

    await MiniTest.RunAsync("Persistence survives a fresh repository instance over the same file", async () =>
    {
        var repo2 = new JsonFileShortUrlRepository(settings, NullLogger<JsonFileShortUrlRepository>.Instance);
        var reloaded = await repo2.GetByCodeAsync("harness-alias");
        MiniTest.True(reloaded is not null, "record should have been reloaded from disk");
        MiniTest.Equal("https://example.com/aliased", reloaded!.LongUrl, "long URL after reload");
    });

    Directory.Delete(tempDir, recursive: true);
}

// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("-- Analytics pipeline (channel queue -> background writer -> store) --");
{
    var tempDir = Path.Combine(Path.GetTempPath(), "harness-analytics-" + Guid.NewGuid().ToString("N"));
    var settings = new StorageSettings
    {
        ShortUrlsFilePath = Path.Combine(tempDir, "short-urls.json"),
        ClickEventsFilePath = Path.Combine(tempDir, "click-events.json"),
    };
    var urlRepo = new JsonFileShortUrlRepository(settings, NullLogger<JsonFileShortUrlRepository>.Instance);
    var clickStore = new JsonFileClickEventStore(settings, NullLogger<JsonFileClickEventStore>.Instance);
    var queue = new ClickEventChannelQueue();
    var writer = new ClickEventBackgroundWriter(queue, clickStore, NullLogger<ClickEventBackgroundWriter>.Instance);
    var analytics = new AnalyticsService(urlRepo, clickStore, queue);

    var seed = new ShortUrl
    {
        ShortCode = "analytics1",
        LongUrl = "https://example.com/tracked",
        CreatedAtUtc = DateTimeOffset.UtcNow,
    };
    await urlRepo.AddAsync(seed);

    using var cts = new CancellationTokenSource();
    var writerTask = writer.StartAsync(cts.Token);

    await MiniTest.RunAsync("TryRecordClick enqueues without throwing or blocking", async () =>
    {
        for (var i = 0; i < 5; i++)
            MiniTest.True(analytics.TryRecordClick("analytics1", "https://google.com", "desktop"), "enqueue should succeed under normal load");
        await Task.CompletedTask;
    });

    await MiniTest.RunAsync("Background writer drains queue and analytics reflects all clicks", async () =>
    {
        ClickAnalytics? result = null;
        for (var i = 0; i < 30 && (result is null || result.TotalClicks < 5); i++)
        {
            result = await analytics.GetAnalyticsAsync("analytics1");
            if (result.TotalClicks < 5) await Task.Delay(50);
        }
        MiniTest.Equal(5L, result!.TotalClicks, "total clicks after drain");
        MiniTest.True(result.ClicksByDevice.TryGetValue("desktop", out var n) && n == 5, "device breakdown");
    });

    await cts.CancelAsync();
    try { await writerTask; } catch (OperationCanceledException) { }
    Directory.Delete(tempDir, recursive: true);
}

return MiniTest.Summarize();
