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
    /// </summary>
    [TestFixture]
    public class UT_SpindlerNewbornFix
    {
        private static GameNPC Npc() => (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        private static GameBot Bot() => (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));

        [Test]
        public void SpindlersTacticFocusesItsNewborns()
        {
            Assert.That(EpicEncounterTactics.For("SpindlerBroodmother")?.FocusRangedAdds, Is.True);
            Assert.That(EpicEncounterTactics.For("GrandSummonerGovannon")?.FocusRangedAdds, Is.False, "others keep one add a party");
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
    }
}
