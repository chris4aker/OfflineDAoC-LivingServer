using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Market = DOL.GS.AutonomousChatMarket;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// Living Server 1.2, the bot chat audit (E2: field talk, market, realm event phrasing): one test per finding, each failing on
/// the build before the fix (integration/1.2 @ 311110e62) and passing after. Tests only; nothing here was seen on the owner's PC.
/// </summary>
[TestFixture]
public class UT_ChatFieldTalkAudit12
{
    private static readonly Field.WorldView Busy = new(["Caer Benowyc", "Caer Berkstead"], ["Caer Benowyc"], ["Caer Renaris"], ["Hibernia"], 0, 1,
        "Forest Sauvage");

    private static Field.Facts Facts(long id = 4242, string cls = "Cleric", int level = 38, string zone = "Lyonesse", bool rvr = false, bool town = false,
        bool dinged = false, bool died = false, bool group = false, bool camp = true, string monster = "river spriteling", string item = "Bloodied Chain Hauberk",
        int hour = 20, Field.WorldView world = null, eRealm realm = eRealm.Albion) =>
        new(id, realm, cls, level, zone, "Keltoi Fogou", rvr, false, town, group, group ? 4 : 1, dinged, died, false, camp, monster, item, 97, true, null, 3, hour,
            cls is "Cleric" or "Friar", world ?? Busy);

    private static readonly string[] Classes =
        ["Armsman", "Paladin", "Mercenary", "Cleric", "Friar", "Minstrel", "Wizard", "Theurgist", "Cabalist", "Infiltrator", "Necromancer", "Scout"];

    /// <summary>Every line a field draw can say: the line, its answers, follow-ups and thanks.</summary>
    private static IEnumerable<string> Spoken(Field.Said said) =>
        new[] { said.Text }.Concat(said.Answers ?? []).Concat(said.Unanswered ?? []).Concat(said.Thanks ?? []);

    private static List<Field.Said> Draw(string channel, Func<int, Field.Facts> facts, int count, int seed = 7)
    {
        var random = new Random(seed);
        var lines = new List<Field.Said>();
        for (int i = 0; i < count; i++)
            if (Field.Line(channel, facts(i), null, random, i * 1_000L) is { } said)
                lines.Add(said);
        return lines;
    }

    private static List<string> AllText(int count = 3000)
    {
        var text = new List<string>();
        foreach (string channel in new[] { "faction", "say", "lfg", "trade", "broadcast", "guild" })
            text.AddRange(Draw(channel, i => Facts(id: i, cls: Classes[i % Classes.Length], level: 5 + i % 46, rvr: i % 5 == 0, town: i % 4 == 0,
                dinged: i % 9 == 0, died: i % 7 == 0, group: i % 6 == 0) with { GuildmateArrived = i % 3 == 0 ? "Bran" : null }, count)
                .SelectMany(Spoken));
        var random = new Random(3);
        for (int i = 0; i < 400; i++)
        {
            Field.Facts teller = Facts(id: i, died: true, rvr: i % 2 == 0) with { KilledBy = i % 3 == 0 ? "player" : "monster" };
            foreach (string kind in new[] { "death", "wipe", "ding50" })
                text.AddRange(Field.Story(kind, teller, Facts(id: i + 1), Facts(id: i + 2), random).Select(step => step.Text));
        }
        return text;
    }

    // ---- TEXT-010, 011, 013, 015, 016, 026, 027: out-of-era words in generated lines ------------------------------------------

    [Test]
    public void GeneratedLinesCarryNoLaterChatWords()
    {
        List<string> text = AllText();
        Assert.That(text, Has.None.Match(@"(^|\s)\\?o/"), "TEXT-010: o/");
        Assert.That(text, Has.None.Match(@"(^|\s)rip(\s|,|$)"), "TEXT-011: the bare rip");
        Assert.That(text, Has.None.Match(@"\btowers?\b"), "TEXT-013: towers");
        Assert.That(text, Has.None.Match(@"\brn\b"), "TEXT-015: rn");
        Assert.That(text, Has.None.Match(@"the map\b"), "TEXT-016: the map");
        Assert.That(text, Has.None.Match(@"/t\b"), "TEXT-026: /t");
        Assert.That(text, Has.None.Match(@"kill 10\b"), "TEXT-027: kill 10");
        Assert.That(text, Has.None.Match(@"\bdps\b"), "TEXT-012: dps");
        Assert.That(text, Has.None.Match(@"\blink it\b"), "TEXT-014: item links");
    }

