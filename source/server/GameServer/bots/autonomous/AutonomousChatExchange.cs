using System;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// One multi-line exchange in a channel: a field thread, a story, the replies to an opener, a burst of reactions (owner,
/// 2026-11-08: "faction feels disconnected... The chats MUST be top notch"). Before this, every line of an exchange was its
/// own timer, set when the exchange was planned; the 2-second spacing rule then put a line back by 2-4 s (three times at
/// most) while the next line still fired on time, so answers came before questions ("main line first" before "solo or
/// grp?", "sup" last), and a line whose speaker was busy was dropped while the lines after it still played ("makes sense
/// ty" to nothing). Now an exchange is a queue: line N+1 is timed from when line N was really said, a line that can't be
/// said ends the exchange (or is skipped when it is an optional chime), and a short line whose referent has scrolled away
/// under other talk is not said at all.
///
/// Every line said inside an exchange is tagged for the transcript (CHAT_LINE src=, ex=, i=, role=), so a played evening
/// can be audited line by line (source/tools/chat_audit.py).
/// </summary>
public static class AutonomousChatExchange
{
    // ---- the transcript's source tag --------------------------------------------------------------------------------------

    private static long _nextId;

    /// <summary>A new exchange id (unique for the server's run).</summary>
    public static long NewId() => Interlocked.Increment(ref _nextId);

    /// <summary>What produced a line: its feature, its exchange, its index in the exchange and its role there.</summary>
    public sealed record Source(string Feature, long Exchange, int Index, string Role, GameBot Speaker = null);

    [ThreadStatic]
    private static Source _current;

    /// <summary>The source of the line being said on this thread now, or null.</summary>
    public static Source Current => _current;

    /// <summary>
    /// The features /faction's ceiling doesn't hold (1.1.3): "event", a realm's big moment said on purpose (a keep or relic
    /// taken, a server first, the raid's Leeroy), itself capped (AutonomousChatBursts.CappedSize); and "player", a line said to
    /// or about the real player (bots answer you first). Every other bot line waits for room or isn't said.
    /// </summary>
    public static bool ExemptFromCeiling(Source source) => source != null && source.Feature is "event" or "player";

    /// <summary>
    /// After a scene ends, the first lines that waited for it would all go at once (live test 2026-11-08: 7 lines in 7 s): for
    /// this long an opener needs <see cref="ResumeSpacingMs"/> since the channel's last line; otherwise <see cref="OpenerSpacingMs"/>.
    /// </summary>
    public const long ResumeAfterSceneMs = 120_000, ResumeSpacingMs = 10_000, OpenerSpacingMs = 3_000;

    /// <summary>Whether a line starts something (an opener: untagged, or line 0 of its exchange) rather than goes on with one.</summary>
    public static bool IsOpener(Source source) => source == null || source.Index == 0;

    /// <summary>
    /// The /faction sink's rule (AutonomousBotChatCoordinator.BroadcastFactionAsWritten), pure: null when the line may be said,
    /// else why it is held. A tagged event or a line to the player always goes. A running scene owns the channel: only its own
    /// lines go. Every other line must fit under the ceiling, and an opener must keep its distance from the line before it
    /// (more so in the two minutes after a scene, so the lines that waited don't all land at once).
    /// </summary>
    public static string SinkHold(Source source, bool sceneRunning, bool fits, long now, long lastLineAt, long sceneEndedAt)
    {
        if (ExemptFromCeiling(source))
            return null;
        bool scene = IsScene(source);
        if (sceneRunning && !scene)
            return "scene";
        if (!fits)
            return "ceiling";
        if (!scene && IsOpener(source))
        {
            long spacing = now - sceneEndedAt < ResumeAfterSceneMs ? ResumeSpacingMs : OpenerSpacingMs;
            if (now - lastLineAt < spacing)
                return "spacing";
        }
        return null;
    }

    // ---- a feature that is due: a gold spammer, a rumour, a roleplayer's line ---------------------------------------------

