using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

namespace DOL.GS;

/// <summary>What an RvR party of gamebots is for (docs/RVR-FIELD-1.2.md, "Who is out there").</summary>
public enum RvrPartyKind : byte
{
    /// <summary>Today's warband: groups moving together on the main roads, sieges, defence calls.</summary>
    Warband,
    /// <summary>A role-built eight that walks a patrol circuit and checks its spots.</summary>
    Eight,
    /// <summary>A soloer, a duo or a stealther: chokepoints and camps.</summary>
    Small,
}

/// <summary>The population split in percent (owner decision 2, 2026-11-16: 55/30/15), from rvr_roam_population_split.</summary>
public readonly record struct RvrRoamShares(int Warband, int Eight, int Small)
{
    public static readonly RvrRoamShares Default = new(55, 30, 15);

    public int Total => Warband + Eight + Small;

    /// <summary>"55/30/15" (any separator of / , ; or space). Anything else, negative or all zero, gives the default.</summary>
    public static RvrRoamShares Parse(string text)
    {
        string[] parts = (text ?? string.Empty).Split(['/', ',', ';', ' '], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || !int.TryParse(parts[0], out int warband) || !int.TryParse(parts[1], out int eight) ||
            !int.TryParse(parts[2], out int small) || warband < 0 || eight < 0 || small < 0 || warband + eight + small <= 0)
            return Default;
        return new(warband, eight, small);
    }

    public double Share(RvrPartyKind kind) => Total <= 0 ? 0 : kind switch
    {
        RvrPartyKind.Warband => Warband,
        RvrPartyKind.Eight => Eight,
        _ => Small,
    } / (double)Total;

    public override string ToString() => $"{Warband}/{Eight}/{Small}";
}

/// <summary>How many RvR gamebots of one realm, not in a siege, are in each kind now.</summary>
public readonly record struct RvrRoamCensus(int Warband, int Eight, int Small)
{
    public int Total => Warband + Eight + Small;

    public int Of(RvrPartyKind kind) => kind switch
    {
        RvrPartyKind.Warband => Warband,
        RvrPartyKind.Eight => Eight,
        _ => Small,
    };

    /// <summary>A party of <paramref name="members"/> formed from free (small) bots as <paramref name="kind"/>.</summary>
    public RvrRoamCensus Formed(RvrPartyKind kind, int members) => kind switch
    {
        RvrPartyKind.Warband => this with { Warband = Warband + members, Small = Math.Max(0, Small - members) },
        RvrPartyKind.Eight => this with { Eight = Eight + members, Small = Math.Max(0, Small - members) },
        _ => this,
    };
}

/// <summary>
/// The population split (work package 6, owner decision 2): of the RvR gamebots not in a siege, 55% roam in warbands,
/// 30% in role-built eights, 15% alone, in duos or in stealth. Each time a free bot could lead a new party, the kind
/// furthest below its share is chosen; a stealther leader stays small unless the small share is clearly over.
/// Siege parties form before this (FormSiegeParties) and are not counted. The split is an assumption the owner can
/// change with rvr_roam_population_split. Pure.
/// </summary>
public static class RvrRoamSplit
{
    /// <summary>Of the small formations, this share is a duo; the rest stay alone.</summary>
    public const double DuoShare = 1 / 3.0;
    /// <summary>A stealther leader stays small while the small share is no more than this far over its target.</summary>
    public const double StealtherSlack = 0.05;

    public static RvrPartyKind Choose(RvrRoamCensus census, RvrRoamShares shares, bool stealtherLeader, double roll)
    {
        int total = census.Total;
        if (total <= 0)
            return RvrPartyKind.Small; // nobody counted: nobody to form with
        double Deficit(RvrPartyKind kind) => shares.Share(kind) * total - census.Of(kind);
        double small = Deficit(RvrPartyKind.Small), warband = Deficit(RvrPartyKind.Warband), eight = Deficit(RvrPartyKind.Eight);
        if (stealtherLeader && small > -StealtherSlack * total)
            return RvrPartyKind.Small;
        // Half a bot of slack: on target, no new party.
        if (small >= warband - 0.5 && small >= eight - 0.5)
            return RvrPartyKind.Small;
        if (Math.Abs(warband - eight) < 0.5)
            return roll < shares.Share(RvrPartyKind.Warband) / Math.Max(1e-9, shares.Share(RvrPartyKind.Warband) + shares.Share(RvrPartyKind.Eight))
                ? RvrPartyKind.Warband : RvrPartyKind.Eight;
        return warband > eight ? RvrPartyKind.Warband : RvrPartyKind.Eight;
    }

