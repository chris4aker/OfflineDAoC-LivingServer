using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Database.Handlers;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests;

[TestFixture]
public class UT_AutonomousBotRealmPointRewards
{
    private sealed class RewardServer : GameServer
    {
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

    public class EmptyReadDatabase : DispatchProxy
    {
        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type result = method.ReturnType;
            if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
            return null;
        }
    }

    private GameServer _previousServer;

    [SetUp]
    public void SetUp()
    {
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((RewardServer)RuntimeHelpers.GetUninitializedObject(typeof(RewardServer)));
    }

    [TearDown]
    public void TearDown() => GameServer.LoadTestDouble(_previousServer);

    private sealed class RewardBot : GameBot
    {
        private RewardBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static RewardBot Bot(bool autonomous, bool temporary)
    {
        RewardBot bot = (RewardBot)RuntimeHelpers.GetUninitializedObject(typeof(RewardBot));
        SetField(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
        SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporary);
        SetField(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
        return bot;
    }

    [Test]
    public void OnlyPersistentAutonomousBotIsRealmPointVictim()
    {
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(true, false)), Is.True);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(Bot(false, true)), Is.False,
            "/spawn companions must never be worth realm points");
        GameNPC ordinaryNpc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), ordinaryNpc, "m_brains", new ArrayList());
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(ordinaryNpc), Is.False,
            "ordinary NPCs and pets remain on the zero-RP NPC path");
    }

    [TestCase(25, 0, 25)]
    [TestCase(35, 0, 225)]
    [TestCase(35, 20, 245)]
    [TestCase(50, 0, 900)]
    [TestCase(50, 90, 990)]
    public void VictimValueMatchesPre181PlayerFormula(int level, int realmLevel, int expected)
    {
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentRealmPointValue(
            (byte)level, realmLevel), Is.EqualTo(expected));
    }

    [Test]
    public void RewardMatchesPlayerKillCapDamageRankAndGroupFormula()
    {
        int actual = AutonomousBotRealmPointRewards.CalculateRealmPointReward(
            victimRealmPointValue: 990,
            victimRealmLevel: 90,
            awarderRealmPointValue: 900,
            awarderRealmLevel: 0,
            participantCount: 2,
            groupContributorCount: 2,
            damagePercent: 0.75,
            applyRealmRankAdjustment: true);

        int baseShare = 990 / 2;
        int damageShare = (int)(baseShare * 0.75);
        int rankAdjusted = (int)(damageShare * (1.0 + 2.0 * 90 / 900.0));
        int expected = rankAdjusted + (int)(rankAdjusted * 0.125);
        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void CompanionAndControlledPetResolveToPlayerOwnerWithoutBecomingVictims()
    {
        GamePlayer owner = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
        RewardBot companion = Bot(false, true);
        typeof(GameBot).GetProperty(nameof(GameBot.Owner))!.SetValue(companion, owner);
        GameNPC pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        SetField(typeof(GameNPC), pet, "m_brains", new ArrayList());
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(companion) { Body = pet });

        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(companion), Is.SameAs(owner));
        Assert.That(AutonomousBotRealmPointRewards.ResolveRootRewardOwner(pet), Is.SameAs(owner));
        Assert.That(AutonomousRvrDefense.IsCombatant(owner), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(companion), Is.True);
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True);
        SetField(typeof(GameNPC), pet, "m_ownBrain", new ControlledMobBrain(owner) { Body = pet });
        Assert.That(AutonomousRvrDefense.IsCombatant(pet), Is.True, "Direct human-owned pets are not IGamePlayer but must be defended against");
        Assert.That(AutonomousRvrDefense.IsCombatant(null), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(companion), Is.False);
        Assert.That(AutonomousBotRealmPointRewards.IsEligibleVictim(pet), Is.False);
    }

    // Issue #396 "No xp for kills in Thid": a gamebot kill paid realm points only. It now pays what a player
    // kill pays (AbstractServerRules.AwardPlayerOnPlayerKill).
    [Test]
    public void GamebotIsWorthAPlayersExperienceAndBountyPoints()
    {
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentExperienceValue(1000), Is.EqualTo(4000),
            "GamePlayer.ExperienceValue is four times the monster value of its level");
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentExperienceValue(-5), Is.EqualTo(0));
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentBountyPointValue(24), Is.EqualTo(15));
        Assert.That(AutonomousBotRealmPointRewards.GetPlayerEquivalentBountyPointValue(50), Is.EqualTo(31));
    }

    [Test]
    public void SoloKillOfAnEvenLevelGamebotPaysExperience()
    {
        // Thidranki: a level 24 player alone kills a level 24 gamebot. Both are worth the same, the cap is 125%.
        long value = AutonomousBotRealmPointRewards.GetPlayerEquivalentExperienceValue(10_000);
        long share = AutonomousBotRealmPointRewards.CalculateCappedExperienceShare(value, value, 1, 125);
        Assert.That(share, Is.EqualTo(40_000));
        Assert.That(share, Is.GreaterThan(0), "a hundred kills in Thidranki must not pay nothing");
    }

    [Test]
    public void ExperienceShareIsSplitAndCappedLikeAPlayerKill()
    {
        Assert.That(AutonomousBotRealmPointRewards.CalculateCappedExperienceShare(40_000, 40_000, 2, 125),
            Is.EqualTo(20_000), "two contributors split the victim's value");
        Assert.That(AutonomousBotRealmPointRewards.CalculateCappedExperienceShare(400_000, 40_000, 1, 125),
            Is.EqualTo(50_000), "capped at XP_PVP_Cap_Percent of the killer's own value");
        Assert.That(AutonomousBotRealmPointRewards.CalculateCappedExperienceShare(0, 40_000, 1, 125), Is.EqualTo(0));
        Assert.That(AutonomousBotRealmPointRewards.CalculateCappedExperienceShare(40_000, 0, 1, 125), Is.EqualTo(0));
        Assert.That(AutonomousBotRealmPointRewards.CalculateCappedExperienceShare(40_000, 40_000, 0, 125), Is.EqualTo(0));
    }

    [Test]
    public void BountyPointsFollowThePlayerKillRuleAndTheBattlegroundSwitch()
    {
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(31, 31, 1, 1.0, false, false),
            Is.EqualTo(31));
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(31, 31, 2, 0.5, false, false),
            Is.EqualTo(7), "split between two, then half the damage");
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(31, 4, 1, 1.0, false, false),
            Is.EqualTo(8), "capped at twice the killer's own value");
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(15, 15, 1, 1.0, true, false),
            Is.EqualTo(0), "allow_bps_in_bgs is off: no bounty points in a battleground, as for a player kill");
        Assert.That(AutonomousBotRealmPointRewards.CalculateBountyPointReward(15, 15, 1, 1.0, true, true),
            Is.EqualTo(15));
    }
}
