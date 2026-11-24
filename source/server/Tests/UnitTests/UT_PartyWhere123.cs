using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Convo = DOL.GS.ChatConversations;
using K = DOL.GS.AutonomousChatKnowledge;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.3, from the owner's game on 1.2.2 (main 868a82d8), in a bot-led group:
/// <code>
/// [Party] Grayborn2003: "welcome! meet up at Humberton Castle"
/// [Party] Dumbledork: "where is that"
/// [Party] Gavillin: "not sure tbh"
/// [Party] Dumbledork: "where is Humberton Castle"
/// [Party] Windrider: "Dumbledork, stablemaster is your friend, saves a lot of running"
/// </code>
/// Humberton Castle is a town of the world (an area circle), not a place in the chat catalog, so nothing answered it: the
/// group's "dunno" rescue said "not sure tbh", and a remembered travel line stood in for an answer. Now the leader's place
/// is the place in play, "where is that" completes to it, a town is answered from the world before any advice, and a
/// question of where never draws a "dunno". Tests only.
/// </summary>
[TestFixture]
public class UT_PartyWhere123
{
    private static readonly K.NamedTown[] Towns =
    [
        new("Humberton Castle", "Black Mtns. South", 1, 504281, 474493),
        new("Humberton Village", "Black Mtns. South", 1, 508440, 477188),
        new("Campacorentin Station", "Campacorentin Forest", 1, 493703, 592523),
        new("Mularn", "Vale of Mularn", 100, 0, 0),
    ];

    [Test]
    public void WhereIsThatCompletesToThePlaceTheLeaderNamed()
    {
        var c = new Convo();
        const string group = "group:42";
        c.NoteBot("Dumbledork", 7, "Grayborn2003", group, "welcome! meet up at Humberton Castle", 0);
        c.NotePlace("Dumbledork", group, "Humberton Castle", 0);
        Assert.Multiple(() =>
        {
            Assert.That(Convo.Resolve("where is that", c.In("Dumbledork", group, 3_000)), Is.EqualTo("where is Humberton Castle"));
            Assert.That(Convo.Resolve("wheres that?", c.In("Dumbledork", group, 3_000)), Is.EqualTo("where is Humberton Castle?"));
            Assert.That(c.FloorHolder("Dumbledork", group, 3_000), Is.EqualTo(7), "the leader that named it has the floor");
        });
    }

