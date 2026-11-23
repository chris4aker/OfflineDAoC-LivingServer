using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Movement;

namespace DOL.GS;

/// <summary>
/// Read-only Classic/SI knowledge used by bot chat. World locations are indexed
/// once, on the first actual player question; spell questions read the player's
/// own class lines. Nothing runs on an AI tick and no external service is used.
/// </summary>
public static class AutonomousChatKnowledge
{
    private sealed record Place(string Name, string Area, ushort RegionId, bool IsDungeon, bool IsMonster, byte Level);

    private static readonly object IndexSync = new();
    private static IReadOnlyDictionary<string, Place[]> _places;

    public static bool TryAnswer(GamePlayer player, string message, out string answer)
    {
        answer = string.Empty;
        if (player == null || string.IsNullOrWhiteSpace(message))
            return false;
        return TryAnswer(PortsFor(player), message, out answer);
    }

    /// <summary>
    /// The world steps of the knowledge lookup (1.2.2 chat fixes, F0): the server fills them from the live world
    /// (<see cref="PortsFor"/>), the question bench (UT_ChatQuestionBench) from its stub world, so both run the same lookup in the
    /// same order (<see cref="TryAnswer(KnowledgePorts, string, out string)"/>). A step left null answers nothing.
    /// </summary>
    public sealed class KnowledgePorts
    {
        public eRealm Realm { get; init; }
        public int Level { get; init; }
        public string ClassName { get; init; }
        public Random Random { get; init; } = Random.Shared;
        public AutonomousBotVoiceCorpus Corpus { get; init; }
        public LoreBible Bible { get; init; }
        /// <summary>"is keltoi fogou far?": how far, from where the asker stands.</summary>
        public Func<string, string> HowFar { get; init; }
        /// <summary>The world's own facts: a place's levels, who holds Darkness Falls, the relics and keeps (AnswerWorld).</summary>
        public Func<string, string> World { get; init; }
        public Func<string, string> Stable { get; init; }
        public Func<IEnumerable<AutonomousRealmRaid.Summary>> Raids { get; init; }
        public Func<string, string> Spell { get; init; }
        public Func<string, string> Trainer { get; init; }
        public Func<string, string> Location { get; init; }
        /// <summary>"where is Humberton Castle?": a named town of the world (its zone, and the way from the asker).</summary>
        public Func<string, string> Town { get; init; }
        /// <summary>Who holds Darkness Falls now, and whether every realm may enter (AutonomousChatGuide's way in).</summary>
        public eRealm DfOwner { get; init; }
        public bool DfOpenToAll { get; init; }
        /// <summary>The world's NPC factions (AutonomousChatGuide.LiveFactions); null: the guide's own table.</summary>
        public Func<IReadOnlyList<(string Name, string[] Aliases, string[] RaisedBy)>> Factions { get; init; }
        /// <summary>"who sells feathers?" from the world's merchant lists.</summary>
        public Func<string, string> WhoSells { get; init; }
    }

    /// <summary>The live world's knowledge steps for a player's question.</summary>
    public static KnowledgePorts PortsFor(GamePlayer player) => new()
    {
        Realm = player.Realm, Level = player.Level, ClassName = player.CharacterClass?.Name, Random = Random.Shared,
        Corpus = AutonomousBotVoices.Corpus, Bible = LoreBible.Current,
        HowFar = query => TryAnswerHowFar(player, query, out string far) ? far : null,
        World = query => AutonomousChatQuestions.AnswerWorld(query, player.Realm, AutonomousChatLiveAnswers.WorldFor(player.Realm), Random.Shared),
        Stable = query => TryAnswerStable(player.Realm, query, out string stable) ? stable : null,
        Raids = () => AutonomousRealmRaid.Snapshot(),
        Spell = query => TryAnswerSpell(player, query, out string spell) ? spell : null,
        Trainer = query => TryAnswerTrainer(player.Realm, query, player.CharacterClass?.ID ?? 0, player, out string trainer) ? trainer : null,
        Location = query => TryAnswerLocation(player.Realm, query, out string where) ? where : null,
        Town = query => TryAnswerTown(player, query, out string town) ? town : null,
        DfOwner = DOL.GS.ServerRules.DFEnterJumpPoint.DarknessFallOwner, DfOpenToAll = ServerProperties.Properties.ALLOW_ALL_REALMS_DF,
        Factions = AutonomousChatGuide.LiveFactions,
        WhoSells = query => AutonomousChatGuide.TryAnswerWhoSells(player, query, out string seller) ? seller : null,
    };

    /// <summary>
    /// The knowledge lookup, in order (pure but for the ports). 1.2.2 chat fixes (F1): the world's own facts (a place's
    /// levels, Darkness Falls, the relics, the keeps, the realm's dragon) come before the lore, and the lore never answers a
    /// question about levels or about who holds something (the bench: "what lvl is gotar?" drew "vidalf is west among the imps",
    /// 32 of 48 place-level questions a lore line). Lore still answers "whats in krondon?", "who is the storm keeper?".
    /// </summary>
    public static bool TryAnswer(KnowledgePorts ports, string message, out string answer)
    {
        answer = string.Empty;
        if (ports == null || string.IsNullOrWhiteSpace(message))
            return false;
        string query = message.Trim();
        Random random = ports.Random ?? Random.Shared;
        Random outer = _draw;
        _draw = random;
        try
        {
            return TryAnswerInOrder(ports, query, random, out answer);
        }
        finally
        {
            _draw = outer;
        }
    }

    private static bool TryAnswerInOrder(KnowledgePorts ports, string query, Random random, out string answer)
    {
        answer = string.Empty;
        static bool Got(string text, out string result)
        {
            result = text ?? string.Empty;
            return !string.IsNullOrEmpty(text);
        }
        if (TryExplainTerm(query, out answer))
            return true;
        // PC check 2026-11-19 (build 5154cae): "is it far?" about Keltoi Fogou drew the place's description; it is a travel question.
        if (ports.HowFar != null && AsksHowFar(query) && Got(ports.HowFar(query), out answer))
            return true;
        // F1: what the world says before what the lore says.
        if (ports.World != null && Got(ports.World(query), out answer))
            return true;
        if (TryAnswerDragon(ports.Realm, query, out answer))
            return true;
        // 1.2.3 (owner's game: "where is Humberton Castle" drew "stablemaster is your friend"): a town of the world is a fact,
        // before the lore and before the advice lines.
        if (ports.Town != null && LooksLikeLocationQuestion(query) && Got(ports.Town(query), out answer))
            return true;
        bool lore = LoreMayAnswer(query);
        // 1.2 (owner, 2026-11-15): "what is atlantis?", "is there anything under camelot?" are answered as rumours only.
        if (lore && ports.Corpus != null && LoreLegends.TryAnswer(ports.Corpus, query, ports.Realm, random, out answer))
            return true;
        if (ports.Stable != null && LooksLikeStableQuestion(query) && Got(ports.Stable(query), out answer))
            return true;
        if (TryAnswerTicketsInGeneral(query, out answer))
            return true;
        // 1.2.2 chat audit F2 (C8): buying, the way into Darkness Falls, the frontier and the battlegrounds, armour, stats,
        // crafting, factions and tasks, before the lore (a fact before a story).
        if (AutonomousChatGuide.TryAnswer(query, ports.Realm, ports.ClassName, ports.Level, ports.DfOwner, ports.DfOpenToAll, random,
                out answer, ports.Factions?.Invoke()))
            return true;
        // Living Server 1.2: "whats in krondon?", "who is the storm keeper?" from the lore bible (docs/LORE.md).
        if (lore && LoreChat.TryAnswer(ports.Bible, ports.Corpus?.Lines, query, random, out answer, ports.Realm))
            return true;
        if (TryAnswerTimeToCap(query, out answer))
            return true;
        if (AsksAboutRealmRaid(query) &&
            TryAnswerRealmRaid(GlobalConstants.RealmToName(ports.Realm), ports.Level, ports.Raids?.Invoke() ?? [], out answer))
            return true;
        if (ports.Spell != null && LooksLikeSpellQuestion(query) && Got(ports.Spell(query), out answer))
            return true;
        if (ports.Trainer != null && LooksLikeTrainerQuestion(query) && Got(ports.Trainer(query), out answer))
            return true;
        if (TryAnswerHowTo(query, out answer))
            return true;
        if (TryAnswerCommon(query, ports.Realm, ports.ClassName, ports.Level, random, out answer))
            return true;
        if (TryAnswerBuyGear(ports.Realm, query, out answer))
            return true;
        // 1.2.2 chat audit F2: "who sells feathers?" from the world's merchant lists.
        if (ports.WhoSells != null && Got(ports.WhoSells(query), out answer))
            return true;
        if (ports.Location != null && LooksLikeLocationQuestion(query) && Got(ports.Location(query), out answer))
            return true;
        answer = string.Empty;
        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex OwnershipWords = new(
        @"\b(who (has|owns|got|has got|took)|ours|theirs|our relics?|do we (still )?(have|own|hold)|(did|have) we (lose|lost|take|took)|still (have|own|hold))\b");

    /// <summary>
    /// Whether a lore line may answer a question (F1): not one about levels ("what lvl is gotar?", "is vendo ok at 24?", "can a
    /// 50 solo in sheeroe hills?") or about who holds a place, which the lore never says (the fourth-wall rule keeps levels out
    /// of it, and who holds a keep is the world's). Pure.
    /// </summary>
    public static bool LoreMayAnswer(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string t = AutonomousChatQuestions.Normalize(text);
        return !AutonomousChatQuestions.AsksLevels(t) && !OwnershipWords.IsMatch(t) &&
               !System.Text.RegularExpressions.Regex.IsMatch(" " + t + " ", @" (solo|soloable|xp|exp|hunt|level|lvl) ");
    }

    /// <summary>
    /// The common questions of a levelling player, as the bots ask them in /faction too (1.1.3, after RC5): a spec or a weapon
    /// for a class (the class named, else the asker's), spellcrafted gear or drops at a level, how to start crafting, where to
    /// hand in a quest, how realm points work, the epic. Answered the way the field answers are. Pure.
    /// </summary>
    public static bool TryAnswerCommon(string text, eRealm realm, string askerClass, int askerLevel, Random random, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(text) || !(text.Contains('?') || AsksSomething(text)))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        bool Has(string pattern) => System.Text.RegularExpressions.Regex.IsMatch(t, pattern);
        string cls = AutonomousChatFieldTalk.ClassNamed(text) ?? askerClass;
        string[] pool = null;
        // 1.2.2 chat audit (C5, fix F7): a spec or weapon answer only for a class the tips know; the general rule ("whatever ur
        // spec is") answers nothing.
        if (Has(@" (what|which|wat) spec | (put|spend|spending) (my |ur |your )?(spec )?points | points in at | how (should|do) i spec "))
            pool = AutonomousChatFieldTalk.ClassSpecAdvice(cls);
        else if (Has(@" (what|which) (weapon|weap) "))
            pool = AutonomousChatFieldTalk.ClassWeaponAdvice(cls);
        else if (Has(@" sc or drops | drops or sc | (worth|should i) (sc|spellcraft)"))
            pool = AutonomousChatFieldTalk.GearAdvice(AutonomousChatLiveAnswers.LevelAsked(text, askerLevel));
        else if (Has(@" (start|starting|get into|begin|learn) (to )?craft| how (do i|to|can i) craft| crafting (start|work) "))
            pool = [$"go see a crafting trainer in {AutonomousChatFieldTalk.CapitalWord(realm)}, then buy tools from the merchant",
                "crafting trainer first, he sets you up, tools from the merchant", $"the crafting trainers in {AutonomousChatFieldTalk.CapitalWord(realm)}"];
        else if (Has(@" (hand|turn) (in|it in) | where do i (finish|complete|return) ") && Has(@" quest | it "))
            pool = ["back to whoever gave it, your journal says who", "the npc that gave it, check the journal", "back to the quest giver"];
        else if (Has(@" (realm points|rps|rp) work | (get|earn|farm) (rps|realm points|rp) "))
            pool = ["kill enemy players in the frontier, taking keeps gives some too", "enemy kills in the frontier, and taking keeps",
                "rvr kills, more if you group, keeps give a lot"];
        else if (Has(@" epic ") && Has(@" (group|grp|solo|help|alone) "))
            // "do i need a group for my epic?": the epic's last steps are group fights in every realm.
            pool = ["the last steps yeah, get a group for those", "some parts you can solo, the end fights need a group",
                "bring a group for the end of it, the rest is running around"];
        else if (Has(@" epic ") && Has(@" (get|gives|give|reward|rewards|worth) ") && !Has(@" where "))
            // "what do i get from the epic?", "is the epic worth doing?": the epic armour, a full suit for the class.
            pool = ["you get your epic armor at the end, the full suit", "worth it for the armor, a full set for your class",
                "a full set of epic armor, long chain though"];
        else if (Has(@" epic "))
            // Never "did mine": the bot saying it may be level 30 (the bench: "aldwyn did you finish your epic?").
            pool = ["its a long chain, your trainer starts it at 50", "ask your trainer at 50, long chain", "long chain, bring a group for the end of it"];
        if (pool == null || pool.Length == 0)
            return false;
        answer = pool[random.Next(pool.Length)];
        return true;
    }

