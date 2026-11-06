using System.Reflection;
using DOL.Database;
using DOL.GS;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Water breathing (Nereid potions) did nothing: its handler only had the old effect system's OnEffectStart, which the
    // server no longer runs. It now makes its own ECS effect (Rmarcoux86's fix, 016e2078).
    [TestFixture]
    public class UT_WaterBreathingEffect
    {
        [Test]
        public void Handler_MakesItsOwnEcsEffect()
        {
            MethodInfo create = typeof(WaterBreathingSpellHandler).GetMethod(nameof(SpellHandler.CreateECSEffect));
            Assert.That(create, Is.Not.Null);
            Assert.That(create.DeclaringType, Is.EqualTo(typeof(WaterBreathingSpellHandler)),
                "the base handler's generic effect does nothing for water breathing");
        }

        [Test]
        public void Effect_IsAWaterBreathingSpellEffect()
        {
            Spell spell = new(new DbSpell { SpellID = 99_871, Name = "Nereid test", Type = "WaterBreathing", Target = "Self",
                Value = 50, Duration = 600 }, 1);
            var handler = new WaterBreathingSpellHandler(null, spell, new SpellLine("test", "test", "", true));
            var effect = new WaterBreathingECSGameEffect(new ECSGameEffectInitParams(null, 600_000, 1.0, handler));
            Assert.That(effect, Is.InstanceOf<ECSGameSpellEffect>());
            Assert.That(effect.EffectType, Is.EqualTo(eEffect.WaterBreathing));
            Assert.That(effect.SpellHandler, Is.SameAs(handler));
        }
    }
}
