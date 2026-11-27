using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace DOL.GS
{
    /// <summary>
    /// 1.2.3 (#569 step 2). Port of zomboden/OfflineDAoC-True165 fb7487a (GPL-3.0, shadowofze; its item 16). Living Server's
    /// own death memory (AutonomousDeathBlame #537, AutonomousPveBossDanger, AutonomousRepeatDeathSpots) is solo-only:
    /// party camp planning skips it on purpose, and a party death only ever started a regroup or, after a wipe, the wipe
    /// penalty. This adds the party's counterpart.
    ///
    /// True165, 2026-11-11 overnight run: bot parties kept going back to places they could not win. Level 43-49 glimmer
    /// ardents and wards beside the Silvermine bindstone killed 331 members of level 12-19 parties in one night: the party
    /// regrouped at the bindstone, its regroup leader walked into the glimmers, died, released beside the same stone and the
    /// next leader tried again (one party 31 resurrection timeouts in 75 minutes). A party death only ever started a regroup;
    /// nothing remembered the place.
    ///
    /// Now a party that loses members to monsters far above them (<see cref="IsFarAboveParty"/>: beyond anything the party
    /// plans to fight) near the same spot - <see cref="DistinctMembersToRetreat"/> different members or
    /// <see cref="DeathsToRetreat"/> deaths in <see cref="WindowMilliseconds"/>, or a wipe with such a death in it - ends
    /// its task there. Its camp (when the deaths were at or near it) is benched for parties of that level band and below,
    /// and the death spot is remembered for the same band: party camp planning avoids both (True165 also keeps corpse walks off it;
    /// Living Server has no party corpse walk). Parties above
    /// the band are never blocked. Monsters, levels and rewards are unchanged.
    ///
    /// Cost: a list of at most a few deaths per party, touched only when a member dies; one dictionary read per camp
    /// candidate during party camp planning; the spot list is a small snapshot read without a lock.
    /// </summary>
    public static class AutonomousGroupDangerRetreat
    {
        public const int KillerLevelsAbove = 4;
        public const int DistinctMembersToRetreat = 2;
        public const int DeathsToRetreat = 3;
        public const long WindowMilliseconds = 10 * 60_000;
        /// <summary>Deaths this close to each other count as one place.</summary>
        public const int ClusterRadius = 4_000;
        /// <summary>A camp whose centre is this close to the deaths is the camp that is blamed.</summary>
        public const int CampBlameRadius = 6_000;
        /// <summary>Party camps this close to a remembered spot are avoided by the band it applies to.</summary>
        public const int SpotAvoidRadius = 4_000;
        public const long BenchMilliseconds = 2 * 60 * 60_000L;
        private const int MaxSpots = 256;

        public readonly record struct Death(long BotId, ushort Region, int X, int Y, int BotLevel, int KillerLevel, long Tick);

        /// <summary>A spot where a party of level <see cref="MaxBlockedLevel"/> or below should not go.</summary>
        public sealed record Spot(ushort Region, int X, int Y, int MaxBlockedLevel, long Until);

        private sealed record CampBench(int MaxBlockedLevel, long Until);

        private static readonly ConcurrentDictionary<string, CampBench> Camps = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object SpotSync = new();
        private static volatile Spot[] _spots = [];

        /// <summary>The killer is far above the member it killed.</summary>
        public static bool IsFarAbove(int killerLevel, int victimLevel) => killerLevel >= victimLevel + KillerLevelsAbove;

        /// <summary>
        /// A death that counts: the killer is far above the member it killed and <see cref="KillerLevelsAbove"/> levels
        /// beyond the hardest level this party plans to fight (its average plus its rolled camp bonus, at least 3; parties
        /// deliberately pull up to average + 10), so a lost pull at an ordinary camp - the wipe penalty's job - never counts.
        /// </summary>
        public static bool IsFarAboveParty(int killerLevel, int victimLevel, int partyAverageLevel, int plannedLevelBonus) =>
            IsFarAbove(killerLevel, victimLevel) &&
            killerLevel >= partyAverageLevel + Math.Max(3, plannedLevelBonus) + KillerLevelsAbove;

        /// <summary>
        /// Pure rule: the deaths (newest last) say the party should leave. Only far-above deaths in the window, clustered
        /// around the newest one, count. <paramref name="wipe"/>: every member is dead right now.
        /// </summary>
        public static bool ShouldRetreat(IReadOnlyList<Death> deaths, long now, bool wipe, out Death anchor)
        {
            anchor = default;
            if (deaths == null || deaths.Count == 0)
                return false;
            anchor = deaths[deaths.Count - 1];
            if (!IsFarAbove(anchor.KillerLevel, anchor.BotLevel) || now - anchor.Tick > WindowMilliseconds)
                return false;
            if (wipe)
                return true;
            int count = 0;
            HashSet<long> members = new();
            foreach (Death death in deaths)
            {
                if (now - death.Tick > WindowMilliseconds || !IsFarAbove(death.KillerLevel, death.BotLevel) ||
                    death.Region != anchor.Region || !Near(death.X, death.Y, anchor.X, anchor.Y, ClusterRadius))
                    continue;
                count++;
                members.Add(death.BotId);
            }
            return members.Count >= DistinctMembersToRetreat || count >= DeathsToRetreat;
        }

        /// <summary>
        /// The band a retreat blocks: the party's own average level, and anything the killers outclass by
        /// <see cref="KillerLevelsAbove"/> (so a level-45 killer blocks parties up to 41). Parties above it are free.
        /// </summary>
        public static int BlockedBand(int partyAverageLevel, int highestKillerLevel) =>
            Math.Max(partyAverageLevel, highestKillerLevel - KillerLevelsAbove);

        /// <summary>The highest killer level among the clustered far-above deaths in the window.</summary>
        public static int HighestKiller(IReadOnlyList<Death> deaths, Death anchor, long now)
        {
            int highest = anchor.KillerLevel;
            foreach (Death death in deaths)
                if (now - death.Tick <= WindowMilliseconds && death.Region == anchor.Region &&
                    Near(death.X, death.Y, anchor.X, anchor.Y, ClusterRadius) && IsFarAbove(death.KillerLevel, death.BotLevel))
                    highest = Math.Max(highest, death.KillerLevel);
            return highest;
        }

        /// <summary>Drops deaths that left the window; keeps the list short.</summary>
        public static void Prune(List<Death> deaths, long now)
        {
            deaths.RemoveAll(death => now - death.Tick > WindowMilliseconds);
            while (deaths.Count > 16)
                deaths.RemoveAt(0);
        }

        public static void BenchCamp(string campId, int maxBlockedLevel, long now)
        {
            if (string.IsNullOrWhiteSpace(campId))
                return;
            Camps.AddOrUpdate(campId, _ => new CampBench(maxBlockedLevel, now + BenchMilliseconds),
                (_, old) => new CampBench(old.Until > now ? Math.Max(old.MaxBlockedLevel, maxBlockedLevel) : maxBlockedLevel,
                    now + BenchMilliseconds));
        }

        public static bool IsCampBenchedFor(string campId, int partyLevel, long now) =>
            !string.IsNullOrWhiteSpace(campId) && Camps.TryGetValue(campId, out CampBench bench) &&
            bench.Until > now && partyLevel <= bench.MaxBlockedLevel;

        public static void MarkSpot(ushort region, int x, int y, int maxBlockedLevel, long now)
        {
            lock (SpotSync)
            {
                List<Spot> next = new(_spots.Length + 1);
                Spot merged = null;
                foreach (Spot spot in _spots)
                {
                    if (spot.Until <= now)
                        continue;
                    if (merged == null && spot.Region == region && Near(spot.X, spot.Y, x, y, ClusterRadius))
                        merged = spot;
                    else
                        next.Add(spot);
                }
                next.Add(new Spot(region, merged?.X ?? x, merged?.Y ?? y,
                    Math.Max(merged?.MaxBlockedLevel ?? 0, maxBlockedLevel), now + BenchMilliseconds));
                while (next.Count > MaxSpots)
                    next.RemoveAt(0);
                _spots = next.ToArray();
            }
        }

        /// <summary>True when (x, y) is within <paramref name="radius"/> of a live spot that blocks this level.</summary>
        public static bool NearSpot(ushort region, int x, int y, int level, long now, int radius = SpotAvoidRadius)
        {
            Spot[] spots = _spots;
            foreach (Spot spot in spots)
                if (spot.Until > now && spot.Region == region && level <= spot.MaxBlockedLevel && Near(spot.X, spot.Y, x, y, radius))
                    return true;
            return false;
        }

        /// <summary>Party camp planning: the camp is not benched for this band and is not beside a remembered spot.</summary>
        public static bool AllowsPartyCamp(string campId, ushort region, int x, int y, int partyLevel, long now) =>
            !IsCampBenchedFor(campId, partyLevel, now) && !NearSpot(region, x, y, partyLevel, now);

        public static void ResetForTests()
        {
            Camps.Clear();
            lock (SpotSync)
                _spots = [];
        }

        private static bool Near(int ax, int ay, int bx, int by, int radius)
        {
            double dx = ax - bx, dy = ay - by;
            return dx * dx + dy * dy <= (double)radius * radius;
        }
    }
}
