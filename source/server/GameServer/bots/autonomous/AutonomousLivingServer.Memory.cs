using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Bot memory of you (Living Server). An autonomous gamebot of your realm remembers grouping with
/// you, being rezzed by you and being insulted by you. It greets you when you pass, thanks you for
/// an old rez, answers you by name, sometimes mentions you in /faction, and stays cool after abuse.
/// Memory is chat only: it never changes who groups with whom.
/// </summary>
public static partial class AutonomousLivingServer
{
    public const long GroupedNoteIntervalMs = 30 * 60_000;
    public const long GreetPairCooldownMs = 45 * 60_000;
    public const long GreetPlayerCooldownMs = 3 * 60_000;
    /// <summary>
    /// One greeting per bot per meeting: a bot that greeted a player (or is about to) doesn't greet them again for this long,
    /// whatever cleared the cooldowns (1.1.3 RC1, live check 2026-11-08: Cabowyn said "hey Manawatch! good to see you again"
    /// and a second later "hey hey Manawatch, City of Camelot treat you well after i left?", the GM test having cleared the
    /// cooldowns while the first greeting was on its way).
    /// </summary>
    public const long MeetingMs = 10 * 60_000;

    /// <summary>Whether a bot may greet a player now: no greeting of its on the way, none said in this meeting. Pure.</summary>
    public static bool MayGreet(long lastSaidAt, long pendingUntil, long now) => now >= pendingUntil && now - lastSaidAt >= MeetingMs;

    // When each bot last greeted each player, and until when a greeting of its is on the way (not cleared by the GM tests).
    private static readonly Dictionary<(long BotId, string Player), long> GreetSaidAt = new();
    private static readonly Dictionary<(long BotId, string Player), long> GreetPendingUntil = new();

    private static bool MayGreetNow((long, string) key, long now) =>
        MayGreet(GreetSaidAt.TryGetValue(key, out long said) ? said : long.MinValue / 2,
            GreetPendingUntil.TryGetValue(key, out long pending) ? pending : 0, now);
    public const long GossipRealmCooldownMinMs = 30 * 60_000;
    /// <summary>
    /// A bot greets a player it remembers within this range, well inside the 512 a /say carries, so a bot walking past is
    /// still in earshot when its line comes a second later. (It was 700: a bot past 512 picked the greeting, used up the
    /// player's three-minute greeting cooldown, and its line was dropped unheard. In-game check 2026-10-28: a walking bot
    /// at the edge of /say range also lost its greeting.)
    /// </summary>
    public const int GreetRange = 400;

    private static readonly Dictionary<(long BotId, string Player), long> LastGroupedNote = new();
    private static readonly Dictionary<(long BotId, string Player), long> LastGreeted = new();
    private static readonly Dictionary<string, long> LastGreetOfPlayer = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<eRealm, long> NextGossip = new();
    private static long _nextMemoryScan;

    /// <summary>friendly, neutral or cool: the mood tags of memory.lines.</summary>
    public static string MoodOf(int grouped, int rezzed, int abused)
    {
        int good = grouped + rezzed * 2;
        if (abused > 0 && abused * 2 >= good)
            return "cool";
        return good > 0 ? "friendly" : "neutral";
    }

    public static string MoodOf(OfflineBotMemoryRecord row) => MoodOf(row.Grouped, row.Rezzed, row.Abused);

    /// <summary>Notes an event with a player (topic rez, group or abuse).</summary>
    public static void Remember(GameBot bot, GamePlayer player, string topic)
    {
        if (!MemoryOn || !IsSpeaker(bot) || player == null || player.Realm != bot.Realm)
            return;
        string place = PlaceOf(bot);
        string now = DateTime.UtcNow.ToString("O");
        Memories.Change((bot.DatabaseID, player.Name), () => new OfflineBotMemoryRecord
        {
            BotId = bot.DatabaseID, PlayerName = player.Name, FirstMetUtc = now,
        }, row =>
        {
            row.PlayerClass = ClassOf(player);
            row.PlayerRealm = (int)player.Realm;
            row.LastSeenUtc = now;
            switch (topic)
            {
                case "rez": row.Rezzed++; break;
                case "group": row.Grouped++; break;
                case "abuse": row.Abused++; break;
            }
            if (topic != "abuse")
            {
                row.LastTopic = topic;
                if (!string.IsNullOrEmpty(place))
                    row.LastPlace = place;
            }
        });
    }

