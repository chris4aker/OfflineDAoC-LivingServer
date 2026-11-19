using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>sh-bosses M2: a pet is never sent at Summoner Lossren, who takes 5 from summoned pets.</summary>
    [TestFixture]
    public class UT_BotPetsAtUselessBoss
    {
        [Test]
        public void LossrenIsABossPetsCannotHurt() =>
            Assert.That(BotBrain.PetsDoNoDamageTo((GameLiving)RuntimeHelpers.GetUninitializedObject(typeof(SummonerLossren))), Is.True);

        [Test]
        public void OtherMonstersAndHisSoulsAreFairGame()
        {
            Assert.That(BotBrain.PetsDoNoDamageTo((GameLiving)RuntimeHelpers.GetUninitializedObject(typeof(TorturedSouls))), Is.False);
            Assert.That(BotBrain.PetsDoNoDamageTo((GameLiving)RuntimeHelpers.GetUninitializedObject(typeof(SummonerRoesia))), Is.False);
            Assert.That(BotBrain.PetsDoNoDamageTo(null), Is.False);
        }
    }
}
