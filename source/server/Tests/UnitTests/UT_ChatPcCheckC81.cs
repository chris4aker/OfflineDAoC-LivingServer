using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Budget = DOL.GS.AutonomousChatBudget;
using Convo = DOL.GS.ChatConversations;
using Coord = DOL.GS.AutonomousBotChatCoordinator;
using Ex = DOL.GS.AutonomousChatExchange;
using Know = DOL.GS.AutonomousChatKnowledge;

namespace DOL.GS.Tests;

/// <summary>
/// The owner's PC log on build c81eb5d, 2026-11-19 09:07-09:42 (Testrunner, Albion 22, in Camelot; scratchpad
/// DIAG-c81-nonraid.md, section 5). One test per bug, each from the PC case. Tests only: none of these fixes has been seen
/// on the owner's PC yet.
/// </summary>
[TestFixture]
public class UT_ChatPcCheckC81
{
    // ---- 1. travel follow-ups and an apostrophe ----------------------------------------------------------------------------

    /// <summary>Zone and region names as the world index (AutonomousChatKnowledge.GetPlaces) keys them: the world's own spelling.</summary>
    private static readonly string[] WorldKeys =
    {
        "Tepok's Mine", "Keltoi Fogou", "Camelot Hills", "Catacombs of Cardova", "City of Camelot", "Black Mtns. South",
        "Campacorentin Forest", "Pennine Mountains", "Salisbury Plains", "Hadrian's Wall", "Avalon Marsh",
    };

    [Test]
    public void TheTepoksMineConversationFromThePcFindsThePlace()
    {
        // 09:19:53 /faction "where do i level at 22?" -> Sven "Tepok's Mine for 22 bring a group"; 09:20:32 "is it far?" ->
        // "no clue sry"; 09:21:22 "how do i get there?" -> "hmm no clue".
        const string room = "faction:1";
        var c = new Convo();
        c.NotePlayer("Testrunner", room, "where do i level at 22?", 0);
        c.NoteBot("Testrunner", 7, "Sven", room, "Tepok's Mine for 22 bring a group", 9_000);

        string far = Convo.Resolve("is it far?", c.In("Testrunner", room, 39_000));
        Assert.That(far, Is.EqualTo("is tepoks mine far?"), "the conversation's place is the catalog alias, as on the PC");
        Assert.That(Know.AsksHowFar(far), Is.True);
        Assert.That(Know.ContainsPhrase(far, "Tepok's Mine"), Is.False, "c81eb5d's match: the apostrophe hid the place");
        Assert.That(Know.NamesPlace(far, "Tepok's Mine"), Is.True);
        Assert.That(Know.NamedPlaceKey(far, WorldKeys, AutonomousChatPlaces.All), Is.EqualTo("Tepok's Mine"));

        string there = Convo.Resolve("how do i get there?", c.In("Testrunner", room, 89_000));
        Assert.That(there, Is.EqualTo("how do i get to tepoks mine?"));
        Assert.That(Know.LooksLikeLocationQuestion(there), Is.True, "it goes to the location answer");
        Assert.That(Know.NamedPlaceKey(there, WorldKeys, AutonomousChatPlaces.All), Is.EqualTo("Tepok's Mine"));

        // As typed with the apostrophe, or the bot's short alias, too; the longest name still wins; nothing named is null.
        Assert.That(Know.NamedPlaceKey("how do i get to Tepok's Mine?", WorldKeys, AutonomousChatPlaces.All), Is.EqualTo("Tepok's Mine"));
        Assert.That(Know.NamedPlaceKey("is tepoks far?", WorldKeys, AutonomousChatPlaces.All), Is.EqualTo("Tepok's Mine"));
        Assert.That(Know.NamedPlaceKey("is camelot hills far?", WorldKeys, AutonomousChatPlaces.All), Is.EqualTo("Camelot Hills"));
        Assert.That(Know.NamedPlaceKey("is it far?", WorldKeys, AutonomousChatPlaces.All), Is.Null);
        Assert.That(Know.NamedPlaceKey("is keltoi fogou far?", WorldKeys, AutonomousChatPlaces.All), Is.EqualTo("Keltoi Fogou"),
            "the c81 case still works");
    }

