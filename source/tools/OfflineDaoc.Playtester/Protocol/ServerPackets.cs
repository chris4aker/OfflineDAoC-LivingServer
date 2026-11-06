// What the scripted player reads: the server -> client packets it needs, decoded into records. The layouts are ported
// from the new client's network client (source/new-client/godot/game/net/DaocClient.cs, Handle / ReadWorld /
// ReadOverview), which follows the server's PacketLib1124 ... 1127.
namespace OfflineDaoc.Playtester.Protocol;

internal abstract record ServerEvent;
internal sealed record LoginGranted(string Account, string ServerName) : ServerEvent;
internal sealed record LoginDenied(int Code) : ServerEvent;
internal sealed record SessionAssigned(int SessionId) : ServerEvent;
internal sealed record RealmAllowed(int Realm) : ServerEvent;
internal sealed record CharacterList(IReadOnlyList<CharacterSlot> Characters) : ServerEvent;
internal sealed record PositionAndObjectId(float X, float Y, float Z, int ObjectId, int Heading, int Region) : ServerEvent;
internal sealed record LivingCreated(int ObjectId, string Name, string Guild, int Level, bool IsPlayer, int X, int Y, int Z) : ServerEvent;
internal sealed record ObjectRemoved(int ObjectId) : ServerEvent;
/// <summary>ObjectUpdate: X / Y are zone-relative (the zone is given), Z absolute.</summary>
internal sealed record ObjectMoved(int ObjectId, int Health, int Zone, int X, int Y, int Z, int Target) : ServerEvent;
internal sealed record PlayerMoved(int SessionId, int ObjectId, float X, float Y, float Z) : ServerEvent;
internal sealed record ChatMessage(int Type, string Text, bool Popup) : ServerEvent;
internal sealed record DialogBox(int Code, int Data1, int Data2, int Data3, int Data4, int Type, string Text, string QuestTitle) : ServerEvent;
internal sealed record GroupWindow(IReadOnlyList<GroupMember> Members) : ServerEvent;
internal sealed record QuestEntry(int Index, string Name, string Text) : ServerEvent;
internal sealed record RegionChanged(int Region) : ServerEvent;
internal sealed record Jumped(int X, int Y, int Z, int Heading) : ServerEvent;
internal sealed record QuitToScreen(int Mode) : ServerEvent;
internal sealed record NameChecked(string Name, int Result) : ServerEvent;
internal sealed record CharacterInfoUpdate(string Name, int Level, string ClassName, string Guild) : ServerEvent;
internal sealed record InitFinished : ServerEvent;
internal sealed record LosRequest(int Source, int Target) : ServerEvent;
internal sealed record TargetChanged(int ObjectId) : ServerEvent;
internal sealed record Died(int ObjectId, int Killer) : ServerEvent;
internal sealed record PointsUpdate(long RealmPoints, long Experience) : ServerEvent;
internal sealed record Disconnected(string Why) : ServerEvent;
/// <summary>TrainerWindow code 0: the points to spend and each trainable specialization (its place, level, name).</summary>
internal sealed record TrainerSpecs(int Points, IReadOnlyList<(int Index, int Level, string Name)> Specs) : ServerEvent;
/// <summary>MoneyUpdate: the purse in copper (Money.GetMoney: mithril, platinum, gold, silver, copper).</summary>
internal sealed record MoneyUpdate(long Copper) : ServerEvent;
internal sealed record InventoryUpdate(IReadOnlyList<InventoryItem> Items) : ServerEvent;
internal sealed record InventoryItem(int Slot, string Name, int Level, int Count);
internal sealed record OtherPacket(int Code, int Length) : ServerEvent;

internal sealed record CharacterSlot(int Realm, int Slot, string Name, int Level, int ClassId, string ClassName, int Race, string RaceName,
    int Region, string Location);
internal sealed record GroupMember(string Name, string ClassName, int ObjectId, int Level);

