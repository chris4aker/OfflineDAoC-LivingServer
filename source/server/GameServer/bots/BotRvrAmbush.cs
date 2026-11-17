using System.Linq;
using DOL.Database;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.GS.ServerProperties;
using DOL.GS.Styles;
using DOL.Logging;

namespace DOL.GS
{
    public static class BotRvrAmbush
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(BotRvrAmbush));

        public static bool IsStealthClass(eCharacterClass characterClass) => BotPoisonSupply.IsAssassin(characterClass) ||
            characterClass is eCharacterClass.Hunter or eCharacterClass.Scout or eCharacterClass.Ranger or eCharacterClass.Minstrel;

        public static bool IsEnemyCombatant(GameBot bot, GameLiving target) =>
            target is GamePlayer or GameBot && target.IsAlive && target.Realm != eRealm.None &&
            target.Realm != bot.Realm && target.CurrentRegionID == bot.CurrentRegionID;

        public static bool CanApproach(GameBot bot, GameLiving target) => bot != null &&
            IsStealthClass((eCharacterClass)bot.CharacterClass.ID) &&
            (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0 &&
            bot.IsAlive && !bot.InCombat && !bot.IsAttacking && !bot.IsCasting && !bot.IsCrowdControlled &&
            !bot.IsOnHorse && bot.castingComponent?.HasPendingSkillRequests != true && !GameRelic.IsPlayerCarryingRelic(bot) &&
            IsEnemyCombatant(bot, target) && bot.IsWithinRadius(target, 2_000) &&
            GameServer.ServerRules.IsAllowedToAttack(bot, target, true);

        // ---- 1.2 work package 6: the stealth opener (RvrStealthOpener, rvr_bot_stealth_openers) ----------------------------

        private sealed class OpenerState
        {
            public GameLiving Target;
            public long Since;
            public long QueuedAt;
            public Style Queued;
            public bool Opened;
        }

        private static readonly ConditionalWeakTable<GameBot, OpenerState> Openers = new();

        private static bool OpenerClass(GameBot bot) =>
            bot?.CharacterClass != null && RvrStealthOpener.IsOpenerClass((eCharacterClass)bot.CharacterClass.ID);

        /// <summary>
        /// Whether the bot already opened on (or gave up the opener against) its current target. An opener that has just
        /// landed counts at once, before the brain's next turn drops stealth, so a second swing from stealth cannot repeat it.
        /// </summary>
        private static bool OpenedOnCurrent(GameBot bot)
        {
            if (!Openers.TryGetValue(bot, out OpenerState state) || state.Target != bot.TargetObject)
                return false;
            AttackData last = bot.attackComponent?.attackAction?.LastAttackData;
            return state.Opened || state.Queued != null && last?.Style?.ID == state.Queued.ID && last.Target == state.Target;
        }

        /// <summary>
        /// The style policy's question (BotBrain.RvrTacticsAllowsStealthOpener, asked by BotMeleeStylePolicy.Usable): may
        /// this hidden stealther use this stealth style? The caller has checked the switch and the scope.
        /// </summary>
        public static bool AllowsOpener(GameBot bot, Style style, bool inScope) =>
            RvrStealthOpener.Allows(Properties.RVR_BOT_STEALTH_OPENERS, inScope, style?.StealthRequirement == true,
                bot?.IsStealthed == true, OpenerClass(bot), bot != null && OpenedOnCurrent(bot));

        /// <summary>
        /// The opener's turn (BotBrain.TryRvrTacticsMove, before the ambush approach): a hidden melee stealther at melee
        /// range of an enemy player or bot queues its best usable stealth style and swings from stealth, or first steps
        /// behind its target for a back opener (for a few seconds at most). True while the opener owns the turn; false
        /// once it has landed (or cannot be used), so the brain drops stealth and fights as before.
        /// </summary>
        public static bool TryStealthOpener(GameBot bot, GameLiving target)
        {
            if (bot?.IsStealthed != true || target == null || !OpenerClass(bot) || !IsEnemyCombatant(bot, target) ||
                bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance || bot.Styles == null)
                return false;
            using var profile = AiProfile.Scope(AiSystem.RvrTactics);
            OpenerState state = Openers.GetValue(bot, _ => new OpenerState());
            long now = GameLoop.GameLoopTime;
            lock (state)
            {
                if (state.Target != target)
                {
                    state.Target = target;
                    state.Since = now;
                    state.QueuedAt = 0;
                    state.Queued = null;
                    state.Opened = false;
                }
                AttackData last = bot.attackComponent?.attackAction?.LastAttackData;
                if (state.Queued != null && last?.Style?.ID == state.Queued.ID && last.Target == target)
                    state.Opened = true;
                bool inRange = bot.IsWithinRadius(target, bot.MeleeAttackRange + 32);
                if (!inRange || state.Opened)
                    return false;

                DbInventoryItem left = bot.Inventory?.GetItem(eInventorySlot.LeftHandWeapon);
                Style[] openers = bot.Styles.Where(style => style?.StealthRequirement == true && style.Level <= bot.Level &&
                        BotMeleeStylePolicy.MatchesWeapon(style, bot.ActiveWeapon, left, bot.ActiveWeaponSlot))
                    .ToArray();
                if (openers.Length == 0)
                {
                    state.Opened = true;
                    return false;
                }
                Style usable = openers.Where(style => BotMeleeStylePolicy.Usable(bot, style, last))
                    .OrderByDescending(style => style.GrowthRate).ThenByDescending(style => style.Level).FirstOrDefault();
                bool needsBehind = usable == null && openers.Any(BotMeleeStylePolicy.IsBackOrSide);
                RvrSkillTier tier = RvrFightBoards.DefaultSkillTier(bot);
                bool goesBehind = RvrStealthOpener.GoesBehind(tier,
                    RvrRoamCircuits.Roll($"behind|{bot.DatabaseID}", target.ObjectID));
                switch (RvrStealthOpener.Next(true, true, false, usable != null, needsBehind, goesBehind, state.QueuedAt, state.Since, now))
                {
                    case RvrStealthOpener.Move.Open:
                        bot.styleComponent.NextCombatStyle = usable;
                        state.Queued = usable;
                        state.QueuedAt = now;
                        bot.StopMovingOnPath();
                        bot.TurnTo(target);
                        bot.StartAttack(target);
                        if (RvrLogBudget.Take("RVR_STEALTH_OPEN", now, 60))
                            Log.Info($"RVR_STEALTH_OPEN bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                                     $"class={(eCharacterClass)bot.CharacterClass.ID} style=\"{usable.Name}\" target=\"{target.Name}\" " +
                                     $"targetRealm={GlobalConstants.RealmToName(target.Realm)} player={target is GamePlayer} tier={tier} " +
                                     $"waitedMs={now - state.Since} region={bot.CurrentRegionID} position={bot.X},{bot.Y},{bot.Z}");
                        return true;
                    case RvrStealthOpener.Move.Wait:
                        if (bot.attackComponent?.AttackState != true)
                            bot.StartAttack(target);
                        return true;
                    case RvrStealthOpener.Move.GetBehind:
                        Point2D behind = target.GetPointFromHeading((ushort)((target.Heading + 2048) & 0xFFF), 90);
                        if (Vector2.DistanceSquared(new(bot.X, bot.Y), new(behind.X, behind.Y)) > 25 * 25)
                            bot.PathTo(new Vector3(behind.X, behind.Y, target.Z), bot.MaxSpeed);
                        return true;
                    default:
                        // Queued too long, or no opener fits from here: the fight goes on without one.
                        state.Opened = true;
                        return false;
                }
            }
        }
    }
}
