using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;

// From shadowofze's True 1.65 v0.7 (f68cc75), via True165 (github.com/zomboden/OfflineDAoC-True165), GPL-3.0.
// Ported to Living Server (owner plan 2026-11-16): gated on HiredCompanionsMode (the hired_companions switch) instead of
// True 1.65 Mode; names claimed through CompanionNames165; the hire cost recorded in the economy ledger
// (EconomyFlowKind.CompanionHire); the population, registry and launcher keep hired companions out of the gamebot roster.

namespace DOL.GS
{
    /// <summary>One hired companion: a persistent gamebot record owned by one player character. A side table, so the
    /// launcher's offline_world_bots roster table is untouched (the guild-membership pattern). Saved progress: in
    /// progress-policy.json, emptied by release builds.</summary>
    [DataTable(TableName = "offline_hired_companions")]
    public sealed class OfflineHiredCompanionRecord : DataObject
    {
        [PrimaryKey] public long BotId { get; set; }
        [DataElement(AllowDbNull = false, Index = true)] public string OwnerCharacterID { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public string OwnerName { get; set; } = string.Empty;
        [DataElement(AllowDbNull = false)] public int Row { get; set; }
        [DataElement(AllowDbNull = false)] public bool WithOwner { get; set; }
        [DataElement(AllowDbNull = false)] public long PaidCopper { get; set; }
        [DataElement(AllowDbNull = false)] public string HiredUtc { get; set; } = string.Empty;
        /// <summary>The realm abilities the player chose for it, "Key:Level;Key:Level" (owner 2026-10-30).</summary>
        [DataElement(AllowDbNull = false)] public string RealmAbilities { get; set; } = string.Empty;
        /// <summary>The classic look chosen in the hire window (0 = the race's default figure) and its hair colour index
        /// (the look's hair set, period hair.csv; the clients re-skin the hair).</summary>
        [DataElement(AllowDbNull = false)] public int LookModel { get; set; }
        [DataElement(AllowDbNull = false)] public int HairColour { get; set; }
        /// <summary>Height chosen in the hire window: 1 Short, 2 Average, 3 Tall (0 = Average, rows from before it).</summary>
        [DataElement(AllowDbNull = false)] public int Height { get; set; }
        /// <summary>The player's choices for this companion (CompanionOptions165): charm, pet, sub-pets, songs.</summary>
        [DataElement(AllowDbNull = false)] public string Options { get; set; } = string.Empty;
    }

    /// <summary>
    /// Hired companions (True 1.65, owner 2026-10-30, plan sections 17, 17a, 17g, 17h, 24). True 1.65 replaced /spawn and
    /// /raid with them; Living Server keeps /spawn and /raid and adds these beside them. Each is a real persistent gamebot record (level, XP, specs, inventory, coin), hired at level 5 with a
    /// training weapon and nothing else, that joins its owner's group as a player-led gamebot (BotGroupInvite) and levels
    /// only by fighting beside the owner. The population controller never logs them in on its own.
    /// - Slots open at levels 5, 10, 15, 20, 30, 40 and 45 (7 companions: a full group of 8 at 45).
    /// - The first is free; each later one costs coin (CostCopper), so nobody is locked out.
    /// - Summon / dismiss in the hire window (/hire beside a healer of a capital or major town; True 1.65: the innkeeper),
    ///   release for good (with a confirm), revive free at any healer of the realm.
    /// - Log out and they leave with you; log in and the ones that were with you come back.
    /// </summary>
    public static class HiredCompanions165
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly ConcurrentDictionary<long, OfflineHiredCompanionRecord> Hired = new();
        private static readonly ConcurrentDictionary<long, GameBot> Live = new();
        private static readonly ConcurrentDictionary<long, byte> Dismissing = new();
        /// <summary>Companions that just left their player's group, waiting for the deferred park: KeepWithOwner must
        /// not invite them back in that gap ("X joins your group" after a kick, audit 2026-11-03).</summary>
        private static readonly ConcurrentDictionary<long, byte> Leaving = new();

        /// <summary>Hire cost by slot (copper): free, then about two hours of coin at the level the slot opens (owner to
        /// confirm in testing): 20 s, 60 s, 2 g, 6 g, 15 g, 30 g.</summary>
        public static readonly long[] CostCopper = [0, 2_000, 6_000, 20_000, 60_000, 150_000, 300_000];

        private static readonly object LoadLock = new();
        private static bool _loaded;

