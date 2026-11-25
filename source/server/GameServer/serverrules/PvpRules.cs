using System;
using DOL.AI.Brain;

namespace DOL.GS.ServerRules
{
    /// <summary>What a character is, for the PvP rules (#521). Real players, autonomous gamebots and companions are told apart.</summary>
    public enum PvpCharacterKind : byte
    {
        /// <summary>A monster, guard, boss, pet or anything else that is not a player character.</summary>
        Other = 0,
        /// <summary>The real player (a GamePlayer).</summary>
        Player,
        /// <summary>An autonomous gamebot (GameBot.IsAutonomousWorldBot, not a /spawn helper or a hired companion).</summary>
        AutonomousGamebot,
        /// <summary>A player's companion: a /spawn helper, temporary group helper or hired companion.</summary>
        Companion,
    }

    /// <summary>One side of a PvP rule, as plain values, so the rule is pure and unit-tested.</summary>
    public readonly record struct PvpSide(PvpCharacterKind Kind, eRealm Realm)
    {
        public bool IsPlayerCharacter => Kind is PvpCharacterKind.Player or PvpCharacterKind.AutonomousGamebot;
    }

    /// <summary>
    /// Player/bot rule parity (#521; docs/rvr-audit-1.2.3/mechanics.md, "The one cause"). The base server writes its PvP
    /// rules as "if the character is a GamePlayer, the player rule; otherwise the monster rule", and a GameBot is a GameNPC,
    /// so autonomous gamebots got monster mez immunity, monster interrupts, monster crits, monster speed, no rez sickness
    /// and no line of sight on bot-to-bot casts. Every such site now asks <see cref="TakesPlayerRules(GameLiving, GameLiving)"/>.
    ///
    /// Owner decision (2026-11-24, #521): autonomous gamebots follow the player's PvP rules in RvR; they keep the fast rest
    /// regeneration in PvE only. Companions are unchanged (the owner named autonomous gamebots only): they keep today's
    /// rules everywhere. Monsters, guards and bosses are never touched: a rule only changes for the autonomous gamebot side.
    ///
    /// "In RvR" has two parts:
    /// <list type="bullet">
    /// <item>A rule with two sides (crowd control and its immunity, interrupts, crits, facing, cast line of sight, spell
    /// level): the other side, after its pets and companions resolve to who controls them, is an enemy-realm player
    /// character (the player or an autonomous gamebot). A bot fighting a monster, a guard or a boss keeps today's rule
    /// wherever it is, so no PvE or keep-guard fight changes.</item>
    /// <item>A rule about the bot alone (run speed, sprint, stealth speed, regeneration, rez sickness): the bot stands in a
    /// frontier zone or a battleground region (<see cref="IsRvrArea"/>), or has fought an enemy realm in the last
    /// 10 s (GameLiving.InCombatPvP). Darkness Falls and the other dungeons are not RvR areas.</item>
    /// </list>
    /// A GamePlayer always takes the player rule, as before.
    /// </summary>
    public static class PvpRules
    {
        // ---- the pure rule ------------------------------------------------------------------------------------------

        /// <summary>
        /// Whether <paramref name="subject"/> takes the player's rule. <paramref name="other"/> is the other side of a
        /// two-sided rule (null for a rule about the subject alone); <paramref name="subjectInRvr"/> is
        /// <see cref="InRvr(GameLiving)"/> and is read only for a one-sided rule. Pure.
        /// </summary>
        public static bool TakesPlayerRules(in PvpSide subject, PvpSide? other, bool subjectInRvr)
        {
            if (subject.Kind == PvpCharacterKind.Player)
                return true;
            if (subject.Kind != PvpCharacterKind.AutonomousGamebot)
                return false;
            if (other is not { } side)
                return subjectInRvr;
            return side.IsPlayerCharacter && side.Realm != eRealm.None && side.Realm != subject.Realm;
        }

        /// <summary>The RvR areas (pure): a frontier zone (Zone.IsRvR), New Frontiers, a battleground (the bracketed
        /// battlegrounds, Cathal Valley and the old battleground regions) or the frontier dungeon. Not Darkness Falls (249),
        /// Summoner's Hall (233) or Demons' Breach (489): raids and PvE there keep today's rules.</summary>
        public static bool IsRvrArea(ushort regionId, bool zoneIsRvr) =>
            zoneIsRvr || regionId is 163 or 165 or (>= 234 and <= 242) or 244 || BattlegroundBrackets.IsBattlegroundRegion(regionId);

