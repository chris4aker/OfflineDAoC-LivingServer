using System;
using DOL.Database;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using DOL.GS.GameEvents;
using DOL.GS.PacketHandler;
using DOL.GS.Realm;

// From shadowofze's True 1.65 v0.7 (f68cc75), via True165 (github.com/zomboden/OfflineDAoC-True165), GPL-3.0. The
// protocol below is kept byte for byte, so either client's True 1.65 hire window can draw it. Living Server changes: the
// window opens with /hire beside a healer of a capital or major town (there are no traveler's inns yet), names come from
// CompanionNames165, and the preview figure stands in front of the player (True 1.65's inn preview spots are not here).

namespace DOL.GS
{
    /// <summary>
    /// The companion hiring window (True 1.65, owner 2026-10-30, plan sections 17 and 17g): the server side of the window
    /// both clients draw (True 1.65: at a traveler's inn; Living Server: /hire beside a healer). Layout shared by the original client's overlay (tools\hire_spec.py) and the
    /// new client (TRUE 1.65 MODE HIRE WINDOW PROTOCOL.md). DebugMode 0x21, fixed 128-byte body: [0]=0, [1]=0x48 'H', [2]=1 (version), [3]=op.
    ///   op 1 open:    [4] mode (0 innkeeper, 1 healer), [5] slots used, [6] slots open, [7] next slot level (0 = all open)
    ///   op 2 close
    ///   op 3 text:    [4] slot 0..47, [5..126] ASCII, NUL-terminated, [127] = 0. Slots: 0 title, 1 race, 2 class,
    ///                 3 gender, 4 "Look n of N", 5 "Hair colour n of N" (empty when the look has none), 6 name, 7 cost line, 8 problem line, 9 slots line, 10-12 blurb lines;
    ///                 roster row r (0..7): 16+3r name, 17+3r "Class level", 18+3r "WITH YOU" / "AT THE INN" (Living Server: "WAITING") / "DEAD"
    ///   op 4 numbers (new client): [4] kind 0 = hire page: [5] race, [6] gender (0 male, 1 female), [7] class,
    ///                 [8] look, [9] look count, [10..11] 0, [12] hair colour (0-based, the look's hair set in the
    ///                 period hair.csv), [13] hair colour count,
    ///                 [14..15] model, [16..19] cost in copper, [20] hire enabled, [21] height (1 Short, 2 Average,
    ///                 3 Tall), [22] height count (3);
    ///                 kind 1 = roster row: [5] row, [6] state (0 at the inn, 1 with you, 2 dead), [7] level, [8] class,
    ///                 [9] race, [10] gender, [11] buttons (1 summon, 2 dismiss, 4 release, 8 revive), [12..13] model
    ///                 (the chosen classic look), [14..15] object id when summoned, [16..17] companion id, [18..19] XP
    ///                 per mille, [20] hair colour; all little-endian
    ///   op 5 slots:   [4] used, [5] open, [6] total (7), [7] next slot level
    ///   op 6 preview: [4..5] preview NPC object id (0 = none)
    /// Client to server: /companionui race- race+ class- class+ gender look- look+ (face-/face+ = the same) hair- hair+ (hair
    /// colour) height- height+ (Short / Average / Tall) look (random look) name hire, tab N,
    /// name &lt;text&gt;, summon|dismiss|release|revive &lt;row 0..7&gt;, ownpreview (the client draws the preview itself: no
    /// world figure for this player from then on). GM: /companionui test opens it with sample data.
    /// The hiring rules themselves (slots, costs, the hired bot) come next; until then "hire" says so.
    /// </summary>
    public static class Hire165
    {
        public const byte Marker = 0x48;
        /// <summary>The hair colours of the chosen look (its hair set in the period hair.csv; 0 or 1 = no choice).</summary>
        public static int HairCount(Design d) => ClassicFigures165.HairColourCount(Model(d));

        // Classic figures (True 1.65 Mode): each face + hair style is its own model (ClassicFigures165, from the client's
        // fig3tofigmain.csv), so the counts depend on race and sex and the preview shows the exact look.
        // One LOOK picker (owner 2026-10-30: "merge the buttons into one option"): every classic look of the race and sex,
        // each a fixed face + hair (ClassicFigures165.Looks). Design.Face holds the look index; hair and hair colour are
        // baked into each look.
        public static int LookCount(Design d) => Math.Max(1, ClassicFigures165.LookCount(d.Race, d.Gender == eGender.Female));
        public static readonly byte[] SlotLevels = [5, 10, 15, 20, 30, 40, 45];   // owner 2026-10-30

        // True 1.65's inn layouts had a hire_preview spot per realm. Living Server has no inns yet, so the preview figure
        // stands in front of the player; a later world update that adds the inns can list their spots here.
        private static readonly Dictionary<eRealm, (ushort Region, int X, int Y, int Z, ushort Heading)> PreviewSpots = new();

