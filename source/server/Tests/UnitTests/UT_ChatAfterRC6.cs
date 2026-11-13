using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Engagement = DOL.GS.AutonomousChatEngagement;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Market = DOL.GS.AutonomousChatMarket;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// 1.1.3, chat after RC6 on the owner's PC (2026-11-09, 10:44-11:00): death stories and death lines that ignored the killer
/// ("got caught by the bridge" after a shard golem, "had 2 on me" after one enemy bot, "the guards got me" after an enemy bot,
/// "elder beeches x3" after two, "died to frontier patrol near death stalkers"), a rez request answered "oof", "busy tonight"
/// at 10:44 AM, "ok back" from a bot that had been nowhere, "how many bubbles?" to "lfg rvr 50 friar", and a duo offer from a
/// friend about to ride off.
/// </summary>
[TestFixture]
public class UT_ChatAfterRC6
{
    private static readonly Field.WorldView Frontier = new(["Caer Benowyc"], [], [], [], 0, 0, "Forest Sauvage");

    private static Field.Facts Facts(long id, string cls, int level, string zone = "Lyonesse", bool rvr = false, int hour = 20) =>
        new(id, eRealm.Albion, cls, level, zone, null, rvr, false, false, false, 1, false, false, false, false, "large skeleton", null, 0, false,
            null, 3, hour, Field.Fills(cls, "healer"), Frontier);

    /// <summary>The lines of many death stories; <paramref name="tellerOnly"/>: only the teller's (a third's "stick with the zerg lol" is advice, not a claim).</summary>
    private static IEnumerable<string> Stories(Field.Facts teller, int count, int seed, bool tellerOnly = false)
    {
        var random = new Random(seed);
        for (int i = 0; i < count; i++)
            foreach (Field.Step step in Field.Story("death", teller with { Id = i }, Facts(i + 1, "Cleric", teller.Level), Facts(i + 2, "Wizard", teller.Level), random))
                if (!tellerOnly || step.Who == 0)
                    yield return step.Text;
    }

    private static readonly Regex GankWords = new(@"\b(gank\w*|zerg\w*|bridge|milegate|caught|farmed|stealth\w*|assassin|jumped|picked off|guards?|reds|grp got)\b");

    // ---- F2: what killed the bot --------------------------------------------------------------------------------------------

    [Test]
    public void AFrontierDeathToAMonsterIsToldWithTheMonsterAndNoGank()
    {
        Field.Facts teller = Facts(1, "Scout", 32, "Hadrian's Wall", rvr: true) with { Monster = "shard golem", KilledBy = "monster", Attackers = 1 };
        List<string> lines = Stories(teller, 400, 11).ToList();
        foreach (string text in lines)
            Assert.That(GankWords.IsMatch(text), Is.False, text);
        Assert.That(lines.Any(text => text.Contains("shard golems")), Is.True, "the story names the monster that killed it");
    }

    /// <summary>RC8 on the owner's PC: "aggroed the guards, dead" drew "stick with the zerg".</summary>
    [Test]
    public void AGuardOrSmallDeathDrawsNoCrowdRiff()
    {
        var crowd = new Regex(@"zerg|full of them|everywhere|sit on that spot");
        Field.Facts guard = Facts(1, "Sorcerer", 30, "Caledonia", rvr: true) with { KilledBy = "guard", Attackers = 2 };
        Field.Facts two = Facts(1, "Sorcerer", 30, "Caledonia", rvr: true) with { KilledBy = "bot", Attackers = 2, KillerClass = "Hero" };
        foreach (Field.Facts teller in new[] { guard, two })
        {
            var random = new Random(7);
            for (int i = 0; i < 300; i++)
                foreach (Field.Step step in Field.Story("death", teller with { Id = i }, Facts(i + 1, "Cleric", 30), Facts(i + 2, "Wizard", 30), random))
                    if (step.Who == 2)
                        Assert.That(crowd.IsMatch(step.Text), Is.False, step.Text);
        }
    }

    /// <summary>RC8 on the owner's PC: a group wiped by enemy bots told "wiped on adds" and "adds linked, all of them".</summary>
    [Test]
    public void AWipeToEnemiesIsToldAsOne()
    {
        var random = new Random(5);
        Field.Facts teller = Facts(1, "Armsman", 33, "Caledonia", rvr: true) with { KilledBy = "bot", Attackers = 2 };
        var monsterWords = new Regex(@"\b(adds|linked|respawned|pulled 3|the named|a train)\b");
        for (int i = 0; i < 300; i++)
            foreach (Field.Step step in Field.Story("wipe", teller with { Id = i }, Facts(i + 1, "Cleric", 33), Facts(i + 2, "Wizard", 33), random))
                Assert.That(monsterWords.IsMatch(step.Text), Is.False, step.Text);
        Field.Facts pve = Facts(1, "Armsman", 33, "Lyonesse") with { KilledBy = "monster", Monster = "spriggarn", Attackers = 4 };
        Assert.That(Enumerable.Range(0, 200).SelectMany(i => Field.Story("wipe", pve with { Id = i }, Facts(i + 1, "Cleric", 33), null, random))
            .Any(step => step.Text.Contains("spriggarn")), Is.True, "a wipe to monsters still names them");
    }