    /// <summary>
    /// How long a realm waits for its free pool to fill an eight before a short eight forms as a warband after all
    /// (fix/rvr-quiet-frontier, owner's PC log 2026-11-17 on 19e7037: 27 eights against 235 warbands, 178 of them pairs).
    /// </summary>
    public const long ShortEightWaitMilliseconds = 5 * 60_000;

    /// <summary>
    /// What a leader chosen for an eight does when its roster found only <paramref name="found"/> members (leader
    /// included). Enough for an eight: <see cref="RvrPartyKind.Eight"/>. Short: it waits (null: no party this pass, the
    /// leader stays free and the realm's free pool fills as warbands end), unless the realm's eight share cannot hold a
    /// minimum eight at all, or the realm has waited <see cref="ShortEightWaitMilliseconds"/> since its first short eight
    /// in a row; then it forms as a warband, as before. Pure.
    /// </summary>
    public static RvrPartyKind? ShortEight(int found, RvrRoamCensus census, RvrRoamShares shares, long waitedMilliseconds)
    {
        if (found >= RvrRosterTemplates.MinimumEight)
            return RvrPartyKind.Eight;
        if (shares.Share(RvrPartyKind.Eight) * census.Total < RvrRosterTemplates.MinimumEight ||
            waitedMilliseconds >= ShortEightWaitMilliseconds)
            return RvrPartyKind.Warband;
        return null;
    }

    /// <summary>
    /// Whether a force may join a running keep event on this side. A roaming eight walks its circuits: it answers its
    /// realm's defence (the defender side), but is not drafted into another realm's keep as an attacker or third realm
    /// (owner's PC log 2026-11-17 on 19e7037: 18 of 27 eights ended "Siege in progress", 2 ever walked a circuit). Every
    /// other force joins as before. Pure.
    /// </summary>
    public static bool MayJoinKeepEvent(bool roamingEight, bool defenderSide) => !roamingEight || defenderSide;

    /// <summary>
    /// The party size for a chosen kind. <paramref name="rolled"/> is today's warband roll (1 means "alone"), kept for a
    /// warband when it is a real party; <paramref name="largestAllowed"/> is what the realm's reserve leaves.
    /// </summary>
    public static int Size(RvrPartyKind kind, int rolled, int largestAllowed, int leaderLevel, double roll)
    {
        int largest = Math.Clamp(largestAllowed, 1, 8);
        switch (kind)
        {
            case RvrPartyKind.Eight:
                return largest;
            case RvrPartyKind.Small:
                return roll < DuoShare && largest >= 2 ? 2 : 1;
            default:
                if (rolled >= 2)
                    return Math.Min(rolled, largest);
                if (largest < 2)
                    return 1;
                // Today's level 50 roll sends a quarter of leaders off alone; under the split the small share does that.
                return leaderLevel >= 50 && largest >= AutonomousRvrStaging.SiegeWarbandSize
                    ? AutonomousRvrStaging.RollWarbandSize(largest, AutonomousRvrStaging.LevelFiftySoloRoamerShare +
                        roll * (1 - AutonomousRvrStaging.LevelFiftySoloRoamerShare), leaderLevel)
                    : 2 + (int)(Math.Clamp(roll, 0, Math.BitDecrement(1d)) * (largest - 1));
        }
    }
}

/// <summary>A slot of the 2003 skeleton (docs/RVR-FIELD-1.2.md, "Realm playbooks").</summary>
public enum RvrRosterRole : byte
{
    MainHealer,
    CrowdControl,
    ShieldTank,
    Speed,
    /// <summary>The Shaman of a Midgard eight, the Theurgist of an Albion one.</summary>
    RealmExtra,
    CureMez,
    Damage,
}

/// <summary>A bot the roster may take: its index in the pool, class, level and whether it knows a cure for mez.</summary>
public readonly record struct RvrRosterCandidate(int Index, eCharacterClass Class, int Level, bool CanCureMez);

/// <summary>A built roster: the pool indices taken (in the order taken), each skeleton role filled, short or covered by
/// a next best fit.</summary>
public sealed record RvrRoster
{
    public string Template { get; init; } = string.Empty;
    public ImmutableArray<int> Picked { get; init; } = [];
    public ImmutableArray<(RvrRosterRole Role, eCharacterClass Class)> Filled { get; init; } = [];
    public ImmutableArray<RvrRosterRole> Missing { get; init; } = [];
    public ImmutableArray<RvrRosterRole> NextBest { get; init; } = [];
}

