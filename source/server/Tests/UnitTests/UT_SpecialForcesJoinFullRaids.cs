using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// In-game check 2026-11-04: an automatic Summoner's Hall raid of small parties (34 at the hub, four more joining on
    /// the road: 38 parties, 277 bots) wiped at Grand Summoner Govannon, and all three Epic Special Forces parties were
    /// refused with "the raid did not accept the party". The 38-party cap was applied to them too, and every formation
    /// slot was taken. They come on top of the realm's limits: three parties and three formation slots of their own.
    /// </summary>
    [TestFixture]
    public class UT_SpecialForcesJoinFullRaids
    {
        private static AutonomousBotGroupCoordinator.ForcedRaidMember Member(bool specialForce = false, bool eligible = true,
            bool alive = true, bool reserved = true, bool inCombat = false, bool inGroup = false, bool mayAnswer = true) =>
            new(eligible, alive, reserved, inCombat, inGroup, mayAnswer, specialForce);

        private static AutonomousBotGroupCoordinator.ForcedRaidMember[] Party(int size, AutonomousBotGroupCoordinator.ForcedRaidMember member) =>
            Enumerable.Repeat(member, size).ToArray();

        [Test]
        public void TheSpecialForcesJoinARaidThatHoldsAllThirtyEightOfTheRealmsParties()
        {
            int partyCap = RealmRaidRecruitmentPolicy.MaximumParties;
            Assert.Multiple(() =>
            {
                // The Summoner's Hall raid at its wipe: 38 parties, 277 bots, no Special Forces yet.
                for (int joined = 0; joined < AutonomousEpicSpecialForces.MaximumParties; joined++)
                    Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(true, partyCap, 277, joined, 8), Is.Null, $"Special Forces party {joined + 1}");
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(true, partyCap, 300, 0, 8), Is.Null, "not held to the 300-bot limit either");
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(true, 10, 80, AutonomousEpicSpecialForces.MaximumParties, 8),
                    Does.Contain("Special Forces"), "never a fourth party of them");
            });
        }

        [Test]
        public void TheRealmsOwnPartiesKeepTheirLimitsAndTheSpecialForcesDoNotCountAgainstThem()
        {
            int partyCap = RealmRaidRecruitmentPolicy.MaximumParties;
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(false, partyCap, 277, 0, 5), Does.Contain("38 parties"));
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(false, partyCap - 1, 296, 0, 8), Does.Contain("300"));
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(false, partyCap - 1, 296, 0, 4), Is.Null, "the final four still fit");
                // Three Special Forces parties in the raid change nothing for the realm (their parties and bots are not counted).
                Assert.That(RealmRaidRecruitmentPolicy.PartyCapRefusal(false, partyCap - 1, 272, AutonomousEpicSpecialForces.MaximumParties, 8), Is.Null);
            });
        }

        [Test]
        public void TheSpecialForcesHaveFormationSlotsOfTheirOwn()
        {
            int[] realm = RealmRaidRecruitmentPolicy.SlotOrder(false).ToArray();
            int[] specialForces = RealmRaidRecruitmentPolicy.SlotOrder(true).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(RealmRaidRecruitmentPolicy.FormationSlots, Is.EqualTo(41));
                Assert.That(realm, Is.EqualTo(Enumerable.Range(0, RealmRaidRecruitmentPolicy.MaximumParties)), "the realm keeps slots 0-37");
                Assert.That(specialForces.Take(3), Is.EqualTo(new[] { 38, 39, 40 }), "their own slots first");
                Assert.That(specialForces.Skip(3), Is.EqualTo(realm), "then any slot the realm left free");
                Assert.That(specialForces, Is.Unique);
            });
        }

        [Test]
        public void TheirSlotsFindAPostBesideTheRealmsThirtyEight()
        {
            var nav = new UT_RealmRaidFormation.Mesh();
            var occupied = new List<Vector3>();
            Vector3 center = new(20000, 20000, 1000);
            for (int slot = 0; slot < RealmRaidRecruitmentPolicy.FormationSlots; slot++)
            {
                Assert.That(RealmRaidFormation.TryResolve(nav, UT_RealmRaidFormation.Zone(), center, slot, occupied, out Vector3 position), Is.True, $"slot {slot}");
                occupied.Add(position);
            }
            Assert.That(RealmRaidFormation.TryResolve(nav, UT_RealmRaidFormation.Zone(), center, RealmRaidRecruitmentPolicy.FormationSlots, occupied, out _),
                Is.False, "no slot beyond the Special Forces' three");
        }

        [Test]
        public void TheSpecialForcesAnswerAnAutomaticRaidWhateverTheirObjective()
        {
            var specialForce = Member(specialForce: true, mayAnswer: false);
            var realmBot = Member(mayAnswer: false);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, specialForce), true, false), Is.Null,
                    "they answer a call; they are not waiting applicants");
                Assert.That(AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, realmBot), true, false),
                    Is.EqualTo("a member may not answer an automatic raid"), "an RvR bot is still left to the frontier");
                Assert.That(AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, realmBot), true, true), Is.Null, "a forced raid takes it");
            });
        }

        [Test]
        public void EveryRefusalOfAPartyNamesItsReason()
        {
            var ok = Member(specialForce: true);
            var reasons = new[]
            {
                AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(7, ok), true, false),
                AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, Member(true, eligible: false)), true, false),
                AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, Member(true, alive: false)), true, false),
                AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, Member(true, reserved: false)), true, false),
                AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, Member(true, inCombat: true)), true, false),
                AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, ok), false, false),
                AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, Member(true, inGroup: true)), true, false),
            };
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, ok), true, false), Is.Null);
                Assert.That(AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(4, ok), true, false), Is.Null);
                Assert.That(reasons, Has.None.Null.And.None.Empty);
                Assert.That(reasons, Is.Unique, string.Join(" | ", reasons));
                Assert.That(AutonomousBotGroupCoordinator.ForcedRaidPartyRefusal(Party(8, Member(true, inGroup: true)), true, true), Is.Null,
                    "a forced raid may take a bot out of its old party");
            });
        }

        [Test]
        public void TheFailedLineCarriesTheRefusal()
        {
            string line = AutonomousEpicSpecialForces.FailedLine("summoners-albion", 1, "the raid already has its 38 parties");
            Assert.Multiple(() =>
            {
                Assert.That(line, Does.StartWith("REALM_RAID_SPECIAL_FORCES_FAILED event=summoners-albion party=1 "));
                Assert.That(line, Does.Contain("refusal=\"the raid already has its 38 parties\""));
                Assert.That(AutonomousEpicSpecialForces.FailedLine("x", 2, null), Does.Contain("refusal=\"unknown\""));
            });
        }

        [Test]
        public void TheArrivedLineNamesTheRealm()
        {
            // eRealm.Albion shares its value with an alias, so the log said realm=_FirstPlayerRealm.
            Assume.That(eRealm.Albion.ToString(), Is.Not.EqualTo("Albion"));
            Assert.Multiple(() =>
            {
                foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                    Assert.That(AutonomousEpicSpecialForces.ArrivedLine("summoners-albion", realm, 24, 24, "a wipe at Grand Summoner Govannon", ["Interrupted"]),
                        Does.Contain($" realm={GlobalConstants.RealmToName(realm)} members=24 asked=24 "));
                Assert.That(AutonomousEpicSpecialForces.ArrivedLine("summoners-albion", eRealm.Albion, 0, 24, "r", null),
                    Does.Contain("realm=Albion ").And.Not.Contain("_FirstPlayerRealm").And.EndsWith("history=\"\""));
            });
        }
    }
}
