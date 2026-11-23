using System;
using System.Linq;
using NUnit.Framework;
using Guide = DOL.GS.AutonomousChatGuide;

namespace DOL.GS.Tests;

/// <summary>
/// The 1.2.2 chat audit's knowledge holes (docs/bot-voices/CHAT-AUDIT-1.2.2.md, C8, fix F2): buying, the way into Darkness
/// Falls, the frontier and the battlegrounds, a class's armour and stats, crafting past the first step, NPC factions, kill
/// tasks, "who sells X". Each answer holds the fact from the server's code or its world data, and a line that isn't such a
/// question (a trade post, small talk, another topic) gets nothing from the guide.
/// </summary>
[TestFixture]
public class UT_ChatGuide
{
    private static string Ask(string text, eRealm realm = eRealm.Albion, string cls = "Armsman", int level = 20, eRealm dfOwner = eRealm.Albion,
        bool openToAll = false, int seed = 1)
    {
        Assert.That(Guide.TryAnswer(text, realm, cls, level, dfOwner, openToAll, new Random(seed), out string answer) == (answer != null));
        return answer;
    }

    private static void AllSeeds(string text, eRealm realm, Action<string> check, string cls = "Armsman", int level = 20, eRealm dfOwner = eRealm.Albion)
    {
        for (int seed = 0; seed < 12; seed++)
        {
            string answer = Ask(text, realm, cls, level, dfOwner, false, seed);
            Assert.That(answer, Is.Not.Null, text);
            Assert.That(answer.Length, Is.LessThanOrEqualTo(140), answer);
            check(answer.ToLowerInvariant());
        }
    }

    [TestCase("where do you buy potions?", eRealm.Midgard, "alchemist")]
    [TestCase("anyone know where to get a speed potion?", eRealm.Albion, "alchemist")]
    [TestCase("where can i buy dyes", eRealm.Hibernia, "dye")]
    [TestCase("where can i buy poison?", eRealm.Albion, "poison")]
    [TestCase("where do i get spellcraft gems?", eRealm.Albion, "spellcraft")]
    [TestCase("where do i get crafting tools", eRealm.Midgard, "merchant")]
    [TestCase("where do i buy crafting materials?", eRealm.Albion, "merchant")]
    [TestCase("where do i buy bags?", eRealm.Hibernia, "bank")]
    [TestCase("where do i sell my loot?", eRealm.Albion, "merchant")]
    [TestCase("wheres the realm exchange?", eRealm.Midgard, "jordheim")]
    [TestCase("how do i repair my armor?", eRealm.Midgard, "smith")]
    [TestCase("where do i buy food for my horse lol", eRealm.Midgard, "ticket")]
    public void Buying_answers_name_who_and_where(string text, eRealm realm, string fact)
    {
        string fallback = fact switch { "alchemist" => "alchem", "spellcraft" => "spellcraft", "merchant" => "merchant", _ => fact };
        AllSeeds(text, realm, answer =>
        {
            // Every pick names the thing or the place it is.
            bool holds = answer.Contains(fallback) || fact == "merchant" && (answer.Contains("vendor") || answer.Contains(Guide.Realms[realm].Tools.ToLowerInvariant())) ||
                         fact == "bank" && answer.Contains("no bags") || fact == "jordheim" && answer.Contains("/tc") || fact == "smith" && answer.Contains("armorcrafter") ||
                         fact == "ticket" && answer.Contains("stable") || fact == "dye" && answer.Contains("enamel") || fact == "poison" && answer.Contains(Guide.Realms[realm].Poison.ToLowerInvariant());
            Assert.That(holds, answer);
        });
    }

    [Test]
    public void The_capital_merchants_are_the_realms_own()
    {
        Assert.That(Ask("where can i buy poison?", eRealm.Hibernia, seed: 2), Does.Contain("Tir na Nog").Or.Contain("Nona"));
        Assert.That(Ask("where can i buy poison?", eRealm.Midgard, seed: 0), Does.Contain("Ander"));
        foreach (int seed in Enumerable.Range(0, 8))
            Assert.That(Ask("where do i buy bags?", eRealm.Albion, seed: seed), Does.Not.Contain("Jordheim").And.Not.Contain("Tir na Nog"));
    }

