using System;
using System.Collections;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// #766 part 2 (owner decision 64A): rc.2's 25.4 h run logged 88,287 sight back-offs from 2,314 bots, mostly in the
/// battlegrounds; the busiest bot backed off 425 times, about one every 3.6 min. Its route walked it back to the same
/// enemies after each wait. A back-off call on the same contact within the cooldown is now quiet: no new walk, friends
/// call or back-off row, and the bot keeps its distance where it stands. More enemies, another place or the cooldown run
/// out is new sight and backs off as before. Tests only.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_SightRepeat766
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Vector2 Contact = new(440000, 300000);
    private const long FirstCall = 1_000_000;

    private static readonly IObjectDatabase EmptyDatabase =
        DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    private GameServer _previousServer;

    private sealed class InertServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
    }

    [OneTimeSetUp]
    public void Setup()
    {
        DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);
        _previousServer = GameServer.Instance;
        GameServer.LoadTestDouble((InertServer)RuntimeHelpers.GetUninitializedObject(typeof(InertServer)));
    }

    [OneTimeTearDown]
    public void Restore() => GameServer.LoadTestDouble(_previousServer);

    // ---- the pure rule ----

    [Test]
    public void TheSameContactWithinTheCooldownIsARepeat()
    {
        long now = FirstCall + 210_000; // the busiest bot's 3.6-minute cycle
        Assert.Multiple(() =>
        {
            Assert.That(RvrSightSizeUp.IsRepeat(Contact, 4, FirstCall, now, Contact, 4), Is.True);
            Assert.That(RvrSightSizeUp.IsRepeat(Contact, 4, FirstCall, now, Contact + new Vector2(1_800, 1_200), 3), Is.True,
                "the group moved about a little, and is no bigger");
            Assert.That(RvrSightSizeUp.RepeatCooldownMilliseconds, Is.GreaterThan(216_000L), "longer than the logged cycle");
        });
    }

    [Test]
    public void NewSightIsNeverARepeat()
    {
        long now = FirstCall + 60_000;
        Assert.Multiple(() =>
        {
            Assert.That(RvrSightSizeUp.IsRepeat(null, 0, 0, now, Contact, 4), Is.False, "nothing remembered");
            Assert.That(RvrSightSizeUp.IsRepeat(Contact, 4, FirstCall, now, Contact, 5), Is.False, "more enemies");
            Assert.That(RvrSightSizeUp.IsRepeat(Contact, 4, FirstCall, now, Contact + new Vector2(2_600, 0), 4), Is.False,
                "another place");
            Assert.That(RvrSightSizeUp.IsRepeat(Contact, 4, FirstCall, FirstCall + RvrSightSizeUp.RepeatCooldownMilliseconds,
                Contact, 4), Is.False, "the cooldown has run out");
        });
    }

    // ---- the controller ----

    [Test]
    public void ARepeatHoldsQuietlyWithoutANewWalkOrCall()
    {
        var controller = new AutonomousWorldBotController();
        var bot = MakeBot(430000, 320000);
        Remember(controller, Contact, 3, FirstCall);
        long now = FirstCall + 200_000;
        var enemies = new GameLiving[] { MakeBot(Contact.X, Contact.Y), MakeBot(Contact.X + 200, Contact.Y), MakeBot(Contact.X, Contact.Y + 200) };
        StartBackOff(controller, bot, enemies, now);
        Assert.Multiple(() =>
        {
            Assert.That(Field("_sightHoldUntil").GetValue(controller), Is.EqualTo(now + RvrSightSizeUp.WaitMilliseconds),
                "it still doesn't open on them: it holds");
            Assert.That(Field("_sightBackOffOrder").GetValue(controller), Is.Null, "no new walk away");
            Assert.That(Field("_sightBackOffPoint").GetValue(controller), Is.Null);
            Assert.That(Field("_sightRepeats").GetValue(controller), Is.EqualTo(1));
            Assert.That(Field("_sightHoldQuiet").GetValue(controller), Is.True);
            Assert.That(bot.Stops, Is.EqualTo(1), "it stops where it stands: its route doesn't walk it on into them");
            Assert.That(Field("_sightContactTick").GetValue(controller), Is.EqualTo(now), "the memory slides with the repeat");
            Assert.That(Field("_sightContactCentre").GetValue(controller), Is.EqualTo((Vector2?)Contact), "its place stays the first call's");
            Assert.That(Field("_sightContactEnemies").GetValue(controller), Is.EqualTo(3));
        });
    }

    [Test]
    public void TheMemoryOutlivesTheHoldAndTheResumeRowCountsIt()
    {
        var controller = new AutonomousWorldBotController();
        var bot = MakeBot(430000, 320000);
        Remember(controller, Contact, 3, FirstCall);
        long now = FirstCall + 120_000;
        StartBackOff(controller, bot, [MakeBot(Contact.X, Contact.Y)], now);
        typeof(AutonomousWorldBotController).GetMethod("EndSightHold", Hidden)!.Invoke(controller, [bot, "waited", now + 30_000]);
        Assert.Multiple(() =>
        {
            Assert.That(Field("_sightHoldUntil").GetValue(controller), Is.EqualTo(0L));
            Assert.That(Field("_sightHoldStarted").GetValue(controller), Is.EqualTo(0L));
            Assert.That(Field("_sightHoldQuiet").GetValue(controller), Is.False);
            Assert.That(Field("_sightContactCentre").GetValue(controller), Is.EqualTo((Vector2?)Contact),
                "the next walk back to them is a repeat too");
            Assert.That(Field("_sightRepeats").GetValue(controller), Is.EqualTo(1));
        });
    }

    [Test]
    public void ABiggerGroupWalkingUpDuringAQuietHoldBacksOff()
    {
        var controller = new AutonomousWorldBotController();
        var bot = MakeBot(430000, 320000);
        Remember(controller, Contact, 3, FirstCall);
        Field("_sightFreshTick").SetValue(controller, FirstCall);
        long now = FirstCall + 200_000;
        var three = new GameLiving[] { MakeBot(Contact.X, Contact.Y), MakeBot(Contact.X + 200, Contact.Y), MakeBot(Contact.X, Contact.Y + 200) };
        StartBackOff(controller, bot, three, now);
        Assert.That(Field("_sightHoldQuiet").GetValue(controller), Is.True, "a quiet repeat first");

        // The same three again within the hold: a plain re-arm, still quiet.
        StartBackOff(controller, bot, three, now + 5_000);
        Assert.Multiple(() =>
        {
            Assert.That(Field("_sightHoldQuiet").GetValue(controller), Is.True);
            Assert.That(Field("_sightRepeats").GetValue(controller), Is.EqualTo(1), "a re-arm is not another repeat");
            Assert.That(Field("_sightHoldUntil").GetValue(controller), Is.EqualTo(now + 5_000 + RvrSightSizeUp.WaitMilliseconds));
        });

        // A fourth walks up within the 30 s hold: new sight, so a fresh back-off.
        long later = now + 10_000;
        var four = new GameLiving[] { three[0], three[1], three[2], MakeBot(Contact.X + 200, Contact.Y + 200) };
        StartBackOff(controller, bot, four, later);
        Assert.Multiple(() =>
        {
            Assert.That(Field("_sightHoldQuiet").GetValue(controller), Is.False, "no longer a quiet hold");
            Assert.That(Field("_sightRepeats").GetValue(controller), Is.EqualTo(0));
            Assert.That(Field("_sightContactEnemies").GetValue(controller), Is.EqualTo(4), "the memory moved to the bigger group");
            Assert.That(Field("_sightContactTick").GetValue(controller), Is.EqualTo(later));
            Assert.That(Field("_sightFreshTick").GetValue(controller), Is.EqualTo(later), "the chain restarts from this back-off");
            Assert.That(Field("_sightHoldStarted").GetValue(controller), Is.EqualTo(later));
            Assert.That(Field("_sightHoldUntil").GetValue(controller), Is.EqualTo(later + RvrSightSizeUp.WaitMilliseconds));
        });
    }

    [Test]
    public void AGroupElsewhereDuringAQuietHoldBacksOff()
    {
        var controller = new AutonomousWorldBotController();
        var bot = MakeBot(430000, 320000);
        Remember(controller, Contact, 3, FirstCall);
        long now = FirstCall + 100_000;
        StartBackOff(controller, bot, [MakeBot(Contact.X, Contact.Y)], now);
        Assert.That(Field("_sightHoldQuiet").GetValue(controller), Is.True);
        var elsewhere = new Vector2(Contact.X - 3_000, Contact.Y);
        StartBackOff(controller, bot, [MakeBot(elsewhere.X, elsewhere.Y)], now + 8_000);
        Assert.Multiple(() =>
        {
            Assert.That(Field("_sightHoldQuiet").GetValue(controller), Is.False);
            Assert.That(Field("_sightContactCentre").GetValue(controller), Is.EqualTo((Vector2?)elsewhere));
            Assert.That(Field("_sightContactEnemies").GetValue(controller), Is.EqualTo(1));
        });
    }

    [Test]
    public void SinceFreshCountsFromTheFirstFreshBackOffAcrossRepeats()
    {
        var controller = new AutonomousWorldBotController();
        var bot = MakeBot(430000, 320000);
        Remember(controller, Contact, 3, FirstCall);
        Field("_sightFreshTick").SetValue(controller, FirstCall);
        long now = FirstCall + 120_000;
        StartBackOff(controller, bot, [MakeBot(Contact.X, Contact.Y)], now);
        Assert.Multiple(() =>
        {
            Assert.That(Field("_sightHoldStarted").GetValue(controller), Is.EqualTo(now), "heldSeconds= is this hold's");
            Assert.That(Field("_sightFreshTick").GetValue(controller), Is.EqualTo(FirstCall), "a repeat keeps the fresh back-off's time");
        });
        typeof(AutonomousWorldBotController).GetMethod("EndSightHold", Hidden)!.Invoke(controller, [bot, "waited", now + 30_000]);
        Assert.That(Field("_sightFreshTick").GetValue(controller), Is.EqualTo(FirstCall), "it outlives the hold, for the next repeat");
    }

    [Test]
    public void TheFreshBackOffStillWalksAwayCallsFriendsAndLogs()
    {
        string sight = Source("AutonomousWorldBotController.SightSizeUp.cs");
        int repeat = sight.IndexOf("if (RvrSightSizeUp.IsRepeat(_sightContactCentre, _sightContactEnemies, _sightContactTick, now, centre, enemies.Length))",
            StringComparison.Ordinal);
        int remember = sight.IndexOf("_sightContactCentre = centre;", StringComparison.Ordinal);
        int walk = sight.IndexOf("_sightBackOffPoint = SightBackOffPoint(bot, enemies, nearest);", StringComparison.Ordinal);
        int call = sight.IndexOf("int called = CallSightFriends(bot, enemies, now);", StringComparison.Ordinal);
        int row = sight.IndexOf("RVR_SIGHT_BACK_OFF bot=", StringComparison.Ordinal);
        int fresh = sight.IndexOf("if (!fresh && !(_sightHoldQuiet &&", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(fresh, Is.GreaterThanOrEqualTo(0), "a re-arm while holding is unchanged, unless a quiet hold sees new sight");
            Assert.That(repeat, Is.GreaterThan(fresh), "only a fresh call is checked for a repeat");
            Assert.That(remember, Is.GreaterThan(repeat), "a fresh call that isn't a repeat is remembered");
            Assert.That(walk, Is.GreaterThan(remember), "then walks away");
            Assert.That(call, Is.GreaterThan(walk), "calls its friends");
            Assert.That(row, Is.GreaterThan(call), "and logs its back-off");
            Assert.That(sight, Does.Contain("repeat={(_sightHoldQuiet ? \"true\" : \"false\")} repeats={_sightRepeats} "),
                "the resume row tells a quiet hold from a fresh one");
            Assert.That(sight, Does.Contain("heldSeconds="), "and how long the hold ran");
            Assert.That(sight, Does.Contain("sinceFreshSeconds={(_sightFreshTick == 0 ? 0 : Math.Max(0, now - _sightFreshTick) / 1000)}"),
                "and how long since the first fresh back-off of the chain, for the owner's capped hold");
            Assert.That(sight, Does.Contain("_sightFreshTick = now;"), "set on the fresh path only");
        });
    }

    private static string Source(string file)
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root");
        return File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous", file)).Replace("\r\n", "\n");
    }

    private static void Remember(AutonomousWorldBotController controller, Vector2 centre, int enemies, long tick)
    {
        Field("_sightContactCentre").SetValue(controller, (Vector2?)centre);
        Field("_sightContactEnemies").SetValue(controller, enemies);
        Field("_sightContactTick").SetValue(controller, tick);
    }

    private static void StartBackOff(AutonomousWorldBotController controller, GameBot bot, GameLiving[] enemies, long now) =>
        typeof(AutonomousWorldBotController).GetMethod("StartSightBackOff", Hidden)!.Invoke(controller, [bot, enemies, enemies[0], now]);

    private static FieldInfo Field(string name) => typeof(AutonomousWorldBotController).GetField(name, Hidden)
        ?? throw new AssertionException("AutonomousWorldBotController has no field " + name);

    private static Stand MakeBot(float x, float y)
    {
        var bot = (Stand)RuntimeHelpers.GetUninitializedObject(typeof(Stand));
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)!.SetValue(bot, new PropertyCollection());
        typeof(GameNPC).GetField("m_brains", Hidden)!.SetValue(bot, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(bot, new BotBrain());
        bot.X = (int)x; bot.Y = (int)y; bot.Z = 3000;
        // As UT_SightBackOffRoute766: a real mover, so the hold's end can read it for a pending failure.
        var mover = new NpcMovementComponent(bot);
        bot.movementComponent = mover;
        ((GameLiving)bot).movementComponent = mover;
        return bot;
    }

    private sealed class Stand : GameBot
    {
        public Stand() : base((OfflineWorldBotRecord)null) { }
        public int Stops;
        public override int X { get; set; }
        public override int Y { get; set; }
        public override int Z { get; set; }
        public override void StopAttack() { }
        public override void StopMoving() => Stops++;
        public override void StopMovingOnPath() { }
    }
}
