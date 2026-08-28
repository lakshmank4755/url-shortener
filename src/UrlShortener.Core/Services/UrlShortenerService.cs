using Microsoft.Extensions.Logging;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Exceptions;
using UrlShortener.Core.Models;

namespace UrlShortener.Core.Services;

public sealed record CreateShortUrlCommand(
    string LongUrl,
    string? CustomAlias,
    DateTimeOffset? ExpiresAtUtc,
    string? CreatedByHash);

public sealed class UrlShortenerService(
    IShortUrlRepository repository,
    IShortCodeGenerator codeGenerator,
    IUrlSafetyValidator safetyValidator,
    ILogger<UrlShortenerService> logger)
{
    private const int MaxGenerationAttempts = 5;
    private const int MinAliasLength = 3;
    private const int MaxAliasLength = 32;

    public async Task<ShortUrl> CreateAsync(CreateShortUrlCommand command, CancellationToken ct = default)
    {
        var validation = safetyValidator.Validate(command.LongUrl);
        if (!validation.IsValid)
        {
            logger.LogWarning("Rejected long URL submission: {Reason}", validation.RejectionReason);
            throw new InvalidLongUrlException(validation.RejectionReason!);
        }

        if (command.ExpiresAtUtc is { } expiry && expiry <= DateTimeOffset.UtcNow)
        {
            throw new InvalidLongUrlException("expiresAt must be in the future.");
        }

        var shortCode = await ResolveShortCodeAsync(command.CustomAlias, ct);

        var record = new ShortUrl
        {
            ShortCode = shortCode,
            LongUrl = command.LongUrl,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            ExpiresAtUtc = command.ExpiresAtUtc,
            IsCustomAlias = command.CustomAlias is not null,
            CreatedByHash = command.CreatedByHash,
        };

        await repository.AddAsync(record, ct);
        logger.LogInformation("Created short URL {ShortCode} (custom alias: {IsCustom})",
            shortCode, record.IsCustomAlias);
        return record;
    }

    public async Task<ShortUrl> ResolveAsync(string shortCode, CancellationToken ct = default)
    {
        var record = await repository.GetByCodeAsync(shortCode, ct)
            ?? throw new ShortUrlNotFoundException(shortCode);

        if (record.IsDeleted)
            throw new ShortUrlNotFoundException(shortCode);

        if (record.IsExpired(DateTimeOffset.UtcNow))
            throw new ShortUrlExpiredException(shortCode, record.ExpiresAtUtc!.Value);

        return record;
    }

    public async Task<ShortUrl?> GetMetadataAsync(string shortCode, CancellationToken ct = default)
    {
        var record = await repository.GetByCodeAsync(shortCode, ct);
        return record is { IsDeleted: false } ? record : null;
    }

    public async Task<bool> DeleteAsync(string shortCode, CancellationToken ct = default) =>
        await repository.SoftDeleteAsync(shortCode, ct);

    public Task<(IReadOnlyList<ShortUrl> Items, int TotalCount)> ListAsync(
        int page, int pageSize, CancellationToken ct = default) =>
        repository.ListAsync(page, pageSize, ct);

    private async Task<string> ResolveShortCodeAsync(string? customAlias, CancellationToken ct)
    {
        if (customAlias is not null)
        {
            if (customAlias.Length is < MinAliasLength or > MaxAliasLength ||
                !customAlias.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
            {
                throw new InvalidLongUrlException(
                    $"Custom alias must be {MinAliasLength}-{MaxAliasLength} alphanumeric characters, '-' or '_'.");
            }

            if (await repository.ExistsAsync(customAlias, ct))
                throw new AliasAlreadyInUseException(customAlias);

            return customAlias;
        }

        for (var attempt = 0; attempt < MaxGenerationAttempts; attempt++)
        {
            var candidate = codeGenerator.Generate();
            if (!await repository.ExistsAsync(candidate, ct))
                return candidate;

            logger.LogWarning("Short code collision on attempt {Attempt}: {Candidate}", attempt + 1, candidate);
        }

        throw new ShortCodeGenerationExhaustedException();
    }
}
