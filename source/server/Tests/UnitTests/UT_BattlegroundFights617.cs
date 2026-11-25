using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #617 and FarmVibes' crowd-control finding on #600: how autonomous gamebots pick fights in a battleground. People rank
    /// above siege engines, an attacking crew leaves its engine for an enemy person in fight range, the rally takes a
    /// contact together, an enemy in sight holds the maintenance pulse, and a mezzed or breakably rooted enemy waits while a
    /// free one is in the fight. Field RvR picks are unchanged. Tests only.
    /// </summary>
    [TestFixture]
    public sealed class UT_BattlegroundFights617
    {
        private sealed record Candidate(string Name, bool Engine = false, bool Controlled = false);

        private static readonly Candidate Ram = new("ram", Engine: true);
        private static readonly Candidate Player = new("player");
        private static readonly Candidate Mezzed = new("mezzed", Controlled: true);
        private static readonly Candidate Free = new("free");

        private static Candidate[] Arrange(bool battleground, params Candidate[] candidates) =>
            AutonomousBattlegroundTargets.Arrange(battleground, candidates, c => c.Engine, c => c.Controlled);

        [Test]
        public void APlayerIsPickedOverAnEngine() => Assert.Multiple(() =>
        {
            Assert.That(Arrange(true, Ram, Player), Is.EqualTo(new[] { Player }), "the engine waits while a person is in reach");
            Assert.That(Arrange(true, Ram), Is.EqualTo(new[] { Ram }), "an engine alone is still taken");
            Assert.That(AutonomousBattlegroundTargets.Rank(true, true), Is.GreaterThan(AutonomousBattlegroundTargets.Rank(true, false)),
                "the scan looks at people before engines");
        });

        [Test]
        public void AnAttackingCrewReleasesItsEngine() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundTargets.SiegeCrewReleases(true, true, false, true, 900), Is.True,
                "an enemy person within fight range");
            Assert.That(AutonomousBattlegroundTargets.SiegeCrewReleases(true, true, false, true, 1_200), Is.False, "beyond fight range");
            Assert.That(AutonomousBattlegroundTargets.SiegeCrewReleases(true, true, false, false, 300), Is.False, "an enemy engine");
            Assert.That(AutonomousBattlegroundTargets.SiegeCrewReleases(true, true, true, true, 300), Is.False,
                "defending operators keep #525's rule (SiegeOperatorMayEngage)");
            Assert.That(AutonomousBattlegroundTargets.SiegeCrewReleases(true, false, false, true, 300), Is.False, "no crew");
            // before the clause the same crew kept its engine for a person at 900 (#525: only within 450)
            Assert.That(AutonomousWorldBotController.SiegeOperatorMayEngage(true, false, false, false, false), Is.False);
        });

        [Test]
        public void AMezzedEnemyIsSkippedWhileAFreeOneExistsAndPickedWhenAlone() => Assert.Multiple(() =>
        {
            Assert.That(Arrange(true, Mezzed, Free), Is.EqualTo(new[] { Free }));
            Assert.That(Arrange(true, Mezzed), Is.EqualTo(new[] { Mezzed }), "the only one left is broken to go on");
            Assert.That(AutonomousBattlegroundTargets.SkipsControlledPick(true, true), Is.True);
            Assert.That(AutonomousBattlegroundTargets.SkipsControlledPick(true, false), Is.False);
            Assert.That(AutonomousBattlegroundTargets.SkipsControlledPick(false, true), Is.False);
        });

        [Test]
        public void FieldRvrPicksAreUnchanged() => Assert.Multiple(() =>
        {
            Candidate[] field = [Mezzed, Ram, Player, Free];
            Assert.That(Arrange(false, field), Is.SameAs(field), "outside a battleground the scan's candidates are untouched");
            Assert.That(AutonomousBattlegroundTargets.Rank(false, true), Is.EqualTo(AutonomousBattlegroundTargets.Rank(false, false)),
                "the field order key is constant");
            Assert.That(AutonomousBattlegroundTargets.SiegeCrewReleases(false, true, false, true, 300), Is.False,
                "field crews keep their engines");
            Assert.That(AutonomousBattlegroundTargets.HoldsMaintenance(true, false, true), Is.False, "field upkeep unchanged");
        });

        [Test]
        public void NoSelfBuffWithAnEnemyInSight() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundTargets.HoldsMaintenance(true, true, true), Is.True);
            Assert.That(AutonomousBattlegroundTargets.HoldsMaintenance(true, true, false), Is.False, "nobody in sight: upkeep as before");
            Assert.That(AutonomousBattlegroundTargets.HoldsMaintenance(false, true, true), Is.False, "companions keep their upkeep");
        });

        [Test]
        public void ARallyContactIsAnEnemyHeadingForTheRallyOrFightingNearIt() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousBattlegroundRallyContact.Approaching(true, 1000, 1200), Is.True);
            Assert.That(AutonomousBattlegroundRallyContact.Approaching(true, 4000, 100), Is.True, "wraps round north");
            Assert.That(AutonomousBattlegroundRallyContact.Approaching(true, 0, 2048), Is.False, "walking away");
            Assert.That(AutonomousBattlegroundRallyContact.Approaching(false, 1000, 1000), Is.False, "standing still");
            Assert.That(AutonomousBattlegroundRallyContact.IsContact(true, 1_800, true, false, float.MaxValue), Is.True);
            Assert.That(AutonomousBattlegroundRallyContact.IsContact(true, 2_500, true, false, float.MaxValue), Is.False);
            Assert.That(AutonomousBattlegroundRallyContact.IsContact(true, 2_500, false, true, 1_200), Is.True, "a fight near the rally");
            Assert.That(AutonomousBattlegroundRallyContact.IsContact(false, 500, true, true, 100), Is.False, "never an engine");
        });

        /// <summary>The same rules on real gamebots in a battleground region of the fight harness (#520).</summary>
        [Test, NonParallelizable]
        public void OnRealBots_TheRallyEngagesTogetherAndAMezzedEnemyWaits()
        {
            AutonomousBattlegroundRallyContact.Clear();
            using var world = new RvrFightWorld(617, RvrFightRehearsal.Repeatable);
            Region region = world.AddRegion(RvrFightRehearsal.BattlegroundRegion, RvrFightRehearsal.BattlegroundZone, "Caledonia flat stand-in (rehearsal)");
            const int x = 30_000, y = 30_000;
            var rally = new Vector3(x, y, 0);
            List<GameBot> rallied = Enumerable.Range(0, 4).Select(i =>
            {
                GameBot bot = world.NewBot(eRealm.Albion, eCharacterClass.Armsman, 30, $"R{i}", "Battleground");
                world.Place(bot, region, x + i * 60, y, 0, 3072);
                return bot;
            }).ToList();
            GameBot mezzed = world.NewBot(eRealm.Midgard, eCharacterClass.Warrior, 30, "Mezzed", "Battleground");
            GameBot free = world.NewBot(eRealm.Midgard, eCharacterClass.Savage, 30, "Free", "Battleground");
            world.Place(mezzed, region, x + 300, y, 0, 1024);
            world.Place(free, region, x + 900, y, 0, 1024);
            var ram = new GameSiegeRam { Realm = eRealm.Midgard };
            try
            {
                long now = world.Now;
                // one looker posts the free enemy; every bot at the rally reads it back, a rally elsewhere does not
                Assert.That(AutonomousBattlegroundRallyContact.Post(region.ID, eRealm.Albion, rally, free, now), Is.True);
                Assert.That(AutonomousBattlegroundRallyContact.Post(region.ID, eRealm.Albion, rally + new Vector3(50, 0, 0), free, now), Is.False,
                    "the same contact at the same rally is not news");
                Assert.Multiple(() =>
                {
                    foreach (GameBot bot in rallied)
                        Assert.That(AutonomousBattlegroundRallyContact.Has(region.ID, bot.Realm, new(bot.X, bot.Y, 0), free, now), Is.True, bot.Name);
                    Assert.That(AutonomousBattlegroundRallyContact.Has(region.ID, eRealm.Albion, rally + new Vector3(3_000, 0, 0), free, now), Is.False,
                        "another rally");
                    Assert.That(AutonomousBattlegroundRallyContact.Has(region.ID, eRealm.Hibernia, rally, free, now), Is.False, "another realm");
                    Assert.That(AutonomousBattlegroundRallyContact.Has(region.ID, eRealm.Albion, rally, free,
                        now + AutonomousBattlegroundRallyContact.ContactMilliseconds + 1), Is.False, "a contact lapses");
                });

                GameBot looker = rallied[0];
                mezzed.IsMezzed = true;
                bool Controlled(GameLiving target) => AutonomousWorldBotController.HeldByCrowdControl(looker, target);
                GameLiving[] Arranged(bool battleground, params GameLiving[] candidates) =>
                    AutonomousBattlegroundTargets.Arrange(battleground, candidates, target => target is GameSiegeWeapon, Controlled);
                Assert.Multiple(() =>
                {
                    Assert.That(Controlled(mezzed), Is.True, "mesmerized");
                    Assert.That(Controlled(free), Is.False);
                    Assert.That(Controlled(ram), Is.False, "an engine is never crowd-controlled");
                    Assert.That(Arranged(true, mezzed, free, ram), Is.EqualTo(new GameLiving[] { free }), "the free person first");
                    Assert.That(Arranged(true, mezzed, ram), Is.EqualTo(new GameLiving[] { mezzed }),
                        "the mezzed person before the engine, broken when it is the only person");
                    Assert.That(Arranged(true, ram), Is.EqualTo(new GameLiving[] { ram }));
                    GameLiving[] field = [mezzed, ram, free];
                    Assert.That(Arranged(false, field), Is.SameAs(field), "field picks unchanged");
                });
                mezzed.IsMezzed = false;
                Assert.That(Controlled(mezzed), Is.False, "awake again");
            }
            finally
            {
                AutonomousBattlegroundRallyContact.Clear();
            }
        }
    }
}
