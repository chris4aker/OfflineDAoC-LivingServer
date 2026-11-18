using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using C = DOL.GS.eCharacterClass;

namespace DOL.UnitTests
{
    /// <summary>
    /// Era builds 1.2, WP5 (owner's review D9): the RvR eight's crowd-control slot ranks a crowd-control era build first
    /// (healer-cc, cc-mezzer), then today's order; bots without an era role keep today's ranking. RvR field code
    /// (RvrRosterTemplates), not raid code. Pure. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_RvrRosterBuildRoles
    {
        private static readonly HashSet<C> Curers = [C.Cleric, C.Healer, C.Shaman, C.Druid, C.Warden, C.Bard, C.Minstrel, C.Friar];

        private static RvrRosterCandidate Bot(int index, C cls, string role = null) => new(index, cls, 50, Curers.Contains(cls), role);

        private static List<RvrRosterCandidate> Pool(params (C Class, string Role)[] bots) =>
            bots.Select((b, i) => Bot(i, b.Class, b.Role)).ToList();

        private static C Filled(RvrRoster roster, RvrRosterRole role) => roster.Filled.First(f => f.Role == role).Class;

        [Test]
        public void MidgardTakesAPacificationHealerForItsCrowdControlSlot()
        {
            // Two Mending Healers come first in the pool; today the slot was "filled" by one of them, a Healer with no mez.
            var pool = Pool((C.Healer, "healer"), (C.Healer, "healer"), (C.Warrior, "tank"), (C.Skald, "support-melee"),
                (C.Shaman, "healer-debuff"), (C.Healer, "healer-cc"), (C.Berserker, "melee-dps"), (C.Runemaster, "assist-dd"));
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Midgard, Bot(-1, C.Savage, "melee-dps"), pool, 7, 2);
            Assert.Multiple(() =>
            {
                Assert.That(Filled(roster, RvrRosterRole.CrowdControl), Is.EqualTo(C.Healer));
                Assert.That(roster.CrowdControlRole, Is.EqualTo("healer-cc"));
                Assert.That(roster.CrowdControlLabel, Is.EqualTo("Healer(healer-cc)"), "RVR_PARTY_FORMED cc=");
                Assert.That(roster.Picked, Does.Contain(5), "the Pacification Healer is in the eight");
                Assert.That(roster.Picked.Take(2), Is.EqualTo(new[] { 0, 1 }), "the main healers are still the first two");
            });

            RvrRoster today = RvrRosterTemplates.Build(eRealm.Midgard, Bot(-1, C.Savage, "melee-dps"), pool, 7, 2, buildRoles: false);
            Assert.That(today.CrowdControlLabel, Is.EqualTo("Healer(healer)"), "rvr_roster_build_roles off: today's overlap");
        }

