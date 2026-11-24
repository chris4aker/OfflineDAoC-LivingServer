using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DOL.GS;

/// <summary>One side's member in a fight, as the ledger keeps it (plain values).</summary>
public readonly record struct RvrFightActor(long Key, string Group, eRealm Realm, bool IsPlayer)
{
    public bool IsValid => Key != 0 && Realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;
}

/// <summary>
/// The fight id (evidence.md §4 row 1): a fight is opened by the first engage, call or kill between realms in one place,
/// joined by every later one within <see cref="JoinRadius"/> of its centre while it is live, and ended once nothing has
/// happened in it for <see cref="IdleEndMilliseconds"/>. Writes RVR_FIGHT_OPEN and RVR_FIGHT_END. Pure apart from its own
/// state (no game objects, no randomness); thread-safe. <see cref="RvrFights"/> feeds it from the game.
/// </summary>
public sealed class RvrFightLedger
{
    public const int JoinRadius = 3_000;
    public const long IdleEndMilliseconds = 30_000;
    /// <summary>The longest a fight is kept open (a battle that never pauses is cut into pieces this long).</summary>
    public const long MaxFightMilliseconds = 30 * 60_000;

    private sealed class Fight
    {
        public long Id;
        public ushort Region;
        public double X, Y;
        public int Touches;
        public long OpenedAt, LastAt;
        public string OpenedBy;
        public readonly Dictionary<eRealm, HashSet<long>> Members = new();
        public readonly Dictionary<eRealm, HashSet<string>> Groups = new();
        public readonly int[] Deaths = new int[4];
        public long FirstDeathAt = -1, FirstCcAt = -1;
        public eRealm FirstDeathRealm;
        public eRealm LastRealm;
        public bool PlayerIn;
        public int PlayerNearest = int.MaxValue;
        public int PlayerDeaths, PlayerKills;
    }

    private readonly object _sync = new();
    private readonly List<Fight> _open = new();
    private readonly Dictionary<string, Fight> _byGroup = new(StringComparer.Ordinal);
    private long _nextId;

    /// <summary>Fights opened and ended since start, per realm taking part (for RVR_HOUR).</summary>
    private readonly int[] _opened = new int[4], _ended = new int[4];

    private static int R(eRealm realm) => Math.Clamp((int)realm, 0, 3);

    private Fight Find(ushort region, double x, double y, long now, string groupA, string groupB)
    {
        if (groupA != null && _byGroup.TryGetValue(groupA, out Fight a) && a.LastAt + IdleEndMilliseconds > now && a.Region == region)
            return a;
        if (groupB != null && _byGroup.TryGetValue(groupB, out Fight b) && b.LastAt + IdleEndMilliseconds > now && b.Region == region)
            return b;
        Fight best = null;
        double bestDistance = (double)JoinRadius * JoinRadius;
        foreach (Fight fight in _open)
        {
            if (fight.Region != region || fight.LastAt + IdleEndMilliseconds <= now)
                continue;
            double dx = fight.X - x, dy = fight.Y - y, d = dx * dx + dy * dy;
            if (d <= bestDistance)
            {
                bestDistance = d;
                best = fight;
            }
        }
        return best;
    }

    private void Add(Fight fight, RvrFightActor actor, long now)
    {
        if (!actor.IsValid)
            return;
        if (!fight.Members.TryGetValue(actor.Realm, out HashSet<long> members))
            fight.Members[actor.Realm] = members = new HashSet<long>();
        members.Add(actor.Key);
        if (actor.Group != null)
        {
            if (!fight.Groups.TryGetValue(actor.Realm, out HashSet<string> groups))
                fight.Groups[actor.Realm] = groups = new HashSet<string>(StringComparer.Ordinal);
            if (groups.Add(actor.Group))
                _byGroup[actor.Group] = fight;
            else
                _byGroup[actor.Group] = fight;
        }
        fight.PlayerIn |= actor.IsPlayer;
        fight.LastRealm = actor.Realm;
    }

