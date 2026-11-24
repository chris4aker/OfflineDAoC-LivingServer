using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DOL.GS;

/// <summary>
/// The human in the war (issue #505; evidence.md §4 rows 3-4, "what the player saw"). Logging only: reads the world,
/// writes rows, draws no random number.
/// <list type="bullet">
/// <item><c>RVR_PLAYER_NOTICED</c>: a bot group first noticing (or being called) the player, with the distance and
/// <c>time_since_first_los_ms</c>. Once per group per contact (<see cref="ContactGapMilliseconds"/>).</item>
/// <item><c>RVR_PLAYER_KILLED</c>: the player's death in a war zone: killer, killer group, how long since that group
/// noticed him, the fight id.</item>
/// <item><c>RVR_PLAYER_VIEW</c>: what the player had around him (enemy and friendly bots within 3,000 and in view, the
/// fights in view), change-only, at most every <see cref="ViewGapMilliseconds"/>.</item>
/// <item><c>RVR_PLAYER_SEAT</c>: hourly per player: minutes in a war zone, minutes with an enemy within 3,000, the most
/// enemies seen, times attacked, notices received, deaths, kills.</item>
/// </list>
/// Player names go in a <c>player=</c> field, which the tools mask.
/// </summary>
public static class RvrPlayerRecord
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrPlayerRecord));

    public const long ContactGapMilliseconds = 120_000;
    public const long ViewGapMilliseconds = 15_000;
    public const int NearRadius = 3_000;
    public const int ViewRadius = WorldMgr.VISIBILITY_DISTANCE;

    /// <summary>When each (group, player) last noticed him; the first notice of a contact writes a row.</summary>
    private static readonly ConcurrentDictionary<(string Group, string Player), (long First, long Last)> Notices = new();

    private sealed class Seat
    {
        public long Samples, InWarSamples, EnemyNearSamples, Attacked, LastAttackedTick;
        public int MostEnemies, Noticed, Deaths, Kills;
        public string LastView;
        public long LastViewAt = long.MinValue / 2;
    }

    private static readonly ConcurrentDictionary<string, Seat> Seats = new(StringComparer.Ordinal);

    /// <summary>The pulse's sample period, for turning samples into minutes.</summary>
    public static long SampleMilliseconds { get; set; } = 5_000;

    /// <summary>
    /// A notice of the player (or the player's pet) by <paramref name="bot"/>. Writes RVR_PLAYER_NOTICED once per group per
    /// contact. <paramref name="called"/>: the group was told by a spotter, not seeing him itself.
    /// </summary>
    public static void Noticed(GameBot bot, GameLiving target, int distance, long sinceFirstLos, bool called)
    {
        GamePlayer player = target as GamePlayer ??
            (target is GameNPC { Brain: AI.Brain.IControlledBrain pet } ? pet.GetPlayerOwner() : null);
        if (bot == null || player == null)
            return;
        long now = GameLoop.GameLoopTime;
        string group = RvrFights.GroupOf(bot) ?? bot.DatabaseID.ToString();
        if (!NoticeIsNew(group, player.Name, now))
            return;
        Seats.GetOrAdd(player.Name, static _ => new Seat()).Noticed++;
        if (!Log.IsInfoEnabled || !RvrRecordBudget.Take("RVR_PLAYER_NOTICED", now, 120))
            return;
        Log.Info(NoticedLine(now, RvrRecordText.Field(player.Name), bot.Name, group, bot.Realm, distance, sinceFirstLos, called,
            target is GamePlayer ? "player" : "pet", RvrFights.IdOf(bot), bot.CurrentRegionID));
    }

    /// <summary>The row. Pure.</summary>
    public static string NoticedLine(long t, string player, string looker, string group, eRealm realm, int distance,
        long sinceFirstLos, bool called, string seen, long fight, ushort region) =>
        $"RVR_PLAYER_NOTICED t={t} player={player} looker={RvrRecordText.Field(looker)} group={group} " +
        $"realm={RvrRecordText.Realm(realm)} distance={distance} time_since_first_los_ms={sinceFirstLos} " +
        $"called={(called ? "true" : "false")} seen={seen} fight={fight} region={region}";

    /// <summary>Whether this is a new contact of the group with the player (and notes it). Pure apart from its memory.</summary>
    public static bool NoticeIsNew(string group, string player, long now)
    {
        var key = (group, player);
        bool fresh = true;
        Notices.AddOrUpdate(key, _ => (now, now), (_, old) =>
        {
            fresh = now - old.Last >= ContactGapMilliseconds;
            return fresh ? (now, now) : (old.First, now);
        });
        return fresh;
    }

    /// <summary>When <paramref name="group"/>'s current contact with the player began, or -1.</summary>
    public static long NoticedAt(string group, string player, long now) =>
        group != null && Notices.TryGetValue((group, player), out var seen) && now - seen.Last < ContactGapMilliseconds * 3
            ? seen.First : -1;

    /// <summary>The player died; writes RVR_PLAYER_KILLED when it was in a war zone.</summary>
    public static void Died(GamePlayer player, GameObject killer)
    {
        if (player == null || !AutonomousRvrRecordScope.InWar(player))
            return;
        GameLiving living = killer as GameLiving;
        GameLiving root = living is GameNPC { Brain: AI.Brain.IControlledBrain c } && c.GetLivingOwner() is { } owner ? owner : living;
        Seats.GetOrAdd(player.Name, static _ => new Seat()).Deaths++;
        long fight = root != null && root.Realm != player.Realm ? RvrFights.Death(player, root) : 0;
        if (!Log.IsInfoEnabled)
            return;
        long now = GameLoop.GameLoopTime;
        string group = RvrFights.GroupOf(root);
        long noticed = NoticedAt(group, player.Name, now);
        Log.Info($"RVR_PLAYER_KILLED t={now} player={RvrRecordText.Field(player.Name)} killer={RvrRecordText.Who(root)} " +
                 $"killerKind={RvrRecordText.Kind(root)} by={RvrRecordText.Kind(living)} " +
                 $"killerRealm={RvrRecordText.Realm(root?.Realm ?? eRealm.None)} killerGroup={group ?? "-"} " +
                 $"killerClass={RvrRecordText.Field((root as GameBot)?.CharacterClass?.Name ?? "-")} " +
                 $"sinceNoticedMs={(noticed < 0 ? -1 : now - noticed)} fight={fight} region={player.CurrentRegionID} " +
                 $"at={player.X},{player.Y},{player.Z}");
    }

    /// <summary>The player killed a bot of another realm (RVR_BOT_KILLED carries the row; this counts it for the seat).</summary>
    public static void Killed(GamePlayer player)
    {
        if (player != null)
            Seats.GetOrAdd(player.Name, static _ => new Seat()).Kills++;
    }

    /// <summary>
    /// The pulse, for every player online: counts the seat and writes RVR_PLAYER_VIEW when what he has around him changed.
    /// <paramref name="bots"/> is the registry's snapshot. Reads the world only.
    /// </summary>
    public static void Sample(long now, IReadOnlyList<GameBot> bots)
    {
        foreach (GamePlayer player in ClientService.Instance.GetPlayers())
        {
            if (player == null || player.ObjectState != GameObject.eObjectState.Active)
                continue;
            Seat seat = Seats.GetOrAdd(player.Name, static _ => new Seat());
            seat.Samples++;
            if (!AutonomousRvrRecordScope.InWar(player))
                continue;
            seat.InWarSamples++;
            int enemiesNear = 0, friendsNear = 0, enemiesInView = 0, friendsInView = 0, fightingNear = 0;
            foreach (GameBot bot in bots)
            {
                if (bot == null || !bot.IsAlive || bot.CurrentRegionID != player.CurrentRegionID ||
                    bot.ObjectState != GameObject.eObjectState.Active)
                    continue;
                double dx = bot.X - player.X, dy = bot.Y - player.Y, d2 = dx * dx + dy * dy;
                if (d2 > (double)ViewRadius * ViewRadius)
                    continue;
                bool near = d2 <= (double)NearRadius * NearRadius;
                if (bot.Realm == player.Realm)
                {
                    friendsInView++;
                    if (near)
                        friendsNear++;
                }
                else
                {
                    enemiesInView++;
                    if (near)
                    {
                        enemiesNear++;
                        if (bot.InCombat)
                            fightingNear++;
                    }
                }
            }
            if (enemiesNear > 0)
                seat.EnemyNearSamples++;
            seat.MostEnemies = Math.Max(seat.MostEnemies, enemiesNear);
            long attacked = Math.Max(player.LastAttackedByEnemyTickPvP, 0);
            if (attacked > seat.LastAttackedTick)
            {
                if (seat.LastAttackedTick == 0 || attacked - seat.LastAttackedTick > 10_000)
                    seat.Attacked++;
                seat.LastAttackedTick = attacked;
            }
            List<long> fights = RvrFights.Ledger.InView(player.CurrentRegionID, player.X, player.Y, now, ViewRadius);
            string view = ViewKey(Bucket(enemiesNear), Bucket(friendsNear), fights);
            if (view == seat.LastView || now - seat.LastViewAt < ViewGapMilliseconds)
                continue;
            seat.LastView = view;
            seat.LastViewAt = now;
            if (Log.IsInfoEnabled && RvrRecordBudget.Take("RVR_PLAYER_VIEW", now, 30))
                Log.Info($"RVR_PLAYER_VIEW t={now} player={RvrRecordText.Field(player.Name)} realm={RvrRecordText.Realm(player.Realm)} " +
                         $"region={player.CurrentRegionID} at={player.X},{player.Y} enemiesNear={enemiesNear} friendsNear={friendsNear} " +
                         $"enemiesInView={enemiesInView} friendsInView={friendsInView} enemiesFighting={fightingNear} " +
                         $"fights={(fights.Count == 0 ? "none" : string.Join(",", fights))} inCombat={(player.InCombat ? "true" : "false")}");
        }
    }

    /// <summary>0, 1, 2-3, 4-7, 8-15, 16+: a view row is written when a band changes, not every bot. Pure.</summary>
    public static int Bucket(int n) => n <= 0 ? 0 : n == 1 ? 1 : n < 4 ? 2 : n < 8 ? 3 : n < 16 ? 4 : 5;

    public static string ViewKey(int enemyBand, int friendBand, IReadOnlyCollection<long> fights) =>
        $"{enemyBand}/{friendBand}/{string.Join(",", fights)}";

    /// <summary>The hourly RVR_PLAYER_SEAT rows (and resets the counts).</summary>
    public static List<string> TakeSeatLines(long now)
    {
        var lines = new List<string>();
        foreach (string name in Seats.Keys.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!Seats.TryRemove(name, out Seat seat) || seat.Samples == 0)
                continue;
            lines.Add(SeatLine(now, RvrRecordText.Field(name), seat.InWarSamples * SampleMilliseconds / 60_000,
                seat.EnemyNearSamples * SampleMilliseconds / 60_000, seat.MostEnemies, seat.Attacked, seat.Noticed,
                seat.Deaths, seat.Kills));
            // The view memory carries on (a new hour does not repeat an unchanged view).
            Seats.TryAdd(name, new Seat { LastView = seat.LastView, LastViewAt = seat.LastViewAt, LastAttackedTick = seat.LastAttackedTick });
        }
        return lines;
    }

    /// <summary>The row. Pure.</summary>
    public static string SeatLine(long t, string player, long minutesInWar, long minutesEnemyNear, int mostEnemies, long attacked,
        int noticed, int deaths, int kills) =>
        $"RVR_PLAYER_SEAT t={t} player={player} minutesInWar={minutesInWar} minutesEnemyWithin3000={minutesEnemyNear} " +
        $"mostEnemiesWithin3000={mostEnemies} timesAttacked={attacked} noticesReceived={noticed} deaths={deaths} kills={kills} " +
        "groupsOffered=na groupsJoined=na defenseEventsCaused=na";

    /// <summary>Tests.</summary>
    public static void Reset()
    {
        Notices.Clear();
        Seats.Clear();
    }
}

