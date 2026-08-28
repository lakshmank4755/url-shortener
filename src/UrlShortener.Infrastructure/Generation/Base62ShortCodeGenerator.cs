using System.Security.Cryptography;
using UrlShortener.Core.Abstractions;

namespace UrlShortener.Infrastructure.Generation;

/// <summary>
/// Generates 7-character Base62 codes using a CSPRNG (not Guid/timestamp
/// based) so that codes are neither predictable nor sequentially enumerable.
/// 62^7 (~3.5 trillion) keyspace keeps collision probability negligible at
/// the scale this prototype targets; the repository layer still checks for
/// collisions defensively rather than trusting the keyspace math alone.
/// </summary>
public sealed class Base62ShortCodeGenerator : IShortCodeGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
    private const int Length = 7;

    public string Generate()
    {
        Span<char> buffer = stackalloc char[Length];
        for (var i = 0; i < Length; i++)
        {
            buffer[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        }
        return new string(buffer);
    }
}