    [Test]
    public void AnEnemyKillWithOneAttackerClaimsNoCrowd()
    {
        var crowd = new Regex(@"had \d on me|zerg|ran into|of them|\d v \d|full grp|like \d|train of|\d or \d|\d+\+|grp of|too many|a fg|they were waiting|stealthers|2 assassins");
        Field.Facts teller = Facts(1, "Mentalist", 40, "Hadrian's Wall", rvr: true) with { Monster = null, KilledBy = "bot", Attackers = 1, KillerClass = "Hero" };
        foreach (string text in Stories(teller, 600, 12, tellerOnly: true))
        {
            Assert.That(crowd.IsMatch(text), Is.False, text);
            Assert.That(text, Does.Not.Contain("guard"), "an enemy bot is no guard: " + text);
            Assert.That(text, Does.Not.Contain("stealth").And.Not.Contain("assassin"), "a hero can't stealth: " + text);
            Assert.That(text, Does.Not.Contain("bubble"), "a death to an enemy costs no xp: " + text);
        }
    }

    [Test]
    public void AnEnemyKillInTheFrontierIsStillToldAsAFrontierDeath()
    {
        Field.Facts teller = Facts(1, "Scout", 50, "Emain Macha", rvr: true) with { KilledBy = "player", Attackers = 3 };
        var random = new Random(13);
        int told = 0, rvr = 0;
        for (int i = 0; i < 300; i++)
        {
            List<Field.Step> story = Field.Story("death", teller with { Id = i }, Facts(i + 1, "Cleric", 50), null, random);
            if (story.Count == 0)
                continue;
            told++;
            Assert.That(story.Any(step => step.Text.Contains("skeleton") || step.Text.Contains("adds")), Is.False, story[0].Text);
            if (GankWords.IsMatch(story[0].Text) || story[0].Text.Contains("died to") || story[0].Text.Contains("got me") || story[0].Text.Contains("dead"))
                rvr++;
        }
        Assert.That(told, Is.GreaterThan(200));
        Assert.That(rvr, Is.GreaterThan(told / 2), "frontier openers");
    }

    [Test]
    public void AnEnemyKillOutsideTheFrontierIsNoMonsterDeath()
    {
        Field.Facts teller = Facts(1, "Scout", 40, "Darkness Falls") with { KilledBy = "bot", Attackers = 2, KillerClass = "Shadowblade" };
        foreach (string text in Stories(teller, 200, 14))
            Assert.That(text, Does.Not.Contain("skeleton").And.Not.Contain("adds"), text);
    }

    /// <summary>RC6: Thaezza, killed in Caledonia by one of two enemy bots, opened "the guards got me on the bridge".</summary>
    [Test]
    public void GuardLinesOnlyForAGuardKill()
    {
        Field.Facts byBot = Facts(1, "Cabalist", 33, "Caledonia", rvr: true) with { Monster = null, KilledBy = "bot", Attackers = 2, KillerClass = "Warrior" };
        foreach (string text in Stories(byBot, 400, 15))
            Assert.That(Regex.IsMatch(text, @"\bguards?\b"), Is.False, text);
        Field.Facts byGuard = Facts(1, "Cabalist", 33, "Hadrian's Wall", rvr: true) with { Monster = null, KilledBy = "guard", Attackers = 1 };
        var random = new Random(16);
        int guard = 0;
        for (int i = 0; i < 100; i++)
            if (Field.Story("death", byGuard with { Id = i }, Facts(i + 1, "Cleric", 33), null, random) is { Count: > 0 } story && story[0].Text.Contains("guard"))
                guard++;
        Assert.That(guard, Is.GreaterThan(30), "a guard kill is told as one");
    }

