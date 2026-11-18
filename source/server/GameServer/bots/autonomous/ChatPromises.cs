using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.Keeps;
using DOL.Logging;

namespace DOL.GS;

/// <summary>What a bot would be promising to come for when it says "omw", "coming" or "ill be there".</summary>
public enum ChatPromiseReason
{
    /// <summary>An alarm or a call for defenders ("inc 8 mids at bled", "they took our relic").</summary>
    Alarm,
    /// <summary>A player's call for help ("help, this mob is killing me", a corpse run).</summary>
    Help,
    /// <summary>A request for heals ("need heals!!") to a bot that can heal.</summary>
    Heal,
    /// <summary>A request for a resurrection to a bot that can rez.</summary>
    Rez,
    /// <summary>"free rps at mmg": realm points to be had somewhere.</summary>
    RealmPoints,
    /// <summary>"relic raid at 9, meet at the portal keep": a raid or meeting at a time.</summary>
    RaidTime,
}

/// <summary>How a kept promise moves the bot.</summary>
public enum ChatPromiseMode : byte
{
    /// <summary>An RvR gamebot's frontier turn takes the promised keep or zone as its destination (the RvR travel, porters,
    /// keep approach and, at its own realm's keep, the defensive post: AutonomousWorldBotController.ChatPromise).</summary>
    RvrObjective,
    /// <summary>A free gamebot walks to the place or the player in the same region (help, a battleground keep).</summary>
    Walk,
    /// <summary>What the bot already does takes it there: a rezzer near a dead player (BotBrain's stranger rez), a healer in
    /// the player's own group (it follows and heals its group).</summary>
    Native,
    /// <summary>The bot already holds the duty that takes it there (a raid reservation, the RvR force defending that keep).</summary>
    Lease,
}

public enum ChatPromiseTargetKind : byte { Keep, Zone, Living, Raid }

/// <summary>Where a promise goes. <paramref name="Anchor"/> is a keep id for keeps and zones (a zone's own-realm keep), else -1.</summary>
public sealed record ChatPromiseTarget(ChatPromiseTargetKind Kind, string Name, ushort RegionId, int X, int Y, int Z,
    int Anchor = -1, eRealm Holder = eRealm.None, ushort ZoneId = 0, bool Frontier = false, bool Battleground = false,
    byte MinLevel = 0, byte MaxLevel = 0, GameLiving Living = null)
{
    /// <summary>The RvR destination id the frontier turn uses ("rvr-keep-12", "rvr-roam-12").</summary>
    public string RvrId => Kind == ChatPromiseTargetKind.Keep ? $"rvr-keep-{Anchor}" : $"rvr-roam-{Anchor}";

    /// <summary>How close counts as there.</summary>
    public int ArrivalRadius => Kind switch
    {
        ChatPromiseTargetKind.Keep => ChatPromiseRules.KeepArrivalRadius,
        ChatPromiseTargetKind.Zone => ChatPromiseRules.ZoneArrivalRadius,
        _ => ChatPromiseRules.LivingArrivalRadius,
    };

    public override string ToString() => Name;
}

/// <summary>One promise a bot made: open until it arrives (kept) or the deadline passes (broken), then held for a while.</summary>
public sealed class ChatPromise
{
    public long Id { get; init; }
    public string Bot { get; init; }
    public long BotId { get; init; }
    public eRealm Realm { get; init; }
    public ChatPromiseReason Reason { get; init; }
    public ChatPromiseMode Mode { get; init; }
    public ChatPromiseTarget Target { get; init; }
    public string Asker { get; init; }
    public long Made { get; init; }
    public long Deadline { get; init; }
    public int EtaSeconds { get; init; }
    /// <summary>The lease the bot held when it promised: a later, different one took it (broken "taken").</summary>
    public eDutyLeaseKind LeaseAtMake { get; init; }
    public string LeaseOwnerAtMake { get; init; }
    public long ArrivedAt { get; internal set; }
    public long HoldUntil { get; internal set; }
    public string Ended { get; internal set; }

    /// <summary>Promised and not yet there.</summary>
    public bool Open => Ended == null && ArrivedAt == 0;

    /// <summary>There, and staying a while (a defender mans the keep, a helper fights beside the player).</summary>
    public bool Holding(long now) => Ended == null && ArrivedAt > 0 && now < HoldUntil;

    /// <summary>Open or holding: the duty still owns the bot's walk.</summary>
    public bool Active(long now) => Open || Holding(now);

    /// <summary>"omw, about 2 min": whole minutes to there, at least 1.</summary>
    public int EtaMinutes => Math.Max(1, (EtaSeconds + 30) / 60);
}