/// <summary>
/// Role-built eights (work package 6). Every realm fills the same skeleton (retrospective Uthgard guide): two main
/// healers, one main crowd controller, one shield tank, a speed class, a Shaman (Midgard) or Theurgist (Albion), two who
/// can cure mez, and the rest damage, the realm's 2003 damage classes first. A role that the realm's own class is
/// missing for goes to the next best fit (a Friar or Shaman heals, a Spiritmaster mezzes, an off-tank guards); with
/// nothing that fits it stays empty and the group still forms. A member counts for crowd control, speed and cure mez on
/// top of its own slot when its class does that job (a Midgard Healer mezzes, a Bard is the Hibernian speed). The pet
/// cap (rvr_group_pet_cap) holds. Pure: classes come in, pool indices go out.
/// </summary>
public static class RvrRosterTemplates
{
    private sealed record Slot(RvrRosterRole Role, eCharacterClass[] Preferred, eCharacterClass[] NextBest, bool Overlaps);

    private static readonly Dictionary<eRealm, Slot[]> Skeletons = new()
    {
        [eRealm.Albion] =
        [
            new(RvrRosterRole.MainHealer, [eCharacterClass.Cleric], [eCharacterClass.Friar, eCharacterClass.Paladin], false),
            new(RvrRosterRole.MainHealer, [eCharacterClass.Cleric], [eCharacterClass.Friar, eCharacterClass.Paladin], false),
            new(RvrRosterRole.CrowdControl, [eCharacterClass.Sorcerer], [eCharacterClass.Minstrel], true),
            new(RvrRosterRole.ShieldTank, [eCharacterClass.Paladin, eCharacterClass.Armsman], [eCharacterClass.Reaver, eCharacterClass.Mercenary], false),
            new(RvrRosterRole.Speed, [eCharacterClass.Minstrel], [], true),
            new(RvrRosterRole.RealmExtra, [eCharacterClass.Theurgist], [], false),
        ],
        [eRealm.Midgard] =
        [
            new(RvrRosterRole.MainHealer, [eCharacterClass.Healer], [eCharacterClass.Shaman], false),
            new(RvrRosterRole.MainHealer, [eCharacterClass.Healer], [eCharacterClass.Shaman], false),
            new(RvrRosterRole.CrowdControl, [eCharacterClass.Healer], [eCharacterClass.Spiritmaster], true),
            new(RvrRosterRole.ShieldTank, [eCharacterClass.Warrior, eCharacterClass.Thane], [eCharacterClass.Berserker, eCharacterClass.Savage], false),
            new(RvrRosterRole.Speed, [eCharacterClass.Skald], [], true),
            new(RvrRosterRole.RealmExtra, [eCharacterClass.Shaman], [], false),
        ],
        [eRealm.Hibernia] =
        [
            new(RvrRosterRole.MainHealer, [eCharacterClass.Druid], [eCharacterClass.Warden], false),
            new(RvrRosterRole.MainHealer, [eCharacterClass.Druid], [eCharacterClass.Warden], false),
            new(RvrRosterRole.CrowdControl, [eCharacterClass.Bard], [eCharacterClass.Mentalist], true),
            new(RvrRosterRole.ShieldTank, [eCharacterClass.Hero, eCharacterClass.Champion, eCharacterClass.Warden], [eCharacterClass.Blademaster, eCharacterClass.Valewalker], false),
            new(RvrRosterRole.Speed, [eCharacterClass.Bard], [], true),
        ],
    };

    /// <summary>The realm's 2003 damage classes, best first (playbooks: two Mercenaries; Savage, Berserker, Runemaster;
    /// two point-blank casters and two tanks).</summary>
    private static readonly Dictionary<eRealm, eCharacterClass[]> DamageOrder = new()
    {
        [eRealm.Albion] = [eCharacterClass.Mercenary, eCharacterClass.Armsman, eCharacterClass.Reaver, eCharacterClass.Wizard,
            eCharacterClass.Paladin, eCharacterClass.Cabalist, eCharacterClass.Scout, eCharacterClass.Necromancer, eCharacterClass.Heretic, eCharacterClass.Friar],
        [eRealm.Midgard] = [eCharacterClass.Savage, eCharacterClass.Berserker, eCharacterClass.Runemaster, eCharacterClass.Warrior,
            eCharacterClass.Thane, eCharacterClass.Spiritmaster, eCharacterClass.Hunter, eCharacterClass.Bonedancer, eCharacterClass.Warlock],
        [eRealm.Hibernia] = [eCharacterClass.Eldritch, eCharacterClass.Hero, eCharacterClass.Blademaster, eCharacterClass.Champion,
            eCharacterClass.Mentalist, eCharacterClass.Valewalker, eCharacterClass.Ranger, eCharacterClass.Enchanter, eCharacterClass.Animist, eCharacterClass.Bainshee],
    };

