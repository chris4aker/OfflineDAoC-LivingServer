using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// #599 step 3 (#530 step 5d): the realm's zerg, the slow realm force of 16-24 gamebots. Period frontiers had one beside the
/// 8-mans: "everyone else in an area following a loose lead", at a milegate or a keep, slow, rarely far from its gate ("AMG
/// for albs"), rolling "AMG &lt;-&gt; MMG and back all night" (docs/rvr-audit-1.2.3/reference.md §1.4, §1.5, §2.1, §2.3, §6).
/// Before this the biggest unit in the field was a battlegroup of three groups that happened to stand near each other
/// (roam.md finding 2).
/// <para>The zerg is a battlegroup record of its own kind (<c>zerg-&lt;realm&gt;-&lt;n&gt;</c>), so it has everything a
/// battlegroup has: one support roster (RealmWarbandSupport), one target call (RvrAssist), follower groups trailing the
/// lead. What differs: its size is set by who is online (<see cref="ZergTargetHeads"/>, owner 50A); it recruits the
/// realm's warbands from the whole frontier, pairs included, not only those within the link radius
/// (<see cref="PlanZerg"/>); a recruit coming from afar is held to the leash only once it arrives
/// (<see cref="ZergRecruitLeaveReason"/>); and its lead holds the region's gates in turn
/// (<see cref="ZergAnchors"/>, the controller's ZergLeadTurn). At most one zerg per realm. Autonomous gamebots only: a
/// group that hosts a player never joins, and companions are never in a warband.</para>
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    /// <summary>The smallest zerg: two full groups, the low end of the issue's 16-24.</summary>
    public const int ZergMinimumHeads = 16;
    /// <summary>The biggest: three full groups (the issue's 16-24).</summary>
    public const int ZergMaximumHeads = 24;
    /// <summary>At most this many groups (pairs may join) [E: no source; 24 heads of pairs and trios].</summary>
    public const int ZergMaxGroups = 8;
    /// <summary>A pair may join a zerg ("invisible or join a zerg", §2.1); a lone bot does not.</summary>
    public const int ZergMinimumGroup = 2;
    /// <summary>
    /// The share of the realm's free frontier gamebots the zerg takes (owner 50A: a fixed share) [E: §2.3, prime time "half
    /// or more in zergs and warbands"; the zerg takes a third, the warbands and battlegroups keep the rest].
    /// </summary>
    public const double ZergShare = 1.0 / 3.0;
    /// <summary>How long the zerg's lead holds a gate before it rolls on to the next [E: §1.4 "for tens of minutes"].</summary>
    public const long ZergHoldMilliseconds = 8 * 60_000L;
    /// <summary>A recruit that has not reached the zerg within this leaves (why=late) and rests [E: a cross-frontier trip].</summary>
    public const long ZergJoinMilliseconds = 15 * 60_000L;
    /// <summary>
    /// A gate's road: the nearest other chokepoint within this of a milegate [E: the catalog's spacing; the nearest road to
    /// Hadrian's Wall's gates is about 30,000 off, Odin's Gate's 28,000, Emain's 8,600 and 22,000].
    /// </summary>
    public const float ZergRoadReach = 35_000f;

    /// <summary>One gate the zerg holds: a check spot of the catalog (a milegate or a road chokepoint by one) and its stand.</summary>
    public sealed record ZergAnchor(string SpotId, string Label, ushort Region, float X, float Y, float Z);

    /// <summary>A zerg to form (<see cref="ZergId"/> null) or to top up, in <see cref="Region"/>, and the groups that join.</summary>
    public sealed record ZergPlan(string ZergId, string LeadForceId, ushort Region, IReadOnlyList<string> Joining);

    /// <summary>What the controller reads: the zerg a force is in, the gate it holds now, and whether the force leads it.</summary>
    public sealed record ZergOrder(string ZergId, ZergAnchor Anchor, bool IsLead, ushort Region);

    /// <summary>
    /// The zerg's size for a realm with <paramref name="freeFrontierBots"/> free frontier gamebots online (owner 50A): a third,
    /// at most 24. With <paramref name="scheduleOn"/> (bot_schedule_mode on) it is never under 16 while at least 16 are free.
    /// Under 16: 0, no zerg (battlegroups of 2-3 groups still form). Pure.
    /// </summary>
    public static int ZergTargetHeads(int freeFrontierBots, bool scheduleOn)
    {
        int free = Math.Max(0, freeFrontierBots);
        int target = Math.Min(ZergMaximumHeads, (int)Math.Floor(free * ZergShare));
        if (scheduleOn && free >= ZergMinimumHeads)
            target = Math.Max(target, ZergMinimumHeads);
        return target >= ZergMinimumHeads ? target : 0;
    }

    /// <summary>Whether a warband may join a zerg: two or more, in no event, hosting no player. Pure.</summary>
    public static bool CanJoinZerg(BattlegroupCandidate candidate) =>
        !string.IsNullOrEmpty(candidate.ForceId) && candidate.Heads >= ZergMinimumGroup && !candidate.InEvent && !candidate.HostsPlayer;

    /// <summary>
    /// The zerg to form for <paramref name="realm"/>, or the groups that top <paramref name="existing"/> up, or null. A new
    /// zerg forms in the frontier region holding most of the realm's free warband heads, led by its biggest group (then the
    /// lowest force id), and only when it reaches <see cref="ZergMinimumHeads"/>. Recruits come from the lead's region
    /// first, nearest first, then from the other regions, biggest first, until the heads reach <paramref name="target"/>;
    /// never over <see cref="ZergMaximumHeads"/> heads or <see cref="ZergMaxGroups"/> groups. A group in
    /// <paramref name="unavailable"/> (resting, or another zerg's) never joins; a group in a battlegroup may. Pure.
    /// </summary>
    public static ZergPlan PlanZerg(eRealm realm, IReadOnlyList<BattlegroupCandidate> candidates, BattlegroupView existing,
        int target, IReadOnlyCollection<string> unavailable = null, IReadOnlyCollection<ushort> frontierRegions = null)
    {
        var byId = new Dictionary<string, BattlegroupCandidate>(StringComparer.Ordinal);
        foreach (BattlegroupCandidate candidate in candidates ?? Array.Empty<BattlegroupCandidate>())
            if (!string.IsNullOrEmpty(candidate.ForceId) && candidate.Realm == realm)
                byId.TryAdd(candidate.ForceId, candidate);
        var taken = new HashSet<string>(unavailable ?? Array.Empty<string>(), StringComparer.Ordinal);
        if (existing != null)
            taken.UnionWith(existing.Members);
        List<BattlegroupCandidate> free = byId.Values.Where(candidate => CanJoinZerg(candidate) && !taken.Contains(candidate.ForceId)).ToList();

        BattlegroupCandidate lead;
        int heads, groups;
        if (existing != null)
        {
            if (!byId.TryGetValue(existing.LeadForceId ?? string.Empty, out lead))
                return null;
            heads = existing.Members.Sum(id => byId.TryGetValue(id, out BattlegroupCandidate member) ? member.Heads : 0);
            groups = existing.Members.Count;
        }
        else
        {
            if (target < ZergMinimumHeads)
                return null;
            IGrouping<ushort, BattlegroupCandidate> region = free
                .Where(candidate => frontierRegions == null || frontierRegions.Contains(candidate.Region))
                .GroupBy(candidate => candidate.Region)
                .OrderByDescending(group => group.Sum(candidate => candidate.Heads)).ThenBy(group => group.Key).FirstOrDefault();
            if (region == null)
                return null;
            lead = region.OrderByDescending(candidate => candidate.Heads).ThenBy(candidate => candidate.ForceId, StringComparer.Ordinal).First();
            free.Remove(lead);
            heads = lead.Heads;
            groups = 1;
        }

        var joining = new List<string>();
        foreach (BattlegroupCandidate other in free
                     .OrderBy(candidate => candidate.Region == lead.Region ? 0 : 1)
                     .ThenBy(candidate => candidate.Region == lead.Region ? DistanceBetween(lead, candidate) : 0f)
                     .ThenByDescending(candidate => candidate.Heads)
                     .ThenBy(candidate => candidate.ForceId, StringComparer.Ordinal))
        {
            if (heads >= target || groups >= ZergMaxGroups)
                break;
            if (heads + other.Heads > ZergMaximumHeads)
                continue;
            joining.Add(other.ForceId);
            heads += other.Heads;
            groups++;
        }
        if (joining.Count == 0 || existing == null && heads < ZergMinimumHeads)
            return null;
        return new ZergPlan(existing?.Id, lead.ForceId, existing?.Region ?? lead.Region, joining);
    }

    /// <summary>Whether a zerg recruit has reached the zerg: in the lead's region, within the link radius of its leader. Pure.</summary>
    public static bool ZergArrived(BattlegroupCandidate? member, BattlegroupCandidate? lead) =>
        member is { } self && lead is { } leader && self.Region == leader.Region && DistanceBetween(self, leader) <= BattlegroupLinkRadius;

    /// <summary>
    /// Why a zerg recruit still on its way leaves, or None: the battlegroup's own reasons except the region and the leash
    /// (it is coming from afar), and <see cref="BattlegroupLeave.Late"/> once it has been on the way
    /// <see cref="ZergJoinMilliseconds"/>. Once it has arrived, <see cref="BattlegroupLeaveReason"/> applies. Pure.
    /// </summary>
    public static BattlegroupLeave ZergRecruitLeaveReason(BattlegroupCandidate? member, BattlegroupCandidate? lead, long joinedTick,
        long stayUntil, long now, bool stuck = false)
    {
        if (member is not { } self)
            return BattlegroupLeave.Gone;
        if (self.InEvent)
            return BattlegroupLeave.Event;
        if (self.HostsPlayer)
            return BattlegroupLeave.Player;
        if (self.Heads < 2)
            return BattlegroupLeave.TooSmall;
        if (lead is null)
            return BattlegroupLeave.LeadGone;
        if (now - joinedTick >= ZergJoinMilliseconds)
            return BattlegroupLeave.Late;
        if (stuck)
            return BattlegroupLeave.Stuck;
        if (now >= stayUntil)
            return BattlegroupLeave.Stay;
        return BattlegroupLeave.None;
    }

    // The catalog's milegate spots are named by whose gate they are: emain-amg, hw-hmg, og-amg ("AMG road" is a road).
    private static readonly Regex MilegateId = new(@"-([ahm])mg$", RegexOptions.CultureInvariant);

    /// <summary>The milegate a check spot is ('A', 'M' or 'H': whose gate), or null when it is not one. Pure.</summary>
    public static char? MilegateOf(RvrFrontierPoi spot)
    {
        if (spot == null || spot.Kind != RvrSpotKind.Chokepoint) return null;
        Match match = MilegateId.Match(spot.Id ?? string.Empty);
        return match.Success ? char.ToUpperInvariant(match.Groups[1].Value[0]) : null;
    }

    private static char RealmGateLetter(eRealm realm) => realm switch
    {
        eRealm.Albion => 'A',
        eRealm.Midgard => 'M',
        eRealm.Hibernia => 'H',
        _ => '?',
    };

    /// <summary>
    /// The gates a realm's zerg holds in a frontier, in turn: each milegate (the realm's own first, then by id) followed by
    /// its road, the nearest other chokepoint within <see cref="ZergRoadReach"/> outside the reserve zones. In Emain for
    /// Albion: AMG, its lane hastener, MMG, the crossroads. Spots <paramref name="usable"/> refuses (rejected by the
    /// navmesh checks, or out of the realm's reach) are left out; no milegate: none. A road already taken by an earlier gate
    /// is not taken twice. Pure.
    /// </summary>
    public static List<ZergAnchor> ZergAnchors(IReadOnlyList<RvrFrontierPoi> spots, eRealm realm, Func<RvrFrontierPoi, bool> usable = null)
    {
        var anchors = new List<ZergAnchor>();
        if (spots == null) return anchors;
        List<RvrFrontierPoi> open = spots.Where(spot => spot != null && spot.Kind == RvrSpotKind.Chokepoint && !spot.ReserveZone &&
                                                        (usable == null || usable(spot))).ToList();
        char own = RealmGateLetter(realm);
        var used = new HashSet<string>(StringComparer.Ordinal);
        ZergAnchor Of(RvrFrontierPoi spot) => new(spot.Id, spot.Label, spot.Region, spot.Stand.X, spot.Stand.Y, spot.Stand.Z);
        foreach (RvrFrontierPoi gate in open.Where(spot => MilegateOf(spot) != null)
                     .OrderBy(spot => MilegateOf(spot) == own ? 0 : 1).ThenBy(spot => spot.Id, StringComparer.Ordinal).ToList())
        {
            anchors.Add(Of(gate));
            used.Add(gate.Id);
            RvrFrontierPoi road = open.Where(spot => MilegateOf(spot) == null && !used.Contains(spot.Id))
                .Select(spot => (Spot: spot, Distance: System.Numerics.Vector2.Distance(new(spot.Position.X, spot.Position.Y),
                    new(gate.Position.X, gate.Position.Y))))
                .Where(pair => pair.Distance <= ZergRoadReach)
                .OrderBy(pair => pair.Distance).ThenBy(pair => pair.Spot.Id, StringComparer.Ordinal)
                .Select(pair => pair.Spot).FirstOrDefault();
            if (road == null) continue;
            anchors.Add(Of(road));
            used.Add(road.Id);
        }
        return anchors;
    }

    /// <summary>Whether the lead, at its gate since <paramref name="since"/> (0: not there yet), rolls on now. Pure.</summary>
    public static bool ZergAnchorDue(long since, long now) => since > 0 && now - since >= ZergHoldMilliseconds;

    // ---- the record ---------------------------------------------------------------------------------------------------

    private static int _nextZergNumber;

    /// <summary>The gates a zerg of <paramref name="realm"/> holds in <paramref name="region"/>, from the check-spot catalog.</summary>
    private static ZergAnchor[] AnchorsFor(eRealm realm, ushort region)
    {
        RvrCheckSpotCatalog catalog = RvrCheckSpots.Catalog;
        RvrFrontier frontier = catalog?.Frontier(region);
        return frontier == null ? Array.Empty<ZergAnchor>()
            : ZergAnchors(frontier.Spots, realm, spot => catalog.Usable(spot, realm)).ToArray();
    }

    /// <summary>
    /// The zerg part of a pulse (under BattlegroupSync): each zerg whose hold is over rolls on to its next gate; then each
    /// realm's zerg forms or is topped up. A group taken from a battlegroup leaves it (why=zerg); a battlegroup that lost its
    /// lead or is down to one group ends.
    /// </summary>
    private static void PulseZergs(Dictionary<string, BattlegroupCandidate> byId, long now, IReadOnlyDictionary<eRealm, int> freeFrontierBots,
        bool scheduleOn, IReadOnlyCollection<ushort> frontierRegions, List<BattlegroupChange> changes)
    {
        foreach (BattlegroupRecord zerg in Battlegroups.Values.Where(r => r.Zerg).OrderBy(r => r.Id, StringComparer.Ordinal).ToList())
        {
            if (zerg.Anchors.Length == 0 || !ZergAnchorDue(zerg.AnchorSince, now)) continue;
            zerg.AnchorIndex = (zerg.AnchorIndex + 1) % zerg.Anchors.Length;
            zerg.AnchorSince = 0;
            changes.Add(new BattlegroupChange("moved", zerg.Id, zerg.Lead, "hold"));
            LogBattlegroup("moved", zerg, byId, zerg.Lead, "hold", now);
        }

        foreach (eRealm realm in byId.Values.Select(candidate => candidate.Realm).Distinct().OrderBy(r => r).ToList())
        {
            int free = freeFrontierBots.TryGetValue(realm, out int count) ? count : 0;
            int target = ZergTargetHeads(free, scheduleOn);
            BattlegroupRecord existing = Battlegroups.Values.FirstOrDefault(r => r.Zerg && r.Realm == realm);
            if (existing != null)
            {
                existing.Target = target;
                existing.Free = free;
                existing.ScheduleOn = scheduleOn;
            }
            var unavailable = new List<string>(BattlegroupRelinkAfter.Keys);
            unavailable.AddRange(Battlegroups.Values.Where(r => r.Zerg && r != existing).SelectMany(r => r.Members.Select(m => m.ForceId)));
            ZergPlan plan = PlanZerg(realm, byId.Values.ToList(), existing?.View, target, unavailable, frontierRegions);
            if (plan == null) continue;

            foreach (string id in plan.Joining.Append(plan.LeadForceId))
                if (BattlegroupOfForce.TryGetValue(id, out BattlegroupRecord from) && !from.Zerg)
                    TakeFromBattlegroup(from, id, byId, changes, now);

            BattlegroupRecord record = existing;
            string what = "joined";
            if (record == null)
            {
                record = new BattlegroupRecord
                {
                    Id = $"zerg-{GlobalConstants.RealmToName(realm).ToLowerInvariant()}-{++_nextZergNumber:000}",
                    Realm = realm, Region = plan.Region, FormedTick = now, Zerg = true, Target = target, Free = free,
                    ScheduleOn = scheduleOn, Anchors = AnchorsFor(realm, plan.Region),
                };
                record.Members.Add(new BattlegroupMember { ForceId = plan.LeadForceId, StayUntil = long.MaxValue, JoinedTick = now });
                Battlegroups[record.Id] = record;
                BattlegroupOfForce[plan.LeadForceId] = record;
                what = "formed";
            }
            BattlegroupCandidate lead = byId[record.Lead];
            foreach (string id in plan.Joining)
            {
                BattlegroupCandidate? self = byId.TryGetValue(id, out BattlegroupCandidate found) ? found : null;
                record.Members.Add(new BattlegroupMember
                {
                    ForceId = id, StayUntil = now + BattlegroupStay(id, now), JoinedTick = now, Arrived = ZergArrived(self, lead),
                });
                BattlegroupOfForce[id] = record;
            }
            string added = string.Join(",", plan.Joining);
            changes.Add(new BattlegroupChange(what, record.Id, added, "recruit"));
            LogBattlegroup(what, record, byId, added, "recruit", now);
        }
    }

    /// <summary>A group the zerg takes leaves its battlegroup; a battlegroup that loses its lead, or is down to one, ends.</summary>
    private static void TakeFromBattlegroup(BattlegroupRecord from, string id, Dictionary<string, BattlegroupCandidate> byId,
        List<BattlegroupChange> changes, long now)
    {
        if (from.Lead == id)
        {
            EndBattlegroup(from, BattlegroupLeave.Zerg, byId, changes, now);
            return;
        }
        BattlegroupMember member = from.Members.FirstOrDefault(m => m.ForceId == id);
        if (member == null) return;
        from.Members.Remove(member);
        BattlegroupOfForce.Remove(id);
        BattlegroupStuckReports.Remove(id);
        changes.Add(new BattlegroupChange("left", from.Id, id, Why(BattlegroupLeave.Zerg)));
        LogBattlegroup("left", from, byId, id, Why(BattlegroupLeave.Zerg), now);
        if (from.Members.Count < 2)
            EndBattlegroup(from, BattlegroupLeave.Zerg, byId, changes, now);
    }

    /// <summary>
    /// RVR_FORCE_ZERG: the realm's zerg formed, a group joined or left it, its lead rolled on to the next gate, or it ended
    /// (#599 step 3). Force ids are the warbands' session ids, never names.
    /// </summary>
    private static void LogZerg(string what, BattlegroupRecord record, Dictionary<string, BattlegroupCandidate> byId,
        string force, string why, long now)
    {
        int heads = record.Members.Sum(member => byId.TryGetValue(member.ForceId, out BattlegroupCandidate candidate) ? candidate.Heads : 0);
        int arrived = record.Members.Count(member => member.Arrived);
        // One change of a realm's zerg (formed, joined, left, moved or ended), with its groups, heads, size target and gate.
        Log.Info($"RVR_FORCE_ZERG what={what} id={record.Id} realm={GlobalConstants.RealmToName(record.Realm)} " +
                 $"region={record.Region} lead={record.Lead ?? "-"} groups={record.Members.Count} arrived={arrived} heads={heads} " +
                 $"target={record.Target} free={record.Free} schedule={(record.ScheduleOn ? "on" : "off")} " +
                 $"anchor={record.Anchor?.SpotId ?? "-"} force={force ?? "-"} why={why} minutes={Math.Max(0, (now - record.FormedTick) / 60_000)}");
    }

    // ---- reads ------------------------------------------------------------------------------------------------------

    /// <summary>The zerg this force is in (its gate now, and whether the force leads it), or null.</summary>
    public static ZergOrder ZergOrderOf(string forceId)
    {
        if (forceId == null) return null;
        lock (BattlegroupSync)
            return BattlegroupOfForce.TryGetValue(forceId, out BattlegroupRecord record) && record.Zerg
                ? new ZergOrder(record.Id, record.Anchor, record.Lead == forceId, record.Region) : null;
    }

    /// <summary>
    /// The zerg's lead reached its gate <paramref name="spotId"/>: the hold starts (once). Ignored for a force that does not
    /// lead a zerg, or a gate that is no longer the zerg's.
    /// </summary>
    public static void ReportZergAtAnchor(string forceId, string spotId, long now)
    {
        if (forceId == null || spotId == null) return;
        lock (BattlegroupSync)
            if (BattlegroupOfForce.TryGetValue(forceId, out BattlegroupRecord record) && record.Zerg && record.Lead == forceId &&
                record.Anchor?.SpotId == spotId && record.AnchorSince == 0)
                record.AnchorSince = Math.Max(1, now);
    }
}
