using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>What a gamebot learns from (item 2, "Getting better"). WP1 credits fights won and deaths; WP2 may credit the
/// rest at its hooks through <see cref="BotSkill.Grow"/>.</summary>
public enum BotSkillEvent : byte
{
    /// <summary>A monster or an enemy the bot or a groupmate near it killed: play time for all six facets.</summary>
    FightWon,
    /// <summary>The bot died: a lesson for positioning, standing in fire and reactions.</summary>
    Death,
    /// <summary>The bot pulled for its group (WP2's pull hook).</summary>
    PullMade,
    /// <summary>The bot cast a buff (WP2's buff hook).</summary>
    BuffCast,
    /// <summary>The bot sat to rest for power (WP2's power hook).</summary>
    RestedForPower,
}

/// <summary>
/// How a skill profile grows with play (docs/BOTS-AS-PEOPLE-1.2.md, item 2, "Getting better"; work package 1). Experience
/// is in minutes per facet. A facet moves from its start toward the top of the next tier's band by
/// 1 - e^(-experience / scale), the curve RaidDiscipline.Familiarity uses: <see cref="HoursPerTier"/> hours of fighting
/// take a facet from the bottom of a band to the bottom of the next. The asymptote is the top of the next band, so a bot
/// never grows more than one tier (owner decision 6), and experience is only ever added, so skill never falls.
/// No source measured how fast a 2003 player improved: the curve and the credits are our design. Pure.
/// </summary>
public static class BotSkillGrowth
{
    /// <summary>Hours of fighting from the bottom of a band to the bottom of the next (design: about 40 hours).</summary>
    public const double HoursPerTier = 40;

    /// <summary>The curve's scale in minutes: 1 - e^(-x / scale) = 1/2 after <see cref="HoursPerTier"/> hours.</summary>
    public static readonly double ExperienceScale = HoursPerTier * 60 / Math.Log(2);

    /// <summary>A fight won credits the time since the bot's last credited fight, at most this much (a bot that fights
    /// at least every 3 minutes learns all the time it plays; a trip or a town break counts as 3 minutes).</summary>
    public const long FightCreditCapMilliseconds = 180_000;

    /// <summary>Minutes a death teaches positioning and standing in fire (reactions half), at most once per
    /// <see cref="DeathGapMilliseconds"/>.</summary>
    public const double DeathLessonMinutes = 5;
    public const long DeathGapMilliseconds = 600_000;

    /// <summary>The WP2 credits: minutes and the least time between two credits of the same kind.</summary>
    public const double PullMinutes = 0.5, BuffMinutes = 0.5, RestMinutes = 1;
    public const long PullGapMilliseconds = 60_000, BuffGapMilliseconds = 60_000, RestGapMilliseconds = 300_000;

    /// <summary>The asymptote of a start tier's facets: the top of the next band (Veteran: the top of its own).</summary>
    public static double Top(RvrSkillTier startTier) => Math.Min((int)startTier + 2, 3);

    /// <summary>A facet's skill after this much experience (minutes): start + (top - start) * (1 - e^(-xp / scale)). Pure.</summary>
    public static double Grown(double start, double top, double experienceMinutes)
    {
        if (!double.IsFinite(experienceMinutes) || experienceMinutes <= 0 || top <= start)
            return start;
        return start + (top - start) * (1 - Math.Exp(-experienceMinutes / ExperienceScale));
    }

    /// <summary>The experience (minutes) a facet needs to grow from <paramref name="start"/> to <paramref name="target"/>
    /// toward <paramref name="top"/>: 0 when already there, infinity when the target is at or past the top. Pure.</summary>
    public static double ExperienceToReach(double start, double top, double target)
    {
        if (target <= start)
            return 0;
        if (target >= top)
            return double.PositiveInfinity;
        return -ExperienceScale * Math.Log(1 - (target - start) / (top - start));
    }

