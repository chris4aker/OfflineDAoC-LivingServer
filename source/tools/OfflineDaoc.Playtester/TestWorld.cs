// The test world database (made by source/tools/make_test_world.py). The playtester only reads it, read-only, and
// refuses any database without the make_test_world marker, so it can never start a server on a played world.
using System.Data.SQLite;

namespace OfflineDaoc.Playtester;

internal sealed record SavedCharacter(string Name, int Level, int Class, int Race, int Realm, int Region, int X, int Y, int Z, string? LastPlayed);
internal sealed record WorldBot(string Name, int Realm, int Level, string ClassName, int Region, int X, int Y, int Z, bool Online);
internal sealed record QuestCandidate(int Id, string Name, string Giver, int FirstStep, string Target, int Region, int GiverX, int GiverY, int GiverZ,
    string AcceptText = "");
internal sealed record QuestStep(int Index, int Type, string Target, string Item, string Text);
internal sealed record ZoneRect(int Id, int Region, int X, int Y, int Width, int Height);

internal sealed class TestWorld
{
    public const string MarkerTable = "offline_playtester_world";

    public string Path { get; }
    public IReadOnlyDictionary<string, string> Marker { get; }
    private readonly List<ZoneRect> _zones;

    private TestWorld(string path, IReadOnlyDictionary<string, string> marker, List<ZoneRect> zones)
    {
        Path = path;
        Marker = marker;
        _zones = zones;
    }

