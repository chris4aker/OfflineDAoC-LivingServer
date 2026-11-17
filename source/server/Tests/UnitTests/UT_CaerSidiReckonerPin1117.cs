using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using NUnit.Framework;
using R = DOL.GS.RealmRaidDungeonRoute;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner's PC, build 453e01a, 2026-11-17 (sweep 4), Caer Sidi (epic-albion): no kill after 13:21 in 2.5 hours. Soul
    /// Reckoner stood at 27254,36125 (1,247 from his room's middle 28472,35857; his spawn 28967,35874 is 495 from it) from
    /// 12:41 to 15:14. 9,870 "out of his room (900) he takes no damage; hold" lines, no "lead him back" line: his target was
    /// never one of the raid's members (one raider-side victim took his hits each minute and never died: a raider's pet).
    /// 65-78 raiders held him 5,666 from the muster at 29617,41288, the muster never filled (7-13 of 58-60), four stalls set
    /// the target aside and each early pull of a court monster committed "inside=108" (12 near), feeding the Crypt Lord.
    /// Soul Reckoner's rules (900 room, full shield, tether) are unchanged; these tests cover how the bots play him and the
    /// route's counts. Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_CaerSidiReckonerPin1117
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private Region _region;
        private ushort _nextId = 1_700;
        private EpicTestServerScope _server;
        private bool _smallRoom;

        private static readonly Vector3 Stuck = new(27254, 36125, 15389);
        private static readonly Vector3 Spawn = new(28967, 35874, 15370);
        private static readonly Vector3 Muster = new(29617, 41288, 15382);
        private static readonly Vector3 Body = new(27256, 36162, 15387);
        private static readonly Vector3 FatesBearer = new(28916, 42783, 15378);

        public sealed class TestReckoner : SoulReckoner { public override bool IsAlive => true; }
        public sealed class Pet : GameNPC { public override bool IsAlive => true; }
        public sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public override bool IsAlive => true;
            public override ICharacterClass CharacterClass => new ClassCabalist();
        }

        [SetUp]
        public void SetUp()
        {
            _server = new EpicTestServerScope();
            _region = new Region(new RegionData { Id = 60, Name = "Caer Sidi test", Description = "Caer Sidi test", Mobs = [] });
            _smallRoom = EpicEncounterMechanics.AllowSoulReckonerSmallRoom;
            EpicEncounterMechanics.AllowSoulReckonerSmallRoom = true;
            T.ResetSoulReckonerForTests();
        }

        [TearDown]
        public void TearDown()
        {
            EpicEncounterMechanics.AllowSoulReckonerSmallRoom = _smallRoom;
            T.ResetSoulReckonerForTests();
            _server.Dispose();
        }

        private TNpc Npc<TNpc>(Vector3 at) where TNpc : GameNPC
        {
            TNpc npc = (TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc));
            typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden)?.SetValue(npc, Activator.CreateInstance(
                typeof(GameObject).GetField("_objectInRadiusCachesLock", Hidden).FieldType));
            typeof(GameNPC).GetField("m_brains", Hidden)?.SetValue(npc, new ArrayList());
            typeof(GameNPC).GetField("m_spells", Hidden)?.SetValue(npc, new List<Spell>());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)?.SetValue(npc, new PropertyCollection());
            typeof(GameLiving).GetField("_abilitiesLock", Hidden)?.SetValue(npc, new Lock());
            typeof(GameLiving).GetField("m_abilities", Hidden)?.SetValue(npc, new Dictionary<string, Ability>());
            npc.CurrentRegion = _region;
            npc.movementComponent = new NpcMovementComponent(npc);
            ((GameLiving)npc).movementComponent = npc.movementComponent;
            npc.X = (int)at.X; npc.Y = (int)at.Y; npc.Z = (int)at.Z;
            npc.movementComponent.ForceUpdatePosition();
            npc.SpawnPoint = new Point3D((int)at.X, (int)at.Y, (int)at.Z);
            npc.ObjectID = _nextId++;
            npc.ObjectState = GameObject.eObjectState.Active;
            return npc;
        }

        private Pet PetOf(GameLiving owner, Vector3 at)
        {
            Pet pet = Npc<Pet>(at);
            typeof(GameNPC).GetField("m_ownBrain", Hidden)!.SetValue(pet, new ControlledMobBrain(owner) { Body = pet });
            return pet;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new(a.X, a.Y), new(b.X, b.Y));

        // ---- where he stood ---------------------------------------------------------------------------------------------------

        [Test]
        public void HeStoodOutOfHisRoomAndHisSpawnIsInsideIt()
        {
            Assert.That(T.SoulReckonerRoomRadius, Is.EqualTo(900), "epic_soul_reckoner_small_room on");
            Assert.That(Flat(Stuck, T.SoulReckonerRoom), Is.EqualTo(1247).Within(1));
            Assert.That(T.SoulReckonerOutOfRoom(Stuck, 900), Is.True, "out there he takes no damage");
            Assert.That(Flat(Spawn, T.SoulReckonerRoom), Is.EqualTo(495).Within(1));
            Assert.That(T.SoulReckonerOutOfRoom(Spawn, 900), Is.False);
            Assert.That(Flat(Spawn, T.SoulReckonerRoom), Is.LessThan(900 - T.SoulReckonerRoomMargin),
                "reset home, he is well inside: the re-pull is in his room");
            Assert.That(Flat(Stuck, Spawn), Is.EqualTo(1731).Within(1), "TARGET_STATUS spawnDistance=1731 (his tether 1,800: no reset)");
        }

        // ---- who leads him ----------------------------------------------------------------------------------------------------

        [Test]
        public void HisTargetsOwnerLeadsHimWhenHeFightsAPet()
        {
            var reckoner = Npc<TestReckoner>(Stuck);
            var cabalist = Npc<Bot>(new(27300, 36300, 15389));
            var holder = Npc<Bot>(new(27200, 36000, 15389));
            Pet simulacrum = PetOf(cabalist, new(27300, 36150, 15389));
            reckoner.TargetObject = simulacrum;
            Assert.That(T.PetOwner(simulacrum), Is.SameAs(cabalist));
            Assert.That(T.PetOwner(cabalist), Is.Null, "a bot is no pet");

            // 453e01a: only a raider whose own self he fought led; the pet's owner held like everyone else.
            var owner = T.SoulReckonerAdvice(cabalist, reckoner, 900);
            Assert.That((owner?.Action, owner?.StandAt), Is.EqualTo(((T.BotAction?)T.BotAction.StandOff, (Vector3?)T.SoulReckonerRoom)),
                "the pet's owner walks to the room's middle; its pet (off him while he is out) follows, and he chases it in");
            Assert.That(T.SoulReckonerAdvice(holder, reckoner, 900)?.Action, Is.EqualTo(T.BotAction.Reposition), "the rest hold");
            Assert.That(T.SoulLeaderAdvice(cabalist, reckoner, 900)?.Action, Is.EqualTo(T.BotAction.StandOff),
                "on a soul, the owner still leads him");

            Func<GameLiving, bool> ours = l => l is Bot;
            Assert.That(T.SoulReckonerLeader(simulacrum, ours, [(holder, 130f), (cabalist, 175f)]), Is.SameAs(cabalist));
            Assert.That(T.SoulReckonerLeader(holder, ours, [(cabalist, 175f)]), Is.SameAs(holder), "one of ours: itself");
            // One the raid doesn't command (a player, a companion): the nearest of ours takes the lead (a tank taunts first).
            var stranger = Npc<Pet>(new(27260, 36130, 15389));
            Assert.That(T.SoulReckonerLeader(stranger, ours, [(cabalist, 175f), (holder, 130f)]), Is.SameAs(holder));
            Assert.That(T.SoulReckonerLeader(null, ours, [(holder, 130f)]), Is.Null);
        }

        [Test]
        public void InHisRoomEveryoneFightsHimAsBefore()
        {
            var reckoner = Npc<TestReckoner>(Spawn);
            var cabalist = Npc<Bot>(new(28900, 35874, 15370));
            reckoner.TargetObject = PetOf(cabalist, new(28950, 35874, 15370));
            Assert.That(T.SoulReckonerAdvice(cabalist, reckoner, 900, breakOff: true), Is.Null, "495 from the middle: fight him");
        }

        // ---- the hold is bounded ----------------------------------------------------------------------------------------------

        [Test]
        public void AfterTwoMinutesOutOfHisRoomTheRaidBreaksOffIntoItFor30Seconds()
        {
            long t = 1_000_000; // 12:41:22
            long since = T.SoulReckonerOutSince(0, 0, outOfRoom: true, inCombat: true, t);
            Assert.That(since, Is.EqualTo(t));
            long seen = t;
            for (long now = t + 1_000; now <= t + 119_000; now += 1_000) { since = T.SoulReckonerOutSince(since, seen, true, true, now); seen = now; }
            Assert.That(since, Is.EqualTo(t), "observed every second: the clock keeps its start");
            Assert.That(T.SoulReckonerBreaksOff(since, t + 119_000), Is.False);
            Assert.That(T.SoulReckonerBreaksOff(since, t + 120_000), Is.True, "two minutes out");
            Assert.That(T.SoulReckonerBreaksOff(since, t + 149_000), Is.True);
            Assert.That(T.SoulReckonerBreaksOff(since, t + 150_000), Is.False, "the break-off lasts 30 s");
            Assert.That(T.SoulReckonerOutSince(since, t + 149_000, true, true, t + 150_000), Is.EqualTo(t + 150_000),
                "then the clock starts again: hold, and break off again two minutes later (453e01a held 2.5 hours)");
            Assert.That(T.SoulReckonerOutSince(since, seen, outOfRoom: false, inCombat: true, t + 130_000), Is.Zero, "back in his room");
            Assert.That(T.SoulReckonerOutSince(since, seen, outOfRoom: true, inCombat: false, t + 130_000), Is.Zero, "reset: no fight");
            // Review 7: unobserved (the break-off clears everyone's target), a later trip out never breaks off at once.
            Assert.That(T.SoulReckonerOutSince(since, t + 100_000, true, true, t + 106_000), Is.EqualTo(t + 106_000), "stale >5 s");
            Assert.That(T.SoulReckonerOutSince(since, t + 100_000, true, true, t + 105_000), Is.EqualTo(t), "5 s is not stale");

            var reckoner = Npc<TestReckoner>(Stuck);
            var holder = Npc<Bot>(Body);
            reckoner.TargetObject = Npc<Pet>(new(27260, 36130, 15389));
            Assert.That(T.SoulReckonerAdvice(holder, reckoner, 900, breakOff: false)?.Action, Is.EqualTo(T.BotAction.Reposition));
            var broke = T.SoulReckonerAdvice(holder, reckoner, 900, breakOff: true);
            Assert.That((broke?.Action, broke?.StandAt), Is.EqualTo(((T.BotAction?)T.BotAction.StandOff, (Vector3?)T.SoulReckonerRoom)),
                "the raiders fighting him walk into his room: he follows whoever he fights in, or walks home, and is pulled again there");
            Assert.That(broke?.Reason, Does.Contain("break off"));
        }

        // ---- the lead's own turn --------------------------------------------------------------------------------------------

        [Test]
        public void TheChosenLeadWalksInOnItsOwnTurnUntilItArrivesOrFor30Seconds()
        {
            var reckoner = Npc<TestReckoner>(Stuck);
            var cabalist = Npc<Bot>(new(27300, 36300, 15389));
            var other = Npc<Bot>(Body);
            long t = 5_000_000;
            T.PublishSoulReckonerLeader(reckoner, cabalist, t);
            Assert.That(T.SoulReckonerLeadFor(cabalist, t + 500), Is.EqualTo((T.ReckonerLeadStep.Walk, (GameNPC)reckoner)));
            Assert.That(T.SoulReckonerLeadFor(other, t + 500).Step, Is.EqualTo(T.ReckonerLeadStep.None),
                "only the chosen bot acts on it, on its own turn (no bot taunts or calls off a pet for another)");
            Assert.That(T.SoulReckonerLeadFor(cabalist, t + 3_500).Step, Is.EqualTo(T.ReckonerLeadStep.None), "not renewed: it lapses");
            for (long now = t + 1_000; now <= t + 31_000; now += 1_000) T.PublishSoulReckonerLeader(reckoner, cabalist, now);
            Assert.That(T.SoulReckonerLeadFor(cabalist, t + 31_000).Step, Is.EqualTo(T.ReckonerLeadStep.None),
                "30 s of walking at most: it goes back to healing or fighting");
            T.PublishSoulReckonerLeader(reckoner, other, t + 32_000);
            Assert.That(T.SoulReckonerLeadFor(other, t + 32_500).Step, Is.EqualTo(T.ReckonerLeadStep.Walk), "a new lead: its own 30 s");
            Assert.That(T.SoulReckonerLeadTurn(true, t + 40_000, t + 32_000, 140, t + 33_000), Is.EqualTo(T.ReckonerLeadStep.Arrived),
                "at the room's middle it is released");
            T.PublishSoulReckonerLeader(reckoner, null, t + 33_000);
            Assert.That(T.SoulReckonerLeadFor(other, t + 33_100).Step, Is.EqualTo(T.ReckonerLeadStep.None), "back in his room: no lead");
        }

        [Test]
        public void TheSameLeadWalksAgainOnHisNextTripOutOrAfterTheBreakOffWindow()
        {
            var reckoner = Npc<TestReckoner>(Stuck);
            var cabalist = Npc<Bot>(new(27300, 36300, 15389));
            long trip = 7_000_000;
            for (long now = trip; now <= trip + 31_000; now += 1_000) T.PublishSoulReckonerLeader(reckoner, cabalist, now, trip);
            Assert.That(T.SoulReckonerLeadFor(cabalist, trip + 31_000).Step, Is.EqualTo(T.ReckonerLeadStep.None), "its 30 s are up");
            // The clock starts again after the break-off's window (150 s out): the same lead walks again.
            long again = trip + 150_000;
            T.PublishSoulReckonerLeader(reckoner, cabalist, again, again);
            Assert.That(T.SoulReckonerLeadFor(cabalist, again + 500).Step, Is.EqualTo(T.ReckonerLeadStep.Walk));
            for (long now = again + 1_000; now <= again + 31_000; now += 1_000) T.PublishSoulReckonerLeader(reckoner, cabalist, now, again);
            Assert.That(T.SoulReckonerLeadFor(cabalist, again + 31_000).Step, Is.EqualTo(T.ReckonerLeadStep.None));
            // Back in his room and out again, without the lead being cleared in between: a new trip, a new walk.
            long next = again + 60_000;
            T.PublishSoulReckonerLeader(reckoner, cabalist, next, next);
            Assert.That(T.SoulReckonerLeadFor(cabalist, next + 500).Step, Is.EqualTo(T.ReckonerLeadStep.Walk));
        }

        [Test]
        public void NoBotsPetTouchesHimOutOfHisRoomOnly()
        {
            var reckoner = Npc<TestReckoner>(Stuck);
            var cabalist = Npc<Bot>(new(27300, 36300, 15389));
            Pet simulacrum = PetOf(cabalist, new(27300, 36150, 15389));
            Assert.That(T.PetMustNotHit(simulacrum, reckoner), Is.True, "1,247 out: the pet comes off him (it does nothing there)");
            reckoner.X = (int)Spawn.X; reckoner.Y = (int)Spawn.Y; reckoner.Z = (int)Spawn.Z;
            reckoner.movementComponent.ForceUpdatePosition();
            Assert.That(T.PetMustNotHit(simulacrum, reckoner), Is.False, "in his room pets fight him as before");
            Assert.That(T.PetMustNotHit(simulacrum, Npc<Pet>(Stuck)), Is.False, "only him");
        }

        // ---- the route ----------------------------------------------------------------------------------------------------------

        public sealed class WonBoss : GameEpicBoss { public override bool IsAlive => true; }

        [Test]
        public void SoulReckonerOnARaidersPetBesideTheRaidIsFightingTheRaid()
        {
            var reckoner = Npc<TestReckoner>(Stuck);
            var cabalist = Npc<Bot>(new(27300, 36300, 15389));
            var raiders = new HashSet<GameLiving> { cabalist };
            Assert.That(R.FightsTheRaid(reckoner, cabalist, raiders), Is.True);
            Assert.That(R.FightsTheRaid(reckoner, PetOf(cabalist, Stuck), raiders), Is.True,
                "12:42:22: his souls' fight over, the route saw no boss on the raid and walked 5,300 off to a soul on a late raider");
            Assert.That(R.FightsTheRaid(reckoner, PetOf(Npc<Bot>(Stuck), Stuck), raiders), Is.False, "someone else's pet");
            var trailing = Npc<Bot>(new(27254, 39000, 15389));
            Assert.That(R.FightsTheRaid(reckoner, PetOf(trailing, Stuck), new HashSet<GameLiving> { trailing }), Is.False,
                "a pet whose owner is 2,875 off (trailing): not yet");
            Assert.That(R.FightsTheRaid(reckoner, null, raiders), Is.False);
        }

        [Test]
        public void AWonRaidsBossAggroedEarlyByAPetDoesNotSwitchTheRoute()
        {
            // Krondon, Hall of the Corrupt, Avalon City and the rest: a trailing pet's natural aggro on another boss is answered as
            // before, only once that boss is on a raider.
            var boss = Npc<WonBoss>(new(40000, 40000, 15000));
            var owner = Npc<Bot>(new(40100, 40000, 15000));
            var raiders = new HashSet<GameLiving> { owner };
            Assert.That(R.FightsTheRaid(boss, PetOf(owner, new(40050, 40000, 15000)), raiders), Is.False);
            Assert.That(R.FightsTheRaid(boss, owner, raiders), Is.True, "on the raider itself: as before");
        }

        [Test]
        public void TheRaidersHeldAtHimArePinnedAwayFromTheMuster()
        {
            Assert.That(Flat(Body, Muster), Is.EqualTo(5644).Within(2), "the body 5,6xx from the muster");
            Assert.That(R.PinnedAway(true, Vector3.Distance(Body, Muster), Vector3.Distance(Body, FatesBearer)), Is.True);
            Assert.That(R.PinnedAway(false, Vector3.Distance(Body, Muster), Vector3.Distance(Body, FatesBearer)), Is.False,
                "walking, not fighting another encounter");
            Assert.That(R.PinnedAway(true, 0, Vector3.Distance(Muster, FatesBearer)), Is.False, "at the muster: mustered");
            Assert.That(R.PinnedAway(true, Vector3.Distance(Body, Muster), 1_500), Is.False, "by the target: it is at the fight");
        }

        [Test]
        public void TheMustersOwnNeedStaysTheWholeRaidsAndStallsWithThePinned()
        {
            // 13:52:57 MUSTER_STALLED mustered=12 needed=60 living=120 pinnedBy="Soul Reckoner:78": the completion need is
            // FinalMusterNeeded(living) as before (#195: 70%, half after 20 minutes), pinned or not.
            Assert.That(R.FinalMusterNeeded(120, 0), Is.EqualTo(84));
            Assert.That(R.FinalMusterNeeded(120, R.FinalMusterPatienceMilliseconds), Is.EqualTo(60));
            var progress = new R.MusterProgress();
            Assert.That(progress.Observe(12, 60, 0), Is.False);
            Assert.That(progress.Observe(12, 60, R.MusterStalledMilliseconds), Is.True, "12 of 60 for 20 minutes: the stall");
        }

        [Test]
        public void AnEarlyPullWithThePinnedLeftOutIsNeverLaxer()
        {
            // The early pull measures "inside" less the pinned against a muster's worth of the raid less them; whenever that
            // commits, the old count (pinned included) committed too.
            for (int living = 8; living <= 160; living += 4)
                for (int pinned = 0; pinned <= living; pinned += 2)
                    for (int free = 0; free <= living - pinned; free += 3)
                    {
                        bool now = T.ChooseEarlyPull(0, free, living, false, false, R.MusterNeededLeavingPinned(living, pinned, 0)) == T.EarlyPull.Commit;
                        bool before = T.ChooseEarlyPull(0, free + pinned, living, false, false, R.FinalMusterNeeded(living, 0)) == T.EarlyPull.Commit;
                        if (now) Assert.That(before, Is.True, $"living={living} pinned={pinned} free={free}");
                    }
        }

        [Test]
        public void PinnedRaidersNameThePinnerTheRouteMayTurnOn()
        {
            var reckoner = Npc<TestReckoner>(Stuck);
            var bearer = Npc<WonBoss>(FatesBearer);
            float bodyFromMuster = Vector3.Distance(Body, Muster), bodyFromBearer = Vector3.Distance(Body, FatesBearer);
            var raiders = new List<(GameLiving, GameNPC, float, float)>();
            // 76 at Soul Reckoner or one of his souls (each soul counts as him: RequiredAddOwner ?? npc), 12 at the muster.
            for (int i = 0; i < 76; i++) raiders.Add((Npc<Bot>(Body), reckoner, bodyFromMuster, bodyFromBearer));
            for (int i = 0; i < 12; i++) raiders.Add((Npc<Bot>(Muster), null, 0, Vector3.Distance(Muster, FatesBearer)));
            var pinned = R.Pinned(raiders, _ => true);
            Assert.That((pinned.By, pinned.Raiders.Count), Is.EqualTo(((GameNPC)reckoner, 76)));
            Assert.That(R.TurnsOnPinner(pinned.Raiders.Count), Is.True, "a stall turns the route on him");
            // Set aside, cleared, a hazard or a live boss's add (HostileEncounter's filters): never turned on.
            var refused = R.Pinned(raiders, _ => false);
            Assert.That((refused.By, refused.Raiders.Count), Is.EqualTo(((GameNPC)null, 76)), "still pinned, but nothing to turn on");
            // Seven pinned elsewhere: fewer than a party, the stall sets aside as before.
            var few = R.Pinned(raiders.Take(7), _ => true);
            Assert.That(R.TurnsOnPinner(few.Raiders.Count), Is.False);
            // A raider on him but beside the target (within 2,000) or at the muster is not pinned.
            Assert.That(R.Pinned([(Npc<Bot>(Body), reckoner, bodyFromMuster, 1_900f)], _ => true).Raiders, Is.Empty);
            Assert.That(R.Pinned([(Npc<Bot>(Body), reckoner, 1_400f, bodyFromBearer)], _ => true).Raiders, Is.Empty);
            _ = bearer;
        }

        [Test]
        public void AStallWithAPartysWorthPinnedTurnsOnThePinner()
        {
            // The four stalls: pinnedBy Soul Reckoner 76, 78, 65, 76 (and 70 at 15:21).
            foreach (int pinned in new[] { 76, 78, 65, 70 })
                Assert.That(R.TurnsOnPinner(pinned), Is.True, $"{pinned} pinned");
            Assert.That(R.TurnsOnPinner(R.MinimumRaidersAtTarget), Is.True, "a party's worth");
            Assert.That(R.TurnsOnPinner(R.MinimumRaidersAtTarget - 1), Is.False, "fewer: set aside as before");
        }

        [Test]
        public void ThePinnedLeaveTheEarlyPullsCountsTheLoggedCommitStands()
        {
            // 13:16:53 EARLY_PULL forerunner near=12 inside=108 raid=120 needed=84 decision=Commit, with 76 at Soul Reckoner.
            Assert.That(T.ChooseEarlyPull(12, 108, 120, false, false, R.FinalMusterNeeded(120, 0)), Is.EqualTo(T.EarlyPull.Commit),
                "453e01a: the pinned made 108 'inside' a muster's worth");
            int free = 108 - 76, needed = R.MusterNeededLeavingPinned(120, 76, 0);
            Assert.That((free, needed), Is.EqualTo((32, 30)));
            // The 32 free are a muster's worth of the 44 who can come; the attempt now counts them, not the 108.
            Assert.That(T.ChooseEarlyPull(12, free, 120, false, false, needed), Is.EqualTo(T.EarlyPull.Commit));
            // Its accounting: 13 fell of the engaged (14:54:11 fate's bearer engaged=109 fallen=13 lowest=78). Of 109 or of the
            // 33 free, it is no wipe and not the raid's (half of 120 was not in it): the verdict stands, the count is honest.
            Assert.That(R.IsWipe(109, 13, 100, 78, 120), Is.False);
            Assert.That(R.IsWipe(33, 13, 100, 78, 120), Is.False);
        }
    }
}