    /// <summary>
    /// The experience one event teaches, per facet, given the time since the bot's last event of the same kind (for the
    /// first one, since it logged in). Zero when the event came too soon after the last. Pure.
    /// </summary>
    public static BotSkillExperience Gain(BotSkillEvent kind, long sinceLastMilliseconds)
    {
        long since = Math.Max(0, sinceLastMilliseconds);
        switch (kind)
        {
            case BotSkillEvent.FightWon:
            {
                double minutes = Math.Min(since, FightCreditCapMilliseconds) / 60_000.0;
                return new BotSkillExperience
                {
                    Reactions = minutes, Positioning = minutes, Pulls = minutes, Fire = minutes, Buffs = minutes, Power = minutes,
                    ReachedTier = -1,
                };
            }
            case BotSkillEvent.Death when since >= DeathGapMilliseconds:
                return new BotSkillExperience
                {
                    Positioning = DeathLessonMinutes, Fire = DeathLessonMinutes, Reactions = DeathLessonMinutes / 2, ReachedTier = -1,
                };
            case BotSkillEvent.PullMade when since >= PullGapMilliseconds:
                return BotSkillExperience.None with { Pulls = PullMinutes };
            case BotSkillEvent.BuffCast when since >= BuffGapMilliseconds:
                return BotSkillExperience.None with { Buffs = BuffMinutes };
            case BotSkillEvent.RestedForPower when since >= RestGapMilliseconds:
                return BotSkillExperience.None with { Power = RestMinutes };
            default:
                return BotSkillExperience.None;
        }
    }

    /// <summary>
    /// Where a bot's clock for one kind of lesson starts at login: a fight credits the time since login (so it starts now);
    /// every other kind only has a gap, so the first one after login counts (a death in the first 10 minutes online teaches
    /// too; work package 7). Pure.
    /// </summary>
    public static long ClockStart(BotSkillEvent kind, long nowMs) =>
        kind == BotSkillEvent.FightWon ? nowMs : nowMs - Math.Max(DeathGapMilliseconds, Math.Max(RestGapMilliseconds,
            Math.Max(PullGapMilliseconds, BuffGapMilliseconds)));

    /// <summary>The experience after one event: each facet plus its gain (never less than before); the tier unchanged. Pure.</summary>
    public static BotSkillExperience Step(in BotSkillExperience experience, BotSkillEvent kind, long sinceLastMilliseconds) =>
        Add(experience, Gain(kind, sinceLastMilliseconds));

    /// <summary>Adds a gain (negative or unreadable parts count as 0). Pure.</summary>
    public static BotSkillExperience Add(in BotSkillExperience experience, in BotSkillExperience gain)
    {
        static double Plus(double a, double b) => (double.IsFinite(a) && a > 0 ? a : 0) + (double.IsFinite(b) && b > 0 ? b : 0);
        return experience with
        {
            Reactions = Plus(experience.Reactions, gain.Reactions), Positioning = Plus(experience.Positioning, gain.Positioning),
            Pulls = Plus(experience.Pulls, gain.Pulls), Fire = Plus(experience.Fire, gain.Fire),
            Buffs = Plus(experience.Buffs, gain.Buffs), Power = Plus(experience.Power, gain.Power),
        };
    }

    /// <summary>
    /// The experience to save with a profile made from it: the reached tier becomes the profile's tier when that is
    /// higher (so a promotion is kept even if rvr_bot_awareness later moves the start roll), never lower. Pure.
    /// </summary>
    public static BotSkillExperience Settle(in BotSkillExperience experience, in BotSkillProfile profile) =>
        profile.IsNone || (int)profile.Tier <= experience.ReachedTier ? experience : experience with { ReachedTier = (int)profile.Tier };

    /// <summary>
    /// Hours of fighting (all six facets learning together) until the facets' mean reaches the next tier, from this
    /// profile: 0 when the bot is already at its cap's band, infinity when it can't grow (Veteran). Pure.
    /// </summary>
    public static double HoursToNextTier(in BotSkillProfile profile)
    {
        if (profile.IsNone || profile.Tier >= profile.MaxTier)
            return double.PositiveInfinity;
        double top = Top(profile.StartTier), target = (int)profile.Tier + 1, mean = profile.MeanSkill;
        if (mean >= target)
            return 0;
        // The mean grows on the same curve as each facet when they learn together: find the extra minutes from now.
        double minutes = ExperienceToReach(mean, top, target);
        return minutes / 60;
    }
}

