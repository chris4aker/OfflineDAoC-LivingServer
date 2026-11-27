using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Reflection;
using DOL.Logging;
using OpenTelemetry.Metrics;

namespace DOL.GS.Metrics.Meters;

/// <summary>
/// Bots online (#653, owner 2026-11-26): the OTLP gauge <c>daoc.online.bot.count</c> with the attributes <c>type</c>
/// (<c>gamebot</c> or <c>companion</c>) and <c>realm</c>. Numbers only: no name, account or position. Read from the bot
/// registries on each export. Real players are never counted here; they keep <c>daoc.online.player.count.*</c>
/// (GameBot is a GameNPC, so ClientService's player list never holds a bot either).
/// <list type="bullet">
/// <item><c>gamebot</c>: the autonomous world bots, the Epic Special Forces and the No Lifers' buffbots.</item>
/// <item><c>companion</c>: a player's own bots: hired companions, <c>/bot</c> companions and the <c>/spawn</c> and
/// <c>/raid</c> helpers in a player's group.</item>
/// </list>
/// Only exported when <c>Server/MetricsEnabled</c> is on in serverconfig.xml (off by default: diagnostics).
/// </summary>
public class BotMeterProvider : IMeterProvider
{
    private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public const string GaugeName = "daoc.online.bot.count";
    public const string Gamebot = "gamebot";
    public const string Companion = "companion";

    /// <summary>The realms every export reports, zero or not, so each series stays continuous.</summary>
    private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

    public string MeterName => "GameServer.BotMetrics";

    public void Register(MeterProviderBuilder meterProviderBuilder)
    {
        meterProviderBuilder.AddMeter(MeterName);
        Meter meter = new(MeterName);

        meter.CreateObservableGauge(
            GaugeName,
            OnlineBots,
            description: "Bots online by type (gamebot or companion) and realm; real players are not counted"
        );
    }

    /// <summary>Pure: the bot's type for the gauge. A hired companion is an autonomous world bot row its player owns, so
    /// it counts as a companion; every other autonomous world bot is a gamebot.</summary>
    public static string TypeOf(bool isAutonomousWorldBot, bool isHiredCompanion) =>
        isAutonomousWorldBot && !isHiredCompanion ? Gamebot : Companion;

    /// <summary>Pure: the realm attribute's value (<c>Albion</c>, <c>Midgard</c>, <c>Hibernia</c>, as the player meter
    /// writes them, or <c>other</c>).</summary>
    public static string RealmName(eRealm realm) => realm switch
    {
        eRealm.Albion => "Albion",
        eRealm.Midgard => "Midgard",
        eRealm.Hibernia => "Hibernia",
        _ => "other",
    };

    /// <summary>
    /// Pure: the counts by type and realm. Always the six type-and-realm series for the three realms (zero included),
    /// plus an <c>other</c> realm series only for a type that has a bot with no realm. Each bot is counted once.
    /// </summary>
    public static List<(string Type, string Realm, int Count)> Count(IEnumerable<(string Type, eRealm Realm)> bots)
    {
        var counts = new Dictionary<(string, string), int>();
        foreach (var (type, realm) in bots ?? [])
        {
            if (type != Gamebot && type != Companion)
                continue;
            var key = (type, RealmName(realm));
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        var result = new List<(string, string, int)>(8);
        foreach (string type in new[] { Gamebot, Companion })
        {
            foreach (eRealm realm in Realms)
                result.Add((type, RealmName(realm), counts.GetValueOrDefault((type, RealmName(realm)))));
            if (counts.TryGetValue((type, "other"), out int other))
                result.Add((type, "other", other));
        }
        return result;
    }

    /// <summary>Every bot in the world now, each once: the same sources as GM /jump (JumpBotTargets.AllBots).</summary>
    internal static IEnumerable<GameBot> BotsInWorld()
    {
        var seen = new HashSet<GameBot>();
        IEnumerable<GameBot> sources = AutonomousBotRegistry.Snapshot()
            .Concat(AutonomousBotRegistry.SpecialForcesSnapshot())
            .Concat(AutonomousBotRegistry.BuffBotSnapshot())
            .Concat(AutonomousBotRegistry.HiredSnapshot())
            .Concat(BotManager.ActiveBotsSnapshot())
            .Concat(ClientService.Instance.GetPlayers()
                .Where(player => player?.Group != null)
                .SelectMany(player => player.Group.GetMembersInTheGroup().OfType<GameBot>()));
        foreach (GameBot bot in sources)
            if (bot?.ObjectState == GameObject.eObjectState.Active && seen.Add(bot))
                yield return bot;
    }

    private static List<Measurement<int>> OnlineBots()
    {
        try
        {
            return Count(BotsInWorld().Select(bot => (TypeOf(bot.IsAutonomousWorldBot, bot.IsHiredCompanion), bot.Realm)))
                .Select(row => new Measurement<int>(row.Count, new("type", row.Type), new("realm", row.Realm)))
                .ToList();
        }
        catch (Exception e)
        {
            log.Error("BotMeterProvider.OnlineBots threw an exception", e);
            return [];
        }
    }
}
