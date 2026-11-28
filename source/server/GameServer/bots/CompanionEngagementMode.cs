using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    public static class CompanionEngagementMode
    {
        public const int DefensiveRadius = 350;
        private sealed class Mode { public bool Defensive; public bool Hold; public GameLiving Pull; }
        private static readonly ConditionalWeakTable<GamePlayer, Mode> Modes = new();
        /// <summary>/aggressive or /defensive (either ends /hold).</summary>
        public static void Set(GamePlayer player, bool defensive)
        {
            Mode mode = Modes.GetOrCreateValue(player);
            mode.Defensive = defensive;
            mode.Hold = false;
            mode.Pull = null;
            CompanionPvpEngagement.Reset(player);
            CompanionRvrEngage.Reset(player);
            // #667: a stance command ends the player's fight order.
            PlayerGroupOrders.Cancel(player);
        }

        /// <summary>
        /// #664 step 2, /hold (owner answer 3, 2026-11-26 22:23: "Besides /defensive, companions only defend themselves (for
        /// sneaking past or waiting at a keep)"): every bot of the player's group fights only what is hitting it or its pet,
        /// until /aggressive or /defensive. Heals and buffs go on.
        /// </summary>
        public static void SetHold(GamePlayer player)
        {
            if (player == null) return;
            Mode mode = Modes.GetOrCreateValue(player);
            mode.Defensive = false;
            mode.Hold = true;
            mode.Pull = null;
            CompanionPvpEngagement.Reset(player);
            CompanionRvrEngage.Reset(player);
            // #667: /hold ends the player's fight order (the hold order itself sets its state after this).
            PlayerGroupOrders.Cancel(player);
        }

        /// <summary>
        /// #667: a fight order other than hold lifts /hold while it runs; the /aggressive or /defensive stance the player
        /// had before stays. <see cref="Restore"/> puts the /hold back when the order ends.
        /// </summary>
        public static void EndHold(GamePlayer player)
        {
            if (player == null || !Modes.TryGetValue(player, out Mode mode) || !mode.Hold) return;
            mode.Hold = false;
            CompanionRvrEngage.Reset(player);
        }

        /// <summary>
        /// #667: when a fight order ends on its own (the fight is over, its target is gone, the next order), the player's
        /// stance from before the order comes back: /defensive or /aggressive, and a standing /hold. Unlike
        /// <see cref="Set"/> and <see cref="SetHold"/> this ends no order.
        /// </summary>
        public static void Restore(GamePlayer player, bool defensive, bool hold)
        {
            if (player == null) return;
            if (!Modes.TryGetValue(player, out Mode mode))
            {
                if (!defensive && !hold) return;
                mode = Modes.GetOrCreateValue(player);
            }
            if (mode.Defensive == defensive && mode.Hold == hold) return;
            mode.Defensive = defensive;
            mode.Hold = hold;
            mode.Pull = null;
            CompanionRvrEngage.Reset(player);
        }

        /// <summary>The player chose /hold for his group.</summary>
        public static bool PlayerHolding(GamePlayer player) =>
            player != null && Modes.TryGetValue(player, out Mode mode) && mode.Hold;

        /// <summary>
        /// /hold or the #664 drop leash governs this bot or pet (a bot of a player's group: under /hold anywhere, or in a
        /// #663 place with bot_companion_rvr_engage on), so its brain drops what <see cref="Allows"/> refuses.
        /// </summary>
        public static bool Restricts(GameLiving actor) =>
            DefensiveLeader(actor) != null || CompanionRvrEngage.Restricting(actor) != null || PlayerGroupOrders.Restricts(actor);
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
            // #664 step 2: /hold (only self-defence) and the drop leash in the #663 places, for the player's whole group.
            if (!CompanionRvrEngage.MayFight(actor, target)) return false;
            // #667: regroup fights nothing hostile on the way back to the player.
            if (!PlayerGroupOrders.MayFight(actor, target)) return false;
            GamePlayer leader = DefensiveLeader(actor);
            return leader == null || target != null && target.CurrentRegionID == leader.CurrentRegionID &&
                (leader.IsWithinRadius(target, DefensiveRadius) || CompanionPvpEngagement.Defending(actor, target) ||
                 PlayerGroupOrders.LetsThrough(actor, target) ||
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
