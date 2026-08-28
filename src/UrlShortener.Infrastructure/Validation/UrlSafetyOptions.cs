namespace UrlShortener.Infrastructure.Validation;

public sealed class UrlSafetyOptions
{
    public required IReadOnlyCollection<string> OwnHostNames { get; init; }
    public required IReadOnlyCollection<string> BlockedHostNames { get; init; }
    public int MaxUrlLength { get; init; } = 2048;
}