    [Test]
    public void EveryPlaceTheBotsNameIsFoundByEachOfItsNames()
    {
        // The sweep: every catalog place that is itself a zone (its key is one of its zones), asked about by any of its
        // aliases as the conversation would complete it, finds that zone's world key. Before: tepoks mine, hadrians wall,
        // odins gate, nisses lair, iarnvidiurs lair, worlds end and every short alias (cardova, pennine, salisbury...) missed.
        var zonal = AutonomousChatPlaces.All
            .Where(p => p.Zones != null && p.Zones.Any(z => AutonomousChatQuestions.Normalize(z) == AutonomousChatQuestions.Normalize(p.Key)))
            .ToArray();
        Assert.That(zonal.Length, Is.GreaterThan(20));
        string[] keys = AutonomousChatPlaces.All.Where(p => p.Zones != null).SelectMany(p => p.Zones).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var missedBefore = new List<string>();
        var missed = new List<string>();
        foreach (AutonomousChatPlaces.Place place in zonal)
        {
            string zone = place.Zones.First(z => AutonomousChatQuestions.Normalize(z) == AutonomousChatQuestions.Normalize(place.Key));
            foreach (string alias in place.Aliases.Append(place.Key))
            {
                string asked = $"how do i get to {AutonomousChatQuestions.Normalize(alias)}?";
                if (!keys.Any(key => key.Length >= 3 && Know.ContainsPhrase(asked, key)))
                    missedBefore.Add(alias);
                // PlaceNamed's own rule: an alias under three letters ("df") names nothing.
                if (AutonomousChatQuestions.Normalize(alias).Length < 3)
                    continue;
                string found = Know.NamedPlaceKey(asked, keys, AutonomousChatPlaces.All);
                // This zone, or a world name that is the alias itself ("caledon": the battleground's own zone).
                if (!string.Equals(found, zone, StringComparison.OrdinalIgnoreCase) &&
                    !(found != null && AutonomousChatQuestions.Normalize(found) == AutonomousChatQuestions.Normalize(alias)))
                    missed.Add($"{alias} -> {found ?? "null"} (want {zone})");
            }
        }
        TestContext.Out.WriteLine("missed before: " + string.Join(", ", missedBefore));
        Assert.That(missedBefore, Does.Contain("tepoks mine").And.Contain("hadrians wall").And.Contain("worlds end"), "the PC failure class");
        Assert.That(missed, Is.Empty);
    }

    // ---- 2. the floor keeps help calls and lines said to everyone ------------------------------------------------------

    [TestCase("can someone help me in Camelot Hills?")]
    [TestCase("anyone know where to get a good shield?")]
    [TestCase("any1 up for keltoi")]
    [TestCase("anybody around camelot hills")]
    [TestCase("can someone help me kill the named in keltoi fogou")]
    public void AHelpCallOrALineToEveryoneDoesNotStayWithTheFloorBot(string line)
    {
        // 09:22:08: eligible=1 (Sven, the floor bot), who refused (in_bot_group); nobody else could answer or offer.
        Assert.That(Coord.KeepsTheFloor(line, AutonomousChatIntentModel.Predict(line)), Is.False, line);
    }

    [TestCase("is it far?")]
    [TestCase("how do i get there?")]
    [TestCase("what level are the mobs there")]
    [TestCase("thanks, what about after 24?")]
    public void AFollowUpStillGoesOnWithTheFloorBot(string line) =>
        Assert.That(Coord.KeepsTheFloor(line, AutonomousChatIntentModel.Predict(line)), Is.True, line);

    [Test]
    public void AllCallWordsAreWholeWords()
    {
        Assert.That(Coord.SaidToEveryone("Anyone know where Tepok's Mine is?"), Is.True);
        Assert.That(Coord.SaidToEveryone("ne1 got a rez"), Is.True);
        Assert.That(Coord.SaidToEveryone("someones armor looks great"), Is.False, "not a call");
        Assert.That(Coord.SaidToEveryone("is it far?"), Is.False);
    }

    // ---- 3. a /broadcast question answered by /tell -------------------------------------------------------------------

