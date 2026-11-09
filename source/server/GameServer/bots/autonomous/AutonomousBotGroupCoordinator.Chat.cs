using System;
using System.Linq;

namespace DOL.GS;

public static partial class AutonomousBotGroupCoordinator
{
    /// <summary>
    /// What a forming bot group could truthfully post in /lfg (Beta 1.1.3, after RC5): <paramref name="Need"/> is a role the
    /// roster really lacks ("healer", "tank", "cc") or "dps", <paramref name="Missing"/> the empty seats of eight,
    /// <paramref name="Where"/> the camp's zone (a dungeon's name when the camp is in one), and the levels the party is.
    /// </summary>
    public sealed record RecruitPost(string Need, int Missing, string Where, bool WhereIsDungeon, int Low, int High);

    /// <summary>
    /// The /lfg post a bot's group could make now, or null: only the leader of an autonomous PvE party that is still
    /// assembling ("Leader staging", "Meeting up") with seats free, never one at its camp, a full one or the player's. A party
    /// is formed at its full locked size and is not backfilled, so its post is the assembly's last call; it stops when the
    /// party sets off (AutonomousChatFieldTalk then says "nvm, full" once).
    /// </summary>
    public static RecruitPost RecruitingPost(GameBot bot)
    {
        if (bot?.Group == null || bot.IsPlayerLedGroup)
            return null;
        lock (Sync)
        {
            if (!TryGetSession(bot.Group, out Session session) || session.Ending || session.Leader != bot ||
                session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve || !IsAssemblyPhase(session.Phase))
                return null;
            GameBot[] members = BotMembers(bot.Group);
            if (members.Length < 2 || members.Length >= 8)
                return null;
            SharedCamp camp = session.PlannedCamp ?? session.Camp;
            string[] classes = members.Select(member => member.CharacterClass?.Name ?? member.ClassName).ToArray();
            string need = classes.Any(cls => AutonomousChatFieldTalk.Fills(cls, "healer")) ? null : "healer";
            need ??= classes.Any(cls => AutonomousChatFieldTalk.Fills(cls, "tank")) ? null : "tank";
            need ??= classes.Any(cls => AutonomousChatFieldTalk.Fills(cls, "cc")) ? null : "cc";
            return new RecruitPost(need ?? "dps", Math.Clamp(8 - members.Length, 1, 4), camp?.ZoneName, camp?.IsDungeon == true,
                members.Min(member => (int)member.Level), members.Max(member => (int)member.Level));
        }
    }
}
