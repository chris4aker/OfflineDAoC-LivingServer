using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Darkness Falls encounter rules (wave 2, 1.2): the pet-eating princes. This is one rule for two named encounters, not
    /// a Darkness Falls raid AI.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
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
        [EncounterRule(typeof(PrinceAsmoien), typeof(PrinceBaalorien))]
        private static Advice? PrincesEatPets(RuleContext c) => PrincePetAdvice(At(c.Raider), c.Target, PetOf(c.Raider) != null);

        public static Advice? PrincePetAdvice(Vector3 raider, GameNPC prince, bool hasPet)
        {
            if (!hasPet) return null;
            return new Advice(BotAction.StandOff, prince,
                Flat(raider, At(prince)) < PrincePetSafeDistance - 100 ? AwayFrom(raider, At(prince), PrincePetSafeDistance) : null,
                "prince-pets: he eats pets and heals by them; keep yours out of his reach");
        }
    }
}