/// <summary>
/// The pure rules of a chat promise (owner, 2026-11-18 09:14: "Make it real for 1.2"): whether a bot can and will go now, how,
/// how long it has, and when it counts as there. No world state is read here (UT_ChatPromises).
/// </summary>
public static class ChatPromiseRules
{
    /// <summary>Normal run speed, units a second: the travel estimate.</summary>
    public const double RunUnitsPerSecond = 190;
    /// <summary>A porter hop from the border keep or the capital to another frontier, with the walk to the porter.</summary>
    public const int PorterSeconds = 180;
    /// <summary>A bot further than this from the place does not promise ("cant get there in time").</summary>
    public const int MaxEtaSeconds = 8 * 60;
    public const int MinDeadlineSeconds = 3 * 60;
    public const int MaxDeadlineSeconds = 15 * 60;
    /// <summary>How long a bot stays once there: a defender mans the keep, a helper stays by the player.</summary>
    public const int HoldSeconds = 3 * 60;
    public const int KeepArrivalRadius = 2500;
    public const int ZoneArrivalRadius = 3000;
    public const int LivingArrivalRadius = 700;
    /// <summary>A stranger in trouble is helped only by a bot this near (a call for help is no cross-zone errand).</summary>
    public const int HelpReach = 4000;
    /// <summary>A healer of the player's group answers "need heals" when it is this near.</summary>
    public const int HealReach = 3000;
    /// <summary>At most this many bots promise one place at a time.</summary>
    public const int MaxPerTarget = 6;
    /// <summary>A timid bot (risk trait below this, of 0-9) does not run to a fight; a shy one (sociability) to a stranger.</summary>
    public const int MinRiskForFights = 2;
    public const int MinSociabilityForHelp = 2;

    /// <summary>What the gate knows about the bot and the place, read from the world by <see cref="ChatPromises"/>.</summary>
    public readonly record struct Candidate(
        bool Gamebot, bool Alive, bool Active, bool InCombat, bool CarryingRelic, bool OnStableRide,
        bool InBotGroup, bool InAskersGroup, bool SpokenForByPlayer, eDutyLeaseKind Lease, bool LeaseGoesThere,
        bool PromisedElsewhere, int PromisedThere, bool RvrAssigned, int Level, bool CanHeal, bool CanRezAsker,
        bool PersonalityApplies, int Risk, int Sociability, bool SameRealm, bool BoundaryAllows,
        bool TargetFound, ChatPromiseTargetKind TargetKind, bool Frontier, bool Battleground, byte MinLevel, byte MaxLevel,
        bool SameRegion, double Distance, bool PorterReach);

    /// <summary>Seconds to there: the walk in the same region, or a porter hop and the walk on the far side.</summary>
    public static int Eta(bool sameRegion, double distance, bool porterReach) =>
        sameRegion ? (int)Math.Ceiling(Math.Max(0, distance) / RunUnitsPerSecond)
        : porterReach ? PorterSeconds + (int)Math.Ceiling(Math.Max(0, distance) / RunUnitsPerSecond) : int.MaxValue;

    /// <summary>The deadline: half again the estimate and two minutes, between 3 and 15 minutes.</summary>
    public static int DeadlineSeconds(int eta) =>
        (int)Math.Clamp(eta * 1.5 + 120, MinDeadlineSeconds, MaxDeadlineSeconds);

    /// <summary>
    /// Null when the bot can and will go now (with the way it goes and its estimate), else the refusal code (logged and
    /// shown by the played check). The order is the order a player would see the reason in.
    /// </summary>
    public static string Refusal(in Candidate c, ChatPromiseReason reason, out ChatPromiseMode mode, out int eta)
    {
        mode = ChatPromiseMode.Walk;
        eta = 0;
        if (!c.Gamebot)
            return "not_a_gamebot";
        if (!c.Alive)
            return "dead";
        if (!c.Active)
            return "offline";
        if (!c.TargetFound)
            return reason is ChatPromiseReason.Help or ChatPromiseReason.Heal or ChatPromiseReason.Rez ? "no_asker" : "no_place";
        if (!c.SameRealm)
            return "other_realm";
        if (c.InCombat)
            return "in_combat";
        if (c.CarryingRelic)
            return "carrying_relic";
        if (c.OnStableRide)
            return "on_a_ride";
        if (c.PromisedElsewhere)
            return "already_promised";
        if (c.PromisedThere >= MaxPerTarget)
            return "enough_going";
        switch (reason)
        {
            case ChatPromiseReason.Rez:
                if (!c.CanRezAsker)
                    return "cannot_rez";
                mode = ChatPromiseMode.Native;
                eta = Eta(true, c.Distance, false);
                return null;
            case ChatPromiseReason.Heal:
                if (!c.CanHeal)
                    return "not_a_healer";
                if (!c.InAskersGroup)
                    return "not_in_group";
                if (!c.SameRegion || c.Distance > HealReach)
                    return "too_far";
                mode = ChatPromiseMode.Native;
                eta = Eta(true, c.Distance, false);
                return null;
            case ChatPromiseReason.RaidTime:
                if (c.Lease != eDutyLeaseKind.RealmRaid)
                    return "no_raid_duty";
                mode = ChatPromiseMode.Lease;
                eta = MaxDeadlineSeconds * 2 / 3;
                return null;
            case ChatPromiseReason.Help:
                if (!c.SameRegion || c.Distance > HelpReach)
                    return "too_far";
                if (c.InAskersGroup)
                {
                    mode = ChatPromiseMode.Native;
                    eta = Eta(true, c.Distance, false);
                    return null;
                }
                if (c.SpokenForByPlayer)
                    return DutyLeaseReason.InPlayersGroup;
                if (c.Lease != eDutyLeaseKind.None)
                    return DutyLeaseReason.HeldBy(c.Lease);
                if (c.InBotGroup)
                    return DutyLeaseReason.InBotGroup;
                if (c.PersonalityApplies && c.Sociability < MinSociabilityForHelp)
                    return "not_the_type";
                mode = ChatPromiseMode.Walk;
                eta = Eta(true, c.Distance, false);
                return null;
        }
        // An alarm, a keep call or free realm points: a fight somewhere in RvR.
        if (c.SpokenForByPlayer)
            return DutyLeaseReason.InPlayersGroup;
        if (c.Battleground)
        {
            if (c.Level < c.MinLevel || c.Level > c.MaxLevel)
                return "level";
            if (!c.SameRegion)
                return "too_far";
            if (c.Lease != eDutyLeaseKind.None)
                return DutyLeaseReason.HeldBy(c.Lease);
            if (c.InBotGroup)
                return DutyLeaseReason.InBotGroup;
            if (c.PersonalityApplies && c.Risk < MinRiskForFights)
                return "not_the_type";
            eta = Eta(true, c.Distance, false);
            if (eta > MaxEtaSeconds)
                return "too_far";
            mode = ChatPromiseMode.Walk;
            return null;
        }
        if (!c.Frontier)
            return "no_place";
        if (c.Lease == eDutyLeaseKind.RvrEvent && c.LeaseGoesThere)
        {
            // Its RvR force already defends (or attacks) that keep: "omw" is what it is doing.
            mode = ChatPromiseMode.Lease;
            eta = Math.Min(MaxEtaSeconds, Eta(c.SameRegion, c.Distance, c.PorterReach));
            return null;
        }
        if (c.Lease != eDutyLeaseKind.None)
            return DutyLeaseReason.HeldBy(c.Lease);
        if (c.InBotGroup)
            return DutyLeaseReason.InBotGroup;
        if (!c.RvrAssigned)
            return "not_on_rvr";
        if (c.Level < 50)
            return "level";
        if (!c.BoundaryAllows)
            return "realm_boundary";
        if (c.PersonalityApplies && c.Risk < MinRiskForFights)
            return "not_the_type";
        eta = Eta(c.SameRegion, c.Distance, c.PorterReach);
        if (eta > MaxEtaSeconds)
            return "too_far";
        mode = ChatPromiseMode.RvrObjective;
        return null;
    }

