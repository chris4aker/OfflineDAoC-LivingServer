using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using DOL.Config;

namespace DOL.Network
{
    /// <summary>What the server needs from a UPnP router (a seam so tests need no router).</summary>
    public interface IUpnpGateway
    {
        bool Discover();
        IEnumerable<string> DescribeMappings();
        void ForwardPort(int internalPort, int externalPort, ProtocolType protocol, string description, IPAddress internalIp);
        void DeleteForwardingRule(int port, ProtocolType protocol);
        IPAddress GetExternalIP();

        /// <summary>This PC's address on the router's network; null when unknown (Living Server 1.2).</summary>
        IPAddress LocalAddress => null;
    }

    /// <summary>The upstream UpnpNat client behind <see cref="IUpnpGateway"/>.</summary>
    public sealed class UpnpNatGateway : IUpnpGateway
    {
        private readonly UpnpNat _nat = new();

        public bool Discover() => _nat.Discover();

        public IEnumerable<string> DescribeMappings()
        {
            foreach (UpnpNat.PortForwarding info in _nat.ListForwardedPort())
                yield return $"{info.description} - {info.externalPort} -> {info.internalIP}:{info.internalPort}({info.protocol}) ({(info.enabled ? "enabled" : "disabled")})";
        }

        public void ForwardPort(int internalPort, int externalPort, ProtocolType protocol, string description, IPAddress internalIp) =>
            _nat.ForwardPort(internalPort, externalPort, protocol, description, internalIp);

        public void DeleteForwardingRule(int port, ProtocolType protocol) => _nat.DeleteForwardingRule(port, protocol);

        public IPAddress GetExternalIP() => _nat.GetExternalIP();

        public IPAddress LocalAddress
        {
            get
            {
                try
                {
                    return _nat.LocalAddress;
                }
                catch (SocketException)
                {
                    return null;
                }
            }
        }
    }

    /// <summary>
    /// The router port mappings this server added (with EnableUPnP on), so Stop removes exactly those (Living Server
    /// 1.1.2, audit 2026-11-03). Upstream added them at every start and never removed them, so the router kept the
    /// game's ports open to the internet after the server had stopped. A mapping that was never added is never removed:
    /// one the player made by hand on the router stays.
    /// </summary>
    public sealed class UpnpPortMappings
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private readonly IUpnpGateway _gateway;
        private readonly object _sync = new();
        private readonly List<(int Port, ProtocolType Protocol)> _added = new();

        public UpnpPortMappings(IUpnpGateway gateway)
        {
            _gateway = gateway ?? throw new ArgumentNullException(nameof(gateway));
        }

        /// <summary>The mappings added and not yet removed.</summary>
        public IReadOnlyList<(int Port, ProtocolType Protocol)> Added
        {
            get { lock (_sync) return _added.ToArray(); }
        }

        /// <summary>Asks the router for the UDP and TCP ports (and the external address with DetectRegionIP). Never throws.</summary>
        /// <summary>GameServer's PassportDesk.PortOffset (CoreBase can't see GameServer; UT_PassportLock keeps them equal).</summary>
        public const int PassportDeskPortOffset = 10;

        public void Open(BaseServerConfig configuration)
        {
            try
            {
                if (!_gateway.Discover())
                    throw new Exception("[UPNP] Unable to access the UPnP Internet Gateway Device");

                if (log.IsDebugEnabled)
                {
                    log.Debug("[UPNP] Current UPnP mappings:");

                    foreach (string mapping in _gateway.DescribeMappings())
                        log.Debug($"[UPNP] {mapping}");
                }

                // Listening on every network (Host for Friends): map to this PC's address on the router's network, not
                // 0.0.0.0. Each port is asked for on its own, so one the router refuses doesn't stop the other.
                // Both from rambodolphin13/DividedRealms (6f22a7a, ec0e456), Living Server 1.2.
                IPAddress localAddress = MappingAddress(configuration.IP, _gateway.LocalAddress);
                TryForward(configuration.UDPPort, ProtocolType.Udp, $"Offline DAoC UDP {configuration.UDPPort}", localAddress);
                TryForward(configuration.Port, ProtocolType.Tcp, $"Offline DAoC TCP {configuration.Port}", localAddress);
                // The Passport Desk listens on the login port + 10 while hosting (PassportDesk.PortOffset).
                TryForward(configuration.Port + PassportDeskPortOffset, ProtocolType.Tcp,
                    $"Offline DAoC passport TCP {configuration.Port + PassportDeskPortOffset}", localAddress);

                if (configuration.DetectRegionIP)
                {
                    try
                    {
                        configuration.RegionIP = _gateway.GetExternalIP();

                        if (log.IsDebugEnabled)
                            log.Debug($"[UPNP] Found the RegionIP: {configuration.RegionIP}");
                    }
                    catch (Exception e)
                    {
                        if (log.IsErrorEnabled)
                            log.Error("[UPNP] Unable to detect the RegionIP, It is possible that no mappings exist yet", e);
                    }
                }
                else if (configuration.PublicIP == null)
                {
                    // The region address stays per client (this PC, home network, Tailscale); internet clients get this one.
                    try
                    {
                        configuration.PublicIP = _gateway.GetExternalIP();

                        if (log.IsInfoEnabled)
                            log.Info($"[UPNP] Internet address for friends: {configuration.PublicIP}");
                    }
                    catch (Exception e)
                    {
                        if (log.IsErrorEnabled)
                            log.Error("[UPNP] Unable to read the router's internet address; set PublicIP in serverconfig.xml", e);
                    }
                }
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error("ConfigureUpnp", e);
            }
        }

        /// <summary>The address a mapping points at: the configured one, or this PC's router-side address when it is 0.0.0.0.</summary>
        public static IPAddress MappingAddress(IPAddress configured, IPAddress routerSide) =>
            configured == null || configured.Equals(IPAddress.Any) ? routerSide ?? configured : configured;

        private void TryForward(int port, ProtocolType protocol, string description, IPAddress localAddress)
        {
            try
            {
                _gateway.ForwardPort(port, port, protocol, description, localAddress);
            }
            catch (Exception e)
            {
                if (log.IsWarnEnabled)
                    log.Warn($"UPNP_MAPPING the router refused {protocol.ToString().ToUpperInvariant()} {port}; forward it by hand if friends can't join.", e);
                return;
            }

            lock (_sync)
                _added.Add((port, protocol));

            if (log.IsInfoEnabled)
                log.Info($"UPNP_MAPPING added {protocol.ToString().ToUpperInvariant()} {port} to {localAddress} (removed again when the server stops)");
        }

        /// <summary>Removes every mapping <see cref="Open"/> added. Returns how many were removed. Never throws.</summary>
        public int Close()
        {
            (int Port, ProtocolType Protocol)[] added;
            lock (_sync)
            {
                added = _added.ToArray();
                _added.Clear();
            }

            int removed = 0;
            foreach (var (port, protocol) in added)
            {
                try
                {
                    _gateway.DeleteForwardingRule(port, protocol);
                    removed++;

                    if (log.IsInfoEnabled)
                        log.Info($"UPNP_MAPPING removed {protocol.ToString().ToUpperInvariant()} {port}");
                }
                catch (Exception e)
                {
                    if (log.IsWarnEnabled)
                        log.Warn($"UPNP_MAPPING could not remove {protocol.ToString().ToUpperInvariant()} {port}; remove it in the router's settings if it stays open.", e);
                }
            }

            return removed;
        }
    }
}
