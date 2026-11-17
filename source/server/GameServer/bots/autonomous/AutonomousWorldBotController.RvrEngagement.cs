using System.Collections.Generic;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// Open-field RvR tactics, work package 4 (docs/RVR-FIELD-1.2.md, "Mind": stances): what the group's stance means for the
// world controller's turn. The threat gate lets a watching, holding, regrouping or retreating group take only an enemy
// that is on it (a bot that is hit always fights back); the travel hold stops a watching group, lets a holding group's
// stragglers catch up, and walks a regrouping group to its rally point to rest there. The rules are RvrEngagement's.
public sealed partial class AutonomousWorldBotController
{
    private long _rvrStanceNextMove;
    private long _rvrStanceRestUntil;
    private Vector3 _rvrStanceRallyRaw;
    private Vector3? _rvrStanceRallySnapped;

    /// <summary>While a regroup rest owns the bot, the turn's start does not wake it (the rest is kept for this long after each turn).</summary>
    private const long RvrStanceRestKeepMilliseconds = 3_000;

    private static bool RvrStanceSwitchesOn =>
        Properties.RVR_BOT_SIZE_UP || Properties.RVR_BOT_DISENGAGE || Properties.RVR_BOT_REGROUP;

    /// <summary>A regroup rest owns the bot now: the start of the controller turn leaves its rest alone.</summary>
    private bool RvrStanceRestHolds(long now) => now < _rvrStanceRestUntil;

    /// <summary>
    /// Work package 4's part of the frontier threat gate: the noticed targets this bot may take under its group's stance.
    /// A notice is stored first (it opens an encounter), so a group that has just noticed an enemy group watches before
    /// anyone engages. A bot-only group in scope only; the same array for no opinion.
    /// </summary>
    private GameLiving[] RvrStanceGateThreats(BotBrain brain, GameBot bot, GameLiving[] visible)
    {
        if (visible == null || visible.Length == 0 || bot?.Group == null || !RvrStanceSwitchesOn || brain?.RvrTacticsActive() != true)
            return visible;
        using var aiProfile = AiProfile.Scope(AiSystem.RvrTactics);
        long now = GameLoop.GameLoopTime;
        RvrGroupState state = RvrEngagementRuntime.NoteNoticed(bot, now);
        RvrFightBoard board = RvrFightBoards.Get(bot);
        if (board == null)
            return visible;
        RvrEngagementRuntime.Observe(bot, board, now);
        RvrStance stance = RvrEngagementRuntime.CurrentStance(board, state, now);
        bool hit = RvrEngagementRuntime.Hit(bot, now);
        List<GameLiving> kept = null;
        for (int i = 0; i < visible.Length; i++)
        {
            GameLiving target = visible[i];
            bool allowed = RvrEngagement.MayTakeTarget(stance, hit, RvrEnemyUponGroup(bot, target)) &&
                           (hit || !brain.RvrStanceLeashed(target, now));
            if (!allowed && kept == null)
            {
                kept = new List<GameLiving>(visible.Length);
                for (int j = 0; j < i; j++)
                    kept.Add(visible[j]);
            }
            else if (allowed && kept != null)
                kept.Add(target);
        }
        return kept == null ? visible : kept.ToArray();
    }

    /// <summary>The enemy is on the group: it targets the bot, a member or a member's pet, or it is within 350 units.</summary>
    private static bool RvrEnemyUponGroup(GameBot bot, GameLiving enemy)
    {
        if (enemy == null)
            return false;
        if (enemy.TargetObject is GameLiving victim && (victim == bot || bot.Group != null && victim.Group == bot.Group ||
                victim is GameNPC { Brain: IControlledBrain pet } && pet.GetLivingOwner() is { } owner &&
                (owner == bot || bot.Group != null && owner.Group == bot.Group)))
            return true;
        return bot.IsWithinRadius(enemy, RvrEngagement.UponGroupRadius);
    }

