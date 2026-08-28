using UrlShortener.Infrastructure.Validation;
using Xunit;

namespace UrlShortener.UnitTests.Validation;

public class UrlSafetyValidatorTests
{
    private readonly UrlSafetyValidator _sut = new(new UrlSafetyOptions
    {
        OwnHostNames = ["short.ly", "localhost:5080"],
        BlockedHostNames = ["known-bad.test"],
    });

    [Theory]
    [InlineData("https://example.com/path?query=1")]
    [InlineData("http://example.com")]
    [InlineData("https://sub.example.co.uk/a/b/c")]
    public void Validate_AcceptsWellFormedHttpAndHttpsUrls(string url) =>
        Assert.True(_sut.Validate(url).IsValid);

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("data:text/html,<script>alert(1)</script>")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/file")]
    public void Validate_RejectsDisallowedSchemes(string url) =>
        Assert.False(_sut.Validate(url).IsValid);

    [Theory]
    [InlineData("http://127.0.0.1/admin")]
    [InlineData("http://localhost/admin")]
    [InlineData("http://10.0.0.5/internal")]
    [InlineData("http://192.168.1.1/router")]
    [InlineData("http://169.254.169.254/latest/meta-data/")] // cloud metadata endpoint
    [InlineData("http://172.16.0.1/")]
    public void Validate_RejectsPrivateLoopbackAndLinkLocalTargets_SsrfGuard(string url) =>
        Assert.False(_sut.Validate(url).IsValid);

    [Fact]
    public void Validate_RejectsOwnDomain_PreventingRedirectLoops() =>
        Assert.False(_sut.Validate("https://short.ly/someOtherCode").IsValid);

    [Fact]
    public void Validate_RejectsBlocklistedHost() =>
        Assert.False(_sut.Validate("https://known-bad.test/phishing").IsValid);

    [Fact]
    public void Validate_RejectsEmptyOrWhitespace() =>
        Assert.False(_sut.Validate("   ").IsValid);

    [Fact]
    public void Validate_RejectsUrlOverMaxLength()
    {
        var longUrl = "https://example.com/" + new string('a', 3000);
        Assert.False(_sut.Validate(longUrl).IsValid);
    }

    [Fact]
    public void Validate_RejectsMalformedUri() =>
        Assert.False(_sut.Validate("not a url at all").IsValid);

    [Fact]
    public void Validate_AllowsPublicIpv4Address() =>
        Assert.True(_sut.Validate("http://8.8.8.8/").IsValid);
}
