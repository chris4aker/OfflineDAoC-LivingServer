using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS;

/// <summary>
/// 1.2.3 (#570 step 4): the party corpse walk. Out of combat, one resurrector of an autonomous gamebot PvE party is granted a
/// walk to the nearest dead gamebot member within sight range, also while the party regroups (before this, BotBrain never
/// walked to a distant corpse during a regroup, so the regrouping party stood at the rendezvous while the corpse released
/// after 60 s). The walk is refused when the corpse lies by what killed it: a death spot or party danger spot for the
/// walker's level at the corpse (or straight on the way), or an aggressive or fighting monster beside the corpse that cons
/// orange or worse to the walker. While a walk or a raise is under way every corpse of the party waits
/// <see cref="CorpseWalkReleaseWaitMilliseconds"/> instead of 60 s. When the walker dies, stalls or the way turns unsafe the
/// grant lapses within seconds and the dead release as before.
///
/// Autonomous gamebots only. Real players' corpses, companions (hired, /bot), buffbots, temporary helpers, player-led groups
/// and realm raids keep the old rule (walk unless regrouping). Spell range, power costs, the shared reservation and rez
/// sickness are unchanged; nothing moves a body.
///
/// Cost: at most two corpses are checked (one radius query each) every few seconds per party, outside the party lock.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    public const int CorpseWalkMaxDistance = WorldMgr.VISIBILITY_DISTANCE;
    public const long CorpseWalkMaxMilliseconds = 100_000;
    public const long CorpseWalkClaimStaleMilliseconds = 5_000;
    public const long CorpseWalkSafetyRecheckMilliseconds = 3_000;
    public const long CorpseWalkRefusalMilliseconds = 30_000;
    public const long CorpseWalkChoiceMilliseconds = 2_000;
    public const ushort CorpseThreatRadius = 1_500;
    /// <summary>A walker this close to the corpse has arrived: it keeps the grant while it takes the raise.</summary>
    public const int CorpseWalkArrivedRadius = 1_500;
    public const long CorpseReleaseWaitMilliseconds = 60_000;
    public const long CorpseWalkReleaseWaitMilliseconds = 120_000;

    private sealed class CorpseWalkClaim
    {
        public GameBot Walker;
        public GameBot Corpse;
        public long Started;
        public long LastSeen;
        public long NextSafetyCheck;
    }

    private sealed class CorpseWalkState
    {
        public CorpseWalkClaim Claim;
        // corpse key -> tick until which no walk to it is granted (unsafe, or a walk to it already ran out)
        public readonly Dictionary<long, long> Refused = new();
        public long NextChoiceTick;
    }
    private static readonly ConditionalWeakTable<Session, CorpseWalkState> CorpseWalks = new();

    /// <summary>Pure rule: a grant is current while its walker is alive in the party, was seen recently and has time left.</summary>
    public static bool IsCorpseWalkCurrent(bool walkerAlive, bool walkerInParty, bool corpseDead, long started, long lastSeen, long now) =>
        walkerAlive && walkerInParty && corpseDead && started > 0 && now >= started &&
        now - lastSeen < CorpseWalkClaimStaleMilliseconds && now - started < CorpseWalkMaxMilliseconds;

    /// <summary>Pure rule: a monster near a corpse makes the walk unsafe when it cons orange or worse to the walker and is
    /// aggressive or already fighting (the walker comes alone; the party is out of combat).</summary>
    public static bool IsCorpseThreat(ConColor conToWalker, bool aggressive, bool fighting) =>
        conToWalker >= ConColor.ORANGE && (aggressive || fighting);

    /// <summary>Pure rule: how long a party corpse with a resurrector in sight waits before it releases.</summary>
    public static long CorpseReleaseWait(bool raiseUnderWay) =>
        raiseUnderWay ? CorpseWalkReleaseWaitMilliseconds : CorpseReleaseWaitMilliseconds;

    /// <summary>
    /// The remembered dangers (no world scan): why a walker of <paramref name="walkerLevel"/> at the walker's spot should not
    /// walk to a corpse at the corpse's spot, or null. A death spot that warns this level at the corpse, or straight on the
    /// way (unless the walker already stands inside one), or a party danger spot for this level by the corpse.
    /// </summary>
    public static string CorpseMemoryDanger(ushort walkerRegion, int walkerX, int walkerY, int walkerLevel,
        ushort corpseRegion, int corpseX, int corpseY, DateTime utc, long now)
    {
        if (AutonomousPveBossDanger.IsNear(corpseRegion, corpseX, corpseY, walkerLevel, utc))
            return "death spot at the corpse";
        if (walkerRegion == corpseRegion &&
            !AutonomousPveBossDanger.IsNear(walkerRegion, walkerX, walkerY, walkerLevel, utc) &&
            AutonomousPveBossDanger.RouteCrosses(walkerRegion, walkerX, walkerY, corpseX, corpseY, walkerLevel, utc))
            return "death spot on the straight route";
        if (AutonomousGroupDangerRetreat.NearSpot(corpseRegion, corpseX, corpseY, walkerLevel, now))
            return "party danger spot at the corpse";
        return null;
    }

    /// <summary>
    /// BotBrain.TryResurrectGroupMember asks this for each dead member beyond spell range: may <paramref name="walker"/>
    /// walk to <paramref name="corpse"/> to raise it? Outside an autonomous gamebot PvE party, and for a real player's
    /// corpse, the old rule stands (walk unless regrouping).
    /// </summary>
    public static bool MayWalkToCorpse(GameBot walker, GameLiving corpse, bool regrouping)
    {
        Group group = walker?.Group;
        if (group == null || corpse is not GameBot corpseBot || !walker.IsAutonomousWorldBot || walker.IsTemporaryGroupHelper ||
            walker.IsPlayerLedGroup || AutonomousRealmRaid.GetView(group) != null)
            return !regrouping;
        long now = GameLoop.GameLoopTime;
        GameBot[] candidates;
        CorpseWalkState state;
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
        {
            if (!TryGetSession(group, out Session session) || session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve)
                return !regrouping;
            state = CorpseWalks.GetOrCreateValue(session);
            CorpseWalkClaim claim = state.Claim;
            if (claim != null && !ClaimCurrent(claim, group, now))
            {
                if (now - claim.Started >= CorpseWalkMaxMilliseconds)
                    state.Refused[MemberKey(claim.Corpse)] = now + CorpseWalkRefusalMilliseconds;
                state.Claim = claim = null;
            }
            if (claim != null)
            {
                if (claim.Walker != walker || claim.Corpse != corpseBot)
                    return false;
                if (now < claim.NextSafetyCheck)
                {
                    claim.LastSeen = now;
                    return true;
                }
            }
            else if (now < state.NextChoiceTick)
                return false;
            candidates = claim != null ? [claim.Corpse] : BotMembers(group)
                .Where(member => member != walker && !member.IsAlive && member.ObjectState == GameObject.eObjectState.Active &&
                                 member.CurrentRegionID == walker.CurrentRegionID &&
                                 walker.IsWithinRadius(member, CorpseWalkMaxDistance) &&
                                 !(state.Refused.TryGetValue(MemberKey(member), out long until) && until > now))
                .OrderBy(walker.GetDistanceTo)
                .Take(2)
                .ToArray();
            if (candidates.Length == 0)
            {
                state.NextChoiceTick = now + CorpseWalkChoiceMilliseconds;
                return false;
            }
        }

        // The world scan runs outside the party lock, at most for two corpses every few seconds per party.
        GameBot chosen = null;
        foreach (GameBot candidate in candidates)
        {
            if (CorpseSafeToApproach(walker, candidate, now, out string reason))
            {
                chosen = candidate;
                break;
            }
            using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
            {
                state.Refused[MemberKey(candidate)] = now + CorpseWalkRefusalMilliseconds;
                bool wasWalking = state.Claim?.Walker == walker && state.Claim.Corpse == candidate;
                if (wasWalking)
                    state.Claim = null;
                // A gamebot party's resurrector was refused the walk to a corpse that lies by what killed it (#570 step 4).
                Log.Info($"AUTONOMOUS_CORPSE_WALK_REFUSED bot=\"{walker.Name}\" level={walker.Level} corpse=\"{candidate.Name}\" " +
                         $"distance={walker.GetDistanceTo(candidate)} walking={wasWalking} regrouping={regrouping} reason=\"{reason}\" " +
                         $"at={candidate.CurrentRegionID}:{candidate.X},{candidate.Y}");
            }
        }
        using (AiProfile.Lock(Sync, AiSystem.LockCoordinator))
        {
            if (chosen == null)
            {
                state.NextChoiceTick = now + CorpseWalkChoiceMilliseconds;
                return false;
            }
            CorpseWalkClaim claim = state.Claim;
            if (claim != null && claim.Walker == walker && claim.Corpse == chosen)
            {
                claim.LastSeen = now;
                claim.NextSafetyCheck = now + CorpseWalkSafetyRecheckMilliseconds;
                return chosen == corpseBot;
            }
            if (claim != null && ClaimCurrent(claim, group, now))
                return false;   // another resurrector took it meanwhile
            state.Claim = new CorpseWalkClaim
            {
                Walker = walker, Corpse = chosen, Started = now, LastSeen = now,
                NextSafetyCheck = now + CorpseWalkSafetyRecheckMilliseconds
            };
            // One resurrector of a gamebot party walks to a distant corpse to raise it (#570 step 4).
            Log.Info($"AUTONOMOUS_CORPSE_WALK_START bot=\"{walker.Name}\" class=\"{walker.ClassName}\" level={walker.Level} " +
                     $"corpse=\"{chosen.Name}\" distance={walker.GetDistanceTo(chosen)} mana={walker.ManaPercent} " +
                     $"regrouping={regrouping} at={chosen.CurrentRegionID}:{chosen.X},{chosen.Y}");
            return chosen == corpseBot;
        }
    }

    /// <summary>Caller holds Sync. A resurrector of this party is walking to (or raising at) one of its corpses.</summary>
    private static bool CorpseWalkUnderWay(Session session, long now) =>
        CorpseWalks.TryGetValue(session, out CorpseWalkState state) && state.Claim is { } claim &&
        ClaimCurrent(claim, session.Group, now);

    private static bool ClaimCurrent(CorpseWalkClaim claim, Group group, long now) =>
        IsCorpseWalkCurrent(claim.Walker.IsAlive, claim.Walker.Group == group, !claim.Corpse.IsAlive &&
            claim.Corpse.ObjectState == GameObject.eObjectState.Active && claim.Corpse.CurrentRegionID == claim.Walker.CurrentRegionID,
            claim.Started, Math.Max(claim.LastSeen, ArrivedOrRaisingTick(claim, now)), now);

    // A walker that has arrived (BotBrain no longer asks once the corpse is in spell range) or is raising keeps the grant.
    private static long ArrivedOrRaisingTick(CorpseWalkClaim claim, long now) =>
        claim.Walker.CurrentRegionID == claim.Corpse.CurrentRegionID && claim.Walker.IsWithinRadius(claim.Corpse, CorpseWalkArrivedRadius) ||
        claim.Walker.IsCasting && claim.Walker.castingComponent.SpellHandler?.Spell?.SpellType == eSpellType.Resurrect
            ? now : 0;

    /// <summary>Not into the killer: no remembered danger for the walker's level at the corpse (or straight on the way), and
    /// no aggressive or fighting monster that outclasses the walker beside it.</summary>
    private static bool CorpseSafeToApproach(GameBot walker, GameBot corpse, long now, out string reason)
    {
        reason = CorpseMemoryDanger(walker.CurrentRegionID, walker.X, walker.Y, walker.Level,
            corpse.CurrentRegionID, corpse.X, corpse.Y, DateTime.UtcNow, now);
        if (reason != null)
            return false;
        foreach (GameNPC npc in corpse.GetNPCsInRadius(CorpseThreatRadius))
        {
            if (npc is GameBot || !npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active ||
                (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 ||
                !GameServer.ServerRules.IsAllowedToAttack(walker, npc, true))
                continue;
            bool aggressive = npc.Brain is StandardMobBrain mob && mob.AggroLevel > 0 && mob.AggroRange > 0;
            if (IsCorpseThreat(ConLevels.GetConColor(walker.GetConLevel(npc)), aggressive, npc.InCombat))
            {
                reason = $"{npc.Name} (level {npc.Level}) beside the corpse";
                return false;
            }
        }
        reason = string.Empty;
        return true;
    }
}
