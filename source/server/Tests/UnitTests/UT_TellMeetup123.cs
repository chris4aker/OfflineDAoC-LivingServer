using System;
using NUnit.Framework;
using Meetup = DOL.GS.AutonomousChatMeetup;
using Path = DOL.GS.AutonomousChatReplyPath;
using ReplyChoice = DOL.GS.AutonomousBotChatCoordinator.ReplyChoice;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.3: a player's tell that says where they are (the owner's game, 2026-11-23). The player invited Eadwynn from too far, the
/// bot told them "in avalon marsh atm, inv me when ur close :)", the player answered "I'm by camp station", and the bot said
/// "lol": the tell read as a grinding remark ("camp"), no grinding reply fitted, and the short answer for a tell ended on a bare
/// laugh (AutonomousChatReplyBudget.ToContent). Pure functions and the reply path's ports only; nothing here was seen in game.
/// </summary>
[TestFixture]
public class UT_TellMeetup123
{
    private const string TooFar = "in avalon marsh atm, inv me when ur close :)";

    private sealed class FixedRandom(int pick) : Random
    {
        public override double NextDouble() => 0.0;
        public override int Next(int maxValue) => Math.Min(pick, maxValue - 1);
        public override int Next(int minValue, int maxValue) => Math.Min(minValue + pick, maxValue - 1);
    }

    [TestCase("I'm by camp station", "camp station")]
    [TestCase("im at the bridge now", "the bridge")]
    [TestCase("near cotswold", "cotswold")]
    [TestCase("in camelot hills", "camelot hills")]
    [TestCase("im in avalon marsh too", "avalon marsh")]
    [TestCase("Im here at camp forest", "camp forest")]
    [TestCase("i'm by the stable master in camelot", "the stable master in camelot")]
    public void APlaceThePlayerSaysIsRead(string message, string place) =>
        Assert.That(Meetup.PlaceSaid(message), Is.EqualTo(place));

    [TestCase("im in combat")]
    [TestCase("im on my way")]
    [TestCase("im in a group")]
    [TestCase("at least i tried")]
    [TestCase("in a sec")]
    [TestCase("im at 50%")]
    [TestCase("at work brb")]
    [TestCase("where r u?")]
    [TestCase("im on it")]
    [TestCase("lol")]
    public void AStateOrAQuestionIsNoPlace(string message) => Assert.That(Meetup.PlaceSaid(message), Is.Null);

    [TestCase("omw", true)]
    [TestCase("ok omw!", true)]
    [TestCase("on my way", true)]
    [TestCase("im on my way", true)]
    [TestCase("heading ur way", true)]
    [TestCase("coming!", true)]
    [TestCase("are you coming?", false)]
    [TestCase("im on it", false)]
    [TestCase("I'm by camp station", false)]
    public void SayingComingIsRead(string message, bool coming) => Assert.That(Meetup.SaysComing(message), Is.EqualTo(coming));

    [TestCase("im a cleric", true)]
    [TestCase("im 5", true)]
    [TestCase("my level is 5", true)]
    [TestCase("im in combat", false)]
    [TestCase("im fine", false)]
    [TestCase("im on it", false)]
    public void ALevelOrAClassAboutThemselvesIsRead(string message, bool self) =>
        Assert.That(Meetup.SaysAboutSelf(message), Is.EqualTo(self));

    [TestCase("lol", true)]
    [TestCase("heh", true)]
    [TestCase("haha lol", true)]
    [TestCase("lol yeah", false)]
    [TestCase("k", false)]
    [TestCase(null, false)]
    public void ABareLaughIsKnown(string reply, bool bare) => Assert.That(Meetup.IsBareLaugh(reply), Is.EqualTo(bare));

