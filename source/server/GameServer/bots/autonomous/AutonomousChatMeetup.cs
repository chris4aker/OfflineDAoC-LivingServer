using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// A player's tell that says where they are, or that they are coming (1.2.3; the owner's game, 2026-11-23: "Eadwynn is too far
/// away to join you", the bot's "in avalon marsh atm, inv me when ur close :)", the player's "I'm by camp station", and the bot's
/// "lol"). The tell read as a grinding remark ("camp"), no grinding reply fitted, and the short answer for a tell ended on a
/// bare laugh. A free gamebot does not walk across zones to meet a player (no promise covers it), so the answer says honestly
/// where the bot is and how to meet; a bot in the player's group follows its group, so it may say it is coming. Pure.
/// </summary>
public static class AutonomousChatMeetup
{
    private static readonly Regex Where = new(
        @"^(?:(?:i'?m|im|i am|am|me|we'?re|were|we are)\s+)?(?:(?:still|now|just|currently|right)\s+)?" +
        @"(?<prep>by|at|near|in|on|around|outside|inside|close to|next to|over by|over at|out by|out at|up by|down by)\s+" +
        @"(?<place>.+?)(?:\s+(?:now|atm|rn|right now|still|already|too|also|lol))?\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex HereAt = new(
        @"^(?:i'?m |im |i am )?(?:here|over here|out here)\s+(?:at|by|in|near)\s+(?<place>.+?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // "im in combat", "im on break", "im at 50%", "im in a group": a state, not a place.
    private static readonly Regex NotAPlace = new(
        @"^(?:a |an |my |your |ur |the )?(?:combat|fight|a fight|group|a group|guild|a guild|raid|a raid|break|lunch|dinner|work|school|" +
        @"bed|the bathroom|bathroom|afk|lag|it|this|that|trouble|danger|luck|line|queue|game|town now|\d+\s*%|\d+|level \d+|lvl \d+|" +
        @"my way|the way|way|my own|love|charge|position|place|spot|time|sync|range|need|deep|over my head|a hurry|hurry|a sec|sec|" +
        @"pain|rough shape|bad shape|good shape|shape|a mood|mood|chat|tells?|voice|discord|vent|ts)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Coming = new(
        @"^(?:ok(?:ay)?|k|kk|cool|sure|yes|yeah|ya|yep|alright|sounds good)?[\s,!.]*(?:i'?m |im |i am |we'?re |were )?" +
        @"(?:omw|otw|on my way|on the way|coming|comin|heading (?:over|there|ur way|your way|to you|to u)|headed (?:over|there|ur way|your way)|" +   // chat-check: input
        @"be right there|brt|be there (?:soon|in a (?:sec|bit|min))|running over)\b[\s!.:)]*$",   // chat-check: input
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Self = new(
        @"^(?:(?:i'?m|im|i am)\s+(?:a |an |lvl |level |only |just |now )?|my (?:level|lvl|class) (?:is |s )?)(?<what>\d{1,2}\b|[a-z]+(?: [a-z]+)?)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly string[] LaughWords = ["lol", "lolol", "lmao", "rofl", "haha", "hahaha", "hehe", "heh", "ha", "hah", "lul", "xd", "kek"];

    private static string Clean(string message)
    {
        string text = (message ?? string.Empty).Trim();
        text = Regex.Replace(text, @"[\s]+", " ");
        return text.TrimEnd('.', '!', ' ', ':', ')', '(');
    }

    /// <summary>
    /// The place a player's line says they are at ("I'm by camp station" -> "camp station", "at the bridge now" -> "the bridge",
    /// "im in camelot hills" -> "camelot hills"), or null for a question, a state ("im in combat", "im on my way") or anything
    /// else. Pure.
    /// </summary>
    public static string PlaceSaid(string message)
    {
        string text = Clean(message);
        if (text.Length == 0 || text.Contains('?') || text.Length > 60)
            return null;
        Match match = Where.Match(text);
        if (!match.Success)
            match = HereAt.Match(text);
        if (!match.Success)
            return null;
        // A bare "in X" or "at X" without "im" reads as a place only when it is short ("at camp station", not "at least i tried").
        string place = match.Groups["place"].Value.Trim().TrimEnd(',', '.');
        if (place.Length < 3 || NotAPlace.IsMatch(place))
            return null;
        string[] words = place.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 5 || words.Any(word => word is "least" or "case" or "fact" or "general" or "theory" or "total" or "all" or "any" or "if" or
                "because" or "but" or "so" or "and" or "lol" or "think" or "guess"))
            return null;
        // "near cap" is short for a place; "on it" is not (above). The place keeps the player's own words.
        return place.ToLowerInvariant();
    }

    /// <summary>"omw", "on my way", "coming", "heading over", "brt": the player says they are coming to the bot. Pure.</summary>
    public static bool SaysComing(string message)
    {
        string text = Clean(message);
        return text.Length > 0 && !text.Contains('?') && Coming.IsMatch(text);
    }

    /// <summary>"im a cleric", "im 5", "my level is 5": the player tells the bot about themselves. Pure.</summary>
    public static bool SaysAboutSelf(string message)
    {
        string text = Clean(message);
        if (text.Length == 0 || text.Contains('?') || PlaceSaid(text) != null || SaysComing(text))
            return false;
        Match match = Self.Match(text);
        if (!match.Success)
            return false;
        // A level ("im 5", "my level is 5") or a class ("im a cleric"), not a state ("im in combat", "im on it", "im fine").
        string what = match.Groups["what"].Value;
        return Regex.IsMatch(what, @"^\d{1,2}$") || AutonomousChatFieldTalk.ClassNamed(what) != null;
    }

    /// <summary>"lol", "heh", "haha lol": a reply that is only a laugh. Pure.</summary>
    public static bool IsBareLaugh(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        string[] words = Regex.Split(text.ToLowerInvariant(), @"[^a-z0-9]+").Where(word => word.Length > 0).ToArray();
        return words.Length > 0 && words.All(word => LaughWords.Contains(word));
    }

    /// <summary>Whether the line laughs or jokes itself ("lol", "haha", ":D"): a laugh back is fair then. Pure.</summary>
    public static bool Laughs(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string text = message.ToLowerInvariant();
        return Regex.Split(text, @"[^a-z0-9]+").Any(word => LaughWords.Contains(word)) || text.Contains(":d") || text.Contains(":p");
    }

    /// <summary>
    /// The answer to a player who says where they are. <paramref name="grouped"/>: the bot is in the player's group and follows
    /// it, so it may say it is coming (the group follow takes it there; promise: native). Otherwise it says where it is and that the
    /// player can invite it once close; a free gamebot does not cross zones for a player. Pure.
    /// </summary>
    public static string PlaceReply(string place, string botZone, bool sameZone, bool grouped, Random random)
    {
        random ??= Random.Shared;
        if (grouped)
            return random.Next(3) switch
            {
                0 => $"k, coming to {place}",   // promise: true (a bot in the player's group follows its group)
                1 => "k omw",                   // promise: true (it follows its group)
                _ => "got it, heading ur way",  // promise: true (it follows its group)
            };
        string z = AutonomousChatFieldTalk.ShortZone(botZone);
        if (string.IsNullOrWhiteSpace(botZone) || z == "here")
            return random.Next(2) == 0 ? $"{place} is a ways from me, inv me when ur close" : "still a ways off from there, come find me and inv";
        if (sameZone || PlaceIn(place, z))
            return random.Next(3) switch
            {
                0 => $"im on the other side of {z} from {place}, come find me and inv",
                1 => $"still across {z}, a ways from {place}. inv when ur close",
                _ => $"im further into {z}, head my way and inv when ur close",
            };
        return random.Next(3) switch
        {
            0 => $"im still in {z}, thats a ways from {place}. inv me when ur closer",
            1 => $"{place} is far from me, im out in {z}. come find me?",
            _ => $"im still out in {z}, cant get to {place} for a bit. inv when ur close",
        };
    }

    private static bool PlaceIn(string place, string zone) =>
        !string.IsNullOrEmpty(place) && !string.IsNullOrEmpty(zone) &&
        (place.Contains(zone, StringComparison.OrdinalIgnoreCase) || zone.Contains(place, StringComparison.OrdinalIgnoreCase));

    /// <summary>"omw" from the player: the bot will be found where it is ("cool, im in avalon marsh"). Pure.</summary>
    public static string ComingReply(string botZone, bool grouped, Random random)
    {
        random ??= Random.Shared;
        if (grouped)
            return random.Next(2) == 0 ? "k" : "cool";
        string z = AutonomousChatFieldTalk.ShortZone(botZone);
        if (string.IsNullOrWhiteSpace(botZone) || z == "here")
            return random.Next(2) == 0 ? "cool, inv when ur close" : "k, see u soon";
        return random.Next(3) switch
        {
            0 => $"cool, im in {z}. inv when ur close",
            1 => $"k, see u in {z}",
            _ => "cool, inv me when ur close",
        };
    }

    /// <summary>"im a cleric", "im 5": the player says something about themselves; a short real answer, never a bare laugh. Pure.</summary>
    public static string AboutSelfReply(Random random)
    {
        random ??= Random.Shared;
        return random.Next(4) switch { 0 => "nice", 1 => "cool", 2 => "oh nice", _ => "ah cool" };
    }

    /// <summary>
    /// The meetup answer to a tell, or null: where the player says they are, or that they are coming. Pure.
    /// </summary>
    public static string Reply(string message, string botZone, bool sameZone, bool grouped, Random random)
    {
        if (PlaceSaid(message) is { } place)
            return PlaceReply(place, botZone, sameZone, grouped, random);
        if (SaysComing(message))
            return ComingReply(botZone, grouped, random);
        return null;
    }
}