    /// <summary>
    /// The frontier turn's destination for an RvR promise: its own realm's keep is defended ("rvr-keep-N", the defence intent:
    /// a verified defensive post); anywhere else is a patrol near the keep ("rvr-roam-N"), never a one-bot assault. Pure.
    /// </summary>
    public static string RvrDestinationId(ChatPromiseTarget target, eRealm realm, out bool defend)
    {
        defend = target.Kind == ChatPromiseTargetKind.Keep && target.Holder == realm;
        return defend ? target.RvrId : $"rvr-roam-{target.Anchor}";
    }

    /// <summary>
    /// A reply that promises to come: "omw", "on my way", "otw", "coming", "porting", "heading there", "ill be there",
    /// "be right there", "count me in", "on it". Pure; the lines the gate guards (WRITING-GUIDE "Never").
    /// </summary>
    public static bool SaysComing(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return false;
        string t = " " + new string(line.ToLowerInvariant().Replace("'", string.Empty)
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray()) + " ";
        foreach (string word in ComingWords)
            if (t.Contains(word, StringComparison.Ordinal))
                return true;
        // "anyone porting out?", "is anyone coming?": asking others is no promise.
        if (line.TrimEnd().EndsWith('?') && (t.Contains(" anyone ") || t.Contains(" any1 ") || t.Contains(" anybody ") || t.Contains(" who ")))
            return false;
        foreach (string word in WeakComingWords)
            if (t.Contains(word, StringComparison.Ordinal))
                return true;
        return false;
    }

    private static readonly string[] ComingWords =
    [
        " omw ", " otw ", " on my way ", " ill be there ", " i will be there ", " be right there ", " count me in ",
        " heading there ", " heading over ", " running over ", " on the way ", " im coming ", " coming now ",
    ];

    private static readonly string[] WeakComingWords = [" coming ", " porting ", " on it ", " be there "];

    /// <summary>
    /// Whether <paramref name="text"/> names the keep <paramref name="keepName"/>: the whole name, the name without
    /// "Caer/Dun/Fort/Castle" and "Faste", or the first 4-5 letters of a longer one ("bled" for Bledmeer Faste), the forms
    /// AutonomousChatFieldTalk.ShortKeep writes. Pure.
    /// </summary>
    public static bool NamesKeep(string text, string keepName)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(keepName))
            return false;
        string lower = " " + new string(text.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch == '\'' ? ch : ' ').ToArray()) + " ";
        string full = keepName.Trim().ToLowerInvariant();
        if (lower.Contains(" " + full + " ", StringComparison.Ordinal))
            return true;
        string k = full;
        foreach (string prefix in new[] { "caer ", "dun ", "fort ", "castle " })
            if (k.StartsWith(prefix, StringComparison.Ordinal))
            {
                k = k[prefix.Length..];
                break;
            }
        if (k.EndsWith(" faste", StringComparison.Ordinal))
            k = k[..^6];
        if (k.Length < 3)
            return false;
        if (lower.Contains(" " + k + " ", StringComparison.Ordinal))
            return true;
        if (k.Length <= 6)
            return false;
        foreach (string word in lower.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            if (word.Length is 4 or 5 && k.StartsWith(word, StringComparison.Ordinal))
                return true;
        return false;
    }
}

