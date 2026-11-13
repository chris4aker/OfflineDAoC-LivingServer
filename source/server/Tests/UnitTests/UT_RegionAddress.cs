using System.Net;
using DOL.GS.PacketHandler;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// One region address per client (Living Server 1.2, play with friends). The first three tests are a hand port from
    /// rambodolphin13/DividedRealms 6f22a7a (UT_RegionAddress); the rest cover RegionAddress.Choose and two players.
    /// </summary>
    [TestFixture]
    public class UT_RegionAddress
    {
        [TestCase("8.8.8.8", true)]
        [TestCase("203.0.114.9", true)]
        [TestCase("127.0.0.1", false)]
        [TestCase("10.0.0.5", false)]
        [TestCase("172.16.0.1", false)]
        [TestCase("172.32.0.1", true)]
        [TestCase("192.168.1.20", false)]
        [TestCase("100.64.0.1", false)] // Tailscale and carrier-grade NAT
        [TestCase("100.127.255.254", false)]
        [TestCase("100.128.0.1", true)]
        [TestCase("169.254.3.4", false)]
        [TestCase("224.0.0.1", false)]
        [TestCase("0.0.0.0", false)]
        public void InternetAddresses(string address, bool internet)
        {
            Assert.That(RegionAddress.IsInternet(IPAddress.Parse(address)), Is.EqualTo(internet));
        }

        [Test]
        public void MappedIPv4IsJudgedAsIPv4()
        {
            Assert.That(RegionAddress.IsInternet(IPAddress.Parse("192.168.1.20").MapToIPv6()), Is.False);
            Assert.That(RegionAddress.IsInternet(IPAddress.Parse("8.8.8.8").MapToIPv6()), Is.True);
        }

        [TestCase("any", true)]
        [TestCase("0.0.0.0", true)]
        [TestCase("127.0.0.1", true)]
        [TestCase("192.168.1.20", true)]
        [TestCase("10.13.0.1", true)]
        [TestCase("172.20.0.1", true)] // upstream sent this one as it was
        [TestCase("100.90.1.2", true)]
        [TestCase("8.8.8.8", false)] // a configured public region address is sent as it is
        public void PlaceholderRegionAddresses(string configured, bool placeholder)
        {
            Assert.That(RegionAddress.IsPlaceholder(configured), Is.EqualTo(placeholder));
        }

        private static IPAddress A(string text) => text == null ? null : IPAddress.Parse(text);

        [Test]
        public void ThisPcKeepsLoopback_AsBeforeHosting()
        {
            // Single player: the release config (RegionIP 127.0.0.1), a client on 127.0.0.1.
            Assert.That(RegionAddress.Choose("127.0.0.1", A("127.0.0.1"), A("127.0.0.1"), null), Is.EqualTo("127.0.0.1"));
            Assert.That(RegionAddress.Choose("127.0.0.1", A("127.0.0.1"), A("127.0.0.1"), A("203.0.113.7")), Is.EqualTo("127.0.0.1"));
        }

        [Test]
        public void TwoFriendsEachGetTheAddressTheyConnectedTo()
        {
            // The host listens on 0.0.0.0. One friend comes in over Tailscale, the other over the home network.
            string tailscale = RegionAddress.Choose("127.0.0.1", A("100.90.1.2"), A("100.80.5.6"), A("203.0.113.7"));
            string home = RegionAddress.Choose("127.0.0.1", A("192.168.1.20"), A("192.168.1.31"), A("203.0.113.7"));
            string host = RegionAddress.Choose("127.0.0.1", A("127.0.0.1"), A("127.0.0.1"), A("203.0.113.7"));

            Assert.That(tailscale, Is.EqualTo("100.90.1.2"));
            Assert.That(home, Is.EqualTo("192.168.1.20"));
            Assert.That(host, Is.EqualTo("127.0.0.1"));
        }

        [Test]
        public void AFriendOnTheInternetGetsThePublicAddress_OnlyWhenOneIsKnown()
        {
            // Behind a router the server sees its own private address; the friend must be told the router's.
            Assert.That(RegionAddress.Choose("0.0.0.0", A("192.168.1.20"), A("198.51.100.4"), A("203.0.113.7")), Is.EqualTo("203.0.113.7"));
            Assert.That(RegionAddress.Choose("0.0.0.0", A("192.168.1.20"), A("198.51.100.4"), null), Is.EqualTo("192.168.1.20"));
            // Two internet friends behind one router both get it.
            Assert.That(RegionAddress.Choose("any", A("192.168.1.20"), A("198.51.100.4"), A("203.0.113.7")), Is.EqualTo("203.0.113.7"));
        }

        [Test]
        public void AConfiguredPublicAddressIsAlwaysSent()
        {
            Assert.That(RegionAddress.Choose("203.0.113.9", A("127.0.0.1"), A("127.0.0.1"), A("203.0.113.7")), Is.EqualTo("203.0.113.9"));
        }

        [Test]
        public void WithNoSocketTheConfiguredValueIsKept()
        {
            Assert.That(RegionAddress.Choose("127.0.0.1", null, null, null), Is.EqualTo("127.0.0.1"));
            Assert.That(RegionAddress.Choose("127.0.0.1", A("::ffff:192.168.1.20"), null, null), Is.EqualTo("192.168.1.20"));
        }
    }
}
