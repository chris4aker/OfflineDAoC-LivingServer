#nullable enable
using System.Reflection;
using NUnit.Framework;

namespace DAoCLivingServer.Launcher.Tests;

// Issue #641: the launcher's About shows CREDITS.md, embedded at build time. It keeps the GPL v3 license, the upstream
// attribution (Dawn of Light, OpenDAoC, the original Offline DAoC) and the "not affiliated" notice.
[TestFixture]
public class AboutCredits641Tests
{
    private static readonly Type About = Assembly.Load("DAoCLivingServer").GetType("DAoCLivingServer.Launcher.AboutForm")!;

    private static string Call(string method, params object[] args) =>
        (string)About.GetMethod(method, BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, args)!;

    [Test]
    public void TheEmbeddedCreditsAreTheRepositorysCreditsFile()
    {
        string repo = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "..", "CREDITS.md"));
        Assert.That(Call("CreditsMarkdown").Replace("\r\n", "\n"), Is.EqualTo(File.ReadAllText(repo).Replace("\r\n", "\n")));
    }

    [Test]
    public void AboutKeepsTheLicenseTheUpstreamNoticesAndTheDisclaimer()
    {
        string text = Call("PlainText", Call("CreditsMarkdown"));
        foreach (string needed in new[]
                 {
                     "DAoC - Living Server", "GNU General Public License, version 3", "Dawn of Light", "OpenDAoC",
                     "Offline DAoC", "shadowofze", "not affiliated with", "Electronic Arts", "Broadsword",
                 })
            Assert.That(text, Does.Contain(needed), needed);
        Assert.That(text, Does.Not.Contain("**"));
        Assert.That(text, Does.Contain("OpenDAoC (https://github.com/OpenDAoC/OpenDAoC-Core)"), "a link keeps its address");
    }
}
