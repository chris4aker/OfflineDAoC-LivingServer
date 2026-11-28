using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// What a combatant is on the field, in broad strokes. One value per class; a rule that needs more (who mezzes, who
/// has bladeturn) reads <see cref="RvrCombatant.Class"/>.
/// </summary>
public enum RvrRole : byte
{
    Unknown,
    /// <summary>Shield and heavy tanks (BotPartyRoles: Armsman, Paladin, Reaver, Warrior, Thane, Hero, Champion, Sluaghbinder).</summary>
    Tank,
    /// <summary>Melee damage: Mercenary, Berserker, Savage, Blademaster, Valewalker and the like.</summary>
    Melee,
    /// <summary>Infiltrator, Shadowblade, Nightshade.</summary>
    Stealther,
    /// <summary>Scout, Hunter, Ranger.</summary>
    Archer,
    /// <summary>Cloth casters, whatever their line (damage, crowd control, pets).</summary>
    Caster,
    /// <summary>The main healers: Cleric, Druid, Healer.</summary>
    Healer,
    /// <summary>Hybrid support: Friar, Minstrel, Shaman, Skald, Bard, Warden.</summary>
    Support,
    /// <summary>A pet (its owner is listed on its own).</summary>
    Pet,
}

/// <summary>The role of each class (pure).</summary>
public static class RvrRoles
{
    public static RvrRole For(eCharacterClass characterClass) => characterClass switch
    {
        eCharacterClass.Unknown => RvrRole.Unknown,
        eCharacterClass.Cleric or eCharacterClass.Druid or eCharacterClass.Healer => RvrRole.Healer,
        eCharacterClass.Infiltrator or eCharacterClass.Shadowblade or eCharacterClass.Nightshade => RvrRole.Stealther,
        eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger => RvrRole.Archer,
        eCharacterClass.Friar or eCharacterClass.Minstrel or eCharacterClass.Shaman or eCharacterClass.Skald or
            eCharacterClass.Bard or eCharacterClass.Warden => RvrRole.Support,
        eCharacterClass.Wizard or eCharacterClass.Theurgist or eCharacterClass.Cabalist or eCharacterClass.Sorcerer or
            eCharacterClass.Necromancer or eCharacterClass.Heretic or
            eCharacterClass.Runemaster or eCharacterClass.Spiritmaster or eCharacterClass.Bonedancer or eCharacterClass.Warlock or
            eCharacterClass.Eldritch or eCharacterClass.Enchanter or eCharacterClass.Mentalist or eCharacterClass.Animist or
            eCharacterClass.Bainshee or
            eCharacterClass.Elementalist or eCharacterClass.Mage or eCharacterClass.Disciple or eCharacterClass.Mystic or
            eCharacterClass.Magician or eCharacterClass.Forester => RvrRole.Caster,
        _ when BotPartyRoles.For(characterClass) == BotPartyRole.Tank => RvrRole.Tank,
        _ => RvrRole.Melee,
    };
}

/// <summary>Crowd control on a combatant when the board was built.</summary>
[Flags]
public enum RvrControlState : byte
{
    None = 0,
    Mezzed = 1,
    Stunned = 2,
    /// <summary>Rooted by a breakable root (BotBrain.IsBreakablyControlled without the mez).</summary>
    Rooted = 4,
}

