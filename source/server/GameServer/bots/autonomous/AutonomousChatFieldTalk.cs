using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// Short, functional chat built from what a bot really is and what the world really is (Beta 1.1.2, blind round 3: the
/// ambient world was the biggest giveaway, 85% caught, for life filler, crafted jokes, unprompted tips, capitals and
/// near-copies). Real 2003 chat was mostly terse and about the game in front of you: "lfg 38 sm", "wts 99% chain /send",
/// "anyone up for tg?", "inc mmg", "who has the relic", "brb food". A line here names the bot's real level, class, zone, camp,
/// bag item and the realm's real keeps and relics, so lines vary because the world varies, not because a template has
/// wordings. Lowercase, mostly unpunctuated, with the period's abbreviations.
/// Round 5 (the stream test): every line belongs to a family (its shape, "ask_xp", "camp_free") and an idea (the family and
/// its subject, "camp_free:large skeletons"); a realm doesn't hear a family again for 3-6 minutes or an idea for 30-60, and
/// each line carries its own answers, so "me" answers only a line that recruits. Pure but for <see cref="Of"/> and
/// <see cref="World"/>, which read the live bot and the live frontier (cached).
/// </summary>
public static class AutonomousChatFieldTalk
{
    /// <summary>The share of unprompted lines in a channel that come from here rather than from the written corpus.</summary>
    public static double Share(string channel) => channel switch
    {
        "faction" => 0.92,
        "say" => 0.9,
        "lfg" or "trade" => 0.9,
        "broadcast" => 0.85,
        "guild" => 0.9,
        _ => 0.0,
    };

    /// <summary>The frontier as one realm sees it: its keeps, those under attack, those lost and to whom, and the relics.</summary>
    public sealed record WorldView(string[] OwnKeeps, string[] AttackedKeeps, string[] LostKeeps, string[] LostTo, int RelicsHeld, int RelicsLost,
        string Frontier)
    {
        public static readonly WorldView Empty = new([], [], [], [], 0, 0, null);
    }

    /// <summary>What a line may say about its speaker.</summary>
    public sealed record Facts(long Id, eRealm Realm, string ClassName, int Level, string Zone, string Dungeon, bool InRvR, bool InDungeon,
        bool InTown, bool InGroup, int GroupSize, bool JustLeveled, bool JustDied, bool BagFull, bool AtCamp, string Monster, string Item,
        int ItemQuality, bool ItemIsGear, string Destination, int Bubbles, int LocalHour, bool IsHealer, WorldView World)
    {
        /// <summary>The guild's claimed keep, if any (guild lines).</summary>
        public string GuildKeep { get; init; }

        /// <summary>A guildmate who just levelled (guild lines: "gz bran").</summary>
        public string GuildmateDinged { get; init; }

        /// <summary>A guildmate who just logged in (guild lines: "o/ bran").</summary>
        public string GuildmateArrived { get; init; }

        /// <summary>Facts read from a live bot (<see cref="Of"/>): the group state below is then known, not guessed.</summary>
        public bool Live { get; init; }

        /// <summary>Whether a solo bot wants a group (awaiting group matchmaking, or out for rvr), or null when unknown.</summary>
        public bool? SeeksGroup { get; init; }

        /// <summary>Whether the bot's task is rvr (true), something else (false), or unknown (null): an rvr bot looks for an rvr group.</summary>
        public bool? RvrTask { get; init; }

        /// <summary>The post the bot's group could make (it is the leader of a party still assembling with seats free), or null.</summary>
        public AutonomousBotGroupCoordinator.RecruitPost Recruit { get; init; }

        /// <summary>The <see cref="Item"/>'s object type (<see cref="eObjectType"/>), or 0 when unknown (read from its name).</summary>
        public int ItemType { get; init; }

        /// <summary>Whether the speaker's class can use its <see cref="Item"/> by the server's rule (a live bot), or null when unknown.</summary>
        public bool? ItemUsable { get; init; }

        /// <summary>
        /// What killed the speaker in its recent death (1.1.3, after RC6), as AutonomousDeathDiagnostics.ChatKillerKind writes it:
        /// "monster", "guard", "pet" (an enemy's), "bot", "player", or null when unknown or no recent death. A death to a monster
        /// is told with the monster lines even in the frontier, one to an enemy with the frontier lines anywhere.
        /// </summary>
        public string KilledBy { get; init; }

        /// <summary>How many were on the speaker when it died (0: unknown): with one, no line claims several.</summary>
        public int Attackers { get; init; }

        /// <summary>The class of the enemy that killed the speaker, or null.</summary>
        public string KillerClass { get; init; }

        /// <summary>
        /// Whether the speaker said it was away ("brb", "afk") lately or died (live facts), or null when unknown: a "back" line
        /// only from one that was (RC6: "ok back" in /faction from a bot that had said neither).
        /// </summary>
        public bool? WasAway { get; init; }

        /// <summary>Whether the speaker lies dead now (live facts), or null when unknown: only a dead bot asks for a rez.</summary>
        public bool? Dead { get; init; }
    }

    /// <summary>Whether the facts' recent death was to an enemy (a player, a bot or an enemy's pet). Pure.</summary>
    public static bool KilledByEnemy(Facts f) => f?.KilledBy is "bot" or "player" or "pet";

    /// <summary>
    /// Whether a death is told as a frontier death: one to an enemy or a guard, or, when the killer is unknown, one in RvR (the
    /// old rule). A death to a monster is a monster death wherever it happened (RC6: a shard golem in the frontier was told
    /// "got caught by the bridge"). Pure.
    /// </summary>
    public static bool ToldAsRvrDeath(Facts f) =>
        f != null && (KilledByEnemy(f) || f.KilledBy == "guard" || f.KilledBy == null && f.InRvR);

    private const System.Text.RegularExpressions.RegexOptions DeathRx =
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled;

    // A crowd without a number: three or more ("zerged", "ran into a grp", "a full grp got me", "train of reds").
    private static readonly System.Text.RegularExpressions.Regex CrowdOfAttackers = new(
        @"\b(zerg|zerged|grp got me|full grp|ran into a (grp|zerg)|whole zerg|too many|a fg|grp of|train of reds)\b", DeathRx);

    // Several without a number: two or more ("they were waiting at the gate", "stealthers", "train of skeletons").
    private static readonly System.Text.RegularExpressions.Regex SeveralAttackers = new(
        @"\b(of them|they were waiting|stealthers|assassins|train of|linked)\b", DeathRx);

    private static readonly System.Text.RegularExpressions.Regex OneAttacker = new(@"\b(just one|just an? |one caster|one [a-z]+$|1 v 1)\b", DeathRx);

    private static readonly System.Text.RegularExpressions.Regex[] CountClaims =
    [
        new(@"\bhad (?<n>\d+) on me\b", DeathRx), new(@"\b(?<n>\d+) of them\b", DeathRx), new(@"\b(?<n>\d+) v 1\b", DeathRx),
        new(@"\b1 v (?<n>\d+)\b", DeathRx), new(@"\bx(?<n>\d+)\b", DeathRx), new(@"\bpulled (?<n>two|three|\d+)\b", DeathRx),
        new(@"\blike (?<n>\d+)\b", DeathRx), new(@"\b(?<n>\d+) or (?<m>\d+)\b", DeathRx), new(@"\bstealthers, (?<n>\d+)\b", DeathRx),
        new(@"\b(?<n>\d+) assassins\b", DeathRx), new(@"\bgrp of (?<n>\d+)\b", DeathRx), new(@"\b(?<n>\d+)\+", DeathRx),
    ];

    /// <summary>
    /// Whether a death line fits how many were on the teller (1.1.3, after RC6: one enemy bot, and the story said "had 2 on me
    /// before i could run"; two elder beeches, and "elder beeches x3, no chance"). A number it claims ("had 2 on me", "x3", "3
    /// of them", "4 v 1", "pulled two") is that many; a crowd ("zerged", "ran into a grp", "full grp") three or more; "they",
    /// "stealthers" or "train of ..." two or more; "just one" one. Unknown (0) allows any. Pure.
    /// </summary>
    public static bool FitsAttackers(string text, int attackers)
    {
        if (string.IsNullOrEmpty(text) || attackers <= 0)
            return true;
        foreach (System.Text.RegularExpressions.Regex claim in CountClaims)
        {
            System.Text.RegularExpressions.Match match = claim.Match(text);
            if (!match.Success)
                continue;
            int low = Count(match.Groups["n"].Value);
            int high = match.Groups["m"].Success ? Count(match.Groups["m"].Value) : match.Value.EndsWith('+') ? int.MaxValue : low;
            if (match.Value.Contains(" or so", StringComparison.Ordinal) || claim.ToString().Contains("grp of", StringComparison.Ordinal))
                high = low + 2;
            if (attackers < low || attackers > high)
                return false;
        }
        if (attackers < 3 && CrowdOfAttackers.IsMatch(text))
            return false;
        if (attackers < 2 && SeveralAttackers.IsMatch(text))
            return false;
        return attackers == 1 || !OneAttacker.IsMatch(text);

        static int Count(string word) => word switch { "two" => 2, "three" => 3, _ => int.TryParse(word, out int n) ? n : 0 };
    }

