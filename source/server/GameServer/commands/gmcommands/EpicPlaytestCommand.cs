using System;
using System.Linq;
using System.Numerics;
using DOL.GS.PacketHandler;

namespace DOL.GS.Commands;

// /epictest, the play test's raid starter (1.2, owner 2026-11-07: "Let's do the play test by you on these encounters").
// It opens a forced realm raid of a chosen size on a chosen encounter, the launcher's forced-raid path with the period size
// (docs/RAID-SIZES.md) instead of 300, and moves the reserved level 50s to the raid's muster hub, which stands in for the
// realm's bots walking there from all over the world. From the hub on everything is the game's own: parties form, assemble,
// travel to the staging post, go in when 80% are staged, and the dungeon route, tactics and encounter scripts run the fight.
// It works from the server console (no player needed) and in game for a GM.
[CmdAttribute(
    "&epictest",
    ePrivLevel.GM,
    "Play test: open a realm raid of a chosen size on a chosen encounter",
    "/epictest list - every raid, its realm, period size and hub",
    "/epictest start <event> [size|era|start] [focus=Name,Name] [nohub] [atstaging] - a forced raid of <size> bots (era = the period size, start = 125%); the reserved bots are moved to its hub unless nohub; atstaging musters it at its own staging post instead of the realm's hub",
    "/epictest gather <event> - move the raid's party members still away from its hub to it (before it leaves), or those not yet on its staging ring to the entrance (before it goes in)",
    "/epictest front <event> - move the raid's living members inside its dungeon to the route's front (its next target, or the final encounter's trigger): skips the climb, for testing a final room",
    "/epictest reset <event> - clear an event's cooldown after a raid ended (the launcher's reset)",
    "/epictest status - the running raids",
    "/epictest familiarity <0..1|off> - test only: how well the realm knows every encounter for the raid discipline (calls, reactions, lapses; off = from the raid record). Also familiarity=<0..1> on start",
    "/epictest latecall <threshold|all|off> - test only: the raid's call of that blast threshold (Xanxicar 80/60/40/20) always comes late. Also latecall=<threshold|all> on start",
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
            case "reset":
                if (args.Length < 3) { DisplaySyntaxOf(client); return; }
                Say(client, AutonomousRealmRaid.ResetCooldown(args[2])
                    ? $"{args[2]}: the event cooldown is cleared (the encounter itself must be alive)."
                    : $"{args[2]} is running: stop it first.");
                return;
            case "familiarity":
                if (args.Length < 3) { Say(client, $"Raid familiarity: {(RaidDiscipline.FamiliarityOverride is double f ? f.ToString("0.00") : "from the raid record")}"); return; }
                Say(client, SetFamiliarity(args[2]));
                return;
            case "latecall":
                if (args.Length < 3) { Say(client, $"Forced late call: {LateCallWord(RaidDiscipline.ForceLateCallAt)}"); return; }
                Say(client, SetLateCall(args[2]));
                return;
            case "gather":
                if (args.Length < 3) { DisplaySyntaxOf(client); return; }
                if (!AutonomousRealmRaid.TryGetHub(args[2], out var gatherHub)) { Say(client, $"{args[2]} is not running."); return; }
                GameBot[] away = AutonomousRealmRaid.MembersToGather(args[2], out ushort gatherRegion, out Vector3 gatherPoint);
                string where = gatherRegion == gatherHub.Region && gatherPoint == gatherHub.Center ? gatherHub.Name : "the staging post";
                MoveTo(args[2], where, gatherRegion, gatherPoint, away);
                Say(client, $"Moving {away.Length} party members to {where} (the hub before the raid leaves it, then the staging post until it goes in).");
                return;
            case "front":
                if (args.Length < 3) { DisplaySyntaxOf(client); return; }
                GameBot[] behind = AutonomousRealmRaid.MembersToFront(args[2], out ushort frontRegion, out Vector3 frontPoint);
                if (frontRegion == 0) { Say(client, $"{args[2]} is not inside its dungeon."); return; }
                MoveTo(args[2], "the route's front", frontRegion, frontPoint, behind);
                Say(client, $"Moving {behind.Length} raiders to the route's front {(int)frontPoint.X},{(int)frontPoint.Y},{(int)frontPoint.Z}.");
                return;
            default:
                Say(client, "/epictest list | start <event> [size|era|start] [focus=Name,Name] [nohub] [familiarity=0..1] [latecall=N|all] | status | stop <event> | familiarity <0..1|off> | latecall <N|all|off>");
                return;
        }
    }

    private static string LateCallWord(int at) => at == -1 ? "every threshold" : at > 0 ? $"the {at}% call" : "off (the usual roll)";

    /// <summary>The play test's late-call override (pure but for setting it): a threshold, all, or off.</summary>
    public static string SetLateCall(string word)
    {
        int at;
        if (string.Equals(word, "off", StringComparison.OrdinalIgnoreCase)) at = 0;
        else if (string.Equals(word, "all", StringComparison.OrdinalIgnoreCase)) at = -1;
        else if (!int.TryParse(word, out at) || at <= 0 || at >= 100) return $"Late call \"{word}\" is not a threshold (1-99), all, or off.";
        RaidDiscipline.ForceLateCallAt = at;
        return $"Forced late call: {LateCallWord(at)} (test only; /epictest latecall off to undo).";
    }

    /// <summary>The play test's familiarity override (pure but for setting it): a number 0-1, or off.</summary>
    public static string SetFamiliarity(string word)
    {
        if (string.Equals(word, "off", StringComparison.OrdinalIgnoreCase))
        {
            RaidDiscipline.FamiliarityOverride = null;
            return "Raid familiarity: from the raid record again.";
        }
        if (!double.TryParse(word, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double f) ||
            f < 0 || f > 1)
            return $"Familiarity \"{word}\" is not a number from 0 to 1, or off.";
        RaidDiscipline.FamiliarityOverride = f;
        return $"Raid familiarity forced to {f:0.00} (test only; /epictest familiarity off to undo).";
    }

    /// <summary>The roster size a start asks for (pure): a number, "era" (the period size) or "start" (125% of it), made
    /// whole parties (eights, and a final four): the forced roster is built that way, so a dragon's 70 is 72.</summary>
    public static int SizeFor(string word, int periodSize)
    {
        int size;
        if (string.IsNullOrEmpty(word) || word == "era") size = periodSize;
        else if (word == "start") size = (periodSize * 125 + 99) / 100;
        else if (!int.TryParse(word, out size) || size < 8) return 0;
        size = Math.Min(size, RealmRaidRecruitmentPolicy.MaximumBots);
        return size % 8 is 0 or 4 ? size : (size + 7) / 8 * 8;
    }

    private static void Start(GameClient client, string[] args)
    {
        if (args.Length < 3) { DisplaySyntaxOf(client); return; }
        var definition = AutonomousRealmRaid.Definitions.FirstOrDefault(d => d.Id == args[2]);
        if (definition == null) { Say(client, $"No raid {args[2]}; /epictest list shows them."); return; }
        string sizeWord = args.Skip(3).FirstOrDefault(a => !a.StartsWith("focus=", StringComparison.OrdinalIgnoreCase) &&
            !a.StartsWith("familiarity=", StringComparison.OrdinalIgnoreCase) &&
            !a.StartsWith("latecall=", StringComparison.OrdinalIgnoreCase) && a != "nohub" && a != "atstaging");
        if (args.Skip(3).FirstOrDefault(a => a.StartsWith("familiarity=", StringComparison.OrdinalIgnoreCase)) is string fam)
            Say(client, SetFamiliarity(fam["familiarity=".Length..]));
        if (args.Skip(3).FirstOrDefault(a => a.StartsWith("latecall=", StringComparison.OrdinalIgnoreCase)) is string late)
            Say(client, SetLateCall(late["latecall=".Length..]));
        int size = SizeFor(sizeWord?.ToLowerInvariant(), definition.SuggestedBots);
        if (size <= 0) { Say(client, $"Size \"{sizeWord}\" is not a number of 8 or more, era or start."); return; }
        string[] focus = args.Skip(3).Where(a => a.StartsWith("focus=", StringComparison.OrdinalIgnoreCase))
            .SelectMany(a => a[6..].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToArray();
        bool moveToHub = !args.Contains("nohub");
        bool atStaging = args.Contains("atstaging");
        if (!AutonomousRealmRaid.Start(definition.Id, definition.Realm, out string reason, true, size, focus.Length > 0 ? focus : null, atStaging))
        {
            Say(client, $"Not started: {reason}");
            return;
        }
        Say(client, $"{definition.Name}: {reason}");
        if (!moveToHub || !AutonomousRealmRaid.TryGetHub(definition.Id, out var hub)) return;
        GameBot[] recruits = AutonomousRealmRaid.ReservedRecruits(definition.Id);
        MoveTo(definition.Id, hub.Name, hub.Region, hub.Center, recruits);
        Say(client, $"Moving {recruits.Length} reserved bots to {hub.Name} (EPIC_PLAYTEST_HUB in the log).");
    }

    private static void MoveTo(string eventId, string where, ushort regionId, Vector3 point, GameBot[] recruits)
    {
        if (regionId == 0 || recruits.Length == 0) return;
        NpcService.Instance.Post(bots =>
        {
            int moved = 0;
            Region region = WorldMgr.GetRegion(regionId);
            Zone zone = region?.GetZone((int)point.X, (int)point.Y);
            var nav = PathfindingProvider.Instance;
            bool meshed = zone != null && nav.IsAvailable && nav.HasNavmesh(zone);
            Vector3 center = point;
            if (meshed && !nav.TrySnapToMesh(zone, ref center, 256f)) center = point;
            foreach (GameBot bot in bots)
            {
                if (bot.ObjectState != GameObject.eObjectState.Active || !bot.IsAlive) continue;
                // Only a spot the hub's own floor connects to (a roof or a closed yard 500 out stranded bots: their walk to
                // the formation failed three times and the raid released them, 2026-11-07).
                Vector3 spot = HubSpot(center, moved, bots.Length);
                if (meshed && (!nav.TrySnapToMesh(zone, ref spot, 128f) || !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, spot, center)))
                    spot = center;
                if (bot.MoveTo(regionId, (int)spot.X, (int)spot.Y, (int)spot.Z, bot.Heading)) moved++;
            }
            Log.Info($"EPIC_PLAYTEST_HUB event={eventId} to=\"{where}\" moved={moved} of={bots.Length}");
        }, recruits);
    }

    /// <summary>The i-th of n spots around a hub (pure): rings 60-240 units out, so no two bots stand on one point.</summary>
    public static Vector3 HubSpot(Vector3 center, int i, int n)
    {
        int perRing = 24;
        int ring = i / perRing;
        double angle = (i % perRing) * Math.PI * 2 / perRing + ring * 0.13;
        float radius = 60 + 45 * Math.Min(ring, 4);
        return new Vector3(center.X + (float)(Math.Cos(angle) * radius), center.Y + (float)(Math.Sin(angle) * radius), center.Z);
    }

    private static void DisplaySyntaxOf(GameClient client) =>
        Say(client, "/epictest start <event> [size|era|start] [focus=Name,Name] [nohub] [atstaging]");

    private static void Say(GameClient client, string message)
    {
        if (client?.Player != null)
            client.Out.SendMessage(message, eChatType.CT_System, eChatLoc.CL_SystemWindow);
        else
            Console.WriteLine(message);
        Log.Info($"EPIC_PLAYTEST {message}");
    }
}
