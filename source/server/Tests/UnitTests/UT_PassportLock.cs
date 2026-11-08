// Character passport: the home lock and the Passport Desk. From rambodolphin13/DividedRealms (da78651, da92848; credit:
// rambodolphin13); the Desk's open rule and the shared-key checks are Living Server 1.2's.
using System.Net;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture]
    public class UT_PassportLock
    {
        private const string Id = "character";

        [Test]
        public void ACharacterWithoutATripIsHome()
        {
            Assert.That(PassportLock.IsAway([], out _), Is.False);
            Assert.That(PassportLock.IsAway([new DbCoreCharacterXCustomParam(Id, "title", "hero")], out _), Is.False);
            Assert.That(PassportLock.IsAway([new DbCoreCharacterXCustomParam(Id, "passport.cancelled", "trip")], out _), Is.False);
        }

        [Test]
        public void ATripLocksTheCharacterAndNamesTheWorld()
        {
            bool away = PassportLock.IsAway(
                [new DbCoreCharacterXCustomParam(Id, PassportLock.AwayKey, "trip"), new DbCoreCharacterXCustomParam(Id, PassportLock.HostKey, "Bob's world")],
                out string where);

            Assert.That(away, Is.True);
            Assert.That(where, Is.EqualTo("Bob's world"));
        }

        [Test]
        public void AnUnnamedWorldStillLocks()
        {
            Assert.That(PassportLock.IsAway([new DbCoreCharacterXCustomParam(Id, PassportLock.AwayKey, "trip")], out string where), Is.True);
            Assert.That(where, Is.EqualTo("another world"));
        }

        [Test]
        public void TheKeysMatchWhatThePassportToolWrites()
        {
            Assert.That(PassportLock.AwayKey, Is.EqualTo("passport.away"));
            Assert.That(PassportLock.HostKey, Is.EqualTo("passport.host"));
            Assert.That(PassportLock.VisitorKey, Is.EqualTo(OfflineDaoc.Passport.PassportEngine.VisitorKey));
        }

        [TestCase("Basic Zm9vOmJhcg==", "foo", "bar")]
        [TestCase("basic Zm9vOmI6cg==", "foo", "b:r")]
        public void TheDeskReadsAnAccountAndPassword(string header, string account, string password)
        {
            Assert.That(PassportDesk.TryReadBasicAuthorization(header, out string readAccount, out string readPassword), Is.True);
            Assert.That(readAccount, Is.EqualTo(account));
            Assert.That(readPassword, Is.EqualTo(password));
        }

        [Test]
        public void TheServerAndTheEngineUseTheSameKeys()
        {
            Assert.That(PassportLock.AwayKey, Is.EqualTo(OfflineDaoc.Passport.PassportEngine.AwayKey));
            Assert.That(PassportLock.HostKey, Is.EqualTo(OfflineDaoc.Passport.PassportEngine.HostKey));
            Assert.That(PassportDesk.PortOffset, Is.EqualTo(OfflineDaoc.Passport.PassportDeskClient.PortOffset));
        }

        [Test]
        public void TheDeskOpensOnlyWhileHostingASqliteWorld()
        {
            Assert.That(PassportDesk.ShouldOpen(IPAddress.Loopback, sqlite: true, testSwitch: false), Is.False, "A single-player world opens nothing.");
            Assert.That(PassportDesk.ShouldOpen(IPAddress.Any, sqlite: true, testSwitch: false), Is.True);
            Assert.That(PassportDesk.ShouldOpen(IPAddress.Parse("192.168.1.20"), sqlite: true, testSwitch: false), Is.True);
            Assert.That(PassportDesk.ShouldOpen(IPAddress.Any, sqlite: false, testSwitch: false), Is.False);
            Assert.That(PassportDesk.ShouldOpen(IPAddress.Loopback, sqlite: true, testSwitch: true), Is.True, "A private test copy.");
        }

        [Test]
        public void VisitorsAreWelcomeUnlessTheHostTurnsThemOff()
        {
            Assert.That(DOL.GS.ServerProperties.Properties.PASSPORT_VISITORS, Is.True);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Bearer Zm9vOmJhcg==")]
        [TestCase("Basic not-base-64!")]
        [TestCase("Basic Zm9v")]
        [TestCase("Basic OmJhcg==")]
        public void TheDeskRefusesAnyOtherHeader(string header)
        {
            Assert.That(PassportDesk.TryReadBasicAuthorization(header, out _, out _), Is.False);
        }
    }
}