internal static class ServerPackets
{
    /// <summary>Decodes one server packet (its code and body); unknown codes come back as OtherPacket.</summary>
    public static ServerEvent Decode(int code, byte[] data, int realmAsked)
    {
        var r = new PacketReader(data);
        switch (code)
        {
            case 0x2A:                                    // LoginGranted (PacketLib1127)
                return new LoginGranted(r.Pascal(), r.Pascal());
            case 0x2C:                                    // LoginDenied (eLoginError)
                return new LoginDenied(r.Byte());
            case 0x28:                                    // SessionID (little-endian)
                return new SessionAssigned(r.ShortLE());
            case 0xFE:                                    // Realm the account may use (0 = any)
                return new RealmAllowed(r.Byte());
            case 0xFC:                                    // CharacterOverview1126
                return new CharacterList(ReadOverview(r, realmAsked));
            case 0x20:                                    // PositionAndObjectID (1124)
            {
                float x = r.FloatLE(), y = r.FloatLE(), z = r.FloatLE();
                int oid = r.Short(), heading = r.Short();
                r.Short(); r.Short();                     // dungeon zone offsets
                return new PositionAndObjectId(x, y, z, oid, heading, r.Short());
            }
            case 0xDA:                                    // NPCCreate (1124): monsters, NPCs, the gamebots
            {
                int oid = r.Short(); r.Short(); r.Short();
                int z = r.Short(); int x = (int)r.Int(); int y = (int)r.Int();
                r.Short(); r.Short(); r.Byte();
                int level = r.Byte();
                r.Byte(); r.Byte(); r.Byte(); r.Byte(); r.Short();
                string name = r.Pascal();
                return new LivingCreated(oid, name, r.Pascal(), level, false, x, y, z);
            }
            case 0x4B:                                    // PlayerCreate172 (1124): another player
            {
                float x = r.FloatLE(), y = r.FloatLE(), z = r.FloatLE();
                r.Short();                                // session
                int oid = r.Short(); r.Short(); r.Short();
                int level = r.Byte(); r.Byte();
                r.Skip(7 + 3);                            // face, 3 bytes
                string name = r.Pascal();
                return new LivingCreated(oid, name, r.Pascal(), level, true, (int)x, (int)y, (int)z);
            }
            case 0xA1:                                    // ObjectUpdate (168): speed, heading, x, target x, y, target y, z,
            {                                             // target z (zone-relative), the object, its target, health, flags, zone
                r.Short(); r.Short();
                int x = r.Short(); r.Short();
                int y = r.Short(); r.Short();
                int z = r.Short(); r.Short();
                int oid = r.Short();
                int target = r.Short();
                int health = r.Byte();
                int flags = r.Byte();
                int zone = r.Byte() | ((flags & 0x04) << 6);
                return new ObjectMoved(oid, health, zone, x, y, (short)z, target);
            }
            case 0xA9:                                    // PlayerPosition (PlayerPositionUpdateHandler.CreateOutPak1127):
            {                                             // another player's position, sent to the players around
                float x = r.FloatLE(), y = r.FloatLE(), z = r.FloatLE();
                r.FloatLE(); r.FloatLE();                 // speed, fall speed
                int session = r.Short();
                return new PlayerMoved(session, r.Short(), x, y, z);
            }
            case 0x02:                                    // InventoryUpdate (189 + WriteItemData 1124): items by slot
            {
                int n = r.Byte();
                r.Skip(5);                                // shield shown, hidden, hood, weapon slots, window
                var items = new List<InventoryItem>();
                for (int i = 0; i < n && r.Left > 0; i++)
                    items.Add(ReadItem(r));
                return new InventoryUpdate(items);
            }
            case 0xA2:                                    // RemoveObject
            case 0xE1:                                    // ObjectDelete
                return new ObjectRemoved(r.Short());
            case 0xAF:                                    // Message (1127): type, "@@" chat window / "##" popup, text
            {
                int type = r.Byte();
                string text = r.CString();
                bool popup = text.StartsWith("##", StringComparison.Ordinal);
                if (popup || text.StartsWith("@@", StringComparison.Ordinal))
                    text = text[2..];
                return new ChatMessage(type, text, popup);
            }
            case 0x81:                                    // Dialog (168)
            {
                r.Byte();
                int dcode = r.Byte();
                int d1 = r.Short(), d2 = r.Short(), d3 = r.Short(), d4 = r.Short();
                int type = r.Byte();
                r.Byte();
                if (dcode is 0x21 or 0x22)
                {                                         // a quest offer (0x22) or its reward (0x21)
                    string title = r.Pascal();
                    string summary = r.Pascal();
                    int storyLen = r.Short();
                    return new DialogBox(dcode, d1, d2, d3, d4, type, summary + "\n\n" + r.FixedRaw(storyLen), title);
                }
                return new DialogBox(dcode, d1, d2, d3, d4, type, r.CString(), "");
            }
            case 0x16:                                    // VariousUpdate: 6 = the group window, 3 = your character
            {
                int sub = r.Byte();
                if (sub == 6)
                {
                    int members = r.Byte();
                    var list = new List<GroupMember>();
                    for (int i = 0; i < members && r.Left > 0; i++)
                        list.Add(new GroupMember(r.Pascal(), r.Pascal(), r.Short(), r.Byte()));
                    return new GroupWindow(list);
                }
                if (sub == 3)
                {
                    r.Byte(); r.Byte(); r.Byte();         // count, subtype, first
                    int level = r.Byte();
                    string name = r.Pascal();
                    r.Byte();
                    string className = r.Pascal();
                    r.Byte();
                    r.Pascal();                           // profession
                    r.Byte();
                    r.Pascal();                           // title
                    r.Byte();
                    r.Pascal();                           // realm title
                    r.Byte();
                    r.Pascal();                           // base class
                    r.Byte();
                    string guild = r.Pascal();
                    return new CharacterInfoUpdate(name, level, className, guild);
                }
                return new OtherPacket(code, data.Length);
            }
            case 0x83:                                    // QuestEntry (1124)
            {
                int index = r.Byte();
                int nameLen = r.Byte();
                int b2 = r.Byte(), b3 = r.Byte();
                if (nameLen == 0)
                    return new QuestEntry(index, "", "");
                if (b2 == 0 && b3 == 0)
                {                                         // a RewardQuest: goal count and level, then the name, description
                    r.Byte(); r.Byte();
                    string rname = r.FixedRaw(nameLen);
                    return new QuestEntry(index, rname, r.Pascal());
                }
                int descLen = b2 | (b3 << 8);
                r.Byte(); r.Byte();
                string name = r.FixedRaw(nameLen);
                return new QuestEntry(index, name, r.FixedRaw(descLen));
            }
            case 0xB7:                                    // RegionChanged
                return new RegionChanged(r.Short());
            case 0x04:                                    // CharacterJump
            {
                int x = (int)r.Int(), y = (int)r.Int();
                r.Short();
                int z = r.Short();
                return new Jumped(x, y, z, r.Short());
            }
            case 0xA4:                                    // Quit: back to the character screen
                return new QuitToScreen(r.Byte());
            case 0xCC:                                    // DupNameCheckReply (1126)
                return new NameChecked(r.Fixed(24), r.Byte());
            case 0x2B:                                    // CharacterInitFinished
                return new InitFinished();
            case 0xD0:                                    // CheckLOSRequest: the client must answer
                return new LosRequest(r.Short(), r.Short());
            case 0xF6:                                    // ChangeTarget
                return new TargetChanged(r.Short());
            case 0xAE:                                    // PlayerDeath
                return new Died(r.Short(), r.Short());
            case 0x7B:                                    // TrainerWindow (1105): count, points, code, 0; code 0 = the specs
            {
                int count = r.Byte(), points = r.Byte(), tcode = r.Byte();
                r.Byte();
                if (tcode != 0)
                    return new OtherPacket(code, data.Length);
                var specs = new List<(int, int, string)>();
                for (int i = 0; i < count && r.Left > 0; i++)
                {
                    int index = r.Byte(), level = r.Byte();
                    r.Byte();                             // the next level's cost
                    specs.Add((index, level, r.Pascal()));
                }
                return new TrainerSpecs(points, specs);
            }
            case 0xFA:                                    // MoneyUpdate: copper, silver, gold, mithril, platinum
            {
                long copper = r.Byte(), silver = r.Byte(), gold = r.Short(), mithril = r.Short(), platinum = r.Short();
                return new MoneyUpdate((((mithril * 1000 + platinum) * 1000 + gold) * 100 + silver) * 100 + copper);
            }
            case 0x91:                                    // CharacterPointsUpdate (190)
            {
                long rp = r.Int();
                r.Short(); r.Short(); r.Int(); r.Short(); r.Short();
                return new PointsUpdate(rp, (long)r.LongLE());
            }
        }
        return new OtherPacket(code, data.Length);
    }

