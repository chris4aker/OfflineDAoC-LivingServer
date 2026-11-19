using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.GS;
using DOL.GS.Quests;

namespace DOL.UnitTests.QuestHarness
{
    /// <summary>
    /// A game's world database, read for the quest harness. The file itself is only ever copied: the copy, in a
    /// folder of its own under %TEMP%, is opened read-only (with its -wal file, so a world whose server closed
    /// uncleanly is read as the server would read it), and the original's SHA-256 is checked unchanged afterwards.
    /// </summary>
    public sealed class WorldDatabase : IDisposable
    {
        public string Original { get; }
        public string OriginalSha256 { get; }
        private readonly string _folder;
        private readonly SQLiteConnection _connection;

        public WorldDatabase(string path)
        {
            Original = Path.GetFullPath(path);
            if (!File.Exists(Original))
                throw new FileNotFoundException("No world database at " + Original, Original);
            OriginalSha256 = Sha256(Original);
            _folder = TemporarySqlite.NewFolder("quest-harness-world");
            string copy = Path.Combine(_folder, "world.sqlite3.db");
            CopyShared(Original, copy);
            if (File.Exists(Original + "-wal"))
                CopyShared(Original + "-wal", copy + "-wal");
            _connection = new SQLiteConnection($"Data Source={copy};Read Only=True;FailIfMissing=True;Pooling=False");
            _connection.Open();
            using SQLiteCommand query = _connection.CreateCommand();
            query.CommandText = "PRAGMA query_only = 1";
            query.ExecuteNonQuery();
        }

        // Read with sharing, so a world the server has open (it shouldn't be) is still read rather than locked.
        private static void CopyShared(string from, string to)
        {
            using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            source.CopyTo(target);
        }

        public static string Sha256(string path)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return System.Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        }

        public bool HasTable(string name)
        {
            using SQLiteCommand query = _connection.CreateCommand();
            query.CommandText = "SELECT 1 FROM sqlite_master WHERE type='table' AND name=@name COLLATE NOCASE";
            query.Parameters.AddWithValue("@name", name);
            return query.ExecuteScalar() != null;
        }

        /// <summary>Every row of the object's table, as the object (columns matched to its data properties by name).</summary>
        public List<T> All<T>() where T : DataObject, new()
        {
            string table = typeof(T).GetCustomAttribute<DataTable>()?.TableName ?? typeof(T).Name;
            var rows = new List<T>();
            if (!HasTable(table))
                return rows;
            PropertyInfo[] properties = typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .Where(p => p.CanWrite && (p.GetCustomAttribute<DataElement>() != null || p.GetCustomAttribute<PrimaryKey>() != null))
                .ToArray();
            using SQLiteCommand query = _connection.CreateCommand();
            query.CommandText = $"SELECT * FROM \"{table}\"";
            using SQLiteDataReader reader = query.ExecuteReader();
            var columns = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
                columns[reader.GetName(i)] = i;
            while (reader.Read())
            {
                var row = new T();
                foreach (PropertyInfo property in properties)
                {
                    if (!columns.TryGetValue(property.Name, out int at) || reader.IsDBNull(at))
                        continue;
                    property.SetValue(row, ToProperty(reader.GetValue(at), property.PropertyType));
                }
                row.IsPersisted = true;
                row.Dirty = false;
                rows.Add(row);
            }
            return rows;
        }

        /// <summary>(Name, Region) of every row of a table with those columns (Mob, WorldObject).</summary>
        public List<(string Name, ushort Region)> Places(string table)
        {
            var places = new List<(string, ushort)>();
            if (!HasTable(table))
                return places;
            using SQLiteCommand query = _connection.CreateCommand();
            query.CommandText = $"SELECT Name, Region FROM \"{table}\"";
            using SQLiteDataReader reader = query.ExecuteReader();
            while (reader.Read())
            {
                if (!reader.IsDBNull(0))
                    places.Add((reader.GetValue(0).ToString(), reader.IsDBNull(1) ? (ushort)0 : System.Convert.ToUInt16(reader.GetValue(1))));
            }
            return places;
        }

        private static object ToProperty(object value, Type type)
        {
            Type target = Nullable.GetUnderlyingType(type) ?? type;
            if (target == typeof(string))
                return value.ToString();
            if (target == typeof(bool))
                return value is string text ? text is "1" or "true" or "True" : System.Convert.ToInt64(value) != 0;
            if (target == typeof(DateTime))
                return value is DateTime date ? date : DateTime.TryParse(value.ToString(), out DateTime parsed) ? parsed : DateTime.MinValue;
            if (target.IsEnum)
                return Enum.ToObject(target, System.Convert.ToInt64(value));
            return System.Convert.ChangeType(value, target, System.Globalization.CultureInfo.InvariantCulture);
        }

