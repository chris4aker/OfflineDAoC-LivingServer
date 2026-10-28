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

    /// <summary>A line a bot said that a player can see. via= is the first method on the stack that chose the line.</summary>
    public static void Bot(string channel, string speaker, eRealm realm, string text, string to = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string via;
        try { via = Via(new StackTrace(1, false)); } catch { via = "?"; }
        Log.Info($"CHAT_LINE who=bot ch={channel} realm={GlobalConstants.RealmToName(realm)} from=\"{speaker}\"" + (to != null ? $" to=\"{to}\"" : "") +
                 $" via={via} text=\"{Clean(text)}\"");
    }

    // The methods that only deliver a line someone else chose.
    private static readonly HashSet<string> Delivery = new(StringComparer.Ordinal)
    {
        "BroadcastFaction", "SendGuildLine", "Speak", "Tell", "Say", "Send", "SayTo", "Later",
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
