using System.Data.SQLite;
using System.Globalization;
using System.Net;
using System.Text;

namespace OfflineDaoc.Launcher;

/// <summary>
/// The Camelot Herald (docs/LIVING-SERVER.md): a static news page about the world, written from the game
/// database by the launcher. It reads only (the database is opened read-only, so it is safe while the server
/// runs), needs no web server and nothing listens on a port. The page is one self-contained HTML file in the
/// game's runtime\herald folder, opened in the default browser.
/// </summary>
public static class HeraldPage
{
    private static readonly string[] RealmNames = ["", "Albion", "Midgard", "Hibernia"];
    private static readonly string[] RealmCss = ["", "alb", "mid", "hib"];

    // Classic and Shrouded Isles class ids (eCharacterClass), for player characters; bots store their class name.
    private static readonly Dictionary<int, string> ClassNames = new()
    {
        [1] = "Paladin", [2] = "Armsman", [3] = "Scout", [4] = "Minstrel", [5] = "Theurgist", [6] = "Cleric",
        [7] = "Wizard", [8] = "Sorcerer", [9] = "Infiltrator", [10] = "Friar", [11] = "Mercenary", [12] = "Necromancer",
        [13] = "Cabalist", [14] = "Fighter", [15] = "Elementalist", [16] = "Acolyte", [17] = "Rogue", [18] = "Mage",
        [19] = "Reaver", [20] = "Disciple", [21] = "Thane", [22] = "Warrior", [23] = "Shadowblade", [24] = "Skald",
        [25] = "Hunter", [26] = "Healer", [27] = "Spiritmaster", [28] = "Shaman", [29] = "Runemaster", [30] = "Bonedancer",
        [31] = "Berserker", [32] = "Savage", [33] = "Heretic", [34] = "Valkyrie", [35] = "Viking", [36] = "Mystic",
        [37] = "Seer", [38] = "Rogue", [39] = "Bainshee", [40] = "Eldritch", [41] = "Enchanter", [42] = "Mentalist",
        [43] = "Blademaster", [44] = "Hero", [45] = "Champion", [46] = "Warden", [47] = "Druid", [48] = "Bard",
        [49] = "Nightshade", [50] = "Ranger", [51] = "Magician", [52] = "Guardian", [53] = "Naturalist", [54] = "Stalker",
        [55] = "Animist", [56] = "Valewalker", [57] = "Forester", [58] = "Vampiir", [59] = "Warlock", [63] = "Sluaghbinder",
    };

    // The six relics of the classic frontier: (realm, relicType) -> name. relicType 0 = strength, 1 = power.
    private static readonly Dictionary<(int Realm, int Type), string> RelicNames = new()
    {
        [(1, 0)] = "Scabbard of Excalibur", [(1, 1)] = "Merlin's Staff",
        [(2, 0)] = "Thor's Hammer", [(2, 1)] = "Horn of Valhalla",
        [(3, 0)] = "Lug's Spear of Lightning", [(3, 1)] = "Cauldron of Dagda",
    };

    public sealed record Character(string Name, int Realm, string ClassName, int Level, long RealmPoints, string Guild, bool IsBot);

    /// <summary>Realm rank and level ("RR3L4") for a realm point total, as the server's table counts them.</summary>
    public static string RealmRank(long realmPoints)
    {
        // As GamePlayer.CalculateRealmLevelFromRPs: no realm points is RR1L0, any points at all at least RR1L1.
        if (realmPoints <= 0) return "RR1L0";
        int index = 0;
        for (int level = RealmPointsForLevel.Length - 1; level > 0; level--)
        {
            if (RealmPointsForLevel[level] <= realmPoints) { index = level; break; }
        }
        return $"RR{index / 10 + 1}L{index % 10}";
    }

    // GamePlayer.REALMPOINTS_FOR_LEVEL beyond its table follows 25/3 n^3 - 25/2 n^2 + 25/6 n; computed up to RR10.
    private static readonly long[] RealmPointsForLevel = Enumerable.Range(0, 100)
        .Select(n => n <= 1 ? 0L : (long)Math.Round(25.0 / 3.0 * n * n * n - 25.0 / 2.0 * n * n + 25.0 / 6.0 * n))
        .ToArray();

    /// <summary>Writes the Herald for this game and returns the file's path.</summary>
    public static string Write(string database, string recordsPath, string outputFolder, DateTime nowLocal)
    {
        string html = Build(database, recordsPath, nowLocal);
        Directory.CreateDirectory(outputFolder);
        string path = Path.Combine(outputFolder, "index.html");
        string temporary = path + ".tmp";
        File.WriteAllText(temporary, html, new UTF8Encoding(false));
        File.Move(temporary, path, overwrite: true);
        return path;
    }