        private static readonly Dictionary<(eRealm, eGender), string[]> Names = new()
        {
            [(eRealm.Albion, eGender.Male)] = ["Aldric", "Bevis", "Cuthbert", "Edwin", "Gareth", "Osbert"],
            [(eRealm.Albion, eGender.Female)] = ["Aldith", "Brangwen", "Edith", "Gwendolyn", "Isolde", "Maud"],
            [(eRealm.Hibernia, eGender.Male)] = ["Aodh", "Brennan", "Colm", "Diarmuid", "Eoghan", "Lorcan"],
            [(eRealm.Hibernia, eGender.Female)] = ["Aine", "Brigid", "Caoimhe", "Deirdre", "Grainne", "Muirne"],
            [(eRealm.Midgard, eGender.Male)] = ["Arnulf", "Eirik", "Gunnar", "Hakon", "Leif", "Sven"],
            [(eRealm.Midgard, eGender.Female)] = ["Astrid", "Freydis", "Gudrun", "Ingrid", "Sigrid", "Thyra"],
        };

        public sealed class Design
        {
            public eRace Race;
            public eGender Gender = eGender.Male;
            public eCharacterClass Class;
            public int Face, Hair, HairColour;
            /// <summary>Height as at character creation: 1 Short, 2 Average, 3 Tall (owner 2026-10-31).</summary>
            public int Height = 2;
            public string Name = string.Empty;
        }

        public sealed class Session
        {
            public byte Mode;               // 0 innkeeper, 1 healer
            public bool Test;
            public int Tab;
            public readonly Design Design = new();
            public PreviewNpc Preview;
            public string PreviewKey = string.Empty;   // the look the figure was last built with (rebuilt only on a change)
            public long LastCommand = GameLoop.GameLoopTime;   // the preview fades after PreviewNpc.PreviewIdleMs without one
            public string Problem = string.Empty;
            public GameNPC Keeper;          // the innkeeper or healer the window was opened at (null: GM test)
            public int Selected = -1;       // the roster row the original client selected
        }

        private static readonly ConditionalWeakTable<GamePlayer, Session> Sessions = new();

        /// <summary>Players whose client draws the hire preview itself (the new client sends /companionui ownpreview):
        /// no figure is spawned in the world for them (owner 2026-10-30: it stood beside the window as a double).</summary>
        private static readonly ConditionalWeakTable<GamePlayer, object> OwnPreview = new();

        /// <summary>Where hiring happens in Living Server (True 1.65: at the innkeeper of a traveler's inn).</summary>
        public const string HireWhereMessage = "Hire beside a healer of your realm (every capital and large town has one): /hire there.";
        public const string SummonWhereMessage = "Summon your companions beside a healer of your realm: /hire summon <n> there.";
        public const string NotOpenMessage = "Stand beside a healer of your realm (every capital and large town has one) and type /hire to hire a companion.";

        public static Session Get(GamePlayer player) => player != null && Sessions.TryGetValue(player, out Session s) ? s : null;

        // ---- choices

        public static List<eRace> Races(eRealm realm)
        {
            var disabled = Util.SplitCSV(ServerProperties.Properties.DISABLED_RACES ?? string.Empty, true);
            return PlayerRace.AllRaces
                .Where(r => r.Realm == realm && r.Expansion is eDAoCExpansion.Classic or eDAoCExpansion.ShroudedIsles &&
                            !disabled.Contains(((int)r.ID).ToString()))
                .Select(r => r.ID).OrderBy(r => r).ToList();
        }

        /// <summary>The advanced classes a hired companion of this race can be (it is hired at level 5, past its base class).</summary>
        public static List<eCharacterClass> Classes(eRealm realm, eRace race)
        {
            var disabled = Util.SplitCSV(ServerProperties.Properties.DISABLED_CLASSES ?? string.Empty, true);
            if (!GlobalConstants.STARTING_CLASSES_DICT.TryGetValue(realm, out List<eCharacterClass> bases))
                return [];
            var baseTypes = bases.Select(b => ScriptMgr.FindCharacterClass((int)b)?.GetType()).Where(t => t != null).ToList();
            var result = new List<eCharacterClass>();
            foreach (eCharacterClass id in Enum.GetValues<eCharacterClass>())
            {
                if (disabled.Contains(((int)id).ToString()))
                    continue;
                ICharacterClass cls = ScriptMgr.FindCharacterClass((int)id);
                if (cls == null || !cls.HasAdvancedFromBaseClass() || IsCustomClass(cls.GetType()) ||
                    !baseTypes.Any(b => cls.GetType().IsSubclassOf(b)) || !cls.EligibleRaces.Exists(r => r.ID == race))
                    continue;
                if (!result.Contains(id))
                    result.Add(id);
            }
            return result.OrderBy(c => c.ToString(), StringComparer.Ordinal).ToList();
        }

