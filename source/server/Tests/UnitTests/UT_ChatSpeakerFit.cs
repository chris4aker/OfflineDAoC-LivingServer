using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Gear = DOL.GS.AutonomousChatGear;
using Stance = DOL.GS.AutonomousChatStance;

namespace DOL.GS.Tests;

/// <summary>
/// 1.1.3, after RC5: a line fits its speaker (owner's PC, RC3-RC5 2026-11-09). An item is paired only with a class that can
/// use it ("99% adroit focus staff any good for pally?"); a bot keeps one story in its group posts ("minst lfg 50, x me" then
/// "lfm barrows 47-50" and "lf1m 47-50 xp grp stonehenge" within ten minutes) and posts only what its real group state is;
/// a question fits the asker's level and class ("where's the paladin trainer" from a 50); a PvE line names no enemy realm's
/// zone ("where do i hand in the uppland quest?" from an Albion bot). The last test plays two hours of /lfg, /trade,
/// /broadcast and /faction for chat_audit.py, without the story memory and with it.
/// </summary>
[TestFixture]
public class UT_ChatSpeakerFit
{
    private static readonly Field.WorldView Frontier = new(["Caer Benowyc", "Caer Berkstead"], [], ["Caer Renaris"], ["Midgard"], 0, 1, "Forest Sauvage");

    private static readonly (eRealm Realm, string Zone)[] Homes =
        [(eRealm.Albion, "Lyonesse"), (eRealm.Midgard, "Vale of Mularn"), (eRealm.Hibernia, "Lough Derg")];

    private static Field.Facts Facts(long id, string cls, int level, string zone = null, eRealm realm = eRealm.None, string item = null,
        int quality = 0, bool gear = false, bool town = false, bool inGroup = false, int groupSize = 1, string destination = null, string dungeon = null)
    {
        realm = realm == eRealm.None ? Gear.RealmOf(cls) : realm;
        zone ??= Homes.First(h => h.Realm == realm).Zone;
        return new Field.Facts(id, realm, cls, level, zone, dungeon ?? AutonomousBotVoices.DungeonFor(realm, level, new Random((int)id)), false, false, town,
            inGroup, groupSize, false, false, false, true, "large skeleton", item, quality, gear, destination, 3, 20, Field.Fills(cls, "healer"), Frontier);
    }

    private static readonly string[] Channels = ["faction", "say", "lfg", "trade", "broadcast", "guild"];

    // ---- 1. items and classes ------------------------------------------------------------------------------------------

    [Test]
    public void TheGameRuleNamesTheArmorAClassWears()
    {
        Assert.That(Gear.ArmorWord("Cabalist", 29), Is.EqualTo("cloth"), "a 29 cabalist's armor is cloth");
        Assert.That(Gear.ArmorWord("Paladin", 15), Is.EqualTo("chain"));
        Assert.That(Gear.ArmorWord("Paladin", 50), Is.EqualTo("plate"));
        Assert.That(Gear.ArmorWord("Minstrel", 50), Is.EqualTo("chain"));
        Assert.That(Gear.ArmorWord("Friar", 50), Is.EqualTo("leather"));
        Assert.That(Gear.ArmorWord("Hero", 50), Is.EqualTo("scale"));
        Assert.That(Gear.ArmorWord("Savage", 40), Is.EqualTo("studded"));
    }

