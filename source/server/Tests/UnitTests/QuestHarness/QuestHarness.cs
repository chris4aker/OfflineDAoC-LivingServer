using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Quests;
using DOL.GS.ServerProperties;
using DOL.GS.ServerRules;
using DOL.Logging;

namespace DOL.UnitTests.QuestHarness
{
    /// <summary>
    /// The quest harness (Living Server 1.2, docs/QUESTING.md): a fake player plays a data quest row from offer to
    /// finish on the server's own quest code. The player is a real GamePlayer (its own XP, money, inventory and quest
    /// lists), the NPCs and monsters are real GameNPCs, and every action goes in where the game sends it: an interact
    /// through GameNPC.Interact, a whisper through GamePlayer.Whisper, a hand-in as the move-item packet handler
    /// notifies it, a kill through the credit rule GameLiving.ProcessDeath uses (QuestKillCredit.PlayersCredited) and
    /// GamePlayer.EnemyKilled, a search where its timer ends, and the classic custom steps through ClassicQuests' own
    /// event handlers and refresh. Only the world around them is a stand-in: no regions, no network, and an in-memory
    /// database that holds the quest rows and item templates it is given and never writes anywhere.
    /// </summary>
    public sealed class QuestHarness : IDisposable
    {
        public const string CharacterId = "quest-harness-character";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

        public QuestWorld World { get; }
        public HarnessPlayer Player { get; private set; }
        public RecordingPackets Packets { get; private set; }
        public LogCapture Logs { get; }

        private readonly GameServer _previousServer;
        private readonly long _previousClock;
        private readonly PetTestLanguageScope _language;
        private readonly Dictionary<(string Name, ushort Region), HarnessNpc> _npcs = new();
        private readonly List<(DOLEvent Event, DOLEventHandler Handler)> _classicHandlers = new();
        private long _clock;
        private int _players;
        private bool _loadedTemplates;

        private static readonly PropertyInfo Clock = typeof(GameLoop).GetProperty(nameof(GameLoop.GameLoopTime));

        public QuestHarness(QuestWorld world, ClassicQuests.Config classic = null)
        {
            World = world ?? new QuestWorld();
            _previousServer = GameServer.Instance;
            _previousClock = GameLoop.GameLoopTime;
            _language = new PetTestLanguageScope();
            var server = (HarnessServer)RuntimeHelpers.GetUninitializedObject(typeof(HarnessServer));
            server.Database = DispatchProxy.Create<IObjectDatabase, QuestWorldDatabase>();
            ((QuestWorldDatabase)(object)server.Database).World = World;
            GameServer.LoadTestDouble(server);
            // After the server double: reading GameObject's logger runs its static constructor, which reads the
            // database's data quest rows.
            Logs = new LogCapture();
            // The NPC templates the classic event monsters are made from (NpcTemplateMgr reads them at server start).
            if (World.NpcTemplates.Count > 0)
            {
                NpcTemplateMgr.Init();
                _loadedTemplates = true;
            }
            ClassicQuests.Use(classic ?? new ClassicQuests.Config());
            _clock = 1_000_000;
            Clock.SetValue(null, _clock);
            RegisterClassicHandlers();
        }

        public void Dispose()
        {
            foreach (var (e, handler) in _classicHandlers)
                GameEventMgr.RemoveHandler(e, handler);
            ClassicQuests.Use(new ClassicQuests.Config());
            if (_loadedTemplates)
            {
                foreach (string table in new[] { "m_mobTemplates", "m_mobTemplatesByName" })
                    ((System.Collections.Hashtable)typeof(NpcTemplateMgr).GetField(table, HiddenStatic).GetValue(null)).Clear();
            }
            Clock.SetValue(null, _previousClock);
            GameServer.LoadTestDouble(_previousServer);
            _language.Dispose();
            Logs.Dispose();
        }

        /// <summary>The game loop's clock moves on between actions, as it does between a player's clicks.</summary>
        public void Tick(long milliseconds = 3_000)
        {
            _clock += milliseconds;
            Clock.SetValue(null, _clock);
        }

