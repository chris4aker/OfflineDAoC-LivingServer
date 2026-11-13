using System.Diagnostics;
using System.Security.Principal;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

/// <summary>
/// Living Server 1.2: runs Host for Friends' real firewall script on Windows and reads the rules back with netsh, then
/// removes them. It changes the PC's firewall, so it runs only on GitHub's Windows runner (GITHUB_ACTIONS=true, as
/// administrator) and is skipped everywhere else, a developer's PC included: it would replace a real host's rules.
/// </summary>
[NonParallelizable]
public sealed class HostForFriendsFirewallTests
{
    private string _runtime = string.Empty;
    // Another program's rule on the same port: STOP HOSTING must leave it.
    private const string OtherRule = "Living Server test other app TCP 10300";

    [SetUp]
    public void SetUp()
    {
        if (!OperatingSystem.IsWindows())
            Assert.Ignore("Windows Firewall rules exist on Windows only.");
        if (!string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase))
            Assert.Ignore("Changes the firewall: runs on the CI runner only.");
        using (var identity = WindowsIdentity.GetCurrent())
        {
            if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
                Assert.Ignore("Needs administrator rights.");
        }

        _runtime = Path.Combine(Path.GetTempPath(), "odc-firewall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(_runtime, "server", "config"));
        File.WriteAllBytes(Path.Combine(_runtime, "server", "CoreServer.exe"), []);
        File.WriteAllText(HostForFriends.ConfigPath(_runtime),
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><root><Server><Port>10300</Port><IP>127.0.0.1</IP><UdpIP>127.0.0.1</UdpIP>" +
            "<UdpPort>10400</UdpPort><EnableUPnP>False</EnableUPnP></Server></root>");
    }

    [TearDown]
    public void TearDown()
    {
        if (_runtime.Length == 0)
            return;
        try
        {
            RunScript(HostForFriends.FirewallScript(_runtime, HostForFriends.Read(_runtime), add: false), "teardown");
            Netsh("advfirewall", "firewall", "delete", "rule", $"name={OtherRule}");
        }
        finally
        {
            Directory.Delete(_runtime, true);
        }
    }

    [Test]
    public void RulesAreScopedToTheServerThePortsAndTheHomeNetworkAndTailscale_ThenRemoved()
    {
        var state = HostForFriends.Read(_runtime);
        Assert.That(RunScript(HostForFriends.FirewallScript(_runtime, state, add: true), "add"), Is.EqualTo(0));

        string program = Path.Combine(_runtime, "server", "CoreServer.exe");
        foreach (var (protocol, port) in new[] { ("TCP", "10300"), ("UDP", "10400") })
        {
            (int exit, string shown) = Netsh("advfirewall", "firewall", "show", "rule", $"name={HostForFriends.FirewallRuleName(protocol, int.Parse(port))}", "verbose");
            Assert.That(exit, Is.EqualTo(0), shown);
            Dictionary<string, string> rule = Fields(shown);
            TestContext.Out.WriteLine(shown);

            Assert.That(rule["Direction"], Is.EqualTo("In"), shown);
            Assert.That(rule["Action"], Is.EqualTo("Allow"), shown);
            Assert.That(rule["Protocol"], Is.EqualTo(protocol), shown);
            Assert.That(rule["LocalPort"], Is.EqualTo(port), shown);
            Assert.That(rule["Program"], Is.EqualTo(program).IgnoreCase, shown);
            string remote = rule["RemoteIP"];
            Assert.That(remote, Does.Contain("LocalSubnet"), shown);
            Assert.That(remote.Contains("100.64.0.0/10") || remote.Contains("100.64.0.0/255.192.0.0"), Is.True, shown);
            Assert.That(remote, Does.Not.Contain("Any"), shown);
        }

        Assert.That(RunScript(HostForFriends.FirewallScript(_runtime, state, add: false), "remove"), Is.EqualTo(0));
        foreach (string name in HostForFriends.FirewallRuleNames(state))
            Assert.That(Netsh("advfirewall", "firewall", "show", "rule", $"name={name}").Exit, Is.Not.EqualTo(0), $"{name} is gone");
    }

    [Test]
    public void StopHostingRemovesExactlyTheRulesStartHostingMade()
    {
        var state = HostForFriends.Read(_runtime);
        Assert.That(RunScript(HostForFriends.FirewallScript(_runtime, state, add: true), "add"), Is.EqualTo(0));
        // What START HOSTING records, as HostForFriendsForm does.
        var settings = new HostForFriends.Settings { FirewallRules = HostForFriends.FirewallRuleNames(state).ToList() };
        Assert.That(Netsh("advfirewall", "firewall", "add", "rule", $"name={OtherRule}", "dir=in", "action=allow", "protocol=TCP", "localport=10300").Exit,
            Is.EqualTo(0));

        // STOP HOSTING's script.
        IReadOnlyList<string> rules = HostForFriends.RulesToRemove(state, settings);
        Assert.That(rules, Is.EquivalentTo(new[] { "Offline DAoC friends TCP 10300", "Offline DAoC friends UDP 10400" }));
        Assert.That(RunScript(HostForFriends.FirewallRemoveScript(rules), "stop"), Is.EqualTo(0));

        foreach (string name in rules)
            Assert.That(Netsh("advfirewall", "firewall", "show", "rule", $"name={name}").Exit, Is.Not.EqualTo(0), $"{name} is gone");
        Assert.That(Netsh("advfirewall", "firewall", "show", "rule", $"name={OtherRule}").Exit, Is.EqualTo(0), "another program's rule stays");

        // A second stop (nothing left of ours) succeeds and still leaves the other rule.
        Assert.That(RunScript(HostForFriends.FirewallRemoveScript(rules), "stop-again"), Is.EqualTo(0));
        Assert.That(Netsh("advfirewall", "firewall", "show", "rule", $"name={OtherRule}").Exit, Is.EqualTo(0));
    }

    // netsh prints "Field:   value" lines; the first of each field is kept.
    private static Dictionary<string, string> Fields(string shown)
    {
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in shown.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            string key = line[..colon].Trim();
            if (key.Length > 0 && !fields.ContainsKey(key))
                fields[key] = line[(colon + 1)..].Trim();
        }
        return fields;
    }

    private int RunScript(string script, string label)
    {
        string path = Path.Combine(_runtime, $"firewall-{label}.cmd");
        File.WriteAllText(path, script);
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", path },
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        })!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        TestContext.Out.WriteLine($"{label}: exit {process.ExitCode} {output}");
        return process.ExitCode;
    }

    private static (int Exit, string Output) Netsh(params string[] arguments)
    {
        var start = new ProcessStartInfo("netsh") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);
        using Process process = Process.Start(start)!;
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output);
    }
}