/// <summary>
/// The skill profiles of the online gamebots (work package 1): made at login (BOT_SKILL_PROFILE), grown at events
/// (fights won, deaths; WP2 adds pulls, buffs and rests through <see cref="Grow"/>) and saved through
/// <see cref="BotPeopleStore.Skill"/> (batched off the game loop every 5 minutes and soon after a logout). A tier change
/// logs BOT_SKILL_GROWTH; every 5 minutes BOT_SKILL_BUDGET counts the online bots per tier per realm. Autonomous gamebots
/// only (<see cref="BotPersonKey.Of(GameBot)"/> and <see cref="BotPeopleScope"/>, rule Record): never companions or
/// players. A profile changes no play by itself; WP2's mistakes read it through <see cref="TryProfile(GameBot, out BotSkillProfile)"/>.
/// </summary>
public static class BotSkill
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public const string ProfileTag = "BOT_SKILL_PROFILE";
    public const string GrowthTag = "BOT_SKILL_GROWTH";
    public const string BudgetTag = "BOT_SKILL_BUDGET";

    /// <summary>BOT_SKILL_PROFILE lines per minute at most (a server start logs thousands of bots in); the rest are counted
    /// in BOT_SKILL_BUDGET. The first ones of a minute are not chosen by tier, so the sample's shares stay fair.</summary>
    public const int ProfileLinesPerMinute = 30;

    /// <summary>Groupmates within this range of a bot that won a fight learn from it too (a healer kills nothing).</summary>
    public const int GroupCreditRange = 3_000;

    private static readonly int EventKinds = Enum.GetValues<BotSkillEvent>().Length;

    /// <summary>What the server keeps per online gamebot: its profile (swapped whole, so readers never see half of one)
    /// and the time of its last credit of each kind.</summary>
    private sealed class Online
    {
        public Online(int realm, long nowMs, int kinds)
        {
            Realm = realm;
            Last = new long[kinds];
            for (int kind = 0; kind < kinds; kind++)
                Last[kind] = BotSkillGrowth.ClockStart((BotSkillEvent)kind, nowMs);
        }

        public readonly int Realm;
        public readonly long[] Last;
        public Snapshot Current;
    }

    private sealed class Snapshot(BotSkillProfile profile)
    {
        public readonly BotSkillProfile Profile = profile;
    }

    private static readonly ConcurrentDictionary<BotPersonKey, Online> Bots = new();

    private static long _creditsFight, _creditsDeath, _creditsPull, _creditsBuff, _creditsRest, _promotions, _minutesTimes1000;
    private static long _profileLines, _profileLinesSuppressed, _profileMinute, _profileLinesThisMinute;

    /// <summary>The awareness setting now (the one difficulty knob, owner decision 6).</summary>
    public static RvrBotAwareness Awareness => AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS);

    private static BotPersonality? PersonalityOf(GameBot bot) =>
        bot != null && AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot) : null;

    // ---- reading (WP2) --------------------------------------------------------------------------------------------------

    /// <summary>
    /// The skill profile of a gamebot, for WP2's rules: false (and None) when bot_skill_profiles is off or the bot has no
    /// person (a companion, a helper, an Epic Special Force, a buffbot, a player's pet). Counts nothing and checks no
    /// scope: a WP2 hook asks <see cref="BotPeopleScope.Check"/> with its own rule first. A cached struct (no allocation)
    /// after the bot's first read.
    /// </summary>
    public static bool TryProfile(GameBot bot, out BotSkillProfile profile)
    {
        profile = default;
        if (!Properties.BOT_SKILL_PROFILES)
            return false;
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
            return false;
        profile = EntryOf(person, bot).Current.Profile;
        return true;
    }

    /// <summary>
    /// A gamebot's own tier, the one every package reads (work package 7): its profile's tier (grown by play) when
    /// <see cref="TryProfile"/> gives one, else its start roll (<see cref="BotSkillProfile.StartTierOf"/>), so switching
    /// bot_skill_profiles off never gives a bot a second tier. A bot with no person rolls on its object id. Counts nothing.
    /// </summary>
    public static RvrSkillTier TierOf(GameBot bot)
    {
        if (TryProfile(bot, out BotSkillProfile profile))
            return profile.Tier;
        BotPersonKey person = BotPersonKey.Of(bot);
        return BotSkillProfile.StartTierOf(person.IsNone ? BotPersonKey.Character(bot?.ObjectID ?? 0) : person, Awareness);
    }

    /// <summary>The profile of a person from the store (online or not; /lswhy and tests). None for no person.</summary>
    public static BotSkillProfile ProfileOf(BotPersonKey person, BotPersonality? personality, RvrBotAwareness awareness, BotPeopleStore store = null)
    {
        if (person.IsNone)
            return default;
        (store ?? BotPeopleStore.Current).Skill.TryGet(person, out OfflineBotSkillRecord row);
        return BotSkillProfile.For(person, personality, awareness, BotSkillExperience.From(row));
    }

    /// <summary>How many gamebots have a cached profile now.</summary>
    public static int OnlineCount => Bots.Count;

    private static Online EntryOf(BotPersonKey person, GameBot bot)
    {
        if (Bots.TryGetValue(person, out Online entry) && entry.Current != null)
            return entry;
        // The factory takes the bot as its argument: no closure, so a cached read allocates nothing.
        entry = Bots.GetOrAdd(person, static (_, b) => new Online((int)(b?.Realm ?? 0), GameLoop.GameLoopTime, EventKinds), bot);
        if (entry.Current == null)
            Interlocked.CompareExchange(ref entry.Current, new Snapshot(ProfileOf(person, PersonalityOf(bot), Awareness)), null);
        return entry;
    }

    // ---- the hooks -------------------------------------------------------------------------------------------------------

    /// <summary>A gamebot came into the world (AutonomousBotRegistry.Register): its profile is made and logged.</summary>
    public static void OnLoggedIn(GameBot bot)
    {
        if (BotPeopleScope.Check(bot, BotPeopleRule.Record, Properties.BOT_SKILL_PROFILES, out _) != BotPeopleRefusal.None)
            return;
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        BotPersonKey person = BotPersonKey.Of(bot);
        Bots.TryRemove(person, out Online _); // a fresh login starts its credit clocks now
        Online entry = EntryOf(person, bot);
        if (TakeProfileLine())
            Log.Info(ProfileLine(bot.Name, person, bot.Realm, bot.Level, entry.Current.Profile, Awareness));
    }

    /// <summary>A gamebot killed something (GameBot.EnemyKilled): it and its gamebot groupmates near it learn.</summary>
    public static void OnFightWon(GameBot bot, GameLiving enemy)
    {
        if (BotPeopleScope.Check(bot, BotPeopleRule.Record, BotPeopleSwitches.Current.SkillGrowth, out _) != BotPeopleRefusal.None)
            return;
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        Credit(bot, BotSkillEvent.FightWon);
        if (bot.Group == null)
            return;
        foreach (GameLiving member in bot.Group.GetMembersInTheGroup())
        {
            // Active only: a groupmate that just logged out must not get its entry made again (nothing would remove it).
            if (member is GameBot mate && mate != bot && mate.IsAlive && mate.ObjectState == GameObject.eObjectState.Active &&
                mate.CurrentRegionID == bot.CurrentRegionID &&
                mate.IsWithinRadius(bot, GroupCreditRange) &&
                BotPeopleScope.Check(mate, BotPeopleRule.Record, true, out _) == BotPeopleRefusal.None)
                Credit(mate, BotSkillEvent.FightWon);
        }
    }

    /// <summary>A gamebot died (GameBot.ProcessDeath): a lesson, at most once in 10 minutes.</summary>
    public static void OnDied(GameBot bot) => Grow(bot, BotSkillEvent.Death);

    /// <summary>
    /// Credits one learning event to a gamebot (WP2's hooks: PullMade, BuffCast, RestedForPower). Checks the switch
    /// (bot_skill_growth) and the scope first. True when experience was added.
    /// </summary>
    public static bool Grow(GameBot bot, BotSkillEvent kind)
    {
        if (BotPeopleScope.Check(bot, BotPeopleRule.Record, BotPeopleSwitches.Current.SkillGrowth, out _) != BotPeopleRefusal.None)
            return false;
        using var scope = AiProfile.Scope(AiSystem.BotSkill);
        return Credit(bot, kind);
    }

    private static bool Credit(GameBot bot, BotSkillEvent kind)
    {
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
            return false;
        Online entry = EntryOf(person, bot);
        long now = GameLoop.GameLoopTime;
        BotSkillExperience gain;
        lock (entry)
        {
            gain = BotSkillGrowth.Gain(kind, now - entry.Last[(int)kind]);
            if (gain.Total <= 0)
                return false;
            entry.Last[(int)kind] = now;
        }
        var (before, after) = Learn(BotPeopleStore.Current, person, bot.DatabaseID, bot.Realm, PersonalityOf(bot), Awareness, gain,
            DateTime.UtcNow);
        Volatile.Write(ref entry.Current, new Snapshot(after));
        Count(kind, gain.Mean);
        if (after.Tier > before.Tier)
        {
            Interlocked.Increment(ref _promotions);
            Log.Info(GrowthLine(bot.Name, person, bot.Realm, before, after));
        }
        return true;
    }

    /// <summary>
    /// Adds a gain to a person's saved experience in <paramref name="store"/> (the row is made on its first lesson and
    /// queued for the batched save; the reached tier is raised to the new profile's tier, never lowered) and returns the
    /// profile before and after. The one place a skill row changes. Thread-safe (under the table's lock).
    /// </summary>
    public static (BotSkillProfile Before, BotSkillProfile After) Learn(BotPeopleStore store, BotPersonKey person, long characterId,
        eRealm realm, BotPersonality? personality, RvrBotAwareness awareness, BotSkillExperience gain, DateTime nowUtc)
    {
        if (person.IsNone)
            return default;
        BotSkillProfile before = default, after = default;
        store.Skill.Change(person,
            () => new OfflineBotSkillRecord { PersonKey = person.ToString(), CharacterId = characterId, Tier = -1 },
            row =>
            {
                BotSkillExperience experience = BotSkillExperience.From(row);
                before = BotSkillProfile.For(person, personality, awareness, experience);
                experience = BotSkillGrowth.Add(experience, gain);
                experience = BotSkillGrowth.Settle(experience, BotSkillProfile.For(person, personality, awareness, experience));
                after = BotSkillProfile.For(person, personality, awareness, experience);
                experience.CopyTo(row);
                row.CharacterId = characterId;
                row.Realm = (int)realm;
                row.UpdatedUtc = nowUtc.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
            });
        return (before, after);
    }

    private static void Count(BotSkillEvent kind, double minutes)
    {
        switch (kind)
        {
            case BotSkillEvent.FightWon: Interlocked.Increment(ref _creditsFight); break;
            case BotSkillEvent.Death: Interlocked.Increment(ref _creditsDeath); break;
            case BotSkillEvent.PullMade: Interlocked.Increment(ref _creditsPull); break;
            case BotSkillEvent.BuffCast: Interlocked.Increment(ref _creditsBuff); break;
            case BotSkillEvent.RestedForPower: Interlocked.Increment(ref _creditsRest); break;
        }
        Interlocked.Add(ref _minutesTimes1000, (long)Math.Round(minutes * 1000));
    }

    private static bool TakeProfileLine()
    {
        long minute = Environment.TickCount64 / 60_000;
        if (Interlocked.Exchange(ref _profileMinute, minute) != minute)
            Interlocked.Exchange(ref _profileLinesThisMinute, 0);
        if (Interlocked.Increment(ref _profileLinesThisMinute) > ProfileLinesPerMinute)
        {
            Interlocked.Increment(ref _profileLinesSuppressed);
            return false;
        }
        Interlocked.Increment(ref _profileLines);
        return true;
    }

    private static void OnLoggedOut(BotPersonKey person, GameBot bot) => Bots.TryRemove(person, out _);

    // ---- log lines (pure) ------------------------------------------------------------------------------------------------

    public static string RealmName(eRealm realm) => realm switch
    {
        eRealm.Albion => "alb",
        eRealm.Midgard => "mid",
        eRealm.Hibernia => "hib",
        _ => "none",
    };

    /// <summary>BOT_SKILL_PROFILE name= person= realm= level= tier= start= place= six facets xp_h= awareness=. Pure.</summary>
    public static string ProfileLine(string name, BotPersonKey person, eRealm realm, int level, in BotSkillProfile profile, RvrBotAwareness awareness) =>
        $"{ProfileTag} name={name} person={person} realm={RealmName(realm)} level={level} {profile.Describe()} " +
        $"awareness={awareness.ToString().ToLowerInvariant()}";

    /// <summary>BOT_SKILL_GROWTH name= person= realm= from= to= start= xp_h= (a tier change). Pure.</summary>
    public static string GrowthLine(string name, BotPersonKey person, eRealm realm, in BotSkillProfile before, in BotSkillProfile after) =>
        $"{GrowthTag} name={name} person={person} realm={RealmName(realm)} from={BotSkillProfile.TierName(before.Tier)} " +
        $"to={BotSkillProfile.TierName(after.Tier)} start={BotSkillProfile.TierName(after.StartTier)} " +
        $"xp_h={(after.Experience.Mean / 60).ToString("0.0", CultureInfo.InvariantCulture)}";

    /// <summary>
    /// BOT_SKILL_BUDGET seconds= online= alb=C/A/V mid=C/A/V hib=C/A/V shares=C/A/V start=C/A/V (percent of the online
    /// bots now and at their start) credits=fight:N,death:N,pull:N,buff:N,rest:N minutes= promotions= profile_lines=N
    /// suppressed=N. Pure.
    /// </summary>
    public static string BudgetLine(long seconds, IEnumerable<(int Realm, BotSkillProfile Profile)> online, long[] credits,
        double minutes, long promotions, long profileLines, long suppressed)
    {
        var tiers = new int[4, 3];
        var all = new int[3];
        var start = new int[3];
        int count = 0;
        foreach (var (realm, profile) in online ?? [])
        {
            if (profile.IsNone)
                continue;
            count++;
            all[(int)profile.Tier]++;
            start[(int)profile.StartTier]++;
            if (realm is >= 1 and <= 3)
                tiers[realm, (int)profile.Tier]++;
        }
        string Realm(int realm) => $"{tiers[realm, 0]}/{tiers[realm, 1]}/{tiers[realm, 2]}";
        string Percent(int[] n) => count == 0 ? "0/0/0" : string.Join("/", n.Select(x => Math.Round(100.0 * x / count).ToString(CultureInfo.InvariantCulture)));
        long Credit(int index) => credits != null && index < credits.Length ? credits[index] : 0;
        return $"{BudgetTag} seconds={seconds} online={count} alb={Realm(1)} mid={Realm(2)} hib={Realm(3)} shares={Percent(all)} " +
               $"start={Percent(start)} credits=fight:{Credit(0)},death:{Credit(1)},pull:{Credit(2)},buff:{Credit(3)},rest:{Credit(4)} " +
               $"minutes={minutes.ToString("0.0", CultureInfo.InvariantCulture)} promotions={promotions} " +
               $"profile_lines={profileLines} suppressed={suppressed}";
    }

    private static IEnumerable<string> TakeBudget(long seconds)
    {
        long[] credits =
        [
            Interlocked.Exchange(ref _creditsFight, 0), Interlocked.Exchange(ref _creditsDeath, 0),
            Interlocked.Exchange(ref _creditsPull, 0), Interlocked.Exchange(ref _creditsBuff, 0),
            Interlocked.Exchange(ref _creditsRest, 0),
        ];
        double minutes = Interlocked.Exchange(ref _minutesTimes1000, 0) / 1000.0;
        var online = Bots.Values.Where(entry => entry.Current != null).Select(entry => (entry.Realm, entry.Current.Profile)).ToArray();
        return
        [
            BudgetLine(seconds, online, credits, minutes, Interlocked.Exchange(ref _promotions, 0),
                Interlocked.Exchange(ref _profileLines, 0), Interlocked.Exchange(ref _profileLinesSuppressed, 0)),
        ];
    }

    // ---- /lstest skill and /lswhy skill (read only) ----------------------------------------------------------------------

    /// <summary>The /lswhy skill lines for a profile (pure).</summary>
    public static IEnumerable<string> WhyLines(in BotSkillProfile profile, bool growthOn)
    {
        if (profile.IsNone)
            return ["No skill profile (bot_skill_profiles is off)."];
        var lines = new List<string>
        {
            $"Skill: {BotSkillProfile.TierName(profile.Tier)} (started {BotSkillProfile.TierName(profile.StartTier)}, " +
            $"{(profile.BandPosition * 100).ToString("0", CultureInfo.InvariantCulture)}% up its band; can reach " +
            $"{BotSkillProfile.TierName(profile.MaxTier)} in 1.2).",
        };
        BotSkillProfile p = profile;
        lines.Add("Facets (0 worst, 1 best in its tier): " + string.Join(", ", BotSkillFacets.All.Select(f =>
            $"{BotSkillProfile.FacetName(f)} {p.Position(f).ToString("0.00", CultureInfo.InvariantCulture)}")));
        double hours = BotSkillGrowth.HoursToNextTier(profile);
        lines.Add($"Experience: {(profile.Experience.Mean / 60).ToString("0.0", CultureInfo.InvariantCulture)} hours of fighting; " +
                  (!growthOn ? "growth is off (bot_skill_growth)." :
                   double.IsPositiveInfinity(hours) ? "it has grown as far as it can in 1.2." :
                   $"about {Math.Ceiling(hours).ToString(CultureInfo.InvariantCulture)} more to {BotSkillProfile.TierName(profile.Tier + 1)}."));
        return lines;
    }

    private static string TestLine(GamePlayer gm, GameBot target)
    {
        if (target == null)
            return "/lstest skill: target an autonomous gamebot first.";
        if (!Properties.BOT_SKILL_PROFILES)
            return "/lstest skill: bot_skill_profiles is off.";
        if (!TryProfile(target, out BotSkillProfile profile))
            return $"/lstest skill: {target.Name} has no person (a companion, helper or in-memory bot): no skill profile.";
        return $"{target.Name} ({BotPersonKey.Of(target)}): {profile.Describe()}; growth {(BotPeopleSwitches.Current.SkillGrowth ? "on" : "OFF")}.";
    }

    // ---- life cycle -------------------------------------------------------------------------------------------------------

    private static int _wired;

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args) => Wire();

    /// <summary>Hooks the logout event, the 5-minute summary, /lstest skill and /lswhy skill (once).</summary>
    public static void Wire()
    {
        if (Interlocked.Exchange(ref _wired, 1) != 0)
            return;
        BotPeopleStore.LoggedOut += OnLoggedOut;
        BotPeopleSummary.Register(BudgetTag, TakeBudget);
        BotPeopleTest.Register("skill", (gm, target, words) => TestLine(gm, target));
        BotPeopleWhy.Register(BotPeopleWhy.Topic.Skill, bot =>
            TryProfile(bot, out BotSkillProfile profile) ? WhyLines(profile, BotPeopleSwitches.Current.SkillGrowth) : WhyLines(default, false));
    }

    /// <summary>Forgets every cached profile (tests).</summary>
    public static void ResetForTests() => Bots.Clear();
}