    [Test]
    public void TheSpokenNukerIsACasterSeat()
    {
        Assert.That(Field.RoleOf("a nuker"), Is.EqualTo("nuker"));
        Assert.That(Field.Fills("Wizard", "nuker"), Is.True);
        Assert.That(Field.Fills("Armsman", "nuker"), Is.False);
    }

    [Test]
    public void SpecTipsAreTrueOfTheClassLines()
    {
        Assert.That(Field.SpecAdvice("Wizard"), Does.Contain("ice if u want pbae").And.Not.Contain("earth if u want pbae"));
        Assert.That(Field.SpecAdvice("Runemaster"), Does.Contain("suppression if u want pbae"));
        Assert.That(Field.SpecAdvice("Theurgist"), Does.Contain("earth for grps, air for rvr"));
        Assert.That(Field.SpecAdvice("Ranger"), Has.None.Contain("archery"));
    }

    // ---- TEXT-018: one realm word in two shapes ------------------------------------------------------------------------------

    [Test]
    public void ARealmIsItsPeopleOrAModifierNeverADraw()
    {
        Assert.That(Field.RealmWord("Hibernia"), Is.EqualTo("hibs"));
        Assert.That(Field.RealmWord("Hibernia", modifier: true), Is.EqualTo("hib"));
        Assert.That(RealmEventSpoken.RealmWord("Midgard"), Is.EqualTo("mids"));
        var random = new Random(5);
        var said = Enumerable.Range(0, 200).Select(_ => RealmEventSpoken.Say("Scouts report Hibernia's army marching on Caer Benowyc!", null, random)).ToList();
        Assert.That(said, Has.None.Match(@"\bhibs (zerg|army)\b"), "a modifier before a noun");
        Assert.That(said, Has.None.Match(@"\bhib (coming|on the way|marching)\b"), "the people as a subject");
        var lost = Enumerable.Range(0, 200).Select(_ => RealmEventSpoken.Say("Caer Benowyc has fallen to Hibernia. Tend the wounded; this struggle is not over.", null, random));
        Assert.That(lost, Has.None.Match(@"\bhib (took|have|got)\b"));
        var fieldText = Draw("faction", i => Facts(id: i, level: 45, rvr: true), 2000).SelectMany(Spoken).ToList();
        Assert.That(fieldText, Has.None.Match(@"\b(albs|mids|hibs) (zerg|grp|army)\b"));
        Assert.That(fieldText, Has.None.Match(@"\b(alb|mid|hib) (have|took|at|cant|got)\b"));
    }

    // ---- LOG-004: a defended siege is a hold --------------------------------------------------------------------------------

    [Test]
    public void ADefendedSiegeIsToldAsHeldToEveryRealm()
    {
        string third = RealmEventBanter.SiegeOutcome("Dun nGed", false, true, false, eRealm.Hibernia, eRealm.Albion);
        Assert.That(third, Does.StartWith("Hibernia held Dun nGed").And.Not.Contain("fallen"));
        Assert.That(RealmEventBanter.SiegeOutcome("Dun nGed", true, true, false, eRealm.Hibernia, eRealm.Hibernia), Does.Contain("We held"));
        var random = new Random(2);
        var spoken = Enumerable.Range(0, 100).Select(_ => RealmEventSpoken.Say(third, null, random)).ToList();
        Assert.That(spoken, Has.None.Contain("took"));
        Assert.That(spoken, Has.Some.EqualTo("hibs held nged"));
        // A real capture still reads as one.
        Assert.That(RealmEventBanter.SiegeOutcome("Keep", false, false, false, eRealm.Hibernia, eRealm.Midgard), Does.Contain("fallen to Hibernia"));
    }

    // ---- TEXT-021, LOG-033, CORPUS-B2-035: one name per place ---------------------------------------------------------------

