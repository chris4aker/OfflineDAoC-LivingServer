using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The 1.2.2 chat audit's question bench (docs/bot-voices/CHAT-AUDIT-1.2.2.md): every question of
    /// tools/bot-voices/eval/player_questions.json (embedded) through the reply path a player's line takes, without a world.
    /// /faction: OnPlayerFactionChat's screen, SchedulePlayerReplies (the knowledge lookup, the help call, the shapes said to
    /// nobody, grouping, the realm-wide thanks and grats), VoicedChoice (the facts, the situation, the shapes, the corpus pick
    /// with the server's filters, the honest "not sure") and Budgeted (credit, contradictions, the wrong situation, the fit, the
    /// acknowledgement and the rescue). A tell takes TellReply's checks first, /group ReplyChoiceToPlayer, a named line
    /// KnownChoice. What needs the live world is a stub: the responder and the world come from the questions file (its
    /// "responders" and "world"); trainer and location answers name the catalog place (data/chat/places.json) where the live
    /// lookup would name an NPC or a zone of the world data; where-to-hunt takes a catalog zone of the level for the zone bots
    /// camp in; enemy sightings, how-far and join replies are "[world: ...]" markers. Every stubbed answer says so (stub).
    /// 1.2.2 (F0): no mirror. The routing is AutonomousChatReplyPath's and AutonomousChatKnowledge.TryAnswer(KnowledgePorts)'s, the
    /// functions the coordinator calls; only the ports (the responder, its corpus picks, the stub world) are the bench's.
    ///
    /// Report-only (the owner's 1.2.2 ask, 2026-11-22: "reduce or eliminate general fluff and non answers"): it writes every
    /// answer to CHAT_BENCH_OUT (else chat-bench/bench-replies.json in the test folder) and fails only when it can't answer.
    /// tools/bot-voices/eval/score_bench.py scores the file in CI. After the fixes it becomes a ratchet.
    /// </summary>
    [TestFixture]
    public class UT_ChatQuestionBench
    {
        internal sealed record Question(string Id, string Text, string Channel, eRealm Realm, string Cls, int Level, string Intent, string To);

        internal sealed record Responder(long Id, string Name, string Cls, int Level, string Zone, string Target, string Spec);

        internal sealed record Answer(string Id, string Route, string Reply, bool Stub, int Fit, string Outcome, int Answerers, int DelayMs, bool Twice);

        private static eRealm RealmOf(string code) => code switch
        {
            "alb" => eRealm.Albion, "mid" => eRealm.Midgard, "hib" => eRealm.Hibernia, _ => eRealm.None,
        };

        private static string CodeOf(eRealm realm) => realm switch
        {
            eRealm.Albion => "alb", eRealm.Midgard => "mid", eRealm.Hibernia => "hib", _ => "*",
        };

        // The classes of each realm (1.65), for a trainer question about another realm's class.
        private static readonly Dictionary<string, eRealm> ClassRealm = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Armsman"] = eRealm.Albion, ["Paladin"] = eRealm.Albion, ["Mercenary"] = eRealm.Albion, ["Reaver"] = eRealm.Albion,
            ["Cleric"] = eRealm.Albion, ["Friar"] = eRealm.Albion, ["Minstrel"] = eRealm.Albion, ["Scout"] = eRealm.Albion,
            ["Infiltrator"] = eRealm.Albion, ["Sorcerer"] = eRealm.Albion, ["Wizard"] = eRealm.Albion, ["Theurgist"] = eRealm.Albion,
            ["Cabalist"] = eRealm.Albion, ["Necromancer"] = eRealm.Albion,
            ["Warrior"] = eRealm.Midgard, ["Thane"] = eRealm.Midgard, ["Skald"] = eRealm.Midgard, ["Berserker"] = eRealm.Midgard,
            ["Savage"] = eRealm.Midgard, ["Hunter"] = eRealm.Midgard, ["Shadowblade"] = eRealm.Midgard, ["Healer"] = eRealm.Midgard,
            ["Shaman"] = eRealm.Midgard, ["Runemaster"] = eRealm.Midgard, ["Spiritmaster"] = eRealm.Midgard, ["Bonedancer"] = eRealm.Midgard,
            ["Hero"] = eRealm.Hibernia, ["Champion"] = eRealm.Hibernia, ["Blademaster"] = eRealm.Hibernia, ["Ranger"] = eRealm.Hibernia,
            ["Nightshade"] = eRealm.Hibernia, ["Druid"] = eRealm.Hibernia, ["Warden"] = eRealm.Hibernia, ["Bard"] = eRealm.Hibernia,
            ["Eldritch"] = eRealm.Hibernia, ["Enchanter"] = eRealm.Hibernia, ["Mentalist"] = eRealm.Hibernia, ["Animist"] = eRealm.Hibernia,
            ["Valewalker"] = eRealm.Hibernia,
        };

        private static readonly HashSet<string> Healers = new(StringComparer.OrdinalIgnoreCase) { "Cleric", "Friar", "Healer", "Shaman", "Druid", "Bard", "Warden" };

        internal sealed class Bench
        {
            public Question[] Questions;
            public Dictionary<eRealm, Responder> Responders = new();
            public AutonomousChatQuestions.WorldFacts World;
        }

        private static string Resource(string file)
        {
            var assembly = typeof(UT_ChatQuestionBench).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("." + file, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static Question[] QuestionsOf(JsonElement root) =>
            root.GetProperty("questions").EnumerateArray().Select(q => new Question(Str(q, "id"), Str(q, "text"), Str(q, "channel"),
                RealmOf(Str(q, "realm")), Str(q, "cls"), q.GetProperty("level").GetInt32(), Str(q, "intent"), Str(q, "to"))).ToArray();

        private static string Str(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        /// <summary>The bench; <paramref name="holdout"/>: its questions from player_questions_holdout.json (written after the 1.2.2 fixes).</summary>
        internal static Bench Load(bool holdout = false)
        {
            using JsonDocument doc = JsonDocument.Parse(Resource("player_questions.json"));
            JsonElement root = doc.RootElement;
            var bench = new Bench();
            if (holdout)
            {
                using JsonDocument held = JsonDocument.Parse(Resource("player_questions_holdout.json"));
                bench.Questions = QuestionsOf(held.RootElement);
            }
            else
                bench.Questions = QuestionsOf(root);
            foreach (JsonProperty r in root.GetProperty("responders").EnumerateObject())
                bench.Responders[RealmOf(r.Name)] = new Responder(r.Value.GetProperty("id").GetInt64(), Str(r.Value, "name"), Str(r.Value, "cls"),
                    r.Value.GetProperty("level").GetInt32(), Str(r.Value, "zone"), Str(r.Value, "target"), Str(r.Value, "spec"));
            JsonElement world = root.GetProperty("world");
            var relics = world.GetProperty("relics").EnumerateArray()
                .Select(r => (Str(r, "name"), RealmOf(Str(r, "owner")), RealmOf(Str(r, "home")), true)).ToList();
            var keeps = world.GetProperty("keeps").EnumerateArray().Select(k => (Str(k, "name"), RealmOf(Str(k, "owner")))).ToList();
            bench.World = new AutonomousChatQuestions.WorldFacts(RealmOf(Str(world, "df_owner")), relics, keeps, AutonomousChatPlaces.All);
            return bench;
        }

        private static AutonomousChatQuestions.BotFacts FactsOf(Responder r, eRealm realm) =>
            new(r.Id, r.Level, r.Cls, realm, 3, BotPersonalityType.Leveler, false, 2, null, 6, 1, false, r.Name, r.Zone, 1, r.Target, r.Spec);

        // ---------------------------------------------------------------------------------------------------------- the path

        private sealed class Run
        {
            public Question Q;
            public string Text;
            public Responder Me;
            public AutonomousChatQuestions.BotFacts Facts;
            public eRealm Realm;
            public Random Rng;
            public VC Corpus;
            public VC.RecentLines Recent;
            public DateTime Now;
            public AutonomousChatQuestions.WorldFacts World;
            public bool Stub;
        }

        private static VC.Speaker SpeakerOf(Run run, long id) =>
            new(id, run.Me.Name, run.Realm, run.Me.Cls, run.Me.Level, BotPersonalityType.Leveler,
                new HashSet<string>(new[] { "solo", "outdoors", "evening" }.Concat(AutonomousChatPlaces.KeysAt(run.Me.Zone).Select(k => "at:" + k)),
                    StringComparer.Ordinal));

        /// <summary>The corpus port: AutonomousBotVoices.TryLine's request, with the bench's speaker, slots and clock.</summary>
        private static VC.Picked Pick(Run run, string kind, string intent, string answers, IEnumerable<string> flags, bool relax, Func<VC.Line, bool> filter,
            long? id = null)
        {
            run.Now = run.Now.AddMinutes(1);
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["player"] = "Wynn", ["me"] = run.Me.Name, ["class"] = run.Me.Cls.ToLowerInvariant(), ["level"] = run.Me.Level.ToString(),
                ["realm"] = GlobalConstants.RealmToName(run.Realm), ["zone"] = run.Me.Zone,
            };
            VC.Speaker speaker = SpeakerOf(run, id ?? run.Me.Id);
            if (flags != null)
                speaker = speaker with { Flags = new HashSet<string>(speaker.Flags.Concat(flags), StringComparer.Ordinal) };
            AutonomousChatFiller.ReplyScope scope = AutonomousChatFiller.Current;
            // As TryLine: no musings or repeated filler in a reply, and no "omw" (the bench has no promise gate, so it never comes).
            bool picked = run.Corpus.TryPick(new VC.Request(kind, speaker, slots, run.Recent, run.Now, run.Rng)
            {
                Intent = intent, Answers = answers,
                Filter = line => AutonomousChatFiller.ReplyAllowed(line, scope) && AutonomousBotVoices.PromiseAllowed(line) && (filter == null || filter(line)),
                RelaxTo = relax ? AutonomousBotVoices.ShortReplyRest : VC.RecentLines.Relaxed, LocalHour = 20,
            }, out VC.Picked chosen);
            if (picked && scope != null && AutonomousChatFiller.IsGeneric(chosen.Text))
                AutonomousChatFiller.NoteGeneric(scope.ChannelKey, scope.Now);
            return picked ? chosen : null;
        }

        private static string Style(Run run, string text) =>
            text == null || text.StartsWith("[world", StringComparison.Ordinal) ? text
                : AutonomousBotVoices.StyleText(run.Corpus.VoiceFor(run.Me.Id), run.Me.Id, text, run.Rng);

        /// <summary>The knowledge lookup's world steps on the stub world (AutonomousChatKnowledge.TryAnswer runs the order).</summary>
        private static AutonomousChatKnowledge.KnowledgePorts KnowledgePortsOf(Run run) => new()
        {
            Realm = run.Realm, Level = run.Q.Level, ClassName = run.Q.Cls, Random = run.Rng, Corpus = run.Corpus, Bible = LoreBible.Current,
            HowFar = query =>
            {
                if (AutonomousChatQuestions.PlaceNamed(query, AutonomousChatPlaces.All) == null)
                    return null;
                run.Stub = true;
                return "[world: how far, from where the player stands]";
            },
            World = query => AutonomousChatQuestions.AnswerWorld(query, run.Realm, run.World, run.Rng),
            // The stable routes and the asker's spell lines are world data the bench doesn't load.
            Stable = null, Spell = null,
            Raids = () => Array.Empty<AutonomousRealmRaid.Summary>(),
            Trainer = query => Trainer(run, query),
            Location = query => Location(run, query),
        };

        private static string Knowledge(Run run, string query) =>
            AutonomousChatKnowledge.TryAnswer(KnowledgePortsOf(run), query, out string answer) ? answer : null;

        /// <summary>TryAnswerTrainer: the class named, else the asker's; the live world names the trainer NPC (stub: the capital).</summary>
        private static string Trainer(Run run, string query)
        {
            string named = AutonomousChatFieldTalk.ClassNamed(query);
            string cls = named ?? run.Q.Cls;
            bool own = named == null || string.Equals(named, run.Q.Cls, StringComparison.OrdinalIgnoreCase);
            // A class of another realm has no trainer the asker can reach: the live lookup finds none and answers nothing.
            if (!own && ClassRealm.TryGetValue(cls, out eRealm home) && home != run.Realm)
                return null;
            run.Stub = true;
            return AutonomousChatKnowledge.GenericTrainerAnswer(run.Realm, cls, run.Rng);
        }

        private static string P(Random random, params string[] options) => options[random.Next(options.Length)];

        /// <summary>TryAnswerLocation on the catalog: the live lookup reads zones, NPCs and mobs from the world data (stub).</summary>
        private static string Location(Run run, string query)
        {
            AutonomousChatPlaces.Place place = AutonomousChatQuestions.PlaceNamed(query, AutonomousChatPlaces.All);
            if (place == null)
                return null;
            run.Stub = true;
            string name = place.Key;
            eRealm owner = RealmOf(place.Realm);
            if (owner != eRealm.None && owner != run.Realm)
                return AutonomousChatKnowledge.OtherRealmLine(name, owner, place.Type == "frontier", run.Rng);
            string area = place.Zones.FirstOrDefault(z => !string.Equals(z, name, StringComparison.OrdinalIgnoreCase)) ?? place.Zones.FirstOrDefault();
            // The live lookup names the zone the entrance is in (DbZonePoint); the catalog doesn't know it.
            if (place.Type == "dungeon")
                return P(run.Rng, $"{name} is on our side, look for the entrance", $"{name}? its a dungeon on our side, bring a group");
            if (place.Type == "capital")
                return P(run.Rng, $"{name}? any stable master runs a horse there", $"take a horse to {name}, the roads all lead there",
                    $"{name}, the capital, a stable master gets you there");
            return AutonomousChatKnowledge.AreaSaysNothing(name, area)
                ? P(run.Rng, $"{name} is its own zone, a stable master can get you close", $"{name}? take a horse from a stable master",
                    $"{name}, a horse gets you most of the way")
                : P(run.Rng, $"{name} is in {area}", $"{name}? thats in {area}", $"in {area}, follow the road", $"{area}, a horse gets you most of the way");
        }

        /// <summary>AutonomousChatLiveAnswers.WhereToHunt with the camps stubbed: a catalog zone of the realm for the level.</summary>
        private static string WhereToHunt(Run run, string incoming)
        {
            run.Stub = true;
            int level = AutonomousChatLiveAnswers.LevelAsked(incoming, run.Q.Level);
            string code = CodeOf(run.Realm);
            string zone = AutonomousChatPlaces.All.Where(p => p.Realm == code && p.Type == "zone" && p.Levels is { Length: 2 } l && level >= l[0] && level <= l[1])
                .OrderBy(p => Math.Abs((p.Levels[0] + p.Levels[1]) / 2 - level)).Select(p => p.Zones.FirstOrDefault() ?? p.Key).FirstOrDefault();
            string dungeon = level >= 7 ? AutonomousBotVoices.DungeonFor(run.Realm, level, run.Rng) : null;
            if (zone != null && dungeon != null && !zone.Contains(dungeon, StringComparison.OrdinalIgnoreCase))
                return run.Rng.Next(3) switch
                {
                    0 => $"for {level} try {zone}, lots of people camping there right now. or {dungeon} if you get a group",
                    1 => $"{zone} is busy with {level}s at the moment, {dungeon} is good too with a group",
                    _ => $"{dungeon} with a group, or {zone} solo, thats where the {level}s are",
                };
            if (zone != null)
                return run.Rng.Next(2) == 0 ? $"{zone}, saw a bunch of {level}s camping there" : $"try {zone}, thats where the {level}s are right now";
            if (dungeon != null)
                return run.Rng.Next(2) == 0 ? $"{dungeon} is good around {level} if you can get a group" : $"{dungeon} for {level}, bring a group";
            return AutonomousChatLiveAnswers.StarterZones(run.Realm, level, run.Rng);
        }

        /// <summary>
        /// The reply path's ports for the bench's responder (F0): AutonomousChatReplyPath runs the rules, these give it the stub
        /// world. AnswerQuestion (the bot's own facts by name, else the world's), the situation, the corpus, the style, the moment.
        /// No memory of the player, no conversation (each question opens a fresh channel), no promise gate (it never comes).
        /// </summary>
        private static AutonomousChatReplyPath.Ports PortsOf(Run run)
        {
            bool heals = Healers.Contains(run.Me.Cls);
            return new AutonomousChatReplyPath.Ports
            {
                Random = run.Rng, Voices = true, Memory = false, BotId = run.Me.Id, ClassName = run.Me.Cls, Level = run.Me.Level,
                Fact = (incoming, addressed) =>
                {
                    eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(incoming);
                    if (intent is eAutonomousChatIntent.BotCheck or eAutonomousChatIntent.Abuse)
                        return null;
                    string self = addressed ? AutonomousChatQuestions.SelfQuestion(incoming) : null;
                    string known = self != null ? AutonomousChatQuestions.AnswerSelf(self, incoming, run.Facts, run.Rng)
                        : AutonomousChatQuestions.AnswerWorld(incoming, run.Realm, run.World, run.Rng);
                    return Style(run, known);
                },
                Context = (channel, _) => new AutonomousChatContext.Facts(run.Me.Name, "Wynn", channel ?? "faction", channel is "say" or "group", heals, false,
                    run.Me.Level, null, AutonomousBotVoices.CapitalOf(run.Realm)),
                WillComeRez = () => false,
                Facts = () => run.Facts,
                Live = (shape, incoming) => shape switch
                {
                    "ask_whereyou" => AutonomousBotChatCoordinator.WhereText(run.Me.Zone, false, run.Me.Level, run.Me.Cls, run.Rng),
                    "ask_hunt" => WhereToHunt(run, incoming),
                    "ask_enemy" => Stubbed(run, "[world: enemy sightings from the realm's news board]"),
                    // "anyone in camelot?": the bench's responder is in its own zone only.
                    _ => null,
                },
                Line = (kind, intent, answers, flags, relax, filter) => Pick(run, kind, intent, answers, flags, relax, filter),
                Style = text => Style(run, text),
                Moment = (message, channel, addressed) => new AutonomousChatReplyBudget.Moment(message, channel, addressed,
                    AutonomousChatIntentModel.Predict(message), run.Me.Cls, heals, heals, false, null, addressed ? run.Facts : null),
                Untrue = text => AutonomousChatQuestions.ContradictsSelf(text, run.Facts),
            };
        }

        private static string Stubbed(Run run, string text)
        {
            run.Stub = true;
            return text;
        }

        private static AutonomousBotChatCoordinator.ReplyChoice VoicedOf(Run run, AutonomousChatReplyPath.Ports ports, string message, bool addressed, string channel) =>
            AutonomousChatReplyPath.Voiced(ports, message, () => null, null, addressed, channel, relax: true);

        /// <summary>One question through the path its channel takes: the server's own functions, the stub world in the ports.</summary>
        private static Answer AnswerOne(Run run)
        {
            Question q = run.Q;
            string channel = q.Channel;
            bool faction = channel == "faction";
            string message = q.Text.Trim();
            bool named = !string.IsNullOrEmpty(q.To) && (channel is "faction" or "say") &&
                         AutonomousChatThreads.WithoutName(message, q.To) is { Length: > 0 } unnamed && unnamed != message;
            if (named)
                message = AutonomousChatThreads.WithoutName(message, q.To);
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
            string shape = AutonomousChatShortTalk.QuestionShape(message);
            using AutonomousChatFiller.ReplyScope fillerScope = AutonomousChatFiller.Replying(message, (faction ? "faction:" : channel + ":") + run.Realm,
                (long)(run.Now - new DateTime(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc)).TotalMilliseconds);
            AutonomousChatReplyPath.Ports ports = PortsOf(run);
            AutonomousBotChatCoordinator.ReplyChoice choice;
            int count = 1;
            AutonomousChatReplyPath.LineIn line = null;
            if (channel == "tell")
            {
                // TellReply (its threads need a conversation: each question is the first line), then the budget.
                choice = AutonomousChatReplyPath.Tell(ports, message, intent,
                    invited: () => Stubbed(run, "[world: got the invite or not]"),
                    join: () => AutonomousBotChatCoordinator.LevelGapReply(run.Me.Level, q.Level, run.Rng) ?? Stubbed(run, "[world: join reply]"),
                    where: () => AutonomousBotChatCoordinator.WhereText(run.Me.Zone, false, run.Me.Level, run.Me.Cls, run.Rng),
                    friend: null,
                    knowledge: () => Knowledge(run, message),
                    voiced: () => VoicedOf(run, ports, message, true, "tell"));
                choice = AutonomousChatReplyPath.Budget(ports, choice, message, true, "tell");
            }
            else if (channel == "group")
                // ReplyChoiceToPlayer: the voiced reply to a groupmate, then the budget (a line that names nobody isn't addressed).
                choice = AutonomousChatReplyPath.Budget(ports, VoicedOf(run, ports, message, true, "group"), message, false, "group");
            else
            {
                // SchedulePlayerReplies: the knowledge lookup, the help call, the plan, the bot's first answer.
                bool helpCall = AutonomousBotChatCoordinator.IsHelpCall(message, shape);
                bool directed = named;
                string knowledge = AutonomousChatReplyPath.KnowledgeFirst(message, intent, helpCall, directed) ? Knowledge(run, message) : null;
                // "anyone in camelot?" is answered by bots that are there: the bench's responder is in its own zone only.
                if (shape == "ask_inplace" && knowledge == null)
                    return Done(run, "none not_there (no bot is there)", null, 0, "silent", 0);
                // "gz Aldric": the bench's responder didn't just level.
                bool heard = !(directed && intent == eAutonomousChatIntent.Congrats);
                line = new AutonomousChatReplyPath.LineIn(message, faction, directed, true, intent, shape, helpCall, knowledge, q.Level);
                AutonomousChatReplyPath.LinePlan plan = AutonomousChatReplyPath.Plan(line, waitForBusyNamed: () => false,
                    anyFits: (level, _) => Math.Abs(run.Me.Level - level) <= 6, anyEligible: heard, random: run.Rng);
                switch (plan.Step)
                {
                    case AutonomousChatReplyPath.Step.Silent:
                        return Done(run, plan.Route, null, 0, "silent", 0);
                    case AutonomousChatReplyPath.Step.Join:
                        return Done(run, "join", Style(run, AutonomousBotChatCoordinator.LevelGapReply(run.Me.Level, q.Level, run.Rng) ?? "[world: join reply]"),
                            AutonomousChatReplyFit.Known, "answer", 1, stub: true);
                }
                if (!heard)
                    return Done(run, "none gz (only a bot that just levelled)", null, 0, "silent", 0);
                if (plan.Grouping && plan.NoneNearLevel == null && Math.Abs(run.Me.Level - plan.GroupLevel) > 6)
                    return Done(run, "none no bot near the level", null, 0, "silent", 0);
                if (plan.JustLevelled)
                    return Done(run, "none gz (only a bot that just levelled)", null, 0, "silent", 0);
                if (plan.Groupmates)
                    return Done(run, "none ty (groupmates only)", null, 0, "silent", 0);
                // Five bots of the realm hear a /faction line.
                count = AutonomousChatReplyPath.ReplyCount(line, plan, directed ? 1 : 5, false, run.Rng);
                if (count == 0)
                    return Done(run, "none " + AutonomousBotVoices.IntentName(intent) + " (draws no answer here)", null, 0, "silent", 0);
                choice = AutonomousChatReplyPath.Choose(ports, line, plan, channel,
                    help: () => AutonomousBotChatCoordinator.HelpRequestLines(AutonomousChatQuestions.PlaceNamed(message, AutonomousChatPlaces.All)?.Key, run.Rng).Instead,
                    voiced: addressed => VoicedOf(run, ports, message, addressed, channel));
            }
            string route = choice.Route, text = choice.Text;
            int fit = AutonomousChatReplyFit.Of(route, message);
            string outcome = text == null ? "silent"
                : route.StartsWith("rescue (", StringComparison.Ordinal) ? "rescue"
                : route.StartsWith("short (", StringComparison.Ordinal) ? "short"
                : route.StartsWith("ack (", StringComparison.Ordinal) ? "ack"
                : "answer";
            // How many bots answer (the arbiter's plan on SchedulePlayerReplies' count), with five bots of the realm to hear it.
            int answerers = 0;
            bool twice = false;
            if (text != null)
            {
                if (line == null || line.Directed)
                    answerers = 1;
                else
                {
                    var plan = AutonomousChatArbiter.Arrange(count, AutonomousChatReplyBudget.IsAlarm(message, intent) ? eAutonomousChatIntent.Alarm : intent, route,
                        false, AutonomousChatQuestions.IsQuestion(message), 5, AutonomousChatBudget.Current.PileOnCap, run.Rng);
                    answerers = Math.Max(1, plan.Total);
                    // A social pile-on draws its own line: the same words twice in a row read as an echo.
                    if (plan.Social > 0 && route.StartsWith("intent ", StringComparison.Ordinal))
                        twice = Pick(run, "reply", AutonomousBotVoices.IntentName(intent), null, null, true, null, id: run.Me.Id + 1) is { } second &&
                                string.Equals(second.Text.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase);
                }
            }
            return Done(run, route, text, fit, outcome, answerers, twice: twice);
        }

        private static Answer Done(Run run, string route, string text, int fit, string outcome, int answerers, bool stub = false, bool twice = false)
        {
            int delay = text == null ? 0 : AutonomousChatArbiter.DelayMs(run.Q.Text, text, 40, 0, run.Rng);
            return new Answer(run.Q.Id, route, text, stub || run.Stub || text != null && text.StartsWith("[world", StringComparison.Ordinal), fit, outcome,
                text == null ? 0 : answerers, delay, twice);
        }

        internal static List<Answer> AnswerAll(Bench bench)
        {
            var corpus = AutonomousBotVoices.Corpus;
            var recent = new VC.RecentLines();
            var now = new DateTime(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc);
            var answers = new List<Answer>();
            foreach (Question q in bench.Questions)
            {
                Responder me = bench.Responders[q.Realm];
                // One seed a question, so a question's answer doesn't change when another is added before it.
                int seed = q.Text.Aggregate(17, (h, c) => unchecked(h * 31 + c));
                var run = new Run
                {
                    Q = q, Me = me, Facts = FactsOf(me, q.Realm), Realm = q.Realm, Rng = new Random(seed), Corpus = corpus, Recent = recent,
                    Now = now, World = bench.World,
                };
                now = now.AddMinutes(3);
                answers.Add(AnswerOne(run));
            }
            return answers;
        }

        // ------------------------------------------------------------------------------------------------------------ tests

        [Test]
        public void AnswersEveryQuestionAndWritesTheReport() =>
            Write(Load(), 300, "CHAT_BENCH_OUT", "bench-replies.json");

        /// <summary>The holdout set (60 questions written after the fixes, never used to tune them); score_bench.py ratchet reports it.</summary>
        [Test]
        public void AnswersTheHoldoutSetAndWritesItsReport() =>
            Write(Load(holdout: true), 60, "CHAT_BENCH_HOLDOUT_OUT", "bench-holdout.json");

        private static void Write(Bench bench, int atLeast, string variable, string file)
        {
            Assert.That(bench.Questions.Length, Is.GreaterThanOrEqualTo(atLeast), $"the set holds at least {atLeast} player questions");
            List<Answer> answers = AnswerAll(bench);
            Assert.That(answers.Select(a => a.Id), Is.EqualTo(bench.Questions.Select(q => q.Id)), "every question gets a route");
            string path = Environment.GetEnvironmentVariable(variable) is { Length: > 0 } output
                ? output
                : Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-bench", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(answers.Select(a => new
            {
                id = a.Id, route = a.Route, reply = a.Reply, stub = a.Stub, fit = a.Fit, outcome = a.Outcome, answerers = a.Answerers, delay_ms = a.DelayMs,
                twice = a.Twice,
            }), new JsonSerializerOptions { WriteIndented = true }));
            int answered = answers.Count(a => a.Reply != null);
            TestContext.Progress.WriteLine($"CHAT_BENCH {file}: {answers.Count} questions, {answered} with a reply, written to {path}");
        }

        [Test]
        public void TheBenchIsDeterministic()
        {
            Bench bench = Load();
            List<Answer> first = AnswerAll(bench), second = AnswerAll(bench);
            // The knowledge lookups that draw with Random.Shared (TryAnswerTimeToCap, TryAnswerHowTo, the buy and tickets answers)
            // may word an answer differently; the route never changes.
            Assert.That(second.Select(a => a.Route), Is.EqualTo(first.Select(a => a.Route)));
        }
    }
}
