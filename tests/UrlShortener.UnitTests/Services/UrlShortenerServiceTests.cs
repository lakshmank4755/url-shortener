using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Models;
using UrlShortener.Core.Services;
using Xunit;

namespace UrlShortener.UnitTests.Services;

public class UrlShortenerServiceTests
{
    private readonly Mock<IShortUrlRepository> _repository = new();
    private readonly Mock<IShortCodeGenerator> _codeGenerator = new();
    private readonly Mock<IUrlSafetyValidator> _validator = new();
    private readonly UrlShortenerService _sut;

    public UrlShortenerServiceTests()
    {
        _validator.Setup(v => v.Validate(It.IsAny<string>())).Returns(UrlValidationResult.Valid());
        _sut = new UrlShortenerService(
            _repository.Object, _codeGenerator.Object, _validator.Object,
            NullLogger<UrlShortenerService>.Instance);
    }

    [Fact]
    public async Task CreateAsync_WithValidUrl_GeneratesAndPersistsShortCode()
    {
        _codeGenerator.Setup(g => g.Generate()).Returns("abc1234");
        _repository.Setup(r => r.ExistsAsync("abc1234", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var result = await _sut.CreateAsync(new CreateShortUrlCommand("https://example.com", null, null, null));

        Assert.Equal("abc1234", result.ShortCode);
        Assert.False(result.IsCustomAlias);
        _repository.Verify(r => r.AddAsync(It.Is<ShortUrl>(s => s.ShortCode == "abc1234"), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenGeneratorCollides_RetriesUntilUniqueCode()
    {
        _codeGenerator.SetupSequence(g => g.Generate())
            .Returns("collide")
            .Returns("collide")
            .Returns("unique1");
        _repository.SetupSequence(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        var result = await _sut.CreateAsync(new CreateShortUrlCommand("https://example.com", null, null, null));

        Assert.Equal("unique1", result.ShortCode);
        _codeGenerator.Verify(g => g.Generate(), Times.Exactly(3));
    }

    [Fact]
    public async Task CreateAsync_WhenGeneratorAlwaysCollides_ThrowsAfterMaxAttempts()
    {
        _codeGenerator.Setup(g => g.Generate()).Returns("always");
        _repository.Setup(r => r.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<ShortCodeGenerationExhaustedException>(() =>
            _sut.CreateAsync(new CreateShortUrlCommand("https://example.com", null, null, null)));
    }

    [Fact]
    public async Task CreateAsync_WithUnsafeUrl_ThrowsInvalidLongUrlException_AndNeverCallsRepository()
    {
        _validator.Setup(v => v.Validate(It.IsAny<string>()))
            .Returns(UrlValidationResult.Invalid("blocked scheme"));

        await Assert.ThrowsAsync<InvalidLongUrlException>(() =>
            _sut.CreateAsync(new CreateShortUrlCommand("javascript:alert(1)", null, null, null)));

        _repository.Verify(r => r.AddAsync(It.IsAny<ShortUrl>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithTakenCustomAlias_ThrowsAliasAlreadyInUseException()
    {
        _repository.Setup(r => r.ExistsAsync("taken", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Assert.ThrowsAsync<AliasAlreadyInUseException>(() =>
            _sut.CreateAsync(new CreateShortUrlCommand("https://example.com", "taken", null, null)));
    }

    [Theory]
    [InlineData("ab")]                     // too short
    [InlineData("this-alias-is-way-too-long-to-be-allowed-here")] // too long
    [InlineData("has spaces")]
    [InlineData("has/slash")]
    public async Task CreateAsync_WithInvalidAliasFormat_ThrowsInvalidLongUrlException(string alias)
    {
        await Assert.ThrowsAsync<InvalidLongUrlException>(() =>
            _sut.CreateAsync(new CreateShortUrlCommand("https://example.com", alias, null, null)));
    }

    [Fact]
    public async Task CreateAsync_WithPastExpiry_ThrowsInvalidLongUrlException()
    {
        await Assert.ThrowsAsync<InvalidLongUrlException>(() =>
            _sut.CreateAsync(new CreateShortUrlCommand(
                "https://example.com", null, DateTimeOffset.UtcNow.AddMinutes(-1), null)));
    }

    [Fact]
    public async Task ResolveAsync_WhenCodeMissing_ThrowsShortUrlNotFoundException()
    {
        _repository.Setup(r => r.GetByCodeAsync("nope", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ShortUrl?)null);

        await Assert.ThrowsAsync<ShortUrlNotFoundException>(() => _sut.ResolveAsync("nope"));
    }

    [Fact]
    public async Task ResolveAsync_WhenSoftDeleted_ThrowsShortUrlNotFoundException_NotExposingDeletedState()
    {
        var record = MakeRecord("gone", isDeleted: true);
        _repository.Setup(r => r.GetByCodeAsync("gone", It.IsAny<CancellationToken>())).ReturnsAsync(record);

        await Assert.ThrowsAsync<ShortUrlNotFoundException>(() => _sut.ResolveAsync("gone"));
    }

    [Fact]
    public async Task ResolveAsync_WhenExpired_ThrowsShortUrlExpiredException()
    {
        var record = MakeRecord("exp1234", expiresAtUtc: DateTimeOffset.UtcNow.AddSeconds(-1));
        _repository.Setup(r => r.GetByCodeAsync("exp1234", It.IsAny<CancellationToken>())).ReturnsAsync(record);

        await Assert.ThrowsAsync<ShortUrlExpiredException>(() => _sut.ResolveAsync("exp1234"));
    }

    [Fact]
    public async Task ResolveAsync_WhenActive_ReturnsRecord()
    {
        var record = MakeRecord("live1234");
        _repository.Setup(r => r.GetByCodeAsync("live1234", It.IsAny<CancellationToken>())).ReturnsAsync(record);

        var result = await _sut.ResolveAsync("live1234");

        Assert.Equal("live1234", result.ShortCode);
    }

    [Fact]
    public async Task DeleteAsync_DelegatesToRepository_AndReturnsItsResult()
    {
        _repository.Setup(r => r.SoftDeleteAsync("x", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await _sut.DeleteAsync("x");

        Assert.True(result);
    }

    private static ShortUrl MakeRecord(string code, bool isDeleted = false, DateTimeOffset? expiresAtUtc = null) => new()
    {
        ShortCode = code,
        LongUrl = "https://example.com",
        CreatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
        ExpiresAtUtc = expiresAtUtc,
        IsDeleted = isDeleted,
    };
}
