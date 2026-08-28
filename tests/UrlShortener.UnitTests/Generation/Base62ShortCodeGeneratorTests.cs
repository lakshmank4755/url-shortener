using UrlShortener.Infrastructure.Generation;
using Xunit;

namespace UrlShortener.UnitTests.Generation;

public class Base62ShortCodeGeneratorTests
{
    private readonly Base62ShortCodeGenerator _sut = new();

    [Fact]
    public void Generate_ReturnsSevenCharacterCode()
    {
        var code = _sut.Generate();
        Assert.Equal(7, code.Length);
    }

    [Fact]
    public void Generate_ReturnsOnlyAlphanumericCharacters()
    {
        var code = _sut.Generate();
        Assert.All(code, c => Assert.True(char.IsLetterOrDigit(c)));
    }

    [Fact]
    public void Generate_ProducesVariedOutput_NotConstant()
    {
        // Statistical smoke test, not a proof of randomness: 200 draws from a
        // 62^7 keyspace should not collide, and should not all be identical.
        var codes = Enumerable.Range(0, 200).Select(_ => _sut.Generate()).ToHashSet();
        Assert.True(codes.Count > 190, $"Expected high uniqueness, got {codes.Count}/200 distinct codes.");
    }
}