    [Test]
    public void No_vendor_sells_potions_and_the_answer_says_so()
    {
        foreach (int seed in Enumerable.Range(0, 8))
            Assert.That(Ask("where do you buy potions?", seed: seed), Does.Match("no vendor|nobody vendors|crafted only"));
    }

    [Test]
    public void The_exchange_of_another_realms_capital_is_not_ours_to_answer()
    {
        Assert.That(Ask("where is the realm exchange in jordheim?", eRealm.Albion), Is.Null);
        Assert.That(Ask("where is the realm exchange in camelot?", eRealm.Albion), Does.Contain("camelot").Or.Contain("/tc"));
    }

    [TestCase(eRealm.Albion, "camelot hills")]
    [TestCase(eRealm.Midgard, "vale of mularn")]
    [TestCase(eRealm.Hibernia, "connacht")]
    public void Darkness_falls_names_the_home_entrance_and_who_holds_it(eRealm realm, string zone)
    {
        AllSeeds("how do i get into df?", realm, answer => Assert.That(answer, Does.Contain(zone)), dfOwner: realm);
        eRealm other = realm == eRealm.Albion ? eRealm.Midgard : eRealm.Albion;
        AllSeeds("how do i get into darkness falls?", realm, answer =>
        {
            Assert.That(answer, Does.Contain(zone));
            Assert.That(answer, Does.Contain(other == eRealm.Midgard ? "mids" : "albs"));
            Assert.That(answer, Does.Contain("keeps"));
        }, dfOwner: other);
        Assert.That(Ask("how do i get into df?", realm, dfOwner: other, openToAll: true), Does.Contain("every realm").Or.Contain("anyone"));
    }

    [TestCase(eRealm.Albion, "Master Visur", "castle sauvage")]
    [TestCase(eRealm.Midgard, "Stor Gothi Annark", "svasud faste")]
    [TestCase(eRealm.Hibernia, "Channeler Glasny", "druim ligen")]
    public void The_frontier_and_the_battlegrounds_go_through_the_realms_porter(eRealm realm, string porter, string keep)
    {
        AllSeeds("how do i get to the frontier?", realm, answer => Assert.That(answer, Does.Contain(keep).And.Contain("keep").And.Contain("port")));
        AllSeeds("how do i get to the battlegrounds?", realm, answer => Assert.That(answer, Does.Contain("porter")), level: 30);
        AllSeeds("how do i get to the battlegrounds?", realm, answer => Assert.That(answer, Does.Contain(porter.ToLowerInvariant())), level: 22);
        AllSeeds("how do i get to the battlegrounds?", realm, answer => Assert.That(answer, Does.Contain("thidranki")), level: 22);
        AllSeeds("how do i get to the bg?", realm, answer => Assert.That(answer, Does.Contain("15 to 35")), level: 44);
    }

    [Test]
    public void Questions_that_only_mention_the_frontier_are_left_alone()
    {
        Assert.That(Ask("is the frontier busy?"), Is.Null);
        Assert.That(Ask("how do i get rps in rvr?"), Is.Null);
        Assert.That(Ask("anyone doing df tonight?"), Is.Null);
    }

    [TestCase("what armor does a thane wear?", "chain")]
    [TestCase("what armor can a ranger wear?", "reinforced")]
    [TestCase("what armor does a cleric wear?", "chain")]
    [TestCase("what armor does a wizard wear?", "cloth")]
    [TestCase("what armor does an armsman wear?", "plate")]
    public void Armor_is_the_games_own_rule(string text, string armor)
    {
        string answer = Ask(text);
        Assert.That(answer, Does.Contain(armor));
        string cls = AutonomousChatFieldTalk.ClassNamed(text);
        Assert.That(answer, Does.StartWith(Guide.ArmorLine(cls)[..6]));
    }

    [Test]
    public void Armor_says_where_the_heavier_kind_starts()
    {
        // Armsman: studded below 5, chain 5-14, plate from 15 (GeneratedUniqueItem.GetAlbionArmorType).
        Assert.That(Guide.ArmorLine("Armsman"), Is.EqualTo("armsmen wear plate from 15, chain before that"));
        Assert.That(Guide.ArmorLine("Wizard"), Is.EqualTo("wizards wear cloth"));
        Assert.That(Guide.ArmorLine("Nobody"), Is.Null);
    }

