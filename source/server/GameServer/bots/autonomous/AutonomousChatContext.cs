using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// Replies chosen by the player's situation, not only by the words' intent (Beta 1.1.3, the blind evaluation's fix 3):
/// goodnights get "gn", "stuck" gets "/stuck", "inc" a direction question or "omw", a call for help an offer to come, a
/// sale a price or a no, a counter-offer a price, realm news a reaction in kind. Pure: the caller gives the bot's facts and
/// styles the text. Routes are "context ..." (AutonomousChatReplyFit: known).
/// </summary>
public static class AutonomousChatContext
{
    /// <summary>
    /// What the bot knows of the moment. <paramref name="Channel"/>: faction, say, group, guild or tell. <paramref name="Near"/>:
    /// close enough to come (the same region, or in the player's group). <paramref name="LastBotLine"/>: the bot's own last
    /// line to this player in this conversation, if any.
    /// </summary>
    public sealed record Facts(string BotName, string PlayerName, string Channel, bool Near, bool CanHeal, bool InRvr, int Level,
        string LastBotLine = null, string Capital = null);

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
        if (Stuck(words, text))
            return ("context stuck", Pick(random, "/stuck", "type /stuck", "try /stuck, takes a few secs", "/stuck works for that", "/stuck then run back",
                "/stuck, happens to everyone"));
        if (intent == eAutonomousChatIntent.Farewell)
            return ("context farewell", Farewell(text, player, random));
        // A warning ("stealthers near the dock watch out") gets thanks from its own set; a call ("inc 3 albs at amg") a question or "omw".
        if (intent == eAutonomousChatIntent.Alarm && facts.Channel != "group" && shape != "player_headsup")
            return ("context inc", NamesAPlace(incoming, text)
                ? facts.Near || facts.InRvr || facts.Level >= 35
                    ? Pick(random, "omw", "on my way", "omw, 2 min", "coming", "omw, how many?", "omw!!")
                    : Pick(random, "how many?", "how many are there?", "ugh, not again")
                : Pick(random, "where?", "where at?", "where?? how many", "how many and where?", "where"));
        if (shape == "player_help")
            return ("context help", Help(text, facts, random));
        if (RealmNews(text, random) is { } news)
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

    private static bool Stuck(string[] words, string text) =>
        words.Contains("stuck") && !Regex.IsMatch(text, @" stuck (at|on) (level|lvl|\d)| stuck with | xp | stuck lfg| stuck in town waiting") &&
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
            return facts.Near ? Pick(random, "omw, where in there?", "how deep?", "sure, where?") : Pick(random, "how deep?", "where in there?");
        bool close = facts.Near || facts.Channel is "say" or "group";
        if (close && facts.CanHeal && Regex.IsMatch(text, @" (heal|heals|healing|dying|low hp|oom|getting hit|killing me) "))
            return Pick(random, "healing", "got u", "hold on, healing", "heals coming", "on it");
        if (close)
            return Pick(random, "omw", "hang on", "coming, hold on", "on it", "hold on!", "omw hang in there");
        return Pick(random, "where are you?", "whats up?", "what do u need?", "where at?", "whats wrong?");
    }

    /// <summary>A reaction in kind to the realm's news a player reports ("DF is ours again", "they got our pwr relic, ugh",
    /// "relic raid at 9, meet at portal keep", "Emain is hot tonight").</summary>
    private static string RealmNews(string text, Random random)
    {
        if (Regex.IsMatch(text, @" (is ours|are ours|ours again|we took|we got (it|the|their)|took (it )?back|we won|we hold) "))
            return Pick(random, "nice!", "finally", "woot", "about time", "gj all", "\\o/");
        if (Regex.IsMatch(text, @" (got our|took our|lost our|taking our|have our|stole our) "))
            return Pick(random, "ugh", "again??", "not again", "where are they?", "omw", "how many?");
        if (Regex.IsMatch(text, @" (raid|meet|forming|form up) .*(at \d|at the|tonight|tmrw|tomorrow) |(at \d{1,2}|tonight) .*(raid|meet) "))
            return Pick(random, "ill be there", "count me in", "x", "might make it", "ill try to make it", "k ill be there");
        if (Regex.IsMatch(text, @" (is hot|is busy|zerg at|zerg in|everywhere|swarming) "))
            return Pick(random, "omw", "how many?", "where at?", "nice, omw");
        return null;
    }

    private static bool NamesAPlace(string incoming, string text) =>
        Regex.IsMatch(text, @" (at|near|by|in|on|outside|behind|inside) (the )?[a-z]{3,}") ||
        AutonomousChatQuestions.PlaceNamed(incoming, AutonomousChatPlaces.All) != null ||
        Regex.IsMatch(text, @" (amg|hmg|mmg|mg|dock|bridge|gate|keep|relic|pk|portal|caer|dun|faste|fort|castle|tower|milegate) ");

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