/// <summary>
/// One player, bot or pet as the fight board saw it when it was built: a snapshot, so a rule reads the same values
/// however long it takes. <see cref="Living"/> is the live object (null in tests); everything else was copied.
/// </summary>
public sealed record RvrCombatant
{
    public GameLiving Living { get; init; }
    /// <summary>The target key the perception memory uses: region in the high half, object id in the low half.</summary>
    public long Key { get; init; }
    public eRealm Realm { get; init; }
    public eCharacterClass Class { get; init; }
    public RvrRole Role { get; init; }
    /// <summary>A real player (not a bot).</summary>
    public bool IsPlayer { get; init; }
    public bool IsPet { get; init; }
    public int Level { get; init; }
    public int HealthPercent { get; init; } = 100;
    public bool UsesPower { get; init; }
    public int PowerPercent { get; init; }
    public bool Casting { get; init; }
    /// <summary>Swinging or in combat.</summary>
    public bool Fighting { get; init; }
    public RvrControlState Control { get; init; }
    /// <summary>Who it had targeted (null: nobody, or not a living thing).</summary>
    public GameLiving Target { get; init; }
    public eRealm TargetRealm { get; init; }
    /// <summary>An enemy: its target is a member of the board's group or a member's pet.</summary>
    public bool TargetsGroup { get; init; }
    /// <summary>An enemy: a member of the board's group, or a member's pet, has it targeted.</summary>
    public bool TargetedByGroup { get; init; }
    public int X { get; init; }
    public int Y { get; init; }
    public int Z { get; init; }

    public bool IsControlled => Control != RvrControlState.None;
}

/// <summary>
/// The shared picture of one bot group's open-field RvR fight (docs/RVR-FIELD-1.2.md layer 2, "Mind"; work package 0).
/// It copies the PATTERN of <see cref="DOL.AI.Brain.GroupFightBoard"/>, not the board: one holder per group and region
/// (<see cref="RvrFightBoards"/>), rebuilt at most once a second by the first member that asks, under the holder's lock,
/// and published as this immutable snapshot, which bots thinking in parallel read without locks. Nothing is built
/// unless a consumer asks.
///
/// It holds the group's members, the enemy players, bots and pets in view with their role, health, casting and
/// crowd-control state and target, both sides' strength, the centroids, whether a fight between the other two realms
/// is in view, the group's stored facts and the group's plan (<see cref="Plan"/>).
///
/// What it does NOT check: line of sight, safe areas, attack rules, and whether any member has noticed the enemy
/// (RvR perception). A rule that picks a target from it must still pass the brain's own target checks.
/// </summary>
public sealed record RvrFightBoard
{
    public const long RebuildMilliseconds = 1_000;
    /// <summary>The one area search per group, from the asking member: as far as a normal bot notices anything.</summary>
    public const int ScanRadius = AutonomousRvrPerception.OuterRange;
    /// <summary>How old a board <see cref="RvrFightBoards.Peek"/> still hands out.</summary>
    public const long PeekMaxAgeMilliseconds = 2_000;

    public long BuiltAt { get; init; }
    public ushort RegionId { get; init; }
    /// <summary>The group's realm.</summary>
    public eRealm Realm { get; init; }
    /// <summary>The group's living members in the board's region (the bot itself when solo), then their pets.</summary>
    public ImmutableArray<RvrCombatant> Members { get; init; } = ImmutableArray<RvrCombatant>.Empty;
    /// <summary>Living, visible (not stealthed) enemy players, bots and their pets within <see cref="ScanRadius"/>.</summary>
    public ImmutableArray<RvrCombatant> Enemies { get; init; } = ImmutableArray<RvrCombatant>.Empty;
    public RvrStrength Friendly { get; init; } = RvrStrength.None;
    public RvrStrength Enemy { get; init; } = RvrStrength.None;
    /// <summary>The centre of the members (pets not counted).</summary>
    public Vector3 FriendlyCentroid { get; init; }
    /// <summary>The centre of the enemy heads (pets not counted); null with no enemy in view.</summary>
    public Vector3? EnemyCentroid { get; init; }
    /// <summary>Two other realms are fighting each other in view.</summary>
    public bool ThirdPartyFight { get; init; }
    /// <summary>
    /// The board is of the group's main body: the region holding most of its living members (the leader's on a tie;
    /// RvrEngagement.MainBody). Only such a board has a stance and moves the group's stance runtime; true for a solo bot.
    /// </summary>
    public bool MainBody { get; init; } = true;
    /// <summary>#529 step 3: the enemies are the group's contacts (rvr_board_from_contacts), not the area search's.</summary>
    public bool FromContacts { get; init; }
    /// <summary>The group's stored facts when the board was built.</summary>
    public RvrGroupState State { get; init; } = RvrGroupState.Empty;
    /// <summary>
    /// #599 step 2: when the group follows a battlegroup's lead group, the lead group's called target on its own published
    /// board for this region (at most two seconds old); null otherwise. The plan's assist step takes it when it is on this
    /// board (<see cref="RvrAssist.BattlegroupCandidate"/>).
    /// </summary>
    public GameLiving BattlegroupCall { get; init; }
    /// <summary>The group's plan, made from this board and the plan before it, and published with it.</summary>
    public RvrFightPlan Plan { get; init; } = RvrFightPlan.None;

