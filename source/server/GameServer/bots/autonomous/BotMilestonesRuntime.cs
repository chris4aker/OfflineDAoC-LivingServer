using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS;

/// <summary>What one gamebot's milestones are read from: its person and its real numbers (a plain value, so tests need no game).</summary>
public readonly record struct BotMilestoneSubject(BotPersonKey Person, long CharacterId, string Name, string ClassName, eRealm Realm,
    int Level, long Experience, long RealmPoints, int RealmLevel, BotPersonality? Personality);

/// <summary>A high-end raid kill the bot was at (RealmRaidLootOwner's roster): the boss, and the item it received, or null.</summary>
public readonly record struct BotMilestoneRaidKill(string Boss, string Item)
{
    public bool Won => !string.IsNullOrEmpty(Item);
}

public enum BotMilestoneStep { Chosen, Dropped, Point, Reached }

/// <summary>One thing that happened to a bot's arc in <see cref="BotMilestoneLife.Advance"/>.</summary>
/// <param name="Unsought">A trophy won while the bot was chasing something else (or nothing): recorded, never told as its goal.</param>
public readonly record struct BotMilestoneEvent(BotMilestoneStep Step, BotMilestoneArc Arc, int Point, double Progress,
    OfflineBotMilestoneRecord Row, bool Unsought = false);

/// <summary>
/// Bots as people for 1.2, work package 6: the game side of personal milestones (rules: <see cref="BotMilestones"/>,
/// <see cref="BotMilestoneTalk"/>). Autonomous gamebots only (BotPeopleScope, Record): a real player's character, companions,
/// hired companions, /spawn helpers, the Epic Special Forces and buffbots never get an arc. It runs at events only: a gamebot
/// logs in, gains experience or realm points, or is at a big raid kill (and receives an item from its loot); nothing is
/// polled and nothing runs per tick (a bot busy fighting when its line is due has a one-shot timer, 15-30 s, at most 8). Rows are saved by the Bots as people store (batched off the game loop). Its lines
/// come from the corpus (milestones.lines) and pass the chat budgets. Tests only until seen in the owner's PC log.
/// </summary>
public static class BotMilestoneLife
{
    private static readonly Logger Log = LoggerManager.Create(typeof(BotMilestoneLife));

    public const string ArcTag = "BOT_MILESTONE_ARC";
    public const string ProgressTag = "BOT_MILESTONE_PROGRESS";
    public const string ReachedTag = "BOT_MILESTONE_REACHED";
    public const string TalkTag = "BOT_MILESTONE_TALK";
    public const string SummaryTag = "BOT_MILESTONE_SUMMARY";

    /// <summary>BOT_MILESTONE_ARC and BOT_MILESTONE_PROGRESS lines a minute at most (a first start chooses thousands of
    /// arcs); the rest are counted in the summary. REACHED and TALK lines are rare and always logged.</summary>
    public const int LinesPerMinute = 30;

    private static bool On => BotPeopleSwitches.Current.Milestones;

    // ---- counts and the summary -----------------------------------------------------------------------------------------

    private static readonly ConcurrentDictionary<string, long> Counts = new(StringComparer.Ordinal);

    public static void Count(string key, long by = 1) => Counts.AddOrUpdate(key, by, (_, n) => n + by);

