using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DOL.Database;
using DOL.Logging;

namespace DOL.GS.Quests;

// The epic chains, linked by the server as it reads the quest rows (QUESTING.md defect 4, Living Server 1.2).
//
// tools/claude-version/epic_chains.py writes the links into the world (DataQuest rows), but it needs Python, which the
// game does not ship, so nothing ran it on a clean install and /epic said "no epic chain linked by quest IDs". And a
// world it was applied to broke the Guild of Shadows once Living Server was rolled back: the stock server reads the
// "#id" dependencies as quest names, never met (defect 14). So the server now makes the same changes itself, in memory,
// every time it reads the rows: the database keeps the released rows, an uninstall (ROLLBACK.cmd, PREPARE-FOR-UPDATE)
// has nothing to undo, and an Offline DAoC update finds the rows it expects.
//
// This is epic_chains.py's plan, ported line for line (links, pins, closed, rewards, texts, dialogue, with the same
// guards and the same data file, epic_chains_data.json, built into GameServer.dll): a row changes only while it holds
// what the clean world has, so a row someone edited is left alone, and a world the Python tool was already applied to
// reads the same (its rows already hold the result). Every reader of DataQuest rows that builds quests from them goes
// through Link (the data quest cache, a player's quests at login, /epic). Server setting epic_chains_linked (on) is the
// kill switch: off, the rows are read as they are.
public static class EpicChainLinks
{
    public const string ClassicType = "DOL.GS.Quests.ClassicQuestStep";
    public const string LedgerTable = "offline_epic_chains_ledger";
    public static readonly string[] Steps = { "links", "pins", "closed", "rewards", "texts", "dialogue" };
    private const string Resource = "epic_chains_data.json";

    private static readonly string[] TextColumns = { "StepText", "SourceText", "TargetText", "Description", "FinishText" };
    private static readonly string[] DialogueColumns = { "AcceptText", "Description", "SourceText", "StepType", "StepText", "TargetText",
        "AdvanceText", "FinishText", "StepItemTemplates", "CollectItemTemplate", "StartName", "QuestDependency" };
    private static readonly string[] PerStage = { "SourceText", "StepType", "StepText", "TargetText", "AdvanceText", "StepItemTemplates", "CollectItemTemplate" };
    private static readonly string[] Piped = { "QuestDependency" };

    private static readonly Logger Log = LoggerManager.Create(typeof(EpicChainLinks));
    private static readonly Dictionary<string, PropertyInfo> Columns = typeof(DbDataQuest).GetProperties()
        .Where(p => p.PropertyType == typeof(string) && p.CanRead && p.CanWrite).ToDictionary(p => p.Name, StringComparer.Ordinal);

    private static readonly Lazy<JsonDocument> s_data = new(LoadData);
    private static volatile Plan s_plan;

    /// <summary>The plan the server links rows by: the last one made from the full quest table.</summary>
    public static Plan Current => s_plan;

    public static bool Enabled => ServerProperties.Properties.EPIC_CHAINS_LINKED;

    /// <summary>
    /// All of the quest table's rows, as just read from the database: makes the plan from them (as epic_chains.py
    /// would from that world) and applies it to them. Returns the rows.
    /// </summary>
    public static IList<DbDataQuest> Link(IList<DbDataQuest> rows)
    {
        if (rows == null || !Enabled)
            return rows;
        try
        {
            Plan plan = MakePlan(rows, s_data.Value.RootElement);
            s_plan = plan;
            foreach (DbDataQuest row in rows)
                plan.Apply(row);
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_CHAINS linked in memory: {plan.Summary()} (the world database keeps its own rows)");
            NoteLinksInWorld(plan);
        }
        catch (Exception ex)
        {
            Log.Error("EPIC_CHAINS could not link the epic chains; the quest rows are read as they are", ex);
        }
        return rows;
    }

    /// <summary>One row read from the database on its own (a player's quest at login, /epic): the current plan's changes.</summary>
    public static DbDataQuest Link(DbDataQuest row)
    {
        if (row != null && Enabled)
        {
            Plan plan = s_plan;
            if (plan == null)
            {
                // Before the data quest cache is filled (it is filled at server start): plan from the whole table.
                Link(GameServer.Database.SelectAllObjects<DbDataQuest>());
                plan = s_plan;
            }
            plan?.Apply(row);
        }
        return row;
    }

