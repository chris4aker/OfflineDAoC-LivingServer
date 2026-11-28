using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;
using Budget = DOL.GS.AutonomousChatBudget;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Calls = DOL.GS.AutonomousBotCombatCalls;

namespace DOL.GS.Tests;

public partial class UT_ChatBudgetSimulation
{
    /// <summary>
    /// Realism 1b (#477; realism audit 1.2.3, docs/realism-audit-1.2.3/evenings.md, brought in from audit/1.2.3-evenings, PR #472):
    /// the player's evening. Two-hour evenings as ONE player reads them, every channel that player sees in one stream (/faction,
    /// /lfg, /trade, /say nearby, /group, /guild, tells), for a level 5 solo in the starter hills, a level 24 in a bot-led dungeon
    /// group and a level 50 solo in the frontier. The realm stream is this fixture's model (the real corpus picker, field talk,
    /// budgets, threads, stories) plus the live guards Evening leaves out (StyleGenerated, NamedWhenApart, the hog hold, three
    /// threads at once, fresh reactions, the group stance), real debate scenes and gold-spam lines; the group is the real
    /// combat-call rules, lead lines and group banter over a scripted fight list; the player's own lines go through
    /// UT_ChatQuestionBench's path (AutonomousChatReplyPath). It shares this fixture's item list and keep world; its stream is
    /// still a loop of its own beside Evening's (one realm, the live guards), so a change to Evening's start rules must be made in
    /// both. The audit's two sim errors are fixed: a thread's lines no longer swap when they fall within 2 s of the channel's last
    /// line, and the party stops fighting after "last pull". A model, not a world: tests only. evening_read.py reads the rows
    /// (evening-transcripts/evenings.tsv) and ratchets seven counts that need no judgement (evening_ratchet.json).
    /// </summary>
    internal static class PlayerEvenings
    {
        private static VC Corpus => AutonomousBotVoices.Corpus;

        public sealed record Row(long At, string Channel, string Who, string Src, long Ex, int I, string Text);

        private sealed record Scenario(string Key, eRealm Realm, int Level, string Where, bool Grouped, bool Guilded);

        private sealed class Bot
        {
            public long Id;
            public string Name, Class, Zone, Monster;
            public int Level;
            public bool Solo, NearPlayer, InGuild, InGroup, InRvr, InDungeon;
            public long NextCheck, LastSpoke = -10 * 60_000;
            /// <summary>#740 (#622's split finished): a bot near the player checks /say on its own clock, with its own last line.</summary>
            public long NextSayCheck, LastSaid = -10 * 60_000;
            public BotPersonalityType Type => (BotPersonalityType)(Id % 6);
            public double Rate;
        }

        private const string Player = "Wynn";   // the bench's {player}
        private const int Bots = 800;           // the owner's PC: about 800 gamebots a realm
        private const double Rate = 2.0;        // bot_voices_chat_rate's default

        private static readonly Dictionary<eRealm, string[]> ClassesOf = new()
        {
            [eRealm.Albion] = ["Armsman", "Cleric", "Wizard", "Friar", "Minstrel", "Paladin", "Sorcerer", "Scout", "Infiltrator", "Theurgist", "Cabalist",
                "Mercenary", "Necromancer", "Reaver"],
            [eRealm.Midgard] = ["Warrior", "Healer", "Runemaster", "Shaman", "Skald", "Thane", "Hunter", "Berserker", "Spiritmaster", "Shadowblade",
                "Bonedancer", "Savage"],
            [eRealm.Hibernia] = ["Hero", "Druid", "Eldritch", "Bard", "Enchanter", "Champion", "Ranger", "Mentalist", "Warden", "Blademaster", "Nightshade",
                "Animist"],
        };

        private static readonly Dictionary<eRealm, string[]> PartyOf = new()
        {
            [eRealm.Albion] = ["Armsman", "Cleric", "Sorcerer", "Wizard", "Minstrel"],
            [eRealm.Midgard] = ["Warrior", "Healer", "Runemaster", "Shaman", "Skald"],
            [eRealm.Hibernia] = ["Hero", "Druid", "Bard", "Eldritch", "Blademaster"],
        };

        private static readonly HashSet<string> Healers = new(StringComparer.OrdinalIgnoreCase) { "Cleric", "Friar", "Healer", "Shaman", "Druid", "Bard", "Warden" };
        private static readonly HashSet<string> Tanks = new(StringComparer.OrdinalIgnoreCase) { "Armsman", "Paladin", "Warrior", "Thane", "Hero", "Champion" };
        private static readonly HashSet<string> Casters = new(StringComparer.OrdinalIgnoreCase)
        {
            "Wizard", "Theurgist", "Sorcerer", "Cabalist", "Necromancer", "Runemaster", "Spiritmaster", "Bonedancer", "Eldritch", "Enchanter", "Mentalist", "Animist",
        };

        private static readonly Dictionary<eRealm, string[]> MonstersOf = new()
        {
            [eRealm.Albion] = ["large skeleton", "bwgan horde", "black wolf", "ghoulic viper", "moor boogey", "giant spider"],
            [eRealm.Midgard] = ["vendo reaver", "svartalf hunter", "wood-eater worker", "hill cat", "rattling skeleton", "black mauler cub"],
            [eRealm.Hibernia] = ["siabra seeker", "curmudgeon scrapper", "lugradan whelp", "fishing bear", "bocanach", "moss sheerie"],
        };

        // The items are UT_ChatBudgetSimulation.Items (one list for both models).

        private static string HillsOf(eRealm realm) => realm switch
        {
            eRealm.Midgard => "Vale of Mularn", eRealm.Hibernia => "Lough Derg", _ => "Camelot Hills",
        };

        private static string FrontierOf(eRealm realm) => realm switch
        {
            eRealm.Midgard => "Uppland", eRealm.Hibernia => "Mount Collory", _ => "Forest Sauvage",
        };

        private static Field.WorldView WorldOf(eRealm realm, long now)
        {
            string[] own = realm switch
            {
                eRealm.Midgard => ["Bledmeer Faste", "Nottmoor Faste", "Hlidskialf Faste", "Blendrake Faste", "Glenlock Faste", "Fensalir Faste"],
                eRealm.Hibernia => ["Dun Crauchon", "Dun Crimthainn", "Dun Bolg", "Dun nGed", "Dun da Behnn", "Dun Scathaig"],
                _ => ["Caer Benowyc", "Caer Berkstead", "Caer Erasleigh", "Caer Boldiam", "Caer Sursbrooke", "Caer Hurbury"],
            };
            string lost = realm switch { eRealm.Midgard => "Arvakr Faste", eRealm.Hibernia => "Dun Ailinne", _ => "Caer Renaris" };
            string to = realm switch { eRealm.Midgard => "Albion", eRealm.Hibernia => "Midgard", _ => "Midgard" };
            // One keep under attack for the first 40 minutes, as UT_ChatBudgetSimulation.WorldAt.
            return new Field.WorldView(own, now < 50 * 60_000 ? [own[0]] : [], [lost], [to], 0, 1, FrontierOf(realm));
        }

        private static readonly string[] First = ["Ael", "Bran", "Cad", "Dun", "Eir", "Fen", "Gar", "Hal", "Isel", "Jor", "Kael", "Lor", "Mor", "Ned", "Orm",
            "Per", "Quil", "Ros", "Sig", "Tor", "Ulf", "Val", "Wil", "Yor", "Zan", "Ash", "Bel", "Cor", "Dag", "Eld"];
        private static readonly string[] Second = ["wyn", "noc", "ric", "stan", "a", "gar", "eth", "vard", "ia", "und", "en", "can", "ven", "ra", "ic",
            "yce", "na", "han", "ald", "o", "is", "win", "ek", "mund", "ara", "or", "ys", "il", "mar", "ot"];

        private static string NameOf(int index) => First[index % First.Length] + Second[index / First.Length % Second.Length];

        // ------------------------------------------------------------------------------------------------------------ the player

        /// <summary>A scripted player line: the minute it is typed, the channel, the text; "{mate}" is a groupmate, "{bot}" the bot
        /// that last sent a tell (the line is skipped when there is none), "{place}" the meeting place, "{keep}" a keep of the realm.</summary>
        private sealed record Typed(double Minute, string Channel, string Text);

        private static Typed[] ScriptOf(Scenario s) => s.Where switch
        {
            "hills" =>
            [
                new(3, "faction", "hi all"),
                new(6, "faction", "where do I choose advanced class"),
                new(10, "faction", "any good spots for a lvl 5?"),
                new(14, "say", "hi"),
                new(15, "tell", "I'm by camp station"),
                new(21, "trade", "any1 selling studded legs"),
                new(27, "faction", "lol just died to a skeleton"),
                new(29, "say", "can anyone rez?"),
                new(36, "faction", "where is the trainer"),
                new(43, "faction", "why did the skeleton cross the road? to get to the other side lol"),
                new(53, "faction", "ding 6!"),
                new(54, "faction", "ty"),
                new(60, "faction", "lvl 6 lfg anyone?"),
                new(66, "tell", "sure, inv me"),
                new(72, "faction", "this game is so slow at low lvl"),
                new(80, "faction", "wher do i buy arows"),
                new(88, "say", "anyone want to group?"),
                new(95, "faction", "wts bone ring 5g"),
                new(96, "faction", "oops wrong chan sorry"),
                new(104, "faction", "is darkness falls open?"),
                new(116, "faction", "gn all"),
            ],
            "dungeon" =>
            [
                new(2.2, "group", "hi all"),
                new(2.8, "group", "where is that"),
                new(4, "group", "where is {place}"),
                new(9, "group", "{mate} what spec are you"),
                new(15, "group", "nice pull"),
                new(22, "group", "brb 2 min"),
                new(25, "group", "back"),
                new(31, "faction", "how far is the dungeon from town"),
                new(36, "group", "ouch, who died"),
                new(44, "group", "why do casters wear robes? no pockets for the loot lol"),
                new(53, "group", "gz {mate}"),
                new(62, "group", "can someone rez me"),
                new(64, "group", "ty for the rez"),
                new(70, "group", "this xp is slow tonight"),
                new(79, "group", "ding!"),
                new(84, "group", "can we invite my friend Tobias?"),
                new(92, "group", "/gu hi oops wrong chat"),
                new(98, "group", "whats the plan after this"),
                new(105, "tell", "thanks for the invite earlier"),
                new(110, "group", "gtg after this pull"),
                new(113, "group", "ty for the group all"),
            ],
            _ =>
            [
                new(3, "faction", "evening all"),
                new(7, "faction", "any rvr groups going?"),
                new(12, "faction", "who holds {keep}"),
                new(17, "say", "hi"),
                new(18, "tell", "where are you"),
                new(24, "faction", "inc at {keep}!"),
                new(31, "faction", "how many relics do we have"),
                new(41, "say", "rez pls"),
                new(43, "say", "ty"),
                new(50, "faction", "mids everywhere again tonight"),
                new(57, "faction", "what do you call a troll with a bow? a hunter with bad aim lol"),
                new(63, "faction", "50 cleric lfg rvr"),
                new(70, "tell", "sure, inv me"),
                new(76, "faction", "who has darkness falls right now?"),
                new(84, "faction", "is the dragon up?"),
                new(90, "say", "nice fight"),
                new(97, "faction", "wtb 99% chain helm"),
                new(98, "faction", "sorry wrong channel"),
                new(104, "faction", "thx for the help earlier all"),
                new(111, "faction", "where do i spend realm points"),
                new(117, "faction", "gn all"),
            ],
        };

