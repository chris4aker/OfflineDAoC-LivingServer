using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>Where a realm's storyline stands: its two protagonists, the next episode, and the side a player took.</summary>
[DataTable(TableName = "offline_bot_storylines")]
public sealed class OfflineBotStorylineRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotStorylineRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Unique = true, Varchar = 48)] public string StoryKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 32)] public string Arc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public long BotA { get; set; }
    [DataElement(AllowDbNull = false)] public long BotB { get; set; }
    [DataElement(AllowDbNull = false)] public int Episode { get; set; } = 1;
    [DataElement(AllowDbNull = false)] public string NextAtUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 1)] public string Side { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 64)] public string SidePlayer { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public bool SideTold { get; set; }
    [DataElement(AllowDbNull = false)] public string DoneUtc { get; set; } = string.Empty;
}

/// <summary>
/// Storylines (bot_living_storylines, docs/bot-voices/TALKING-UPDATE.md section 13): dramas between two bots that play out
/// in /faction as debate episodes, hours or days apart: a kill steal, a guild split, a romance, a ninja-loot trial. The
/// same two bots (roles A and B) play every episode of an arc, and name each other ({name_a}, {name_b}); the other roles are
/// cast fresh each time. A player who names one of them in /faction during an episode takes that side ("{name_a} is right",
/// "nah {name_b} did nothing wrong"): the one they backed thanks them by tell, the other tells them off, and the last
/// episode plays that side's ending (scene "_4a" or "_4b"; plain "_4" when nobody took a side).
/// Scenes are "story_&lt;arc&gt;_&lt;episode&gt;[a|b]" with topic "story_&lt;arc&gt;" (storylines.lines). Progress is saved
/// (offline_bot_storylines), one realm plays at most one episode every 45 minutes, an arc's episodes are 3-10 hours apart,
/// and a finished arc starts again with a new pair a week later.
/// </summary>
public static class AutonomousBotStorylines
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool On => Properties.BOT_LIVING_STORYLINES;

    // Pacing (owner 2026-10-28, for a player on a few hours a week): one episode per realm every 45 minutes at most,
    // episodes 3-10 hours apart, a new arc every 2 hours (they were 75 minutes, 6-20 hours and 4 hours).
    public static readonly TimeSpan RealmRest = TimeSpan.FromMinutes(45);
    public static readonly TimeSpan EpisodeGapMin = TimeSpan.FromHours(3), EpisodeGapSpread = TimeSpan.FromHours(7);
    public static readonly TimeSpan RestartAfter = TimeSpan.FromDays(7);
    public static readonly TimeSpan NewArcGap = TimeSpan.FromHours(2);
    // A day (it was three): in-game check 2026-10-28 found two of Albion's four arcs waiting on bots out of the game,
    // and a player on a few hours a week rarely comes back within three days, so stuck arcs held up new ones for weeks.
    public static readonly TimeSpan AbandonAfter = TimeSpan.FromDays(1);

    internal static readonly LivingServerTable<string, OfflineBotStorylineRecord> Stories =
        new("storylines", row => row.StoryKey, StringComparer.OrdinalIgnoreCase);

    private static readonly ConcurrentDictionary<eRealm, DateTime> LastEpisodeUtc = new();

    /// <summary>The realm's episode that ended last: players often weigh in after reading it, so a side still counts for
    /// <see cref="SideWindow"/> after it ends.</summary>
    private static readonly ConcurrentDictionary<eRealm, (string StoryKey, long BotA, long BotB, DateTime EndedUtc)> LastEpisode = new();

    public static readonly TimeSpan SideWindow = TimeSpan.FromMinutes(10);
    private static readonly ConcurrentDictionary<eRealm, DateTime> NextNewArcUtc = new();
    private static Timer _flushTimer;

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            Stories.Load(GameServer.Database.SelectAllObjects<OfflineBotStorylineRecord>());
        }
        catch (Exception exception)
        {
            Log.Warn("Storylines: could not read saved storylines", exception);
        }
        _flushTimer = new Timer(_ => Stories.Flush(), null, 60_000, 60_000);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _flushTimer?.Dispose();
        _flushTimer = null;
        Stories.Flush(wait: true);
    }

    #region The arcs (pure, unit tested)

    private static readonly Regex SceneId = new(@"^story_(?<arc>[a-z]+)_(?<n>\d+)(?<side>[ab]?)$", RegexOptions.Compiled);

    /// <summary>True for a storyline episode (never chosen as an ordinary debate). Pure.</summary>
    public static bool IsStory(VC.Scene scene) => scene?.Id != null && SceneId.IsMatch(scene.Id);

    public sealed record Episode(VC.Scene Neutral, VC.Scene SideA, VC.Scene SideB);

    /// <summary>The arcs in the corpus: arc name to its episodes in order (1-based; index 0 unused). Pure.</summary>
    public static Dictionary<string, Episode[]> Arcs(IEnumerable<VC.Scene> scenes)
    {
        var parts = new Dictionary<string, Dictionary<int, (VC.Scene N, VC.Scene A, VC.Scene B)>>(StringComparer.Ordinal);
        foreach (VC.Scene scene in scenes ?? [])
        {
            Match match = SceneId.Match(scene.Id ?? string.Empty);
            if (!match.Success)
                continue;
            string arc = match.Groups["arc"].Value;
            int n = int.Parse(match.Groups["n"].Value);
            if (!parts.TryGetValue(arc, out var episodes))
                parts[arc] = episodes = new();
            episodes.TryGetValue(n, out var episode);
            episodes[n] = match.Groups["side"].Value switch
            {
                "a" => (episode.N, scene, episode.B),
                "b" => (episode.N, episode.A, scene),
                _ => (scene, episode.A, episode.B),
            };
        }
        var arcs = new Dictionary<string, Episode[]>(StringComparer.Ordinal);
        foreach (var (arc, episodes) in parts)
        {
            int count = episodes.Keys.Max();
            // An arc with a gap or a missing plain episode is left out.
            if (Enumerable.Range(1, count).Any(n => !episodes.TryGetValue(n, out var e) || e.N == null))
                continue;
            var list = new Episode[count + 1];
            for (int n = 1; n <= count; n++)
                list[n] = new Episode(episodes[n].N, episodes[n].A, episodes[n].B);
            arcs[arc] = list;
        }
        return arcs;
    }

    /// <summary>The scene to play: the side's ending when a side was taken and the episode has one, else the plain one. Pure.</summary>
    public static VC.Scene SceneFor(Episode episode, string side) =>
        side == "A" && episode.SideA != null ? episode.SideA : side == "B" && episode.SideB != null ? episode.SideB : episode.Neutral;

    private static readonly Regex Against = new(
        @"\b(wrong|lying|liar|lies|guilty|did it|ninja|thief|cheat|cheater|jerk|clown|sucks|is bad|is the problem|fault|blame|shame on|boo)\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // Support that outweighs a blame word in the same line ("{name_b} did nothing wrong", "not guilty").
    private static readonly Regex For = new(
        @"\b(nothing wrong|not wrong|not guilty|innocent|is right|was right|backing|team|on .{1,20} side|\+1|ty)\b|\+1",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// The side a player's line takes, from the protagonist it names: "A" or "B", or null when it names neither or both.
    /// Naming one with blame ("{name_b} is lying", "boo {name_a}") backs the other. Names match whole words, case-insensitive,
    /// or by their first five letters ("aelw" is too short, "aelwy" counts). Pure.
    /// </summary>
    public static string SideOf(string message, string nameA, string nameB)
    {
        if (string.IsNullOrWhiteSpace(message) || string.IsNullOrEmpty(nameA) || string.IsNullOrEmpty(nameB))
            return null;
        bool a = Names(message, nameA), b = Names(message, nameB);
        if (!a && !b)
            return null;
        if (a && b)
        {
            // Both named: read it clause by clause ("Ysalith you ninja looted that rare, Wileralhelm is right" backs A, in-game
            // check 2026-10-27). A side counts when the clauses that name one of them agree.
            string[] sides = Regex.Split(message, @"[,.;!?]+|\b(?:and|but)\b", RegexOptions.IgnoreCase)
                .Where(clause => Names(clause, nameA) != Names(clause, nameB))
                .Select(clause => SideOf(clause, nameA, nameB)).Distinct().ToArray();
            return sides.Length == 1 ? sides[0] : null;
        }
        bool blame = Against.IsMatch(message) && !For.IsMatch(message);
        return a ? blame ? "B" : "A" : blame ? "A" : "B";
    }

    private static bool Names(string message, string name)
    {
        string text = message.ToLowerInvariant();
        string full = name.ToLowerInvariant();
        string shortName = full.Length > 5 ? full[..5] : full;
        return Regex.IsMatch(text, $@"(?<![a-z]){Regex.Escape(full)}(?![a-z])") ||
               full.Length > 5 && Regex.IsMatch(text, $@"(?<![a-z]){Regex.Escape(shortName)}[a-z]*");
    }

    #endregion

    #region Choosing the next episode (AutonomousBotDebates.TryStart)

    public sealed record Next(string StoryKey, VC.Scene Scene, long BotA, long BotB, Dictionary<string, string> Slots);

    public static string KeyOf(eRealm realm, string arc) => $"{(int)realm}:{arc}";

    private static DateTime ParseUtc(string value) =>
        DateTime.TryParse(value, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime when) ? when : DateTime.MinValue;

    /// <summary>
    /// The realm's next due episode, with its two protagonists, or null. The protagonists of a new arc are cast from the
    /// free bots (<paramref name="online"/>) that fit the first episode's roles A and B. A running arc plays when its pair is
    /// in the game (<paramref name="present"/>: alive, even if fighting; the scene waits for a busy speaker); an arc whose
    /// pair is offline waits, and is abandoned (to start again with a new pair) once it has waited a day.
    /// </summary>
    /// <remarks>
    /// In-game check 2026-10-28: the pair used to have to be out of combat at the very moment a scene was chosen, and
    /// gamebots grinding a camp almost never both are, so running arcs hardly ever got their next episode.
    /// </remarks>
    public static Next TryNext(eRealm realm, GameBot[] online, DateTime nowUtc, Random random, GameBot[] present = null)
    {
        if (!On || online == null || online.Length < 4)
            return null;
        if (LastEpisodeUtc.TryGetValue(realm, out DateTime last) && nowUtc - last < RealmRest)
            return null;
        var arcs = Arcs(AutonomousBotVoices.Corpus?.Scenes);
        var byId = online.Concat(present ?? []).GroupBy(bot => (long)bot.DatabaseID).ToDictionary(g => g.Key, g => g.First());
        // An arc whose first episode never played may recast its pair from the free bots.
        bool Free(long id) => online.Any(bot => bot.DatabaseID == id);
        foreach (var (arc, episodes) in arcs.OrderBy(_ => random.Next()))
        {
            string key = KeyOf(realm, arc);
            Stories.TryGet(key, out OfflineBotStorylineRecord row);
            if (row != null && !string.IsNullOrEmpty(row.DoneUtc))
            {
                if (nowUtc - ParseUtc(row.DoneUtc) < RestartAfter)
                    continue;
                row = null; // finished long enough ago: a new pair lives it again
            }
            if (row != null && ParseUtc(row.NextAtUtc) > nowUtc)
                continue;
            if (row != null && row.Episode > 1 && (!byId.ContainsKey(row.BotA) || !byId.ContainsKey(row.BotB)))
            {
                if (nowUtc - ParseUtc(row.NextAtUtc) > AbandonAfter)
                    Abandon(key, "its pair stayed offline");
                continue;
            }
            if (row == null || row.Episode <= 1 && (!Free(row.BotA) || !Free(row.BotB)))
            {
                // A new arc (or one whose first episode never played): only every few hours per realm.
                if (NextNewArcUtc.TryGetValue(realm, out DateTime newAt) && nowUtc < newAt)
                    continue;
                (long a, long b) = CastPair(episodes[1].Neutral, online, random);
                if (a == 0)
                    continue;
                row = Stories.Change(key, () => new OfflineBotStorylineRecord { StoryKey = key, Realm = (int)realm, Arc = arc }, r =>
                {
                    r.BotA = a;
                    r.BotB = b;
                    r.Episode = 1;
                    r.Side = string.Empty;
                    r.SidePlayer = string.Empty;
                    r.SideTold = false;
                    r.DoneUtc = string.Empty;
                    r.NextAtUtc = nowUtc.ToString("O");
                });
                NextNewArcUtc[realm] = nowUtc + NewArcGap;
            }
            if (row.Episode < 1 || row.Episode >= episodes.Length)
            {
                // The corpus lost this arc's later episodes: end it, so its pair is free for other stories.
                Stories.Change(key, () => row, r => r.DoneUtc = nowUtc.ToString("O"));
                continue;
            }
            VC.Scene scene = SceneFor(episodes[row.Episode], row.Side);
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["name_a"] = byId[row.BotA].Name,
                ["name_b"] = byId[row.BotB].Name,
            };
            if (!string.IsNullOrEmpty(row.SidePlayer))
                slots["player"] = row.SidePlayer;
            return new Next(key, scene, row.BotA, row.BotB, slots);
        }
        return null;
    }

    private static (long A, long B) CastPair(VC.Scene scene, GameBot[] online, Random random)
    {
        scene.Roles.TryGetValue("A", out VC.RoleSpec specA);
        scene.Roles.TryGetValue("B", out VC.RoleSpec specB);
        var pool = online.Where(bot => !InAnotherStory(bot.DatabaseID))
            .Select(bot => new AutonomousBotDebates.Candidate(bot.DatabaseID, AutonomousBotPersonality.For(bot).Type,
                bot.CharacterClass?.Name ?? bot.ClassName ?? string.Empty, bot.Level))
            .OrderBy(_ => random.Next()).ToArray();
        var a = pool.FirstOrDefault(c => AutonomousBotDebates.Fits(specA, c));
        if (a.Id == 0)
            return (0, 0);
        var b = pool.FirstOrDefault(c => c.Id != a.Id && AutonomousBotDebates.Fits(specB, c));
        return b.Id == 0 ? (0, 0) : (a.Id, b.Id);
    }

    /// <summary>
    /// GM test (/lstest storyline): the realm's rests are lifted and every unfinished arc is due, so the next /faction scene
    /// is a storyline episode (a new arc when none is running). Returns how many arcs can play now; <paramref name="note"/>
    /// says what else happened. An arc already due keeps its due time, so the abandon clock isn't reset. When
    /// every arc is either running with its pair out of the game or finished this week, the one that has waited longest is
    /// dropped so a new pair can be cast (in-game check 2026-10-28: all four arcs were waiting for offline bots).
    /// </summary>
    public static int MakeDueNow(eRealm realm, out string note)
    {
        note = null;
        LastEpisodeUtc.TryRemove(realm, out _);
        NextNewArcUtc.TryRemove(realm, out _);
        DateTime now = DateTime.UtcNow;
        string due = now.AddMinutes(-1).ToString("O");
        OfflineBotStorylineRecord[] open = Stories.Where(row => row.Realm == (int)realm && string.IsNullOrEmpty(row.DoneUtc));
        foreach (OfflineBotStorylineRecord row in open.Where(row => ParseUtc(row.NextAtUtc) > now))
            Stories.Change(row.StoryKey, () => row, r => r.NextAtUtc = due);
        static bool InGame(long id) => AutonomousBotRegistry.TryGet(id, out GameBot bot) && bot.IsAlive &&
                                       bot.ObjectState == GameObject.eObjectState.Active;
        OfflineBotStorylineRecord[] waiting = open.Where(row => row.Episode > 1 && (!InGame(row.BotA) || !InGame(row.BotB))).ToArray();
        int playable = open.Length - waiting.Length;
        bool anyFree = Arcs(AutonomousBotVoices.Corpus?.Scenes).Keys.Any(arc =>
            !Stories.TryGet(KeyOf(realm, arc), out OfflineBotStorylineRecord row) ||
            !string.IsNullOrEmpty(row.DoneUtc) && now - ParseUtc(row.DoneUtc) >= RestartAfter);
        if (playable == 0 && !anyFree && waiting.Length > 0)
        {
            OfflineBotStorylineRecord stuck = waiting.OrderBy(row => ParseUtc(row.NextAtUtc)).First();
            Abandon(stuck.StoryKey, "GM test: its pair is out of the game");
            note = $"the {stuck.Arc} storyline was dropped (its two bots are out of the game) so a new one can start";
        }
        else if (waiting.Length > 0)
            note = $"{waiting.Length} storyline(s) wait for bots that are out of the game";
        return playable;
    }

    /// <summary>
    /// A new play session (AutonomousLivingWorld): the realm's rests are lifted, so the session's first /faction scene is a
    /// storyline episode when one is due or a new arc can be cast. Episode gaps are kept.
    /// </summary>
    public static void OnSessionStart(eRealm realm)
    {
        LastEpisodeUtc.TryRemove(realm, out _);
        NextNewArcUtc.TryRemove(realm, out _);
    }

    /// <summary>A bot already a protagonist of an unfinished arc (one drama at a time).</summary>
    private static bool InAnotherStory(long botId) =>
        Stories.Where(row => string.IsNullOrEmpty(row.DoneUtc) && (row.BotA == botId || row.BotB == botId)).Length > 0;

    private static void Abandon(string key, string why)
    {
        Stories.RemoveWhere(row => string.Equals(row.StoryKey, key, StringComparison.OrdinalIgnoreCase));
        Log.Info($"STORYLINE_ABANDONED story={key} reason=\"{why}\"");
    }

    /// <summary>An episode ended (AutonomousBotDebates.End): played to its end, it moves the arc on; cut short, it is tried again later.</summary>
    public static void OnEpisodeEnded(eRealm realm, string storyKey, string sceneId, bool finished, DateTime nowUtc)
    {
        if (string.IsNullOrEmpty(storyKey) || !Stories.TryGet(storyKey, out OfflineBotStorylineRecord existing))
            return;
        LastEpisodeUtc[realm] = nowUtc;
        LastEpisode[realm] = (storyKey, existing.BotA, existing.BotB, nowUtc);
        var arcs = Arcs(AutonomousBotVoices.Corpus?.Scenes);
        Stories.Change(storyKey, () => existing, row =>
        {
            if (!finished)
            {
                row.NextAtUtc = (nowUtc + TimeSpan.FromMinutes(30)).ToString("O");
                return;
            }
            int count = arcs.TryGetValue(row.Arc, out var episodes) ? episodes.Length - 1 : row.Episode;
            row.Episode++;
            if (row.Episode > count)
                row.DoneUtc = nowUtc.ToString("O");
            else
                row.NextAtUtc = (nowUtc + EpisodeGapMin + TimeSpan.FromMinutes(Random.Shared.Next((int)EpisodeGapSpread.TotalMinutes))).ToString("O");
        });
        Log.Info($"STORYLINE_EPISODE story={storyKey} scene={sceneId} finished={finished}");
    }

    #endregion

    #region A player takes a side

    /// <summary>
    /// A player's /faction line while a storyline episode runs, or up to ten minutes after one ended
    /// (AutonomousBotChatCoordinator.OnPlayerFactionChat): naming one
    /// protagonist takes a side. The latest side counts. The first time, the one backed thanks them by tell and the other
    /// tells them off.
    /// </summary>
    public static bool OnPlayerFactionChat(GamePlayer player, string message)
    {
        if (!On || player == null)
            return false;
        (string StoryKey, long BotA, long BotB) story;
        if (AutonomousBotDebates.RunningStory(player.Realm) is { } running)
            story = running;
        else if (LastEpisode.TryGetValue(player.Realm, out var last) && DateTime.UtcNow - last.EndedUtc <= SideWindow)
            story = (last.StoryKey, last.BotA, last.BotB);
        else
            return false;
        if (!AutonomousBotRegistry.TryGet(story.BotA, out GameBot a) || !AutonomousBotRegistry.TryGet(story.BotB, out GameBot b))
            return false;
        string side = SideOf(message, a.Name, b.Name);
        if (side == null || !Stories.TryGet(story.StoryKey, out OfflineBotStorylineRecord row))
            return false;
        bool tell = !row.SideTold || row.Side != side;
        Stories.Change(story.StoryKey, () => row, r =>
        {
            r.Side = side;
            r.SidePlayer = player.Name;
            r.SideTold = true;
        });
        Log.Info($"STORYLINE_SIDE story={story.StoryKey} player=\"{player.Name}\" side={side}");
        if (!tell)
            return true;
        GameBot backed = side == "A" ? a : b, other = side == "A" ? b : a;
        AutonomousLivingServer.Later(backed, 20_000 + Random.Shared.Next(40_000), () => AutonomousLivingServer.IsSpeaker(backed), () =>
        {
            if (AutonomousBotVoices.TryEventLine(backed, "storyline", out string line, "side_thanks",
                    new Dictionary<string, string> { ["player"] = player.Name, ["target"] = other.Name }))
                AutonomousLivingServer.Tell(backed, player, line);
        });
        AutonomousLivingServer.Later(other, 45_000 + Random.Shared.Next(45_000), () => AutonomousLivingServer.IsSpeaker(other), () =>
        {
            if (AutonomousBotVoices.TryEventLine(other, "storyline", out string line, "side_scorn",
                    new Dictionary<string, string> { ["player"] = player.Name, ["target"] = backed.Name }))
                AutonomousLivingServer.Tell(other, player, line);
        });
        return true;
    }

    #endregion
}