        // ClassicQuests' handlers, registered as the server registers them at start (OnServerStarted), without its timer.
        private void RegisterClassicHandlers()
        {
            void Add(DOLEvent e, string method)
            {
                MethodInfo info = typeof(ClassicQuests).GetMethod(method, HiddenStatic)
                    ?? throw new MissingMethodException(nameof(ClassicQuests), method);
                var handler = (DOLEventHandler)Delegate.CreateDelegate(typeof(DOLEventHandler), info);
                GameEventMgr.AddHandler(e, handler);
                _classicHandlers.Add((e, handler));
            }

            Add(GamePlayerEvent.InteractWith, "OnInteract");
            Add(GamePlayerEvent.Whisper, "OnWhisper");
            Add(GameLivingEvent.Say, "OnSay");
            Add(GamePlayerEvent.UseSlot, "OnUseSlot");
            Add(GameLivingEvent.Dying, "OnDying");
            Add(GamePlayerEvent.GiveItem, "OnGiveItem");
        }

        // The player

        /// <summary>A new player for the harness: a real GamePlayer on a fake connection, at the start of its level.</summary>
        public HarnessPlayer NewPlayer(int classId, byte level, short race = 0, eRealm realm = eRealm.Albion, string name = "Harness", bool primary = true)
        {
            Packets = DispatchProxy.Create<IPacketLib, RecordingPackets>() as RecordingPackets;
            var packets = (IPacketLib)(object)Packets;
            var client = (GameClient)RuntimeHelpers.GetUninitializedObject(typeof(GameClient));
            typeof(GameClient).GetField("<Account>k__BackingField", Hidden)
                .SetValue(client, new DbAccount { Name = "harness", Language = "EN", PrivLevel = 1 });
            client.Out = packets;
            var character = new DbCoreCharacter
            {
                Name = name, Level = level, Class = classId, Realm = (byte)realm, Race = race, AccountName = "harness",
                GainXP = true, GainRP = true,
            };
            // Every player is a new character, so a quest walked twice starts from nothing each time.
            typeof(DataObject).GetProperty(nameof(DataObject.ObjectId)).SetValue(character, $"{CharacterId}-{name}-{++_players}");
            var player = new HarnessPlayer(client, character, realm);
            player.SetCharacterClass(classId);
            player.InternalID = character.ObjectId;
            player.Experience = player.ExperienceForCurrentLevel;
            player.ObjectState = GameObject.eObjectState.Active;
            typeof(GameClient).GetField("<Player>k__BackingField", Hidden)?.SetValue(client, player);
            if (primary || Player == null)
            {
                Player = player;
                _npcs.Clear();
            }
            return player;
        }

        // NPCs and monsters

        /// <summary>The NPC of that name in that region (region 0: the player's), made on first use.</summary>
        public HarnessNpc Npc(string name, ushort region = 0, eRealm realm = eRealm.Albion)
        {
            ushort at = region == 0 ? Player.CurrentRegionID : region;
            if (_npcs.TryGetValue((name.ToLowerInvariant(), at), out HarnessNpc known))
                return known;
            HarnessNpc npc = HarnessNpc.Make(name, at, realm);
            npc.X = Player.X + 50;
            npc.Y = Player.Y;
            _npcs[(name.ToLowerInvariant(), at)] = npc;
            return npc;
        }

        /// <summary>A giver offering this row's quest, as GameObject.LoadDataQuests gives it one.</summary>
        public HarnessNpc Giver(DbDataQuest row, ushort region = 0)
        {
            HarnessNpc giver = Npc(row.StartName, region != 0 ? region : row.StartRegionID == 0 ? Player.CurrentRegionID : row.StartRegionID, Player.Realm);
            if (!giver.DataQuestList.Any(q => q.ID == row.ID))
                giver.AddDataQuest(new DataQuest(row, giver));
            return giver;
        }

        /// <summary>A fresh monster of that name (every kill is a new monster object, as a respawn is).</summary>
        public HarnessNpc Monster(string name, ushort region = 0, byte level = 1)
        {
            HarnessNpc mob = HarnessNpc.Make(name, region == 0 ? Player.CurrentRegionID : region, eRealm.None);
            mob.Level = level;
            mob.X = Player.X + 100;
            mob.Y = Player.Y;
            return mob;
        }

        // Actions, each where the game sends it

        public bool Interact(GameNPC npc)
        {
            Tick();
            WalkTo(npc);
            return npc.Interact(Player);
        }

