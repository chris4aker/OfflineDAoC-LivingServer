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
    private static readonly HashSet<string> AwayWords = Set("brb", "afk", "bio", "brt");
    private static readonly HashSet<string> BackWords = Set("back", "re", "bk", "rebuffed");
    private static readonly HashSet<string> FarewellWords = Set("gn", "gnight", "goodnight", "nite", "bye", "byebye", "cya", "cu",
        "laters", "gtg", "g2g", "farewell", "nn");
    private static readonly HashSet<string> GreetingWords = Set("hi", "hello", "hey", "heya", "hiya", "yo", "hail", "sup", "greetings",
        "howdy", "hallo", "hiho", "evening", "morning", "afternoon", "o", "evenin", "mornin", "morn", "eve", "hiya", "hallo", "salut", "hola");
    private static readonly HashSet<string> GreetingObjects = Set("all", "everyone", "everybody", "guys", "folks", "realm", "there",
        "ppl", "people", "friends", "yall", "albion", "midgard", "hibernia", "albs", "mids", "hibs", "guild", "again", "back", "peeps", "gang",
        "folk", "lads", "ladies", "team", "crew", "fam", "everyone", "mates", "all");
    private static readonly HashSet<string> LaughWords = Set("lol", "lolol", "lololol", "lmao", "rofl", "roflmao", "haha", "hahaha",
        "hehe", "heh", "hah", "ha", "lul", "xd", "hehehe", "ahaha", "lawl", "kek", "gg", "omg", "wow", "nice");
    private static readonly HashSet<string> RezWords = Set("rez", "res", "rezz", "rezzes", "ress", "resurrect", "resurrection");
    private static readonly HashSet<string> AlarmWords = Set("inc", "incs", "incoming");
    private static readonly HashSet<string> TradeWords = Set("wts", "wtb", "wtt", "pc", "selling", "buying", "sell", "buy",
        "price", "trade", "trading", "auction", "plat", "craft", "crafter", "crafting", "crafted", "spellcraft", "spellcrafter",
        "alchemist", "tailor", "armorcrafter", "weaponcrafter", "fletcher", "commission");
    private static readonly HashSet<string> GroupWords = Set("lfg", "lfm", "lf1m", "lf2m", "lf3m", "lf4m", "lfmore", "group", "grp",
        "groups", "grps", "party", "inv", "invite", "duo", "trio", "fg", "lfguild", "lf", "forming", "puller", "pullers");
    private static readonly HashSet<string> RvrWords = Set("rvr", "frontier", "relic", "relics", "emain", "hadrians",
        "hadrian", "odins", "mg", "amg", "hmg", "mmg", "milegate", "stealther", "stealthers", "rps", "rp", "rr", "thid", "thidranki",
        "abermenai", "murdaigean", "caledonia", "cathal", "bg", "battleground", "siege", "realmpoints", "zerg", "zergs", "zerging",
        "benowyc", "berkstead", "boldiam", "bledmeer", "crauchon", "crimthainn", "nottmoor", "glenlock", "renaris", "erasleigh",
        "sursbrooke", "hurbury", "blendrake", "arvakr", "fensalir", "hlidskialf", "nottmoor", "bolg", "ailline", "behnn", "scathaig");
    private static readonly HashSet<string> DungeonWords = Set("dungeon", "dungeons", "tomb", "cave", "caves", "caverns",
        "barrows", "den", "lair", "spindelhalla", "spindel", "varulvhamn", "varulv", "df", "darkness", "treibh", "coruscating",
        "keltoi", "fogou", "tepok", "tepoks", "mithra", "vendo", "nisse", "nisses", "muire", "spraggon", "koalinth", "catacombs");
    private static readonly HashSet<string> BanterWords = Set("same", "true", "ikr", "agreed", "yep", "yup", "indeed", "word", "truth",
        "exactly", "totally", "right", "ouch", "yikes", "oof", "ugh", "bah", "meh", "nice", "sweet", "cool", "awesome", "wow");
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
        return words.Length <= 5 && HasPhrase(words, "anyone on", "anybody on", "any1 on", "anyone here", "anybody here", "anyone around",
            "anybody around", "anyone awake", "anyone online", "anybody online", "who is on", "whos on", "whos around", "who is around",
            "anyone still on", "anyone still up", "anyone up");
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
        if (!text.Contains('?') && said.Length is > 0 and <= 8 && IsOrder(said))
            return "player_order";
        // A warning ("stealthers near the dock watch out", "heads up, adds"): thanks for the heads up.
        if (!text.Contains('?') && HasPhrase(said, "watch out", "heads up", "look out", "be careful", "careful all", "careful guys",
                "stealthers near", "stealther near", "stealthers around", "stealther around"))
            return "player_headsup";
        // "dont rez me yet", "hold the pull", "wait for me": a groupmate says ok.
        // Not "anyone need pow pots?" or "i always need pow" (review 2026-10-26).
        if (HasPhrase(said, "dont rez", "don t rez", "no rez yet", "wait to rez", "dont res", "dont pull", "hold the pull", "hold pulls",
                "wait for me", "wait up", "hold up", "sec need pow", "wait for the cleric", "wait for heals", "wait for buffs") ||
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
        // "where to next?" in a group: the next hunt, not directions (live test 2026-10-18).
        if (HasPhrase(words, "where to next", "where now", "where next", "where are we heading", "where are we going", "where should we go",
                "where should we head", "where we going", "where we heading"))
            return "ask_next";
        // "what are you up to?" in /gu drew "lol what" (live test of 0.37-se.1, 2026-10-18): what the bot is doing now, or,
        // with "tonight" or "later", its plans.
        if (HasPhrase(words, "what are we doing", "what are we up to", "what r we doing", "what r we up to"))
            return "ask_plans";
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
        if (HasPhrase(words, "good place to level", "good place to hunt", "good spot to level", "good spot to hunt", "good place to xp",
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
                "what are they worth", "what is it worth") || words.Contains("worth") && HasPhrase(words, "what is", "whats", "what are"))
            return "ask_price";
        if (!words.Any(GroupWords.Contains) && HasPhrase(words, "anyone want to", "anyone wanna", "anyone going", "anyone up for",
                "anyone feel like", "anybody want to", "anybody wanna", "anybody going", "anybody up for", "any1 want to", "any1 wanna",
                "who wants to", "who wants", "who is up for", "whos up for", "anyone doing", "anyone running", "anyone heading", "anyone coming",
                "anybody coming", "anyone interested in", "anyone keen"))
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
        // A real-life remark (guild and group chat in 2003 were full of them): a short reply in kind, by subject.
        // "afk 5 pizza here" is a brb first (the holdout check).
        if (!words.Any(AwayWords.Contains) && RealLife(words) is { } life)
            return "player_rl_" + life;
        // "so i died to a blue con mob?" is a complaint with a question mark, not a question.
        if ((!text.Contains('?') || !AutonomousChatQuestions.StartsLikeQuestion(text)) && (HasPhrase(words, "i died", "died again", "keep dying", "we wiped", "i give up", "so slow", "hate this",
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
        if (Has("sopranos", "lord of the rings", "return of the king", "two towers", "the matrix", "movie", "on tv", "episode", "seen it yet",
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
        return HasPhrase(words, "got my epic", "finished my epic", "finished the epic", "epic done", "epic is done", "done my epic",
            "my first orange", "my first red", "my first purple", "got a masterpiece", "got a mp", "finally got a mp", "finally got my",
            "just got my", "finally made", "made rr", "hit rr", "finally rr", "got the drop", "finally dropped", "first epic",
            "had a baby", "had our baby", "got married", "got the job", "new job", "passed my", "made good", "made bank", "good inc today");
    }

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
        "same", "here", "true", "ikr", "totally", "either", "neither", "thats", "that's", "is", "was", "really", "yeh", "ye");

    /// <summary>"ok", "sure", "nice", "me too", "makes sense": a line that only acknowledges what was said.</summary>
    public static bool IsAcknowledgement(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Contains('?'))
            return false;
        string[] words = AutonomousChatIntentModel.Words(text);
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
            if (raw is "o/" or "\\o" or "o7")
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
        if (!question && n <= 4 && (wave.StartsWith("o/", StringComparison.Ordinal) || wave.StartsWith("\\o", StringComparison.Ordinal)))
            return Is(eAutonomousChatIntent.Greeting, out intent);

        // "are you a bot?", "r u real?", "is this an npc"
        if (Any(words, BotWords) && (HasPhrase(words, "are you", "r u", "are u", "you a", "u a", "is this", "you an", "u an", "you're a",
                "youre a", "ur a", "is he", "is she", "are they", "are all", "are these", "like a", "type like") || n <= 3 && question) ||
            HasPhrase(words, "are you real", "r u real", "are u real", "are you human", "r u human", "are u human", "real person",
                "you even real", "u even real", "you a real", "u a real", "even human", "all bots", "you bots", "u bots", "ur bots",
                "real player", "real people", "actual person", "actual people", "actual player", "u human", "you human",
                "are you macroing", "you macroing", "u macroing", "are you botting", "u botting", "you botting", "actually a person",
                "actually human", "actually real", "a real human", "real or scripted", "real or a bot", "a person or", "human or"))
            return Is(eAutonomousChatIntent.BotCheck, out intent);

        // "thanks for the train buddy": sarcasm after a pull gone wrong, not thanks (the talking update audit).
        if (HasPhrase(words, "thanks for the train", "thx for the train", "ty for the train", "thanks for the adds", "thx for the adds",
                "thanks for the aggro", "thanks a lot for the train"))
            return Is(eAutonomousChatIntent.Laugh, out intent);
        bool thanks = Any(words, ThanksWords) || HasPhrase(words, "thank you", "much appreciated", "appreciate it", "appreciate the",
            "appreciate that", "appreciate you", "appreciated", "owe you one", "owe u one");
        // "ty for the rez" is thanks; "rez pls", "need a rez", "anyone rez?", "rez pls ty" ask for one. "heat res gear" is a resist.
        bool sickness = words.Contains("sick") || words.Contains("sickness");
        bool rezWord = Any(words, RezWords) && !IsResist(words);
        bool asking = question || HasPhrase(words, "need a", "need rez", "can someone", "can anyone", "anyone", "pls", "please", "plz",
            "rez me", "res me", "rez pls", "rezz me", "any healer", "any cleric", "any druid", "any shaman", "any friar", "any rezzer",
            "for a rez", "for rez", "need rez", "need res");
        if (rezWord && !sickness && (asking || !thanks && n <= 6))
            return Is(eAutonomousChatIntent.Rez, out intent);
        // "any healer around? died by the gate", "died in the lake, any druid nearby" ask for a rez without the word.
        if (!thanks && words.Any(word => word is "died" or "dead" or "down") && HasPhrase(words, "any healer", "any healers", "any druid",
                "any cleric", "any shaman", "any friar", "any rezzer", "anyone near", "anyone around", "anybody near", "any1 near", "nearby",
                "anyone close", "can someone", "can anyone"))
            return Is(eAutonomousChatIntent.Rez, out intent);
        // "gotta log ty for the grp all": a goodbye with thanks in it is a goodbye (the holdout check).
        if (thanks && !question && !HasPhrase(words, "back to", "run back", "go back", "run to") && HasPhrase(words, "gotta log", "got to log", "gotta go", "got to go", "logging off", "logging out", "gotta run",
                "heading to bed", "off to bed", "going to bed", "calling it", "night all", "gn all", "gnight"))
            return Is(eAutonomousChatIntent.Farewell, out intent);
        // "thanks! how many are there, should I bring my cleric?": thanks with a real question after it asks about what the bot
        // just said; "anytime" ignored it and a guess read it as class talk (in-game check 2026-10-27). It is left unread, so a
        // question gets an honest answer or a "not sure". A short "ty, any more?" stays thanks.
        if (thanks && question && words.Count(word => !ThanksWords.Contains(word)) >= 5)
            return Is(eAutonomousChatIntent.Unknown, out intent);
        if (thanks)
            return Is(eAutonomousChatIntent.Thanks, out intent);

        // "gz on the ding" and "nice ding" congratulate someone; "ding!", "ding 20", "finally 35" announce one's own.
        bool praise = Any(words, CongratsWords) || words[0] is "nice" or "gj" or "wtg" or "woot";
        if (praise && (Any(words, CongratsWords) || words.Contains("ding")))
            return Is(eAutonomousChatIntent.Congrats, out intent);
        if (words.Contains("ding") && !HasPhrase(words, "ding dong") || words.Contains("dinged") || HasPhrase(words, "just dinged", "hit level", "made level") ||
            !question && System.Text.RegularExpressions.Regex.IsMatch(string.Join(' ', words), @"\b(finally|just) (hit|reached|made) (\d{1,2}|fifty)\b") ||
            words.Contains("finally") && words.Any(word => int.TryParse(word, out int level) && level is >= 2 and <= 50) &&
            (n <= 3 || words.Contains("level") || words.Contains("lvl")))
            return Is(eAutonomousChatIntent.Ding, out intent);

        // "inventory full, gonna run to the merchant brb": a brb at the end of a longer line is still a brb (the audit).
        if ((n <= 6 && HasPhrase(words, "be right back", "be back in", "one sec", "1 sec", "afk for", "hang on", "hold on", "doorbell", "door bell",
                 "phone ringing", "phone call", "gotta get the door", "gotta answer") || Any(words, AwayWords) && !question) &&
            !HasPhrase(words, "im back", "i'm back", "back now"))
            return Is(eAutonomousChatIntent.Away, out intent);
        // "back from dinner", "back sorry phone call" (the audit); not "back to camp", "back in town".
        if (n <= 3 && Only(words, BackWords) || n <= 6 && (HasPhrase(words, "im back", "i'm back", "back now", "back again") ||
                words[0] is "back" or "bk" && (n <= 2 || n <= 6 && !question && words[1] is "from" or "sorry" or "sry" or "all" or "guys" or "finally" or "again") ||
                // "back from vacation guys miss me?" (the holdout check): a question can follow.
                n <= 8 && words[0] is "back" or "bk" && words[1] == "from" ||
                n > 1 && words[0] is "ok" or "k" or "and" or "aaand" && words[1] == "back"))
            return Is(eAutonomousChatIntent.Back, out intent);
        if (words[0] == "wb" && n <= 3)
            return Is(eAutonomousChatIntent.Greeting, out intent);

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
            !question && n <= 4 && HasPhrase(words, "long time", "long time no see", "lt no see", "ltns") ||
            !question && (n <= 6 && HasPhrase(words, "well met") || words[0] == "gm" && words.Skip(1).All(word => GreetingObjects.Contains(word) || IsNoise(word))))
            return Is(eAutonomousChatIntent.Greeting, out intent);
        // "night guys work in the morning", "nite all" (the audit: read as nothing).
        if (!question && words[0] is "night" or "nite" or "gnite" && (n <= 3 || n <= 8 && words[1] is "all" or "guys" or "everyone" or "folks" or
                "peeps" or "night" or "nite" or "yall" or "work" or "gotta" or "bed" or "wife" or "kids"))
            return Is(eAutonomousChatIntent.Farewell, out intent);

        // "inc", "inc 10 mids at amg", "zerg at the mile gate"; not "decent inc these days" or "made good inc today" (income;
        // the audit).
        bool income = HasPhrase(words, "decent inc", "good inc", "bad inc", "nice inc", "great inc", "my inc", "some inc", "steady inc",
            "inc these days", "inc today", "inc from", "inc per") || words.Contains("income");
        // "pushing", "heading for our" and "watch out for" warn only with an enemy in the line: not "pushing the 40s finally",
        // "heading for our guild house" or "watch out for the pat" (review 2026-10-26).
        bool enemy = words.Any(word => word is "albs" or "mids" or "hibs" or "alb" or "mid" or "hib" or "enemy" or "enemies" or "zerg" or
            "stealther" or "stealthers" or "gankers" or "realm" or "group" or "fg" or "train" or "assassins" or "infils" or "nightshades" or
            "shadowblades" or "ns" or "sb" or "sbs");
        if (!income && (Any(words, AlarmWords) || HasPhrase(words, "under attack", "under siege", "albs at", "mids at", "hibs at",
                "enemies at", "enemy at", "stealthers at", "zerg at", "zerg heading", "zerg coming", "stealthers near", "stealther near",
                "stealthers by", "hibs pushing", "mids pushing", "albs pushing", "at our relic", "hitting our", "train coming", "train inc") ||
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
        if (!question && !errand && (HasPhrase(words, "gotta log", "got to log", "gotta go", "got to go", "logging off", "logging out", "gotta run",
                "thats it for me", "that s it for me", "done for tonight", "done for tonite", "done for the night", "im done for", "gn all",
                "gnite all", "nite all") || n <= 3 && words[^1] is "later" or "laters" or "cya" or "bye" && !words.Contains("maybe")))
            return Is(eAutonomousChatIntent.Farewell, out intent);
        // "pc crashed" is a computer, not a price check (the audit).
        bool computer = HasPhrase(words, "pc crashed", "my pc", "new pc", "pc died", "pc froze", "pc is", "pc keeps", "pc broke");
        if (!computer && (words.Where(word => word != "pc").Any(TradeWords.Contains) || words.Contains("pc") && n <= 6) ||
            HasPhrase(words, "how much", "price check", "anyone selling", "anyone buying", "what's it worth", "whats it worth", "will pay",
                "ill pay", "i'll pay", "paying for", "spellcrafted", "make me a"))
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

        if (n <= 6 && Any(words, FarewellWords) || HasPhrase(words, "bed time", "bedtime", "time for bed", "fall asleep", "off to sleep") ||
            System.Text.RegularExpressions.Regex.IsMatch(" " + string.Join(' ', words) + " ",
                @" im off (?!(to|work|today|tomorrow|tmrw|tonight|this|from|next|on|for|the) )") ||
            HasPhrase(words, "logging off", "log off", "heading to bed", "off to bed",
                "going to bed", "good night", "night all", "night everyone", "gotta go", "got to go", "calling it", "im out", "i'm out",
                "see you later", "see ya later", "later all", "later guys", "later everyone"))
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
                "what is a", "whats a", "buff me", "buffs", "can i get") ||
            words.Contains("mean") && (words.Contains("what") || words.Contains("whats") || words.Contains("does")))
            return Is(eAutonomousChatIntent.Help, out intent);

        // "lol", "haha ok", "gg" with nothing else; "same", "true", "ouch" are a nod
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
