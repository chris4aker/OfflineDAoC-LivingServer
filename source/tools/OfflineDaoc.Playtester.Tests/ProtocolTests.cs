using System.Text;
using NUnit.Framework;
using OfflineDaoc.Playtester;
using OfflineDaoc.Playtester.Protocol;
using OfflineDaoc.Playtester.Scenarios;

namespace OfflineDaoc.Playtester.Tests;

/// <summary>Packet layouts the scripted player writes and reads, against the server's own code (named in each test).</summary>
public class ProtocolTests
{
    private static byte[] Latin(string s) => Text1252.Encoding.GetBytes(s);

    [Test]
    public void FrameCarriesLengthSequenceSessionCodeAndTheServersChecksum()
    {
        var (code, body) = ClientPackets.Command("&say hi");
        byte[] frame = ClientPackets.Frame(code, body.ToArray(), sequence: 0x0102, session: 0x0304);
        int len = body.Length;
        Assert.That(frame.Length, Is.EqualTo(10 + len + 2));
        Assert.That((frame[0] << 8) | frame[1], Is.EqualTo(len));
        Assert.That(frame[2..6], Is.EqualTo(new byte[] { 1, 2, 3, 4 }));
        Assert.That(frame[9], Is.EqualTo(0xAF));
        // PacketProcessor.CalculateChecksum, written out independently
        byte v1 = 0x7E, v2 = 0x7E;
        for (int i = 0; i < frame.Length - 2; i++) { v1 += frame[i]; v2 += v1; }
        ushort sum = (ushort)(v2 - ((v1 + v2) << 8));
        Assert.That((frame[^2] << 8) | frame[^1], Is.EqualTo(sum));
    }

    [Test]
    public void CommandIsAZeroByteThenTheTextNulTerminated()
    {
        var (code, body) = ClientPackets.Command("&send Aldric hey");
        Assert.That(code, Is.EqualTo(0xAF));
        byte[] b = body.ToArray();
        Assert.That(b[0], Is.EqualTo(0));
        Assert.That(Encoding.ASCII.GetString(b, 1, b.Length - 2), Is.EqualTo("&send Aldric hey"));
        Assert.That(b[^1], Is.EqualTo(0));
    }

    [Test]
    public void CreateCharacterPacketHasTheLengthTheServerReads()
    {
        // CharacterCreateRequestHandler.CreationCharacterData (1.126+): slot 1, name, 4, custom 1, face 6, skip 3, mood 1, skip 9,
        // op 1, customize 1, skip 2, skip 15, class, realm, race/gender, model 2, 8 stats, skip 40, 3 bytes, new constitution.
        var (code, body) = ClientPackets.CreateCharacter(1, 2, "Ptabc", 14, 1, 0, 32, new[] { 70, 70, 70, 60, 60, 60, 60, 60 });
        byte[] b = body.ToArray();
        Assert.That(code, Is.EqualTo(0xFF));
        Assert.That(b[0], Is.EqualTo(2), "slot + (realm - 1) * 10");
        int afterName = 1 + 4 + 5;
        Assert.That(b[afterName + 4], Is.EqualTo(1), "custom mode");
        int op = afterName + 4 + 1 + 6 + 3 + 1 + 9;
        Assert.That(b[op], Is.EqualTo(1), "operation create");
        int cls = op + 1 + 1 + 2 + 15;
        Assert.That(b[cls], Is.EqualTo(14));
        Assert.That(b[cls + 1], Is.EqualTo(1));
        Assert.That(b[cls + 3] | (b[cls + 4] << 8), Is.EqualTo(32), "model, little-endian");
        Assert.That(b[cls + 5], Is.EqualTo(70), "strength");
        Assert.That(b[^2], Is.EqualTo(70), "constitution again");
    }

    [Test]
    public void GiveItemIsAMoveToSlotThousandPlusTheObject()
    {
        var (code, body) = ClientPackets.MoveItem(7, 41, 1000 + 6940, 1);
        byte[] b = body.ToArray();
        Assert.That(code, Is.EqualTo(0xDD));
        Assert.That((b[2] << 8) | b[3], Is.EqualTo(7940));
        Assert.That((b[4] << 8) | b[5], Is.EqualTo(41));
    }

    [Test]
    public void PlayerRequestUsesCreatePlayerRequestNotTheChampionTrainer()
    {
        // eClientPackets.CreatePlayerRequest = 0xD5; 0x7C is ChampionTrainHandler (a first draft sent that).
        var (code, body) = ClientPackets.RequestPlayer(0x0203);
        Assert.That(code, Is.EqualTo(0xD5));
        Assert.That(body.ToArray()[..2], Is.EqualTo(new byte[] { 0x03, 0x02 }), "little-endian session id");
    }

