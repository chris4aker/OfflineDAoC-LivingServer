using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// CHAT_LINE: every chat line a player can see from the bots, and every line a player says that bots may answer, in
/// one server-log stream (owner, 2026-10-21: "responses and the conversations aren't happening correctly or logically",
/// and nothing in the log showed what was said). Each bot line names the code that produced it (via=File.Member) and
/// each player line the intent the chat model read in it, so a wrong or illogical reply can be traced to its source.
/// </summary>
public static class BotChatTranscript
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>A line a player said (channel: say, faction, guild, group, tell).</summary>
    public static void Player(string channel, GamePlayer player, string text, string to = null)
    {
        if (player == null || string.IsNullOrWhiteSpace(text)) return;
        string intent;
        try { intent = AutonomousChatIntentModel.Predict(text).ToString(); } catch { intent = "?"; }
        // Issue #475: guarded, so a line can be traced through its exit where the server log is off (tests); the row is unchanged.
        if (Log.IsInfoEnabled)
            Log.Info($"CHAT_LINE who=player ch={channel} realm={GlobalConstants.RealmToName(player.Realm)} from=\"{player.Name}\"" +
                 (to != null ? $" to=\"{to}\"" : "") + $" intent={intent} zone=\"{player.CurrentZone?.Description}\" text=\"{Clean(text)}\"");
        // Issue #475: the say gate's CHAT_HEAR row in the session chat log (logs only).
        BotSayGate.Hear(channel, player, text, to, intent);
    }

    /// <summary>
    /// A line a bot said that a player can see. via= is the first method on the stack that chose the line; src= the feature
    /// that produced it, and inside an exchange (a thread, a story, replies, a burst, a debate) ex= its id, i= the line's index
    /// and role= its speaker's part (1.1.3, owner 2026-11-08: most lines showed only the delivery method). kind= is the bot kind
    /// (gamebot, companion, buffbot, helper; chat audit TEXT-051), cls= and lvl= the speaker's real class and level, so a line
    /// can be checked against them (source/tools/chat_audit.py). Pass the speaking bot when it is known: a hired companion,
    /// a helper or a buffbot is not found by name among the realm's gamebots (and a buffbot may share its name with one).
    /// Issue #475: only the say gate calls this (<see cref="BotSayGate.Say"/>, its send-and-record step); an exit hands its line
    /// to the gate instead (tools/bot-voices/say_gate_ratchet.py fails a call anywhere else).
    /// </summary>
    public static void Bot(string channel, string speaker, eRealm realm, string text, string to = null, GameBot who = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        // Chat 1.2 follow-up: a promise made when its line was chosen stands once the line is out (ChatPromises, "unsaid").
        if (who != null) ChatPromises.NoteSaid(who, text);
        else ChatPromises.NoteSaid(speaker, realm, text);
        string via;
        try { via = Via(new StackTrace(1, false)); } catch { via = "?"; }
        if (Log.IsInfoEnabled)
            Log.Info($"CHAT_LINE who=bot ch={channel} realm={GlobalConstants.RealmToName(realm)} from=\"{speaker}\"" + (to != null ? $" to=\"{to}\"" : "") +
                 $" via={via}{SourceFields(speaker, realm, via, who)}{CapField(channel, realm)} text=\"{Clean(text)}\"");
    }

    /// <summary>
    /// A bot's emote a player can see ("CHAT_EMOTE from=\"Marelle\" kind=gamebot realm=Albion emote=Cheer"): a separate tag, so the
    /// chat audit's line counts stay lines (chat audit TEXT-051).
    /// </summary>
    public static void Emote(GameBot bot, eEmote emote)
    {
        if (bot == null) return;
        // #625: an emote can be a bot's answer to the player's line (PLAYER_LINE_OUTCOME; logs only).
        PlayerLineOutcomes.Emoted(bot, GameLoop.GameLoopTime);
        Log.Info($"CHAT_EMOTE from=\"{bot.Name}\" kind={KindOf(bot)} realm={GlobalConstants.RealmToName(bot.Realm)} emote={emote}");
    }

    /// <summary>
    /// Text the server puts on players' screens that no chat line carries: atmosphere lines, game-wide announcements, server
    /// firsts ("CHAT_SCREEN kind=first realm=Albion text=\"...\""; to= when only one player sees it). Chat audit TEXT-051.
    /// </summary>
    public static void Screen(string kind, string text, eRealm realm = eRealm.None, string to = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Log.Info($"CHAT_SCREEN kind={kind}" + (realm != eRealm.None ? $" realm={GlobalConstants.RealmToName(realm)}" : "") +
                 (to != null ? $" to=\"{to}\"" : "") + $" text=\"{Clean(text)}\"");
    }

    /// <summary>The bot kind a transcript names: buffbot, companion (hired), helper (/spawn, /raid) or gamebot. Pure.</summary>
    public static string KindOf(GameBot bot) =>
        bot == null ? "?"
        : bot.IsNoLiferBuffBot ? "buffbot"
        : bot.IsHiredCompanion ? "companion"
        : bot.IsTemporaryGroupHelper || !bot.IsAutonomousWorldBot ? "helper"
        : "gamebot";

    /// <summary>" src=thread ex=12 i=2 role=answerer kind=gamebot cls=\"Cabalist\" lvl=50" for the line being said now.</summary>
    public static string SourceFields(string speaker, eRealm realm, string via, GameBot who = null)
    {
        AutonomousChatExchange.Source source = AutonomousChatExchange.Current;
        string fields = source != null
            ? $" src={source.Feature} ex={source.Exchange} i={source.Index} role={source.Role}"
            : $" src={FeatureOf(via)}";
        GameBot bot = who != null && string.Equals(who.Name, speaker, StringComparison.Ordinal) ? who
            : source?.Speaker != null && string.Equals(source.Speaker.Name, speaker, StringComparison.Ordinal) ? source.Speaker : null;
        try
        {
            bot ??= Array.Find(AutonomousBotRegistry.Snapshot(), candidate => candidate != null && candidate.Realm == realm &&
                                                                               string.Equals(candidate.Name, speaker, StringComparison.Ordinal));
            bot ??= HiredCompanions165.LiveBots().FirstOrDefault(candidate => candidate.Realm == realm &&
                                                                              string.Equals(candidate.Name, speaker, StringComparison.Ordinal));
        }
        catch
        {
            bot = null;
        }
        if (bot != null)
            fields += $" kind={KindOf(bot)} cls=\"{bot.CharacterClass?.Name ?? bot.ClassName}\" lvl={bot.Level}";
        return fields;
    }

    /// <summary>
    /// " cap=6.9": the realm's /faction (or, 1.1.3, /say) ceiling when the line was said, so a log can be checked against it
    /// alone.
    /// </summary>
    public static string CapField(string channel, eRealm realm)
    {
        if (channel is not ("faction" or "say"))
            return "";
        try
        {
            double cap = channel == "faction" ? AutonomousBotChatCoordinator.FactionCeiling(realm) : AutonomousBotChatCoordinator.SayCeiling(realm);
            return " cap=" + cap.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>The feature a line outside any exchange came from, by the code that chose it.</summary>
    public static string FeatureOf(string via)
    {
        string member = via?[(via.IndexOf('.') + 1)..].TrimEnd('~') ?? "";
        return member switch
        {
            "TryField" => "field",
            "SpeakIn" or "TryStartBudgeted" or "TryStartAmbientLegacy" => "ambient",
            "SayNews" or "TellNews" or "TrySayEventNews" => "news",
            "PlayBeat" => "debate",
            "TryAdvertiseExchangeItem" => "exchange_ad",
            "RecordKill" => "nemesis",
            "OnBotKilled" => "revenge",
            "ObserveEnemies" => "alarm",
            "StartTell" => "tell",
            "SchedulePlayerReplies" or "TryThreadReply" or "OnPlayerTell" or "OnPlayerFactionChat" or "OnPlayerLocalChat" => "player_reply",
            "React" => "burst",
            "PlayStory" => "story",
            "PlayThread" => "thread",
            _ => "other",
        };
    }

    // The methods that only deliver a line someone else chose.
    private static readonly HashSet<string> Delivery = new(StringComparer.Ordinal)
    {
        "BroadcastFaction", "SendGuildLine", "Speak", "Tell", "Say", "Send", "SayTo", "Later",
        // 1.1.3: the wrappers that only deliver, so via= names the code that chose the line (BroadcastFactionAsWritten hid it).
        "BroadcastFactionAsWritten", "SendFaction", "SayLine", "SendRealmChannel", "SendLfgLine", "SendThreadLine", "Play", "Step", "Arm",
    };

    /// <summary>"Type.Method" of the first caller that is not a delivery method (a lambda shows as Type.Outer~).</summary>
    public static string Via(StackTrace trace)
    {
        for (int i = 0; i < trace.FrameCount; i++)
        {
            MethodBase method = trace.GetFrame(i)?.GetMethod();
            if (method == null) continue;
            Type type = method.DeclaringType;
            while (type is { DeclaringType: not null } && type.Name.StartsWith('<')) type = type.DeclaringType;
            string name = method.Name;
            if (name.StartsWith('<'))
            {
                int close = name.IndexOf('>');
                name = (close > 1 ? name[1..close] : "lambda") + "~";
            }
            if (Delivery.Contains(name) || type == typeof(BotChatTranscript) || type == typeof(BotSayGate)) continue;
            if (type != null && (type.Name.StartsWith("Timer") || type.Namespace?.StartsWith("System") == true)) break;
            return $"{type?.Name}.{name}";
        }
        return "?";
    }

    private static string Clean(string text) => (text ?? "").Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ').Trim();
}
