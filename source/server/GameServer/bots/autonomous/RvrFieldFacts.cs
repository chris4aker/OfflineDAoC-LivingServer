using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// What the fight board does not hold about one enemy and the work package 2 rules need (pure record): reachability,
/// the switch triggers and the crowd-control immunities the server would apply.
/// </summary>
public readonly record struct RvrEnemyFacts
{
    /// <summary>A member of the group dropped it as unreachable or stalemated (the controller's IgnoresTarget).</summary>
    public bool Unreachable { get; init; }
    /// <summary>Up on a keep or tower wall (the rule AutonomousRvrDefense.HoldWall uses: above the doors, near a keep).</summary>
    public bool OnWall { get; init; }
    /// <summary>Mastery of Concentration is up: "If you see a caster use MoC kill it and kill it fast" (FH Aug 2003).</summary>
    public bool BigSpell { get; init; }
    /// <summary>Back on its feet in the last few seconds: "re-kill whoever was just instantly resurrected" (FH Aug 2003).</summary>
    public bool JustResurrected { get; init; }
    /// <summary>The server would refuse even the group's longest mez (immunity effect, ability, or the enraged rule).</summary>
    public bool MezImmune { get; init; }
    public bool StunImmune { get; init; }
    public bool RootImmune { get; init; }

    public bool Rising => BigSpell || JustResurrected;
    public bool CannotBeCalled => Unreachable || OnWall;
}

/// <summary>What the board does not hold about one member (pure record): its tools for crowd control and interrupts.</summary>
public readonly record struct RvrMemberFacts
{
    public bool IsLeader { get; init; }
    public bool CanMez { get; init; }
    /// <summary>Has an area mez.</summary>
    public bool AoeMez { get; init; }
    public bool CanRoot { get; init; }
    public bool CanStun { get; init; }
    /// <summary>A dedicated archer with a bow.</summary>
    public bool Ranged { get; init; }
    /// <summary>An instant harmful spell that is not crowd control.</summary>
    public bool InstantSpell { get; init; }
    /// <summary>A harmful spell of at most <see cref="RvrInterruptPlan.FastCastMilliseconds"/>.</summary>
    public bool FastSpell { get; init; }
    public bool HasPet { get; init; }
    /// <summary>A support-only bot (BotPartyRoles.IsSupport): it heals and controls, it does not attack.</summary>
    public bool SupportOnly { get; init; }
    /// <summary>0 to 1 (AutonomousRvrPerception.Boldness; 0.5 with personalities off).</summary>
    public double Boldness { get; init; }
    /// <summary>When the member was first seen mezzed or stunned in its present spell of it (0: not controlled).</summary>
    public long ControlledSince { get; init; }

    public bool CanControl => CanMez || CanRoot || CanStun;
}

/// <summary>
/// The facts for one board, keyed by <see cref="RvrCombatant.Key"/> (pure record). The rules read it; only
/// <see cref="RvrFieldReader"/> makes it from the game. A combatant with no entry has default facts.
/// </summary>
public sealed record RvrFieldFacts
{
    public ImmutableDictionary<long, RvrEnemyFacts> Enemies { get; init; } = ImmutableDictionary<long, RvrEnemyFacts>.Empty;
    public ImmutableDictionary<long, RvrMemberFacts> Members { get; init; } = ImmutableDictionary<long, RvrMemberFacts>.Empty;

    public static RvrFieldFacts Empty { get; } = new();

    public RvrEnemyFacts Enemy(RvrCombatant enemy) =>
        enemy != null && Enemies.TryGetValue(enemy.Key, out RvrEnemyFacts facts) ? facts : default;

    public RvrMemberFacts Member(RvrCombatant member) =>
        member != null && Members.TryGetValue(member.Key, out RvrMemberFacts facts) ? facts : new RvrMemberFacts { Boldness = 0.5 };
}