    /// <summary>
    /// How long a feature that has decided to speak in /faction waits for its first line to be said, and how often it tries
    /// (1.1.3 RC1, live check 2026-11-08: a gold spammer logged its start, its ads were held by a scene and the ceiling after
    /// it, each held ad was spent, and it never spoke; a held rumour was lost and /lstest said "no line"). Its lines count
    /// against the ceiling like any line, but it keeps its turn: tried again every <see cref="DueRetryMs"/>, given up (logged)
    /// after <see cref="DueWaitMs"/>.
    /// </summary>
    public const long DueWaitMs = 180_000, DueRetryMs = 3_000;

    public enum DueStep { Said, Retry, GiveUp }

    /// <summary>
    /// What a due feature does after a try: said; held by a running scene, try again (a scene owns the channel and ends on its
    /// own, so waiting behind it costs nothing); otherwise try again soon, or give up once it has waited
    /// <see cref="DueWaitMs"/> since it was due or since the last scene ended, whichever is later. Pure.
    /// </summary>
    public static DueStep AfterTry(bool said, string held, long dueSince, long sceneEndedAt, long now) =>
        said ? DueStep.Said
        : held == "scene" ? DueStep.Retry
        : now - Math.Max(dueSince, sceneEndedAt) >= DueWaitMs ? DueStep.GiveUp
        : DueStep.Retry;

    /// <summary>The log line of a feature that is waiting for its turn (chat_audit.py checks that its exchange is said).</summary>
    public static string DueLog(string feature, eRealm realm, long exchange, string detail = "") =>
        $"LIVING_FEATURE_DUE feature={feature} realm={GlobalConstants.RealmToName(realm)} ex={exchange}{detail}";

    /// <summary>The log line of a due feature that gave up without a line (no_audience: nobody was left to hear it).</summary>
    public static string DroppedLog(string feature, eRealm realm, long exchange, long waitedMs, string reason) =>
        $"LIVING_FEATURE_DROPPED feature={feature} realm={GlobalConstants.RealmToName(realm)} ex={exchange} waited_s={waitedMs / 1_000} reason={reason ?? "held"}";

    /// <summary>A line of a running debate, storyline, dunce scene or tale (AutonomousBotDebates): it owns /faction while it
    /// plays. (PC check 2026-11-10, build cf2c873: a tale's lines were tagged "tale", which this did not know, so the tale held
    /// /faction and its own lines with it; nothing was heard.)</summary>
    public static bool IsScene(Source source) => source != null && source.Feature is "debate" or "storyline" or "tale";

    /// <summary>Whether one more line fits under a channel's ceiling: the rate it would make, not the rate before it. Pure.</summary>
    public static bool Fits(double perMinute, double ceiling, double windowMinutes = 2.0) => perMinute + 1.0 / windowMinutes <= ceiling + 1e-9;

    /// <summary>Tags the lines said until the scope is disposed (the transcript reads it).</summary>
    public static Scope Tag(string feature, long exchange, int index, string role, GameBot speaker = null) =>
        new(new Source(feature, exchange, index, role, speaker));

    /// <summary>A source tag for the lines said inside it; the previous tag comes back on dispose.</summary>
    public readonly struct Scope : IDisposable
    {
        private readonly Source _previous;
        private readonly bool _set;

        public Scope(Source source)
        {
            _previous = _current;
            _current = source;
            _set = true;
        }

        /// <summary>Restores the tag before this one (a default scope, which set none, changes nothing).</summary>
        public void Dispose()
        {
            if (_set)
                _current = _previous;
        }
    }

    // ---- lanes: what a channel carried -------------------------------------------------------------------------------------

    /// <summary>The lane of a realm's /faction.</summary>
    public static string FactionKey(eRealm realm) => "faction:" + (int)realm;

    /// <summary>The lane of a guild's chat.</summary>
    public static string GuildKey(string guildId) => "guild:" + guildId;

    /// <summary>The lane of a region's /say.</summary>
    public static string SayKey(ushort region) => "say:" + region;

