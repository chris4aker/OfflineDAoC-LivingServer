using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// GameObject's static constructor reads the data quest cache from the database, so the lease tests run with an
    /// empty database behind a server test double, as the other object tests do.
    /// </summary>
    [NonParallelizable]
    public abstract class DutyLeaseFixture
    {
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => System.Reflection.DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        }

        private GameServer _previous;

        [SetUp]
        public void LoadServerDouble()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void RestoreServer() => GameServer.LoadTestDouble(_previous);
    }

    /// <summary>
    /// Gamebots, players and groups without a world, for the duty lease tests (docs/DUTY-LEASE.md). The objects are
    /// uninitialized, so only what a test sets is real: realm, level, life, objective, region, the player-led state.
    /// </summary>
    internal static class DutyLeaseTestKit
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        internal sealed class Bot : GameBot
        {
            private Bot() : base((OfflineWorldBotRecord)null) { }
            public bool Alive;
            public ushort Region;
            public int TestX;
            public override byte Level { get; set; }
            public override eRealm Realm { get; set; }
            public override bool IsAlive => Alive;
            public override int X => TestX;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => Region; set { } }
        }

        internal sealed class Player : GamePlayer
        {
            private Player() : base(null, null) { }
            public eRealm TestRealm;
            public override eRealm Realm { get => TestRealm; set { } }
            public override int X => 0;
            public override int Y => 0;
            public override int Z => 0;
            public override ushort CurrentRegionID { get => 1; set { } }
        }

        private static long _nextId = 7_310_000_000;

        /// <summary>A level-50 autonomous gamebot (alive, Albion, region 1) with a saved record and the given objective.</summary>
        internal static Bot Gamebot(byte level = 50, eRealm realm = eRealm.Albion, string objective = "SoloPve", bool autonomous = true)
        {
            Bot bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
            typeof(GameNPC).GetField("m_brains", Hidden)!.SetValue(bot, new ArrayList());
            typeof(GameLiving).GetField("<TempProperties>k__BackingField", Hidden)!.SetValue(bot, new PropertyCollection());
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
            typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord))!.SetValue(bot,
                new OfflineWorldBotRecord { ObjectiveKind = objective });
            bot.DatabaseID = Interlocked.Increment(ref _nextId);
            bot.Level = level;
            bot.Realm = realm;
            bot.Alive = true;
            bot.Region = 1;
            return bot;
        }

        /// <summary>A /spawn companion: a temporary helper, not an autonomous gamebot.</summary>
        internal static Bot Companion()
        {
            Bot bot = Gamebot(autonomous: false);
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, true);
            return bot;
        }

        /// <summary>A member of a realm's Epic Special Forces.</summary>
        internal static Bot SpecialForce()
        {
            Bot bot = Gamebot();
            typeof(GameBot).GetProperty(nameof(GameBot.IsEpicSpecialForce))!.SetValue(bot, true);
            return bot;
        }

        internal static Player RealPlayer(eRealm realm = eRealm.Albion)
        {
            Player player = (Player)RuntimeHelpers.GetUninitializedObject(typeof(Player));
            player.TestRealm = realm;
            return player;
        }

        /// <summary>A group object (no members list): enough for code that compares a bot's Group.</summary>
        internal static Group Token() => (Group)RuntimeHelpers.GetUninitializedObject(typeof(Group));

        /// <summary>Puts the gamebot in the player's group the way /invite does (the group, then the follow order).</summary>
        internal static void JoinPlayersGroup(Bot bot, Player player)
        {
            Group group = player.Group ?? Token();
            player.Group = group;
            bot.Group = group;
            if (!bot.EnterPlayerLedGroup(player))
                throw new System.InvalidOperationException("the bot did not enter the player's group");
        }
    }
}
