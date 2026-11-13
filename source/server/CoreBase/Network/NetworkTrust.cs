// Also compiled into the launcher (Host for Friends and Join Friend), which has nullable annotations on.
#nullable disable

using System.Net;
using System.Net.Sockets;

namespace DOL.Network
{
    /// <summary>
    /// Where a client's address is, for play with friends (Living Server 1.2).
    ///
    /// - <see cref="IsInternet"/>: a public internet address. Ported from rambodolphin13/DividedRealms 6f22a7a
    ///   (RegionAddress.IsInternet and HostForFriends.IsInternet), shared here by the server and the launcher.
    /// - <see cref="IsTrusted"/>: this PC, the home network (10/8, 172.16/12, 192.168/16) or Tailscale (100.64/10).
    ///   The server treats these like this PC for idle time-outs and the same-address account rule. A public address
    ///   is never trusted.
    /// </summary>
    public static class NetworkTrust
    {
        /// <summary>True for a public internet address; false for loopback, private, link-local and shared (CGNAT, Tailscale) ones.</summary>
        public static bool IsInternet(IPAddress address)
        {
            if (address == null)
                return false;

            address = Normalize(address);

            if (address.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(address))
                return false;

            byte[] b = address.GetAddressBytes();
            return !(b[0] == 0
                || b[0] == 10
                || (b[0] == 100 && b[1] >= 64 && b[1] <= 127)
                || (b[0] == 169 && b[1] == 254)
                || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                || (b[0] == 192 && b[1] == 168)
                || b[0] >= 224);
        }

        /// <summary>Tailscale's addresses (100.64.0.0/10, shared with carrier-grade NAT).</summary>
        public static bool IsTailscale(IPAddress address)
        {
            if (address == null)
                return false;

            address = Normalize(address);

            if (address.AddressFamily != AddressFamily.InterNetwork)
                return false;

            byte[] b = address.GetAddressBytes();
            return b[0] == 100 && b[1] >= 64 && b[1] <= 127;
        }

        /// <summary>A private home-network address: 10/8, 172.16/12 or 192.168/16.</summary>
        public static bool IsHomeNetwork(IPAddress address)
        {
            if (address == null)
                return false;

            address = Normalize(address);

            if (address.AddressFamily != AddressFamily.InterNetwork)
                return false;

            byte[] b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
        }

        /// <summary>This PC, the home network or Tailscale. Anything else (a public address, IPv6 other than ::1) is not trusted.</summary>
        public static bool IsTrusted(IPAddress address)
        {
            if (address == null)
                return false;

            address = Normalize(address);
            return IPAddress.IsLoopback(address) || IsHomeNetwork(address) || IsTailscale(address);
        }

        /// <summary>The same, for an address written as text (the login handler's form). Unreadable text is not trusted.</summary>
        public static bool IsTrusted(string address) =>
            IPAddress.TryParse(address, out IPAddress parsed) && IsTrusted(parsed);

        public static IPAddress Normalize(IPAddress address) =>
            address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }
}
