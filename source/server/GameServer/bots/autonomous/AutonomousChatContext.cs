using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// Replies chosen by the player's situation, not only by the words' intent (Beta 1.1.2, the blind evaluation's fix 3):
/// goodnights get "gn", "stuck" gets "/stuck", "inc" a direction or count question, a call for help "where are you?", a
/// sale a price or a no, a counter-offer a price, realm news a reaction in kind, "ready?" the bot's real power and health.
/// A reply that says the bot is coming ("omw", "ill be there") is said only when <see cref="Facts.TryCome"/> says the bot
/// really took a duty to go (ChatPromises; 1.2 chat audit, DESIGN-006 and CORPUS-C-018; WRITING-GUIDE "Never"); otherwise
/// one that promises nothing. Pure apart from that callback: the caller gives the bot's facts and styles the text. Routes are
/// "context ..." (AutonomousChatReplyFit: known).
/// </summary>
public static class AutonomousChatContext
{
    /// <summary>
    /// What the bot knows of the moment. <paramref name="Channel"/>: faction, say, group, guild or tell. <paramref name="Near"/>:
    /// close enough to come (the same region, or in the player's group). <paramref name="LastBotLine"/>: the bot's own last
    /// line to this player in this conversation, if any. <paramref name="HealthPercent"/> and <paramref name="PowerPercent"/>: the
    /// bot's own, for "ready?" (1.2).
    /// </summary>
    public sealed record Facts(string BotName, string PlayerName, string Channel, bool Near, bool CanHeal, bool InRvr, int Level,
        string LastBotLine = null, string Capital = null, int HealthPercent = 100, int PowerPercent = 100)
    {
        /// <summary>
        /// The promise gate (ChatPromises.TryPromiseCome for this bot): true when the bot has taken a real duty to go to the
        /// place (may be null) for the reason, so it may say "omw". Null: it never promises.
        /// </summary>
        public Func<string, ChatPromiseReason, bool> TryCome { get; init; }

        /// <summary>Asks the gate once; true commits the bot.</summary>
        public bool Comes(string place, ChatPromiseReason reason) => TryCome?.Invoke(place, reason) == true;
    }

    private static string Pick(Random random, params string[] options) => options[random.Next(options.Length)];

