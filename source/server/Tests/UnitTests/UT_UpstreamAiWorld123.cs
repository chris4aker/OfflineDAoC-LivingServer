using System;
using System.Numerics;
using DOL.Database;
using DOL.GS;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3 port package P4 (NPC AI, pathing, keeps and world) from OpenDAoC-Core and DOL: the pure rules each port added.
    /// Tests only; nothing here runs a world tick, a navmesh or a client.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_UpstreamAiWorld123
    {
        // ---- B4: NPCs finish what they started (cbe604ce1, 978b991fc), gamebots keep the player's checks ----

        [TestCase(true, false, true)]   // a monster, guard or pet
        [TestCase(true, true, false)]   // an autonomous gamebot or a companion: player rules
        [TestCase(false, false, false)] // the player
        public void FinishesStarted_OnlyNonBotNpcs(bool isNpc, bool isGameBot, bool expected)
        {
            Assert.That(NpcFinishRules.FinishesStarted(isNpc, isGameBot), Is.EqualTo(expected));
        }

        [Test]
        public void FinishRules_DefaultsAreLive()
        {
            Assert.That(Properties.CHECK_LOS_DURING_NPC_CAST, Is.False);
            Assert.That(Properties.CHECK_RANGE_AT_NPC_CAST_END, Is.False);
            Assert.That(Properties.CHECK_LOS_DURING_NPC_RANGED_ATTACK, Is.False);
            Assert.That(Properties.CHECK_RANGE_AT_NPC_RANGED_ATTACK_END, Is.False);
        }

        [Test]
        public void FinishRules_PlayerKeepsEveryCheck()
        {
            // A null living is neither an NPC nor a bot: the player's side, every check on.
            Assert.That(NpcFinishRules.ChecksRangeAtCastEnd(null), Is.True);
            Assert.That(NpcFinishRules.ChecksLosWhileAiming(null), Is.True);
            Assert.That(NpcFinishRules.ChecksRangeAtShotEnd(null), Is.True);
        }

        // ---- 678b8f16f: caster guards and immobile casters forget a target they can't see; bots keep their own handling ----

        [TestCase(false, true, true)]
        [TestCase(false, false, false)]
        [TestCase(true, true, false)]
        public void CasterGuard_ForgetsTargetOnNoLos(bool isGameBot, bool casterGuardOrImmobile, bool expected)
        {
            Assert.That(NpcCastingComponent.ForgetsTargetOnNoLos(isGameBot, casterGuardOrImmobile), Is.EqualTo(expected));
        }

        // ---- X1: navmesh line of sight only on one floor (wall archers keep their shots at the ground) ----

        [TestCase(100, 100, true)]
        [TestCase(100, 228, true)]
        [TestCase(100, 229, false)]  // a wall top above the yard
        [TestCase(500, 0, false)]
        public void NavmeshSight_OnlyOnOneFloor(float sourceZ, float targetZ, bool expected)
        {
            Assert.That(NavmeshSight.OnOneFloor(sourceZ, targetZ), Is.EqualTo(expected));
        }

        [Test]
        public void NavmeshSight_FailsOpenWithoutObjects()
        {
            Assert.That(NavmeshSight.Blocked(null, null), Is.False);
        }

        // ---- 2fecd64f0: door open requests ignored right after a close ----

        [Test]
        public void DoorCooldown_BorderKeepDoorsLonger()
        {
            Assert.That(GameDoor.StateChangeCooldown(true), Is.EqualTo(3000));
            Assert.That(GameDoor.StateChangeCooldown(false), Is.EqualTo(1000));
        }

        // ---- X3 (DOL 2239176): side doors move you 175 units ----

        [TestCase(1, 150)]
        [TestCase(2, 100)]
        [TestCase(3, 175)]
        [TestCase(6, 175)]
        [TestCase(7, 100)]
        public void KeepDoor_TraverseDistance(int doorIndex, int expected)
        {
            Assert.That(GameKeepDoor.TraverseDistance(doorIndex), Is.EqualTo(expected));
        }

        // ---- X2 (DOL 0554bfb0): players and bots above the reset wall height are set down ----

        [TestCase(100, 750, 500, 400, true)]
        [TestCase(800, 750, 500, 400, false)] // outside the keep
        [TestCase(100, 750, 300, 400, false)] // on the ground
        public void KeepReset_SetsDownWhoStandsAbove(int distance, int radius, int z, int wallZ, bool expected)
        {
            Assert.That(AbstractGameKeep.NeedsSettingDown(distance, radius, z, wallZ), Is.EqualTo(expected));
        }

        // ---- B12: saved gravestones and their decay (25c8e9c5e, e10348f31) ----

        [Test]
        public void Gravestone_DecaysAfterTheSetDays()
        {
            DateTime made = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            Assert.That(GameGravestone.HasDecayed(made, made.AddDays(6.9), 7), Is.False);
            Assert.That(GameGravestone.HasDecayed(made, made.AddDays(7), 7), Is.True);
            Assert.That(GameGravestone.HasDecayed(made, made.AddDays(400), 0), Is.False, "0 days: never");
            Assert.That(GameGravestone.HasDecayed(default, made, 7), Is.False, "no date (an older save): kept");
        }

        [Test]
        public void Gravestone_RestoredOnlyWithXpAndUndecayed()
        {
            DateTime now = new(2026, 11, 24, 0, 0, 0, DateTimeKind.Utc);
            Assert.That(GameGravestone.ShouldRestore(true, 1_000, now.AddDays(-1), now, 7), Is.True);
            Assert.That(GameGravestone.ShouldRestore(true, 0, now.AddDays(-1), now, 7), Is.False);
            Assert.That(GameGravestone.ShouldRestore(false, 1_000, now.AddDays(-1), now, 7), Is.False);
            Assert.That(GameGravestone.ShouldRestore(true, 1_000, now.AddDays(-8), now, 7), Is.False);
        }

        // ---- A8: the border crossing on the straight line (799b4f507), Zone.IsPointInZone (21443ebd6) ----

        [Test]
        public void ZoneBounds_AndBorderCrossing()
        {
            using EpicTestServerScope server = new();
            Region region = new(new RegionData { Id = 51, Name = "border test", Description = "border test", Mobs = [] });
            Zone west = new(region, 900, "west", 0, 0, 65536, 65536, 900, false, 0, false, 0, 0, 0, 0, 0);
            Zone east = new(region, 901, "east", 65536, 0, 65536, 65536, 901, false, 0, false, 0, 0, 0, 0, 0);
            region.Zones.Add(west);
            region.Zones.Add(east);

            Assert.That(west.IsPointInZone(65535, 100), Is.True);
            Assert.That(west.IsPointInZone(65536, 100), Is.False);
            Assert.That(region.GetZone(70000, 100), Is.SameAs(east));

            Pathfinder.FindBorderCrossing(west, new Vector3(60000, 1000, 0), new Vector3(70000, 1000, 0),
                out Vector3 inside, out Vector3 outside);
            Assert.That(west.IsPointInZone(inside), Is.True);
            Assert.That(east.IsPointInZone(outside), Is.True);
            Assert.That(inside.X, Is.EqualTo(65536 - 8).Within(1f));
            Assert.That(outside.X, Is.EqualTo(65536 + 8).Within(1f));
        }

        [Test]
        public void ZoneRules_GamebotsKeepTheirItinerary()
        {
            Assert.That(Pathfinder.UsesUpstreamLinkAndZoneRules(null), Is.True, "any NPC that isn't a gamebot");
        }
    }
}
