using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>Beggars (AutonomousBeggars, bot_living_beggars): who they are, what sets them off, and their lines.</summary>
    [TestFixture]
    public class UT_Beggars
    {
        [Test]
        public void AboutFourPercentOfCraftersAreBeggarsAndNobodyElse()
        {
            int crafters = 0, beggars = 0;
            for (long id = 1; id <= 300_000; id++)
            {
                bool crafter = AutonomousBotPersonality.TypeFor(id) == BotPersonalityType.Crafter;
                bool beggar = AutonomousBeggars.IsBeggarByRoll(id);
                if (crafter) crafters++;
                if (beggar)
                {
                    beggars++;
                    Assert.That(crafter, Is.True, $"bot {id} is a beggar but not a Crafter");
                }
            }
            double share = (double)beggars / crafters;
            TestContext.Out.WriteLine($"{beggars} beggars of {crafters} crafters ({share:P2}); {beggars / 300.0:F2} per 1,000 bots");
            Assert.That(share, Is.InRange(0.036, 0.044));
            Assert.That(AutonomousBeggars.IsBeggarId(0), Is.False);
            Assert.That(AutonomousBeggars.IsBeggarId(-5), Is.False);
        }

        [Test]
        public void EveryRealmGetsItsShareOfBeggars()
        {
            // In-game check 2026-10-27: the roll alone gave Albion 1 beggar, Midgard 2 and Hibernia 4.
            var roster = Enumerable.Range(1, 2700).Select(i => ((long)(i * 7 + 13), 1 + (i - 1) / 900)).ToArray();
            HashSet<long> beggars = AutonomousBeggars.ChooseBeggars(roster);
            foreach (var realm in roster.GroupBy(bot => bot.Item2))
            {
                int crafters = realm.Count(bot => AutonomousBotPersonality.TypeFor(bot.Item1) == BotPersonalityType.Crafter);
                int mine = realm.Count(bot => beggars.Contains(bot.Item1));
                Assert.That(mine, Is.EqualTo(Math.Max(1, (int)Math.Round(crafters * AutonomousBeggars.ShareOfCrafters, MidpointRounding.AwayFromZero))), $"realm {realm.Key}");
                Assert.That(realm.Where(bot => beggars.Contains(bot.Item1)).All(bot => AutonomousBotPersonality.TypeFor(bot.Item1) == BotPersonalityType.Crafter), Is.True);
            }
            Assert.That(AutonomousBeggars.ChooseBeggars(roster.Reverse()), Is.EquivalentTo(beggars), "The same bots beg whatever the roster order.");
            Assert.That(AutonomousBeggars.ChooseBeggars(new[] { (5L, 1) }), Is.Empty.Or.Count.EqualTo(1));
        }

        [TestCase("spare some gold?")]
        [TestCase("can i have some coin")]
        [TestCase("gimme ur loot")]
        [TestCase("got any gold for me?")]
        [TestCase("i need money")]
        [TestCase("any spare change?")]
        [TestCase("can you spare a few silver")]
        [TestCase("plz gold")]
        [TestCase("lend me 5g")]
        [TestCase("give me something")]
        [TestCase("could i borrow some plat")]
        [TestCase("anyone got any armor for me?")]
        [TestCase("SPARE A COPPER??")]
        public void AskingForGoldOrItemsIsRead(string line) =>
            Assert.That(AutonomousBeggars.AsksForCharity(line), Is.True, line);

        [TestCase("give me a sec")]
        [TestCase("can i get an invite?")]
        [TestCase("u want some gold?")]
        [TestCase("here, take this")]
        [TestCase("i need a group")]
        [TestCase("anyone selling armor?")]
        [TestCase("i'll give you some gold")]
        [TestCase("nice armor")]
        [TestCase("lfg 20 cleric")]
        [TestCase("this is for you")]
        [TestCase("have some gold, friend")]
        [TestCase("you can have my old sword")]
        [TestCase("")]
        [TestCase(null)]
        public void OtherTalkIsNotAskingForCharity(string line) =>
            Assert.That(AutonomousBeggars.AsksForCharity(line), Is.False, line ?? "(null)");

        [TestCase(0, "0c")]
        [TestCase(40, "40c")]
        [TestCase(250, "2s 50c")]
        [TestCase(12_345, "1g 23s")]
        [TestCase(10_000_000, "1p")]
        public void CoinIsWrittenTheWayPlayersTypedIt(long copper, string text) =>
            Assert.That(AutonomousBeggars.CoinText(copper), Is.EqualTo(text));

        [Test]
        public void ABotGivesALittleAndNeverMoreThanItHas()
        {
            Assert.That(AutonomousBeggars.DonationFor(0, 0.5), Is.EqualTo(0));
            Assert.That(AutonomousBeggars.DonationFor(1, 0.9), Is.EqualTo(1));
            Assert.That(AutonomousBeggars.DonationFor(50, 1), Is.InRange(1, 50));
            Assert.That(AutonomousBeggars.DonationFor(10_000, 0), Is.EqualTo(40));
            Assert.That(AutonomousBeggars.DonationFor(1_000_000_000, 1), Is.EqualTo(200));
        }

        [Test]
        public void ABeggarTurnsDownInvitations() =>
            Assert.That(BotGroupInvite.BusyMessage("Aldwin", BotGroupInvite.Duty.Begging), Does.Contain("corner"));

        /// <summary>What AutonomousBeggars passes for each topic, plus the slots every line gets.</summary>
        private static readonly Dictionary<string, string[]> Supplied = new()
        {
            ["beg"] = ["player", "player_class", "npc", "hoard"],
            ["beg_bot"] = ["target", "npc", "hoard"],
            ["beg_idle"] = ["npc", "hoard"],
            ["hoard"] = ["npc", "hoard"],
            ["thanks_coin"] = ["player", "player_class", "npc", "hoard", "coin"],
            ["thanks_item"] = ["player", "player_class", "npc", "hoard", "item"],
            ["full"] = ["player", "player_class", "npc", "hoard"],
            ["thanks_bot"] = ["target", "npc", "hoard", "coin"],
            ["donor_give"] = ["target", "hoard"],
            ["donor_refuse"] = ["target", "hoard"],
            ["spurned"] = ["target", "npc", "hoard"],
            ["sold"] = ["npc", "hoard", "coin", "item"],
            ["listed"] = ["npc", "hoard", "coin", "item"],
            ["tell"] = ["player", "player_class", "npc", "hoard"],
            ["tell_group"] = ["player", "player_class", "npc", "hoard"],
            ["outraged"] = ["player", "player_class", "npc", "hoard"],
            ["heard"] = ["player", "player_class", "npc", "hoard"],
            ["insult"] = ["player", "player_class", "npc", "hoard"],
            ["corner"] = ["player", "player_class", "npc", "hoard"],
            ["gone"] = ["player", "player_class", "npc", "hoard"],
            ["calm"] = ["player", "player_class", "npc", "hoard", "coin"],
        };
        private static readonly string[] Always = ["me", "class", "level", "realm", "capital"];

        [Test]
        public void EveryBeggarLineCanBeSaid()
        {
            var assembly = typeof(AutonomousBotVoiceCorpus).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using var stream = assembly.GetManifestResourceStream(name);
                using var reader = new System.IO.StreamReader(stream);
                return reader.ReadToEnd();
            }
            var corpus = AutonomousBotVoiceCorpus.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
            var lines = corpus.Lines.Where(line => line.Kind == "beggar").ToArray();
            var byTopic = lines.GroupBy(line => line.Topic ?? "").ToDictionary(group => group.Key, group => group.ToArray());
            TestContext.Out.WriteLine(string.Join(", ", byTopic.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} {pair.Value.Length}")));
            Assert.That(byTopic.Keys, Is.SubsetOf(Supplied.Keys), "a topic the code never asks for");
            var floors = new Dictionary<string, int>
            {
                ["beg"] = 100, ["insult"] = 200, ["heard"] = 60, ["corner"] = 100, ["outraged"] = 40, ["gone"] = 20, ["calm"] = 10,
                ["thanks_coin"] = 30, ["thanks_item"] = 20, ["hoard"] = 30, ["beg_idle"] = 40, ["donor_give"] = 20,
                ["donor_refuse"] = 15, ["tell"] = 20, ["sold"] = 10, ["listed"] = 3, ["full"] = 5, ["thanks_bot"] = 10,
                ["beg_bot"] = 10, ["spurned"] = 10,
            };
            foreach (var (topic, floor) in floors)
                Assert.That(byTopic.GetValueOrDefault(topic)?.Length ?? 0, Is.GreaterThanOrEqualTo(floor), topic);
            var slot = new Regex(@"\{([^{}]+)\}");
            var bad = new List<string>();
            foreach (var line in lines)
            {
                var allowed = new HashSet<string>(Supplied[line.Topic].Concat(Always));
                foreach (Match match in slot.Matches(line.Text))
                    if (!allowed.Contains(match.Groups[1].Value))
                        bad.Add($"{line.Topic}: {line.Text}");
            }
            Assert.That(bad, Is.Empty, "lines with a slot their topic never gets");
        }
    }
}
