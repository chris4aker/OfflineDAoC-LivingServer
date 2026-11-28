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
            // #667: the group's own pick also stands aside under the assist and regroup orders (ownPick).
            Assert.That(observe, Does.Contain("if (ownPick && CompanionRvrEngage.Applies(leader))"));
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
            // Step 2 (review of #765): opening is "no pick and nobody fighting", not "no pick" alone.
            Assert.That(world, Does.Contain("bool opening = Opening(party.Pick != null, InEnemyFight(leader));"),
                "no pick and no fight means the group is opening one");
            Assert.That(world, Does.Contain("int chosen = Pick(candidates, bound, current, opening: opening, leash: leash);"),
                "the pick is told whether it opens, and keeps its pick out to the leash");
            Assert.That(world, Does.Contain("bool sized = opening;"), "only an opening is sized up");
            Assert.That(world.Split("opening:").Length - 1, Is.EqualTo(1), "one call passes the flag");
            Assert.That(world, Does.Contain("new CompanionRvrCandidate(Rank(role), ready, BotBrain.IsBreakablyControlled(target),"),
                "a candidate is held when a breakable mez or root is on it");
        });
    }

    // ---- #664 step 2: /hold, the drop leash, what opening means, the sight memory --------------------------------------

    [Test]
    public void TheLeashIsTheBoundPlusAMargin()
    {
        Assert.Multiple(() =>
        {
            Assert.That(E.Leash(defensive: false), Is.EqualTo(E.AggressiveBound + E.LeashMargin));
            Assert.That(E.Leash(defensive: true), Is.EqualTo(E.DefensiveBound + E.LeashMargin));
            Assert.That(E.Leash(true), Is.LessThan(E.Leash(false)), "/defensive is tighter");
        });
    }

    [Test]
    public void UnderHoldABotOnlyDefendsItself()
    {
        // Owner answer 3 on #664 (2026-11-26 22:23): "Besides /defensive, companions only defend themselves".
        Assert.Multiple(() =>
        {
            Assert.That(E.MayFight(holding: true, hitsMe: true, warZone: true, enemyRealm: true, fromPlayer: 300, leash: 2_250,
                playersTarget: false, hitsGroup: true), Is.True, "what hits the bot (or its pet) is fought");
            Assert.That(E.MayFight(true, false, true, true, 300, 2_250, playersTarget: false, hitsGroup: true), Is.False,
                "an enemy on a groupmate is not");
            Assert.That(E.MayFight(true, false, true, true, 300, 2_250, playersTarget: true, hitsGroup: false), Is.False,
                "nor the player's own target");
            Assert.That(E.MayFight(true, false, warZone: false, enemyRealm: false, 100, 0, false, false), Is.False,
                "anywhere: a monster that isn't hitting the bot is left alone too");
        });
    }

    [Test]
    public void TheDropLeashLetsGoPastTheLeashUnlessItIsTheirFight()
    {
        int leash = E.Leash(false);
        Assert.Multiple(() =>
        {
            Assert.That(E.MayFight(false, false, true, true, leash, leash, false, false), Is.True, "at the leash: kept");
            Assert.That(E.MayFight(false, false, true, true, leash + 1, leash, false, false), Is.False, "past it: let go");
            Assert.That(E.MayFight(false, false, true, true, 9_000, leash, playersTarget: true, hitsGroup: false), Is.True,
                "the player's own target is never let go");
            Assert.That(E.MayFight(false, false, true, true, 9_000, leash, playersTarget: false, hitsGroup: true), Is.True,
                "nor an enemy hitting the group");
            Assert.That(E.MayFight(false, hitsMe: true, true, true, 9_000, leash, false, false), Is.True, "nor one hitting the bot");
            Assert.That(E.MayFight(false, false, warZone: false, enemyRealm: true, 9_000, leash, false, false), Is.True,
                "PvE: unchanged");
            Assert.That(E.MayFight(false, false, true, enemyRealm: false, 9_000, leash, false, false), Is.True,
                "a monster in the frontier: unchanged");
        });
    }

    [Test]
    public void APickIsKeptOutToTheLeashButANewOneOnlyWithinTheBound()
    {
        int bound = E.AggressiveBound, leash = E.Leash(false);
        var field = new[] { C(RvrRole.Healer, bound + 100) };
        Assert.Multiple(() =>
        {
            Assert.That(E.Pick(field, bound, current: -1, leash: leash), Is.EqualTo(-1), "a new pick: within the bound");
            Assert.That(E.Pick(field, bound, current: 0, leash: leash), Is.EqualTo(0), "the pick from before: kept to the leash");
            Assert.That(E.Pick(new[] { C(RvrRole.Healer, leash + 1) }, bound, current: 0, leash: leash), Is.EqualTo(-1),
                "past the leash: let go");
            Assert.That(E.Pick(new[] { C(RvrRole.Melee, bound + 100), C(RvrRole.Healer, 900) }, bound, current: 0, leash: leash),
                Is.EqualTo(1), "a better role within the bound still wins");
        });
    }

    [Test]
    public void OpeningMeansNoPickAndNobodyFighting()
    {
        // Review of #765: when the pick died the next one counted as an opening, so it was sized up afresh and the "last one
        // left" rule for a held enemy could not happen. Now a group already fighting goes on.
        Assert.Multiple(() =>
        {
            Assert.That(E.Opening(hasPick: false, groupFighting: false), Is.True);
            Assert.That(E.Opening(hasPick: false, groupFighting: true), Is.False, "the pick died, the fight goes on");
            Assert.That(E.Opening(hasPick: true, groupFighting: false), Is.False);
            var alone = new[] { C(RvrRole.Healer, 300, controlled: true) };
            Assert.That(E.Pick(alone, E.AggressiveBound, opening: E.Opening(false, groupFighting: true)), Is.EqualTo(0),
                "mid-fight the last enemy left is fought even though a groupmate holds it");
            Assert.That(E.Pick(alone, E.AggressiveBound, opening: E.Opening(false, groupFighting: false)), Is.EqualTo(-1),
                "opening: never on a held enemy");
        });
    }

    [Test]
    public void ThePickSurvivesAShortSpellOutOfSight()
    {
        // Review of #765: one look without line of sight dropped the pick and the group opened again.
        const long seen = 10_000;
        Assert.Multiple(() =>
        {
            Assert.That(E.KeepsOutOfSight(seenNow: true, now: seen + 60_000, lastSeen: 0), Is.True, "seen now");
            Assert.That(E.KeepsOutOfSight(false, seen + 1_100, seen), Is.True, "one look later");
            Assert.That(E.KeepsOutOfSight(false, seen + E.SightMemoryMilliseconds - 1, seen), Is.True);
            Assert.That(E.KeepsOutOfSight(false, seen + E.SightMemoryMilliseconds, seen), Is.False, "then it is let go");
            Assert.That(E.KeepsOutOfSight(false, seen, lastSeen: 0), Is.False, "never seen");
        });
    }

    [Test]
    public void HoldAndTheLeashAreWiredIntoTheOneGate()
    {
        string root = TestContext.CurrentContext.TestDirectory;
        while (root != null && !Directory.Exists(Path.Combine(root, "source", "server", "GameServer", "bots")))
            root = Path.GetDirectoryName(root);
        Assume.That(root, Is.Not.Null, "the repository's server source");
        string bots = Path.Combine(root, "source", "server", "GameServer", "bots");
        string mode = File.ReadAllText(Path.Combine(bots, "CompanionEngagementMode.cs"));
        string brain = File.ReadAllText(Path.Combine(bots, "BotBrain.cs"));
        string pet = File.ReadAllText(Path.Combine(root, "source", "server", "GameServer", "ai", "brain", "ControlledMob", "ControlledMobBrain.cs"));
        string observe = File.ReadAllText(Path.Combine(bots, "CompanionPvpEngagement.cs"));
        Assert.Multiple(() =>
        {
            Assert.That(mode, Does.Contain("if (!CompanionRvrEngage.MayFight(actor, target)) return false;"),
                "Allows (every pick, assist, defence and pet path) asks /hold and the leash first");
            Assert.That(brain, Does.Contain("if (!CompanionEngagementMode.Restricts(Body)) return;"),
                "the bot drops what the gate refuses");
            Assert.That(pet, Does.Contain("if (CompanionEngagementMode.Restricts(Body))"), "and so does its pet");
            Assert.That(observe, Does.Contain("if (CompanionEngagementMode.PlayerHolding(leader)) return;"),
                "under /hold the group opens nothing");
        });
    }
}
