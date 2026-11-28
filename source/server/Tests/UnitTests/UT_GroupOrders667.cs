using System.IO;
using System.Reflection;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using O = DOL.GS.PlayerGroupOrders;

namespace DOL.GS.Tests;

/// <summary>
/// #667 step 1: the player's fight orders to his group (PlayerGroupOrders): the words, who obeys, how each order ends, the
/// gates it opens or closes, the warning and the switch. The pure rules; the world wiring is UT_GroupOrders667Harness.
/// Tests only until seen in a PC log.
/// </summary>
[TestFixture]
public class UT_GroupOrders667
{
    [Test]
    public void OneSettingOnByDefault()
    {
        ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_GROUP_ORDERS))
            .GetCustomAttribute<ServerPropertyAttribute>();
        Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", O.PropertyKey, (object)true)));
    }

    [Test]
    public void TheReleaseWorldShipsItOn()
    {
        string root = TestContext.CurrentContext.TestDirectory;
        while (root != null && !File.Exists(Path.Combine(root, "source", "tools", "release-server-settings.json")))
            root = Path.GetDirectoryName(root);
        Assume.That(root, Is.Not.Null, "the repository's release settings");
        string json = File.ReadAllText(Path.Combine(root, "source", "tools", "release-server-settings.json"));
        Assert.That(json, Does.Contain("\"" + O.PropertyKey + "\": \"True\""));
    }

    [TestCase("attack", GroupOrder.Attack)]
    [TestCase("  Attack! ", GroupOrder.Attack)]
    [TestCase("atk", GroupOrder.Attack)]
    [TestCase("assist me", GroupOrder.Assist)]
    [TestCase("Assist  on me.", GroupOrder.Assist)]
    [TestCase("assist", GroupOrder.Assist)]
    [TestCase("hold", GroupOrder.Hold)]
    [TestCase("HOLD position", GroupOrder.Hold)]
    [TestCase("regroup", GroupOrder.Regroup)]
    [TestCase("on me", GroupOrder.Regroup)]
    [TestCase("regroup on me!", GroupOrder.Regroup)]
    public void AnOrderWordAsTheWholeLineIsAnOrder(string line, GroupOrder order) =>
        Assert.That(O.Parse(line), Is.EqualTo(order));

    [TestCase("did you see that attack")]
    [TestCase("attack the healer first next time")]
    [TestCase("can you hold on a sec")]
    [TestCase("i will assist you later")]
    [TestCase("")]
    [TestCase(null)]
    public void TalkThatOnlyContainsAWordIsTalk(string line) => Assert.That(O.Parse(line), Is.EqualTo(GroupOrder.None));

    [Test]
    public void EveryOrderHasAWordAndALine()
    {
        foreach ((string word, string does) in O.Help)
        {
            GroupOrder order = O.Parse(word);
            Assert.That(order, Is.Not.EqualTo(GroupOrder.None), word);
            Assert.That(O.Word(order), Is.EqualTo(word));
            Assert.That(O.Line(order, "Enemy", false), Is.Not.Empty, word);
            Assert.That(does, Is.Not.Empty);
        }
    }

    [Test]
    public void WhoObeys()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O.Obeys(true, true, false, false, false), Is.True, "a bot of his group he commands");
            Assert.That(O.Obeys(false, true, false, false, false), Is.False, "not in his group");
            Assert.That(O.Obeys(true, false, false, false, false), Is.False, "another human's companion");
            Assert.That(O.Obeys(true, true, true, false, false), Is.False, "passive stance wins");
            Assert.That(O.Obeys(true, true, false, true, false), Is.False, "/bot hold wins");
            Assert.That(O.Obeys(true, true, false, false, true), Is.False, "/bot stay wins");
        });
    }

    private static GroupOrderFacts F(bool saw = false, bool inFight = false, long quiet = 0, long since = 0, bool gone = false,
        bool back = false) => new(saw, inFight, quiet, since, gone, back);

    [Test]
    public void AttackAndAssistEndWithTheFight()
    {
        foreach (GroupOrder order in new[] { GroupOrder.Attack, GroupOrder.Assist })
            Assert.Multiple(() =>
            {
                Assert.That(O.Ends(order, F(since: 60_000)), Is.Null, $"{order}: waits for a fight");
                Assert.That(O.Ends(order, F(since: O.NoFightMilliseconds)), Is.EqualTo("timeout"), $"{order}: no fight came");
                Assert.That(O.Ends(order, F(saw: true, inFight: true, since: 200_000)), Is.Null, $"{order}: a long fight goes on");
                Assert.That(O.Ends(order, F(saw: true, quiet: O.FightOverQuietMilliseconds - 1, since: 30_000)), Is.Null,
                    $"{order}: a short lull is not the end");
                Assert.That(O.Ends(order, F(saw: true, quiet: O.FightOverQuietMilliseconds, since: 30_000)), Is.EqualTo("fight"),
                    $"{order}: the fight is over");
            });
        Assert.That(O.Ends(GroupOrder.Attack, F(saw: true, inFight: true, gone: true)), Is.EqualTo("target"), "attack: its target fell");
        Assert.That(O.Ends(GroupOrder.Assist, F(saw: true, inFight: true, gone: true)), Is.Null, "assist has no target of its own");
    }

    [Test]
    public void RegroupEndsWhenEveryoneIsBackOrAfterItsTime()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O.Ends(GroupOrder.Regroup, F(since: 1_000)), Is.Null);
            Assert.That(O.Ends(GroupOrder.Regroup, F(since: 1_000, back: true)), Is.Null, "already beside him: breaks off first");
            Assert.That(O.Ends(GroupOrder.Regroup, F(since: O.RegroupMinimumMilliseconds, back: true)), Is.EqualTo("back"));
            Assert.That(O.Ends(GroupOrder.Regroup, F(since: O.RegroupMilliseconds)), Is.EqualTo("timeout"));
            Assert.That(O.Ends(GroupOrder.Regroup, F(saw: true, quiet: 60_000, since: 1_000)), Is.Null, "not ended by quiet");
        });
    }

    [Test]
    public void HoldIsTheStandingHold()
    {
        Assert.That(O.Ends(GroupOrder.Hold, F(saw: true, quiet: 600_000, since: 3_600_000)), Is.Null,
            "only the next order, /aggressive or /defensive end it");
    }

    [Test]
    public void RegroupOpensNothingOnTheWayBackAndDefendsTheGroupOnceBack()
    {
        const GroupOrder R = GroupOrder.Regroup;
        int low = O.RegroupFightBackHealthPercent - 1, full = 100;
        Assert.Multiple(() =>
        {
            // On the way back: break off, even what hits it, while it can still get away.
            Assert.That(O.MayFight(R, back: false, hitsMe: true, hitsGroup: false, cannotMove: false, full), Is.False,
                "break off while it can get away");
            Assert.That(O.MayFight(R, back: false, hitsMe: false, hitsGroup: true, cannotMove: false, full), Is.False,
                "on the way back it doesn't turn for a groupmate");
            Assert.That(O.MayFight(R, back: false, hitsMe: true, hitsGroup: false, cannotMove: true, full), Is.True,
                "rooted: fights back what is hitting it");
            Assert.That(O.MayFight(R, back: false, hitsMe: true, hitsGroup: false, cannotMove: false, low), Is.True,
                "below half health: fights back");
            Assert.That(O.MayFight(R, back: false, hitsMe: true, hitsGroup: false, cannotMove: false,
                O.RegroupFightBackHealthPercent), Is.False, "at exactly half it still breaks off");
            Assert.That(O.MayFight(R, back: false, hitsMe: false, hitsGroup: false, cannotMove: true, low), Is.False,
                "rooted and hurt, but nothing hits it: opens nothing");
            // Back: defends itself, the player and the group, and opens nothing else.
            Assert.That(O.MayFight(R, back: true, hitsMe: false, hitsGroup: false, cannotMove: false, full), Is.False,
                "back, but opens nothing");
            Assert.That(O.MayFight(R, back: true, hitsMe: true, hitsGroup: false, cannotMove: false, full), Is.True,
                "back: defends itself");
            Assert.That(O.MayFight(R, back: true, hitsMe: false, hitsGroup: true, cannotMove: false, full), Is.True,
                "back: defends the player and the group");
            foreach (GroupOrder order in new[] { GroupOrder.None, GroupOrder.Attack, GroupOrder.Assist, GroupOrder.Hold })
                Assert.That(O.MayFight(order, false, false, false, false, full), Is.True, $"{order} leaves the gate to the stance rules");
        });
    }

    [Test]
    public void ThePerBotHoldsAreReadFromTheBot()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O.HeldBack(eBotStance.Passive, null, botCompanion: false), Is.True, "the passive stance");
            Assert.That(O.HeldBack(eBotStance.Auto, eFSMStateType.PASSIVE, botCompanion: false), Is.True, "/bot hold (PASSIVE)");
            Assert.That(O.HeldBack(eBotStance.Auto, eFSMStateType.IDLE, botCompanion: true), Is.True, "/bot stay (a /bot companion in IDLE)");
            Assert.That(O.HeldBack(eBotStance.Auto, eFSMStateType.IDLE, botCompanion: false), Is.False,
                "a gamebot or helper passing through IDLE in its own work");
            Assert.That(O.HeldBack(eBotStance.Auto, eFSMStateType.FOLLOW, botCompanion: true), Is.False, "following: obeys");
            Assert.That(O.HeldBack(eBotStance.Auto, null, botCompanion: true), Is.False, "no state (a stopped brain): obeys");
        });
    }

    [Test]
    public void AttackAndAssistLetTheirTargetThroughDefensiveAndTheLeash()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O.LetsThrough(GroupOrder.Attack, orderedTarget: true, playersFight: false), Is.True);
            Assert.That(O.LetsThrough(GroupOrder.Attack, orderedTarget: false, playersFight: true), Is.False);
            Assert.That(O.LetsThrough(GroupOrder.Assist, orderedTarget: false, playersFight: true), Is.True);
            Assert.That(O.LetsThrough(GroupOrder.Assist, orderedTarget: false, playersFight: false), Is.False);
            Assert.That(O.LetsThrough(GroupOrder.Regroup, true, true), Is.False);
            Assert.That(O.LetsThrough(GroupOrder.Hold, true, true), Is.False);
        });
    }

    [Test]
    public void AttackAndAssistSetEveryFightersTargetTanksIncluded()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O.Focuses(GroupOrder.Attack, support: false), Is.True);
            Assert.That(O.Focuses(GroupOrder.Assist, support: false), Is.True);
            Assert.That(O.Focuses(GroupOrder.Attack, support: true), Is.False, "healers heal");
            Assert.That(O.Focuses(GroupOrder.Hold, false), Is.False);
            Assert.That(O.Focuses(GroupOrder.Regroup, false), Is.False);
            Assert.That(O.SuppressesOwnPick(GroupOrder.Assist), Is.True, "the player alone calls the target");
            Assert.That(O.SuppressesOwnPick(GroupOrder.Regroup), Is.True);
            Assert.That(O.SuppressesOwnPick(GroupOrder.Attack), Is.False);
            Assert.That(O.SuppressesOwnPick(GroupOrder.None), Is.False);
        });
    }

    [Test]
    public void AClearlyLosingAttackInRvRIsObeyedWithAWarning()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O.Warns(GroupOrder.Attack, warZone: true, RvrSightCall.BackOff), Is.True);
            Assert.That(O.Warns(GroupOrder.Attack, warZone: true, RvrSightCall.Engage), Is.False);
            Assert.That(O.Warns(GroupOrder.Attack, warZone: false, RvrSightCall.BackOff), Is.False, "PvE: no size-up");
            Assert.That(O.Warns(GroupOrder.Assist, warZone: true, RvrSightCall.BackOff), Is.False);
            Assert.That(O.Line(GroupOrder.Attack, "Enemy", warn: true), Does.Contain("Going in anyway"));
        });
    }

    [Test]
    public void OneAnswerPerGroupEveryFewSeconds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(O.Answers(10_000, 0), Is.True, "the first order");
            Assert.That(O.Answers(10_000 + O.AnswerCooldownMilliseconds - 1, 10_000), Is.False, "a macro spammed");
            Assert.That(O.Answers(10_000 + O.AnswerCooldownMilliseconds, 10_000), Is.True);
        });
    }
}
