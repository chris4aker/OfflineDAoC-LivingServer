using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Living Server 1.2 (planned as 1.2.1): a gamebot on a town break hangs around the bank, the merchants, the trainers
    /// and the crowds already standing in town instead of one spot (AutonomousTownHangouts).
    /// </summary>
    [TestFixture]
    public sealed class UT_TownHangouts
    {
        [Test]
        public void TheBankIsTheUsualSpotThenACrowdMerchantsAndTrainers()
        {
            var counts = new Dictionary<AutonomousTownHangouts.Kind, int>();
            for (int i = 0; i < 1000; i++)
            {
                var kind = AutonomousTownHangouts.Choose(true, true, true, true, (i + 0.5) / 1000.0).Value;
                counts[kind] = counts.GetValueOrDefault(kind) + 1;
            }
            Assert.Multiple(() =>
            {
                Assert.That(counts[AutonomousTownHangouts.Kind.Bank], Is.EqualTo(350));
                Assert.That(counts[AutonomousTownHangouts.Kind.Crowd], Is.EqualTo(300));
                Assert.That(counts[AutonomousTownHangouts.Kind.Merchant], Is.EqualTo(200));
                Assert.That(counts[AutonomousTownHangouts.Kind.Trainer], Is.EqualTo(150));
            });
        }

        [Test]
        public void OnlyWhatTheTownHasIsChosen() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousTownHangouts.Choose(false, false, false, false, 0.5), Is.Null, "nothing: it stays put");
            Assert.That(AutonomousTownHangouts.Choose(false, true, false, false, 0.99), Is.EqualTo(AutonomousTownHangouts.Kind.Merchant));
            Assert.That(AutonomousTownHangouts.Choose(false, false, true, true, 0.1), Is.EqualTo(AutonomousTownHangouts.Kind.Crowd));
            Assert.That(AutonomousTownHangouts.Choose(false, false, true, true, 0.99), Is.EqualTo(AutonomousTownHangouts.Kind.Trainer));
            Assert.That(Enumerable.Range(0, 100).All(i =>
                AutonomousTownHangouts.Choose(false, true, true, false, i / 100.0) != AutonomousTownHangouts.Kind.Bank), Is.True,
                "no vault keeper, no bank");
        });

        [Test]
        public void ItWandersAfterWalkingInThenEveryFewMinutesNotAtTheEndOfItsBreak()
        {
            var now = new DateTime(2026, 11, 8, 12, 0, 0, DateTimeKind.Utc);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousTownHangouts.FirstHopDelay(0), Is.EqualTo(20_000));
                Assert.That(AutonomousTownHangouts.FirstHopDelay(1), Is.LessThan(90_000));
                Assert.That(AutonomousTownHangouts.NextDwell(0), Is.EqualTo(120_000));
                Assert.That(AutonomousTownHangouts.NextDwell(1), Is.LessThan(360_000));
                Assert.That(AutonomousTownHangouts.HopDue(now, null, now.AddMinutes(10)), Is.False, "not started");
                Assert.That(AutonomousTownHangouts.HopDue(now, now.AddSeconds(1), now.AddMinutes(10)), Is.False, "not yet");
                Assert.That(AutonomousTownHangouts.HopDue(now, now, now.AddMinutes(10)), Is.True);
                Assert.That(AutonomousTownHangouts.HopDue(now, now.AddMinutes(-1), now.AddSeconds(119)), Is.False,
                    "less than two minutes of the break left");
            });
        }

        [TestCase((ushort)0)] [TestCase((ushort)1024)] [TestCase((ushort)3000)]
        public void ItStandsInFrontOfTheNpcItCameTo(ushort npcHeading)
        {
            var npc = new Vector2(5_000, 5_000);
            for (double r = 0; r < 1; r += 0.1)
            {
                Vector2 spot = AutonomousTownHangouts.SpotNear(npc, npcHeading, AutonomousTownHangouts.Kind.Merchant, r, 1 - r);
                float distance = Vector2.Distance(npc, spot);
                int off = Math.Abs(BotTurning.Difference(npcHeading, BotCombatMovement.Heading(npc, spot)));
                Assert.That(distance, Is.InRange(AutonomousTownHangouts.SpotMinimum - 0.01f, AutonomousTownHangouts.SpotMaximum + 0.01f));
                Assert.That(off, Is.LessThanOrEqualTo(AutonomousTownHangouts.FrontSpreadDegrees * 4096 / 360 + 2), "in front of it");
                Vector2 bank = AutonomousTownHangouts.SpotNear(npc, npcHeading, AutonomousTownHangouts.Kind.Bank, r, 0.999);
                Assert.That(Vector2.Distance(npc, bank), Is.LessThanOrEqualTo(AutonomousTownHangouts.BankSpotMaximum + 0.01f),
                    "the bank queue stands closer");
            }
        }

        [Test]
        public void ACrowdIsTwoOrMoreStandingTogetherNotTheBotsOwnAndNotAFullOne()
        {
            var self = new Vector2(0, 0);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousTownHangouts.TryFindCrowd([], self, out _, out _), Is.False);
                Assert.That(AutonomousTownHangouts.TryFindCrowd([new(1000, 0)], self, out _, out _), Is.False, "one is not a crowd");
                Assert.That(AutonomousTownHangouts.TryFindCrowd([new(50, 0), new(90, 20)], self, out _, out _), Is.False,
                    "the bot's own crowd");
                Assert.That(AutonomousTownHangouts.TryFindCrowd([new(1000, 0), new(1060, 30)], self, out Vector2 center, out int count),
                    Is.True);
                Assert.That(count, Is.EqualTo(2));
                Assert.That(Vector2.Distance(center, new(1030, 15)), Is.LessThan(1f));

                List<Vector2> big = [.. Enumerable.Range(0, 5).Select(i => new Vector2(-800 + i * 20, 400))];
                List<Vector2> both = [new(1000, 0), new(1060, 30), .. big];
                Assert.That(AutonomousTownHangouts.TryFindCrowd(both, self, out center, out count), Is.True);
                Assert.That(count, Is.EqualTo(5), "the biggest crowd");

                List<Vector2> full = [.. Enumerable.Range(0, 13).Select(i => new Vector2(1000 + i * 5, 0))];
                Assert.That(AutonomousTownHangouts.TryFindCrowd(full, self, out _, out _), Is.False, "a full crowd is left alone");
            });
        }

        [Test]
        public void ItJoinsACrowdOnItsEdgeFromItsOwnSideWithRoomToStand()
        {
            var center = new Vector2(1000, 0);
            var self = new Vector2(0, 0);
            for (double r = 0; r < 1; r += 0.2)
            {
                Vector2 spot = AutonomousTownHangouts.SpotInCrowd(center, self, r, r);
                Assert.That(Vector2.Distance(center, spot),
                    Is.InRange(AutonomousTownHangouts.CrowdRingMinimum - 0.01f, AutonomousTownHangouts.CrowdRingMaximum + 0.01f));
                Assert.That(spot.X, Is.LessThan(center.X), "the side it comes from");
            }
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousTownHangouts.HasRoom(new(0, 0), [new(20, 0)]), Is.False, "someone stands there");
                Assert.That(AutonomousTownHangouts.HasRoom(new(0, 0), [new(40, 0)]), Is.True);
            });
        }

        [Test]
        public void TheWalkOverIsAStrollAcrossTownNotATrekAroundIt() => Assert.Multiple(() =>
        {
            Assert.That(AutonomousTownHangouts.WalkAcceptable(500, 450), Is.True);
            Assert.That(AutonomousTownHangouts.WalkAcceptable(350, 60), Is.True, "around a stall");
            Assert.That(AutonomousTownHangouts.WalkAcceptable(1500, 300), Is.False, "around the whole town wall");
            Assert.That(AutonomousTownHangouts.WalkAcceptable(2300, 2000), Is.False, "too far");
            Assert.That(AutonomousTownHangouts.WalkAcceptable(0, 0), Is.False, "no path");
        });
    }
}