    public static string Build(string database, string recordsPath, DateTime nowLocal)
    {
        if (!File.Exists(database)) throw new InvalidOperationException("The world database is missing: " + database);
        using var c = new SQLiteConnection(new SQLiteConnectionStringBuilder
        { DataSource = database, ReadOnly = true, FailIfMissing = true, Pooling = false, DefaultTimeout = 10 }.ConnectionString);
        c.Open();
        var tables = Tables(c);

        var guildNames = new Dictionary<string, (string Name, int Realm, long RealmPoints, long Merit)>(StringComparer.Ordinal);
        if (tables.Contains("Guild"))
        {
            foreach (var r in Rows(c, "SELECT GuildID, GuildName, Realm, RealmPoints, MeritPoints FROM Guild WHERE COALESCE(IsStartingGuild,0)=0"))
                guildNames[Str(r[0])] = (Str(r[1]), Int(r[2]), Long(r[3]), Long(r[4]));
        }
        // A gamebot's guild, one per bot so it is counted once: a bot guild's member (offline_bot_guild_members), or a
        // player's guild it was invited into (offline_world_bot_guild, AutonomousBotGuilds), which wins over a bot-guild
        // row for the same bot. Before 1.1.2 only the first table was read, so a player's guild of 30 bots showed 1 member.
        var botGuild = new Dictionary<long, string>();
        if (tables.Contains("offline_bot_guild_members"))
            foreach (var r in Rows(c, "SELECT BotId, GuildId FROM offline_bot_guild_members"))
                botGuild[Long(r[0])] = Str(r[1]);
        if (tables.Contains("offline_world_bot_guild"))
            foreach (var r in Rows(c, "SELECT BotId, GuildID FROM offline_world_bot_guild"))
                botGuild[Long(r[0])] = Str(r[1]);

        var characters = new List<Character>();
        if (tables.Contains("DOLCharacters"))
        {
            foreach (var r in Rows(c, "SELECT Name, Realm, Class, Level, RealmPoints, GuildID FROM DOLCharacters"))
            {
                string guild = guildNames.TryGetValue(Str(r[5]), out var g) ? g.Name : "";
                characters.Add(new Character(Str(r[0]), Int(r[1]), ClassNames.GetValueOrDefault(Int(r[2]), "Adventurer"),
                    Int(r[3]), Long(r[4]), guild, false));
            }
        }
        if (tables.Contains("offline_world_bots"))
        {
            foreach (var r in Rows(c, "SELECT BotId, Name, Realm, ClassName, Level, RealmPoints FROM offline_world_bots WHERE COALESCE(IsRetired,0)=0"))
            {
                string guild = botGuild.TryGetValue(Long(r[0]), out string? id) && guildNames.TryGetValue(id, out var g) ? g.Name : "";
                characters.Add(new Character(Str(r[1]), Int(r[2]), Str(r[3]), Int(r[4]), Long(r[5]), guild, true));
            }
        }
        characters.RemoveAll(ch => ch.Realm is < 1 or > 3 || string.IsNullOrWhiteSpace(ch.Name));

        var keeps = new List<(string Name, int Realm, int Original, int Level, string Claim)>();
        if (tables.Contains("Keep"))
            foreach (var r in Rows(c, "SELECT Name, Realm, OriginalRealm, Level, COALESCE(ClaimedGuildName,'') FROM Keep WHERE OriginalRealm IN (1,2,3)"))
                keeps.Add((Str(r[0]), Int(r[1]), Int(r[2]), Int(r[3]), Str(r[4])));

        var relics = new List<(int Realm, int Original, int Type, string Captured)>();
        if (tables.Contains("Relic"))
            foreach (var r in Rows(c, "SELECT Realm, OriginalRealm, relicType, COALESCE(LastCaptureDate,'') FROM Relic WHERE OriginalRealm IN (1,2,3)"))
                relics.Add((Int(r[0]), Int(r[1]), Int(r[2]), Str(r[3])));

        var firsts = new List<(string Title, int Realm, string Name, string ClassName, string When)>();
        if (tables.Contains("offline_server_firsts"))
            foreach (var r in Rows(c, "SELECT Title, Realm, Name, ClassName, AchievedUtc FROM offline_server_firsts WHERE Name <> '' ORDER BY AchievedUtc DESC LIMIT 40"))
                firsts.Add((Str(r[0]), Int(r[1]), Str(r[2]), Str(r[3]), Str(r[4])));

        var wanted = new List<(string Name, int Realm, int Kills, int Hunters)>();
        if (tables.Contains("offline_bot_nemesis"))
            foreach (var r in Rows(c, """
                SELECT KillerName, KillerRealm, SUM(Kills), COUNT(*) FROM offline_bot_nemesis
                GROUP BY KillerName, KillerRealm ORDER BY SUM(Kills) DESC LIMIT 10
                """))
                wanted.Add((Str(r[0]), Int(r[1]), Int(r[2]), Int(r[3])));

        List<DOL.GS.RealmEventRecord> events = [];
        try
        {
            if (File.Exists(recordsPath)) events = DOL.GS.RealmEventRecordStore.Read(recordsPath, "", "", "", "", 0, 12).Rows;
        }
        catch (Exception) { /* the ledger is optional news */ }

        return Render(nowLocal, characters, guildNames, botGuild, keeps, relics, firsts, wanted, events);
    }

