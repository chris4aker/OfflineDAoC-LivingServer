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
    /// The /lfg post a bot's group could make now, or null: only the leader of an autonomous PvE party with seats free that
    /// is still assembling ("Leader staging", "Meeting up"), or (1.2.1, #423) one on its way or at its camp while it looks
    /// for a member (AutonomousBotGroupCoordinator.Replacement.cs: an open seat or a replacement); never a full one or one
    /// the player leads. Between searches a party that set off doesn't recruit, so its post stops (AutonomousChatFieldTalk
    /// then says "nm, heading out" once). A search takes one member at a time, so a set-off party's post asks for one.
    /// </summary>
    public static RecruitPost RecruitingPost(GameBot bot)
    {
        if (bot?.Group == null || bot.IsPlayerLedGroup)
            return null;
        lock (Sync)
        {
            if (!TryGetSession(bot.Group, out Session session) || session.Ending || session.Leader != bot ||
                session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve)
                return null;
            bool assembling = IsAssemblyPhase(session.Phase);
            AutonomousPartyReplacement.Search search = session.Replacement;
            if (!assembling && search == null)
                return null;
            GameBot[] members = BotMembers(bot.Group);
            int seated = bot.Group.MemberCount;
            if (members.Length < 2 || seated >= Math.Min(8, bot.Group.MaximumMemberCount))
                return null;
            SharedCamp camp = session.PlannedCamp ?? session.Camp;
            if (!assembling)
                // On the way or at camp: the one role the search looks for, one seat.
                return new RecruitPost(AutonomousPartyReplacement.PostNeed(search.Role), 1, camp?.ZoneName, camp?.IsDungeon == true,
                    members.Min(member => (int)member.Level), members.Max(member => (int)member.Level));
            string[] classes = members.Select(member => member.CharacterClass?.Name ?? member.ClassName).ToArray();
            string need = classes.Any(cls => AutonomousChatFieldTalk.Fills(cls, "healer")) ? null : "healer";
            need ??= classes.Any(cls => AutonomousChatFieldTalk.Fills(cls, "tank")) ? null : "tank";
            need ??= classes.Any(cls => AutonomousChatFieldTalk.Fills(cls, "cc")) ? null : "cc";
            return new RecruitPost(need ?? "dps", Math.Clamp(8 - members.Length, 1, 4), camp?.ZoneName, camp?.IsDungeon == true,
                members.Min(member => (int)member.Level), members.Max(member => (int)member.Level));
        }
    }
}