    [Test]
    public void ABroadcastQuestionIsAnsweredInBroadcastOrSayNeverByTell()
    {
        // 09:23:42 /broadcast "anyone know where to get a good shield?" -> Sven by /tell 19 s later.
        Assert.That(Coord.OpenChannelAnswerRoute("broadcast", true, false), Is.EqualTo("broadcast"));
        Assert.That(Coord.OpenChannelAnswerRoute("broadcast", true, true), Is.EqualTo("broadcast"), "the channel it was asked in");
        Assert.That(Coord.OpenChannelAnswerRoute("broadcast", false, true), Is.EqualTo("say"), "over the ceiling: in /say beside the player");
        Assert.That(Coord.OpenChannelAnswerRoute("broadcast", false, false), Is.Null, "nowhere the player hears: nothing, no tell");
        Assert.That(Coord.OpenChannelAnswerRoute("chatgroup", false, false), Is.EqualTo("tell"), "a /chatgroup line keeps its tell");
        Assert.That(Coord.OpenChannelAnswerRoute("chatgroup", true, true), Is.EqualTo("tell"));

        Assert.That(Coord.OpenChannelAnswerDelayMs("broadcast", 19_000), Is.EqualTo(Coord.DirectTypingCapMs));
        Assert.That(Coord.OpenChannelAnswerDelayMs("broadcast", 6_000), Is.EqualTo(6_000));
        Assert.That(Coord.OpenChannelAnswerDelayMs("chatgroup", 19_000), Is.EqualTo(19_000));
    }

    // ---- 4. /lfg at a level with no bot parties ----------------------------------------------------------------------

    [Test]
    public void AnLfgNobodyCanAnswerGetsTheHonestNoneNearReply()
    {
        // 09:26:43 /lfg "lfg 22, any group": no_party_fits, no Albion solo bot within 2 levels of 22, nothing said.
        Assert.That(AutonomousLivingServer.LfgNeedsNoneNearReply(0, 0), Is.True);
        Assert.That(AutonomousLivingServer.LfgNeedsNoneNearReply(1, 3), Is.False, "a duo offer went out");
        Assert.That(AutonomousLivingServer.LfgNeedsNoneNearReply(0, 2), Is.False, "bots near the level exist: \"nobody\" would be untrue");

        var random = new Random(26);
        for (int i = 0; i < 30; i++)
        {
            string reply = Coord.NoneNearLevelReply(AutonomousChatLiveAnswers.LevelAsked("lfg 22, any group", 22), null, random);
            Assert.That(reply, Is.Not.Empty);
            Assert.That(ChatPromiseRules.SaysComing(reply), Is.False, reply);
        }
    }

    [Test]
    public void NoPartyFitsIsLoggedOncePerAsk()
    {
        // 09:26:43, :47 and :52: the ask and two pulses each logged not_sent no_party_fits.
        var memory = new BotLedGroups.InviteMemory(0);
        memory.Asked(1_000_000);
        Assert.That(memory.FirstNoFitThisAsk(), Is.True, "the ask");
        Assert.That(memory.FirstNoFitThisAsk(), Is.False, "the pulse 4 s later");
        Assert.That(memory.FirstNoFitThisAsk(), Is.False, "the pulse 9 s later");
        memory.Asked(1_300_000);
        Assert.That(memory.FirstNoFitThisAsk(), Is.True, "a new ask is logged again");
    }

    // ---- 5. /faction under the owner's 5-7 ---------------------------------------------------------------------------

    private static Budget.Settings Settings => Budget.Parse(Budget.BuiltIn());

    /// <summary>c81eb5d's budget: the built-in file without the minimum.</summary>
    private static Budget.Settings Before => Budget.Parse(Budget.BuiltIn().Replace("\"faction_min_per_minute\": 5", "\"faction_min_per_minute\": 0"));

