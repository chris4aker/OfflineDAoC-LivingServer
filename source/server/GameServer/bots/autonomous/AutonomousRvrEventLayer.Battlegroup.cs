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

    /// <summary>One warband as the coordinator sees it this pulse: its force id, realm, region, leader's spot and size.</summary>
    public readonly record struct BattlegroupCandidate(string ForceId, eRealm Realm, ushort Region, float X, float Y, int Heads,
        bool InEvent, bool HostsPlayer);

    /// <summary>A planned link: an existing battlegroup (or null for a new one), its lead, and the groups that join it.</summary>
    public sealed record BattlegroupLink(string BattlegroupId, string LeadForceId, IReadOnlyList<string> Joining);

    /// <summary>A battlegroup as it stands: its id, realm, region, lead group and every group in it (the lead first).</summary>
    public sealed record BattlegroupView(string Id, eRealm Realm, ushort Region, string LeadForceId, IReadOnlyList<string> Members);

    /// <summary>Why a group left (or a battlegroup ended).</summary>
    public enum BattlegroupLeave { None, Gone, LeadGone, Event, Player, Region, Leash, Stay, TooSmall }

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
    /// battlegroup never moves to another. Pure.
    /// </summary>
    public static List<BattlegroupLink> PlanBattlegroups(IReadOnlyList<BattlegroupCandidate> candidates,
        IReadOnlyList<BattlegroupView> existing)
    {
        var links = new List<BattlegroupLink>();
        candidates ??= Array.Empty<BattlegroupCandidate>();
        existing ??= Array.Empty<BattlegroupView>();
        var byId = new Dictionary<string, BattlegroupCandidate>(StringComparer.Ordinal);
        foreach (BattlegroupCandidate candidate in candidates)
            if (!string.IsNullOrEmpty(candidate.ForceId))
                byId.TryAdd(candidate.ForceId, candidate);
        var taken = new HashSet<string>(existing.SelectMany(view => view.Members), StringComparer.Ordinal);
        List<BattlegroupCandidate> free = byId.Values.Where(candidate => CanLink(candidate) && !taken.Contains(candidate.ForceId))
            .OrderByDescending(candidate => candidate.Heads).ThenBy(candidate => candidate.ForceId, StringComparer.Ordinal).ToList();

        List<string> Pick(BattlegroupCandidate lead, int groups, int heads)
        {
            var picked = new List<string>();
            foreach (BattlegroupCandidate other in free.Where(other => Near(lead, other))
                         .OrderBy(other => DistanceBetween(lead, other)).ThenBy(other => other.ForceId, StringComparer.Ordinal).ToList())
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

        foreach (BattlegroupView view in existing.OrderBy(view => view.Id, StringComparer.Ordinal))
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
    /// leash (beyond <see cref="BattlegroupLeashRadius"/> for a minute) and its own stay. Pure.
    /// </summary>
    public static BattlegroupLeave BattlegroupLeaveReason(BattlegroupCandidate? member, BattlegroupCandidate? lead, bool isLead,
        long farSince, long stayUntil, long now)
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
    }

    private sealed class BattlegroupRecord
    {
        public string Id;
        public eRealm Realm;
        public ushort Region;
        public long FormedTick;
        public readonly List<BattlegroupMember> Members = new(); // the lead first
        public string Lead => Members.Count > 0 ? Members[0].ForceId : null;
        public BattlegroupView View => new(Id, Realm, Region, Lead, Members.Select(member => member.ForceId).ToArray());
    }

    private static readonly object BattlegroupSync = new();
    private static readonly Dictionary<string, BattlegroupRecord> Battlegroups = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, BattlegroupRecord> BattlegroupOfForce = new(StringComparer.Ordinal);
    private static int _nextBattlegroupNumber;

    /// <summary>
    /// One pulse of the battlegroup layer: groups leave by <see cref="BattlegroupLeaveReason"/> (a battlegroup whose lead
    /// left or that is down to one group ends), then <see cref="PlanBattlegroups"/> links. Each change is logged once as
    /// RVR_FORCE_BATTLEGROUP. Returns the changes (tests).
    /// </summary>
    public static List<BattlegroupChange> PulseBattlegroups(IReadOnlyList<BattlegroupCandidate> candidates, long now)
    {
        var changes = new List<BattlegroupChange>();
        var byId = new Dictionary<string, BattlegroupCandidate>(StringComparer.Ordinal);
        foreach (BattlegroupCandidate candidate in candidates ?? Array.Empty<BattlegroupCandidate>())
            if (!string.IsNullOrEmpty(candidate.ForceId))
                byId.TryAdd(candidate.ForceId, candidate);
        BattlegroupCandidate? Find(string id) => id != null && byId.TryGetValue(id, out BattlegroupCandidate found) ? found : null;
        lock (BattlegroupSync)
        {
            foreach (BattlegroupRecord record in Battlegroups.Values.OrderBy(record => record.Id, StringComparer.Ordinal).ToList())
            {
                BattlegroupCandidate? lead = Find(record.Lead);
                BattlegroupLeave leadReason = BattlegroupLeaveReason(lead, lead, true, 0, long.MaxValue, now);
                if (leadReason != BattlegroupLeave.None)
                {
                    EndBattlegroup(record, leadReason, byId, changes, now);
                    continue;
                }
                foreach (BattlegroupMember member in record.Members.Skip(1).ToList())
                {
                    BattlegroupCandidate? self = Find(member.ForceId);
                    if (self is { } here && lead is { } leader && here.Region == leader.Region)
                        member.FarSince = NextFarSince(member.FarSince, DistanceBetween(leader, here), now);
                    BattlegroupLeave reason = BattlegroupLeaveReason(self, lead, false, member.FarSince, member.StayUntil, now);
                    if (reason == BattlegroupLeave.None)
                        continue;
                    record.Members.Remove(member);
                    BattlegroupOfForce.Remove(member.ForceId);
                    changes.Add(new BattlegroupChange("left", record.Id, member.ForceId, Why(reason)));
                    LogBattlegroup("left", record, byId, member.ForceId, Why(reason), now);
                }
                if (record.Members.Count < 2)
                    EndBattlegroup(record, BattlegroupLeave.TooSmall, byId, changes, now);
            }

            foreach (BattlegroupLink link in PlanBattlegroups(byId.Values.ToList(), Battlegroups.Values.Select(record => record.View).ToList()))
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
                    record.Members.Add(new BattlegroupMember { ForceId = id, StayUntil = now + BattlegroupStay(id, now) });
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
        foreach (BattlegroupMember member in record.Members)
            BattlegroupOfForce.Remove(member.ForceId);
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
        int heads = record.Members.Sum(member => byId.TryGetValue(member.ForceId, out BattlegroupCandidate candidate) ? candidate.Heads : 0);
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
            _nextBattlegroupNumber = 0;
        }
    }
}
