using System.Net;
using UrlShortener.Core.Abstractions;
using UrlShortener.Core.Models;

namespace UrlShortener.Infrastructure.Validation;

/// <summary>
/// Concrete rules for "is this URL safe to accept and redirect users to".
/// Added in the ambiguous-requirement scenario (docs/04) after normalizing
/// a vague "harden this before launch" ask into five concrete checks. Each
/// rule below maps 1:1 to a risk named in that doc — do not add checks here
/// without a corresponding entry there, and vice versa.
/// </summary>
public sealed class UrlSafetyValidator(UrlSafetyOptions options) : IUrlSafetyValidator
{
    public UrlValidationResult Validate(string longUrl)
    {
        if (string.IsNullOrWhiteSpace(longUrl))
            return UrlValidationResult.Invalid("URL must not be empty.");

        if (longUrl.Length > options.MaxUrlLength)
            return UrlValidationResult.Invalid($"URL exceeds the maximum length of {options.MaxUrlLength} characters.");

        if (!Uri.TryCreate(longUrl, UriKind.Absolute, out var uri))
            return UrlValidationResult.Invalid("URL must be a well-formed absolute URI.");

        // Rule: only http/https. Blocks javascript:, data:, file:, ftp:, etc.
        // which have no legitimate use as a redirect target and are common
        // XSS/local-file-disclosure vectors when a browser follows them.
        if (uri.Scheme is not ("http" or "https"))
            return UrlValidationResult.Invalid($"Scheme '{uri.Scheme}' is not allowed; only http/https are permitted.");

        // Rule: block loopback/private/link-local targets to prevent this
        // service being used as an SSRF proxy against internal infrastructure
        // (e.g. shortening a link to http://169.254.169.254/ cloud metadata,
        // or http://10.x.x.x/ internal admin panels).
        if (IsDisallowedNetworkTarget(uri.Host))
            return UrlValidationResult.Invalid("URLs targeting private, loopback, or link-local addresses are not allowed.");

        // Rule: don't allow shortening our own short-link domain — prevents
        // redirect chains/loops (a shortened link that points at another
        // shortened link on the same service).
        if (options.OwnHostNames.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return UrlValidationResult.Invalid("Shortening a URL that points back at this service is not allowed.");

        // Rule: static blocklist, standing in for a real threat-intel feed
        // (e.g. Google Safe Browsing / a security vendor API) that a
        // production deployment would call here instead.
        if (options.BlockedHostNames.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            return UrlValidationResult.Invalid("This destination has been flagged and cannot be shortened.");

        return UrlValidationResult.Valid();
    }

    private static bool IsDisallowedNetworkTarget(string host)
    {
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        if (!IPAddress.TryParse(host, out var ip))
            return false; // a DNS name we can't resolve here; DNS-rebinding
                           // protection would happen at the egress proxy in
                           // production (see docs/01 limitations).

        if (IPAddress.IsLoopback(ip)) return true;

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            return bytes[0] switch
            {
                10 => true,                                   // 10.0.0.0/8
                127 => true,                                   // 127.0.0.0/8
                169 when bytes[1] == 254 => true,               // 169.254.0.0/16 (link-local/cloud metadata)
                172 when bytes[1] is >= 16 and <= 31 => true,   // 172.16.0.0/12
                192 when bytes[1] == 168 => true,               // 192.168.0.0/16
                0 => true,                                      // 0.0.0.0/8
                _ => false,
            };
        }

        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal;
    }
}
