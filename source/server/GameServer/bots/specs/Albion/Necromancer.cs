namespace DOL.GS
{
    public class NecromancerBotSpec : BotSpec
    {
        public NecromancerBotSpec(eSpecType spec)
        {
            SpecName = "NecromancerBotSpec";
            WeaponOneType = eObjectType.Staff;
            Is2H = true;

            if (spec == eSpecType.DeathServantNecro)
            {
                // Death Servant build (owner 2026-10-29): the servant's own line first (its taunts, damage adds and
                // heals), Deathsight second, on the same points as the other two builds.
                SpecType = eSpecType.DeathServantNecro;
                Add(Specs.Death_Servant, 49, 1.0f);
                Add(Specs.Deathsight, 22, 0.2f);
                return;
            }

            bool deathsight = spec != eSpecType.PainworkingNecro;
            SpecType = deathsight ? eSpecType.DeathsightNecro : eSpecType.PainworkingNecro;

            if (deathsight)
            {
                Add(Specs.Deathsight, 49, 1.0f);
                Add(Specs.Painworking, 22, 0.2f);
            }
            else
            {
                Add(Specs.Deathsight, 22, 0.2f);
                Add(Specs.Painworking, 49, 1.0f);
            }
        }
    }
}
