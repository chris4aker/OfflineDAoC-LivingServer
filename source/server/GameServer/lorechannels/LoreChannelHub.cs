using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS.LoreChannels
{
    /// <summary>
    /// Living Server 1.2: where the lore channels beyond bot chat (loremasters, lore books, the /lore codex, first kills)
    /// read the lore bible. By default the embedded files under bots/autonomous/data/lore; the lore engine's loader can
    /// take over with <see cref="UseSource"/> (docs/LORE.md, "Channels").
    /// </summary>
    public static class LoreChannelHub
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);
        private static ILoreSource _source;
        private static readonly object Sync = new();

        /// <summary>Raised after <see cref="UseSource"/> changed the source (the loremasters and books rebuild).</summary>
        public static event Action SourceChanged;

        public static ILoreSource Source
        {
            get
            {
                lock (Sync)
                    return _source ??= EmbeddedLoreSource.Load(Log);
            }
        }

        /// <summary>Use another lore source (the lore engine's, or a test's). Null goes back to the embedded files.</summary>
        public static void UseSource(ILoreSource source)
        {
            lock (Sync)
                _source = source;
            SourceChanged?.Invoke();
        }
    }

    /// <summary>The lore files embedded in GameServer.dll (data/lore/locations/*.json, data/lore/bosses/*.json).</summary>
    public static class EmbeddedLoreSource
    {
        public static LoreLibrary Load(Logger log = null)
        {
            try
            {
                Assembly assembly = typeof(EmbeddedLoreSource).Assembly;
                var files = new List<(string, string)>();
                foreach (string name in assembly.GetManifestResourceNames()
                             .Where(n => n.Contains(".data.lore.", StringComparison.Ordinal) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(n => n, StringComparer.Ordinal))
                {
                    using Stream stream = assembly.GetManifestResourceStream(name);
                    using var reader = new StreamReader(stream);
                    files.Add((name, reader.ReadToEnd()));
                }
                var errors = new List<string>();
                LoreLibrary library = LoreLibrary.Parse(files, errors);
                foreach (string error in errors)
                    log?.Warn("LORE_FILE_SKIPPED " + error);
                log?.Info($"LORE_CHANNELS locations={library.AllLocations.Count} bosses={library.AllBosses.Count} skipped={errors.Count}");
                return library;
            }
            catch (Exception exception)
            {
                log?.Error("Lore channels: the lore files could not be read; loremasters and books stay silent", exception);
                return LoreLibrary.Empty;
            }
        }
    }
}
