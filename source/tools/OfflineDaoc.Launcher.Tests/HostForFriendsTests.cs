#nullable enable
using System.Net;
using System.Xml.Linq;
using DOL.GS.PacketHandler.Client.v168;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

// Hand port from rambodolphin13/DividedRealms (HostForFriendsTests at 3c6cc9d, 6f22a7a, af1fcf8, b0744a2), Living Server
// 1.2: no Passport Desk port, a host password is always needed, and internet mode is chosen at START HOSTING.
public sealed class HostForFriendsTests
{
    private string _runtime = string.Empty;
    private string Config => HostForFriends.ConfigPath(_runtime);

    // A shipped serverconfig.xml, trimmed: loopback only, UPnP and region-IP detection off.
    private const string Shipped = """
        <?xml version="1.0" encoding="utf-8"?>
        <root>
          <Server>
            <Port>10300</Port>
            <IP>127.0.0.1</IP>
            <RegionIP>127.0.0.1</RegionIP>
            <RegionPort>10400</RegionPort>
            <UdpIP>127.0.0.1</UdpIP>
            <UdpPort>10400</UdpPort>
            <EnableUPnP>False</EnableUPnP>
            <DetectRegionIP>False</DetectRegionIP>
            <ServerName>Offline DAoC</ServerName>
            <DBType>SQLITE</DBType>
            <DBConnectionString>Data Source=../data/opendaoc.sqlite3.db</DBConnectionString>
          </Server>
        </root>
        """;

