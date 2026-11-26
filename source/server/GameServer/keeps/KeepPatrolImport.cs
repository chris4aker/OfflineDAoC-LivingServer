using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.Database;

namespace DOL.GS.Keeps
{
    /// <summary>
    /// #577 (owner decision 2026-11-26: "(a) yes"): the frontier keeps and towers get the New Frontiers keep patrols as
    /// Eve-of-Darkness/db-public has them (commit e6770459, MIT; Mythic game data): 16 KeepPosition rows (one patrol per keep
    /// layout and one per tower, for the old and the new keep models), 16 Path rows and 140 PathPoints, in
    /// <c>keeps/data/keep_patrols_dbpublic.json</c>. A patrol is 2 guards at a keep, 3 above level 4, and 1 at a tower
    /// (<see cref="Patrol"/>), walking a ground loop near the structure. Period source: the 1.70a test notes (19 April 2004),
    /// "Patrolling guards will now stay much closer to the structure in which they are guarding."
    /// <para>A one-time import into saved world data, run before the keep manager loads (so the first FillPositions sees the
    /// rows), recorded in <c>living_server_world_repairs</c>. It only adds rows: a position, path or path's points already in
    /// the world are left as they are. A world that already has a patrol position under a key the data doesn't have (db-public's
    /// earlier eight, <c>Benowyc</c> and so on) gets nothing, so no keep has two patrols; the import says so each start until
    /// those rows are gone. Each import writes one <c>KEEP_PATROL_IMPORTED</c> row with the counts. Needs #697's fix (one
    /// patrol key), or the next keep level change would throw.</para>
    /// </summary>
    public static class KeepPatrolImport
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const string RepairName = "keep-patrols-1.2.3";
        public const string LogTag = "KEEP_PATROL_IMPORTED";
        public const string ResourceSuffix = "keep_patrols_dbpublic.json";
        public const string PatrolClassType = "DOL.GS.Keeps.Patrol";

        public sealed record Position(string Id, string TemplateId, int KeepType, int ComponentSkin, int ComponentRotation, int Height,
            int XOff, int YOff, int ZOff, int HOff, int TemplateType);

        public sealed record PathRow(string Id, string PathId, int PathType, ushort RegionId);

        public sealed record Point(string Id, string PathId, int Step, int X, int Y, int Z, int MaxSpeed, int WaitTime);

        public sealed record Data(IReadOnlyList<Position> Positions, IReadOnlyList<PathRow> Paths, IReadOnlyList<Point> Points);

        /// <summary>What the import adds to a world, or why it adds nothing (<see cref="Skip"/> not null).</summary>
        public sealed record Plan(IReadOnlyList<Position> Positions, IReadOnlyList<PathRow> Paths, IReadOnlyList<Point> Points, string Skip)
        {
            public bool Empty => Positions.Count == 0 && Paths.Count == 0 && Points.Count == 0;
        }

