using System;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Issue #397: "/hire train <n> <line>" read a line number only, so every line name answered "Choose a spec line.", and
    // the trainer window listed the unlocks still to train at the bottom of each line, below the window's edge.
    [TestFixture]
    public class UT_HireTrainCommand
    {
        private static readonly string[] Sorcerer = ["Body Magic", "Matter Magic", "Mind Twisting"];
        private static readonly string[] Cleric = ["Enhancement", "Rejuvenation", "Smite"];

        private static (bool Ok, int Line, int ToLevel) Parse(string[] lines, string typed)
        {
            bool ok = HireText165.ParseTrain(lines, typed.Split(' ', StringSplitOptions.RemoveEmptyEntries), out int line, out int toLevel);
            return (ok, line, toLevel);
        }

        [Test]
        public void ALineIsFoundByNameInAnyCase() => Assert.Multiple(() =>
        {
            Assert.That(Parse(Cleric, "Smite"), Is.EqualTo((true, 2, -1)));
            Assert.That(Parse(Cleric, "smite"), Is.EqualTo((true, 2, -1)));
            Assert.That(Parse(Cleric, "REJUVENATION"), Is.EqualTo((true, 1, -1)));
            Assert.That(Parse(Sorcerer, "Mind Twisting"), Is.EqualTo((true, 2, -1)), "a name of two words");
            Assert.That(Parse(Sorcerer, "mind  twisting"), Is.EqualTo((true, 2, -1)));
            Assert.That(Parse(Sorcerer, "mindtwisting"), Is.EqualTo((true, 2, -1)));
        });

        [Test]
        public void ALineIsFoundByItsBeginningOrAPartOnlyItHas() => Assert.Multiple(() =>
        {
            Assert.That(Parse(Cleric, "rej"), Is.EqualTo((true, 1, -1)));
            Assert.That(Parse(Sorcerer, "mind"), Is.EqualTo((true, 2, -1)));
            Assert.That(Parse(Sorcerer, "Mind Twist"), Is.EqualTo((true, 2, -1)));
            Assert.That(Parse(Sorcerer, "twisting"), Is.EqualTo((true, 2, -1)), "not its beginning, but no other line has it");
            Assert.That(Parse(Sorcerer, "body"), Is.EqualTo((true, 0, -1)));
            Assert.That(Parse(Sorcerer, "m").Ok, Is.False, "Matter Magic or Mind Twisting");
            Assert.That(Parse(Sorcerer, "magic").Ok, Is.False, "Body Magic or Matter Magic");
            Assert.That(Parse(Sorcerer, "m").Line, Is.EqualTo(-1));
            Assert.That(Parse(new[] { "Slash", "Slash Mastery" }, "slash"), Is.EqualTo((true, 0, -1)), "the whole name wins");
        });

        [Test]
        public void ALineIsFoundByItsNumber() => Assert.Multiple(() =>
        {
            Assert.That(Parse(Cleric, "1"), Is.EqualTo((true, 0, -1)));
            Assert.That(Parse(Cleric, "3"), Is.EqualTo((true, 2, -1)));
            Assert.That(Parse(Cleric, "0").Ok, Is.False);
            Assert.That(Parse(Cleric, "4").Ok, Is.False);
        });

        [Test]
        public void ANumberAfterTheLineIsTheLevelToTrainTo() => Assert.Multiple(() =>
        {
            Assert.That(Parse(Cleric, "smite 10"), Is.EqualTo((true, 2, 10)));
            Assert.That(Parse(Sorcerer, "Mind Twisting 12"), Is.EqualTo((true, 2, 12)));
            Assert.That(Parse(Sorcerer, "2 12"), Is.EqualTo((true, 1, 12)), "line 2 to level 12");
            Assert.That(Parse(Cleric, "smite 0").Ok, Is.False);
            Assert.That(Parse(Cleric, "smite -2").Ok, Is.False);
        });

        [Test]
        public void WhatIsNotALineIsNotGuessed() => Assert.Multiple(() =>
        {
            Assert.That(Parse(Cleric, "").Ok, Is.False);
            Assert.That(Parse(Cleric, "stealth").Ok, Is.False);
            Assert.That(Parse(Cleric, "stealth 5").Ok, Is.False);
            Assert.That(Parse(Cleric, "' '").Ok, Is.False);
            Assert.That(Parse([], "smite").Ok, Is.False);
            Assert.That(HireText165.ParseTrain(null, ["smite"], out _, out _), Is.False);
            Assert.That(HireText165.ParseTrain(Cleric, null, out _, out _), Is.False);
        });

        [Test]
        public void TheAnswerListsTheLinesAndTheWordsToType()
        {
            string help = HireText165.TrainHelp(2, "Isolde", Sorcerer);
            Assert.That(help, Does.Contain("1 Body Magic, 2 Matter Magic, 3 Mind Twisting"));
            Assert.That(help, Does.Contain("/hire train 2 <line name or number> [level]"));
            Assert.That(help, Does.Contain("/hire train 2 mind twisting 10"));
            // the example it gives is one the command reads
            Assert.That(Parse(Sorcerer, "mind twisting 10"), Is.EqualTo((true, 2, 10)));
            Assert.That(HireText165.TrainHelp(1, "Isolde", []), Is.EqualTo("Isolde has no spec lines to train."));
        }

        // the native trainer window: the next unlock to train is the second row of its line, never further down
        [Test]
        public void TheTrainerWindowStartsAtTheLastUnlockTrained() => Assert.Multiple(() =>
        {
            int[] levels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12];
            Assert.That(CompanionWindows165.FirstVisibleUnlock(levels, 1, 10), Is.EqualTo(0));
            Assert.That(CompanionWindows165.FirstVisibleUnlock(levels, 6, 10), Is.EqualTo(5), "6 trained: row 1 is 6, row 2 is 7");
            Assert.That(CompanionWindows165.FirstVisibleUnlock(levels, 10, 10), Is.EqualTo(9),
                "before #397 this was 2, with 11 and 12 the last two rows of ten");
            Assert.That(CompanionWindows165.FirstVisibleUnlock([1, 3, 5, 6, 8], 5, 10), Is.EqualTo(2), "a short list too");
            Assert.That(CompanionWindows165.FirstVisibleUnlock([3, 5], 1, 10), Is.EqualTo(0), "nothing trained yet");
            Assert.That(CompanionWindows165.FirstVisibleUnlock(levels, 50, 10), Is.EqualTo(2), "all trained: the last ten");
            Assert.That(CompanionWindows165.FirstVisibleUnlock([1, 2], 50, 10), Is.EqualTo(0));
            Assert.That(CompanionWindows165.FirstVisibleUnlock([], 5, 10), Is.EqualTo(0));
            Assert.That(CompanionWindows165.FirstVisibleUnlock(null, 5, 10), Is.EqualTo(0));
        });
    }
}
