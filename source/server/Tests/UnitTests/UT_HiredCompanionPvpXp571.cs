using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// #571 (True165 de83551a; owner 2026-11-27: "yes, as True165 de83551a does; under the owner's 'only add' rule the
/// player's own share is never lowered by it"): hired companions earn experience from PvP kills, a hire below the
/// battleground bracket included. Hired companions only; the player is never paid or charged by this path.
/// </summary>
[TestFixture]
public class UT_HiredCompanionPvpXp571
{
    private const int Cap = 125;
    private const long VictimXp = 40_000;

    private sealed class XpBot : GameBot
    {
        private XpBot() : base((OfflineWorldBotRecord)null) { }
        public long XpAwarded { get; private set; }
        public override void GainExperience(GainedExperienceEventArgs arguments, bool notify = true) => XpAwarded += arguments.ExpTotal;
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
    }

    private sealed class XpPlayer : GamePlayer
    {
        private XpPlayer() : base(null, null) { }
        public long XpAwarded { get; private set; }
        public long RpAwarded { get; private set; }
        public long BpAwarded { get; private set; }
        public override void GainExperience(GainedExperienceEventArgs arguments, bool notify = true) => XpAwarded += arguments.ExpTotal;
        public override void GainRealmPoints(long amount, bool modify, bool sendMessage, bool notify) => RpAwarded += amount;
        public override void GainBountyPoints(long amount, bool modify, bool sendMessage, bool notify) => BpAwarded += amount;
        public override byte Level { get; set; }
        public override eRealm Realm { get; set; }
    }

    private static void SetField(Type type, object target, string name, object value) =>
        type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(target, value);