    /// <summary>Opens a test world, or explains why this database must not be used.</summary>
    public static TestWorld Open(string path)
    {
        string full = System.IO.Path.GetFullPath(path);
        if (!File.Exists(full))
            throw new PlaytestException($"{full} does not exist (make one with source/tools/make_test_world.py)");
        using var con = Connect(full);
        using (var has = new SQLiteCommand($"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='{MarkerTable}'", con))
            if (Convert.ToInt32(has.ExecuteScalar()) == 0)
                throw new PlaytestException($"{full} is not a test world (no {MarkerTable} table). The playtester never runs on a " +
                    "played world: make a test world with source/tools/make_test_world.py and pass that file.");
        var marker = new Dictionary<string, string>();
        using (var read = new SQLiteCommand($"SELECT Key, Value FROM {MarkerTable}", con))
        using (var r = read.ExecuteReader())
            while (r.Read())
                marker[r.GetString(0)] = r.GetString(1);
        if (marker.GetValueOrDefault("format") != "1")
            throw new PlaytestException($"{full}: test world format {marker.GetValueOrDefault("format") ?? "?"} is not the one this playtester knows (1)");
        var zones = new List<ZoneRect>();
        using (var read = new SQLiteCommand("SELECT ZoneID, RegionID, OffsetX, OffsetY, Width, Height FROM Zones", con))
        using (var r = read.ExecuteReader())
            while (r.Read())
                zones.Add(new ZoneRect(r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5)));
        return new TestWorld(full, marker, zones);
    }

    public bool IsMinimal => Marker.GetValueOrDefault("mode") == "minimal";

    /// <summary>The zone at a region position (8192 units per zone offset step), as WorldMgr places it.</summary>
    public int? ZoneOf(int region, int x, int y)
    {
        foreach (var z in _zones)
            if (z.Region == region && x >= z.X * 8192 && x < (z.X + z.Width) * 8192 && y >= z.Y * 8192 && y < (z.Y + z.Height) * 8192)
                return z.Id;
        return _zones.FirstOrDefault(z => z.Region == region)?.Id;
    }

    public SavedCharacter? Character(string name)
    {
        using var con = Connect(Path);
        if (!TableExists(con, "DOLCharacters"))
            return null;
        using var cmd = new SQLiteCommand("SELECT Name, Level, Class, Race, Realm, Region, Xpos, Ypos, Zpos, LastPlayed FROM DOLCharacters WHERE Name=@n", con);
        cmd.Parameters.AddWithValue("@n", name);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return null;
        return new SavedCharacter(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4), r.GetInt32(5),
            r.GetInt32(6), r.GetInt32(7), r.GetInt32(8), r.IsDBNull(9) ? null : Convert.ToString(r.GetValue(9)));
    }

    public IReadOnlyList<WorldBot> Bots(int realm)
    {
        var list = new List<WorldBot>();
        using var con = Connect(Path);
        if (!TableExists(con, "offline_world_bots"))
            return list;
        using var cmd = new SQLiteCommand("SELECT Name, Realm, Level, ClassName, RegionId, X, Y, Z, IsOnline FROM offline_world_bots " +
            "WHERE Realm=@r AND COALESCE(IsRetired,0)=0 ORDER BY BotId", con);
        cmd.Parameters.AddWithValue("@r", realm);
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new WorldBot(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3), r.GetInt32(4), r.GetInt32(5), r.GetInt32(6),
                r.GetInt32(7), r.GetInt32(8) != 0));
        return list;
    }

    /// <summary>
    /// Data quests (DataQuest) a level-1 character of a region can take whose first step is one of the step types
    /// (DataQuest.eStepType: 0/1 kill, 2/3 deliver), with where their giver stands (Mob).
    /// </summary>
    public IReadOnlyList<QuestCandidate> QuestCandidates(int region, int[] firstStepTypes, int maxMinLevel = 1)
    {
        var list = new List<QuestCandidate>();
        using var con = Connect(Path);
        if (!TableExists(con, "DataQuest") || !TableExists(con, "Mob"))
            return list;
        using var cmd = new SQLiteCommand("SELECT q.ID, q.Name, q.StartName, q.StepType, q.TargetName, m.X, m.Y, m.Z, q.AcceptText " +
            "FROM DataQuest q JOIN Mob m ON m.Name = q.StartName AND m.Region = q.StartRegionID " +
            "WHERE q.StartType = 0 AND q.StartRegionID = @r AND q.MinLevel <= @lvl AND COALESCE(q.QuestDependency,'') = '' ORDER BY q.ID", con);
        cmd.Parameters.AddWithValue("@r", region);
        cmd.Parameters.AddWithValue("@lvl", maxMinLevel);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string steps = r.IsDBNull(3) ? "" : r.GetString(3);
            if (!int.TryParse(steps.Split('|')[0], out int first) || !firstStepTypes.Contains(first))
                continue;
            string target = r.IsDBNull(4) ? "" : r.GetString(4).Split('|')[0].Split(';')[0];
            if (list.All(q => q.Id != r.GetInt32(0)))
                list.Add(new QuestCandidate(r.GetInt32(0), r.GetString(1), r.GetString(2), first, target, region, r.GetInt32(5), r.GetInt32(6), r.GetInt32(7),
                    r.IsDBNull(8) ? "" : r.GetString(8)));
        }
        return list;
    }

    /// <summary>A data quest's steps: step type, target name and step item of each ("|"-separated columns of DataQuest).</summary>
    public IReadOnlyList<QuestStep> QuestSteps(int questId)
    {
        using var con = Connect(Path);
        using var cmd = new SQLiteCommand("SELECT StepType, TargetName, StepItemTemplates, StepText FROM DataQuest WHERE ID=@id", con);
        cmd.Parameters.AddWithValue("@id", questId);
        using var r = cmd.ExecuteReader();
        if (!r.Read())
            return Array.Empty<QuestStep>();
        string[] Col(int i) => (r.IsDBNull(i) ? "" : r.GetString(i)).Split('|');
        string[] types = Col(0), targets = Col(1), items = Col(2), texts = Col(3);
        var steps = new List<QuestStep>();
        for (int i = 0; i < types.Length; i++)
            if (int.TryParse(types[i], out int type))
                steps.Add(new QuestStep(i, type, i < targets.Length ? targets[i].Split(';')[0] : "", i < items.Length ? items[i] : "",
                    i < texts.Length ? texts[i] : ""));
        return steps;
    }

    /// <summary>An item template's name (ItemTemplate), as the inventory shows it.</summary>
    public string? ItemName(string template)
    {
        using var con = Connect(Path);
        using var cmd = new SQLiteCommand("SELECT Name FROM ItemTemplate WHERE Id_nb=@t", con);
        cmd.Parameters.AddWithValue("@t", template);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>Where a named NPC stands (its Mob row), preferring the region given; null when the world has none.</summary>
    public (int Region, int X, int Y, int Z)? MobPosition(string name, int region)
    {
        using var con = Connect(Path);
        using var cmd = new SQLiteCommand("SELECT Region, X, Y, Z FROM Mob WHERE Name=@n ORDER BY Region=@r DESC LIMIT 1", con);
        cmd.Parameters.AddWithValue("@n", name);
        cmd.Parameters.AddWithValue("@r", region);
        using var r = cmd.ExecuteReader();
        return r.Read() ? (r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3)) : null;
    }

    public int AccountPrivLevel(string account)
    {
        using var con = Connect(Path);
        using var cmd = new SQLiteCommand("SELECT PrivLevel FROM Account WHERE Name=@n", con);
        cmd.Parameters.AddWithValue("@n", account);
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }

    private static bool TableExists(SQLiteConnection con, string table)
    {
        using var cmd = new SQLiteCommand("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=@t", con);
        cmd.Parameters.AddWithValue("@t", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static SQLiteConnection Connect(string path)
    {
        var con = new SQLiteConnection($"Data Source={path};Version=3;Read Only=True;Pooling=False;Default Timeout=10");
        con.Open();
        return con;
    }
}
