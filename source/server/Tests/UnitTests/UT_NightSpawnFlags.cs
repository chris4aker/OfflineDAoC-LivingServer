using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    // Owner 2026-10-31: a day/night spawn saved while asleep must not store its asleep flags (peace, can't target,
    // no name), or it never wakes up again (night ghosts that could never be fought).
    [TestFixture]
    public class UT_NightSpawnFlags
    {
        private sealed class SleepingGhost : NightSpawn
        {
            public eFlags Saved => FlagsToSave;
        }

        [Test]
        public void SleepingNightSpawnSavesItsOwnFlagsOnly()
        {
            // built without the constructor (it needs a running server); no brain yet = asleep
            SleepingGhost ghost = (SleepingGhost)RuntimeHelpers.GetUninitializedObject(typeof(SleepingGhost));
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ghost, new ArrayList());
            typeof(GameNPC).GetField("m_flags", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(ghost, GameNPC.eFlags.GHOST);
            Assert.Multiple(() =>
            {
                Assert.That(ghost.Flags.HasFlag(GameNPC.eFlags.PEACE), Is.True, "asleep: shown peaceful");
                Assert.That(ghost.Flags.HasFlag(GameNPC.eFlags.CANTTARGET), Is.True);
                Assert.That(ghost.Saved, Is.EqualTo(GameNPC.eFlags.GHOST), "saved without the asleep flags");
            });
        }
    }
}
