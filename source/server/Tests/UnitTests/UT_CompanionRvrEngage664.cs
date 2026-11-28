using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using E = DOL.GS.CompanionRvrEngage;

namespace DOL.GS.Tests;

/// <summary>
/// #664 step 1: the player's group picks and engages enemies on its own in the frontier, the battlegrounds and Darkness
/// Falls (CompanionRvrEngage): the perception bound around the player, the period kill order, the size-up and the switch.
/// The pure rules; the world wiring is UT_CompanionRvrEngage664Harness. Tests only until seen in a PC log.
/// </summary>
[TestFixture]
public class UT_CompanionRvrEngage664
{
    private static CompanionRvrCandidate C(RvrRole role, int fromPlayer, int health = 100, bool noticed = true, bool controlled = false) =>
        new(E.Rank(role), noticed, controlled, health, fromPlayer);

    [Test]
    public void OneSettingOnByDefault()
    {
        ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_COMPANION_RVR_ENGAGE))
            .GetCustomAttribute<ServerPropertyAttribute>();
        Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", E.PropertyKey, (object)true)));
    }

    [Test]
    public void TheReleaseWorldShipsItOn()
    {
        string root = TestContext.CurrentContext.TestDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "source", "tools", "release-server-settings.json")))
            root = Path.GetDirectoryName(root);
        Assume.That(root, Is.Not.Null, "the repository's release settings");
        string json = File.ReadAllText(Path.Combine(root, "source", "tools", "release-server-settings.json"));
        Assert.That(json, Does.Contain("\"" + E.PropertyKey + "\": \"True\""));
    }

    [Test]
    public void OnlyInTheWarZonesWithTheSwitchOn()
    {
        Assert.Multiple(() =>
        {
            Assert.That(E.Applies(true, true), Is.True, "frontier, battlegrounds, Darkness Falls");
            Assert.That(E.Applies(true, false), Is.False, "PvE stays as it is (#663 answer 2)");
            Assert.That(E.Applies(false, true), Is.False, "the switch off: today's rules");
        });
    }

    [Test]
    public void TheBoundIsMeasuredFromThePlayerAndDefensiveIsTighter()
    {
        // #664 acceptance: /aggressive engages at 1,500; /defensive at 800 and not at 1,500.
        Assert.Multiple(() =>
        {
            Assert.That(E.Eligible(C(RvrRole.Melee, 1_500), E.Bound(defensive: false)), Is.True);
            Assert.That(E.Eligible(C(RvrRole.Melee, 800), E.Bound(defensive: true)), Is.True);
            Assert.That(E.Eligible(C(RvrRole.Melee, 1_500), E.Bound(defensive: true)), Is.False);
            Assert.That(E.Eligible(C(RvrRole.Melee, 2_500), E.Bound(defensive: false)), Is.False, "never far from the player");
            Assert.That(E.Bound(true), Is.GreaterThan(CompanionEngagementMode.DefensiveRadius), "far wider than the PvE 350");
            Assert.That(E.Bound(false), Is.LessThanOrEqualTo(BotBrain.GROUP_DEFENSE_ASSIST_RADIUS), "within the group's assist reach");
        });
    }

    [Test]
    public void OnlyWhatTheGroupHasNoticed()
    {
        Assert.That(E.Eligible(C(RvrRole.Healer, 500, noticed: false), E.AggressiveBound), Is.False,
            "an enemy in range but not noticed yet (the notice curve, or a stealther beyond the close-range rule) is not picked");
        Assert.That(E.Pick(new[] { C(RvrRole.Healer, 500, noticed: false) }, E.AggressiveBound), Is.EqualTo(-1));
    }

    [Test]
    public void TheKillOrderHealersCastersFightersPets()
    {
        Assert.Multiple(() =>
        {
            Assert.That(E.Rank(RvrRole.Healer), Is.EqualTo(0));
            Assert.That(E.Rank(RvrRole.Support), Is.EqualTo(0), "the hybrid healers (Shaman, Friar, Bard...) with them");
            Assert.That(E.Rank(RvrRole.Caster), Is.EqualTo(1));
            Assert.That(E.Rank(RvrRole.Tank), Is.EqualTo(2));
            Assert.That(E.Rank(RvrRole.Melee), Is.EqualTo(2));
            Assert.That(E.Rank(RvrRole.Archer), Is.EqualTo(2));
            Assert.That(E.Rank(RvrRole.Pet), Is.EqualTo(3));
        });
        var field = new[] { C(RvrRole.Tank, 300), C(RvrRole.Pet, 200), C(RvrRole.Caster, 1_200), C(RvrRole.Healer, 1_800) };
        Assert.That(E.Pick(field, E.AggressiveBound), Is.EqualTo(3), "the healer first, though furthest");
        Assert.That(E.Pick(field, E.DefensiveBound), Is.EqualTo(0), "on /defensive the healer and caster are out of bounds: the tank, not the pet");
    }

    [Test]
    public void ThenTheLowestHealthThenTheNearestThePlayer()
    {
        Assert.That(E.Pick(new[] { C(RvrRole.Melee, 300, 90), C(RvrRole.Melee, 900, 40) }, E.AggressiveBound), Is.EqualTo(1));
        Assert.That(E.Pick(new[] { C(RvrRole.Melee, 900), C(RvrRole.Melee, 300) }, E.AggressiveBound), Is.EqualTo(1));
    }

    [Test]
    public void AHeldEnemyWaitsWhileAFreeOneIsThere()
    {
        var field = new[] { C(RvrRole.Healer, 300, controlled: true), C(RvrRole.Tank, 900) };
        Assert.That(E.Pick(field, E.AggressiveBound), Is.EqualTo(1), "never break a groupmate's mez while a free enemy is in the fight");
        Assert.That(E.Pick(new[] { C(RvrRole.Healer, 300, controlled: true) }, E.AggressiveBound), Is.EqualTo(0),
            "mid-fight, the last one left is fought, as a player does");
    }

    [Test]
    public void TheGroupNeverOpensOnAHeldEnemy()
    {
        // Review of e0027909: with no fight on, the group opened on an enemy the player or a groupmate had just mezzed
        // (mez and run). Opening, a held enemy is never taken, even alone; mid-fight the last-one-left rule stays.
        var alone = new[] { C(RvrRole.Healer, 300, controlled: true) };
        Assert.That(E.Pick(alone, E.AggressiveBound, opening: true), Is.EqualTo(-1), "opening: the held enemy is left alone");
        Assert.That(E.Pick(alone, E.AggressiveBound, opening: false), Is.EqualTo(0), "mid-fight: the last one left");
        var field = new[] { C(RvrRole.Healer, 300, controlled: true), C(RvrRole.Tank, 900) };
        Assert.That(E.Pick(field, E.AggressiveBound, opening: true), Is.EqualTo(1), "opening: the free one");
    }

    [Test]
    public void TheGroupNeverOpensInASafeAreaOrUnderGuards()
    {
        Assert.Multiple(() =>
        {
            Assert.That(E.MayOpenOn(safeArea: false, underHostileGuards: false), Is.True);
            Assert.That(E.MayOpenOn(safeArea: true, underHostileGuards: false), Is.False, "a border, safe or bind area");
            Assert.That(E.MayOpenOn(safeArea: false, underHostileGuards: true), Is.False, "under its tower or keep guards");
            Assert.That(E.GuardCover(1_850), Is.EqualTo(1_850), "the guard's own aggro range");
            Assert.That(E.GuardCover(0), Is.EqualTo(E.DefaultGuardCover), "no range from the brain: the keep guards' 1,800");
            Assert.That(E.GuardCover(5_000), Is.EqualTo(E.MaxGuardCover), "at most the archers' 2,100");
        });
    }

    [Test]
    public void ThePickIsStickyUntilABetterRoleAppears()
    {
        var field = new[] { C(RvrRole.Melee, 900, 100), C(RvrRole.Melee, 300, 20) };
        Assert.That(E.Pick(field, E.AggressiveBound, current: 0), Is.EqualTo(0), "no switching between two fighters");
        var healer = new[] { C(RvrRole.Melee, 900), C(RvrRole.Healer, 1_500) };
        Assert.That(E.Pick(healer, E.AggressiveBound, current: 0), Is.EqualTo(1), "a healer comes in: switch (the kill order)");
        var held = new[] { C(RvrRole.Healer, 300, controlled: true), C(RvrRole.Tank, 900) };
        Assert.That(E.Pick(held, E.AggressiveBound, current: 0), Is.EqualTo(1), "a pick a groupmate has since mezzed is left");
        var gone = new[] { C(RvrRole.Melee, 1_900), C(RvrRole.Tank, 600) };
        Assert.That(E.Pick(gone, E.DefensiveBound, current: 0), Is.EqualTo(1), "a pick that ran past the bound is let go");
    }

    private static RvrSightFacts Fight(int ours, int theirs, int healers = 0, int level = 22) => new()
    {
        OwnLevel = level, Allies = ours, Fighters = ours, AllyAverageLevel = level,
        Enemies = theirs, EnemyHealers = healers, EnemyAverageLevel = level, EnemyTopLevel = level,
    };

    [Test]
    public void TheGroupNeverOpensOnAClearlyLosingFight()
    {
        Assert.That(RvrSightSizeUp.LineFor(E.SizeUpBoldness), Is.EqualTo(0.64).Within(1e-9), "the boldest gamebot line (owner question on #664)");
        Assert.Multiple(() =>
        {
            Assert.That(E.SizeUp(Fight(4, 1)).Call, Is.EqualTo(RvrSightCall.Engage));
            Assert.That(E.SizeUp(Fight(4, 4)).Call, Is.EqualTo(RvrSightCall.Engage), "a fair fight");
            Assert.That(E.SizeUp(Fight(4, 6)).Call, Is.EqualTo(RvrSightCall.Engage), "4 against 6: odds 0.67, not clearly losing");
            Assert.That(E.SizeUp(Fight(4, 7)).Call, Is.EqualTo(RvrSightCall.BackOff), "4 against 7: clearly losing");
            Assert.That(E.SizeUp(Fight(2, 8)).Call, Is.EqualTo(RvrSightCall.BackOff), "a duo against a bot eight");
            Assert.That(E.SizeUp(Fight(4, 5)).Call, Is.EqualTo(RvrSightCall.Engage), "4 against 5: odds 0.8");
            Assert.That(E.SizeUp(Fight(4, 5, healers: 2)).Call, Is.EqualTo(RvrSightCall.BackOff), "main healers count twice");
            Assert.That(E.SizeUp(Fight(4, 2) with { EnemyAverageLevel = 40, EnemyTopLevel = 40 }).Call,
                Is.EqualTo(RvrSightCall.BackOff), "two enemies of 40 against four of 22");
        });
    }

    [Test]
    public void TheSizeUpIsAboutOpeningNeverAboutDefending()
    {
        // Being attacked goes through the threat path (CompanionPvpEngagement.RecordThreat), above the group's own pick, so
        // the size-up never makes the group stand still under attack; it is never asked to wave an attack through either.
        RvrSightDecision decision = E.SizeUp(Fight(2, 8) with { Attacked = true });
        Assert.That(decision.Call, Is.EqualTo(RvrSightCall.BackOff));
        Assert.That(decision.Mistake, Is.False, "no misreads for the player's group");
    }

    [Test]
    public void WhoJoins()
    {
        Assert.Multiple(() =>
        {
            Assert.That(E.Joins(true, false, false, false), Is.True);
            Assert.That(E.Joins(false, false, false, false), Is.False, "not in the player's group: autonomous gamebots unchanged");
            Assert.That(E.Joins(true, true, false, false), Is.False, "/bot stance passive");
            Assert.That(E.Joins(true, false, true, false), Is.False, "/bot hold");
            Assert.That(E.Joins(true, false, false, true), Is.False, "/bot stay");
        });
    }

    [Test]
    public void TheLogRowIsChangeOnly()
    {
        object a = new(), b = new();
        Assert.Multiple(() =>
        {
            Assert.That(E.Logs("engage", a, null, null), Is.True);
            Assert.That(E.Logs("engage", a, "engage", a), Is.False);
            Assert.That(E.Logs("engage", b, "engage", a), Is.True);
            Assert.That(E.Logs("hold", a, "engage", a), Is.True);
        });
    }

    [Test]
    public void TheWiringReplacesTheRadarCirclesOnlyWhereTheGroupPickRuns()
    {
        string root = TestContext.CurrentContext.TestDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "source", "server", "GameServer", "bots")))
            root = Path.GetDirectoryName(root);
        Assume.That(root, Is.Not.Null, "the repository's server source");
        string observe = File.ReadAllText(Path.Combine(root, "source", "server", "GameServer", "bots", "CompanionPvpEngagement.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(observe, Does.Contain("if (CompanionRvrEngage.Applies(leader))"));
            Assert.That(observe, Does.Contain("CompanionRvrEngage.Choose(leader, CompanionEngagementMode.PlayerDefensive(leader))"));
            Assert.That(observe.IndexOf("CompanionRvrEngage.Choose("), Is.GreaterThan(observe.IndexOf("PlayerLedPullCoordinator.FindLeaderTarget(leader)")),
                "the player's own target and the group's attackers come first");
        });
    }

    [Test]
    public void TheGroupsOwnPickPassesOpeningWhenItHasNoPickYet()
    {
        // Review of 64c0cfc9: TheGroupNeverOpensOnAHeldEnemy sets the flag by hand, so this pins the call site that sets it
        // in the game. With "opening: false" here the group would open on a mezzed enemy again.
        string root = TestContext.CurrentContext.TestDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "source", "server", "GameServer", "bots")))
            root = Path.GetDirectoryName(root);
        Assume.That(root, Is.Not.Null, "the repository's server source");
        string world = File.ReadAllText(Path.Combine(root, "source", "server", "GameServer", "bots", "CompanionRvrEngage.World.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(world, Does.Contain("int chosen = Pick(candidates, bound, current, opening: party.Pick == null);"),
                "no pick yet means the group is opening a fight");
            Assert.That(world.Split("opening:").Length - 1, Is.EqualTo(1), "one call passes the flag");
            Assert.That(world, Does.Contain("new CompanionRvrCandidate(Rank(role), ready, BotBrain.IsBreakablyControlled(target),"),
                "a candidate is held when a breakable mez or root is on it");
        });
    }
}
