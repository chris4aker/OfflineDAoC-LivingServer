using System;
using System.IO;
using DOL.AI.Brain;
using DOL.Database;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// #567 A3 (port of True165 BotHotPrecheck, 03dce76ee): a heal over time or a regeneration is held back only by an effect
/// the server would make it conflict with (SpellHandler.HasConflictingEffectWith), and not cast over a groupmate's.
/// </summary>
[TestFixture]
public class UT_BotHotPrecheck567
{
    private static Spell Of(int id, string type, int effectGroup) =>
        new(new DbSpell { SpellID = id, Type = type, Target = "Realm", Value = 10, EffectGroup = effectGroup }, 50);

    [Test]
    public void ASongOfRestNoLongerBlocksAShamansRegeneration()
    {
        Spell regeneration = Of(1, "HealthRegenBuff", 71);
        Spell songOfRest = Of(2, "HealthRegenBuff", 72);
        Assert.That(BotBrain.HotBlockedBy(regeneration, songOfRest), Is.False, "different effect groups stack on the server");
    }

    [Test]
    public void AConflictingEffectStillBlocks()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotBrain.HotBlockedBy(Of(1, "HealOverTime", 0), Of(1, "HealOverTime", 0)), Is.True, "the same spell");
            Assert.That(BotBrain.HotBlockedBy(Of(1, "HealOverTime", 0), Of(3, "HealOverTime", 0)), Is.True,
                "no groups: the same spell type conflicts");
            Assert.That(BotBrain.HotBlockedBy(Of(1, "HealthRegenBuff", 71), Of(4, "HealthRegenBuff", 71)), Is.True, "the same group");
            Assert.That(BotBrain.HotBlockedBy(Of(1, "HealOverTime", 5), Of(3, "HealOverTime", 0)), Is.False,
                "one has a group, the other none: no conflict");
            Assert.That(BotBrain.HotBlockedBy(Of(1, "HealOverTime", 0), null), Is.True, "an effect with no spell stays a block");
        });
    }

    [Test]
    public void TheHealerChecksBothBeforeItsHealOverTime()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root");
        string brain = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")).Replace("\r\n", "\n");
        Assert.That(brain, Does.Contain("!LivingHasEffect(spellTarget, BotBody.HealOverTime) &&\n                        " +
                                        "!GroupmateCastsHotAt(spellTarget, BotBody.HealOverTime)"));
        int living = brain.IndexOf("public bool LivingHasEffect(GameLiving target, Spell spell)", StringComparison.Ordinal);
        int hot = brain.IndexOf("if (spellEffect is eEffect.HealOverTime or eEffect.HealthRegenBuff)", living, StringComparison.Ordinal);
        int dot = brain.IndexOf("if (spellEffect == eEffect.DamageOverTime)", living, StringComparison.Ordinal);
        Assert.That(hot, Is.GreaterThan(living).And.LessThan(dot), "the heal-over-time rule sits beside the DoT rule");
        Assert.That(brain.Substring(hot, dot - hot), Does.Contain("HotBlockedBy(spell, effect?.SpellHandler?.Spell)"));
    }
}
