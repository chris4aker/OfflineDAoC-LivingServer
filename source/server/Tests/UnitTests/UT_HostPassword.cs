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

        [Test]
        public void AFriendNeedsTheHostPassword()
        {
            Assert.That(HostPassword.AllowsNewAccount(Stored, "swordfish", "203.0.113.25"), Is.True);
            Assert.That(HostPassword.CheckNewAccount(Stored, "Swordfish", "203.0.113.25"), Is.EqualTo(HostPassword.NewAccount.WrongPassword));
            Assert.That(HostPassword.AllowsNewAccount(Stored, "", "203.0.113.25"), Is.False);
            Assert.That(HostPassword.AllowsNewAccount(Stored, null, "203.0.113.25"), Is.False);
        }

        [Test]
        public void HomeNetworkAndTailscaleFriendsNeedItToo()
        {
            Assert.That(HostPassword.AllowsNewAccount(Stored, "wrong", "192.168.1.20"), Is.False);
            Assert.That(HostPassword.AllowsNewAccount(Stored, "wrong", "100.90.1.2"), Is.False);
            Assert.That(HostPassword.AllowsNewAccount(Stored, "swordfish", "192.168.1.20"), Is.True);
            Assert.That(HostPassword.AllowsNewAccount(Stored, "swordfish", "100.90.1.2"), Is.True);
        }

        [Test]
        public void AStoredHashInCapitalsStillMatches()
        {
            Assert.That(HostPassword.AllowsNewAccount(Stored.ToUpperInvariant(), "swordfish", "203.0.113.25"), Is.True);
            Assert.That(HostPassword.AllowsNewAccount("not a hash", "swordfish", "203.0.113.25"), Is.False);
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
            Assert.That(HostPassword.SkipsSameAddressWait(Stored, "swordfish", "203.0.113.25"), Is.True);
            Assert.That(HostPassword.SkipsSameAddressWait(Stored, "swordfish", "203.0.113.25"), Is.True);
            // Home network, Tailscale and this PC (two local test clients) skip it anyway.
            Assert.That(HostPassword.SkipsSameAddressWait("", "x", "192.168.1.30"), Is.True);
            Assert.That(HostPassword.SkipsSameAddressWait("", "x", "100.90.1.2"), Is.True);
            Assert.That(HostPassword.SkipsSameAddressWait("", "x", "127.0.0.1"), Is.True);
        }

        [Test]
        public void APublicAddressWithoutTheHostPasswordKeepsTheWait()
        {
            Assert.That(HostPassword.SkipsSameAddressWait(Stored, "wrong", "203.0.113.25"), Is.False);
            Assert.That(HostPassword.SkipsSameAddressWait("", "anything", "203.0.113.25"), Is.False);
            Assert.That(HostPassword.SkipsSameAddressWait(Stored, "swordfish", "[not connected]"), Is.True, "the password is the gate");
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
