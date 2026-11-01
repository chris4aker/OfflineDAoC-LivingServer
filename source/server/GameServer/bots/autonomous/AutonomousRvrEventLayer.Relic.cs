using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Relic carrier events and relic campaigns across restarts (relic audit 2026-10-31: L5, L6, B5).
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    /// <summary>A carrier event's own window from the moment the relic leaves its pad (it no longer keeps the siege's leftover time).</summary>
    public static long CarrierEventMilliseconds => 2 * 60 * 60_000L;
    /// <summary>While the relic is carried or on the ground the carrier event stays open at least this long ahead.</summary>
    public const long CarrierKeepOpenMilliseconds = 15 * 60_000;
    /// <summary>A relic seen back on a pad this long without its RelicMounted/RelicReturned event ends the carrier event anyway.</summary>
    public const long CarrierMountedGraceMilliseconds = 2_000;

    /// <summary>The relic keep a carrier event came from and the realm that took the relic (resumed after a restart).</summary>
    private static readonly Dictionary<string, (string KeepId, eRealm Attacker)> CarrierOrigins = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, long> CarrierMountedSeen = new(StringComparer.Ordinal);

    /// <summary>
    /// Called under Sync from Expire. A relic that went back onto a pad by itself (return timer, lost item) never fired
    /// RelicMounted, so its carrier event lived on for hours and ChooseCore refused every new siege of every realm (L5).
    /// While the relic is still carried or on the ground the event stays open (L6).
    /// </summary>
    private static void RefreshCarrierEvents(long nowTick)
    {
        if (CarrierEvents.Count == 0) return;
        GameRelic[] relics = RelicMgr.GetRelics();
        if (relics.Length == 0) return;
        foreach (string id in CarrierEvents.Keys.ToArray())
        {
            GameRelic relic = relics.FirstOrDefault(r => RelicCarrierTargetId(r) == id);
            if (relic == null) continue;
            if (relic.IsMounted)
            {
                if (!CarrierMountedSeen.TryGetValue(id, out long seen)) { CarrierMountedSeen[id] = nowTick; continue; }
                if (nowTick - seen < CarrierMountedGraceMilliseconds) continue;
                CarrierMountedSeen.Remove(id);
                eRealm taker = CarrierOrigins.TryGetValue(id, out var origin) ? origin.Attacker
                    : CarrierTargets.GetValueOrDefault(id)?.OwningRealm ?? eRealm.None;
                bool captured = taker != eRealm.None && relic.Realm == taker;
                EndTarget(id, nowTick, captured ? relic.Realm : eRealm.None, captured ? "captured" : "returned");
                continue;
            }
            CarrierMountedSeen.Remove(id);
            var entry = CarrierEvents[id];
            long keepOpen = nowTick + CarrierKeepOpenMilliseconds;
            if (entry.ExpiresTick < keepOpen) CarrierEvents[id] = (keepOpen, entry.Participants);
        }
    }

    private static void LogCarrierEnded(string carrierId, string outcome, eRealm winner)
    {
        CarrierMountedSeen.Remove(carrierId);
        var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
        if (log.IsInfoEnabled) log.Info($"RVR_RELIC_CARRIER_ENDED carrier={carrierId} outcome={outcome} winner={GlobalConstants.RealmToName(winner)}");
    }

    /// <summary>Whether a relic campaign for this relic keep is open (tests, dashboard).</summary>
    public static bool HasRelicCampaign(string relicKeepId)
    {
        lock (Sync) return Campaigns.ContainsKey(relicKeepId);
    }

    /// <summary>One relic raid in progress, as saved across a restart.</summary>
    /// <param name="Source">"campaign" (taking the sentinel keeps), "siege" (the relic siege itself) or "carrier" (the relic was
    /// being carried home: after a restart it is back on its pad, see AutonomousRvrRelicPersistence).</param>
    public sealed record RelicState(string RelicKeepId, eRealm Attacker, string Source, long IdleRemainingMilliseconds, int StepsWithoutProgress);

    /// <summary>The relic raids in progress: open campaigns, running relic sieges and relic carrier events.</summary>
    public static RelicState[] RelicStateSnapshot(long nowTick)
    {
        lock (Sync)
        {
            var result = new List<RelicState>();
            var seen = new HashSet<(string, eRealm)>();
            void Add(RelicState state)
            {
                if ((state.Attacker is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) && seen.Add((state.RelicKeepId, state.Attacker)))
                    result.Add(state);
            }
            foreach ((string id, RelicCampaign campaign) in Campaigns)
                Add(new(id, campaign.Attacker, "campaign", Math.Max(0, campaign.DeadlineTick - nowTick), campaign.StepsWithoutProgress));
            foreach (ActiveEvent active in Events.Values.Where(e => e.RelicKeep && !e.DefenseReaction && !e.Retake))
                Add(new(active.TargetId, active.AttackerRealm, "siege", CampaignIdleMilliseconds, 0));
            foreach (var origin in CarrierOrigins.Values)
                Add(new(origin.KeepId, origin.Attacker, "carrier", CampaignIdleMilliseconds, 0));
            return result.ToArray();
        }
    }

    /// <summary>
    /// Re-plans saved relic raids after a restart (B5): each becomes a relic campaign that continues after
    /// <paramref name="resumeDelayMilliseconds"/> (the next sentinel keep, or the relic siege itself), with a fresh muster.
    /// </summary>
    public static int RestoreRelicState(IEnumerable<RelicState> states, long nowTick, long resumeDelayMilliseconds)
    {
        int restored = 0;
        foreach (RelicState state in states ?? Array.Empty<RelicState>())
        {
            if (state?.RelicKeepId == null || !state.RelicKeepId.StartsWith("rvr-keep-", StringComparison.Ordinal) ||
                !int.TryParse(state.RelicKeepId["rvr-keep-".Length..], out int keepId)) continue;
            var keep = GameServer.KeepManager?.GetKeepByID(keepId);
            if (keep == null || !keep.IsRelic) continue;
            if (RestoreRelicCampaign(ObjectiveForKeep(keep), state.Attacker, nowTick, resumeDelayMilliseconds,
                    state.IdleRemainingMilliseconds, state.StepsWithoutProgress, state.Source))
                restored++;
        }
        return restored;
    }

    public static bool RestoreRelicCampaign(LiveObjective relic, eRealm attacker, long nowTick, long resumeDelayMilliseconds,
        long idleRemainingMilliseconds, int stepsWithoutProgress, string source)
    {
        if (relic?.IsRelicKeep != true || relic.OwningRealm == attacker ||
            attacker is not (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia)) return false;
        lock (Sync)
        {
            if (Campaigns.ContainsKey(relic.Id)) return false;
            int held = HeldSentinelKeeps(relic).Length;
            Campaigns[relic.Id] = new RelicCampaign
            {
                Relic = relic, Attacker = attacker,
                ContinueAt = nowTick + Math.Max(1, resumeDelayMilliseconds),
                // The downtime does not count; a raid saved just before giving up still gets one more try.
                DeadlineTick = nowTick + Math.Max(idleRemainingMilliseconds, CampaignRetryMaximumMilliseconds) + resumeDelayMilliseconds,
                StepsWithoutProgress = Math.Clamp(stepsWithoutProgress, 0, CampaignMaxStepsWithoutProgress - 1),
                LastHeld = held,
            };
            var log = DOL.Logging.LoggerManager.Create(typeof(AutonomousRvrEventLayer));
            if (log.IsInfoEnabled) log.Info($"RVR_RELIC_CAMPAIGN_RESUMED relic={relic.Id} (\"{relic.Name}\") attacker={attacker} source={source} " +
                $"sentinels={held} resumeMinutes={resumeDelayMilliseconds / 60_000d:0.#}");
            return true;
        }
    }
}
