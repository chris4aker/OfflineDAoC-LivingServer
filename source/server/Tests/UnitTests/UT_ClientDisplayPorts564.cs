using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DOL.Database;
using DOL.GS;
using DOL.GS.Keeps;
using DOL.GS.PacketHandler;
using DOL.GS.PacketHandler.Client.v168;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Issue #564 step 1, OpenDAoC client display ports for the 1.127 client: the delve cast time (ef017586c), the portal
    /// stone war map (d74d4941f), the keep hookpoint store (476059682, d15534653), spell and song tooltip levels
    /// (42bf098af, f5f3e4e43, 95e394d35), quest reward delves (ec097f507) and item charge tooltips (a376d6182).
    /// These are server-to-client packets for players; bots and companions have no client and never get them.
    /// </summary>
    [TestFixture]
    public class UT_ClientDisplayPorts564
    {
        #region Delve cast time (ef017586c)

        [TestCase(0, false)]
        [TestCase(500, true)]
        [TestCase(1999, true)]
        [TestCase(2000, true)]
        [TestCase(3500, true)]
        public void CastTimer_IsSentForEveryCastThatIsNotInstant(int castTime, bool sent)
        {
            // The client shows cast_timer + 2000 ms and 2.0 s when the field is missing, so a 1.5 s cast showed 2.0 s.
            Assert.That(SpellDelve.SendsCastTimer(castTime), Is.EqualTo(sent));
        }

        [TestCase(1500, -500)]
        [TestCase(2000, 0)]
        [TestCase(3000, 1000)]
        public void CastTimer_IsTheCastTimeLessTheClientsTwoSeconds(int castTime, int value)
        {
            Assert.That(SpellDelve.CastTimerValue(castTime), Is.EqualTo(value));
            Assert.That(SpellDelve.CastTimerValue(castTime) + 2000, Is.EqualTo(castTime), "what the client shows");
        }

        #endregion

        #region Portal stone war map (d74d4941f)

        [TestCase(eRealm.Albion, (ushort)1)]
        [TestCase(eRealm.Midgard, (ushort)2)]
        [TestCase(eRealm.Hibernia, (ushort)3)]
        public void WarMap_On1127_UsesDialog0x26WithTheRealmsMap(eRealm realm, ushort map)
        {
            (eDialogCode code, ushort data1) = FrontiersPortalStone.WarmapDialog(GameClient.eClientVersion.Version1127, realm);
            Assert.That((byte)code, Is.EqualTo(0x26));
            Assert.That(data1, Is.EqualTo(map));
        }

        [TestCase(eRealm.Albion, eDialogCode.WarmapWindowAlbion)]
        [TestCase(eRealm.Midgard, eDialogCode.WarmapWindowMidgard)]
        [TestCase(eRealm.Hibernia, eDialogCode.WarmapWindowHibernia)]
        public void WarMap_OnOlderClients_KeepsTheOldCodes(eRealm realm, eDialogCode expected)
        {
            (eDialogCode code, ushort data1) = FrontiersPortalStone.WarmapDialog(GameClient.eClientVersion.Version1126, realm);
            Assert.That(code, Is.EqualTo(expected));
            Assert.That(data1, Is.EqualTo(0));
        }

        [Test]
        public void PortalStone_SendsTheDialogFromWarmapDialog()
        {
            string interact = Method(Source("keeps", "Gameobjects", "FrontiersPortalStone.cs"), "public override bool Interact(GamePlayer player)");
            Assert.That(interact, Does.Contain("WarmapDialog(player.Client.Version, player.Realm)"));
            Assert.That(interact, Does.Contain("SendDialogBox(code, map,"), "the map goes in data1");
        }

        #endregion

        #region Hookpoint store on 1.127 (476059682, d15534653)

        [Test]
        public void HookPointStore_Has1127HeaderAndMerchantRows()
        {
            var items = new ArrayList
            {
                new HookPointItem("Ballista", 3, 0x0A0B, "GameSiegeBallista", 0x0102),
                new HookPointItem("Boiling Oil", 7, 0x0C0D, "GameSiegeCauldron", 0x0304),
            };

            byte[] body = Body(pak => PacketLib1127.WriteHookPointStore(pak, 0x0123, 0x0045, 0x0081, items));

            var expected = new List<byte> { 2, 0, 1, 1, 1, 0x23, 0x01, 0x45, 0x00, 0x81, 0x00 };
            expected.AddRange(Row(0, 0x01, 0x02, 3, 0x0A0B, "Ballista"));
            expected.AddRange(Row(1, 0x03, 0x04, 7, 0x0C0D, "Boiling Oil"));
            Assert.That(body, Is.EqualTo(expected.ToArray()));
        }

        [Test]
        public void HookPointStore_SendsAtMostThirtyItems()
        {
            var items = new ArrayList();
            for (int i = 0; i < 40; i++)
                items.Add(new HookPointItem("Guard", 1, 1, "GameKeepGuard", 0));

            byte[] body = Body(pak => PacketLib1127.WriteHookPointStore(pak, 1, 1, 1, items));
            Assert.That(body[0], Is.EqualTo(PacketLib1127.MAX_HOOKPOINT_STORE_ITEMS));
            int rowLength = Row(0, 0, 0, 1, 1, "Guard").Length;
            Assert.That(body.Length, Is.EqualTo(11 + PacketLib1127.MAX_HOOKPOINT_STORE_ITEMS * rowLength));
        }

        [Test]
        public void HookPointStore_1127OverridesTheOldLayout()
        {
            string lib = Source("packets", "Server", "PacketLib1127.cs");
            Assert.That(Method(lib, "public override void SendHookPointStore(GameKeepHookPoint hookPoint)"),
                Does.Contain("WriteHookPointStore(pak,"));
        }

        private static byte[] Row(byte slot, byte flagHigh, byte flagLow, uint gold, ushort icon, string name)
        {
            var row = new List<byte> { slot, flagHigh, flagLow, 0, 0, 0, 0, 0, 0 };
            row.AddRange(BitConverter.GetBytes(gold));
            row.Add((byte)(icon & 0xFF));
            row.Add((byte)(icon >> 8));
            row.AddRange(BitConverter.GetBytes((uint)name.Length + 1));
            row.AddRange(System.Text.Encoding.ASCII.GetBytes(name));
            row.Add(0);
            return row.ToArray();
        }

        #endregion

        #region Spell tooltip levels (42bf098af, f5f3e4e43, 95e394d35)

        private static Spell MakeSpell(int id, int tooltipId, int level, ushort icon = 0)
        {
            return new Spell(new DbSpell { SpellID = id, TooltipId = (ushort)tooltipId, Icon = icon, Name = "Spell " + id, Type = eSpellType.DirectDamage.ToString(), Target = "Enemy" }, level);
        }

        private static readonly List<(Skill, Skill)> NoSkills = new();
        private static readonly List<(SpellLine, List<Skill>)> NoLists = new();

        [Test]
        public void Tooltip_PrefersThePlayersOwnSpellOverTheLevelOneTemplate()
        {
            Spell own = MakeSpell(10, 500, 40);
            Spell template = MakeSpell(10, 500, 1);
            var lists = new List<(SpellLine, List<Skill>)> { (new SpellLine("line", "Line", "Spec", true), new List<Skill> { MakeSpell(11, 501, 30), own }) };

            Spell found = DetailDisplayHandler.FindSpellForTooltip(Array.Empty<(Spell, ushort)>(), 500, NoSkills, lists, _ => template);
            Assert.That(found, Is.SameAs(own));
            Assert.That(found.Level, Is.EqualTo(40));
        }

        [Test]
        public void Tooltip_FindsUsableSkillsAfterTheSpellLists()
        {
            Spell own = MakeSpell(12, 502, 25);
            var skills = new List<(Skill, Skill)> { (own, new SpellLine("line", "Line", "Spec", true)) };
            Assert.That(DetailDisplayHandler.FindSpellForTooltip(Array.Empty<(Spell, ushort)>(), 502, skills, NoLists, _ => null), Is.SameAs(own));
        }

        [Test]
        public void Tooltip_AnEffectOnThePlayerComesFirst()
        {
            Spell onPlayer = MakeSpell(13, 503, 50);
            var lists = new List<(SpellLine, List<Skill>)> { (new SpellLine("line", "Line", "Spec", true), new List<Skill> { MakeSpell(13, 503, 20) }) };
            Assert.That(DetailDisplayHandler.FindSpellForTooltip(new[] { (onPlayer, (ushort)0) }, 503, NoSkills, lists, _ => null), Is.SameAs(onPlayer));
        }

        [Test]
        public void Tooltip_RealmAbilitySpellWithoutIdIsFoundByIconAndGetsTheId()
        {
            Spell fromRealmAbility = MakeSpell(14, 0, 50, icon: 7001);
            Spell other = MakeSpell(15, 900, 50, icon: 7001);

            Spell found = DetailDisplayHandler.FindSpellForTooltip(new[] { (other, (ushort)7001), (fromRealmAbility, (ushort)7001) }, 7001, NoSkills, NoLists, _ => null);
            Assert.That(found, Is.SameAs(fromRealmAbility), "a spell with its own id is matched only by that id");
            Assert.That(found.InternalID, Is.EqualTo(7001), "the client's reply carries the id it asked for");
        }

        [Test]
        public void Tooltip_FallsBackToTheTemplate()
        {
            Spell template = MakeSpell(16, 504, 1);
            Assert.That(DetailDisplayHandler.FindSpellForTooltip(Array.Empty<(Spell, ushort)>(), 504, NoSkills, NoLists, id => id == 504 ? template : null), Is.SameAs(template));
        }

        [Test]
        public void SpellAndSongTooltips_UseThePlayersSpell()
        {
            string handler = Source("packets", "Client", "168", "DetailDisplayHandler.cs");
            int spells = handler.IndexOf("case 24://SpellsNew", StringComparison.Ordinal);
            int styles = handler.IndexOf("case 25://StylesNew", StringComparison.Ordinal);
            int songs = handler.IndexOf("case 26://SongsNew", StringComparison.Ordinal);
            int ras = handler.IndexOf("case 27://RANew", StringComparison.Ordinal);
            Assert.That(handler.Substring(spells, styles - spells), Does.Contain("FindSpellForTooltip(client, objectId, snapSkills, snapLists)").And.Not.Contain("GetSpellByTooltipID"));
            Assert.That(handler.Substring(songs, ras - songs), Does.Contain("FindSpellForTooltip(client, objectId, snapSkills, snapLists)").And.Not.Contain("GetSpellByTooltipID"));
            Assert.That(Source("packets", "Server", "PacketLib1110.cs"), Does.Contain("DelveSong(m_gameClient, spell)"));
        }

        #endregion

        #region Quest reward delve (ec097f507)

        [Test]
        public void QuestReward_IsFoundByOnePlusItsPosition()
        {
            var rewards = new List<DbItemTemplate> { new() { Id_nb = "basic" }, new() { Id_nb = "optional1" }, new() { Id_nb = "optional2" } };
            Assert.That(DetailDisplayHandler.QuestWindowReward(rewards, 1).Id_nb, Is.EqualTo("basic"));
            Assert.That(DetailDisplayHandler.QuestWindowReward(rewards, 3).Id_nb, Is.EqualTo("optional2"));
            Assert.That(DetailDisplayHandler.QuestWindowReward(rewards, 0), Is.Null, "0 is what the old item block sent");
            Assert.That(DetailDisplayHandler.QuestWindowReward(rewards, 4), Is.Null);
            Assert.That(DetailDisplayHandler.QuestWindowReward(null, 1), Is.Null, "no quest window was sent");
        }

        [Test]
        public void QuestReward_PositionIsStampedIntoTheItemBlocksFirstShort()
        {
            using var pak = new GSTCPPacketOut().Init(0x9B);
            long start = pak.Position;
            pak.WriteShort(0); // WriteItemData's unique id
            pak.WriteByte(0x2A);
            pak.WriteByte(0x2B);
            long end = pak.Position;

            PacketLib194.StampQuestRewardPosition(pak, start, 3);

            Assert.That(pak.Position, Is.EqualTo(end), "the writer goes on after the block");
            byte[] bytes = pak.ToArray();
            Assert.That(bytes.Skip((int)start).ToArray(), Is.EqualTo(new byte[] { 0x00, 0x03, 0x2A, 0x2B }));
        }

        [TestCase(GameClient.eClientVersion.Version1112, false)]
        [TestCase(GameClient.eClientVersion.Version1114, false)]
        [TestCase(GameClient.eClientVersion.Version1115, true)]
        [TestCase(GameClient.eClientVersion.Version1127, true)]
        public void QuestReward_PositionIsStampedOnlyWhereTheBlockHasAPlaceholder(GameClient.eClientVersion version, bool stamped)
        {
            // Before 1.115 the item block's first two bytes are the level and value1, not a u16 placeholder.
            Assert.That(PacketLib194.StampsQuestRewardPosition(version), Is.EqualTo(stamped));
            string item = Method(Source("packets", "Server", "PacketLib194.cs"), "private void WriteQuestRewardItem(");
            Assert.That(item, Does.Contain("if (StampsQuestRewardPosition(m_gameClient.Version))"));
        }

        [Test]
        public void QuestWindows_WriteRewardsThroughWriteQuestRewards()
        {
            string lib = Source("packets", "Server", "PacketLib194.cs");
            Assert.That(lib.Split("WriteQuestRewards(pak, player,").Length - 1, Is.EqualTo(2), "data quest and reward quest windows");
            Assert.That(lib, Does.Contain("TempProperties.SetProperty(QUEST_WINDOW_REWARDS_PROPERTY, rewards)"));
            Assert.That(Source("packets", "Client", "168", "DetailDisplayHandler.cs"), Does.Contain("GetProperty<List<DbItemTemplate>>(PacketLib194.QUEST_WINDOW_REWARDS_PROPERTY)"));
        }

        #endregion

        #region Item charges (a376d6182)

        [Test]
        public void ItemCharges_AreLookedUpByIdInAnySpellLine()
        {
            string write = Method(Source("packets", "Server", "PacketLib1124.cs"), "protected override void WriteItemData(GSTCPPacketOut pak, DbInventoryItem item)");
            Assert.That(write, Does.Contain("ChargeSpell(item.SpellID)"));
            Assert.That(write, Does.Contain("ChargeSpell(item.SpellID1)"));
            Assert.That(write, Does.Not.Contain("GlobalSpellsLines.Item_Effects"), "no Item_Effects-only scan");
        }

        #endregion

        private static byte[] Body(Action<GSTCPPacketOut> write)
        {
            using var pak = new GSTCPPacketOut().Init(0xD0);
            long header = pak.Length;
            write(pak);
            return pak.ToArray().Skip((int)header).ToArray();
        }

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

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
    }
}
