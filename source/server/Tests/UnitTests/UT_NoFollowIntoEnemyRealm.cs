using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Owner 2026-11-15 (build 1dc66b6): a GM /jump into Midgard's Tuscaran Glacier carried an Albion bot-led party into
    /// Midgard's Kvasir raid. "No, bots should not follow a teleport into another realm." RealmTerritory says where that is.
    /// </summary>
    [TestFixture]
    public class UT_NoFollowIntoEnemyRealm
    {
        private const ushort AnyZone = ushort.MaxValue;

        [Test]
        public void TheOwnersJumpIntoTuscaranGlacierIsMidgardTerritoryForAnAlbionBot()
        {
            Assert.That(RealmTerritory.OwnerOf(160, 160), Is.EqualTo(eRealm.Midgard));
            Assert.That(RealmTerritory.IsEnemyRealmTerritory(eRealm.Albion, 160, 160), Is.True);
            Assert.That(RealmTerritory.IsEnemyRealmTerritory(eRealm.Hibernia, 160, 160), Is.True);
            Assert.That(RealmTerritory.IsEnemyRealmTerritory(eRealm.Midgard, 160, 160), Is.False, "Midgard's own bots still follow");
        }

        [TestCase((ushort)100, (ushort)100, eRealm.Midgard, TestName = "Vale of Mularn")]
        [TestCase((ushort)100, (ushort)116, eRealm.Midgard, TestName = "Malmohus")]
        [TestCase((ushort)101, (ushort)120, eRealm.Midgard, TestName = "Jordheim")]
        [TestCase((ushort)102, (ushort)114, eRealm.Midgard, TestName = "Midgard housing")]
        [TestCase((ushort)125, (ushort)125, eRealm.Midgard, TestName = "Spindelhalla")]
        [TestCase((ushort)151, (ushort)151, eRealm.Midgard, TestName = "Aegir")]
        [TestCase((ushort)161, (ushort)161, eRealm.Midgard, TestName = "Iarnvidiur's Lair")]
        [TestCase((ushort)1, (ushort)0, eRealm.Albion, TestName = "Camelot Hills")]
        [TestCase((ushort)2, (ushort)13, eRealm.Albion, TestName = "Albion housing")]
        [TestCase((ushort)10, (ushort)26, eRealm.Albion, TestName = "Camelot City")]
        [TestCase((ushort)51, (ushort)51, eRealm.Albion, TestName = "Avalon")]
        [TestCase((ushort)60, (ushort)60, eRealm.Albion, TestName = "Caer Sidi")]
        [TestCase((ushort)200, (ushort)200, eRealm.Hibernia, TestName = "Lough Derg")]
        [TestCase((ushort)201, (ushort)209, eRealm.Hibernia, TestName = "Tir na Nog")]
        [TestCase((ushort)202, (ushort)213, eRealm.Hibernia, TestName = "Hibernia housing")]
        [TestCase((ushort)181, (ushort)181, eRealm.Hibernia, TestName = "Hy Brasil")]
        [TestCase((ushort)191, (ushort)191, eRealm.Hibernia, TestName = "Galladoria")]
        [TestCase((ushort)221, (ushort)221, eRealm.Hibernia, TestName = "Muire Tomb")]
        public void ARealmsHomeZonesDungeonsCitiesAndEpicDungeonsAreItsTerritory(ushort region, ushort zone, eRealm owner)
        {
            Assert.That(RealmTerritory.OwnerOf(region, zone), Is.EqualTo(owner));
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                Assert.That(RealmTerritory.IsEnemyRealmTerritory(realm, region, zone), Is.EqualTo(realm != owner), realm.ToString());
        }

        [TestCase((ushort)1, (ushort)11, TestName = "Forest Sauvage")]
        [TestCase((ushort)1, (ushort)15, TestName = "Hadrian's Wall")]
        [TestCase((ushort)100, (ushort)111, TestName = "Uppland")]
        [TestCase((ushort)100, (ushort)115, TestName = "Odin's Gate")]
        [TestCase((ushort)200, (ushort)210, TestName = "Mount Collory")]
        [TestCase((ushort)200, (ushort)214, TestName = "Emain Macha")]
        [TestCase((ushort)163, (ushort)163, TestName = "New Frontiers")]
        [TestCase((ushort)249, (ushort)249, TestName = "Darkness Falls")]
        [TestCase((ushort)246, (ushort)246, TestName = "Dodens Gruva")]
        [TestCase((ushort)248, (ushort)248, TestName = "Summoner's Hall")]
        [TestCase((ushort)276, (ushort)276, TestName = "Marfach Caverns")]
        [TestCase((ushort)277, (ushort)277, TestName = "Hall of the Corrupt")]
        [TestCase((ushort)234, (ushort)234, TestName = "The Proving Grounds")]
        [TestCase((ushort)238, (ushort)238, TestName = "Thidranki battleground")]
        [TestCase((ushort)242, (ushort)254, TestName = "Leirvik")]
        [TestCase((ushort)252, (ushort)252, TestName = "Old Thidranki")]
        [TestCase((ushort)30, (ushort)30, TestName = "Atlantis")]
        [TestCase((ushort)27, (ushort)27, TestName = "Tutorial")]
        public void FrontiersBattlegroundsDarknessFallsAndNeutralZonesBelongToNoRealm(ushort region, ushort zone)
        {
            Assert.That(RealmTerritory.OwnerOf(region, zone), Is.EqualTo(eRealm.None));
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                Assert.That(RealmTerritory.IsEnemyRealmTerritory(realm, region, zone), Is.False, realm.ToString());
        }

        [Test]
        public void AnUnknownZoneOfAHomeRegionCountsAsTheRealmsOwn()
        {
            // No zone (a spot not yet resolved) in a home region is the realm's, not a frontier.
            Assert.That(RealmTerritory.IsEnemyRealmTerritory(eRealm.Albion, 100, AnyZone), Is.True);
            Assert.That(RealmTerritory.IsEnemyRealmTerritory(eRealm.Midgard, 1, AnyZone), Is.True);
            Assert.That(RealmTerritory.IsEnemyRealmTerritory(eRealm.Hibernia, 200, AnyZone), Is.False);
        }

        [Test]
        public void ABotWithoutARealmIsNeverHeldBack()
        {
            Assert.That(RealmTerritory.IsEnemyRealmTerritory(eRealm.None, 160, 160), Is.False);
            Assert.That(RealmTerritory.IsEnemyRealmTerritory(eRealm.Door, 10, 26), Is.False);
            Assert.That(RealmTerritory.IsEnemyRealmTerritoryFor(null, null), Is.False);
        }
    }
}
