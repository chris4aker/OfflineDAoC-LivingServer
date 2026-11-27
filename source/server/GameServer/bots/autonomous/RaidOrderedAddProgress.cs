using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// The progress an attempt made on a boss's required adds that die first, in order (the catalog's <c>AddsInOrder</c>:
    /// Myrddraxis's heads, Aroon's Scaiths, Xaga's Beatha and Tine, Scurceol Hyrde's orbs), for judging the attempt (#768).
    /// The route judged an attempt by the boss's own health alone, and the raid fights such a boss's adds while the boss stands
    /// at full health. So a lost head fight never counted as a wipe unless half the raid fell. Owner's PC, rc.2 (4f5a2aa,
    /// overnight 2026-11-26/27, 11.08 h): Myrddraxis's attempts broke off 15 times in 15, with 872 raider deaths, no try used
    /// and no Special Forces called. Owner, 2026-11-27 (#768): yes, an attempt's lowest also counts those adds, with the same
    /// 70% / 95% rule, so a lost head fight is one of the raid's two tries and calls the Special Forces. No boss is changed.
    /// <para>
    /// The evidence is code analysis, not a log: rc.2's rows carry only the body's health, so no log shows how low the heads
    /// went, and the log study of #768 (D1) has not been run. If the heads never fall below 70% (candidate A on #768: melee
    /// can't reach the flying heads and the heads heal each other), these attempts stay broke-off under this rule too. Whether
    /// rc.2's outcome changes is unknown until <c>addsLowest=</c> shows up in a PC log. Known limit: a head killed and not yet
    /// back when the attempt ends leaves its progress standing (<see cref="AddsNowPercent"/> 0), so an attempt that killed a head
    /// and then lost with fewer than half fallen is still no wipe (Myrddraxis brings killed heads back only once his body loses
    /// aggro). Tests only: not yet seen in a PC log.
    /// </para>
    /// </summary>
    public sealed class RaidOrderedAddProgress
    {
        private readonly Dictionary<object, int> _lowest = new(ReferenceEqualityComparer.Instance);

        /// <summary>Whether this attempt tracks any ordered adds (its boss has them, and they were there at the pull).</summary>
        public bool Tracking => BeganWith > 0;

        /// <summary>How many of the boss's ordered adds were alive at the pull.</summary>
        public int BeganWith { get; private set; }

        /// <summary>The lowest health in percent any tracked add reached in the attempt (0: one died); 100 when none is tracked.</summary>
        public int Lowest => _lowest.Count == 0 ? 100 : _lowest.Values.Min();

        /// <summary>The adds seen in this attempt, for the route's look at those that have died since.</summary>
        public object[] Tracked => _lowest.Keys.ToArray();

        /// <summary>A new attempt: forgets the last one's adds (an add killed in an earlier attempt is not this one's).</summary>
        public void Begin(int liveOrderedAdds)
        {
            _lowest.Clear();
            BeganWith = Math.Max(0, liveOrderedAdds);
        }

        /// <summary>One look at an add of the attempt: a dead add counts as 0.</summary>
        public void Note(object add, bool alive, int healthPercent)
        {
            if (add == null || !Tracking) return;
            int now = alive ? Math.Clamp(healthPercent, 0, 100) : 0;
            _lowest[add] = _lowest.TryGetValue(add, out int was) ? Math.Min(was, now) : now;
        }

        /// <summary>
        /// The adds' health now, for the 95% half of the rule (pure): the lowest of the live adds, or 0 while fewer are alive
        /// than at the pull. An add killed in the attempt and not back means the encounter has not reset, so that attempt's
        /// progress stands and it is no wipe by the reset rule. 100 with none tracked.
        /// </summary>
        public static int AddsNowPercent(int beganWith, IReadOnlyCollection<int> liveHealthPercents)
        {
            if (beganWith <= 0) return 100;
            int live = liveHealthPercents?.Count ?? 0;
            if (live < beganWith) return 0;
            return liveHealthPercents.Min();
        }

        /// <summary>The encounter's health now and its lowest, as the attempt is judged (pure): the boss's alone without ordered
        /// adds; with them, the lower of the boss's and the adds' (owner, #768: "the same 70%/95% rule").</summary>
        public static (int Now, int Lowest) Judged(int bossNow, int bossLowest, bool tracking, int addsNow, int addsLowest) =>
            tracking ? (Math.Min(bossNow, addsNow), Math.Min(bossLowest, addsLowest)) : (bossNow, bossLowest);

        /// <summary>An adds' field of the attempt's end rows (<c>addsHealth=</c>, <c>addsLowest=</c>): "-" without ordered adds. Pure.</summary>
        public static string Text(bool tracking, int percent) => tracking ? percent.ToString(CultureInfo.InvariantCulture) : "-";

        /// <summary>How an ended attempt is judged on the boss and its ordered adds.</summary>
        public readonly record struct Scored(bool Tracking, int AddsNow, int AddsLowest, int Now, int Lowest);

        /// <summary>
        /// The ended attempt as it is judged (pure but for this attempt's notes; EndAttempt calls it): the boss's health now and
        /// lowest, and with ordered adds tracked, the adds' (<see cref="AddsNowPercent"/> of <paramref name="liveAddHealths"/>,
        /// <see cref="Lowest"/>) folded in by <see cref="Judged"/>.
        /// </summary>
        public Scored Score(int bossNow, int bossLowest, IReadOnlyCollection<int> liveAddHealths)
        {
            bool tracking = Tracking;
            int addsNow = tracking ? AddsNowPercent(BeganWith, liveAddHealths) : 100, addsLowest = Lowest;
            var (now, lowest) = Judged(bossNow, bossLowest, tracking, addsNow, addsLowest);
            return new Scored(tracking, addsNow, addsLowest, now, lowest);
        }
    }
}