    [Test]
    public void TheFactionBandKeepsTheOwnersMinimumAtAnyHour()
    {
        Assert.That(Settings.FactionPaceMin, Is.EqualTo(5.0), "chat-budget.json pace.faction_min_per_minute");
        Assert.That(Budget.Parse("{}").FactionPaceMin, Is.EqualTo(5.0), "an older copy of the file gets the owner's minimum");
        // 09h, 960 Albion bots, rate 2: c81eb5d's band was about 2.3 / 4.7 / 11.7.
        var before = Budget.Band(Before, Budget.Channel.Faction, 960, 9, false, 2.0);
        Assert.That(before.Floor, Is.LessThan(5.0));
        Assert.That(before.Target, Is.LessThan(5.0), "c81eb5d: a target under the owner's 5");
        foreach (int hour in new[] { 3, 9, 14, 21 })
        {
            var band = Budget.Band(Settings, Budget.Channel.Faction, 960, hour, false, 2.0);
            Assert.That(band.Floor, Is.GreaterThanOrEqualTo(5.0), $"{hour}h floor");
            Assert.That(band.Target, Is.GreaterThanOrEqualTo(5.5), $"{hour}h target");
            Assert.That(band.Ceiling, Is.GreaterThanOrEqualTo(band.Target));
        }
        Assert.That(Budget.FactionPace(Settings, 2.0).Max, Is.EqualTo(7.0), "the cap stays 7");
        // Under the floor the realm's bots try as often as they can; c81eb5d's pressure at 2.7 ambient lines read about 2.
        var nine = Budget.Band(Settings, Budget.Channel.Faction, 960, 9, false, 2.0);
        Assert.That(Budget.Pressure(2.7, nine), Is.EqualTo(Budget.MaxPressure));
        Assert.That(Budget.Pressure(2.7, before.Target), Is.LessThan(2.0));
        Assert.That(Budget.Pressure(6.0, nine), Is.EqualTo(Budget.Pressure(6.0, nine.Target)), "over the floor: as before");
        // "How much bots talk" keeps its meaning: 1 can go under 5, 0 only answers, 3 is busier.
        Assert.That(Budget.Band(Settings, Budget.Channel.Faction, 960, 9, false, 1.0).Floor, Is.EqualTo(2.5).Within(1e-9));
        Assert.That(Budget.Band(Settings, Budget.Channel.Faction, 960, 9, false, 0.0).Target, Is.EqualTo(0.0));
        Assert.That(Budget.Band(Settings, Budget.Channel.Faction, 960, 9, false, 3.0).Floor, Is.GreaterThanOrEqualTo(7.5 - 1e-9));
        // A small realm isn't pushed to a full one's pace (the scenes' roster scale).
        Assert.That(Budget.Band(Settings, Budget.Channel.Faction, 10, 9, false, 2.0).Floor, Is.LessThan(1.5));
        // Other channels are untouched.
        Assert.That(Budget.Band(Settings, Budget.Channel.Say, 960, 9, false, 2.0), Is.EqualTo(Budget.Band(Before, Budget.Channel.Say, 960, 9, false, 2.0)));
    }

    /// <summary>
    /// One realm's /faction for an hour at 09h with the player online and no scene (c81eb5d 09:30-09:40: 3.9 a minute), 960
    /// bots a realm, rate 2: the real ambient gate (ChannelMeter.TryStart) and pace rule (OpenerFitsPace), a bot with an
    /// opener only now and then (on the PC openers came at about half the target), its pressure (Budget.Pressure) raising how
    /// often a bot tries (with <paramref name="floorPressure"/>, the full pressure under /faction's floor), six in ten openers
    /// answered 4-10 s later, and the player's question every three minutes answered 8 s later with the floor held. The opener
    /// supply (3.5% a second at pressure 1) is set so c81eb5d's budget reads about 4.2 a minute, near the PC's 3.9; with a
    /// thinner supply (2%, 3.6 before) the fix reaches only about 4.6, so the PC check decides. Deterministic.
    /// </summary>
    private static (double PerMinute, int Busiest) QuietMorning(Budget.Settings settings, int seed, double supply = 0.035, bool floorPressure = true)
    {
        var meter = new Budget.ChannelMeter();
        var band = Budget.Band(settings, Budget.Channel.Faction, 960, 9, false, 2.0);
        var pace = Budget.FactionPace(settings, 2.0);
        var random = new Random(seed);
        const long key = 1;
        long start = 1_000_000, end = start + 60 * 60_000L;
        var lines = new List<long>();
        var answers = new List<long>();
        long playerAnswerAt = 0, playerFloor = 0;
        for (long now = start; now < end; now += 1_000)
        {
            if ((now - start) % 180_000 == 90_000)
            {
                playerAnswerAt = now + 8_000;
                playerFloor = now + 10_000;
            }
            if (playerAnswerAt > 0 && now >= playerAnswerAt)
            {
                meter.Count(Budget.Channel.Faction, key, now, Budget.LineKind.Reply);
                lines.Add(now);
                playerAnswerAt = 0;
                playerFloor = 0;
            }
            for (int i = answers.Count - 1; i >= 0; i--)
            {
                if (answers[i] > now || playerFloor > now)
                    continue;
                answers.RemoveAt(i);
                meter.Count(Budget.Channel.Faction, key, now, Budget.LineKind.Reply);
                lines.Add(now);
            }
            double measured = meter.AmbientPerMinute(Budget.Channel.Faction, key, now, band.Target);
            double pressure = floorPressure ? Budget.Pressure(measured, band) : Budget.Pressure(measured, band.Target);
            if (playerFloor > now || random.NextDouble() >= supply * pressure)
                continue;
            Budget.Verdict verdict = meter.TryStart(Budget.Channel.Faction, key, now, band, random);
            if (verdict is not (Budget.Verdict.Speak or Budget.Verdict.SpeakUnderFloor) ||
                !Budget.OpenerFitsPace(meter.LinesIn(Budget.Channel.Faction, key, now, Budget.PaceWindowMs), answers.Count, pace.Max))
                continue;
            meter.Count(Budget.Channel.Faction, key, now);
            lines.Add(now);
            if (random.NextDouble() < 0.6)
                answers.Add(now + 4_000 + random.Next(6_000));
        }
        int busiest = 0;
        for (int i = 0, j = 0; i < lines.Count; i++)
        {
            while (lines[i] - lines[j] >= 60_000)
                j++;
            busiest = Math.Max(busiest, i - j + 1);
        }
        // The first ten minutes fill the meter's window; the rest is the settled pace.
        long settled = start + 10 * 60_000L;
        return (lines.Count(t => t >= settled) * 60_000.0 / (end - settled), busiest);
    }

