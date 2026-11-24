using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The say gate (realism plan step 1, issue #475; docs/realism-audit-1.2.3/structure.md "A. The say gate"): the one place every
/// bot line passes on its way to a player's screen, in every channel. Each wire exit (the 17 places that used to call
/// <see cref="BotChatTranscript.Bot"/> themselves) hands its line here with the send it would have done; the gate runs its
/// fixed steps in order and then sends and records.
/// <para>
/// <b>Since #478 the gate holds one class of line</b>: a non-answer as the whole reply to a line said to the speaker or asked of
/// its channel (step 2, <see cref="WholeAnswer"/>; BotSayGate.NonAnswer.cs), with a CHAT_HELD row (rule=non-answer). Every
/// other check is still a no-op. <b>Step 1 was a pass-through.</b> Every check is a no-op that lets the line through as it came, byte for byte, so nothing a
/// bot says, when it says it, or what it remembers changes. What the gate adds is the record: one <c>CHAT_SAY</c> row per
/// line a player saw, <c>CHAT_HEAR</c> per player line, <c>CHAT_HELD</c> per line not sent or player line left unanswered
/// (from <see cref="AutonomousChatWhy"/>, the /lswhy ring, written to the log), <c>SCREEN_BURST</c> for system-window bursts
/// (<see cref="ScreenBursts"/>) and <c>CHAT_FLAG</c> for <c>/odd</c>, all in the small separate <c>logs/session-chat.log</c>
/// (the "sessionchat" logger in logconfig.xml). <c>tools/bot-voices/session_transcript.py</c> turns that file into a transcript
/// per player. The <c>CHAT_LINE</c> rows in server.log are written exactly as before.
/// </para>
/// </summary>
public static partial class BotSayGate
{
    /// <summary>The logger the session chat log's rows go to (logconfig.xml sends it to logs/session-chat.log only).</summary>
    public const string LoggerName = "sessionchat";

    private static readonly Logger Log = LoggerManager.Create(LoggerName);

    /// <summary>The gate's checks, in their fixed order (structure.md A). All are no-ops in step 1.</summary>
    public enum Step { ChannelFit, Addressee, Freshness, Grounding, Pacing, Style, SendAndRecord }

    /// <summary>The steps in the order every line meets them.</summary>
    public static readonly IReadOnlyList<Step> Order =
        [Step.ChannelFit, Step.Addressee, Step.Freshness, Step.Grounding, Step.Pacing, Step.Style, Step.SendAndRecord];

    /// <summary>
    /// One bot line on its way out. Speaker, channel and text are always known; the rest is filled where the exit knows it
    /// (later steps add the line's contract: act, line id, the line it answers, its claims).
    /// </summary>
    public sealed class Utterance
    {
        public Utterance(string channel, string speaker, eRealm realm, string text, string to = null, GameBot who = null)
        {
            Channel = channel;
            Speaker = speaker;
            Realm = realm;
            Text = text;
            To = to;
            Who = who;
        }

        public string Channel { get; }
        public string Speaker { get; }
        public eRealm Realm { get; }
        /// <summary>The text as the exit hands it to the gate (and, in step 1, as the gate hands it to the send).</summary>
        public string Text { get; internal set; }
        /// <summary>The addressee the CHAT_LINE row names (a tell's player, a guild line's guild).</summary>
        public string To { get; }
        public GameBot Who { get; }
        /// <summary>
        /// The speaking bot, for who heard it and the room, where the exit knows it but the CHAT_LINE row never named it
        /// (<see cref="Who"/> stays as the exit passed it, so CHAT_LINE is written exactly as before).
        /// </summary>
        public GameBot Body { get; init; }
        /// <summary>Who hears it, when the exit knows better than the channel (a raid call's parties, a /broadcast's listeners).</summary>
        public IEnumerable<GamePlayer> Listeners { get; init; }
        /// <summary>The room it is said in, when the channel alone doesn't say (a guild's name, a group).</summary>
        public string Room { get; init; }
        /// <summary>The act (question, answer, remark, ...) once lines carry a contract; null today ("no contract").</summary>
        public string Act { get; init; }
        /// <summary>The corpus line id or code site once lines carry a contract; null today.</summary>
        public string LineId { get; init; }
        /// <summary>The seq of the player line it answers, when the exit knows it (else the gate infers one, see <see cref="ReOf"/>).</summary>
        public long Re { get; init; }
        /// <summary>Whether the caller already styled the text (so step 6 won't style it twice, once it styles).</summary>
        public bool? Styled { get; init; }
        /// <summary>
        /// The line this one answers, when the exit knows it (#478); else the gate takes the newest line in the room by someone
        /// else (<see cref="AnswerWindowMs"/>).
        /// </summary>
        public string Answers { get; init; }
        /// <summary>With <see cref="Answers"/>: whether that line was said to this speaker (a tell, its group, its name).</summary>
        public bool SaidToSpeaker { get; init; }
    }

    /// <summary>What a step decided: pass the line on (maybe changed), or hold it with a reason.</summary>
    public readonly record struct Verdict(bool Held, string Rule, string Reason)
    {
        public static readonly Verdict Pass = new(false, "pass", null);
    }

    private static long _seq;
    private static long _lines, _noContract, _held;
    private static int _sessionWritten;

    /// <summary>Lines the gate let through, lines among them that came without a contract, and lines it held (step 1: none).</summary>
    public static (long Lines, long NoContract, long Held) Counts =>
        (Interlocked.Read(ref _lines), Interlocked.Read(ref _noContract), Interlocked.Read(ref _held));

    /// <summary>Tests read the session chat log's rows here (the server's own logger is off in tests).</summary>
    public static Action<string> TestSink;

    /// <summary>
    /// The way out for a bot line: the steps in <see cref="Order"/>, then <paramref name="send"/> (the exit's own transport,
    /// handed the text the steps let through) and the record of what it returned. <paramref name="send"/> returns the text a
    /// player saw, or null when nobody did (nothing is recorded then). Returns what <paramref name="send"/> returned, or null
    /// when a step held the line (never in step 1).
    /// </summary>
    public static string Say(Utterance line, Func<string, string> send)
    {
        if (line == null || send == null)
            return null;
        Verdict verdict = Check(line);
        if (verdict.Held)
        {
            Interlocked.Increment(ref _held);
            Write(HeldRow(Next(), line, verdict));
            return null;
        }
        string shown = send(line.Text);
        if (shown != null)
            Record(line, shown);
        return shown;
    }

    /// <summary>Steps 1-6 in their order; the first that holds the line decides. Step 1 of the plan: every step passes.</summary>
    public static Verdict Check(Utterance line)
    {
        foreach (Step step in Order)
        {
            if (step == Step.SendAndRecord)
                break;
            Verdict verdict = Run(step, line);
            if (verdict.Held)
                return verdict;
        }
        return Verdict.Pass;
    }

    /// <summary>One step. Each is a no-op for now (shadow checks come in step 5 of structure.md A).</summary>
    public static Verdict Run(Step step, Utterance line) => step switch
    {
        Step.ChannelFit => ChannelFit(line),
        Step.Addressee => Addressee(line),
        Step.Freshness => Freshness(line),
        Step.Grounding => Grounding(line),
        Step.Pacing => Pacing(line),
        Step.Style => Style(line),
        _ => Verdict.Pass,
    };

    // 1. The line's contract allows this channel and audience.
    private static Verdict ChannelFit(Utterance line) => Verdict.Pass;
    // 2. No non-answer as the whole answer to a line said to the speaker or asked of the channel (#478, BotSayGate.NonAnswer.cs).
    //    Still to come: a short reply far from its question names the asker; a reaction's trigger is still on the listener's screen.
    private static Verdict Addressee(Utterance line) => WholeAnswer(line);
    // 3. What it answers is recent; the speaker's state is still what the line needs.
    private static Verdict Freshness(Utterance line) => Verdict.Pass;
    // 4. Each claim is true now (place, level, class, grouped, item, promise).
    private static Verdict Grounding(Utterance line) => Verdict.Pass;
    // 5. Meter, ceiling, speaker share, repeat windows.
    private static Verdict Pacing(Utterance line) => Verdict.Pass;
    // 6. Voice, idiolect, time of day, roleplayer voice, knowing the act. Changes no text yet.
    private static Verdict Style(Utterance line) => Verdict.Pass;

    // 7. The record of a line sent: the CHAT_LINE row as before (server.log), and the gate's CHAT_SAY row.
    private static void Record(Utterance line, string shown)
    {
        BotChatTranscript.Bot(line.Channel, line.Speaker, line.Realm, shown, line.To, line.Who);
        if (string.IsNullOrWhiteSpace(shown))
            return;
        Interlocked.Increment(ref _lines);
        NoteInRoom(RoomKey(line.Channel, line.Realm, RoomOf(line), line.Speaker, line.To),
            new Said(0, Now(), line.Speaker, shown, line.To, false, line.Channel));
        if (line.Act == null && line.LineId == null)
            Interlocked.Increment(ref _noContract);
        try
        {
            Write(SayRow(Next(), line, shown));
        }
        catch (Exception)
        {
            // The record never stops a line.
        }
    }

    // ---- Rows ------------------------------------------------------------------------------------------------------

    /// <summary>The next row number (one sequence for every row of the session chat log, so rows can be cited).</summary>
    public static long Next() => Interlocked.Increment(ref _seq);

    private static string SayRow(long seq, Utterance line, string shown)
    {
        List<string> heard = Heard(line);
        long re = line.Re > 0 ? line.Re : ReOf(line, heard);
        AutonomousChatExchange.Source source = AutonomousChatExchange.Current;
        string via;
        try { via = BotChatTranscript.Via(new System.Diagnostics.StackTrace(1, false)); } catch { via = "?"; }
        var row = new StringBuilder("CHAT_SAY");
        row.Append(" seq=").Append(seq).Append(" t=").Append(Now());
        row.Append(" ch=").Append(line.Channel).Append(" room=").Append(Quote(RoomOf(line)));
        row.Append(" realm=").Append(GlobalConstants.RealmToName(line.Realm));
        row.Append(" from=").Append(Quote(line.Speaker));
        if ((line.Body ?? line.Who) is { } bot)
            row.Append(" id=").Append(bot.DatabaseID).Append(" kind=").Append(BotChatTranscript.KindOf(bot));
        if (line.To != null)
            row.Append(" to=").Append(Quote(line.To));
        if (re > 0)
            row.Append(" re=").Append(re);
        row.Append(source != null
            ? $" src={source.Feature} ex={source.Exchange} i={source.Index} role={source.Role}"
            : $" src={BotChatTranscript.FeatureOf(via)}");
        row.Append(" via=").Append(via);
        row.Append(" act=").Append(line.Act ?? "?").Append(" line=").Append(line.LineId ?? "?");
        row.Append(" contract=").Append(line.Act == null && line.LineId == null ? "none" : "some");
        row.Append(" styled=").Append(line.Styled is { } styled ? (styled ? "yes" : "no") : "?");
        row.Append(" rule=pass");
        row.Append(" heard=").Append(Quote(string.Join(",", heard)));
        row.Append(" text=").Append(Quote(shown));
        Remember(seq, "say", line.Channel, line.Realm, heard, line.Speaker, shown);
        return row.ToString();
    }

    private static string HeldRow(long seq, Utterance line, Verdict verdict) =>
        $"CHAT_HELD seq={seq} t={Now()} ch={line.Channel} realm={GlobalConstants.RealmToName(line.Realm)} from={Quote(line.Speaker)}" +
        (line.To != null ? $" to={Quote(line.To)}" : "") + $" code=gate rule={verdict.Rule} reason={Quote(verdict.Reason)} text={Quote(line.Text)}";

    /// <summary>
    /// CHAT_HEAR: a player's line bots may answer, and how it was read (the intent the chat model gave it), with who heard it
    /// and the bots in the player's group. Called by <see cref="BotChatTranscript.Player"/>; logs only.
    /// </summary>
    public static void Hear(string channel, GamePlayer player, string text, string to, string intent)
    {
        if (player == null || string.IsNullOrWhiteSpace(text))
            return;
        try
        {
            long seq = Next();
            List<string> heard = HeardPlayer(channel, player, to);
            string grouped = "";
            try
            {
                grouped = player.Group == null ? "" : string.Join(",", player.Group.GetMembersInTheGroup().OfType<GameBot>().Select(bot => bot.Name));
            }
            catch (Exception)
            {
                grouped = "";
            }
            string room = channel switch
            {
                "tell" => to ?? "",
                "guild" => to ?? player.Guild?.Name ?? "",
                "group" => GroupRoom(player.Group),
                "say" => player.CurrentZone?.Description ?? "",
                _ => GlobalConstants.RealmToName(player.Realm),
            };
            NoteHeard(seq, channel, player);
            NoteInRoom(RoomKey(channel, player.Realm, room, player.Name, to), new Said(seq, Now(), player.Name, text, to, true, channel));
            Remember(seq, "hear", channel, player.Realm, heard, player.Name, text);
            Write($"CHAT_HEAR seq={seq} t={Now()} ch={channel} room={Quote(room)} realm={GlobalConstants.RealmToName(player.Realm)} " +
                  $"from={Quote(player.Name)}" + (to != null ? $" to={Quote(to)}" : "") + $" intent={intent ?? "?"} " +
                  $"zone={Quote(player.CurrentZone?.Description)} grouped={Quote(grouped)} heard={Quote(string.Join(",", heard))} text={Quote(text)}");
        }
        catch (Exception)
        {
            // Logging only.
        }
    }

    /// <summary>
    /// CHAT_HELD from a chat decision that ended without a line (outcome "silent" or "held"; the /lswhy ring, now in the log
    /// too). Called by <see cref="AutonomousChatWhy.Record(in AutonomousChatWhy.Decision)"/>; logs only.
    /// </summary>
    public static void Held(in AutonomousChatWhy.Decision decision)
    {
        if (decision.Outcome is not ("silent" or "held"))
            return;
        try
        {
            long seq = Next();
            long re = 0;
            if (!string.IsNullOrEmpty(decision.To))
                lock (Recent)
                    if (LastHeardBy.TryGetValue(decision.To, out (long Seq, long At) last) && Now() - last.At <= ReWindowMs)
                        re = last.Seq;
            Write($"CHAT_HELD seq={seq} t={Now()} ch={decision.Channel} realm={GlobalConstants.RealmToName(decision.Realm)}" +
                  (decision.Bot != null ? $" from={Quote(decision.Bot)}" : "") + (decision.To != null ? $" to={Quote(decision.To)}" : "") +
                  (re > 0 ? $" re={re}" : "") + $" outcome={decision.Outcome} code={AutonomousChatWhy.CodeOf(decision)} rule={Quote(decision.Rule)} " +
                  $"score={decision.Score} trigger={Quote(decision.Trigger)} reason={Quote(decision.Reason)}" +
                  (decision.Text != null ? $" text={Quote(decision.Text)}" : ""));
        }
        catch (Exception)
        {
            // Logging only.
        }
    }

    /// <summary>
    /// CHAT_FLAG: the player's <c>/odd [note]</c>. Marks the newest rows the player saw (up to <paramref name="lines"/>); the
    /// transcript tool counts each flag as a defect at that spot. Returns the flag's seq and the rows it marks.
    /// </summary>
    public static (long Seq, long[] Marked) Flag(GamePlayer player, string note, int lines = 3)
    {
        if (player == null)
            return (0, []);
        ScreenBursts.Flush(player);
        long seq = Next();
        long[] marked;
        string channel = "?";
        lock (Recent)
        {
            var seen = Recent.Where(row => row.Heard.Contains(player.Name, StringComparer.OrdinalIgnoreCase)).TakeLast(Math.Max(1, lines)).ToArray();
            marked = seen.Select(row => row.Seq).ToArray();
            if (seen.Length > 0)
                channel = seen[^1].Channel;
        }
        Write($"CHAT_FLAG seq={seq} t={Now()} to={Quote(player.Name)} realm={GlobalConstants.RealmToName(player.Realm)} " +
              $"at={(marked.Length > 0 ? marked[^1] : 0)} ch={channel} lines=\"{string.Join(",", marked)}\" " +
              $"zone={Quote(player.CurrentZone?.Description)} note={Quote(note ?? "")}");
        return (seq, marked);
    }

    /// <summary>SCREEN_BURST: one message key shown to a player many times in a short window (<see cref="ScreenBursts"/>).</summary>
    public static void Burst(string player, eRealm realm, string key, string sample, int count, long first, long last, int type)
    {
        long seq = Next();
        lock (Recent)
            Remember(seq, "burst", "system", realm, [player], "", sample);
        Write($"SCREEN_BURST seq={seq} t={last} to={Quote(player)} realm={GlobalConstants.RealmToName(realm)} type={type} n={count} " +
              $"window={(last - first).ToString(CultureInfo.InvariantCulture)}ms key={Quote(key)} text={Quote(sample)}");
    }

    // ---- Who heard it, what it answers ------------------------------------------------------------------------------

    /// <summary>A player line answered up to this long ago counts as the one a reply answers (re=, inferred).</summary>
    public const long ReWindowMs = 120_000;

    private static readonly Dictionary<string, (long Seq, long At)> LastHeardBy = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (long Seq, long At)> LastHeardIn = new(StringComparer.Ordinal);

    private readonly record struct Row(long Seq, string Kind, string Channel, eRealm Realm, string[] Heard);
    private const int RecentCapacity = 128;
    private static readonly Queue<Row> Recent = new();

    private static void Remember(long seq, string kind, string channel, eRealm realm, List<string> heard, string from, string text)
    {
        lock (Recent)
        {
            Recent.Enqueue(new Row(seq, kind, channel, realm, heard.ToArray()));
            while (Recent.Count > RecentCapacity)
                Recent.Dequeue();
        }
    }

    private static void NoteHeard(long seq, string channel, GamePlayer player)
    {
        long now = Now();
        lock (Recent)
        {
            LastHeardBy[player.Name] = (seq, now);
            LastHeardIn[channel + ":" + (int)player.Realm] = (seq, now);
        }
    }

    /// <summary>
    /// The player line a bot line answers, inferred until lines carry it: the addressee's newest line (a tell, a reply by name),
    /// else, for a reply to a player, the newest player line in the channel; 0 when there is none within <see cref="ReWindowMs"/>.
    /// </summary>
    private static long ReOf(Utterance line, List<string> heard)
    {
        long now = Now();
        AutonomousChatExchange.Source source = AutonomousChatExchange.Current;
        lock (Recent)
        {
            if (line.To != null && LastHeardBy.TryGetValue(line.To, out (long Seq, long At) by) && now - by.At <= ReWindowMs)
                return by.Seq;
            bool reply = source?.Feature is "player" or "player_reply";
            if (reply && LastHeardIn.TryGetValue(line.Channel + ":" + (int)line.Realm, out (long Seq, long At) inChannel) && now - inChannel.At <= ReWindowMs)
                return inChannel.Seq;
        }
        return 0;
    }

    /// <summary>The real players a bot line reached: the exit's listeners if it gave them, else the channel's audience.</summary>
    private static List<string> Heard(Utterance line)
    {
        try
        {
            IEnumerable<GamePlayer> players = line.Listeners ?? line.Channel switch
            {
                "faction" or "lfg" or "trade" or "advice" => ClientService.Instance.GetPlayersOfRealm(line.Realm),
                "tell" => line.To == null ? null : [ClientService.Instance.GetPlayerByExactName(line.To)],
                "group" => (line.Body ?? line.Who)?.Group?.GetPlayersInTheGroup(),
                "guild" => (line.Body ?? line.Who)?.Guild?.GetListOfOnlineMembers(),
                "say" => (line.Body ?? line.Who)?.GetPlayersInRadius(WorldMgr.SAY_DISTANCE),
                "yell" => (line.Body ?? line.Who)?.GetPlayersInRadius(WorldMgr.YELL_DISTANCE),
                _ => null,
            };
            return Names(players);
        }
        catch (Exception)
        {
            return [];
        }
    }

    private static List<string> HeardPlayer(string channel, GamePlayer player, string to)
    {
        try
        {
            IEnumerable<GamePlayer> players = channel switch
            {
                "faction" or "lfg" or "trade" or "advice" => ClientService.Instance.GetPlayersOfRealm(player.Realm),
                "group" => player.Group?.GetPlayersInTheGroup(),
                "guild" => player.Guild?.GetListOfOnlineMembers(),
                "say" => player.GetPlayersInRadius(WorldMgr.SAY_DISTANCE),
                _ => null,
            };
            List<string> names = Names(players);
            if (!names.Contains(player.Name, StringComparer.Ordinal))
                names.Insert(0, player.Name);
            return names;
        }
        catch (Exception)
        {
            return [player.Name];
        }
    }

    private static List<string> Names(IEnumerable<GamePlayer> players) =>
        players == null ? [] : players.Where(player => player != null && !string.IsNullOrEmpty(player.Name)).Select(player => player.Name)
            .Distinct(StringComparer.Ordinal).ToList();

    private static string RoomOf(Utterance line)
    {
        if (line.Room != null)
            return line.Room;
        try
        {
            return line.Channel switch
            {
                "tell" => line.To ?? "",
                "guild" => line.To ?? (line.Body ?? line.Who)?.Guild?.Name ?? "",
                "group" => GroupRoom((line.Body ?? line.Who)?.Group),
                "say" or "yell" => (line.Body ?? line.Who)?.CurrentZone?.Description ?? "",
                _ => GlobalConstants.RealmToName(line.Realm),
            };
        }
        catch (Exception)
        {
            return "";
        }
    }

    private static string GroupRoom(Group group) => group?.Leader?.Name is { } leader ? "group:" + leader : "group";

    // ---- The log ---------------------------------------------------------------------------------------------------

    private static long Now()
    {
        try { return GameLoop.GameLoopTime; } catch { return 0; }
    }

    private static void Write(string row)
    {
        try
        {
            if (Interlocked.Exchange(ref _sessionWritten, 1) == 0)
                Emit($"CHAT_SESSION build={ForkBuildInfo.BuildName} commit={ForkBuildInfo.Commit} rows=say,hear,held,burst,flag gate=non-answer");
            Emit(row);
        }
        catch (Exception)
        {
            // Logging only.
        }
    }

    private static void Emit(string row)
    {
        TestSink?.Invoke(row);
        if (Log.IsInfoEnabled)
            Log.Info(row);
    }

    private static string Quote(string text) =>
        "\"" + (text ?? "").Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ').Trim() + "\"";

    /// <summary>Forgets the rows and counts (tests).</summary>
    public static void ResetForTests()
    {
        lock (Recent)
        {
            Recent.Clear();
            LastHeardBy.Clear();
            LastHeardIn.Clear();
            LastInRoom.Clear();
        }
        Interlocked.Exchange(ref _lines, 0);
        Interlocked.Exchange(ref _noContract, 0);
        Interlocked.Exchange(ref _held, 0);
        Interlocked.Exchange(ref _sessionWritten, 0);
    }
}