    /// <summary>
    /// Something happened between <paramref name="a"/> and <paramref name="b"/> at (x, y): an engage, a call, a hit.
    /// Returns the fight id; <paramref name="openLine"/> is the RVR_FIGHT_OPEN row when this opened it, else null.
    /// <paramref name="playerDistance"/>: the human's distance from the spot (-1 when he is not in the region).
    /// </summary>
    public long Touch(ushort region, int x, int y, long now, string source, RvrFightActor a, RvrFightActor b,
        int playerDistance, out string openLine)
    {
        openLine = null;
        if (!a.IsValid || !b.IsValid || a.Realm == b.Realm)
            return 0;
        lock (_sync)
        {
            Fight fight = Find(region, x, y, now, a.Group, b.Group);
            if (fight == null)
            {
                fight = new Fight { Id = ++_nextId, Region = region, X = x, Y = y, OpenedAt = now, OpenedBy = source };
                _open.Add(fight);
                Add(fight, a, now);
                Add(fight, b, now);
                Note(fight, playerDistance);
                fight.LastAt = now;
                fight.Touches = 1;
                foreach (eRealm realm in fight.Members.Keys)
                    _opened[R(realm)]++;
                openLine = $"RVR_FIGHT_OPEN fight={fight.Id} region={region} x={x} y={y} by={source} " +
                           $"realms={RvrRecordText.Realm(a.Realm)},{RvrRecordText.Realm(b.Realm)} " +
                           $"groups={GroupsText(fight)} playerIn={(fight.PlayerIn ? "true" : "false")} " +
                           $"playerDistance={playerDistance}";
                return fight.Id;
            }
            bool newRealm = !fight.Members.ContainsKey(a.Realm) || !fight.Members.ContainsKey(b.Realm);
            Add(fight, a, now);
            Add(fight, b, now);
            if (newRealm)
                foreach (eRealm realm in new[] { a.Realm, b.Realm })
                    if (fight.Members[realm].Count == 1)
                        _opened[R(realm)]++;
            // The centre follows the fight (a running mean, so a moving fight keeps its id).
            fight.Touches++;
            double weight = 1.0 / Math.Min(fight.Touches, 20);
            fight.X += (x - fight.X) * weight;
            fight.Y += (y - fight.Y) * weight;
            fight.LastAt = now;
            Note(fight, playerDistance);
            return fight.Id;
        }
    }

    private static void Note(Fight fight, int playerDistance)
    {
        if (playerDistance >= 0 && playerDistance < fight.PlayerNearest)
            fight.PlayerNearest = playerDistance;
    }

    /// <summary>A death between realms at (x, y). Opens a fight when none is live there. Returns the fight id.</summary>
    public long Death(ushort region, int x, int y, long now, RvrFightActor victim, RvrFightActor killer, int playerDistance,
        out string openLine)
    {
        long id = Touch(region, x, y, now, "death", victim, killer, playerDistance, out openLine);
        if (id == 0)
            return 0;
        lock (_sync)
        {
            Fight fight = _open.FirstOrDefault(f => f.Id == id);
            if (fight == null)
                return id;
            fight.Deaths[R(victim.Realm)]++;
            if (fight.FirstDeathAt < 0)
            {
                fight.FirstDeathAt = now;
                fight.FirstDeathRealm = victim.Realm;
            }
            if (victim.IsPlayer)
                fight.PlayerDeaths++;
            if (killer.IsPlayer)
                fight.PlayerKills++;
            // The side still standing is the one that killed last.
            fight.LastRealm = killer.Realm;
        }
        return id;
    }

    /// <summary>A crowd-control job or cast in <paramref name="group"/>'s fight (the fight's first CC).</summary>
    public void CrowdControl(string group, long now)
    {
        if (group == null)
            return;
        lock (_sync)
            if (_byGroup.TryGetValue(group, out Fight fight) && fight.LastAt + IdleEndMilliseconds > now && fight.FirstCcAt < 0)
                fight.FirstCcAt = now;
    }

    /// <summary>The live fight of a group, or 0.</summary>
    public long IdOf(string group, long now)
    {
        if (group == null)
            return 0;
        lock (_sync)
            return _byGroup.TryGetValue(group, out Fight fight) && fight.LastAt + IdleEndMilliseconds > now &&
                   _open.Contains(fight) ? fight.Id : 0;
    }

    /// <summary>The live fight nearest (x, y) in a region within <paramref name="radius"/>, or 0 (/odd).</summary>
    public long Nearest(ushort region, int x, int y, long now, int radius)
    {
        lock (_sync)
        {
            long best = 0;
            double bestDistance = (double)radius * radius;
            foreach (Fight fight in _open)
            {
                if (fight.Region != region || fight.LastAt + IdleEndMilliseconds * 4 <= now)
                    continue;
                double dx = fight.X - x, dy = fight.Y - y, d = dx * dx + dy * dy;
                if (d <= bestDistance)
                {
                    bestDistance = d;
                    best = fight.Id;
                }
            }
            return best;
        }
    }

