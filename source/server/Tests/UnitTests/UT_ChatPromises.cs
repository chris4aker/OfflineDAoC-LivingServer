using System.Collections.Generic;
using DOL.GS;
using NUnit.Framework;
using C = DOL.GS.ChatPromiseRules.Candidate;
using R = DOL.GS.ChatPromiseReason;

namespace DOL.UnitTests
{
    /// <summary>
    /// "omw" made true (owner 2026-11-18 09:14; chat audit DESIGN-006, CORPUS-C-018, CORPUS-B2-047, LOG-019): a bot says it is
    /// coming only when it can and will go, saying it gives it a duty to go, and the duty ends kept (there in time) or broken.
    /// </summary>
    [TestFixture]
    public class UT_ChatPromises
    {
        /// <summary>A free level 50 RvR gamebot 4,000 units from its own realm's keep: it can go.</summary>
        private static C FreeRvrBot() => new(
            Gamebot: true, Alive: true, Active: true, InCombat: false, CarryingRelic: false, OnStableRide: false,
            InBotGroup: false, InAskersGroup: false, SpokenForByPlayer: false, Lease: eDutyLeaseKind.None, LeaseGoesThere: false,
            PromisedElsewhere: false, PromisedThere: 0, RvrAssigned: true, Level: 50, CanHeal: false, CanRezAsker: false,
            PersonalityApplies: true, Risk: 5, Sociability: 5, SameRealm: true, BoundaryAllows: true,
            TargetFound: true, TargetKind: ChatPromiseTargetKind.Keep, Frontier: true, Battleground: false, MinLevel: 0, MaxLevel: 0,
            SameRegion: true, Distance: 4000, PorterReach: false);

        private static string Why(C candidate, R reason = R.Alarm) => ChatPromiseRules.Refusal(candidate, reason, out _, out _);

        [Test]
        public void AFreeRvrBotNearItsKeepPromisesAndGoesByTheFrontierTurn()
        {
            Assert.That(ChatPromiseRules.Refusal(FreeRvrBot(), R.Alarm, out ChatPromiseMode mode, out int eta), Is.Null);
            Assert.That(mode, Is.EqualTo(ChatPromiseMode.RvrObjective));
            Assert.That(eta, Is.EqualTo(22), "4,000 units at run speed");
            Assert.That(Why(FreeRvrBot(), R.RealmPoints), Is.Null, "free rps: the same frontier trip");
        }

        [Test]
        public void APromiseIsRefusedWhenTheBotCantGo()
        {
            C free = FreeRvrBot();
            Assert.Multiple(() =>
            {
                Assert.That(Why(free with { Gamebot = false }), Is.EqualTo("not_a_gamebot"), "companions and buffbots never promise");
                Assert.That(Why(free with { Alive = false }), Is.EqualTo("dead"));
                Assert.That(Why(free with { InCombat = true }), Is.EqualTo("in_combat"));
                Assert.That(Why(free with { CarryingRelic = true }), Is.EqualTo("carrying_relic"));
                Assert.That(Why(free with { OnStableRide = true }), Is.EqualTo("on_a_ride"));
                Assert.That(Why(free with { SameRealm = false }), Is.EqualTo("other_realm"));
                Assert.That(Why(free with { TargetFound = false }), Is.EqualTo("no_place"), "\"inc\" with no keep or frontier zone");
                Assert.That(Why(free with { PromisedElsewhere = true }), Is.EqualTo("already_promised"));
                Assert.That(Why(free with { PromisedThere = ChatPromiseRules.MaxPerTarget }), Is.EqualTo("enough_going"));
                Assert.That(Why(free with { Lease = eDutyLeaseKind.RealmRaid }), Is.EqualTo(DutyLeaseReason.OnRealmRaid),
                    "a raid it can't leave");
                Assert.That(Why(free with { Lease = eDutyLeaseKind.RvrEvent }), Is.EqualTo(DutyLeaseReason.OnRvrEvent),
                    "a siege force going elsewhere is not pulled off");
                Assert.That(Why(free with { SpokenForByPlayer = true }), Is.EqualTo(DutyLeaseReason.InPlayersGroup));
                Assert.That(Why(free with { InBotGroup = true }), Is.EqualTo(DutyLeaseReason.InBotGroup), "a warband answers through its event");
                Assert.That(Why(free with { RvrAssigned = false }), Is.EqualTo("not_on_rvr"), "a PvE bot does not drop its camp");
                Assert.That(Why(free with { Level = 44 }), Is.EqualTo("level"), "the frontier is for level 50s");
                Assert.That(Why(free with { BoundaryAllows = false }), Is.EqualTo("realm_boundary"));
                Assert.That(Why(free with { Risk = 1 }), Is.EqualTo("not_the_type"), "a timid bot stays out of it");
                Assert.That(Why(free with { Distance = 200_000 }), Is.EqualTo("too_far"), "17 minutes away is no \"omw\"");
                Assert.That(Why(free with { SameRegion = false }), Is.EqualTo("too_far"), "another frontier with no porter");
                Assert.That(Why(free with { Frontier = false }), Is.EqualTo("no_place"), "an alarm is a frontier or battleground place");
            });
        }

