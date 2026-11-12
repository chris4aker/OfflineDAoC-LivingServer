using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using Line = DOL.GS.AtmosphereThrottle.Line;

namespace DOL.GS.Tests
{
    /// <summary>The atmosphere lines' throttle and the lore triggers as pure rules (Living Server 1.2, docs/LORE.md).</summary>
    [TestFixture]
    public class UT_LoreAtmosphere
    {
        private static Line Local(string text, long expires = 1_000_000) => new(text, false, false, null, expires);
        private static Line News(string text, long expires = 1_000_000) => new(text, true, true, null, expires);

        #region Throttle

        [Test]
        public void OneAtmosphereLineEvery30Seconds()
        {
            var t = new AtmosphereThrottle();
            t.Offer(Local("a"), 0);
            Assert.That(t.Due(0).Select(l => l.Text), Is.EqualTo(new[] { "a" }));
            t.Offer(Local("b"), 1_000);
            Assert.That(t.Due(1_000), Is.Empty);
            Assert.That(t.Due(29_999), Is.Empty);
            Assert.That(t.Due(30_000).Select(l => l.Text), Is.EqualTo(new[] { "b" }));
        }

        [Test]
        public void OnlyTheNewestAtmosphereLineWaitsAndStaleOnesAreDropped()
        {
            var t = new AtmosphereThrottle();
            t.Offer(Local("first"), 0);
            t.Due(0);
            t.Offer(Local("old"), 1_000);
            t.Offer(Local("new", 40_000), 2_000);
            Assert.That(t.Waiting.Text, Is.EqualTo("new"));
            Assert.That(t.Due(30_000).Single().Text, Is.EqualTo("new"));
            t.Offer(Local("phase", 65_000), 57_000);
            Assert.That(t.Due(66_000), Is.Empty, "a phase line 8 s stale is not shown late");
        }

        [Test]
        public void RealmNewsAlwaysWinsAndQuietsAtmosphereFor10Seconds()
        {
            var t = new AtmosphereThrottle();
            t.Offer(Local("atmosphere"), 0);
            t.Offer(News("keep taken"), 0);
            Assert.That(t.Due(0).Select(l => l.Text), Is.EqualTo(new[] { "keep taken" }));
            Assert.That(t.Due(9_999), Is.Empty);
            Assert.That(t.Due(10_000).Single().Text, Is.EqualTo("atmosphere"));
            t.Offer(News("first kill"), 11_000);
            Assert.That(t.Due(11_000).Single().Text, Is.EqualTo("first kill"), "news is never held by the 30 s gap");
        }

        [Test]
        public void NewsSentElsewhereAlsoQuietsAtmosphere()
        {
            var t = new AtmosphereThrottle();
            t.NoteNews(5_000);
            t.Offer(Local("entry"), 6_000);
            Assert.That(t.Due(6_000), Is.Empty);
            Assert.That(t.Due(15_000).Single().Text, Is.EqualTo("entry"));
        }

        [Test]
        public void NeverMoreThanFourLinesInFiveSeconds()
        {
            var t = new AtmosphereThrottle();
            for (int i = 0; i < 6; i++)
                t.Offer(News("n" + i), 0);
            Assert.That(t.Due(0).Count, Is.EqualTo(AtmosphereThrottle.StackMax));
            Assert.That(t.Due(4_999), Is.Empty);
            Assert.That(t.Due(5_000).Select(l => l.Text), Is.EqualTo(new[] { "n4", "n5" }));
            var u = new AtmosphereThrottle();
            for (int i = 0; i < 4; i++)
                u.NoteNews(0);
            u.Offer(News("late"), 0);
            Assert.That(u.Due(1_000), Is.Empty, "other centre lines count toward the stack");
        }

        [Test]
        public void TheSameNewsIsQueuedOnce()
        {
            var t = new AtmosphereThrottle();
            t.Offer(News("x"), 0);
            t.Offer(News("x"), 0);
            Assert.That(t.RealmWaiting, Is.EqualTo(1));
        }

        #endregion

        #region Entry and areas

        private static LoreBible Bible(params (string Path, string Json)[] files) => LoreBible.Parse(files);

        private const string Dungeon = "{\"id\":\"d\",\"name\":\"D\",\"realm\":\"Albion\",\"regions\":[61]}";
        private const string Lair = "{\"id\":\"lair\",\"name\":\"Lair\",\"realm\":\"Hibernia\",\"regions\":[200],\"zones\":[204]}";
        private const string Open = "{\"id\":\"open\",\"name\":\"Open\",\"realm\":\"Hibernia\",\"regions\":[200]}";

