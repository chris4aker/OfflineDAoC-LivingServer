using System;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2.2, the raid tactic audit's fixes (everything but the Green Knight): pure rules. Tests only:
    /// nothing here has been played in game.
    /// </summary>
    [TestFixture]
    public sealed class UT_RaidTactics122
    {
        // ---- finding 3: the paladin, friar and warden fight in melee and heal from there -----------------------------------

        [TestCase(eCharacterClass.Paladin, true)]
        [TestCase(eCharacterClass.Friar, true)]
        [TestCase(eCharacterClass.Warden, true)]
        [TestCase(eCharacterClass.Cleric, false)]
        [TestCase(eCharacterClass.Druid, false)]
        [TestCase(eCharacterClass.Healer, false)]
        [TestCase(eCharacterClass.Shaman, false)]
        public void MeleeHybridsHealButFightInMelee(eCharacterClass cls, bool hybrid)
        {
            Assert.That(BotPartyRoles.IsHealingClass(cls), Is.True, "every one of them heals");
            Assert.That(BotPartyRoles.IsMeleeHybridHealer(cls), Is.EqualTo(hybrid));
            Assert.That(BotPartyRoles.IsRangedHealer(cls), Is.EqualTo(!hybrid));
            Assert.That(T.FightsFromRange(false, (int)cls), Is.EqualTo(!hybrid), "where it stands in every raid rule");
            var c = new T.RuleContext(null, null, null, 0, false, 0, (int)cls);
            Assert.That(c.StandsBack, Is.EqualTo(!hybrid), "the new rules of unwon fights");
            Assert.That(c.AtRange, Is.True, "v1.2's test, kept for the rules of won fights (regression audit, item 1)");
        }

        [Test]
        public void CastersAndArchersStillFightFromRangeAndMeleeDoNot()
        {
            Assert.That(T.FightsFromRange(true, (int)eCharacterClass.Eldritch), Is.True);
            Assert.That(T.FightsFromRange(true, (int)eCharacterClass.Ranger), Is.True);
            Assert.That(T.FightsFromRange(false, (int)eCharacterClass.Hero), Is.False);
            Assert.That(T.FightsFromRange(false, 0), Is.False, "no class");
            Assert.That(T.IsRangedHealerClass(0), Is.False);
        }

        [Test]
        public void EveryHealingClassIsEitherAMeleeHybridOrARangedHealer()
        {
            foreach (eCharacterClass cls in Enum.GetValues<eCharacterClass>().Where(BotPartyRoles.IsHealingClass))
                Assert.That(BotPartyRoles.IsMeleeHybridHealer(cls) ^ BotPartyRoles.IsRangedHealer(cls), Is.True, cls.ToString());
            // The keep lord formation's healer band (RvrLordFormation.RoleOf) holds the same ranged healers.
            foreach (eCharacterClass cls in new[] { eCharacterClass.Cleric, eCharacterClass.Druid, eCharacterClass.Healer, eCharacterClass.Shaman })
                Assert.That(RvrLordFormation.RoleOf(cls, false), Is.EqualTo(RvrLordFormation.Role.Healer));
            Assert.That(RvrLordFormation.RoleOf(eCharacterClass.Friar, false), Is.EqualTo(RvrLordFormation.Role.Melee));
            Assert.That(RvrLordFormation.RoleOf(eCharacterClass.Warden, false), Is.EqualTo(RvrLordFormation.Role.Melee));
        }

        [Test]
        public void TheSacristanAmbushGivesMeleeHybridsTheMeleeReach()
        {
            // The ambush rule's reach comes from StandsBack: a friar waits for him at the melee station's 450, not the casters' 1,400.
            var friar = new T.RuleContext(null, null, null, 0, false, 0, (int)eCharacterClass.Friar);
            var cleric = new T.RuleContext(null, null, null, 0, false, 0, (int)eCharacterClass.Cleric);
            Assert.That((friar.StandsBack, cleric.StandsBack), Is.EqualTo((false, true)));
        }

        // ---- regression audit, item 2: the Summoner Pain cure rules in Summoner's Hall only ---------------------------------

        [Test]
        public void OutsideSummonersHallTheRaidCureIsV12s()
        {
            Assert.That(RaidCurePoison.PainRules(248), Is.True, "Summoner's Hall");
            foreach (ushort region in new ushort[] { 0, 1, 100, 191, 223, 249 })
                Assert.That(RaidCurePoison.PainRules(region), Is.False, region.ToString());
            // v1.2: a member under 50% is in danger, dotted or not; the cure waits 5 s.
            Assert.That(RaidCurePoison.InDanger(40, true, painRules: false), Is.True);
            Assert.That(RaidCurePoison.InDanger(49, false, painRules: false), Is.True);
            Assert.That(RaidCurePoison.InDanger(50, true, painRules: false), Is.False);
            Assert.That(RaidCurePoison.InDanger(40, true, painRules: true), Is.False, "Summoner Pain: under a quarter only");
            Assert.That(RaidCurePoison.CureDelayFor(false), Is.EqualTo(5_000L));
            Assert.That(RaidCurePoison.CureDelayFor(true), Is.EqualTo(RaidCurePoison.CureDelayMilliseconds));
            var dotted = new[]
            {
                new RaidCurePoison.Dotted(1, true, 60, 900, true), new RaidCurePoison.Dotted(2, true, 40, 1_600, true),
                new RaidCurePoison.Dotted(3, false, 30, 400, true),
            };
            // v1.2: with its party in danger, no cure at all (round 2 would cure the fresh ones).
            Assert.That(RaidCurePoison.Decide(true, true, false, true, dotted, 1, false, 9, painRules: false),
                Is.EqualTo((RaidCurePoison.Cure.None, -1)));
            Assert.That(RaidCurePoison.Decide(true, true, false, true, dotted, 1, false, 9, painRules: true).Cure,
                Is.EqualTo(RaidCurePoison.Cure.Single));
            // v1.2: nobody in danger, its own party's lowest percentage (40%), not the fewest health points (900).
            Assert.That(RaidCurePoison.Decide(true, true, false, true, dotted, 0, false, 9, painRules: false),
                Is.EqualTo((RaidCurePoison.Cure.Single, 1)));
            Assert.That(RaidCurePoison.Decide(true, true, false, true, dotted, 0, false, 9, painRules: true),
                Is.EqualTo((RaidCurePoison.Cure.Single, 0)));
            // v1.2's own Choose on the same raiders (no health points then).
            Assert.That(RaidCurePoison.Choose(dotted.Select(d => d with { Health = 0, Fresh = false }).ToList(), 9), Is.EqualTo(1));
        }

        // ---- finding 9: King Tuscar's target holds him with its taunts -------------------------------------------------

        [Test]
        public void HisTankTauntsWhileKulaLives()
        {
            Assert.That(T.TuscarTankTaunts(isHisTarget: true, kulaLives: true, isTank: true), Is.True);
            Assert.That(T.TuscarTankTaunts(true, false, true), Is.False, "Kula dead: it fights him as it likes");
            Assert.That(T.TuscarTankTaunts(false, true, true), Is.False, "not his target: it does not touch him");
            Assert.That(T.TuscarTankTaunts(true, true, false), Is.False, "no tank: no taunt to use");
        }

        // ---- finding 12: Spindler's spread, short bows on the closer ring ----------------------------------------------

        [Test]
        public void ShortRangeArchersTakeAPostOnTheCloserRing()
        {
            Vector3 boss = new(10_000, 10_000, 0);
            Vector3[] posts = T.SpreadRing(boss, T.SpindlerSpreadSpacing, T.SpindlerSpreadRings);
            float D(Vector3 p) => Vector2.Distance(new(p.X, p.Y), new(boss.X, boss.Y));
            Assert.That(posts.Any(p => D(p) > 1_300) && posts.Any(p => D(p) < 800), Is.True, "both rings");
            Assert.That(T.DistanceWeaponReach(eObjectType.Fired), Is.EqualTo(1_200));
            Assert.That(T.DistanceWeaponReach(eObjectType.Crossbow), Is.EqualTo(1_200));
            Assert.That(T.DistanceWeaponReach(eObjectType.Thrown, "weighted throwing axes"), Is.EqualTo(1_450));
            Assert.That(T.DistanceWeaponReach(eObjectType.Longbow), Is.EqualTo(1_760));
            for (int party = 0; party < 20; party++)
            {
                int shortBow = T.SpreadPostFor(boss, posts, party, T.DistanceWeaponReach(eObjectType.Fired));
                Assert.That(D(posts[shortBow]) + T.SpreadReachMargin, Is.LessThanOrEqualTo(1_200), $"party {party}: in reach of her");
                Assert.That(T.SpreadPostFor(boss, posts, party, T.DistanceWeaponReach(eObjectType.Longbow)),
                    Is.EqualTo(T.SpreadPostIndex(party, posts.Length)), "a longbow (and every caster) keeps its party's post");
                Assert.That(T.SpreadPostFor(boss, posts, party, float.MaxValue), Is.EqualTo(T.SpreadPostIndex(party, posts.Length)));
            }
            Assert.That(T.SpreadPostFor(boss, posts, 0, 500), Is.EqualTo(-1), "no post in reach: it fights as usual");
        }

        // ---- finding 11: the lapsed court guard holds, never a blind pull ----------------------------------------------

        [Test]
        public void TheLapsedCourtGuardCoversTheCourtFightAndHolds()
        {
            Assert.That(T.CourtLapsedRunSeconds, Is.EqualTo(T.CourtPullHorizonSeconds), "the 35 s court fight");
            Assert.That(RealmRaidDungeonRoute.LapsedGuardHolds("lapsed-guard: the Crypt Lord's patrol comes within 600"), Is.True,
                "past both lapses it still holds");
            Assert.That(RealmRaidDungeonRoute.LapsedGuardHolds(null), Is.False, "its window: the pull goes in");
        }

        [Test]
        public void TheRangedHealerTestByClassId()
        {
            Assert.That(T.IsRangedHealerClass((int)eCharacterClass.Friar), Is.False);
            Assert.That(T.IsRangedHealerClass((int)eCharacterClass.Cleric), Is.True);
        }
    }
}
