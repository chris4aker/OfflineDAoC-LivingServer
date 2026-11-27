using System;
using System.Collections.Generic;
using System.Threading;
using DOL.GS;
using DOL.GS.Keeps;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #572 (port of zomboden/OfflineDAoC-True165 <c>16284ba</c>, bots/BotBrain.RvrSupport.cs, GPL-3.0): the RvR healer's
    /// support target and the relic-carrier list, cached. Before it, every heal check of every autonomous gamebot healer on
    /// an RvR objective ran a 2,000-unit area search, a navmesh line-of-sight raycast for each keep guard in it and a sort,
    /// and walked the relic list with LINQ twice: thousands of raycasts a second at a keep fight. Now:
    /// - the relic carriers are read at most every 2 s for the whole server (every bot's heal check reads them);
    /// - a healer keeps its chosen support target for 2 s, re-checking only that it is alive, of its realm and region, in
    ///   heal range and under 85%; "nobody needs it" is kept 2 s too;
    /// - a rebuild ranks the candidates first (relic carrier, keep lord, other guards; then the lowest health) and raycasts
    ///   only the best one, moving to the next only when that one has no line of sight.
    /// The choice itself is the one the old query made. Companions and players' groups never reach the support block.
    /// </summary>
    public partial class BotBrain
    {
        public const long RvrSupportHoldMilliseconds = 2_000;
        public const int RvrSupportRadius = 2_000;
        public const int RvrSupportBelowPercent = 85;
        public const long RelicCarrierRefreshMilliseconds = 2_000;

        private static GameLiving[] _relicCarriers = [];
        private static long _relicCarriersUntil = long.MinValue;
        private static readonly object RelicCarriersLock = new();

        private GameLiving _rvrSupportTarget;
        private long _rvrSupportUntil = long.MinValue;

        /// <summary>The living relic carriers, refreshed at most every 2 s for the whole server.</summary>
        internal static GameLiving[] RelicCarriers()
        {
            long now = GameLoop.GameLoopTime;
            if (now < Volatile.Read(ref _relicCarriersUntil))
                return Volatile.Read(ref _relicCarriers);
            lock (RelicCarriersLock)
            {
                if (now < _relicCarriersUntil)
                    return _relicCarriers;
                var carriers = new List<GameLiving>(2);
                foreach (GameRelic relic in RelicMgr.GetRelics() ?? [])
                    if (relic?.CurrentCarrier is GameLiving carrier && carrier.IsAlive)
                        carriers.Add(carrier);
                Volatile.Write(ref _relicCarriers, carriers.ToArray());
                Volatile.Write(ref _relicCarriersUntil, now + RelicCarrierRefreshMilliseconds);
                return _relicCarriers;
            }
        }

        /// <summary>Pure: a support candidate's rank, lower first: a relic carrier, then a keep lord, then other guards.</summary>
        public static int RvrSupportRank(bool carriesRelic, bool keepLord) => carriesRelic ? 0 : keepLord ? 1 : 2;

        /// <summary>Pure: the held support target is kept (no new search) while its hold lasts and it still needs it.</summary>
        public static bool KeepsRvrSupport(long now, long holdUntil, bool alive, bool inRange, int healthPercent) =>
            now < holdUntil && alive && inRange && healthPercent < RvrSupportBelowPercent;

        /// <summary>Pure: the hold has run out (a held "nobody" counts as held until then).</summary>
        public static bool RvrSupportHoldOver(long now, long holdUntil) => now >= holdUntil;

        /// <summary>
        /// Pure: the first candidate in rank order (rank, then lowest health) that <paramref name="visible"/> passes. Only
        /// the candidates up to that one are raycast; the old query raycast every one before sorting.
        /// </summary>
        public static T PickRvrSupport<T>(List<(T Living, int Rank, int Health)> candidates, Func<T, bool> visible) where T : class
        {
            candidates.Sort((a, b) => a.Rank != b.Rank ? a.Rank.CompareTo(b.Rank) : a.Health.CompareTo(b.Health));
            foreach (var candidate in candidates)
                if (visible(candidate.Living))
                    return candidate.Living;
            return null;
        }

        private bool RvrSupportCandidate(GameLiving living) =>
            living?.IsAlive == true && living.Realm == Body.Realm && living.HealthPercent < RvrSupportBelowPercent &&
            living.CurrentRegion == Body.CurrentRegion && CanHealRelicCarrier(living);

        /// <summary>The RvR healer's support target (a relic carrier or keep guard of its realm under 85%), or null.</summary>
        private GameLiving RvrSupportTarget()
        {
            long now = GameLoop.GameLoopTime;
            GameLiving held = _rvrSupportTarget;
            if (held != null)
            {
                if (KeepsRvrSupport(now, _rvrSupportUntil, held.IsAlive && held.Realm == Body.Realm &&
                        held.CurrentRegion == Body.CurrentRegion, CanHealRelicCarrier(held), held.HealthPercent))
                    return held;
            }
            else if (!RvrSupportHoldOver(now, _rvrSupportUntil))
                return null;

            var candidates = new List<(GameLiving Living, int Rank, int Health)>();
            foreach (GameLiving carrier in RelicCarriers())
                if (RvrSupportCandidate(carrier))
                    candidates.Add((carrier, RvrSupportRank(GameRelic.IsPlayerCarryingRelic(carrier), false), carrier.HealthPercent));
            foreach (GameNPC npc in Body.GetNPCsInRadius(RvrSupportRadius))
                if (npc is GameKeepGuard guard && RvrSupportCandidate(guard))
                    candidates.Add((guard, RvrSupportRank(GameRelic.IsPlayerCarryingRelic(guard), guard is GuardLord), guard.HealthPercent));

            var nav = PathfindingProvider.Instance;
            GameLiving chosen = PickRvrSupport(candidates, living =>
                nav.HasLineOfSight(Body.CurrentZone, new(Body.X, Body.Y, Body.Z), new(living.X, living.Y, living.Z), nav.DefaultFilters));
            _rvrSupportTarget = chosen;
            _rvrSupportUntil = now + RvrSupportHoldMilliseconds;
            return chosen;
        }
    }
}
