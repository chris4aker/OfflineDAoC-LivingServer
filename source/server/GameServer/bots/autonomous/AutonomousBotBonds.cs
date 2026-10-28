using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>What one autonomous gamebot remembers about another it grouped with (AutonomousBotBonds). One row per direction.</summary>
[DataTable(TableName = "offline_bot_bonds")]
public sealed class OfflineBotBondRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotBondRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Index = true)] public long BotId { get; set; }
    [DataElement(AllowDbNull = false)] public long OtherId { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string OtherName { get; set; } = string.Empty;
    /// <summary>PvE tasks started together.</summary>
    [DataElement(AllowDbNull = false)] public int Grouped { get; set; }
    /// <summary>Tasks that ran their whole time with at most one wipe.</summary>
    [DataElement(AllowDbNull = false)] public int Good { get; set; }
    [DataElement(AllowDbNull = false)] public int Wipes { get; set; }
    /// <summary>Times the other bot never reached the meeting point.</summary>
    [DataElement(AllowDbNull = false)] public int Flaked { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string LastPlace { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string FirstUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastUtc { get; set; } = string.Empty;
}

/// <summary>
/// Gamebots remember each other (the groundwork for the bots' memory of one another; docs/LIVING-SERVER.md). After a
/// PvE party ends, every member remembers every other: grouped, a good run, wipes together. A member who never reached
/// the meeting point is remembered as a flake by the ones who waited. Matchmaking uses it: a leader takes its friends
/// first (after guildmates), and a leader and a bot with a grudge between them won't group until one of them has
/// waited long enough to take anyone. Only autonomous gamebots are remembered here; companion bots and real players
/// are not (bot memory of players is offline_bot_memory).
/// </summary>
public static class AutonomousBotBonds
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool On => Properties.BOT_GROUP_BONDS;

    public const int FriendScore = 5;
    public const int GrudgeScore = -4;
    /// <summary>Remembered bots per bot; the least memorable go first.</summary>
    public const int MaximumBondsPerBot = 40;

    internal static readonly LivingServerTable<(long Bot, long Other), OfflineBotBondRecord> Bonds =
        new("bot bonds", row => (row.BotId, row.OtherId));

    /// <summary>Who each bot remembers (so trimming never scans the whole table on the game loop).</summary>
    private static readonly Dictionary<long, HashSet<long>> Known = new();

    private static Timer _flushTimer;

    /// <summary>Loads the rows and the per-bot index (tests call it with their own rows).</summary>
    public static void Load(IEnumerable<OfflineBotBondRecord> rows)
    {
        OfflineBotBondRecord[] all = rows.ToArray();
        Bonds.Load(all);
        lock (Known)
        {
            Known.Clear();
            foreach (OfflineBotBondRecord row in all)
                KnownBy(row.BotId).Add(row.OtherId);
        }
    }

    private static HashSet<long> KnownBy(long botId)
    {
        if (!Known.TryGetValue(botId, out HashSet<long> others))
            Known[botId] = others = new HashSet<long>();
        return others;
    }

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            Load(GameServer.Database.SelectAllObjects<OfflineBotBondRecord>());
        }
        catch (Exception exception)
        {
            Log.Warn("Bonds: could not read bot bonds", exception);
        }
        _flushTimer = new Timer(_ => Bonds.Flush(), null, 60_000, 60_000);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        Bonds.Flush(wait: true);
    }

    #region Pure rules (unit tested)

    public enum Outcome { NotStarted, Plain, Good, Rough }

    /// <summary>How one bot feels about another. Good runs count most, a flake costs two, a wipe one. Pure.</summary>
    public static int Score(int grouped, int good, int wipes, int flaked) =>
        2 * Math.Max(0, good) + Math.Min(Math.Max(0, grouped), 5) - Math.Max(0, wipes) - 2 * Math.Max(0, flaked);

    public static bool IsFriend(int score) => score >= FriendScore;
    public static bool IsGrudge(int score) => score <= GrudgeScore;

    /// <summary>A party that never started its task leaves no memory; one that ran its time with at most one wipe was good;
    /// two or more wipes made it rough. Pure.</summary>
    public static Outcome OutcomeOf(bool taskStarted, bool ranItsCourse, int wipes) =>
        !taskStarted ? Outcome.NotStarted :
        wipes >= 2 ? Outcome.Rough :
        ranItsCourse ? Outcome.Good : Outcome.Plain;

    /// <summary>Order of a candidate in a leader's pick: 0 friend, 1 someone it has grouped with, 2 a stranger. Pure.</summary>
    public static int FriendRank(int? score) =>
        score is not { } known ? 2 : IsFriend(known) ? 0 : 1;

    /// <summary>A grudge either way keeps two bots apart until the wait is long enough to take anyone (stage 2). Pure.</summary>
    public static bool WontGroup(int? leaderView, int? candidateView, int patienceStage) =>
        patienceStage < 2 && (leaderView is { } l && IsGrudge(l) || candidateView is { } c && IsGrudge(c));

    /// <summary>Which remembered bots a bot forgets when it has too many: lowest |score| first, then the oldest. Pure.</summary>
    public static IEnumerable<T> ToForget<T>(IReadOnlyCollection<T> rows, Func<T, int> score, Func<T, string> lastUtc, int keep) =>
        rows.OrderBy(row => Math.Abs(score(row))).ThenBy(lastUtc, StringComparer.Ordinal).Take(Math.Max(0, rows.Count - keep));

    #endregion

    public static int ScoreOf(OfflineBotBondRecord row) => Score(row.Grouped, row.Good, row.Wipes, row.Flaked);

    private static int? View(GameBot from, GameBot about) =>
        On && from != null && about != null && from.DatabaseID > 0 && about.DatabaseID > 0 &&
        Bonds.TryGet((from.DatabaseID, about.DatabaseID), out OfflineBotBondRecord row) ? ScoreOf(row) : null;

    /// <summary>How <paramref name="from"/> feels about <paramref name="about"/>, or null when it doesn't know it.</summary>
    public static int? ScoreBetween(GameBot from, GameBot about) => View(from, about);

    public static int FriendRank(GameBot leader, GameBot candidate) => FriendRank(View(leader, candidate));

    public static bool WontGroup(GameBot leader, GameBot candidate, int patienceStage) =>
        On && WontGroup(View(leader, candidate), View(candidate, leader), patienceStage);

    /// <summary>The bots this bot counts as friends, best first (for talk and future features).</summary>
    public static OfflineBotBondRecord[] Friends(long botId)
    {
        long[] others;
        lock (Known)
            others = Known.TryGetValue(botId, out HashSet<long> set) ? set.ToArray() : [];
        return others.Select(other => Bonds.TryGet((botId, other), out OfflineBotBondRecord row) ? row : null)
            .Where(row => row != null && IsFriend(ScoreOf(row))).OrderByDescending(ScoreOf).ToArray();
    }

    /// <summary>Pairs of members who are friends (either way), for the formation log.</summary>
    public static int FriendPairs(IReadOnlyList<GameBot> members)
    {
        if (!On || members == null) return 0;
        int pairs = 0;
        for (int i = 0; i < members.Count; i++)
            for (int j = i + 1; j < members.Count; j++)
                if (View(members[i], members[j]) is { } a && IsFriend(a) || View(members[j], members[i]) is { } b && IsFriend(b))
                    pairs++;
        return pairs;
    }

    /// <summary>Every member remembers every other once a PvE party ends.</summary>
    public static void OnPartyFinished(IReadOnlyList<GameBot> members, bool taskStarted, bool ranItsCourse, int wipes, string place)
    {
        if (!On || members == null) return;
        Outcome outcome = OutcomeOf(taskStarted, ranItsCourse, wipes);
        if (outcome == Outcome.NotStarted) return;
        GameBot[] bots = members.Where(IsRemembered).Distinct().ToArray();
        var newFriends = new List<(GameBot Bot, GameBot Other)>();
        foreach (GameBot bot in bots)
            foreach (GameBot other in bots)
                if (other != bot && Remember(bot, other, place, row =>
                    {
                        row.Grouped++;
                        if (outcome == Outcome.Good) row.Good++;
                        row.Wipes += Math.Max(0, wipes);
                    }))
                    newFriends.Add((bot, other));
        if (newFriends.Count > 0)
        {
            var (speaker, friend) = newFriends[Random.Shared.Next(newFriends.Count)];
            Tell(speaker, friend, "friends", place, 20_000 + Random.Shared.Next(70_000));
        }
    }

    /// <summary>A new party with an old friend in it: now and then one of the two says so.</summary>
    public static void OnPartyFormed(IReadOnlyList<GameBot> members)
    {
        if (!On || members == null || Random.Shared.NextDouble() >= ReunionTalkChance) return;
        var pairs = new List<(GameBot, GameBot)>();
        foreach (GameBot bot in members.Where(IsRemembered))
            foreach (GameBot other in members.Where(IsRemembered))
                if (other != bot && View(bot, other) is { } score && IsFriend(score))
                    pairs.Add((bot, other));
        if (pairs.Count == 0) return;
        var (speaker, friend) = pairs[Random.Shared.Next(pairs.Count)];
        Tell(speaker, friend, "reunion", speaker.CurrentZone?.Description, 5_000 + Random.Shared.Next(20_000));
    }

    /// <summary>The members who waited remember the one who never came.</summary>
    public static void OnNoShow(IReadOnlyList<GameBot> waiting, GameBot flake, string place)
    {
        if (!On || waiting == null || !IsRemembered(flake)) return;
        GameBot[] waited = waiting.Where(IsRemembered).Distinct().Where(bot => bot != flake).ToArray();
        foreach (GameBot bot in waited)
            Remember(bot, flake, place, row => row.Flaked++);
        if (waited.Length > 0 && Random.Shared.NextDouble() < FlakeTalkChance)
            Tell(waited[Random.Shared.Next(waited.Length)], flake, "flake", place, 30_000 + Random.Shared.Next(90_000));
    }

    private static bool IsRemembered(GameBot bot) =>
        bot != null && bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && bot.DatabaseID > 0;

    /// <returns>Whether this made <paramref name="other"/> a friend.</returns>
    private static bool Remember(GameBot bot, GameBot other, string place, Action<OfflineBotBondRecord> change)
    {
        string now = DateTime.UtcNow.ToString("O");
        int before = 0;
        bool known = Bonds.TryGet((bot.DatabaseID, other.DatabaseID), out OfflineBotBondRecord existing);
        if (known) before = ScoreOf(existing);
        OfflineBotBondRecord row = Bonds.Change((bot.DatabaseID, other.DatabaseID), () => new OfflineBotBondRecord
        {
            BotId = bot.DatabaseID,
            OtherId = other.DatabaseID,
            FirstUtc = now,
        }, row =>
        {
            row.OtherName = other.Name ?? string.Empty;
            change(row);
            if (!string.IsNullOrWhiteSpace(place)) row.LastPlace = place.Length > 64 ? place[..64] : place;
            row.LastUtc = now;
        });
        int after = ScoreOf(row);
        if (!IsFriend(before) && IsFriend(after))
            Log.Info($"BOND_FRIENDS bot=\"{bot.Name}\" other=\"{other.Name}\" realm={GlobalConstants.RealmToName(bot.Realm)} score={after}");
        else if (!IsGrudge(before) && IsGrudge(after))
            Log.Info($"BOND_GRUDGE bot=\"{bot.Name}\" other=\"{other.Name}\" realm={GlobalConstants.RealmToName(bot.Realm)} score={after}");
        if (!known)
            ForgetExtra(bot.DatabaseID, other.DatabaseID);
        return !IsFriend(before) && IsFriend(after);
    }

    #region Talk

    /// <summary>One line of a topic per realm this often, so bonds season /faction instead of filling it.</summary>
    public static readonly TimeSpan TalkRest = TimeSpan.FromMinutes(10);
    public const double ReunionTalkChance = 0.4, FlakeTalkChance = 0.5;
    private static readonly ConcurrentDictionary<(eRealm Realm, string Topic), long> NextTalk = new();

    /// <summary>Whether a topic may be told in a realm now (and if so, holds it for <see cref="TalkRest"/>). Pure but for the clock.</summary>
    public static bool TryReserveTalk(eRealm realm, string topic, long now)
    {
        var key = (realm, topic);
        if (NextTalk.TryGetValue(key, out long next) && now < next)
            return false;
        NextTalk[key] = now + (long)TalkRest.TotalMilliseconds;
        return true;
    }

    /// <summary>The speaker tells its realm's /faction about <paramref name="other"/> after a delay, and a bot or two react.</summary>
    private static void Tell(GameBot speaker, GameBot other, string topic, string place, int delayMs)
    {
        if (!AutonomousLivingServer.IsSpeaker(speaker) || AutonomousBeggars.Is(speaker) || other == null)
            return;
        eRealm realm = speaker.Realm;
        if (!AutonomousBotChatCoordinator.HasFactionAudience(realm) || !TryReserveTalk(realm, topic, GameLoop.GameLoopTime))
            return;
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["other"] = other.Name ?? string.Empty,
            ["other_class"] = AutonomousLivingServer.ClassOf(other).ToLowerInvariant(),
        };
        if (!string.IsNullOrWhiteSpace(place))
            slots["place"] = place;
        string otherName = other.Name;
        AutonomousLivingServer.Later(speaker, delayMs, () => AutonomousLivingServer.CanTalk(speaker), () =>
        {
            if (!AutonomousBotChatCoordinator.HasFactionAudience(realm) || AutonomousBotDebates.IsRunning(realm))
                return;
            if (!AutonomousBotVoices.TryEventLine(speaker, "bond", out string line, topic, slots))
                return;
            AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, line);
            AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), speaker, line, "chat");
            Log.Info($"BOND_TALK topic={topic} bot=\"{speaker.Name}\" other=\"{otherName}\" realm={GlobalConstants.RealmToName(realm)}");
            React(realm, speaker, other, topic, slots);
        });
    }

    private static void React(eRealm realm, GameBot teller, GameBot other, string topic, Dictionary<string, string> slots)
    {
        int count = Random.Shared.Next(3);   // 0-2
        if (count == 0) return;
        GameBot[] reactors = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot != teller && bot != other && bot.Realm == realm && AutonomousLivingServer.CanTalk(bot) && !AutonomousBeggars.Is(bot))
            .OrderBy(_ => Random.Shared.Next()).Take(count).ToArray();
        int due = 0;
        foreach (GameBot bot in reactors)
        {
            due += 4_000 + Random.Shared.Next(10_000);
            GameBot speaker = bot;
            AutonomousLivingServer.Later(speaker, due, () => AutonomousLivingServer.CanTalk(speaker), () =>
            {
                if (AutonomousBotVoices.TryEventLine(speaker, "bond_reply", out string line, topic, slots))
                    AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, line);
            });
        }
    }

    #endregion

    private static void ForgetExtra(long botId, long added)
    {
        long[] others;
        lock (Known)
        {
            HashSet<long> set = KnownBy(botId);
            set.Add(added);
            if (set.Count <= MaximumBondsPerBot) return;
            others = set.ToArray();
        }
        var mine = new List<OfflineBotBondRecord>(others.Length);
        foreach (long other in others)
            if (Bonds.TryGet((botId, other), out OfflineBotBondRecord row))
                mine.Add(row);
        var forget = ToForget(mine, ScoreOf, row => row.LastUtc, MaximumBondsPerBot).Select(row => row.OtherId).ToHashSet();
        foreach (long other in forget)
            Bonds.Remove((botId, other));
        lock (Known)
            KnownBy(botId).ExceptWith(forget);
    }

    /// <summary>The number of bots this bot remembers.</summary>
    public static int KnownCount(long botId)
    {
        lock (Known)
            return Known.TryGetValue(botId, out HashSet<long> others) ? others.Count : 0;
    }
}