    [Test]
    public void Buying_armor_is_not_an_armor_weight_question()
    {
        Assert.That(Ask("where can i buy better armor at 20?"), Is.Null);
        Assert.That(Ask("what is the best armor in the game?"), Is.Null);
    }

    [Test]
    public void Stats_come_from_the_class_script()
    {
        Assert.That(Guide.StatsOf("Sorcerer"), Is.EqualTo((eStat.INT, eStat.DEX, eStat.QUI, eStat.INT)));
        Assert.That(Ask("what stats for a sorcerer?"), Is.EqualTo("int first, then dex and qui, con for hp"));
        Assert.That(Ask("what stats should a druid get?", eRealm.Midgard), Does.StartWith("emp first"));
        // Healer: piety, then con and strength.
        for (int seed = 0; seed < 6; seed++)
            Assert.That(Ask("does strength matter for a healer?", eRealm.Hibernia, seed: seed), Does.Contain("pie"));
        Assert.That(Ask("does piety matter for a healer?", eRealm.Midgard), Does.Contain("yes").Or.Contain("yeah"));
        Assert.That(Ask("what does con mean on items?", eRealm.Hibernia), Does.Contain("hit points"));
        Assert.That(Ask("lol nice stats"), Is.Null);
        Assert.That(Ask("what are your stats?"), Is.Null);
    }