    /// <summary>What one channel carried: a line count, its last line's time, who said what when, and its running exchanges.</summary>
    public sealed class Lane
    {
        public long Seq;
        public long LastAt = long.MinValue / 2;
        public readonly Dictionary<string, long> LastSeqBy = new(StringComparer.OrdinalIgnoreCase);
        public readonly Dictionary<long, long> Running = new();
    }

    private static readonly Dictionary<string, Lane> Lanes = new(StringComparer.Ordinal);

    /// <summary>The lane of a channel key (made on first use).</summary>
    public static Lane LaneOf(string key)
    {
        lock (Lanes)
        {
            if (!Lanes.TryGetValue(key, out Lane lane))
                Lanes[key] = lane = new Lane();
            return lane;
        }
    }

    /// <summary>A line was said in the channel (by a bot or a player): counted, and remembered by speaker.</summary>
    public static void NoteLine(string key, string speaker, long now)
    {
        Lane lane = LaneOf(key);
        lock (lane)
        {
            lane.Seq++;
            lane.LastAt = now;
            if (!string.IsNullOrEmpty(speaker))
            {
                if (lane.LastSeqBy.Count > 4_096)
                    lane.LastSeqBy.Clear();
                lane.LastSeqBy[speaker] = lane.Seq;
            }
        }
    }

    /// <summary>How long an exchange counts as running after its last line (an exchange whose timer was lost ends here).</summary>
    public const long RunningForMs = 120_000;

    /// <summary>How many multi-line exchanges may run in one channel at once: more than this and nobody can follow them.</summary>
    public const int MaxRunning = 3;

    /// <summary>The exchanges running in a lane now.</summary>
    public static int RunningCount(string key, long now)
    {
        Lane lane = LaneOf(key);
        lock (lane)
        {
            List<long> stale = null;
            foreach (KeyValuePair<long, long> entry in lane.Running)
                if (now - entry.Value > RunningForMs)
                    (stale ??= []).Add(entry.Key);
            if (stale != null)
                foreach (long id in stale)
                    lane.Running.Remove(id);
            return lane.Running.Count;
        }
    }

    /// <summary>Whether another exchange may start in the lane now (fewer than <see cref="MaxRunning"/> run).</summary>
    public static bool RoomFor(string key, long now) => RunningCount(key, now) < MaxRunning;

    /// <summary>Marks an exchange as running in the lane now (its last line, or its start).</summary>
    public static void Touch(Lane lane, long id, long now)
    {
        lock (lane)
            lane.Running[id] = now;
    }

    /// <summary>The exchange is over: it no longer counts against the lane's room.</summary>
    public static void Finish(Lane lane, long id)
    {
        lock (lane)
            lane.Running.Remove(id);
    }

    // ---- the exchange's lines and the rules that time them ----------------------------------------------------------------

    /// <summary>
    /// One line of an exchange: who says it (an index into the exchange's speakers), what, how long after the line before it
    /// (or after the start, for the first), and its role ("asker", "answerer", "third", "teller", "riff", "reactor",
    /// "closing"). An optional line (a third bot's chime, one reaction of a burst) is skipped when its speaker can't say it;
    /// any other line ends the exchange then. A lone line comes long after (an unanswered "anyone?"): it is said only if its
    /// speaker said nothing else in the channel since. A reaction is timed against the exchange's trigger.
    /// </summary>
    public sealed record Line(int Who, string Text, int GapMs, string Role, bool Optional = false, bool Reaction = false, bool Lone = false);