    [SetUp]
    public void SetUp()
    {
        _runtime = Path.Combine(Path.GetTempPath(), "odc-host-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.GetDirectoryName(Config)!);
        File.WriteAllText(Config, Shipped);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_runtime))
            Directory.Delete(_runtime, true);
    }

    private string Value(string name) => XDocument.Load(Config).Root!.Element("Server")!.Element(name)!.Value;

    [Test]
    public void ShippedConfigIsNotHosting()
    {
        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting, Is.False);
        Assert.That(state.Internet, Is.False);
        Assert.That(state.PasswordSet, Is.False);
        Assert.That(state.LoginPort, Is.EqualTo(10300));
        Assert.That(state.UdpPort, Is.EqualTo(10400));
    }

    [Test]
    public void EnableListensEverywhereButKeepsLoopbackRegionAndRouterClosed()
    {
        HostForFriends.Enable(_runtime, "swordfish");

        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting && state.PasswordSet, Is.True);
        Assert.That(state.Internet, Is.False, "internet mode is opt-in");
        Assert.That(Value("IP"), Is.EqualTo("0.0.0.0"));
        Assert.That(Value("UdpIP"), Is.EqualTo("0.0.0.0"));
        // A loopback RegionIP makes the server answer each client with the address it connected to.
        Assert.That(Value("RegionIP"), Is.EqualTo("127.0.0.1"));
        Assert.That(Value("EnableUPnP"), Is.EqualTo("False"));
        Assert.That(Value("DetectRegionIP"), Is.EqualTo("False"));
        Assert.That(Value("AutoAccountCreation"), Is.EqualTo("True"));
        Assert.That(Value("ServerName"), Is.EqualTo("Offline DAoC"));
        Assert.That(Value("DBConnectionString"), Is.EqualTo("Data Source=../data/opendaoc.sqlite3.db"));
        Assert.That(XDocument.Load(Config).Root!.Element("Server")!.Element("PublicIP"), Is.Null);
        Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Config)!, HostForFriends.BackupFileName)), Is.EqualTo(Shipped));
    }

    [TestCase("")]
    [TestCase("short")]
    [TestCase("abcdefghijklmnopqrstu")]
    public void HostingNeedsAPassword(string password)
    {
        Assert.Throws<ArgumentException>(() => HostForFriends.Enable(_runtime, password));
        Assert.That(File.ReadAllText(Config), Is.EqualTo(Shipped), "nothing changed");
        Assert.That(HostForFriends.Read(_runtime).Hosting, Is.False);
    }

    [Test]
    public void EnablingTwiceKeepsTheOriginalBackup()
    {
        HostForFriends.Enable(_runtime, "swordfish");
        HostForFriends.Enable(_runtime, "swordfish2");
        Assert.That(File.ReadAllText(Path.Combine(Path.GetDirectoryName(Config)!, HostForFriends.BackupFileName)), Is.EqualTo(Shipped));
        Assert.That(Value("HostPasswordHash"), Is.EqualTo(HostForFriends.HashHostPassword("swordfish2")));
    }

    [Test]
    public void DisableReturnsToLoopbackAndClearsThePassword()
    {
        HostForFriends.Enable(_runtime, "swordfish", internet: true);
        HostForFriends.Disable(_runtime);

        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting || state.Internet || state.PasswordSet, Is.False);
        Assert.That(Value("IP"), Is.EqualTo("127.0.0.1"));
        Assert.That(Value("UdpIP"), Is.EqualTo("127.0.0.1"));
        Assert.That(Value("EnableUPnP"), Is.EqualTo("False"));
        Assert.That(Value("Port"), Is.EqualTo("10300"));
    }

    [Test]
    public void FirewallRulesAreLimitedToTheHomeNetworkTailscaleAndThisServer()
    {
        var state = HostForFriends.Read(_runtime);
        string add = HostForFriends.FirewallScript(_runtime, state, add: true);
        string program = Path.Combine(_runtime, "server", "CoreServer.exe");

        Assert.That(add, Does.Contain("protocol=TCP localport=10300 remoteip=LocalSubnet,100.64.0.0/10"));
        Assert.That(add, Does.Contain("protocol=UDP localport=10400 remoteip=LocalSubnet,100.64.0.0/10"));
        Assert.That(add, Does.Contain($"program=\"{program}\""));
        Assert.That(add.Split('\n').Count(line => line.Contains("add rule")), Is.EqualTo(2), "the game's two ports only");
        Assert.That(HostForFriends.FirewallRuleNames(state), Is.EqualTo(new[] { "Offline DAoC friends TCP 10300", "Offline DAoC friends UDP 10400" }));

        string remove = HostForFriends.FirewallScript(_runtime, state, add: false);
        Assert.That(remove, Does.Not.Contain("add rule"));
        Assert.That(remove, Does.Contain("delete rule name=\"Offline DAoC friends TCP 10300\""));
        Assert.That(remove, Does.Contain("delete rule name=\"Offline DAoC friends UDP 10400\""));
    }

    [Test]
    public void InternetFirewallAdmitsAnyAddress()
    {
        var state = HostForFriends.Read(_runtime);
        string add = HostForFriends.FirewallScript(_runtime, state, add: true, openToInternet: true);
        Assert.That(add, Does.Contain("protocol=TCP localport=10300 remoteip=any "));
        Assert.That(add, Does.Contain("protocol=UDP localport=10400 remoteip=any "));
    }

    [TestCase("100.64.0.1", true)]
    [TestCase("100.100.100.100", true)]
    [TestCase("192.168.1.2", false)]
    public void TailscaleRange(string address, bool tailscale) =>
        Assert.That(HostForFriends.IsTailscale(IPAddress.Parse(address)), Is.EqualTo(tailscale));

    private static HostForFriends.LocalAddress Local(string address, bool gateway = true) => new(IPAddress.Parse(address), gateway);

    [Test]
    public void InviteCarriesTailscaleThenInternetThenHome()
    {
        var local = new[] { Local("192.168.1.20"), Local("100.90.1.2", gateway: false), Local("172.20.0.1", gateway: false) };
        var addresses = HostForFriends.InviteAddresses(IPAddress.Parse("203.0.113.7"), local);
        Assert.That(addresses.Select(a => a.ToString()), Is.EqualTo(new[] { "100.90.1.2", "203.0.113.7", "192.168.1.20" }));
    }

    [Test]
    public void WithoutInternetModeTheInviteHoldsNoInternetAddress()
    {
        var local = new[] { Local("192.168.1.20"), Local("100.90.1.2", gateway: false) };
        var addresses = HostForFriends.InviteAddresses(null, local);
        Assert.That(addresses.Select(a => a.ToString()), Is.EqualTo(new[] { "100.90.1.2", "192.168.1.20" }));
        Assert.That(addresses.Any(HostForFriends.IsInternet), Is.False);
    }

    [Test]
    public void PcOnTheInternetDirectlyIsItsOwnInternetAddress()
    {
        // Modem in bridge mode: no router to answer UPnP and no home address, which used to give no invite code.
        var local = new[] { Local("203.0.113.7"), Local("172.20.0.1", gateway: false) };
        IPAddress? direct = HostForFriends.DirectInternetAddress(local);
        Assert.That(direct?.ToString(), Is.EqualTo("203.0.113.7"));
        Assert.That(HostForFriends.InviteAddresses(direct, local).First().ToString(), Is.EqualTo("203.0.113.7"));
    }

    [Test]
    public void HomeAddressWithoutAGatewayBeatsNone()
    {
        Assert.That(HostForFriends.InviteAddresses(null, [Local("192.168.1.20", gateway: false)]).Select(a => a.ToString()),
            Is.EqualTo(new[] { "192.168.1.20" }));
        Assert.That(HostForFriends.InviteAddresses(null, [Local("172.20.0.1", gateway: false), Local("10.0.0.5")]).Single().ToString(),
            Is.EqualTo("10.0.0.5"));
        Assert.That(HostForFriends.InviteAddresses(null, [Local("169.254.3.4")]), Is.Empty);
    }

    [Test]
    public void InternetModeOpensTheRouterWithoutChangingTheRegionAddress()
    {
        HostForFriends.Enable(_runtime, "swordfish", internet: true);

        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting && state.Internet && state.PasswordSet, Is.True);
        Assert.That(Value("EnableUPnP"), Is.EqualTo("True"));
        Assert.That(Value("DetectRegionIP"), Is.EqualTo("False"));
        Assert.That(Value("RegionIP"), Is.EqualTo("127.0.0.1"));
        Assert.That(Value("PublicIP"), Is.EqualTo("auto"));
        Assert.That(HostForFriends.ManualPublicAddress(_runtime), Is.Null, "auto is not an address");
    }

    [Test]
    public void TurningInternetModeOffKeepsHostingAtHome()
    {
        HostForFriends.Enable(_runtime, "swordfish", internet: true);
        HostForFriends.Enable(_runtime, "swordfish", internet: false);

        var state = HostForFriends.Read(_runtime);
        Assert.That(state.Hosting, Is.True);
        Assert.That(state.Internet, Is.False);
        Assert.That(Value("EnableUPnP"), Is.EqualTo("False"));
    }

    [Test]
    public void ManualPublicAddressIsKept()
    {
        var document = XDocument.Load(Config);
        document.Root!.Element("Server")!.Add(new XElement("PublicIP", "203.0.113.7"));
        document.Save(Config);

        HostForFriends.Enable(_runtime, "swordfish", internet: true);
        Assert.That(Value("PublicIP"), Is.EqualTo("203.0.113.7"));
        Assert.That(HostForFriends.ManualPublicAddress(_runtime), Is.EqualTo(IPAddress.Parse("203.0.113.7")));
    }

    [Test]
    public void FirewallSettingRoundTrips()
    {
        HostForFriends.SaveSettings(_runtime, new HostForFriends.Settings { FirewallOpenToInternet = true });
        Assert.That(HostForFriends.LoadSettings(_runtime).FirewallOpenToInternet, Is.True);

        File.WriteAllText(HostForFriends.SettingsPath(_runtime), "{ broken");
        Assert.That(HostForFriends.LoadSettings(_runtime).FirewallOpenToInternet, Is.False);
    }

    [Test]
    public void HostPasswordIsStoredOnlyAsAHash()
    {
        HostForFriends.Enable(_runtime, "swordfish");

        Assert.That(Value("HostPasswordHash"), Is.EqualTo(HostForFriends.HashHostPassword("swordfish")));
        Assert.That(File.ReadAllText(Config), Does.Not.Contain("swordfish"));
    }

    // The server's HostPassword.Hash has this same value in UT_HostPassword (and the fork's, so hashes carry over).
    [Test]
    public void HostPasswordHashMatchesTheServer()
    {
        Assert.That(HostForFriends.HashHostPassword("swordfish"), Is.EqualTo("4a9e1d7e2bb770e3f89110ee8d09517a3a4dbd6f4165881d3a79c6d3ef9d0273"));
        Assert.That(HostForFriends.HashHostPassword("swordfish"), Is.EqualTo(HostPassword.Hash("swordfish")));
    }

    [Test]
    public void TheHashTheLauncherWritesLetsTwoFriendsInAndNobodyElse()
    {
        HostForFriends.Enable(_runtime, "swordfish");
        string stored = Value("HostPasswordHash");

        // Two friends behind one router (same address), and a third on Tailscale, each with an own password.
        Assert.That(HostPassword.AllowsNewAccount(stored, HostPassword.Combine("swordfish", "aaron-pw"), "192.168.1.30"), Is.True);
        Assert.That(HostPassword.AllowsNewAccount(stored, HostPassword.Combine("swordfish", "bea-pw"), "192.168.1.30"), Is.True);
        Assert.That(HostPassword.AllowsNewAccount(stored, HostPassword.Combine("swordfish", "cai-pw"), "100.90.1.2"), Is.True);
        Assert.That(HostPassword.AllowsNewAccount(stored, "swordfish", "192.168.1.30"), Is.False, "the host password alone makes no account");
        Assert.That(HostPassword.AllowsNewAccount(stored, HostPassword.Combine("guess", "pw"), "203.0.113.25"), Is.False);
        Assert.That(HostPassword.AllowsNewAccount(stored, "anything", "127.0.0.1"), Is.True, "this PC is never asked");
    }

    [TestCase("", false)]
    [TestCase("short", false)]
    [TestCase("swordfish", true)]
    [TestCase("abcdefghijklmnopqrst", true)]
    [TestCase("abcdefghijklmnopqrstu", false)]
    [TestCase("swördfish", false)]
    public void HostPasswordRules(string password, bool valid) =>
        Assert.That(HostForFriends.TryCheckHostPassword(password, out _), Is.EqualTo(valid));

    [Test]
    public void StopHostingRemovesOnlyTheRulesThisLauncherNamed()
    {
        var state = HostForFriends.Read(_runtime);
        var settings = new HostForFriends.Settings
        {
            FirewallRules = ["Offline DAoC friends TCP 10301", "Someone else's rule", "Offline DAoC friends UDP 10400"],
        };

        IReadOnlyList<string> rules = HostForFriends.RulesToRemove(state, settings);
        Assert.That(rules, Is.EquivalentTo(new[] { "Offline DAoC friends TCP 10301", "Offline DAoC friends UDP 10400", "Offline DAoC friends TCP 10300" }));

        string script = HostForFriends.FirewallRemoveScript(rules.Append("Someone else's rule").Append("Offline DAoC friends \" & del"));
        Assert.That(script, Does.Not.Contain("Someone else"));
        Assert.That(script, Does.Not.Contain("& del"));
        Assert.That(script, Does.Not.Contain("add rule"));
        Assert.That(script, Does.Contain("delete rule name=\"Offline DAoC friends TCP 10300\""));
        Assert.That(script.Split('\n').Count(line => line.Contains("delete rule")), Is.EqualTo(3));
        Assert.That(script, Does.Contain("exit /b %failed%"));
    }

    [Test]
    public void TheRuleNamesStartHostingMadeAreRemembered()
    {
        HostForFriends.SaveSettings(_runtime, new HostForFriends.Settings { FirewallRules = ["Offline DAoC friends TCP 10300"] });
        Assert.That(HostForFriends.LoadSettings(_runtime).FirewallRules, Is.EqualTo(new[] { "Offline DAoC friends TCP 10300" }));
        File.WriteAllText(HostForFriends.SettingsPath(_runtime), "{\"FirewallOpenToInternet\":true}");
        Assert.That(HostForFriends.LoadSettings(_runtime).FirewallRules, Is.Empty, "older settings files have no names");
    }
}

