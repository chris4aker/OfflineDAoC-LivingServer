using System;
using System.Linq;
using System.Numerics;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands;

// /epictest, the play test's raid starter (1.2, owner 2026-11-07: "Let's do the play test by you on these encounters").
// It opens a forced realm raid of a chosen size on a chosen encounter, the launcher's forced-raid path with the period size
// (docs/RAID-SIZES.md) instead of 300, and moves the reserved level 50s to the raid's muster hub, which stands in for the
// realm's bots walking there from all over the world. From the hub on everything is the game's own: parties form, assemble,
// travel to the staging post, go in when 90% are staged, and the dungeon route, tactics and encounter scripts run the fight.
// It works from the server console (no player needed) and in game for a GM.
[CmdAttribute(
    "&epictest",
    ePrivLevel.GM,
    "Play test: open a realm raid of a chosen size on a chosen encounter",
    "/epictest list - every raid, its realm, period size and hub",
    "/epictest start <event> [size|era|start] [focus=Name,Name] [nohub] - a forced raid of <size> bots (era = the period size, start = 125%); the reserved bots are moved to its hub unless nohub",
    "/epictest gather <event> - move the raid's party members still away from its hub to it (before it leaves the hub)",
    "/epictest status - the running raids",
    "/epictest stop <event> - end a running raid")]
public sealed class EpicPlaytestCommandHandler : AbstractCommandHandler, ICommandHandler
{
    private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EpicPlaytestCommandHandler));

    public void OnCommand(GameClient client, string[] args)
    {
        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        switch (sub)
        {
            case "list":
                foreach (var d in AutonomousRealmRaid.Definitions)
                {
                    var hub = RealmRaidMuster.Hubs.FirstOrDefault(h => h.Event == d.Id);
                    Say(client, $"{d.Id}: {d.Name} ({GlobalConstants.RealmToName(d.Realm)}), period size {d.SuggestedBots}, hub {hub?.Name ?? "none"}");
                }
                return;
            case "status":
                foreach (var s in AutonomousRealmRaid.Snapshot().Where(s => s.Assigned > 0 || s.State.StartsWith("RAID") || s.State.StartsWith("Raid rally")))
                    Say(client, $"{s.Id}: {s.State} assigned={s.Assigned} present={s.Present} required={s.Suggested} phase={s.Phase}");
                return;
            case "stop":
                if (args.Length < 3) { DisplaySyntaxOf(client); return; }
                AutonomousRealmRaid.Stop(args[2], out string stopped);
                Say(client, stopped);
                return;
            case "start":
                Start(client, args);
                return;
            case "gather":
                if (args.Length < 3) { DisplaySyntaxOf(client); return; }
                if (!AutonomousRealmRaid.TryGetHub(args[2], out var gatherHub)) { Say(client, $"{args[2]} is not running."); return; }
                GameBot[] away = AutonomousRealmRaid.MembersAwayFromHub(args[2]);
                MoveToHub(args[2], gatherHub, away);
                Say(client, $"Moving {away.Length} party members still away from {gatherHub.Name} to it (before the raid leaves the hub only).");
                return;
            default:
                Say(client, "/epictest list | start <event> [size|era|start] [focus=Name,Name] [nohub] | status | stop <event>");
                return;
        }
    }

    /// <summary>The roster size a start asks for (pure): a number, "era" (the period size) or "start" (125% of it).</summary>
    public static int SizeFor(string word, int periodSize)
    {
        if (string.IsNullOrEmpty(word) || word == "era") return periodSize;
        if (word == "start") return (periodSize * 125 + 99) / 100;
        return int.TryParse(word, out int n) && n >= 8 ? Math.Min(n, RealmRaidRecruitmentPolicy.MaximumBots) : 0;
    }

    private static void Start(GameClient client, string[] args)
    {
        if (args.Length < 3) { DisplaySyntaxOf(client); return; }
        var definition = AutonomousRealmRaid.Definitions.FirstOrDefault(d => d.Id == args[2]);
        if (definition == null) { Say(client, $"No raid {args[2]}; /epictest list shows them."); return; }
        string sizeWord = args.Skip(3).FirstOrDefault(a => !a.StartsWith("focus=", StringComparison.OrdinalIgnoreCase) && a != "nohub");
        int size = SizeFor(sizeWord?.ToLowerInvariant(), definition.SuggestedBots);
        if (size <= 0) { Say(client, $"Size \"{sizeWord}\" is not a number of 8 or more, era or start."); return; }
        string[] focus = args.Skip(3).Where(a => a.StartsWith("focus=", StringComparison.OrdinalIgnoreCase))
            .SelectMany(a => a[6..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray();
        bool moveToHub = !args.Contains("nohub");
        if (!AutonomousRealmRaid.Start(definition.Id, definition.Realm, out string reason, true, size, focus.Length > 0 ? focus : null))
        {
            Say(client, $"Not started: {reason}");
            return;
        }
        Say(client, $"{definition.Name}: {reason}");
        if (!moveToHub || !AutonomousRealmRaid.TryGetHub(definition.Id, out var hub)) return;
        GameBot[] recruits = AutonomousRealmRaid.ReservedRecruits(definition.Id);
        MoveToHub(definition.Id, hub, recruits);
        Say(client, $"Moving {recruits.Length} reserved bots to {hub.Name} (EPIC_PLAYTEST_HUB in the log).");
    }

    private static void MoveToHub(string eventId, RealmRaidMuster.Hub hub, GameBot[] recruits)
    {
        NpcService.Instance.Post(bots =>
        {
            int moved = 0;
            Region region = WorldMgr.GetRegion(hub.Region);
            Zone zone = region?.GetZone((int)hub.Center.X, (int)hub.Center.Y);
            foreach (GameBot bot in bots)
            {
                if (bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive) continue;
                Vector3 spot = HubSpot(hub.Center, moved, bots.Length);
                if (zone != null && PathfindingProvider.Instance.IsAvailable && PathfindingProvider.Instance.HasNavmesh(zone) &&
                    !PathfindingProvider.Instance.TrySnapToMesh(zone, ref spot, 256f))
                    spot = hub.Center;
                if (bot.MoveTo(hub.Region, (int)spot.X, (int)spot.Y, (int)spot.Z, bot.Heading)) moved++;
            }
            Log.Info($"EPIC_PLAYTEST_HUB event={eventId} hub=\"{hub.Name}\" moved={moved} of={bots.Length}");
        }, recruits);
    }

    /// <summary>The i-th of n spots around a hub (pure): rings 150-600 units out, so no two bots stand on one point.</summary>
    public static Vector3 HubSpot(Vector3 center, int i, int n)
    {
        int perRing = 24;
        int ring = i / perRing;
        double angle = (i % perRing) * Math.PI * 2 / perRing + ring * 0.13;
        float radius = 150 + 90 * Math.Min(ring, 5);
        return new Vector3(center.X + (float)(Math.Cos(angle) * radius), center.Y + (float)(Math.Sin(angle) * radius), center.Z);
    }

    private static void DisplaySyntaxOf(GameClient client) =>
        Say(client, "/epictest start <event> [size|era|start] [focus=Name,Name] [nohub]");

    private static void Say(GameClient client, string message)
    {
        if (client?.Player != null)
            client.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        else
            Console.WriteLine(message);
        Log.Info($"EPIC_PLAYTEST {message}");
    }
}