        /// <summary>Reads the data file (its "positions", "paths" and "points"). Pure; throws on text that is not it.</summary>
        public static Data Parse(string json)
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            var positions = root.GetProperty("positions").EnumerateArray().Select(p => new Position(
                p.GetProperty("KeepPosition_ID").GetString(), p.GetProperty("TemplateID").GetString(), p.GetProperty("KeepType").GetInt32(),
                p.GetProperty("ComponentSkin").GetInt32(), p.GetProperty("ComponentRotation").GetInt32(), p.GetProperty("Height").GetInt32(),
                p.GetProperty("XOff").GetInt32(), p.GetProperty("YOff").GetInt32(), p.GetProperty("ZOff").GetInt32(),
                p.GetProperty("HOff").GetInt32(), p.GetProperty("TemplateType").GetInt32())).ToList();
            var paths = root.GetProperty("paths").EnumerateArray().Select(p => new PathRow(
                p.GetProperty("Path_ID").GetString(), p.GetProperty("PathID").GetString(), p.GetProperty("PathType").GetInt32(),
                (ushort)p.GetProperty("RegionID").GetInt32())).ToList();
            var points = root.GetProperty("points").EnumerateArray().Select(p => new Point(
                p.GetProperty("PathPoints_ID").GetString(), p.GetProperty("PathID").GetString(), p.GetProperty("Step").GetInt32(),
                p.GetProperty("X").GetInt32(), p.GetProperty("Y").GetInt32(), p.GetProperty("Z").GetInt32(),
                p.GetProperty("MaxSpeed").GetInt32(), p.GetProperty("WaitTime").GetInt32())).ToList();
            return new Data(positions, paths, points);
        }

        /// <summary>
        /// The rows to add, given the world's patrol positions (TemplateID, ComponentSkin), its path ids and the point count of
        /// each path. Pure. Nothing when the world has a patrol position under a key the data doesn't have.
        /// </summary>
        public static Plan For(Data data, IEnumerable<(string TemplateId, int Skin)> worldPatrols, IEnumerable<string> worldPaths,
            Func<string, int> worldPointCount)
        {
            var patrols = worldPatrols.ToList();
            var known = new HashSet<string>(data.Positions.Select(p => p.TemplateId), StringComparer.OrdinalIgnoreCase);
            var other = patrols.Select(p => p.TemplateId).Where(id => !known.Contains(id ?? string.Empty))
                .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(id => id, StringComparer.Ordinal).ToList();
            if (other.Count > 0)
                return new Plan([], [], [], "other patrol positions in the world: " + string.Join(",", other));
            var havePositions = new HashSet<(string, int)>(patrols.Select(p => ((p.TemplateId ?? string.Empty).ToLowerInvariant(), p.Skin)));
            var havePaths = new HashSet<string>(worldPaths, StringComparer.OrdinalIgnoreCase);
            var positions = data.Positions.Where(p => !havePositions.Contains((p.TemplateId.ToLowerInvariant(), p.ComponentSkin))).ToList();
            var paths = data.Paths.Where(p => !havePaths.Contains(p.PathId)).ToList();
            var points = data.Points.Where(p => worldPointCount(p.PathId) == 0).ToList();
            return new Plan(positions, paths, points, null);
        }

        public static string ImportedLine(Plan plan) =>
            $"{LogTag} positions={plan.Positions.Count} paths={plan.Paths.Count} points={plan.Points.Count} " +
            "source=db-public-e6770459 reason=owner-577a";

        /// <summary>Whether this world's repair list already has the import. Pure.</summary>
        public static bool AlreadyRan(string repairs) =>
            (repairs ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(RepairName, StringComparer.OrdinalIgnoreCase);

        /// <summary>The repair list with the import added. Pure.</summary>
        public static string WithRepair(string repairs) =>
            AlreadyRan(repairs) ? repairs : string.IsNullOrWhiteSpace(repairs) ? RepairName : repairs.Trim() + "," + RepairName;

        /// <summary>The data file, embedded in GameServer.dll.</summary>
        public static Data Load()
        {
            Assembly assembly = typeof(KeepPatrolImport).Assembly;
            string name = assembly.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith(ResourceSuffix, StringComparison.Ordinal))
                          ?? throw new InvalidOperationException($"{ResourceSuffix} is not embedded");
            using var reader = new StreamReader(assembly.GetManifestResourceStream(name));
            return Parse(reader.ReadToEnd());
        }

        /// <summary>From the server start, before the keep manager loads. Never stops the start.</summary>
        public static bool Run()
        {
            try
            {
                if (AlreadyRan(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS))
                    return true;
                Data data = Load();
                var worldPatrols = DOLDB<DbKeepPosition>.SelectObjects(DB.Column("ClassType").IsEqualTo(PatrolClassType))
                    .Select(p => (p.TemplateID, p.ComponentSkin)).ToList();
                var pathIds = data.Paths.Select(p => p.PathId).ToList();
                var worldPaths = pathIds.Where(id => DOLDB<DbPath>.SelectObject(DB.Column("PathID").IsEqualTo(id)) != null).ToList();
                var pointCounts = pathIds.ToDictionary(id => id,
                    id => DOLDB<DbPathPoint>.SelectObjects(DB.Column("PathID").IsEqualTo(id)).Count, StringComparer.OrdinalIgnoreCase);
                Plan plan = For(data, worldPatrols, worldPaths, id => pointCounts.TryGetValue(id, out int n) ? n : 0);
                if (plan.Skip != null)
                {
                    log.Warn($"{LogTag} nothing added: {plan.Skip}; tried again next start");
                    return true;
                }
                foreach (Position p in plan.Positions)
                    GameServer.Database.AddObject(new DbKeepPosition
                    {
                        ObjectId = p.Id, TemplateID = p.TemplateId, ClassType = PatrolClassType, KeepType = p.KeepType,
                        ComponentSkin = p.ComponentSkin, ComponentRotation = p.ComponentRotation, Height = p.Height,
                        XOff = p.XOff, YOff = p.YOff, ZOff = p.ZOff, HOff = p.HOff, TemplateType = p.TemplateType,
                    });
                foreach (PathRow p in plan.Paths)
                    GameServer.Database.AddObject(new DbPath { ObjectId = p.Id, PathID = p.PathId, PathType = p.PathType, RegionID = p.RegionId });
                foreach (Point p in plan.Points)
                    GameServer.Database.AddObject(new DbPathPoint
                    {
                        ObjectId = p.Id, PathID = p.PathId, Step = p.Step, X = p.X, Y = p.Y, Z = p.Z, MaxSpeed = p.MaxSpeed,
                        WaitTime = p.WaitTime,
                    });
                log.Info(ImportedLine(plan));
                MarkDone();
            }
            catch (Exception exception)
            {
                log.Error($"{LogTag} the keep patrols could not be added; tried again next start", exception);
            }
            return true;
        }

        private static void MarkDone()
        {
            string done = WithRepair(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS);
            DbServerProperty row = DOLDB<DbServerProperty>.SelectObject(DB.Column("Key").IsEqualTo(ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS_KEY));
            if (row != null)
            {
                row.Value = done;
                GameServer.Database.SaveObject(row);
            }
            ServerProperties.Properties.LIVING_SERVER_WORLD_REPAIRS = done;
        }
    }
}
