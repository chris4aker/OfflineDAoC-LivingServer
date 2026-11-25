using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS;

/// <summary>
/// #617 (and FarmVibes' crowd-control finding on #600): how an autonomous gamebot in a battleground picks among the enemies
/// its frontier scan found. Battlegrounds only; field RvR, companions and the player keep their picks.
/// - People first: enemy players, gamebots and pets rank above siege engines; an engine is taken only when no enemy person
///   is left among the candidates (OpenDAoC's keep guards never aggro an engine on sight; True165's kill order puts
///   engines last).
/// - An attacking siege crew leaves its engine when an enemy person comes within <see cref="CrewFightRadius"/>
///   (<see cref="SiegeCrewReleases"/>, beside AutonomousWorldBotController.SiegeOperatorMayEngage).
/// - A mezzed or breakably rooted enemy is passed over while an uncontrolled one is in the fight, and taken when it is the
///   only one (BotBrain.CrowdControlRespect, BotPvpCrowdControl.Protected; period: "call the mez, hold damage").
/// All pure.
/// </summary>
public static class AutonomousBattlegroundTargets
{
    /// <summary>How near an enemy person must come for an attacking crew to leave its engine: the gatherers' self-defence
    /// radius (AutonomousBattlegroundWave.GatherSelfDefenseRadius), about a caster's reach.</summary>
    public const int CrewFightRadius = 1_000;

    /// <summary>How long a crew that left its engine waits before it may take a siege job again.</summary>
    public const long CrewRetakeMilliseconds = 10_000;

    /// <summary>How long a battleground gamebot counts an enemy person it saw as "in sight" (no maintenance pulse).</summary>
    public const long EnemyInSightMilliseconds = 5_000;

    /// <summary>The radius around a bot in which an uncontrolled enemy counts as "in the fight" for the controller's picks
    /// (BotPvpCrowdControl.FreeEnemyRadius).</summary>
    public const ushort FreeEnemyRadius = BotPvpCrowdControl.FreeEnemyRadius;

    /// <summary>The scan's ordering key: 1 for an engine in a battleground, 0 for everything else (and always 0 outside a
    /// battleground, so the field order is unchanged).</summary>
    public static int Rank(bool battleground, bool isEngine) => battleground && isEngine ? 1 : 0;

    /// <summary>The candidates without the engines while any non-engine is among them; all of them otherwise.</summary>
    public static T[] PeopleFirst<T>(T[] candidates, Func<T, bool> isEngine)
    {
        if (candidates == null || candidates.Length == 0)
            return candidates;
        List<T> people = null;
        foreach (T candidate in candidates)
            if (!isEngine(candidate))
                (people ??= new List<T>(candidates.Length)).Add(candidate);
        return people == null || people.Count == candidates.Length ? candidates : people.ToArray();
    }

    /// <summary>The candidates without the crowd-controlled ones while any uncontrolled one is among them; all of them when
    /// every one is controlled (the bots then break one to go on, as a player does).</summary>
    public static T[] PassOverControlled<T>(T[] candidates, Func<T, bool> controlled)
    {
        if (candidates == null || candidates.Length == 0)
            return candidates;
        List<T> free = null;
        foreach (T candidate in candidates)
            if (!controlled(candidate))
                (free ??= new List<T>(candidates.Length)).Add(candidate);
        return free == null || free.Count == candidates.Length ? candidates : free.ToArray();
    }

    /// <summary>
    /// The battleground arrangement of the scan's candidates before the distributed pick: people first, then the
    /// crowd-controlled ones passed over. Outside a battleground the same array comes back, untouched.
    /// </summary>
    public static T[] Arrange<T>(bool battleground, T[] candidates, Func<T, bool> isEngine, Func<T, bool> controlled) =>
        battleground ? PassOverControlled(PeopleFirst(candidates, isEngine), controlled) : candidates;

    /// <summary>
    /// The attacker clause beside SiegeOperatorMayEngage: an attacking siege crew (holding a job, not defending) in a
    /// battleground takes an enemy person (player, gamebot or pet; never an engine) within <see cref="CrewFightRadius"/>,
    /// and leaves its engine to do it. False outside a battleground.
    /// </summary>
    public static bool SiegeCrewReleases(bool battleground, bool crew, bool defending, bool isPerson, double distance) =>
        battleground && crew && !defending && isPerson && distance <= CrewFightRadius;

    /// <summary>#617 (FarmVibes' review 1): whether a battleground attacker already hitting an enemy engine still runs the
    /// scan (past the "already fighting" gate), so an enemy person in reach takes it off the engine. Defenders keep their
    /// own engine rule; outside a battleground nothing changes.</summary>
    public static bool EngineAttackerScans(bool battleground, bool defending, bool onEnemyEngine) =>
        battleground && !defending && onEnemyEngine;

    /// <summary>#617 (FarmVibes' review 2): what a controller pick becomes. A controlled pick with a free enemy in the
    /// fight becomes that free enemy (never null, so the bot does not fall back to the gate while someone is free);
    /// otherwise the pick stands.</summary>
    public static T ReplaceControlledPick<T>(T pick, bool controlled, T freeEnemy) where T : class =>
        controlled && freeEnemy != null ? freeEnemy : pick;

