using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>Quiet twisting: a bot's song restarts within 30 seconds play no cast music or start sound (owner 2026-10-30).</summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_BotQuietSongs
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }
        private GameServer _previous;

        [SetUp] public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown] public void Cleanup() => GameServer.LoadTestDouble(_previous);

        private static Spell Song(int id, string type = "HealthRegenBuff", string target = "Group") =>
            new(new DbSpell { SpellID = id, Type = type, Target = target, Pulse = 1, Frequency = 60, Duration = 6, CastTime = 3 }, 10);

        private static GameBot Bot() => (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));

        [Test]
        public void ARestartWithinThirtySecondsIsQuiet()
        {
            Assert.That(BotQuietSongs.IsRestart(0, 5_000), Is.False, "never started");
            Assert.That(BotQuietSongs.IsRestart(10_000, 17_000), Is.True, "the usual twist, seven seconds later");
            Assert.That(BotQuietSongs.IsRestart(10_000, 10_000 + BotQuietSongs.RestartWindowMs), Is.True);
            Assert.That(BotQuietSongs.IsRestart(10_000, 10_001 + BotQuietSongs.RestartWindowMs), Is.False, "after a break it's heard again");
        }

        [Test]
        public void ABotsFirstStartIsHeardAndItsTwistsAreQuiet()
        {
            GameBot bot = Bot();
            Spell heal = Song(990101), speed = Song(990102, "SpeedEnhancement");

            Assert.That(BotQuietSongs.IsRestart(bot, heal, 1_000), Is.False);
            Assert.That(BotQuietSongs.MarkStarted(bot, heal, 1_000), Is.False, "the first start plays its sound");
            Assert.That(BotQuietSongs.MarkStarted(bot, speed, 5_000), Is.False, "a different song's first start too");
            Assert.That(BotQuietSongs.IsRestart(bot, heal, 8_000), Is.True, "the cast back to the first song is quiet");
            Assert.That(BotQuietSongs.MarkStarted(bot, heal, 11_000), Is.True);
            Assert.That(BotQuietSongs.MarkStarted(bot, heal, 50_000), Is.False, "heard again after a break");
        }

        [Test]
        public void PlayersAndHarmfulSongsKeepTheirSounds()
        {
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            Spell heal = Song(990103);
            BotQuietSongs.MarkStarted(player, heal, 1_000);
            Assert.That(BotQuietSongs.MarkStarted(player, heal, 2_000), Is.False, "players' own twisting is untouched");

            GameBot bot = Bot();
            Spell mez = Song(990104, "Mesmerize", "Enemy");
            Assert.That(mez.IsHarmful, Is.True);
            BotQuietSongs.MarkStarted(bot, mez, 1_000);
            Assert.That(BotQuietSongs.MarkStarted(bot, mez, 2_000), Is.False, "a harmful pulse is never muted");
        }
    }
}
