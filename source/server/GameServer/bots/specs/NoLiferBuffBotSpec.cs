namespace DOL.GS
{
    /// <summary>
    /// The No Lifers' buffbot builds (NoLiferBuffBots, Living Server 1.2): the buff line maxed, what is left in the healing
    /// line, a point in the third. Era evidence (docs/research/BUFFBOTS-2003.md): "2 extra buffbots both 50enh with MotA2"
    /// (Albion, Allakhazam, 2003-08-29) and "buffbot shamans with 47 aug" (Midgard, FreddysHouse, 2003-11-07); Hibernia's
    /// buffer Druids ran Nurture high (ERA-BUILDS-1.65.md, Nurture 44 / Regrowth 31, 2003), and a pure buffbot maxes it.
    /// </summary>
    public sealed class NoLiferBuffBotSpec : BotSpec
    {
        public NoLiferBuffBotSpec(eCharacterClass characterClass)
        {
            SpecName = "NoLiferBuffBotSpec";
            switch (characterClass)
            {
                case eCharacterClass.Cleric:
                    WeaponOneType = eObjectType.CrushingWeapon;
                    SpecType = eSpecType.EnhanceCleric;
                    Add(Specs.Enhancement, 50, 1.0f);
                    Add(Specs.Rejuvenation, 50, 0.3f);
                    Add(Specs.Smite, 1, 0.0f);
                    break;
                case eCharacterClass.Shaman:
                    WeaponOneType = eObjectType.Hammer;
                    SpecType = eSpecType.AugShaman;
                    Add(Specs.Augmentation, 50, 1.0f);
                    Add(Specs.Mending, 50, 0.3f);
                    Add(Specs.Subterranean, 1, 0.0f);
                    break;
                case eCharacterClass.Druid:
                    WeaponOneType = eObjectType.Blunt;
                    SpecType = eSpecType.NurtureDruid;
                    Add(Specs.Nurture, 50, 1.0f);
                    Add(Specs.Regrowth, 50, 0.3f);
                    Add(Specs.Nature, 1, 0.0f);
                    break;
            }
        }

        /// <summary>The buff build for a buffbot class; any other class keeps its ordinary spec.</summary>
        public static BotSpec For(eCharacterClass characterClass) =>
            NoLiferBuffBots.IsBuffBotClass(characterClass) ? new NoLiferBuffBotSpec(characterClass) : GetSpec(characterClass);

        /// <summary>The buff line of a buffbot class (null for any other class). Pure.</summary>
        public static string BuffLine(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Cleric => Specs.Enhancement,
            eCharacterClass.Shaman => Specs.Augmentation,
            eCharacterClass.Druid => Specs.Nurture,
            _ => null,
        };
    }
}
