using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// RvR perception in the frontier scan (owner Chris, 2026-11-01; docs/BOT-BUILDS-AND-RVR.md "RvR perception"). The rule
// is AutonomousRvrPerception; this reads the world for it. Only enemy players, their companions and pets go through it.
public sealed partial class AutonomousWorldBotController
{
    internal static readonly AutonomousRvrPerception.Memory RvrPerceptionMemory = new();
    private static readonly ConditionalWeakTable<Group, StrongBox<long>> RvrSpotCalls = new();

    /// <summary>What kind of target this is for the perception rule.</summary>
    internal static RvrPerceivedKind PerceivedKind(GameLiving target) => target switch
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

    /// <summary>
    /// The visible targets this bot may engage now: player-kind targets only once noticed and after the reaction delay;
    /// everything else, and anyone attacking the bot, its realm-mates near it or its group, as before. A bot holding a
    /// keep or working a siege is at an objective and keeps the old rules.
    /// </summary>
    private GameLiving[] PerceiveRvrTargets(GameBot bot, GameLiving[] visible, bool atObjective)
    {
        if (!Properties.RVR_BOT_PERCEPTION || atObjective || visible.Length == 0)
            return visible;
        RvrAwarenessProfile profile = AutonomousRvrPerception.Profile(AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS));
        long now = GameLoop.GameLoopTime;
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
            var state = RvrPerceptionMemory.Seen(bot.DatabaseID, key, now);
            if (state == AutonomousRvrPerception.Memory.State.Ready)
            {
                engage.Add(target);
                continue;
            }
            if (state == AutonomousRvrPerception.Memory.State.Reacting)
                continue;
            int distance = bot.GetDistanceTo(target);
            var sighting = new RvrSighting(distance, target.InCombat, target.IsCasting, RvrGroupSize(target),
                bot.IsObjectInFront(target, AutonomousRvrPerception.FacingConeDegrees), !target.IsMoving);
            if (!AutonomousRvrPerception.Notices(sighting, profile, Random.Shared.NextDouble(), out RvrNoticeFactors factors))
                continue;
            int delay = RvrReactionDelay(bot, profile);
            RvrPerceptionMemory.Notice(bot.DatabaseID, key, now, delay);
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_NOTICE bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} target=\"{target.Name}\" " +
                         $"kind={kind} distance={distance} reasonFactors={AutonomousRvrPerception.Describe(factors)} delayMs={delay} " +
                         $"awareness={Properties.RVR_BOT_AWARENESS} region={bot.CurrentRegionID}");
            if (Properties.RVR_BOT_SPOTTERS)
                AlertRvrMates(bot, target, key, now, profile);
            if (delay <= 0)
                engage.Add(target);
        }
        return engage.ToArray();
    }

    /// <summary>The spotter tells its group and warband mates near it; each treats the target as noticed after its own delay.</summary>
    private static void AlertRvrMates(GameBot spotter, GameLiving target, long key, long now, RvrAwarenessProfile profile)
    {
        IEnumerable<GameBot> candidates = (spotter.Group?.GetMembersInTheGroup() ?? Enumerable.Empty<GameLiving>())
            .Concat(RealmWarbandSupport.Get(spotter)?.Members ?? [])
            .OfType<GameBot>()
            .Where(mate => mate.IsAutonomousWorldBot && !mate.IsTemporaryGroupHelper && mate.DatabaseID > 0 && mate.Realm == spotter.Realm);
        var mates = new Dictionary<long, GameBot>();
        foreach (GameBot mate in candidates)
            mates.TryAdd(mate.DatabaseID, mate);
        if (mates.Count == 0)
            return;
        long[] recipients = AutonomousRvrPerception.SpotterRecipients(spotter.DatabaseID, spotter.X, spotter.Y, spotter.Z,
            mates.Values.Select(mate => new RvrSpotterMate(mate.DatabaseID, mate.X, mate.Y, mate.Z, mate.IsAlive,
                mate.CurrentRegionID == spotter.CurrentRegionID)), profile.SpotterRadius);
        int told = 0;
        foreach (long id in recipients)
            if (RvrPerceptionMemory.Alert(id, key, now, RvrReactionDelay(mates[id], profile)))
                told++;
        if (told == 0)
            return;
        string call = AutonomousRvrPerception.SpotCall(RvrGroupSize(target), target.X - spotter.X, target.Y - spotter.Y);
        // Spoken only in a group with a real player (no one hears a bot-only /group). Warbands have no chat channel of
        // their own yet, so a warband call is the log line only.
        string channel = "log";
        Group group = spotter.Group;
        if (AutonomousBotVoices.Enabled && group != null && AutonomousLivingServer.GroupHasPlayer(group))
        {
            StrongBox<long> last = RvrSpotCalls.GetValue(group, _ => new StrongBox<long>(long.MinValue / 2));
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
            Log.Info($"RVR_SPOT bot={spotter.Name} id={spotter.DatabaseID} target=\"{target.Name}\" told={told} call=\"{call}\" channel={channel} " +
                     $"region={spotter.CurrentRegionID}");
    }
}
