using System;
using System.IO;
using System.Linq;
using System.Numerics;
using DAoCLivingServer.Shared;
using DOL.GS;
using DOL.GS.Commands;
using DOL.GS.PacketHandler.Client.v168;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The OpenDAoC-Core player and GM command ports of issue #563 (ledger package X2): /bonuses (c9c597a78, 0db2d9d9e),
    /// dropping from the quiver (b28922b5e, 117370277), /ngg (f449b8769), /fixnpcspawn (1a3b12ba7, ec9cfe94d, 9ff5d3c7e,
    /// 29a935897, b2770fa85), /door on keep doors (3f0f163a3), /targetstats (87ff8bafe, f0aaa1ea2), GM login
    /// (c0f4c72bb), /stuck (74587b664), the invalid-name list (a8c4f88a0) and /stats RP per hour (e845e011f).
    /// Pure rules are called directly; wiring that needs a live client is guarded on the source.
    /// </summary>
    [TestFixture]
    public class UT_UpstreamCommandPorts
    {
        private static string Root()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "GameServer", "GameServer.csproj")))
                    return Path.Combine(dir.FullName, "GameServer");
                if (File.Exists(Path.Combine(dir.FullName, "source", "server", "GameServer", "GameServer.csproj")))
                    return Path.Combine(dir.FullName, "source", "server", "GameServer");
            }
            Assume.That(false, "the sources are next to the tests");
            return null;
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

        private static string Method(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
            int open = source.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
            }
            throw new AssertionException("unbalanced " + signature);
        }

        // /bonuses

        [Test]
        public void Bonuses_ResistRowShowsPrimaryThenSecondary()
        {
            Assert.That(GamePlayerUtils.BonusesResistLine("Crush", 25, 3), Is.EqualTo(" Crush: +25%/+3%"));
            Assert.That(GamePlayerUtils.BonusesResistLine("Heat", -4, 0), Is.EqualTo(" Heat: -4%/+0%"));
        }

        [Test]
        public void Bonuses_PrimaryResistIsItemsBuffsAndRaceNotTheWholeResist()
        {
            string info = Method(Source("gameutils", "GamePlayerUtils.cs"), "public static ICollection<string> GetBonusesInfo(this GamePlayer player)");
            Assert.That(info, Does.Contain("player.GetModifiedFromItems(prop) + player.GetModifiedFromBuffs(prop) + SkillBase.GetRaceResist(player.Race, (eResist) prop)"));
            Assert.That(info, Does.Not.Contain("GetModified(eProperty.Resist_"), "GetModified includes the secondary layer");
        }

        [Test]
        public void Bonuses_MaxManaIsCutAtItsFlatCapAndPowerPoolAtItsOwn()
        {
            // MaxManaCalculator: flat MaxMana up to level / 2 + 1, PowerPool up to level / 2 + the cap bonus (up to the level).
            Assert.That(GamePlayerUtils.BonusesDisplayedItemBonus(eProperty.MaxMana, 50, 40, 0, 0), Is.EqualTo(26));
            Assert.That(GamePlayerUtils.BonusesDisplayedItemBonus(eProperty.MaxMana, 50, 10, 0, 0), Is.EqualTo(10));
            Assert.That(GamePlayerUtils.BonusesDisplayedItemBonus(eProperty.MaxMana, 20, 40, 0, 0), Is.EqualTo(11));
            Assert.That(GamePlayerUtils.BonusesDisplayedItemBonus(eProperty.PowerPool, 50, 40, 10, 0), Is.EqualTo(35), "no longer cut at 26");
            Assert.That(GamePlayerUtils.BonusesDisplayedItemBonus(eProperty.PowerPool, 50, 90, 80, 0), Is.EqualTo(75));
            Assert.That(GamePlayerUtils.BonusesDisplayedItemBonus(eProperty.MaxHealth, 50, 500, 40, 0), Is.EqualTo(200));
            Assert.That(GamePlayerUtils.BonusesDisplayedItemBonus(eProperty.MaxHealth, 50, 500, 0, 300), Is.EqualTo(400));
            Assert.That(GamePlayerUtils.BonusesDisplayedItemBonus(eProperty.MeleeDamage, 50, 7, 0, 0), Is.EqualTo(7));
        }

        // Dropping from the quiver

        [Test]
        public void Drop_BackpackAndQuiverSlotsCanDropWornSlotsCannot()
        {
            Assert.That(GamePlayer.CanDropFromSlot(eInventorySlot.FirstBackpack), Is.True);
            Assert.That(GamePlayer.CanDropFromSlot(eInventorySlot.LastBackpack), Is.True);
            Assert.That(GamePlayer.CanDropFromSlot(eInventorySlot.FirstQuiver), Is.True);
            Assert.That(GamePlayer.CanDropFromSlot(eInventorySlot.FourthQuiver), Is.True);
            Assert.That(GamePlayer.CanDropFromSlot(eInventorySlot.RightHandWeapon), Is.False);
            Assert.That(GamePlayer.CanDropFromSlot(eInventorySlot.TorsoArmor), Is.False);
            Assert.That(GamePlayer.CanDropFromSlot(eInventorySlot.FirstVault), Is.False);
            Assert.That(GamePlayer.CanDropFromSlot(eInventorySlot.Ground), Is.False);
        }

        [Test]
        public void Drop_TheMoveHandlerAndTheDropUseTheSameSlotRule()
        {
            string handler = Method(Source("packets", "Client", "168", "PlayerMoveItemRequestHandler.cs"), "private static void DropItem(GameClient client, eInventorySlot fromClientSlot)");
            Assert.That(handler, Does.Contain("GamePlayer.CanDropFromSlot(fromClientSlot)"));
            Assert.That(handler, Does.Not.Contain("fromClientSlot < eInventorySlot.FirstBackpack"));
            string drop = Method(Source("gameobjects", "GamePlayer.cs"), "public virtual bool DropItem(eInventorySlot slot_pos, out WorldInventoryItem droppedItem)");
            Assert.That(drop, Does.Contain("CanDropFromSlot(slot_pos)"));
            Assert.That(drop, Does.Contain("item == null"), "an empty quiver slot must not crash");
        }

        // /ngg

        [Test]
        public void Ngg_ReadsItemModelsOnFirstUseNotAtStartUp()
        {
            string source = Source("scripts", "system", "ngg.cs");
            Assert.That(source, Does.Not.Contain("[ScriptLoadedEvent]"));
            Assert.That(Method(source, "public void OnCommand(GameClient client, string[] args)"), Does.Contain("EnsureModelsLoaded();"));
        }

        // /fixnpcspawn

        [Test]
        public void FixNpcSpawn_OnlySpawnPointsAtLeast16UnitsOffTheMeshAreFixed()
        {
            Assert.That(FixNpcSpawnCommandHandler.IsAlreadyOnMesh(0f), Is.True);
            Assert.That(FixNpcSpawnCommandHandler.IsAlreadyOnMesh(15.9f), Is.True);
            Assert.That(FixNpcSpawnCommandHandler.IsAlreadyOnMesh(16f), Is.False);
            Assert.That(Source("commands", "admincommands", "FixNpcPosition.cs"), Does.Not.Contain("GetClosestPoint"));
        }

        [Test]
        public void FixNpcSpawn_TakesTheNearerOfFloorAndRoof()
        {
            Vector3 pos = new(0, 0, 100);
            Vector3 floor = new(0, 0, 0);
            Vector3 roof = new(0, 0, 130);
            Assert.That(FixNpcSpawnCommandHandler.ChooseSnapPoint(pos, floor, roof), Is.EqualTo(roof));
            Assert.That(FixNpcSpawnCommandHandler.ChooseSnapPoint(pos, new Vector3(0, 0, 90), roof), Is.EqualTo(new Vector3(0, 0, 90)));
            Assert.That(FixNpcSpawnCommandHandler.ChooseSnapPoint(pos, new Vector3(0, 0, 50), new Vector3(0, 0, 150)), Is.EqualTo(new Vector3(0, 0, 50)), "a tie takes the floor");
            Assert.That(FixNpcSpawnCommandHandler.ChooseSnapPoint(pos, null, roof), Is.EqualTo(roof));
            Assert.That(FixNpcSpawnCommandHandler.ChooseSnapPoint(pos, floor, null), Is.EqualTo(floor));
            Assert.That(FixNpcSpawnCommandHandler.ChooseSnapPoint(pos, null, null), Is.Null);
        }

        [Test]
        public void FixNpcSpawn_NeverMovesScriptedNpcsBotsOrPets()
        {
            string source = Source("commands", "admincommands", "FixNpcPosition.cs");
            Assert.That(source, Does.Contain("if (!dryRun && !npc.LoadedFromScript)"));
            string excluded = Method(source, "private static bool ShouldNpcBeExcluded(GameNPC npc)");
            Assert.That(excluded, Does.Contain("npc is IGamePlayer"), "gamebots and companion bots are GameBots, not spawn points");
            Assert.That(excluded, Does.Contain("npc.Brain is IControlledBrain"), "pets");
            Assert.That(source, Does.Contain("case \"visible\":"));
            Assert.That(source, Does.Contain("float snapDown = 4096f;"));
        }

        // /door

        [Test]
        public void Door_KeepAndRelicDoorsTakeOnlyTheSubcommandsThatLeaveThemAlone()
        {
            foreach (string sub in new[] { "info", "heal", "kill" })
                Assert.That(NewDoorCommandHandler.KeepDoorAllows(sub), Is.True, sub);
            foreach (string sub in new[] { "add", "update", "delete", "name", "guild", "level", "realm", "sound", "locked", "unlocked" })
                Assert.That(NewDoorCommandHandler.KeepDoorAllows(sub), Is.False, sub);
        }

        [Test]
        public void Door_AnyTargetThatIsNotADoorIsRefusedBeforeASubcommandRuns()
        {
            string command = Method(Source("commands", "gmcommands", "door.cs"), "public void OnCommand(GameClient client, string[] args)");
            Assert.That(command, Does.Contain("client.Player.TargetObject is not GameDoorBase targetDoor"));
            Assert.That(command, Does.Not.Contain("GameDoor targetDoor = null"), "a keep door left this null and every subcommand crashed");
        }

        // /targetstats

        [Test]
        public void TargetStats_ReadsTheVarianceWithoutDrawingADeckCard()
        {
            string source = Source("commands", "playercommands", "targetstats.cs");
            Assert.That(source, Does.Not.Contain("out (double lowerLimit, double upperLimit) varianceRange"));
            Assert.That(source, Does.Contain("CalculateWeaponSkill(weapon, 0, out double baseWeaponSkill)"));
            Assert.That(source, Does.Contain("CalculateVarianceRange(client.Player, spec)"));
        }

        [Test]
        public void TargetStats_ShieldAttackNeedsAShieldAndShieldSpec()
        {
            Assert.That(TargetStatsCommandHandler.ShowsShieldAttack(true, 1), Is.True);
            Assert.That(TargetStatsCommandHandler.ShowsShieldAttack(true, 0), Is.False);
            Assert.That(TargetStatsCommandHandler.ShowsShieldAttack(false, 50), Is.False);
            Assert.That(Source("commands", "playercommands", "targetstats.cs"), Does.Contain("if (target is GamePlayer && ShowsShieldAttack("));
        }

        // GM login

        [Test]
        public void Login_OnlyPlayersAreMovedFromKeepsAndBattlegrounds()
        {
            Assert.That(PlayerInitRequestHandler.RelocatesAtLogin(ePrivLevel.Player), Is.True);
            Assert.That(PlayerInitRequestHandler.RelocatesAtLogin(ePrivLevel.GM), Is.False);
            Assert.That(PlayerInitRequestHandler.RelocatesAtLogin(ePrivLevel.Admin), Is.False);
        }

        // /stuck

        [Test]
        public void Stuck_SaysWhatHappensAndClearsTheFlagWhenTheQuitIsRefused()
        {
            string command = Method(Source("commands", "playercommands", "stuck.cs"), "public void OnCommand(GameClient client, string[] args)");
            Assert.That(command, Does.Contain("last known safe point"));
            int refused = command.IndexOf("if (!player.Quit(false))", StringComparison.Ordinal);
            Assert.That(refused, Is.GreaterThan(command.IndexOf("UseSafePosition = true", StringComparison.Ordinal)));
            Assert.That(command.Substring(refused), Does.Contain("movementComponent.UseSafePosition = false;"));
        }

        // Invalid names

        [Test]
        public void InvalidNames_SapphIsAllowedSapphicIsNot()
        {
            Func<string, bool> rejected = EraBotNames.LoadRejected(Path.Combine(Root(), "config", "invalidnames.txt"));
            Assert.That(rejected, Is.Not.Null);
            Assert.That(rejected("Sapphira"), Is.False);
            Assert.That(rejected("Sapphire"), Is.False);
            Assert.That(rejected("Sapphic"), Is.True);
        }

        // /stats

        [Test]
        public void Stats_RealmPointsPerHourAreRounded()
        {
            Assert.That(PlayerStatistics.RPsPerHour(1000, TimeSpan.FromHours(3)), Is.EqualTo(333u));
            Assert.That(PlayerStatistics.RPsPerHour(1000, TimeSpan.FromMinutes(90)), Is.EqualTo(667u));
            Assert.That(PlayerStatistics.RPsPerHour(0, TimeSpan.FromHours(1)), Is.EqualTo(0u));
            Assert.That(PlayerStatistics.RPsPerHour(500, TimeSpan.Zero), Is.EqualTo(0u));
        }
    }
}
