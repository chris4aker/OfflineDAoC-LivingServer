using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// The open-world big bosses' raid rules (Living Server 1.2, owner 2026-11-14: every big boss is in, as close to the
    /// original as possible; RealmRaidOpenWorld gives each its realm raid). Most of their plans are catalog rows (the hand
    /// notes' aiTactics): Evern's fairies and the Green Knight's trees as ranged adds, the area hits around a boss's target
    /// as spreads (the Green Knight 500, Marfoirwort 350, Mortufoghus 1,000), Curengkur's nest as a hazard, Myrddraxis's heads
    /// in the period's order. The rules a catalog field can't say are here:
    /// <list type="bullet">
    /// <item>The Glacier Giant throws a raider within 2,000 of him who is not his target, every few seconds
    /// (GlacierGiantBrain.TeleportPlayer: 20% each 1.5 s turn, to one of six spots up to 2,000 out of 15,000 away and 300-600
    /// up). The thrown raider walks back and the healers heal it first (EncounterDisplacementRecovery). Here: the healers
    /// outside his target's party stand 2,060 from him on his target's side, out of the throw's reach and within 1,975 of
    /// the one he fights, so the throws take the melee and casters and never the healers who keep his tank up.</item>
    /// <item>Curengkur heals 200 every 1.5 s within 800 of his spawn, where his nest burns everyone within 800 every 4 s
    /// (CurengkurBrain.Think, CurengkurNestBrain). The period: "Pulling it out of the nest stops the regen" (Allakhazam,
    /// Curengkur, 2005). Here: the raider he fights leads him 1,500 out from his spawn while he is within 1,000 of it; the
    /// catalog's hazard keeps everyone else out of the nest's 800.</item>
    /// <item>The Spirit of Lord Emthoro takes no damage from an attacker more than 440 from his spawn
    /// (SpiritOfLordEmthoro.TakeDamage). The period: "Theurgist pets only worked when summoned inside the pit" (Allakhazam,
    /// Mar 2004). Here: every raider, casters too, fights from within 360 of his spawn.</item>
    /// </list>
    /// Autonomous gamebots only, as every tactic. Tests only: none of this was seen on the owner's PC.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>The Glacier Giant throws only raiders within this of him (GlacierGiantBrain.TeleportPlayer).</summary>
        public const int GlacierThrowReach = 2_000;
        /// <summary>His healers stand this far from him, and within <see cref="GlacierHealerReach"/> of the one he fights (heals
        /// reach 2,000: from 2,060 out they reach a tank 100-350 from him on any of the three spots).</summary>
        public const int GlacierHealerStand = 2_060, GlacierHealerReach = 1_975;
        /// <summary>A healer this far past his reach and within its reach of his target stays where it is.</summary>
        public const int GlacierHealerSlack = 50;
        /// <summary>The healers fan out this many degrees either side of the line to his target (three spots).</summary>
        public const float GlacierHealerFanDegrees = 20;

        /// <summary>Curengkur's regeneration and his nest's burn reach this far from his spawn.</summary>
        public const int CurengkurNestRadius = 800;
        /// <summary>While he is within this of his spawn, the raider he fights leads him to <see cref="CurengkurLeadOut"/> from it.</summary>
        public const int CurengkurLeadWhileWithin = 1_000, CurengkurLeadOut = 1_500, CurengkurLeadSlack = 50;

        /// <summary>The Spirit of Lord Emthoro takes damage only from attackers within this of his spawn.</summary>
        public const int EmthoroReach = 440;

        [DungeonRules]
        private static IEnumerable<DungeonRule> OpenWorldRules() =>
        [
            new(nameof(GlacierGiant), [nameof(GlacierGiant)], GlacierGiantHealers)
            {
                Summary = "His throw takes any raider within 2,000 but his target: the healers outside his target's party stand 2,060 " +
                          "from him on his target's side, within 1,975 of it; the thrown walk back and are healed first.",
            },
            new(nameof(Curengkur), [nameof(Curengkur)], CurengkurNestPull)
            {
                Summary = "He heals within 800 of his spawn, where his nest burns: the raider he fights leads him 1,500 out while he " +
                          "is within 1,000 of it, and nobody stands in the nest's 800.",
            },
            new(nameof(SpiritOfLordEmthoro), [nameof(SpiritOfLordEmthoro)], c =>
                CloseIn(c.At, SpawnOf(c.Target), EmthoroReach) is Vector3 close
                    ? new(BotAction.Reposition, c.Target, close, $"close in: he takes damage only from within {EmthoroReach} of his spawn", "epic_close_in")
                    : null)
            {
                Summary = "He takes damage only from attackers within 440 of his spawn: everyone, casters too, fights from within 360 of it.",
            },
        ];

        private static Advice? GlacierGiantHealers(RuleContext c)
        {
            if (!c.Target.InCombat || c.Holds || c.ClassId <= 0 || !BotPartyRoles.IsHealingClass((eCharacterClass)c.ClassId) ||
                c.Target.TargetObject is not GameLiving tank || tank == c.Raider)
                return null;
            return GlacierHealerSpot(c.At, PosOf(c.Target), PosOf(tank), c.Raider.ObjectID) is Vector3 spot
                ? new(BotAction.Reposition, c.Target, spot,
                    $"throw: healers out of his {GlacierThrowReach}, within {GlacierHealerReach} of \"{tank.Name}\"")
                : null;
        }

        /// <summary>
        /// Where a healer of the Glacier Giant's raid stands (pure): <see cref="GlacierHealerStand"/> from him on the side of
        /// the one he fights, turned by up to <see cref="GlacierHealerFanDegrees"/> by <paramref name="fan"/> (the raider's
        /// id: three spots, so a raid's healers don't all stand on one). Null when it already stands past his reach and within
        /// <see cref="GlacierHealerReach"/> of his target, or when no spot is in reach of his target (he fights someone far
        /// from him): then it stays and heals as usual.
        /// </summary>
        public static Vector3? GlacierHealerSpot(Vector3 at, Vector3 giant, Vector3 target, long fan)
        {
            if (Flat(at, giant) > GlacierThrowReach + GlacierHealerSlack && Flat(at, target) <= GlacierHealerReach)
                return null;
            Vector2 toTarget = new(target.X - giant.X, target.Y - giant.Y), own = new(at.X - giant.X, at.Y - giant.Y);
            Vector2 dir = toTarget.Length() >= 1 ? Vector2.Normalize(toTarget) : own.Length() >= 1 ? Vector2.Normalize(own) : new Vector2(1, 0);
            double turn = ((fan % 3 + 3) % 3 - 1) * GlacierHealerFanDegrees * Math.PI / 180;
            var fanned = new Vector2((float)(dir.X * Math.Cos(turn) - dir.Y * Math.Sin(turn)), (float)(dir.X * Math.Sin(turn) + dir.Y * Math.Cos(turn)));
            Vector2 p = new Vector2(giant.X, giant.Y) + fanned * GlacierHealerStand;
            var spot = new Vector3(p.X, p.Y, at.Z);
            return Flat(spot, target) <= GlacierHealerReach ? spot : null;
        }

        private static Advice? CurengkurNestPull(RuleContext c)
        {
            if (!c.Target.InCombat || c.Target.TargetObject != c.Raider) return null;
            return CurengkurLeadSpot(c.At, PosOf(c.Target), SpawnOf(c.Target)) is Vector3 spot
                ? new(BotAction.Reposition, c.Target, spot, $"nest: lead him {CurengkurLeadOut} out of his spawn (he heals within {CurengkurNestRadius})")
                : null;
        }

        /// <summary>
        /// Where the raider Curengkur fights leads him (pure): while he stands within <see cref="CurengkurLeadWhileWithin"/> of
        /// his spawn and the raider is not yet <see cref="CurengkurLeadOut"/> out, the point that far from his spawn on the
        /// raider's side; null otherwise (he is out, or following it there).
        /// </summary>
        public static Vector3? CurengkurLeadSpot(Vector3 raider, Vector3 boss, Vector3 spawn)
        {
            if (Flat(boss, spawn) > CurengkurLeadWhileWithin || Flat(raider, spawn) >= CurengkurLeadOut - CurengkurLeadSlack) return null;
            return PointAway(raider, spawn, CurengkurLeadOut);
        }
    }
}
