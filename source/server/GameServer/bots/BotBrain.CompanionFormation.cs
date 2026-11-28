// #665: the player's group spreads into the period backline in an RvR fight (CompanionRvrFormation). This partial reads the
// group's fight, picks the bot's place and walks it with the gamebots' proven-leg walk (BotBrain.RvrPositioning). Tests only
// until seen in a PC log.
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        /// <summary>COMPANION_RVR_FORMATION at most this often per player's group.</summary>
        public const long CompanionFormationLogGapMilliseconds = 10_000;

        private long _companionFormationNextCheck;
        private long _companionFormationDueAt;

        private sealed class CompanionFormationLog
        {
            public long LastAt = long.MinValue / 2;
            public int Moved;
        }

        private static readonly ConditionalWeakTable<GamePlayer, CompanionFormationLog> CompanionFormationLogs = new();

        /// <summary>The player whose group's formation this bot takes part in now, or null.</summary>
        private GamePlayer CompanionFormationLeader()
        {
            GameBot bot = BotBody;
            if (bot == null || !CompanionRvrFormation.On) return null;
            GamePlayer leader = CompanionRvrEngage.GroupLeader(bot);
            return leader != null && CompanionRvrFormation.Applies(bot, leader) && !RvrTacticsActive() ? leader : null;
        }

        /// <summary>Hook: AttackMostWanted, after the gamebots' tactics. True when the formation used the turn to walk.</summary>
        public bool TryCompanionFormationMove(GameLiving target) => CompanionFormationTurn(target, support: false);

        /// <summary>Hook: PerformGroupSupport, after the gamebots' tactics. True when the formation used the turn to walk.</summary>
        public bool TryCompanionFormationSupport() => CompanionFormationTurn(Body.TargetObject as GameLiving, support: true);

        /// <summary>
        /// A back member of the player's group holds its place in a fight instead of walking back to its travel slot
        /// (FollowFormation): the group is fighting, and the bot is within the bound of the player.
        /// </summary>
        public bool CompanionFormationHolds()
        {
            GamePlayer leader = CompanionFormationLeader();
            if (leader == null || leader.CurrentRegionID != Body.CurrentRegionID) return false;
            CompanionRvrFormation.Fight fight = CompanionRvrFormation.Read(leader, GameLoop.GameLoopTime);
            return fight != null && CompanionRvrFormation.Places(fight.SlotOf(Body).Stand) &&
                Body.IsWithinRadius(leader, CompanionRvrFormation.PlayerBound + (int)RvrPositioning.SlotTolerance);
        }

        private bool CompanionFormationTurn(GameLiving target, bool support)
        {
            GameBot bot = BotBody;
            GamePlayer leader = CompanionFormationLeader();
            if (leader == null || !bot.IsAlive || bot.CurrentRegion == null || leader.CurrentRegionID != bot.CurrentRegionID)
                return false;
            long now = GameLoop.GameLoopTime;
            if (ContinueRvrPlaceWalk(bot, now))
                return true;
            if (bot.IsCasting || bot.IsIncapacitated || bot.IsCrowdControlled || bot.MaxSpeed <= 0 || bot.IsStealthed ||
                bot.IsUnderwater || bot.IsTurningDisabled)
                return false;
            if (now < _companionFormationNextCheck)
                return false;
            _companionFormationNextCheck = now + RvrPositioning.CheckMilliseconds + Math.Abs(bot.DatabaseID % 250);

            CompanionRvrFormation.Fight fight = CompanionRvrFormation.Read(leader, now);
            if (fight == null)
            {
                _companionFormationDueAt = 0;
                return false;
            }
            RvrSlot mine = fight.SlotOf(Body);
            if (!CompanionRvrFormation.Places(mine.Stand))
                return false;
            // Heals first: a healer walks for its place only while nobody of the group needs a heal.
            if (IsHealer || mine.Healer)
                foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
                    if (member != null && member.IsAlive && member.HealthPercent < RvrPositioning.HealFirstPercent)
                        return false;

            Vector2 self = Flat(bot);
            var others = new List<(Vector2, bool)>(fight.Backs.Count);
            foreach ((GameLiving member, Vector2 position, bool healer) in fight.Backs)
                if (!ReferenceEquals(member, Body))
                    others.Add((position, healer));
            RvrMoveReason reason = RvrPositioning.NeedsMove(self, mine, fight.Line, fight.Front, fight.Incoming, others);
            // The bound (owner answer 2): a member standing past it walks back in, whatever else.
            if (reason == RvrMoveReason.None &&
                Vector2.Distance(self, fight.Player) > CompanionRvrFormation.PlayerBound + RvrPositioning.SlotTolerance)
                reason = RvrMoveReason.Far;
            if (reason == RvrMoveReason.None)
            {
                _companionFormationDueAt = 0;
                return false;
            }
            if (_companionFormationDueAt == 0)
                _companionFormationDueAt = now + RvrPositioning.ReactionMilliseconds(CompanionRvrFormation.Tier, bot.DatabaseID, now);
            if (now < _companionFormationDueAt)
                return false;
            Vector2 spot = mine.Spot;
            if (!support && target != null && target.CurrentRegionID == bot.CurrentRegionID && PrefersCurrentSpellRange())
                spot = RvrPositioning.WithinRange(spot, Flat(target), Math.Max(300, OffensiveApproachRange(target) - 75));
            spot = CompanionRvrFormation.Clamp(spot, fight.Player, CompanionRvrFormation.PlayerBound);
            bool setOff = CompanionRvrFormation.ProveWalk is { } prove
                ? prove(bot, spot) is Vector3 proven && BeginRvrPlaceWalk(bot, proven, RvrPlaceWalk.Place, now)
                : StartRvrPlaceWalk(bot, spot, RvrPlaceWalk.Place, now);
            if (!setOff)
                return false;
            _companionFormationDueAt = 0;
            float depth = fight.Line is Vector2 anchor ? -RvrPositioning.Ahead(spot, anchor, fight.Front) : 0;
            LogCompanionFormation(leader, reason, depth, (int)Vector2.Distance(spot, fight.Player), fight, now);
            return true;
        }

        /// <summary>COMPANION_RVR_FORMATION: at most every 10 s per player's group, with how many walked since the last line.</summary>
        private void LogCompanionFormation(GamePlayer leader, RvrMoveReason reason, float depth, int fromPlayer,
            CompanionRvrFormation.Fight fight, long now)
        {
            if (!log.IsInfoEnabled)
                return;
            CompanionFormationLog entry = CompanionFormationLogs.GetValue(leader, static _ => new CompanionFormationLog());
            int moved;
            lock (entry)
            {
                entry.Moved++;
                if (!RvrPositioning.ShouldLog(now, entry.LastAt, CompanionFormationLogGapMilliseconds))
                    return;
                moved = entry.Moved;
                entry.Moved = 0;
                entry.LastAt = now;
            }
            // The player's group spread into its attack formation in an RvR fight: a caster or healer walked for its place (#665).
            log.Info($"COMPANION_RVR_FORMATION leader=\"{leader.Name}\" region={Body.CurrentRegionID} moved={moved} " +
                     $"member=\"{Body.Name}\" class={(eCharacterClass)(BotBody?.CharacterClass?.ID ?? 0)} reason={reason} " +
                     $"depth={(int)depth} fromPlayer={fromPlayer} enemies={fight.Enemies} incoming={fight.Incoming}");
        }
    }
}