        [Test]
        public void ALocationIsFoundByZoneFirstThenRegion()
        {
            LoreBible bible = Bible(("locations/d.json", Dungeon), ("locations/lair.json", Lair), ("locations/open.json", Open));
            Assert.That(LoreEntryRules.LocationAt(bible, 61, 3)?.Id, Is.EqualTo("d"));
            Assert.That(LoreEntryRules.LocationAt(bible, 200, 204)?.Id, Is.EqualTo("lair"));
            Assert.That(LoreEntryRules.LocationAt(bible, 200, 201)?.Id, Is.EqualTo("open"));
            Assert.That(LoreEntryRules.LocationAt(Bible(("locations/lair.json", Lair)), 200, 201), Is.Null, "a lair is only its zones");
            Assert.That(LoreEntryRules.LocationAt(bible, 1, 0), Is.Null);
        }

        [Test]
        public void FirstEntryIsOncePerDayOnWalkingIn()
        {
            Assert.That(LoreEntryRules.EntryDue(null, "krondon", null, "2026-11-06"), Is.True, "logging in inside counts as walking in");
            Assert.That(LoreEntryRules.EntryDue("krondon", "krondon", null, "2026-11-06"), Is.False, "still inside");
            Assert.That(LoreEntryRules.EntryDue("caer-sidi", "krondon", "2026-11-06", "2026-11-06"), Is.False, "already seen today");
            Assert.That(LoreEntryRules.EntryDue("caer-sidi", "krondon", "2026-11-05", "2026-11-06"), Is.True, "a new day");
            Assert.That(LoreEntryRules.EntryDue("krondon", null, null, "2026-11-06"), Is.False);
            Assert.That(LoreEntryRules.Day(new DateTime(2026, 11, 6, 23, 59, 0)), Is.EqualTo("2026-11-06"));
        }

        [Test]
        public void AnAreaIsACircleWithAFloor()
        {
            var area = new LoreBible.Area("room", 61, 1000, 1000, 5000, 500, ["x"]);
            Assert.That(LoreEntryRules.InArea(area, 61, 1300, 1300, 5100), Is.True);
            Assert.That(LoreEntryRules.InArea(area, 61, 1400, 1400, 5000), Is.False, "outside the radius");
            Assert.That(LoreEntryRules.InArea(area, 61, 1000, 1000, 6000), Is.False, "another floor");
            Assert.That(LoreEntryRules.InArea(area, 62, 1000, 1000, 5000), Is.False, "another region");
            Assert.That(LoreEntryRules.InArea(area with { Z = 0 }, 61, 1000, 1000, 9000), Is.True, "no height given");
        }

        #endregion

        #region Fights

        [Test]
        public void TriggersParse()
        {
            Assert.That(LoreFightRules.TryParseTrigger("health<=50", out var t) && t.Kind == LoreFightRules.Kind.Health && t.Percent == 50, Is.True);
            Assert.That(LoreFightRules.TryParseTrigger("health <= 10", out t) && t.Percent == 10, Is.True);
            Assert.That(LoreFightRules.TryParseTrigger("addsDead", out t) && t.NeedsAdds, Is.True);
            Assert.That(LoreFightRules.TryParseTrigger("orbDeath", out _) && LoreFightRules.TryParseTrigger("addSpawn", out _), Is.True);
            Assert.That(LoreFightRules.TryParseTrigger("health<=0", out _), Is.False);
            Assert.That(LoreFightRules.TryParseTrigger("health<=100", out _), Is.False);
            Assert.That(LoreFightRules.TryParseTrigger("enrage", out _), Is.False);
            Assert.That(LoreFightRules.TryParseTrigger("addsSpawn", out t) && t.Kind == LoreFightRules.Kind.AddSpawn, Is.True);
            Assert.That(LoreFightRules.TryParseTrigger("formChange", out t) && !t.NeedsAdds && !t.ByEvent, Is.True);
            Assert.That(LoreFightRules.TryParseTrigger("death", out t) && t.ByEvent && !t.NeedsAdds, Is.True);
            Assert.That(LoreFightRules.TryParseTrigger("wake", out t) && t.ByEvent, Is.True);
        }

        [Test]
        public void AHealthTriggerFiresOnceAFight()
        {
            var fight = new LoreFightRules.Fight();
            string[] triggers = ["health<=50", "health<=10"];
            Assert.That(LoreFightRules.Observe(fight, triggers, 80, -1), Is.Empty);
            Assert.That(LoreFightRules.Observe(fight, triggers, 49, -1), Is.EqualTo(new[] { "health<=50" }));
            Assert.That(LoreFightRules.Observe(fight, triggers, 60, -1), Is.Empty, "healed back up");
            Assert.That(LoreFightRules.Observe(fight, triggers, 45, -1), Is.Empty, "once a fight");
            Assert.That(LoreFightRules.Observe(fight, triggers, 5, -1), Is.EqualTo(new[] { "health<=10" }));
            Assert.That(LoreFightRules.Observe(new LoreFightRules.Fight(), triggers, 5, -1), Is.EquivalentTo(triggers), "a big first hit passes both");
        }

