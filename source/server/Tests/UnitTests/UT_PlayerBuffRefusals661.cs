using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS;
using DOL.GS.PlayerClass;
using DOL.GS.Spells;
using DOL.Logging;
using NUnit.Framework;
using R = DOL.GS.PlayerBuffRefusals;
using B = DOL.GS.NoLiferBuffBots;

namespace DOL.UnitTests
{
    /// <summary>
    /// #661 (owner, 2026-11-26): a player refuses a buff type from bots (/buff refuse, or a right-click cancel of a bot's
    /// buff) until /buff allow, saved on the character. Bots then leave it off the player and the player's pet. Tests only;
    /// nothing here is seen in game.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public class UT_PlayerBuffRefusals661
    {
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private GameServer _previous;

        [OneTimeSetUp]
        public void InitializeLogging() => LoggerManager.InitializeWithExplicitLibrary(null, LogLibrary.None);

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup() => GameServer.LoadTestDouble(_previous);

        #region Live objects with no constructor run

        private class ClassBot : GameBot
        {
            private ClassBot() : base((OfflineWorldBotRecord)null) { }
            public ICharacterClass TestClass;
            public override ICharacterClass CharacterClass => TestClass;
        }

        private static GameBot Bot()
        {
            var bot = (ClassBot)RuntimeHelpers.GetUninitializedObject(typeof(ClassBot));
            bot.TestClass = new ClassDruid();
            SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
            return bot;
        }

        private static GamePlayer Player() => (GamePlayer)RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));

        /// <summary>A pet whose controlling brain's owner is <paramref name="owner"/>.</summary>
        private static GameNPC PetOf(GameLiving owner)
        {
            var pet = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
            var brain = (ControlledMobBrain)RuntimeHelpers.GetUninitializedObject(typeof(ControlledMobBrain));
            SetField(typeof(ControlledMobBrain), brain, "m_owner", owner);
            SetField(typeof(GameNPC), pet, "m_brains", new ArrayList());
            SetField(typeof(GameNPC), pet, "m_ownBrain", brain);
            return pet;
        }

        private static void SetField(Type type, object target, string name, object value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"{type.Name}.{name}");
            field.SetValue(target, value);
        }

        private static Spell SpellOf(eSpellType type, string name = null) =>
            new(new DbSpell { Type = type.ToString(), Target = "Realm", Value = 10, Name = name ?? type.ToString(), Duration = 600 }, 20);

        private static readonly SpellLine Line = new("Mob_Spells", "Mob Spells", "", true);

        #endregion

        #region Command parsing and names

        [Test]
        public void EveryBuffShortNameCanBeRefused_IncludingDs()
        {
            Assert.That(R.ShortNames.Length, Is.EqualTo(33), "every short name /buff knows");
            Assert.That(R.ShortNames.Select(entry => entry.Short).Distinct().Count(), Is.EqualTo(R.ShortNames.Length));
            foreach ((string shortName, eSpellType type) in R.ShortNames)
            {
                Assert.That(R.TryParse(shortName, out eSpellType parsed), Is.True, shortName);
                Assert.That(parsed, Is.EqualTo(type), shortName);
                Assert.That(R.TryParse(shortName.ToUpperInvariant(), out _), Is.True, "any case");
                Assert.That(R.ShortNameOf(type), Is.EqualTo(shortName));
            }
            Assert.That(R.TryParse("ds", out eSpellType ds) && ds == eSpellType.DamageShield, Is.True);
            Assert.That(R.IsRefusable(eSpellType.SpeedEnhancement), Is.False, "a type with no short name can't be refused");
        }

        [Test]
        public void RefuseAndAllow_Parse_WithUnknownNames()
        {
            Assert.Multiple(() =>
            {
                R.Command list = R.ParseCommand(["&buff", "refuse"]);
                Assert.That(list.Kind, Is.EqualTo(R.CommandKind.List), "/buff refuse alone lists");

                R.Command refuse = R.ParseCommand(["&buff", "refuse", "ds,bt", "DA", "nope", "ds"]);
                Assert.That(refuse.Kind, Is.EqualTo(R.CommandKind.Refuse));
                Assert.That(refuse.Types, Is.EqualTo(new[] { eSpellType.DamageShield, eSpellType.Bladeturn, eSpellType.DamageAdd }));
                Assert.That(refuse.Unknown, Is.EqualTo(new[] { "nope" }));
                Assert.That(refuse.All, Is.False);

                R.Command allowAll = R.ParseCommand(["&buff", "ALLOW", "all"]);
                Assert.That(allowAll.Kind, Is.EqualTo(R.CommandKind.Allow));
                Assert.That(allowAll.All, Is.True);
                Assert.That(allowAll.Unknown, Is.Empty);

                Assert.That(R.ParseCommand(["&buff", "refuse", "all"]).Unknown, Is.EqualTo(new[] { "all" }),
                    "refuse all is not offered: name what you refuse");
                R.Command allowNothing = R.ParseCommand(["&buff", "allow"]);
                Assert.That(allowNothing.Kind, Is.EqualTo(R.CommandKind.Allow));
                Assert.That(allowNothing.Types, Is.Empty);

                Assert.That(R.ParseCommand(["&buff", "ds"]).Kind, Is.EqualTo(R.CommandKind.None), "an ordinary /buff cast");
                Assert.That(R.ParseCommand(["&buff", "all"]).Kind, Is.EqualTo(R.CommandKind.None));
                Assert.That(R.ParseCommand(["&buff"]).Kind, Is.EqualTo(R.CommandKind.None));
            });
        }

        [Test]
        public void ResultLines_SayWhatChanged_AndNameUnknowns()
        {
            R.Command refuse = R.ParseCommand(["&buff", "refuse", "ds,xyz"]);
            IReadOnlyList<string> lines = R.ResultLines(refuse, [eSpellType.DamageShield], [eSpellType.DamageShield]);
            Assert.That(lines.Any(line => line.Contains("\"xyz\" is not a buff short name")), Is.True);
            Assert.That(lines.Any(line => line.Contains("You refuse ds (DamageShield)") && line.Contains("/buff allow ds")), Is.True);

            IReadOnlyList<string> again = R.ResultLines(R.ParseCommand(["&buff", "refuse", "ds"]), [], [eSpellType.DamageShield]);
            Assert.That(again.Single(), Does.Contain("already refuse ds"));

            Assert.That(R.ListLine([]), Does.Contain("refuse no buffs"));
            Assert.That(R.ListLine([eSpellType.DamageShield, eSpellType.Bladeturn]), Does.Contain("ds (DamageShield), bt (Bladeturn)"), "in /buff's order");
            Assert.That(R.CancelledLine("Thorn Shield", "ds"), Is.EqualTo("You refused Thorn Shield; /buff allow ds to take it again"));
        }

        #endregion

        #region Saved with the character

        [Test]
        public void TheSavedFormReadsBack_AndDropsUnknownNames()
        {
            Assert.That(R.Format([eSpellType.DamageShield, eSpellType.Bladeturn, eSpellType.DamageAdd]), Is.EqualTo("da,ds,bt"));
            Assert.That(R.ParseStored("da,ds,bt"), Is.EquivalentTo(new[] { eSpellType.DamageAdd, eSpellType.DamageShield, eSpellType.Bladeturn }));
            Assert.That(R.ParseStored(" ds , gone,, BT"), Is.EquivalentTo(new[] { eSpellType.DamageShield, eSpellType.Bladeturn }));
            Assert.That(R.ParseStored(null), Is.Empty);
            Assert.That(R.Format([]), Is.Empty);
        }

        [Test]
        public void TheRefusalSurvivesASaveAndLoad_AndAnEmptyOneLeavesNoRow()
        {
            string folder = TemporarySqlite.NewFolder("buffrefuse");
            try
            {
                IObjectDatabase database = ObjectDatabase.GetObjectDatabase(DOL.Database.Connection.EConnectionType.DATABASE_SQLITE,
                    $"Data Source={Path.Combine(folder, "world.db")};Pooling=False");
                database.RegisterDataObject(typeof(DbCoreCharacter));
                database.RegisterDataObject(typeof(DbCoreCharacterXCustomParam));
                var character = new DbCoreCharacter { AccountName = "owner", Name = "Rootkeeper", Realm = (int)eRealm.Hibernia };
                var other = new DbCoreCharacter { AccountName = "owner", Name = "Otherone", Realm = (int)eRealm.Hibernia };
                Assert.That(database.AddObject(character) && database.AddObject(other), Is.True);

                Assert.That(R.Load(database, character.ObjectId), Is.Empty, "nothing refused by default");
                Assert.That(R.Save(database, character.ObjectId, [eSpellType.DamageShield, eSpellType.Bladeturn]), Is.True);
                Assert.That(R.Load(database, character.ObjectId), Is.EquivalentTo(new[] { eSpellType.DamageShield, eSpellType.Bladeturn }));
                Assert.That(R.Load(database, other.ObjectId), Is.Empty, "per character");

                Assert.That(R.Save(database, character.ObjectId, [eSpellType.DamageShield]), Is.True, "an update keeps one row");
                IList<DbCoreCharacterXCustomParam> rows = database.SelectObjects<DbCoreCharacterXCustomParam>(
                    DB.Column("DOLCharactersObjectId").IsEqualTo(character.ObjectId));
                Assert.That(rows.Count, Is.EqualTo(1));
                Assert.That(rows[0].KeyName, Is.EqualTo(R.Param));
                Assert.That(rows[0].Value, Is.EqualTo("ds"));
                Assert.That(R.Load(database, character.ObjectId), Is.EquivalentTo(new[] { eSpellType.DamageShield }));

                Assert.That(R.Save(database, character.ObjectId, []), Is.True);
                Assert.That(database.SelectObjects<DbCoreCharacterXCustomParam>(
                    DB.Column("DOLCharactersObjectId").IsEqualTo(character.ObjectId)), Is.Empty, "allowing everything removes the row");
            }
            finally
            {
                TemporarySqlite.DeleteFolder(folder);
            }
        }

        [Test]
        public void TheTableIsSavedProgress_SoReleasePackagesEmptyIt()
        {
            string policy = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, FindPolicy()));
            Assert.That(policy, Does.Contain("\"DOLCharactersXCustomParam\""));
        }

        private static string FindPolicy()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            for (int i = 0; i < 10 && dir != null; i++, dir = Path.GetDirectoryName(dir))
            {
                string candidate = Path.Combine(dir, "source", "tools", "DAoCLivingServer.ProgressImport", "progress-policy.json");
                if (File.Exists(candidate))
                    return candidate;
            }
            Assert.Ignore("progress-policy.json not found from the test folder");
            return null;
        }

        #endregion

        #region Bots leave a refused type off the player and the player's pet

        [Test]
        public void WithDsRefused_TheBotPickNeverChoosesThePlayerOrThePet_ForDamageShield_AndOtherBuffsStillGo()
        {
            GamePlayer refuser = Player();
            GamePlayer other = Player();
            GameNPC refusersPet = PetOf(refuser);
            GameNPC botsPet = PetOf(Bot());
            Spell ds = SpellOf(eSpellType.DamageShield);

            Assert.That(R.Refuse(refuser, [eSpellType.DamageShield], "test"), Is.EqualTo(new[] { eSpellType.DamageShield }));

            Assert.Multiple(() =>
            {
                // BotBrain's realm-buff pick (FindRandomMissingRealmBuffTarget), its group and pet picks and the defensive
                // picks all filter through this.
                Assert.That(BotBuffTargetPolicy.Benefits(refuser, ds), Is.False, "the refusing player");
                Assert.That(BotBuffTargetPolicy.Benefits(refusersPet, ds), Is.False, "the refusing player's pet");
                Assert.That(BotBuffTargetPolicy.Benefits(other, ds), Is.True, "another player still gets it");
                Assert.That(BotBuffTargetPolicy.Benefits(botsPet, ds), Is.True, "a bot's pet still gets it");
                Assert.That(BotBuffTargetPolicy.Benefits(Bot(), ds), Is.True, "a bot never refuses");
                foreach (eSpellType type in new[] { eSpellType.StrengthBuff, eSpellType.Bladeturn, eSpellType.DamageAdd, eSpellType.SpecArmorFactorBuff })
                {
                    Assert.That(BotBuffTargetPolicy.Benefits(refuser, SpellOf(type)), Is.True, $"{type} still goes out");
                    Assert.That(BotBuffTargetPolicy.Benefits(refusersPet, SpellOf(type)), Is.True, $"{type} still goes to the pet");
                }
            });
        }

        [Test]
        public void WhereTheSpellLands_ABotsBuffSkipsTheRefuser_APlayersDoesNot()
        {
            GamePlayer refuser = Player();
            GameNPC refusersPet = PetOf(refuser);
            GameBot companion = Bot();
            GameNPC companionsPet = PetOf(companion);
            GamePlayer friend = Player();
            Spell ds = SpellOf(eSpellType.DamageShield);
            R.Refuse(refuser, [eSpellType.DamageShield], "test");

            Assert.Multiple(() =>
            {
                Assert.That(R.IsBotCaster(companion), Is.True);
                Assert.That(R.IsBotCaster(companionsPet), Is.True, "a bot's pet casts for the bot");
                Assert.That(R.IsBotCaster(friend), Is.False);
                Assert.That(R.IsBotCaster(PetOf(friend)), Is.False, "a player's pet is not a bot");

                Assert.That(R.BlocksBotBuff(companion, refuser, ds), Is.True);
                Assert.That(R.BlocksBotBuff(companion, refusersPet, ds), Is.True);
                Assert.That(R.BlocksBotBuff(companionsPet, refuser, ds), Is.True);
                Assert.That(R.BlocksBotBuff(friend, refuser, ds), Is.False, "players buffing players are unaffected");
                Assert.That(R.BlocksBotBuff(refuser, refuser, ds), Is.False, "the player's own damage shield");
                Assert.That(R.BlocksBotBuff(companion, friend, ds), Is.False);
                Assert.That(R.BlocksBotBuff(companion, refuser, SpellOf(eSpellType.StrengthBuff)), Is.False);
            });
        }

        [Test]
        public void NoLiferBuffBotPlan_LeavesARefusedTypeOut_AndKeepsTheRest()
        {
            var known = new List<B.BuffCandidate>
            {
                new(1, "saf", eSpellType.SpecArmorFactorBuff, eSpellTarget.REALM, 10, 40, false),
                new(2, "ds", eSpellType.DamageShield, eSpellTarget.REALM, 0, 40, true),
                new(3, "str", eSpellType.StrengthBuff, eSpellTarget.REALM, 0, 40, true),
                new(4, "da", eSpellType.DamageAdd, eSpellTarget.REALM, 10, 40, false),
            };
            IReadOnlyList<B.BuffCandidate> everything = B.PlanBuffs(known, true, true, 100);
            Assert.That(everything.Select(spell => spell.Type), Does.Contain(eSpellType.DamageShield), "damage shield stays on by default");

            IReadOnlyList<B.BuffCandidate> plan = B.PlanBuffs(known, true, true, 100, [eSpellType.DamageShield]);
            Assert.That(plan.Select(spell => spell.Type), Does.Not.Contain(eSpellType.DamageShield));
            Assert.That(plan.Select(spell => spell.Type),
                Is.EquivalentTo(new[] { eSpellType.SpecArmorFactorBuff, eSpellType.StrengthBuff, eSpellType.DamageAdd }));

            // A refused concentration buff leaves its concentration for the next one.
            IReadOnlyList<B.BuffCandidate> tight = B.PlanBuffs(known, true, true, 10, [eSpellType.SpecArmorFactorBuff]);
            Assert.That(tight.Select(spell => spell.Type), Does.Contain(eSpellType.DamageAdd));

            // The refresh loop's per-spell check, for a target that refuses.
            GamePlayer refuser = Player();
            R.Refuse(refuser, [eSpellType.DamageShield], "test");
            Assert.That(B.PlanBuffs(known, true, true, 100, R.Of(refuser)).Any(spell => spell.Type == eSpellType.DamageShield), Is.False);
            Assert.That(R.Refuses((GameLiving)refuser, eSpellType.DamageShield), Is.True);
        }

        #endregion

        #region Right-click cancel

        [Test]
        public void CancellingABotsDamageShield_RefusesIt_UntilAllow()
        {
            GamePlayer player = Player();
            GameBot druid = Bot();
            Spell ds = SpellOf(eSpellType.DamageShield, "Thorn Shield");
            var fromBot = new DamageShieldSpellHandler(druid, ds, Line);
            Assert.That(fromBot.HasPositiveEffect, Is.True);

            Assert.That(R.OnPlayerCancelled(player, fromBot), Is.True, "refused now (and told once)");
            Assert.That(R.Refuses(player, eSpellType.DamageShield), Is.True);
            Assert.That(R.OnPlayerCancelled(player, fromBot), Is.False, "already refused: not told again");
            Assert.That(BotBuffTargetPolicy.Benefits(player, ds), Is.False, "the next pass doesn't pick the player");
            Assert.That(R.BlocksBotBuff(druid, player, ds), Is.True, "and a group cast doesn't land on them");

            Assert.That(R.Allow(player, [eSpellType.DamageShield], "test"), Is.EqualTo(new[] { eSpellType.DamageShield }));
            Assert.That(R.Refuses(player, eSpellType.DamageShield), Is.False);
            Assert.That(BotBuffTargetPolicy.Benefits(player, ds), Is.True, "/buff allow ds restores it");
            Assert.That(R.BlocksBotBuff(druid, player, ds), Is.False);
            Assert.That(R.Allow(player, [eSpellType.DamageShield], "test"), Is.Empty, "allowing twice changes nothing");
        }

        [Test]
        public void CancellingAPlayersBuff_OrOneWithNoShortName_RefusesNothing()
        {
            GamePlayer player = Player();
            GamePlayer friend = Player();
            Assert.That(R.OnPlayerCancelled(player, new DamageShieldSpellHandler(friend, SpellOf(eSpellType.DamageShield), Line)), Is.False,
                "a player's damage shield");
            Assert.That(R.Refuses(player, eSpellType.DamageShield), Is.False);
            Assert.That(R.OnPlayerCancelled(player, null), Is.False);
            Assert.That(R.Of(player), Is.Empty);
        }

        #endregion
    }
}
