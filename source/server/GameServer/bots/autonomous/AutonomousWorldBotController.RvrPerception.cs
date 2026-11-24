using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.Effects;
using DOL.GS.RealmAbilities;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// RvR perception in the frontier scan (owner Chris, 2026-11-01; North Star audit 2026-11-03, challenge 2;
// docs/LIVING-SERVER.md "RvR perception"). The rule is AutonomousRvrPerception; this reads the world for it. Enemy
// players, their companions and pets go through it, and since 1.2 work package 1 ("Eyes", docs/RVR-FIELD-1.2.md) enemy
// gamebots and their pets too, keyed by enemy party (RvrPartyKey), looked for by two lookers per group (RvrScanRoster),
// with close-range stealth detection (RvrStealthNotice).
public sealed partial class AutonomousWorldBotController
{
    internal static readonly AutonomousRvrPerception.Memory RvrPerceptionMemory = new();
    internal static readonly AutonomousRvrPerception.ObserverBudget RvrObservers = new();
    private static readonly ConditionalWeakTable<Group, StrongBox<long>> RvrSpotCalls = new();

    /// <summary>What one frontier scan knows about the rules that apply to it (see RvrFieldRules).</summary>
    private readonly record struct RvrScanContext(bool AtObjective, RvrFieldRules Rules, RvrBotAwareness Awareness,
        RvrAwarenessProfile Profile);

    private static RvrScanContext RvrContext(BotBrain brain, GameBot bot, bool committed)
    {
        RvrBotAwareness awareness = AutonomousRvrPerception.ParseAwareness(Properties.RVR_BOT_AWARENESS);
        bool atObjective = RvrAtObjective.Within(committed, BotSiegeRuntime.Assigned(bot), bot.CurrentRegionID, bot.X, bot.Y,
            committed ? bot.TempProperties.GetProperty<RvrObjectivePoint?>(RvrAtObjective.PointProperty, null) : null);
        RvrFieldRules rules = Properties.RVR_BOT_PERCEPTION && !atObjective && Properties.RVR_BOT_FIELD_TACTICS
            ? RvrFieldRules.For(true, brain.RvrTacticsActive(), RvrTacticsSwitches.Current)
            : RvrFieldRules.None;
        return new(atObjective, rules, awareness, AutonomousRvrPerception.Profile(awareness));
    }

    /// <summary>What kind of target this is for the perception rule.</summary>
    public static RvrPerceivedKind PerceivedKind(GameLiving target) => target switch
    {
        GamePlayer => RvrPerceivedKind.Player,
        GameBot bot when IsPlayersBot(bot) => RvrPerceivedKind.PlayerCompanion,
        GameBot => RvrPerceivedKind.EnemyBot,
        GameNPC npc when npc.Brain is IControlledBrain pet && pet.GetLivingOwner() is GameLiving owner =>
            owner is GamePlayer || owner is GameBot ownerBot && IsPlayersBot(ownerBot) ? RvrPerceivedKind.PlayerPet :
            owner is GameBot ? RvrPerceivedKind.EnemyBotPet : RvrPerceivedKind.Other,
        _ => RvrPerceivedKind.Other,
    };

    // A /spawn companion, a temporary helper, a hired companion, or a gamebot in a player's group: it travels with the player.
    private static bool IsPlayersBot(GameBot bot) =>
        !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper || bot.IsPlayerLedGroup || bot.IsHiredCompanion;

    private static long RvrTargetKey(GameLiving target) => RvrPartyKey.TargetKey(target);

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
        bot.IsAutonomousWorldBot && !bot.IsPlayerLedGroup && !bot.IsTemporaryGroupHelper && !bot.IsHiredCompanion &&
        AutonomousRvrPerception.MayAnswerCall(BotDutyLeases.KindOf(bot));

    /// <summary>
    /// Lookers (rvr_bot_group_lookers): whether this bot runs its scan now. A soloer and each of its group's two lookers
    /// do; another member only when something woke it (a call in its memory, a group mate fighting, the plan's target).
    /// </summary>
    private static RvrScanRole RvrScanTurn(BotBrain brain, GameBot bot, in RvrScanContext context, long now)
    {
        if (!context.Rules.Lookers)
            return RvrScanRole.Everyone;
        Group group = bot.Group;
        if (group == null)
            return RvrScanRole.Solo;
        var ids = new List<long>(8);
        bool fighting = false;
        foreach (GameLiving member in group.GetMembersInTheGroup())
        {
            if (member is not GameBot mate || !mate.IsAutonomousWorldBot || !mate.IsAlive ||
                mate.CurrentRegionID != bot.CurrentRegionID)
                continue;
            ids.Add(mate.DatabaseID);
            fighting |= mate != bot && (mate.InCombat || mate.IsAttacking);
        }
        bool isLooker = ids.Count > RvrScanRoster.LookersPerGroup && RvrScanRoster.IsLooker(bot.DatabaseID, ids, now);
        bool alerted = !isLooker && RvrPerceptionMemory.HasNews(bot.DatabaseID, now);
        bool planTarget = !isLooker && !alerted && !fighting && brain.PeekRvrPlan()?.CalledTarget != null;
        return RvrScanRoster.Decide(true, ids.Count, isLooker, alerted, fighting, planTarget);
    }

