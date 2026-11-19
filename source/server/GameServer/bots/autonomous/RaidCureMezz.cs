using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS
{
    /// <summary>
    /// A raid's mesmerized are cured across its parties (Living Server 1.2). A bot cured only its own party's mesmerized,
    /// and only when nobody there needed a heal. Play test duo34 (2026-11-11, 128 Hibernians at Spindler Broodmother, who
    /// mesmerizes everyone within 300 of a random raider for 60 seconds about every half minute): 95 to 105 of the raid
    /// stood mesmerized on average through the second, third and fourth minute, her health moved 65% to 61% in those
    /// minutes, and the raid wiped at 57% and 52%. Bards and Mentalists (Hibernia), Minstrels and Sorcerers (Albion),
    /// Healers and Spiritmasters (Midgard) have the cure: 1,500 range, 3 seconds.
    /// </summary>
    public static class RaidCureMezz
    {
        /// <summary>A mesmerized raider a curer could reach.</summary>
        public readonly record struct Mezzed(long Id, bool CanCure, bool Healer, bool Tank);

        /// <summary>Whom to cure first: one who can cure in turn (each cured curer frees others), then a healer, then a
        /// tank, then anyone.</summary>
        public static int Rank(Mezzed m) => m.CanCure ? 0 : m.Healer ? 1 : m.Tank ? 2 : 3;

        /// <summary>
        /// The mesmerized raider this curer takes (pure), or -1 when there is none: from the most urgent kind present
        /// (<see cref="Rank"/>), a different one for each curer as far as there are enough (by the curer's id), so that
        /// sixteen curers do not all cast on the same raider.
        /// </summary>
        public static int Choose(IReadOnlyList<Mezzed> mezzed, long curerId) => Choose(mezzed, curerId, null);

        /// <summary>
        /// As <see cref="Choose(IReadOnlyList{Mezzed}, long)"/>, leaving out every raider another curer has claimed
        /// (<paramref name="claimed"/>: <see cref="RaidCureClaims.IsClaimed"/>); -1 when all are claimed, so the bot heals or
        /// cures someone else this turn. Owner's PC, build 9a173f4, 2026-11-19 (sweep 7c, H-1): at 02:21:52 twelve Midgard
        /// Healers cast Cleanse Mind on Captainfluffy, the one mesmerized raider in their reach, in the same second, and 263
        /// of the night's 1,121 RAID_CURE_MEZZ lines (23%) were a second or later curer on the same raider in the same second.
        /// </summary>
        public static int Choose(IReadOnlyList<Mezzed> mezzed, long curerId, Func<long, bool> claimed)
        {
            if (mezzed == null || mezzed.Count == 0) return -1;
            int[] open = Enumerable.Range(0, mezzed.Count).Where(i => claimed?.Invoke(mezzed[i].Id) != true).ToArray();
            if (open.Length == 0) return -1;
            int best = open.Min(i => Rank(mezzed[i]));
            int[] urgent = open.Where(i => Rank(mezzed[i]) == best).OrderBy(i => mezzed[i].Id).ToArray();
            return urgent[(int)(Math.Abs(curerId) % urgent.Length)];
        }

        /// <summary>Whether the raid cure comes before this turn's ordinary heal (pure): always, unless a member of the
        /// curer's own party is about to die.</summary>
        public static bool ComesBeforeHealing(int partyEmergencies) => partyEmergencies <= 0;
    }

    /// <summary>
    /// Which raider a raid curer is curing right now (Living Server 1.2, sweep 7c, H-1; tests only): the curer that takes a
    /// raider claims him for the cure's cast time plus <see cref="Slack"/>, and the raid's other curers of the same kind
    /// (<see cref="Kind"/>) leave him out and heal or cure someone else. A claim ends on its own; a curer whose cast does not
    /// start releases it. Shared by every raid (ids are object ids, unique in the world). No spell or boss changes.
    /// </summary>
    public static class RaidCureClaims
    {
        public enum Kind { Mezz, Poison }

        /// <summary>A claim outlasts the cure's cast time by this much (the cure lands, the target's state updates).</summary>
        public const int Slack = 500;

        private readonly record struct Claim(long Curer, long Until);

        private static readonly ConcurrentDictionary<(Kind, long), Claim> Claims = new();
        private static long _nextPrune;

        /// <summary>How long a claim lasts for a cure of <paramref name="castTimeMs"/> (pure).</summary>
        public static long Duration(int castTimeMs) => Math.Max(0, castTimeMs) + Slack;

        /// <summary>Another curer than <paramref name="curerId"/> holds a live claim on <paramref name="targetId"/>.</summary>
        public static bool IsClaimed(Kind kind, long targetId, long curerId, long now) =>
            Claims.TryGetValue((kind, targetId), out Claim c) && c.Until > now && c.Curer != curerId;

        /// <summary>Claims <paramref name="targetId"/> for this curer until now + <see cref="Duration"/>; false when another
        /// curer's live claim holds him (it is left as it is).</summary>
        public static bool TryClaim(Kind kind, long targetId, long curerId, long now, int castTimeMs)
        {
            Prune(now);
            var claim = new Claim(curerId, now + Duration(castTimeMs));
            while (true)
            {
                if (!Claims.TryGetValue((kind, targetId), out Claim held))
                {
                    if (Claims.TryAdd((kind, targetId), claim)) return true;
                    continue;
                }
                if (held.Until > now && held.Curer != curerId) return false;
                if (Claims.TryUpdate((kind, targetId), claim, held)) return true;
            }
        }

        /// <summary>Ends this curer's claim on <paramref name="targetId"/> (its cast did not start).</summary>
        public static void Release(Kind kind, long targetId, long curerId)
        {
            if (Claims.TryGetValue((kind, targetId), out Claim held) && held.Curer == curerId)
                Claims.TryRemove(new KeyValuePair<(Kind, long), Claim>((kind, targetId), held));
        }

        /// <summary>Every claim (tests).</summary>
        public static void Clear() => Claims.Clear();

        private static void Prune(long now)
        {
            if (now < Interlocked.Read(ref _nextPrune)) return;
            Interlocked.Exchange(ref _nextPrune, now + 10_000);
            foreach (var entry in Claims)
                if (entry.Value.Until <= now) Claims.TryRemove(entry);
        }
    }
}
