using System;

namespace DOL.GS;

/// <summary>How well a bot group fights in the open field (docs/RVR-FIELD-1.2.md, "Skill tiers").</summary>
public enum RvrSkillTier : byte
{
    Casual,
    Average,
    Veteran,
}

/// <summary>
/// The skill tier of a bot group or solo bot in open-field RvR (work package 0; design principle 1, "human-plausible,
/// not optimal"). A group gets its tier once, from its id and its leader's personality: 30% Casual, 50% Average, 20%
/// Veteran at normal rvr_bot_awareness; easy and veteran move the mix. The later packages read the tier for their
/// delays, spreads and failure rates; nothing reads it for a decision yet. Pure and the same for a given id on every
/// run (no random number, no clock).
/// </summary>
public static class RvrGroupSkill
{
    public const int CasualPercent = 30;
    public const int AveragePercent = 50;
    public const int VeteranPercent = 20;

    /// <summary>A leader's personality moves the roll by at most this many points (of 100).</summary>
    public const int MaxLeaderTilt = 10;

    private const ulong RollSalt = 0x52565253; // "RVRS"

    /// <summary>The shares in percent (sum 100) for an awareness level: normal 30/50/20, easy 45/45/10, veteran 15/45/40.</summary>
    public static (int Casual, int Average, int Veteran) Shares(RvrBotAwareness awareness) => awareness switch
    {
        RvrBotAwareness.Easy => (45, 45, 10),
        RvrBotAwareness.Veteran => (15, 45, 40),
        _ => (CasualPercent, AveragePercent, VeteranPercent),
    };

    /// <summary>
    /// How far the leader's personality moves the group's roll towards Veteran, in points of 100 (negative: towards
    /// Casual): patient leaders run tighter groups, and a No-lifer plays enough to know the drill. 0 with personalities
    /// off (null). The traits are spread so that the mix over all leaders stays close to the shares.
    /// </summary>
    public static int LeaderTilt(BotPersonality? leader)
    {
        if (leader is not { } personality)
            return 0;
        int tilt = (int)Math.Round((Math.Clamp(personality.Traits.Patience, 0, 9) - 5.5) * 1.5, MidpointRounding.AwayFromZero);
        if (personality.Type == BotPersonalityType.NoLifer)
            tilt += 4;
        return Math.Clamp(tilt, -MaxLeaderTilt, MaxLeaderTilt);
    }

    /// <summary>The group's roll, 0 to 100 (exclusive), fixed by its id.</summary>
    public static double Roll(long id) => AutonomousBotPersonality.Mix(id, RollSalt) % 10_000 / 100.0;

    /// <summary>The tier for a roll (0 to 100) moved by a leader's tilt, with these shares. Pure.</summary>
    public static RvrSkillTier TierForRoll(double roll, int leaderTilt, (int Casual, int Average, int Veteran) shares)
    {
        double moved = roll + Math.Clamp(leaderTilt, -MaxLeaderTilt, MaxLeaderTilt);
        if (moved < shares.Casual)
            return RvrSkillTier.Casual;
        return moved >= 100 - shares.Veteran ? RvrSkillTier.Veteran : RvrSkillTier.Average;
    }

    /// <summary>The tier of the group (or solo bot) with this numeric id. Pure.</summary>
    public static RvrSkillTier For(long id, BotPersonality? leader, RvrBotAwareness awareness) =>
        TierForRoll(Roll(id), LeaderTilt(leader), Shares(awareness));

    /// <summary>The tier of the group with this session id (AutonomousBotGroupCoordinator.SessionId). Pure.</summary>
    public static RvrSkillTier For(string groupId, BotPersonality? leader, RvrBotAwareness awareness) =>
        For(Key(groupId), leader, awareness);

    /// <summary>A stable number for a text id (FNV-1a; string.GetHashCode differs from run to run).</summary>
    public static long Key(string groupId)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in groupId ?? string.Empty)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return (long)hash;
        }
    }
}
