using System.Security.Cryptography;
using System.Text;

namespace UrlShortener.Api.Contracts;

/// <summary>
/// We never persist a caller's raw IP address (data-minimization: we don't
/// need it, only a stable-but-anonymized correlation handle for abuse
/// investigation). A per-process-random salt means the hash can't be
/// reversed or correlated across a service restart or by an outside party
/// who doesn't have the salt.
/// </summary>
public static class ClientHasher
{
    private static readonly byte[] Salt = RandomNumberGenerator.GetBytes(16);

    public static string HashClientIp(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var bytes = Encoding.UTF8.GetBytes(ip);
        var combined = new byte[Salt.Length + bytes.Length];
        Salt.CopyTo(combined, 0);
        bytes.CopyTo(combined, Salt.Length);
        return Convert.ToHexString(SHA256.HashData(combined))[..16];
    }
}
