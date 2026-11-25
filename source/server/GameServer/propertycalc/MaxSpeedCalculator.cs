using System;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.Effects;
using DOL.GS.RealmAbilities;
using DOL.GS.ServerRules;

namespace DOL.GS.PropertyCalc
{
    /// <summary>
    /// The Max Speed calculator
    /// BuffBonusCategory1 unused
    /// BuffBonusCategory2 unused
    /// BuffBonusCategory3 unused
    /// BuffBonusCategory4 unused
    /// BuffBonusMultCategory1 used for all multiplicative speed bonuses
    /// </summary>
    [PropertyCalculator(eProperty.MaxSpeed)]
    public class MaxSpeedCalculator : PropertyCalculator
    {
        public const double SPEED1 = 1.44;
        public const double SPEED2 = 1.59;
        public const double SPEED3 = 1.74;
        public const double SPEED4 = 1.89;
        public const double SPEED5 = 2.04;
        private const double SPRINT = 1.3;

        public override int CalcValue(GameLiving living, eProperty property)
        {
            bool hasSpeedOfSound = living.effectListComponent.ContainsEffectForEffectType(eEffect.SpeedOfSound);

            // Hard CC check. Speed of Sound ignores it.
            if (!hasSpeedOfSound && living.IsCrowdControlled)
                return 0;

            double speedIncrease = 1.0;

            // Speed of Sound forces the speed multiplier from buffs and debuffs (roots included), but gives no speed increase
            // while stealthed (port of OpenDAoC-Core 324780db2). Snares, roots and speed buffs stay on and apply once it ends.
            if (!hasSpeedOfSound)
                speedIncrease = living.BuffBonusMultCategory1.Get((int) property);
            else if (!living.IsStealthed)
                speedIncrease = SPEED4;
            double maxSpeedBase = living.MaxSpeedBase;

            if (living is GamePlayer player)
            {
                // Since Dark Age of Camelot's launch, we have heard continuous feedback from our community about the movement speed in our game. The concerns over how slow
                // our movement is has continued to grow as we have added more and more areas in which to travel. Because we believe these concerns are valid, we have decided
                // to make a long requested change to the game, enhancing the movement speed of all players who are out of combat. This new run state allows the player to move
                // faster than normal run speed, provided that the player is not in any form of combat. Along with this change, we have slightly increased the speed of all
                // secondary speed buffs (see below for details). Both of these changes are noticeable but will not impinge upon the supremacy of the primary speed buffs available
                // to the Bard, Skald and Minstrel.
                // - The new run speed does not work if the player is in any form of combat. All combat timers must also be expired.
                // - The new run speed will not stack with any other run speed spell or ability, except for Sprint.
                // - Pets that are not in combat have also received the new run speed, only when they are following, to allow them to keep up with their owners.

                double horseSpeed = player.IsOnHorse ? player.ActiveHorse.Speed * 0.01 : 1.0;

                if (speedIncrease > horseSpeed)
                    horseSpeed = 1.0;

                if (ServerProperties.Properties.ENABLE_PVE_SPEED)
                {
                    // OF zones technically aren't in a RvR region.
                    if (speedIncrease == 1 && !player.InCombat && !player.IsStealthed && !player.CurrentRegion.IsRvR && !player.CurrentZone.IsRvR)
                        speedIncrease *= 1.25; // New run speed is 125% when no buff.
                }

                if (player.IsEncumbered && player.Client.Account.PrivLevel == 1 && ServerProperties.Properties.ENABLE_ENCUMBERANCE_SPEED_LOSS)
                {
                    speedIncrease *= player.MaxSpeedModifierFromEncumbrance;

                    if (speedIncrease <= 0)
                        speedIncrease = 0;
                }

                if (player.IsStealthed && player.Client.Account.PrivLevel == 1)
                {
                    //GameSpellEffect bloodrage = SpellHandler.FindEffectOnTarget(player, "BloodRage");
                    //VanishEffect vanish = player.EffectList.GetOfType<VanishEffect>();
                    //if (vanish != null)
                    //    speed *= vanish.SpeedBonus;
                    //if (bloodrage != null)
                    //    speed *= 1 + (bloodrage.Spell.Value * 0.01); // 25 * 0.01 = 0.25 (a.k 25%) value should be 25.5

                    // #521/#522: one formula for the player and the gamebots (StealthRules.SpeedFactor, PvpRules.StealthSpeedFactor).
                    speedIncrease *= StealthRules.SpeedFactor(player);
                }

                if (GameRelic.IsPlayerCarryingRelic(player))
                {
                    if (speedIncrease > 1.0)
                        speedIncrease = 1.0;

                    horseSpeed = 1.0;
                }

                if (player.IsSprinting)
                    speedIncrease *= SPRINT;

                speedIncrease *= horseSpeed;
            }
            else if (living is GameNPC npc)
            {
                // Special handling for pets.
                if (npc.Brain is IControlledBrain brain)
                {
                    GameLiving owner = brain.Owner;

                    if (owner != null && owner == brain.Body.FollowTarget)
                    {
                        // Increase the pet's speed to match its owner's (using mount speed if applicable) when both are out of combat
                        if (!living.InCombat && !owner.InCombat)
                        {
                            GamePlayer playerOwner = brain.GetPlayerOwner();

                            if (playerOwner != null)
                            {
                                GameNPC steed = playerOwner.Steed;

                                if (steed != null)
                                    owner = steed;
                            }

                            // Only increase the pet's speed, never decrease it, so take the max of the two.
                            // Overwrite whatever speed bonuses the pet has at this point.
                            maxSpeedBase = Math.Max(maxSpeedBase, owner.MaxSpeedBase);
                            speedIncrease = Math.Max(1, (double) owner.MaxSpeed / owner.MaxSpeedBase);
                        }

                        // A pet following its owner moves at sprint speed (+30%),
                        // even in combat (This behavior has been confirmed on Uthgard for Necromancer and Cabalist pets),
                        // even if the catch up mechanic applies (to ensure it stays within the catch up radius).
                        speedIncrease *= SPRINT;
                    }
                }

                // #521: an autonomous gamebot in RvR moves by the player's speed rules: stealth slows it, sprint is +30%, and
                // low health does not slow it. Everywhere else (PvE), and for every other NPC, today's rule.
                bool botPlayerRules = npc is GameBot && PvpRules.TakesPlayerRules(npc);

                if (botPlayerRules && npc is GameBot bot)
                {
                    // #522: the same stealth rule as the player's (StealthRules.SpeedFactor).
                    speedIncrease *= StealthRules.SpeedFactor(bot);

                    speedIncrease *= PvpRules.SprintSpeedFactor(true, bot.IsSprinting);
                }

                double healthPercent = living.Health / (double) living.MaxHealth;

                // 33% HP = full speed, 0% HP = 20% speed (monsters only)
                speedIncrease *= PvpRules.LowHealthSpeedFactor(botPlayerRules, healthPercent);
            }

            if (living is GameBot && GameRelic.IsPlayerCarryingRelic(living))
                speedIncrease = Math.Min(speedIncrease, 1.0);
            return (int) Math.Round(maxSpeedBase * speedIncrease);
        }
    }
}
