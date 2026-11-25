using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Port of DetShaft/OfflineDAoC-LivingServer a303aefaf (their issue #114, 2026-11-18): a bot passed through a keep door by a move (GameObject.MoveTo stops and restarts
    // the brain) came out with no FSM state, because ABrain.Stop sets WAKING_UP and a bot brain has no such state. With
    // no state, a bot in the player's group stood frozen in the gate passage.
    [TestFixture]
    public class UT_BotBrainRestartState
    {
        [Test]
        public void AFollowingBot_FollowsAgain()
        {
            Assert.That(BotBrain.ResumeStateAfterRestart(eFSMStateType.FOLLOW, true, false), Is.EqualTo(eFSMStateType.FOLLOW));
        }

        [Test]
        public void AFightingBot_FollowsItsLeader_SinceItsAggroWasCleared()
        {
            Assert.That(BotBrain.ResumeStateAfterRestart(eFSMStateType.AGGRO, true, false), Is.EqualTo(eFSMStateType.FOLLOW));
        }

        [Test]
        public void ABotToldToStay_StaysIdle()
        {
            Assert.That(BotBrain.ResumeStateAfterRestart(eFSMStateType.FOLLOW, true, true), Is.EqualTo(eFSMStateType.IDLE));
            Assert.That(BotBrain.ResumeStateAfterRestart(eFSMStateType.IDLE, true, true), Is.EqualTo(eFSMStateType.IDLE));
        }

        [Test]
        public void WithoutALiveLeader_ABotIsIdle()
        {
            // Solo gamebots: their own controller drives them; IDLE is the state a bot brain starts in.
            Assert.That(BotBrain.ResumeStateAfterRestart(eFSMStateType.FOLLOW, false, false), Is.EqualTo(eFSMStateType.IDLE));
            Assert.That(BotBrain.ResumeStateAfterRestart(eFSMStateType.AGGRO, false, false), Is.EqualTo(eFSMStateType.IDLE));
            Assert.That(BotBrain.ResumeStateAfterRestart(eFSMStateType.IDLE, false, false), Is.EqualTo(eFSMStateType.IDLE));
        }

        [Test]
        public void APassiveBot_StaysPassive()
        {
            Assert.That(BotBrain.ResumeStateAfterRestart(eFSMStateType.PASSIVE, true, false), Is.EqualTo(eFSMStateType.PASSIVE));
        }

        [Test]
        public void ABotBrain_HasNoWakingUpState_SoAStopLeavesItWithNone()
        {
            // The cause, pinned: if a WAKING_UP state is ever added to BotBrain, the restore above can be revisited.
            BotBrain brain = new();
            Assert.That(brain.FSM.GetState(eFSMStateType.WAKING_UP), Is.Null);
            brain.FSM.SetCurrentState(eFSMStateType.WAKING_UP);
            Assert.That(brain.FSM.GetCurrentState(), Is.Null);
        }
    }
}
