using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PropertyCalc;
using DOL.Logging;
using NUnit.Framework;

namespace DOL.UnitTests
{
    [TestFixture, NonParallelizable]
    public sealed class UT_JailerTunnelGuardians
    {
        private static readonly IPropertyCalculator[] Calculators = (IPropertyCalculator[])typeof(GameLiving)
            .GetField("m_propertyCalc", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

        private GameServer _oldServer;
        private IPropertyCalculator _oldSpeedCalculator;
        private long _oldTime;
        private Region _region;
        private Jailer _jailer;
        private JailerBrain _brain;

        private sealed class TestServer : GameServer
        {
            protected override IObjectDatabase DataBaseImpl =>
                DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();

            public void InitializeNpcManager()
            {
                m_database = DataBaseImpl;
                NpcManager = new NpcManager(this);
            }
        }

        [OneTimeSetUp]
        public void InitializeLogging() => LoggerManager.InitializeWithExplicitLibrary(null, LogLibrary.None);

        [SetUp]
        public void SetUp()
        {
            _oldServer = GameServer.Instance;
            _oldTime = GameLoop.GameLoopTime;
            _oldSpeedCalculator = Calculators[(int)eProperty.MaxSpeed];
            Calculators[(int)eProperty.MaxSpeed] = new MaxSpeedCalculator();
            var server = (TestServer)RuntimeHelpers.GetUninitializedObject(typeof(TestServer));
            GameServer.LoadTestDouble(server);
            server.InitializeNpcManager();
            _region = new Region(new RegionData { Id = 160, Name = "Glacier", Description = "Glacier", Mobs = [] });
            _region.Zones.Add(new Zone(_region, 160, "Glacier", 8192, 8192, 65536, 65536, 160, false, 0, false, 0, 0, 0, 0, 0));
            _jailer = new Jailer { CurrentRegion = _region, X = 27074, Y = 61280, Z = 10351 };
            AddJailerWithNewBrain();
        }

        [TearDown]
        public void TearDown()
        {
            _jailer?.Delete();
            foreach (JailerAdd guardian in Guardians())
                guardian.Delete();

            Calculators[(int)eProperty.MaxSpeed] = _oldSpeedCalculator;
            GameServer.LoadTestDouble(_oldServer);
            SetTime(_oldTime);
        }

        [Test]
        public void PullWaitsForFightingGuardiansAndCleansUpAfterTheOldResetWindow()
        {
            PullAt(100_000);
            JailerAdd[] pair = Guardians();
            Assert.That(pair.Select(add => (add.X, add.Y, add.Z)),
                Is.EquivalentTo(new[] { (16709, 58973, 10879), (16379, 58954, 10885) }));
            Assert.That(pair.Select(add => add.RespawnInterval), Is.All.EqualTo(-1));

            pair[0].LastAttackedByEnemyTickPvE = 132_000;
            SetTime(132_000);
            _brain.Think();
            Assert.That(Guardians(), Is.EquivalentTo(pair), "A live guardian fight keeps the pair in play.");

            PullAt(133_000);
            Assert.That(Guardians(), Is.EquivalentTo(pair), "Re-engaging Vifil cannot stack a second pair.");

            SetTime(175_000);
            _brain.Think();
            Assert.That(Guardians(), Is.Empty);
            PullAt(176_000);
            Assert.That(Guardians(), Has.Length.EqualTo(2), "A complete reset rearms both spawns.");
        }

        [Test]
        public void SimultaneousOpeningHitsSpawnOnePair()
        {
            SetTime(100_000);
            _jailer.LastAttackedByEnemyTickPvE = 100_000;
            Parallel.For(0, 16, _ => _brain.OnAttackedByEnemy(new AttackData()));
            Assert.That(Guardians(), Has.Length.EqualTo(2));
        }

        [Test]
        public void BossRemovalClearsOnlyItsOwnGuardiansBeforeTheNextSpawn()
        {
            JailerAdd other = new() { CurrentRegion = _region, X = 17000, Y = 58950, Z = 10879 };
            Assert.That(other.AddToWorld(), Is.True);
            PullAt(100_000);
            Assert.That(Guardians(), Has.Length.EqualTo(3));

            _jailer.Delete();
            Assert.That(Guardians(), Is.EqualTo(new[] { other }));

            AddJailerWithNewBrain();
            PullAt(101_000);
            Assert.That(Guardians(), Has.Length.EqualTo(3));
        }

        private void AddJailerWithNewBrain()
        {
            _brain = new JailerBrain();
            _jailer.SetOwnBrain(_brain);
            // Register the real NPC without loading or saving the boss template from the world database.
            var add = typeof(Region).GetMethod("AddObject", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(add.Invoke(_region, new object[] { _jailer }), Is.True);
            _jailer.ObjectState = GameObject.eObjectState.Active;
            _jailer.Health = _jailer.MaxHealth;
        }

        private void PullAt(long time)
        {
            SetTime(time);
            _jailer.LastAttackedByEnemyTickPvE = time;
            _brain.OnAttackedByEnemy(new AttackData());
        }

        private JailerAdd[] Guardians()
        {
            return (_region?.Objects ?? []).OfType<JailerAdd>()
                .Where(add => add.ObjectState == GameObject.eObjectState.Active).ToArray();
        }

        private static void SetTime(long time)
        {
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime)).SetValue(null, time);
        }
    }
}
