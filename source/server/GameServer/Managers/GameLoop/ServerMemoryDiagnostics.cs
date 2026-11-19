using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Memory diagnostics for the long-run stalls (2026-10-12/13: the heap grew from 1.8 to 5.2 GB in
    /// 11.5 hours and garbage-collection pauses reached 15 s, freezing every bot at once).
    /// <list type="bullet">
    /// <item>SERVER_GC_PAUSE: one line per pause of PauseLogMilliseconds or more (generation, reason, kind, heap).</item>
    /// <item>SERVER_MEMORY: every five minutes, heap by generation, fragmentation, allocation rate, collections,
    /// pauses, memory load, the largest allocating types (sampled) and the sizes of known bot caches.</item>
    /// <item>SERVER_HEAP_SNAPSHOT: a dotnet-gcdump heap snapshot into logs/ when the heap first passes
    /// SnapshotThresholdsMegabytes (needs the dotnet-gcdump tool; each snapshot pauses the server a few seconds).</item>
    /// </list>
    /// Allocation sampling and snapshots are skipped when the server folder holds a file named gc-diagnostics.off.
    /// Read the results with tools/claude-version/gc_report.py.
    /// </summary>
    public static class ServerMemoryDiagnostics
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const double PauseLogMilliseconds = 250;
        public const int ReportMinutes = 5;
        // Measured on the data still alive after the last full collection, not the heap size: a server-GC heap
        // carries gigabytes of dead gen2 objects between full collections (run 2026-10-13: a 2.5 GB heap held 615 MB
        // of live data, and the snapshot's own induced collection froze the server for 9.4 seconds for nothing).
        public static readonly int[] SnapshotThresholdsMegabytes = [1_500, 2_500];
        public const int SnapshotSpacingMinutes = 30;
        public const string DisableFileName = "gc-diagnostics.off";
        public const string SnapshotEnableFileName = "gc-snapshots.on";

        private static readonly Lock Sync = new();
        private static GcListener _listener;
        private static bool _detailed;
        private static int _started;

        // Since the last report (guarded by Sync).
        private static readonly Dictionary<string, long> SampledBytes = new();
        private static readonly int[] PauseCounts = new int[3];
        private static double _pauseTotalMs, _pauseMaxMs;
        private static int _longPauses;
        private static readonly Dictionary<string, int> Reasons = new();

        // Listener state (event thread).
        private static long _gcNumber, _gcDepth, _gcReason, _gcType;
        private static DateTime _suspendedAt;
        private static long[] _lastHeapStats = new long[5];

        private static long _lastAllocated;
        private static int _lastGen0, _lastGen1, _lastGen2;
        private static long _lastReportTicks;
        private static int _minutes;
        private static int _snapshotsTaken;
        private static long _lastSnapshotTicks;

        private static readonly List<(string Name, Func<long> Count)> Caches = new();

        /// <summary>A bounded cache whose size is listed in SERVER_MEMORY (so growth can be traced to it).</summary>
        public static void RegisterCache(string name, Func<long> count)
        {
            lock (Sync) Caches.Add((name, count));
        }

        // Every NPC object ever made, held weakly: one still alive (not collected) but no longer in the world is
        // retained by something. The owner's 2026-10-14 session grew its live heap from 1.4 to 4.3 GB in 7.5 hours.
        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameNPC, object> NpcCensus = new();

        public const string CensusEnableFileName = "memory-census.on";
        private static readonly bool CensusOn = File.Exists(Path.Combine(AppContext.BaseDirectory, CensusEnableFileName));

        public static void Track(GameNPC npc)
        {
            if (!CensusOn) return;
            try { NpcCensus.AddOrUpdate(npc, null); } catch { }
        }

        private static long CountNpcs(Func<GameNPC, bool> which)
        {
            long count = 0;
            foreach (var entry in NpcCensus)
                if (which(entry.Key)) count++;
            return count;
        }

        public static void EnsureStarted()
        {
            if (Interlocked.Exchange(ref _started, 1) == 1) return;
            _detailed = !File.Exists(Path.Combine(AppContext.BaseDirectory, DisableFileName));
            _lastAllocated = GC.GetTotalAllocatedBytes(false);
            _lastGen0 = GC.CollectionCount(0);
            _lastGen1 = GC.CollectionCount(1);
            _lastGen2 = GC.CollectionCount(2);
            _lastReportTicks = Stopwatch.GetTimestamp();
            RegisterCache("zoneRoutes", () => AutonomousZoneRoads.RememberedRoutes);
            RegisterCache("zoneSteps", () => AutonomousZoneItinerary.RememberedSteps);
            RegisterCache("navQueries", () => LocalPathfindingMgr.LiveQueries);
            RegisterCache("navQueriesMade", () => LocalPathfindingMgr.QueriesCreated);
            RegisterCache("npcObjects", () => CountNpcs(_ => true));
            RegisterCache("npcDetached", () => CountNpcs(npc => npc.ObjectState != GameObject.eObjectState.Active));
            RegisterCache("botDetached", () => CountNpcs(npc => npc is GameBot && npc.ObjectState != GameObject.eObjectState.Active));
            RegisterCache("petDetached", () => CountNpcs(npc => npc is GameSummonedPet && npc.ObjectState != GameObject.eObjectState.Active));
            try { _listener = new GcListener(); }
            catch (Exception exception) { Log.Warn("GC event listener could not start; SERVER_GC_PAUSE lines are off", exception); }
            Log.Info($"SERVER_MEMORY_DIAGNOSTICS started allocationSampling={_detailed} heapSnapshots={_detailed && GcdumpPath() != null && File.Exists(Path.Combine(AppContext.BaseDirectory, SnapshotEnableFileName))} " +
                $"serverGc={System.Runtime.GCSettings.IsServerGC} latency={System.Runtime.GCSettings.LatencyMode} " +
                // The launcher's "Lean server memory" option sets these two (Living Server 1.2); "default" when unset.
                $"gcHeapCount={Environment.GetEnvironmentVariable("DOTNET_GCHeapCount") ?? "default"} " +
                $"gcConserveMemory={Environment.GetEnvironmentVariable("DOTNET_GCConserveMemory") ?? "default"}");
        }

        /// <summary>Called once a minute from the game loop's work report.</summary>
        public static void Minute()
        {
            if (Volatile.Read(ref _started) == 0) return;
            GCMemoryInfo info = GC.GetGCMemoryInfo();
            TrySnapshot(info);
            if (++_minutes % ReportMinutes != 0) return;
            Report(info);
        }

        private static void Report(GCMemoryInfo info)
        {
            long now = Stopwatch.GetTimestamp();
            double minutes = Math.Max(1e-6, (now - _lastReportTicks) / (double)Stopwatch.Frequency / 60);
            _lastReportTicks = now;
            long allocated = GC.GetTotalAllocatedBytes(false);
            double allocMbPerMin = (allocated - _lastAllocated) / 1048576.0 / minutes;
            _lastAllocated = allocated;
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long[] generationMb = new long[5];
            ReadOnlySpan<GCGenerationInfo> generations = info.GenerationInfo;
            for (int i = 0; i < generationMb.Length; i++)
                generationMb[i] = i < generations.Length ? generations[i].SizeAfterBytes / 1048576 : -1;
            string Gen(int i) => generationMb[i].ToString();
            string top, pauses, reasons, caches;
            lock (Sync)
            {
                double sampled = Math.Max(1, SampledBytes.Values.Sum());
                top = SampledBytes.Count == 0 ? "off" : string.Join("; ", SampledBytes.OrderByDescending(p => p.Value).Take(12)
                    .Select(p => FormattableString.Invariant($"{p.Key} {p.Value / 1048576.0 / minutes:F0}MB/min {100 * p.Value / sampled:F0}%")));
                pauses = FormattableString.Invariant($"pauseTotalMs={_pauseTotalMs:F0} pauseMaxMs={_pauseMaxMs:F0} longPauses={_longPauses} pausesByGen={PauseCounts[0]}/{PauseCounts[1]}/{PauseCounts[2]}");
                reasons = Reasons.Count == 0 ? "none" : string.Join(",", Reasons.OrderByDescending(p => p.Value).Select(p => $"{p.Key}:{p.Value}"));
                caches = string.Join(" ", Caches.Select(c => { try { return $"{c.Name}={c.Count()}"; } catch { return $"{c.Name}=?"; } }));
                SampledBytes.Clear();
                Reasons.Clear();
                Array.Clear(PauseCounts);
                _pauseTotalMs = _pauseMaxMs = 0;
                _longPauses = 0;
            }
            using Process process = Process.GetCurrentProcess();
            Log.Info(FormattableString.Invariant(
                $"SERVER_MEMORY heapMB={info.HeapSizeBytes / 1048576} liveMB={LiveAfterFullCollectionMegabytes()} gen0MB={Gen(0)} gen1MB={Gen(1)} gen2MB={Gen(2)} lohMB={Gen(3)} pohMB={Gen(4)} ") +
                FormattableString.Invariant($"fragmentedMB={info.FragmentedBytes / 1048576} committedMB={info.TotalCommittedBytes / 1048576} workingSetMB={process.WorkingSet64 / 1048576} ") +
                // Native side (2026-10-20): private bytes beyond the GC's committed memory, and what could hold it.
                FormattableString.Invariant($"privateMB={process.PrivateMemorySize64 / 1048576} nativeMB={(process.PrivateMemorySize64 - info.TotalCommittedBytes) / 1048576} ") +
                FormattableString.Invariant($"threads={ThreadCount(process)} poolThreads={ThreadPool.ThreadCount} handles={process.HandleCount} ") +
                FormattableString.Invariant($"memoryLoadMB={info.MemoryLoadBytes / 1048576} availableMB={info.TotalAvailableMemoryBytes / 1048576} pauseTimePct={info.PauseTimePercentage:F1} ") +
                FormattableString.Invariant($"allocMBperMin={allocMbPerMin:F0} gcs={gen0 - _lastGen0}/{gen1 - _lastGen1}/{gen2 - _lastGen2} {pauses} reasons=\"{reasons}\" ") +
                FormattableString.Invariant($"pinned={info.PinnedObjectsCount} finalizable={info.FinalizationPendingCount} caches=\"{caches}\" ") +
                $"roadSearches={AutonomousZoneRoads.TakeStats()} " +
                FormattableString.Invariant($"topAllocations=\"{top}\""));
            _lastGen0 = gen0; _lastGen1 = gen1; _lastGen2 = gen2;
            // Leak-hunting censuses (2026-10-15) only with runtime\server\memory-census.on present: test runs, never a release.
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, CensusEnableFileName))) return;
            try { LogNpcCensus(); } catch (Exception exception) { Log.Warn("SERVER_NPC_CENSUS failed", exception); }
            try { LogStaticCensus(); } catch (Exception exception) { Log.Warn("SERVER_STATIC_CENSUS failed", exception); }
            try { LogBotFieldCensus(); } catch (Exception exception) { Log.Warn("SERVER_BOT_FIELD_CENSUS failed", exception); }
            try { ServerHolderCensus.MaybeRun(DetachedNpcs, ActiveNpcs); } catch (Exception exception) { Log.Warn("SERVER_HOLDER_CENSUS failed", exception); }
            try { ServerHolderCensus.LogOrphans(); } catch (Exception exception) { Log.Warn("SERVER_ORPHAN_TIMERS failed", exception); }
            try { ServerHandleCensus.MaybeRun(); } catch (Exception exception) { Log.Warn("SERVER_HANDLE_CENSUS failed", exception); }
            try { ServerLockEventCensus.MaybeRun(() => ServerLockEventCensus.DefaultRoots(CensusNpcs())); }
            catch (Exception exception) { Log.Warn("SERVER_LOCK_EVENTS failed", exception); }
        }

        /// <summary>NPC objects still in memory but no longer in the world (census on only).</summary>
        private static List<GameNPC> DetachedNpcs()
        {
            var detached = new List<GameNPC>();
            foreach (var entry in NpcCensus)
                if (entry.Key.ObjectState != GameObject.eObjectState.Active) detached.Add(entry.Key);
            return detached;
        }

        /// <summary>Every NPC object still in memory, in or out of the world (census on only).</summary>
        private static List<GameNPC> CensusNpcs()
        {
            var all = new List<GameNPC>();
            foreach (var entry in NpcCensus) all.Add(entry.Key);
            return all;
        }

        /// <summary>NPC objects in the world now (census on only): the holder census walks their fields too.</summary>
        private static List<GameNPC> ActiveNpcs()
        {
            var active = new List<GameNPC>();
            foreach (var entry in NpcCensus)
                if (entry.Key.ObjectState == GameObject.eObjectState.Active) active.Add(entry.Key);
            return active;
        }

        private static int ThreadCount(Process process)
        {
            try { return process.Threads.Count; }
            catch { return -1; }
        }

        private static readonly Dictionary<Type, FieldInfo[]> InstanceCollectionFields = new();
        private static readonly Dictionary<string, double> BotFieldAverages = new();
        private const int BotFieldSample = 500;

        private static FieldInfo[] CollectionFieldsOf(Type type)
        {
            if (InstanceCollectionFields.TryGetValue(type, out FieldInfo[] fields)) return fields;
            var list = new List<FieldInfo>();
            for (Type t = type; t != null && t != typeof(object); t = t.BaseType)
                list.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(field => !field.FieldType.IsPrimitive && field.FieldType != typeof(string) &&
                                    typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType)));
            return InstanceCollectionFields[type] = list.ToArray();
        }

        /// <summary>
        /// SERVER_BOT_FIELD_CENSUS: for up to 500 bots, the average element count of every collection field on the bot,
        /// its brain and its autonomous controller, with the fields that grew most per bot since the last report. The
        /// static census found nothing growing fast enough for the live heap's ~500 MB an hour (run 10, 2026-10-15).
        /// </summary>
        private static void LogBotFieldCensus()
        {
            GameBot[] bots = AutonomousBotRegistry.Snapshot();
            if (bots.Length == 0) return;
            int step = Math.Max(1, bots.Length / BotFieldSample);
            var sums = new Dictionary<string, long>();
            int sampled = 0;
            FieldInfo controllerField = typeof(DOL.AI.Brain.BotBrain).GetField("_autonomousWorldController", BindingFlags.Instance | BindingFlags.NonPublic);
            for (int i = 0; i < bots.Length; i += step)
            {
                GameBot bot = bots[i];
                sampled++;
                object brain = bot.Brain;
                object controller = brain is DOL.AI.Brain.BotBrain ? controllerField?.GetValue(brain) : null;
                foreach (object owner in new[] { bot, brain, controller })
                {
                    if (owner == null) continue;
                    foreach (FieldInfo field in CollectionFieldsOf(owner.GetType()))
                    {
                        object value;
                        try { value = field.GetValue(owner); } catch { continue; }
                        if (value == null) continue;
                        Type type = value.GetType();
                        if (!CountProperties.TryGetValue(type, out PropertyInfo count))
                            CountProperties[type] = count = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public) is { } p &&
                                                            (p.PropertyType == typeof(int) || p.PropertyType == typeof(long)) ? p : null;
                        long size;
                        if (count != null) { try { size = Convert.ToInt64(count.GetValue(value)); } catch { continue; } }
                        else if (value is Array array) size = array.LongLength;
                        else continue;
                        string name = $"{field.DeclaringType?.Name}.{field.Name}";
                        sums[name] = sums.GetValueOrDefault(name) + size;
                    }
                }
            }
            var rows = sums.Select(pair =>
            {
                double average = pair.Value / (double)sampled;
                double before = BotFieldAverages.GetValueOrDefault(pair.Key, average);
                BotFieldAverages[pair.Key] = average;
                return (Name: pair.Key, Average: average, Delta: average - before);
            }).ToArray();
            Log.Info(FormattableString.Invariant($"SERVER_BOT_FIELD_CENSUS bots={bots.Length} sampled={sampled} ") +
                     $"grew=\"{string.Join(",", rows.Where(r => r.Delta > 0.01).OrderByDescending(r => r.Delta).Take(12).Select(r => FormattableString.Invariant($"{r.Name}={r.Average:F1}(+{r.Delta:F2})")))}\" " +
                     $"largest=\"{string.Join(",", rows.OrderByDescending(r => r.Average).Take(12).Select(r => FormattableString.Invariant($"{r.Name}={r.Average:F1}")))}\"");
        }

        private static (FieldInfo Field, string Name)[] _staticCollections;
        private static readonly Dictionary<string, long> StaticCounts = new();
        private static readonly Dictionary<Type, PropertyInfo> CountProperties = new();

        /// <summary>
        /// SERVER_STATIC_CENSUS: the element count of every static collection in the game server, the ones that grew
        /// most since the last report and the largest. NPC objects were ruled out (run 9, 2026-10-14: detached pets and
        /// NPCs rise and fall while the live heap still grows about 500 MB an hour).
        /// </summary>
        private static void LogStaticCensus()
        {
            _staticCollections ??= typeof(GameServer).Assembly.GetTypes()
                .Where(type => !type.ContainsGenericParameters)
                .SelectMany(type => type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .Where(field => !field.IsLiteral && !field.FieldType.IsPrimitive && field.FieldType != typeof(string) &&
                                    (typeof(System.Collections.IEnumerable).IsAssignableFrom(field.FieldType) || field.FieldType == typeof(object)))
                    .Select(field => (field, $"{type.Name}.{field.Name}")))
                .ToArray();
            var now = new List<(string Name, long Count, long Delta)>();
            foreach (var (field, name) in _staticCollections)
            {
                object value;
                try { value = field.GetValue(null); } catch { continue; }
                if (value == null) continue;
                Type type = value.GetType();
                if (!CountProperties.TryGetValue(type, out PropertyInfo count))
                    CountProperties[type] = count = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public) is { } p &&
                                                    (p.PropertyType == typeof(int) || p.PropertyType == typeof(long)) ? p : null;
                long size;
                if (count != null)
                {
                    try { size = Convert.ToInt64(count.GetValue(value)); } catch { continue; }
                }
                else if (value is Array array) size = array.LongLength;
                else continue;
                long before = StaticCounts.GetValueOrDefault(name, size);
                StaticCounts[name] = size;
                now.Add((name, size, size - before));
            }
            Log.Info($"SERVER_STATIC_CENSUS fields={now.Count} " +
                     $"grew=\"{string.Join(",", now.Where(n => n.Delta > 0).OrderByDescending(n => n.Delta).Take(12).Select(n => $"{n.Name}={n.Count}(+{n.Delta})"))}\" " +
                     $"largest=\"{string.Join(",", now.OrderByDescending(n => n.Count).Take(10).Select(n => $"{n.Name}={n.Count}"))}\"");
        }

        /// <summary>
        /// SERVER_NPC_CENSUS: NPC objects still in memory but out of the world, by type and state, and the event handlers
        /// that hold such objects (run 8, 2026-10-14: detached summoned pets rose from 2,900 to 5,200 in ten minutes).
        /// </summary>
        private static void LogNpcCensus()
        {
            var byType = new Dictionary<string, int>();
            foreach (var entry in NpcCensus)
            {
                GameNPC npc = entry.Key;
                if (npc.ObjectState == GameObject.eObjectState.Active) continue;
                string key = $"{npc.GetType().Name}/{npc.ObjectState}";
                byType[key] = byType.GetValueOrDefault(key) + 1;
            }
            static bool Detached(object o) => o is GameNPC npc && npc.ObjectState != GameObject.eObjectState.Active;
            var events = DOL.Events.GameEventMgr.Census(Detached);
            Log.Info($"SERVER_NPC_CENSUS detached=\"{string.Join(",", byType.OrderByDescending(p => p.Value).Take(10).Select(p => $"{p.Key}:{p.Value}"))}\" " +
                     $"eventObjects={events.Objects} eventDetachedObjects={events.DetachedObjects} eventDetachedTargets={events.DetachedTargets} " +
                     $"eventHolders=\"{events.TargetTypes}\"");
        }

        private static string GcdumpPath()
        {
            string home = Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetEnvironmentVariable("HOME");
            string path = home == null ? null : Path.Combine(home, ".dotnet", "tools", OperatingSystem.IsWindows() ? "dotnet-gcdump.exe" : "dotnet-gcdump");
            return path != null && File.Exists(path) ? path : null;
        }

        /// <summary>Bytes promoted (still alive) by the most recent gen2 collection, background or blocking.</summary>
        private static long LiveAfterFullCollectionMegabytes()
        {
            GCMemoryInfo background = GC.GetGCMemoryInfo(GCKind.Background);
            GCMemoryInfo blocking = GC.GetGCMemoryInfo(GCKind.FullBlocking);
            GCMemoryInfo last = background.Index >= blocking.Index ? background : blocking;
            return last.Index == 0 ? 0 : last.PromotedBytes / 1048576;
        }

        private static void TrySnapshot(GCMemoryInfo info)
        {
            // Opt-in only (runs 1-4, 2026-10-13/14: every automatic snapshot froze the server about nine seconds, and
            // dotnet-gcdump samples at most ten million objects, so the reports showed almost none of the heap).
            if (!_detailed || _snapshotsTaken >= SnapshotThresholdsMegabytes.Length ||
                !File.Exists(Path.Combine(AppContext.BaseDirectory, SnapshotEnableFileName))) return;
            long heapMb = info.HeapSizeBytes / 1048576;
            if (LiveAfterFullCollectionMegabytes() < SnapshotThresholdsMegabytes[_snapshotsTaken]) return;
            long now = Stopwatch.GetTimestamp();
            if (_lastSnapshotTicks != 0 && now - _lastSnapshotTicks < SnapshotSpacingMinutes * 60 * Stopwatch.Frequency) return;
            string tool = GcdumpPath();
            if (tool == null) return;
            _snapshotsTaken++;
            _lastSnapshotTicks = now;
            string file = Path.Combine(AppContext.BaseDirectory, "logs", $"heap-{DateTime.Now:yyyyMMdd-HHmmss}-{heapMb}MB.gcdump");
            Log.Warn($"SERVER_HEAP_SNAPSHOT heapMB={heapMb} liveMB={LiveAfterFullCollectionMegabytes()} file=\"{file}\" (the snapshot's own full collection pauses the server for a few seconds)");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    using Process dump = Process.Start(new ProcessStartInfo(tool)
                    {
                        ArgumentList = { "collect", "-p", Environment.ProcessId.ToString(), "-o", file },
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (dump != null && dump.WaitForExit(600_000))
                        Log.Info($"SERVER_HEAP_SNAPSHOT done exit={dump.ExitCode} file=\"{file}\" exists={File.Exists(file)}");
                }
                catch (Exception exception) { Log.Warn("SERVER_HEAP_SNAPSHOT failed", exception); }
            });
        }

        private static string ReasonName(long reason) => reason switch
        {
            0 => "AllocSmall", 1 => "Induced", 2 => "LowMemory", 3 => "Empty", 4 => "AllocLarge", 5 => "OutOfSpaceSOH",
            6 => "OutOfSpaceLOH", 7 => "InducedNotForced", 8 => "Internal", 9 => "InducedLowMemory", 10 => "InducedCompacting",
            11 => "LowMemoryHost", 12 => "PMFullGC", 13 => "LowMemoryHostBlocking", _ => reason.ToString()
        };

        private static string TypeName(long type) => type switch { 0 => "Blocking", 1 => "Background", 2 => "ForegroundDuringBackground", _ => type.ToString() };

        private static long Number(EventWrittenEventArgs e, string name)
        {
            int index = e.PayloadNames?.IndexOf(name) ?? -1;
            return index < 0 || e.Payload == null || index >= e.Payload.Count ? 0 : Convert.ToInt64(e.Payload[index]);
        }

        private sealed class GcListener : EventListener
        {
            protected override void OnEventSourceCreated(EventSource source)
            {
                if (source.Name == "Microsoft-Windows-DotNETRuntime")
                    // GC keyword; Verbose adds the sampled AllocationTick (one event per ~100 KB allocated).
                    EnableEvents(source, File.Exists(Path.Combine(AppContext.BaseDirectory, DisableFileName))
                        ? EventLevel.Informational : EventLevel.Verbose, (EventKeywords)0x1);
            }

            protected override void OnEventWritten(EventWrittenEventArgs e)
            {
                try
                {
                    switch (e.EventId)
                    {
                        case 1: // GCStart
                            _gcNumber = Number(e, "Count"); _gcDepth = Number(e, "Depth");
                            _gcReason = Number(e, "Reason"); _gcType = Number(e, "Type");
                            lock (Sync)
                            {
                                string reason = $"g{_gcDepth}{ReasonName(_gcReason)}";
                                Reasons[reason] = Reasons.GetValueOrDefault(reason) + 1;
                            }
                            break;
                        case 4: // GCHeapStats
                            _lastHeapStats = [Number(e, "GenerationSize0"), Number(e, "GenerationSize1"), Number(e, "GenerationSize2"),
                                Number(e, "GenerationSize3"), Number(e, "GenerationSize4")];
                            break;
                        case 9: // GCSuspendEEBegin
                            _suspendedAt = e.TimeStamp;
                            break;
                        case 3: // GCRestartEEEnd
                            if (_suspendedAt == default) break;
                            double ms = (e.TimeStamp - _suspendedAt).TotalMilliseconds;
                            _suspendedAt = default;
                            lock (Sync)
                            {
                                PauseCounts[Math.Clamp((int)_gcDepth, 0, 2)]++;
                                _pauseTotalMs += ms;
                                _pauseMaxMs = Math.Max(_pauseMaxMs, ms);
                                if (ms >= PauseLogMilliseconds) _longPauses++;
                            }
                            if (ms >= PauseLogMilliseconds)
                                Log.Warn(FormattableString.Invariant(
                                    $"SERVER_GC_PAUSE ms={ms:F0} gc={_gcNumber} gen={_gcDepth} reason={ReasonName(_gcReason)} kind={TypeName(_gcType)} ") +
                                    FormattableString.Invariant($"lastHeapMB={_lastHeapStats[0] / 1048576}/{_lastHeapStats[1] / 1048576}/{_lastHeapStats[2] / 1048576}/{_lastHeapStats[3] / 1048576}/{_lastHeapStats[4] / 1048576} ") +
                                    $"totalMemoryMB={GC.GetTotalMemory(false) / 1048576}");
                            break;
                        case 10: // GCAllocationTick
                        {
                            int index = e.PayloadNames?.IndexOf("TypeName") ?? -1;
                            string type = index >= 0 && e.Payload != null && index < e.Payload.Count ? e.Payload[index] as string : null;
                            long amount = Number(e, "AllocationAmount64");
                            if (amount == 0) amount = Number(e, "AllocationAmount");
                            type = string.IsNullOrEmpty(type) ? "?" : type;
                            lock (Sync)
                                if (SampledBytes.Count < 4_000 || SampledBytes.ContainsKey(type))
                                    SampledBytes[type] = SampledBytes.GetValueOrDefault(type) + amount;
                            break;
                        }
                    }
                }
                catch
                {
                    // Diagnostics must never disturb the server.
                }
            }
        }
    }
}
