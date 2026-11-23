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
    /// It writes every answer to CHAT_BENCH_OUT (else chat-bench/bench-replies.json in the test folder), and the holdout set's to
    /// CHAT_BENCH_HOLDOUT_OUT (bench-holdout.json); it fails only when it can't answer. tools/bot-voices/eval/score_bench.py ratchet
    /// scores them in CI and fails the build when the good share falls or a bad category grows past bench_ratchet.json (1.2.2 fixes).
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

        private static readonly HashSet<string> Healers = new(StringComparer.OrdinalIgnoreCase) { "Cleric", "Friar", "Healer", "Shaman", "Druid", "Bard", "Warden" };

        internal sealed class Bench
        {
            public Question[] Questions;
            public Dictionary<eRealm, Responder> Responders = new();
            public AutonomousChatQuestions.WorldFacts World;
            /// <summary>Towns of the world (1.2.3: the live lookup reads them from the Area table), with their realm.</summary>
            public (AutonomousChatKnowledge.NamedTown Town, eRealm Realm)[] Towns = [];
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
            if (world.TryGetProperty("towns", out JsonElement towns))
                bench.Towns = towns.EnumerateArray()
                    .Select(t => (new AutonomousChatKnowledge.NamedTown(Str(t, "name"), Str(t, "zone"), 0, 0, 0), RealmOf(Str(t, "realm")))).ToArray();
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
            public (AutonomousChatKnowledge.NamedTown Town, eRealm Realm)[] Towns = [];
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
            Town = query => Town(run, query),
            DfOwner = run.World.DfOwner,
        };

        /// <summary>
        /// TryAnswerTown on the stub towns: the town's zone; the live lookup adds the way and how far from where the player stands,
        /// which the bench doesn't know (so it answers as from another zone, the way unknown).
        /// </summary>
        private static string Town(Run run, string query)
        {
            AutonomousChatKnowledge.NamedTown town = AutonomousChatKnowledge.TownNamed(query,
                run.Towns.Where(t => t.Realm == run.Realm).Select(t => t.Town));
            return town == null || !AutonomousChatKnowledge.AsksOnlyWhereTownIs(query, town.Name) ? null
                : AutonomousChatKnowledge.TownWhereLine(town.Name, town.Zone, null, AutonomousChatKnowledge.Far.Unknown, false, false, run.Rng);
        }

        private static string Knowledge(Run run, string query) =>
            AutonomousChatKnowledge.TryAnswer(KnowledgePortsOf(run), query, out string answer) ? answer : null;

        /// <summary>TryAnswerTrainer: the class named, else the asker's; another realm's class said as such; the live world names the
        /// trainer NPC (stub: the capital).</summary>
        private static string Trainer(Run run, string query)
        {
            string named = AutonomousChatFieldTalk.ClassNamed(query);
            string cls = named ?? run.Q.Cls;
            // Another realm's class is answered from the class table alone; the rest name the trainer NPC live (stubbed here).
            eRealm home = AutonomousChatGear.RealmOf(cls);
            run.Stub |= home == run.Realm || home == eRealm.None;
            return AutonomousChatKnowledge.TrainerAnswerFor(run.Realm, named, run.Q.Cls, run.Rng);
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

        /// <summary>AutonomousChatLiveAnswers.WhereToHunt with the camps stubbed: a catalog zone of the realm for the level stands for
        /// the zone bots camp in (a realm named in the question has no camps, as live).</summary>
        private static string WhereToHunt(Run run, string incoming)
        {
            run.Stub = true;
            int level = AutonomousChatLiveAnswers.LevelAsked(incoming, run.Q.Level);
            eRealm named = AutonomousChatShortTalk.RealmNamed(incoming);
            eRealm realm = named is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia ? named : run.Realm;
            string zone = AutonomousChatLiveAnswers.CatalogZone(realm, level);
            string dungeon = level >= 7 && !AutonomousChatShortTalk.AsksSolo(incoming) ? AutonomousBotVoices.DungeonFor(realm, level, run.Rng) : null;
            return AutonomousChatLiveAnswers.HuntText(realm, level, zone, dungeon, realm == run.Realm, run.Rng);
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
            {
                // OnPlayerGroupChat: the knowledge lookup first (1.2.3: as the live path does; "where is Humberton Castle" in a
                // bot-led group), else ReplyChoiceToPlayer's voiced reply, then the budget (a line that names nobody isn't addressed).
                string known = Knowledge(run, message);
                choice = known != null ? new AutonomousBotChatCoordinator.ReplyChoice(Style(run, known), "knowledge")
                    : AutonomousChatReplyPath.Budget(ports, VoicedOf(run, ports, message, true, "group"), message, false, "group");
            }
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
            // SchedulePlayerReplies' timing: a short known fact types faster (F11), and a line said to the bot or the first answer to
            // a question in /faction comes within DirectTypingCapMs (PlayerAnswerDueMs).
            bool question = AutonomousChatQuestions.IsQuestion(run.Q.Text);
            bool directed = run.Q.Channel is "tell" or "group" || !string.IsNullOrEmpty(run.Q.To);
            int wpm = question && AutonomousChatArbiter.IsQuickFact(route, text) ? AutonomousChatArbiter.QuickWpm(40) : 40;
            int delay = text == null ? 0 : AutonomousBotChatCoordinator.PlayerAnswerDueMs(AutonomousChatArbiter.DelayMs(run.Q.Text, text, wpm, 0, run.Rng), 0,
                directed, run.Q.Channel == "faction" && question);
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
                    Now = now, World = bench.World, Towns = bench.Towns,
                };
                now = now.AddMinutes(3);
                answers.Add(AnswerOne(run));
            }
            return answers;
        }

        // ------------------------------------------------- audit 1.2.3 (report only): the same questions in every channel

        /// <summary>The channels of the audit's side-by-side run. "faction", "say", "group" and "tell" are the bench's own paths.</summary>
        private static readonly string[] AuditChannels =
            ["faction", "say", "group", "group_named", "tell", "guild", "guild_named", "advice", "broadcast", "chatgroup", "companion_say", "trade"];

        private static readonly DateTime AuditEpoch = new(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc);

        /// <summary>
        /// One question as the channel's own handler answers it (audit 1.2.3, report only; the handlers need live bots, so each is
        /// read off the code): /gu is AutonomousBotGuildSocial.OnPlayerGuildChat, /advice OnPlayerAdvice, /b and /c
        /// OnPlayerOpenChannel, a hired companion in /say CompanionChat165.ReplyTo, /trade OnPlayerTradePost's gate.
        /// </summary>
        private static Answer AnswerAs(Run run, string channel)
        {
            Question q = run.Q;
            if (channel is "faction" or "say" or "group" or "tell")
            {
                run.Q = q with { Channel = channel, To = null };
                return AnswerOne(run);
            }
            string message = q.Text.Trim();
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
            string shape = AutonomousChatShortTalk.QuestionShape(message);
            using AutonomousChatFiller.ReplyScope fillerScope = AutonomousChatFiller.Replying(message, channel + ":" + run.Realm,
                (long)(run.Now - AuditEpoch).TotalMilliseconds);
            AutonomousChatReplyPath.Ports ports = PortsOf(run);
            AutonomousBotChatCoordinator.ReplyChoice choice;
            switch (channel)
            {
                case "group_named":
                {
                    string known = Knowledge(run, message);
                    choice = known != null ? new AutonomousBotChatCoordinator.ReplyChoice(Style(run, known), "knowledge")
                        : AutonomousChatReplyPath.Budget(ports, VoicedOf(run, ports, message, true, "group"), message, true, "group");
                    break;
                }
                case "guild":
                case "guild_named":
                {
                    bool named = channel == "guild_named";
                    if (shape == "ack")
                        return AuditDone(run, "none ack", null);
                    if (shape == "ask_inplace")
                        return AuditDone(run, "none not_there (no guildmate is there)", null);
                    if (!named && AutonomousChatScreen.OtherAddressee(message, null) != null)
                        return AuditDone(run, "none other", null);
                    string known = Knowledge(run, message);
                    if (known != null)
                    {
                        choice = new AutonomousBotChatCoordinator.ReplyChoice(Style(run, known), "knowledge");
                        break;
                    }
                    AutonomousBotGuildChatLines.ReplyKind kind = AutonomousBotGuildChatLines.Classify(message, intent);
                    int count = named && kind != AutonomousBotGuildChatLines.ReplyKind.None ? 1
                        : AutonomousBotGuildSocial.ResponderCount(kind, 5, run.Rng.NextDouble());
                    if (count == 0)
                        return AuditDone(run, "none guild kind " + kind, null);
                    choice = AutonomousChatReplyPath.Budget(ports, VoicedOf(run, ports, message, true, "guild"), message, named, "guild");
                    break;
                }
                case "advice":
                {
                    string known = Knowledge(run, message);
                    choice = known != null ? new AutonomousBotChatCoordinator.ReplyChoice(Style(run, known), "knowledge")
                        : VoicedOf(run, ports, message, false, "advice");
                    if (string.IsNullOrWhiteSpace(choice.Text) || AutonomousChatReplyFit.Of(choice.Route, message) < AutonomousChatReplyFit.Specific ||
                        AutonomousChatReplyBudget.IsBrushOff(choice.Text))
                        return AuditDone(run, "silent advice (" + choice.Route + ")", null);
                    break;
                }
                case "broadcast":
                case "chatgroup":
                {
                    if (!AutonomousBotChatCoordinator.NeedsAnswer(message))
                        return AuditDone(run, "none open (asks nothing)", null);
                    if (AutonomousChatScreen.OtherAddressee(message, null) != null)
                        return AuditDone(run, "none other", null);
                    if (AutonomousBotChatCoordinator.AsksToJoinGroup(message) || AutonomousBotChatCoordinator.BroadcastsLfg(message))
                    {
                        run.Stub = true;
                        choice = new AutonomousBotChatCoordinator.ReplyChoice(
                            Style(run, AutonomousBotChatCoordinator.LevelGapReply(run.Me.Level, q.Level, run.Rng) ?? "[world: join reply]"), "join");
                        break;
                    }
                    string known = Knowledge(run, message);
                    choice = known != null ? new AutonomousBotChatCoordinator.ReplyChoice(Style(run, known), "knowledge")
                        : AutonomousChatReplyPath.Budget(ports, VoicedOf(run, ports, message, false, channel), message, false, channel);
                    break;
                }
                case "companion_say":
                    // A hired companion named in /say (or holding the floor): CompanionChat165.Reply's last step, by intent alone.
                    choice = new AutonomousBotChatCoordinator.ReplyChoice(Style(run, CompanionChat165.ReplyTo(intent, "Wynn", run.Rng, false)),
                        "companion intent " + AutonomousBotVoices.IntentName(intent));
                    break;
                case "trade":
                {
                    AutonomousChatMarket.Post post = AutonomousChatMarket.PostOf(message);
                    if (post is not (AutonomousChatMarket.Post.Wts or AutonomousChatMarket.Post.Wtb))
                        return AuditDone(run, "none trade (not a wts or wtb post)", null);
                    run.Stub = true;
                    choice = new AutonomousBotChatCoordinator.ReplyChoice("[world: a market reply at its share]", "market " + post);
                    break;
                }
                default:
                    return AuditDone(run, "none channel", null);
            }
            return AuditDone(run, choice.Route, choice.Text);
        }

        private static Answer AuditDone(Run run, string route, string text)
        {
            string outcome = text == null ? "silent"
                : route.StartsWith("rescue (", StringComparison.Ordinal) ? "rescue"
                : route.StartsWith("short (", StringComparison.Ordinal) ? "short"
                : route.StartsWith("ack (", StringComparison.Ordinal) ? "ack"
                : "answer";
            return new Answer(run.Q.Id, route, text, run.Stub || text != null && text.StartsWith("[world", StringComparison.Ordinal),
                AutonomousChatReplyFit.Of(route, run.Q.Text), outcome, text == null ? 0 : 1, 0, false);
        }

        private static Run AuditRun(Bench bench, Question q, VC.RecentLines recent, DateTime now)
        {
            Responder me = bench.Responders[q.Realm];
            int seed = q.Text.Aggregate(17, (h, c) => unchecked(h * 31 + c));
            return new Run
            {
                Q = q, Me = me, Facts = FactsOf(me, q.Realm), Realm = q.Realm, Rng = new Random(seed), Corpus = AutonomousBotVoices.Corpus, Recent = recent,
                Now = now, World = bench.World, Towns = bench.Towns,
            };
        }

        private static string AuditPath(string file)
        {
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-bench", file);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            return path;
        }

        /// <summary>
        /// Audit 1.2.3, report only: every /faction question of the bench asked in every channel (the same words, the same seed, a
        /// fresh channel each), written one JSON row a line to chat-bench/channel-rows.jsonl (CI prints them as CHAT_CHANNEL_ROW).
        /// </summary>
        [Test]
        public void Audit_TheSameQuestionsInEveryChannel()
        {
            Bench bench = Load();
            Question[] questions = bench.Questions.Where(q => q.Channel == "faction" && string.IsNullOrEmpty(q.To)).ToArray();
            var rows = new List<string>();
            foreach (string channel in AuditChannels)
            {
                var recent = new VC.RecentLines();
                DateTime now = AuditEpoch;
                foreach (Question q in questions)
                {
                    Run run = AuditRun(bench, q, recent, now);
                    now = now.AddMinutes(3);
                    Answer a = AnswerAs(run, channel);
                    rows.Add(JsonSerializer.Serialize(new { ch = channel, id = q.Id, route = a.Route, reply = a.Reply, stub = a.Stub, outcome = a.Outcome }));
                }
            }
            File.WriteAllLines(AuditPath("channel-rows.jsonl"), rows);
            Assert.That(rows.Count, Is.EqualTo(questions.Length * AuditChannels.Length));
        }

        /// <summary>
        /// Audit 1.2.3, report only: follow-ups that lean on the line before ("how do i get there", "what lvl is it", "where is
        /// he"). For each phrasing: what the thread path reads it as (AutonomousChatThreads.FollowUpWord), what the conversation
        /// memory completes it to (ChatConversations.Resolve, after the player named a place, an item or a class), and what
        /// /faction, /group and a tell then answer. Rows go to chat-bench/context-rows.jsonl (CHAT_CONTEXT_ROW in CI).
        /// </summary>
        [Test]
        public void Audit_FollowUpsThatLeanOnTheLineBefore()
        {
            Bench bench = Load();
            (string Setup, string[] FollowUps)[] sets =
            [
                ("anyone been to the barrows?",
                new[]
                {
                    "how do i get there", "how do i get there?", "how do you get there from camelot?", "where is that", "where is that?", "wheres that",
                    "where's that at?", "where is it", "and where is that", "ty, where is it", "never heard of it, where?", "where?", "where exactly",
                    "is it far?", "how far is it", "how far is that?", "which way?", "what lvl is it?", "what level is that?", "what lvl do i need for it",
                    "is it any good?", "is it good xp?", "any good?", "whats there?", "what's in there?", "what drops there?", "is it hard?",
                    "can i solo it?", "can i solo there?", "anyone there now?", "is that a dungeon?", "what zone is that in?", "ok how do i find it",
                    "is there a group going there?", "whats the best way there",
                }),
                ("wtb a studded helm",
                new[]
                {
                    "how much for it?", "how much for one?", "how much?", "what stats on it?", "who sells them?", "where do i get one?", "can u make one?",
                    "who makes those?",
                }),
                ("thinking of rolling a minstrel",
                new[]
                {
                    "are they good solo?", "what spec for one?", "who trains them?", "what armor do they wear?", "what race is best for one?",
                }),
                ("who is the green knight?",
                new[]
                {
                    "where is he?", "where does he spawn?", "what lvl is he?", "what does he drop?", "can we kill him?", "how many to kill him?",
                }),
            ];
            var rows = new List<string>();
            var recent = new VC.RecentLines();
            DateTime now = AuditEpoch;
            foreach ((string setup, string[] followUps) in sets)
                foreach (string followUp in followUps)
                {
                    var conversations = new ChatConversations();
                    conversations.NotePlayer("Wynn", "faction:Albion", setup, 1_000);
                    ChatConversations.Conversation held = conversations.In("Wynn", "faction:Albion", 2_000);
                    string resolved = ChatConversations.Resolve(followUp, held);
                    string word = AutonomousChatThreads.FollowUpWord(followUp);
                    var replies = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (string channel in new[] { "faction", "group", "tell" })
                    {
                        var q = new Question("ctx", resolved, channel, eRealm.Albion, "Armsman", 24, "context", null);
                        Run run = AuditRun(bench, q, recent, now);
                        now = now.AddMinutes(3);
                        Answer a = AnswerAs(run, channel);
                        replies[channel] = (a.Reply ?? "(nothing)") + "  <" + a.Route + ">";
                    }
                    rows.Add(JsonSerializer.Serialize(new
                    {
                        setup, said = followUp, word, place = held?.Place, item = held?.Item, cls = held?.ClassName,
                        resolved = resolved == followUp ? null : resolved, faction = replies["faction"], group = replies["group"], tell = replies["tell"],
                    }));
                }
            File.WriteAllLines(AuditPath("context-rows.jsonl"), rows);
            Assert.That(rows, Is.Not.Empty);
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