/// <summary>
/// The promises bots have made, by bot: one at a time each. Made, kept (arrived before the deadline) and broken (not there
/// in time, died, taken by a bigger duty, logged out, the place gone) are each logged once as a CHAT_PROMISE line, which
/// the played check reads (docs/PLAYED-CHECK-1.2.md, "A bot that says it is coming arrives"). Keyed by the bot object,
/// in memory only: no promise survives a restart.
/// </summary>
public sealed class ChatPromiseBook
{
    /// <summary>What the world says about an open or holding promise at a check.</summary>
    public readonly record struct Progress(bool Present, bool Alive, bool Arrived, string Gone, bool Taken);

    private readonly ConcurrentDictionary<object, ChatPromise> _byBot = new(ReferenceEqualityComparer.Instance);
    private readonly Action<string> _log;
    private long _nextId;

    public ChatPromiseBook(Action<string> log) => _log = log;

    public int Count => _byBot.Count;

    public ChatPromise Of(object bot) => bot != null && _byBot.TryGetValue(bot, out ChatPromise promise) ? promise : null;

    /// <summary>Bots with an open promise to this place now.</summary>
    public int GoingTo(ChatPromiseTarget target, long now) =>
        target == null ? 0 : _byBot.Values.Count(p => p.Active(now) && SamePlace(p.Target, target));

    public static bool SamePlace(ChatPromiseTarget a, ChatPromiseTarget b) =>
        a != null && b != null && a.Kind == b.Kind && a.RegionId == b.RegionId && (a.Kind == ChatPromiseTargetKind.Living
            ? ReferenceEquals(a.Living, b.Living) && a.Living != null || a.Living == null && b.Living == null && a.Name == b.Name
            : a.Anchor == b.Anchor && string.Equals(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether the bot's current promise is to somewhere else (it then can't promise this one).</summary>
    public bool PromisedElsewhere(object bot, ChatPromiseTarget target, long now) =>
        Of(bot) is { } p && p.Active(now) && !SamePlace(p.Target, target);

    /// <summary>
    /// Records a promise and logs "CHAT_PROMISE made". A bot already on its way to the same place keeps that promise (true,
    /// nothing new is logged: saying "omw" twice is still one trip).
    /// </summary>
    public ChatPromise Make(object bot, string name, long botId, eRealm realm, ChatPromiseReason reason, ChatPromiseMode mode,
        ChatPromiseTarget target, string asker, int etaSeconds, long now, eDutyLeaseKind lease = eDutyLeaseKind.None,
        string leaseOwner = null)
    {
        if (Of(bot) is { } existing && existing.Active(now))
        {
            if (SamePlace(existing.Target, target))
                return existing;
            return null;
        }
        int deadline = ChatPromiseRules.DeadlineSeconds(etaSeconds);
        var promise = new ChatPromise
        {
            Id = System.Threading.Interlocked.Increment(ref _nextId), Bot = name, BotId = botId, Realm = realm, Reason = reason,
            Mode = mode, Target = target, Asker = asker, Made = now, Deadline = now + deadline * 1000L, EtaSeconds = etaSeconds,
            LeaseAtMake = lease, LeaseOwnerAtMake = leaseOwner,
        };
        _byBot[bot] = promise;
        _log?.Invoke($"CHAT_PROMISE made bot=\"{name}\" id={botId} promise={promise.Id} realm={GlobalConstants.RealmToName(realm)} " +
                     $"reason={reason} mode={mode} target=\"{target.Name}\" kind={target.Kind} region={target.RegionId} " +
                     $"eta={etaSeconds}s deadline={deadline}s asker=\"{asker}\"");
        return promise;
    }

    /// <summary>
    /// Judges the bot's promise now: arrived before the deadline is kept (logged with how long it took; the bot then holds
    /// there for <see cref="ChatPromiseRules.HoldSeconds"/>), anything that ends it first is broken (logged with why). A held
    /// promise ends quietly. Returns "kept", "broken" or "done" when that happened at this check, else null.
    /// </summary>
    public string Check(object bot, long now, in Progress progress)
    {
        ChatPromise promise = Of(bot);
        if (promise == null || promise.Ended != null)
            return null;
        if (promise.Open)
        {
            string broken = !progress.Present ? "logged_out" : !progress.Alive ? "died" : null;
            if (broken == null && progress.Arrived)
            {
                promise.ArrivedAt = now;
                promise.HoldUntil = now + ChatPromiseRules.HoldSeconds * 1000L;
                _log?.Invoke($"CHAT_PROMISE kept bot=\"{promise.Bot}\" id={promise.BotId} promise={promise.Id} reason={promise.Reason} " +
                             $"mode={promise.Mode} target=\"{promise.Target.Name}\" after={(now - promise.Made) / 1000}s eta={promise.EtaSeconds}s");
                return "kept";
            }
            broken ??= progress.Taken ? "taken" : progress.Gone ?? (now >= promise.Deadline ? "timeout" : null);
            if (broken == null)
                return null;
            End(bot, promise);
            promise.Ended = broken;
            _log?.Invoke($"CHAT_PROMISE broken bot=\"{promise.Bot}\" id={promise.BotId} promise={promise.Id} reason={promise.Reason} " +
                         $"mode={promise.Mode} target=\"{promise.Target.Name}\" why={broken} after={(now - promise.Made) / 1000}s " +
                         $"deadline={(promise.Deadline - promise.Made) / 1000}s");
            return "broken";
        }
        if (now >= promise.HoldUntil || !progress.Present || !progress.Alive || progress.Taken || progress.Gone != null)
        {
            End(bot, promise);
            promise.Ended = "done";
            return "done";
        }
        return null;
    }

    /// <summary>Ends a bot's promise without a verdict (tests; the world cleared).</summary>
    public void Forget(object bot)
    {
        if (bot != null)
            _byBot.TryRemove(bot, out _);
    }

    public void Clear() => _byBot.Clear();

    /// <summary>The bots whose promise is in the book (a sweep for bots that stopped thinking).</summary>
    public object[] Bots() => _byBot.Keys.ToArray();

    private void End(object bot, ChatPromise promise) =>
        _byBot.TryRemove(new KeyValuePair<object, ChatPromise>(bot, promise));
}

/// <summary>
/// The one gate every chat promise to come goes through (owner, 2026-11-18 09:14: "Make it real for 1.2"). A reply that says
/// the bot is coming ("omw", "on my way", "coming", "ill be there", "count me in") is said only when
/// <see cref="TryPromiseCome(GameBot, string, ChatPromiseReason, string)"/> returns true, and returning true means the bot
/// has really taken a duty to go there; otherwise the reply is one that promises nothing ("how many?", "where are you?",
/// "might make it").
/// <list type="bullet">
/// <item>Who: autonomous gamebots only, alive, not fighting, not on a ride or carrying a relic, not already promised
/// elsewhere, not held by a raid, a siege force, a battleground rally or a bot group (unless that duty already goes there),
/// and not timid (the personality's risk trait) for a fight. RvR: an RvR gamebot of level 50 within 8 minutes of the keep
/// (a porter hop counts); a battleground: in that battleground and its bracket.</item>
/// <item>The duty: <see cref="ChatPromiseMode"/>. The frontier turn takes the keep as its destination and, at its own
/// realm's keep, holds a defensive post (AutonomousWorldBotController.ChatPromise); a helper walks to the player and fights
/// what is fighting them; a rezzer or the player's own healer is already on its way by its own rules.</item>
/// <item>The proof: CHAT_PROMISE made / kept / broken lines, judged by played_check_log.py.</item>
/// </list>
/// Callers call <see cref="TryPromiseCome(GameBot, string, ChatPromiseReason, string)"/> once, at the moment the line is
/// chosen, because a true answer commits the bot; <see cref="CanPromiseCome"/> asks without committing (a line filter).
/// </summary>
public static class ChatPromises
{
    private static readonly Logger Log = LoggerManager.Create(typeof(ChatPromises));
    private static readonly ConcurrentDictionary<(long, string), long> RecentRefusals = new();

