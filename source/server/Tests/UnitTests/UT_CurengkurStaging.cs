using System;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Curengkur, owner's PC, 2026-11-16 (build ccba364, /epictest sweep): the raid never started. REALM_RAID_START_UNAVAILABLE
    /// said staging=False hubPost=True route="not tried" three times, but the sweep reported "No complete connected
    /// hub-to-encounter route was found". On the PC's zone187.nav (UT_StagingRingProbe) his spawn 376086,172803,3452 is a
    /// navmesh island about 200 above the hollow floor; every walk from the ring stops at 376013,172662,3248. A staging post
    /// now also counts when it walks to the ground at the home's foot (RealmRaidStaging.FootOf), and the reason says which
    /// part failed. Tests only: not yet seen on the owner's PC.
    /// </summary>
    [TestFixture]
    public class UT_CurengkurStaging
    {
        private sealed class BoundsNav : PathfindingMgrBase
        {
            public Vector3 Origin, Min, Max;
            public Vector3? Answer;
            public override Vector3? GetClosestPointInBounds(Zone zone, Vector3 origin, Vector3 minOffset, Vector3 maxOffset, EDtPolyFlags[] filters)
            {
                Origin = origin; Min = minOffset; Max = maxOffset;
                return Answer;
            }
            public override bool HasNavmesh(Zone zone) => true;
            public override bool IsAvailable => true;
        }

        private static Zone Allta() => new(null, 187, "Allta Fearann", 344064, 163840, 65536, 65536, 187, false, 0, false, 0, 0, 0, 0, 0);

        [Test]
        public void TheFootIsTheFloorUnderTheHomeNotTheHomesOwnLedge()
        {
            var home = new Vector3(376086, 172803, 3452);
            var nav = new BoundsNav { Answer = new Vector3(376013, 172662, 3248) };
            Vector3? foot = RealmRaidStaging.FootOf(nav, Allta(), home);
            Assert.Multiple(() =>
            {
                Assert.That(foot, Is.EqualTo(new Vector3(376013, 172662, 3248)));
                Assert.That(nav.Origin, Is.EqualTo(home));
                // Looked for at least 64 below the home (never the ledge it stands on), up to 600 down and 400 round.
                Assert.That(nav.Max.Z, Is.EqualTo(-RealmRaidStaging.FootMinimumDrop));
                Assert.That(nav.Min.Z, Is.EqualTo(-RealmRaidStaging.FootDepth));
                Assert.That(nav.Min.X, Is.EqualTo(-RealmRaidStaging.FootRadius));
                Assert.That(nav.Max.Y, Is.EqualTo(RealmRaidStaging.FootRadius));
            });
        }

        [Test]
        public void NoMeshMeansNoFoot()
        {
            Assert.That(RealmRaidStaging.FootOf(null, Allta(), new Vector3(376086, 172803, 3452)), Is.Null);
            Assert.That(RealmRaidStaging.FootOf(new BoundsNav(), null, new Vector3(376086, 172803, 3452)), Is.Null);
        }

        [Test]
        public void TheStartReasonSaysWhichPartFailed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousRealmRaid.StartUnavailableReason(staged: false, posted: true),
                    Does.StartWith("No staging post with a walk to the encounter"));
                Assert.That(AutonomousRealmRaid.StartUnavailableReason(true, false), Does.StartWith("No muster post"));
                Assert.That(AutonomousRealmRaid.StartUnavailableReason(true, true),
                    Does.StartWith("No complete connected hub-to-encounter route"));
            });
        }
    }
}
