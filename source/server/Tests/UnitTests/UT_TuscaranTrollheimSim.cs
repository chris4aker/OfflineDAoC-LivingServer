using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using P = DOL.GS.RealmRaidRecruitmentPolicy;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2.1 raid simulation pass (owner, 2026-11-22): Tuscaran Glacier's twins (#409) and Trollheim's stall
    /// clock at Nosdoden (#411). Deterministic models of the fights with the real rule and stall-clock code deciding; no
    /// world tick, combat, spell or pathing runs, and nothing here was seen on the owner's PC. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_TuscaranTrollheimSim
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 900;
        private EpicTestServerScope _server;
        private Func<GameNPC, bool> _kulaLives;

        public class Boss : GameEpicBoss
        {
            public bool Dead;
            public override bool IsAlive => !Dead;
            public override bool InCombat => true;
            public override byte HealthPercent => 100;
        }
        public sealed class KingTuscar : Boss { }
        public sealed class QueenKula : Boss { }
        public sealed class Pet : GameNPC
        {
            public override bool IsAlive => true;
        }
        public sealed class Raider : GameNPC
        {
            public override bool IsAlive => true;
        }
        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override bool IsAlive => true;
            public override ICharacterClass CharacterClass => new ClassSpiritmaster();
        }

        private KingTuscar _king;
        private QueenKula _queen;

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 66, Name = "Twins test", Description = "Twins test", Mobs = [] });
            EpicEncounterMechanics.ResetPerEncounter();
            EpicEncounterMechanics.AllowTuscarTwinHeal = true;
            _kulaLives = T.KulaLivesBeside;
            // The twins' spawns (KingTuscarAndQueenKula, the tests of UT_EpicWave2MidHib): 414 apart.
            _king = Npc<KingTuscar>(34542, 57121, 11881);
            _queen = Npc<QueenKula>(34136, 57202, 12025);
            T.KulaLivesBeside = _ => _queen.IsAlive;
        }

        [TearDown]
        public void TearDown()
        {
            T.KulaLivesBeside = _kulaLives;
            EpicEncounterMechanics.ResetPerEncounter();
            EpicEncounterMechanics.AllowTuscarTwinHeal = true;
            _server.Dispose();
        }

        private TNpc Npc<TNpc>(int x, int y, int z) where TNpc : GameNPC
        {
            TNpc npc = (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)?.SetValue(npc, new ArrayList());
            typeof(GameNPC).GetField("m_spells", Hidden)?.SetValue(npc, new List<Spell>());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)?.SetValue(npc, new PropertyCollection());
            typeof(GameLiving).GetField("_abilitiesLock", Hidden)?.SetValue(npc, new Lock());
            typeof(GameLiving).GetField("m_abilities", Hidden)?.SetValue(npc, new Dictionary<string, Ability>());
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            npc.X = x; npc.Y = y; npc.Z = z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D(x, y, z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        private Pet PetOf(Bot owner, int x, int y)
        {
            var pet = Npc<Pet>(x, y, 12000);
            pet.MeleeDamageType = eDamageType.Crush;
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(pet, new ControlledMobBrain(owner) { Body = pet });
            return pet;
        }

        // ---- #409: Queen Kula's twin heal ---------------------------------------------------------------------------

        [Test]
        public void WhileKulaLivesNoBotsPetAndNoOtherBotsHitOrAreaSpellLandsOnTheKing()
        {
            var tank = Npc<Bot>(34600, 57150, 11881);
            var caster = Npc<Bot>(34000, 57900, 12025);
            _king.TargetObject = tank;
            var pet = PetOf(caster, 34150, 57210);
            Assert.Multiple(() =>
            {
                Assert.That(T.PetMustNotHit(pet, _king), Is.True, "no pet attacks him or defends its owner against him");
                Assert.That(T.PetMustNotHit(pet, _queen), Is.False, "pets fight Kula");
                Assert.That(T.MustNotHit(caster, _king), Is.True, "no other bot hits him: an area spell at Kula that reaches him is refused");
                Assert.That(T.MustNotHit(tank, _king), Is.False, "his own target keeps him");
                Assert.That(T.MustNotHit(caster, _queen), Is.False);
                Assert.That(GameBot.RefusesAttackOn(caster, _king), Is.True, "and starts no attack on him");
            });
            _queen.Dead = true;
            Assert.That(T.PetMustNotHit(pet, _king), Is.False, "Kula dead: pets fight him");
            Assert.That(T.MustNotHit(caster, _king), Is.False, "Kula dead: everyone fights him");
            _queen.Dead = false;
            EpicEncounterMechanics.AllowTuscarTwinHeal = false;
            Assert.That(T.PetMustNotHit(pet, _king), Is.False, "twin heal off: as in 1.1");
            Assert.That(T.MustNotHit(caster, _king), Is.False, "twin heal off: as in 1.1");
        }

        [Test]
        public void OnceKulaIsDeadCastersAndHealersKeepClearOfTheKingsHammers()
        {
            var tank = Npc<Raider>(34600, 57150, 11881);
            _king.TargetObject = tank;
            _queen.Dead = true;
            var live = new List<GameNPC> { _king, _queen };
            var healer = Npc<Raider>(34800, 57400, 11881); // 381 from his target
            int healerClass = (int)eCharacterClass.Healer;
            var a = T.Decide(healer, _king, live, 0, false, 1_000, healerClass);
            Assert.That(a.Action, Is.EqualTo(T.BotAction.Reposition));
            Assert.That(Vector2.Distance(new(a.StandAt.Value.X, a.StandAt.Value.Y), new(tank.X, tank.Y)),
                Is.EqualTo(T.TuscarHammerRadius + T.SpreadStepOut).Within(1));
            var far = Npc<Raider>(35400, 57900, 11881);
            Assert.That(T.Decide(far, _king, live, 0, false, 1_000, healerClass).Action, Is.EqualTo(T.BotAction.None), "already clear");
            var melee = Npc<Raider>(34500, 57100, 11881);
            Assert.That(T.Decide(melee, _king, live, 0, false, 1_000, (int)eCharacterClass.Warrior).Action, Is.EqualTo(T.BotAction.None),
                "melee fight on");
        }

        /// <summary>
        /// The Kula phase as a model. Every point the raid deals the King while she lives heals her (KingTuscar.TakeDamage), so her
        /// net loss is the raid's damage on her less twice its damage on him. The raid's rate is calibrated on Icelord Kvasir (100,000
        /// health, the same 40% melee and 70% magic resists): the Midgard raid killed him in 7.3 and 5.3 minutes on the owner's PC
        /// (ccba364, 19e7037), 269 per second at the midpoint. A 128-raider Midgard raid of 16 parties (warrior, thane, berserker,
        /// savage, hunter, healer, shaman and a runemaster, spiritmaster or bonedancer in turn) brings 36 pets; a pet is taken at half
        /// a raider's damage. <paramref name="petsOnKing"/> is the share of the pets that turn on the King when he or his hammers hit
        /// them or their owners (the period guide: "Kill all pets, because pets can aggro the King and Queen"); it is not measured.
        /// The real rule decides which pets may land on him. Kula is 300,000 health.
        /// </summary>
        private double KulaMinutes(double petsOnKing, bool guarded)
        {
            const double raidRate = 100_000 / (6.2 * 60), kulaHealth = 300_000;
            var tank = Npc<Bot>(34600, 57150, 11881);
            _king.TargetObject = tank;
            int raiders = 0;
            var pets = new List<Pet>();
            for (int party = 0; party < 16; party++)
            {
                raiders += 8;
                var hunter = Npc<Bot>(33900, 57600 + party, 12025);
                pets.Add(PetOf(hunter, 34150, 57210 + party));
                var caster = Npc<Bot>(33600, 57900 + party, 12025);
                int petsOfCaster = (party % 3) switch { 1 => 1, 2 => 3, _ => 0 }; // runemaster, spiritmaster, bonedancer
                for (int i = 0; i < petsOfCaster; i++) pets.Add(PetOf(caster, 34140 + i, 57200 + party));
            }
            double unit = raidRate / (raiders + 0.5 * pets.Count);
            // His own target hits him (it keeps him); a pet on him only where the rule lets it.
            int petsLanding = pets.Count(p => !guarded || !T.PetMustNotHit(p, _king));
            double onKing = unit * (T.MustNotHit(tank, _king) ? 0 : 1) + petsOnKing * petsLanding * 0.5 * unit;
            double net = raidRate - 2 * onKing;
            Assert.That(pets, Has.Count.EqualTo(36));
            return kulaHealth / net / 60;
        }

        [TestCase(0.25)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void TheKulaPhaseLosesNothingToPetsOnTheKing(double petsOnKing)
        {
            double clean = 300_000 / (100_000 / (6.2 * 60)) / 60; // 18.6 minutes
            double unguarded = KulaMinutes(petsOnKing, guarded: false), now = KulaMinutes(petsOnKing, guarded: true);
            TestContext.Out.WriteLine($"kula petsOnKing={petsOnKing}: unguarded {unguarded:0.0} min, with the rule {now:0.0} min, no leak {clean:0.0} min");
            Assert.That(now, Is.LessThan(clean * 1.02), "only his target's hits heal her");
            Assert.That(unguarded, Is.GreaterThan(now * 1.05), "without the rule the pets' hits heal her");
        }

        // ---- #411: Trollheim, the stall clock after a wipe at Nosdoden ---------------------------------------------------

        /// <summary>A small fixed generator, so the runs are the same on every machine (and in the Python mirror).</summary>
        private sealed class Lcg
        {
            private long _x;
            public Lcg(int seed) => _x = (seed * 2654435761L + 12345) & 0xffffffffL;
            public double Next()
            {
                _x = (_x * 1103515245L + 12345) & 0x7fffffffL;
                return _x / (double)0x7fffffff;
            }
        }

        /// <summary>The stall rule before 1.2.1 (AutonomousRealmRaid.WatchProgress on ccba364): the target's low is kept across a
        /// wipe and a retry pull is no progress.</summary>
        private static (P.ProgressWatch, bool) LegacyStep(P.ProgressWatch b, int named, int wipes, int retries, string target, int health)
        {
            bool progress = P.MadeProgress(b.Named, named, b.Wipes, wipes, b.Target, target, b.Lowest, health);
            int lowest = target != b.Target ? health : Math.Min(b.Lowest, health);
            return (new P.ProgressWatch(named, wipes, retries, target, lowest, false), progress);
        }

        /// <summary>
        /// Nosdoden as a model (Nosdoden.cs): 100,000 health; at each 10% from 90% a wave of 12-18 essences of 2,500 (resists 15%
        /// melee, 25% magic: about 1.8 times the raid's rate on him); while a wave lives the route's target is an essence (no
        /// named), then him again. The raid deals him 150-220 a second (64 raiders, Kvasir's calibration scaled). Attempt 1 wipes
        /// at 15-45%; he resets to full. The regroup takes 45-59 minutes: the owner's log (ccba364) shows no kill, no new low and
        /// no wipe in the hour after the wipe, so the retry, if any came, came late in it. Attempt 2 is modelled as a win. Each
        /// 10 s the stall rule looks; the raid is Clocked (60 minutes), no return wave has headway.
        /// </summary>
        private static (bool Killed, double Minutes, double LongestGap) Nosdoden(int seed, bool legacy)
        {
            var g = new Lcg(seed);
            double rate = 150 + g.Next() * 70;
            int wipeLow = 15 + (int)(g.Next() * 31);
            long regroup = (long)((45 + g.Next() * 14) * 60_000);
            const double max = 100_000;
            const long step = 10_000;
            double health = max, wave = 0;
            long now = 0, last = 0, regroupEnd = 0, gap = 0;
            var watch = P.ProgressWatch.Start;
            int named = 0, wipes = 0, retries = 0, attempt = 1, nextWave = 90;
            string phase = "fight";
            while (now < 8 * 3_600_000L)
            {
                now += step;
                string target;
                int pct;
                if (phase == "fight")
                {
                    health -= rate * step / 1000;
                    pct = Math.Max(0, (int)(health * 100 / max));
                    if (health <= 0) { named = 1; pct = 101; }
                    else if (attempt == 1 && pct <= wipeLow)
                    {
                        wipes = 1; health = max; pct = 100; phase = "regroup"; regroupEnd = now + regroup; nextWave = 90;
                    }
                    else if (pct <= nextWave && nextWave >= 10)
                    {
                        wave = (12 + (int)(g.Next() * 7)) * 2_500.0; phase = "wave"; nextWave -= 10;
                    }
                    target = named > 0 || phase == "wave" ? null : "Nosdoden";
                }
                else if (phase == "wave")
                {
                    wave -= rate * 1.8 * step / 1000;
                    target = null; pct = 101;
                    if (wave <= 0) { phase = "fight"; target = "Nosdoden"; pct = Math.Max(0, (int)(health * 100 / max)); }
                }
                else
                {
                    target = "Nosdoden"; pct = 100;
                    if (now >= regroupEnd) { phase = "fight"; attempt = 2; retries = 1; }
                }
                bool progress;
                if (legacy) (watch, progress) = LegacyStep(watch, named, wipes, retries, target, pct);
                else
                {
                    var r = P.WatchStep(watch, named, wipes, retries, target, pct);
                    watch = r.Next; progress = r.Progress;
                }
                if (progress) { gap = Math.Max(gap, now - last); last = now; }
                if (named > 0) return (true, now / 60_000.0, gap / 60_000.0);
                if (RealmRaidReturnWave.Stalled(P.StalledMilliseconds, now, last, 0)) return (false, now / 60_000.0, gap / 60_000.0);
            }
            return (false, now / 60_000.0, gap / 60_000.0);
        }

        [Test]
        public void ARetryAfterAWipeAtNosdodenIsNotEndedByTheStallClock()
        {
            const int runs = 200;
            var before = Enumerable.Range(1, runs).Select(s => Nosdoden(s, legacy: true)).ToArray();
            var after = Enumerable.Range(1, runs).Select(s => Nosdoden(s, legacy: false)).ToArray();
            int lostBefore = before.Count(r => !r.Killed), lostAfter = after.Count(r => !r.Killed);
            double worstGap = after.Max(r => r.LongestGap);
            TestContext.Out.WriteLine($"nosdoden: before {lostBefore}/{runs} timed out, after {lostAfter}/{runs}; " +
                                      $"kill {after.Min(r => r.Minutes):0}-{after.Max(r => r.Minutes):0} min; longest gap {worstGap:0.0} of 60");
            Assert.That(lostBefore, Is.GreaterThan(0), "the old rule ends a late retry: the ccba364 time-out");
            Assert.That(lostAfter, Is.Zero);
            Assert.That(worstGap, Is.LessThan(P.StalledMilliseconds / 60_000.0), "the longest quiet spell is the regroup itself");
        }

        [Test]
        public void TheRetrysLowAndPullCountOncePerWipe()
        {
            var w = P.ProgressWatch.Start;
            (w, _, _) = P.WatchStep(w, 0, 0, 0, "Nosdoden", 100);
            var r = P.WatchStep(w, 0, 0, 0, "Nosdoden", 40);
            Assert.That(r.Progress, Is.True);
            w = r.Next;
            r = P.WatchStep(w, 0, 1, 0, "Nosdoden", 100); // the wipe: he is back at full
            Assert.That((r.Progress, r.LowReset, r.Next.Lowest), Is.EqualTo((true, true, 100)));
            w = r.Next;
            Assert.That(P.WatchStep(w, 0, 1, 0, "Nosdoden", 100).Progress, Is.False, "waiting at full is no progress");
            r = P.WatchStep(w, 0, 1, 1, "Nosdoden", 100);
            Assert.That(r.Progress, Is.True, "the retry pull");
            w = r.Next;
            Assert.That(P.WatchStep(w, 0, 1, 1, "Nosdoden", 99).Progress, Is.True, "the retry's first percent");
            // A heal to full in a fight with no wipe re-arms nothing.
            var x = P.WatchStep(P.WatchStep(w, 0, 1, 1, "Nosdoden", 60).Next, 0, 1, 1, "Nosdoden", 100);
            Assert.That((x.LowReset, x.Next.Lowest), Is.EqualTo((false, 60)));
            // A wipe counted while he is still being fought low waits for him to reset.
            var y = P.WatchStep(P.WatchStep(w, 0, 1, 1, "Nosdoden", 50).Next, 0, 2, 1, "Nosdoden", 45);
            Assert.That((y.LowReset, y.Next.Lowest, y.Next.LowRearmed), Is.EqualTo((false, 45, true)));
            Assert.That(P.WatchStep(y.Next, 0, 2, 1, "Nosdoden", 97).LowReset, Is.True);
        }
    }
}