        // ---- the game's characters ----------------------------------------------------------------------------------

        public static PvpCharacterKind KindOf(GameLiving living) => living switch
        {
            GamePlayer => PvpCharacterKind.Player,
            GameBot bot when bot.IsTemporaryGroupHelper || bot.IsPlayerCompanion => PvpCharacterKind.Companion,
            GameBot { IsAutonomousWorldBot: true } => PvpCharacterKind.AutonomousGamebot,
            GameBot => PvpCharacterKind.Companion,
            _ => PvpCharacterKind.Other,
        };

        /// <summary>The player or an autonomous gamebot (not a companion, pet, monster or guard).</summary>
        public static bool IsPlayerCharacter(GameLiving living) =>
            KindOf(living) is PvpCharacterKind.Player or PvpCharacterKind.AutonomousGamebot;

        public static bool IsAutonomousGamebot(GameLiving living) => KindOf(living) == PvpCharacterKind.AutonomousGamebot;

        /// <summary>Who stands behind <paramref name="living"/>: a pet's (or a companion's) controller, followed up the chain.</summary>
        public static GameLiving Controller(GameLiving living)
        {
            GameLiving current = living;
            for (int depth = 0; depth < 16 && current is GameNPC npc && npc.Brain is IControlledBrain controlled &&
                 controlled.GetLivingOwner() is GameLiving owner && owner != current; depth++)
                current = owner;
            return current;
        }

        public static PvpSide SideOf(GameLiving living) => new(KindOf(living), living?.Realm ?? eRealm.None);

        /// <summary>In an RvR area (<see cref="IsRvrArea"/>), or fought an enemy realm in the last 10 s.</summary>
        public static bool InRvr(GameLiving living)
        {
            if (living == null)
                return false;
            if (living.InCombatPvP)
                return true;
            return living.CurrentRegion != null && IsRvrArea(living.CurrentRegionID, living.CurrentZone?.IsRvR == true);
        }

        /// <summary>
        /// The one "is a player character in PvP" check: whether <paramref name="subject"/> takes the player's rule
        /// against <paramref name="other"/> (null: a rule about the subject alone). A GamePlayer always does; an
        /// autonomous gamebot does in RvR (see the class summary); a companion, monster, guard or boss never does.
        /// </summary>
        public static bool TakesPlayerRules(GameLiving subject, GameLiving other = null)
        {
            if (subject is GamePlayer)
                return true;
            if (subject is not GameBot)
                return false;
            PvpSide self = SideOf(subject);
            if (self.Kind != PvpCharacterKind.AutonomousGamebot)
                return false;
            if (other != null)
                return TakesPlayerRules(self, SideOf(Controller(other)), false);
            return TakesPlayerRules(self, null, InRvr(subject));
        }

        // ---- the rule at each site (pure; the parity fixture asks each the same question for a player and a bot) ----

        /// <summary>The extra interrupt a monster gets on every hit (GameNPC.StartInterruptTimer): 2.5 s, not for a player
        /// character in PvP nor for a player's pet or companion.</summary>
        public const int MonsterInterruptExtraMs = 2500;

        public static int InterruptExtraMs(bool playerRules, bool hasPlayerOwner) =>
            playerRules || hasPlayerOwner ? 0 : MonsterInterruptExtraMs;

        /// <summary>The most a critical hit adds (melee and spell): half the damage on a player character, all of it on a monster.</summary>
        public static double CriticalCap(bool targetTakesPlayerRules) => targetTakesPlayerRules ? 0.5 : 1.0;

        /// <summary>The "enraged" refusal (CCSpellHandler): a monster below 75% health cannot be mezzed. A player never is.</summary>
        public static bool EnragedAgainstMez(bool playerRules, bool isNpc, int healthPercent) =>
            !playerRules && isNpc && healthPercent < 75;

