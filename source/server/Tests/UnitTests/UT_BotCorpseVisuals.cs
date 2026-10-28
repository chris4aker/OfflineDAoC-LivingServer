using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>A bot's death is shown to the players around it (owner 2026-10-28).</summary>
    [TestFixture]
    public class UT_BotCorpseVisuals
    {
        [Test]
        public void ADeadBotLiesDownForANewViewerUnlessItIsBeingReleased()
        {
            Assert.That(BotCorpseVisuals.ShowsCorpseOnCreate(alive: false, active: true, movingCorpseForRevival: false), Is.True);
            Assert.That(BotCorpseVisuals.ShowsCorpseOnCreate(alive: true, active: true, movingCorpseForRevival: false), Is.False);
            Assert.That(BotCorpseVisuals.ShowsCorpseOnCreate(alive: false, active: true, movingCorpseForRevival: true), Is.False,
                "a released corpse is moved to its bind to be revived there, not laid down");
            Assert.That(BotCorpseVisuals.ShowsCorpseOnCreate(alive: false, active: false, movingCorpseForRevival: false), Is.False);
        }

        [Test]
        public void OnlyARealReturnToLifeStandsTheBotUp()
        {
            Assert.That(BotCorpseVisuals.CameBackToLife(0, 120), Is.True, "raised or released");
            Assert.That(BotCorpseVisuals.CameBackToLife(50, 120), Is.False, "a heal");
            Assert.That(BotCorpseVisuals.CameBackToLife(120, 0), Is.False, "a death");
            Assert.That(BotCorpseVisuals.CameBackToLife(0, 0), Is.False);
        }
    }
}
