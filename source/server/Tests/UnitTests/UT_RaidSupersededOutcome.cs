using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Dodens Gruva, owner's PC, 2026-11-17 (build ccba364, /epictest sweep): the realm's own raid was called at 01:12:54
    /// (REALM_RAID_SCHEDULED) and ended at 01:15:55 as outcome="Stopped by player" with reason "superseded by the forced
    /// summoners-midgard expedition", the moment the sweep started its forced Summoner's Hall raid (EPIC_SWEEP_RAID_START
    /// 01:15:55). Nobody pressed Stop. A supersede is now its own outcome, "Superseded"; like a stop it sets no cooldown and
    /// is no result for the raid's record. Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidSupersededOutcome
    {
        [Test]
        public void ARaidSupersededByAForcedExpeditionIsSupersededNotStoppedByThePlayer()
        {
            string reason = AutonomousRealmRaid.SupersededReason("summoners-midgard");
            Assert.Multiple(() =>
            {
                Assert.That(reason, Is.EqualTo("Superseded by the forced summoners-midgard expedition."));
                Assert.That(AutonomousRealmRaid.StopOutcome(reason), Is.EqualTo("Superseded"));
                Assert.That(AutonomousRealmRaid.SetsCooldown(reason), Is.False, "no cooldown, as before");
            });
        }

        [Test]
        public void TheLauncherStopIsStillStoppedByPlayer()
        {
            const string reason = "Stopped by the player from the launcher.";
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.StopOutcome(reason), Is.EqualTo("Stopped by player"));
                Assert.That(AutonomousRealmRaid.SetsCooldown(reason), Is.False);
            });
        }

        [TestCase("Stalled: no named or boss kill, no new low on its target and no wipe for 60 minutes.")]
        [TestCase("Defeated at Chthonic Knight Ukobat after 2 failed attempts.")]
        [TestCase("The final dungeon encounter has been defeated.")]
        public void OtherEndingsAreNoStopAndSetTheCooldown(string reason)
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.StopOutcome(reason), Is.Null);
                Assert.That(AutonomousRealmRaid.SetsCooldown(reason), Is.True);
            });
        }

        [Test]
        public void ASupersedeIsNoResultForTheRaidRecord()
        {
            var rules = RealmRaidRecruitmentPolicy.SizingRules.Default;
            Assert.That(RealmRaidRecruitmentPolicy.SizePercent(["Boss defeated", "Superseded", "Superseded", "Boss defeated"], rules),
                Is.EqualTo(RealmRaidRecruitmentPolicy.SizePercent(["Boss defeated", "Boss defeated"], rules)));
        }

        [Test]
        public void TheRealmHearsTheExpeditionIsCalledOff()
        {
            Assert.That(RealmEventBanter.RaidOutcome("Dodens Gruva", "Superseded"),
                Is.EqualTo(RealmEventBanter.RaidOutcome("Dodens Gruva", "Stopped by player")));
        }
    }
}
