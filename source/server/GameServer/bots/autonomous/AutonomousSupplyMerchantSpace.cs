using System;
using System.Numerics;

namespace DOL.GS
{
    public static class AutonomousSupplyMerchantSpace
    {
        public const int ClearRadius = 100;

        /// <summary>
        /// Interaction spots are resolved only this close to the merchant; farther away the bot walks toward it.
        /// Issue #25 (2026-10-27): since a distant warband walks to its medallion merchant (7ef2d66), each bot
        /// 20,000-110,000 units out ran up to 18 multi-zone HasRoute probes every 15 seconds, all failing, and
        /// held NpcService ticks at 1-1.3 s. Up close the probes are single-zone corridor checks.
        /// </summary>
        public const int SpotSearchRange = 2_000;

        /// <summary>A failed search (all 18 candidates) waits this long before the next one.</summary>
        public const int RetryAfterFailureMs = 30_000;

        /// <summary>Whether to look for an interaction spot now. Pure.</summary>
        public static bool ShouldResolveSpot(float distanceToMerchant) => distanceToMerchant <= SpotSearchRange;

        public static bool IsInteractionSpot(Vector3 center, Vector3 position, int interactionRadius)
        {
            return float.IsFinite(position.X) && float.IsFinite(position.Y) && float.IsFinite(position.Z) &&
                Vector2.DistanceSquared(new(center.X, center.Y), new(position.X, position.Y)) >= ClearRadius * ClearRadius &&
                Vector3.DistanceSquared(center, position) <= interactionRadius * interactionRadius;
        }

        public static Vector3 Offset(long id, int attempt, int interactionRadius)
        {
            float radius = Math.Min(interactionRadius - 32, 160 + attempt % 3 * 24);
            float angle = ((unchecked((ulong)id * 2654435761UL) % 360) + attempt * 137.50776f) * MathF.PI / 180;
            return new(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius, 0);
        }

        public static bool TryResolve(IPathfindingMgr nav, Region region, eRealm realm, Vector3 actor,
            Vector3 center, long botId, int interactionRadius, out Vector3 point)
        {
            point = default;
            Zone zone = region?.GetZone((int)center.X, (int)center.Y);
            if (zone == null || nav == null || !nav.IsAvailable || !nav.HasNavmesh(zone) || interactionRadius <= ClearRadius + 32) return false;
            for (int attempt = 0; attempt < 18; attempt++)
            {
                Vector3 desired = center + Offset(botId, attempt, interactionRadius);
                Vector3? floor = nav.GetClosestPoint(zone, desired, 24, 24, 128, nav.DefaultFilters);
                if (!floor.HasValue || !IsInteractionSpot(center, floor.Value, interactionRadius - 8) ||
                    !AutonomousRendezvousNavigation.HasLocalExit(nav, zone, floor.Value) ||
                    !AutonomousRvrRally.HasRoute(region, nav, realm, actor, floor.Value)) continue;
                point = floor.Value;
                return true;
            }
            return false;
        }
    }

    public sealed partial class AutonomousWorldBotController
    {
        private GameMerchant _supplySpaceMerchant;
        private Vector3 _supplySpaceCenter;
        private Vector3? _supplySpacePoint;
        private Zone _supplySpaceOriginZone;
        private long _nextSupplySpaceSearch;

        // Returns true only when a real transaction is in range and the bot
        // is clear of the NPC. Cached per visit; no population scan per AI turn.
        private bool ApproachSupplyMerchant(GameBot bot, GameMerchant merchant)
        {
            Vector3 center = new(merchant.X, merchant.Y, merchant.Z);
            int radius = ServerProperties.Properties.WORLD_PICKUP_DISTANCE;
            if (AutonomousSupplyMerchantSpace.IsInteractionSpot(center, new(bot.X, bot.Y, bot.Z), radius))
            {
                bot.StopMovingOnPath();
                bot.StopMoving();
                return true;
            }
            if (_supplySpaceMerchant != merchant || _supplySpaceCenter != center || _supplySpaceOriginZone != bot.CurrentZone)
            {
                _supplySpaceMerchant = merchant;
                _supplySpaceCenter = center;
                _supplySpaceOriginZone = bot.CurrentZone;
                _supplySpacePoint = null;
                _nextSupplySpaceSearch = 0;
            }
            int distance = bot.GetDistanceTo(merchant);
            if (!_supplySpacePoint.HasValue && AutonomousSupplyMerchantSpace.ShouldResolveSpot(distance) &&
                GameLoop.GameLoopTime >= _nextSupplySpaceSearch)
            {
                if (AutonomousSupplyMerchantSpace.TryResolve(PathfindingProvider.Instance, bot.CurrentRegion, bot.Realm,
                    new(bot.X, bot.Y, bot.Z), center, bot.DatabaseID, radius, out var point)) _supplySpacePoint = point;
                else
                    _nextSupplySpaceSearch = GameLoop.GameLoopTime + AutonomousSupplyMerchantSpace.RetryAfterFailureMs;
            }
            if (_supplySpacePoint.HasValue)
                IssuePath(bot, _supplySpacePoint.Value, preciseArrival: true);
            else if (!AutonomousSupplyMerchantSpace.ShouldResolveSpot(distance))
                IssuePath(bot, center); // no interaction spot resolved from far away yet: head toward the merchant, never stand still
            else
            {
                bot.StopMovingOnPath();
                bot.StopMoving();
            }
            return false;
        }
    }
}
