namespace DOL.GS
{
    /// <summary>
    /// Which realm a spot of the world belongs to, for the rule that a bot is never carried into another realm's territory
    /// (owner 2026-11-15: "No, bots should not follow a teleport into another realm"). The zones table leaves every zone's
    /// Realm at None, so ownership comes from the region and zone ids: a realm's classic home zones, its dungeons, capital,
    /// housing, Shrouded Isles and epic dungeons belong to it (AutonomousWorldBotController.ProtectedRealm plus the three
    /// housing regions). Frontier zones, Darkness Falls, the Old Frontiers dungeons, New Frontiers, battlegrounds and every
    /// other region belong to no realm.
    /// </summary>
    public static class RealmTerritory
    {
        public const ushort AlbionHousingRegion = 2, MidgardHousingRegion = 102, HibernianHousingRegion = 202;

        /// <summary>The realm whose home territory this is, or None (frontiers, battlegrounds, Darkness Falls, neutral). Pure.</summary>
        public static eRealm OwnerOf(ushort regionId, ushort zoneId) => regionId switch
        {
            AlbionHousingRegion => eRealm.Albion,
            MidgardHousingRegion => eRealm.Midgard,
            HibernianHousingRegion => eRealm.Hibernia,
            _ => AutonomousWorldBotController.ProtectedRealm(regionId, zoneId),
        };

        /// <summary>
        /// True when the spot is another realm's home territory for a bot of <paramref name="realm"/>. A bot without one of
        /// the three realms is never held back. Pure.
        /// </summary>
        public static bool IsEnemyRealmTerritory(eRealm realm, ushort regionId, ushort zoneId)
        {
            if (realm is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia))
                return false;
            eRealm owner = OwnerOf(regionId, zoneId);
            return owner != eRealm.None && owner != realm;
        }

        /// <summary>Whether <paramref name="bot"/> would be in another realm's territory where <paramref name="destination"/> stands.</summary>
        public static bool IsEnemyRealmTerritoryFor(GameLiving bot, GameObject destination) =>
            bot != null && destination != null &&
            IsEnemyRealmTerritory(bot.Realm, destination.CurrentRegionID, destination.CurrentZone?.ID ?? ushort.MaxValue);
    }
}