    /// <summary>Whether a controller pick passes over this target: it is crowd-controlled and an uncontrolled enemy is in
    /// the fight (BotBrain.PassOverControlled).</summary>
    public static bool SkipsControlledPick(bool controlled, bool freeEnemyInFight) =>
        BotBrain.PassOverControlled(controlled, freeEnemyInFight);

    /// <summary>Whether no maintenance or self-buff pulse runs: an autonomous gamebot in a battleground with an enemy in
    /// sight. Companions, the player's group and the field keep their upkeep.</summary>
    public static bool HoldsMaintenance(bool autonomousGamebot, bool battleground, bool enemyInSight) =>
        autonomousGamebot && battleground && enemyInSight;
}

/// <summary>
/// #617 part 2: the rally reacts as a group. A bot holding at its realm's battleground rally (the wave's gather staging or
/// the keep rally outside the breach) that sees an enemy person heading for the rally, or fighting a realm-mate near it,
/// posts the contact here; every bot holding at that rally takes the posted contacts on its next scan, past the
/// gatherers' "only what comes at me" filter (AutonomousBattlegroundWave.GathererMayEngage), so the rally engages
/// together instead of standing still until the enemy is upon it. Per region and realm; thread-safe.
/// </summary>
public static class AutonomousBattlegroundRallyContact
{
    /// <summary>How far from the rally an enemy heading for it is a contact.</summary>
    public const float ContactRadius = 2_000f;

    /// <summary>How near the rally a realm-mate under attack makes its attacker a contact
    /// (AutonomousBattlegroundWave.GatherDefendRadius).</summary>
    public const float FightRadius = 1_500f;

    /// <summary>How far an enemy's heading may turn from the rally and still count as heading for it: 60 degrees, in the
    /// game's 4,096 heading units.</summary>
    public const int ApproachTolerance = 683;

    /// <summary>How long a posted contact stands.</summary>
    public const long ContactMilliseconds = 8_000;

    /// <summary>Two rally points this near each other are one rally.</summary>
    public const float SameRallyRadius = 600f;

    /// <summary>Whether an enemy moving on <paramref name="heading"/> is heading for a point that lies on
    /// <paramref name="headingToRally"/> from it (both in 4,096 units). Pure.</summary>
    public static bool Approaching(bool moving, ushort heading, ushort headingToRally)
    {
        if (!moving)
            return false;
        int difference = Math.Abs(heading % 4096 - headingToRally % 4096);
        return Math.Min(difference, 4096 - difference) <= ApproachTolerance;
    }

    /// <summary>
    /// Whether a sighted enemy is a contact for the rally: an enemy person within <see cref="ContactRadius"/> heading for
    /// it, or one fighting a realm-mate within <see cref="FightRadius"/> of it. Engines and keep pieces never are. Pure.
    /// </summary>
    public static bool IsContact(bool person, float targetToRally, bool approaching, bool fightingRealmMate, float victimToRally) =>
        person && (targetToRally <= ContactRadius && approaching || fightingRealmMate && victimToRally <= FightRadius);

    private sealed class Contact
    {
        public WeakReference<GameLiving> Target;
        public Vector3 Rally;
        public long Until;
    }

    private static readonly Dictionary<(ushort Region, eRealm Realm), List<Contact>> Boards = new();

    /// <summary>Posts (or refreshes) a contact for the rally at <paramref name="rally"/>. True when it is news.</summary>
    public static bool Post(ushort region, eRealm realm, Vector3 rally, GameLiving target, long now)
    {
        if (target == null)
            return false;
        lock (Boards)
        {
            if (!Boards.TryGetValue((region, realm), out List<Contact> board))
                Boards[(region, realm)] = board = new List<Contact>(8);
            board.RemoveAll(contact => contact.Until <= now || !contact.Target.TryGetTarget(out _));
            foreach (Contact contact in board)
                if (contact.Target.TryGetTarget(out GameLiving known) && ReferenceEquals(known, target) &&
                    Vector3.DistanceSquared(contact.Rally, rally) <= SameRallyRadius * SameRallyRadius)
                {
                    contact.Until = now + ContactMilliseconds;
                    return false;
                }
            if (board.Count >= 64)
                board.RemoveAt(0);
            board.Add(new Contact { Target = new(target), Rally = rally, Until = now + ContactMilliseconds });
            return true;
        }
    }

    /// <summary>The live contacts posted for the rally at <paramref name="rally"/> (alive ones only).</summary>
    public static List<GameLiving> Contacts(ushort region, eRealm realm, Vector3 rally, long now)
    {
        var result = new List<GameLiving>();
        lock (Boards)
        {
            if (!Boards.TryGetValue((region, realm), out List<Contact> board))
                return result;
            foreach (Contact contact in board)
                if (contact.Until > now && contact.Target.TryGetTarget(out GameLiving target) && target.IsAlive &&
                    Vector3.DistanceSquared(contact.Rally, rally) <= SameRallyRadius * SameRallyRadius && !result.Contains(target))
                    result.Add(target);
        }
        return result;
    }

    /// <summary>Whether <paramref name="target"/> is a live contact of the rally at <paramref name="rally"/>.</summary>
    public static bool Has(ushort region, eRealm realm, Vector3 rally, GameLiving target, long now) =>
        target != null && Contacts(region, realm, rally, now).Contains(target);

    /// <summary>Forgets every contact (tests).</summary>
    public static void Clear()
    {
        lock (Boards)
            Boards.Clear();
    }
}