    /// <summary>A player insulted bots in chat; those who heard remember it.</summary>
    public static void RecordAbuse(IEnumerable<GameBot> heard, GamePlayer player)
    {
        if (!MemoryOn || heard == null)
            return;
        foreach (GameBot bot in heard.Take(3))
            Remember(bot, player, "abuse");
    }

    /// <summary>GM test (/lstest memory): every bot's memory of a player, most recent first.</summary>
    public static OfflineBotMemoryRecord[] MemoriesOf(GamePlayer player) =>
        Memories.Where(row => string.Equals(row.PlayerName, player.Name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(row => row.LastSeenUtc, StringComparer.Ordinal).ToArray();

    /// <summary>
    /// GM test (/lstest remember): the bot remembers a good group with the player (twice, so gossip about the player is
    /// possible) and the player's greeting cooldowns are cleared, so walking up to the bot gets a greeting.
    /// </summary>
    public static bool RememberForTest(GameBot bot, GamePlayer player)
    {
        if (!MemoryOn || !IsSpeaker(bot) || player == null || player.Realm != bot.Realm)
            return false;
        Remember(bot, player, "group");
        Remember(bot, player, "group");
        GreetSoon(player);
        return true;
    }

    /// <summary>GM test (/lstest greet): a remembered bot within greeting range greets the player on the next scan.</summary>
    public static void GreetSoon(GamePlayer player)
    {
        lock (LastGreeted)
        {
            LastGreetOfPlayer.Remove(player.Name);
            foreach (var key in LastGreeted.Keys.Where(k => string.Equals(k.Item2, player.Name, StringComparison.OrdinalIgnoreCase)).ToArray())
                LastGreeted.Remove(key);
        }
    }

    /// <summary>
    /// GM test (/lstest greet): a remembered bot near the player greets them now. Returns the bot, or null with
    /// <paramref name="why"/>. (In-game check 2026-10-28: the old "on the next scan" version gave no word when nothing fit.)
    /// </summary>
    public static GameBot GreetNow(GamePlayer player, out string why)
    {
        why = null;
        if (!MemoryOn)
        {
            why = "bot memory is off (bot_living_memory)";
            return null;
        }
        GreetSoon(player);
        GameBot[] known = player.GetNPCsInRadius(GreetRange).OfType<GameBot>()
            .Where(bot => bot.Realm == player.Realm && Memories.TryGet((bot.DatabaseID, player.Name), out OfflineBotMemoryRecord row) &&
                          row.Grouped + row.Rezzed + row.Abused > 0).ToArray();
        if (known.Length == 0)
        {
            why = $"no bot within {GreetRange} units remembers you (/lstest remember on one, then stand by it)";
            return null;
        }
        if (!player.IsAlive || player.InCombat)
        {
            why = "you are dead or in combat";
            return null;
        }
        GameBot greeter = TryGreet(player, GameLoop.GameLoopTime);
        if (greeter == null)
        {
            bool met;
            lock (LastGreeted)
                met = !MayGreetNow((known[0].DatabaseID, player.Name), GameLoop.GameLoopTime);
            why = met
                ? $"{known[0].Name} already greeted you in this meeting (once per {MeetingMs / 60_000} minutes)"
                : $"{known[0].Name} remembers you but is busy (fighting, casting, dead or in your group)";
        }
        return greeter;
    }

    /// <summary>GM test (/lstest gossip): a bot that likes the player mentions them in /faction now. Returns the bot, or null with <paramref name="why"/>.</summary>
    public static GameBot GossipNow(GamePlayer player, out string why)
    {
        if (!MemoryOn)
        {
            why = "bot memory is off (bot_living_memory)";
            return null;
        }
        if (AutonomousBotDebates.IsRunning(player.Realm))
        {
            why = "a /faction scene is playing; try again when it ends";
            return null;
        }
        return GossipAbout(player, out why);
    }

    public static bool TryRecall(GameBot bot, GamePlayer player, out OfflineBotMemoryRecord row)
    {
        row = null;
        return MemoryOn && bot != null && player != null && Memories.TryGet((bot.DatabaseID, player.Name), out row);
    }

    private static Dictionary<string, string> MemorySlots(OfflineBotMemoryRecord row, GamePlayer player) => new(StringComparer.Ordinal)
    {
        ["player"] = player.Name,
        ["player_class"] = string.IsNullOrEmpty(row.PlayerClass) ? null : row.PlayerClass.ToLowerInvariant(),
        ["place"] = string.IsNullOrEmpty(row.LastPlace) ? null : row.LastPlace,
    };

    /// <summary>
    /// A reply to a player's chat by a bot that knows them: by name, or a cool brush-off after abuse.
    /// False when the bot doesn't know the player or no line fits (the caller keeps its old reply).
    /// </summary>
    public static bool TryKnownReply(GameBot bot, GamePlayer player, string intent, out string text)
    {
        text = null;
        if (!TryRecall(bot, player, out OfflineBotMemoryRecord row))
            return false;
        Dictionary<string, string> slots = MemorySlots(row, player);
        string mood = MoodOf(row);
        if (mood == "cool")
            return AutonomousBotVoices.TryEventLine(bot, "grudge", out text, slots: slots, flags: ["knows_player"], mood: "cool");
        // Only the lines written for a known player (topic "player"): the general replies don't use the name.
        return AutonomousBotVoices.TryEventLine(bot, "reply", out text, "player", slots, ["knows_player"], intent: intent);
    }

    /// <summary>Once a second on the game loop: grouping notes, greetings, gossip (each on its own timer).</summary>
    private static void MemoryPulse(long now)
    {
        if (now < _nextMemoryScan)
            return;
        _nextMemoryScan = now + 5_000;
        foreach (GamePlayer player in ClientService.Instance.GetPlayers())
        {
            if (player?.Client == null || player.ObjectState != GameObject.eObjectState.Active || player.CurrentRegion == null)
                continue;
            NoteGroupmates(player, now);
            if (player.IsAlive && !player.InCombat)
                TryGreet(player, now);
            TryStartTell(player, now);
        }
        TryGossip(now);
    }

    private static void NoteGroupmates(GamePlayer player, long now)
    {
        if (player.Group == null)
            return;
        foreach (GameBot bot in player.Group.GetMembersInTheGroup().OfType<GameBot>())
        {
            if (!IsSpeaker(bot) || bot.Realm != player.Realm)
                continue;
            var key = (bot.DatabaseID, player.Name);
            lock (LastGroupedNote)
            {
                if (LastGroupedNote.TryGetValue(key, out long last) && now - last < GroupedNoteIntervalMs)
                    continue;
                LastGroupedNote[key] = now;
            }
            Remember(bot, player, "group");
        }
    }

    private static GameBot TryGreet(GamePlayer player, long now)
    {
        lock (LastGreeted)
            if (LastGreetOfPlayer.TryGetValue(player.Name, out long last) && now - last < GreetPlayerCooldownMs)
                return null;
        foreach (GameBot bot in player.GetNPCsInRadius(GreetRange).OfType<GameBot>())
        {
            if (!CanTalk(bot) || bot.Realm != player.Realm || bot.Group != null && bot.Group == player.Group ||
                !Memories.TryGet((bot.DatabaseID, player.Name), out OfflineBotMemoryRecord row) ||
                row.Grouped + row.Rezzed + row.Abused == 0)
                continue;
            var key = (bot.DatabaseID, player.Name);
            int delay = 600 + Random.Shared.Next(1_200);
            lock (LastGreeted)
            {
                if (LastGreeted.TryGetValue(key, out long greeted) && now - greeted < GreetPairCooldownMs || !MayGreetNow(key, now))
                    continue;
                LastGreeted[key] = now;
                LastGreetOfPlayer[player.Name] = now;
                GreetPendingUntil[key] = now + delay + 1_000;
            }
            Dictionary<string, string> slots = MemorySlots(row, player);
            string mood = MoodOf(row);
            bool thanks = mood == "friendly" && row.LastTopic is "rez" or "group" && Random.Shared.NextDouble() < 0.3;
            Later(bot, delay, () => CanTalk(bot) && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE), () =>
            {
                // The last word on it: one greeting per meeting, said or not.
                lock (LastGreeted)
                {
                    GreetPendingUntil.Remove(key);
                    if (!MayGreetNow(key, GameLoop.GameLoopTime))
                        return;
                    GreetSaidAt[key] = GameLoop.GameLoopTime;
                }
                string line = null;
                if (thanks)
                    AutonomousBotVoices.TryEventLine(bot, "thanks", out line, row.LastTopic, slots, ["knows_player", "player_nearby"]);
                if (line == null)
                    AutonomousBotVoices.TryEventLine(bot, "greet", out line, slots: slots, flags: ["knows_player", "player_nearby"], mood: mood);
                if (line != null)
                    Speak(bot, Channel.Say, line);
            });
            return bot;
        }
        return null;
    }

    /// <summary>Now and then a bot that likes you mentions you in /faction (never one standing next to you).</summary>
    private static void TryGossip(long now)
    {
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            List<GamePlayer> players = ClientService.Instance.GetPlayersOfRealm(realm);
            if (players.Count == 0)
                continue;
            lock (NextGossip)
            {
                if (!NextGossip.TryGetValue(realm, out long next))
                {
                    NextGossip[realm] = now + GossipRealmCooldownMinMs / 2 + Random.Shared.Next(15 * 60_000);
                    continue;
                }
                if (now < next)
                    continue;
                NextGossip[realm] = now + GossipRealmCooldownMinMs + Random.Shared.Next(30 * 60_000);
            }
            if (AutonomousBotDebates.IsRunning(realm))
                continue;
            // 1.1.3: gossip naming the player takes one of their turns (AutonomousChatEngagement), so it never piles on.
            GamePlayer subject = players[Random.Shared.Next(players.Count)];
            if (EngageTurn(subject, now))
                GossipAbout(subject, out _);
        }
    }

