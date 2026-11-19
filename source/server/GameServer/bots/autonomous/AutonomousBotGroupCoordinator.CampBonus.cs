using System;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;

namespace DOL.GS;

public static partial class AutonomousBotGroupCoordinator
{
    // A party moves on when its camp bonus runs dry, as period groups did (the camp bonus falls 2% of its full value per
    // kill of a spawn, see AbstractServerRules). From Offline DAoC True 1.65 (shadowofze, 2026-11-06); before it a party
    // kept its camp for the whole task. After several kills in a row with less than a quarter of the bonus left, the camp is set aside
    // for 30 minutes (a spawn left alone that long earns the full bonus again) and the leader picks another one; camp
    // selection already prefers near camps. Outdoor camps only: a dungeon party keeps its room. The streak is a
    // lock-free counter per group; the session lock is taken only on the rare move.
    public const double DryCampBonusFraction = 0.25;
    public const int DryCampKillsInARow = 5;
    private const long DryCampSetAsideMilliseconds = 30 * 60_000;

    private sealed class DryCampStreak { public int Kills; }
    private static readonly ConditionalWeakTable<Group, DryCampStreak> DryCampStreaks = new();

    /// <summary>Pure rule: the kill paid less than a quarter of the full camp bonus.</summary>
    public static bool IsDryCampKill(double campBonusFraction) => campBonusFraction < DryCampBonusFraction;

    /// <summary>Pure rule: the streak after one more kill.</summary>
    public static int NextDryCampStreak(int streak, bool dry) => dry ? streak + 1 : 0;

    /// <summary>Pure rule: enough dry kills in a row to move on.</summary>
    public static bool MovesOnFromDryCamp(int streak) => streak >= DryCampKillsInARow;

    /// <summary>One kill by a gamebot party, with the camp bonus fraction the kill paid (0 to 0.98).</summary>
    public static void NoteCampBonus(Group group, string monsterName, double campBonusFraction)
    {
        if (group == null)
            return;
        DryCampStreak streak = DryCampStreaks.GetValue(group, _ => new DryCampStreak());
        if (!IsDryCampKill(campBonusFraction))
        {
            Volatile.Write(ref streak.Kills, 0);
            return;
        }
        if (!MovesOnFromDryCamp(Interlocked.Increment(ref streak.Kills)))
            return;
        Volatile.Write(ref streak.Kills, 0);
        MoveOnFromDryCamp(group, monsterName, campBonusFraction);
    }

    private static void MoveOnFromDryCamp(Group group, string monsterName, double campBonusFraction)
    {
        if (AutonomousRealmRaid.GetView(group) != null)
            return;
        lock (Sync)
        {
            if (!Sessions.TryGetValue(group, out Session session) || session.Ending ||
                session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve || session.Phase != "Grinding" ||
                session.Camp is not { IsDungeon: false } camp ||
                !string.Equals(camp.MonsterName, monsterName, StringComparison.OrdinalIgnoreCase))
                return;
            Log.Info("AUTONOMOUS_GROUP_CAMP_DRY " + JsonSerializer.Serialize(new
            {
                group = session.Id,
                camp = camp.Id,
                target = camp.MonsterName,
                zone = camp.ZoneName,
                campBonusPercent = Math.Round(campBonusFraction * 100),
                kills = DryCampKillsInARow
            }));
            session.RejectedDungeonCamps[camp.Id] = GameLoop.GameLoopTime + DryCampSetAsideMilliseconds;
            session.Camp = null;
            // Change the target, not the party or its already-running clock.
            if (!session.Recovery.IsRegrouping)
                session.Phase = "Choosing group target";
            WriteSessionMetadata(session, BotMembers(session.Group));
        }
    }
}