    /// <summary>The reply the situation calls for, as (route, text), or null when no situation applies.</summary>
    public static (string Route, string Text)? Reply(string incoming, eAutonomousChatIntent intent, string shape, Facts facts, Random random)
    {
        if (string.IsNullOrWhiteSpace(incoming) || facts == null)
            return null;
        string[] words = AutonomousChatIntentModel.Words(incoming).Select(word => word.Replace("'", string.Empty)).ToArray();
        string text = " " + string.Join(' ', words) + " ";
        string player = string.IsNullOrEmpty(facts.PlayerName) ? null : facts.PlayerName.ToLowerInvariant();

        // A counter-offer to the bot's own price ("how about 45?", "too much", "lol no" after "60g for the set").
        if (Price(facts.LastBotLine) is { } asked && CounterOffer(incoming, asked, random) is { } counter)
            return ("context counter", counter);
        // An offer to the bot ("want a res?", "need heals?"): a polite no, never "need a healer for that" (blind round 2).
        if (Regex.IsMatch(text, @"^ (want|need|u want|you want|u need|you need) (a |some )?(res|rez|heal|heals|buff|buffs|port|speed|help) ") && incoming.Contains('?'))
            return ("context offer", Pick(random, "nah im good ty", "im ok ty", "nah ty", "all good ty"));
        // "loot the named", "grab the drop": the group's loot, said to the group.
        if (facts.Channel is "group" or "say" && Regex.IsMatch(text, @"^ (pls |ok |k )?(loot|grab|take) (the |that |it|ur |your )?(named|drop|drops|corpse|chest|loot|it|bag)? "))
            return ("context loot", Pick(random, "k", "on it", "got it", "looting", "k, anyone need it?", "rolling"));   // promise: none (a loot order acknowledged)
        // "fake inc dont bother", "nvm inc": an inc called off.
        if (Regex.IsMatch(text, @" (fake inc|false alarm|no inc|nvm inc|inc nvm|not inc|was a pet|just a pet|was nothing) "))
            return ("context inc", Pick(random, "k", "kk", "ok", "k ty"));
        if (Regex.IsMatch(text, @" (free rps|free rp|free realm points|free kills|easy rps|come get rps) "))
            return ("context rvr", facts.Comes(PlaceIn(incoming), ChatPromiseReason.RealmPoints)
                ? Pick(random, "omw", "on my way", "omw!", "coming", "omw, save me some")   // promise: gated (facts.Comes)
                : Pick(random, "nice", "save me some", "nice, save me some", "lol nice", "gl, get em"));
        // 1.2 chat audit (DESIGN-005): "ready?", "we pulling?", "how much power you got": the bot's real power and health.
        if (shape == "ask_ready" && facts.Channel is "group" or "say" or "tell")
            return ("context ready", Ready(facts, random));
        if (shape == "ask_who_pulls" && facts.Channel is "group" or "say" or "tell")
            return ("context ready", facts.PowerPercent < 50 || facts.HealthPercent < 60 ? Ready(facts, random)
                : Pick(random, "you pull", "your call", "you, go ahead", "go ahead, im ready", "whoever, im rdy"));
        if (Stuck(words, text))
            return ("context stuck", Pick(random, "/stuck", "type /stuck", "try /stuck, takes a few secs", "/stuck works for that", "/stuck then run back",
                "/stuck, happens to everyone"));
        if (intent == eAutonomousChatIntent.Farewell)
            return ("context farewell", Farewell(text, player, random));
        // A warning ("stealthers near the dock watch out") gets thanks from its own set; a call ("inc 3 albs at amg") a question.
        // "inc": never ask what the line already says (blind round 2: "zerg at mmg, 40+ hibs" drew "how many?").
        // 1.2 chat audit (DESIGN-006, CORPUS-C-018): "omw" only from a bot the promise gate sends there; any other reacts, and one
        // already in the frontier may say it has seen them.
        if (intent == eAutonomousChatIntent.Alarm && shape != "player_headsup")
        {
            bool place = NamesAPlace(incoming, text), count = SaysHowMany(text);
            if (facts.Channel == "group")
                return ("context inc", place || count ? Pick(random, "k", "got it", "ok", "k ty") : Pick(random, "where", "where?", "which way", "where?!"));
            bool comes = place && facts.Comes(PlaceIn(incoming), ChatPromiseReason.Alarm);
            string reply = place && count ? comes ? Pick(random, "omw", "on my way", "coming", "omw!!")   // promise: gated (facts.Comes)
                    : facts.InRvr ? Pick(random, "seen them", "ugh, again", "k ty", "not again") : Pick(random, "ugh", "ugh, again", "gl all", "not again", "k ty")
                : place ? comes ? Pick(random, "omw", "omw, how many?", "coming")   // promise: gated (facts.Comes)
                    : facts.InRvr ? Pick(random, "how many?", "seen them, ugh", "how many are there?") : Pick(random, "how many?", "how many are there?", "ugh, again")
                : count ? Pick(random, "where?", "where at?", "where??")
                : Pick(random, "where?", "where at?", "how many and where?", "where");
            return ("context inc", reply);
        }
        if (shape == "player_help")
            return ("context help", Help(text, facts, random));
        if (RealmNews(text, facts, random) is { } news)
            return ("context rvr", news);
        if (intent == eAutonomousChatIntent.Trading || Selling(text) || Buying(text))
        {
            if (Selling(text) && Price(incoming) is { } price)
                return ("context sell", random.NextDouble() < 0.45
                    ? Pick(random, "too rich for me", "no thanks", "pass, sorry", "cant afford that lol", "out of my price range heh", "nah, gl with it")
                    : string.Format(Pick(random, "would do {0}", "{0}?", "id give {0} for it", "{0} and its a deal", "might take it for {0}"), Format(price, 0.65 + random.NextDouble() * 0.2)));
            if (Buying(text))
                return ("context wtb", Pick(random, "dont have any, sorry", "none on me sorry", "cant help, none here", "no spare ones, gl",
                    "sold my last one, sorry", "nope, gl"));
        }
        return null;
    }