        /// <summary>Which immunity a crowd-control effect leaves (ECSGameEffect.AppliedImmunityType): the player's (60 s
        /// after it wears off; roots, snares and nearsight too; no refresh by the same spell) or a monster's (diminishing,
        /// from the start; mez and stun only).</summary>
        public static ECSGameEffect.ImmunityType ImmunityTypeFor(bool triggersImmunity, bool playerRules) =>
            !triggersImmunity ? ECSGameEffect.ImmunityType.None :
            playerRules ? ECSGameEffect.ImmunityType.Player : ECSGameEffect.ImmunityType.Npc;

        /// <summary>A melee attacker must face its target (AttackComponent): the player always, a bot in PvP.</summary>
        public static bool MustFaceToSwing(bool attackerTakesPlayerRules) => attackerTakesPlayerRules;

        /// <summary>The low-health slow of monsters (MaxSpeedCalculator): below 33% health, 20-100% speed. Never for a player.</summary>
        public static double LowHealthSpeedFactor(bool playerRules, double healthFraction) =>
            playerRules || !(healthFraction < 0.33) ? 1.0 : 0.2 + healthFraction * (0.8 / 0.33);

        /// <summary>The player's speed in stealth (MaxSpeedCalculator): 30% plus up to 30% by Stealth spec, then Mastery
        /// of Stealth and Shadow Run.</summary>
        public static double StealthSpeedFactor(double stealthSpec, int level, double masteryOfStealthPercent, bool shadowRun)
        {
            stealthSpec = Math.Min(stealthSpec, level);
            double factor = 0.3 + (stealthSpec + 10) * 0.3 / (level + 10);
            factor *= 1 + masteryOfStealthPercent / 100.0;
            if (shadowRun)
                factor *= 2;
            return factor;
        }

        public const double SprintFactor = 1.3;

        /// <summary>Sprint's speed (MaxSpeedCalculator): +30% for a player character. A monster's sprint effect does nothing.</summary>
        public static double SprintSpeedFactor(bool playerRules, bool sprinting) => playerRules && sprinting ? SprintFactor : 1.0;

        /// <summary>Endurance a moving sprinter loses each 1 s tick (GamePlayer.EnduranceRegenerationTimerCallback): 5, less Long Wind.</summary>
        public static int SprintDrainPerTick(int longWindPercent) => 5 - 5 * longWindPercent / 100;

        /// <summary>The RvR resurrection illness (GamePlayer.OnRevive): a player character raised after an RvR death at or
        /// above RESS_SICKNESS_LEVEL, unless the spell removes it (Perfect Recovery: effectiveness 0).</summary>
        public static bool RvrRezSicknessApplies(bool playerRules, bool rvrDeath, int level, int sicknessLevel, double rezSickEffectiveness) =>
            playerRules && rvrDeath && level >= sicknessLevel && rezSickEffectiveness > 0;

        /// <summary>The repeat-kill window (AbstractServerRules.AwardPlayerOnPlayerKill): a character is worth realm points
        /// only if its last death is at least <paramref name="windowSeconds"/> old (times in seconds played).</summary>
        public static bool WorthRealmPoints(long lastDeathTime, long playedTime, long windowSeconds) =>
            lastDeathTime + windowSeconds <= playedTime;

        /// <summary>Whether the bot's fast rest floor (BotRestRecovery, 10% a second) applies: PvE only.</summary>
        public static bool UsesFastRest(bool playerRules) => !playerRules;

        /// <summary>Health and power tick (ClassicRestRegeneration): the player's 6 s standing, 3 s sitting, longer in combat.</summary>
        public static int PlayerRegenerationIntervalMs(bool sitting, bool inCombat) =>
            ClassicRestRegeneration.HealthAndPowerInterval(sitting, inCombat);

        /// <summary>The level a spell rolls to hit with (SpellHandler.CalculateToHitChance): the spell's own for a player
        /// character in PvP, the caster's for a monster.</summary>
        public static int SpellLevelForToHit(bool casterTakesPlayerRules, int spellLevel, int casterLevel) =>
            casterTakesPlayerRules ? spellLevel : casterLevel;

        /// <summary>A bot's cast is checked for line of sight on the navmesh when nobody's client checks it: a bot caster
        /// in PvP on a target that is not the real player (whose client answers).</summary>
        public static bool BotCastNeedsNavmeshSight(bool casterTakesPlayerRules, bool targetIsRealPlayer, bool spellNeedsSight) =>
            casterTakesPlayerRules && !targetIsRealPlayer && spellNeedsSight;
    }
}