    private static void LogRvrScanBudget(long now)
    {
        List<string> lines = RvrScanBudget.TakeLines(now);
        if (lines == null || !Log.IsInfoEnabled)
            return;
        foreach (string line in lines)
            Log.Info(line);
    }

    /// <summary>
    /// Before the line-of-sight ray: true when the scan could not use this target anyway (a perceived target this bot
    /// hasn't noticed, beyond the outer range, or outside the sure radius while this second's observer slots for its
    /// party are taken and the bot is not already watching that party), so the ray is not cast. Targets firing on the
    /// realm or attacking the group, and stealthed targets, are always checked.
    /// </summary>
    private static bool RvrSkipsLineOfSight(GameBot bot, GameLiving target, in RvrScanContext context, long now)
    {
        if (!Properties.RVR_BOT_PERCEPTION || context.AtObjective || target.IsStealthed)
            return false;
        RvrPerceivedKind kind = AutonomousRvrPerception.Effective(PerceivedKind(target), context.Rules.PerceiveBots);
        if (!AutonomousRvrPerception.Applies(true, kind, FiringOnRealm(bot, target), AttackingGroup(bot, target)))
            return false;
        return AutonomousRvrPerception.SkipsLineOfSight(RvrPerceptionMemory, RvrObservers, bot.DatabaseID, (int)bot.Realm,
            RvrTargetKey(target), RvrPartyKey.Of(target), now, bot.GetDistanceTo(target), context.Profile);
    }

