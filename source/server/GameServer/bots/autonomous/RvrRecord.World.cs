using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using DOL.Events;
using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// The sieges as the record sees them (issue #505; evidence.md §4 row 5). A watcher over every keep, sampled by the
/// record's pulse: RVR_SIEGE_DOOR when a door of a keep falls, RVR_SIEGE_LORD when its lord is first hit, falls below
/// half and dies, RVR_SIEGE_STALL every <see cref="StallMilliseconds"/> of a battle (enemy bots at the keep) with no
/// door or lord progress, and RVR_SIEGE_CLEAR when the attackers have gone. Reads the world only.
/// </summary>
public static class RvrSieges
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrSieges));

    public const long StallMilliseconds = 5 * 60_000;
    public const int AtKeepRadius = 2_500;
    public const int AtDoorRadius = 600;
    /// <summary>A battle ends once no attacker has been at the keep this long.</summary>
    public const long ClearMilliseconds = 3 * 60_000;

    /// <summary>One keep's last sample: plain values (tests feed <see cref="Step"/> directly).</summary>
    public readonly record struct Sample(int DoorsUp, int Doors, int DoorHealth, int LordHealth, bool LordAlive, int GuardsUp,
        int Guards, int Attackers, int AttackersAtDoor, int Defenders, int Engines, eRealm Holder);

    public sealed class State
    {
        public bool Battle;
        public long BattleSince, LastProgressAt, LastStallAt, LastAttackersAt;
        public int BestDoorsUp = int.MaxValue, BestDoorHealth = int.MaxValue, BestLordHealth = int.MaxValue;
        public bool LordHit, LordHalf, LordDead;
        public int Stalls;
        public eRealm Holder;
    }

    private static readonly ConcurrentDictionary<int, State> States = new();
    private static readonly ConcurrentDictionary<int, (ushort Region, int X, int Y)> Places = new();

    /// <summary>
    /// One sample of a keep against its state; returns the rows it gives (without the keep fields, which
    /// <see cref="Watch"/> prefixes). Pure apart from <paramref name="state"/>.
    /// </summary>
    public static List<string> Step(State state, in Sample s, long now)
    {
        var rows = new List<string>();
        if (!state.Battle)
        {
            if (s.Attackers == 0)
            {
                state.BestDoorsUp = s.DoorsUp;
                state.BestDoorHealth = s.DoorHealth;
                state.BestLordHealth = s.LordAlive ? s.LordHealth : 0;
                state.LordHit = state.LordHalf = state.LordDead = false;
                state.Holder = s.Holder;
                return rows;
            }
            state.Battle = true;
            state.BattleSince = state.LastProgressAt = state.LastStallAt = state.LastAttackersAt = now;
            state.Stalls = 0;
            // The keep as it stands when the battle begins (doors may have been repaired since the last one).
            state.BestDoorsUp = s.DoorsUp;
            state.BestDoorHealth = s.DoorHealth;
            state.BestLordHealth = s.LordAlive ? s.LordHealth : 0;
            state.LordHit = s.LordAlive && s.LordHealth < 100;
            state.LordHalf = s.LordAlive && s.LordHealth < 50;
            state.LordDead = !s.LordAlive;
            state.Holder = s.Holder;
            rows.Add($"RVR_SIEGE_BEGIN {Fields(s)}");
        }
        long minute = (now - state.BattleSince) / 60_000;
        if (s.Attackers > 0)
            state.LastAttackersAt = now;
        bool progress = false;
        if (s.DoorsUp < state.BestDoorsUp)
        {
            rows.Add($"RVR_SIEGE_DOOR minute={minute} down={state.BestDoorsUp - s.DoorsUp} {Fields(s)}");
            state.BestDoorsUp = s.DoorsUp;
            progress = true;
        }
        if (s.DoorHealth < state.BestDoorHealth)
        {
            state.BestDoorHealth = s.DoorHealth;
            progress = true;
        }
        int lord = s.LordAlive ? s.LordHealth : 0;
        if (lord < state.BestLordHealth)
        {
            state.BestLordHealth = lord;
            progress = true;
        }
        if (!state.LordHit && s.LordAlive && lord < 100)
        {
            state.LordHit = true;
            rows.Add($"RVR_SIEGE_LORD minute={minute} stage=hit {Fields(s)}");
        }
        if (!state.LordHalf && s.LordAlive && lord < 50)
        {
            state.LordHalf = true;
            rows.Add($"RVR_SIEGE_LORD minute={minute} stage=half {Fields(s)}");
        }
        if (!state.LordDead && !s.LordAlive && state.LordHit)
        {
            state.LordDead = true;
            rows.Add($"RVR_SIEGE_LORD minute={minute} stage=dead {Fields(s)}");
        }
        if (progress)
            state.LastProgressAt = now;
        if (s.Holder != state.Holder)
        {
            rows.Add($"RVR_SIEGE_END minute={minute} result=taken from={RvrRecordText.Realm(state.Holder)} stalls={state.Stalls} {Fields(s)}");
            state.Battle = false;
            state.Holder = s.Holder;
            return rows;
        }
        if (now - state.LastAttackersAt >= ClearMilliseconds)
        {
            rows.Add($"RVR_SIEGE_END minute={minute} result=cleared stalls={state.Stalls} {Fields(s)}");
            state.Battle = false;
            return rows;
        }
        if (now - state.LastProgressAt >= StallMilliseconds && now - state.LastStallAt >= StallMilliseconds)
        {
            state.LastStallAt = now;
            state.Stalls++;
            rows.Add($"RVR_SIEGE_STALL minute={minute} stalledMinutes={(now - state.LastProgressAt) / 60_000} why={StallReason(s)} {Fields(s)}");
        }
        return rows;
    }

    /// <summary>What holds a stalled siege, the first that applies. Pure.</summary>
    public static string StallReason(in Sample s)
    {
        if (s.Attackers == 0)
            return "noAttackersAtKeep";
        if (s.DoorsUp > 0 && s.DoorsUp == s.Doors)
            return s.AttackersAtDoor == 0 ? "noAttackersAtDoor" : s.Engines == 0 ? "doorHoldingNoEngine" : "doorHolding";
        if (s.LordAlive && s.LordHealth >= 100)
            return s.GuardsUp > 0 ? "lordUnreachedGuardsUp" : "lordUnreached";
        if (s.LordAlive)
            return "lordHealing";
        return "other";
    }

    private static string Fields(in Sample s) =>
        $"holder={RvrRecordText.Realm(s.Holder)} doors={s.DoorsUp}/{s.Doors} doorHealth={s.DoorHealth} " +
        $"lord={(s.LordAlive ? s.LordHealth + "%" : "dead")} guards={s.GuardsUp}/{s.Guards} attackers={s.Attackers} " +
        $"attackersAtDoor={s.AttackersAtDoor} defenders={s.Defenders} engines={s.Engines}";

    /// <summary>The pulse: samples every keep (frontier and battleground). Reads the world only.</summary>
    public static void Watch(long now, IReadOnlyList<GameBot> bots)
    {
        ICollection<AbstractGameKeep> keeps = GameServer.KeepManager?.GetAllKeeps();
        if (keeps == null)
            return;
        foreach (AbstractGameKeep keep in keeps)
        {
            if (keep == null || keep.IsPortalKeep)
                continue;
            GameKeepDoor[] doors = keep.Doors.Values.ToArray();
            GameKeepGuard[] guards = keep.Guards.Values.ToArray();
            GameKeepGuard lord = guards.FirstOrDefault(guard => guard is GuardLord);
            Places[keep.KeepID] = (keep.Region, keep.X, keep.Y);
            int attackers = 0, atDoor = 0, defenders = 0;
            foreach (GameBot bot in bots)
            {
                if (bot == null || !bot.IsAlive || bot.CurrentRegionID != keep.Region)
                    continue;
                double dx = bot.X - keep.X, dy = bot.Y - keep.Y;
                if (dx * dx + dy * dy > (double)AtKeepRadius * AtKeepRadius)
                    continue;
                if (bot.Realm == keep.Realm)
                {
                    defenders++;
                    continue;
                }
                attackers++;
                foreach (GameKeepDoor door in doors)
                    if (door.IsAlive && bot.IsWithinRadius(door, AtDoorRadius))
                    {
                        atDoor++;
                        break;
                    }
            }
            State state = States.GetOrAdd(keep.KeepID, static _ => new State());
            if (attackers == 0 && !state.Battle && state.Holder == keep.Realm && state.BestDoorsUp != int.MaxValue)
                continue;   // a quiet keep: nothing to count
            int engines = 0;
            if (attackers > 0)
                foreach (GameNPC npc in keep.CurrentRegion?.GetNPCsInRadius(new Point3D(keep.X, keep.Y, keep.Z), (ushort)AtKeepRadius) ??
                                        Enumerable.Empty<GameNPC>())
                    if (npc is GameSiegeWeapon && npc.Realm != keep.Realm)
                        engines++;
            var sample = new Sample(doors.Count(door => door.IsAlive), doors.Length,
                doors.Length == 0 ? 0 : doors.Min(door => door.HealthPercent), lord?.HealthPercent ?? 0, lord?.IsAlive == true,
                guards.Count(guard => guard.IsAlive), guards.Length, attackers, atDoor, defenders, engines, keep.Realm);
            List<string> rows;
            lock (state)
                rows = Step(state, sample, now);
            if (!Log.IsInfoEnabled)
                continue;
            foreach (string row in rows)
            {
                int cut = row.IndexOf(' ');
                Log.Info($"{row[..cut]} t={now} keep={RvrRecordText.Field(keep.Name)} id={keep.KeepID} siege=rvr-keep-{keep.KeepID} " +
                         $"region={keep.Region} battleground={(BattlegroundBrackets.IsBattlegroundRegion(keep.Region) ? "true" : "false")}" +
                         row[cut..]);
            }
        }
    }

    /// <summary>Stalls since the last call, by keep (RVR_HOUR).</summary>
    public static int TakeStalls()
    {
        int total = 0;
        foreach (State state in States.Values)
            lock (state)
            {
                total += state.Stalls;
                state.Stalls = 0;
            }
        return total;
    }

    /// <summary>The siege id of the nearest keep within <paramref name="radius"/> that is in a battle, else of any keep; or null.</summary>
    public static string NearestKeep(ushort region, int x, int y, int radius)
    {
        string best = null;
        double bestDistance = (double)radius * radius;
        foreach (var (id, place) in Places)
        {
            if (place.Region != region)
                continue;
            double dx = place.X - x, dy = place.Y - y, d = dx * dx + dy * dy;
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = $"rvr-keep-{id}";
            }
        }
        return best;
    }

    public static void Reset()
    {
        States.Clear();
        Places.Clear();
    }
}

