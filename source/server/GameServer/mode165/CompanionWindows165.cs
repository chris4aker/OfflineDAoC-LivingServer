using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.Database;
using DOL.GS.PacketHandler;
using DOL.GS.Styles;

// From shadowofze's True 1.65 v0.7 (f68cc75), via True165, GPL-3.0. Living Server changes: companions' repairs cost the
// player the smith's price under player_upkeep (True 1.65: free) and are recorded in the economy ledger.

namespace DOL.GS
{
    /// <summary>
    /// The companion windows (True 1.65), server side (owner 2026-10-30, plan 17h): gear, stats and XP, repairs. The new
    /// client draws its own windows from these messages (DebugMode 0x21 'H', 128 bytes, Hire165.Send); the original client
    /// uses its native windows (trainer / vault), driven separately. "row" is the companion's roster row 0..6; the
    /// companion must be with the player (summoned).
    ///   op 7 gear item:  [4] row, [5] slot (eInventorySlot, 0xFF = end), [6..7] model, [8..9] colour, [10] item level,
    ///                    [11] quality %, [12] condition %, [13] object type, [14..15] extension, [16..79] name
    ///   op 8 spec line:  [4] row, [5] line index, [6] line count, [7] level, [8] cap, [9..10] points, [11..12] next cost,
    ///                    [13..75] name
    ///   op 9 preview:    [4] row, [5] line, [6] spec level, [7] kind (1 style, 2 spell, 3 ability, 0xFF end), [8..9] icon,
    ///                    [10..72] name
    ///   op 11 realm ability: [4] row, [5] index, [6] count, [7] level, [8] max, [9..10] next cost, [11..12] points,
    ///                    [13..14] icon, [15] realm rank, [16] sub-level, [17..79] name
    ///   op 10 stats:     [4] row, [5] level, [6..7] XP per mille, [8..23] STR..CHA, [24..31] health/max, [32..39] power/max,
    ///                    [40..43] endurance/max, [44..45] AF, [46] absorb %, [47..56] resists (crush slash thrust heat cold
    ///                    matter body spirit energy) %, [57..60] money copper, [61..62] realm points / 1000
    /// Commands (each takes the row first): gear | equip &lt;bag slot&gt; | unequip &lt;slot&gt; | swap &lt;slot&gt; &lt;bag slot&gt; |
    /// move &lt;its slot&gt; &lt;its slot | wear | bag&gt; (inside the companion's own gear and bag) | stats | repair | select | spec |
    /// train &lt;line&gt; | respec.
    /// </summary>
    public static class CompanionWindows165
    {
        private static readonly Logging.Logger StatsLog = Logging.LoggerManager.Create(typeof(CompanionWindows165));

        // Every AI Options verb is routed here too, taken from CompanionOptions165 so the two lists cannot drift apart
        // (2026-11-05: the generic "option" verb was missing here, so every new AI Options row answered "Unknown hiring command").
        public static readonly HashSet<string> Verbs =
            ["gear", "equip", "bag", "unequip", "swap", "move", "stats", "repair", "select", "spec", "respec", "train", "ra", "buyra", "pick", "vault", "delve",
             .. CompanionOptions165.Verbs];

        // ---- the original client's clean list windows (owner 2026-10-30: TRAIN / GEAR / REALM ABILITIES / STATS replace the
        // champion trainer window and the house-vault gear window there). Op 12 = list text [4] slot 0..63, [5..] ASCII;
        // op 13 = clear all list slots. Each reply sends op 13 then the window's slots; "pick r k" acts on the list last sent.

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GamePlayer, string> LastList = new();

        /// <summary>The GEAR window's fixed row order (eInventorySlot numbers), rows 0..17; bag rows 18..27 follow.</summary>
        private static readonly int[] GearRows = [21, 25, 28, 22, 27, 23, 26, 29, 32, 24, 33, 34, 35, 36, 10, 11, 12, 13];

        private static void ListText(GamePlayer player, int slot, string text)
        {
            byte[] body = new byte[123];
            body[0] = (byte)slot;
            byte[] ascii = Encoding.ASCII.GetBytes(text ?? string.Empty);
            Array.Copy(ascii, 0, body, 1, Math.Min(ascii.Length, 121));
            Hire165.Send(player, 12, body);
        }

        private static void BeginList(GamePlayer player, string list)
        {
            LastList.AddOrUpdate(player, list);
            Hire165.Send(player, 13, []);
        }

        private static string Header(GameBot bot) => $"{bot.Name} - {bot.CharacterClass?.Name ?? bot.ClassName} {bot.Level}";

        private static void SendSpecList(GamePlayer player, GameBot bot)
        {
            BeginList(player, "spec");
            ListText(player, 0, Header(bot));
            ListText(player, 1, $"{bot.CompanionSpecPoints} specialization points to spend");
            List<Specialization> lines = Lines(bot);
            for (int k = 0; k < Math.Min(lines.Count, 12); k++)
            {
                Specialization spec = lines[k];
                int next = spec.Level + 1;
                ListText(player, 2 + 3 * k, spec.Name ?? spec.KeyName);
                ListText(player, 3 + 3 * k, $"level {spec.Level}");
                // an autotrain line's free levels cost nothing, as a player's (GameBot.CompanionTrainCost)
                ListText(player, 4 + 3 * k, spec.Level >= bot.Level ? (bot.Level >= 50 ? "max" : $"needs level {next}")
                    : $"next: {bot.CompanionTrainCost(spec)} points");
            }
        }

        private static void SendRaList(GamePlayer player, GameBot bot, OfflineHiredCompanionRecord hired)
        {
            BeginList(player, "ra");
            ListText(player, 0, Header(bot));
            int realmLevel = bot.RealmLevel;
            ListText(player, 1, $"Realm rank {realmLevel / 10 + 1}L{realmLevel % 10} - {RealmAbilityPoints(bot, hired)} realm ability points");
            List<RealmAbilities.RealmAbility> list = RealmAbilityList(bot);
            Dictionary<string, int> chosen = ChosenRealmAbilities(hired);
            for (int k = 0; k < Math.Min(list.Count, 20); k++)
            {
                RealmAbilities.RealmAbility ra = list[k];
                int level = chosen.GetValueOrDefault(ra.KeyName);
                ListText(player, 2 + 2 * k, ra.Name ?? ra.KeyName);
                ListText(player, 3 + 2 * k, level >= ra.MaxLevel ? $"level {level} of {ra.MaxLevel} (max)"
                    : $"level {level} of {ra.MaxLevel} - next costs {ra.CostForUpgrade(level)}");
            }
        }

        private static string GearLine(DbInventoryItem item)
        {
            if (item == null)
                return string.Empty;
            int condition = item.MaxCondition <= 0 ? 100 : (int)Math.Clamp(item.Condition * 100L / item.MaxCondition, 0, 100);
            return $"{item.Name} - quality {item.Quality}%, condition {condition}%";
        }

        private static void SendGearList(GamePlayer player, GameBot bot)
        {
            BeginList(player, "gear");
            ListText(player, 0, Header(bot));
            ListText(player, 1, "TAKE moves an item to your backpack. GIVE ITEMS to hand gear over.");
            for (int k = 0; k < GearRows.Length; k++)
                ListText(player, 2 + k, GearLine(bot.Inventory.GetItem((eInventorySlot)GearRows[k])));
            for (int j = 0; j < HiredCompanions165.BagSlots; j++)
                ListText(player, 20 + j, GearLine(bot.Inventory.GetItem(HiredCompanions165.FirstBagSlot + j)));
        }

