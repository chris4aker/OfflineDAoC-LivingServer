using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS;
using DOL.GS.Effects;
using DOL.GS.PropertyCalc;
using DOL.GS.Spells;
using NUnit.Framework;
using OfflineDaoc.Configuration;
using static DOL.UnitTests.DutyLeaseTestKit;

namespace DOL.UnitTests
{
    /// <summary>Shared helpers of the set A row tests (#625).</summary>
    internal static class LivingRowsTestKit
    {
        public static string Field(string row, string name)
        {
            Match match = Regex.Match(row, $@"(?:^| ){name}=(""[^""]*""|\S+)");
            return match.Success ? match.Groups[1].Value.Trim('"') : null;
        }

        public static void SetTime(long value) =>
            typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime))!.SetValue(null, value);

        /// <summary>Every key=value field of a row, in order, without the marker.</summary>
        public static string[] Keys(string row) =>
            Regex.Matches(row, @"(?:^| )([A-Za-z]+)=").Select(match => match.Groups[1].Value).ToArray();
    }

    /// <summary>
    /// #625 set A: the rows BG_POPULATION, CC_BREAK, BG_TARGET_PICK, BG_RALLY_RESPONSE and PLAYER_LINE_OUTCOME are written with
    /// their fields, never carry a player's name, and stay low-volume at about 3,000 bots. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_LivingRowsSetA : DutyLeaseFixture
    {
        private readonly List<string> _rows = [];
        private long _oldTime;

        [SetUp]
        public void Capture()
        {
            _oldTime = GameLoop.GameLoopTime;
            LivingRowsTestKit.SetTime(1_000_000);
            RvrRecordBudget.Reset();
            PlayerLineOutcomes.Reset();
            BgRallyResponseRows.Reset();
            BgTargetPickRows.Sampler.Reset();
            BotSayGate.ResetForTests();
            _rows.Clear();
            LivingRows.TestSink = row => { lock (_rows) _rows.Add(row); };
        }

        [TearDown]
        public void Release()
        {
            LivingRows.TestSink = null;
            PlayerLineOutcomes.Reset();
            BgRallyResponseRows.Reset();
            BotSayGate.ResetForTests();
            RvrRecordBudget.Reset();
            LivingRowsTestKit.SetTime(_oldTime);
        }

        private List<string> Rows(string tag)
        {
            lock (_rows)
                return _rows.Where(row => row.StartsWith(tag + " ", StringComparison.Ordinal)).ToList();
        }

        private static string F(string row, string name) => LivingRowsTestKit.Field(row, name);

        // ---- BG_POPULATION ---------------------------------------------------------------------------------------------

        [Test]
        public void BgPopulation_counts_goal_passing_travelling_companions_and_the_player()
        {
            var thidranki = new BattlegroundBrackets.Bracket("Thidranki", "Thidranki Faste", 252, 20, 24);
            var bots = new[]
            {
                new BgPopulationRows.Bot(252, eRealm.Albion, 22, true, true),
                new BgPopulationRows.Bot(252, eRealm.Albion, 23, true, true),
                new BgPopulationRows.Bot(252, eRealm.Albion, 21, false, true),   // passing through
                new BgPopulationRows.Bot(252, eRealm.Albion, 22, true, false),   // dead: not present
                new BgPopulationRows.Bot(1, eRealm.Albion, 24, true, true),      // travelling
                new BgPopulationRows.Bot(1, eRealm.Albion, 20, false, true),     // eligible, other goal
                new BgPopulationRows.Bot(1, eRealm.Albion, 40, true, true),      // outside the bracket
                new BgPopulationRows.Bot(252, eRealm.Midgard, 22, true, true),
            };
            List<string> lines = BgPopulationRows.Lines(5, [thidranki], bots, [new BgPopulationRows.Other(252, eRealm.Albion)],
                [new BgPopulationRows.Other(252, eRealm.Albion)], level => 0.25);
            Assert.That(lines, Has.Count.EqualTo(3), "one row per realm");
            string albion = lines.Single(line => F(line, "realm") == "Albion");
            Assert.Multiple(() =>
            {
                Assert.That(albion, Does.StartWith("BG_POPULATION "));
                Assert.That(F(albion, "bg"), Is.EqualTo("Thidranki"));
                Assert.That(F(albion, "bracket"), Is.EqualTo("20-24"));
                Assert.That(F(albion, "bots"), Is.EqualTo("3"));
                Assert.That(F(albion, "bgGoal"), Is.EqualTo("2"));
                Assert.That(F(albion, "passing"), Is.EqualTo("1"));
                Assert.That(F(albion, "travelling"), Is.EqualTo("1"));
                Assert.That(F(albion, "assigned"), Is.EqualTo("3"));
                Assert.That(F(albion, "eligible"), Is.EqualTo("6"));
                Assert.That(F(albion, "expected"), Is.EqualTo("2"), "6 x 25% rounded");
                Assert.That(F(albion, "companions"), Is.EqualTo("1"));
                Assert.That(F(albion, "player"), Is.EqualTo("1"));
                Assert.That(F(lines.Single(line => F(line, "realm") == "Hibernia"), "bots"), Is.EqualTo("0"));
            });
        }

        [Test]
        public void BgPopulation_share_reads_the_goal_settings_and_volume_is_twelve_rows_per_five_minutes()
        {
            Assert.That(BgPopulationRows.Share(BotGoalSettings.Defaults, 25), Is.EqualTo(0.25).Within(1e-9), "#610: 30/30/15/25 at 20-49");
            Assert.That(BgPopulationRows.Share(BotGoalSettings.Defaults, 17), Is.EqualTo(0.10).Within(1e-9), "#610: 55/35/0/10 at 1-19");
            var brackets = Enumerable.Range(0, BattlegroundBrackets.Count).Select(BattlegroundBrackets.Get).ToList();
            var random = new Random(625);
            var bots = Enumerable.Range(0, 3_000).Select(_ => new BgPopulationRows.Bot((ushort)random.Next(249, 254),
                (eRealm)random.Next(1, 4), random.Next(1, 51), random.Next(4) == 0, true)).ToArray();
            List<string> lines = BgPopulationRows.Lines(0, brackets, bots, [], [], level => 0.25);
            Assert.That(lines, Has.Count.EqualTo(12), "4 battlegrounds x 3 realms, whatever the bot count");
            Assert.That(60 / 5 * lines.Count, Is.EqualTo(144), "rows an hour");
        }

        // ---- CC_BREAK --------------------------------------------------------------------------------------------------

        [Test]
        public void CcBreak_classifies_mez_and_breakable_roots_only()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CcBreakRows.Classify(eEffect.Mez, eSpellType.Mesmerize, 0), Is.EqualTo("mez"));
                Assert.That(CcBreakRows.Classify(eEffect.MovementSpeedDebuff, eSpellType.SpeedDecrease, 99), Is.EqualTo("root"));
                Assert.That(CcBreakRows.Classify(eEffect.MovementSpeedDebuff, eSpellType.SpeedDecrease, 100), Is.EqualTo("root"));
                Assert.That(CcBreakRows.Classify(eEffect.MovementSpeedDebuff, eSpellType.SpeedDecrease, 60), Is.Null, "a snare");
                Assert.That(CcBreakRows.Classify(eEffect.MovementSpeedDebuff, eSpellType.UnbreakableSpeedDecrease, 99), Is.Null);
                Assert.That(CcBreakRows.Classify(eEffect.Stun, eSpellType.Stun, 0), Is.Null);
            });
        }

        [Test]
        public void CcBreak_row_has_its_fields_and_tells_the_sides_apart()
        {
            Bot caster = Gamebot();
            caster.Realm = eRealm.Albion;
            Bot ally = Gamebot();
            ally.Realm = eRealm.Albion;
            Bot enemy = Gamebot();
            enemy.Realm = eRealm.Midgard;
            Assert.Multiple(() =>
            {
                Assert.That(CcBreakRows.Side(caster, caster), Is.EqualTo("self"));
                Assert.That(CcBreakRows.Side(ally, caster), Is.EqualTo("ally"));
                Assert.That(CcBreakRows.Side(enemy, caster), Is.EqualTo("enemy"));
                Assert.That(CcBreakRows.Side(enemy, null), Is.EqualTo("none"));
            });
            string row = CcBreakRows.Format(7, "mez", "gamebot", "player", true, "gamebot", "ally", 1_250, true, 252);
            Assert.That(LivingRowsTestKit.Keys(row), Is.EqualTo(new[]
                { "t", "cc", "target", "breaker", "breakerPet", "caster", "side", "ms", "attack", "region", "bg" }));
            Assert.That(F(row, "bg"), Is.EqualTo("Thidranki"));
            Assert.That(F(row, "attack"), Is.EqualTo("spell"));
            Assert.DoesNotThrow(() => CcBreakRows.Broke(null, null, null), "logging never throws");
        }

        [Test]
        public void Kinds_tell_player_companion_gamebot_and_npc_apart()
        {
            Bot gamebot = Gamebot();
            Bot helper = Gamebot();
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(helper, false);
            Assert.Multiple(() =>
            {
                Assert.That(LivingRows.Kind(RealPlayer()), Is.EqualTo("player"));
                Assert.That(LivingRows.Kind(gamebot), Is.EqualTo("gamebot"));
                Assert.That(LivingRows.Kind(helper), Is.EqualTo("companion"));
                Assert.That(LivingRows.Kind((GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC))), Is.EqualTo("npc"));
                Assert.That(LivingRows.Kind(null), Is.EqualTo("-"));
            });
        }

        // ---- BG_TARGET_PICK, BG_RALLY_RESPONSE ---------------------------------------------------------------------

        [Test]
        public void BgTargetPick_row_has_its_fields()
        {
            string row = BgTargetPickRows.Format(40, 10, 9, "Caledonia", eRealm.Hibernia, "scan", "person", "gamebot", 2, 1, true, "rally", 4);
            Assert.That(LivingRowsTestKit.Keys(row), Is.EqualTo(new[]
            {
                "seq", "every", "t", "bg", "realm", "src", "pick", "target", "skippedControlled", "skippedEngines", "rally",
                "list", "candidates"
            }));
            Assert.That(F(row, "rally"), Is.EqualTo("1"));
            Assert.That(LivingRows.PickKind(RealPlayer()), Is.EqualTo("person"));
            Assert.That(LivingRows.PickKind(Gamebot()), Is.EqualTo("person"));
        }

        [Test]
        public void BgTargetPick_sampling_keeps_an_hour_at_3000_bots_under_600_rows()
        {
            int every = LivingRowProperties.BgTargetPickSampleEvery;
            Assert.That(every, Is.EqualTo(10), "on by default, every 10th pick");
            int rows = 0;
            // 3,000 bots, every one picking a battleground target every 5 s for an hour (far more than really fight there).
            for (int minute = 0; minute < 60; minute++)
            {
                LivingRowsTestKit.SetTime(1_000_000 + minute * 60_000L);
                for (int pick = 0; pick < 3_000 * 12; pick++)
                    if (BgTargetPickRows.Sample(out _, out _))
                        rows++;
            }
            Assert.That(rows, Is.EqualTo(60 * BgTargetPickRows.PerMinute), "the minute ceiling holds");
            Assert.That(rows, Is.LessThanOrEqualTo(600));
        }

        [Test]
        public void BgRallyResponse_times_each_bots_first_engage_of_a_posted_contact()
        {
            Bot contact = Gamebot();
            contact.Realm = eRealm.Midgard;
            BgRallyResponseRows.Post(252, eRealm.Albion, contact, 1_000_000);
            string first = BgRallyResponseRows.Engaged(252, eRealm.Albion, 11, contact, 1_001_200);
            Assert.That(BgRallyResponseRows.Engaged(252, eRealm.Albion, 11, contact, 1_001_500), Is.Null, "the same bot once");
            string second = BgRallyResponseRows.Engaged(252, eRealm.Albion, 12, contact, 1_002_000);
            Assert.That(BgRallyResponseRows.Engaged(252, eRealm.Hibernia, 13, contact, 1_002_000), Is.Null, "another realm's board");
            Assert.That(BgRallyResponseRows.Engaged(252, eRealm.Albion, 14, contact, 1_000_000 + 31_000), Is.Null, "forgotten after 30 s");
            Assert.Multiple(() =>
            {
                Assert.That(LivingRowsTestKit.Keys(first), Is.EqualTo(new[] { "t", "bg", "realm", "contact", "pet", "ms", "nth" }));
                Assert.That(F(first, "ms"), Is.EqualTo("1200"));
                Assert.That(F(first, "nth"), Is.EqualTo("1"));
                Assert.That(F(first, "contact"), Is.EqualTo("gamebot"));
                Assert.That(F(first, "bg"), Is.EqualTo("Thidranki"));
                Assert.That(F(second, "ms"), Is.EqualTo("2000"));
                Assert.That(F(second, "nth"), Is.EqualTo("2"));
                Assert.That(Rows("BG_RALLY_RESPONSE"), Has.Count.EqualTo(2));
            });
        }

        [Test]
        public void BgRallyResponse_and_CcBreak_budgets_hold_an_hour_at_3000_bots()
        {
            int rally = 0, breaks = 0;
            for (int minute = 0; minute < 60; minute++)
            {
                long now = 1_000_000 + minute * 60_000L;
                for (int i = 0; i < 3_000; i++)
                {
                    if (LivingRows.Take(BgRallyResponseRows.Marker, BgRallyResponseRows.PerMinute)) rally++;
                    if (LivingRows.Take(CcBreakRows.Marker, CcBreakRows.PerMinute)) breaks++;
                }
                LivingRowsTestKit.SetTime(now + 60_000);
            }
            Assert.That(rally, Is.LessThanOrEqualTo(600));
            Assert.That(breaks, Is.LessThanOrEqualTo(1_200));
            Assert.That(RvrRecordBudget.TakeDropped(), Does.Contain("CC_BREAK:").And.Contain("BG_RALLY_RESPONSE:"),
                "drops are counted for RVR_HOUR");
        }

        // ---- PLAYER_LINE_OUTCOME ---------------------------------------------------------------------------------------

        private static Player Owner()
        {
            Player player = RealPlayer();
            typeof(GameObject).GetField("m_name", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(player, "Owner");
            return player;
        }

        [Test]
        public void PlayerLine_answered_within_30s_through_the_say_gate()
        {
            Player player = Owner();
            BotChatTranscript.Player("tell", player, "where are you camping?", "Marelle");
            Assert.That(PlayerLineOutcomes.PendingCount, Is.EqualTo(1));
            LivingRowsTestKit.SetTime(1_002_500);
            AutonomousChatWhy.Record(new AutonomousChatWhy.Decision(1_002_500, 5, "Marelle", eRealm.Albion, "tell", "tell from Owner",
                "said", "knowledge camp", 4, null, "said", "spindelhalla", "Owner"));
            BotSayGate.Say(new BotSayGate.Utterance("tell", "Marelle", eRealm.Albion, "spindelhalla, lower floor", "Owner"), text => text);
            Assert.That(Rows("PLAYER_LINE_OUTCOME"), Is.Empty, "settled once its 30 s are up, not before");
            LivingRowsTestKit.SetTime(1_031_000);
            Assert.That(PlayerLineOutcomes.Settle(1_031_000), Is.EqualTo(1));
            string row = Rows("PLAYER_LINE_OUTCOME").Single();
            Assert.Multiple(() =>
            {
                Assert.That(LivingRowsTestKit.Keys(row), Is.EqualTo(new[]
                    { "seq", "t", "ch", "realm", "intent", "answered", "latencyMs", "kind", "bots", "replies", "window" }));
                Assert.That(F(row, "ch"), Is.EqualTo("tell"));
                Assert.That(F(row, "answered"), Is.EqualTo("1"));
                Assert.That(F(row, "latencyMs"), Is.EqualTo("2500"));
                Assert.That(F(row, "kind"), Is.EqualTo("knowledge"));
                Assert.That(F(row, "bots"), Is.EqualTo("1"));
                Assert.That(F(row, "window"), Is.EqualTo("30000"));
                Assert.That(row, Does.Not.Contain("Owner").And.Not.Contain("Marelle"), "no names in the row");
            });
        }

        [Test]
        public void PlayerLine_unanswered_is_none_and_a_late_answer_does_not_count()
        {
            Player player = Owner();
            BotChatTranscript.Player("tell", player, "anyone know where the lord is?", "Marelle");
            LivingRowsTestKit.SetTime(1_040_000);
            BotSayGate.Say(new BotSayGate.Utterance("tell", "Marelle", eRealm.Albion, "sorry, missed that", "Owner"), text => text);
            PlayerLineOutcomes.Settle(1_040_000);
            string row = Rows("PLAYER_LINE_OUTCOME").Single();
            Assert.Multiple(() =>
            {
                Assert.That(F(row, "answered"), Is.EqualTo("0"));
                Assert.That(F(row, "latencyMs"), Is.EqualTo("-1"));
                Assert.That(F(row, "kind"), Is.EqualTo("none"));
                Assert.That(F(row, "bots"), Is.EqualTo("0"));
            });
        }

        [Test]
        public void PlayerLine_follows_only_the_listed_channels_and_the_next_line_settles_old_ones()
        {
            Player player = Owner();
            BotChatTranscript.Player("guild", player, "hello guild", "Guild");
            Assert.That(PlayerLineOutcomes.PendingCount, Is.Zero, "guild is not one of say, group, tell, faction, trade");
            BotChatTranscript.Player("faction", player, "lfg anyone?");
            LivingRowsTestKit.SetTime(1_035_000);
            BotChatTranscript.Player("faction", player, "still lfg");
            Assert.That(Rows("PLAYER_LINE_OUTCOME"), Has.Count.EqualTo(1), "the second line settled the first, no timer");
            Assert.That(PlayerLineOutcomes.PendingCount, Is.EqualTo(1));
            Assert.Multiple(() =>
            {
                Assert.That(PlayerLineOutcomes.KindOf("ack", "ack busy", null), Is.EqualTo("ack"));
                Assert.That(PlayerLineOutcomes.KindOf("said", "knowledge where", null), Is.EqualTo("knowledge"));
                Assert.That(PlayerLineOutcomes.KindOf("said", "fact level", AutonomousChatWhy.Codes.SelfKnowledge), Is.EqualTo("knowledge"));
                Assert.That(PlayerLineOutcomes.KindOf("said", "intent greeting", null), Is.EqualTo("answer"));
            });
        }

        [Test]
        public void PlayerLine_volume_is_capped_at_30_rows_a_minute()
        {
            Player player = Owner();
            for (int i = 0; i < 200; i++)
                BotChatTranscript.Player("faction", player, "spam " + i);
            PlayerLineOutcomes.Settle(1_000_000 + 30_000);
            Assert.That(Rows("PLAYER_LINE_OUTCOME"), Has.Count.LessThanOrEqualTo(PlayerLineOutcomes.PerMinute));
            Assert.That(PlayerLineOutcomes.PendingCount, Is.Zero);
        }
    }

    /// <summary>
    /// #625 set A: CAST_INTERRUPT is written at the half-cast point (#596's decision) through the real SpellHandler cast path,
    /// for a kept and a lost cast, and its sampling keeps it low-volume. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_LivingRowsCastInterrupt
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameLiving> _actors = [];
        private readonly List<string> _rows = [];
        private GameServer _oldServer;
        private long _oldTime;
        private Region _region;
        private PetTestLanguageScope _language;
        private int _oldSelfInterrupt, _oldEvery;

        private sealed class EmptyServer : GameServer
        {
            protected override IObjectDatabase DataBaseImpl =>
                DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
            protected override GS.ServerRules.IServerRules ServerRulesImpl => new GS.ServerRules.NormalServerRules();
        }

        private static int Modified(eProperty property) => property switch
        {
            eProperty.Dexterity => 60,
            eProperty.CastingSpeed => 0,
            _ => 100
        };

        private sealed class CasterBot : GameBot
        {
            private CasterBot() : base((OfflineWorldBotRecord) null) { }
            public override byte Level { get; set; } = 50;
            public override bool IsAlive => true;
            public override bool IsCrowdControlled => false;
            public override int Health { get; set; } = 50;
            public override int Mana { get; set; } = 100;
            public override int Endurance { get; set; } = 100;
            public override int MaxHealth => 100;
            public override int MaxMana => 100;
            public override int MaxEndurance => 100;
            public override int GetModified(eProperty property) => Modified(property);
        }

        private sealed class Enemy : GameNPC
        {
            public override bool IsAlive => true;
            public override int Health { get; set; } = 100;
            public override int MaxHealth => 100;
            public override int GetModified(eProperty property) => 100;
        }

        [SetUp]
        public void SetUp()
        {
            _oldServer = GameServer.Instance;
            _oldTime = GameLoop.GameLoopTime;
            GameServer.LoadTestDouble((EmptyServer) RuntimeHelpers.GetUninitializedObject(typeof(EmptyServer)));
            _language = new PetTestLanguageScope();
            _region = new Region(new RegionData { Id = 202, Name = "Interrupt rows", Description = "Interrupt rows", Mobs = [] });
            LivingRowsTestKit.SetTime(100_000);
            ScriptMgr.ClearSpellHandlerCache();
            _oldSelfInterrupt = GS.ServerProperties.Properties.SPELL_SELF_INTERRUPT_DURATION;
            GS.ServerProperties.Properties.SPELL_SELF_INTERRUPT_DURATION = 2_200;
            _oldEvery = LivingRowProperties.CastInterruptSampleEvery;
            LivingRowProperties.CastInterruptSampleEvery = 1;
            CastInterruptRows.Sampler.Reset();
            RvrRecordBudget.Reset();
            _rows.Clear();
            LivingRows.TestSink = row => { lock (_rows) _rows.Add(row); };
        }

        [TearDown]
        public void TearDown()
        {
            LivingRows.TestSink = null;
            LivingRowProperties.CastInterruptSampleEvery = _oldEvery;
            foreach (GameLiving actor in _actors)
            {
                ServiceObjectStore.Remove(actor.castingComponent);
                ServiceObjectStore.Remove(actor.effectListComponent);
            }
            _actors.Clear();
            _language.Dispose();
            GS.ServerProperties.Properties.SPELL_SELF_INTERRUPT_DURATION = _oldSelfInterrupt;
            RvrRecordBudget.Reset();
            GameServer.LoadTestDouble(_oldServer);
            LivingRowsTestKit.SetTime(_oldTime);
        }

        private List<string> Rows() { lock (_rows) return _rows.Where(row => row.StartsWith("CAST_INTERRUPT ")).ToList(); }

        private static Spell SelfHeal() => new(new DbSpell
        {
            SpellID = 59111, Name = "Test Mending", Type = "Heal", Target = "Self",
            Range = 0, CastTime = 3.0, Value = 20, Power = 5, Uninterruptible = false
        }, 1);

        [Test]
        public void A_lost_cast_is_a_row_with_the_timer_left_at_half_cast()
        {
            (CasterBot caster, SpellHandler handler, Enemy enemy) = BeginCast();
            LivingRowsTestKit.SetTime(100_100);
            Hit(caster, enemy, interruptEnds: 103_100);
            handler.Tick();
            Assert.That(Rows(), Is.Empty, "nothing before the half-cast point");
            LivingRowsTestKit.SetTime(101_600);
            handler.Tick();
            Assert.That(handler.CastState, Is.Not.EqualTo(eCastState.Casting), "the cast is lost as before");
            string row = Rows().Single();
            Assert.Multiple(() =>
            {
                Assert.That(LivingRowsTestKit.Keys(row), Is.EqualTo(new[]
                {
                    "seq", "every", "t", "caster", "pet", "realm", "spell", "castMs", "leftMs", "outcome", "why", "attacker",
                    "attackerPet", "region", "bg"
                }));
                Assert.That(LivingRowsTestKit.Field(row, "caster"), Is.EqualTo("gamebot"));
                Assert.That(LivingRowsTestKit.Field(row, "attacker"), Is.EqualTo("npc"));
                Assert.That(LivingRowsTestKit.Field(row, "spell"), Is.EqualTo("Heal"));
                Assert.That(LivingRowsTestKit.Field(row, "castMs"), Is.EqualTo("3000"));
                Assert.That(LivingRowsTestKit.Field(row, "leftMs"), Is.EqualTo("1500"));
                Assert.That(LivingRowsTestKit.Field(row, "outcome"), Is.EqualTo("lost"));
                Assert.That(LivingRowsTestKit.Field(row, "why"), Is.EqualTo("timer"));
            });
        }

        [Test]
        public void A_kept_cast_is_a_row_with_the_timer_run_out()
        {
            (CasterBot caster, SpellHandler handler, Enemy enemy) = BeginCast();
            Hit(caster, enemy, interruptEnds: 101_000);
            LivingRowsTestKit.SetTime(101_600);
            handler.Tick();
            Assert.That(handler.CastState, Is.EqualTo(eCastState.Casting), "the cast goes on as before");
            string row = Rows().Single();
            Assert.That(LivingRowsTestKit.Field(row, "outcome"), Is.EqualTo("kept"));
            Assert.That(LivingRowsTestKit.Field(row, "why"), Is.EqualTo("expired"));
            Assert.That(long.Parse(LivingRowsTestKit.Field(row, "leftMs")), Is.LessThanOrEqualTo(0));
        }

        [Test]
        public void A_cast_no_hit_touched_writes_nothing()
        {
            (_, SpellHandler handler, _) = BeginCast();
            LivingRowsTestKit.SetTime(101_600);
            handler.Tick();
            Assert.That(Rows(), Is.Empty);
        }

        [Test]
        public void Sampling_keeps_an_hour_at_3000_bots_under_1200_rows()
        {
            LivingRowProperties.CastInterruptSampleEvery = 10;
            (CasterBot caster, _, Enemy enemy) = Casters();
            Spell heal = SelfHeal();
            int rows = 0;
            // 3,000 bots, each with two interrupted casts a minute, for an hour.
            for (int minute = 0; minute < 60; minute++)
            {
                long now = 1_000_000 + minute * 60_000L;
                LivingRowsTestKit.SetTime(now);
                Hit(caster, enemy, interruptEnds: now + 1_000);
                for (int cast = 0; cast < 6_000; cast++)
                    CastInterruptRows.Observe(caster, heal, now - 1_500, 3_000, false, () => false);
                rows = Rows().Count;
            }
            Assert.That(rows, Is.EqualTo(60 * CastInterruptRows.PerMinute), "every 10th, then the minute ceiling");
            Assert.That(rows, Is.LessThanOrEqualTo(1_200));
            Assert.That(CastInterruptRows.Why(false, 500, true, false), Is.EqualTo("uninterruptible"));
            Assert.That(CastInterruptRows.Why(false, 500, false, true), Is.EqualTo("immune"));
        }

        private (CasterBot, SpellHandler, Enemy) BeginCast()
        {
            (CasterBot caster, _, Enemy enemy) = Casters();
            SpellLine line = new("test", "test", "test", false);
            SpellHandler handler = (SpellHandler) ScriptMgr.CreateSpellHandler(caster, SelfHeal(), line);
            handler.Target = caster;
            handler.Tick();
            Assert.That(handler.CastState, Is.EqualTo(eCastState.Casting), "the cast begins");
            return (caster, handler, enemy);
        }

        private (CasterBot, object, Enemy) Casters()
        {
            CasterBot caster = Actor<CasterBot>();
            caster.Name = "Castertest";
            caster.Health = 50;
            Set(typeof(GameObject), caster, "<Realm>k__BackingField", eRealm.Albion);
            Set(typeof(GameBot), caster, "m_characterClass", new GS.PlayerClass.ClassCleric());
            Set(typeof(GameBot), caster, "<IsAutonomousWorldBot>k__BackingField", true);
            Enemy enemy = Actor<Enemy>();
            enemy.Name = "test enemy";
            Set(typeof(GameObject), enemy, "<Realm>k__BackingField", eRealm.None);
            return (caster, null, enemy);
        }

        private static void Hit(GameLiving caster, GameLiving attacker, long interruptEnds)
        {
            Set(typeof(GameLiving), caster, "<InterruptTime>k__BackingField", interruptEnds);
            Set(typeof(GameLiving), caster, "<LastInterrupter>k__BackingField", attacker);
        }

        private T Actor<T>() where T : GameLiving
        {
            T actor = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
            SetNew(typeof(GameObject), actor, "_objectInRadiusCachesLock");
            SetNew(typeof(GameObject), actor, "_objectsInRadiusCaches");
            foreach (string field in new[] { "_changeHealthLock", "_changeManaLock", "_changeEnduranceLock", "_abilitiesLock",
                "_interruptTimerLock", "_interruptCallbackLock" })
                Set(typeof(GameLiving), actor, field, new System.Threading.Lock());
            Set(typeof(GameLiving), actor, "_disabledSkillsLock", new System.Threading.Lock());
            Set(typeof(GameLiving), actor, "m_disabledSkills", new Dictionary<(int, Type), KeyValuePair<long, Skill>>());
            foreach (string property in new[] { "ItemBonus", "AbilityBonus", "BaseBuffBonusCategory", "SpecBuffBonusCategory", "OtherBonus", "DebuffCategory", "SpecDebuffCategory" })
                Set(typeof(GameLiving), actor, $"<{property}>k__BackingField", new PropertyIndexer());
            Set(typeof(GameLiving), actor, "<TempProperties>k__BackingField", new PropertyCollection());
            Set(typeof(GameLiving), actor, "m_effects", new GameEffectList(actor));
            Set(typeof(GameLiving), actor, "m_abilities", new Dictionary<string, Ability>());
            Set(typeof(GameLiving), actor, "<ActivePulseSpells>k__BackingField", new ConcurrentDictionary<eSpellType, Spell>());
            if (actor is GameNPC)
            {
                Set(typeof(GameNPC), actor, "m_brains", new ArrayList());
                Set(typeof(GameNPC), actor, "m_spells", new List<Spell>());
            }
            actor.CurrentRegion = _region;
            actor.ObjectState = GameObject.eObjectState.Active;
            actor.castingComponent = CastingComponent.Create(actor);
            if (actor is GameNPC npc)
                npc.castingComponent = (NpcCastingComponent) actor.castingComponent;
            actor.effectListComponent = EffectListComponent.Create(actor);
            actor.attackComponent = new AttackComponent(actor);
            actor.styleComponent = StyleComponent.Create(actor);
            if (actor is GameNPC movingNpc)
            {
                movingNpc.movementComponent = new NpcMovementComponent(movingNpc);
                actor.movementComponent = movingNpc.movementComponent;
                movingNpc.movementComponent.ForceUpdatePosition();
            }
            _actors.Add(actor);
            return actor;
        }

        private static void Set(Type type, object target, string field, object value) =>
            type.GetField(field, Hidden)!.SetValue(target, value);

        private static void SetNew(Type type, object target, string field)
        {
            FieldInfo info = type.GetField(field, Hidden)!;
            info.SetValue(target, Activator.CreateInstance(info.FieldType));
        }
    }
}