    /// <summary>RC6: two elder beeches killed Grelliard and the story said "elder beeches x3, no chance".</summary>
    [Test]
    public void AMonsterStoryCountsWhatWasReallyThere()
    {
        Field.Facts two = Facts(1, "Friar", 24) with { Monster = "elder beech", KilledBy = "monster", Attackers = 2 };
        List<string> lines = Stories(two, 600, 17).ToList();
        Assert.That(lines.Any(text => text.Contains("x3")), Is.False);
        Assert.That(lines.Any(text => text.Contains("elder beeches x2")), Is.True, "the count it had");
        Field.Facts one = two with { Attackers = 1 };
        foreach (string text in Stories(one, 600, 18))
            Assert.That(Regex.IsMatch(text, @"\bx\d|train of|pulled two|too many"), Is.False, text);
    }

    /// <summary>RC6: "ran into a grp right outside the gate" and "jumped right outside the gate heh" within two minutes.</summary>
    [Test]
    public void AFrontierDeathPlaceIsNotToldTwiceInHalfAnHour()
    {
        var memory = new Field.Memory();
        var random = new Random(19);
        var heard = new Dictionary<string, long>();
        long now = 1_000_000;
        int placed = 0;
        for (int i = 0; i < 120; i++, now += 2 * 60_000)
        {
            Field.Facts teller = Facts(i, "Scout", 45, "Hadrian's Wall", rvr: true) with { KilledBy = "bot" };
            List<Field.Step> story = Field.Story("death", teller, Facts(i + 1, "Cleric", 45), null, random, memory, null, now);
            if (story.Count == 0 || Field.DeathPlace(story[0].Text) is not { } place)
                continue;
            placed++;
            Assert.That(!heard.TryGetValue(place, out long at) || now - at >= 30 * 60_000, Is.True, $"\"{place}\" again after {(now - at) / 60_000} min: {story[0].Text}");
            heard[place] = now;
        }
        Assert.That(placed, Is.GreaterThan(10), "openers still name places");
    }

    [Test]
    public void ARealmHearsOneDeathStoryInSevenMinutes()
    {
        Assert.That(AutonomousBotChatCoordinator.DeathStoryRoom(0, 90_000), Is.False);
        Assert.That(AutonomousBotChatCoordinator.DeathStoryRoom(0, 6 * 60_000), Is.False);
        Assert.That(AutonomousBotChatCoordinator.DeathStoryRoom(0, AutonomousBotChatCoordinator.DeathStoryGapMs), Is.True);
        Assert.That(AutonomousBotChatCoordinator.DeathStoryRoom(long.MinValue / 2, 0), Is.True, "the first story of the evening");
    }

    [TestCase("had 2 on me before i could run", 1, false)]
    [TestCase("had 2 on me before i could run", 2, true)]
    [TestCase("zerged at the milegate", 1, false)]
    [TestCase("zerged at the milegate", 2, false)]
    [TestCase("zerged at the milegate", 5, true)]
    [TestCase("3 of them on me", 3, true)]
    [TestCase("3 of them on me", 2, false)]
    [TestCase("4 v 1, dead", 4, true)]
    [TestCase("1 v 5 lol, dead", 1, false)]
    [TestCase("a zerg, 20+", 6, false)]
    [TestCase("3 or 4", 4, true)]
    [TestCase("a grp of 6 or so", 7, true)]
    [TestCase("just one stealther lol", 2, false)]
    [TestCase("just one stealther lol", 1, true)]
    [TestCase("elder beeches x3, no chance", 2, false)]
    [TestCase("elder beeches x2, no chance", 2, true)]
    [TestCase("never saw them coming", 1, true)]
    [TestCase("anything goes", 0, true)]
    [TestCase("zerged at the milegate", 0, true)]
    public void ADeathLineFitsTheAttackerCount(string text, int attackers, bool fits) =>
        Assert.That(Field.FitsAttackers(text, attackers), Is.EqualTo(fits));

    [TestCase("frontier patrol near death stalkers", null)]
    [TestCase("grind camp large skeletons", null)]
    [TestCase("Final encounter staging", null)]
    [TestCase("group rendezvous", null)]
    [TestCase("raid muster", null)]
    [TestCase("keep siege", null)]
    [TestCase("shard golem", "shard golem")]
    [TestCase("elder beech", "elder beech")]
    [TestCase("Briaelaewen", null)]
    public void AnObjectiveIsNoMonster(string target, string kind) => Assert.That(Field.MonsterKind(target, false), Is.EqualTo(kind));

