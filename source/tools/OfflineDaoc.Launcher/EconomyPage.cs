using System.Data.SQLite;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The economy page (Crafting 2.0 WP9; docs/ECONOMY-LEDGER.md): the economy as the server's ledger records it, written
/// from the game database like the Camelot Herald. It reads only (the database is opened read-only, so it is safe while
/// the server runs), needs no web server, and changes nothing. One self-contained HTML file, runtime\herald\economy.html.
///
/// Its sources are two saved tables: offline_economy_ledger (one row per realm and UTC hour: the coin of bots, players and
/// guild banks, the Exchange's listed value, faucets and sinks by kind, the residual; WP2) and offline_exchange_index (the
/// price index per band; WP5). The Exchange tax is the ledger's "exchangetax" sink. The server's log lines (ECON_SUPPLY,
/// ECON_FLOWS) hold the same numbers; the launcher reads the table, as it reads every other saved state.
/// </summary>
public static class EconomyPage
{
    public const string FileName = "economy.html";
    public const string LedgerTable = "offline_economy_ledger";
    public const string IndexTable = "offline_exchange_index";
    /// <summary>EconomyFlowClassifier.KeyName(EconomyFlowKind.ExchangeTax).</summary>
    public const string TaxKind = "exchangetax";
    /// <summary>How many of the latest recorded hours the totals cover.</summary>
    public const int WindowHours = 24;

    private static readonly string[] RealmNames = ["All", "Albion", "Midgard", "Hibernia"];
    private static readonly string[] RealmCss = ["", "alb", "mid", "hib"];

    /// <summary>One offline_economy_ledger row. Supply is what ECON_SUPPLY calls supply: bots, players and guild banks.</summary>
    public sealed record LedgerHour(DateTime HourUtc, int Realm, long BotCoin, long PlayerCoin, long GuildBanks, long ListedValue,
        IReadOnlyDictionary<string, long> Faucets, IReadOnlyDictionary<string, long> Sinks, long Residual, int Bots, int Crafters)
    {
        public long Supply => BotCoin + PlayerCoin + GuildBanks;
        public long FaucetTotal => Faucets.Values.Sum();
        public long SinkTotal => Sinks.Values.Sum();
        public long Tax => Sinks.GetValueOrDefault(TaxKind);
    }

    /// <summary>One offline_exchange_index row; BandKey is "realm|objectType|itemType|levelBand|qualityTier" (MarketBand.Key).</summary>
    public sealed record IndexBand(int Realm, int ObjectType, int ItemType, int LevelBand, int QualityTier, int Count, int Demand,
        long IndexCopper, long FloorCopper, long CeilingCopper, int DailyPlayerSales, string UpdatedUtc);

    /// <summary>What the page shows, read from the database (or built by a test).</summary>
    public sealed record Snapshot(IReadOnlyList<LedgerHour> Hours, IReadOnlyList<IndexBand> Bands, bool HasLedgerTable, bool HasIndexTable,
        bool LedgerEnabled, int TaxPercent, bool BotCrafters, IReadOnlyList<string> Unreadable);

    /// <summary>One hour of every realm together.</summary>
    public sealed record HourTotal(DateTime HourUtc, long Supply, long Faucets, long Sinks, long Tax, long Residual, int Bots, int Crafters);

