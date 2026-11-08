using System.Net;
using NUnit.Framework;

namespace OfflineDaoc.Launcher.Tests;

// Hand port from rambodolphin13/DividedRealms (JoinFriendTests at 3c6cc9d, 6f22a7a, af1fcf8), Living Server 1.2: the original
// client only (no modern 1.130 client), LS invite codes, and the host's own account name "offline" is refused.

public sealed class JoinFriendTests
{
    private string _directory = string.Empty;
    private string ProfilePath => Path.Combine(_directory, "join-friend.json");

    [SetUp]
    public void SetUp() => _directory = Path.Combine(Path.GetTempPath(), "odc-join-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, true);
    }

    [Test]
    public void RememberedPasswordRoundTripsWithoutPlaintextOnDisk()
    {
        JoinFriendProfile.Save(ProfilePath, "100.120.169.75", "Aaron", "S3cret!pw", rememberPassword: true);

        JoinFriendProfile loaded = JoinFriendProfile.Load(ProfilePath);
        Assert.That(loaded.HostAddress, Is.EqualTo("100.120.169.75"));
        Assert.That(loaded.GuestAccount, Is.EqualTo("Aaron"));
        Assert.That(loaded.Password, Is.EqualTo("S3cret!pw"));
        Assert.That(File.ReadAllText(ProfilePath), Does.Not.Contain("S3cret!pw"));
    }

    [Test]
    public void DetailsAreKeptButPasswordIsForgottenWhenNotRemembered()
    {
        JoinFriendProfile.Save(ProfilePath, "100.120.169.75", "Aaron", "S3cret!pw", rememberPassword: true);
        JoinFriendProfile.Save(ProfilePath, "100.120.169.75", "Aaron", "S3cret!pw", rememberPassword: false);

        JoinFriendProfile loaded = JoinFriendProfile.Load(ProfilePath);
        Assert.That(loaded.GuestAccount, Is.EqualTo("Aaron"));
        Assert.That(loaded.Password, Is.Empty);
        Assert.That(loaded.RememberPassword, Is.False);
    }

    [Test]
    public void MissingOrDamagedProfileLoadsEmpty()
    {
        Assert.That(JoinFriendProfile.Load(ProfilePath).HostAddress, Is.Empty);

        Directory.CreateDirectory(_directory);
        File.WriteAllText(ProfilePath, "{ not json");
        Assert.That(JoinFriendProfile.Load(ProfilePath).GuestAccount, Is.Empty);

        // stefanrows' profile files also carry HostAccount; it is ignored.
        File.WriteAllText(ProfilePath, "{\"HostAddress\":\"100.64.0.1\",\"HostAccount\":\"offline\",\"ProtectedPassword\":\"not-base64!\"}");
        JoinFriendProfile loaded = JoinFriendProfile.Load(ProfilePath);
        Assert.That(loaded.HostAddress, Is.EqualTo("100.64.0.1"));
        Assert.That(loaded.Password, Is.Empty);
    }

    [TestCase("100.64.0.1", true)]
    [TestCase("100.127.255.254", true)]
    [TestCase("192.168.1.20", true)]
    [TestCase("10.0.0.7", true)]
    [TestCase("172.20.1.1", true)]
    [TestCase("100.63.0.1", true)]
    [TestCase("203.0.113.25", true)] // internet mode
    [TestCase("169.254.1.1", false)]
    [TestCase("224.0.0.1", false)]
    [TestCase("0.1.2.3", false)]
    [TestCase("127.0.0.1", false)]
    [TestCase("100.64.1", false)]
    [TestCase(" 100.64.0.1 ", true)]
    [TestCase("friend.example.com", false)]
    public void HostAddressIsAnyHostButThisPc(string text, bool accepted)
    {
        Assert.That(JoinFriend.TryParseHost(text, out _, out string error), Is.EqualTo(accepted));
        Assert.That(error, accepted ? Is.Empty : Is.Not.Empty);
    }

