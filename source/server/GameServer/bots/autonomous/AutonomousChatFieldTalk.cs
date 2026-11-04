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
/// wordings. Lowercase, mostly unpunctuated, with the period's abbreviations. Pure but for <see cref="Of"/> and
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
    /// camp, travel, rvr, relic, crafter, and the guild kinds), and <paramref name="Answers"/>, short true answers another bot
    /// may give, or null. An answer may name the answerer's own {cls}, {lvl} and {zone} (see <see cref="Answer"/>).
    /// <paramref name="Item"/> is the item a sale names, so a realm doesn't hear one item advertised all evening.
    /// </summary>
    public sealed record Said(string Text, string Kind, string[] Answers, string Item = null);

    private static string[] Pool(params string[] answers) => answers;

    /// <summary>The skeletons a realm's field lines used lately, so the same line isn't said twice in an evening.</summary>
    public sealed class Memory
    {
        private readonly int _size;
        private readonly string[] _ring;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        private int _next;

        /// <param name="size">How many recent lines it holds: a realm's evening is several thousand (round 4: a 3-hour run).</param>
        public Memory(int size = 4096)
        {
            _size = Math.Max(16, size);
            _ring = new string[_size];
        }

        public bool Seen(string skeleton)
        {
            lock (_ring)
                return _seen.Contains(skeleton);
        }

        private readonly Dictionary<string, long> _items = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>How long one item rests after it was advertised (Beta 1.1.3, round 4: an hour).</summary>
        public const long ItemRestMs = 60 * 60_000;

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
    /// A field line for a channel (faction, say, lfg, trade, broadcast), or null when the facts give nothing to say there.
    /// Tries a few draws for one the realm hasn't heard lately. Pure.
    /// </summary>
    public static Said Line(string channel, Facts facts, Memory memory, Random random, long now = 0)
    {
        if (facts == null)
            return null;
        for (int attempt = 0; attempt < 8; attempt++)
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
                    _ => Say(facts with { InTown = true }, random),
                },
                "guild" => Guild(facts, random),
                _ => Faction(facts, random),
            };
            if (said == null || string.IsNullOrWhiteSpace(said.Text))
                continue;
            said = said with { Text = Finish(said.Text, random) };
            string skeleton = AutonomousChatFiller.Skeleton(said.Text);
            if (memory != null && memory.Seen(skeleton))
                continue;
            // One item advertised at most once an hour (round 4: three of 120 lines sold the same ring).
            if (said.Item != null && memory != null && !memory.TryItem(said.Item, now))
                continue;
            memory?.Add(skeleton);
            return said;
        }
        return null;
    }

    /// <summary>
    /// Another bot's short answer to a field line, in its own words: "me, thane 31" names the answerer's class and level. Tries
    /// a few for one the channel hasn't heard lately (lines of three words or more; "me" and "omw" may repeat). Null for none.
    /// Pure.
    /// </summary>
    public static string Answer(Said said, Facts answerer, Memory memory, Random random)
    {
        if (said?.Answers == null || said.Answers.Length == 0)
            return null;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            string text = said.Answers[random.Next(said.Answers.Length)];
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

    /// <summary>How likely another bot answers a field line in /faction.</summary>
    public static double AnswerChance(string kind) => kind switch
    {
        "ask_keep" or "relic" => 0.65,
        "ask_xp" or "travel" => 0.55,
        "inc" => 0.5,
        "ding" => 0.6,
        "death" => 0.4,
        "social" => 0.35,
        "ask_group" or "crafter" => 0.45,
        "camp" or "rvr" => 0.35,
        "raid" or "guild_group" or "guild_craft" or "guild_sell" => 0.45,
        "guild_ding" or "guild_hello" => 0.5,
        _ => 0.0,
    };

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
                $"{cls} lfg rvr", $"lfg {f.World.Frontier?.ToLowerInvariant() ?? "rvr"} {cls}"), "lfg", null);
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
            $"anyone selling {want}?",
            $"lf {want} pst", $"wtb {want}, paying ok", $"any1 selling {want}"), "wtb", null);
    }

    private static Said Faction(Facts f, Random random)
    {
        var options = new List<(Func<Said> Make, double Weight)>(10);
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
            options.Add((() => AskXp(f, random), 2));
            options.Add((() => AskGroup(f, random), 3));
        }
        if (f.AtCamp && f.Monster != null)
            options.Add((() => Camp(f, random), 1.5));
        if (f.Destination != null)
            options.Add((() => Travel(f, random), 1.5));
        options.Add((() => Crafter(f, random), 0.8));
        options.Add((() => Social(f, random), 1.2));
        if (f.Bubbles > 0 && f.Level < 50)
            options.Add((() => new Said(Pick(random, $"{f.Bubbles} bubbles to {f.Level + 1}", $"{f.Bubbles} bubs to go", $"almost {f.Level + 1}",
                $"{f.Bubbles} bubbles til {f.Level + 1} ugh"), "social", null), 0.6));
        double total = options.Sum(o => o.Weight), draw = random.NextDouble() * total;
        foreach ((Func<Said> make, double weight) in options)
        {
            if (draw < weight)
                return make();
            draw -= weight;
        }
        return Social(f, random);
    }

    private static Said Say(Facts f, Random random)
    {
        string cls = ShortClass(f.ClassName, random);
        if (f.InTown)
        {
            if (f.Item != null && random.NextDouble() < 0.4)
                return Trade(f, random);
            return new Said(Pick(random,
                $"lfg {f.Level} {cls}", "anyone grouping?", "brb", "back", "any healers around?", "anyone heading out?",
                f.Destination != null ? $"which stable for {ShortZone(f.Destination, random)}?" : "where is the trainer",
                $"{f.Level} {cls} lf grp", "afk 5", "anyone selling pots?", "brb selling"), "social", null);
        }
        string monster = f.Monster;
        string monsters = monster == null ? null : AutonomousBotVoiceCorpus.Plural(monster);
        if (f.InGroup)
            return new Said(Pick(random,
                "pulling", "inc", "oom", "sitting", "need pow", "brb", "rdy", "1 sec", "wait for heals",
                monster != null ? $"{monster} inc" : "adds", monster != null ? $"careful {monsters} link" : "careful"), "social", null);
        return new Said(Pick(random,
            "this camp taken?", "anyone grouping here?", $"lf grp here {f.Level} {cls}", "sry ks", "camp free if u want it", "anyone camping this?",
            monster != null ? $"{monster} up?" : "anything up?", monster != null ? $"anyone need {monster} drops?" : "anyone need anything?",
            monster != null ? $"who's camping {monsters}?" : "who's camping here?", "anyone wanna duo?", "oom brb", "careful adds",
            f.IsHealer ? "need heals?" : "any healers here?"), "camp", Pool("go ahead", "its free", "sure", "x me", "ya sry"));
    }

    // ---- the faction kinds -------------------------------------------------------------------------------------------------

    private static Said Inc(Facts f, WorldView w, Random random)
    {
        string keep = ShortKeep(w.AttackedKeeps[random.Next(w.AttackedKeeps.Length)], random);
        string enemy = EnemyWord(f.Realm, random);
        int count = 3 + random.Next(20);
        return new Said(Pick(random,
            $"inc {keep}", $"{keep} inc", $"{enemy} at {keep}", $"{count} {enemy} at {keep}", $"{keep} under attack", $"need def {keep}",
            $"def {keep} pls", $"{keep} is being hit", $"{enemy} zerg at {keep}", $"{enemy} hitting {keep} door", $"inc {keep}, {count}+",
            $"rams on {keep}"), "inc", Pool("omw", "omw", "k", "how many?", "porting", $"omw {keep}", $"omw to {keep}", $"porting to {keep}"));
    }

    private static Said AskKeep(Facts f, WorldView w, Random random)
    {
        int index = random.Next(w.LostKeeps.Length);
        string keep = ShortKeep(w.LostKeeps[index], random), owner = RealmWord(w.LostTo[index], random);
        return new Said(Pick(random, $"who has {keep}?", $"who has {keep}", $"{keep} still {owner}?", $"is {keep} ours?", $"anyone taking {keep} back?",
            $"{owner} still have {keep}?", $"grp for {keep}?"), "ask_keep",
            Pool(Plural(owner), $"{Plural(owner)} have {keep}", $"still {Plural(owner)}", $"{Plural(owner)} :/", $"{Plural(owner)} since {Since(random)}"));
    }

    private static Said Relic(Facts f, WorldView w, Random random)
    {
        if (w.RelicsLost > 0)
            return new Said(Pick(random, "who has our relic?", "who has the relic", "relic still gone?", "where is our relic", "any relic raid tonight?",
                "relic raid when?"), "relic", Pool("still gone", "not ours", "gone still", $"{EnemyWord(f.Realm, random)} have it", $"gone since {Since(random)}"));
        return new Said(Pick(random, $"we have {w.RelicsHeld} relics now", "still have their relic?", "relic still ours?", "anyone guarding the relic?"),
            "relic", Pool("still ours", "ya", "for now", $"ours since {Since(random)}"));
    }

    private static Said Rvr(Facts f, WorldView w, Random random)
    {
        string frontier = f.World?.Frontier?.ToLowerInvariant() ?? "the frontier";
        string own = w.OwnKeeps.Length > 0 ? ShortKeep(w.OwnKeeps[random.Next(w.OwnKeeps.Length)], random) : "the keep";
        return new Said(Pick(random,
            "any grps out?", "anyone out tonight?", $"anyone in {frontier}?", "rvr grp forming pst", "where's the zerg", "where is everyone",
            $"any1 at {own}?", $"quiet in {frontier}", "port to frontier up?", "any stealthers out?", "lf rvr grp", "anyone want to hit a keep?",
            $"{EnemyWord(f.Realm, random)} out?", "where are the enemies at"), "rvr",
            w.AttackedKeeps.Length > 0 ? Pool($"{ShortKeep(w.AttackedKeeps[0], random)} under attack", $"at {ShortKeep(w.AttackedKeeps[0], random)}")
                : Pool("not much", "dunno", "quiet", $"quiet at {own}", $"dead in {frontier}", $"{EnemyWord(f.Realm, random)} near {own}?"));
    }

    private static Said AskXp(Facts f, Random random)
    {
        string lvl = f.Level.ToString(), cls = ShortClass(f.ClassName, random);
        return new Said(Pick(random,
            $"where do {lvl}s xp?", $"where to xp at {lvl}?", $"good xp for {lvl} {cls}?", $"where should a {lvl} {cls} hunt",
            f.Dungeon != null ? $"{ShortDungeon(f.Dungeon, random)} ok at {lvl}?" : $"where at {lvl}?", $"{lvl} where to go", $"xp spot for {lvl}?"),
            "ask_xp", f.Dungeon != null ? Pool(ShortDungeon(f.Dungeon, random), $"try {ShortDungeon(f.Dungeon, random)}",
                $"{ShortDungeon(f.Dungeon, random)} prob", $"{ShortDungeon(f.Dungeon, random)} at {lvl}", $"{ShortDungeon(f.Dungeon, random)} if u get a grp",
                $"{ShortDungeon(f.Dungeon, random)} is ok at {lvl}", $"{ShortDungeon(f.Dungeon, random)} or solo outside") : null);
    }

    private static Said AskGroup(Facts f, Random random)
    {
        string dun = ShortDungeon(f.Dungeon, random), zone = ShortZone(f.Zone, random);
        return new Said(Pick(random,
            dun != null ? $"anyone up for {dun}?" : "anyone xping?", dun != null ? $"any grps in {dun}?" : "any grps out?",
            $"anyone wanna duo in {zone}?", $"anyone xping in {zone}?", $"lf duo {f.Level} {ShortClass(f.ClassName, random)}", $"{zone} anyone?",
            dun != null ? $"{dun} grp?" : "grp anyone?", "any healers on?", "any1 lf grp?", $"anyone near {zone}?"), "ask_group",
            Pool("me", "x", "pst", "me pls", "x me", "can i come", "o/", "me!", "inv pls", "later", "sec", "me, {cls} {lvl}", "{lvl} {cls} here", "{cls} {lvl}, x?", "room for a {cls}?", "whats ur lvl?", "after this camp, {zone}"));
    }

    private static Said Camp(Facts f, Random random)
    {
        string monsters = AutonomousBotVoiceCorpus.Plural(f.Monster), zone = ShortZone(f.Zone, random);
        return new Said(Pick(random,
            $"{monsters} in {zone} camped?", $"anyone camping {monsters}?", $"{monsters} drop anything good?", $"{f.Monster} up anyone?",
            $"who's on {monsters}?", $"{monsters} are camped lol", $"camping {monsters} if anyone wants to join"), "camp",
            Pool("dunno", "me", "free", $"free earlier in {zone}", $"{monsters} were free", "camped"));
    }

    private static Said Travel(Facts f, Random random)
    {
        string dest = ShortZone(f.Destination, random);
        return new Said(Pick(random, $"where is {dest}?", $"how do i get to {dest}", $"which stable for {dest}?", $"{dest} from here?",
            $"fastest way to {dest}?"), "travel", null);
    }

    private static Said Crafter(Facts f, Random random)
    {
        string craft = Pick(random, "armorcrafters", "weaponcrafters", "fletchers", "tailors", "spellcrafters", "alchemists");
        string one = craft[..^1], article = "aeiou".Contains(one[0]) ? "an" : "a";
        return new Said(Pick(random, $"any {craft} on?", $"need {article} {one} pst", $"any good {craft} around?", $"{one} on? pst"),
            "crafter", Pool("me, pst", "me", "/t me", $"{one} here", $"me, {one}"));
    }

    private static Said Ding(Facts f, Random random) =>
        new(Pick(random, $"ding {f.Level}", "ding", "ding!", $"{f.Level} finally", $"dinged {f.Level}", $"ding {f.Level} :)", $"{f.Level}!!"), "ding",
            Pool("gz", "grats", "gz!", "gratz", "nice gz"));

    private static Said Death(Facts f, Random random) =>
        new(Pick(random, "died lol", "rip me", f.Monster != null ? $"died to {AutonomousBotVoiceCorpus.Plural(f.Monster)} ugh" : "died ugh",
            "argh died", $"rez in {ShortZone(f.Zone, random)}?", "lost a bubble, ugh", "train lol, dead", "need rez pls"), "death",
            Pool("rip", "ouch", "lol rip", "oof"));

    private static Said Social(Facts f, Random random)
    {
        int hour = f.LocalHour;
        bool late = hour >= 23 || hour < 4, evening = hour is >= 17 and < 23;
        return new Said(Pick(random,
            "brb food", "brb", "back", "anyone on?", late ? "gn all" : evening ? "evening all" : "hi all", "quiet tonight", "who's on",
            late ? "one more lvl then bed" : "lol", "afk 5", "brb phone", "ok back", f.BagFull ? "bags full brb" : "brb drink",
            late ? "late crew still here" : "busy tonight"), "social", Pool("ya", "here", "o/", "wb", "lol"));
    }

    // ---- guild chat ----------------------------------------------------------------------------------------------------------

    /// <summary>
    /// A guild line from the bot's state and the guild's (round 4: guild ambient was still the old written lines, 39 of 40
    /// caught): a group call with its level and zone, raid and keep plans with the guild's real keep, a "gz" to a guildmate who
    /// really dinged, an "o/" to one who logged in, a crafter wanted, an old item offered, and the small talk of a channel
    /// ("brb", "back", "lag?", "night all"). Pure.
    /// </summary>
    private static Said Guild(Facts f, Random random)
    {
        var options = new List<(Func<Said> Make, double Weight)>(10);
        string lvl = f.Level.ToString(), cls = ShortClass(f.ClassName, random), zone = ShortZone(f.Zone, random);
        string dun = ShortDungeon(f.Dungeon, random);
        WorldView w = f.World ?? WorldView.Empty;
        if (f.GuildmateDinged != null)
        {
            string mate = f.GuildmateDinged.ToLowerInvariant();
            options.Add((() => new Said(Pick(random, $"gz {mate}", $"grats {mate}", $"gz {mate}!", $"nice {mate} gz", $"gratz {mate}"), "guild_ding",
                Pool("gz", "grats!", "gz gz", "woot")), 8));
        }
        if (f.GuildmateArrived != null)
        {
            string mate = f.GuildmateArrived.ToLowerInvariant();
            options.Add((() => new Said(Pick(random, $"o/ {mate}", $"hey {mate}", $"wb {mate}", $"{mate}!", $"yo {mate}"), "guild_hello",
                Pool("o/", "hey", "wb", "heya")), 8));
        }
        if (f.JustLeveled)
            options.Add((() => Ding(f, random) with { Kind = "guild_ding" }, 6));
        if (f.Level < 50 && !f.InRvR)
            options.Add((() => new Said(Pick(random,
                $"anyone want to group, {lvl} in {zone}", $"anyone around {lvl}? {zone}", $"any1 {Math.Max(1, f.Level - 2)}-{Math.Min(50, f.Level + 2)} wanna xp?",
                dun != null ? $"{dun} anyone? {lvl} {cls}" : $"xp anyone? {lvl} {cls}", $"lf guildies for {dun ?? zone}", $"who's {lvl}ish, wanna duo",
                $"need 1 more for {dun ?? zone}", $"{lvl} {cls} lf grp in guild?"), "guild_group",
                Pool("me", "x", "me pls", "sec", "me, {cls} {lvl}", "{lvl} {cls} here", "after this lvl", "can i come", "wheres that?", "sure, omw")), 3));
        if (f.Level >= 35)
            options.Add((() => new Said(Pick(random,
                "who's on for df tonight", "raid tonight?", "anyone up for a keep take later", "relic raid when?", "who's up for rvr after",
                w.LostKeeps.Length > 0 ? $"lets take {ShortKeep(w.LostKeeps[random.Next(w.LostKeeps.Length)], random)} back" : "keep run tonight?",
                f.GuildKeep != null ? $"anyone at {ShortKeep(f.GuildKeep, random)}?" : "df later?",
                f.GuildKeep != null ? $"{ShortKeep(f.GuildKeep, random)} doors ok?" : "rvr later anyone", "dragon this weekend?", "who's around for df"), "raid",
                Pool("me", "maybe", "after this lvl", "im in", "later maybe", "what time?", "count me in", "me if im on")), f.InRvR ? 3 : 1.5));
        if (f.GuildKeep != null && w.AttackedKeeps.Contains(f.GuildKeep))
            options.Add((() => new Said(Pick(random, $"{ShortKeep(f.GuildKeep, random)} under attack", $"our keep is being hit, {ShortKeep(f.GuildKeep, random)}",
                $"def {ShortKeep(f.GuildKeep, random)} pls", $"inc {ShortKeep(f.GuildKeep, random)}"), "inc", Pool("omw", "porting", "omw!", "on it")), 4));
        string craft = Pick(random, "smith", "armorcrafter", "tailor", "fletcher", "spellcrafter", "alchemist");
        int skill = 300 + 50 * random.Next(13);
        options.Add((() => new Said(Pick(random, $"need a {craft}, anyone {skill}+?", $"any {craft}s on? need {skill}+", $"{craft} on? pst",
            $"who's our best {craft} now", $"lf {craft} {skill}+"), "guild_craft",
            Pool("me, whats it for", "me, {lvl}?", "ask the gl", "/t me", "not me sry", "me but only 400", "sec, me")), 1));
        if (f.Item != null)
        {
            string item = f.Item.ToLowerInvariant();
            string article = "aeiou".Contains(item[0]) ? "an" : "a";
            options.Add((() => new Said(f.ItemIsGear
                ? Pick(random, $"selling my old {item} if anyone wants", $"anyone need {article} {item}? free for guild", $"got {article} {item} spare, anyone?",
                    $"{item} up for grabs", $"who wants {article} {item}", $"old {item} free to a guildie")
                : Pick(random, $"anyone need {item}?", $"got some {item} if anyone needs it", $"{item} free to whoever wants it", $"who collects {item}"), "guild_sell",
                Pool("me!", "me pls", "what lvl?", "me, i need one", "what stats?", "dibs"), item), 1.2));
        }
        bool late = f.LocalHour >= 23 || f.LocalHour < 4;
        options.Add((() => new Said(Pick(random, "brb", "back", "lag?", "lag", "brb food", "afk 5", late ? "night all" : "hey all", late ? "gn guild" : "o/",
            "quiet tonight", "who's on", "ok back", "brb phone", "laggy for anyone else?", late ? "bed, night" : "evening"), "social",
            Pool("ya", "o/", "here", "wb", "yep", "lag here too", "not for me")), 2.2));
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
