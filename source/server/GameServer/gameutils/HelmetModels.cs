namespace DOL.GS
{
    /// <summary>
    /// Helmet models the client draws nothing for (owner 2026-10-19: model 822, Arcane Cloth Cap, the most common Albion
    /// cloth cap). Their objects.csv row in gamedata.mpk has no head mesh (Head # 0 and no alternates; the only mesh is the
    /// dropped-item model, often the loot bag), so a worn one is invisible in both clients. Such a helmet takes the realm's
    /// most-used visible helm of its armour type; random items never roll one (GeneratedUniqueItem), and
    /// tools/claude-version/fix_invisible_helmets.py swaps the stored items. tools/helmet_invisible_scan.py finds them.
    /// </summary>
    public static class HelmetModels
    {
        private static readonly System.Collections.Generic.HashSet<int> Invisible =
        [
            34,   // Leather Gloves
            35,   // Leather Cap
            488,  // All purpose bag
            493,  // Full helmet chain
            497,  // Bone
            499,  // Scroll
            511,  // Crown
            596,  // circlet
            822,  // Albion Cloth Cap
            1276, // Alb Plate 3 (Scale) Boots
            1839, // toa A Crownofzahur
            3861, // Dragonsworn Albion Plate Full Helm
            3865, // Dragonsworn Midgard Chain Full Helm
            3867, // Dragonsworn Hibernian Scale Full Helm
            4831, // Cairnwarden's Helm
        ];

        public static bool IsInvisible(int model) => Invisible.Contains(model);

        /// <summary>The model to use for a helmet: itself, or a visible stand-in when the client has no head mesh for it.</summary>
        public static int Visible(int model, eRealm realm, eObjectType type)
        {
            if (!Invisible.Contains(model))
                return model;
            return (realm, type) switch
            {
                (eRealm.Midgard, eObjectType.Leather) => 335,
                (eRealm.Midgard, eObjectType.Studded) => 829,
                (eRealm.Midgard, eObjectType.Chain) => 832,
                (eRealm.Midgard, _) => 825,
                (eRealm.Hibernia, eObjectType.Leather) => 438,
                (eRealm.Hibernia, eObjectType.Reinforced) => 835,
                (eRealm.Hibernia, eObjectType.Scale) => 839,
                (eRealm.Hibernia, _) => 826,
                (_, eObjectType.Leather) => 62,
                (_, eObjectType.Studded) => 824,
                (_, eObjectType.Chain) => 1236,
                (_, eObjectType.Plate) => 93,
                (_, eObjectType.Reinforced) => 835,
                (_, eObjectType.Scale) => 839,
                _ => 823, // Albion Cloth Helm
            };
        }
    }
}