    [Test]
    public void TheOwnersPairsAreRefused()
    {
        Assert.That(Gear.CanUse("Paladin", 50, 0, "Adroit Focus Staff"), Is.False, "paladins can't use staves");
        Assert.That(Gear.CanUse("Friar", 31, (int)eObjectType.Staff, "Adroit Focus Staff"), Is.False, "a focus staff is a caster's");
        Assert.That(Gear.CanUse("Friar", 31, (int)eObjectType.Staff, "Oaken Quarterstaff"), Is.True, "a friar's own staff");
        Assert.That(Gear.CanUse("Cabalist", 29, 0, "Fortifying Focus Staff"), Is.True);
        Assert.That(Gear.CanUse("Necromancer", 30, 0, "adroit focus staff"), Is.True);
        Assert.That(Gear.CanUse("Sorcerer", 50, 0, "Earthshaker Chain Boots"), Is.False, "a sorcerer wears cloth");
        Assert.That(Gear.CanUse("Infiltrator", 50, 0, "Matterbender Medium Shield"), Is.False, "an infiltrator carries a small shield at most");
        Assert.That(Gear.CanUse("Scout", 50, 0, "Bluntbender Longbow"), Is.True);
        Assert.That(Gear.CanUse("Necromancer", 50, 0, "Thrustbender Cloth Vest"), Is.True);
        Assert.That(Gear.CanUse("Minstrel", 50, 0, "Bladeblocker Dagger"), Is.True);
        Assert.That(Gear.CanUse("Reaver", 28, 0, "Fortifying Chain Helm"), Is.True);
        Assert.That(Gear.CanUse("Paladin", 15, 0, "Fortifying Plate Helm"), Is.False, "plate from 20");
        Assert.That(Gear.CanUse("Druid", 50, 0, "Earthshaker Chain Boots"), Is.False, "Hibernia wears scale, not chain");
        Assert.That(Gear.CanUse("Cleric", 50, 0, "Icy Sapphire Jewel"), Is.True, "jewelry is anyone's");
    }

    /// <summary>Every weapon the game's random item generator makes for a class, the class can use by this table.</summary>
    [Test]
    public void EveryWeaponTheItemGeneratorGivesAClassIsUsable()
    {
        var wrong = new List<string>();
        foreach (eCharacterClass cls in Gear.Classes)
        {
            string name = cls.ToString();
            eRealm realm = Gear.RealmOf(name);
            for (int i = 0; i < 300; i++)
            {
                eObjectType type = realm switch
                {
                    eRealm.Midgard => GeneratedUniqueItem.GetMidgardWeapon(cls),
                    eRealm.Hibernia => GeneratedUniqueItem.GetHiberniaWeapon(cls),
                    _ => GeneratedUniqueItem.GetAlbionWeapon(cls),
                };
                string item = type == eObjectType.Shield ? "small shield" : type.ToString();
                if (!Gear.CanUse(name, 50, (int)type, item))
                    wrong.Add($"{name}: {type}");
            }
        }
        Assert.That(wrong.Distinct(), Is.Empty);
    }

    private static readonly (string Name, eObjectType Type)[] Items =
    [
        ("Adroit Focus Staff", eObjectType.Staff), ("Fortifying Focus Staff", eObjectType.Staff), ("Oaken Quarterstaff", eObjectType.Staff),
        ("Earthshaker Chain Boots", eObjectType.Chain), ("Fortifying Chain Helm", eObjectType.Chain), ("Speedy Plate Helm", eObjectType.Plate),
        ("Thrustbender Cloth Vest", eObjectType.Cloth), ("Leather Jerkin", eObjectType.Leather), ("Studded Leggings", eObjectType.Studded),
        ("Reinforced Gloves", eObjectType.Reinforced), ("Scale Hauberk", eObjectType.Scale), ("Matterbender Medium Shield", eObjectType.Shield),
        ("Large Kite Shield", eObjectType.Shield), ("Bluntbender Longbow", eObjectType.Longbow), ("Bladeblocker Dagger", eObjectType.ThrustWeapon),
        ("Heavy Crossbow", eObjectType.Crossbow), ("Great Sword", eObjectType.TwoHandedWeapon), ("War Hammer", eObjectType.Hammer),
        ("Celtic Spear", eObjectType.CelticSpear), ("Battle Scythe", eObjectType.Scythe), ("Icy Sapphire Jewel", eObjectType.Magical),
        ("Spiked Flail", eObjectType.Flexible), ("Bone Claw", eObjectType.HandToHand), ("Lute", eObjectType.Instrument),
    ];

