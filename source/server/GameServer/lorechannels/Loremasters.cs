using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS.LoreChannels
{
    /// <summary>
    /// One capital's loremaster: who, and the stock NPC spot they stand beside. Anchors are world NPCs of every release
    /// world (Camelot's library, Jordheim's temple of the gods, Tir na Nog's filidh by the bards); the loremaster takes
    /// the first free spot on the floor beside the anchor, never on top of it.
    /// </summary>
    public sealed record LoremasterDefinition(int Realm, string Name, ushort RegionId, ushort Model, byte Size, eGender Gender,
        Vector3 Anchor, ushort AnchorHeading, string AnchorName);

    /// <summary>Where the dry run puts a loremaster, or why it can't (Spot null).</summary>
    public sealed record LoremasterSpawnPlan(LoremasterDefinition Definition, Vector3? Spot, ushort Heading, string Reason);

    /// <summary>
    /// The three capital loremasters (Living Server 1.2): script-owned NPCs, the way this fork adds its world NPCs
    /// (FactionEmissaryRuntime): nothing is written to the world database, so a played world is never edited and taking
    /// the build out (ROLLBACK.cmd) takes them out. <see cref="Plan"/> is the dry run (where each would stand, checked
    /// against the floor and the NPCs around it), <see cref="Apply"/> spawns, <see cref="Rollback"/> removes them (also
    /// when the scripts unload). Players only: they ignore bots.
    /// </summary>
    public static class Loremasters
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>Spots tried beside the anchor: along its sides, then a little in front (units, its own frame).</summary>
        public static readonly (float Side, float Forward)[] Offsets =
            [(130, 0), (-130, 0), (190, 0), (-190, 0), (130, 90), (-130, 90), (0, 160), (250, 0), (-250, 0)];

        /// <summary>No other NPC this close to the spot.</summary>
        public const float Clearance = 85;
        /// <summary>The floor must be this close to the anchor's height (no balcony below or roof above).</summary>
        public const float MaxHeightDifference = 48;

        public static readonly LoremasterDefinition[] Definitions =
        [
            // Camelot's library: Librarian Pelan (world Mob, region 10). Model 279 is the Avalonian scholar Librarian Ophus wears.
            new(LoreRealm.Albion, "Loremaster Aldwin", 10, 279, 50, eGender.Male, new(34667, 30504, 8207), 428, "Librarian Pelan"),
            // Jordheim's temple of the gods: the Gothi of Ymir (region 101). Model 217, a Norse woman of the town.
            new(LoreRealm.Midgard, "Loremaster Sigrun", 101, 217, 50, eGender.Female, new(34083, 32649, 8030), 3288, "Gothi of Ymir"),
            // Tir na Nog, by the bards: Filidh Meilseior (region 201). Model 313, the Celt the filidh wear elsewhere.
            new(LoreRealm.Hibernia, "Loremaster Eithne", 201, 313, 50, eGender.Female, new(34496, 36123, 8098), 1513, "Filidh Meilseior"),
        ];

        public static LoremasterDefinition For(int realm) => Definitions.FirstOrDefault(d => d.Realm == realm);

        /// <summary>A spot beside the anchor in its frame: DOL heading h faces (-sin, cos); its side is (cos, sin).</summary>
        public static Vector3 Beside(Vector3 anchor, ushort heading, float side, float forward)
        {
            double angle = heading * 2 * Math.PI / 4096;
            return new Vector3((float)(anchor.X + side * Math.Cos(angle) - forward * Math.Sin(angle)),
                (float)(anchor.Y + side * Math.Sin(angle) + forward * Math.Cos(angle)), anchor.Z);
        }

        /// <summary>
        /// The dry run for one loremaster. <paramref name="floor"/> gives the floor near a point (null: none; the world
        /// without navmesh passes the point itself); <paramref name="sight"/> whether the anchor sees the spot;
        /// <paramref name="others"/> the NPCs standing around. Nothing is spawned.
        /// </summary>
        public static LoremasterSpawnPlan Plan(LoremasterDefinition definition, Func<Vector3, Vector3?> floor,
            Func<Vector3, Vector3, bool> sight, IReadOnlyCollection<Vector3> others)
        {
            foreach (var (side, forward) in Offsets)
            {
                Vector3 wanted = Beside(definition.Anchor, definition.AnchorHeading, side, forward);
                Vector3? found = floor(wanted);
                if (found is not Vector3 spot) continue;
                if (Vector2.Distance(new(spot.X, spot.Y), new(wanted.X, wanted.Y)) > 40 || Math.Abs(spot.Z - definition.Anchor.Z) > MaxHeightDifference) continue;
                if (others.Any(o => Vector2.Distance(new(o.X, o.Y), new(spot.X, spot.Y)) < Clearance && Math.Abs(o.Z - spot.Z) < 200)) continue;
                if (!sight(definition.Anchor, spot)) continue;
                return new(definition, spot, definition.AnchorHeading, null);
            }
            return new(definition, null, 0, $"no free floor beside {definition.AnchorName}");
        }

        // --- the running server

        private static readonly ConcurrentDictionary<int, LoremasterNPC> Spawned = new();

        [ScriptLoadedEvent]
        public static void ScriptLoaded(DOLEvent e, object sender, EventArgs args)
        {
            Apply();
            LoreChannelHub.SourceChanged -= Rebuild;
            LoreChannelHub.SourceChanged += Rebuild;
        }

        [ScriptUnloadedEvent]
        public static void ScriptUnloaded(DOLEvent e, object sender, EventArgs args)
        {
            LoreChannelHub.SourceChanged -= Rebuild;
            Rollback();
        }

        private static void Rebuild()
        {
            foreach (LoremasterNPC npc in Spawned.Values) npc.Rebuild();
        }

        /// <summary>The live dry run: where each loremaster would stand in this world.</summary>
        public static List<LoremasterSpawnPlan> PlanWorld()
        {
            var plans = new List<LoremasterSpawnPlan>();
            foreach (LoremasterDefinition definition in Definitions)
            {
                Region region = WorldMgr.GetRegion(definition.RegionId);
                Zone zone = region?.GetZone((int)definition.Anchor.X, (int)definition.Anchor.Y);
                if (region == null || region.IsDisabled || zone == null)
                {
                    plans.Add(new(definition, null, 0, "region not loaded"));
                    continue;
                }
                IPathfindingMgr nav = PathfindingProvider.Instance;
                bool mesh = nav.IsAvailable && nav.HasNavmesh(zone);
                var others = region.Objects.OfType<GameNPC>().Where(n => n.ObjectState == GameObject.eObjectState.Active && n is not GameBot &&
                        Math.Abs(n.X - definition.Anchor.X) < 600 && Math.Abs(n.Y - definition.Anchor.Y) < 600)
                    .Select(n => new Vector3(n.X, n.Y, n.Z)).ToList();
                plans.Add(Plan(definition,
                    p => mesh ? nav.GetClosestPoint(zone, p, 24, 24, 64, nav.DefaultFilters) : p,
                    (a, b) => !mesh || nav.HasLineOfSight(zone, a + new Vector3(0, 0, 60), b + new Vector3(0, 0, 60), nav.DefaultFilters),
                    others));
            }
            return plans;
        }

        public static void Apply()
        {
            foreach (LoremasterSpawnPlan plan in PlanWorld())
            {
                if (plan.Spot is not Vector3 spot)
                {
                    Log.Warn($"LOREMASTER_SKIPPED realm={LoreRealm.Name(plan.Definition.Realm)} reason=\"{plan.Reason}\"");
                    continue;
                }
                if (Spawned.ContainsKey(plan.Definition.Realm)) continue;
                LoremasterDefinition d = plan.Definition;
                Region region = WorldMgr.GetRegion(d.RegionId);
                var npc = new LoremasterNPC(d)
                {
                    Name = d.Name,
                    GuildName = "Loremaster",
                    Realm = (eRealm)d.Realm,
                    Gender = d.Gender,
                    Model = d.Model,
                    Size = d.Size,
                    Level = 50,
                    CurrentRegionID = d.RegionId,
                    CurrentRegion = region,
                    X = (int)spot.X,
                    Y = (int)spot.Y,
                    Z = (int)spot.Z,
                    Heading = plan.Heading,
                    LoadedFromScript = true,
                    MaxSpeedBase = 0,
                };
                npc.Flags |= GameNPC.eFlags.PEACE;
                if (npc.AddToWorld())
                {
                    Spawned[d.Realm] = npc;
                    Log.Info($"LOREMASTER_SPAWNED name=\"{d.Name}\" region={d.RegionId} x={npc.X} y={npc.Y} z={npc.Z} beside=\"{d.AnchorName}\"");
                }
            }
        }

        public static void Rollback()
        {
            foreach (int realm in Spawned.Keys.ToArray())
                if (Spawned.TryRemove(realm, out LoremasterNPC npc) && npc.ObjectState == GameObject.eObjectState.Active)
                    npc.Delete();
        }
    }

    /// <summary>A capital loremaster: keyword dialogue from the lore bible, and books for a silver.</summary>
    public class LoremasterNPC : GameNPC
    {
        private readonly LoremasterDefinition _definition;
        private LoremasterDialogue _dialogue;
        private readonly ConcurrentDictionary<string, string> _context = new(StringComparer.Ordinal);

        public LoremasterNPC(LoremasterDefinition definition)
        {
            _definition = definition;
            Rebuild();
        }

        public void Rebuild() => _dialogue = new LoremasterDialogue(LoreChannelHub.Source, _definition.Realm, _definition.Name);

        /// <summary>Long answers go in pieces (the popup window's lines stay short; the new client stacks them).</summary>
        /// <summary>A reply in chat-sized pieces, on paragraph, then sentence, then word boundaries (LorePieces).</summary>
        public static IEnumerable<string> Pieces(string text, int max = LorePieces.SayChars) => LorePieces.Split(text, max);

        private void Say(GamePlayer player, string text)
        {
            foreach (string piece in Pieces(text)) SayTo(player, piece);
        }

        public override bool Interact(GamePlayer player)
        {
            if (player == null || !base.Interact(player)) return false;
            TurnTo(player, 10000);
            Say(player, _dialogue.Greet(player.Name).Text);
            return true;
        }

        public override bool WhisperReceive(GameLiving source, string text)
        {
            if (!base.WhisperReceive(source, text) || source is not GamePlayer player) return false;
            string key = player.InternalID ?? player.Name;
            LoremasterReply reply = _dialogue.Resolve(text, _context.GetValueOrDefault(key));
            if (reply == null)
            {
                SayTo(player, "I know no tale by that name. Ask me of the [places] I keep, or the [books].");
                return true;
            }
            if (reply.LocationId != null) _context[key] = reply.LocationId;
            Say(player, reply.Text);
            if (reply.BookLocationId != null)
            {
                List<DbItemTemplate> volumes = LoreBooks.OfferAll(reply.BookLocationId);
                if (volumes.Count == 0)
                {
                    SayTo(player, "My copy is still at the binder's. Ask me another day.");
                    return true;
                }
                string title = volumes.Count == 1 ? $"\"{volumes[0].Name}\"" : $"\"{LoreChannelHub.Source.GetLocation(reply.BookLocationId)?.Book?.Title}\" in {volumes.Count} volumes";
                string location = reply.BookLocationId;
                player.Out.SendCustomDialog($"Buy {title} from {Name} for {LoreBooks.PriceFor(volumes.Count)}?", (p, response) =>
                {
                    if (response != 0x01 || !IsWithinRadius(p, WorldMgr.INTERACT_DISTANCE)) return;
                    if (LoreBooks.TryBuy(p, location, out string reason))
                        SayTo(p, "There. Mind the binding, and read it before you go, not after.");
                    else
                        SayTo(p, reason);
                });
            }
            return true;
        }
    }
}