        // ---------------------------------------------------------------------------------------------------------- one evening

        private static readonly UT_ChatQuestionBench.Bench Loaded = UT_ChatQuestionBench.Load();

        /// <summary>
        /// #622: one seeded random stream per subsystem, so a change that alters one subsystem's draws (a /group reply, a fight call,
        /// a /trade line) can't reshuffle every later draw of the others. Each stream's seed is derived from the evening's seed and
        /// the stream's name (FNV-1a, stable across runs and machines); "setup" is the evening's own seed, so the roster is the one the
        /// single stream drew. The evening switches <c>random</c> to a subsystem's stream where that subsystem's code starts; its
        /// local functions capture the variable, so every helper it calls (Pick, Styled, Generated, Near, ...) draws from it.
        /// Streams isolate the draws only; #740 (owner, 2026-11-27, "24A") isolates the state too: /say keeps its own recent-line
        /// memory, field memory, stance, exchange numbers and check clock (<see cref="SideOf"/>), and its check reads only its own
        /// meter, so nothing the realm's channels say can move a /say line. A model choice, not the live server's: live, a bot near
        /// the player picks ONE channel a check (/faction, /lfg, /trade or /say, furthest under its target first) and the realm's
        /// memories serve every channel, which is right for a player who reads them all; here a near bot's /say check runs beside
        /// its realm check. /faction, /lfg and /trade still share one choice (and the realm's memory and stance), as live.
        /// </summary>
        internal sealed class Streams
        {
            internal static readonly string[] Names =
            [
                "setup", "ambient", "faction", "say", "lfg", "trade", "guild", "scenes", "stories", "group", "fights", "solo", "tell", "player",
            ];
            private readonly Dictionary<string, Random> _byName = new(StringComparer.Ordinal);

            /// <param name="skew">Test only: the stream whose seed is moved by one, as if that subsystem's code had changed its draws.</param>
            internal Streams(int seed, string skew = null)
            {
                foreach (string name in Names)
                    _byName[name] = new Random(SeedOf(seed, name) + (name == skew ? 1 : 0));
            }

            internal Random this[string name] => _byName[name];

            /// <summary>The seed of stream <paramref name="name"/>: the evening's seed for "setup", else FNV-1a of the name over it.</summary>
            internal static int SeedOf(int seed, string name)
            {
                if (name == "setup")
                    return seed;
                unchecked
                {
                    uint hash = 2166136261u ^ (uint)seed;
                    foreach (char c in name)
                        hash = (hash ^ c) * 16777619u;
                    return (int)(hash & 0x7FFF_FFFF);
                }
            }

            /// <summary>The stream that plays a queued line of a channel.</summary>
            internal static string OfChannel(string channel) => channel switch
            {
                "faction" or "say" or "lfg" or "trade" or "guild" or "group" or "tell" => channel,
                _ => "ambient",
            };

            /// <summary>
            /// #740: whose recent-line memory a stream's corpus picks use: the realm's channels (and the scenes, stories and the
            /// player's turn, all in /faction) share the realm's, as live; /say (the solo events' lines are /say), the party
            /// (/group: its banter and its fights), /guild and tells keep their own.
            /// </summary>
            internal static string SideOf(string stream) => stream switch
            {
                "say" or "solo" => "say",
                "group" or "fights" => "group",
                "guild" => "guild",
                "tell" => "tell",
                _ => "realm",
            };

            /// <summary>Every stream with the recent-line side it picks under.</summary>
            internal IEnumerable<(Random Stream, string Side)> Sides() => Names.Select(name => (_byName[name], SideOf(name)));
        }

        /// <summary>#740: /say's exchange numbers start here, so the realm's exchanges can't renumber /say's (a 2-hour evening has far fewer).</summary>
        internal const long SayExchangeBase = 50_000_000;

        private static List<Row> Evening(Scenario s, int seed, bool speaks, int minutes = 120, string skew = null)
        {
            eRealm realm = s.Realm;
            string realmName = GlobalConstants.RealmToName(realm);
            var streams = new Streams(seed, skew);
            Random random = streams["setup"];
            // #740: one recent-line memory a side (Streams.SideOf); a corpus pick uses the one of the stream it draws from.
            var recents = new Dictionary<string, VC.RecentLines>(StringComparer.Ordinal);
            var recentOf = new Dictionary<Random, VC.RecentLines>(ReferenceEqualityComparer.Instance);
            foreach ((Random sideStream, string sideName) in streams.Sides())
                recentOf[sideStream] = recents.TryGetValue(sideName, out VC.RecentLines sideLines) ? sideLines : recents[sideName] = new VC.RecentLines();
            var meter = new Budget.ChannelMeter();
            Budget.Settings settings = Budget.Current;
            var fresh = new AutonomousChatFiller.RecentTexts();
            var field = new Field.Memory();
            // #740: /say's own field memory and stance (live, the realm's serve /say too).
            var sayField = new Field.Memory();
            var sayStance = new AutonomousChatStance();
            long sayExchanges = SayExchangeBase;
            var guildField = new Field.Memory();
            var rows = new List<Row>();
            string capital = AutonomousBotVoices.CapitalOf(realm);
            string dungeonHere = AutonomousBotVoices.DungeonFor(realm, s.Level, random);
            string playerZone = s.Where switch { "hills" => HillsOf(realm), "dungeon" => dungeonHere, _ => FrontierOf(realm) };
            string[] classes = ClassesOf[realm], monsters = MonstersOf[realm];
            int guildSize = Math.Max(4, Bots / 50);
            int nearCount = s.Where switch { "hills" => 6, "dungeon" => 4, _ => 5 };
            var all = new List<Bot>(Bots);
            for (int i = 0; i < Bots; i++)
            {
                var bot = new Bot
                {
                    Id = 10_000 + i, Name = NameOf(i), Class = classes[random.Next(classes.Length)], Level = 5 + random.Next(46), Solo = random.Next(3) != 0,
                    InGuild = s.Guilded && i < guildSize, NextCheck = random.Next(35_000), Rate = AutonomousBotPersonality.RegularFactor(10_000 + i),
                };
                bot.Monster = monsters[(int)(bot.Id % monsters.Length)];
                bot.InRvr = bot.Level >= 40 && bot.Id % 4 == 0;
                bot.Zone = bot.InRvr ? FrontierOf(realm) : AutonomousChatLiveAnswers.CatalogZone(realm, bot.Level) ?? HillsOf(realm);
                all.Add(bot);
            }
            // The bots within /say range of the player: about the player's level, where the player is.
            for (int i = 0; i < nearCount; i++)
            {
                Bot bot = all[100 + i * 7];
                bot.NearPlayer = true;
                bot.Level = Math.Clamp(s.Level - 2 + random.Next(5), 1, 50);
                bot.Zone = playerZone;
                bot.InDungeon = s.Where == "dungeon";
                bot.InRvr = s.Where == "frontier";
                // #740: its /say clock, on the /say stream (the setup stream's draws, and so the roster, are as before).
                bot.NextSayCheck = streams["say"].Next(35_000);
            }
            // The bot-led party the player joins: the last five bots, the first of them leads.
            Bot[] party = [];
            if (s.Grouped)
            {
                party = all.Skip(Bots - 5).ToArray();
                for (int i = 0; i < party.Length; i++)
                {
                    party[i].InGroup = true;
                    party[i].Solo = false;
                    party[i].Class = PartyOf[realm][i];
                    party[i].Level = s.Level - 1 + random.Next(3);
                    party[i].Zone = playerZone;
                    party[i].InDungeon = true;
                    party[i].InRvr = false;
                    party[i].InGuild = false;
                    party[i].NearPlayer = false;
                }
            }
            Dictionary<long, Bot> byId = all.ToDictionary(bot => bot.Id);
            string Who(long id) => id < 0 ? "Xgoldx" + (-id) : byId.TryGetValue(id, out Bot b) ? b.Name : "Bot" + id;

            long start = 10 * 60_000, end = start + minutes * 60_000L;
            long exchanges = 0;
            long lastFactionLine = long.MinValue / 2, lastPlayerFaction = 0, debateUntil = 0;
            var dueReplies = new List<(long At, string Channel, string Text, long Who, bool Reply, string Src, long Ex, int I)>();
            var deferred = new Dictionary<string, int>(StringComparer.Ordinal);
            // Live guards the budget simulation leaves out (audit, second run): a thread answer apart from its question names the asker
            // (NamedWhenApart), a bot that said one of /faction's last four lines starts nothing new (SinkHold "hog"), no more than three
            // threads at once (RoomFor), and a reaction the channel heard in the last ten minutes is said another way (Shorts).
            // AutonomousChatStance.Shared in the live server: a bot looks for a group or forms one, not both, for half an hour.
            var stance = new AutonomousChatStance();
            var realmLog = new List<(long At, long Who)>();
            var opened = new Dictionary<long, (long At, long Starter)>();
            var lastFactionSpeakers = new Queue<long>();
            var threadEnds = new List<long>();
            string lane = $"sim:{s.Key}:{seed}:{speaks}:{Guid.NewGuid():N}";

            bool Visible(string channel, long who) => channel switch
            {
                "faction" or "lfg" or "trade" or "tell" or "sys" => true,
                "say" => true,
                "guild" => s.Guilded,
                "group" => s.Grouped,
                _ => false,
            };

            void Say(long now, string channel, string text, long who, bool reply, string src, long ex, int index)
            {
                if (channel == "faction")
                {
                    lastFactionLine = now;
                    lastFactionSpeakers.Enqueue(who);
                    while (lastFactionSpeakers.Count > AutonomousChatExchange.SpeakerShareWindow)
                        lastFactionSpeakers.Dequeue();
                }
                if (channel is "faction" or "lfg" or "trade")
                    realmLog.Add((now, who));
                if (index == 0 && ex > 0)
                    opened[ex] = (now, who);
                if (now >= start && now < end && Visible(channel, who))
                    rows.Add(new Row(now - start, channel, Who(who), src, ex, index, text));
                Budget.Channel? metered = channel switch
                {
                    "faction" => Budget.Channel.Faction, "lfg" => Budget.Channel.Lfg, "trade" => Budget.Channel.Trade, "say" => Budget.Channel.Say,
                    "guild" => Budget.Channel.Guild, "group" => Budget.Channel.Group, _ => null,
                };
                if (metered is { } c)
                    meter.Count(c, 1, now, reply ? Budget.LineKind.Reply : Budget.LineKind.Ambient);
            }

            void Sys(long now, string text)
            {
                if (now >= start && now < end)
                    rows.Add(new Row(now - start, "sys", "", "event", 0, 0, text));
            }

            bool ReplyGate(string channel, long now)
            {
                Budget.Channel c = channel == "say" ? Budget.Channel.Say : Budget.Channel.Faction;
                return Budget.Says(meter.TryReply(c, 1, now, Budget.ReplyBand(settings, c, Bots, 20, false, Rate), Budget.ReplyKind.BotThread, random));
            }

            bool Regular(Bot bot, long now) => AutonomousChatRegulars.IsRegular(bot.Id, bot.Type, now, Bots) ||
                                               bot.InGuild && AutonomousChatRegulars.IsGuildRegular(bot.Id, bot.Type, now, guildSize);

            HashSet<string> FlagsOf(Bot bot, params string[] extra)
            {
                var flags = new HashSet<string>(StringComparer.Ordinal) { bot.Solo ? "solo" : "in_group", "evening", VC.TimeFlag(20), VC.SeasonFlag(new DateTime(2026, 11, 23)) };
                if (bot.InDungeon) flags.Add("in_dungeon");
                else flags.Add("outdoors");
                if (bot.InRvr)
                {
                    flags.Add("in_rvr");
                    flags.Add("in_frontier");
                }
                if (bot.Solo && bot.Id % 2 == 0) flags.Add("at_camp");
                if (bot.Id % 7 == 0) flags.Add("bag_full");
                if (bot.InGroup)
                {
                    flags.Add("in_player_group");
                    flags.Add("bot_leads");
                }
                if (Healers.Contains(bot.Class))
                {
                    flags.Add("is_healer");
                    if (bot.Level >= 10) flags.Add("can_rez");
                }
                if (Tanks.Contains(bot.Class)) flags.Add("is_tank");
                if (Casters.Contains(bot.Class)) flags.Add("is_caster");
                if (bot.Level <= 14) flags.Add("newbie");
                else if (bot.Level >= 40) flags.Add("veteran");
                foreach (string key in AutonomousChatPlaces.KeysAt(bot.Zone))
                    flags.Add("at:" + key);
                foreach (string flag in extra)
                    flags.Add(flag);
                return flags;
            }

            VC.Speaker SpeakerOf(Bot bot, params string[] extra) => new(bot.Id, bot.Name, realm, bot.Class, bot.Level, bot.Type, FlagsOf(bot, extra));

            // AutonomousBotVoices.SlotsFor: the bot's own values and one keep, frontier, enemy, capital and dungeon.
            Dictionary<string, string> Slots(Bot bot)
            {
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["me"] = bot.Name, ["class"] = bot.Class.ToLowerInvariant(), ["level"] = bot.Level.ToString(), ["realm"] = realmName,
                    ["zone"] = bot.Zone, ["monster"] = bot.Monster, ["monsters"] = VC.Plural(bot.Monster),
                    ["count"] = random.Next(2, 5).ToString(), ["bubbles"] = (1 + (int)(bot.Id % 9)).ToString(),
                    ["q"] = VC.Openers[random.Next(VC.Openers.Length)],
                };
                if (bot.Id % 3 != 0)
                    slots["item"] = Items[(int)(bot.Id / 3 % Items.Length)];
                foreach (var pair in AutonomousBotVoices.SharedSlots(realm, bot.Level, random,
                             bot.InDungeon ? bot.Zone : AutonomousBotVoices.DungeonFor(realm, bot.Level, random)))
                    slots[pair.Key] = pair.Value;
                return slots;
            }