    /// <summary>The live book. Its lines go to the server log.</summary>
    public static ChatPromiseBook Book { get; } = new(line =>
    {
        if (Log.IsInfoEnabled)
            Log.Info(line);
    });

    /// <summary>
    /// A replacement for the live decision (tests, and E1's stub seam): when set, it alone decides, and no duty is taken.
    /// Arguments: the bot, the place named in the line it answers (a place key or the raw words, may be null), why it would
    /// come, and the line it answers.
    /// </summary>
    public static Func<GameBot, string, ChatPromiseReason, string, bool> Duty { get; set; }

    private static long Now()
    {
        try
        {
            return GameLoop.GameLoopTime;
        }
        catch (Exception)
        {
            return Environment.TickCount64;
        }
    }

    /// <summary>
    /// True when <paramref name="bot"/> has taken a real duty to go to <paramref name="place"/> for <paramref name="reason"/>,
    /// so it may say "omw". False when it will not go: it then must not say it is coming. <paramref name="said"/> is the line
    /// it answers (or the asker's name): keeps are read from it when <paramref name="place"/> names none.
    /// </summary>
    public static bool TryPromiseCome(GameBot bot, string place, ChatPromiseReason reason, string said = null) =>
        TryPromiseCome(bot, place, reason, said, null, out _);

    /// <summary>The same, with the player (or bot) who called: needed for help, heals and a rez (the place is them).</summary>
    public static bool TryPromiseCome(GameBot bot, string place, ChatPromiseReason reason, string said, GameLiving asker) =>
        TryPromiseCome(bot, place, reason, said, asker, out _);

    /// <summary>The same, giving the promise (its estimate, <see cref="ChatPromise.EtaMinutes"/>, for "omw, 2 min").</summary>
    public static bool TryPromiseCome(GameBot bot, string place, ChatPromiseReason reason, string said, GameLiving asker,
        out ChatPromise promise)
    {
        promise = null;
        if (bot == null)
            return false;
        try
        {
            if (Duty is { } duty)
                return duty(bot, place, reason, said);
            long now = Now();
            if (!Decide(bot, place, reason, said, asker, now, out ChatPromiseTarget target, out ChatPromiseMode mode, out int eta,
                    out string why))
            {
                LogRefusal(bot, reason, place ?? target?.Name, why, now);
                return false;
            }
            BotDutyLease lease = BotDutyLeases.Current(bot);
            promise = Book.Make(bot, bot.Name, bot.DatabaseID, bot.Realm, reason, mode, target, AskerName(asker, said), eta, now,
                lease?.Kind ?? eDutyLeaseKind.None, lease?.Owner);
            return promise != null;
        }
        catch (Exception exception)
        {
            Log.Warn($"CHAT_PROMISE error bot=\"{bot.Name}\" reason={reason}: {exception.Message}");
            return false;
        }
    }