    /// <summary>The timing rules (defaults for /faction).</summary>
    public sealed record Rules
    {
        /// <summary>No line within this long of the channel's last line; wait instead (round 8).</summary>
        public int SpacingMs { get; init; } = 2_000;
        public int MaxSpacingWaits { get; init; } = 3;
        /// <summary>A busy speaker (fighting, casting) is waited for this long, this many times.</summary>
        public int BusyRetryMs { get; init; } = 2_500;
        public int MaxBusyWaits { get; init; } = 2;
        /// <summary>A short line ("which part?", "ty") is not said once this many other lines came since the exchange's last.</summary>
        public int MaxInterleaveShort { get; init; } = 3;
        /// <summary>A longer line is not said once this many other lines came since the exchange's last.</summary>
        public int MaxInterleave { get; init; } = 6;
        /// <summary>No reaction once this many other lines came since the trigger, or this long after it.</summary>
        public int MaxSinceTrigger { get; init; } = 4;
        public long ReactionWithinMs { get; init; } = 45_000;
        /// <summary>No line (but a lone one) this long after the exchange's last (with its waits, at most about 42 s).</summary>
        public long MaxLateMs { get; init; } = 25_000;

        public static readonly Rules Faction = new();
        public static readonly Rules Say = new() { SpacingMs = 0, MaxInterleaveShort = 4, MaxInterleave = 8 };
    }

    /// <summary>What to do with the line due now.</summary>
    public enum Act { Say, Wait, Skip, Stop }

    public readonly record struct Decision(Act Act, int WaitMs, string Reason);

    /// <summary>A short line: three words or fewer (its referent must still be on screen).</summary>
    public static bool IsShort(string text)
    {
        string norm = Normalize(text);
        if (norm.Length == 0)
            return true;
        int words = 1;
        foreach (char c in norm)
            if (c == ' ')
                words++;
        return words <= 3 && norm.Length <= 24;
    }

    /// <summary>
    /// The state of one exchange, clock-driven and pure (the game's timers and the tests' simulated clock both drive it):
    /// <see cref="Due"/> says when the next line is due; <see cref="Step"/> decides it and says it through the delivery.
    /// </summary>
    public sealed class Run
    {
        private readonly IReadOnlyList<Line> _lines;
        private readonly Rules _rules;
        private int _waitsSpacing, _waitsBusy;

        public long Id { get; }
        public string Feature { get; }
        public string LaneKey { get; }
        public Lane Lane { get; }
        /// <summary>The index the first line has in the transcript (1 when the opener was said before, as index 0).</summary>
        public int FirstIndex { get; }
        public int Next { get; private set; }
        public long DueAt { get; private set; }
        public bool Done { get; private set; }
        public string StopReason { get; private set; }
        public int Said { get; private set; }

        private long _prevSeq, _prevAt, _triggerSeq, _triggerAt;
        private int _ownSinceTrigger;

        public Run(long id, string feature, string laneKey, Lane lane, IReadOnlyList<Line> lines, long now, Rules rules = null, int firstIndex = 1)
        {
            Id = id;
            Feature = feature;
            LaneKey = laneKey;
            Lane = lane;
            _lines = lines ?? [];
            _rules = rules ?? Rules.Faction;
            FirstIndex = firstIndex;
            lock (lane)
            {
                _prevSeq = _triggerSeq = lane.Seq;
                _prevAt = _triggerAt = now;
            }
            Done = _lines.Count == 0;
            DueAt = Done ? now : now + Math.Max(0, _lines[0].GapMs);
        }

        public Line Current => Done || Next >= _lines.Count ? null : _lines[Next];

