using System;
using System.IO;
using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using T = DOL.GS.BotHealTriage165;

namespace DOL.GS.Tests;

/// <summary>
/// #567 B1-B5 (owner decision 2026-11-26; port of True165 BotHealTriage165, 1add69478): the 1.65 healer behind one server
/// setting, bot_healer_period_rules, on by default.
/// </summary>
[TestFixture]
public class UT_BotHealTriage567
{
    [Test]
    public void OneSettingOnByDefault()
    {
        ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_HEALER_PERIOD_RULES))
            .GetCustomAttribute<ServerPropertyAttribute>();
        Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", T.PropertyKey, (object)true)));
    }

    [Test]
    public void B1_AHealerStartsAtTwoThirds()
    {
        Assert.That(T.HealerNeedAt(true), Is.EqualTo(65));
        Assert.That(T.HealerNeedAt(false), Is.EqualTo(80), "with the setting off, as before");
    }

    [Test]
    public void B2_TheTankFirst()
    {
        Assert.Multiple(() =>
        {
            Assert.That(T.TriageHealth(70, true), Is.EqualTo(60), "a tank at 70% counts as 60%: under the 65% line");
            Assert.That(T.TriageHealth(70, false), Is.EqualTo(70));
            Assert.That(T.TriageHealth(45, true), Is.EqualTo(40), "never into the emergency band");
            Assert.That(T.TriageHealth(39, true), Is.EqualTo(39), "an emergency stays an emergency");
            Assert.That(T.Prefer(70, true, 62, false), Is.True, "the tank at 70% before a damage dealer at 62%");
            Assert.That(T.Prefer(70, true, 55, false), Is.False, "but not before one at 55%");
            Assert.That(BotPartyRoles.For(eCharacterClass.Armsman), Is.EqualTo(BotPartyRole.Tank));
            Assert.That(BotPartyRoles.For(eCharacterClass.Paladin), Is.EqualTo(BotPartyRole.Tank));
        });
    }

    [Test]
    public void B4_APreHotOnTheTankAsTheFightWearsItDown()
    {
        Assert.Multiple(() =>
        {
            Assert.That(T.PreHotTank(true, true, 85, false, 80), Is.True);
            Assert.That(T.PreHotTank(true, true, 95, false, 80), Is.False, "not hurt enough yet");
            Assert.That(T.PreHotTank(true, true, 35, false, 80), Is.False, "an emergency gets a real heal");
            Assert.That(T.PreHotTank(true, true, 85, true, 80), Is.False, "one running or coming");
            Assert.That(T.PreHotTank(true, true, 85, false, 40), Is.False, "the healer keeps its power");
            Assert.That(T.PreHotTank(false, false, 85, false, 80), Is.False, "out of a fight");
            Assert.That(T.PreHotHealerFree(true, true, true), Is.True, "an instant one never stops the healer");
            Assert.That(T.PreHotHealerFree(false, false, false), Is.True);
            Assert.That(T.PreHotHealerFree(false, true, false), Is.False, "a cast-time one never stops a swing");
            Assert.That(T.PreHotHealerFree(false, false, true), Is.False, "or a walk");
        });
    }

    [Test]
    public void B5_NoGroupHealOnThePull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(T.GroupHealWorthwhile(2, false, 0), Is.False, "two hurt: a single heal");
            Assert.That(T.GroupHealWorthwhile(3, true, 3_999), Is.False, "the pull");
            Assert.That(T.GroupHealWorthwhile(3, true, 4_000), Is.True);
            Assert.That(T.GroupHealWorthwhile(3, false, 0), Is.True, "after the fight");
        });
    }

    [Test]
    public void TheHealerUsesTheRulesOnlyBehindTheSetting()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        string brain = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs"));
        int start = brain.IndexOf("public bool CheckHeals()", StringComparison.Ordinal);
        string heals = brain.Substring(start, brain.IndexOf("// Cast the selected spell", start, StringComparison.Ordinal) - start);
        Assert.Multiple(() =>
        {
            Assert.That(heals, Does.Contain("bool periodRules = BotHealTriage165.On;"));
            Assert.That(heals, Does.Contain("else if (IsHealer && triage < healerNeedAt)"), "B1");
            Assert.That(heals, Does.Contain("triage < TriageHealth(spellTarget)"), "B2");
            Assert.That(heals, Does.Contain("else if (periodRules && numEmergency == 0 && CheckHealSpell(BotBody.HealEfficient))"), "B3");
            Assert.That(heals, Does.Contain("BotHealTriage165.PreHotTank("), "B4");
            Assert.That(heals, Does.Contain("else if (canCastInstantGroupHeal && groupHealOk)"), "B5");
            Assert.That(heals, Does.Contain("CheckHealSpell(BotBody.HealGroup) && groupHealOk"), "B5");
            Assert.That(heals, Does.Contain("bool groupHealOk = !periodRules ||"), "off: the group heal as before");
        });
    }
}
