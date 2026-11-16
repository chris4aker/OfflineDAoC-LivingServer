using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Open-field RvR tactics, work package 0 (docs/RVR-FIELD-1.2.md, "Skill tiers"): each bot group is Casual, Average or
    /// Veteran, 30/50/20, fixed by its id; the leader's personality and the rvr_bot_awareness setting move the mix. Pure.
    /// </summary>
    [TestFixture]
    public class UT_RvrGroupSkill
    {
        private const int Groups = 20_000;

        private static (double Casual, double Average, double Veteran) Mix(RvrBotAwareness awareness, BotPersonality? leader = null)
        {
            int[] counts = new int[3];
            for (long id = 1; id <= Groups; id++)
                counts[(int)RvrGroupSkill.For(id * 7919 + 13, leader, awareness)]++;
            return (counts[0] * 100.0 / Groups, counts[1] * 100.0 / Groups, counts[2] * 100.0 / Groups);
        }

        private static BotPersonality Leader(BotPersonalityType type, int patience) =>
            new(type, new BotPersonalityTraits(Curiosity: 5, Risk: 5, Sociability: 5, Patience: patience, Chattiness: 5));

        [Test]
        public void Groups_are_thirty_fifty_twenty_at_normal()
        {
            (double casual, double average, double veteran) = Mix(RvrBotAwareness.Normal);
            Assert.Multiple(() =>
            {
                Assert.That(casual, Is.EqualTo(30).Within(1.5));
                Assert.That(average, Is.EqualTo(50).Within(1.5));
                Assert.That(veteran, Is.EqualTo(20).Within(1.5));
                Assert.That(RvrGroupSkill.Shares(RvrBotAwareness.Normal), Is.EqualTo((30, 50, 20)));
            });
        }

        [Test]
        public void Session_ids_give_the_same_mix()
        {
            int[] counts = new int[3];
            for (int number = 1; number <= Groups; number++)
                counts[(int)RvrGroupSkill.For($"albion-a1b2c3-{number:000}", null, RvrBotAwareness.Normal)]++;
            Assert.Multiple(() =>
            {
                Assert.That(counts[0] * 100.0 / Groups, Is.EqualTo(30).Within(1.5));
                Assert.That(counts[1] * 100.0 / Groups, Is.EqualTo(50).Within(1.5));
                Assert.That(counts[2] * 100.0 / Groups, Is.EqualTo(20).Within(1.5));
            });
        }

        [Test]
        public void A_group_keeps_its_tier()
        {
            for (long id = 1; id <= 500; id++)
            {
                RvrSkillTier first = RvrGroupSkill.For(id, null, RvrBotAwareness.Normal);
                Assert.That(RvrGroupSkill.For(id, null, RvrBotAwareness.Normal), Is.EqualTo(first));
                Assert.That(RvrGroupSkill.Roll(id), Is.InRange(0, 100).And.LessThan(100));
            }
            Assert.Multiple(() =>
            {
                Assert.That(RvrGroupSkill.For("midgard-ffffff-042", null, RvrBotAwareness.Normal),
                    Is.EqualTo(RvrGroupSkill.For("midgard-ffffff-042", null, RvrBotAwareness.Normal)));
                // a fixed value, so a change of the hash (which would re-roll every group on an update) shows here
                Assert.That(RvrGroupSkill.Key("midgard-ffffff-042"), Is.EqualTo(6134851015094546477L));
                Assert.That(RvrGroupSkill.Key("albion-000000-001"), Is.Not.EqualTo(RvrGroupSkill.Key("albion-000000-002")));
                Assert.That(RvrGroupSkill.Key(null), Is.EqualTo(RvrGroupSkill.Key(string.Empty)));
                Assert.That(Enumerable.Range(1, 200).Select(id => RvrGroupSkill.For(id, null, RvrBotAwareness.Normal)).Distinct().Count(),
                    Is.EqualTo(3), "all three tiers occur");
            });
        }

        [Test]
        public void The_awareness_setting_moves_the_mix()
        {
            (double easyCasual, double easyAverage, double easyVeteran) = Mix(RvrBotAwareness.Easy);
            (double vetCasual, double vetAverage, double vetVeteran) = Mix(RvrBotAwareness.Veteran);
            Assert.Multiple(() =>
            {
                Assert.That(easyCasual, Is.EqualTo(45).Within(1.5));
                Assert.That(easyAverage, Is.EqualTo(45).Within(1.5));
                Assert.That(easyVeteran, Is.EqualTo(10).Within(1.5));
                Assert.That(vetCasual, Is.EqualTo(15).Within(1.5));
                Assert.That(vetAverage, Is.EqualTo(45).Within(1.5));
                Assert.That(vetVeteran, Is.EqualTo(40).Within(1.5));
                foreach (RvrBotAwareness awareness in System.Enum.GetValues<RvrBotAwareness>())
                {
                    (int casual, int average, int veteran) = RvrGroupSkill.Shares(awareness);
                    Assert.That(casual + average + veteran, Is.EqualTo(100), awareness.ToString());
                }
            });
            // a group never gets worse when the setting gets sharper
            for (long id = 1; id <= 2_000; id++)
            {
                RvrSkillTier easy = RvrGroupSkill.For(id, null, RvrBotAwareness.Easy);
                RvrSkillTier normal = RvrGroupSkill.For(id, null, RvrBotAwareness.Normal);
                RvrSkillTier veteran = RvrGroupSkill.For(id, null, RvrBotAwareness.Veteran);
                Assert.That(normal, Is.GreaterThanOrEqualTo(easy));
                Assert.That(veteran, Is.GreaterThanOrEqualTo(normal));
            }
        }

        [Test]
        public void The_leaders_personality_tilts_the_roll_by_at_most_ten_points()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RvrGroupSkill.LeaderTilt(null), Is.Zero, "personalities off");
                Assert.That(RvrGroupSkill.LeaderTilt(Leader(BotPersonalityType.Leveler, 9)), Is.GreaterThan(0), "a patient leader");
                Assert.That(RvrGroupSkill.LeaderTilt(Leader(BotPersonalityType.Daredevil, 1)), Is.LessThan(0), "an impatient one");
                Assert.That(RvrGroupSkill.LeaderTilt(Leader(BotPersonalityType.NoLifer, 9)),
                    Is.GreaterThan(RvrGroupSkill.LeaderTilt(Leader(BotPersonalityType.Leveler, 9))), "a No-lifer knows the drill");
                foreach (BotPersonalityType type in System.Enum.GetValues<BotPersonalityType>())
                    for (int patience = -3; patience <= 12; patience++)
                        Assert.That(RvrGroupSkill.LeaderTilt(Leader(type, patience)),
                            Is.InRange(-RvrGroupSkill.MaxLeaderTilt, RvrGroupSkill.MaxLeaderTilt));
            });

            (double patientCasual, _, double patientVeteran) = Mix(RvrBotAwareness.Normal, Leader(BotPersonalityType.Leveler, 9));
            (double hastyCasual, _, double hastyVeteran) = Mix(RvrBotAwareness.Normal, Leader(BotPersonalityType.Daredevil, 1));
            Assert.Multiple(() =>
            {
                Assert.That(patientVeteran, Is.GreaterThan(20).And.LessThanOrEqualTo(31.5));
                Assert.That(patientCasual, Is.LessThan(30).And.GreaterThanOrEqualTo(18.5));
                Assert.That(hastyCasual, Is.GreaterThan(30).And.LessThanOrEqualTo(41.5));
                Assert.That(hastyVeteran, Is.LessThan(20).And.GreaterThanOrEqualTo(8.5));
            });

            // the tilt moves a group one way only: a patient leader never makes it worse than a hasty one would
            for (long id = 1; id <= 2_000; id++)
                Assert.That(RvrGroupSkill.For(id, Leader(BotPersonalityType.Leveler, 9), RvrBotAwareness.Normal),
                    Is.GreaterThanOrEqualTo(RvrGroupSkill.For(id, Leader(BotPersonalityType.Daredevil, 1), RvrBotAwareness.Normal)));
        }

        [Test]
        public void The_tier_for_a_roll_follows_the_shares() => Assert.Multiple(() =>
        {
            (int, int, int) normal = RvrGroupSkill.Shares(RvrBotAwareness.Normal);
            Assert.That(RvrGroupSkill.TierForRoll(0, 0, normal), Is.EqualTo(RvrSkillTier.Casual));
            Assert.That(RvrGroupSkill.TierForRoll(29.99, 0, normal), Is.EqualTo(RvrSkillTier.Casual));
            Assert.That(RvrGroupSkill.TierForRoll(30, 0, normal), Is.EqualTo(RvrSkillTier.Average));
            Assert.That(RvrGroupSkill.TierForRoll(79.99, 0, normal), Is.EqualTo(RvrSkillTier.Average));
            Assert.That(RvrGroupSkill.TierForRoll(80, 0, normal), Is.EqualTo(RvrSkillTier.Veteran));
            Assert.That(RvrGroupSkill.TierForRoll(99.99, 0, normal), Is.EqualTo(RvrSkillTier.Veteran));
            Assert.That(RvrGroupSkill.TierForRoll(25, 10, normal), Is.EqualTo(RvrSkillTier.Average), "tilted up");
            Assert.That(RvrGroupSkill.TierForRoll(85, -10, normal), Is.EqualTo(RvrSkillTier.Average), "tilted down");
            Assert.That(RvrGroupSkill.TierForRoll(25, 500, normal), Is.EqualTo(RvrSkillTier.Average), "the tilt is capped");
        });
    }
}
