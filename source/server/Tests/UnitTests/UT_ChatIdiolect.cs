using System;
using System.Linq;
using NUnit.Framework;
using Habit = DOL.GS.AutonomousChatIdiolect.Habit;

namespace DOL.GS.Tests;

/// <summary>Beta 1.1.2, the style layer: per-bot typing habits, fixed from the id, on lowercase chat words only.</summary>
[TestFixture]
public class UT_ChatIdiolect
{
    private static long BotWith(Habit habit, Habit without = Habit.None) =>
        Enumerable.Range(1, 100_000).First(id => AutonomousChatIdiolect.HabitsOf(id).HasFlag(habit) && (AutonomousChatIdiolect.HabitsOf(id) & without) == 0);

    [Test]
    public void AboutAThirdOfBotsHaveAHabitAndLeetIsRare()
    {
        int[] ids = Enumerable.Range(1, 10_000).ToArray();
        double any = ids.Count(id => AutonomousChatIdiolect.HabitsOf(id) != Habit.None) / 10_000.0;
        double leet = ids.Count(id => AutonomousChatIdiolect.HabitsOf(id).HasFlag(Habit.Leet)) / 10_000.0;
        Assert.That(any, Is.InRange(0.25, 0.40));
        Assert.That(leet, Is.InRange(0.01, 0.05));
        Assert.That(AutonomousChatIdiolect.HabitsOf(42), Is.EqualTo(AutonomousChatIdiolect.HabitsOf(42)), "fixed per bot");
    }

    [Test]
    public void HabitsChangeChatWordsButNotNamesOrTidyWriters()
    {
        long shortWords = BotWith(Habit.ShortWords, Habit.Leet | Habit.LolTail | Habit.TrailOff | Habit.DoubleQuestion);
        Assert.That(AutonomousChatIdiolect.Apply("are you going to Camelot with your guild", shortWords, false, new Random(1)),
            Does.Contain(" u ").And.Contain(" ur ").And.Contain("Camelot"));
        Assert.That(AutonomousChatIdiolect.Apply("You Are welcome", shortWords, false, new Random(1)), Is.EqualTo("You Are welcome"), "capitalised words stay");
        Assert.That(AutonomousChatIdiolect.Apply("are you there", shortWords, true, new Random(1)), Is.EqualTo("are you there"), "a tidy writer is left alone");
        long leet = BotWith(Habit.Leet);
        string typed = string.Join(" | ", Enumerable.Range(0, 20).Select(seed => AutonomousChatIdiolect.Apply("anyone want to see the dragon later", leet, false, new Random(seed))));
        Assert.That(typed, Does.Contain("ne1").And.Contain("l8r"));
        long questions = BotWith(Habit.DoubleQuestion, Habit.ShortWords);
        Assert.That(Enumerable.Range(0, 20).Any(seed => AutonomousChatIdiolect.Apply("where at?", questions, false, new Random(seed)) == "where at??"));
        Assert.That(AutonomousChatIdiolect.Apply("lol", BotWith(Habit.LolTail), false, new Random(1)), Is.EqualTo("lol"), "a short reaction stays");
    }
}