    [Test]
    public void APlaceHasOneNameEveryTime()
    {
        var random = new Random(11);
        foreach ((string keep, string name) in new[] { ("Caer Benowyc", "beno"), ("Bledmeer Faste", "bled"), ("Dun da Behnn", "behn"), ("Dun Ailinne", "ail"),
                     ("Caer Erasleigh", "eras"), ("keep Dun nGed", "nged"), ("Castle Sauvage", "sauvage") })
            Assert.That(Enumerable.Range(0, 30).Select(_ => Field.ShortKeep(keep, random)).Distinct(), Is.EqualTo(new[] { name }), keep);
        Assert.That(Enumerable.Range(0, 30).Select(_ => Field.ShortZone("Pennine Mountains", random)).Distinct(), Is.EqualTo(new[] { "pennine mountains" }));
        Assert.That(Field.ShortZone("Silvermine Mts.", random), Is.EqualTo("silvermine mts"));
        Assert.That(Field.ShortZone("Black Mtns. South", random), Is.EqualTo("black mtns south"));
    }

    // ---- LOG-007: wipes -------------------------------------------------------------------------------------------------------

    [Test]
    public void AWipeIsHalfTheGroupDownAndToldWhereItHappened()
    {
        Assert.That(Field.IsWipe(3, 1), Is.False);
        Assert.That(Field.IsWipe(4, 2), Is.True);
        Assert.That(Field.IsWipe(1, 1), Is.False);
        var random = new Random(4);
        for (int i = 0; i < 200; i++)
        {
            // Killed by a monster in a battleground: no enemy group; the place is the zone, not the dungeon drawn for the level.
            Field.Facts teller = Facts(id: i, zone: "Pennine Mountains", rvr: true) with { KilledBy = "monster" };
            List<string> told = Field.Story("wipe", teller, Facts(id: i + 1), null, random).Select(step => step.Text).ToList();
            Assert.That(told, Has.None.Match(@"\b(hibs?|mids?|albs?|fg)\b"), string.Join(" / ", told));
            Assert.That(told, Has.None.Match(@"\b(keltoi|fogou)\b"));
            List<string> part = Field.Story("wipe", teller, Facts(id: i + 1), null, random, wholeGroup: false).Select(step => step.Text).ToList();
            Assert.That(part.FirstOrDefault(), Does.Not.Match(@"\b(whole|wiped|wipe)\b"));
        }
    }

    // ---- LOG-008, 009, 010, 034, TEXT-014: the market -------------------------------------------------------------------------

    [Test]
    public void ABuyerAnswersAsABuyer()
    {
        var random = new Random(8);
        Field.Facts poster = Facts(level: 50);
        var said = Enumerable.Range(0, 200).Select(_ => Market.PosterAnswer("paying", Market.Post.Wtb, "wtb chain helm pst", poster, null, false, true, random)).ToList();
        Assert.That(said, Has.None.Contain("make an offer").And.None.Contain("offers"));
        Assert.That(said, Has.Some.Match(@"\d+p tops"));
    }

    [Test]
    public void SaleRepliesDontAskTheObviousOrRepeat()
    {
        var random = new Random(9);
        Field.Facts replier = Facts(level: 30, cls: "Armsman");
        var wts = Enumerable.Range(0, 300).Select(_ => Market.ReplyTo(Market.Post.Wts, "wts 99% chain legs 2p", replier, true, false, random).Text).ToList();
        Assert.That(wts, Has.None.EqualTo("still have it?"), "LOG-009");
        var wtb = Enumerable.Range(0, 300).Select(_ => Market.ReplyTo(Market.Post.Wtb, "wtb 27 plate sleeves pst", replier, true, false, random).Text).ToList();
        Assert.That(wtb, Has.None.EqualTo("what lvl?"), "LOG-010");
        var stats = Enumerable.Range(0, 100).Select(_ => Market.PosterAnswer("stats", Market.Post.Wts, "wts x", replier, null, false, true, random));
        Assert.That(stats, Has.None.Contain("link"), "TEXT-014");
        var lfg = Enumerable.Range(0, 300).Select(_ => Market.ReplyTo(Market.Post.Lfg, "lfg 38 clr", replier, false, false, random, 38).Text).Distinct().ToList();
        Assert.That(lfg.Count, Is.GreaterThan(2), "LOG-034: more than one opening");
        var asked = new Market.AskedPosters();
        Assert.That(asked.TryAsk(77, 1_000), Is.True);
        Assert.That(asked.TryAsk(77, 20 * 60_000), Is.False);
        Assert.That(asked.TryAsk(77, 1_000 + Market.AskedPosters.RestMs), Is.True);
    }

