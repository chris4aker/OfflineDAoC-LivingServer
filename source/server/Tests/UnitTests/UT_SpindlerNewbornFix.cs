using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Spindler Broodmother (play test duo3, Galladoria, 2026-11-07): her newborn spindlers (1-2 every half minute, 8,000
    /// health, they go for bards, druids, mentalists and wardens) piled up to 11 alive while the raid's casters and archers
    /// were split one party per add, and killed 62 of the 120 raiders who died in attempt 2. Now the casters and archers all
    /// take one newborn (the one with the least health left) and a party's melee peel a newborn off their own party's casters and healers.
    /// Round 3 (owner 2026-11-08): 2-3 newborn-only parties and area spells on her spot (EpicEncounterTactics.AddParties.cs).
    /// </summary>
    [TestFixture]
    public class UT_SpindlerNewbornFix
    {
        private static GameNPC Npc() => (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        private static GameBot Bot() => (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));

        [Test]
        public void SpindlersTacticFocusesItsNewborns()
        {
            // Round 3 (owner 2026-11-08): newborn-only parties and area spells on her spot replace the raid-wide focus.
            var spindler = EpicEncounterTactics.For("SpindlerBroodmother");
            Assert.That(spindler?.FocusRangedAdds, Is.False);
            Assert.That(spindler?.AddParties, Is.EqualTo(3));
            Assert.That(spindler?.AreaOnClump, Is.True);
            Assert.That(EpicEncounterTactics.For("GrandSummonerGovannon")?.FocusRangedAdds, Is.False, "others keep one add a party");
            Assert.That(EpicEncounterTactics.Catalog, Has.Exactly(1).Matches<EpicEncounterTactics.Tactic>(t => t.AddParties > 0 || t.AreaOnClump),
                "gated to Spindler: area choice is shared caster AI");
        }

        [Test]
        public void TheCastersAndArchersAllFinishTheSameNewborn()
        {
            // Play test duo5: "the one on a healer first" changed with every newborn's target, and the casters still spread
            // over 3-8 of them. The one with the least health left is the same one for every caster until it dies.
            GameNPC a = Npc(), b = Npc(), c = Npc();
            var health = new System.Collections.Generic.Dictionary<GameNPC, int> { [a] = 100, [b] = 40, [c] = 70 };
            Assert.That(EpicEncounterTactics.FocusedRangedAdd([a, b, c], n => health[n]), Is.SameAs(b));
            health[b] = 100;
            Assert.That(EpicEncounterTactics.FocusedRangedAdd([a, b, c], n => health[n]), Is.SameAs(c));
            health[c] = 100;
            Assert.That(EpicEncounterTactics.FocusedRangedAdd([a, b, c], n => health[n]), Is.SameAs(a), "all full: a fixed order");
        }

        [Test]
        public void APartysMeleePeelANewbornOffTheirOwnCastersAndHealers()
        {
            GameBot melee = Bot(), ownDruid = Bot(), otherDruid = Bot(), ownHero = Bot();
            GameNPC onOther = Npc(), onOwnHero = Npc(), onOwnDruid = Npc();
            onOther.TargetObject = otherDruid; onOwnHero.TargetObject = ownHero; onOwnDruid.TargetObject = ownDruid;
            bool Healer(GameLiving l) => l == ownDruid || l == otherDruid;
            bool Party(GameLiving l) => l == ownDruid || l == ownHero;
            Assert.That(EpicEncounterTactics.PeeledRangedAdd(melee, [onOther, onOwnHero, onOwnDruid], Party, Healer), Is.SameAs(onOwnDruid));
            Assert.That(EpicEncounterTactics.PeeledRangedAdd(melee, [onOther, onOwnHero], Party, Healer), Is.Null,
                "another party's healer, or a non-healer of its own: stay on the boss");
        }
        [Test]
        public void TwoOrThreePartiesWithHealersGoOnTheNewborns()
        {
            Assert.That(EpicEncounterTactics.AddPartyCount(13, 3), Is.EqualTo(2), "era 100: 13 parties");
            Assert.That(EpicEncounterTactics.AddPartyCount(16, 3), Is.EqualTo(3), "start 125: 16 parties");
            Assert.That(EpicEncounterTactics.AddPartyCount(22, 3), Is.EqualTo(3), "175%");
            Assert.That(EpicEncounterTactics.AddPartyCount(4, 3), Is.EqualTo(1), "never more than a third");
            Assert.That(EpicEncounterTactics.AddPartyCount(1, 3), Is.EqualTo(0));
            bool[] healers = [true, true, false, true, false, true];
            Assert.That(EpicEncounterTactics.AddPartyPicks(healers, 2), Is.EqualTo(new[] { 5, 3 }), "the last ones with a healer");
            Assert.That(EpicEncounterTactics.AddPartyPicks([false, false, true], 2), Is.EqualTo(new[] { 2, 1 }), "then any");
        }

        [Test]
        public void AnAddPartyTakesTheNewbornsOnHealersFirst()
        {
            GameBot druid = Bot(), hero = Bot();
            GameNPC onHero = Npc(), onDruidFull = Npc(), onDruidHurt = Npc();
            onHero.TargetObject = hero; onDruidFull.TargetObject = druid; onDruidHurt.TargetObject = druid;
            var health = new System.Collections.Generic.Dictionary<GameNPC, int> { [onHero] = 10, [onDruidFull] = 90, [onDruidHurt] = 50 };
            bool Soft(GameLiving l) => l == druid;
            Assert.That(EpicEncounterTactics.AddPartyTarget([onHero, onDruidFull, onDruidHurt], Soft, n => health[n]), Is.SameAs(onDruidHurt));
            Assert.That(EpicEncounterTactics.AddPartyTarget([onHero, onDruidFull], l => false, n => health[n]), Is.SameAs(onHero),
                "none on a healer: the one with the least health");
            Assert.That(EpicEncounterTactics.AddPartyTarget([], Soft), Is.Null);
        }

        [Test]
        public void TheAreaGoesWhereTheMostStandTogether()
        {
            // Her (index 0) with two newborns just born beside her, and three newborns bunched on a druid 900 away.
            System.Numerics.Vector3 her = new(21000, 53000, 0);
            var points = new[]
            {
                her, her + new System.Numerics.Vector3(60, 40, 0), her + new System.Numerics.Vector3(-50, 70, 0),
                her + new System.Numerics.Vector3(900, 0, 0), her + new System.Numerics.Vector3(950, 60, 0), her + new System.Numerics.Vector3(880, -70, 0),
                her + new System.Numerics.Vector3(930, 20, 0),
            };
            Assert.That(EpicEncounterTactics.BestClump(points, 350), Is.EqualTo((3, 4)), "the bunch on the druid: 4 in reach");
            Assert.That(EpicEncounterTactics.BestClump(points[..3], 350), Is.EqualTo((0, 3)), "her spot: ties go to her");
            Assert.That(EpicEncounterTactics.BestClump([her, her + new System.Numerics.Vector3(2000, 0, 0)], 350).Hits, Is.EqualTo(1),
                "nothing together: no area target (the caster fights on)");
        }
    }
}
