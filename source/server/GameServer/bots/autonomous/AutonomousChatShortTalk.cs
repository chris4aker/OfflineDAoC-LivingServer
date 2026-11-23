using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Word rules for the short chat and common requests players type (0.36-se.4). They run before the
/// classifier: "ty", "gz", "lol", "brb", "wb", "inc", "rez pls", "wts", "lfg", "ding 20", "is friar good?",
/// "are you a bot?". The audit (2026-10-18) ran 90 realistic messages through the classifier alone: 41%
/// were read right, and 19 of them ("lol", "ty", "gz", "inc", "brb"...) read as real-world talk.
/// Pure and tested (UT_ChatShortTalk).
/// </summary>
public static class AutonomousChatShortTalk
{
    private static HashSet<string> Set(params string[] words) => new(words, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ThanksWords = Set("ty", "thx", "thanks", "thank", "tyvm", "tyty", "tx", "thnx", "thanx",
        "thks", "ta", "tyfr", "cheers", "appreciated");
    private static readonly HashSet<string> CongratsWords = Set("gz", "grats", "gratz", "grtz", "congrats", "congratulations",
        "gratss", "gzz", "grats!");
    // 1.2 chat audit (DESIGN-047): "bbiab", "bbs", "bak", "returned", "ttyl", "bbl", "ttfn" were read as nothing.
    private static readonly HashSet<string> AwayWords = Set("brb", "afk", "bio", "brt", "bbiab", "bbs");
    private static readonly HashSet<string> BackWords = Set("back", "re", "bk", "rebuffed", "bak", "returned");
    private static readonly HashSet<string> FarewellWords = Set("gn", "gnight", "goodnight", "nite", "bye", "byebye", "cya", "cu",
        "laters", "gtg", "g2g", "farewell", "nn", "ttyl", "bbl", "ttfn");
    private static readonly HashSet<string> GreetingWords = Set("hi", "hello", "hey", "heya", "hiya", "yo", "hail", "sup", "greetings",
        "howdy", "hallo", "hiho", "evening", "morning", "afternoon", "o", "evenin", "mornin", "morn", "eve", "hiya", "hallo", "salut", "hola", "ello");
    private static readonly HashSet<string> GreetingObjects = Set("all", "everyone", "everybody", "guys", "folks", "realm", "there",
        "ppl", "people", "friends", "yall", "albion", "midgard", "hibernia", "albs", "mids", "hibs", "guild", "again", "back", "peeps", "gang",
        "folk", "lads", "ladies", "team", "crew", "fam", "everyone", "mates", "all");
    private static readonly HashSet<string> LaughWords = Set("lol", "lolol", "lololol", "lmao", "rofl", "roflmao", "haha", "hahaha",
        "hehe", "heh", "hah", "ha", "lul", "xd", "hehehe", "ahaha", "lawl", "kek", "gg", "omg", "wow", "nice");   // chat-check: input
    private static readonly HashSet<string> RezWords = Set("rez", "res", "rezz", "rezzes", "ress", "resurrect", "resurrection", "rezzer", "rezzers");
    private static readonly HashSet<string> AlarmWords = Set("inc", "incs", "incoming");
    private static readonly HashSet<string> TradeWords = Set("wts", "wtb", "wtt", "pc", "selling", "buying", "sell", "buy",
        "price", "trade", "trading", "auction", "plat", "craft", "crafter", "crafting", "crafted", "spellcraft", "spellcrafter",
        "alchemist", "tailor", "armorcrafter", "weaponcrafter", "fletcher", "commission");
    private static readonly HashSet<string> GroupWords = Set("lfg", "lfm", "lf1m", "lf2m", "lf3m", "lf4m", "lfmore", "group", "grp",
        "groups", "grps", "party", "inv", "invite", "duo", "trio", "fg", "lfguild", "lf", "forming", "puller", "pullers");
    private static readonly HashSet<string> RvrWords = Set("rvr", "frontier", "relic", "relics", "emain", "hadrians",
        "hadrian", "odins", "mg", "amg", "hmg", "mmg", "milegate", "stealther", "stealthers", "rps", "rp", "rr", "thid", "thidranki",
        "abermenai", "murdaigean", "caledonia", "cathal", "bg", "battleground", "siege", "realmpoints", "zerg", "zergs", "zerging",   // chat-check: input
        "benowyc", "berkstead", "boldiam", "bledmeer", "crauchon", "crimthainn", "nottmoor", "glenlock", "renaris", "erasleigh",
        "sursbrooke", "hurbury", "blendrake", "arvakr", "fensalir", "hlidskialf", "nottmoor", "bolg", "ailline", "behnn", "scathaig",
        // 1.2 chat audit: the short keep names players typed ("lets take bled", "we lost beno again"), and keep talk.
        "beno", "berk", "bled", "crim", "crau", "nott", "keeps", "defending", "defenders");
    private static readonly HashSet<string> DungeonWords = Set("dungeon", "dungeons", "tomb", "cave", "caves", "caverns",
        "barrows", "den", "lair", "spindelhalla", "spindel", "varulvhamn", "varulv", "df", "darkness", "treibh", "coruscating",
        "keltoi", "fogou", "tepok", "tepoks", "mithra", "vendo", "nisse", "nisses", "muire", "spraggon", "koalinth", "catacombs");   // chat-check: input
    private static readonly HashSet<string> BanterWords = Set("same", "true", "ikr", "agreed", "yep", "yup", "indeed", "word", "truth",   // chat-check: input
        "exactly", "totally", "right", "ouch", "yikes", "oof", "ugh", "bah", "meh", "nice", "sweet", "cool", "awesome", "wow");   // chat-check: input
    private static readonly HashSet<string> GankWords = Set("gank", "ganked", "ganking", "ganks", "stealthed", "stealther", "stealthers");
    private static readonly HashSet<string> BotWords = Set("bot", "bots", "npc", "npcs", "ai", "program", "script", "scripted", "robot");
    private static readonly HashSet<string> SpecWords = Set("spec", "specs", "specced", "respec", "build", "nerf", "nerfed", "nerfs",
        "op", "overpowered", "gimped", "gimp", "uber", "underpowered");
    private static readonly HashSet<string> OpinionWords = Set("good", "bad", "better", "best", "worse", "worst", "fun", "worth",
        "op", "strong", "weak", "viable", "think", "recommend", "favorite", "easy", "hard", "squishy", "tanky", "solo",
        "soloing", "soloable", "useful", "useless", "broken");

    /// <summary>Every class name of the three realms, lower-cased, with the usual short forms.</summary>
    private static readonly HashSet<string> ClassWords = Set("armsman", "armsmen", "paladin", "paladins", "pally", "mercenary", "merc",
        "mercs", "reaver", "reavers", "scout", "scouts", "infiltrator", "infiltrators", "infil", "infils", "minstrel", "minstrels",
        "minstrels", "cleric", "clerics", "friar", "friars", "wizard", "wizards", "wiz", "theurgist", "theurgists", "theurg", "theu",
        "sorcerer", "sorcerers", "sorc", "sorcs", "cabalist", "cabalists", "cabby", "necromancer", "necromancers", "necro", "necros",
        "warrior", "warriors", "thane", "thanes", "skald", "skalds", "berserker", "berserkers", "zerker", "zerkers", "savage",
        "savages", "hunter", "hunters", "shadowblade", "shadowblades", "sb", "sbs", "healer", "healers", "shaman", "shamans",
        "shammy", "runemaster", "runemasters", "rm", "rms", "spiritmaster", "spiritmasters", "sm", "sms", "bonedancer",
        "bonedancers", "bd", "bds", "hero", "heroes", "champion", "champions", "champ", "champs", "blademaster", "blademasters",
        "bm", "bms", "warden", "wardens", "druid", "druids", "bard", "bards", "ranger", "rangers", "nightshade", "nightshades",
        "ns", "eldritch", "eldritches", "eld", "enchanter", "enchanters", "ench", "mentalist", "mentalists", "menta", "animist",
        "animists", "valewalker", "valewalkers", "vw");

    private static readonly HashSet<string> ResistKinds = Set("heat", "cold", "matter", "body", "spirit", "energy", "crush", "slash", "thrust",
        "magic", "melee", "resist", "resists");
    private static readonly HashSet<string> ResistUses = Set("gear", "cap", "capped", "buff", "buffs", "cloak", "jewel", "jewelry", "item",
        "items", "armor", "piece", "pieces");

    /// <summary>"res" meaning a resist: "heat res gear", "what res should i cap" (review 2026-10-18).</summary>
    private static bool IsResist(string[] words)
    {
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i] is not ("res" or "ress"))
                continue;
            if (i > 0 && ResistKinds.Contains(words[i - 1]) || i + 1 < words.Length && ResistUses.Contains(words[i + 1]) ||
                i + 3 < words.Length && (words[i + 2] is "cap" or "capped" || words[i + 3] is "cap" or "capped"))
                return true;
        }
        return false;
    }

    /// <summary>The class words in a text ("friar", "zerkers", "sm").</summary>
    public static string[] ClassWordsIn(string text) =>
        AutonomousChatIntentModel.Words(text).Where(ClassWords.Contains).Distinct().ToArray();

    /// <summary>
    /// "anyone know a good paladin spec?", "how should i spec my zerk", "what do i put points in on a thane": a question about how to
    /// spec a class (1.2 chat audit, CORPUS-C-003, LOG-027: it drew a remark, "paladins have so many chants"). Pure.
    /// </summary>
    public static bool AsksSpec(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] words = AutonomousChatIntentModel.Words(text);
        bool specWord = words.Any(word => word is "spec" or "specs" or "respec" or "build" or "specced") || HasPhrase(words, "points in", "put points", "spend points", "spend my points");
        bool asks = text.Contains('?') || AutonomousChatQuestions.IsQuestion(text) || HasPhrase(words, "anyone know", "any tips", "any advice", "how should i", "how do i", "what should i");
        return specWord && asks && !HasPhrase(words, "spec is", "specced into", "i specced", "my spec is", "nerf");
    }

    /// <summary>True when a text names any class.</summary>
    public static bool NamesAClass(string text) => AutonomousChatIntentModel.Words(text).Any(ClassWords.Contains);

    /// <summary>"friar" and "friars", "zerker" and "zerkers": the same class word, singular or plural.</summary>
    public static bool SameClassWord(string a, string b) =>
        string.Equals(a.TrimEnd('s'), b.TrimEnd('s'), StringComparison.OrdinalIgnoreCase);

    /// <summary>"how are you?", "hows everyone", "sup": a greeting that asks back, answered "not bad, you?" rather than "hi"
    /// (live test 2026-10-18: "hi all" drew "not bad, and you?").</summary>
    public static bool AsksHowAreYou(string text) =>
        HasPhrase(AutonomousChatIntentModel.Words(text).Select(word => word.Replace("'", string.Empty)).ToArray(),
            "how are you", "how are u", "how r u", "how r you", "how are ya", "how are yall", "how is everyone", "how is everybody",
            "hows everyone", "hows everybody", "hows it going", "how is it going", "how are things", "hows things", "how you doing",
            "how u doing", "how ya doing", "how goes it", "whats up", "wassup", "sup");

    /// <summary>"anyone on?", "anybody here?", "whos on": asks who is around, answered "here" or "im on" (live test
    /// 2026-10-18: /gu "anyone on?" drew "good question, someone else might know").</summary>
    public static bool AsksWhoIsOn(string text)
    {
        string[] words = AutonomousChatIntentModel.Words(text).Select(word => word.Replace("'", string.Empty)).ToArray();
        if (HasPhrase(words, "anyone here from", "anybody here from", "anyone on from"))
            return false;
        // Beta 1.1.2: "ne1 on?", "hey u there?", "you around?" (blind round 1: read as nothing or "not sure").
        if (words.Length <= 5 && HasPhrase(words, "ne1 on", "ne1 here", "ne1 around", "u there", "you there", "r u there", "are you there",
                "are u there", "u around", "you around", "r u around", "u still on", "you still on", "u awake", "you awake", "u still there",
                "you still there"))
            return true;
        return words.Length <= 5 && HasPhrase(words, "anyone on", "anybody on", "any1 on", "anyone here", "anybody here", "anyone around",
            "anybody around", "anyone awake", "anyone online", "anybody online", "who is on", "whos on", "whos around", "who is around",
            "anyone still on", "anyone still up", "anyone up");
    }

    /// <summary>
    /// "ready?", "rdy?", "everyone ready", "we pulling or what", "is the healer oom", "how much power you got": a question about
    /// whether the group can go on (1.2 chat audit, DESIGN-005). Pure.
    /// </summary>
    public static bool AsksReady(string[] words, string text)
    {
        if (words.Length == 0 || words.Length > 7)
            return false;
        bool asks = (text ?? string.Empty).Contains('?') || words[0] is "everyone" or "every1" or "all" or "u" or "you" or "we" or "is" or "are" or "r" or "how" or "hows";
        if (words.Length <= 3 && words.All(word => word is "ready" or "rdy" or "rdy2" or "everyone" or "every1" or "all" or "u" or "you" or "guys" or "we" or "yet" or "r" or "are" or "to" or "go" or "pull") &&
            words.Any(word => word is "ready" or "rdy") && asks)
            return true;
        return HasPhrase(words, "everyone ready", "every1 ready", "all ready", "everybody ready", "are we ready", "r we ready", "ready to pull", "rdy to pull",
                   "ready for the pull", "ready to go", "we pulling", "are we pulling", "r we pulling", "can we pull", "ok to pull", "good to pull",
                   "u oom", "you oom", "anyone oom", "is the healer oom", "healer oom", "how much power", "how much pow", "how much mana",
                   "hows ur pow", "hows your pow", "how is your power", "hows ur power", "hows your power", "how is everyone on power", "hows everyone on pow",
                   "everyone got pow", "everyone have power", "u got pow", "you got pow", "need to med") && asks && !HasPhrase(words, "how much for", "how much is", "how much are");
    }

    /// <summary>The question type a player's line asks, answered from that reply set before replies by topic: "ask_howareyou",
    /// "ask_on", or null.</summary>
    public static string QuestionShape(string text)
    {
        // "ok", "sure", "nice", "me too": an acknowledgement, which nobody answers (chat audit 2026-10-21: "yes" drew
        // "interesting", "sure" drew "lol what", "nice" drew "ouch been there").
        if (IsAcknowledgement(text))
            return "ack";
        string[] said = AutonomousChatIntentModel.Words(text).Select(word => word.Replace("'", string.Empty)).ToArray();
        // "re all, isp died again", "re, crashed to desktop": a hello back first, read as one (Beta 1.1.2).
        if (said.Length is > 0 and <= 8 && said[0] is "re" or "rehi" && !text.Contains('?'))
            return null;
        // The talking update audit (2026-10-26): a decline nobody asked for ("no thanks im good" in /faction, to someone else)
        // gets no answer; one that answers a bot's offer is read as part of that thread first.
        if (said.Length <= 5 && !text.Contains('?') && HasPhrase(said, "no thanks", "no thx", "no ty", "nah im good", "nah im ok",
                "im good thanks", "im ok thanks", "nah thanks", "nvm", "never mind", "nevermind", "nvm found it"))
            return "ack";
        // A player's good news ("finally got my epic", "woot 35", "got a masterpiece"): grats, not a remark on the topic.
        if (IsGoodNews(said))
            return "player_goodnews";
        // An offer ("want some buffs?", "need a hand with that?", "i got an extra pair of boots u want em"): a thank-you or a
        // polite no, not "how many are you? i can fill a spot".
        // "need buffs?" offers; "i need buffs" asks, so a "need ..." offer is a question that doesn't start with "i".
        // The holdout check: "anyone need buffs? casting at the bindstone" has its "?" in the middle.
        bool needOffer = said.Length > 0 && said[0] != "i" &&
                         (text.TrimEnd().EndsWith('?') || said[0] is "anyone" or "any1" or "anybody" or "who" or "need" or "u" or "you" && text.Contains('?')) &&
                         HasPhrase(said, "need a hand", "need buffs", "need some buffs", "need coins", "need money", "need some coin",
                             "need some money", "need a buff", "need help with that", "need a rez", "need a port", "need speed");
        // "if you need a healer pst me, lfg 30 cleric" is an lfg, not an offer (review 2026-10-26).
        bool lfg = said.Any(word => word is "lfg" or "lfm" or "lf1m" or "lf2m" or "lf3m");
        if (!lfg && (needOffer || HasPhrase(said, "want some buffs", "want buffs", "want a buff", "i can pl", "can pl u", "can pl you", "want em",
                "u want it", "you want it", "want them", "i have spare", "i got spare", "have some spare", "got some spare", "got an extra", "have an extra", "ill give u",
                "ill give you", "i can give you", "i can give u", "trade u the", "trade you the", "want me to buff", "want me to help",
                "want me to rez", "want a rez", "want some help", "want a pl", "u want a pl", "want a port", "want speed", "want a speed",
                "if any1 needs", "if anyone needs", "if anybody needs", "if u need", "if you need", "if ya need", "anyone need anything",
                "giving away", "giving it away", "free to a good home", "anyone want my", "anyone want this", "anyone want these",
                "can make you", "can make u", "i can craft", "ill craft", "i can make") ||
            // "anyone want free spellcraft on their armor", "free buffs at the bind": something given away.
            OffersFree(said)))
            return "player_offer";
        // "can anyone spare a few coins for gear?": asking for charity gets a no with advice or a jab, not "dunno" or an exchange
        // advert (in-game check 2026-10-27, Midgard and Hibernia). The same test marks the player for the beggars.
        if (AutonomousBeggars.AsksForCharity(text))
            return "ask_charity";
        // "wb Sigrun": the bot welcomed back says ty (the audit: "wb sigrun" drew "hi hi, welcome back" for the player).
        if (said.Length is > 0 and <= 4 && said[0] is "wb" or "welcome" && (said[0] == "wb" || said.Length > 1 && said[1] == "back"))
            return "player_wb";
        // "Aldric pull the next one", "fiona heal me": an order to a groupmate, answered "on it" by the bot named.
        if (!text.Contains('?') && said.Length is > 0 and <= 8 && IsOrder(said) && AutonomousChatIntentModel.Predict(text) != eAutonomousChatIntent.Abuse)
            return "player_order";
        // A warning ("stealthers near the dock watch out", "heads up, adds"): thanks for the heads up.
        if (!text.Contains('?') && HasPhrase(said, "watch out", "heads up", "look out", "be careful", "careful all", "careful guys",
                "stealthers near", "stealther near", "stealthers around", "stealther around"))
            return "player_headsup";
        // "dont rez me yet", "hold the pull", "wait for me": a groupmate says ok.
        // Not "anyone need pow pots?" or "i always need pow" (review 2026-10-26).
        if (HasPhrase(said, "dont rez", "don t rez", "no rez yet", "wait to rez", "dont res", "dont pull", "hold the pull", "hold pulls",
                "wait for me", "wait up", "hold up", "sec need pow", "wait for the cleric", "wait for heals", "wait for buffs", "low pow", "low power",
                "need a sec", "need a min", "gimme a sec", "give me a sec", "one sec need", "med up", "need to med", "let me med") ||
            said.Length <= 4 && !text.Contains('?') && HasPhrase(said, "need pow", "need power", "need mana", "oom sec", "need to med"))
            return "player_wait";
        if (AsksHowAreYou(text))
            return "ask_howareyou";
        if (AsksWhoIsOn(text))
            return "ask_on";
        // "where is everyone?" in /gu asks where the guild is, not for directions (played 2026-10-18: it read as travel).
        string[] where = AutonomousChatIntentModel.Words(text).Select(word => word.Replace("'", string.Empty)).ToArray();
        // The holdout check: "where is everyone getting xp in the 30s" asks where to hunt, not where the guild is.
        bool hunting = where.Any(word => word is "xp" or "exp" or "level" or "leveling" or "levelling" or "lvl" or "lvling" or "hunt" or
            "hunting" or "camp" or "camping" or "grinding" or "grind");
        if (!hunting && HasPhrase(where, "where is everyone", "wheres everyone", "where is everybody", "wheres everybody", "where did everyone go",
                "where is the guild", "where are you all", "where are you guys", "where r u all", "where is everyone at",
                "where yall at", "where are yall", "where all you at"))
            return "ask_whereall";
        string[] words = AutonomousChatIntentModel.Words(text).Select(word => word.Replace("'", string.Empty)).ToArray();
        // "what level are you?" in /group drew "sure Wrenfield, what do you need?" (live test 2026-10-18).
        if (HasPhrase(words, "what level are you", "what lvl are you", "what level r u", "what lvl r u", "whats your level", "whats ur level",
                "what level is everyone", "what lvl is everyone", "what level are you all", "how high are you"))
            return "ask_level";
        if (HasPhrase(words, "how close are you to", "how close r u to", "how close is everyone to", "how far to your next level"))
            return "ask_xp";
        // 1.2.2 chat audit (C4, fix F3): where to level, read as a pattern ("good place to lvl at 18", "best xp for 21", "where do 24s
        // hunt", "im 36, where should i go now", "where do i go after 33", "15->20?"), ahead of class talk ("any good spots for a 12
        // paladin?" drew "Paladin chants carry a whole group.").
        if (AsksWhereToLevel(text))
            return "ask_hunt";
        // "where to next?" in a group: the next hunt, not directions (live test 2026-10-18).
        // 1.2 chat audit (DESIGN-005): "where we headed?", "where to?", "what next", "next camp?" were read as nothing, and in a
        // group fell to "dunno" (docs/PLAYED-CHECK-1.2.md: "where we headed?" drew no answer).
        if (HasPhrase(words, "where to next", "where now", "where next", "where are we heading", "where are we going", "where should we go",
                "where should we head", "where we going", "where we heading", "where we headed", "where are we headed", "where r we going",
                "where r we headed", "where r we heading", "where we off to", "where are we off to", "what next", "whats next", "what is next",
                "next camp", "where to now") ||
            words.Length <= 3 && HasPhrase(words, "where to"))
            return "ask_next";
        // "what are you up to?" in /gu drew "lol what" (live test of 0.37-se.1, 2026-10-18): what the bot is doing now, or,
        // with "tonight" or "later", its plans.
        if (HasPhrase(words, "what are we doing", "what are we up to", "what r we doing", "what r we up to", "whats the plan", "what is the plan",
                "whats the plan tonight", "whats our plan", "what is our plan", "any plans tonight"))
            return "ask_plans";
        // 1.2 chat audit (DESIGN-005, DESIGN-041): "ready?", "everyone ready", "we pulling or what", "is the healer oom", "how much
        // power you got": the group's readiness, answered from the bot's real power and health (AutonomousChatContext).
        if (AsksReady(words, text))
            return "ask_ready";
        if (HasPhrase(words, "who is pulling", "whos pulling", "who s pulling", "who pulls", "who is pulling next", "whos gonna pull", "who wants to pull"))
            return "ask_who_pulls";
        if (HasPhrase(words, "what are you up to", "what r u up to", "what are u up to", "what you up to", "what u up to",
                "what are you doing", "what r u doing", "what are u doing", "what you doing", "whatcha doing", "wyd",
                "what is everyone up to", "whats everyone up to", "what is everyone doing", "whats everyone doing",
                "what are you all up to", "what are you guys up to", "what are you all doing", "what are you guys doing",
                "what is everybody up to", "whats everybody up to", "whats everybody doing", "what are yall up to"))
            return HasPhrase(words, "tonight", "later", "tomorrow", "this weekend") ? "ask_plans" : "ask_doing";
        // "lf guild" that no bot guild took up (already in a guild, bot guilds off) drew "i could use a group too".
        if (AutonomousBotGuildChatLines.AsksForGuild(text, false))
            return "ask_guild";
        // "mind if i hunt near cotswold?" drew "what?" and "yeah?" (live test 2026-10-18).
        if (HasPhrase(words, "mind if i", "ok if i", "okay if i", "is it ok if", "alright if i", "anyone mind if"))
            return "ask_permission";
        // "should i roll a cleric or a friar?"
        if (IsEitherOr(text))
            return "ask_choice";
        // Chat audit 2026-10-21: these fell to replies by topic that did not answer them ("where is a good place to level at 24?"
        // drew "take the horse from Camelot", "anyone in camelot?" "maybe maybe not", "how much is a bloodied sword worth?"
        // "not my class sorry", "anyone want to do a dungeon?" "take it slow, one pull at a time", "i died again" "what?").
        if (AutonomousBotChatCoordinator.AsksWhereYouAre(text))
            return "ask_whereyou";
        if (PlaceAsked(text) != null)
            return "ask_inplace";
        if (HasPhrase(words, "best place to solo", "best place to level", "best place to hunt", "best spot to solo", "best spot to level", "good place to solo",
                "where to solo", "where should i solo", "where can i solo", "place to solo",
                "good place to level", "good place to hunt", "good spot to level", "good spot to hunt", "good place to xp",
                "good spot to xp", "where to xp", "good xp spot", "good spot for", "good camp for",
                "good place for", "good xp for", "where is good xp", "wheres good xp", "where to level", "where to hunt", "where should i level",
                "where should i hunt", "where can i level", "where can i hunt", "where do i level", "where do i hunt", "where is a good camp",
                "wheres a good camp", "where is a good spot", "wheres a good spot", "where is a good place", "wheres a good place",
                "where do people level", "where do people hunt", "where is everyone leveling", "where are people leveling") ||
            words.Length > 0 && words[0] is "where" or "wheres" && hunting)
            return "ask_hunt";
        if (HasPhrase(words, "where are the mids", "where are the hibs", "where are the albs", "where are the enemies", "where is the enemy",
                "wheres the enemy", "where is the zerg", "wheres the zerg", "where did the zerg go", "any mids out", "any hibs out", "any albs out",
                "seen any mids", "seen any hibs", "seen any albs", "any enemies out", "where is the fight", "wheres the fight", "where is the action",
                "wheres the action", "hows the zerg", "how is the zerg", "hows emain", "how is emain", "hows the frontier", "how is the frontier",
                "hows rvr", "how is rvr", "hows hadrians", "hows odins", "any action", "any fights", "is emain busy", "is emain quiet",
                // Play test 2026-10-27: "whats going on in rvr tonight?" drew "wish i knew" and an off-topic line.
                "whats going on in rvr", "what is going on in rvr", "whats happening in rvr", "what is happening in rvr", "whats up in rvr",
                "anything going on in rvr", "anything happening in rvr", "whats going on in the frontier", "whats happening in the frontier",
                "whats going on in emain", "whats happening in emain", "hows rvr tonight", "any rvr tonight", "anything in rvr"))
            return "ask_enemy";
        if (HasPhrase(words, "how much", "price check", "pc on", "what do they go for", "what does it go for", "what do these go for",
                "what are they worth", "what is it worth") || words.Contains("worth") && HasPhrase(words, "what is", "whats", "what are") ||
            // "pc thrustbender mantle?": a price check (1.2 chat audit, CORPUS-C-007), not "pc crashed".
            words.Length is > 1 and <= 6 && words[0] == "pc" && words[1] is not ("crashed" or "died" or "froze" or "is" or "keeps" or "broke" or "broken"))
            return "ask_price";
        if (!words.Any(GroupWords.Contains) && HasPhrase(words, "anyone want to", "anyone wanna", "anyone going", "anyone up for",
                "anyone feel like", "anybody want to", "anybody wanna", "anybody going", "anybody up for", "any1 want to", "any1 wanna",
                "who wants to", "who wants", "who is up for", "whos up for", "anyone doing", "anyone running", "anyone heading", "anyone coming",   // chat-check: input
                "anybody coming", "anyone interested in", "anyone keen"))   // chat-check: input
            return "ask_anyone";
        if (HasPhrase(words, "best spec", "what spec", "which spec", "how should i spec", "how do i spec", "how to spec", "any tips",
                "any advice", "tips for", "advice for", "how do i make money", "how do i get money", "how to make money", "how do i get gold",
                "how do i make gold", "how do i get coin", "how do i make coin", "how do i get plat", "how do i make plat", "how do i level",
                "how do i solo", "how to level", "how to solo", "how do i get better", "how do i get good"))
            return "ask_advice";
        if (HasPhrase(words, "what should i do", "what do i do now", "what do i do next", "what should i do next", "what now"))
            return "ask_advice";
        if (HasPhrase(words, "can you help", "can someone help", "can anyone help", "can somebody help", "help me", "i need help", "need help",
                "anyone help", "could use some help", "could use help") && words.Length <= 6 ||
            // "can someone help me kill the named in keltoi fogou" (the audit: drew a dungeon tip)
            HasPhrase(words, "can someone help me", "can anyone help me", "any1 help me", "anyone help me", "help me kill", "help me with",
                "need help with", "need help killing", "help a lowbie", "need help w", "need help on", "help w my", "help with my",
                "can any1 come", "can anyone come", "can someone come", "can somebody come", "come get me", "lead me out", "show me the way",
                "anyone able to help", "could someone help", "could anyone help"))
            return "ask_help";
        if ((text.Contains('?') || AutonomousChatQuestions.IsQuestion(text)) && (words.Contains("worth") || words.Contains("decent") ||
                HasPhrase(words, "what do you think", "what do u think", "thoughts on", "do you think", "would you", "is it smart", "is it a good idea",
                "good idea to", "agree", "anyone think", "any1 think", "anyone else think", "do ppl think", "do people think", "is it true",
                "better than", "or is it me", "or is it just me", "is it just me", "what do u guys think", "what do you guys think",
                "what do yall think", "what does everyone think", "hows the new", "how is the new", "how are the new") || HasPhrase(words, "should i") && !words.Any(word => word is "what" or "where" or "how" or "which" or "when" or "who")))
            return "ask_opinion";
        // Beta 1.1.2: a mishap told for a laugh ("i fell off the bridge in tir na nog. again.", "omg i jsut got 1 shotted by a grey
        // con", "dont mind me, just dying over here"): "lol rip", not advice.
        if (!text.Contains('?') && IsMishap(words))
            return "player_mishap";
        // A call for help ("help please, this mob is killing me", "need heals here!!", "im lost in the forest can someone help",
        // "first char, very confused lol"): someone offers to come or asks what is up.
        if (AsksForHelp(words, text))
            return "player_help";
        // A real-life remark (guild and group chat in 2003 were full of them): a short reply in kind, by subject.
        // "afk 5 pizza here" is a brb first (the holdout check).
        // "re all, isp died again" is a hello back first (Beta 1.1.2).
        // "game crashed 3 times today, thx mythic": a complaint (Beta 1.1.2, round 2).
        if (HasPhrase(words, "thx", "thanks", "ty", "thank") && IsSarcasticThanks(words))
            return "complaint";
        if (!words.Any(AwayWords.Contains) && words.Length > 0 && words[0] is not ("re" or "back") && RealLife(words) is { } life)
            return "player_rl_" + life;
        // "so i died to a blue con mob?" is a complaint with a question mark, not a question.
        // Beta 1.1.2: "that was close" (relief), "sorry, my fault" (an apology): their own short replies.
        if (!text.Contains('?') && words.Length <= 6 && HasPhrase(words, "that was close", "close one", "too close", "that was a close one", "phew", "whew",
                "almost died", "nearly died", "that was scary"))
            return "player_close";
        if (!text.Contains('?') && words.Length <= 8 && HasPhrase(words, "my fault", "my bad", "sorry my", "sry my", "my mistake", "sorry guys", "sorry all",
                "sorry about that", "sry about that", "oops sorry", "sorry for the", "sry for the", "i messed up", "my pull"))
            return "player_sorry";
        if ((!text.Contains('?') || !AutonomousChatQuestions.StartsLikeQuestion(text)) && (HasPhrase(words, "i died", "died again", "keep dying", "we wiped", "i give up", "so slow", "hate this",
                "i hate", "worst day", "yelled at me", "nerfed my", "nerfed us", "so broke", "lost all my", "im broke", "i m broke", "bad day", "long day",
                "this is hard", "fml", "lost my", "no drops", "nothing drops", "so tired of", "cant win", "can't win", "got ganked", "ganked again",
                "went ld", "lost 2 bubbles", "lost a bubble", "lost a level", "lost xp", "ninjas it", "ninjaed", "ninjad", "kinda lonely",
                "quit the game", "keeps crashing", "keep crashing", "cant find a group", "can t find a group", "no groups", "nobody wants",
                "no one wants",
                // The holdout check: "3 hours lfg and still no grp", "lfg chat is so dead tonight", "died to a grey mob lol im so bad",
                // "my mezz got resisted again", "oh great another wipe".
                "still no grp", "still no group", "still lfg", "hours lfg", "so dead", "is dead tonight", "dead tonight", "died to a",
                "im so bad", "i suck", "got resisted", "resisted again", "keeps resisting", "fizzled", "interrupted again", "another wipe",
                "wiped again", "missed again", "no luck", "lag is", "lagging", "so laggy", "the lag", "is killing me", "bugged out", "bugged again",
                "is bugged", "broke again", "bubbles to a", "lost 2 bubbles", "lost 3 bubbles", "lost a bubble", "how is that even possible") ||
                words.Any(word => word is "sucks" or "ugh" or "annoying" or "wiped" or "argh" or "sigh")))
            return "complaint";
        return null;
    }

    /// <summary>
    /// What an in-character line does, for a reply in kind (Beta 1.1.2, round 2): "challenge" ("Halt! State thy business"),
    /// "question" ("Knowest thou where...", "Have ye seen..."), "greeting" ("Hail, good sir", "Well met"), "mourn" ("*lights a
    /// candle for the fallen*"), "toast" ("*raises a mug* To Midgard!"), or "emote" for anything else. Pure.
    /// </summary>
    public static string RoleplayAct(string text)
    {
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        bool emote = (text ?? string.Empty).TrimStart().StartsWith('*') || (text ?? string.Empty).TrimStart().StartsWith("/me", StringComparison.OrdinalIgnoreCase);
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (halt|stand fast|who goes there|state (thy|your) (business|name)|declare (thyself|yourself)|draw (thy|your) (blade|sword)|have at (thee|you)|i challenge|en garde|defend (thyself|yourself)) "))
            return "challenge";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (candle|fallen|mourn|grieve|rest in peace|rip|the dead|lost a brother|lost a sister|in memory|kneels|bows (his|her) head|weeps|lament) "))
            return "mourn";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (raises (a|his|her|my) (mug|tankard|cup|glass|horn|ale)|a toast|toast to|to midgard|to albion|to hibernia|skal|skol|cheers|drinks to|for the realm) "))
            return "toast";
        if (AutonomousChatQuestions.IsQuestion(text) || System.Text.RegularExpressions.Regex.IsMatch(t, @" (knowest thou|pray tell|have ye|hast thou|canst thou|where might|whither|what news|tell me) "))
            return "question";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^ (hail|well met|greetings|good morrow|good evening|good day|good eve|hello|hey|hi) "))
            return "greeting";
        return emote ? "emote" : "remark";
    }

    private static readonly HashSet<string> ArchaicWords = Set("thee", "thy", "thou", "thine", "hast", "dost", "doth", "forsooth",
        "methinks", "whither", "mayhap", "milady", "milord", "tis", "twas", "nay", "verily", "prithee", "befall", "morrow",
        "wouldst", "shouldst", "couldst", "canst", "shalt", "wilt", "naught", "yon", "yonder", "begone", "hither", "thither");

    /// <summary>
    /// An in-character line (Beta 1.1.2): archaic words ("thy", "whither", "good morrow"), "ye" as a pronoun, an emote written
    /// out ("*bows deeply* My lady...", "/me does a happy dance"), an oath ("By Odin's beard"), or a formal sentence in the
    /// voice of a story ("The Morrigan watches over us tonight.", "I seek the tavern."). Not chat words like "lol", "u" or "pls".
    /// Pure.
    /// </summary>
    public static bool IsRoleplay(string text, string[] words)
    {
        if (string.IsNullOrWhiteSpace(text) || words == null || words.Length == 0)
            return false;
        string trimmed = text.Trim();
        words = words.Select(word => word.Replace("'s", string.Empty).Replace("'", string.Empty)).ToArray();
        if (words.Any(word => word is "lol" or "u" or "ur" or "pls" or "plz" or "lfg" or "lfm" or "wts" or "wtb" or "inc" or "omg" or "brb"))
            return false;
        if (trimmed.StartsWith("/me ", StringComparison.OrdinalIgnoreCase) && words.Length >= 3 ||
            System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^\*[a-z][a-z' ]{3,}\*", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            return true;
        if (words.Any(ArchaicWords.Contains) || HasPhrase(words, "good sir", "good lady", "my lady", "my lord", "pray tell", "good morrow",
                "fare thee well", "by odin", "by the gods", "by arawn", "by lugh", "by the light", "have ye", "ye be", "ye ve", "ye have", "can ye",
                "be lookin", "art thou", "i seek", "i sense", "brothers and sisters", "we march", "watches over us", "the forest whispers",
                "dark magic", "rest by the fire", "another day another", "hail warrior", "hail traveler", "hail traveller", "hail stranger",
                "hail friend", "hail good"))
            return true;
        // "The Morrigan watches over us tonight.": a whole, formal sentence of six words or more, capitalised and stopped.
        return words.Length >= 6 && char.IsUpper(trimmed[0]) && (trimmed.EndsWith('.') || trimmed.EndsWith('!')) && !trimmed.EndsWith("..") &&
               HasPhrase(words, "the gods", "the realm", "our lands", "this place", "the shadows", "the night", "the darkness", "the forest", "my blade");
    }

    /// <summary>"thx mythic", "thanks a lot lag", "ty goa", "game crashed again, thanks": thanks that complain. Pure.</summary>
    public static bool IsSarcasticThanks(string[] words) =>
        words.Any(word => word is "mythic" or "goa" or "ea" or "lag" or "server" or "patch" or "isp" or "aol" or "comcast" or "crashed" or "crash" or   // chat-check: input
            "crashes" or "crashing" or "ld" or "linkdead" or "nerf" or "nerfed" or "bug" or "bugged" or "disconnected" or "dc");

    /// <summary>A mishap told for a laugh: a fall, a one-shot, a death by something grey or silly. Pure.</summary>
    public static bool IsMishap(string[] words) =>
        HasPhrase(words, "fell off", "i fell", "fell in the", "fell into", "fell down", "1 shotted", "one shotted", "1 shot by", "one shot by",
            "just dying over here", "dont mind me", "don t mind me", "drowned", "fell to my death", "ran into a wall", "aggroed the whole",
            "pulled the whole", "trained myself", "died to a grey", "killed by a grey", "died to a rabbit", "died to a rat", "jumped off") &&
        !HasPhrase(words, "someone fell", "he fell", "she fell", "they fell");

    /// <summary>
    /// A call for help the player makes now: under attack, out of power, lost, new and confused, a corpse run, a quest. Not
    /// "can you help me find a group" (a group request) or "how do i ..." (a question with an answer). Pure.
    /// </summary>
    public static bool AsksForHelp(string[] words, string text)
    {
        if (words.Length == 0 || HasPhrase(words, "how do i", "how do you", "where is", "where do") ||
            words.Any(word => word is "lag" or "server" or "laggy" or "lagging" or "boss" or "work"))
            return false;
        bool urgent = HasPhrase(words, "is killing me", "killing me", "getting hit", "getting killed", "need heals", "need a heal", "heal me",
            "heals here", "help me", "help please", "help pls", "help plz", "pls help", "please help", "someone help", "anyone help", "can someone help",
            "can anyone help", "need help", "need some help", "im lost", "i m lost", "i am lost", "very confused", "so confused", "first char",
            "first character", "new here", "corpse run", "help with my quest", "help with a quest", "help w my", "around to help", "need a hand",
            "oom and", "low hp", "im dying", "i m dying", "save me");
        return urgent || words[0] == "help" && words.Length <= 6;
    }

    /// <summary>"free buffs at the bindstone", "anyone want free sc": something offered for nothing (not "free time", "free for all").</summary>
    private static bool OffersFree(string[] words)
    {
        int at = Array.IndexOf(words, "free");
        return at >= 0 && at + 1 < words.Length && words[at + 1] is "buffs" or "buff" or "spellcraft" or "sc" or "pl" or "rez" or "rezzes" or
            "res" or "ports" or "port" or "speed" or "stuff" or "gear" or "items" or "armor" or "weapons" or "crafting" or "repairs" or "pots";
    }

    // Not "rez"/"res": "rez pls" is a rez request (its own intent) and "rez sickness sucks" a complaint.
    private static readonly HashSet<string> OrderVerbs = Set("pull", "heal", "buff", "wait", "stay", "follow", "come", "assist",
        "mez", "root", "taunt", "grab", "loot", "sit", "med", "stop", "run", "help", "kill", "attack", "target", "hold", "speed", "go");

    /// <summary>
    /// A trade reply that fits the post. Play test 2026-10-27: "wtb a better staff for a lvl 1 mage" drew "how much are you
    /// asking?", a reply to a seller. A buy post isn't answered as a sale; a sale isn't asked "wts or wtb?"; and no bot offers
    /// to craft (gamebots don't). Pure.
    /// </summary>
    public static bool TradeReplyFits(string incoming, string reply)
    {
        if (string.IsNullOrEmpty(reply))
            return true;
        string line = reply.ToLowerInvariant();
        if (line.Contains("need made") || line.Contains("can make") || line.Contains("craft it for"))
            return false;
        string text = " " + (incoming ?? string.Empty).ToLowerInvariant() + " ";
        bool buying = text.Contains(" wtb ") || text.Contains(" looking for ") || text.Contains(" anyone selling ") || text.Contains(" lf ") ||
                      text.Contains(" buying ");
        bool selling = text.Contains(" wts ") || text.Contains(" selling ") || text.Contains(" for sale ");
        if (buying && !selling)
            return !(line.Contains("asking") || line.Contains("stats") || line.Contains("vendor") || line.Contains("worth") ||
                     line.Contains("no coin") || line.Contains("wts or wtb") || line.Contains("post it"));
        if (selling && !buying)
            return !line.Contains("wts or wtb");
        return true;
    }

    /// <summary>An order: an imperative verb first, after a name or "ok" ("pull the next one", "heal me", "ok follow me"). Pure.</summary>
    public static bool IsOrder(string[] words)
    {
        if (words.Contains("or"))
            return false;
        // A bot's name before the order is taken out by the caller ("aldric pull the next one" is read as "pull the next one"), so a
        // first word that is not a verb is no order: "mobs pull hard here" (review 2026-10-26).
        int start = words.Length > 1 && words[0] is "ok" or "k" or "pls" or "please" or "plz" ? 1 : 0;
        if (start >= words.Length || !OrderVerbs.Contains(words[start]))
            return false;
        // "help me" alone is ask_help; "run!" is a warning; "go go" is cheering.
        return words.Length - start >= 2 && words[start] is not ("help" or "go");
    }

    /// <summary>The subject of a real-life remark ("late", "school", "food", "tech", "pet", "weather", "media", "work"), or null. Pure.</summary>
    public static string RealLife(string[] words)
    {
        string text = " " + string.Join(' ', words) + " ";
        bool Has(params string[] phrases) => phrases.Any(phrase => text.Contains(" " + phrase + " ", StringComparison.Ordinal));
        if (Has("3am", "4am", "2am", "5am", "so late", "cant sleep", "can't sleep", "should be asleep", "up late", "past my bedtime", "so tired",
                "exhausted") || System.Text.RegularExpressions.Regex.IsMatch(text, @" its \d{1,2} ?am "))
            return "late";
        if (Has("finals", "exam", "exams", "homework", "studying", "midterm", "midterms", "paper due", "have class", "class at", "school tomorrow"))
            return "school";
        if (Has("pizza", "chinese food", "hungry", "takeout", "dinner is", "making dinner", "eating dinner", "lunch break"))
            return "food";
        if (Has("dsl", "cable modem", "isp", "my internet", "internet keeps", "connection keeps", "geforce", "radeon", "video card",
                "graphics card", "new computer", "new pc", "pc crashed", "my pc", "computer crashed", "monitor died"))
            return "tech";
        if (Has("my cat", "my dog", "the cat", "the dog", "cat keeps", "dog keeps", "puppy", "kitten", "my cats", "my dogs"))
            return "pet";
        if (Has("snowing", "raining", "so hot here", "so cold here", "thunderstorm", "heat wave", "no ac", "freezing here", "power went out"))
            return "weather";
        if (Has("sopranos", "lord of the rings", "return of the king", "two towers", "the matrix", "movie", "on tv", "episode", "seen it yet",   // chat-check: input
                "the game last night", "watch the game", "the game tonight", "watching the game", "the playoffs", "the series"))
            return "media";
        if (Has("work tomorrow", "from work", "at work", "my boss", "overtime", "long day at work", "job interview", "work sucks", "got home from work",
                "all nighter", "taxes", "tax due", "bills due", "rent due"))
            return "work";
        // The holdout check: "my back is killing me from sitting so long" read as grinding.
        if (Has("my back", "back hurts", "back is killing", "my wrist", "wrist hurts", "my eyes hurt", "headache", "sitting so long",
                "need to stretch", "carpal tunnel", "my neck"))
            return "body";
        return null;
    }

    /// <summary>
    /// The level band a group or raid post asks for: "45+", "lvl 40 and up", "20-25", "need 45 or higher", "50s only", or null
    /// (Beta 1.1.2, round 3: a level 30 bot accepted a "45+" raid). Pure.
    /// </summary>
    public static (int Min, int Max)? LevelBand(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        string t = text.ToLowerInvariant();
        System.Text.RegularExpressions.Match range = System.Text.RegularExpressions.Regex.Match(t, @"\b(?:lvl |level |lv )?([1-4]?\d|50) ?(?:-|to) ?([1-4]?\d|50)\b(?! ?(?:%|g\b|p\b|s\b|min|mins|gold|plat))");
        if (range.Success && int.TryParse(range.Groups[1].Value, out int low) && int.TryParse(range.Groups[2].Value, out int high) &&
            low >= 5 && high <= 50 && low < high && high - low <= 15)
            return (low, high);
        System.Text.RegularExpressions.Match plus = System.Text.RegularExpressions.Regex.Match(t, @"\b(?:lvl |level |lv )?([1-4]\d|50) ?(?:\+|plus\b| and up\b| or higher\b| or above\b| or better\b|s only\b| only\b)");
        if (plus.Success && int.TryParse(plus.Groups[1].Value, out int min) && min >= 10)
            return (min, 50);
        return null;
    }

    /// <summary>The role a group asks for ("lfm vendo need healer pst" -> "healer"; "need tank and cc" -> "tank"), or null. Pure.</summary>
    public static string NeededRole(string text)
    {
        string[] words = AutonomousChatIntentModel.Words(text);
        // The role comes after what is asked for: "lfg 30 cleric, need a group" names the player's own class (review 2026-10-26).
        int asked = Array.FindIndex(words, word => word is "lfm" or "lf1m" or "lf2m" or "lf3m" or "need" or "needs" or "lf" or "looking");
        if (asked < 0)
            return null;
        words = words.Skip(asked + 1).ToArray();
        foreach ((string role, string[] forms) in new[]
                 {
                     ("healer", new[] { "healer", "healers", "heals", "heal", "cleric", "druid", "shaman" }),
                     ("tank", new[] { "tank", "tanks" }),
                     ("cc", new[] { "cc", "mezzer", "mez", "mezz" }),
                     ("puller", new[] { "puller", "pullers" }),
                     ("caster", new[] { "caster", "casters", "nuker", "nukers", "nukes" }),
                 })
            if (words.Any(forms.Contains))
                return role;
        return null;
    }

    /// <summary>
    /// A player's good news ("finally got my epic", "woot 35", "killed my first orange solo", "4 hours of crafting and i finally got
    /// a masterpiece", "finally rr3?"): grats. Pure.
    /// </summary>
    public static bool IsGoodNews(string[] words)
    {
        if (words.Length == 0)
            return false;
        // Beta 1.1.2, round 2: "50 cleric lfg rvr, rr4" is a group post, not news to cheer.
        if (words.Any(word => GroupWords.Contains(word) && word is not ("group" or "party")))
            return false;
        if (words[0] is "woot" or "w00t" or "yay" or "finally" && words.Length <= 4 && words.Skip(1).Any(word => word.Length > 0 &&
                (char.IsDigit(word[0]) || word.StartsWith("rr", StringComparison.Ordinal))))
            return true;
        // The holdout check: "rr3 baby", "finally hit 50 after 3 months", "just made legendary weaponcrafter woo".
        // "hit 50 omg", "just hit 3L1 woot": a level or rank reached, cheered.
        if (words.Length <= 6 && words.Any(word => System.Text.RegularExpressions.Regex.IsMatch(word, @"^\dl\d$")) &&
            words.Any(word => word is "hit" or "made" or "woot" or "w00t" or "finally" or "yay" or "omg" or "ding" or "got"))
            return true;
        if (words.Length <= 5 && words[0] is "hit" or "made" or "reached" && words.Length > 1 &&
            (int.TryParse(words[1], out int reached) && reached is >= 5 and <= 50 || words[1] == "fifty") &&
            words.Skip(2).All(word => word is "omg" or "woot" or "w00t" or "yay" or "finally" or "lol" or "yes" or "baby" or "at" or "last" or "!"))
            return true;
        if (words.Length <= 6 && words.Any(word => System.Text.RegularExpressions.Regex.IsMatch(word, @"^rr\d{1,2}$")) &&
            !words.Contains("what") && !words.Contains("whats"))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(string.Join(' ', words),
                @"\b(finally|just) (hit|made|got|reached) (\d{1,2}|fifty|rr\d+|level \d+|lvl \d+|legendary|grandmaster|master|my \w+)\b") ||
            HasPhrase(words, "made legendary", "hit legendary", "made grandmaster", "made master", "got legendary"))
            return true;
        // Beta 1.1.2 (blind round 1: read as banter or nothing): "my first solo kill in rvr!!", "got a 99 quality on the first try!!",
        // "i got my necro to 25!", "woot just got the named drop", "finally 50 after 4 months :D".
        // A sale or a price check of a 99% item is no news ("WTS 2h sword 99% qual, pst").
        if (words.Any(word => word is "wts" or "wtb" or "selling" or "sell" or "buying" or "pc" or "price" or "much" or "obo" or "pst"))
            return false;
        if (words.Length <= 6 && words[0] is "woot" or "w00t" or "yay" && words.Any(word => word is "pwned" or "owned" or "killed" or "beat" or "got" or "won"))
            return true;
        if (HasPhrase(words, "first realm rank", "first rr", "rr2 baby", "rr3 baby", "rr4 baby", "rr5 baby"))
            return true;
        if (System.Text.RegularExpressions.Regex.IsMatch(string.Join(' ', words),
                @"\b(got|get) (my|the) \w+ to (\d{1,2}|fifty)\b|\bfinally (\d{1,2}|fifty) after\b|\b(9[5-9]|100) (quality|qual|q)\b|\bgot (a|the) (named|rare) drop\b|\bjust got the \w+ drop\b"))
            return true;
        return HasPhrase(words, "my first solo kill", "first solo kill", "first rvr kill", "my first kill", "first deathblow", "got the named drop",
            "got my epic", "finished my epic", "finished the epic", "epic done", "epic is done", "done my epic",
            "my first orange", "my first red", "my first purple", "got a masterpiece", "got a mp", "finally got a mp", "finally got my",
            "just got my", "finally made", "made rr", "hit rr", "finally rr", "got the drop", "finally dropped", "first epic",
            "had a baby", "had our baby", "got married", "got the job", "new job", "passed my", "made good", "made bank", "good inc today");
    }

    private static readonly System.Text.RegularExpressions.Regex HuntWords = new(
        @" (lvl|lvls|lvling|lvl ing|level|levels|leveling|levelling|xp|exp|xping|exping|hunt|hunting|solo|soloing|grind|grinding|camp|camps|camping) ",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex HuntPlaceWords = new(@" (spot|spots|place|places|zone|zones|area|areas) ",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex HuntAsks = new(
        @" (where|wheres|best|good|any|anywhere|recommend|recommendations|suggest|suggestions|ideas) ", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    // "where should i go now", "where do i go after 33", "where to go at 20": with a level, the next hunt.
    private static readonly System.Text.RegularExpressions.Regex HuntGo = new(
        @" where (should|do|can|would|shall) (i|we|a|an|\d{1,2}s?) (\w+ )?(go|head|move) | where (to )?(go|head) (now|next|after|at|for) ",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    private static readonly System.Text.RegularExpressions.Regex HuntLevel = new(@" \d{1,2}s? ", System.Text.RegularExpressions.RegexOptions.CultureInvariant);
    // What a "best/good/where ... level" line is about when it isn't a hunt: crafting, trading, a trainer, a class or realm to
    // pick, a group post, how leveling works.
    private static readonly System.Text.RegularExpressions.Regex HuntNot = new(
        @" (craft\w*|tradeskill\w*|spellcraft\w*|weaponcraft\w*|armorcraft\w*|tailor\w*|fletch\w*|alchemy|siegecraft\w*|trainer|trainers|train|buy|sell|selling|buying|merchant|vendor|bind|stable|horse|teleport|porter|class|classes|race|races|realm|realms|spec|specs|alt|alts|toon|char|character|weapon|armor|armour|lfg|lfm|lf|group|groups|grp|grps|luck|grats|gz|night|job|music|song|songs|tunes|how long|cap|ding|dinged|range) ",   // chat-check: input
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// A where-to-level question, read as a pattern (1.2.2 chat audit, C4, fix F3): a hunting word ("lvl", "level", "xp", "exp",
    /// "hunt", "solo", "camp") or a place word with a level ("good spot for 24"), with an asking word ("where", "best", "good",
    /// "any"); or "where should i go now" / "where do i go after 33" with a level. "best xp for 21?", "where do 24s hunt", "lvl
    /// 18 where to xp?", "15->20?" ("15 20" once normalized) all count; "what lvl is gotar?", "where is the cleric trainer",
    /// "best class to level", "good luck leveling", "lfg 24 any good xp" and "cleric or friar?" don't. Pure.
    /// </summary>
    public static bool AsksWhereToLevel(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || IsEitherOr(text))
            return false;
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        if (HuntNot.IsMatch(t))
            return false;
        // A question: a "?", a question opening, or a line that opens with an asking word ("best xp for 21", "good camp for 30").
        string first = t.Trim().Split(' ')[0];
        bool asking = text.Contains('?') || AutonomousChatQuestions.IsQuestion(text) ||
                      first is "where" or "wheres" or "best" or "good" or "any" or "anyone" or "anybody" or "any1" or "lvl" or "level" or "im";
        if (!asking)
            return false;
        bool level = HuntLevel.IsMatch(t);
        if (HuntGo.IsMatch(t))
            return level;
        if (!HuntAsks.IsMatch(t))
            return false;
        return HuntWords.IsMatch(t) || level && HuntPlaceWords.IsMatch(t) ||
               // "where at 15->20?" ("15 20" once normalized): a level band.
               System.Text.RegularExpressions.Regex.IsMatch(t, @" \d{1,2} (to )?\d{1,2} ") ||
               // "good camp for 30?", "any good spots for 12?"
               System.Text.RegularExpressions.Regex.IsMatch(t, @" (good|best) (camp|camps|spot|spots|place|places|zone|xp|exp) ");
    }

    /// <summary>The realm a line names as people or by name ("where do mids level at 20", "albion"), or None. "mid" alone is
    /// left out ("mid 20s" is a level). Pure.</summary>
    public static eRealm RealmNamed(string text)
    {
        string t = " " + AutonomousChatQuestions.Normalize(text) + " ";
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (albs|alb|albion|albions) "))
            return eRealm.Albion;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (mids|midgard|midgards) "))
            return eRealm.Midgard;
        if (System.Text.RegularExpressions.Regex.IsMatch(t, @" (hibs|hib|hibernia|hibernias) "))
            return eRealm.Hibernia;
        return eRealm.None;
    }

    /// <summary>A where-to-level question that asks for a solo spot ("where can a 15 minstrel solo?"). Pure.</summary>
    public static bool AsksSolo(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(" " + AutonomousChatQuestions.Normalize(text) + " ", @" (solo|soloing|soloable|alone|by myself) ");

    /// <summary>"cleric or friar?", "polearm or two handed?": a question between two things. Not "is it just me or...",
    /// "or am i doing it wrong", "or what" (chat audit 2026-10-21: "is the enchanter pet worth it or just a buffbot?" drew
    /// "second one imo").</summary>
    public static bool IsEitherOr(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;
        string[] words = AutonomousChatIntentModel.Words(text).Select(word => word.Replace("'", string.Empty)).ToArray();
        // "necro or cab for pve", "pole or sword on my armsman", "thid or keep leveling pve": players often leave out the "?"
        // (the audit: these drew class remarks). Without one, only a short line that isn't about the speaker ("i or...").
        // The holdout check: "tir na nog or mag mell first for a new toon", "rejuv or smite for a cleric, what would u do": the "or"
        // early, or the line asks what to pick.
        int or = Array.IndexOf(words, "or");
        bool asks = HasPhrase(words, "what would u", "what would you", "which would", "which is better", "what do u think", "idk", "help me pick",
            "help me choose", "cant decide", "can t decide");
        if (!text.Contains('?') && (words.Length > (or is >= 0 and <= 5 || asks ? 13 : 9) || words.Length == 0 || words[0] is "i" or "im" or "we" or "it" or "its" or "thats" or
                "either" or "neither" or "whether" or "and" or "but"))
            return false;
        // "are you real or scripted" asks whether the bot is a bot.
        if (words.Any(BotWords.Contains) || HasPhrase(words, "or real", "or human", "or a person", "or a real", "real or", "human or"))
            return false;
        return words.Contains("or") && !HasPhrase(words, "or something", "or anything", "or what", "or so", "or not", "or is it just me",
            "or is that just me", "or just me", "or is it me", "or am i", "or is that a myth", "or is that true", "or just a", "or no",
            "or nah", "or is it", "or is that", "or a bot", "or bot", "or an npc", "or a waste", "or waste", "or real");
    }

    /// <summary>"really?", "what?", "why?", "srsly": a two-word follow-up to something else that was said, which only the
    /// one it was said to can answer.</summary>
    public static bool IsFollowUp(string text)
    {
        string[] words = AutonomousChatIntentModel.Words(text);
        return words.Length is > 0 and <= 2 && words.All(word => word is "really" or "what" or "why" or "how" or "huh" or "srsly" or
            "seriously" or "for" or "real" or "wait" or "who" or "when" or "where" or "which" or "rly" or "wut" or "wat" or "eh" or "sorry");
    }

    private static readonly HashSet<string> AckWords = Set("yes", "yeah", "yea", "ya", "yep", "yup", "no", "nope", "nah", "ok", "okay",
        "k", "kk", "sure", "cool", "nice", "same", "true", "right", "agreed", "agree", "maybe", "alright", "aight", "fair", "indeed",
        "exactly", "word", "mhm", "hm", "hmm", "oh", "ah", "ahh", "see", "me", "too", "got", "gotcha", "understood", "noted", "makes",
        "sense", "sounds", "good", "fine", "neat", "great", "perfect", "def", "definitely", "probably", "prolly", "guess", "suppose",
        "same", "here", "true", "ikr", "totally", "either", "neither", "thats", "that's", "is", "was", "really", "yeh", "ye");   // chat-check: input

    /// <summary>"ok", "sure", "nice", "me too", "makes sense": a line that only acknowledges what was said.</summary>
    public static bool IsAcknowledgement(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('?'))
            return false;
        string[] words = AutonomousChatIntentModel.Words(text);
        // "see ya", "see you all": a goodbye, not an acknowledgement (1.2 chat audit, DESIGN-047).
        if (HasPhrase(words, "see ya", "see you", "see u", "see yas"))
            return false;
        return words.Length is > 0 and <= 5 && Only(words, AckWords) && !words.All(word => word is "i" or "me" or "is" or "was" or "here" or "see");
    }

    private static readonly System.Text.RegularExpressions.Regex PlacePattern = new(
        @"^(?:is |are )?(?:there )?(?:anyone|anybody|any1|someone|somebody|any (?:albs?|mids?|hibs?|players?|people|one|body))(?: still)? (?:in|at|near|around|by|over in) (?:the )?([a-z' ]{3,40}?)\s*\??$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The place in "anyone in camelot?", "any albs near cotswold?", or null.</summary>
    public static string PlaceAsked(string text)
    {
        string t = string.Join(' ', AutonomousChatIntentModel.Words(text));
        var match = PlacePattern.Match(t + (text?.TrimEnd().EndsWith('?') == true ? "?" : ""));
        if (!match.Success)
            return null;
        string place = match.Groups[1].Value.Trim();
        // "is anyone in this guild an actual person" is not about a place (the audit).
        if (place.StartsWith("this ", StringComparison.Ordinal) || place.StartsWith("my ", StringComparison.Ordinal) ||
            place.StartsWith("our ", StringComparison.Ordinal) || place.StartsWith("your ", StringComparison.Ordinal) ||
            place.StartsWith("ur ", StringComparison.Ordinal) || place.Contains("guild") || place.Contains("group"))
            return null;
        return place is "a group" or "group" or "a guild" or "guild" or "game" or "the game" or "here" or "town" || place.Length < 3 ? null : place;
    }

    /// <summary>
    /// False for a reply that asks what the line already said: "what lvl?" to "paladin 6 here" (live test 2026-10-18).
    /// </summary>
    public static bool ReplyFits(string incoming, string reply)
    {
        if (string.IsNullOrEmpty(incoming) || string.IsNullOrEmpty(reply))
            return true;
        string[] words = AutonomousChatIntentModel.Words(reply);
        bool asksLevel = HasPhrase(words, "what lvl", "what level", "level range", "what level are you", "what lvl are you", "how high are you");
        if (asksLevel && incoming.Any(char.IsDigit))
            return false;
        // "lfg 50 reaver will travel" drew "what class are u?".
        bool asksClass = HasPhrase(words, "what class", "which class", "what are you playing", "what r u playing");
        if (asksClass && NamesAClass(incoming))
            return false;
        // "lfg" is a player looking for a group, not one with a group (in-game check 2026-10-18: "lfg 50 cleric, in camelot"
        // drew "how many do u have?"). Lines that assume the asker leads a group fit "lfm", not "lfg".
        string[] said = AutonomousChatIntentModel.Words(incoming);
        bool seeksGroup = (HasPhrase(said, "lfg", "looking for group", "looking for a group", "looking for grp", "looking for a grp", "lf grp",
                              "any grps", "any groups") || said.Contains("lf")) &&
                          !HasPhrase(said, "lfm", "looking for more", "lf1m", "lf2m", "lf3m");
        // A player forming a group ("lfm vendo need healer pst") is asked about the group, not "what level and class?" or told
        // "i could use a group too" (the talking update audit).
        bool forming = HasPhrase(said, "lfm", "lf1m", "lf2m", "lf3m", "lf4m", "looking for more", "need a healer", "need healer",
            "need a tank", "need tank");
        if (forming && HasPhrase(words, "what level and class", "what class are you", "what class r u", "i could use a group too",
                "solo for now", "lf a group too", "keep at it", "someone will bite"))
            return false;
        bool assumesGroup = HasPhrase(words, "how many do you have", "how many do u have", "need a healer still", "need a tank or",
            "need a healer or", "what do you need", "what do u need", "i can fill", "fill a spot",
            "still need someone", "if you still need someone", "how many u got", "how many are u", "how many are you", "fill a spot",
            "if u inv", "if you inv", "pst me when u form", "pst me when you form", "when u form up", "when you form up");
        if (seeksGroup && assumesGroup)
            return false;
        // Chat audit 2026-10-21: "two handed or sword and board?" drew "imo yes"; "anyone selling a staff?" drew "i would just
        // vendor it"; "how much is it worth?" drew "how much?".
        string replyText = string.Join(' ', words);
        bool eitherOr = IsEitherOr(incoming);
        if (eitherOr && words.Length <= 4 && HasPhrase(words, "imo yes", "imo no", "yes do it", "i would", "i wouldnt", "go for it",
                "worth it imo", "not worth it", "yes", "no", "do it"))
            return false;
        bool buying = HasPhrase(said, "wtb", "anyone selling", "anybody selling", "want to buy", "looking to buy", "looking for a", "need a",
            "where can i buy", "where do i buy", "anyone have a", "anyone got a");
        if (buying && HasPhrase(words, "vendor it", "worth something", "what its worth", "pst me the stats", "gl selling", "gl with the sale",
                "is it still up", "what are you asking", "no idea what its worth", "someone will want it", "hold onto it", "post it on",
                "upgrade for an alt", "list it", "sell it", "who is buying", "whos buying", "merchants pay half"))
            return false;
        if (HasPhrase(words, "wts or wtb") && (said.Any(word => word is "wts" or "wtb" or "selling" or "buying" or "sell" or "buy")))
            return false;
        // "wts dyes cheap pst" drew "i would just vendor it" (the audit): advice for someone holding an item, not a seller.
        bool selling = HasPhrase(said, "wts", "selling", "for sale", "want to sell", "looking to sell") &&
                       !HasPhrase(said, "anyone selling", "anybody selling", "who is selling", "whos selling", "any1 selling", "u selling", "you selling");
        if (selling && HasPhrase(words, "vendor it", "sell it", "list it", "post it on", "hold onto it", "worth something", "someone will want it",
                "no idea what its worth", "upgrade for an alt", "not my class", "no clue on pc", "depends who is buying", "check the exchange",
                "a crafter can"))
            return false;
        // 1.2 chat audit (CORPUS-C-053): an alarm that names its place, count or classes is not asked them back ("where?", "how
        // many?", "what classes?" to "inc 8 mids at bled, 2 sbs").
        if (AutonomousChatReplyBudget.IsAlarm(incoming, eAutonomousChatIntent.Unknown) || AutonomousChatIntentModel.Predict(incoming) == eAutonomousChatIntent.Alarm)
        {
            if (HasPhrase(words, "where", "where at", "which keep", "which way", "where exactly", "where are they", "wheres that") && AutonomousChatContext.CallNamesAPlace(incoming))
                return false;
            if (HasPhrase(words, "how many", "how big", "how many of them") && AutonomousChatContext.CallSaysHowMany(incoming))
                return false;
            if (HasPhrase(words, "what classes", "what class", "which classes", "what are they", "what kind") && NamesAClass(incoming))
                return false;
        }
        bool asksPrice = HasPhrase(said, "how much", "price check", "pc on") || said.Contains("worth");
        if (asksPrice && (HasPhrase(words, "how much", "whats the price", "what are you asking", "what price") || replyText == "price"))
            return false;
        return true;
    }

    public static bool HasPhrase(string[] words, params string[] phrases)
    {
        string text = " " + string.Join(' ', words) + " ";
        return phrases.Any(phrase => text.Contains(" " + phrase + " ", StringComparison.Ordinal));
    }

    private static bool Any(string[] words, HashSet<string> set) => words.Any(set.Contains);

    private static bool Only(string[] words, params HashSet<string>[] sets) =>
        words.Any(word => sets.Any(set => set.Contains(word))) && words.All(word => sets.Any(set => set.Contains(word)) || IsNoise(word));

    /// <summary>Filler a short message can carry without changing what it is ("ty all", "lol ok", "back now").</summary>
    private static bool IsNoise(string word) => word is "all" or "u" or "you" or "ya" or "guys" or "man" or "dude" or "much" or
        "so" or "very" or "again" or "now" or "ok" or "k" or "kk" or "np" or "and" or "everyone" or "for" or "the" or "a" or
        "that" or "it" or "im" or "i'm" or "i" or "am" or "all" or "m8" or "mate" or "bud" or "buddy" or "friend" or "sir" or
        "bro" or "too" or "lol" or "heh" or "haha" or ":)" or "x" or "xx" or "o";

    /// <summary>The index of the first word in <paramref name="set"/>, or -1.</summary>
    private static int IndexIn(string[] words, HashSet<string> set) => Array.FindIndex(words, set.Contains);

    /// <summary>The word index where the first of the phrases starts, or -1.</summary>
    internal static int PhraseIndex(string[] words, params string[] phrases)
    {
        int best = -1;
        foreach (string phrase in phrases)
        {
            string[] parts = phrase.Split(' ');
            for (int i = 0; i + parts.Length <= words.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < parts.Length && match; j++)
                    match = words[i + j] == parts[j];
                if (match)
                {
                    if (best < 0 || i < best)
                        best = i;
                    break;
                }
            }
        }
        return best;
    }

    private static readonly HashSet<string> EnemyPeople = Set("albs", "mids", "hibs", "alb", "mid", "hib", "enemies", "enemy", "they", "zerg",
        "zergs", "stealthers", "assassins");

    /// <summary>Keep names and the short forms players typed ("beno", "berk", "crim"), for "they are hitting bold".</summary>
    private static readonly HashSet<string> KeepWords = Set("benowyc", "beno", "berkstead", "berk", "boldiam", "bold", "bledmeer", "bled",
        "crauchon", "crau", "crimthainn", "crim", "nottmoor", "nott", "glenlock", "glen", "renaris", "erasleigh", "sursbrooke", "hurbury",
        "blendrake", "arvakr", "fensalir", "hlidskialf", "ailline", "behnn", "scathaig", "bolg", "keep", "relic", "gate", "door", "lord", "walls",
        "wall", "milegate", "mg", "amg", "mmg", "hmg", "pk", "dock", "bridge");

    /// <summary>
    /// 1.2 chat audit (DESIGN-009): plain calls without "inc": "4 mids on the bridge", "hibs everywhere at dc", "mids taking berk",
    /// "rams on the door at crim", "they are hitting bold", "relic keep is being hit", "sb just jumped me at the dock", "big group
    /// of hibs heading to emain", "they are in the keep". Pure.
    /// </summary>
    private static bool EnemyCall(string[] words)
    {
        string line = string.Join(' ', words);
        if (System.Text.RegularExpressions.Regex.IsMatch(line,
                @"\b\d+\+? ?(albs?|mids?|hibs?|enemies|stealthers|assassins) (on|at|by|near|in|inside|outside|heading|coming|going|moving|running|hitting|taking)\b|" +
                @"\b(albs|mids|hibs|enemies|zerg) (are )?(taking|hitting|inside|everywhere|sieging|attacking|swarming|camping|all over|in our|at our|on our)\b|" +
                @"\b(albs|mids|hibs|enemies|zerg|enemy) (are )?(heading|moving|coming|going|running|pushing) (to|for|towards|toward|into|at|on)\b|" +
                @"\brams? (on|at|up|hitting)\b|\bbeing (hit|attacked|sieged|rammed|taken)\b|\bjumped me\b|" +
                @"\b(they|theyre|they re) (are )?(in|inside|at|on|hitting|taking|sieging|attacking) (the |our )?"))
        {
            // "they are in the keep", "they are hitting bold": only about a keep, a gate, a relic or the like.
            if (System.Text.RegularExpressions.Regex.IsMatch(line, @"^(they|theyre|they re) ") && !words.Any(KeepWords.Contains))
                return false;
            // "jumped me" from an assassin or a stealther only ("the cat jumped me" is no call).
            if (line.Contains("jumped me") && !words.Any(word => word is "sb" or "ns" or "infil" or "infiltrator" or "nightshade" or "shadowblade" or
                    "assassin" or "stealther" or "stealthers" or "albs" or "mids" or "hibs" or "alb" or "mid" or "hib" or "zerg" or "scout" or "hunter" or "ranger"))
                return false;
            return true;
        }
        return words.Any(word => word is "hitting" or "attacking" or "sieging" or "taking") && words.Any(KeepWords.Contains) &&
               words.Any(EnemyPeople.Contains);
    }

    private static readonly HashSet<string> ThirdPersons = Set("he", "she", "they", "hes", "shes", "theyre", "him", "nobody", "noone");

    /// <summary>Words a question starts with when it has no "?" ("who is afk", "when do you ding").</summary>
    private static readonly HashSet<string> QuestionStarts = Set("who", "whos", "when", "where", "wheres", "is", "did", "does", "do", "was",
        "were", "will", "why", "what", "how");

    private static readonly HashSet<string> AskWords = Set("when", "who", "whos", "what", "whats", "where", "wheres", "how", "hows", "did", "does",
        "do", "is", "was", "were", "why", "will", "any");

    /// <summary>
    /// 1.2 chat audit (DESIGN-010): the social word at <paramref name="at"/> is talked about, not done: negated just before it
    /// ("no ding yet", "dont rez me", "dont say gz yet", "nobody say gn"), short of it ("almost ding", "3 bubs from ding"), said
    /// of someone else ("he dinged, not me", "he is afk", "did he say thanks?"), or the thing a question asks about ("when do you
    /// ding", "who is afk", "is the inc over"). <paramref name="ask"/> are the question words that make it the object of a
    /// question; null for the default set. Pure.
    /// </summary>
    internal static bool AboutTheAct(string[] words, int at, bool question, HashSet<string> ask = null)
    {
        if (at < 0)
            return false;
        for (int j = Math.Max(0, at - 3); j < at; j++)
            if (AutonomousChatIntentModel.IsNegation(words[j]) || j == at - 1 && words[j] is "almost" or "nearly" or "from" or "till" or "until" or "before" or "til")
                return true;
        for (int j = 0; j < at; j++)
            if (ThirdPersons.Contains(words[j]))
                return true;
        // "what a group, ty all", "how nice, thanks": an exclamation, no question.
        bool asked = question || words.Length > 1 && QuestionStarts.Contains(words[0]) && (ask ?? AskWords).Contains(words[0]) &&
                     !(words[0] is "what" or "how" && words[1] is "a" or "an" or "nice" or "cool" or "sweet" or "great" or "kind");
        if (asked)
            for (int j = 0; j < at; j++)
                if ((ask ?? AskWords).Contains(words[j]))
                    return true;
        return false;
    }

    private static readonly HashSet<string> NoAskWords = Set();
    private static readonly HashSet<string> RezAskWords = Set("when", "what", "whats", "how", "why", "does", "did");
    private static readonly HashSet<string> AlarmAskWords = Set("is", "was", "were", "where", "wheres", "when", "did", "any", "how", "what", "whats");

    /// <summary>"im out" is a goodbye at the end or before a comma, never "im out of power" (DESIGN-010). Pure.</summary>
    private static bool SaysImOut(string text) =>
        System.Text.RegularExpressions.Regex.IsMatch(text ?? string.Empty, @"\bi'?\s?m out\b(?!\s+(of|for|on|in|at|with|to|here|there)\b)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// A goodbye word that is no goodbye (DESIGN-010): "not going to bed yet", "nobody say gn yet", "good night for a keep raid",
    /// "bye bye strength relic". Pure.
    /// </summary>
    private static bool FarewellTalkedAbout(string[] words, bool question)
    {
        int at = PhraseIndex(words, "going to bed", "off to bed", "heading to bed", "gotta go", "got to go", "logging off", "log off", "good night");
        if (at < 0)
            at = IndexIn(words, FarewellWords);
        if (AboutTheAct(words, at, question))
            return true;
        if (HasPhrase(words, "good night for", "good night to", "good night of", "a good night", "good night tonight"))
            return true;
        return words.Length >= 3 && words[0] is "bye" && words[1] is "bye" && !GreetingObjects.Contains(words[2]) && !IsNoise(words[2]);
    }

    /// <summary>
    /// What a short or common chat line is, or false to let the classifier decide. <paramref name="words"/>
    /// are the message's lower-case words (single letters and digits kept).
    /// </summary>
    public static bool TryRead(string text, string[] words, out eAutonomousChatIntent intent)
    {
        intent = eAutonomousChatIntent.Unknown;
        if (words == null || words.Length == 0)
        {
            // ":)" ";p" "o/" and the like
            string raw = (text ?? string.Empty).Trim();
            if (raw is "o/" or "\\o" or "o7")   // chat-check: input
            {
                intent = eAutonomousChatIntent.Greeting;
                return true;
            }
            if (raw.Length > 0 && raw.Length <= 4 && !raw.All(c => c == '?') &&
                raw.All(c => !char.IsLetterOrDigit(c) || c is 'p' or 'P' or 'D' or 'o' or 'O'))
            {
                intent = eAutonomousChatIntent.Laugh;
                return true;
            }
            return false;
        }
        bool question = (text ?? string.Empty).Contains('?');
        int n = words.Length;
        // "o/ evening", "\\o all": a wave, then the greeting.
        string wave = (text ?? string.Empty).TrimStart();
        if (!question && n <= 4 && (wave.StartsWith("o/", StringComparison.Ordinal) || wave.StartsWith("\\o", StringComparison.Ordinal)))   // chat-check: input
            return Is(eAutonomousChatIntent.Greeting, out intent);

        // "are you a bot?", "r u real?", "is this an npc"
        if (Any(words, BotWords) && (HasPhrase(words, "are you", "r u", "are u", "you a", "u a", "is this", "you an", "u an", "you're a",
                "youre a", "ur a", "is he", "is she", "are they", "are all", "are these", "like a", "type like") || n <= 3 && question) ||
            HasPhrase(words, "are you real", "r u real", "are u real", "are you human", "r u human", "are u human", "real person",
                "you even real", "u even real", "you a real", "u a real", "even human", "all bots", "you bots", "u bots", "ur bots",
                "real player", "real people", "actual person", "actual people", "actual player", "u human", "you human",
                "are you macroing", "you macroing", "u macroing", "are you botting", "u botting", "you botting", "actually a person",
                "actually human", "actually real", "a real human", "real or scripted", "real or a bot", "a person or", "human or",
                // 1.2 chat audit (DESIGN-046): "am i talking to a computer", "are you automated", "anyone here human".
                "talking to a computer", "talking to a bot", "talking to bots", "talking to npcs", "talking to a program", "are you automated",
                "u automated", "you automated", "anyone here human", "anyone human", "anybody human", "controlled by the server", "run by the server",
                "you are a bot", "u are a bot", "is that guy a bot", "that guy a bot", "is he a bot", "is she a bot"))
            return Is(eAutonomousChatIntent.BotCheck, out intent);

        // "thanks for the train buddy": sarcasm after a pull gone wrong, not thanks (the talking update audit).
        if (HasPhrase(words, "thanks for the train", "thx for the train", "ty for the train", "thanks for the adds", "thx for the adds",
                "thanks for the aggro", "thanks a lot for the train"))
            return Is(eAutonomousChatIntent.Laugh, out intent);
        bool thanks = Any(words, ThanksWords) || HasPhrase(words, "thank you", "much appreciated", "appreciate it", "appreciate the",
            "appreciate that", "appreciate you", "appreciated", "owe you one", "owe u one");
        // 1.2 chat audit (DESIGN-010): "no thanks", "dont thank me yet", "did he say thanks?", "ty but no" are no thanks to answer.
        if (thanks && (AboutTheAct(words, IndexIn(words, ThanksWords) is >= 0 and var thankAt ? thankAt : PhraseIndex(words, "thank you", "appreciate"), false, NoAskWords) ||
                       HasPhrase(words, "but no", "but nah", "but im good", "but i m good", "but no thanks")))
            thanks = false;
        // "ty for the rez" is thanks; "rez pls", "need a rez", "anyone rez?", "rez pls ty" ask for one. "heat res gear" is a resist.
        bool sickness = words.Contains("sick") || words.Contains("sickness");
        // 1.2 chat audit (DESIGN-010): "dont rez me", "no rez needed", "what level do clerics get rez" ask for no rez.
        bool rezWord = Any(words, RezWords) && !IsResist(words) && !AboutTheAct(words, IndexIn(words, RezWords), question, RezAskWords) &&
                       !HasPhrase(words, "rez needed", "need no rez");
        bool asking = question || HasPhrase(words, "need a", "need rez", "can someone", "can anyone", "anyone", "pls", "please", "plz",
            "rez me", "res me", "rez pls", "rezz me", "any healer", "any cleric", "any druid", "any shaman", "any friar", "any rezzer",
            "for a rez", "for rez", "need rez", "need res");
        if (rezWord && !sickness && (asking || !thanks && n <= 6))
            return Is(eAutonomousChatIntent.Rez, out intent);
        // "any healer around? died by the gate", "died in the lake, any druid nearby" ask for a rez without the word.
        if (!thanks && words.Any(word => word is "died" or "dead" or "down") && !AboutTheAct(words, Array.FindIndex(words, word => word is "died" or "dead" or "down"), question, RezAskWords) &&
            HasPhrase(words, "help", "help me", "pls", "plz", "any healer", "any healers", "any druid",
                "any cleric", "any shaman", "any friar", "any rezzer", "anyone near", "anyone around", "anybody near", "any1 near", "nearby",
                "anyone close", "can someone", "can anyone"))
            return Is(eAutonomousChatIntent.Rez, out intent);
        // "gotta log ty for the grp all": a goodbye with thanks in it is a goodbye (the holdout check). "im out, ty all", "i have to
        // go guys, sorry. thx for the group" (blind round 1).
        if (thanks && !question && !HasPhrase(words, "back to", "run back", "go back", "run to") && HasPhrase(words, "gotta log", "got to log", "gotta go", "got to go", "logging off", "logging out", "gotta run",
                "heading to bed", "off to bed", "going to bed", "calling it", "night all", "gn all", "gnight", "have to go",
                "need to go", "heading out", "gotta bounce", "have to log", "need to log", "time to go", "i have to go") || thanks && !question && SaysImOut(text))
            return Is(eAutonomousChatIntent.Farewell, out intent);
        // "thanks! how many are there, should I bring my cleric?": thanks with a real question after it asks about what the bot
        // just said; "anytime" ignored it and a guess read it as class talk (in-game check 2026-10-27). It is left unread, so a
        // question gets an honest answer or a "not sure". A short "ty, any more?" stays thanks.
        if (thanks && question && words.Count(word => !ThanksWords.Contains(word)) >= 5)
            return Is(eAutonomousChatIntent.Unknown, out intent);
        // "game crashed 3 times today, thx mythic", "thanks lag": sarcasm, a complaint (blind round 2: read as thanks).
        if (thanks && IsSarcasticThanks(words))
            return Is(eAutonomousChatIntent.Banter, out intent);
        if (thanks)
            return Is(eAutonomousChatIntent.Thanks, out intent);

        // "gz on the ding" and "nice ding" congratulate someone; "ding!", "ding 20", "finally 35" announce one's own.
        bool praise = Any(words, CongratsWords) || words[0] is "nice" or "gj" or "wtg" or "woot";
        // 1.2 chat audit (DESIGN-010): "no gz for me?", "dont say gz yet" are no grats; "gj on the kill" is.
        if (praise && (Any(words, CongratsWords) || words.Contains("ding")) && !AboutTheAct(words, IndexIn(words, CongratsWords), question, RezAskWords))
            return Is(eAutonomousChatIntent.Congrats, out intent);
        if (words[0] is "gj" or "wtg" && n <= 6 && !question)
            return Is(eAutonomousChatIntent.Congrats, out intent);
        // "3 bubs from ding", "almost ding", "no ding yet", "when do you ding", "he dinged, not me" announce no level (DESIGN-010).
        int dingAt = Array.FindIndex(words, word => word is "ding" or "dinged" or "leveled" or "levelled");
        bool dingTalked = AboutTheAct(words, dingAt, question) || HasPhrase(words, "from ding", "to ding", "till ding", "until ding", "before ding", "ding yet");
        if (!dingTalked && (words.Contains("ding") && !HasPhrase(words, "ding dong") || words.Contains("dinged") || HasPhrase(words, "just dinged", "hit level", "made level") ||
            !question && HasPhrase(words, "i leveled", "i levelled", "leveled up", "levelled up", "new level", "got my level", "level up") ||
            !question && System.Text.RegularExpressions.Regex.IsMatch(string.Join(' ', words), @"^(just turned|finally) (\d{1,2})\b|^(level|lvl) \d{1,2} (at last|finally|woot|yay)\b") ||
            !question && System.Text.RegularExpressions.Regex.IsMatch(string.Join(' ', words), @"\b(finally|just) (hit|reached|made) (\d{1,2}|fifty)\b") ||
            words.Contains("finally") && words.Any(word => int.TryParse(word, out int level) && level is >= 2 and <= 50) &&
            (n <= 3 || words.Contains("level") || words.Contains("lvl"))))
            return Is(eAutonomousChatIntent.Ding, out intent);

        // "free rps at mmg, mids are afk": a rally, not an away (Beta 1.1.2, round 2).
        if (HasPhrase(words, "free rps", "free rp", "free realm points", "free kills", "easy rps", "come get rps"))
            return Is(eAutonomousChatIntent.RvR, out intent);
        // "inventory full, gonna run to the merchant brb": a brb at the end of a longer line is still a brb (the audit).
        // 1.2 chat audit (DESIGN-010): "he is afk", "dont go afk", "who is afk" are about someone else; "back from afk" is a return.
        if ((n <= 6 && HasPhrase(words, "be right back", "be back in", "one sec", "1 sec", "afk for", "hang on", "hold on", "doorbell", "door bell",
                 "phone ringing", "phone call", "gotta get the door", "gotta answer", "gimme a minute", "give me a minute", "gimme a min", "give me a min",
                 "gimme 5", "give me 5") ||
             Any(words, AwayWords) && !question && !AboutTheAct(words, IndexIn(words, AwayWords), question)) && words[^1] != "back" &&
            words[0] is not ("back" or "bk") && !HasPhrase(words, "im back", "i'm back", "back now", "back from afk") ||
            // "back in 5": gone, back in five minutes.
            n <= 4 && n > 2 && words[0] == "back" && words[1] == "in" && words[2].Length > 0 && char.IsDigit(words[2][0]) && !question)
            return Is(eAutonomousChatIntent.Away, out intent);
        // "back from dinner", "back sorry phone call" (the audit); not "back to camp", "back in town".
        if (n <= 3 && Only(words, BackWords) || n <= 6 && (HasPhrase(words, "im back", "i'm back", "back now", "back again") ||
                words[0] is "back" or "bk" or "bak" && (n <= 2 || n <= 6 && !question && words[1] is "from" or "sorry" or "sry" or "all" or "guys" or "finally" or "again" or "at" or "where") ||
                // 1.2 chat audit (DESIGN-047): "alright back", "back at keyboard", "back, where were we", "im here again".
                n <= 6 && words[0] is "back" && words[1] is "where" ||
                HasPhrase(words, "im here again", "here again", "back at keyboard", "back at the keyboard", "back at the pc") ||
                // "back from vacation guys miss me?" (the holdout check): a question can follow.
                n <= 8 && words[0] is "back" or "bk" && words[1] == "from" ||
                n > 1 && words[0] is "ok" or "k" or "and" or "aaand" or "alright" or "aight" or "right" && words[1] == "back"))
            return Is(eAutonomousChatIntent.Back, out intent);
        if (words[0] == "wb" && n <= 3)
            return Is(eAutonomousChatIntent.Greeting, out intent);
        // Beta 1.1.2, the period's short words: "lo", "lo all" (hello), "re", "re all, isp died again", "re. kids are in bed
        // finally" (back), "sry was afk, back" (blind round 1: all read as nothing).
        if (!question && words[0] == "lo" && words.Skip(1).All(word => GreetingObjects.Contains(word) || IsNoise(word)))
            return Is(eAutonomousChatIntent.Greeting, out intent);
        if (!question && words[0] is "re" or "rehi" && n <= 8 && !HasPhrase(words, "re rolled", "re roll", "re spec", "re do"))
            return Is(eAutonomousChatIntent.Back, out intent);
        if (!question && n <= 6 && words[^1] == "back" && words.Any(word => word is "afk" or "was" or "sry" or "sorry" or "ok" or "and"))
            return Is(eAutonomousChatIntent.Back, out intent);

        // "morning guild, coffee in one hand...", "heya everyone long time no see": a greeting that goes on.
        if (!question && n >= 3 && GreetingWords.Contains(words[0]) && words[0] is not ("o" or "yo") && GreetingObjects.Contains(words[1]) &&
            !Any(words, GroupWords) && !Any(words, TradeWords) && !Any(words, AlarmWords) && !AutonomousChatQuestions.IsQuestion(text))
            return Is(eAutonomousChatIntent.Greeting, out intent);
        // "hi", "hey all", "hail Haralhelm", "evening folks" (a greeting with a question after it is a question)
        if (GreetingWords.Contains(words[0]) && !question &&
            (n == 1 || n == 2 && !Any(words, GroupWords) && !Any(words, TradeWords) && !Any(words, AlarmWords) ||
             n <= 4 && words.Skip(1).All(word => GreetingObjects.Contains(word) || IsNoise(word))))
            return Is(eAutonomousChatIntent.Greeting, out intent);
        if (HasPhrase(words, "good morning", "good evening", "good afternoon", "how is everyone", "hows everyone", "how's everyone",
                "whats up", "what's up", "how goes it") ||
            !question && n <= 6 && HasPhrase(words, "long time", "long time no see", "lt no see", "ltns") ||
            !question && (n <= 6 && HasPhrase(words, "well met") || words[0] == "gm" && words.Skip(1).All(word => GreetingObjects.Contains(word) || IsNoise(word))))
            return Is(eAutonomousChatIntent.Greeting, out intent);
        // "night guys work in the morning", "nite all" (the audit: read as nothing).
        bool farewellTalked = FarewellTalkedAbout(words, question);
        if (!question && words[0] is "night" or "nite" or "gnite" && (n <= 3 || n <= 8 && words[1] is "all" or "guys" or "everyone" or "folks" or
                "peeps" or "night" or "nite" or "yall" or "work" or "gotta" or "bed" or "wife" or "kids"))
            return Is(eAutonomousChatIntent.Farewell, out intent);

        // "inc", "inc 10 mids at amg", "zerg at the mile gate"; not "decent inc these days" or "made good inc today" (income;
        // the audit).
        // 1.2 chat audit (DESIGN-009): "albs inc from the south", "inc from behind" are calls; income is "inc from that camp".
        bool income = (HasPhrase(words, "decent inc", "good inc", "bad inc", "nice inc", "great inc", "my inc", "some inc", "steady inc",
                           "inc these days", "inc today", "inc per") || words.Contains("income") ||
                       System.Text.RegularExpressions.Regex.IsMatch(string.Join(' ', words), @"\binc from (that|this|the|my) (camp|spot|drops?|loot|quest|named)\b")) &&
                      !words.Any(word => word is "albs" or "mids" or "hibs" or "enemy" or "enemies" or "north" or "south" or "east" or "west" or "behind" or "left" or "right");
        // "no inc", "is the inc over", "any inc tonight?", "where is the inc" are about a call, not one (DESIGN-010).
        bool alarmTalked = AboutTheAct(words, IndexIn(words, AlarmWords), question, AlarmAskWords) ||
                           (question || QuestionStarts.Contains(words[0])) && AlarmAskWords.Contains(words[0]) && !Any(words, AlarmWords) &&
                           HasPhrase(words, "albs at", "mids at", "hibs at", "enemies at", "enemy at", "zerg at");
        // "pushing", "heading for our" and "watch out for" warn only with an enemy in the line: not "pushing the 40s finally",
        // "heading for our guild house" or "watch out for the pat" (review 2026-10-26).
        bool enemy = words.Any(word => word is "albs" or "mids" or "hibs" or "alb" or "mid" or "hib" or "enemy" or "enemies" or "zerg" or
            "stealther" or "stealthers" or "gankers" or "realm" or "group" or "fg" or "train" or "assassins" or "infils" or "nightshades" or
            "shadowblades" or "ns" or "sb" or "sbs");
        if (!income && !alarmTalked && (Any(words, AlarmWords) || EnemyCall(words) || HasPhrase(words, "under attack", "under siege", "albs at", "mids at", "hibs at",
                // Beta 1.1.2 (blind round 1): "need defenders at dun crim NOW", "mids ganking at the dock".
                "need defenders", "defenders at", "defenders needed", "need def", "ganking at", "ganking near", "ganking by", "camping the dock",
                "enemies at", "enemy at", "stealthers at", "zerg at", "zerg heading", "zerg coming", "stealthers near", "stealther near",   // chat-check: input
                "stealthers by", "hibs pushing", "mids pushing", "albs pushing", "at our relic", "hitting our", "train coming", "train inc") ||   // chat-check: input
                enemy && HasPhrase(words, "heading for our", "pushing on", "pushing our", "pushing the", "watch out for") ||
                n <= 2 && words.All(word => word is "adds" or "add" or "run" or "train" or "runner" or "runners") ||
                // "stealther on me help", "add on me" (the holdout check)
                Any(words, GankWords) && HasPhrase(words, "hit me", "hitting me", "attacked me", "attacking me", "on me", "jumped me", "killing me",
                    "just hit", "just opened", "camping the", "at the gate", "near the") ||
                n <= 6 && HasPhrase(words, "on me") && !HasPhrase(words, "keep", "pull", "bring", "get them", "get it", "hold") &&
                words.Any(word => word is "add" or "adds" or "stealther" or "stealthers" or "assassin" or
                    "infil" or "nightshade" or "ns" or "shadowblade" or "sb" or "mob" or "mobs" or "pat" or "patrol" or "train" or "zerg")))
            return Is(eAutonomousChatIntent.Alarm, out intent);

        // "gotta log, wife wants the pc", "thats it for me tonite gn all", "u too, later" (the holdout check): a goodbye first.
        // Not "lfg later", "ill buy later", "gotta run back to camp" (review 2026-10-26).
        bool errand = HasPhrase(words, "back to", "run back", "go back", "go get", "go sell", "go buy", "go to", "run to") ||
                      Any(words, GroupWords) || Any(words, TradeWords);
        // "cya all next week, going out of town" (blind round 1: read as travel): a line that starts with a goodbye is one.
        if (!question && !farewellTalked && n <= 10 && words[0] is "cya" or "bye" or "gn" or "gnight" or "laters" or "cu" or "nite" or "goodnight" or "byebye" or "ttyl" or "bbl")
            return Is(eAutonomousChatIntent.Farewell, out intent);
        // 1.2 chat audit (DESIGN-047): "signing off", "peace out", "see you all tomorrow", "logging for the night", "thats me done".
        if (!question && !errand && !farewellTalked && (HasPhrase(words, "gotta log", "got to log", "gotta go", "got to go", "logging off", "logging out", "gotta run",
                "thats it for me", "that s it for me", "done for tonight", "done for tonite", "done for the night", "im done for", "gn all",
                "gnite all", "nite all", "signing off", "peace out", "see you all tomorrow", "see you tomorrow", "see ya tomorrow", "see u tomorrow",
                "see you all later", "logging for the night", "logging for tonight", "thats me done", "that s me done", "thats me for tonight",
                "im gone", "i m gone", "off to bed", "see ya all", "see you all", "see ya guys", "see you guys") ||
                n <= 3 && words[^1] is "later" or "laters" or "cya" or "bye" or "ttyl" && !words.Contains("maybe") ||
                n <= 3 && HasPhrase(words, "see ya", "see you", "see u")))
            return Is(eAutonomousChatIntent.Farewell, out intent);
        // "pc crashed" is a computer, not a price check (the audit).
        bool computer = HasPhrase(words, "pc crashed", "my pc", "new pc", "pc died", "pc froze", "pc is", "pc keeps", "pc broke");
        // "how much power you got" asks about power, not a price (1.2 chat audit, DESIGN-005).
        bool howMuchElse = HasPhrase(words, "how much power", "how much pow", "how much mana", "how much hp", "how much health", "how much xp",
            "how much exp", "how much longer", "how much time", "how much further", "how much farther");
        if (!computer && (words.Where(word => word != "pc").Any(TradeWords.Contains) || words.Contains("pc") && n <= 6) ||
            !howMuchElse && HasPhrase(words, "how much") ||
            HasPhrase(words, "price check", "anyone selling", "anyone buying", "what's it worth", "whats it worth", "will pay",
                "ill pay", "i'll pay", "paying for", "spellcrafted", "make me a",
                // 1.2 chat audit (DESIGN-046): "who sells dyes", "anyone have a spare shield", "what do you want for it", "is 5g fair".
                "who sells", "where can i buy", "anyone have a spare", "anyone got a spare", "want for it", "want for that", "fair price") ||
            System.Text.RegularExpressions.Regex.IsMatch(string.Join(' ', words), @"\b\d+ ?(g|p|gold|plat) (fair|ok|cheap|good|too much)\b"))
            return Is(eAutonomousChatIntent.Trading, out intent);
        // The audit: "not lfg just chatting", "inv full again" (inventory), "my old group quit the game", "group is full sorry" are
        // not group requests; a group that is only talked about is not one either.
        bool notRequest = HasPhrase(words, "not lfg", "not looking", "no lfg", "inv full", "inventory full", "bags full", "group is full",
                              "still no grp", "still no group", "hours lfg", "so dead", "chat is dead", "another wipe",
                              "were full", "we are full", "we're full", "full sorry", "group quit", "group left", "group disbanded",
                              "old group", "my group", "lost my group", "our group", "cant find a group", "can't find a group") ||
                          words.Contains("inventory");
        // The holdout check: "love this group", "if i see one more pbae group im quitting lol" only talk about a group. A bare
        // "group" asks for one only in a question or with a request word ("group?", "any group for vendo", "need a group").
        bool strongGroup = words.Any(word => GroupWords.Contains(word) && word is not ("group" or "grp" or "groups" or "grps" or "party" or "forming"));
        bool groupAsked = Any(words, GroupWords) && (question || n <= 5 && words[0] is "group" or "grp" or "party" || HasPhrase(words, "need a", "need", "any", "looking", "want", "wanna", "join",
            "pst", "send tell", "/tell", "invite", "room", "spot", "spots", "forming", "lf", "anyone", "any1", "who wants", "whos up", "who is up", "starting", "up for"));
        // "my group disbanded, lfg 30 cleric", "our group needs a healer pst" still ask (review 2026-10-26); "3 hours lfg and
        // still no grp" and "not lfg" don't.
        bool complaining = HasPhrase(words, "not lfg", "not looking", "no lfg", "still no grp", "still no group", "hours lfg", "so dead",
            "chat is dead", "another wipe");
        bool asksAnyway = !complaining && (words.Any(word => word is "lfg" or "lfm" or "lf1m" or "lf2m" or "lf3m" or "pst") ||
            HasPhrase(words, "lf grp", "lf group", "lf a group", "need a", "needs a", "need healer", "need tank", "looking for a group", "looking for group"));
        if ((!notRequest || asksAnyway) && (strongGroup || groupAsked || HasPhrase(words, "looking for", "need a healer", "need a tank", "need healer",
                "need tank", "room for", "want to team", "team up", "spots open", "spot open", "spots left", "spot left", "send tells",
                "send tell", "send me a tell", "tag along")))
            return Is(eAutonomousChatIntent.Grouping, out intent);
        // A group only talked about ("love this group", "my old group quit") is nothing to answer as a request, and not the
        // classifier's "group" signal either.
        if (notRequest && Any(words, GroupWords))
            return Is(eAutonomousChatIntent.Unknown, out intent);

        if (!farewellTalked && (n <= 6 && Any(words, FarewellWords) || HasPhrase(words, "bed time", "bedtime", "time for bed", "fall asleep", "off to sleep") ||
            System.Text.RegularExpressions.Regex.IsMatch(" " + string.Join(' ', words) + " ",
                @" im off (?!(to|work|today|tomorrow|tmrw|tonight|this|from|next|on|for|the) )") ||
            HasPhrase(words, "logging off", "log off", "heading to bed", "off to bed",
                "going to bed", "good night", "night all", "night everyone", "gotta go", "got to go", "calling it",
                "see you later", "see ya later", "later all", "later guys", "later everyone") || !question && SaysImOut(text)))
            return Is(eAutonomousChatIntent.Farewell, out intent);

        // "1 more for vendo" asks; "if i see one more pbae group im quitting lol" doesn't (the holdout check).
        if (HasPhrase(words, "need puller", "need a puller", "up for", "anyone forming", "group up") ||
            HasPhrase(words, "1 more", "one more") && (question || HasPhrase(words, "need", "lf", "for", "spot", "pst", "room")) && !HasPhrase(words, "if i see"))
            return Is(eAutonomousChatIntent.Grouping, out intent);
        if (HasPhrase(words, "how do i get", "how do you get", "how far", "which way", "where is", "wheres", "where's", "where are the",
                "where can i", "where do i", "how to get", "stable master", "stablemaster", "horse to", "ticket to", "im lost", "i'm lost",
                "lost again", "i am lost"))
            return Is(eAutonomousChatIntent.Travel, out intent);
        if (Any(words, GankWords) && !Any(words, DungeonWords))
            return Is(eAutonomousChatIntent.RvR, out intent);

        // Class and spec talk: "is friar good?", "best spec for a cleric", "scouts op", "zerkers got nerfed", "bard or druid?"
        // Remarks made for a laugh: "i swear...", "ever notice...", sarcasm ("oh great another patch", "wow that pull went real
        // well"), a race joke ("my troll keeps getting stuck", "my dwarfs beard is better than urs") (the audit).
        if (!question && (HasPhrase(words, "i swear", "ever notice", "ever noticed", "oh great", "went real well", "real smooth", "just great",
                "better than urs", "better than yours", "better than ur", "nothing like getting", "bigger than me", "bigger axe", "smell like",
                "smells like", "you know youre", "you know ur", "more hp than", "pretty sure that", "than our tank", "than my tank",
                "stares at me", "looks at me weird") ||
                // "mids smell like wet wolf", "that troll had a bigger axe than me" (the holdout check)
                words.Length >= 3 && words[0] is "that" or "the" or "this" && words[1] is "troll" or "kobold" or "dwarf" or "lurikeen" or "firbolg" or
                    "frog" or "spraggon" or "mob" or "giant" or "elf" or "celt" or "norseman" or "saracen" or "highlander" ||
                words.Length >= 3 && words[0] is "my" && words[1] is "troll" or "trolls" or "dwarf" or "dwarfs" or "kobold" or "lurikeen" or "elf" or
                    "firbolg" or "norseman" or "saracen" or "inconnu" or "highlander" or "avalonian" or "briton" or "celt" or "sylvan" or "valkyn"))
            return Is(eAutonomousChatIntent.Banter, out intent);
        // "i have class at 8", "finals, should be in class" is school (the audit).
        bool school = HasPhrase(words, "have class", "in class", "to class", "after class", "class tomorrow", "class in the", "class at");
        bool classWord = Any(words, ClassWords) || !school && (words.Contains("class") || words.Contains("classes"));
        if (classWord && n <= 10 || Any(words, SpecWords) && (question || Any(words, OpinionWords)))
            return Is(eAutonomousChatIntent.ClassTalk, out intent);

        if (Any(words, DungeonWords))
            return Is(eAutonomousChatIntent.Dungeon, out intent);
        if (Any(words, RvrWords) || HasPhrase(words, "the keep", "our keep", "their keep", "keep raid", "keep take", "the lord",
                "the frontier", "relic raid", "realm war"))
            return Is(eAutonomousChatIntent.RvR, out intent);
        if (HasPhrase(words, "good xp", "good exp", "where to level", "where to lvl", "good spot", "good camp", "xp spot", "level at",
                "lvl at", "camp at", "hunting at", "where should i hunt", "where to hunt", "should i hunt", "how long to", "camping",
                "where do people level", "xp at"))
            return Is(eAutonomousChatIntent.Grinding, out intent);
        if (HasPhrase(words, "how do i", "how does", "what do i", "what should i", "can someone help", "can anyone help", "need help",
                "help me", "any tips", "any advice", "what level", "what lvl", "when do i", "trainer", "how do you", "what does",
                "what is a", "whats a", "buff me", "buffs", "can i get",
                // 1.2 chat audit (DESIGN-046): "can anyone explain realm abilities", "whats the command for anon", "i dont understand
                // resists", "what happens when i die".
                "explain", "command for", "dont understand", "don t understand", "what happens when", "what happens if", "can i respec",
                "what is power") ||
            words.Contains("mean") && (words.Contains("what") || words.Contains("whats") || words.Contains("does")))
            return Is(eAutonomousChatIntent.Help, out intent);

        // "lol", "haha ok", "gg" with nothing else; "same", "true", "ouch" are a nod
        // 1.2 chat audit (DESIGN-046): "haha yeah", "lol classic", "too funny", "hah good one", "*laughs*", "bwahaha".
        if (n <= 3 && words[0] is "lol" or "lolol" or "haha" or "hahaha" or "hehe" or "heh" or "hah" or "lmao" or "rofl" or "bwahaha" or "muahaha" or "lolz" &&
                words.Skip(1).All(word => word is "yeah" or "yea" or "ya" or "classic" or "good" or "one" or "funny" or "man" or "dude" or "oh" or "no" or "wow" or "nice") ||
            n <= 3 && HasPhrase(words, "too funny", "so funny", "good one", "thats funny", "that s funny") ||
            n == 1 && words[0] is "bwahaha" or "muahaha" or "lolz" or "hahah" or "ahahah" ||
            (text ?? string.Empty).Trim().ToLowerInvariant() is "*laughs*" or "*giggles*" or "*chuckles*" or "*lol*")
            return Is(eAutonomousChatIntent.Laugh, out intent);
        if (Only(words, LaughWords) && !Only(words, BanterWords))
            return Is(eAutonomousChatIntent.Laugh, out intent);
        if (n <= 3 && Only(words, BanterWords, LaughWords))
            return Is(eAutonomousChatIntent.Banter, out intent);
        if (n <= 2 && words.All(word => word is "noob" or "n00b" or "newb" or "nub" or "pff" or "pfft" or "meh"))
            return Is(eAutonomousChatIntent.Banter, out intent);
        // "lol kobolds are so tiny", "cotswold guards never help lol", "i swear the mobs are on steroids": a remark made for a
        // laugh, answered in kind ("lol so true"), not by its topic (the audit: "never help lol" drew "try ur trainer").
        if (!question && n >= 3 && (words.Any(word => word is "lol" or "haha" or "hehe" or "heh" or "lmao" or "rofl" or "lul") ||
                                    HasPhrase(words, "i swear", "ever notice", "so tiny", "the cutest", "on steroids")))
            return Is(eAutonomousChatIntent.Banter, out intent);
        return false;
    }

    private static bool Is(eAutonomousChatIntent value, out eAutonomousChatIntent intent)
    {
        intent = value;
        return true;
    }
}
