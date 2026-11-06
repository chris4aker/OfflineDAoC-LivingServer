using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// One short-lived gate per player group, no timers, database writes or world scans.
    /// Existing bot turns check the gate; actual attack events release it once.
    /// </summary>
    public static class PlayerLedPullCoordinator
    {
        public const int ContactTimeoutMilliseconds = 30_000;
        private sealed class GroupState { public Order Pending; }
        private sealed class Order
        {
            public Group Group;
            public GamePlayer Leader;
            public GameBot Tank;
            public GameLiving Target;
            public long Deadline;
            public int Finished;
        }
        private static readonly ConditionalWeakTable<Group, GroupState> States = new();

        public static string Begin(GamePlayer player, GameLiving target)
        {
            Group group = player?.Group;
            if (!ValidEnemy(player, target)) return "No valid nearby pull target.";
            if (group == null)
            {
                // Preserve /pull as a direct pet command when playing solo.
                player.ControlledBrain?.Attack(target);
                return player.ControlledBrain == null ? "No party companions or pet to pull with." : "Pet pull ordered.";
            }
            GroupState state = States.GetOrCreateValue(group);
            Cancel(state.Pending, false);
            GameBot[] helpers = group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(bot => Available(bot, player) && bot.IsWithinRadius(target, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) &&
                    bot.Brain is BotBrain).ToArray();
            foreach (GameBot bot in helpers) bot.EnterPlayerLedGroup(player);
            if (CompanionPvpEngagement.Enemy(player, target) && helpers.Any(bot => CompanionPvpEngagement.Leader(bot) == player))
            {
                // RvR is not a PvE tank-contact pull. A resting companion or an
                // unbroken mez must not silently veto the human's explicit order.
                CompanionPvpEngagement.Order(player, target);
                CompanionEngagementMode.RememberPull(player, target);
                foreach (GameBot bot in helpers.Where(bot => CompanionPvpEngagement.Leader(bot) == player))
                    ((BotBrain)bot.Brain).AssistPlayerAttack(target);
                bool engaging = helpers.Any(bot => CompanionPvpEngagement.Leader(bot) == player && CompanionEngagementMode.Allows(bot, target));
                if (engaging) player.ControlledBrain?.Attack(target);
                return engaging
                    ? "Raid PvP target ordered: attackers and pets engage; healers support."
                    : "Defensive mode: target remembered. Bring it within 350 units, or use /aggressive to engage at range.";
            }
            // /pull is a manual order: it fires now. Resting companions stand up
            // instead of vetoing it (a chanting Paladin or Bard never refills, so
            // the old "party is resting" refusal could block /pull forever).
            foreach (GameBot bot in helpers.Where(bot => bot.IsTemporaryCompanionRestLocked))
                bot.WakeTemporaryCompanionRest();
            GameBot tank = helpers.Where(BotPartyRoles.IsTank)
                .Where(bot => !bot.IsCrowdControlled)
                .OrderBy(bot => bot.GetDistanceTo(target)).ThenBy(bot => bot.ObjectID).FirstOrDefault();
            if (tank == null || helpers.Any(bot => bot.InCombat || bot.IsAttacking) || FindLeaderTarget(player) != null)
            {
                Engage(player, target, true);
                return "Pull ordered: attackers engage; healers and buffers support the party.";
            }

            var order = new Order { Group = group, Leader = player, Tank = tank, Target = target,
                Deadline = GameLoop.GameLoopTime + ContactTimeoutMilliseconds };
            Volatile.Write(ref state.Pending, order);
            foreach (GameBot bot in helpers)
                if (bot != tank) ((BotBrain)bot.Brain).PrepareForTankPull();
            if (!((BotBrain)tank.Brain).OrderPull(target))
            {
                Cancel(order, false);
                return "Pull cancelled: the tank could not engage that target.";
            }
            return $"{tank.Name} leads the pull. Attackers wait for tank contact; healers and buffers support.";
        }

        public static bool IsWaiting(GameBot bot)
        {
            if (bot?.Group == null || !States.TryGetValue(bot.Group, out GroupState state)) return false;
            Order order = Volatile.Read(ref state.Pending);
            if (order == null || Volatile.Read(ref order.Finished) != 0) return false;
            if (order.Leader.Group != order.Group || !order.Group.IsInTheGroup(order.Leader) ||
                !ValidEnemy(order.Leader, order.Target) || !Available(order.Tank, order.Leader) ||
                order.Tank.Group != order.Group || !order.Group.IsInTheGroup(order.Tank) ||
                GameLoop.GameLoopTime >= order.Deadline)
            {
                Cancel(order, true);
                return false;
            }
            GameLiving humanTarget = FindLeaderTarget(order.Leader);
            if (humanTarget != null)
            {
                LeaderEngaged(order.Leader, humanTarget);
                return false;
            }
            return bot != order.Tank;
        }

        /// <param name="fromPet">
        /// The player's pet started it (owner 2026-10-29): the fight starts for the companions not yet in one, while a
        /// companion already fighting keeps its own target, and so does its pet; only the player's own attacks and
        /// /pull retarget the whole group.
        /// </param>
        public static void LeaderEngaged(GamePlayer player, GameLiving target, bool fromPet = false)
        {
            CompanionEngagementMode.RememberPull(player, target);
            if (player?.Group == null || !player.Group.IsInTheGroup(player) || !ValidEnemy(player, target)) return;
            CompanionPvpEngagement.Order(player, target);
            if (States.TryGetValue(player.Group, out GroupState state))
            {
                Order order = Volatile.Read(ref state.Pending);
                if (order != null && Volatile.Read(ref order.Finished) == 0 && order.Leader != player) return;
                if (order != null && Interlocked.CompareExchange(ref order.Finished, 1, 0) == 0 && order.Target != target)
                    (order.Tank.Brain as BotBrain)?.CancelOrderedPull(order.Target);
            }
            Engage(player, target, false, fromPet);
        }

        public static void OnAttack(GameLiving actor, AttackData attack)
        {
            if (attack?.Target is not GameLiving target || attack.Attacker != actor || !attack.CausesCombat) return;
            if (actor is GamePlayer player) { LeaderEngaged(player, target); return; }
            // Do not resolve a companion's pet through GetPlayerOwner: that would
            // mistake a bot's pet for a human-initiated override of the tank gate.
            if (actor is GameNPC { Brain: ControlledMobBrain { Owner: GamePlayer petOwner } } &&
                petOwner.ControlledBrain?.Body == actor)
            { LeaderEngaged(petOwner, target); return; }
            if (actor is not GameBot tank || tank.Group == null || !attack.IsMeleeAttack ||
                !States.TryGetValue(tank.Group, out GroupState state)) return;
            Order order = Volatile.Read(ref state.Pending);
            if (order?.Tank != tank || order.Target != target || order.Leader.Group != order.Group ||
                !order.Group.IsInTheGroup(order.Leader) || !Available(tank, order.Leader) || !IsContact(attack.AttackResult) ||
                Interlocked.CompareExchange(ref order.Finished, 1, 0) != 0) return;
            Engage(order.Leader, target, true);
        }

        public static bool IsContact(eAttackResult result) => result is eAttackResult.HitStyle or
            eAttackResult.HitUnstyled or eAttackResult.Blocked or eAttackResult.Parried or eAttackResult.Evaded or eAttackResult.Missed;

        public static void OnGroupThreat(GameLiving member)
        {
            if (member?.Group != null && States.TryGetValue(member.Group, out GroupState state))
            {
                // Incoming danger overrides ordered-pull waiting, not group/range
                // restrictions. The existing defensive broadcast chooses the foe.
                Order order = Volatile.Read(ref state.Pending);
                if (order != null && Interlocked.CompareExchange(ref order.Finished, 1, 0) == 0)
                    (order.Tank.Brain as BotBrain)?.CancelOrderedPull(order.Target);
            }
        }

        /// <summary>
        /// The monster the player is fighting, which their companions join: what the player or the player's pet attacks,
        /// or what the player casts harmful magic at. A mesmerize, stun or root is not one (owner 2026-10-29: companions
        /// broke the player's own mez): that fight is joined on another monster (<see cref="FindLeaderControlFight"/>).
        /// </summary>
        public static GameLiving FindLeaderTarget(GamePlayer player)
        {
            if (player?.IsAlive != true) return null;
            Spell casting = player.castingComponent?.SpellHandler?.Spell;
            if ((player.IsAttacking || player.IsCasting && casting?.IsHarmful == true && !IsCrowdControl(casting)) &&
                player.TargetObject is GameLiving target && ValidEnemy(player, target)) return target;
            if (player.ControlledBrain is ControlledMobBrain brain && brain.Owner == player &&
                brain.Body?.IsAlive == true && brain.Body.CurrentRegionID == player.CurrentRegionID &&
                player.IsWithinRadius(brain.Body, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS))
            {
                GameLiving petTarget = brain.OrderedAttackTarget ??
                    ((brain.Body.IsAttacking || brain.Body.IsCasting &&
                      brain.Body.castingComponent?.SpellHandler?.Spell?.IsHarmful == true)
                        ? brain.Body.TargetObject as GameLiving : null);
                if (ValidEnemy(player, petTarget)) return petTarget;
            }
            return null;
        }

        private static void Engage(GamePlayer player, GameLiving target, bool orderHumanPet, bool onlyIdle = false)
        {
            if (player?.Group == null || !ValidEnemy(player, target)) return;
            foreach (GameLiving member in player.Group.GetMembersInTheGroup())
            {
                if (member is GameBot bot && bot.PlayerGroupLeader == player && Available(bot, player) &&
                    bot.IsWithinRadius(target, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) && bot.Brain is BotBrain brain &&
                    !(onlyIdle && InOwnFight(bot)))
                    brain.AssistPlayerAttack(target);
            }
            if (orderHumanPet) player.ControlledBrain?.Attack(target);
        }

        /// <summary>A companion already fighting a live, unheld monster of its own.</summary>
        public static bool InOwnFight(GameBot bot) =>
            bot.Brain is BotBrain { HasAggro: true } && bot.TargetObject is GameLiving { IsAlive: true } current &&
            !BotBrain.IsBreakablyControlled(current);

        public static bool Available(GameBot bot, GamePlayer player) => bot != null && player?.IsAlive == true &&
            bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && bot.Realm == player.Realm &&
            bot.Group == player.Group && bot.Group?.IsInTheGroup(bot) == true &&
            bot.CurrentRegionID == player.CurrentRegionID && bot.IsWithinRadius(player, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) &&
            !bot.IsOnStableMasterRoute && !bot.IsReturningAfterRelease;

        /// <summary>The monster the player is mesmerizing, stunning or rooting right now, or null.</summary>
        public static GameLiving LeaderControlTarget(GamePlayer player)
        {
            if (player?.IsAlive != true || !player.IsCasting) return null;
            Spell casting = player.castingComponent?.SpellHandler?.Spell;
            return IsCrowdControl(casting) && player.TargetObject is GameLiving target && target.IsAlive ? target : null;
        }

        /// <summary>
        /// The player is controlling one monster of a pack: the companions join the fight on another monster that is
        /// fighting the group (nearest first, never the controlled one or one already held), or stay out when none is.
        /// </summary>
        public static GameLiving FindLeaderControlFight(GamePlayer player, GameBot bot)
        {
            GameLiving held = LeaderControlTarget(player);
            if (held == null || player.Group == null || bot == null) return null;
            GameLiving best = null;
            int bestDistance = int.MaxValue;
            foreach (GameNPC npc in player.GetNPCsInRadius((ushort)BotBrain.GROUP_DEFENSE_ASSIST_RADIUS))
            {
                if (npc == held || !npc.IsAlive || npc is GameBot || BotBrain.IsBreakablyControlled(npc) ||
                    npc.TargetObject is not GameLiving victim || !InGroupFight(player, victim) || !ValidEnemy(player, npc))
                    continue;
                int distance = bot.GetDistanceTo(npc);
                if (distance < bestDistance)
                {
                    best = npc;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static bool InGroupFight(GamePlayer player, GameLiving victim)
        {
            if (victim is GameNPC { Brain: IControlledBrain controlled } && controlled.GetLivingOwner() is GameLiving owner)
                victim = owner;
            return victim == player || player.Group.IsInTheGroup(victim);
        }

        /// <summary>A spell that holds a monster rather than hurts it: mesmerize, stun, root.</summary>
        public static bool IsCrowdControl(Spell spell) =>
            spell?.SpellType is eSpellType.Mesmerize or eSpellType.Mez or eSpellType.Stun or eSpellType.Amnesia ||
            spell?.SpellType is eSpellType.SpeedDecrease or eSpellType.UnbreakableSpeedDecrease && spell.Value >= 99;

        private static bool ValidEnemy(GamePlayer player, GameLiving target) => player != null && target?.IsAlive == true &&
            target.ObjectState == GameObject.eObjectState.Active && target.CurrentRegionID == player.CurrentRegionID &&
            player.IsWithinRadius(target, BotBrain.GROUP_DEFENSE_ASSIST_RADIUS) &&
            GameServer.ServerRules.IsAllowedToAttack(player, target, true);

        private static void Cancel(Order order, bool inform)
        {
            if (order == null || Interlocked.CompareExchange(ref order.Finished, 1, 0) != 0) return;
            (order.Tank.Brain as BotBrain)?.CancelOrderedPull(order.Target);
            if (inform && order.Leader.Group == order.Group)
                order.Leader.Out.SendMessage("Pull cancelled: the tank could not make contact. Reposition and use /pull again.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }
    }
}
