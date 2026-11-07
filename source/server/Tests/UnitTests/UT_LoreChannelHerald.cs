using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.LoreChannels;
using NUnit.Framework;

namespace DOL.UnitTests;

/// <summary>The Herald's lore (Living Server 1.2): chronicle sentences, the bestiary's unlock rules, first kills, the codex.</summary>
[TestFixture, NonParallelizable]
public class UT_LoreChannelHerald
{
    private static RealmEventRecord Raid(string eventId, string realm, string outcome, string details, string start = "2026-11-03T20:00:00Z",
        string end = "2026-11-03T20:47:00Z") =>
        new(Guid.NewGuid().ToString("N"), eventId, "Krondon", "Epic dungeon", realm, start, end, "Ended", outcome, details, 50, 48);

    [Test]
    public void TheRaidFinalsAreTheRaidCodesFinals()
    {
        var code = AutonomousRealmRaid.Definitions.ToDictionary(d => d.Id, d => d.FinalTypes);
        Assert.That(LoreChronicle.RaidFinals.Keys, Is.EquivalentTo(code.Keys));
        foreach (var (id, finals) in code)
            Assert.That(LoreChronicle.RaidFinals[id], Is.EqualTo(finals), id);
    }

    [Test]
    public void TheRaidLeaderRidesAtTheEndOfTheDetails()
    {
        string details = RealmEventRecordStore.WithRaidSize(RealmEventRecordStore.WithWipes("Final defeated.", 1), 85);
        string withLeader = LoreChronicle.WithRaidLeader(details, "Sir Ector");
        Assert.That(LoreChronicle.RaidLeaderIn(withLeader), Is.EqualTo("Sir Ector"));
        Assert.That(RealmEventRecordStore.WipesIn(withLeader), Is.EqualTo(1), "the other markers still read");
        Assert.That(RealmEventRecordStore.RaidSizeIn(withLeader), Is.EqualTo(85));
        Assert.That(LoreChronicle.RaidLeaderIn(details), Is.Null);
        Assert.That(LoreChronicle.WithRaidLeader(details, null), Is.EqualTo(details));
    }

    [Test]
    public void ChronicleTemplating()
    {
        Assert.That(LoreChronicle.Render("{realm} won in {minutes} minutes, led by {raidLeader}.", "Albion", "Ector", 47),
            Is.EqualTo("Albion won in 47 minutes, led by Ector."));
        Assert.That(LoreChronicle.Render("{raidLeader} led {realm}.", "Albion", null, 0), Is.EqualTo("Its raid leader led Albion."),
            "no leader on the record; the sentence still starts with a capital");
        Assert.That(LoreChronicle.Render("", "Albion", "x", 1), Is.Null);
        Assert.That(LoreChronicle.Minutes("2026-11-03T20:00:00Z", "2026-11-03T20:47:20Z"), Is.EqualTo(47));
    }