    public const int CureMezWanted = 2;
    /// <summary>An eight that finds fewer members than this (leader included) forms as an ordinary warband instead.</summary>
    public const int MinimumEight = 4;

    public static string TemplateName(eRealm realm) => realm switch
    {
        eRealm.Albion => "eight-albion",
        eRealm.Midgard => "eight-midgard",
        eRealm.Hibernia => "eight-hibernia",
        _ => "eight",
    };

    /// <summary>The skeleton roles of a realm's eight, in fill order (two cure-mez slots after them; damage fills the rest).</summary>
    public static ImmutableArray<RvrRosterRole> Skeleton(eRealm realm) =>
        (Skeletons.TryGetValue(realm, out Slot[] slots) ? slots : Skeletons[eRealm.Albion]).Select(slot => slot.Role)
        .Append(RvrRosterRole.CureMez).Append(RvrRosterRole.CureMez).ToImmutableArray();

    /// <summary>
    /// Builds the eight for <paramref name="leader"/> from <paramref name="pool"/> (in the coordinator's order: guild,
    /// level, distance), taking at most <paramref name="take"/> members besides the leader and at most
    /// <paramref name="petCap"/> pet classes counting the leader (AutonomousRvrPetCap).
    /// </summary>
    public static RvrRoster Build(eRealm realm, RvrRosterCandidate leader, IReadOnlyList<RvrRosterCandidate> pool, int take, int petCap)
    {
        Slot[] slots = Skeletons.TryGetValue(realm, out Slot[] found) ? found : Skeletons[eRealm.Albion];
        eCharacterClass[] damage = DamageOrder.TryGetValue(realm, out eCharacterClass[] order) ? order : [];
        var members = new List<RvrRosterCandidate> { leader };
        var picked = new List<int>();
        var used = new HashSet<int>();
        var filled = ImmutableArray.CreateBuilder<(RvrRosterRole, eCharacterClass)>();
        var missing = ImmutableArray.CreateBuilder<RvrRosterRole>();
        var nextBest = ImmutableArray.CreateBuilder<RvrRosterRole>();
        int pets = AutonomousRvrPetCap.IsPetClass(leader.Class) ? 1 : 0;
        // Each member stands in one skeleton slot; a member can also cover an overlapping role its class does.
        bool leaderSlotted = false;

        bool CanTake(RvrRosterCandidate candidate) => !used.Contains(candidate.Index) &&
            (!AutonomousRvrPetCap.IsPetClass(candidate.Class) || pets < petCap);

        void Take(RvrRosterCandidate candidate)
        {
            used.Add(candidate.Index);
            picked.Add(candidate.Index);
            members.Add(candidate);
            if (AutonomousRvrPetCap.IsPetClass(candidate.Class))
                pets++;
        }

        foreach (Slot slot in slots)
        {
            // The leader takes the first slot its class is preferred for.
            if (!leaderSlotted && slot.Preferred.Contains(leader.Class))
            {
                leaderSlotted = true;
                filled.Add((slot.Role, leader.Class));
                continue;
            }
            // An overlapping job (mez, speed) already done by a member whose class does it.
            if (slot.Overlaps && members.Any(member => slot.Preferred.Contains(member.Class)))
            {
                filled.Add((slot.Role, members.First(member => slot.Preferred.Contains(member.Class)).Class));
                continue;
            }
            if (picked.Count >= take)
            {
                missing.Add(slot.Role);
                continue;
            }
            RvrRosterCandidate? choice = pool.Where(CanTake).Cast<RvrRosterCandidate?>()
                .FirstOrDefault(candidate => slot.Preferred.Contains(candidate.Value.Class));
            bool fallback = false;
            if (choice == null)
            {
                // The next best fit, in the order the slot lists them; or, for an overlapping job, a member who does it.
                if (slot.Overlaps && members.Any(member => slot.NextBest.Contains(member.Class)))
                {
                    filled.Add((slot.Role, members.First(member => slot.NextBest.Contains(member.Class)).Class));
                    nextBest.Add(slot.Role);
                    continue;
                }
                choice = pool.Where(CanTake).Where(candidate => slot.NextBest.Contains(candidate.Class))
                    .OrderBy(candidate => Array.IndexOf(slot.NextBest, candidate.Class)).Cast<RvrRosterCandidate?>().FirstOrDefault();
                fallback = choice != null;
            }
            if (choice == null)
            {
                missing.Add(slot.Role);
                continue;
            }
            Take(choice.Value);
            filled.Add((slot.Role, choice.Value.Class));
            if (fallback)
                nextBest.Add(slot.Role);
        }

        // Two who can cure mez: members who already can count; then the pool, healers and support first.
        int curers = members.Count(member => member.CanCureMez);
        for (int k = 0; k < CureMezWanted; k++)
        {
            if (k < curers)
            {
                filled.Add((RvrRosterRole.CureMez, members.Where(member => member.CanCureMez).ElementAt(k).Class));
                continue;
            }
            RvrRosterCandidate? curer = picked.Count >= take ? null : pool.Where(CanTake).Where(candidate => candidate.CanCureMez)
                .OrderBy(candidate => RvrRoles.For(candidate.Class) is RvrRole.Healer or RvrRole.Support ? 0 : 1)
                .Cast<RvrRosterCandidate?>().FirstOrDefault();
            if (curer == null)
            {
                missing.Add(RvrRosterRole.CureMez);
                continue;
            }
            Take(curer.Value);
            filled.Add((RvrRosterRole.CureMez, curer.Value.Class));
        }

        // The rest damage: the realm's 2003 damage classes first, then anyone (stealthers last: they camp alone).
        int DamageRank(RvrRosterCandidate candidate)
        {
            int at = Array.IndexOf(damage, candidate.Class);
            if (at >= 0) return at;
            return RvrRoles.For(candidate.Class) == RvrRole.Stealther ? 200 : 100;
        }
        foreach (RvrRosterCandidate candidate in pool.Where(c => !used.Contains(c.Index)).OrderBy(DamageRank).ToArray())
        {
            if (picked.Count >= take)
                break;
            if (!CanTake(candidate))
                continue;
            Take(candidate);
            filled.Add((RvrRosterRole.Damage, candidate.Class));
        }
        if (!leaderSlotted)
            filled.Insert(0, (RvrRosterRole.Damage, leader.Class));

        return new RvrRoster
        {
            Template = TemplateName(realm),
            Picked = picked.ToImmutableArray(),
            Filled = filled.ToImmutable(),
            Missing = missing.ToImmutable(),
            NextBest = nextBest.ToImmutable(),
        };
    }