    /// <summary>RC6: Leofwinee, killed by one enemy bot, said "died to frontier patrol near death stalkers ugh".</summary>
    [Test]
    public void AFieldDeathLineToAnEnemyNamesNoMonster()
    {
        Field.Facts dead = Facts(1, "Paladin", 50, "Hadrian's Wall", rvr: true) with
        {
            JustDied = true, Monster = "death stalker", KilledBy = "bot", Attackers = 1, KillerClass = "Nightshade", Dead = false,
        };
        int deaths = 0;
        for (int i = 0; i < 3_000 && deaths < 60; i++)
        {
            Field.Said said = Field.Line("faction", dead with { Id = i }, null, new Random(i));
            if (said?.Kind != "death")
                continue;
            deaths++;
            Assert.That(said.Text, Does.Not.Contain("stalker").And.Not.Contain("train").And.Not.Contain("bubble").And.Not.Contain("rez"), said.Text);
        }
        Assert.That(deaths, Is.GreaterThan(10));
    }

    /// <summary>RC6: Zoe's "rez in murdaigean?" was answered "oof".</summary>
    [Test]
    public void ARezRequestIsAnsweredAsOne()
    {
        Field.Facts dead = Facts(1, "Scout", 30) with { JustDied = true, Dead = true };
        int asked = 0;
        for (int i = 0; i < 6_000 && asked < 40; i++)
        {
            Field.Said said = Field.Line("faction", dead with { Id = i }, null, new Random(i));
            if (said?.Kind != "death" || !said.Text.Contains("rez"))
                continue;
            asked++;
            Assert.That(said.Answers, Has.None.Matches<string>(a => Regex.IsMatch(a, @"^(rip|oof|ouch|lol rip)$")));
            Assert.That(said.Unanswered, Has.None.Contains("got a rez"));
            for (int k = 0; k < 20; k++)
            {
                if (Field.Answer(said, Facts(50 + k, "Wizard", 30), null, new Random(k)) is { } plain)
                    Assert.That(plain, Does.Not.Contain("omw").And.Not.Contain("where").And.Not.Contain("coming"), "a wizard can't rez");
            }
            Assert.That(Enumerable.Range(0, 30).Select(k => Field.Answer(said, Facts(80 + k, "Cleric", 30), null, new Random(k)))
                .Any(text => text != null && (text.Contains("omw") || text.Contains("where") || text.Contains("coming"))), Is.True, "a cleric can");
        }
        Assert.That(asked, Is.GreaterThan(5));
        // A bot already up asks nobody for a rez.
        for (int i = 0; i < 2_000; i++)
            if (Field.Line("faction", dead with { Id = i, Dead = false }, null, new Random(i)) is { Kind: "death" } said)
                Assert.That(said.Text, Does.Not.Contain("rez"));
    }

    // ---- F3: the time of day, away and back ------------------------------------------------------------------------------------

    [TestCase("busy tonight", 10, "busy today")]
    [TestCase("busy tonight", 21, "busy tonight")]
    [TestCase("raid this evening?", 9, "raid today?")]
    [TestCase("done for tonite", 14, "done for today")]
    [TestCase("gone since this morning", 15, "gone since earlier")]
    [TestCase("gone since this morning", 9, "gone since this morning")]
    [TestCase("third time tonight", 2, "third time tonight")]
    [TestCase("anyone heading to the frontier tonight? heh", 11, "anyone heading to the frontier today? heh")]
    [TestCase("nice to see the guild on tonight", 23, "nice to see the guild on tonight")]
    [TestCase("first night in the frontier how do i even start", 15, "first time in the frontier how do i even start")]
    [TestCase("first night in the frontier, keep me alive pls", 22, "first night in the frontier, keep me alive pls")]
    public void ALineFitsTheTimeOfDay(string text, int hour, string said) => Assert.That(VC.ForTimeOfDay(text, hour), Is.EqualTo(said));

    private static readonly Regex Evening = new(@"\b(tonight|tonite|this evening|evening)\b");

    [Test]
    public void ByDayNoFieldLineThreadOrStorySaysTonight()
    {
        var memory = new Field.Memory();
        for (int i = 0; i < 4_000; i++)
        {
            var random = new Random(i);
            Field.Facts facts = Facts(i, i % 2 == 0 ? "Scout" : "Cleric", 20 + i % 31, i % 3 == 0 ? "Hadrian's Wall" : "Lyonesse", rvr: i % 3 == 0, hour: 10);
            foreach (string channel in new[] { "faction", "guild", "say" })
            {
                Field.Said said = Field.Line(channel, facts, null, random);
                if (said == null)
                    continue;
                Assert.That(Evening.IsMatch(said.Text), Is.False, said.Text);
                foreach (string answer in said.Answers ?? [])
                    Assert.That(Evening.IsMatch(answer), Is.False, answer);
                foreach (Field.Step step in Field.Thread(said, facts, Facts(i + 1, "Friar", facts.Level, hour: 10), Facts(i + 2, "Wizard", facts.Level, hour: 10),
                             memory, random))
                    Assert.That(Evening.IsMatch(step.Text), Is.False, step.Text);
            }
            foreach (Field.Step step in Field.Story("death", facts with { KilledBy = "monster" }, Facts(i + 1, "Friar", 30, hour: 10), null, random))
                Assert.That(Evening.IsMatch(step.Text), Is.False, step.Text);
        }
    }

