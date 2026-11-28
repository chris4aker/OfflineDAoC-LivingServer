using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// #599 step 1: the battlegroup, one force above groups. Outside a keep or relic event a warband used to be the biggest
/// unit in the field (roam.md finding 2), so two eights that met went their own ways. Period groups did not: guild groups
/// ran "one or two FGs ... on voice or in group chat" and a zerg was "everyone else in an area following a loose lead"
/// (docs/rvr-audit-1.2.3/reference.md §2.1, §2.3); from 1.66 the battlegroup (/bg, /bc) tied groups together with one
/// leader's calls, the groups staying groups. Here frontier warbands of one realm that stand near each other link into a
/// battlegroup of at most three groups: the lead group roams as before, the others trail it (the controller's
/// TrailBattlegroupLead), and the groups share one support roster (RealmWarbandSupport), so the "inc" calls, the stance
/// relay, the size-up and cross-group support work across them as they already do inside an event.
/// The rules are pure (<see cref="PlanBattlegroups"/>, <see cref="BattlegroupLeaveReason"/>); the record is kept here and
/// fed by the coordinator (<c>AutonomousBotGroupCoordinator.PulseBattlegroups</c>). Autonomous gamebots only: a group that
/// hosts a player never links, and companions are never in a warband.
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    /// <summary>Groups whose leaders stand this close link (the #505 force-cluster sampler's and roam.md's distance).</summary>
    public const float BattlegroupLinkRadius = 3_000f;
    /// <summary>A follower group this far from the lead's leader for <see cref="BattlegroupLeashMilliseconds"/> leaves.</summary>
    public const float BattlegroupLeashRadius = 6_000f;
    public const long BattlegroupLeashMilliseconds = 60_000L;
    /// <summary>A 2-3 group roam (the issue; reference.md §2.1 "one or two FGs", §5.2 "1-3 FGs").</summary>
    public const int BattlegroupMaxGroups = 3;
    public const int BattlegroupMaxHeads = 24;
    /// <summary>A duo or a lone bot stays as it is (§2.1: stealth pairs and duos roamed on their own).</summary>
    public const int BattlegroupMinimumGroup = 3;
    /// <summary>How long a follower group stays linked, rolled when it joins [E: no source gives it].</summary>
    public const long BattlegroupStayMinimumMilliseconds = 15 * 60_000L, BattlegroupStayMaximumMilliseconds = 40 * 60_000L;
    /// <summary>
    /// A group that left by its stay (or stuck on the trail) links to no battlegroup for this long, so it goes its own way
    /// instead of linking straight back to the lead it still stands near [E: no source; the shortest stay].
    /// </summary>
    public const long BattlegroupRelinkCooldownMilliseconds = BattlegroupStayMinimumMilliseconds;

    /// <summary>
    /// One warband as the coordinator sees it this pulse: its force id, realm, region, leader's spot and size.
    /// <paramref name="EventTarget"/> (#599 step 3): the event it is committed to, or null.
    /// </summary>
    public readonly record struct BattlegroupCandidate(string ForceId, eRealm Realm, ushort Region, float X, float Y, int Heads,
        bool InEvent, bool HostsPlayer, string EventTarget = null);

    /// <summary>A planned link: an existing battlegroup (or null for a new one), its lead, and the groups that join it.</summary>
    public sealed record BattlegroupLink(string BattlegroupId, string LeadForceId, IReadOnlyList<string> Joining);

    /// <summary>A battlegroup as it stands: its id, realm, region, lead group and every group in it (the lead first).</summary>
    public sealed record BattlegroupView(string Id, eRealm Realm, ushort Region, string LeadForceId, IReadOnlyList<string> Members,
        bool IsZerg = false);

    /// <summary>
    /// Why a group left (or a battlegroup ended). <see cref="BattlegroupLeave.Late"/> and <see cref="BattlegroupLeave.Zerg"/>
    /// (#599 step 3): a zerg recruit that never reached the zerg, and a group the realm's zerg took from its battlegroup.
    /// <see cref="BattlegroupLeave.Cap"/>: a zerg follower that did not fit under the cap of the event the zerg's lead took
    /// it to (<see cref="EnrolZergFollowers"/>).
    /// </summary>
    public enum BattlegroupLeave { None, Gone, LeadGone, Event, Player, Region, Leash, Stay, TooSmall, Stuck, Late, Zerg, Cap }

    /// <summary>One change of the record, as logged (RVR_FORCE_BATTLEGROUP): formed, joined, left or ended.</summary>
    public readonly record struct BattlegroupChange(string What, string Id, string ForceId, string Why);

    /// <summary>Whether a warband may be in a battlegroup at all. Pure.</summary>
    public static bool CanLink(BattlegroupCandidate candidate) =>
        !string.IsNullOrEmpty(candidate.ForceId) && candidate.Heads >= BattlegroupMinimumGroup && !candidate.InEvent && !candidate.HostsPlayer;

    private static float DistanceBetween(BattlegroupCandidate a, BattlegroupCandidate b) =>
        MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));

    private static bool Near(BattlegroupCandidate lead, BattlegroupCandidate other) =>
        other.Realm == lead.Realm && other.Region == lead.Region && DistanceBetween(lead, other) <= BattlegroupLinkRadius;

    /// <summary>
    /// The links to make this pulse. Existing battlegroups are topped up first (to three groups, 24 heads), nearest first;
    /// then free groups near each other form new ones, led by the biggest (then the lowest force id). A group already in a
    /// battlegroup never moves to another, and a <paramref name="resting"/> one (its re-link cooldown) links to none. Pure.
    /// </summary>
    public static List<BattlegroupLink> PlanBattlegroups(IReadOnlyList<BattlegroupCandidate> candidates,
        IReadOnlyList<BattlegroupView> existing, IReadOnlyCollection<string> resting = null)
    {
        var links = new List<BattlegroupLink>();
        candidates ??= Array.Empty<BattlegroupCandidate>();
        existing ??= Array.Empty<BattlegroupView>();
        var byId = new Dictionary<string, BattlegroupCandidate>(StringComparer.Ordinal);
        foreach (BattlegroupCandidate candidate in candidates)
            if (!string.IsNullOrEmpty(candidate.ForceId))
                byId.TryAdd(candidate.ForceId, candidate);
        var taken = new HashSet<string>(existing.SelectMany(view => view.Members), StringComparer.Ordinal);
        if (resting != null)
            taken.UnionWith(resting);
        List<BattlegroupCandidate> free = byId.Values.Where(candidate => CanLink(candidate) && !taken.Contains(candidate.ForceId))
            .OrderByDescending(candidate => candidate.Heads).ThenBy(candidate => candidate.ForceId, StringComparer.Ordinal).ToList();

        List<string> Pick(BattlegroupCandidate lead, int groups, int heads)
        {
            var picked = new List<string>();
            foreach (BattlegroupCandidate other in free.Where(c => Near(lead, c))
                         .OrderBy(c => DistanceBetween(lead, c)).ThenBy(c => c.ForceId, StringComparer.Ordinal).ToList())
            {
                if (groups >= BattlegroupMaxGroups) break;
                if (heads + other.Heads > BattlegroupMaxHeads) continue;
                picked.Add(other.ForceId);
                groups++;
                heads += other.Heads;
                free.Remove(other);
            }
            return picked;
        }

        foreach (BattlegroupView view in existing.OrderBy(v => v.Id, StringComparer.Ordinal))
        {
            if (view.Members.Count >= BattlegroupMaxGroups || !byId.TryGetValue(view.LeadForceId ?? string.Empty, out BattlegroupCandidate lead))
                continue;
            int heads = view.Members.Sum(id => byId.TryGetValue(id, out BattlegroupCandidate member) ? member.Heads : 0);
            List<string> joining = Pick(lead, view.Members.Count, heads);
            if (joining.Count > 0)
                links.Add(new BattlegroupLink(view.Id, view.LeadForceId, joining));
        }
        while (free.Count > 0)
        {
            BattlegroupCandidate lead = free[0];
            free.RemoveAt(0);
            List<string> joining = Pick(lead, 1, lead.Heads);
            if (joining.Count > 0)
                links.Add(new BattlegroupLink(null, lead.ForceId, joining));
        }
        return links;
    }

    /// <summary>When a member first stood beyond the leash (0: within it). Pure.</summary>
    public static long NextFarSince(long farSince, float distance, long now) =>
        distance > BattlegroupLeashRadius ? (farSince == 0 ? now : farSince) : 0;

    /// <summary>
    /// Why this group leaves its battlegroup now, or None. <paramref name="member"/> null: the group is gone (disbanded or no
    /// longer a warband). The lead's own reasons end the battlegroup; a follower also leaves for the lead's region, the
    /// leash (beyond <see cref="BattlegroupLeashRadius"/> for a minute), a trail its leader reported stuck
    /// (<paramref name="stuck"/>) and its own stay. Pure.
    /// </summary>
    public static BattlegroupLeave BattlegroupLeaveReason(BattlegroupCandidate? member, BattlegroupCandidate? lead, bool isLead,
        long farSince, long stayUntil, long now, bool stuck = false)
    {
        if (member is not { } self)
            return isLead ? BattlegroupLeave.LeadGone : BattlegroupLeave.Gone;
        if (self.InEvent)
            return BattlegroupLeave.Event;
        if (self.HostsPlayer)
            return BattlegroupLeave.Player;
        if (self.Heads < 2)
            return BattlegroupLeave.TooSmall;
        if (isLead)
            return BattlegroupLeave.None;
        if (lead is not { } leader)
            return BattlegroupLeave.LeadGone;
        if (self.Region != leader.Region)
            return BattlegroupLeave.Region;
        if (farSince > 0 && now - farSince >= BattlegroupLeashMilliseconds)
            return BattlegroupLeave.Leash;
        if (stuck)
            return BattlegroupLeave.Stuck;
        if (now >= stayUntil)
            return BattlegroupLeave.Stay;
        return BattlegroupLeave.None;
    }

    /// <summary>A follower group's stay, 15-40 minutes, the same for the same group and moment (a stable hash). Pure.</summary>
    public static long BattlegroupStay(string forceId, long now)
    {
        uint hash = 2166136261;
        foreach (char c in (forceId ?? string.Empty) + "/" + now.ToString(System.Globalization.CultureInfo.InvariantCulture))
            hash = unchecked((hash ^ c) * 16777619u);
        long range = BattlegroupStayMaximumMilliseconds - BattlegroupStayMinimumMilliseconds;
        return BattlegroupStayMinimumMilliseconds + hash % (range + 1);
    }

    // ---- the record ---------------------------------------------------------------------------------------------------

    private sealed class BattlegroupMember
    {
        public string ForceId;
        public long StayUntil;
        public long FarSince;
        /// <summary>#599 step 3: when it joined, and whether a zerg recruit has reached the zerg yet.</summary>
        public long JoinedTick;
        public bool Arrived = true;
    }

    private sealed class BattlegroupRecord
    {
        public string Id;
        public eRealm Realm;
        public ushort Region;
        public long FormedTick;
        public readonly List<BattlegroupMember> Members = new(); // the lead first
        public string Lead => Members.Count > 0 ? Members[0].ForceId : null;
        /// <summary>#599 step 3: the realm's zerg, with its size target and the gates it rolls between.</summary>
        public bool Zerg;
        public int Target, Free, Online;
        public bool ScheduleOn;
        public ZergAnchor[] Anchors = Array.Empty<ZergAnchor>();
        public int AnchorIndex;
        public long AnchorSince;
        /// <summary>When the lead was sent to the gate it holds now (the give-up timer), when the zerg ends, and the attack it is in.</summary>
        public long AnchorSetTick;
        public long EndsTick = long.MaxValue;
        public string Attack;
        public long AttackTick;
        public ZergAnchor Anchor => Anchors.Length > 0 ? Anchors[AnchorIndex % Anchors.Length] : null;
        public BattlegroupView View => new(Id, Realm, Region, Lead, Members.Select(member => member.ForceId).ToArray(), Zerg);
    }

    private static readonly object BattlegroupSync = new();
    private static readonly Dictionary<string, BattlegroupRecord> Battlegroups = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BattlegroupRecord> BattlegroupOfForce = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> BattlegroupRelinkAfter = new(StringComparer.Ordinal);
    private static readonly HashSet<string> BattlegroupStuckReports = new(StringComparer.Ordinal);
    /// <summary>#599 step 3 review: zerg followers that did not fit under the cap of the zerg's event (they leave, why=cap).</summary>
    private static readonly HashSet<string> ZergCapReports = new(StringComparer.Ordinal);
    private static int _nextBattlegroupNumber;

    /// <summary>Whether a group that left by <paramref name="reason"/> rests before it may link again. Pure.</summary>
    public static bool BattlegroupLeaveRests(BattlegroupLeave reason) =>
        reason is BattlegroupLeave.Stay or BattlegroupLeave.Stuck or BattlegroupLeave.Late or BattlegroupLeave.Cap;

    /// <summary>
    /// A follower group's leader made no progress on the trail (the controller's <c>BattlegroupTrailStuck</c>): the group
    /// leaves at the next pulse, by <see cref="BattlegroupLeave.Stuck"/>, and rests. Ignored for a group that follows none.
    /// </summary>
    public static void ReportBattlegroupStuck(string forceId)
    {
        if (forceId == null) return;
        lock (BattlegroupSync)
            if (BattlegroupOfForce.TryGetValue(forceId, out BattlegroupRecord record) && record.Lead != forceId)
                BattlegroupStuckReports.Add(forceId);
    }

    /// <summary>Whether this group is in its re-link cooldown at <paramref name="now"/> (tests and the rehearsal).</summary>
    public static bool BattlegroupResting(string forceId, long now)
    {
        if (forceId == null) return false;
        lock (BattlegroupSync)
            return BattlegroupRelinkAfter.TryGetValue(forceId, out long until) && now < until;
    }

    /// <summary>
    /// One pulse of the battlegroup layer: groups leave by <see cref="BattlegroupLeaveReason"/> (a battlegroup whose lead
    /// left or that is down to one group ends), then <see cref="PlanBattlegroups"/> links. Each change is logged once as
    /// RVR_FORCE_BATTLEGROUP. Returns the changes (tests).
    /// </summary>
    public static List<BattlegroupChange> PulseBattlegroups(IReadOnlyList<BattlegroupCandidate> candidates, long now) =>
        PulseBattlegroups(candidates, now, null, false);

    /// <summary>
    /// The pulse with the realm's zerg (#599 step 3): <paramref name="freeFrontierBots"/> is each realm's free frontier
    /// gamebots online (null: no zerg is planned), <paramref name="onlineFrontierBots"/> all of them, those in events
    /// included (null: the free count), <paramref name="scheduleOn"/> whether bot_schedule_mode is on, and
    /// <paramref name="frontierRegions"/> the regions a zerg may form in (null: any). After the leave rules, each realm's zerg
    /// rolls on to its next gate when its hold is over, then forms or is topped up (<see cref="PlanZerg"/>), taking groups
    /// out of their battlegroups; then the battlegroups link as before, from the groups no zerg holds.
    /// </summary>
    public static List<BattlegroupChange> PulseBattlegroups(IReadOnlyList<BattlegroupCandidate> candidates, long now,
        IReadOnlyDictionary<eRealm, int> freeFrontierBots, bool scheduleOn, IReadOnlyCollection<ushort> frontierRegions = null,
        IReadOnlyDictionary<eRealm, int> onlineFrontierBots = null)
    {
        var changes = new List<BattlegroupChange>();
        var byId = new Dictionary<string, BattlegroupCandidate>(StringComparer.Ordinal);
        foreach (BattlegroupCandidate candidate in candidates ?? Array.Empty<BattlegroupCandidate>())
            if (!string.IsNullOrEmpty(candidate.ForceId))
                byId.TryAdd(candidate.ForceId, candidate);
        BattlegroupCandidate? Find(string id) => id != null && byId.TryGetValue(id, out BattlegroupCandidate found) ? found : null;
        lock (BattlegroupSync)
        {
            foreach (string id in BattlegroupRelinkAfter.Where(pair => now >= pair.Value).Select(pair => pair.Key).ToList())
                BattlegroupRelinkAfter.Remove(id);
            foreach (BattlegroupRecord record in Battlegroups.Values.OrderBy(r => r.Id, StringComparer.Ordinal).ToList())
            {
                BattlegroupCandidate? lead = Find(record.Lead);
                // #599 step 3: a zerg's lead keeps it through the attack it opened or joined, and the zerg has a lifetime.
                if (record.Zerg && record.Attack != null && !ZergAttackHolds(lead, record.Attack, record.AttackTick, now))
                    record.Attack = null;
                BattlegroupLeave leadReason = record.Zerg
                    ? ZergLeadLeaveReason(lead, record.Attack, record.EndsTick, now)
                    : BattlegroupLeaveReason(lead, lead, true, 0, long.MaxValue, now);
                if (leadReason != BattlegroupLeave.None)
                {
                    EndBattlegroup(record, leadReason, byId, changes, now);
                    continue;
                }
                foreach (BattlegroupMember member in record.Members.Skip(1).ToList())
                {
                    BattlegroupCandidate? self = InZergAttack(Find(member.ForceId), record);
                    // #599 step 3: a zerg recruit travelling in from afar is held to the leash only once it has arrived.
                    if (!member.Arrived && ZergArrived(self, lead))
                        member.Arrived = true;
                    if (member.Arrived && self is { } here && lead is { } leader && here.Region == leader.Region)
                        member.FarSince = NextFarSince(member.FarSince, DistanceBetween(leader, here), now);
                    bool stuck = BattlegroupStuckReports.Contains(member.ForceId);
                    // #599 step 3 review: a follower the zerg's event had no room for leaves first, whatever else holds.
                    BattlegroupLeave reason = record.Zerg && ZergCapReports.Contains(member.ForceId) ? BattlegroupLeave.Cap
                        : member.Arrived
                        ? BattlegroupLeaveReason(self, lead, false, member.FarSince, member.StayUntil, now, stuck)
                        : ZergRecruitLeaveReason(self, lead, member.JoinedTick, member.StayUntil, now, stuck);
                    if (reason == BattlegroupLeave.None)
                        continue;
                    record.Members.Remove(member);
                    BattlegroupOfForce.Remove(member.ForceId);
                    BattlegroupStuckReports.Remove(member.ForceId);
                    // #599 review: a group whose stay ran out still stands near the lead; it goes its own way for a while.
                    if (BattlegroupLeaveRests(reason))
                        BattlegroupRelinkAfter[member.ForceId] = now + BattlegroupRelinkCooldownMilliseconds;
                    changes.Add(new BattlegroupChange("left", record.Id, member.ForceId, Why(reason)));
                    LogBattlegroup("left", record, byId, member.ForceId, Why(reason), now);
                }
                if (record.Members.Count < 2)
                    EndBattlegroup(record, BattlegroupLeave.TooSmall, byId, changes, now);
            }

            if (freeFrontierBots != null)
                PulseZergs(byId, now, freeFrontierBots, scheduleOn, frontierRegions, changes, onlineFrontierBots);
            BattlegroupStuckReports.Clear();
            ZergCapReports.Clear();
            // The groups a zerg holds are not free to link; the zergs are not battlegroups to top up.
            var unavailable = new List<string>(BattlegroupRelinkAfter.Keys);
            unavailable.AddRange(Battlegroups.Values.Where(r => r.Zerg).SelectMany(r => r.Members.Select(member => member.ForceId)));
            foreach (BattlegroupLink link in PlanBattlegroups(byId.Values.ToList(),
                         Battlegroups.Values.Where(r => !r.Zerg).Select(r => r.View).ToList(), unavailable))
            {
                BattlegroupRecord record;
                string what;
                if (link.BattlegroupId != null && Battlegroups.TryGetValue(link.BattlegroupId, out record))
                    what = "joined";
                else
                {
                    BattlegroupCandidate lead = byId[link.LeadForceId];
                    record = new BattlegroupRecord
                    {
                        Id = $"bgrp-{GlobalConstants.RealmToName(lead.Realm).ToLowerInvariant()}-{++_nextBattlegroupNumber:000}",
                        Realm = lead.Realm, Region = lead.Region, FormedTick = now,
                    };
                    record.Members.Add(new BattlegroupMember { ForceId = link.LeadForceId, StayUntil = long.MaxValue });
                    Battlegroups[record.Id] = record;
                    BattlegroupOfForce[link.LeadForceId] = record;
                    what = "formed";
                }
                foreach (string id in link.Joining)
                {
                    record.Members.Add(new BattlegroupMember { ForceId = id, StayUntil = now + BattlegroupStay(id, now), JoinedTick = now });
                    BattlegroupOfForce[id] = record;
                }
                string added = string.Join(",", link.Joining);
                changes.Add(new BattlegroupChange(what, record.Id, added, "near"));
                LogBattlegroup(what, record, byId, added, "near", now);
            }
        }
        return changes;
    }

    private static void EndBattlegroup(BattlegroupRecord record, BattlegroupLeave reason, Dictionary<string, BattlegroupCandidate> byId,
        List<BattlegroupChange> changes, long now)
    {
        Battlegroups.Remove(record.Id);
        // #599 step 3: a zerg whose lead was called or whose time is up rests its groups, so they go on as warbands (and answer
        // the call) instead of forming the realm's next zerg in the same pulse.
        bool rest = record.Zerg && ZergEndRests(reason);
        foreach (BattlegroupMember member in record.Members)
        {
            BattlegroupOfForce.Remove(member.ForceId);
            BattlegroupStuckReports.Remove(member.ForceId);
            if (rest)
                BattlegroupRelinkAfter[member.ForceId] = now + BattlegroupRelinkCooldownMilliseconds;
        }
        changes.Add(new BattlegroupChange("ended", record.Id, record.Lead, Why(reason)));
        LogBattlegroup("ended", record, byId, record.Lead, Why(reason), now);
    }

    private static string Why(BattlegroupLeave reason) => reason.ToString().ToLowerInvariant();

    /// <summary>
    /// RVR_FORCE_BATTLEGROUP: a battlegroup formed, a group joined or left it, or it ended (#599 step 1). Force ids are the
    /// warbands' session ids, never names.
    /// </summary>
    private static void LogBattlegroup(string what, BattlegroupRecord record, Dictionary<string, BattlegroupCandidate> byId,
        string force, string why, long now)
    {
        if (!Log.IsInfoEnabled) return;
        if (record.Zerg)
        {
            LogZerg(what, record, byId, force, why, now);
            return;
        }
        int heads = record.Members.Sum(member => byId.TryGetValue(member.ForceId, out BattlegroupCandidate candidate) ? candidate.Heads : 0);
        // One change of a frontier battlegroup (formed, joined, left or ended), with its groups and heads.
        Log.Info($"RVR_FORCE_BATTLEGROUP what={what} id={record.Id} realm={GlobalConstants.RealmToName(record.Realm)} " +
                 $"region={record.Region} lead={record.Lead ?? "-"} groups={record.Members.Count} heads={heads} " +
                 $"force={force ?? "-"} why={why} minutes={Math.Max(0, (now - record.FormedTick) / 60_000)}");
    }

    // ---- reads ------------------------------------------------------------------------------------------------------

    /// <summary>The battlegroup this force is in, or null.</summary>
    public static BattlegroupView BattlegroupOf(string forceId)
    {
        if (forceId == null) return null;
        lock (BattlegroupSync)
            return BattlegroupOfForce.TryGetValue(forceId, out BattlegroupRecord record) ? record.View : null;
    }

    /// <summary>The lead group's force id when <paramref name="forceId"/> follows one in a battlegroup; null for a lead or no battlegroup.</summary>
    public static string BattlegroupLeadOf(string forceId)
    {
        if (forceId == null) return null;
        lock (BattlegroupSync)
            return BattlegroupOfForce.TryGetValue(forceId, out BattlegroupRecord record) && record.Lead != forceId ? record.Lead : null;
    }

    /// <summary>Every battlegroup now (RealmWarbandSupport's rosters).</summary>
    public static List<BattlegroupView> BattlegroupsNow()
    {
        lock (BattlegroupSync)
            return Battlegroups.Values.OrderBy(record => record.Id, StringComparer.Ordinal).Select(record => record.View).ToList();
    }

    /// <summary>Tests.</summary>
    public static void ResetBattlegroups()
    {
        lock (BattlegroupSync)
        {
            Battlegroups.Clear();
            BattlegroupOfForce.Clear();
            BattlegroupRelinkAfter.Clear();
            BattlegroupStuckReports.Clear();
            ZergCapReports.Clear();
            _nextBattlegroupNumber = 0;
            _nextZergNumber = 0;
        }
    }
}