    /// <summary>
    /// A bot that likes the player (two good groups or rezzes), is free and isn't standing next to them, mentions them in
    /// /faction. Returns the bot, or null with <paramref name="why"/> (for /lstest gossip).
    /// </summary>
    private static GameBot GossipAbout(GamePlayer subject, out string why)
    {
        why = null;
        OfflineBotMemoryRecord[] friends = Memories.Where(row => string.Equals(row.PlayerName, subject.Name, StringComparison.OrdinalIgnoreCase) &&
                                                                 MoodOf(row) == "friendly" && row.Grouped + row.Rezzed >= 2);
        if (friends.Length == 0)
        {
            why = "no bot likes you enough yet (two good groups or rezzes; /lstest remember)";
            return null;
        }
        foreach (OfflineBotMemoryRecord row in friends.OrderBy(_ => Random.Shared.Next()))
        {
            if (!AutonomousBotRegistry.TryGet(row.BotId, out GameBot bot) || !CanTalk(bot) ||
                bot.CurrentRegion == subject.CurrentRegion && bot.IsWithinRadius(subject, WorldMgr.SAY_DISTANCE * 2))
                continue;
            // Lines about a rez need a rez this bot remembers (in-game check 2026-10-18).
            var flags = new List<string> { "knows_player" };
            if (AutonomousBotGuildSocial.CountsAsGuildless(subject.Guild)) flags.Add("player_guildless");
            if (row.Rezzed > 0) flags.Add("player_rezzed_me");
            if (AutonomousBotVoices.TryEventLine(bot, "gossip", out string line, slots: MemorySlots(row, subject),
                    flags: flags.ToArray(), mood: "friendly"))
            {
                // About the real player (1.1.3: a line about you is not held by the ceiling).
                using (AutonomousChatExchange.Tag("player", AutonomousChatExchange.NewId(), 0, "gossip", bot))
                    Speak(bot, Channel.Faction, line);
                return bot;
            }
            why = $"{bot.Name} had no gossip line that fits";
            return null;
        }
        why = $"the {friends.Length} bot(s) that like you are offline, busy, or within {WorldMgr.SAY_DISTANCE * 2} units of you (they greet you instead)";
        return null;
    }
}
