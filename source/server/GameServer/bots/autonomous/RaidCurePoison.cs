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
        /// <summary>The cure comes first when at least this many raiders in reach carry a curable damage over time.</summary>
        public const int MinDotted = 2;
        /// <summary>A curer looks over the raid for dotted raiders at most this often.</summary>
        public const long ScanMilliseconds = 500;

        /// <summary>A raider in the curer's reach carrying a curable damage over time.</summary>
        public readonly record struct Dotted(long Id, bool OwnParty, int HealthPercent);

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
        public static int Choose(IReadOnlyList<Dotted> dotted, long curerId)
        {
            if (dotted == null || dotted.Count == 0) return -1;
            int own = Enumerable.Range(0, dotted.Count).Where(i => dotted[i].OwnParty)
                .OrderBy(i => dotted[i].HealthPercent).ThenBy(i => dotted[i].Id).DefaultIfEmpty(-1).First();
            if (own >= 0) return own;
            int[] others = Enumerable.Range(0, dotted.Count).OrderBy(i => dotted[i].Id).ToArray();
            return others[(int)(Math.Abs(curerId) % others.Length)];
        }

        /// <summary>
        /// This turn's raid cure (pure): which cure and on whom (an index into <paramref name="dotted"/>), or
        /// (<see cref="Cure.None"/>, -1) when the turn's heal comes first. <paramref name="cureReady"/>: the curer's cure delay
        /// is over. <paramref name="partyInDanger"/>: its own party's members in reach under <see cref="DangerPercent"/>%.
        /// </summary>
        public static (Cure Cure, int Target) Decide(bool inRaid, bool cureReady, bool canGroup, bool canSingle,
            IReadOnlyList<Dotted> dotted, int partyInDanger, bool markedPatient, long curerId)
        {
            int count = dotted?.Count ?? 0;
            if (!cureReady || !CuresFirst(inRaid, count, partyInDanger, markedPatient)) return (Cure.None, -1);
            int ownDotted = dotted.Count(d => d.OwnParty);
            Cure cure = Which(canGroup, canSingle, ownDotted);
            if (cure == Cure.None) return (Cure.None, -1);
            if (cure == Cure.Group)
                return (cure, Enumerable.Range(0, count).Where(i => dotted[i].OwnParty)
                    .OrderBy(i => dotted[i].HealthPercent).ThenBy(i => dotted[i].Id).First());
            return (cure, Choose(dotted, curerId));
        }
    }
}