    /// <summary>
    /// The visible targets this bot may engage now: perceived targets only once noticed (or called by a mate whose call
    /// its duty lets it answer) and after the reaction delay, one look per enemy party; everything else, and anyone
    /// attacking the bot, its realm-mates near it or its group, as before. A bot at its objective (holding a keep or
    /// working a siege there) keeps the old rules.
    /// </summary>
    private GameLiving[] PerceiveRvrTargets(GameBot bot, GameLiving[] visible, in RvrScanContext context)
    {
        if (!Properties.RVR_BOT_PERCEPTION || context.AtObjective || visible.Length == 0)
            return visible;
        RvrAwarenessProfile profile = context.Profile;
        long now = GameLoop.GameLoopTime;
        double interval = _frontierThreat.LastIntervalMilliseconds;
        bool mayAnswerCall = MayAnswerRvrCall(bot);
        Func<int> reactionDelay = () => RvrReactionDelay(bot, profile);
        var engage = new List<GameLiving>(visible.Length);
        var parties = new List<(long Party, List<GameLiving> Members)>();
        foreach (GameLiving target in visible)
        {
            if (target.IsStealthed)
            {
                if (context.Rules.Stealth && PerceiveStealthed(bot, target, context, now, interval, reactionDelay))
                    engage.Add(target);
                continue;
            }
            RvrPerceivedKind kind = AutonomousRvrPerception.Effective(PerceivedKind(target), context.Rules.PerceiveBots);
            if (!AutonomousRvrPerception.Applies(true, kind, FiringOnRealm(bot, target), AttackingGroup(bot, target)))
            {
                engage.Add(target);
                continue;
            }
            long party = RvrPartyKey.Of(target);
            int index = parties.FindIndex(entry => entry.Party == party);
            if (index < 0)
                parties.Add((party, new List<GameLiving>(2) { target }));
            else
                parties[index].Members.Add(target);
        }
        var results = new List<RvrMemberOutcome>(visible.Length);
        foreach ((long party, List<GameLiving> targets) in parties)
        {
            var members = new RvrPartyMember[targets.Count];
            for (int i = 0; i < targets.Count; i++)
            {
                GameLiving target = targets[i];
                members[i] = new(RvrTargetKey(target), new RvrSighting(bot.GetDistanceTo(target), target.InCombat, target.IsCasting,
                    RvrGroupSize(target), bot.IsObjectInFront(target, AutonomousRvrPerception.FacingConeDegrees), !target.IsMoving),
                    target.X, target.Y, target.Z);
            }
            results.Clear();
            AutonomousRvrPerception.ScanParty(RvrPerceptionMemory, RvrObservers, bot.DatabaseID, (int)bot.Realm, party, members, now,
                profile, interval, Random.Shared.NextDouble(), reactionDelay, mayAnswerCall, results);
            int primary = -1, noticed = 0;
            bool rolled = false, capped = false;
            for (int i = 0; i < results.Count; i++)
            {
                RvrMemberOutcome outcome = results[i];
                switch (outcome.Result)
                {
                    case RvrScanResult.Engage:
                        engage.Add(targets[i]);
                        break;
                    case RvrScanResult.Noticed:
                        noticed++;
                        if (outcome.DelayMilliseconds <= 0)
                            engage.Add(targets[i]);
                        if ((outcome.Factors & RvrNoticeFactors.Party) == 0 && primary < 0)
                            primary = i;
                        break;
                    case RvrScanResult.Capped:
                        capped = true;
                        break;
                    case RvrScanResult.Unseen when outcome.Chance > 0:
                        rolled = true;
                        break;
                }
            }
            if (capped)
                RvrScanBudget.Add(bot.Realm, RvrScanBudget.Counter.Capped);
            if (rolled || primary >= 0)
                RvrScanBudget.Add(bot.Realm, RvrScanBudget.Counter.Rolls);
            if (primary < 0)
                continue;
            RvrScanBudget.Add(bot.Realm, RvrScanBudget.Counter.Notices);
            GameLiving spotted = targets[primary];
            RvrMemberOutcome seen = results[primary];
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_NOTICE bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} target=\"{spotted.Name}\" " +
                         $"kind={PerceivedKind(spotted)} party={RvrPartyKey.Label(party)} partyNoticed={noticed} partyVisible={targets.Count} " +
                         $"distance={(int)members[primary].Sighting.Distance} reasonFactors={AutonomousRvrPerception.Describe(seen.Factors)} " +
                         $"chance={seen.Chance:0.###} time_since_first_los_ms={seen.SinceFirstSightMilliseconds} delayMs={seen.DelayMilliseconds} " +
                         $"awareness={Properties.RVR_BOT_AWARENESS} region={bot.CurrentRegionID}");
            // #505: the player (or his pet) noticed by this group: RVR_PLAYER_NOTICED once per contact.
            RvrPlayerRecord.Noticed(bot, spotted, (int)members[primary].Sighting.Distance, seen.SinceFirstSightMilliseconds, called: false);
            if (Properties.RVR_BOT_SPOTTERS)
            {
                var keys = new List<long>(noticed);
                for (int i = 0; i < results.Count; i++)
                    if (results[i].Result == RvrScanResult.Noticed)
                        keys.Add(results[i].Key);
                AlertRvrMates(bot, spotted, keys, now, profile);
            }
        }
        return engage.ToArray();
    }

    /// <summary>The stealth rule's facts for this bot looking at this stealthed enemy (reads the game).</summary>
    private static RvrStealthSighting StealthSighting(GameBot bot, GameLiving target, bool lineOfSight) => new(
        bot.GetDistanceTo(target), bot.Level, target.GetModifiedSpecLevel(Specs.Stealth),
        ObserverDetectHidden: bot.HasAbility(Abilities.DetectHidden),
        TargetDetectHidden: target.HasAbility(Abilities.DetectHidden),
        ObserverSeeHidden: bot.HasAbilityType(typeof(AtlasOF_SeeHidden)),
        TargetAssassin: target switch
        {
            GamePlayer player => player.CharacterClass?.IsAssassin == true,
            GameBot enemy => enemy.CharacterClass?.IsAssassin == true,
            _ => false,
        },
        Camouflage: target.effectListComponent.ContainsEffectForEffectType(eEffect.Camouflage),
        Vanish: target.effectListComponent.ContainsEffectForEffectType(eEffect.Vanish) ||
            target is GamePlayer vanisher && vanisher.EffectList.GetOfType<VanishEffect>() != null,
        ObserverStealthBonus: bot.BaseBuffBonusCategory[eProperty.Skill_Stealth],
        ObserverInCombatWithOther: bot.InCombat && bot.TargetObject != target,
        LineOfSight: lineOfSight);

    /// <summary>
    /// A stealthed enemy in line of sight (the scan cast the ray): engaged once noticed by the close-range stealth rule
    /// and after the reaction delay. No call: the bot's mates cannot see it.
    /// </summary>
    private static bool PerceiveStealthed(GameBot bot, GameLiving target, in RvrScanContext context, long now, double interval,
        Func<int> reactionDelay)
    {
        RvrStealthSighting sighting = StealthSighting(bot, target, lineOfSight: true);
        double chance = RvrStealthNotice.ChancePerScan(sighting, context.Awareness, interval);
        RvrScanOutcome outcome = AutonomousRvrPerception.ScanStealthed(RvrPerceptionMemory, bot.DatabaseID, RvrTargetKey(target), now,
            chance, Random.Shared.NextDouble(), reactionDelay);
        if (outcome.Result is RvrScanResult.Noticed || outcome.Result is RvrScanResult.Unseen && chance > 0)
            RvrScanBudget.Add(bot.Realm, RvrScanBudget.Counter.StealthRolls);
        if (outcome.Result == RvrScanResult.Engage)
            return true;
        if (outcome.Result != RvrScanResult.Noticed)
            return false;
        RvrScanBudget.Add(bot.Realm, RvrScanBudget.Counter.Notices);
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_NOTICE bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} target=\"{target.Name}\" " +
                     $"kind={PerceivedKind(target)} party={RvrPartyKey.Label(RvrPartyKey.Of(target))} partyNoticed=1 partyVisible=1 " +
                     $"distance={(int)sighting.Distance} reasonFactors={AutonomousRvrPerception.Describe(outcome.Factors)} " +
                     $"chance={outcome.Chance:0.###} stealthLimit={RvrStealthNotice.Limit(sighting)} " +
                     $"time_since_first_los_ms={outcome.SinceFirstSightMilliseconds} delayMs={outcome.DelayMilliseconds} " +
                     $"awareness={Properties.RVR_BOT_AWARENESS} region={bot.CurrentRegionID}");
        RvrPlayerRecord.Noticed(bot, target, (int)sighting.Distance, outcome.SinceFirstSightMilliseconds, called: false);
        return outcome.DelayMilliseconds <= 0;
    }

    /// <summary>Whether a mate can hear the spotter's call at all: an autonomous gamebot of its realm, not one a player has.</summary>
    public static bool CanHearRvrCall(GameBot mate, GameBot spotter) =>
        mate.IsAutonomousWorldBot && !mate.IsTemporaryGroupHelper && !mate.IsPlayerLedGroup && !mate.IsHiredCompanion &&
        mate.DatabaseID > 0 &&
        mate.Realm == spotter.Realm;

    private static RvrSpotterMate SpotterMate(GameBot mate, GameBot spotter) =>
        new(mate.DatabaseID, mate.X, mate.Y, mate.Z, mate.IsAlive, mate.CurrentRegionID == spotter.CurrentRegionID, MayAnswerRvrCall(mate));

    /// <summary>
    /// The spotter tells its group mates near it, each of whom treats the noticed enemies (<paramref name="keys"/>: the
    /// one it saw and the party mates noticed with it) as noticed after its own delay. Other groups of its warband hear it
    /// relayed: each group as a whole, half the time, 4 s later. A mate whose duty (a raid, a battleground assault, a
    /// player's group) doesn't let it answer is not told (AutonomousRvrPerception.CallRecipients).
    /// </summary>
    private static void AlertRvrMates(GameBot spotter, GameLiving target, IReadOnlyList<long> keys, long now, RvrAwarenessProfile profile)
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
        {
            int delay = callee.ExtraDelayMilliseconds + RvrReactionDelay(mates[callee.Id], profile);
            bool news = false;
            foreach (long key in keys)
                news |= RvrPerceptionMemory.Alert(callee.Id, key, now, delay);
            if (!news)
                continue;
            if (callee.Relayed)
            {
                relayed++;
                // #505: another group of the warband told of the player (it did not see him itself).
                if (target is GamePlayer)
                    RvrPlayerRecord.Noticed(mates[callee.Id], target, (int)mates[callee.Id].GetDistanceTo(target), -1, called: true);
            }
            else told++;
        }
        if (told + relayed == 0 && held == 0)
            return;
        string call = AutonomousRvrPerception.SpotCall(Math.Max(keys.Count, RvrGroupSize(target)), target.X - spotter.X, target.Y - spotter.Y);
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
            Log.Info($"RVR_SPOT bot={spotter.Name} id={spotter.DatabaseID} target=\"{target.Name}\" kind={PerceivedKind(target)} " +
                     $"noticed={keys.Count} told={told} relayed={relayed} held={held} call=\"{call}\" channel={channel} region={spotter.CurrentRegionID}");
    }
}
