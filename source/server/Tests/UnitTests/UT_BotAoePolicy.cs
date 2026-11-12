using System.Collections.Generic;
using NUnit.Framework;
using static DOL.GS.BotAoePolicy;

namespace DOL.GS.Tests
{
    /// <summary>Area spells for bots (GitHub issue #17): two or more monsters in the fight, nothing woken or pulled.</summary>
    [TestFixture]
    public sealed class UT_BotAoePolicy
    {
        private const int Radius = 300;
        private static readonly int Count = Radius - EdgeMargin;

        private static Blast Evaluate(bool mez = false, params Candidate[] candidates) =>
            BotAoePolicy.Evaluate(candidates, Count, Radius, mez);

        [Test]
        public void TwoMonstersInTheFightAreWorthIt()
        {
            Blast blast = Evaluate(false, new Candidate(100, true, false), new Candidate(200, true, false));
            Assert.That(blast.Hits, Is.EqualTo(2));
            Assert.That(blast.Worthwhile, Is.True);
        }

        [Test]
        public void OneMonsterIsNot() =>
            Assert.That(Evaluate(false, new Candidate(100, true, false)).Worthwhile, Is.False);

        [Test]
        public void OnlyMonstersWellInsideTheEdgeCount()
        {
            Blast blast = Evaluate(false, new Candidate(100, true, false), new Candidate(Radius - 10, true, false));
            Assert.That(blast.Hits, Is.EqualTo(1), "a monster at the edge may step out while the spell is cast");
            Assert.That(blast.Vetoed, Is.False);
        }

        [Test]
        public void AMesmerizedMonsterInReachVetoesDamage()
        {
            Blast blast = Evaluate(false, new Candidate(100, true, false), new Candidate(150, true, false), new Candidate(Radius + 20, true, true));
            Assert.That(blast.Vetoed, Is.True, "the hit would wake it, even just past the edge");
            Assert.That(blast.Worthwhile, Is.False);
        }

        [Test]
        public void AMonsterNotInTheFightVetoes() =>
            Assert.That(Evaluate(false, new Candidate(100, true, false), new Candidate(150, true, false), new Candidate(200, false, false)).Vetoed, Is.True,
                "the hit would pull it");

        [Test]
        public void FarMonstersAreIgnored() =>
            Assert.That(Evaluate(false, new Candidate(100, true, false), new Candidate(150, true, false), new Candidate(Radius + SafetyMargin + 1, false, true)).Worthwhile,
                Is.True);

        [Test]
        public void AMezIgnoresMonstersAlreadyMesmerized()
        {
            Blast blast = Evaluate(true, new Candidate(100, true, false), new Candidate(150, true, true), new Candidate(200, true, false));
            Assert.That(blast.Vetoed, Is.False);
            Assert.That(blast.Hits, Is.EqualTo(2));
        }

        [Test]
        public void AreaDamageBeatsSingleOnlyWithTheTotal()
        {
            Assert.That(BeatsSingle(120, 2, 200), Is.True);
            Assert.That(BeatsSingle(90, 2, 200), Is.False);
        }

        [Test]
        public void TheSpotWithMostHitsWithinTheWalkWins()
        {
            List<(Blast, float)> spots =
            [
                (new Blast(2, false), 100),
                (new Blast(4, false), 650),
                (new Blast(5, false), PointBlankWalkLimit + 1),
                (new Blast(6, true), 50),
                (new Blast(4, false), 400),
            ];
            Assert.That(ChooseSpot(spots), Is.EqualTo(4), "most hits, then the shorter walk; too far and vetoed spots never");
            Assert.That(ChooseSpot([(new Blast(1, false), 10)]), Is.EqualTo(-1));
        }

        [Test]
        public void AWalkInNeedsAGroupHoldingTheMonsters() => Assert.Multiple(() =>
        {
            Assert.That(MayWalkIn(true, false, false, 1_000, 0, 0), Is.True);
            Assert.That(MayWalkIn(false, false, false, 1_000, 0, 0), Is.False, "alone, nobody holds them");
            Assert.That(MayWalkIn(true, true, false, 1_000, 0, 0), Is.False, "a monster is on the caster");
            Assert.That(MayWalkIn(true, false, true, 1_000, 0, 0), Is.False, "being hit");
            Assert.That(MayWalkIn(true, false, false, 1_000, 5_000, 0), Is.False, "backing off after an interrupted cast");
            Assert.That(MayWalkIn(true, false, false, 1_000, 0, PointBlankFailuresPerFight), Is.False, "failed twice this fight");
        });

        [Test]
        public void CelerityIsAShortGroupHaste()
        {
            Spell celerity = new(new DOL.Database.DbSpell
            {
                Name = "Celerity", Type = eSpellType.CombatSpeedBuff.ToString(), Target = "Group", Duration = 20, Concentration = 0,
            }, 18);
            Spell purity = new(new DOL.Database.DbSpell
            {
                Name = "Purity of Attack", Type = eSpellType.CombatSpeedBuff.ToString(), Target = "Realm", Duration = 0, Concentration = 4,
            }, 10);
            Assert.That(DOL.AI.Brain.BotBrain.IsShortGroupHaste(celerity), Is.True);
            Assert.That(DOL.AI.Brain.BotBrain.IsShortGroupHaste(purity), Is.False);
        }
    }
}
