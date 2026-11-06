using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 2026-11-04 23:48:17 on the owner's test copy: a NullReferenceException in NpcService at
    /// ControlledMobBrain.CheckAttackOrderTarget (an Animist turret: TurretBrain.CalculateNextAttackTarget via
    /// AttackMostWanted), and the server removed the turret. The order target was cleared between the null check and its
    /// use. The check now reads it once, and a pet that has left the world has no order target.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_PetOrderTargetGuard
    {
        private sealed class Rules : NormalServerRules
        {
            public override bool IsAllowedToAttack(GameLiving a, GameLiving b, bool quiet) => true;
        }

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
            protected override IServerRules ServerRulesImpl => new Rules();
        }

        private GameServer _previous;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void TearDown() => GameServer.LoadTestDouble(_previous);

        private sealed class Brain : ControlledMobBrain
        {
            public Brain(GameLiving owner) : base(owner) { }
            public GameLiving Check() => CheckAttackOrderTarget();
        }

        private sealed class Npc : GameNPC
        {
            public Brain ClearsOnLook;
            public override bool IsAlive
            {
                get
                {
                    // Another thread clears the order (Disengage) while this one is checking it.
                    if (ClearsOnLook != null) ClearsOnLook.OrderedAttackTarget = null;
                    return true;
                }
            }
        }

        private static Npc Actor(GameObject.eObjectState state = GameObject.eObjectState.Active)
        {
            var npc = (Npc)RuntimeHelpers.GetUninitializedObject(typeof(Npc));
            npc.ObjectState = state;
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(npc, new ArrayList());
            return npc;
        }

        [Test]
        public void AnOrderClearedWhileItIsCheckedDoesNotThrow()
        {
            var brain = new Brain(Actor()) { Body = Actor() };
            // Gone from the world, so the check stops before the server rules: the old code read the field again here.
            Npc target = Actor(GameObject.eObjectState.Deleted);
            target.ClearsOnLook = brain;
            brain.OrderedAttackTarget = target;
            Assert.That(() => brain.Check(), Throws.Nothing);
            Assert.That(brain.OrderedAttackTarget, Is.Null);
        }

        [Test]
        public void APetThatHasLeftTheWorldHasNoOrderTarget()
        {
            var noBody = new Brain(Actor()) { OrderedAttackTarget = Actor() };
            Assert.That(noBody.Check(), Is.Null, "no body at all");

            var removed = new Brain(Actor()) { Body = Actor(GameObject.eObjectState.Deleted), OrderedAttackTarget = Actor() };
            Assert.That(removed.Check(), Is.Null, "the server removed the turret");
            Assert.That(removed.OrderedAttackTarget, Is.Null);
        }

        [Test]
        public void ATargetThatLeftTheWorldIsDropped()
        {
            var brain = new Brain(Actor()) { Body = Actor(), OrderedAttackTarget = Actor(GameObject.eObjectState.Inactive) };
            Assert.That(brain.Check(), Is.Null);
            Assert.That(brain.OrderedAttackTarget, Is.Null);
            Assert.That(new Brain(Actor()) { Body = Actor() }.Check(), Is.Null, "no order: nothing");
            Npc kept = Actor();
            Assert.That(new Brain(Actor()) { Body = Actor(), OrderedAttackTarget = kept }.Check(), Is.SameAs(kept), "a valid order stays");
        }
    }
}
