using System;
using System.Net;

namespace TiaMcpServer
{
    /// <summary>
    /// Security decisions for the HTTP transport, kept free of HttpListener so they are easy to reason
    /// about (and to pin from the offline suite). This endpoint can download to a PLC and delete blocks,
    /// so "who may call it" is not a detail.
    /// Ported from upstream bulaofen0036-coder/TIA_Portal_Openness_MCP (commit 53731356).
    /// </summary>
    internal static class HttpSecurity
    {
        /// <summary>Browsers attach Origin to cross-site requests; a page on any other site must not be
        /// able to drive this server (a text/plain POST needs no CORS preflight). Clients that are not
        /// browsers send no Origin and are allowed.</summary>
        public static bool IsAllowedOrigin(string? origin)
        {
            if (string.IsNullOrEmpty(origin)) return true;
            // Sandboxed frames and file:// pages send the literal "null", which we do not trust.
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri)) return false;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;
            return IsLoopbackHost(uri.Host);
        }

        public static bool IsLoopbackHost(string? host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            string h = host!.Trim('[', ']');
            if (string.Equals(h, "localhost", StringComparison.OrdinalIgnoreCase)) return true;
            return IPAddress.TryParse(h, out var ip) && IPAddress.IsLoopback(ip);
        }
    }
}