        public bool Whisper(GameNPC npc, string text)
        {
            Tick();
            WalkTo(npc);
            return Player.Whisper(npc, text);
        }

        /// <summary>The player walks up to the NPC first, as a player does to talk to it or hand it something.</summary>
        public void WalkTo(GameObject npc)
        {
            if (Player.CurrentRegionID == npc.CurrentRegionID && Player.IsWithinRadius(npc, 100))
                return;
            MoveTo(npc.CurrentRegionID, npc.X - 50, npc.Y, npc.Z);
        }

        public void Say(string text)
        {
            Tick();
            Player.Notify(GameLivingEvent.Say, Player, new SayEventArgs(text));
        }

        /// <summary>A hand-in: PlayerMoveItemRequestHandler's notify, then the NPC's ReceiveItem if nothing took it.</summary>
        public bool Give(GameNPC npc, DbInventoryItem item)
        {
            Tick();
            WalkTo(npc);
            Player.Notify(GamePlayerEvent.GiveItem, Player, new GiveItemEventArgs(Player, npc, item));
            if (item.OwnerID == null)
                return true;
            npc.ReceiveItem(Player, item);
            return item.OwnerID == null;
        }

        /// <summary>Uses an item from the backpack (the classic use_item steps), as the use-slot packet notifies it.</summary>
        public void Use(DbInventoryItem item)
        {
            Tick();
            Player.Notify(GamePlayerEvent.UseSlot, Player, new UseSlotEventArgs(item.SlotPosition, 0));
        }

        /// <summary>
        /// A kill of <paramref name="monster"/> by these attackers, from the moment GameLiving.ProcessDeath works out
        /// whom it credits: the same rule (QuestKillCredit.PlayersCredited), every credited player's EnemyKilled, the
        /// monster's own quests told it is dying, and the Dying event (ClassicQuests' step drops).
        /// </summary>
        public List<GamePlayer> Kill(HarnessNpc monster, params GameLiving[] attackers)
        {
            Tick();
            // The player stands by the kill, as in the game: a gamebot's kill credits its leader only in the kill's
            // region and range (QuestKillCredit.GroupLeaderFor), and the step's target may stand in another region.
            WalkTo(monster);
            monster.SetAttackers(attackers);
            var told = new List<GameLiving>();
            AttackerTracker tracker = monster.attackComponent.AttackerTracker;
            List<GamePlayer> credited = QuestKillCredit.PlayersCredited(monster, tracker.Attackers, tracker.ContainsAttacker, told);
            foreach (GamePlayer player in credited)
                player.EnemyKilled(monster);
            GameObject killer = attackers.FirstOrDefault();
            foreach (DataQuest quest in monster.DataQuestList.ToArray())
                quest.Notify(GameLivingEvent.Dying, monster, new DyingEventArgs(killer, credited));
            monster.Notify(GameLivingEvent.Dying, monster, new DyingEventArgs(killer, credited));
            return credited;
        }

        /// <summary>The end of a /search where the player stands in the step's search area (AbstractQuest's timer).</summary>
        public void SearchDone(DataQuest quest)
        {
            Tick();
            typeof(AbstractQuest).GetMethod("QuestCommandCompleted", Hidden)
                .Invoke(quest, new object[] { AbstractQuest.eQuestCommand.SEARCH, Player });
        }

        /// <summary>One run of ClassicQuests' 5-second refresh for the player, on the game loop as the server runs it.</summary>
        public void ClassicRefresh()
        {
            Tick(5_000);
            ClassicQuests.RefreshPlayer(Player);
        }

        public void MoveTo(ushort region, int x, int y, int z = 0)
        {
            Player.TestRegion = region;
            Player.X = x;
            Player.Y = y;
            Player.Z = z;
        }

        // What the player holds

        public DataQuest Active(int questId) => Player.QuestList.Keys.OfType<DataQuest>().FirstOrDefault(q => q.ID == questId);

        public bool Finished(int questId) => Player.GetFinishedQuests().OfType<DataQuest>().Any(q => q.ID == questId) && Active(questId) == null;

        public DbInventoryItem Carried(string id) => string.IsNullOrEmpty(id) ? null
            : Player.Inventory.GetFirstItemByID(id, eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);

