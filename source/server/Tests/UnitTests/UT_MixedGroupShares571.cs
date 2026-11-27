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
/// #571 step 2 (harvest F11; idea from True165 74334692 and de83551a). Owner, 2026-11-27: "only add. A hired companion or
/// befriended gamebot in your group may raise your share of XP, bounty points, coins and RP, but never lower it below what
/// you'd get without it." The share with the group's gamebots counted is used only when it raises the player's rewards.
/// Tests only.
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

    /// <summary>A level 50 victim for a level 50 player, no realm ranks: 900 RP, 1,000 XP (cap 125%), 31 BP, 5,000 coins.</summary>
    private static readonly R.PvpKillValues Level50 = new(900, 0, 900, 0, true, 1_000, 1_000, 125, 31, 31, false, 5_000, 5_000);

    private static long[] Rewards(int count, double share, Func<long, long> bonus = null, bool floor = false) =>
        R.PvpKillRewards(Level50, count, true, share, bonus, floor);

    [Test]
    public void APlayerWhoDidAllTheDamageBesideAnIdleCompanionKeepsHisWholeReward()
    {
        long[] alone = Rewards(1, 1.0), split = Rewards(2, 1.0);
        Assert.Multiple(() =>
        {
            Assert.That(alone, Is.EqualTo(new long[] { 900, 31, 1_000, 5_000 }), "RP, BP, XP, coins: the worked case");
            Assert.That(split, Is.EqualTo(new long[] { 506, 15, 500, 2_500 }), "what a parity split would have paid");
            Assert.That(R.UseMixedShare(alone, split), Is.False, "only add: he keeps 900 RP, full XP, BP and coins");
        });
    }

    [Test]
    public void APlayerWhoOutDamagesHisCompanionKeepsHisReward()
    {
        long[] own = Rewards(1, 0.7);
        Assert.Multiple(() =>
        {
            Assert.That(own, Is.EqualTo(new long[] { 630, 21, 700, 3_500 }));
            Assert.That(R.UseMixedShare(own, Rewards(2, 1.0)), Is.False);
            Assert.That(R.UseMixedShare(Rewards(1, 1.0, b => b / 10), Rewards(2, 1.0, b => b / 10)), Is.False,
                "with a keep's experience bonus too");
        });
    }

    [Test]
    public void AHealerWhoDealtNothingGetsTheGroupShare()
    {
        long[] before = Rewards(1, 0), after = Rewards(2, 1.0);
        Assert.Multiple(() =>
        {
            Assert.That(before, Is.EqualTo(new long[] { 0, 0, 0, 0 }), "before: 0% of the damage, nothing");
            Assert.That(R.UseMixedShare(before, after), Is.True);
            Assert.That(after[0], Is.EqualTo(506), "900 / 2 + 12.5%: a two-player group's share");
            Assert.That(R.UseMixedShare(Rewards(1, 0, b => b / 10), Rewards(2, 1.0, b => b / 10)), Is.True,
                "with a keep's experience bonus too");
        });
    }

    [Test]
    public void ACompanionThatDidMostOfTheDamageRaisesTheShare()
    {
        Assert.That(R.UseMixedShare(Rewards(1, 0.1), Rewards(2, 1.0)), Is.True, "10% alone, against the group's 100% over two");
    }

    [Test]
    public void SevenIdleBotsDoNotReduceThePlayerAtAll()
    {
        Assert.Multiple(() =>
        {
            Assert.That(R.UseMixedShare(Rewards(1, 1.0), Rewards(8, 1.0)), Is.False);
            Assert.That(R.UseMixedShare(Rewards(1, 0.5), Rewards(8, 0.5)), Is.False, "they did no damage: no gain, so no change");
        });
    }

    [Test]
    public void AnEqualShareIsNotAChange()
    {
        Assert.That(R.UseMixedShare(new long[] { 5, 5, 5, 5 }, new long[] { 5, 5, 5, 5 }), Is.False);
        Assert.That(R.UseMixedShare(new long[] { 5, 5, 5, 5 }, new long[] { 6, 5, 5, 4 }), Is.False, "one reward lower: never");
    }

    [Test]
    public void ABattlegroundShareOfOneRealmPointDoesNotRoundToZeroOnlyWhereItAdds()
    {
        // a level 21 victim (1 RP, 100 XP) for a level 24 player (16 RP value)
        var bg = new R.PvpKillValues(1, 0, 16, 0, true, 100, 100, 125, 0, 15, true, 0, 0);
        long[] healerBefore = R.PvpKillRewards(bg, 1, true, 0, null);
        long[] healerAfter = R.PvpKillRewards(bg, 2, true, 1.0, null, battlegroundFloor: true);
        long[] alone = R.PvpKillRewards(bg, 1, true, 1.0, null);
        long[] split = R.PvpKillRewards(bg, 2, true, 1.0, null, battlegroundFloor: true);
        Assert.Multiple(() =>
        {
            Assert.That(R.GetPlayerEquivalentRealmPointValue(21, 0), Is.EqualTo(1));
            Assert.That(R.PvpKillRewards(bg, 2, true, 1.0, null)[0], Is.EqualTo(0), "1 / 2 rounds to 0");
            Assert.That(healerAfter[0], Is.EqualTo(1), "the floor keeps 1 RP");
            Assert.That(R.UseMixedShare(healerBefore, healerAfter), Is.True, "the healer gains");
            Assert.That(alone[0], Is.EqualTo(1));
            Assert.That(R.UseMixedShare(alone, split), Is.False, "a player who did it all keeps his own share");
            Assert.That(R.KeepBattlegroundShare(0, true, false, 1.0, 1), Is.EqualTo(0), "battlegrounds only");
            Assert.That(R.KeepBattlegroundShare(0, true, true, 0, 1), Is.EqualTo(0), "no share, no points");
            Assert.That(R.KeepBattlegroundShare(7, true, true, 1.0, 50), Is.EqualTo(7), "a real share is untouched");
        });
    }

    [Test]
    public void AHealerAndHisHiredCompanionAreOneMixedGroup()
    {
        SharePlayer healer = Player(eRealm.Albion);
        ShareBot hire = Bot(eRealm.Albion);
        Group group = GroupOf(healer, hire);
        ShareBot victim = Bot(eRealm.Midgard);
        var contributors = new Dictionary<GameLiving, double> { [hire] = 100, [healer] = 0 };
        var mixed = R.MixedGroupContributions(contributors, victim);
        Assert.Multiple(() =>
        {
            Assert.That(R.IsMixedGroup(group), Is.True);
            Assert.That(mixed.Keys, Is.EquivalentTo(new[] { group }));
            Assert.That((mixed[group].Count, mixed[group].Damage), Is.EqualTo((2, 100.0)));
        });
    }

    [Test]
    public void OnlyPersistentGamebotsOfAPlayersGroupCount()
    {
        SharePlayer player = Player(eRealm.Albion);
        ShareBot helper = Bot(eRealm.Albion, autonomous: false, temporary: true);
        ShareBot hire = Bot(eRealm.Albion);
        ShareBot away = Bot(eRealm.Albion, region: 2);
        Group group = GroupOf(player, helper, hire, away);
        ShareBot victim = Bot(eRealm.Midgard);
        var contributors = new Dictionary<GameLiving, double> { [player] = 10, [helper] = 20, [hire] = 30, [away] = 40 };
        Assert.Multiple(() =>
        {
            Assert.That(R.CountsWithPlayerGroup(helper, victim), Is.False, "a /spawn helper's damage is already its owner's");
            Assert.That(R.CountsWithPlayerGroup(away, victim), Is.False, "out of reward range, as a player would be");
            Assert.That(R.CountsWithPlayerGroup(hire, Bot(eRealm.Albion)), Is.False, "never for a victim of its own realm");
            Assert.That((R.MixedGroupContributions(contributors, victim)[group].Count,
                R.MixedGroupContributions(contributors, victim)[group].Damage), Is.EqualTo((2, 40.0)), "the player and the hire");
        });
    }

    [Test]
    public void GroupsOfPlayersOnlyOrOfGamebotsOnlyAreUnchanged()
    {
        SharePlayer a = Player(eRealm.Albion), b = Player(eRealm.Albion);
        Group players = GroupOf(a, b);
        ShareBot c = Bot(eRealm.Albion), d = Bot(eRealm.Albion);
        Group bots = GroupOf(c, d);
        ShareBot victim = Bot(eRealm.Midgard);
        var contributors = new Dictionary<GameLiving, double> { [a] = 50, [b] = 0, [c] = 25, [d] = 25 };
        Assert.Multiple(() =>
        {
            Assert.That(R.IsMixedGroup(players), Is.False);
            Assert.That(R.IsMixedGroup(bots), Is.False);
            Assert.That(R.MixedGroupContributions(contributors, victim), Is.Empty, "no mixed share: the old rule alone");
        });
    }

    [Test]
    public void BothKillPathsChooseTheShareOnlyWhenItAdds()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "serverrules", "AbstractServerRules.cs")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root");
        string game = Path.Combine(dir, "source", "server", "GameServer");
        string rules = File.ReadAllText(Path.Combine(game, "serverrules", "AbstractServerRules.cs")).Replace("\r\n", "\n");
        string rewards = File.ReadAllText(Path.Combine(game, "bots", "autonomous", "AutonomousBotRealmPointRewards.cs")).Replace("\r\n", "\n");
        int pay = rewards.IndexOf("public static void PayGamebots(", StringComparison.Ordinal);
        string payGamebots = rewards.Substring(pay, rewards.IndexOf("public static void PayGamebotsForPlayerKill", pay, StringComparison.Ordinal) - pay);
        Assert.Multiple(() =>
        {
            Assert.That(rules, Does.Contain("bool mixedShare = isWorthAnything && UseMixedShare();"), "a player victim");
            Assert.That(rules, Does.Contain("if (!AutonomousBotRealmPointRewards.UseMixedShare("));
            Assert.That(rewards, Does.Contain("if (UseMixedShare(PvpKillRewards(values, contributorCount, true, damagePercent, Bonus),"),
                "a gamebot victim");
            Assert.That(payGamebots, Does.Not.Contain("Mixed"), "the gamebots' own side is unchanged");
        });
    }
}