    /// <summary>WriteItemData (PacketLib1124) after its slot byte: 24 zero bytes for an empty slot, else the item.</summary>
    internal static InventoryItem ReadItem(PacketReader r)
    {
        int slot = r.Byte();
        r.Short();                                        // unique id
        int level = r.Byte();
        int value1 = r.Byte();                            // a stack's count for stackable items
        r.Byte(); r.Byte(); r.Byte(); r.Byte();           // value2, hand, type, 0
        r.Short();                                        // weight
        r.Skip(5);                                        // condition, durability, quality, bonus, bonus level
        r.Short();                                        // model
        r.Byte();                                         // extension
        r.Short();                                        // colour or emblem
        int flag = r.Byte();
        if ((flag & 0x08) != 0) { r.Short(); r.Pascal(); }
        if ((flag & 0x10) != 0) { r.Short(); r.Pascal(); }
        r.Short();                                        // effect
        return new InventoryItem(slot, r.Pascal(), level, value1);
    }

    /// <summary>PacketLib1126.SendCharacterOverview: four u32 flags, then ten slots (0 for an empty one).</summary>
    internal static List<CharacterSlot> ReadOverview(PacketReader r, int realmAsked)
    {
        var list = new List<CharacterSlot>();
        r.IntLE(); r.IntLE(); r.IntLE(); r.IntLE();
        for (int slot = 0; slot < 10 && r.Left > 0; slot++)
        {
            int level = r.Byte();
            if (level == 0)
                continue;
            string name = r.IntPascal();
            r.IntLE();                                    // 0x18
            r.Byte();                                     // 1
            r.Skip(10);                                   // eye size ... mood
            r.Skip(13);
            string location = r.IntPascal();
            string className = r.IntPascal();
            string raceName = r.IntPascal();
            r.ShortLE();                                  // model
            int region = r.Byte();
            r.Byte();                                     // expansion
            r.Skip(2 * 8 + 2 * 9 + 2 * 3);                // models and colours (helmet ... distance)
            r.Skip(8);                                    // stats
            int classId = r.Byte();
            int realm = r.Byte();
            int rg = r.Byte();
            int race = ((rg >> 2) & 0x10) | (rg & 0x0F);
            r.Byte(); r.Byte();                           // weapon slots
            r.Byte(); r.Byte(); r.Byte();                 // SI, constitution, 0
            list.Add(new CharacterSlot(realm != 0 ? realm : realmAsked, slot, name, level, classId, className, race, raceName, region, location));
        }
        return list;
    }
}
