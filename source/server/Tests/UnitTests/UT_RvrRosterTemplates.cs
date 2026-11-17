using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using C = DOL.GS.eCharacterClass;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR, work package 6: role-built eights per realm (docs/RVR-FIELD-1.2.md, "Realm playbooks"), with a
    /// full pool and with short ones (the next best fit, the group still forms), the pet cap, and duo partners. Pure.
    /// </summary>
    [TestFixture]
    public class UT_RvrRosterTemplates
    {
        private static readonly HashSet<C> Curers = [C.Cleric, C.Healer, C.Shaman, C.Druid, C.Warden, C.Bard, C.Minstrel, C.Friar];

        private static List<RvrRosterCandidate> Pool(params C[] classes) =>
            classes.Select((c, i) => new RvrRosterCandidate(i, c, 50, Curers.Contains(c))).ToList();

        private static RvrRosterCandidate Leader(C c) => new(-1, c, 50, Curers.Contains(c));

        private static C[] Picked(RvrRoster roster, List<RvrRosterCandidate> pool) => roster.Picked.Select(i => pool[i].Class).ToArray();

        private static C Filled(RvrRoster roster, RvrRosterRole role, int nth = 0) => roster.Filled.Where(f => f.Role == role).ElementAt(nth).Class;

        [Test]
        public void Albion_fills_the_2003_eight_from_a_full_pool()
        {
            var pool = Pool(C.Wizard, C.Mercenary, C.Cleric, C.Infiltrator, C.Sorcerer, C.Mercenary, C.Cabalist, C.Cleric, C.Minstrel,
                C.Theurgist, C.Paladin, C.Scout, C.Armsman, C.Cleric, C.Necromancer);
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Albion, Leader(C.Mercenary), pool, 7, 2);
            Assert.Multiple(() =>
            {
                Assert.That(roster.Template, Is.EqualTo("eight-albion"));
                Assert.That(roster.Picked, Has.Length.EqualTo(7));
                Assert.That(roster.Missing, Is.Empty);
                Assert.That(roster.NextBest, Is.Empty);
                Assert.That(Picked(roster, pool).Count(c => c == C.Cleric), Is.EqualTo(2), "two main healers");
                Assert.That(Filled(roster, RvrRosterRole.CrowdControl), Is.EqualTo(C.Sorcerer));
                Assert.That(Filled(roster, RvrRosterRole.ShieldTank), Is.EqualTo(C.Paladin));
                Assert.That(Filled(roster, RvrRosterRole.Speed), Is.EqualTo(C.Minstrel));
                Assert.That(Filled(roster, RvrRosterRole.RealmExtra), Is.EqualTo(C.Theurgist));
                Assert.That(roster.Filled.Count(f => f.Role == RvrRosterRole.CureMez), Is.EqualTo(2), "two who can cure mez (the Clerics count)");
                Assert.That(Picked(roster, pool), Does.Contain(C.Mercenary), "the rest damage, Mercenaries first");
                Assert.That(Picked(roster, pool), Does.Not.Contain(C.Infiltrator), "stealthers camp alone");
                Assert.That(Picked(roster, pool).Count(AutonomousRvrPetCap.IsPetClass), Is.LessThanOrEqualTo(2));
            });
        }

        [Test]
        public void Midgard_healers_mez_and_the_shaman_comes_along()
        {
            var pool = Pool(C.Berserker, C.Healer, C.Runemaster, C.Savage, C.Shaman, C.Skald, C.Healer, C.Warrior, C.Spiritmaster, C.Shadowblade, C.Hunter, C.Thane);
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Midgard, Leader(C.Savage), pool, 7, 2);
            C[] picked = Picked(roster, pool);
            Assert.Multiple(() =>
            {
                Assert.That(roster.Missing, Is.Empty);
                Assert.That(picked.Count(c => c == C.Healer), Is.EqualTo(2));
                Assert.That(Filled(roster, RvrRosterRole.CrowdControl), Is.EqualTo(C.Healer), "a Healer mezzes in its own slot");
                Assert.That(Filled(roster, RvrRosterRole.ShieldTank), Is.EqualTo(C.Warrior));
                Assert.That(Filled(roster, RvrRosterRole.Speed), Is.EqualTo(C.Skald));
                Assert.That(Filled(roster, RvrRosterRole.RealmExtra), Is.EqualTo(C.Shaman));
                Assert.That(picked, Does.Contain(C.Berserker).Or.Contain(C.Runemaster), "the rest: Savage, Berserker, Runemaster");
                Assert.That(picked, Has.Length.EqualTo(7));
            });
        }

        [Test]
        public void Hibernia_takes_druids_a_bard_a_warden_or_hero_and_eldritches()
        {
            var pool = Pool(C.Ranger, C.Eldritch, C.Druid, C.Blademaster, C.Bard, C.Hero, C.Eldritch, C.Druid, C.Nightshade, C.Enchanter, C.Mentalist, C.Champion);
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Hibernia, Leader(C.Hero), pool, 7, 2);
            C[] picked = Picked(roster, pool);
            Assert.Multiple(() =>
            {
                Assert.That(roster.Missing, Is.Empty);
                Assert.That(picked.Count(c => c == C.Druid), Is.EqualTo(2));
                Assert.That(Filled(roster, RvrRosterRole.CrowdControl), Is.EqualTo(C.Bard));
                Assert.That(Filled(roster, RvrRosterRole.Speed), Is.EqualTo(C.Bard), "the Bard is the speed too");
                Assert.That(Filled(roster, RvrRosterRole.ShieldTank), Is.EqualTo(C.Hero), "the Hero leader holds the tank slot");
                Assert.That(picked.Count(c => c == C.Eldritch), Is.EqualTo(2), "two point-blank casters");
                Assert.That(roster.Filled.Any(f => f.Role == RvrRosterRole.RealmExtra), Is.False, "Hibernia has no extra slot");
                Assert.That(picked, Has.Length.EqualTo(7));
            });
        }

        [Test]
        public void A_short_pool_takes_the_next_best_fit_and_still_forms()
        {
            // No Cleric, no Sorcerer, no Paladin or Armsman, no Theurgist.
            var pool = Pool(C.Friar, C.Mercenary, C.Minstrel, C.Reaver, C.Wizard);
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Albion, Leader(C.Scout), pool, 7, 2);
            Assert.Multiple(() =>
            {
                Assert.That(roster.Picked, Has.Length.EqualTo(5), "everyone it found");
                Assert.That(Filled(roster, RvrRosterRole.MainHealer), Is.EqualTo(C.Friar), "a Friar heals");
                Assert.That(Filled(roster, RvrRosterRole.CrowdControl), Is.EqualTo(C.Minstrel), "a Minstrel mezzes");
                Assert.That(Filled(roster, RvrRosterRole.ShieldTank), Is.EqualTo(C.Reaver));
                Assert.That(roster.NextBest, Does.Contain(RvrRosterRole.MainHealer).And.Contain(RvrRosterRole.ShieldTank));
                Assert.That(roster.Missing, Does.Contain(RvrRosterRole.MainHealer), "the second healer is missing");
                Assert.That(roster.Missing, Does.Contain(RvrRosterRole.RealmExtra));
            });

            RvrRoster none = RvrRosterTemplates.Build(eRealm.Midgard, Leader(C.Berserker), Pool(C.Savage, C.Savage, C.Hunter), 7, 2);
            Assert.That(none.Picked, Has.Length.EqualTo(3));
            Assert.That(none.Missing.Count(r => r == RvrRosterRole.MainHealer), Is.EqualTo(2));
            Assert.That(none.Missing.Count(r => r == RvrRosterRole.CureMez), Is.EqualTo(2));

            RvrRoster empty = RvrRosterTemplates.Build(eRealm.Hibernia, Leader(C.Druid), [], 7, 2);
            Assert.That(empty.Picked, Is.Empty);
            Assert.That(Filled(empty, RvrRosterRole.MainHealer), Is.EqualTo(C.Druid), "the leader's own slot");
        }

        [Test]
        public void The_pet_cap_holds_and_a_small_take_is_respected()
        {
            var pool = Pool(C.Cabalist, C.Theurgist, C.Necromancer, C.Cabalist, C.Cleric, C.Theurgist, C.Necromancer, C.Mercenary);
            RvrRoster roster = RvrRosterTemplates.Build(eRealm.Albion, Leader(C.Cabalist), pool, 7, 2);
            Assert.That(Picked(roster, pool).Count(AutonomousRvrPetCap.IsPetClass) + 1, Is.LessThanOrEqualTo(2), "the leader counts");
            Assert.That(Filled(roster, RvrRosterRole.RealmExtra), Is.EqualTo(C.Theurgist));

            RvrRoster three = RvrRosterTemplates.Build(eRealm.Albion, Leader(C.Mercenary), pool, 3, 2);
            Assert.That(three.Picked, Has.Length.EqualTo(3));

            RvrRoster uncapped = RvrRosterTemplates.Build(eRealm.Albion, Leader(C.Cabalist), pool, 7, int.MaxValue);
            Assert.That(uncapped.Picked, Has.Length.EqualTo(7));
        }

        [Test]
        public void The_skeleton_of_every_realm()
        {
            Assert.That(RvrRosterTemplates.Skeleton(eRealm.Albion), Is.EqualTo(new[] { RvrRosterRole.MainHealer, RvrRosterRole.MainHealer,
                RvrRosterRole.CrowdControl, RvrRosterRole.ShieldTank, RvrRosterRole.Speed, RvrRosterRole.RealmExtra, RvrRosterRole.CureMez, RvrRosterRole.CureMez }));
            Assert.That(RvrRosterTemplates.Skeleton(eRealm.Midgard), Does.Contain(RvrRosterRole.RealmExtra));
            Assert.That(RvrRosterTemplates.Skeleton(eRealm.Hibernia), Does.Not.Contain(RvrRosterRole.RealmExtra));
        }

        [Test]
        public void A_duo_pairs_stealthers_and_gives_a_fighter_a_healer()
        {
            var pool = Pool(C.Mercenary, C.Cleric, C.Infiltrator, C.Cabalist);
            Assert.Multiple(() =>
            {
                Assert.That(pool[RvrRosterTemplates.DuoPartner(Leader(C.Infiltrator), pool, 1)].Class, Is.EqualTo(C.Infiltrator));
                Assert.That(pool[RvrRosterTemplates.DuoPartner(Leader(C.Armsman), pool, 1)].Class, Is.EqualTo(C.Cleric));
                Assert.That(pool[RvrRosterTemplates.DuoPartner(Leader(C.Cleric), pool, 1)].Class, Is.EqualTo(C.Mercenary));
                Assert.That(RvrRosterTemplates.DuoPartner(Leader(C.Necromancer), Pool(C.Cabalist), 1), Is.EqualTo(-1), "one pet class in a small group");
                Assert.That(RvrRosterTemplates.DuoPartner(Leader(C.Wizard), [], 1), Is.EqualTo(-1));
            });
        }
    }
}