        [Test]
        public void AddsSpawnDieOneByOneAndAllDie()
        {
            var fight = new LoreFightRules.Fight();
            string[] triggers = ["addSpawn", "orbDeath", "addsDead"];
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, 0), Is.Empty);
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, 4), Is.EqualTo(new[] { "addSpawn" }));
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, 3), Is.EqualTo(new[] { "orbDeath" }));
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, 3), Is.Empty);
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, 1), Is.EqualTo(new[] { "orbDeath" }));
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, 0), Is.EqualTo(new[] { "addsDead" }), "the last one is addsDead, not orbDeath");
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, 2), Is.EqualTo(new[] { "addSpawn" }), "a new wave");
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, 0), Is.Empty, "addsDead once a fight");
            Assert.That(LoreFightRules.Observe(new LoreFightRules.Fight(), ["addsDead"], 100, -1), Is.Empty, "no adds counted");
        }

        [Test]
        public void AFormChangeIsANewModel()
        {
            var fight = new LoreFightRules.Fight();
            string[] triggers = ["formChange", "death", "wake"];
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, -1, 900), Is.Empty, "the first look only learns the form");
            Assert.That(LoreFightRules.Observe(fight, triggers, 90, -1, 900), Is.Empty);
            Assert.That(LoreFightRules.Observe(fight, triggers, 60, -1, 901), Is.EqualTo(new[] { "formChange" }));
            Assert.That(LoreFightRules.Observe(fight, triggers, 30, -1, 902), Is.EqualTo(new[] { "formChange" }), "every new form");
            Assert.That(LoreBossWatch.PhaseTexts([new("death", "a"), new("wake", "b"), new("Death", "c")], LoreFightRules.Kind.Death), Is.EqualTo(new[] { "a", "c" }));
        }

        [Test]
        public void AFightEndsWhenTheBossResets()
        {
            Assert.That(LoreFightRules.Reset(false, 10_000, 100), Is.True);
            Assert.That(LoreFightRules.Reset(false, 9_000, 100), Is.False);
            Assert.That(LoreFightRules.Reset(true, 60_000, 100), Is.False);
            Assert.That(LoreFightRules.Reset(false, 60_000, 50), Is.False, "a wounded boss out of combat is still the same fight");
        }

        [Test]
        public void RealmLinesAreRateLimited()
        {
            Assert.That(LoreOnceRules.GapOver(null, 0, LoreOnceRules.DragonWakeGapMs), Is.True);
            Assert.That(LoreOnceRules.GapOver(0, LoreOnceRules.DragonWakeGapMs - 1, LoreOnceRules.DragonWakeGapMs), Is.False);
            Assert.That(LoreOnceRules.GapOver(0, LoreOnceRules.DarknessFallsGapMs, LoreOnceRules.DarknessFallsGapMs), Is.True);
        }

        #endregion

        [Test]
        public void TheWritersEventAndScriptTriggersAreKnown()
        {
            Assert.That(LoreFightRules.TryParseTrigger("partnerDead", out var t) && t.NeedsPartners && !t.ByEvent, Is.True);
            Assert.That(LoreFightRules.TryParseTrigger("playerDeath", out t) && t.ByEvent, Is.True);
            foreach (string script in new[] { "teleport", "berserk", "breath", "glare" })
                Assert.That(LoreFightRules.TryParseTrigger(script, out t) && t.Kind == LoreFightRules.Kind.Mechanic, Is.True, script + ": fired by its script since 1.2 (UT_LoreMechanicTriggers)");
            var fight = new LoreFightRules.Fight();
            string[] triggers = ["partnerDead", "teleport", "breath"];
            Assert.That(LoreFightRules.Observe(fight, triggers, 100, -1, -1, 4), Is.Empty);
            Assert.That(LoreFightRules.Observe(fight, triggers, 90, -1, -1, 3), Is.EqualTo(new[] { "partnerDead" }));
            Assert.That(LoreFightRules.Observe(fight, triggers, 80, -1, -1, 3), Is.Empty);
            Assert.That(LoreFightRules.Observe(fight, triggers, 70, -1, -1, 1), Is.EqualTo(new[] { "partnerDead" }), "each partner that falls");
            Assert.That(LoreFightRules.Observe(new LoreFightRules.Fight(), ["partnerDead"], 70, -1, -1, -1), Is.Empty, "no partners counted");
        }


        [Test]
        public void TheSwitchIsOnByDefault()
        {
            var field = typeof(Properties).GetField(nameof(Properties.LIVING_ATMOSPHERE_LINES));
            var attribute = (ServerPropertyAttribute)Attribute.GetCustomAttribute(field, typeof(ServerPropertyAttribute));
            Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", "living_atmosphere_lines", (object)true)));
        }
    }
}
