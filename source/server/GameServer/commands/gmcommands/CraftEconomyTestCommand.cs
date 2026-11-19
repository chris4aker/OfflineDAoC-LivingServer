using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DOL.Database;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands
{
    // /lscraft, the GM's window on Crafting 2.0 in game (WP10). Each part answers from what is built and registered now and
    // says plainly what is not built yet; a package that lands registers its own handler (CraftEconomyTestHooks.Register),
    // so this file is not edited again. It reads only; nothing is changed by looking.
    [CmdAttribute(
        "&lscraft",
        ePrivLevel.GM,
        "Crafting 2.0 in game: what is built, and what a bot or an item looks like to the economy",
        "/lscraft craft - bot crafting: switches, the recipe catalog, and the crafter roster: each realm's crafter gamebots in the game (name, order, skill, level, where, on a break or a task)",
        "/lscraft wtb - bots' want-to-buy posts",
        "/lscraft commission - realm quartermaster commissions",
        "/lscraft ledger - the realm's coin now (online gamebots) and the economy ledger",
        "/lscraft index - what the price index says of the first item in your backpack",
        "/lscraft crafter <name> - a gamebot's type, crafting skills, purse and crafter record")]
    public sealed class CraftEconomyTestCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public static readonly string[] Parts = ["craft", "wtb", "commission", "ledger", "index", "crafter"];

        public void OnCommand(GameClient client, string[] args)
        {
            if (args.Length < 2 || !Parts.Contains(args[1].ToLowerInvariant()))
            {
                DisplaySyntax(client);
                return;
            }
            Run(client, args[1..]);
        }

        /// <summary>Runs a part (words[0]) for the GM; /lstest craft|wtb|commission delegates here.</summary>
        public static void Run(GameClient client, string[] words)
        {
            GamePlayer gm = client?.Player;
            if (gm == null || words == null || words.Length == 0)
                return;
            string part = words[0].ToLowerInvariant();
            IEnumerable<string> lines;
            try
            {
                lines = CraftEconomyTestHooks.TryGet(part, out CraftEconomyTestHooks.Handler handler)
                    ? handler(gm, words)
                    : Answer(part, gm, words);
            }
            catch (Exception e)
            {
                lines = [$"/lscraft {part} failed: {e.Message}"];
            }
            foreach (string line in lines ?? [])
                client.Out.SendMessage(line, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        }

        private static IEnumerable<string> Answer(string part, GamePlayer gm, string[] words) => part switch
        {
            "craft" => CraftLines(RosterNow()),
            "wtb" => WtbLines(),
            "commission" => CommissionLines(),
            "ledger" => LedgerLines(OnlineBots(), EconomyFlows.Sink),
            "index" => IndexLines(FirstBackpackItem(gm), gm.Realm, PriceIndexes.Current),
            "crafter" => CrafterLines(gm, string.Join(' ', words.Skip(1))),
            _ => [$"Unknown part {part}. " + string.Join(" | ", Parts)],
        };

        private static DbInventoryItem FirstBackpackItem(GamePlayer gm) =>
            gm.Inventory?.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)?.OrderBy(i => i.SlotPosition).FirstOrDefault();

        // ---- pure answers (UT_CraftEconomyTestCommand) ----

        /// <summary>A holder's registered implementation is a package's when it is not the holder's own nested default.</summary>
        public static bool IsBuilt(object implementation, Type holder) =>
            implementation != null && implementation.GetType().DeclaringType != holder;

        private static string OnOff(bool on) => on ? "on" : "off";

        /// <summary>Coin as "1,234g 5s 6c" (zero parts left out; "0c" for none). Not Money.GetString: that needs the
        /// language tables, and these answers stay pure so the tests run without a server.</summary>
        public static string Coin(long copper)
        {
            if (copper == 0)
                return "0c";
            string sign = copper < 0 ? "-" : "";
            long value = Math.Abs(copper);
            var parts = new List<string>();
            if (value >= 10_000)
                parts.Add((value / 10_000).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture) + "g");
            if (value / 100 % 100 != 0)
                parts.Add(value / 100 % 100 + "s");
            if (value % 100 != 0)
                parts.Add(value % 100 + "c");
            return sign + string.Join(" ", parts);
        }

        public static string[] CraftLines() => CraftLines(null);

        /// <summary>A crafter on the roster as /lscraft craft lists it: in the game (with where it is) or not.</summary>
        public sealed record RosterEntry(CrafterState State, bool Online, string Where, bool AtBreak);

        /// <summary>The switches, the catalog and the roster. <paramref name="roster"/> null: the registered roster is not read
        /// (the tests' pure call).</summary>
        public static string[] CraftLines(IReadOnlyList<RosterEntry> roster)
        {
            var lines = new List<string>
            {
                $"Bot crafting: bot_crafters {OnOff(CraftingEconomyProperties.BOT_CRAFTERS)}, bot_craft_orders {OnOff(CraftingEconomyProperties.BOT_CRAFT_ORDERS)}, " +
                $"crafter share {CraftingEconomyProperties.BOT_CRAFTER_SHARE_PERCENT}% of new bots, pace {CraftingEconomyProperties.BOT_CRAFTING_PACE}.",
            };
            IRecipeCatalog catalog = RecipeCatalogs.Current;
            if (!IsBuilt(catalog, typeof(RecipeCatalogs)))
                lines.Add("Recipe catalog: not registered on this server (bot_crafters off, or still starting); recipes load one at a time.");
            else if (!catalog.IsLoaded)
                lines.Add("Recipe catalog: registered, still loading.");
            else
            {
                eCraftingSkill[] skills = Enum.GetValues<eCraftingSkill>().Where(s => s is not eCraftingSkill.NoCrafting and not eCraftingSkill._Last).Distinct().ToArray();
                lines.Add("Recipe catalog: " + string.Join(", ", skills.Select(s => (s, n: catalog.ForSkill(s)?.Count ?? 0)).Where(p => p.n > 0).Select(p => $"{p.s} {p.n}")) + ".");
            }
            if (!IsBuilt(BotCrafters.Roster, typeof(BotCrafters)))
                lines.Add("Crafter roster: not registered on this server (bot_crafters off, or still starting). No gamebot crafts; Crafter-type bots only take longer town breaks.");
            else
                lines.AddRange(RosterLines(roster ?? []));
            return lines.ToArray();
        }

        /// <summary>At most this many crafters are named a realm (the best skill first); the rest are counted.</summary>
        public const int RosterNamesPerRealm = 12;

        /// <summary>
        /// The crafter roster by realm: how many crafters, how many in the game, and the ones in the game by name with their
        /// order, primary skill, level and where they are. Seen on the owner's PC (build b1b6846, 2026-11-18): the line was only
        /// "Crafter roster: registered." while crafters were working. Pure.
        /// </summary>
        public static string[] RosterLines(IReadOnlyList<RosterEntry> roster)
        {
            var lines = new List<string>();
            RosterEntry[] all = (roster ?? []).Where(e => e?.State != null).ToArray();
            if (all.Length == 0)
            {
                lines.Add($"Crafter roster: no crafter gamebots{(CraftingEconomyProperties.BOT_CRAFTERS ? "" : " (bot_crafters is off)")}.");
                return lines.ToArray();
            }
            int online = all.Count(e => e.Online);
            lines.Add($"Crafter roster: {all.Length} crafter gamebots, {online} in the game. /lscraft crafter <name> for one of them.");
            foreach (var realm in all.GroupBy(e => e.State.Realm).OrderBy(g => g.Key))
            {
                RosterEntry[] here = realm.Where(e => e.Online).OrderByDescending(e => SkillOf(e.State)).ThenBy(e => e.State.Name, StringComparer.Ordinal).ToArray();
                lines.Add($"  {GlobalConstants.RealmToName(realm.Key)}: {realm.Count()} crafters, {here.Length} in the game" + (here.Length == 0 ? "." : ":"));
                foreach (RosterEntry e in here.Take(RosterNamesPerRealm))
                    lines.Add($"    {e.State.Name}: {OrderName(e.State.Order)} {SkillOf(e.State)}, level {e.State.BotLevel}, " +
                              $"{(string.IsNullOrWhiteSpace(e.Where) ? "somewhere" : e.Where)}, {(e.AtBreak ? "on a break" : "on a task")}.");
                if (here.Length > RosterNamesPerRealm)
                    lines.Add($"    and {here.Length - RosterNamesPerRealm} more in the game.");
            }
            return lines.ToArray();
        }

        private static int SkillOf(CrafterState state) => state.Skills != null && state.Skills.TryGetValue(state.Order, out int value) ? value : 0;

        private static string OrderName(eCraftingSkill order) => order switch
        {
            eCraftingSkill.WeaponCrafting => "weaponcraft",
            eCraftingSkill.ArmorCrafting => "armorcraft",
            eCraftingSkill.Tailoring => "tailoring",
            eCraftingSkill.Fletching => "fletching",
            eCraftingSkill.SpellCrafting => "spellcraft",
            eCraftingSkill.Alchemy => "alchemy",
            eCraftingSkill.SiegeCrafting => "siegecraft",
            _ => order.ToString().ToLowerInvariant(),
        };

        /// <summary>The registered roster now, every realm, with where each crafter in the game is. Game loop.</summary>
        private static IReadOnlyList<RosterEntry> RosterNow()
        {
            var entries = new List<RosterEntry>();
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                foreach (CrafterState state in BotCrafters.Roster.InRealm(realm) ?? [])
                {
                    bool online = AutonomousBotRegistry.TryGet(state.BotId, out GameBot bot) && bot != null && bot.ObjectState == GameObject.eObjectState.Active;
                    bool atBreak = false;
                    try { atBreak = online && AutonomousObjectiveAssignments.IsBetweenPveTasks(bot); }
                    catch { }
                    entries.Add(new RosterEntry(state, online, online ? AutonomousLivingServer.PlaceOf(bot) : string.Empty, atBreak));
                }
            return entries;
        }

        public static string[] WtbLines() =>
        [
            "Want-to-buy: not registered on this server (bot_crafters off, or still starting).",
            "Today a gamebot buys an upgrade on the Realm Exchange or from a vendor at a task break, scored by EquipmentValue; nothing is posted.",
        ];

        public static string[] CommissionLines()
        {
            var lines = new List<string> { $"Realm commissions: setting {CraftingEconomyProperties.REALM_COMMISSIONS}; Exchange tax {CraftingEconomyProperties.EXCHANGE_TAX_PERCENT}%." };
            lines.Add(IsBuilt(CraftOrders.Board, typeof(CraftOrders))
                ? "Order board: registered."
                : "Order board and quartermaster commissions: not registered on this server (bot_craft_orders off, or still starting). The settings do nothing until then.");
            return lines.ToArray();
        }

        /// <summary>The coin of the online gamebots by realm (the sum of their purses), crafters apart, and whether the ledger runs.</summary>
        public static string[] LedgerLines(IEnumerable<(eRealm Realm, long Purse, bool Crafter)> bots, IEconomyFlowSink sink)
        {
            var lines = new List<string>
            {
                sink == null
                    ? $"Economy ledger: not registered on this server (economy_ledger_enabled {OnOff(CraftingEconomyProperties.ECONOMY_LEDGER_ENABLED)}, or still starting)."
                    : $"Economy ledger: {sink.GetType().Name} records every gamebot coin move (economy_ledger_enabled {OnOff(CraftingEconomyProperties.ECONOMY_LEDGER_ENABLED)}).",
                "Online gamebots' coin now (offline bots' purses are not counted here):",
            };
            var rows = (bots ?? []).GroupBy(b => b.Realm).OrderBy(g => g.Key).ToArray();
            if (rows.Length == 0)
                lines.Add("  no gamebot is online.");
            foreach (var realm in rows)
            {
                long[] purses = realm.Select(b => b.Purse).OrderBy(p => p).ToArray();
                long crafters = realm.Where(b => b.Crafter).Sum(b => b.Purse);
                lines.Add($"  {GlobalConstants.RealmToName(realm.Key)}: {purses.Length} bots, {Coin(purses.Sum())} " +
                          $"(median {Coin(purses[purses.Length / 2])}; Crafter-type bots hold {Coin(crafters)}).");
            }
            return lines.ToArray();
        }

        public static string[] IndexLines(DbInventoryItem item, eRealm realm, IPriceIndex index)
        {
            if (item == null)
                return ["Put an item in your first backpack slots, then /lscraft index."];
            index ??= PriceIndexes.Current;
            var band = MarketBand.Of(realm, item);
            string source = IsBuilt(index, typeof(PriceIndexes))
                ? $"index {index.GetType().Name}"
                : "the merchant appraisal and the listing valuation (no price index is registered on this server)";
            return
            [
                $"{item.Name} (level {item.Level}, quality {item.Quality}{(item.IsCrafted ? ", crafted" : "")}), band {band.Key}, from {source}:",
                $"  floor {Coin(index.Floor(item))}, fair {Coin(index.Fair(item))}, " +
                $"ask {Coin(index.Ask(item, 0))} (at a seller's mood of 0), bid {Coin(index.Bid(item, 0))}.",
            ];
        }

        private static IEnumerable<(eRealm, long, bool)> OnlineBots() =>
            AutonomousBotRegistry.Snapshot()
                .Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper && bot.PersistentRecord != null)
                .Select(bot => (bot.Realm, bot.PersistentRecord.MoneyCopper,
                    AutonomousBotPersonality.TypeFor(bot.DatabaseID) == BotPersonalityType.Crafter));

        private static IEnumerable<string> CrafterLines(GamePlayer gm, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return ["/lscraft crafter <name> (an online gamebot)"];
            if (!AutonomousBotRegistry.TryGetByName(name, out GameBot bot) || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper)
                return [$"No online gamebot is called {name.Trim()} (companions are not part of the economy)."];
            CrafterState state = BotCrafters.Roster.TryGet(bot.DatabaseID, out CrafterState found) ? found : null;
            return CrafterLines(bot.Name, bot.Realm, bot.Level, AutonomousBotPersonality.For(bot).Type,
                bot.PersistentRecord?.SerializedCraftingSkills, bot.PersistentRecord?.MoneyCopper ?? 0, state,
                IsBuilt(BotCrafters.Roster, typeof(BotCrafters)));
        }

        public static string[] CrafterLines(string name, eRealm realm, int level, BotPersonalityType type, string savedSkills, long purse,
            CrafterState state, bool rosterBuilt)
        {
            var skills = BotCraftingSkills.Parse(savedSkills);
            var lines = new List<string>
            {
                $"{name}: {GlobalConstants.RealmToName(realm)} level {level}, {type}, purse {Coin(purse)}.",
                skills.Count == 0
                    ? "  Crafting skills: none."
                    : "  Crafting skills: " + string.Join(", ", skills.Select((s, i) => $"{s.Key} {s.Value}{(i == 0 ? " (primary)" : "")}")) + ".",
            };
            if (state != null)
                lines.Add($"  Crafter: order {state.Order}, lifetime net {Coin(state.LifetimeNetCopper)}, " +
                          $"reinvests up to {state.Policy?.MaxReinvestShareOfProfit:P0} of profit.");
            else
                lines.Add(rosterBuilt ? "  Not on the crafter roster." : "  Crafter record and stock: not registered on this server (bot_crafters off, or still starting).");
            return lines.ToArray();
        }
    }

    /// <summary>
    /// Where a package that lands registers its /lscraft part (WP4 "craft" and "crafter", WP5 "commission" and "index",
    /// WP6 "wtb", WP2 "ledger"), so CraftEconomyTestCommand.cs is never edited again. Copy-on-write; a later registration of
    /// the same part replaces the earlier one.
    /// </summary>
    public static class CraftEconomyTestHooks
    {
        /// <summary>The lines for the GM; words[0] is the part, the rest its arguments.</summary>
        public delegate IEnumerable<string> Handler(GamePlayer gm, string[] words);

        private static Dictionary<string, Handler> _handlers = new(StringComparer.OrdinalIgnoreCase);

        public static void Register(string part, Handler handler)
        {
            if (string.IsNullOrWhiteSpace(part) || handler == null)
                return;
            while (true)
            {
                Dictionary<string, Handler> current = Volatile.Read(ref _handlers);
                var next = new Dictionary<string, Handler>(current, StringComparer.OrdinalIgnoreCase) { [part.Trim()] = handler };
                if (Interlocked.CompareExchange(ref _handlers, next, current) == current)
                    return;
            }
        }

        public static bool TryGet(string part, out Handler handler) =>
            Volatile.Read(ref _handlers).TryGetValue(part ?? string.Empty, out handler);

        public static void ResetForTests() => Volatile.Write(ref _handlers, new Dictionary<string, Handler>(StringComparer.OrdinalIgnoreCase));
    }
}
