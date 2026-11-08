using System.Collections.Generic;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Boss lines written as stage directions, "(The dead wolf rises, stiff-legged, from its post.)", are shown as action
    /// text, not yelled aloud with their parentheses (owner PC soak, 2026-11-08: 974 such yells in a night).
    /// </summary>
    [TestFixture]
    public class UT_LoreStageDirections
    {
        [TestCase("(The dead wolf rises, stiff-legged, from its post.)", true)]
        [TestCase("  (leaves fall, gold, slow)  ", true)]
        [TestCase("Conquest is patience. I am very patient.", false)]
        [TestCase("You will not leave (alive).", false)]
        [TestCase("()", false)]
        [TestCase("", false)]
        [TestCase(null, false)]
        public void AStageDirectionIsALineWhollyInParentheses(string text, bool stage) =>
            Assert.That(LoreBossWatch.IsStageDirection(text), Is.EqualTo(stage));

        [TestCase("(leaves fall, gold, slow)", "Leaves fall, gold, slow.")]
        [TestCase("(The dead wolf rises, stiff-legged, from its post.)", "The dead wolf rises, stiff-legged, from its post.")]
        [TestCase("( Coimirceoir's eyes dull, still facing the sea )", "Coimirceoir's eyes dull, still facing the sea.")]
        [TestCase("(It howls!)", "It howls!")]
        public void AStageDirectionReadsAsActionText(string text, string shown) =>
            Assert.That(LoreBossWatch.StageText(text), Is.EqualTo(shown));

        [Test]
        public void SpeakYellsALineAndShowsAStageDirectionAsAction()
        {
            var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            var yells = new List<string>();
            var actions = new List<string>();
            var saved = LoreBossWatch.StageAction;
            try
            {
                LoreBossWatch.StageAction = (_, text) => actions.Add(text);
                LoreBossWatch.Speak(npc, "aggro", "Conquest is patience.", (_, text) => yells.Add(text));
                LoreBossWatch.Speak(npc, "death", "(the gold crown falls across the crag)", (_, text) => yells.Add(text));
            }
            finally
            {
                LoreBossWatch.StageAction = saved;
            }
            Assert.That(yells, Is.EqualTo(new[] { "Conquest is patience." }));
            Assert.That(actions, Is.EqualTo(new[] { "The gold crown falls across the crag." }));
        }
    }
}
