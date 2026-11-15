using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Albion's Summoner's Hall raid in the owner's PC sweep of 2026-11-14 (build 1dc66b6, event summoners-albion, 64 bots and
    /// 24 Special Forces): defeated twice at Aidon the Archwizard in Hall of the Corrupt, a route gate on the way in. Tests
    /// only: none of these fixes has been seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_RaidSweep1115
    {
        // ---- the defeat broadcast --------------------------------------------------------------------------------------

        [Test]
        public void ADefeatAtARouteGateNamesTheGateBoss()
        {
            // The client showed "... driven out of Summoner's Hall by GrandSummonerGovannon!": only the main route's defeat was
            // read (null: the raid never reached Summoner's Hall), and the fallback was the final boss's class name.
            string by = AutonomousRealmRaid.DefeatedBy("Aidon the Archwizard", null, null, "Grand Summoner Govannon", "GrandSummonerGovannon");
            Assert.That(by, Is.EqualTo("Aidon the Archwizard"));
            Assert.That(GameWideAnnouncements.RaidDefeated(eRealm.Albion, by, "Summoner's Hall"),
                Is.EqualTo("Albion's expedition has been driven out of Summoner's Hall by Aidon the Archwizard!"));
        }

        [Test]
        public void EveryRaidTypeNamesTheEncounterThatBeatIt()
        {
            // A dungeon raid beaten in its own dungeon.
            Assert.That(AutonomousRealmRaid.DefeatedBy(null, "Conservator", null, "Conservator", "Conservator"), Is.EqualTo("Conservator"));
            // A dragon raid (and an open-world boss, fought like one).
            Assert.That(AutonomousRealmRaid.DefeatedBy(null, null, "Golestandt", "Golestandt", "Golestandt"), Is.EqualTo("Golestandt"));
            // No defeat recorded: the final boss's display name, never its class name.
            Assert.That(AutonomousRealmRaid.DefeatedBy(null, null, null, "Grand Summoner Govannon", "GrandSummonerGovannon"),
                Is.EqualTo("Grand Summoner Govannon"));
            // No boss object either: the class name made readable.
            Assert.That(AutonomousRealmRaid.DefeatedBy(null, null, null, null, "GrandSummonerGovannon"), Is.EqualTo("Grand Summoner Govannon"));
            Assert.That(AutonomousRealmRaid.DefeatedBy("", " ", null, null, "AidonTheArchwizard"), Is.EqualTo("Aidon The Archwizard"));
        }

        [Test]
        public void AClassNameIsMadeReadable()
        {
            Assert.That(AutonomousRealmRaid.Readable("GrandSummonerGovannon"), Is.EqualTo("Grand Summoner Govannon"));
            Assert.That(AutonomousRealmRaid.Readable("Legion"), Is.EqualTo("Legion"));
            Assert.That(AutonomousRealmRaid.Readable("Aidon the Archwizard"), Is.EqualTo("Aidon the Archwizard"), "a display name is left alone");
            Assert.That(AutonomousRealmRaid.Readable("HRimthursaKing"), Is.EqualTo("H Rimthursa King"));
            Assert.That(AutonomousRealmRaid.Readable(null), Is.Null);
        }

        // ---- the second wipe -------------------------------------------------------------------------------------------

        [Test]
        public void TheSecondWipeAtAidonWasARealWipe()
        {
            // REALM_EXPEDITION_WIPE failures=2 engaged=65 fallen=65 health=100 lowest=85: everyone in the attempt fell (72 deaths
            // to Aidon and his illusions between 23:28:54 and 23:31:15). The raid's 76 "living" were the 15 not in it and the
            // fallen back on their feet at the staging post outside (REALM_RAID_RETURN_WAVE outside=54, 10 s before).
            Assert.That(RealmRaidDungeonRoute.IsResetWithRaidStanding(65, 65, 100, 85), Is.False);
            Assert.That(RealmRaidDungeonRoute.IsWipe(65, 65, 100, 85, RealmRaidDungeonRoute.AttemptRaidSize(56, 80)), Is.True);
        }

        [Test]
        public void TheFirstWipeAtAidonWasHisTetherHealNotTheRaidFalling()
        {
            // REALM_EXPEDITION_WIPE failures=1 engaged=56 fallen=21 health=100 lowest=58: he was chased out of his tether and
            // healed in combat (REALM_EXPEDITION_TARGET_HEALED from=58 to=100 fromSpawn=1829). 21 of 56 fell: more than the
            // quarter a free reset allows, so by the rules of #195 it is an attempt. The fix is the lead-home rule below, not the
            // scoring.
            Assert.That(RealmRaidDungeonRoute.IsResetWithRaidStanding(56, 21, 100, 58), Is.False);
            Assert.That(RealmRaidDungeonRoute.IsWipe(56, 21, 100, 58, 56), Is.True);
        }

        [Test]
        public void TheWipeLineSaysWhoWasInTheAttempt()
        {
            Assert.That(AutonomousRealmRaid.WipeLine("summoners-albion", "Aidon the Archwizard", 2, 80, 76, 65, 65, 58),
                Is.EqualTo("REALM_RAID_WIPE event=summoners-albion boss=\"Aidon the Archwizard\" failures=2 support=80 living=76 " +
                           "engaged=65 fallen=65 minutes=58"));
            // A dragon's wipe has no such count: the line is as before.
            Assert.That(AutonomousRealmRaid.WipeLine("dragon-albion", "Golestandt", 1, 120, 30, -1, -1, 20),
                Is.EqualTo("REALM_RAID_WIPE event=dragon-albion boss=\"Golestandt\" failures=1 support=120 living=30 minutes=20"));
        }

        // ---- Aidon's fight at home -------------------------------------------------------------------------------------

        [Test]
        public void TheRaiderAidonChasesOutOfHisHomeLeadsHimBackWhateverItDoes()
        {
            // Fight at home 1,100 (hand notes), tether 1,500. A friar healing from the muster 1,400 out, chased at 1,416.
            Assert.That(EpicEncounterTactics.LeadsHome(1_100, 1_416, true, true, false), Is.True);
            // Past his tether (immune, healing): still led home, the only way the fight resumes.
            Assert.That(EpicEncounterTactics.LeadsHome(1_100, 2_109, true, true, false), Is.True);
            // At home the fight goes on.
            Assert.That(EpicEncounterTactics.LeadsHome(1_100, 1_019, true, true, false), Is.False);
            // Somebody else he is after, a boss already walking home, another region, a boss with no fight-at-home rule.
            Assert.That(EpicEncounterTactics.LeadsHome(1_100, 1_416, false, true, false), Is.False);
            Assert.That(EpicEncounterTactics.LeadsHome(1_100, 1_416, true, true, true), Is.False);
            Assert.That(EpicEncounterTactics.LeadsHome(1_100, 1_416, true, false, false), Is.False);
            Assert.That(EpicEncounterTactics.LeadsHome(0, 5_000, true, true, false), Is.False);
        }
    }
}
