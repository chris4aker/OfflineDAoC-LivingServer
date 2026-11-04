using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace DOL.GS;

/// <summary>
/// The Epic Special Forces: each realm's elite guild of level-50 gamebots in endgame gear, who answer when an epic
/// expedition is in trouble. Owner 2026-10-19: scaling the encounters down "removes the wow factor"; keep the epic
/// bosses at full strength and the stakes high, but give the realm "better than not" odds. So after a wipe the
/// raid regroups and up to <see cref="MaximumMembers"/> of them join it for the retry, and an expedition beaten
/// here the last two times brings them from the start.
///
/// They are the owner's one exception to gamebots earning their armor ("they are a special exception"), and "as
/// the rest of the realm progresses, they are needed less": every win at that dungeon sends one party (8) fewer
/// next time, every defeat one more, from the dungeon's last <see cref="HistoryDepth"/> results in the event
/// ledger (the launcher's Herald). A realm that keeps winning stops calling them.
///
/// Each member is made for one raid and lives in memory only: never saved, never in the roster or the population
/// count, no loot or coin from the raid (that stays with the realm's own raiders), and removed when the raid ends.
/// A crash or restart leaves nothing behind.
///
/// Since 2026-10-30 dragon raids call them too, on the same terms: a wipe at the dragon (RealmRaidDragonFight) asks
/// for the call-up sized by that dragon's own record, they arrive at the raid's muster hub and walk to the staging
/// ring like any late party, and two defeats in a row at the dragon bring them from the start.
/// </summary>
public static class AutonomousEpicSpecialForces
{
    public const string GuildName = "Epic Special Forces";
    public const int PartySize = 8;
    public const int MaximumMembers = 24;
    public const int MaximumParties = MaximumMembers / PartySize;
    public const int HistoryDepth = 6;
    /// <summary>Far above any roster id (offline_world_bots counts up from 1), so the two never meet.</summary>
    public const long FirstBotId = 1_900_000_000;
    private static long _lastBotId = FirstBotId;

    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(AutonomousEpicSpecialForces));
    private static readonly object Sync = new();
    private sealed record Call(string RaidId, string RaidName, eRealm Realm, int Members, string Reason);
    private static readonly Queue<Call> Calls = new();
    private static readonly Queue<string> Dismissals = new();
    // Raids that have had their call this run (one call per raid).
    private static readonly HashSet<string> Answered = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, List<GameBot>> Members = new(StringComparer.Ordinal);
    private static readonly HashSet<GameBot> SeenOutOfWorld = new();
    // Each dungeon's last results, oldest first; loaded once from the ledger off the game loop.
    private static Dictionary<string, List<string>> _history;
    private static readonly List<(string Id, string Outcome)> EndedBeforeLoad = new();
    private static int _loading;
    private static long _nextCleanup;

    /// <summary>One party per realm, in the shape every expedition party takes: tank, healers, support, damage.</summary>
    public static readonly IReadOnlyDictionary<eRealm, eCharacterClass[]> PartyClasses = new Dictionary<eRealm, eCharacterClass[]>
    {
        [eRealm.Albion] = [eCharacterClass.Armsman, eCharacterClass.Paladin, eCharacterClass.Cleric, eCharacterClass.Cleric,
            eCharacterClass.Friar, eCharacterClass.Minstrel, eCharacterClass.Wizard, eCharacterClass.Mercenary],
        [eRealm.Midgard] = [eCharacterClass.Warrior, eCharacterClass.Thane, eCharacterClass.Healer, eCharacterClass.Healer,
            eCharacterClass.Shaman, eCharacterClass.Skald, eCharacterClass.Runemaster, eCharacterClass.Berserker],
        [eRealm.Hibernia] = [eCharacterClass.Hero, eCharacterClass.Champion, eCharacterClass.Druid, eCharacterClass.Druid,
            eCharacterClass.Bard, eCharacterClass.Warden, eCharacterClass.Eldritch, eCharacterClass.Blademaster],
    };

    public static bool IsWin(string outcome) => outcome == "Boss defeated";
    /// <summary>Beaten by the fight: defeated at an encounter, or still fighting when the four hours ran out.</summary>
    public static bool IsLoss(string outcome) => outcome is "Defeated" or "Timed out";

    /// <summary>Parties the realm sends (0-3): three with no record, one fewer per win, one more per loss.</summary>
    public static int CallUpParties(IEnumerable<string> outcomesOldestFirst)
    {
        int parties = MaximumParties;
        foreach (string outcome in outcomesOldestFirst ?? [])
        {
            if (IsWin(outcome)) parties = Math.Max(0, parties - 1);
            else if (IsLoss(outcome)) parties = Math.Min(MaximumParties, parties + 1);
        }
        return parties;
    }

    public static int CallUpSize(IEnumerable<string> outcomesOldestFirst) => CallUpParties(outcomesOldestFirst) * PartySize;

    /// <summary>The last two counted results (wins and losses) at this dungeon were both losses.</summary>
    public static bool ComesFromStart(IEnumerable<string> outcomesOldestFirst)
    {
        string[] last = (outcomesOldestFirst ?? []).Where(o => IsWin(o) || IsLoss(o)).TakeLast(2).ToArray();
        return last.Length == 2 && last.All(IsLoss);
    }

    public static bool ComesFromStart(string raidId) => ComesFromStart(History(raidId));

    private static List<string> History(string raidId)
    {
        lock (Sync) return _history?.GetValueOrDefault(raidId)?.ToList() ?? [];
    }

    /// <summary>Asks for the raid's call-up; the members arrive on the next coordinator tick. Safe inside the raid lock.</summary>
    public static void Request(string raidId, string raidName, eRealm realm, string reason)
    {
        List<string> history = History(raidId);
        int members = CallUpSize(history);
        lock (Sync)
        {
            if (!Answered.Add(raidId)) return;
            if (members == 0)
            {
                if (Log.IsInfoEnabled)
                    Log.Info($"REALM_RAID_SPECIAL_FORCES_NOT_NEEDED event={raidId} reason=\"{reason}\" history=\"{string.Join(",", history)}\"");
                return;
            }
            Calls.Enqueue(new(raidId, raidName, realm, members, reason));
        }
    }

    /// <summary>The raid ended: its result joins the dungeon's record and its Special Forces go home.</summary>
    public static void RaidEnded(string raidId, string outcome)
    {
        lock (Sync)
        {
            Answered.Remove(raidId);
            Dismissals.Enqueue(raidId);
            if (_history == null) EndedBeforeLoad.Add((raidId, outcome));
            else Append(_history, raidId, outcome);
        }
    }

    private static void Append(Dictionary<string, List<string>> history, string raidId, string outcome)
    {
        if (!history.TryGetValue(raidId, out var list)) history[raidId] = list = new();
        list.Add(outcome);
        if (list.Count > HistoryDepth) list.RemoveRange(0, list.Count - HistoryDepth);
    }

    private static void EnsureHistoryLoading()
    {
        if (Interlocked.Exchange(ref _loading, 1) != 0) return;
        string[] ids = AutonomousRealmRaid.Definitions.Select(d => d.Id).ToArray();
        Task.Run(() =>
        {
            var loaded = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (string id in ids)
                loaded[id] = RealmEventRecords.FinishedOutcomes(id, HistoryDepth);
            lock (Sync)
            {
                foreach (var (id, outcome) in EndedBeforeLoad) Append(loaded, id, outcome);
                EndedBeforeLoad.Clear();
                _history = loaded;
            }
        });
    }

    /// <summary>Game loop, coordinator phase, outside the raid and coordinator locks: arrivals and departures.</summary>
    public static void Pulse(long now)
    {
        EnsureHistoryLoading();
        while (true)
        {
            string dismissed;
            lock (Sync) { if (!Dismissals.TryDequeue(out dismissed)) break; }
            Dismiss(dismissed, "the raid ended");
        }
        while (true)
        {
            Call call;
            lock (Sync) { if (!Calls.TryDequeue(out call)) break; }
            Arrive(call);
        }
        if (now < _nextCleanup) return;
        _nextCleanup = now + 10_000;
        // Anyone no longer part of a running raid leaves (a broken-up party, a member taken out of the world).
        foreach (var (raidId, members) in Snapshot())
        {
            if (!AutonomousRealmRaid.IsRunning(raidId)) { Dismiss(raidId, "the raid is no longer running"); continue; }
            foreach (GameBot bot in members)
            {
                bool outOfWorld = bot.ObjectState != GameObject.eObjectState.Active;
                bool gone = bot.ObjectState == GameObject.eObjectState.Deleted || bot.Group == null ||
                            AutonomousRealmRaid.GetView(bot.Group) == null;
                lock (Sync)
                {
                    if (outOfWorld && !SeenOutOfWorld.Add(bot)) gone = true; // out of the world on two checks running
                    else if (!outOfWorld) SeenOutOfWorld.Remove(bot);
                }
                if (gone) Remove(raidId, bot);
            }
        }
    }

    private static (string RaidId, GameBot[] Members)[] Snapshot()
    {
        lock (Sync) return Members.Select(p => (p.Key, p.Value.ToArray())).ToArray();
    }

    public static int Count(string raidId)
    {
        lock (Sync) return Members.GetValueOrDefault(raidId)?.Count ?? 0;
    }

    private static void Arrive(Call call)
    {
        if (!AutonomousRealmRaid.TryGetSpecialForcesPost(call.RaidId, out ushort region, out Vector3 post) ||
            !PartyClasses.TryGetValue(call.Realm, out eCharacterClass[] classes))
        {
            Log.Warn($"REALM_RAID_SPECIAL_FORCES_FAILED event={call.RaidId} reason=\"the raid has no muster hub\"");
            return;
        }
        var names = new HashSet<string>(AutonomousBotRegistry.Snapshot().Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
        int joined = 0;
        for (int party = 0; party < call.Members / PartySize; party++)
        {
            GameBot[] members = classes.Select((c, slot) => Create(call.Realm, c, region, post, party, slot, names)).ToArray();
            if (members.Any(b => b == null))
            {
                foreach (GameBot made in members.Where(b => b != null)) made.Delete();
                Log.Warn($"REALM_RAID_SPECIAL_FORCES_FAILED event={call.RaidId} party={party + 1} reason=\"a member could not enter the world\"");
                continue;
            }
            foreach (GameBot bot in members) AutonomousRealmRaid.ReserveSpecialForce(bot, call.RaidId);
            if (!AutonomousBotGroupCoordinator.FormForcedRaidParty(call.RaidId, members))
            {
                foreach (GameBot bot in members) bot.Delete();
                Log.Warn($"REALM_RAID_SPECIAL_FORCES_FAILED event={call.RaidId} party={party + 1} reason=\"the raid did not accept the party\"");
                continue;
            }
            lock (Sync)
            {
                if (!Members.TryGetValue(call.RaidId, out var list)) Members[call.RaidId] = list = new();
                list.AddRange(members);
            }
            joined += members.Length;
        }
        Log.Info($"REALM_RAID_SPECIAL_FORCES_ARRIVED event={call.RaidId} realm={call.Realm} members={joined} asked={call.Members} " +
                 $"reason=\"{call.Reason}\" history=\"{string.Join(",", History(call.RaidId))}\"");
        if (joined == 0) return;
        GameWideAnnouncements.Queue(AnnouncementKind.PveRealmEvent,
            $"The {GuildName} answer {GlobalConstants.RealmToName(call.Realm)}'s call at {call.RaidName}!");
        RealmEventNotices.Queue(call.RaidId, call.Realm,
            $"{joined} of the {GuildName} have arrived at the hub and are marching to join the {call.RaidName} expedition.");
    }

    private static GameBot Create(eRealm realm, eCharacterClass characterClass, ushort region, Vector3 post, int party, int slot, ISet<string> names)
    {
        try
        {
            eGender gender = Random.Shared.Next(2) == 0 ? eGender.Male : eGender.Female;
            var identity = AutonomousBotIdentityGenerator.GenerateForClass(realm, gender, characterClass, names);
            names.Add(identity.Name);
            // A small circle around the hub, one party to a ring.
            double angle = (party * PartySize + slot) * Math.PI * 2 / MaximumMembers;
            int x = (int)(post.X + Math.Cos(angle) * (80 + 50 * party)), y = (int)(post.Y + Math.Sin(angle) * (80 + 50 * party)), z = (int)post.Z;
            string now = DateTime.UtcNow.ToString("O");
            var record = new OfflineWorldBotRecord
            {
                BotId = Interlocked.Increment(ref _lastBotId), Name = identity.Name, Realm = (int)realm,
                ClassId = (int)characterClass, RaceId = (int)identity.Race, RaceName = identity.Race.ToString(),
                Gender = (int)gender, Level = 50, Experience = 169_999_999_950L, IsOnline = true, IsAlive = true,
                Activity = GuildName, CurrentGoal = "Reinforcing the realm's expedition", LastUpdateUtc = now,
                RegionId = region, X = x, Y = y, Z = z, BindRegionId = region, BindX = x, BindY = y, BindZ = z,
            };
            GameBot bot = GameBot.CreateEpicSpecialForce(record);
            bot.SynchronizePositionForLoginValidation();
            AutonomousStuckWatchdog.RepairInvalidLoginPosition(bot); // off the mesh beside the hub: onto it
            if (!bot.AddToWorld()) { bot.Delete(); return null; }
            return bot;
        }
        catch (Exception exception)
        {
            Log.Error($"Could not create an {GuildName} {characterClass} for {realm}", exception);
            return null;
        }
    }

    private static void Dismiss(string raidId, string why)
    {
        GameBot[] members;
        lock (Sync)
        {
            if (!Members.Remove(raidId, out var list)) return;
            members = list.ToArray();
            foreach (GameBot bot in members) SeenOutOfWorld.Remove(bot);
        }
        foreach (GameBot bot in members) bot.Delete();
        if (Log.IsInfoEnabled)
            Log.Info($"REALM_RAID_SPECIAL_FORCES_LEFT event={raidId} members={members.Length} reason=\"{why}\"");
    }

    private static void Remove(string raidId, GameBot bot)
    {
        lock (Sync)
        {
            if (Members.TryGetValue(raidId, out var list)) list.Remove(bot);
            SeenOutOfWorld.Remove(bot);
        }
        bot.Delete();
    }

    /// <summary>Tests only.</summary>
    public static void ResetForTests()
    {
        lock (Sync)
        {
            Calls.Clear(); Dismissals.Clear(); Answered.Clear(); Members.Clear(); SeenOutOfWorld.Clear();
            _history = new(StringComparer.Ordinal); EndedBeforeLoad.Clear();
        }
        Interlocked.Exchange(ref _loading, 1);
    }

    /// <summary>Tests only: the dungeon's record, oldest first.</summary>
    public static void SetHistoryForTests(string raidId, params string[] outcomes)
    {
        lock (Sync)
        {
            _history ??= new(StringComparer.Ordinal);
            _history[raidId] = outcomes.ToList();
        }
    }

    /// <summary>Tests only: calls waiting to arrive.</summary>
    public static int PendingCalls
    {
        get { lock (Sync) return Calls.Count; }
    }
}