    /// <summary>Forgets the plan (tests).</summary>
    public static void Reset() => s_plan = null;

    /// <summary>
    /// Where the server leaves <see cref="NoteFile"/>: beside it (runtime\server, where classic-quests.json is). Null:
    /// nowhere (tests).
    /// </summary>
    public static string NoteFolder { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// The note that this world's own rows hold the links (epic_chains.py --apply was run on it, as Living Server 1.1
    /// asked). ROLLBACK.cmd and PREPARE-FOR-UPDATE.cmd refuse while it is there: the released server would read those
    /// links as quest names and never offer the Guild of Shadows. epic_chains.py writes it on --apply and removes it
    /// on rollback; the server writes it when it finds the links in the rows and removes it when it doesn't.
    /// </summary>
    public const string NoteFile = "epic-chains-in-world.txt";

    private static void NoteLinksInWorld(Plan plan)
    {
        if (string.IsNullOrEmpty(NoteFolder))
            return;
        try
        {
            string path = Path.Combine(NoteFolder, NoteFile);
            if (plan.LinkedInWorld == 0)
            {
                if (File.Exists(path))
                    File.Delete(path);
                return;
            }
            if (Log.IsWarnEnabled)
                Log.Warn($"EPIC_CHAINS this world's quest rows hold {plan.LinkedInWorld} epic chain links (epic_chains.py --apply was run on it). " +
                         "Before removing Living Server put them back (python tools\\claude-version\\epic_chains.py rollback --apply), or the released " +
                         "server will not offer the Guild of Shadows. ROLLBACK.cmd refuses until then.");
            if (!File.Exists(path))
                File.WriteAllText(path, "The epic chain links are written into this world's quest rows (epic_chains.py --apply).\r\n" +
                    "Before ROLLBACK.cmd or PREPARE-FOR-UPDATE.cmd, put them back: python tools\\claude-version\\epic_chains.py rollback --apply\r\n" +
                    $"Found by the Living Server server at {DateTime.Now:yyyy-MM-dd HH:mm}.\r\n");
        }
        catch (Exception ex)
        {
            Log.Error("EPIC_CHAINS could not write or remove " + NoteFile, ex);
        }
    }

    private static JsonDocument LoadData()
    {
        Assembly assembly = typeof(EpicChainLinks).Assembly;
        string name = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith(Resource, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(Resource + " is not built into GameServer.dll");
        using Stream stream = assembly.GetManifestResourceStream(name);
        return JsonDocument.Parse(stream);
    }

    /// <summary>The data built into GameServer.dll (tools/claude-version/epic_chains_data.json).</summary>
    public static JsonElement Data => s_data.Value.RootElement;

    // The plan

    /// <summary>The cells to change: quest ID -> column -> value, and which step changed each quest.</summary>
    public sealed class Plan
    {
        internal readonly Dictionary<(int Id, string Column), (string Old, string New)> Cells = new();
        internal readonly List<(int Id, string Column)> Order = new();
        internal readonly Dictionary<string, HashSet<int>> Counts = new(StringComparer.Ordinal);
        /// <summary>Guild of Shadows rows whose own QuestDependency already holds the link (written into the world).</summary>
        public int LinkedInWorld { get; internal set; }
        /// <summary>Dialogue rows left as they are: their text changed since the clean world.</summary>
        public readonly List<int> Kept = new();
        private Dictionary<int, List<(string Column, string Value)>> _byQuest;

        /// <summary>The changes, in the order epic_chains.py writes them (cells whose value really changes).</summary>
        public IEnumerable<(int Id, string Column, string Old, string New)> Rows =>
            Order.Select(key => (key.Id, key.Column, Cells[key].Old, Cells[key].New))
                .Where(c => (c.Old ?? string.Empty) != (c.New ?? string.Empty));

        public int Count(string step) => Counts.TryGetValue(step, out HashSet<int> ids) ? ids.Count : 0;

        public string Summary() => string.Join(", ", Steps.Select(step => $"{step} {Count(step)}")) +
                                   (Kept.Count == 0 ? string.Empty : $"; dialogue left as it is (text changed since): {string.Join(", ", Kept.Distinct().OrderBy(i => i))}");

        /// <summary>Writes this plan's values into a row read from the database (never saved: nothing marks it changed).</summary>
        public void Apply(DbDataQuest row)
        {
            _byQuest ??= Rows.GroupBy(c => c.Id).ToDictionary(g => g.Key, g => g.Select(c => (c.Column, c.New)).ToList());
            if (row == null || !_byQuest.TryGetValue(row.ID, out var cells))
                return;
            bool dirty = row.Dirty;
            foreach ((string column, string value) in cells)
                Columns[column].SetValue(row, value);
            row.Dirty = dirty;
        }
    }

    private sealed class Changes
    {
        private readonly Dictionary<int, DbDataQuest> _rows;
        public readonly Plan Plan = new();

        public Changes(IEnumerable<DbDataQuest> rows)
        {
            _rows = new Dictionary<int, DbDataQuest>();
            foreach (DbDataQuest row in rows)
                _rows[row.ID] = row;
        }

        public bool Has(int id) => _rows.ContainsKey(id);

        public string Value(int id, string column)
        {
            if (Plan.Cells.TryGetValue((id, column), out var cell))
                return cell.New;
            return _rows.TryGetValue(id, out DbDataQuest row) ? (string)Columns[column].GetValue(row) : null;
        }

        public void Set(int id, string column, string value, string step)
        {
            if (Plan.Cells.TryGetValue((id, column), out var cell))
                Plan.Cells[(id, column)] = (cell.Old, value);
            else
            {
                Plan.Cells[(id, column)] = (Value(id, column), value);
                Plan.Order.Add((id, column));
            }
            if (!Plan.Counts.TryGetValue(step, out HashSet<int> ids))
                Plan.Counts[step] = ids = new HashSet<int>();
            ids.Add(id);
        }
    }

    /// <summary>epic_chains.py's plan() for these rows and this data. Nothing is written.</summary>
    public static Plan MakePlan(IEnumerable<DbDataQuest> rows, JsonElement data)
    {
        var ch = new Changes(rows);
        PlanLinks(ch, data);
        PlanPins(ch, data, rows);
        PlanClosed(ch, data);
        PlanRewards(ch, data);
        PlanTexts(ch, data);
        PlanDialogue(ch, data);
        return ch.Plan;
    }

    private static int?[] Ids(JsonElement step) =>
        step.GetProperty("ids").EnumerateArray().Select(id => id.ValueKind == JsonValueKind.Null ? (int?)null : id.GetInt32()).ToArray();

    private static string Entry(JsonElement steps, string link, int col)
    {
        bool closes = link.StartsWith('!');
        var ids = link.TrimStart('!').Split('/').Select(key => Ids(steps.GetProperty(key))[col]).Where(id => id != null);
        return (closes ? "!#" : "#") + string.Join("/", ids);
    }

    private static void PlanLinks(Changes ch, JsonElement data)
    {
        JsonElement steps = data.GetProperty("steps");
        foreach (JsonProperty step in steps.EnumerateObject())
        {
            if (!step.Value.TryGetProperty("links", out JsonElement links))
                continue;
            string upstream = step.Value.GetProperty("upstream_dependency").GetString();
            int?[] ids = Ids(step.Value);
            for (int col = 0; col < ids.Length; col++)
            {
                if (ids[col] is not int id)
                    continue;
                string linked = string.Join("|", links.EnumerateArray().Select(link => Entry(steps, link.GetString(), col)));
                string held = ch.Value(id, "QuestDependency") ?? string.Empty;
                if (held != upstream)
                {
                    if (held == linked && ch.Has(id))
                        ch.Plan.LinkedInWorld++;
                    continue;
                }
                ch.Set(id, "QuestDependency", linked, "links");
            }
        }
    }

    private static HashSet<int> Classes(string allowed)
    {
        var classes = new HashSet<int>();
        foreach (string part in (allowed ?? string.Empty).Split('|', ';', ','))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0 && trimmed.All(char.IsAsciiDigit))
                classes.Add(int.Parse(trimmed));
        }
        return classes;
    }

