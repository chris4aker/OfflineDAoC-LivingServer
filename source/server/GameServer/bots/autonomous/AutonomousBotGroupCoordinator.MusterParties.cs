using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// #530 step 5b: groups inside the battleground muster. On the owner's PC (build 1.2.2 868a82d, Dun Abermenai) 618 of 619
/// battleground fight boards were a single bot: the realm's wave was a crowd of soloers, so the group fight plan (assist,
/// crowd control, peel) almost never ran at the keep. Now the solo gamebots mustering for a battleground keep (the event
/// layer's records, step 5a) are put into parties where they stand, as a battleground pick-up group formed at the rally:
/// they first fill the realm's muster parties there, and three or more left form new ones (AutonomousRvrEventLayer
/// .PlanMusterParties; the siege parties' sizes, owner 2026-10-07: "Siege armies move as full groups").
/// A muster party is a battleground trip (<see cref="BattlegroundTrip.FromMuster"/>): the members run the battleground turn
/// with their leader's part, through the wave, the rally and the storm. Each member keeps its own battleground tour; the
/// party ends when the first of them ends (or below two members, or when a player joins), and nobody gets a new goal from it.
/// Autonomous gamebots only.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    /// <summary>A bot with less than this left on its battleground tour is not put into a muster party.</summary>
    public const long MusterPartyMinimumTourMilliseconds = 5 * 60_000L;
    /// <summary>A muster party lasts at most this long (then its members may form new ones at the next muster).</summary>
    public const long MusterPartyMaximumMilliseconds = 60 * 60_000L;
    /// <summary>A free bot joins a muster party whose leader stands this close to the staging point.</summary>
    public const float MusterPartyJoinRadius = 1_500f;
    private static long _nextMusterPartyTick;

    private static void FormBattlegroundMusterParties()
    {
        long now = GameLoop.GameLoopTime;
        if (now < _nextMusterPartyTick) return;
        _nextMusterPartyTick = now + 10_000;
        List<AutonomousRvrEventLayer.MusterRoster> rosters = AutonomousRvrEventLayer.BattlegroundMusterRosters(now);
        if (rosters.Count == 0) return;
        var byId = new Dictionary<long, GameBot>();
        foreach (GameBot bot in AutonomousBotRegistry.Snapshot())
            byId.TryAdd(bot.DatabaseID, bot);
        DateTime utcNow = DateTime.UtcNow;
        foreach (AutonomousRvrEventLayer.MusterRoster roster in rosters)
            foreach (var side in roster.MemberIds.Select(id => byId.GetValueOrDefault(id)).Where(bot => bot != null)
                         .GroupBy(bot => (bot.CurrentRegionID, bot.Realm)))
            {
                if (!AutonomousBattlegroundWave.IsGathering(side.Key.CurrentRegionID, side.Key.Realm, out Vector3 staging))
                    continue;
                FormMusterParties(roster.ObjectiveId, side.Key.Realm, staging, side.ToList(), now, utcNow);
            }
    }

    /// <summary>How long the bot's own battleground tour has left, or null when it has none it can read. Pure.</summary>
    public static TimeSpan? TourRemaining(OfflineWorldBotRecord record, DateTime utcNow)
    {
        if (record == null || AutonomousObjectiveAssignments.Parse(record.ObjectiveKind) != eAutonomousObjectiveKind.Battleground ||
            !DateTime.TryParse(record.ObjectiveExpiresUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime expires))
            return null;
        return expires.ToUniversalTime() - utcNow.ToUniversalTime();
    }

    /// <summary>Whether a bot may be put into a muster party at <paramref name="staging"/>.</summary>
    private static bool FreeForMusterParty(GameBot bot, Vector3 staging, DateTime utcNow) =>
        bot is { IsAutonomousWorldBot: true, IsAlive: true, IsTemporaryGroupHelper: false } && bot.Group == null && !bot.InCombat &&
        bot.CurrentRegion != null && BattlegroundBrackets.ForLevel(bot.Level)?.RegionId == bot.CurrentRegionID &&
        AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.Battleground) &&
        !BotDutyLeases.IsSpokenForByPlayer(bot) && !bot.IsOnStableMasterRoute && !AutonomousRealmRaid.IsReserved(bot) &&
        !GameRelic.IsPlayerCarryingRelic(bot) &&
        Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), staging) <=
            AutonomousBattlegroundWave.GatheredRadius * AutonomousBattlegroundWave.GatheredRadius &&
        TourRemaining(bot.PersistentRecord, utcNow) is { } left && left.TotalMilliseconds >= MusterPartyMinimumTourMilliseconds;

    /// <summary>
    /// Puts the free bots of <paramref name="realm"/> at a battleground keep's muster into parties: first into the realm's muster parties
    /// there, then into new ones (<see cref="AutonomousRvrEventLayer.PlanMusterParties"/>). Returns how many bots were
    /// placed. Public for the RvR fight rehearsal (#520).
    /// </summary>
    public static int FormMusterParties(string objectiveId, eRealm realm, Vector3 staging, IReadOnlyList<GameBot> candidates, long now,
        DateTime utcNow)
    {
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
        {
            List<GameBot> free = (candidates ?? Array.Empty<GameBot>())
                .Where(bot => bot?.Realm == realm && FreeForMusterParty(bot, staging, utcNow))
                .Distinct()
                .OrderBy(bot => Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), staging)).ThenBy(bot => bot.DatabaseID).ToList();
            if (free.Count == 0) return 0;
            ushort region = free[0].CurrentRegionID;
            free.RemoveAll(bot => bot.CurrentRegionID != region);
            int cap = AutonomousRvrPetCap.CapFor(AutonomousRvrEventLayer.MusterPartySize, ServerProperties.Properties.RVR_GROUP_PET_CAP);
            Group[] parties = Trips.Where(pair => pair.Value.FromMuster && pair.Value.ObjectiveId == objectiveId &&
                                                  pair.Value.Leader is { IsAlive: true } leader && leader.Realm == realm &&
                                                  leader.CurrentRegionID == region &&
                                                  Vector3.DistanceSquared(new(leader.X, leader.Y, leader.Z), staging) <=
                                                      MusterPartyJoinRadius * MusterPartyJoinRadius &&
                                                  !pair.Key.GetMembersInTheGroup().Any(member => member is GamePlayer))
                .Select(pair => pair.Key).OrderBy(group => group.MemberCount).ToArray();
            AutonomousRvrEventLayer.MusterPartyPlan plan =
                AutonomousRvrEventLayer.PlanMusterParties(parties.Select(group => (int)group.MemberCount).ToArray(), free.Count);
            int placed = 0;
            for (int i = 0; i < parties.Length; i++)
            {
                if (plan.Joins[i] == 0) continue;
                Group group = parties[i];
                BattlegroundTrip trip = Trips[group];
                GameBot[] joining = AutonomousRvrPetCap.Pick(free, plan.Joins[i],
                    BotMembers(group).Count(AutonomousRvrPetCap.IsPetClass), cap, AutonomousRvrPetCap.IsPetClass);
                var joined = new List<GameBot>();
                foreach (GameBot bot in joining)
                    if (group.AddMember(bot))
                    {
                        free.Remove(bot);
                        joined.Add(bot);
                        ClaimMusterParty(bot, trip, group);
                        trip.ExpiresTick = Math.Min(trip.ExpiresTick, MusterPartyEnds(new[] { bot }, now, utcNow));
                    }
                if (joined.Count == 0) continue;
                placed += joined.Count;
                GameBot[] members = BotMembers(group);
                WriteTripMetadata(trip, members);
                LogMusterParty("joined", trip, members, joined);
            }
            foreach (int size in plan.NewParties)
            {
                if (free.Count < AutonomousRvrEventLayer.MusterPartyMinimum) break;
                GameBot leader = free[0];
                GameBot[] mates = AutonomousRvrPetCap.Pick(free.Skip(1)
                        .Where(bot => Vector3.DistanceSquared(new(bot.X, bot.Y, bot.Z), new(leader.X, leader.Y, leader.Z)) <=
                                      MusterPartyJoinRadius * MusterPartyJoinRadius)
                        .OrderBy(bot => bot.GetDistanceTo(leader)),
                    size - 1, AutonomousRvrPetCap.IsPetClass(leader) ? 1 : 0, cap, AutonomousRvrPetCap.IsPetClass);
                free.Remove(leader);
                if (mates.Length + 1 < AutonomousRvrEventLayer.MusterPartyMinimum) continue;
                GameBot[] members = new[] { leader }.Concat(mates).ToArray();
                foreach (GameBot mate in mates) free.Remove(mate);
                if (TryFormMusterParty(objectiveId, members, now, utcNow) is { } formed)
                    placed += formed;
            }
            return placed;
        }
    }

    /// <summary>When a muster party of these members ends: at the first member's tour end, at most an hour away.</summary>
    private static long MusterPartyEnds(IEnumerable<GameBot> members, long now, DateTime utcNow)
    {
        double left = members.Select(member => TourRemaining(member.PersistentRecord, utcNow)?.TotalMilliseconds ?? 0)
            .DefaultIfEmpty(0).Min();
        return now + (long)Math.Clamp(left, 0, MusterPartyMaximumMilliseconds);
    }

    private static int? TryFormMusterParty(string objectiveId, GameBot[] members, long now, DateTime utcNow)
    {
        GameBot leader = members[0];
        var group = new Group(leader);
        GroupMgr.AddGroup(group);
        if (!group.AddMember(leader))
        {
            GroupMgr.RemoveGroup(group);
            return null;
        }
        foreach (GameBot member in members.Skip(1)) group.AddMember(member);
        GameBot[] joined = BotMembers(group);
        if (joined.Length < 2)
        {
            group.DisbandGroup();
            return null;
        }
        BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.ForRegion(leader.CurrentRegionID);
        var trip = new BattlegroundTrip
        {
            Id = $"bgmuster-{GlobalConstants.RealmToName(leader.Realm).ToLowerInvariant()}-{RuntimeGroupToken}-{++_nextTripNumber:000}",
            Leader = leader, BracketRegion = leader.CurrentRegionID, BracketName = bracket?.Name ?? "the battleground",
            ExpiresTick = MusterPartyEnds(joined, now, utcNow), FromMuster = true, ObjectiveId = objectiveId,
        };
        Trips[group] = trip;
        foreach (GameBot member in joined)
            ClaimMusterParty(member, trip, group);
        WriteTripMetadata(trip, joined);
        LogMusterParty("formed", trip, joined, joined);
        return joined.Length;
    }

    private static void ClaimMusterParty(GameBot member, BattlegroundTrip trip, Group group)
    {
        if (BotDutyLeases.WouldGrant(member, eDutyLeaseKind.BotGroupSession, trip.Id))
            BotDutyLeases.Claim(member, eDutyLeaseKind.BotGroupSession, trip.Id, DutyLeaseReason.GroupSession, "Battleground muster party", group);
    }

    /// <summary>
    /// BG_MUSTER_PARTY: a muster party formed (or bots joined one) at a battleground keep's muster (#530 step 5b).
    /// </summary>
    private static void LogMusterParty(string what, BattlegroundTrip trip, GameBot[] members, IReadOnlyCollection<GameBot> added)
    {
        GameBot leader = trip.Leader;
        Log.Info($"BG_MUSTER_PARTY what={what} group={trip.Id} keep={trip.ObjectiveId} battleground={trip.BracketName.Replace(' ', '_')} " +
                 $"realm={GlobalConstants.RealmToName(leader.Realm)} size={members.Length} added={added.Count} " +
                 $"minutes={Math.Max(0, (trip.ExpiresTick - GameLoop.GameLoopTime) / 60_000)} " +
                 $"members=\"{string.Join(",", members.Select(member => member.Name))}\"");
    }

    /// <summary>The bot's muster party's id when it is in one, else null (tests and the controller's force id).</summary>
    public static string MusterPartyOf(GameBot bot)
    {
        if (bot?.Group == null) return null;
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
            return Trips.TryGetValue(bot.Group, out BattlegroundTrip trip) && trip.FromMuster ? trip.Id : null;
    }
}
