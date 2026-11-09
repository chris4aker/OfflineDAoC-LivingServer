using System;

namespace DOL.GS;

/// <summary>
/// The regulars (Beta 1.1.2, round 5: in the stream test every /faction line had a new speaker, and nobody talked twice;
/// on a real server the top tenth of speakers produce most of the lines). Each realm has a rotating set of talkative bots,
/// about 8-15 at a busy peak, chosen by personality (Socialites most, then the always-online), who carry a large share of
/// /faction and guild chat, follow up their own lines and answer each other; most other bots speak there rarely. Who is a
/// regular follows from the bot's id and a six-hour rotation, so it needs no state and costs nothing per tick. Pure.
/// 1.1.3 (owner's PC, RC3: 301 bots spoke in /faction in 2.5 hours, the busiest 34 lines, the top ten a quarter of them): the
/// rotation is no longer staggered by bot, so an evening keeps one cast instead of a slow churn through everyone, and each
/// regular has a part (<see cref="Archetype"/>: the helpful one, the joker, the complainer, the guild recruiter, the old hand)
/// with a running bit of its own.
/// </summary>
public static class AutonomousChatRegulars
{
    /// <summary>How long the realm's cast holds before it rotates, all at once: an evening's regulars are the evening's.</summary>
    public const long RotationMs = 6 * 60 * 60_000L;

    /// <summary>A regular's rest between its own unprompted lines (others rest 2-4 minutes).</summary>
    public const long RegularRestMs = 35_000;

    /// <summary>A regular's chance to talk at a chat check once rested (others: about 1%).</summary>
    public const double RegularChance = 0.7;

    /// <summary>How often a bot that isn't a regular takes a /faction or guild turn when the channel is near its target.</summary>
    public const double OthersShare = 0.08;

    /// <summary>Under this share of its target, a channel takes anyone's line (the regulars alone can't always fill it).</summary>
    public const double OthersBelow = 0.8;

    /// <summary>How many regulars a realm has with this many bots online: 1.2% of them, 5 to 15.</summary>
    public static int TargetCount(int realmBots) => Math.Clamp((int)Math.Round(realmBots * 0.012), 5, 15);

    /// <summary>How many regulars a guild has with this many members online: a fifth of them, 2 to 6.</summary>
    public static int GuildTargetCount(int online) => Math.Clamp((int)Math.Round(online * 0.2), 2, 6);

    /// <summary>A personality's pull towards being a regular (Socialites three times, the always-online one and a half).</summary>
    public static double Weight(BotPersonalityType type) => type switch
    {
        BotPersonalityType.Socialite => 3.0,
        BotPersonalityType.NoLifer => 1.5,
        _ => 1.0,
    };

    /// <summary>The average weight over the six personalities, which are spread evenly.</summary>
    private const double MeanWeight = (3.0 + 1.5 + 4 * 1.0) / 6;

    /// <summary>Whether a bot is one of its realm's regulars now.</summary>
    public static bool IsRegular(long botId, BotPersonalityType type, long now, int realmBots) =>
        Drawn(botId, type, now, realmBots, TargetCount(realmBots), 71);

    /// <summary>Whether a bot is one of its guild's regulars now.</summary>
    public static bool IsGuildRegular(long botId, BotPersonalityType type, long now, int guildOnline) =>
        Drawn(botId, type, now, guildOnline, GuildTargetCount(guildOnline), 73);

    private static bool Drawn(long botId, BotPersonalityType type, long now, int population, int target, ulong salt)
    {
        if (population <= 0 || botId <= 0)
            return false;
        long slot = Math.Max(0, now) / RotationMs;
        double p = Math.Min(1.0, target / (double)population * Weight(type) / MeanWeight);
        return AutonomousBotPersonality.Mix(botId ^ (slot * 7_919), salt) % 1_000_000 / 1_000_000.0 < p;
    }

    /// <summary>The part a regular plays in its realm's chat, fixed from its id and personality.</summary>
    public enum Archetype { Helper, Joker, Complainer, Recruiter, Veteran }