        /// <summary>A custom class a companion can't be hired as: the Sluaghbinder, in every edition. True 1.65 refused it
        /// with every freeshard script; bots never play the custom class (owner 2026-11-17, BotCustomClassRule).</summary>
        public static bool IsCustomClass(Type type)
        {
            for (Type t = type; t != null; t = t.DeclaringType)
                if (t.Name.Contains("Sluaghbinder", StringComparison.Ordinal))
                    return true;
            return false;
        }

        public static ushort Model(Design d)
        {
            ushort classic = ClassicFigures165.LookModel(d.Race, d.Gender == eGender.Female, d.Face);
            return classic != 0 ? classic : (ushort)(PlayerRace.AllRaces.FirstOrDefault(r => r.ID == d.Race)?.GetModel(d.Gender) ?? 0);
        }

        /// <summary>Keeps the look inside what this race and sex has (10 to 12 player looks).</summary>
        private static void FixLook(Design d)
        {
            d.Face = Math.Clamp(d.Face, 0, LookCount(d) - 1);
            d.Hair = 0;
            d.HairColour = Math.Clamp(d.HairColour, 0, Math.Max(0, HairCount(d) - 1));
        }

        // ---- opening and closing

        public static void OpenTest(GamePlayer player) => Open(player, 0, null, test: true);

        /// <summary>Talking to an innkeeper (hiring) or, with companions, to a healer (free revive).</summary>
        public static void OpenInnkeeper(GamePlayer player, GameNPC keeper) => Open(player, 0, keeper, test: false);

        public static void OpenHealer(GamePlayer player, GameNPC healer) => Open(player, 1, healer, test: false);

        /// <summary>Right-clicking your own companion: the recruit screen on its roster row (hiring itself is only at
        /// an innkeeper).</summary>
        public static void OpenForCompanion(GamePlayer player, int row)
        {
            Open(player, 0, null, test: false);
            if (Get(player) is { } s)
            {
                s.Tab = 1;
                s.Selected = row;
                SendAll(player, s);
            }
        }