    /// <summary>RC6: Dreadrider said "ok back" in /faction without having said brb or died.</summary>
    [Test]
    public void BackOnlyFromABotThatWasAway()
    {
        int back = 0;
        for (int i = 0; i < 6_000; i++)
        {
            Field.Facts facts = Facts(i, "Cleric", 30) with { Live = true, WasAway = false };
            foreach (string channel in new[] { "faction", "guild" })
                if (Field.Line(channel, facts, null, new Random(i)) is { } said)
                    Assert.That(Engagement.SaysBack(said.Text), Is.False, said.Text);
            if (Field.Line("faction", facts with { WasAway = true }, null, new Random(i)) is { } away && Engagement.SaysBack(away.Text))
                back++;
        }
        Assert.That(back, Is.GreaterThan(0), "a bot back from its brb may say so");
    }

    [Test]
    public void ALoginHelloFitsTheTimeOfDay()
    {
        for (int i = 0; i < 300; i++)
        {
            Assert.That(Evening.IsMatch(Engagement.LoginTell("Manawatch", i % 2 == 0, new Random(i), 10)), Is.False);
            Assert.That(Engagement.LoginTell("Manawatch", false, new Random(i), 21), Does.Not.Contain("morning"));
        }
    }

    // ---- F4: market replies --------------------------------------------------------------------------------------------------

    [TestCase("lfg rvr 50 friar", 50, false)]
    [TestCase("lfg rvr 38 friar", 38, false)]
    [TestCase("lfg 50 friar", 50, false)]
    [TestCase("lfg 50 friar", 0, false)]
    [TestCase("lfg 38 friar keltoi", 38, true)]
    [TestCase("lf1m healer barrows 31-37", 0, true)]
    [TestCase("lf rr grp, 50 cleric", 0, false)]
    public void AnXpQuestionFitsOnlyAnXpPost(string post, int level, bool fits) => Assert.That(Market.AsksXp(post, level), Is.EqualTo(fits));

    /// <summary>RC6: a scout answered "lfg rvr 50 friar" with "how many bubbles?".</summary>
    [Test]
    public void NoBubblesAskedOfAnRvrPostOrA50()
    {
        Field.Facts scout = Facts(2, "Scout", 50);
        int bubbles = 0;
        for (int i = 0; i < 400; i++)
        {
            Assert.That(Market.ReplyTo(Market.Post.Lfg, "lfg rvr 50 friar", scout, false, false, new Random(i), 50).Text, Is.Not.EqualTo("how many bubbles?"));
            Assert.That(Market.ReplyTo(Market.Post.Lfg, "lfg 50 friar", scout, false, false, new Random(i), 50).Text, Is.Not.EqualTo("how many bubbles?"));
            if (Market.ReplyTo(Market.Post.Lfg, "lfg 38 friar", Facts(3, "Scout", 38), false, false, new Random(i), 38).Text == "how many bubbles?")
                bubbles++;
        }
        Assert.That(bubbles, Is.GreaterThan(50), "still asked of a 38 out for xp");
        Assert.That(Market.PosterAnswer("bubbles", Market.Post.Lfg, "lfg 50 friar", Facts(4, "Friar", 50), null, false, false, new Random(1)), Is.Null);
    }

    // ---- F5: offers to group ----------------------------------------------------------------------------------------------------

    [TestCase(false, false, BotGroupInvite.Duty.None, false, 30, 31, true)]
    [TestCase(false, false, BotGroupInvite.Duty.None, true, 30, 31, false)]
    [TestCase(false, false, BotGroupInvite.Duty.StableRoute, false, 30, 31, false)]
    [TestCase(true, false, BotGroupInvite.Duty.None, false, 30, 31, false)]
    [TestCase(false, true, BotGroupInvite.Duty.None, false, 30, 31, false)]
    [TestCase(false, false, BotGroupInvite.Duty.None, false, 30, 34, false)]
    public void AnOfferToGroupNeedsAFreeBot(bool playerGrouped, bool botGrouped, BotGroupInvite.Duty duty, bool riding, int bot, int player, bool free) =>
        Assert.That(AutonomousLivingServer.FreeToOffer(playerGrouped, botGrouped, duty, riding, bot, player), Is.EqualTo(free));
}