    private static readonly Regex AnyGoodFor = new(@"^(?:\d+% )?(?<item>.+?) any good for ", RegexOptions.CultureInvariant);
    private static readonly Regex MyOld = new(@"\b(?:my old|old) (?<item>.+?)(?: if anyone wants| free to a guildie)", RegexOptions.CultureInvariant);
    private static readonly Regex ArmorWant = new(@"\b(?<armor>cloth|leather|studded|chain|plate|reinforced|scale) (?:helm|gloves|boots|sleeves|legs|vest|arms)\b",
        RegexOptions.CultureInvariant);

    /// <summary>No line pairs an item with a class that can't use it, over every class, five levels, many items and seeds.</summary>
    [Test]
    public void NoLinePairsAnItemWithAClassThatCantUseIt()
    {
        var wrong = new List<string>();
        int asked = 0, wants = 0;
        foreach (eCharacterClass cls in Gear.Classes)
            foreach (int level in new[] { 12, 20, 29, 35, 50 })
                foreach ((string name, eObjectType type) in Items)
                    for (int seed = 0; seed < 6; seed++)
                    {
                        var random = new Random(seed * 7919 + level * 31 + (int)cls * 131 + name.Length);
                        Field.Facts facts = Facts(1_000 + seed, cls.ToString(), level, item: name, quality: 95, gear: type != eObjectType.Magical);
                        // Half as a live bot would see it (the server's rule said yes or no), half from the name alone.
                        facts = seed % 2 == 0 ? facts with { ItemType = (int)type } : facts;
                        bool usable = Gear.CanUse(cls.ToString(), level, seed % 2 == 0 ? (int)type : 0, name);
                        foreach (string channel in Channels)
                        {
                            Field.Said said = Field.Line(channel, facts, null, random, seed * 60_000L);
                            if (said == null)
                                continue;
                            if (AnyGoodFor.Match(said.Text) is { Success: true } q)
                            {
                                asked++;
                                if (!usable)
                                    wrong.Add($"{cls} {level} ({name}): {said.Text}");
                            }
                            if (MyOld.Match(said.Text) is { Success: true } && !usable)
                                wrong.Add($"{cls} {level} ({name}): {said.Text}");
                            foreach (Match armor in ArmorWant.Matches(said.Text))
                            {
                                if (said.Kind is not ("wtb" or "ask"))
                                    continue;
                                wants++;
                                if (!Gear.CanUse(cls.ToString(), level, 0, armor.Value))
                                    wrong.Add($"{cls} {level} wants: {said.Text}");
                            }
                        }
                    }
        TestContext.Progress.WriteLine($"SPEAKER_FIT items: {asked} item questions, {wants} armor wants, {wrong.Count} unusable");
        Assert.That(asked, Is.GreaterThan(40), "the item question is still asked");
        Assert.That(wants, Is.GreaterThan(100));
        Assert.That(wrong, Is.Empty, string.Join("\n", wrong.Take(20)));
    }

    // ---- 2. one story per speaker, and the real group state --------------------------------------------------------------

    private static readonly Regex Closing = new(
        @"^(?:nm got a grp|nvm, grouped|nm found a grp ty|got a grp, ty|nvm, full|grp full ty|full now, ty|nm full|nm, heading out|nvm, going as is|nm, we're off)$");

