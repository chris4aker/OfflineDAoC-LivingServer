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

                IPAddress localAddress = configuration.IP;
                Forward(configuration.UDPPort, ProtocolType.Udp, "DOL UDP", localAddress);
                Forward(configuration.Port, ProtocolType.Tcp, "DOL TCP", localAddress);

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
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error("ConfigureUpnp", e);
            }
        }

        private void Forward(int port, ProtocolType protocol, string description, IPAddress localAddress)
        {
            _gateway.ForwardPort(port, port, protocol, description, localAddress);
            lock (_sync)
                _added.Add((port, protocol));

            if (log.IsInfoEnabled)
                log.Info($"UPNP_MAPPING added {protocol.ToString().ToUpperInvariant()} {port} (removed again when the server stops)");
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