    private static bool Shares(HashSet<int> mine, HashSet<int> theirs) => mine.Count == 0 || theirs.Count == 0 || mine.Overlaps(theirs);

    private static void PlanPins(Changes ch, JsonElement data, IEnumerable<DbDataQuest> rows)
    {
        var gos = new HashSet<int>(data.GetProperty("steps").EnumerateObject().SelectMany(s => Ids(s.Value)).Where(i => i != null).Select(i => i.Value));
        var quests = rows.Where(r => (r.ClassType ?? string.Empty).Contains(ClassicType, StringComparison.Ordinal))
            .Select(r => (r.ID, r.Name ?? string.Empty, (int)r.MinLevel, r.AllowedClasses, r.QuestDependency ?? string.Empty)).ToList();
        var byName = new Dictionary<string, List<(int Id, string Name, int Level, string Allowed, string Dependency)>>(StringComparer.Ordinal);
        foreach (var quest in quests)
        {
            string key = quest.Item2.ToLowerInvariant();
            if (!byName.TryGetValue(key, out var list))
                byName[key] = list = new();
            list.Add(quest);
        }
        foreach ((int id, string name, int level, string allowed, string dependency) in quests)
        {
            string[] entries = dependency.Split('|').Where(e => e.Length > 0).ToArray();
            if (gos.Contains(id) || entries.Length == 0 || entries.Any(e => e.Trim().StartsWith('#') || e.Trim().StartsWith("!#", StringComparison.Ordinal)))
                continue;
            HashSet<int> mine = Classes(allowed);
            var pinned = new List<string>();
            bool anyPinned = false;
            foreach (string entry in entries)
            {
                var before = (byName.TryGetValue(entry.ToLowerInvariant(), out var named) ? named : new())
                    .Where(q => q.Id != id && q.Level < level && Shares(mine, Classes(q.Allowed))).ToList();
                var levels = before.Select(q => q.Level).ToHashSet();
                if (before.Count > 0 && (entry.ToLowerInvariant() == name.ToLowerInvariant() || levels.Count > 1))
                {
                    int top = levels.Max();
                    pinned.Add("#" + string.Join("/", before.Where(q => q.Level == top).Select(q => q.Id).OrderBy(i => i)));
                    anyPinned = true;
                }
                else
                    pinned.Add(entry);
            }
            if (anyPinned)
                ch.Set(id, "QuestDependency", string.Join("|", pinned), "pins");
        }
    }