        /// <summary>
        /// Decides the line due now. <paramref name="free"/> says whether its speaker can talk; <paramref name="speakerName"/>
        /// is that speaker's name (for a lone line's check). Pure but for the lane it reads.
        /// </summary>
        public Decision Decide(long now, bool free, string speakerName)
        {
            Line line = Current;
            if (line == null)
                return new Decision(Act.Stop, 0, "done");
            long seq, lastAt, speakerSeq = 0;
            lock (Lane)
            {
                seq = Lane.Seq;
                lastAt = Lane.LastAt;
                if (speakerName != null)
                    Lane.LastSeqBy.TryGetValue(speakerName, out speakerSeq);
            }
            if (!free)
            {
                if (_waitsBusy < _rules.MaxBusyWaits)
                    return new Decision(Act.Wait, _rules.BusyRetryMs, "speaker busy");
                return line.Optional ? new Decision(Act.Skip, 0, "speaker busy") : new Decision(Act.Stop, 0, "speaker busy");
            }
            if (line.Reaction)
            {
                long foreign = seq - _triggerSeq - _ownSinceTrigger;
                if (foreign > _rules.MaxSinceTrigger || now - _triggerAt > _rules.ReactionWithinMs)
                    return new Decision(Act.Stop, 0, "the moment passed");
            }
            else if (line.Lone)
            {
                // "anyone?" two minutes on is only right if the speaker said nothing else meanwhile.
                if (speakerSeq > _prevSeq)
                    return new Decision(Act.Stop, 0, "the speaker moved on");
            }
            else if (Said > 0 || FirstIndex > 0)
            {
                long since = seq - _prevSeq;
                int cap = IsShort(line.Text) ? _rules.MaxInterleaveShort : _rules.MaxInterleave;
                if (since > cap)
                    return new Decision(Act.Stop, 0, "lost its referent");
                if (now - _prevAt > _rules.MaxLateMs + _rules.MaxSpacingWaits * 2L * _rules.SpacingMs + _rules.MaxBusyWaits * (long)_rules.BusyRetryMs)
                    return new Decision(Act.Stop, 0, "too late");
            }
            if (_rules.SpacingMs > 0 && now - lastAt < _rules.SpacingMs && _waitsSpacing < _rules.MaxSpacingWaits)
                return new Decision(Act.Wait, _rules.SpacingMs, "spacing");
            return new Decision(Act.Say, 0, null);
        }

        /// <summary>
        /// Runs the line due now: says it (through <paramref name="deliver"/>, which returns false when it could not), waits,
        /// skips or stops. Returns the time the next line is due, or null when the exchange is over.
        /// </summary>
        public long? Step(long now, Func<Line, bool> free, Func<Line, string> nameOf, Func<Line, int, bool> deliver, Random random = null)
        {
            if (Done)
                return null;
            Line line = Current;
            Decision decision = Decide(now, free(line), nameOf(line));
            switch (decision.Act)
            {
                case Act.Wait:
                    if (decision.Reason == "spacing")
                        _waitsSpacing++;
                    else
                        _waitsBusy++;
                    int jitter = random == null ? 0 : random.Next(Math.Max(1, decision.WaitMs));
                    DueAt = now + decision.WaitMs + jitter;
                    return DueAt;
                case Act.Stop:
                    return End(decision.Reason);
                case Act.Skip:
                    return Advance(now);
            }
            if (!deliver(line, FirstIndex + Next))
                return line.Optional ? Advance(now) : End("not delivered");
            Said++;
            lock (Lane)
            {
                _prevSeq = Lane.Seq;
                if (line.Reaction)
                    _ownSinceTrigger++;
            }
            _prevAt = now;
            return Advance(now);
        }

        private long? Advance(long now)
        {
            Next++;
            _waitsSpacing = _waitsBusy = 0;
            if (Next >= _lines.Count)
                return End("done");
            DueAt = now + Math.Max(0, _lines[Next].GapMs);
            return DueAt;
        }

        private long? End(string reason)
        {
            Done = true;
            StopReason ??= reason;
            return null;
        }
    }

    // ---- the game's driver -------------------------------------------------------------------------------------------------