    private static string Render(DateTime now, List<Character> characters,
        Dictionary<string, (string Name, int Realm, long RealmPoints, long Merit)> guilds, Dictionary<long, string> botGuild,
        List<(string Name, int Realm, int Original, int Level, string Claim)> keeps,
        List<(int Realm, int Original, int Type, string Captured)> relics,
        List<(string Title, int Realm, string Name, string ClassName, string When)> firsts,
        List<(string Name, int Realm, int Kills, int Hunters)> wanted, List<DOL.GS.RealmEventRecord> events)
    {
        var h = new StringBuilder();
        string E(string s) => WebUtility.HtmlEncode(s);
        string Realm(int realm) => realm is >= 1 and <= 3 ? $"<span class=\"realm {RealmCss[realm]}\">{RealmNames[realm]}</span>" : "";

        h.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        h.Append("<title>The Camelot Herald</title><style>").Append(Css).Append("</style></head><body><main>");
        h.Append("<header><div class=\"rule\"></div><h1>The Camelot Herald</h1>");
        h.Append($"<p class=\"dateline\">News from the three realms · Offline DAoC · {E(now.ToString("dddd, d MMMM yyyy, h:mm tt", CultureInfo.InvariantCulture))}</p>");
        h.Append("<div class=\"rule\"></div></header>");

        // The war: relics and keeps.
        h.Append("<section><h2>The War</h2><div class=\"realms\">");
        for (int realm = 1; realm <= 3; realm++)
        {
            int held = keeps.Count(k => k.Realm == realm);
            int taken = keeps.Count(k => k.Realm == realm && k.Original != realm);
            var owned = relics.Where(r => r.Realm == realm).ToList();
            h.Append($"<div class=\"card {RealmCss[realm]}\"><h3>{RealmNames[realm]}</h3>");
            h.Append($"<p class=\"big\">{held}</p><p class=\"caption\">keeps held{(taken > 0 ? $", {taken} taken from the enemy" : "")}</p>");
            h.Append("<ul class=\"relics\">");
            if (owned.Count == 0) h.Append("<li class=\"muted\">No relics on its shrines</li>");
            foreach (var relic in owned.OrderBy(r => r.Original).ThenBy(r => r.Type))
            {
                string name = RelicNames.GetValueOrDefault((relic.Original, relic.Type), "a relic");
                h.Append(relic.Original == realm
                    ? $"<li>{E(name)}</li>"
                    : $"<li class=\"captured\">{E(name)} <span class=\"muted\">captured from {RealmNames[relic.Original]}</span></li>");
            }
            h.Append("</ul></div>");
        }
        h.Append("</div>");
        var captured = keeps.Where(k => k.Realm != k.Original && k.Realm is >= 1 and <= 3).OrderBy(k => k.Name).ToList();
        if (captured.Count > 0)
        {
            h.Append("<h3 class=\"sub\">Keeps in enemy hands</h3><table><tr><th>Keep</th><th>Held by</th><th>Built by</th><th>Claimed by</th></tr>");
            foreach (var k in captured)
                h.Append($"<tr><td>{E(k.Name)}</td><td>{Realm(k.Realm)}</td><td>{Realm(k.Original)}</td><td>{E(k.Claim)}</td></tr>");
            h.Append("</table>");
        }
        else h.Append("<p class=\"muted\">Every keep stands in the hands of the realm that built it.</p>");
        h.Append("</section>");

        // Server firsts.
        h.Append("<section><h2>Server Firsts</h2>");
        if (firsts.Count == 0) h.Append("<p class=\"muted\">No firsts recorded yet. The first of each class to reach level 50 or a new realm rank will be named here.</p>");
        else
        {
            h.Append("<table><tr><th>Achievement</th><th>Who</th><th>Realm</th><th>When</th></tr>");
            foreach (var f in firsts)
                h.Append($"<tr><td>{E(f.Title)}</td><td>{E(f.Name)} <span class=\"muted\">{E(f.ClassName)}</span></td><td>{Realm(f.Realm)}</td><td>{E(When(f.When))}</td></tr>");
            h.Append("</table>");
        }
        h.Append("</section>");

        // Realm point leaders.
        var leaders = characters.Where(ch => ch.RealmPoints > 0).OrderByDescending(ch => ch.RealmPoints).ThenBy(ch => ch.Name).Take(20).ToList();
        h.Append("<section><h2>Realm Point Leaders</h2>");
        if (leaders.Count == 0) h.Append("<p class=\"muted\">No one has earned realm points yet. The frontier awaits.</p>");
        else
        {
            h.Append("<table><tr><th>#</th><th>Name</th><th>Class</th><th>Realm</th><th>Guild</th><th class=\"num\">Realm points</th><th>Rank</th></tr>");
            int place = 1;
            foreach (var ch in leaders)
                h.Append($"<tr><td>{place++}</td><td>{E(ch.Name)}</td><td>{E(ch.ClassName)} {ch.Level}</td><td>{Realm(ch.Realm)}</td><td>{E(ch.Guild)}</td>" +
                         $"<td class=\"num\">{ch.RealmPoints:N0}</td><td>{RealmRank(ch.RealmPoints)}</td></tr>");
            h.Append("</table>");
        }
        h.Append("</section>");

        // Most wanted (nemesis records).
        if (wanted.Count > 0)
        {
            h.Append("<section><h2>Most Wanted</h2><p class=\"muted\">The enemies the realms most want to settle a score with.</p>");
            h.Append("<table><tr><th>Name</th><th>Realm</th><th class=\"num\">Kills</th><th class=\"num\">Hunted by</th></tr>");
            foreach (var w in wanted)
                h.Append($"<tr><td>{E(w.Name)}</td><td>{Realm(w.Realm)}</td><td class=\"num\">{w.Kills:N0}</td><td class=\"num\">{w.Hunters:N0}</td></tr>");
            h.Append("</table></section>");
        }

        // Guilds.
        var guildRows = guilds.Select(g => new
            {
                g.Value.Name, g.Value.Realm, g.Value.RealmPoints, g.Value.Merit,
                Members = characters.Count(ch => ch.Guild == g.Value.Name && ch.Realm == g.Value.Realm),
            })
            .Where(g => g.Members > 0 && g.Realm is >= 1 and <= 3)
            .OrderByDescending(g => g.RealmPoints).ThenByDescending(g => g.Members).ThenBy(g => g.Name).Take(15).ToList();
        h.Append("<section><h2>Guilds of Note</h2>");
        if (guildRows.Count == 0) h.Append("<p class=\"muted\">No guilds have formed yet.</p>");
        else
        {
            h.Append("<table><tr><th>Guild</th><th>Realm</th><th class=\"num\">Members</th><th class=\"num\">Realm points</th><th class=\"num\">Merit</th></tr>");
            foreach (var g in guildRows)
                h.Append($"<tr><td>{E(g.Name)}</td><td>{Realm(g.Realm)}</td><td class=\"num\">{g.Members:N0}</td><td class=\"num\">{g.RealmPoints:N0}</td><td class=\"num\">{g.Merit:N0}</td></tr>");
            h.Append("</table>");
        }
        h.Append("</section>");

        // Population census.
        h.Append("<section><h2>Census</h2><table><tr><th>Realm</th><th class=\"num\">Characters</th><th class=\"num\">Level 50</th><th class=\"num\">Level 40–49</th><th class=\"num\">Level 20–39</th><th class=\"num\">Below 20</th><th>Most common class</th></tr>");
        for (int realm = 1; realm <= 3; realm++)
        {
            var people = characters.Where(ch => ch.Realm == realm).ToList();
            string common = people.GroupBy(ch => ch.ClassName).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).Select(g => g.Key).FirstOrDefault() ?? "";
            h.Append($"<tr><td>{Realm(realm)}</td><td class=\"num\">{people.Count:N0}</td><td class=\"num\">{people.Count(p => p.Level >= 50):N0}</td>" +
                     $"<td class=\"num\">{people.Count(p => p.Level is >= 40 and < 50):N0}</td><td class=\"num\">{people.Count(p => p.Level is >= 20 and < 40):N0}</td>" +
                     $"<td class=\"num\">{people.Count(p => p.Level < 20):N0}</td><td>{E(common)}</td></tr>");
        }
        h.Append("</table></section>");