    [Test]
    public void Crafting_past_the_first_step()
    {
        AllSeeds("where do i learn weaponcrafting?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("aase")));
        AllSeeds("where is the tailoring trainer?", eRealm.Albion, answer => Assert.That(answer, Does.Contain("arliss eadig")));
        AllSeeds("how do i get my tailoring up?", eRealm.Albion, answer => Assert.That(answer, Does.Contain("orange")));
        AllSeeds("what do i need for alchemy?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("herbs")));
        AllSeeds("how does spellcrafting work?", eRealm.Hibernia, answer => Assert.That(answer, Does.Contain("gems")));
        AllSeeds("whats a good secondary craft?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("craft")));
        Assert.That(Ask("how do i start crafting?"), Is.Null, "TryAnswerCommon's first step answers that");
        Assert.That(Ask("sc or drops at 25?"), Is.Null);
        Assert.That(Ask("can a tailor make me a robe?"), Is.Null);
    }

    [Test]
    public void Factions_as_the_server_keeps_them()
    {
        AllSeeds("how do i raise my faction with the svartalfs?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("drakulv")));
        AllSeeds("how do i fix bad faction?", eRealm.Albion, answer => Assert.That(answer, Does.Contain("kill")));
        AllSeeds("how do i get the sylvan to like me?", eRealm.Hibernia, answer => Assert.That(answer, Does.Contain("kill")));
        AllSeeds("how do i check my faction?", eRealm.Albion, answer => Assert.That(answer, Does.Contain("npc").And.Contain("friendly")));
        AllSeeds("why are the guards attacking me?", eRealm.Hibernia, answer => Assert.That(answer, Does.Contain("faction")));
        AllSeeds("why wont the merchant talk to me?", eRealm.Albion, answer => Assert.That(answer, Does.Contain("faction")));
        AllSeeds("does killing dwarves hurt my faction?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("friends").And.Match(@"\b(no|yeah)\b")));
        AllSeeds("can i get kos with a town?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("neutral")));
        AllSeeds("what faction are the trolls in?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("faction")));
        AllSeeds("whats faction for?", eRealm.Hibernia, answer => Assert.That(answer, Does.Contain("npc")));
        // The realm's emissary (FactionEmissaryRuntime) when the general answer names one.
        string emissary = Enumerable.Range(0, 12).Select(seed => Ask("how do i fix bad faction?", eRealm.Hibernia, seed: seed)).First(a => a.Contains("Kzzirrak"));
        Assert.That(emissary, Does.Contain("Necht"));
    }

    [Test]
    public void Kill_tasks_are_slash_task_on_a_named_guard()
    {
        AllSeeds("how do i get kill tasks?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("/task")));
        AllSeeds("how do i get kill tasks?", eRealm.Midgard, answer => Assert.That(answer, Does.Contain("19")));
    }

    [TestCase("wts dyes 5g pst")]
    [TestCase("selling poison 5s each")]
    [TestCase("where do 24s level?")]
    [TestCase("how do i repair the keep door?")]
    [TestCase("where can i buy a hammer?")]
    [TestCase("lol")]
    [TestCase("")]
    public void Other_lines_get_nothing_from_the_guide(string text) => Assert.That(Ask(text), Is.Null);

    [Test]
    public void Who_sells_reads_the_thing_asked_for()
    {
        Assert.That(Guide.WantedThing("who sells feathers?"), Is.EqualTo("feathers"));
        Assert.That(Guide.WantedThing("where can i buy hemlock"), Is.EqualTo("hemlock"));
        Assert.That(Guide.WantedThing("anyone sell crow feathers in camelot?"), Is.EqualTo("crow feathers"));
        Assert.That(Guide.WantedThing("who sells stuff?"), Is.Null);
        Assert.That(Guide.WantedThing("where is camelot?"), Is.Null);
        Assert.That(Guide.WantedThing("lol"), Is.Null);
    }

    [Test]
    public void Prices_read_as_chat_says_them()
    {
        Assert.That(Guide.PriceWord(40), Is.EqualTo("40c"));
        Assert.That(Guide.PriceWord(3000), Is.EqualTo("30s"));
        Assert.That(Guide.PriceWord(35_000), Is.EqualTo("3g 50s"));
        Assert.That(Guide.PriceWord(165_000), Is.EqualTo("16g"));
        Assert.That(Guide.WhoSellsLine("hemlock", "Gilbert", "City of Camelot", 162, 162, new Random(0)), Does.Contain("Gilbert").And.Contain("about 1s"));
        Assert.That(Guide.WhoSellsLine("dye", "Ozur", "Jordheim", 50, 165_000, new Random(1)), Does.Contain("50c to 16g"));
        Assert.That(Guide.WhoSellsLine("hammer", "Om", "Jordheim", 0, 0, new Random(1)), Does.Not.Contain(","));
    }

    [Test]
    public void Horse_tickets_are_sold_by_the_stable_master()
    {
        Assert.That(AutonomousChatKnowledge.TryAnswerTicketsInGeneral("who sells horse tickets?", out string a), Is.True);
        Assert.That(a, Does.Contain("stable"));
        Assert.That(AutonomousChatKnowledge.TryAnswerTicketsInGeneral("how do i get a horse?", out a), Is.True);
        Assert.That(AutonomousChatKnowledge.TryAnswerBuyGear(eRealm.Albion, "who sells rings and necklaces?", out a), Is.True);
        Assert.That(a, Does.Contain("drops").Or.Contain("exchange"));
    }

    [Test]
    public void Every_line_fits_chat()
    {
        string[] questions =
        [
            "where do you buy potions?", "where can i buy dyes", "where can i buy poison?", "where do i get spellcraft gems?", "where do i get crafting tools",
            "where do i buy crafting materials?", "where do i buy bags?", "where do i sell my loot?", "wheres the realm exchange?", "how do i repair my armor?",
            "how do i get into df?", "how do i get to the frontier?", "how do i get to the battlegrounds?", "where do i learn spellcrafting?",
            "how do i raise my faction with the svartalfs?", "whats faction for?", "how do i get kill tasks?",
        ];
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            foreach (string q in questions)
                for (int seed = 0; seed < 10; seed++)
                {
                    string answer = Ask(q, realm, seed: seed, dfOwner: eRealm.Hibernia);
                    Assert.That(answer, Is.Not.Null, q);
                    Assert.That(answer.Length, Is.LessThanOrEqualTo(140), answer);
                    Assert.That(answer, Does.Not.Contain("{").And.Not.Contain("  ").And.Not.EndWith("."), answer);
                    Assert.That(answer, Does.Not.Match(@"\b(a|an) (a|an)\b"), answer);
                }
    }
}
