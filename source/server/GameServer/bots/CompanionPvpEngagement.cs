using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>Human-led temporary companions only; no PvE or autonomous AI policy.</summary>
    public static class CompanionPvpEngagement
    {
        private sealed class State
        {
            public GameLiving Focus;
            public long FocusUntil, NextScan;
            public readonly Dictionary<GameLiving, long> Threats = new();
        }
        private static readonly ConditionalWeakTable<GamePlayer, State> States = new();

        // Validate each link, so released/replaced pets cannot command or enlist a raid.
        public static GameLiving Character(GameLiving actor)
        {
            for (int depth = 0; depth < 16 && actor != null; depth++)
            {
                if (actor is GamePlayer or GameBot) return actor;
                if (actor is not GameNPC { Brain: IControlledBrain brain } pet || brain.Owner is not GameLiving owner ||
                    owner.ControlledBrain != brain && (owner is not GameNPC npc || npc.ControlledNpcList?.Contains(brain) != true))
                    return null;
                if (brain.Body != pet) return null;
                actor = owner;
            }
            return null;
        }

        public static GamePlayer Leader(GameLiving actor)
        {
            if (Character(actor) is not GameBot { IsPlayerCompanion: true } bot) return null;
            GamePlayer leader = bot.PlayerGroupLeader ?? bot.Owner;
            return leader != null && bot.Group != null && bot.Group == leader.Group &&
                bot.Group.IsInTheGroup(bot) && bot.Group.IsInTheGroup(leader) ? leader : null;
        }

        public static bool Enemy(GamePlayer player, GameLiving target) => player != null &&
            Character(target) is GameBot bot && bot.Realm != eRealm.None && bot.Realm != player.Realm;

        private static bool Live(GamePlayer player, GameLiving target) => Enemy(player, target) && target.IsAlive &&
            target.ObjectState == GameObject.eObjectState.Active && target.CurrentRegionID == player.CurrentRegionID &&
            player.IsWithinRadius(target, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS);

        public static void Reset(GamePlayer player) => States.Remove(player);

        public static void Order(GamePlayer player, GameLiving target)
        {
            if (player != null && !Enemy(player, target))
            {
                if (States.TryGetValue(player, out State previous))
                    lock (previous) { previous.Focus = null; previous.FocusUntil = 0; }
                return;
            }
            if (!Live(player, target)) return;
            State state = States.GetOrCreateValue(player);
            lock (state) { state.Focus = target; state.FocusUntil = GameLoop.GameLoopTime + 30_000; }
        }

        public static bool Focused(GameLiving actor, GameLiving target)
        {
            GamePlayer leader = Leader(actor);
            if (!Live(leader, target)) return false;
            if (leader.IsAttacking && leader.TargetObject == target) return true;
            if (leader.ControlledBrain is ControlledMobBrain pet && pet.Owner == leader && pet.Body?.IsAlive == true &&
                pet.Body.CurrentRegionID == leader.CurrentRegionID &&
                Character(pet.Body) == leader && leader.IsWithinRadius(pet.Body, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) &&
                (pet.OrderedAttackTarget == target || pet.Body.IsAttacking && pet.Body.TargetObject == target)) return true;
            if (!States.TryGetValue(leader, out State state)) return false;
            lock (state) return state.Focus == target && GameLoop.GameLoopTime < state.FocusUntil;
        }

        public static bool Defending(GameLiving actor, GameLiving target)
        {
            GamePlayer leader = Leader(actor);
            if (!Live(leader, target) || !States.TryGetValue(leader, out State state)) return false;
            lock (state) return state.Threats.TryGetValue(target, out long until) && GameLoop.GameLoopTime < until;
        }

        /// <summary>
        /// <paramref name="target"/> hit someone of <paramref name="leader"/>'s group lately (RecordThreat's memory), whoever
        /// asks (#664 step 2: the drop leash never lets go of an enemy that is hitting the group).
        /// </summary>
        public static bool Threatens(GamePlayer leader, GameLiving target)
        {
            if (leader == null || target == null || !States.TryGetValue(leader, out State state)) return false;
            lock (state) return state.Threats.TryGetValue(target, out long until) && GameLoop.GameLoopTime < until;
        }

        public static bool RecordThreat(GameBot helper, GameLiving victim, AttackData attack)
        {
            GamePlayer leader = Leader(helper);
            GameLiving member = Character(victim);
            GameLiving enemy = attack?.Attacker;
            if (leader == null || member?.Group != helper.Group || !helper.Group.IsInTheGroup(member) ||
                victim.CurrentRegionID != helper.CurrentRegionID || !helper.IsWithinRadius(victim, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) ||
                attack?.CausesCombat != true || !attack.IsHit || !Live(leader, enemy) ||
                !GameServer.ServerRules.IsAllowedToAttack(leader, enemy, true)) return false;
            State state = States.GetOrCreateValue(leader);
            lock (state)
            {
                long now = GameLoop.GameLoopTime;
                foreach (GameLiving old in state.Threats.Where(p => p.Value <= now || !p.Key.IsAlive).Select(p => p.Key).ToArray())
                    state.Threats.Remove(old);
                if (state.Threats.Count >= 64 && !state.Threats.ContainsKey(enemy))
                    state.Threats.Remove(state.Threats.MinBy(p => p.Value).Key);
                // A whole raid may be mezzed. Keep the attacker remembered until
                // its hostile duration can finish; do not cleanse or bypass CC.
                long hostileDuration = attack.SpellHandler?.Spell?.Duration ?? 0;
                state.Threats.TryGetValue(enemy, out long previous);
                state.Threats[enemy] = Math.Max(previous, now + 12_000 + Math.Clamp(hostileDuration * 2, 0, 120_000));
            }
            return true;
        }

        // Once per whole party/raid, not 80 independent proximity scans.
        public static void Observe(GameBot helper)
        {
            // #664: in the #663 places the player's whole group (befriended gamebots and /bot companions too) takes the
            // group's own pick (CompanionRvrEngage); the rest of this stays the companions' (Leader).
            GamePlayer leader = Leader(helper);
            if (leader == null && CompanionRvrEngage.GroupLeader(helper) is GamePlayer groupLeader && CompanionRvrEngage.Applies(groupLeader))
                leader = groupLeader;
            if (leader?.IsAlive != true || !PlayerLedPullCoordinator.Available(helper, leader)) return;
            // #664 step 2: under /hold the group opens nothing and assists nobody; each bot only defends itself (in its brain,
            // through CompanionEngagementMode.Allows).
            if (CompanionEngagementMode.PlayerHolding(leader)) return;
            // #667: under assist the player alone calls the target, and under regroup the group is breaking off: no focus
            // of the group's own (no shared threat, no #664 pick, no /defensive nearby pull, no /raid radar).
            GroupOrder order = PlayerGroupOrders.Current(leader);
            if (order == GroupOrder.Regroup) return;
            bool ownPick = !PlayerGroupOrders.SuppressesOwnPick(order);
            State state = States.GetOrCreateValue(leader);
            GameLiving focus;
            lock (state)
            {
                long now = GameLoop.GameLoopTime;
                if (now < state.NextScan) return;
                state.NextScan = now + 500;
                focus = now < state.FocusUntil ? state.Focus : null;
                if (!Live(leader, focus)) state.Focus = focus = null;
                if (focus == null && ownPick)
                    focus = state.Threats.Where(p => p.Value > now && Live(leader, p.Key))
                        .OrderBy(p => leader.GetDistanceTo(p.Key)).Select(p => p.Key).FirstOrDefault();
            }
            // #667: an attack order's target is the group's focus as the player's own attack is.
            GameLiving ordered = PlayerLedPullCoordinator.FindLeaderTarget(leader) ?? PlayerGroupOrders.AttackTarget(leader);
            if (ordered != null) { Order(leader, ordered); focus = Enemy(leader, ordered) ? ordered : null; }
            bool defensive = CompanionEngagementMode.DefensiveLeader(helper) != null;
            if (focus != null && !CompanionEngagementMode.Allows(helper, focus)) focus = null;
            bool groupPick = false;
            if (ownPick && CompanionRvrEngage.Applies(leader))
            {
                // #664 (bot_companion_rvr_engage): the group's own pick, by perception, bounded by the player, sized up.
                // It replaces the /defensive 350 scan and the /raid radar circle in the #663 places.
                if (focus == null)
                {
                    focus = CompanionRvrEngage.Choose(leader, CompanionEngagementMode.PlayerDefensive(leader));
                    groupPick = focus != null;
                }
            }
            else if (ownPick)
            {
                if (focus == null && defensive)
                    focus = SelectNearby(helper, leader.GetNPCsInRadius(CompanionEngagementMode.DefensiveRadius), BotSiegeRuntime.Visible);
                if (focus == null && FrontierRaidAggro(leader))
                    focus = SelectRoamer(helper, leader.GetNPCsInRadius((ushort)FrontierRaidAggroRadius(defensive)), BotSiegeRuntime.Visible);
            }
            if (focus == null) return;
            // #667: under attack or assist every bot that obeys the player takes the focus, gamebots of his group too.
            bool byOrder = focus == ordered && (order is GroupOrder.Attack or GroupOrder.Assist);
            foreach (GameLiving member in leader.Group.GetMembersInTheGroup())
                if (member is GameBot bot &&
                    (groupPick ? CompanionRvrEngage.Joins(bot, leader) && CompanionEngagementMode.Allows(bot, focus) :
                     byOrder ? PlayerGroupOrders.Obeys(bot, leader) && CompanionEngagementMode.Allows(bot, focus) : Leader(bot) == leader) &&
                    PlayerLedPullCoordinator.Available(bot, leader) &&
                    bot.IsWithinRadius(focus, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) && CompanionRaidSiege.MayAssist(bot, focus) &&
                    bot.Brain is BotBrain brain)
                    brain.AssistPlayerAttack(focus);
        }

        // Owner 2026-10-07: in the frontier a /raid 40 or /raid 80 engages enemy roamers near the player on its own,
        // without waiting for the player to attack or be attacked, but never far from the player. Its range is separate
        // from the PvE engagement rules: /aggressive gives the long range, /defensive a smaller one that is still far
        // beyond the PvE DefensiveRadius. The leash stays: a raid bot chasing beyond
        // TemporaryCompanionRecovery.MaximumLeaderDistance is recalled once the player's own area is quiet (see
        // TemporaryCompanionRecovery.FrontierChaseLeash). Only enemy-realm gamebots are affected; PvE engagement is unchanged.
        // (Raids are level 50 only, so battlegrounds never apply.)
        public const int FrontierRaidAggressiveRadius = 2000;
        public const int FrontierRaidDefensiveRadius = 1000;

        public static int FrontierRaidAggroRadius(bool defensive) =>
            defensive ? FrontierRaidDefensiveRadius : FrontierRaidAggressiveRadius;

        /// <summary>An enemy roamer the raid may fight in the frontier even under /defensive's short PvE range.</summary>
        public static bool FrontierRoamerInRange(GamePlayer leader, GameLiving target, bool defensive) =>
            FrontierRaidAggro(leader) && Enemy(leader, target) && target.CurrentRegionID == leader.CurrentRegionID &&
            leader.IsWithinRadius(target, FrontierRaidAggroRadius(defensive));

        public static bool FrontierRaidAggro(GamePlayer leader) =>
            leader?.Group?.IsCompanionRaid == true && leader.CurrentZone?.IsRvR == true;

        public static GameLiving SelectRoamer(GameBot helper, IEnumerable<GameNPC> candidates, Func<GameLiving, GameLiving, bool> visible)
        {
            GamePlayer leader = Leader(helper);
            if (!FrontierRaidAggro(leader)) return null;
            int radius = FrontierRaidAggroRadius(CompanionEngagementMode.DefensiveLeader(helper) != null);
            return candidates.Where(t => Live(leader, t) && !t.IsStealthed &&
                    leader.IsWithinRadius(t, radius) &&
                    GameServer.ServerRules.IsAllowedToAttack(helper, t, true) && visible(leader, t))
                .OrderBy(leader.GetDistanceTo).FirstOrDefault();
        }

        public static GameLiving SelectNearby(GameBot helper, IEnumerable<GameNPC> candidates, Func<GameLiving, GameLiving, bool> visible)
        {
            GamePlayer leader = Leader(helper);
            if (leader == null || CompanionEngagementMode.DefensiveLeader(helper) == null) return null;
            return candidates.Where(t => Live(leader, t) && !t.IsStealthed &&
                    leader.IsWithinRadius(t, CompanionEngagementMode.DefensiveRadius) &&
                    GameServer.ServerRules.IsAllowedToAttack(helper, t, true) && visible(leader, t))
                .OrderBy(leader.GetDistanceTo).FirstOrDefault();
        }
    }
}
