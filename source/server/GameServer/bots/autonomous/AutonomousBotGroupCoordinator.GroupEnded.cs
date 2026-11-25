using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Issue #625 row 7: the GROUP_ENDED row of an ended autonomous gamebot group (GroupEndedLog has the format, the
/// classifier and the sampling). Written once per session, where the session leaves <c>Sessions</c>: FinishGroupTask,
/// a handover to the player (DetachSession), a battleground trip that takes the group on (TryStartBattlegroundTrip), and
/// a disband that came from elsewhere (OnDisbanding). Under the coordinator's lock; reads the session, writes a line.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    private static void NoteMemberLeft(Session session, GameBot bot)
    {
        if (session == null || session.Ending || bot == null)
            return;
        string cause = session.ProcessingAttendanceRemovals ? "no_show" : GroupEndedLog.LeftCause(bot);
        session.LeftBy[cause] = session.LeftBy.GetValueOrDefault(cause) + 1;
        session.LastLeft = cause;
    }

    private static void LogGroupEnded(Session session, GameBot[] members, string reason, string endCode)
    {
        if (session == null || session.EndLogged)
            return;
        session.EndLogged = true;
        try
        {
            int[] groupChanged = GroupEndedLog.TakeGroupChanged(session.Id);
            if (!GroupEndedLog.TakeSample(out long sequence, out int every, out long dropped))
                return;
            members ??= [];
            (string why, string cause) = GroupEndedLog.Classify(reason, endCode);
            int alive = members.Count(member => member.IsAlive);
            if (members.Length > 0 && alive == 0)
                why = "wipe";
            GroupEndedLog.Write(new GroupEndedLog.Row
            {
                Sequence = sequence, Every = every, Dropped = dropped,
                Group = session.Id,
                Realm = LivingLogText.Realm((int)(session.Leader?.Realm ?? 0)),
                Objective = session.ObjectiveKind.ToString(),
                Reason = why, Cause = cause, LastLeft = session.LastLeft,
                Phase = session.Phase,
                TaskStarted = session.TaskClock.HasStarted,
                HasCamp = session.Camp != null,
                DurationSeconds = (GameLoop.GameLoopTime - session.FormedTick) / 1000,
                SizeStart = session.StartSize, SizeEnd = members.Length, AliveEnd = alive,
                LeftBy = session.LeftBy,
                Wipes = session.Wipes,
                GroupChanged = groupChanged,
                Open = members.Count(member => member.GoalDiagnosticAttempt != null),
                Hosted = session.HadHostedPlayer,
            });
        }
        catch { } // a log line never touches the group
    }
}
