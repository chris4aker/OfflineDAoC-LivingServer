using System;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// The duty behind a chat promise (ChatPromises; owner 2026-11-18 09:14: "omw" made true). A bot that said it is coming
    /// goes, through the travel it already has:
    /// <list type="bullet">
    /// <item>An RvR gamebot's frontier turn takes the promised keep as its destination: at its own realm's keep with the
    /// defence intent, so it walks to and holds a verified defensive post as a rallied defender does; elsewhere as a patrol
    /// near the keep. A committed siege or defence plan, a rally order and a relic carry still come first (they return before
    /// this in ExecuteRvr, and a bot on one never promises).</item>
    /// <item>A free gamebot walks to the player (or a battleground keep) in its region, and once there fights what is
    /// fighting that player, for <see cref="ChatPromiseRules.HoldSeconds"/>.</item>
    /// </list>
    /// Combat, the stable ride and rest come first, as on any trip; the promise's deadline is judged by ChatPromises.Observe.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        private bool _chatPromiseDestination;

        /// <summary>A point in one of the Old Frontiers' RvR zones (or a shared frontier dungeon): where a frontier promise may go.</summary>
        internal static bool IsFrontierPoint(ushort regionId, int x, int y) => IsFrontierRegionPoint(regionId, x, y);

        /// <summary>
        /// The frontier turn's promised destination: true while an RvR promise owns it (the re-plan is skipped), false otherwise.
        /// When the promise ends, the destination it set is dropped so the next turn plans as before.
        /// </summary>
        private bool ApplyChatPromiseRvr(GameBot bot)
        {
            // #530: a promise proposes to the event layer, whose rule keeps an event plan over it.
            ChatPromise promise = !AutonomousRvrEventLayer.AcceptsChatPromise(new(_rvrIntent, _rvrSharedEvent)) ? null : ChatPromises.ActiveFor(bot);
            if (promise is not { Mode: ChatPromiseMode.RvrObjective })
            {
                if (_chatPromiseDestination)
                {
                    _chatPromiseDestination = false;
                    _rvrDestination = null;
                    _rvrApproachDestination = null;
                    _patrolDestination = null;
                    _nextRvrPlanReview = 0;
                }
                return false;
            }
            ChatPromiseTarget target = promise.Target;
            string id = ChatPromiseRules.RvrDestinationId(target, bot.Realm, out bool defend);
            long now = GameLoop.GameLoopTime;
            if (_rvrDestination?.Id != id)
            {
                _rvrRuleHint = "chat.promise";
                _rvrDestination = new CampDestination(id, defend ? $"keep {target.Name}" : $"frontier near {target.Name}", target.Name,
                    target.RegionId, target.X, target.Y, target.Z, 1, false, true);
                _rvrApproachDestination = null;
                _patrolDestination = null;
                _campStartedTick = now;
            }
            ProposeRvrIntent(bot, new(AutonomousRvrEventLayer.IntentSource.ChatPromise,
                defend ? AutonomousRvrEventLayer.Intent.DefendEvent : AutonomousRvrEventLayer.Intent.Roam, id, null,
                defend ? target.Anchor : -1, "chat.promise", $"promised in chat: {target.Name}"), mirrorIntent: true, mirrorKeep: true);
            _nextRvrPlanReview = now + 45_000;
            _chatPromiseDestination = true;
            return true;
        }

        /// <summary>
        /// A walking promise's turn (help, a battleground keep): true while it owns the bot's movement. Once there, a helper
        /// fights the monster fighting the player (returning false so the brain fights) or stands by them.
        /// </summary>
        private bool ChatPromiseWalkTurn(BotBrain brain, GameBot bot, long now)
        {
            ChatPromise promise = ChatPromises.ActiveFor(bot);
            if (promise is not { Mode: ChatPromiseMode.Walk })
                return false;
            ChatPromiseTarget target = promise.Target;
            GameLiving living = target.Living;
            ushort region = living?.CurrentRegionID ?? target.RegionId;
            if (bot.CurrentRegionID != region || living != null && living.ObjectState != GameObject.eObjectState.Active)
                return false;
            Vector3 goal = living != null ? new(living.X, living.Y, living.Z) : new(target.X, target.Y, target.Z);
            float distance = Vector2.Distance(new(bot.X, bot.Y), new(goal.X, goal.Y));
            if (promise.Holding(now))
            {
                if (living == null)
                    return false; // at the battleground keep: the battleground turn fights there
                if (AttackerOf(living, bot) is { } foe && brain.CanAggroTarget(foe))
                {
                    bot.TargetObject = foe;
                    brain.AddToAggroList(foe, Math.Max(100, foe.EffectiveLevel * 12));
                    brain.FSM.SetCurrentState(eFSMStateType.AGGRO);
                    SetStatus(bot, $"Helping {living.Name}", "Keeping a promise made in chat", $"Fighting {foe.Name} beside {living.Name}", foe.Name);
                    return false;
                }
                if (distance <= 350)
                {
                    bot.StopMovingOnPath();
                    bot.StopMoving();
                    SetStatus(bot, $"Standing by {living.Name}", "Keeping a promise made in chat", "Came when asked; staying a few minutes", living.Name);
                    return true;
                }
            }
            if (distance > 150)
                IssuePath(bot, goal, retargetWhileMoving: living != null);
            SetStatus(bot, $"On the way to {target.Name}", "Keeping a promise made in chat",   // promise: none (the /lswhy status of a kept promise)
                $"Said it would come ({promise.Reason}); {(now - promise.Made) / 1000}s of {(promise.Deadline - promise.Made) / 1000}s", target.Name);
            return true;
        }

        /// <summary>A live monster (or enemy) near the player that is fighting them, the bot can reach and would not die to at once.</summary>
        private static GameLiving AttackerOf(GameLiving player, GameBot bot)
        {
            GameLiving best = null;
            foreach (GameNPC npc in player.GetNPCsInRadius(1500))
            {
                if (npc == null || !npc.IsAlive || !npc.InCombat || npc.TargetObject != player || npc.Realm == player.Realm)
                    continue;
                if (npc.EffectiveLevel > bot.Level + 5)
                    continue; // a promise is no reason to die: a far stronger monster is left to the player and their group
                if (best == null || player.GetDistanceTo(npc) < player.GetDistanceTo(best))
                    best = npc;
            }
            return best;
        }
    }
}