/// <summary>
/// Reads a board's <see cref="RvrFieldFacts"/> from the game (work package 2) and keeps the little the group remembers
/// between boards: enemies members found unreachable, enemies the group killed (to see them resurrected), members
/// under crowd control since when, and the log clocks. Not pure; the rules that use the facts are. Every read is of
/// state the board builder already reads from other threads (effects lists are locked; spell lists are read by index
/// and a concurrent re-sort only skips a member for one board).
/// </summary>
public static class RvrFieldReader
{
    /// <summary>How long a member's "unreachable" report stands (AutonomousWorldBotController's unreachable ignore: 2 minutes).</summary>
    public const long UnreachableMilliseconds = 2 * 60_000;
    /// <summary>A killed enemy seen alive again within this time was resurrected ...</summary>
    public const long ResurrectWindowMilliseconds = 60_000;
    /// <summary>... and counts as "just resurrected" for this long after it is seen again.</summary>
    public const long JustResurrectedMilliseconds = 10_000;
    /// <summary>How far above the group's lowest member an enemy must be before the wall test runs (HoldWall: doors + 100).</summary>
    public const int WallHeight = 100;
    /// <summary>HoldWall's keep radius.</summary>
    public const int WallKeepRadius = 1_800;

    /// <summary>One group's memory. Every member's brain and the board builder use it, under its own lock.</summary>
    public sealed class Memory
    {
        internal readonly object Lock = new();
        internal readonly Dictionary<long, long> UnreachableUntil = new();
        internal readonly Dictionary<long, long> KilledAt = new();
        internal readonly Dictionary<long, long> ResurrectedAt = new();
        internal readonly Dictionary<long, long> ControlledSince = new();
        internal GameLiving LoggedCall;
        internal long LoggedCallAt = long.MinValue / 2;
        internal long LastCallLogAt = long.MinValue / 2;
    }

    private static readonly ConditionalWeakTable<object, Memory> Memories = new();
    private static readonly ConditionalWeakTable<RvrFightBoard, RvrFieldFacts> ByBoard = new();

    public static Memory MemoryOf(object groupKey) => groupKey == null ? null : Memories.GetValue(groupKey, static _ => new Memory());

    /// <summary>The group key of a board: that of its first member with a live object (RvrFightBoards.KeyOf), or null.</summary>
    public static object GroupKeyOf(RvrFightBoard board)
    {
        if (board != null)
            foreach (RvrCombatant member in board.Members)
                if (member?.Living != null && !member.IsPet)
                    return RvrFightBoards.KeyOf(member.Living);
        return null;
    }

    public static long KeyOf(GameLiving living) => living == null ? 0 : ((long)living.CurrentRegionID << 32) | (uint)living.ObjectID;

    /// <summary>A member's brain reports that it will not fight <paramref name="enemy"/> (IgnoresTarget): the group never calls it.</summary>
    public static void ReportUnreachable(object groupKey, GameLiving enemy, long now)
    {
        Memory memory = MemoryOf(groupKey);
        if (memory == null || enemy == null)
            return;
        lock (memory.Lock)
            memory.UnreachableUntil[KeyOf(enemy)] = now + UnreachableMilliseconds;
    }

    /// <summary>The previous call is dead: noted, so that seeing it alive again marks it resurrected.</summary>
    public static void NoteKilled(object groupKey, GameLiving enemy, long now)
    {
        Memory memory = MemoryOf(groupKey);
        if (memory == null || enemy == null)
            return;
        lock (memory.Lock)
            memory.KilledAt[KeyOf(enemy)] = now;
    }

    /// <summary>
    /// The facts of <paramref name="board"/>, read once per board (cached on the board object). The first reader passes
    /// the call of the plan before it: when that enemy is dead, the group remembers the kill (to see a resurrection).
    /// </summary>
    public static RvrFieldFacts Of(RvrFightBoard board, GameLiving previousCall = null)
    {
        if (board == null)
            return RvrFieldFacts.Empty;
        if (ByBoard.TryGetValue(board, out RvrFieldFacts cached))
            return cached;
        RvrFieldFacts facts = Read(board, previousCall);
        return ByBoard.GetValue(board, _ => facts);
    }

