using System.Linq;
using System.Reflection;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.1 (owner 2026-11-21: "Death animations need fixed absolutely, or PvP and RvR will look horrible"): a dead bot falls
    /// in the original client (BotDeathAnimation, bot_death_animation). The decisions only; the packets themselves are not
    /// sent here. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_BotDeathAnimation
    {
        private const ushort OriginalBuild = 0x072A; // any build but 0: PacketLib1125's "last seen 0x2A 0x07"

        [Test]
        public void TheClientsAreToldApartByTheHandshakeBuild()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotDeathAnimation.IsOriginalClient(true, 0), Is.False, "the new client sends build 0 (DaocClient.Open)");
                Assert.That(BotDeathAnimation.IsOriginalClient(true, OriginalBuild), Is.True, "the original game.dll sends its build");
                Assert.That(BotDeathAnimation.IsOriginalClient(false, OriginalBuild), Is.False, "no build read: not known, nothing new");
            });
        }

        [Test]
        public void OnlyAnOriginalClientThatIsPlayingAndSeesTheBotGetsTheFall()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotDeathAnimation.SendsFall(true, true, true, true), Is.True);
                Assert.That(BotDeathAnimation.SendsFall(true, false, true, true), Is.False, "the new client gets what it got before");
                Assert.That(BotDeathAnimation.SendsFall(true, true, false, true), Is.False, "zoning or logging in");
                Assert.That(BotDeathAnimation.SendsFall(true, true, true, false), Is.False, "a stealthed bot it can't see");
                Assert.That(BotDeathAnimation.SendsFall(false, true, true, true), Is.False, "setting off");
            });
        }

        [Test]
        public void WhileDeadTheOriginalClientGetsNothingAboutTheBot()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotDeathAnimation.Holds(true, true, true), Is.True, "no create, update, equipment or removal");
                Assert.That(BotDeathAnimation.Holds(true, true, false), Is.False, "the new client: unchanged");
                Assert.That(BotDeathAnimation.MarksAtDeath(true), Is.True,
                    "marked whoever is in range, so a player who comes into range later gets nothing either");
            });
        }

        [Test]
        public void TheFallGoesOutATickLaterOnlyIfTheBotIsStillHidden()
        {
            // The fall is sent on the tick after the death (BotDeathAnimation.OnDied): an update the death queued as UDP is
            // written after the tick's TCP packets for a client without UDP, and reached the original client after the fall
            // (playtester bot-death, CI 2026-11-22). Tests only.
            Assert.Multiple(() =>
            {
                Assert.That(BotDeathAnimation.FallStillDue(true, true, true), Is.True, "still dead and hidden: the fall goes out");
                Assert.That(BotDeathAnimation.FallStillDue(false, false, true), Is.False, "raised in between: no fall for a standing bot");
                Assert.That(BotDeathAnimation.FallStillDue(true, false, true), Is.False, "health again: no fall");
                Assert.That(BotDeathAnimation.FallStillDue(true, true, false), Is.False, "gone from the world (deleted, its owner quit)");
            });
        }

        [Test]
        public void ARaisedBotIsCreatedAgain()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotDeathAnimation.EndsAtRaise(true, true, true), Is.True, "health again: the clients forget it, to create it");
                Assert.That(BotDeathAnimation.Holds(true, false, true), Is.False, "alive: its create goes out (it stands up)");
                Assert.That(BotDeathAnimation.EndsAtRaise(true, false, true), Is.False, "a heal of a living bot is no raise");
                Assert.That(BotDeathAnimation.EndsAtRaise(true, true, false), Is.False, "still dead");
                Assert.That(BotDeathAnimation.EndsAtRaise(false, true, true), Is.False, "no fall was shown: nothing to undo");
            });
        }

        [Test]
        public void AReleaseSendsNothingAtTheDeathSpotAndTheCreateAtTheBind()
        {
            // The release moves the corpse first (still dead: its removal at the death spot is held back), then gives it
            // health (the hidden state ends) and shows it standing at the bind (GameBot.ShowStandingToViewers).
            Assert.Multiple(() =>
            {
                Assert.That(BotDeathAnimation.Holds(true, true, true), Is.True, "the move's removal and create while dead");
                Assert.That(BotDeathAnimation.EndsAtRaise(true, true, true), Is.True);
                Assert.That(BotDeathAnimation.Holds(false, false, true), Is.False, "the create at the bind goes out");
            });
        }

        [Test]
        public void WithTheSettingOffNothingChanges()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotDeathAnimation.MarksAtDeath(false), Is.False, "never marked");
                Assert.That(BotDeathAnimation.Holds(false, true, true), Is.False, "an unmarked dead bot: every packet as before");
                Assert.That(BotDeathAnimation.EndsAtRaise(false, true, true), Is.False);
                Assert.That(BotDeathAnimation.HidesFrom((GameObject) null, (GameClient) null), Is.False);
                Assert.That(BotDeathAnimation.Blocks(null, null, "NPCCreate"), Is.False, "not a bot: never held");
            });
        }

        [Test]
        public void TheSettingIsOnByDefaultAndNewSoNoUpgradeLine()
        {
            ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_DEATH_ANIMATION))
                .GetCustomAttribute<ServerPropertyAttribute>();
            Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", "bot_death_animation", (object) true)));
            Assert.That(LivingServerSettingsUpgrade.Changes.Select(change => change.Key), Does.Not.Contain("bot_death_animation"),
                "a new key: a world from an earlier build has no row and takes the code default");
        }
    }
}