    private static bool AsksSomething(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(" " + AutonomousChatQuestions.Normalize(text) + " ", @"^ (what|which|where|how|is|should|any1|anyone|wat) ");

    /// <summary>
    /// "is it true theres a dragon in midgard?", "where is the hib dragon?": each realm's dragon and its zone (world data:
    /// Golestandt in Dartmoor, Gjalpinulva in Malmohus, Cuuldurach the Glimmer King in Sheeroe Hills). Play test 2026-10-27:
    /// the question drew three off-topic lines. Pure.
    /// </summary>
    public static bool TryAnswerDragon(eRealm askerRealm, string text, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string lower = " " + new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
        bool asks = text.Contains('?') || lower.Contains(" where ") || lower.Contains(" is there ") || lower.Contains(" theres ");
        if (!asks || !(lower.Contains(" dragon ") || lower.Contains(" dragons ")))
            return false;
        eRealm realm = lower.Contains(" alb") || lower.Contains(" albion ") ? eRealm.Albion
            : lower.Contains(" mid") || lower.Contains(" midgard ") ? eRealm.Midgard
            : lower.Contains(" hib") || lower.Contains(" hibernia ") ? eRealm.Hibernia
            : askerRealm;
        (string name, string zone) = realm switch
        {
            eRealm.Albion => ("Golestandt", "Dartmoor"),
            eRealm.Midgard => ("Gjalpinulva", "Malmohus"),
            eRealm.Hibernia => ("Cuuldurach the Glimmer King", "Sheeroe Hills"),
            _ => (null, null),
        };
        if (name == null)
            return false;
        answer = $"yeah, {name}, up in {zone}. thats a full raid, dont go alone";
        return true;
    }

    /// <summary>
    /// "how long does it take to hit 50?": what players of the era said, a couple of weeks /played. Play test 2026-10-27: the
    /// question drew a lore line about Orasgaen. Pure.
    /// </summary>
    public static bool TryAnswerTimeToCap(string text, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string lower = " " + new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
        bool howLong = lower.Contains(" how long ") || lower.Contains(" how many hours ") || lower.Contains(" how many days ");
        bool cap = lower.Contains(" 50 ") || lower.Contains(" cap ") || lower.Contains(" max level ");
        if (!howLong || !cap)
            return false;
        answer = Draw.Next(3) switch
        {
            0 => "couple weeks /played if you group a lot, way longer solo",
            1 => "took me about 15 days played. groups are faster, the 40s drag",
            _ => "depends, two or three weeks played. 1 to 20 goes quick, then it slows down",
        };
        return true;
    }

    /// <summary>"any hibs doing a dungeon run tonight?", "is there a raid going?", "when is the next dragon raid". Pure.</summary>
    public static bool AsksAboutRealmRaid(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string lower = " " + new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
        bool asks = text.Contains('?') || lower.Contains(" any ") || lower.Contains(" when ") || lower.Contains(" is there ");
        // 1.2.2 chat fixes (F7, the bench's C2): "epic" is a raid word only for an epic fight ("any epic raids?", "is the epic boss
        // up?"), never for the epic quest or its armour ("how long is the epic?", "where do i get my epic armor?").
        bool epicFight = lower.Contains(" epic ") && !EpicQuestTalk(lower) &&
                         System.Text.RegularExpressions.Regex.IsMatch(lower, @" epic (boss|bosses|mob|mobs|encounter|encounters|fight|fights|kill|kills) ");
        bool raid = lower.Contains(" raid ") || lower.Contains(" raids ") || lower.Contains(" dungeon run") || lower.Contains(" expedition ") || epicFight;
        return asks && raid;
    }

    /// <summary>
    /// "my epic", "the epic quest", "epic armor", "epic weapon": the epic quest and what it gives, not a raid (F7). Takes the padded,
    /// lower-case line. Pure.
    /// </summary>
    public static bool EpicQuestTalk(string padded)
    {
        string t = " " + (padded ?? string.Empty).Trim().ToLowerInvariant() + " ";
        return System.Text.RegularExpressions.Regex.IsMatch(t,
            @" (my|your|ur|the|an|our) epic( quest| armor| armour| weapon| chain| steps?)? | epic (quest|quests|armor|armour|weapon|weapons|chain|step|steps|gear|set) ");
    }

    /// <summary>
    /// What the realm's raids are doing, from the live raid state. Play test 2026-10-27: "any hibs doing a dungeon run tonight?"
    /// drew "no clue" while 116 Hibernians were fighting in Galladoria. Pure.
    /// </summary>
    public static bool TryAnswerRealmRaid(string realmName, int askerLevel, IEnumerable<AutonomousRealmRaid.Summary> raids, out string answer)
    {
        answer = string.Empty;
        var mine = (raids ?? []).Where(raid => string.Equals(raid.Realm, realmName, StringComparison.OrdinalIgnoreCase)).ToArray();
        string tail = askerLevel < 45 ? ", its a 50 thing though" : ", come along";
        var battle = mine.FirstOrDefault(raid => raid.Phase == "Battle");
        if (battle != null)
        {
            answer = battle.Present > 0 ? $"yeah, the realm is in {battle.Name} right now, {battle.Present} of us{tail}"
                : $"yeah, the {battle.Name} raid is underway{tail}";
            return true;
        }
        var forming = mine.FirstOrDefault(raid => raid.Phase is "Muster" or "Staging" or "Waiting");
        if (forming != null)
        {
            answer = forming.Assigned > 0 ? $"{forming.Name} raid is gathering, {forming.Assigned} signed up so far{tail}"
                : $"{forming.Name} raid is gathering{tail}";
            return true;
        }
        answer = "nothing running right now, the next one gets called when enough 50s are on";
        return true;
    }

    public static bool TryAnswer(eRealm realm, string message, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string query = message.Trim();
        // A bot's own line is answered only when it really asks something (in game "ranger, recurve bow, pathfinding"
        // drew a location answer about an NPC named Ranger; audit 2026-10-18).
        if (AutonomousBotVoices.Enabled && !query.Contains('?'))
            return false;
        if (LooksLikeStableQuestion(query) && TryAnswerStable(realm, query, out answer))
            return true;
        if (LooksLikeTrainerQuestion(query) && TryAnswerTrainer(realm, query, 0, null, out answer))
            return true;
        return LooksLikeLocationQuestion(query) && TryAnswerLocation(realm, query, out answer);
    }

    #region How do I (Beta 1.1.2)

    /// <summary>
    /// "how do i equip this", "how do i bind", "how many rp for rr2", "how do i get to hib from alb", "how do i go anon":
    /// questions about how the game works, answered as a player would (blind round 1: "i cant figure out how to equip this"
    /// drew nothing, "how many rp for rr2?" drew "nice, gz"). The realm-rank numbers are the server's own table. Pure.
    /// </summary>
    public static bool TryAnswerHowTo(string text, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        bool how = System.Text.RegularExpressions.Regex.IsMatch(t, @" (how (do|can|to|would) (i|you|u|one|ya)|how to|cant figure out how|cant figure how|whats the command|what s the command|is there a command|how does one) ");
        var rank = System.Text.RegularExpressions.Regex.Match(t, @" how (many|much) (rp|rps|realm points)( do i need| do you need| for| to get)?( to)?( get)? (to )?(rr|realm rank )(?<rank>\d{1,2}) ");
        if (rank.Success && int.TryParse(rank.Groups["rank"].Value, out int rr) && rr is >= 2 and <= 13)
        {
            long[] table = GamePlayer.REALMPOINTS_FOR_LEVEL;
            int index = (rr - 1) * 10;
            if (index < table.Length)
            {
                answer = Pick($"{table[index]:N0} for rr{rr}", $"{table[index]:N0} rps for rr{rr}", $"rr{rr} is {table[index]:N0} rps");
                return true;
            }
        }
        if (!how)
            return false;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (equip|wear|put on|wield|use this (sword|weapon|shield|armor|staff|bow)) "))
            answer = Pick("open your inventory and drag it onto your character", "drag it from your bag onto the paperdoll", "inventory, then drag it onto yourself");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (bind|set my bind|bind point|bind stone|bindstone) "))
            answer = Pick("stand by the bindstone in town and type /bind", "/bind next to a bindstone", "go to the bindstone and /bind");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (unstuck|get unstuck|stuck) "))
            answer = Pick("/stuck", "type /stuck", "/stuck, takes a few secs");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (go anon|be anon|anonymous|hide from who) "))
            answer = Pick("/anon", "type /anon", "/anon toggles it");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (release|go to my bind|respawn) "))
            answer = Pick("/release takes you to your bind", "type /release", "/release, but you lose more xp than a rez");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (train|spend my (spec|points)|spec points|level my skills|raise my skills) "))
            answer = Pick("talk to your trainer, he spends the points", "your class trainer, right click him", "visit your trainer after every level");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (get|go) to (hib|hibernia|mid|midgard|alb|albion) ") &&
                 System.Text.RegularExpressions.Regex.IsMatch(t, @" (from|frm) (hib|hibernia|mid|midgard|alb|albion) "))
            answer = Pick("you cant, other realms are the enemy. you only meet them in the frontier", "cant cross over, only in rvr lol",
                "you cant, they kill you on sight. the frontier is where you meet them");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (talk to|speak to) (my )?(guild|group|someone|people) "))
            answer = Pick("/gu for guild, /g for group, /send name for a tell", "/send name msg for a tell, /gu for guild");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (see|check) (my )?(xp|experience|level progress|bubbles) "))
            answer = Pick("the xp bar at the bottom, each bubble is a tenth", "watch the bubbles on the xp bar");
        else if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (group|invite someone|inv someone|start a group) "))
            answer = Pick("/invite name", "target them and /invite", "/invite their name");
        return answer.Length > 0;
    }