    private static RvrFieldFacts Read(RvrFightBoard board, GameLiving previousCall)
    {
        object groupKey = GroupKeyOf(board);
        if (groupKey == null)
            return RvrFieldFacts.Empty;
        Memory memory = MemoryOf(groupKey);
        long now = board.BuiltAt;
        if (previousCall != null && !previousCall.IsAlive)
            NoteKilled(groupKey, previousCall, now);

        int mezDuration = 0, stunDuration = 0;
        GameLiving boardCaster = null;
        var members = ImmutableDictionary.CreateBuilder<long, RvrMemberFacts>();
        int lowestZ = int.MaxValue;
        foreach (RvrCombatant member in board.Members)
        {
            if (member?.Living == null || member.IsPet)
                continue;
            lowestZ = Math.Min(lowestZ, member.Z);
            boardCaster ??= member.Living;   // #521: the immunity reads below are for a caster of this side
            RvrMemberFacts facts = ReadMember(member.Living, ref mezDuration, ref stunDuration);
            long controlledSince = 0;
            lock (memory.Lock)
            {
                if ((member.Control & (RvrControlState.Mezzed | RvrControlState.Stunned)) != 0)
                {
                    if (!memory.ControlledSince.TryGetValue(member.Key, out controlledSince))
                        memory.ControlledSince[member.Key] = controlledSince = now;
                }
                else
                    memory.ControlledSince.Remove(member.Key);
            }
            members[member.Key] = facts with { ControlledSince = controlledSince };
        }

        var enemies = ImmutableDictionary.CreateBuilder<long, RvrEnemyFacts>();
        lock (memory.Lock)
        {
            Prune(memory.UnreachableUntil, now, 0);
            Prune(memory.KilledAt, now, ResurrectWindowMilliseconds);
            Prune(memory.ResurrectedAt, now, JustResurrectedMilliseconds);
        }
        foreach (RvrCombatant enemy in board.Enemies)
        {
            if (enemy?.Living is not GameLiving living)
                continue;
            bool unreachable, resurrected;
            lock (memory.Lock)
            {
                unreachable = memory.UnreachableUntil.TryGetValue(enemy.Key, out long until) && until > now;
                if (memory.KilledAt.Remove(enemy.Key))
                    memory.ResurrectedAt[enemy.Key] = now;
                resurrected = memory.ResurrectedAt.ContainsKey(enemy.Key);
            }
            try
            {
                enemies[enemy.Key] = new RvrEnemyFacts
                {
                    Unreachable = unreachable,
                    OnWall = lowestZ != int.MaxValue && enemy.Z > lowestZ + WallHeight && OnKeepWall(living),
                    BigSpell = living.effectListComponent?.ContainsEffectForEffectType(eEffect.MasteryOfConcentration) == true,
                    JustResurrected = resurrected || RecentResurrectionIllness(living, now),
                    MezImmune = !enemy.IsPet && RvrCrowdControlPlan.MezImmune(living, mezDuration, boardCaster),
                    StunImmune = !enemy.IsPet && RvrCrowdControlPlan.StunImmune(living, stunDuration, boardCaster),
                    RootImmune = !enemy.IsPet && RvrCrowdControlPlan.RootImmune(living),
                };
            }
            catch (InvalidOperationException)
            {
                // An effect list changed under the read: default facts for this enemy on this board.
            }
        }
        return new RvrFieldFacts { Enemies = enemies.ToImmutable(), Members = members.ToImmutable() };
    }

    private static void Prune(Dictionary<long, long> table, long now, long keep)
    {
        if (table.Count < 16)
            return;
        var stale = new List<long>();
        foreach (KeyValuePair<long, long> pair in table)
            if (keep == 0 ? pair.Value <= now : now - pair.Value > keep)
                stale.Add(pair.Key);
        foreach (long key in stale)
            table.Remove(key);
    }