    /// <summary>A regular's part: Socialites are mostly helpers and jokers, the always-online mostly old hands. Pure.</summary>
    public static Archetype ArchetypeOf(long botId, BotPersonalityType type, bool guildOfficer)
    {
        if (guildOfficer && AutonomousBotPersonality.Mix(botId, 79) % 3 == 0)
            return Archetype.Recruiter;
        int roll = (int)(AutonomousBotPersonality.Mix(botId, 83) % 100);
        return type switch
        {
            BotPersonalityType.Socialite => roll < 45 ? Archetype.Helper : roll < 85 ? Archetype.Joker : Archetype.Complainer,
            BotPersonalityType.NoLifer => roll < 60 ? Archetype.Veteran : roll < 80 ? Archetype.Complainer : Archetype.Helper,
            BotPersonalityType.Daredevil => roll < 55 ? Archetype.Joker : roll < 85 ? Archetype.Complainer : Archetype.Veteran,
            BotPersonalityType.Leveler => roll < 50 ? Archetype.Complainer : roll < 80 ? Archetype.Helper : Archetype.Joker,
            _ => roll < 40 ? Archetype.Helper : roll < 70 ? Archetype.Joker : roll < 90 ? Archetype.Complainer : Archetype.Veteran,
        };
    }

    // The running bits: each regular has two of its part's lines (by id) and comes back to them through the evening, so the
    // channel learns who it is. {guild} is the recruiter's real guild.
    private static readonly string[] HelperBits =
    [
        "if anyone new needs directions just /send me", "reminder: visit ur trainer when u ding, ppl forget", "anyone stuck on a quest, ask away",
        "if u need a hand with a named /send me, im around", "new folks: bind at the nearest town before u head out", "happy to answer questions tonight, ask whatever",
    ];

    private static readonly string[] JokerBits =
    [
        "if anyone needs me ill be face down in a bog somewhere", "i solo for the challenge. the challenge is staying alive", "my horse has better gear than me",
        "pretty sure that last mob was lagging on purpose", "achievement unlocked: died to a rabbit again", "i dont run from fights, i strategically relocate",
    ];

    private static readonly string[] ComplainerBits =
    [
        "is it just me or is everything camped tonight", "4 hours, zero drops. love this game", "spawns are worse every patch i swear",
        "the lag tonight is something else", "why is every good camp taken when i log on", "this exp curve is a crime",
    ];

    private static readonly string[] RecruiterBits =
    [
        "{guild} still has room, friendly folks, /send me", "{guild} is recruiting all levels, we group most nights", "looking for a guild? {guild}, /send me",
    ];

    private static readonly string[] VeteranBits =
    [
        "back in my day we walked to the frontier. both ways.", "been at this since launch and still get lost in dungeons", "this realm was a ghost town at launch, look at it now",
        "remember when the frontier was empty on weeknights", "i have died to every mob in this realm at least once", "old hands know: never trust a quiet keep",
    ];

    private static string[] BitsOf(Archetype archetype) => archetype switch
    {
        Archetype.Helper => HelperBits,
        Archetype.Joker => JokerBits,
        Archetype.Complainer => ComplainerBits,
        Archetype.Recruiter => RecruiterBits,
        _ => VeteranBits,
    };

    /// <summary>
    /// A regular's running bit: one of the two lines of its part that belong to it (the same two for the same bot), or null
    /// (a recruiter with no guild). <paramref name="turn"/> alternates between them. Pure.
    /// </summary>
    public static string BitOf(long botId, Archetype archetype, string guildName, int turn)
    {
        string[] pool = BitsOf(archetype);
        int first = (int)(AutonomousBotPersonality.Mix(botId, 89) % (ulong)pool.Length);
        int second = (first + 1 + (int)(AutonomousBotPersonality.Mix(botId, 97) % (ulong)(pool.Length - 1))) % pool.Length;
        string line = pool[Math.Abs(turn) % 2 == 0 ? first : second];
        if (line.Contains("{guild}"))
        {
            if (string.IsNullOrWhiteSpace(guildName))
                return null;
            line = line.Replace("{guild}", guildName);
        }
        return line;
    }

    /// <summary>A regular comes back to its bit every 35-65 minutes (the same rest for the same bot).</summary>
    public static long BitRestMs(long botId) => 35 * 60_000 + (long)(AutonomousBotPersonality.Mix(botId, 101) % 30) * 60_000;

    /// <summary>
    /// Whether a bot tells its own death or wipe in /faction: a regular always; anyone else about four times in ten (RC3: 180
    /// of the 784 /faction lines were stories, each from another bot, so the quiet many crowded out the voices the realm knew).
    /// </summary>
    public const double OthersTellStory = 0.4;

    public static bool TellsOwnStory(bool regular, Random random) => regular || random.NextDouble() < OthersTellStory;
}
