using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;
using GuildKeeps = DOL.GS.AutonomousBotGuildKeeps;
using L = DOL.GS.AutonomousRvrEventLayer;
using Spoken = DOL.GS.RealmEventSpoken;

namespace DOL.GS.Tests;

/// <summary>
/// #523 step 1: (f) a relic in motion blocks new sieges only in the carrier's frontier; (h) the army that took a keep holds
/// it for the claim, and the claim counts the realm's guild members across that army; (i) the siege notice names the muster.
/// </summary>
[TestFixture, NonParallelizable]
public sealed class UT_SiegeRelicClaimMuster523
{
    [SetUp] public void ResetEvents() { RvrEventTestState.Clear(); Pending.Clear(); }
    [TearDown] public void ClearEvents() { RvrEventTestState.Clear(); Pending.Clear(); }

    /// <summary>The notices waiting to be told, by (event, realm): what each realm will read or hear.</summary>
    private static Dictionary<(string, eRealm), (string Text, long Expires, string Topic, IReadOnlyDictionary<string, string> Slots)> Pending =>
        (Dictionary<(string, eRealm), (string Text, long Expires, string Topic, IReadOnlyDictionary<string, string> Slots)>)typeof(RealmEventNotices)
            .GetField("Pending", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    private static L.LiveObjective DunDagda() =>
        new("rvr-keep-110", "Dun Dagda", L.Intent.AssaultRelicKeep, eRealm.Hibernia, 200, 401036, 465147, 2888, true, 0, 0, 12, 9);

    private static L.LiveObjective HibKeep() =>
        new("rvr-keep-120", "Dun Crauchon", L.Intent.AssaultKeep, eRealm.Hibernia, 200, 420000, 470000, 3000, false, 0, 0, 2, 2);

    private static L.LiveObjective AlbKeep() =>
        new("rvr-keep-50", "Caer Benowyc", L.Intent.AssaultKeep, eRealm.Albion, 1, 600000, 380000, 3000, false, 0, 0, 2, 2);

    private static L.LiveObjective Roam() =>
        new("roam-1", "Patrol", L.Intent.Roam, eRealm.Albion, 1, 610000, 390000, 3000, false, 0, 0, 0, 0);

    private static L.Force Mids(string id) => new(id, eRealm.Midgard, 8, 50, 2);

    // ---- (f) --------------------------------------------------------------------------------------------------------

    [Test]
    public void TheCarrierRuleHoldsOnlyTheCarriersFrontier()
    {
        Assert.Multiple(() =>
        {
            Assert.That(L.CarrierBlocksSiege(200, new ushort[] { 200 }), Is.True);
            Assert.That(L.CarrierBlocksSiege(1, new ushort[] { 200 }), Is.False);
            Assert.That(L.CarrierBlocksSiege(1, new ushort[0]), Is.False, "no relic in motion holds nothing");
            Assert.That(L.CarrierBlocksSiege(1, null), Is.True, "a carrier whose whereabouts are unknown holds every target");
        });
    }

    [Test]
    public void ARelicOnTheRoadInHiberniaLeavesAlbionsFrontOpen()
    {
        Assert.That(L.ForceStart(DunDagda(), eRealm.Midgard, 1_000, out string reason), Is.True, reason);
        L.TransferToRelicCarrier(DunDagda().Id, "rvr-relic-carrier-3-1", 2_000);

        L.Plan plan = L.ChooseOrJoin(Mids("mid-a"), new[] { HibKeep(), AlbKeep(), Roam() }, 3_000, 0);

        Assert.That(plan?.IsSharedEvent, Is.True, plan?.Reason);
        Assert.That(plan?.TargetId, Is.EqualTo(AlbKeep().Id), "the keep in another frontier can be sieged");
    }

    [Test]
    public void ARelicOnTheRoadStillHoldsItsOwnFrontier()
    {
        Assert.That(L.ForceStart(DunDagda(), eRealm.Midgard, 1_000, out _), Is.True);
        L.TransferToRelicCarrier(DunDagda().Id, "rvr-relic-carrier-3-1", 2_000);

        L.Plan plan = L.ChooseOrJoin(Mids("mid-b"), new[] { HibKeep(), Roam() }, 3_000, 0);

        Assert.That(plan?.IsSharedEvent, Is.Not.True, "no new siege in the carrier's frontier");
        Assert.That(L.IsTargetActive(HibKeep().Id, 3_000), Is.False);
    }

    [Test]
    public void ACarrierWhoseRegionIsUnknownHoldsEveryFrontier()
    {
        Assert.That(L.ForceStart(DunDagda(), eRealm.Midgard, 1_000, out _), Is.True);
        var nowhere = DunDagda() with { Id = "rvr-relic-carrier-3-1", RegionId = 0, IsRelicCarrier = true };
        L.TransferToRelicCarrier(DunDagda().Id, nowhere.Id, 2_000, nowhere);

        L.Plan plan = L.ChooseOrJoin(Mids("mid-c"), new[] { AlbKeep(), Roam() }, 3_000, 0);

        Assert.That(plan?.IsSharedEvent, Is.Not.True);
        Assert.That(L.IsTargetActive(AlbKeep().Id, 3_000), Is.False);
    }

    [Test]
    public void AForceThatLosesTheCarrierRollIsNotHeldInReserve()
    {
        // A Hibernian relic on the road in Hibernia's frontier; a roll of 0.99 is above every join chance (0.78 at most).
        var carrier = DunDagda() with { Id = "rvr-relic-carrier-3-1", Name = "Hibernian relic", Kind = L.Intent.HuntEnemy,
            IsRelicKeep = false, IsRelicCarrier = true };
        Assume.That(L.ShouldJoinActiveEvent(Mids("mid-d"), 0, 64, true, false, 0.99), Is.False, "the roll loses the carrier join");

        L.Plan plan = L.ChooseOrJoin(Mids("mid-d"), new[] { carrier, HibKeep(), AlbKeep(), Roam() }, 3_000, 0.99);

        Assert.That(plan?.IsSharedEvent, Is.True, plan?.Reason);
        Assert.That(plan?.TargetId, Is.EqualTo(AlbKeep().Id), "it opens a siege outside the carrier's frontier, not a reserve patrol");
        Assert.That(L.IsTargetActive(HibKeep().Id, 3_000), Is.False, "the carrier's own frontier stays held");
    }

    // ---- (h) --------------------------------------------------------------------------------------------------------

    [Test]
    public void TheArmyThatTookAKeepHoldsItThreeMinutesThenGoes()
    {
        var keep = AlbKeep();
        Assert.That(L.ChooseOrJoin(Mids("mid-army"), new[] { keep }, 1_000, 0)?.TargetId, Is.EqualTo(keep.Id));
        L.Plan defend = L.ChooseOrJoin(new L.Force("alb-def", eRealm.Albion, 8, 50, 2), new[] { keep }, 1_100, 0);
        Assert.That(defend?.Intent, Is.EqualTo(L.Intent.DefendEvent), defend?.Reason);

        const long taken = 10_000;
        L.EndTarget(keep.Id, taken, eRealm.Midgard);

        Assert.Multiple(() =>
        {
            Assert.That(L.TryConsumeRelease("alb-def", taken + 1, out _), Is.True, "the defenders are released at once");
            Assert.That(L.TryConsumeRelease("mid-army", taken + 1, out _), Is.False, "the capturing army holds the keep");
            Assert.That(L.IsForceCommitted("mid-army", taken + 1), Is.True);
            Assert.That(L.IsEventForce("mid-army", taken + 1), Is.True);
            Assert.That(L.IsTargetActive(keep.Id, taken + 1), Is.True, "its destination stays live while it holds");
            L.Plan held = L.KeepPlan("mid-army", eRealm.Midgard, taken + 1);
            Assert.That(held?.Intent, Is.EqualTo(L.Intent.DefendEvent));
            Assert.That(held?.TargetId, Is.EqualTo(keep.Id));
            Assert.That(L.KeepPlan("mid-army", eRealm.Albion, taken + 1), Is.Null, "the hold is the capturing realm's");
            L.Plan chosen = L.ChooseOrJoin(Mids("mid-army"), new[] { keep with { OwningRealm = eRealm.Midgard } }, taken + 2, 0);
            Assert.That(chosen?.Intent, Is.EqualTo(L.Intent.DefendEvent), chosen?.Reason);
            Assert.That(chosen?.TargetId, Is.EqualTo(keep.Id));
            Assert.That(L.TryConsumeRelease("mid-army", taken + L.ClaimHoldMilliseconds - 1, out _), Is.False, "not before three minutes");
        });

        Assert.That(L.TryConsumeRelease("mid-army", taken + L.ClaimHoldMilliseconds, out string why), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(why, Does.StartWith("Keep captured"));
            Assert.That(L.IsForceCommitted("mid-army", taken + L.ClaimHoldMilliseconds), Is.False);
            Assert.That(L.IsTargetActive(keep.Id, taken + L.ClaimHoldMilliseconds), Is.False);
        });
    }

