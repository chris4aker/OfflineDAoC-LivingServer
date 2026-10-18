using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Guild-chat lines for autonomous gamebots (docs/BOT-GUILDS.md, phase 2). Pure functions of their
/// inputs and a Random, so they can be tested without a server. Lines are short, lowercase and
/// period-plain; every slot is filled from live values with a fallback, never left empty.
/// </summary>
public static class AutonomousBotGuildChatLines
{
    public const int MaxLength = 160;

    public enum ReplyKind
    {
        None,
        Greeting,
        Farewell,
        Thanks,
        Grouping,
        Question,
        Banter,
        Abuse,
    }

    public static string Greeting(string player, Random random) => Pick(random,
        $"hey {player}", $"hi {player}", $"evening {player}", $"{player}!", $"wb {player}", $"yo {player}",
        $"hey {player}, good timing", $"o/ {player}", $"look who it is, hi {player}", $"hiya {player}");

    public static string GreetingToBot(string bot, Random random) => Pick(random,
        $"hey {bot}", $"wb {bot}", $"hi {bot}", $"{bot}!", $"evening {bot}");

    public static string Congratulate(string name, int level, Random random) => Pick(random,
        $"grats {name}!", "gratz!", $"grats on {level} {name}", "gz", $"nice, {level} already?", $"grats {name}, {level} is a good one",
        $"ding grats {name}", $"woot, grats {name}");

    public static string OwnDing(int level, Random random) => Pick(random,
        $"ding {level}!", $"ding {level}", $"finally {level}", $"{level}! about time", $"ding, {level}. off to the trainer");

    public static string OwnDeath(string zone, Random random) => Pick(random,
        $"ugh, died in {zone}", $"well that pull went badly. dead in {zone}", $"anyone near {zone} with a rez?",
        "note to self: check the con before pulling", "died. the run back is the worst part", $"{zone} 1, me 0");

    public static string Welcome(string player, string guild, Random random) => Pick(random,
        $"welcome to {guild}, {player}!", $"welcome {player}!", $"glad to have you {player}", $"welcome aboard {player}",
        $"hey {player}, welcome. ask if you need anything");

    public static string GuildGroup(int guildmates, string zone, bool rvr, Random random)
    {
        string where = string.IsNullOrWhiteSpace(zone) ? "the usual spot" : zone;
        return rvr
            ? Pick(random, $"{guildmates} of us heading out to the frontier, join up if you can", "guild warband forming, frontier bound",
                $"rolling out with {guildmates} guildies, wish us luck")
            : Pick(random, $"guild group forming in {where}, {guildmates} of us so far", $"{guildmates} guildies grouped up in {where}",
                $"heading to {where} with the guild crew", $"guild group at {where} if anyone wants to swap in later");
    }

    public static string KeepClaimed(string keep, string guild, Random random) => Pick(random,
        $"{keep} is ours! claimed for {guild}", $"we claimed {keep}, nice work everyone", $"{guild} holds {keep} now. lets keep it",
        $"claimed {keep}. somebody bring the guards some tea");

    public static string KeepUnderAttack(string keep, Random random) => Pick(random,
        $"{keep} is under attack! everyone who can, get there", $"they are hitting our keep at {keep}, need bodies",
        $"{keep} needs defenders now", $"defend {keep}! our claim, our problem");

    public static string Farewell(string player, Random random) => Pick(random,
        $"night {player}", $"cya {player}", $"later {player}", "gn", $"take care {player}");

