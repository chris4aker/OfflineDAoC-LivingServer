using System;
using System.IO;
using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using F = DOL.GS.CompanionLeaderFocus;

namespace DOL.GS.Tests;

/// <summary>
/// #571 step 1, leader focus (port of DividedRealms ad8e087c, BotBrain.LeaderFocus.cs): the damage dealers of a real
/// player's group fight the player's target before their own attacker or top threat. One server setting,
/// bot_leader_focus, on by default. Tests only until seen in a PC log.
/// </summary>
[TestFixture]
public class UT_CompanionLeaderFocus571
{
    private sealed class T(string name)
    {
        public override string ToString() => name;
    }

    private static readonly T Protect = new("protection"), Leader = new("leader's target"), Attacker = new("own attacker"),
        Pet = new("pet's fight"), Threat = new("top threat");

    [Test]
    public void OneSettingOnByDefault()
    {
        ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_LEADER_FOCUS))
            .GetCustomAttribute<ServerPropertyAttribute>();
        Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", F.PropertyKey, (object)true)));
    }

    [Test]
    public void ACompanionFightsThePlayersTargetBeforeItsOwnAttacker()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F.Choose(null, Leader, Attacker, Pet, Threat), Is.SameAs(Leader), "the issue's acceptance");
            Assert.That(F.Choose(null, Leader, null, null, Threat), Is.SameAs(Leader), "and before its top threat");
            Assert.That(F.Choose(null, Leader, null, Pet, Threat), Is.SameAs(Leader), "and before its pet's fight");
        });
    }

    [Test]
    public void ATanksProtectionStillComesFirst()
    {
        Assert.That(F.Choose(Protect, Leader, Attacker, Pet, Threat), Is.SameAs(Protect));
    }

    [Test]
    public void WithoutLeaderFocusTheChoiceIsAsBefore()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F.Choose(null, null, Attacker, Pet, Threat), Is.SameAs(Attacker));
            Assert.That(F.Choose(null, null, null, Pet, Threat), Is.SameAs(Pet));
            Assert.That(F.Choose<T>(null, null, null, null, Threat), Is.SameAs(Threat));
            Assert.That(F.Choose<T>(null, null, null, null, null), Is.Null);
        });
    }

    [Test]
    public void OnlyTheDamageDealersOfAPlayersGroup()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F.AppliesTo(true, true, false, false), Is.True, "a damage dealer in the player's group");
            Assert.That(F.AppliesTo(true, true, true, false), Is.False, "a tank holds its fight and peels");
            Assert.That(F.AppliesTo(true, true, false, true), Is.False, "a support heals and buffs");
            Assert.That(F.AppliesTo(true, false, false, false), Is.False, "an autonomous gamebot outside a player's group is unchanged");
            Assert.That(F.AppliesTo(false, true, false, false), Is.False, "the switch off: as before");
        });
    }

    [Test]
    public void TheTargetMustBeOneTheBotMayFightAndNotHeld()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F.Takes(true, true, false, true, false), Is.True);
            Assert.That(F.Takes(false, true, false, true, false), Is.False, "dead, gone or in another region");
            Assert.That(F.Takes(true, false, false, true, false), Is.False, "out of /defensive's range or not attackable");
            Assert.That(F.Takes(true, true, true, true, false), Is.False, "a groupmate's mesmerize or root holds");
            Assert.That(F.Takes(true, true, false, false, false), Is.False, "beyond the bot's aggro distance");
            Assert.That(F.Takes(true, true, false, true, true), Is.False, "an epic rule forbids hitting it (Behemoth, Funus)");
        });
    }

    [Test]
    public void InPveAndRvrAlikeSinceTheOwnersYes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F.InPve, Is.True, "owner, 2026-11-27, #571: leader focus in PvE: yes");
            Assert.That(F.InPlace(true), Is.True, "frontier, battlegrounds, Darkness Falls");
            Assert.That(F.InPlace(false), Is.True, "PvE too");
            Assert.That(F.InPlace(false, inPve: false), Is.False, "with PvE off: realm fights only");
            Assert.That(F.InPlace(false, inPve: true), Is.True);
            Assert.That(F.InPlace(true, inPve: true), Is.True);
        });
    }

    [Test]
    public void TheLogRowIsChangeOnly()
    {
        Assert.Multiple(() =>
        {
            Assert.That(F.Logs(Leader, Attacker, null), Is.True, "turned off its own attacker");
            Assert.That(F.Logs(Leader, Leader, null), Is.False, "the bot would have fought it anyway");
            Assert.That(F.Logs(Leader, Attacker, Leader), Is.False, "already logged");
            Assert.That(F.Logs<T>(null, Attacker, null), Is.False);
            Assert.That(F.Why(Attacker, Attacker, Pet), Is.EqualTo("attacker"));
            Assert.That(F.Why(Pet, null, Pet), Is.EqualTo("pet"), "a pet's fight is not a threat");
            Assert.That(F.Why(Threat, null, null), Is.EqualTo("threat"));
            Assert.That(F.Why<T>(null, null, null), Is.EqualTo("idle"));
        });
    }

    [Test]
    public void TheBrainUsesTheRuleInItsTargetChoice()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root");
        string bots = Path.Combine(dir, "source", "server", "GameServer", "bots");
        string brain = File.ReadAllText(Path.Combine(bots, "BotBrain.cs")).Replace("\r\n", "\n");
        int start = brain.IndexOf("public virtual void AttackMostWanted()", StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThan(0));
        string attack = brain.Substring(start, brain.IndexOf("RedirectPetFromControlledTarget(Body.TargetObject", start, StringComparison.Ordinal) - start);
        string focus = File.ReadAllText(Path.Combine(bots, "BotBrain.LeaderFocus.cs")).Replace("\r\n", "\n");
        Assert.Multiple(() =>
        {
            Assert.That(attack, Does.Contain("GameLiving leaderFocus = protectionTarget == null ? LeaderFocusTarget() : null;"));
            Assert.That(attack, Does.Contain(
                "Body.TargetObject = CompanionLeaderFocus.Choose(protectionTarget, leaderFocus, directAttacker, activePetTarget, threatTarget);"));
            Assert.That(attack, Does.Not.Contain("Body.TargetObject = protectionTarget ?? directAttacker"), "one target choice");
            Assert.That(focus, Does.Contain("CompanionLeaderFocus.AppliesTo(CompanionLeaderFocus.On, bot.IsPlayerLedGroup,"));
            Assert.That(focus, Does.Contain("PlayerLedPullCoordinator.FindLeaderTarget(leader)"), "the player's live fight, not a selection");
            Assert.That(focus, Does.Contain("CanAggroTarget(focus)"), "/defensive, attack and con rules");
            Assert.That(focus, Does.Contain("IsBreakablyControlled(focus)"), "a held target is left alone");
            Assert.That(focus, Does.Contain("EpicEncounterTactics.MustNotHit(bot, focus)"), "an epic must-not-hit rule holds");
            Assert.That(focus, Does.Contain("CompanionLeaderFocus.InPlace(AutonomousWorldBotController.IsBattleground(bot))"),
                "the #663 places");
            Assert.That(focus, Does.Contain("COMPANION_LEADER_FOCUS"));
        });
    }
}