    #endregion

    #region Chat words (0.36-se.4)

    // What period chat words mean. Live test 2026-10-18: "what does lfg mean?" drew "what level range?".
    private static readonly Dictionary<string, string> Terms = new(StringComparer.OrdinalIgnoreCase)
    {
        ["lfg"] = "looking for group", ["lfm"] = "looking for more, a group wants people", ["lf1m"] = "looking for one more",
        ["gz"] = "congrats", ["grats"] = "congrats", ["inc"] = "incoming, enemies or adds coming", ["oom"] = "out of mana",   // promise: none (what "inc" means)
        ["oop"] = "out of power", ["brb"] = "be right back", ["afk"] = "away from keyboard", ["bio"] = "bathroom break lol",
        ["ks"] = "kill steal, taking someone elses mob", ["pbae"] = "point blank area effect, the caster blows up everything around him",
        ["pbaoe"] = "point blank area effect, the caster blows up everything around him", ["rp"] = "realm points, you get them in rvr",
        ["rps"] = "realm points, you get them in rvr", ["rr"] = "realm rank, from realm points", ["rvr"] = "realm vs realm, the frontier",
        ["wts"] = "want to sell", ["wtb"] = "want to buy", ["wtt"] = "want to trade", ["pst"] = "please send tell",
        ["pc"] = "price check", ["con"] = "how tough a mob looks, by its color", ["add"] = "another mob joining the fight",
        ["adds"] = "more mobs joining the fight", ["ding"] = "level up", ["rez"] = "resurrect", ["aggro"] = "a mob that attacks on sight",
        ["df"] = "darkness falls, the big dungeon", ["ml"] = "master level, from the atlantis rumours. nobody here has one", ["ra"] = "realm ability", ["np"] = "no problem",   // chat-check: allow ml, master-level, atlantis (the glossary's rumour answer, WRITING-GUIDE's one exception)
        ["ty"] = "thank you", ["lol"] = "laughing out loud", ["imo"] = "in my opinion", ["mez"] = "mesmerize, it holds a mob still",
        ["stun"] = "they cant move or act for a bit", ["zerg"] = "a huge mob of players running together", ["gank"] = "killing someone who cant fight back",
    };

    /// <summary>"what does lfg mean?", "whats pbae", "what is a con": a short meaning.</summary>
    public static bool TryExplainTerm(string text, out string answer)
    {
        answer = string.Empty;
        string t = (text ?? string.Empty).Trim().TrimEnd('?', '!', '.').ToLowerInvariant();
        var m = System.Text.RegularExpressions.Regex.Match(t,
            @"^(?:so |um |hey |sorry |noob question,? |dumb question,? )?(?:what does|what do|wat does|what is|whats|what's|wat is|wats) (?:an? )?([a-z0-9]+)(?: mean| stand for| means)?$");
        if (!m.Success || !Terms.TryGetValue(m.Groups[1].Value, out string meaning))
            return false;
        string word = m.Groups[1].Value;
        answer = Pick($"{word} = {meaning}", $"{meaning}", $"it means {meaning}", $"{meaning} :)");
        return true;
    }

    #endregion

    #region Trainers (0.36-se.4)

    /// <summary>"where is the scout trainer", "where do i train", "who trains clerics" (playtest 2026-10-18: a new Scout
    /// spent 25 minutes finding Lt. Rydderac, and a bot by /send named the wrong zone).</summary>
    public static bool LooksLikeTrainerQuestion(string text) =>
        HasWord(text, "trainer") || HasWord(text, "trainers") || HasPhrase(text, "where do i train") ||
        HasPhrase(text, "where to train") || HasPhrase(text, "who trains") || HasPhrase(text, "where can i train") ||
        HasPhrase(text, "where do you train") || AsksWhereToTrain(text);

    /// <summary>
    /// "where do i get my spells trained?", "where do i learn new spells", "how do i get trained": a question about training
    /// with no "trainer" in it (owner's PC, RC2 2026-11-08: the player's question drew "no idea"). Only a question: "just
    /// trained, heading out" is not one. Pure.
    /// </summary>
    public static bool AsksWhereToTrain(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        bool asks = text.Contains('?') || System.Text.RegularExpressions.Regex.IsMatch(t, @"^ (where|wheres|where s|how|who|any1|anyone) ");
        return asks && System.Text.RegularExpressions.Regex.IsMatch(t,
            @" (trained|get trained|train my|train up|my spells|new spells|learn spells|learn new|spells trained|skills trained|spend my points|my spec points) ");
    }

    /// <summary>
    /// The answer to "where's my trainer": the nearest trainer of the class from the world data ("go see Lady Fridwulf in
    /// Camelot City"), else the capital, where every class of the realm trains ("your paladin trainer in camelot"). Null for
    /// no class. 1.1.3, after RC5: bots asking where their trainer is in /faction get this too.
    /// </summary>
    public static string TrainerAnswer(eRealm realm, int classId, GameObject near, Random random)
    {
        if (classId == 0)
            return null;
        string className = null;
        try
        {
            className = AllClasses().FirstOrDefault(c => c?.ID == classId)?.Name;
            var trainers = TrainersOf(realm, classId);
            if (trainers.Count > 0)
                return TrainerText(trainers, className, near, random);
        }
        catch
        {
            // No world loaded (tests, a minimal world): the capital, which is true for every class.
        }
        return GenericTrainerAnswer(realm, className ?? ((eCharacterClass)classId).ToString(), random);
    }