    private static void PlanClosed(Changes ch, JsonElement data)
    {
        JsonElement closed = data.GetProperty("steps").GetProperty("7closed");
        string upstream = closed.GetProperty("upstream_dependency").GetString();
        foreach (int? id in Ids(closed))
        {
            if (id is int quest && (ch.Value(quest, "QuestDependency") ?? string.Empty) == upstream)
                ch.Set(quest, "QuestDependency", "#" + quest, "closed");
        }
    }

    private static void PlanRewards(Changes ch, JsonElement data)
    {
        foreach (JsonElement fix in data.GetProperty("rewards").EnumerateArray())
        {
            int id = fix.GetProperty("id").GetInt32();
            if ((ch.Value(id, "FinalRewardItemTemplates") ?? string.Empty) == fix.GetProperty("upstream").GetString())
                ch.Set(id, "FinalRewardItemTemplates", fix.GetProperty("value").GetString(), "rewards");
        }
    }

    private static void PlanTexts(Changes ch, JsonElement data)
    {
        foreach (JsonElement fix in data.GetProperty("texts").EnumerateArray())
        {
            string column = fix.GetProperty("column").GetString();
            if (!TextColumns.Contains(column))
                throw new InvalidDataException("unknown text column " + column);
            var replace = fix.GetProperty("replace").EnumerateArray().Select(pair => (Old: pair[0].GetString(), New: pair[1].GetString())).ToList();
            foreach (JsonElement idElement in fix.GetProperty("ids").EnumerateArray())
            {
                int id = idElement.GetInt32();
                string text = ch.Value(id, column);
                if (text == null || !replace.All(r => text.Contains(r.Old, StringComparison.Ordinal)))
                    continue;
                foreach ((string old, string @new) in replace)
                    text = text.Replace(old, @new, StringComparison.Ordinal);
                ch.Set(id, column, text, "texts");
            }
        }
    }