    /// <summary>The live fights within <paramref name="radius"/> of (x, y) in a region, by id (what the player could see).</summary>
    public List<long> InView(ushort region, int x, int y, long now, int radius)
    {
        var ids = new List<long>();
        lock (_sync)
            foreach (Fight fight in _open)
            {
                if (fight.Region != region || fight.LastAt + IdleEndMilliseconds <= now)
                    continue;
                double dx = fight.X - x, dy = fight.Y - y;
                if (dx * dx + dy * dy <= (double)radius * radius)
                    ids.Add(fight.Id);
            }
        ids.Sort();
        return ids;
    }

    /// <summary>Fights that ended since start, per realm taking part, since the last call (resets).</summary>
    public int[] TakeEndedPerRealm()
    {
        lock (_sync)
        {
            int[] copy = (int[])_ended.Clone();
            Array.Clear(_ended);
            return copy;
        }
    }

    /// <summary>Ends the fights with nothing new for <see cref="IdleEndMilliseconds"/>; returns their RVR_FIGHT_END rows.</summary>
    public List<string> Sweep(long now)
    {
        var lines = new List<string>();
        lock (_sync)
        {
            for (int i = _open.Count - 1; i >= 0; i--)
            {
                Fight fight = _open[i];
                bool idle = fight.LastAt + IdleEndMilliseconds <= now;
                bool tooLong = now - fight.OpenedAt >= MaxFightMilliseconds;
                if (!idle && !tooLong)
                    continue;
                _open.RemoveAt(i);
                foreach (string group in fight.Groups.Values.SelectMany(g => g))
                    if (_byGroup.TryGetValue(group, out Fight mapped) && mapped == fight)
                        _byGroup.Remove(group);
                foreach (eRealm realm in fight.Members.Keys)
                    _ended[R(realm)]++;
                lines.Add(EndLine(fight, idle ? "quiet" : "cut"));
            }
        }
        return lines;
    }

    /// <summary>Ends every live fight (shutdown, tests).</summary>
    public List<string> EndAll(long now)
    {
        lock (_sync)
            foreach (Fight fight in _open)
                fight.LastAt = Math.Min(fight.LastAt, now - IdleEndMilliseconds);
        return Sweep(now);
    }

    /// <summary>Fights opened that a realm took part in since the last call (resets).</summary>
    public int[] TakeOpenedPerRealm()
    {
        lock (_sync)
        {
            int[] copy = (int[])_opened.Clone();
            Array.Clear(_opened);
            return copy;
        }
    }

    public int OpenCount
    {
        get { lock (_sync) return _open.Count; }
    }

    private static string GroupsText(Fight fight) => string.Join(";", fight.Groups.OrderBy(p => p.Key)
        .Select(p => $"{RvrRecordText.Realm(p.Key)}:{string.Join("+", p.Value.OrderBy(g => g, StringComparer.Ordinal))}"));

    /// <summary>
    /// Who won: the one realm that lost nobody while the other lost someone; else the realm with the fewest deaths
    /// (against the most); "even" on a tie; "none" when nobody died. Who held the field: the realm that acted last
    /// (killed last, or was last seen fighting), unless every member of it seen in the fight died. Pure.
    /// </summary>
    public static (string Winner, string Held) Outcome(IReadOnlyDictionary<eRealm, int> sizes, IReadOnlyDictionary<eRealm, int> deaths,
        eRealm last)
    {
        int total = deaths.Values.Sum();
        string winner;
        if (total == 0)
            winner = "none";
        else
        {
            var ranked = sizes.Keys.OrderBy(realm => deaths.GetValueOrDefault(realm)).ToList();
            int fewest = deaths.GetValueOrDefault(ranked[0]);
            winner = ranked.Count(realm => deaths.GetValueOrDefault(realm) == fewest) > 1 ? "even" : RvrRecordText.Realm(ranked[0]);
        }
        bool lastWiped = sizes.TryGetValue(last, out int lastSize) && deaths.GetValueOrDefault(last) >= lastSize;
        eRealm held = last;
        if (lastWiped || !sizes.ContainsKey(last))
            held = sizes.Keys.Where(realm => deaths.GetValueOrDefault(realm) < sizes[realm])
                .OrderBy(realm => deaths.GetValueOrDefault(realm)).FirstOrDefault();
        return (winner, held == eRealm.None ? "none" : RvrRecordText.Realm(held));
    }