        private static void SendStatsList(GamePlayer player, GameBot bot, OfflineHiredCompanionRecord hired)
        {
            BeginList(player, "stats");
            OfflineWorldBotRecord rec = bot.PersistentRecord ?? HiredCompanions165.Record(hired.BotId);
            ListText(player, 0, Header(bot));
            double bubbles = rec == null ? 0 : Hire165.XpPerMille(bot, rec) / 100.0;
            ListText(player, 1, $"Level {bot.Level} - {bubbles:0.0} bubbles");
            int M(eProperty p) => bot.GetModified(p);
            eProperty[] stats = [eProperty.Strength, eProperty.Constitution, eProperty.Dexterity, eProperty.Quickness,
                eProperty.Intelligence, eProperty.Piety, eProperty.Empathy, eProperty.Charisma];
            for (int i = 0; i < stats.Length; i++)
                ListText(player, 2 + i, M(stats[i]).ToString());
            ListText(player, 10, $"{bot.Health} / {bot.MaxHealth}");
            ListText(player, 11, $"{bot.Mana} / {bot.MaxMana}");
            ListText(player, 12, $"{bot.Endurance} / {bot.MaxEndurance}");
            ListText(player, 13, M(eProperty.ArmorFactor).ToString());
            ListText(player, 14, $"{M(eProperty.ArmorAbsorption)}%");
            eProperty[] resists = [eProperty.Resist_Crush, eProperty.Resist_Slash, eProperty.Resist_Thrust, eProperty.Resist_Heat,
                eProperty.Resist_Cold, eProperty.Resist_Matter, eProperty.Resist_Body, eProperty.Resist_Spirit, eProperty.Resist_Energy];
            for (int i = 0; i < resists.Length; i++)
                ListText(player, 15 + i, $"{M(resists[i])}%");
            ListText(player, 24, Money.GetString(rec?.MoneyCopper ?? 0));
            ListText(player, 25, (rec?.RealmPoints ?? 0).ToString("N0"));
            ListText(player, 26, $"{bot.RealmLevel / 10 + 1}L{bot.RealmLevel % 10}");
        }

        /// <summary>"pick r k" in the original client's list windows: acts on the list last sent to this player.</summary>
        private static string Pick(GamePlayer player, GameBot bot, OfflineHiredCompanionRecord hired, int k)
        {
            LastList.TryGetValue(player, out string list);
            return list switch
            {
                "spec" => Train(bot, k),
                "ra" => BuyRealmAbility(bot, hired, k),
                "gear" when k is >= 0 and < 18 => Unequip(player, bot, GearRows[k]),
                "gear" when k is >= 18 and < 28 => Unequip(player, bot, (int)HiredCompanions165.FirstBagSlot + (k - 18)),
                _ => "Choose a row first.",
            };
        }

        public static void Handle(GamePlayer player, string[] args)
        {
            string verb = args[1].ToLowerInvariant();
            if (args.Length < 3 || !int.TryParse(args[2], out int row))
            {
                Problem(player, "Choose a companion first.");
                return;
            }
            OfflineHiredCompanionRecord hired = HiredCompanions165.RosterOf(player).FirstOrDefault(h => h.Row == row);
            GameBot bot = hired == null ? null : HiredCompanions165.LiveBot(hired.BotId);
            if (verb is "delve")
            {
                SendDelve(player, bot, hired, args);   // always answered: the new client matches replies in order
                return;
            }
            if (CompanionOptions165.Verbs.Contains(verb))
            {
                // the Pets / Songs tab (reset-day S33 / C17): apply the choice, then send the rows again
                Problem(player, CompanionOptions165.Handle(bot, hired, verb, args) ?? string.Empty);
                if (bot != null)
                    CompanionOptions165.Send(player, row, bot);
                return;
            }
            if (verb is "select" && hired != null)
            {
                // the original client's roster click: remember it, mark it, show its stats (owner layout, overlay 0.14)
                if (Hire165.Get(player) is { } session)
                    session.Selected = row;
                Hire165.Refresh(player);
                if (bot != null)
                    SendStats(player, row, bot);   // op 10: the companion window's XP bar (overlay 0.18)
                SendStatsText(player, row, hired, bot);
                return;
            }
            if (bot == null)
            {
                Problem(player, hired == null ? "There is no companion in that row." : "Summon them first: they are waiting (dismissed).");
                return;
            }
            int Arg(int i) => args.Length > i && int.TryParse(args[i], out int v) ? v : -1;
            string problem = verb switch
            {
                "gear" => null,
                "vault" => null,
                "pick" => Pick(player, bot, hired, Arg(3)),
                "equip" => Equip(player, bot, Arg(3), Arg(4)),
                "bag" => PutInBag(player, bot, Arg(3), Arg(4)),
                "unequip" => Unequip(player, bot, Arg(3)),
                // onto a worn slot: Equip hands the piece worn there back into the backpack slot the new one leaves (it
                // used to take the worn piece off first, which left it off when the new one went elsewhere or to the bag)
                "swap" => Equip(player, bot, Arg(4), Arg(3)),
                "move" => MoveOwn(player, bot, Arg(3), args.Length > 4 ? args[4] : null),
                "stats" => null,
                "repair" => AskRepair(player, [bot]),
                "train" => Train(bot, Arg(3)),
                "buyra" => BuyRealmAbility(bot, hired, Arg(3)),
                "respec" => Respec(bot),
                _ => null,
            };
            Problem(player, problem ?? string.Empty);
            // "pick" acts on the list last sent and answers with that list again
            if (verb is "pick")
                verb = LastList.TryGetValue(player, out string last) ? last switch { "spec" => "train", "ra" => "buyra", _ => last } : "gear";
            CompanionOptions165.Send(player, row, bot);   // op 14: the Pets / Songs tab rows, with every window fill
            if (verb is "stats")
            {
                SendStats(player, row, bot);
                SendStatsText(player, row, hired, bot);
                SendStatsList(player, bot, hired);
            }
            else if (verb is "spec" or "train" or "respec")
            {
                SendSpec(player, row, bot, preview: verb == "spec");   // ops 8/9: the new client
                SendStats(player, row, bot);
                SendStatsText(player, row, hired, bot);
                SendSpecList(player, bot);                             // ops 12/13: the original client
            }
            else if (verb is "ra" or "buyra")
            {
                SendRealmAbilities(player, row, bot, hired);
                SendStatsText(player, row, hired, bot);
                SendRaList(player, bot, hired);
            }
            else if (verb is "vault")
                CompanionGearVault165.Open(player, row, bot);   // GIVE ITEMS: the drag window (no longer opened by "gear")
            else if (verb is not "repair")
            {
                SendGear(player, row, bot);
                SendGearList(player, bot);
            }
            if (verb is "train" or "respec")
                Hire165.Refresh(player);
        }

        // The hire session keeps its own message and sends it again on every refresh, so it follows this one: a stale
        // "Unknown hiring command." came back after a successful TRAIN (2026-11-05).
        private static void Problem(GamePlayer player, string text)
        {
            if (Hire165.Get(player) is { } session)
                session.Problem = text;
            Hire165.Text(player, 8, text);
        }

