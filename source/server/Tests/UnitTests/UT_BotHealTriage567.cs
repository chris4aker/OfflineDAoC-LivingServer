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
    public void NeverInARaidAWarbandOrABossFight()
    {
        Assert.Multiple(() =>
        {
            Assert.That(T.Applies(true, false, false), Is.True, "an ordinary party");
            Assert.That(T.Applies(true, true, false), Is.False, "a realm raid or warband: its own healer rules");
            Assert.That(T.Applies(true, false, true), Is.False, "a boss with a tactic");
            Assert.That(T.Applies(false, false, false), Is.False, "the setting off");
        });
    }

    // The acceptance (#567): a healer at a tank's 70% casts the small heal on the tank, not a group heal.
    // A tank at 70% triages at 60% (B2), so it is under the 65% line; a dealer at 62% beside it; 2 or 3 hurt.
    [TestCase(true, 2, false, T.HealPick.Efficient, TestName = "Rules on, two hurt: the small heal")]
    [TestCase(true, 3, true, T.HealPick.Efficient, TestName = "Rules on, three hurt on the pull: the small heal")]
    [TestCase(true, 3, false, T.HealPick.Efficient, TestName = "Rules on, three hurt after the pull: still the small heal first")]
    [TestCase(false, 2, false, T.HealPick.Big, TestName = "Rules off: the big heal, as before")]
    public void TheTankAtSeventyGetsTheSmallHeal(bool rules, int hurt, bool onThePull, T.HealPick expected)
    {
        Assert.That(T.TriageHealth(70, true), Is.LessThan(T.HealerNeedAt(true)), "the tank is under the line");
        Assert.That(T.Prefer(70, true, 62, false), Is.True, "and healed before the dealer at 62%");
        bool groupOk = !rules || T.GroupHealWorthwhile(hurt, true, onThePull ? 1_000 : 6_000);
        Assert.That(T.NonEmergencyPick(rules, 0, false, true, true, true, groupOk), Is.EqualTo(expected));
    }

    [Test]
    public void TheGroupHealOnlyWhenNothingElseFitsAndItIsWorthIt()
    {
        Assert.Multiple(() =>
        {
            // Only a group heal ready: B5 decides.
            Assert.That(T.NonEmergencyPick(true, 0, false, false, false, true, T.GroupHealWorthwhile(2, true, 6_000)),
                Is.EqualTo(T.HealPick.None), "two hurt: no group heal");
            Assert.That(T.NonEmergencyPick(true, 0, false, false, false, true, T.GroupHealWorthwhile(3, true, 1_000)),
                Is.EqualTo(T.HealPick.None), "three hurt on the pull: none yet");
            Assert.That(T.NonEmergencyPick(true, 0, false, false, false, true, T.GroupHealWorthwhile(3, true, 6_000)),
                Is.EqualTo(T.HealPick.Group));
            Assert.That(T.NonEmergencyPick(false, 0, false, false, false, true, true), Is.EqualTo(T.HealPick.Group), "off: as before");
            Assert.That(T.NonEmergencyPick(true, 0, true, true, true, true, true), Is.EqualTo(T.HealPick.OverTime),
                "a heal over time not yet running comes first, as before");
            Assert.That(T.NonEmergencyPick(true, 1, false, true, true, false, false), Is.EqualTo(T.HealPick.Big),
                "with an emergency elsewhere the big heal keeps its place (B3 is outside emergencies)");
        });
    }

    [Test]
    public void AnEmergencyIsHealedAsBeforeButNoGroupHealOnThePull()
    {
        Assert.Multiple(() =>
        {
            Assert.That(T.EmergencyPick(true, true, false, true, true, true, true), Is.EqualTo(T.HealPick.Instant));
            Assert.That(T.EmergencyPick(false, true, false, true, true, true, true), Is.EqualTo(T.HealPick.InstantGroup));
            Assert.That(T.EmergencyPick(false, true, false, true, true, true, false), Is.EqualTo(T.HealPick.Big),
                "B5: no instant group heal on the pull; the big heal instead");
            Assert.That(T.EmergencyPick(false, false, false, false, false, true, false), Is.EqualTo(T.HealPick.None),
                "B5: no group heal on the pull");
            Assert.That(T.EmergencyPick(false, false, true, true, true, true, true), Is.EqualTo(T.HealPick.None),
                "a heal already casting finishes");
            Assert.That(T.EmergencyPick(false, false, false, false, true, true, true), Is.EqualTo(T.HealPick.Efficient));
        });
    }

    [Test]
    public void TheHealerUsesTheRulesOnlyBehindTheSetting()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root");
        string brain = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")).Replace("\r\n", "\n");
        int start = brain.IndexOf("public bool CheckHeals()", StringComparison.Ordinal);
        string heals = brain.Substring(start, brain.IndexOf("// Cast the selected spell", start, StringComparison.Ordinal) - start);
        Assert.Multiple(() =>
        {
            Assert.That(heals, Does.Contain("bool raidSupport = AutonomousRealmRaid.HasSharedSupport(BotBody);"));
            Assert.That(heals, Does.Contain("bool periodRules = BotHealTriage165.Applies(BotHealTriage165.On, raidSupport,"), "raid gate");
            Assert.That(heals, Does.Contain("EpicEncounterTactics.For(healerTarget) != null"), "boss gate");
            Assert.That(heals, Does.Contain("if (raidSupport)"), "the raid patient uses the same value");
            Assert.That(heals, Does.Contain("!EpicEncounterTactics.GreenKnightHoldsHeal(BotBody, tank) &&"), "the Green Knight hold before B4");
            Assert.That(heals, Does.Not.Contain(".OrderBy(member => member.HealthPercent)"), "B4 walks the group once");
            Assert.That(heals, Does.Contain("else if (IsHealer && triage < healerNeedAt)"), "B1");
            Assert.That(heals, Does.Contain("triage < TriageHealth(spellTarget)"), "B2");
            Assert.That(heals, Does.Contain("BotHealTriage165.NonEmergencyPick(periodRules, numEmergency,"), "B3");
            Assert.That(heals, Does.Contain("BotHealTriage165.PreHotTank("), "B4");
            Assert.That(heals, Does.Contain("BotHealTriage165.EmergencyPick(canCastInstantHeal, canCastInstantGroupHeal, isCastingHeal,"), "B5");
            Assert.That(heals, Does.Contain("bool groupHealOk = !periodRules ||"), "off: the group heal as before");
        });
    }
}
