namespace UrlShortener.Infrastructure.Persistence;

public sealed class StorageSettings
{
    public required string ShortUrlsFilePath { get; init; }
    public required string ClickEventsFilePath { get; init; }
}