    // ---- LOG-011: "quick q" -----------------------------------------------------------------------------------------------------

    [Test]
    public void AQuickQIsAlwaysAsked()
    {
        var random = new Random(12);
        int threads = 0;
        for (int i = 0; i < 4000 && threads < 40; i++)
        {
            Field.Facts asker = Facts(id: i, cls: "Necromancer", level: 30 + i % 21);
            if (Field.Line("faction", asker, null, random, 0) is not { Family: "class_q" } said)
                continue;
            List<Field.Step> steps = Field.Thread(said, asker, Facts(id: i + 9, cls: "Necromancer", level: 40), null, null, random);
            if (steps.Count == 0 || steps[0].Who != 1)
                continue;
            threads++;
            // "me", then the asker's question, then the answer to it.
            Assert.That(steps.Count >= 3 && steps[1].Who == 0 && steps[2].Who == 1, Is.True, string.Join(" / ", steps.Select(step => step.Text)));
        }
        Assert.That(threads, Is.GreaterThan(5));
    }

    // ---- LOG-012, LOG-018: answers that answer ------------------------------------------------------------------------------

    [Test]
    public void BuyOrWaitIsNoPriceQuestionAndPoolsInventNothing()
    {
        Assert.That(Field.QuestionKind("worth buying chain legs at 30 or wait for drops?"), Is.Null);
        Assert.That(Field.QuestionKind("what's a chain helm worth?"), Is.EqualTo("price"));
        List<string> text = AllText(2000);
        Assert.That(text, Has.None.Match(@"^is .* worth it at \d+"), "LOG-012");
        foreach (string invented in new[] { "decent stuff", "nearest town", "by the inn", "next to the merchants", "not much", "ask a stable master", "jewelry mostly" })
            Assert.That(text, Has.None.Contain(invented), invented);
    }

    // ---- LOG-013: no beginner's questions from a 50 ----------------------------------------------------------------------------

    [Test]
    public void ALevelFiftyAsksNoBeginnersQuestion()
    {
        var text = Draw("faction", i => Facts(id: i, cls: Classes[i % Classes.Length], level: 50, rvr: i % 3 == 0) with { Live = true, RealmLevel = 25 }, 4000)
            .Select(said => said.Text).ToList();
        foreach (string beginner in new[] { "still grinding", "grinding ", "what weapon for", "where to buy", "ish quest", "where do i bind", "how do realm points work" })
            Assert.That(text, Has.None.Contain(beginner), beginner);
        // A frontier zone has no quests or binds to ask about, at any level.
        var frontier = Draw("faction", i => Facts(id: i, level: 30, zone: "Snowdonia"), 3000).Select(said => said.Text).ToList();
        Assert.That(frontier, Has.None.Match(@"\bquest\b|\bbind\b"));
    }

    // ---- LOG-014, LOG-028: town /say lines --------------------------------------------------------------------------------------

    [Test]
    public void TownSayQuestionsAreQuestionsAndSmallTalkHasNoLostFollowUp()
    {
        var said = Draw("say", i => Facts(id: i, town: true, item: null), 2000);
        Assert.That(said.Where(s => s.Text.TrimEnd('?') == "any healers around"), Has.All.Matches<Field.Said>(s => Field.WantsAnswer(s)));
        Assert.That(said.Where(s => s.Text.StartsWith("anyone selling dye")), Has.All.Matches<Field.Said>(s => s.Answers is { Length: > 0 }));
        Assert.That(said.Where(s => s.Text is "afk 5" or "back" or "brb"), Has.All.Matches<Field.Said>(s => s.Unanswered == null));
        Assert.That(said.SelectMany(s => s.Unanswered ?? []), Has.None.Match(@"for \?|for$"));
    }

    // ---- LOG-016: plurals -------------------------------------------------------------------------------------------------------

    [Test]
    public void ClassPluralsAreWords()
    {
        var random = new Random(1);
        Assert.That(Enumerable.Range(0, 20).Select(_ => Field.ClassPlural("Armsman", random)), Is.All.AnyOf("arms", "armsmen"));
        Assert.That(Enumerable.Range(0, 20).Select(_ => Field.ShortClass("Minstrel", random)), Has.None.EqualTo("mins"));
        var text = Draw("faction", i => Facts(id: i, cls: new[] { "Armsman", "Wizard", "Minstrel" }[i % 3], level: 20 + i % 30), 3000).Select(s => s.Text);
        Assert.That(text, Has.None.Match(@"\b(armss|armsmans|wizs|mins)\b"));
    }

