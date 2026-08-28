using UrlShortener.Core.Models;

namespace UrlShortener.Core.Abstractions;

/// <summary>
/// Validates that a submitted long URL is safe/well-formed enough to accept.
/// Introduced in the "ambiguous requirement" scenario (see docs/04) — see
/// that doc for the reasoning behind each rule.
/// </summary>
public interface IUrlSafetyValidator
{
    UrlValidationResult Validate(string longUrl);
}