/// <summary>
/// RVR_TELEPORT (issue #505; pathing.md 3): every move or snap of an autonomous gamebot in a war zone by
/// <see cref="GameBot.MoveTo"/> or a forced <see cref="GameNPC.MoveInRegion"/>, with the distance, who did it (the first
/// caller outside the object classes) and whether the player could have seen it. Logging only; under a counted budget.
/// </summary>
public static class RvrTeleports
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrTeleports));

    /// <summary>A shorter move within a region is a step, not a snap.</summary>
    public const int MinimumDistance = 250;
    public const int LinesPerMinute = 120;

    public static void Before(GameBot bot, ushort region, int x, int y, int z, string how)
    {
        if (bot == null || !bot.IsAutonomousWorldBot || !Log.IsInfoEnabled || bot.ObjectState != GameObject.eObjectState.Active)
            return;
        bool inWar = AutonomousRvrRecordScope.InWar(bot);
        bool crossing = region != bot.CurrentRegionID;
        if (!inWar && !(crossing && AutonomousRvrRecordScope.Follows(bot)))
            return;
        int distance = crossing ? -1 : (int)Math.Sqrt((double)(bot.X - x) * (bot.X - x) + (double)(bot.Y - y) * (bot.Y - y) +
                                                     (double)(bot.Z - z) * (bot.Z - z));
        if (!crossing && distance < MinimumDistance)
            return;
        long now = GameLoop.GameLoopTime;
        if (!RvrRecordBudget.Take("RVR_TELEPORT", now, LinesPerMinute))
            return;
        int fromSeen = RvrFights.PlayerDistance(bot.CurrentRegionID, bot.X, bot.Y);
        int toSeen = RvrFights.PlayerDistance(region, x, y);
        Log.Info(Line(now, bot.Name, bot.DatabaseID, bot.Realm, how, Caller(), bot.CurrentRegionID, bot.X, bot.Y, region, x, y,
            distance, fromSeen, toSeen, bot.IsAlive, bot.InCombat));
    }

    /// <summary>Whether the player could see a spot at that distance (-1: not in the region). Pure.</summary>
    public static string Seen(int fromDistance, int toDistance)
    {
        bool from = fromDistance >= 0 && fromDistance <= WorldMgr.VISIBILITY_DISTANCE;
        bool to = toDistance >= 0 && toDistance <= WorldMgr.VISIBILITY_DISTANCE;
        return from && to ? "both" : from ? "from" : to ? "to" : "none";
    }

    /// <summary>The row. Pure.</summary>
    public static string Line(long t, string bot, long id, eRealm realm, string how, string caller, ushort fromRegion, int fromX,
        int fromY, ushort toRegion, int toX, int toY, int distance, int playerFrom, int playerTo, bool alive, bool inCombat) =>
        $"RVR_TELEPORT t={t} bot={RvrRecordText.Field(bot)} id={id} realm={RvrRecordText.Realm(realm)} how={how} " +
        $"by={RvrRecordText.Field(caller)} from={fromRegion}:{fromX},{fromY} to={toRegion}:{toX},{toY} distance={distance} " +
        $"playerFrom={playerFrom} playerTo={playerTo} playerSaw={Seen(playerFrom, playerTo)} " +
        $"alive={(alive ? "true" : "false")} inCombat={(inCombat ? "true" : "false")}";

    /// <summary>The first method on the stack outside the object classes and this one ("Type.Method").</summary>
    private static string Caller()
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(2, false);
            for (int i = 0; i < trace.FrameCount && i < 12; i++)
            {
                System.Reflection.MethodBase method = trace.GetFrame(i)?.GetMethod();
                Type type = method?.DeclaringType;
                if (type == null || type == typeof(GameBot) || type == typeof(GameNPC) || type == typeof(GameLiving) ||
                    type == typeof(GameObject) || type == typeof(RvrTeleports))
                    continue;
                string name = type.Name;
                int angle = name.IndexOf('<');
                if (angle > 0)
                    name = name[..angle];
                if (type.DeclaringType != null && name.StartsWith("<", StringComparison.Ordinal))
                    name = type.DeclaringType.Name;
                return $"{name}.{method.Name}";
            }
        }
        catch (Exception)
        {
            // A trace is a nicety; the row is written without it.
        }
        return "unknown";
    }
}