    [Test]
    public void ChatMessageDropsTheChatWindowPrefix()
    {
        var w = new PacketWriter();
        w.Byte(ChatTypes.Send);
        w.CString("@@Aldric sends, \"good, slow xp but good :)\"");
        var e = (ChatMessage)ServerPackets.Decode(0xAF, w.ToArray(), 0);
        Assert.That(e.Type, Is.EqualTo(2));
        Assert.That(e.Text, Is.EqualTo("Aldric sends, \"good, slow xp but good :)\""));
        Assert.That(ChatTypes.IsTellFrom(new ChatLine(1, e.Type, e.Text, "p"), "Aldric"), Is.True);
        Assert.That(ChatTypes.IsTellFrom(new ChatLine(1, e.Type, e.Text, "p"), "Brennan"), Is.False);
    }

    [Test]
    public void FactionLinesAreReadAsTheCoordinatorWritesThem()
    {
        var line = new ChatLine(1, ChatTypes.Broadcast, "(Aldric) <Faction> lf cleric 1-3", "p");
        Assert.That(ChatTypes.IsFaction(line, out string who), Is.True);
        Assert.That(who, Is.EqualTo("Aldric"));
        Assert.That(ChatTypes.IsFaction(new ChatLine(1, ChatTypes.Broadcast, "[Trade] Brennan: wts", "p"), out _), Is.False);
        Assert.That(ChatTypes.IsFaction(new ChatLine(1, ChatTypes.Say, "(Aldric) <Faction> hi", "p"), out _), Is.False);
    }

    [Test]
    public void GroupInviteDialogKeepsItsDataWords()
    {
        var w = new PacketWriter();
        w.Byte(0); w.Byte(0x05);
        w.Short(3); w.Short(0); w.Short(0); w.Short(0);
        w.Byte(1); w.Byte(1);
        w.CString("Ptlead has invited you to join a group.");
        var d = (DialogBox)ServerPackets.Decode(0x81, w.ToArray(), 0);
        Assert.That((d.Code, d.Data1, d.Type), Is.EqualTo((5, 3, 1)));
        Assert.That(d.Text, Does.Contain("invited"));
    }

    [Test]
    public void GroupWindowListsEveryMember()
    {
        var w = new PacketWriter();
        w.Byte(6); w.Byte(2);
        w.Pascal("Ptgm"); w.Pascal("Fighter"); w.Short(10535); w.Byte(5);
        w.Pascal("Aldric"); w.Pascal("Armsman"); w.Short(10536); w.Byte(1);
        var g = (GroupWindow)ServerPackets.Decode(0x16, w.ToArray(), 0);
        Assert.That(g.Members.Select(m => m.Name), Is.EqualTo(new[] { "Ptgm", "Aldric" }));
        Assert.That(g.Members[1].ObjectId, Is.EqualTo(10536));
    }

    [Test]
    public void OverviewStringsLoseTheirNul()
    {
        // PacketLib1126.SendCharacterOverview writes its names with the NUL counted in the length (first run, 2026-11-04).
        var w = new PacketWriter();
        for (int i = 0; i < 4; i++) w.IntLE(0);
        w.Byte(1);                                        // slot 0: level 1
        WriteIntPascalNul(w, "Ptsave");
        w.IntLE(0x18); w.Byte(1); w.Bytes(new byte[10]); w.Bytes(new byte[13]);
        WriteIntPascalNul(w, "Camelot Hills");
        WriteIntPascalNul(w, "Fighter");
        WriteIntPascalNul(w, "Briton");
        w.ShortLE(32); w.Byte(1); w.Byte(0);
        w.Bytes(new byte[40]);                            // models and colours
        w.Bytes(new byte[8]);                             // stats
        w.Byte(14); w.Byte(1); w.Byte(1);                 // class, realm, race
        w.Bytes(new byte[5]);
        for (int slot = 1; slot < 10; slot++) w.Byte(0);
        var list = ((CharacterList)ServerPackets.Decode(0xFC, w.ToArray(), 1)).Characters;
        Assert.That(list, Has.Count.EqualTo(1));
        Assert.That((list[0].Name, list[0].ClassName, list[0].Location, list[0].ClassId, list[0].Realm),
            Is.EqualTo(("Ptsave", "Fighter", "Camelot Hills", 14, 1)));
    }

    private static void WriteIntPascalNul(PacketWriter w, string s)
    {
        w.IntLE((uint)s.Length + 1);
        w.Bytes(Latin(s));
        w.Byte(0);
    }

    [Test]
    public void PlayerPositionGivesTheSessionAndObject()
    {
        // PlayerPositionUpdateHandler.CreateOutPak1127
        var w = new PacketWriter();
        w.FloatLE(574123); w.FloatLE(530520); w.FloatLE(2933); w.FloatLE(0); w.FloatLE(0);
        w.Short(3); w.Short(4); w.Short(0);
        var p = (PlayerMoved)ServerPackets.Decode(0xA9, w.ToArray(), 0);
        Assert.That((p.SessionId, p.ObjectId, p.X), Is.EqualTo((3, 4, 574123f)));
    }

