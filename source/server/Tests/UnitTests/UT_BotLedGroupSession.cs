using DOL.GS;
using NUnit.Framework;
using static DOL.GS.BotLedGroups;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot-led groups (docs/BOT-LED-GROUPS.md, B1): how a party follows the real player it hosts. The meetup (20 minutes,
    /// reminders at 5 and 10), the pace and the pull gate counting the player, trailing ("where u at?" at 2 minutes, on
    /// after 10 without getting closer), AFK (10/15/30 minutes), the dead player, linkdead, the camp cap, the player's role
    /// and leader succession that never picks the player.
    /// </summary>
    [TestFixture]
    public class UT_BotLedGroupSession
    {
        private const long Minute = 60_000;

        private static Observation Near(float distance = 300) => new(SameRegion: true, DistanceToLeader: distance, Alive: true, Sitting: false,
            ResourcesReady: true, AfkFlag: false, Moved: true, InCombat: false, Linkdead: false, MeetingUp: false, GroupFighting: false);

        private static Observation Far(float distance = 5_000, bool moved = true) => Near(distance) with { Moved = moved };

        private static FollowState Following(long at = 0) => new(at, meetupDone: true);

        [Test]
        public void TheMeetupEndsWhenThePlayerArrives()
        {
            var state = new FollowState(0);
            Assert.That(state.Step(Far(), Minute), Is.EqualTo(Say.None));
            Assert.That(state.ReadyToLeaveMeetup, Is.False);
            Assert.That(state.Step(Near(), 2 * Minute), Is.EqualTo(Say.None));
            Assert.That(state.ReadyToLeaveMeetup, Is.True);
        }

        [Test]
        public void TheMeetupRemindsAt5And10AndGoesOnAt20KeepingTheSpot()
        {
            var state = new FollowState(0);
            Assert.Multiple(() =>
            {
                Assert.That(state.Step(Far(), 4 * Minute), Is.EqualTo(Say.None));
                Assert.That(state.Step(Far(), 5 * Minute), Is.EqualTo(Say.MeetupReminder));
                Assert.That(state.Step(Far(), 6 * Minute), Is.EqualTo(Say.None));
                Assert.That(state.Step(Far(), 10 * Minute), Is.EqualTo(Say.MeetupReminder));
                Assert.That(state.Step(Far(), 19 * Minute), Is.EqualTo(Say.None));
                Assert.That(state.HoldsPace, Is.True, "the group waits at the meeting spot");
                Assert.That(state.Step(Far(), 20 * Minute), Is.EqualTo(Say.MeetupGoingOn));
                Assert.That(state.ReadyToLeaveMeetup, Is.True);
                Assert.That(state.Released, Is.True);
                Assert.That(state.HoldsPace, Is.False);
                Assert.That(state.BlocksPull, Is.False, "the group pulls without a player who isn't there yet");
            });
            state.Step(Near(), 30 * Minute);
            Assert.That(state.Released, Is.False, "back with the group, the player counts again");
        }

        [Test]
        public void ThePaceWaitsForAPlayerFarBehindOrInAnotherRegion()
        {
            FollowState state = Following();
            state.Step(Near(TrailRadius), Minute);
            Assert.That(state.HoldsPace, Is.False);
            state.Step(Near(TrailRadius + 1), 2 * Minute);
            Assert.That(state.HoldsPace, Is.True);
            state.Step(Near() with { SameRegion = false }, 3 * Minute);
            Assert.That(state.HoldsPace, Is.True);
        }

        [Test]
        public void ATrailingPlayerIsAskedWhereAt2MinutesAndLeftAt10WithoutGettingCloser()
        {
            FollowState state = Following();
            Assert.Multiple(() =>
            {
                Assert.That(state.Step(Far(8_000), 0), Is.EqualTo(Say.None));
                Assert.That(state.Step(Far(8_000), 2 * Minute - 1), Is.EqualTo(Say.None));
                Assert.That(state.Step(Far(8_000), 2 * Minute), Is.EqualTo(Say.WhereAreYou));
                Assert.That(state.Step(Far(8_000), 3 * Minute), Is.EqualTo(Say.None), "asked once");
                Assert.That(state.Step(Far(8_000), 10 * Minute), Is.EqualTo(Say.GoingOn));
                Assert.That(state.Released, Is.True);
                Assert.That(state.HoldsPace, Is.False);
            });
        }

        [Test]
        public void APlayerGettingCloserIsWaitedFor()
        {
            FollowState state = Following();
            state.Step(Far(20_000), 0);
            for (int minute = 1; minute <= 30; minute++)
                Assert.That(state.Step(Far(20_000 - minute * 500), minute * Minute), Is.Not.EqualTo(Say.GoingOn), $"minute {minute}");
            Assert.That(state.HoldsPace, Is.True);
        }

        [Test]
        public void ThePullWaitsForThePlayerAliveNearAndReadyLikeTheRest()
        {
            FollowState state = Following();
            state.Step(Near(), Minute);
            Assert.That(state.BlocksPull, Is.False);
            state.Step(Near() with { ResourcesReady = false }, Minute);
            Assert.That(state.BlocksPull, Is.True, "low on health or power");
            state.Step(Near() with { Sitting = true }, Minute);
            Assert.That(state.BlocksPull, Is.True, "sitting to rest");
            state.Step(Near() with { Alive = false }, Minute);
            Assert.That(state.BlocksPull, Is.True, "dead");
            Assert.That(state.HoldsPace, Is.True, "the group waits until the player is back up");
            state.Step(Far(), Minute);
            Assert.That(state.BlocksPull, Is.True, "not near the camp");
        }

        [Test]
        public void AwayTenMinutesTheGroupPullsInPlaceThenHoldsAt15AndLetsGoAt30AtABreak()
        {
            FollowState state = Following();
            Observation idle = Near() with { Moved = false };
            state.Step(Near(), 0);
            Assert.Multiple(() =>
            {
                Assert.That(state.Step(idle, 9 * Minute), Is.EqualTo(Say.None));
                Assert.That(state.Away, Is.EqualTo(AfkStage.Active));
                state.Step(idle, 10 * Minute);
                Assert.That(state.Away, Is.EqualTo(AfkStage.KeepPulling));
                Assert.That(state.BlocksPull, Is.False, "keeps pulling where it stands");
                Assert.That(state.BlocksNewTravel, Is.True, "but goes nowhere new");
                Assert.That(state.Step(idle, 15 * Minute), Is.EqualTo(Say.AfkHold));
                Assert.That(state.BlocksPull, Is.True);
                Assert.That(state.Step(idle, 16 * Minute), Is.EqualTo(Say.None), "said once");
                Assert.That(state.Step(idle with { GroupFighting = true }, 30 * Minute), Is.EqualTo(Say.None), "not mid-fight");
                Assert.That(state.Step(idle, 31 * Minute), Is.EqualTo(Say.AfkLetGo));
            });
        }

        [Test]
        public void SlashAfkCountsFromWhenItWasSetAndMovingDoesNotClearIt()
        {
            FollowState state = Following();
            state.Step(Near(), 0);
            state.Step(Near() with { AfkFlag = true }, Minute);
            Assert.That(state.Away, Is.EqualTo(AfkStage.KeepPulling));
            Assert.That(state.Step(Near() with { AfkFlag = true }, 16 * Minute), Is.EqualTo(Say.AfkHold));
            state.Step(Near(), 17 * Minute);
            Assert.That(state.Away, Is.EqualTo(AfkStage.Active), "back from /afk");
        }

        [Test]
        public void LinkdeadFor5MinutesCountsAsLeaving()
        {
            FollowState state = Following();
            Observation ld = Near() with { Linkdead = true };
            Assert.That(state.Step(ld, 0), Is.EqualTo(Say.None));
            Assert.That(state.Step(ld, 5 * Minute - 1), Is.EqualTo(Say.None));
            Assert.That(state.Step(ld, 5 * Minute), Is.EqualTo(Say.LinkdeadLeft));
            var back = Following();
            back.Step(ld, 0);
            back.Step(Near(), Minute);
            Assert.That(back.Step(ld, 2 * Minute), Is.EqualTo(Say.None), "the clock restarts after a reconnect");
        }

        [TestCase(20, 8, 20, 20, 6)]   // 20 + 8 would reach 28: held to 26 (the player + 6)
        [TestCase(20, 4, 20, 20, 4)]   // the group's own top is lower: kept
        [TestCase(23, 10, 20, 23, 3)]  // a group above the player: floor 26, cap 26
        [TestCase(30, 10, 20, 23, 3)]  // far above (levels drifted): never above the cap
        public void TheCampIsNeverMoreThanSixLevelsAboveThePlayer(int group, int bonus, int player, int planning, int expectedBonus)
        {
            (int p, int b) = HostedCampBand(group, bonus, player);
            Assert.That((p, b), Is.EqualTo((planning, expectedBonus)));
            Assert.That(p + b, Is.LessThanOrEqualTo(player + CampCapAbovePlayer));
        }

        [Test]
        public void TheGroupsLevelCountsThePlayer()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GroupLevelWithPlayer([20, 22, 24], 18), Is.EqualTo(21));
                Assert.That(GroupLevelWithPlayer([], 18), Is.EqualTo(18));
            });
        }

        [Test]
        public void ThePlayersRoleIsCountedForTheRoster()
        {
            Assert.Multiple(() =>
            {
                Assert.That(PlayerRole(eCharacterClass.Cleric), Is.EqualTo(BotPveGroupRole.Healer));
                Assert.That(PlayerRole(eCharacterClass.Armsman), Is.EqualTo(BotPveGroupRole.Tank));
                Assert.That(PlayerRole(eCharacterClass.Wizard), Is.EqualTo(BotPveGroupRole.Attacker));
            });
        }

        [Test]
        public void SuccessionElectsALivingGamebotNeverThePlayer()
        {
            MemberView player = new(true, false, true), dead = new(false, true, false), alive = new(false, true, true),
                companion = new(false, false, true);
            Assert.Multiple(() =>
            {
                Assert.That(ElectLeader([player, dead, alive]), Is.EqualTo(2));
                Assert.That(ElectLeader([player, companion, dead]), Is.EqualTo(2), "a dead gamebot before the player or a companion");
                Assert.That(ElectLeader([player, companion]), Is.EqualTo(-1), "no gamebot left: none");
                Assert.That(ElectLeader([player]), Is.EqualTo(-1));
            });
        }
    }
}
