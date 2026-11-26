// Hand port from zomboden/OfflineDAoC-True165 1add69478 (bots/BotHealTriage165.cs, GPL-3.0), reviewed to e2af3f2 (#567 B1-B5).
// Dropped: True 1.65's "Classic165Mode" gate and the companion "Heal at" choice; here one server setting,
// bot_healer_period_rules, turns the rules on (on by default), so an overnight log can be compared with it off.
using System;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #567 B1-B5 (owner decision 2026-11-26): period healer triage for bot healers. Prima (Healer tips, as quoted in True
    /// 1.65's source; not yet seen first-hand): "Over-healing your party paints a target on you. Try to keep everyone at
    /// two-thirds Health or better, and stick to Minor Heals instead of large Heals." Freddyshouse healer threads (2002-2003,
    /// as True 1.65 summarises them): small heals by default, big and instant heals for emergencies, no group heal on the pull,
    /// the main tank first.
    /// <list type="bullet">
    /// <item>B1: a dedicated healer starts healing at <see cref="HealAtPercent"/> (65%), not 80%.</item>
    /// <item>B2: a tank is triaged <see cref="TankBonusPercent"/> points lower than its health, never into the emergency band.</item>
    /// <item>B3: outside an emergency the efficient (small) heal comes before the big heal.</item>
    /// <item>B4: a heal over time goes on the tank below <see cref="PreHotBelowPercent"/> in a fight, before it needs a heal.</item>
    /// <item>B5: a group heal only with <see cref="GroupHealMembers"/> or more hurt, and not in a fight's first four seconds.</item>
    /// </list>
    /// Who: every bot whose healing goes through BotBrain.CheckHeals: autonomous gamebots, the player's own companion healers
    /// and /spawn helpers (owner 2026-11-26: companions too), and B2 for the player a companion assists. "Dedicated healer"
    /// is BotBrain.IsHealer: a healing class (BotPartyRoles.IsHealingClass: Cleric, Friar, Druid, Warden, Healer, Shaman,
    /// Paladin), or a Bard in a party's Healer slot (#567 B7). With the rules on, its line (65%) is the same as every other
    /// heal-capable bot's, so the two share one line.
    /// Where not (<see cref="Applies"/>): realm raids and warbands (shared support) and fights against a boss with a tactic keep
    /// their own healer rules (EpicEncounterTactics); the Green Knight's hold (#412) is checked before B4 anywhere. The
    /// relic-carrier rules and temporary companions' own target choice are unchanged.
    /// </summary>
    public static class BotHealTriage165
    {
        public const string PropertyKey = "bot_healer_period_rules";

        public const int EmergencyPercent = 40;
        public const int HealAtPercent = 65;
        /// <summary>Where a dedicated healer started before B1 (and still does with the setting off).</summary>
        public const int LegacyHealerAtPercent = 80;
        public const int TankBonusPercent = 10;
        public const int GroupHealMembers = 3;
        public const long GroupHealFightMilliseconds = 4_000;
        public const int PreHotBelowPercent = 90;
        public const int PreHotMinPowerPercent = 50;

        /// <summary>Whether the rules are on (the server setting; on by default).</summary>
        public static bool On => Properties.BOT_HEALER_PERIOD_RULES;

        /// <summary>Whether this heal check uses the rules: the setting is on, and the healer is in no raid or warband (shared
        /// support) and not targeting a boss with a tactic (owner 2026-11-26: "stay out of raids and boss fights"). Pure.</summary>
        public static bool Applies(bool on, bool raidSupport, bool bossFight) => on && !raidSupport && !bossFight;

        /// <summary>A heal CheckHeals may pick (BotBrain.HealSpellFor names the spell).</summary>
        public enum HealPick { None, Instant, InstantGroup, OverTime, Efficient, Big, Group }

        /// <summary>
        /// The emergency heal (someone under 40%), as CheckHeals chose it before the rules, with B5's gate on both group heals:
        /// an instant heal, else an instant group heal, else (no heal already casting) the big, the efficient, then the group
        /// heal. Pure.
        /// </summary>
        public static HealPick EmergencyPick(bool instantReady, bool instantGroupReady, bool castingHeal, bool bigReady,
            bool efficientReady, bool groupReady, bool groupHealOk)
        {
            if (instantReady) return HealPick.Instant;
            if (instantGroupReady && groupHealOk) return HealPick.InstantGroup;
            if (castingHeal) return HealPick.None;
            if (bigReady) return HealPick.Big;
            if (efficientReady) return HealPick.Efficient;
            return groupReady && groupHealOk ? HealPick.Group : HealPick.None;
        }

        /// <summary>
        /// The heal for a member under the line: a heal over time not yet running, then (B3, with the rules and no emergency)
        /// the efficient heal, then the big heal where it is worth it (the healer at 90% power and the wound at least its
        /// size), then the efficient heal, then the group heal (B5's gate). Pure.
        /// </summary>
        public static HealPick NonEmergencyPick(bool periodRules, int emergencies, bool overTimeReady, bool efficientReady,
            bool bigWorthIt, bool groupReady, bool groupHealOk)
        {
            if (overTimeReady) return HealPick.OverTime;
            if (periodRules && emergencies == 0 && efficientReady) return HealPick.Efficient;
            if (bigWorthIt) return HealPick.Big;
            if (efficientReady) return HealPick.Efficient;
            return groupReady && groupHealOk ? HealPick.Group : HealPick.None;
        }

        /// <summary>The health a dedicated healer starts healing a member at: 65% with the rules, 80% without. Pure.</summary>
        public static int HealerNeedAt(bool periodRules) => periodRules ? HealAtPercent : LegacyHealerAtPercent;

        /// <summary>The health a member is triaged at: a tank outside an emergency counts 10 points lower, never into one. Pure.</summary>
        public static int TriageHealth(int healthPercent, bool tank) =>
            tank && healthPercent >= EmergencyPercent
                ? Math.Max(EmergencyPercent, healthPercent - TankBonusPercent)
                : healthPercent;

        /// <summary>Whether the candidate is healed before the current pick (the lower triage health first). Pure.</summary>
        public static bool Prefer(int candidateHealth, bool candidateTank, int currentHealth, bool currentTank) =>
            TriageHealth(candidateHealth, candidateTank) < TriageHealth(currentHealth, currentTank);

        /// <summary>A group heal: three or more members under the heal line, and in a fight only once it has run 4 s. Pure.</summary>
        public static bool GroupHealWorthwhile(int membersHurt, bool inFight, long fightMilliseconds) =>
            membersHurt >= GroupHealMembers && (!inFight || fightMilliseconds >= GroupHealFightMilliseconds);

        /// <summary>
        /// B4: a heal over time on the tank as the sustained damage starts: in a fight, the tank fighting and hurt but not yet
        /// at the emergency line, none running or coming, the healer with power to spare. Pure.
        /// </summary>
        public static bool PreHotTank(bool groupInFight, bool tankFighting, int tankHealthPercent, bool hotRunningOrComing,
            int healerPowerPercent) =>
            groupInFight && tankFighting && tankHealthPercent < PreHotBelowPercent && tankHealthPercent >= EmergencyPercent &&
            !hotRunningOrComing && healerPowerPercent >= PreHotMinPowerPercent;

        /// <summary>The pre-HoT never moves or stops the healer: an instant one always, a cast-time one only on a healer standing free. Pure.</summary>
        public static bool PreHotHealerFree(bool instant, bool healerAttacking, bool healerMoving) =>
            instant || !healerAttacking && !healerMoving;

        /// <summary>A tank: a tank-class bot or player (BotPartyRoles.For gives Tank, the Paladin included).</summary>
        public static bool IsTank(GameLiving living) => living switch
        {
            GameBot bot => BotPartyRoles.IsTank(bot),
            GamePlayer player => player.CharacterClass != null &&
                                 BotPartyRoles.For((eCharacterClass)player.CharacterClass.ID) == BotPartyRole.Tank,
            _ => false,
        };
    }
}