    /// <summary>The place a line names, as a place key, or null (what the promise gate is told).</summary>
    private static string PlaceIn(string line) => AutonomousChatQuestions.PlaceNamed(line, AutonomousChatPlaces.All)?.Key;

    /// <summary>"rdy", "need pow", "sec, low hp": what the bot's own power and health say about going on (DESIGN-005). Pure.</summary>
    public static string Ready(Facts facts, Random random)
    {
        if (facts.PowerPercent < 40)
            return Pick(random, "need pow", "oom, sec", "need to med", "low pow, sec", $"{facts.PowerPercent}% pow, sec");
        if (facts.HealthPercent < 60)
            return Pick(random, "sec, low hp", "need hp", "hp first, sec");
        if (facts.PowerPercent < 70)
            return Pick(random, $"{facts.PowerPercent}% pow, ok to go", "half pow but ok", "go, im ok");
        return Pick(random, "rdy", "ready", "yep", "go", "rdy when u are", "full pow, go");
    }

    private static bool Stuck(string[] words, string text) =>
        words.Contains("stuck") && !Regex.IsMatch(text, @" stuck (at|on) (level|lvl|\d)| stuck with | xp | stuck lfg| stuck in town waiting") &&
        // Realism 2e (#482): "stuck on the last epic step" is a quest, not the terrain.
        !Regex.IsMatch(text, @" (quest|quests|epic|step|steps|chain|part) ") &&
        // A pet or a mob stuck behind a door is no /stuck for the player (blind round 2).
        !Regex.IsMatch(text, @" (pet|pets|mob|mobs|add|adds|monster|npc|horse|minion|summon|named|boss|it|he|she|they) (is|s|got|was|keeps|keep|getting) stuck | stuck (pet|mob)") &&
        Regex.IsMatch(text, @" (im|i m|i am|got|get|getting|totally|so|still) stuck | stuck (in|under|on|behind|inside|between|at) | help stuck | stuck help ");

    private static string Farewell(string text, string player, Random random)
    {
        if (Regex.IsMatch(text, @" (gn|gnight|goodnight|night|nite|gnite|bed|bedtime|sleep|asleep|tired|work in the morning|early tomorrow|up early) "))
            return Pick(random, "gn", "night", "gn " + (player ?? "all"), "nite", "night, see ya tomorrow", "gn, sleep well", "gnight", "night night");
        if (Regex.IsMatch(text, @" (next week|out of town|vacation|trip|holiday|for a while|for a few days|this weekend|business trip) "))
            return Pick(random, "have a good trip", "cya when u get back", "have fun, cya next week", "safe trip", "enjoy, cya");
        if (Regex.IsMatch(text, @" (ty|thx|thanks|thank you|tyvm) "))
            return Pick(random, "np, ty too", "cya, good group", "ty, cya", "np cya", "later, good group");
        return Pick(random, "cya", "later", "take care", "cya " + (player ?? "around"), "later man", "have fun", "laters", "bye");
    }

    private static string Help(string text, Facts facts, Random random)
    {
        if (Regex.IsMatch(text, @" (lost) "))
            return Pick(random, "where are you?", "what zone?", "what zone are u in?", "where are u, ill point the way");
        if (Regex.IsMatch(text, @" (confused|first char|first character|new here|new player) "))
            return Pick(random, "welcome! whats up?", "ask away, what do u need?", "welcome :) what do u need?", "whats confusing u?");
        if (Regex.IsMatch(text, @" quest "))
            return Pick(random, "which quest?", "what quest?", "sure, which one?");
        if (Regex.IsMatch(text, @" corpse run "))
            return facts.Near && facts.Comes(PlaceIn(text), ChatPromiseReason.Help) ? Pick(random, "omw, where in there?", "sure, where?")   // promise: gated (facts.Comes)
                : Pick(random, "how deep?", "where in there?", "how far in?");
        // 1.2 chat audit (DESIGN-006): "omw", "coming" or "healing" only from a bot the promise gate sends; any other asks where.
        bool close = facts.Near || facts.Channel is "say" or "group";
        bool healCall = facts.CanHeal && Regex.IsMatch(text, @" (heal|heals|healing|dying|low hp|oom|getting hit|killing me) ");
        if (close && facts.Comes(PlaceIn(text), healCall ? ChatPromiseReason.Heal : ChatPromiseReason.Help))
            return healCall ? Pick(random, "healing", "got u", "hold on, healing", "heals coming", "on it")   // promise: gated (facts.Comes)
                : Pick(random, "omw", "hang on", "coming, hold on", "on it", "hold on!", "omw hang in there");   // promise: gated (facts.Comes)
        if (healCall)
            return Pick(random, "where r u?", "where are you?", "where at?");
        return Pick(random, "where are you?", "whats up?", "what do u need?", "where at?", "whats wrong?");
    }

