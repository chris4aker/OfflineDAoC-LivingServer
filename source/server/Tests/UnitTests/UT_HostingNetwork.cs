using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using DOL.GS;
using DOL.Network;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2, play with friends: which addresses are trusted, the idle time-outs for friends, the router
    /// mappings while hosting and the PublicIP setting. No server or socket is started.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_HostingNetwork
    {
        [TestCase("127.0.0.1", true)]
        [TestCase("::1", true)]
        [TestCase("10.0.0.5", true)]
        [TestCase("172.16.0.1", true)]
        [TestCase("172.31.255.254", true)]
        [TestCase("192.168.1.20", true)]
        [TestCase("100.64.0.1", true)] // Tailscale
        [TestCase("100.127.255.254", true)]
        [TestCase("172.32.0.1", false)]
        [TestCase("100.128.0.1", false)]
        [TestCase("203.0.113.25", false)]
        [TestCase("8.8.8.8", false)]
        [TestCase("169.254.3.4", false)]
        [TestCase("2001:db8::1", false)]
        [TestCase("not an address", false)]
        public void TrustedAddresses(string address, bool trusted)
        {
            Assert.That(NetworkTrust.IsTrusted(address), Is.EqualTo(trusted));
        }

        [Test]
        public void MappedAddressesAreJudgedAsIPv4()
        {
            Assert.That(NetworkTrust.IsTrusted(IPAddress.Parse("192.168.1.20").MapToIPv6()), Is.True);
            Assert.That(NetworkTrust.IsTrusted(IPAddress.Parse("203.0.113.25").MapToIPv6()), Is.False);
            Assert.That(NetworkTrust.IsTrusted((IPAddress) null), Is.False);
        }

        [Test]
        public void ThisPcKeepsItsExemptions_SinglePlayerIsUnchanged()
        {
            Assert.That(ClientTimeoutPolicy.SkipsConnectionTimeouts(loopback: true, trustedNetwork: true), Is.True);
            Assert.That(ClientTimeoutPolicy.SkipsIdleKick(loopback: true, trustedNetwork: true), Is.True);
        }

        [Test]
        public void TwoFriendsOnTheHomeNetworkAndTailscaleAreTreatedLikeThisPc()
        {
            bool homeFriend = NetworkTrust.IsTrusted("192.168.1.31");
            bool tailscaleFriend = NetworkTrust.IsTrusted("100.80.5.6");

            Assert.That(ClientTimeoutPolicy.SkipsConnectionTimeouts(false, homeFriend), Is.True);
            Assert.That(ClientTimeoutPolicy.SkipsConnectionTimeouts(false, tailscaleFriend), Is.True);
            Assert.That(ClientTimeoutPolicy.SkipsIdleKick(false, homeFriend), Is.True);
            Assert.That(ClientTimeoutPolicy.SkipsIdleKick(false, tailscaleFriend), Is.True);
        }

        [Test]
        public void AFriendFromAPublicAddressKeepsEveryTimeout()
        {
            bool internetFriend = NetworkTrust.IsTrusted("203.0.113.25");

            Assert.That(ClientTimeoutPolicy.SkipsConnectionTimeouts(false, internetFriend), Is.False);
            Assert.That(ClientTimeoutPolicy.SkipsIdleKick(false, internetFriend), Is.False);
            Assert.That(ClientTimeoutPolicy.SkipsConnectionTimeouts((GameClient) null), Is.False);
        }

        [Test]
        public void HostingMapsTheRouterToThisPcsAddress_NotToEveryNetwork()
        {
            var gateway = new FakeGateway { Local = IPAddress.Parse("192.168.1.20") };
            var config = new GameServerConfiguration { IP = IPAddress.Any, UDPIP = IPAddress.Any, EnableUPnP = true };
            var mappings = new UpnpPortMappings(gateway);
            mappings.Open(config);

            Assert.That(gateway.Targets.Distinct(), Is.EqualTo(new[] { IPAddress.Parse("192.168.1.20") }));
            Assert.That(mappings.Close(), Is.EqualTo(3), "the game's two ports and the Passport Desk's");
            Assert.That(gateway.Open, Is.Empty, "every port is closed again at stop");

            Assert.That(UpnpPortMappings.MappingAddress(IPAddress.Parse("10.0.0.5"), IPAddress.Parse("192.168.1.20")), Is.EqualTo(IPAddress.Parse("10.0.0.5")));
            Assert.That(UpnpPortMappings.MappingAddress(IPAddress.Any, null), Is.EqualTo(IPAddress.Any));
        }

        [Test]
        public void ARefusedPortDoesNotStopTheOther()
        {
            // UDP is asked for first; a refusal there used to end the setup before TCP (rambodolphin13 ec0e456).
            var gateway = new FakeGateway { RefuseUdp = true };
            var mappings = new UpnpPortMappings(gateway);
            mappings.Open(new GameServerConfiguration { EnableUPnP = true });

            Assert.That(mappings.Added, Is.EqualTo(new[] { (10300, ProtocolType.Tcp), (10310, ProtocolType.Tcp) }));
            Assert.That(mappings.Close(), Is.EqualTo(2));
        }

        [Test]
        public void TheRoutersAddressBecomesThePublicIP_WithoutTouchingTheRegionAddress()
        {
            var config = new GameServerConfiguration { IP = IPAddress.Any, UDPIP = IPAddress.Any, EnableUPnP = true };
            new UpnpPortMappings(new FakeGateway()).Open(config);

            Assert.That(config.PublicIP, Is.EqualTo(IPAddress.Parse("203.0.113.7")));
            Assert.That(config.RegionIP, Is.EqualTo(IPAddress.Loopback), "each client still gets its own address");

            var manual = new GameServerConfiguration { EnableUPnP = true, PublicIP = IPAddress.Parse("198.51.100.9") };
            new UpnpPortMappings(new FakeGateway()).Open(manual);
            Assert.That(manual.PublicIP, Is.EqualTo(IPAddress.Parse("198.51.100.9")), "a PublicIP set by hand is kept");
        }

        [TestCase("auto", null)]
        [TestCase("", null)]
        [TestCase("garbage", null)]
        [TestCase("203.0.113.7", "203.0.113.7")]
        public void PublicIPSetting(string value, string expected)
        {
            string folder = Path.Combine(Path.GetTempPath(), "ls-hosting-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var file = new FileInfo(Path.Combine(folder, "serverconfig.xml"));
                File.WriteAllText(file.FullName, $"<?xml version=\"1.0\" encoding=\"utf-8\"?><root><Server><PublicIP>{value}</PublicIP></Server></root>");
                var config = new GameServerConfiguration();
                config.LoadFromXMLFile(file);
                Assert.That(config.PublicIP?.ToString(), Is.EqualTo(expected));
                Assert.That(config.IP, Is.EqualTo(IPAddress.Loopback), "nothing else changes");
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        [Test]
        public void TheHostPasswordHashIsReadAndWritten_AndEmptyByDefault()
        {
            string folder = Path.Combine(Path.GetTempPath(), "ls-hosting-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var file = new FileInfo(Path.Combine(folder, "serverconfig.xml"));
                var config = new GameServerConfiguration();
                Assert.That(config.HostPasswordHash, Is.Empty);
                Assert.That(config.PublicIP, Is.Null);
                config.SaveToXMLFile(file);
                Assert.That(File.ReadAllText(file.FullName), Does.Contain("<PublicIP>auto</PublicIP>"));

                File.WriteAllText(file.FullName, "<?xml version=\"1.0\" encoding=\"utf-8\"?><root><Server><HostPasswordHash>abc</HostPasswordHash></Server></root>");
                var loaded = new GameServerConfiguration();
                // A fresh FileInfo: the first one cached "does not exist" from before the save.
                loaded.LoadFromXMLFile(new FileInfo(file.FullName));
                Assert.That(loaded.HostPasswordHash, Is.EqualTo("abc"));
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }

        private sealed class FakeGateway : IUpnpGateway
        {
            public bool RefuseUdp;
            public IPAddress Local;
            public readonly List<(int, ProtocolType)> Open = new();
            public readonly List<IPAddress> Targets = new();
            public bool Discover() => true;
            public IEnumerable<string> DescribeMappings() => Open.Select(o => o.ToString());

            public void ForwardPort(int internalPort, int externalPort, ProtocolType protocol, string description, IPAddress internalIp)
            {
                if (RefuseUdp && protocol == ProtocolType.Udp)
                    throw new InvalidOperationException("refused");
                Open.Add((externalPort, protocol));
                Targets.Add(internalIp);
            }

            public void DeleteForwardingRule(int port, ProtocolType protocol) => Open.Remove((port, protocol));
            public IPAddress GetExternalIP() => IPAddress.Parse("203.0.113.7");
            public IPAddress LocalAddress => Local;
        }
    }
}