    /// <summary>The defenders of a live event, as <c>PulseDefense</c>'s recruit pass fills them.</summary>
    private static Dictionary<string, int> DefendersOf(string targetId)
    {
        var events = (IDictionary)typeof(L).GetField("Events", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
        object active = events[targetId];
        return active == null ? null : (Dictionary<string, int>)active.GetType().GetField("Defenders").GetValue(active);
    }

    /// <summary>
    /// A bot capture, then the losing realm's player strikes back within the hold: a player-attack defense opens on the same
    /// keep and the holding army (now the keep's realm) is recruited as a defender. The hold must not release it at three
    /// minutes. The recruit is installed as the recruit pass installs it (the pass itself needs live warbands); the pass also
    /// drops the hold at the join (JoinedEventDropsClaimHold), and the expiry skips any force in a live event.
    /// </summary>
    [Test]
    public void AHoldingArmyRecruitedToDefendTheKeepIsNotReleasedWhenTheHoldEnds()
    {
        var keep = AlbKeep();
        Assert.That(L.ChooseOrJoin(Mids("mid-army"), new[] { keep }, 1_000, 0)?.TargetId, Is.EqualTo(keep.Id));
        const long taken = 10_000;
        L.EndTarget(keep.Id, taken, eRealm.Midgard);
        Assume.That(L.TryConsumeRelease("mid-army", taken + 1, out _), Is.False, "the army holds the keep it took");

        var midKeep = keep with { OwningRealm = eRealm.Midgard };
        Assert.That(L.BeginDefenseResponse(midKeep, eRealm.Albion, "offline", taken + 30_000), Is.True, "the player's attack opens a defense");
        DefendersOf(keep.Id).Add("mid-army", 8);

        long end = taken + L.ClaimHoldMilliseconds;
        Assert.Multiple(() =>
        {
            Assert.That(L.TryConsumeRelease("mid-army", end, out string why), Is.False, why);
            Assert.That(DefendersOf(keep.Id)?.ContainsKey("mid-army"), Is.True, "the army is still in the defense");
            Assert.That(L.IsForceCommitted("mid-army", end), Is.True);
            Assert.That(L.KeepPlan("mid-army", eRealm.Midgard, end)?.Intent, Is.EqualTo(L.Intent.DefendEvent));
        });
    }

    [Test]
    public void AHoldingArmyThatChoosesTheDefenseOfItsKeepLeavesTheHoldWithNoRelease()
    {
        var keep = AlbKeep();
        L.ChooseOrJoin(Mids("mid-army"), new[] { keep }, 1_000, 0);
        const long taken = 10_000;
        L.EndTarget(keep.Id, taken, eRealm.Midgard);
        var midKeep = keep with { OwningRealm = eRealm.Midgard };
        L.BeginDefenseResponse(midKeep, eRealm.Albion, "offline", taken + 30_000);
        DefendersOf(keep.Id).Add("mid-army", 8);

        L.Plan plan = L.ChooseOrJoin(Mids("mid-army"), new[] { midKeep }, taken + 31_000, 0);
        Assert.That(plan?.Intent, Is.EqualTo(L.Intent.DefendEvent), plan?.Reason);
        Assert.That(plan?.TargetId, Is.EqualTo(keep.Id));

        // The defense ends early: the army goes with the defense's own release, and no second "Keep captured" one follows.
        DefendersOf(keep.Id).Remove("mid-army");
        Assert.That(L.TryConsumeRelease("mid-army", taken + L.ClaimHoldMilliseconds, out string why), Is.False,
            $"the hold was left at the join, so its end releases nothing ({why})");
    }

    [Test]
    public void AHoldingArmyGivenAPlanOutsideAnyEventIsReleasedAtOnce()
    {
        var keep = AlbKeep();
        L.ChooseOrJoin(Mids("mid-army"), new[] { keep }, 1_000, 0);
        const long taken = 10_000;
        L.EndTarget(keep.Id, taken, eRealm.Midgard);

        // Below siege level the force gets a roaming reserve plan, which is no event: it leaves the hold and is released.
        L.Plan plan = L.ChooseOrJoin(new L.Force("mid-army", eRealm.Midgard, 8, 40, 2), new[] { Roam() }, taken + 2, 0);
        Assume.That(plan?.IsSharedEvent, Is.Not.True, plan?.Reason);
        Assert.That(L.TryConsumeRelease("mid-army", taken + 3, out string why), Is.True);
        Assert.That(why, Does.StartWith("Keep captured"));
        Assert.That(L.IsForceCommitted("mid-army", taken + 3), Is.False);
    }

    [Test]
    public void AKeepTakenByTheThirdRealmHoldsNobody()
    {
        var keep = AlbKeep();
        L.ChooseOrJoin(Mids("mid-late"), new[] { keep }, 1_000, 0);
        L.EndTarget(keep.Id, 10_000, eRealm.Hibernia);
        Assert.That(L.TryConsumeRelease("mid-late", 10_001, out _), Is.True);
        Assert.That(L.IsTargetActive(keep.Id, 10_001), Is.False);
    }

    [Test]
    public void ARelicKeepIsNotHeldForAClaim()
    {
        L.ChooseOrJoin(Mids("mid-relic"), new[] { DunDagda() }, 1_000, 0);
        Assume.That(L.IsTargetActive(DunDagda().Id, 1_001), Is.True, "the relic siege opened");
        L.EndTarget(DunDagda().Id, 10_000, eRealm.Midgard);
        Assert.That(L.IsTargetActive(DunDagda().Id, 10_001), Is.False);
        Assert.That(L.IsForceCommitted("mid-relic", 10_001), Is.False);
    }

    [TestCase(false, false, true, eRealm.Midgard, eRealm.Midgard, true, TestName = "A bot siege that took its keep holds it")]
    [TestCase(true, false, true, eRealm.Albion, eRealm.Albion, false, TestName = "A player-attack defense holds nobody")]
    [TestCase(false, true, true, eRealm.Midgard, eRealm.Midgard, false, TestName = "A relic keep holds nobody")]
    [TestCase(false, false, false, eRealm.Midgard, eRealm.None, false, TestName = "A siege that failed holds nobody")]
    [TestCase(false, false, true, eRealm.Midgard, eRealm.Hibernia, false, TestName = "A keep the third realm took holds nobody")]
    public void WhoHoldsForTheClaim(bool defense, bool relic, bool captured, eRealm attacker, eRealm winner, bool holds) =>
        Assert.That(L.HoldsForClaim(defense, relic, captured, attacker, winner), Is.EqualTo(holds));

    private static GuildKeeps.ClaimCheck Ready() => new(SameRealm: true, AlreadyClaimed: false, InCombat: false, ClaimableType: true,
        PlayerPresent: false, GuildClaims: 0, ClaimLimit: 1, MembersPresent: 8, Needed: 8);

    [Test]
    public void EachFailedClaimRuleHasItsReason()
    {
        Assert.Multiple(() =>
        {
            Assert.That(GuildKeeps.SkipReason(Ready()), Is.Null);
            Assert.That(GuildKeeps.CanClaim(Ready()), Is.True);
            Assert.That(GuildKeeps.SkipReason(Ready() with { MembersPresent = 7 }), Is.EqualTo("too-few"));
            Assert.That(GuildKeeps.SkipReason(Ready() with { AlreadyClaimed = true }), Is.EqualTo("claimed"));
            Assert.That(GuildKeeps.SkipReason(Ready() with { ClaimableType = false }), Is.EqualTo("not-claimable"));
            Assert.That(GuildKeeps.SkipReason(Ready() with { ClaimLimit = 0 }), Is.EqualTo("claims-off"));
            Assert.That(GuildKeeps.SkipReason(Ready() with { SameRealm = false }), Is.EqualTo("other-realm"));
            Assert.That(GuildKeeps.SkipReason(Ready() with { PlayerPresent = true }), Is.EqualTo("player-present"));
            Assert.That(GuildKeeps.SkipReason(Ready() with { InCombat = true }), Is.EqualTo("under-attack"));
            Assert.That(GuildKeeps.SkipReason(Ready() with { GuildClaims = 1 }), Is.EqualTo("guild-at-limit"));
            Assert.That(GuildKeeps.CanClaim(Ready() with { MembersPresent = 7 }), Is.False);
        });
    }

    [Test]
    public void TheClaimCountsTheRealmsGuildMembersTogether()
    {
        var rvr = new GuildKeeps.Claimant("guild-rvr", BotGuildCharter.Rvr, 5);
        var social = new GuildKeeps.Claimant("guild-social", BotGuildCharter.Social, 4);
        Assert.Multiple(() =>
        {
            Assert.That(GuildKeeps.ClaimCount(new[] { rvr, social }), Is.EqualTo(9), "two guilds of the army count together");
            Assert.That(GuildKeeps.ClaimCount(new[] { rvr, social }), Is.GreaterThanOrEqualTo(GuildKeeps.NeededMembers(8, false)),
                "five and four together make the eight a keep needs, though neither guild has eight");
            Assert.That(GuildKeeps.ChooseClaimant(new[] { rvr, social }), Is.EqualTo("guild-rvr"), "the bigger guild claims");
            Assert.That(GuildKeeps.ClaimCount(new[] { rvr, social with { AtClaimLimit = true } }), Is.EqualTo(9),
                "a guild at its claim limit still counts toward the claim");
            Assert.That(GuildKeeps.ClaimCount(new GuildKeeps.Claimant[0]), Is.Zero);
            Assert.That(GuildKeeps.ClaimCount(null), Is.Zero);
        });
    }

    private sealed class HoldingBot : GameBot
    {
        private HoldingBot() : base((OfflineWorldBotRecord)null) { }
        public override eRealm Realm { get; set; }
        public override bool IsAlive => true;
        public int PositionX;
        public override bool InCombat => false;
        public override bool IsAttacking => false;
        public override int X => PositionX;
        public override int Y => 380000;
        public override int Z => 3000;
        public override ushort CurrentRegionID { get => 1; set { } }
    }

    [Test]
    public void AMemberHoldingTheKeepItTookIsNotIdle()
    {
        using var server = new DOL.UnitTests.EpicTestServerScope();
        var keep = AlbKeep();
        var bot = (HoldingBot)RuntimeHelpers.GetUninitializedObject(typeof(HoldingBot));
        bot.Realm = eRealm.Midgard;
        bot.ObjectState = GameObject.eObjectState.Active;
        bot.PositionX = keep.X;
        typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot)).SetValue(bot, true);
        typeof(GameBot).GetProperty(nameof(GameBot.PersistentRecord)).SetValue(bot, new OfflineWorldBotRecord { ObjectiveKind = "RvR" });
        typeof(GameLiving).GetField("<TempProperties>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(bot, new PropertyCollection());
        bot.TempProperties.SetProperty("RvrEventForce", "mid-hold");

        Assert.That(L.ChooseOrJoin(Mids("mid-hold"), new[] { keep }, 1_000, 0)?.TargetId, Is.EqualTo(keep.Id));
        const long taken = 10_000;
        L.EndTarget(keep.Id, taken, eRealm.Midgard);

        Assert.Multiple(() =>
        {
            Assert.That(L.ProtectsParticipantFromInactivity(bot, taken + 1), Is.True, "holding the keep it took is not idle");
            Assert.That(L.ProtectsParticipantFromInactivity(bot, taken + L.ClaimHoldMilliseconds), Is.False, "the hold is over");
        });
        bot.PositionX = keep.X + 5_000;
        Assert.That(L.ProtectsParticipantFromInactivity(bot, taken + 1), Is.False, "a member far from the keep is not holding it");
        bot.PositionX = keep.X;
        bot.Realm = eRealm.Albion;
        Assert.That(L.ProtectsParticipantFromInactivity(bot, taken + 1), Is.False, "the hold is the capturing realm's");
    }