    /// <summary>
    /// Plays an exchange on the game's timers: each line by <paramref name="speakers"/>[line.Who] (or, when that bot can't
    /// talk, by a stand-in from <paramref name="standIns"/>[line.Who] that can), tagged for the transcript, through
    /// <paramref name="deliver"/>. Returns the run (null when there was nothing to play).
    /// </summary>
    public static Run Play(string feature, long id, string laneKey, IReadOnlyList<Line> lines, GameBot[] speakers, Func<GameBot, bool> canSpeak,
        Func<GameBot, Line, bool> deliver, Rules rules = null, int firstIndex = 1, GameBot[][] standIns = null)
    {
        if (lines == null || lines.Count == 0 || speakers == null)
            return null;
        long now = GameLoop.GameLoopTime;
        Lane lane = LaneOf(laneKey);
        var run = new Run(id, feature, laneKey, lane, lines, now, rules, firstIndex);
        Touch(lane, id, now);
        var chosen = new Dictionary<int, GameBot>();
        GameBot SpeakerOf(Line line)
        {
            if (chosen.TryGetValue(line.Who, out GameBot fixedBot))
                return fixedBot;
            GameBot first = line.Who < speakers.Length ? speakers[line.Who] : null;
            if (first != null && canSpeak(first))
                return first;
            GameBot[] others = standIns != null && line.Who < standIns.Length ? standIns[line.Who] : null;
            if (others != null)
                foreach (GameBot other in others)
                    if (other != null && Array.IndexOf(speakers, other) < 0 && !chosen.ContainsValue(other) && canSpeak(other))
                        return other;
            return first;
        }
        void Arm(long due)
        {
            Line line = run.Current;
            GameBot owner = line == null ? null : SpeakerOf(line) ?? speakers[0];
            if (owner == null)
            {
                Finish(lane, id);
                return;
            }
            _ = new ECSGameTimer(owner, _ =>
            {
                long at = GameLoop.GameLoopTime;
                long? next;
                try
                {
                    next = run.Step(at, l => SpeakerOf(l) is { } bot && canSpeak(bot), l => SpeakerOf(l)?.Name, (l, index) =>
                    {
                        GameBot bot = SpeakerOf(l);
                        if (bot == null)
                            return false;
                        using (Tag(feature, id, index, l.Role, bot))
                        {
                            if (!deliver(bot, l))
                                return false;
                        }
                        // A role keeps its speaker for the rest of the exchange ("cab, 50" and "kk inv" by the same two).
                        chosen[l.Who] = bot;
                        return true;
                    }, Random.Shared);
                }
                catch (Exception)
                {
                    next = null;
                }
                if (next is { } nextAt)
                {
                    Touch(lane, id, at);
                    Arm(nextAt);
                }
                else
                    Finish(lane, id);
                return 0;
            }, (int)Math.Max(1, due - GameLoop.GameLoopTime));
        }
        Arm(run.DueAt);
        return run;
    }

    // ---- short lines, once in ten minutes ---------------------------------------------------------------------------------

    /// <summary>
    /// Short reactions ("oof", "rip", "gz") heard in a channel in the last ten minutes (owner 2026-11-08: "oof" from several
    /// bots within minutes). A reaction already heard there is picked again or left out.
    /// </summary>
    public sealed class ShortWindow
    {
        public const long WindowMs = 10 * 60_000;
        private readonly Dictionary<(string Key, string Text), long> _heard = new();

        /// <summary>
        /// A reaction about to be said in the channel (the send itself, every path): false when the channel heard it in the window;
        /// otherwise it is recorded as said now (a plan's own claim, which lies ahead, doesn't hold it back). 1.1.3.
        /// </summary>
        public bool SayOrHold(string key, string text, long now)
        {
            string norm = Normalize(text);
            lock (_heard)
            {
                if (_heard.TryGetValue((key, norm), out long at) && at <= now && now - at < WindowMs)
                    return false;
                _heard[(key, norm)] = now;
                return true;
            }
        }

        /// <summary>Whether the reaction was not heard in the channel in the window.</summary>
        public bool Fresh(string key, string text, long now)
        {
            string norm = Normalize(text);
            lock (_heard)
                return !_heard.TryGetValue((key, norm), out long at) || now - at >= WindowMs;
        }

