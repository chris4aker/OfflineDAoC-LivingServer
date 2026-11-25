using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using DOL.GS.Styles;
using NUnit.Framework;
using Bench = DOL.GS.AutonomousDefensivePull.UnreachableBench;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3 fork package P5: fixes taken from the Offline DAoC fork family (each test names its source). Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_ForkBotFixes123
    {
        private sealed class SpellIndex : SkillBase
        {
            public static Dictionary<int, Spell> Index => m_spellIndex;
        }

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return File.ReadAllText(Path.Combine(new[] { dir, "source", "server", "GameServer" }.Concat(parts).ToArray()));
        }

        // F2, DividedRealms 8fda4493 / cfad6c50: a heal finds and casts its own sub-spell.
        [Test]
        public void AHealResolvesItsOwnSubSpell()
        {
            const int healId = 990_101, hotId = 990_102;
            var hot = new Spell(new DbSpell { SpellID = hotId, Name = "Test regrowth", Type = "HealOverTime", Target = "Realm" }, 1);
            var heal = new Spell(new DbSpell { SpellID = healId, Name = "Test heal", Type = "Heal", Target = "Realm", SubSpellID = hotId }, 1);
            var self = new Spell(new DbSpell { SpellID = healId, Name = "Self loop", Type = "Heal", Target = "Realm", SubSpellID = healId }, 1);
            var plain = new Spell(new DbSpell { SpellID = healId, Name = "Plain heal", Type = "Heal", Target = "Realm" }, 1);
            SpellIndex.Index[hotId] = hot;
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(HealSpellHandler.ResolveOwnSubSpell(heal)?.ID, Is.EqualTo(hotId), "the spell table's copy of the sub-spell");
                    Assert.That(HealSpellHandler.ResolveOwnSubSpell(self), Is.Null, "never itself");
                    Assert.That(HealSpellHandler.ResolveOwnSubSpell(plain), Is.Null);
                    Assert.That(HealSpellHandler.ResolveOwnSubSpell(null), Is.Null);
                });
            }
            finally { SpellIndex.Index.Remove(hotId); }
            Assert.That(Source("spells", "HealSpellHandler.cs"), Does.Contain("CastOwnSubSpell(target ?? Caster);"));
        }

        // F3, DividedRealms bebdbb71: a class proc row replaces the shared ClassID 0 row for a bot too.
        [Test]
        public void AClassProcReplacesTheSharedProc()
        {
            Spell spell = new(new DbSpell { SpellID = 990_201, Name = "Proc", Type = "DirectDamage", Target = "Enemy" }, 1);
            var shared = new StyleProcInfo(spell, 0, 100);
            var thane = new StyleProcInfo(spell, (int)eCharacterClass.Thane, 100);
            var other = new StyleProcInfo(spell, (int)eCharacterClass.Warrior, 100);
            var procs = new[] { shared, thane, other };
            Assert.Multiple(() =>
            {
                Assert.That(StyleProcessor.SelectFixedProcs(procs, (int)eCharacterClass.Thane, (int)eCharacterClass.Thane),
                    Is.EqualTo(new[] { thane }), "a Thane (player or bot) fires only its class proc");
                Assert.That(StyleProcessor.SelectFixedProcs(procs, (int)eCharacterClass.Berserker, 0),
                    Is.EqualTo(new[] { shared }), "no class row: the shared one");
                Assert.That(StyleProcessor.SelectFixedProcs(procs, 0, (int)eCharacterClass.Warrior),
                    Is.EqualTo(new[] { shared, other }), "a mob keeps the style class rule as before");
            });
            string doom = Source("spells", "DoomHammer.cs");
            Assert.That(doom, Does.Not.Contain("base.ApplyEffectOnTarget(Caster)"), "Doom Hammer hits its target");
            Assert.That(Source("gameutils", "Specialization.cs"), Does.Contain("internal static int ResolveClassId"));
        }

        // F7, True165 bc2440ca: /gc edit dues and withdraw set their own permission, not Release.
        [Test]
        public void GuildRankDuesAndWithdrawSetTheirOwnPermission()
        {
            string guild = Source("commands", "playercommands", "guild.cs");
            // The assignment just before each rank-edit message names the permission that message reports.
            string Assigned(string message)
            {
                int at = guild.IndexOf(message, System.StringComparison.Ordinal);
                Assert.That(at, Is.GreaterThan(0), message);
                int assign = guild.LastIndexOf("= reponse;", at, System.StringComparison.Ordinal);
                int line = guild.LastIndexOf('\n', assign);
                return guild.Substring(line, assign - line);
            }
            Assert.Multiple(() =>
            {
                Assert.That(Assigned("Scripts.Player.Guild.RankDuesSet"), Does.Contain(".Dues "));
                Assert.That(Assigned("Scripts.Player.Guild.RankWithdrawSet"), Does.Contain(".Withdraw "));
                Assert.That(Assigned("Scripts.Player.Guild.RankReleaseSet"), Does.Contain(".Release "));
            });
        }

        // F8, DividedRealms 1d18f864: a ranged pull the monster ignored counts toward the #539 bench.
        [Test]
        public void AnIgnoredRangedPullBenchesTheTargetOnTheSecondTry()
        {
            var bench = new Bench();
            Assert.Multiple(() =>
            {
                Assert.That(bench.RecordIgnored(7, 1_000), Is.False, "the first is a retry");
                Assert.That(bench.RecordIgnored(7, 41_000), Is.True, "the second benches it");
                Assert.That(bench.IsBenched(7, 41_000), Is.True);
                Assert.That(bench.IsBenched(8, 41_000), Is.False, "other monsters still get ranged pulls");
            });
            var reached = new Bench();
            reached.RecordIgnored(9, 1_000);
            reached.Reached(9, 2_000);
            Assert.That(reached.RecordIgnored(9, 3_000), Is.False, "a pull that reached the party forgets the failure");
            var mixed = new Bench();
            mixed.RecordUnreachable(5, 1_000);
            Assert.That(mixed.RecordIgnored(5, 31_000), Is.True, "no firing point and an ignored shot are both failed tries");
            Assert.That(Source("bots", "autonomous", "AutonomousDefensivePull.cs"), Does.Contain("if (!broken) RecordIgnoredPull(state);"));
        }

        // P1, True165 e4d2498ca / 927018e: one side per monster; a raid is one side.
        [Test]
        public void MonsterClaimSidesAreGroupsOrRaids()
        {
            object groupA = new(), groupB = new();
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousMobClaims.SameSide(groupA, groupA, false, false), Is.True);
                Assert.That(AutonomousMobClaims.SameSide(groupA, groupB, false, false), Is.False, "another group's monster");
                Assert.That(AutonomousMobClaims.SameSide(groupA, groupB, false, true), Is.False,
                    "a player outside an ordinary party is another side");
                Assert.That(AutonomousMobClaims.SameSide(groupA, groupB, true, true), Is.True,
                    "a realm player fighting beside a raid is on its side");
                Assert.That(AutonomousMobClaims.SameSide(null, groupA, true, true), Is.False);
                Assert.That(AutonomousMobClaims.TakenByOther(null, null), Is.False);
                Assert.That(AutonomousMobClaims.ClaimMilliseconds, Is.EqualTo(30_000));
            });
            string controller = Source("bots", "autonomous", "AutonomousWorldBotController.cs");
            Assert.That(controller, Does.Contain(".Where(npc => !AutonomousMobClaims.TakenByOther(bot, npc))"));
            Assert.That(Regex.Matches(controller, @"AutonomousMobClaims\.Take\(bot, target\)").Count, Is.EqualTo(3),
                "claimed when a flying handoff, a ranged pull or an ordinary pull starts");
        }
    }
}
