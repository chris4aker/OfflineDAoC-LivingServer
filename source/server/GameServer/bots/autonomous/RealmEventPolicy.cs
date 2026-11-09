using System;

namespace DOL.GS
{
    /// <summary>Population-independent event timing. Actual world combat decides outcomes.</summary>
    public static class RealmEventPolicy
    {
        public const int DragonAttackDamageLimit = 600;

        // Include criticals and resistance debuffs; never raise weaker hits.
        public static void CapDragonDamage(AttackData attack)
        {
            if (attack == null) return;
            attack.Damage = Math.Clamp(attack.Damage, 0, DragonAttackDamageLimit);
            attack.CriticalDamage = Math.Clamp(attack.CriticalDamage, 0, DragonAttackDamageLimit - attack.Damage);
        }
        public const long EarliestAssaultMilliseconds = 3 * 60_000L;

        public static long RecruitmentMilliseconds(double roll) =>
            60 * 60_000L;

        public static bool CanRecruitRealm(bool defender, bool attackObserved, int attackers, int defenders, int cap) =>
            attackObserved || (defender ? attackers >= Math.Max(8, cap / 8) :
                attackers >= cap / 4 && defenders >= cap / 4);

        // Dynamic sieges (owner, 2026-10-07): defenders rally once the attack actually lands; a third realm
        // intervenes only sometimes, a few minutes into the battle.
        public const double ThirdRealmChance = 0.5;
        public const long ThirdRealmDelayMilliseconds = 5 * 60_000L;

        public static bool CanRespond(bool defender, bool attackObserved, bool battleStarted, bool thirdRealmIntervenes,
            long sinceBattleMilliseconds) =>
            defender ? attackObserved || battleStarted
                : thirdRealmIntervenes && battleStarted && sinceBattleMilliseconds >= ThirdRealmDelayMilliseconds;

        /// <summary>
        /// Dynamic siege sizes (owner, 2026-10-07 evening): a keep siege is up to 128 attackers against 64
        /// defenders, a relic siege 240 against 120. Attackers start with the edge (walls, doors and keep guards
        /// are the defenders' extra); the defenders gather as one quick rally and go in together instead of
        /// trickling. The third realm interferes half the time, with at most half the attackers (64 / 120).
        /// </summary>
        public const int KeepAttackers = 128;
        public const int RelicAttackers = 240;

        // 0.5 (owner, 2026-10-07) followed a run where defenders matched attackers and every siege failed. At 0.5 every
        // bot siege of 2026-10-19 fell (12-17 defenders against 42-53 attackers). The owner wants fights won about two
        // times in three, never guaranteed: 0.75 to start, then SiegeDefenseBalance steers by recent results.
        // New key: the stored rvr_siege_defense_ratio (0.5 on the owner's server since 2026-10-07) is no longer read, as
        // neutral_raid_encounter_level_cap replaced neutral_raid_encounter_level.
        [DOL.GS.ServerProperties.ServerProperty("autonomous", "rvr_siege_defense_share",
            "Bot defenders a siege may draw, as a share of its attacker cap (128 keep / 240 relic); walls, doors and keep guards are extra. Recent results move it up or down by up to 0.25 (SiegeDefenseBalance).", 0.75)]
        public static double DefenseRatio = 0.75;

        /// <summary>The ratio in use: the setting, steered by the last sieges' results.</summary>
        public static double EffectiveDefenseRatio => SiegeDefenseBalance.Ratio(DefenseRatio);

        public static int AttackerCap(bool relic) => relic ? RelicAttackers : KeepAttackers;
        /// <summary>The attackers' cap in use: the full cap, trimmed by the siege balance once the defenders' share is at its limit.</summary>
        public static int EffectiveAttackerCap(bool relic) => SiegeDefenseBalance.AttackerCap(AttackerCap(relic));
        /// <summary>
        /// Defenders a siege takes: the defense share of the attacking army, measured on its larger size, when it marched or
        /// now, at least 8 and at most the cap. 2026-10-19/20: the share was taken of the army that marched (10-60) while
        /// attacker reinforcements grew it to 70-105, so Dun Bolg's defense was capped at 25 against 89 and the attackers won
        /// every siege that finished.
        /// </summary>
        /// <summary>
        /// Home defense first (owner, 2026-10-20): an attacking warband breaks off to defend its own keep when the
        /// defense there is under half its cap, and only while the attack it leaves keeps at least half of the larger
        /// of its marched and current army.
        /// </summary>
        public static bool BreaksOffToDefend(int defenders, int defenderCap, int attackers, int marchedAttackers, int forceSize) =>
            defenders * 2 < defenderCap && attackers - forceSize >= (Math.Max(attackers, marchedAttackers) + 1) / 2;

        /// <summary>
        /// Whether a bot siege calls its keep's realm when the attacking army marches (scouts see it leave its border keep), not
        /// when it reaches the walls (1.1.3, AutonomousRvrEventLayer.CallDefendersAtMarch). A player's attack keeps its own
        /// immediate defense, and the losing realm's one retake keeps its rules; the legacy unscheduled sieges are unchanged.
        /// </summary>
        public static bool DefendersCalledAtMarch(bool siegeScheduling, bool defenseReaction, bool retake) =>
            siegeScheduling && !defenseReaction && !retake;

        /// <summary>No defense muster once the battle is on and attackers are at the keep (owner, 2026-10-20).</summary>
        public static bool DefendersSkipMuster(bool battleStarted, int attackersPresent) => battleStarted && attackersPresent > 0;

        public static int DefendersFor(int marchedAttackers, int currentAttackers, double ratio, int cap) =>
            Math.Clamp((int)Math.Round(Math.Max(marchedAttackers, currentAttackers) * Math.Clamp(ratio, 0.25, 2.0)), 8, Math.Max(8, cap));

        public static int DefenderCap(bool relic) => Math.Max(8, (int)Math.Round(AttackerCap(relic) * Math.Clamp(EffectiveDefenseRatio, 0.25, 2.0)));
        public static int ThirdRealmCap(bool relic) => AttackerCap(relic) / 2;

        /// <summary>Attackers needed to start the battle: 85% of the cap at the posts, half of it on a strike or at the deadline.</summary>
        public static int DynamicStartThreshold(bool relic, bool deadline) =>
            deadline ? AttackerCap(relic) / 2 : AttackerCap(relic) * 85 / 100;

        public static bool SiegeReady(bool relic, bool deadline, int attackers, int defenders) =>
            attackers >= (deadline ? relic ? 48 : 32 : relic ? 170 : 108);

        public static bool AttackersReady(long started, long now, int assigned, int present) =>
            now >= started && now - started >= EarliestAssaultMilliseconds &&
            assigned > 0 && present >= assigned;

        public static bool CanReact(bool actualAttackObserved) => actualAttackObserved;
    }
}
