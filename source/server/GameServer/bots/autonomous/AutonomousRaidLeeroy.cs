using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Leeroy (bot_living_leeroy, owner 2026-10-27): one end-game raid in twenty has a raider who can't wait. While the raid
/// musters for its final encounter (a dungeon's final boss, a landed dragon), once part of the raid is there but not
/// all of it, he yells his name and charges in alone; the boss is in the fight, so the raid has to follow him in with
/// whoever is there. The raid's own attempt and wipe rules take it from there (a wipe still musters again).
/// A Daredevil or No-lifer melee bot is picked first; healers and support classes never charge.
/// </summary>
public static class AutonomousRaidLeeroy
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(AutonomousRaidLeeroy));

    public static bool On => Properties.BOT_LIVING_LEEROY;

    /// <summary>The share of raids that roll a Leeroy (owner: 5%).</summary>
    public const double Chance = 0.05;
    /// <summary>He waits until the muster has stood this long (long enough to look like the raid is getting ready).</summary>
    public const long EarliestMusterMilliseconds = 60_000;
    public const long EarliestStagingMilliseconds = 3 * 60_000;
    /// <summary>The fewest raiders at the muster before he goes (the raid's own minimum at a target).</summary>
    public const int FewestFollowers = 8;

    public static bool Rolls(double draw) => draw < Chance;

    private static int _forceNextRaid, _chargeNow;

    /// <summary>GM test (/lstest leeroy): the next realm raid to form has a Leeroy.</summary>
    public static void ForceNextRaid() => Volatile.Write(ref _forceNextRaid, 1);

    /// <summary>The raid forming now has a Leeroy: the 5% roll, or once after /lstest leeroy.</summary>
    public static bool RollForRaid(double draw) => Interlocked.Exchange(ref _forceNextRaid, 0) == 1 || Rolls(draw);

    /// <summary>GM test (/lstest leeroy now): a raid with a Leeroy that is gathering for its final encounter charges at once.</summary>
    public static void ChargeNow() => Volatile.Write(ref _chargeNow, 1);

    /// <summary>Whether a raid with a Leeroy goes now: its own rules, or a GM's "now" with at least two raiders there (used once).</summary>
    public static bool GoesNow(bool rulesSay, int present) =>
        rulesSay || present >= 2 && Interlocked.Exchange(ref _chargeNow, 0) == 1;

    /// <summary>
    /// Whether he charges now: a first attempt at the encounter (never a regroup after a wipe), the muster standing for a
    /// minute, a quarter of what the raid waits for already there, and not all of it (then it's just the pull). Pure.
    /// </summary>
    public static bool ShouldCharge(int mustered, int needed, long musterMilliseconds, int failedAttempts, bool regrouping) =>
        !regrouping && failedAttempts == 0 && musterMilliseconds >= EarliestMusterMilliseconds &&
        mustered < needed && mustered >= Math.Max(FewestFollowers, needed / 4);

    /// <summary>
    /// The raid reached the muster all at once, so the moment "part of it is there" never came: he jumps the pull instead,
    /// a first attempt with the whole muster there. Pure. (Overnight check 2026-10-29: the Crystal Cave raid rolled a Leeroy,
    /// went from 1 to 52 raiders at Xanxicar in ten seconds and engaged as usual, so he never charged.)
    /// </summary>
    public static bool JumpsThePull(int mustered, int needed, int failedAttempts, bool regrouping) =>
        !regrouping && failedAttempts == 0 && mustered >= needed && mustered >= FewestFollowers;

    /// <summary>A dragon raid at its staging: half the raid there, not all, after three minutes. Pure.</summary>
    public static bool ShouldChargeStaging(int present, int required, long stagingMilliseconds) =>
        stagingMilliseconds >= EarliestStagingMilliseconds && present < required &&
        present >= Math.Max(FewestFollowers, required / 2);

    /// <summary>Who may be Leeroy: a melee or ranged damage dealer, never a healer or a support class. Pure.</summary>
    public static bool CanBeLeeroy(eCharacterClass characterClass) =>
        !BotPartyRoles.IsHealingClass(characterClass) && BotPartyRoles.For(characterClass) != BotPartyRole.Support &&
        !BotPartyRoles.IsHybridSupport(characterClass);

    /// <summary>How much more likely a personality is to be the one who charges.</summary>
    public static double Eagerness(BotPersonalityType type) => type switch
    {
        BotPersonalityType.Daredevil => 6,
        BotPersonalityType.NoLifer => 3,
        BotPersonalityType.Crafter => 0.3,
        _ => 1,
    };

    public static GameBot Pick(IEnumerable<GameLiving> raiders, Random random = null)
    {
        random ??= Random.Shared;
        var candidates = raiders.OfType<GameBot>()
            .Where(b => b.IsAutonomousWorldBot && b.IsAlive && b.ObjectState == GameObject.eObjectState.Active && b.CharacterClass != null &&
                        CanBeLeeroy((eCharacterClass)b.CharacterClass.ID) && b.Brain is BotBrain)
            .Select(b => (Bot: b, Weight: Eagerness(AutonomousBotPersonality.For(b).Type)))
            .ToList();
        double total = candidates.Sum(c => c.Weight);
        if (total <= 0)
            return null;
        double draw = random.NextDouble() * total;
        foreach (var c in candidates)
        {
            if (draw < c.Weight)
                return c.Bot;
            draw -= c.Weight;
        }
        return candidates[^1].Bot;
    }

    public static readonly string[] Shouts =
    [
        "LEEEEEEROOOOOOOOY JENKINS!!!",
        "LEEEEROOOOOOOY JEEENKIIINS!",
        "LEEEEEEEEROOOOOOOOOOOY JENKINSSSSS!!!",
    ];

    /// <summary>What the raiders beside him say, a few seconds apart.</summary>
    public static readonly string[] Reactions =
    [
        "oh my god he just ran in",
        "WHAT ARE YOU DOING",
        "stick to the plan!!",
        "we werent even buffed yet",
        "oh no. oh no no no",
        "great. GO GO GO",
        "everyone in, now!!",
        "was that the plan? nobody said that was the plan",
        "wipe incoming",
        "omg",
        "well at least he has chicken",
        "rez him after... if we live",
    ];

    /// <summary>The /faction line a raider sends after he charges ({leeroy}, {boss}).</summary>
    public static readonly string[] FactionCalls =
    [
        "{leeroy} just charged {boss} alone, the whole raid is going in after him",
        "{leeroy} pulled {boss} before we were ready. here we go",
        "raid update: {leeroy} happened. we are fighting {boss} now",
    ];

    /// <summary>After the raid: a raider's /faction line, by outcome ({leeroy}, {boss}).</summary>
    public static readonly string[] AfterWin =
    [
        "cant believe that worked. {leeroy} is a legend",
        "{boss} down even after {leeroy} pulled early. never doing that again",
        "we won. {leeroy} still owes everyone an apology",
    ];
    public static readonly string[] AfterWipe =
    [
        "thanks a lot {leeroy}",
        "wiped on {boss}. we all know whose fault that was, {leeroy}",
        "{leeroy} cost us the raid. at least he has chicken",
    ];

    public static string Fill(string line, string leeroy, string boss) =>
        line.Replace("{leeroy}", leeroy ?? "someone").Replace("{boss}", boss ?? "the boss");

    /// <summary>
    /// He yells and charges. False when the bot can't attack the boss (then nothing is said and the raid goes on as it
    /// was). The reactions and the realm call follow on timers.
    /// </summary>
    public static bool Charge(GameBot leeroy, GameNPC boss, IReadOnlyCollection<GameLiving> raiders, string raidName, Random random = null)
    {
        random ??= Random.Shared;
        if (leeroy?.Brain is not BotBrain brain || boss == null || !boss.IsAlive)
            return false;
        if (!brain.OrderPull(boss))
            return false;
        string shout = Shouts[random.Next(Shouts.Length)];
        if (leeroy.Yell(shout) && AutonomousLivingServer.PlayerHears(leeroy))
            BotChatTranscript.Bot("yell", leeroy.Name, leeroy.Realm, shout);
        // And in /faction, so the whole realm hears him go (owner 2026-10-29: "the leeroy character should also be shouting
        // in faction when they run in"). Sent as written: the speech pass would tidy it.
        if (AutonomousBotChatCoordinator.HasFactionAudience(leeroy.Realm))
            AutonomousBotChatCoordinator.BroadcastFactionAsWritten(leeroy.Name, leeroy.Realm, shout);
        Log.Info($"RAID_LEEROY bot=\"{leeroy.Name}\" class=\"{leeroy.CharacterClass?.Name}\" boss=\"{boss.Name}\" raid=\"{raidName}\" " +
                 $"region={leeroy.CurrentRegionID} raiders={raiders.Count}");

        // Two or three raiders react, a few seconds apart; then one tells the realm.
        GameBot[] others = raiders.OfType<GameBot>().Where(b => b != leeroy && b.IsAlive && b.IsAutonomousWorldBot)
            .OrderBy(_ => random.Next()).Take(3).ToArray();
        var said = new HashSet<int>();
        int delay = 1_500;
        foreach (GameBot speaker in others.Take(2 + random.Next(2)))
        {
            int pick;
            do pick = random.Next(Reactions.Length); while (!said.Add(pick) && said.Count < Reactions.Length);
            string line = Reactions[pick];
            GameBot who = speaker;
            delay += 1_200 + random.Next(2_500);
            _ = new ECSGameTimer(who, _ =>
            {
                if (who.ObjectState == GameObject.eObjectState.Active && who.IsAlive)
                    who.Say(line);
                return 0;
            }, delay);
        }
        GameBot caller = others.FirstOrDefault();
        if (caller != null)
        {
            string call = Fill(FactionCalls[random.Next(FactionCalls.Length)], leeroy.Name, boss.Name);
            _ = new ECSGameTimer(caller, _ =>
            {
                if (caller.ObjectState == GameObject.eObjectState.Active && AutonomousBotChatCoordinator.HasFactionAudience(caller.Realm))
                    AutonomousBotChatCoordinator.BroadcastFaction(caller.Name, caller.Realm, call);
                return 0;
            }, delay + 6_000 + random.Next(6_000));
        }
        return true;
    }

    /// <summary>When the raid ends, a raider tells /faction how it went with him.</summary>
    public static void AfterRaid(eRealm realm, string leeroy, string boss, bool won, IEnumerable<GameLiving> raiders, Random random = null)
    {
        random ??= Random.Shared;
        if (string.IsNullOrEmpty(leeroy) || !AutonomousBotChatCoordinator.HasFactionAudience(realm))
            return;
        GameBot speaker = raiders?.OfType<GameBot>().Where(b => b.IsAutonomousWorldBot && b.Name != leeroy && b.ObjectState == GameObject.eObjectState.Active)
            .OrderBy(_ => random.Next()).FirstOrDefault();
        if (speaker == null)
            return;
        string[] lines = won ? AfterWin : AfterWipe;
        string line = Fill(lines[random.Next(lines.Length)], leeroy, boss);
        _ = new ECSGameTimer(speaker, _ =>
        {
            AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, line);
            return 0;
        }, 20_000 + random.Next(40_000));
    }
}