    // ---- LOG-019: who could know ------------------------------------------------------------------------------------------------

    [Test]
    public void OnlyThoseWhoCouldKnowSaySo()
    {
        Assert.That(Field.SaysNothing("dont think so"), Is.True);
        Assert.That(Field.SaysNothing("not that i know"), Is.True);
        var item = new ChatNewsItem("enemy_seen", eRealm.Albion, DateTime.UtcNow) { Enemy = eRealm.Midgard, Zone = "Hadrian's Wall", Size = 4 };
        var random = new Random(6);
        Assert.That(Enumerable.Range(0, 50).Select(_ => AutonomousChatLiveAnswers.SightingText(item, DateTime.UtcNow, random)), Has.None.StartWith("saw"));
        Assert.That(AutonomousChatLiveAnswers.BattlegroundLevels("Murdaigean"), Is.EqualTo(((int, int)?)(25, 29)));
        // "did it" to a level 50's quest question only from a bot of 50.
        var quest = new Field.Said("anyone know the 50ish quest in lyonesse?", "help", ["{done}did it, easy one", "dunno that one"]) { AskerLevel = 50 };
        Assert.That(Enumerable.Range(0, 50).Select(_ => Field.Answer(quest, Facts(level: 28, cls: "Friar"), null, random)), Has.None.StartWith("did it"));
    }

    // ---- LOG-020: exchange adverts ----------------------------------------------------------------------------------------------

    [Test]
    public void AnExchangeAdvertIsASale()
    {
        var random = new Random(3);
        var ads = Enumerable.Range(0, 60).Select(_ => AutonomousBotChatCoordinator.ExchangeAdvert("Edgebender Choker", random)).ToList();
        Assert.That(ads, Has.None.StartWith("pc "));
        Assert.That(ads, Has.All.Contain("edgebender choker"));
        Assert.That(AllText(1500), Has.None.Contain("broker"));
    }

    // ---- LOG-030: the death feed ---------------------------------------------------------------------------------------------------

    [Test]
    public void DeathOpenersRestAndNameTheirCause()
    {
        Assert.That(Field.Memory.FamilyRestMs("death"), Is.EqualTo(Field.Memory.DeathRestMs));
        var memory = new Field.Memory();
        var random = new Random(10);
        var deaths = new List<long>();
        for (long now = 0; now < 30 * 60_000; now += 10_000)
            if (Field.Line("faction", Facts(id: now, died: true) with { KilledBy = "monster", Dead = false }, memory, random, now) is { Kind: "death" } said)
            {
                deaths.Add(now);
                Assert.That(said.Text, Is.Not.AnyOf("died lol", "argh died", "dead again"), "a death with a known cause names it");
            }
        Assert.That(deaths.Zip(deaths.Skip(1), (a, b) => b - a), Has.All.GreaterThanOrEqualTo(Field.Memory.DeathRestMs));
    }

    // ---- LOG-031: wordings per bot ------------------------------------------------------------------------------------------------

    [Test]
    public void AFrameKeepsOneWordingPerBotAndVariesAcrossBots()
    {
        Field.Facts one = Facts(id: 1);
        Assert.That(Enumerable.Range(0, 10).Select(_ => Field.Worded(one, "status_zone", "a", "b", "c")).Distinct().Count(), Is.EqualTo(1));
        Assert.That(Enumerable.Range(0, 60).Select(i => Field.Worded(Facts(id: i), "status_zone", "a", "b", "c")).Distinct().Count(), Is.EqualTo(3));
    }

    // ---- LOG-035: dings -------------------------------------------------------------------------------------------------------------

    [Test]
    public void ARealDingIsSaidWithItsGz()
    {
        Field.Said ding = Field.DingLine(Facts(level: 31, dinged: true), new Random(1));
        Assert.That(ding.Kind, Is.EqualTo("ding"));
        Assert.That(ding.Answers, Does.Contain("gz"));
        Assert.That(AutonomousBotChatCoordinator.DingRoom(0, AutonomousBotChatCoordinator.DingGapMs - 1), Is.False);
        Assert.That(AutonomousBotChatCoordinator.DingRoom(0, AutonomousBotChatCoordinator.DingGapMs), Is.True);
    }