            Field.Facts FactsOf(Bot bot, long now) => new(
                bot.Id, realm, bot.Class, bot.Level, bot.Zone, bot.InDungeon ? bot.Zone : AutonomousBotVoices.DungeonFor(realm, bot.Level, random), bot.InRvr,
                bot.InDungeon, false, !bot.Solo, bot.Solo ? 1 : bot.InGroup ? 6 : 2 + (int)(bot.Id % 6), random.NextDouble() < 0.03, random.NextDouble() < 0.02,
                bot.Id % 7 == 0, bot.Solo && bot.Id % 2 == 0, bot.Monster, bot.Id % 3 == 0 ? null : Items[(int)(bot.Id / 3 % Items.Length)],
                85 + (int)(bot.Id % 16),
                bot.Id / 3 % Items.Length < 8 || bot.Id / 3 % Items.Length is 14 or 15 or 16 or 26 or 27 or 28 or 36 or 37 or 38 or 39,
                bot.Id % 11 == 0 ? capital : null, 1 + (int)(bot.Id % 9), 20, Healers.Contains(bot.Class), WorldOf(realm, now));

            DateTime ClockAt(long now) => new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc).AddMilliseconds(now);

            bool Pick(long now, string kind, Bot bot, out VC.Picked picked, string answers = null, Func<VC.Line, bool> filter = null, string topic = null,
                string[] flags = null, IReadOnlyDictionary<string, string> extra = null, bool repeat = false)
            {
                Dictionary<string, string> slots = Slots(bot);
                if (extra != null)
                    foreach (var pair in extra)
                        slots[pair.Key] = pair.Value;
                return Corpus.TryPick(new VC.Request(kind, SpeakerOf(bot, flags ?? []), slots, repeat ? null : recentOf[random], ClockAt(now), random)
                {
                    Answers = answers, Filter = filter, Topic = topic, LocalHour = 20, RelaxTo = VC.RecentLines.Relaxed,
                }, out picked);
            }

            string Styled(Bot bot, string text) => AutonomousBotVoices.StyleText(Corpus.VoiceFor(bot.Id), bot.Id, text, random);

            // AutonomousBotVoices.StyleGenerated: a functional line as typed, a conversational one in its speaker's writing style.
            string Generated(Bot bot, string text) => string.IsNullOrEmpty(text) ? text
                : VC.IsFunctional(text) ? AutonomousChatIdiolect.Apply(VC.FixArticles(text), bot.Id, false, random, terse: true)
                : Styled(bot, text);

            Bot Near(long now, Bot to, Func<Bot, bool> pool, double regularShare = 1.0)
            {
                bool regularFirst = regularShare >= 1 || random.NextDouble() < regularShare;
                Bot near = null, any = null;
                for (int tries = 0; tries < 200; tries++)
                {
                    Bot mate = all[random.Next(all.Count)];
                    if (mate == to || !pool(mate))
                        continue;
                    if (regularFirst && Regular(mate, now))
                        return mate;
                    if (near == null && Math.Abs(mate.Level - to.Level) <= 5)
                        near = mate;
                    any ??= mate;
                }
                return near ?? any;
            }

            void Play(long now, string channel, List<Field.Step> steps, Bot[] speakers, long ex)
            {
                if (steps.Count > 0 && channel is "faction" or "say" && !ReplyGate(channel, now))
                    steps = [];
                int index = 1;
                foreach (Field.Step step in steps)
                    if (speakers[step.Who] is { } who)
                        dueReplies.Add((now + Math.Max(500, step.AtMs), channel, step.Text, who.Id,
                            channel is "faction" or "say", "thread:" + step.Who, ex, index++));
                if (steps.Count > 0 && channel == "faction")
                    threadEnds.Add(now + steps.Max(step => step.AtMs) + 2_000);
            }

