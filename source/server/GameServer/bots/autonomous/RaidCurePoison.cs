using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// A raid-wide damage over time is cured before a top-up heal (Living Server 1.2, sweep 7b; tests only).
    /// <para>Why: owner's PC, build 9a173f4, 2026-11-18/19 (Summoner's Hall, Hibernia and Albion, four attempts at Summoner
    /// Roesia): her Summoner Pain (150 every 3 s for 36 s, radius 1,000 around a random raider, recast every 20 s, so two
    /// casts are always active) was never cured: 0 RAID_CURE_POISON lines all night. BotBrain's heal turn cast a cure only
    /// when no heal had been chosen, and in a raid a heal was always chosen: the raid-wide patient check marks an emergency
    /// whenever any raider within 1,800 is under 40%, which under Summoner Pain is always. A single cure was also handed to
    /// the raid's heal reservation, which aimed it at the lowest raider under 80% (poisoned or not) or dropped it.</para>
    /// <para>What: an autonomous raider of a realm raid with a cure poison spell of its class (CurePoison or CurePoisonGroup:
    /// nothing new) cures first when at least <see cref="MinDotted"/> raiders in its reach carry a curable damage over time
    /// (<see cref="GameLiving.IsPoisoned"/>) and nobody of its own party in reach is under <see cref="DangerPercent"/>% and
    /// no boss mechanic marks a patient. Its own party's emergencies still come first; another party's emergency is that
    /// party's and the raid's other healers' (each curer cures at most once every 5 seconds, then heals). The group cure is
    /// preferred when two or more of its own party carry it; otherwise the single cure goes to its own party's lowest dotted
    /// member, then to a raider of another party (a different one for each curer).</para>
    /// No boss is changed: Summoner Roesia and Summoner Pain are as they were. Autonomous gamebots in a realm raid only.
    /// </summary>
    public static class RaidCurePoison
    {
        /// <summary>A member of the curer's own party under this is in danger: it is healed first.</summary>
        public const int DangerPercent = 50;
        /// <summary>A member that carries a curable damage over time is in danger only under this (1.2.1, #410; tests only):
        /// the cure is its best help, the rest of a Summoner Pain (up to 12 ticks) outlasts any heal.</summary>
        public const int DottedDangerPercent = 25;
        /// <summary>A raid curer cures again at most this long after its last raid cure (1.2.1, #410; tests only; was the
        /// heal turn's 5 s cure delay).</summary>
        public const long CureDelayMilliseconds = 2_500;

        /// <summary>
        /// The member holds the raid cure back (pure): under <see cref="DangerPercent"/>%, or under
        /// <see cref="DottedDangerPercent"/>% when it carries the curable damage over time itself.
        /// <para>Why (1.2.1, #410; tests only): a deterministic model of the Roesia fight (UT_RaidSim121SummonersGreenKnight),
        /// calibrated to build b1b6846's first minute (Summoner Pain 124,913 on 71 raiders; the raid wiped at 76%), showed the
        /// 50% rule block the cure in the parties that needed it most: their dotted members sat between 25% and 50% under
        /// Pain, so their healers topped them up while the Pain went on ticking.</para>
        /// </summary>
        public static bool InDanger(int healthPercent, bool dotted) => healthPercent < (dotted ? DottedDangerPercent : DangerPercent);
        /// <summary>The cure comes first when at least this many raiders in reach carry a curable damage over time.</summary>
        public const int MinDotted = 2;
        /// <summary>A curer looks over the raid for dotted raiders at most this often.</summary>
        public const long ScanMilliseconds = 500;

        /// <summary>A raider in the curer's reach carrying a curable damage over time. <paramref name="Health"/>: its health
        /// points (0: not given; the own party's order then goes by <paramref name="HealthPercent"/>). <paramref name="Fresh"/>:
        /// its damage over time has most of its time left (<see cref="IsFresh"/>).</summary>
        public readonly record struct Dotted(long Id, bool OwnParty, int HealthPercent, int Health = 0, bool Fresh = false);

        /// <summary>
        /// A damage over time with at least two thirds of its time left is fresh (pure): it is cured even while a member of the
        /// curer's own party is in danger (<see cref="Decide"/>).
        /// <para>Why (1.2.1, #410 round 2; tests only): Summoner Pain ticks the same 125 on everyone it holds, lasts 36 s and
        /// comes back on about 92% of the raid every 25-30 s (her 20 s recast, a 35% chance at each 2 s think, a 3 s cast), so
        /// a cure in the first 12 s after it lands spares about 7-9 ticks (900-1,100 health) and one in its last seconds spares
        /// nearly nothing. Round 1's rule held every cure back while any member of the curer's party was under 25% (dotted) or
        /// 50%, which after the first minute is nearly always, so the Pain ran its whole length on most raiders. No realm has a
        /// group cure poison (the OpenDAoC database: Cure Poison 1371, 3072 and 4983 are single-target, radius 0), so every
        /// raider is one cast.</para>
        /// </summary>
        public static bool IsFresh(long remainingMilliseconds, long durationMilliseconds) =>
            durationMilliseconds > 0 && remainingMilliseconds * 3 >= durationMilliseconds * 2;

        /// <summary>The longest-lasting curable damage over time on <paramref name="living"/>: its time left and its whole
        /// duration (0, 0: none).</summary>
        public static (long Remaining, long Duration) DotTime(GameLiving living)
        {
            if (living?.effectListComponent == null) return (0, 0);
            long remaining = 0, duration = 0;
            try
            {
                foreach (ECSGameEffect effect in living.effectListComponent.GetEffects(eEffect.DamageOverTime))
                {
                    if (effect == null || effect.Duration <= 0) continue;
                    long left = effect.GetRemainingTimeForClient();
                    if (left > remaining) { remaining = left; duration = effect.Duration; }
                }
            }
            catch (InvalidOperationException) { return (0, 0); }
            return (remaining, duration);
        }

        public enum Cure { None, Group, Single }

        /// <summary>The raid cure comes before this turn's heal (pure).</summary>
        public static bool CuresFirst(bool inRaid, int dottedInReach, int partyInDanger, bool markedPatient) =>
            inRaid && dottedInReach >= MinDotted && partyInDanger <= 0 && !markedPatient;

        /// <summary>Which of the class's cures (pure): the group cure for two or more of the own party, else the single cure,
        /// else the group cure for one of the own party.</summary>
        public static Cure Which(bool canGroup, bool canSingle, int ownPartyDotted)
        {
            if (canGroup && ownPartyDotted >= MinDotted) return Cure.Group;
            if (canSingle) return Cure.Single;
            return canGroup && ownPartyDotted > 0 ? Cure.Group : Cure.None;
        }

        /// <summary>Whom a single cure goes to (pure; an index into <paramref name="dotted"/>, -1: nobody): the own party's
        /// lowest, else one of the others by the curer's id, so that the raid's curers spread.</summary>
        public static int Choose(IReadOnlyList<Dotted> dotted, long curerId) => Choose(dotted, curerId, null);

        /// <summary>As <see cref="Choose(IReadOnlyList{Dotted}, long)"/>, leaving out every raider another curer has claimed
        /// for a single cure (<paramref name="claimed"/>: RaidCureClaims, sweep 7c H-1); -1 when all are claimed. With two
        /// dotted raiders of other parties and twelve curers, six cast on each in the same moment; now one does.</summary>
        public static int Choose(IReadOnlyList<Dotted> dotted, long curerId, Func<long, bool> claimed)
        {
            if (dotted == null || dotted.Count == 0) return -1;
            int[] open = Enumerable.Range(0, dotted.Count).Where(i => claimed?.Invoke(dotted[i].Id) != true).ToArray();
            if (open.Length == 0) return -1;
            // 1.2.1 (#410 round 2): the own party's member with the fewest health points first (a damage over time ticks the
            // same on everyone: the one with the least health dies first), then the lowest percentage, then the lowest id.
            int own = open.Where(i => dotted[i].OwnParty)
                .OrderBy(i => dotted[i].Health).ThenBy(i => dotted[i].HealthPercent).ThenBy(i => dotted[i].Id).DefaultIfEmpty(-1).First();
            if (own >= 0) return own;
            int[] others = open.OrderBy(i => dotted[i].Id).ToArray();
            return others[(int)(Math.Abs(curerId) % others.Length)];
        }

        /// <summary>
        /// This turn's raid cure (pure): which cure and on whom (an index into <paramref name="dotted"/>), or
        /// (<see cref="Cure.None"/>, -1) when the turn's heal comes first. <paramref name="cureReady"/>: the curer's cure delay
        /// is over. <paramref name="partyInDanger"/>: its own party's members in reach under <see cref="DangerPercent"/>%.
        /// </summary>
        public static (Cure Cure, int Target) Decide(bool inRaid, bool cureReady, bool canGroup, bool canSingle,
            IReadOnlyList<Dotted> dotted, int partyInDanger, bool markedPatient, long curerId, Func<long, bool> claimed = null)
        {
            // 1.2.1 (#410 round 2): with a member of its party in danger the curer still cures the fresh ones (IsFresh), when
            // two or more are fresh; the indexes returned are into the whole list.
            if (partyInDanger > 0 && !markedPatient && dotted != null)
            {
                int[] fresh = Enumerable.Range(0, dotted.Count).Where(i => dotted[i].Fresh).ToArray();
                if (fresh.Length >= MinDotted)
                {
                    var (c, p) = Decide(inRaid, cureReady, canGroup, canSingle, fresh.Select(i => dotted[i]).ToArray(), 0, false,
                        curerId, claimed);
                    return p < 0 ? (c, p) : (c, fresh[p]);
                }
            }
            int count = dotted?.Count ?? 0;
            if (!cureReady || !CuresFirst(inRaid, count, partyInDanger, markedPatient)) return (Cure.None, -1);
            int ownDotted = dotted.Count(d => d.OwnParty);
            Cure cure = Which(canGroup, canSingle, ownDotted);
            if (cure == Cure.None) return (Cure.None, -1);
            if (cure == Cure.Group)
                return (cure, Enumerable.Range(0, count).Where(i => dotted[i].OwnParty)
                    .OrderBy(i => dotted[i].HealthPercent).ThenBy(i => dotted[i].Id).First());
            // A single cure skips a raider another curer is already curing (the group cure is the curer's own party's).
            int pick = Choose(dotted, curerId, claimed);
            return pick < 0 ? (Cure.None, -1) : (cure, pick);
        }
    }
}
