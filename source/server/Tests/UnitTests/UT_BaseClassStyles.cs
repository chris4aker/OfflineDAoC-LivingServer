using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Base classes (Fighter, Viking, Guardian ...) had no weapon styles before choosing their class at level 5, because
    /// the style table lists styles only for the classes they lead to (r/daoc report, 2026-10-30).
    /// </summary>
    [TestFixture]
    public class UT_BaseClassStyles
    {
        [Test]
        public void ABaseClassLeadsToTheClassesItCanChooseAtLevelFive()
        {
            int[] fighter = Specialization.ClassesLedToBy((int)eCharacterClass.Fighter);
            Assert.That(fighter, Does.Contain((int)eCharacterClass.Armsman));
            Assert.That(fighter, Does.Contain((int)eCharacterClass.Paladin));
            Assert.That(fighter, Does.Contain((int)eCharacterClass.Mercenary));
            Assert.That(fighter, Does.Not.Contain((int)eCharacterClass.Thane));

            int[] viking = Specialization.ClassesLedToBy((int)eCharacterClass.Viking);
            Assert.That(viking, Does.Contain((int)eCharacterClass.Thane));
            Assert.That(viking, Does.Contain((int)eCharacterClass.Warrior));

            Assert.That(Specialization.ClassesLedToBy((int)eCharacterClass.Guardian), Does.Contain((int)eCharacterClass.Hero));
            Assert.That(Specialization.ClassesLedToBy((int)eCharacterClass.Armsman), Is.Empty, "a chosen class leads nowhere");
        }

        [Test]
        public void ABaseClassGetsTheStylesItsClassesHaveInTheLineOnce()
        {
            const string line = "Slash";
            var table = new[]
            {
                Style(910001, 990001, (int)eCharacterClass.Armsman, line, "Shared Cut", 2),
                Style(910002, 990001, (int)eCharacterClass.Mercenary, line, "Shared Cut", 2),
                Style(910003, 990002, (int)eCharacterClass.Mercenary, line, "Mercenary Cut", 4),
                Style(910004, 990003, (int)eCharacterClass.Thane, line, "Midgard Cut", 2),
            };
            List<GS.Styles.Style> StylesOf(string key, int classId) =>
                table.Where(row => row.SpecKeyName == key && row.ClassId == classId).Select(row => new GS.Styles.Style(row, null)).ToList();

            var fighter = Specialization.BaseClassStyles(line, (int)eCharacterClass.Fighter, StylesOf);
            Assert.That(fighter.Select(style => style.Name), Is.EquivalentTo(new[] { "Shared Cut", "Mercenary Cut" }));

            Assert.That(Specialization.BaseClassStyles(line, (int)eCharacterClass.Armsman, StylesOf), Is.Empty,
                "a chosen class keeps its own list, unchanged");
            Assert.That(Specialization.BaseClassStyles(line, (int)eCharacterClass.Viking, StylesOf).Select(style => style.Name),
                Is.EquivalentTo(new[] { "Midgard Cut" }));
        }

        private static DbStyle Style(int styleId, int id, int classId, string line, string name, int specLevel) => new()
        {
            StyleID = styleId,
            ID = id,
            ClassId = classId,
            SpecKeyName = line,
            Name = name,
            SpecLevelRequirement = specLevel,
        };
    }
}