    [Test]
    public void InviteCodeRoundTripsAddressesInOrder()
    {
        IPAddress[] addresses = [IPAddress.Parse("100.90.12.34"), IPAddress.Parse("203.0.113.25"), IPAddress.Parse("192.168.1.20")];
        string code = InviteCode.Create(addresses, 10330);

        Assert.That(code, Does.StartWith("LS-"));
        Assert.That(InviteCode.TryParse(code, out InviteCode.Invite invite), Is.True);
        Assert.That(invite.Addresses, Is.EqualTo(addresses));
        Assert.That(invite.Port, Is.EqualTo(10330));
    }

    [Test]
    public void InviteCodeSurvivesCopyingMistakes()
    {
        string code = InviteCode.Create([IPAddress.Parse("203.0.113.25")], 10330);

        Assert.That(InviteCode.TryParse("  " + code.ToLowerInvariant() + "\r\n", out _), Is.True);
        Assert.That(InviteCode.TryParse(code.Replace("-", " "), out _), Is.True);
        Assert.That(InviteCode.TryParse(code.Replace('0', 'O').Replace('1', 'I'), out _), Is.True);
    }

    [Test]
    public void DamagedInviteCodeIsRefused()
    {
        string code = InviteCode.Create([IPAddress.Parse("203.0.113.25")], 10330);

        Assert.That(InviteCode.TryParse(code[..^1], out _), Is.False);
        Assert.That(InviteCode.TryParse("", out _), Is.False);
        Assert.That(InviteCode.TryParse("hello friend", out _), Is.False);
        for (int index = 3; index < code.Length; index++)
        {
            if (code[index] == '-')
                continue;
            string changed = code[..index] + (code[index] == '2' ? '3' : '2') + code[(index + 1)..];
            Assert.That(InviteCode.TryParse(changed, out _), Is.False, changed);
        }
    }

    [Test]
    public void TargetIsACodeOrAnAddress()
    {
        string code = InviteCode.Create([IPAddress.Parse("100.90.12.34"), IPAddress.Parse("203.0.113.25")], 10330);

        Assert.That(JoinFriend.TryParseTarget(code, 10330, out var hosts, out _), Is.True);
        Assert.That(hosts, Is.EqualTo(new[] { IPAddress.Parse("100.90.12.34"), IPAddress.Parse("203.0.113.25") }));
        Assert.That(JoinFriend.TryParseTarget("203.0.113.25", 10330, out hosts, out _), Is.True);
        Assert.That(hosts, Is.EqualTo(new[] { IPAddress.Parse("203.0.113.25") }));
        Assert.That(JoinFriend.TryParseTarget("127.0.0.1", 10330, out _, out _), Is.False);
        Assert.That(JoinFriend.TryParseTarget("not a code", 10330, out _, out _), Is.False);
    }

    [Test]
    public void CodeForAnotherGameIsRefused()
    {
        string code = InviteCode.Create([IPAddress.Parse("203.0.113.25")], 10300);

        Assert.That(JoinFriend.TryParseTarget(code, 10330, out _, out string error), Is.False);
        Assert.That(error, Does.Contain("different game"));
    }

    [Test]
    public void CodeHoldingOnlyThisPcIsRefused()
    {
        string code = InviteCode.Create([IPAddress.Loopback], 10330);

        Assert.That(JoinFriend.TryParseTarget(code, 10330, out _, out _), Is.False);
    }

