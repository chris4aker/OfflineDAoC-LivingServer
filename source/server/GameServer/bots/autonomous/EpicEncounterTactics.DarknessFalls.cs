using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Darkness Falls encounter rules: the pet-eating princes (wave 2, 1.2) and the parts of Legion's fight the bots play
    /// (wave 3, 1.2): no bladeturn near him, the wounded step out of his reach, and his throw to the platform takes bots
    /// too (they come back down by the recovery's recall). These are rules for named encounters, not a Darkness Falls raid
    /// AI: Legion's platform positions, a walked way down from the platform and add-wave assignments are not built.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        [DungeonRules]
        private static IEnumerable<DungeonRule> DarknessFallsRules() =>
        [
            new(nameof(PrinceAsmoien), [nameof(PrinceAsmoien), nameof(PrinceBaalorien)], PrincesEatPets)
            {
                Summary = "A raider with a pet keeps it 2,400 away from the princes (they eat pets to heal).",
            },
            new("Legion", ["Legion"], LegionWounded)
            {
                Summary = "A wounded raider (under 35%) who isn't his target steps out of his 1,400 reach until it is healed " +
                          "(each raider he kills heals him 2.5%); no bot casts a bladeturn within 2,500 of him while he fights " +
                          "(he eats the bubble of his target, and five of them set off his 1,000-radius soul blast).",
            },
        ];

        // ---- Legion ---------------------------------------------------------------------------------------------------

        /// <summary>A raider under this health who isn't Legion's target steps out of his reach.</summary>
        public const int LegionWoundedPercent = 35;
        /// <summary>Where it waits: past his close range (1,400, Legion.TakeDamage) and his blast (1,000 around his target).</summary>
        public const int LegionWoundedStandOff = 1_700;
        /// <summary>No bot casts a bladeturn within this of a fighting Legion.</summary>
        public const int LegionBubbleRadius = 2_500;
        public const ushort DarknessFallsRegion = 249;

        private static Advice? LegionWounded(RuleContext c) =>
            LegionWoundedAdvice(c.At, c.Raider.HealthPercent, c.Target.TargetObject == c.Raider, c.Target);

        /// <summary>
        /// Legion heals 2.5% of his health by each raider he kills (EnemyKilled; flood guard Legion.KillHeal) and his adds and
        /// blast finish the wounded. A raider under <see cref="LegionWoundedPercent"/> who is not the one he fights steps out
        /// to <see cref="LegionWoundedStandOff"/> on its side and comes back once its healers have it up again; the healers
        /// near him take anyone under half first (KillHealBosses). His target stays: running would drag him onto the raid.
        /// </summary>
        public static Advice? LegionWoundedAdvice(Vector3 raider, int healthPercent, bool isHisTarget, GameNPC legion)
        {
            if (isHisTarget || healthPercent >= LegionWoundedPercent) return null;
            Vector3 boss = At(legion);
            if (Flat(raider, boss) >= LegionWoundedStandOff - 100) return new(BotAction.StandOff, legion, null,
                "legion-wounded: out of his reach until healed (each death heals him)");
            return new(BotAction.StandOff, legion, PointAway(raider, boss, LegionWoundedStandOff),
                "legion-wounded: out of his reach until healed (each death heals him)", "epic_legion_wounded");
        }

        private static DOL.GS.Scripts.Legion _legion;
        private static long _legionLookedUp;

        /// <summary>The live Legion of Darkness Falls (looked up at most every 5 s), or null.</summary>
        private static DOL.GS.Scripts.Legion LiveLegion(long now)
        {
            if (now - _legionLookedUp < 5_000 && (_legion == null || _legion.IsAlive && _legion.ObjectState == GameObject.eObjectState.Active))
                return _legion;
            _legionLookedUp = now;
            _legion = WorldMgr.GetRegion(DarknessFallsRegion)?.Objects.OfType<DOL.GS.Scripts.Legion>()
                .FirstOrDefault(l => l.IsAlive && l.ObjectState == GameObject.eObjectState.Active);
            return _legion;
        }

        /// <summary>No bladeturn here (pure): within <see cref="LegionBubbleRadius"/> of a Legion who fights.</summary>
        public static bool BladeturnFeedsLegion(GameLiving caster, GameNPC legion) =>
            caster != null && legion is { IsAlive: true, InCombat: true } && caster.CurrentRegionID == legion.CurrentRegionID &&
            caster.IsWithinRadius(legion, LegionBubbleRadius);

        /// <summary>
        /// A bot must not cast a bladeturn now (BotBrain's buffs, group bubbles and pulsing chants ask it): Legion fights within
        /// <see cref="LegionBubbleRadius"/>. He ends the bladeturn on his target on every turn, and after five of them he
        /// casts his soul blast (1,000 body damage to everyone within 1,000 of his target, a quarter of it healing him).
        /// </summary>
        public static bool NoBladeturnHere(GameLiving caster)
        {
            if (caster == null || caster.CurrentRegionID != DarknessFallsRegion) return false;
            DOL.GS.Scripts.Legion legion;
            try { legion = LiveLegion(GameLoop.GameLoopTime); }
            catch (System.Exception) { return false; }
            return BladeturnFeedsLegion(caster, legion);
        }

        /// <summary>Princes Asmoien and Ba'alorien eat the nearest summoned pet within 2,000 every 5 s and heal by its
        /// health; a raider with a pet keeps it (it follows its owner) this far away.</summary>
        public const int PrincePetSafeDistance = 2_400;

        /// <summary>The raider's own summoned pet that a prince would eat, or null.</summary>
        public static GameNPC PetOf(GameLiving raider) =>
            raider?.ControlledBrain?.Body is GameNPC pet && EpicEncounterMechanics.IsDevourablePet(pet) ? pet : null;

        /// <summary>
        /// A raider whose pet a prince would eat (and heal by) stays out of the fight with it: it drops the prince, calls its
        /// pet off (TryHandle) and waits 2,400 away, so the pet that follows it is out of his 2,000 reach. Raiders without a
        /// pet fight on. (Only real summoned pets are eaten, never a bot: EpicEncounterMechanics.IsDevourablePet.)
        /// </summary>
        private static Advice? PrincesEatPets(RuleContext c) => PrincePetAdvice(At(c.Raider), c.Target, PetOf(c.Raider) != null);

        public static Advice? PrincePetAdvice(Vector3 raider, GameNPC prince, bool hasPet)
        {
            if (!hasPet) return null;
            return new Advice(BotAction.StandOff, prince,
                Flat(raider, At(prince)) < PrincePetSafeDistance - 100 ? PointAway(raider, At(prince), PrincePetSafeDistance) : null,
                "prince-pets: he eats pets and heals by them; keep yours out of his reach");
        }
    }
}
