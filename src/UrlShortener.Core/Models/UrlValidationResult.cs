namespace UrlShortener.Core.Models;

public sealed record UrlValidationResult(bool IsValid, string? RejectionReason)
{
    public static UrlValidationResult Valid() => new(true, null);
    public static UrlValidationResult Invalid(string reason) => new(false, reason);
}