    // ---- owner's decision 6: written lines true where the bot stands ------------------------------------------------------------

    private static VC.Line Written(string text, string topic, params string[] needs) =>
        new($"t{text.GetHashCode()}", "ambient", topic, [], "*", [], 1, 50, needs, "", "chat", 1, text);

    [Test]
    public void ShortWrittenLinesTrueWhereTheBotStandsComeThroughOften()
    {
        VC.Line camp = Written("good camp, good xp, no drama", "camp", "at_camp");
        VC.Line dungeon = Written("its so dark down here, i keep seeing things", "dungeon", "in_dungeon");
        VC.Line musing = Written("the bind stone and i are on first name terms", "general");
        Assert.That(AutonomousChatFiller.IsSituated(camp), Is.True);
        Assert.That(AutonomousChatFiller.IsSituated(dungeon), Is.True);
        Assert.That(AutonomousChatFiller.IsSituated(musing), Is.False);
        Assert.That(AutonomousChatFiller.IsSituated(Written("my boss kept me late at work again, ugh", "reallife", "in_town")), Is.False);
        // A "dungeon" line was crafted by its topic and passed one opener in eight; a situated one always passes.
        Assert.That(AutonomousChatFiller.AmbientFilter(true, new FixedRandom(0.99))(dungeon), Is.True);

        var memory = new Field.Memory();
        Func<VC.Line, bool> any = _ => true;
        AutonomousBotChatCoordinator.NoteWritten(memory, musing, 0);
        // The musing's 20 minutes hold other written lines, not a situated one.
        Assert.That(AutonomousBotChatCoordinator.WrittenFits(any, memory, 60_000)(Written("lol anyway", "general")), Is.False);
        Assert.That(AutonomousBotChatCoordinator.WrittenFits(any, memory, 60_000)(camp), Is.True);
        AutonomousBotChatCoordinator.NoteWritten(memory, camp, 60_000);
        Assert.That(AutonomousBotChatCoordinator.WrittenFits(any, memory, 100_000)(dungeon), Is.False, "90 seconds between situated lines");
        Assert.That(AutonomousBotChatCoordinator.WrittenFits(any, memory, 200_000)(dungeon), Is.True);
        Assert.That(AutonomousBotChatCoordinator.WrittenFits(any, memory, 400_000)(camp), Is.False, "once an evening");
        Assert.That(Field.Share("faction"), Is.LessThan(0.9));
    }

    private static VC LoadCorpus()
    {
        var assembly = typeof(VC).Assembly;
        string Read(string suffix)
        {
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using var stream = assembly.GetManifestResourceStream(name);
            using var reader = new System.IO.StreamReader(stream);
            return reader.ReadToEnd();
        }
        return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
    }