    /// <summary>A bot's group posts, over two hours in every channel, with its state changing: one stance and one dungeon a while.</summary>
    [Test]
    public void ABotKeepsOneStoryInItsGroupPosts()
    {
        int posts = 0;
        var wrong = new List<string>();
        foreach (bool live in new[] { false, true })
            for (int b = 0; b < 40; b++)
            {
                var random = new Random(b * 17 + (live ? 1 : 0));
                var stance = new Stance();
                string cls = UT_ChatSpeakerFitData.Albion[b % UT_ChatSpeakerFitData.Albion.Length];
                int level = 35 + b % 16;
                var said = new List<(long At, string Text)>();
                bool grouped = false;
                for (long now = 0; now < 2 * 3_600_000L; now += 30_000 + random.Next(60_000))
                {
                    if (random.NextDouble() < 0.03)
                        grouped = !grouped;
                    // As a live bot: a fresh dungeon draw for its level every line, unless it is in one.
                    Field.Facts facts = Facts(500 + b, cls, level, inGroup: grouped, groupSize: grouped ? 5 : 1,
                        dungeon: AutonomousBotVoices.DungeonFor(eRealm.Albion, level, random));
                    if (live)
                        facts = facts with
                        {
                            Live = true, SeeksGroup = grouped ? null : random.NextDouble() < 0.8,
                            Recruit = grouped && random.NextDouble() < 0.5 ? new AutonomousBotGroupCoordinator.RecruitPost("healer", 3, "Stonehenge Barrows", true, level - 3, level) : null,
                        };
                    string channel = Channels[random.Next(Channels.Length)];
                    if (Field.Line(channel, facts, null, random, now, stance) is { } line)
                    {
                        said.Add((now, line.Text));
                        if (Stance.SeekOf(line.Text) != null)
                            posts++;
                        if (live && !FitsState(line.Text, facts))
                            wrong.Add($"{cls} grouped={grouped} recruit={facts.Recruit != null} seeks={facts.SeekesText()}: {line.Text}");
                    }
                }
                wrong.AddRange(Contradictions(said).Select(c => $"{cls} live={live}: {c}"));
            }
        TestContext.Progress.WriteLine($"SPEAKER_FIT story: {posts} group posts, {wrong.Count} contradictions");
        Assert.That(posts, Is.GreaterThan(200));
        Assert.That(wrong, Is.Empty, string.Join("\n", wrong.Take(20)));
    }

    private static bool FitsState(string text, Field.Facts f) => Stance.SeekOf(text) switch
    {
        "lfg" => !f.InGroup && f.SeeksGroup != false,
        "lfm" => f.Recruit != null,
        _ => true,
    };

    /// <summary>The checks chat_audit.py makes: lfg and lfm within 30 minutes (no closing between), two dungeons within 10.</summary>
    internal static IEnumerable<string> Contradictions(List<(long At, string Text)> said)
    {
        for (int i = 0; i < said.Count; i++)
        {
            string seek = Stance.SeekOf(said[i].Text);
            if (seek == null)
                continue;
            string dungeon = UT_ChatSpeakerFitData.DungeonIn(said[i].Text);
            for (int j = i + 1; j < said.Count && said[j].At - said[i].At < 30 * 60_000; j++)
            {
                if (Closing.IsMatch(said[j].Text))
                    break;
                string other = Stance.SeekOf(said[j].Text);
                if (other != null && other != seek)
                    yield return $"{said[i].Text} | {said[j].Text}";
                string there = other == null ? null : UT_ChatSpeakerFitData.DungeonIn(said[j].Text);
                if (dungeon != null && there != null && there != dungeon && said[j].At - said[i].At < 10 * 60_000)
                    yield return $"{said[i].Text} | {said[j].Text}";
            }
        }
    }

