using System.IO;
using System.Linq;
using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using A = DOL.GS.BotPartyMainAssist;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #569 step 6, main assist for autonomous gamebot PvE parties (port of True165 897ddfa, f745061): the tank's target is
    /// the party's kill target and the other members take it. One server setting, bot_party_main_assist, on by default.
    /// Tests only until seen in a PC log.
    /// </summary>
    [TestFixture]
    public class UT_PartyMainAssist569
    {
        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [Test]
        public void OneSettingOnByDefaultAndShippedOn()
        {
            ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_PARTY_MAIN_ASSIST)).GetCustomAttribute<ServerPropertyAttribute>();
            Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", A.PropertyKey, (object)true)));
            string settings = Source("..", "..", "tools", "release-server-settings.json");
            Assert.That(settings, Does.Contain("\"bot_party_main_assist\": \"True\""));
        }

        [Test]
        public void MembersFollowTheKillTargetUnlessLowAndHit() => Assert.Multiple(() =>
        {
            Assert.That(A.AssistsKillTarget(true, false, 100, true), Is.True, "hit but healthy: stay on the kill target");
            Assert.That(A.AssistsKillTarget(true, false, 40, false), Is.True, "low but nobody on it: keep hitting the kill");
            Assert.That(A.AssistsKillTarget(true, false, 49, true), Is.False, "low and being hit: fight back");
            Assert.That(A.AssistsKillTarget(true, false, A.LowHealthPercent, true), Is.True, "exactly half is not low");
            Assert.That(A.AssistsKillTarget(true, true, 100, false), Is.False, "never onto a mesmerized or rooted kill target");
            Assert.That(A.AssistsKillTarget(false, false, 100, false), Is.False, "gone, stale or out of reach");
        });

        [Test]
        public void TheFirstTanksTargetStaysTheKillTarget() => Assert.Multiple(() =>
        {
            Assert.That(A.ReplacesKillTarget(true, false, true, 1_000), Is.False, "a second tank on an add does not take over");
            Assert.That(A.ReplacesKillTarget(false, false, true, 1_000), Is.True, "the kill target died");
            Assert.That(A.ReplacesKillTarget(true, true, true, 1_000), Is.True, "the tank that set it switched");
            Assert.That(A.ReplacesKillTarget(true, false, false, 1_000), Is.True, "that tank left it (dead or elsewhere)");
            Assert.That(A.ReplacesKillTarget(true, false, true, A.KillTargetStaleMilliseconds + 1), Is.True, "nobody confirmed it");
        });

        [Test]
        public void APeelStartsAKillTargetButNeverMovesALiveOne() => Assert.Multiple(() =>
        {
            Assert.That(A.MayWriteKillTarget(peel: true, liveStoredTarget: true), Is.False,
                "the tank takes the add off the healer; the others stay on the main monster");
            Assert.That(A.MayWriteKillTarget(peel: true, liveStoredTarget: false), Is.True);
            Assert.That(A.MayWriteKillTarget(peel: false, liveStoredTarget: true), Is.True);
        });

        [Test]
        public void AKillTargetGoesStaleAfterTenSeconds() => Assert.Multiple(() =>
        {
            Assert.That(A.IsFresh(20_000, 10_000), Is.True);
            Assert.That(A.IsFresh(20_001, 10_000), Is.False);
        });

        [Test]
        public void OnlyBotOnlyGamebotPveParties() => Assert.Multiple(() =>
        {
            Assert.That(A.Applies(true, true, false, true, false, true), Is.True, "a gamebot in a bot-only PvE party");
            Assert.That(A.Applies(false, true, false, true, false, true), Is.False, "the switch off: as before");
            Assert.That(A.Applies(true, false, false, true, false, true), Is.False, "not an autonomous gamebot");
            Assert.That(A.Applies(true, true, true, true, false, true), Is.False,
                "a companion, helper or hired companion, or a bot in a player's group: leader focus's ground");
            Assert.That(A.Applies(true, true, false, false, false, true), Is.False, "solo");
            Assert.That(A.Applies(true, true, false, true, true, true), Is.False, "a real player in the group: leader focus");
            Assert.That(A.Applies(true, true, false, true, false, false), Is.False, "an RvR warband keeps its own calls");
        });

        [Test]
        public void TheBrainTakesTheKillTargetWhereLeaderFocusGoesAndNeverBoth()
        {
            string brain = Source("bots", "BotBrain.cs");
            Assert.Multiple(() =>
            {
                Assert.That(brain, Does.Contain(
                    "GameLiving partyAssist = protectionTarget == null && leaderFocus == null ? PartyMainAssistTarget(directAttacker) : null;"));
                Assert.That(brain, Does.Contain("CompanionLeaderFocus.Choose(protectionTarget, leaderFocus ?? partyAssist, directAttacker,"));
                Assert.That(brain, Does.Contain("OfferPartyKillTarget(Body.TargetObject as GameLiving, protectionTarget != null &&"));
            });
            // Leader focus and main assist never apply to the same bot: one needs a player's group, the other a bot-only one.
            Assert.That(CompanionLeaderFocus.AppliesTo(true, true, false, false) && A.Applies(true, true, true, true, false, true),
                Is.False);
        }

        [Test]
        public void TheKillTargetRowShipsWithoutNames()
        {
            string assist = Source("bots", "BotBrain.MainAssist.cs");
            Assert.That(assist, Does.Contain("AUTONOMOUS_PARTY_KILL_TARGET bot=\\\"{Body.Name}\\\""));
            Assert.That(assist, Does.Contain("target=\\\"{npc.Name}\\\""));
        }
    }
}