    /// <summary>
    /// Work package 4's travel hold: true when the group's stance owns this turn. Observe: stand and watch. Hold: those
    /// with the group wait for the rest (the stragglers keep following). Regroup: walk to the rally point and rest there.
    /// A bot that is fighting or was just hit is never held; the coordinator's own casualty regroup comes first.
    /// </summary>
    private bool RvrStanceTravelHold(BotBrain brain, GameBot bot)
    {
        if (brain == null || bot?.Group == null || !(Properties.RVR_BOT_SIZE_UP || Properties.RVR_BOT_REGROUP) ||
            _groupDirective?.Phase == "Regrouping")
            return false;
        long now = GameLoop.GameLoopTime;
        if (bot.InCombat || bot.IsAttacking || brain.HasAggro || RvrEngagementRuntime.Hit(bot, now))
            return false;
        using var aiProfile = AiProfile.Scope(AiSystem.RvrTactics);
        RvrFightBoard board;
        if (RvrEngagementRuntime.NeedsUpkeep(bot, now))
        {
            RvrEngagementRuntime.EnsureFacts(bot);
            board = RvrFightBoards.Get(bot);
        }
        else
            board = RvrFightBoards.Peek(bot);
        if (board?.Plan == null)
            return false;
        RvrEngagementRuntime.Observe(bot, board, now);
        switch (board.Plan.Stance)
        {
            case RvrStance.Observe when Properties.RVR_BOT_SIZE_UP:
                RvrStanceStop(bot);
                if (board.EnemyCentroid is { } enemy)
                    bot.TurnTo((int)enemy.X, (int)enemy.Y);
                SetRvrStatus(bot, "Watching the enemy", "Sizing up",
                    $"Counting heads before joining: {board.Enemy.Count} enemies{(board.ThirdPartyFight ? ", two other realms fighting" : string.Empty)}");
                return true;
            case RvrStance.Hold when Properties.RVR_BOT_REGROUP:
                if (Vector2.Distance(new(bot.X, bot.Y), new(board.FriendlyCentroid.X, board.FriendlyCentroid.Y)) > RvrEngagement.HoldRadius)
                    return false;   // a straggler: the formation follow brings it in
                RvrStanceStop(bot);
                SetRvrStatus(bot, "Holding for the group", "Staying together", "Members are strung out; waiting for the rest");
                return true;
            case RvrStance.Regroup when Properties.RVR_BOT_REGROUP:
                return RvrStanceRegroupTurn(brain, bot, board, now);
            default:
                return false;
        }
    }

    private static void RvrStanceStop(GameBot bot)
    {
        AutonomousGroupFormationFollow.Release(bot);
        if (bot.IsMovingOnPath || bot.IsMoving)
        {
            bot.StopMovingOnPath();
            bot.StopMoving();
        }
    }

    private bool RvrStanceRegroupTurn(BotBrain brain, GameBot bot, RvrFightBoard board, long now)
    {
        RvrFightPlan plan = board.Plan;
        Vector3 rally = plan.RallyPoint is { } point && plan.RallyRegion == bot.CurrentRegionID ? point : board.FriendlyCentroid;
        Vector3 target = RvrStanceSnapRally(bot, rally);
        float distance = Vector2.Distance(new(bot.X, bot.Y), new(target.X, target.Y));
        if (distance > RvrEngagement.RallyArriveRadius)
        {
            bool moving = bot.IsMoving || bot.IsMovingOnPath;
            if (!moving || now >= _rvrStanceNextMove)
            {
                _rvrStanceNextMove = now + 3_000;
                if (bot.IsRecoveryResting)
                    bot.WakeRecoveryRest();
                moving = IssuePath(bot, target);
            }
            if (moving)
            {
                SetRvrStatus(bot, "Regrouping", "Meeting the group", $"Rally point {(int)distance} away");
                return true;
            }
            // no way there: rest where it stands
        }
        RvrStanceStop(bot);
        if (brain.CheckHeals())
            return true;
        bool full = AutonomousRestPolicy.IsFullyRecovered(bot.HealthPercent, bot.ManaPercent, bot.EndurancePercent, bot.MaxMana > 0);
        if (full)
            bot.WakeRecoveryRest();
        else if (!BotRestRecovery.BlocksRest(bot))
        {
            bot.BeginRecoveryRest();
            _rvrStanceRestUntil = now + RvrStanceRestKeepMilliseconds;
        }
        SetRvrStatus(bot, "Regrouping", "Resting with the group",
            $"At the rally point: HP {bot.HealthPercent}% • power {bot.ManaPercent}% • endurance {bot.EndurancePercent}%");
        AutonomousBotBodyLanguage.Idle(bot, BodySpot.Resting, default);
        return true;
    }

    /// <summary>The rally point on the navigation mesh (kept while the rally does not move); the point itself when none is found.</summary>
    private Vector3 RvrStanceSnapRally(GameBot bot, Vector3 rally)
    {
        if (_rvrStanceRallySnapped.HasValue && _rvrStanceRallyRaw == rally)
            return _rvrStanceRallySnapped.Value;
        var nav = PathfindingProvider.Instance;
        Vector3? floor = nav.IsAvailable && bot.CurrentZone != null
            ? nav.GetClosestPoint(bot.CurrentZone, rally, 128, 128, 256, nav.DefaultFilters)
            : null;
        _rvrStanceRallyRaw = rally;
        _rvrStanceRallySnapped = floor ?? rally;
        return _rvrStanceRallySnapped.Value;
    }
}