/// <summary>
/// <c>/rvrodd [note]</c> (issue #505): the player flags a bad moment in the war. Writes RVR_ODD with the nearest fight
/// id and siege (keep) id. Integration point with #475: when its <c>/odd</c> lands (branch feature/1.2.3-say-gate,
/// <c>OddCommandHandler.OnCommand</c>), it calls <see cref="Flag"/> as well and <c>/rvrodd</c> can go. Changes nothing
/// in the game.
/// </summary>
public static class RvrOdd
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrOdd));
    private static long _seq;

    /// <summary>Writes the row; returns its number and the fight and siege it names (0 / "none" when none).</summary>
    public static (long Seq, long Fight, string Siege) Flag(GamePlayer player, string note)
    {
        if (player == null)
            return (0, 0, "none");
        long now = GameLoop.GameLoopTime;
        long seq = System.Threading.Interlocked.Increment(ref _seq);
        long fight = RvrFights.Ledger.Nearest(player.CurrentRegionID, player.X, player.Y, now, RvrPlayerRecord.ViewRadius);
        string siege = RvrSieges.NearestKeep(player.CurrentRegionID, player.X, player.Y, RvrPlayerRecord.ViewRadius) ?? "none";
        if (Log.IsInfoEnabled)
            Log.Info(Line(seq, now, player.Name, player.Realm, player.CurrentRegionID, player.X, player.Y, fight, siege,
                AutonomousRvrRecordScope.InWar(player), note));
        return (seq, fight, siege);
    }

    /// <summary>The row. Pure.</summary>
    public static string Line(long seq, long t, string player, eRealm realm, ushort region, int x, int y, long fight, string siege,
        bool inWar, string note) =>
        $"RVR_ODD seq={seq} t={t} player={RvrRecordText.Field(player)} realm={RvrRecordText.Realm(realm)} region={region} " +
        $"at={x},{y} inWar={(inWar ? "true" : "false")} fight={fight} siege={siege} note={RvrRecordText.Field(note ?? string.Empty)}";
}
