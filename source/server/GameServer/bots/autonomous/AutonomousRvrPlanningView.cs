using System;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS
{
    public sealed partial class AutonomousWorldBotController
    {
        // One planning view per NPC phase, not one global population regrouping
        // per warband. No combat target, inventory, purchase, or route is cached.
        private sealed record RvrPlanningView(GameBot[] Candidates, Dictionary<ushort, int[]> Population)
        {
            public (int Allies, int Enemies) Count(ushort region, eRealm realm)
            {
                if (!Population.TryGetValue(region, out int[] counts)) return default;
                int allies = (int)realm is >= 0 and < 4 ? counts[(int)realm] : 0;
                return (allies, counts[4] - allies);
            }
        }
        private static readonly Lock RvrPlanningLock = new();
        private static RvrPlanningView _rvrPlanningView;

        /// <summary>
        /// Whether a live gamebot is one the RvR planner may hunt (rvr-enemy-*): of a realm, on RvR duty, in the frontier, and
        /// not stealthed (#522; stealth.md finding 8). The planner sees every bot of the world, so without this a warband
        /// walked straight to the spot where a hidden stealther camped, with nobody having seen it. The population counts
        /// still include stealthers. Pure.
        /// </summary>
        public static bool IsRvrHuntCandidate(eRealm realm, bool stealthed, bool onRvrDuty, bool inFrontier) =>
            realm != eRealm.None && !stealthed && onRvrDuty && inFrontier;

        public static void PrepareRvrPlanningTick() => Volatile.Write(ref _rvrPlanningView, null);

        private static RvrPlanningView GetRvrPlanningView()
        {
            RvrPlanningView view = Volatile.Read(ref _rvrPlanningView);
            if (view != null) return view;
            lock (RvrPlanningLock)
            {
                if (_rvrPlanningView != null) return _rvrPlanningView;
                var population = new Dictionary<ushort, int[]>();
                var candidates = new List<GameBot>();
                foreach (GameBot actor in AutonomousBotRegistry.Snapshot())
                {
                    if (!actor.IsAlive) continue;
                    if (!population.TryGetValue(actor.CurrentRegionID, out int[] counts))
                        population[actor.CurrentRegionID] = counts = new int[5];
                    counts[4]++;
                    if ((int)actor.Realm is >= 0 and < 4) counts[(int)actor.Realm]++;
                    if (IsRvrHuntCandidate(actor.Realm, actor.IsStealthed,
                            AutonomousObjectiveAssignments.Is(actor, eAutonomousObjectiveKind.RvR), IsInFrontier(actor)))
                        candidates.Add(actor);
                }
                view = new(candidates.ToArray(), population);
                Volatile.Write(ref _rvrPlanningView, view);
                return view;
            }
        }
    }
}