        public IEnumerable<string> CarriedIds() => Player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
            .Select(i => i.Id_nb);

        /// <summary>Puts copies of an item template into the backpack, as if bought or looted (collect steps).</summary>
        public DbInventoryItem Hand(string id, int count = 1)
        {
            DbItemTemplate template = World.Item(id);
            if (template == null)
                return null;
            DbInventoryItem first = null;
            for (int i = 0; i < Math.Max(1, count); i++)
            {
                DbInventoryItem item = GameInventoryItem.Create(template);
                if (!Player.Inventory.AddItem(eInventorySlot.FirstEmptyBackpack, item))
                    return first;
                first ??= item;
            }
            return first;
        }
    }

    /// <summary>
    /// Makes ClientService list only these players while in use (ClassicQuests' refresh walks the players online).
    /// </summary>
    public sealed class OnlinePlayers : IDisposable
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly FieldInfo Clients = typeof(ClientService).GetField("_clients", Hidden);
        private static readonly FieldInfo LastIndex = typeof(ClientService).GetField("_lastValidIndex", Hidden);
        private static readonly FieldInfo State = typeof(GameClient).GetField("<ClientState>k__BackingField", Hidden);
        private readonly object _clients;
        private readonly object _last;
        private readonly List<(GameClient Client, object State)> _states = new();

        private OnlinePlayers(GamePlayer[] players)
        {
            _clients = Clients.GetValue(ClientService.Instance);
            _last = LastIndex.GetValue(ClientService.Instance);
            var list = new List<GameClient>();
            foreach (GamePlayer player in players)
            {
                _states.Add((player.Client, State.GetValue(player.Client)));
                State.SetValue(player.Client, GameClient.eClientState.Playing);
                list.Add(player.Client);
            }
            Clients.SetValue(ClientService.Instance, list);
            LastIndex.SetValue(ClientService.Instance, list.Count - 1);
        }

        public static OnlinePlayers Only(params GamePlayer[] players) => new(players);

