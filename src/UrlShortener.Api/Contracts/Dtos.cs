using System.ComponentModel.DataAnnotations;

namespace UrlShortener.Api.Contracts;

public sealed class CreateShortUrlRequest
{
    [Required]
    public required string LongUrl { get; init; }

    [StringLength(32, MinimumLength = 3)]
    public string? CustomAlias { get; init; }

    public DateTimeOffset? ExpiresAtUtc { get; init; }
}

public sealed class ShortUrlResponse
{
    public required string ShortCode { get; init; }
    public required string ShortUrl { get; init; }
    public required string LongUrl { get; init; }
    public required DateTimeOffset CreatedAtUtc { get; init; }
    public DateTimeOffset? ExpiresAtUtc { get; init; }
    public required bool IsCustomAlias { get; init; }
}

public sealed class PagedResponse<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required int TotalCount { get; init; }
}

public sealed class ClickAnalyticsResponse
{
    public required string ShortCode { get; init; }
    public required long TotalClicks { get; init; }
    public DateTimeOffset? LastClickedAtUtc { get; init; }
    public required IReadOnlyList<DailyClickCountDto> ClicksByDay { get; init; }
    public required IReadOnlyDictionary<string, long> ClicksByDevice { get; init; }
}

public sealed record DailyClickCountDto(DateOnly Date, long Count);

public sealed class ProblemDetailsResponse
{
    public required string Title { get; init; }
    public required int Status { get; init; }
    public string? Detail { get; init; }
    public string? TraceId { get; init; }
}