/// <summary>
/// The record's pulse (issue #505): every <see cref="PulseMilliseconds"/> it ends quiet fights, samples the keeps and the
/// player; every five minutes the force-cluster sampler (RVR_FORCE_CLUSTER, #530); every hour RVR_HOUR per realm and
/// RVR_PLAYER_SEAT per player. A timer like the dashboard's; it reads the world and writes rows, nothing else.
/// </summary>
public static class RvrRecordPulse
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrRecordPulse));

    public const int PulseMilliseconds = 5_000;
    public const long ClusterMilliseconds = 5 * 60_000;
    public const long HourMilliseconds = 60 * 60_000;
    public const int ClusterRadius = 3_000;

    private static Timer _timer;
    private static int _running;
    private static long _nextCluster, _nextHour, _hourStart;
    private static readonly int[] Kills = new int[4], Deaths = new int[4], Captures = new int[4];
    private static readonly object CountSync = new();

    [GameServerStartedEvent]
    public static void Start(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        GameEventMgr.AddHandlerUnique(KeepEvent.KeepTaken, OnKeepTaken);
        GameEventMgr.AddHandlerUnique(GameLivingEvent.Dying, OnDying);
        long now = GameLoop.GameLoopTime;
        _hourStart = now;
        _nextHour = now + HourMilliseconds;
        _nextCluster = now + ClusterMilliseconds;
        RvrPlayerRecord.SampleMilliseconds = PulseMilliseconds;
        _timer = new Timer(_ => Pulse(), null, PulseMilliseconds, PulseMilliseconds);
    }

    [GameServerStoppedEvent]
    public static void Stop(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = null;
        GameEventMgr.RemoveHandler(KeepEvent.KeepTaken, OnKeepTaken);
        GameEventMgr.RemoveHandler(GameLivingEvent.Dying, OnDying);
        foreach (string line in RvrFights.Ledger.EndAll(GameLoop.GameLoopTime))
            if (Log.IsInfoEnabled)
                Log.Info(line);
    }

    private static void OnKeepTaken(DOLEvent e, object sender, EventArgs args)
    {
        if (args is KeepEventArgs { Keep: not null } taken)
            lock (CountSync)
                Captures[Math.Clamp((int)taken.Keep.Realm, 0, 3)]++;
    }

    private static void OnDying(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            GameObject killer = (args as DyingEventArgs)?.Killer;
            if (sender is GamePlayer player)
                RvrPlayerRecord.Died(player, killer);
        }
        catch (Exception exception)
        {
            Log.Warn("RvR record: a death", exception);
        }
    }

    /// <summary>A death between realms in a war zone, for the hour's counts (RVR_BOT_KILLED and RVR_PLAYER_KILLED feed it).</summary>
    public static void CountDeath(eRealm victim, eRealm killer)
    {
        lock (CountSync)
        {
            Deaths[Math.Clamp((int)victim, 0, 3)]++;
            Kills[Math.Clamp((int)killer, 0, 3)]++;
        }
    }

    private static void Pulse()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        try
        {
            long now = GameLoop.GameLoopTime;
            RvrFights.Sweep(now);
            GameBot[] bots = AutonomousBotRegistry.Snapshot();
            RvrSieges.Watch(now, bots);
            RvrPlayerRecord.Sample(now, bots);
            if (now >= _nextCluster)
            {
                _nextCluster = now + ClusterMilliseconds;
                if (Log.IsInfoEnabled)
                    foreach (string line in ClusterLines(now, bots))
                        Log.Info(line);
            }
            if (now >= _nextHour)
            {
                long minutes = Math.Max(1, (now - _hourStart) / 60_000);
                _hourStart = now;
                _nextHour = now + HourMilliseconds;
                if (Log.IsInfoEnabled)
                {
                    foreach (string line in HourLines(now, minutes, bots))
                        Log.Info(line);
                    foreach (string line in RvrPlayerRecord.TakeSeatLines(now))
                        Log.Info(line);
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warn("RvR record pulse", exception);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }

    /// <summary>The kind of a bot's party for the counts: solo, small (2-7) or eight (8+). Pure.</summary>
    public static string PartyKind(int members) => members <= 1 ? "solo" : members < 8 ? "small" : "eight";

    private static List<string> HourLines(long now, long minutes, GameBot[] bots)
    {
        int[] opened = RvrFights.Ledger.TakeOpenedPerRealm();
        RvrFights.Ledger.TakeEndedPerRealm();
        int stalls = RvrSieges.TakeStalls();
        string dropped = RvrRecordBudget.TakeDropped();
        int[] kills, deaths, captures;
        lock (CountSync)
        {
            kills = (int[])Kills.Clone();
            deaths = (int[])Deaths.Clone();
            captures = (int[])Captures.Clone();
            Array.Clear(Kills);
            Array.Clear(Deaths);
            Array.Clear(Captures);
        }
        ICollection<AbstractGameKeep> keeps = GameServer.KeepManager?.GetFrontierKeeps() ?? Array.Empty<AbstractGameKeep>();
        string topRules = string.Join(",", RvrDecide.Rules().OrderByDescending(p => p.Value).Take(5).Select(p => $"{p.Key}:{p.Value}"));
        var lines = new List<string>();
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            int r = (int)realm;
            var war = bots.Where(bot => bot != null && bot.IsAlive && bot.Realm == realm && AutonomousRvrRecordScope.InWar(bot)).ToArray();
            var kinds = war.GroupBy(bot => PartyKind(bot.Group?.MemberCount ?? 1)).ToDictionary(g => g.Key, g => g.Count());
            lines.Add($"RVR_HOUR t={now} minutes={minutes} realm={RvrRecordText.Realm(realm)} botsInWar={war.Length} " +
                      $"solo={kinds.GetValueOrDefault("solo")} small={kinds.GetValueOrDefault("small")} eight={kinds.GetValueOrDefault("eight")} " +
                      $"fights={opened[r]} kills={kills[r]} deaths={deaths[r]} captures={captures[r]} " +
                      $"keepsHeld={keeps.Count(keep => keep.Realm == realm && !keep.IsPortalKeep)} stalls={stalls} dropped={dropped} " +
                      $"topRules={(topRules.Length == 0 ? "none" : topRules)}");
            // #528 step 4: the realm rank spread of the realm's online gamebots, measured before any brake is decided.
            GameBot[] roster = bots.Where(bot => bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false } && bot.Realm == realm).ToArray();
            lines.Add(RanksLine(now, realm, roster.Select(bot => bot.RealmLevel).ToArray(),
                roster.Where(bot => bot.Level >= 50).Select(bot => bot.RealmLevel).ToArray()));
        }
        return lines;
    }

    /// <summary>The realm rank a realm level is (RR1L0 is level 0, RR2L0 level 10), capped at <see cref="TopRank"/>. Pure.</summary>
    public static int RankOf(int realmLevel) => Math.Clamp(realmLevel / 10 + 1, 1, TopRank);

    /// <summary>The highest rank the histogram keeps apart; anything above counts as it.</summary>
    public const int TopRank = 10;

    /// <summary>A realm level as players write it: 14 is "2L4". Pure.</summary>
    public static string RankText(int realmLevel) => $"{RankOf(realmLevel)}L{Math.Max(0, realmLevel) % 10}";

    /// <summary>The middle realm level (the lower middle for an even count), or -1 for none. Pure.</summary>
    public static int MedianLevel(IReadOnlyList<int> realmLevels)
    {
        if (realmLevels == null || realmLevels.Count == 0)
            return -1;
        int[] sorted = realmLevels.OrderBy(level => level).ToArray();
        return sorted[(sorted.Length - 1) / 2];
    }

    /// <summary>
    /// RVR_RANKS (#528 step 4; war.md finding 5): one realm's online autonomous gamebots by realm rank, every hour, with
    /// <c>rr1</c>..<c>rr10</c> counts, the median realm level of all of them and of the level 50s, and the highest. Companions
    /// and players are not in it. It measures only: whether bots out-rank the period (most RvR regulars RR2-RR4) is the
    /// owner's call once a played world prints it. Pure.
    /// </summary>
    public static string RanksLine(long now, eRealm realm, IReadOnlyList<int> realmLevels, IReadOnlyList<int> level50RealmLevels)
    {
        var counts = new int[TopRank + 1];
        foreach (int level in realmLevels)
            counts[RankOf(level)]++;
        int median = MedianLevel(realmLevels), median50 = MedianLevel(level50RealmLevels);
        return $"RVR_RANKS t={now} realm={RvrRecordText.Realm(realm)} bots={realmLevels.Count} rr1={counts[1]} rr2={counts[2]} " +
               $"rr3={counts[3]} rr4={counts[4]} rr5={counts[5]} rr6={counts[6]} rr7={counts[7]} rr8={counts[8]} rr9={counts[9]} " +
               $"rr10={counts[10]} median={(median < 0 ? "-" : RankText(median))} level50={level50RealmLevels.Count} " +
               $"median50={(median50 < 0 ? "-" : RankText(median50))} top={(realmLevels.Count == 0 ? "-" : RankText(realmLevels.Max()))}";
    }

    /// <summary>
    /// RVR_FORCE_CLUSTER per realm: its RvR parties in a war zone outside an event, joined when within
    /// <see cref="ClusterRadius"/> of each other (does a zerg form?).
    /// </summary>
    private static List<string> ClusterLines(long now, GameBot[] bots)
    {
        var lines = new List<string>();
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            var parties = bots.Where(bot => bot != null && bot.IsAlive && bot.Realm == realm && AutonomousRvrRecordScope.InWar(bot) &&
                                            string.IsNullOrEmpty(bot.TempProperties.GetProperty<string>("RvrEventForce")))
                .GroupBy(bot => (bot.CurrentRegionID, RvrFights.GroupOf(bot)))
                .Select(g => new ClusterParty(g.Key.CurrentRegionID, (int)g.Average(b => b.X), (int)g.Average(b => b.Y), g.Count()))
                .ToList();
            if (parties.Count == 0)
                continue;
            List<int> sizes = Clusters(parties, ClusterRadius);
            lines.Add($"RVR_FORCE_CLUSTER t={now} realm={RvrRecordText.Realm(realm)} parties={parties.Count} " +
                      $"bots={parties.Sum(p => p.Bots)} clusters={sizes.Count} biggest={sizes.Max()} " +
                      $"over24={sizes.Count(s => s > 24)}");
        }
        return lines;
    }

    public readonly record struct ClusterParty(ushort Region, int X, int Y, int Bots);

    /// <summary>Single-link clusters of parties within <paramref name="radius"/>: the bots in each, biggest first. Pure.</summary>
    public static List<int> Clusters(IReadOnlyList<ClusterParty> parties, int radius)
    {
        int n = parties.Count;
        int[] root = Enumerable.Range(0, n).ToArray();
        int Find(int i) => root[i] == i ? i : root[i] = Find(root[i]);
        double r2 = (double)radius * radius;
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
            {
                if (parties[i].Region != parties[j].Region)
                    continue;
                double dx = parties[i].X - parties[j].X, dy = parties[i].Y - parties[j].Y;
                if (dx * dx + dy * dy <= r2)
                    root[Find(i)] = Find(j);
            }
        return Enumerable.Range(0, n).GroupBy(Find).Select(g => g.Sum(i => parties[i].Bots)).OrderByDescending(s => s).ToList();
    }
}

