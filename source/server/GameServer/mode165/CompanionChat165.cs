using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS
{
    /// <summary>
    /// What a hired companion says of its own (True 1.65 Mode; owner 2026-10-30: "talks in /say which is fine but make sure
    /// they talk about relevant things"). Now and then, near its player and out of combat, it says something about what is
    /// actually going on: its own health or power, the fight just won, a level gained, the place they are in, the road
    /// ahead. When its player speaks near it, it answers (always when named).
    ///
    /// One voice (owner 2026-11-18 09:14: "Companions should have all the same chat framework and structure as any bot you
    /// interact or group with, if not more"): a companion also has the gamebots' grats, memory, group talk, fight calls, rez
    /// thanks and tells, so its own lines here are written as a 2003 player would type them and sent in its own writing
    /// style (AutonomousBotVoices.StyleFor), like every other line it says. The True 1.65 formal register ("Lead the way,
    /// Testrunner.") is kept as the companion's Speech option (CompanionOptions165, "Formal"); a formal companion keeps to
    /// these lines and says none of the gamebots' (AutonomousLivingServer.IsSpeaker), so it too has one voice.
    /// </summary>
    public static class CompanionChat165
    {
        private const long AmbientGapMs = 240_000;      // a companion speaks up at most every four minutes
        private const long ReplyGapMs = 8_000;
        private static readonly ConcurrentDictionary<long, long> NextAmbient = new();
        private static readonly ConcurrentDictionary<long, long> NextReply = new();
        private static readonly ConcurrentDictionary<long, int> LastLevel = new();
        private static readonly ConcurrentDictionary<long, string> LastAmbientLine = new();
        // Companions told to keep quiet: no ambient lines, and an answer only when named, until told to talk again.
        private static readonly ConcurrentDictionary<long, bool> Quiet = new();

        // Whole words only (chat audit TEXT-006: the old substring test took "don't stop", "shut the gate" and "healthy").
        private static readonly Regex QuietWords = new(@"\b(be quiet|quiet|shut up|stop talking|silence|hush)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex SpeakWords = new(@"\b(speak|you can talk|u can talk|talk again|talk to me)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HealthWords = new(@"\b(how are you|how r u|you ok|u ok|your health)\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static bool IsQuiet(long botId) => Quiet.ContainsKey(botId);

        /// <summary>A hired companion whose player set its Speech option to Formal (the True 1.65 companion).</summary>
        public static bool IsFormal(GameBot bot) => bot?.IsHiredCompanion == true && CompanionOptions165.Choice(bot, "speech") == 1;

        /// <summary>Tests: forget who was told to keep quiet and the last ambient lines.</summary>
        internal static void ClearForTests()
        {
            Quiet.Clear();
            LastAmbientLine.Clear();
            LastLevel.Clear();
            NextAmbient.Clear();
            NextReply.Clear();
        }

        /// <summary>The player's own live hired companion by name, for a /send to it (TEXT-009), else null.</summary>
        public static GameBot OwnCompanionNamed(GamePlayer player, string name) =>
            player == null || string.IsNullOrWhiteSpace(name) ? null
                : HiredCompanions165.RosterOf(player).Select(h => HiredCompanions165.LiveBot(h.BotId))
                    .FirstOrDefault(bot => bot != null && string.Equals(bot.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// A /send to one's own companion: answered by tell as any gamebot answers one (AutonomousBotChatCoordinator.OnPlayerTell),
        /// or with its own formal answer when its Speech option is Formal.
        /// </summary>
        public static void OnPlayerTell(GamePlayer player, GameBot bot, string message)
        {
            if (player == null || bot == null || string.IsNullOrWhiteSpace(message))
                return;
            if (!IsFormal(bot))
            {
                AutonomousBotChatCoordinator.OnPlayerTell(player, bot, message);
                return;
            }
            BotChatTranscript.Player("tell", player, message, bot.Name);
            string reply = Reply(bot, player, message, Random.Shared);
            _ = new ECSGameTimer(bot, _ =>
            {
                if (bot.IsAlive && player.Client != null && player.ObjectState == GameObject.eObjectState.Active)
                {
                    ChatUtil.SendSendMessage(player.Client, "Social.ReceiveMessage.Private.Sends", bot.Name, reply);
                    BotChatTranscript.Bot("tell", bot.Name, bot.Realm, reply, player.Name, bot);
                }
                return 0;
            }, 1_500 + Random.Shared.Next(1_500));
        }

        public static bool IsCompanion(GameBot bot) => HiredCompanionsMode.IsOn && HiredCompanions165.IsHired(bot);

        private static GamePlayer Owner(GameBot bot) =>
            bot?.Group?.GetPlayersInTheGroup().FirstOrDefault(p => HiredCompanions165.RosterOf(p).Any(h => h.BotId == bot.DatabaseID));

        private static bool CanSpeak(GameBot bot, GamePlayer owner) =>
            bot != null && owner != null && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && !bot.IsCasting &&
            owner.CurrentRegionID == bot.CurrentRegionID && bot.IsWithinRadius(owner, WorldMgr.SAY_DISTANCE);

        /// <summary>A line as the companion types it: its own writing style, or as written for a formal companion.</summary>
        private static string Typed(GameBot bot, string line) =>
            line == null || IsFormal(bot) ? line : AutonomousBotVoices.StyleFor(bot, line);

        /// <summary>Called where the world's bots start their own talk: a companion says something relevant instead.</summary>
        public static bool TryAmbient(GameBot bot)
        {
            long now = GameLoop.GameLoopTime;
            if (bot == null || NextAmbient.TryGetValue(bot.DatabaseID, out long next) && now < next || IsQuiet(bot.DatabaseID))
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
            LastAmbientLine[bot.DatabaseID] = line;
            return bot.Say(Typed(bot, line));
        }

        public static string AmbientLine(GameBot bot, GamePlayer owner, Random random)
        {
            int level = bot.Level;
            bool levelled = LastLevel.TryGetValue(bot.DatabaseID, out int was) && level > was;
            LastLevel[bot.DatabaseID] = level;
            LastAmbientLine.TryGetValue(bot.DatabaseID, out string last);
            return AmbientLine(new AmbientFacts(
                owner.Name, level, levelled,
                bot.MaxHealth > 0 ? bot.Health * 100 / bot.MaxHealth : 100,
                bot.MaxMana > 0 ? bot.Mana * 100 / bot.MaxMana : 100,
                bot.InCombatInLast(30_000),
                bot.CurrentZone?.Description,
                bot.CurrentRegion?.IsCapitalCity == true || AutonomousBotGroupCoordinator.IsInTownArea(bot),
                owner.TargetObject is GameNPC { IsAlive: true } npc && npc.Realm == eRealm.None ? npc.Name : null,
                last), random, IsFormal(bot));
        }

        /// <summary>What a companion's ambient line is drawn from.</summary>
        public readonly record struct AmbientFacts(string Owner, int Level, bool Levelled, int HealthPercent, int PowerPercent,
            bool FoughtRecently, string Zone, bool InTown, string TargetName, string LastLine);

        /// <summary>
        /// The ambient line for these facts, as written (the caller types it in the companion's style). Pure. Chat audit
        /// (TEXT-007, TEXT-008, LOG-021): no danger line in a capital or a town ("Keep your eyes open here in City of Camelot."
        /// on 11/18 07:34 and 07:39), more idle lines than five, and never the line it said last.
        /// </summary>
        public static string AmbientLine(AmbientFacts facts, Random random, bool formal = false)
        {
            string who = facts.Owner;
            int level = facts.Level;
            if (facts.Levelled)
                return formal
                    ? random.Next(2) == 0 ? $"I feel stronger, {who}. Level {level} now." : $"Level {level}. I should see my trainer when we are next in town."
                    : random.Next(2) == 0 ? $"ding {level} :)" : $"{level} now, need my trainer next time were in town";
            if (facts.HealthPercent < 50)
                return formal
                    ? random.Next(2) == 0 ? "I am hurt. A moment to bind my wounds would help." : $"Give me a moment, {who}. I am not at full strength."
                    : random.Next(2) == 0 ? "low on hp, gimme a sec" : $"{who} hold up, need to heal up a bit";
            if (facts.PowerPercent < 30)
                return formal
                    ? random.Next(2) == 0 ? "My power is low. Let me sit a moment before the next pull." : "I need to rest my power before we go on."
                    : random.Next(2) == 0 ? "oom, need to med before the next pull" : "low pow, med break pls";
            var lines = new List<string>();
            string zone = ZoneName(facts.Zone);
            if (facts.FoughtRecently)
                lines.AddRange(formal
                    ? ["That went well. Ready for the next one.", $"Well fought, {who}.", "I am ready when you are."]
                    : ["nice one, next?", $"gj {who}", "ready when u are"]);
            else if (formal)
            {
                if (!facts.InTown && zone != null)
                    lines.Add($"Keep your eyes open here in {zone}.");
                if (!string.IsNullOrWhiteSpace(facts.TargetName))
                    lines.Add($"Shall we take on that {facts.TargetName}?");
                lines.AddRange([$"Lead the way, {who}.", "I am right behind you.", $"Say the word and I will follow, {who}.",
                    $"I have your back, {who}.", "Shall we move on?", "I could walk further yet.", "Tell me when you wish to rest.",
                    "I am ready for whatever comes.", "A good day for it."]);
                if (!facts.InTown)
                    lines.AddRange(["It is quiet. I do not trust it.", "I am watching behind us."]);
            }
            else
            {
                if (!facts.InTown && zone != null)
                    lines.Add($"eyes open, {zone} can get nasty");
                if (!string.IsNullOrWhiteSpace(facts.TargetName))
                    lines.Add($"that {facts.TargetName} next?");
                lines.AddRange([$"lead the way {who}", "right behind u", "where to next?", $"got ur back {who}", "move on?",
                    "i can go a while yet", "tell me when u want a break", "ready for whatever", "good day for it"]);
                if (!facts.InTown)
                    lines.AddRange(["quiet out here, dont like it", "watching behind us"]);
            }
            string[] fresh = lines.Where(line => !string.Equals(line, facts.LastLine, StringComparison.Ordinal)).ToArray();
            return fresh.Length > 0 ? fresh[random.Next(fresh.Length)] : lines[random.Next(lines.Count)];
        }

        /// <summary>A zone's name as a companion says it: "City of Camelot" reads "Camelot". Null when there is none. Pure.</summary>
        public static string ZoneName(string zone)
        {
            if (string.IsNullOrWhiteSpace(zone))
                return null;
            zone = zone.Trim();
            return zone.StartsWith("City of ", StringComparison.OrdinalIgnoreCase) ? zone[8..] : zone;
        }

        /// <summary>A player spoke near their companions: one answers (always the one named).</summary>
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
            // A companion told to keep quiet answers only when named.
            GameBot[] open = near.Where(b => !IsQuiet(b.DatabaseID)).ToArray();
            if (named == null && open.Length == 0)
                return;
            GameBot speaker = named ?? open[Random.Shared.Next(open.Length)];
            long now = GameLoop.GameLoopTime;
            if (NextReply.TryGetValue(speaker.DatabaseID, out long next) && now < next)
                return;
            NextReply[speaker.DatabaseID] = now + ReplyGapMs;
            string reply = Typed(speaker, Reply(speaker, player, message, Random.Shared));
            _ = new ECSGameTimer(speaker, _ =>
            {
                if (CanSpeak(speaker, player))
                    speaker.Say(reply);
                return 0;
            }, 1_200 + Random.Shared.Next(1_200));
        }

        /// <summary>A companion's answer to its player's /say, as written (in the formal register when its Speech is Formal).</summary>
        public static string Reply(GameBot bot, GamePlayer owner, string message, Random random)
        {
            string who = owner.Name;
            bool formal = IsFormal(bot);
            // The promise is kept (TEXT-006): a quiet companion says nothing of its own until told to talk again.
            if (QuietWords.IsMatch(message) && !SpeakWords.IsMatch(message))
            {
                Quiet[bot.DatabaseID] = true;
                return formal ? "As you wish. I will keep quiet until you tell me to speak." : "k, quiet till u tell me to talk again";
            }
            if (SpeakWords.IsMatch(message) && Quiet.TryRemove(bot.DatabaseID, out _))
                return formal ? $"Very well, {who}." : "k, talking again :)";
            if (HealthWords.IsMatch(message))
            {
                bool hurt = bot.MaxHealth > 0 && bot.Health * 100 / bot.MaxHealth < 50;
                bool oom = bot.MaxMana > 0 && bot.Mana * 100 / bot.MaxMana < 30;
                return formal
                    ? hurt ? "Hurt, but standing. A rest would help." : oom ? "Fine, but my power is low." : "Ready to fight."
                    : hurt ? "hurt but ok, a rest would help" : oom ? "fine, low on power tho" : "fine, ready";
            }
            return ReplyTo(AutonomousChatIntentModel.Predict(message), who, random, formal);
        }

        /// <summary>The answer to a line of this intent. Pure.</summary>
        public static string ReplyTo(eAutonomousChatIntent intent, string who, Random random, bool formal) => formal
            ? intent switch
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
            }
            : intent switch
            {
                eAutonomousChatIntent.Greeting => random.Next(2) == 0 ? $"hey {who}" : "hi :)",
                eAutonomousChatIntent.Farewell => "k, ill be here",
                eAutonomousChatIntent.Travel => random.Next(2) == 0 ? "lead on, im behind u" : "wherever u go",
                eAutonomousChatIntent.Dungeon => "stay close, one pull at a time",
                eAutonomousChatIntent.RvR => "if we go rvr im with u",
                eAutonomousChatIntent.Trading => "hand me anything u want me to wear, ill put it on",
                eAutonomousChatIntent.Grinding => "point me at the next one",
                eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Help => "im here, what do u need?",
                eAutonomousChatIntent.Banter => random.Next(2) == 0 ? "lol" : "haha ya",
                eAutonomousChatIntent.Abuse => "save it for the enemy",
                eAutonomousChatIntent.IgnoreOutOfWorld => "no idea what that means but im ready",
                _ => random.Next(3) switch { 0 => "k", 1 => "ok", _ => $"sure {who}" },
            };
    }
}
