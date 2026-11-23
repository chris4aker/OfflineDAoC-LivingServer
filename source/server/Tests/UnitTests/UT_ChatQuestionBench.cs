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
    /// Mirrors the coordinator; keep the two in step, as ChatAuditRoute does.
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

        internal static Bench Load()
        {
            var assembly = typeof(UT_ChatQuestionBench).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith("player_questions.json", StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            using JsonDocument doc = JsonDocument.Parse(reader.ReadToEnd());
            JsonElement root = doc.RootElement;
            var bench = new Bench();
            static string Str(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            bench.Questions = root.GetProperty("questions").EnumerateArray().Select(q => new Question(Str(q, "id"), Str(q, "text"), Str(q, "channel"),
                RealmOf(Str(q, "realm")), Str(q, "cls"), q.GetProperty("level").GetInt32(), Str(q, "intent"), Str(q, "to"))).ToArray();
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

        private static string Pick(Run run, string intent = null, string answers = null, Func<VC.Line, bool> filter = null, long? id = null)
        {
            run.Now = run.Now.AddMinutes(1);
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["player"] = "Wynn", ["me"] = run.Me.Name, ["class"] = run.Me.Cls.ToLowerInvariant(), ["level"] = run.Me.Level.ToString(),
                ["realm"] = GlobalConstants.RealmToName(run.Realm), ["zone"] = run.Me.Zone,
            };
            AutonomousChatFiller.ReplyScope scope = AutonomousChatFiller.Current;
            bool picked = run.Corpus.TryPick(new VC.Request("reply", SpeakerOf(run, id ?? run.Me.Id), slots, run.Recent, run.Now, run.Rng)
            {
                Intent = intent, Answers = answers, Filter = line => AutonomousChatFiller.ReplyAllowed(line, scope) && (filter == null || filter(line)),
                RelaxTo = TimeSpan.FromMinutes(40), LocalHour = 20,
            }, out VC.Picked chosen);
            if (picked && scope != null && AutonomousChatFiller.IsGeneric(chosen.Text))
                AutonomousChatFiller.NoteGeneric(scope.ChannelKey, scope.Now);
            return picked ? chosen.Text : null;
        }

        private static string Style(Run run, string text) =>
            text == null ? null : AutonomousBotVoices.StyleText(run.Corpus.VoiceFor(run.Me.Id), run.Me.Id, text, run.Rng);

        private static bool Short(string message, eAutonomousChatIntent intent) =>
            !message.Contains('?') && intent is eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Congrats or eAutonomousChatIntent.Ding or
                eAutonomousChatIntent.Laugh or eAutonomousChatIntent.Away or eAutonomousChatIntent.Back or eAutonomousChatIntent.Greeting or
                eAutonomousChatIntent.Farewell or eAutonomousChatIntent.BotCheck;

        /// <summary>AutonomousChatKnowledge.TryAnswer without the world: its pure steps in order, the world steps stubbed.</summary>
        private static string Knowledge(Run run, string query)
        {
            if (AutonomousChatKnowledge.TryExplainTerm(query, out string answer))
                return answer;
            if (AutonomousChatKnowledge.AsksHowFar(query) && AutonomousChatQuestions.PlaceNamed(query, AutonomousChatPlaces.All) != null)
            {
                run.Stub = true;
                return "[world: how far, from where the player stands]";
            }
            if (LoreLegends.TryAnswer(run.Corpus, query, run.Realm, run.Rng, out answer))
                return answer;
            if (AutonomousChatKnowledge.TryAnswerTicketsInGeneral(query, out answer))
                return answer;
            if (AutonomousChatKnowledge.TryAnswerDragon(run.Realm, query, out answer))
                return answer;
            if (LoreChat.TryAnswer(LoreBible.Current, run.Corpus.Lines, query, run.Rng, out answer, run.Realm))
                return answer;
            if (AutonomousChatKnowledge.TryAnswerTimeToCap(query, out answer))
                return answer;
            if (AutonomousChatKnowledge.AsksAboutRealmRaid(query) &&
                AutonomousChatKnowledge.TryAnswerRealmRaid(GlobalConstants.RealmToName(run.Realm), run.Q.Level, Array.Empty<AutonomousRealmRaid.Summary>(), out answer))
                return answer;
            if (AutonomousChatKnowledge.LooksLikeTrainerQuestion(query) && Trainer(run, query) is { } trainer)
                return trainer;
            if (AutonomousChatKnowledge.TryAnswerHowTo(query, out answer))
                return answer;
            if (AutonomousChatKnowledge.TryAnswerCommon(query, run.Realm, run.Q.Cls, run.Q.Level, run.Rng, out answer))
                return answer;
            if (AutonomousChatKnowledge.TryAnswerBuyGear(run.Realm, query, out answer))
                return answer;
            if (AutonomousChatKnowledge.LooksLikeLocationQuestion(query) && Location(run, query) is { } where)
                return where;
            return null;
        }

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

        private static readonly eAutonomousChatIntent[] NotUnsure =
        [
            eAutonomousChatIntent.Greeting, eAutonomousChatIntent.Thanks, eAutonomousChatIntent.Rez, eAutonomousChatIntent.Alarm,
            eAutonomousChatIntent.Grouping, eAutonomousChatIntent.BotCheck, eAutonomousChatIntent.Abuse, eAutonomousChatIntent.Farewell,
            eAutonomousChatIntent.Ding, eAutonomousChatIntent.Congrats, eAutonomousChatIntent.Back, eAutonomousChatIntent.Away,
            eAutonomousChatIntent.Roleplay,
        ];

        /// <summary>VoicedChoiceCore without a live bot: (route, text).</summary>
        private static (string Route, string Text) Voiced(Run run, string incoming, bool addressed, string channel)
        {
            eAutonomousChatIntent value = AutonomousChatIntentModel.Predict(incoming);
            string intent = AutonomousBotVoices.IntentName(value);
            // AutonomousChatLiveAnswers.AnswerQuestion
            if (value is not (eAutonomousChatIntent.BotCheck or eAutonomousChatIntent.Abuse))
            {
                string self = addressed ? AutonomousChatQuestions.SelfQuestion(incoming) : null;
                string known = self != null ? AutonomousChatQuestions.AnswerSelf(self, incoming, run.Facts, run.Rng)
                    : AutonomousChatQuestions.AnswerWorld(incoming, run.Realm, run.World, run.Rng);
                if (known != null)
                    return ("fact " + (addressed ? self ?? "world" : "world"), Style(run, known));
            }
            string shape = AutonomousChatShortTalk.QuestionShape(incoming);
            var facts = new AutonomousChatContext.Facts(run.Me.Name, "Wynn", channel, channel is "say" or "group", Healers.Contains(run.Me.Cls), false,
                run.Me.Level, null, AutonomousBotVoices.CapitalOf(run.Realm));
            if (AutonomousChatContext.Reply(incoming, value, shape, facts, run.Rng) is { } situation)
                return (situation.Route, Style(run, situation.Text));
            if (shape == "player_order" && AutonomousChatQuestions.CantDo(incoming, run.Facts) is { } cant)
                return ("fact cant", Style(run, cant));
            // TryShapedReply
            if (shape == "ask_howareyou")
            {
                string hello = Pick(run, intent: "greeting", filter: l => AutonomousChatShortTalk.AsksHowAreYou(incoming) || !l.Answers.Contains("ask_howareyou"));
                if (hello != null)
                    return ("shape ask_howareyou", hello);
            }
            else if (shape is "ask_whereyou" or "ask_hunt" or "ask_enemy")
            {
                string live = shape switch
                {
                    "ask_whereyou" => AutonomousBotChatCoordinator.WhereText(run.Me.Zone, false, run.Me.Level, run.Me.Cls, run.Rng),
                    "ask_hunt" => WhereToHunt(run, incoming),
                    _ => null,
                };
                if (shape == "ask_enemy")
                {
                    run.Stub = true;
                    return ("live ask_enemy", "[world: enemy sightings from the realm's news board]");
                }
                if (live != null)
                    return ("live " + shape, Style(run, live));
            }
            else if (shape == "ask_help")
            {
                string help = Pick(run, intent: "help", filter: l => l.Text.TrimEnd().EndsWith('?') || l.Text.Contains("whats up", StringComparison.OrdinalIgnoreCase) ||
                                                                l.Text.Contains("what do", StringComparison.OrdinalIgnoreCase));
                if (help != null)
                    return ("shape ask_help", help);
            }
            else if (shape is not (null or "ack" or "ask_inplace"))
            {
                string answers = shape == "ask_price" ? "trade" : shape;
                string shaped = Pick(run, answers: answers, filter: l => AutonomousBotVoices.ShapedFits(shape, incoming, l.Text));
                if (shaped != null)
                    return ("shape " + shape, shaped);
            }
            if (shape is "ack" or "ask_inplace")
                return ("none " + (shape == "ack" ? "ack" : "not_there"), null);
            if (AutonomousChatShortTalk.IsFollowUp(incoming))
                return ("none followup", null);
            if (AutonomousChatQuestions.IsFactual(incoming) && !NotUnsure.Contains(value))
            {
                if (run.Rng.NextDouble() < 0.5 && Pick(run, answers: "ask", filter: l => !l.Text.Contains("trainer") && !l.Text.Contains("guard")) is { } honest)
                    return ("not sure", honest);
                return ("none factual", null);
            }
            if (value == eAutonomousChatIntent.Unknown)
            {
                if (incoming.Contains('?') && Pick(run, answers: "ask", filter: l => !l.Text.Contains("trainer") && !l.Text.Contains("guard")) is { } unsure)
                    return ("not sure", unsure);
                return ("none unknown", null);
            }
            if (value == eAutonomousChatIntent.Roleplay)
            {
                string act = AutonomousChatShortTalk.RoleplayAct(incoming);
                return ("intent roleplay", Pick(run, answers: "player_rp_" + act) ?? Pick(run, answers: "player_rp_emote") ?? "*nods*");
            }
            if (value == eAutonomousChatIntent.ClassTalk)
            {
                if (AutonomousChatShortTalk.AsksSpec(incoming))
                {
                    string cls = AutonomousChatFieldTalk.ClassNamed(incoming) ?? run.Me.Cls;
                    string[] pool = AutonomousChatFieldTalk.SpecAdvice(cls.ToLowerInvariant());
                    if (pool.Length > 0)
                        return ("knowledge spec", Style(run, pool[run.Rng.Next(pool.Length)]));
                }
                string[] asked = AutonomousChatShortTalk.ClassWordsIn(incoming);
                if (asked.Length > 0 && Pick(run, intent: intent, filter: l => AutonomousChatShortTalk.ClassWordsIn(l.Text)
                        .Any(w => asked.Any(a => AutonomousChatShortTalk.SameClassWord(w, a)))) is { } about)
                    return ("intent classtalk asked", about);
                if (Pick(run, intent: intent, filter: l => !AutonomousChatShortTalk.NamesAClass(l.Text)) is { } general)
                    return ("intent classtalk", general);
            }
            else if (value == eAutonomousChatIntent.Greeting)
            {
                if (Pick(run, intent: "greeting", filter: l => AutonomousChatShortTalk.AsksHowAreYou(incoming) || !l.Answers.Contains("ask_howareyou")) is { } hello)
                    return ("intent greeting", hello);
            }
            string topicIntent = value is eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.RvR && !AutonomousChatQuestions.IsQuestion(incoming) &&
                                 !AutonomousBotChatCoordinator.PlansSomething(incoming)
                ? AutonomousBotVoices.IntentName(eAutonomousChatIntent.Banter)
                : intent;
            if (value is not (eAutonomousChatIntent.ClassTalk or eAutonomousChatIntent.Greeting) &&
                Pick(run, intent: topicIntent, filter: l => AutonomousChatShortTalk.ReplyFits(incoming, l.Text) && AutonomousChatShortTalk.TradeReplyFits(incoming, l.Text) &&
                    !(value is eAutonomousChatIntent.Banter or eAutonomousChatIntent.Dungeon or eAutonomousChatIntent.RvR &&
                      System.Text.RegularExpressions.Regex.IsMatch(l.Text, @"sure (u|you) did|bet (u|you) cant|bet you can't|pics or|show off",
                          System.Text.RegularExpressions.RegexOptions.IgnoreCase))) is { } voiced)
                return ("intent " + topicIntent, voiced);
            if (incoming.Contains('?') && value is not (eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or eAutonomousChatIntent.Thanks or
                    eAutonomousChatIntent.Back or eAutonomousChatIntent.Away or eAutonomousChatIntent.Laugh or eAutonomousChatIntent.Roleplay) &&
                Pick(run, answers: "ask", filter: l => !l.Text.Contains("trainer") && !l.Text.Contains("guard")) is { } notSure)
                return ("intent " + intent + " -> not sure", notSure);
            return ("none " + intent, null);
        }

        /// <summary>Budgeted without a live bot: (route, text, fit, outcome).</summary>
        private static (string Route, string Text, int Fit, string Outcome) Budgeted(Run run, string route, string text, string message, bool addressed, string channel)
        {
            if (AutonomousChatScreen.IsSpam(message))
                return ("silent (none spam)", null, 0, "silent");
            bool heals = Healers.Contains(run.Me.Cls);
            if (AutonomousChatReplyBudget.CreditFor(message) != null && AutonomousChatReplyBudget.ClaimsCredit(message, text, heals, heals))
                (route, text) = ("none credit", null);
            if (text != null && AutonomousChatQuestions.ContradictsSelf(text, run.Facts))
                (route, text) = ("none contradicts (" + route + ")", null);
            else if (text != null && AutonomousChatReplyFit.Of(route, message) < AutonomousChatReplyFit.Known && AutonomousChatReplyFit.WrongSituation(message, text))
                (route, text) = ("none situation (" + route + ")", null);
            int fit = AutonomousChatReplyFit.Of(route, message);
            if (string.IsNullOrWhiteSpace(text) || AutonomousChatReplyBudget.IsBrushOff(text))
                fit = Math.Min(fit, AutonomousChatReplyFit.Weak);
            var outcome = AutonomousChatReplyBudget.Decide(fit, addressed, AutonomousChatBudget.MinReplyFit);
            if (outcome == AutonomousChatReplyBudget.Outcome.Answer)
                return (route, text, fit, "answer");
            if (route is "none ack" or "none spam" or "none other")
                return ("silent (" + route + ")", null, fit, "silent");
            var moment = new AutonomousChatReplyBudget.Moment(message, channel, addressed, AutonomousChatIntentModel.Predict(message), run.Me.Cls, heals, heals,
                false, null, run.Facts);
            if (outcome == AutonomousChatReplyBudget.Outcome.Ack)
                return AutonomousChatReplyBudget.ToContent(moment, run.Rng) is { } content
                    ? ("short (" + route + ")", Style(run, content), fit, "short")
                    : ("ack (" + route + ")", AutonomousChatReplyBudget.Ack(run.Me.Id, false, run.Rng), fit, "ack");
            return AutonomousChatReplyBudget.Rescue(moment, route, run.Rng) is { } rescue
                ? ("rescue (" + route + ")", Style(run, rescue), fit, "rescue")
                : ("silent (" + route + ")", null, fit, "silent");
        }

        /// <summary>One question through the path its channel takes.</summary>
        private static Answer AnswerOne(Run run)
        {
            Question q = run.Q;
            string channel = q.Channel;
            bool faction = channel == "faction";
            bool tell = channel == "tell";
            bool group = channel == "group";
            string message = q.Text.Trim();
            bool named = !string.IsNullOrEmpty(q.To) && (channel is "faction" or "say") &&
                         AutonomousChatThreads.WithoutName(message, q.To) is { Length: > 0 } unnamed && unnamed != message;
            if (named)
                message = AutonomousChatThreads.WithoutName(message, q.To);
            bool directed = named || tell || group;
            if (AutonomousChatScreen.IsSpam(message))
                return Done(run, "none spam", null, 0, "silent", 0);
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
            string shape = AutonomousChatShortTalk.QuestionShape(message);
            using AutonomousChatFiller.ReplyScope fillerScope = AutonomousChatFiller.Replying(message, (faction ? "faction:" : channel + ":") + run.Realm,
                (long)(run.Now - new DateTime(2026, 11, 22, 20, 0, 0, DateTimeKind.Utc)).TotalMilliseconds);
            string route, text;
            bool budgeted = true;
            // TellReply's own checks first; /group and a named line go to VoicedChoice (addressed), after the knowledge lookup.
            if (tell && AutonomousBotChatCoordinator.SaysInvited(message))
                (route, text, budgeted) = ("tell invited", "[world: got the invite or not]", false);
            else if (tell && AutonomousBotChatCoordinator.AsksToJoinGroup(message))
                (route, text, budgeted) = ("join", AutonomousBotChatCoordinator.LevelGapReply(run.Me.Level, q.Level, run.Rng) ?? "[world: join reply]", false);
            else if (tell && AutonomousBotChatCoordinator.AsksWhereYouAre(message))
                (route, text, budgeted) = ("live where", Style(run, AutonomousBotChatCoordinator.WhereText(run.Me.Zone, false, run.Me.Level, run.Me.Cls, run.Rng)), false);
            else
            {
                bool shortTalk = Short(message, intent);
                bool helpCall = AutonomousBotChatCoordinator.IsHelpCall(message, shape);
                string knowledge = null;
                if (tell)
                {
                    // TellReply: the bot's own facts, then the knowledge lookup.
                    (string factRoute, string fact) = FactFirst(run, message);
                    if (fact != null)
                        (route, text) = (factRoute, fact);
                    else if (!shortTalk && Knowledge(run, message) is { } k)
                        (route, text) = ("knowledge", Style(run, k));
                    else
                        (route, text) = Voiced(run, message, true, "tell");
                }
                else
                {
                    knowledge = !shortTalk && !helpCall && !group ? Knowledge(run, message) : null;
                    if (shape == "ack")
                        return Done(run, "none ack", null, 0, "silent", 0);
                    if (shape == "ask_inplace" && knowledge == null)
                        return Done(run, "none not_there (no bot is there)", null, 0, "silent", 0);
                    if (!directed && shape is "player_wb" or "player_order")
                        return Done(run, "none undirected", null, 0, "silent", 0);
                    if (!directed && AutonomousChatScreen.OtherAddressee(message, null) is not null)
                        return Done(run, "none other", null, 0, "silent", 0);
                    if (AutonomousBotChatCoordinator.AsksToJoinGroup(message) && (directed || !faction && !AutonomousBotChatCoordinator.BroadcastsLfg(message)))
                        return Done(run, "join", AutonomousBotChatCoordinator.LevelGapReply(run.Me.Level, q.Level, run.Rng) ?? "[world: join reply]",
                            AutonomousChatReplyFit.Known, "answer", 1, stub: true);
                    if (!directed && intent == eAutonomousChatIntent.Grouping)
                    {
                        int level = AutonomousChatLiveAnswers.LevelAsked(message, q.Level);
                        if (Math.Abs(run.Me.Level - level) > 6)
                            return run.Rng.NextDouble() < AutonomousBotChatCoordinator.NoneNearLevelReplyChance
                                ? Done(run, "live none_near_level", Style(run, AutonomousBotChatCoordinator.NoneNearLevelReply(level,
                                    AutonomousChatShortTalk.NeededRole(message), run.Rng)), AutonomousChatReplyFit.Known, "answer", 1)
                                : Done(run, "none no bot near the level", null, 0, "silent", 0);
                    }
                    else if (intent == eAutonomousChatIntent.Congrats)
                        return Done(run, "none gz (only a bot that just levelled)", null, 0, "silent", 0);
                    else if (intent == eAutonomousChatIntent.Thanks && faction)
                        return Done(run, "none ty (groupmates only)", null, 0, "silent", 0);
                    if (helpCall)
                    {
                        (string come, string instead) = AutonomousBotChatCoordinator.HelpRequestLines(AutonomousChatQuestions.PlaceNamed(message, AutonomousChatPlaces.All)?.Key, run.Rng);
                        (route, text, budgeted) = ("context help", Style(run, instead), false);
                        _ = come;
                    }
                    else if (knowledge != null)
                        (route, text, budgeted) = ("knowledge", Style(run, knowledge), false);
                    else
                        (route, text) = Voiced(run, message, directed || !faction, channel);
                }
            }
            int fit = AutonomousChatReplyFit.Of(route, message);
            string outcome = text == null ? "silent" : "answer";
            if (budgeted)
                (route, text, fit, outcome) = Budgeted(run, route, text, message, tell || named, channel);
            // How many bots answer (SchedulePlayerReplies' count and the arbiter's plan), with five bots of the realm to hear it.
            int answerers = 0;
            bool twice = false;
            if (text != null)
            {
                if (directed)
                    answerers = 1;
                else
                {
                    int count = route == "knowledge" ? 1 : AutonomousBotChatCoordinator.PlayerReplyCount(intent, faction, false, 5, run.Rng);
                    if (shape is "ask_hunt" or "ask_enemy" or "ask_price" or "player_offer" or "player_wait" || AutonomousChatQuestions.IsFactual(message))
                        count = Math.Min(count, 1);
                    var plan = AutonomousChatArbiter.Arrange(count, AutonomousChatReplyBudget.IsAlarm(message, intent) ? eAutonomousChatIntent.Alarm : intent, route,
                        false, AutonomousChatQuestions.IsQuestion(message), 5, AutonomousChatBudget.Current.PileOnCap, run.Rng);
                    answerers = Math.Max(1, plan.Total);
                    // A social pile-on draws its own line: the same words twice in a row read as an echo.
                    if (plan.Social > 0 && route.StartsWith("intent ", StringComparison.Ordinal))
                        twice = Pick(run, intent: AutonomousBotVoices.IntentName(intent), id: run.Me.Id + 1) is { } second &&
                                string.Equals(second.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase);
                }
            }
            return Done(run, route, text, fit, outcome, answerers, twice: twice);
        }

        /// <summary>AutonomousChatLiveAnswers.AnswerQuestion(addressed: true), as TellReply asks it before the knowledge lookup.</summary>
        private static (string Route, string Text) FactFirst(Run run, string message)
        {
            eAutonomousChatIntent intent = AutonomousChatIntentModel.Predict(message);
            if (intent is eAutonomousChatIntent.BotCheck or eAutonomousChatIntent.Abuse)
                return (null, null);
            string self = AutonomousChatQuestions.SelfQuestion(message);
            string known = self != null ? AutonomousChatQuestions.AnswerSelf(self, message, run.Facts, run.Rng)
                : AutonomousChatQuestions.AnswerWorld(message, run.Realm, run.World, run.Rng);
            return known == null ? (null, null) : ("fact " + (self ?? "world"), Style(run, known));
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
        public void AnswersEveryQuestionAndWritesTheReport()
        {
            Bench bench = Load();
            Assert.That(bench.Questions.Length, Is.GreaterThanOrEqualTo(300), "the bench holds at least 300 player questions");
            List<Answer> answers = AnswerAll(bench);
            Assert.That(answers.Select(a => a.Id), Is.EqualTo(bench.Questions.Select(q => q.Id)), "every question gets a route");
            string path = Environment.GetEnvironmentVariable("CHAT_BENCH_OUT") is { Length: > 0 } output
                ? output
                : Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-bench", "bench-replies.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(answers.Select(a => new
            {
                id = a.Id, route = a.Route, reply = a.Reply, stub = a.Stub, fit = a.Fit, outcome = a.Outcome, answerers = a.Answerers, delay_ms = a.DelayMs,
                twice = a.Twice,
            }), new JsonSerializerOptions { WriteIndented = true }));
            int answered = answers.Count(a => a.Reply != null);
            TestContext.Progress.WriteLine($"CHAT_BENCH {answers.Count} questions, {answered} with a reply, written to {path}");
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
