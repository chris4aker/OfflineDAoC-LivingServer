using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using Lines = DOL.GS.AutonomousBotGuildChatLines;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Guild social life for autonomous gamebots (docs/BOT-GUILDS.md, phase 2): guild chat when a real
/// member can hear it, greetings and congratulations, replies to your /gu, recruiting in /faction,
/// and bot officers inviting you to their guild with a yes/no dialog. Bots never "talk" to a guild
/// channel nobody hears. All state here is in memory; nothing is saved.
/// </summary>
public static class AutonomousBotGuildSocial
{
    public const long PlayerGreetingCooldownMs = 10 * 60_000;
    public const long GuildReplyCooldownMs = 6_000;
    public const long GuildDeathCooldownMs = 2 * 60_000;
    public const long RealmRecruitCooldownMs = 25 * 60_000;
    public const long RecruitOpenMs = 5 * 60_000;
    public static readonly TimeSpan DeclineMemory = TimeSpan.FromHours(24);
    public static readonly TimeSpan NearbyOfferMemory = TimeSpan.FromHours(24);

    private static readonly object Sync = new();
    private static readonly Dictionary<string, long> LastIdleByGuild = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LastReplyByGuild = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LastDeathByGuild = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LastGreetingByPlayer = new(StringComparer.Ordinal);
    private static readonly Dictionary<eRealm, long> LastRecruitByRealm = new();
    private static readonly Dictionary<eRealm, (string GuildId, long Tick)> OpenRecruit = new();
    private static readonly Dictionary<string, DateTime> DeclinedUntil = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, DateTime> LastNearbyOffer = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> LastTooLowAnswer = new(StringComparer.Ordinal);

    /// <summary>
    /// What each guild heard in the last hour. Live run of 2026-10-21: "died. the run back is the worst part" three times in /gu
    /// within 17 minutes, and "guild warband forming, frontier bound" twice 30 seconds apart, from the short fixed lists for deaths
    /// and new groups. Those lines are now picked from the ones the guild hasn't heard lately, or not said at all.
    /// </summary>
    public const int RecentGuildLineMs = 60 * 60_000;
    private static readonly Dictionary<string, Dictionary<string, long>> RecentByGuild = new(StringComparer.Ordinal);

    /// <summary>A line from <paramref name="line"/> this guild hasn't heard in the last hour (a few draws), or null.</summary>
    public static string FreshLine(string guildId, Func<string> line, long now, int draws = 8)
    {
        lock (Sync)
        {
            RecentByGuild.TryGetValue(guildId ?? string.Empty, out Dictionary<string, long> heard);
            for (int i = 0; i < draws; i++)
            {
                string text = line()?.Trim();
                if (string.IsNullOrEmpty(text))
                    continue;
                if (heard == null || !heard.TryGetValue(LineKey(text), out long at) || now - at >= RecentGuildLineMs)
                    return text;
            }
            return null;
        }
    }

    /// <summary>
    /// The line as the guild hears it: numbers don't make it new (live run 2026-10-22: "2 of us heading out to the frontier"
    /// then "3 of us heading out to the frontier" a minute later).
    /// </summary>
    public static string LineKey(string text) =>
        System.Text.RegularExpressions.Regex.Replace((text ?? string.Empty).Trim().ToLowerInvariant(), @"\d+", "#");

    /// <summary>Notes a line the guild heard (every bot line in /gu goes through here).</summary>
    public static void NoteHeard(string guildId, string text, long now)
    {
        if (string.IsNullOrEmpty(text))
            return;
        lock (Sync)
        {
            if (!RecentByGuild.TryGetValue(guildId ?? string.Empty, out Dictionary<string, long> heard))
                RecentByGuild[guildId ?? string.Empty] = heard = new Dictionary<string, long>(StringComparer.Ordinal);
            heard[LineKey(text)] = now;
            if (heard.Count > 200)
                foreach (string old in heard.Where(pair => now - pair.Value >= RecentGuildLineMs).Select(pair => pair.Key).ToList())
                    heard.Remove(old);
        }
    }

    public static bool Enabled => AutonomousBotRealmGuilds.Enabled && Properties.BOT_GUILD_SOCIAL;

    private static bool InvitesEnabled => Enabled && Properties.BOT_GUILD_RECRUIT_PLAYER;

    #region Pure rules (tested)

    /// <summary>Minimum gap between unprompted guild-chat lines, by charter and online bot members.</summary>
    public static long IdleIntervalMs(BotGuildCharter charter, int onlineBots)
    {
        long baseMs = charter switch
        {
            BotGuildCharter.Social => 4 * 60_000,
            BotGuildCharter.Leveling => 6 * 60_000,
            BotGuildCharter.Raiding => 8 * 60_000,
            BotGuildCharter.Rvr => 7 * 60_000,
            _ => 9 * 60_000,
        };
        // Busier guilds talk more, up to three times as often.
        double busy = Math.Clamp(Math.Sqrt(Math.Max(1, onlineBots) / 4.0), 1.0, 3.0);
        return (long)(baseMs / busy);
    }