    private static string EndLine(Fight fight, string why)
    {
        var sizes = fight.Members.ToDictionary(p => p.Key, p => p.Value.Count);
        var deaths = fight.Members.Keys.ToDictionary(realm => realm, realm => fight.Deaths[R(realm)]);
        (string winner, string held) = Outcome(sizes, deaths, fight.LastRealm);
        var text = new StringBuilder(220);
        text.Append("RVR_FIGHT_END fight=").Append(fight.Id).Append(" region=").Append(fight.Region)
            .Append(" x=").Append((int)fight.X).Append(" y=").Append((int)fight.Y)
            .Append(" seconds=").Append(Math.Max(0, (fight.LastAt - fight.OpenedAt) / 1000))
            .Append(" by=").Append(fight.OpenedBy).Append(" end=").Append(why)
            .Append(" size=").Append(sizes.Values.Sum())
            .Append(" sizes=").Append(string.Join(",", sizes.OrderBy(p => p.Key).Select(p => $"{RvrRecordText.Realm(p.Key)}:{p.Value}")))
            .Append(" deaths=").Append(string.Join(",", deaths.OrderBy(p => p.Key).Select(p => $"{RvrRecordText.Realm(p.Key)}:{p.Value}")))
            .Append(" groups=").Append(GroupsText(fight))
            .Append(" firstDeathS=").Append(fight.FirstDeathAt < 0 ? "none" : ((fight.FirstDeathAt - fight.OpenedAt) / 1000).ToString())
            .Append(" firstDeathRealm=").Append(fight.FirstDeathAt < 0 ? "none" : RvrRecordText.Realm(fight.FirstDeathRealm))
            .Append(" firstCcS=").Append(fight.FirstCcAt < 0 ? "none" : ((fight.FirstCcAt - fight.OpenedAt) / 1000).ToString())
            .Append(" winner=").Append(winner).Append(" held=").Append(held)
            .Append(" playerIn=").Append(fight.PlayerIn ? "true" : "false")
            .Append(" playerDeaths=").Append(fight.PlayerDeaths).Append(" playerKills=").Append(fight.PlayerKills)
            .Append(" playerNearest=").Append(fight.PlayerNearest == int.MaxValue ? -1 : fight.PlayerNearest);
        return text.ToString();
    }

    /// <summary>Tests.</summary>
    public void Clear()
    {
        lock (_sync)
        {
            _open.Clear();
            _byGroup.Clear();
            _nextId = 0;
            Array.Clear(_opened);
            Array.Clear(_ended);
        }
    }
}

