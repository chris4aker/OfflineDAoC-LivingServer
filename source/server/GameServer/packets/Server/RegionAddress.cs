using System.Net;
using DOL.Network;

namespace DOL.GS.PacketHandler
{
    /// <summary>
    /// The address a client is told to use for a region's game (UDP) connection.
    ///
    /// Hand port from rambodolphin13/DividedRealms 6f22a7a ("Add internet mode to Host for Friends"), Living Server 1.2.
    /// Upstream sent every client one region address (or the address it connected on, for a few private prefixes only),
    /// so a friend on Tailscale (100.64/10) or 172.16/12 was told an address it could not use. The address checks moved to
    /// DOL.Network.NetworkTrust, and the choice is a pure function (<see cref="Choose"/>) so it is tested without a socket.
    /// </summary>
    public static class RegionAddress
    {
        /// <summary>
        /// A configured region address that is not reachable from outside (any, loopback, private) is replaced per client:
        /// a client from the internet gets the server's public address (PublicIP, set in serverconfig.xml or found by
        /// UPnP), any other client (this PC, the home network, Tailscale) the address it connected to.
        /// </summary>
        public static string ForClient(GameClient client, string configured)
        {
            if (!IsPlaceholder(configured))
                return configured;

            IPEndPoint local = null;
            IPEndPoint remote = null;

            try
            {
                local = client?.Socket?.LocalEndPoint as IPEndPoint;
                remote = client?.Socket?.RemoteEndPoint as IPEndPoint;
            }
            catch (System.ObjectDisposedException) { }
            catch (System.Net.Sockets.SocketException) { }

            return Choose(configured, local?.Address, remote?.Address, GameServer.Instance?.Configuration?.PublicIP);
        }

        /// <summary>
        /// The rule behind <see cref="ForClient"/>: a real (public) configured address is sent as it is; otherwise a client
        /// from the internet gets <paramref name="publicIP"/> when one is known, and every other client the address it
        /// connected to. With nothing better known, the configured value is kept.
        /// </summary>
        public static string Choose(string configured, IPAddress connectedTo, IPAddress clientAddress, IPAddress publicIP)
        {
            if (!IsPlaceholder(configured))
                return configured;

            if (publicIP != null && clientAddress != null && NetworkTrust.IsInternet(clientAddress))
                return NetworkTrust.Normalize(publicIP).ToString();

            if (connectedTo != null)
                return NetworkTrust.Normalize(connectedTo).ToString();

            return configured;
        }

        public static bool IsPlaceholder(string ip)
        {
            if (ip is "any" or "0.0.0.0")
                return true;

            return IPAddress.TryParse(ip, out IPAddress address) && !NetworkTrust.IsInternet(address);
        }

        /// <summary>True for a public internet address (see <see cref="NetworkTrust.IsInternet"/>).</summary>
        public static bool IsInternet(IPAddress address) => NetworkTrust.IsInternet(address);
    }
}
