using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.AutonomousBotRealmPointRewards;

namespace DOL.UnitTests;

/// <summary>
/// #571 step 2 (harvest F11; idea from True165 74334692 and de83551a): a player's group with persistent gamebots in it
/// (hired companions, befriended gamebots) shares a kill as one group. Before, a player who healed while his hired
/// companion landed every blow had a group share of 0% and earned nothing, and the companion took the whole value alone.
/// The player-kill formula (CalculateRealmPointReward, AwardPlayerOnPlayerKill) is unchanged. Tests only.
/// </summary>
[TestFixture]
public class UT_MixedGroupShares571
{
    private sealed class ShareBot : GameBot
    {
        private ShareBot() : base((OfflineWorldBotRecord)null) { }
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; }
        public override int X => 0;
        public override int Y => 0;
        public override int Z => 0;
    }

    private sealed class SharePlayer : GamePlayer
    {
        private SharePlayer() : base(null, null) { }
        public override eRealm Realm { get; set; }
        public override ushort CurrentRegionID { get; set; }
        public override int X => 0;
        public override int Y => 0;
        public override int Z => 0;
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static ShareBot Bot(eRealm realm, bool autonomous = true, bool temporary = false, ushort region = 1)
    {
        var bot = (ShareBot)RuntimeHelpers.GetUninitializedObject(typeof(ShareBot));
        SetField(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
        SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporary);
        SetField(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
        bot.Realm = realm;
        bot.CurrentRegionID = region;
        bot.ObjectState = GameObject.eObjectState.Active;
        return bot;
    }

    private static SharePlayer Player(eRealm realm)
    {
        var player = (SharePlayer)RuntimeHelpers.GetUninitializedObject(typeof(SharePlayer));
        player.Realm = realm;
        player.CurrentRegionID = 1;
        player.ObjectState = GameObject.eObjectState.Active;
        return player;
    }

    private static Group GroupOf(params GameLiving[] members)
    {
        var group = new Group(members[0]);
        SetField(typeof(Group), group, "_groupMembers", new List<GameLiving>(members));
        foreach (GameLiving member in members)
            member.Group = group;
        return group;
    }

    [Test]
    public void AHealerAndHisHiredCompanionAreOneGroupForTheKill()
    {
        SharePlayer healer = Player(eRealm.Albion);
        ShareBot hire = Bot(eRealm.Albion);
        Group group = GroupOf(healer, hire);
        ShareBot victim = Bot(eRealm.Midgard);
        // the companion lands every blow; the healer is enrolled with 0 (GameLiving.TakeDamage)
        var contributors = new Dictionary<GameLiving, double> { [hire] = 100, [healer] = 0 };
        Assert.Multiple(() =>
        {
            Assert.That(R.IsMixedGroup(group), Is.True);
            Assert.That(R.CountsWithPlayerGroup(hire, victim), Is.True);
            Assert.That(R.CountsWithPlayerGroup(healer, victim), Is.False, "players count on their own side");
            Assert.That(R.MixedGroupShare(group, contributors, victim), Is.EqualTo((2, 100.0)));
        });
    }

    [Test]
    public void AHealerOnlyPlayerGetsANonZeroRealmPointShare()
    {
        // The issue's acceptance. A level 50 victim (900) for a level 50 player (900), no realm ranks.
        int before = R.CalculateRealmPointReward(900, 0, 900, 0, 1, 1, 0 / 100.0, true);
        int after = R.CalculateRealmPointReward(900, 0, 900, 0, 2, 2, 100 / 100.0, true);
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(0), "before: the group's damage counted players only (0%)");
            Assert.That(after, Is.EqualTo(506), "after: 900 / 2 = 450, plus the 12.5% group bonus, as a two-player group");
            Assert.That(R.CalculateRealmPointReward(900, 0, 900, 0, 1, 1, 1.0, true), Is.EqualTo(900),
                "before, the companion took the whole value alone");
        });
    }

    [Test]
    public void OnlyPersistentGamebotsOfAPlayersGroupCount()
    {
        SharePlayer player = Player(eRealm.Albion);
        ShareBot helper = Bot(eRealm.Albion, autonomous: false, temporary: true);
        ShareBot hire = Bot(eRealm.Albion);
        ShareBot away = Bot(eRealm.Albion, region: 2);
        GroupOf(player, helper, hire, away);
        ShareBot loneBotA = Bot(eRealm.Albion), loneBotB = Bot(eRealm.Albion);
        Group botsOnly = GroupOf(loneBotA, loneBotB);
        ShareBot victim = Bot(eRealm.Midgard);
        Assert.Multiple(() =>
        {
            Assert.That(R.CountsWithPlayerGroup(helper, victim), Is.False, "a /spawn helper's damage is already its owner's");
            Assert.That(R.CountsWithPlayerGroup(away, victim), Is.False, "out of reward range, as a player would be");
            Assert.That(R.CountsWithPlayerGroup(loneBotA, victim), Is.False, "a group of gamebots only is unchanged");
            Assert.That(R.IsMixedGroup(botsOnly), Is.False);
            Assert.That(R.CountsWithPlayerGroup(hire, Bot(eRealm.Albion)), Is.False, "never for a victim of its own realm");
        });
    }

    [Test]
    public void APlayerOnlyGroupIsUnchanged()
    {
        SharePlayer a = Player(eRealm.Albion), b = Player(eRealm.Albion);
        Group group = GroupOf(a, b);
        Assert.Multiple(() =>
        {
            Assert.That(R.IsMixedGroup(group), Is.False);
            Assert.That(R.KeepBattlegroundShare(0, false, true, 0.5, 1), Is.EqualTo(0), "the player rule's own rounding stays");
        });
    }

    [Test]
    public void ABattlegroundShareOfOneRealmPointDoesNotRoundToZero()
    {
        // A level 21 victim is worth 1 RP; split over a player and his companion it is 0 each.
        int split = R.CalculateRealmPointReward(1, 0, 16, 0, 2, 2, 1.0, true);
        Assert.Multiple(() =>
        {
            Assert.That(R.GetPlayerEquivalentRealmPointValue(21, 0), Is.EqualTo(1));
            Assert.That(split, Is.EqualTo(0));
            Assert.That(R.KeepBattlegroundShare(split, true, true, 1.0, 1), Is.EqualTo(1), "the issue's acceptance");
            Assert.That(R.KeepBattlegroundShare(split, true, false, 1.0, 1), Is.EqualTo(0), "battlegrounds only");
            Assert.That(R.KeepBattlegroundShare(split, true, true, 0, 1), Is.EqualTo(0), "no share, no points");
            Assert.That(R.KeepBattlegroundShare(split, true, true, 1.0, 0), Is.EqualTo(0), "a worthless victim stays worthless");
            Assert.That(R.KeepBattlegroundShare(7, true, true, 1.0, 50), Is.EqualTo(7), "a real share is untouched");
        });
    }

    [Test]
    public void BothKillPathsUseTheGroupShare()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "serverrules", "AbstractServerRules.cs")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root");
        string game = Path.Combine(dir, "source", "server", "GameServer");
        string rules = File.ReadAllText(Path.Combine(game, "serverrules", "AbstractServerRules.cs")).Replace("\r\n", "\n");
        string rewards = File.ReadAllText(Path.Combine(game, "bots", "autonomous", "AutonomousBotRealmPointRewards.cs")).Replace("\r\n", "\n");
        Assert.Multiple(() =>
        {
            Assert.That(rules, Does.Contain("if (AutonomousBotRealmPointRewards.CountsWithPlayerGroup(pair.Key, killedPlayer))"),
                "a player victim: the group's count and damage take its gamebots");
            Assert.That(rules, Does.Contain("realmPoints = AutonomousBotRealmPointRewards.KeepBattlegroundShare(realmPoints,"));
            Assert.That(rewards, Does.Contain("if (CountsWithPlayerGroup(pair.Key, killedBot))"), "a gamebot victim: the same");
            Assert.That(rewards, Does.Contain("(groupCount, groupDamage) = MixedGroupShare(bot.Group, hostileContributors, victim);"),
                "the gamebot side shares with the players");
            Assert.That(rewards, Does.Contain("points = KeepBattlegroundShare(points, mixed, battleground, share, victimRealmPointValue);"));
        });
    }
}
