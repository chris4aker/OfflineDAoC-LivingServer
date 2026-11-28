using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// #599 step 4: a realm's battleground parties at one keep act as one battlegroup. Since #530 step 5b the solo gamebots at a
/// battleground keep's muster form parties of 3-8, but each party stayed on its own: its healers healed only it, and a
/// spotter's call reached only it. Period battleground pick-up groups were tied together by one leader's calls (the
/// battlegroup, /bg and /bc from 1.66; docs/rvr-audit-1.2.3/reference.md §2.1: groups were the spine, "3fg+ add onto the
/// fight"). Now the realm's parties whose force record says they muster for, or attack, one battleground keep
/// (<see cref="InBattlegroundWave"/>) form one battlegroup there, <c>bgbg-&lt;realm&gt;-&lt;keep&gt;</c>: one support roster
/// (RealmWarbandSupport), so cross-group heals and buffs, the inc relay and the size-up reach across the parties, and one
/// lead party whose call the others take in the open (step 2). Nothing moves differently: the wave, the rally and the storm
/// already move the realm together. The rules are pure (<see cref="PlanBattlegroundBattlegroups"/>,
/// <see cref="BattlegroundPartyLeaveReason"/>); the record is kept here and fed by the coordinator
/// (<c>AutonomousBotGroupCoordinator.PulseBattlegroundBattlegroups</c>). Autonomous gamebots only: a party that hosts a
/// player is never taken, and companions are never in a battleground party.
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    public const string BattlegroundBattlegroupPrefix = "bgbg-";
    /// <summary>A party with fewer bots than this is not taken (a party falls apart below two).</summary>
    public const int BattlegroundBattlegroupMinimumParty = 2;

    /// <summary>
    /// One battleground party as the coordinator sees it this pulse: its trip id, realm, region, the battleground keep
    /// objective its record says it musters for or attacks (null: none, see <see cref="InBattlegroundWave"/>), its bots and
    /// whether a player is in it.
    /// </summary>
    public readonly record struct BattlegroundPartyCandidate(string PartyId, eRealm Realm, ushort Region, string ObjectiveId, int Heads,
        bool HostsPlayer);

    /// <summary>A battleground battlegroup: its id, realm, region, keep objective, lead party and every party in it (the lead first).</summary>
    public sealed record BattlegroundBattlegroupView(string Id, eRealm Realm, ushort Region, string ObjectiveId, string LeadPartyId,
        IReadOnlyList<string> Members);

    /// <summary>
    /// Whether a battleground party's force record puts it in its realm's wave: mustering for or attacking a battleground
    /// keep (#530 step 5a's roles), written within <see cref="MusterRosterFreshMilliseconds"/>. Holding the walls, roaming,
    /// a frontier objective or a stale record does not. Pure.
    /// </summary>
    public static bool InBattlegroundWave(ForceIntent intent, long writtenTick, long nowTick) =>
        intent != null && intent.Role is ForceRole.Muster or ForceRole.Attacker && IsBattlegroundObjective(intent.ObjectiveId) &&
        nowTick - writtenTick <= MusterRosterFreshMilliseconds;

    /// <summary>The battleground keep objective the party's record (<c>bg:&lt;party id&gt;</c>) says it is in the wave for, or null.</summary>
    public static string BattlegroundWaveObjective(string partyId, long nowTick)
    {
        if (string.IsNullOrEmpty(partyId) || !ForceIntents.TryGetValue("bg:" + partyId, out IntentRecord record))
            return null;
        ForceIntent intent;
        long written;
        lock (record)
        {
            intent = record.Intent;
            written = record.LastWriteTick;
        }
        return InBattlegroundWave(intent, written, nowTick) ? intent.ObjectiveId : null;
    }

    /// <summary>Whether a party may be in a battleground battlegroup: two bots or more, no player, in its realm's wave. Pure.</summary>
    public static bool CanJoinBattlegroundBattlegroup(BattlegroundPartyCandidate party) =>
        !string.IsNullOrEmpty(party.PartyId) && party.Heads >= BattlegroundBattlegroupMinimumParty && !party.HostsPlayer &&
        IsBattlegroundObjective(party.ObjectiveId);

    /// <summary>The battlegroup id of a realm at a battleground keep, <c>bgbg-albion-12</c>: one per realm and keep. Pure.</summary>
    public static string BattlegroundBattlegroupId(eRealm realm, string objectiveId) =>
        BattlegroundBattlegroupPrefix + GlobalConstants.RealmToName(realm).ToLowerInvariant() + "-" +
        (IsBattlegroundObjective(objectiveId) ? objectiveId.Substring(BattlegroundObjectivePrefix.Length) : "none");

    /// <summary>
    /// The battleground battlegroups this pulse: the realm's eligible parties at one keep (same realm, region and objective)
    /// form one when there are two or more. The lead is the current one (<paramref name="currentLeads"/>, by battlegroup id)
    /// while it is still in; otherwise the biggest party, then the lowest id. There is no cap: the realm's wave at its keep
    /// is the bound. Pure.
    /// </summary>
    public static List<BattlegroundBattlegroupView> PlanBattlegroundBattlegroups(IReadOnlyList<BattlegroundPartyCandidate> candidates,
        IReadOnlyDictionary<string, string> currentLeads = null)
    {
        var byId = new Dictionary<string, BattlegroundPartyCandidate>(StringComparer.Ordinal);
        foreach (BattlegroundPartyCandidate candidate in candidates ?? Array.Empty<BattlegroundPartyCandidate>())
            if (!string.IsNullOrEmpty(candidate.PartyId))
                byId.TryAdd(candidate.PartyId, candidate);
        var views = new List<BattlegroundBattlegroupView>();
        foreach (var side in byId.Values.Where(CanJoinBattlegroundBattlegroup).GroupBy(party => (party.Realm, party.Region, party.ObjectiveId)))
        {
            List<BattlegroundPartyCandidate> parties = side.OrderByDescending(party => party.Heads)
                .ThenBy(party => party.PartyId, StringComparer.Ordinal).ToList();
            if (parties.Count < 2)
                continue;
            string id = BattlegroundBattlegroupId(side.Key.Realm, side.Key.ObjectiveId);
            string lead = currentLeads != null && currentLeads.TryGetValue(id, out string held) &&
                          parties.Any(party => party.PartyId == held)
                ? held
                : parties[0].PartyId;
            var members = new List<string> { lead };
            members.AddRange(parties.Select(party => party.PartyId).Where(party => party != lead));
            views.Add(new BattlegroundBattlegroupView(id, side.Key.Realm, side.Key.Region, side.Key.ObjectiveId, lead, members));
        }
        return views.OrderBy(view => view.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Why a party is no longer in its battlegroup (<paramref name="battlegroup"/>), or null while it still is:
    /// <c>gone</c> (the party ended), <c>player</c> (a player joined), <c>toosmall</c> (one bot left), <c>region</c> (it left
    /// the battleground or changed realm), <c>wave</c> (its record no longer musters for or attacks this keep). Pure.
    /// </summary>
    public static string BattlegroundPartyLeaveReason(BattlegroundPartyCandidate? party, BattlegroundBattlegroupView battlegroup)
    {
        if (party is not { } self)
            return "gone";
        if (self.HostsPlayer)
            return "player";
        if (self.Heads < BattlegroundBattlegroupMinimumParty)
            return "toosmall";
        if (battlegroup != null && (self.Region != battlegroup.Region || self.Realm != battlegroup.Realm))
            return "region";
        if (battlegroup != null && !string.Equals(self.ObjectiveId, battlegroup.ObjectiveId, StringComparison.Ordinal))
            return "wave";
        return null;
    }

    // ---- the record ---------------------------------------------------------------------------------------------------

    private sealed class BattlegroundBattlegroupRecord
    {
        public BattlegroundBattlegroupView View;
        public long FormedTick;
    }

    private static readonly object BattlegroundBattlegroupSync = new();
    private static readonly Dictionary<string, BattlegroundBattlegroupRecord> BattlegroundBattlegroups = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> BattlegroundBattlegroupOfParty = new(StringComparer.Ordinal);

    /// <summary>
    /// One pulse of the battleground battlegroups: the plan (<see cref="PlanBattlegroundBattlegroups"/>) replaces the record,
    /// and each change is logged once as RVR_FORCE_BG_BATTLEGROUP: formed, joined, left (with the party's reason), lead (the
    /// lead party left and the next took over) or ended (down to one party). Returns the changes (tests).
    /// </summary>
    public static List<BattlegroupChange> PulseBattlegroundBattlegroups(IReadOnlyList<BattlegroundPartyCandidate> candidates, long now)
    {
        var changes = new List<BattlegroupChange>();
        var byId = new Dictionary<string, BattlegroundPartyCandidate>(StringComparer.Ordinal);
        foreach (BattlegroundPartyCandidate candidate in candidates ?? Array.Empty<BattlegroundPartyCandidate>())
            if (!string.IsNullOrEmpty(candidate.PartyId))
                byId.TryAdd(candidate.PartyId, candidate);
        BattlegroundPartyCandidate? Find(string id) => id != null && byId.TryGetValue(id, out BattlegroundPartyCandidate found) ? found : null;
        lock (BattlegroundBattlegroupSync)
        {
            var leads = BattlegroundBattlegroups.ToDictionary(pair => pair.Key, pair => pair.Value.View.LeadPartyId, StringComparer.Ordinal);
            Dictionary<string, BattlegroundBattlegroupView> planned = PlanBattlegroundBattlegroups(byId.Values.ToList(), leads)
                .ToDictionary(view => view.Id, StringComparer.Ordinal);

            foreach (BattlegroundBattlegroupRecord record in BattlegroundBattlegroups.Values.OrderBy(r => r.View.Id, StringComparer.Ordinal).ToList())
            {
                BattlegroundBattlegroupView old = record.View;
                planned.TryGetValue(old.Id, out BattlegroundBattlegroupView next);
                foreach (string party in old.Members.Where(party => next == null || !next.Members.Contains(party)))
                {
                    string why = BattlegroundPartyLeaveReason(Find(party), old);
                    // A party still in the wave when its battlegroup ends (down to one) ends with it: no "left" row of its own.
                    if (why == null && next == null)
                        continue;
                    why ??= "wave";
                    changes.Add(new BattlegroupChange("left", old.Id, party, why));
                    LogBattlegroundBattlegroup("left", old, byId, party, why, record.FormedTick, now);
                }
                if (next == null)
                {
                    BattlegroundBattlegroups.Remove(old.Id);
                    changes.Add(new BattlegroupChange("ended", old.Id, old.LeadPartyId, "toosmall"));
                    LogBattlegroundBattlegroup("ended", old, byId, old.LeadPartyId, "toosmall", record.FormedTick, now);
                    continue;
                }
                record.View = next;
                string joined = string.Join(",", next.Members.Where(party => !old.Members.Contains(party)));
                if (joined.Length > 0)
                {
                    changes.Add(new BattlegroupChange("joined", next.Id, joined, "wave"));
                    LogBattlegroundBattlegroup("joined", next, byId, joined, "wave", record.FormedTick, now);
                }
                if (next.LeadPartyId != old.LeadPartyId)
                {
                    string why = BattlegroundPartyLeaveReason(Find(old.LeadPartyId), old) ?? "handover";
                    changes.Add(new BattlegroupChange("lead", next.Id, next.LeadPartyId, why));
                    LogBattlegroundBattlegroup("lead", next, byId, next.LeadPartyId, why, record.FormedTick, now);
                }
            }
            foreach (BattlegroundBattlegroupView view in planned.Values.OrderBy(v => v.Id, StringComparer.Ordinal))
            {
                if (BattlegroundBattlegroups.ContainsKey(view.Id))
                    continue;
                BattlegroundBattlegroups[view.Id] = new BattlegroundBattlegroupRecord { View = view, FormedTick = now };
                string added = string.Join(",", view.Members);
                changes.Add(new BattlegroupChange("formed", view.Id, added, "wave"));
                LogBattlegroundBattlegroup("formed", view, byId, added, "wave", now, now);
            }
            BattlegroundBattlegroupOfParty.Clear();
            foreach (BattlegroundBattlegroupRecord record in BattlegroundBattlegroups.Values)
                foreach (string party in record.View.Members)
                    BattlegroundBattlegroupOfParty[party] = record.View.Id;
        }
        return changes;
    }

    /// <summary>
    /// RVR_FORCE_BG_BATTLEGROUP: a realm's battleground battlegroup at one keep formed, a party joined or left it, its lead
    /// changed, or it ended (#599 step 4). Party ids are the trips' ids, never names.
    /// </summary>
    private static void LogBattlegroundBattlegroup(string what, BattlegroundBattlegroupView view,
        Dictionary<string, BattlegroundPartyCandidate> byId, string force, string why, long formedTick, long now)
    {
        if (!Log.IsInfoEnabled) return;
        int heads = view.Members.Sum(party => byId.TryGetValue(party, out BattlegroundPartyCandidate candidate) ? candidate.Heads : 0);
        // One change of a realm's battleground battlegroup at a keep (formed, joined, left, lead or ended), with its parties and heads.
        Log.Info($"RVR_FORCE_BG_BATTLEGROUP what={what} id={view.Id} realm={GlobalConstants.RealmToName(view.Realm)} " +
                 $"region={view.Region} keep={view.ObjectiveId ?? "-"} lead={view.LeadPartyId ?? "-"} groups={view.Members.Count} " +
                 $"heads={heads} force={force ?? "-"} why={why} minutes={Math.Max(0, (now - formedTick) / 60_000)}");
    }

    // ---- reads ------------------------------------------------------------------------------------------------------

    /// <summary>Every battleground battlegroup now (RealmWarbandSupport's rosters).</summary>
    public static List<BattlegroundBattlegroupView> BattlegroundBattlegroupsNow()
    {
        lock (BattlegroundBattlegroupSync)
            return BattlegroundBattlegroups.Values.Select(record => record.View).OrderBy(view => view.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>The battleground battlegroup this party (its trip id) is in, or null.</summary>
    public static BattlegroundBattlegroupView BattlegroundBattlegroupOf(string partyId)
    {
        if (partyId == null) return null;
        lock (BattlegroundBattlegroupSync)
            return BattlegroundBattlegroupOfParty.TryGetValue(partyId, out string id) &&
                   BattlegroundBattlegroups.TryGetValue(id, out BattlegroundBattlegroupRecord record)
                ? record.View
                : null;
    }

    /// <summary>Tests.</summary>
    public static void ResetBattlegroundBattlegroups()
    {
        lock (BattlegroundBattlegroupSync)
        {
            BattlegroundBattlegroups.Clear();
            BattlegroundBattlegroupOfParty.Clear();
        }
    }
}
