namespace DOL.GS
{
    public class BardBotSpec : BotSpec
    {
        public BardBotSpec(eSpecType spec = eSpecType.None)
        {
            SpecName = "BardBotSpec";

            if (spec == eSpecType.RegrowthBard)
            {
                // Regrowth first, the healing Bard (owner 2026-10-29), on the same points as the others.
                WeaponOneType = Util.Random(1) == 0 ? eObjectType.Blades : eObjectType.Blunt;
                SpecType = eSpecType.Instrument;
                Add(Specs.Regrowth, 47, 1.0f);
                Add(Specs.Nurture, 38, 0.8f);
                Add(Specs.Music, 22, 0.3f);
                return;
            }

            int randBaseWeap = Util.Random(1);

            switch (randBaseWeap)
            {
                case 0: WeaponOneType = eObjectType.Blades; break;
                case 1: WeaponOneType = eObjectType.Blunt; break;
            }

            int randVariance = Util.Random(2);

            SpecType = eSpecType.Instrument;

            switch (randVariance)
            {
                case 0:
                Add(Specs.Music, 47, 0.8f);
                Add(Specs.Nurture, 43, 0.9f);
                Add(Specs.Regrowth, 16, 0.1f);
                break;

                case 1:
                Add(Specs.Music, 37, 0.4f);
                Add(Specs.Nurture, 43, 0.9f);
                Add(Specs.Regrowth, 33, 0.7f);
                break;

                case 2:
                Add(ObjToSpec(WeaponOneType), 29, 0.7f);
                Add(Specs.Music, 37, 0.4f);
                Add(Specs.Nurture, 43, 0.9f);
                Add(Specs.Regrowth, 16, 0.1f);
                break;
            }
        }
    }
}