    [Test]
    public async Task ReachableHostIsFoundAfterADeadOne()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint) listener.LocalEndpoint).Port;
        IPAddress dead = IPAddress.Parse("127.0.0.2");
        try
        {
            IPAddress? found = await JoinFriend.FindReachableAsync([dead, IPAddress.Loopback], port, TimeSpan.FromSeconds(2));
            Assert.That(found, Is.EqualTo(IPAddress.Loopback));
        }
        finally
        {
            listener.Stop();
        }
    }

    [TestCase("Aaron", true)]
    [TestCase("guest2", true)]
    [TestCase("", false)]
    [TestCase("has space", false)]
    [TestCase("abcdefghijklmnopqrstu", false)]
    public void AccountNamesFollowTheLegacyClient(string account, bool valid) =>
        Assert.That(JoinFriend.IsValidAccount(account), Is.EqualTo(valid));

    [TestCase("pw", true)]
    [TestCase("", false)]
    [TestCase("tab\tchar", false)]
    [TestCase("ünicode", false)]
    public void PasswordsFollowTheLegacyClient(string password, bool valid) =>
        Assert.That(JoinFriend.IsValidPassword(password), Is.EqualTo(valid));

    [Test]
    public void OriginalClientConnectsToTheHostOnTheClassicPort()
    {
        string runtime = Path.Combine(_directory, "runtime");
        string app = Path.Combine(runtime, "client-opendaoc", "app");
        Directory.CreateDirectory(app);
        File.WriteAllText(Path.Combine(app, "game.dll"), "x");
        File.WriteAllText(Path.Combine(app, "connect.exe"), "x");

        JoinClient client = JoinClient.For(runtime);
        Assert.That(client.IsInstalled, Is.True);
        Assert.That(client.LoginPort, Is.EqualTo(10300));
        var info = client.CreateStartInfo(IPAddress.Parse("100.70.1.2"), "Aaron", "pw");
        Assert.That(info.FileName, Is.EqualTo(Path.Combine(app, "connect.exe")));
        Assert.That(info.ArgumentList.ToArray(), Is.EqualTo(new[] { "game.dll", "100.70.1.2", "Aaron", "pw" }));
        Assert.That(info.UseShellExecute, Is.False);
    }

    [TestCase("offline")]
    [TestCase("Offline")]
    [TestCase("OFFLINE")]
    public void TheHostsOwnAccountNameIsRefusedWithASuggestion(string account)
    {
        Assert.That(JoinFriend.IsValidAccount(account), Is.True, "a legal name, but the host's own");
        Assert.That(JoinFriend.IsHostAccountName(account), Is.True);
        string problem = JoinFriend.AccountProblem(account, "Aaron", new Random(1));
        Assert.That(problem, Does.Contain("your friend's own account").And.Contain("Aaron"));
    }

    [Test]
    public void AnOwnNameIsFine()
    {
        Assert.That(JoinFriend.AccountProblem("Aaron", "Aaron", new Random(1)), Is.Empty);
        Assert.That(JoinFriend.AccountProblem("offline2", "Aaron", new Random(1)), Is.Empty);
        Assert.That(JoinFriend.AccountProblem("has space", "Aaron", new Random(1)), Is.Not.Empty);
    }

    [TestCase("Aaron", "Aaron")]
    [TestCase("Jane Doe", "JaneDoe")]
    [TestCase("averyveryverylongwindowsusername", "averyveryverylongwin")]
    public void TheSuggestionIsTheWindowsUserName(string user, string suggested) =>
        Assert.That(JoinFriend.SuggestAccountName(user, new Random(1)), Is.EqualTo(suggested));

    [TestCase("offline")]
    [TestCase("Al")]
    [TestCase("")]
    [TestCase(null)]
    [TestCase("éè")]
    public void WithoutAUsableUserNameTheSuggestionIsFriendAndDigits(string? user)
    {
        string suggested = JoinFriend.SuggestAccountName(user, new Random(1));
        Assert.That(suggested, Does.Match("^friend[0-9]{4}$"));
        Assert.That(JoinFriend.IsValidAccount(suggested) && !JoinFriend.IsHostAccountName(suggested), Is.True);
    }

    [Test]
    public void TwoFriendsGetTheirOwnNamesAndTheSameHostTargets()
    {
        string code = InviteCode.Create([IPAddress.Parse("100.90.12.34"), IPAddress.Parse("192.168.1.20")], 10300);

        Assert.That(JoinFriend.TryParseTarget(code, JoinClient.ClassicLoginPort, out var first, out _), Is.True);
        Assert.That(JoinFriend.TryParseTarget(code, JoinClient.ClassicLoginPort, out var second, out _), Is.True);
        Assert.That(second, Is.EqualTo(first));
        Assert.That(JoinFriend.AccountProblem("Aaron", null, new Random(1)), Is.Empty);
        Assert.That(JoinFriend.AccountProblem("Bea", null, new Random(2)), Is.Empty);
    }
}
