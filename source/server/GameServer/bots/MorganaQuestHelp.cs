using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// #611 (owner decision 2026-11-25): bots help a player through the Albion level 50 epic quest fight (Academy_50, "Symbol
    /// of the Broken"): Morgana's two demons, Bechard and Silcharde, at the fallen tower in Lyonesse. Morgana stays as she
    /// is: no raid, never attacked ("Do not attack Morgana. She will not do anything during this attack": Allakhazam's
    /// walkthrough, quoted in Academy50.cs). The event still starts only for a player on the quest's step
    /// (MorganaBrain.Think looks at GamePlayers only, and a bot is a GameNPC).
    ///
    /// When it starts, every bot in the quest player's own group (a /spawn helper, a hired companion or an autonomous
    /// gamebot grouped with the player) within <see cref="HelpRadius"/> assists on the nearer demon through
    /// <see cref="BotBrain.AssistPlayerAttack"/>, so the usual rules hold: support bots hold and heal, a defensive companion
    /// stays within 350 of its player, a passive bot stays out. The demons pull each other into the fight (their brains link),
    /// and their minion waves come in through group defense as before. Solo gamebots and other groups never join. Nothing
    /// about Morgana or the demons changes. Switch: bot_morgana_quest_help (on). Log: MORGANA_QUEST_HELP.
    /// </summary>
    public static class MorganaQuestHelp
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(MorganaQuestHelp));

        /// <summary>How far from the quest player a groupmate bot joins (the group-defense radius).</summary>
        public const int HelpRadius = BotBrain.GROUP_DEFENSE_ASSIST_RADIUS;

        /// <summary>Morgana herself: never a bot's target, whatever her flags or the switch.</summary>
        public static bool IsMorgana(GameObject target) => target is Morgana || target is GameNPC { Brain: MorganaBrain };

        /// <summary>One of her two named demons.</summary>
        public static bool IsDemon(GameObject target) =>
            target is Bechard or Silcharde || target is GameNPC { Brain: BechardBrain or SilchardeBrain };

        /// <summary>The bots in the quest player's own group that join: alive, in the world, in the player's region and within
        /// <see cref="HelpRadius"/>, not on a stable ride. Companions and grouped gamebots alike; nobody outside the group.</summary>
        public static List<GameBot> Helpers(GamePlayer questPlayer)
        {
            var helpers = new List<GameBot>();
            Group group = questPlayer?.Group;
            if (group == null)
                return helpers;
            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                if (member is GameBot bot && bot.Group == group && bot.IsAlive &&
                    bot.ObjectState == GameObject.eObjectState.Active && !bot.IsOnStableMasterRoute &&
                    bot.CurrentRegionID == questPlayer.CurrentRegionID && bot.IsWithinRadius(questPlayer, HelpRadius) &&
                    bot.Brain is BotBrain)
                    helpers.Add(bot);
            }
            return helpers;
        }

        /// <summary>The demon the group opens on: the live one nearest the quest player.</summary>
        public static GameNPC FirstDemon(GamePlayer questPlayer, IEnumerable<GameNPC> demons)
        {
            if (questPlayer == null || demons == null)
                return null;
            return demons
                .Where(d => IsDemon(d) && d.IsAlive && d.ObjectState == GameObject.eObjectState.Active &&
                            d.CurrentRegionID == questPlayer.CurrentRegionID)
                .OrderBy(d => DistanceSquared(questPlayer, d))
                .FirstOrDefault();
        }

        /// <summary>
        /// The quest player's event started and these demons are up: each helper assists on the first demon. Returns how many
        /// bots were asked. Nothing with the switch off, no quest player, or no live demon.
        /// </summary>
        public static int Engage(GamePlayer questPlayer, IEnumerable<GameNPC> demons)
        {
            if (!Properties.BOT_MORGANA_QUEST_HELP || questPlayer == null)
                return 0;
            GameNPC demon = FirstDemon(questPlayer, demons);
            if (demon == null)
                return 0;
            List<GameBot> helpers = Helpers(questPlayer);
            int engaged = 0, support = 0;
            foreach (GameBot bot in helpers)
            {
                BotBrain brain = (BotBrain)bot.Brain;
                brain.AssistPlayerAttack(demon);
                if (BotPartyRoles.IsSupport(bot))
                    support++;
                else if (brain.HasAggro)
                    engaged++;
            }
            Log.Info($"MORGANA_QUEST_HELP region={questPlayer.CurrentRegionID} demon={demon.Name} helpers={helpers.Count} " +
                     $"engaged={engaged} support={support} companions={helpers.Count(b => !b.IsAutonomousWorldBot || b.IsHiredCompanion || b.IsTemporaryGroupHelper)} " +
                     $"gamebots={helpers.Count(b => b.IsAutonomousWorldBot && !b.IsHiredCompanion && !b.IsTemporaryGroupHelper)}");
            return helpers.Count;
        }

        /// <summary>From MorganaBrain.Think, right after it spawned (or found) <paramref name="demons"/> for
        /// <paramref name="questPlayer"/>. They are passed in: a new NPC joins its subzone only at the zone service's next
        /// tick, so a radius search in the same think would not see it. Never throws into the encounter.</summary>
        public static int OnEventStarted(GamePlayer questPlayer, IEnumerable<GameNPC> demons)
        {
            if (questPlayer == null || !Properties.BOT_MORGANA_QUEST_HELP)
                return 0;
            try
            {
                return Engage(questPlayer, demons);
            }
            catch (Exception e)
            {
                Log.Error("MORGANA_QUEST_HELP failed", e);
                return 0;
            }
        }

        private static long DistanceSquared(GameObject a, GameObject b)
        {
            long dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }
}