    [Test]
    public void TheLeaderAnswersFirst()
    {
        long[] pool = [11, 12, 7, 13];
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupChatter.AnswererFirst(pool, 7, id => id), Is.EqualTo(new long[] { 7, 11, 12, 13 }));
            Assert.That(AutonomousBotGroupChatter.AnswererFirst(pool, 0, id => id), Is.EqualTo(pool), "nobody has the floor");
            Assert.That(AutonomousBotGroupChatter.AnswererFirst(pool, 99, id => id), Is.EqualTo(pool), "the floor is not in the group");
        });
    }

    [Test]
    public void TheLeadersPlaceLinesNameAPlace()
    {
        Assert.Multiple(() =>
        {
            Assert.That(BotLedGroups.NamesAPlace(BotLedGroups.Lead.Welcome), Is.True);
            Assert.That(BotLedGroups.NamesAPlace(BotLedGroups.Lead.WelcomeOnTheWay), Is.True);
            Assert.That(BotLedGroups.NamesAPlace(BotLedGroups.Lead.MeetupReminder), Is.True);
            Assert.That(BotLedGroups.NamesAPlace(BotLedGroups.Lead.AfkLetGo), Is.False);
        });
    }

    [Test]
    public void ATownIsFoundByItsWholeName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(K.TownNamed("where is Humberton Castle", Towns)?.Name, Is.EqualTo("Humberton Castle"));
            Assert.That(K.TownNamed("where is humberton village?", Towns)?.Name, Is.EqualTo("Humberton Village"));
            Assert.That(K.TownNamed("where is humberton", Towns), Is.Null, "half a name is not the town");
            Assert.That(K.TownNamed("where is camelot hills", Towns), Is.Null, "a zone is the catalog's");
        });
    }

    [TestCase("where is Humberton Castle", true)]
    [TestCase("where is that Humberton Castle?", true)]
    [TestCase("how do i get to humberton castle", true)]
    [TestCase("wheres humberton castle again?", true)]
    [TestCase("anyone know where humberton castle is", true)]
    [TestCase("where can i buy dyes in humberton castle", false)]
    [TestCase("whats a good camp near humberton castle", false)]
    [TestCase("humberton castle", false)]
    public void OnlyAQuestionOfTheWayIsATownQuestion(string line, bool asks) =>
        Assert.That(K.AsksOnlyWhereTownIs(line, "Humberton Castle"), Is.EqualTo(asks));

    [Test]
    public void TheWayIsACompassPoint()
    {
        Assert.Multiple(() =>
        {
            Assert.That(K.Compass(10_000, 0), Is.EqualTo("east"));
            Assert.That(K.Compass(0, -10_000), Is.EqualTo("north"), "the world's y runs south");
            Assert.That(K.Compass(-10_000, 10_000), Is.EqualTo("southwest"));
            Assert.That(K.Compass(500, 500), Is.Null, "close by has no way");
        });
    }

    [Test]
    public void TheAnswerSaysWhereAndNeverDunno()
    {
        var random = new Random(3);
        for (int i = 0; i < 200; i++)
        {
            foreach (K.Far far in Enum.GetValues<K.Far>())
                foreach (bool same in new[] { false, true })
                    foreach (string way in new[] { null, "west" })
                        foreach (bool horse in new[] { false, true })
                        {
                            string line = K.TownWhereLine("Humberton Castle", "Black Mtns. South", way, far, same, horse, random);
                            Assert.That(line, Does.Contain("Humberton Castle"));
                            Assert.That(line, Does.Not.Contain("not sure").And.Not.Contain("dunno").And.Not.Contain("{"));
                            if (!same && far is not (K.Far.Here or K.Far.Close))
                                Assert.That(line, Does.Contain("Black Mtns. South"), "from another zone it names the zone");
                            if (!horse)
                                Assert.That(line, Does.Not.Contain("stable"), "a horse only when a route goes there");
                        }
        }
    }

    [Test]
    public void ATownIsAnsweredBeforeTheAdvice()
    {
        var ports = new K.KnowledgePorts
        {
            Realm = eRealm.Albion, Level = 5, Random = new Random(1),
            Town = q => K.AsksOnlyWhereTownIs(q, "Humberton Castle") ? "Humberton Castle is in Black Mtns. South, west of u" : null,
        };
        Assert.That(K.TryAnswer(ports, "where is Humberton Castle", out string answer), Is.True);
        Assert.That(answer, Is.EqualTo("Humberton Castle is in Black Mtns. South, west of u"));
    }

    // #478: no question draws a "dunno" in any channel now, a question of where included.
    [TestCase("group", "where is that")]
    [TestCase("group", "where is Humberton Castle")]
    [TestCase("say", "how do i get to humberton castle")]
    [TestCase("group", "is it raining for you too?")]
    public void AQuestionOfWhereNeverDrawsADunno(string channel, string line) =>
        Assert.That(AutonomousChatReplyBudget.Rescue(new AutonomousChatReplyBudget.Moment(line, channel, false, AutonomousChatIntentModel.Predict(line), "Thane",
            false, false, false), "none factual", new Random(1)), Is.Null);

    [TestCase("Ardagh", "DOL.GS.Area+Circle", 1750, true)]
    [TestCase("Humberton Castle", "DOL.GS.Area+Circle", 2000, true)]
    [TestCase("thehiddenlair", "DOL.GS.Area+Circle", 2000, false)]
    [TestCase("Camelot bind", "DOL.GS.Area+Circle", 2000, false)]
    [TestCase("Humberton Castle", "DOL.GS.Area+Square", 2000, false)]
    [TestCase("Some Region Wide Area", "DOL.GS.Area+Circle", 60_000, false)]
    public void ATownIsANamedCircleOfTownSize(string description, string classType, int radius, bool town) =>
        Assert.That(K.IsTownArea(description, classType, radius), Is.EqualTo(town));

    [Test]
    public void NoRememberedLineStandsInForWhereAPlaceIs()
    {
        string lines = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, FindRepoFile("data/chat/memory.lines")));
        Assert.That(lines, Does.Not.Contain("stablemaster is your friend"));
        Assert.That(lines, Does.Not.Contain("someone else might know"));
    }

    private static string FindRepoFile(string tail)
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
        {
            string candidate = Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous", tail.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
        }
        Assert.Inconclusive("repository files not found from " + TestContext.CurrentContext.TestDirectory);
        return null;
    }
}
