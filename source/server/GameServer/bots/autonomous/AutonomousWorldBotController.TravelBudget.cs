using System;
using System.Collections.Generic;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// 1.2.3 (#569 step 3). Port of zomboden/OfflineDAoC-True165 14dcaa5 (GPL-3.0, shadowofze): travel estimates for
    /// meetup planning and the PvE party camp travel budget (AutonomousGroupTravelBudget). Straight-line estimates over
    /// the existing zone-point crossings (FindNextCrossing); no navmesh query and no navmesh change.
    /// </summary>
    public sealed partial class AutonomousWorldBotController
    {
        /// <summary>
        /// EstimateTravelMinutes for many bots or targets: the next crossing is cached per (realm, from region, to
        /// region), so targets in different regions each get their own crossing (the older per-(realm, from) cache gave
        /// every later target region the first target's crossing). speed &gt; 0 replaces bot.MaxSpeed (meetup planning);
        /// perGoalCrossing skips the cache, for dungeon and Darkness Falls goals whose usable entrance depends on the goal
        /// point.
        /// </summary>
        public static double EstimateTravelMinutes(GameBot bot, ushort regionId, int x, int y,
            Dictionary<(eRealm Realm, ushort From, ushort To), DbZonePoint> crossings, double speed = 0,
            bool perGoalCrossing = false)
        {
            double unitsPerSecond = Math.Max(1d, speed > 0 ? speed : bot.MaxSpeed);
            if (bot.CurrentRegionID == regionId)
                return Distance(bot.X, bot.Y, x, y) / unitsPerSecond / 60d;
            DbZonePoint crossing = perGoalCrossing || crossings == null
                ? FindNextCrossing(bot.Realm, bot.CurrentRegionID, regionId, x, y)
                : AutonomousTravelCrossingCache.GetOrResolve(crossings, bot.Realm, bot.CurrentRegionID,
                    regionId, () => FindNextCrossing(bot.Realm, bot.CurrentRegionID, regionId, x, y));
            if (crossing == null)
                return AutonomousMeetupPlanPolicy.Unreachable;
            double firstLeg = Distance(bot.X, bot.Y, crossing.SourceX, crossing.SourceY) / unitsPerSecond / 60d;
            double finalBias = crossing.TargetRegion == regionId
                ? Distance(crossing.TargetX, crossing.TargetY, x, y) / unitsPerSecond / 60d
                : 6;
            return firstLeg + finalBias + 1;
        }

        /// <summary>
        /// A PvE party's camp candidates cut to those its leader can reach within its task time
        /// (AutonomousGroupTravelBudget). Runs after the quiet-camp filter; never empties the list.
        /// </summary>
        private AutonomousBotDecisionEngine.Camp[] BudgetGroupCamps(GameBot bot, AutonomousBotDecisionEngine.Camp[] camps,
            Dictionary<string, CampDestination> destinations)
        {
            if (camps.Length <= 1 || _groupDirective == null)
                return camps;
            GameBot leader = _groupDirective.Leader ?? bot;
            long remaining = _groupDirective.TaskRemainingMilliseconds(GameLoop.GameLoopTime);
            var crossings = new Dictionary<(eRealm Realm, ushort From, ushort To), DbZonePoint>();
            var estimates = new Dictionary<string, double>(StringComparer.Ordinal);
            bool anyDungeon = false, anyOutdoor = false;
            foreach (AutonomousBotDecisionEngine.Camp camp in camps)
            {
                anyDungeon |= camp.IsDungeon;
                anyOutdoor |= !camp.IsDungeon;
            }
            double Estimate(AutonomousBotDecisionEngine.Camp camp)
            {
                if (estimates.TryGetValue(camp.Id, out double known))
                    return known;
                double minutes = destinations.TryGetValue(camp.Id, out CampDestination d)
                    ? AutonomousGroupTravelBudget.EffectiveEstimate(
                        EstimateTravelMinutes(leader, d.RegionId, d.X, d.Y, crossings,
                            perGoalCrossing: camp.IsDungeon || d.RegionId == AutonomousDarknessFallsPolicy.RegionId),
                        AutonomousGroupTravelBudget.AppliesDungeonSurcharge(camp.IsDungeon, anyDungeon && anyOutdoor,
                            leader.CurrentRegionID == d.RegionId))
                    : AutonomousMeetupPlanPolicy.Unreachable;
                estimates[camp.Id] = minutes;
                return minutes;
            }
            AutonomousBotDecisionEngine.Camp[] kept = AutonomousGroupTravelBudget.Filter(camps, Estimate, remaining,
                out AutonomousGroupTravelBudget.Tier tier);
            if (kept.Length < camps.Length)
            {
                double nearest = double.MaxValue, farthestKept = 0;
                foreach (AutonomousBotDecisionEngine.Camp camp in camps)
                    nearest = Math.Min(nearest, Estimate(camp));
                foreach (AutonomousBotDecisionEngine.Camp camp in kept)
                    farthestKept = Math.Max(farthestKept, Estimate(camp));
                // A PvE party camp pick whose candidates were cut to the leader's travel budget, with the tier that was kept.
                Log.Info($"AUTONOMOUS_GROUP_CAMP_BUDGET group={_groupDirective.GroupId} phase=\"{_groupDirective.Phase}\" " +
                         $"region={leader.CurrentRegionID} remainingMin={remaining / 60_000d:0.0} " +
                         $"budgetMin={AutonomousGroupTravelBudget.BudgetMinutes(remaining):0.0} " +
                         $"candidates={camps.Length} kept={kept.Length} tier={tier} " +
                         $"nearestMin={nearest:0.0} farthestKeptMin={farthestKept:0.0}");
            }
            return kept;
        }
    }
}