    [Test]
    public void PostsFollowTheRealGroupState()
    {
        var random = new Random(5);
        Field.Facts solo = Facts(1, "Minstrel", 50) with { Live = true, SeeksGroup = false };
        Field.Facts seeking = solo with { SeeksGroup = true };
        Field.Facts grouped = Facts(1, "Minstrel", 50, inGroup: true, groupSize: 5) with { Live = true };
        var post = new AutonomousBotGroupCoordinator.RecruitPost("healer", 3, "Stonehenge Barrows", true, 46, 50);
        Field.Facts recruiting = grouped with { Recruit = post };
        for (int i = 0; i < 300; i++)
        {
            Assert.That(Field.Line("lfg", solo, null, random), Is.Null, "a bot on a solo task posts no group");
            Assert.That(Field.Line("lfg", grouped, null, random), Is.Null, "a group that isn't recruiting posts nothing");
            Assert.That(Field.Line("lfg", seeking, null, random)?.Kind, Is.EqualTo("lfg"));
            foreach (string channel in Channels)
                if (Field.Line(channel, grouped, null, random) is { } line)
                    Assert.That(Stance.SeekOf(line.Text), Is.Null, $"{channel}: {line.Text}");
            Field.Said lfm = Field.Line("lfg", recruiting, null, random);
            Assert.That(lfm.Kind, Is.EqualTo("lfm"));
            Assert.That(lfm.Text, Does.Match(@"\b(?:barrows|sh barrows|stonehenge|stonehenge barrows)\b"), "its real camp");
            Assert.That(lfm.Text, Does.Not.Match(@"\b(?:tank|cc|mezzer|caster|support|any)\b"), "the role it lacks");
            if (Regex.Match(lfm.Text, @"\b(\d+)-(\d+)\b") is { Success: true } band)
                Assert.That(band.Value, Is.EqualTo("46-50"), "its real levels");
            Assert.That(lfm.Role, Is.EqualTo("healer"));
        }
        // An "lfg" closes once the bot is in a group, where it was posted, and its story then ends.
        int closed = 0;
        for (int i = 0; i < 100; i++)
        {
            var stance = new Stance();
            Assert.That(Field.Line("lfg", seeking, null, random, 0, stance)?.Kind, Is.EqualTo("lfg"));
            Field.Said next = Field.Line("lfg", recruiting, null, random, 120_000, stance);
            if (next != null && Closing.IsMatch(next.Text))
                closed++;
            else
                Assert.That(next?.Kind, Is.Not.EqualTo("lfm"), "no lfm right after its lfg without a word");
        }
        Assert.That(closed, Is.GreaterThan(50));
    }

    /// <summary>
    /// One closing per post (merged with the /lfg market's closings, AutonomousChatMarket): a closing the market said ends the
    /// story so the bot's next post doesn't close it again, and one the bot's next post said leaves the market nothing to say.
    /// A party that stopped short says it set off, not "full".
    /// </summary>
    [Test]
    public void AGroupPostIsClosedOnceAndTruly()
    {
        var random = new Random(9);
        Field.Facts seeking = Facts(1, "Minstrel", 50) with { Live = true, SeeksGroup = true };
        Field.Facts grouped = Facts(1, "Minstrel", 50, inGroup: true, groupSize: 5) with { Live = true };
        var post = new AutonomousBotGroupCoordinator.RecruitPost("healer", 3, "Stonehenge Barrows", true, 46, 50);
        for (int i = 0; i < 50; i++)
        {
            // The market closed the lfg ("got a grp ty"): the next /lfg post says no closing of its own.
            var stance = new Stance();
            Assert.That(Field.Line("lfg", seeking, null, random, 0, stance)?.Kind, Is.EqualTo("lfg"));
            Assert.That(stance.TryClose(1, "lfg", 120_000), Is.True);
            Assert.That(stance.TryClose(1, "lfg", 130_000), Is.False, "closed once");
            Field.Said next = Field.Line("lfg", grouped with { Recruit = post }, null, random, 180_000, stance);
            Assert.That(next == null || !Closing.IsMatch(next.Text), Is.True, next?.Text);
            // The bot's next post closed it: the market's closing finds nothing open.
            stance = new Stance();
            Field.Line("lfg", seeking, null, random, 0, stance);
            Field.Said closing = Field.Line("lfg", grouped, null, random, 120_000, stance);
            Assert.That(closing?.Text, Does.Match(Closing), "the bot is in a group now");
            Assert.That(stance.TryClose(1, "lfg", 200_000), Is.False);
            // A party that set off short of eight says so; a full one says full.
            stance = new Stance();
            Assert.That(Field.Line("lfg", grouped with { Recruit = post }, null, random, 0, stance)?.Kind, Is.EqualTo("lfm"));
            Assert.That(Field.Line("lfg", grouped, null, random, 60_000, stance)?.Text, Does.Match(@"heading out|going as is|we're off"));
            stance = new Stance();
            Field.Line("lfg", grouped with { Recruit = post }, null, random, 0, stance);
            Assert.That(Field.Line("lfg", grouped with { GroupSize = 8 }, null, random, 60_000, stance)?.Text, Does.Match(@"full"));
        }
        Assert.That(AutonomousChatMarket.Closing(AutonomousChatMarket.Post.Lfm, false, true, true, random), Is.Null, "not full, still recruiting");
        Assert.That(AutonomousChatMarket.Closing(AutonomousChatMarket.Post.Lfm, false, true, true, random, stoppedShort: true), Does.Match(@"heading out|going as is|we're off"));
        Assert.That(AutonomousChatMarket.Closing(AutonomousChatMarket.Post.Lfm, true, true, true, random), Does.Contain("full"));
    }