/// <summary>RVR_ENGAGE and RVR_PLAN (issue #505): the threat scan's engage and each destination plan. Pure rows.</summary>
public static class RvrRecordRows
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrRecordRows));

    public static void Engage(GameBot bot, GameLiving target, string list, int visible, long fight)
    {
        if (bot == null || target == null || !Log.IsInfoEnabled || !RvrRecordBudget.Take("RVR_ENGAGE", GameLoop.GameLoopTime, 300))
            return;
        Log.Info($"RVR_ENGAGE t={GameLoop.GameLoopTime} bot={RvrRecordText.Field(bot.Name)} id={bot.DatabaseID} " +
                 $"realm={RvrRecordText.Realm(bot.Realm)} group={RvrFights.GroupOf(bot)} target={RvrRecordText.Who(target)} " +
                 $"targetKind={RvrRecordText.Kind(target)} list={list} visible={visible} fight={fight} " +
                 $"distance={(int)bot.GetDistanceTo(target)} region={bot.CurrentRegionID}" +
                 (target is GamePlayer player ? $" player={RvrRecordText.Field(player.Name)}" : string.Empty));
    }

    public static string PlanLine(long t, string bot, long id, eRealm realm, string chosen, string intent, string rule, int choices,
        int rejected, string reason) =>
        $"RVR_PLAN t={t} bot={RvrRecordText.Field(bot)} id={id} realm={RvrRecordText.Realm(realm)} chosen={RvrRecordText.Field(chosen ?? "none")} " +
        $"intent={intent} rule={rule} choices={choices} rejected={rejected} reason={RvrRecordText.Field(reason ?? string.Empty)}";

    public static void Plan(GameBot bot, string chosen, string intent, string rule, int choices, int rejected, string reason)
    {
        if (bot == null || !Log.IsInfoEnabled || !RvrRecordBudget.Take("RVR_PLAN", GameLoop.GameLoopTime, 300))
            return;
        Log.Info(PlanLine(GameLoop.GameLoopTime, bot.Name, bot.DatabaseID, bot.Realm, chosen, intent, rule, choices, rejected, reason));
    }
}