    /// <summary>
    /// A duo's second: a stealther goes with another stealther (stealth groups, 2003), a healer with a fighter, anyone
    /// else with a healer or support class if one is free; otherwise the first in the pool. -1 for none. The pet cap of a
    /// small group (one pet class) holds.
    /// </summary>
    public static int DuoPartner(RvrRosterCandidate leader, IReadOnlyList<RvrRosterCandidate> pool, int petCap)
    {
        bool leaderPet = AutonomousRvrPetCap.IsPetClass(leader.Class);
        var free = pool.Where(candidate => !(leaderPet && AutonomousRvrPetCap.IsPetClass(candidate.Class) && petCap <= 1)).ToArray();
        if (free.Length == 0)
            return -1;
        RvrRole role = RvrRoles.For(leader.Class);
        RvrRosterCandidate? best = role switch
        {
            RvrRole.Stealther or RvrRole.Archer => free.Cast<RvrRosterCandidate?>().FirstOrDefault(c => RvrRoles.For(c.Value.Class) is RvrRole.Stealther or RvrRole.Archer),
            RvrRole.Healer => free.Cast<RvrRosterCandidate?>().FirstOrDefault(c => RvrRoles.For(c.Value.Class) is not (RvrRole.Healer or RvrRole.Support)),
            _ => free.Cast<RvrRosterCandidate?>().FirstOrDefault(c => RvrRoles.For(c.Value.Class) is RvrRole.Healer or RvrRole.Support),
        };
        return (best ?? free[0]).Index;
    }
}
