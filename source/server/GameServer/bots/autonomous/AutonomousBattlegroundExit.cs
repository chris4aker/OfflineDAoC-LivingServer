using System;
using System.Collections.Concurrent;

namespace DOL.GS
{
    /// <summary>The next step of a gamebot walking out of a battleground.</summary>
    public enum eBattlegroundExitStep
    {
        /// <summary>Walk to the realm's exit spot (its field arrival point, where the exit portal is).</summary>
        WalkToExit,
        /// <summary>At the exit spot and out of combat: take the exit to the realm's border keep.</summary>
        TakeExit,
        /// <summary>At the exit spot but fighting: finish the fight first.</summary>
        FinishFight,
        /// <summary>The walk has failed for too long: take the exit from where the bot stands (the guaranteed way out).</summary>
        ExitFromHere,
    }

    /// <summary>Why the watchdog takes a gamebot out of a battleground.</summary>
    public enum eBattlegroundForcedExit
    {
        None,
        /// <summary>No XP or realm points gained (and, on a battleground tour, no real movement) for <see cref="AutonomousBattlegroundExit.StuckMilliseconds"/>.</summary>
        NoProgress,
        /// <summary><see cref="AutonomousBattlegroundExit.DeathStrikeLimit"/> deaths inside with nothing gained in between.</summary>
        DeathStrikes,
    }

    /// <summary>
    /// #572 step 4: the rules for a gamebot leaving a battleground,
    /// the exit fallback chain, the in-battleground backstops and the re-entry block that stops a bot looping between the
    /// entry and the exit. Battlegrounds have no zone connections, so every way out goes through the exit portal
    /// (AutonomousWorldBotController.LeaveBattleground) or, for a forced exit, the watchdog's move to the border keep.
    /// Period (1.70 notes): a player who logs out in a battleground not designed for his level is placed in the border
    /// keep; the block here is a recovery backstop for bots only, never a rule for players. Autonomous gamebots only.
    /// </summary>
    public static class AutonomousBattlegroundExit
    {
        /// <summary>A gamebot inside a battleground with no progress (see <see cref="IsProgress"/>) for this long is taken out.</summary>
        public const long StuckMilliseconds = 20 * 60_000;
        /// <summary>Moving this far from the last progress spot is progress for a battleground-tour bot (a roamer crossing the field).</summary>
        public const int ProgressMoveDistance = 1_500;
        /// <summary>A walk to the exit spot that has not arrived in this long ends with the exit taken where the bot stands.</summary>
        public const long ExitWalkMilliseconds = 4 * 60_000;
        /// <summary>Even a bot that keeps getting into fights at the exit leaves after this long.</summary>
        public const long ExitHardLimitMilliseconds = 10 * 60_000;
        /// <summary>A bot forced out cannot be sent into any battleground again for this long.</summary>
        public const long ReentryBlockMilliseconds = 30 * 60_000;
        /// <summary>This many deaths inside with no XP or realm points gained in between force the bot out.</summary>
        public const int DeathStrikeLimit = 3;
        /// <summary>
        /// A leave that has not been asked for in this long has ended; the next one starts a fresh clock. Long enough that a
        /// fight on the way out (which keeps the controller away from the leave) does not restart the 4 and 10 minute limits.
        /// </summary>
        public const long LeaveGapMilliseconds = 5 * 60_000;

        /// <summary>Pure: the next step out, from the distance to the exit spot, combat and how long the bot has been leaving.</summary>
        public static eBattlegroundExitStep ExitStep(float distanceToExit, int exitRadius, bool inCombat, long leavingMilliseconds)
        {
            if (leavingMilliseconds >= ExitHardLimitMilliseconds) return eBattlegroundExitStep.ExitFromHere;
            if (distanceToExit <= exitRadius) return inCombat ? eBattlegroundExitStep.FinishFight : eBattlegroundExitStep.TakeExit;
            if (!inCombat && leavingMilliseconds >= ExitWalkMilliseconds) return eBattlegroundExitStep.ExitFromHere;
            return eBattlegroundExitStep.WalkToExit;
        }

        /// <summary>Pure: when the current leave started (0 or a stale call starts a new one at <paramref name="now"/>).</summary>
        public static long LeaveStarted(long leavingSince, long lastLeaveCall, long now) =>
            leavingSince <= 0 || lastLeaveCall <= 0 || now - lastLeaveCall > LeaveGapMilliseconds ? now : leavingSince;

        /// <summary>Pure: no progress inside for <see cref="StuckMilliseconds"/> (0 = not yet measured).</summary>
        public static bool IsStuck(long lastProgress, long now) =>
            lastProgress > 0 && now - lastProgress >= StuckMilliseconds;

        /// <summary>
        /// Pure: progress inside a battleground. XP or realm points GAINED (a death's XP loss is not progress); moving far
        /// counts only for a battleground-tour bot, whose job is roaming the field (a grinder's job is kills, and walking
        /// back in after every death must not hide a death loop).
        /// </summary>
        public static bool IsProgress(long experience, long lastExperience, long realmPoints, long lastRealmPoints,
            bool movedFar, bool battlegroundTour) =>
            experience > lastExperience || realmPoints > lastRealmPoints || battlegroundTour && movedFar;

