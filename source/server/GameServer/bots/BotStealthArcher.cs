using System;
using System.Runtime.CompilerServices;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// The archer's stealth opener (#522; stealth.md finding 5; rubric reference.md 4.2-4.3: "an archer's first shot comes
    /// from 1,500-2,300 with no warning"). Before, a bot Scout, Hunter or Ranger followed its target to 1,000 in stealth,
    /// unhid and fired a plain arrow. Now, in the open-field scope (rvr_bot_stealth_openers, like the melee opener), a hidden
    /// archer stops at its bow's range (at most 2,300, at least 1,500 when the bow reaches that far), draws a Critical Shot
    /// from stealth when the target may be critical-shot (standing, walking or casting; not running, swinging or mezzed) and
    /// the bot has the spec for it, and keeps stealth on the nock by the player's chance (stealth spec / level, 20 points
    /// less for a Critical Shot; AttackComponent). Once the shot is away the brain fights as before. Logged as
    /// RVR_STEALTH_ARCHER_OPEN. Rapid Fire as the target closes needs the Rapid Fire ability on bots (the realm and class
    /// ability work, #531): not done here. The rules are pure; <see cref="TryOpen"/> reads the game.
    /// </summary>
    public static class BotStealthArcher
    {
        public const int MinOpenerRange = 1_500;
        public const int MaxOpenerRange = 2_300;
        /// <summary>How far inside the bow's reach the archer stands, so a small step of the target does not put it out of range.</summary>
        public const int RangeMargin = 50;
        /// <summary>A draw that has not loosed in this long is given up (the fight goes on without the opener).</summary>
        public const long DrawMilliseconds = 8_000;

        // ---- pure ----------------------------------------------------------------------------------------------------

        /// <summary>Where the archer stands to open: its bow's range less a margin, never beyond 2,300. Pure.</summary>
        public static int OpenerRange(int bowRange) => Math.Clamp(bowRange - RangeMargin, 0, MaxOpenerRange);

        /// <summary>Whether that stand is the period opener's (1,500-2,300), not a short-bow shot from closer in. Pure.</summary>
        public static bool IsLongOpener(int range) => range >= MinOpenerRange && range <= MaxOpenerRange;

        /// <summary>Critical Shot's level by bow spec, as the player's class grants it (ClassScout/Hunter/Ranger): one per 3 spec, at most 9; none under 3. Pure.</summary>
        public static int CriticalShotLevel(int bowSpec) => bowSpec < 3 ? 0 : Math.Min(9, bowSpec / 3);

        /// <summary>The player's rule for a Critical Shot target (RangeAttackComponent): not running (over walking speed), not swinging in combat, not mezzed. Pure.</summary>
        public static bool CriticalAllowed(int targetSpeed, bool targetSwinging, bool targetMezzed) =>
            targetSpeed <= 90 && !targetSwinging && !targetMezzed;

        /// <summary>The player's chance (in percent) to stay hidden while nocking: stealth spec x 100 / level, 20 less for a Critical Shot. Pure.</summary>
        public static int StayHiddenChance(int stealthSpec, int level, bool critical) =>
            stealthSpec * 100 / Math.Max(1, level) - (critical ? 20 : 0);

        // ---- the game ------------------------------------------------------------------------------------------------

        private sealed class State
        {
            public GameLiving Target;
            public long DrawnAt;
            public AttackData LastBefore;
            public bool Opened;
        }

        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        public static bool IsStealthArcher(GameBot bot) => bot?.CharacterClass != null &&
            BotRangedCombat.IsDedicatedArcher((eCharacterClass)bot.CharacterClass.ID) &&
            BotRangedCombat.CanUse(bot, bot.Inventory?.GetItem(eInventorySlot.DistanceWeapon));

        /// <summary>The bot's best bow spec (Longbow, Composite Bow or Recurve Bow).</summary>
        public static int BowSpec(GameBot bot) => Math.Max(bot.GetModifiedSpecLevel(Specs.Longbow),
            Math.Max(bot.GetModifiedSpecLevel(Specs.CompositeBow), bot.GetModifiedSpecLevel(Specs.RecurveBow)));

        /// <summary>
        /// How close the ambush approach (BotBrain.TryApproachAmbush) takes a hidden archer: its opener range with the
        /// opener on, the old 1,000 without. The bow is put in hand for the reading (a hidden archer carries it anyway).
        /// </summary>
        public static int ApproachRange(GameBot bot, bool openerOn)
        {
            if (!openerOn)
                return 1_000;
            if (bot.ActiveWeaponSlot != eActiveWeaponSlot.Distance)
                bot.SwitchWeapon(eActiveWeaponSlot.Distance);
            int bow = bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance ? bot.attackComponent.AttackRange : 0;
            return bow > 0 ? Math.Max(1_000, OpenerRange(bow)) : 1_000;
        }

        /// <summary>
        /// The opener's turn (BotBrain.TryRvrTacticsMove, beside the melee opener): true while it owns the brain's turn (the
        /// draw from stealth); false when it is not an opener's turn or once the shot is away.
        /// </summary>
        public static bool TryOpen(GameBot bot, GameLiving target)
        {
            if (!Properties.RVR_BOT_STEALTH_OPENERS || bot == null || target == null || !IsStealthArcher(bot) ||
                !BotRvrAmbush.IsEnemyCombatant(bot, target))
                return false;
            State state = States.GetValue(bot, _ => new State());
            long now = GameLoop.GameLoopTime;
            lock (state)
            {
                if (state.Target != target)
                {
                    state.Target = target;
                    state.DrawnAt = 0;
                    state.LastBefore = null;
                    state.Opened = false;
                }
                if (state.Opened)
                    return false;
                if (state.DrawnAt > 0)
                {
                    AttackData last = bot.attackComponent?.attackAction?.LastAttackData;
                    bool loosed = last != null && last != state.LastBefore && last.Target == target;
                    if (!loosed && bot.attackComponent?.AttackState == true && now - state.DrawnAt < DrawMilliseconds)
                        return true;
                    state.Opened = true;
                    BotStealthEdge.Opened(bot, target, now);
                    return false;
                }
                if (!bot.IsStealthed || bot.InCombat)
                    return false;
                if (bot.ActiveWeaponSlot != eActiveWeaponSlot.Distance)
                    bot.SwitchWeapon(eActiveWeaponSlot.Distance);
                int bowRange = bot.attackComponent.AttackRange;
                int distance = (int)bot.GetDistanceTo(target);
                if (distance > bowRange - 30)
                    return false; // the approach closes in first
                bool critical = CriticalShotLevel(BowSpec(bot)) > 0 && bot.Endurance >= RangeAttackComponent.CRITICAL_SHOT_ENDURANCE_COST &&
                    CriticalAllowed(target.CurrentSpeed, target.attackComponent?.AttackState == true && target.InCombat,
                        target.effectListComponent?.ContainsEffectForEffectType(eEffect.Mez) == true);
                bool staysHidden = Util.Chance(StayHiddenChance(bot.GetModifiedSpecLevel(Specs.Stealth), bot.Level, critical));
                bot.StopMovingOnPath();
                bot.StopMoving();
                bot.TurnTo(target);
                state.LastBefore = bot.attackComponent?.attackAction?.LastAttackData;
                bot.rangeAttackComponent.RangedAttackType = critical ? eRangedAttackType.Critical : eRangedAttackType.Normal;
                bot.StartAttack(target);
                if (critical && bot.rangeAttackComponent.RangedAttackType != eRangedAttackType.Critical &&
                    bot.rangeAttackComponent.RangedAttackState is eRangedAttackState.Aim or eRangedAttackState.None)
                    bot.rangeAttackComponent.RangedAttackType = eRangedAttackType.Critical;
                if (!staysHidden)
                    bot.Stealth(false);
                state.DrawnAt = now;
                RvrStealthRecord.ArcherOpened(bot, target, distance, critical, staysHidden);
                return true;
            }
        }
    }

    /// <summary>
    /// "My edge is gone" (#522; stealth.md finding 6; rubric reference.md 4.4: "a stealther who loses the opener usually
    /// runs"). A gamebot stealther that opened from stealth (the melee opener or the archer's) and did not take its target
    /// below half health once the opener had its moment, or that a second enemy joined, has lost its edge: the solo bail
    /// (BotBrain.ConsiderSoloBail) lets it run then, whatever its own health, and the travel stealth (BotStealthTravel)
    /// hides it again once it is out of combat. The one flee model (#533) may absorb this as one of its inputs. Pure
    /// apart from the per-bot memory of the last opener.
    /// </summary>
    public static class BotStealthEdge
    {
        /// <summary>How long after the opener the target's health is judged (the opener's follow-up has landed by then).</summary>
        public const long JudgeAfterMilliseconds = 4_000;
        /// <summary>An opener older than this no longer gives or takes an edge: the fight is an ordinary one.</summary>
        public const long EdgeMilliseconds = 30_000;
        public const int HalfHealth = 50;

        private sealed class Opener
        {
            public GameLiving Target;
            public long At;
        }

        private static readonly ConditionalWeakTable<GameBot, Opener> Openers = new();

        /// <summary>Notes an opener from stealth on <paramref name="target"/>.</summary>
        public static void Opened(GameBot bot, GameLiving target, long now)
        {
            if (bot == null || target == null)
                return;
            Opener opener = Openers.GetValue(bot, _ => new Opener());
            lock (opener)
            {
                opener.Target = target;
                opener.At = now;
            }
        }

        /// <summary>
        /// Whether the edge is gone (pure): an opener within <see cref="EdgeMilliseconds"/>, and either a second attacker,
        /// or the target still at or above half health once <see cref="JudgeAfterMilliseconds"/> have passed.
        /// </summary>
        public static bool Gone(bool opened, long sinceOpenerMilliseconds, int targetHealthPercent, bool targetAlive, int attackers) =>
            opened && targetAlive && sinceOpenerMilliseconds >= 0 && sinceOpenerMilliseconds <= EdgeMilliseconds &&
            (attackers >= 2 || sinceOpenerMilliseconds >= JudgeAfterMilliseconds && targetHealthPercent >= HalfHealth);

        /// <summary>The edge check for this bot now (reads its last opener, its target and its attackers).</summary>
        public static bool Gone(GameBot bot, long now, out string reason)
        {
            reason = null;
            if (bot == null || !Openers.TryGetValue(bot, out Opener opener))
                return false;
            GameLiving target;
            long at;
            lock (opener)
            {
                target = opener.Target;
                at = opener.At;
            }
            int attackers = bot.attackComponent?.AttackerTracker.Count ?? 0;
            if (target == null || !Gone(true, now - at, target.HealthPercent, target.IsAlive, attackers))
                return false;
            reason = attackers >= 2 ? "edgeSecondEnemy" : "edgeOpenerFailed";
            return true;
        }

        /// <summary>Forgets the opener (after the bot ran on it).</summary>
        public static void Clear(GameBot bot)
        {
            if (bot != null)
                Openers.Remove(bot);
        }
    }
}
