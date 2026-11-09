using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
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
        Log.Info($"CHAT_LINE who=player ch={channel} realm={GlobalConstants.RealmToName(player.Realm)} from=\"{player.Name}\"" +
                 (to != null ? $" to=\"{to}\"" : "") + $" intent={intent} zone=\"{player.CurrentZone?.Description}\" text=\"{Clean(text)}\"");
    }

    /// <summary>
    /// A line a bot said that a player can see. via= is the first method on the stack that chose the line; src= the feature
    /// that produced it, and inside an exchange (a thread, a story, replies, a burst, a debate) ex= its id, i= the line's index
    /// and role= its speaker's part (1.1.3, owner 2026-11-08: most lines showed only the delivery method). cls= and lvl= are the
    /// speaker's real class and level, so a line can be checked against them (source/tools/chat_audit.py).
    /// </summary>
    public static void Bot(string channel, string speaker, eRealm realm, string text, string to = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string via;
        try { via = Via(new StackTrace(1, false)); } catch { via = "?"; }
        Log.Info($"CHAT_LINE who=bot ch={channel} realm={GlobalConstants.RealmToName(realm)} from=\"{speaker}\"" + (to != null ? $" to=\"{to}\"" : "") +
                 $" via={via}{SourceFields(speaker, realm, via)}{CapField(channel, realm)} text=\"{Clean(text)}\"");
    }

    /// <summary>" src=thread ex=12 i=2 role=answerer cls=\"Cabalist\" lvl=50" for the line being said now.</summary>
    public static string SourceFields(string speaker, eRealm realm, string via)
    {
        AutonomousChatExchange.Source source = AutonomousChatExchange.Current;
        string fields = source != null
            ? $" src={source.Feature} ex={source.Exchange} i={source.Index} role={source.Role}"
            : $" src={FeatureOf(via)}";
        GameBot bot = source?.Speaker != null && string.Equals(source.Speaker.Name, speaker, StringComparison.Ordinal) ? source.Speaker : null;
        try
        {
            bot ??= Array.Find(AutonomousBotRegistry.Snapshot(), candidate => candidate != null && candidate.Realm == realm &&
                                                                               string.Equals(candidate.Name, speaker, StringComparison.Ordinal));
        }
        catch
        {
            bot = null;
        }
        if (bot != null)
            fields += $" cls=\"{bot.CharacterClass?.Name ?? bot.ClassName}\" lvl={bot.Level}";
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
            if (Delivery.Contains(name) || type == typeof(BotChatTranscript)) continue;
            if (type != null && (type.Name.StartsWith("Timer") || type.Namespace?.StartsWith("System") == true)) break;
            return $"{type?.Name}.{name}";
        }
        return "?";
    }

    private static string Clean(string text) => (text ?? "").Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ').Trim();
}