    // ---- 3. questions that fit the asker -----------------------------------------------------------------------------------

    private static readonly HashSet<string> QuestionKinds = new(StringComparer.Ordinal) { "help", "ask", "camp", "ask_xp", "class", "ask_group", "crafter", "social" };

    /// <summary>A question's levels are the asker's own, a 50 doesn't ask where its trainer is or for tips on its own class.</summary>
    [Test]
    public void QuestionsFitTheAsker()
    {
        var wrong = new List<string>();
        int questions = 0;
        foreach (string cls in UT_ChatSpeakerFitData.All)
            foreach (int level in new[] { 8, 15, 20, 29, 40, 47, 50 })
                for (int seed = 0; seed < 40; seed++)
                {
                    var random = new Random(seed * 101 + level * 7 + cls.Length * 13);
                    Field.Facts facts = Facts(seed, cls, level, town: seed % 3 == 0);
                    foreach (string channel in Channels)
                    {
                        if (Field.Line(channel, facts, null, random, seed * 60_000L) is not { } said || !QuestionKinds.Contains(said.Kind))
                            continue;
                        string text = said.Text;
                        if (!text.EndsWith('?') && !Regex.IsMatch(text, @"^(?:where|what|any|anyone|is|how|good|best)\b"))
                            continue;
                        questions++;
                        if (Regex.IsMatch(text, @"\btrainer\b") && level > Field.TrainerAskMaxLevel)
                            wrong.Add($"{cls} {level}: {text}");
                        if (Regex.IsMatch(text, @"\bneed tips\b") && level >= 40)
                            wrong.Add($"{cls} {level}: {text}");
                        if (level >= 50 && Regex.IsMatch(text, @"\b(?:xp|xping|camp for)\b") && !text.Contains("alt"))
                            wrong.Add($"{cls} {level}: {text}");
                        foreach (Match number in Regex.Matches(text, @"(?<![\d%-])(\d{1,2})(?:ish|s)?(?![\d%p-])"))
                        {
                            int n = int.Parse(number.Groups[1].Value);
                            bool after = Regex.IsMatch(text, $@"\bafter {n}\b") && n == level + 5;
                            bool range = Regex.IsMatch(text, $@"\d+-{n}\b|\b{n}-\d+") ;
                            bool bubbles = Regex.IsMatch(text, $@"\b{n} (?:bubbles|bubs)\b");
                            if (n != level && !after && !range && !bubbles && n is >= 5 and <= 50)
                                wrong.Add($"{cls} {level}: {text}");
                        }
                    }
                }
        TestContext.Progress.WriteLine($"SPEAKER_FIT questions: {questions} asked, {wrong.Count} that don't fit");
        Assert.That(questions, Is.GreaterThan(1000));
        Assert.That(wrong, Is.Empty, string.Join("\n", wrong.Distinct().Take(20)));
    }

    // ---- 4. zones of the speaker's realm ------------------------------------------------------------------------------------

    private static readonly string[] Pve = ["quest", "hand in", "stable", "bind", "trainer", "camp", "grinding", "xp", "duo", "hate me", "on the horse",
        "back in", "third time tonight", "same happened to me", "heading to"];

