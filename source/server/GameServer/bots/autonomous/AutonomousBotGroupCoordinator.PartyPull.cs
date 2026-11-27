using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace DOL.GS;

/// <summary>
/// The puller's camp turn, coordinator side (1.2.3, #569 step 9). Port of zomboden/OfflineDAoC-True165 0e0a397
/// (GPL-3.0, shadowofze); AutonomousPartyPullCamp has the rules. A party whose only targets are too far to shoot from a
/// reachable spot moves its camp point onto the floor at one of them, like TryRepositionEmptyCamp: same camp identity and
/// task clock, the leader walks the formation in with the ordinary camp travel. Outdoor bot-only GroupPve parties only:
/// realm expeditions, parties hosting a real player and parties formed from a player's group keep their camp.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    /// <summary>The party's cohesion radius, for the puller's arrival beside its leader.</summary>
    public const int PartyCohesionRadius = CohesionRadius;

    private sealed class PartyPullCampState
    {
        public string AdvanceCampId = string.Empty;
        public readonly List<long> AdvanceTicks = new();
    }

    private static readonly ConditionalWeakTable<Session, PartyPullCampState> PartyPullCampStates = new();

    /// <summary>Pure: whether a party may move its camp point toward a too-far target at all.</summary>
    public static bool MayAdvancePartyCamp(bool groupPve, bool sameCamp, bool dungeon, bool regrouping, bool expedition,
        bool hostsPlayer, bool fromPlayersGroup) =>
        groupPve && sameCamp && !dungeon && !regrouping && !expedition && !hostsPlayer && !fromPlayersGroup;

    private static bool MayAdvance(Session session, string campId) =>
        MayAdvancePartyCamp(session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve,
            session.Camp != null && string.Equals(session.Camp.Id, campId, StringComparison.Ordinal),
            session.Camp?.IsDungeon == true, session.Recovery.IsRegrouping, AutonomousRealmRaid.GetView(session.Group) != null,
            session.HostedPlayer != null || BotLedGroups.IsHostedGroup(session.Group), session.FromPlayersGroup);

    /// <summary>Whether the puller's party may still move this camp point (fewer than 2 moves in 10 minutes).</summary>
    public static bool PartyCampAdvanceAllowed(GameBot puller, string campId)
    {
        if (puller?.Group == null) return false;
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
        {
            if (!TryGetSession(puller.Group, out Session session) || !MayAdvance(session, campId))
                return false;
            PartyPullCampState state = PartyPullCampStates.GetOrCreateValue(session);
            if (!string.Equals(state.AdvanceCampId, campId, StringComparison.Ordinal)) return true;
            return AutonomousPartyPullCamp.CanAdvanceCamp(state.AdvanceTicks, GameLoop.GameLoopTime);
        }
    }

    /// <summary>
    /// Moves the shared camp point onto the floor at a too-far target. The leader walks the formation in and stops about
    /// 1,250 short, which puts the puller inside its pull range. Puller only, outdoors only, bounded.
    /// </summary>
    public static bool TryAdvanceCampTowardTarget(GameBot puller, string campId, Vector3 point, string targetName,
        long tooFarMilliseconds, out int advancesThisCamp)
    {
        advancesThisCamp = 0;
        if (puller?.Group == null)
            return false;
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
        {
            if (!TryGetSession(puller.Group, out Session session) || !MayAdvance(session, campId))
                return false;
            GameBot[] members = BotMembers(session.Group);
            if (ChoosePuller(session, members) != puller || !HasActivePveComposition(session, members))
                return false;
            PartyPullCampState state = PartyPullCampStates.GetOrCreateValue(session);
            long now = GameLoop.GameLoopTime;
            if (!string.Equals(state.AdvanceCampId, campId, StringComparison.Ordinal))
            {
                state.AdvanceCampId = campId;
                state.AdvanceTicks.Clear();
            }
            if (!AutonomousPartyPullCamp.CanAdvanceCamp(state.AdvanceTicks, now))
                return false;
            state.AdvanceTicks.Add(now);
            advancesThisCamp = state.AdvanceTicks.Count;

            SharedCamp previous = session.Camp;
            session.Camp = previous with
            {
                X = (int)Math.Round(point.X),
                Y = (int)Math.Round(point.Y),
                Z = (int)Math.Round(point.Z)
            };
            session.Phase = "Traveling";
            Log.Info("AUTONOMOUS_GROUP_CAMP_ADVANCED " + JsonSerializer.Serialize(new
            {
                group = session.Id,
                camp = previous.Id,
                target = previous.MonsterName,
                pullTarget = targetName,
                zone = previous.ZoneName,
                tooFarSeconds = Math.Max(0, tooFarMilliseconds / 1000),
                advancesThisCamp,
                from = new { previous.X, previous.Y, previous.Z },
                to = new { session.Camp.X, session.Camp.Y, session.Camp.Z }
            }));
            WriteSessionMetadata(session, members);
            return true;
        }
    }
}