        /// <summary>
        /// The new client's hover tooltips in the companion window (owner 2026-11-05, reset-day C15): "delve &lt;row&gt; item
        /// &lt;slot&gt;" answers with the player's own item delve of the companion's item in that slot, "delve &lt;row&gt; style
        /// &lt;line&gt; &lt;level&gt;" with the player style delve (endurance cost, threat, opening...) of the style the TRAIN
        /// preview lists at that line (ops 8/9 line index) and level. Every request gets a window, empty when there is
        /// nothing to show, because the client pairs replies with requests in order.
        /// "delve &lt;row&gt; ra &lt;index&gt;" (owner 2026-11-06: no tooltip on a companion's realm abilities): the realm
        /// ability at that index of the op 11 list, captioned with its name - the player trainer's delve text (description,
        /// per-level values and costs) for the companion's class, then the companion's own level, next cost and points.
        /// </summary>
        private static void SendDelve(GamePlayer player, GameBot bot, OfflineHiredCompanionRecord hired, string[] args)
        {
            string caption = string.Empty;
            var lines = new List<string>();
            string kind = args.Length > 3 ? args[3].ToLowerInvariant() : string.Empty;
            int first = args.Length > 4 && int.TryParse(args[4], out int a) ? a : -1;
            int second = args.Length > 5 && int.TryParse(args[5], out int b) ? b : -1;
            try
            {
                if (bot != null && kind == "item" && first >= 0 && bot.Inventory?.GetItem((eInventorySlot)first) is DbInventoryItem item)
                {
                    caption = item.Name;
                    player.DelveItem(item, lines);
                }
                else if (bot != null && kind == "style")
                {
                    List<Specialization> specs = Lines(bot);
                    if (first >= 0 && first < specs.Count &&
                        AllUnlocks(bot, specs[first]).Where(u => u.Skill is Style && u.Level == second)
                            .Select(u => (Style)u.Skill).FirstOrDefault() is { } style)
                    {
                        caption = style.Name;
                        DOL.GS.PacketHandler.Client.v168.DetailDisplayHandler.WriteStyleInfo(lines, style, player.Client);
                    }
                }
                else if (bot != null && kind == "spell" && first > 0 && SkillBase.GetSpellByID(first) is { } spell)
                {
                    // a spell in the AI Options rows (owner 2026-11-08: tooltips on songs, pets, sub-pets and debuffs),
                    // delved as this companion casts it; a Necromancer's servant command shows the servant's spell too
                    caption = spell.Name;
                    SpellLine spellLine = SkillBase.GetSpellLine(GlobalSpellsLines.Reserved_Spells);
                    if (ScriptMgr.CreateSpellHandler(bot, spell, spellLine) is { } handler)
                        lines.AddRange(handler.DelveInfo);
                    if (spell.SubSpellID > 0 && SkillBase.GetSpellByID(spell.SubSpellID) is { } payload &&
                        ScriptMgr.CreateSpellHandler(bot, payload, spellLine) is { } payloadHandler)
                    {
                        lines.Add(" ");
                        lines.AddRange(payloadHandler.DelveInfo);
                    }
                }
                else if (bot != null && hired != null && kind == "ra")
                {
                    List<RealmAbilities.RealmAbility> list = RealmAbilityList(bot);
                    if (first >= 0 && first < list.Count)
                    {
                        RealmAbilities.RealmAbility ra = list[first];
                        caption = ra.Name ?? ra.KeyName;   // as op 11 names it: the client keys the answer by it
                        if (ra.DelveInfo is { Count: > 0 } info)
                            lines.AddRange(info);
                        int level = ChosenRealmAbilities(hired).GetValueOrDefault(ra.KeyName);
                        lines.Add(string.Empty);
                        lines.Add(RealmAbilityStatus(bot.Name, level, ra.MaxLevel,
                            level >= ra.MaxLevel ? 0 : ra.CostForUpgrade(level), RealmAbilityPoints(bot, hired)));
                    }
                }
            }
            catch (Exception ex)
            {
                StatsLog.Warn($"COMPANION_DELVE_165 player={player.Name} args=\"{string.Join(' ', args)}\" failed: {ex.Message}");
                lines.Clear();
            }
            player.Out.SendCustomTextWindow(caption, lines);
        }

        // ---- gear

        public static void SendGear(GamePlayer player, int row, GameBot bot)
        {
            foreach (DbInventoryItem item in bot.Inventory.AllItems
                         .Where(i => i != null && (i.SlotPosition is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable ||
                                                   HiredCompanions165.IsBagSlot(i.SlotPosition)))
                         .OrderBy(i => i.SlotPosition))
            {
                var body = new byte[76];
                body[0] = (byte)row;
                body[1] = (byte)Math.Min(item.SlotPosition, 0xFE);
                body[2] = (byte)item.Model; body[3] = (byte)(item.Model >> 8);
                body[4] = (byte)item.Color; body[5] = (byte)(item.Color >> 8);
                body[6] = (byte)Math.Clamp(item.Level, 0, 255);
                body[7] = (byte)Math.Clamp(item.Quality, 0, 100);
                body[8] = (byte)(item.MaxCondition <= 0 ? 100 : Math.Clamp(item.Condition * 100L / item.MaxCondition, 0, 100));
                body[9] = (byte)item.Object_Type;
                body[10] = (byte)item.Extension; body[11] = (byte)(item.Extension >> 8);
                byte[] name = Encoding.ASCII.GetBytes(item.Name ?? string.Empty);
                Array.Copy(name, 0, body, 12, Math.Min(name.Length, 61));   // [12..72], [73] stays 0
                // the stack count (owner 2026-10-30: how much ammo / poison / thrown weapons a companion has left)
                int count = Math.Clamp(item.Count, 0, ushort.MaxValue);
                body[74] = (byte)count; body[75] = (byte)(count >> 8);
                Hire165.Send(player, 7, body);
            }
            Hire165.Send(player, 7, [(byte)row, 0xFF]);
        }

