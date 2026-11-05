using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// RvR perception in the frontier scan (owner Chris, 2026-11-01; North Star audit 2026-11-03, challenge 2;
// docs/LIVING-SERVER.md "RvR perception"). The rule is AutonomousRvrPerception; this reads the world for it. Only enemy
// players, their companions and pets go through it.
public sealed partial class AutonomousWorldBotController
{
    internal static readonly AutonomousRvrPerception.Memory RvrPerceptionMemory = new();
    internal static readonly AutonomousRvrPerception.ObserverBudget RvrObservers = new();
    private static readonly ConditionalWeakTable<Group, StrongBox<long>> RvrSpotCalls = new();

    /// <summary>What kind of target this is for the perception rule.</summary>
    public static RvrPerceivedKind PerceivedKind(GameLiving target) => target switch
    {
        GamePlayer => RvrPerceivedKind.Player,
        GameBot bot when IsPlayersBot(bot) => RvrPerceivedKind.PlayerCompanion,
        GameNPC npc when npc.Brain is IControlledBrain pet && pet.GetLivingOwner() is GameLiving owner &&
            (owner is GamePlayer || owner is GameBot ownerBot && IsPlayersBot(ownerBot)) => RvrPerceivedKind.PlayerPet,
        _ => RvrPerceivedKind.Other,
    };

    // A /spawn companion, a temporary helper, or a gamebot in a player's group: it travels with the player.
    private static bool IsPlayersBot(GameBot bot) => !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup;

    private static long RvrTargetKey(GameLiving target) => ((long)target.CurrentRegionID << 32) | (uint)target.ObjectID;

