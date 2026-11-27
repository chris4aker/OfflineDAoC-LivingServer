using System.Runtime.CompilerServices;
using System.Threading;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #569 step 6, main assist for autonomous gamebot PvE parties (rules in <see cref="BotPartyMainAssist"/>). The tank fighting first
    /// publishes its target as the party's kill target; the other members take it in AttackMostWanted over their own
    /// attacker, their pet's fight and their top threat. Lock-free: one small holder per group, read and written by the
    /// members' own thinks. Groups with a real player keep leader focus (BotBrain.LeaderFocus), never both.
    /// </summary>
    public partial class BotBrain
    {
        private sealed class PartyKillTargetHolder
        {
            public volatile GameLiving Writer;
            public volatile GameLiving Target;
            public long Tick;
        }

        private static readonly ConditionalWeakTable<Group, PartyKillTargetHolder> PartyKillTargets = new();

        private long _mainAssistEnemyScanUntil = long.MinValue;
        private bool _mainAssistEnemyInView;

        /// <summary>Main assist runs for this bot (BotPartyMainAssist.Applies).</summary>
        private bool UsesPartyMainAssist
        {
            get
            {
                GameBot bot = BotBody;
                Group group = Body?.Group;
                return bot != null && BotPartyMainAssist.Applies(BotPartyMainAssist.On, bot.IsAutonomousWorldBot,
                    bot.IsPlayerLedGroup || bot.IsTemporaryGroupHelper || bot.IsHiredCompanion || bot.IsPlayerCompanion,
                    group != null, group != null && group.GetPlayersInTheGroup().Count > 0,
                    AutonomousObjectiveAssignments.KindFor(bot) == eAutonomousObjectiveKind.GroupPve) &&
                    !BotPartyMainAssist.StepsAside(AutonomousRealmRaid.GetView(group) != null, RvrTacticsActive(),
                        MainAssistEnemyInView(bot));
            }
        }

        /// <summary>
        /// An enemy player or gamebot stands within <see cref="BotPartyMainAssist.EnemyWatchRadius"/>. Only battle regions
        /// are scanned (elsewhere no enemy realm can be met), at most once a second per bot.
        /// </summary>
        private bool MainAssistEnemyInView(GameBot bot)
        {
            if (!AutonomousWorldBotController.IsBattleground(bot))
                return false;
            long now = GameLoop.GameLoopTime;
            if (now < _mainAssistEnemyScanUntil)
                return _mainAssistEnemyInView;
            _mainAssistEnemyInView = BotMovementHabitsRuntime.EnemyNear(bot, BotPartyMainAssist.EnemyWatchRadius);
            _mainAssistEnemyScanUntil = now + 1000;
            return _mainAssistEnemyInView;
        }

        /// <summary>A gamebot, an enemy-realm pet or a siege engine is never the party's kill target.</summary>
        private bool EligiblePartyKillTarget(GameLiving target) =>
            target is GameNPC npc && BotPartyMainAssist.EligibleKillTarget(npc is GameBot,
                npc.Brain is IControlledBrain && npc.Realm != Body.Realm && npc.Realm != eRealm.None,
                npc is GameSiegeWeapon);

        /// <summary>
        /// The party's kill target for a non-tank member, or null to keep its own choice. Puts the target on the aggro
        /// list, as an assist does, so the attack turn keeps it.
        /// </summary>
        private GameLiving PartyMainAssistTarget(GameLiving directAttacker)
        {
            if (IsTankClass || Body.Group is not { } group || !PartyKillTargets.TryGetValue(group, out PartyKillTargetHolder holder) ||
                !UsesPartyMainAssist)
                return null;
            GameLiving target = holder.Target;
            bool valid = target != null && target != Body &&
                BotPartyMainAssist.IsFresh(GameLoop.GameLoopTime, Volatile.Read(ref holder.Tick)) &&
                EligiblePartyKillTarget(target) && CanDefendAgainst(target) &&
                Body.IsWithinRadius(target, GROUP_DEFENSE_ASSIST_RADIUS) && !EpicEncounterTactics.MustNotHit(BotBody, target);
            // Hit by an enemy player, gamebot or their pet: fight it now, as BotIncomingFireReaction does.
            if (!BotPartyMainAssist.AssistsKillTarget(valid, valid && IsBreakablyControlled(target), Body.HealthPercent,
                    directAttacker != null, BotPvpCrowdControl.PlayerLike(directAttacker)))
                return null;
            if (!AggroList.ContainsKey(target))
                AddToAggroList(target, 1);
            return target;
        }

        /// <summary>
        /// A tank publishes the monster it is fighting as the party's kill target. <paramref name="peel"/>: the tank is
        /// going for a monster on a groupmate, which may start a kill target but never moves a live one.
        /// </summary>
        private void OfferPartyKillTarget(GameLiving target, bool peel)
        {
            if (!IsTankClass || target is not GameNPC { IsAlive: true } npc || !EligiblePartyKillTarget(npc) ||
                npc.ObjectState != GameObject.eObjectState.Active || !(Body.IsAttacking || Body.InCombat) ||
                IsBreakablyControlled(npc) || Body.Group is not { } group || !UsesPartyMainAssist)
                return;
            PartyKillTargetHolder holder = PartyKillTargets.GetValue(group, _ => new PartyKillTargetHolder());
            long now = GameLoop.GameLoopTime;
            long tick = Volatile.Read(ref holder.Tick);
            GameLiving stored = holder.Target, writer = holder.Writer;
            bool storedAlive = stored != null && stored.IsAlive && stored.ObjectState == GameObject.eObjectState.Active;
            bool liveStored = storedAlive && stored != npc && BotPartyMainAssist.IsFresh(now, tick);
            if (!BotPartyMainAssist.MayWriteKillTarget(peel, liveStored))
                return;
            if (stored == npc && writer == Body)
            {
                Volatile.Write(ref holder.Tick, now);
                return;
            }
            if (stored != null && !BotPartyMainAssist.ReplacesKillTarget(storedAlive, writer == Body,
                    writer is { IsAlive: true } && writer.TargetObject == stored, now - tick))
                return;
            holder.Writer = Body;
            holder.Target = npc;
            Volatile.Write(ref holder.Tick, now);
            if (stored != npc)
                // A tank set its party's kill target (once per new target, per party). Names go under dropped keys only.
                log.Info($"AUTONOMOUS_PARTY_KILL_TARGET bot=\"{Body.Name}\" id={BotBody?.DatabaseID ?? 0} " +
                         $"target=\"{npc.Name}\" targetLevel={npc.Level} level={Body.Level} peel={(peel ? "true" : "false")} " +
                         $"replaced={(stored == null ? "none" : storedAlive ? "live" : "dead")} " +
                         $"members={group.MemberCount} region={Body.CurrentRegionID}");
        }
    }
}
