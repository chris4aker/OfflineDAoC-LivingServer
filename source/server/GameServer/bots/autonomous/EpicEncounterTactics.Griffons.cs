using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Tuscaran Glacier's griffons, Torst and Hurika, with their original flying patrol on (epic wave 3, 1.2; the switch
    /// AllowGlacierGriffonPatrol, off as shipped: see <see cref="GriffonPatrolAdvice"/>). Out of combat each flies a fixed
    /// loop 0-1,800 above the glacier floor; once a raider is on its aggro list it stops the loop, flies at its target and
    /// lands when it is in reach (TorstBrain/HurikaBrain.Think: Flags = 0). Nobody walks up to it, so the raid pulls it down:
    /// each party waits under its own point of the loop, its casters and archers shoot the griffon when it passes within
    /// reach (the first hit puts the shooter on its aggro list), and the melee beside them fight it where it lands.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>One griffon's loop (TorstFlyingPath / HurikaFlyingPath, in order) and where it spawns.</summary>
        public sealed record GriffonPatrol(string Boss, Vector3 Spawn, Vector3[] Loop);

        public static readonly GriffonPatrol[] GriffonPatrols =
        [
            new("Torst", new(50897, 36006, 16659),
            [
                new(51166, 37442, 17331), new(53201, 39956, 16314), new(55178, 38616, 17901),
                new(54852, 36185, 17859), new(53701, 35635, 17859), new(52118, 36114, 17265),
            ]),
            new("Hurika", new(50898, 35598, 16164),
            [
                new(54652, 36348, 18279), new(55113, 38549, 16679), new(53370, 40527, 16268),
                new(51711, 38978, 17130), new(51519, 37213, 17046),
            ]),
        ];

        /// <summary>A caster or archer shoots the flying griffon from this close (spells and bows reach 1,500; the griffon
        /// moves, so a margin).</summary>
        public const int GriffonPullReach = 1_350;
        /// <summary>A raider this close (flat) to its party's point under the loop is there.</summary>
        public const int GriffonStationArrive = 250;

        [DungeonRules]
        private static IEnumerable<DungeonRule> GriffonRules() =>
        [
            new("Torst", ["Torst", "Hurika"], GriffonPullDown)
            {
                Summary = "With their flying patrol on: each party waits under its point of the loop; casters and archers shoot " +
                          "the griffon as it passes (it comes down at the shooter), melee fight it where it lands.",
            },
        ];

        public static GriffonPatrol PatrolOf(GameNPC griffon)
        {
            if (griffon == null) return null;
            foreach (GriffonPatrol p in GriffonPatrols)
                for (System.Type t = griffon.GetType(); t != null && t != typeof(GameNPC); t = t.BaseType)
                    if (t.Name == p.Boss) return p;
            return null;
        }

        private static Advice? GriffonPullDown(RuleContext c)
        {
            if (PatrolOf(c.Target) is not { } patrol || !EpicEncounterMechanics.AllowGlacierGriffonPatrolFor(c.Target)) return null;
            bool flying = (c.Target.Flags & GameNPC.eFlags.FLYING) != 0;
            return GriffonPatrolAdvice(c.At, PosOf(c.Target), c.Target, flying, patrol, c.PartyIndex, c.CanShootFlyers);
        }

        /// <summary>
        /// The pull (pure but for the advice's target): null once the griffon has landed (the fight as usual). While it flies,
        /// a raider who can shoot it does so from within <see cref="GriffonPullReach"/> (3D); otherwise it goes to its party's
        /// point under the loop and waits there for it to pass. Melee wait at the same point (where the griffon lands on the
        /// shooter of their party). Off as shipped (owner, epic_encounter_switches "AllowGlacierGriffonPatrol@Torst=True;
        /// AllowGlacierGriffonPatrol@Hurika=True"): not played in game, and the floor under the loop was never checked against
        /// the navmesh, so a raid could stand where the griffon never passes in reach and leave him unpulled.
        /// </summary>
        public static Advice? GriffonPatrolAdvice(Vector3 raider, Vector3 griffon, GameNPC boss, bool flying, GriffonPatrol patrol,
            int partyIndex, bool canShoot)
        {
            if (!flying || patrol == null) return null;
            if (canShoot && Vector3.Distance(raider, griffon) <= GriffonPullReach)
                return new(BotAction.None, boss, null, "griffon-pull: shoot it down as it flies past", "epic_griffon_pull");
            Vector3 point = patrol.Loop[AssignedIndex(partyIndex, patrol.Loop.Length)];
            // Under the loop's point at the raider's own height: the walk finds the floor there.
            var station = new Vector3(point.X, point.Y, raider.Z);
            // A shooter keeps the griffon as its target while it waits (a stand-off would drop it, and it would not shoot
            // when the griffon passes); melee drop it and are called back by the fight when it lands.
            if (canShoot)
                return new(BotAction.Reposition, boss, station, Flat(raider, station) > GriffonStationArrive
                    ? "griffon-pull: wait under the patrol for it to pass" : "griffon-pull: under the patrol, waiting for it in reach");
            return new(BotAction.StandOff, boss, Flat(raider, station) > GriffonStationArrive ? station : null,
                "griffon-pull: melee wait under the patrol for it to land");
        }
    }
}