    private static readonly System.Text.RegularExpressions.Regex GuardWords = new(@"\bguards?\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex StealthWords = new(@"\b(stealth\w*|assassins?)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly HashSet<string> StealthClasses = new(StringComparer.OrdinalIgnoreCase)
        { "Infiltrator", "Scout", "Minstrel", "Nightshade", "Ranger", "Shadowblade", "Hunter" };

    /// <summary>
    /// Whether a frontier death line fits its killer (1.1.3, after RC6): no guard named for a death to an enemy, and no
    /// stealther for one to a known class that can't stealth; with the killer unknown, any. Also <see cref="FitsAttackers"/>. Pure.
    /// </summary>
    public static bool FitsDeath(string text, Facts f)
    {
        if (string.IsNullOrEmpty(text) || f == null)
            return true;
        if (!FitsAttackers(text, f.Attackers))
            return false;
        if (KilledByEnemy(f) && GuardWords.IsMatch(text))
            return false;
        if (f.KillerClass != null && !StealthClasses.Contains(f.KillerClass) && StealthWords.IsMatch(text))
            return false;
        // A death to an enemy costs no experience in DAoC: no "lost a bubble" for it, nor at 50.
        if ((KilledByEnemy(f) || f.Level >= 50) && text.Contains("bubble", StringComparison.OrdinalIgnoreCase))
            return false;
        return true;
    }

    /// <summary>
    /// A line and what it is: <paramref name="Kind"/> (lfg, lfm, wts, wtb, ask_xp, ask_group, ask_keep, inc, ding, death, social,
    /// camp, travel, rvr, relic, crafter, and the guild kinds), and <paramref name="Answers"/>, short answers that fit this very
    /// line, or null. An answer may name the answerer's own {cls}, {lvl} and {zone}; one marked {healer} only a healer gives
    /// (see <see cref="Answer"/>). <paramref name="Item"/> is the item a sale names.
    /// </summary>
    public sealed record Said(string Text, string Kind, string[] Answers, string Item = null)
    {
        /// <summary>The shape of the line ("ask_xp", "camp_free"), resting 3-6 minutes; null for calls, sales and events, which may repeat.</summary>
        public string Family { get; init; }

        /// <summary>The family and its subject ("camp_free:large skeletons").</summary>
        public string Idea { get; init; }

        /// <summary>The help question's frame (its shape across wordings), resting 10-15 minutes and capped at 4 an hour; null for others.</summary>
        public string Frame { get; init; }

        /// <summary>What the speaker says a while later when nobody answered ("still lf healer", "nm found one").</summary>
        public string[] Unanswered { get; init; }

        /// <summary>What the speaker says after an answer ("ty", "k x").</summary>
        public string[] Thanks { get; init; }

        /// <summary>
        /// The role a line asks for ("tank", "healer", "cc", "puller", "dps", or "class:Cleric"), or null: only a bot that fills
        /// it answers (owner 2026-11-08: "need a tank for sh barrows" drew "sec, me" and "cab, 50" from a cabalist).
        /// </summary>
        public string Role { get; init; }

        /// <summary>The levels a group ask is for (0: any): a joiner outside them doesn't say "me".</summary>
        public int LevelMin { get; init; }

        public int LevelMax { get; init; }

        /// <summary>The asker's class, for answers marked {same} ("any reavers on?" is answered "me" by a reaver only).</summary>
        public string SameClass { get; init; }

        /// <summary>
        /// Whether the line asks a question (it was written with a "?", which <see cref="Finish"/> sometimes drops). A question is
        /// answered whenever someone can answer it (owner's PC, RC3 and RC5 2026-11-09: 71 of 106 bot questions drew nothing).
        /// </summary>
        public bool Asks { get; init; }

        /// <summary>The place a question asks about ("any1 at bold?": Caer Boldiam; "anyone in hadrian's wall?"), for a live answer.</summary>
        public string Place { get; init; }
    }

    /// <summary>
    /// Whether a bot of this class and level may answer the line at all: it fills the role asked for and is in the levels
    /// asked for. Pure.
    /// </summary>
    public static bool CanAnswer(Said said, string className, int level, bool isHealer = false)
    {
        if (said == null)
            return false;
        if (said.LevelMax > 0 && (level < said.LevelMin || level > said.LevelMax))
            return false;
        return said.Role == null || Fills(className, said.Role, isHealer);
    }

    /// <summary>
    /// Whether a bot can give one of the line's answers at all: <see cref="CanAnswer"/>, and when every answer is marked {same}
    /// or {healer}, it is of the asker's class or a healer (RC5: "any reavers on? quick q" went to a bot of another class, which
    /// had nothing to say, so nobody answered). Pure.
    /// </summary>
    public static bool CanGiveAnswer(Said said, string className, int level, bool isHealer = false)
    {
        if (!CanAnswer(said, className, level, isHealer))
            return false;
        if (said.Answers == null || said.Answers.Length == 0)
            return true;
        if (said.Answers.All(a => a.StartsWith("{same}", StringComparison.Ordinal)) &&
            !string.Equals(className, said.SameClass, StringComparison.OrdinalIgnoreCase))
            return false;
        return isHealer || !said.Answers.All(a => a.StartsWith("{healer}", StringComparison.Ordinal));
    }

    /// <summary>Whether a line is a question that has answers, so it should get one (1.1.3, after RC5). Pure.</summary>
    public static bool WantsAnswer(Said said) => said?.Answers is { Length: > 0 } && said.Kind != "social" &&
                                                 (said.Asks || said.Text?.TrimEnd().EndsWith('?') == true);

    /// <summary>Whether a class fills a group role ("tank", "healer", "cc", "puller", "dps", "class:Druid"). Pure.</summary>
    public static bool Fills(string className, string role, bool isHealer = false)
    {
        if (role == null)
            return true;
        if (string.IsNullOrWhiteSpace(className) || !Enum.TryParse(className.Replace(" ", ""), true, out eCharacterClass cls))
            return false;
        if (role.StartsWith("class:", StringComparison.Ordinal))
            return string.Equals(cls.ToString(), role[6..], StringComparison.OrdinalIgnoreCase);
        return role switch
        {
            "healer" => cls is eCharacterClass.Cleric or eCharacterClass.Friar or eCharacterClass.Druid or eCharacterClass.Warden or
                eCharacterClass.Healer or eCharacterClass.Shaman,
            "tank" => BotPartyRoles.For(cls) == BotPartyRole.Tank,
            "cc" => cls is eCharacterClass.Sorcerer or eCharacterClass.Minstrel or eCharacterClass.Healer or eCharacterClass.Bard or
                eCharacterClass.Mentalist,
            "puller" => cls is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger || BotPartyRoles.For(cls) == BotPartyRole.Tank,
            "dps" => BotPartyRoles.For(cls) == BotPartyRole.Damage,
            _ => true,
        };
    }

    /// <summary>The role a need word names ("a tank" is "tank", "mezzer" is "cc", "druid" is "class:Druid"), or null for any.</summary>
    public static string RoleOf(string need) => need?.Replace("a ", "").Trim() switch
    {
        "healer" => "healer",
        "tank" => "tank",
        "mezzer" or "cc" => "cc",
        "puller" => "puller",
        "dps" => "dps",
        "cleric" => "class:Cleric",
        "druid" => "class:Druid",
        "shaman" => "class:Shaman",
        "bard" => "class:Bard",
        _ => null,
    };

    private static string[] Pool(params string[] answers) => answers;

    /// <summary>The kinds whose families are help or request frames (round 7), resting longer and capped per hour.</summary>
    private static readonly HashSet<string> HelpKinds = new(StringComparer.Ordinal) { "ask_xp", "class", "ask", "crafter", "camp", "ask_group", "travel", "help" };

    /// <summary>One way to say a kind of line: its text, its family, and the answers that fit it.</summary>
    private readonly record struct V(string Text, string Family, string[] Answers = null);

    /// <summary>
    /// A line from one of the ways, with its idea and its follow-ups. The idea is the kind and its subject ("ask_keep:caer
    /// renaris"), so every rewording of it rests together; with no subject, the way's family. "{s}" in a follow-up is the subject.
    /// </summary>
    private static Said Make(Random random, string kind, string subject, string[] unanswered, string[] thanks, params V[] ways)
    {
        V way = ways[random.Next(ways.Length)];
        return new Said(way.Text, kind, way.Answers)
        {
            Family = way.Family,
            // Round 8: every family is a frame (help questions, lfg and crafter lines, level questions alike), but a call to arms.
            Frame = way.Family != null && way.Family is not ("inc_call" or "g_inc") ? way.Family : null,
            Idea = way.Family == null ? null : subject == null ? way.Family : kind + ":" + subject,
            // "{s}" is the subject as said ("keltoi", "cleric"), not its key ("keltoi:4").
            Unanswered = unanswered?.Select(line => line.Replace("{s}", subject?.Split(':')[0] ?? string.Empty).Trim()).ToArray(),
            Thanks = way.Answers == null ? null : thanks,
            Asks = way.Text.TrimEnd().EndsWith('?'),
        };
    }

    // Answer pools by what a line asks. "me" and "x" answer only a line that recruits (round 5).
    private static readonly string[] Joins = ["me", "x", "me pls", "x me", "can i come", "me, {cls} {lvl}", "{lvl} {cls} here", "{cls} {lvl}, x?", "inv pls", "sec, me"];
    private static readonly string[] SeekThanks = ["ty, inv", "k x", "inv sent", "ty", "kk inv", "got one ty"];
    private static readonly string[] SeekStill = ["still lf {s}", "anyone for {s}?", "nvm found one", "nm", "solo it is then", "still looking for {s}",
        "{s} anyone?", "nm got a grp", "nvm, going solo", "bump {s}"];

    /// <summary>The skeletons, families, ideas and items a realm used lately, so it doesn't hear the same thing twice in an evening.</summary>
    public sealed class Memory
    {
        private readonly int _size;
        private readonly string[] _ring;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private int _next;
        private readonly Dictionary<string, long> _items = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, long> _used = new(StringComparer.Ordinal);
        private long _lastWritten = long.MinValue / 2;

        /// <param name="size">How many recent lines it holds: a realm's evening is several thousand (round 4: a 3-hour run).</param>
        public Memory(int size = 4096)
        {
            _size = Math.Max(16, size);
            _ring = new string[_size];
        }

        /// <summary>How long one item rests after it was advertised (Beta 1.1.2, round 4: an hour).</summary>
        public const long ItemRestMs = 60 * 60_000;

        /// <summary>A written musing at most once in 20 minutes per realm (round 5).</summary>
        public const long WrittenGapMs = 20 * 60_000;

        /// <summary>How long a family rests: 3-6 minutes, the same for a family each time.</summary>
        public static long FamilyRestMs(string family) => 3 * 60_000 + (long)(StableFraction(family) * 3 * 60_000);

        /// <summary>How long an idea rests: 30-60 minutes, the same for an idea each time.</summary>
        public static long IdeaRestMs(string idea) => 30 * 60_000 + (long)(StableFraction(idea) * 30 * 60_000);

        private static double StableFraction(string key)
        {
            uint hash = 2166136261;
            foreach (char c in key ?? string.Empty)
                hash = (hash ^ c) * 16777619;
            return hash % 10_000 / 10_000.0;
        }

        public bool Seen(string skeleton)
        {
            lock (_ring)
                return _seen.Contains(skeleton);
        }

        public void Add(string skeleton)
        {
            lock (_ring)
            {
                if (!_seen.Add(skeleton))
                    return;
                if (_ring[_next] != null)
                    _seen.Remove(_ring[_next]);
                _ring[_next] = skeleton;
                _next = (_next + 1) % _size;
            }
        }

        /// <summary>True (and remembered) when the item wasn't advertised here in the last hour.</summary>
        public bool TryItem(string item, long now)
        {
            if (string.IsNullOrWhiteSpace(item))
                return true;
            lock (_ring)
            {
                if (_items.TryGetValue(item, out long at) && now - at < ItemRestMs && now >= at)
                    return false;
                if (_items.Count > 2048)
                    _items.Clear();
                _items[item] = now;
                return true;
            }
        }

        /// <summary>Whether a family (3-6 min) or an idea (30-60 min) was used too lately to be heard again.</summary>
        public bool Rested(string family, string idea, long now)
        {
            lock (_ring)
                return Resting("f:" + family, family, now, FamilyRestMs) || Resting("i:" + idea, idea, now, IdeaRestMs);
        }

        private bool Resting(string key, string name, long now, Func<string, long> rest) =>
            name != null && _used.TryGetValue(key, out long at) && now >= at && now - at < rest(name);

        /// <summary>A family and an idea were just used.</summary>
        public void Use(string family, string idea, long now)
        {
            lock (_ring)
            {
                if (_used.Count > 8192)
                    _used.Clear();
                if (family != null)
                    _used["f:" + family] = now;
                if (idea != null)
                    _used["i:" + idea] = now;
            }
        }

        private readonly Dictionary<string, Queue<long>> _frames = new(StringComparer.Ordinal);

        /// <summary>How long a help frame rests: 10-15 minutes (round 7: "good xp for..." 14 times in three hours).</summary>
        public static long FrameRestMs(string frame) => 10 * 60_000 + (long)(StableFraction(frame) * 5 * 60_000);

        /// <summary>A frame is heard at most this often an hour per realm.</summary>
        public const int FramesPerHour = 4;

        /// <summary>Whether a help frame (a question's shape across its wordings) may be heard now: rested, and under its hourly cap.</summary>
        public bool FrameAllowed(string frame, long now)
        {
            if (frame == null)
                return true;
            lock (_ring)
            {
                if (!_frames.TryGetValue(frame, out Queue<long> uses))
                    return true;
                while (uses.Count > 0 && (now - uses.Peek() >= 60 * 60_000 || now < uses.Peek()))
                    uses.Dequeue();
                if (uses.Count >= FramesPerHour)
                    return false;
                long last = uses.Count == 0 ? long.MinValue / 2 : uses.Last();
                return now - last >= FrameRestMs(frame);
            }
        }

        /// <summary>A help frame was just heard.</summary>
        public void UseFrame(string frame, long now)
        {
            if (frame == null)
                return;
            lock (_ring)
            {
                if (!_frames.TryGetValue(frame, out Queue<long> uses))
                {
                    if (_frames.Count > 4096)
                        _frames.Clear();
                    _frames[frame] = uses = new Queue<long>();
                }
                uses.Enqueue(now);
            }
        }

        private readonly Dictionary<string, long> _life = new(StringComparer.Ordinal);

        /// <summary>One life line (weather, school, work, pets, check-ins) per guild per evening (round 7).</summary>
        public const long LifeGapMs = 6 * 60 * 60_000;

        /// <summary>Whether a life line may be said now (none in the last six hours).</summary>
        public bool LifeAllowed(long now)
        {
            lock (_ring)
                return !_life.TryGetValue("life", out long at) || now - at >= LifeGapMs || now < at;
        }

        /// <summary>A life line was just said.</summary>
        public void NoteLife(long now)
        {
            lock (_ring)
                _life["life"] = now;
        }

        /// <summary>Whether a written musing may be said now (none in the last 20 minutes).</summary>
        public bool WrittenAllowed(long now)
        {
            lock (_ring)
                return now - _lastWritten >= WrittenGapMs || now < _lastWritten;
        }

        /// <summary>A written musing was just said.</summary>
        public void NoteWritten(long now)
        {
            lock (_ring)
                _lastWritten = now;
        }
    }

    private static readonly Dictionary<eRealm, Memory> Memories = new();

    /// <summary>The live memory of a realm's field lines.</summary>
    public static Memory MemoryOf(eRealm realm)
    {
        lock (Memories)
        {
            if (!Memories.TryGetValue(realm, out Memory memory))
                Memories[realm] = memory = new Memory();
            return memory;
        }
    }

    private static readonly Dictionary<string, Memory> GuildMemories = new(StringComparer.Ordinal);

    /// <summary>The live memory of one guild's field lines.</summary>
    public static Memory MemoryOfGuild(string guildId)
    {
        lock (GuildMemories)
        {
            if (!GuildMemories.TryGetValue(guildId ?? string.Empty, out Memory memory))
            {
                if (GuildMemories.Count > 512)
                    GuildMemories.Clear();
                GuildMemories[guildId ?? string.Empty] = memory = new Memory(512);
            }
            return memory;
        }
    }

    /// <summary>
    /// A field line for a channel (faction, say, lfg, trade, broadcast, guild), or null when the facts give nothing to say
    /// there. Tries a few draws for one whose text, family, idea and item the realm hasn't heard lately. Pure.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex Capital = new(@"^(city of camelot|camelot|jordheim|tir na nog)$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Whether a line fits a bot standing in its zone when that zone is a capital: it doesn't hunt, camp, group, quest or port
    /// "in" the capital ("lf 2 more for city of camelot", "grinding city of camelot at 50"). Any line fits elsewhere. Pure.
    /// </summary>
    public static bool FitsCapital(string text, string zone)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(zone) || !Capital.IsMatch(zone.Trim()))
            return true;
        string t = text.ToLowerInvariant();
        string z = zone.Trim().ToLowerInvariant();
        string names = System.Text.RegularExpressions.Regex.Escape(z) + (z.StartsWith("city of ", StringComparison.Ordinal)
            ? "|" + System.Text.RegularExpressions.Regex.Escape(z["city of ".Length..]) : string.Empty);
        return !System.Text.RegularExpressions.Regex.IsMatch(t,
            @"\b(camp|camps|camping|xp|xping|duo|group|grp|quest|port|guildies|more|grind|grinding|hunt|hunting|pull|pulling|mobs?)\b[^,?.!]*\b(in|near|to|for|at|around)\s+(" + names + @")\b" +
            @"|^(" + names + @")\s*(anyone|camp|grp|group)|\bgrinding\s+(" + names + @")\b|\?\s*(" + names + @")$");
    }

    public static Said Line(string channel, Facts facts, Memory memory, Random random, long now = 0, AutonomousChatStance stance = null)
    {
        if (facts == null)
            return null;
        // 1.1.3, after RC5: a post the bot's state has outrun is closed where it was made, once, if it was lately: "nm got a grp" after an "lfg"
        // once the bot is in a group, "nvm, full" after an "lfm" once its party set off. Its story then ends.
        if (stance?.Outrun(facts.Id, channel, facts.InGroup, !facts.Live || facts.Recruit != null, now) is { } outrun &&
            outrun.AgeMs < OutrunCloseMs)
            return new Said(Finish(outrun.Seek == "lfg" ? Pick(random, "nm got a grp", "nvm, grouped", "nm found a grp ty", "got a grp, ty")
                : facts.GroupSize >= 8 ? Pick(random, "nvm, full", "grp full ty", "full now, ty", "nm full")
                : Pick(random, "nm, heading out", "nvm, going as is", "nm, we're off"), random), "social", null) { Family = "outrun" };
        // A bot's group posts keep the dungeon it named in the last 30 minutes (a bot outside a dungeon draws one for its level
        // each time: "lfm barrows" then "lf1m ... stonehenge" four minutes apart).
        string keptDungeon = stance?.DungeonOf(facts.Id, now);
        if (keptDungeon != null && !facts.InDungeon)
            facts = facts with { Dungeon = keptDungeon };
        for (int attempt = 0; attempt < 16; attempt++)
        {
            Said said = channel switch
            {
                "lfg" => Lfg(facts, random),
                "trade" => Trade(facts, random),
                "say" => Say(facts, random),
                "broadcast" => random.NextDouble() switch
                {
                    < 0.5 => Trade(facts, random),
                    < 0.75 => Lfg(facts, random),
                    _ => Say(facts with { InTown = true }, random, broadcast: true),
                },
                "guild" => Guild(facts, random),
                _ => Faction(facts, random),
            };
            if (said == null || string.IsNullOrWhiteSpace(said.Text))
                continue;
            said = said with { Text = Finish(said.Text, random) };
            // 1.1.3, after RC5: what fits the speaker. A PvE line names no zone of an enemy realm ("where do i hand in the
            // uppland quest?" from an Albion bot), and a group post keeps the speaker's story: looking for a group or forming
            // one, one dungeon, by one name.
            if (NamesForeignZone(said, facts) || !FitsGroupState(said.Text, facts))
                continue;
            // 1.1.3, after RC6: "back" only from a bot that said it was away or died ("ok back" from one that had said neither),
            // and "tonight" is "today" by day ("busy tonight" at 10:44 AM), in the line and in its answers.
            if (!FitsAway(said.Text, facts))
                continue;
            // RC11 on the owner's PC: "city of camelot anyone", "where do 41s camp in city of camelot?". A capital is no place
            // to hunt, camp or group in.
            if (!FitsCapital(said.Text, facts.Zone))
                continue;
            said = AtHour(said, facts.LocalHour);
            string named = NamedDungeon(said.Text, facts.Dungeon);
            if (stance != null)
            {
                if (!stance.Allows(facts.Id, said.Text, named, now))
                    continue;
                if (named != null && stance.DungeonWordOf(facts.Id, now) is { } word)
                    said = said with { Text = UseDungeonWord(said.Text, named, word) };
            }
            // A group ask is answered by bots near the asker's level (or in the range it names).
            if (said.LevelMax == 0 && (said.Kind == "ask_group" || ReferenceEquals(said.Answers, Joins)))
                said = said with { LevelMin = Math.Max(1, facts.Level - 6), LevelMax = Math.Min(50, facts.Level + 6) };
            string skeleton = AutonomousChatFiller.Skeleton(said.Text);
            // Short lines ("brb", "back", "lol") may repeat, as in the live near-copy rule; anything longer once an evening.
            bool reaction = skeleton.IndexOf(' ') < 0 || skeleton.Split(' ').Length <= 2 && skeleton.Length <= 10;
            if (memory != null && (!reaction && memory.Seen(skeleton) || memory.Rested(said.Family, said.Idea, now) || !memory.FrameAllowed(said.Frame, now)))
                continue;
            // One item advertised at most once an hour (round 4: three of 120 lines sold the same ring).
            if (said.Item != null && memory != null && !memory.TryItem(said.Item, now))
                continue;
            memory?.Add(skeleton);
            memory?.Use(said.Family, said.Idea, now);
            memory?.UseFrame(said.Frame, now);
            stance?.Note(facts.Id, said.Text, named, named == null ? null : DungeonWordIn(said.Text, named), now, channel);
            return said;
        }
        return null;
    }

    // ---- what fits the speaker (1.1.3, after RC5) -----------------------------------------------------------------------------

    /// <summary>How long after a group post a bot closes it ("nm got a grp") when its state outran it: its whole story window.</summary>
    public const long OutrunCloseMs = AutonomousChatStance.WindowMs;

    /// <summary>
    /// Whether a group post fits the speaker's real state (1.1.3, after RC5): an "lfg" only from a bot out of a group that
    /// wants one, an "lfm" only from a group that is recruiting (live facts; facts without the live state allow any). Pure.
    /// </summary>
    public static bool FitsGroupState(string text, Facts f)
    {
        if (f == null)
            return true;
        return AutonomousChatStance.SeekOf(text) switch
        {
            // RC7 on the owner's PC: a cabalist out in Odin's Gate for rvr posted "lfg Stonehenge Barrows or anywhere with xp".
            "lfg" => !f.InGroup && f.SeeksGroup != false && FitsTask(text, f.RvrTask),
            "lfm" => !f.Live || f.Recruit != null,
            _ => true,
        };
    }

    private static readonly System.Text.RegularExpressions.Regex XpWords = new(
        @"\b(xp|exp|bubbles?|camp|lvling|leveling|levelling|dungeon|barrows|fogou|catacombs|tomb|mine|keltoi|spindel|tepok|krondon|mithra)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex RvrWords = new(
        @"\b(rvr|realm points|rps?|keep|keeps|frontier|emain|hadrian'?s|odin'?s|zerg|8v8|fg)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Whether a group post fits the bot's task: an xp or dungeon post not from a bot out for rvr, an rvr post not from a bot that
    /// is levelling; unknown fits either. Pure.
    /// </summary>
    public static bool FitsTask(string text, bool? rvrTask)
    {
        if (rvrTask == null || string.IsNullOrWhiteSpace(text))
            return true;
        bool xp = XpWords.IsMatch(text), rvr = RvrWords.IsMatch(text);
        return rvrTask.Value ? !xp || rvr : !rvr || xp;
    }

    /// <summary>
    /// Whether a "back" line ("back", "ok back", "re") fits the speaker (1.1.3, after RC6): only one that said it was away or died
    /// lately (<see cref="Facts.WasAway"/>; unknown allows it), as in /group (AutonomousChatEngagement.FitsGroup). Pure.
    /// </summary>
    public static bool FitsAway(string text, Facts f) => f?.WasAway != false || !AutonomousChatEngagement.SaysBack(text);

    /// <summary>A line, its answers and its follow-ups as said at the PC's local hour ("tonight" by day reads "today"). Pure.</summary>
    public static Said AtHour(Said said, int localHour)
    {
        if (said == null)
            return null;
        // A pool with nothing to change stays the same array (Line knows the joiners' pool by reference).
        string[] Map(string[] pool)
        {
            if (pool == null)
                return null;
            string[] mapped = pool.Select(text => AutonomousBotVoiceCorpus.ForTimeOfDay(text, localHour)).ToArray();
            return mapped.SequenceEqual(pool, StringComparer.Ordinal) ? pool : mapped;
        }
        return said with
        {
            Text = AutonomousBotVoiceCorpus.ForTimeOfDay(said.Text, localHour), Answers = Map(said.Answers), Unanswered = Map(said.Unanswered),
            Thanks = Map(said.Thanks),
        };
    }

    /// <summary>The highest level that asks where its own class trainer is: by 20 a character has trained there a dozen times.</summary>
    public const int TrainerAskMaxLevel = 20;

    /// <summary>The kinds that may name an enemy realm's frontier zone: the frontier's own talk, and a death or a group post there.</summary>
    private static readonly HashSet<string> FrontierKinds = new(StringComparer.Ordinal) { "inc", "ask_keep", "relic", "rvr", "rally", "keeps", "death", "lfg", "lfm" };

    /// <summary>Whether a zone is another realm's frontier for this speaker (a zone of no realm, or its own, is not).</summary>
    public static bool IsForeignZone(eRealm realm, string zone)
    {
        eRealm home = AutonomousChatLiveAnswers.FrontierHome(zone?.Trim());
        return home != eRealm.None && realm != eRealm.None && home != realm;
    }

    /// <summary>
    /// Whether a line names an enemy realm's zone where only a home zone fits (owner's PC, RC3 2026-11-09, all Albion: "where do
    /// i hand in the uppland quest?", "which stable for uppland?", "grinding odin's gate at 29", "back in uppland"). Talk of the
    /// frontier itself may name it ("inc", keeps, relics, a death there, "lfg 50 merc, odin's gate"), but not for xp or a duo.
    /// Pure.
    /// </summary>
    public static bool NamesForeignZone(Said said, Facts facts)
    {
        if (said == null || facts == null)
            return false;
        bool foreignHere = IsForeignZone(facts.Realm, facts.Zone), foreignThere = IsForeignZone(facts.Realm, facts.Destination);
        if (!foreignHere && !foreignThere)
            return false;
        string text = said.Text?.ToLowerInvariant() ?? string.Empty;
        bool names = foreignHere && ZoneWords(facts.Zone).Any(word => ContainsWord(text, word)) ||
                     foreignThere && ZoneWords(facts.Destination).Any(word => ContainsWord(text, word));
        if (!names)
            return false;
        if (!FrontierKinds.Contains(said.Kind ?? string.Empty))
            return true;
        return said.Kind is "lfg" or "lfm" && System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(?:xp|duo|camp)\b");
    }

    /// <summary>The ways <see cref="ShortZone"/> writes a zone ("jamtland mountains", "jamtland mtns").</summary>
    private static IEnumerable<string> ZoneWords(string zone)
    {
        if (string.IsNullOrWhiteSpace(zone))
            yield break;
        string z = zone.Trim().ToLowerInvariant();
        if (z.StartsWith("the ", StringComparison.Ordinal))
            z = z[4..];
        yield return z;
        string shortened = z.Replace("mountains", "mtns").Replace("mountain", "mtn").Replace(" north", " n").Replace(" south", " s");
        if (shortened != z)
            yield return shortened;
    }

    private static bool ContainsWord(string text, string word) =>
        System.Text.RegularExpressions.Regex.IsMatch(text, @"(?<![a-z0-9'])" + System.Text.RegularExpressions.Regex.Escape(word) + @"(?![a-z0-9'])");

    /// <summary>A zone word for a PvE line about the speaker's own place: "frontier" in an enemy realm's frontier zone.</summary>
    private static string HomeZoneWord(Facts f, Random random) =>
        f != null && IsForeignZone(f.Realm, f.Zone) ? "frontier" : ShortZone(f?.Zone, random);

    /// <summary>The ways chat names a dungeon: its words ("barrows", "sh barrows", "stonehenge") and its name.</summary>
    private static IEnumerable<string> DungeonAliases(string dungeon)
    {
        if (string.IsNullOrWhiteSpace(dungeon))
            return [];
        string name = dungeon.Trim().ToLowerInvariant();
        IEnumerable<string> words = DungeonWords.TryGetValue(dungeon.Trim(), out string[] list) ? list : [];
        return words.Append(name).Distinct().OrderByDescending(word => word.Length);
    }

    /// <summary>The dungeon a line names (the speaker's <paramref name="dungeon"/>, by any of its names), or null. Pure.</summary>
    public static string NamedDungeon(string text, string dungeon) =>
        dungeon != null && text != null && DungeonAliases(dungeon).Any(word => ContainsWord(text.ToLowerInvariant(), word)) ? dungeon : null;

    private static string DungeonWordIn(string text, string dungeon) =>
        DungeonAliases(dungeon).FirstOrDefault(word => ContainsWord(text.ToLowerInvariant(), word));

    /// <summary>The line with every name of the dungeon written as <paramref name="word"/> ("stonehenge" -> "barrows").</summary>
    private static string UseDungeonWord(string text, string dungeon, string word)
    {
        string pattern = @"(?<![a-z0-9'])(?:" + string.Join("|", DungeonAliases(dungeon).Select(System.Text.RegularExpressions.Regex.Escape)) + @")(?![a-z0-9'])";
        return System.Text.RegularExpressions.Regex.Replace(text, pattern, word);
    }

    /// <summary>
    /// Whether a bot's own item is one its class can use (the server's rule for a live bot, <see cref="Facts.ItemUsable"/>, else
    /// <see cref="AutonomousChatGear.CanUse"/>); a focus staff only for a caster. Only then does a line pair the item with the
    /// bot's class ("99% adroit focus staff any good for pally?" on the owner's PC, RC5). Pure.
    /// </summary>
    public static bool UsesItem(Facts f)
    {
        if (f?.Item == null)
            return false;
        if (AutonomousChatGear.IsFocusStaff(f.Item) && !AutonomousChatGear.IsFocusCaster(f.ClassName))
            return false;
        return f.ItemUsable ?? AutonomousChatGear.CanUse(f.ClassName, f.Level, f.ItemType, f.Item);
    }

    /// <summary>
    /// Another bot's short answer to a field line, in its own words: "me, thane 31" names the answerer's class and level; an
    /// answer marked {healer} comes only from a healer. Tries a few for one the channel hasn't heard this evening (three words or
    /// more; "me" and "omw" may repeat); when all of them have been heard, one not heard in the last 30-60 minutes (1.1.3, after
    /// RC5: each answer was said once an evening, so a question asked a fourth time had none left and went unanswered). Null
    /// for none. Pure.
    /// </summary>
    public static string Answer(Said said, Facts answerer, Memory memory, Random random, long now = 0)
    {
        if (said?.Answers == null || said.Answers.Length == 0)
            return null;
        // Nobody answers for a role or level they don't have (owner 2026-11-08: "cab, 50" to "need a tank").
        if (answerer != null && !CanAnswer(said, answerer.ClassName, answerer.Level, answerer.IsHealer))
            return null;
        string asked = QuestionKind(said.Text);
        string rested = null;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            string text = said.Answers[random.Next(said.Answers.Length)];
            if (text.StartsWith("{same}", StringComparison.Ordinal))
            {
                if (answerer == null || said.SameClass == null || !string.Equals(answerer.ClassName, said.SameClass, StringComparison.OrdinalIgnoreCase))
                    continue;
                text = text["{same}".Length..];
            }
            if (text.StartsWith("{healer}", StringComparison.Ordinal))
            {
                if (answerer?.IsHealer != true)
                    continue;
                text = text["{healer}".Length..];
            }
            if (answerer != null)
                text = text.Replace("{cls}", ShortClass(answerer.ClassName, random)).Replace("{lvl}", answerer.Level.ToString())
                    .Replace("{zone}", ShortZone(answerer.Zone, random));
            else if (text.Contains('{'))
                continue;
            text = Finish(text, random);
            // Round 8: the answer gives what the question asks for.
            if (!Fits(text, asked))
                continue;
            string skeleton = AutonomousChatFiller.Skeleton(text);
            if (memory != null && skeleton.Split(' ').Length > 2)
            {
                if (memory.Seen(skeleton))
                {
                    if (rested == null && !memory.Rested(null, AnswerIdea(skeleton), now))
                        rested = text;
                    continue;
                }
                memory.Add(skeleton);
                memory.Use(null, AnswerIdea(skeleton), now);
            }
            return text;
        }
        if (rested != null)
        {
            memory?.Use(null, AnswerIdea(AutonomousChatFiller.Skeleton(rested)), now);
            return rested;
        }
        return asked == null ? null : AnswerFor(asked, answerer, random) is { } plain ? Finish(plain, random) : null;
    }

    /// <summary>The idea an answer rests under (30-60 minutes, <see cref="Memory.IdeaRestMs"/>) once the evening has heard it.</summary>
    private static string AnswerIdea(string skeleton) => "answer:" + skeleton;

    /// <summary>A follow-up from the speaker: thanks after an answer, or "still lf"/"nm" when nobody answered. Null for none. Pure.</summary>
    public static string FollowUp(Said said, bool answered, Random random, Memory memory = null, long now = 0)
    {
        string[] pool = answered ? said?.Thanks : said?.Unanswered;
        if (pool == null || pool.Length == 0)
            return null;
        // Round 5: the same follow-up ("nvm", "anyone?") isn't heard again in the realm for a while.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            string text = Finish(pool[random.Next(pool.Length)], random);
            string key = "u:" + text;
            // Its frame too: "still lf...", "nobody? ok..." at most four an hour (round 7).
            string frame = "fu:" + string.Join(' ', AutonomousChatIntentModel.Words(text).Take(2));
            string skeleton = AutonomousChatFiller.Skeleton(text);
            bool once = skeleton.Split(' ').Length > 2;
            if (memory != null && (memory.Rested(null, key, now) || !memory.FrameAllowed(frame, now) || once && memory.Seen(skeleton)))
                continue;
            memory?.Use(null, key, now);
            memory?.UseFrame(frame, now);
            if (once)
                memory?.Add(skeleton);
            return text;
        }
        return null;
    }

    /// <summary>How likely another bot answers a field line that has answers.</summary>
    public static double AnswerChance(string kind) => kind switch
    {
        "ask_keep" or "relic" => 0.7,
        "ask_xp" or "travel" => 0.6,
        "inc" => 0.55,
        "ding" => 0.65,
        "death" => 0.45,
        "social" => 0.5,
        "ask_group" or "crafter" => 0.6,
        "camp" or "rvr" or "keeps" or "class" => 0.5,
        "rally" => 0.55,
        "help" => 0.6,
        "raid" or "guild_group" or "guild_craft" or "guild_sell" => 0.55,
        "guild_ding" or "guild_hello" => 0.55,
        _ => 0.0,
    };

    /// <summary>How likely the speaker follows up its own line: thanks after an answer, or a "still lf" when none came.</summary>
    public static double FollowUpChance(bool answered) => answered ? 0.55 : 0.5;

    // ---- the channels --------------------------------------------------------------------------------------------------------

    private static Said Lfg(Facts f, Random random)
    {
        if (f.Level < 5)
            return null;
        string cls = ShortClass(f.ClassName, random), lvl = f.Level.ToString(), dun = ShortDungeon(f.Dungeon, random);
        // A group with room looks for more; a rvr-level bot looks for a rvr group now and then. 1.1.3, after RC5: posts follow
        // the bot's real state. A bot in a group never posts "lfg"; only a group that really has seats and lacks someone posts
        // "lfm" (Facts.Recruit, from the group coordinator: the leader of a party still assembling), naming the role it lacks,
        // its camp and its levels. Facts without the live state (Recruit and SeeksGroup unknown) keep the old draw.
        if (f.InGroup)
        {
            AutonomousBotGroupCoordinator.RecruitPost post = f.Recruit;
            if (post == null && (f.Live || f.GroupSize >= 8 || f.GroupSize <= 1))
                return null;
            int missing = post?.Missing ?? Math.Clamp(8 - f.GroupSize, 1, 4);
            string need = post == null ? Pick(random, "heal", "healer", "tank", "cc", "mezzer", "dps", "any", "caster", "support")
                : post.Need switch
                {
                    "healer" => Pick(random, "heal", "healer"),
                    "cc" => Pick(random, "cc", "mezzer"),
                    _ => post.Need,
                };
            string band = post == null ? $"{Math.Max(1, f.Level - 3)}-{Math.Min(50, f.Level + 3)}"
                : post.Low == post.High ? post.Low.ToString() : $"{post.Low}-{post.High}";
            string where = post?.Where == null ? dun ?? ShortZone(f.Zone, random)
                : post.WhereIsDungeon ? ShortDungeon(post.Where, random) : ShortZone(post.Where, random);
            string xpGrp = (post?.High ?? f.Level) < 50 ? "xp grp" : "grp";
            Said lfm = new(Pick(random,
                $"lf{missing}m {where}", $"lf{missing}m {need} {where}", $"lfm {where} {band}", $"lf1m {need} {where} pst",
                $"lfm {band} {where}, need {need}", $"lf {need} for {where} pst", $"{where} grp lf{missing}m", $"lf{missing}m {band} {xpGrp} {where}",
                $"need {need} {where} x me", $"lf1m {need}, {where}, {band}"), "lfm", null);
            return post == null ? lfm : lfm with
            {
                Role = RoleOf(post.Need),
                LevelMin = Math.Max(1, post.Low - 3),
                LevelMax = Math.Min(50, post.High + 3),
            };
        }
        // A solo bot that doesn't want a group (a solo task) doesn't post one.
        if (f.SeeksGroup == false)
            return null;
        if ((f.InRvR || f.Level >= 40) && random.NextDouble() < 0.35)
            return new Said(Pick(random,
                $"lfg rvr {lvl} {cls}", $"{lvl} {cls} lf rvr grp", $"any rvr grps lf {cls}?", $"lf rvr grp {cls} {lvl} pst",
                $"{cls} lfg rvr", $"lfg {f.World?.Frontier?.ToLowerInvariant() ?? "rvr"} {cls}"), "lfg", null);
        string zone = ShortZone(f.Zone, random);
        // No xp at the level cap: a 50 looks for a group, not an xp group.
        string xp = f.Level < 50 ? "xp " : string.Empty;
        // Round 8: the terse posts ("lfg 38 thane") may repeat as on any server; the sentence shapes are frames, rested and capped.
        return Make(random, "lfg", cls + ":" + lvl, null, null,
            new V($"lfg {lvl} {cls}", null), new V($"{lvl} {cls} lfg", null), new V($"lfg {lvl} {cls} pst", null), new V($"lf grp {lvl} {cls}", null),
            new V($"{cls} {lvl} lfg", null), new V(dun != null ? $"lfg {dun} {lvl} {cls}" : $"lfg {lvl} {cls} any", null),
            new V(dun != null ? $"{lvl} {cls} lfg {dun}" : $"{lvl} {cls} lfg {xp}".TrimEnd(), null), new V($"{lvl} {cls} lf {xp}grp", null),
            new V($"lfg {lvl} {cls}, {zone}", null), new V($"lfg {cls} {lvl} /t", null), new V($"{cls} lfg {lvl}, x me", null),
            new V($"any grps for {lvl} {cls}?", "lfg_anygrps"), new V($"{lvl} {cls} lf duo {zone}", "lfg_duo"), new V($"{lvl} {cls} lfg anything", "lfg_anything"),
            new V($"any room for a {lvl} {cls}?", "lfg_room"), new V($"{cls} {lvl} looking for a grp", "lfg_looking"), new V($"lf {xp}grp, {lvl} {cls}", "lfg_xpgrp"),
            new V($"{lvl} {cls} free for grps", "lfg_free"), new V($"{lvl} {cls} wants a grp in {dun ?? zone}", "lfg_wants"));
    }

    private static Said Trade(Facts f, Random random)
    {
        string item = f.Item?.ToLowerInvariant();
        bool sell = item != null && (f.BagFull || random.NextDouble() < 0.6);
        if (sell)
        {
            // A quality and a price only for gear: "wts 99% chain legs 2p", but "wts spider silk offers".
            string quality = f.ItemIsGear && f.ItemQuality >= 90 && random.NextDouble() < 0.6 ? f.ItemQuality + "% " : string.Empty;
            string price = f.ItemIsGear ? PriceFor(f.Level, random) : Pick(random, "cheap", "offers", "make offer", "cheap");
            return new Said(Pick(random,
                $"wts {quality}{item} /send", $"wts {quality}{item} pst", $"wts {quality}{item} {price}", $"selling {quality}{item} cheap",
                $"wts {quality}{item}, {price} obo", $"wts {quality}{item} /t me", $"{quality}{item} for sale pst", $"wts {item} offers",
                $"wts {quality}{item} x me"), "wts", null, item);
        }
        string want = WantFor(f, random);
        // Asked in /say in town too, where a thread answers it (1.1.3, after RC5).
        string[] seller = Pool("try the broker", $"merchants in {CapitalWord(f.Realm)}", "crafters can make it, pst one", "nope sry");
        return Make(random, "wtb", want, null, null,
            new V($"wtb {want} pst", null), new V($"wtb {want}", null),
            new V(want.Any(char.IsDigit) ? $"wtb {want} asap" : $"wtb {want} for {f.Level} {ShortClass(f.ClassName, random)}", want.Any(char.IsDigit) ? null : "wtb_for"),
            new V($"anyone selling {want}?", "wtb_anyone", seller), new V($"lf {want} pst", null), new V($"wtb {want}, paying ok", "wtb_paying"),
            new V($"any1 selling {want}", "wtb_any1"), new V($"need {want}, anyone?", "wtb_need", seller), new V($"who has {want}?", "wtb_who", seller),
            new V($"buying {want}", "wtb_buying"), new V($"looking for {want}, will pay", "wtb_looking"));
    }

    private static Said Faction(Facts f, Random random)
    {
        var options = new List<(Func<Said> Make, double Weight)>(12);
        WorldView w = f.World ?? WorldView.Empty;
        if (w.AttackedKeeps.Length > 0)
            options.Add((() => Inc(f, w, random), f.InRvR ? 4 : f.Level >= 35 ? 1 : 0.3));
        if (w.LostKeeps.Length > 0)
            options.Add((() => AskKeep(f, w, random), 1.2));
        if (w.RelicsLost > 0 || w.RelicsHeld > 0)
            options.Add((() => Relic(f, w, random), 0.6));
        if (f.InRvR || f.Level >= 40)
            options.Add((() => Rvr(f, w, random), f.InRvR ? 3 : 1));
        if (f.JustLeveled)
            options.Add((() => Ding(f, random), 8));
        if (f.JustDied)
            options.Add((() => Death(f, random), 6));
        if (!f.InRvR && f.Level < 50)
        {
            options.Add((() => AskXp(f, random), 0.6));
            options.Add((() => AskGroup(f, random), 3));
            options.Add((() => NeedClass(f, random), 1.2));
        }
        if (f.AtCamp && f.Monster != null)
            options.Add((() => Camp(f, random), 1.5));
        if (f.Destination != null)
            options.Add((() => Travel(f, random), 1.5));
        options.Add((() => Crafter(f, random), 0.8));
        options.Add((() => PriceCheck(f, random), 0.8));
        options.Add((() => Status(f, random), 2.0));
        options.Add((() => AskAround(f, random), 1.2));
        // Round 6: more kinds that are true and varied (the owner's "never quiet" at 1,000 bots).
        if (w.AttackedKeeps.Length > 0 && (f.InRvR || f.Level >= 30))
            options.Add((() => Rally(f, w, random), f.InRvR ? 2.5 : 1));
        if (w.OwnKeeps.Length > 0 || w.LostKeeps.Length > 0)
            options.Add((() => KeepTalk(f, w, random), 1.2));
        if (!f.InRvR && f.Level < 50)
        {
            options.Add((() => CampAsk(f, random), 1.2));
            options.Add((() => LfFor(f, random), 1.5));
            if (f.Dungeon != null || f.InDungeon)
                options.Add((() => Named(f, random), 0.8));
        }
        options.Add((() => ClassTalk(f, random), 0.6));
        options.Add((() => Help(f, random), 2.2));
        options.Add((() => Social(f, random), 1.2));
        if (f.Bubbles > 0 && f.Level < 50)
            options.Add((() => Make(random, "social", null, null, null,
                new V($"{f.Bubbles} bubbles to {f.Level + 1}", "bubbles"), new V($"{f.Bubbles} bubs to go", "bubbles"),
                new V($"almost {f.Level + 1}", "almost"), new V($"{f.Bubbles} bubbles til {f.Level + 1} ugh", "bubbles")), 0.6));
        double total = options.Sum(o => o.Weight), draw = random.NextDouble() * total;
        foreach ((Func<Said> make, double weight) in options)
        {
            if (draw < weight)
                return make();
            draw -= weight;
        }
        return Social(f, random);
    }

    private static Said Say(Facts f, Random random, bool broadcast = false)
    {
        string cls = ShortClass(f.ClassName, random);
        string[] pots = Pool("the merchant has em", "try the merchant", $"alchemists in {CapitalWord(f.Realm)}", "nope sry");
        if (f.InTown)
        {
            if (f.Item != null && random.NextDouble() < 0.4)
                return Trade(f, random);
            // A town /broadcast carries no "brb" or "afk" (round 5: those were said to a whole town).
            if (broadcast)
                return Make(random, "social", null, SeekStill, SeekThanks,
                    new V($"lfg {f.Level} {cls}", null, Joins), new V("anyone grouping?", "say_grouping", Joins),
                    new V("any healers around?", "say_healers", Pool("{healer}me", "{healer}me, {lvl}")),
                    new V("anyone heading out?", "say_out", Pool("me", "in a bit", "me, where to?")), new V($"{f.Level} {cls} lf grp", null, Joins),
                    new V("anyone selling pots?", "say_pots", pots),
                    // 1.1.3, after RC5: tips about one's own class only while learning it ("any minsts around? need tips" from a 50).
                    f.Level < 40 ? new V($"any {cls}s around? need tips", "say_tips", Pool("{same}me, whats up", "{same}sure, ask"))
                        : new V("anyone selling dye?", "say_dye"));
            return Make(random, "social", null, SeekStill, SeekThanks,
                new V($"lfg {f.Level} {cls}", null, Joins), new V("anyone grouping?", "say_grouping", Joins), new V("brb", "brb"),
                new V("back", "back"), new V("any healers around?", "say_healers", Pool("{healer}me", "{healer}me, {lvl}")),
                new V("anyone heading out?", "say_out", Pool("me", "in a bit", "me, where to?")),
                f.Destination != null ? new V($"which stable for {ShortZone(f.Destination, random)}?", "say_stable", TravelAnswers(f.Realm))
                    : f.Level <= TrainerAskMaxLevel ? new V("where is the trainer", "say_trainer", TrainerAnswers(f, random)) : new V("anyone selling dye?", "say_dye"),
                new V($"{f.Level} {cls} lf grp", null, Joins), new V("afk 5", "afk"), new V("anyone selling pots?", "say_pots", pots), new V("brb selling", "brb_selling"));
        }
        string monster = f.Monster;
        string monsters = monster == null ? null : AutonomousBotVoiceCorpus.Plural(monster);
        if (f.InGroup)
            return new Said(Pick(random,
                "pulling", "inc", "oom", "sitting", "need pow", "brb", "rdy", "1 sec", "wait for heals",
                monster != null ? $"{monster} inc" : "adds", monster != null ? $"careful {monsters} link" : "careful"), "social", null);
        return Make(random, "camp", monster, null, null,
            new V("this camp taken?", "say_taken", Pool("go ahead", "its free", "ya sry", "were leaving soon")),
            new V("anyone grouping here?", "say_grouping", Joins), new V($"lf grp here {f.Level} {cls}", null, Joins), new V("sry ks", null),
            new V("camp free if u want it", "say_free", Pool("ty", "ty!", "thx")), new V("anyone camping this?", "say_taken", Pool("ya", "me sry", "go ahead")),
            new V(monster != null ? $"{monster} up?" : "anything up?", "say_up", Pool("not yet", "dunno, just got here", "was a min ago")),
            new V("anyone wanna duo?", "say_duo", Joins), new V("oom brb", "oom"), new V("careful adds", "adds"),
            new V(f.IsHealer ? "need heals?" : "any healers here?", f.IsHealer ? "say_offerheal" : "say_healers",
                f.IsHealer ? Pool("ya pls", "nah im ok", "ty") : Pool("{healer}me", "{healer}me, sec")));
    }

    // ---- the faction kinds -------------------------------------------------------------------------------------------------

    private static Said Inc(Facts f, WorldView w, Random random)
    {
        string keep = ShortKeep(w.AttackedKeeps[random.Next(w.AttackedKeeps.Length)], random);
        string enemy = EnemyWord(f.Realm, random);
        int count = 3 + random.Next(20);
        string[] omw = Pool("omw", "omw", "k", "how many?", "porting", $"omw {keep}", $"omw to {keep}", $"porting to {keep}");
        return Make(random, "inc", null, null, null,
            new V($"inc {keep}", "inc_call", omw), new V($"{keep} inc", "inc_call", omw), new V($"{enemy} at {keep}", "inc_call", omw),
            new V($"{count} {enemy} at {keep}", "inc_call", omw), new V($"{keep} under attack", "inc_call", omw), new V($"need def {keep}", "inc_call", omw),
            new V($"def {keep} pls", "inc_call", omw), new V($"{keep} is being hit", "inc_call", omw), new V($"{enemy} zerg at {keep}", "inc_call", omw),
            new V($"{enemy} hitting {keep} door", "inc_call", omw), new V($"inc {keep}, {count}+", "inc_call", omw), new V($"rams on {keep}", "inc_call", omw));
    }

    private static Said AskKeep(Facts f, WorldView w, Random random)
    {
        int index = random.Next(w.LostKeeps.Length);
        string keep = ShortKeep(w.LostKeeps[index], random), owner = RealmWord(w.LostTo[index], random), owners = Plural(owner);
        string[] who = Pool(owners, $"{owners} have {keep}", $"still {owners}", $"{owners} :/", $"{owners} since {Since(random)}");
        string subject = w.LostKeeps[index].ToLowerInvariant();
        return Make(random, "ask_keep", subject, Pool("nm, still theirs", "anyone?"), Pool("ty", "ugh", "figures"),
            new V($"who has {keep}?", "keep_who", who), new V($"who has {keep}", "keep_who", who), new V($"{keep} still {owner}?", "keep_still", who),
            new V($"is {keep} ours?", "keep_ours", Pool($"no, {owners}", $"nope {owners}", $"{owners} have it")),
            new V($"anyone taking {keep} back?", "keep_back", Pool("tonight maybe", "if we get a grp", "im in", "x me if so")),
            new V($"{owners} still have {keep}?", "keep_still", who), new V($"grp for {keep}?", "keep_back", Pool("im in", "x", "me", "later maybe")));
    }

    private static Said Relic(Facts f, WorldView w, Random random)
    {
        if (w.RelicsLost > 0)
        {
            string[] gone = Pool("still gone", "not ours", "gone still", $"{Plural(EnemyWord(f.Realm, random))} have it", $"gone since {Since(random)}");
            return Make(random, "relic", "lost", null, Pool("ty", "ugh"),
                new V("who has our relic?", "relic_who", gone), new V("who has the relic", "relic_who", gone), new V("relic still gone?", "relic_who", gone),
                new V("where is our relic", "relic_who", gone), new V("any relic raid tonight?", "relic_raid", Pool("hope so", "dunno", "ask in guild")),
                new V("relic raid when?", "relic_raid", Pool("dunno", "this weekend maybe", "no idea")));
        }
        return Make(random, "relic", "held", null, null,
            new V($"we have {w.RelicsHeld} relics now", "relic_count"), new V("still have their relic?", "relic_still", Pool("still ours", "ya", "for now")),
            new V("relic still ours?", "relic_still", Pool("still ours", "ya", $"ours since {Since(random)}")),
            new V("anyone guarding the relic?", "relic_guard", Pool("dunno", "few ppl", "not enough")));
    }

    private static Said Rvr(Facts f, WorldView w, Random random)
    {
        string frontier = f.World?.Frontier?.ToLowerInvariant() ?? "the frontier";
        string ownFull = w.OwnKeeps.Length > 0 ? w.OwnKeeps[random.Next(w.OwnKeeps.Length)] : null;
        string own = ownFull != null ? ShortKeep(ownFull, random) : "the keep";
        string[] where = w.AttackedKeeps.Length > 0
            ? Pool($"{ShortKeep(w.AttackedKeeps[0], random)} under attack", $"at {ShortKeep(w.AttackedKeeps[0], random)}", $"hitting {ShortKeep(w.AttackedKeeps[0], random)}")
            : Pool("quiet atm", "dunno, quiet tonight", "havent seen any");
        Said said = Make(random, "rvr", null, Pool("anyone?", "nm"), Pool("ty", "k omw", "omw"),
            new V("any grps out?", "rvr_grps", where), new V("anyone out tonight?", "rvr_out", Pool("me", "few of us", "me, {cls}")),
            new V($"anyone in {frontier}?", "rvr_out", Pool("me", "few of us", "on my way")),
            new V("rvr grp forming pst", "rvr_form", Joins), new V("where's the zerg", "rvr_zerg", where), new V("where is everyone", "rvr_zerg", where),
            new V($"any1 at {own}?", "rvr_keep", Pool("dunno, not me", "not me, check the map", "dunno")), new V($"quiet in {frontier}", "rvr_quiet"),
            new V("port to frontier up?", "rvr_port", Pool("ya", "should be", $"ya, porter in {CapitalWord(f.Realm)}")),
            new V("any stealthers out?", "rvr_stealth", Pool("havent seen any", "always lol, stick together", "dunno, watch ur back")),
            new V("lf rvr grp", "rvr_lf", Pool("x", "me too", "x me")),
            new V("anyone want to hit a keep?", "rvr_hit", Pool("me", "im in", "which one?", "x")), new V($"{EnemyWord(f.Realm, random)} out?", "rvr_enemy", where));
        // The place it asks about, for the live answer (who of the realm is really there).
        return said.Family switch
        {
            "rvr_keep" when ownFull != null => said with { Place = ownFull },
            "rvr_out" when said.Text.Contains(frontier, StringComparison.Ordinal) && f.World?.Frontier != null => said with { Place = f.World.Frontier },
            _ => said,
        };
    }

    private static Said AskXp(Facts f, Random random)
    {
        string lvl = f.Level.ToString(), cls = ShortClass(f.ClassName, random);
        string dun = ShortDungeon(f.Dungeon, random);
        string[] answers = f.Dungeon == null ? Pool("check lfg, someone always xps at that lvl", "solo outside town is ok", "lfg for a dungeon grp")
            : Pool(dun, $"try {dun}", $"{dun} prob", $"{dun} at {lvl}", $"{dun} if u get a grp", $"{dun} is ok at {lvl}", $"{dun} or solo outside");
        return Make(random, "ask_xp", (f.Level / 5).ToString(), Pool("nm found one", $"anyone? {lvl} here", "nm, trying {s} solo", $"no one? ok {dun ?? "solo"} it is"),
            Pool("ty", "ty!", "k ty", "kk", "cool ty", "ty, omw"),
            new V($"where do {lvl}s xp?", "ask_xp", answers), new V($"good xp for {lvl} {cls}?", "ask_xp_good", answers),
            new V(dun != null ? $"{dun} ok at {lvl}?" : $"where at {lvl}?", "ask_xp_place",
                dun != null ? Pool("ya", "ya with a grp", "should be", "ya, bring a healer", "fine at that lvl") : answers),
            new V($"xp spot for {lvl}?", "ask_xp_spot", answers), new V($"best xp at {lvl}?", "ask_xp_best", answers),
            new V($"{lvl} here, where should i go", "ask_xp_go", answers), new V($"any good spots for {lvl}s?", "ask_xp_spots", answers));
    }

    private static Said AskGroup(Facts f, Random random)
    {
        string dun = ShortDungeon(f.Dungeon, random), zone = ShortZone(f.Zone, random), lvl = f.Level.ToString(), cls = ShortClass(f.ClassName, random);
        string[] where = Pool("dunno, check lfg", "not me sry", "think so, check lfg", "not atm", "nope, elsewhere");
        // RC11 on the owner's PC: "city of camelot anyone" from a bot that had just ported to Camelot. Nobody xps in a town: a
        // bot in one asks for a group to head out with instead.
        V Out(string inZone, string fromTown, string kind, string[] answers) => new(f.InTown ? fromTown : inZone, kind, answers);
        return Make(random, "ask_group", (dun ?? zone) + ":" + f.Level / 5, SeekStill, SeekThanks,
            new V(dun != null ? $"anyone up for {dun}?" : "anyone xping?", "group_up", Joins),
            new V(dun != null ? $"any grps in {dun}?" : "any grps out?", "group_any", Pool("dunno, check lfg", "saw some in lfg", "not that i know", "few posting in lfg earlier", "none i saw", "check lfg, was a couple")),
            Out($"anyone wanna duo in {zone}?", "anyone wanna duo? heading out now", "group_duo", Joins), new V($"lf duo {lvl} {cls}", "group_duo", Joins),
            Out($"anyone xping in {zone}?", "anyone heading out to xp?", f.InTown ? "group_up" : "group_where", f.InTown ? Joins : where),
            Out($"{zone} anyone?", "grp anyone? in town, can come to u", f.InTown ? "group_up" : "group_where",
                f.InTown ? Joins : Pool("not me sry", "dunno, check lfg", "not that i know", "nope", "not me, other side of the realm")),
            new V(dun != null ? $"{dun} grp?" : "grp anyone?", "group_up", Joins), new V("any1 lf grp?", "group_lf", Pool("me", "x", "me, {lvl}", "me pls")),
            Out($"anyone near {zone}?", "anyone leaving town soon? lf grp", f.InTown ? "group_up" : "group_where",
                f.InTown ? Joins : Pool("not me sry", "dunno, check lfg", "not that i know", "not atm sry", "was earlier, left")));
    }

    private static Said NeedClass(Facts f, Random random)
    {
        // Only classes the asker's realm has (an Albion "any druids on?" was possible), answered only by bots that fill the need.
        string need = f.Realm switch
        {
            eRealm.Midgard => Pick(random, "healer", "tank", "shaman", "puller"),
            eRealm.Hibernia => Pick(random, "healer", "tank", "mezzer", "bard", "druid", "puller"),
            _ => Pick(random, "healer", "tank", "mezzer", "cleric", "puller"),
        };
        string dun = ShortDungeon(f.Dungeon, random) ?? ShortZone(f.Zone, random);
        string[] fits = Pool("me", "me, {lvl}", "x", "{lvl} {cls} here", "me, {cls} {lvl}");
        int low = Math.Max(1, f.Level - 2), high = Math.Min(50, f.Level + 2);
        Said said = Make(random, "ask_group", need + ":" + dun + ":" + f.Level / 5, SeekStill, SeekThanks,
            new V($"any {need}s on?", "need_class", fits), new V($"need a {need} for {dun}", "need_class", fits),
            new V($"lf {need} {low}-{high}", "need_class", fits), new V($"{need} anyone? {dun}", "need_class", fits));
        bool range = said.Text.Contains($"{low}-{high}", StringComparison.Ordinal);
        return said with { Role = RoleOf(need), LevelMin = range ? low : Math.Max(1, f.Level - 6), LevelMax = range ? high : Math.Min(50, f.Level + 6) };
    }

    private static Said Camp(Facts f, Random random)
    {
        string monsters = AutonomousBotVoiceCorpus.Plural(f.Monster), zone = ShortZone(f.Zone, random);
        return Make(random, "camp", f.Monster, Pool("nm got it", $"nm, {monsters} were free", "nvm found a spot"), Pool("ty", "k", "kk"),
            new V($"{monsters} in {zone} free?", "camp_free", Pool("dunno, go look", "not me, go for it", "dunno, havent been")),
            new V($"anyone camping {monsters}?", "camp_free", Pool("not me, go for it", "dunno, go look", "not me")),
            new V($"{monsters} drop anything good?", "camp_drops", Pool("mostly junk", "some decent stuff", "coin mostly lol", "now and then")),
            new V($"{f.Monster} up anyone?", "camp_up", Pool("dunno", "didnt see it", "dunno, not there")),
            new V($"camping {monsters} if anyone wants to join", "camp_join", Pool("me", "where at?", "x", "me, {lvl}")));
    }

    private static Said Travel(Facts f, Random random)
    {
        string dest = ShortZone(f.Destination, random);
        string[] way = TravelAnswers(f.Realm);
        return Make(random, "travel", dest, Pool("nm found it", $"nm, found {dest}"), Pool("ty", "k ty", "ty!"),
            new V($"where is {dest}?", "ask_way", way), new V($"how do i get to {dest}", "ask_way", way), new V($"which stable for {dest}?", "ask_stable", way),
            new V($"fastest way to {dest}?", "ask_way", way));
    }

    /// <summary>A rally while a keep is really under attack: "all to beno", "grp up for bled def" (round 6).</summary>
    private static Said Rally(Facts f, WorldView w, Random random)
    {
        string full = w.AttackedKeeps[random.Next(w.AttackedKeeps.Length)];
        string keep = ShortKeep(full, random);
        string[] omw = Pool("omw", "otw", "porting", "omw!", "on my way", $"omw {keep}", "coming", "2 min");
        return Make(random, "rally", full.ToLowerInvariant() + ":" + f.Level / 10, null, null,
            new V($"everyone to {keep}", "rally_all", omw), new V($"all to {keep} pls", "rally_all", omw), new V($"grp up for {keep} def", "rally_grp", Joins),
            new V($"need bodies at {keep}", "rally_bodies", omw), new V($"any healers to {keep}?", "rally_heal", Pool("{healer}omw", "{healer}porting")),
            new V($"port to {keep} up?", "rally_port", Pool("ya", "think so", "dunno")), new V($"{keep} needs ppl", "rally_bodies", omw));
    }

    /// <summary>Keep news and realm pride tied to the real keep count (round 6): "we hold 6 keeps", "when did we lose rena?".</summary>
    private static Said KeepTalk(Facts f, WorldView w, Random random)
    {
        int held = w.OwnKeeps.Length;
        var ways = new List<V>(6);
        if (held > 0)
        {
            ways.Add(new V($"we hold {held} keeps", "pride_count", Pool("not bad", "nice", "for now lol", "need more")));
            ways.Add(new V(held >= 6 ? $"{held} keeps, not bad" : $"only {held} keeps?", "pride_count", Pool("ya", "lol", "we'll get em back")));
            ways.Add(new V(held >= 6 ? $"{EnemyWord(f.Realm, random)} cant hold anything lol" : $"{EnemyWord(f.Realm, random)} got lucky", "pride_banter",
                Pool("lol", "ha", "true")));
        }
        if (w.LostKeeps.Length > 0)
        {
            int index = random.Next(w.LostKeeps.Length);
            string keep = ShortKeep(w.LostKeeps[index], random), owners = Plural(RealmWord(w.LostTo[index], random));
            ways.Add(new V($"when did we lose {keep}?", "keep_when", Pool(Since(random), $"{Since(random)} i think", "dunno, earlier")));
            ways.Add(new V($"{owners} took {keep}?", "keep_took", Pool("ya", $"ya, {Since(random)}", "yep")));
            ways.Add(new V($"{keep} gone?", "keep_took", Pool("ya", $"{owners} have it", "yep")));
        }
        return Make(random, "keeps", held.ToString() + ":" + w.LostKeeps.Length, null, Pool("ugh", "figures", "ty", "k"), ways.ToArray());
    }

    /// <summary>A camp question at the asker's real level and zone (round 6).</summary>
    private static Said CampAsk(Facts f, Random random)
    {
        string lvl = f.Level.ToString(), zone = ShortZone(f.Zone, random), dun = ShortDungeon(f.Dungeon, random);
        string[] answers = dun == null ? Pool("check lfg, someone always camps there", "solo outside town is ok", "lfg for a dungeon grp")
            : Pool($"{dun}", $"try {dun}", $"{dun} with a grp", $"outside {dun}");
        return Make(random, "camp", "ask:" + zone + ":" + f.Level / 3, Pool($"anyone? {lvl} here", "nm found one", "ok, guess ill wander"),
            Pool("ty", "k ty", "ty, omw", "cool"),
            new V($"good camp for {lvl}?", "camp_ask", answers),
            // RC11 on the owner's PC: nobody camps in a town ("where do 41s camp in city of camelot?").
            f.InTown ? new V($"where should a {lvl} camp?", "camp_ask", answers) : new V($"where do {lvl}s camp in {zone}?", "camp_ask_zone", answers),
            f.InTown ? new V($"heading out, camp for {lvl}?", "camp_ask", answers) : new V($"{zone} camp for {lvl}?", "camp_ask_zone", answers),
            new V($"anything good to camp at {lvl}?", "camp_ask", answers));
    }

    /// <summary>"lf X for Y" with a real dungeon for the level (round 6).</summary>
    private static Said LfFor(Facts f, Random random)
    {
        string dun = ShortDungeon(f.Dungeon, random) ?? ShortZone(f.Zone, random);
        string need = Pick(random, "1 more", "2 more", "a healer", "a tank", "dps", "a puller", "cc");
        int low = Math.Max(1, f.Level - 2), high = Math.Min(50, f.Level + 2);
        // "{s}" in a follow-up is the need ("still lf a tank"), not the key's "lf".
        Said said = Make(random, "ask_group", need + ":" + dun + ":" + f.Level / 4, SeekStill, SeekThanks,
            new V($"lf {need} for {dun}", "lf_for", Joins), new V($"{dun}, lf {need}", "lf_for", Joins),
            new V($"lf {need} {dun} {low}-{high}", "lf_for", Joins), new V($"need {need} for {dun}", "lf_for", Joins));
        bool range = said.Text.Contains($"{low}-{high}", StringComparison.Ordinal);
        return said with { Role = RoleOf(need), LevelMin = range ? low : Math.Max(1, f.Level - 6), LevelMax = range ? high : Math.Min(50, f.Level + 6) };
    }

    /// <summary>"anyone seen the named in keltoi?" (round 6): a question, answered with what a bot could know, or nothing.</summary>
    private static Said Named(Facts f, Random random)
    {
        string dun = f.InDungeon ? ShortZone(f.Zone, random) : ShortDungeon(f.Dungeon, random);
        return Make(random, "camp", "named:" + dun, Pool("nm", "guess it's down"), Pool("ty", "k ty"),
            new V($"anyone seen the named in {dun}?", "named", Pool("not tonight", "dunno", "was up earlier")),
            new V($"{dun} named up?", "named", Pool("dunno", "not when i left", "think so")),
            new V($"who's camping the {dun} named", "named", Pool("dunno", "nobody i think", "dunno, go look")));
    }

    /// <summary>Class talk tied to the speaker's real class and level (round 6).</summary>
    private static Said ClassTalk(Facts f, Random random)
    {
        string cls = ShortClass(f.ClassName, random), lvl = f.Level.ToString();
        // "any reavers on? quick q" is answered "me" by a reaver only, and the asker then asks the question it had (owner
        // 2026-11-08: the asker asked "solo or grp?", the answerer's question).
        Said said = Make(random, "class", (f.ClassName ?? "").ToLowerInvariant() + ":" + f.Level / 5, Pool("nobody? ok lol", "nm figured it out"),
            Pool("ty", "ty!", "makes sense ty", "k ty"),
            new V($"{cls}s, sc or drops at {lvl}?", "class_gear", GearAdvice(f.Level)), new V($"what weapon for a {lvl} {cls}?", "class_weapon", WeaponAdvice(f.ClassName)),
            // 1.1.3, after RC5: a 50 has spent its points and has no xp to find; it asks about a respec or gear instead.
            f.Level < 50 ? new V($"{cls} {lvl}, what should i put points in?", "class_spec", SpecAdvice(f.ClassName))
                : new V($"{cls}s, any respec tips at 50?", "class_spec", SpecAdvice(f.ClassName)),
            new V($"any {cls}s on? quick q", "class_q", Pool("{same}me, whats up", "{same}me", "{same}me, sup")),
            f.Level <= 45 ? new V($"is {cls} fun after {f.Level + 5}?", "class_later", Pool($"gets better at {f.Level + 5}", "ya, more fun in a grp", "ya, rvr is where its fun"))
                : new V($"anyone else play {cls}?", "class_others", Pool("{same}me", "my alt is one", "{same}ya, me", "used to, rerolled", "my bro does")),
            f.Level < 50
                ? new V($"{cls}s, where did u xp at {lvl}?", "class_xp", ShortDungeon(f.Dungeon, random) is { } xpDun
                    ? Pool($"{xpDun} with a grp", $"{xpDun} mostly", $"solo outside {xpDun}") : Pool("lfg grps mostly", "solo outside town"))
                : new V($"{cls}s, sc or drops at 50?", "class_gear", GearAdvice(50)));
        return said with { SameClass = f.ClassName };
    }

    /// <summary>A word on what the bot is doing (round 5: regulars talk about their own evening): its camp, its trip, its power.</summary>
    private static Said Status(Facts f, Random random)
    {
        var ways = new List<V>(8);
        string zone = ShortZone(f.Zone, random);
        if (f.AtCamp && f.Monster != null)
        {
            string monsters = AutonomousBotVoiceCorpus.Plural(f.Monster);
            ways.Add(new V($"back to {monsters}", "status_camp"));
            ways.Add(new V($"more {monsters} lol", "status_camp"));
            ways.Add(new V($"{monsters} hit hard tonight", "status_camp_hard", Pool("lol", "ya they do", "bring a healer")));
            ways.Add(new V($"{monsters} again, {f.Bubbles} bubs to go", "status_camp"));
        }
        if (f.Destination != null)
        {
            string dest = ShortZone(f.Destination, random);
            ways.Add(new V($"heading to {dest}", "status_travel"));
            ways.Add(new V($"off to {dest}, back in a bit", "status_travel"));
            ways.Add(new V($"on the horse to {dest}", "status_travel"));
        }
        if (f.InDungeon)
            ways.Add(new V($"{zone} is busy tonight", "status_dungeon", Pool("ya", "always is")));
        ways.Add(random.Next(3) switch
        {
            0 => new V($"{f.Level} {ShortClass(f.ClassName, random)}, still grinding", "status_grind"),
            // RC10 on the owner's PC: "grinding city of camelot at 50" from a bot back at its bind after a death.
            1 when !f.InTown => new V($"grinding {zone} at {f.Level}", "status_grind_zone"),
            1 => new V($"{f.Level} {ShortClass(f.ClassName, random)}, still grinding", "status_grind"),
            _ when f.Level < 50 => new V($"still {f.Level} lol", "status_still"),
            _ => new V("50 and still grinding lol", "status_grind"),
        });
        ways.Add(new V($"back in {zone}", "status_zone"));
        ways.Add(new V(f.IsHealer ? "sitting for pow" : "sitting, need hp", "status_sit"));
        return Make(random, "social", (f.Monster ?? f.Destination ?? zone)?.ToLowerInvariant() + ":" + f.Level / 5, null, null, ways.ToArray());
    }

    /// <summary>Asking the realm for something: an item, a trainer, a spec (round 5: more ways to ask, each its own idea).</summary>
    private static Said AskAround(Facts f, Random random)
    {
        string cls = ShortClass(f.ClassName, random), lvl = f.Level.ToString();
        string want = WantFor(f, random);
        string[] have = Pool("try the broker", $"merchants in {CapitalWord(f.Realm)}", "crafters can make it, pst one", "nope sry");
        return Make(random, "ask", cls + ":" + want, Pool("nm found it", $"nobody? ok", $"still need {want}"), Pool("ty", "ty!", "k ty", "cool"),
            new V($"anyone have {want}?", "ask_have", have), new V($"anyone got spare {want}", "ask_have", have),
            // 1.1.3, after RC5: only a young character asks where its own trainer is (a 50 paladin asked "where's the paladin trainer").
            f.Level <= TrainerAskMaxLevel ? new V($"where's the {cls} trainer", "ask_trainer", TrainerAnswers(f, random)) : new V($"anyone got spare {want}?", "ask_have", have),
            new V($"what spec for a {lvl} {cls}?", "ask_spec", SpecAdvice(f.ClassName)),
            new V($"{cls}s, what do u put points in at {lvl}?", "ask_spec", SpecAdvice(f.ClassName)),
            want.Any(char.IsDigit) ? new V($"anyone selling {want}?", "ask_have", have)
                : new V($"is {want} worth it at {lvl}?", "ask_worth", Pool("ya, if its cheap", "only if u cant find better", "ya, any upgrade helps", "nah, wait for a drop", "depends what they want for it", "if u got the coin sure")));
    }

    private static Said Crafter(Facts f, Random random)
    {
        string craft = Pick(random, "armorcrafters", "weaponcrafters", "fletchers", "tailors", "spellcrafters", "alchemists");
        string one = craft[..^1], article = "aeiou".Contains(one[0]) ? "an" : "a";
        string[] crafters = Pool($"try the crafters in {CapitalWord(f.Realm)}", "check the broker", $"few in {CapitalWord(f.Realm)} usually");
        return Make(random, "crafter", one + ":" + f.Level / 10, Pool("nm found one", "still lf " + article + " " + one, $"any {craft}? anyone", $"no {craft} on? ok"), null,
            new V($"any {craft} on?", "need_crafter", crafters), new V($"need {article} {one} pst", "need_crafter"),
            new V($"any good {craft} around?", "need_crafter", crafters),
            new V($"{one} on? pst", "need_crafter"));
    }

    private static Said PriceCheck(Facts f, Random random)
    {
        if (f.Item == null || !f.ItemIsGear)
            return Social(f, random);
        string item = f.Item.ToLowerInvariant();
        string[] price = Pool(PriceFor(f.Level, random), $"{PriceFor(f.Level, random)} maybe", "dunno, try the broker", "not much");
        return Make(random, "crafter", item, Pool("nm", "guess ill keep it"), Pool("ty", "k ty"),
            new V($"pc {item}?", "price_check", price), new V($"what's {article(item)} {item} worth?", "price_check", price),
            new V($"how much for {article(item)} {f.ItemQuality}% {item}?", "price_check", price));

        static string article(string word) => "aeiou".Contains(word[0]) ? "an" : "a";
    }

    private static Said Ding(Facts f, Random random) =>
        new(Pick(random, $"ding {f.Level}", "ding", "ding!", $"{f.Level} finally", $"dinged {f.Level}", $"ding {f.Level} :)", $"{f.Level}!!"), "ding",
            Pool("gz", "grats", "gz!", "gratz", "nice gz"))
        {
            Thanks = Pool("ty", "ty!", "thx :)", "ty all", "ty ty"),
            Unanswered = f.Level >= 50 ? Pool("finally lol", "now what", "rvr time") : Pool("off to train", $"{f.Level + 1} next lol", "trainer time", "finally lol"),
        };

    /// <summary>
    /// A bot's own death in a line (1.1.3, after RC6: a paladin killed by one enemy bot said "died to frontier patrol near death
    /// stalkers ugh"): a death to an enemy is told as a gank, with the enemy's class when known and no monster; one to a monster
    /// names the monster that killed it; no line claims a crowd it didn't have. Pure.
    /// </summary>
    private static Said Death(Facts f, Random random)
    {
        // A rez request is answered as one (RC6: "rez in murdaigean?" drew "oof"): "omw" or "where at?" only from a healer,
        // "no rez on me sry" from anyone, and asked only while the bot still lies dead.
        if (f.Dead != false && random.NextDouble() < 0.25)
            return new Said(Pick(random, $"rez in {ShortZone(f.Zone, random)}?", "need rez pls", "any rez?", $"rez pls, {ShortZone(f.Zone, random)}"), "death",
                Pool("{healer}omw", "{healer}where at?", "{healer}sec, coming", "too far sry", "no rez on me sry", "cant, sry, too far"))
            {
                Asks = true,
                Thanks = Pool("k", "k ty", "np"),
                Unanswered = Pool("released, running back", "nm, released", "k releasing", "nvm, running back"),
            };
        var options = new List<string> { "died lol", "rip me", "argh died" };
        if (KilledByEnemy(f))
        {
            string cls = f.KillerClass == null ? null : ShortClass(f.KillerClass, random);
            options.AddRange(["got ganked", "ganked lol", "got jumped ugh", "died ugh"]);
            if (cls != null)
                options.AddRange([$"died to {("aeiou".Contains(cls[0]) ? "an" : "a")} {cls}", $"{cls} got me ugh"]);
        }
        else if (f.KilledBy == "guard")
            options.AddRange(["guards got me lol", "died to a guard ugh"]);
        else
        {
            options.Add(f.Monster != null ? $"died to {AutonomousBotVoiceCorpus.Plural(f.Monster)} ugh" : "died ugh");
            options.Add("lost a bubble, ugh");
            if (f.Attackers != 1)
                options.Add("train lol, dead");
        }
        string[] fitting = options.Where(text => FitsDeath(text, f)).ToArray();
        return new(Pick(random, fitting), "death", Pool("rip", "ouch", "lol rip", "oof"))
        {
            Thanks = Pool("ty", "heh ty", "ugh ya"),
            Unanswered = Pool("released, running back", "got a rez ty", "back up", "ok running back"),
        };
    }

    private static Said Social(Facts f, Random random)
    {
        int hour = f.LocalHour;
        bool late = hour >= 23 || hour < 4, evening = hour is >= 17 and < 23;
        return Make(random, "social", null, null, null,
            new V("brb food", "brb_food"), new V("brb", "brb"), new V("back", "back", Pool("wb", "wb!")), new V("anyone on?", "anyone_on", Pool("ya", "here", "o/")),
            new V(late ? "gn all" : evening ? "evening all" : "hi all", "hello_all", late ? Pool("gn", "night") : Pool("o/", "hey", "heya")),
            new V("quiet tonight", "quiet"), new V("who's on", "anyone_on", Pool("me", "here", "o/")),
            // 1.1.3: never a bare "lol" out of nowhere: unprompted, it reads as a laugh at whatever was said before it.
            new V(late ? "one more lvl then bed" : evening ? "long night lol" : "long day lol", late ? "bedtime" : "long_night"), new V("afk 5", "afk"),
            new V("brb phone", "brb_phone"),
            new V("ok back", "back", Pool("wb")), new V(f.BagFull ? "bags full brb" : "brb drink", "brb_bags"),
            new V(late ? "late crew still here" : "busy tonight", "busy_night"));
    }

    // ---- guild chat ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A guild line from the bot's state and the guild's (round 4: guild ambient was still the old written lines, 39 of 40
    /// caught): a group call with its level and zone, raid and keep plans with the guild's real keep, a "gz" to a guildmate who
    /// really dinged, an "o/" to one who logged in, a crafter wanted, an old item offered, and the small talk of a channel
    /// ("brb", "back", "lag?", "night all"). Each way carries the answers that fit it (round 5: "benow doors ok?" drew "me"). Pure.
    /// </summary>
    private static Said Guild(Facts f, Random random)
    {
        var options = new List<(Func<Said> Make, double Weight)>(10);
        string lvl = f.Level.ToString(), cls = ShortClass(f.ClassName, random), zone = ShortZone(f.Zone, random);
        string dun = ShortDungeon(f.Dungeon, random);
        WorldView w = f.World ?? WorldView.Empty;
        string[] gz = Pool("gz", "grats!", "gz gz", "woot");
        if (f.GuildmateDinged != null)
        {
            string mate = f.GuildmateDinged.ToLowerInvariant();
            options.Add((() => Make(random, "guild_ding", null, null, null, new V($"gz {mate}", null, gz), new V($"grats {mate}", null, gz),
                new V($"gz {mate}!", null, gz), new V($"nice {mate} gz", null, gz), new V($"gratz {mate}", null, gz)), 8));
        }
        if (f.GuildmateArrived != null)
        {
            string mate = f.GuildmateArrived.ToLowerInvariant();
            string[] hi = Pool("o/", "hey", "wb", "heya");
            options.Add((() => Make(random, "guild_hello", null, null, null, new V($"o/ {mate}", null, hi), new V($"hey {mate}", null, hi),
                new V($"wb {mate}", null, hi), new V($"{mate}!", null, hi), new V($"yo {mate}", null, hi)), 8));
        }
        if (f.JustLeveled)
            options.Add((() => Ding(f, random) with { Kind = "guild_ding" }, 6));
        if (f.Level < 50 && !f.InRvR)
            options.Add((() => Make(random, "guild_group", dun ?? zone, SeekStill, SeekThanks,
                f.InTown ? new V($"anyone want to group, {lvl}, heading out", "g_group", Joins) : new V($"anyone want to group, {lvl} in {zone}", "g_group", Joins),
                f.InTown ? new V($"anyone around {lvl}? in town", "g_around", Joins) : new V($"anyone around {lvl}? {zone}", "g_around", Joins),
                new V($"any1 {Math.Max(1, f.Level - 2)}-{Math.Min(50, f.Level + 2)} wanna xp?", "g_group", Joins),
                new V(dun != null ? $"{dun} anyone? {lvl} {cls}" : $"xp anyone? {lvl} {cls}", "g_dun", Joins),
                new V(dun != null || !f.InTown ? $"lf guildies for {dun ?? zone}" : $"lf guildies to xp with, {lvl}", "g_dun", Joins),
                new V($"who's {lvl}ish, wanna duo", "g_duo", Joins),
                new V(dun != null || !f.InTown ? $"need 1 more for {dun ?? zone}" : $"need 1 more, {lvl}ish, leaving town", "g_more", Joins),
                new V($"{lvl} {cls} lf grp in guild?", "g_lfgrp", Joins),
                new V($"anyone in guild near {lvl}?", "g_near", Joins), new V(dun != null || !f.InTown ? $"guild grp for {dun ?? zone}?" : "guild grp anyone?", "g_grp_for", Joins),
                new V($"who wants to xp, {lvl} {cls} here", "g_who_xp", Joins), new V($"{dun ?? zone} tonight, anyone?", "g_tonight", Joins)), 3));
        if (f.Level >= 35)
        {
            string keep = f.GuildKeep != null ? ShortKeep(f.GuildKeep, random) : null;
            string[] plan = Pool("me", "maybe", "after this lvl", "im in", "later maybe", "count me in", "me if im on");
            string[] when = Pool("dunno", "ask the gl", "think so", "maybe sat", "no idea");
            options.Add((() => Make(random, "raid", keep, Pool("anyone?", "nobody? ok lol"), Pool("k", "cool", "k, 9ish then"),
                new V("who's on for df tonight", "g_df", plan), new V("raid tonight?", "g_raid", when), new V("anyone up for a keep take later", "g_keep_take", plan),
                new V("relic raid when?", "g_relic", when), new V("who's up for rvr after", "g_rvr", plan),
                w.LostKeeps.Length > 0 ? new V($"lets take {ShortKeep(w.LostKeeps[random.Next(w.LostKeeps.Length)], random)} back", "g_keep_take", plan)
                    : new V("keep run tonight?", "g_keep_take", when),
                keep != null ? new V($"anyone at {keep}?", "g_keep_at") : new V("df later?", "g_df", plan),
                keep != null ? new V($"{keep} doors ok?", "g_keep_doors", Pool("dunno", "were fine earlier", "havent looked")) : new V("rvr later anyone", "g_rvr", plan),
                new V("dragon this weekend?", "g_dragon", when), new V("who's around for df", "g_df", plan)), f.InRvR ? 3 : 1.5));
        }
        if (f.GuildKeep != null && w.AttackedKeeps.Contains(f.GuildKeep))
        {
            string keep = ShortKeep(f.GuildKeep, random);
            string[] omw = Pool("omw", "porting", "omw!", "on it");
            options.Add((() => Make(random, "inc", null, null, null, new V($"{keep} under attack", "g_inc", omw), new V($"our keep is being hit, {keep}", "g_inc", omw),
                new V($"def {keep} pls", "g_inc", omw), new V($"inc {keep}", "g_inc", omw)), 4));
        }
        string craft = Pick(random, "smith", "armorcrafter", "tailor", "fletcher", "spellcrafter", "alchemist");
        int skill = 300 + 50 * random.Next(13);
        options.Add((() => Make(random, "guild_craft", craft, Pool("nm", "still need one"), null,
            new V($"need a {craft}, anyone {skill}+?", "g_craft_need"), new V($"any {craft}s on? need {skill}+", "g_craft_any"), new V($"{craft} on? pst", "g_craft_on"),
            new V($"who's our best {craft} now", "g_craft_best", Pool("ask the gl", "dunno", "not me lol")), new V($"lf {craft} {skill}+", "g_craft_lf"),
            new V($"who has {craft} at {skill}?", "g_craft_who", Pool("dunno", "ask the gl")), new V($"{craft} {skill}+ around?", "g_craft_around")), 1));
        if (f.Item != null)
        {
            string item = f.Item.ToLowerInvariant();
            string article = "aeiou".Contains(item[0]) ? "an" : "a";
            string[] want = Pool("me!", "me pls", "what lvl?", "me, i need one", "what stats?", "dibs");
            options.Add((() => (f.ItemIsGear
                ? Make(random, "guild_sell", item, Pool("nobody? ok merchant it is", "last call"), Pool("sending", "trade me", "k, where r u"),
                    // "my old" only for an item the bot could have worn (1.1.3, after RC5).
                    UsesItem(f) ? new V($"selling my old {item} if anyone wants", "g_sell", want) : new V($"selling {article} {item} if anyone wants", "g_sell", want),
                    new V($"anyone need {article} {item}? free for guild", "g_give", want),
                    new V($"got {article} {item} spare, anyone?", "g_give", want), new V($"{item} up for grabs", "g_give", want),
                    new V($"who wants {article} {item}", "g_give", want),
                    UsesItem(f) ? new V($"old {item} free to a guildie", "g_give", want) : new V($"{item} free to a guildie", "g_give", want))
                : Make(random, "guild_sell", item, null, Pool("sending", "trade me"),
                    new V($"anyone need {item}?", "g_drop", Pool("me", "what for?", "nah")), new V($"got some {item} if anyone needs it", "g_drop", Pool("me", "how many?")),
                    new V($"{item} free to whoever wants it", "g_drop", want), new V($"who collects {item}", "g_drop", Pool("me lol", "nobody lol")))) with { Item = item }, 1.2));
        }
        bool late = f.LocalHour >= 23 || f.LocalHour < 4, evening = f.LocalHour is >= 17 and < 23;
        options.Add((() => Make(random, "social", null, null, null,
            new V("brb", "g_brb"), new V("back", "g_back", Pool("wb", "wb!")), new V("lag?", "g_lag", Pool("lag here too", "not for me", "ya, bad")),
            new V("lag", "g_lag", Pool("same", "ya")), new V("brb food", "g_food"), new V("afk 5", "g_afk"),
            new V(late ? "night all" : "hey all", "g_hello", late ? Pool("night", "gn") : Pool("o/", "hey", "heya")),
            new V(late ? "gn guild" : "o/", "g_hello", late ? Pool("night", "gn") : Pool("o/", "hey")),
            new V("quiet tonight", "g_quiet"), new V("who's on", "g_who", Pool("me", "here", "o/")), new V("ok back", "g_back", Pool("wb")),
            new V("brb phone", "g_phone"), new V("laggy for anyone else?", "g_lag", Pool("ya", "fine here", "bit")),
            new V(late ? "bed, night" : evening ? "evening" : "hi all", "g_hello", late ? Pool("night", "gn") : evening ? Pool("o/", "evening") : Pool("o/", "hey"))), 2.2));
        double total = options.Sum(o => o.Weight), draw = random.NextDouble() * total;
        foreach ((Func<Said> make, double weight) in options)
        {
            if (draw < weight)
                return make();
            draw -= weight;
        }
        return null;
    }

    // ---- threads and stories (round 7) ---------------------------------------------------------------------------------------

    /// <summary>One line of a thread: who says it (0 the opener's speaker, 1 the answerer, 2 a third bot), what, and when (ms after the opener).</summary>
    public readonly record struct Step(int Who, string Text, int AtMs);

    /// <summary>
    /// How likely a line that invites a reply gets one (round 7: real channels answer about seven in ten). Lines with no
    /// answers invite nothing; the small talk of a channel ("brb", "back") is answered less. 1.1.3 (owner's PC, RC3 and RC5
    /// 2026-11-09): a real question is always answered when a bot can answer it; what still leaves one unanswered is a channel
    /// at its ceiling or with three threads running (the asker then doesn't ask it, see
    /// <c>AutonomousBotChatCoordinator.TryField</c>), or no free bot to say the answer.
    /// </summary>
    public static double ReplyChance(Said said) => said?.Answers == null ? 0 : WantsAnswer(said) ? 1.0 :
        said.Kind == "social" ? 0.45 : Math.Max(0.7, AnswerChance(said.Kind));

    /// <summary>The share of answered lines that try to run on (a follow-up question, its answer, a thanks); about one in five does.</summary>
    public const double LongThreadChance = 0.28;

    // A line's thread after the first answer: the opener's follow-up question, the answerer's reply, and what a third bot may
    // add. {cls}, {lvl}, {zone}, {dun}, {monsters} are the speaker's own.
    private static readonly Dictionary<string, (string[] Ask, string[] Reply, string[] Chime)> More = new(StringComparer.Ordinal)
    {
        ["ask_xp"] = (["grp or solo?", "which camp there?", "how far in?", "is it busy?", "solo ok there?", "what mobs?", "how long to {lvl}?"],
            ["grp is better", "solo outside is ok", "front rooms are easy", "back rooms if u have a healer", "was quiet earlier"],
            ["bring pots", "^ that", "watch the adds", "was there earlier, busy"]),
        ["ask_group"] = (["what lvl r u?", "where u at?", "what class?", "u close?", "how far r u?"], ["{lvl}", "{lvl} {cls}", "in {zone}", "{cls}, {lvl}", "coming from {zone}"],
            ["room for 1 more?", "me too if room", "x2"]),
        ["camp"] = (["how many up?", "is it camped?", "which side?"], ["few", "2 last i saw", "was empty", "dunno, just got here", "north side"],
            ["watch the adds there", "they link", "bring a healer", "{monsters} hit hard at {lvl}", "was there at {lvl}"]),
        ["keeps"] = (["when?", "who took it?"], ["earlier tonight", "while i was afk lol", "dunno, saw it on the map"], ["we'll get it back", "ugh", "always on my break"]),
        ["rally"] = (["how many?", "where r they?"], ["big grp", "10+ at the door", "at the gate", "inside already"], ["porting too", "omw also", "bringing my grp"]),
        ["rvr"] = (["how many?", "which way?"], ["few grps", "small grp", "through the mg"], ["count me in", "omw too"]),
        ["raid"] = (["what time?", "who's leading?"], ["9ish", "in an hour", "gl is"], ["im in too", "same"]),
        ["guild_group"] = (["what lvl?", "where at?"], ["{lvl}", "{zone}", "{lvl} {cls}"], ["me too", "room for 1?"]),
        ["guild_sell"] = (["what stats?", "what lvl is it?"], ["decent", "dunno, check it", "{lvl}ish"], ["dibs after"]),
    };

    // A thread's follow-up by the line's family, each question with the answers that fit it (owner 2026-11-08: one pool for
    // every help question gave "which part?" to "crafters, pst one" and "the map helps" to "which part?"). Families without an
    // entry here (and kinds "class" and "help") don't run on.
    private static readonly Dictionary<string, ((string Ask, string[] Reply)[] Pairs, string[] Chime)> FamilyMore = new(StringComparer.Ordinal)
    {
        ["class_q"] = ([("what do u put points in at {lvl}?", ["main line first", "max ur main line", "main line, rest wherever"]),
                ("sc or drops at {lvl}?", ["sc if u can afford it", "drops till 50 lol", "whatever drops lol"]),
                ("solo or grp spec?", ["grp, always", "depends what u do most", "grp if u can find one"])],
            ["^", "+1", "what he said"]),
        ["class_others"] = ([("how do u like it?", ["love it", "its ok", "fun in a grp"]), ("solo much?", ["not really", "some", "grp mostly"])],
            ["^", "same"]),
        ["q_quest"] = ([("where are the mobs for it?", ["near the town, i think", "out past the road", "dunno exactly, ask a guard"]),
                ("what lvl for that?", ["{lvl}ish", "did it at {lvl}"])],
            ["^ that"]),
        ["q_handin"] = ([("where exactly?", ["by the inn", "near the town, i think", "next to the merchants"])], ["^ that"]),
        ["q_bind"] = ([("where exactly?", ["by the inn", "near the town, i think", "next to the merchants"]), ("near what?", ["the inn", "the merchants"])],
            ["^ that"]),
    };

    /// <summary>
    /// Whether a line says something about its speaker: its level, its class, its zone or dungeon ("me, thane 31", "in lyonesse").
    /// Such a line can only be said by that bot; any other line of an answer may be said by another bot that can answer the
    /// question when the first one is busy fighting (1.1.3, after RC5). Pure.
    /// </summary>
    public static bool MentionsSelf(string text, Facts speaker)
    {
        if (string.IsNullOrWhiteSpace(text) || speaker == null)
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        if (t.Contains(" " + speaker.Level + " ", StringComparison.Ordinal) || t.Contains(" me ", StringComparison.Ordinal) ||
            t.StartsWith(" me", StringComparison.Ordinal) || t.Contains(" im ", StringComparison.Ordinal) || t.Contains(" my ", StringComparison.Ordinal))
            return true;
        if (speaker.ClassName != null && ClassWords.TryGetValue(speaker.ClassName.Trim(), out string[] words) &&
            words.Append(speaker.ClassName.Trim().ToLowerInvariant()).Any(word => t.Contains(" " + word + " ", StringComparison.Ordinal)))
            return true;
        foreach (string place in new[] { speaker.Zone, speaker.Dungeon })
        {
            if (string.IsNullOrWhiteSpace(place))
                continue;
            string p = AutonomousChatQuestions.Normalize(place);
            if (p.StartsWith("the ", StringComparison.Ordinal))
                p = p[4..];
            if (p.Length > 0 && t.Contains(" " + p.Split(' ')[0] + " ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>Whether an answer already gives what a follow-up would ask ("what lvl r u?" after "me, cab 50"). Pure.</summary>
    public static bool AlreadyAnswered(string ask, string answer)
    {
        if (string.IsNullOrWhiteSpace(ask) || string.IsNullOrWhiteSpace(answer))
            return false;
        string kind = QuestionKind(ask);
        string a = " " + AutonomousChatQuestions.Normalize(answer) + " ";
        if (kind == "level")
            return System.Text.RegularExpressions.Regex.IsMatch(a, @"\b\d{1,2}\b");
        if (kind == "who")
            return ClassWords.Values.SelectMany(words => words).Any(word => a.Contains(" " + word + " ", StringComparison.Ordinal));
        return false;
    }

    /// <summary>
    /// The thread a field line starts (round 7: lines invited replies that never came, and nothing ran on). About seven in ten
    /// inviting lines get an answer; one in five of those runs on to a follow-up question, its answer and a thanks, and a third
    /// bot sometimes chimes in. With no answer the speaker may follow up alone. Every line is from the speaker's own state.
    /// <paramref name="known"/> is the answer the live world gives (the class trainer's name and town, the stable with the
    /// route, the camps bots of that level use, who is at a keep): said instead of a written one (1.1.3, after RC5). Pure.
    /// </summary>
    public static List<Step> Thread(Said said, Facts asker, Facts answerer, Facts third, Memory memory, Random random, long now = 0,
        string known = null)
    {
        List<Step> steps = ThreadSteps(said, asker, answerer, third, memory, random, now, known);
        // "earlier tonight" is "earlier today" by day (1.1.3, after RC6).
        int? hour = (asker ?? answerer)?.LocalHour;
        if (hour is { } h)
            for (int i = 0; i < steps.Count; i++)
                steps[i] = steps[i] with { Text = AutonomousBotVoiceCorpus.ForTimeOfDay(steps[i].Text, h) };
        return steps;
    }

    private static List<Step> ThreadSteps(Said said, Facts asker, Facts answerer, Facts third, Memory memory, Random random, long now,
        string known)
    {
        var steps = new List<Step>(6);
        if (said == null)
            return steps;
        string answer = answerer == null || random.NextDouble() >= ReplyChance(said) ? null
            : !string.IsNullOrWhiteSpace(known) ? Finish(known, random) : Answer(said, answerer, memory, random, now);
        if (answer == null)
        {
            if (random.NextDouble() < FollowUpChance(false) && FollowUp(said, false, random, memory, now) is { } alone)
                steps.Add(new Step(0, alone, 90_000 + random.Next(120_000)));
            return steps;
        }
        int at = AutonomousChatArbiter.DelayMs(said.Text, answer, 40, 0, random);
        steps.Add(new Step(1, answer, at));
        // An answer that asks back ("where at?") is answered by the asker, and that is the thread: no "ty" to a question.
        if (answer.TrimEnd().EndsWith('?'))
        {
            if (AnswerFor(QuestionKind(answer), asker, random) is { } back)
                steps.Add(new Step(0, Finish(back, random), AutonomousChatArbiter.DelayMs(answer, back, 40, at, random)));
            return steps;
        }
        // The follow-up's answer gives what it asks ("where at?" a place, never a level).
        string FreshFitting(string[] pool, Facts speaker, string kind)
        {
            string[] fitting = pool.Where(text => Fits(Fill(text, speaker, random), kind)).ToArray();
            return fitting.Length == 0 ? null : Fresh(fitting, speaker);
        }
        // A line of three words or more is heard once an evening; with none fresh, the thread stops there.
        string Fresh(string[] pool, Facts speaker)
        {
            if (pool == null || pool.Length == 0)
                return null;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                string text = Fill(pool[random.Next(pool.Length)], speaker, random);
                string skeleton = AutonomousChatFiller.Skeleton(text);
                if (memory == null || skeleton.Split(' ').Length <= 2)
                    return text;
                if (memory.Seen(skeleton))
                    continue;
                memory.Add(skeleton);
                return text;
            }
            return null;
        }
        // RC6 on the owner's PC: "any grps out?" "quiet atm" ran on with "how many?" "small grp" "count me in". An answer that
        // says there is nothing doesn't run on, and isn't met with "omw".
        bool nothing = SaysNothing(answer);
        if (!nothing && random.NextDouble() < LongThreadChance && said.Family != null && FamilyMore.TryGetValue(said.Family, out var family))
        {
            var pairs = family.Pairs.Where(pair => !AlreadyAnswered(pair.Ask, answer)).ToArray();
            var pair = pairs.Length == 0 ? default : pairs[random.Next(pairs.Length)];
            if (pair.Ask != null && Fresh([pair.Ask], asker) is { } familyAsk && Fresh(pair.Reply, answerer) is { } familyReply)
            {
                at = AutonomousChatArbiter.DelayMs(answer, familyAsk, 40, at, random);
                steps.Add(new Step(0, familyAsk, at));
                at = AutonomousChatArbiter.DelayMs(familyAsk, familyReply, 40, at, random);
                steps.Add(new Step(1, familyReply, at));
                if (third != null && random.NextDouble() < 0.4 && Fresh(family.Chime, third) is { } familyChime)
                {
                    at = AutonomousChatArbiter.DelayMs(familyReply, familyChime, 40, at, random);
                    steps.Add(new Step(2, familyChime, at));
                }
                if (FollowUp(said, true, random, memory, now) is { } familyThanks)
                    steps.Add(new Step(0, familyThanks, AutonomousChatArbiter.DelayMs(familyReply, familyThanks, 40, at, random)));
                return steps;
            }
        }
        else if (!nothing && random.NextDouble() < LongThreadChance && said.Kind is not ("class" or "help") && RunsOnAbout(said) && More.TryGetValue(said.Kind, out var more) &&
            Fresh(more.Ask.Where(text => !AlreadyAnswered(text, answer)).ToArray(), asker) is { } ask &&
            (FreshFitting(more.Reply, answerer, QuestionKind(ask)) ?? (AnswerFor(QuestionKind(ask), answerer, random) is { } plain ? Finish(plain, random) : null)) is { } reply)
        {
            at = AutonomousChatArbiter.DelayMs(answer, ask, 40, at, random);
            steps.Add(new Step(0, ask, at));
            at = AutonomousChatArbiter.DelayMs(ask, reply, 40, at, random);
            steps.Add(new Step(1, reply, at));
            if (third != null && random.NextDouble() < 0.4 && Fresh(more.Chime, third) is { } chime)
            {
                at = AutonomousChatArbiter.DelayMs(reply, chime, 40, at, random);
                steps.Add(new Step(2, chime, at));
            }
            if (FollowUp(said, true, random, memory, now) is { } thanks)
                steps.Add(new Step(0, thanks, AutonomousChatArbiter.DelayMs(reply, thanks, 40, at, random)));
            return steps;
        }
        // A "dunno" isn't the end of it when someone else knows: the asker asks again and a third bot gives an answer from the
        // same true pool ("any grps in keltoi?" "not that i know" "anyone else?" "saw some in lfg").
        if (nothing && third != null && random.NextDouble() < LongThreadChance &&
            Answer(said, third, memory, random, now) is { } other && !SaysNothing(other) && !other.TrimEnd().EndsWith('?'))
        {
            string again = Pick(random, "anyone else?", "anyone?", "any1 else know?", "nobody?");
            at = AutonomousChatArbiter.DelayMs(answer, again, 40, at, random);
            steps.Add(new Step(0, again, at));
            at = AutonomousChatArbiter.DelayMs(again, other, 40, at, random);
            steps.Add(new Step(2, other, at));
            if (FollowUp(said, true, random, memory, now) is { } thanksOther)
                steps.Add(new Step(0, thanksOther, AutonomousChatArbiter.DelayMs(other, thanksOther, 40, at, random)));
            return steps;
        }
        if (random.NextDouble() < FollowUpChance(true) && FollowUp(said, true, random, memory, now) is { } done)
        {
            // After a "no" the asker says ok, not "omw" or "ty, inv" (RC6 and RC8 on the owner's PC).
            if (nothing && GoesSomewhere(done))
                done = Pick(random, "k ty", "ok ty", "k", "np ty");
            steps.Add(new Step(0, done, AutonomousChatArbiter.DelayMs(answer, done, 40, at, random)));
        }
        return steps;
    }

    /// <summary>
    /// Whether a line's kind-wide follow-ups ("how many?", "which way?") fit it: for rvr only a line about enemies or the zerg
    /// (RC7 on the owner's PC: "port to frontier up?" "ya, porter in camelot" ran on with "which way" "few grps"). Pure.
    /// </summary>
    public static bool RunsOnAbout(Said said) =>
        said?.Kind != "rvr" || said.Family is "rvr_grps" or "rvr_zerg" or "rvr_enemy";

    /// <summary>"quiet atm", "havent seen any", "dunno", "nope": an answer that says there is nothing to go on. Pure.</summary>
    public static bool SaysNothing(string answer)
    {
        if (string.IsNullOrWhiteSpace(answer))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(answer) + " ";
        return System.Text.RegularExpressions.Regex.IsMatch(t,
            @" (quiet|havent seen|haven t seen|not seen|no idea|no clue|dunno|nope|nobody|no one|none|nothing|not me|no enemies) ");
    }

    /// <summary>"omw", "porting", "k omw": a line that says the speaker is heading somewhere. Pure.</summary>
    public static bool GoesSomewhere(string line) =>
        !string.IsNullOrWhiteSpace(line) &&
        // RC8 on the owner's PC: "city of camelot anyone??" "not that i know" "ty, inv".
        System.Text.RegularExpressions.Regex.IsMatch(" " + AutonomousChatQuestions.Normalize(line) + " ", @" (omw|on my way|porting|heading|coming|inv|invite|inviting) ");

    /// <summary>
    /// A small story from a real event, for regulars to riff on for a minute or two (round 7): a ding to 50, a death, a wipe in
    /// a bot group. Three to six lines between two or three speakers, from their own state. Pure.
    /// </summary>
    public static List<Step> Story(string kind, Facts teller, Facts mate, Facts third, Random random, Memory memory = null,
        Func<string, bool> freshReaction = null, long now = 0)
    {
        var lines = new List<(int Who, string Text)>(6);
        // A line of three words or more is told once an evening: the next story picks another way to say it, or leaves it out.
        string Pick(Random r, params string[] options) => PickFor(0, r, options);
        // A riff ("oof", "rip") another bot said in the channel in the last ten minutes is picked again or left out (owner
        // 2026-11-08: "oof" from several bots within minutes).
        string Riff(Random r, params string[] options) => PickFor(1, r, options);
        string PickFor(int who, Random r, string[] options)
        {
            if (options == null || options.Length == 0)
                return null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                string text = options[r.Next(options.Length)];
                // The teller's own short line too (owner's PC, RC3 2026-11-09: once the longer openers had each been told,
                // every death story opened with "dead. again." and it was heard seven times in half an hour).
                if ((who != 0 || AutonomousChatExchange.IsShort(text)) && freshReaction != null && !freshReaction(text))
                    continue;
                string skeleton = AutonomousChatFiller.Skeleton(text);
                if (memory == null || skeleton.Split(' ').Length <= 2)
                    return text;
                if (memory.Seen(skeleton))
                    continue;
                memory.Add(skeleton);
                return text;
            }
            return null;
        }
        // A frontier death's own lines are remembered for the evening whatever their length (1.1.3, after RC5), and a short one
        // the channel heard in the last ten minutes is picked again or left out.
        string Told(Random r, string[] options)
        {
            if (options == null || options.Length == 0)
                return null;
            for (int attempt = 0; attempt < 8; attempt++)
            {
                string text = options[r.Next(options.Length)];
                if (AutonomousChatExchange.IsShort(text) && freshReaction != null && !freshReaction(text))
                    continue;
                string skeleton = AutonomousChatFiller.Skeleton(text);
                if (memory == null)
                    return text;
                if (memory.Seen(skeleton))
                    continue;
                memory.Add(skeleton);
                return text;
            }
            return null;
        }
        string monsters = teller?.Monster == null ? "adds" : AutonomousBotVoiceCorpus.Plural(teller.Monster);
        switch (kind)
        {
            case "ding50":
                lines.Add((0, Pick(random, "DING 50", "50!!!", "ding 50 finally", "50 :D", "DING 50!!")));
                lines.Add((1, Riff(random, "gz!!", "grats man", "gratz!", "about time lol", "gz gz")));
                if (third != null)
                    lines.Add((2, Riff(random, "gz", "grats!", "nice gz", "woot")));
                lines.Add((1, Pick(random, "what now, rvr?", "epic next?", "alt time?", "now the real game starts lol", "rvr now?", "time for rps?")));
                lines.Add((0, Pick(random, "rvr tomorrow, bed now", "epic first", "sleep lol", "frontier tomorrow", "alt maybe", "bed, gz me tomorrow lol")));
                break;
            case "death" when ToldAsRvrDeath(teller):
            {
                // A frontier death is to other players, not to a kind of monster (owner's PC, RC3 2026-11-09). 1.1.3, after RC5:
                // "got ganked lol" twice in 13 minutes, so each step has a wide pool, and the teller's line is kept out of the
                // evening's memory however short it is. After RC6: a death to an enemy (or a guard) is told so wherever it was,
                // and each line fits the real killer and how many were on the teller ("had 2 on me" after one enemy bot).
                string front = ShortZone(teller.Zone, random);
                string[] Fit(string[] options) => options.Where(text => FitsDeath(text, teller)).ToArray();
                string cls = teller.KillerClass == null ? null : ShortClass(teller.KillerClass, random);
                string aCls = cls == null ? null : ("aeiou".Contains(cls[0]) ? "an " : "a ") + cls;
                string[] openers = teller.KilledBy == "guard"
                    ? ["the guards got me", "died to a guard", "keep guards got me lol", "aggroed the guards, dead", "guards, dead. ugh",
                        $"guards at the {front} keep got me", "too close to the walls lol"]
                    : RvrDeathOpeners(front, random);
                if (aCls != null)
                    openers = [.. openers, $"died to {aCls}", $"{aCls} got me", $"{aCls} got me in {front}", $"killed by {aCls} lol"];
                // A place ("outside the gate", "milegate", "bridge") the realm heard in a death story in the last 30-60 minutes is
                // not told again (RC6: "outside the gate" twice in two minutes).
                bool PlaceRested(string text) => memory != null && DeathPlace(text) is { } place && memory.Rested(null, "death_place:" + place, now);
                string opener = Told(random, Fit(openers).Where(text => !PlaceRested(text)).ToArray());
                if (memory != null && DeathPlace(opener) is { } toldPlace)
                    memory.Use(null, "death_place:" + toldPlace, now);
                lines.Add((0, opener));
                // "where?" only when the teller didn't say where (as for a death to monsters).
                bool toldWhere = opener != null && (opener.Contains(front, StringComparison.OrdinalIgnoreCase) ||
                    new[] { "milegate", "bridge", "keep", "road", "gate", "lake" }.Any(word => opener.Contains(word, StringComparison.Ordinal)));
                string ask = toldWhere
                    ? Riff(random, "rip", "lol rip", "ouch", "oof", "by who?", "how many?", "lol again?", "ugh")
                    : Riff(random, "rip", "lol rip", "ouch", "where?", "oof", "where at?", "by who?", "how many?", "lol again?");
                lines.Add((1, ask));
                bool askedWhere = ask != null && ask.StartsWith("where", StringComparison.Ordinal);
                bool who = ask is "by who?" or "how many?";
                string[] whoPool = teller.KilledBy == "guard" ? ["the guards lol", "keep guards", "just the guards"]
                    : aCls != null ? [.. RvrDeathWho(random), $"{aCls}", $"just {aCls}", $"one {cls}"]
                    : RvrDeathWho(random);
                string answer = askedWhere ? Told(random, RvrDeathPlaces(front, random).Where(text => !PlaceRested(text)).ToArray())
                    : who ? Told(random, Fit(whoPool))
                    : Told(random, Fit(RvrDeathAfter(front, random)));
                // A question with no fresh answer left is not asked (no "where?" left hanging).
                if (answer == null && (askedWhere || who))
                    lines[^1] = (1, null);
                lines.Add((0, answer));
                if (third != null && random.NextDouble() < 0.5)
                {
                    // "same happened to me in ..." names a frontier: the third's own when it is out there too, else the teller's
                    // (RC5: "ganked in jamtland mtns" drew "same happened to me in dales of devwy", a hunting zone), and an enemy
                    // realm's frontier only as "their frontier" (the line speaks of the third's own past, as a PvE line would).
                    string mineZone = third.InRvR ? third.Zone : teller.Zone;
                    string mine = IsForeignZone(third.Realm, mineZone) ? "their frontier" : ShortZone(mineZone, random);
                    // RC8 on the owner's PC: "stick with the zerg" after "aggroed the guards, dead". A guard death gets guard riffs, and a
                    // death to one or two gets none about crowds.
                    lines.Add((2, teller.KilledBy == "guard"
                        ? Riff(random, "dont aggro the guards lol", "guards hit hard", "happens", "stay off the keep walls lol", "those guards are no joke")
                        : teller.Attackers is > 0 and <= 2
                            ? Riff(random, "bring a group", $"same happened to me in {mine}", "happens", "thats rvr", "solo in the frontier, brave",
                                "group up next time", "should've stayed with the grp", "welcome to the frontier lol")
                            : Riff(random, "stick with the zerg lol", "bring a group", $"same happened to me in {mine}", "happens", "thats rvr",
                                "they sit on that spot", "solo in the frontier, brave", "group up next time", $"{mine} is full of them tonight",
                                "should've stayed with the grp", "welcome to the frontier lol", "they're everywhere tonight")));
                }
                break;
            }
            case "death":
                // 1.1.3, after RC5: a monster death in an enemy realm's frontier is told without naming that realm's zone ("dread
                // commanders in jamtland mountains hate me" from an Albion bot).
                // After RC6: told with the monster that really killed the teller (its facts carry the death), and no count it
                // didn't have ("x3" for two, "train of" for one).
                string zone = HomeZoneWord(teller, random);
                string[] FitMonster(params string[] options) => options.Where(text => FitsDeath(text, teller)).ToArray();
                int count = teller?.Attackers ?? 0;
                string told = Pick(random, FitMonster($"died to {monsters} again", $"{monsters} got me lol", "dead. again.", $"train of {monsters}, im dead",
                    $"{monsters} in {zone} hate me", $"killed by {monsters} at {teller?.Level}"));
                lines.Add((0, told));
                // "where?" only when the teller didn't say where, and then the teller says where (owner 2026-11-08: "where?" was
                // answered "pulled two by accident").
                bool saidWhere = told != null && zone != null && told.Contains(zone, StringComparison.OrdinalIgnoreCase);
                string riff = saidWhere ? Riff(random, "rip", "lol rip", "ouch", "oof") : Riff(random, "rip", "lol rip", "ouch", "where?", "oof");
                lines.Add((1, riff));
                bool asked = riff != null && riff.StartsWith("where", StringComparison.Ordinal);
                lines.Add((0, asked
                    ? Pick(random, $"{zone}, near the camp", $"by the {zone} camp", $"in {zone}")
                    : Pick(random, FitMonster($"third time tonight in {zone}", "lost a bubble too", count >= 2 ? $"{monsters} x{count}, no chance" : $"{monsters} x3, no chance",
                        "pulled two by accident", count == 1 ? "it hit way too hard" : "too many at once"))));
                if (third != null && random.NextDouble() < 0.5)
                    lines.Add((2, Riff(random, $"they hit hard at {third.Level}", $"{monsters} are nasty at {teller?.Level}", "bring a healer lol",
                        $"same happened to me in {HomeZoneWord(third, random)}", "happens")));
                break;
            case "wipe" when KilledByEnemy(teller) || teller?.KilledBy == null && teller?.InRvR == true:
            {
                // RC8 on the owner's PC: a group wiped by enemy bots in the frontier told "wiped on adds" and "adds linked, all of
                // them". A wipe to enemies is told as one.
                string front = ShortZone(teller?.Zone, random);
                string enemy = Plural(EnemyWord(teller?.Realm ?? eRealm.None, random));
                lines.Add((0, Pick(random, "grp wiped lol", "whole grp got rolled", $"wiped by a {EnemyWord(teller?.Realm ?? eRealm.None, random)} grp",
                    $"fg wipe in {front}", "our grp got farmed", $"{enemy} rolled us")));
                lines.Add((1, Riff(random, "what happened?", "lol how", "rip grp", "ouch")));
                lines.Add((0, Pick(random, "they had a better healer", "got mezzed, gg", "they had more", "we went too far from the keep",
                    "stealthers first, then their fg", "lost our healer first")));
                if (third != null)
                    lines.Add((2, Riff(random, "happens", "thats rvr", "rip", "regroup and go again", "lol")));
                lines.Add((0, Pick(random, "back to it", "running back", "regrouping", "rezzing up")));
                break;
            }
            case "wipe":
                string where = ShortDungeon(teller?.Dungeon, random) ?? ShortZone(teller?.Zone, random);
                lines.Add((0, Pick(random, "wiped lol", "whole grp wiped", "wipe. ugh", $"wiped on {monsters}", $"grp wiped in {where}", $"{where} wipe lol")));
                lines.Add((1, Riff(random, "what happened?", "lol how", "rip grp", "ouch", "how?")));
                lines.Add((0, Pick(random, "pulled 3 at once", "healer went oom", "adds from nowhere", "someone pulled the named", $"{monsters} linked, all of them",
                    "tank ran lol", "a train came through", $"{monsters} respawned on us")));
                if (third != null)
                    lines.Add((2, Riff(random, "classic", "happens lol", "rip", $"{where} does that", "lol")));
                lines.Add((0, Pick(random, "back to it", "running back", "regrouping", $"back to {where}", "rezzing up")));
                break;
            default:
                return [];
        }
        // No story without its teller's first line (the riffs alone would answer nothing).
        if (lines.Count == 0 || lines[0].Text == null)
            return [];
        var steps = new List<Step>(lines.Count);
        int at = 0;
        string before = null;
        foreach ((int who, string text) in lines)
        {
            if (text == null)
                continue;
            string finished = text.Contains("DING") || text.Contains(":D") ? text : Finish(text, random);
            // "third time tonight" is "third time today" by day (1.1.3, after RC6).
            if (teller != null)
                finished = AutonomousBotVoiceCorpus.ForTimeOfDay(finished, teller.LocalHour);
            at = before == null ? 0 : AutonomousChatArbiter.DelayMs(before, finished, 40, at, random);
            steps.Add(new Step(who, finished, at));
            before = finished;
        }
        return steps;
    }

    /// <summary>
    /// How a frontier death is told (1.1.3, after RC5): who got the teller (a gank, a zerg, a stealther, a group, the guards) and
    /// where (a milegate, a bridge, a keep, the zone), in the words players typed. Several hundred ways; pure but for the draw.
    /// </summary>
    public static string[] RvrDeathOpeners(string front, Random random)
    {
        string[] how = ["ganked", "got ganked", "zerged", "got zerged", "stealthed", "got stealthed", "jumped", "got jumped", "picked off",
            "got picked off", "caught", "got caught", "farmed", "got farmed", "ran into a grp", "ran into a zerg", "ran into 8 of them",
            "a stealther got me", "an assassin got me", "a full grp got me", "the guards got me", "died to a guard", "3 of them on me",
            "4 v 1, dead", "1 v 5 lol, dead", "train of reds", "mezzed and dead"];
        string[] at = ["", "", $" in {front}", $" in {front}", " at the milegate", " by the bridge", " outside the keep", " near the keep",
            " on the road", " by the portal keep", $" at the {front} milegate", " right outside the gate", " by the lake", " on the bridge"];
        string[] tail = ["", "", "", " lol", " again", " ugh", ", rip me", " heh", ", never saw them", " zzz"];
        var options = new string[24];
        for (int i = 0; i < options.Length; i++)
            options[i] = how[random.Next(how.Length)] + at[random.Next(at.Length)] + tail[random.Next(tail.Length)];
        return options;
    }

    private static readonly System.Text.RegularExpressions.Regex DeathPlaces = new(@"\b(milegate|bridge|gate|portal keep|keep|road|lake|walls)\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>The place a frontier death line names ("outside the gate": "gate"), or null (1.1.3, after RC6). Pure.</summary>
    public static string DeathPlace(string text) =>
        text != null && DeathPlaces.Match(text) is { Success: true } match ? match.Value.ToLowerInvariant() : null;

    /// <summary>Where it happened, when asked "where?". Pure but for the draw.</summary>
    public static string[] RvrDeathPlaces(string front, Random random) =>
    [
        front, $"in {front}", $"{front}, near the keep", $"{front} milegate", $"by the bridge in {front}", $"outside the keep in {front}",
        $"{front}, on the road", $"{front} by the lake", "milegate", "right by our keep lol", $"{front}, just past the gate", $"middle of {front}",
    ];

    /// <summary>Who did it, when asked "by who?" or "how many?". Pure but for the draw.</summary>
    public static string[] RvrDeathWho(Random random) =>
    [
        "no idea, never saw them", "stealthers, 2 i think", "full grp", "like 8", "3 or 4", "a zerg, 20+", "just one stealther lol",
        "2 assassins", "a grp of 6 or so", "whole zerg", "too many to count", "a fg with a healer", "one caster, a lot of dots", "dunno, i was mezzed",
    ];

    /// <summary>What came after: how it went, in the teller's words. Pure but for the draw.</summary>
    public static string[] RvrDeathAfter(string front, Random random) =>
    [
        "never saw them coming", "lost a bubble too", "third time tonight", "had 2 on me before i could run", "was just running to the keep",
        "they were waiting at the gate", "didnt even get a cast off", "mezzed and stunned, gg", "should've waited for the grp", "rr is not happening tonight",
        $"{front} is a death trap tonight", "zerg came over the hill", "porting back", "they farm that bridge all night", "almost got away too",
        "was afk for like a second lol", "my grp ran lol", "at least i got one of them", "heading back out", "walk of shame back to the keep",
    ];

    private static string Fill(string text, Facts facts, Random random)
    {
        if (facts == null)
            return Finish(text.Replace("{cls}", "").Replace("{lvl}", "").Replace("{zone}", "").Replace("{dun}", "").Trim(), random);
        return Finish(text.Replace("{cls}", ShortClass(facts.ClassName, random)).Replace("{lvl}", facts.Level.ToString())
            .Replace("{zone}", ShortZone(facts.Zone, random)).Replace("{dun}", ShortDungeon(facts.Dungeon, random) ?? ShortZone(facts.Zone, random))
            .Replace("{monsters}", facts.Monster == null ? "adds" : AutonomousBotVoiceCorpus.Plural(facts.Monster)), random);
    }

    // ---- answers that fit the question (round 8) ------------------------------------------------------------------------------

    /// <summary>
    /// What a question asks for (round 8: "where at??" got "43"): "place" for where, which camp or side, how far; "price" for
    /// how much, worth, a price check; "count" for how many; "time" for when, what time, how long; "who" for who and which
    /// class; "level" for what level. Null for anything else (a yes or no, a call, a recruit). Pure.
    /// </summary>
    public static string QuestionKind(string text)
    {
        string t = " " + AutonomousChatQuestions.Normalize(text ?? string.Empty) + " ";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (how much|price|pc|worth|cost) "))
            return "price";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" how many "))
            return "count";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^ (where|wheres|where s)| where (at|is|r|are|do|did|exactly|in) | which (camp|side|way|zone|keep|room|dungeon) | (u|you) close | how far "))
            return "place";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^ (when|what time)| when | what time | how long "))
            return "time";
        // RC6 on the owner's PC: "what class?" was answered "coming from mount collory".
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (what|which|wat) class "))
            return "class";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^ (who|whos|who s) | (what|which) race "))
            return "who";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (what|wat) (lvl|level) | lvl $| how high "))
            return "level";
        return null;
    }

    /// <summary>
    /// Whether a (normalised) answer names a class, in full or as players shortened it ("cab", "pally", "sb", "arms"): the answer
    /// to "what class?", where "arms" is the class. Pure.
    /// </summary>
    public static bool NamesAClass(string normalized)
    {
        string t = " " + (normalized ?? string.Empty) + " ";
        foreach ((string name, string[] words) in ClassWords)
            if (t.Contains(" " + name.ToLowerInvariant() + " ", StringComparison.Ordinal) ||
                words.Any(word => t.Contains(" " + word + " ", StringComparison.Ordinal)))
                return true;
        return false;
    }

    private static readonly HashSet<string> NotAnAnswer = new(StringComparer.Ordinal)
        { "me", "x", "me pls", "x me", "inv pls", "sure", "sec", "later", "me!", "can i come", "sec, me", "ya", "yep", "k", "lol" };

    /// <summary>Whether an answer gives what its question asks for: a place for where, a price for how much, a time for when. Pure.</summary>
    public static bool Fits(string answer, string kind)
    {
        if (kind == null || string.IsNullOrWhiteSpace(answer))
            return true;
        string a = AutonomousChatQuestions.Normalize(answer);
        bool dunno = System.Text.RegularExpressions.Regex.IsMatch(" " + a + " ", @" (dunno|no idea|not sure|idk|ask) ");
        switch (kind)
        {
            case "place":
                return dunno || !NotAnAnswer.Contains(a) && !System.Text.RegularExpressions.Regex.IsMatch(a, @"(^\d|\b\d{1,2}\b)") &&
                       // RC7 on the owner's PC: "which way" was answered "few grps".
                       !System.Text.RegularExpressions.Regex.IsMatch(a, @"^(few|big|small|lots|couple|a few|many|\d+)( grps?| groups?| of them)?$");
            case "price":
                return dunno || System.Text.RegularExpressions.Regex.IsMatch(a, @"\b\d+ ?(p|g|s|plat|gold|silver)\b|not much|cheap|broker|offers|free|worth|nothing");
            case "count":
                return dunno || System.Text.RegularExpressions.Regex.IsMatch(a, @"\d|\b(few|big|small|lots|many|couple|bunch|handful|zerg|grp|grps|nobody|none)\b");
            case "time":
                return dunno || System.Text.RegularExpressions.Regex.IsMatch(a,
                    @"\d+ ?ish|\d+ ?(pm|am)\b|\b(tonight|later|tomorrow|sat|sun|weekend|after|earlier|ago|last night|morning|soon|now|dinner|lunch|tuesday|yesterday|hour|hours|min|mins|while|long)\b");
            case "who":
                return !System.Text.RegularExpressions.Regex.IsMatch(a, @"^\d+$");
            case "class":
                return dunno || NamesAClass(a);
            case "level":
                return dunno || System.Text.RegularExpressions.Regex.IsMatch(a, @"\d");
            default:
                return true;
        }
    }

    /// <summary>A plain answer of the asked kind from the answerer's own state, when no written answer fits. Pure.</summary>
    public static string AnswerFor(string kind, Facts answerer, Random random)
    {
        if (answerer == null)
            return kind is "time" or "price" or "count" ? "dunno" : null;
        string zone = ShortZone(answerer.Zone, random), dun = ShortDungeon(answerer.Dungeon, random);
        return kind switch
        {
            "place" => Pick(random, $"in {zone}", zone, dun != null ? $"near {dun}" : $"by {zone}", $"{zone}, near the camp"),
            "level" => Pick(random, answerer.Level.ToString(), $"{answerer.Level} {ShortClass(answerer.ClassName, random)}"),
            "time" => Pick(random, "dunno", "later tonight", "9ish", "in an hour"),
            "price" => Pick(random, PriceFor(answerer.Level, random), "dunno", "not much"),
            "count" => Pick(random, "few", "5 or 6", "big grp", "dunno"),
            "who" => Pick(random, ShortClass(answerer.ClassName, random), "dunno, ask the gl"),
            "class" => Pick(random, ShortClass(answerer.ClassName, random), $"{ShortClass(answerer.ClassName, random)} {answerer.Level}"),
            _ => null,
        };
    }

    // ---- help questions (round 7) --------------------------------------------------------------------------------------------

    /// <summary>
    /// A help question on something other than xp and spec (round 7: those frames dominated): gear, quests, travel, crafting,
    /// the frontier, trainers, named monsters, epic steps, binding, ports. Each frame rests and is capped (see
    /// <see cref="Memory.FrameAllowed"/>). Pure.
    /// </summary>
    private static Said Help(Facts f, Random random)
    {
        string cls = ShortClass(f.ClassName, random), lvl = f.Level.ToString(), zone = ShortZone(f.Zone, random);
        string dun = ShortDungeon(f.Dungeon, random);
        var ways = new List<V>(12)
        {
            new(f.InTown ? $"anyone know a good {lvl}ish quest?" : $"anyone know the {lvl}ish quest in {zone}?", "q_quest",
                Pool("did it, kill 10 then go back", "dunno that one", "ask the guard in town", "did it, easy one", "never did it", "skipped that one")),
            new($"where to hand in the {zone} quest?", "q_handin", Pool("back in town", "the guy that gave it", "back to whoever gave it", "check ur journal, it says who")),
            new($"how do i get to {f.World?.Frontier?.ToLowerInvariant() ?? "the frontier"} from here?", "q_frontier", Pool("port from the capital", "horse to the border keep", "porter in town")),
            new($"where do i bind in {zone}?", "q_bind", Pool("at the bindstone in town", "nearest town", "by the inn")),
            new($"anyone know where to buy {cls} stuff at {lvl}?", "q_gear", Pool("merchants in the capital", "crafters, pst one", "the broker")),
            new("what do i need to start crafting?", "q_craft", Pool("go see the crafting trainer", "tools from the merchant", "lots of money lol",
                $"crafting trainers in {CapitalWord(f.Realm)}, start there")),
            dun != null || !f.InTown ? new($"is there a port to {dun ?? zone}?", "q_port", Pool("no, run", "horse most of the way", "dunno"))
                : new("which porter goes to the frontier?", "q_port", Pool("the one by the gate", "porter in town", "ask a guard")),
        };
        // 1.1.3, after RC5: only an item the class can use ("99% adroit focus staff any good for pally?" on the owner's PC).
        if (UsesItem(f))
            ways.Add(new V($"{(f.ItemIsGear ? f.ItemQuality + "% " : "")}{f.Item.ToLowerInvariant()} any good for {cls}?", "q_item",
                Pool("not really", "ok for now", "sell it", "keep it till {lvl}")));
        if (f.Level >= 45)
            ways.Add(new V($"anyone done the {cls} epic?", "q_epic", Pool("not yet", "{healer}did mine last week", "need a grp for it", "ask in guild",
                "did it, long chain", "long chain, ur trainer starts it")));
        if (dun != null)
            ways.Add(new V($"what's the named in {dun} drop?", "q_named", Pool("dunno, never got it", "decent stuff", "jewelry mostly i think")));
        if (f.InRvR || f.Level >= 35)
            ways.Add(new V("how do realm points work?", "q_rps", Pool("kill enemies, get rps", "kills and keeps", "keeps give a lot",
                "kill enemy players in the frontier", "enemy kills, keeps and towers")));
        return Make(random, "help", (f.Level / 5) + ":" + zone, Pool("nm found it", "nobody? ok", "guess ill figure it out"), Pool("ty", "ty!", "k ty", "cool"),
            ways.ToArray());
    }

    // ---- words ---------------------------------------------------------------------------------------------------------------

    private static string Pick(Random random, params string[] options) => options[random.Next(options.Length)];

    /// <summary>Lowercase, no full stop, the odd question mark dropped: how most people typed in /faction.</summary>
    public static string Finish(string text, Random random)
    {
        string result = text.Trim().ToLowerInvariant().TrimEnd('.');
        if (result.EndsWith('?') && random.NextDouble() < 0.3)
            result = result.TrimEnd('?');
        return result;
    }

    private static readonly Dictionary<string, string[]> ClassWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["armsman"] = ["arms", "armsman"], ["paladin"] = ["pally", "paladin", "pal"], ["mercenary"] = ["merc"], ["reaver"] = ["reaver"],
        ["cleric"] = ["cleric", "clr"], ["friar"] = ["friar"], ["minstrel"] = ["mins", "minstrel", "minst"], ["scout"] = ["scout"],
        ["infiltrator"] = ["inf", "infil"], ["sorcerer"] = ["sorc"], ["wizard"] = ["wiz", "wizard"], ["theurgist"] = ["theu", "theurg"],
        ["cabalist"] = ["cab", "cabby"], ["necromancer"] = ["necro"], ["warrior"] = ["war", "warrior"], ["thane"] = ["thane"],
        ["skald"] = ["skald"], ["berserker"] = ["zerk", "zerker"], ["savage"] = ["sav", "savage"], ["hunter"] = ["hunter", "hunt"],
        ["shadowblade"] = ["sb"], ["healer"] = ["healer", "heal"], ["shaman"] = ["shaman", "shammy", "sham"], ["runemaster"] = ["rm"],
        ["spiritmaster"] = ["sm"], ["bonedancer"] = ["bd", "bonedancer"], ["hero"] = ["hero"], ["champion"] = ["champ"],
        ["blademaster"] = ["bm"], ["ranger"] = ["ranger"], ["nightshade"] = ["ns", "shade"], ["druid"] = ["druid"], ["bard"] = ["bard"],
        ["warden"] = ["warden"], ["eldritch"] = ["eld", "eldritch"], ["enchanter"] = ["ench", "chanter"], ["mentalist"] = ["menta", "ment"],
        ["animist"] = ["animist", "ani"], ["valewalker"] = ["vw", "valewalker"],
    };

    // Words that are a class in "lfg 30 heal" but usually something else in a question ("what spec for heals", "war").
    private static readonly HashSet<string> AmbiguousClassWords = new(StringComparer.Ordinal) { "heal", "war", "hunt", "arms", "pal", "sav", "shade", "ani", "ment", "sham" };

    /// <summary>The class a line names ("what spec for a 50 inf?" is Infiltrator), or null. Pure.</summary>
    public static string ClassNamed(string text)
    {
        string t = " " + AutonomousChatQuestions.Normalize(text ?? string.Empty) + " ";
        foreach ((string cls, string[] words) in ClassWords)
            if (words.Append(cls).Where(word => !AmbiguousClassWords.Contains(word)).Any(word => t.Contains(" " + word + " ", StringComparison.Ordinal) || t.Contains(" " + word + "s ", StringComparison.Ordinal)))
                return char.ToUpperInvariant(cls[0]) + cls[1..];
        return null;
    }

    /// <summary>A class as chat wrote it ("zerk", "sm", "pally"), or the lowercase name.</summary>
    public static string ShortClass(string className, Random random)
    {
        if (string.IsNullOrWhiteSpace(className))
            return "lvl";
        return ClassWords.TryGetValue(className.Trim(), out string[] words) ? words[random.Next(words.Length)] : className.Trim().ToLowerInvariant();
    }

    private static readonly Dictionary<string, string[]> DungeonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Tomb of Mithra"] = ["tomb", "mithra", "mithra tomb"], ["Tepok's Mine"] = ["tepoks", "tepok"], ["Keltoi Fogou"] = ["keltoi", "fogou"],
        ["Stonehenge Barrows"] = ["barrows", "sh barrows", "stonehenge"], ["Nisse's Lair"] = ["nisse", "nisses"], ["Cursed Tomb"] = ["cursed tomb", "ct"],
        ["Vendo Caverns"] = ["vendo", "vendos"], ["Varulvhamn"] = ["varul", "varulv"], ["Spindelhalla"] = ["spindel", "spindle"],
        ["Muire Tomb"] = ["muire", "muire tomb"], ["Spraggon Den"] = ["spraggon", "sprag den"], ["Koalinth Tribal Caverns"] = ["koalinth", "koal"],
        ["Treibh Caillte"] = ["tc", "treibh"], ["Coruscating Mine"] = ["cm", "coruscating"],
    };

    /// <summary>A dungeon as chat wrote it ("keltoi", "tc", "spindel"), or null.</summary>
    public static string ShortDungeon(string dungeon, Random random)
    {
        if (string.IsNullOrWhiteSpace(dungeon))
            return null;
        return DungeonWords.TryGetValue(dungeon.Trim(), out string[] words) ? words[random.Next(words.Length)] : dungeon.Trim().ToLowerInvariant();
    }

    /// <summary>A zone in lowercase, sometimes by its main word ("black mountains south" -> "black mtns"), never with "the".</summary>
    public static string ShortZone(string zone, Random random)
    {
        if (string.IsNullOrWhiteSpace(zone))
            return "here";
        string z = zone.Trim().ToLowerInvariant();
        if (z.StartsWith("the ", StringComparison.Ordinal))
            z = z[4..];
        if (random.NextDouble() < 0.3)
            z = z.Replace("mountains", "mtns").Replace("mountain", "mtn").Replace(" north", " n").Replace(" south", " s");
        return z;
    }

    /// <summary>A keep as chat wrote it: "Caer Benowyc" -> "beno" or "benowyc", "Bledmeer Faste" -> "bled", "Dun Crauchon" -> "crauch".</summary>
    public static string ShortKeep(string keep, Random random)
    {
        if (string.IsNullOrWhiteSpace(keep))
            return "the keep";
        string k = keep.Trim().ToLowerInvariant();
        foreach (string prefix in new[] { "caer ", "dun ", "fort ", "castle " })
            if (k.StartsWith(prefix, StringComparison.Ordinal))
            {
                k = k[prefix.Length..];
                break;
            }
        if (k.EndsWith(" faste", StringComparison.Ordinal))
            k = k[..^6];
        if (k.Length > 6 && random.NextDouble() < 0.5)
            k = k[..(random.Next(2) == 0 ? 4 : 5)];
        return k;
    }

    private static string EnemyWord(eRealm realm, Random random)
    {
        eRealm[] others = new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia }.Where(r => r != realm).ToArray();
        return RealmWord(GlobalConstants.RealmToName(others[random.Next(others.Length)]), random);
    }

    private static string RealmWord(string realm, Random random) => realm?.ToLowerInvariant() switch
    {
        "albion" => random.Next(3) == 0 ? "alb" : "albs",
        "midgard" => random.Next(3) == 0 ? "mid" : "mids",
        "hibernia" => random.Next(3) == 0 ? "hib" : "hibs",
        _ => "enemy",
    };

    private static string Since(Random random) => Pick(random, "last night", "this morning", "yesterday", "an hour ago", "lunch", "tuesday", "earlier");

    private static string Plural(string realmWord) => realmWord is "alb" or "mid" or "hib" ? realmWord + "s" : realmWord;

    private static string PriceFor(int level, Random random)
    {
        if (level >= 35)
            return $"{1 + random.Next(Math.Max(1, level / 10))}p";
        if (level >= 15)
            return $"{(1 + random.Next(9)) * 10}g";
        return $"{(1 + random.Next(9)) * 5}s";
    }

    // ---- answers for the questions that had none (1.1.3, after RC5) -----------------------------------------------------------
    // Owner's PC, RC3 and RC5 (2026-11-09): "where's the paladin trainer", "what spec for a 50 inf?", "necros, sc or drops at
    // 27?", "any stealthers out?", "fastest way to darkness falls?" were written with no answers, so nobody ever answered them.
    // Each now carries answers that fit it; where the live world has the fact, the coordinator says that instead (Thread's known).

    /// <summary>A realm's capital as chat wrote it, where every class trainer of the realm stands.</summary>
    public static string CapitalWord(eRealm realm) => realm switch
    {
        eRealm.Albion => "camelot",
        eRealm.Midgard => "jordheim",
        eRealm.Hibernia => "tir na nog",
        _ => "the capital",
    };

    /// <summary>A class's usual spec and weapon as players gave them (the classic lines of each class).</summary>
    private static readonly Dictionary<string, (string[] Spec, string[] Weapon)> ClassTips = new(StringComparer.OrdinalIgnoreCase)
    {
        ["armsman"] = (["polearm and shield", "max polearm, rest in shield", "2h or polearm, some shield"], ["a polearm", "polearm or a 2h", "a 2h"]),
        ["paladin"] = (["chants and 2h", "max chants, rest in 2h", "chants first, then slash or 2h"], ["a 2h", "2h, or slash and a shield"]),
        ["mercenary"] = (["dual wield and slash", "max dw, rest in slash", "dw first, then thrust or slash"], ["two slash weapons", "dw, slash or thrust"]),
        ["reaver"] = (["soulrending and flex", "max flex, rest in soulrending", "flex and soulrending, a bit of shield"], ["a flex weapon", "flex, whips are fine"]),
        ["cleric"] = (["rejuv, rest in enhance", "smite if u solo, rejuv for grps", "max rejuv for grps"], ["a mace and a shield", "crush, whatever drops"]),
        ["friar"] = (["staff and rejuv", "max staff, rest in rejuv", "staff and enhance"], ["a quarterstaff", "staff, always"]),
        ["minstrel"] = (["instruments, rest in slash", "max instruments", "instruments and stealth"], ["a slash blade", "rapier or slash, whatever drops"]),
        ["scout"] = (["longbow and stealth", "max longbow, some stealth", "bow first, shield later"], ["a longbow", "longbow, small shield for melee"]),
        ["infiltrator"] = (["critical strike, envenom and stealth", "cs and envenom, rest in stealth", "max cs, then envenom"], ["two thrust daggers", "dw thrust, slash ok"]),
        ["sorcerer"] = (["body for grps, mind for cc", "max mind, rest in body", "matter if u solo"], ["a staff", "staff, caster stats"]),
        ["wizard"] = (["fire for solo, earth for grps", "max fire", "earth if u want pbae"], ["a staff", "staff, caster stats"]),
        ["theurgist"] = (["air for grps, earth for solo", "max earth, pets do the work", "ice for dmg"], ["a staff", "staff, caster stats"]),
        ["cabalist"] = (["matter for solo, body for grps", "max matter", "body and spirit"], ["a staff", "staff, caster stats"]),
        ["necromancer"] = (["deathsight or painworking", "painworking for grps", "max deathsight for solo"], ["a staff", "staff, ur pet does the rest"]),
        ["warrior"] = (["shield and hammer", "max shield, rest in axe or hammer", "parry and a weapon line"], ["a 2h hammer or axe", "hammer and a shield"]),
        ["thane"] = (["stormcalling and hammer", "max stormcalling, rest in hammer", "hammer and shield, some storm"], ["a hammer", "hammer and a shield"]),
        ["skald"] = (["battlesongs and 2h", "max battlesongs", "songs and hammer"], ["a 2h hammer or axe", "a 2h"]),
        ["berserker"] = (["left axe and axe", "max left axe", "lefthand and axe, some parry"], ["two axes", "axes, both hands"]),
        ["savage"] = (["savagery and hand to hand", "max h2h, rest in savagery", "h2h first"], ["hand to hand claws", "h2h, both hands"]),
        ["hunter"] = (["beastcraft and composite bow", "spear or bow, beastcraft", "max bow, some stealth"], ["a composite bow", "a spear and a bow"]),
        ["shadowblade"] = (["critical strike and left axe", "cs and envenom", "max cs, then left axe"], ["two axes", "a 2h sword for cs"]),
        ["healer"] = (["mending, rest in pac", "pac for cc, mending for heals", "max mending for grps"], ["a hammer and a shield", "hammer, whatever drops"]),
        ["shaman"] = (["mending or cave", "cave for solo, mending for grps", "max mending"], ["a hammer and a shield", "hammer, whatever drops"]),
        ["runemaster"] = (["darkness or runecarving", "max darkness", "runes if u want pbae"], ["a staff", "staff, caster stats"]),
        ["spiritmaster"] = (["darkness or summoning", "darkness for solo", "max summoning for the pet"], ["a staff", "staff, caster stats"]),
        ["bonedancer"] = (["bone army", "max bone army", "army, rest in darkness"], ["a staff", "staff, the pets do the work"]),
        ["hero"] = (["large weapons and shield", "shield and blades", "max shield, rest in large"], ["a large weapon", "a spear or large weapon"]),
        ["champion"] = (["valor and large weapons", "max valor", "valor and blades"], ["a large weapon", "large or blades"]),
        ["blademaster"] = (["celtic dual and blades", "max celtic dual", "cd and piercing"], ["two blades", "blades in both hands"]),
        ["ranger"] = (["archery and pathfinding", "max recurve bow", "bow and pathfinding, some stealth"], ["a recurve bow", "recurve bow, blades for melee"]),
        ["nightshade"] = (["critical strike and stealth", "cs and piercing", "max cs, then stealth"], ["two piercers", "blades or piercing"]),
        ["druid"] = (["nurture or nature", "nurture for grps, nature for the pet", "max regrowth for heals"], ["blunt and a shield", "a hammer, whatever drops"]),
        ["warden"] = (["regrowth and nurture", "blades and nurture", "max nurture"], ["blades and a shield", "blunt or blades with a shield"]),
        ["bard"] = (["music and regrowth", "max music", "music and nurture"], ["blades", "blades or blunt, whatever drops"]),
        ["eldritch"] = (["light or void", "max light", "void for dmg"], ["a staff", "staff, caster stats"]),
        ["enchanter"] = (["light or enchantments", "light and mana", "enchant for the pet"], ["a staff", "staff, caster stats"]),
        ["mentalist"] = (["mentalism for heals, light for dmg", "max light", "mana and mentalism"], ["a staff", "staff, caster stats"]),
        ["animist"] = (["arboreal or creeping", "creeping for shrooms", "max arboreal"], ["a staff", "staff, the shrooms do the work"]),
        ["valewalker"] = (["scythe and arboreal", "max scythe", "scythe, some parry"], ["a scythe", "scythe, always"]),
    };

    /// <summary>Spec advice for a class ("cs and envenom, rest in stealth" for an infiltrator), or a general rule for one not listed. Pure.</summary>
    public static string[] SpecAdvice(string className) =>
        ClassTips.TryGetValue(className?.Trim() ?? string.Empty, out var tips) ? tips.Spec.Append("max ur main line first").ToArray()
            : Pool("max ur main line first", "main line, rest wherever", "ask ur trainer, he shows the lines");

    /// <summary>The weapon a class uses ("two thrust daggers" for an infiltrator). Pure.</summary>
    public static string[] WeaponAdvice(string className) =>
        ClassTips.TryGetValue(className?.Trim() ?? string.Empty, out var tips) ? tips.Weapon.Append("whatever ur spec is").ToArray()
            : Pool("whatever ur spec is", "best one that drops lol");

    /// <summary>Where a class trains: every class trainer of the realm stands in its capital. Pure.</summary>
    public static string[] TrainerAnswers(Facts f, Random random)
    {
        string cap = CapitalWord(f.Realm), cls = ShortClass(f.ClassName, random);
        return Pool($"your trainer in {cap}", $"in {cap}", $"{cap}, {cls} trainer is there", $"theres one in {cap}, ask a guard", $"{cap}, by the other trainers");
    }

    /// <summary>"sc or drops at 27?": drops while levelling, spellcrafted gear at the cap. Pure.</summary>
    public static string[] GearAdvice(int level) => level switch
    {
        < 35 => Pool("drops till 40ish", "drops, sc later", "drops, u outlevel it fast", "drops, save ur money"),
        < 45 => Pool("drops for now, sc at 45ish", "drops, sc at 50", "drops, unless u have money"),
        _ => Pool("sc if u can afford it", "sc for rvr", "sc, drops dont cut it at 50", "sc if u rvr"),
    };

    private static string[] TravelAnswers(eRealm realm) =>
        Pool("ask a stable master, they have routes", "horse most of the way", $"stable master in {CapitalWord(realm)}", "horse from the stable, then run");

    /// <summary>
    /// The armour a class wears at 50 as chat names it ("plate", "chain", "cloth", "scale"), by the game's item rule
    /// (<see cref="AutonomousChatGear.ArmorWord"/>; 1.1.3, after RC5), or null for a class it doesn't know. Pure.
    /// </summary>
    public static string ArmorOf(string className) => AutonomousChatGear.ArmorWord(className?.Trim(), 50);

    /// <summary>
    /// Something to buy for the bot's own class and level: armour of the weight the class wears then, by the game's own item
    /// rule (1.1.3, after RC5: "armor for a 29 cabalist" is cloth, a 15 paladin's is chain), or a slot, jewelry or pots.
    /// </summary>
    private static string WantFor(Facts f, Random random)
    {
        string armor = AutonomousChatGear.ArmorWord(f.ClassName?.Trim(), f.Level);
        string slot = Pick(random, "helm", "gloves", "boots", "sleeves", "legs", "vest", "arms");
        return random.Next(4) switch
        {
            0 when armor != null => $"{armor} {slot}",
            1 when AutonomousChatGear.UsesBow(f.ClassName) && random.Next(6) == 0 => "arrows",
            1 => Pick(random, "pots", "power pots", "heal pots", "dye", "a cloak", "a ring", "bracers", "a necklace", "a belt"),
            2 when armor != null => $"{f.Level} {armor} {slot}",
            _ => $"{slot} for lvl {f.Level}",
        };
    }

    // ---- the live parts ------------------------------------------------------------------------------------------------------

    private static readonly Dictionary<eRealm, (long At, WorldView View)> Views = new();
    private const long ViewMs = 30_000;

    /// <summary>The realm's view of the frontier, rebuilt at most every 30 seconds.</summary>
    public static WorldView World(eRealm realm)
    {
        long now = GameLoop.GameLoopTime;
        lock (Views)
            if (Views.TryGetValue(realm, out var cached) && now - cached.At < ViewMs && now >= cached.At)
                return cached.View;
        WorldView view = WorldView.Empty;
        try
        {
            AbstractGameKeep[] keeps = GameServer.KeepManager.GetFrontierKeeps().Where(keep => !keep.IsPortalKeep && !string.IsNullOrEmpty(keep.Name)).ToArray();
            AbstractGameKeep[] own = keeps.Where(keep => keep.Realm == realm).ToArray();
            AbstractGameKeep[] lost = keeps.Where(keep => keep.OriginalRealm == realm && keep.Realm != realm && keep.Realm != eRealm.None).ToArray();
            int relicsHeld = 0, relicsLost = 0;
            foreach (var relic in RelicMgr.GetRelics())
            {
                if (relic.Realm == realm && relic.OriginalRealm != realm)
                    relicsHeld++;
                else if (relic.OriginalRealm == realm && relic.Realm != realm && relic.Realm != eRealm.None)
                    relicsLost++;
            }
            AbstractGameKeep any = own.FirstOrDefault() ?? keeps.FirstOrDefault(keep => keep.OriginalRealm == realm);
            view = new WorldView(own.Select(keep => keep.Name).ToArray(), own.Where(keep => keep.InCombat).Select(keep => keep.Name).ToArray(),
                lost.Select(keep => keep.Name).ToArray(), lost.Select(keep => GlobalConstants.RealmToName(keep.Realm)).ToArray(), relicsHeld, relicsLost,
                any?.CurrentRegion?.GetZone(any.X, any.Y)?.Description);
        }
        catch
        {
            // No frontier loaded (tests, a minimal world): nothing to say about it.
        }
        lock (Views)
            Views[realm] = (now, view);
        return view;
    }

    /// <summary>The facts of a live bot, for one line.</summary>
    /// <summary>
    /// The kind of monster a bot is fighting, as players name it ("thrawn ogre miner"), or null. A target whose name starts with a
    /// capital is a person, a guard or a named creature, not a kind of monster: on the owner's PC (RC2 and RC3, 2026-11-08/09) most
    /// death stories in the frontier read "siuses in murdaigean hate me", "train of sneakywarriors, im dead" or "died to summoner
    /// roesias again". Pure.
    /// </summary>
    /// 1.1.3, after RC6: nor is a goal or an objective the controller wrote there ("frontier patrol near death stalkers", "grind
    /// camp ...", "Final encounter staging"): "died to frontier patrol near death stalkers ugh" in /faction.
    public static string MonsterKind(string targetName, bool playerLedGroup)
    {
        string target = targetName?.Trim();
        if (playerLedGroup || string.IsNullOrEmpty(target) || char.IsUpper(target[0]) || ObjectiveName.IsMatch(target))
            return null;
        return target.ToLowerInvariant();
    }

    // RC10 on the owner's PC: "careful group rendezvouses link" in /say. The goal names the controller writes into TargetName
    // (AutonomousBotVoices.IsPlaceName lists the same words) are no monster either.
    private static readonly System.Text.RegularExpressions.Regex ObjectiveName = new(
        @"\b(patrol|near|camp|grind|grinding|objective|staging|route|toward|towards|roam|roaming|rendezvous|muster|mustering|egress|" +
        @"matchmaking|formation|assignment|waypoint|meetup|gathering|escort|siege|assault)\b|[:/(]|\d",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    internal static Facts Of(GameBot bot, Random random)
    {
        AutonomousBotVoiceCorpus.Speaker speaker = AutonomousBotVoices.SpeakerFor(bot);
        var flags = speaker.Flags;
        OfflineWorldBotRecord record = bot.PersistentRecord;
        string zone = bot.CurrentZone?.Description;
        bool inDungeon = flags.Contains("in_dungeon");
        string item = null;
        int quality = 0;
        bool gear = false;
        long best = -1;
        int itemType = 0;
        bool? usable = null;
        if (bot.Inventory != null)
            foreach (var entry in bot.Inventory.AllItems)
            {
                if (entry == null || entry.SlotPosition < (int)eInventorySlot.FirstBackpack || entry.SlotPosition > (int)eInventorySlot.LastBackpack ||
                    string.IsNullOrWhiteSpace(entry.Name) || entry.Price <= best ||
                    // The level 50 starter kits ("Offline level 50 Reaver 3/10") are no item to sell (playtest 2026-11-04:
                    // "wts offline level 50 reaver 3/10 pst" in /trade).
                    !AutonomousBotChatCoordinator.IsAdvertisableItemName(entry.Name))
                    continue;
                best = entry.Price;
                item = entry.Name;
                quality = entry.Quality;
                itemType = entry.Object_Type;
                usable = entry.Template != null && GameServer.ServerRules?.CheckAbilityToUseItem(bot, entry.Template) == true;
                gear = entry.Item_Type is >= (int)eInventorySlot.RightHandWeapon and <= (int)eInventorySlot.DistanceWeapon or
                    (int)eInventorySlot.HeadArmor or (int)eInventorySlot.HandsArmor or (int)eInventorySlot.FeetArmor or (int)eInventorySlot.TorsoArmor or
                    (int)eInventorySlot.LegsArmor or (int)eInventorySlot.ArmsArmor;
            }
        string monster = MonsterKind(record?.TargetName, bot.IsPlayerLedGroup);
        // 1.1.3, after RC6: while its death is recent, the bot's monster is what killed it (none for an enemy or a guard):
        // after the release the controller writes an objective into TargetName ("frontier patrol near death stalkers").
        AutonomousBotVoices.DeathNote death = AutonomousBotVoices.RecentDeath(bot);
        if (death?.Killer == "monster")
            monster = death.Monster ?? monster;
        else if (death?.Killer is "guard" or "pet" or "bot" or "player")
            monster = null;
        int groupSize = bot.Group?.MemberCount ?? 1;
        // 1.1.3, after RC5: the bot's real group state. A solo bot wants a group when group matchmaking holds it or it is out
        // for rvr; a group recruits only while its party assembles with seats free (AutonomousBotGroupCoordinator).
        AutonomousBotGroupCoordinator.RecruitPost recruit = null;
        try
        {
            recruit = AutonomousBotGroupCoordinator.RecruitingPost(bot);
        }
        catch
        {
            // No coordinator state (tests, a minimal world): the bot isn't recruiting.
        }
        bool seeksGroup = bot.Group == null && (AutonomousObjectiveAssignments.IsAwaitingGroupMatchmaking(bot) ||
                                                AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR));
        string dungeon = inDungeon ? zone : recruit is { WhereIsDungeon: true } ? recruit.Where : AutonomousBotVoices.DungeonFor(bot.Realm, bot.Level, random);
        return new Facts(bot.DatabaseID, bot.Realm, bot.CharacterClass?.Name ?? bot.ClassName, bot.Level, zone,
            dungeon, flags.Contains("in_rvr"), inDungeon,
            flags.Contains("in_town") || flags.Contains("in_capital"), groupSize > 1, groupSize, flags.Contains("just_leveled"),
            flags.Contains("just_died"), flags.Contains("bag_full"), flags.Contains("at_camp"), monster, item, quality, gear,
            AutonomousBotVoices.TravelingTo(bot), AutonomousBotVoices.Bubbles(bot) ?? 0, DateTime.Now.Hour, flags.Contains("is_healer"),
            World(bot.Realm))
        {
            Live = true,
            SeeksGroup = bot.Group == null ? seeksGroup : null,
            RvrTask = AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.RvR),
            Recruit = recruit,
            ItemType = itemType,
            ItemUsable = usable,
            KilledBy = death?.Killer is "monster" or "guard" or "pet" or "bot" or "player" ? death.Killer : null,
            Attackers = death?.Attackers ?? 0,
            KillerClass = death?.KillerClass,
            WasAway = AutonomousChatEngagement.WasAway(bot.DatabaseID, GameLoop.GameLoopTime, flags.Contains("just_died") || bot.IsReturningAfterRelease),
            Dead = !bot.IsAlive,
        };
    }
}