        /// <summary>A companion's name follows the player name rules (owner 2026-10-30: "same character limit with no spaces like
        /// player names"): one word of 3 to 20 letters, not worn by any player or bot. Null when it is fine.</summary>
        public static string NameProblem(string name)
        {
            if (name.Length < 3 || name.Length > 20 || !name.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
                return "A name is one word of 3 to 20 letters (A-Z), like a player's.";
            string proper = char.ToUpperInvariant(name[0]) + name[1..];
            if (GameServer.Instance?.PlayerManager?.InvalidNames[proper] == true)
                return $"The name {proper} is not allowed.";
            // the name table (offline_bot_names): any player, bot or companion, live or archived
            if (CompanionNames165.IsTaken(proper))
                return $"The name {proper} is taken.";
            return null;
        }

        /// <summary>Re-sends the window if it is open (after a hire, release, death...).</summary>
        public static void Refresh(GamePlayer player)
        {
            if (Get(player) is { } s)
                SendAll(player, s);
        }

        private static void Open(GamePlayer player, byte mode, GameNPC keeper, bool test)
        {
            Session s = Sessions.GetValue(player, _ => new Session());
            s.Test = test;
            s.Mode = mode;
            s.Keeper = keeper;
            s.Problem = string.Empty;
            List<eRace> races = Races(player.Realm);
            if (!races.Contains(s.Design.Race))
                s.Design.Race = races.FirstOrDefault();
            FixClass(player.Realm, s.Design);
            FixLook(s.Design);
            if (string.IsNullOrEmpty(s.Design.Name))
                s.Design.Name = RandomName(player.Realm, s.Design.Race, s.Design.Gender);
            int used = HiredCompanions165.RosterOf(player).Count;
            Send(player, 1, [s.Mode, (byte)used, (byte)HiredCompanions165.OpenSlots(player.Level), (byte)HiredCompanions165.NextSlotLevel(player.Level)]);
            s.LastCommand = GameLoop.GameLoopTime;
            SendAll(player, s);   // the numbers before the figure (see Handle)
            // the figure only at a hiring spot (a healer, an innkeeper or the GM test): the window opened from a companion
            // anywhere in the world shows no figure (Living Server)
            if (mode == 0 && (keeper != null || test) && !OwnPreview.TryGetValue(player, out _))
            {
                // one figure per window: a figure still standing is kept (opening again spawned a second one; the old
                // client saw the preview id alternate and the figure flicker, 2026-10-30)
                if (s.Preview is { ObjectState: GameObject.eObjectState.Active } live && live.CurrentRegionID == player.CurrentRegionID)
                    UpdatePreview(s);
                else
                    SpawnPreview(player, s);
                Send(player, 6, s.Preview == null ? [0, 0] : [(byte)s.Preview.ObjectID, (byte)(s.Preview.ObjectID >> 8)]);
            }
        }

        public static void Close(GamePlayer player)
        {
            Session s = Get(player);
            if (s == null)
                return;
            s.Preview?.Delete();
            s.Preview = null;
            Sessions.Remove(player);
            if (player.ObjectState == GameObject.eObjectState.Active)
                Send(player, 2, []);
        }

        // ---- the buttons

        public static void Handle(GamePlayer player, string[] args)
        {
            if (args.Length > 1 && args[1].Equals("protocol", StringComparison.OrdinalIgnoreCase))
            {
                // a client that draws the hire window from these messages (Living Server: none ships yet)
                SetReadsProtocol(player);
                if (Get(player) is { } open)
                    SendAll(player, open);
                return;
            }
            if (args.Length > 1 && args[1].Equals("ownpreview", StringComparison.OrdinalIgnoreCase))
            {
                // the client draws its own preview: drop the world figure now and never spawn one for this player
                OwnPreview.AddOrUpdate(player, true);
                SetReadsProtocol(player);   // only a client that reads the protocol knows this word
                if (Get(player) is { } open)
                {
                    open.Preview?.Delete();
                    open.Preview = null;
                    Send(player, 6, [0, 0]);
                }
                return;
            }
            Session s = Get(player);
            if (s == null)
            {
                player.Out.SendMessage(NotOpenMessage, eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            if (s.Keeper != null && (s.Keeper.CurrentRegionID != player.CurrentRegionID || !player.IsWithinRadius(s.Keeper, 512)))
            {
                Close(player);
                player.Out.SendMessage($"You walked away from {s.Keeper.Name}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return;
            }
            Design d = s.Design;
            s.Problem = string.Empty;
            s.LastCommand = GameLoop.GameLoopTime;
            List<eRace> races = Races(player.Realm);
            switch (verb)
            {
                case "race+": case "race-":
                    if (races.Count > 0)
                        d.Race = races[Step(races.IndexOf(d.Race), races.Count, verb.EndsWith('+'))];
                    FixClass(player.Realm, d);
                    break;
                case "class+": case "class-":
                    List<eCharacterClass> classes = Classes(player.Realm, d.Race);
                    if (classes.Count > 0)
                        d.Class = classes[Step(classes.IndexOf(d.Class), classes.Count, verb.EndsWith('+'))];
                    break;
                case "gender":
                    d.Gender = d.Gender == eGender.Male ? eGender.Female : eGender.Male;
                    FixClass(player.Realm, d);
                    d.Name = RandomName(player.Realm, d.Race, d.Gender);   // names match the companion's sex
                    break;
                case "look+": case "look-": case "face+": case "face-":
                    d.Face = Step(d.Face, LookCount(d), verb.EndsWith('+'));
                    break;
                case "height+": case "height-":   // Short / Average / Tall, as a player picks at creation (owner 2026-10-31)
                    d.Height = Step(Math.Clamp(d.Height, 1, 3) - 1, 3, verb.EndsWith('+')) + 1;
                    break;
                case "hair+": case "hair-":   // hair colour (owner 2026-10-30); the clients re-skin the hair
                    d.HairColour = HairCount(d) > 1 ? Step(d.HairColour, HairCount(d), verb.EndsWith('+')) : 0;
                    break;
                case "look":   // NEW LOOK: a random look of this race and sex
                    d.Face = LookCount(d) > 1 ? (d.Face + Random.Shared.Next(1, LookCount(d))) % LookCount(d) : 0;
                    break;
                case "name":
                    string name = args.Length > 2 ? string.Join(' ', args.Skip(2)).Trim() : string.Empty;
                    if (name.Length == 0)
                        d.Name = RandomName(player.Realm, d.Race, d.Gender);   // plain "name": a random one
                    else if (NameProblem(name) is { } bad)
                        s.Problem = bad;
                    else
                        d.Name = char.ToUpperInvariant(name[0]) + name[1..];   // as a player's: first letter capital, rest as typed
                    break;
                case "tab":
                    s.Tab = args.Length > 2 && int.TryParse(args[2], out int tab) ? Math.Clamp(tab, 0, 1) : 0;
                    break;
                case "hire":
                    s.Problem = s.Mode != 0 || s.Keeper == null && !s.Test ? HireWhereMessage
                        : HiredCompanions165.Hire(player, d) ?? string.Empty;
                    if (s.Problem.Length == 0)
                    {
                        d.Name = RandomName(player.Realm, d.Race, d.Gender);   // ready for the next one
                        s.Preview?.Delete();                            // hired: the figure goes
                        s.Preview = null;
                        FixLook(d);
                        SendAll(player, s);
                        return;
                    }
                    break;
                case "summon": case "dismiss": case "release": case "revive":
                    if (args.Length < 3 || !int.TryParse(args[2], out int row))
                    {
                        s.Problem = "Choose a companion first.";
                        break;
                    }
                    s.Problem = verb switch
                    {
                        // owner 2026-11-05: companions are summoned at the innkeeper of a traveler's inn (the window opened
                        // from a companion anywhere in the world may dismiss them, not call them back)
                        "summon" => s.Mode != 0 || s.Keeper == null && !s.Test
                            ? SummonWhereMessage
                            : HiredCompanions165.Summon(player, row),
                        "dismiss" => HiredCompanions165.Dismiss(player, row),
                        "revive" => s.Mode == 1 || s.Test ? HiredCompanions165.Revive(player, row) : "A healer brings companions back.",
                        _ => null,
                    } ?? string.Empty;
                    if (verb == "release")
                        HiredCompanions165.AskRelease(player, row);
                    break;
                case "close":
                    Close(player);
                    return;
                default:
                    s.Problem = "Unknown hiring command.";
                    break;
            }
            FixLook(d);
            // the numbers (look, hair colour) first, then the figure: the original client builds the figure's hair from
            // the last op 4 it has (tested in the original client 2026-10-30)
            SendAll(player, s);
            // a figure that faded (idle, or after a hire) comes back on the next change while the hire page is open
            if (s.Preview == null && s.Mode == 0 && (s.Keeper != null || s.Test) && !OwnPreview.TryGetValue(player, out _))
                SpawnPreview(player, s);
            else
                UpdatePreview(s);
            Send(player, 6, s.Preview == null ? [0, 0] : [(byte)s.Preview.ObjectID, (byte)(s.Preview.ObjectID >> 8)]);
        }

        private static int Step(int index, int count, bool up) => count <= 0 ? 0 : ((Math.Max(index, 0) + (up ? 1 : count - 1)) % count);

        private static void FixClass(eRealm realm, Design d)
        {
            List<eCharacterClass> classes = Classes(realm, d.Race);
            if (!classes.Contains(d.Class))
                d.Class = classes.FirstOrDefault();
        }

        // A realm-styled name nobody wears (CompanionNames165); the short list only if that finds none.
        private static string RandomName(eRealm realm, eRace race, eGender gender) =>
            CompanionNames165.Suggest(realm, gender)
            ?? (Names.TryGetValue((realm, gender), out string[] list) ? list[Util.Random(list.Length - 1)] : "Companion");

        // ---- what the window shows

        private static void SendAll(GamePlayer player, Session s)
        {
            Design d = s.Design;
            string className = d.Class == 0 ? "-" : ScriptMgr.FindCharacterClass((int)d.Class)?.Name ?? d.Class.ToString();
            string raceName = PlayerRace.AllRaces.FirstOrDefault(r => r.ID == d.Race) is { } race ? Spaced(race.ID.ToString()) : "-";
            ushort model = Model(d);
            Text(player, 0, s.Mode == 1 ? "HEALER - YOUR COMPANIONS" : "HIRE A COMPANION");
            Text(player, 1, raceName);
            Text(player, 2, className);
            Text(player, 3, d.Gender == eGender.Female ? "Female" : "Male");
            Text(player, 4, $"Look {d.Face + 1} of {LookCount(d)}");
            Text(player, 5, HairCount(d) > 1 ? $"Hair color {d.HairColour + 1} of {HairCount(d)}" : string.Empty);
            Text(player, 6, d.Name);
            var roster = HiredCompanions165.RosterOf(player);
            int open = HiredCompanions165.OpenSlots(player.Level), next = HiredCompanions165.NextSlotLevel(player.Level);
            long cost = HiredCompanions165.NextCost(player);
            Text(player, 7, cost == 0 ? "Cost: free (your first companion)" : $"Cost: {Money.GetString(cost)}");
            Text(player, 8, s.Problem);
            Text(player, 9, $"Companions: {roster.Count} of {open} slots" + (next > 0 ? $" (next at level {next})" : string.Empty));
            Text(player, 10, $"A level 5 {className} who joins you with a training");
            Text(player, 11, "weapon and nothing else. Give them gear and train");
            Text(player, 12, s.Test ? "(GM test window)" : "their specs from the GEAR and SPEC buttons.");
            for (int r = 0; r < 8; r++)
            {
                OfflineHiredCompanionRecord hired = roster.FirstOrDefault(h => h.Row == r);
                if (hired == null || HiredCompanions165.Record(hired.BotId) is not { } rec)
                {
                    Text(player, 16 + 3 * r, r < HiredCompanions165.OpenSlots(player.Level) ? "(empty)" : "(locked)");   // the old client can't hide a row
                    Text(player, 17 + 3 * r, string.Empty);
                    Text(player, 18 + 3 * r, string.Empty);
                    continue;
                }
                GameBot live = HiredCompanions165.LiveBot(hired.BotId);
                bool dead = live != null ? !live.IsAlive : !rec.IsAlive;
                byte state = dead ? (byte)2 : live != null ? (byte)1 : (byte)0;
                byte level = (byte)(live?.Level ?? rec.Level);
                byte buttons = (byte)(dead ? 8 | 4 : live != null ? 2 | 4 : 1 | 4);
                RosterRow(player, r, (s.Selected == r ? "> " : string.Empty) + rec.Name, $"{rec.ClassName} {level}",
                    dead ? "DEAD" : live != null ? "WITH YOU" : "WAITING",
                    state, level, (eCharacterClass)rec.ClassId, (eRace)rec.RaceId, (eGender)rec.Gender, buttons,
                    live?.ObjectID ?? 0, hired.BotId, XpPerMille(live, rec), (ushort)hired.LookModel, (byte)hired.HairColour);
                if (live != null && live.ObjectState == GameObject.eObjectState.Active)
                    player.Out.SendLivingEquipmentUpdate(live);   // re-skins its hair in the original client
                // the roster's XP bubbles (op 10 for every row, owner layout)
                if (live != null)
                    CompanionWindows165.SendStats(player, r, live);
                else
                    CompanionWindows165.SendXpOnly(player, r, rec);
            }
            byte[] numbers = new byte[19];
            numbers[0] = 0;
            numbers[1] = (byte)d.Race;
            numbers[2] = (byte)(d.Gender == eGender.Female ? 1 : 0);
            numbers[3] = (byte)d.Class;
            numbers[4] = (byte)d.Face; numbers[5] = (byte)LookCount(d);   // [8] look, [9] look count
            // [10..11] (hair style, hair count) unused since the LOOK picker; [12] hair colour, [13] colour count
            numbers[8] = (byte)d.HairColour; numbers[9] = (byte)Math.Max(0, HairCount(d));
            numbers[10] = (byte)model; numbers[11] = (byte)(model >> 8);
            // [12..15] cost 0, [16] hire enabled
            numbers[12] = (byte)cost; numbers[13] = (byte)(cost >> 8); numbers[14] = (byte)(cost >> 16); numbers[15] = (byte)(cost >> 24);
            numbers[16] = (byte)(s.Mode == 0 && roster.Count < open ? 1 : 0);
            numbers[17] = (byte)Math.Clamp(d.Height, 1, 3); numbers[18] = 3;   // [21] height 1-3, [22] height count (new client)
            Send(player, 4, numbers);
            // Op 5 only here, after the kind-1 row of every occupied row: the new client drops the rows it did not get
            // since the last op 5 (rows keep their numbers when one is dismissed; client 8506954, 2026-11-08).
            Send(player, 5, [(byte)roster.Count, (byte)open, 7, (byte)next]);
            Send(player, 6, s.Preview == null ? [0, 0] : [(byte)s.Preview.ObjectID, (byte)(s.Preview.ObjectID >> 8)]);
        }

        private static void RosterRow(GamePlayer player, int row, string name, string classLevel, string where, byte state,
            byte level, eCharacterClass cls, eRace race, eGender gender, byte buttons, ushort objectId, long botId, int xpPerMille,
            ushort lookModel = 0, byte hairColour = 0)
        {
            Text(player, 16 + 3 * row, name);
            Text(player, 17 + 3 * row, classLevel);
            Text(player, 18 + 3 * row, where);
            ushort model = lookModel != 0 ? lookModel : (ushort)(PlayerRace.AllRaces.FirstOrDefault(r => r.ID == race)?.GetModel(gender) ?? 0);
            Send(player, 4, [1, (byte)row, state, level, (byte)cls, (byte)race, (byte)(gender == eGender.Female ? 1 : 0), buttons,
                (byte)model, (byte)(model >> 8), (byte)objectId, (byte)(objectId >> 8), (byte)botId, (byte)(botId >> 8),
                (byte)xpPerMille, (byte)(xpPerMille >> 8), hairColour]);
        }

        /// <summary>
        /// The roster numbers (op 4 kind 1) for every hired companion, with or without the window (the original client,
        /// 2026-10-30: it keeps their hair colours and object ids from these). Each summoned companion's row is followed by
        /// its equipment update, which is what makes the original client re-skin a classic figure's hair.
        /// Sent at login and whenever a companion appears (summon, recall, revive).
        /// </summary>
        public static void SendRosterNumbers(GamePlayer player)
        {
            if (!HiredCompanionsMode.IsOn || player?.Out == null)
                return;
            foreach (OfflineHiredCompanionRecord hired in HiredCompanions165.RosterOf(player))
            {
                if (HiredCompanions165.Record(hired.BotId) is not { } rec)
                    continue;
                GameBot live = HiredCompanions165.LiveBot(hired.BotId);
                bool dead = live != null ? !live.IsAlive : !rec.IsAlive;
                byte state = dead ? (byte)2 : live != null ? (byte)1 : (byte)0;
                byte level = (byte)(live?.Level ?? rec.Level);
                byte buttons = (byte)(dead ? 8 | 4 : live != null ? 2 | 4 : 1 | 4);
                ushort objectId = live != null && live.ObjectState == GameObject.eObjectState.Active ? live.ObjectID : (ushort)0;
                ushort model = hired.LookModel != 0 ? (ushort)hired.LookModel
                    : (ushort)(PlayerRace.AllRaces.FirstOrDefault(r => r.ID == (eRace)rec.RaceId)?.GetModel((eGender)rec.Gender) ?? 0);
                int xp = XpPerMille(live, rec);
                long botId = hired.BotId;
                Send(player, 4, [1, (byte)hired.Row, state, level, (byte)rec.ClassId, (byte)rec.RaceId, (byte)(rec.Gender == (int)eGender.Female ? 1 : 0),
                    buttons, (byte)model, (byte)(model >> 8), (byte)objectId, (byte)(objectId >> 8), (byte)botId, (byte)(botId >> 8),
                    (byte)xp, (byte)(xp >> 8), (byte)hired.HairColour]);
                if (objectId != 0)
                    player.Out.SendLivingEquipmentUpdate(live);
            }
        }

        /// <summary>XP toward the next level, per mille (the bubbles are /100).</summary>
        public static int XpPerMille(GameBot live, OfflineWorldBotRecord rec)
        {
            int level = live?.Level ?? rec.Level;
            long xp = live?.Experience ?? rec.Experience;
            long from = GamePlayer.GetExperienceAmountForLevel(level - 1), to = GamePlayer.GetExperienceAmountForLevel(level);
            return to <= from ? 0 : (int)System.Math.Clamp((xp - from) * 1000 / (to - from), 0, 1000);
        }

        private static string Spaced(string enumName) => string.Concat(enumName.Select((c, i) => i > 0 && char.IsUpper(c) ? " " + c : c.ToString()));

        public static void Text(GamePlayer player, int slot, string text)
        {
            byte[] body = new byte[123];
            body[0] = (byte)slot;
            byte[] ascii = Encoding.ASCII.GetBytes(text ?? string.Empty);
            Array.Copy(ascii, 0, body, 1, Math.Min(ascii.Length, 121));
            Send(player, 3, body);
        }

        /// <summary>
        /// Clients that read this protocol (Living Server, 2026-11-16). Only a client that says so gets 'H' packets: the
        /// original client's game.dll sends DebugMode bodies it doesn't hook down its stock debug path (byte 0 is the debug
        /// flag, byte 1 the observer-flight byte), untested with 0x48, so it never gets one. A client opts in with
        /// "/companionui protocol" (or True 1.65's "/companionui ownpreview"); Living Server's new client does not send
        /// either yet, so today every player gets the native windows (HireDialog165, /hire) and no 'H' packet at all.
        /// </summary>
        private static readonly ConditionalWeakTable<GamePlayer, object> ProtocolClients = new();

        public static bool ReadsProtocol(GamePlayer player) => player != null && ProtocolClients.TryGetValue(player, out _);

        public static void SetReadsProtocol(GamePlayer player) => ProtocolClients.AddOrUpdate(player, true);

        /// <summary>One 128-byte message: the 4-byte header, then <paramref name="payload"/> from byte 4, zero-padded.
        /// Sent only to a client that reads the protocol (<see cref="ReadsProtocol"/>).</summary>
        public static void Send(GamePlayer player, byte op, byte[] payload)
        {
            if (player?.Client?.Out == null || !ReadsProtocol(player))   // no client, or one that can't read it
                return;
            using var packet = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init((byte)eServerPackets.DebugMode);
            packet.WriteByte(0);
            packet.WriteByte(Marker);
            packet.WriteByte(1);
            packet.WriteByte(op);
            for (int i = 4; i < 128; i++)
                packet.WriteByte(i - 4 < payload.Length && i < 127 ? payload[i - 4] : (byte)0);
            player.Out.SendTCP(packet);
        }

        // ---- the private preview figure

        private static void SpawnPreview(GamePlayer player, Session s)
        {
            s.Preview?.Delete();
            s.Preview = null;
            (ushort Region, int X, int Y, int Z, ushort Heading) spot =
                PreviewSpots.TryGetValue(player.Realm, out var at) && at.Region == player.CurrentRegionID &&
                player.IsWithinRadius(new Point3D(at.X, at.Y, at.Z), 2500)
                    ? at
                    : (player.CurrentRegionID, player.X + (int)(150 * Math.Sin(player.Heading * Math.PI / 2048) * -1),
                       player.Y + (int)(150 * Math.Cos(player.Heading * Math.PI / 2048)), player.Z, (ushort)((player.Heading + 2048) % 4096));
            var npc = new PreviewNpc(player)
            {
                Name = string.Empty,   // no nameplate: the window shows the name (text 6)
                Realm = player.Realm,
                Level = 5,
                X = spot.X, Y = spot.Y, Z = spot.Z,
                Heading = spot.Heading,
                CurrentRegionID = spot.Region,
                Flags = GameNPC.eFlags.PEACE,
            };
            s.Preview = npc;
            s.PreviewKey = string.Empty;
            UpdatePreview(s);
            npc.AddToWorld();
            PreviewLog.Info($"HIRE_PREVIEW_165 spawn player={player.Name} id={npc.ObjectID} model={npc.Model}");
        }

        private static readonly Logging.Logger PreviewLog = Logging.LoggerManager.Create(typeof(Hire165));

        private static void UpdatePreview(Session s)
        {
            PreviewNpc npc = s.Preview;
            if (npc == null)
                return;
            eCharacterClass baseClass = (eCharacterClass)(ScriptMgr.FindCharacterBaseClass((int)s.Design.Class)?.ID ?? 0);
            var weapon = CreationStartupEquipment.GetStarterWeaponTemplate(baseClass);
            // rebuilt only when what it shows changes (look, sex, weapon, hair colour); every command re-created it, which
            // the original client drew as a constant flicker (2026-10-30)
            string key = $"{Model(s.Design)}|{s.Design.Gender}|{weapon?.Model ?? 0}|{s.Design.HairColour}";
            if (key == s.PreviewKey)
                return;
            s.PreviewKey = key;
            npc.Model = Model(s.Design);
            npc.HairColour = s.Design.HairColour;
            npc.Name = string.Empty;
            npc.Gender = s.Design.Gender;
            var template = new GameNpcInventoryTemplate();
            if (weapon != null)
                template.AddNPCEquipment(weapon.Item_Type == Slot.TWOHAND ? eInventorySlot.TwoHandWeapon : eInventorySlot.RightHandWeapon, weapon.Model);
            npc.Inventory = template.CloseTemplate();
            npc.SwitchWeapon(weapon?.Item_Type == Slot.TWOHAND ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
            if (npc.ObjectState == GameObject.eObjectState.Active)
            {
                // re-create it for its one viewer so the new model and weapon show
                npc.Owner.Out.SendObjectRemove(npc);
                npc.Owner.Out.SendNPCCreate(npc);
                npc.Owner.Out.SendLivingEquipmentUpdate(npc);
            }
        }

        /// <summary>A figure only the hiring player sees, turning slowly; gone when the window closes.</summary>
        public sealed class PreviewNpc : GameNPC
        {
            public GamePlayer Owner { get; }
            private ECSGameTimer _turn;

            public const int PreviewWalkAway = 500;
            public const long PreviewIdleMs = 60_000;

            /// <summary>Where the player stood when the window opened.</summary>
            public Point3D OpenedAt { get; }

            /// <summary>The chosen hair colour (True 1.65 sent it in the NPC create packet's model word; here it is only stored and sent in op 4, see HiredCompanionsMode).</summary>
            public int HairColour { get; set; }

            public PreviewNpc(GamePlayer owner) : base()
            {
                Owner = owner;
                OpenedAt = new Point3D(owner.X, owner.Y, owner.Z);
            }

            public override bool IsVisibleTo(GameObject checkObject) => checkObject == Owner && base.IsVisibleTo(checkObject);

            public override bool AddToWorld()
            {
                if (!base.AddToWorld())
                    return false;
                _turn = new ECSGameTimer(this, _ =>
                {
                    Session session = Get(Owner);
                    // walked away (500 from where the window opened; the old client can't report its X button) or the
                    // window gone: close it; a minute without a command: the figure alone goes (tested 2026-10-30)
                    if (Owner.ObjectState != eObjectState.Active || Owner.CurrentRegionID != CurrentRegionID ||
                        !Owner.IsWithinRadius(OpenedAt, PreviewWalkAway) || session?.Preview != this)
                    {
                        if (session?.Preview == this)
                            Close(Owner);
                        else
                            Delete();
                        return 0;
                    }
                    if (GameLoop.GameLoopTime - session.LastCommand > PreviewIdleMs)
                    {
                        session.Preview = null;
                        Delete();
                        Send(Owner, 6, [0, 0]);
                        return 0;
                    }
                    // Turn by hand: this figure has no brain, and GameNPC.TurnTo threw in its movement component,
                    // which made the timer service remove the figure 2 s after the window opened.
                    Heading = (ushort)((Heading + 128) % 4096);
                    ClientService.UpdateObjectForPlayer(Owner, this);
                    return 500;
                }, 500);
                return true;
            }

            public override void Delete()
            {
                _turn?.Stop();
                _turn = null;
                base.Delete();
            }
        }
    }
}