    /// <summary>A line a guild member might say unprompted, shaped by the guild's charter.</summary>
    public static string Idle(BotGuildCharter charter, string className, int level, string zone, Random random)
    {
        string cls = string.IsNullOrWhiteSpace(className) ? "adventurer" : className.ToLowerInvariant();
        string where = string.IsNullOrWhiteSpace(zone) ? "the usual spot" : zone;
        string[] shared =
        [
            $"anyone else out in {where}?", "how is everyone doing tonight", $"{level} {cls} looking for something to do",
            "slow night", "anyone need anything? heading to town soon", $"camping in {where}, its quiet",
        ];
        string[] charterLines = charter switch
        {
            BotGuildCharter.Leveling =>
            [
                $"anyone around {level} want to group up?", $"{where} is decent xp for {level}ish if anyone wants in",
                $"lf guildies near {level}, {cls} here", "whos leveling tonight?", "need a healer for a guild group, anyone?",
                $"got a spot at {where}, room for one or two",
            ],
            BotGuildCharter.Social =>
            [
                "anyone else up way too late", "so who has the best story from this week", "brb, getting food",
                "i love this guild, just saying", "anyone want to do something dumb and fun", "what is everyone up to",
            ],
            BotGuildCharter.Rvr =>
            [
                "frontier later? we need numbers", "anyone up for a keep take tonight?", "who is geared for rvr right now",
                "heard the enemy is pushing again", "muster at the portal keep in an hour?", "need more healers for rvr nights",
            ],
            BotGuildCharter.Raiding =>
            [
                "dungeon run this weekend, who is in?", "we need a puller who knows the place", "anyone 40+ up for a raid later",
                "start thinking about the dragon, we need numbers", "who has the gear for a big run",
            ],
            BotGuildCharter.Crafting =>
            [
                "anyone need something made?", "selling a few pieces cheap to guildies", "working on my tradeskill again",
                "who has spare materials, i will pay", "any crafters near the forge tonight?", "masterpiece finally, so happy",
            ],
            _ => [],
        };
        return Pick(random, charterLines.Concat(shared).ToArray());
    }

    /// <summary>A /faction recruiting line for a bot guild.</summary>
    public static string Recruit(string guild, BotGuildCharter charter, int minLevel, Random random)
    {
        string what = charter switch
        {
            BotGuildCharter.Leveling => Pick(random, "leveling guild, we group a lot", "friendly leveling guild", "lots of guild groups at every level"),
            BotGuildCharter.Social => Pick(random, "laid back social guild", "friendly guild, no drama", "chatty social guild"),
            BotGuildCharter.Rvr => Pick(random, "rvr guild, frontier most nights", "rvr focused, keep takes and defense", "rvr guild looking for regulars"),
            BotGuildCharter.Raiding => Pick(random, "dungeon and raid guild", "we run dungeons and big raids", "raid guild, weekend runs"),
            BotGuildCharter.Crafting => Pick(random, "crafting guild, we help with gear", "crafters and friends", "tradeskill guild, all welcome"),
            _ => "friendly guild",
        };
        int level = Math.Max(1, minLevel);
        return Pick(random,
            $"{guild} is recruiting, {what}. {level}+ welcome, say lf guild in faction",
            $"<{guild}> recruiting all classes {level}+, {what}. interested? say so in faction",
            $"{guild} has room for a few more. {what}. lf guild? say it here",
            $"looking for a guild? {guild}, {what}, {level}+");
    }

    public static string OfferInvite(string player, string guild, Random random) => Pick(random,
        $"{player}, sending you an invite to {guild}", $"sure {player}, invite coming from {guild}",
        $"{player} we would love to have you, check for an invite", $"inviting you now {player}");

    /// <summary>To a player asking for a guild below the level bot guilds take members (BOT_GUILD_MIN_LEVEL).</summary>
    public static string TooLowForGuild(string player, string guild, int minLevel, Random random) => Pick(random,
        $"{player}, {guild} takes people from {minLevel} up, come find us then",
        $"hit {minLevel} and send me a tell {player}, {guild} will have a spot",
        $"{player} we invite at {minLevel}+, get a couple more levels and ask again");

    public static string OfferInviteNearby(string player, string playerClass, string guild, Random random)
    {
        string cls = string.IsNullOrWhiteSpace(playerClass) ? "good player" : playerClass.ToLowerInvariant();
        return Pick(random,
            $"hey {player}, you in a guild? {guild} has room", $"{player}, want an invite to {guild}? no pressure",
            $"we could use a {cls} like you in {guild}, {player}");
    }

    /// <summary>A reply to a real member's /gu line.</summary>
    public static string Reply(ReplyKind kind, string player, int level, string zone, Random random)
    {
        string where = string.IsNullOrWhiteSpace(zone) ? "where i am" : zone;
        return kind switch
        {
            ReplyKind.Greeting => Greeting(player, random),
            ReplyKind.Farewell => Farewell(player, random),
            ReplyKind.Thanks => Pick(random, "np", $"anytime {player}", "no problem", "np, glad to help"),
            ReplyKind.Grouping => Pick(random, $"im {level}, in {where}. could come to you", $"{player} i can group in a bit, finishing a pull",
                $"if you are near {level} i am in", $"where are you {player}? i am in {where}"),
            ReplyKind.Question => Pick(random, "not sure, sorry", "good question, someone else might know", $"no idea {player}, try faction",
                "hmm, i would ask a trainer"),
            ReplyKind.Banter => Pick(random, "lol", "heh", $"{player} you are a riot", "haha true", "ha"),
            ReplyKind.Abuse => Pick(random, "easy now", "whoa, ok", "lets keep guild chat friendly"),
            _ => string.Empty,
        };
    }