    /// <summary>Writes the page for this game and returns the file's path.</summary>
    public static string Write(string database, string outputFolder, DateTime nowLocal)
    {
        string html = Render(Read(database), nowLocal);
        Directory.CreateDirectory(outputFolder);
        string path = Path.Combine(outputFolder, FileName);
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, html, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public static string Build(string database, DateTime nowLocal) => Render(Read(database), nowLocal);

    /// <summary>Reads the ledger, the price index and the three settings the page names. Read-only.</summary>
    public static Snapshot Read(string database)
    {
        if (!File.Exists(database)) throw new InvalidOperationException("The world database is missing: " + database);
        using var c = new SQLiteConnection(new SQLiteConnectionStringBuilder
        { DataSource = database, ReadOnly = true, FailIfMissing = true, Pooling = false, DefaultTimeout = 10 }.ConnectionString);
        c.Open();
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var command = c.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";
            using var reader = command.ExecuteReader();
            while (reader.Read()) tables.Add(reader.GetString(0));
        }

        var unreadable = new List<string>();
        var hours = new List<LedgerHour>();
        if (tables.Contains(LedgerTable))
        {
            using var command = c.CreateCommand();
            command.CommandText = $"""
                SELECT HourUtc, Realm, BotCoin, PlayerCoin, GuildBanks, ListedValue, FaucetsJson, SinksJson, ResidualCopper, Bots, Crafters
                FROM {LedgerTable} WHERE Realm BETWEEN 1 AND 3
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string hourText = Text(reader, 0);
                if (!DateTime.TryParse(hourText, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime hour))
                {
                    unreadable.Add($"{LedgerTable} hour \"{hourText}\"");
                    continue;
                }
                hours.Add(new LedgerHour(hour, (int)Number(reader, 1), Number(reader, 2), Number(reader, 3), Number(reader, 4), Number(reader, 5),
                    Kinds(Text(reader, 6), unreadable), Kinds(Text(reader, 7), unreadable), Number(reader, 8), (int)Number(reader, 9), (int)Number(reader, 10)));
            }
        }

        var bands = new List<IndexBand>();
        if (tables.Contains(IndexTable))
        {
            using var command = c.CreateCommand();
            command.CommandText = $"""
                SELECT BandKey, Realm, Count, Demand, IndexCopper, FloorCopper, CeilingCopper, DailyPlayerSales, UpdatedUtc FROM {IndexTable}
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                string key = Text(reader, 0);
                if (!TryParseBand(key, out int realm, out int objectType, out int itemType, out int levelBand, out int tier))
                {
                    unreadable.Add($"{IndexTable} band \"{key}\"");
                    continue;
                }
                bands.Add(new IndexBand(realm, objectType, itemType, levelBand, tier, (int)Number(reader, 2), (int)Number(reader, 3),
                    Number(reader, 4), Number(reader, 5), Number(reader, 6), (int)Number(reader, 7), Text(reader, 8)));
            }
        }

        var settings = new Dictionary<string, string>(StringComparer.Ordinal);
        if (tables.Contains("ServerProperty"))
        {
            using var command = c.CreateCommand();
            command.CommandText = "SELECT `Key`, Value FROM ServerProperty WHERE `Key` IN ('economy_ledger_enabled','exchange_tax_percent','bot_crafters')";
            using var reader = command.ExecuteReader();
            while (reader.Read()) settings[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
        }
        // As the server reads them (CraftingEconomyProperties): a missing or unreadable row keeps the default.
        bool ledgerOn = !settings.TryGetValue("economy_ledger_enabled", out string? ledger) || !bool.TryParse(ledger.Trim(), out bool on) || on;
        int tax = settings.TryGetValue("exchange_tax_percent", out string? taxText) &&
                  int.TryParse(taxText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int percent) ? Math.Clamp(percent, 0, 50) : 0;
        bool crafters = settings.TryGetValue("bot_crafters", out string? craftText) && bool.TryParse(craftText.Trim(), out bool craft) && craft;
        return new Snapshot(hours, bands, tables.Contains(LedgerTable), tables.Contains(IndexTable), ledgerOn, tax, crafters, unreadable);
    }