        public void Dispose()
        {
            _connection.Dispose();
            TemporarySqlite.DeleteFolder(_folder);
        }
    }

    /// <summary>One quest row of the world: the harness's walks and what the world lacks for it.</summary>
    public sealed class QuestRowReport
    {
        public DbDataQuest Row;
        public readonly List<QuestWalk> Walks = new();
        public readonly List<string> WorldProblems = new();
        public bool Passed => Walks.Count > 0 && Walks.All(w => w.Passed);
        /// <summary>The harness failed on this row (not the quest): what to fix in the harness, or null.</summary>
        public string HarnessProblem => Walks.FirstOrDefault(w => w.HarnessProblem != null)?.HarnessProblem;
        /// <summary>The quest can't be completed: a walk failed on the quest's own account.</summary>
        public bool Fails => !Passed && HarnessProblem == null;
    }

    /// <summary>
    /// The world report: every quest row of a world database walked by the harness (by the player alone; again with
    /// the kills made by a gamebot in the group; again outlevelled before the hand-ins), and checked against the world
    /// (its giver and step targets stand in their region, its items are item templates). Read only; see WorldDatabase.
    /// </summary>
    public static class QuestWorldReport
    {
        public static List<QuestRowReport> Run(WorldDatabase world, ClassicQuests.Config classic, Func<DbDataQuest, bool> include = null)
        {
            var data = new QuestWorld();
            foreach (DbItemTemplate item in world.All<DbItemTemplate>())
                data.Add(item);
            data.NpcTemplates.AddRange(world.All<DbNpcTemplate>());
            List<DbDataQuest> rows = world.All<DbDataQuest>().OrderBy(r => r.ID).ToList();
            foreach (DbDataQuest row in rows)
                data.Add(row);

            var places = new HashSet<(string, ushort)>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string name, ushort region) in world.Places("Mob").Concat(world.Places("WorldObject")))
            {
                places.Add((name.ToLowerInvariant(), region));
                names.Add(name);
            }
            var questNames = new HashSet<string>(rows.Select(r => r.Name), StringComparer.OrdinalIgnoreCase);

            var reports = new List<QuestRowReport>();
            using var harness = new QuestHarness(data, classic);
            foreach (DbDataQuest row in rows.Where(r => include == null || include(r)))
            {
                var report = new QuestRowReport { Row = row };
                Audit(row, data, places, names, questNames, classic, report.WorldProblems);
                QuestWalk walk = QuestWalker.Walk(harness, Copy(row));
                report.Walks.Add(walk);
                if (walk.Passed && HasStep(row, DataQuest.eStepType.Kill, DataQuest.eStepType.KillFinish))
                    report.Walks.Add(Tagged(QuestWalker.Walk(harness, Copy(row), new WalkOptions { KillBy = KillBy.Gamebot }), "kills by a gamebot in the group"));
                if (walk.Passed && row.MaxLevel < GamePlayer.MAX_LEVEL && HasStep(row, DataQuest.eStepType.Deliver, DataQuest.eStepType.DeliverFinish,
                        DataQuest.eStepType.Collect, DataQuest.eStepType.CollectFinish))
                    report.Walks.Add(Tagged(QuestWalker.Walk(harness, Copy(row), new WalkOptions { OutlevelAfterAccept = true }), "outlevelled before the hand-ins"));
                reports.Add(report);
            }
            return reports;
        }

        // Each walk gets its own copy of the row, so one walk never sees another's.
        private static DbDataQuest Copy(DbDataQuest row) => QuestWorld.Copy(row);

        private static QuestWalk Tagged(QuestWalk walk, string how)
        {
            walk.Notes.Insert(0, how);
            if (!walk.Passed)
                walk.Stuck = how + ": " + walk.Stuck;
            return walk;
        }

        private static bool HasStep(DbDataQuest row, params DataQuest.eStepType[] types) =>
            QuestWalker.Split(row.StepType).Any(t => byte.TryParse(t, out byte b) && types.Contains((DataQuest.eStepType)b));

        /// <summary>What the world lacks for a row: its giver, a step's target, an item template, a dependency.</summary>
        public static void Audit(DbDataQuest row, QuestWorld data, HashSet<(string, ushort)> places, HashSet<string> names,
            HashSet<string> questNames, ClassicQuests.Config classic, List<string> problems)
        {
            string[] types = QuestWalker.Split(row.StepType);
            int steps = types.Length;
            var start = (DataQuest.eStartType)row.StartType;
            ClassicQuests.QuestInfo info = classic?.Quests != null && classic.Quests.TryGetValue(row.ID, out var i) ? i : null;

            bool Stands(string name, ushort region) =>
                region == 0 ? names.Contains(name) : places.Contains((name.ToLowerInvariant(), region));

            if (start != DataQuest.eStartType.SearchStart && !string.IsNullOrWhiteSpace(row.StartName) && !Stands(row.StartName, row.StartRegionID))
                problems.Add($"giver \"{row.StartName}\" does not stand in region {row.StartRegionID}");

            foreach ((string column, string value) in new[]
                     {
                         ("SourceText", row.SourceText), ("TargetName", row.TargetName), ("TargetText", row.TargetText),
                         ("StepText", row.StepText), ("StepItemTemplates", row.StepItemTemplates), ("AdvanceText", row.AdvanceText),
                         ("CollectItemTemplate", row.CollectItemTemplate), ("RewardXP", row.RewardXP), ("RewardMoney", row.RewardMoney),
                     })
            {
                int count = QuestWalker.Split(value).Length;
                if (count != 0 && count < steps && start is not (DataQuest.eStartType.Collection))
                    problems.Add($"{column} has {count} entries for {steps} steps");
            }

            string[] targets = QuestWalker.Split(row.TargetName);
            for (int s = 0; s < steps && s < targets.Length; s++)
            {
                if (!byte.TryParse(types[s], out byte type) || (DataQuest.eStepType)type is DataQuest.eStepType.Search or DataQuest.eStepType.SearchFinish)
                    continue;
                string[] parts = targets[s].Split(';');
                string name = parts[0].Trim();
                ushort region = parts.Length > 1 && ushort.TryParse(parts[1], out ushort r) ? r : (ushort)0;
                if (name.Length == 0)
                {
                    if (info?.Steps == null || s + 1 >= info.Steps.Count || info.Steps[s + 1]?.Custom == null)
                        problems.Add($"step {s + 1} has no target");
                    continue;
                }
                ClassicQuests.EventSpawn spawn = info?.Steps != null && s + 1 < info.Steps.Count ? info.Steps[s + 1]?.Spawn : null;
                bool spawned = spawn != null && string.Equals(spawn.Name, name, StringComparison.OrdinalIgnoreCase);
                if (!spawned && !Stands(name, region))
                    problems.Add($"step {s + 1} target \"{name}\" does not stand in region {region}");
            }

            void Item(string where, string id)
            {
                id = (id ?? string.Empty).Split(';')[0].Trim();
                if (id.Length > 0 && data.Item(id) == null)
                    problems.Add($"{where}: {id} is not an item template");
            }

            foreach (string id in QuestWalker.Split(row.StepItemTemplates)) Item("StepItemTemplates", id);
            foreach (string id in QuestWalker.Split(row.CollectItemTemplate)) Item("CollectItemTemplate", id);
            foreach (string id in QuestWalker.Split(row.FinalRewardItemTemplates)) Item("FinalRewardItemTemplates", id);
            if (!string.IsNullOrEmpty(row.OptionalRewardItemTemplates) && row.OptionalRewardItemTemplates.Length > 1)
                foreach (string id in row.OptionalRewardItemTemplates.Substring(1).Split('|')) Item("OptionalRewardItemTemplates", id);
            if (info?.Steps != null)
                foreach (ClassicQuests.StepInfo step in info.Steps.Where(s => s?.Custom != null))
                    Item("classic-quests.json " + step.Custom.Kind, step.Custom.Item);

            foreach (string entry in QuestWalker.Split(row.QuestDependency).Select(e => e.Trim()).Where(e => e.Length > 0))
            {
                if (QuestDependencies.TryParseIds(entry, out int[] ids, out _))
                {
                    foreach (int id in ids.Where(id => !data.Quests.ContainsKey(id)))
                        problems.Add($"dependency {entry}: no quest {id}");
                }
                else if (!QuestDependencies.IsValid(entry))
                    problems.Add($"dependency \"{entry}\" is not valid and is never met");
                else if (!questNames.Contains(entry))
                    problems.Add($"dependency \"{entry}\" names no data quest (a scripted quest of that name may meet it)");
            }

            bool delivers = HasStep(row, DataQuest.eStepType.Deliver, DataQuest.eStepType.DeliverFinish);
            if (delivers && string.IsNullOrEmpty(row.CollectItemTemplate))
                problems.Add("delivers with no CollectItemTemplate (QUESTING.md defect 7: could not be handed in before 1.2)");
        }

        /// <summary>The report as Markdown.</summary>
        public static string Markdown(List<QuestRowReport> reports, WorldDatabase world, string classicPath, DateTime when)
        {
            var text = new StringBuilder();
            int walked = reports.Count;
            int passed = reports.Count(r => r.Passed);
            text.AppendLine("# Quest harness world report");
            text.AppendLine();
            text.AppendLine($"- World: `{world.Original}` (SHA-256 `{world.OriginalSha256}`, read from a copy; the file was not changed)");
            text.AppendLine($"- classic-quests.json: {(classicPath == null ? "none" : "`" + classicPath + "`")}");
            text.AppendLine($"- Run: {when:yyyy-MM-dd HH:mm} UTC");
            int harness = reports.Count(r => !r.Passed && r.HarnessProblem != null);
            text.AppendLine($"- Quest rows walked: {walked}; finished by every walk: {passed}; can't be completed: {reports.Count(r => r.Fails)}" +
                            (harness == 0 ? string.Empty : $"; not walked to the end because of a harness problem: {harness}"));
            text.AppendLine($"- Rows with world problems (giver, targets, items, dependencies): {reports.Count(r => r.WorldProblems.Count > 0)}");
            text.AppendLine();
            text.AppendLine("These are automated walks on the server's quest code with a fake player; none of it was played in game.");
            text.AppendLine();

            text.AppendLine("## Why quests can't be completed");
            text.AppendLine();
            foreach (var group in reports.Where(r => r.Fails).GroupBy(r => Category(r.Walks.First(w => !w.Passed).Stuck)).OrderByDescending(g => g.Count()))
                text.AppendLine($"- {group.Key}: {group.Count()}");
            text.AppendLine();

            if (harness > 0)
            {
                text.AppendLine("## Harness problems (not quest failures)");
                text.AppendLine();
                text.AppendLine("The harness itself failed on these rows; they say nothing about the quest until the harness is fixed.");
                text.AppendLine();
                foreach (var group in reports.Where(r => !r.Passed && r.HarnessProblem != null).GroupBy(r => r.HarnessProblem))
                    text.AppendLine($"- {group.Key}: {string.Join(", ", group.Select(r => r.Row.ID))}");
                text.AppendLine();
            }

            text.AppendLine("## Repeatable rows (QUESTING.md defect 5)");
            text.AppendLine();
            foreach (var group in reports.GroupBy(r => r.Row.MaxCount).OrderBy(g => g.Key))
                text.AppendLine($"- MaxCount {group.Key}: {group.Count()} rows" + (group.Key == 0 ? " (\"no limit\" in the row format; still offered once)" : group.Key > 1 ? " (offered again from 1.2)" : ""));
            text.AppendLine();

            text.AppendLine("## Quests that can't be completed");
            text.AppendLine();
            text.AppendLine("| ID | Quest | Start | Levels | Classes | Stuck at | Why |");
            text.AppendLine("|---|---|---|---|---|---|---|");
            foreach (QuestRowReport report in reports.Where(r => r.Fails))
            {
                QuestWalk walk = report.Walks.First(w => !w.Passed);
                text.AppendLine($"| {report.Row.ID} | {Cell(report.Row.Name)} | {(DataQuest.eStartType)report.Row.StartType} | {report.Row.MinLevel}-{report.Row.MaxLevel} | " +
                                $"{Cell(report.Row.AllowedClasses)} | {walk.StepReached}/{walk.StepsTotal} | {Cell(walk.Stuck)} |");
            }
            text.AppendLine();

            text.AppendLine("## World problems");
            text.AppendLine();
            foreach (QuestRowReport report in reports.Where(r => r.WorldProblems.Count > 0))
                text.AppendLine($"- {report.Row.ID} {report.Row.Name}: {string.Join("; ", report.WorldProblems)}");
            text.AppendLine();

            text.AppendLine("## Every walk");
            text.AppendLine();
            foreach (QuestRowReport report in reports)
            {
                foreach (QuestWalk walk in report.Walks)
                {
                    text.AppendLine("```");
                    text.AppendLine(walk.ToString());
                    foreach (string note in walk.Notes)
                        text.AppendLine("  note: " + note);
                    text.AppendLine($"  xp {walk.Experience}, money {walk.Money}, items {string.Join(", ", walk.ItemsReceived)}");
                    text.AppendLine("```");
                }
            }
            return text.ToString();
        }

        private static string Cell(string text) => (text ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        /// <summary>A failure's kind, for the summary: its reason without the names and numbers that differ per quest.</summary>
        public static string Category(string stuck)
        {
            if (string.IsNullOrEmpty(stuck))
                return "did not finish";
            string text = stuck;
            int colon = text.IndexOf("; the server logged", StringComparison.Ordinal);
            if (colon > 0)
                text = text[..colon];
            text = System.Text.RegularExpressions.Regex.Replace(text, "\"[^\"]*\"", "\"…\"");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(step|level) \d+", "$1 N");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\b\d+\b", "N");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"\b(cq|item)_[A-Za-z0-9_]+", "<item>");
            text = System.Text.RegularExpressions.Regex.Replace(text, @"(interacting with|whispering \[[^\]]*\] to|killing|giving <item> to|kills of) [^;:]+? (did|never)", "$1 … $2");
            return text;
        }
    }
}
