using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.GS;
using DOL.GS.LoreChannels;
using NUnit.Framework;
using Tales = DOL.GS.AutonomousTales;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.UnitTests;

/// <summary>
/// The 1.2 chat audit's lore and tales fixes (LORE-001 to LORE-029, LOG-025; scratchpad chataudit/lore/FINDINGS.md). The
/// pure rules of tales (requests, questions during a tale, who may tell, the cut and the hand-over), lore answers from the
/// written lore lines, the /lore heading, Golestandt's emotes, and the retired wordings. Tests only: none of this has been
/// seen on the owner's PC.
/// </summary>
[TestFixture]
public class UT_ChatLoreFixes
{
    private static VC _corpus;
    private static VC Corpus => _corpus ??= Load();

    private static VC Load()
    {
        var assembly = typeof(VC).Assembly;
        string Read(string suffix)
        {
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
    }

    private static string Source(params string[] parts)
    {
        string dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
            dir = Path.GetDirectoryName(dir);
        Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
        return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray()));
    }

    // LORE-002: the audit's eleven lines, each a real question or remark that started a tale or a brush-off.
    [TestCase("can someone tell me what spec is good for friar")]
    [TestCase("tell me about cleric spec")]
    [TestCase("anyone tell me who sells arrows")]
    [TestCase("long story short i need a rez")]
    [TestCase("can you tell them to stop pulling")]
    [TestCase("i cant tell if df is open")]
    [TestCase("how can you tell what con it is")]
    [TestCase("whats the story with the keeps tonight")]
    [TestCase("wts tale of the sea cloak")]
    [TestCase("pls tell me why i keep dying")]
    [TestCase("can anyone tell me a good xp spot at 30")]
    public void TellAloneIsNotARequestForATale(string message) => Assert.That(Tales.IsRequest(message), Is.False, message);

    [TestCase("tell the one about legion")]
    [TestCase("any stories tonight?")]
    [TestCase("Maeve, tell us a tale")]
    [TestCase("story time!")]
    [TestCase("tell us about the city under the sea")]
    [TestCase("tell it again pls")]
    public void AStoryWordOrATellingIsARequest(string message) => Assert.That(Tales.IsRequest(message), Is.True, message);

    [Test]
    public void TellWithATalesOrATellersNameIsARequest()
    {
        Assert.That(Tales.IsRequest("tell orylle"), Is.False, "no name known");
        Assert.That(Tales.IsRequest("tell orylle", namesATale: true), Is.True);
        Assert.That(Tales.IsRequest("Thotavyn tell the stone dragon pls", namesATeller: true), Is.True);
        Assert.That(Tales.NamesATeller("thotavyn tell the stone dragon pls", ["Thotavyn", "Merootek"]), Is.True);
        Assert.That(Tales.NamesATeller("can someone tell me a spot", ["Thotavyn", "Merootek"]), Is.False);
        Assert.That(Tales.IsRequest("wts tale of the sea cloak", namesATale: true), Is.False, "a trade line never is");
    }

    // LORE-003: a question asked during a tale goes to the teller only when it is about the tale.
    [TestCase("is anyone selling a cloak")]
    [TestCase("where is the trainer")]
    [TestCase("can i get a rez at the bind")]
    [TestCase("wts 99% hauberk?")]
    [TestCase("how much for the sword")]
    public void EverydayQuestionsDuringATaleAreNotForTheTeller(string message)
    {
        string[] tale = Tales.LinesOf(Corpus.Scenes.Single(s => s.Id == "tale_orylle")).ToArray();
        Assert.That(Tales.IsTaleQuestion(message, AutonomousChatIntentModel.Predict(message), tale), Is.False, message);
    }

    [Test]
    public void QuestionsAboutTheTaleAreForTheTeller()
    {
        string[] tale = Tales.LinesOf(Corpus.Scenes.Single(s => s.Id == "tale_orylle")).ToArray();
        Assert.That(Tales.IsTaleQuestion("who was the last elder?", eAutonomousChatIntent.Unknown, tale), Is.True);
        Assert.That(Tales.IsTaleQuestion("why does she throw ppl", eAutonomousChatIntent.Unknown, tale), Is.True);
        Assert.That(Tales.IsTaleQuestion("lol", eAutonomousChatIntent.Laugh, tale), Is.False, "not a question");
        const string offTopic = "whats a decent spec for a twohanded mercenary leveling solo near thirty";
        Assert.That(Tales.SharesAWord(offTopic, tale), Is.False, "precondition");
        Assert.That(Tales.IsTaleQuestion(offTopic, eAutonomousChatIntent.Unknown, tale), Is.False, "long and shares no word with the tale");
        Assert.That(Tales.IsTaleQuestion("what about the guest stone, did the ogres always keep it like the hall folk", eAutonomousChatIntent.Unknown,
            tale), Is.True, "long but about the tale");
        Assert.That(Tales.SharesAWord("the guest stone", ["she set them back on the guest stone"]), Is.True);
        Assert.That(Tales.SharesAWord("where is the trainer", ["she set them back on the guest stone"]), Is.False);
    }

    // LORE-004 and LOG-025: who can take a tale, and what happens when the teller leaves before its first line.
    [Test]
    public void ATellerMustBeAbleToStay()
    {
        long longAgo = long.MaxValue;
        Assert.That(Tales.CanTellNow(false, false, longAgo, false), Is.True);
        Assert.That(Tales.CanTellNow(false, false, 11_000, false), Is.False, "Chocchipper: died eleven seconds earlier");
        Assert.That(Tales.CanTellNow(false, false, longAgo, true), Is.False, "Chocchipper: logged off five seconds after the asking line");
        Assert.That(Tales.CanTellNow(true, false, longAgo, false), Is.False, "fighting");
        Assert.That(Tales.CanTellNow(false, true, longAgo, false), Is.False, "running back after a death");
        Assert.That(Tales.CanTellNow(false, false, Tales.TellerDiedMs, false), Is.True);
        Assert.That(Tales.TellerStaysMs, Is.EqualTo(10 * 60_000L));
    }

    [Test]
    public void ATaleWhoseTellerLeftBeforeItsFirstLineIsHandedOverOnce()
    {
        Assert.That(Tales.ShouldHandOver(false, "the teller left", 0, false), Is.True, "the build 5378dbe Krondon tale");
        Assert.That(Tales.ShouldHandOver(false, "the teller left", 0, true), Is.False, "once only");
        Assert.That(Tales.ShouldHandOver(false, "the teller left", 3, false), Is.False, "the teller had begun");
        Assert.That(Tales.ShouldHandOver(true, "finished", 0, false), Is.False);
        Assert.That(Tales.ShouldHandOver(false, "room", 0, false), Is.False);
        // The remark after a cut: the 5378dbe tale was cut after three lines and got none (the floor was four).
        Assert.That(Tales.CutRemarkDue(false, "the teller left", 3), Is.True);
        Assert.That(Tales.CutRemarkDue(false, "the teller left", 0), Is.False);
        VC.Scene orylle = Corpus.Scenes.Single(s => s.Id == "tale_orylle");
        int first = Tales.FirstTellerBeat(orylle);
        Assert.That(orylle.Beats[first].Role, Is.EqualTo(Tales.TellerRole));
        Assert.That(orylle.Beats.Take(first).Any(b => b.Role == Tales.TellerRole), Is.False);
        foreach (string line in Tales.Handover.Concat(Tales.CutBeforeStart))
        {
            Assert.That(line.Length, Is.InRange(8, 110), line);
            Assert.That(line, Is.EqualTo(line.ToLowerInvariant()), line);
        }
        Assert.That(Tales.Handover, Has.All.Contain("{old}").And.All.Contain("{new}"));
        Assert.That(Tales.CutBeforeStart, Has.All.Contain("{teller}"));
    }

    // LORE-005: a tale is cut only when the teller logs off; the remark says so.
    [Test]
    public void TheCutShortRemarkSaysTheTellerLogged()
    {
        Assert.That(Tales.CutShort, Has.None.Contain("fight"));
        Assert.That(Tales.CutShort, Does.Contain("aw, {teller} logged").And.Contain("{teller} went ld i think"));
    }

    // LORE-006
    [Test]
    public void TheRealmsOwnTaleSaysWhyItDidNotStart()
    {
        string line = Tales.OwnTaleNotStartedLog(eRealm.Albion, "the realm's storyteller (Thotavyn or Merootek) is not online");
        Assert.That(line, Does.StartWith("TALE_NOT_STARTED realm=Albion kind=own"));
        Assert.That(line, Does.Contain("reason=\"the realm's storyteller (Thotavyn or Merootek) is not online\""));
    }

    // LORE-001: the written lore lines answer a question that names their subject.
    [Test]
    public void AQuestionCanBeAnsweredFromTheWrittenLoreLines()
    {
        LoreBible bible = LoreBible.Current;
        string[] written = LoreChat.WrittenLoreNaming(Corpus.Lines, LoreBible.NamesOf("Xanxicar", "xanxicar", []), eRealm.Albion);
        Assert.That(written, Is.Not.Empty);
        Assert.That(written, Has.All.Contain("xanxicar"));
        Assert.That(written.Any(text => text.Contains('{')), Is.False, "said as it stands");
        Assert.That(LoreChat.WrittenLoreNaming(Corpus.Lines, LoreBible.NamesOf("Xanxicar", "xanxicar", []), eRealm.Midgard), Is.Empty,
            "an Albion dragon's lines are Albion's");
        var heard = new HashSet<string>();
        for (int seed = 0; seed < 40; seed++)
            if (LoreChat.TryAnswer(bible, Corpus.Lines, "who is xanxicar?", new Random(seed), out string answer, eRealm.Albion))
                heard.Add(answer);
        Assert.That(heard.Overlaps(written), Is.True, "a boss has no tell line, so the written lines answer");
        Assert.That(LoreChat.TryAnswer(bible, Corpus.Lines, "lfg krondon", new Random(1), out _, eRealm.Albion), Is.False, "not a question");
    }

    // LORE-001.3 (owner, 2026-11-18 09:14: lore is never cut): placed lore where the bot stands, at a measured rate.
    [Test]
    public void PlacedLoreIsHeardAboutTwiceAnHourInAPlaceWithLore()
    {
        var random = new Random(7);
        int heard = 0;
        long last = long.MinValue / 2;
        const int hours = 200;
        for (long now = 0; now < hours * 3_600_000L; now += LoreChat.PlacedCheckMs)
            if (LoreChat.PlacedDue(now, last, random.NextDouble()))
            {
                heard++;
                last = now;
            }
        double perHour = heard / (double)hours;
        Assert.That(perHour, Is.InRange(LoreChat.PlacedTargetPerHour - 0.5, LoreChat.PlacedTargetPerHour + 0.5), $"{perHour:0.00} an hour");
        Assert.That(LoreChat.PlacedDue(10 * 60_000, 0, 0.0), Is.False, "never two within the gap");
    }

    [Test]
    public void PlacedLoreUsesTheWrittenLinesOfThePlaceAndNeverRepeats()
    {
        LoreBible bible = LoreBible.Current;
        LoreBible.Location crystal = bible.Locations.FirstOrDefault(l => bible.BossesAt(l.Id).Any(b => b.Id == "xanxicar"));
        Assume.That(crystal, Is.Not.Null, "Xanxicar's lair has a lore file");
        string[] pool = LoreChat.PlacedLines(bible, Corpus.Lines, crystal, eRealm.Albion);
        string[] written = LoreChat.WrittenLoreNaming(Corpus.Lines, LoreBible.NamesOf("Xanxicar", "xanxicar", []), eRealm.Albion);
        Assert.That(pool.Intersect(written), Is.Not.Empty, "the written lore lines that name its boss are said there");
        var heard = new HashSet<string>(pool.Take(3));
        Assert.That(LoreChat.PlacedLines(bible, Corpus.Lines, crystal, eRealm.Albion, heard).Intersect(heard), Is.Empty);
        Assert.That(LoreChat.PlacedLines(bible, Corpus.Lines, null, eRealm.Albion), Is.Empty);
    }

    [Test]
    public void APlacesOwnTaleIsAskedForByThePlacesName()
    {
        LoreBible bible = LoreBible.Current;
        LoreBible.Location place = bible.Locations.First(l => bible.BossById(l.Id) == null && !Tales.IsLegend(l.Id));
        Assert.That(Tales.RequestNames(place.Id, bible), Does.Contain(LoreBible.NamesOf(place.Name, place.Id, place.Aliases).First()));
    }

    // LORE-029
    [Test]
    public void TheCodexBookDoesNotSayPlayers()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        LoreCodexPage krondon = LoreCodex.Find(lore, LoreRealm.Albion, [], "krondon");
        Assert.That(krondon.Lines, Does.Contain("What is said"));
        Assert.That(krondon.Lines, Has.None.EqualTo("What players say"));
    }

    // LORE-011: the script's emotes follow the lore's "she" (strings only; the fight is unchanged).
    [Test]
    public void GolestandtsEmotesSayShe()
    {
        string script = Source("scripts", "namedmobs", "Dragons", "AlbGolestandt.cs");
        foreach (string old in new[] { "falls before his might", "as he prepares to unleash", "creatures attacking him.", "from his wounds as he attempts" })
            Assert.That(script, Does.Not.Contain(old));
        Assert.That(script, Does.Contain("falls before her might").And.Contain("creatures attacking her."));
    }

    // LORE-007..025: wordings the audit retired (anachronisms, contradictions with this world, lost apostrophes, coinages).
    [TestCase("shield node")]
    [TestCase("healing node")]
    [TestCase("past the deepest rooms of darkness falls")]
    [TestCase("by sixty percent")]
    [TestCase("uther sent sir arward")]
    [TestCase("every herald writes she")]
    [TestCase("every herald piece says so")]
    [TestCase("burn them down together")]
    [TestCase("so we split damage")]
    [TestCase("a copy of himself")]
    [TestCase("apparition of nosdoden")]
    [TestCase("thats how most raids there end")]
    [TestCase("every hit on him does nothing")]
    [TestCase("she is level sixty seven")]
    [TestCase("the green knight at sixty")]
    [TestCase("the arawnites in the black mountains")]
    [TestCase("caer witrin, wearyall, all on the isle")]
    [TestCase("heals to full")]
    [TestCase("each with a full life")]
    [TestCase("something at the bottom keeps the count")]
    [TestCase("aim for the lid")]
    [TestCase("sear a neck with a sun spell")]
    [TestCase("the islands last autumn")]
    [TestCase("when the keeps got rebuilt")]
    [TestCase("still sell those wards")]
    [TestCase("cool cool")]
    [TestCase("real talk")]
    [TestCase("for science")]
    [TestCase("just saying")]
    [TestCase(" oof")]
    [TestCase("jordheims skalds")]
    [TestCase("thors might")]
    [TestCase("ferrymans son")]
    [TestCase("facing the summoners hall")]
    [TestCase("the Bed")]
    [TestCase("the Quieting")]
    [TestCase("King's Law")]
    [TestCase("thats why. and the giants")]
    public void ARetiredWordingIsGone(string phrase)
    {
        var lore = Corpus.Lines.Where(l => l.Topic == "lore").Select(l => l.Text)
            .Concat(Corpus.Scenes.Where(s => s.Topic != null && (s.Topic.StartsWith("lore", StringComparison.Ordinal) || Tales.IsTaleScene(s)))
                .SelectMany(Tales.LinesOf));
        Assert.That(lore.Where(text => text.Contains(phrase, StringComparison.Ordinal)), Is.Empty, phrase);
    }

    // LORE-023: every tale's teller says yes before the story, in its own words.
    [Test]
    public void EveryTellerSaysYesBeforeTheStory()
    {
        // The boss tales (data/chat/tales.lines); the legends of other files keep their own openings.
        VC.Scene[] tales = Corpus.Scenes.Where(Tales.IsTale).Where(s => Tales.RaidIdFor(Tales.LoreIdOf(s.Topic), eRealm.Albion) != null).ToArray();
        Assert.That(tales.Length, Is.GreaterThanOrEqualTo(25));
        var openers = new List<string>();
        foreach (VC.Scene tale in tales)
        {
            VC.Beat first = tale.Beats[Tales.FirstTellerBeat(tale)];
            Assert.That(first.Variants.All(v => v.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 11), Is.True,
                $"{tale.Id}: the teller's first line is the yes, not the story: {first.Variants[0]}");
            openers.AddRange(first.Variants);
        }
        Assert.That(openers.Distinct().Count(), Is.EqualTo(openers.Count), "each tale's yes is its own");
    }

    // LORE-007: the older tale scenes that retell a storyteller's subject send the listener to the storytellers.
    [TestCase("alb_tale_arwards_knights")]
    [TestCase("lore_hib_tale_sheeroe_pass")]
    [TestCase("lore_hib_tale_balor_eye")]
    [TestCase("lore_hib_tale_olcasgean_banished")]
    [TestCase("mid_tale_ice_queen")]
    [TestCase("mid_tale_fenris_daughter")]
    public void AHalfRememberedTalePointsAtTheStorytellers(string sceneId)
    {
        VC.Scene scene = Corpus.Scenes.Single(s => s.Id == sceneId);
        Assert.That(scene.Beats.Where(b => !b.IsPause).TakeLast(2).SelectMany(b => b.Variants).Any(v => v.Contains("storytellers")), Is.True, sceneId);
    }
}