    [Test]
    public void TheScreenshotExchangeGetsWhereTheBotIsNotALaugh()
    {
        for (int pick = 0; pick < 3; pick++)
        {
            string reply = Meetup.Reply("I'm by camp station", "Avalon Marsh", sameZone: false, grouped: false, new FixedRandom(pick));
            Assert.Multiple(() =>
            {
                Assert.That(reply, Does.Contain("avalon marsh"), reply);
                Assert.That(reply, Does.Contain("camp station"), reply);
                Assert.That(Meetup.IsBareLaugh(reply), Is.False);
                Assert.That(ChatPromiseRules.SaysComing(reply), Is.False, "a free gamebot doesn't cross zones for a player, so it doesn't say it comes: " + reply);
            });
        }
    }

    [Test]
    public void ABotInThePlayersGroupSaysItIsComing()
    {
        string reply = Meetup.Reply("I'm by camp station", "Avalon Marsh", sameZone: false, grouped: true, new FixedRandom(0));
        Assert.That(reply, Is.EqualTo("k, coming to camp station"));
    }

    [Test]
    public void OmwGetsWhereToFindTheBot()
    {
        string reply = Meetup.Reply("omw", "Avalon Marsh", sameZone: false, grouped: false, new FixedRandom(0));
        Assert.That(reply, Is.EqualTo("cool, im in avalon marsh. inv when ur close"));
    }

    [Test]
    public void TheTellPathAsksTheMeetupBeforeTheCorpus()
    {
        var ports = new Path.Ports { Random = new FixedRandom(0), Style = text => text, Line = (_, _, _, _, _, _) => null };
        ReplyChoice choice = Path.Tell(ports, "I'm by camp station", eAutonomousChatIntent.Grinding,
            invited: () => null, join: () => null, where: () => null, friend: () => null, knowledge: () => null,
            voiced: () => new ReplyChoice("lol", "intent banter"),
            meetup: () => Meetup.Reply("I'm by camp station", "Avalon Marsh", false, false, new FixedRandom(0)));
        Assert.That(choice.Route, Is.EqualTo("live meetup"));
        Assert.That(choice.Text, Is.EqualTo("im still in avalon marsh, thats a ways from camp station. inv me when ur closer"));
    }

    [Test]
    public void ABareLaughToALineSaidToTheBotThatDoesntLaughIsReplaced()
    {
        var ports = new Path.Ports { Random = new FixedRandom(0), Style = text => text, Line = (_, _, _, _, _, _) => null };
        ReplyChoice choice = Path.Budget(ports, new ReplyChoice("lol", "intent banter"), "I'm by camp station", addressed: true, "tell");
        Assert.That(Meetup.IsBareLaugh(choice.Text), Is.False, choice.Text + " (" + choice.Route + ")");
    }

    [Test]
    public void ALaughToALineThatLaughsMayStay()
    {
        var ports = new Path.Ports { Random = new FixedRandom(0), Style = text => text, Line = (_, _, _, _, _, _) => null };
        ReplyChoice choice = Path.Budget(ports, new ReplyChoice("lol", "intent laugh"), "haha that mob ran off", addressed: true, "tell");
        Assert.That(choice.Route, Does.Not.StartWith("none laugh"));
    }

    [Test]
    public void TheShortAnswerAfterTheBotsTellIsAboutMeetingUp()
    {
        var moment = new AutonomousChatReplyBudget.Moment("I'm by camp station", "tell", true, eAutonomousChatIntent.Grinding, "Cleric", true, true,
            false, LastBotLine: TooFar);
        string text = AutonomousChatReplyBudget.ToContent(moment, new FixedRandom(0));
        Assert.That(text, Is.EqualTo("k, inv me when ur close"));
    }

    [TestCase("my level is 5")]
    [TestCase("im a cleric")]
    [TestCase("sounds like a plan")]
    [TestCase("thats the one by the river")]
    public void NoStatementSaidToTheBotGetsABareLaugh(string message)
    {
        for (int pick = 0; pick < 6; pick++)
        {
            var moment = new AutonomousChatReplyBudget.Moment(message, "tell", true, AutonomousChatIntentModel.Predict(message), "Cleric", true, true, false);
            string text = AutonomousChatReplyBudget.ToContent(moment, new FixedRandom(pick));
            Assert.That(Meetup.IsBareLaugh(text), Is.False, message + " -> " + text);
        }
    }
}