        /// <summary>
        /// The hired companions from the database, once. Also called by the population system's start-up work before it
        /// sorts the bots, so a companion is never taken for a gamebot whichever start handler runs first (an updated
        /// world: every character's companions stay theirs). Throws when the table can't be read.
        /// </summary>
        public static void EnsureLoaded()
        {
            lock (LoadLock)
            {
                if (_loaded)
                    return;
                foreach (OfflineHiredCompanionRecord r in DOLDB<OfflineHiredCompanionRecord>.SelectAllObjects())
                    Hired[r.BotId] = r;
                _loaded = true;
                // A companion whose character was deleted stays hired (never a gamebot, never logged in) until the launcher's
                // DELETE ALL removes it (BotPurge deletes orphans only); said once per start.
                foreach (OfflineHiredCompanionRecord r in Hired.Values)
                    if (GameServer.Database != null &&
                        GameServer.Database.FindObjectByKey<DbCoreCharacter>(r.OwnerCharacterID) == null)
                        Log.Warn($"HIRED_COMPANION_ORPHAN bot={r.BotId} owner=\"{r.OwnerName}\": its character no longer exists");
            }
        }

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                EnsureLoaded();
            }
            catch (Exception ex)
            {
                Log.Error("Hired companions could not be loaded", ex);
            }
            GameEventMgr.AddHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));
            GameEventMgr.AddHandler(GamePlayerEvent.Quit, new DOLEventHandler(OnPlayerQuit));
            // (True 1.65's RegionChanged handler is not ported: Living Server's GamePlayer.MoveTo already brings the
            // player's group bots along, TemporaryGroupStableTravel.RelocateHelpersAfterPlayerTransfer / TryBringGroupmateAlong.)
        }

        [GameServerStoppedEvent]
        public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
        {
            GameEventMgr.RemoveHandler(GamePlayerEvent.GameEntered, new DOLEventHandler(OnPlayerEntered));
            GameEventMgr.RemoveHandler(GamePlayerEvent.Quit, new DOLEventHandler(OnPlayerQuit));
        }

        /// <summary>A hired companion's record: the population controller leaves these to their owners.</summary>
        public static bool IsHired(long botId) => Hired.ContainsKey(botId);

        /// <summary>A hired companion's chosen hair colour, or -1 for any other bot.</summary>
        public static int HairColourOf(GameBot bot) =>
            bot != null && bot.DatabaseID > 0 && Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord h) ? h.HairColour : -1;

        public static bool IsHired(GameBot bot) => bot != null && bot.DatabaseID > 0 && Hired.ContainsKey(bot.DatabaseID);

        /// <summary>This bot is one of this player character's hired companions.</summary>
        public static bool IsHiredBy(GameBot bot, GamePlayer player) =>
            player != null && bot != null && bot.DatabaseID > 0 && Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord h) &&
            h.OwnerCharacterID == player.InternalID;

        /// <summary>Tests: a hire record without the database.</summary>
        internal static void RegisterForTests(OfflineHiredCompanionRecord hired) => Hired[hired.BotId] = hired;

        /// <summary>Tests: forget every hire record.</summary>
        internal static void ClearForTests()
        {
            Hired.Clear();
            Live.Clear();
        }

        /// <summary>A hired companion's hire record, or null for any other bot.</summary>
        public static OfflineHiredCompanionRecord HiredRecord(GameBot bot) =>
            bot != null && bot.DatabaseID > 0 && Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord h) ? h : null;

        public static void SaveRecord(OfflineHiredCompanionRecord hired) => Save(hired);

        /// <summary>The population migration deleted this hire row: its owner character no longer exists.</summary>
        internal static void ForgetOrphan(long botId)
        {
            Hired.TryRemove(botId, out _);
            Live.TryRemove(botId, out _);
        }

        /// <summary>A hired companion's bag: 10 slots, no weight limit (owner 2026-10-30), for what the player gives it
        /// to use (arrows, poisons, potions) or to carry. The first 10 backpack slots of the bot's own inventory.</summary>
        public const int BagSlots = 10;
        public const eInventorySlot FirstBagSlot = eInventorySlot.FirstBackpack;
        public const eInventorySlot LastBagSlot = eInventorySlot.FirstBackpack + (BagSlots - 1);

        public static bool IsBagSlot(int slot) => slot >= (int)FirstBagSlot && slot <= (int)LastBagSlot;

        public static List<OfflineHiredCompanionRecord> RosterOf(GamePlayer player) =>
            player == null ? [] : Hired.Values.Where(h => h.OwnerCharacterID == player.InternalID).OrderBy(h => h.Row).ToList();

        public static int OpenSlots(int level) => Hire165.SlotLevels.Count(l => level >= l);

        public static int NextSlotLevel(int level) => Hire165.SlotLevels.FirstOrDefault(l => l > level);

        public static long NextCost(GamePlayer player) => CostCopper[Math.Min(RosterOf(player).Count, CostCopper.Length - 1)];

        public static OfflineWorldBotRecord Record(long botId) =>
            DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(botId));

        /// <summary>The player's hired companions that are in the world now.</summary>
        public static IEnumerable<GameBot> LiveFor(GamePlayer player) =>
            RosterOf(player).Select(h => LiveBot(h.BotId)).Where(bot => bot != null).ToArray();

        public static GameBot LiveBot(long botId) =>
            Live.TryGetValue(botId, out GameBot bot) && bot.ObjectState == GameObject.eObjectState.Active ? bot : null;

        // ---- hiring

        /// <summary>Hires the designed companion. Returns the problem, or null when hired (it joins the owner at once).</summary>
        public static string Hire(GamePlayer player, Hire165.Design d)
        {
            if (!HiredCompanionsMode.IsOn)
                return "Hired companions are switched off on this server (hired_companions).";
            List<OfflineHiredCompanionRecord> roster = RosterOf(player);
            int open = OpenSlots(player.Level);
            if (roster.Count >= open)
                return open == 0 ? "You can hire your first companion at level 5."
                    : NextSlotLevel(player.Level) is > 0 and var next ? $"Your next companion slot opens at level {next}." : "All your companion slots are taken.";
            if (d.Class == 0 || d.Race == 0)
                return "Choose a race and class first.";
            if (!OfflineDaoc.Shared.BotCustomClassRule.BotsMayPlay((int)d.Class))
                return OfflineDaoc.Shared.BotCustomClassRule.RefusedMessage;
            string name = d.Name?.Trim() ?? string.Empty;
            if (Hire165.NameProblem(name) is { } badName)
                return badName;
            name = char.ToUpperInvariant(name[0]) + name[1..];
            // claim the name (atomic against other hires), checked against every player and bot
            if (!CompanionNames165.TryClaim(name))
                return $"The name {name} is taken.";
            long cost = CostCopper[Math.Min(roster.Count, CostCopper.Length - 1)];
            if (cost > 0 && !player.RemoveMoney(cost))
            {
                CompanionNames165.Release(name);
                return $"You need {Money.GetString(cost)} to hire another companion.";
            }
            // the hire cost leaves the money supply: a named player-side sink in the economy ledger
            if (cost > 0)
                EconomyFlows.RecordPlayer(player.Realm, -cost, EconomyFlowKind.CompanionHire);

            string now = DateTime.UtcNow.ToString("O");
            ICharacterClass cls = ScriptMgr.FindCharacterClass((int)d.Class);
            var record = new OfflineWorldBotRecord
            {
                Name = name,
                Realm = (int)player.Realm,
                ClassId = (int)d.Class,
                ClassName = cls?.Name ?? d.Class.ToString(),
                RaceId = (int)d.Race,
                RaceName = d.Race.ToString(),
                Gender = (int)d.Gender,
                Level = 5,
                Experience = GamePlayer.GetExperienceAmountForLevel(4),
                RegionId = player.CurrentRegionID, X = player.X, Y = player.Y, Z = player.Z,
                ZoneId = player.CurrentZone?.ID ?? 0, ZoneName = player.CurrentZone?.Description ?? string.Empty,
                BindRegionId = player.CurrentRegionID, BindX = player.X, BindY = player.Y, BindZ = player.Z,
                IsAlive = true,
                Activity = $"Hired by {player.Name}",
                CurrentGoal = $"Companion of {player.Name}",
                LastUpdateUtc = now,
            };
            GameServer.Database.AddObject(record);
            CompanionNames165.Release(name);   // the record holds the name from here on
            int row = Enumerable.Range(0, Hire165.SlotLevels.Length).First(r => roster.All(h => h.Row != r));
            var hired = new OfflineHiredCompanionRecord
            {
                BotId = record.BotId, OwnerCharacterID = player.InternalID, OwnerName = player.Name, Row = row,
                WithOwner = true, PaidCopper = cost, HiredUtc = now,
                LookModel = Hire165.Model(d), HairColour = d.HairColour, Height = Math.Clamp(d.Height, 1, 3),
            };
            GameServer.Database.AddObject(hired);
            Hired[record.BotId] = hired;
            Log.Info($"HIRED_COMPANION owner=\"{player.Name}\" bot={record.BotId} \"{name}\" {record.RaceName} {record.ClassName} cost={cost}");
            Summon(player, row, firstTime: true);
            return null;
        }

        // ---- summon, dismiss, release, revive

        public static string Summon(GamePlayer player, int row, bool firstTime = false, bool revived = false)
        {
            OfflineHiredCompanionRecord hired = RosterOf(player).FirstOrDefault(h => h.Row == row);
            if (hired == null)
                return "There is no companion in that row.";
            GameBot live = LiveBot(hired.BotId);
            if (live != null)
            {
                if (live.Group == null || !live.Group.IsInTheGroup(player))
                    BotGroupInvite.TryInvite(player, live);
                return null;
            }
            OfflineWorldBotRecord record = Record(hired.BotId);
            if (record == null)
                return "That companion's record is missing.";
            // Bots never play the custom class (owner 2026-11-17): a companion hired as a Sluaghbinder before 1.2 stays in
            // the roster with its items, but never comes into the world.
            if (!OfflineDaoc.Shared.BotCustomClassRule.BotsMayPlay(record.ClassId))
                return $"{record.Name} stays at the inn: {OfflineDaoc.Shared.BotCustomClassRule.RefusedMessage}";
            if (!record.IsAlive)
                return $"{record.Name} is dead. Any healer of your realm brings them back, free.";
            // GitHub #49: an earlier copy of this companion that is no longer in the world (a move that never finished)
            // may still sit in the group; the new one would then show as a second row of the same companion.
            RetireStaleCopies(player, hired.BotId);
            if (player.Group != null && player.Group.MemberCount >= 8)
                return "Your group is full.";
            try
            {
                string ownerId = AutonomousBotEconomy.GetOwnerId(record.BotId);
                var items = new BotInventory(ownerId).SelectPersonalItems(ownerId);
                (int sx, int sy, int sz) = ArrivalSpot(player);
                var bot = new GameBot(record, items)
                {
                    CurrentRegionID = player.CurrentRegionID,
                    X = sx,
                    Y = sy,
                    Z = sz,
                    Heading = player.Heading,
                };
                bot.SetLookModel((ushort)hired.LookModel);
                // (True 1.65's /hood for companions is not ported: Living Server has no hood state on a bot.)
                bot.Size = GameBot.SizeForHeight(hired.Height);   // its chosen height, as a player of that height (owner 2026-10-31)
                bot.SynchronizePositionForLoginValidation();
                // a revive (its body had faded) brings it back whole, like a raise of a body still lying there
                if (firstTime || revived)
                {
                    // its level 2-5 points stay unspent: the player places them in the spec screen (owner rule)
                    bot.Health = bot.MaxHealth;
                    bot.Mana = bot.MaxMana;
                    bot.Endurance = bot.MaxEndurance;
                }
                if (!bot.AddToWorld())
                    return $"{record.Name} could not join you here.";
                CompanionWindows165.ApplyRealmAbilities(bot, hired);
                bot.LogCompanionSkills("summoned");
                record.IsOnline = true;
                record.Activity = $"With {player.Name}";
                bot.MarkAutonomousStateDirty();
                Live[record.BotId] = bot;
                hired.WithOwner = true;
                Save(hired);
                BotGroupInvite.TryInvite(player, bot);
                // the owner's PC log proves the summon (hire, revive, login or the roster's button) and the group join
                Log.Info($"HIRED_COMPANION_SUMMONED owner=\"{player.Name}\" bot={record.BotId} \"{record.Name}\" " +
                         $"why={(firstTime ? "hired" : revived ? "revived" : "summon")} region={player.CurrentRegionID} " +
                         $"grouped={bot.Group != null && bot.Group == player.Group} level={bot.Level}");
                AutonomousBotGuilds.Present(bot);   // its player's guild name and emblem (owner 2026-11-05)
                AutonomousBotStatusPersistence.Queue(bot, true);
                Hire165.SendRosterNumbers(player);   // its object id and hair colour for the clients
                return null;
            }
            catch (Exception ex)
            {
                Log.Error($"Hired companion {hired.BotId} could not be summoned", ex);
                return "Your companion could not be summoned.";
            }
        }

        /// <summary>Pure (GitHub #49): a group member is a leftover copy of the companion being summoned - a gamebot with that
        /// companion's id that is not the object now in the world.</summary>
        public static bool IsStaleCopy(long memberBotId, bool memberIsLiveObject, long summonedBotId) =>
            summonedBotId > 0 && memberBotId == summonedBotId && !memberIsLiveObject;

        /// <summary>Takes every leftover copy of this companion out of its group (and out of the world): the copy the
        /// roster still points at when it is not in the world, and any other object with its id in the player's group.
        /// Runs only when a companion is summoned.</summary>
        private static void RetireStaleCopies(GamePlayer player, long botId)
        {
            GameBot live = LiveBot(botId);
            var stale = new List<GameBot>(2);
            if (Live.TryGetValue(botId, out GameBot listed) && IsStaleCopy(listed.DatabaseID, ReferenceEquals(listed, live), botId))
                stale.Add(listed);
            if (player?.Group != null)
                foreach (GameLiving member in player.Group.GetMembersInTheGroup())
                    if (member is GameBot copy && IsStaleCopy(copy.DatabaseID, ReferenceEquals(copy, live), botId) && !stale.Contains(copy))
                        stale.Add(copy);
            if (stale.Count == 0)
                return;
            Dismissing[botId] = 0;   // leaving the group must not park the companion we are about to summon
            try
            {
                foreach (GameBot copy in stale)
                {
                    Log.Warn($"COMPANION_STALE_COPY_165 bot=\"{copy.Name}\" id={botId} state={copy.ObjectState} grouped={copy.Group != null} " +
                             $"player=\"{player?.Name}\": removed before the summon");
                    copy.Group?.RemoveMember(copy);
                    if (copy.ObjectState == GameObject.eObjectState.Active)
                        copy.RemoveFromWorld();
                    Live.TryRemove(new KeyValuePair<long, GameBot>(botId, copy));
                }
            }
            finally
            {
                Dismissing.TryRemove(botId, out _);
            }
        }

        /// <summary>Sends a companion back to the inn (it keeps everything it carries). keepWithOwner: it comes back the
        /// next time the owner logs in (logout); otherwise it waits at the inn until summoned.</summary>
        public static string Dismiss(GamePlayer player, int row, bool keepWithOwner = false)
        {
            OfflineHiredCompanionRecord hired = RosterOf(player).FirstOrDefault(h => h.Row == row);
            if (hired == null)
                return "There is no companion in that row.";
            Park(hired, keepWithOwner);
            Log.Info($"HIRED_COMPANION_DISMISSED owner=\"{player.Name}\" bot={hired.BotId} row={row} keepWithOwner={keepWithOwner}");
            return null;
        }

        private static void Park(OfflineHiredCompanionRecord hired, bool keepWithOwner)
        {
            hired.WithOwner = keepWithOwner;
            Save(hired);
            if (!Live.TryRemove(hired.BotId, out GameBot bot))
                return;
            if (bot.ObjectState != GameObject.eObjectState.Active)
            {
                // GitHub #49: a copy that is not in the world still leaves the group, or it stays there as a row nobody
                // can see when the companion is summoned again
                Dismissing[hired.BotId] = 0;
                try { bot.Group?.RemoveMember(bot); }
                finally { Dismissing.TryRemove(hired.BotId, out _); }
                return;
            }
            Dismissing[hired.BotId] = 0;
            try
            {
                bot.Group?.RemoveMember(bot);
                if (bot.PersistentRecord is { } record)
                {
                    record.Activity = keepWithOwner ? $"Resting with {hired.OwnerName} (offline)" : "Waiting for its player (dismissed)";
                    record.Dirty = true;
                }
                bot.RemoveFromWorld();   // saves the record and inventory
            }
            finally
            {
                Dismissing.TryRemove(hired.BotId, out _);
            }
        }

        /// <summary>Releases a companion for good (asks first): it and what it carries are gone.</summary>
        public static void AskRelease(GamePlayer player, int row)
        {
            OfflineHiredCompanionRecord hired = RosterOf(player).FirstOrDefault(h => h.Row == row);
            if (hired == null)
                return;
            string name = Record(hired.BotId)?.Name ?? "this companion";
            player.Out.SendCustomDialog($"Release {name} for good? They leave with everything they carry, quest and no-trade items included, and cannot be hired back.",
                (p, response) =>
                {
                    if (response != 0x01 || !Hired.ContainsKey(hired.BotId))
                        return;
                    Park(hired, false);
                    Hired.TryRemove(hired.BotId, out _);
                    GameServer.Database.DeleteObject(hired);
                    AutonomousPopulationController.DeleteHiredCompanion(hired.BotId);
                    Log.Info($"HIRED_COMPANION_RELEASED owner=\"{p.Name}\" bot={hired.BotId} \"{name}\"");
                    p.Out.SendMessage($"{name} has left your service.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    Hire165.Refresh(p);
                });
        }

        /// <summary>Free revive at any healer of the realm (owner rule: nobody gets stuck unable to afford their team).</summary>
        /// <summary>Where a summoned companion appears: a few steps from the player on the same floor (the navmesh there is at
        /// the player's height), never on a counter, table or ledge beside them (owner 2026-10-30: one landed on the inn's
        /// counter); the player's own spot when nothing nearby qualifies.</summary>
        private static (int X, int Y, int Z) ArrivalSpot(GamePlayer player)
        {
            Zone zone = player.CurrentZone;
            var nav = PathfindingProvider.Instance;
            if (zone != null && nav.HasNavmesh(zone))
            {
                // Slide from the player's own spot along the walkable floor toward each candidate, so the spot is one the
                // player could walk straight to on the same floor. The closest navmesh point could be a stair surface or
                // a floor behind a wall (owner 2026-10-31: companions kept appearing in the staircase of Hibernia's inn).
                var from = new System.Numerics.Vector3(player.X, player.Y, player.Z);
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    double angle = Util.RandomDouble() * Math.PI * 2;
                    int distance = Util.Random(40, 90);
                    var wanted = new System.Numerics.Vector3(player.X + (float)(Math.Cos(angle) * distance),
                        player.Y + (float)(Math.Sin(angle) * distance), player.Z);
                    System.Numerics.Vector3? floor = nav.GetMoveAlongSurface(zone, from, wanted, nav.DefaultFilters);
                    if (floor is not { } f || Math.Abs(f.Z - player.Z) > 12 ||
                        System.Numerics.Vector2.Distance(new(f.X, f.Y), new(player.X, player.Y)) < 30 ||
                        System.Numerics.Vector2.Distance(new(f.X, f.Y), new(wanted.X, wanted.Y)) > 30 ||
                        !nav.HasLineOfSight(zone, from, f, nav.DefaultFilters))
                        continue;
                    return ((int)f.X, (int)f.Y, (int)Math.Round(f.Z));
                }
            }
            return (player.X, player.Y, player.Z);
        }

        // ---- away from the player, and death (owner 2026-10-30)

        /// <summary>A dead companion's body stays where it fell this long (a party member may still resurrect it there); then
        /// it fades, and a healer brings it back (True 1.65: a healer at a traveler's inn).</summary>
        public const int BodyFadeMilliseconds = 5 * 60 * 1000;

        private static readonly ConcurrentDictionary<long, long> NextRecall = new();

        /// <summary>The online player who hired this companion, or null.</summary>
        public static GamePlayer OwnerOf(GameBot bot) =>
            bot != null && Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord hired) ? OwnerOnline(hired) : null;

        private static GamePlayer OwnerOnline(OfflineHiredCompanionRecord hired)
        {
            GamePlayer player = ClientService.Instance.GetPlayerByExactName(hired.OwnerName);
            return player != null && player.InternalID == hired.OwnerCharacterID ? player : null;
        }

        /// <summary>A hired companion never runs the world bots' own goals (errands, camps, travel). Out of its player's group
        /// while the player is online it rejoins them; with the player gone it waits. True when it was handled here.</summary>
        public static bool KeepWithOwner(GameBot bot)
        {
            // only a living companion outside its player's group is handled here; one following its player thinks as usual
            if (!HiredCompanionsMode.IsOn || bot == null || !bot.IsAlive || bot.IsPlayerLedGroup ||
                !Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord hired))
                return false;
            if (Leaving.ContainsKey(bot.DatabaseID))
                return true;   // on its way to the inn (or parked offline): never invited back in that moment
            GamePlayer owner = OwnerOnline(hired);
            if (owner == null || !hired.WithOwner)
                return true;
            long now = GameLoop.GameLoopTime;
            if (NextRecall.TryGetValue(-bot.DatabaseID, out long next) && now < next)
                return true;
            NextRecall[-bot.DatabaseID] = now + 3000;
            if (bot.Group == null || !bot.Group.IsInTheGroup(owner))
                BotGroupInvite.TryInvite(owner, bot);
            else
                bot.EnterPlayerLedGroup(owner);
            TryRecallToOwner(bot);
            return true;
        }

        private sealed class FollowProgress
        {
            public int X, Y;
            public long Since;
        }

        private static readonly ConcurrentDictionary<long, FollowProgress> Progress = new();
        public const int StuckDistance = 600;
        public const long StuckMilliseconds = 6000;

        /// <summary>A companion in its player's region that has fallen more than 600 behind and not moved for 6 s outside a
        /// fight is stuck (owner 2026-10-31: a Cleric stopped following after its player zoned into Camelot City, inside
        /// the 3000 leash so the recall never came). It logs what it was doing (COMPANION_STUCK_165) and is brought back
        /// beside its player like a recall.</summary>
        public static bool TryUnstick(GameBot bot)
        {
            if (!HiredCompanionsMode.IsOn || bot is not { IsAlive: true, ObjectState: GameObject.eObjectState.Active } ||
                bot.StowedForRide || !Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord hired) || !hired.WithOwner)
                return false;
            GamePlayer owner = OwnerOnline(hired);
            long now = GameLoop.GameLoopTime;
            FollowProgress p = Progress.GetOrAdd(bot.DatabaseID, _ => new FollowProgress { X = bot.X, Y = bot.Y, Since = now });
            bool fighting = bot.InCombat || TemporaryCompanionRecovery.HasPartyCombat(bot);
            if (owner is not { IsAlive: true, ObjectState: GameObject.eObjectState.Active } || owner.IsRiding || fighting ||
                owner.CurrentRegionID != bot.CurrentRegionID || bot.IsWithinRadius(owner, StuckDistance) ||
                Math.Abs(bot.X - p.X) + Math.Abs(bot.Y - p.Y) > 50)
            {
                p.X = bot.X; p.Y = bot.Y; p.Since = now;
                return false;
            }
            if (now - p.Since < StuckMilliseconds)
                return false;
            Log.Info($"COMPANION_STUCK_165 bot=\"{bot.Name}\" id={bot.DatabaseID} player=\"{owner.Name}\" region={bot.CurrentRegionID} " +
                     $"at={bot.X},{bot.Y},{bot.Z} player_at={owner.X},{owner.Y},{owner.Z} distance={(int)bot.GetDistanceTo(owner)} " +
                     $"fsm={(bot.Brain as DOL.AI.Brain.BotBrain)?.FSM?.GetCurrentState()?.StateType} moving={bot.IsMoving} " +
                     $"resting={bot.IsRecoveryResting} casting={bot.IsCasting} follow=\"{bot.FollowTarget?.Name}\" " +
                     $"stable_route={bot.IsOnStableMasterRoute} led={bot.IsPlayerLedGroup}");
            p.Since = now;
            (int x, int y, int z) = ArrivalSpot(owner);
            bot.StopCurrentSpellcast();
            bot.StopMoving();
            if (bot.IsRecoveryResting)
                bot.WakeRecoveryRest();
            if (!bot.MoveTo(owner.CurrentRegionID, x, y, z, owner.Heading))
                return false;
            bot.Follow(owner, BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);
            (bot.Brain as DOL.AI.Brain.BotBrain)?.FSM?.SetCurrentState(eFSMStateType.FOLLOW);
            bot.Brain?.Start();
            Hire165.SendRosterNumbers(owner);
            return true;
        }

        /// <summary>A living companion that is with its player but too far away (another region, or past the companions'
        /// leash of 3000) is brought back beside them, as the normal companions are; never mid-fight in the same region.</summary>
        public static bool TryRecallToOwner(GameBot bot)
        {
            if (!HiredCompanionsMode.IsOn || bot == null || !bot.IsAlive || bot.ObjectState != GameObject.eObjectState.Active ||
                !Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord hired) || !hired.WithOwner)
                return false;
            GamePlayer owner = OwnerOnline(hired);
            if (owner == null || !owner.IsAlive || owner.ObjectState != GameObject.eObjectState.Active || owner.IsRiding)
                return false;
            bool sameRegion = bot.CurrentRegionID == owner.CurrentRegionID;
            if (sameRegion && bot.IsWithinRadius(owner, TemporaryCompanionRecovery.MaximumLeaderDistance))
                return false;
            if (sameRegion && TemporaryCompanionRecovery.HasPartyCombat(bot))
                return false;
            long now = GameLoop.GameLoopTime;
            if (NextRecall.TryGetValue(bot.DatabaseID, out long next) && now < next)
                return false;
            NextRecall[bot.DatabaseID] = now + 5000;
            (int x, int y, int z) = ArrivalSpot(owner);
            bot.StopCurrentSpellcast();
            bot.StopMoving();
            if (bot.IsRecoveryResting)
                bot.WakeRecoveryRest();
            if (!bot.MoveTo(owner.CurrentRegionID, x, y, z, owner.Heading))
                return false;
            // MoveTo stops the brain (WAKING_UP, a state BotBrain doesn't have: no state at all), so the companion stood
            // there until the stuck check moved it again (owner 2026-11-01): it follows its player again, as TryUnstick does.
            bot.Follow(owner, BotManager.FOLLOW_DISTANCE, BotManager.MAX_FOLLOW_DISTANCE);
            (bot.Brain as DOL.AI.Brain.BotBrain)?.FSM?.SetCurrentState(eFSMStateType.FOLLOW);
            bot.Brain?.Start();
            Hire165.SendRosterNumbers(owner);   // it may have a new object id after the move
            return true;
        }

        /// <summary>Raised where it fell (by the player, a companion, a healer): back in its player's group and at their
        /// side, as a raised player rejoins (owner 2026-10-30). It follows from there; far away, the recall brings it.</summary>
        public static void OnRaised(GameBot bot)
        {
            if (!HiredCompanionsMode.IsOn || bot == null || !Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord hired))
                return;
            GamePlayer owner = OwnerOnline(hired);
            if (owner == null)
                return;
            hired.WithOwner = true;
            Save(hired);
            if (bot.Group == null || !bot.Group.IsInTheGroup(owner))
                BotGroupInvite.TryInvite(owner, bot);
            NextRecall.TryRemove(bot.DatabaseID, out _);
            TryRecallToOwner(bot);
            Hire165.SendRosterNumbers(owner);
        }

        /// <summary>The death of a hired companion (GameBot's death recovery asks here first): the body never moves to the
        /// player; after <see cref="BodyFadeMilliseconds"/> it fades and the companion waits, dead, for a healer.
        /// Returns the next check in ms, 0 when done, or -1 when this is not a hired companion.</summary>
        public static int DeathTick(GameBot bot, long deadForMilliseconds)
        {
            if (!HiredCompanionsMode.IsOn || bot == null || !Hired.TryGetValue(bot.DatabaseID, out OfflineHiredCompanionRecord hired))
                return -1;
            if (bot.IsAlive)
                return 0;
            if (deadForMilliseconds < BodyFadeMilliseconds)
                return 3000;
            GamePlayer owner = OwnerOnline(hired);
            if (bot.PersistentRecord is { } record)
            {
                record.IsAlive = false;
                record.Dirty = true;
            }
            Park(hired, keepWithOwner: false);
            owner?.Out.SendMessage($"{bot.Name}'s body has faded. Any healer of your realm brings them back, free (/hire beside the healer).",
                eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            if (owner != null)
                Hire165.Refresh(owner);
            return 0;
        }

        /// <summary>
        /// A player who talks to a healer with a dead companion (its body still lying or faded) gets the healer's window
        /// (Hire165 mode 1, REVIVE) and a line in chat saying how to bring each one back; nothing for anyone else.
        /// </summary>
        public static void OfferHealer(GamePlayer player, GameNPC healer)
        {
            if (!HiredCompanionsMode.IsOn || player == null)
                return;
            var dead = new List<(int Row, string Name)>();
            foreach (OfflineHiredCompanionRecord hired in RosterOf(player))
            {
                GameBot live = LiveBot(hired.BotId);
                if (live != null ? !live.IsAlive : Record(hired.BotId) is { IsAlive: false } rec)
                    dead.Add((hired.Row, live?.Name ?? Record(hired.BotId)?.Name ?? "your companion"));
            }
            if (dead.Count == 0)
                return;
            Hire165.OpenHealer(player, healer);
            foreach ((int row, string name) in dead)
                player.Out.SendMessage($"{healer.GetName(0, true)} can bring {name} back, free: /hire revive {row + 1}",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        public static string Revive(GamePlayer player, int row)
        {
            OfflineHiredCompanionRecord hired = RosterOf(player).FirstOrDefault(h => h.Row == row);
            if (hired == null)
                return "There is no companion in that row.";
            GameBot live = LiveBot(hired.BotId);
            if (live != null)
            {
                if (live.IsAlive)
                    return $"{live.Name} is alive.";
                // alive first: raised while still at 0 health it counted as dead, failed to rejoin the group and was left to
                // the world bots' own goals, idling in the inn (owner 2026-10-30: Osbert never followed after a revive)
                live.Health = live.MaxHealth;
                live.Mana = live.MaxMana;
                live.Endurance = live.MaxEndurance;
                (int rx, int ry, int rz) = ArrivalSpot(player);
                live.MoveTo(player.CurrentRegionID, rx, ry, rz, player.Heading);
                live.OnResurrectedBy(player);
                Log.Info($"HIRED_COMPANION_REVIVED owner=\"{player.Name}\" bot={hired.BotId} \"{live.Name}\" body=lying " +
                         $"grouped={live.Group != null && live.Group == player.Group}");
                return null;
            }
            OfflineWorldBotRecord record = Record(hired.BotId);
            if (record == null)
                return "That companion's record is missing.";
            if (record.IsAlive)
                return $"{record.Name} is alive.";
            record.IsAlive = true;
            record.Health = 0;   // full on the summon below (revived: it came back on 1 health before, audit 2026-11-03)
            record.Dirty = true; // its properties don't mark it changed; without this the save wrote nothing
            GameServer.Database.SaveObject(record);
            // straight back into the party (owner 2026-10-30: the inn is only for hiring and retrieving dismissed ones)
            Log.Info($"HIRED_COMPANION_REVIVED owner=\"{player.Name}\" bot={hired.BotId} \"{record.Name}\" body=faded");
            string problem = Summon(player, row, revived: true);
            return problem == null ? null : $"{record.Name} lives again. {problem}";
        }

        // ---- the bot's side

        /// <summary>A hired companion that leaves its owner's group goes back to the inn instead of roaming (called from
        /// GameBot.LeavePlayerLedGroup).</summary>
        public static void OnLeftPlayerGroup(GameBot bot)
        {
            if (!IsHired(bot) || Dismissing.ContainsKey(bot.DatabaseID) || !Hired.TryGetValue(bot.DatabaseID, out var hired))
                return;
            // Logging out breaks the group up before the quit event: a companion whose owner is gone (or going) is
            // parked to come back at the next login, not sent to wait at the inn (tested in the new client, 2026-10-30).
            Leaving[hired.BotId] = 0;
            NpcService.Instance.Post<OfflineHiredCompanionRecord>(h =>
            {
                try
                {
                    GamePlayer owner = ClientService.Instance.GetPlayerByExactName(h.OwnerName);
                    bool ownerPlaying = owner != null && owner.ObjectState == GameObject.eObjectState.Active &&
                                        owner.Client?.ClientState == GameClient.eClientState.Playing;
                    Park(h, keepWithOwner: !ownerPlaying);
                }
                finally
                {
                    Leaving.TryRemove(h.BotId, out _);
                }
            }, hired);
        }

        private static void OnPlayerEntered(DOLEvent e, object sender, EventArgs args)
        {
            if (!HiredCompanionsMode.IsOn || sender is not GamePlayer player)
                return;
            List<OfflineHiredCompanionRecord> withOwner = RosterOf(player).Where(h => h.WithOwner).ToList();
            if (withOwner.Count > 0)
                Log.Info($"HIRED_COMPANION_LOGIN owner=\"{player.Name}\" summoning={withOwner.Count}");
            foreach (OfflineHiredCompanionRecord hired in withOwner)
                Summon(player, hired.Row);
            Hire165.SendRosterNumbers(player);
        }

        private static void OnPlayerQuit(DOLEvent e, object sender, EventArgs args)
        {
            if (sender is not GamePlayer player)
                return;
            List<OfflineHiredCompanionRecord> live = RosterOf(player).Where(h => Live.ContainsKey(h.BotId)).ToList();
            foreach (OfflineHiredCompanionRecord hired in live)
                Park(hired, keepWithOwner: true);
            if (live.Count > 0)
                Log.Info($"HIRED_COMPANION_LOGOUT owner=\"{player.Name}\" parked={live.Count}");
        }

        /// <summary>
        /// Pure: the experience a hired companion may take now. At its player's level it fills its bar and stops; above
        /// it (its player lost a level) it takes nothing; without a known cap it takes all of it.
        /// </summary>
        public static long CappedExperience(long gained, long experience, int level, byte? cap)
        {
            if (cap is not byte capLevel || level < capLevel || gained <= 0)
                return gained;
            if (level > capLevel)
                return 0;
            long full = GamePlayer.GetExperienceAmountForLevel(capLevel) - 1;
            return Math.Min(gained, Math.Max(0, full - experience));
        }

        /// <summary>An item dragged onto one's own hired companion: worn when it can (CanWear decides the slot), else into
        /// its 10-slot bag. False for any other bot or player, so the drag goes on as before.</summary>
        public static bool TryReceiveItem(GameBot bot, GamePlayer player, DbInventoryItem item)
        {
            if (!HiredCompanionsMode.IsOn || item == null || !IsHiredBy(bot, player) ||
                item.SlotPosition is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack)
                return false;
            bool wears = AutonomousBotEconomy.CanWear(bot, item, out _);
            string problem = CompanionWindows165.Equip(player, bot, item.SlotPosition);
            player.Out.SendMessage(problem ?? (wears ? $"{bot.Name} puts on {item.Name}." : $"{bot.Name} puts {item.Name} in their bag."),
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
            return problem == null;
        }

        internal static void Save(OfflineHiredCompanionRecord hired)
        {
            hired.Dirty = true;
            GameServer.Database.SaveObject(hired);
        }
    }
}