        // Realm events.
        h.Append("<section><h2>From the Frontier and Beyond</h2>");
        if (events.Count == 0) h.Append("<p class=\"muted\">No realm events recorded yet.</p>");
        else
        {
            h.Append("<table><tr><th>Event</th><th>Realm</th><th>Outcome</th><th>Began</th></tr>");
            foreach (var e in events)
                h.Append($"<tr><td>{E(e.Name)}</td><td>{E(e.Realm)}</td><td>{E(e.Outcome)}</td><td>{E(When(e.StartedUtc))}</td></tr>");
            h.Append("</table>");
        }
        h.Append("</section>");

        h.Append("<footer>Written by the Offline DAoC launcher from this game's own records. Nothing here leaves your computer.</footer>");
        h.Append("</main></body></html>");
        return h.ToString();
    }

    private static string When(string utc) =>
        DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime t) && t.Year > 2000
            ? t.ToLocalTime().ToString("d MMM yyyy, h:mm tt", CultureInfo.InvariantCulture)
            : "";

    private static HashSet<string> Tables(SQLiteConnection c) =>
        Rows(c, "SELECT name FROM sqlite_master WHERE type='table'").Select(r => Str(r[0])).ToHashSet(StringComparer.Ordinal);

    private static List<object?[]> Rows(SQLiteConnection c, string sql)
    {
        using var command = c.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var rows = new List<object?[]>();
        while (reader.Read())
        {
            var row = new object?[reader.FieldCount];
            for (int i = 0; i < row.Length; i++) row[i] = reader.IsDBNull(i) ? null : reader.GetValue(i);
            rows.Add(row);
        }
        return rows;
    }

    private static string Str(object? v) => Convert.ToString(v, CultureInfo.InvariantCulture) ?? "";
    private static int Int(object? v) => v is null ? 0 : int.TryParse(Str(v), NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : 0;
    private static long Long(object? v) => v is null ? 0 : long.TryParse(Str(v), NumberStyles.Integer, CultureInfo.InvariantCulture, out long l) ? l : 0;

    private const string Css = """
        :root{--paper:#f3e9d2;--ink:#2b2016;--muted:#7a6a52;--rule:#8a6d3b;--alb:#ab403a;--mid:#3e5e90;--hib:#397542;--card:#efe2c4}
        @media (prefers-color-scheme:dark){:root{--paper:#231f1a;--ink:#e6d9bd;--muted:#a6997f;--rule:#c29d4d;--card:#2c2620;--alb:#d9645d;--mid:#7b9fd6;--hib:#6fb27a}}
        *{box-sizing:border-box}body{margin:0;background:var(--paper);color:var(--ink);font:16px/1.5 Georgia,'Times New Roman',serif}
        main{max-width:980px;margin:0 auto;padding:24px 16px 48px}
        header{text-align:center;margin-bottom:8px}h1{font-size:clamp(34px,7vw,64px);letter-spacing:.04em;margin:6px 0;font-variant:small-caps}
        .rule{border-top:3px double var(--rule);margin:6px 0}.dateline{margin:0;color:var(--muted);font-style:italic}
        h2{font-variant:small-caps;letter-spacing:.06em;border-bottom:1px solid var(--rule);padding-bottom:4px;margin:32px 0 12px}
        h3{margin:0 0 4px}.sub{margin:18px 0 8px;font-size:17px}
        .realms{display:grid;grid-template-columns:repeat(auto-fit,minmax(240px,1fr));gap:12px}
        .card{background:var(--card);border-top:4px solid var(--rule);padding:12px 14px}.card.alb{border-color:var(--alb)}.card.mid{border-color:var(--mid)}.card.hib{border-color:var(--hib)}
        .big{font-size:40px;margin:0;line-height:1.1}.caption{margin:0 0 8px;color:var(--muted)}
        .relics{margin:0;padding-left:18px}.captured{font-weight:bold}.muted{color:var(--muted)}
        table{width:100%;border-collapse:collapse;font-size:15px}th,td{text-align:left;padding:6px 8px;border-bottom:1px solid color-mix(in srgb,var(--rule) 35%,transparent)}
        th{font-variant:small-caps;letter-spacing:.04em;color:var(--muted);font-weight:normal}.num{text-align:right;font-variant-numeric:tabular-nums}
        .realm{font-weight:bold}.realm.alb{color:var(--alb)}.realm.mid{color:var(--mid)}.realm.hib{color:var(--hib)}
        section{overflow-x:auto}footer{margin-top:40px;text-align:center;color:var(--muted);font-style:italic;font-size:14px}
        """;
}