        /// <summary>
        /// Claims the reaction for the channel when it is fresh; false when it was heard in the window. A reaction is claimed when
        /// it is planned and said up to <paramref name="aheadMs"/> later, so the window counts from then.
        /// </summary>
        public bool TryUse(string key, string text, long now, long aheadMs = 60_000)
        {
            string norm = Normalize(text);
            lock (_heard)
            {
                if (_heard.TryGetValue((key, norm), out long at) && now - at < WindowMs)
                    return false;
                if (_heard.Count > 4_096)
                {
                    var old = new List<(string, string)>();
                    foreach (KeyValuePair<(string Key, string Text), long> entry in _heard)
                        if (now - entry.Value >= WindowMs)
                            old.Add(entry.Key);
                    foreach ((string, string) k in old)
                        _heard.Remove(k);
                }
                _heard[(key, norm)] = now + Math.Max(0, aheadMs);
                return true;
            }
        }
    }

    // The reactions source/tools/chat_audit.py counts (keep the two lists together).
    private static readonly HashSet<string> Reactions = new(StringComparer.Ordinal)
    {
        "oof", "rip", "lol rip", "ouch", "ugh", "bleh", "gz", "gz gz", "grats", "gratz", "grats grats", "grats man", "nice", "nice gz", "woot", "wow nice",
        "sweet", "lucky", "lucky you", "lucky you lol", "jealous", "show off", "well earned", "well played", "well done", "nice going", "nice score",
        "good stuff", "go you", "way to go", "look at you", "must be nice", "not bad at all", "very nice", "brag brag brag", "teach me",
        "pics or it didnt happen", "classic", "happens", "happens lol", "rip grp", "that stinks", "that sucks", "ugh that sucks", "ugh same",
        "ugh the worst", "rough", "rough night huh", "bad luck", "been there", "bleh been there", "hang in there", "it gets better", "ah man",
        "sorry that is rough", "oof sorry", "oof sorry heh", "thats annoying", "argh hate that", "ugh i feel you", "the dice hate us all",
        "the worst feeling", "thats just mean", "dont let it get to you", "happens to the best of us", "yep happened to me too",
        "next one will go better", "take a break maybe", "been there haha", "grr i know that one", "been like that all night haha",
        "aww sorry that is awful", "ha welcome to my week", "yep it does that", "forgive me unlucky", "gz u earned it", "gz you earned it",
        "ha nice now find something meaner", "now thats a good night", "lol", "heh", "haha", "hehe", "true", "lol true", "true true", "ha", "rip me",
        "lol how",
    };

    /// <summary>A pure reaction ("oof", "rip", "lol", "gz"): it says nothing but how the speaker feels about the line before.</summary>
    public static bool IsReaction(string text) => Reactions.Contains(Normalize(text));

    /// <summary>The server's short-reaction window.</summary>
    public static readonly ShortWindow Shorts = new();

    /// <summary>Lowercase letters, digits and single spaces.</summary>
    public static string Normalize(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;
        var chars = new char[text.Length];
        int n = 0;
        bool space = true;
        foreach (char raw in text)
        {
            char c = char.ToLowerInvariant(raw);
            if (char.IsLetterOrDigit(c))
            {
                chars[n++] = c;
                space = false;
            }
            else if (!space)
            {
                chars[n++] = ' ';
                space = true;
            }
        }
        while (n > 0 && chars[n - 1] == ' ')
            n--;
        return new string(chars, 0, n);
    }

    /// <summary>
    /// Converts a plan's steps (each at a time after the start) to lines (each a gap after the one before), keeping the plan's
    /// order. Roles by speaker: <paramref name="roles"/>[Who].
    /// </summary>
    public static List<Line> FromSteps(IEnumerable<AutonomousChatFieldTalk.Step> steps, string[] roles, Func<AutonomousChatFieldTalk.Step, bool> optional = null,
        long loneAfterMs = 60_000)
    {
        var lines = new List<Line>();
        int at = 0;
        foreach (AutonomousChatFieldTalk.Step step in steps)
        {
            int gap = Math.Max(0, step.AtMs - at);
            at = Math.Max(at, step.AtMs);
            string role = step.Who >= 0 && step.Who < roles.Length ? roles[step.Who] : "speaker";
            lines.Add(new Line(step.Who, step.Text, gap, role, optional?.Invoke(step) == true, Lone: gap >= loneAfterMs));
        }
        return lines;
    }
}
