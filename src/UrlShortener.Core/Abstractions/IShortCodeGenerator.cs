namespace UrlShortener.Core.Abstractions;

public interface IShortCodeGenerator
{
    /// <summary>Generates a random, URL-safe short code candidate. Callers
    /// are responsible for checking collisions and retrying — the generator
    /// itself has no knowledge of what codes already exist.</summary>
    string Generate();
}