        [Test]
        public void APorterHopCountsAndARvrForceAlreadyThereIsAPromiseKept()
        {
            C free = FreeRvrBot();
            Assert.That(ChatPromiseRules.Refusal(free with { SameRegion = false, PorterReach = true, Distance = 6000 }, R.Alarm,
                out ChatPromiseMode mode, out int eta), Is.Null);
            Assert.That(mode, Is.EqualTo(ChatPromiseMode.RvrObjective));
            Assert.That(eta, Is.EqualTo(ChatPromiseRules.PorterSeconds + 32));
            Assert.That(ChatPromiseRules.Refusal(free with { Lease = eDutyLeaseKind.RvrEvent, LeaseGoesThere = true }, R.Alarm,
                out mode, out _), Is.Null);
            Assert.That(mode, Is.EqualTo(ChatPromiseMode.Lease), "its force already defends that keep: no second destination");
        }

        [Test]
        public void BattlegroundsNeedTheBracketAndTheBattleground()
        {
            C bg = FreeRvrBot() with { Frontier = false, Battleground = true, MinLevel = 20, MaxLevel = 24, Level = 22, RvrAssigned = false };
            Assert.That(ChatPromiseRules.Refusal(bg, R.Alarm, out ChatPromiseMode mode, out _), Is.Null);
            Assert.That(mode, Is.EqualTo(ChatPromiseMode.Walk));
            Assert.That(Why(bg with { Level = 50 }), Is.EqualTo("level"), "LOG-019: a level 50 answering for Murdaigean");
            Assert.That(Why(bg with { SameRegion = false }), Is.EqualTo("too_far"));
            Assert.That(Why(bg with { Lease = eDutyLeaseKind.BattlegroundWave }), Is.EqualTo(DutyLeaseReason.OnBattleground));
        }

        [Test]
        public void HelpHealsRezAndRaidTimeAreOnlyPromisedWhenTheyHappen()
        {
            C near = FreeRvrBot() with { TargetKind = ChatPromiseTargetKind.Living, Frontier = false, RvrAssigned = false, Distance = 1200 };
            Assert.That(ChatPromiseRules.Refusal(near, R.Help, out ChatPromiseMode mode, out _), Is.Null);
            Assert.That(mode, Is.EqualTo(ChatPromiseMode.Walk), "a near free bot walks to the player");
            Assert.That(Why(near with { Distance = 9000 }, R.Help), Is.EqualTo("too_far"));
            Assert.That(Why(near with { TargetFound = false }, R.Help), Is.EqualTo("no_asker"));
            Assert.That(Why(near with { Sociability = 0 }, R.Help), Is.EqualTo("not_the_type"));
            Assert.That(ChatPromiseRules.Refusal(near with { InAskersGroup = true, SpokenForByPlayer = true }, R.Help, out mode, out _), Is.Null);
            Assert.That(mode, Is.EqualTo(ChatPromiseMode.Native), "a groupmate is already with the player");

            Assert.That(Why(near, R.Heal), Is.EqualTo("not_a_healer"));
            Assert.That(Why(near with { CanHeal = true }, R.Heal), Is.EqualTo("not_in_group"), "no stranger heals exist: no \"omw\"");
            Assert.That(ChatPromiseRules.Refusal(near with { CanHeal = true, InAskersGroup = true }, R.Heal, out mode, out _), Is.Null);
            Assert.That(mode, Is.EqualTo(ChatPromiseMode.Native));

            Assert.That(Why(near, R.Rez), Is.EqualTo("cannot_rez"));
            Assert.That(Why(near with { CanRezAsker = true }, R.Rez), Is.Null, "the rez path that was already true (WillComeRez)");

            Assert.That(Why(near, R.RaidTime), Is.EqualTo("no_raid_duty"), "\"raid at 9\": no bot signs up for a raid that isn't one");
            Assert.That(ChatPromiseRules.Refusal(near with { Lease = eDutyLeaseKind.RealmRaid }, R.RaidTime, out mode, out _), Is.Null);
            Assert.That(mode, Is.EqualTo(ChatPromiseMode.Lease));
        }