    private static IEnumerable<string> TakeSummary(long seconds)
    {
        var taken = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string key in Counts.Keys.ToArray())
            if (Counts.TryRemove(key, out long n))
                taken[key] = n;
        return [BotMilestoneTalk.SummaryLine(seconds, taken, ActiveCounts(BotPeopleStore.Current))];
    }

    /// <summary>Active arcs in the store now, by arc (ding50, rr5, dragon_trophy).</summary>
    public static int[] ActiveCounts(BotPeopleStore store)
    {
        var active = new int[3];
        foreach (OfflineBotMilestoneRecord row in store.Milestones.Where(r => r.State == OfflineBotMilestoneRecord.Active))
        {
            int i = Array.IndexOf(BotMilestones.Arcs, BotMilestones.Parse(row.Arc));
            if (i >= 0)
                active[i]++;
        }
        return active;
    }

    private static long _lineMinute, _linesThisMinute;

    private static bool TakeLine()
    {
        if (!Log.IsInfoEnabled)
            return false;
        long minute = Environment.TickCount64 / 60_000;
        if (Interlocked.Exchange(ref _lineMinute, minute) != minute)
            Interlocked.Exchange(ref _linesThisMinute, 0);
        if (Interlocked.Increment(ref _linesThisMinute) > LinesPerMinute)
        {
            Count("suppressed");
            return false;
        }
        Count("lines");
        return true;
    }

    // ---- the arc (store, testable) ----------------------------------------------------------------------------------------

    private static (BotPersonKey, long, string) Key(in BotMilestoneSubject s, BotMilestoneArc arc) => (s.Person, s.CharacterId, BotMilestones.Id(arc));

    /// <summary>The character's active arc row, or null.</summary>
    public static OfflineBotMilestoneRecord ActiveRow(BotPeopleStore store, BotPersonKey person, long characterId)
    {
        foreach (BotMilestoneArc arc in BotMilestones.Arcs)
            if (store.Milestones.TryGet((person, characterId, BotMilestones.Id(arc)), out OfflineBotMilestoneRecord row) &&
                row.State == OfflineBotMilestoneRecord.Active)
                return row;
        return null;
    }

    private static string Now(DateTime utc) => utc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);

    /// <summary>
    /// Moves a gamebot's arc on from its numbers now (and, at a high-end raid kill, <paramref name="raid"/>): drops an arc no
    /// longer possible, chooses one when none is active, counts a raid kill without a drop, reaches the arc when its numbers
    /// say so (then chooses the next), and reports a talk point passed but not yet handled (the caller decides the talk and
    /// calls <see cref="MarkPoint"/>). Thread-safe through the store's table lock. Changes only milestone rows.
    /// </summary>
    public static List<BotMilestoneEvent> Advance(BotPeopleStore store, BotMilestoneSubject s, DateTime nowUtc, BotMilestoneRaidKill? raid = null)
    {
        var events = new List<BotMilestoneEvent>(2);
        if (s.Person.IsNone)
            return events;
        string now = Now(nowUtc);
        BotPersonKey person = s.Person;
        long characterId = s.CharacterId;
        // A real raid drop is remembered whatever the bot is chasing (or nothing, or below level 45): its trophy is reached
        // there and then, so it can never later take the trophy arc and say it has won nothing.
        if (raid is { Won: true } win && ActiveRow(store, person, characterId) is not { Arc: BotMilestones.DragonTrophyId })
        {
            var key = (person, characterId, BotMilestones.DragonTrophyId);
            if (!(store.Milestones.TryGet(key, out OfflineBotMilestoneRecord had) && had.State == OfflineBotMilestoneRecord.Reached))
            {
                OfflineBotMilestoneRecord trophy = store.Milestones.Change(key,
                    () => new OfflineBotMilestoneRecord { PersonKey = person.ToString(), CharacterId = characterId, Arc = BotMilestones.DragonTrophyId },
                    r =>
                    {
                        r.State = OfflineBotMilestoneRecord.Reached;
                        r.Realm = (int)s.Realm;
                        r.Name = s.Name ?? string.Empty;
                        r.ClassName = s.ClassName ?? string.Empty;
                        if (string.IsNullOrEmpty(r.StartedUtc))
                            r.StartedUtc = now;
                        r.ReachedUtc = now;
                        r.Trophy = Clip(win.Item, 96);
                        r.Source = Clip(win.Boss, 64);
                    });
                events.Add(new BotMilestoneEvent(BotMilestoneStep.Reached, BotMilestoneArc.DragonTrophy, 100, 1, trophy, Unsought: true));
            }
            raid = null;
        }
        for (int pass = 0; pass < 2; pass++)
        {
            OfflineBotMilestoneRecord row = ActiveRow(store, person, characterId);
            BotMilestoneArc arc = row == null ? BotMilestoneArc.None : BotMilestones.Parse(row.Arc);
            if (row != null && !BotMilestones.StillPossible(arc, s.Level))
            {
                row = store.Milestones.Change(Key(s, arc), () => row, r => r.State = OfflineBotMilestoneRecord.Dropped);
                events.Add(new BotMilestoneEvent(BotMilestoneStep.Dropped, arc, 0, 0, row));
                row = null;
            }
            if (row == null)
            {
                arc = BotMilestones.Choose(s.Level, s.RealmLevel, s.Personality, characterId,
                    a => store.Milestones.TryGet((person, characterId, BotMilestones.Id(a)), out OfflineBotMilestoneRecord r) ? r.State : null);
                if (arc == BotMilestoneArc.None)
                    return events;
                BotMilestoneSubject subject = s;
                BotMilestoneArc chosen = arc;
                row = store.Milestones.Change(Key(s, arc),
                    () => new OfflineBotMilestoneRecord { PersonKey = subject.Person.ToString(), CharacterId = subject.CharacterId, Arc = BotMilestones.Id(chosen) },
                    r =>
                    {
                        r.State = OfflineBotMilestoneRecord.Active;
                        r.Realm = (int)subject.Realm;
                        r.Name = subject.Name ?? string.Empty;
                        r.ClassName = subject.ClassName ?? string.Empty;
                        r.StartedUtc = now;
                        r.ReachedUtc = string.Empty;
                        r.TalkPoint = 0;
                        r.Count = 0;
                        r.Trophy = string.Empty;
                        r.Source = string.Empty;
                    });
                events.Add(new BotMilestoneEvent(BotMilestoneStep.Chosen, arc, 0, BotMilestones.Progress(arc, s.Level, s.Experience, s.RealmPoints), row));
            }

            // A raid kill only counts once, toward the arc active at the kill.
            BotMilestoneRaidKill? kill = pass == 0 && arc == BotMilestoneArc.DragonTrophy ? raid : null;
            if (kill is { Won: false } miss)
                row = store.Milestones.Change(Key(s, arc), () => row, r =>
                {
                    r.Count++;
                    r.Source = Clip(miss.Boss, 64);
                });

            double progress = BotMilestones.Progress(arc, s.Level, s.Experience, s.RealmPoints);
            if (BotMilestones.Reached(arc, s.Level, s.RealmLevel, kill is { Won: true }))
            {
                row = store.Milestones.Change(Key(s, arc), () => row, r =>
                {
                    r.State = OfflineBotMilestoneRecord.Reached;
                    r.ReachedUtc = now;
                    r.Name = s.Name ?? r.Name;
                    r.ClassName = s.ClassName ?? r.ClassName;
                    if (kill is { Won: true } won)
                    {
                        r.Trophy = Clip(won.Item, 96);
                        r.Source = Clip(won.Boss, 64);
                    }
                });
                events.Add(new BotMilestoneEvent(BotMilestoneStep.Reached, arc, 100, 1, row));
                continue; // the next arc, if one fits now
            }
            int point = BotMilestones.PointOf(arc, progress, row.Count);
            if (point > row.TalkPoint)
                events.Add(new BotMilestoneEvent(BotMilestoneStep.Point, arc, point, progress, row));
            return events;
        }
        return events;
    }

    /// <summary>Saves that a talk point is handled (said, or passed in silence), with the talk's time when said.</summary>
    public static void MarkPoint(BotPeopleStore store, OfflineBotMilestoneRecord row, int point, DateTime? talkedUtc)
    {
        if (row == null || !BotPersonKey.TryParse(row.PersonKey, out BotPersonKey person))
            return;
        store.Milestones.Change((person, row.CharacterId, row.Arc ?? string.Empty), () => row, r =>
        {
            if (r.State != OfflineBotMilestoneRecord.Active || point <= r.TalkPoint)
                return;
            r.TalkPoint = point;
            if (talkedUtc is { } at)
                r.LastTalkUtc = Now(at);
        });
    }

    private static string Clip(string text, int length) =>
        string.IsNullOrEmpty(text) ? string.Empty : text.Length <= length ? text : text[..length];

    // ---- the read side: the goal tilt (owner decision 10) ------------------------------------------------------------------

    /// <summary>The active arc of a gamebot (no row is made; None when the switch is off or it has no person). Counts nothing.</summary>
    public static BotMilestoneArc ArcOf(GameBot bot, out OfflineBotMilestoneRecord row)
    {
        row = null;
        if (bot == null || bot.Level < BotMilestones.DingFromLevel || !On)
            return BotMilestoneArc.None;
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
            return BotMilestoneArc.None;
        row = ActiveRow(BotPeopleStore.Current, person, bot.DatabaseID);
        return row == null ? BotMilestoneArc.None : BotMilestones.Parse(row.Arc);
    }

    /// <summary>The town-break chance factor for a gamebot (1 = today; AutonomousBotPersonality.RollTownBreak).</summary>
    public static double TownBreakFactor(GameBot bot)
    {
        BotMilestoneArc arc = ArcOf(bot, out _);
        return arc == BotMilestoneArc.None ? 1.0 : BotMilestones.TownBreakFactor(arc, bot.Level, bot.DatabaseID);
    }

    /// <summary>How much more the bot wants an RvR slot (1 = no lean; AutonomousBotPersonality.PlanGoals).</summary>
    public static double RvrLean(GameBot bot)
    {
        BotMilestoneArc arc = ArcOf(bot, out _);
        return arc != BotMilestoneArc.RealmRank5 ? 1.0 : BotMilestones.RvrLean(arc, BotMilestones.RankProgress(bot.AutonomousRealmPoints));
    }

    // ---- the hooks ----------------------------------------------------------------------------------------------------------

    /// <summary>A gamebot came into the world (AutonomousBotRegistry.Register): it takes or resumes its arc.</summary>
    public static void OnLoggedIn(GameBot bot) => Consider(bot, null);

    /// <summary>A gamebot gained experience (GameBot.GainExperience, after any level it gained): the ding to 50.</summary>
    public static void OnExperience(GameBot bot)
    {
        if (bot != null && bot.Level >= BotMilestones.DingFromLevel)
            Consider(bot, null);
    }

    /// <summary>A gamebot gained realm points (GameBot.GainRealmPoints, after its realm level is set): realm rank 5. Every
    /// gain, not only a new realm level: a level 50 has no experience event, and RR4L9 is already past 90%.</summary>
    public static void OnRealmPoints(GameBot bot)
    {
        if (bot != null && bot.Level >= BotMilestones.MaxLevel)
            Consider(bot, null);
    }

    public static BotMilestoneSubject SubjectOf(GameBot bot) => new(BotPersonKey.Of(bot), bot.DatabaseID, bot.Name, AutonomousLivingServer.ClassOf(bot),
        bot.Realm, bot.Level, bot.Experience, bot.AutonomousRealmPoints, bot.RealmLevel,
        AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot) : null);

    /// <summary>Moves the bot's arc on and acts on what happened. True when a moment's own line was scheduled.</summary>
    private static bool Consider(GameBot bot, BotMilestoneRaidKill? raid, bool mayAnnounce = true)
    {
        // Below 45 only a raid drop matters (it is remembered); everything else starts at 45.
        if (bot == null || bot.Level < BotMilestones.DingFromLevel && raid is not { Won: true })
            return false;
        if (BotPeopleScope.Check(bot, BotPeopleRule.Record, On, out _) != BotPeopleRefusal.None)
            return false;
        using var aiProfile = AiProfile.Scope(AiSystem.Milestones); // AI_PROFILE: measures only
        bool announced = false;
        try
        {
            BotMilestoneSubject subject = SubjectOf(bot);
            foreach (BotMilestoneEvent e in Advance(BotPeopleStore.Current, subject, DateTime.UtcNow, raid))
                announced |= Act(bot, subject, e, false, mayAnnounce && !announced);
        }
        catch (Exception exception)
        {
            if (Log.IsWarnEnabled)
                Log.Warn($"Bots as people: milestones for {bot.Name} failed", exception);
        }
        return announced;
    }

    // ---- what the bot does about an event ------------------------------------------------------------------------------------

    /// <summary>A bot's wait before its next line about its arc (game loop time).</summary>
    private static readonly ConcurrentDictionary<BotPersonKey, long> NotBefore = new();
    /// <summary>A talk point a timer owns (a busy retry, or the line being typed): how often it has tried, and until when
    /// it owns the point, so the bot's next kills start no second line.</summary>
    private static readonly ConcurrentDictionary<BotPersonKey, (int Tries, long Until)> Pending = new();
    private static readonly long[] LastFactionLine = [long.MinValue / 2, long.MinValue / 2, long.MinValue / 2, long.MinValue / 2];
    private static readonly long[] LastLocalLine = [long.MinValue / 2, long.MinValue / 2, long.MinValue / 2, long.MinValue / 2];

    private static int RealmIndex(eRealm realm) => (int)realm is >= 0 and <= 3 ? (int)realm : 0;

    private static bool FactionGapOpen(eRealm realm, long now) =>
        BotMilestoneTalk.FactionGapOpen(now, Volatile.Read(ref LastFactionLine[RealmIndex(realm)]));

    private static bool LocalGapOpen(eRealm realm, long now) =>
        BotMilestoneTalk.LocalGapOpen(now, Volatile.Read(ref LastLocalLine[RealmIndex(realm)]));

    /// <summary>Takes the realm's turn in the channel (its /faction gap, or the gap of its group, /say and guild lines).</summary>
    private static bool Reserve(BotMilestoneChannel channel, eRealm realm, long now)
    {
        bool faction = channel == BotMilestoneChannel.Faction;
        long[] lastLines = faction ? LastFactionLine : LastLocalLine;
        int i = RealmIndex(realm);
        long last = Volatile.Read(ref lastLines[i]);
        bool open = faction ? BotMilestoneTalk.FactionGapOpen(now, last) : BotMilestoneTalk.LocalGapOpen(now, last);
        return open && Interlocked.CompareExchange(ref lastLines[i], now, last) == last;
    }

    private static string Describe(in BotMilestoneSubject s) =>
        $"bot=\"{s.Name}\" id={s.CharacterId} person={s.Person} realm={GlobalConstants.RealmToName(s.Realm)} level={s.Level} " +
        $"class=\"{s.ClassName}\" xp={s.Experience} rr={s.RealmLevel / 10 + 1}L{s.RealmLevel % 10} rp={s.RealmPoints}";

    /// <summary>The numbers a line's slots were filled from, for the log (the played check compares them with the line).</summary>
    private static string Numbers(in BotMilestoneSubject s, OfflineBotMilestoneRecord row) =>
        $"bubbles={BotMilestones.BubblesLeft(s.Level, s.Experience)} left_k={BotMilestones.RankThousandsLeft(s.RealmPoints)} count={row?.Count ?? 0}";

    private static bool Act(GameBot bot, BotMilestoneSubject s, BotMilestoneEvent e, bool forced, bool mayAnnounce = true)
    {
        string arc = BotMilestones.Id(e.Arc);
        switch (e.Step)
        {
            case BotMilestoneStep.Chosen:
                Count("chosen");
                if (TakeLine())
                    Log.Info($"{ArcTag} {Describe(s)} arc={arc} state=active progress={BotMilestones.Percent(e.Progress)}");
                break;
            case BotMilestoneStep.Dropped:
                Count("dropped");
                if (TakeLine())
                    Log.Info($"{ArcTag} {Describe(s)} arc={arc} state=dropped reason=no_longer_possible");
                break;
            case BotMilestoneStep.Reached:
                Count("reached." + arc);
                if (e.Arc == BotMilestoneArc.DragonTrophy)
                    Count("trophies");
                if (e.Unsought)
                    Count("unsought");
                if (!e.Unsought)
                {
                    // The moment is the bot's line about its arc for now (the ding to 50 has its own ding line): the next
                    // arc's talk point keeps its 15 minutes, across a restart too.
                    NotBefore[s.Person] = GameLoop.GameLoopTime + BotMilestoneTalk.OwnGapMs;
                    Pending.TryRemove(s.Person, out _);
                    StampTalk(BotPeopleStore.Current, s.Person, s.CharacterId, DateTime.UtcNow);
                }
                // A trophy won while chasing something else is recorded, never told as "finally": it was not its goal.
                string announce = e.Unsought ? "unsought" : BotMilestones.ReachedTopic(e.Arc, e.Row?.Count ?? 0) == null ? "own_ding"
                    : !mayAnnounce ? "folded" : Announce(bot, s, e) ? "yes" : "unheard";
                if (announce == "folded")
                    Count("folded");
                if (Log.IsInfoEnabled)
                    Log.Info($"{ReachedTag} {Describe(s)} arc={arc} trophy=\"{e.Row?.Trophy}\" source=\"{e.Row?.Source}\" " +
                             $"dry_kills={e.Row?.Count ?? 0} sought={(e.Unsought ? "false" : "true")} announce={announce} started=\"{e.Row?.StartedUtc}\"");
                return announce == "yes";
            case BotMilestoneStep.Point:
                TalkAtPoint(bot, s, e, forced, 0, false);
                break;
        }
        return false;
    }

    /// <summary>Notes on the character's active arc that it has just spoken about its milestones (saved, so a restart keeps
    /// the 15 minutes).</summary>
    public static void StampTalk(BotPeopleStore store, BotPersonKey person, long characterId, DateTime utc)
    {
        OfflineBotMilestoneRecord row = ActiveRow(store, person, characterId);
        if (row != null)
            store.Milestones.Change((person, characterId, row.Arc ?? string.Empty), () => row, r => r.LastTalkUtc = Now(utc));
    }

    private static bool GuildListens(GameBot bot) => bot.Guild != null && AutonomousBotGuildSocial.HasListener(bot.Guild);

    private static BotMilestoneChannel ChannelFor(GameBot bot, bool moment, long now, bool gaps = true) => BotMilestoneTalk.Pick(moment,
        AutonomousLivingServer.GroupHasPlayer(bot.Group), AutonomousLivingServer.PlayerHears(bot), GuildListens(bot),
        AutonomousBotChatCoordinator.HasFactionAudience(bot.Realm), !gaps || FactionGapOpen(bot.Realm, now),
        !gaps || LocalGapOpen(bot.Realm, now));

    private static Dictionary<string, string> Slots(in BotMilestoneSubject s, BotMilestoneEvent e)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["level"] = s.Level.ToString(CultureInfo.InvariantCulture) };
        switch (e.Arc)
        {
            case BotMilestoneArc.Ding50:
                slots["bubbles"] = BotMilestones.BubblesLeft(s.Level, s.Experience).ToString(CultureInfo.InvariantCulture);
                break;
            case BotMilestoneArc.RealmRank5:
                slots["count"] = BotMilestones.RankThousandsLeft(s.RealmPoints).ToString(CultureInfo.InvariantCulture);
                break;
            case BotMilestoneArc.DragonTrophy:
                slots["count"] = (e.Row?.Count ?? 0).ToString(CultureInfo.InvariantCulture);
                if (!string.IsNullOrEmpty(e.Row?.Source))
                    slots["boss"] = e.Row.Source;
                if (!string.IsNullOrEmpty(e.Row?.Trophy))
                    slots["item"] = e.Row.Trophy;
                break;
        }
        return slots;
    }

    /// <summary>Why a bot cannot type now ("in_combat", "casting", "dead"), or null when it can (AutonomousLivingServer.CanTalk).</summary>
    private static string BusyReason(GameBot bot) =>
        !bot.IsAlive ? "dead" : bot.InCombat ? "in_combat" : bot.IsCasting ? "casting" : AutonomousLivingServer.CanTalk(bot) ? null : "away";

    private static string PointText(in BotMilestoneSubject s, BotMilestoneEvent e) =>
        $"{ProgressTag} {Describe(s)} arc={BotMilestones.Id(e.Arc)} point={e.Point} progress={BotMilestones.Percent(e.Progress)} count={e.Row.Count}";

    /// <summary>
    /// A talk point the bot's numbers have passed. Decided once (a fixed roll: say it, or pass it in silence). To say it the
    /// bot needs someone to hear, the channel's turn, and its hands free: the decision usually comes inside a kill (its
    /// experience or realm points), when it is still in combat, so a busy bot tries again on a timer after 15-30 s, at most
    /// 8 times; with nobody to hear, or the channel's gap closed, it waits 10 minutes.
    /// </summary>
    private static void TalkAtPoint(GameBot bot, BotMilestoneSubject s, BotMilestoneEvent e, bool forced, int tries, bool fromTimer)
    {
        long now = GameLoop.GameLoopTime;
        BotPersonKey person = s.Person;
        if (!fromTimer && Pending.TryGetValue(person, out var owned) && now < owned.Until)
            return; // its timer has this point
        if (!forced && !fromTimer)
        {
            if (!BotMilestoneTalk.Due(e.Point, e.Row.TalkPoint, now, NotBefore.GetValueOrDefault(person, long.MinValue)))
                return;
            if (!BotMilestoneTalk.WillTalk(s.CharacterId, e.Arc, e.Point, s.Personality?.Traits.Chattiness ?? 4))
            {
                MarkPoint(BotPeopleStore.Current, e.Row, e.Point, null);
                Count("points");
                Count("silent");
                if (TakeLine())
                    Log.Info($"{PointText(s, e)} talk=no");
                return;
            }
            if (DateTime.TryParse(e.Row.LastTalkUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out DateTime last) &&
                (DateTime.UtcNow - last).TotalMilliseconds is >= 0 and var since && since < BotMilestoneTalk.OwnGapMs)
            {
                NotBefore[person] = now + BotMilestoneTalk.OwnGapMs - (long)since;
                Count("waiting");
                return;
            }
        }
        BotMilestoneChannel channel = AutonomousBotVoices.Enabled && AutonomousLivingServer.IsSpeaker(bot)
            ? ChannelFor(bot, false, now) : BotMilestoneChannel.None;
        if (channel == BotMilestoneChannel.None)
        {
            Pending.TryRemove(person, out _);
            NotBefore[person] = now + BotMilestoneTalk.RetryMs;
            Count("waiting");
            if (TakeLine())
                Log.Info($"{PointText(s, e)} talk=later reason=" + (!AutonomousBotVoices.Enabled || !AutonomousLivingServer.IsSpeaker(bot) ||
                    ChannelFor(bot, false, now, gaps: false) == BotMilestoneChannel.None ? "nobody_hears" : "channel_gap"));
            return;
        }
        if (BusyReason(bot) is { } busy)
        {
            RetryWhenFree(bot, s, e, tries, busy);
            return;
        }
        if (tries == 0 && TakeLine())
            Log.Info($"{PointText(s, e)} talk=yes channel={BotMilestoneTalk.ChannelName(channel)}");
        int typing = BotMilestoneTalk.TypingDelayMs(Random.Shared);
        Pending[person] = (tries, now + typing + 10_000);
        string arc = BotMilestones.Id(e.Arc);
        string topic = BotMilestones.Topic(e.Arc, e.Point, BotMilestones.BubblesLeft(s.Level, s.Experience));
        Dictionary<string, string> slots = Slots(s, e);
        OfflineBotMilestoneRecord row = e.Row;
        int point = e.Point;
        AutonomousLivingServer.Later(bot, typing, null, () =>
        {
            long at = GameLoop.GameLoopTime;
            if (BusyReason(bot) is { } nowBusy)
            {
                RetryWhenFree(bot, s, e, tries, nowBusy); // a fight began while it typed
                return;
            }
            Pending.TryRemove(person, out _);
            if (!Reserve(channel, bot.Realm, at) || !AutonomousBotVoices.TryEventLine(bot, "milestone", out string line, topic, slots) ||
                !Say(bot, channel, line))
            {
                NotBefore[person] = at + BotMilestoneTalk.RetryMs;
                Count("held");
                return;
            }
            MarkPoint(BotPeopleStore.Current, row, point, DateTime.UtcNow);
            NotBefore[person] = at + BotMilestoneTalk.OwnGapMs;
            Count("points");
            Count("talk." + BotMilestoneTalk.ChannelName(channel));
            if (Log.IsInfoEnabled)
                Log.Info($"{TalkTag} {Describe(s)} arc={arc} point={point} topic={topic} channel={BotMilestoneTalk.ChannelName(channel)} " +
                         $"{Numbers(s, row)} tries={tries} line=\"{line.Replace("\"", "'")}\"");
        });
    }

    /// <summary>The bot is fighting, casting or dead: its timer tries the point again in 15-30 s, or after 8 tries it waits 10 minutes.</summary>
    private static void RetryWhenFree(GameBot bot, BotMilestoneSubject s, BotMilestoneEvent e, int tries, string reason)
    {
        long now = GameLoop.GameLoopTime;
        if (!BotMilestoneTalk.RetriesWhenBusy(tries))
        {
            Pending.TryRemove(s.Person, out _);
            NotBefore[s.Person] = now + BotMilestoneTalk.RetryMs;
            Count("waiting");
            if (TakeLine())
                Log.Info($"{PointText(s, e)} talk=later reason={reason} tries={tries}");
            return;
        }
        int delay = BotMilestoneTalk.BusyRetryMs(Random.Shared);
        Pending[s.Person] = (tries + 1, now + delay + 10_000);
        Count("busy");
        if (tries == 0 && TakeLine())
            Log.Info($"{PointText(s, e)} talk=later reason={reason} retry_s={delay / 1000}");
        AutonomousLivingServer.Later(bot, delay, null, () => RetryPoint(bot, tries + 1));
    }

    /// <summary>The timer's try: the bot's numbers are read again (the point may be handled, or a later one passed).</summary>
    private static void RetryPoint(GameBot bot, int tries)
    {
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone || BotPeopleScope.Check(bot, BotPeopleRule.Record, On, out _) != BotPeopleRefusal.None)
        {
            Pending.TryRemove(person, out _);
            return;
        }
        using var aiProfile = AiProfile.Scope(AiSystem.Milestones); // AI_PROFILE: measures only
        try
        {
            BotMilestoneSubject subject = SubjectOf(bot);
            BotMilestoneEvent point = default;
            foreach (BotMilestoneEvent e in Advance(BotPeopleStore.Current, subject, DateTime.UtcNow))
            {
                if (e.Step == BotMilestoneStep.Point)
                    point = e;
                else
                    Act(bot, subject, e, false);
            }
            if (point.Row == null)
                Pending.TryRemove(person, out _);
            else
                TalkAtPoint(bot, subject, point, false, tries, true);
        }
        catch (Exception exception)
        {
            Pending.TryRemove(person, out _);
            if (Log.IsWarnEnabled)
                Log.Warn($"Bots as people: milestone retry for {bot.Name} failed", exception);
        }
    }

    private static bool Say(GameBot bot, BotMilestoneChannel channel, string line)
    {
        switch (channel)
        {
            case BotMilestoneChannel.Faction:
                if (!AutonomousBotChatCoordinator.HasFactionAudience(bot.Realm) || AutonomousBotDebates.IsRunning(bot.Realm) ||
                    !AutonomousBotChatCoordinator.BroadcastFaction(bot.Name, bot.Realm, line))
                    return false;
                AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(bot.Realm), bot, line, "none");
                return true;
            case BotMilestoneChannel.Group:
                return AutonomousLivingServer.GroupHasPlayer(bot.Group) && AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Group, line);
            case BotMilestoneChannel.Guild:
                return GuildListens(bot) && AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Guild, line);
            case BotMilestoneChannel.Say:
                return AutonomousLivingServer.PlayerHears(bot) && AutonomousLivingServer.Speak(bot, AutonomousLivingServer.Channel.Say, line);
            default:
                return false;
        }
    }

    /// <summary>
    /// The moment itself. The ding to 50 already has its ding line, its /faction story and the grats chain with 1-3 realm-mates
    /// in /faction (AutonomousLivingServer.Grats): nothing is added. Realm rank 5 and a dragon trophy: the bot says so (in
    /// /faction first), and in /faction the realm cheers, a burst of 1-3 reactions (AutonomousChatBursts, under the ceiling).
    /// One moment a kill is told (the other winners' are folded into it: the log and the Herald have them), and each takes
    /// its channel's turn like a talk point, so moments never come seconds apart. True when a line was scheduled.
    /// </summary>
    private static bool Announce(GameBot bot, BotMilestoneSubject s, BotMilestoneEvent e)
    {
        string topic = BotMilestones.ReachedTopic(e.Arc, e.Row?.Count ?? 0);
        if (topic == null || !AutonomousBotVoices.Enabled || !AutonomousLivingServer.IsSpeaker(bot))
            return false;
        long now = GameLoop.GameLoopTime;
        BotMilestoneChannel channel = ChannelFor(bot, true, now);
        if (channel == BotMilestoneChannel.None || !Reserve(channel, bot.Realm, now))
            return false;
        Dictionary<string, string> slots = Slots(s, e);
        string arc = BotMilestones.Id(e.Arc);
        AutonomousLivingServer.Later(bot, BotMilestoneTalk.TypingDelayMs(Random.Shared), () => AutonomousLivingServer.IsSpeaker(bot) && bot.IsAlive, () =>
        {
            if (!AutonomousBotVoices.TryEventLine(bot, "milestone", out string line, topic, slots) || !Say(bot, channel, line))
            {
                Count("held");
                return;
            }
            NotBefore[s.Person] = GameLoop.GameLoopTime + BotMilestoneTalk.OwnGapMs;
            StampTalk(BotPeopleStore.Current, s.Person, s.CharacterId, DateTime.UtcNow);
            Count("talk." + BotMilestoneTalk.ChannelName(channel));
            if (Log.IsInfoEnabled)
                Log.Info($"{TalkTag} {Describe(s)} arc={arc} point=100 topic={topic} channel={BotMilestoneTalk.ChannelName(channel)} " +
                         $"{Numbers(s, e.Row)} trophy=\"{e.Row?.Trophy}\" source=\"{e.Row?.Source}\" line=\"{line.Replace("\"", "'")}\"");
            if (channel == BotMilestoneChannel.Faction)
            {
                var cheer = new Dictionary<string, string>(slots, StringComparer.Ordinal) { ["player"] = bot.Name, ["target"] = bot.Name };
                AutonomousChatBursts.React(bot, bot.Realm, "milestone", line, BotMilestones.ReactionAnswers(e.Arc), cheer);
            }
        });
        return true;
    }

    // ---- the dragon trophy: a high-end raid kill and its loot --------------------------------------------------------------

    internal sealed class RaidKillContext
    {
        public string Boss;
        public GameBot[] Members;
        public Dictionary<GameBot, string> Won;
    }

    [ThreadStatic] private static RaidKillContext _raidKill;

    /// <summary>The kill's scope: disposing it counts the kill for each raider and reaches the trophies won meanwhile.</summary>
    public readonly struct RaidLootScope : IDisposable
    {
        private readonly RaidKillContext _context;
        internal RaidLootScope(RaidKillContext context) => _context = context;

        /// <summary>Whether this kill counts (a real boss with raiders at it, the switch on).</summary>
        public bool IsOpen => _context != null;

        /// <summary>The item a raider has received from this kill's loot so far, or null.</summary>
        public string WonBy(GameBot bot) => _context != null && bot != null ? _context.Won.GetValueOrDefault(bot) : null;

        public void Dispose()
        {
            if (_context == null)
                return;
            if (ReferenceEquals(_raidKill, _context))
                _raidKill = null;
            Count("raidKills");
            // The winners first, so the one moment a kill is told is a winner's; then everyone who won nothing.
            bool announced = false;
            foreach (GameBot member in _context.Members)
                if (_context.Won.TryGetValue(member, out string item))
                    announced |= Consider(member, new BotMilestoneRaidKill(_context.Boss, item), !announced);
            foreach (GameBot member in _context.Members)
                if (!_context.Won.ContainsKey(member))
                    Consider(member, new BotMilestoneRaidKill(_context.Boss, null));
        }
    }

    /// <summary>
    /// Whether a victim is a big kill for the trophy: a dragon, an epic boss or a raid's final boss, never a boss's add
    /// (BotMilestones.IsBigKill). Every high-end raid encounter passes AutonomousRealmRaid.IsHighEndRaidEncounter, and that
    /// is true of any epic NPC: Xanxicar's champions die by the dozen in one fight, and each would count as a kill without
    /// a drop, name itself in the bot's lines and make whatever it dropped the trophy. Reads only.
    /// </summary>
    public static bool IsBigKill(GameNPC victim) => victim != null &&
        BotMilestones.IsBigKill(victim is GameEpicNPC, AutonomousRealmRaid.RaidIdForBoss(victim.GetType().Name) != null);

    /// <summary>
    /// Opened by AbstractServerRules.OnNpcKilled around a high-end raid encounter's loot with the expedition's roster near
    /// the kill (RealmRaidLootOwner's members). Only a real boss opens it (<see cref="IsBigKill(GameNPC)"/>): an add's
    /// death and an add's drop are nothing here. Reads raid state only; changes no loot rule. A no-op when the switch is
    /// off or there is no raid.
    /// </summary>
    public static RaidLootScope RaidLoot(GameNPC boss, IReadOnlyList<GameBot> members)
    {
        if (boss == null || members == null || members.Count == 0 || !On || !IsBigKill(boss))
            return default;
        var context = new RaidKillContext
        {
            Boss = boss.Name, Members = members.Where(m => m != null).Distinct().ToArray(), Won = new Dictionary<GameBot, string>(),
        };
        _raidKill = context;
        return new RaidLootScope(context);
    }

    /// <summary>
    /// A gamebot really received an item (GameBot.TryAutoPickUpItem, after it is in its backpack). Inside a big raid
    /// kill's loot it is that raider's trophy; everywhere else nothing happens (one thread-static read).
    /// </summary>
    public static void OnItemReceived(GameBot bot, DbInventoryItem item)
    {
        RaidKillContext context = _raidKill;
        if (context == null || bot == null || item == null || context.Won.ContainsKey(bot) || Array.IndexOf(context.Members, bot) < 0)
            return;
        // The server's own item rules (none in a bare test world: then every item counts, as before).
        DOL.GS.ServerRules.IServerRules rules = RulesOrNull();
        if (!CountsAsTrophy(item, rules == null ? null : template => rules.CheckAbilityToUseItem(bot, template)))
            return;
        context.Won[bot] = item.Name;
    }

    private static DOL.GS.ServerRules.IServerRules RulesOrNull()
    {
        try
        {
            return GameServer.Instance?.Configuration == null ? null : GameServer.ServerRules;
        }
        catch (NullReferenceException)
        {
            return null;   // a server with no rules set up (a bare test world)
        }
    }

    /// <summary>
    /// Whether a received item can be a raider's trophy: one its class can use (PC log 5378dbe, 01:58:50: an Infiltrator
    /// cheered "worth the wait" over a generated cloth vest it would never wear). Pure for a given check.
    /// </summary>
    public static bool CountsAsTrophy(DbInventoryItem item, Func<DbItemTemplate, bool> canUse) =>
        item?.Template != null && !string.IsNullOrEmpty(item.Name) && (canUse == null || canUse(item.Template));

    // ---- /lstest milestone and life cycle --------------------------------------------------------------------------------------

    /// <summary>
    /// /lstest milestone: the target's arc comes to its next talk point now if its numbers have passed one: its wait and the
    /// chance roll are skipped (never the truth: a point its numbers have not reached is not said).
    /// </summary>
    private static string TestMilestone(GamePlayer gm, GameBot target, string[] words)
    {
        if (target == null)
            return "/lstest milestone: target an autonomous gamebot first.";
        if (!On)
            return "/lstest milestone: bot_living_milestones is off.";
        BotPeopleRefusal refusal = BotPeopleScope.Refusal(target, BotPeopleRule.Record);
        if (refusal != BotPeopleRefusal.None)
            return $"/lstest milestone: {target.Name} has no milestones ({BotPeopleScope.RefusalName(refusal)}).";
        BotMilestoneSubject s = SubjectOf(target);
        List<BotMilestoneEvent> events = Advance(BotPeopleStore.Current, s, DateTime.UtcNow);
        foreach (BotMilestoneEvent e in events.Where(e => e.Step != BotMilestoneStep.Point))
            Act(target, s, e, false);
        BotMilestoneEvent point = events.FirstOrDefault(e => e.Step == BotMilestoneStep.Point);
        OfflineBotMilestoneRecord row = ActiveRow(BotPeopleStore.Current, s.Person, s.CharacterId);
        if (row == null)
            return $"{target.Name} has no arc (level {s.Level}; arcs start at level 45, and at 50 only with an RvR or raid leaning).";
        BotMilestoneArc arc = BotMilestones.Parse(row.Arc);
        double progress = BotMilestones.Progress(arc, s.Level, s.Experience, s.RealmPoints);
        if (point.Row == null)
            return $"{target.Name}: arc {row.Arc}, {BotMilestones.Percent(progress)}% (raid kills without a drop {row.Count}), talk point {row.TalkPoint} " +
                   "already handled; the next one is not reached yet.";
        NotBefore.TryRemove(s.Person, out _);
        Pending.TryRemove(s.Person, out _);
        Act(target, s, point, true);
        return $"{target.Name}: arc {row.Arc} at point {point.Point} ({BotMilestones.Percent(progress)}%): talks now if someone can hear (see {TalkTag}).";
    }

    private static int _wired;

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args) => Wire();

    /// <summary>The summary, /lstest milestone and the logout clean-up (once; tests call it too).</summary>
    public static void Wire()
    {
        if (Interlocked.Exchange(ref _wired, 1) != 0)
            return;
        BotPeopleStore.LoggedOut += (person, bot) =>
        {
            NotBefore.TryRemove(person, out _);
            Pending.TryRemove(person, out _);
        };
        BotPeopleSummary.Register(SummaryTag, TakeSummary);
        BotPeopleTest.Register("milestone", TestMilestone);
    }

    /// <summary>Forgets the waits, the counts and the realm gaps (tests).</summary>
    public static void ResetForTests()
    {
        NotBefore.Clear();
        Pending.Clear();
        Counts.Clear();
        for (int i = 0; i < LastLocalLine.Length; i++)
            LastLocalLine[i] = long.MinValue / 2;
        for (int i = 0; i < LastFactionLine.Length; i++)
            LastFactionLine[i] = long.MinValue / 2;
    }
}
