using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Convo = DOL.GS.ChatConversations;
using Ex = DOL.GS.AutonomousChatExchange;
using Know = DOL.GS.AutonomousChatKnowledge;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// The in-game chat check on the owner's PC, build 5154cae, 2026-11-19 07:04-07:27 (Testrunner, Albion 22, in Camelot;
/// scratchpad/SWEEP8-RESULTS-1119.md, "In-game check"). One test per problem the helper saw. Tests only: none of these
/// fixes has been seen on the owner's PC yet.
/// </summary>
[TestFixture]
public class UT_ChatPcCheck0719
{
    private static string LoreDir()
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null)
            Assert.Ignore("Repository not found beside the tests.");
        return Path.Combine(dir, "source", "server", "GameServer", "bots", "autonomous", "data", "lore");
    }

    /// <summary>Camelot Hills and Keltoi Fogou from the lore bible as committed.</summary>
    private static LoreBible Bible()
    {
        string dir = LoreDir();
        return LoreBible.Parse(new[] { "locations/camelot-hills.json", "locations/keltoi-fogou.json" }
            .Select(p => (p, File.ReadAllText(Path.Combine(dir, p)))));
    }

    // ---- 1. "is it far?" after a place was named -------------------------------------------------------------------------

    [Test]
    public void IsItFarAfterKeltoiFogouIsATravelQuestionNotTheDescription()
    {
        // 07:07:01 "where do i level at 22?" -> 07:07:13 Protette "Keltoi Fogou is good around 22 if u can get a group"
        // -> 07:08:25 "is it far?" -> 07:08:58 Ogria "Keltoi fogou? The keltoi holy cave in campacorentin. Priests, ...".
        const string room = "faction:1";
        var c = new Convo();
        c.NotePlayer("Testrunner", room, "where do i level at 22?", 0);
        c.NoteBot("Testrunner", 42, "Protette", room, "Keltoi Fogou is good around 22 if u can get a group", 12_000);
        string resolved = Convo.Resolve("is it far?", c.In("Testrunner", room, 84_000));
        Assert.That(resolved, Is.EqualTo("is keltoi fogou far?"), "the memory still completes \"it\"");

        Assert.That(Know.AsksHowFar(resolved), Is.True, "it asks how far, so the travel answer takes it");
        Assert.That(Bible().Subject(resolved), Is.EqualTo(((LoreBible.Location)null, (LoreBible.Boss)null)),
            "no lore answer (the place's description) for a how-far question");
        Assert.That(Bible().Subject("tell me about keltoi fogou").Location?.Id, Is.EqualTo("keltoi-fogou"), "a question about the place still is one");
    }

    [TestCase("is it far?", true)]
    [TestCase("is keltoi fogou far?", true)]
    [TestCase("how far is lyonesse", true)]
    [TestCase("how long to get to camelot hills?", true)]
    [TestCase("is it a long way", true)]
    [TestCase("where is keltoi fogou?", false)]
    [TestCase("how long does it take to hit 50?", false)]
    [TestCase("whats in keltoi fogou", false)]
    public void HowFarQuestionsAreKnown(string line, bool asks) => Assert.That(Know.AsksHowFar(line), Is.EqualTo(asks));

    [Test]
    public void TheHowFarAnswerSaysHowFarAndHowToGetThere()
    {
        Assert.That(Know.FarOf(true, 90_000), Is.EqualTo(Know.Far.Here));
        Assert.That(Know.FarOf(false, null), Is.EqualTo(Know.Far.Unknown));
        Assert.That(Know.FarOf(false, 8_000), Is.EqualTo(Know.Far.Close));
        Assert.That(Know.FarOf(false, 30_000), Is.EqualTo(Know.Far.NotFar));
        Assert.That(Know.FarOf(false, 70_000), Is.EqualTo(Know.Far.Trip));
        Assert.That(Know.FarOf(false, 200_000), Is.EqualTo(Know.Far.LongWay));

        var random = new Random(5);
        foreach (Know.Far far in Enum.GetValues<Know.Far>())
            for (int i = 0; i < 20; i++)
            {
                string withHorse = Know.HowFarLine("Keltoi Fogou", "Campacorentin", far, true, true, random);
                string onFoot = Know.HowFarLine("Keltoi Fogou", "Campacorentin", far, false, true, random);
                foreach (string line in new[] { withHorse, onFoot })
                {
                    Assert.That(line, Does.Contain("Campacorentin").Or.Contain("ur in").Or.Contain("ur standing"), line);
                    Assert.That(line, Does.Not.Contain("Priests").IgnoreCase.And.Not.Contain("holy cave").IgnoreCase, "not the description");
                    Assert.That(line.Length, Is.LessThanOrEqualTo(VC.MaxLength));
                }
                Assert.That(onFoot, Does.Not.Contain("horse"), "a horse only where a stable route really ends there");
                if (far is Know.Far.NotFar or Know.Far.Trip or Know.Far.LongWay or Know.Far.Unknown)
                    Assert.That(withHorse, Does.Contain("horse"));
            }
        Assert.That(Know.HowFarLine("Keltoi Fogou", "Campacorentin", Know.Far.LongWay, false, false, new Random(1)),
            Does.StartWith("long way").Or.StartWith("far lol"), "roughly how far comes first");
    }

    // ---- 2. a bot-to-bot answer right after the player's line ----------------------------------------------------------

    private static int _lane;

    private static string NewLane() => "test0719:" + System.Threading.Interlocked.Increment(ref _lane);

    private static List<(long At, Ex.Line Line)> Drive(Ex.Run run, string lane, Action<long> at = null, long until = 120_000)
    {
        var said = new List<(long, Ex.Line)>();
        for (long now = 0; now <= until && !run.Done; now += 100)
        {
            at?.Invoke(now);
            if (run.DueAt > now)
                continue;
            run.Step(now, _ => true, line => "Bot" + line.Who, (line, _) =>
            {
                said.Add((now, line));
                Ex.NoteLine(lane, "Bot" + line.Who, now);
                return true;
            }, new Random(1));
        }
        return said;
    }

    [Test]
    public void ABotThreadsAnswerWaitsWhileAPlayersLineHasTheFloor()
    {
        // 07:08:12 Grittath "how do i get to pennine mountains from here?"; 07:08:25 the player's "is it far?"; 07:08:31
        // Emberreaver's "pennine mountains, a horse gets you most of the way" read as the answer to the player.
        string lane = NewLane();
        var lines = new List<Ex.Line> { new(1, "pennine mountains, a horse gets you most of the way", 6_000, "answerer"), new(0, "cool", 3_000, "asker") };
        var run = new Ex.Run(1, "thread", lane, Ex.LaneOf(lane), lines, 0);
        Ex.HoldForPlayer(lane, 0, 20_000);   // the player's answer is due at 18 s
        var said = Drive(run, lane, now =>
        {
            if (now == 18_000)
            {
                Ex.NoteLine(lane, "Ogria", now);   // the player's answer
                Ex.ReleasePlayerFloor(lane);
            }
        });
        Assert.That(said.Select(s => s.Line.Text), Is.EqualTo(lines.Select(l => l.Text)), "the thread is still said, whole");
        Assert.That(said[0].At, Is.GreaterThanOrEqualTo(18_000), "after the player's answer, not 6 s after the player's line");
        Assert.That(said[1].At - said[0].At, Is.GreaterThanOrEqualTo(3_000));

        string free = NewLane();
        var again = new Ex.Run(2, "thread", free, Ex.LaneOf(free), lines, 0);
        Assert.That(Drive(again, free)[0].At, Is.LessThan(8_000), "with no player line it goes on time");
    }

    [Test]
    public void APlayersHoldIsCappedSoAThreadIsNeverHeldForGood()
    {
        string lane = NewLane();
        var lines = new List<Ex.Line> { new(1, "me", 2_000, "answerer") };
        var run = new Ex.Run(3, "thread", lane, Ex.LaneOf(lane), lines, 0);
        Ex.HoldForPlayer(lane, 0, 10 * 60_000);   // an answer that never came
        var said = Drive(run, lane);
        Assert.That(said.Count, Is.EqualTo(1));
        Assert.That(said[0].At, Is.LessThanOrEqualTo(Ex.PlayerFloorMaxMs + 2_000));
    }

    // ---- 3. a help request is not a lore question ---------------------------------------------------------------------

    [Test]
    public void AHelpRequestNamingAPlaceGetsNoLoreLine()
    {
        // 07:10:24 "can someone help me in Camelot Hills?" -> 07:10:57 Thaxard "fielath? the demon in the tallest broken keep ...".
        const string line = "can someone help me in Camelot Hills?";
        LoreBible bible = Bible();
        Assert.That(bible.Subject(line), Is.EqualTo(((LoreBible.Location)null, (LoreBible.Boss)null)));
        Assert.That(LoreChat.TryAnswer(bible, Array.Empty<VC.Line>(), line, new Random(1), out _, eRealm.Albion), Is.False);
        Assert.That(bible.Subject("whats in camelot hills?").Location?.Id, Is.EqualTo("camelot-hills"), "the lore question still is one");

        Assert.That(AutonomousChatShortTalk.QuestionShape(line), Is.EqualTo("ask_help"));
        Assert.That(AutonomousBotChatCoordinator.IsHelpCall(line, "ask_help"), Is.True, "it goes to the help path");
        Assert.That(AutonomousBotChatCoordinator.IsHelpCall("can you help?", "ask_help"), Is.False, "a plain ask is answered \"sure, whats up?\"");
        Assert.That(AutonomousBotChatCoordinator.IsHelpCall("need help with my spec", "ask_help"), Is.False, "a how-to keeps its answer");
        Assert.That(AutonomousBotChatCoordinator.IsHelpCall("can someone help me kill the named in keltoi fogou", "ask_help"), Is.True);
    }

    [Test]
    public void TheHelpPathOffersToComeOnlyThroughThePromiseGate()
    {
        var random = new Random(2);
        for (int i = 0; i < 40; i++)
        {
            (string come, string instead) = AutonomousBotChatCoordinator.HelpRequestLines(i % 2 == 0 ? "camelot hills" : null, random);
            Assert.That(ChatPromiseRules.SaysComing(come), Is.True, come);
            Assert.That(ChatPromiseRules.SaysComing(instead), Is.False, instead);
            // No bot that can go (here none at all): the gate gives the honest line, never "omw".
            Assert.That(ChatPromises.Gate(null, come, "camelot hills", ChatPromiseReason.Help, "can someone help me in Camelot Hills?", instead),
                Is.EqualTo(instead));
        }
    }

    // ---- 4. placed lore only from a bot the player hears --------------------------------------------------------------

    [Test]
    public void PlacedLoreComesOnlyFromABotInSayRange()
    {
        // 07:30:14 LORE_PLACED bot="Trorirrum" channel=Say, picked within 1,000 units; nothing was said.
        Assert.That(LoreChat.HearsPlacedLore(true, 400, false), Is.True);
        Assert.That(LoreChat.HearsPlacedLore(true, WorldMgr.SAY_DISTANCE, false), Is.True);
        Assert.That(LoreChat.HearsPlacedLore(true, 900, false), Is.False, "the old 1,000 radius took bots out of /say range");
        Assert.That(LoreChat.HearsPlacedLore(true, 100, true), Is.False, "not a bot on its way with an RvR party");
        Assert.That(LoreChat.HearsPlacedLore(false, 100, false), Is.False);
    }

    // ---- 5. another realm's zones ---------------------------------------------------------------------------------------

    [Test]
    public void AnotherRealmsZoneIsToldHonestly()
    {
        // 07:06:40 Furyarrow (Albion) to "how do i get to breifine": "new frontiers, a horse gets u most of the way".
        var breifine = new[] { ("Breifine", (ushort)200, false), ("Breifine", (ushort)163, false) };
        Assert.That(Know.OtherRealmZone(eRealm.Albion, "Breifine", breifine), Is.EqualTo(eRealm.Hibernia), "region 163's leftover zone doesn't make it ours");
        Assert.That(Know.OtherRealmZone(eRealm.Hibernia, "Breifine", breifine), Is.Null, "a Hibernian gets the route");
        Assert.That(Know.OtherRealmZone(eRealm.Albion, "Pennine Mountains", new[] { ("Pennine Mountains", (ushort)1, false) }), Is.Null);
        Assert.That(Know.OtherRealmZone(eRealm.Albion, "Darkness Falls", new[] { ("Darkness Falls", (ushort)249, false) }), Is.Null, "neutral land");
        Assert.That(Know.OtherRealmZone(eRealm.Albion, "Connacht", new[] { ("Connacht", (ushort)200, false) }), Is.EqualTo(eRealm.Hibernia));
        Assert.That(Know.OtherRealmZone(eRealm.Albion, "Breifine", new[] { ("Breifine", (ushort)200, true) }), Is.Null, "a monster is no zone");

        var random = new Random(3);
        for (int i = 0; i < 20; i++)
        {
            string frontier = Know.OtherRealmLine("Breifine", eRealm.Hibernia, AutonomousChatLiveAnswers.FrontierHome("Breifine") == eRealm.Hibernia, random);
            Assert.That(frontier, Does.Contain("hib frontier"));
            Assert.That(frontier, Does.Not.Contain("new frontiers").And.Not.Contain("horse"));
            string home = Know.OtherRealmLine("Connacht", eRealm.Hibernia, AutonomousChatLiveAnswers.FrontierHome("Connacht") == eRealm.Hibernia, random);
            Assert.That(home, Does.Contain("hib").Or.Contain("hibernia"));
            Assert.That(home, Does.Contain("cant").Or.Contain("no way"));
        }
        Assert.That(Know.HomeRealmOfRegion(1), Is.EqualTo(eRealm.Albion));
        Assert.That(Know.HomeRealmOfRegion(100), Is.EqualTo(eRealm.Midgard));
        Assert.That(Know.HomeRealmOfRegion(163), Is.EqualTo(eRealm.None));
    }

    // ---- 6. a / an --------------------------------------------------------------------------------------------------------

    [TestCase("armorcrafter", true)]
    [TestCase("infiltrator", true)]
    [TestCase("elementalist", true)]
    [TestCase("Odin's Gate", true)]
    [TestCase("undead", true)]
    [TestCase("unique", false)]
    [TestCase("useful", false)]
    [TestCase("one-hander", false)]
    [TestCase("fletcher", false)]
    [TestCase("18", true)]
    [TestCase("8", true)]
    [TestCase("80", true)]
    [TestCase("11", true)]
    [TestCase("20", false)]
    [TestCase("1", false)]
    [TestCase("hour", true)]
    public void TheArticleFitsTheWordAfterIt(string word, bool an) => Assert.That(VC.TakesAn(word), Is.EqualTo(an));

    [Test]
    public void AArmorcrafterReadsAnArmorcrafter()
    {
        // 07:13:50 Brachea: "a armorcrafter can make it, or look on the realm exchange in camelot".
        for (int i = 0; i < 40; i++)
        {
            Assert.That(Know.TryAnswerBuyGear(eRealm.Albion, "anyone know where to get a good shield?", out string answer), Is.True);
            Assert.That(answer, Does.Not.Contain("a armorcrafter"));
            if (answer.Contains("armorcrafter"))
                Assert.That(answer, Does.Contain("an armorcrafter"));
        }
        Assert.That(VC.FixArticles("a armorcrafter can make it"), Is.EqualTo("an armorcrafter can make it"));
        Assert.That(VC.FixArticles("saw a infil near a elder"), Is.EqualTo("saw an infil near an elder"));
        Assert.That(VC.FixArticles("plan a or b, a unique drop, a fletcher"), Is.EqualTo("plan a or b, a unique drop, a fletcher"), "the rest as typed");
        Assert.That(VC.TryFill("im a {cls} lol", new Dictionary<string, string> { ["cls"] = "Infiltrator" }, out string filled), Is.True);
        Assert.That(filled, Is.EqualTo("im an Infiltrator lol"));
        Assert.That(VC.TryFill("what does everyone charge for a {level} suit?", new Dictionary<string, string> { ["level"] = "18" }, out string suit), Is.True);
        Assert.That(suit, Is.EqualTo("what does everyone charge for an 18 suit?"));
        Assert.That(BotLedGroups.Fill("need a {need} for {camp}", new Dictionary<string, string> { ["need"] = "enchanter", ["camp"] = "the bridge" }),
            Is.EqualTo("need an enchanter for the bridge"));
    }
}