    [Test]
    public void TheHoldCoversTheWholeClaimWindow() =>
        Assert.That(L.ClaimHoldMilliseconds, Is.GreaterThanOrEqualTo((long)GuildKeeps.AttemptIntervalMs * GuildKeeps.MaximumAttempts));

    // ---- (i) --------------------------------------------------------------------------------------------------------

    [Test]
    public void TheSiegeNoticeNamesTheMusterPlace()
    {
        L.StagedAssault = true;
        var keep = AlbKeep();
        Assert.That(L.ChooseOrJoin(Mids("mid-muster"), new[] { keep }, 1_000, 0)?.TargetId, Is.EqualTo(keep.Id));
        string muster = L.MusterPlace(eRealm.Midgard, keep.RegionId);
        Assert.That(muster, Does.StartWith("the portal outpost in "), "Midgard musters at its outpost in Albion's frontier");
        Assert.That(L.MusterPlace(eRealm.Midgard, 100), Is.EqualTo("Svasud Faste"), "in its own frontier, at the border keep");
        Assert.That(Pending[(keep.Id, eRealm.Midgard)].Text,
            Is.EqualTo($"Warbands are gathering at {muster} to assault Caer Benowyc. Join them there!"), "the automatic siege's notice");
    }

    [Test]
    public void AForcedSiegeNoticeNamesTheMusterPlace()
    {
        L.StagedAssault = true;
        var keep = AlbKeep();
        Assert.That(L.ForceStart(keep, eRealm.Midgard, 1_000, out string reason), Is.True, reason);
        string muster = L.MusterPlace(eRealm.Midgard, keep.RegionId);
        Assume.That(muster, Is.Not.Null);
        Assert.That(Pending[(keep.Id, eRealm.Midgard)].Text,
            Is.EqualTo($"Warbands are gathering at {muster} to assault Caer Benowyc. Join them there!"));
    }