    /// <summary>A reaction in kind to the realm's news a player reports ("DF is ours again", "they got our pwr relic, ugh",
    /// "relic raid at 9, meet at portal keep", "Emain is hot tonight").</summary>
    private static string RealmNews(string text, Facts facts, Random random)
    {
        if (Regex.IsMatch(text, @" (is ours|are ours|ours again|we took|we got (it|the|their)|took (it )?back|we won|we hold) "))
            return Pick(random, "nice!", "finally", "woot", "about time", "gj all", "yay");
        // 1.2 chat audit (DESIGN-006, CORPUS-C-018, TEXT-010): no "omw", "ill be there" or "count me in": the bot will not come.
        if (Regex.IsMatch(text, @" (got our|took our|lost our|taking our|have our|stole our) "))
            return facts.Comes(PlaceIn(text), ChatPromiseReason.Alarm) ? Pick(random, "omw", "omw, where are they?")   // promise: gated (facts.Comes)
                : Pick(random, "ugh", "again??", "not again", "where are they?", "how many?");
        if (Regex.IsMatch(text, @" (raid|meet|forming|form up) .*(at \d|at the|tonight|tmrw|tomorrow) |(at \d{1,2}|tonight) .*(raid|meet) "))
            return facts.Comes(PlaceIn(text), ChatPromiseReason.RaidTime) ? Pick(random, "ill be there", "count me in", "x", "k ill be there")   // promise: gated (facts.Comes)
                : Pick(random, "might make it", "ill try to make it", "maybe, depends when i get on", "good luck all", "where do we meet?");
        if (Regex.IsMatch(text, @" (is hot|is busy|zerg at|zerg in|everywhere|swarming) "))
            return facts.Comes(PlaceIn(text), ChatPromiseReason.Alarm) ? Pick(random, "omw", "nice, omw")   // promise: gated (facts.Comes)
                : Pick(random, "how many?", "where at?", "nice", "busy night");
        return null;
    }

    /// <summary>Whether a call names where (1.2: public for the bot-to-bot alarm replies, CORPUS-C-053). Pure.</summary>
    public static bool CallNamesAPlace(string incoming) =>
        NamesAPlace(incoming, " " + string.Join(' ', AutonomousChatIntentModel.Words(incoming).Select(word => word.Replace("'", string.Empty))) + " ");

    /// <summary>Whether a call says how many (CORPUS-C-053). Pure.</summary>
    public static bool CallSaysHowMany(string incoming) =>
        SaysHowMany(" " + string.Join(' ', AutonomousChatIntentModel.Words(incoming).Select(word => word.Replace("'", string.Empty))) + " ");

    private static bool NamesAPlace(string incoming, string text) =>
        Regex.IsMatch(text, @" (at|near|by|in|on|outside|behind|inside) (the )?[a-z]{3,}") ||
        AutonomousChatQuestions.PlaceNamed(incoming, AutonomousChatPlaces.All) != null ||
        Regex.IsMatch(text, @" (amg|hmg|mmg|mg|dock|bridge|gate|keep|relic|pk|portal|caer|dun|faste|fort|castle|tower|milegate) ");   // chat-check: input

    /// <summary>"40+ hibs", "3 grps", "2 fg", "a zerg", "8 man": the line says how many. Pure.</summary>
    private static bool SaysHowMany(string text) =>
        Regex.IsMatch(text, @" \d+\+? ?(hibs?|mids?|albs?|enemies|grps?|groups?|fgs?|man|men|people|ppl|of them|stealthers?|assassins?|bards?|champs?|heroes|clerics?)? | (zerg|fg|full group|two groups|three groups|a few|lots of|tons of|a bunch) ") &&
        !Regex.IsMatch(text, @" (rr|level|lvl)\d");

