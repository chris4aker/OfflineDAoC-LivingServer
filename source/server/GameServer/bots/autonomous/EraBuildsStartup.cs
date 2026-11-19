using System;
using System.Reflection;
using DOL.Events;
using DOL.Logging;
using OfflineDaoc.Shared;

namespace DOL.GS
{
    /// <summary>
    /// Era builds 1.2 (docs/ERA-BUILDS-1.2.md, WP1): says at server start what the era data holds, so the owner's log shows
    /// which table the gamebots are drawn from. <c>ERA_BUILDS_LOADED</c> once; <c>ERA_BUILDS_INVALID</c> once per entry the
    /// loader dropped. Not behind a diagnostics switch: a handful of lines per start.
    /// </summary>
    public static class EraBuildsStartup
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        [GameServerStartedEvent]
        public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
        {
            try
            {
                EraBuildCatalog catalog = EraBuildCatalog.Default;
                foreach (string problem in catalog.Problems)
                    Log.Warn($"ERA_BUILDS_INVALID {problem}");
                Log.Info($"ERA_BUILDS_LOADED {catalog.LoadedLine()} classShares={(ServerProperties.Properties.BOT_ERA_CLASS_SHARES ? "on" : "off")}");
            }
            catch (Exception exception)
            {
                Log.Error("ERA_BUILDS_INVALID class=* build=* reason=\"the catalog could not be read\"", exception);
            }
        }
    }
}
