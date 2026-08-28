namespace UrlShortener.Core.Exceptions;

/// <summary>Base type for all expected/domain-level failures. The API layer
/// maps these to specific HTTP status codes; anything NOT derived from this
/// is treated as an unexpected fault (500) by the global exception handler.</summary>
public abstract class DomainException(string message) : Exception(message);

public sealed class ShortUrlNotFoundException(string shortCode)
    : DomainException($"No short URL found for code '{shortCode}'.");

public sealed class ShortUrlExpiredException(string shortCode, DateTimeOffset expiredAtUtc)
    : DomainException($"Short URL '{shortCode}' expired at {expiredAtUtc:O}.");

public sealed class AliasAlreadyInUseException(string alias)
    : DomainException($"Custom alias '{alias}' is already in use.");

public sealed class InvalidLongUrlException(string reason)
    : DomainException($"The submitted URL was rejected: {reason}");

public sealed class ShortCodeGenerationExhaustedException()
    : DomainException("Could not generate a unique short code after the maximum number of attempts.");
