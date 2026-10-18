using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Bot voices (docs/bot-voices/DESIGN.md, part of the Social Expansion): live bots say lines from
/// the embedded corpus instead of the generic phrase lists, chosen by kind, realm, class, level,
/// personality and situation, written in the bot's own style, never repeating a line in a realm
/// within 6 hours. Every caller keeps its old line when nothing fits or the setting is off.
/// </summary>
public static class AutonomousBotVoices
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly Lazy<VC> Loaded = new(Load);
    private static readonly ConcurrentDictionary<long, long> LeveledAt = new();
    private static readonly ConcurrentDictionary<long, long> DiedAt = new();
    public static readonly VC.RecentLines Recent = new();

    public const long JustLeveledMs = 3 * 60_000;
    public const long JustDiedMs = 3 * 60_000;

    public static VC Corpus => Loaded.Value;

    public static bool Enabled => Properties.BOT_VOICES_ENABLED && Corpus != null;

    /// <summary>
    /// Parses the 1.27 MB corpus at start-up, off the game loop, when bot voices or any Living Server part is on.
    /// Audit 2026-10-17: it loaded lazily on a game-loop thread the first time a line or /personality needed it.
    /// A caller that needs it before the preload finishes waits for that one load (Lazy is thread-safe).
    /// </summary>
    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        // Guild chat uses the corpus too (0.36-se.4), with or without bot voices.
        if (Properties.BOT_VOICES_ENABLED || AutonomousLivingServer.AnyOn || Properties.BOT_GUILDS_ENABLED && Properties.BOT_GUILD_SOCIAL)
            Preload();
    }

    public static void Preload() => ThreadPool.QueueUserWorkItem(_ => _ = Loaded.Value);

    private static VC Load()
    {
        try
        {
            Assembly assembly = typeof(AutonomousBotVoices).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith(suffix, StringComparison.Ordinal));
                if (name == null)
                    throw new FileNotFoundException($"embedded resource *{suffix} is missing");
                using Stream stream = assembly.GetManifestResourceStream(name);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            VC corpus = VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
            if (Log.IsInfoEnabled)
                Log.Info($"Bot voices: loaded {corpus.Lines.Length} lines, {corpus.Scenes.Length} scenes, {corpus.Voices.Length} voices");
            return corpus;
        }
        catch (Exception exception)
        {
            Log.Error("Bot voices: could not load the corpus; bots keep their old lines", exception);
            return null;
        }
    }

    #region Context

    public static void NoteLevelUp(GameBot bot)
    {
        if (bot?.DatabaseID > 0)
            LeveledAt[bot.DatabaseID] = GameLoop.GameLoopTime;
    }

    /// <summary>True when the bot levelled in the last few minutes (it is the one a "gz" is meant for).</summary>
    public static bool JustLeveled(GameBot bot) =>
        bot?.DatabaseID > 0 && LeveledAt.TryGetValue(bot.DatabaseID, out long leveled) && GameLoop.GameLoopTime - leveled < JustLeveledMs;

    public static void NoteDeath(GameBot bot)
    {
        if (bot?.DatabaseID > 0)
            DiedAt[bot.DatabaseID] = GameLoop.GameLoopTime;
    }

    public static VC.Speaker SpeakerFor(GameBot bot)
    {
        var flags = new HashSet<string>(StringComparer.Ordinal);
        OfflineWorldBotRecord record = bot.PersistentRecord;
        flags.Add(bot.Group != null ? "in_group" : "solo");
        if (InDungeon(bot)) flags.Add("in_dungeon");
        if (bot.CurrentZone?.IsRvR == true || bot.CurrentRegion?.IsRvR == true)
        {
            flags.Add("in_rvr");
            // 0.36-se.4: a battleground (Abermenai, Thidranki, Murdaigean, Caledonia, Cathal Valley) or the frontier proper
            flags.Add(IsBattlegroundRegion(bot.CurrentRegionID) ? "in_battleground" : "in_frontier");
        }
        if (!string.IsNullOrEmpty(record?.TravelDestination)) flags.Add("traveling");
        else if (!string.IsNullOrEmpty(record?.CurrentCampId)) flags.Add("at_camp");
        if (record != null && AutonomousObjectiveAssignments.IsIntentionalTownIdle(record, DateTime.UtcNow)) flags.Add("in_town");
        if (LeveledAt.TryGetValue(bot.DatabaseID, out long leveled) && GameLoop.GameLoopTime - leveled < JustLeveledMs) flags.Add("just_leveled");
        if (DiedAt.TryGetValue(bot.DatabaseID, out long died) && GameLoop.GameLoopTime - died < JustDiedMs) flags.Add("just_died");
        if (AutonomousBotEconomy.IsBackpackNearlyFull(bot)) flags.Add("bag_full");
        // 0.36-se.4: where and when, and who the bot is with.
        if (IsCapitalRegion(bot.CurrentRegionID)) flags.Add("in_capital");
        if (!flags.Contains("in_dungeon") && !flags.Contains("in_capital")) flags.Add("outdoors");
        flags.Add(VC.TimeFlag(DateTime.Now.Hour));
        if (bot.IsPlayerLedGroup || AutonomousLivingServer.GroupHasPlayer(bot.Group)) flags.Add("in_player_group");
        if (bot.ResurrectionSpell != null && bot.ResurrectionSpell.Level <= bot.Level) flags.Add("can_rez");
        return new VC.Speaker(bot.DatabaseID, bot.Name, bot.Realm, bot.CharacterClass?.Name ?? bot.ClassName ?? string.Empty,
            bot.Level, AutonomousBotPersonality.For(bot).Type, flags);
    }

    /// <summary>The battlegrounds of this world (the Battleground table): Cathal Valley 165, Caledonia 250, Murdaigean 251,
    /// Thidranki 252, Abermenai 253.</summary>
    public static bool IsBattlegroundRegion(ushort regionId) => regionId is 165 or 250 or 251 or 252 or 253;

    /// <summary>Camelot City, Jordheim and Tir na Nog.</summary>
    public static bool IsCapitalRegion(ushort regionId) => regionId is 10 or 101 or 201;

    /// <summary>Whether a bot is in a dungeon. The capitals are one zone at the dungeon offset, so the server calls them
    /// dungeons; in the live test of se.4 a bot in Camelot said "lfg City of Camelot".</summary>
    public static bool InDungeon(GameBot bot) =>
        !IsCapitalRegion(bot.CurrentRegionID) && (bot.CurrentRegion?.IsDungeon == true || bot.CurrentZone?.IsDungeon == true);

    /// <summary>A realm dungeon a bot of this level would use (Kirstena's 2002 ranges, checked against the world database).</summary>
    public static string DungeonFor(eRealm realm, int level)
    {
        (string Name, int Low, int High)[] dungeons = realm switch
        {
            // The Catacombs of Cardova (24-40) is left out: players read "catacombs" as the 2004 expansion
            // (audit 2026-10-16); Keltoi Fogou and the Barrows cover those levels.
            eRealm.Albion => [("Tomb of Mithra", 7, 17), ("Tepok's Mine", 16, 24), ("Keltoi Fogou", 19, 32),
                ("Stonehenge Barrows", 32, 50)],
            eRealm.Midgard => [("Nisse's Lair", 8, 21), ("Cursed Tomb", 18, 28), ("Vendo Caverns", 20, 35),
                ("Varulvhamn", 25, 40), ("Spindelhalla", 33, 50)],
            eRealm.Hibernia => [("Muire Tomb", 12, 20), ("Spraggon Den", 15, 25), ("Koalinth Tribal Caverns", 20, 30),
                ("Treibh Caillte", 27, 40), ("Coruscating Mine", 30, 45)],
            _ => [],
        };
        var fitting = dungeons.Where(d => level >= d.Low - 2 && level <= d.High + 2).ToArray();
        if (fitting.Length == 0)
            fitting = dungeons.OrderBy(d => Math.Abs((d.Low + d.High) / 2 - level)).Take(1).ToArray();
        return fitting.Length == 0 ? null : fitting[Random.Shared.Next(fitting.Length)].Name;
    }

    public static string CapitalOf(eRealm realm) => realm switch
    {
        eRealm.Albion => "Camelot",
        eRealm.Midgard => "Jordheim",
        eRealm.Hibernia => "Tir na Nog",
        _ => null,
    };

    public static string EnemyOf(eRealm realm, Random random)
    {
        eRealm[] others = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.Where(r => r != realm).ToArray();
        return others.Length == 0 ? null : GlobalConstants.RealmToName(others[random.Next(others.Length)]);
    }

    /// <summary>A frontier keep of the realm and the zone it stands in (so a line names a real pair).</summary>
    public static (string Keep, string Zone) FrontierKeepOf(eRealm realm, Random random)
    {
        try
        {
            AbstractGameKeep[] keeps = GameServer.KeepManager.GetFrontierKeeps()
                .Where(keep => keep.Realm == realm && !keep.IsPortalKeep && !string.IsNullOrEmpty(keep.Name)).ToArray();
            if (keeps.Length == 0)
                return (null, null);
            AbstractGameKeep chosen = keeps[random.Next(keeps.Length)];
            return (chosen.Name, chosen.CurrentRegion?.GetZone(chosen.X, chosen.Y)?.Description);
        }
        catch
        {
            return (null, null);
        }
    }

    /// <summary>
    /// Slot values from a live bot. Shared values (keep, frontier, enemy, capital, dungeon, camp)
    /// can be passed in so every speaker in a scene names the same ones.
    /// </summary>
    public static Dictionary<string, string> SlotsFor(GameBot bot, Random random, IReadOnlyDictionary<string, string> shared = null)
    {
        OfflineWorldBotRecord record = bot.PersistentRecord;
        string zone = bot.CurrentZone?.Description;
        string item = bot.Inventory?.AllItems.FirstOrDefault(entry => entry != null &&
            entry.SlotPosition >= (int)eInventorySlot.FirstBackpack && entry.SlotPosition <= (int)eInventorySlot.LastBackpack)?.Name;
        string monster = !string.IsNullOrWhiteSpace(record?.TargetName) ? record.TargetName.Trim().ToLowerInvariant() : null;
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["me"] = bot.Name,
            ["class"] = (bot.CharacterClass?.Name ?? bot.ClassName)?.ToLowerInvariant(),
            ["level"] = bot.Level.ToString(),
            ["realm"] = GlobalConstants.RealmToName(bot.Realm),
            ["zone"] = zone,
            ["monster"] = monster,
            ["monsters"] = monster == null ? null : VC.Plural(monster),
            ["item"] = item,
            ["count"] = random.Next(2, 5).ToString(),
            ["q"] = VC.Openers[random.Next(VC.Openers.Length)],
        };
        if (shared != null)
        {
            foreach (var pair in shared)
                slots[pair.Key] = pair.Value;
        }
        else
        {
            foreach (var pair in SharedSlots(bot.Realm, bot.Level, random, InDungeon(bot) ? zone : null))
                slots[pair.Key] = pair.Value;
            // A bot at a camp names where it really is, not a dungeon for its level (audit 2026-10-16).
            if (!string.IsNullOrEmpty(record?.CurrentCampId) && !string.IsNullOrEmpty(zone))
                slots["camp"] = zone;
        }
        return slots;
    }

    /// <summary>Values a whole conversation shares: one keep, one frontier, one enemy, one capital, one dungeon.</summary>
    public static Dictionary<string, string> SharedSlots(eRealm realm, int level, Random random, string currentDungeon = null)
    {
        (string keep, string frontier) = FrontierKeepOf(realm, random);
        string dungeon = currentDungeon ?? DungeonFor(realm, level);
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["keep"] = keep,
            ["frontier"] = frontier,
            ["enemy"] = EnemyOf(realm, random),
            ["capital"] = CapitalOf(realm),
            ["dungeon"] = dungeon,
            ["camp"] = dungeon,
        };
    }

    #endregion

    /// <summary>
    /// A reply to a greeting: "how is everyone?" gets "not bad, you?", a plain "hi all" a hello and never an answer to a
    /// question nobody asked (live test 2026-10-18: "hi all" drew "not bad, and you?" in /faction and "good just dinged
    /// actually" in /gu).
    /// </summary>
    public static bool TryGreetingReply(GameBot bot, string incoming, out VC.Picked result, IReadOnlyDictionary<string, string> slots = null,
        bool relax = false, bool requireVoices = true)
    {
        bool asked = AutonomousChatShortTalk.AsksHowAreYou(incoming);
        if (asked && TryLine(bot, "reply", out result, answers: "ask_howareyou", slots: slots, relax: relax, requireVoices: requireVoices))
            return true;
        return TryLine(bot, "reply", out result, IntentName(eAutonomousChatIntent.Greeting), slots: slots, relax: relax,
            requireVoices: requireVoices, filter: line => asked || !line.Answers.Contains("ask_howareyou", StringComparer.Ordinal));
    }

    /// <summary>A reply for the kind of question a player asked ("anyone on?" -> "here"), before replies by topic.</summary>
    public static bool TryShapedReply(GameBot bot, string incoming, out VC.Picked result, IReadOnlyDictionary<string, string> slots = null,
        bool relax = false, bool requireVoices = true)
    {
        result = null;
        string shape = AutonomousChatShortTalk.QuestionShape(incoming);
        return shape is "ask_on" or "ask_permission" or "ask_choice" or "ask_guild" &&
               TryLine(bot, "reply", out result, answers: shape, slots: slots, relax: relax, requireVoices: requireVoices);
    }

    /// <summary>A corpus line of a kind for this bot, or false (the caller keeps its old line).</summary>
    public static bool TryLine(GameBot bot, string kind, out string text, string intent = null, Random random = null)
    {
        bool picked = TryLine(bot, kind, out VC.Picked result, intent: intent, random: random);
        text = result?.Text;
        return picked;
    }

    /// <summary>How long a short, common reply ("np", "lol", "where?") rests before it may be said again once the
    /// six-hour pool for its kind is used up (0.36-se.4: a player is never left unanswered because the pool ran dry).</summary>
    public static readonly TimeSpan ShortReplyRest = TimeSpan.FromMinutes(40);

    /// <summary>
    /// A corpus line for this bot with everything the caller may need (the corpus line, so its invitation;
    /// a typo correction). Extra slots and flags are merged in; <paramref name="answers"/> picks replies or
    /// closings that fit an opener's invitation.
    /// </summary>
    public static bool TryLine(GameBot bot, string kind, out VC.Picked result, string intent = null, string answers = null,
        string topic = null, IReadOnlyDictionary<string, string> slots = null, IEnumerable<string> flags = null,
        IEnumerable<string> withoutFlags = null, bool relax = false, bool requireVoices = true, Random random = null,
        string requireNeed = null, Func<VC.Line, bool> filter = null)
    {
        result = null;
        if ((requireVoices ? !Enabled : Corpus == null) || bot == null || !bot.IsAutonomousWorldBot || bot.DatabaseID <= 0)
            return false;
        random ??= Random.Shared;
        try
        {
            VC.Speaker speaker = SpeakerFor(bot);
            if (flags != null || withoutFlags != null)
            {
                var merged = new HashSet<string>(speaker.Flags, StringComparer.Ordinal);
                if (flags != null)
                    merged.UnionWith(flags);
                if (withoutFlags != null)
                    merged.ExceptWith(withoutFlags);
                speaker = speaker with { Flags = merged };
            }
            Dictionary<string, string> all = SlotsFor(bot, random);
            if (slots != null)
                foreach (var pair in slots)
                    all[pair.Key] = pair.Value;
            return Corpus.TryPick(new VC.Request(kind, speaker, all, Recent, DateTime.UtcNow, random)
            {
                Intent = intent,
                Answers = answers,
                Topic = topic,
                RequireNeed = requireNeed,
                Filter = filter,
                // Answering a player: a short reply may come back after 40 minutes. Anything else: after six hours,
                // once the three-day pool for its kind is used up.
                RelaxTo = relax ? ShortReplyRest : VC.RecentLines.Relaxed,
                LocalHour = DateTime.Now.Hour,
            }, out result);
        }
        catch (Exception exception)
        {
            Log.Warn($"Bot voices: no line for {bot.Name} ({kind})", exception);
            return false;
        }
    }

    /// <summary>A text in this bot's writing style (knowledge answers and other fixed text).</summary>
    public static string StyleFor(GameBot bot, string text) =>
        !Enabled || bot == null || string.IsNullOrEmpty(text) ? text : VC.Style(text, Corpus.VoiceFor(bot.DatabaseID), Random.Shared);

    /// <summary>
    /// A line for a Living Server event (docs/LIVING-SERVER.md). Needs only the corpus, so it works
    /// whether bot voices are on or off. The caller adds the event's slots ({target}, {player},
    /// {killer}...) and flags (knows_player, player_nearby...). Short, frequent kinds (grats, rez
    /// thanks) pass noRepeat false so a "gz" can be said again within the six-hour window.
    /// </summary>
    public static bool TryEventLine(GameBot bot, string kind, out string text, string topic = null,
        IReadOnlyDictionary<string, string> slots = null, IEnumerable<string> flags = null, bool noRepeat = true,
        string intent = null, string mood = null, Random random = null)
    {
        text = null;
        if (Corpus == null || bot == null || !bot.IsAutonomousWorldBot || bot.DatabaseID <= 0)
            return false;
        random ??= Random.Shared;
        try
        {
            VC.Speaker speaker = SpeakerFor(bot);
            if (flags != null)
            {
                var merged = new HashSet<string>(speaker.Flags, StringComparer.Ordinal);
                merged.UnionWith(flags);
                speaker = speaker with { Flags = merged };
            }
            Dictionary<string, string> all = SlotsFor(bot, random);
            if (slots != null)
                foreach (var pair in slots)
                    all[pair.Key] = pair.Value;
            bool picked = Corpus.TryPick(new VC.Request(kind, speaker, all, noRepeat ? Recent : null, DateTime.UtcNow, random)
            {
                Intent = intent,
                Topic = topic,
                Mood = mood,
                RelaxTo = VC.RecentLines.Relaxed,
                LocalHour = DateTime.Now.Hour,
            }, out VC.Picked result);
            text = result?.Text;
            return picked;
        }
        catch (Exception exception)
        {
            Log.Warn($"Living Server: no {kind} line for {bot.Name}", exception);
            return false;
        }
    }

    /// <summary>Picks the kind of an unprompted line from the bot's situation and personality.</summary>
    public static string AmbientKind(GameBot bot, bool faction, Random random)
    {
        var speaker = SpeakerFor(bot);
        BotPersonalityType type = speaker.Type;
        var options = new List<(string Kind, double Weight)> { ("ambient", 6) };
        if (speaker.Flags.Contains("solo") && bot.Level >= 5)
            options.Add(("lfg", type is BotPersonalityType.Socialite or BotPersonalityType.Leveler ? 3 : 1.5));
        if (type == BotPersonalityType.Crafter || speaker.Flags.Contains("bag_full"))
            options.Add(("trade", type == BotPersonalityType.Crafter ? 3 : 1.5));
        if (speaker.Flags.Contains("in_rvr") || bot.Level >= 40 && type is BotPersonalityType.NoLifer or BotPersonalityType.Daredevil)
            options.Add(("rvr", speaker.Flags.Contains("in_rvr") ? 4 : 1));
        if (!faction && speaker.Flags.Contains("at_camp"))
            options.Add(("camp", 2));
        // A bot group near the player talks among itself in /say (0.36-se.4: these lines went to /faction).
        if (!faction && speaker.Flags.Contains("in_group") && !speaker.Flags.Contains("in_player_group"))
            options.Add(("group", 3));
        double draw = random.NextDouble() * options.Sum(o => o.Weight);
        foreach ((string kind, double weight) in options)
        {
            if (draw < weight)
                return kind;
            draw -= weight;
        }
        return "ambient";
    }

    /// <summary>Intent names in the corpus are the chat model's, lowercased.</summary>
    public static string IntentName(eAutonomousChatIntent intent) => intent.ToString().ToLowerInvariant();

    #region Moments (ding, death) in /say when a player is near

    public static void OnBotLevelUp(GameBot bot)
    {
        NoteLevelUp(bot);
        SayMoment(bot, "ding", 0.35, 1_500);
    }

    public static void OnBotDeath(GameBot bot)
    {
        NoteDeath(bot);
        SayMoment(bot, "death", 0.25, 3_000);
    }

    private static void SayMoment(GameBot bot, string kind, double chance, int delayMs)
    {
        if (!Enabled || bot?.CurrentRegion == null || Random.Shared.NextDouble() >= chance)
            return;
        bool heard = ClientService.Instance.GetPlayersOfRegion(bot.CurrentRegion)
            .Any(player => player.ObjectState == GameObject.eObjectState.Active && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE));
        if (!heard)
            return;
        _ = new ECSGameTimer(bot, _ =>
        {
            if (bot.ObjectState == GameObject.eObjectState.Active && TryLine(bot, kind, out string line))
                bot.Say(line);
            return 0;
        }, delayMs + Random.Shared.Next(1_500));
    }

    #endregion
}