    [Test]
    public void InventoryUpdateReadsNamedAndEmptySlots()
    {
        var w = new PacketWriter();
        w.Byte(2); w.Bytes(new byte[5]);
        w.Byte(41);                                       // the quest item
        w.Short(0); w.Byte(1); w.Byte(1); w.Byte(0); w.Byte(0); w.Byte(0); w.Byte(0);
        w.Short(1); w.Bytes(new byte[5]); w.Short(498); w.Byte(0); w.Short(0); w.Byte(0); w.Short(0);
        w.Pascal("Sealed Note");
        w.Byte(42); w.Bytes(new byte[24]);                // an empty slot (WriteItemData: 24 zeros)
        var inv = (InventoryUpdate)ServerPackets.Decode(0x02, w.ToArray(), 0);
        Assert.That(inv.Items.Select(i => (i.Slot, i.Name)), Is.EqualTo(new[] { (41, "Sealed Note"), (42, "") }));
    }

    [Test]
    public void PositionTellsTheServerTheTargetIsInView()
    {
        // ProcessActionFlags: TARGET_IN_VIEW (0x30) on every position update, or melee says "X is not in view!"
        byte[] without = ClientPackets.Position(1, 2, 3, 0, 1, 2, 0, 0, 100).Item2.ToArray();
        byte[] with = ClientPackets.Position(1, 2, 3, 0, 1, 2, 0, 0, 100, targetInView: true).Item2.ToArray();
        int action = 20 + 2 + 2 + 2 + 1 + 1 + 2 + 2;
        Assert.That(without[action], Is.EqualTo(0));
        Assert.That(with[action], Is.EqualTo(0x30));
    }

    [Test]
    public void TrainSpecNamesTheLineByItsPlaceThenTheLevel()
    {
        // PlayerTrainHandler: 8 codes ending at 0xFFFFFFFF, then a level for each code, realm abilities from byte 64
        var (code, body) = ClientPackets.TrainSpec(3, 9);
        byte[] b = body.ToArray();
        Assert.That(code, Is.EqualTo(0x53));
        var r = new PacketReader(b);
        Assert.That(r.Int(), Is.EqualTo(3u));
        Assert.That(r.Int(), Is.EqualTo(0xFFFFFFFFu));
        r.Skip(24);
        Assert.That(r.Int(), Is.EqualTo(9u));
        Assert.That(b.Length, Is.EqualTo(64 + 200));
    }

    [Test]
    public void TrainerWindowListsTheSpecsAndPoints()
    {
        var w = new PacketWriter();
        w.Byte(2); w.Byte(80); w.Byte(0); w.Byte(0);
        w.Byte(0); w.Byte(1); w.Byte(2); w.Pascal("Slash");
        w.Byte(1); w.Byte(1); w.Byte(2); w.Pascal("Shields");
        var t = (TrainerSpecs)ServerPackets.Decode(0x7B, w.ToArray(), 0);
        Assert.That(t.Points, Is.EqualTo(80));
        Assert.That(t.Specs.Select(sp => (sp.Index, sp.Name)), Is.EqualTo(new[] { (0, "Slash"), (1, "Shields") }));
    }

    [Test]
    public void MoneyIsCountedInCopper()
    {
        var w = new PacketWriter();
        w.Byte(5); w.Byte(3); w.Short(2); w.Short(0); w.Short(1);   // 5 copper, 3 silver, 2 gold, 0 mithril, 1 platinum
        var m = (MoneyUpdate)ServerPackets.Decode(0xFA, w.ToArray(), 0);
        Assert.That(m.Copper, Is.EqualTo(1L * 1000 * 100 * 100 + 2 * 100 * 100 + 3 * 100 + 5));
        Assert.That(GuildDues.ParseMoney("1 gold, 2 silver, 3 copper,"), Is.EqualTo(10203));
        Assert.That(GuildDues.ParseMoney("0 copper pieces"), Is.EqualTo(0));
    }

    [Test]
    public void ObjectUpdatesArePlacedInTheirZone()
    {
        // ObjectUpdate (168): zone-relative x / y, the zone in its own byte (bit 8 in the flags)
        var w = new PacketWriter();
        w.Short(0); w.Short(0);
        w.Short(500); w.Short(0); w.Short(700); w.Short(0); w.Short(2300); w.Short(0);
        w.Short(42); w.Short(0); w.Byte(55); w.Byte(0); w.Byte(3); w.Byte(3);
        var m = (ObjectMoved)ServerPackets.Decode(0xA1, w.ToArray(), 0);
        Assert.That((m.ObjectId, m.Zone, m.X, m.Y, m.Z, m.Health), Is.EqualTo((42, 3, 500, 700, 2300, 55)));
    }

    [Test]
    public void FreshNamesAreNamesTheServerTakes()
    {
        for (int i = 0; i < 50; i++)
            Assert.That(Names.Valid(Names.Fresh("Ptsave")), Is.True);
        Assert.That(Names.Valid("pt"), Is.False);
        Assert.That(Names.Valid("Pt9x"), Is.False);
    }
}