    private static RvrAwarenessProfile RvrProfile() =>
        AutonomousRvrPerception.Profile(AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS));

    private static bool AttackingGroup(GameBot bot, GameLiving target) =>
        bot.Group != null && (target.IsAttacking || target.IsCasting) && target.TargetObject is GameLiving victim &&
        victim.Group == bot.Group;

    private static int RvrGroupSize(GameLiving target)
    {
        GameLiving owner = target is GameNPC { Brain: IControlledBrain pet } ? pet.GetLivingOwner() ?? target : target;
        return owner.Group?.MemberCount ?? 1;
    }

    private static int RvrReactionDelay(GameBot bot, RvrAwarenessProfile profile) =>
        AutonomousRvrPerception.ReactionDelayMilliseconds(bot.Level,
            AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot) : null, Random.Shared.NextDouble(), profile);

    /// <summary>Whether this bot may leave what it is doing to answer a mate's call (its duty lease).</summary>
    public static bool MayAnswerRvrCall(GameBot bot) =>
        bot.IsAutonomousWorldBot && !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper &&
        AutonomousRvrPerception.MayAnswerCall(BotDutyLeases.KindOf(bot));

    /// <summary>
    /// Before the line-of-sight ray: true when the scan could not use this target anyway (a player-kind target this bot
    /// hasn't noticed, beyond the outer range, or outside the sure radius while this second's observer slots for it are
    /// taken), so the ray is not cast. Targets firing on the realm or attacking the group are always checked.
    /// </summary>
    private static bool RvrSkipsLineOfSight(GameBot bot, GameLiving target, bool atObjective, RvrAwarenessProfile profile, long now)
    {
        if (!Properties.RVR_BOT_PERCEPTION || atObjective)
            return false;
        if (!AutonomousRvrPerception.Applies(true, PerceivedKind(target), FiringOnRealm(bot, target), AttackingGroup(bot, target)))
            return false;
        return AutonomousRvrPerception.SkipsLineOfSight(RvrPerceptionMemory, RvrObservers, bot.DatabaseID, (int)bot.Realm,
            RvrTargetKey(target), now, bot.GetDistanceTo(target), profile);
    }

    /// <summary>
    /// The visible targets this bot may engage now: player-kind targets only once noticed (or called by a mate whose call
    /// its duty lets it answer) and after the reaction delay; everything else, and anyone attacking the bot, its
    /// realm-mates near it or its group, as before. A bot holding a keep or working a siege is at an objective and keeps
    /// the old rules.
    /// </summary>
    private GameLiving[] PerceiveRvrTargets(GameBot bot, GameLiving[] visible, bool atObjective)
    {
        if (!Properties.RVR_BOT_PERCEPTION || atObjective || visible.Length == 0)
            return visible;
        RvrAwarenessProfile profile = RvrProfile();
        long now = GameLoop.GameLoopTime;
        double interval = _frontierThreat.LastIntervalMilliseconds;
        bool mayAnswerCall = MayAnswerRvrCall(bot);
        Func<int> reactionDelay = () => RvrReactionDelay(bot, profile);
        var engage = new List<GameLiving>(visible.Length);
        foreach (GameLiving target in visible)
        {
            RvrPerceivedKind kind = PerceivedKind(target);
            if (!AutonomousRvrPerception.Applies(true, kind, FiringOnRealm(bot, target), AttackingGroup(bot, target)))
            {
                engage.Add(target);
                continue;
            }
            long key = RvrTargetKey(target);
            int distance = bot.GetDistanceTo(target);
            var sighting = new RvrSighting(distance, target.InCombat, target.IsCasting, RvrGroupSize(target),
                bot.IsObjectInFront(target, AutonomousRvrPerception.FacingConeDegrees), !target.IsMoving);
            RvrScanOutcome outcome = AutonomousRvrPerception.Scan(RvrPerceptionMemory, RvrObservers, bot.DatabaseID, (int)bot.Realm,
                key, now, sighting, profile, interval, Random.Shared.NextDouble(), reactionDelay, mayAnswerCall);
            if (outcome.Result == RvrScanResult.Engage)
            {
                engage.Add(target);
                continue;
            }
            if (outcome.Result != RvrScanResult.Noticed)
                continue;
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_NOTICE bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} target=\"{target.Name}\" " +
                         $"kind={kind} distance={distance} reasonFactors={AutonomousRvrPerception.Describe(outcome.Factors)} " +
                         $"chance={outcome.Chance:0.###} time_since_first_los_ms={outcome.SinceFirstSightMilliseconds} delayMs={outcome.DelayMilliseconds} " +
                         $"awareness={Properties.RVR_BOT_AWARENESS} region={bot.CurrentRegionID}");
            if (Properties.RVR_BOT_SPOTTERS)
                AlertRvrMates(bot, target, key, now, profile);
            if (outcome.DelayMilliseconds <= 0)
                engage.Add(target);
        }
        return engage.ToArray();
    }

    /// <summary>Whether a mate can hear the spotter's call at all: an autonomous gamebot of its realm, not one a player has.</summary>
    public static bool CanHearRvrCall(GameBot mate, GameBot spotter) =>
        mate.IsAutonomousWorldBot && !mate.IsTemporaryGroupHelper && !mate.IsPlayerLedGroup && mate.DatabaseID > 0 &&
        mate.Realm == spotter.Realm;

    private static RvrSpotterMate SpotterMate(GameBot mate, GameBot spotter) =>
        new(mate.DatabaseID, mate.X, mate.Y, mate.Z, mate.IsAlive, mate.CurrentRegionID == spotter.CurrentRegionID, MayAnswerRvrCall(mate));

    /// <summary>
    /// The spotter tells its group mates near it, each of whom treats the target as noticed after its own delay. Other
    /// groups of its warband hear it relayed: each group as a whole, half the time, 4 s later. A mate whose duty (a raid,
    /// a battleground assault, a player's group) doesn't let it answer is not told (AutonomousRvrPerception.CallRecipients).
    /// </summary>
    private static void AlertRvrMates(GameBot spotter, GameLiving target, long key, long now, RvrAwarenessProfile profile)
    {
        Group spotterGroup = spotter.Group;
        var mates = new Dictionary<long, GameBot>();
        var groupMates = new List<RvrSpotterMate>();
        foreach (GameBot mate in (spotterGroup?.GetMembersInTheGroup() ?? Enumerable.Empty<GameLiving>()).OfType<GameBot>())
            if (CanHearRvrCall(mate, spotter) && mates.TryAdd(mate.DatabaseID, mate))
                groupMates.Add(SpotterMate(mate, spotter));
        var otherGroups = new Dictionary<Group, List<RvrSpotterMate>>();
        foreach (GameBot mate in (RealmWarbandSupport.Get(spotter)?.Members ?? []).OfType<GameBot>())
        {
            if (mate.Group == null || mate.Group == spotterGroup || !CanHearRvrCall(mate, spotter) || !mates.TryAdd(mate.DatabaseID, mate))
                continue;
            if (!otherGroups.TryGetValue(mate.Group, out var members))
                otherGroups[mate.Group] = members = new List<RvrSpotterMate>();
            members.Add(SpotterMate(mate, spotter));
        }
        if (mates.Count == 0)
            return;

        List<RvrCallee> callees = AutonomousRvrPerception.CallRecipients(spotter.DatabaseID, spotter.X, spotter.Y, spotter.Z,
            groupMates, otherGroups.Values, profile.SpotterRadius, () => Random.Shared.NextDouble(), out int held);
        int told = 0, relayed = 0;
        foreach (RvrCallee callee in callees)
            if (RvrPerceptionMemory.Alert(callee.Id, key, now, callee.ExtraDelayMilliseconds + RvrReactionDelay(mates[callee.Id], profile)))
            {
                if (callee.Relayed) relayed++;
                else told++;
            }
        if (told + relayed == 0 && held == 0)
            return;
        string call = AutonomousRvrPerception.SpotCall(RvrGroupSize(target), target.X - spotter.X, target.Y - spotter.Y);
        // Spoken only in a group with a real player (no one hears a bot-only /group). Warbands have no chat channel of
        // their own yet, so a warband call is the log line only.
        string channel = "log";
        if (told > 0 && AutonomousBotVoices.Enabled && spotterGroup != null && AutonomousLivingServer.GroupHasPlayer(spotterGroup))
        {
            StrongBox<long> last = RvrSpotCalls.GetValue(spotterGroup, _ => new StrongBox<long>(long.MinValue / 2));
            bool speak;
            lock (last)
            {
                speak = now - last.Value >= AutonomousRvrPerception.SpotCallGapMilliseconds;
                if (speak)
                    last.Value = now;
            }
            if (speak)
            {
                AutonomousBotGroupChatter.Send(spotter, call, "none");
                channel = "group";
            }
        }
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_SPOT bot={spotter.Name} id={spotter.DatabaseID} target=\"{target.Name}\" told={told} relayed={relayed} " +
                     $"held={held} call=\"{call}\" channel={channel} region={spotter.CurrentRegionID}");
    }
}
