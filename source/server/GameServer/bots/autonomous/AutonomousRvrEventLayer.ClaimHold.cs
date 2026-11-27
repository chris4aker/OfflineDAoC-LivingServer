using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// #523 step 1 (h): after the take, the army stays. A bot army that captured a keep used to be released the moment the lord
/// died (EndEvent) and walked off, so the bot-guild claim (AutonomousBotGuildKeeps: 8 tries, 20 s apart) found nobody inside
/// and no claim was ever seen (rvr-audit-1.2.3 siege.md finding 10, evidence.md row 40). Its forces now hold the keep they
/// took for <see cref="ClaimHoldMilliseconds"/> (rez, regroup, claim) and are released after it.
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    /// <summary>How long the capturing army holds the keep: the whole claim window (the eighth try is 160 s after the take).</summary>
    public const long ClaimHoldMilliseconds = 3 * 60_000;

    /// <summary>Forces holding a keep they just took: the keep (now their realm's), until when, and the event's end reason.</summary>
    private static readonly Dictionary<string, (LiveObjective Target, long UntilTick, string Reason)> ClaimHolds = new(StringComparer.Ordinal);

    /// <summary>
    /// Whether the attackers of an ended siege hold the keep for the claim: a bot siege (not a player-attack defense) of an
    /// ordinary keep (a relic keep can't be claimed), captured by the attacking realm itself. Pure.
    /// </summary>
    public static bool HoldsForClaim(bool defenseReaction, bool relicKeep, bool captured, eRealm attacker, eRealm winner) =>
        !defenseReaction && !relicKeep && captured && winner == attacker &&
        attacker is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia;

    /// <summary>Called under Sync from EndEvent: the attacking forces hold the keep instead of being released.</summary>
    private static void BeginClaimHold(ActiveEvent active, long nowTick, string reason)
    {
        LiveObjective keep = active.Target with { OwningRealm = active.AttackerRealm };
        foreach (string force in active.Attackers.Keys)
            ClaimHolds[force] = (keep, nowTick + ClaimHoldMilliseconds, reason);
        // The army that captured an ordinary keep stays to hold it for the bot-guild claim, then is released.
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_CLAIM_HOLD target={active.TargetId} realm={GlobalConstants.RealmToName(active.AttackerRealm)} " +
                     $"forces={active.Attackers.Count} bots={active.Attackers.Values.Sum()} minutes={ClaimHoldMilliseconds / 60_000}");
    }

    /// <summary>Called under Sync from Expire: a hold that ran out releases its force as the siege's end would have.</summary>
    private static void ExpireClaimHolds(long nowTick)
    {
        if (ClaimHolds.Count == 0) return;
        foreach (var entry in ClaimHolds.Where(pair => pair.Value.UntilTick <= nowTick).ToArray())
        {
            ClaimHolds.Remove(entry.Key);
            ReleasedForces[entry.Key] = entry.Value.Reason;
            BotDutyLeases.ReleaseOwner(eDutyLeaseKind.RvrEvent, entry.Key);
        }
    }

    /// <summary>Called under Sync. The plan of a force holding a keep it took, or null.</summary>
    private static Plan ClaimHoldPlan(string forceId, IReadOnlyCollection<LiveObjective> objectives, long nowTick)
    {
        if (!ClaimHolds.TryGetValue(forceId, out var hold) || hold.UntilTick <= nowTick) return null;
        LiveObjective target = objectives?.FirstOrDefault(objective => objective.Id == hold.Target.Id) ?? hold.Target;
        return ToPlan(Intent.DefendEvent, target, true,
            "Holding the keep this force just took: rez, regroup and claim it before moving on.");
    }

    /// <summary>
    /// Called under Sync. A force that took another plan (a home-defense call, a new event) leaves its hold. One that took a
    /// plan outside any event (<paramref name="planTargetId"/> null) is released as <see cref="ExpireClaimHolds"/> would
    /// release it: the release is recorded and its duty lease freed. One that joined another event keeps its lease for it.
    /// </summary>
    private static void LeaveClaimHold(string forceId, string planTargetId)
    {
        if (!ClaimHolds.TryGetValue(forceId, out var hold) || hold.Target.Id == planTargetId)
            return;
        ClaimHolds.Remove(forceId);
        if (planTargetId != null)
            return;
        ReleasedForces[forceId] = hold.Reason;
        BotDutyLeases.ReleaseOwner(eDutyLeaseKind.RvrEvent, forceId);
    }

    private static bool IsClaimHoldTarget(string targetId, long nowTick) =>
        ClaimHolds.Values.Any(hold => hold.UntilTick > nowTick && hold.Target.Id == targetId);

    private static bool IsClaimHoldForce(string forceId, long nowTick) =>
        ClaimHolds.TryGetValue(forceId, out var hold) && hold.UntilTick > nowTick;

    /// <summary>Called under Sync: a member of a holding force at the keep it holds is not idle.</summary>
    private static bool HoldsClaimPost(GameBot bot, string forceId, long nowTick) =>
        ClaimHolds.TryGetValue(forceId, out var hold) && hold.UntilTick > nowTick && bot.Realm == hold.Target.OwningRealm &&
        IsHoldingSiegeDefense(true, bot.CurrentRegionID == hold.Target.RegionId,
            Vector2.DistanceSquared(new(bot.X, bot.Y), new(hold.Target.X, hold.Target.Y)), Math.Abs(bot.Z - hold.Target.Z));
}