        private static ChatPromiseTarget Keep(eRealm holder = eRealm.Albion) =>
            new(ChatPromiseTargetKind.Keep, "Caer Benowyc", 1, 100_000, 100_000, 0, Anchor: 12, Holder: holder, Frontier: true);

        [Test]
        public void AnAcceptedPromiseCreatesTheDuty()
        {
            var lines = new List<string>();
            var book = new ChatPromiseBook(lines.Add);
            object bot = new();
            ChatPromise promise = book.Make(bot, "Bot1", 7, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, Keep(), "Asker", 60, now: 1_000);
            Assert.That(promise, Is.Not.Null);
            Assert.That(book.Of(bot), Is.SameAs(promise));
            Assert.That(promise.Active(1_000) && promise.Open, Is.True);
            Assert.That(promise.Deadline, Is.EqualTo(1_000 + ChatPromiseRules.DeadlineSeconds(60) * 1000L));
            Assert.That(ChatPromiseRules.DeadlineSeconds(60), Is.EqualTo(210), "half again the estimate and two minutes");
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0], Does.StartWith("CHAT_PROMISE made bot=\"Bot1\" id=7 promise=1 realm=Albion reason=Alarm mode=RvrObjective " +
                                                 "target=\"Caer Benowyc\" kind=Keep region=1 eta=60s deadline=210s"));
            // The frontier turn's duty: its own keep is defended at a verified post, another realm's keep is only patrolled near.
            Assert.That(ChatPromiseRules.RvrDestinationId(promise.Target, eRealm.Albion, out bool defend), Is.EqualTo("rvr-keep-12"));
            Assert.That(defend, Is.True);
            Assert.That(ChatPromiseRules.RvrDestinationId(Keep(eRealm.Midgard), eRealm.Albion, out defend), Is.EqualTo("rvr-roam-12"));
            Assert.That(defend, Is.False);
            // One promise at a time: "omw" again to the same keep is the same trip; another place is refused.
            Assert.That(book.Make(bot, "Bot1", 7, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, Keep(), "Asker", 60, 2_000), Is.SameAs(promise));
            var other = Keep() with { Name = "Dun Crauchon", Anchor = 30 };
            Assert.That(book.Make(bot, "Bot1", 7, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, other, "Asker", 60, 2_000), Is.Null);
            Assert.That(book.PromisedElsewhere(bot, other, 2_000), Is.True);
            Assert.That(book.GoingTo(Keep(), 2_000), Is.EqualTo(1));
            Assert.That(lines, Has.Count.EqualTo(1), "no second made line");
        }

        private static ChatPromiseBook.Progress On(bool arrived = false, bool alive = true, bool present = true, string gone = null,
            bool taken = false) => new(present, alive, arrived, gone, taken);

        [Test]
        public void TheDutyEndsKeptOnArrivalThenHoldsAndEnds()
        {
            var lines = new List<string>();
            var book = new ChatPromiseBook(lines.Add);
            object bot = new();
            ChatPromise promise = book.Make(bot, "Bot1", 7, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, Keep(), "Asker", 60, 0);
            book.NoteSaid(bot, "omw");   // chat 1.2 follow-up: a promise stands once its line is said
            Assert.That(book.Check(bot, 30_000, On()), Is.Null, "on its way");
            Assert.That(book.Check(bot, 95_000, On(arrived: true)), Is.EqualTo("kept"));
            Assert.That(lines[^1], Is.EqualTo("CHAT_PROMISE kept bot=\"Bot1\" id=7 promise=1 reason=Alarm mode=RvrObjective " +
                                              "target=\"Caer Benowyc\" after=95s eta=60s"));
            Assert.That(promise.Holding(96_000) && promise.Active(96_000), Is.True, "it stays a while: the walk is still the duty's");
            Assert.That(book.Check(bot, 100_000, On(arrived: true)), Is.Null);
            Assert.That(book.Check(bot, 95_000 + ChatPromiseRules.HoldSeconds * 1000L, On(arrived: true)), Is.EqualTo("done"));
            Assert.That(book.Of(bot), Is.Null, "the duty ended; the bot plans as before");
            Assert.That(lines, Has.Count.EqualTo(2), "made and kept, nothing for the quiet end");
        }

        [Test]
        public void TheDutyEndsBrokenAtTheDeadlineOrWhenSomethingEndsItFirst()
        {
            var lines = new List<string>();
            var book = new ChatPromiseBook(lines.Add);
            object bot = new();
            book.Make(bot, "Bot1", 7, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, Keep(), "Asker", 60, 0);
            book.NoteSaid(bot, "omw");   // chat 1.2 follow-up: a promise stands once its line is said
            Assert.That(book.Check(bot, 209_000, On()), Is.Null);
            Assert.That(book.Check(bot, 210_000, On()), Is.EqualTo("broken"));
            Assert.That(lines[^1], Is.EqualTo("CHAT_PROMISE broken bot=\"Bot1\" id=7 promise=1 reason=Alarm mode=RvrObjective " +
                                              "target=\"Caer Benowyc\" why=timeout after=210s deadline=210s"));
            Assert.That(book.Of(bot), Is.Null);

            foreach (var (progress, why) in new[]
                     {
                         (On(alive: false), "died"), (On(present: false), "logged_out"), (On(taken: true), "taken"),
                         (On(gone: "keep_fell"), "keep_fell"),
                     })
            {
                book.Make(bot, "Bot1", 7, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, Keep(), "Asker", 60, 0);
                book.NoteSaid(bot, "omw");
                Assert.That(book.Check(bot, 10_000, progress), Is.EqualTo("broken"), why);
                Assert.That(lines[^1], Does.Contain($" why={why} "));
            }
            // Dead on arrival is not a kept promise.
            book.Make(bot, "Bot1", 7, eRealm.Albion, R.Alarm, ChatPromiseMode.RvrObjective, Keep(), "Asker", 60, 0);
            book.NoteSaid(bot, "omw");
            Assert.That(book.Check(bot, 10_000, On(arrived: true, alive: false)), Is.EqualTo("broken"));
        }

        [Test]
        public void TheLinesThatPromiseAreTheOnesGuarded()
        {
            foreach (string promise in new[] { "omw", "on my way", "omw, 2 min", "coming", "coming, hold the door", "ill be there",
                         "i'll be there", "heading there now", "rgr, omw", "count me in", "porting to bled", "k omw", "omw!!", "otw", "on it" })
                Assert.That(ChatPromiseRules.SaysComing(promise), Is.True, promise);
            foreach (string none in new[] { "how many?", "where are you?", "might make it", "ugh, again", "anyone porting out?",
                         "is anyone coming?", "save me some rp", "hold the inner door", "seen them", "gl, cant get there in time", "" })
                Assert.That(ChatPromiseRules.SaysComing(none), Is.False, none);
        }

        [Test]
        public void KeepsAreReadFromChatAsBotsWriteThem()
        {
            Assert.That(ChatPromiseRules.NamesKeep("inc bled", "Bledmeer Faste"), Is.True);
            Assert.That(ChatPromiseRules.NamesKeep("12 hibs at bledmeer", "Bledmeer Faste"), Is.True);
            Assert.That(ChatPromiseRules.NamesKeep("need def crim pls", "Dun Crimthain"), Is.True);
            Assert.That(ChatPromiseRules.NamesKeep("rams on benowyc", "Caer Benowyc"), Is.True);
            Assert.That(ChatPromiseRules.NamesKeep("inc Caer Benowyc!!", "Caer Benowyc"), Is.True);
            Assert.That(ChatPromiseRules.NamesKeep("inc emain", "Dun Crimthain"), Is.False);
            Assert.That(ChatPromiseRules.NamesKeep("the blob is here", "Bledmeer Faste"), Is.False, "4-5 letters, a prefix of the name");
            Assert.That(ChatPromiseRules.NamesKeep(null, "Bledmeer Faste"), Is.False);
        }

    }

    /// <summary>The live gate's guard of a chosen line (a gamebot double needs the server double).</summary>
    [TestFixture]
    public class UT_ChatPromiseGate : DutyLeaseFixture
    {
        [Test]
        public void TheGateSaysTheLineOnlyWhenTheBotGoes()
        {
            var previous = ChatPromises.Duty;
            try
            {
                Assert.That(ChatPromises.TryPromiseCome(null, "bled", R.Alarm), Is.False);
                ChatPromises.Duty = (_, _, _, _) => false;
                GameBot bot = DutyLeaseTestKit.Gamebot();
                Assert.That(ChatPromises.Gate(bot, "omw", "bled", R.Alarm, "inc bled", "how many?"), Is.EqualTo("how many?"));
                Assert.That(ChatPromises.Gate(bot, "ugh, again", "bled", R.Alarm, "inc bled", "how many?"), Is.EqualTo("ugh, again"),
                    "a line that promises nothing needs no duty");
                ChatPromises.Duty = (_, _, _, _) => true;
                Assert.That(ChatPromises.Gate(bot, "omw", "bled", R.Alarm, "inc bled", "how many?"), Is.EqualTo("omw"));
                Assert.That(ChatPromises.CanPromiseCome(bot, "bled", R.Alarm), Is.True);
            }
            finally
            {
                ChatPromises.Duty = previous;
            }
        }
    }
}