    /// <summary>Whether the bot could promise now, without promising (a filter over candidate lines). Pure but for reads.</summary>
    public static bool CanPromiseCome(GameBot bot, string place, ChatPromiseReason reason, string said = null, GameLiving asker = null)
    {
        if (bot == null)
            return false;
        try
        {
            if (Duty is { } duty)
                return duty(bot, place, reason, said);
            return Decide(bot, place, reason, said, asker, Now(), out _, out _, out _, out _);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// The one-line guard for a chosen reply: <paramref name="line"/> when it promises nothing or when the bot really
    /// promises (commits, as <see cref="TryPromiseCome(GameBot, string, ChatPromiseReason, string, GameLiving)"/>), else
    /// <paramref name="fallback"/> (which must promise nothing; null drops the reply).
    /// </summary>
    public static string Gate(GameBot bot, string line, string place, ChatPromiseReason reason, string said, string fallback,
        GameLiving asker = null) =>
        !ChatPromiseRules.SaysComing(line) || TryPromiseCome(bot, place, reason, said, asker) ? line : fallback;

    /// <summary>The bot's promise that still owns its walk (open or holding), or null. One dictionary read.</summary>
    public static ChatPromise ActiveFor(GameBot bot)
    {
        ChatPromise promise = Book.Of(bot);
        return promise != null && promise.Active(Now()) ? promise : null;
    }

    /// <summary>
    /// The bot's turn on its promise: arrival, the deadline and what ends it are judged here (AutonomousWorldBotController.Tick
    /// calls it every turn; one dictionary read when the bot has none).
    /// </summary>
    public static void Observe(GameBot bot)
    {
        ChatPromise promise = Book.Of(bot);
        if (promise == null)
            return;
        try
        {
            Book.Check(bot, Now(), ProgressOf(bot, promise));
        }
        catch (Exception exception)
        {
            Log.Warn($"CHAT_PROMISE check error bot=\"{bot?.Name}\": {exception.Message}");
        }
    }

    /// <summary>The bot left the world: an open promise is broken ("logged_out").</summary>
    public static void OnLoggedOut(GameBot bot)
    {
        if (bot == null || Book.Of(bot) == null)
            return;
        Book.Check(bot, Now(), new ChatPromiseBook.Progress(false, bot.IsAlive, false, null, false));
    }

    /// <summary>A line for /lswhy: "promise: omw to Bledmeer Faste (alarm), 95s of 300s".</summary>
    public static string Describe(GameBot bot)
    {
        ChatPromise promise = Book.Of(bot);
        long now = Now();
        if (promise == null || !promise.Active(now))
            return "promise: none";
        return promise.Open
            ? $"promise: going to {promise.Target.Name} ({promise.Reason}, {promise.Mode}), {(now - promise.Made) / 1000}s of {(promise.Deadline - promise.Made) / 1000}s"
            : $"promise: at {promise.Target.Name} ({promise.Reason}), staying {Math.Max(0, promise.HoldUntil - now) / 1000}s more";
    }

    #region Reading the world

    private static bool Decide(GameBot bot, string place, ChatPromiseReason reason, string said, GameLiving asker, long now,
        out ChatPromiseTarget target, out ChatPromiseMode mode, out int eta, out string why)
    {
        asker ??= ResolveAsker(said);
        target = ResolveTarget(bot, place, reason, said, asker);
        ChatPromiseRules.Candidate candidate = CandidateOf(bot, target, reason, asker, now);
        why = ChatPromiseRules.Refusal(candidate, reason, out mode, out eta);
        return why == null;
    }

    private static string AskerName(GameLiving asker, string said) =>
        asker?.Name ?? (said != null && said.Length <= 24 && !said.Contains(' ') ? said : string.Empty);

    private static GameLiving ResolveAsker(string said)
    {
        if (string.IsNullOrWhiteSpace(said) || said.Length > 24 || said.Contains(' '))
            return null;
        try
        {
            return ClientService.Instance?.GetPlayerByExactName(said);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static ChatPromiseRules.Candidate CandidateOf(GameBot bot, ChatPromiseTarget target, ChatPromiseReason reason,
        GameLiving asker, long now)
    {
        BotDutyLease lease = BotDutyLeases.Current(bot, now);
        bool sameRegion = target != null && bot.CurrentRegionID == target.RegionId;
        double distance = target == null ? 0
            : sameRegion ? Vector2.Distance(new(bot.X, bot.Y), new(target.X, target.Y))
            : 0;
        bool porterReach = target != null && !sameRegion && target.Frontier &&
                           AutonomousFrontierTransport.PorterFrontiers(bot.Realm, bot.CurrentRegionID).Contains(target.RegionId);
        if (porterReach)
            distance = 6000; // the far border keep to its frontier's keeps: a rough, honest walk after the porter
        bool inAskersGroup = asker != null && bot.Group != null && ReferenceEquals(bot.Group, asker.Group);
        BotPersonality personality = AutonomousBotPersonality.For(bot);
        bool gamebot = bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper;
        return new ChatPromiseRules.Candidate(
            Gamebot: gamebot,
            Alive: bot.IsAlive,
            Active: bot.ObjectState == GameObject.eObjectState.Active,
            InCombat: bot.InCombat || bot.IsAttacking || bot.IsCrowdControlled || (bot.Brain as BotBrain)?.HasAggro == true,
            CarryingRelic: GameRelic.IsPlayerCarryingRelic(bot),
            OnStableRide: bot.IsOnStableMasterRoute,
            InBotGroup: bot.Group != null && !inAskersGroup && !bot.IsPlayerLedGroup,
            InAskersGroup: inAskersGroup,
            SpokenForByPlayer: BotDutyLeases.IsSpokenForByPlayer(bot) || bot.IsPlayerLedGroup,
            Lease: lease?.Kind ?? eDutyLeaseKind.None,
            LeaseGoesThere: lease?.Kind == eDutyLeaseKind.RvrEvent && target?.Kind == ChatPromiseTargetKind.Keep &&
                            string.Equals(lease.OwnerName, target.Name, StringComparison.OrdinalIgnoreCase),
            PromisedElsewhere: Book.PromisedElsewhere(bot, target, now),
            PromisedThere: target == null || Book.Of(bot) is { } own && own.Active(now) ? 0 : Book.GoingTo(target, now),
            RvrAssigned: AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR),
            Level: bot.Level,
            CanHeal: bot.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)bot.CharacterClass.ID),
            CanRezAsker: reason == ChatPromiseReason.Rez && asker is GamePlayer dead &&
                         AutonomousBotChatCoordinator.WillComeRez(bot, dead),
            PersonalityApplies: AutonomousBotPersonality.Applies(bot),
            Risk: personality.Traits.Risk,
            Sociability: personality.Traits.Sociability,
            SameRealm: asker == null || asker.Realm == bot.Realm,
            BoundaryAllows: target == null || target.ZoneId == 0 || AutonomousRealmBoundary.Allows(bot.Realm, target.RegionId, target.ZoneId),
            TargetFound: target != null,
            TargetKind: target?.Kind ?? ChatPromiseTargetKind.Zone,
            Frontier: target?.Frontier == true,
            Battleground: target?.Battleground == true,
            MinLevel: target?.MinLevel ?? 0,
            MaxLevel: target?.MaxLevel ?? 0,
            SameRegion: sameRegion,
            Distance: distance,
            PorterReach: porterReach);
    }

    /// <summary>Where the promise would go: the asker for help, heals and a rez; a keep or a zone for the others.</summary>
    private static ChatPromiseTarget ResolveTarget(GameBot bot, string place, ChatPromiseReason reason, string said, GameLiving asker)
    {
        if (reason is ChatPromiseReason.Help or ChatPromiseReason.Heal or ChatPromiseReason.Rez)
            return asker == null || asker.CurrentRegion == null ? null
                : new ChatPromiseTarget(ChatPromiseTargetKind.Living, asker.Name, asker.CurrentRegionID, asker.X, asker.Y, asker.Z,
                    ZoneId: (ushort)(asker.CurrentZone?.ID ?? 0), Living: asker);
        if (reason == ChatPromiseReason.RaidTime)
        {
            BotDutyLease lease = BotDutyLeases.Current(bot);
            return lease?.Kind == eDutyLeaseKind.RealmRaid
                ? new ChatPromiseTarget(ChatPromiseTargetKind.Raid, lease.OwnerName ?? lease.Owner, bot.CurrentRegionID, bot.X, bot.Y, bot.Z)
                : null;
        }
        ICollection<AbstractGameKeep> keeps = GameServer.KeepManager?.GetAllKeeps();
        if (keeps == null)
            return null;
        // A keep named in the place or the line it answers ("inc bled", "need def crim").
        AbstractGameKeep named = keeps.Where(AutonomousRvrKeepPolicy.IsSiegeObjective)
            .Where(k => ChatPromiseRules.NamesKeep(place, k.Name) || ChatPromiseRules.NamesKeep(said, k.Name))
            .OrderByDescending(k => k.Realm == bot.Realm)
            .ThenByDescending(k => k.LastAttackedByEnemyTick)
            .FirstOrDefault();
        if (named != null)
            return KeepTarget(named);
        // A zone ("inc emain", "free rps in thid"): its own-realm keep under attack, else its battleground keep, else any of
        // its keeps as the anchor of a walk into the zone.
        AutonomousChatPlaces.Place zonePlace = AutonomousChatPlaces.All.FirstOrDefault(p =>
            string.Equals(p.Key, place, StringComparison.OrdinalIgnoreCase)) ??
            (said == null ? null : AutonomousChatQuestions.PlaceNamed(said, AutonomousChatPlaces.All));
        if (zonePlace == null || zonePlace.Type is not ("frontier" or "battleground"))
            return null;
        AbstractGameKeep[] inZone = keeps.Where(AutonomousRvrKeepPolicy.IsSiegeObjective)
            .Where(k => ZoneOf(k) is { } z && zonePlace.Zones.Any(name => string.Equals(name, z.Description, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        if (inZone.Length == 0)
            return null;
        if (zonePlace.Type == "battleground")
            return KeepTarget(inZone.OrderByDescending(k => k.LastAttackedByEnemyTick).First());
        AbstractGameKeep hit = inZone.Where(k => k.Realm == bot.Realm && k.LastAttackedByEnemyTick > 0)
            .OrderByDescending(k => k.LastAttackedByEnemyTick).FirstOrDefault();
        if (hit != null && reason == ChatPromiseReason.Alarm)
            return KeepTarget(hit);
        AbstractGameKeep anchor = inZone.OrderByDescending(k => k.Realm == bot.Realm).ThenBy(k => k.KeepID).First();
        Zone zone = ZoneOf(anchor);
        return new ChatPromiseTarget(ChatPromiseTargetKind.Zone, zone?.Description ?? zonePlace.Key, anchor.Region, anchor.X, anchor.Y,
            anchor.Z, anchor.KeepID, anchor.Realm, (ushort)(zone?.ID ?? 0), Frontier: AutonomousWorldBotController.IsFrontierPoint(anchor.Region, anchor.X, anchor.Y));
    }

    private static Zone ZoneOf(AbstractGameKeep keep) => WorldMgr.GetRegion(keep.Region)?.GetZone(keep.X, keep.Y);

    private static ChatPromiseTarget KeepTarget(AbstractGameKeep keep)
    {
        Zone zone = ZoneOf(keep);
        BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.IsBattlegroundRegion(keep.Region) ? BattlegroundBrackets.ForRegion(keep.Region) : null;
        return new ChatPromiseTarget(ChatPromiseTargetKind.Keep, keep.Name, keep.Region, keep.X, keep.Y, keep.Z, keep.KeepID, keep.Realm,
            (ushort)(zone?.ID ?? 0), Frontier: bracket == null && AutonomousWorldBotController.IsFrontierPoint(keep.Region, keep.X, keep.Y),
            Battleground: bracket != null, MinLevel: bracket?.MinLevel ?? 0, MaxLevel: bracket?.MaxLevel ?? 0);
    }

    private static ChatPromiseBook.Progress ProgressOf(GameBot bot, ChatPromise promise)
    {
        bool present = bot.ObjectState == GameObject.eObjectState.Active;
        ChatPromiseTarget target = promise.Target;
        bool arrived;
        string gone = null;
        switch (target.Kind)
        {
            case ChatPromiseTargetKind.Living:
                GameLiving living = target.Living;
                if (living == null || living.ObjectState != GameObject.eObjectState.Active)
                    gone = "asker_gone";
                arrived = gone == null && (promise.Reason == ChatPromiseReason.Rez
                    ? living.IsAlive && bot.CurrentRegion == living.CurrentRegion && bot.IsWithinRadius(living, 1_500)
                    : bot.CurrentRegion == living.CurrentRegion && bot.IsWithinRadius(living, target.ArrivalRadius));
                break;
            case ChatPromiseTargetKind.Raid:
                arrived = BotDutyLeases.HoldsWithReason(bot, eDutyLeaseKind.RealmRaid, DutyLeaseReason.RaidParty);
                if (!arrived && !BotDutyLeases.Holds(bot, eDutyLeaseKind.RealmRaid))
                    gone = "duty_ended";
                break;
            default:
                arrived = bot.CurrentRegionID == target.RegionId &&
                          (target.Kind == ChatPromiseTargetKind.Zone && target.ZoneId != 0 && bot.CurrentZone?.ID == target.ZoneId ||
                           Vector2.DistanceSquared(new(bot.X, bot.Y), new(target.X, target.Y)) <= (float)target.ArrivalRadius * target.ArrivalRadius);
                if (promise.Reason == ChatPromiseReason.Alarm && target.Kind == ChatPromiseTargetKind.Keep && target.Holder == bot.Realm &&
                    GameServer.KeepManager?.GetKeepsOfRegion(target.RegionId)?.FirstOrDefault(k => k.KeepID == target.Anchor) is { } keep &&
                    keep.Realm != bot.Realm)
                    gone = "keep_fell";
                if (promise.Mode == ChatPromiseMode.Lease && !BotDutyLeases.Holds(bot, eDutyLeaseKind.RvrEvent))
                    gone ??= "duty_ended";
                break;
        }
        // A bigger duty took the bot after it promised (a raid reservation, a player's group, a siege force): broken, honestly.
        BotDutyLease lease = BotDutyLeases.Current(bot);
        bool taken = promise.Mode != ChatPromiseMode.Lease && promise.Mode != ChatPromiseMode.Native && lease != null &&
                     (lease.Kind != promise.LeaseAtMake || !string.Equals(lease.Owner, promise.LeaseOwnerAtMake, StringComparison.Ordinal));
        return new ChatPromiseBook.Progress(present, bot.IsAlive, arrived, gone, taken);
    }

    private static void LogRefusal(GameBot bot, ChatPromiseReason reason, string place, string why, long now)
    {
        // Once a minute per bot and reason: the line proves a bot that did not go did not say it would.
        var key = (bot.DatabaseID, why + reason);
        if (RecentRefusals.TryGetValue(key, out long last) && now - last < 60_000)
            return;
        RecentRefusals[key] = now;
        if (RecentRefusals.Count > 4096)
            foreach (var stale in RecentRefusals.Where(p => now - p.Value >= 60_000).Select(p => p.Key).ToArray())
                RecentRefusals.TryRemove(stale, out _);
        if (Log.IsInfoEnabled)
            Log.Info($"CHAT_PROMISE refused bot=\"{bot.Name}\" id={bot.DatabaseID} reason={reason} place=\"{place}\" why={why}");
    }

    #endregion
}