    [Test]
    public void AQuietMorningHourWithThePlayerOnlineLandsAtFiveToSeven()
    {
        foreach (int seed in new[] { 81, 1, 2, 3, 919 })
        {
            (double before, _) = QuietMorning(Before, seed, floorPressure: false);
            (double after, int busiest) = QuietMorning(Settings, seed);
            TestContext.Out.WriteLine($"seed {seed}: before {before:0.00}/min, after {after:0.00}/min, busiest minute {busiest}");
            Assert.That(before, Is.LessThan(5.0), $"seed {seed}: c81eb5d's budget settles under 5 (the PC: 3.9)");
            Assert.That(after, Is.InRange(5.0, 7.0), $"seed {seed}: the owner's 5-7");
            Assert.That(busiest, Is.LessThanOrEqualTo(8), $"seed {seed}: the cap of 7 holds (a player's answer, never held, aside)");
        }
    }

    // ---- 6. a storyline beat between the player's question and its answer ---------------------------------------------

    [Test]
    public void AStorylineBeatWaitsForThePlayersAnswer()
    {
        // 09:22:57 "how do i get to breifine?"; 09:23:07 Ealhswith's storyline beat; 09:23:09 Sven's answer.
        string lane = Ex.FactionKey(eRealm.Albion) + ":c81test";
        long asked = 5_000_000, answerDue = asked + 12_000;
        Ex.HoldForPlayer(lane, asked, answerDue + 2_000);
        long beatAt = asked + 10_000;
        long held = Ex.SceneWaitsForPlayerUntil(Ex.PlayerFloorUntil(lane), beatAt);
        Assert.That(held, Is.GreaterThan(beatAt), "the beat waits");
        Assert.That(held - beatAt, Is.LessThanOrEqualTo(Ex.PlayerFloorRetryMs), "and looks again within a second");
        Ex.ReleasePlayerFloor(lane);   // the answer is said at 09:23:09
        Assert.That(Ex.SceneWaitsForPlayerUntil(Ex.PlayerFloorUntil(lane), answerDue), Is.EqualTo(0), "then the scene goes on");
        // Never held for good: the hold is capped at PlayerFloorMaxMs.
        Ex.HoldForPlayer(lane, asked, asked + 10 * 60_000);
        Assert.That(Ex.SceneWaitsForPlayerUntil(Ex.PlayerFloorUntil(lane), asked + Ex.PlayerFloorMaxMs), Is.EqualTo(0));
        Ex.ReleasePlayerFloor(lane);
    }

    // ---- 9. the equipment upgrade line -------------------------------------------------------------------------------

    [Test]
    public void TheUpgradeLineCarriesTheGainThatDecidedIt()
    {
        // 109 of 200 AUTONOMOUS_EQUIPMENT_UPGRADE lines read newValue < oldValue: the set score decided, the line printed
        // EquipmentValue.
        Assert.That(AutonomousBotEconomy.UpgradeValues(640, 612, 37), Is.EqualTo("oldValue=640 newValue=612 gain=37 decidedBy=set"));
        Assert.That(AutonomousBotEconomy.UpgradeValues(500, 540, null), Is.EqualTo("oldValue=500 newValue=540 gain=40 decidedBy=value"));
    }
}
