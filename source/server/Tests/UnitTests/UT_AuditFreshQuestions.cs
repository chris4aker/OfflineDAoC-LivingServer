using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Realism audit 1.2.3, report only (never merged): the fresh player lines of tools/bot-voices/eval/audit_fresh_questions.json
    /// through UT_ChatQuestionBench's path (the same responders and stub world). Writes chat-bench/bench-fresh.json; the CI step
    /// "Audit: fresh questions" scores it and prints every row to the log.
    /// </summary>
    [TestFixture]
    public class UT_AuditFreshQuestions
    {
        [Test]
        public void AnswersTheFreshSet()
        {
            UT_ChatQuestionBench.Bench bench = UT_ChatQuestionBench.Load();
            var assembly = typeof(UT_AuditFreshQuestions).Assembly;
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(".audit_fresh_questions.json", StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using JsonDocument doc = JsonDocument.Parse(stream);
            static string Str(JsonElement e, string key) => e.TryGetProperty(key, out JsonElement v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
            static eRealm RealmOf(string code) => code switch { "alb" => eRealm.Albion, "mid" => eRealm.Midgard, "hib" => eRealm.Hibernia, _ => eRealm.None };
            bench.Questions = doc.RootElement.GetProperty("questions").EnumerateArray().Select(q => new UT_ChatQuestionBench.Question(Str(q, "id"),
                Str(q, "text"), Str(q, "channel"), RealmOf(Str(q, "realm")), Str(q, "cls"), q.GetProperty("level").GetInt32(), Str(q, "intent"), Str(q, "to"))).ToArray();
            List<UT_ChatQuestionBench.Answer> answers = UT_ChatQuestionBench.AnswerAll(bench);
            string path = Path.Combine(TestContext.CurrentContext.WorkDirectory, "chat-bench", "bench-fresh.json");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(answers.Select(a => new
            {
                id = a.Id, route = a.Route, reply = a.Reply, stub = a.Stub, fit = a.Fit, outcome = a.Outcome, answerers = a.Answerers, delay_ms = a.DelayMs,
                twice = a.Twice,
            }), new JsonSerializerOptions { WriteIndented = true }));
            TestContext.Progress.WriteLine($"AUDIT_FRESH {answers.Count} lines, {answers.Count(a => a.Reply != null)} with a reply, written to {path}");
            Assert.That(answers.Count, Is.EqualTo(bench.Questions.Length));
        }
    }
}