    /// <summary>
    /// A measured evening (owner's decision 6): 4.6 hours of Albion /faction openers at the PC's pace (3.5 a minute, session A),
    /// speakers like the PC's roster (85% level 50) in their real places, each opener going to the field or to the written corpus
    /// as the live rules say. Before: the 20-minute rule for every written line, the crafted topics, 8% to the corpus and an opener
    /// lost when no written line fitted. After: the situated lines, 20% to the corpus, the field line when none fits. Logged as
    /// CHAT_WRITTEN_SHARE for the report; asserts only that the written share is well above the PC's 0.6% and no opener is lost.
    /// </summary>
    [Test]
    public void AnEveningHearsTheWrittenCorpusWithoutLosingPace()
    {
        VC corpus = LoadCorpus();
        (int Written, int Field, int Lost) Run(bool after)
        {
            var random = new Random(1865);
            var memory = new Field.Memory();
            var recent = new VC.RecentLines();
            int written = 0, field = 0, lost = 0;
            string[] places = ["at_camp", "in_group", "in_rvr", "in_dungeon", "outdoors", "in_town", "traveling", "just_died", "bag_full", "solo"];
            double[] chance = [0.4, 0.3, 0.3, 0.1, 0.6, 0.15, 0.1, 0.03, 0.05, 0.6];
            for (long now = 1_000; now < (long)(4.6 * 3_600_000); now += 17_000)
            {
                var flags = new HashSet<string>(StringComparer.Ordinal);
                for (int f = 0; f < places.Length; f++)
                    if (random.NextDouble() < chance[f])
                        flags.Add(places[f]);
                int level = random.NextDouble() < 0.85 ? 50 : 20 + random.Next(30);
                var speaker = new VC.Speaker(now, "Sim", eRealm.Albion, Classes[random.Next(Classes.Length)], level, (BotPersonalityType)random.Next(6), flags);
                double share = after ? Field.Share("faction") : 0.92;
                if (random.NextDouble() < share)
                {
                    field++;
                    continue;
                }
                Func<VC.Line, bool> basic = AutonomousChatFiller.AmbientFilter(true, random);
                Func<VC.Line, bool> fits = after ? AutonomousBotChatCoordinator.WrittenFits(basic, memory, now)
                    // The old filter: a crafted topic held a situated line too (bar the 12% crafted draw, left out here).
                    : memory.WrittenAllowed(now) ? line => basic(line) && !(AutonomousChatFiller.IsSituated(line) && AutonomousChatFiller.IsCrafted(line)) &&
                                                           !memory.Rested(null, AutonomousChatFiller.IdeaOf(line), now)
                    : _ => false;
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["me"] = "Sim", ["class"] = "cleric", ["level"] = level.ToString(), ["realm"] = "Albion", ["zone"] = "Lyonesse", ["monster"] = "river spriteling",
                    ["monsters"] = "river spritelings", ["item"] = "chain helm", ["count"] = "3", ["q"] = "", ["keep"] = "beno", ["frontier"] = "Pennine Mountains",
                    ["enemy"] = "mids", ["capital"] = "Camelot", ["dungeon"] = "Keltoi Fogou", ["camp"] = "Keltoi Fogou",
                };
                DateTime clock = new DateTime(2026, 11, 17, 21, 55, 0).AddMilliseconds(now);
                if (corpus.TryPick(new VC.Request("ambient", speaker, slots, recent, clock, random) { Filter = fits }, out VC.Picked picked))
                {
                    written++;
                    recent.Use(eRealm.Albion, picked.Line.Id, clock);
                    if (after)
                        AutonomousBotChatCoordinator.NoteWritten(memory, picked.Line, now);
                    else
                    {
                        memory.Use(null, AutonomousChatFiller.IdeaOf(picked.Line), now);
                        memory.NoteWritten(now);
                    }
                }
                else if (after)
                    field++;
                else
                    lost++;
            }
            return (written, field, lost);
        }
        var before = Run(false);
        var now2 = Run(true);
        double Share((int Written, int Field, int Lost) r) => 100.0 * r.Written / (r.Written + r.Field + r.Lost);
        TestContext.Progress.WriteLine($"CHAT_WRITTEN_SHARE before written={before.Written} field={before.Field} lost={before.Lost} share={Share(before):0.0}% " +
                                       $"after written={now2.Written} field={now2.Field} lost={now2.Lost} share={Share(now2):0.0}%");
        Assert.That(now2.Lost, Is.Zero, "the pace holds: every opener is said");
        Assert.That(Share(now2), Is.GreaterThan(Share(before) * 2).And.GreaterThan(5.0));
    }

    private sealed class FixedRandom(double value) : Random
    {
        public override double NextDouble() => value;
        public override int Next(int maxValue) => 0;
    }

    // ---- owner's decision 12: /broadcast answers ------------------------------------------------------------------------------------

    [Test]
    public void ABroadcastQuestionGetsOneShortAnswerAtMost()
    {
        var steps = new List<Field.Step> { new(1, "me", 2_000), new(0, "where at?", 5_000), new(1, "camelot", 8_000), new(0, "ty", 10_000) };
        Assert.That(AutonomousBotChatCoordinator.ThreadToPlay(steps, true).Select(step => step.Text), Is.EqualTo(new[] { "me" }));
        Assert.That(AutonomousBotChatCoordinator.ThreadToPlay(steps, false), Has.Count.EqualTo(4));
        Assert.That(AutonomousBotChatCoordinator.BroadcastAnswerRoom(0, AutonomousBotChatCoordinator.BroadcastAnswerGapMs - 1), Is.False);
        Assert.That(AutonomousBotChatCoordinator.BroadcastAnswerShare, Is.InRange(0.05, 0.5));
    }
}
