using System;
using System.Collections;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>
/// #766 (rc.2 4f5a2aa against rc.1 820944dc, the owner's PC): AUTONOMOUS_ROUTE_RECOVERY in region 200 rose 7x, mostly
/// NoPathFound on travel in Emain Macha, after #662's sight size-up (#715). A back-off is a plain PathTo outside the route's
/// own orders. When that walk could not be pathed, the mover kept its NoPathFound pending. When the route resumed with the
/// same destination, it took that failure as its own: AppliesToCurrentDestination(false, ...) holds, and the back-off point is
/// on the mesh, so it is not set aside as foreign. Its recovery corners then aimed at the back-off point, and after three the
/// travel goal was dropped. The hold now keeps its own failure, and the route's progress clock restarts when the hold ends.
/// Tests only: the share of the rc.2 counts this explains needs the PC log join on #766.
/// </summary>
[TestFixture, NonParallelizable]
public class UT_SightBackOffRoute766
{
    private const BindingFlags Hidden = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly Vector3 Goal = new(440000, 300000, 3000);
    private static readonly Vector3 BackOff = new(431500, 318200, 3400);
    private static readonly Vector3 Standing = new(430000, 320000, 3400);

    [Test]
    public void BeforeTheFixTheRouteTookTheBackOffsFailureAsItsOwn()
    {
        // The route's rules are unchanged; the hold has to keep its own failure away from them.
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousRouteRecoveryPolicy.AppliesToCurrentDestination(false, BackOff, Goal), Is.True,
                "an unchanged destination takes any pending failure");
            Assert.That(AutonomousForeignRouteFailure.IsForeign(BackOff, Goal, null), Is.True, "the back-off point is far from the goal");
            Assert.That(AutonomousForeignRouteFailure.SetAside(true, true, failedOffMesh: false, alreadySetAside: false), Is.False,
                "but it is on the mesh, so it was not set aside");
        });
    }

    [Test]
    public void OnlyAFailureAtTheBackOffPointIsTheHolds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(RvrSightSizeUp.FailureIsBackOffs(BackOff, BackOff), Is.True);
            Assert.That(RvrSightSizeUp.FailureIsBackOffs(BackOff, BackOff + new Vector3(60, 60, 20)), Is.True, "moved onto its floor");
            Assert.That(RvrSightSizeUp.FailureIsBackOffs(BackOff, BackOff + new Vector3(120, 0, 0)), Is.False);
            Assert.That(RvrSightSizeUp.FailureIsBackOffs(BackOff, Goal), Is.False, "the route's own failure");
            Assert.That(RvrSightSizeUp.FailureIsBackOffs(null, BackOff), Is.False, "no back-off walk ordered");
        });
    }

    [Test]
    public void TheRouteResumesCleanAfterAFailedBackOff()
    {
        var (controller, bot) = Held(failedAt: BackOff);
        EndHold(controller, bot, 100_000);
        Assert.Multiple(() =>
        {
            Assert.That(bot.TryConsumeAutonomousPathFailure(out _, out _), Is.False, "the back-off's failure stayed with the hold");
            Assert.That(Field("_lastRouteProgressTick").GetValue(controller), Is.EqualTo(100_000L),
                "the wait is not a route stall");
            Assert.That(Field("_lastRoutePosition").GetValue(controller), Is.EqualTo(Standing));
            Assert.That(Field("_sightBackOffOrder").GetValue(controller), Is.Null);
            Assert.That(Field("_sightHoldUntil").GetValue(controller), Is.EqualTo(0L));
        });
    }

    [Test]
    public void TheRoutesOwnFailureIsLeftForItsRecovery()
    {
        var (controller, bot) = Held(failedAt: Goal);
        EndHold(controller, bot, 100_000);
        Assert.That(bot.TryConsumeAutonomousPathFailure(out PathfindingStatus status, out Vector3 failed), Is.True);
        Assert.That((status, failed), Is.EqualTo((PathfindingStatus.NoPathFound, Goal)));
    }

    [Test]
    public void TheHoldTakesItsFailureBeforeOrderingTheWalkAgain()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source", "server", "GameServer", "bots", "BotBrain.cs")))
            dir = Path.GetDirectoryName(dir);
        Assert.That(dir, Is.Not.Null, "repository root");
        string sight = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous",
            "AutonomousWorldBotController.SightSizeUp.cs")).Replace("\r\n", "\n");
        int take = sight.IndexOf("if (TakeBackOffFailure(bot))\n            _sightBackOffPoint = null;", StringComparison.Ordinal);
        int repath = sight.IndexOf("_sightRepaths++;", StringComparison.Ordinal);
        Assert.Multiple(() =>
        {
            Assert.That(take, Is.GreaterThanOrEqualTo(0), "a failed back-off walk is not ordered again");
            Assert.That(repath, Is.GreaterThan(take), "checked before the repath");
            Assert.That(sight, Does.Contain("_sightBackOffOrder = point;\n            bot.PathTo(point, bot.MaxSpeed);"),
                "the walk's point is remembered as it is ordered");
        });
    }

    private static FieldInfo Field(string name) => typeof(AutonomousWorldBotController).GetField(name, Hidden)
        ?? throw new AssertionException("AutonomousWorldBotController has no field " + name);

    private static void EndHold(AutonomousWorldBotController controller, GameBot bot, long now) =>
        typeof(AutonomousWorldBotController).GetMethod("EndSightHold", Hidden)!.Invoke(controller, [bot, "waited", now]);

    /// <summary>A bot in a back-off hold whose mover holds a NoPathFound for <paramref name="failedAt"/>.</summary>
    private static (AutonomousWorldBotController, Stand) Held(Vector3 failedAt)
    {
        var bot = (Stand)RuntimeHelpers.GetUninitializedObject(typeof(Stand));
        // As UT_SeamReversalGuard570's walker: the fields the mover and the controller read on a bot built without its constructor.
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)!.SetValue(bot, new PropertyCollection());
        typeof(GameNPC).GetField("m_brains", Hidden)!.SetValue(bot, new ArrayList());
        typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(bot, new BotBrain());
        bot.X = (int)Standing.X; bot.Y = (int)Standing.Y; bot.Z = (int)Standing.Z;
        var mover = new NpcMovementComponent(bot);
        bot.movementComponent = mover;
        ((GameLiving)bot).movementComponent = mover;
        typeof(NpcMovementComponent).GetField("_autonomousPathFailurePending", Hidden)!.SetValue(mover, true);
        typeof(NpcMovementComponent).GetField("_autonomousPathFailureStatus", Hidden)!.SetValue(mover, PathfindingStatus.NoPathFound);
        typeof(NpcMovementComponent).GetField("_autonomousPathFailureDestination", Hidden)!.SetValue(mover, failedAt);
        var controller = new AutonomousWorldBotController();
        Field("_sightHoldUntil").SetValue(controller, 120_000L);
        Field("_sightBackOffOrder").SetValue(controller, (Vector3?)BackOff);
        Field("_lastRouteProgressTick").SetValue(controller, 1_000L);
        return (controller, bot);
    }

    private sealed class Stand : GameBot
    {
        public Stand() : base((OfflineWorldBotRecord)null) { }
        public override int X { get; set; }
        public override int Y { get; set; }
        public override int Z { get; set; }
    }
}