    private static bool Selling(string text) => Regex.IsMatch(text, @" (wts|selling|for sale|obo) ") && !Regex.IsMatch(text, @" (anyone|anybody|any1|who s|whos|who is) selling ");

    private static bool Buying(string text) => Regex.IsMatch(text, @" (wtb|anyone selling|anybody selling|any1 selling|who s selling|whos selling|looking to buy|want to buy) ");

    /// <summary>A price in a line ("3p obo", "for 45g", "60 gold"), as an amount and unit (p, g or s), or null. Pure.</summary>
    public static (double Amount, string Unit)? Price(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;
        Match match = Regex.Match(line.ToLowerInvariant(), @"(\d+(?:\.\d+)?)\s*(p|plat|platinum|g|gold|gc|s|silver)\b");
        if (!match.Success || !double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture,
                out double amount) || amount <= 0)
            return null;
        string unit = match.Groups[2].Value switch { "p" or "plat" or "platinum" => "p", "s" or "silver" => "s", _ => "g" };
        return (amount, unit);
    }

    /// <summary>A share of a price, written as players wrote prices ("45g", "2p", "800g" for 0.8p). Pure.</summary>
    public static string Format((double Amount, string Unit) price, double share)
    {
        double amount = price.Amount * share;
        if (price.Unit == "p" && amount < 1)
            return $"{Math.Max(1, (int)Math.Round(amount * 1000 / 50) * 50)}g";
        int rounded = amount >= 20 ? (int)Math.Round(amount / 5) * 5 : (int)Math.Max(1, Math.Round(amount));
        return $"{rounded}{price.Unit}";
    }

    /// <summary>
    /// The bot's answer to a counter-offer on its own price: "how about 45?" gets a deal or a middle price, "too much" or
    /// "lower?" a small drop or a firm no, "lol no" an easy "worth a shot". Null when the line is no counter-offer. Pure.
    /// </summary>
    public static string CounterOffer(string incoming, (double Amount, string Unit) asked, Random random)
    {
        string t = " " + AutonomousChatQuestions.Normalize(incoming) + " ";
        (double Amount, string Unit)? offered = Price(incoming);
        if (offered == null && Regex.Match(t, @" (?:how about|what about|would you take|would u take|will you take|will u take|ill give you|ill give u|ill do) (\d+(?:\.\d+)?) |^ (\d+(?:\.\d+)?) $") is { Success: true } bare)
            offered = (double.Parse(bare.Groups[1].Success && bare.Groups[1].Value.Length > 0 ? bare.Groups[1].Value : bare.Groups[2].Value,
                System.Globalization.CultureInfo.InvariantCulture), asked.Unit);
        if (offered is { } offer)
        {
            double ratio = offer.Amount * UnitValue(offer.Unit) / (asked.Amount * UnitValue(asked.Unit));
            if (ratio >= 0.9)
                return Pick(random, "deal", "ok deal, trade me", "sure, deal", "done");
            if (ratio >= 0.7)
                return string.Format(Pick(random, "meet me at {0}?", "{0} and its yours", "cant do that, {0}?", "lowest is {0}"),
                    Format(asked, (1 + ratio) / 2));
            return Pick(random, "lol no", "way too low", "nah, cant go that low", "no thanks, ill vendor it first");
        }
        if (Regex.IsMatch(t, @" (too much|too high|too steep|too expensive|lower|cheaper|discount|any lower|best price|come down) "))
            return random.NextDouble() < 0.5
                ? string.Format(Pick(random, "{0} then?", "ok, {0}", "{0}, lowest i go"), Format(asked, 0.85))
                : Pick(random, "firm on price sorry", "cant go lower", "price is fair, sorry");
        if (Regex.IsMatch(t, @"^ (lol no|no|nah|no thanks|no ty|pass) $"))
            return Pick(random, "worth a shot lol", "ok lol", "np", "heh ok");
        return null;
    }

    private static double UnitValue(string unit) => unit switch { "p" => 1000, "s" => 0.01, _ => 1 };
}