        public void Dispose()
        {
            Clients.SetValue(ClientService.Instance, _clients);
            LastIndex.SetValue(ClientService.Instance, _last);
            foreach (var (client, state) in _states)
                State.SetValue(client, state);
        }
    }

    /// <summary>The quest rows and item templates the harness's database holds. Nothing in it is ever written out.</summary>
    public sealed class QuestWorld
    {
        public Dictionary<int, DbDataQuest> Quests { get; } = new();
        // Item templates are looked up without case, as the server's pre-cache does (DataTableHandler).
        public Dictionary<string, DbItemTemplate> Items { get; } = new(StringComparer.OrdinalIgnoreCase);
        public List<DbCharacterXDataQuest> CharacterQuests { get; } = new();
        public List<DbNpcTemplate> NpcTemplates { get; } = new();
        /// <summary>Writes the quest code asked of tables that are world data (DataQuest, ItemTemplate): always none.</summary>
        public List<string> WorldWrites { get; } = new();
        private int _nextId = 1;

        public QuestWorld Add(DbDataQuest row)
        {
            Quests[row.ID] = row;
            return this;
        }

        public QuestWorld Add(params DbItemTemplate[] items)
        {
            foreach (DbItemTemplate item in items)
                Items[item.Id_nb] = item;
            return this;
        }

        public DbItemTemplate Item(string id) => string.IsNullOrEmpty(id) ? null : Items.GetValueOrDefault(id.Trim());

        public static DbItemTemplate Template(string id, string name = null) => new()
        {
            Id_nb = id, Name = name ?? id, MaxCount = 1, PackSize = 1, Weight = 1, Item_Type = (int)eInventorySlot.FirstBackpack,
            Object_Type = (int)eObjectType.GenericItem, IsDropable = true, IsTradable = true, IsPickable = true,
        };

        internal int NextId() => _nextId++;

        /// <summary>A quest row as a database read hands it out: a new object each time, so the server's changes to
        /// what it read (EpicChainLinks links rows in memory) never reach the world's own rows.</summary>
        public static DbDataQuest Copy(DbDataQuest row)
        {
            if (row == null)
                return null;
            var copy = new DbDataQuest();
            foreach (PropertyInfo property in typeof(DbDataQuest).GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.CanRead && property.CanWrite && property.GetIndexParameters().Length == 0 && property.DeclaringType == typeof(DbDataQuest))
                    property.SetValue(copy, property.GetValue(row));
            }
            copy.IsPersisted = true;
            copy.Dirty = false;
            return copy;
        }

        internal DbCharacterXDataQuest Copy(DbCharacterXDataQuest stored)
        {
            // A real database hands out a new object on every read; the quest code relies on nothing else.
            var copy = new DbCharacterXDataQuest(stored.Character_ID, stored.DataQuestID) { Step = stored.Step, Count = stored.Count };
            copy.ID = stored.ID;
            copy.IsPersisted = true;
            copy.Dirty = false;
            return copy;
        }
    }

    /// <summary>The harness's in-memory database (an IObjectDatabase stand-in).</summary>
    public class QuestWorldDatabase : DispatchProxy
    {
        public QuestWorld World;

        protected override object Invoke(MethodInfo method, object[] args)
        {
            Type type = method.IsGenericMethod ? method.GetGenericArguments()[0] : null;
            switch (method.Name)
            {
                case nameof(IObjectDatabase.FindObjectByKey):
                    if (type == typeof(DbItemTemplate))
                        return World.Item(args[0] as string);
                    if (type == typeof(DbDataQuest))
                        return QuestWorld.Copy(World.Quests.GetValueOrDefault(Convert.ToInt32(args[0])));
                    if (type == typeof(DbCharacterXDataQuest))
                    {
                        int id = Convert.ToInt32(args[0]);
                        DbCharacterXDataQuest stored = World.CharacterQuests.FirstOrDefault(c => c.ID == id);
                        return stored == null ? null : World.Copy(stored);
                    }
                    return null;
                case nameof(IObjectDatabase.SelectObject):
                    if (type == typeof(DbCharacterXDataQuest))
                    {
                        // DataQuest.GetCharacterQuest: Character_ID = @a AND DataQuestID = @b.
                        object[] values = ((WhereClause)args[0]).Parameters.Select(p => p.Value).ToArray();
                        string character = values.OfType<string>().FirstOrDefault();
                        int quest = values.Where(v => v is int or long or short).Select(Convert.ToInt32).FirstOrDefault();
                        DbCharacterXDataQuest stored = World.CharacterQuests.FirstOrDefault(c => c.Character_ID == character && c.DataQuestID == quest);
                        return stored == null ? null : World.Copy(stored);
                    }
                    return null;
                case nameof(IObjectDatabase.SelectObjects):
                case nameof(IObjectDatabase.SelectAllObjects):
                    if (type == typeof(DbDataQuest))
                        return World.Quests.Values.Select(QuestWorld.Copy).ToList();
                    if (type == typeof(DbNpcTemplate))
                        return World.NpcTemplates.ToList();
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(type));
                case nameof(IObjectDatabase.MultipleSelectObjects):
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(typeof(IList<>).MakeGenericType(type)));
                case nameof(IObjectDatabase.AddObject):
                case nameof(IObjectDatabase.SaveObject):
                    foreach (DataObject obj in Objects(args[0]))
                        Write(method.Name, obj);
                    return true;
                case nameof(IObjectDatabase.DeleteObject):
                    foreach (DataObject obj in Objects(args[0]))
                    {
                        if (obj is DbCharacterXDataQuest gone)
                            World.CharacterQuests.RemoveAll(c => c.ID == gone.ID);
                        else if (obj is DbDataQuest or DbItemTemplate)
                            World.WorldWrites.Add($"{method.Name} {obj.GetType().Name}");
                        obj.IsDeleted = true;
                    }
                    return true;
            }
            if (method.ReturnType == typeof(bool)) return true;
            if (method.ReturnType == typeof(void)) return null;
            return method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }

        private static IEnumerable<DataObject> Objects(object arg) => arg switch
        {
            DataObject one => new[] { one },
            IEnumerable<DataObject> many => many,
            _ => Array.Empty<DataObject>(),
        };

        private void Write(string how, DataObject obj)
        {
            if (obj is DbDataQuest or DbItemTemplate)
            {
                World.WorldWrites.Add($"{how} {obj.GetType().Name}");
                return;
            }
            if (obj is not DbCharacterXDataQuest record)
            {
                obj.IsPersisted = true;
                obj.Dirty = false;
                return;
            }
            DbCharacterXDataQuest stored = record.IsPersisted ? World.CharacterQuests.FirstOrDefault(c => c.ID == record.ID) : null;
            if (stored == null)
            {
                record.ID = World.NextId();
                stored = new DbCharacterXDataQuest(record.Character_ID, record.DataQuestID) { ID = record.ID };
                World.CharacterQuests.Add(stored);
            }
            stored.Step = record.Step;
            stored.Count = record.Count;
            record.IsPersisted = true;
            record.Dirty = false;
        }
    }

    public sealed class HarnessServer : GameServer
    {
        public IObjectDatabase Database;
        protected override IObjectDatabase DataBaseImpl => Database;
        protected override IServerRules ServerRulesImpl => new NormalServerRules();
    }

    /// <summary>The packets the player's client would get: messages, quest markers, map markers, windows.</summary>
    public class RecordingPackets : DispatchProxy
    {
        public readonly List<string> Messages = new();
        public readonly List<(GameNPC Npc, eQuestIndicator Indicator)> QuestEffects = new();
        public readonly List<(uint Id, ushort Region, int X, int Y)> MapMarkers = new();
        public readonly List<uint> MapMarkersRemoved = new();
        public int OfferWindows;
        public int RewardWindows;

        protected override object Invoke(MethodInfo method, object[] args)
        {
            switch (method.Name)
            {
                case nameof(IPacketLib.SendMessage) when args.Length > 0 && args[0] is string message:
                    Messages.Add(message);
                    break;
                case nameof(IPacketLib.SendNPCsQuestEffect):
                    QuestEffects.Add(((GameNPC)args[0], (eQuestIndicator)args[1]));
                    break;
                case nameof(IPacketLib.SendMinotaurRelicMapUpdate):
                    MapMarkers.Add(((uint)args[0], (ushort)args[1], (int)args[2], (int)args[3]));
                    break;
                case nameof(IPacketLib.SendMinotaurRelicMapRemove):
                    MapMarkersRemoved.Add((uint)args[0]);
                    break;
                case nameof(IPacketLib.SendQuestOfferWindow):
                    OfferWindows++;
                    break;
                case nameof(IPacketLib.SendQuestRewardWindow):
                    RewardWindows++;
                    break;
            }
            return method.ReturnType != typeof(void) && method.ReturnType.IsValueType ? Activator.CreateInstance(method.ReturnType) : null;
        }
    }

    /// <summary>A real GamePlayer on a fake connection. Only position, realm and the property calculators are its own.</summary>
    public sealed class HarnessPlayer : GamePlayer
    {
        public ushort TestRegion = 1;
        private int _x = 10_000, _y = 10_000, _z;
        private readonly eRealm _realm;
        public readonly List<long> MoneyAwards = new();
        public readonly List<long> RealmPointAwards = new();
        public readonly List<long> BountyPointAwards = new();
        public readonly List<long> ChampionAwards = new();

        public HarnessPlayer(GameClient client, DbCoreCharacter character, eRealm realm) : base(client, character)
        {
            _realm = realm;
        }

        public override ushort CurrentRegionID { get => TestRegion; set => TestRegion = value; }
        public override int X { get => _x; set => _x = value; }
        public override int Y { get => _y; set => _y = value; }
        public override int Z { get => _z; set => _z = value; }
        public override eRealm Realm { get => _realm; set { } }
        // Alive while in the world: the harness has no health calculators (MaxHealth reads them).
        public override bool IsAlive => ObjectState == eObjectState.Active;

        // The harness has no property calculators (the server sets them up at start); only encumbrance asks here.
        public override int GetModified(eProperty property) => 0;
        public override int GetModifiedBase(eProperty property) => 0;

        public override void AddMoney(long money, string messageFormat, eChatType ct, eChatLoc cl)
        {
            MoneyAwards.Add(money);
            base.AddMoney(money, messageFormat, ct, cl);
        }

        public override void GainRealmPoints(long amount, bool modify, bool sendMessage, bool notify)
        {
            RealmPointAwards.Add(amount);
        }

        public override void GainBountyPoints(long amount, bool modify, bool sendMessage, bool notify)
        {
            BountyPointAwards.Add(amount);
        }

        public override void GainChampionExperience(long experience, eXPSource source)
        {
            ChampionAwards.Add(experience);
        }
    }

    /// <summary>A real GameNPC standing in a region of its own number (no world behind it).</summary>
    public sealed class HarnessNpc : GameNPC
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private ushort _region;
        private int _x, _y, _z;
        private eRealm _realm;
        private string _name;
        private byte _level = 1;
        public readonly List<string> Said = new();

        private HarnessNpc() { }

        public static HarnessNpc Make(string name, ushort region, eRealm realm)
        {
            var npc = (HarnessNpc)RuntimeHelpers.GetUninitializedObject(typeof(HarnessNpc));
            Fields.Fill(npc);
            npc._name = name;
            npc._region = region;
            npc._realm = realm;
            npc.ObjectState = eObjectState.Active;
            typeof(GameObject).GetField("m_health", Hidden)?.SetValue(npc, 100);
            var attack = (AttackComponent)RuntimeHelpers.GetUninitializedObject(typeof(AttackComponent));
            var tracker = (AttackerTracker)RuntimeHelpers.GetUninitializedObject(typeof(AttackerTracker));
            Fields.Fill(tracker);
            typeof(AttackComponent).GetField("<AttackerTracker>k__BackingField", Hidden).SetValue(attack, tracker);
            npc.attackComponent = attack;
            return npc;
        }

        /// <summary>The monster's attackers (AttackerTracker), set directly: the harness has no combat.</summary>
        public void SetAttackers(IEnumerable<GameLiving> attackers)
        {
            AttackerTracker tracker = attackComponent.AttackerTracker;
            var map = (IDictionary)typeof(AttackerTracker).GetField("_attackers", Hidden).GetValue(tracker);
            map.Clear();
            Type info = typeof(AttackerTracker).GetNestedType("AttackerInfo", BindingFlags.NonPublic | BindingFlags.Public);
            foreach (GameLiving attacker in attackers)
                map[attacker] = Activator.CreateInstance(info, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new object[] { true, long.MaxValue }, null);
        }

        public override string Name { get => _name; set => _name = value; }
        public override ushort CurrentRegionID { get => _region; set => _region = value; }
        public override int X { get => _x; set => _x = value; }
        public override int Y { get => _y; set => _y = value; }
        public override int Z { get => _z; set => _z = value; }
        public override eRealm Realm { get => _realm; set => _realm = value; }
        public override byte Level { get => _level; set => _level = value; }
        public override bool IsAlive => ObjectState == eObjectState.Active;
        public override void TurnTo(GameObject target, int duration = 0) { }
        public override void TurnTo(int x, int y, int duration = 0) { }
        public override void TurnTo(ushort heading, int duration = 0) { }
        public override void SayTo(GamePlayer target, eChatLoc loc, string message, bool announce = true) => Said.Add(message);
        public override void SayTo(GamePlayer target, string message, bool announce = true) => Said.Add(message);
        public override void FireAmbientSentence(eAmbientTrigger trigger, GameObject living) { }
    }

    /// <summary>An autonomous gamebot (no owner) for "a kill by a gamebot in the group".</summary>
    public sealed class HarnessBot : GameBot
    {
        private HarnessBot() : base((OfflineWorldBotRecord)null) { }
        private ushort _region;
        private int _x, _y;

        public static HarnessBot Make(GamePlayer leader)
        {
            var bot = (HarnessBot)RuntimeHelpers.GetUninitializedObject(typeof(HarnessBot));
            Fields.Fill(bot);
            bot._region = leader.CurrentRegionID;
            bot._x = leader.X;
            bot._y = leader.Y;
            bot.ObjectState = eObjectState.Active;
            return bot;
        }

        public override string Name { get => "Gamebot"; set { } }
        public override ushort CurrentRegionID { get => _region; set => _region = value; }
        public override int X { get => _x; set => _x = value; }
        public override int Y { get => _y; set => _y = value; }
        public override int Z { get => 0; set { } }
        public override eRealm Realm { get => eRealm.Albion; set { } }
    }

    /// <summary>Groups for the harness: a real Group with its member list filled in.</summary>
    public static class HarnessGroups
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        public static Group Of(GamePlayer leader, params GameLiving[] others)
        {
            var group = new Group(leader);
            var members = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden).GetValue(group);
            foreach (GameLiving member in new[] { (GameLiving)leader }.Concat(others))
            {
                member.Group = group;
                members.Add(member);
                if (member is GameBot bot && !bot.EnterPlayerLedGroup(leader))
                    throw new InvalidOperationException("the gamebot did not join the player's group");
            }
            return group;
        }
    }

    /// <summary>Collections and locks for objects made without their constructor (their field initializers never ran).</summary>
    internal static class Fields
    {
        public static void Fill(object target)
        {
            for (Type type = target.GetType(); type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (field.GetValue(target) != null || field.FieldType.IsValueType)
                        continue;
                    object value = Make(field.FieldType);
                    if (value != null)
                        field.SetValue(target, value);
                }
            }
        }

        private static object Make(Type type)
        {
            if (type == typeof(object))
                return new object();
            if (type == typeof(System.Threading.Lock))
                return new System.Threading.Lock();
            if (type == typeof(PropertyCollection))
                return new PropertyCollection();
            if (type.IsAbstract || type.IsInterface || type.GetConstructor(Type.EmptyTypes) == null)
                return null;
            string space = type.Namespace ?? string.Empty;
            return space.StartsWith("System.Collections", StringComparison.Ordinal) ? Activator.CreateInstance(type) : null;
        }
    }

    /// <summary>
    /// The server's log lines while the harness runs. Loggers made before logging is set up have no queue, and an
    /// error logged on one throws; the harness gives the quest code's loggers a queue of its own and keeps what they
    /// say, so a quest that fails says why.
    /// </summary>
    public sealed class LogCapture : IDisposable
    {
        private static readonly FieldInfo QueueField = typeof(Logger).GetField("_queueProcessor", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo CollectionField = typeof(LogEntryQueueProcessor).GetField("_loggingQueue", BindingFlags.Instance | BindingFlags.NonPublic);
        private readonly System.Collections.Concurrent.BlockingCollection<LogEntry> _entries = new(new ConcurrentQueue<LogEntry>());
        private readonly LogEntryQueueProcessor _processor = new();
        private readonly List<(Logger Logger, object Previous)> _swapped = new();

        // The types whose loggers the quest code reaches.
        private static readonly Type[] Types =
        {
            typeof(DataQuest), typeof(AbstractQuest), typeof(ClassicQuests), typeof(GameObject), typeof(GameLiving),
            typeof(GameNPC), typeof(GamePlayer), typeof(GameLivingInventory), typeof(GamePlayerInventory),
            typeof(GameInventoryItem), typeof(QuestIndicators), typeof(InventoryLogging), typeof(ScriptMgr), typeof(GameBot), typeof(NpcTemplateMgr),
            typeof(Group), typeof(QuestMgr), typeof(GameEventMgr), typeof(GameServer), typeof(DOLEventHandlerChain), typeof(DOLEventHandlerCollection),
        };

        public LogCapture()
        {
            CollectionField.SetValue(_processor, _entries);
            foreach (Type type in Types)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    if (field.GetValue(null) is not Logger logger)
                        continue;
                    _swapped.Add((logger, QueueField.GetValue(logger)));
                    QueueField.SetValue(logger, _processor);
                }
            }
        }

        /// <summary>The error lines logged since the last call (warnings and errors), drained.</summary>
        public List<string> TakeErrors()
        {
            var lines = new List<string>();
            while (_entries.TryTake(out LogEntry entry))
            {
                if (entry.Level is ELogLevel.Error or ELogLevel.Fatal or ELogLevel.Warning)
                    lines.Add(Format(entry));
            }
            return lines;
        }

        private static string Format(LogEntry entry)
        {
            string message = entry.Message ?? string.Empty;
            if (entry.Args is { Length: > 0 })
            {
                try { message = string.Format(message, entry.Args); } catch (FormatException) { }
            }
            if (entry.Exception != null)
                message += " (" + entry.Exception.GetType().Name + ": " + entry.Exception.Message + ")";
            return message;
        }

        public void Dispose()
        {
            foreach (var (logger, previous) in _swapped)
                QueueField.SetValue(logger, previous);
        }
    }
}
