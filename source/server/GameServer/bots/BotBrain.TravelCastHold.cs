// Hand port from zomboden/OfflineDAoC-True165 03dce76ee (bots/BotBrain.TravelCastHold.cs, HoldsUpkeepForGroupTravel; GPL-3.0),
// reviewed to e2af3f2 (#567 A1). Only the travel hold; the heal-over-time checks of that file are #567 A3.
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private long _upkeepPartySeenWalking;

        /// <summary>
        /// #567 A1: whether an autonomous party's ordinary buff upkeep waits for the party to stand still
        /// (<see cref="BotTravelCastHold"/>). One pass over at most eight members, only when the upkeep runs.
        /// </summary>
        internal bool HoldsUpkeepForGroupTravel()
        {
            GameBot bot = BotBody;
            Group group = bot?.Group;
            if (bot is not { IsAutonomousWorldBot: true, IsPlayerLedGroup: false } || group == null)
                return false;
            bool fighting = bot.InCombat || bot.IsAttacking || HasAggro;
            if (!fighting)
                foreach (GameLiving member in group.GetMembersInTheGroup())
                    if (member.IsAlive && (member.InCombat || member.IsAttacking))
                    {
                        fighting = true;
                        break;
                    }
            GameLiving leader = group.LivingLeader ?? bot;
            bool walking = bot.IsMoving || leader is { IsAlive: true, IsMoving: true };
            long now = GameLoop.GameLoopTime;
            if (walking)
                _upkeepPartySeenWalking = now;
            bool restingStop = bot.IsSitting || bot.IsRecoveryResting || leader.IsSitting || leader is GameBot { IsRecoveryResting: true };
            return BotTravelCastHold.Holds(true, fighting, walking, restingStop,
                _upkeepPartySeenWalking <= 0 ? -1 : now - _upkeepPartySeenWalking);
        }
    }
}