    private static bool RecentResurrectionIllness(GameLiving living, long now)
    {
        if (living is not GamePlayer)
            return false;
        foreach (eEffect type in new[] { eEffect.RvrResurrectionIllness, eEffect.ResurrectionIllness })
            if (EffectListService.GetEffectOnTarget(living, type) is ECSGameEffect illness &&
                now - illness.StartTick <= JustResurrectedMilliseconds)
                return true;
        return false;
    }

    /// <summary>Up on a keep's wall: within HoldWall's radius of a keep, and above its lowest door by more than 100.</summary>
    public static bool OnKeepWall(GameLiving living)
    {
        if (living == null || GameServer.KeepManager == null)
            return false;
        foreach (AbstractGameKeep keep in GameServer.KeepManager.GetKeepsOfRegion(living.CurrentRegionID))
        {
            if (keep == null || keep.Doors.Count == 0 || living.GetDistanceTo(new Point3D(keep.X, keep.Y, keep.Z)) >= WallKeepRadius)
                continue;
            int lowestDoor = int.MaxValue;
            foreach (GameKeepDoor door in keep.Doors.Values)
                lowestDoor = Math.Min(lowestDoor, door.Z);
            if (living.Z > lowestDoor + WallHeight)
                return true;
        }
        return false;
    }

    private static RvrMemberFacts ReadMember(GameLiving living, ref int mezDuration, ref int stunDuration)
    {
        if (living is not GameBot bot)
            return new RvrMemberFacts { Boldness = 0.5 };
        bool canMez = false, aoeMez = false, canRoot = false, canStun = false, instant = false, fast = false;
        try
        {
            List<Spell> control = bot.CrowdControlSpells;
            for (int i = 0; control != null && i < control.Count; i++)
            {
                Spell spell = control[i];
                if (spell == null || spell.Level > bot.Level)
                    continue;
                if (spell.SpellType == eSpellType.Mesmerize)
                {
                    canMez = true;
                    aoeMez |= spell.Radius > 0;
                    mezDuration = Math.Max(mezDuration, spell.Duration);
                }
                else
                    canRoot = true;
            }
            foreach (List<Spell> list in new[] { bot.InstantHarmfulSpells, bot.HarmfulSpells })
                for (int i = 0; list != null && i < list.Count; i++)
                {
                    Spell spell = list[i];
                    if (spell == null || spell.Level > bot.Level)
                        continue;
                    if (spell.SpellType == eSpellType.Stun)
                    {
                        canStun = true;
                        stunDuration = Math.Max(stunDuration, spell.Duration);
                    }
                    else if (spell.IsInstantCast)
                        instant = true;
                    else if (spell.CastTime <= RvrInterruptPlan.FastCastMilliseconds)
                        fast = true;
                }
        }
        catch (ArgumentOutOfRangeException)
        {
            // The bot re-sorted its spells during the read; it counts as having none for this board.
        }
        catch (InvalidOperationException)
        {
        }
        eCharacterClass characterClass = (eCharacterClass)(bot.CharacterClass?.ID ?? 0);
        BotPersonality? personality = AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot) : null;
        return new RvrMemberFacts
        {
            IsLeader = bot.Group == null || ReferenceEquals(bot.Group.LivingLeader, bot),
            CanMez = canMez,
            AoeMez = aoeMez,
            CanRoot = canRoot,
            CanStun = canStun,
            Ranged = BotRangedCombat.IsDedicatedArcher(characterClass) && bot.Inventory?.GetItem(eInventorySlot.DistanceWeapon) != null,
            InstantSpell = instant,
            FastSpell = fast,
            HasPet = bot.ControlledBrain?.Body is GameNPC { IsAlive: true },
            SupportOnly = BotPartyRoles.IsSupport(bot),
            Boldness = AutonomousRvrPerception.Boldness(personality),
        };
    }
}
