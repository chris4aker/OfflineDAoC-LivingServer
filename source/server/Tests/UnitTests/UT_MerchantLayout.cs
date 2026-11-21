using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Row = DOL.GS.MerchantTradeItems.MerchantRow;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Merchant windows from world data with clashing rows (MerchantTradeItems.Layout; docs/LOG-ERROR-BACKLOG.md, 2026-10-27:
    /// 1,754 copies and 182 different items sharing a slot across 50 lists, logged as errors on every page view).
    /// </summary>
    [TestFixture]
    public class UT_MerchantLayout
    {
        private static Dictionary<int, string> Lay(out int copies, out int moved, out int dropped, params Row[] rows) =>
            MerchantTradeItems.Layout(rows, out copies, out moved, out dropped);

        [Test]
        public void AListWithoutClashesIsLaidOutAsWritten()
        {
            var layout = Lay(out int copies, out int moved, out int dropped,
                new Row(0, 0, "sword", "a"), new Row(0, 5, "shield", "b"), new Row(2, 29, "axe", "c"), new Row(1, 3, "sword", "d"));
            Assert.That(layout, Is.EquivalentTo(new Dictionary<int, string> { [0] = "sword", [5] = "shield", [89] = "axe", [33] = "sword" }));
            Assert.That((copies, moved, dropped), Is.EqualTo((0, 0, 0)), "An item listed twice on free slots stays twice.");
        }

        [Test]
        public void ACopyInTheSameSlotIsDropped()
        {
            var layout = Lay(out int copies, out int moved, out int dropped,
                new Row(0, 4, "staff", "a"), new Row(0, 4, "staff", "b"), new Row(0, 4, "staff", "c"));
            Assert.That(layout, Is.EquivalentTo(new Dictionary<int, string> { [4] = "staff" }));
            Assert.That((copies, moved, dropped), Is.EqualTo((2, 0, 0)));
        }

        [Test]
        public void ADifferentItemInATakenSlotMovesToTheFirstFreeSlotOfItsPage()
        {
            var layout = Lay(out int copies, out int moved, out int dropped,
                new Row(1, 0, "bow", "a"), new Row(1, 0, "arrows", "b"), new Row(1, 1, "quiver", "c"));
            Assert.That(layout[30], Is.EqualTo("bow"), "The first row by id keeps the slot.");
            Assert.That(layout[31], Is.EqualTo("quiver"));
            Assert.That(layout[32], Is.EqualTo("arrows"), "Moved to the first free slot from its own page.");
            Assert.That((copies, moved, dropped), Is.EqualTo((0, 1, 0)));
        }

        [Test]
        public void ADisplacedItemAlreadyShownElsewhereIsACopy()
        {
            var layout = Lay(out int copies, out int moved, out int dropped,
                new Row(0, 0, "helm", "a"), new Row(0, 1, "gloves", "b"), new Row(0, 1, "helm", "c"));
            Assert.That(layout, Is.EquivalentTo(new Dictionary<int, string> { [0] = "helm", [1] = "gloves" }));
            Assert.That((copies, moved, dropped), Is.EqualTo((1, 0, 0)));
        }

        [Test]
        public void TheLayoutDoesNotDependOnRowOrder()
        {
            Row[] rows = [new(0, 2, "a", "3"), new(0, 2, "b", "1"), new(0, 2, "c", "2"), new(4, 29, "d", "4"), new(4, 29, "e", "5")];
            var forward = Lay(out _, out _, out _, rows);
            var backward = Lay(out _, out _, out _, rows.Reverse().ToArray());
            Assert.That(backward, Is.EquivalentTo(forward));
            Assert.That(forward[2], Is.EqualTo("b"), "Row id 1 keeps the slot.");
        }

        [Test]
        public void AFullWindowWrapsToTheFirstPageAndThenHasNoRoom()
        {
            var rows = new List<Row>();
            for (int i = 0; i < 150; i++)
                rows.Add(new Row(i / 30, i % 30, "item" + i, "r" + i.ToString("000")));
            rows.Add(new Row(4, 29, "late", "z1"));
            var layout = MerchantTradeItems.Layout(rows, out int copies, out int moved, out int dropped);
            Assert.That(layout.Count, Is.EqualTo(150));
            Assert.That((copies, moved, dropped), Is.EqualTo((0, 0, 1)), "No free slot left anywhere.");

            rows.RemoveAt(10);   // slot 10 on page 0 is free now
            layout = MerchantTradeItems.Layout(rows, out _, out moved, out dropped);
            Assert.That(layout[10], Is.EqualTo("late"), "From page 4, the search wraps round to page 0.");
            Assert.That((moved, dropped), Is.EqualTo((1, 0)));
        }

        [Test]
        public void ARowOutsideTheWindowIsMovedIn()
        {
            var layout = Lay(out _, out int moved, out _, new Row(7, 3, "far", "a"), new Row(0, 40, "wide", "b"));
            Assert.That(layout.Values, Is.EquivalentTo(new[] { "far", "wide" }));
            Assert.That(layout.Keys.All(slot => slot is >= 0 and < 150), Is.True);
            Assert.That(moved, Is.EqualTo(2));
        }

        // 1.2.1: "Old merchant item templates missing at startup: about 760 error lines at every start from the stock world
        // data" (KNOWN-ISSUES 1.2). One warning with a count and a few examples; each row is named at debug level.

        [Test]
        public void MissingTemplatesAreSummedUpInOneLineWithAFewExamples()
        {
            var log = new MerchantTradeItems.MissingTemplateLog();
            for (int i = 0; i < 760; i++)
                Assert.That(log.Add("list" + i % 40, "old_item_" + i, now: 1_000), Is.True, "each row is new once: it is named at debug level");

            string line = log.Summary(now: 1_000, force: true);

            Assert.That(line, Does.StartWith("MERCHANT_ITEMS_MISSING rows=760 lists=40 this_run=760 "));
            Assert.That(line, Does.Contain("examples=[list0:old_item_0, list1:old_item_1, list2:old_item_2, list3:old_item_3, list4:old_item_4]"));
            Assert.That(line, Does.Not.Contain("old_item_5"), "a few examples, not the list");
            Assert.That(log.Summary(now: 9_000_000, force: true), Is.Null, "said once");
        }

        [Test]
        public void AMissingTemplateIsCountedOncePerRun()
        {
            var log = new MerchantTradeItems.MissingTemplateLog();
            Assert.That(log.Add("smith", "Old_Sword", now: 0), Is.True);
            Assert.That(log.Add("smith", "old_sword", now: 0), Is.False, "the list is read again every five minutes");
            Assert.That(log.Add("fletcher", "old_sword", now: 0), Is.True, "another list's row");
            Assert.That(log.Total, Is.EqualTo(2));
            Assert.That(log.Summary(now: 0, force: true), Does.StartWith("MERCHANT_ITEMS_MISSING rows=2 lists=2 this_run=2 "));
        }

        [Test]
        public void RowsFoundLaterWaitAndAreSummedUpTogether()
        {
            var log = new MerchantTradeItems.MissingTemplateLog();
            log.Add("smith", "a", now: 0);
            log.Summary(now: 0, force: true);

            const int quiet = MerchantTradeItems.MissingTemplateLog.QuietMilliseconds;
            log.Add("tailor", "b", now: 10_000);
            Assert.That(log.Summary(now: 10_000, force: false), Is.Null, "not a line per list as lists are read");
            log.Add("tailor", "c", now: 20_000);
            Assert.That(log.Summary(now: 10_000 + quiet - 1, force: false), Is.Null);

            string line = log.Summary(now: 10_000 + quiet, force: false);
            Assert.That(line, Does.StartWith("MERCHANT_ITEMS_MISSING rows=2 lists=1 this_run=3 examples=[tailor:b, tailor:c]"));
            Assert.That(log.Summary(now: 10_000 + 2 * quiet, force: false), Is.Null);
        }

        [Test]
        public void NothingMissingSaysNothing()
        {
            Assert.That(new MerchantTradeItems.MissingTemplateLog().Summary(now: 0, force: true), Is.Null);
        }
    }
}
