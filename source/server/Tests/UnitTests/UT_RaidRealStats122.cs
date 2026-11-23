using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using Sim = DOL.UnitTests.UT_RaidSim121SummonersGreenKnight;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.2: the raid sims' assumed numbers replaced by real ones (docs/RAID-SIZES.md, "Real stats"). The bosses' rows come
    /// from the public OpenDAoC database (npctemplate: level, strength, quickness, intelligence; ReplaceMobValues), their
    /// scripts (health, armour, resists, spells, the stats a script sets) and the server's own formulas, written out here:
    /// <list type="bullet">
    /// <item>GameNPC.SetStats: with force_mob_autoset_stats (default True; the database sets no server property) a monster's
    /// STR, QUI and INT are 30 + (level - 1) unless its script sets them afterwards (the Green Knight's trees 150 / 80,
    /// Spindler's newborns 250 / 80, Legion his template's 200 / 100, Govannon's demon 800, Queen Kula 350 / 500);</item>
    /// <item>AttackComponent.AttackSpeed (NPC): NpcWeaponSpeed (30, two-handed 40) x 100 x (1 - (QUI - 60) / 500);</item>
    /// <item>AttackComponent.CalculateNpcDamage: (1 + L / 3.2 + L^2 / 150) x speed / 10 x DamageFactor (GameEpicBoss 2.25)
    /// x 1.1 two-handed; the cap 3 x that (x 1.5 an epic boss);</item>
    /// <item>GameNPC.GetWeaponSkill: L x 2.5 x (1 + (STR + 30) / 200), over the target's armour (CalculateTargetArmor:
    /// AF + level x 0.4, over 1 - absorb; GameBot.GetArmorAF: item AF capped at twice the level);</item>
    /// <item>GetMissChance: 18% less 1.33 a level above the target;</item>
    /// <item>SpellHandler.CalculateDamageBase (NPC): Damage x (1 + INT / 200), Mob_Spells variance 0.6-1.0 (DoTs: no level
    /// offset); DamageShieldSpellHandler: Damage x the attacker's swing in seconds, variance 0.75-1.25.</item>
    /// </list>
    /// Reference target: a level 50 bot in full-quality armour at the cap. "High": no buffs, no resists. "Low": a capped
    /// spec AF buff (+18.75) and 20% resists. Tests only: nothing here has been seen in a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_RaidRealStats122
    {
        private EpicTestServerScope _server;

        [SetUp]
        public void SetUp() => _server = new EpicTestServerScope();

        [TearDown]
        public void TearDown() => _server.Dispose();

        // ---- the formulas (see the class summary) ---------------------------------------------------------------------

        public static int Autoset(int level) => 30 + (level - 1);

        public static double SwingMs(int quickness, bool twoHand) => Math.Max(500, (twoHand ? 40 : 30) * 100 * (1.0 - (quickness - 60) / 500.0));

        public static double BaseDamage(int level, double damageFactor, bool twoHand)
        {
            double d = (1.0 + level / 3.2 + level * (double)level / 150.0) * (twoHand ? 40 : 30) * 0.1 * damageFactor;
            return twoHand ? d * 1.1 : d;
        }

        public static double WeaponSkill(int level, int strength) => Math.Max(1, level) * 2.5 * (1 + 0.01 * (strength + 30) / 2);

        /// <summary>Armour type: item AF (cap 100, cloth 50) and absorb (plate 34%, chain 27%, cloth 0).</summary>
        public static readonly Dictionary<string, (double Af, double Absorb)> Armour = new()
        {
            ["plate"] = (100, 0.34), ["chain"] = (100, 0.27), ["scale"] = (100, 0.19), ["leather"] = (100, 0.10), ["cloth"] = (50, 0.0),
        };

        public static double ArmourMod(string kind, bool buffed) =>
            Math.Max(1.0, (Armour[kind].Af + 50 * 0.4 + (buffed ? 18.75 : 0)) / (1 - Armour[kind].Absorb));

        public static double Hit(int level, int strength, double damageFactor, bool twoHand, string kind, bool low, bool epic)
        {
            double b = BaseDamage(level, damageFactor, twoHand);
            double cap = b * 3.0 * (epic ? 1.5 : 1.0);
            return Math.Min(b * WeaponSkill(level, strength) / ArmourMod(kind, low), cap) * (low ? 0.8 : 1.0);
        }

        public static double Miss(int level) => Math.Max(0.0, 18 - (level - 50) * (1 + 1 / 3.0)) / 100;

        public static double PerSecond(int level, int strength, int quickness, double damageFactor, bool twoHand, string kind, bool low, bool epic) =>
            Hit(level, strength, damageFactor, twoHand, kind, low, epic) * (1 - Miss(level)) / (SwingMs(quickness, twoHand) / 1000.0);

        // ---- the real numbers the sims use (rounded as the Python mirror passes them) ----------------------------------

        /// <summary>A tree's hit a second by its target's role (level 48, STR 150, QUI 80): tank plate, melee and healers
        /// chain, casters cloth.</summary>
        public static readonly IReadOnlyDictionary<string, double> TreeHigh = new Dictionary<string, double>
            { ["tank"] = 32.5, ["melee"] = 35.9, ["caster"] = 77.7, ["healer"] = 35.9 };
        public static readonly IReadOnlyDictionary<string, double> TreeLow = new Dictionary<string, double>
            { ["tank"] = 22.5, ["melee"] = 24.9, ["caster"] = 53.3, ["healer"] = 24.9 };

        /// <summary>The Green Knight on his target (plate), a second, and his swing (level 79, autoset STR / QUI 108, two-handed).</summary>
        public const double GreenKnightHigh = 338, GreenKnightLow = 234;
        public const int GreenKnightSwingMs = 3_616;

        /// <summary>Summoner Roesia (level 75, autoset 104, two-handed): 200 a second on plate in the low case, a half second
        /// 100; her damage shield 80 x 3.5 s = 280 a swing, 224 after 20% heat resists; the healer's effective heal
        /// recalibrated so that a raid without cures falls as on b1b6846 with her real melee (the C# model's 400 fell in 0.7
        /// min against b1b6846's 1.5).</summary>
        public const int RoesiaTankHitLow = 100, RoesiaDsHitLow = 224, RoesiaHealReal = 730;

        /// <summary>Marfoirwort's swing (level 80, autoset QUI 109, no weapon), in seconds; his hit chance on a level 50 is
        /// 100% (the 1.2.1 model assumed 85% and tried 3.0-4.5 s).</summary>
        public const double MarfoirwortSwing = 2.706;

        [Test]
        public void TheNumbersRestOnTheServersDefaults()
        {
            object Default(string field) => typeof(Properties).GetField(field)!.GetCustomAttribute<ServerPropertyAttribute>()!.DefaultValue;
            Assert.Multiple(() =>
            {
                Assert.That(Convert.ToDouble(Default(nameof(Properties.PVE_MOB_DAMAGE_F1))), Is.EqualTo(3.2));
                Assert.That(Convert.ToDouble(Default(nameof(Properties.PVE_MOB_DAMAGE_F2))), Is.EqualTo(150.0));
                Assert.That(Convert.ToDouble(Default(nameof(Properties.SET_EPIC_ENCOUNTER_WEAPON_DAMAGE_CAP))), Is.EqualTo(1.5));
                Assert.That((bool)Default(nameof(Properties.FORCE_MOB_AUTOSET_STATS)), Is.True, "templates' STR / QUI / INT are replaced");
                Assert.That(Convert.ToInt32(Default(nameof(Properties.MOB_AUTOSET_STR_BASE))), Is.EqualTo(30));
                Assert.That(Convert.ToDouble(Default(nameof(Properties.MOB_AUTOSET_STR_MULTIPLIER))), Is.EqualTo(1.0));
                Assert.That(Convert.ToInt32(Default(nameof(Properties.MOB_AUTOSET_QUI_BASE))), Is.EqualTo(30));
                Assert.That(Convert.ToDouble(Default(nameof(Properties.MOB_AUTOSET_QUI_MULTIPLIER))), Is.EqualTo(1.0));
                Assert.That(AttackComponent.NpcWeaponSpeed(null), Is.EqualTo(30), "no weapon: a one-hander's 3.0");
            });
        }

        private static TNpc Raw<TNpc>() where TNpc : GameNPC => (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));

        [Test]
        public void TheScriptsNumbers()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Raw<GKTrees>().MaxHealth, Is.EqualTo(800));
                Assert.That(Raw<OFGreenKnight>().MaxHealth, Is.EqualTo(100_000));
                Assert.That(Raw<SummonerRoesia>().MaxHealth, Is.EqualTo(100_000));
                Assert.That(Raw<QueenKula>().MaxHealth, Is.EqualTo(300_000));
                Assert.That(Raw<KingTuscar>().MaxHealth, Is.EqualTo(300_000));
                Assert.That(Raw<SpindlerBroodmother>().MaxHealth, Is.EqualTo(100_000));
                Assert.That(Raw<SBAdds>().MaxHealth, Is.EqualTo(8_000), "a newborn");
                Assert.That(Raw<Nosdoden>().MaxHealth, Is.EqualTo(100_000));
                Assert.That(Raw<DOL.GS.Scripts.Legion>().MaxHealth, Is.EqualTo(300_000));
                Assert.That(Raw<AlbGolestandt>().MaxHealth, Is.EqualTo(300_000));
                Assert.That(Raw<MidGjalpinulva>().MaxHealth, Is.EqualTo(300_000));
                Assert.That(Raw<HibCuuldurach>().MaxHealth, Is.EqualTo(300_000));
                Behemoth b = Raw<Behemoth>();
                Assert.That((b.MaxHealth, b.GetArmorAF(eArmorSlot.TORSO), b.GetArmorAbsorb(eArmorSlot.TORSO)), Is.EqualTo((600_000, 550.0, 0.5)));
                Assert.That((b.GetResist(eDamageType.Slash), b.GetResist(eDamageType.Heat)), Is.EqualTo((40, 70)));
            });
        }

        [Test]
        public void TheRealHitsAreTheServersFormulas()
        {
            Assert.Multiple(() =>
            {
                // The Green Knight's trees: level 47-49 (48 here), STR 150 and QUI 80 forced by GKTrees, no weapon, not epic.
                Assert.That(SwingMs(80, false), Is.EqualTo(2_880).Within(0.01));
                Assert.That(Miss(48), Is.EqualTo(0.2067).Within(0.0001));
                var roles = new Dictionary<string, string> { ["tank"] = "plate", ["melee"] = "chain", ["caster"] = "cloth", ["healer"] = "chain" };
                foreach (var (role, kind) in roles)
                {
                    Assert.That(PerSecond(48, 150, 80, 1.0, false, kind, false, false), Is.EqualTo(TreeHigh[role]).Within(0.06), role + " high");
                    Assert.That(PerSecond(48, 150, 80, 1.0, false, kind, true, false), Is.EqualTo(TreeLow[role]).Within(0.06), role + " low");
                }
                Assert.That(Hit(48, 150, 1.0, false, "cloth", false, false), Is.EqualTo(BaseDamage(48, 1.0, false) * 3).Within(1e-9),
                    "a tree's hit on cloth is at its cap");
                // The Green Knight: level 79, autoset 108, two-handed (his script's TwoHandWeapon), an epic boss.
                Assert.That(Autoset(79), Is.EqualTo(108));
                Assert.That(SwingMs(108, true), Is.EqualTo(GreenKnightSwingMs).Within(0.01));
                Assert.That(PerSecond(79, 108, 108, 2.25, true, "plate", false, true), Is.EqualTo(GreenKnightHigh).Within(0.5));
                Assert.That(PerSecond(79, 108, 108, 2.25, true, "plate", true, true), Is.EqualTo(GreenKnightLow).Within(0.5));
                // Summoner Roesia: level 75, autoset 104, two-handed.
                Assert.That(PerSecond(75, 104, 104, 2.25, true, "plate", true, true) / 2, Is.EqualTo(RoesiaTankHitLow).Within(0.5));
                Assert.That(80 * 3.5 * 0.8, Is.EqualTo(RoesiaDsHitLow).Within(1e-9), "her damage shield on a 3.5 s swing, 20% heat resists");
                // Her Pain: 150 x (1 + 104 / 200) = 228 at most, 182 on average, before resists; the 1.2.1 model's 125 (from
                // b1b6846's first minute) means about a third resisted: the log still sets it.
                Assert.That(150 * (1 + Autoset(75) * 0.005), Is.EqualTo(228).Within(1e-9));
                // Marfoirwort: level 80, autoset 109, no weapon.
                Assert.That(SwingMs(Autoset(80), false) / 1000, Is.EqualTo(MarfoirwortSwing).Within(1e-9));
                Assert.That(Miss(80), Is.Zero);
                // Behemoth: level 99, no weapon; autoset QUI 128 (his template's 200 is replaced).
                Assert.That(SwingMs(Autoset(99), false), Is.EqualTo(2_592).Within(0.01));
                Assert.That(SwingMs(200, false), Is.EqualTo(2_160).Within(0.01), "with his template's QUI");
            });
        }

        // ---- the sims rerun with the real numbers --------------------------------------------------------------------

        [Test]
        public void WithRealTreesTheGreenKnightIsLostAtEverySizeToTheLimitAndPastIt()
        {
            // Python mirror (/tmp/sizes real-stats sweep, 64 seeds, docs/RAID-SIZES.md): with the real trees and his real melee,
            // no attempt is won at 40-104 in any case, by either rule; the stack and the peel win from 120 in the low case
            // (59/64 at 80 on a tree; the 1.2 catalog 0/64), past the no-zerg limit (80). Here 8 seeds, 80 on a tree.
            var cases = new (string Name, IReadOnlyDictionary<string, double> Trees, double Gk)[] { ("high", TreeHigh, GreenKnightHigh), ("low", TreeLow, GreenKnightLow) };
            var won = new Dictionary<(string, int, Sim.GkRule), int>();
            foreach (var c in cases)
                foreach (int n in new[] { 40, 80, 104, 120 })
                    foreach (Sim.GkRule rule in new[] { Sim.GkRule.Before, Sim.GkRule.Round2 })
                    {
                        var r = Enumerable.Range(0, 8).Select(s => Sim.GreenKnightFight(s, rule, n, 80.0, 0, treeHitByRole: c.Trees,
                            tankDps: c.Gk, swingMs: GreenKnightSwingMs)).ToArray();
                        won[(c.Name, n, rule)] = r.Count(x => x.Won);
                        TestContext.WriteLine($"{c.Name} {n} {rule}: won {r.Count(x => x.Won)}/8, mean {r.Average(x => x.Minutes):0.0} min");
                    }
            string mirror = string.Join("; ", cases.SelectMany(c => new[] { 40, 80, 104, 120 }.Select(n =>
                $"{c.Name} {n}: {won[(c.Name, n, Sim.GkRule.Before)]} {won[(c.Name, n, Sim.GkRule.Round2)]}")));
            Assert.That(mirror, Is.EqualTo("high 40: 0 0; high 80: 0 0; high 104: 0 0; high 120: 0 0; " +
                                           "low 40: 0 0; low 80: 0 0; low 104: 0 0; low 120: 0 8"));
        }

        [Test]
        public void WithHerRealMeleeRoesiasRaiseToTheLimitHolds()
        {
            // The calibration first: without cures the raid still falls (b1b6846: about 1.5 minutes, her lowest 76%).
            var none = Enumerable.Range(0, 16).Select(s => Sim.RoesiaFight.Run(s, 77, Sim.CureRule.Sweep7b, "midgard", cures: false,
                tankHit: RoesiaTankHitLow, dsHit: RoesiaDsHitLow, heal: RoesiaHealReal)).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(none.Count(x => x.Won), Is.Zero);
                Assert.That(none.Max(x => x.Minutes), Is.LessThan(3.0));
                Assert.That(none.Average(x => x.Lowest), Is.InRange(75, 90), "84.7 here (b1b6846: 76)");
                Assert.That(none.Average(x => x.FirstMinutePain), Is.InRange(105_000, 165_000));
            });
            // Python mirror, 16 seeds (sweep 7b, round 2). 64 seeds (docs/RAID-SIZES.md): round 2 at 80 wins Albion 15,
            // Hibernia 7, Midgard 0 of 64; the 1.2 rule 0-5 of 64 there: raised to the limit (80) holds.
            int[] sizes = [64, 80, 96, 112];
            var won = new Dictionary<(string, int, Sim.CureRule), int>();
            foreach (string realm in new[] { "midgard", "albion", "hibernia" })
                foreach (int n in sizes)
                    foreach (Sim.CureRule rule in new[] { Sim.CureRule.Sweep7b, Sim.CureRule.Round2 })
                        won[(realm, n, rule)] = Enumerable.Range(0, 16).Count(s => Sim.RoesiaFight.Run(s, n, rule, realm,
                            tankHit: RoesiaTankHitLow, dsHit: RoesiaDsHitLow, heal: RoesiaHealReal).Won);
            string mirror = string.Join("; ", new[] { "midgard", "albion", "hibernia" }.SelectMany(realm => sizes.Select(n =>
                $"{realm} {n}: {won[(realm, n, Sim.CureRule.Sweep7b)]} {won[(realm, n, Sim.CureRule.Round2)]}")));
            TestContext.WriteLine(mirror);
            Assert.That(mirror, Is.EqualTo(
                "midgard 64: 0 0; midgard 80: 0 0; midgard 96: 0 0; midgard 112: 1 8; " +
                "albion 64: 0 0; albion 80: 0 5; albion 96: 9 14; albion 112: 10 16; " +
                "hibernia 64: 0 0; hibernia 80: 0 1; hibernia 96: 10 12; hibernia 112: 7 16"));
        }

        [Test]
        public void AtHisRealSwingMarfoirwortIsStillKilledByFortyAndTheOldRuleStillLoses()
        {
            foreach (double perMinute in new[] { 400.0, 200.0 })
            {
                int fix = Enumerable.Range(0, 16).Count(s => Sim.MarfoirwortFight(s, true, MarfoirwortSwing, perMinute, land: 1.0).Killed);
                int old = Enumerable.Range(0, 16).Count(s => Sim.MarfoirwortFight(s, false, MarfoirwortSwing, perMinute, land: 1.0).Killed);
                Assert.That((fix, old), Is.EqualTo((16, 0)), perMinute + " a raider-minute");
            }
        }

        // ---- #414 Behemoth -------------------------------------------------------------------------------------------

        /// <summary>
        /// Raiders a Behemoth raid would need without resurrections: every swing kills one (OnAttackEnemy -> Die, called for
        /// every swing, hit or miss), so with L raiders lost a minute and d damage a raider-minute, n raiders deal
        /// d x n^2 / (2 L) before the last falls; his 600,000 need n = sqrt(2 L x 600,000 / d).
        /// </summary>
        public static double BehemothRaiders(double swingMs, double perRaiderMinute) =>
            Math.Sqrt(2 * (60_000 / swingMs) * 600_000 / perRaiderMinute);

        [Test]
        public void ABehemothRaidWouldNeedMoreThanTheRealmsThreeHundred()
        {
            // d: the b1b6846 ledger's 400 a raider-minute at Marfoirwort (AF 350, absorb 20%, resists 20 / 30), scaled to his
            // AF 550, absorb 50%, resists 40 / 70: melee x 0.30, spells x 0.43, about 145 for a mixed raid.
            double melee = 437.5 / 1_100 * (0.6 / 0.8), spells = 0.3 / 0.7;
            Assert.That(melee, Is.EqualTo(0.298).Within(0.001));
            Assert.That(spells, Is.EqualTo(0.429).Within(0.001));
            double d = 400 * (melee + spells) / 2;
            Assert.That(d, Is.EqualTo(145.4).Within(0.1));
            Assert.Multiple(() =>
            {
                Assert.That(BehemothRaiders(SwingMs(Autoset(99), false), d), Is.EqualTo(437).Within(1));
                Assert.That(BehemothRaiders(SwingMs(200, false), d), Is.EqualTo(479).Within(1));
                Assert.That(BehemothRaiders(SwingMs(Autoset(99), false), d), Is.GreaterThan(RealmRaidRecruitmentPolicy.MaximumBots));
            });
        }
    }
}
