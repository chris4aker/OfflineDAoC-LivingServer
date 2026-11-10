using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// A player hosted by a bot-led group stays hosted through a zone or region change (PC test 2026-11-07: let go with
    /// reason=left 80 s after accepting Krychie's invite, after only a GM /jump to the leader and a /follow).
    /// </summary>
    [TestFixture]
    public class UT_BotLedZoneChange
    {
        [Test]
        public void AZoneChangeIsNotLeaving()
        {
            Assert.That(BotLedGroups.HostedPlayerLeft(inGroup: true, outOfWorld: true, awaySince: 10_000, now: 10_000), Is.False);
            Assert.That(BotLedGroups.HostedPlayerLeft(true, true, 10_000, 10_000 + 30_000), Is.False, "a slow load");
            Assert.That(BotLedGroups.HostedPlayerLeft(true, false, 0, 50_000), Is.False, "back in the world");
        }

        [Test]
        public void LeavingTheGroupOrNeverComingBackIsLeaving()
        {
            Assert.That(BotLedGroups.HostedPlayerLeft(false, false, 0, 1_000), Is.True, "out of the group");
            Assert.That(BotLedGroups.HostedPlayerLeft(true, true, 10_000, 10_000 + BotLedGroups.AwayGraceMs + 1), Is.True,
                "out of the world past the grace");
        }
    }
}