        public static bool DeathStrikesBlock(int deathsWithoutGain) => deathsWithoutGain >= DeathStrikeLimit;

        /// <summary>One bot's watch inside a battleground: the last progress snapshot and the deaths since a gain.</summary>
        public readonly record struct Watch(ushort RegionId, long Experience, long RealmPoints, int X, int Y, long LastProgress,
            int DeathsWithoutGain);

        /// <summary>
        /// Pure: the watch after one observation. Entering a battleground (or another one) starts a fresh watch; progress
        /// moves the snapshot spot and clears the death strikes; the XP and realm point snapshot always follows the bot
        /// (so a death's loss is not counted back as a gain).
        /// </summary>
        public static Watch Advance(Watch watch, ushort regionId, long experience, long realmPoints, int x, int y,
            bool battlegroundTour, long now)
        {
            if (watch.RegionId != regionId || watch.LastProgress <= 0)
                return new Watch(regionId, experience, realmPoints, x, y, now, 0);
            long dx = (long)x - watch.X, dy = (long)y - watch.Y;
            bool movedFar = dx * dx + dy * dy >= (long)ProgressMoveDistance * ProgressMoveDistance;
            bool gained = experience > watch.Experience || realmPoints > watch.RealmPoints;
            if (IsProgress(experience, watch.Experience, realmPoints, watch.RealmPoints, movedFar, battlegroundTour))
                return new Watch(regionId, experience, realmPoints, x, y, now, gained ? 0 : watch.DeathsWithoutGain);
            return watch with { Experience = experience, RealmPoints = realmPoints };
        }

        /// <summary>Pure: whether, and why, the watch takes the bot out now.</summary>
        public static eBattlegroundForcedExit Verdict(Watch watch, long now) =>
            DeathStrikesBlock(watch.DeathsWithoutGain) ? eBattlegroundForcedExit.DeathStrikes
            : IsStuck(watch.LastProgress, now) ? eBattlegroundForcedExit.NoProgress
            : eBattlegroundForcedExit.None;

        private static readonly ConcurrentDictionary<long, Watch> Watches = new();
        private static readonly ConcurrentDictionary<long, long> ReentryBlockedUntil = new();

        /// <summary>
        /// The watchdog's observation of a gamebot inside a battleground (milliseconds on the watchdog's UTC clock); returns
        /// the forced exit due, or None. Outside a battleground the watch is dropped.
        /// </summary>
        public static eBattlegroundForcedExit Observe(long botId, ushort regionId, bool inBattleground, long experience,
            long realmPoints, int x, int y, bool battlegroundTour, long now)
        {
            if (!inBattleground)
            {
                Watches.TryRemove(botId, out _);
                return eBattlegroundForcedExit.None;
            }
            Watch watch = Watches.TryGetValue(botId, out Watch held) ? held : default;
            watch = Advance(watch, regionId, experience, realmPoints, x, y, battlegroundTour, now);
            Watches[botId] = watch;
            return Verdict(watch, now);
        }

        /// <summary>A protected hold or a recovery: the running watch's clock restarts (its death strikes stay).</summary>
        public static void Touch(long botId, long now)
        {
            if (Watches.TryGetValue(botId, out Watch watch))
                Watches[botId] = watch with { LastProgress = now };
        }

        /// <summary>A death inside a battleground (AutonomousDeathDiagnostics). Counts only against a running watch.</summary>
        public static void NoteDeath(long botId, ushort regionId)
        {
            if (Watches.TryGetValue(botId, out Watch watch) && watch.RegionId == regionId)
                Watches[botId] = watch with { DeathsWithoutGain = watch.DeathsWithoutGain + 1 };
        }

        public static int DeathsWithoutGain(long botId) => Watches.TryGetValue(botId, out Watch watch) ? watch.DeathsWithoutGain : 0;

        /// <summary>Blocks every battleground for this bot for <see cref="ReentryBlockMilliseconds"/> (after a forced exit).</summary>
        public static void BlockReentry(long botId, long now)
        {
            if (ReentryBlockedUntil.Count > 8_192)
                foreach (var pair in ReentryBlockedUntil)
                    if (pair.Value <= now) ReentryBlockedUntil.TryRemove(pair.Key, out _);
            ReentryBlockedUntil[botId] = now + ReentryBlockMilliseconds;
            Watches.TryRemove(botId, out _);
        }

        public static bool ReentryBlocked(long botId, long now) =>
            ReentryBlockedUntil.TryGetValue(botId, out long until) && now < until;

        /// <summary>The same block on the wall clock the watchdog and the goal pass use.</summary>
        public static bool ReentryBlocked(long botId) => ReentryBlocked(botId, NowMilliseconds());

        public static long NowMilliseconds(DateTime? utc = null) => (utc ?? DateTime.UtcNow).Ticks / TimeSpan.TicksPerMillisecond;

        public static void Clear()
        {
            Watches.Clear();
            ReentryBlockedUntil.Clear();
        }
    }
}
