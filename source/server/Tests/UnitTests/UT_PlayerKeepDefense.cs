using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS.Keeps;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    [TestFixture, NonParallelizable]
    public class UT_PlayerKeepDefense
    {
        // The Attacker (GameNPC) and Group fixtures need a server double: without it they throw TypeInitializationException
        // for DOL.GS.GameObject when the fixture runs alone (#788 review).
        private DOL.UnitTests.EpicTestServerScope _server;
        private object _botSource;
        private static FieldInfo BotSourceField => typeof(AutonomousRvrEventLayer).GetField("AttackerRealmBotSource", BindingFlags.NonPublic | BindingFlags.Static);
        [SetUp] public void SetUp()
        {
            _server = new DOL.UnitTests.EpicTestServerScope();
            _botSource = BotSourceField.GetValue(null);
            BotSourceField.SetValue(null, (Func<IEnumerable<GameLiving>>)(() => Array.Empty<GameLiving>()));
            RvrEventTestState.Clear();
        }
        [TearDown] public void TearDown()
        {
            RvrEventTestState.Clear();
            BotSourceField.SetValue(null, _botSource);
            _server.Dispose();
        }
        private static AutonomousRvrEventLayer.LiveObjective Target(string id = "defense") =>
            new(id, "Test keep", AutonomousRvrEventLayer.Intent.AssaultKeep, eRealm.Midgard, 100, 100000, 100000, 100, false, 0, 0, 0, 0);

        [Test]
        public void AlarmOpensImmediateTimedBattleAndRepeatedHitsDoNotResetItsTimer()
        {
            Assert.That(AutonomousRvrEventLayer.DefenseResponseMilliseconds, Is.EqualTo(4 * 60 * 60_000L));
            long now = GameLoop.GameLoopTime + 100;
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now), Is.True);
            var first = AutonomousRvrEventLayer.Snapshot().Single();
            Assert.That(first.BattleStarted, Is.True);
            Assert.That(first.Kind, Does.Contain("defense"));
            Assert.That(first.RemainingMilliseconds, Is.EqualTo(now + AutonomousRvrEventLayer.DefenseResponseMilliseconds - GameLoop.GameLoopTime));
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now + 30_000), Is.False);
            Assert.That(AutonomousRvrEventLayer.Snapshot().Single().RemainingMilliseconds, Is.EqualTo(first.RemainingMilliseconds));
        }

        [Test]
        public void PlayerResponseDoesNotBlockIndependentAutonomousSiegeOrResetExistingSiegeClock()
        {
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now);
            Assert.That(AutonomousRvrEventLayer.ForceStart(Target("other"), eRealm.Hibernia, now, out _), Is.True);
            var before = AutonomousRvrEventLayer.Snapshot().Single(e => e.TargetId == "other");
            AutonomousRvrEventLayer.BeginDefenseResponse(Target("other"), eRealm.Albion, "offline", now + 30_000);
            var after = AutonomousRvrEventLayer.Snapshot().Single(e => e.TargetId == "other");
            Assert.That(after.RemainingMilliseconds, Is.EqualTo(before.RemainingMilliseconds));
            Assert.That(after.Attacker, Is.EqualTo(eRealm.Hibernia));
        }

        [Test]
        public void QuietDefenseSurvivesFiveMinutesButEndsAtFourHours()
        {
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now);
            AutonomousRvrEventLayer.PulseDefense(now + 5 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1));
            AutonomousRvrEventLayer.PulseDefense(now + AutonomousRvrEventLayer.DefenseResponseMilliseconds);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty);
        }

        // #523 step 1 (a), owner 2026-11-24: a player-attack defense ends about ten minutes after the attackers leave.
        [Test]
        public void DefenseEndsTenMinutesAfterTheAttackersLeave()
        {
            Assert.That(AutonomousRvrEventLayer.RepelMilliseconds, Is.EqualTo(10 * 60_000));
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now);
            AutonomousRvrEventLayer.PulseDefense(now + 9 * 60_000 + 55_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "Not yet ten minutes quiet");
            AutonomousRvrEventLayer.PulseDefense(now + 10 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty, "Ten minutes with no attacker ends the defense");
        }

        [Test]
        public void AHitOnTheKeepPutsTheEndBackTenMinutes()
        {
            long now = GameLoop.GameLoopTime + 100;
            var target = Target();
            AutonomousRvrEventLayer.BeginDefenseResponse(target, eRealm.Albion, "offline", now);
            Pressure()[target.Id] = now + 8 * 60_000; // a guard or door hit by any enemy realm (ObserveKeepAttack)
            AutonomousRvrEventLayer.PulseDefense(now + 12 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1));
            AutonomousRvrEventLayer.PulseDefense(now + 17 * 60_000 + 55_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1));
            AutonomousRvrEventLayer.PulseDefense(now + 18 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty);
        }

        // Owner, 2026-11-27 (#784, answer 1B): "bots should be performing their own sieges and defense without the player",
        // so attacker-realm bots still at the keep hold the defense open after the player leaves.
        [Test]
        public void TheAttackingRealmsBotsAtTheKeepHoldTheDefenseOpen()
        {
            long now = GameLoop.GameLoopTime + 100;
            var target = Target();
            AutonomousRvrEventLayer.BeginDefenseResponse(target, eRealm.Albion, "offline", now);
            object active = EventOf(target.Id);
            ((Dictionary<string, int>)active.GetType().GetField("Attackers").GetValue(active)).Add("albion-helpers", 8);
            var present = (IDictionary)active.GetType().GetField("Present").GetValue(active);
            long seenAt = now + 9 * 60_000;
            present[1L] = ("albion-helpers", eRealm.Albion, seenAt, (GameBot)null, new Vector3(target.X, target.Y, target.Z), target.RegionId);
            AutonomousRvrEventLayer.PulseDefense(seenAt + 5_000);
            AutonomousRvrEventLayer.PulseDefense(now + 18 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "Attackers seen at minute 9 hold it to minute 19");
            AutonomousRvrEventLayer.PulseDefense(seenAt + 5_000 + AutonomousRvrEventLayer.RepelMilliseconds);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty);
        }

        // Review ask 2: the 4-hour cap still ends a defense whose attack is seen the whole time (a hit every five minutes,
        // so the ten-minute rule never fires). Without the cap it would stay open.
        [Test]
        public void ADefenseAttackedTheWholeTimeStillEndsAtFourHours()
        {
            long now = GameLoop.GameLoopTime + 100;
            var target = Target();
            AutonomousRvrEventLayer.BeginDefenseResponse(target, eRealm.Albion, "offline", now);
            long cap = now + AutonomousRvrEventLayer.DefenseResponseMilliseconds;
            for (long t = now + 5 * 60_000; t < cap; t += 5 * 60_000)
            {
                Pressure()[target.Id] = t;
                AutonomousRvrEventLayer.PulseDefense(t);
                Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), $"Still attacked at minute {(t - now) / 60_000}");
            }
            Pressure()[target.Id] = cap - 1_000;
            AutonomousRvrEventLayer.PulseDefense(cap - 1_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "Hit a second before the cap");
            AutonomousRvrEventLayer.PulseDefense(cap + 5_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty, "The four-hour cap ends it though the attack was seen a moment ago");
        }

        // Review ask 3: the attacking player standing at the keep (no hits after the alarm) holds the defense open, through
        // the alarm path that records who raised it (active.AttackingPlayer = alarm.Player).
        [Test]
        public void TheAttackingPlayerAtTheKeepHoldsTheDefenseOpenUntilTheyLeave()
        {
            using var server = new DOL.UnitTests.EpicTestServerScope();
            long now = GameLoop.GameLoopTime + 100;
            var keep = (DOL.GS.Keeps.GameKeep)RuntimeHelpers.GetUninitializedObject(typeof(DOL.GS.Keeps.GameKeep));
            keep.DBKeep = new DOL.Database.DbKeep { KeepID = 7, Name = "Test keep", Realm = (byte)eRealm.Midgard, Region = 100,
                X = 100000, Y = 100000, Z = 100, BaseLevel = 50 };
            var player = (KeepPlayer)RuntimeHelpers.GetUninitializedObject(typeof(KeepPlayer));
            player.TestX = 100000; player.TestY = 100000; player.TestZ = 100;
            Alarms()["rvr-keep-7"] = NewAlarm(keep, player, now);

            AutonomousRvrEventLayer.PulseDefense(now);
            Assert.That(AutonomousRvrEventLayer.Snapshot().Single().TargetId, Is.EqualTo("rvr-keep-7"), "The alarm raised the defense");
            AutonomousRvrEventLayer.PulseDefense(now + 6 * 60_000);
            AutonomousRvrEventLayer.PulseDefense(now + 12 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "The player is still at the keep at minute 12");

            player.TestX = 200000; // rides away at minute 12
            AutonomousRvrEventLayer.PulseDefense(now + 12 * 60_000 + 5_000);
            AutonomousRvrEventLayer.PulseDefense(now + 21 * 60_000 + 55_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Has.Length.EqualTo(1), "Not yet ten minutes since they left");
            AutonomousRvrEventLayer.PulseDefense(now + 22 * 60_000);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty, "Ten minutes after the player left, the defense ends");
        }

        private sealed class KeepPlayer : GamePlayer
        {
            private KeepPlayer() : base(null, null) { }
            public int TestX, TestY, TestZ;
            public override bool IsAlive => true;
            public override eRealm Realm { get => eRealm.Albion; set { } }
            public override int X => TestX;
            public override int Y => TestY;
            public override int Z => TestZ;
            public override ushort CurrentRegionID { get => 100; set { } }
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
            public override DOL.GS.PacketHandler.IPacketLib Out => DispatchProxy.Create<DOL.GS.PacketHandler.IPacketLib, Silent>();
        }

        public class Silent : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args) =>
                method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null;
        }

        private static IDictionary Alarms() =>
            (IDictionary)typeof(AutonomousRvrEventLayer).GetField("DefenseAlarms", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        // The alarm record is private; fill its constructor by parameter type so the test survives a change of its fields.
        // A bool is the alarm's door hit or guard death (#523 (b)): these fixtures predate the lone-guard-pull rule, so their
        // lone attacker has hit a door (UT_PlayerSiegeAlarm523 covers the guard hit alone).
        private static object NewAlarm(AbstractGameKeep keep, GamePlayer player, long tick)
        {
            var type = typeof(AutonomousRvrEventLayer).GetNestedType("Alarm", BindingFlags.NonPublic);
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            object[] args = ctor.GetParameters().Select(p =>
                p.ParameterType == typeof(long) ? tick :
                p.ParameterType == typeof(int) ? 1 :
                p.ParameterType == typeof(bool) ? true :
                typeof(GamePlayer).IsAssignableFrom(p.ParameterType) ? player :
                typeof(AbstractGameKeep).IsAssignableFrom(p.ParameterType) ? (object)keep : null).ToArray();
            return ctor.Invoke(args);
        }

        [TestCase(true, true, 100, 0f, 0f, 0f, true)]
        [TestCase(true, true, 100, 9_000f, 3_000f, 0f, true)]
        [TestCase(true, true, 100, 9_000f, 4_000f, 0f, false)]
        [TestCase(true, true, 100, 0f, 0f, 2_500f, false)]
        [TestCase(true, true, 101, 0f, 0f, 0f, false)]
        [TestCase(false, true, 100, 0f, 0f, 0f, false)]
        [TestCase(true, false, 100, 0f, 0f, 0f, false)]
        public void AnAttackerCountsAtTheKeepOnlyAliveInItsRegionAndInsideTheRing(bool alive, bool inPlay, int region,
            float dx, float dy, float dz, bool expected)
        {
            Assert.That(AutonomousRvrEventLayer.AttackerAtKeep(alive, inPlay, (ushort)region, 100, dx, dy, dz), Is.EqualTo(expected));
        }

        [TestCase(0L, 99_000_000L, false)]
        [TestCase(1_000L, 1_000L + 10 * 60_000 - 1, false)]
        [TestCase(1_000L, 1_000L + 10 * 60_000, true)]
        public void RepelRuleNeedsTenQuietMinutes(long seen, long now, bool expected)
        {
            Assert.That(AutonomousRvrEventLayer.PlayerAttackRepelled(now, seen), Is.EqualTo(expected));
        }

        private static ConcurrentDictionary<string, long> Pressure() =>
            (ConcurrentDictionary<string, long>)typeof(AutonomousRvrEventLayer).GetField("KeepCombatPressure", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);

        private static object EventOf(string id) =>
            ((IDictionary)typeof(AutonomousRvrEventLayer).GetField("Events", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null))[id];

        [Test]
        public void CrossRealmAttackPreservesBothIndependentDefenseWindows()
        {
            long now = GameLoop.GameLoopTime + 100;
            AutonomousRvrEventLayer.BeginDefenseResponse(Target("albion") with { OwningRealm = eRealm.Albion }, eRealm.Midgard, "offline", now);
            var old = AutonomousRvrEventLayer.Snapshot().Single();
            AutonomousRvrEventLayer.BeginDefenseResponse(Target("hibernia") with { OwningRealm = eRealm.Hibernia }, eRealm.Midgard, "offline", now + 60_000);
            var events = AutonomousRvrEventLayer.Snapshot();
            Assert.That(events.Length, Is.EqualTo(2));
            Assert.That(events.Single(e => e.TargetId == "albion").RemainingMilliseconds, Is.EqualTo(old.RemainingMilliseconds));
            Assert.That(AutonomousRvrEventLayer.CanRedirectDefense(eRealm.Albion, eRealm.Hibernia, "offline", "offline", now, now + 60_000), Is.False);
            Assert.That(AutonomousRvrEventLayer.CanRedirectDefense(eRealm.Albion, eRealm.Albion, "offline", "offline", now, now + 60_000), Is.True);
            Assert.That(AutonomousRvrEventLayer.CanRedirectDefense(eRealm.Albion, eRealm.Albion, "offline", "offline", now + 60_000, now), Is.False);
            Assert.That(AutonomousRvrEventLayer.CanRedirectDefense(eRealm.Albion, eRealm.Albion, "other", "offline", now, now + 60_000), Is.False);
        }

        [Test]
        public void FriendlyAndProtectedKeepsDoNotRaiseHostileResponse()
        {
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Midgard, "offline", 100), Is.False);
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target() with { IsPortalKeep = true }, eRealm.Albion, "offline", 100), Is.False);
            Assert.That(AutonomousRvrEventLayer.Snapshot(), Is.Empty);
        }

        [Test]
        public void ResponseScalesWithTheAttackingForce()
        {
            Assert.That(AutonomousRvrEventLayer.ScaleOf(1), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Small));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(8), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(40), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Large));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(80), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Massive));
            int previous = 0;
            foreach (var scale in new[] { AutonomousRvrEventLayer.DefenseScale.Small, AutonomousRvrEventLayer.DefenseScale.Standard,
                         AutonomousRvrEventLayer.DefenseScale.Large, AutonomousRvrEventLayer.DefenseScale.Massive })
            {
                int defenders = AutonomousRvrEventLayer.ResponseCap(true, scale);
                Assert.That(defenders, Is.GreaterThan(previous));
                Assert.That(defenders, Is.LessThanOrEqualTo(AutonomousRvrEventLayer.ResponseCap(true)));
                Assert.That(AutonomousRvrEventLayer.ResponseCap(false, scale), Is.LessThan(defenders));
                previous = defenders;
            }
            Assert.That(AutonomousRvrEventLayer.RallyLine(AutonomousRvrEventLayer.DefenseScale.Small), Does.Contain("small defense force"));
            Assert.That(AutonomousRvrEventLayer.RallyLine(AutonomousRvrEventLayer.DefenseScale.Standard), Does.StartWith("A defense force"));
            Assert.That(AutonomousRvrEventLayer.RallyLine(AutonomousRvrEventLayer.DefenseScale.Large), Does.Contain("large defensive force"));
            Assert.That(AutonomousRvrEventLayer.RallyLine(AutonomousRvrEventLayer.DefenseScale.Massive), Does.Contain("massive defense force"));
        }

        // #523, owner 2026-11-24: a player-attack defense is sized "by the attackers actually seen", not the roster.
        [TestCase(0, 1)]
        [TestCase(1, 1)]
        [TestCase(3, 3)]
        [TestCase(80, 80)]
        public void SeenForceNeverCountsBelowOne(int seen, int expected)
        {
            Assert.That(AutonomousRvrEventLayer.SeenForceSize(seen), Is.EqualTo(expected));
        }

        private sealed class Attacker : GameNPC
        {
            public int TestX, TestY, TestZ;
            public ushort TestRegion = 100;
            public bool Alive = true;
            public eRealm TestRealm = eRealm.Albion;
            public override bool IsAlive => Alive;
            public override int X => TestX;
            public override int Y => TestY;
            public override int Z => TestZ;
            public override ushort CurrentRegionID { get => TestRegion; set { } }
            public override eRealm Realm { get => TestRealm; set { } }
            public override eObjectState ObjectState { get => eObjectState.Active; set { } }
        }

        private static Attacker At(int x, int y = 100000, int z = 100)
        {
            var attacker = (Attacker)RuntimeHelpers.GetUninitializedObject(typeof(Attacker));
            attacker.TestX = x; attacker.TestY = y; attacker.TestZ = z;
            attacker.TestRegion = 100; attacker.Alive = true; attacker.TestRealm = eRealm.Albion;
            return attacker;
        }

        private static Group GroupOf(params GameLiving[] members)
        {
            var group = new Group(members[0]);
            var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(group);
            foreach (GameLiving member in members) { list.Add(member); member.Group = group; }
            return group;
        }

        private static int Seen(GameLiving attacker) => AutonomousRvrEventLayer.SeenForceSize(attacker, 100, 100000, 100000, 100);

        [Test]
        public void OnlyTheGroupMembersAtTheKeepSizeTheDefense()
        {
            // A group of eight (the player and seven companions) with only three at the keep: Standard, from three.
            Attacker leader = At(100000);
            var members = new List<GameLiving> { leader, At(101000), At(102000) };
            for (int i = 0; i < 5; i++) members.Add(At(150000)); // fifty thousand units away: not seen
            GroupOf(members.ToArray());
            Assert.That(leader.Group.MemberCount, Is.EqualTo(8), "the roster is eight");
            Assert.That(Seen(leader), Is.EqualTo(3));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(Seen(leader)), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard));
        }

        [Test]
        public void AbsentDeadAndOtherRegionMembersAreNotCounted()
        {
            Attacker leader = At(100000), dead = At(100000), elsewhere = At(100000), high = At(100000, z: 5000),
                otherRealm = At(100000), here = At(100500);
            dead.Alive = false;
            elsewhere.TestRegion = 101;
            otherRealm.TestRealm = eRealm.Hibernia;
            GroupOf(leader, dead, elsewhere, high, otherRealm, here);
            Assert.That(Seen(leader), Is.EqualTo(2), "the leader and the one living member in the ring");
        }

        [Test]
        public void ACompanionRaidCountsOnlyWhoIsThere()
        {
            // A /raid 40 with twelve at the keep: Large (9-40), not the forty of the roster; never Massive.
            Attacker leader = At(100000);
            var members = new List<GameLiving> { leader };
            for (int i = 0; i < 11; i++) members.Add(At(100000 + i * 100));
            for (int i = 0; i < 28; i++) members.Add(At(100000, y: 130000));
            GroupOf(members.ToArray());
            Assert.That(Seen(leader), Is.EqualTo(12));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(Seen(leader)), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Large));
        }

        [Test]
        public void ASoloOrFarAwayAttackerStillCountsOne()
        {
            Assert.That(Seen(At(100000)), Is.EqualTo(1), "solo at the keep");
            Assert.That(Seen(At(200000)), Is.EqualTo(1), "the attack itself was seen (a pet or a siege engine hit)");
            Assert.That(Seen(null), Is.EqualTo(1));
            Attacker far = At(200000);
            GroupOf(far, At(200000), At(200000));
            Assert.That(Seen(far), Is.EqualTo(1), "a whole group away from the keep is still one seen attack");
        }

        // #788 answer 49A (owner, 2026-11-27): the attacking realm's gamebots at the keep count with the player's group.
        private static int Seen(GameLiving attacker, params GameLiving[] bots) =>
            AutonomousRvrEventLayer.SeenForceSize(attacker, 100, 100000, 100000, 100, bots);

        [Test]
        public void TheAttackingRealmsBotsAtTheKeepAreCounted()
        {
            Attacker solo = At(100000);
            Assert.That(Seen(solo, At(100500), At(101000), At(99000, y: 104000)), Is.EqualTo(4), "the player and three bots in the ring");
            Assert.That(AutonomousRvrEventLayer.ScaleOf(Seen(solo, At(100500), At(101000), At(99000, y: 104000))),
                Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard));

            // A player away from the keep with eight bots attacking it: eight, Large.
            Attacker away = At(200000);
            var bots = Enumerable.Range(0, 8).Select(i => (GameLiving)At(100000 + i * 200)).ToArray();
            Assert.That(Seen(away, bots), Is.EqualTo(8));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(Seen(away, bots)), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Large));

            // A bot that is also in the player's group, or listed twice, counts once.
            Attacker leader = At(100000), grouped = At(100100);
            GroupOf(leader, grouped);
            Assert.That(Seen(leader, grouped, grouped, At(100200)), Is.EqualTo(3));
        }

        [Test]
        public void BotsElsewhereOrOfOtherRealmsAreNotCounted()
        {
            Attacker far = At(110000), high = At(100000, z: 5000), otherRegion = At(100000), dead = At(100000),
                midgard = At(100000), hibernia = At(100000), here = At(100000);
            otherRegion.TestRegion = 101;
            dead.Alive = false;
            midgard.TestRealm = eRealm.Midgard;   // the keep's own realm: a defender, not an attacker
            hibernia.TestRealm = eRealm.Hibernia; // the third realm
            Assert.That(Seen(At(100000), far, high, otherRegion, dead, midgard, hibernia), Is.EqualTo(1), "only the player");
            Assert.That(Seen(At(100000), far, high, otherRegion, dead, midgard, hibernia, here), Is.EqualTo(2));
            Assert.That(Seen(At(200000), far, midgard, hibernia), Is.EqualTo(1), "nobody of the attack at the keep: still the floor of one");
        }

        [Test]
        public void TheFloorAndCapsHoldWithTheBotsCounted()
        {
            Assert.That(Seen(null, At(100000)), Is.EqualTo(1), "no attacker: the floor");
            Assert.That(Seen(At(200000)), Is.EqualTo(1), "no one at the keep: the floor");
            var army = Enumerable.Range(0, 300).Select(i => (GameLiving)At(100000 + i % 50 * 100, y: 100000 + i / 50 * 100)).ToArray();
            int seen = Seen(At(100000), army);
            Assert.That(seen, Is.EqualTo(301));
            var scale = AutonomousRvrEventLayer.ScaleOf(seen);
            Assert.That(scale, Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Massive));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, scale), Is.EqualTo(240), "the defenders' cap is unchanged");
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, scale), Is.EqualTo(96), "the helpers' cap is unchanged");
        }

        // #788 review ask 2: the live sizing line in PulseDefense. A group of twelve with three at the keep is Standard
        // (sized from three; the roster of twelve would be Large); then four attacker-realm gamebots at the keep make it
        // seven seen, and nine bots make it twelve: Large.
        [Test]
        public void TheAlarmSizesTheDefenseByTheGroupAndBotsAtTheKeep()
        {
            long now = GameLoop.GameLoopTime + 100;
            var keep = (DOL.GS.Keeps.GameKeep)RuntimeHelpers.GetUninitializedObject(typeof(DOL.GS.Keeps.GameKeep));
            keep.DBKeep = new DOL.Database.DbKeep { KeepID = 8, Name = "Test keep", Realm = (byte)eRealm.Midgard, Region = 100,
                X = 100000, Y = 100000, Z = 100, BaseLevel = 50 };
            var player = (KeepPlayer)RuntimeHelpers.GetUninitializedObject(typeof(KeepPlayer));
            player.TestX = 100000; player.TestY = 100000; player.TestZ = 100;
            var members = new List<GameLiving> { player, At(100500), At(101000) };
            for (int i = 0; i < 9; i++) members.Add(At(150000));
            GroupOf(members.ToArray());
            Assert.That(player.Group.MemberCount, Is.EqualTo(12));

            Alarms()["rvr-keep-8"] = NewAlarm(keep, player, now);
            AutonomousRvrEventLayer.PulseDefense(now);
            Assert.That(ScaleOfEvent("rvr-keep-8"), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard), "three of twelve at the keep");

            var bots = Enumerable.Range(0, 9).Select(i => (GameLiving)At(100000 + i * 300)).ToArray();
            bots[8] = At(300000); // one bot far away
            BotSourceField.SetValue(null, (Func<IEnumerable<GameLiving>>)(() => bots.Take(4)));
            Alarms()["rvr-keep-8"] = NewAlarm(keep, player, now + 6_000);
            AutonomousRvrEventLayer.PulseDefense(now + 6_000);
            Assert.That(ScaleOfEvent("rvr-keep-8"), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard), "three and four bots: seven");

            BotSourceField.SetValue(null, (Func<IEnumerable<GameLiving>>)(() => bots));
            Alarms()["rvr-keep-8"] = NewAlarm(keep, player, now + 12_000);
            AutonomousRvrEventLayer.PulseDefense(now + 12_000);
            Assert.That(ScaleOfEvent("rvr-keep-8"), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Large), "three and eight bots at the keep: eleven");
        }

        private static AutonomousRvrEventLayer.DefenseScale ScaleOfEvent(string id)
        {
            object active = EventOf(id);
            return (AutonomousRvrEventLayer.DefenseScale)active.GetType().GetField("DefenseScale").GetValue(active);
        }

        [Test]
        public void TiersAndCapsAreUnchanged()
        {
            Assert.That(AutonomousRvrEventLayer.ScaleOf(1), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Small));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(2), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Standard));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(9), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Large));
            Assert.That(AutonomousRvrEventLayer.ScaleOf(41), Is.EqualTo(AutonomousRvrEventLayer.DefenseScale.Massive));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, AutonomousRvrEventLayer.DefenseScale.Small), Is.EqualTo(24));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, AutonomousRvrEventLayer.DefenseScale.Standard), Is.EqualTo(48));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, AutonomousRvrEventLayer.DefenseScale.Large), Is.EqualTo(128));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true, AutonomousRvrEventLayer.DefenseScale.Massive), Is.EqualTo(240));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, AutonomousRvrEventLayer.DefenseScale.Small), Is.EqualTo(8));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false, AutonomousRvrEventLayer.DefenseScale.Massive), Is.EqualTo(96));
        }

        [Test]
        public void DefenseMarchesInNumbersNotATrickle()
        {
            // A huge force over speed (owner): 80% of the force gathers before it marches (or ten minutes pass).
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(240), Is.EqualTo(192));
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(64), Is.EqualTo(51));
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(24), Is.EqualTo(19));
            Assert.That(AutonomousRvrEventLayer.PlayerDefenseMusterMilliseconds, Is.EqualTo(10 * 60_000));
            Assert.That(AutonomousRvrEventLayer.DefenseWaveSize(4), Is.EqualTo(4));
        }

        [Test]
        public void PlayerHelpingAPlannedSiegeRaisesNoDefense()
        {
            long now = GameLoop.GameLoopTime + 100;
            Assert.That(AutonomousRvrEventLayer.ForceStart(Target(), eRealm.Albion, now, out _), Is.True);
            Assert.That(AutonomousRvrEventLayer.BeginDefenseResponse(Target(), eRealm.Albion, "offline", now + 1000,
                AutonomousRvrEventLayer.DefenseScale.Small), Is.False);
            Assert.That(AutonomousRvrEventLayer.IsPlayerDefenseResponse(Target().Id, now + 1000), Is.False);
        }

        [Test]
        public void ResponseHasLargeDefenseAndBoundedHelpersWithStableReserve()
        {
            Assert.That(AutonomousRvrEventLayer.ResponseCap(true), Is.EqualTo(240));
            Assert.That(AutonomousRvrEventLayer.ResponseCap(false), Is.EqualTo(96));
            Assert.That(Enumerable.Range(1, 100).Count(id => AutonomousRvrEventLayer.ResponseReserve(id)), Is.EqualTo(30));
            Assert.That(AutonomousRvrEventLayer.PlayerInstigator(null), Is.Null);
        }

        [TestCase(true, true, false, true)]
        [TestCase(true, true, true, false)]
        [TestCase(false, true, false, false)]
        [TestCase(false, false, false, true)]
        public void ForcedExpeditionMayCoexistButNeverDuplicateEncounter(bool forced, bool sameRealm, bool sameEncounter, bool allowed)
        {
            Assert.That(RealmRaidRecruitmentPolicy.CanOpenEvent(forced, sameRealm, sameEncounter), Is.EqualTo(allowed));
        }
    }
}
