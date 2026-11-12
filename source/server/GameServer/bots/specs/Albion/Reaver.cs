namespace DOL.GS
{
    public class ReaverBotSpec : BotSpec
    {
        public ReaverBotSpec(eSpecType spec = eSpecType.None)
        {
            SpecName = "ReaverBotSpec";

            if (spec == eSpecType.SoulrendingReaver)
            {
                // Soulrending first with a flail and shield (owner 2026-10-29), on the same points as the others.
                SpecType = eSpecType.OneHandAndShield;
                WeaponOneType = eObjectType.Flexible;
                Add(Specs.Soulrending, 50, 1.0f);
                Add(Specs.Flexible, 42, 0.8f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 9, 0.1f);
                return;
            }

            int randBaseWeap = Util.Random(4);

            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.SlashingWeapon; break;
                case 1: WeaponOneType = eObjectType.ThrustWeapon; break;
                case 2: WeaponOneType = eObjectType.CrushingWeapon; break;
                case 3:
                case 4: WeaponOneType = eObjectType.Flexible; break;
            }

            int randVariance = Util.Random(2);

            switch (randVariance)
            {
                case 0:
                case 1:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Soulrending, 41, 1.0f);
                Add(Specs.Shields, 42, 0.5f);
                Add(Specs.Parry, 13, 0.1f);
                break;

                case 2:
                SpecType = eSpecType.OneHandAndShield;
                Add(ObjToSpec(WeaponOneType), 50, 0.8f);
                Add(Specs.Soulrending, 50, 1.0f);
                Add(Specs.Shields, 29, 0.4f);
                Add(Specs.Parry, 16, 0.1f);
                break;
            }
        }
    }
}
