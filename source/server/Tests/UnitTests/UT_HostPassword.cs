using System;
using DOL.GS.PacketHandler.Client.v168;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The host password gate for new accounts from other PCs (Living Server 1.2, play with friends). Hand port from
    /// rambodolphin13/DividedRealms af1fcf8 (UT_HostPassword), with the owner's rule of 2026-11-08: no host password
    /// means no new account from another PC. Plus the same-address rule and the wrong-password lockout.
    /// </summary>
    [TestFixture]
    public class UT_HostPassword
    {
        private static readonly string Stored = HostPassword.Hash("swordfish");

        [Test]
        public void ThisPcNeverNeedsIt_SinglePlayerIsUnchanged()
        {
            Assert.That(HostPassword.AllowsNewAccount("", "anything", "127.0.0.1"), Is.True);
            Assert.That(HostPassword.AllowsNewAccount(null, "anything", "127.0.0.1"), Is.True);
            Assert.That(HostPassword.AllowsNewAccount(Stored, "my own password", "127.0.0.1"), Is.True);
            Assert.That(HostPassword.AllowsNewAccount(Stored, "x", "::1"), Is.True);
            Assert.That(HostPassword.AllowsNewAccount(Stored, "x", "127.0.0.2"), Is.True);
        }

        [TestCase("192.168.1.20")]
        [TestCase("100.90.1.2")]
        [TestCase("203.0.113.25")]
        [TestCase("[not connected]")]
        public void WithoutAHostPasswordNoOtherPcMakesAnAccount(string address)
        {
            Assert.That(HostPassword.CheckNewAccount("", "anything", address), Is.EqualTo(HostPassword.NewAccount.NoHostPassword));
            Assert.That(HostPassword.CheckNewAccount(null, "anything", address), Is.EqualTo(HostPassword.NewAccount.NoHostPassword));
        }

        private static string Crypt(string password) => DOL.GS.PacketHandler.Client.v168.LoginRequestHandler.CryptPassword(password);

        [Test]
        public void AFriendNeedsTheHostPassword_AndKeepsOnlyTheirOwn()
        {
            string sent = HostPassword.Combine("swordfish", "aaron-pw");
            Assert.That(sent, Has.Length.LessThanOrEqualTo(HostPassword.MaxSentLength));
            Assert.That(sent, Does.Not.Contain("swordfish"), "the host password never travels");

            Assert.That(HostPassword.CheckNewAccount(Stored, sent, "203.0.113.25", out string kept), Is.EqualTo(HostPassword.NewAccount.Allowed));
            Assert.That(kept, Is.EqualTo("aaron-pw"), "the account is made with the friend's own password only");
        }

        [TestCase("swordfish")] // the host password typed as the account password
        [TestCase("")]
        [TestCase("aaron-pw")]
        public void AnythingElseFromAnotherPcIsRefused(string sent)
        {
            Assert.That(HostPassword.CheckNewAccount(Stored, sent, "203.0.113.25", out string kept), Is.EqualTo(HostPassword.NewAccount.WrongPassword));
            Assert.That(kept, Is.Null);
        }

        [Test]
        public void AWrongHostPasswordCreatesNothing()
        {
            string sent = HostPassword.Combine("Swordfish", "aaron-pw");
            Assert.That(HostPassword.CheckNewAccount(Stored, sent, "203.0.113.25", out string kept), Is.EqualTo(HostPassword.NewAccount.WrongPassword));
            Assert.That(kept, Is.Null);
            Assert.That(HostPassword.CheckNewAccount(Stored, "~ZZZZZZaaron-pw", "203.0.113.25", out _), Is.EqualTo(HostPassword.NewAccount.WrongPassword));
            Assert.That(HostPassword.CheckNewAccount(Stored, null, "203.0.113.25", out _), Is.EqualTo(HostPassword.NewAccount.WrongPassword));
        }

        [Test]
        public void HomeNetworkAndTailscaleFriendsNeedItToo()
        {
            Assert.That(HostPassword.AllowsNewAccount(Stored, HostPassword.Combine("wrong", "pw"), "192.168.1.20"), Is.False);
            Assert.That(HostPassword.AllowsNewAccount(Stored, HostPassword.Combine("wrong", "pw"), "100.90.1.2"), Is.False);
            Assert.That(HostPassword.AllowsNewAccount(Stored, HostPassword.Combine("swordfish", "pw-123"), "192.168.1.20"), Is.True);
            Assert.That(HostPassword.AllowsNewAccount(Stored, HostPassword.Combine("swordfish", "pw-123"), "100.90.1.2"), Is.True);
        }

        [Test]
        public void ANewAccountFromAnotherPcNeedsAnOwnPasswordOfSixOrMore()
        {
            // Audit 2026-11-13: a short own password could be guessed while the world is hosted.
            Assert.That(HostPassword.CheckNewAccount(Stored, HostPassword.Combine("swordfish", "pw"), "192.168.1.20", out string kept),
                Is.EqualTo(HostPassword.NewAccount.ShortOwnPassword));
            Assert.That(kept, Is.Null);
            Assert.That(HostPassword.CheckNewAccount(Stored, HostPassword.Combine("swordfish", "12345"), "192.168.1.20", out _),
                Is.EqualTo(HostPassword.NewAccount.ShortOwnPassword));
            Assert.That(HostPassword.CheckNewAccount(Stored, HostPassword.Combine("swordfish", "123456"), "192.168.1.20", out _),
                Is.EqualTo(HostPassword.NewAccount.Allowed));
            Assert.That(HostPassword.CheckNewAccount(Stored, "pw", "127.0.0.1", out _), Is.EqualTo(HostPassword.NewAccount.Allowed), "this PC is never asked");
        }

        [TestCase("127.0.0.1", true)]
        [TestCase("::1", true)]
        [TestCase("::ffff:127.0.0.1", true)]
        [TestCase("192.168.86.216", false)]
        [TestCase("100.90.1.2", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void ThisPcIsOnlyALoopbackAddress(string address, bool thisPc) =>
            Assert.That(HostPassword.IsThisPc(address), Is.EqualTo(thisPc));

        [Test]
        public void WrongAccountPasswordsHaveTheirOwnLockout()
        {
            Assert.That(HostPasswordAttempts.AccountLogins, Is.Not.SameAs(HostPasswordAttempts.Shared),
                "a friend's typos on their own password don't use up the host password's tries");
        }

        [Test]
        public void ThisPcKeepsItsPasswordAsSent()
        {
            Assert.That(HostPassword.CheckNewAccount(Stored, "0123456789ABCDEF0123", "127.0.0.1", out string kept), Is.EqualTo(HostPassword.NewAccount.Allowed));
            Assert.That(kept, Is.EqualTo("0123456789ABCDEF0123"));
            Assert.That(HostPassword.MatchesAccount(Crypt("0123456789ABCDEF0123"), "0123456789ABCDEF0123", Crypt), Is.True);
        }

        [Test]
        public void TwoFriends_NeitherCanLogInAsTheOtherWithTheHostPassword()
        {
            HostPassword.CheckNewAccount(Stored, HostPassword.Combine("swordfish", "aaron-pw"), "192.168.1.30", out string aaron);
            HostPassword.CheckNewAccount(Stored, HostPassword.Combine("swordfish", "bea-pw"), "192.168.1.31", out string bea);
            string aaronAccount = Crypt(aaron);

            // Bea knows the host password, not Aaron's.
            Assert.That(HostPassword.MatchesAccount(aaronAccount, "swordfish", Crypt), Is.False);
            Assert.That(HostPassword.MatchesAccount(aaronAccount, HostPassword.Combine("swordfish", "bea-pw"), Crypt), Is.False);
            Assert.That(HostPassword.MatchesAccount(aaronAccount, HostPassword.Combine("swordfish", ""), Crypt), Is.False);
            Assert.That(HostPassword.MatchesAccount(aaronAccount, bea, Crypt), Is.False);
        }

        [Test]
        public void AFriendsLaterLoginNeedsOnlyTheirOwnPassword()
        {
            HostPassword.CheckNewAccount(Stored, HostPassword.Combine("swordfish", "aaron-pw"), "203.0.113.25", out string aaron);
            string account = Crypt(aaron);

            Assert.That(HostPassword.MatchesAccount(account, "aaron-pw", Crypt), Is.True);
            // Join Friend sent the first-login form again: the own part counts, the host part is not needed.
            Assert.That(HostPassword.MatchesAccount(account, HostPassword.Combine("swordfish", "aaron-pw"), Crypt), Is.True);
            Assert.That(HostPassword.MatchesAccount(account, HostPassword.Combine("changed-host-pw", "aaron-pw"), Crypt), Is.True);
            Assert.That(HostPassword.MatchesAccount(account, "wrong", Crypt), Is.False);
        }

        [Test]
        public void AnExistingAccountsOwnPasswordStillWorks_EvenOneStartingWithTheMarker()
        {
            Assert.That(HostPassword.MatchesAccount(Crypt("~ABCDEFsecret"), "~ABCDEFsecret", Crypt), Is.True);
            Assert.That(HostPassword.MatchesAccount(Crypt("plain"), "plain", Crypt), Is.True);
        }

        [Test]
        public void TheJoinTokenIsSixLettersOfTheAlphabet_AndTheOwnPasswordFitsThirteen()
        {
            string token = HostPassword.TokenForPassword("swordfish");
            Assert.That(token, Does.Match("^[0-9A-HJKMNP-TV-Z]{6}$"));
            Assert.That(token, Is.EqualTo(HostPassword.TokenForHash(Stored)));
            Assert.That(token, Is.EqualTo(HostPassword.TokenForHash(Stored.ToUpperInvariant())));
            Assert.That(HostPassword.MaxOwnPasswordLength, Is.EqualTo(13));
            Assert.That(HostPassword.TrySplit(HostPassword.Combine("swordfish", "1234567890123"), out string t, out string own), Is.True);
            Assert.That((t, own), Is.EqualTo((token, "1234567890123")));
            Assert.That(HostPassword.TrySplit("~abc", out _, out _), Is.False);
            Assert.That(HostPassword.TrySplit("plain password", out _, out _), Is.False);
        }

        // The launcher's HostForFriends.HashHostPassword has this same value in HostForFriendsTests.
        [Test]
        public void HashMatchesTheLauncher()
        {
            Assert.That(Stored, Has.Length.EqualTo(64));
            Assert.That(Stored, Is.EqualTo("4a9e1d7e2bb770e3f89110ee8d09517a3a4dbd6f4165881d3a79c6d3ef9d0273"));
        }

        [Test]
        public void TwoFriendsBehindOneRouterAreNotKeptWaiting()
        {
            // The upstream 15-minute same-address rule would refuse the second friend; with the host password it doesn't.
            Assert.That(HostPassword.SkipsSameAddressWait(Stored, HostPassword.Combine("swordfish", "aaron-pw"), "203.0.113.25"), Is.True);
            Assert.That(HostPassword.SkipsSameAddressWait(Stored, HostPassword.Combine("swordfish", "bea-pw"), "203.0.113.25"), Is.True);
            // Home network, Tailscale and this PC (two local test clients) skip it anyway.
            Assert.That(HostPassword.SkipsSameAddressWait("", "x", "192.168.1.30"), Is.True);
            Assert.That(HostPassword.SkipsSameAddressWait("", "x", "100.90.1.2"), Is.True);
            Assert.That(HostPassword.SkipsSameAddressWait("", "x", "127.0.0.1"), Is.True);
        }

        [Test]
        public void APublicAddressWithoutTheHostPasswordKeepsTheWait()
        {
            Assert.That(HostPassword.SkipsSameAddressWait(Stored, HostPassword.Combine("wrong", "pw-123"), "203.0.113.25"), Is.False);
            Assert.That(HostPassword.SkipsSameAddressWait(Stored, "swordfish", "203.0.113.25"), Is.False);
            Assert.That(HostPassword.SkipsSameAddressWait("", "anything", "203.0.113.25"), Is.False);
        }

        [Test]
        public void FiveWrongHostPasswordsLockThatAddressOutForTenMinutes()
        {
            var attempts = new HostPasswordAttempts();
            var start = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);

            for (int i = 0; i < HostPasswordAttempts.MaxFailures - 1; i++)
                attempts.RecordFailure("203.0.113.25", start.AddSeconds(i));
            Assert.That(attempts.IsLockedOut("203.0.113.25", start.AddSeconds(10)), Is.False);

            attempts.RecordFailure("203.0.113.25", start.AddSeconds(20));
            Assert.That(attempts.IsLockedOut("203.0.113.25", start.AddSeconds(21)), Is.True);
            Assert.That(attempts.IsLockedOut("203.0.113.26", start.AddSeconds(21)), Is.False, "another address is not affected");
            Assert.That(attempts.IsLockedOut("203.0.113.25", start + HostPasswordAttempts.Window + TimeSpan.FromSeconds(21)), Is.False);
        }
    }
}
