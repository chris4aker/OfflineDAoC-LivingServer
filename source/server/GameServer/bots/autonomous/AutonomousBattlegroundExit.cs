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
        private static readonly ConcurrentDictionary<long, Sighting> Sightings = new();

        // #572, owner's answer 51B (2026-11-27): an out-levelled gamebot inside a battleground stays until it dies, the
        // period rule (1.70: players "will still be able to participate in that battleground until they exceed the level
        // cap"; Camelot Herald, Battleground Limits: an over-cap player is removed after a death). Entry keeps the cap.

        /// <summary>A sighting older than this (the bot logged out, or was not seen) does not carry a stay over.</summary>
        public const long SightingGapMilliseconds = 2 * 60_000;

        /// <summary>
        /// The battleground a gamebot is in on a life that began there within its bracket: the region (0 = none) and when
        /// it was last seen there alive.
        /// </summary>
        public readonly record struct Sighting(ushort RegionId, long LastSeen);

        /// <summary>
        /// Pure: the sighting after one look at the bot. Alive inside a battleground whose bracket fits its level: this
        /// battleground, now. Alive inside the same battleground it was last seen in (within
        /// <see cref="SightingGapMilliseconds"/>), now out-levelled: kept, so the stay carries on. A death, leaving every
        /// battleground, another battleground, or a gap (a logout) ends it.
        /// </summary>
        public static Sighting NextSighting(Sighting held, ushort regionId, bool inBattleground, bool alive, bool levelFits,
            long now)
        {
            if (!inBattleground || !alive) return default;
            if (levelFits) return new Sighting(regionId, now);
            return held.RegionId == regionId && held.LastSeen > 0 && now - held.LastSeen <= SightingGapMilliseconds
                ? new Sighting(regionId, now)
                : default;
        }

        /// <summary>
        /// Pure (51B): a gamebot stays in the battleground it has out-levelled while it lives. Only on a life that began
        /// there within its bracket (<paramref name="sighting"/>), and never in a player's company, where today's rules hold.
        /// </summary>
        public static bool StaysOutlevelled(Sighting sighting, ushort regionId, bool alive, bool inPlayerCompany, int level,
            BattlegroundBrackets.Bracket here) =>
            here != null && here.RegionId == regionId && sighting.RegionId == regionId && alive && !inPlayerCompany &&
            level > here.MaxLevel;

        /// <summary>
        /// Pure: the battleground a gamebot works: the one it stands in while it stays out-levelled, otherwise its level's
        /// (null above every bracket).
        /// </summary>
        public static BattlegroundBrackets.Bracket WorkingBracket(bool staysOutlevelled, ushort regionId, int level) =>
            staysOutlevelled ? BattlegroundBrackets.ForRegion(regionId) : BattlegroundBrackets.ForLevel(level);

        /// <summary>
        /// Pure: a party member may be taken to (or kept at) a battleground's camps: its level fits, or it stays there
        /// out-levelled. Entry itself keeps the level cap.
        /// </summary>
        public static bool MayBeIn(BattlegroundBrackets.Bracket bracket, int level, bool staysOutlevelledThere) =>
            BattlegroundBrackets.Allows(bracket, level) || staysOutlevelledThere;

        /// <summary>One look at a gamebot (the controller's turn); returns whether it stays out-levelled where it is.</summary>
        public static bool ObserveStay(long botId, ushort regionId, int level, bool alive, bool inPlayerCompany, long now)
        {
            bool inBattleground = BattlegroundBrackets.IsBattlegroundRegion(regionId);
            Sighting held = Sightings.TryGetValue(botId, out Sighting found) ? found : default;
            if (!inBattleground && held.RegionId == 0) return false; // every turn outside: one lookup, nothing written
            BattlegroundBrackets.Bracket here = inBattleground ? BattlegroundBrackets.ForRegion(regionId) : null;
            Sighting next = NextSighting(held, regionId, inBattleground, alive, BattlegroundBrackets.Allows(here, level), now);
            if (next.RegionId == 0)
                Sightings.TryRemove(botId, out _);
            else
                Sightings[botId] = next;
            return StaysOutlevelled(next, regionId, alive, inPlayerCompany, level, here);
        }

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
            // 51B: a death ends the out-levelled stay; the bot then leaves and the level cap keeps it out.
            Sightings.TryRemove(botId, out _);
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
            Sightings.Clear();
        }
    }
}
