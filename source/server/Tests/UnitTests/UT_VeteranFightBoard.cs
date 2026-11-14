using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Veteran review 2026-11-11, M5: one shared fight board per group, rebuilt at most once a second, with one area
    /// search per group at most every 2 s, instead of every member's own area searches.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_VeteranFightBoard
    {
        [Test]
        public void TheFightBoardIsRebuiltAtMostOnceASecond() => Assert.Multiple(() =>
        {
            Assert.That(GroupFightBoard.RebuildMilliseconds, Is.EqualTo(1_000));
            Assert.That(GroupFightBoard.NeedsRebuild(1_999, 1_000, true), Is.False);
            Assert.That(GroupFightBoard.NeedsRebuild(2_000, 1_000, true), Is.True);
            Assert.That(GroupFightBoard.NeedsRebuild(1_100, 1_000, false), Is.True, "another region");
            Assert.That(new GroupFightBoard { BuiltAt = 1_000, RegionId = 51 }.IsFresh(1_500, 51), Is.True);
            Assert.That(new GroupFightBoard { BuiltAt = 1_000, RegionId = 51 }.IsFresh(2_000, 51), Is.False);
            Assert.That(new GroupFightBoard { BuiltAt = 1_000, RegionId = 51 }.IsFresh(1_500, 52), Is.False);
        });

        [Test]
        public void OneAreaSearchPerGroupAtMostEveryTwoSeconds() => Assert.Multiple(() =>
        {
            Assert.That(GroupFightBoard.ScanRadius, Is.LessThanOrEqualTo(2_600), "never wider than the old protection search");
            Assert.That(GroupFightBoard.ScanIntervalMilliseconds, Is.EqualTo(1_000));
            Assert.That(GroupFightBoard.ScanIntervalMilliseconds, Is.GreaterThanOrEqualTo(GroupFightBoard.RebuildMilliseconds));
        });

        private sealed class Npc : GameNPC
        {
            public bool Alive = true;
            public GameObject Target;
            public override bool IsAlive => Alive;
            public override GameObject TargetObject { get => Target; set => Target = value; }
            public ushort Region;
            public override ushort CurrentRegionID { get => Region; set => Region = value; }
        }

        [Test]
        public void AnNpcThatAggroesAfterTheAreaSearchIsOnTheNextBoard()
        {
            using var scope = new EpicTestServerScope();
            var monster = (Npc)RuntimeHelpers.GetUninitializedObject(typeof(Npc));
            monster.Alive = true;
            var member = (Npc)RuntimeHelpers.GetUninitializedObject(typeof(Npc));
            var members = new HashSet<GameLiving> { member };
            var pets = new HashSet<GameLiving>();
            Assert.Multiple(() =>
            {
                // at search time it has no target yet: it is still stored ...
                Assert.That(BotBrain.KeepInScan(monster), Is.True, "stored even before it targets anyone");
                Assert.That(BotBrain.ScannedNpcOnGroup(monster, members, pets), Is.False);
                // ... and the next board (built from the stored search) sees it once it runs at a member
                monster.Target = member;
                Assert.That(BotBrain.ScannedNpcOnGroup(monster, members, pets), Is.True);
                monster.Alive = false;
                Assert.That(BotBrain.KeepInScan(monster), Is.False, "the dead are not stored");
                Assert.That(BotBrain.KeepInScan(null), Is.False);
            });
        }

        [Test]
        public void ThePeekNeverRebuildsAndHandsOutBoardsUpToTwoSecondsOld() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.PeekFresh(3_000, 1_000), Is.True);
            Assert.That(BotBrain.PeekFresh(3_001, 1_000), Is.False);
            Assert.That(BotBrain.PeekFightBoard(null), Is.Null);
        });

        [Test]
        public void AnEmptyBoardHasNothingOnTheGroup()
        {
            var board = new GroupFightBoard();
            Assert.Multiple(() =>
            {
                Assert.That(board.MonstersOnGroup, Is.Zero);
                Assert.That(board.UncontrolledOnGroup, Is.Zero);
                Assert.That(board.NpcsOnGroup, Is.Empty);
                Assert.That(board.Threats, Is.Empty);
                Assert.That(board.ActiveTargets, Is.Empty);
                Assert.That(BotBrain.GetFightBoard(null), Is.Null);
            });
        }
    }
}
