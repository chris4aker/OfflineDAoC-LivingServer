using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// #667 step 1, the world side of <see cref="PlayerGroupOrders"/>: one order per player, given by /order or a /group
    /// word (<see cref="Give"/>), ended by its rule (<see cref="Current"/> looks at most every
    /// <see cref="CheckIntervalMilliseconds"/>), and read by the gates the group already has:
    /// CompanionEngagementMode.Allows / Restricts (regroup's break-off, attack's and assist's way through /defensive and
    /// the leash), CompanionPvpEngagement.Observe (no own pick under assist or regroup; attack's target as the group's
    /// focus) and BotBrain.LeaderFocusTarget (the ordered target before a bot's own attacker). Log: GROUP_ORDER.
    /// </summary>
    public static partial class PlayerGroupOrders
    {
        private sealed class State
        {
            public GroupOrder Order;
            public GameLiving Target;
            public string Via;
            public long GivenAt, LastFight, NextCheck, LastAnswer;
            public bool SawFight, Warned;
            /// <summary>The player's stance before the order (/defensive, a standing /hold), put back when it ends.</summary>
            public bool WasDefensive, WasHolding;
        }

        private static readonly ConditionalWeakTable<GamePlayer, State> States = new();
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>The bots of <paramref name="player"/>'s group that obey his orders now (<see cref="Obeys(GameBot, GamePlayer)"/>).</summary>
        public static List<GameBot> Obeying(GamePlayer player)
        {
            var bots = new List<GameBot>();
            if (player?.Group == null) return bots;
            foreach (GameBot bot in player.Group.GetMembersInTheGroup().OfType<GameBot>())
                if (bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && Obeys(bot, player))
                    bots.Add(bot);
            return bots;
        }

        /// <summary>This bot obeys <paramref name="player"/>'s orders: in his group, commanded by him, no per-bot order in the way.</summary>
        public static bool Obeys(GameBot bot, GamePlayer player)
        {
            if (bot == null || player == null) return false;
            // As CompanionRvrEngage.Joins: /bot stay puts a /bot companion in IDLE; other bots pass through IDLE in their own work.
            eFSMStateType? state = (bot.Brain as BotBrain)?.FSM?.GetCurrentState()?.StateType;
            bool botCompanion = !bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper;
            return Obeys(CompanionRvrEngage.GroupLeader(bot) == player, BotHumanBinding.Commands(player, bot, bot.Group),
                HeldBack(bot.Stance, state, botCompanion), false, false);
        }

        /// <summary>
        /// The player gives an order (<paramref name="via"/> "command" or "chat"). Returns the line for the player's own
        /// window, or null when the order was given and the group's answer says enough. <paramref name="given"/> is false
        /// when nothing changed (switch off, no group, no target).
        /// </summary>
        public static string Give(GamePlayer player, GroupOrder order, string via, out bool given)
        {
            given = false;
            if (player == null || order == GroupOrder.None) return null;
            if (!On) return "Group orders are turned off on this server (bot_group_orders).";
            Group group = player.Group;
            if (group == null) return "You have no group to give orders to.";
            if (BotLedGroups.HostedPlayerOf(group) == player)
                return BotLedGroups.BotLeadsMessage(group.LivingLeader?.Name);
            List<GameBot> obeying = Obeying(player);
            if (obeying.Count == 0) return "No bot in your group takes your orders (a bot on /bot hold, /bot stay or passive keeps to that).";
            GameLiving target = null;
            if (order == GroupOrder.Attack)
            {
                target = player.TargetObject as GameLiving;
                if (!PlayerLedPullCoordinator.ValidTarget(player, target))
                    return "Select an enemy near you to attack first.";
            }

            // The order before this one ends first and puts back the stance from before it, so that is the stance saved.
            End(player, "next");
            bool wasDefensive = CompanionEngagementMode.PlayerDefensive(player);
            bool wasHolding = CompanionEngagementMode.PlayerHolding(player);
            long now = GameLoop.GameLoopTime;
            State state = States.GetOrCreateValue(player);
            bool warn = false;
            if (order == GroupOrder.Hold)
            {
                // The /hold stance itself (#664 step 2), so /hold and the order are one rule.
                CompanionEngagementMode.SetHold(player);
                foreach (GameBot bot in obeying)
                    (bot.Brain as BotBrain)?.EnforceCompanionEngagementRange();
            }
            else
                CompanionEngagementMode.EndHold(player);
            lock (state)
            {
                state.Order = order;
                state.Target = target;
                state.Via = via;
                state.GivenAt = now;
                state.LastFight = 0;
                state.SawFight = false;
                state.NextCheck = now + CheckIntervalMilliseconds;
                state.WasDefensive = wasDefensive;
                state.WasHolding = wasHolding;
            }
            switch (order)
            {
                case GroupOrder.Attack:
                    PlayerLedPullCoordinator.OrderAttack(player, target, obeying);
                    warn = Warns(order, AutonomousWorldBotController.IsBattleground(player),
                        CompanionRvrEngage.SizeUpAgainst(player, target).Call);
                    break;
                case GroupOrder.Assist:
                    // The player alone calls the target now: the group's own pick is forgotten.
                    CompanionRvrEngage.Reset(player);
                    break;
                case GroupOrder.Regroup:
                    CompanionRvrEngage.Reset(player);
                    PlayerLedPullCoordinator.CancelPull(player);
                    foreach (GameBot bot in obeying)
                        if (bot.Brain is BotBrain brain)
                        {
                            brain.EnforceCompanionEngagementRange();
                            bot.ControlledBrain?.Disengage();
                            bot.ControlledBrain?.Follow(bot);
                        }
                    break;
            }
            given = true;
            lock (state) state.Warned = warn;
            Note(player, "given", order, target, via, obeying.Count, warn, null);
            Answer(player, state, obeying, order, target, warn);
            return via == "command" ? $"Your group: {Word(order).ToUpperInvariant()}. {Does(order)}" : null;
        }

        private static string Does(GroupOrder order) => order switch
        {
            GroupOrder.Attack => "They go for your target now, until it falls, you give another order or the fight ends.",
            GroupOrder.Assist => "They fight only your fight and switch when you switch, until your next order or the fight ends.",
            GroupOrder.Hold => "They only defend themselves and healers keep healing, until your next order or the fight ends; then your stance from before comes back (/hold holds until /aggressive or /defensive).",
            GroupOrder.Regroup => "They break off and come back to you, then defend you and the group; one that can't get back (rooted, or below half health) fights back.",
            _ => string.Empty,
        };

        /// <summary>
        /// A /group line from the player: when the whole line is an order word, the order is given (and the group's chatter
        /// does not also answer it). False when it is ordinary talk, or the order could not be given.
        /// </summary>
        public static bool FromGroupChat(GamePlayer player, string message)
        {
            if (!On || player?.Group == null) return false;
            GroupOrder order = Parse(message);
            if (order == GroupOrder.None) return false;
            Give(player, order, "chat", out bool given);
            return given;
        }

        /// <summary>The order running now was given with the warning (an attack into a fight #664 would back off from).</summary>
        public static bool Warned(GamePlayer player)
        {
            if (Current(player) == GroupOrder.None || !States.TryGetValue(player, out State state)) return false;
            lock (state) return state.Warned;
        }

        /// <summary>The player's order now (its end rule looked at first), or None.</summary>
        public static GroupOrder Current(GamePlayer player) => Check(player, out _);

        /// <summary>The attack order's target, while it runs.</summary>
        public static GameLiving AttackTarget(GamePlayer player) =>
            Check(player, out GameLiving target) == GroupOrder.Attack ? target : null;

        /// <summary>Under assist or regroup the group picks nothing of its own.</summary>
        public static bool SuppressesOwnPick(GamePlayer player) => SuppressesOwnPick(Current(player));

        /// <summary>The player cancelled orders by a stance command (/aggressive, /defensive, /hold).</summary>
        public static void Cancel(GamePlayer player, string why = "stance") => End(player, why);

        private static GameBot OwnerBot(GameLiving actor)
        {
            for (int depth = 0; depth < 8 && actor != null; depth++)
            {
                if (actor is GameBot bot) return bot;
                actor = (actor as GameNPC)?.Brain is IControlledBrain controlled ? controlled.Owner : null;
            }
            return null;
        }

        /// <summary>The player whose order governs this bot (or its pet), with the order; null when none does.</summary>
        private static GamePlayer Commander(GameLiving actor, out GroupOrder order, out GameLiving target)
        {
            order = GroupOrder.None;
            target = null;
            GameBot bot = OwnerBot(actor);
            GamePlayer leader = bot == null ? null : CompanionRvrEngage.GroupLeader(bot);
            if (leader == null || !States.TryGetValue(leader, out _)) return null;
            order = Check(leader, out target);
            return order != GroupOrder.None && Obeys(bot, leader) ? leader : null;
        }

        /// <summary>
        /// Regroup's gate (<see cref="MayFight(GroupOrder, bool, bool, bool, bool, int)"/>): a bot of the group opens nothing
        /// on the way back and fights back only when it can't get away; once back it defends the player and the group.
        /// </summary>
        public static bool MayFight(GameLiving actor, GameLiving target)
        {
            if (target == null) return true;
            GamePlayer leader = Commander(actor, out GroupOrder order, out _);
            if (leader == null || order != GroupOrder.Regroup) return true;
            GameBot bot = OwnerBot(actor);
            if (target.Realm != eRealm.None && target.Realm == bot.Realm) return true; // a heal target, a guard, a merchant
            bool back = bot.CurrentRegionID == leader.CurrentRegionID && bot.IsWithinRadius(leader, RegroupRadius);
            bool hitsMe = CompanionRvrEngage.HitsUs(bot, actor, target);
            bool hitsGroup = back && !hitsMe && CompanionRvrEngage.HittingGroup(leader, target);
            return MayFight(order, back, hitsMe, hitsGroup, actor.MaxSpeed <= 0, actor.HealthPercent);
        }

        /// <summary>Regroup governs this bot or pet, so its brain drops what <see cref="MayFight(GameLiving, GameLiving)"/> refuses.</summary>
        public static bool Restricts(GameLiving actor) =>
            Commander(actor, out GroupOrder order, out _) != null && order == GroupOrder.Regroup;

        /// <summary>The order lets <paramref name="target"/> through /defensive's range and the RvR leash for this bot.</summary>
        public static bool LetsThrough(GameLiving actor, GameLiving target)
        {
            if (target == null) return false;
            GamePlayer leader = Commander(actor, out GroupOrder order, out GameLiving ordered);
            if (leader == null) return false;
            return LetsThrough(order, ordered == target,
                order == GroupOrder.Assist && PlayerLedPullCoordinator.FindLeaderTarget(leader) == target);
        }

        /// <summary>The order lets <paramref name="target"/> through the leash for the player's whole group.</summary>
        public static bool Ordered(GamePlayer leader, GameLiving target)
        {
            if (leader == null || target == null || !States.TryGetValue(leader, out _)) return false;
            GroupOrder order = Check(leader, out GameLiving ordered);
            return LetsThrough(order, ordered == target,
                order == GroupOrder.Assist && PlayerLedPullCoordinator.FindLeaderTarget(leader) == target);
        }

        /// <summary>
        /// The target an attack or assist order gives this bot (attack: the order's target; assist: the player's fight), or
        /// null. <paramref name="applies"/>: the order sets this bot's target at all (a fighter under attack or assist), even
        /// when the player has no fight right now.
        /// </summary>
        public static GameLiving FocusFor(GameBot bot, out bool applies)
        {
            applies = false;
            GamePlayer leader = Commander(bot, out GroupOrder order, out GameLiving ordered);
            if (leader == null || !Focuses(order, BotPartyRoles.IsSupport(bot))) return null;
            applies = true;
            return order == GroupOrder.Attack ? ordered : PlayerLedPullCoordinator.FindLeaderTarget(leader);
        }

        private static GroupOrder Check(GamePlayer player, out GameLiving target)
        {
            target = null;
            if (player == null || !States.TryGetValue(player, out State state)) return GroupOrder.None;
            string why = null;
            GroupOrder order;
            lock (state)
            {
                order = state.Order;
                target = state.Target;
                if (order == GroupOrder.None) return order;
                long now = GameLoop.GameLoopTime;
                if (now >= state.NextCheck)
                {
                    state.NextCheck = now + CheckIntervalMilliseconds;
                    if (!player.IsAlive || player.Group == null || player.ObjectState != GameObject.eObjectState.Active)
                        why = "left";
                    else
                    {
                        bool inFight = GroupInCombat(player);
                        if (inFight)
                        {
                            state.SawFight = true;
                            state.LastFight = now;
                        }
                        bool gone = target == null || !target.IsAlive || target.ObjectState != GameObject.eObjectState.Active ||
                            target.CurrentRegionID != player.CurrentRegionID;
                        var facts = new GroupOrderFacts(state.SawFight, inFight, inFight ? 0 : now - state.LastFight,
                            now - state.GivenAt, gone, order == GroupOrder.Regroup && AllBack(player));
                        why = Ends(order, facts);
                    }
                }
            }
            if (why == null) return order;
            End(player, why);
            target = null;
            return GroupOrder.None;
        }

        private static bool GroupInCombat(GamePlayer player)
        {
            foreach (GameLiving member in player.Group.GetMembersInTheGroup())
                if (member != null && member.IsAlive && member.InCombat && member.CurrentRegionID == player.CurrentRegionID &&
                    player.IsWithinRadius(member, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS))
                    return true;
            return false;
        }

        private static bool AllBack(GamePlayer player)
        {
            foreach (GameBot bot in Obeying(player))
                if (bot.CurrentRegionID != player.CurrentRegionID || !bot.IsWithinRadius(player, RegroupRadius))
                    return false;
            return true;
        }

        private static void End(GamePlayer player, string why)
        {
            if (player == null || !States.TryGetValue(player, out State state)) return;
            GroupOrder order;
            GameLiving target;
            string via;
            bool wasDefensive, wasHolding;
            lock (state)
            {
                order = state.Order;
                target = state.Target;
                via = state.Via;
                wasDefensive = state.WasDefensive;
                wasHolding = state.WasHolding;
                if (order == GroupOrder.None) return;
                state.Order = GroupOrder.None;
                state.Target = null;
            }
            // The stance from before the order comes back (/order hold's own /hold ends with it, a standing /hold an
            // order lifted returns), unless a stance command is what ended the order: it set its own stance.
            if (why != "stance")
                CompanionEngagementMode.Restore(player, wasDefensive, wasHolding);
            Note(player, "ended", order, target, via, -1, false, why);
        }

        private static void Answer(GamePlayer player, State state, List<GameBot> obeying, GroupOrder order, GameLiving target, bool warn)
        {
            long now = GameLoop.GameLoopTime;
            lock (state)
            {
                // A warning always gets said: the player should hear it even right after another order.
                if (!warn && !Answers(now, state.LastAnswer)) return;
                state.LastAnswer = now;
            }
            GameBot speaker = obeying.Where(bot => bot.IsAlive && bot.Group != null)
                .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
            string text = Line(order, target?.Name, warn);
            if (speaker == null || string.IsNullOrWhiteSpace(text)) return;
            text = AutonomousRoleplayers.Voice(speaker, text);
            Group group = speaker.Group;
            BotSayGate.Say(new BotSayGate.Utterance("group", speaker.Name, speaker.Realm, text, who: speaker), said =>
            {
                group.SendMessageToGroupMembers(speaker, said, eChatType.CT_Group, eChatLoc.CL_ChatWindow);
                return said;
            });
        }

        private static void Note(GamePlayer player, string call, GroupOrder order, GameLiving target, string via, int obeying,
            bool warn, string why)
        {
            if (!Log.IsInfoEnabled) return;
            int size = player.Group?.MemberCount ?? 1;
            // The player's order to his group, given or ended (#667).
            Log.Info($"GROUP_ORDER leader=\"{player.Name}\" call={call} order={Word(order)} via={via ?? "-"} " +
                     $"target=\"{target?.Name ?? "-"}\" obeying={(obeying < 0 ? "-" : obeying.ToString())} size={size} " +
                     $"warn={(warn ? "yes" : "no")} why={why ?? "-"} region={player.CurrentRegionID}");
        }
    }
}
