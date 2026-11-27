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
                    // Issue #475: out through the say gate (a pass-through).
                    BotSayGate.Say(new BotSayGate.Utterance("tell", bot.Name, bot.Realm, reply, player.Name, bot) { Listeners = [player] }, said =>
                    {
                        ChatUtil.SendSendMessage(player.Client, "Social.ReceiveMessage.Private.Sends", bot.Name, said);
                        return said;
                    });
                }
                return 0;
            }, AutonomousChatArbiter.AnswerDelayMs(message, reply, AutonomousBotChatCoordinator.Wpm(bot), "companion formal", false, true, 0, Random.Shared));
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

        /// <summary>
        /// A player spoke near their companions: one answers (always the one named). True when a companion took the line (its answer
        /// is on its way), so the gamebots nearby leave it to the companion (Realism 2b, #479; channels.md finding 1: a companion's
        /// "ok" was followed by a gamebot's real answer).
        /// </summary>
        public static bool OnPlayerSay(GamePlayer player, string message)
        {
            if (!HiredCompanionsMode.IsOn || player?.Group == null || string.IsNullOrWhiteSpace(message))
                return false;
            long[] ids = HiredCompanions165.RosterOf(player).Select(h => h.BotId).ToArray();
            GameBot[] near = player.Group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(b => ids.Contains(b.DatabaseID) && CanSpeak(b, player) && !b.InCombat).ToArray();
            if (near.Length == 0)
                return false;
            GameBot named = near.FirstOrDefault(b => AutonomousBotChatCoordinator.ContainsMention(message, b.Name));
            long now = GameLoop.GameLoopTime;
            // Conversation memory (#350): the player's line is already a turn of the /say conversation (the coordinator notes
            // it first); a companion that has the floor takes a line that names nobody, as a gamebot would.
            string room = AutonomousChatThreads.Say(player.CurrentRegionID);
            long floor = ChatConversations.Live.FloorHolder(player.Name, room, now);
            // PC check 0730 (build 5154cae): "you ready?" in /say with the gamebot Linaibel targeted (and holding the floor after
            // her "oh hey") drew Pannax's "ok". A line the player aims at another bot, by target or by talking with it, is that bot's.
            bool aimedElsewhere = AimedElsewhere(ids, player.TargetObject is GameBot target ? target.DatabaseID : 0, floor,
                AutonomousBotChatCoordinator.KeepsTheFloor(message, AutonomousChatIntentModel.Predict(message)));
            GameBot speaker = ChooseSpeaker(near, named, floor, Random.Shared, aimedElsewhere);
            if (speaker == null)
                return false;
            // #479: a question or a line naming the companion is never lost to its reply gap (as /faction and /say).
            if (!(AutonomousBotChatCoordinator.NeedsAnswer(message) || named != null) && NextReply.TryGetValue(speaker.DatabaseID, out long next) && now < next)
                return false;
            string unnamed = named != null && AutonomousChatThreads.WithoutName(message, named.Name) is { Length: > 0 } without ? without : message;
            string resolved = ChatConversations.Resolve(unnamed, ChatConversations.Live.In(player.Name, room, now));
            AutonomousBotChatCoordinator.ReplyChoice choice = Answer(speaker, player, resolved, Random.Shared);
            if (string.IsNullOrWhiteSpace(choice.Text))
            {
                AutonomousChatWhy.RecordSilence(player.Realm, "say", "player " + player.Name, choice.Route ?? "none", AutonomousChatReplyFit.Of(choice.Route, resolved),
                    "the companion has nothing true to say to it", player.Name);
                return false;
            }
            NextReply[speaker.DatabaseID] = now + ReplyGapMs;
            string reply = choice.Text;
            int due = AutonomousChatArbiter.AnswerDelayMs(resolved, reply, AutonomousBotChatCoordinator.Wpm(speaker), choice.Route,
                AutonomousChatQuestions.IsQuestion(resolved), true, 0, Random.Shared);
            _ = new ECSGameTimer(speaker, _ =>
            {
                if (CanSpeak(speaker, player) && speaker.Say(reply))
                {
                    NoteSaid(player.Name, room, speaker, reply, GameLoop.GameLoopTime);
                    AutonomousChatWhy.Record(speaker, "say", "player " + player.Name, "said", choice.Route, AutonomousChatReplyFit.Of(choice.Route, resolved),
                        named != null ? "named in the line" : "the player's companion answers", reply, choice.Line?.Id, player.Name);
                }
                return 0;
            }, due);
            return true;
        }

        /// <summary>
        /// A companion's answer to its player's /say line, as it types it (#479): its three commands (keep quiet, talk again, how are
        /// you); a Formal companion its own lines (<see cref="ReplyTo"/>); any other the party reply path its groupmates take in /group
        /// (<see cref="AutonomousBotGroupChatter.PartyChoice"/>: the knowledge lookup, a warning, "come", the meet-up, a pause, the
        /// voiced reply and the budget), and for a plain statement nothing there answers, the companion's own line where it says
        /// something true of a companion (<see cref="StatementLine"/>). Text null: it says nothing.
        /// </summary>
        public static AutonomousBotChatCoordinator.ReplyChoice Answer(GameBot bot, GamePlayer owner, string message, Random random)
        {
            if (Command(bot, owner, message) is { } command)
                return new AutonomousBotChatCoordinator.ReplyChoice(Typed(bot, command), "companion command");
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
            if (IsFormal(bot))
                return new AutonomousBotChatCoordinator.ReplyChoice(ReplyTo(intent, owner.Name, random, true), "companion formal");
            AutonomousBotChatCoordinator.ReplyChoice choice = AutonomousBotGroupChatter.PartyChoice(bot, owner, message, intent, true, "say", 1);
            if (!string.IsNullOrWhiteSpace(choice.Text))
                return choice;
            return StatementLine(message, intent, owner.Name, random) is { } own
                ? new AutonomousBotChatCoordinator.ReplyChoice(Typed(bot, own), "companion " + AutonomousBotVoices.IntentName(intent))
                : choice;
        }

        /// <summary>
        /// The companion's own answer to a plain statement of these intents, where it says something true of a hired companion: it
        /// follows ("lead on, im behind u"), it waits, it fights the next one, it wears what it is handed. Null for a question and
        /// for any other intent (no "k / ok / sure {who}" to anything, channels.md finding 1). Pure.
        /// </summary>
        public static string StatementLine(string message, eAutonomousChatIntent intent, string who, Random random)
        {
            if (string.IsNullOrWhiteSpace(message) || AutonomousChatReplyBudget.AsksSomething(message))
                return null;
            return intent is eAutonomousChatIntent.Travel or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Grinding or eAutonomousChatIntent.Trading
                ? ReplyTo(intent, who, random, false)
                : null;
        }

        /// <summary>
        /// Which companion answers the player's /say: the one named; else the one that has the floor (the last to speak to the
        /// player in this conversation); else, now and then (35%), one that was not told to keep quiet. Null: nobody.
        /// </summary>
        public static GameBot ChooseSpeaker(IReadOnlyList<GameBot> near, GameBot named, long floorBot, Random random, bool aimedElsewhere = false)
        {
            if (named != null)
                return named;
            if (near == null || near.Count == 0 || aimedElsewhere)
                return null;
            if (floorBot > 0 && near.FirstOrDefault(b => b.DatabaseID == floorBot && !IsQuiet(b.DatabaseID)) is { } floor)
                return floor;
            if (random.NextDouble() > 0.35)
                return null;
            GameBot[] open = near.Where(b => !IsQuiet(b.DatabaseID)).ToArray();
            return open.Length == 0 ? null : open[random.Next(open.Length)];
        }

        /// <summary>
        /// Whether the player's /say line is for a bot that is not one of their companions (PC check 0730): the player targets that
        /// bot, or it has the floor and the line goes on with it (<see cref="AutonomousBotChatCoordinator.KeepsTheFloor"/>), as the
        /// gamebots' own rule reads it (AutonomousChatScreen.SayAddressee, DESIGN-040). Then no companion chimes in unnamed. Pure.
        /// </summary>
        public static bool AimedElsewhere(IReadOnlyCollection<long> companionIds, long targetBotId, long floorBot, bool keepsTheFloor) =>
            targetBotId > 0 && companionIds?.Contains(targetBotId) != true ||
            keepsTheFloor && floorBot > 0 && companionIds?.Contains(floorBot) != true;

        /// <summary>A companion's line to its player is a turn of their conversation and gives it the floor (#350).</summary>
        public static void NoteSaid(string player, string room, GameBot bot, string text, long now) =>
            ChatConversations.Live.NoteBot(player, bot.DatabaseID, bot.Name, room, text, now);

        /// <summary>A companion's answer to its player's /say by its commands and intent alone, as written (the formal register when its
        /// Speech is Formal). A non-formal companion answers through <see cref="Answer"/>; this stays for the Formal one's tells.</summary>
        public static string Reply(GameBot bot, GamePlayer owner, string message, Random random) =>
            Command(bot, owner, message) ?? ReplyTo(AutonomousChatIntentModel.Predict(message), owner.Name, random, IsFormal(bot));

        /// <summary>The companion's three commands: keep quiet, talk again, how are you; null for any other line.</summary>
        private static string Command(GameBot bot, GamePlayer owner, string message)
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
            return null;
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
