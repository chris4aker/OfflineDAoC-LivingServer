using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>What a scripted move does to a raider.</summary>
    public enum DisplacementKind
    {
        /// <summary>Hurled: into the air or across the room (lands and walks back).</summary>
        Throw,
        /// <summary>Teleported to a spot (walks back).</summary>
        Teleport,
        /// <summary>Put in a jail (waits until the encounter frees it).</summary>
        Jail,
        /// <summary>Pulled to the boss (nothing to recover from).</summary>
        Pull,
        /// <summary>Pushed back from a place it may not enter (stays where it was put; walking back would only trip it again).</summary>
        Barrier,
    }

    /// <summary>How a displaced bot is expected to get back.</summary>
    public enum DisplacementRecovery
    {
        /// <summary>Lands (fall damage), then walks back.</summary>
        Falls,
        /// <summary>Walks back.</summary>
        WalkBack,
        /// <summary>Waits until the encounter frees it (the jail's rule), then walks back.</summary>
        Wait,
        /// <summary>Needs the raid to free it (kill the jail's guards); fights them meanwhile.</summary>
        NeedsRescue,
        /// <summary>Nothing to do.</summary>
        None,
    }

    /// <summary>A raider moved by an encounter script (see <see cref="EncounterDisplacement.Displaced"/>).</summary>
    /// <param name="FallHeight">Units above the floor it lands on (0: on the floor).</param>
    /// <param name="Landing">Where a bot lands (the floor under <paramref name="To"/>, or the nearest floor); for a player, To.</param>
    /// <param name="OnMesh">False: no walkable floor under or near the spot (the bot gets the fallback recall).</param>
    public sealed record DisplacementEvent(GameLiving Living, GameObject Source, string Encounter, DisplacementKind Kind,
        DisplacementRecovery Expected, ushort Region, Vector3 From, Vector3 To, Vector3 Landing, int FallHeight, bool OnMesh,
        long At, bool IsBot);

    /// <summary>
    /// One way for every encounter script to throw, teleport, jail or pull a raider (1.2, the epic "wow" pass). A real player
    /// is moved exactly as the script always did (<see cref="GamePlayer.MoveTo(ushort,int,int,int,ushort)"/>; the client
    /// does its own fall), but the move is not a party portal: the player's companions and pet stay at the fight instead
    /// of being carried along (as the dragon and Legion throws already did). A bot (companion or autonomous gamebot) is
    /// moved too when the encounter's <see cref="EpicEncounterMechanics.AllowBotDisplacement"/> is on: it is placed where
    /// the player would be, falls to the floor the navmesh has under that spot with the player's fall damage, and
    /// <see cref="EncounterDisplacementRecovery"/> brings it back (walk, wait for the jail, or a short recall).
    /// Every move raises <see cref="Displaced"/> and logs EPIC_DISPLACEMENT.
    /// </summary>
    public static class EncounterDisplacement
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        // The keys the dragon throw already used (DragonCombatGeometry): companions, pets and the stable-travel coordinator
        // read them, so every encounter move now gets the same treatment.
        internal const string ActiveKey = "dragon_throw_active";
        internal const string UntilKey = "dragon_throw_until";

        /// <summary>How long a moved raider counts as recovering (no companion recall, no pet teleport, heal priority).</summary>
        public const int RecoveryWindowMilliseconds = 15_000;

        /// <summary>The client's gravity: 18 m/s² at 40 units a metre (the new client's player.gd matches the old client).</summary>
        public const double Gravity = 720;

        /// <summary>A spot more than this above its floor is a fall.</summary>
        public const int FallThreshold = 64;

        /// <summary>How far down a landing floor is looked for.</summary>
        public const int FloorSearchDepth = 4096;

        /// <summary>How far sideways a landing floor is looked for when there is none under the spot.</summary>
        public const int LandingLateralSearch = 600;

        /// <summary>A bot or player moved by an encounter (game thread; handlers must be quick).</summary>
        public static event Action<DisplacementEvent> Displaced;

        private static readonly ConcurrentDictionary<string, byte> RefusedLogged = new(StringComparer.Ordinal);
        private static readonly ConcurrentDictionary<string, byte> OffMeshLogged = new(StringComparer.Ordinal);

        /// <summary>
        /// Moves <paramref name="living"/> for the encounter <paramref name="source"/> (the boss, or the add or object that casts
        /// the mechanic). Returns true when it was moved. A bot is refused (stays, false) while the encounter's
        /// <see cref="EpicEncounterMechanics.AllowBotDisplacement"/> is off, across regions, or dead.
        /// </summary>
        public static bool Move(GameLiving living, ushort region, int x, int y, int z, ushort heading, DisplacementKind kind,
            GameObject source)
        {
            if (living == null || !living.IsAlive)
                return false;
            if (living is GamePlayer player)
                return MovePlayer(player, region, x, y, z, heading, kind, source);
            if (living is GameBot bot)
                return MoveBot(bot, region, x, y, z, heading, kind, source);
            // Anything else (a pet a script picked): as the script always did.
            return living.MoveTo(region, x, y, z, heading);
        }

        /// <summary>A real player's /spawn companions and pet stay at the fight when an encounter moves the player (owner default,
        /// server property <c>epic_companions_stay_when_thrown</c>, on). Off: the move takes them along like a portal.</summary>
        public static bool CompanionsStay => ServerProperties.Properties.EPIC_COMPANIONS_STAY_WHEN_THROWN;

        /// <summary>Encounters whose move never takes companions along, whatever the setting: Legion's platform has no way
        /// down but a zone point bots can't use (2026-10-30: companions dragged up there were stuck).</summary>
        public static readonly string[] CompanionsAlwaysStay = ["Legion"];

        /// <summary><see cref="CompanionsStay"/> for one encounter (<see cref="CompanionsAlwaysStay"/> always).</summary>
        public static bool CompanionsStayFor(GameObject source) =>
            CompanionsStay || Array.IndexOf(CompanionsAlwaysStay, EpicEncounterMechanics.EncounterName(source)) >= 0;

        /// <summary>The raider is inside a displacement move right now (a portal handler must not treat it as a party portal).</summary>
        public static bool IsMoving(GameLiving living) => living?.TempProperties.GetProperty<bool>(ActiveKey) == true;

        /// <summary>The raider was moved by an encounter in the last <see cref="RecoveryWindowMilliseconds"/>.</summary>
        public static bool IsRecovering(GameLiving living) => living != null &&
            living.TempProperties.GetProperty<long>(UntilKey) > GameLoop.GameLoopTime;

        /// <summary>What a kind of move leaves a bot to do.</summary>
        public static DisplacementRecovery Expect(DisplacementKind kind, int fallHeight, bool jailNeedsRescue) => kind switch
        {
            DisplacementKind.Jail => jailNeedsRescue ? DisplacementRecovery.NeedsRescue : DisplacementRecovery.Wait,
            DisplacementKind.Pull or DisplacementKind.Barrier => fallHeight > FallThreshold ? DisplacementRecovery.Falls : DisplacementRecovery.None,
            _ => fallHeight > FallThreshold ? DisplacementRecovery.Falls : DisplacementRecovery.WalkBack,
        };

        /// <summary>
        /// The player's fall damage in percent of health for a fall of <paramref name="height"/> units (the server's rule,
        /// PlayerPositionUpdateHandler: the landing speed less 100 per Safe Fall level, over 500, divided by 15, at most 99).
        /// </summary>
        public static int FallDamagePercent(int height, int safeFallLevel)
        {
            if (height <= 0) return 0;
            double speed = Math.Sqrt(2 * Gravity * height) - 100 * Math.Max(0, safeFallLevel);
            if (speed <= 500) return 0;
            return (int)Math.Min(99, (speed - 501) / 15);
        }

        /// <summary>How long a fall of <paramref name="height"/> units takes, in milliseconds.</summary>
        public static int FallMilliseconds(int height) => height <= 0 ? 0 : (int)Math.Round(Math.Sqrt(2.0 * height / Gravity) * 1000);

        /// <summary>The floor a bot moved to <paramref name="spot"/> lands on: straight under it (or a little above, for a
        /// spot just under a floor), else the nearest walkable floor within <see cref="LandingLateralSearch"/>. False: no navmesh
        /// here, or no floor near the spot.</summary>
        public static bool TryLanding(IPathfindingMgr nav, Zone zone, Vector3 spot, out Vector3 landing)
        {
            landing = spot;
            if (nav == null || zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone))
                return false;
            EDtPolyFlags[] filters = nav.DefaultFilters;
            Vector3? floor = nav.GetFloorBeneath(zone, spot + new Vector3(0, 0, 32), FloorSearchDepth, filters);
            if (floor.HasValue && Math.Abs(floor.Value.X - spot.X) <= 64 && Math.Abs(floor.Value.Y - spot.Y) <= 64)
            {
                landing = floor.Value;
                return true;
            }
            Vector3? near = nav.GetClosestPoint(zone, spot, LandingLateralSearch, LandingLateralSearch, FloorSearchDepth, filters);
            if (near.HasValue && near.Value.Z <= spot.Z + 128)
            {
                landing = near.Value;
                return true;
            }
            return false;
        }

        private static void MarkRecovering(GameLiving living, long now) =>
            living.TempProperties.SetProperty(UntilKey, now + RecoveryWindowMilliseconds);

        private static bool MovePlayer(GamePlayer player, ushort region, int x, int y, int z, ushort heading, DisplacementKind kind,
            GameObject source)
        {
            long now = GameLoop.GameLoopTime;
            var from = new Vector3(player.X, player.Y, player.Z);
            // Encounter displacement is NOT a party portal: helpers and pets stay at the fight (GamePlayer.MoveTo and
            // TemporaryGroupStableTravel read the flag); the real client handles its own fall. The server property
            // epic_companions_stay_when_thrown (CompanionsStay) set to False makes it one again: they come along.
            if (CompanionsStayFor(source))
                player.TempProperties.SetProperty(ActiveKey, true);
            MarkRecovering(player, now);
            bool moved;
            try { moved = player.MoveTo(region, x, y, z, heading); }
            finally { player.TempProperties.RemoveProperty(ActiveKey); }
            if (!moved) return false;
            var to = new Vector3(x, y, z);
            Raise(new DisplacementEvent(player, source, EpicEncounterMechanics.EncounterName(source), kind,
                Expect(kind, 0, false), region, from, to, to, 0, true, now, false));
            return true;
        }

        private static bool MoveBot(GameBot bot, ushort region, int x, int y, int z, ushort heading, DisplacementKind kind,
            GameObject source)
        {
            string encounter = EpicEncounterMechanics.EncounterName(source);
            if (!EpicEncounterMechanics.AllowBotDisplacementFor(source))
            {
                if (RefusedLogged.TryAdd(encounter ?? "?", 0) && Log.IsInfoEnabled)
                    Log.Info($"EPIC_DISPLACEMENT_BOT_OFF encounter={encounter} kind={kind} bot=\"{bot.Name}\" (AllowBotDisplacement is off " +
                             "for this encounter: the bot stays)");
                return false;
            }
            if (bot.CurrentRegionID != region || bot.ObjectState != GameObject.eObjectState.Active)
                return false;
            long now = GameLoop.GameLoopTime;
            var from = new Vector3(bot.X, bot.Y, bot.Z);
            var to = new Vector3(x, y, z);
            Zone zone = bot.CurrentRegion?.GetZone(x, y);
            bool onMesh = TryLanding(PathfindingProvider.Instance, zone, to, out Vector3 landing);
            if (!onMesh)
            {
                landing = to;
                if (OffMeshLogged.TryAdd($"{encounter}:{region}:{x}:{y}:{z}", 0) && Log.IsWarnEnabled)
                    Log.Warn($"EPIC_DISPLACEMENT_OFF_MESH encounter={encounter} region={region} spot={x},{y},{z} zone={zone?.ID} " +
                             "(no walkable floor under or near the spot: a bot moved there is recalled to its raid)");
            }
            int fall = onMesh ? Math.Max(0, (int)Math.Round(to.Z - landing.Z)) : 0;
            bool needsRescue = EncounterDisplacementRecovery.JailNeedsRescue(encounter);
            DisplacementRecovery expected = Expect(kind, fall, needsRescue);

            bot.StopMoving();
            if (bot.IsCasting) bot.StopCurrentSpellcast();
            bot.TempProperties.SetProperty(ActiveKey, true);
            MarkRecovering(bot, now);
            bool moved;
            try
            {
                // In the region: no world removal (the bot keeps its group, aggro and pet); the clients see it at the spot.
                Vector3 first = fall > FallThreshold ? to : landing;
                moved = bot.MoveInRegion(region, (int)Math.Round(first.X), (int)Math.Round(first.Y), (int)Math.Round(first.Z), heading, true);
            }
            finally { bot.TempProperties.RemoveProperty(ActiveKey); }
            if (!moved) return false;
            var e = new DisplacementEvent(bot, source, encounter, kind, expected, region, from, to, landing, fall, onMesh, now, true);
            EncounterDisplacementRecovery.Begin(bot, e);
            Raise(e);
            return true;
        }

        private static void Raise(DisplacementEvent e)
        {
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_DISPLACEMENT who=\"{e.Living.Name}\" bot={(e.IsBot ? "yes" : "no")} encounter={e.Encounter} " +
                         $"source=\"{e.Source?.Name}\" kind={e.Kind} expected={e.Expected} region={e.Region} " +
                         $"from={(int)e.From.X},{(int)e.From.Y},{(int)e.From.Z} to={(int)e.To.X},{(int)e.To.Y},{(int)e.To.Z} " +
                         $"landing={(int)e.Landing.X},{(int)e.Landing.Y},{(int)e.Landing.Z} fall={e.FallHeight} on_mesh={(e.OnMesh ? "yes" : "no")}");
            EncounterDisplacementRecovery.Observe(e);
            try { Displaced?.Invoke(e); }
            catch (Exception ex) { Log.Error("EPIC_DISPLACEMENT handler failed", ex); }
        }
    }
}
