using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// 1.2.1: loot rows naming an item template the world lacks were one error line each at every start
    /// ("ItemTemplate: X is not found, it is referenced from loottemplate: Y"). Now one warning with a count and a few
    /// examples (LootMissingItems); each row is named at debug level.
    /// </summary>
    [TestFixture]
    public class UT_LootMissingItems
    {
        [Test]
        public void RowsAreSummedUpInOneLineWithAFewExamples()
        {
            var missing = new LootMissingItems("LootTemplate");
            for (int i = 0; i < 300; i++)
                Assert.That(missing.Add("mob" + i / 10, "old_item_" + i % 120), Is.True, "each row is new once: it is named at debug level");

            string line = missing.Summary();

            Assert.That(line, Does.StartWith("LOOT_ITEMS_MISSING source=LootTemplate rows=300 templates=30 items=120 "));
            Assert.That(line, Does.Contain("examples=[mob0:old_item_0, mob0:old_item_1, mob0:old_item_2, mob0:old_item_3, mob0:old_item_4]"));
            Assert.That(line, Does.Not.Contain("old_item_5,"), "a few examples, not the list");
        }

        [Test]
        public void ARowIsCountedOnce()
        {
            var missing = new LootMissingItems("LootOTD");
            Assert.That(missing.Add("Golestandt", "Old_Sword"), Is.True);
            Assert.That(missing.Add("golestandt", "old_sword"), Is.False, "the server matches names and ids without case");
            Assert.That(missing.Add("Xanxicar", "old_sword"), Is.True, "another monster's row");
            Assert.That(missing.Rows, Is.EqualTo(2));
            Assert.That(missing.Summary(), Does.StartWith("LOOT_ITEMS_MISSING source=LootOTD rows=2 templates=2 items=1 "));
        }

        [Test]
        public void NothingMissingSaysNothing()
        {
            Assert.That(new LootMissingItems("DropTemplateXItemTemplate").Summary(), Is.Null);
        }

        [Test]
        public void NullNamesDoNotThrow()
        {
            var missing = new LootMissingItems(null);
            Assert.That(missing.Add(null, null), Is.True);
            Assert.That(missing.Summary(), Does.StartWith("LOOT_ITEMS_MISSING source= rows=1 "));
        }
    }
}
