using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Hall of the Corrupt's raid rules (1.2 big-boss plans, owner 2026-11-14; tests only). Aidon the Archwizard's illusions
    /// and his fight at home are catalog tactics (hand-notes "aiTactics"). Code rule here: Lord Gildas.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        [DungeonRules]
        private static IEnumerable<DungeonRule> HallOfTheCorruptRules() =>
        [
            new(nameof(LordGildas), [nameof(LordGildas)], c => MeleeFromBehind(c, "gildas-behind"))
            {
                Summary = "He slams his target (a stun) and, while it is stunned, steps behind it for a two-handed back style at " +
                          "Strength 400: the nearest other tank taunts him off a stunned target; the melee fight him from behind " +
                          "(he blocks 75-80% and parries in front, 60% with his polearm under 50%).",
                Act = GildasTankTakesOver,
            },
        ];

        /// <summary>Tanks within this of Gildas that fight him are asked; the nearest taunts.</summary>
        public const int GildasTauntReach = 1_500;

        private static readonly Dictionary<GameNPC, (long Tick, GameLiving Tank)> GildasTauntPicks = new();

        /// <summary>
        /// Which raider takes Gildas over (pure): when his target is stunned, the nearest of <paramref name="tanks"/> (tanks
        /// fighting him) that is not that target and not stunned itself; null when his target is not stunned or none is left.
        /// </summary>
        public static GameLiving TakeOverFromStunned(GameLiving bossTarget, bool targetStunned,
            IEnumerable<(GameLiving Tank, float Distance, bool Stunned)> tanks) =>
            bossTarget == null || !targetStunned ? null
                : tanks.Where(t => t.Tank != null && t.Tank != bossTarget && !t.Stunned).OrderBy(t => t.Distance).Select(t => t.Tank)
                    .FirstOrDefault();

        /// <summary>
        /// Lord Gildas (Catacombs, Hall of the Corrupt; no period guide found for him: the plan is the script's). In his first
        /// stage he slams his target (a stun) whenever it has no stun and no stun immunity, and while it is stunned he moves
        /// behind it and back-styles with his two-hander at Strength 400 (LordGildasBrain.WalkBack). So the moment his target
        /// is stunned, the nearest other raid tank fighting him taunts him (taunt spell where its class has one, else its taunt
        /// style): his next swings go to a tank on its feet. Chosen once a second for the whole raid.
        /// </summary>
        private static void GildasTankTakesOver(GameBot bot, GameNPC gildas, IReadOnlyList<GameNPC> live)
        {
            if (bot == null || gildas is not LordGildas || !IsTankLiving(bot) || gildas.TargetObject is not GameLiving target) return;
            long now = GameLoop.GameLoopTime;
            GameLiving pick;
            lock (GildasTauntPicks)
            {
                if (GildasTauntPicks.TryGetValue(gildas, out var hit) && now - hit.Tick < 1_000) pick = hit.Tank;
                else
                {
                    pick = TakeOverFromStunned(target, target.IsStunned,
                        (live ?? []).OfType<GameBot>().Where(b => b.IsAlive && b.IsAutonomousWorldBot && b.Realm == bot.Realm &&
                                                                  b.TargetObject == gildas && IsTankLiving(b) && gildas.IsWithinRadius(b, GildasTauntReach))
                            .Select(b => ((GameLiving)b, (float)gildas.GetDistanceTo(b), b.IsStunned)));
                    if (GildasTauntPicks.Count > 32) GildasTauntPicks.Clear();
                    GildasTauntPicks[gildas] = (now, pick);
                }
            }
            if (pick == bot && bot.Brain is BotBrain brain) LogTaunt(bot, gildas, brain.TauntForEncounter(gildas));
        }
    }
}
