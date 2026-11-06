using System;

namespace DOL.GS;

/// <summary>
/// Bot-led groups, step B4: the natural break of a party that hosts the player ("last pull then town"). Gamebot parties
/// sell only at a natural task break (the owner's rule): the end of their task, when each gamebot unloads on its own
/// between tasks. A party hosting the player now calls that break itself, before the bags overflow or the task runs out:
/// the leader says "last pull then town", the party finishes that pull, and the session ends there, so its gamebots go
/// to sell as at any task's end. The pure rules; the glue is in AutonomousBotGroupCoordinator.Replacement.cs.
/// </summary>
public static partial class BotLedGroups
{
    /// <summary>The leader calls the last pull when the task has this little left.</summary>
    public const long LastPullBeforeEndMs = 5 * 60_000L;
    /// <summary>No pull came this long after the call (the camp is empty, or the party holds): the party goes.</summary>
    public const long NoPullAfterCallMs = 3 * 60_000L;
    /// <summary>How often the leader looks at the bags and the clock.</summary>
    public const int BreakCheckMs = 10_000;

    /// <summary>
    /// Why the party should take its break now, or null: half or more of its gamebots have nearly full bags ("bags", the
    /// same nearly-full mark a gamebot unloads at between tasks), or its task has started and has five minutes or less to
    /// run ("session"). Gamebots carry no potions and wear no gear down, so there is no break for those.
    /// </summary>
    public static string BreakDue(int bots, int botsNearlyFull, bool taskStarted, long taskRemainingMs) =>
        bots > 0 && botsNearlyFull * 2 >= bots ? "bags"
        : taskStarted && taskRemainingMs <= LastPullBeforeEndMs ? "session"
        : null;

    /// <summary>A called break: why, when, and whether the last pull has been fought.</summary>
    public sealed class BreakState
    {
        public BreakState(string reason, long calledAt, bool fighting)
        {
            Reason = reason;
            CalledAt = calledAt;
            SawFight = fighting;
        }

        public string Reason { get; }
        public long CalledAt { get; }
        public bool SawFight { get; private set; }

        /// <summary>
        /// True when the party ends now: the last pull (the fight going on at the call, or the next one) is over, or no pull
        /// came within <see cref="NoPullAfterCallMs"/>. Never in the middle of a fight.
        /// </summary>
        public bool EndsNow(bool fighting, long now)
        {
            if (fighting)
            {
                SawFight = true;
                return false;
            }
            return SawFight || now - CalledAt >= NoPullAfterCallMs;
        }
    }
}
