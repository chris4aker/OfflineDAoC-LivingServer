using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using DOL.GS.PacketHandler;

namespace DOL.GS;

/// <summary>
/// SCREEN_BURST (realism plan step 1, issue #475): one system-window message shown to a player many times in a short window,
/// such as the ten "XxEvilxX's weapon returns to normal." lines of the owner's 1.2.2 game (defect 9). The client packet
/// libraries tell it every message they send to a player; it counts by message key and writes one row per burst to the
/// session chat log (<see cref="BotSayGate"/>). It only counts: no message is changed, held or delayed.
/// </summary>
public static class ScreenBursts
{
    /// <summary>A key seen this many times or more within <see cref="WindowMs"/> of its previous showing is a burst.</summary>
    public const int Threshold = 4;
    /// <summary>The longest gap between two showings of one key inside a burst.</summary>
    public const long WindowMs = 2_000;

    private sealed class Seen
    {
        public long First, Last;
        public int Count, Type;
        public string Sample;
    }

    private sealed class State
    {
        public readonly Dictionary<string, Seen> Keys = new(StringComparer.Ordinal);
    }

    private static readonly ConditionalWeakTable<object, State> States = new();

    /// <summary>
    /// A message the server sent to a player (PacketLib168/173/1127 SendMessage). Only system-window kinds are counted; chat
    /// channels are the gate's. Never throws.
    /// </summary>
    public static void Note(GamePlayer player, string message, eChatType type)
    {
        if (player == null || string.IsNullOrEmpty(message) || !Counted(type))
            return;
        try
        {
            Note(player, player.Name, player.Realm, message, (int)type, GameLoop.GameLoopTime);
        }
        catch (Exception)
        {
            // Counting only.
        }
    }

    /// <summary>The kinds of message counted: the system window's own, not chat and not the combat lines every fight repeats. Pure.</summary>
    public static bool Counted(eChatType type) => type is eChatType.CT_System or eChatType.CT_Spell or eChatType.CT_SpellExpires
        or eChatType.CT_SpellResisted or eChatType.CT_Important or eChatType.CT_Skill or eChatType.CT_Merchant
        or eChatType.CT_ScreenCenter_And_CT_System or eChatType.CT_ScreenCenterSmaller_And_CT_System;

    /// <summary>The count for one viewer (a player, or any object in tests), at <paramref name="now"/>.</summary>
    public static void Note(object viewer, string name, eRealm realm, string message, int type, long now)
    {
        string key = KeyOf(message);
        State state = States.GetOrCreateValue(viewer);
        List<(string Key, Seen Seen)> closed = null;
        lock (state)
        {
            foreach (KeyValuePair<string, Seen> entry in state.Keys)
                if (now - entry.Value.Last > WindowMs)
                    (closed ??= []).Add((entry.Key, entry.Value));
            if (closed != null)
                foreach ((string old, _) in closed)
                    state.Keys.Remove(old);
            if (state.Keys.TryGetValue(key, out Seen seen))
            {
                seen.Count++;
                seen.Last = now;
            }
            else if (state.Keys.Count < 64)
                state.Keys[key] = new Seen { First = now, Last = now, Count = 1, Type = type, Sample = message };
        }
        if (closed != null)
            foreach ((string old, Seen burst) in closed)
                if (burst.Count >= Threshold)
                    BotSayGate.Burst(name, realm, old, burst.Sample, burst.Count, burst.First, burst.Last, burst.Type);
    }

    /// <summary>Writes the bursts still open for a player (on /odd, so a burst just seen is in the log before its flag).</summary>
    public static void Flush(GamePlayer player)
    {
        if (player != null)
            Flush(player, player.Name, player.Realm);
    }

    /// <summary>Writes the bursts still open for one viewer.</summary>
    public static void Flush(object viewer, string name, eRealm realm)
    {
        if (viewer == null || !States.TryGetValue(viewer, out State state))
            return;
        List<(string Key, Seen Seen)> open = [];
        lock (state)
        {
            foreach (KeyValuePair<string, Seen> entry in state.Keys)
                open.Add((entry.Key, entry.Value));
            state.Keys.Clear();
        }
        foreach ((string key, Seen burst) in open)
            if (burst.Count >= Threshold)
                BotSayGate.Burst(name, realm, key, burst.Sample, burst.Count, burst.First, burst.Last, burst.Type);
    }

    /// <summary>
    /// A message's key: digits as "#", and a leading name's possessive dropped ("XxEvilxX's weapon returns to normal." and
    /// "Marelle's weapon returns to normal." share "*'s weapon returns to normal."), so one effect on many people counts once. Pure.
    /// </summary>
    public static string KeyOf(string message)
    {
        string text = message.Trim();
        int space = text.IndexOf(' ');
        if (space > 2 && (text[..space].EndsWith("'s", StringComparison.Ordinal) || text[..space].EndsWith("s'", StringComparison.Ordinal)))
            text = "*'s" + text[space..];
        var key = new StringBuilder(text.Length);
        bool lastDigit = false;
        foreach (char c in text)
        {
            bool digit = char.IsDigit(c);
            if (!digit)
                key.Append(c);
            else if (!lastDigit)
                key.Append('#');
            lastDigit = digit;
        }
        return key.ToString();
    }
}