    [Test]
    public void PveLinesNameNoEnemyRealmsZone()
    {
        var wrong = new List<string>();
        int frontier = 0;
        string[] enemy = ["Uppland", "Jamtland Mountains", "Odin's Gate", "Yggdra Forest", "Emain Macha", "Breifine", "Cruachan Gorge", "Mount Collory"];
        foreach (string zone in enemy)
            foreach (string cls in UT_ChatSpeakerFitData.Albion)
                for (int seed = 0; seed < 30; seed++)
                {
                    var random = new Random(seed * 37 + zone.Length + cls.Length * 3);
                    Field.Facts facts = Facts(seed, cls, 25 + seed % 26, zone: zone, destination: enemy[(seed + 3) % enemy.Length]);
                    var lines = new List<string>();
                    foreach (string channel in Channels)
                        if (Field.Line(channel, facts, null, random, seed * 60_000L) is { } said)
                            lines.Add(said.Text);
                    lines.AddRange(Field.Story("death", facts, Facts(seed + 1, "Cleric", 40, zone: enemy[(seed + 1) % enemy.Length]), Facts(seed + 2, "Wizard", 40,
                        zone: zone), random).Select(step => step.Text));
                    foreach (string text in lines)
                    {
                        string named = enemy.FirstOrDefault(z => text.Contains(z.ToLowerInvariant()) || text.Contains(z.ToLowerInvariant().Replace("mountains", "mtns")));
                        if (named == null)
                            continue;
                        frontier++;
                        if (Pve.Any(word => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b")))
                            wrong.Add($"{cls} in {zone}: {text}");
                    }
                }
        TestContext.Progress.WriteLine($"SPEAKER_FIT zones: {frontier} lines naming an enemy frontier zone, {wrong.Count} of them PvE");
        Assert.That(wrong, Is.Empty, string.Join("\n", wrong.Distinct().Take(20)));
        Assert.That(Field.IsForeignZone(eRealm.Albion, "Uppland"), Is.True);
        Assert.That(Field.IsForeignZone(eRealm.Albion, "Pennine Mountains"), Is.False, "its own frontier");
        Assert.That(Field.IsForeignZone(eRealm.Midgard, "Uppland"), Is.False);
        Assert.That(Field.IsForeignZone(eRealm.Albion, "Lyonesse"), Is.False);
    }

    // ---- 5. the simulated channels for chat_audit.py -------------------------------------------------------------------------

    /// <summary>
    /// Two hours of /lfg, /trade, /broadcast and /faction from 60 bots whose group state and bags change, played without the
    /// story memory (each line as the facts allow) and with it and the live group state, written for source/tools/chat_audit.py
    /// (faction-sim-channels-before/after.chat.log beside the /faction evening).
    /// </summary>
    [Test]
    public void TheChannelsHoldTogether()
    {
        (List<string> Log, int Contradictions) before = Channels2h(false), after = Channels2h(true);
        string folder = Environment.GetEnvironmentVariable("CHAT_SIM_DIR") is { Length: > 0 } dir ? dir : Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-sim");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "faction-sim-channels-before.chat.log"), before.Log);
        File.WriteAllLines(Path.Combine(folder, "faction-sim-channels-after.chat.log"), after.Log);
        TestContext.Progress.WriteLine($"SPEAKER_FIT channels: before {before.Log.Count} lines, {before.Contradictions} contradictions; " +
                                       $"after {after.Log.Count} lines, {after.Contradictions} contradictions");
        Assert.That(before.Contradictions, Is.GreaterThan(0), "the simulation reproduces the owner's PC");
        Assert.That(after.Contradictions, Is.Zero);
        Assert.That(after.Log.Count, Is.GreaterThan(before.Log.Count / 3), "still talking");
    }

    private static (List<string> Log, int Contradictions) Channels2h(bool fixedCode)
    {
        var random = new Random(11);
        var memory = new Field.Memory();
        var stance = fixedCode ? new Stance() : null;
        var log = new List<string>();
        var bySpeaker = new Dictionary<string, List<(long At, string Text)>>();
        string[] channels = ["lfg", "lfg", "trade", "broadcast", "broadcast", "faction"];
        var bots = Enumerable.Range(0, 60).Select(i => (Id: 90_000L + i, Name: "Chanbot" + (char)('a' + i % 26) + (char)('a' + i / 26),
            Class: UT_ChatSpeakerFitData.Albion[i % UT_ChatSpeakerFitData.Albion.Length], Level: 30 + i * 7 % 21)).ToArray();
        var grouped = new bool[bots.Length];
        long ex = 1;
        long baseTime = 20 * 3_600_000L;
        for (long now = 0; now < 2 * 3_600_000L; now += 5_000)
        {
            if (random.NextDouble() > 0.12)
                continue;
            int k = random.Next(bots.Length);
            if (random.NextDouble() < 0.08)
                grouped[k] = !grouped[k];
            var bot = bots[k];
            (string name, eObjectType type) = Items[(int)(bot.Id % Items.Length)];
            string channel = channels[random.Next(channels.Length)];
            Field.Facts facts = Facts(bot.Id, bot.Class, bot.Level, item: name, quality: 96, gear: type != eObjectType.Magical, town: channel == "broadcast",
                inGroup: grouped[k], groupSize: grouped[k] ? 5 : 1, dungeon: AutonomousBotVoices.DungeonFor(eRealm.Albion, bot.Level, random)) with { ItemType = (int)type };
            if (fixedCode)
                facts = facts with
                {
                    Live = true, SeeksGroup = grouped[k] ? null : bot.Id % 4 != 0,
                    Recruit = grouped[k] && bot.Id % 3 == 0 ? new AutonomousBotGroupCoordinator.RecruitPost("tank", 3, "Stonehenge Barrows", true, bot.Level - 3, bot.Level) : null,
                };
            if (Field.Line(channel, facts, memory, random, now, stance) is not { } said)
                continue;
            long t = baseTime + now;
            log.Add($"11/08 {t / 3_600_000 % 24:00}:{t / 60_000 % 60:00}:{t / 1_000 % 60:00} | INF | DOL.GS.BotChatTranscript | CHAT_LINE who=bot ch={channel} " +
                    $"realm=Albion from=\"{bot.Name}\" via=Sim src=field ex={ex++} i=0 role=asker cls=\"{bot.Class}\" lvl={bot.Level} " +
                    (channel == "faction" ? "cap=12.0 " : "") + $"text=\"{said.Text.Replace('"', '\'')}\"");
            if (!bySpeaker.TryGetValue(bot.Name, out var list))
                bySpeaker[bot.Name] = list = [];
            list.Add((now, said.Text));
        }
        return (log, bySpeaker.Values.Sum(list => Contradictions(list).Count()));
    }
}

