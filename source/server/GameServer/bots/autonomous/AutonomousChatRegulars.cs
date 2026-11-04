using System;

namespace DOL.GS;

/// <summary>
/// The regulars (Beta 1.1.3, round 5: in the stream test every /faction line had a new speaker, and nobody talked twice;
/// on a real server the top tenth of speakers produce most of the lines). Each realm has a rotating set of talkative bots,
/// about 8-15 at a busy peak, chosen by personality (Socialites most, then the always-online), who carry a large share of
/// /faction and guild chat, follow up their own lines and answer each other; most other bots speak there rarely. Who is a
/// regular follows from the bot's id and a three-hour rotation (staggered by bot), so it needs no state and costs nothing per
/// tick. Pure.
/// </summary>
public static class AutonomousChatRegulars
{
    /// <summary>How long a bot stays a regular before the set rotates (staggered per bot): an evening's regulars are the evening's.</summary>
    public const long RotationMs = 3 * 60 * 60_000;

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
        long slot = (Math.Max(0, now) + Math.Abs(botId % 180) * 60_000) / RotationMs;
        double p = Math.Min(1.0, target / (double)population * Weight(type) / MeanWeight);
        return AutonomousBotPersonality.Mix(botId ^ (slot * 7_919), salt) % 1_000_000 / 1_000_000.0 < p;
    }
}