    [Test]
    public void ARaidRecordTellsItsFinalBossesWinOrLoss()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        string won = LoreChronicle.For(lore, Raid("dungeon-albion-krondon", "Albion", "Boss defeated", LoreChronicle.WithRaidLeader("Done.", "Ector")));
        Assert.That(won, Is.EqualTo("Albion felled Orylle in 47 minutes, led by Ector."));
        string lost = LoreChronicle.For(lore, Raid("dungeon-albion-krondon", "Albion", "Defeated", "Defeated at Orylle."));
        Assert.That(lost, Is.EqualTo("Orylle threw Albion out of Krondon after 47 minutes."));
        Assert.That(LoreChronicle.For(lore, Raid("dungeon-albion-krondon", "Albion", "Timed out", RealmEventRecordStore.WithWipes("x", 0))), Is.Null,
            "a time-out without a wipe is no result");
        Assert.That(LoreChronicle.For(lore, Raid("dungeon-albion-krondon", "Albion", "Interrupted", "x")), Is.Null);
        Assert.That(LoreChronicle.For(lore, Raid("epic-albion", "Albion", "Boss defeated", "x")), Is.Null, "no chronicle in Apocalypse's lore yet");
        Assert.That(LoreChronicle.For(lore, Raid("keep-1", "Albion", "Captured", "x")), Is.Null);
        Assert.That(LoreChronicle.For(lore, Raid("dungeon-albion-krondon", "Albion", "In progress", "x", end: "")), Is.Null);
    }

    [Test]
    public void TheBestiaryListsTheRealmsBossesAndUnlocksThemOnTheFirstKill()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        var locked = LoreBestiary.For(lore, LoreRealm.Albion, [], []);
        Assert.That(locked.Select(e => e.Boss.Id), Does.Contain("scurceol-hyrde").And.Contain("legion").And.Not.Contain("nosdoden"));
        Assert.That(locked.All(e => !e.Unlocked), Is.True);
        LoreBestiaryEntry hyrde = locked.Single(e => e.Boss.Id == "scurceol-hyrde");
        Assert.That(hyrde.StoryShown, Is.EqualTo(LoreBestiaryEntry.LockedStory), "the story waits for the kill");
        List<string> page = LoreBestiary.Page(hyrde, LoreRealm.Albion);
        Assert.That(page, Does.Contain("Not yet slain by Albion.").And.Contain("Kill Scurceol Hyrde's adds first."), "the legend is always told");

        var kills = new[] { new LoreFirstKill(LoreRealm.Albion, "scurceol-hyrde", "2026-11-04T10:00:00.0000000Z", "Ector", "") ,
                            new LoreFirstKill(LoreRealm.Midgard, "legion", "2026-11-01T10:00:00Z", "Bjorn", "") };
        var open = LoreBestiary.For(lore, LoreRealm.Albion, kills, []);
        hyrde = open.Single(e => e.Boss.Id == "scurceol-hyrde");
        Assert.That(hyrde.Unlocked, Is.True);
        Assert.That(hyrde.StoryShown, Is.EqualTo("The story of Scurceol Hyrde."));
        Assert.That(LoreBestiary.Page(hyrde, LoreRealm.Albion), Does.Contain("First slain by Albion: 4 Nov 2026 (Ector)"));
        Assert.That(open.Single(e => e.Boss.Id == "legion").Unlocked, Is.False, "Midgard's kill opens nothing for Albion");
    }

    [Test]
    public void ARaidWinOpensItsFinalBossWithItsRecord()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        var records = new[]
        {
            Raid("dungeon-albion-krondon", "Albion", "Defeated", "x", "2026-11-01T20:00:00Z", "2026-11-01T21:00:00Z"),
            Raid("dungeon-albion-krondon", "Albion", "Boss defeated", LoreChronicle.WithRaidLeader("x", "Ector"), "2026-11-02T20:00:00Z", "2026-11-02T21:00:00Z"),
            Raid("dungeon-albion-krondon", "Albion", "Boss defeated", "x", "2026-11-03T20:00:00Z", "2026-11-03T21:00:00Z"),
            Raid("dungeon-albion-krondon", "Midgard", "Boss defeated", "x", "2026-10-03T20:00:00Z", "2026-10-03T21:00:00Z"),
        };
        LoreBestiaryEntry orylle = LoreBestiary.For(lore, LoreRealm.Albion, [], records).Single(e => e.Boss.Id == "orylle");
        Assert.That(orylle.Unlocked, Is.True);
        Assert.That(orylle.FirstKillUtc, Is.EqualTo("2026-11-02T21:00:00Z"));
        Assert.That(orylle.FirstKiller, Is.EqualTo("Albion raid led by Ector"));
        Assert.That((orylle.Wins, orylle.Losses), Is.EqualTo((2, 1)));
        Assert.That(LoreFirstKills.FromRaidWins(lore, records).Select(k => (k.Realm, k.BossId, k.KilledUtc)),
            Is.EquivalentTo(new[] { (1, "orylle", "2026-11-02T21:00:00Z"), (2, "orylle", "2026-10-03T21:00:00Z") }));
    }

    [Test]
    public void AFirstKillIsDecidedOncePerRealmAndNeverForStaff()
    {
        LoreBoss boss = LoreChannelFixtures.Library().GetBoss("balor");
        var known = new HashSet<(int, string)>();
        DateTime now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);
        Assert.That(LoreFirstKills.Decide(boss, LoreRealm.Hibernia, "Fionn", true, known, now), Is.Null, "a GM's kill");
        LoreFirstKill kill = LoreFirstKills.Decide(boss, LoreRealm.Hibernia, "Fionn", false, known, now);
        Assert.That(kill, Is.Not.Null);
        known.Add((kill.Realm, kill.BossId));
        Assert.That(LoreFirstKills.Decide(boss, LoreRealm.Hibernia, "Oisin", false, known, now), Is.Null);
        Assert.That(LoreFirstKills.Decide(boss, LoreRealm.Albion, "Ector", false, known, now), Is.Not.Null, "each realm has its own");
        Assert.That(LoreFirstKills.Decide(boss, 0, "a mob", false, known, now), Is.Null);
        Assert.That(LoreFirstKills.Decide(null, LoreRealm.Albion, "x", false, known, now), Is.Null);
    }

    [Test]
    public void FirstKillsLiveInTheEventLedgerOncePerRealmAndBoss()
    {
        string path = TemporarySqlite.NewDatabasePath("lore-first-kills");
        try
        {
            Assert.That(LoreFirstKillStore.Read(path), Is.Empty);
            RealmEventRecordStore.Save(path, [Raid("dungeon-albion-krondon", "Albion", "Boss defeated", "x")]);
            Assert.That(LoreFirstKillStore.Add(path, [new(1, "orylle", "2026-11-03T21:00:00Z", "Ector", "")]), Is.EqualTo(1));
            Assert.That(LoreFirstKillStore.Add(path, [new(1, "orylle", "2026-11-05T21:00:00Z", "Later", ""), new(2, "orylle", "2026-11-05T21:00:00Z", "Bjorn", "")]), Is.EqualTo(1));
            var kills = LoreFirstKillStore.Read(path);
            Assert.That(kills.Select(k => (k.Realm, k.Killer)), Is.EquivalentTo(new[] { (1, "Ector"), (2, "Bjorn") }));
            Assert.That(LoreFirstKillStore.RaidWins(path).Single().EventId, Is.EqualTo("dungeon-albion-krondon"));
            Assert.That(RealmEventRecordStore.DeleteAll(path), Is.EqualTo(1));
            Assert.That(LoreFirstKillStore.Read(path).Count, Is.EqualTo(2), "deleting the records keeps the realms' first kills");
        }
        finally { TemporarySqlite.DeleteDatabase(path); }
    }

    [Test]
    public void TheCodexListsPagesWithLinksAndFirstKillStatus()
    {
        LoreLibrary lore = LoreChannelFixtures.Library();
        var kills = new[] { new LoreFirstKill(1, "scurceol-hyrde", "2026-11-04T10:00:00Z", "Ector", "") };
        LoreCodexPage index = LoreCodex.Find(lore, LoreRealm.Albion, kills, "");
        Assert.That(index.Caption, Is.EqualTo("Lore: Lore of Albion"));
        Assert.That(index.Lines, Does.Contain("[Krondon] - 1 of 2 slain").And.Contain("[Darkness Falls] - 0 of 1 slain"));
        Assert.That(index.Lines.Any(l => l.Contains("Trollheim")), Is.False);
        LoreCodexPage krondon = LoreCodex.Find(lore, LoreRealm.Albion, kills, "krondon");
        Assert.That(krondon.Caption, Does.StartWith(LoreCodex.CaptionPrefix + "Krondon"));
        Assert.That(krondon.Lines, Does.Contain("[Scurceol Hyrde] - first slain 4 Nov 2026").And.Contain("[Orylle] - not yet slain"));
        LoreCodexPage boss = LoreCodex.Find(lore, LoreRealm.Albion, kills, "Orylle");
        Assert.That(boss.Lines[0], Does.StartWith("[Krondon]"));
        Assert.That(boss.Lines, Does.Contain(LoreBestiaryEntry.LockedStory));
        Assert.That(LoreCodex.Find(lore, LoreRealm.Albion, kills, "Nosdoden"), Is.Null, "Midgard's boss is not in Albion's codex");
        Assert.That(LoreCodex.Find(lore, LoreRealm.Midgard, kills, "Nosdoden"), Is.Not.Null);
    }

    [Test]
    public void LongCodexPagesSplitWithANextLink()
    {
        var lines = Enumerable.Range(0, 40).Select(i => new string('x', 60) + i).ToList();
        List<LoreCodexPage> pages = LoreCodex.Paginate("Krondon", "Krondon", lines);
        Assert.That(pages.Count, Is.GreaterThan(1));
        Assert.That(pages.All(p => p.Lines.Sum(l => l.Length + 2) <= LoreCodex.PageCharacters), Is.True);
        Assert.That(pages[0].Lines[^1], Is.EqualTo("More: [Krondon 2]"));
        Assert.That(pages[0].Caption, Is.EqualTo($"Lore: Krondon (1/{pages.Count})"));
        Assert.That(pages.SelectMany(p => p.Lines).Count(l => !l.StartsWith("More:")), Is.EqualTo(40));
    }

    [Test]
    public void TheShippedLoreFilesLoadAndPointAtRealEncounters()
    {
        LoreLibrary embedded = EmbeddedLoreSource.Load();
        Assert.That(embedded.GetLocation("krondon"), Is.Not.Null);
        LoreBoss hyrde = embedded.GetBoss("scurceol-hyrde");
        Assert.That(hyrde, Is.Not.Null);
        Assert.That(hyrde.IsClass("ScurceolHyrde"), Is.True);
        Assert.That(embedded.FindBossByClass("ScurceolHyrde")?.Id, Is.EqualTo("scurceol-hyrde"));
        string catalog = Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "..", "..", "..", "..", "docs", "encounters", "epic-encounters.json");
        if (!File.Exists(catalog)) catalog = FindUp("docs/encounters/epic-encounters.json");
        string json = File.ReadAllText(catalog);
        foreach (LoreBoss boss in embedded.AllBosses)
        {
            // A plain world spawn has no catalog entry: it names its exact spawn names instead (npcNames).
            if (string.IsNullOrWhiteSpace(boss.CatalogKey))
            {
                Assert.That(boss.NpcNames, Is.Not.Empty, $"{boss.Id}: a catalogKey or npcNames");
                continue;
            }
            Assert.That(json, Does.Contain($"\"id\": \"{boss.CatalogKey}\"").Or.Contain($"\"class\": \"{boss.CatalogKey}\""), boss.Id);
        }
        foreach (LoreLocation location in embedded.AllLocations)
        {
            var dialogue = new LoremasterDialogue(embedded, location.RealmId == 0 ? 1 : location.RealmId, "x");
            foreach (string text in new[] { location.Loremaster?.Intro }.Concat(location.Loremaster?.Topics.Select(t => t.Value) ?? []))
                foreach (string link in LoremasterDialogue.Links(text))
                    Assert.That(dialogue.Knows(link), Is.True, $"{location.Id}: [{link}] leads nowhere");
        }
    }

    private static string FindUp(string relative)
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, relative))) return Path.Combine(dir.FullName, relative);
        throw new FileNotFoundException(relative);
    }

    [Test]
    public void ALoremasterTakesTheFirstFreeFloorBesideItsAnchor()
    {
        LoremasterDefinition d = Loremasters.For(LoreRealm.Albion);
        Vector3 first = Loremasters.Beside(d.Anchor, d.AnchorHeading, Loremasters.Offsets[0].Side, Loremasters.Offsets[0].Forward);
        Vector3 second = Loremasters.Beside(d.Anchor, d.AnchorHeading, Loremasters.Offsets[1].Side, Loremasters.Offsets[1].Forward);
        Assert.That(Vector2.Distance(new(first.X, first.Y), new(d.Anchor.X, d.Anchor.Y)), Is.EqualTo(130).Within(0.5));

        LoremasterSpawnPlan open = Loremasters.Plan(d, p => p, (_, _) => true, [d.Anchor]);
        Assert.That(open.Spot, Is.EqualTo(first), "dry run: the first side spot");

        LoremasterSpawnPlan crowded = Loremasters.Plan(d, p => p, (_, _) => true, [d.Anchor, first]);
        Assert.That(crowded.Spot, Is.EqualTo(second), "an NPC on the first spot: the next one");

        LoremasterSpawnPlan wall = Loremasters.Plan(d, p => p, (_, b) => b != first, [d.Anchor]);
        Assert.That(wall.Spot, Is.EqualTo(second), "no line of sight to the anchor: the next one");

        LoremasterSpawnPlan balcony = Loremasters.Plan(d, p => p with { Z = p.Z - 200 }, (_, _) => true, []);
        Assert.That(balcony.Spot, Is.Null, "the floor is a storey below: nowhere");
        Assert.That(balcony.Reason, Does.Contain("Librarian Pelan"));

        LoremasterSpawnPlan noFloor = Loremasters.Plan(d, _ => null, (_, _) => true, []);
        Assert.That(noFloor.Spot, Is.Null);
    }
}
