using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;

namespace DOL.GS;

/// <summary>
/// The key of the enemy party a target belongs to, for RvR perception (docs/RVR-FIELD-1.2.md, layer "Eyes"; work package
/// 1). A grouped player or bot is keyed by its group, a pet by its owner's group (or by its owner when the owner is
/// solo), and anyone else by itself. The observer cap counts lookers per enemy party, and noticing one member of a party
/// notices its mates near it. The key never equals a target key: target keys are region &lt;&lt; 32 | object id (below
/// 2^48), party keys of groups have bit 62 set.
/// </summary>
public static class RvrPartyKey
{
    /// <summary>Set on the key of a group, so it can never equal the key of one target.</summary>
    public const long GroupBit = 1L << 62;

    private static readonly ConditionalWeakTable<Group, StrongBox<long>> Serials = new();
    private static long _nextSerial;

    /// <summary>
    /// The party key from plain values: the group's serial when the target (or a pet's owner) is grouped, otherwise the
    /// key of the target itself (or of the pet's owner). Pure.
    /// </summary>
    public static long Compose(long ownerTargetKey, long? groupSerial) =>
        groupSerial is long serial ? GroupBit | (serial & (GroupBit - 1)) : ownerTargetKey;

    /// <summary>Whether a party key names a group (false: one target and its pets).</summary>
    public static bool IsGroup(long partyKey) => (partyKey & GroupBit) != 0;

    /// <summary>The log label of a party key: "solo" or "group-&lt;serial&gt;". Pure.</summary>
    public static string Label(long partyKey) => IsGroup(partyKey) ? $"group-{partyKey & (GroupBit - 1)}" : "solo";

    /// <summary>The key one target is perceived under (region &lt;&lt; 32 | object id), as RvR perception keys its memory.</summary>
    public static long TargetKey(GameLiving target) => ((long)target.CurrentRegionID << 32) | (uint)target.ObjectID;

    /// <summary>A stable serial for a group, given out once per group object for as long as the group lives.</summary>
    public static long SerialOf(Group group) =>
        Serials.GetValue(group, _ => new StrongBox<long>(Interlocked.Increment(ref _nextSerial))).Value;

    /// <summary>Who a target fights with: itself, or the owner of a pet.</summary>
    public static GameLiving OwnerOf(GameLiving target) =>
        target is GameNPC { Brain: IControlledBrain pet } && pet.GetLivingOwner() is GameLiving owner ? owner : target;

    /// <summary>The party key of a target (reads the game: the group of the target or of its owner).</summary>
    public static long Of(GameLiving target)
    {
        GameLiving owner = OwnerOf(target);
        Group group = owner.Group;
        return Compose(TargetKey(owner), group != null ? SerialOf(group) : null);
    }
}