    /// <summary>The kinds of a FaucetsJson or SinksJson value ({"lootcoin":123}); an unreadable value counts as none.</summary>
    public static IReadOnlyDictionary<string, long> Kinds(string json, List<string>? unreadable = null)
    {
        var kinds = new Dictionary<string, long>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return kinds;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("not an object");
            foreach (var property in document.RootElement.EnumerateObject())
                if (property.Value.TryGetInt64(out long copper)) kinds[property.Name] = copper;
        }
        catch (JsonException)
        {
            unreadable?.Add("flows \"" + (json.Length > 40 ? json[..40] + "…" : json) + "\"");
            kinds.Clear();
        }
        return kinds;
    }

    public static bool TryParseBand(string key, out int realm, out int objectType, out int itemType, out int levelBand, out int tier)
    {
        realm = objectType = itemType = levelBand = tier = 0;
        string[] parts = (key ?? "").Split('|');
        return parts.Length == 5 &&
               int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out realm) &&
               int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out objectType) &&
               int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out itemType) &&
               int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out levelBand) &&
               int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out tier);
    }

    // ---- the numbers (pure) ----

    /// <summary>The latest <paramref name="count"/> recorded hours (of any realm), oldest first.</summary>
    public static IReadOnlyList<DateTime> LatestHours(IEnumerable<LedgerHour> hours, int count = WindowHours) =>
        hours.Select(h => h.HourUtc).Distinct().OrderByDescending(h => h).Take(count).OrderBy(h => h).ToList();

    /// <summary>Every realm together, per hour, for the latest hours.</summary>
    public static IReadOnlyList<HourTotal> Totals(IReadOnlyList<LedgerHour> hours, int count = WindowHours)
    {
        var window = LatestHours(hours, count).ToHashSet();
        return hours.Where(h => window.Contains(h.HourUtc)).GroupBy(h => h.HourUtc).OrderBy(g => g.Key)
            .Select(g => new HourTotal(g.Key, g.Sum(h => h.Supply), g.Sum(h => h.FaucetTotal), g.Sum(h => h.SinkTotal), g.Sum(h => h.Tax),
                g.Sum(h => h.Residual), g.Sum(h => h.Bots), g.Sum(h => h.Crafters)))
            .ToList();
    }

    /// <summary>Copper per kind over the latest hours, for one realm (1-3) or all (0).</summary>
    public static IReadOnlyDictionary<string, long> SumKinds(IReadOnlyList<LedgerHour> hours, bool sinks, int realm = 0, int count = WindowHours)
    {
        var window = LatestHours(hours, count).ToHashSet();
        var sum = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var hour in hours.Where(h => window.Contains(h.HourUtc) && (realm == 0 || h.Realm == realm)))
            foreach (var (kind, copper) in sinks ? hour.Sinks : hour.Faucets)
                sum[kind] = sum.GetValueOrDefault(kind) + copper;
        return sum;
    }

    /// <summary>The biggest sinks over the latest hours, every realm together, biggest first.</summary>
    public static IReadOnlyList<(string Kind, long Copper)> TopSinks(IReadOnlyList<LedgerHour> hours, int top = 5, int count = WindowHours) =>
        SumKinds(hours, sinks: true, realm: 0, count).Where(p => p.Value > 0).OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
            .Take(top).Select(p => (p.Key, p.Value)).ToList();

    /// <summary>A kind's name for a reader (EconomyFlowKind, lower case, as the ledger stores it).</summary>
    public static string KindName(string kind) => kind switch
    {
        "lootcoin" => "Monster coin",
        "lootshare" => "Group coin shares",
        "vendorsale" => "Sales to merchants",
        "vendorgear" => "Gear from merchants",
        "exchangesale" => "Realm Exchange sales",
        "repair" => "Repairs",
        "healer" => "Healers",
        "craftmaterials" => "Crafting materials",
        "craftsale" => "Crafted goods sold",
        "craftorder" => "Craft orders",
        "enchant" => "Enchanting",
        "dye" => "Dyes",
        "ammo" => "Arrows and bolts",
        "poison" => "Poisons",
        "potion" => "Potions",
        "travel" => "Travel",
        "siege" => "Siege supplies",
        "guild" => "Guild dues",
        "killtask" => "Kill tasks",
        TaxKind => "Realm Exchange tax",
        "stableticket" => "Horse routes",
        "medallion" => "Frontier medallions",
        "repairkit" => "Repair kits",
        "beggargift" => "Gifts to beggars",
        "commission" => "Quartermaster commissions",
        "consumable" => "Consumables",
        "luxury" => "Luxuries",
        "refund" => "Refunds",
        "launchergrant" => "Launcher grants",
        "unattributed" => "Not yet named",
        _ => kind,
    };

    // ---- the page ----

    public static string Render(Snapshot s, DateTime now)
    {
        var h = new StringBuilder();
        string E(string text) => WebUtility.HtmlEncode(text);
        string Realm(int realm) => realm is >= 1 and <= 3 ? $"<span class=\"realm {RealmCss[realm]}\">{RealmNames[realm]}</span>" : "All realms";

        h.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        h.Append("<title>The Realms' Purses</title><style>").Append(Css).Append("</style></head><body><main>");
        h.Append("<header><div class=\"rule\"></div><h1>The Realms' Purses</h1>");
        h.Append($"<p class=\"dateline\">The economy as the ledger records it · Offline DAoC · {E(now.ToString("dddd, d MMMM yyyy, h:mm tt", CultureInfo.InvariantCulture))}</p>");
        h.Append("<div class=\"rule\"></div><p class=\"nav\"><a href=\"index.html\">The Camelot Herald</a></p></header>");

        // What the server is set to do.
        h.Append("<p class=\"settings\">");
        h.Append(s.LedgerEnabled ? "Economy ledger: on (one row a realm every hour)." : "<b>Economy ledger: off.</b> No new hours are recorded until it is switched on in the launcher's Living Server tab.");
        h.Append(s.TaxPercent > 0 ? $" Realm Exchange tax: {s.TaxPercent}% of each sale, from the seller's proceeds." : " Realm Exchange tax: none.");
        h.Append(s.BotCrafters ? " Bot crafters: on." : " Bot crafters: off.");
        h.Append(" Settings take effect at the next server start.</p>");

        var latest = LatestHours(s.Hours);
        if (latest.Count == 0)
        {
            h.Append("<section><h2>No hours recorded yet</h2><p class=\"muted\">");
            h.Append(s.HasLedgerTable
                ? "The ledger writes its first row at the first full UTC hour after the server starts, if the economy ledger is on."
                : "This world has no economy ledger yet. The server creates it when it starts with Living Server 1.2 or later.");
            h.Append("</p></section>");
        }
        else
        {
            DateTime last = latest[^1];
            var lastRows = s.Hours.Where(x => x.HourUtc == last).OrderBy(x => x.Realm).ToList();
            var firstRows = s.Hours.Where(x => x.HourUtc == latest[0]).ToDictionary(x => x.Realm);

            // Supply by realm, the latest hour.
            h.Append($"<section><h2>Money Supply</h2><p class=\"muted\">At {E(When(last))}, the hour last recorded. Change over the {latest.Count} hour{(latest.Count == 1 ? "" : "s")} shown.</p><div class=\"realms\">");
            foreach (var row in lastRows)
            {
                long change = firstRows.TryGetValue(row.Realm, out var first) ? row.Supply - first.Supply : 0;
                h.Append($"<div class=\"card {RealmCss[row.Realm]}\"><h3>{RealmNames[row.Realm]}</h3>");
                h.Append($"<p class=\"big\">{E(Gold(row.Supply))}</p><p class=\"caption\">{E(Signed(change))} over the hours shown</p><table>");
                h.Append($"<tr><td>Gamebots' purses</td><td class=\"num\">{E(Gold(row.BotCoin))}</td></tr>");
                h.Append($"<tr><td>Players' coin and Exchange proceeds</td><td class=\"num\">{E(Gold(row.PlayerCoin))}</td></tr>");
                h.Append($"<tr><td>Guild banks</td><td class=\"num\">{E(Gold(row.GuildBanks))}</td></tr>");
                h.Append($"<tr><td>On sale at the Exchange <span class=\"muted\">(goods, not counted)</span></td><td class=\"num\">{E(Gold(row.ListedValue))}</td></tr>");
                h.Append($"<tr><td>Gamebots · crafters</td><td class=\"num\">{row.Bots:N0} · {row.Crafters:N0}</td></tr>");
                h.Append("</table></div>");
            }
            h.Append("</div></section>");

            // Hour by hour, every realm together, with the tax column.
            var totals = Totals(s.Hours);
            h.Append("<section><h2>Hour by Hour</h2><p class=\"muted\">Every realm together. Faucets bring new coin in, sinks take it out; " +
                     "the residual is what the ledger did not see (players' own coin moves, guild dues taken from loot, raid shares).</p>");
            h.Append("<table><tr><th>Hour</th><th class=\"num\">Supply</th><th class=\"num\">Faucets</th><th class=\"num\">Sinks</th><th class=\"num\">Exchange tax</th><th class=\"num\">Residual</th></tr>");
            foreach (var t in totals.OrderByDescending(t => t.HourUtc))
                h.Append($"<tr><td>{E(When(t.HourUtc))}</td><td class=\"num\">{E(Gold(t.Supply))}</td><td class=\"num\">{E(Gold(t.Faucets))}</td>" +
                         $"<td class=\"num\">{E(Gold(t.Sinks))}</td><td class=\"num\">{E(Gold(t.Tax))}</td><td class=\"num\">{E(Signed(t.Residual))}</td></tr>");
            h.Append($"<tr class=\"total\"><td>{latest.Count} hour{(latest.Count == 1 ? "" : "s")}</td><td></td><td class=\"num\">{E(Gold(totals.Sum(t => t.Faucets)))}</td>" +
                     $"<td class=\"num\">{E(Gold(totals.Sum(t => t.Sinks)))}</td><td class=\"num\">{E(Gold(totals.Sum(t => t.Tax)))}</td><td class=\"num\">{E(Signed(totals.Sum(t => t.Residual)))}</td></tr>");
            h.Append("</table></section>");

            // Top sinks.
            var top = TopSinks(s.Hours);
            h.Append("<section><h2>Where the Coin Goes</h2>");
            if (top.Count == 0) h.Append("<p class=\"muted\">No coin left the realms in the hours shown.</p>");
            else
            {
                long all = SumKinds(s.Hours, sinks: true).Values.Sum();
                h.Append("<ol class=\"sinks\">");
                foreach (var (kind, copper) in top)
                    h.Append($"<li><b>{E(KindName(kind))}</b> {E(Gold(copper))} <span class=\"muted\">({(all > 0 ? copper * 100.0 / all : 0).ToString("0", CultureInfo.InvariantCulture)}% of the sinks)</span></li>");
                h.Append("</ol>");
            }
            h.Append("</section>");

            // Flows by kind, per realm.
            h.Append("<section><h2>Flows by Kind</h2><p class=\"muted\">Copper in and out of the money supply over the hours shown, per realm.</p>");
            foreach (bool sinks in new[] { false, true })
            {
                var perRealm = Enumerable.Range(1, 3).ToDictionary(r => r, r => SumKinds(s.Hours, sinks, r));
                var kinds = perRealm.Values.SelectMany(d => d.Keys).Distinct(StringComparer.Ordinal)
                    .OrderByDescending(k => perRealm.Values.Sum(d => d.GetValueOrDefault(k))).ThenBy(k => k, StringComparer.Ordinal).ToList();
                h.Append($"<h3 class=\"sub\">{(sinks ? "Sinks (out)" : "Faucets (in)")}</h3>");
                if (kinds.Count == 0) { h.Append("<p class=\"muted\">None in the hours shown.</p>"); continue; }
                h.Append("<table><tr><th>Kind</th><th class=\"num\">Albion</th><th class=\"num\">Midgard</th><th class=\"num\">Hibernia</th><th class=\"num\">All</th></tr>");
                foreach (string kind in kinds)
                {
                    h.Append($"<tr><td>{E(KindName(kind))}</td>");
                    for (int r = 1; r <= 3; r++) h.Append($"<td class=\"num\">{E(Gold(perRealm[r].GetValueOrDefault(kind)))}</td>");
                    h.Append($"<td class=\"num\">{E(Gold(perRealm.Values.Sum(d => d.GetValueOrDefault(kind))))}</td></tr>");
                }
                h.Append("</table>");
            }
            h.Append("</section>");
        }

        // The price index.
        h.Append("<section><h2>Prices at the Realm Exchange</h2>");
        if (s.Bands.Count == 0)
            h.Append("<p class=\"muted\">No price index yet. The server keeps one per kind of item, level band and quality once bot crafters are on.</p>");
        else
        {
            h.Append("<p class=\"muted\">The index price of each kind of item, by level band and quality, from what is listed and what sells. The ten bands with the most stock per realm.</p>");
            for (int realm = 1; realm <= 3; realm++)
            {
                var rows = s.Bands.Where(b => b.Realm == realm).OrderByDescending(b => b.Count).ThenByDescending(b => b.Demand)
                    .ThenBy(b => b.ObjectType).ThenBy(b => b.ItemType).ThenBy(b => b.LevelBand).ThenBy(b => b.QualityTier).Take(10).ToList();
                if (rows.Count == 0) continue;
                h.Append($"<h3 class=\"sub\">{Realm(realm)}</h3><table><tr><th>Item</th><th>Levels</th><th>Quality</th><th class=\"num\">Stock</th>" +
                         "<th class=\"num\">Wanted</th><th class=\"num\">Index</th><th class=\"num\">Floor</th><th class=\"num\">Ceiling</th><th class=\"num\">Sold to players today</th></tr>");
                foreach (var b in rows)
                    h.Append($"<tr><td>{E(ItemName(b.ObjectType, b.ItemType))}</td><td>{b.LevelBand * 5}–{b.LevelBand * 5 + 4}</td><td>{E(TierName(b.QualityTier))}</td>" +
                             $"<td class=\"num\">{b.Count:N0}</td><td class=\"num\">{b.Demand:N0}</td><td class=\"num\">{E(Gold(b.IndexCopper))}</td>" +
                             $"<td class=\"num\">{E(Gold(b.FloorCopper))}</td><td class=\"num\">{E(Gold(b.CeilingCopper))}</td><td class=\"num\">{b.DailyPlayerSales:N0}</td></tr>");
                h.Append("</table>");
            }
        }
        h.Append("</section>");

        if (s.Unreadable.Count > 0)
            h.Append($"<p class=\"muted\">Skipped {s.Unreadable.Count} unreadable value{(s.Unreadable.Count == 1 ? "" : "s")}: {E(string.Join("; ", s.Unreadable.Take(5)))}.</p>");
        h.Append("<footer>Written by the Offline DAoC launcher from this game's own records (docs/ECONOMY-LEDGER.md). Nothing here leaves your computer.</footer>");
        h.Append("</main></body></html>");
        return h.ToString();
    }

    /// <summary>Copper as gold and silver ("12,345g 67s"), the way the ledger's sums read best; under a silver, copper.</summary>
    public static string Gold(long copper)
    {
        string sign = copper < 0 ? "−" : "";
        long abs = copper == long.MinValue ? long.MaxValue : Math.Abs(copper);
        long gold = abs / 10_000, silver = abs % 10_000 / 100, rest = abs % 100;
        if (gold > 0) return $"{sign}{gold.ToString("N0", CultureInfo.InvariantCulture)}g {silver}s";
        if (silver > 0) return $"{sign}{silver}s {rest}c";
        return $"{sign}{rest}c";
    }

    /// <summary>A change: "+1,234g 5s", "−12s 3c", "0c".</summary>
    public static string Signed(long copper) => copper > 0 ? "+" + Gold(copper) : Gold(copper);

    private static string TierName(int tier) => tier switch { 2 => "100%", 1 => "99%", _ => "below 99%" };

    /// <summary>A band's item: the object type (eObjectType) and, for armour, the slot (Item_Type, eInventorySlot).</summary>
    public static string ItemName(int objectType, int itemType)
    {
        string kind = objectType switch
        {
            2 => "Crush weapon", 3 => "Slash weapon", 4 => "Thrust weapon", 5 => "Short bow", 6 => "Two-handed weapon", 7 => "Polearm",
            8 => "Staff", 9 => "Longbow", 10 => "Crossbow", 11 => "Sword", 12 => "Hammer", 13 => "Axe", 14 => "Spear",
            15 => "Composite bow", 16 => "Thrown weapon", 17 => "Left axe", 18 => "Recurve bow", 19 => "Blade", 20 => "Blunt weapon",
            21 => "Piercing weapon", 22 => "Large weapon", 23 => "Celtic spear", 24 => "Flexible weapon", 25 => "Hand to hand",
            26 => "Scythe", 27 => "Fist wraps", 28 => "Mauler staff",
            31 => "Armour", 32 => "Cloth", 33 => "Leather", 34 => "Studded", 35 => "Chain", 36 => "Plate", 37 => "Reinforced", 38 => "Scale",
            41 => "Jewellery", 42 => "Shield", 43 => "Arrows", 44 => "Bolts", 45 => "Instrument", 46 => "Poison", 47 => "Tincture",
            48 => "Spellcraft gem", 0 => "Item",
            _ => "Item type " + objectType.ToString(CultureInfo.InvariantCulture),
        };
        if (objectType is < 31 or > 38) return kind;
        string slot = itemType switch
        {
            21 => "helm", 22 => "gloves", 23 => "boots", 25 => "vest", 27 => "leggings", 28 => "sleeves", _ => "",
        };
        return slot.Length > 0 ? $"{kind} {slot}" : kind;
    }

    private static string When(DateTime utc) => utc.ToLocalTime().ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture);

    private static string Text(SQLiteDataReader reader, int i) =>
        reader.IsDBNull(i) ? "" : Convert.ToString(reader.GetValue(i), CultureInfo.InvariantCulture) ?? "";

    private static long Number(SQLiteDataReader reader, int i) =>
        long.TryParse(Text(reader, i), NumberStyles.Integer, CultureInfo.InvariantCulture, out long value) ? value : 0;

    // The Herald's look (HeraldPage), so the two pages read as one paper.
    private const string Css = """
        :root{--paper:#f3e9d2;--ink:#2b2016;--muted:#7a6a52;--rule:#8a6d3b;--alb:#ab403a;--mid:#3e5e90;--hib:#397542;--card:#efe2c4}
        @media (prefers-color-scheme:dark){:root{--paper:#231f1a;--ink:#e6d9bd;--muted:#a6997f;--rule:#c29d4d;--card:#2c2620;--alb:#d9645d;--mid:#7b9fd6;--hib:#6fb27a}}
        *{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:16px/1.5 Georgia,'Times New Roman',serif}
        main{max-width:980px;margin:0 auto;padding:24px 16px 48px}
        header{text-align:center;margin-bottom:8px}h1{font-size:clamp(30px,6vw,56px);letter-spacing:.04em;margin:6px 0;font-variant:small-caps}
        .rule{border-top:3px double var(--rule);margin:6px 0}.dateline{margin:0;color:var(--muted);font-style:italic}.nav{margin:6px 0 0}
        a{color:var(--ink)}.settings{font-style:italic;color:var(--muted);text-align:center}
        h2{font-variant:small-caps;letter-spacing:.06em;border-bottom:1px solid var(--rule);padding-bottom:4px;margin:32px 0 12px}
        h3{margin:0 0 4px}.sub{margin:18px 0 8px;font-size:17px}
        .realms{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:12px}
        .card{background:var(--card);border-top:4px solid var(--rule);padding:12px 14px}.card.alb{border-color:var(--alb)}.card.mid{border-color:var(--mid)}.card.hib{border-color:var(--hib)}
        .big{font-size:32px;margin:0;line-height:1.1}.caption{margin:0 0 8px;color:var(--muted)}.muted{color:var(--muted)}
        table{width:100%;border-collapse:collapse;font-size:15px}th,td{text-align:left;padding:6px 8px;border-bottom:1px solid color-mix(in srgb,var(--rule) 35%,transparent)}
        th{font-variant:small-caps;letter-spacing:.04em;color:var(--muted);font-weight:normal}.num{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}
        .total td{font-weight:bold}.sinks{padding-left:22px}
        .realm{font-weight:bold}.realm.alb{color:var(--alb)}.realm.mid{color:var(--mid)}.realm.hib{color:var(--hib)}
        section{overflow-x:auto}footer{margin-top:40px;text-align:center;color:var(--muted);font-style:italic;font-size:14px}
        """;
}