    // A dialogue value: one text for every class, or an object with each class's text.
    private static Dictionary<string, string> PerClass(JsonElement value, IReadOnlyList<string> classes, string what, bool piped = false)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            string text = value.GetString();
            if (text.Contains('|') && !piped)
                throw new InvalidDataException(what + ": a value contains '|'");
            return classes.ToDictionary(c => c, _ => text, StringComparer.Ordinal);
        }
        if (value.ValueKind != JsonValueKind.Object ||
            !value.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).SequenceEqual(classes.OrderBy(n => n, StringComparer.Ordinal)))
            throw new InvalidDataException(what + ": expected a string or an object with every class");
        var byClass = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (JsonProperty entry in value.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String || (entry.Value.GetString().Contains('|') && !piped))
                throw new InvalidDataException(what + ": a per-class value is not a string, or contains '|'");
            byClass[entry.Name] = entry.Value.GetString();
        }
        return byClass;
    }

    private static Dictionary<string, string> Resolve(string column, JsonElement value, IReadOnlyList<string> classes, Dictionary<string, int?> stages)
    {
        if (!DialogueColumns.Contains(column))
            throw new InvalidDataException("unknown column " + column);
        if (value.ValueKind != JsonValueKind.Array)
        {
            if (column == "StepType")
                throw new InvalidDataException("StepType: a list with one entry for each stage");
            return PerClass(value, classes, column, Piped.Contains(column));
        }
        if (!PerStage.Contains(column))
            throw new InvalidDataException(column + ": a list is only for the per-stage columns");
        var entries = value.EnumerateArray().Select(entry => PerClass(entry, classes, column)).ToList();
        return classes.ToDictionary(name => name,
            name => stages[name] is int count && entries.Count != count ? null : string.Join("|", entries.Select(entry => entry[name])),
            StringComparer.Ordinal);
    }

    private static void PlanDialogue(Changes ch, JsonElement data)
    {
        List<string> classes = data.GetProperty("classes").EnumerateObject().Select(p => p.Name).ToList();
        JsonElement steps = data.GetProperty("steps");
        foreach (JsonElement entry in data.GetProperty("dialogue").EnumerateArray())
        {
            int?[] ids = Ids(steps.GetProperty(entry.GetProperty("step").GetString()));
            var byClass = new List<(string Class, int Id)>();
            for (int i = 0; i < classes.Count && i < ids.Length; i++)
                if (ids[i] is int id)
                    byClass.Add((classes[i], id));
            JsonElement set = entry.GetProperty("set");
            List<string> columns = set.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
            var stages = classes.ToDictionary(c => c, _ => (int?)null, StringComparer.Ordinal);
            foreach ((string c, int id) in byClass)
                stages[c] = ch.Value(id, "StepType") is string types ? types.Split('|').Length : null;
            var resolved = columns.ToDictionary(column => column, column => Resolve(column, set.GetProperty(column), classes, stages), StringComparer.Ordinal);
            JsonElement guard = entry.GetProperty("guard");
            foreach ((string c, int id) in byClass)
            {
                if (ch.Value(id, "StepType") == null)
                    continue;
                List<string> values = columns.Select(column => resolved[column][c]).ToList();
                List<string> held = columns.Select(column => ch.Value(id, column) ?? string.Empty).ToList();
                bool guarded = guard.TryGetProperty(id.ToString(), out JsonElement digests) &&
                               digests.EnumerateArray().Any(d => d.GetString() == Digest(held));
                if (values.Contains(null) || (!held.SequenceEqual(values) && !guarded))
                {
                    ch.Plan.Kept.Add(id);
                    continue;
                }
                for (int k = 0; k < columns.Count; k++)
                    ch.Set(id, columns[k], values[k], "dialogue");
            }
        }
    }

    /// <summary>
    /// epic_chains.py's guard digest: SHA-256 of json.dumps(values, ensure_ascii=False) in UTF-8, so the same texts
    /// give the same digest in both. Python writes '["a", "b"]', escaping the quote, the backslash and the control
    /// characters, and leaves every other character as it is.
    /// </summary>
    public static string Digest(IEnumerable<string> values)
    {
        var json = new StringBuilder("[");
        bool first = true;
        foreach (string value in values)
        {
            if (!first)
                json.Append(", ");
            first = false;
            json.Append('"');
            foreach (char c in value ?? string.Empty)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    case '\b': json.Append("\\b"); break;
                    case '\f': json.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                            json.Append("\\u").Append(((int)c).ToString("x4"));
                        else
                            json.Append(c);
                        break;
                }
            }
            json.Append('"');
        }
        json.Append(']');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json.ToString()))).ToLowerInvariant();
    }
}
