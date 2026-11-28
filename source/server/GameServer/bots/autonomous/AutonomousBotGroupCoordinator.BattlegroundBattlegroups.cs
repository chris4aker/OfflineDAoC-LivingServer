using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// #599 step 4: feeds the event layer's battleground battlegroups (AutonomousRvrEventLayer.PulseBattlegroundBattlegroups)
/// from the battleground parties (the muster parties of #530 step 5b and the group trips of #598 C), every ten seconds,
/// and keeps the party id to group map RealmWarbandSupport reads. The trips are read under the coordinator's lock; the
/// event layer is asked outside it. Frontier warbands (RvR sessions) are never read here: they have their own battlegroups.
/// Autonomous gamebots only: a party a player joined is passed as such and never taken.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    private static long _nextBattlegroundBattlegroupTick;
    private static Dictionary<string, Group> _battlegroundBattlegroupGroups = new();

    /// <summary>One battleground battlegroup pulse. <paramref name="force"/>: run now (the RvR fight rehearsal, #520).</summary>
    public static List<AutonomousRvrEventLayer.BattlegroupChange> PulseBattlegroundBattlegroups(long now, bool force = false)
    {
        if (!force && now < Interlocked.Read(ref _nextBattlegroundBattlegroupTick))
            return null;
        Interlocked.Exchange(ref _nextBattlegroundBattlegroupTick, now + 10_000);
        var parties = new List<(string Id, Group Group, GameBot Leader)>();
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
            foreach (KeyValuePair<Group, BattlegroundTrip> pair in Trips)
                if (pair.Value.Leader is { } leader && !string.IsNullOrEmpty(pair.Value.Id) &&
                    BattlegroundBrackets.IsBattlegroundRegion(leader.CurrentRegionID))
                    parties.Add((pair.Value.Id, pair.Key, leader));
        var candidates = new List<AutonomousRvrEventLayer.BattlegroundPartyCandidate>(parties.Count);
        var groups = new Dictionary<string, Group>(parties.Count);
        foreach (var party in parties)
        {
            List<GameLiving> members = party.Group.GetMembersInTheGroup();
            candidates.Add(new(party.Id, party.Leader.Realm, party.Leader.CurrentRegionID,
                AutonomousRvrEventLayer.BattlegroundWaveObjective(party.Id, now), members.Count(member => member is GameBot),
                members.Any(member => member is GamePlayer)));
            groups[party.Id] = party.Group;
        }
        List<AutonomousRvrEventLayer.BattlegroupChange> changes = AutonomousRvrEventLayer.PulseBattlegroundBattlegroups(candidates, now);
        Volatile.Write(ref _battlegroundBattlegroupGroups, groups);
        return changes;
    }

    /// <summary>The group behind a battleground party's id, as of the last battleground battlegroup pulse, or null.</summary>
    public static Group BattlegroundBattlegroupGroup(string partyId) =>
        partyId != null && Volatile.Read(ref _battlegroundBattlegroupGroups).TryGetValue(partyId, out Group group) ? group : null;

    /// <summary>The bot's battleground party id (a muster party or a group trip), or null (tests and the rehearsal).</summary>
    public static string BattlegroundPartyOf(GameBot bot)
    {
        if (bot?.Group == null) return null;
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
            return Trips.TryGetValue(bot.Group, out BattlegroundTrip trip) ? trip.Id : null;
    }

    /// <summary>Tests.</summary>
    public static void ResetBattlegroundBattlegroups()
    {
        Interlocked.Exchange(ref _nextBattlegroundBattlegroupTick, 0);
        Volatile.Write(ref _battlegroundBattlegroupGroups, new Dictionary<string, Group>());
        AutonomousRvrEventLayer.ResetBattlegroundBattlegroups();
    }
}
