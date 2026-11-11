using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DOL.GS.ServerProperties;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Tales (Living Server 1.2, the owner's idea, 2026-11-10; docs/TALES.md): now and then a bot in /faction asks the realm's
/// storyteller for the story of a dragon or a boss. Others hush the channel and beg, the teller tells it with a question or
/// two leading it along (data/chat/tales.lines, played by AutonomousBotDebates as a scene whose teller is never recast),
/// the channel stays quiet a moment, and then a rumour follows. Now and then that rumour is true: the realm's raid on that
/// boss forms a few minutes later, and its leader invites the player like any other raid.
/// <list type="bullet">
/// <item>Each realm has up to two storytellers, kept across restarts: other bots name them, and only they tell.</item>
/// <item>At most one tale an evening per realm on its own; a player can ask for one ("tell the one about legion"), and a
/// question asked while a tale runs is answered by the teller.</item>
/// <item>It can go wrong: the teller is pulled into a fight and never finishes, and nobody pretends otherwise.</item>
/// <item>Fireside tales: resting in or near a boss's place with a gamebot in the group, a groupmate tells the short version
/// in /group, once a day per character and tale.</item>
/// </list>
/// Autonomous gamebots only; real players hear it, companions and temporary helpers never speak in it.
/// </summary>
public static class AutonomousTales
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public const string TalePrefix = "tale_", FiresidePrefix = "fireside_";
    /// <summary>The scene role the realm's storyteller plays; its name is the scenes' {name_b}.</summary>
    public const string TellerRole = "B";
    public const int TellersPerRealm = 2;

    /// <summary>A realm's first tale of a session comes this long after the player arrives, plus up to the spread.</summary>
    public const long FirstTaleDelayMs = 25 * 60_000, FirstTaleSpreadMs = 20 * 60_000;
    /// <summary>The least time between two tales a realm starts on its own, plus up to the spread.</summary>
    public const long TaleGapMs = 3 * 60 * 60_000L, TaleGapSpreadMs = 90 * 60_000;
    /// <summary>A tale a player asks for needs only this long since the realm's last one.</summary>
    public const long RequestGapMs = 20 * 60_000;
    /// <summary>A start that could not happen (no storyteller on, a scene running) is tried again this much later.</summary>
    public const long RetryMs = 10 * 60_000;
    /// <summary>The rumour that follows a finished tale comes this long after its last line, plus up to the spread.</summary>
    public const long AftermathDelayMs = 45_000, AftermathSpreadMs = 45_000;
    /// <summary>How often the rumour after a tale is a raid really forming, when one can.</summary>
    public const double TrueRumourChance = 0.5;
    /// <summary>The raid a true rumour promised opens this long after the rumour, plus up to the spread.</summary>
    public const long RaidDelayMs = 6 * 60_000, RaidSpreadMs = 6 * 60_000;
    /// <summary>The teller answers at most this many of the players' questions in one tale.</summary>
    public const int MaxAsides = 2;
    /// <summary>A fireside tale needs the group this long out of a fight, and a player hears at most one in this long.</summary>
    public const long FiresideQuietMs = 20_000, FiresideGapMs = 30 * 60_000;

    public static bool On => AutonomousBotVoices.Enabled && Properties.BOT_LIVING_TALES;

    #region Pure rules (tested)

    public static bool IsTale(VC.Scene scene) => scene?.Topic != null && scene.Topic.StartsWith(TalePrefix, StringComparison.Ordinal);
    public static bool IsFireside(VC.Scene scene) => scene?.Topic != null && scene.Topic.StartsWith(FiresidePrefix, StringComparison.Ordinal);
    /// <summary>A scene only this class plays: never an ordinary /faction debate.</summary>
    public static bool IsTaleScene(VC.Scene scene) => IsTale(scene) || IsFireside(scene);

    /// <summary>The lore file a tale is about ("tale_legion" and "fireside_legion": "legion").</summary>
    public static string LoreIdOf(string topic) =>
        topic == null ? null
        : topic.StartsWith(TalePrefix, StringComparison.Ordinal) ? topic[TalePrefix.Length..]
        : topic.StartsWith(FiresidePrefix, StringComparison.Ordinal) ? topic[FiresidePrefix.Length..] : null;

    // The raid event of each tale's boss: a full id, or a stem that takes the realm ("dragon" -> "dragon-albion").
    private static readonly Dictionary<string, string> RaidStems = new(StringComparer.Ordinal)
    {
        ["golestandt"] = "dragon", ["gjalpinulva"] = "dragon", ["cuuldurach-the-glimmer-king"] = "dragon",
        ["apocalypse"] = "epic", ["king-tuscar"] = "epic", ["olcasgean"] = "epic",
        ["legion"] = "darkness", ["grand-summoner-govannon"] = "summoners",
        ["xanxicar"] = "dungeon-albion-crystal", ["nosdoden"] = "dungeon-midgard-trollheim", ["black-lady"] = "dungeon-hibernia-marfach",
    };

    /// <summary>The realm's raid event on a tale's boss, or null when it has none.</summary>
    public static string RaidIdFor(string loreId, eRealm realm)
    {
        if (loreId == null || !RaidStems.TryGetValue(loreId, out string stem))
            return null;
        if (stem.Contains('-'))
            return stem;
        string word = realm switch { eRealm.Albion => "albion", eRealm.Midgard => "midgard", eRealm.Hibernia => "hibernia", _ => null };
        return word == null ? null : $"{stem}-{word}";
    }

    /// <summary>A bot that could be a realm's storyteller.</summary>
    public readonly record struct TellerCandidate(long Id, string Name, BotPersonalityType Type, int Level);

    /// <summary>
    /// Who becomes a storyteller: a talker or a wanderer before anyone else (Socialite, then Explorer), the higher level
    /// first (a veteran has the stories), then by id so the choice doesn't depend on who happened to be listed first.
    /// </summary>
    public static TellerCandidate? PickTeller(IEnumerable<TellerCandidate> candidates, ISet<long> taken = null) =>
        (candidates ?? []).Where(c => c.Id > 0 && !string.IsNullOrWhiteSpace(c.Name) && taken?.Contains(c.Id) != true)
        .OrderBy(c => c.Type == BotPersonalityType.Socialite ? 0 : c.Type == BotPersonalityType.Explorer ? 1 : 2)
        .ThenByDescending(c => c.Level).ThenBy(c => c.Id)
        .Select(c => (TellerCandidate?)c).FirstOrDefault();

    /// <summary>
    /// Which tale is told: the one asked for or fitting the moment when it exists, else the one the realm heard longest ago
    /// (never told comes first), a random one among equals.
    /// </summary>
    public static VC.Scene ChooseTale(IReadOnlyList<VC.Scene> tales, string preferLoreId, Func<string, long> toldAtUtcTicks, Random random)
    {
        if (tales == null || tales.Count == 0)
            return null;
        if (preferLoreId != null && tales.Where(t => LoreIdOf(t.Topic) == preferLoreId).ToArray() is { Length: > 0 } wanted)
            return wanted[random.Next(wanted.Length)];
        long oldest = tales.Min(t => toldAtUtcTicks?.Invoke(t.Id) ?? 0);
        VC.Scene[] due = tales.Where(t => (toldAtUtcTicks?.Invoke(t.Id) ?? 0) == oldest).ToArray();
        return due[random.Next(due.Length)];
    }

    private static readonly Regex AsksForTale = new(
        @"\b(tell|story|stories|tale|tales|storytime)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex NotARequest = new(
        @"\b(dont tell|don t tell|do not tell|tell me (where|how|when|if)|tell (him|her) (to|i)|told)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static string Plain(string text) =>
        " " + Regex.Replace((text ?? string.Empty).ToLowerInvariant().Replace('\'', ' '), @"[^a-z0-9 ]+", " ").Trim() + " ";

    /// <summary>Whether a player's /faction line asks for a story ("tell the one about legion", "any stories tonight?"). Pure.</summary>
    public static bool IsRequest(string message)
    {
        string plain = Regex.Replace(Plain(message), @"\s+", " ");
        return AsksForTale.IsMatch(plain) && !NotARequest.IsMatch(plain);
    }

    /// <summary>The tale a request names, by the boss's names ("the stone dragon", "gole"), or null for any. Pure.</summary>
    public static string RequestedLore(string message, IEnumerable<(string LoreId, IEnumerable<string> Names)> bosses)
    {
        string plain = Regex.Replace(Plain(message), @"\s+", " ");
        string best = null;
        int length = 0;
        foreach ((string loreId, IEnumerable<string> names) in bosses ?? [])
            foreach (string name in names ?? [])
            {
                string n = Regex.Replace(Plain(name), @"\s+", " ");
                if (n.Trim().Length >= 4 && n.Length > length && plain.Contains(n, StringComparison.Ordinal))
                {
                    best = loreId;
                    length = n.Length;
                }
            }
        return best;
    }

    private static readonly Regex Question = new(
        @"\?|^\s*(who|what|why|how|where|when|did|does|is|was|were|can|could)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Whether a player's line during a tale is a question for the teller. Pure.</summary>
    public static bool IsQuestion(string message) => !string.IsNullOrWhiteSpace(message) && Question.IsMatch(message);

    /// <summary>When a realm may start its next tale on its own after one at <paramref name="lastUtc"/> (pure).</summary>
    public static bool GapOver(DateTime nowUtc, DateTime? lastUtc, long gapMs) =>
        lastUtc is not DateTime last || (nowUtc - last).TotalMilliseconds >= gapMs || nowUtc < last;

    /// <summary>What the teller says to a question the tale has no answer for.</summary>
    public static readonly string[] Deflections =
    [
        "patience, i am getting there", "the tale does not say. nobody who knew came back", "good question. ask me when it is done",
        "that part nobody agrees on", "wait for it", "if i knew that i would be richer",
    ];

    /// <summary>What a listener says when the teller is pulled away mid-tale.</summary>
    public static readonly string[] CutShort =
    [
        "aw, {teller} got pulled into a fight", "well. to be continued i guess", "of course it stops at the good part",
        "{teller}? ... and gone. typical", "someone remind {teller} to finish that later",
    ];

    /// <summary>What a bot says when a player asks for a tale and no storyteller is on.</summary>
    public static readonly string[] TellerAway =
    [
        "{teller} tells that one, not on right now", "ask {teller} when they are on, nobody tells it better",
        "wait for {teller}, i would only ruin it", "{teller} isnt around. shame, its a good one",
    ];

    /// <summary>The rumour after a tale when the realm's raid on that boss can really form.</summary>
    public static readonly string[] RaidRumours =
    [
        "funny timing. heard some guilds are talking about a {boss} raid tonight", "someone is putting a raid together for {boss} i think",
        "all this talk and i heard there is a {boss} run forming later", "heard a few groups want to try {boss} tonight",
    ];

    #endregion

    #region Memory (offline_lore_memory, keys "tale:...")

    private static string TellerKey(eRealm realm, int slot) => $"tale:teller:{(int)realm}:{slot}";
    private static string ToldKey(eRealm realm, string sceneId) => $"tale:told:{(int)realm}:{sceneId}";
    private static string LastKey(eRealm realm) => $"tale:last:{(int)realm}";
    private static string FiresideKey(string characterId, string sceneId) => $"tale:fire:{characterId}:{sceneId}";

    private static string Recall(string key) =>
        LoreAtmosphere.Memory.TryGet(key, out OfflineLoreMemoryRecord row) ? row.Value : null;

    private static void Remember(string key, string value) =>
        LoreAtmosphere.Memory.Change(key, () => new OfflineLoreMemoryRecord { MemoryKey = key }, row =>
        {
            row.Value = value.Length > 128 ? value[..128] : value;
            row.UpdatedUtc = DateTime.UtcNow.ToString("O");
        });

    private static DateTime? RecallUtc(string key) =>
        DateTime.TryParse(Recall(key), null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime at) ? at.ToUniversalTime() : null;

    /// <summary>A realm's storytellers as stored: bot id and name, in slot order.</summary>
    public static (long Id, string Name)[] Tellers(eRealm realm)
    {
        var known = new List<(long, string)>(TellersPerRealm);
        for (int slot = 0; slot < TellersPerRealm; slot++)
            if (Recall(TellerKey(realm, slot)) is { Length: > 0 } value && value.Split('|') is { Length: >= 2 } parts &&
                long.TryParse(parts[0], out long id) && id > 0 && parts[1].Length > 0)
                known.Add((id, parts[1]));
        return known.ToArray();
    }

    /// <summary>
    /// Whether a line says again what one of <paramref name="said"/> says: half or more of the shorter line's telling words
    /// (no "the", "heard", "they say"; "stops" and "stopped" count as one word) are in both, three at least. Pure.
    /// (PC checks 2026-11-10. Build 15fcc70: the teller answered a player with a sentence the tale itself said a minute later,
    /// and the rumour after the tale was that sentence a third time. Build 5300a87, which compared whole words: "the whole
    /// fortress stops turning for a second" as the answer, "the whole fortress stopped turning for one breath" in the tale.)
    /// </summary>
    public static bool Repeats(string line, IEnumerable<string> said)
    {
        HashSet<string> words = WordsOf(line);
        if (words.Count < 3 || said == null)
            return false;
        foreach (string other in said)
        {
            HashSet<string> theirs = WordsOf(other);
            if (theirs.Count < 3)
                continue;
            int shared = words.Count(theirs.Contains);
            if (shared >= 3 && shared * 2 >= Math.Min(words.Count, theirs.Count))
                return true;
        }
        return false;
    }

    private static readonly HashSet<string> SmallWords = new(StringComparer.Ordinal)
    {
        "the", "and", "for", "that", "this", "with", "from", "they", "them", "their", "his", "her", "him", "she", "was", "were",
        "are", "you", "your", "say", "says", "said", "heard", "someone", "when", "what", "who", "its", "has", "had", "have", "not",
        "but", "out", "one", "all", "any", "every", "into", "than", "then", "there", "here", "got", "get", "will", "would", "can",
        "about", "just", "even", "ever", "never", "still", "only", "once", "been", "some",
    };

    /// <summary>A line's telling words, each cut to its first four letters.</summary>
    private static HashSet<string> WordsOf(string text) =>
        new string((text ?? string.Empty).ToLowerInvariant().Select(c => char.IsLetter(c) ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(word => word.Length >= 3 && !SmallWords.Contains(word))
            .Select(word => word.Length > 4 ? word[..4] : word).ToHashSet(StringComparer.Ordinal);

    /// <summary>Every line a tale's scene can say (all the ways of saying each).</summary>
    public static IEnumerable<string> LinesOf(VC.Scene scene) =>
        scene?.Beats.Where(beat => !beat.IsPause).SelectMany(beat => beat.Variants) ?? [];

    #endregion

    #region Runtime

    private sealed class RealmState
    {
        public long NextTaleAt = long.MaxValue;
        public long LastRequestAt = long.MinValue / 2;
        /// <summary>The tale playing now (its scene, teller and how many questions the teller answered), or null.</summary>
        public VC.Scene Playing;
        public long TellerId;
        public string TellerName;
        public int Asides;
        /// <summary>What the teller answered a player in this tale, so the rumour after it says something else.</summary>
        public readonly List<string> Answers = new();
        /// <summary>GM check (/lstest tale ... raid): the next rumour is the raid when the raid can form, without the roll.</summary>
        public bool RaidRumourNext;
        public readonly List<(long At, Action Do)> Due = new();
    }

    private sealed class Fireside
    {
        public GameBot Teller, Listener;
        public readonly Queue<(string Role, string Text)> Lines = new();
        public long NextAt;
    }

    private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];
    private static readonly ConcurrentDictionary<eRealm, RealmState> States = new();
    private static readonly ConcurrentDictionary<string, long> FiresideQuietSince = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, long> LastFireside = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<Group, Fireside> Firesides = new();
    private static long _nextPulse;

    private static RealmState State(eRealm realm) => States.GetOrAdd(realm, _ => new RealmState());

    private static bool Speaks(GameBot bot, eRealm realm) => bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
        bot.Realm == realm && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && bot.DatabaseID > 0 && !AutonomousBeggars.Is(bot);

    private static VC.Scene[] TalesOf(eRealm realm) =>
        AutonomousBotVoices.Corpus.Scenes.Where(scene => IsTale(scene) && VC.RealmFits(scene.Realm, realm)).ToArray();

    /// <summary>A new play session (AutonomousLivingWorld): the realm's first tale comes 25-45 minutes in, never sooner than
    /// three hours after its last one.</summary>
    public static void OnSessionStart(eRealm realm, long now)
    {
        RealmState state = State(realm);
        lock (state)
        {
            long wait = FirstTaleDelayMs + (long)(Random.Shared.NextDouble() * FirstTaleSpreadMs);
            if (RecallUtc(LastKey(realm)) is DateTime last)
                wait = Math.Max(wait, TaleGapMs - (long)(DateTime.UtcNow - last).TotalMilliseconds);
            state.NextTaleAt = now + Math.Max(wait, 60_000);
        }
    }

    /// <summary>Game-loop pulse (AutonomousLivingServer.Pulse), every five seconds: due rumours and raids, fireside tales, and a
    /// realm's own tale when its time has come.</summary>
    public static void Pulse(long now)
    {
        if (now < _nextPulse)
            return;
        _nextPulse = now + 5_000;
        foreach (eRealm realm in Realms)
        {
            RealmState state = State(realm);
            Action[] due;
            bool start;
            lock (state)
            {
                due = state.Due.Where(item => item.At <= now).Select(item => item.Do).ToArray();
                state.Due.RemoveAll(item => item.At <= now);
                start = On && state.Playing == null && now >= state.NextTaleAt;
                if (start)
                    state.NextTaleAt = now + RetryMs;   // moved on again when the tale ends
            }
            foreach (Action action in due)
                Run("aftermath", action);
            if (start && ClientService.Instance.GetPlayersOfRealm(realm).Count > 0)
                Run("start", () => TryStart(realm, null, null, out _));
        }
        if (On)
            Run("fireside", () => PulseFiresides(now));
    }

    private static void Run(string what, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Log.Warn($"Tales: {what} failed", exception);
        }
    }

    /// <summary>The realm's storyteller who is in the game now, choosing new ones while the realm has fewer than two.</summary>
    private static GameBot TellerOnline(eRealm realm, out (long Id, string Name)[] known)
    {
        GameBot[] online = AutonomousBotRegistry.Snapshot().Where(bot => Speaks(bot, realm)).ToArray();
        known = Tellers(realm);
        if (known.Length < TellersPerRealm)
        {
            var taken = known.Select(k => k.Id).ToHashSet();
            for (int slot = 0; slot < TellersPerRealm; slot++)
            {
                if (Recall(TellerKey(realm, slot)) is { Length: > 0 })
                    continue;
                TellerCandidate? pick = PickTeller(online.Where(bot => bot.Level >= 35)
                    .Select(bot => new TellerCandidate(bot.DatabaseID, bot.Name, AutonomousBotPersonality.For(bot).Type, bot.Level)), taken);
                if (pick is not TellerCandidate chosen)
                    break;
                Remember(TellerKey(realm, slot), $"{chosen.Id}|{chosen.Name}");
                taken.Add(chosen.Id);
                Log.Info($"TALE_TELLER realm={GlobalConstants.RealmToName(realm)} slot={slot} bot=\"{chosen.Name}\" type={chosen.Type} level={chosen.Level}");
            }
            known = Tellers(realm);
        }
        var ids = known.Select(k => k.Id).ToHashSet();
        return online.Where(bot => ids.Contains(bot.DatabaseID)).OrderBy(bot => bot.InCombat || bot.IsCasting ? 1 : 0)
            .ThenBy(_ => Random.Shared.Next()).FirstOrDefault();
    }

    /// <summary>
    /// Starts a tale in the realm's /faction: the one named (<paramref name="loreId"/>), else one that fits the moment, else
    /// the one heard longest ago. <paramref name="askedBy"/> is the player who asked for it (the scene then skips its own
    /// asking line). False with the reason when it can't start now.
    /// </summary>
    public static bool TryStart(eRealm realm, string loreId, GamePlayer askedBy, out string why)
    {
        if (!On)
        {
            why = "tales are off (bot_living_tales, or bot voices)";
            return false;
        }
        VC.Scene[] tales = TalesOf(realm);
        if (tales.Length == 0)
        {
            why = "the corpus has no tale for that realm";
            return false;
        }
        if (AutonomousBotDebates.IsRunning(realm))
        {
            why = "a /faction scene is running in that realm; wait for it to end";
            return false;
        }
        GameBot teller = TellerOnline(realm, out var known);
        if (teller == null)
        {
            why = known.Length == 0 ? "no gamebot of that realm can be its storyteller yet (level 35 or more, online)"
                : $"the realm's storyteller ({string.Join(" or ", known.Select(k => k.Name))}) is not online";
            return false;
        }
        loreId ??= FittingLore(realm, tales);
        VC.Scene scene = ChooseTale(tales, loreId, id => RecallUtc(ToldKey(realm, id))?.Ticks ?? 0, Random.Shared);
        if (!AutonomousBotDebates.StartTale(realm, scene, teller.DatabaseID, askedBy != null ? 1 : 0, out why))
            return false;
        RealmState state = State(realm);
        lock (state)
        {
            state.Playing = scene;
            state.TellerId = teller.DatabaseID;
            state.TellerName = teller.Name;
            state.Asides = 0;
            state.Answers.Clear();
            state.RaidRumourNext = false;
        }
        Log.Info($"TALE_START realm={GlobalConstants.RealmToName(realm)} scene={scene.Id} teller=\"{teller.Name}\" asked_by=\"{askedBy?.Name ?? "a bot"}\"");
        return true;
    }

    /// <summary>The tale that fits the moment: of the place a player of the realm stands in, when it has one.</summary>
    private static string FittingLore(eRealm realm, VC.Scene[] tales)
    {
        var told = tales.Select(t => LoreIdOf(t.Topic)).ToHashSet(StringComparer.Ordinal);
        LoreBible bible = LoreBible.Current;
        foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(realm))
        {
            if (player?.CurrentZone == null)
                continue;
            LoreBible.Location here = LoreEntryRules.LocationAt(bible, player.CurrentRegionID, player.CurrentZone.ID);
            if (here != null && bible.BossesAt(here.Id).Select(boss => boss.Id).FirstOrDefault(told.Contains) is { } local &&
                Random.Shared.NextDouble() < 0.6)
                return local;
        }
        return null;
    }

    /// <summary>The scene engine could not start the tale it was handed (no cast, a line with no fitting speaker).</summary>
    internal static void OnTaleNotStarted(eRealm realm, VC.Scene scene, string reason)
    {
        RealmState state = State(realm);
        lock (state)
        {
            state.Playing = null;
            state.NextTaleAt = Math.Min(state.NextTaleAt, GameLoop.GameLoopTime + RetryMs);
        }
        Log.Info($"TALE_NOT_STARTED realm={GlobalConstants.RealmToName(realm)} scene={scene?.Id} reason=\"{reason}\"");
    }

    /// <summary>
    /// A tale's scene ended (AutonomousBotDebates.End; called under its lock, so nothing here calls back into it):
    /// finished, cut for room, or the teller left. A finished tale is remembered and its rumour follows; a tale the teller
    /// left gets one rueful line and is told again another night.
    /// </summary>
    internal static void OnTaleEnded(eRealm realm, VC.Scene scene, bool finished, string reason, int linesSaid)
    {
        long now = GameLoop.GameLoopTime;
        RealmState state = State(realm);
        string teller;
        long tellerId;
        lock (state)
        {
            teller = state.TellerName;
            tellerId = state.TellerId;
            state.Playing = null;
            bool heard = finished || linesSaid >= 6;
            state.NextTaleAt = now + (heard ? TaleGapMs + (long)(Random.Shared.NextDouble() * TaleGapSpreadMs) : RetryMs * 3);
            if (heard)
            {
                Remember(ToldKey(realm, scene.Id), DateTime.UtcNow.ToString("O"));
                Remember(LastKey(realm), DateTime.UtcNow.ToString("O"));
            }
            string loreId = LoreIdOf(scene.Topic);
            string[] heardLines = LinesOf(scene).Concat(state.Answers).ToArray();
            if (finished)
                state.Due.Add((now + AftermathDelayMs + (long)(Random.Shared.NextDouble() * AftermathSpreadMs), () => Aftermath(realm, loreId, tellerId, heardLines)));
            else if (linesSaid >= 4 && reason == "the teller left")
                state.Due.Add((now + 8_000, () => SayAfter(realm, tellerId, "listener",
                    CutShort[Random.Shared.Next(CutShort.Length)].Replace("{teller}", teller ?? "the teller"))));
        }
        Log.Info($"TALE_END realm={GlobalConstants.RealmToName(realm)} scene={scene.Id} teller=\"{teller}\" finished={finished} lines={linesSaid} reason=\"{reason}\"");
    }

    /// <summary>
    /// One line in /faction from a bot of the realm that is free to type and is not <paramref name="notId"/>. Null when it was
    /// said; else why not: no_audience, no_bot, empty, or the sink's hold (scene, ceiling, spacing, duplicate).
    /// </summary>
    private static string TrySay(eRealm realm, long notId, string feature, string role, string text, long exchange)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "empty";
        if (ClientService.Instance.GetPlayersOfRealm(realm).Count == 0)
            return "no_audience";
        GameBot speaker = AutonomousBotRegistry.Snapshot()
            .Where(bot => Speaks(bot, realm) && bot.DatabaseID != notId && !bot.InCombat && !bot.IsCasting)
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (speaker == null)
            return "no_bot";
        using (AutonomousChatExchange.Tag(feature, exchange, 0, role, speaker))
            return AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, text) ? null : AutonomousBotChatCoordinator.LastHeld ?? "held";
    }

    /// <summary>
    /// A line after a tale (the rumour, a listener's grumble): not a scene's line, so it fits under /faction's ceiling like any
    /// other (src=tale_after), but it keeps its turn like a rumour or a gold spammer (AutonomousChatExchange.AfterTry): tried
    /// again at each pulse, given up (LIVING_FEATURE_DROPPED) after three minutes. PC check 2026-11-10, build cf2c873: the
    /// rumour after a finished tale was tried once, in the busy minute after the scene, and lost without a log line.
    /// <paramref name="said"/> runs once the line is in the channel.
    /// </summary>
    private static void SayAfter(eRealm realm, long notId, string role, string text, Action said = null, long exchange = 0, long since = 0)
    {
        long now = GameLoop.GameLoopTime;
        bool first = exchange == 0;
        if (first)
        {
            exchange = AutonomousChatExchange.NewId();
            since = now;
        }
        string why = TrySay(realm, notId, "tale_after", role, text, exchange);
        if (why == null)
        {
            said?.Invoke();
            return;
        }
        bool over = why is "no_audience" or "empty" or "duplicate" ||
            AutonomousChatExchange.AfterTry(false, why, since, AutonomousBotDebates.LastSceneEnd(realm), now) == AutonomousChatExchange.DueStep.GiveUp;
        if (first && !over)
            Log.Info(AutonomousChatExchange.DueLog("tale_after", realm, exchange, $" role={role} held={why}"));
        if (over)
        {
            if (!first)
                Log.Info(AutonomousChatExchange.DroppedLog("tale_after", realm, exchange, now - since, why));
            else
                Log.Info($"TALE_AFTER_NOT_SAID realm={GlobalConstants.RealmToName(realm)} role={role} reason={why}");
            return;
        }
        RealmState state = State(realm);
        lock (state)
            state.Due.Add((now + AutonomousChatExchange.DueRetryMs, () => SayAfter(realm, notId, role, text, said, exchange, since)));
    }

    /// <summary>
    /// After a finished tale: half the time, when the realm's raid on that boss can form, a bot has heard that one is forming
    /// and it opens a few minutes after the line is said (the realm's own automatic raid, sized and invited like any other);
    /// otherwise one of the boss's rumours from its lore file.
    /// </summary>
    private static void Aftermath(eRealm realm, string loreId, long tellerId, string[] heard)
    {
        LoreBible.Boss boss = LoreBible.Current.BossById(loreId);
        string raidId = RaidIdFor(loreId, realm);
        string realmName = GlobalConstants.RealmToName(realm);
        // Why the rumour is not a raid this time, for the log (null: it is).
        RealmState state = State(realm);
        bool asked;
        lock (state)
        {
            asked = state.RaidRumourNext;
            state.RaidRumourNext = false;
        }
        string noRaid = boss == null || raidId == null ? "no raid for this tale"
            : !Properties.BOT_LIVING_TALE_RAIDS ? "bot_living_tale_raids is off"
            : !asked && Random.Shared.NextDouble() >= TrueRumourChance ? "not this time"
            : AutonomousRealmRaid.CouldStartForTale(raidId, realm, out string cannot) ? null : cannot;
        if (noRaid == null)
        {
            string line = RaidRumours[Random.Shared.Next(RaidRumours.Length)].Replace("{boss}", boss.Name.ToLowerInvariant());
            SayAfter(realm, tellerId, "rumour", line, () =>
            {
                long at = GameLoop.GameLoopTime + RaidDelayMs + (long)(Random.Shared.NextDouble() * RaidSpreadMs);
                lock (state)
                    state.Due.Add((at, () =>
                    {
                        bool opened = AutonomousRealmRaid.StartForTale(raidId, realm, out string reason);
                        Log.Info($"TALE_RAID realm={realmName} event={raidId} opened={opened} reason=\"{reason}\"");
                    }));
                Log.Info($"TALE_RUMOUR realm={realmName} about={loreId} kind=raid event={raidId} opens_in_s={(at - GameLoop.GameLoopTime) / 1_000}");
            });
            return;
        }
        // A rumour the tale did not just tell (its own lines and the teller's answers are often the same lore).
        string[] fresh = FreshRumours(boss, realm, heard);
        if (fresh.Length > 0)
            SayAfter(realm, tellerId, "rumour", fresh[Random.Shared.Next(fresh.Length)],
                () => Log.Info($"TALE_RUMOUR realm={realmName} about={loreId} kind=lore no_raid=\"{noRaid}\""));
        else
            Log.Info($"TALE_RUMOUR realm={realmName} about={loreId} kind=none no_raid=\"{noRaid}\"");
    }

    /// <summary>
    /// GM check (/lstest tale ... raid): the rumour after the realm's next finished tale is the raid, without the roll, when
    /// that raid can form. Returns why it could not form right now (null: it could), for the GM's line.
    /// </summary>
    public static string AskRaidRumour(eRealm realm, string loreId)
    {
        RealmState state = State(realm);
        lock (state)
            state.RaidRumourNext = true;
        string raidId = RaidIdFor(loreId, realm);
        return raidId == null ? "no raid for this tale"
            : !Properties.BOT_LIVING_TALE_RAIDS ? "bot_living_tale_raids is off"
            : AutonomousRealmRaid.CouldStartForTale(raidId, realm, out string cannot) ? null : cannot;
    }

    /// <summary>
    /// The boss's rumours a bot of the realm may pass on after its tale, one way of saying each, without those that repeat
    /// <paramref name="heard"/>: the realm's own take first, and when the tale used all of those, the ones every realm tells.
    /// </summary>
    public static string[] FreshRumours(LoreBible.Boss boss, eRealm realm, IEnumerable<string> heard, Random random = null)
    {
        if (boss == null)
            return [];
        random ??= Random.Shared;
        string[] lines = heard?.ToArray() ?? [];
        string[] Fresh(IEnumerable<string> rumours) => (rumours ?? []).Select(rumour => LoreText.Pick(rumour, random))
            .Where(line => !string.IsNullOrWhiteSpace(line) && !Repeats(line, lines)).ToArray();
        string[] own = Fresh(boss.RumourFor(realm));
        return own.Length > 0 ? own : Fresh(boss.Rumour);
    }

    /// <summary>
    /// A player's /faction line (AutonomousBotChatCoordinator.OnPlayerFactionChat). During a tale, a question is answered
    /// by the teller (at most twice a tale); with no scene running, a request for a story starts one, or a bot says the
    /// storyteller is not on. True when the line was handled here.
    /// </summary>
    public static bool OnPlayerFactionChat(GamePlayer player, string message)
    {
        if (!On || player == null || string.IsNullOrWhiteSpace(message))
            return false;
        eRealm realm = player.Realm;
        RealmState state = State(realm);
        VC.Scene playing;
        string teller;
        lock (state)
        {
            playing = state.Playing;
            teller = state.TellerName;
        }
        if (playing != null)
        {
            if (!IsQuestion(message))
                return false;
            lock (state)
            {
                if (state.Asides >= MaxAsides)
                    return false;
                state.Asides++;
            }
            LoreBible.Boss boss = LoreBible.Current.BossById(LoreIdOf(playing.Topic));
            // Not a sentence the tale says itself, or one the teller already answered with.
            string[] heard;
            lock (state)
                heard = LinesOf(playing).Concat(state.Answers).ToArray();
            string[] fresh = FreshRumours(boss, realm, heard);
            string answer = LoreChat.TryAnswer(message, out string known, realm) && !string.IsNullOrWhiteSpace(known) && !Repeats(known, heard)
                ? known
                : fresh.Length > 0 && Random.Shared.NextDouble() < 0.5
                    ? fresh[Random.Shared.Next(fresh.Length)]
                    : Deflections[Random.Shared.Next(Deflections.Length)];
            bool said = AutonomousBotDebates.TellerAside(realm, $"{player.Name}: {answer}");
            if (said)
                lock (state)
                    state.Answers.Add(answer);
            Log.Info($"TALE_ASIDE realm={GlobalConstants.RealmToName(realm)} scene={playing.Id} player=\"{player.Name}\" said={said}");
            return said;
        }
        if (!IsRequest(message) || AutonomousBotDebates.IsRunning(realm))
            return false;
        long now = GameLoop.GameLoopTime;
        lock (state)
        {
            if (now - state.LastRequestAt < 60_000)
                return false;
            state.LastRequestAt = now;
        }
        if (!GapOver(DateTime.UtcNow, RecallUtc(LastKey(realm)), RequestGapMs))
            return false;
        LoreBible bible = LoreBible.Current;
        string loreId = RequestedLore(message, TalesOf(realm).Select(t => LoreIdOf(t.Topic)).Distinct()
            .Select(id => (id, bible.BossById(id) is { } b ? LoreBible.NamesOf(b.Name, b.Id, b.Aliases) : (IEnumerable<string>)[id])));
        if (TryStart(realm, loreId, player, out string why))
            return true;
        Log.Info($"TALE_REQUEST realm={GlobalConstants.RealmToName(realm)} player=\"{player.Name}\" about={loreId ?? "any"} started=False reason=\"{why}\"");
        (long Id, string Name)[] known2 = Tellers(realm);
        if (known2.Length == 0 || known2.Any(k => AutonomousBotRegistry.TryGet(k.Id, out GameBot bot) && Speaks(bot, realm)))
            return false;
        string away = known2[Random.Shared.Next(known2.Length)].Name;
        // An answer to the player: the ceiling doesn't hold it (src=player).
        return TrySay(realm, 0, "player", "answerer", TellerAway[Random.Shared.Next(TellerAway.Length)].Replace("{teller}", away),
            AutonomousChatExchange.NewId()) == null;
    }

    #endregion

    #region Fireside tales (/group)

    /// <summary>Whether a fireside tale may start (pure): the group has been out of a fight long enough, the player is
    /// sitting, and has heard none for a while.</summary>
    public static bool FiresideDue(bool sitting, long quietSince, long lastFireside, long now) =>
        sitting && quietSince > 0 && now - quietSince >= FiresideQuietMs && now - lastFireside >= FiresideGapMs;

    private static void PulseFiresides(long now)
    {
        foreach (var pair in Firesides)
            if (now >= pair.Value.NextAt)
                PlayFireside(pair.Key, pair.Value, now);
        LoreBible bible = LoreBible.Current;
        foreach (eRealm realm in Realms)
            foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRealm(realm))
            {
                Group group = player?.Group;
                string id = player?.ObjectId;
                if (id == null || group == null || player.CurrentZone == null || Firesides.ContainsKey(group))
                {
                    if (id != null)
                        FiresideQuietSince.TryRemove(id, out _);
                    continue;
                }
                GameBot[] mates = group.GetMembersInTheGroup().OfType<GameBot>().Where(bot => Speaks(bot, realm)).ToArray();
                if (mates.Length == 0 || player.InCombat || mates.Any(bot => bot.InCombat))
                {
                    FiresideQuietSince.TryRemove(id, out _);
                    continue;
                }
                long quietSince = FiresideQuietSince.GetOrAdd(id, now);
                if (!FiresideDue(player.IsSitting, quietSince, LastFireside.GetValueOrDefault(id, long.MinValue / 2), now))
                    continue;
                LoreBible.Location here = LoreEntryRules.LocationAt(bible, player.CurrentRegionID, player.CurrentZone.ID);
                if (here == null)
                    continue;
                var local = bible.BossesAt(here.Id).Select(boss => boss.Id).ToHashSet(StringComparer.Ordinal);
                string day = DateTime.Now.ToString("yyyy-MM-dd");
                VC.Scene scene = AutonomousBotVoices.Corpus.Scenes
                    .Where(s => IsFireside(s) && VC.RealmFits(s.Realm, realm) && local.Contains(LoreIdOf(s.Topic)) &&
                                Recall(FiresideKey(id, s.Id)) != day)
                    .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
                if (scene == null)
                    continue;
                StartFireside(player, group, mates, scene, now, day);
            }
    }

    private static void StartFireside(GamePlayer player, Group group, GameBot[] mates, VC.Scene scene, long now, string day)
    {
        GameBot teller = mates.OrderBy(bot => AutonomousBotPersonality.For(bot).Type == BotPersonalityType.Socialite ? 0 : 1)
            .ThenBy(_ => Random.Shared.Next()).First();
        GameBot listener = mates.FirstOrDefault(bot => bot != teller);
        (long Id, string Name)[] tellers = Tellers(player.Realm);
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tellers.Length > 0)
            slots["name_b"] = tellers[Random.Shared.Next(tellers.Length)].Name;
        var fire = new Fireside { Teller = teller, Listener = listener, NextAt = now + 3_000 };
        foreach (VC.Beat beat in scene.Beats)
        {
            if (beat.IsPause || beat.Variants.Length == 0 || beat.Role != "A" && listener == null)
                continue;
            if (beat.Optional && Random.Shared.NextDouble() < 0.4)
                continue;
            string[] filled = AutonomousBotDebates.FilledVariants(beat, slots);
            if (filled.Length > 0)
                fire.Lines.Enqueue((beat.Role, filled[Random.Shared.Next(filled.Length)]));
        }
        if (fire.Lines.Count < 3 || !Firesides.TryAdd(group, fire))
            return;
        LastFireside[player.ObjectId] = now;
        Remember(FiresideKey(player.ObjectId, scene.Id), day);
        Log.Info($"TALE_FIRESIDE player=\"{player.Name}\" scene={scene.Id} teller=\"{teller.Name}\" listener=\"{listener?.Name}\" lines={fire.Lines.Count}");
    }

    private static void PlayFireside(Group group, Fireside fire, long now)
    {
        // A fight, a death or a teller who left the group ends it there: nobody tells stories mid-pull.
        if (fire.Lines.Count == 0 || fire.Teller.Group != group || !fire.Teller.IsAlive || fire.Teller.InCombat ||
            !AutonomousLivingServer.GroupHasPlayer(group))
        {
            Firesides.TryRemove(group, out _);
            return;
        }
        (string role, string text) = fire.Lines.Dequeue();
        GameBot speaker = role == "A" ? fire.Teller : fire.Listener;
        if (speaker != null && speaker.Group == group && speaker.IsAlive)
            AutonomousBotGroupChatter.Send(speaker, text);
        fire.NextAt = now + (fire.Lines.Count > 0 ? AutonomousBotDebates.BeatDelayMs(fire.Lines.Peek().Text) + 1_500 : 0);
        if (fire.Lines.Count == 0)
            Firesides.TryRemove(group, out _);
    }

    /// <summary>GM test (/lstest fireside): the next look treats the GM as rested and due, here and today.</summary>
    public static void ForgetFiresides(GamePlayer player)
    {
        if (player?.ObjectId == null)
            return;
        LastFireside.TryRemove(player.ObjectId, out _);
        FiresideQuietSince[player.ObjectId] = GameLoop.GameLoopTime - FiresideQuietMs;
        foreach (VC.Scene scene in AutonomousBotVoices.Corpus.Scenes.Where(IsFireside))
            if (Recall(FiresideKey(player.ObjectId, scene.Id)) != null)
                Remember(FiresideKey(player.ObjectId, scene.Id), "-");
    }

    #endregion
}