            void FieldThread(long now, Bot bot, string channel, Field.Said said, Field.Memory memory, Func<Bot, bool> pool, long ex)
            {
                bool Fits(Bot other) => Field.CanGiveAnswer(said, other.Class, other.Level, Healers.Contains(other.Class));
                double share = Field.WantsAnswer(said) ? AutonomousBotChatCoordinator.QuestionRegularShare : 1.0;
                Bot mate = Near(now, bot, other => pool(other) && Fits(other), share), third = Near(now, bot, other => other != mate && pool(other) && Fits(other));
                // AutonomousChatExchange.RoomFor: no more than three threads at once in /faction.
                threadEnds.RemoveAll(at => at <= now);
                if (channel == "faction" && threadEnds.Count >= AutonomousChatExchange.MaxRunning)
                    return;
                List<Field.Step> steps = null;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    steps = Field.Thread(said, FactsOf(bot, now), mate == null ? null : FactsOf(mate, now), third == null ? null : FactsOf(third, now), memory, random, now);
                    if (steps.All(step => step.Who == 0 || !AutonomousChatExchange.IsReaction(step.Text) || AutonomousChatExchange.Shorts.Fresh(lane + channel, step.Text, now)))
                        break;
                    steps = null;
                }
                if (steps == null)
                    return;
                foreach (Field.Step step in steps)
                    if (step.Who != 0 && AutonomousChatExchange.IsReaction(step.Text))
                        AutonomousChatExchange.Shorts.TryUse(lane + channel, step.Text, now);
                Play(now, channel, steps, [bot, mate, third], ex);
            }

            // AutonomousBotChatCoordinator.ScheduleMarketReply: a public reply to an /lfg or /trade post now and then, and the poster's answer.
            void Market(long now, Bot poster, Field.Said said, string channel, long ex)
            {
                AutonomousChatMarket.Post post = AutonomousChatMarket.KindOf(said.Kind);
                if (post == AutonomousChatMarket.Post.None)
                    return;
                if (random.NextDouble() < AutonomousChatMarket.ReplyShare(post))
                {
                    Bot replier = null;
                    for (int tries = 0; tries < 200 && replier == null; tries++)
                    {
                        Bot candidate = all[random.Next(all.Count)];
                        if (candidate != poster && !candidate.InGroup && AutonomousChatMarket.CanReply(post, said.Text, poster.Level, FactsOf(candidate, now), candidate.Solo))
                            replier = candidate;
                    }
                    if (replier != null)
                    {
                        AutonomousChatMarket.Reply reply = AutonomousChatMarket.ReplyTo(post, said.Text, FactsOf(replier, now), replier.Solo, false, random, poster.Level);
                        if (!string.IsNullOrWhiteSpace(reply.Text))
                        {
                            int at = AutonomousChatArbiter.DelayMs(said.Text, reply.Text, 40, 0, random);
                            dueReplies.Add((now + at, channel, Styled(replier, reply.Text), replier.Id, false, "market:reply", ex, 1));
                            string answer = AutonomousChatMarket.PosterAnswer(reply.Asks, post, said.Text, FactsOf(poster, now), null, false, true, random);
                            if (answer != null)
                                dueReplies.Add((now + at + AutonomousChatArbiter.DelayMs(reply.Text, answer, 40, 0, random), channel, Styled(poster, answer), poster.Id,
                                    false, "market:poster", ex, 2));
                        }
                    }
                }
            }

            void WrittenReplies(long now, Bot starter, string channel, VC.Picked opener, long ex)
            {
                if (opener.Line?.Kind is "ambient" or "rvr" or "camp" or "group")
                {
                    if (AutonomousBotChatCoordinator.WrittenReaction(opener.Line, random) is { } reaction && ReplyGate(channel, now))
                    {
                        Bot mate = channel == "say" ? all.Where(b => b.NearPlayer && b != starter).OrderBy(_ => random.Next()).FirstOrDefault() : all[random.Next(all.Count)];
                        if (mate != null)
                            dueReplies.Add((now + AutonomousBotChatCoordinator.ReplyDelayMs(reaction, 0, random), channel, reaction, mate.Id, true, "reaction", ex, 1));
                    }
                    return;
                }
                int count = AutonomousBotChatCoordinator.BotReplyCount(opener.Line?.Invites, 20, random);
                if (count > 0 && !ReplyGate(channel, now))
                    return;
                int due = 0;
                for (int r = 0; r < count; r++)
                {
                    Bot mate = channel == "say" ? all.Where(b => b.NearPlayer && b != starter).OrderBy(_ => random.Next()).FirstOrDefault() : all[random.Next(all.Count)];
                    if (mate == null)
                        break;
                    foreach (string answers in VC.AnswerChain(opener.Line?.Invites))
                        if (Pick(now, "reply", mate, out VC.Picked reply, answers: answers, filter: line => AutonomousChatShortTalk.ReplyFits(opener.Text, line.Text)))
                        {
                            due = AutonomousBotChatCoordinator.ReplyDelayMs(reply.Text, due, random);
                            dueReplies.Add((now + due, channel, reply.Text, mate.Id, true, "reply:" + answers, ex, r + 1));
                            break;
                        }
                }
            }

            Func<VC.Line, bool> CorpusFilter(bool faction, long now, Field.Memory memory) =>
                AutonomousBotChatCoordinator.WrittenFits(AutonomousChatFiller.AmbientFilter(faction, random), memory, now);

            string AmbientKind(Bot bot)
            {
                var kinds = new List<(string, double)> { ("ambient", 6) };
                if (bot.Solo && bot.Level >= 5) kinds.Add(("lfg", bot.Id % 6 is 2 or 0 ? 3 : 1.5));
                if (bot.Id % 6 == 1) kinds.Add(("trade", 3));
                if (bot.Level >= 40 && bot.Id % 6 is 4 or 5) kinds.Add(("rvr", 1));
                double draw = random.NextDouble() * kinds.Sum(k => k.Item2);
                foreach ((string kind, double weight) in kinds)
                {
                    if (draw < weight)
                        return kind;
                    draw -= weight;
                }
                return "ambient";
            }

            // AutonomousBotChatCoordinator.TryStartBudgeted and SpeakIn, as UT_ChatBudgetSimulation.Evening models them.
            bool Budgeted(Bot bot, long now)
            {
                var options = new List<(Budget.Channel Channel, string Name, (double, double, double) Band, double Measured)>();
                void Add(Budget.Channel channel, string name, int count)
                {
                    var band = Budget.Band(settings, channel, count, 20, false, Rate);
                    options.Add((channel, name, band, meter.AmbientPerMinute(channel, 1, now, band.Target)));
                }
                // A running debate owns /faction, and nothing unprompted follows a player's line for a moment (QuietAfterPlayer).
                bool factionFree = now >= debateUntil && !AutonomousBotChatCoordinator.QuietAfterPlayer(lastPlayerFaction, now);
                if (factionFree && (Regular(bot, now) || random.NextDouble() < AutonomousChatRegulars.OthersShare ||
                                    meter.PerMinute(Budget.Channel.Faction, 1, now) <
                                    AutonomousChatRegulars.OthersBelow * Budget.Band(settings, Budget.Channel.Faction, Bots, 20, false, Rate).Target))
                    Add(Budget.Channel.Faction, "faction", Bots);
                Add(Budget.Channel.Lfg, "lfg", Bots);
                Add(Budget.Channel.Trade, "trade", Bots);
                // #740: /say is not among them: a bot near the player checks it on its own (SayCheck).
                bool regularFirst = Regular(bot, now) && options.Count > 0 && options[0].Name == "faction";
                while (options.Count > 0)
                {
                    random = streams["ambient"];
                    double total = options.Sum(o => Budget.ChoiceWeight(o.Band.Item2, o.Measured));
                    double draw = random.NextDouble() * total;
                    int pick = 0;
                    if (regularFirst)
                    {
                        regularFirst = false;
                        draw = -1;
                    }
                    for (; draw >= 0 && pick < options.Count - 1; pick++)
                    {
                        double weight = Budget.ChoiceWeight(options[pick].Band.Item2, options[pick].Measured);
                        if (draw < weight)
                            break;
                        draw -= weight;
                    }
                    var choice = options[pick];
                    options.RemoveAt(pick);
                    random = streams[choice.Name];
                    if (SpeakIn(bot, now, choice.Channel, choice.Name, choice.Band))
                        return true;
                }
                return false;
            }

            // AutonomousBotChatCoordinator.SpeakIn: a bot's unprompted line in the channel chosen (false: held, or nothing fitting).
            // #740: /say's line uses /say's own field memory, stance and exchange numbers.
            bool SpeakIn(Bot bot, long now, Budget.Channel channel, string name, (double, double, double) band)
            {
                Field.Memory memory = name == "say" ? sayField : field;
                AutonomousChatStance stanceHere = name == "say" ? sayStance : stance;
                long NextEx() => name == "say" ? ++sayExchanges : ++exchanges;
                // AutonomousChatExchange.SinkHold "hog" (1.2.3): a bot that said one of /faction's last four lines starts nothing new.
                if (name == "faction" && lastFactionSpeakers.Contains(bot.Id) && now - lastFactionLine < AutonomousChatExchange.SpeakerShareQuietMs)
                    return false;
                if (meter.TryStart(channel, 1, now, band, random) is Budget.Verdict.HoldCeiling or Budget.Verdict.HoldGap)
                    return false;
                if (random.NextDouble() < Field.Share(name) && Field.Line(name, FactsOf(bot, now), memory, random, now, stanceHere) is { } said)
                {
                    string text = Generated(bot, said.Text);
                    if (!fresh.TryAdd(name.GetHashCode(), text, now))
                        return false;
                    long ex = NextEx();
                    Say(now, name, text, bot.Id, false, "field:" + said.Kind, ex, 0);
                    if (name is "faction" or "say")
                        FieldThread(now, bot, name, said, memory, name == "faction" ? _ => true : mate => mate.NearPlayer, ex);
                    else
                        Market(now, bot, said, name, ex);
                    return true;
                }
                string kind = name switch
                {
                    "lfg" => "lfg", "trade" => "trade",
                    _ => AmbientKind(bot) is var k && k is "lfg" or "trade" && random.NextDouble() < 0.7 ? "ambient" : k,
                };
                // The ambient channel lines never say a join hello (AutonomousBotChatCoordinator.IsAmbientChannelLine).
                Func<VC.Line, bool> basic = CorpusFilter(name == "faction", now, memory);
                Func<VC.Line, bool> filter = line => AutonomousBotChatCoordinator.IsAmbientChannelLine(line) && basic(line);
                if (!Pick(now, kind, bot, out VC.Picked picked, filter: filter) &&
                    (name is "lfg" or "trade" || !Pick(now, "ambient", bot, out picked, filter: filter)))
                {
                    if (name is "faction" or "say" && Field.Line(name, FactsOf(bot, now), memory, random, now, stanceHere) is { } fallback)
                    {
                        string text = Generated(bot, fallback.Text);
                        if (!fresh.TryAdd(name.GetHashCode(), text, now))
                            return false;
                        long ex = NextEx();
                        Say(now, name, text, bot.Id, false, "field:" + fallback.Kind, ex, 0);
                        FieldThread(now, bot, name, fallback, memory, name == "faction" ? _ => true : mate => mate.NearPlayer, ex);
                        return true;
                    }
                    return false;
                }
                if (!fresh.TryAdd(name.GetHashCode(), picked.Text, now))
                    return false;
                if (picked.Line?.Kind is "ambient" or "rvr" or "camp" or "group")
                    AutonomousBotChatCoordinator.NoteWritten(memory, picked.Line, now);
                long written = NextEx();
                Say(now, name, picked.Text, bot.Id, false, "written:" + picked.Line?.Kind + "/" + picked.Line?.Topic, written, 0);
                if (name is "faction" or "say")
                    WrittenReplies(now, bot, name, picked, written);
                return true;
            }

            // #740: a bot near the player checks /say on its own clock, on the /say stream, against /say's meter only (live, /say is
            // one of the channels its single check chooses from: see Streams).
            void SayCheck(Bot bot, long now, double sayPressure)
            {
                random = streams["say"];
                if (bot.NextSayCheck > now)
                    return;
                bot.NextSayCheck = now + 15_000 + random.Next(20_001) + bot.Id % 4_000;
                if (random.NextDouble() >= 0.55 || bot.InGroup)
                    return;   // fighting, casting or riding
                DateTime clock = ClockAt(now);
                if (!AutonomousBotChat.ShouldSpeak(clock, clock.AddMilliseconds(bot.LastSaid - now), false, random, bot.Rate, sayPressure, Regular(bot, now)))
                    return;
                if (SpeakIn(bot, now, Budget.Channel.Say, "say", Budget.Band(settings, Budget.Channel.Say, Bots, 20, false, Rate)))
                    bot.LastSaid = now;
            }

            // ---- the realm's /faction scenes: real debate scenes (their beats as written), the gold spammer's ads and the reactions.
            long debatePlanned = 0, debateDue = 5 * 60_000 + random.Next(5 * 60_000), wavePlanned = 0,
                waveDue = AutonomousGoldSpammers.FirstWaveMinMs + random.Next((int)AutonomousGoldSpammers.FirstWaveSpreadMs);
            int adsLeft = -1, adsSaid = 0;
            long nextAd = 0;
            var heardScenes = new HashSet<string>(StringComparer.Ordinal);
            int[] levels = all.Select(bot => bot.Level).ToArray();
            long spammer = -(1 + random.Next(900));

            bool StartDebate(long now)
            {
                for (int tries = 0; tries < 8; tries++)
                {
                    VC.Scene scene = AutonomousBotDebates.ChooseScene(Corpus.Scenes, realm, heardScenes, levels, random, out _, "evening", null, false, false);
                    if (scene == null)
                        return false;
                    heardScenes.Add(scene.Id);
                    var cast = new Dictionary<string, Bot>(StringComparer.Ordinal);
                    foreach ((string role, VC.RoleSpec spec) in scene.Roles)
                    {
                        Bot chosen = null;
                        for (int t = 0; t < 300 && chosen == null; t++)
                        {
                            Bot candidate = all[random.Next(all.Count)];
                            if (cast.ContainsValue(candidate) || candidate.InGroup || !spec.LevelFits(candidate.Level))
                                continue;
                            if (spec.Classes is { Length: > 0 } && !spec.Classes.Contains(candidate.Class, StringComparer.OrdinalIgnoreCase))
                                continue;
                            if (spec.Types is { Length: > 0 } && !spec.Types.Contains(candidate.Type.ToString(), StringComparer.OrdinalIgnoreCase))
                                continue;
                            chosen = candidate;
                        }
                        if (chosen != null)
                            cast[role] = chosen;
                    }
                    var lines = new List<(Bot Who, string Text, int Pause)>();
                    bool playable = true;
                    int pause = 0;
                    foreach (VC.Beat beat in scene.Beats)
                    {
                        if (beat.IsPause)
                        {
                            pause += beat.PauseSeconds * 1_000;
                            continue;
                        }
                        if (!cast.TryGetValue(beat.Role, out Bot speaker))
                        {
                            if (!beat.Optional)
                                playable = false;
                            continue;
                        }
                        Dictionary<string, string> slots = Slots(speaker);
                        if (cast.TryGetValue("A", out Bot a)) slots["name_a"] = a.Name;
                        if (cast.TryGetValue("B", out Bot b)) slots["name_b"] = b.Name;
                        string[] filled = AutonomousBotDebates.FilledVariants(beat, slots);
                        if (filled.Length == 0)
                        {
                            if (!beat.Optional)
                                playable = false;
                            continue;
                        }
                        lines.Add((speaker, filled[random.Next(filled.Length)], pause));
                        pause = 0;
                    }
                    if (!playable || lines.Count < 3)
                        continue;
                    long at = now, ex = ++exchanges;
                    int index = 0;
                    foreach ((Bot who, string text, int wait) in lines)
                    {
                        at += wait + (index == 0 ? 0 : AutonomousBotDebates.BeatDelayMs(text));
                        dueReplies.Add((at, "faction", text, who.Id, false, "debate:" + scene.Topic, ex, index++));
                    }
                    debateUntil = at + 2_000;
                    return true;
                }
                return false;
            }

            void SpamReact(long now, int count, long firstDelay, long ex)
            {
                long due = now + firstDelay;
                for (int r = 0; r < count; r++)
                {
                    due += 2_500 + random.Next(8_000);
                    if (!ReplyGate("faction", now))
                        continue;
                    Bot bot = all[random.Next(all.Count)];
                    if (Pick(now, "spam_react", bot, out VC.Picked react))
                        dueReplies.Add((due, "faction", react.Text, bot.Id, true, "spamreact", ex, r + 1));
                }
            }

            // ---- the bot-led party (BotLedGroups.Announce, AutonomousBotCombatCalls, AutonomousBotGroupChatter), over a fight list.
            var groupState = new Calls.GroupState();
            var callStates = party.ToDictionary(bot => bot.Id, _ => new Calls.BotState());
            Bot leader = party.Length > 0 ? party[0] : null;
            Bot healer = party.FirstOrDefault(bot => Healers.Contains(bot.Class));
            string meet = Loaded.Towns.Where(t => t.Realm == realm).Select(t => t.Town.Name).FirstOrDefault() ?? capital;
            bool joined = false, groupKnown = false;
            long nextFight = start + 8 * 60_000, fightEnd = 0, lastPlayerGroupReply = 0;
            bool fighting = false, playerDead = false, lastPull = false;
            int enemies = 0, alive = 6, healerMana = 100, fightNo = 0;
            long xp = 0;
            Bot downMate = null;
            var groupDue = new List<(long At, Action Act)>();
            // #622: a queued group line plays under the stream that queued it.
            void Later(long at, Action act)
            {
                Random queuedBy = random;
                groupDue.Add((at, () =>
                {
                    random = queuedBy;
                    act();
                }));
            }
            var events = new SortedDictionary<long, string>();
            if (s.Grouped)
            {
                events[start + 34 * 60_000] = "mate_dies";
                events[start + 52 * 60_000] = "mate_dings";
                events[start + 61 * 60_000] = "player_dies";
                events[start + 78 * 60_000] = "player_dings";
                events[start + 90 * 60_000] = "mate_leaves";
                events[start + 93 * 60_000] = "mate_joins";
            }
            else if (s.Where == "hills")
            {
                events[start + 28 * 60_000] = "solo_death";
                events[start + 46 * 60_000] = "near_bot_dings";
                events[start + 52 * 60_000] = "solo_ding";
                events[start + 68 * 60_000] = "near_bot_dies";
            }
            else
            {
                events[start + 40 * 60_000] = "solo_death";
                events[start + 85 * 60_000] = "solo_death";
                events[start + 66 * 60_000] = "near_bot_dies";
            }

            void GroupSay(long now, Bot bot, string text, string src, long ex = 0, int index = 0) => Say(now, "group", text, bot.Id, src.StartsWith("group:reply"), src, ex, index);

            void Lead(long now, BotLedGroups.Lead what, string place, string camp)
            {
                var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["place"] = place ?? "the camp", ["destination"] = place ?? "the camp", ["camp"] = camp ?? "the camp", ["player"] = Player };
                string text = Pick(now, "group", leader, out VC.Picked picked, topic: BotLedGroups.Topic(what), flags: ["lead_call"], extra: slots)
                    ? picked.Text
                    : BotLedGroups.Fill(BotLedGroups.Fallback(what), slots);
                Later(now + 800 + random.Next(1_500), () => GroupSay(now + 1_500, leader, text, "lead:" + what));
            }

            void CombatCall(long now, string topic, Bot preferred)
            {
                if (topic == "pull" && leader != null && Calls.Gate(groupState, callStates[leader.Id], topic, now))
                {
                    if (Pick(now, "group", leader, out VC.Picked pull, topic: "lead_pull", flags: ["lead_call", "in_combat"]))
                    {
                        GroupSay(now, leader, pull.Text, "combat:lead_pull");
                        return;
                    }
                    groupState.LastCall = long.MinValue / 2;
                    callStates[leader.Id].LastCall = long.MinValue / 2;
                }
                IEnumerable<Bot> order = preferred != null ? new[] { preferred }
                    : topic is "pull" or "add" ? party.OrderByDescending(bot => Tanks.Contains(bot.Class)).ThenBy(_ => random.Next())
                    : party.OrderBy(_ => random.Next());
                foreach (Bot speaker in order.Where(bot => bot != downMate))
                {
                    if (!Calls.Gate(groupState, callStates[speaker.Id], topic, now))
                        continue;
                    int count = enemies;
                    bool gave = groupState.LastFightGaveExperience;
                    bool power = Healers.Contains(speaker.Class) || Casters.Contains(speaker.Class);
                    if (Pick(now, "combat", speaker, out VC.Picked call, topic: topic, flags: ["in_combat"], filter: line => topic switch
                        {
                            "pull" => Calls.PullLineFits(line.Text, count),
                            "kill" => Calls.KillLineFits(line.Text, gave),
                            "rest" => Calls.RestLineFits(line.Text, power, speaker == healer ? healerMana : 70),
                            "root" => Calls.RootLineFits(line.Text, count),
                            _ => true,
                        }))
                    {
                        GroupSay(now, speaker, call.Text, "combat:" + topic);
                        return;
                    }
                    groupState.LastCall = long.MinValue / 2;
                    callStates[speaker.Id].LastCall = long.MinValue / 2;
                }
            }

            void Grats(long now, string target, Bot[] from, string channel)
            {
                var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = target };
                for (int i = 0; i < from.Length; i++)
                {
                    Bot bot = from[i];
                    long at = now + 1_200 + i * 1_700 + random.Next(1_500);
                    Later(at, () =>
                    {
                        if (Pick(at, "grats", bot, out VC.Picked line, topic: "general", extra: slots, repeat: true))
                            Say(at, channel, line.Text, bot.Id, false, "grats", 0, 0);
                    });
                }
            }

            void GroupTick(long now)
            {
                if (!joined)
                {
                    if (now == start + 60_000)
                    {
                        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["destination"] = playerZone, ["monsters"] = VC.Plural(leader.Monster) };
                        string text = Pick(now, "tell", leader, out VC.Picked invite, topic: "lead_invite", flags: ["lead_call"], extra: slots)
                            ? invite.Text
                            : $"lf 1 more for {VC.Plural(leader.Monster)} in {playerZone}, want in?";
                        Say(now, "tell", text, leader.Id, false, "tell:lead_invite", 0, 0);
                    }
                    if (now == start + 105_000)
                    {
                        joined = true;
                        Sys(now, $"You join the group. Leader: {leader.Name}. Members: {string.Join(", ", party.Select(bot => $"{bot.Name} ({bot.Level} {bot.Class})"))}");
                        Lead(now, BotLedGroups.Lead.Welcome, meet, playerZone);
                    }
                    return;
                }
                if (now == start + 5 * 60_000)
                    Lead(now, BotLedGroups.Lead.Move, meet, playerZone);
                if (now == start + 108 * 60_000)
                {
                    Lead(now, BotLedGroups.Lead.LastPull, meet, playerZone);
                    // #477 (evenings.md, sim errors): after "last pull" one more fight at most, then none; the party used to fight on
                    // until the town break.
                    lastPull = true;
                }
                if (now == start + 114 * 60_000)
                {
                    Lead(now, BotLedGroups.Lead.TownBreak, meet, playerZone);
                    nextFight = long.MaxValue;
                }
                // Scripted events.
                if (events.TryGetValue(now, out string what))
                {
                    switch (what)
                    {
                        case "mate_dies":
                            if (!fighting)
                            {
                                events[now + 5_000] = "mate_dies";
                                break;
                            }
                            downMate = party[3];
                            alive--;
                            Sys(now, $"{downMate.Name} was just killed by a {downMate.Monster}!");
                            break;
                        case "player_dies":
                            random = streams["fights"];
                            if (!fighting)
                            {
                                events[now + 5_000] = "player_dies";
                                break;
                            }
                            playerDead = true;
                            alive--;
                            Sys(now, $"You have died. Type /release to return to your last bind point.");
                            // AutonomousBotGroupChatter.OnPlayerDying: three times in four a groupmate says so, a rezzer first.
                            if (random.NextDouble() < 0.75)
                            {
                                Bot speaker = healer ?? party[random.Next(party.Length)];
                                long at = now + 1_500 + random.Next(2_000);
                                Later(at, () =>
                                {
                                    if (alive * 2 >= 6 && Pick(at, "combat", speaker, out VC.Picked down, topic: "player_down", flags: ["in_combat"]))
                                        GroupSay(at, speaker, down.Text, "combat:player_down");
                                });
                            }
                            break;
                        case "mate_dings":
                        {
                            Bot dinger = party[2];
                            dinger.Level++;
                            Sys(now, $"{dinger.Name} glows: reached level {dinger.Level}.");
                            Grats(now, dinger.Name, party.Where(bot => bot != dinger).OrderBy(_ => random.Next()).Take(random.Next(1, 4)).ToArray(), "group");
                            break;
                        }
                        case "player_dings":
                            Sys(now, $"You have reached level {s.Level + 1}!");
                            Grats(now, Player, party.OrderBy(_ => random.Next()).Take(random.Next(1, 4)).ToArray(), "group");
                            break;
                        case "mate_leaves":
                            Sys(now, $"{party[4].Name} has left the group.");
                            break;
                        case "mate_joins":
                            Sys(now, $"{party[4].Name} has joined the group.");
                            break;
                    }
                }
                // The fights.
                random = streams["fights"];
                if (!fighting && now >= nextFight)
                {
                    fighting = true;
                    fightNo++;
                    enemies = 1 + (random.NextDouble() < 0.35 ? 1 : 0) + (random.NextDouble() < 0.1 ? 1 : 0);
                    fightEnd = now + 25_000 + random.Next(45_000);
                    if (playerDead)
                    {
                        playerDead = false;
                        alive++;
                    }
                    if (downMate != null)
                    {
                        downMate = null;
                        alive++;
                    }
                    healerMana = Math.Max(5, healerMana - 10 - random.Next(18));
                }
                if (fighting && now < fightEnd && random.NextDouble() < 0.012)
                    enemies++;
                bool worn = healerMana < 40;
                var view = new Calls.GroupView(fighting && now < fightEnd, fighting ? enemies : 0, 6, alive, worn, playerDead, fighting && !playerDead, xp);
                if (fighting && now >= fightEnd)
                {
                    xp += 1_000;
                    view = view with { Fighting = false, Enemies = 0, Experience = xp };
                }
                string topic = Calls.GroupEvent(groupState, view, now, random, botsLead: true);
                if (topic != null && (topic == "wipe" || now - groupState.LastCall >= Calls.GroupGapMs))
                    CombatCall(now, topic, null);
                if (fighting && now < fightEnd)
                {
                    // The bots' own calls: the healer's power, a tank's health now and then, and the spells they cast.
                    if (healer != null && healer != downMate &&
                        Calls.SelfEvent(callStates[healer.Id], true, 80, healerMana, true, now, random) is { } own && Calls.Gate(groupState, callStates[healer.Id], own, now))
                        CombatCall(now, own, healer);
                    if (random.NextDouble() < 0.004 && Calls.SelfEvent(callStates[leader.Id], true, 22, 50, false, now, random) is { } low &&
                        Calls.Gate(groupState, callStates[leader.Id], low, now))
                        CombatCall(now, low, leader);
                    else
                        Calls.SelfEvent(callStates[leader.Id], true, 80, 50, false, now, random);
                    if (random.NextDouble() < 0.03)
                    {
                        Bot caster = party[random.Next(party.Length)];
                        string cast = Healers.Contains(caster.Class) ? (downMate != null || playerDead) && random.NextDouble() < 0.5 ? "rez" : "heal_big"
                            : Tanks.Contains(caster.Class) ? "taunt"
                            : enemies >= 2 ? "mez" : random.NextDouble() < 0.5 ? "root" : "stun";
                        Calls.BotState state = callStates[caster.Id];
                        if (caster != downMate && (!state.LastCast.TryGetValue(cast, out long last) || now - last >= Calls.CastGapMs) &&
                            random.NextDouble() < Calls.CastChance(cast))
                        {
                            state.LastCast[cast] = now;
                            if (Calls.Gate(groupState, state, cast, now))
                                CombatCall(now, cast, caster);
                        }
                    }
                }
                if (fighting && now >= fightEnd)
                {
                    fighting = false;
                    nextFight = lastPull ? long.MaxValue : now + 45_000 + random.Next(105_000) + (worn ? 60_000 : 0);
                    if (worn)
                        healerMana = 90;
                }
                if (!fighting)
                    healerMana = Math.Min(100, healerMana + (now % 4_000 == 0 ? 1 : 0));
                // AutonomousBotGroupChatter.TryGroupLine: the banter, out of combat, on the group channel's budget.
                random = streams["group"];
                if (!fighting && now % 5_000 == 0)
                {
                    Bot bot = party[random.Next(party.Length)];
                    if (bot == downMate || random.NextDouble() >= 0.55)
                        return;
                    if (!groupKnown)
                    {
                        groupKnown = true;
                        meter.Delay(Budget.Channel.Group, 1, now + AutonomousBotGroupChatter.NextGapMs(true, random));
                        return;
                    }
                    var verdict = meter.TryStart(Budget.Channel.Group, 1, now, Budget.Band(settings, Budget.Channel.Group, 0, 20, false, Rate), random);
                    if (verdict is Budget.Verdict.HoldCeiling or Budget.Verdict.HoldGap)
                        return;
                    bool hunted = Calls.HuntedTogether(groupState, now);
                    bool lowNow = bot == healer && healerMana < 40;
                    if (!Pick(now, "group", bot, out VC.Picked said, filter: line => (hunted || !Calls.IsHuntTalk(line.Text)) && (lowNow || !Calls.ClaimsLow(line.Text)) &&
                                                                               AutonomousChatEngagement.FitsGroup(line.Text, 6, false)))
                        return;
                    long ex = ++exchanges;
                    GroupSay(now, bot, said.Text, "group:ambient/" + said.Line?.Topic, ex, 0);
                    string[] chain = VC.AnswerChain(said.Line?.Invites);
                    if (chain.Length == 0 || AutonomousBotChatCoordinator.BotReplyCount(said.Line.Invites, 1, random) == 0)
                        return;
                    Bot mate = party.Where(other => other != bot && other != downMate).OrderBy(_ => random.Next()).First();
                    var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = bot.Name };
                    foreach (string answers in chain)
                    {
                        if (!Pick(now, "reply", mate, out VC.Picked reply, answers: answers, extra: slots,
                                filter: line => AutonomousBotVoices.ShapedFits(answers, said.Text, line.Text) && AutonomousChatEngagement.FitsGroup(line.Text, 6, false)))
                            continue;
                        long at = now + AutonomousBotChatCoordinator.ReplyDelayMs(reply.Text, 0, random);
                        Later(at, () => GroupSay(at, mate, reply.Text, "group:reply:" + answers, ex, 1));
                        break;
                    }
                }
            }

            // ---- solo events near the player (AutonomousLivingServer.Engage, Events.Grats).
            void SoloEvent(long now, string what)
            {
                Bot[] near = all.Where(bot => bot.NearPlayer).OrderBy(_ => random.Next()).ToArray();
                switch (what)
                {
                    case "solo_death":
                        Sys(now, "You have died. Type /release to return to your last bind point.");
                        if (near.Length > 0 && random.NextDouble() < 0.6)
                        {
                            long at = now + 2_500 + random.Next(3_000);
                            string text = Styled(near[0], AutonomousChatEngagement.DeathSympathy(Player, false, random));
                            Later(at, () => Say(at, "say", text, near[0].Id, false, "engage:death", 0, 0));
                        }
                        break;
                    case "solo_ding":
                        Sys(now, $"You have reached level {s.Level + 1}!");
                        Grats(now, Player, near.Take(random.Next(0, 3)).ToArray(), "say");
                        break;
                    case "near_bot_dings":
                        if (near.Length > 0)
                        {
                            near[0].Level++;
                            Sys(now, $"{near[0].Name} glows beside you: reached level {near[0].Level}.");
                            Grats(now, near[0].Name, near.Skip(1).Take(random.Next(0, 2)).ToArray(), "say");
                        }
                        break;
                    case "near_bot_dies":
                        if (near.Length > 0)
                            Sys(now, $"{near[0].Name} was just killed by a {near[0].Monster}! (in sight of you)");
                        break;
                }
            }

            // ---- bot tells to the player (AutonomousLivingServer.Tells: a duo offer from a solo bot of about the player's level).
            long nextTell = start + AutonomousLivingServer.NextTellGap(true, random);
            Bot lastTeller = null;
            void TellTick(long now)
            {
                if (now < nextTell || s.Grouped)
                    return;
                nextTell = now + AutonomousLivingServer.NextTellGap(false, random);
                Bot bot = all.Where(b => b.Solo && !b.InGroup && AutonomousLivingServer.FitsGroupOffer(b.Level, s.Level, false, true, 3_000)).OrderBy(_ => random.Next()).FirstOrDefault();
                if (bot == null)
                    return;
                bot.Zone = playerZone;
                lastTeller = bot;
                if (random.NextDouble() < AutonomousLivingServer.RealInviteShare)
                {
                    Say(now, "tell", AutonomousChatEngagement.DuoInviteTell(Player, false, random), bot.Id, false, "tell:duo_invite", 0, 0);
                    Sys(now + 1, AutonomousChatEngagement.InviteDialog(bot.Name, bot.Level, bot.Class));
                }
                else if (Pick(now, "tell", bot, out VC.Picked offer, topic: "group_offer", extra: new Dictionary<string, string> { ["player"] = Player }))
                    Say(now, "tell", offer.Text, bot.Id, false, "tell:group_offer", 0, 0);
            }

            // ---- the player's own lines, through the question bench's reply path.
            Typed[] script = speaks ? ScriptOf(s) : [];
            int typed = 0;
            void PlayerTick(long now)
            {
                while (typed < script.Length && start + (long)(script[typed].Minute * 60_000) <= now)
                {
                    Typed line = script[typed++];
                    string text = line.Text;
                    Bot mate = party.Length > 0 ? party[1 + typed % (party.Length - 1)] : null;
                    if (text.Contains("{mate}"))
                    {
                        if (mate == null)
                            continue;
                        text = text.Replace("{mate}", mate.Name);
                    }
                    text = text.Replace("{place}", meet).Replace("{keep}", WorldOf(realm, now).OwnKeeps[1]);
                    string channel = line.Channel;
                    Bot responder;
                    string to = null;
                    switch (channel)
                    {
                        case "tell":
                            responder = s.Grouped ? leader : lastTeller;
                            if (responder == null)
                                continue;
                            rows.Add(new Row(now - start, "tell", Player, "player>" + responder.Name, 0, 0, text));
                            break;
                        case "group":
                            if (!joined)
                                continue;
                            rows.Add(new Row(now - start, "group", Player, "player", 0, 0, text));
                            responder = text.Contains(mate.Name) ? mate : party[random.Next(party.Length)];
                            to = text.Contains(mate.Name) ? mate.Name : null;
                            if (now - lastPlayerGroupReply < AutonomousBotGroupChatter.PlayerReplyCooldownMs)
                                continue;
                            lastPlayerGroupReply = now;
                            break;
                        case "say":
                            rows.Add(new Row(now - start, "say", Player, "player", 0, 0, text));
                            responder = all.Where(b => b.NearPlayer).OrderBy(_ => random.Next()).FirstOrDefault();
                            if (responder == null)
                                continue;
                            break;
                        case "trade":
                        {
                            rows.Add(new Row(now - start, "trade", Player, "player", 0, 0, text));
                            // AutonomousBotChatCoordinator.OnPlayerTradePost.
                            AutonomousChatMarket.Post post = AutonomousChatMarket.PostOf(text);
                            if (post is AutonomousChatMarket.Post.Wts or AutonomousChatMarket.Post.Wtb && random.NextDouble() < AutonomousChatMarket.ReplyShare(post))
                            {
                                Bot replier = all.OrderBy(_ => random.Next()).Take(200)
                                    .FirstOrDefault(b => !b.InGroup && AutonomousChatMarket.CanReply(post, text, s.Level, FactsOf(b, now), b.Solo));
                                if (replier != null && AutonomousChatMarket.ReplyTo(post, text, FactsOf(replier, now), replier.Solo, false, random) is { Text: { Length: > 0 } said })
                                    dueReplies.Add((now + AutonomousChatArbiter.DelayMs(text, said, 40, 0, random) + 4_000 + random.Next(10_000), "trade",
                                        AutonomousChatIdiolect.Apply(said, replier.Id, false, random, terse: true), replier.Id, false, "to-player:market", 0, 1));
                            }
                            continue;
                        }
                        default:
                            rows.Add(new Row(now - start, "faction", Player, "player", 0, 0, text));
                            lastPlayerFaction = now;
                            responder = Near(now, all[0], _ => true, 0.4) ?? all[random.Next(all.Count)];
                            break;
                    }
                    try
                    {
                        var bench = new UT_ChatQuestionBench.Bench
                        {
                            Questions = [new UT_ChatQuestionBench.Question("evening", text, channel, realm,
                                s.Level <= 5 ? realm switch { eRealm.Midgard => "Seer", eRealm.Hibernia => "Naturalist", _ => "Acolyte" } : PartyOf[realm][1],
                                s.Level, "evening", to)],
                            World = Loaded.World, Towns = Loaded.Towns,
                        };
                        bench.Responders[realm] = new UT_ChatQuestionBench.Responder(responder.Id, responder.Name, responder.Class, responder.Level, responder.Zone,
                            responder.Monster, Loaded.Responders[realm].Cls == responder.Class ? Loaded.Responders[realm].Spec : null);
                        UT_ChatQuestionBench.Answer answer = UT_ChatQuestionBench.AnswerAll(bench)[0];
                        if (answer.Reply == null)
                            rows.Add(new Row(now - start + 1, "meta", "", "to-player:none", 0, 0, $"no bot answers \"{text}\" ({answer.Route})"));
                        else
                            dueReplies.Add((now + Math.Max(1_500, answer.DelayMs), channel, answer.Reply, responder.Id, true,
                                $"to-player:{answer.Route}|answerers={answer.Answerers}|stub={answer.Stub}", -typed, 1));
                    }
                    catch (Exception exception)
                    {
                        rows.Add(new Row(now - start + 1, "meta", "", "sim-error", 0, 0, $"the bench threw on \"{text}\": {exception.GetType().Name} {exception.Message}"));
                    }
                }
            }

            // ------------------------------------------------------------------------------------------------------- the clock
            for (long now = 0; now < end; now += 1_000)
            {
                random = streams["scenes"];
                if (now >= debateUntil && adsLeft < 0 && Budget.SceneDue(debatePlanned, debateDue, now, Bots))
                {
                    StartDebate(now);
                    (debatePlanned, debateDue) = (now, Math.Max(now, debateUntil) + AutonomousBotDebates.NextGapMs(random));
                }
                if (adsLeft < 0 && now >= debateUntil && Budget.SceneDue(wavePlanned, waveDue, now, Bots))
                {
                    adsLeft = Budget.SceneLines(AutonomousGoldSpammers.AdsMin + random.Next(AutonomousGoldSpammers.AdsSpread), Bots);
                    adsSaid = 0;
                    nextAd = now;
                    spammer--;
                }
                if (adsLeft >= 0 && now >= nextAd)
                {
                    if (adsLeft > 0)
                    {
                        adsLeft--;
                        long ex = ++exchanges;
                        if (Pick(now, "spam", all[random.Next(all.Count)], out VC.Picked ad))
                            Say(now, "faction", ad.Text, spammer, false, "spam", ex, 0);
                        if (++adsSaid <= 3 && random.NextDouble() < 0.8)
                            SpamReact(now, 1 + random.Next(2), 4_000, ex);
                        nextAd = now + 18_000 + random.Next(30_000);
                    }
                    else
                    {
                        SpamReact(now, 1 + random.Next(3), 30_000 + random.Next(60_000), ++exchanges);
                        adsLeft = -1;
                        (wavePlanned, waveDue) = (now, now + AutonomousGoldSpammers.WaveMinMs + random.Next((int)AutonomousGoldSpammers.WaveSpreadMs));
                    }
                }
                // Stories from real events (AutonomousBotChatCoordinator.TellStory), at the budget simulation's modelled rate.
                random = streams["stories"];
                if (now % 60_000 == 0 && random.NextDouble() < 0.17 * Bots / 1_000.0)
                {
                    Bot teller = all[random.Next(all.Count)];
                    Bot mate = Near(now, teller, _ => true), third = Near(now, teller, other => other != mate);
                    double roll = random.NextDouble();
                    string story = roll < 0.6 ? "death" : roll < 0.95 ? "wipe" : "ding50";
                    bool riffs = ReplyGate("faction", now);
                    long ex = ++exchanges;
                    int index = 0;
                    if (mate != null)
                        foreach (Field.Step step in Field.Story(story, FactsOf(teller, now), FactsOf(mate, now), third == null ? null : FactsOf(third, now), random, field))
                            if (step.Who == 0 || riffs)
                                dueReplies.Add((now + 2_000 + step.AtMs, "faction", step.Text, (step.Who switch { 0 => teller, 1 => mate, _ => third ?? mate }).Id,
                                    step.Who != 0, "story:" + story + ":" + step.Who, ex, index++));
                }
                if (s.Grouped)
                {
                    random = streams["group"];
                    GroupTick(now);
                }
                else if (events.TryGetValue(now, out string solo))
                {
                    random = streams["solo"];
                    SoloEvent(now, solo);
                }
                random = streams["tell"];
                TellTick(now);
                random = streams["player"];
                PlayerTick(now);
                for (int i = groupDue.Count - 1; i >= 0; i--)
                    if (groupDue[i].At <= now)
                    {
                        Action act = groupDue[i].Act;
                        groupDue.RemoveAt(i);
                        act();
                    }
                // #477 (evenings.md, sim errors): the lines due now in their own order (time, exchange, place in it), and a deferred line
                // takes the later lines of its exchange with it, so a thread's lines never swap.
                foreach (var due in dueReplies.Where(d => d.At <= now).OrderBy(d => d.At).ThenBy(d => d.Ex).ThenBy(d => d.I).ToList())
                    {
                        int i = dueReplies.IndexOf(due);
                        if (i < 0)
                            continue;   // moved on with an earlier line of its exchange
                        random = streams[Streams.OfChannel(due.Channel)];
                        // AutonomousBotChatCoordinator.PlayThread/PlayStory: not within 2 s of the channel's last line.
                        if (due.Channel == "faction" && now - lastFactionLine < 2_000 && deferred.GetValueOrDefault(due.Text) < 3)
                        {
                            deferred[due.Text] = deferred.GetValueOrDefault(due.Text) + 1;
                            long later = now + 2_000 + random.Next(2_000) - due.At;
                            for (int k = 0; k < dueReplies.Count; k++)
                                if (dueReplies[k].Ex == due.Ex && due.Ex != 0 && dueReplies[k].I >= due.I && dueReplies[k].Channel == due.Channel)
                                    dueReplies[k] = dueReplies[k] with { At = dueReplies[k].At + later };
                            if (due.Ex == 0)
                                dueReplies[i] = due with { At = due.At + later };
                            continue;
                        }
                        string said = due.Text;
                        if (due.Src.StartsWith("thread:", StringComparison.Ordinal) && byId.TryGetValue(due.Who, out Bot speaker))
                        {
                            if (due.Channel == "faction" && opened.TryGetValue(due.Ex, out var open) && open.Starter != due.Who)
                                said = Field.NamedWhenApart(said, Who(open.Starter),
                                    realmLog.Count(line => line.At >= open.At && line.Who != open.Starter && line.Who != due.Who));
                            said = Generated(speaker, said);
                        }
                        Say(now, due.Channel, said, due.Who, due.Reply, due.Src, due.Ex, due.I);
                        dueReplies.RemoveAt(i);
                    }
                double Pressure(Budget.Channel channel, int count)
                {
                    double target = Budget.Band(settings, channel, count, 20, false, Rate).Target;
                    return Budget.Pressure(meter.AmbientPerMinute(channel, 1, now, target), target);
                }
                double factionPressure = Math.Max(Pressure(Budget.Channel.Faction, Bots), Math.Max(Pressure(Budget.Channel.Lfg, Bots), Pressure(Budget.Channel.Trade, Bots))),
                    sayPressure = Pressure(Budget.Channel.Say, Bots), guildPressure = Pressure(Budget.Channel.Guild, guildSize);
                foreach (Bot bot in all)
                {
                    if (bot.NearPlayer)
                        SayCheck(bot, now, sayPressure);
                    random = streams["ambient"];
                    if (bot.NextCheck > now)
                        continue;
                    bot.NextCheck = now + 15_000 + random.Next(20_001) + bot.Id % 4_000;
                    if (random.NextDouble() >= 0.55 || bot.InGroup)
                        continue;   // fighting, casting or riding; the party's talk is GroupTick
                    double pressure = Math.Max(Math.Max(factionPressure, 1), bot.InGuild ? guildPressure : 1);   // #740: /say's pressure is SayCheck's
                    bool regular = Regular(bot, now);
                    DateTime clock = ClockAt(now);
                    if (!AutonomousBotChat.ShouldSpeak(clock, clock.AddMilliseconds(bot.LastSpoke - now), false, random, bot.Rate, pressure, regular))
                        continue;
                    if (bot.InGuild && (regular || random.NextDouble() < AutonomousChatRegulars.OthersShare ||
                                        meter.PerMinute(Budget.Channel.Guild, 1, now) < Budget.Band(settings, Budget.Channel.Guild, guildSize, 20, false, Rate).Floor))
                    {
                        random = streams["guild"];
                        bool turn = meter.TryStart(Budget.Channel.Guild, 1, now, Budget.Band(settings, Budget.Channel.Guild, guildSize, 20, false, Rate), random)
                            is Budget.Verdict.Speak or Budget.Verdict.SpeakUnderFloor;
                        if (turn && random.NextDouble() < Field.Share("guild") &&
                            Field.Line("guild", FactsOf(bot, now) with
                                {
                                    GuildmateDinged = random.NextDouble() < 0.04 ? all[random.Next(guildSize)].Name : null,
                                    GuildKeep = WorldOf(realm, now).OwnKeeps[2],
                                }, guildField, random, now) is { } gsaid)
                        {
                            long ex = ++exchanges;
                            Say(now, "guild", AutonomousChatIdiolect.Apply(gsaid.Text, bot.Id, false, random, terse: true), bot.Id, false, "field:" + gsaid.Kind, ex, 0);
                            Bot gmate = all[random.Next(guildSize)], gthird = all[random.Next(guildSize)];
                            if (gmate == bot) gmate = null;
                            if (gthird == bot || gthird == gmate) gthird = null;
                            Play(now, "guild", Field.Thread(gsaid, FactsOf(bot, now), gmate == null ? null : FactsOf(gmate, now),
                                gthird == null ? null : FactsOf(gthird, now), guildField, random, now), [bot, gmate, gthird], ex);
                            bot.LastSpoke = now;
                            continue;
                        }
                        if (turn && Pick(now, "guild", bot, out VC.Picked gl, filter: line => AutonomousChatFiller.GuildCorpusFits(line, guildField, now)))
                        {
                            AutonomousChatFiller.NoteGuildCorpus(gl.Line, guildField, now);
                            Say(now, "guild", gl.Text, bot.Id, false, "written:guild/" + gl.Line?.Topic, ++exchanges, 0);
                            bot.LastSpoke = now;
                            continue;
                        }
                    }
                    if (Budgeted(bot, now))
                        bot.LastSpoke = now;
                }
            }
            return rows.OrderBy(row => row.At).ToList();
        }

        // ------------------------------------------------------------------------------------------------------------------ run

        /// <summary>#622 test hook: the Albion bot-led dungeon evening (the player grouped and speaking), with one stream skewed.</summary>
        internal static List<Row> DungeonEvening(int seed, string skew, int minutes = 60) =>
            Evening(new Scenario("L24-dungeon-alb", eRealm.Albion, 24, "dungeon", true, true), seed, true, minutes, skew);

        /// <summary>#740 test hook: an Albion evening of <paramref name="where"/> (hills, dungeon or frontier, the player speaking), one stream skewed.</summary>
        internal static List<Row> EveningAt(string where, int seed, string skew, int minutes = 60) => where switch
        {
            "hills" => Evening(new Scenario("L5-hills-alb", eRealm.Albion, 5, "hills", false, false), seed, true, minutes, skew),
            "frontier" => Evening(new Scenario("L50-frontier-alb", eRealm.Albion, 50, "frontier", false, true), seed, true, minutes, skew),
            _ => DungeonEvening(seed, skew, minutes),
        };

        private static string Clean(string text) => (text ?? string.Empty).Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');

        /// <summary>The 18 evenings: three scenarios in each realm, Albion with two seeds, each with the player speaking and silent.</summary>
        internal static string Play(out int evenings)
        {
            var scenarios = new List<(Scenario S, int Seed, bool Speaks)>();
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
            {
                string code = realm switch { eRealm.Albion => "alb", eRealm.Midgard => "mid", _ => "hib" };
                Scenario[] three =
                [
                    new("L5-hills-" + code, realm, 5, "hills", false, false),
                    new("L24-dungeon-" + code, realm, 24, "dungeon", true, true),
                    new("L50-frontier-" + code, realm, 50, "frontier", false, true),
                ];
                foreach (Scenario s in three)
                {
                    scenarios.Add((s, 1101, true));
                    if (realm == eRealm.Albion)
                    {
                        scenarios.Add((s, 1101, false));
                        scenarios.Add((s, 2202, true));
                        scenarios.Add((s, 2202, false));
                    }
                }
            }
            var text = new StringBuilder();
            evenings = 0;
            foreach ((Scenario s, int seed, bool speaks) in scenarios)
            {
                string id = $"{s.Key}\t{seed}\t{(speaks ? "speaks" : "silent")}";
                try
                {
                    List<Row> rows = Evening(s, seed, speaks);
                    foreach (Row row in rows)
                        text.Append(id).Append('\t').Append(row.At / 1000).Append('\t').Append(row.Channel).Append('\t').Append(Clean(row.Who)).Append('\t')
                            .Append(Clean(row.Src)).Append('\t').Append(row.Ex).Append('\t').Append(row.I).Append('\t').Append(Clean(row.Text)).Append('\n');
                    evenings++;
                }
                catch (Exception exception)
                {
                    text.Append(id).Append("\t0\tmeta\t\tsim-error\t0\t0\t").Append(Clean(exception.ToString())).Append('\n');
                }
            }
            return text.ToString();
        }
    }

    /// <summary>
    /// The player's evenings (PlayerEvenings): writes evening-transcripts/evenings.tsv (EVENINGS_OUT to put it elsewhere), and with
    /// EVENING_PRINT_ROWS=1 prints every row as EVENING_ROW. The CI step "Evening transcripts ratchet" runs evening_read.py on it.
    /// </summary>
    // #622: one random stream per subsystem in the player's evenings.
    [Test]
    public void EachEveningStreamHasAStableSeedOfItsOwn()
    {
        Assert.That(PlayerEvenings.Streams.SeedOf(1101, "setup"), Is.EqualTo(1101), "the roster is the one the single stream drew");
        // FNV-1a of the name over the evening's seed: the same on every machine and run (string.GetHashCode is not).
        Assert.That(PlayerEvenings.Streams.SeedOf(1101, "group"), Is.EqualTo(484401835));
        Assert.That(PlayerEvenings.Streams.SeedOf(1101, "trade"), Is.EqualTo(1427446806));
        int[] seeds = PlayerEvenings.Streams.Names.Select(name => PlayerEvenings.Streams.SeedOf(1101, name)).ToArray();
        Assert.That(seeds, Is.Unique);
        Assert.That(PlayerEvenings.Streams.OfChannel("tell"), Is.EqualTo("tell"));
        Assert.That(PlayerEvenings.Streams.OfChannel("meta"), Is.EqualTo("ambient"));
        Assert.That(PlayerEvenings.Streams.Names.Contains(PlayerEvenings.Streams.OfChannel("group")));
    }

    [Test]
    public void AGroupChangeLeavesTheTradeLinesAndTheFightCallsAlone()
    {
        List<PlayerEvenings.Row> before = PlayerEvenings.DungeonEvening(1101, null);
        List<PlayerEvenings.Row> again = PlayerEvenings.DungeonEvening(1101, null);
        Assert.That(again, Is.EqualTo(before), "control: the same evening twice is the same (no state carried between runs)");
        List<PlayerEvenings.Row> after = PlayerEvenings.DungeonEvening(1101, "group");
        static bool Banter(PlayerEvenings.Row row) => row.Channel == "group" && row.Src.StartsWith("group:", StringComparison.Ordinal);
        Assert.That(after.Where(Banter).Select(row => (row.At, row.Text)), Is.Not.EqualTo(before.Where(Banter).Select(row => (row.At, row.Text))),
            "the skew changed the /group banter (else this test proves nothing)");
        static bool Trade(PlayerEvenings.Row row) => row.Channel == "trade";
        Assert.That(after.Where(Trade).Select(row => (row.At, row.Who, row.Src, row.Text)),
            Is.EqualTo(before.Where(Trade).Select(row => (row.At, row.Who, row.Src, row.Text))), "/trade unchanged");
        static bool Call(PlayerEvenings.Row row) => row.Src.StartsWith("combat:", StringComparison.Ordinal);
        Assert.That(after.Where(Call).Select(row => (row.At, row.Src)), Is.EqualTo(before.Where(Call).Select(row => (row.At, row.Src))),
            "the fight calls come at the same moments");
    }

    // #740 (owner, 2026-11-27, "24A"): /say keeps its own state, so nothing the realm's channels say can move a /say line: their
    // draws (a /faction text change, as #528's inc calls made), the channel they pick or the scenes. Each skew must change
    // /faction (else the test proves nothing) and leave every /say row (time, speaker, source, exchange, text) as it was.
    [TestCase("frontier", "faction")]
    [TestCase("hills", "faction")]
    [TestCase("hills", "ambient")]
    [TestCase("frontier", "scenes")]
    public void ARealmChannelChangeLeavesSayAlone(string where, string skew)
    {
        List<PlayerEvenings.Row> before = PlayerEvenings.EveningAt(where, 1101, null);
        List<PlayerEvenings.Row> after = PlayerEvenings.EveningAt(where, 1101, skew);
        static bool Faction(PlayerEvenings.Row row) => row.Channel == "faction";
        Assert.That(after.Where(Faction).Select(row => (row.At, row.Text)), Is.Not.EqualTo(before.Where(Faction).Select(row => (row.At, row.Text))),
            $"the {skew} skew changed /faction (else this test proves nothing)");
        static bool Say(PlayerEvenings.Row row) => row.Channel == "say";
        List<PlayerEvenings.Row> said = before.Where(Say).ToList();
        Assert.That(said.Count(row => row.Src != "player"), Is.GreaterThan(5), "the evening has bot /say lines to compare");
        Assert.That(after.Where(Say), Is.EqualTo(said), $"/say unchanged by the {skew} skew");
    }

    [Test]
    public void ASayChangeStillChangesSay()
    {
        // The control: the /say stream does move /say (the isolation is not /say gone quiet or fixed).
        List<PlayerEvenings.Row> before = PlayerEvenings.EveningAt("hills", 1101, null);
        List<PlayerEvenings.Row> after = PlayerEvenings.EveningAt("hills", 1101, "say");
        static bool Say(PlayerEvenings.Row row) => row.Channel == "say" && row.Src != "player";
        Assert.That(after.Where(Say), Is.Not.EqualTo(before.Where(Say)));
    }

    [Test]
    public void ATradeChangeLeavesTheFightCallsAlone()
    {
        List<PlayerEvenings.Row> before = PlayerEvenings.DungeonEvening(1101, null);
        List<PlayerEvenings.Row> after = PlayerEvenings.DungeonEvening(1101, "trade");
        static bool Trade(PlayerEvenings.Row row) => row.Channel == "trade";
        Assert.That(after.Where(Trade).Select(row => (row.At, row.Text)), Is.Not.EqualTo(before.Where(Trade).Select(row => (row.At, row.Text))),
            "the skew changed /trade (else this test proves nothing)");
        static bool Call(PlayerEvenings.Row row) => row.Src.StartsWith("combat:", StringComparison.Ordinal);
        Assert.That(after.Where(Call).Select(row => (row.At, row.Src, row.Text)),
            Is.EqualTo(before.Where(Call).Select(row => (row.At, row.Src, row.Text))), "the fight calls unchanged");
    }

    [Test]
    public void PlaysThePlayersEvenings()
    {
        string text = PlayerEvenings.Play(out int evenings);
        string path = Environment.GetEnvironmentVariable("EVENINGS_OUT") is { Length: > 0 } output
            ? output
            : Path.Combine(TestContext.CurrentContext.WorkDirectory, "evening-transcripts", "evenings.tsv");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
        if (Environment.GetEnvironmentVariable("EVENING_PRINT_ROWS") == "1")
            foreach (string row in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
                TestContext.Progress.WriteLine("EVENING_ROW\t" + row);
        TestContext.Progress.WriteLine($"EVENINGS {evenings} evenings, {text.Length} characters: {path}");
        Assert.That(evenings, Is.EqualTo(18), "every evening played");
        Assert.That(text, Does.Not.Contain("\tsim-error\t"), "no evening threw");
    }
}