    private static XpBot Bot(byte level, bool autonomous = true, bool temporary = false)
    {
        XpBot bot = (XpBot)RuntimeHelpers.GetUninitializedObject(typeof(XpBot));
        SetField(typeof(GameLiving), bot, "<TempProperties>k__BackingField", new PropertyCollection());
        SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
        typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporary);
        SetField(typeof(GameNPC), bot, "m_ownBrain", new BotBrain { Body = bot });
        bot.Level = level;
        bot.Realm = eRealm.Albion;
        return bot;
    }

    private static XpPlayer Player(byte level)
    {
        XpPlayer player = (XpPlayer)RuntimeHelpers.GetUninitializedObject(typeof(XpPlayer));
        player.Level = level;
        player.Realm = eRealm.Albion;
        return player;
    }

    // A level's PvP value in this test: simple and monotonic, so the cap is easy to read.
    private static long ValueOf(byte level) => level * 1_000L;

    private sealed class World
    {
        public readonly Dictionary<Group, List<GameLiving>> Rosters = new();
        public readonly HashSet<GameLiving> OutOfReach = new();
        public readonly HashSet<GameBot> Hired = new();

        public Group GroupOf(GamePlayer leader, params GameLiving[] members)
        {
            Group group = new(leader);
            var roster = new List<GameLiving> { leader };
            roster.AddRange(members);
            foreach (GameLiving member in roster)
                member.Group = group;
            Rosters[group] = roster;
            return group;
        }

        public Dictionary<GameBot, long> Pay(GameLiving victim, Dictionary<GameLiving, double> damage, long victimXp = VictimXp) =>
            AutonomousBotRealmPointRewards.PayHiredCompanionExperience(victim, victimXp, damage, damage.Values.Sum(),
                bot => Hired.Contains(bot), member => !OutOfReach.Contains(member), group => Rosters[group], ValueOf, Cap);
    }

    private static long Expected(long victimXp, int members, byte hireLevel, double share) =>
        (long)(Math.Min(victimXp / members, ValueOf(hireLevel) * Cap / 100) * share);

    private static XpBot Victim()
    {
        XpBot victim = Bot(30);
        victim.Realm = eRealm.Midgard;
        return victim;
    }

    [Test]
    public void HireLearnsWhenThePlayerDidAllTheDamage()
    {
        var world = new World();
        XpPlayer player = Player(30);
        XpBot hire = Bot(30);
        world.Hired.Add(hire);
        world.GroupOf(player, hire);

        var paid = world.Pay(Victim(), new() { [player] = 100, [hire] = 0 });

        Assert.That(hire.XpAwarded, Is.EqualTo(Expected(VictimXp, 2, 30, 1.0)).And.GreaterThan(0));
        Assert.That(paid[hire], Is.EqualTo(hire.XpAwarded));
    }

    [Test]
    public void HireLearnsWhenItDidAllTheDamage()
    {
        var world = new World();
        XpPlayer player = Player(30);
        XpBot hire = Bot(30);
        world.Hired.Add(hire);
        world.GroupOf(player, hire);

        world.Pay(Victim(), new() { [player] = 0, [hire] = 100 });

        Assert.That(hire.XpAwarded, Is.EqualTo(Expected(VictimXp, 2, 30, 1.0)));
    }

    [Test]
    public void HireUnderTheBracketLearnsUpToItsOwnCap()
    {
        var world = new World();
        XpPlayer player = Player(24);
        XpBot hire = Bot(15); // below a 20-24 battleground's bracket
        world.Hired.Add(hire);
        world.GroupOf(player, hire);

        world.Pay(Victim(), new() { [player] = 50, [hire] = 50 });

        long cap = ValueOf(15) * Cap / 100;
        Assert.That(cap, Is.LessThan(VictimXp / 2), "the hire's own level caps it, not the bracket");
        Assert.That(hire.XpAwarded, Is.EqualTo(cap));
    }

    [Test]
    public void ThePlayerIsNeverPaidOrChargedByThisPath()
    {
        var world = new World();
        XpPlayer player = Player(30);
        XpBot hire = Bot(30);
        world.Hired.Add(hire);
        world.GroupOf(player, hire);

        var paid = world.Pay(Victim(), new() { [player] = 70, [hire] = 30 });

        Assert.That(player.XpAwarded, Is.Zero);
        Assert.That(player.RpAwarded, Is.Zero);
        Assert.That(player.BpAwarded, Is.Zero);
        Assert.That(hire.AutonomousRealmPoints, Is.Zero, "the hire's realm points stay on PayGamebots");
        Assert.That(paid.Keys, Is.EquivalentTo(new GameBot[] { hire }));
    }

    [Test]
    public void OutsideDamageLowersOnlyTheHiresShare()
    {
        var world = new World();
        XpPlayer player = Player(30);
        XpBot hire = Bot(30);
        world.Hired.Add(hire);
        world.GroupOf(player, hire);
        XpPlayer stranger = Player(30); // another group's player

        world.Pay(Victim(), new() { [player] = 30, [hire] = 20, [stranger] = 50 });

        Assert.That(hire.XpAwarded, Is.EqualTo(Expected(VictimXp, 2, 30, 0.5)));
    }

    [Test]
    public void OnlyHiredCompanionsLearn()
    {
        var world = new World();
        XpPlayer player = Player(30);
        XpBot hire = Bot(30);
        XpBot befriended = Bot(30); // a gamebot in the player's group, not hired
        XpBot helper = Bot(30, autonomous: false, temporary: true); // a /spawn helper
        world.Hired.Add(hire);
        world.Hired.Add(helper); // even if a predicate said so, a temporary helper is never paid
        world.GroupOf(player, hire, befriended, helper);

        world.Pay(Victim(), new() { [player] = 25, [hire] = 25, [befriended] = 50 });

        Assert.That(befriended.XpAwarded, Is.Zero);
        Assert.That(helper.XpAwarded, Is.Zero);
        // The befriended gamebot is not one of the hire's sharers: 2 members (player + hire), 50% of the damage.
        Assert.That(hire.XpAwarded, Is.EqualTo(Expected(VictimXp, 2, 30, 0.5)));
    }

    [Test]
    public void TwoHiresSplitTheBaseWithThePlayer()
    {
        var world = new World();
        XpPlayer player = Player(30);
        XpBot first = Bot(30), second = Bot(30);
        world.Hired.Add(first);
        world.Hired.Add(second);
        world.GroupOf(player, first, second);

        world.Pay(Victim(), new() { [player] = 40, [first] = 60 });

        Assert.That(first.XpAwarded, Is.EqualTo(Expected(VictimXp, 3, 30, 1.0)));
        Assert.That(second.XpAwarded, Is.EqualTo(Expected(VictimXp, 3, 30, 1.0)),
            "a hire in the group that dealt nothing learns too (roster, not only XPGainers)");
    }

    [Test]
    public void AHireOutOfReachLearnsNothingAndIsNotCounted()
    {
        var world = new World();
        XpPlayer player = Player(30);
        XpBot near = Bot(30), far = Bot(30);
        world.Hired.Add(near);
        world.Hired.Add(far);
        world.GroupOf(player, near, far);
        world.OutOfReach.Add(far);

        world.Pay(Victim(), new() { [player] = 50, [near] = 50 });

        Assert.That(far.XpAwarded, Is.Zero);
        Assert.That(near.XpAwarded, Is.EqualTo(Expected(VictimXp, 2, 30, 1.0)));
    }

    [Test]
    public void ASoloHireIsCountedAlone()
    {
        var world = new World();
        XpBot hire = Bot(30);
        world.Hired.Add(hire);

        world.Pay(Victim(), new() { [hire] = 60, [Player(30)] = 40 });

        Assert.That(hire.XpAwarded, Is.EqualTo(Expected(VictimXp, 1, 30, 0.6)));
    }

    [Test]
    public void NothingWithoutExperienceValue()
    {
        var world = new World();
        XpPlayer player = Player(30);
        XpBot hire = Bot(30);
        world.Hired.Add(hire);
        world.GroupOf(player, hire);

        var paid = world.Pay(Victim(), new() { [player] = 50, [hire] = 50 }, victimXp: 0);

        Assert.That(paid, Is.Empty);
        Assert.That(hire.XpAwarded, Is.Zero);
    }

    private static string Source(params string[] parts)
    {
        DirectoryInfo dir = new(TestContext.CurrentContext.TestDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Dawn of Light.sln")))
            dir = dir.Parent;
        Assert.That(dir, Is.Not.Null, "source tree not found");
        return File.ReadAllText(Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray()));
    }

    [Test]
    public void BothKillPathsPayHiresOnlyInsideTheRepeatKillWindow()
    {
        string source = Source("GameServer", "bots", "autonomous", "AutonomousBotRealmPointRewards.cs");
        int award = source.IndexOf("public static void Award(", StringComparison.Ordinal);
        int worth = source.IndexOf("if (isWorthRealmPoints)", award, StringComparison.Ordinal);
        int pay = source.IndexOf("PayHiredCompanionExperience(killedBot,", award, StringComparison.Ordinal);
        Assert.That(worth, Is.GreaterThan(award));
        Assert.That(pay, Is.GreaterThan(worth).And.LessThan(source.IndexOf("Dictionary<GamePlayer, EntityCountTotalDamagePair>", award, StringComparison.Ordinal)),
            "a gamebot victim pays its killers' hires inside the repeat-kill window");

        int forPlayer = source.IndexOf("public static void PayGamebotsForPlayerKill(", StringComparison.Ordinal);
        int window = source.IndexOf("WorthRealmPoints(", forPlayer, StringComparison.Ordinal);
        int payPlayerKill = source.IndexOf("PayHiredCompanionExperience(killedPlayer, killedPlayer.ExperienceValue", forPlayer, StringComparison.Ordinal);
        Assert.That(window, Is.GreaterThan(forPlayer));
        Assert.That(payPlayerKill, Is.GreaterThan(window), "a player victim pays hires only past the repeat-kill window");
    }
}
