using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    public static class CompanionEngagementMode
    {
        public const int DefensiveRadius = 350;
        private sealed class Mode { public bool Defensive; public GameLiving Pull; }
        private static readonly ConditionalWeakTable<GamePlayer, Mode> Modes = new();
        public static void Set(GamePlayer player, bool defensive)
        {
            Mode mode = Modes.GetOrCreateValue(player);
            mode.Defensive = defensive;
            mode.Pull = null;
            CompanionPvpEngagement.Reset(player);
            CompanionRvrEngage.Reset(player);
        }
        public static GamePlayer DefensiveLeader(GameLiving actor)
        {
            // Include companion pets, never human pets or autonomous bots.
            for (int depth = 0; depth < 8 && actor != null; depth++)
            {
                if (actor is GameBot bot)
                {
                    GamePlayer leader = bot.PlayerGroupLeader ?? bot.Owner;
                    // a /spawn helper, or a hired companion (True 1.65): its own Stance choice first (1 aggressive,
                    // 2 defensive), else its player's /aggressive or /defensive
                    bool companion = bot.IsTemporaryGroupHelper && !bot.IsAutonomousWorldBot || bot.IsHiredCompanion;
                    if (!companion || leader == null || bot.Group == null || bot.Group != leader.Group)
                        return null;
                    bool defensive = (bot.IsHiredCompanion ? CompanionOptions165.Choice(bot, "stance") : 0) switch
                    {
                        CompanionControls165.StanceAggressive => false,
                        CompanionControls165.StanceDefensive => true,
                        _ => Modes.TryGetValue(leader, out Mode mode) && mode.Defensive,
                    };
                    return defensive ? leader : null;
                }
                actor = (actor as GameNPC)?.Brain is IControlledBrain controlled ? controlled.Owner : null;
            }
            return null;
        }
        public static bool Allows(GameLiving actor, GameLiving target)
        {
            GamePlayer leader = DefensiveLeader(actor);
            return leader == null || target != null && target.CurrentRegionID == leader.CurrentRegionID &&
                (leader.IsWithinRadius(target, DefensiveRadius) || CompanionPvpEngagement.Defending(actor, target) ||
                 CompanionPvpEngagement.FrontierRoamerInRange(leader, target, true) ||
                 CompanionRvrEngage.Engaged(leader, target));
        }
        /// <summary>The player chose /defensive (his party's standing stance; a hired companion's own Stance row aside).</summary>
        public static bool PlayerDefensive(GamePlayer player) =>
            player != null && Modes.TryGetValue(player, out Mode mode) && mode.Defensive;
        public static void RememberPull(GamePlayer player, GameLiving target)
        {
            if (player != null && Modes.TryGetValue(player, out Mode mode) && mode.Defensive) mode.Pull = target;
        }
        public static GameLiving NearbyPull(GameBot bot)
        {
            GamePlayer player = DefensiveLeader(bot);
            if (player == null || !Modes.TryGetValue(player, out Mode mode)) return null;
            GameLiving target = mode.Pull;
            if (target?.IsAlive != true || target.ObjectState != GameObject.eObjectState.Active ||
                target.CurrentRegionID != player.CurrentRegionID)
            {
                mode.Pull = null;
                return null;
            }
            return Allows(bot, target) ? target : null;
        }
    }
}
