using System;
using System.Collections.Concurrent;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// What a hired companion says (True 1.65 Mode; owner 2026-10-30: "talks in /say which is fine but make sure they talk
    /// about relevant things"). A companion is not one of the world's wandering bots: it never joins their talk about camps,
    /// destinations or grouping up. Now and then, near its player and out of combat, it says something about what is
    /// actually going on: its own health or power, the fight just won, a level gained, the place they are in, the road
    /// ahead. When its player speaks near it, it answers as a companion would (always when named).
    /// </summary>
    public static class CompanionChat165
    {
        private const long AmbientGapMs = 240_000;      // a companion speaks up at most every four minutes
        private const long ReplyGapMs = 8_000;
        private static readonly ConcurrentDictionary<long, long> NextAmbient = new();
        private static readonly ConcurrentDictionary<long, long> NextReply = new();
        private static readonly ConcurrentDictionary<long, int> LastLevel = new();

        public static bool IsCompanion(GameBot bot) => HiredCompanionsMode.IsOn && HiredCompanions165.IsHired(bot);

        private static GamePlayer Owner(GameBot bot) =>
            bot?.Group?.GetPlayersInTheGroup().FirstOrDefault(p => HiredCompanions165.RosterOf(p).Any(h => h.BotId == bot.DatabaseID));

        private static bool CanSpeak(GameBot bot, GamePlayer owner) =>
            bot != null && owner != null && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && !bot.IsCasting &&
            owner.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(owner, WorldMgr.SAY_DISTANCE);

        /// <summary>Called where the world's bots start their own talk: a companion says something relevant instead.</summary>
        public static bool TryAmbient(GameBot bot)
        {
            long now = GameLoop.GameLoopTime;
            if (bot == null || NextAmbient.TryGetValue(bot.DatabaseID, out long next) && now < next)
                return false;   // checked first: this runs on every think of a following companion
            GamePlayer owner = Owner(bot);
            if (!CanSpeak(bot, owner) || bot.InCombat || owner.InCombat)
            {
                NextAmbient[bot.DatabaseID] = now + 15_000;   // look again in a while
                return false;
            }
            string line = AmbientLine(bot, owner, Random.Shared);
            if (line == null)
                return false;
            NextAmbient[bot.DatabaseID] = now + AmbientGapMs + Random.Shared.Next(120_000);
            return bot.Say(line);
        }

        public static string AmbientLine(GameBot bot, GamePlayer owner, Random random)
        {
            string who = owner.Name;
            int level = bot.Level;
            if (LastLevel.TryGetValue(bot.DatabaseID, out int was) && level > was)
            {
                LastLevel[bot.DatabaseID] = level;
                return random.Next(2) == 0 ? $"I feel stronger, {who}. Level {level} now." : $"Level {level}. I should see my trainer when we are next in town.";
            }
            LastLevel[bot.DatabaseID] = level;
            if (bot.MaxHealth > 0 && bot.Health * 100 / bot.MaxHealth < 50)
                return random.Next(2) == 0 ? "I am hurt. A moment to bind my wounds would help." : $"Give me a moment, {who}. I am not at full strength.";
            if (bot.MaxMana > 0 && bot.Mana * 100 / bot.MaxMana < 30)
                return random.Next(2) == 0 ? "My power is low. Let me sit a moment before the next pull." : "I need to rest my power before we go on.";
            if (bot.InCombatInLast(30_000))
                return random.Next(3) switch
                {
                    0 => "That went well. Ready for the next one.",
                    1 => $"Well fought, {who}.",
                    _ => "I am ready when you are.",
                };
            string zone = bot.CurrentZone?.Description;
            return random.Next(5) switch
            {
                0 when !string.IsNullOrWhiteSpace(zone) => $"Keep your eyes open here in {zone}.",
                1 => $"Lead the way, {who}.",
                2 => "I am right behind you.",
                3 when owner.TargetObject is GameNPC { IsAlive: true } npc && npc.Realm == eRealm.None => $"Shall we take on that {npc.Name}?",
                _ => $"Say the word and I will follow, {who}.",
            };
        }

        /// <summary>A player spoke near their companions: one answers (always the one named), as a companion.</summary>
        public static void OnPlayerSay(GamePlayer player, string message)
        {
            if (!HiredCompanionsMode.IsOn || player?.Group == null || string.IsNullOrWhiteSpace(message))
                return;
            long[] ids = HiredCompanions165.RosterOf(player).Select(h => h.BotId).ToArray();
            GameBot[] near = player.Group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(b => ids.Contains(b.DatabaseID) && CanSpeak(b, player) && !b.InCombat).ToArray();
            if (near.Length == 0)
                return;
            GameBot named = near.FirstOrDefault(b => AutonomousBotChatCoordinator.ContainsMention(message, b.Name));
            if (named == null && Random.Shared.NextDouble() > 0.35)
                return;
            GameBot speaker = named ?? near[Random.Shared.Next(near.Length)];
            long now = GameLoop.GameLoopTime;
            if (NextReply.TryGetValue(speaker.DatabaseID, out long next) && now < next)
                return;
            NextReply[speaker.DatabaseID] = now + ReplyGapMs;
            string reply = Reply(speaker, player, message, Random.Shared);
            _ = new ECSGameTimer(speaker, _ =>
            {
                if (CanSpeak(speaker, player))
                    speaker.Say(reply);
                return 0;
            }, 1_200 + Random.Shared.Next(1_200));
        }

        public static string Reply(GameBot bot, GamePlayer owner, string message, Random random)
        {
            string who = owner.Name;
            if (message.Contains("stop", StringComparison.OrdinalIgnoreCase) || message.Contains("quiet", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("shut", StringComparison.OrdinalIgnoreCase))
                return "As you wish. I will keep quiet.";
            if (message.Contains("how are you", StringComparison.OrdinalIgnoreCase) || message.Contains("you ok", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("health", StringComparison.OrdinalIgnoreCase))
                return bot.MaxHealth > 0 && bot.Health * 100 / bot.MaxHealth < 50 ? "Hurt, but standing. A rest would help."
                    : bot.MaxMana > 0 && bot.Mana * 100 / bot.MaxMana < 30 ? "Fine, but my power is low." : "Ready to fight.";
            return AutonomousChatIntentModel.Predict(message) switch
            {
                eAutonomousChatIntent.Greeting => random.Next(2) == 0 ? $"At your side, {who}." : $"Here, {who}.",
                eAutonomousChatIntent.Farewell => "I will be here when you need me.",
                eAutonomousChatIntent.Travel => random.Next(2) == 0 ? "Lead on. I will follow." : "Wherever you go, I go.",
                eAutonomousChatIntent.Dungeon => "Stay close and we take them one pull at a time.",
                eAutonomousChatIntent.RvR => "If we go to the frontier, I fight beside you.",
                eAutonomousChatIntent.Trading => "Hand me anything you want me to wear. I will put it on.",
                eAutonomousChatIntent.Grinding => "Point me at the next one.",
                eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Help => $"I am with you, {who}. Tell me what you need.",
                eAutonomousChatIntent.Banter => random.Next(2) == 0 ? "Ha! Save that spirit for the next fight." : "You keep me on my toes.",
                eAutonomousChatIntent.Abuse => "Let us save that for our enemies.",
                eAutonomousChatIntent.IgnoreOutOfWorld => "I do not follow, but I am ready when you are.",
                _ => random.Next(3) switch { 0 => "Aye.", 1 => "Understood.", _ => $"As you say, {who}." },
            };
        }
    }
}