        /// <summary>From the player's backpack onto the companion (what it was wearing there goes to the player).</summary>
        internal static string Equip(GamePlayer player, GameBot bot, int bagSlot, int wantedSlot = -1)
        {
            if (bagSlot is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack)
                return "Drag an item from your backpack.";
            DbInventoryItem item = player.Inventory.GetItem((eInventorySlot)bagSlot);
            if (item == null)
                return "That backpack slot is empty.";
            // No-trade and quest items move freely between a player and their own companions (owner 2026-11-01: Leggings
            // of the Deft "can't be given away"); trading with anyone else keeps its normal rules.
            // what it can't wear goes into its bag (arrows, poisons, potions...); "bag" puts a wearable piece there too
            if (!AutonomousBotEconomy.CanWear(bot, item, out eInventorySlot slot))
            {
                // a weapon, shield or armour piece it can't use: said, not just bagged (tester 0.3: "won't equip")
                bool gear = item.Item_Type is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable;
                string bagged = PutInBag(player, bot, bagSlot);
                if (gear && bagged == null)
                {
                    StatsLog.Info($"COMPANION_EQUIP_REFUSED bot={bot.Name} cls={bot.CharacterClass?.ID} lvl={bot.Level} item={item.Id_nb} type={item.Object_Type} size={item.Type_Damage} realm={item.Realm} shield={bot.GetAbilityLevel(Abilities.Shield)}");
                    player.Out.SendMessage($"{bot.Name} can't use {item.Name}, so it went into {bot.Name}'s bag.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                }
                return bagged;
            }
            // the slot the player dropped it on, when it can go there (a dual wielder's second weapon, a ring's hand);
            // otherwise the slot it belongs in (owner 2026-11-01: "not the slot I told it to")
            if (AutonomousBotEconomy.FitsChosenSlot(bot, item, wantedSlot))
                slot = (eInventorySlot)wantedSlot;
            // what it wore there takes the backpack slot the new item leaves
            DbInventoryItem worn = bot.Inventory.GetItem(slot);
            lock (player.Inventory.Lock)
            {
                if (!player.Inventory.RemoveItemWithoutDbDeletion(item))
                    return $"Could not hand over {item.Name}.";
                if (worn != null)
                {
                    bot.Inventory.RemoveItemWithoutDbDeletion(worn);
                    worn.OwnerID = player.InternalID;
                    worn = ForPlayer(worn);
                    if (!player.Inventory.AddItemWithoutDbAddition((eInventorySlot)bagSlot, worn))
                        player.Inventory.AddItemWithoutDbAddition(player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack), worn);
                }
                if (!bot.Inventory.AddItem(slot, item))
                {
                    item.OwnerID = player.InternalID;
                    player.Inventory.AddItemWithoutDbAddition((eInventorySlot)bagSlot, item);
                    return $"{bot.Name} couldn't take {item.Name}.";
                }
            }
            Saved(player, bot, item, worn);
            ReadyWeapons(bot, slot);
            ShieldNote(player, bot, item);
            return null;
        }

        /// <summary>A shield worn but not in use: why (a companion trained in two-handed weapons keeps them, owner 2026-11-07).</summary>
        private static void ShieldNote(GamePlayer player, GameBot bot, DbInventoryItem item)
        {
            if (item.Object_Type == (int)eObjectType.Shield && bot.ActiveWeaponSlot != eActiveWeaponSlot.Standard)
                player.Out.SendMessage(BotWeaponStats.CanUseMelee(bot, bot.Inventory.GetItem(eInventorySlot.RightHandWeapon))
                    ? $"{bot.Name} wears {item.Name} but keeps fighting with its two-handed weapon, the one it is trained in more."
                    : $"{bot.Name} wears {item.Name}, but needs a one-handed weapon to fight with a shield.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        /// <summary>From the player's backpack into the companion's 10-slot bag (a chosen empty bag slot, or the first).</summary>
        internal static string PutInBag(GamePlayer player, GameBot bot, int bagSlot, int companionBagSlot = -1)
        {
            if (bagSlot is < (int)eInventorySlot.FirstBackpack or > (int)eInventorySlot.LastBackpack)
                return "Drag an item from your backpack.";
            DbInventoryItem item = player.Inventory.GetItem((eInventorySlot)bagSlot);
            if (item == null)
                return "That backpack slot is empty.";
            // no-trade and quest items too: the companion is the player's own (owner 2026-11-01)
            eInventorySlot target = HiredCompanions165.IsBagSlot(companionBagSlot) && bot.Inventory.GetItem((eInventorySlot)companionBagSlot) == null
                ? (eInventorySlot)companionBagSlot
                : bot.Inventory.FindFirstEmptySlot(HiredCompanions165.FirstBagSlot, HiredCompanions165.LastBagSlot);
            if (target == eInventorySlot.Invalid)
                return $"{bot.Name}'s bag is full ({HiredCompanions165.BagSlots} slots).";
            lock (player.Inventory.Lock)
            {
                if (!player.Inventory.RemoveItemWithoutDbDeletion(item))
                    return $"Could not hand over {item.Name}.";
                if (!bot.Inventory.AddItem(target, item))
                {
                    item.OwnerID = player.InternalID;
                    player.Inventory.AddItemWithoutDbAddition((eInventorySlot)bagSlot, item);
                    return $"{bot.Name} couldn't take {item.Name}.";
                }
            }
            Saved(player, bot, item, null);
            return null;
        }

        /// <summary>From the companion back into the player's backpack.</summary>
        internal static string Unequip(GamePlayer player, GameBot bot, int slot, int toBagSlot = -1)
        {
            DbInventoryItem item = slot < 0 ? null : bot.Inventory.GetItem((eInventorySlot)slot);
            if (item == null)
                return "Nothing there.";
            eInventorySlot free = toBagSlot >= (int)eInventorySlot.FirstBackpack && toBagSlot <= (int)eInventorySlot.LastBackpack &&
                                  player.Inventory.GetItem((eInventorySlot)toBagSlot) == null
                ? (eInventorySlot)toBagSlot
                : player.Inventory.FindFirstEmptySlot(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack);
            if (free == eInventorySlot.Invalid)
                return "Your backpack is full.";
            lock (player.Inventory.Lock)
            {
                if (!bot.Inventory.RemoveItemWithoutDbDeletion(item))
                    return $"Could not take {item.Name}.";
                item.OwnerID = player.InternalID;
                item = ForPlayer(item);
                if (!player.Inventory.AddItemWithoutDbAddition(free, item))
                {
                    bot.Inventory.AddItem((eInventorySlot)slot, item);
                    return $"Could not take {item.Name}.";
                }
            }
            Saved(player, bot, null, item);
            ReadyWeapons(bot, (eInventorySlot)slot);
            return null;
        }

        // ---- inside the companion's own gear and bag (issue #50: "Companions can't equip items when dragged from their own
        // bag to the rag doll", nor take them off into their bag - everything had to go through the player's backpack)

        /// <summary>"move" targets that are not a slot: put it on where it belongs (a drop on the figure), or into the first
        /// free bag slot (a drop on the bag panel).</summary>
        public const int MoveToWear = -1, MoveToBag = -2;

        /// <summary>A slot of the companion's paper doll (the weapons, armour and jewellery the window shows).</summary>
        public static bool IsDollSlot(int slot) => slot is >= 10 and <= 13 or >= 21 and <= 29 or >= 32 and <= 36;

        /// <summary>What a "move" does: the slot the item ends in (below 0 with no problem: nothing to do) and whether
        /// the item there swaps into the slot it left; or the plain refusal.</summary>
        public readonly record struct OwnMove(string Problem, int Target, bool Swap);

        /// <summary>
        /// The decision of a move inside a companion's own inventory, with the same wear rules as "equip": a bag item put
        /// on needs <paramref name="canWear"/> (AutonomousBotEconomy.CanWear: class, level, weapon skill) and goes on the
        /// dropped-on slot when <paramref name="fitsSlot"/> (FitsChosenSlot: hands, two-hander, shield, rings) allows it,
        /// otherwise where it belongs (<paramref name="wearSlot"/>); what was worn there takes the bag slot it left. A worn
        /// item put on another doll slot must fit it. An item displaced into a doll slot must fit that slot
        /// (<paramref name="occupantFitsFrom"/>); one that doesn't, from a bag row, leaves the worn item to the first free
        /// bag slot instead. <paramref name="occupied"/> tells which slots hold an item.
        /// </summary>
        public static OwnMove PlanOwnMove(int from, int to, bool alive, Func<int, bool> occupied, bool canWear, int wearSlot,
            Func<int, bool> fitsSlot, Func<int, bool> occupantFitsFrom, string botName, string itemName)
        {
            static OwnMove No(string problem) => new(problem, -1, false);
            if (!alive)
                return No($"Revive {botName} first.");
            bool fromBag = HiredCompanions165.IsBagSlot(from);
            if (!fromBag && !IsDollSlot(from))
                return No($"Drag one of {botName}'s items.");
            if (!occupied(from))
                return No("Nothing there.");
            if (from == to || fromBag && to == MoveToBag || !fromBag && to == MoveToWear)
                return No(null);   // where it already is
            OwnMove IntoBag()
            {
                for (int s = (int)HiredCompanions165.FirstBagSlot; s <= (int)HiredCompanions165.LastBagSlot; s++)
                    if (!occupied(s))
                        return new(null, s, false);
                return No($"{botName}'s bag is full ({HiredCompanions165.BagSlots} slots).");
            }
            if (HiredCompanions165.IsBagSlot(to))
            {
                if (fromBag || !occupied(to))
                    return new(null, to, occupied(to));
                // a worn item onto a bag row that holds something: they swap when that one can be worn here instead
                return occupantFitsFrom(to) ? new(null, to, true) : IntoBag();
            }
            if (to == MoveToBag)
                return IntoBag();
            if (to != MoveToWear && !IsDollSlot(to))
                return No($"Drop it on {botName}'s gear or bag.");
            if (!canWear)
                return No($"{botName} can't use {itemName}.");
            if (fromBag)
            {
                // as "equip": the slot it was dropped on when it can go there, otherwise the slot it belongs in
                int target = to != MoveToWear && fitsSlot(to) ? to : wearSlot;
                if (!IsDollSlot(target))
                    return No($"{botName} can't use {itemName}.");
                return new(null, target, occupied(target));
            }
            if (!fitsSlot(to))
                return No($"{itemName} doesn't go there.");
            if (occupied(to) && !occupantFitsFrom(to))
                return No("Those two can't trade places.");
            return new(null, to, occupied(to));
        }

        /// <summary>"move": from one of the companion's slots to another of its own (bag to gear, gear to bag, bag row to
        /// bag row, hand to hand), a swap when the target holds an item; only its hirer, while it is alive and with him.</summary>
        internal static string MoveOwn(GamePlayer player, GameBot bot, int from, string to)
        {
            int toSlot = to?.ToLowerInvariant() switch
            {
                "wear" => MoveToWear,
                "bag" => MoveToBag,
                _ => int.TryParse(to, out int v) ? v : int.MinValue,
            };
            DbInventoryItem item = HiredCompanions165.IsBagSlot(from) || IsDollSlot(from) ? bot.Inventory.GetItem((eInventorySlot)from) : null;
            eInventorySlot wearSlot = eInventorySlot.Invalid;
            bool canWear = item != null && AutonomousBotEconomy.CanWear(bot, item, out wearSlot);
            OwnMove plan = PlanOwnMove(from, toSlot, bot.IsAlive, s => bot.Inventory.GetItem((eInventorySlot)s) != null, canWear,
                (int)wearSlot, s => AutonomousBotEconomy.FitsChosenSlot(bot, item, s),
                s => bot.Inventory.GetItem((eInventorySlot)s) is { } other && AutonomousBotEconomy.CanWear(bot, other, out _) &&
                     AutonomousBotEconomy.FitsChosenSlot(bot, other, from),
                bot.Name, item?.Name);
            if (plan.Problem != null || plan.Target < 0)
                return plan.Problem;
            if (!bot.Inventory.MoveItem((eInventorySlot)from, (eInventorySlot)plan.Target, item.Count))
                return $"Could not move {item.Name}.";
            Saved(player, bot, null, null);
            ReadyWeapons(bot, (eInventorySlot)from);
            ReadyWeapons(bot, (eInventorySlot)plan.Target);
            if (IsDollSlot(plan.Target))
                ShieldNote(player, bot, item);
            return null;
        }

        /// <summary>An item coming back from a companion as a player item (GameInventoryItem), so it wears on the player
        /// at once instead of only after a relog (audit 2026-10-31).</summary>
        private static DbInventoryItem ForPlayer(DbInventoryItem item) => item as GameInventoryItem ?? GameInventoryItem.Create(item);

        /// <summary>
        /// After the player changed a weapon slot, the companion fights with what it was just given: a two-hander given
        /// makes it use the two-hander, a one-hander or shield given the weapon-and-shield set; a set taken away falls
        /// back to the other one. A bow or instrument in use (a pull, a song) is left as it is; the weapon set is
        /// re-selected so the cached weapon is the one now in the slot.
        /// </summary>
        private static void ReadyWeapons(GameBot bot, eInventorySlot changed)
        {
            if (bot?.Inventory == null || changed is not (eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon or
                    eInventorySlot.TwoHandWeapon or eInventorySlot.DistanceWeapon))
                return;
            bool oneHand = BotWeaponStats.CanUseMelee(bot, bot.Inventory.GetItem(eInventorySlot.RightHandWeapon));
            bool twoHand = BotWeaponStats.CanUseMelee(bot, bot.Inventory.GetItem(eInventorySlot.TwoHandWeapon));
            if (bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance)
            {
                if (changed != eInventorySlot.DistanceWeapon)
                    return;
                // its bow taken away mid-pull: back to its melee weapons rather than a ranged stance with nothing
                bot.SwitchWeapon(bot.Inventory.GetItem(eInventorySlot.DistanceWeapon) != null ? eActiveWeaponSlot.Distance
                    : !oneHand && twoHand ? eActiveWeaponSlot.TwoHanded : eActiveWeaponSlot.Standard);
                return;
            }
            eActiveWeaponSlot want = bot.ActiveWeaponSlot;
            if (changed == eInventorySlot.TwoHandWeapon && twoHand)
                want = eActiveWeaponSlot.TwoHanded;
            else if (changed is eInventorySlot.RightHandWeapon or eInventorySlot.LeftHandWeapon && oneHand)
                want = eActiveWeaponSlot.Standard;
            else if (want == eActiveWeaponSlot.TwoHanded && !twoHand && oneHand)
                want = eActiveWeaponSlot.Standard;
            else if (want == eActiveWeaponSlot.Standard && !oneHand && twoHand)
                want = eActiveWeaponSlot.TwoHanded;
            // (True 1.65 also kept a two-hander the companion is less trained in on its back and remembered the set for its
            // BotWeaponSetPolicy; Living Server's bots choose their weapon set by their own rules in each fight, so only
            // the switch is ported.)
            if (changed != eInventorySlot.DistanceWeapon)
                bot.SwitchWeapon(want);
        }

        private static void Saved(GamePlayer player, GameBot bot, DbInventoryItem given, DbInventoryItem returned)
        {
            if (returned != null)
            {
                returned.Dirty = true;
                GameServer.Database.SaveObject(returned);
            }
            AutonomousBotEconomy.MoveStrayOffhandWeapon(bot);
            bot.RefreshItemBonuses();
            bot.BroadcastLivingEquipmentUpdate();
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            player.Out.SendInventoryItemsUpdate(eInventoryWindowType.Update, player.Inventory.AllItems
                .Where(i => i.SlotPosition is >= (int)eInventorySlot.FirstBackpack and <= (int)eInventorySlot.LastBackpack).ToList());
        }

        // ---- specs (the player trains them; RESPEC is free)

        // ---- the original client's native trainer window (the champion window type), driven for a companion

        /// <summary>Window ids from here up are companions (base + roster row); the champion click handler sends their
        /// clicks to <see cref="OnTrainerClick"/>.</summary>
        public const int TrainerWindowBase = 200;

        // The original (1.127) client copies this window into a fixed buffer with no bounds checks: at most 10 sublines of
        // 10 items each, numbered 1..10, or it crashes (tested in the original client, 2026-10-30: TRAIN crashed it). Every item
        // goes out as kind 1 (ability): for kinds 2/3 the client reads the icon word as an index into its own style/spell
        // tables.
        private const int NativeMaxLines = 10, NativeMaxItems = 10;
        private const byte NativeItemKind = 1;

        /// <summary>
        /// Opens the native trainer window for one companion: each spec line is a row, each style/spell/ability it gives
        /// an item, marked trained (1), trainable now (2) or not yet (0); the points byte is the companion's spec points.
        /// Clicking an item trains its line up to the level the item needs, if the points allow.
        /// </summary>
        public static void SendTrainerWindow(GamePlayer player, int row, GameBot bot)
        {
            List<Specialization> lines = Lines(bot);
            int points = bot.CompanionSpecPoints;
            using var pak = PooledObjectFactory.GetForTick<GSTCPPacketOut>().Init((byte)eServerPackets.TrainerWindow);
            pak.WriteByte((byte)(TrainerWindowBase + row));
            pak.WriteByte((byte)Math.Min(points, 255));
            pak.WriteByte(2);   // champion window type
            pak.WriteByte(0);
            int lineCount = Math.Min(lines.Count, NativeMaxLines);
            pak.WriteByte((byte)lineCount);
            for (int i = 0; i < lineCount; i++)
            {
                List<(Skill Skill, int Level)> items = VisibleUnlocks(bot, lines[i]);
                pak.WriteByte((byte)(i + 1));
                pak.WriteByte((byte)items.Count);
                for (int j = 0; j < items.Count; j++)
                {
                    (Skill skill, int level) = items[j];
                    pak.WriteByte((byte)(j + 1));
                    pak.WriteByte(NativeItemKind);
                    pak.WriteShortLowEndian(skill.Icon);
                    pak.WritePascalString($"{skill.Name} ({level})");
                    pak.WriteByte(lines[i].Level >= level ? (byte)1 : bot.CompanionTrainCost(lines[i], level) <= points && level <= bot.Level ? (byte)2 : (byte)0);
                    pak.WriteByte(0);   // no attached skills
                }
            }
            player.Out.SendTCP(pak);
        }

        /// <summary>A click in the companion trainer window: train that line up to the clicked item's level.</summary>
        public static void OnTrainerClick(GamePlayer player, int windowId, int line, int item)
        {
            int row = windowId - TrainerWindowBase;
            OfflineHiredCompanionRecord hired = HiredCompanions165.RosterOf(player).FirstOrDefault(h => h.Row == row);
            GameBot bot = hired == null ? null : HiredCompanions165.LiveBot(hired.BotId);
            if (bot == null)
                return;
            List<Specialization> lines = Lines(bot);
            if (line < 0 || line >= lines.Count)
                return;
            List<(Skill Skill, int Level)> items = VisibleUnlocks(bot, lines[line]);
            if (item < 0 || item >= items.Count)
                return;
            int target = items[item].Level;
            string problem = null;
            while (lines[line].Level < target && problem == null)
                problem = bot.CompanionTrain(lines[line]);
            player.Out.SendMessage(problem ?? $"{bot.Name} trains {lines[line].Name} to {lines[line].Level}.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
            SendTrainerWindow(player, row, bot);
            SendStatsText(player, row, hired, bot);
            Hire165.Refresh(player);
        }

        /// <summary>The 10 unlocks of a line the native window shows: the last one trained, then the next ones (clicks map
        /// back through this same list).</summary>
        private static List<(Skill Skill, int Level)> VisibleUnlocks(GameBot bot, Specialization spec)
        {
            List<(Skill Skill, int Level)> all = Unlocks(bot, spec);
            int firstUntrained = all.FindIndex(e => e.Level > spec.Level);
            int start = firstUntrained < 0 ? all.Count - NativeMaxItems : firstUntrained - 1;
            start = Math.Clamp(start, 0, Math.Max(0, all.Count - NativeMaxItems));
            return all.Skip(start).Take(NativeMaxItems).ToList();
        }

        /// <summary>What a line gives up to level 50, by the spec level each needs (styles, spells, abilities): the original
        /// client's native trainer window (unchanged, owner 2026-10-30).</summary>
        private static List<(Skill Skill, int Level)> Unlocks(GameBot bot, Specialization spec)
        {
            var list = new List<(Skill Skill, int Level)>();
            try
            {
                foreach (Style style in spec.PretendStylesForLiving(bot, 50))
                    list.Add((style, style.SpecLevelRequirement));
                foreach (var line in spec.PretendLinesSpellsForLiving(bot, 50))
                    foreach (Skill skill in line.Value)
                        list.Add((skill, skill.Level));
                foreach (Ability ability in spec.PretendAbilitiesForLiving(bot, 50))
                    list.Add((ability, ability.Level));
            }
            catch (Exception)
            {
            }
            return list.Where(e => e.Level > 0).OrderBy(e => e.Level).Take(60).ToList();
        }

        /// <summary>Everything a line gives from 1 to 50, for the companion TRAIN tab (op 9). For a bot the game's own
        /// "pretend" spell lists stop at its current level and its ability list ignores its class, so the line's whole spell
        /// list and the class's spec abilities are read directly (owner 2026-10-30: "should show all the 1-50 for each line
        /// ... so you can see what comes later"). Display only; the bot's own spell choice is untouched.</summary>
        private static List<(Skill Skill, int Level, bool Baseline)> AllUnlocks(GameBot bot, Specialization spec)
        {
            var list = new List<(Skill Skill, int Level, bool Baseline)>();
            try
            {
                foreach (Style style in spec.PretendStylesForLiving(bot, 50))
                    list.Add((style, style.SpecLevelRequirement, false));
                // A baseline spell list comes with character level, not with points in the line (owner 2026-10-31: show
                // them apart, greyed, so it is clear what training a line actually unlocks).
                foreach (SpellLine line in spec.PretendLinesSpellsForLiving(bot, 50).Keys)
                    foreach (Spell spell in SkillBase.GetSpellList(line.KeyName).Where(sp => sp.Level is > 0 and <= 50))
                        if (!list.Any(e => e.Skill is Spell known && known.ID == spell.ID))
                            list.Add((spell, spell.Level, line.IsBaseLine));
                foreach (Ability ability in SkillBase.GetSpecAbilityList(spec.KeyName, bot.CharacterClass?.ID ?? 0)
                             .Where(a => a.SpecLevelRequirement is > 0 and <= 50))
                    list.Add((ability, ability.SpecLevelRequirement, false));
            }
            catch (Exception)
            {
                // a line without previews still trains
            }
            return list.Where(e => e.Level > 0).OrderBy(e => e.Level).ToList();
        }

        // ---- realm abilities: earned from group kills with the player, chosen by the player (owner 2026-10-30)

        /// <summary>Window ids 230 + row are a companion's realm abilities in the original client's trainer window.</summary>
        public const int RealmAbilityWindowBase = 230;

        /// <summary>Every realm ability the companion's class can have (not the realm rank 5 one).</summary>
        public static List<RealmAbilities.RealmAbility> RealmAbilityList(GameBot bot) =>
            SkillBase.GetClassRealmAbilities(bot.CharacterClass.ID)
                .Where(ra => ra is not RealmAbilities.RR5RealmAbility)
                .GroupBy(ra => ra.KeyName).Select(g => g.First())
                .OrderBy(ra => ra.Name, StringComparer.Ordinal).ToList();

        public static Dictionary<string, int> ChosenRealmAbilities(OfflineHiredCompanionRecord hired) =>
            (hired.RealmAbilities ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Split(':')).Where(p => p.Length == 2 && int.TryParse(p[1], out _))
                .GroupBy(p => p[0]).ToDictionary(g => g.Key, g => int.Parse(g.Last()[1]));

        private static int SpentRealmPoints(GameBot bot, Dictionary<string, int> chosen)
        {
            int spent = 0;
            foreach (RealmAbilities.RealmAbility ra in RealmAbilityList(bot))
                for (int level = 0; level < chosen.GetValueOrDefault(ra.KeyName); level++)
                    spent += ra.CostForUpgrade(level);
            return spent;
        }

        public static int RealmAbilityPoints(GameBot bot, OfflineHiredCompanionRecord hired) =>
            Math.Max(0, AutonomousBotRealmAbilities.Points(bot.Level, bot.RealmLevel) - SpentRealmPoints(bot, ChosenRealmAbilities(hired)));

        /// <summary>Gives a summoned companion the realm abilities its player chose.</summary>
        public static void ApplyRealmAbilities(GameBot bot, OfflineHiredCompanionRecord hired)
        {
            foreach ((string key, int level) in ChosenRealmAbilities(hired))
                if (level > 0 && SkillBase.GetAbility(key, level) is RealmAbilities.RealmAbility ability &&
                    (bot.GetAbility(key)?.Level ?? 0) < level)
                    bot.AddAbility(ability, false);
            AutonomousBotRealmAbilities.RefreshActives(bot);   // the actives it was given are used, not only granted
        }

        /// <summary>Pure: the companion's line under a realm ability's delve: its level, and the next level's cost.</summary>
        public static string RealmAbilityStatus(string name, int level, int maxLevel, int nextCost, int points) =>
            level >= maxLevel
                ? $"{name} has it at level {level} of {maxLevel} (the highest)."
                : $"{name} has it at level {level} of {maxLevel}. Level {level + 1} costs {nextCost} realm ability points; " +
                  $"{name} has {points}.";

        private static string BuyRealmAbility(GameBot bot, OfflineHiredCompanionRecord hired, int index)
        {
            List<RealmAbilities.RealmAbility> list = RealmAbilityList(bot);
            if (index < 0 || index >= list.Count)
                return "Choose a realm ability.";
            RealmAbilities.RealmAbility ra = list[index];
            Dictionary<string, int> chosen = ChosenRealmAbilities(hired);
            int level = chosen.GetValueOrDefault(ra.KeyName);
            if (level >= ra.MaxLevel)
                return $"{ra.Name} is at its highest level.";
            int cost = ra.CostForUpgrade(level);
            int points = RealmAbilityPoints(bot, hired);
            if (points < cost)
                return $"{ra.Name} {level + 1} costs {cost} realm ability points; {bot.Name} has {points}.";
            chosen[ra.KeyName] = level + 1;
            hired.RealmAbilities = string.Join(';', chosen.Select(kv => $"{kv.Key}:{kv.Value}"));
            HiredCompanions165.Save(hired);
            ApplyRealmAbilities(bot, hired);
            bot.MarkAutonomousStateDirty();
            AutonomousBotStatusPersistence.Queue(bot, true);
            return null;
        }

        /// <summary>op 11 per realm ability (new client) and the native trainer window, id 230 + row (original client).</summary>
        public static void SendRealmAbilities(GamePlayer player, int row, GameBot bot, OfflineHiredCompanionRecord hired)
        {
            List<RealmAbilities.RealmAbility> list = RealmAbilityList(bot);
            Dictionary<string, int> chosen = ChosenRealmAbilities(hired);
            int points = RealmAbilityPoints(bot, hired);
            byte rank = (byte)(bot.RealmLevel / 10 + 1), sub = (byte)(bot.RealmLevel % 10);
            for (int i = 0; i < list.Count; i++)
            {
                RealmAbilities.RealmAbility ra = list[i];
                int level = chosen.GetValueOrDefault(ra.KeyName);
                int cost = level >= ra.MaxLevel ? 0 : ra.CostForUpgrade(level);
                var body = new byte[77];
                body[0] = (byte)row; body[1] = (byte)i; body[2] = (byte)list.Count;
                body[3] = (byte)level; body[4] = (byte)ra.MaxLevel;
                body[5] = (byte)cost; body[6] = (byte)(cost >> 8);
                body[7] = (byte)points; body[8] = (byte)(points >> 8);
                body[9] = (byte)ra.Icon; body[10] = (byte)(ra.Icon >> 8);
                body[11] = rank; body[12] = sub;
                byte[] name = Encoding.ASCII.GetBytes(ra.Name ?? ra.KeyName);
                Array.Copy(name, 0, body, 13, Math.Min(name.Length, 63));
                Hire165.Send(player, 11, body);
            }
            // the original client's native trainer window is no longer sent (tested in the original client 2026-10-30: its clean REALM
            // ABILITIES window reads the op 12 list instead)
        }

        /// <summary>A click in the companion realm ability window: buy the next level of that ability.</summary>
        public static void OnRealmAbilityClick(GamePlayer player, int windowId, int item)
        {
            int row = windowId - RealmAbilityWindowBase;
            OfflineHiredCompanionRecord hired = HiredCompanions165.RosterOf(player).FirstOrDefault(h => h.Row == row);
            GameBot bot = hired == null ? null : HiredCompanions165.LiveBot(hired.BotId);
            if (bot == null)
                return;
            string problem = BuyRealmAbility(bot, hired, item);
            if (problem == null && (item < 0 || item >= RealmAbilityList(bot).Count))
                problem = "Choose a realm ability.";
            player.Out.SendMessage(problem ?? $"{bot.Name} learns {RealmAbilityList(bot)[item].Name}.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
            SendRealmAbilities(player, row, bot, hired);
            SendStatsText(player, row, hired, bot);
        }

        /// <summary>The companion's trainable lines, in the order op 8 lists them.</summary>
        public static List<Specialization> Lines(GameBot bot) =>
            bot.GetSpecList().Where(s => s.Trainable).OrderBy(s => s.Name, StringComparer.Ordinal).ToList();

        private static string Train(GameBot bot, int line)
        {
            List<Specialization> lines = Lines(bot);
            return line < 0 || line >= lines.Count ? "Choose a spec line." : bot.CompanionTrain(lines[line]);
        }

        private static string Respec(GameBot bot)
        {
            bot.CompanionRespec();
            return $"{bot.Name}'s specs are reset; {bot.CompanionSpecPoints} points to spend.";
        }

        /// <summary>op 8 per line; with preview, op 9 for everything each line gives up to level 50 (styles, spells,
        /// abilities, with the spec level they need), then an end marker per line.</summary>
        public static void SendSpec(GamePlayer player, int row, GameBot bot, bool preview)
        {
            List<Specialization> lines = Lines(bot);
            int points = bot.CompanionSpecPoints;
            for (int i = 0; i < lines.Count; i++)
            {
                Specialization spec = lines[i];
                int cost = bot.CompanionTrainCost(spec);   // 0 for an autotrain line's free level
                var body = new byte[73];
                body[0] = (byte)row; body[1] = (byte)i; body[2] = (byte)lines.Count;
                body[3] = (byte)spec.Level; body[4] = (byte)bot.Level;
                body[5] = (byte)points; body[6] = (byte)(points >> 8);
                body[7] = (byte)cost; body[8] = (byte)(cost >> 8);
                byte[] name = Encoding.ASCII.GetBytes(spec.Name ?? spec.KeyName);
                Array.Copy(name, 0, body, 9, Math.Min(name.Length, 63));
                Hire165.Send(player, 8, body);
                if (!preview)
                    continue;
                // kind: 1 style, 2 spell, 3 ability, 4 baseline spell (comes with character level; the client greys it)
                var entries = AllUnlocks(bot, spec)
                    .Select(u => (Level: u.Level, Kind: u.Skill is Style ? (byte)1 : u.Skill is Spell ? (u.Baseline ? (byte)4 : (byte)2) : (byte)3,
                        u.Skill.Icon, u.Skill.Name))
                    .ToList();
                foreach (var e in entries.Where(e => e.Level > 0).OrderBy(e => e.Level).ThenBy(e => e.Kind).Take(120))
                {
                    var entry = new byte[70];
                    entry[0] = (byte)row; entry[1] = (byte)i; entry[2] = (byte)Math.Clamp(e.Level, 0, 255); entry[3] = e.Kind;
                    entry[4] = (byte)e.Icon; entry[5] = (byte)(e.Icon >> 8);
                    byte[] n = Encoding.ASCII.GetBytes(e.Name ?? string.Empty);
                    Array.Copy(n, 0, entry, 6, Math.Min(n.Length, 63));
                    Hire165.Send(player, 9, entry);
                }
                Hire165.Send(player, 9, [(byte)row, (byte)i, 0, 0xFF]);
            }
        }

        // ---- stats and XP

        public static void SendStats(GamePlayer player, int row, GameBot bot)
        {
            var body = new byte[77];
            body[0] = (byte)row;
            body[1] = (byte)bot.Level;
            int xp = Hire165.XpPerMille(bot, bot.PersistentRecord);
            body[2] = (byte)xp; body[3] = (byte)(xp >> 8);
            int at = 4;
            void Short(int v) { v = Math.Clamp(v, 0, ushort.MaxValue); body[at++] = (byte)v; body[at++] = (byte)(v >> 8); }
            void Int(long v) { int x = (int)Math.Clamp(v, 0, int.MaxValue); body[at++] = (byte)x; body[at++] = (byte)(x >> 8); body[at++] = (byte)(x >> 16); body[at++] = (byte)(x >> 24); }
            for (eProperty p = eProperty.Strength; p <= eProperty.Charisma; p++)
                Short(bot.GetModified(p));
            Int(bot.Health); Int(bot.MaxHealth);
            Int(bot.Mana); Int(bot.MaxMana);
            Short(bot.Endurance); Short(bot.MaxEndurance);
            Short(bot.GetModified(eProperty.ArmorFactor));
            body[at++] = (byte)Math.Clamp(bot.GetModified(eProperty.ArmorAbsorption), 0, 100);
            foreach (eProperty r in new[] { eProperty.Resist_Crush, eProperty.Resist_Slash, eProperty.Resist_Thrust, eProperty.Resist_Heat,
                         eProperty.Resist_Cold, eProperty.Resist_Matter, eProperty.Resist_Body, eProperty.Resist_Spirit, eProperty.Resist_Energy })
                body[at++] = (byte)Math.Clamp(bot.GetModified(r), 0, 100);
            at++;   // [56] spare: 10 resist bytes reserved
            Int(bot.PersistentRecord?.MoneyCopper ?? 0);
            Short((int)((bot.PersistentRecord?.RealmPoints ?? 0) / 1000));
            // [63..78] the 8 stats without buffs or debuffs (base and items), [79..80] armor factor likewise: the new
            // client shows a buffed stat green and a debuffed one red (owner 2026-11-05, reset-day C15)
            for (eProperty p = eProperty.Strength; p <= eProperty.Charisma; p++)
                Short(bot.GetModifiedBase(p));
            Short(bot.GetModifiedBase(eProperty.ArmorFactor));
            Hire165.Send(player, 10, body);
        }

        /// <summary>op 10 with just level and XP for a companion at the inn (the roster's XP bubbles).</summary>
        public static void SendXpOnly(GamePlayer player, int row, OfflineWorldBotRecord rec)
        {
            int xp = Hire165.XpPerMille(null, rec);
            Hire165.Send(player, 10, [(byte)row, (byte)rec.Level, (byte)xp, (byte)(xp >> 8)]);
        }

        /// <summary>The stats sheet as text for the original client: slot 40 header, 41-46 six lines.</summary>
        public static void SendStatsText(GamePlayer player, int row, OfflineHiredCompanionRecord hired, GameBot bot)
        {
            OfflineWorldBotRecord rec = bot?.PersistentRecord ?? HiredCompanions165.Record(hired.BotId);
            if (rec == null)
                return;
            int level = bot?.Level ?? rec.Level;
            double bubbles = Hire165.XpPerMille(bot, rec) / 100.0;
            int realmLevel = bot?.RealmLevel ?? 0;
            string header = $"{rec.Name} - {rec.ClassName} {level} - {bubbles:0.0} bubbles" +
                            (bot != null ? $" - realm rank {realmLevel / 10 + 1}L{realmLevel % 10}" : string.Empty);
            Hire165.Text(player, 40, header);
            // diagnostics for the original client's STATS button (2026-10-30): what slot 40 carried
            StatsLog.Info($"COMPANION_STATS_165 player={player.Name} row={row} summoned={bot != null} slot40=\"{header}\"");
            if (bot == null)
            {
                Hire165.Text(player, 41, rec.IsAlive ? "Waiting (dismissed). Summon them to see their full stats." : "Dead. Any healer brings them back, free.");
                for (int s = 42; s <= 46; s++)
                    Hire165.Text(player, s, string.Empty);
                return;
            }
            int M(eProperty p) => bot.GetModified(p);
            Hire165.Text(player, 41, $"STR {M(eProperty.Strength)}  CON {M(eProperty.Constitution)}  DEX {M(eProperty.Dexterity)}  QUI {M(eProperty.Quickness)}");
            Hire165.Text(player, 42, $"INT {M(eProperty.Intelligence)}  PIE {M(eProperty.Piety)}  EMP {M(eProperty.Empathy)}  CHA {M(eProperty.Charisma)}");
            Hire165.Text(player, 43, $"Health {bot.Health}/{bot.MaxHealth}  Power {bot.Mana}/{bot.MaxMana}  Endurance {bot.Endurance}/{bot.MaxEndurance}");
            Hire165.Text(player, 44, $"Armor factor {M(eProperty.ArmorFactor)}  Absorb {M(eProperty.ArmorAbsorption)}%");
            Hire165.Text(player, 45, $"Resists: crush {M(eProperty.Resist_Crush)} slash {M(eProperty.Resist_Slash)} thrust {M(eProperty.Resist_Thrust)} " +
                                     $"heat {M(eProperty.Resist_Heat)} cold {M(eProperty.Resist_Cold)} matter {M(eProperty.Resist_Matter)} " +
                                     $"body {M(eProperty.Resist_Body)} spirit {M(eProperty.Resist_Spirit)} energy {M(eProperty.Resist_Energy)}");
            Hire165.Text(player, 46, $"Money {Money.GetString(rec.MoneyCopper)}  Realm points {rec.RealmPoints:N0}");
        }

        // ---- repairs (owner: never piece by piece; the player pays the normal price of each worn piece)

        public static List<GameBot> WithPlayer(GamePlayer player) =>
            HiredCompanions165.RosterOf(player).Select(h => HiredCompanions165.LiveBot(h.BotId)).Where(b => b != null).ToList();

        private static IEnumerable<DbInventoryItem> Worn(GameBot bot) =>
            bot.Inventory.AllItems.Where(i => i != null && i.Condition < i.MaxCondition && i.Durability > 0 &&
                                              i.SlotPosition is >= (int)eInventorySlot.MinEquipable and <= (int)eInventorySlot.MaxEquipable);

        /// <summary>Pure: what a smith charges for these pieces under player_upkeep (classic the smith's price, light half,
        /// free nothing), as a player's own "repair all" without its 20% surcharge.</summary>
        public static long RepairCost(IEnumerable<long> classicCosts) =>
            DOL.GS.Crafting.PlayerCraftingRules.SmithCost(classicCosts.Sum());

        /// <summary>
        /// The smith's offer (or the REPAIR button): one confirm, every worn piece of the companions with the player. Returns
        /// a problem or null. True 1.65 repaired companions' gear free (owner 2026-10-31); Living Server charges the player
        /// the smith's price under player_upkeep (free when that is "free"), recorded in the economy ledger as a repair.
        /// </summary>
        public static string OfferRepair(GamePlayer player, List<GameBot> bots)
        {
            if (!player.GetNPCsInRadius(500).OfType<Blacksmith>().Any())
                return "Find a smith first.";
            List<DbInventoryItem> items = bots.SelectMany(Worn).ToList();
            if (items.Count == 0)
                return "Their gear needs no repair.";
            string who = bots.Count == 1 ? bots[0].Name : "your companions";
            long cost = RepairCost(items.Select(item => item.RepairCost));
            if (cost <= 0)
            {
                RepairNow(player, bots, 0);
                return null;
            }
            player.Out.SendCustomDialog($"It will cost {Money.GetString(cost)} to repair {who}'s gear ({items.Count} pieces). Do you accept?",
                (p, response) =>
                {
                    if (response != 0x01)
                        return;
                    List<GameBot> still = bots.Where(b => HiredCompanions165.LiveBot(b.DatabaseID) == b).ToList();
                    long now = RepairCost(still.SelectMany(Worn).Select(item => item.RepairCost));
                    if (now <= 0 || !p.GetNPCsInRadius(500).OfType<Blacksmith>().Any())
                        return;
                    if (!p.RemoveMoney(now))
                    {
                        p.Out.SendMessage($"You need {Money.GetString(now)} to repair {who}'s gear.", eChatType.CT_System, eChatLoc.CL_SystemWindow);
                        return;
                    }
                    // the player's coin leaves the supply for a repair: the ledger's existing kind
                    DOL.GS.CraftingEconomy.EconomyFlows.RecordPlayer(p.Realm, -now, DOL.GS.CraftingEconomy.EconomyFlowKind.Repair);
                    RepairNow(p, still, now);
                });
            return null;
        }

        /// <summary>The REPAIR button and the smith's offer (kept under True 1.65's name).</summary>
        public static string AskRepair(GamePlayer player, List<GameBot> bots) => OfferRepair(player, bots);

        private static void RepairNow(GamePlayer player, List<GameBot> bots, long paid)
        {
            List<DbInventoryItem> items = bots.SelectMany(Worn).ToList();
            foreach (DbInventoryItem item in items)
            {
                Repair.ModifyConditionAndDurability(item);
                item.Dirty = true;
            }
            foreach (GameBot b in bots)
            {
                b.MarkAutonomousStateDirty();
                AutonomousBotStatusPersistence.Queue(b, true);
            }
            string who = bots.Count == 1 ? bots[0].Name : "your companions";
            player.Out.SendMessage(paid > 0
                    ? $"The smith repairs {who}'s gear ({items.Count} pieces) for {Money.GetString(paid)}."
                    : $"The smith repairs {who}'s gear ({items.Count} pieces) at no charge.",
                eChatType.CT_System, eChatLoc.CL_SystemWindow);
            StatsLog.Info($"COMPANION_REPAIR_165 player={player.Name} companions={bots.Count} pieces={items.Count} paid={paid}");
        }
    }
}