    /// <summary>What kind of guild-chat line a player wrote, for choosing a reply.</summary>
    public static ReplyKind Classify(string message, eAutonomousChatIntent intent)
    {
        string text = (message ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length == 0)
            return ReplyKind.None;
        if (ContainsWord(text, "ty") || ContainsWord(text, "thx") || ContainsWord(text, "thanks") || text.Contains("thank you"))
            return ReplyKind.Thanks;
        if (ContainsWord(text, "gn") || ContainsWord(text, "night") || ContainsWord(text, "cya") || ContainsWord(text, "bye") ||
            text.Contains("logging off") || text.Contains("heading to bed"))
            return ReplyKind.Farewell;
        return intent switch
        {
            eAutonomousChatIntent.Greeting => ReplyKind.Greeting,
            eAutonomousChatIntent.Farewell => ReplyKind.Farewell,
            eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Grinding or eAutonomousChatIntent.Dungeon => ReplyKind.Grouping,
            eAutonomousChatIntent.Help or eAutonomousChatIntent.Travel or eAutonomousChatIntent.Trading => ReplyKind.Question,
            eAutonomousChatIntent.Abuse => ReplyKind.Abuse,
            eAutonomousChatIntent.Banter => ReplyKind.Banter,
            _ => text.EndsWith('?') ? ReplyKind.Question : ReplyKind.Banter,
        };
    }

    /// <summary>True when a player's /faction or /say line asks to join a guild.</summary>
    public static bool AsksForGuild(string message, bool recruitOpen)
    {
        string text = (message ?? string.Empty).Trim().ToLowerInvariant();
        if (text.Length == 0)
            return false;
        if (text.Contains("lf guild") || text.Contains("lfguild") || text.Contains("looking for a guild") ||
            text.Contains("looking for guild") || text.Contains("need a guild") || text.Contains("guild recruiting") ||
            text.Contains("any guilds") || text.Contains("join your guild") || text.Contains("guild invite") ||
            text.Contains("ginvite me"))
            return true;
        // Short answers only count right after a recruiting post.
        return recruitOpen && (text is "me" or "me!" or "me please" or "interested" or "i'm interested" or "im interested" or
            "inv" or "inv me" or "invite me" or "i'll join" or "ill join" or "count me in" or "sign me up");
    }

    private static bool ContainsWord(string text, string word)
    {
        foreach (string part in text.Split(new[] { ' ', ',', '.', '!', '?', ':', ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == word)
                return true;
        }
        return false;
    }

    private static string Pick(Random random, params string[] options)
    {
        string line = options[(random ?? Random.Shared).Next(options.Length)];
        return line.Length <= MaxLength ? line : line[..MaxLength];
    }

    /// <summary>Every line the generators can produce for sample inputs (for tests).</summary>
    public static IEnumerable<string> Samples(string player, string bot, string guild, int level, string zone)
    {
        for (int seed = 0; seed < 64; seed++)
        {
            var random = new Random(seed);
            yield return Greeting(player, random);
            yield return GreetingToBot(bot, random);
            yield return Congratulate(player, level, random);
            yield return OwnDing(level, random);
            yield return OwnDeath(zone, random);
            yield return Welcome(player, guild, random);
            yield return Farewell(player, random);
            yield return OfferInvite(player, guild, random);
            yield return OfferInviteNearby(player, "Cleric", guild, random);
            yield return GuildGroup(3, zone, false, random);
            yield return GuildGroup(3, zone, true, random);
            yield return KeepClaimed("Caer Benowyc", guild, random);
            yield return KeepUnderAttack("Caer Benowyc", random);
            foreach (BotGuildCharter charter in Enum.GetValues<BotGuildCharter>())
            {
                yield return Idle(charter, "Cleric", level, zone, random);
                yield return Recruit(guild, charter, 5, random);
            }
            foreach (ReplyKind kind in Enum.GetValues<ReplyKind>())
            {
                if (kind != ReplyKind.None)
                    yield return Reply(kind, player, level, zone, random);
            }
        }
    }
}