    /// <summary>An enemy targets the group, or the group targets an enemy.</summary>
    public bool InContact
    {
        get
        {
            foreach (RvrCombatant enemy in Enemies)
                if (enemy.TargetsGroup || enemy.TargetedByGroup)
                    return true;
            return false;
        }
    }

    public bool IsFresh(long now, ushort region) => !NeedsRebuild(now, BuiltAt, region == RegionId);

    /// <summary>Pure: a board built at <paramref name="builtAt"/> is rebuilt from this time on.</summary>
    public static bool NeedsRebuild(long now, long builtAt, bool sameRegion) =>
        !sameRegion || now - builtAt >= RebuildMilliseconds;

    /// <summary>Pure: a board built at <paramref name="builtAt"/> may still be peeked at.</summary>
    public static bool PeekFresh(long now, long builtAt) => now - builtAt <= PeekMaxAgeMilliseconds;

    /// <summary>
    /// Builds the board's facts from the two sides as plain records: strengths, centroids and the third-party flag. Pure
    /// (the game is read by <see cref="RvrFightBoards"/>); <see cref="State"/> and <see cref="Plan"/> are set by the caller.
    /// </summary>
    public static RvrFightBoard Build(long now, ushort regionId, eRealm realm, IEnumerable<RvrCombatant> members,
        IEnumerable<RvrCombatant> enemies)
    {
        ImmutableArray<RvrCombatant> friendly = members == null ? ImmutableArray<RvrCombatant>.Empty : members.ToImmutableArray();
        ImmutableArray<RvrCombatant> hostile = enemies == null ? ImmutableArray<RvrCombatant>.Empty : enemies.ToImmutableArray();
        return new RvrFightBoard
        {
            BuiltAt = now,
            RegionId = regionId,
            Realm = realm,
            Members = friendly,
            Enemies = hostile,
            Friendly = RvrStrength.Of(friendly),
            Enemy = RvrStrength.Of(hostile),
            FriendlyCentroid = Centroid(friendly) ?? default,
            EnemyCentroid = Centroid(hostile),
            ThirdPartyFight = SeesThirdPartyFight(realm, hostile),
        };
    }

    /// <summary>The centre of a side's heads (pets not counted); null when it has none. Pure.</summary>
    public static Vector3? Centroid(ImmutableArray<RvrCombatant> side)
    {
        double x = 0, y = 0, z = 0;
        int count = 0;
        foreach (RvrCombatant combatant in side)
        {
            if (combatant == null || combatant.IsPet)
                continue;
            x += combatant.X;
            y += combatant.Y;
            z += combatant.Z;
            count++;
        }
        return count == 0 ? null : new Vector3((float)(x / count), (float)(y / count), (float)(z / count));
    }

    /// <summary>
    /// Whether two realms other than <paramref name="realm"/> are fighting each other: an enemy that is swinging or
    /// casting at someone of a third realm. Pure.
    /// </summary>
    public static bool SeesThirdPartyFight(eRealm realm, ImmutableArray<RvrCombatant> enemies)
    {
        foreach (RvrCombatant enemy in enemies)
            if (enemy != null && (enemy.Fighting || enemy.Casting) &&
                enemy.TargetRealm != eRealm.None && enemy.TargetRealm != realm && enemy.TargetRealm != enemy.Realm)
                return true;
        return false;
    }
}
