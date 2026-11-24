using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #521, the rule-parity fixture of docs/rvr-audit-1.2.3/mechanics.md ("Measuring tools worth building"): the same PvP
    /// scenario against the real player and against an autonomous gamebot gives the same rule outcome at every site of
    /// table A (mez "enraged", CC immunity kind, interrupt length, crit cap, facing, speed hurt / sprinting / stealthed,
    /// rez sickness, spell level, regeneration, repeat-kill window, cast line of sight). Monsters, guards, bosses and
    /// companions keep today's rule (PvE unchanged), and a gamebot fighting a monster keeps it too.
    /// </summary>
    [TestFixture]
    public sealed class UT_PvpRuleParity
    {
        private static readonly PvpSide Player = new(PvpCharacterKind.Player, eRealm.Albion);
        private static readonly PvpSide Gamebot = new(PvpCharacterKind.AutonomousGamebot, eRealm.Albion);
        private static readonly PvpSide Companion = new(PvpCharacterKind.Companion, eRealm.Albion);
        private static readonly PvpSide Monster = new(PvpCharacterKind.Other, eRealm.None);
        private static readonly PvpSide EnemyPlayer = new(PvpCharacterKind.Player, eRealm.Midgard);
        private static readonly PvpSide EnemyGamebot = new(PvpCharacterKind.AutonomousGamebot, eRealm.Hibernia);
        private static readonly PvpSide EnemyGuard = new(PvpCharacterKind.Other, eRealm.Midgard);
        private static readonly PvpSide FriendlyGamebot = new(PvpCharacterKind.AutonomousGamebot, eRealm.Albion);

        private static bool Rules(PvpSide subject, PvpSide? other, bool inRvr = true) => PvpRules.TakesPlayerRules(subject, other, inRvr);

        private static object[] EnemyCharacters => [EnemyPlayer, EnemyGamebot];

        // ---- the predicate --------------------------------------------------------------------------------------

        [TestCaseSource(nameof(EnemyCharacters))]
        public void AnAutonomousGamebotTakesThePlayerRuleAgainstAnEnemyPlayerCharacter(PvpSide enemy)
        {
            Assert.That(Rules(Player, enemy), Is.True);
            Assert.That(Rules(Gamebot, enemy), Is.True);
            // Out of the RvR areas too: the other side decides a two-sided rule.
            Assert.That(Rules(Gamebot, enemy, inRvr: false), Is.True);
        }

        [Test]
        public void AgainstAMonsterAGuardOrItsOwnRealmAGamebotKeepsTodaysRule()
        {
            Assert.That(Rules(Gamebot, Monster), Is.False, "PvE unchanged");
            Assert.That(Rules(Gamebot, EnemyGuard), Is.False, "keep guards unchanged");
            Assert.That(Rules(Gamebot, FriendlyGamebot), Is.False);
        }

        [Test]
        public void ARuleAboutTheBotAloneFollowsTheRvrArea()
        {
            Assert.That(Rules(Gamebot, null, inRvr: true), Is.True);
            Assert.That(Rules(Gamebot, null, inRvr: false), Is.False, "PvE: today's rule (fast rest, monster speed)");
            Assert.That(Rules(Player, null, inRvr: false), Is.True, "the player always takes the player rule");
        }

        [Test]
        public void CompanionsAndMonstersNeverChange()
        {
            foreach (PvpSide? other in new PvpSide?[] { EnemyPlayer, EnemyGamebot, Monster, null })
            {
                Assert.That(Rules(Companion, other), Is.False);
                Assert.That(Rules(Monster, other), Is.False);
            }
        }

        [TestCase((ushort)163, false, true)]   // New Frontiers
        [TestCase((ushort)1, true, true)]      // a classic frontier zone in Albion's region
        [TestCase((ushort)165, false, true)]   // Cathal Valley
        [TestCase((ushort)250, false, true)]   // the bracketed battlegrounds
        [TestCase((ushort)253, false, true)]
        [TestCase((ushort)238, false, true)]   // an old battleground region
        [TestCase((ushort)244, false, true)]   // the frontier dungeon
        [TestCase((ushort)249, false, false)]  // Darkness Falls: raids keep today's rules
        [TestCase((ushort)233, false, false)]  // Summoner's Hall
        [TestCase((ushort)489, false, false)]  // Demons' Breach
        [TestCase((ushort)1, false, false)]    // Albion's PvE zones
        public void TheRvrAreasAreTheFrontiersAndBattlegrounds(ushort region, bool zoneIsRvr, bool rvr) =>
            Assert.That(PvpRules.IsRvrArea(region, zoneIsRvr), Is.EqualTo(rvr));

        // ---- the same scenario against a player and a gamebot: the same outcome, site by site -------------------

        private static void Same<T>(System.Func<PvpSide, T> outcome, string site)
        {
            T player = outcome(Player);
            T bot = outcome(Gamebot);
            Assert.That(bot, Is.EqualTo(player), site);
        }

        [TestCaseSource(nameof(EnemyCharacters))]
        public void EverySiteGivesTheBotThePlayersOutcome(PvpSide enemy)
        {
            Same(s => PvpRules.EnragedAgainstMez(Rules(s, enemy), s.Kind != PvpCharacterKind.Player, 40), "mez at 40% health");
            Same(s => PvpRules.ImmunityTypeFor(true, Rules(s, enemy)), "immunity after mez, stun, root, snare");
            Same(s => PvpRules.InterruptExtraMs(Rules(s, enemy), false), "interrupt length");
            Same(s => PvpRules.CriticalCap(Rules(s, enemy)), "crit cap on the target");
            Same(s => PvpRules.MustFaceToSwing(Rules(s, enemy)), "facing to swing");
            Same(s => PvpRules.SpellLevelForToHit(Rules(s, enemy), 40, 50), "spell level to hit");
            // One-sided rules, in an RvR area.
            Same(s => PvpRules.LowHealthSpeedFactor(Rules(s, null), 0.10), "speed at 10% health");
            Same(s => PvpRules.SprintSpeedFactor(Rules(s, null), true), "sprint speed");
            Same(s => PvpRules.RvrRezSicknessApplies(Rules(s, null), true, 30, 10, 1.0), "RvR rez sickness");
            Same(s => PvpRules.RvrRezSicknessApplies(Rules(s, null), true, 30, 10, 0.0), "Perfect Recovery");
            Same(s => PvpRules.UsesFastRest(Rules(s, null)), "regeneration");
            Assert.That(PvpRules.ImmunityTypeFor(true, Rules(Gamebot, enemy)), Is.EqualTo(ECSGameEffect.ImmunityType.Player));
            Assert.That(PvpRules.InterruptExtraMs(Rules(Gamebot, enemy), false), Is.Zero);
            Assert.That(PvpRules.CriticalCap(Rules(Gamebot, enemy)), Is.EqualTo(0.5));
        }

        [Test]
        public void AMonsterAndAGamebotInPveKeepTheMonsterOutcome()
        {
            foreach (bool rules in new[] { Rules(Monster, EnemyPlayer), Rules(Gamebot, Monster), Rules(Companion, EnemyPlayer) })
            {
                Assert.That(PvpRules.EnragedAgainstMez(rules, true, 40), Is.True);
                Assert.That(PvpRules.ImmunityTypeFor(true, rules), Is.EqualTo(ECSGameEffect.ImmunityType.Npc));
                Assert.That(PvpRules.CriticalCap(rules), Is.EqualTo(1.0));
                Assert.That(PvpRules.SpellLevelForToHit(rules, 40, 50), Is.EqualTo(50));
            }
            Assert.That(PvpRules.InterruptExtraMs(Rules(Monster, EnemyPlayer), false), Is.EqualTo(PvpRules.MonsterInterruptExtraMs));
            Assert.That(PvpRules.InterruptExtraMs(Rules(Companion, EnemyPlayer), true), Is.Zero, "companions: no extra, as before");
            bool pve = Rules(Gamebot, null, inRvr: false);
            Assert.That(PvpRules.UsesFastRest(pve), Is.True, "owner: the fast rest regeneration stays in PvE");
            Assert.That(PvpRules.LowHealthSpeedFactor(pve, 0.10), Is.LessThan(1.0));
            Assert.That(PvpRules.SprintSpeedFactor(pve, true), Is.EqualTo(1.0));
            Assert.That(PvpRules.RvrRezSicknessApplies(pve, false, 30, 10, 1.0), Is.False);
        }

        [Test]
        public void NoTriggerNoImmunity() =>
            Assert.That(PvpRules.ImmunityTypeFor(false, true), Is.EqualTo(ECSGameEffect.ImmunityType.None));

        [TestCase(true, false, true, true)]     // a bot's cast at another bot: the navmesh checks
        [TestCase(true, true, true, false)]     // at the player: his client checks, as before
        [TestCase(false, false, true, false)]   // at a monster: unchanged
        [TestCase(true, false, false, false)]   // a self or group spell
        public void ABotsCastAtAnotherBotIsCheckedForSight(bool pvp, bool atPlayer, bool needsSight, bool check) =>
            Assert.That(PvpRules.BotCastNeedsNavmeshSight(pvp, atPlayer, needsSight), Is.EqualTo(check));

        [TestCase(0L, 299L, false)]
        [TestCase(0L, 300L, true)]
        [TestCase(1000L, 1200L, false)]
        [TestCase(1000L, 1300L, true)]
        public void TheRepeatKillWindowIsThePlayers(long lastDeath, long played, bool worth) =>
            Assert.That(PvpRules.WorthRealmPoints(lastDeath, played, 300), Is.EqualTo(worth));

        [TestCase(50.0, 50, 0.0, false)]
        [TestCase(30.0, 50, 10.0, false)]
        [TestCase(60.0, 50, 0.0, true)]
        [TestCase(1.0, 20, 0.0, false)]
        public void TheStealthSpeedIsThePlayersFormula(double spec, int level, double mos, bool shadowRun)
        {
            // GamePlayer's formula before #521 (MaxSpeedCalculator), written out.
            double capped = System.Math.Min(spec, level);
            double expected = (0.3 + (capped + 10) * 0.3 / (level + 10)) * (1 + mos / 100.0) * (shadowRun ? 2 : 1);
            Assert.That(PvpRules.StealthSpeedFactor(spec, level, mos, shadowRun), Is.EqualTo(expected).Within(1e-12));
            Assert.That(PvpRules.StealthSpeedFactor(50, 50, 0, false), Is.EqualTo(0.6).Within(1e-12), "about 60% of run at 50 Stealth");
        }

        [Test]
        public void SprintDrainsFivePerTickLessLongWind()
        {
            Assert.That(PvpRules.SprintDrainPerTick(0), Is.EqualTo(5));
            Assert.That(PvpRules.SprintDrainPerTick(100), Is.Zero);
        }

        // ---- the game's characters: the same question asked of a real GamePlayer and a real GameBot --------------

        private static void SetField(System.Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);

        private static GameBot Bot(eRealm realm, bool autonomous, bool temporaryHelper = false)
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.Realm = realm;
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporaryHelper);
            return bot;
        }

        private static GamePlayer PlayerOf(eRealm realm)
        {
            var player = (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            player.Realm = realm;
            return player;
        }

        private static GameNPC MonsterOf(eRealm realm)
        {
            var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            SetField(typeof(GameNPC), npc, "m_brains", new ArrayList());
            npc.Realm = realm;
            return npc;
        }

        [Test]
        public void TheGameCharactersAreToldApart()
        {
            Assert.That(PvpRules.KindOf(PlayerOf(eRealm.Albion)), Is.EqualTo(PvpCharacterKind.Player));
            Assert.That(PvpRules.KindOf(Bot(eRealm.Albion, true)), Is.EqualTo(PvpCharacterKind.AutonomousGamebot));
            Assert.That(PvpRules.KindOf(Bot(eRealm.Albion, false, true)), Is.EqualTo(PvpCharacterKind.Companion));
            Assert.That(PvpRules.KindOf(Bot(eRealm.Albion, true, true)), Is.EqualTo(PvpCharacterKind.Companion));
            Assert.That(PvpRules.KindOf(MonsterOf(eRealm.None)), Is.EqualTo(PvpCharacterKind.Other));
        }

        [Test]
        public void AGamePlayerAndAGameBotGetTheSameAnswerFromTheGameObjects()
        {
            GamePlayer enemy = PlayerOf(eRealm.Midgard);
            GameBot enemyBot = Bot(eRealm.Hibernia, true);
            GamePlayer player = PlayerOf(eRealm.Albion);
            GameBot bot = Bot(eRealm.Albion, true);
            foreach (GameLiving other in new GameLiving[] { enemy, enemyBot })
                Assert.That(PvpRules.TakesPlayerRules(bot, other), Is.EqualTo(PvpRules.TakesPlayerRules(player, other)).And.True);

            Assert.That(PvpRules.TakesPlayerRules(bot, MonsterOf(eRealm.None)), Is.False, "PvE unchanged");
            Assert.That(PvpRules.TakesPlayerRules(bot, MonsterOf(eRealm.Midgard)), Is.False, "an enemy guard: unchanged");
            Assert.That(PvpRules.TakesPlayerRules(Bot(eRealm.Albion, false, true), enemy), Is.False, "companions unchanged");
            Assert.That(PvpRules.TakesPlayerRules(MonsterOf(eRealm.None), enemy), Is.False, "monsters unchanged");
            Assert.That(PvpRules.TakesPlayerRules(bot), Is.False, "no region, no PvP combat: not in RvR");
        }
    }
}
