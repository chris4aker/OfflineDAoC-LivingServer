using System.Linq;
using NUnit.Framework;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Realism 2b (#479), step 1, on the bench's stub world: a hired companion in /say and a groupmate in /group answer through the
    /// party reply path (AutonomousChatReplyPath.Party), as the live handlers do. Tests only: no world, no live bots.
    /// </summary>
    public partial class UT_ChatQuestionBench
    {
        private static Answer PartyAnswer(Bench bench, string channel, string text)
        {
            var q = new Question("p479", text, "faction", eRealm.Albion, "Armsman", 24, "party", null);
            return AnswerAs(RunOf(bench, q, new VC.RecentLines(), Epoch), channel);
        }

        [Test]
        public void A_companion_in_say_answers_from_what_it_knows_not_by_intent_alone()
        {
            Bench bench = Load();
            // channels.md finding 1: each of these drew "sure Wynn", "ok" or "Point me at the next one."
            foreach (string line in new[] { "where is the cleric trainer?", "can someone rez me", "add!", "back", "where should i level at 3?" })
            {
                Answer a = PartyAnswer(bench, "companion_say", line);
                Assert.That(a.Reply ?? string.Empty, Does.Not.Match(@"^\W*(sure wynn|k|ok|point me at the next one)\W*$").IgnoreCase, line);
                Assert.That(a.Route, Does.Not.StartWith("companion intent"), line);
            }
            Answer trainer = PartyAnswer(bench, "companion_say", "where is the cleric trainer?");
            Assert.That(trainer.Route, Does.StartWith("knowledge"), "the knowledge lookup a groupmate uses in /group");
        }

        [Test]
        public void A_groupmate_answers_where_the_player_is_and_a_pause()
        {
            Bench bench = Load();
            // channels.md finding 4: each of these drew nothing in /group.
            foreach (string line in new[] { "im by camp station", "im at the bridge", "omw", "be there in 5", "oom", "need to rest", "med break", "rdy" })
                foreach (string channel in new[] { "group", "group_named", "companion_say" })
                {
                    Answer a = PartyAnswer(bench, channel, line);
                    Assert.That(a.Reply, Is.Not.Null.And.Not.Empty, $"{channel}: {line}");
                    Assert.That(a.Route, Is.AnyOf("live meetup", "live pause"), $"{channel}: {line}");
                }
        }

        [Test]
        public void A_companion_beside_the_player_says_k_to_where_they_are()
        {
            Bench bench = Load();
            Answer a = PartyAnswer(bench, "companion_say", "im at the bridge");
            Assert.That(a.Reply.Trim().TrimEnd('.', '!').ToLowerInvariant(), Does.Match(@"^(k|kk|ok)\b"));
        }
    }
}
