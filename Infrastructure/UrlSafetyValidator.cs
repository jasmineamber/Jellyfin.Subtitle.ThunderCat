using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security;
using System.Threading;
using System.Threading.Tasks;

namespace Emby.Subtitle.ThunderCat.Infrastructure
{
    internal sealed class UrlPolicy
    {
        private readonly HashSet<string> _allowedHosts;

        public UrlPolicy(params string[] allowedHosts)
        {
            if (allowedHosts == null || allowedHosts.Length == 0)
            {
                throw new ArgumentException("At least one host must be allowed.", nameof(allowedHosts));
            }

            _allowedHosts = new HashSet<string>(allowedHosts, StringComparer.OrdinalIgnoreCase);
        }

        public bool IsHostAllowed(string host) => _allowedHosts.Contains(host);
    }

    internal static class UrlSafetyValidator
    {
        public static void ValidateSyntax(Uri uri, UrlPolicy policy)
        {
            if (uri == null)
            {
                throw new ArgumentNullException(nameof(uri));
            }

            if (!uri.IsAbsoluteUri || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            {
                throw new SecurityException("Only absolute HTTPS URLs are allowed.");
            }

            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                throw new SecurityException("URLs containing user information are not allowed.");
            }

            if (!uri.IsDefaultPort && uri.Port != 443)
            {
                throw new SecurityException("Only HTTPS port 443 is allowed.");
            }

            var host = uri.DnsSafeHost;
            if (string.IsNullOrWhiteSpace(host) || !policy.IsHostAllowed(host))
            {
                throw new SecurityException("The URL host is not allowlisted.");
            }
        }

        public static async Task ValidateAsync(Uri uri, UrlPolicy policy, CancellationToken cancellationToken)
        {
            ValidateSyntax(uri, policy);

            IPAddress[] addresses;
            if (IPAddress.TryParse(uri.DnsSafeHost, out var literalAddress))
            {
                addresses = new[] { literalAddress };
            }
            else
            {
                var lookupTask = Dns.GetHostAddressesAsync(uri.DnsSafeHost);
                var cancellationTask = Task.Delay(Timeout.Infinite, cancellationToken);
                var completedTask = await Task.WhenAny(lookupTask, cancellationTask).ConfigureAwait(false);
                if (completedTask != lookupTask)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                addresses = await lookupTask.ConfigureAwait(false);
            }

            if (addresses.Length == 0 || addresses.Any(address => !IsPublicAddress(address)))
            {
                throw new SecurityException("The URL resolves to a non-public network address.");
            }
        }

        internal static bool IsPublicAddress(IPAddress address)
        {
            if (address == null)
            {
                return false;
            }

            if (address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                var first = bytes[0];
                var second = bytes[1];

                return first != 0 &&
                       first != 10 &&
                       first != 127 &&
                       !(first == 100 && second >= 64 && second <= 127) &&
                       !(first == 169 && second == 254) &&
                       !(first == 172 && second >= 16 && second <= 31) &&
                       !(first == 192 && second == 0) &&
                       !(first == 192 && second == 168) &&
                       !(first == 198 && (second == 18 || second == 19)) &&
                       !(first == 198 && second == 51 && bytes[2] == 100) &&
                       !(first == 203 && second == 0 && bytes[2] == 113) &&
                       first < 224;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (IPAddress.IPv6Any.Equals(address) ||
                    IPAddress.IPv6Loopback.Equals(address) ||
                    address.IsIPv6LinkLocal ||
                    address.IsIPv6Multicast ||
                    address.IsIPv6SiteLocal)
                {
                    return false;
                }

                var bytes = address.GetAddressBytes();
                return (bytes[0] & 0xfe) != 0xfc;
            }

            return false;
        }
    }
}
