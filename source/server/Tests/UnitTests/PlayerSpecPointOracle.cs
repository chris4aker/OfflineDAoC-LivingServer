using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;

namespace DOL.UnitTests
{
    /// <summary>
    /// The player's own count of specialization points, for tests that hold a bot to it (owner 2026-11-17: "companions and
    /// bots should be the same as a player"). <see cref="PointsLeft"/> is GamePlayer.VerifySpecPoints itself, run on a
    /// player of the class and level with the given trained lines: every point earned (levels 2-5, each level above 5,
    /// each half level above 40) less what the lines cost, with the class's autotrain lines free up to a quarter of the
    /// level (GamePlayer.GetAutoTrainPoints). Nothing here restates the formula.
    /// </summary>
    internal static class PlayerSpecPointOracle
    {
        private sealed class OraclePlayer : GamePlayer
        {
            private OraclePlayer() : base(null, null) { }
            public byte TestLevel;
            public bool TestSecondStage;
            public ICharacterClass TestClass;
            public List<Specialization> TestSpecs;
            public override byte Level { get => TestLevel; set { } }
            public override bool IsLevelSecondStage { get => TestSecondStage; set { } }
            public override ICharacterClass CharacterClass => TestClass;
            public override IList<Specialization> GetSpecList() => TestSpecs;
        }

        // Not a plain player account: VerifySpecPoints then reports an overspent total instead of respeccing it.
        private static readonly Lazy<GameClient> Client = new(() =>
        {
            var client = new BotDummyClient();
            client.Account.PrivLevel = (uint)ePrivLevel.GM;
            return client;
        });

        /// <summary>The points a player of the class and level has left to spend with these lines trained (negative when
        /// the lines cost more than the player has earned).</summary>
        public static int PointsLeft(ICharacterClass characterClass, int level, bool secondStage = false,
            IEnumerable<KeyValuePair<string, int>> lines = null)
        {
            var player = (OraclePlayer)RuntimeHelpers.GetUninitializedObject(typeof(OraclePlayer));
            player.TestClass = characterClass;
            player.TestLevel = (byte)level;
            player.TestSecondStage = secondStage;
            player.TestSpecs = (lines ?? []).Select(line => new Specialization(line.Key, line.Key, 0) { Level = line.Value }).ToList();
            typeof(GamePlayer).GetField("m_client", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, Client.Value);
            return player.VerifySpecPoints();
        }

        /// <summary>The points a player of the class has earned at the level (no line trained).</summary>
        public static int Earned(ICharacterClass characterClass, int level, bool secondStage = false) =>
            PointsLeft(characterClass, level, secondStage);

        /// <summary>The bot's trainable lines and their levels now.</summary>
        public static IEnumerable<KeyValuePair<string, int>> Lines(GameBot bot) =>
            bot.GetSpecList().Where(spec => spec.Trainable).Select(spec => new KeyValuePair<string, int>(spec.KeyName, spec.Level));

        public static ICharacterClass ClassOf(int classId) =>
            ScriptMgr.FindCharacterClass(classId) ?? throw new InvalidOperationException($"class {classId}");

        public static ICharacterClass ClassOf(eCharacterClass characterClass) => ClassOf((int)characterClass);
    }
}