    /// <summary>
    /// "your paladin trainer is in camelot": every class trainer of a realm stands in its capital. 1.2.2 chat audit (C7, fix F5):
    /// "your" only for the asker's own class (<paramref name="own"/>; "who trains minstrels?" from a sorcerer drew "your minstrel
    /// trainer"), and no "he trains ur spells" (said to an armsman, who has none): the trainer is where the points are spent, true
    /// of every class. Pure.
    /// </summary>
    public static string GenericTrainerAnswer(eRealm realm, string className, Random random, bool own = true)
    {
        // The capital as its name is written ("Camelot", "Tir na Nog"): a voice that capitalizes the line keeps it whole (C11).
        string cap = AutonomousBotVoices.CapitalOf(realm) ?? AutonomousChatFieldTalk.CapitalWord(realm);
        string cls = string.IsNullOrWhiteSpace(className) ? "class" : className.Trim().ToLowerInvariant();
        if (own)
            return random.Next(3) switch
            {
                0 => $"your {cls} trainer is in {cap}",
                1 => $"{cap}, ur {cls} trainer is there",
                _ => $"ur {cls} trainer in {cap}, spend ur points there",
            };
        return random.Next(3) switch
        {
            0 => $"the {cls} trainer is in {cap}",
            1 => $"{cap}, the {cls} trainer is there",
            _ => $"in {cap}, with the other class trainers",
        };
    }

    /// <summary>"where is the blademaster trainer?" in Albion: a class of another realm, said as such (C7, fix F5). Pure.</summary>
    public static string OtherRealmClassAnswer(string className, eRealm home, Random random)
    {
        string cls = string.IsNullOrWhiteSpace(className) ? "that" : className.Trim().ToLowerInvariant();
        string realm = AutonomousChatFieldTalk.RealmWord(GlobalConstants.RealmToName(home), modifier: true);
        return random.Next(3) switch
        {
            0 => $"{cls} is a {realm} class, no trainer for it on our side",
            1 => $"thats a {realm} class, cant train it here",
            _ => $"{cls}? {realm} only, we dont have those",
        };
    }

    /// <summary>
    /// The trainer answer without the world's trainers (C7, fix F5): the class named (<paramref name="named"/>, else the asker's),
    /// another realm's class said as such, else its capital, "your" only when it is the asker's own class. Null with no class. Pure.
    /// </summary>
    public static string TrainerAnswerFor(eRealm realm, string named, string askerClass, Random random)
    {
        string cls = string.IsNullOrWhiteSpace(named) ? askerClass : named;
        if (string.IsNullOrWhiteSpace(cls))
            return null;
        eRealm home = AutonomousChatGear.RealmOf(cls);
        if (home is (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) && realm is (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) && home != realm)
            return OtherRealmClassAnswer(cls, home, random);
        bool own = string.IsNullOrWhiteSpace(named) || string.Equals(named, askerClass, StringComparison.OrdinalIgnoreCase);
        return GenericTrainerAnswer(realm, cls, random, own);
    }

    private static bool TryAnswerTrainer(eRealm realm, string query, int askerClassId, GamePlayer asker, out string answer)
    {
        answer = string.Empty;
        // The class named in the question, else the asker's own class.
        int classId = 0;
        foreach (ICharacterClass cls in AllClasses())
        {
            if (cls != null && !string.IsNullOrEmpty(cls.Name) && (HasWord(query, cls.Name) || HasWord(query, cls.Name + "s")))
            {
                classId = cls.ID;
                break;
            }
        }
        // "who trains mercenaries?", "where do bms train": the chat forms of a class (C7).
        if (classId == 0 && AutonomousChatFieldTalk.ClassNamed(query) is { } chatName)
            classId = AllClasses().FirstOrDefault(c => string.Equals(c?.Name, chatName, StringComparison.OrdinalIgnoreCase))?.ID ?? 0;
        bool own = classId == 0 || classId == askerClassId;
        if (classId == 0)
            classId = askerClassId;
        if (classId == 0)
            return false;
        string name = AllClasses().FirstOrDefault(c => c?.ID == classId)?.Name;
        // A class of another realm has no trainer the asker can reach: say so (C7: "where is the blademaster trainer?" in Albion
        // drew "no clue, sry").
        eRealm home = AutonomousChatGear.RealmOf(name);
        if (!own && home is (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) && home != realm)
        {
            answer = OtherRealmClassAnswer(name, home, Random.Shared);
            return true;
        }
        var trainers = TrainersOf(realm, classId);
        // 1.1.3: with no trainer in the world data, the capital, where every class of the realm trains (not "no idea").
        answer = trainers.Count == 0 ? GenericTrainerAnswer(realm, name, Random.Shared, own) : TrainerText(trainers, name, asker, Random.Shared);
        return true;
    }

    private static string TrainerText(List<(string Name, string Zone, ushort Region, bool Capital, int X, int Y)> trainers, string name, GameObject asker,
        Random random)
    {
        // The nearest one in the asker's own region, then the capital.
        var best = trainers.OrderByDescending(t => asker != null && t.Region == asker.CurrentRegionID)
            .ThenBy(t => asker != null && t.Region == asker.CurrentRegionID ? Math.Pow(t.X - asker.X, 2) + Math.Pow(t.Y - asker.Y, 2) : 0)
            .ThenByDescending(t => t.Capital).First();
        string className = name?.ToLowerInvariant() ?? "your class";
        var other = trainers.FirstOrDefault(t => t.Zone != best.Zone);
        // Worded to read right when a voice drops the commas (live test 2026-10-18: "Lieutenant Mhoudi in Campacorentin Forest they
        // train scouts").
        return other.Name != null && random.Next(3) == 0
            ? $"{best.Name} in {best.Zone} trains {className}s, theres one in {other.Zone} too"
            : random.Next(3) switch
            {
                0 => $"{className} trainer is {best.Name} in {best.Zone}",
                1 => $"{best.Name} trains {className}s in {best.Zone}",
                _ => $"go see {best.Name} in {best.Zone}",
            };
    }

    private static readonly object TrainerSync = new();
    private static Dictionary<(eRealm Realm, int ClassId), List<(string Name, string Zone, ushort Region, bool Capital, int X, int Y)>> _trainers;

