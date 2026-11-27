using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DOL.Events;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;
using DOL.UnitTests.QuestHarness;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// GamePlayer's active quest list after the port of OpenDAoC-Core 22197f1f6 and cdb4384c0 (#565): one lock over the
    /// list and its indexes, so the journal limit, the duplicate check, the free slot and the add are one step; a quest
    /// not in the list is never "removed" (it used to clear slot 0); the journal update moves a quest waiting past the
    /// 25th slot under the same lock. Players only: GameBots and companions are GameNPCs and hold no quests.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_QuestListLocking
    {
        private QuestHarness.QuestHarness _harness;
        private HarnessPlayer _player;
        private JournalPackets _packets;

        /// <summary>Records the journal packets (quest list, update, remove) the player's client gets.</summary>
        public class JournalPackets : DispatchProxy
        {
            public readonly List<string> Calls = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name.StartsWith("SendQuest", StringComparison.Ordinal))
                {
                    lock (Calls)
                        Calls.Add(method.Name + (args.Length > 0 && args[0] is byte index ? "(" + index + ")" : string.Empty));
                }

                return method.ReturnType != typeof(void) && method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
            }
        }

        private sealed class FakeDataQuest : DataQuest
        {
            private readonly int _id;
            public FakeDataQuest(int id) => _id = id;
            public override int ID => _id;
            public override string Name => "Data quest " + _id;
            public override void OnQuestAssigned(GamePlayer player) { }
            public override void Notify(DOLEvent e, object sender, EventArgs args) { }
        }

        private abstract class Scripted : AbstractQuest
        {
            public int Notified;
            public Action OnNotify;
            public override string Name => GetType().Name;
            public override int Step { get => 1; set { } }
            public override bool IsDoingQuest() => true;
            public override bool CheckQuestQualification(GamePlayer player) => true;
            public override void OnQuestAssigned(GamePlayer player) { }

            public override void Notify(DOLEvent e, object sender, EventArgs args)
            {
                Notified++;
                OnNotify?.Invoke();
            }
        }

        private sealed class ScriptedA : Scripted { }
        private sealed class ScriptedB : Scripted { }

        [SetUp]
        public void SetUp()
        {
            _harness = new QuestHarness.QuestHarness(new QuestWorld());
            _player = _harness.NewPlayer(QuestWalker.DefaultClass, 20);
            _packets = DispatchProxy.Create<IPacketLib, JournalPackets>() as JournalPackets;
            _player.Client.Out = (IPacketLib)(object)_packets;
        }

        [TearDown]
        public void TearDown()
        {
            _harness?.Dispose();
            _harness = null;
        }

        private void LoadActive(AbstractQuest quest, byte index) =>
            typeof(GamePlayer).GetMethod("SetActiveQuest", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(_player, new object[] { quest, index });

        [Test]
        public void AQuestTakesTheLowestFreeSlotAndAFreedSlotIsReused()
        {
            var a = new FakeDataQuest(1);
            var b = new FakeDataQuest(2);
            var c = new FakeDataQuest(3);
            Assert.That(_player.AddQuest(a) && _player.AddQuest(b) && _player.AddQuest(c), Is.True);
            Assert.That(_player.RemoveQuest(b, notifyPlayer: false), Is.True);
            var d = new FakeDataQuest(4);
            Assert.That(_player.AddQuest(d), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(_player.TryGetQuestIndex(a, out byte ia) ? ia : -1, Is.EqualTo(0));
                Assert.That(_player.TryGetQuestIndex(c, out byte ic) ? ic : -1, Is.EqualTo(2));
                Assert.That(_player.TryGetQuestIndex(d, out byte id) ? id : -1, Is.EqualTo(1), "the freed slot");
                Assert.That(_player.TryGetQuestIndex(b, out _), Is.False);
                Assert.That(_player.GetQuestByIndex(1), Is.SameAs(d));
                Assert.That(_player.ActiveQuestCount, Is.EqualTo(3));
            });
        }

        [Test]
        public void TheSameQuestIsNotTakenTwice()
        {
            Assert.Multiple(() =>
            {
                Assert.That(_player.AddQuest(new FakeDataQuest(20171)), Is.True);
                Assert.That(_player.AddQuest(new FakeDataQuest(20171)), Is.False, "a data quest by ID");
                Assert.That(_player.AddQuest(new FakeDataQuest(20435)), Is.True, "another data quest");
                Assert.That(_player.AddQuest(new ScriptedA()), Is.True);
                Assert.That(_player.AddQuest(new ScriptedA()), Is.False, "a scripted quest by class");
                Assert.That(_player.AddQuest(new ScriptedB()), Is.True);
                Assert.That(_player.ActiveQuestCount, Is.EqualTo(4));
            });
        }

        [Test]
        public void TheIndexesFindActiveQuestsByIdAndByClass()
        {
            var data = new FakeDataQuest(7);
            var scripted = new ScriptedA();
            _player.AddQuest(data);
            _player.AddQuest(scripted);

            Assert.Multiple(() =>
            {
                Assert.That(_player.IsDoingDataQuest(7), Is.True);
                Assert.That(_player.IsDoingDataQuest(8), Is.False);
                Assert.That(_player.IsDoingDataQuest(7, 0), Is.True, "a fake data quest is at step 0");
                Assert.That(_player.IsDoingDataQuest(7, 1), Is.False);
                Assert.That(_player.GetActiveDataQuest(7), Is.SameAs(data));
                Assert.That(_player.IsDoingQuest(typeof(ScriptedA)), Is.SameAs(scripted));
                Assert.That(_player.IsDoingQuest(typeof(ScriptedB)), Is.Null);
                Assert.That(_player.IsDoingQuest(new ScriptedA()), Is.SameAs(scripted));
                Assert.That(_player.IsDoingQuest(new FakeDataQuest(7)), Is.SameAs(data));
                Assert.That(_player.GetActiveDataQuests(), Is.EqualTo(new[] { data }));
            });

            _player.RemoveQuest(data, notifyPlayer: false);
            _player.RemoveQuest(scripted, notifyPlayer: false);
            Assert.Multiple(() =>
            {
                Assert.That(_player.IsDoingDataQuest(7), Is.False);
                Assert.That(_player.IsDoingQuest(typeof(ScriptedA)), Is.Null);
                Assert.That(_player.GetActiveQuests(), Is.Empty);
            });
        }

        // An older save can hold the same quest twice; when the indexed one goes, the other is still found.
        [Test]
        public void ASecondCopyFromAnOlderSaveKeepsTheIndexWhenTheFirstGoes()
        {
            var first = new FakeDataQuest(9);
            var second = new FakeDataQuest(9);
            LoadActive(first, 0);
            LoadActive(second, 1);

            Assert.That(_player.GetActiveDataQuest(9), Is.SameAs(first));
            _player.RemoveQuest(first, notifyPlayer: false);
            Assert.That(_player.GetActiveDataQuest(9), Is.SameAs(second));
        }

        [Test]
        public void TheJournalTakesTwentyFiveQuests()
        {
            for (int id = 1; id <= QuestJournal.Capacity; id++)
                Assert.That(_player.AddQuest(new FakeDataQuest(id)), Is.True, $"quest {id}");

            Assert.Multiple(() =>
            {
                Assert.That(_player.AddQuest(new FakeDataQuest(100)), Is.False);
                Assert.That(_player.ActiveQuestCount, Is.EqualTo(QuestJournal.Capacity));
            });
        }

        // The race 22197f1f6 removes: quest starts on many threads at once (kill credit, hand-ins, search) each passed the
        // duplicate check and the slot pick before any of them added.
        [Test]
        public void QuestStartsAtOnceNeverShareASlotOrPassTheLimit()
        {
            for (int round = 0; round < 20; round++)
            {
                foreach (AbstractQuest quest in _player.GetActiveQuests())
                    _player.RemoveQuest(quest, notifyPlayer: false);

                // 30 different quests, each offered 4 times at once.
                Parallel.For(0, 120, new ParallelOptions { MaxDegreeOfParallelism = 8 }, i => _player.AddQuest(new FakeDataQuest(1000 + i % 30)));

                List<AbstractQuest> active = _player.GetActiveQuests();
                var slots = active.Select(q => _player.TryGetQuestIndex(q, out byte index) ? index : -1).ToList();
                Assert.Multiple(() =>
                {
                    Assert.That(active, Has.Count.EqualTo(QuestJournal.Capacity), $"round {round}");
                    Assert.That(active.OfType<DataQuest>().Select(q => q.ID), Is.Unique, $"round {round}: one copy of each quest");
                    Assert.That(slots, Is.EquivalentTo(Enumerable.Range(0, QuestJournal.Capacity)), $"round {round}: one quest per slot");
                });
            }
        }

        [Test]
        public void RemovingAQuestThatIsNotActiveSendsNothing()
        {
            _player.AddQuest(new ScriptedA());
            _packets.Calls.Clear();

            Assert.Multiple(() =>
            {
                Assert.That(_player.RemoveQuest(new ScriptedB(), notifyPlayer: true), Is.False);
                Assert.That(_player.RemoveQuest(null, notifyPlayer: true), Is.False);
                Assert.That(_packets.Calls, Is.Empty, "before, a finish or abort of a quest not in the list cleared slot 0");
            });
        }

        [Test]
        public void RemovingAScriptedQuestClearsItsSlotAndADataQuestResendsTheList()
        {
            var a = new ScriptedA();
            var data = new FakeDataQuest(5);
            _player.AddQuest(new ScriptedB());
            _player.AddQuest(a);
            _player.AddQuest(data);
            _packets.Calls.Clear();

            _player.RemoveQuest(a, notifyPlayer: true);
            _player.RemoveQuest(data, notifyPlayer: true);

            Assert.That(_packets.Calls, Is.EqualTo(new[] { "SendQuestRemove(1)", "SendQuestListUpdate" }));
        }

        [Test]
        public void TheJournalUpdateMovesAQuestWaitingPastTheLastSlotIntoAFreeOne()
        {
            // An older save: 26 active quests, slot 3 free after one was abandoned.
            var quests = new List<AbstractQuest>();
            for (byte i = 0; i < 26; i++)
            {
                if (i == 3)
                    continue;
                var quest = new FakeDataQuest(200 + i);
                quests.Add(quest);
                LoadActive(quest, i);
            }

            var sent = new List<(AbstractQuest Quest, byte Index)>();
            _player.SendQuestListUpdate(0, QuestJournal.Capacity, (quest, index) => sent.Add((quest, index)));

            AbstractQuest waiting = quests.Single(q => ((DataQuest)q).ID == 225);
            Assert.Multiple(() =>
            {
                Assert.That(_player.TryGetQuestIndex(waiting, out byte moved) ? moved : -1, Is.EqualTo(3), "moved into the free slot");
                Assert.That(sent, Does.Contain((waiting, (byte)3)));
                Assert.That(sent.Where(s => s.Quest == null), Is.Empty, "every slot holds a quest");
                Assert.That(sent.Select(s => s.Index), Is.EquivalentTo(Enumerable.Range(0, QuestJournal.Capacity).Select(i => (byte)i)));
            });

            // The next quest gets a slot no quest holds (owner 2026-10-30: two quests on one slot showed as one).
            _player.RemoveQuest(quests[0], notifyPlayer: false);
            var next = new FakeDataQuest(999);
            Assert.That(_player.AddQuest(next), Is.True);
            Assert.That(_player.TryGetQuestIndex(next, out byte nextIndex) ? nextIndex : -1, Is.EqualTo(0));
        }

        [Test]
        public void TheJournalUpdateClearsEmptySlotsWithTheClientsOffset()
        {
            _player.AddQuest(new FakeDataQuest(1));
            var sent = new List<(AbstractQuest Quest, byte Index)>();
            _player.SendQuestListUpdate(1, QuestJournal.Capacity, (quest, index) => sent.Add((quest, index)));

            Assert.Multiple(() =>
            {
                Assert.That(sent, Has.Count.EqualTo(QuestJournal.Capacity));
                Assert.That(sent.Count(s => s.Quest != null), Is.EqualTo(1));
                Assert.That(sent.Single(s => s.Quest != null).Index, Is.EqualTo(1));
                Assert.That(sent.Select(s => (int)s.Index), Is.EquivalentTo(Enumerable.Range(1, QuestJournal.Capacity)));
            });
        }

        // A quest that finishes on a notify leaves the list while the player's notify walks it.
        [Test]
        public void AQuestThatLeavesTheListDuringANotifyDoesNotStopTheOthers()
        {
            var a = new ScriptedA();
            var b = new ScriptedB();
            _player.AddQuest(a);
            _player.AddQuest(b);
            a.OnNotify = () => _player.RemoveQuest(a, notifyPlayer: false);
            b.OnNotify = () => _player.RemoveQuest(b, notifyPlayer: false);

            Assert.DoesNotThrow(() => _player.Notify(new DOLEvent("UT_QuestListLocking.Notify"), _player, EventArgs.Empty));
            Assert.Multiple(() =>
            {
                Assert.That(a.Notified, Is.EqualTo(1));
                Assert.That(b.Notified, Is.EqualTo(1));
                Assert.That(_player.ActiveQuestCount, Is.Zero);
            });
        }

        [Test]
        public void AnyActiveQuestLooksWithoutHoldingTheList()
        {
            var a = new ScriptedA();
            _player.AddQuest(a);
            _player.AddQuest(new FakeDataQuest(3));

            Assert.Multiple(() =>
            {
                Assert.That(_player.AnyActiveQuest(q => q is DataQuest { ID: 3 }), Is.True);
                Assert.That(_player.AnyActiveQuest(q => q is DataQuest { ID: 4 }), Is.False);
                // The predicate may change the list: it runs on a snapshot.
                Assert.That(_player.AnyActiveQuest(q => q == a && _player.RemoveQuest(a, notifyPlayer: false)), Is.True);
                Assert.That(_player.ActiveQuestCount, Is.EqualTo(1));
            });
        }
    }
}
