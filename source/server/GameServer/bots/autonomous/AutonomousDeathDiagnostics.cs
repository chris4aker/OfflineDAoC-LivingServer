using System;
using System.Linq;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// One server-log line per autonomous gamebot death (AUTONOMOUS_BOT_DEATH): what it was doing, what killed it and
    /// how many were on it. The overnight log of 2026-10-19 had 3,553 deaths and nothing that named a killer, so the
    /// cause of the half that happen on the way to a camp could only be guessed.
    /// </summary>
    public static class AutonomousDeathDiagnostics
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousDeathDiagnostics));

        /// <summary>The killer's kind: a monster, a pet, an enemy bot, a real player or something else.</summary>
        public static string KillerKind(GameObject killer) => killer switch
        {
            null => "none",
            GamePlayer => "player",
            GameBot => "bot",
            GameNPC { Brain: DOL.AI.Brain.IControlledBrain } => "pet",
            GameNPC => "monster",
            _ => killer.GetType().Name,
        };

        /// <summary>
        /// The killer as a death is told in chat (1.1.3, after RC6): "guard" for a keep or town guard, "pet" for the pet of a
        /// player or a bot, "monster" for a monster or a pet that is no one's (a charmed or summoned creature without a player or
        /// bot owner), else <see cref="KillerKind"/> ("bot", "player", "none").
        /// </summary>
        public static string ChatKillerKind(GameObject killer) => killer switch
        {
            // RC7 on the owner's PC: a gamebot's brain is a controlled brain with no owner, so an enemy bot read as a monster
            // ("adds in murdaigean hate me" after one enemy friar). A bot is a bot before anything else.
            GameBot => "bot",
            DOL.GS.Keeps.GameKeepGuard or GameGuard => "guard",
            GameNPC { Brain: DOL.AI.Brain.IControlledBrain brain } => brain.GetLivingOwner() is GamePlayer or GameBot ? "pet" : "monster",
            _ => KillerKind(killer),
        };

        /// <summary>The class of the enemy behind a kill (the player or bot, or a pet's owner), or null.</summary>
        public static string KillerClassName(GameObject killer)
        {
            GameLiving who = killer is not GameBot && killer is GameNPC { Brain: DOL.AI.Brain.IControlledBrain brain } ? brain.GetLivingOwner() : killer as GameLiving;
            return who switch
            {
                GamePlayer player => player.CharacterClass?.Name,
                GameBot bot => bot.CharacterClass?.Name ?? bot.ClassName,
                _ => null,
            };
        }

        /// <summary>How many were on the bot (its attacker tracker), read before the death clears it.</summary>
        public static int Attackers(GameBot bot)
        {
            try
            {
                return bot?.attackComponent?.AttackerTracker?.Attackers?.Count() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        public static void Record(GameBot bot, GameObject killer, string activityBefore)
        {
            if (bot?.IsAutonomousWorldBot != true)
                return;
            // 1.2.3 (#537): the killer and its spot, by con (purple, or red to a bot alone); the controller reads
            // LastAutonomousKiller to blame the killer, not the camp it was heading to.
            bool alone = bot.Group == null || bot.Group.MemberCount <= 1;
            if (killer is GameLiving deadly)
            {
                bot.LastAutonomousKiller = new AutonomousDeathBlame.Killer(deadly.Name, deadly.Level, KillerKind(killer),
                    bot.CurrentRegionID, bot.X, bot.Y);
                if (AutonomousPveBossDanger.Marks(KillerKind(killer), deadly.Level, bot.Level, alone) &&
                    AutonomousPveBossDanger.Record(bot.CurrentRegionID, bot.X, bot.Y, deadly.Level, DateTime.UtcNow, deadly.Name) &&
                    Log.IsInfoEnabled)
                    Log.Info($"AUTONOMOUS_KILLER_DANGER_SPOT killer=\"{deadly.Name}\" killerLevel={deadly.Level} " +
                             $"victim=\"{bot.Name}\" victimLevel={bot.Level} con={AutonomousPveBossDanger.ConTo(bot.Level, deadly.Level)} " +
                             $"alone={alone} region={bot.CurrentRegionID} at={bot.X},{bot.Y} spots={AutonomousPveBossDanger.Count}");
            }
            else
                bot.LastAutonomousKiller = null;
            if (string.Equals(KillerKind(killer), "monster", StringComparison.Ordinal))
            {
                AutonomousRepeatDeathSpots.Record(bot.DatabaseID, bot.CurrentRegionID, bot.X, bot.Y, DateTime.UtcNow);
                // #569 step 2: a party losing members to monsters far above them near one spot leaves it
                if (bot.Group != null && killer is GameLiving partyKiller)
                    AutonomousBotGroupCoordinator.NoteMemberDeath(bot, partyKiller);
            }
            if (BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID))
            {
                AutonomousBattlegroundWave.NoteDeath(bot.CurrentRegionID, bot.Realm, killer is DOL.GS.Keeps.GameKeepGuard);
                AutonomousBattlegroundKeepRally.NoteDeath(bot, killer);
                // #572 step 4: three deaths inside with nothing gained between take the bot out (the watchdog acts).
                AutonomousBattlegroundExit.NoteDeath(bot.DatabaseID, bot.CurrentRegionID);
            }
            if (!Log.IsInfoEnabled)
                return;
            try
            {
                GameLiving living = killer as GameLiving;
                int attackers = Attackers(bot);
                string con = living != null ? ConLevels.GetConColor(bot.GetConLevel(living)).ToString() : "-";
                bool arrived = bot.GoalDiagnosticAttempt?.ArrivedUtc != null;
                int armor = bot.Inventory?.EquippedItems?.Count(item => item != null && item.Object_Type >= (int)eObjectType._FirstArmor &&
                    item.Object_Type <= (int)eObjectType._LastArmor) ?? -1;
                Log.Info($"AUTONOMOUS_BOT_DEATH bot=\"{bot.Name}\" id={bot.DatabaseID} level={bot.Level} class=\"{bot.ClassName}\" " +
                         $"realm={GlobalConstants.RealmToName(bot.Realm)} group={(bot.Group?.MemberCount ?? 1)} " +
                         $"killer=\"{killer?.Name}\" killerKind={KillerKind(killer)} killerLevel={living?.Level ?? 0} con={con} " +
                         $"attackers={attackers} armorPieces={armor} maxHealth={bot.MaxHealth} arrivedAtCamp={arrived} " +
                         $"activity=\"{activityBefore}\" goal=\"{bot.PersistentRecord?.CurrentGoal}\" " +
                         $"region={bot.CurrentRegionID} zone=\"{bot.CurrentZone?.Description}\" at={bot.X},{bot.Y},{bot.Z}");
            }
            catch (Exception exception)
            {
                Log.Warn("AUTONOMOUS_BOT_DEATH could not be written", exception);
            }
        }
    }
}