    [Test]
    public void TheRetakeCallNamesTheMusterPlace()
    {
        // A player's Midgard attack took Albion's keep (a player-attack defense the attackers won): Albion rallies to retake it.
        var keep = AlbKeep();
        Type type = typeof(L).GetNestedType("ActiveEvent", BindingFlags.NonPublic);
        object lost = Activator.CreateInstance(type, nonPublic: true);
        void Set(string field, object value) => type.GetField(field).SetValue(lost, value);
        Set("TargetId", keep.Id);
        Set("Target", keep);
        Set("AttackerRealm", eRealm.Midgard);
        Set("DefenderRealm", eRealm.Albion);
        Set("DefenseReaction", true);
        Set("CreatedTick", 1_000L);
        Set("ExpiresTick", 1_000_000L);
        var events = (IDictionary)typeof(L).GetField("Events", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        events[keep.Id] = lost;

        L.EndTarget(keep.Id, 10_000, eRealm.Midgard);

        string muster = L.MusterPlace(eRealm.Albion, keep.RegionId);
        Assume.That(muster, Is.Not.Null, "Albion has a muster in its own frontier");
        Assert.That(Pending[(keep.Id, eRealm.Albion)].Text, Is.EqualTo($"Caer Benowyc has fallen! Rally at {muster}: we take it back."));
    }

    [Test]
    public void TheNamedMusterIsReadAndSaidLikeAPlayer()
    {
        const string notice = "Warbands are gathering at the portal outpost in Hadrian's Wall to assault Caer Benowyc. Join them there!";
        Spoken.Reading reading = Spoken.Read(notice);
        Assert.That(reading?.Kind, Is.EqualTo("siege_gather"));
        Assert.That(reading?.Keep, Is.EqualTo("Caer Benowyc"));
        Assert.That(reading?.Hub, Is.EqualTo("the portal outpost in Hadrian's Wall"));
        Assert.That(Spoken.ShortMuster("the portal outpost in Hadrian's Wall"), Is.EqualTo("hadrians wall portal"));
        Assert.That(Spoken.ShortMuster("the portal outpost in the frontier"), Is.EqualTo("the portal"));
        Assert.That(Spoken.ShortMuster("Svasud Faste"), Is.EqualTo("svasud"));
        var random = new System.Random(7);
        var said = new HashSet<string>();
        for (int i = 0; i < 30; i++)
        {
            string line = Spoken.Say(notice, RealmEventNotices.KeepSlots("Caer Benowyc", eRealm.Albion), random);
            said.Add(line);
            Assert.That(line, Does.Contain("hadrians wall portal"), line);
            Assert.That(Spoken.SystemVoice(line), Is.False, line);
        }
        Assert.That(said.Count, Is.GreaterThanOrEqualTo(3));
        Assert.That(Spoken.Read("Caer Benowyc has fallen! Rally at the portal outpost in Hadrian's Wall: we take it back.")?.Kind,
            Is.EqualTo("keep_retake"));
    }
}
