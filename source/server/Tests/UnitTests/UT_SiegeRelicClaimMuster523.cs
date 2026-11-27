using System.Collections.Generic;
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
    [SetUp] public void ResetEvents() => RvrEventTestState.Clear();
    [TearDown] public void ClearEvents() => RvrEventTestState.Clear();

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