    /// <summary>How many bots answer a guild event or line.</summary>
    public static int ResponderCount(Lines.ReplyKind kind, int available, double roll)
    {
        if (available <= 0)
            return 0;
        int wanted = kind switch
        {
            Lines.ReplyKind.Greeting => roll < 0.35 ? 3 : roll < 0.75 ? 2 : 1,
            Lines.ReplyKind.Farewell => roll < 0.4 ? 2 : 1,
            Lines.ReplyKind.Thanks or Lines.ReplyKind.Grouping or Lines.ReplyKind.Abuse => 1,
            // A question to your own guild is answered (played 2026-10-18: "where is everyone?" drew nothing).
            Lines.ReplyKind.Question => roll < 0.3 ? 2 : 1,
            Lines.ReplyKind.Banter => roll < 0.4 ? 1 : 0,
            _ => 0,
        };
        return Math.Min(available, wanted);
    }

    public readonly record struct GuildChoice(string GuildId, BotGuildCharter Charter, int Members, bool HasSpeaker);

    /// <summary>
    /// Which bot guild answers a player asking for one: the guild that just recruited when it can
    /// speak, otherwise the speaking guild whose charter best fits the player's level, then the
    /// larger one, then the lowest id (stable).
    /// </summary>
    public static string ChooseGuildForPlayer(IReadOnlyList<GuildChoice> guilds, int playerLevel, string openRecruitGuildId)
    {
        if (guilds == null || guilds.Count == 0)
            return null;
        GuildChoice open = guilds.FirstOrDefault(g => g.GuildId == openRecruitGuildId && g.HasSpeaker);
        if (open.GuildId != null)
            return open.GuildId;
        return guilds.Where(g => g.HasSpeaker)
            .OrderByDescending(g => AutonomousBotGuildPlanner.Fit(g.Charter, playerLevel))
            .ThenByDescending(g => g.Members)
            .ThenBy(g => g.GuildId, StringComparer.Ordinal)
            .Select(g => g.GuildId)
            .FirstOrDefault();
    }

    #endregion

    #region Event hooks

