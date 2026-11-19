using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Hibernia's raid rules (1.2, epic wave 2): Fomor and Marfach Caverns. Myrddraxis's breaths at health thresholds and the
    /// Black Lady's Void Strike around her target are catalog tactics (blasts, spread). Code rules here:
    /// <list type="bullet">
    /// <item>Krackenschtein takes damage only from within 300 of him: everyone fights from close by.</item>
    /// <item>Avartack the Champion swings his back style at a target that turns its back on him: the raider he fights faces
    /// him.</item>
    /// </list>
    /// Fomor City has no raid of its own (its navmesh has no way in, AutonomousRealmRaid.RaidDungeons), so these steer the
    /// autonomous gamebots that fight there; companion bots fight as their player orders.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        public const int KrackenschteinReach = 300;
        /// <summary>Avartack's back style lands when he stands 160-200 degrees off his target's facing; his target turns to
        /// him once he is this far off it.</summary>
        public const float AvartackTurnAngle = 120;

        [DungeonRules]
        private static IEnumerable<DungeonRule> HiberniaRules() =>
        [
            new("Krackenschtein", ["Krackenschtein"], c =>
                CloseIn(c.At, PosOf(c.Target), KrackenschteinReach) is Vector3 close
                    ? new(BotAction.Reposition, c.Target, close, $"close in: he takes damage only within {KrackenschteinReach}", "epic_close_in")
                    : default)
            {
                Summary = "He takes damage only from within 300: everyone fights from close by.",
            },
            new("Avartack", ["Avartack"], null)
            {
                Summary = "The raider he fights faces him (his back style lands on a turned back).",
                Act = (bot, boss, _) =>
                {
                    if (boss.TargetObject == bot && !bot.IsCasting && TurnedAway(bot.GetAngle(boss)))
                    {
                        bot.TurnTo(boss);
                        MechanicCall(bot, "epic_face_him", boss, bot);
                    }
                },
            },
        ];

        /// <summary>The angle of the boss off the raider's facing (0-360, 180 = straight behind) shows the raider's back to it.</summary>
        public static bool TurnedAway(float angle) => angle >= AvartackTurnAngle && angle <= 360 - AvartackTurnAngle;
    }
}