/// <summary>
/// The game side of the fight ledger: turns bots, players and their pets into <see cref="RvrFightActor"/>s, gives the
/// human's distance, writes the rows and stamps <c>fight=</c> on the field rows (RVR_STANCE, RVR_ASSIST_*, RVR_CC_ASSIGN,
/// RVR_PEEL, RVR_KITE, RVR_BOT_KILLED). Reads the world only.
/// </summary>
public static class RvrFights
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(RvrFights));

    public static readonly RvrFightLedger Ledger = new();

    /// <summary>The living that counts for a pet: its owner.</summary>
    private static GameLiving Root(GameLiving living)
    {
        if (living is GameNPC { Brain: AI.Brain.IControlledBrain controlled } && controlled.GetLivingOwner() is { } owner)
            return owner;
        return living;
    }

    /// <summary>The group name the rows use: the bot group's leader id (as RVR_FIGHT_BOARD), "player" for the human's own.</summary>
    public static string GroupOf(GameLiving living)
    {
        living = Root(living);
        switch (living)
        {
            case GamePlayer:
                return "player";
            case GameBot bot:
            {
                if (bot.Group?.LivingLeader is GamePlayer)
                    return "player";
                GameLiving leader = bot.Group?.LivingLeader ?? bot;
                return leader is GameBot leaderBot ? leaderBot.DatabaseID.ToString() : leader?.ObjectID.ToString();
            }
            default:
                return null;
        }
    }

    public static RvrFightActor Actor(GameLiving living)
    {
        living = Root(living);
        return living switch
        {
            GamePlayer player => new RvrFightActor(-(long)player.ObjectID - 1, "player", player.Realm, true),
            GameBot bot => new RvrFightActor(bot.DatabaseID > 0 ? bot.DatabaseID : -(long)bot.ObjectID - 1_000_000, GroupOf(bot), bot.Realm, false),
            _ => default,
        };
    }

    /// <summary>The human's distance from a spot in a region, -1 when no player is in it. Cheap: one player online.</summary>
    public static int PlayerDistance(ushort region, int x, int y)
    {
        int best = -1;
        foreach (GamePlayer player in ClientService.Instance.GetPlayers())
        {
            if (player?.CurrentRegionID != region || player.ObjectState != GameObject.eObjectState.Active)
                continue;
            int d = (int)Math.Sqrt((double)(player.X - x) * (player.X - x) + (double)(player.Y - y) * (player.Y - y));
            if (best < 0 || d < best)
                best = d;
        }
        return best;
    }

    private static bool Counts(GameLiving a, GameLiving b) =>
        a != null && b != null && a.Realm != b.Realm && Actor(a).IsValid && Actor(b).IsValid &&
        AutonomousRvrRecordScope.InWar(a);

    /// <summary>An engage, call or hit between <paramref name="actor"/> and <paramref name="enemy"/>; returns the fight id.</summary>
    public static long Touch(GameLiving actor, GameLiving enemy, string source)
    {
        if (!Counts(actor, enemy))
            return 0;
        int x = (actor.X + enemy.X) / 2, y = (actor.Y + enemy.Y) / 2;
        long id = Ledger.Touch(actor.CurrentRegionID, x, y, GameLoop.GameLoopTime, source, Actor(actor), Actor(enemy),
            PlayerDistance(actor.CurrentRegionID, x, y), out string open);
        if (open != null && Log.IsInfoEnabled)
            Log.Info(open);
        return id;
    }

    /// <summary>A board with a call or an enemy on the group: the group is fighting.</summary>
    public static void OnBoard(GameLiving asker, RvrFightBoard board)
    {
        if (asker == null || board == null || board.Enemies.IsDefaultOrEmpty)
            return;
        GameLiving enemy = board.Plan?.CalledTarget;
        if (enemy == null)
            foreach (RvrCombatant combatant in board.Enemies)
                if (combatant.TargetsGroup && combatant.Living != null)
                {
                    enemy = combatant.Living;
                    break;
                }
        if (enemy != null)
            Touch(asker, enemy, "board");
    }

    /// <summary>A death between realms (a bot or the human); returns the fight id for the death row.</summary>
    public static long Death(GameLiving victim, GameLiving killer)
    {
        killer = Root(killer);
        if (!Counts(victim, killer))
            return 0;
        long id = Ledger.Death(victim.CurrentRegionID, victim.X, victim.Y, GameLoop.GameLoopTime, Actor(victim), Actor(killer),
            PlayerDistance(victim.CurrentRegionID, victim.X, victim.Y), out string open);
        if (open != null && Log.IsInfoEnabled)
            Log.Info(open);
        return id;
    }

    /// <summary>The live fight id of a living's group, 0 for none: the <c>fight=</c> stamp.</summary>
    public static long IdOf(GameLiving living) => Ledger.IdOf(GroupOf(living), GameLoop.GameLoopTime);

    /// <summary>The <c>fight=</c> stamp for a group named as the field rows name it (its leader's id).</summary>
    public static long IdOfGroup(string group) => Ledger.IdOf(group, GameLoop.GameLoopTime);

    public static void CrowdControl(GameLiving caster) => Ledger.CrowdControl(GroupOf(caster), GameLoop.GameLoopTime);

    /// <summary>The pulse: ends quiet fights.</summary>
    public static void Sweep(long now)
    {
        foreach (string line in Ledger.Sweep(now))
            if (Log.IsInfoEnabled)
                Log.Info(line);
    }
}

/// <summary>Which livings the record follows: in a frontier, a battleground or a neutral RvR dungeon.</summary>
public static class AutonomousRvrRecordScope
{
    public static bool InWar(GameLiving living) => living != null && AutonomousWorldBotController.IsBattleground(living);

    /// <summary>An autonomous gamebot on RvR or battleground duty, or one standing in a war zone.</summary>
    public static bool Follows(GameBot bot) => bot != null && bot.IsAutonomousWorldBot &&
        (AutonomousObjectiveAssignments.KindFor(bot) is eAutonomousObjectiveKind.RvR or eAutonomousObjectiveKind.Battleground ||
         InWar(bot));
}