    [ScriptLoadedEvent]
    public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args)
    {
        GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));
        GameEventMgr.AddHandler(GamePlayerEvent.LevelUp, new DOLEventHandler(OnPlayerLevelUp));
    }

    [ScriptUnloadedEvent]
    public static void OnScriptUnloaded(DOLEvent e, object sender, EventArgs args)
    {
        GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));
        GameEventMgr.RemoveHandler(GamePlayerEvent.LevelUp, new DOLEventHandler(OnPlayerLevelUp));
    }

    private static void OnPlayerEntered(DOLEvent e, object sender, EventArgs args)
    {
        if (!Enabled || sender is not GamePlayer player || player.Guild == null)
            return;
        lock (Sync)
        {
            if (LastGreetingByPlayer.TryGetValue(player.InternalID ?? player.Name, out long last) &&
                GameLoop.GameLoopTime - last < PlayerGreetingCooldownMs)
                return;
            LastGreetingByPlayer[player.InternalID ?? player.Name] = GameLoop.GameLoopTime;
        }
        Guild guild = player.Guild;
        GameBot[] speakers = Speakers(guild);
        int count = ResponderCount(Lines.ReplyKind.Greeting, speakers.Length, Random.Shared.NextDouble());
        for (int i = 0; i < count; i++)
        {
            GameBot bot = speakers[i];
            ScheduleGuildLine(bot, guild, 4_000 + i * 2_500 + Random.Shared.Next(4_000), 
                // live run 2026-10-22: two guildmates both said "hey Claudecheck, good timing" 2 s apart
                () => FreshLine(guild.GuildID, () => Lines.Greeting(player.Name, Random.Shared), GameLoop.GameLoopTime));
        }
    }

    private static void OnPlayerLevelUp(DOLEvent e, object sender, EventArgs args)
    {
        if (!Enabled || sender is not GamePlayer player || player.Guild == null)
            return;
        Guild guild = player.Guild;
        GameBot[] speakers = Speakers(guild);
        int count = Math.Min(speakers.Length, 1 + (Random.Shared.NextDouble() < 0.5 ? 1 : 0) + (Random.Shared.NextDouble() < 0.25 ? 1 : 0));
        int level = player.Level;
        for (int i = 0; i < count; i++)
        {
            GameBot bot = speakers[i];
            ScheduleGuildLine(bot, guild, 2_000 + i * 1_800 + Random.Shared.Next(2_500), 
                () => FreshLine(guild.GuildID, () => Lines.Congratulate(player.Name, level, Random.Shared), GameLoop.GameLoopTime));
        }
    }

    /// <summary>Called by GameBot when an autonomous gamebot gains one or more levels.</summary>
    public static void OnBotLevelUp(GameBot bot)
    {
        if (!Enabled || bot?.Guild == null || !HasListener(bot.Guild))
            return;
        Guild guild = bot.Guild;
        int level = bot.Level;
        bool announces = Random.Shared.NextDouble() < 0.5;
        if (announces)
            ScheduleGuildLine(bot, guild, 1_500 + Random.Shared.Next(2_000), () => Lines.OwnDing(level, Random.Shared));
        GameBot[] others = Speakers(guild).Where(other => other != bot).ToArray();
        int count = announces ? Math.Min(others.Length, Random.Shared.NextDouble() < 0.4 ? 2 : 1) : 0;
        for (int i = 0; i < count; i++)
        {
            GameBot other = others[i];
            ScheduleGuildLine(other, guild, 4_500 + i * 1_800 + Random.Shared.Next(2_500), 
                () => FreshLine(guild.GuildID, () => Lines.Congratulate(bot.Name, level, Random.Shared), GameLoop.GameLoopTime));
        }
    }

    /// <summary>Called by GameBot when an autonomous gamebot dies.</summary>
    public static void OnBotDeath(GameBot bot)
    {
        if (!Enabled || bot?.Guild == null || Random.Shared.NextDouble() >= 0.2 || !HasListener(bot.Guild))
            return;
        Guild guild = bot.Guild;
        lock (Sync)
        {
            if (LastDeathByGuild.TryGetValue(guild.GuildID, out long last) && GameLoop.GameLoopTime - last < GuildDeathCooldownMs)
                return;
            LastDeathByGuild[guild.GuildID] = GameLoop.GameLoopTime;
        }
        string zone = bot.CurrentZone?.Description ?? "the wilds";
        // A dead bot is still a member and can type; Eligible() below would refuse it, so send directly.
        _ = new ECSGameTimer(bot, _ =>
        {
            string text = bot.Guild == guild && bot.ObjectState == GameObject.eObjectState.Active && HasListener(guild)
                ? AutonomousBotVoices.StyleFor(bot, FreshLine(guild.GuildID, () => Lines.OwnDeath(zone, Random.Shared), GameLoop.GameLoopTime))
                : null;
            if (text != null)
                SendGuildLine(guild, bot.Name, text);
            return 0;
        }, 3_000 + Random.Shared.Next(3_000));
    }

    /// <summary>The guild that most members of a new group share, if at least two do.</summary>
    public static string SharedGuildId(IEnumerable<string> memberGuildIds)
    {
        return (memberGuildIds ?? Enumerable.Empty<string>())
            .Where(id => !string.IsNullOrEmpty(id))
            .GroupBy(id => id, StringComparer.Ordinal)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault();
    }

    /// <summary>Called when the group coordinator forms a bot group; guild groups say so in guild chat.</summary>
    public static void OnGroupFormed(GameBot[] members, bool rvr)
    {
        if (!Enabled || members == null || members.Length < 2)
            return;
        string guildId = SharedGuildId(members.Select(member => member?.Guild?.GuildID));
        if (guildId == null)
            return;
        GameBot speaker = members.FirstOrDefault(member => member?.Guild?.GuildID == guildId && Eligible(member));
        Guild guild = speaker?.Guild;
        if (guild == null || !HasListener(guild))
            return;
        int guildmates = members.Count(member => member?.Guild == guild);
        string zone = speaker.CurrentZone?.Description;
        ScheduleGuildLine(speaker, guild, 3_000 + Random.Shared.Next(4_000),
            () => FreshLine(guild.GuildID, () => Lines.GuildGroup(guildmates, zone, rvr, Random.Shared), GameLoop.GameLoopTime));
    }

    /// <summary>Called when a bot guild claims a keep.</summary>
    public static void OnKeepClaimed(GameBot speaker, Guild guild, string keepName)
    {
        if (!Enabled || speaker == null || guild == null || !HasListener(guild))
            return;
        ScheduleGuildLine(speaker, guild, 2_000 + Random.Shared.Next(2_000), () => Lines.KeepClaimed(keepName, guild.Name, Random.Shared));
    }

    /// <summary>Called when the defense response starts for a keep; its claiming guild calls for help.</summary>
    public static void OnClaimedKeepAttacked(Guild guild, string keepName)
    {
        if (!Enabled || guild == null || !HasListener(guild))
            return;
        GameBot speaker = guild.GetOnlineBotMembers().Where(Eligible)
            .OrderBy(bot => bot.GuildRank?.RankLevel ?? AutonomousBotGuildPlanner.LowestRank).FirstOrDefault();
        if (speaker != null)
            ScheduleGuildLine(speaker, guild, 1_500 + Random.Shared.Next(2_000), () => Lines.KeepUnderAttack(keepName, Random.Shared));
    }

    /// <summary>Called after a player's /gu line was sent to the guild.</summary>
    public static void OnPlayerGuildChat(GamePlayer player, string message)
    {
        AutonomousBeggars.OnPlayerAsked(player, message, "guild");
        if (!Enabled || player?.Guild == null || string.IsNullOrWhiteSpace(message))
            return;
        BotChatTranscript.Player("guild", player, message, player.Guild.Name);
        Guild guild = player.Guild;
        GameBot[] speakers = Speakers(guild);
        if (speakers.Length == 0)
            return;
        // Chat audit 2026-10-21: "ok" and "nice" draw no answer, "anyone in camelot?" only from guildmates who are there,
        // and a question is not dropped by the reply cooldown.
        string shape = AutonomousChatShortTalk.QuestionShape(message);
        if (shape == "ack")
            return;
        if (shape == "ask_inplace")
        {
            string place = AutonomousChatShortTalk.PlaceAsked(message);
            speakers = speakers.Where(bot => AutonomousChatLiveAnswers.IsIn(bot, place)).ToArray();
            if (speakers.Length == 0)
                return;
        }
        lock (Sync)
        {
            if (LastReplyByGuild.TryGetValue(guild.GuildID, out long last) && GameLoop.GameLoopTime - last < GuildReplyCooldownMs &&
                !AutonomousBotChatCoordinator.NeedsAnswer(message))
                return;
            LastReplyByGuild[guild.GuildID] = GameLoop.GameLoopTime;
        }

        GameBot[] mentioned = speakers.Where(bot => AutonomousBotChatCoordinator.ContainsMention(message, bot.Name)).ToArray();
        if (AutonomousChatKnowledge.TryAnswer(player, message, out string knowledge))
        {
            GameBot answerer = mentioned.FirstOrDefault() ?? speakers[0];
            ScheduleGuildLine(answerer, guild, 2_000 + Random.Shared.Next(2_500), () => knowledge);
            return;
        }

        eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
        Lines.ReplyKind kind = Lines.Classify(message, intent);
        GameBot[] pool = mentioned.Length > 0 ? mentioned : speakers;
        if (kind == Lines.ReplyKind.Grouping)
            pool = pool.OrderBy(bot => Math.Abs(bot.Level - player.Level)).ToArray();
        int count = mentioned.Length > 0 && kind != Lines.ReplyKind.None
            ? 1
            : ResponderCount(kind, pool.Length, Random.Shared.NextDouble());
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name };
        var spoken = new HashSet<GameBot>();
        // Busy guildmates hand their line on (a level 50 RvR guild is mostly fighting); a named bot answers itself.
        GameBot[] standIns = mentioned.Length > 0 ? [] : pool.Skip(count).Take(AutonomousBotChatCoordinator.PlayerReplyStandIns).ToArray();
        int due = 0;
        for (int i = 0; i < count; i++)
        {
            GameBot bot = pool[i];
            // A corpus reply for what the player said ("ty" -> "np", "lol" -> "heh", "ding" -> "gz"), else today's lines
            // (0.36-se.4: "haha true" answered any guild line that wasn't a question).
            // Unknown and real-world lines keep today's guild replies; nobody here says "omw" to a rez ask.
            VC.Picked picked = null;
            // The question type first, whatever the intent ("anyone on?" reads as none; live test 2026-10-18: it drew
            // "hmm, i would ask a trainer").
            string line = AutonomousBotVoices.TryShapedReply(bot, message, out picked, slots, relax: true, requireVoices: false, asker: player) ||
                          intent is not (eAutonomousChatIntent.Unknown or eAutonomousChatIntent.IgnoreOutOfWorld) &&
                          (intent == eAutonomousChatIntent.Greeting
                              ? AutonomousBotVoices.TryGreetingReply(bot, message, out picked, slots, relax: true, requireVoices: false)
                              : AutonomousBotVoices.TryLine(bot, "reply", out picked, AutonomousBotVoices.IntentName(intent),
                                  slots: slots, relax: true, requireVoices: false,
                                  // in-game check 2026-10-18: "lfg, anyone want a 50 cleric?" drew "what level and class?"
                                  filter: candidate => AutonomousChatShortTalk.ReplyFits(message, candidate.Text)))
                ? picked.Text
                : AutonomousBotVoices.StyleFor(bot, Lines.Reply(kind, player.Name, bot.Level, bot.CurrentZone?.Description, Random.Shared));
            if (!AutonomousBotChatCoordinator.TryAddDistinctResponse(used, line, out line))
                continue;
            due = AutonomousBotChatCoordinator.ReplyDelayMs(line, due, Random.Shared) + 600;
            string text = line;
            GameBot chosen = bot;
            _ = new ECSGameTimer(player, _ =>
            {
                GameBot speaker = AutonomousBotChatCoordinator.PickSpeaker(chosen, standIns, b => b.Guild == guild && Eligible(b), spoken);
                if (speaker != null && HasListener(guild))
                    SendGuildLine(guild, speaker.Name, text);
                return 0;
            }, due);
        }
    }

    /// <summary>
    /// An idle guild line from the corpus (kind "guild", by charter, with the no-repeat window), or null. The
    /// audit (2026-10-18) found a 12-member guild said about 34 idle lines in 3 hours and only 11 distinct ones.
    /// </summary>
    #region Gamebots in a player's guild (the base game's AutonomousBotGuilds, 0.37)

    /// <summary>With bot voices on, a corpus guild line for a gamebot in a player's guild, and what it invites; else null
    /// (the base game keeps its own lines).</summary>
    public static string VoicedPlayerGuildOpener(GameBot bot, out string invites)
    {
        invites = null;
        if (!AutonomousBotVoices.Enabled || bot == null)
            return null;
        VC.Picked picked = CorpusIdle(bot, BotGuildCharter.Social);
        if (picked == null)
            return null;
        invites = picked.Line.Invites;
        return picked.Text;
    }

    /// <summary>With bot voices on, a guildmate's answer of the kind a guild line invites; else null.</summary>
    public static string VoicedPlayerGuildAnswer(GameBot bot, string opener, string invites)
    {
        if (!AutonomousBotVoices.Enabled || bot == null || string.IsNullOrEmpty(invites))
            return null;
        foreach (string answers in VC.AnswerChain(invites))
            if (AutonomousBotVoices.TryLine(bot, "reply", out VC.Picked reply, answers: answers,
                    filter: line => AutonomousChatShortTalk.ReplyFits(opener, line.Text)))
                return reply.Text;
        return null;
    }

    /// <summary>With bot voices on, a gamebot's answer to a player's /gu line (as /faction and /group answer); else null.</summary>
    public static string VoicedPlayerGuildReply(GameBot bot, GamePlayer player, string message) =>
        AutonomousBotVoices.Enabled && bot != null && player != null
            ? AutonomousChatKnowledge.TryAnswer(player, message, out string knowledge) ? AutonomousBotVoices.StyleFor(bot, knowledge)
                : AutonomousBotChatCoordinator.ReplyToPlayer(bot, player, message)
            : null;

    #endregion

    private static VC.Picked CorpusIdle(GameBot bot, BotGuildCharter charter)
    {
        string own = charter.ToString().ToLowerInvariant();
        string[] topics = Random.Shared.NextDouble() < 0.65 ? [own, "general"] : ["general", own];
        foreach (string topic in topics)
            if (AutonomousBotVoices.TryLine(bot, "guild", out VC.Picked picked, topic: topic, requireVoices: false))
                return picked;
        return null;
    }

    /// <summary>
    /// An idle guild line, a recruiting post or a nearby invite offer, tried from a bot's ambient
    /// chat check. True when the bot spoke (the caller then skips its usual ambient line).
    /// </summary>
    public static bool TryIdle(GameBot bot)
    {
        if (!Enabled || !Eligible(bot) || bot.Guild == null)
            return false;
        Guild guild = bot.Guild;
        bool botGuild = AutonomousBotRealmGuilds.TryGetBotGuild(guild.GuildID, out AutonomousBotRealmGuilds.BotGuildInfo info);
        bool officer = botGuild && bot.GuildRank != null && bot.GuildRank.RankLevel <= 1;
        if (botGuild && bot.GuildRank?.RankLevel == 0)
            AutonomousBotGuildEconomy.TryBuyBuff(bot, info.Charter);

        if (officer && InvitesEnabled && TryOfferNearby(bot, guild))
            return true;
        if (officer && TryRecruit(bot, guild, info))
            return true;
        BotGuildCharter charter = botGuild ? info.Charter : BotGuildCharter.Social;
        // RvR and raiding guilds sometimes talk on their alliance channel instead.
        Alliance alliance = guild.alliance;
        bool allianceLine = AutonomousBotGuildAlliances.Enabled && alliance != null &&
            AutonomousBotGuildAlliances.IsAllianceCharter(charter) && Random.Shared.NextDouble() < 0.3 &&
            AutonomousBotGuildAlliances.HasListener(alliance);
        if (!allianceLine && !HasListener(guild))
            return false;

        GameBot[] online = guild.GetOnlineBotMembers();
        lock (Sync)
        {
            if (LastIdleByGuild.TryGetValue(guild.GuildID, out long last) &&
                GameLoop.GameLoopTime - last < IdleIntervalMs(charter, online.Length))
                return false;
            LastIdleByGuild[guild.GuildID] = GameLoop.GameLoopTime;
        }
        VC.Picked voiced = CorpusIdle(bot, charter);
        string idle = voiced?.Text ?? AutonomousBotVoices.StyleFor(bot, Lines.Idle(charter, bot.ClassName, bot.Level, bot.CurrentZone?.Description, Random.Shared));
        if (allianceLine)
        {
            AutonomousBotGuildAlliances.SendLine(alliance, bot.Name, idle);
            return true;
        }
        SendGuildLine(guild, bot.Name, idle);
        if (!string.IsNullOrEmpty(voiced?.Correction))
            ScheduleGuildLine(bot, guild, 1_200 + Random.Shared.Next(1_400), () => voiced.Correction, styled: true);

        // A guildmate picks it up: a corpus line answers what it invites (a question nearly always, a remark
        // sometimes); today's lines keep their old 30% "lol" or "haha true".
        GameBot other = Speakers(guild).FirstOrDefault(candidate => candidate != bot);
        if (other == null)
            return true;
        if (voiced != null)
        {
            if (AutonomousBotChatCoordinator.BotReplyCount(voiced.Line.Invites, 1, Random.Shared) == 0)
                return true;
            var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = bot.Name };
            foreach (string answers in VC.AnswerChain(voiced.Line.Invites))
            {
                if (!AutonomousBotVoices.TryLine(other, "reply", out VC.Picked reply, answers: answers, slots: slots,
                        requireVoices: false, filter: line => AutonomousChatShortTalk.ReplyFits(voiced.Text, line.Text)))
                    continue;
                ScheduleGuildLine(other, guild, AutonomousBotChatCoordinator.ReplyDelayMs(reply.Text, 0, Random.Shared) + 1_500, () => reply.Text, styled: true);
                break;
            }
        }
        else if (Random.Shared.NextDouble() < 0.3)
            ScheduleGuildLine(other, guild, 3_500 + Random.Shared.Next(4_000),
                () => Lines.Reply(Lines.ReplyKind.Banter, bot.Name, other.Level, other.CurrentZone?.Description, Random.Shared));
        return true;
    }

    /// <summary>
    /// A player's /faction or /say line asking for a guild. True when a bot guild answered (the
    /// caller then skips its generic replies).
    /// </summary>
    public static bool TryAnswerGuildRequest(GamePlayer player, string message, bool faction)
    {
        if (!InvitesEnabled || player == null || !CountsAsGuildless(player.Guild))
            return false;
        int minLevel = Math.Max(1, Properties.BOT_GUILD_MIN_LEVEL);
        if (player.Level < minLevel)
            return TryAnswerTooLow(player, message, faction, minLevel);
        string openGuildId;
        lock (Sync)
        {
            openGuildId = OpenRecruit.TryGetValue(player.Realm, out var open) && GameLoop.GameLoopTime - open.Tick < RecruitOpenMs
                ? open.GuildId : null;
        }
        if (!Lines.AsksForGuild(message, openGuildId != null))
            return false;

        var choices = new List<GuildChoice>();
        var speakerByGuild = new Dictionary<string, GameBot>(StringComparer.Ordinal);
        foreach (Guild guild in BotGuildsOfRealm(player.Realm))
        {
            GameBot speaker = guild.GetOnlineBotMembers()
                .Where(Eligible)
                .Where(bot => faction || bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE))
                .OrderBy(bot => bot.GuildRank?.RankLevel ?? AutonomousBotGuildPlanner.LowestRank)
                .FirstOrDefault();
            if (speaker != null)
                speakerByGuild[guild.GuildID] = speaker;
            AutonomousBotRealmGuilds.TryGetBotGuild(guild.GuildID, out AutonomousBotRealmGuilds.BotGuildInfo info);
            choices.Add(new GuildChoice(guild.GuildID, info?.Charter ?? BotGuildCharter.Social, guild.GetOnlineBotMembers().Length, speaker != null));
        }
        string chosenId = ChooseGuildForPlayer(choices, player.Level, openGuildId);
        if (chosenId == null || !speakerByGuild.TryGetValue(chosenId, out GameBot officer))
            return false;
        Guild chosen = GuildMgr.GetGuildByGuildID(chosenId);
        if (chosen == null)
            return false;

        string offer = Lines.OfferInvite(player.Name, chosen.Name, Random.Shared);
        _ = new ECSGameTimer(officer, _ =>
        {
            if (!Eligible(officer) || player.ObjectState != GameObject.eObjectState.Active || !CountsAsGuildless(player.Guild))
                return 0;
            if (faction)
                AutonomousBotChatCoordinator.BroadcastFaction(officer.Name, officer.Realm, offer);
            else
                officer.Say(offer);
            // ECS timers run off the client thread; the player's dialog state belongs to the client service, as in
            // TryOfferNearby (audit 2026-10-17).
            ClientService.Instance.Post(static state => SendInviteDialog(state.Player, state.Officer, state.Guild),
                (Player: player, Officer: officer, Guild: chosen));
            return 0;
        }, 2_000 + Random.Shared.Next(2_000));
        return true;
    }

    public const long TooLowAnswerMs = 10 * 60_000;

    /// <summary>One answer per player per 10 minutes to "lf guild" below the guild level.</summary>
    public static bool ShouldAnswerTooLow(bool answeredBefore, long lastAnswered, long now) =>
        !answeredBefore || now - lastAnswered >= TooLowAnswerMs;

    /// <summary>
    /// In game 2026-10-18 a level 3 asking "lf guild" in /faction got silence; at level 5 the same post got an invite
    /// in 12 s. Below the guild level an officer now says when to come back, once per player every 10 minutes.
    /// </summary>
    private static bool TryAnswerTooLow(GamePlayer player, string message, bool faction, int minLevel)
    {
        if (!Lines.AsksForGuild(message, false))
            return false;
        long now = GameLoop.GameLoopTime;
        lock (Sync)
        {
            bool before = LastTooLowAnswer.TryGetValue(player.Name, out long last);
            if (!ShouldAnswerTooLow(before, last, now))
                return true;                                  // asked again too soon: still handled, nothing said
            LastTooLowAnswer[player.Name] = now;
        }
        GameBot officer = null;
        Guild guild = null;
        foreach (Guild candidate in BotGuildsOfRealm(player.Realm).OrderBy(_ => Random.Shared.Next()))
        {
            officer = candidate.GetOnlineBotMembers()
                .Where(Eligible)
                .Where(bot => faction || bot.CurrentRegion == player.CurrentRegion && bot.IsWithinRadius(player, WorldMgr.SAY_DISTANCE))
                .OrderBy(bot => bot.GuildRank?.RankLevel ?? AutonomousBotGuildPlanner.LowestRank)
                .FirstOrDefault();
            if (officer != null)
            {
                guild = candidate;
                break;
            }
        }
        if (officer == null)
            return false;
        string line = Lines.TooLowForGuild(player.Name, guild.Name, minLevel, Random.Shared);
        _ = new ECSGameTimer(officer, _ =>
        {
            if (!Eligible(officer) || player.ObjectState != GameObject.eObjectState.Active)
                return 0;
            if (faction)
                AutonomousBotChatCoordinator.BroadcastFaction(officer.Name, officer.Realm, line);
            else
                officer.Say(line);
            return 0;
        }, 2_000 + Random.Shared.Next(2_000));
        return true;
    }

    #endregion

    #region Recruiting and invites

    private static bool TryRecruit(GameBot officer, Guild guild, AutonomousBotRealmGuilds.BotGuildInfo info)
    {
        eRealm realm = officer.Realm;
        int minLevel = Math.Max(1, Properties.BOT_GUILD_MIN_LEVEL);
        bool audience = ClientService.Instance.GetPlayersOfRealm(realm)
            .Any(player => CountsAsGuildless(player.Guild) && player.Level >= minLevel);
        if (!audience)
            return false;
        lock (Sync)
        {
            if (LastRecruitByRealm.TryGetValue(realm, out long last) && GameLoop.GameLoopTime - last < RealmRecruitCooldownMs)
                return false;
            LastRecruitByRealm[realm] = GameLoop.GameLoopTime;
            OpenRecruit[realm] = (guild.GuildID, GameLoop.GameLoopTime);
        }
        AutonomousBotChatCoordinator.BroadcastFaction(officer.Name, realm, AutonomousBotVoices.StyleFor(officer, Lines.Recruit(guild.Name, info.Charter, minLevel, Random.Shared)));
        return true;
    }

    private static bool TryOfferNearby(GameBot officer, Guild guild)
    {
        if (officer.CurrentRegion == null || Random.Shared.NextDouble() >= 0.25)
            return false;
        int minLevel = Math.Max(1, Properties.BOT_GUILD_MIN_LEVEL);
        DateTime now = DateTime.UtcNow;
        GamePlayer target = null;
        lock (Sync)
        {
            foreach (GamePlayer player in ClientService.Instance.GetPlayersOfRegion(officer.CurrentRegion))
            {
                if (player.ObjectState != GameObject.eObjectState.Active || player.Realm != officer.Realm || !CountsAsGuildless(player.Guild) ||
                    player.Level < minLevel || Math.Abs(player.Level - officer.Level) > 10 || player.InCombat ||
                    !officer.IsWithinRadius(player, WorldMgr.SAY_DISTANCE))
                    continue;
                string key = PlayerKey(player);
                if (DeclinedUntil.TryGetValue(key, out DateTime until) && until > now)
                    continue;
                if (LastNearbyOffer.TryGetValue(key, out DateTime offered) && now - offered < NearbyOfferMemory)
                    continue;
                LastNearbyOffer[key] = now;
                target = player;
                break;
            }
        }
        if (target == null)
            return false;
        officer.Say(Lines.OfferInviteNearby(target.Name, target.CharacterClass?.Name, guild.Name, Random.Shared));
        // TryIdle runs in parallel brain ticks; the player's dialog state belongs to the client service (audit 2026-10-16).
        ClientService.Instance.Post(static state => SendInviteDialog(state.Player, state.Officer, state.Guild), (Player: target, Officer: officer, Guild: guild));
        return true;
    }

    private static void SendInviteDialog(GamePlayer player, GameBot officer, Guild guild)
    {
        // Never replace another open yes/no question (a buff purchase, a boat, a battlegroup invite).
        if (player?.Out == null || guild == null || player.CustomDialogCallback != null)
            return;
        string officerName = officer.Name;
        string guildId = guild.GuildID;
        player.Out.SendCustomDialog($"{officerName} invites you to join {guild.Name}. Do you accept?", (answering, response) =>
        {
            Guild target = GuildMgr.GetGuildByGuildID(guildId);
            if (target == null || answering == null)
                return;
            if (response != 0x01)
            {
                lock (Sync)
                    DeclinedUntil[PlayerKey(answering)] = DateTime.UtcNow + DeclineMemory;
                return;
            }
            if (!CountsAsGuildless(answering.Guild))
            {
                answering.Out.SendMessage("You are still in a guild, you'll have to leave it first.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            DbGuildRank joinRank = target.GetRankByID(AutonomousBotRealmGuilds.PlayerJoinRank) ?? target.GetRankByID(AutonomousBotGuildPlanner.LowestRank);
            if (answering.Realm != target.Realm || joinRank == null)
                return;
            // A new character starts in its realm's starting guild; accepting a bot guild's invite moves it
            // out of that guild first, as /gc quit would (in-game check 2026-10-17: "lf guild" got no answer
            // while the character was still in Clan Cotswold).
            if (answering.Guild != null && !answering.Guild.RemovePlayer(answering.Name, answering))
                return;
            if (!target.AddPlayer(answering, joinRank))
                return;
            GameBot[] speakers = Speakers(target);
            int count = Math.Min(speakers.Length, 1 + (Random.Shared.NextDouble() < 0.5 ? 1 : 0));
            for (int i = 0; i < count; i++)
                ScheduleGuildLine(speakers[i], target, 2_500 + i * 2_000 + Random.Shared.Next(2_000),
                    () => FreshLine(target.GuildID, () => Lines.Welcome(answering.Name, target.Name, Random.Shared), GameLoop.GameLoopTime));
        });
    }

    #endregion

    #region Helpers

    /// <summary>
    /// True when a player can be recruited by a bot guild: no guild, or only the realm's starting guild
    /// (every new character is put in one, so treating it as a real guild meant nobody was ever recruited).
    /// </summary>
    public static bool CountsAsGuildless(Guild guild) => guild == null || guild.IsStartingGuild;

    /// <summary>True when a real player in the guild can hear guild chat.</summary>
    public static bool HasListener(Guild guild) =>
        guild != null && guild.GetListOfOnlineMembers().Any(player => guild.HasRank(player, Guild.eRank.GcHear));

    private static IEnumerable<Guild> BotGuildsOfRealm(eRealm realm)
    {
        foreach (Guild guild in GuildMgr.GetGuilds())
        {
            if (guild.Realm == realm && AutonomousBotRealmGuilds.IsBotGuild(guild))
                yield return guild;
        }
    }

    private static GameBot[] Speakers(Guild guild) => guild == null
        ? []
        : guild.GetOnlineBotMembers().Where(Eligible).OrderBy(_ => Random.Shared.Next()).ToArray();

    /// <summary>
    /// Sends a bot's guild line after a pause. The talking update: fixed text (templates, knowledge answers) is written in the
    /// bot's own style when bot voices are on; corpus lines and typo fixes are passed with <paramref name="styled"/> true.
    /// </summary>
    private static void ScheduleGuildLine(GameBot bot, Guild guild, int delayMs, Func<string> line, bool styled = false)
    {
        _ = new ECSGameTimer(bot, _ =>
        {
            if (bot.Guild == guild && Eligible(bot) && HasListener(guild))
                SendGuildLine(guild, bot.Name, styled ? line() : AutonomousBotVoices.StyleFor(bot, line()));
            return 0;
        }, delayMs);
    }

    public static void SendGuildLine(Guild guild, string speaker, string text)
    {
        string safe = (text ?? string.Empty).Trim();
        if (safe.Length == 0)
            return;
        if (safe.Length > Lines.MaxLength)
            safe = safe[..Lines.MaxLength];
        guild.SendMessageToGuildMembers($"[Guild] {speaker}: \"{safe}\"", eChatType.CT_Guild, eChatLoc.CL_ChatWindow);
        NoteHeard(guild.GuildID, safe, GameLoop.GameLoopTime);
        BotChatTranscript.Bot("guild", speaker, (eRealm)guild.Realm, safe, guild.Name);
    }

    private static bool Eligible(GameBot bot) => bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper && bot.IsAlive &&
        bot.ObjectState == GameObject.eObjectState.Active && !bot.InCombat && !bot.IsCasting;

    private static string PlayerKey(GamePlayer player) => player.InternalID ?? player.Name;

    #endregion
}