    /// <summary>The trainers of a class a realm can reach, indexed once on the first trainer question.</summary>
    private static List<(string Name, string Zone, ushort Region, bool Capital, int X, int Y)> TrainersOf(eRealm realm, int classId)
    {
        if (_trainers == null)
        {
            lock (TrainerSync)
            {
                if (_trainers == null)
                {
                    var index = new Dictionary<(eRealm, int), List<(string, string, ushort, bool, int, int)>>();
                    foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null &&
                                 AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion)))
                    {
                        foreach (GameTrainer trainer in region.Objects.OfType<GameTrainer>())
                        {
                            if (trainer?.ObjectState is not GameObject.eObjectState.Active || trainer.TrainedClass == eCharacterClass.Unknown)
                                continue;
                            foreach (eRealm reach in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                            {
                                if (!RegionIsAccessible(reach, region.ID) || trainer.Realm != reach && trainer.Realm != eRealm.None)
                                    continue;
                                var key = (reach, (int)trainer.TrainedClass);
                                if (!index.TryGetValue(key, out var list))
                                    index[key] = list = new();
                                list.Add((trainer.Name, trainer.CurrentZone?.Description ?? region.Description, region.ID,
                                    AutonomousBotVoices.IsCapitalRegion(region.ID), trainer.X, trainer.Y));
                            }
                        }
                    }
                    _trainers = index;
                }
            }
        }
        return _trainers.TryGetValue((realm, classId), out var found) ? found : new();
    }

    private static ICharacterClass[] _classes;

    private static ICharacterClass[] AllClasses() => _classes ??= LoadClasses().ToArray();

    private static IEnumerable<ICharacterClass> LoadClasses()
    {
        foreach (eCharacterClass value in Enum.GetValues<eCharacterClass>())
        {
            ICharacterClass cls = null;
            try
            {
                cls = ScriptMgr.FindCharacterClass((int)value);
            }
            catch
            {
                // a class the server doesn't script
            }
            if (cls != null)
                yield return cls;
        }
    }

    #endregion

    /// <summary>Every horse route of the realm's stable masters: who sells it, where they stand, the ticket's destination and the zone it ends in.</summary>
    private static List<(string Master, string Area, string Destination, string EndpointArea)> StableRoutes(eRealm realm)
    {
        var routes = new List<(string Master, string Area, string Destination, string EndpointArea)>();
        foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null &&
                     AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion) &&
                     RegionIsAccessible(realm, region.ID)))
        {
            foreach (GameStableMaster master in region.Objects.OfType<GameStableMaster>()
                         .Where(master => master.ObjectState is GameObject.eObjectState.Active &&
                                          (master.Realm == realm || master.Realm == eRealm.None) && master.TradeItems != null))
            {
                foreach (DictionaryEntry entry in master.TradeItems.GetAllItems())
                {
                    if (entry.Value is not DbItemTemplate ticket || ticket.Item_Type != 40)
                        continue;
                    string destination = TicketDestination(ticket.Name);
                    PathPoint path = MovementMgr.LoadPath(ticket.Id_nb);
                    PathPoint endpoint = path;
                    int guard = 0;
                    while (endpoint?.Next != null && guard++ < 5000)
                        endpoint = endpoint.Next;
                    string endpointArea = endpoint == null ? string.Empty : region.GetZone(endpoint.X, endpoint.Y)?.Description ?? string.Empty;
                    routes.Add((master.Name, master.CurrentZone?.Description ?? region.Description, destination, endpointArea));
                }
            }
        }
        return routes;
    }

    private static bool TryAnswerStable(eRealm realm, string query, out string answer)
    {
        answer = string.Empty;
        var routes = StableRoutes(realm)
            .Where(route => NamesPlace(query, route.Destination) || NamesPlace(query, route.EndpointArea))
            .Select(route => (route.Master, route.Area, Destination: string.IsNullOrWhiteSpace(route.EndpointArea) ? route.Destination : route.EndpointArea))
            .ToList();

        (string Master, string Area, string Destination)[] matches = routes.Distinct().Take(3).ToArray();
        if (matches.Length == 0)
            return false;
        string masters = string.Join(" or ", matches.Take(2).Select(route => $"{route.Master} in {route.Area}"));
        answer = Pick($"for {matches[0].Destination} get a ticket from {masters}", $"{masters} sells tickets to {matches[0].Destination}",
            $"horse to {matches[0].Destination}: {masters}");
        return true;
    }

    private static bool TryAnswerSpell(GamePlayer player, string query, out string answer)
    {
        answer = string.Empty;
        var candidates = new List<(Spell Spell, SpellLine Line)>();
        foreach (Specialization specialization in player.GetSpecList())
        {
            foreach (SpellLine line in specialization.PretendSpellLinesForLiving(player, 50))
            {
                foreach (Spell spell in SkillBase.GetSpellList(line.KeyName))
                {
                    if (spell?.Level is < 1 or > 50 || string.IsNullOrWhiteSpace(spell.Name) ||
                        !ContainsPhrase(query, spell.Name))
                        continue;
                    candidates.Add((spell, line));
                }
            }
        }

        (Spell Spell, SpellLine Line) match = candidates
            .DistinctBy(entry => (entry.Spell.Name.ToLowerInvariant(), entry.Spell.Level, entry.Line.KeyName))
            .OrderByDescending(entry => entry.Spell.Name.Length)
            .ThenBy(entry => entry.Spell.Level)
            .FirstOrDefault();
        if (match.Spell == null)
            return false;

        answer = match.Line.IsBaseLine
            ? Pick($"{match.Spell.Name} comes at level {match.Spell.Level}", $"you get {match.Spell.Name} at {match.Spell.Level}, from {match.Line.Name}")
            : Pick($"{match.Spell.Name} needs {match.Spell.Level} in {match.Line.Name}", $"{match.Spell.Level} {match.Line.Name} for {match.Spell.Name}");
        return true;
    }

    /// <summary>
    /// Whether naming a place's area tells the asker nothing: the place is the area, or the area is the whole realm (RC10 on the
    /// owner's PC: "how do i get to pennine mountains from here" was answered "pennine mountains? thats in albion"). Pure.
    /// </summary>
    public static bool AreaSaysNothing(string name, string area) =>
        string.IsNullOrWhiteSpace(area) || area.Equals(name, StringComparison.OrdinalIgnoreCase) ||
        System.Text.RegularExpressions.Regex.IsMatch(area.Trim(), @"^(albion|midgard|hibernia)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool TryAnswerLocation(eRealm realm, string query, out string answer)
    {
        answer = string.Empty;
        IReadOnlyDictionary<string, Place[]> index = GetPlaces();
        // PC check 2026-11-19 (build c81eb5d): "how do i get to tepoks mine?" (the catalog alias, from the bot's own answer) missed
        // the zone "Tepok's Mine". Both sides are compared normalized (NamedPlaceKey).
        if (NamedPlaceKey(query, index.Keys, AutonomousChatPlaces.All) is not { } key)
            return false;
        KeyValuePair<string, Place[]> match = new(key, index[key]);
        // PC check 2026-11-19 (build 5154cae): an Albion bot answered "how do i get to breifine" (Hibernia's frontier) with "new
        // frontiers, a horse gets u most of the way", from a leftover zone of that name in region 163. A zone of another realm's
        // land is told as that: its frontier is RvR, its home zones can't be reached from here.
        if (OtherRealmZone(realm, match.Key, match.Value.Select(place => (place.Name, place.RegionId, place.IsMonster))) is { } owner)
        {
            answer = OtherRealmLine(match.Value[0].Name, owner, AutonomousChatLiveAnswers.FrontierHome(match.Value[0].Name) == owner, Random.Shared);
            return true;
        }

        Place[] reachable = match.Value.Where(place => RegionIsAccessible(realm, place.RegionId)).ToArray();
        if (reachable.Length == 0)
            return false;
        Place place = reachable
            .GroupBy(entry => (entry.Area, entry.RegionId, entry.IsDungeon, entry.IsMonster))
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
            .First();

        if (place.IsMonster)
        {
            answer = place.IsDungeon
                ? Pick($"{place.Name}? inside {place.Area}, bring a group", $"{place.Name} is in {place.Area}, deep in", $"inside {place.Area}, careful going in")
                : Pick($"{place.Name}? around {place.Area}", $"{place.Name} are out in {place.Area}", $"try {place.Area}, seen them there");
            return true;
        }

        if (place.IsDungeon)
        {
            DbZonePoint entrance = DOLDB<DbZonePoint>.SelectObjects(DB.Column("TargetRegion").IsEqualTo(place.RegionId))
                .Where(point => point.SourceRegion != 0 && (point.Realm == 0 || point.Realm == (ushort)realm))
                .FirstOrDefault(point => RegionIsAccessible(realm, point.SourceRegion));
            if (entrance != null)
            {
                Region source = WorldMgr.GetRegion(entrance.SourceRegion);
                string sourceArea = source?.GetZone(entrance.SourceX, entrance.SourceY)?.Description ?? source?.Description;
                if (!string.IsNullOrWhiteSpace(sourceArea))
                {
                    answer = Pick($"{place.Name} entrance is in {sourceArea}", $"{place.Name}? the way in is in {sourceArea}",
                        $"head to {sourceArea}, {place.Name} entrance is there");
                    return true;
                }
            }
            answer = Pick($"{place.Name} is on our side, look for the entrance", $"{place.Name}? its a dungeon on our side, bring a group");
            return true;
        }

        if (AutonomousBotVoices.IsCapitalRegion(place.RegionId) && place.Area.Equals(place.Name, StringComparison.OrdinalIgnoreCase))
        {
            answer = Pick($"{place.Name}? any stable master runs a horse there", $"take a horse to {place.Name}, the roads all lead there",
                $"{place.Name}, the capital, a stable master gets you there");
            return true;
        }

        answer = AreaSaysNothing(place.Name, place.Area)
            ? Pick($"{place.Name} is its own zone, a stable master can get you close", $"{place.Name}? take a horse from a stable master",
                $"{place.Name}, a horse gets you most of the way")
            : Pick($"{place.Name} is in {place.Area}", $"{place.Name}? thats in {place.Area}", $"in {place.Area}, follow the road",
                $"{place.Area}, a horse gets you most of the way");
        return true;
    }

    /// <summary>The realm whose home region this is (the capitals, home zones and dungeons, and the realm's classic frontier), or None. Pure.</summary>
    public static eRealm HomeRealmOfRegion(ushort regionId) =>
        regionId is 1 or 10 or 20 or 21 or 22 or 23 or 24 or 50 or 51 or 60 or 61 or 62 ? eRealm.Albion
        : regionId is 100 or 101 or 125 or 126 or 127 or 128 or 129 or 150 or 151 or 160 or 161 ? eRealm.Midgard
        : regionId is 180 or 181 or 190 or 191 or 192 or 193 or 194 or 200 or 201 or 220 or 221 or 222 or 223 or 224 ? eRealm.Hibernia
        : eRealm.None;

    /// <summary>
    /// The other realm a zone or region name belongs to, for an asker of <paramref name="asker"/>'s realm, or null: the name is
    /// a zone (not a monster or NPC) of another realm's land and of no land of the asker's own. A same-named zone in a neutral
    /// region (region 163's leftover "Breifine") doesn't make it the asker's. Pure.
    /// </summary>
    public static eRealm? OtherRealmZone(eRealm asker, string name, IEnumerable<(string Name, ushort RegionId, bool IsMonster)> places)
    {
        var zones = (places ?? []).Where(place => !place.IsMonster && string.Equals(place.Name?.Trim(), name?.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray();
        if (zones.Length == 0 || zones.Any(place => HomeRealmOfRegion(place.RegionId) == asker))
            return null;
        eRealm owner = zones.Select(place => HomeRealmOfRegion(place.RegionId)).FirstOrDefault(realm => realm != eRealm.None);
        return owner == eRealm.None || owner == asker ? null : owner;
    }

    /// <summary>
    /// The honest answer about another realm's zone (2003 chat): its frontier is RvR land, its home zones can't be reached from
    /// here. Pure but for the draw.
    /// </summary>
    public static string OtherRealmLine(string name, eRealm owner, bool frontier, Random random)
    {
        random ??= Random.Shared;
        string place = (name ?? "that").Trim().ToLowerInvariant();
        string side = owner switch { eRealm.Albion => "alb", eRealm.Midgard => "mid", eRealm.Hibernia => "hib", _ => "enemy" };
        string realmName = owner switch { eRealm.Albion => "albion", eRealm.Midgard => "midgard", eRealm.Hibernia => "hibernia", _ => "their side" };
        string[] lines = frontier
            ? [$"{place}? thats {side} frontier, rvr only. bring a grp", $"{place} is {side} frontier, go with an rvr grp", $"thats {side} frontier lol, not a place to go alone"]
            : [$"{place}? thats {side} land, cant get there from here", $"{place} is in {realmName}, we cant go there", $"thats {side} side, no way over from here"];
        return lines[random.Next(lines.Length)];
    }

    /// <summary>
    /// "is keltoi fogou far?", "how far is lyonesse", "how long to get to camelot hills" (and "is it far?" completed from the
    /// conversation, ChatConversations.Resolve): a question about the distance or the trip, not about the place. Pure.
    /// </summary>
    public static bool AsksHowFar(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        return System.Text.RegularExpressions.Regex.IsMatch(t,
            @" (is (it|that|\w+( \w+){0,3}) (far|close|a long way|far from here|close to here)|how far|how long (to get|to run|does it take to get|does it take to run|is the run|of a run)|far from (here|camelot|jordheim|tir na nog)|a long run) ");
    }

    /// <summary>How far a trip is, in four steps (world units; a zone is 65,536 across, a minute's run about 11,000). Pure.</summary>
    public enum Far { Here, Close, NotFar, Trip, LongWay, Unknown }

    public static Far FarOf(bool alreadyThere, double? distance) =>
        alreadyThere ? Far.Here
        : distance is not { } d ? Far.Unknown
        : d < 15_000 ? Far.Close
        : d < 45_000 ? Far.NotFar
        : d < 100_000 ? Far.Trip
        : Far.LongWay;

    /// <summary>
    /// The answer to "is it far?" (PC check 2026-11-19, build 5154cae: "is it far?" about Keltoi Fogou drew the place's
    /// description): roughly how far, and how to get there: the horse when a stable route really ends in that zone, else the
    /// run; a dungeon's entrance zone. 2003 typing, true for the place. Pure but for the draw.
    /// </summary>
    public static string HowFarLine(string name, string area, Far far, bool horse, bool dungeon, Random random)
    {
        random ??= Random.Shared;
        string n = (name ?? "it").Trim();
        string a = string.IsNullOrWhiteSpace(area) ? n : area.Trim();
        string there = dungeon ? $", {n} entrance is there" : "";
        string[] lines = far switch
        {
            Far.Here => dungeon ? [$"close, the {n} entrance is right here in {a}", $"not far at all, ur in {a} already, entrance is here"]
                : [$"ur in {a} already lol", $"not far, ur standing in it"],
            Far.Close => dungeon ? [$"not far at all, entrance is in {a}", $"close, {n} entrance is right in {a}"]
                : [$"not far, {a} is right there", $"close, just head into {a}"],
            Far.NotFar => horse ? [$"not far, take the horse to {a}{there}", $"not too far, horse to {a} then a short run"]
                : [$"not too far, run to {a}{there}", $"few min run, its in {a}"],
            Far.Trip => horse ? [$"bit of a trip, horse to {a} then run{there}", $"kinda far, take the horse to {a}"]
                : [$"bit of a run, its out in {a}{there}", $"kinda far, follow the road to {a}"],
            Far.LongWay => horse ? [$"long way, horse to {a} and then run{there}", $"far, get a horse to {a}"]
                : [$"long way, its way out in {a}{there}", $"far lol, its all the way out in {a}"],
            _ => horse ? [$"take the horse to {a}{there}", $"horse to {a}, not bad from there"]
                : [$"its in {a}, follow the road{there}", $"{a}, its a run but not bad"],
        };
        return lines[random.Next(lines.Length)];
    }

    /// <summary>
    /// The live answer to a how-far question: where the place is (a dungeon by its entrance), how far that is from the asker
    /// (in the same region, or from where the asker's region opens into it), and whether a horse route of the asker's realm ends
    /// in that zone. Another realm's zone is told honestly (<see cref="OtherRealmLine"/>).
    /// </summary>
    private static bool TryAnswerHowFar(GamePlayer player, string query, out string answer)
    {
        answer = string.Empty;
        if (player == null || !AsksHowFar(query))
            return false;
        eRealm realm = player.Realm;
        IReadOnlyDictionary<string, Place[]> index = GetPlaces();
        // PC check 2026-11-19 (build c81eb5d): "is tepoks mine far?" missed the zone "Tepok's Mine" (see NamedPlaceKey).
        if (NamedPlaceKey(query, index.Keys, AutonomousChatPlaces.All) is not { } key)
            return false;
        KeyValuePair<string, Place[]> match = new(key, index[key]);
        if (OtherRealmZone(realm, match.Key, match.Value.Select(place => (place.Name, place.RegionId, place.IsMonster))) is { } owner)
        {
            answer = OtherRealmLine(match.Value[0].Name, owner, AutonomousChatLiveAnswers.FrontierHome(match.Value[0].Name) == owner, Random.Shared);
            return true;
        }
        Place place = match.Value.Where(entry => !entry.IsMonster && RegionIsAccessible(realm, entry.RegionId))
            .GroupBy(entry => (entry.Area, entry.RegionId, entry.IsDungeon))
            .OrderByDescending(group => group.Count())
            .Select(group => group.First())
            .FirstOrDefault();
        if (place == null)
            return false;
        ushort regionId;
        double x, y;
        string area;
        if (place.IsDungeon)
        {
            DbZonePoint entrance = DOLDB<DbZonePoint>.SelectObjects(DB.Column("TargetRegion").IsEqualTo(place.RegionId))
                .Where(point => point.SourceRegion != 0 && point.SourceRegion != place.RegionId && (point.Realm == 0 || point.Realm == (ushort)realm))
                .FirstOrDefault(point => RegionIsAccessible(realm, point.SourceRegion));
            Region source = entrance == null ? null : WorldMgr.GetRegion(entrance.SourceRegion);
            area = source?.GetZone(entrance.SourceX, entrance.SourceY)?.Description;
            if (entrance == null || string.IsNullOrWhiteSpace(area))
                return false;
            (regionId, x, y) = (entrance.SourceRegion, entrance.SourceX, entrance.SourceY);
        }
        else
        {
            Region region = WorldMgr.GetRegion(place.RegionId);
            Zone zone = region?.Zones.FirstOrDefault(z => z != null && string.Equals(z.Description, place.Name, StringComparison.OrdinalIgnoreCase));
            if (zone == null)
                return false;
            (regionId, x, y, area) = (place.RegionId, zone.XOffset + zone.Width / 2.0, zone.YOffset + zone.Height / 2.0, zone.Description);
        }
        bool alreadyThere = string.Equals(player.CurrentZone?.Description, area, StringComparison.OrdinalIgnoreCase);
        double? distance = null;
        if (player.CurrentRegionID == regionId)
            distance = Math.Sqrt(Math.Pow(player.X - x, 2) + Math.Pow(player.Y - y, 2));
        else if (DOLDB<DbZonePoint>.SelectObjects(DB.Column("SourceRegion").IsEqualTo(player.CurrentRegionID))
                     .FirstOrDefault(point => point.TargetRegion == regionId) is { } exit)
            // From a capital or a dungeon: from where its door opens into that region.
            distance = Math.Sqrt(Math.Pow(exit.TargetX - x, 2) + Math.Pow(exit.TargetY - y, 2));
        bool horse = StableRoutes(realm).Any(route => string.Equals(route.EndpointArea, area, StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(route.Destination, area, StringComparison.OrdinalIgnoreCase));
        answer = HowFarLine(place.Name, area, FarOf(alreadyThere, distance), horse, place.IsDungeon, Random.Shared);
        return true;
    }

    /// <summary>A named town of the world (a circle area: Humberton Castle, Campacorentin Station, Mularn), where it stands.</summary>
    public sealed record NamedTown(string Name, string Zone, ushort RegionId, int X, int Y);

    private static NamedTown[] _towns;

    /// <summary>
    /// Whether an area record names a town a player would ask the way to: a named circle of town size (the meetup towns' own
    /// rule, AutonomousTownIdleRouting), not a bind area, a safe zone, a GM spot or a lower-case leftover ("thehiddenlair"). Pure.
    /// </summary>
    public static bool IsTownArea(string description, string classType, int radius) =>
        !string.IsNullOrWhiteSpace(description) && description.Trim().Length >= 4 && char.IsUpper(description.Trim()[0]) &&
        (classType ?? string.Empty).EndsWith("Circle", StringComparison.Ordinal) && radius is >= 500 and <= 6_000 &&
        !description.Contains("bind", StringComparison.OrdinalIgnoreCase);

    private static NamedTown[] GetTowns()
    {
        if (_towns != null)
            return _towns;
        lock (IndexSync)
        {
            if (_towns != null)
                return _towns;
            var towns = new List<NamedTown>();
            try
            {
                foreach (DbArea area in DOLDB<DbArea>.SelectAllObjects())
                {
                    if (area == null || !IsTownArea(area.Description, area.ClassType, area.Radius))
                        continue;
                    string zone = WorldMgr.GetRegion(area.Region)?.GetZone(area.X, area.Y)?.Description;
                    if (!string.IsNullOrWhiteSpace(zone))
                        towns.Add(new NamedTown(area.Description.Trim(), zone.Trim(), area.Region, area.X, area.Y));
                }
            }
            catch (Exception)
            {
                // No world loaded (a unit test): no towns, and the next call tries again.
                return [];
            }
            _towns = towns.ToArray();
            return _towns;
        }
    }

    /// <summary>The town a line names, the longest name first ("where is humberton castle" -> Humberton Castle), or null. Pure.</summary>
    public static NamedTown TownNamed(string text, IEnumerable<NamedTown> towns)
    {
        if (string.IsNullOrWhiteSpace(text) || towns == null)
            return null;
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        return towns.Where(town => town != null && !string.IsNullOrWhiteSpace(town.Name))
            .OrderByDescending(town => town.Name.Length)
            .FirstOrDefault(town => t.Contains(" " + AutonomousChatQuestions.Normalize(town.Name) + " ", StringComparison.Ordinal));
    }

    private static readonly HashSet<string> WhereWords = new(StringComparer.Ordinal)
    {
        "where", "wheres", "is", "s", "that", "the", "how", "do", "does", "i", "we", "get", "go", "to", "which", "way", "find",
        "located", "can", "could", "u", "you", "tell", "me", "far", "again", "ok", "k", "so", "hmm", "um", "and", "exactly", "at",
        "whats", "what", "its", "it", "pls", "plz", "please", "anyone", "know", "ne1", "sorry", "sry",
    };

    /// <summary>
    /// Whether a line only asks the way to the town it names ("where is Humberton Castle", "how do i get to mularn?"), so a
    /// question that merely mentions a town ("where can i buy dyes in mularn", "whats a good camp near ludlow") keeps its own
    /// answer. Pure.
    /// </summary>
    public static bool AsksOnlyWhereTownIs(string text, string town)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(town))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        string n = " " + AutonomousChatQuestions.Normalize(town) + " ";
        int at = t.IndexOf(n, StringComparison.Ordinal);
        if (at < 0)
            return false;
        string rest = t.Remove(at, n.Length).Insert(at, " ");
        string[] words = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length > 0 && words.All(WhereWords.Contains) && words.Any(word => word is "where" or "wheres" or "way" or "find" or "located" or "get");
    }

    /// <summary>The compass way from one spot to another (the world's x runs east, its y south), or null when they are close. Pure.</summary>
    public static string Compass(double dx, double dy)
    {
        if (Math.Sqrt(dx * dx + dy * dy) < 1_500)
            return null;
        double degrees = Math.Atan2(-dy, dx) * 180 / Math.PI;
        int sector = (int)Math.Round(((degrees % 360) + 360) % 360 / 45.0) % 8;
        return sector switch
        {
            0 => "east", 1 => "northeast", 2 => "north", 3 => "northwest", 4 => "west", 5 => "southwest", 6 => "south", _ => "southeast",
        };
    }

    /// <summary>
    /// The answer to "where is Humberton Castle?" (1.2.3, owner's game: a groupmate said "not sure tbh" and another "stablemaster
    /// is your friend"): the zone it is in, and from the asker the way and roughly how far; a horse only when a stable route of
    /// the asker's realm ends in that zone. 2003 typing, true for the town. Pure but for the draw.
    /// </summary>
    public static string TownWhereLine(string town, string zone, string way, Far far, bool sameZone, bool horse, Random random)
    {
        random ??= Random.Shared;
        string n = (town ?? "it").Trim();
        string z = string.IsNullOrWhiteSpace(zone) ? null : zone.Trim();
        string[] lines;
        if (far == Far.Here || far == Far.Close && sameZone)
            lines = way == null
                ? [$"{n}? ur right by it", $"ur basically there, {n} is right here"]
                : [$"{n} is just {way} of u, not far", $"head {way}, {n} is right there", $"{n}? little bit {way} of u"];
        else if (sameZone)
            lines = way == null ? [$"{n} is here in {z}", $"{n}? its in {z}, same zone as u"]
                : [$"{n} is in {z} too, head {way}", $"{n}? same zone, its {way} of u", $"head {way}, {n} is in this zone"];
        else
        {
            string horseTail = horse && far is Far.Trip or Far.LongWay or Far.Unknown ? ", a stable master can get u close" : "";
            lines = way == null
                ? [$"{n} is in {z}{horseTail}", $"{n}? thats in {z}{horseTail}"]
                : far is Far.Trip or Far.LongWay
                    ? [$"{n} is in {z}, way {way} of u{horseTail}", $"{n}? out in {z}, {way} of here{horseTail}"]
                    : [$"{n} is in {z}, {way} of u", $"{n}? in {z}, head {way}", $"head {way} into {z}, {n} is there"];
        }
        return lines[random.Next(lines.Length)];
    }

    /// <summary>
    /// "where?" after a line that names a town of the world ("meet up at Humberton Castle"): where that town is, from the asker
    /// (1.2.3). False when the line names none.
    /// </summary>
    public static bool TryAnswerTownNamedIn(GamePlayer player, string line, out string answer)
    {
        answer = string.Empty;
        if (player == null || string.IsNullOrWhiteSpace(line))
            return false;
        eRealm realm = player.Realm;
        return TownNamed(line, GetTowns().Where(town => RegionIsAccessible(realm, town.RegionId))) is { } found &&
               TryAnswerTown(player, "where is " + found.Name, out answer);
    }

    /// <summary>The live answer to a question naming a town of the world (<see cref="TownWhereLine"/>).</summary>
    private static bool TryAnswerTown(GamePlayer player, string query, out string answer)
    {
        answer = string.Empty;
        if (player == null || string.IsNullOrWhiteSpace(query))
            return false;
        eRealm realm = player.Realm;
        if (TownNamed(query, GetTowns().Where(town => RegionIsAccessible(realm, town.RegionId))) is not { } found ||
            !AsksOnlyWhereTownIs(query, found.Name))
            return false;
        bool sameRegion = player.CurrentRegionID == found.RegionId;
        double dx = found.X - player.X, dy = found.Y - player.Y;
        double? distance = sameRegion ? Math.Sqrt(dx * dx + dy * dy) : null;
        string way = sameRegion ? Compass(dx, dy) : null;
        bool sameZone = string.Equals(player.CurrentZone?.Description, found.Zone, StringComparison.OrdinalIgnoreCase);
        Far far = distance is { } d && d < 3_000 ? Far.Here : FarOf(false, distance);
        bool horse = StableRoutes(realm).Any(route => string.Equals(route.EndpointArea, found.Zone, StringComparison.OrdinalIgnoreCase) ||
                                                     string.Equals(route.Destination, found.Name, StringComparison.OrdinalIgnoreCase));
        answer = TownWhereLine(found.Name, found.Zone, way, far, sameZone, horse, Random.Shared);
        return true;
    }

    private static IReadOnlyDictionary<string, Place[]> GetPlaces()
    {
        if (_places != null)
            return _places;
        lock (IndexSync)
        {
            if (_places != null)
                return _places;

            var building = new Dictionary<string, List<Place>>(StringComparer.OrdinalIgnoreCase);
            foreach (Region region in WorldMgr.GetAllRegions().Where(region => region != null &&
                         AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion)))
            {
                bool capital = AutonomousBotVoices.IsCapitalRegion(region.ID); // the capitals look like dungeons to the server
                Add(building, region.Description, new(region.Description, region.Description, region.ID, region.IsDungeon && !capital, false, 0));
                foreach (Zone zone in region.Zones.Where(zone => zone != null))
                    Add(building, zone.Description, new(zone.Description, region.Description, region.ID, zone.IsDungeon && !capital, false, 0));
                // "where is camelot?": nobody types "camelot city" or "city of camelot" (live test 2026-10-18).
                if (capital && ShortCityName(region.Description) is string shortName)
                    Add(building, shortName, new(region.Description, region.Description, region.ID, false, false, 0));

                foreach (GameNPC npc in region.Objects.OfType<GameNPC>())
                {
                    if (npc?.ObjectState is not GameObject.eObjectState.Active || string.IsNullOrWhiteSpace(npc.Name) || npc.CurrentZone == null)
                        continue;
                    bool monster = npc.Realm == eRealm.None && npc.Level > 0 && npc.Brain != null &&
                                   (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) == 0 &&
                                   npc is not GameMerchant and not GameGuard and not GameTaxi and not GameSummonedPet and
                                   not GameTrainer and not GameTeleporter and not GameHealer and not CraftNPC;
                    // 0.36-se.4: guards, trainers and NPCs named after a role ("Healer", "Armsman", "Ranger") are not
                    // places; a short name is matched only as a whole word (see ContainsPhrase).
                    if (!monster && (npc is GameGuard or GameSummonedPet || IsRoleName(npc.Name) || npc.Name.Trim().Length < 5))
                        continue;
                    if (monster && npc.Name.Trim().Length < 4)
                        continue;
                    Add(building, npc.Name, new(npc.Name, npc.CurrentZone.Description, region.ID,
                        npc.CurrentZone.IsDungeon || region.IsDungeon, monster, npc.Level));
                }
            }
            _places = building.ToDictionary(entry => entry.Key, entry => entry.Value.Distinct().ToArray(), StringComparer.OrdinalIgnoreCase);
            return _places;
        }
    }

    /// <summary>"Camelot City" or "City of Camelot" -> "Camelot"; null for other names.</summary>
    public static string ShortCityName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        if (name.StartsWith("City of ", StringComparison.OrdinalIgnoreCase))
            return name["City of ".Length..].Trim();
        if (name.EndsWith(" City", StringComparison.OrdinalIgnoreCase))
            return name[..^" City".Length].Trim();
        return null;
    }

    private static void Add(Dictionary<string, List<Place>> index, string key, Place place)
    {
        if (string.IsNullOrWhiteSpace(key))
            return;
        if (!index.TryGetValue(key.Trim(), out List<Place> places))
            index[key.Trim()] = places = new();
        places.Add(place);
    }

    public static bool LooksLikeLocationQuestion(string text) =>
        HasWord(text, "where") || HasWord(text, "wheres") || HasWord(text, "where's") || HasWord(text, "find") ||
        HasWord(text, "located") || HasWord(text, "location") || HasPhrase(text, "how do i get") || HasPhrase(text, "how do you get") ||
        HasPhrase(text, "how to get");

    /// <summary>
    /// "where can i buy a horse ticket?" names no destination, so no route matches; it went unanswered in game (2026-10-27).
    /// Any stable master sells tickets, and /stables lists the routes. Pure.
    /// </summary>
    public static bool TryAnswerTicketsInGeneral(string text, out string answer)
    {
        answer = string.Empty;
        string t = (text ?? string.Empty).ToLowerInvariant();
        // 1.2.2 chat audit (C8): "who sells horse tickets?" and "how do i get a horse?" went unanswered.
        bool aboutTickets = t.Contains("ticket") || t.Contains("horse route") || t.Contains("stable") ||
                            System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(get|buy|rent|ride|find) (a |an )?horses?\b");
        bool asks = t.Contains("where") || t.Contains("buy") || t.Contains("get") || t.Contains("how") ||
                    System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(who|anyone|any1) sells?\b");
        if (!aboutTickets || !asks)
            return false;
        answer = Pick("any stable master sells them, most towns have one. /stables lists the routes",
            "stable master in town, they sell tickets to everywhere nearby. /stables shows where they go",
            "buy them from the stable master, every big town has one");
        return true;
    }

    /// <summary>
    /// "where do i buy a better staff for my elem?" (live test 2026-11-08: no answer at all): the Realm Exchange in the capital,
    /// or the crafter who makes it (staves and bows a fletcher, weapons a weaponcrafter, armor an armorcrafter or a tailor).
    /// Pure but for the pick.
    /// </summary>
    public static bool TryAnswerBuyGear(eRealm realm, string text, out string answer)
    {
        answer = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string t = " " + new string(text.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()) + " ";
        bool asks = HasAnyPhrase(t, " where ", " who sells ", " anyone sell ", " how do i get ", " where can i get ", " where to get ") &&
                    HasAnyPhrase(t, " buy ", " get ", " find ", " sells ", " sell ", " better ", " new ", " upgrade ");
        if (!asks)
            return false;
        string crafter =
            HasAnyPhrase(t, " staff ", " staves ", " staffs ", " bow ", " bows ", " longbow ", " arrows ", " crossbow ") ? "fletcher"
            : HasAnyPhrase(t, " sword ", " axe ", " hammer ", " mace ", " spear ", " blade ", " dagger ", " weapon ", " weapons ", " club ", " scythe ", " flail ",
                " rapier ", " polearm ", " halberd ") ? "weaponcrafter"
            : HasAnyPhrase(t, " robe ", " cloth ", " leather ", " vest ") ? "tailor"
            : HasAnyPhrase(t, " armor ", " armour ", " chain ", " plate ", " scale ", " helm ", " boots ", " gloves ", " legs ", " sleeves ", " chest ", " shield ")
                ? "armorcrafter" : null;
        bool jewelry = HasAnyPhrase(t, " ring ", " necklace ", " bracer ", " belt ", " cloak ", " jewel ", " jewelry ", " rings ", " necklaces ",
            " bracers ", " belts ", " cloaks ", " jewels ");
        if (crafter == null && !jewelry && !HasAnyPhrase(t, " gear ", " stuff ", " items ", " equipment "))
            return false;
        string capital = realm switch { eRealm.Midgard => "jordheim", eRealm.Hibernia => "tir na nog", _ => "camelot" };
        answer = crafter != null
            // PC check 2026-11-19 (build 5154cae): "a armorcrafter can make it" -> "an armorcrafter".
            ? Pick($"realm exchange in {capital}, or get {AutonomousBotVoiceCorpus.AOrAn(crafter)} {crafter} to make you one",
                $"check the exchange in {capital}. {AutonomousBotVoiceCorpus.AOrAn(crafter)} {crafter} can make one too",
                $"{AutonomousBotVoiceCorpus.AOrAn(crafter)} {crafter} can make it, or look on the realm exchange in {capital}")
            : jewelry
                ? Pick($"mostly drops, but check the realm exchange in {capital}", $"exchange in {capital} sometimes has some, otherwise drops")
                : Pick($"realm exchange in {capital}, or a crafter", $"check the exchange in {capital}, crafters too");
        return true;
    }

    private static bool HasAnyPhrase(string padded, params string[] phrases) => phrases.Any(padded.Contains);

    private static bool LooksLikeStableQuestion(string text) =>
        text.Contains("stable master", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("stablemaster", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("horse to", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("ticket to", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeSpellQuestion(string text) =>
        text.Contains("what level", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("which level", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("when do i learn", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("when can i cast", StringComparison.OrdinalIgnoreCase) ||
        text.Contains("learn to cast", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a line names a place, both compared as <see cref="AutonomousChatQuestions.Normalize"/> leaves them: no case, no
    /// apostrophes, other punctuation as spaces. PC check 2026-11-19 (build c81eb5d): "is tepoks mine far?" did not name
    /// "Tepok's Mine" by <see cref="ContainsPhrase"/>, so the follow-up was "no clue sry". Pure.
    /// </summary>
    public static bool NamesPlace(string text, string place)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(place))
            return false;
        string name = AutonomousChatQuestions.Normalize(place);
        return name.Length >= 3 && ContainsPhrase(AutonomousChatQuestions.Normalize(text), name);
    }

    /// <summary>
    /// The world place key a question names: the longest key the line names (<see cref="NamesPlace"/>), else the key of a
    /// chat-catalog place the line names by an alias ("tepoks", "cardova", "pennine") when that place is itself a zone of the
    /// world (its key is one of its zones), or null. Pure.
    /// </summary>
    public static string NamedPlaceKey(string query, IEnumerable<string> keys, IEnumerable<AutonomousChatPlaces.Place> catalog)
    {
        if (string.IsNullOrWhiteSpace(query) || keys == null)
            return null;
        string[] all = keys.Where(key => !string.IsNullOrWhiteSpace(key)).ToArray();
        string direct = all.Where(key => NamesPlace(query, key)).OrderByDescending(key => key.Length).FirstOrDefault();
        if (direct != null || catalog == null)
            return direct;
        AutonomousChatPlaces.Place named = AutonomousChatQuestions.PlaceNamed(query, catalog);
        if (named?.Zones == null)
            return null;
        string zone = named.Zones.FirstOrDefault(z => string.Equals(AutonomousChatQuestions.Normalize(z), AutonomousChatQuestions.Normalize(named.Key), StringComparison.Ordinal));
        return zone == null ? null
            : all.FirstOrDefault(key => string.Equals(AutonomousChatQuestions.Normalize(key), AutonomousChatQuestions.Normalize(zone), StringComparison.Ordinal));
    }

    /// <summary>The phrase as whole words (audit 2026-10-18: "cleric trainer" matched the NPC "Raine" in "tRAINEr",
    /// and "camelot" the monster "Mel").</summary>
    public static bool ContainsPhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(phrase) || string.IsNullOrEmpty(text))
            return false;
        string needle = phrase.Trim();
        int start = 0;
        while ((start = text.IndexOf(needle, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int end = start + needle.Length;
            if ((start == 0 || !char.IsLetterOrDigit(text[start - 1])) && (end == text.Length || !char.IsLetterOrDigit(text[end])))
                return true;
            start = end;
        }
        return false;
    }

    private static bool HasWord(string text, string word) => ContainsPhrase(text, word);

    private static bool HasPhrase(string text, string phrase) => ContainsPhrase(text, phrase);

    private static readonly HashSet<string> RoleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "healer", "guard", "merchant", "trainer", "master", "stable master", "stablemaster", "smith", "blacksmith", "armorer",
        "armourer", "weaponsmith", "tailor", "fletcher", "enchanter", "vault keeper", "banker", "guildmaster", "guild registrar",   // chat-check: input
        "innkeeper", "bartender", "hastener", "townsman", "townswoman", "villager", "peasant", "farmer", "messenger", "herald",
        "captain", "sergeant", "lieutenant", "commander", "soldier", "watchman", "sentry", "archer", "scout", "ranger", "hunter",
        "mage", "wizard", "cleric", "priest", "monk", "friar", "druid", "bard", "warrior", "armsman", "champion", "hero", "warden",
    };

    /// <summary>An NPC name that is a role or a class, not someone players name ("Healer", "Armsman", "Ranger").</summary>
    public static bool IsRoleName(string name) => string.IsNullOrWhiteSpace(name) || RoleNames.Contains(name.Trim()) ||
        AllClassNames.Contains(name.Trim());

    private static readonly HashSet<string> AllClassNames = new(Enum.GetNames<eCharacterClass>(), StringComparer.OrdinalIgnoreCase);

    private static string Pick(params string[] options) => options[Draw.Next(options.Length)];

    [ThreadStatic] private static Random _draw;

    /// <summary>The draw of the pure answers that take no random of their own: the lookup's (ports.Random) while one runs, else Random.Shared.</summary>
    private static Random Draw => _draw ?? Random.Shared;

    private static string TicketDestination(string name)
    {
        string value = name?.Trim() ?? string.Empty;
        int marker = value.IndexOf("ticket to", StringComparison.OrdinalIgnoreCase);
        return marker >= 0 ? value[(marker + "ticket to".Length)..].Trim() : value;
    }

    // Matches the authoritative Classic/SI homeland ownership used by /mobs and player travel.
    private static bool RegionIsAccessible(eRealm realm, ushort regionId)
    {
        if (regionId is 1 or 10 or 20 or 21 or 22 or 23 or 24 or 50 or 51 or 60 or 61 or 62)
            return realm == eRealm.Albion;
        if (regionId is 100 or 101 or 125 or 126 or 127 or 128 or 129 or 150 or 151 or 160 or 161)
            return realm == eRealm.Midgard;
        if (regionId is 180 or 181 or 190 or 191 or 192 or 193 or 194 or 200 or 201 or 220 or 221 or 222 or 223 or 224)
            return realm == eRealm.Hibernia;
        return true;
    }
}
