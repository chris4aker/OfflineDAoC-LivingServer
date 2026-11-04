using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// Short, functional chat built from what a bot really is and what the world really is (Beta 1.1.3, blind round 3: the
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

        /// <summary>What the speaker says a while later when nobody answered ("still lf healer", "nm found one").</summary>
        public string[] Unanswered { get; init; }

        /// <summary>What the speaker says after an answer ("ty", "k x").</summary>
        public string[] Thanks { get; init; }
    }

    private static string[] Pool(params string[] answers) => answers;

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
            Idea = way.Family == null ? null : subject == null ? way.Family : kind + ":" + subject,
            // "{s}" is the subject as said ("keltoi", "cleric"), not its key ("keltoi:4").
            Unanswered = unanswered?.Select(line => line.Replace("{s}", subject?.Split(':')[0] ?? string.Empty).Trim()).ToArray(),
            Thanks = way.Answers == null ? null : thanks,
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

        /// <summary>How long one item rests after it was advertised (Beta 1.1.3, round 4: an hour).</summary>
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
    public static Said Line(string channel, Facts facts, Memory memory, Random random, long now = 0)
    {
        if (facts == null)
            return null;
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
            string skeleton = AutonomousChatFiller.Skeleton(said.Text);
            // Short lines ("brb", "back", "lol") may repeat, as in the live near-copy rule; anything longer once an evening.
            bool reaction = skeleton.IndexOf(' ') < 0 || skeleton.Split(' ').Length <= 2 && skeleton.Length <= 10;
            if (memory != null && (!reaction && memory.Seen(skeleton) || memory.Rested(said.Family, said.Idea, now)))
                continue;
            // One item advertised at most once an hour (round 4: three of 120 lines sold the same ring).
            if (said.Item != null && memory != null && !memory.TryItem(said.Item, now))
                continue;
            memory?.Add(skeleton);
            memory?.Use(said.Family, said.Idea, now);
            return said;
        }
        return null;
    }

    /// <summary>
    /// Another bot's short answer to a field line, in its own words: "me, thane 31" names the answerer's class and level; an
    /// answer marked {healer} comes only from a healer. Tries a few for one the channel hasn't heard lately (three words or
    /// more; "me" and "omw" may repeat). Null for none. Pure.
    /// </summary>
    public static string Answer(Said said, Facts answerer, Memory memory, Random random)
    {
        if (said?.Answers == null || said.Answers.Length == 0)
            return null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string text = said.Answers[random.Next(said.Answers.Length)];
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
            string skeleton = AutonomousChatFiller.Skeleton(text);
            if (memory != null && skeleton.Split(' ').Length > 2)
            {
                if (memory.Seen(skeleton))
                    continue;
                memory.Add(skeleton);
            }
            return text;
        }
        return null;
    }

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
            if (memory != null && memory.Rested(null, key, now))
                continue;
            memory?.Use(null, key, now);
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
        // A group with room looks for more; a rvr-level bot looks for a rvr group now and then.
        if (f.InGroup && f.GroupSize is > 1 and < 8 && random.NextDouble() < 0.7)
        {
            int missing = Math.Clamp(8 - f.GroupSize, 1, 4);
            string need = Pick(random, "heal", "healer", "tank", "cc", "mezzer", "dps", "any", "caster", "support");
            string band = $"{Math.Max(1, f.Level - 3)}-{Math.Min(50, f.Level + 3)}";
            string where = dun ?? ShortZone(f.Zone, random);
            return new Said(Pick(random,
                $"lf{missing}m {where}", $"lf{missing}m {need} {where}", $"lfm {where} {band}", $"lf1m {need} {where} pst",
                $"lfm {band} {where}, need {need}", $"lf {need} for {where} pst", $"{where} grp lf{missing}m", $"lf{missing}m {band} xp grp {where}",
                $"need {need} {where} x me", $"lf1m {need}, {where}, {band}"), "lfm", null);
        }
        if ((f.InRvR || f.Level >= 40) && random.NextDouble() < 0.35)
            return new Said(Pick(random,
                $"lfg rvr {lvl} {cls}", $"{lvl} {cls} lf rvr grp", $"any rvr grps lf {cls}?", $"lf rvr grp {cls} {lvl} pst",
                $"{cls} lfg rvr", $"lfg {f.World?.Frontier?.ToLowerInvariant() ?? "rvr"} {cls}"), "lfg", null);
        string zone = ShortZone(f.Zone, random);
        return new Said(Pick(random,
            $"lfg {lvl} {cls}", $"{lvl} {cls} lfg", $"lfg {lvl} {cls} pst", $"lf grp {lvl} {cls}", $"{cls} {lvl} lfg",
            dun != null ? $"lfg {dun} {lvl} {cls}" : $"lfg {lvl} {cls} any",
            dun != null ? $"{lvl} {cls} lfg {dun}" : $"{lvl} {cls} lfg xp",
            $"{lvl} {cls} lf xp grp", $"any grps for {lvl} {cls}?", $"lfg {lvl} {cls}, {zone}", $"{lvl} {cls} lf duo {zone}",
            $"lfg {cls} {lvl} /t", $"{cls} lfg {lvl}, x me", $"{lvl} {cls} lfg anything"), "lfg", null);
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
        return new Said(Pick(random,
            $"wtb {want} pst", $"wtb {want}", want.Any(char.IsDigit) ? $"wtb {want} asap" : $"wtb {want} for {f.Level} {ShortClass(f.ClassName, random)}",
            $"anyone selling {want}?", $"lf {want} pst", $"wtb {want}, paying ok", $"any1 selling {want}"), "wtb", null);
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
            options.Add((() => AskXp(f, random), 1.5));
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
        options.Add((() => ClassTalk(f, random), 1.2));
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
                    new V("anyone selling pots?", "say_pots"), new V($"any {cls}s around? need tips", "say_tips"));
            return Make(random, "social", null, SeekStill, SeekThanks,
                new V($"lfg {f.Level} {cls}", null, Joins), new V("anyone grouping?", "say_grouping", Joins), new V("brb", "brb"),
                new V("back", "back"), new V("any healers around?", "say_healers", Pool("{healer}me", "{healer}me, {lvl}")),
                new V("anyone heading out?", "say_out", Pool("me", "in a bit", "me, where to?")),
                f.Destination != null ? new V($"which stable for {ShortZone(f.Destination, random)}?", "say_stable") : new V("where is the trainer", "say_trainer"),
                new V($"{f.Level} {cls} lf grp", null, Joins), new V("afk 5", "afk"), new V("anyone selling pots?", "say_pots"), new V("brb selling", "brb_selling"));
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
            new V(monster != null ? $"{monster} up?" : "anything up?", "say_up"),
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
        string own = w.OwnKeeps.Length > 0 ? ShortKeep(w.OwnKeeps[random.Next(w.OwnKeeps.Length)], random) : "the keep";
        string[] where = w.AttackedKeeps.Length > 0
            ? Pool($"{ShortKeep(w.AttackedKeeps[0], random)} under attack", $"at {ShortKeep(w.AttackedKeeps[0], random)}", $"hitting {ShortKeep(w.AttackedKeeps[0], random)}")
            : null;
        return Make(random, "rvr", null, Pool("anyone?", "nm"), Pool("ty", "k omw", "omw"),
            new V("any grps out?", "rvr_grps", where), new V("anyone out tonight?", "rvr_out", Pool("me", "few of us", "me, {cls}")),
            new V($"anyone in {frontier}?", "rvr_out", Pool("me", "few of us", "on my way")),
            new V("rvr grp forming pst", "rvr_form", Joins), new V("where's the zerg", "rvr_zerg", where), new V("where is everyone", "rvr_zerg", where),
            new V($"any1 at {own}?", "rvr_keep"), new V($"quiet in {frontier}", "rvr_quiet"), new V("port to frontier up?", "rvr_port"),
            new V("any stealthers out?", "rvr_stealth"), new V("lf rvr grp", "rvr_lf", Pool("x", "me too", "x me")),
            new V("anyone want to hit a keep?", "rvr_hit", Pool("me", "im in", "which one?", "x")), new V($"{EnemyWord(f.Realm, random)} out?", "rvr_enemy", where));
    }

    private static Said AskXp(Facts f, Random random)
    {
        string lvl = f.Level.ToString(), cls = ShortClass(f.ClassName, random);
        string dun = ShortDungeon(f.Dungeon, random);
        string[] answers = f.Dungeon == null ? null
            : Pool(dun, $"try {dun}", $"{dun} prob", $"{dun} at {lvl}", $"{dun} if u get a grp", $"{dun} is ok at {lvl}", $"{dun} or solo outside");
        return Make(random, "ask_xp", (f.Level / 5).ToString(), Pool("nm found one", $"anyone? {lvl} here", "nm, trying {s} solo", $"no one? ok {dun ?? "solo"} it is"),
            Pool("ty", "ty!", "k ty", "kk", "cool ty", "ty, omw"),
            new V($"where do {lvl}s xp?", "ask_xp", answers), new V($"good xp for {lvl} {cls}?", "ask_xp", answers),
            new V(dun != null ? $"{dun} ok at {lvl}?" : $"where at {lvl}?", "ask_xp_place",
                dun != null ? Pool("ya", "ya with a grp", "should be", "ya, bring a healer", "fine at that lvl") : answers),
            new V($"xp spot for {lvl}?", "ask_xp", answers));
    }

    private static Said AskGroup(Facts f, Random random)
    {
        string dun = ShortDungeon(f.Dungeon, random), zone = ShortZone(f.Zone, random), lvl = f.Level.ToString(), cls = ShortClass(f.ClassName, random);
        return Make(random, "ask_group", (dun ?? zone) + ":" + f.Level / 5, SeekStill, SeekThanks,
            new V(dun != null ? $"anyone up for {dun}?" : "anyone xping?", "group_up", Joins),
            new V(dun != null ? $"any grps in {dun}?" : "any grps out?", "group_any"),
            new V($"anyone wanna duo in {zone}?", "group_duo", Joins), new V($"lf duo {lvl} {cls}", "group_duo", Joins),
            new V($"anyone xping in {zone}?", "group_where"), new V($"{zone} anyone?", "group_where"),
            new V(dun != null ? $"{dun} grp?" : "grp anyone?", "group_up", Joins), new V("any1 lf grp?", "group_lf", Pool("me", "x", "me, {lvl}", "me pls")),
            new V($"anyone near {zone}?", "group_where"));
    }

    private static Said NeedClass(Facts f, Random random)
    {
        string need = Pick(random, "healer", "tank", "mezzer", "bard", "cleric", "druid", "shaman", "puller");
        string dun = ShortDungeon(f.Dungeon, random) ?? ShortZone(f.Zone, random);
        string[] fits = need is "healer" or "cleric" or "druid" or "shaman" ? Pool("{healer}me", "{healer}me, {lvl}", "{healer}x", "{healer}{lvl} {cls} here") : null;
        return Make(random, "ask_group", need + ":" + dun + ":" + f.Level / 5, SeekStill, SeekThanks,
            new V($"any {need}s on?", "need_class", fits), new V($"need a {need} for {dun}", "need_class", fits),
            new V($"lf {need} {f.Level - 2}-{f.Level + 2}", "need_class", fits), new V($"{need} anyone? {dun}", "need_class", fits));
    }

    private static Said Camp(Facts f, Random random)
    {
        string monsters = AutonomousBotVoiceCorpus.Plural(f.Monster), zone = ShortZone(f.Zone, random);
        return Make(random, "camp", f.Monster, Pool("nm got it", $"nm, {monsters} were free", "nvm found a spot"), Pool("ty", "k", "kk"),
            new V($"{monsters} in {zone} free?", "camp_free"), new V($"anyone camping {monsters}?", "camp_free"),
            new V($"{monsters} drop anything good?", "camp_drops"), new V($"{f.Monster} up anyone?", "camp_up"),
            new V($"camping {monsters} if anyone wants to join", "camp_join", Pool("me", "where at?", "x", "me, {lvl}")));
    }

    private static Said Travel(Facts f, Random random)
    {
        string dest = ShortZone(f.Destination, random);
        return Make(random, "travel", dest, Pool("nm found it", $"nm, found {dest}"), null,
            new V($"where is {dest}?", "ask_way"), new V($"how do i get to {dest}", "ask_way"), new V($"which stable for {dest}?", "ask_stable"),
            new V($"fastest way to {dest}?", "ask_way"));
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
        string[] answers = dun == null ? null : Pool($"{dun}", $"try {dun}", $"{dun} with a grp", $"outside {dun}");
        return Make(random, "camp", "ask:" + zone + ":" + f.Level / 3, Pool($"anyone? {lvl} here", "nm found one", "ok, guess ill wander"),
            Pool("ty", "k ty", "ty, omw", "cool"),
            new V($"good camp for {lvl}?", "camp_ask", answers), new V($"where do {lvl}s camp in {zone}?", "camp_ask_zone", answers),
            new V($"{zone} camp for {lvl}?", "camp_ask_zone", answers), new V($"anything good to camp at {lvl}?", "camp_ask", answers));
    }

    /// <summary>"lf X for Y" with a real dungeon for the level (round 6).</summary>
    private static Said LfFor(Facts f, Random random)
    {
        string dun = ShortDungeon(f.Dungeon, random) ?? ShortZone(f.Zone, random);
        string need = Pick(random, "1 more", "2 more", "a healer", "a tank", "dps", "a puller", "cc");
        return Make(random, "ask_group", "lf:" + dun + ":" + f.Level / 4, SeekStill, SeekThanks,
            new V($"lf {need} for {dun}", "lf_for", Joins), new V($"{dun}, lf {need}", "lf_for", Joins),
            new V($"lf {need} {dun} {Math.Max(1, f.Level - 2)}-{Math.Min(50, f.Level + 2)}", "lf_for", Joins), new V($"need {need} for {dun}", "lf_for", Joins));
    }

    /// <summary>"anyone seen the named in keltoi?" (round 6): a question, answered with what a bot could know, or nothing.</summary>
    private static Said Named(Facts f, Random random)
    {
        string dun = f.InDungeon ? ShortZone(f.Zone, random) : ShortDungeon(f.Dungeon, random);
        return Make(random, "camp", "named:" + dun, Pool("nm", "guess it's down"), Pool("ty", "k ty"),
            new V($"anyone seen the named in {dun}?", "named", Pool("not tonight", "dunno", "was up earlier")),
            new V($"{dun} named up?", "named", Pool("dunno", "not when i left", "think so")),
            new V($"who's camping the {dun} named", "named"));
    }

    /// <summary>Class talk tied to the speaker's real class and level (round 6).</summary>
    private static Said ClassTalk(Facts f, Random random)
    {
        string cls = ShortClass(f.ClassName, random), lvl = f.Level.ToString();
        return Make(random, "class", (f.ClassName ?? "").ToLowerInvariant() + ":" + f.Level / 5, Pool("nobody? ok lol", "nm figured it out"),
            Pool("ty", "ty!", "makes sense ty", "k ty"),
            new V($"{cls}s, sc or drops at {lvl}?", "class_gear"), new V($"what weapon for a {lvl} {cls}?", "class_weapon"),
            new V($"{cls} {lvl}, what should i put points in?", "class_spec"), new V($"any {cls}s on? quick q", "class_q", Pool("me, whats up", "me", "sup")),
            new V($"is {cls} fun after {f.Level + 5}?", "class_later"), new V($"{cls}s, where did u xp at {lvl}?", "class_xp"));
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
        ways.Add(new V($"{f.Level} {ShortClass(f.ClassName, random)}, still grinding", "status_grind"));
        ways.Add(new V($"back in {zone}", "status_zone"));
        ways.Add(new V(f.IsHealer ? "sitting for pow" : "sitting, need hp", "status_sit"));
        return Make(random, "social", (f.Monster ?? f.Destination ?? zone)?.ToLowerInvariant() + ":" + f.Level / 5, null, null, ways.ToArray());
    }

    /// <summary>Asking the realm for something: an item, a trainer, a spec (round 5: more ways to ask, each its own idea).</summary>
    private static Said AskAround(Facts f, Random random)
    {
        string cls = ShortClass(f.ClassName, random), lvl = f.Level.ToString();
        string want = WantFor(f, random);
        return Make(random, "ask", cls + ":" + want, Pool("nm found it", $"nobody? ok", $"still need {want}"), Pool("ty", "ty!", "k ty", "cool"),
            new V($"anyone have {want}?", "ask_have"), new V($"anyone got spare {want}", "ask_have"),
            new V($"where's the {cls} trainer", "ask_trainer"), new V($"what spec for a {lvl} {cls}?", "ask_spec"),
            new V($"{cls}s, what do u put points in at {lvl}?", "ask_spec"),
            want.Any(char.IsDigit) ? new V($"anyone selling {want}?", "ask_have") : new V($"is {want} worth it at {lvl}?", "ask_worth"));
    }

    private static Said Crafter(Facts f, Random random)
    {
        string craft = Pick(random, "armorcrafters", "weaponcrafters", "fletchers", "tailors", "spellcrafters", "alchemists");
        string one = craft[..^1], article = "aeiou".Contains(one[0]) ? "an" : "a";
        return Make(random, "crafter", one + ":" + f.Level / 10, Pool("nm found one", "still lf " + article + " " + one, $"any {craft}? anyone", $"no {craft} on? ok"), null,
            new V($"any {craft} on?", "need_crafter"), new V($"need {article} {one} pst", "need_crafter"), new V($"any good {craft} around?", "need_crafter"),
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
            Unanswered = Pool("off to train", $"{f.Level + 1} next lol", "trainer time", "finally lol"),
        };

    private static Said Death(Facts f, Random random) =>
        new(Pick(random, "died lol", "rip me", f.Monster != null ? $"died to {AutonomousBotVoiceCorpus.Plural(f.Monster)} ugh" : "died ugh",
            "argh died", $"rez in {ShortZone(f.Zone, random)}?", "lost a bubble, ugh", "train lol, dead", "need rez pls"), "death",
            Pool("rip", "ouch", "lol rip", "oof"))
        {
            Thanks = Pool("ty", "heh ty", "ugh ya"),
            Unanswered = Pool("released, running back", "got a rez ty", "back up", "ok running back"),
        };

    private static Said Social(Facts f, Random random)
    {
        int hour = f.LocalHour;
        bool late = hour >= 23 || hour < 4, evening = hour is >= 17 and < 23;
        return Make(random, "social", null, null, null,
            new V("brb food", "brb_food"), new V("brb", "brb"), new V("back", "back", Pool("wb", "wb!")), new V("anyone on?", "anyone_on", Pool("ya", "here", "o/")),
            new V(late ? "gn all" : evening ? "evening all" : "hi all", "hello_all", late ? Pool("gn", "night") : Pool("o/", "hey", "heya")),
            new V("quiet tonight", "quiet"), new V("who's on", "anyone_on", Pool("me", "here", "o/")),
            new V(late ? "one more lvl then bed" : "lol", late ? "bedtime" : "lol"), new V("afk 5", "afk"), new V("brb phone", "brb_phone"),
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
                new V($"anyone want to group, {lvl} in {zone}", "g_group", Joins), new V($"anyone around {lvl}? {zone}", "g_around", Joins),
                new V($"any1 {Math.Max(1, f.Level - 2)}-{Math.Min(50, f.Level + 2)} wanna xp?", "g_group", Joins),
                new V(dun != null ? $"{dun} anyone? {lvl} {cls}" : $"xp anyone? {lvl} {cls}", "g_dun", Joins),
                new V($"lf guildies for {dun ?? zone}", "g_dun", Joins), new V($"who's {lvl}ish, wanna duo", "g_duo", Joins),
                new V($"need 1 more for {dun ?? zone}", "g_more", Joins), new V($"{lvl} {cls} lf grp in guild?", "g_group", Joins)), 3));
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
            new V($"need a {craft}, anyone {skill}+?", "g_craft"), new V($"any {craft}s on? need {skill}+", "g_craft"), new V($"{craft} on? pst", "g_craft"),
            new V($"who's our best {craft} now", "g_craft_best", Pool("ask the gl", "dunno", "not me lol")), new V($"lf {craft} {skill}+", "g_craft")), 1));
        if (f.Item != null)
        {
            string item = f.Item.ToLowerInvariant();
            string article = "aeiou".Contains(item[0]) ? "an" : "a";
            string[] want = Pool("me!", "me pls", "what lvl?", "me, i need one", "what stats?", "dibs");
            options.Add((() => (f.ItemIsGear
                ? Make(random, "guild_sell", item, Pool("nobody? ok merchant it is", "last call"), Pool("sending", "trade me", "k, where r u"),
                    new V($"selling my old {item} if anyone wants", "g_sell", want), new V($"anyone need {article} {item}? free for guild", "g_give", want),
                    new V($"got {article} {item} spare, anyone?", "g_give", want), new V($"{item} up for grabs", "g_give", want),
                    new V($"who wants {article} {item}", "g_give", want), new V($"old {item} free to a guildie", "g_give", want))
                : Make(random, "guild_sell", item, null, Pool("sending", "trade me"),
                    new V($"anyone need {item}?", "g_drop", Pool("me", "what for?", "nah")), new V($"got some {item} if anyone needs it", "g_drop", Pool("me", "how many?")),
                    new V($"{item} free to whoever wants it", "g_drop", want), new V($"who collects {item}", "g_drop", Pool("me lol", "nobody lol")))) with { Item = item }, 1.2));
        }
        bool late = f.LocalHour >= 23 || f.LocalHour < 4;
        options.Add((() => Make(random, "social", null, null, null,
            new V("brb", "g_brb"), new V("back", "g_back", Pool("wb", "wb!")), new V("lag?", "g_lag", Pool("lag here too", "not for me", "ya, bad")),
            new V("lag", "g_lag", Pool("same", "ya")), new V("brb food", "g_food"), new V("afk 5", "g_afk"),
            new V(late ? "night all" : "hey all", "g_hello", late ? Pool("night", "gn") : Pool("o/", "hey", "heya")),
            new V(late ? "gn guild" : "o/", "g_hello", late ? Pool("night", "gn") : Pool("o/", "hey")),
            new V("quiet tonight", "g_quiet"), new V("who's on", "g_who", Pool("me", "here", "o/")), new V("ok back", "g_back", Pool("wb")),
            new V("brb phone", "g_phone"), new V("laggy for anyone else?", "g_lag", Pool("ya", "fine here", "bit")),
            new V(late ? "bed, night" : "evening", "g_hello", late ? Pool("night", "gn") : Pool("o/", "evening"))), 2.2));
        double total = options.Sum(o => o.Weight), draw = random.NextDouble() * total;
        foreach ((Func<Said> make, double weight) in options)
        {
            if (draw < weight)
                return make();
            draw -= weight;
        }
        return null;
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

    private static readonly HashSet<string> Plate = new(StringComparer.OrdinalIgnoreCase) { "armsman", "paladin" };

    private static readonly HashSet<string> Chain = new(StringComparer.OrdinalIgnoreCase)
        { "mercenary", "reaver", "cleric", "minstrel", "warrior", "thane", "skald", "healer", "shaman" };

    private static readonly HashSet<string> Cloth = new(StringComparer.OrdinalIgnoreCase)
    {
        "wizard", "sorcerer", "theurgist", "cabalist", "necromancer", "runemaster", "spiritmaster", "bonedancer", "eldritch", "enchanter",
        "mentalist", "animist", "valewalker",
    };

    private static readonly HashSet<string> Studded = new(StringComparer.OrdinalIgnoreCase) { "berserker", "savage", "hunter", "scout" };
    private static readonly HashSet<string> Leather = new(StringComparer.OrdinalIgnoreCase) { "infiltrator", "shadowblade", "nightshade" };

    private static string WantFor(Facts f, Random random)
    {
        string cls = f.ClassName?.Trim() ?? string.Empty;
        string armor = Plate.Contains(cls) ? "plate" : Chain.Contains(cls) ? "chain" : Cloth.Contains(cls) ? "cloth" : Studded.Contains(cls) ? "studded"
            : Leather.Contains(cls) ? "leather" : null;
        string slot = Pick(random, "helm", "gloves", "boots", "sleeves", "legs", "vest", "arms");
        return random.Next(4) switch
        {
            0 when armor != null => $"{armor} {slot}",
            1 => Pick(random, "pots", "power pots", "heal pots", "dye", "arrows", "a cloak", "a ring", "bracers", "a necklace", "a belt"),
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
        if (bot.Inventory != null)
            foreach (var entry in bot.Inventory.AllItems)
            {
                if (entry == null || entry.SlotPosition < (int)eInventorySlot.FirstBackpack || entry.SlotPosition > (int)eInventorySlot.LastBackpack ||
                    string.IsNullOrWhiteSpace(entry.Name) || entry.Price <= best)
                    continue;
                best = entry.Price;
                item = entry.Name;
                quality = entry.Quality;
                gear = entry.Item_Type is >= (int)eInventorySlot.RightHandWeapon and <= (int)eInventorySlot.DistanceWeapon or
                    (int)eInventorySlot.HeadArmor or (int)eInventorySlot.HandsArmor or (int)eInventorySlot.FeetArmor or (int)eInventorySlot.TorsoArmor or
                    (int)eInventorySlot.LegsArmor or (int)eInventorySlot.ArmsArmor;
            }
        string monster = !string.IsNullOrWhiteSpace(record?.TargetName) && !bot.IsPlayerLedGroup ? record.TargetName.Trim().ToLowerInvariant() : null;
        int groupSize = bot.Group?.MemberCount ?? 1;
        return new Facts(bot.DatabaseID, bot.Realm, bot.CharacterClass?.Name ?? bot.ClassName, bot.Level, zone,
            inDungeon ? zone : AutonomousBotVoices.DungeonFor(bot.Realm, bot.Level, random), flags.Contains("in_rvr"), inDungeon,
            flags.Contains("in_town") || flags.Contains("in_capital"), groupSize > 1, groupSize, flags.Contains("just_leveled"),
            flags.Contains("just_died"), flags.Contains("bag_full"), flags.Contains("at_camp"), monster, item, quality, gear,
            AutonomousBotVoices.TravelingTo(bot), AutonomousBotVoices.Bubbles(bot) ?? 0, DateTime.Now.Hour, flags.Contains("is_healer"),
            World(bot.Realm));
    }
}
