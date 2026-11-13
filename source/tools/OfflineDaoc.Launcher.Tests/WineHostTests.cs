using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>Living Server on Linux runs the launcher under Wine (docs/LINUX.md §0): what it tells a Linux player instead.</summary>
public sealed class WineHostTests
{
    [Test]
    public void FirewallNote_ListsEveryHostingPort_ForUfwAndFirewalld()
    {
        var state = new HostForFriends.State(Hosting: true, LoginPort: 10300, UdpPort: 10400, AutoAccountCreation: true);
        string note = WineHost.FirewallNote(HostForFriends.FirewallPorts(state));
        foreach (string port in new[] { "10300/tcp", "10400/udp", "10310/tcp" })
        {
            Assert.That(note, Does.Contain("sudo ufw allow " + port));
            Assert.That(note, Does.Contain("sudo firewall-cmd --add-port=" + port));
        }
    }

    [TestCase(@"Z:\home\ryan\OfflineDAoC\game\linux/offlinedaoc", "/home/ryan/OfflineDAoC/game/linux/offlinedaoc")]
    [TestCase(@"C:\Games\OfflineDAoC\linux\offlinedaoc", "C:/Games/OfflineDAoC/linux/offlinedaoc")]
    public void LinuxPath_ShowsWinesZDriveAsTheLinuxRoot(string wine, string linux) =>
        Assert.That(WineHost.LinuxPath(wine), Is.EqualTo(linux));

    [Test]
    public void UpdateNote_NamesTheLinuxScriptsUpdateCommand() =>
        Assert.That(WineHost.UpdateNote(@"Z:\home\ryan\OfflineDAoC\game"), Does.Contain("/home/ryan/OfflineDAoC/game/linux/offlinedaoc update"));
}