/// <summary>Class lists and the dungeon names chat uses, for <see cref="UT_ChatSpeakerFit"/>.</summary>
internal static class UT_ChatSpeakerFitData
{
    public static readonly string[] Albion =
        ["Armsman", "Paladin", "Mercenary", "Reaver", "Cleric", "Friar", "Minstrel", "Scout", "Infiltrator", "Sorcerer", "Wizard", "Theurgist", "Cabalist", "Necromancer"];

    public static readonly string[] All = AutonomousChatGear.Classes.Select(cls => cls.ToString()).ToArray();

    private static readonly (string Dungeon, string[] Words)[] Dungeons =
    [
        ("Tomb of Mithra", ["tomb of mithra", "mithra tomb", "tomb", "mithra"]), ("Tepok's Mine", ["tepok's mine", "tepoks", "tepok"]),
        ("Keltoi Fogou", ["keltoi fogou", "keltoi", "fogou"]), ("Stonehenge Barrows", ["stonehenge barrows", "sh barrows", "barrows", "stonehenge"]),
    ];

    /// <summary>The dungeon a line names, by any of its names, or null.</summary>
    public static string DungeonIn(string text) =>
        Dungeons.FirstOrDefault(d => d.Words.Any(w => Regex.IsMatch(text, $@"(?<![a-z']){Regex.Escape(w)}(?![a-z'])"))).Dungeon;
}

internal static class UT_ChatSpeakerFitFacts
{
    public static string SeekesText(this AutonomousChatFieldTalk.Facts facts) => facts.SeeksGroup?.ToString() ?? "-";
}