        [Test]
        public void AMezHealerAlreadyInTheEightCoversTheSlot()
        {
            var pool = Pool((C.Healer, "healer"), (C.Healer, "healer-cc"), (C.Warrior, "tank"), (C.Skald, "support-melee"),
                (C.Shaman, "healer-debuff"), (C.Healer, "healer-cc"), (C.Berserker, "melee-dps"), (C.Runemaster, "assist-dd"),
                (C.Savage, "melee-dps"));
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Midgard, Bot(-1, C.Savage), pool, 7, 2);
            Assert.That(roster.CrowdControlRole, Is.EqualTo("healer-cc"));
            Assert.That(roster.Picked.Count(i => pool[i].Class == C.Healer), Is.EqualTo(2), "no third Healer is needed");
        }

        [Test]
        public void AHealerLeaderWithAMezBuildCountsForTheSlot()
        {
            var pool = Pool((C.Healer, "healer"), (C.Warrior, "tank"), (C.Skald, "support-melee"), (C.Shaman, "healer-debuff"),
                (C.Healer, "healer-cc"), (C.Savage, "melee-dps"));
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Midgard, Bot(-1, C.Healer, "healer-cc"), pool, 7, 2);
            Assert.That(roster.CrowdControlLabel, Is.EqualTo("Healer(healer-cc)"));
            Assert.That(roster.Filled.First(f => f.Role == RvrRosterRole.MainHealer).Class, Is.EqualTo(C.Healer), "the leader heals too");
        }

        [Test]
        public void ARegrowthBardLeaderLeavesTheSlotToAMusicBard()
        {
            var pool = Pool((C.Druid, "healer"), (C.Druid, "healer"), (C.Hero, "tank"), (C.Bard, "cc-mezzer"), (C.Eldritch, "pbae"),
                (C.Blademaster, "melee-dps"), (C.Warden, "support-healer"));
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Hibernia, Bot(-1, C.Bard, "healer"), pool, 7, 2);
            Assert.Multiple(() =>
            {
                Assert.That(roster.CrowdControlLabel, Is.EqualTo("Bard(cc-mezzer)"));
                Assert.That(roster.Picked, Does.Contain(3));
                Assert.That(Filled(roster, RvrRosterRole.Speed), Is.EqualTo(C.Bard), "the leader is the speed");
            });
        }

        [Test]
        public void AlbionPrefersAMindSorcererOverTheFirstSorcerer()
        {
            var pool = Pool((C.Cleric, "healer"), (C.Cleric, "healer-buffer"), (C.Sorcerer, "assist-dd"), (C.Paladin, "tank-support"),
                (C.Minstrel, "cc-support"), (C.Theurgist, "cc-root"), (C.Sorcerer, "cc-mezzer"), (C.Mercenary, "melee-dps"));
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Albion, Bot(-1, C.Armsman, "tank"), pool, 7, 2);
            Assert.That(roster.CrowdControlLabel, Is.EqualTo("Sorcerer(cc-mezzer)"));
            Assert.That(roster.Picked, Does.Contain(6));
            Assert.That(roster.Picked, Does.Not.Contain(2), "the Body Sorcerer is not needed for the slot");
            Assert.That(Filled(roster, RvrRosterRole.RealmExtra), Is.EqualTo(C.Theurgist), "the Theurgist keeps its own slot");
        }

        [Test]
        public void BotsWithoutAnEraRoleKeepTodaysRanking()
        {
            // Random pools of every realm, no era roles: the same eight, member for member, with the switch on or off.
            var random = new Random(1665);
            C[][] classes =
            [
                [C.Cleric, C.Friar, C.Paladin, C.Armsman, C.Mercenary, C.Reaver, C.Sorcerer, C.Minstrel, C.Theurgist, C.Wizard, C.Cabalist, C.Scout, C.Infiltrator, C.Necromancer],
                [C.Healer, C.Shaman, C.Warrior, C.Thane, C.Berserker, C.Savage, C.Skald, C.Spiritmaster, C.Runemaster, C.Bonedancer, C.Hunter, C.Shadowblade],
                [C.Druid, C.Warden, C.Hero, C.Champion, C.Blademaster, C.Valewalker, C.Bard, C.Mentalist, C.Eldritch, C.Enchanter, C.Animist, C.Ranger, C.Nightshade],
            ];
            for (int trial = 0; trial < 3000; trial++)
            {
                int realm = trial % 3;
                C[] set = classes[realm];
                var pool = Enumerable.Range(0, random.Next(3, 16)).Select(i => Bot(i, set[random.Next(set.Length)])).ToList();
                RvrRosterCandidate leader = Bot(-1, set[random.Next(set.Length)]);
                RvrRoster on = RvrRosterTemplates.Build((eRealm)(realm + 1), leader, pool, 7, 2, buildRoles: true);
                RvrRoster off = RvrRosterTemplates.Build((eRealm)(realm + 1), leader, pool, 7, 2, buildRoles: false);
                Assert.That(on.Picked, Is.EqualTo(off.Picked), $"trial {trial}");
                Assert.That(on.Filled, Is.EqualTo(off.Filled), $"trial {trial}");
                Assert.That(on.Missing, Is.EqualTo(off.Missing), $"trial {trial}");
                Assert.That(on.NextBest, Is.EqualTo(off.NextBest), $"trial {trial}");
                Assert.That(on.CrowdControlRole, Is.Empty);
            }
        }

        [Test]
        public void OnlyTheThreeCrowdControlRolesCount()
        {
            Assert.That(new[] { "healer-cc", "cc-mezzer" }.All(RvrRosterTemplates.IsCrowdControlRole), Is.True);
            Assert.That(new[] { "healer", "cc-support", "cc-root", "pbae", "", null }.Any(RvrRosterTemplates.IsCrowdControlRole), Is.False,
                "cc-root is the Theurgist's, which is no realm's crowd-control class");
            Assert.That(OfflineDaoc.Shared.EraBuildCatalog.Default.Classes.SelectMany(c => c.Builds)
                .Where(b => RvrRosterTemplates.IsCrowdControlRole(b.Role)).Select(b => b.Id).OrderBy(id => id, StringComparer.Ordinal),
                Is.EqualTo(new[] { "bard-music-nurt", "healer-pac-mend-aug", "healer-pac-mend-aug19", "healer-pac-mend31", "sorcerer-mind-body" }),
                "healer-pac-mend-aug19: the research's tri-spec (38 Pac, its instant AoE stun)");
        }

        [Test]
        public void TheCrowdControlLabelSaysWhoAndWhichBuild()
        {
            Assert.That(new RvrRoster().CrowdControlLabel, Is.EqualTo("none"));
            var noRole = RvrRosterTemplates.Build(eRealm.Albion, Bot(-1, C.Mercenary), Pool((C.Sorcerer, null)), 7, 2);
            Assert.That(noRole.CrowdControlLabel, Is.EqualTo("Sorcerer"), "a bot without an era build");
        }
    }
}
