// Hand port from shadowofze/OfflineDAoC-True165 d2187a498 (UT_CompanionFlank165, by shadowofze, GPL-3.0), adapted to
// Living Server's gamebot-only rule: the companion "Positioning" option tests are dropped, raid/epic, player-led and
// reachability rules are added.
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.Styles;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Positional styles, step 2 (1.2.1): a damage-dealing gamebot steps behind or beside a monster that fights someone
    /// else, and tries again when the monster turned or moved.
    /// </summary>
    [TestFixture]
    public sealed class UT_BotFlank
    {
        [Test]
        public void OnlyADamageDealingGamebotOutsideRaidsAndPlayerGroupsFlanks() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.FlankAllowed(true, false, false, false), Is.True, "a gamebot damage dealer flanks");
            Assert.That(BotBrain.FlankAllowed(true, false, true, false), Is.False, "a gamebot tank keeps facing its monster");
            Assert.That(BotBrain.FlankAllowed(false, false, false, false), Is.False, "a companion never flanks");
            Assert.That(BotBrain.FlankAllowed(true, true, false, false), Is.False, "nor a bot in a player-led group");
            Assert.That(BotBrain.FlankAllowed(true, false, false, true), Is.False, "raids and epic encounters keep their tactics");
        });

        [Test]
        public void ATurnedOrMovedMonsterIsFlankedAgainAtMostEveryThreeSeconds() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds - 1, 0, 2048, 500), Is.False, "too soon");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 0, 0), Is.False, "it stood still");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 512, 0), Is.False, "45 degrees exactly");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 513, 0), Is.True, "turned past 45 degrees");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 4000, 600, 0), Is.True, "turned across north");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 4000, 100, 0), Is.False, "a small turn across north");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 0, 101), Is.True, "moved more than 100");
            Assert.That(BotBrain.FlankRetry(BotBrain.FlankRetryMilliseconds, 0, 0, 100), Is.False);
        });

        [Test]
        public void TheFlankWindowsAreStyleProcessorsWindows() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.InFlankPosition(180, Style.eOpeningPosition.Back), Is.True);
            Assert.That(BotBrain.InFlankPosition(150, Style.eOpeningPosition.Back), Is.False);
            Assert.That(BotBrain.InFlankPosition(90, Style.eOpeningPosition.Back), Is.False);
            Assert.That(BotBrain.InFlankPosition(90, Style.eOpeningPosition.Side), Is.True);
            Assert.That(BotBrain.InFlankPosition(270, Style.eOpeningPosition.Side), Is.True);
            Assert.That(BotBrain.InFlankPosition(180, Style.eOpeningPosition.Side), Is.False);
            Assert.That(BotBrain.InFlankPosition(0, Style.eOpeningPosition.Side), Is.False);
        });

        private static GameNPC Monster(int x, int y, ushort heading)
        {
            var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            // GameNPC's movement component hides GameLiving's, and turning reads the GameLiving one: set both.
            ((GameLiving)npc).movementComponent = npc.movementComponent = new NpcMovementComponent(npc);
            npc.X = x; npc.Y = y;
            npc.movementComponent.ForceUpdatePosition();
            npc.Heading = heading;
            return npc;
        }

        [TestCase((ushort)0, 30f)] [TestCase((ushort)0, 250f)] [TestCase((ushort)1000, 100f)]
        [TestCase((ushort)3000, 340f)] [TestCase((ushort)4000, 200f)]
        public void TheFlankSpotIsWhereTheStyleCanBeUsed(ushort heading, float botAngle)
        {
            GameNPC monster = Monster(10_000, 10_000, heading);
            Assert.That(monster.Heading, Is.EqualTo(heading));
            Point2D back = monster.GetPointFromHeading(
                BotBrain.FlankHeading(heading, botAngle, Style.eOpeningPosition.Back), BotBrain.FlankStandOff);
            Point2D side = monster.GetPointFromHeading(
                BotBrain.FlankHeading(heading, botAngle, Style.eOpeningPosition.Side), BotBrain.FlankStandOff);
            float sideAngle = monster.GetAngle(side);
            Assert.Multiple(() =>
            {
                Assert.That(BotBrain.InFlankPosition(monster.GetAngle(back), Style.eOpeningPosition.Back), Is.True);
                Assert.That(BotBrain.InFlankPosition(sideAngle, Style.eOpeningPosition.Side), Is.True);
                Assert.That(sideAngle > 180, Is.EqualTo(botAngle > 180), "the side the bot is already nearer to");
            });
        }

        [Test]
        public void AFlankSpotIsTakenOnlyWhenTheBotCanWalkThereOnItsFloor() => Assert.Multiple(() =>
        {
            var spot = new Vector3(1000, 1000, 500);
            Assert.That(BotBrain.FlankSpotReached(spot, null, 500), Is.False, "no navigation mesh: don't move");
            Assert.That(BotBrain.FlankSpotReached(spot, new Vector3(1010, 1010, 505), 500), Is.True);
            Assert.That(BotBrain.FlankSpotReached(spot, new Vector3(1030, 1000, 500), 500), Is.False, "a wall stops the walk");
            Assert.That(BotBrain.FlankSpotReached(spot, new Vector3(1000, 1000, 700), 500), Is.False, "another floor");
        });

        private static Style Make(int id, int openingType = 0, int openingValue = 0, bool stealth = false) =>
            new(new DbStyle { Name = $"s{id}", ID = id, SpecKeyName = "Sword", SpecLevelRequirement = 10, WeaponTypeRequirement = 3,
                OpeningRequirementType = openingType, OpeningRequirementValue = openingValue, StealthRequirement = stealth,
                GrowthRate = 1 }, null);

        [Test]
        public void OnlyBackAndSideStylesThatAreNotTauntsOrStealthOpenersCount()
        {
            int positional = (int)Style.eOpening.Positional;
            Style taunt = Make(5, positional, (int)Style.eOpeningPosition.Back);
            taunt.Procs.Add(new StyleProcInfo(new Spell(new DbSpell { SpellID = BotMeleeStylePolicy.TauntSpellId, Name = "taunt",
                Type = "StyleTaunt", Target = "Enemy" }, 1), 0, 100));
            Assert.Multiple(() =>
            {
                Assert.That(BotBrain.IsFlankStyle(Make(1, positional, (int)Style.eOpeningPosition.Back)), Is.True);
                Assert.That(BotBrain.IsFlankStyle(Make(2, positional, (int)Style.eOpeningPosition.Side)), Is.True);
                Assert.That(BotBrain.IsFlankStyle(Make(3, positional, (int)Style.eOpeningPosition.Front)), Is.False);
                Assert.That(BotBrain.IsFlankStyle(Make(4, positional, (int)Style.eOpeningPosition.Back, stealth: true)), Is.False);
                Assert.That(BotBrain.IsFlankStyle(taunt), Is.False);
                Assert.That(BotBrain.IsFlankStyle(Make(6)), Is.False);
                Assert.That(BotBrain.IsFlankStyle(null), Is.False);
            });
        }
    }
}
