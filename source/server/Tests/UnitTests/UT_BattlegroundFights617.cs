using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using DOL.AI.Brain;
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
        // The fight harness reads every server logger (RvrFightWorld's error capture touches each type's static Logger).
        // Logging must be up first: a logger made before it is a null one, and every later fixture that logs through it
        // throws (CI on 5b4e9f990: 53 unrelated tests). Same set-up as the other fixtures that need logging.
        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp, TearDown]
        public void ForgetRallyContacts() => AutonomousBattlegroundRallyContact.Clear();

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

    /// <summary>
    /// #617, FarmVibes' review on PR #621: the server's own battleground scan (TryEngageFrontierThreat) and controller pick on
    /// real gamebots in the fight harness's Caledonia stand-in (#520). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_BattlegroundFights617Harness
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
        private const int X = 30_000, Y = 30_000;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        [SetUp, TearDown]
        public void Forget()
        {
            AutonomousBattlegroundRallyContact.Clear();
            Waves().Remove((RvrFightRehearsal.BattlegroundRegion, eRealm.Albion));
        }

        private static IDictionary Waves() =>
            (IDictionary)typeof(AutonomousBattlegroundWave).GetField("Waves", Hidden)!.GetValue(null);

        /// <summary>Albion's wave gathers at <paramref name="staging"/> (as AutonomousBattlegroundWave.ShouldGather leaves it).</summary>
        private static void Gathering(Vector3 staging)
        {
            System.Type waveType = typeof(AutonomousBattlegroundWave).GetNestedType("Wave", BindingFlags.NonPublic)!;
            object wave = System.Activator.CreateInstance(waveType, nonPublic: true)!;
            waveType.GetField("Staging")!.SetValue(wave, (Vector3?)staging);
            Waves()[(RvrFightRehearsal.BattlegroundRegion, eRealm.Albion)] = wave;
        }

        private static AutonomousWorldBotController GathererController(Vector3 staging)
        {
            var controller = new AutonomousWorldBotController();
            typeof(AutonomousWorldBotController).GetField("_battlegroundGatherStaging", Hidden)!.SetValue(controller, (Vector3?)staging);
            return controller;
        }

        private static (RvrFightWorld World, Region Region) Caledonia(IReadOnlyDictionary<string, object> extra = null)
        {
            var settings = new Dictionary<string, object>(RvrFightRehearsal.Repeatable) { ["rvr_bot_perception"] = false };
            if (extra != null)
                foreach (var (key, value) in extra)
                    settings[key] = value;
            var world = new RvrFightWorld(617, settings);
            return (world, world.AddRegion(RvrFightRehearsal.BattlegroundRegion, RvrFightRehearsal.BattlegroundZone, "Caledonia flat stand-in (rehearsal)"));
        }

        private static GameBot Bot(RvrFightWorld world, Region region, eRealm realm, string name, int x, int y, ushort heading = 0)
        {
            GameBot bot = world.NewBot(realm, realm == eRealm.Albion ? eCharacterClass.Armsman : eCharacterClass.Warrior, 30, name, "Battleground");
            Assert.That(world.Place(bot, region, x, y, 0, heading), Is.True, name + " placed");
            return bot;
        }

        [Test]
        public void ABotAlreadyOnARam_SwitchesToAPlayerInReach()
        {
            var (world, region) = Caledonia();
            using (world)
            {
                GameBot attacker = Bot(world, region, eRealm.Albion, "OnRam", X, Y);
                var ram = new GameSiegeRam { Realm = eRealm.Midgard, Level = 30 };
                Assert.That(world.Place(ram, region, X + 150, Y), Is.True, "ram placed");
                ram.Health = ram.MaxHealth;
                GameBot person = Bot(world, region, eRealm.Midgard, "Walker", X + 700, Y, 1024);
                world.Tick(); // the zones file what was placed
                var brain = (BotBrain)attacker.Brain;
                attacker.TargetObject = ram;
                brain.AddToAggroList(ram, 500);
                Assert.That(brain.HasAggro, Is.True, "already fighting the ram: the old gate skipped the scan here");

                bool engaged = new AutonomousWorldBotController().TryEngageFrontierThreat(brain);

                Assert.Multiple(() =>
                {
                    Assert.That(engaged, Is.True, "the scan ran past the gate");
                    Assert.That(attacker.TargetObject, Is.SameAs(person), "the person, not the ram");
                    Assert.That(brain.GetBaseAggroAmount(ram), Is.Zero, "the ram is off the aggro list");
                });
            }
        }

        [Test]
        public void ASkippedMezzedGuard_LeadsToTheFreeEnemy_NotTheDoor()
        {
            var (world, region) = Caledonia();
            using (world)
            {
                GameBot stormer = Bot(world, region, eRealm.Albion, "Stormer", X, Y);
                RvrFightRehearsal.RehearsalLord guard = RvrFightRehearsal.RehearsalLord.Make(region);
                guard.Realm = eRealm.Midgard;
                guard.Level = 30;
                Assert.That(world.Place(guard, region, X + 200, Y), Is.True, "guard placed");
                GameBot free = Bot(world, region, eRealm.Midgard, "Free", X + 800, Y, 1024);
                world.Tick(); // the zones file what was placed
                guard.IsMezzed = true;
                Assert.Multiple(() =>
                {
                    Assert.That(AutonomousWorldBotController.BattlegroundPickPastControl(stormer, guard), Is.SameAs(free),
                        "the free enemy, never null (null fell through to the gate)");
                    Assert.That(AutonomousWorldBotController.BattlegroundPickPastControl(stormer, free), Is.SameAs(free));
                });
                free.MoveTo(region.ID, X + 6_000, Y, 0, 1024);
                world.Tick();
                Assert.That(AutonomousWorldBotController.BattlegroundPickPastControl(stormer, guard), Is.SameAs(guard),
                    "the only one left is broken to go on");
                guard.IsMezzed = false;
            }
        }

        [Test]
        public void GatherersWithDistantDefendersInSight_StillBuff_ButNotWithAnEnemyTheyMayFight()
        {
            var (world, region) = Caledonia();
            using (world)
            {
                var staging = new Vector3(X, Y, 0);
                Gathering(staging);
                GameBot gatherer = Bot(world, region, eRealm.Albion, "Gatherer", X, Y);
                GameBot defender = Bot(world, region, eRealm.Midgard, "Defender", X + 2_200, Y, 1024);
                world.Tick(); // the zones file what was placed
                AutonomousWorldBotController controller = GathererController(staging);
                var brain = (BotBrain)gatherer.Brain;

                Assert.That(controller.TryEngageFrontierThreat(brain), Is.False, "a defender far off is not the gatherer's fight");
                Assert.That(controller.BattlegroundEnemyInSight, Is.False, "so upkeep goes on");

                defender.MoveTo(region.ID, X + 400, Y, 0, 1024);
                world.Tick();
                var again = GathererController(staging);
                Assert.That(again.TryEngageFrontierThreat(brain), Is.True, "an enemy at the staging point is fought");
                Assert.That(again.BattlegroundEnemyInSight, Is.True, "and holds the upkeep");
            }
        }

        [Test]
        public void EveryRalliedBot_LookersAndNonLookersAlike_EngagesWithinOneScanOfThePost()
        {
            var (world, region) = Caledonia(new Dictionary<string, object> { ["rvr_bot_group_lookers"] = true });
            using (world)
            {
                var staging = new Vector3(X, Y, 0);
                Gathering(staging);
                List<GameBot> rally = Enumerable.Range(0, 6).Select(i => Bot(world, region, eRealm.Albion, $"R{i}", X - 100 + i * 40, Y)).ToList();
                world.Form(rally.Cast<GameLiving>().ToList());
                // 1,800 out and walking at the rally: no gatherer's own rule (700, or firing on the realm) lets it be fought
                GameBot incoming = Bot(world, region, eRealm.Midgard, "Incoming", X + 1_800, Y, 1024);
                incoming.Heading = incoming.GetHeading(X, Y);
                incoming.WalkTo(staging, incoming.MaxSpeed);
                world.Tick(); // the zones file what was placed
                Assert.That(incoming.IsMoving, Is.True, "walking at the rally");

                // round one: whoever looks this turn (two lookers per group of six) sees it walking in and posts it
                // (stopping at the first, so every other bot must come in through the post alone)
                var engaged = new List<GameBot>();
                foreach (GameBot bot in rally)
                    if (GathererController(staging).TryEngageFrontierThreat((BotBrain)bot.Brain))
                    {
                        engaged.Add(bot);
                        break;
                    }
                Assert.That(engaged, Has.Count.EqualTo(1), "a looker saw it and posted it");
                Assert.That(AutonomousBattlegroundRallyContact.Has(region.ID, eRealm.Albion, staging, incoming, world.Now), Is.True);
                // it stops: from here on only the posted contact can bring the rest in
                incoming.StopMoving();
                Assert.Multiple(() =>
                {
                    foreach (GameBot bot in rally.Except(engaged))
                    {
                        Assert.That(GathererController(staging).TryEngageFrontierThreat((BotBrain)bot.Brain), Is.True,
                            bot.Name + " engages on its next scan, looker or not");
                        Assert.That(bot.TargetObject, Is.SameAs(incoming), bot.Name);
                    }
                    foreach (GameBot bot in engaged)
                        Assert.That(bot.TargetObject, Is.SameAs(incoming), bot.Name);
                });
            }
        }
    }
}
