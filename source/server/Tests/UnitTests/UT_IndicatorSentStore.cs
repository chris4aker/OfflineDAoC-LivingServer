using System;
using System.Collections.Generic;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

// Keeping quest markers current: the wire value last sent per (player, NPC), one queued refresh per player however often
// it is asked, and a refresh that sends only what changed. From lometur/HearthDAoC.
[TestFixture]
public sealed class UT_IndicatorSentStore
{
    private static SentPlace At(int objectId, ushort region = 51) => new(objectId, region);

    // A refresh over the NPCs the client has, with these meanings; what it sends is added to sent.
    private static int Refresh(IndicatorSentStore store, object player, object[] known,
        Dictionary<object, eQuestIndicator> meaning, List<(object, eQuestIndicator)> sent) =>
        store.SendChanges(player, known, npc => (meaning[npc], (byte)QuestIndicators.Wire(meaning[npc])),
            (npc, m) => sent.Add((npc, m)));

    [Test]
    public void NothingSentReadsAsNothing()
    {
        var store = new IndicatorSentStore();
        Assert.That(store.Last(new object(), new object()), Is.EqualTo(0));
    }

    [Test]
    public void ZeroIsNotKept()
    {
        var store = new IndicatorSentStore();
        object player = new(), elaru = new();
        store.Record(player, elaru, 8);
        Assert.That(store.Count(player), Is.EqualTo(1));
        store.Record(player, elaru, 0);
        Assert.That(store.Count(player), Is.EqualTo(0));
        Assert.That(store.Last(player, elaru), Is.EqualTo(0));
    }

    [Test]
    public void OnlyChangedValuesAreSent()
    {
        var store = new IndicatorSentStore();
        object player = new(), elaru = new(), tage = new(), maggot = new();
        object[] known = { elaru, tage, maggot };
        var meaning = new Dictionary<object, eQuestIndicator>
        {
            [elaru] = eQuestIndicator.Available, [tage] = eQuestIndicator.None, [maggot] = eQuestIndicator.None,
        };
        var sent = new List<(object, eQuestIndicator)>();
        store.Record(player, elaru, 8); // Elaru's create packet carried the yellow knot.
        Assert.That(Refresh(store, player, known, meaning, sent), Is.EqualTo(0));

        // "Strange Beings" accepted: Elaru's knot goes, Tage's white knot comes.
        meaning[elaru] = eQuestIndicator.None;
        meaning[tage] = eQuestIndicator.Pending;
        Assert.That(Refresh(store, player, known, meaning, sent), Is.EqualTo(2));
        Assert.That(sent, Is.EqualTo(new[] { (elaru, eQuestIndicator.None), (tage, eQuestIndicator.Pending) }));
        Assert.That(store.Last(player, tage), Is.EqualTo(16));
        Assert.That(Refresh(store, player, known, meaning, sent), Is.EqualTo(0));
    }

    [Test]
    public void LoreAndLessonAreNothingSoNotSent()
    {
        var store = new IndicatorSentStore();
        object player = new(), trainer = new(), merchant = new();
        var meaning = new Dictionary<object, eQuestIndicator>
        {
            [trainer] = eQuestIndicator.Lore, [merchant] = eQuestIndicator.Lesson,
        };
        var sent = new List<(object, eQuestIndicator)>();
        Assert.That(Refresh(store, player, new[] { trainer, merchant }, meaning, sent), Is.EqualTo(0));
    }

    [Test]
    public void ARawValueSentIsPutRight()
    {
        // A raw effect of 1 drew the ring; the next refresh sends the real value (8, the yellow knot).
        var store = new IndicatorSentStore();
        object gm = new(), elaru = new();
        store.Record(gm, elaru, 1);
        var sent = new List<(object, eQuestIndicator)>();
        var meaning = new Dictionary<object, eQuestIndicator> { [elaru] = eQuestIndicator.Available };
        Assert.That(Refresh(store, gm, new[] { elaru }, meaning, sent), Is.EqualTo(1));
        Assert.That(store.Last(gm, elaru), Is.EqualTo(8));
    }

    [Test]
    public void ACreateThatChangesTheValueAsksARefreshThatSendsIt()
    {
        // Elaru lingered on the client with a yellow knot; she comes back into range with nothing for the player now.
        var store = new IndicatorSentStore();
        object player = new(), elaru = new();
        store.Record(player, elaru, 8, At(101));
        Assert.That(store.RecordCreate(player, elaru, 0, At(101), probe: false), Is.True);
        Assert.That(store.Last(player, elaru), Is.EqualTo(IndicatorSentStore.Unknown));
        var sent = new List<(object, eQuestIndicator)>();
        var meaning = new Dictionary<object, eQuestIndicator> { [elaru] = eQuestIndicator.None };
        Assert.That(Refresh(store, player, new[] { elaru }, meaning, sent), Is.EqualTo(1));
        Assert.That(sent, Is.EqualTo(new[] { (elaru, eQuestIndicator.None) }));
        Assert.That(store.Last(player, elaru), Is.EqualTo(0));
    }

    [Test]
    public void ACreateWithTheSameValueAsksNothing()
    {
        var store = new IndicatorSentStore();
        object player = new(), tage = new(), guard = new();
        store.Record(player, tage, 16, At(102));
        Assert.That(store.RecordCreate(player, tage, 16, At(102), probe: false), Is.False);
        Assert.That(store.RecordCreate(player, guard, 0, At(103), probe: false), Is.False);
        Assert.That(store.Last(player, tage), Is.EqualTo(16));
    }

    [Test]
    public void AFirstCreateWithAMarkerIsSentAgainByTheRefresh()
    {
        var store = new IndicatorSentStore();
        object player = new(), elaru = new();
        Assert.That(store.RecordCreate(player, elaru, 8, At(101), probe: false), Is.True);
        var sent = new List<(object, eQuestIndicator)>();
        var meaning = new Dictionary<object, eQuestIndicator> { [elaru] = eQuestIndicator.Available };
        Assert.That(Refresh(store, player, new[] { elaru }, meaning, sent), Is.EqualTo(1));
        Assert.That(store.Last(player, elaru), Is.EqualTo(8));
    }

    [Test]
    public void SeveralRequestsQueueOneRun()
    {
        var store = new IndicatorSentStore();
        object player = new();
        Assert.That(store.TryQueue(player), Is.True);
        Assert.That(store.TryQueue(player), Is.False);
        Assert.That(store.TryQueue(player), Is.False);
        Assert.That(store.Dequeue(player), Is.True);
        Assert.That(store.TryQueue(player), Is.True); // asked again during or after the run: the next tick's run
    }

    [Test]
    public void AnEmptyCacheSendsNothing()
    {
        var store = new IndicatorSentStore();
        object player = new();
        store.Record(player, new object(), 8);
        int count = store.SendChanges(player, Array.Empty<object>(), _ => (eQuestIndicator.Finish, (byte)2),
            (_, _) => Assert.Fail("sent with nothing on the client"));
        Assert.That(count, Is.EqualTo(0));
    }

    [Test]
    public void PlayersAreSeparate()
    {
        var store = new IndicatorSentStore();
        object one = new(), two = new(), elaru = new();
        store.Record(one, elaru, 8);
        Assert.That(store.Last(two, elaru), Is.EqualTo(0));
        Assert.That(store.TryQueue(one), Is.True);
        Assert.That(store.TryQueue(two), Is.True);
    }

    [Test]
    public void PruneDropsOtherRegionsAndMarksNpcsThatLeftTheWorldUnknown()
    {
        var store = new IndicatorSentStore();
        object player = new(), elaru = new(), dead = new(), moved = new(), camelot = new();
        store.Record(player, elaru, 8, At(101));
        store.Record(player, dead, 2, At(104));
        store.Record(player, moved, 16, At(105));
        store.Record(player, camelot, 8, At(201, 10)); // sent before the player left Camelot
        store.Prune(player, 51, npc => !ReferenceEquals(npc, dead),
            npc => ReferenceEquals(npc, moved) ? (ushort)1 : (ushort)51);
        Assert.Multiple(() =>
        {
            Assert.That(store.Last(player, elaru), Is.EqualTo(8));
            Assert.That(store.Last(player, dead), Is.EqualTo(IndicatorSentStore.Unknown)); // the client may still draw it
            Assert.That(store.Last(player, moved), Is.EqualTo(0));
            Assert.That(store.Last(player, camelot), Is.EqualTo(0));
        });
    }

    [Test]
    public void ANpcThatLeftTheWorldGetsItsMarkerResentWhenItIsBack()
    {
        // A killed Adam Glaze (blue knot) still drawn by a client out of range; he respawns with nothing for the player.
        var store = new IndicatorSentStore();
        object player = new(), adam = new();
        store.Record(player, adam, 2, At(106));
        store.Prune(player, 51, _ => false, _ => 51);
        Assert.That(store.RecordCreate(player, adam, 0, At(107), probe: false), Is.True);
    }

    [Test]
    public void ACreateForAnObjectIdAnotherNpcHadAsksARefresh()
    {
        // The client may still draw the old NPC under that object ID, with its marker: the new one's value is re-sent.
        var store = new IndicatorSentStore();
        object player = new(), old = new(), newcomer = new();
        store.Record(player, old, 8, At(101));
        Assert.That(store.RecordCreate(player, newcomer, 0, At(101), probe: false), Is.True);
        Assert.That(store.Last(player, old), Is.EqualTo(0)); // gone: object IDs are unique among live objects
        Assert.That(store.Last(player, newcomer), Is.EqualTo(IndicatorSentStore.Unknown));
    }

    [Test]
    public void TheSameObjectIdInAnotherRegionIsAnotherObject()
    {
        var store = new IndicatorSentStore();
        object player = new(), old = new(), other = new();
        store.Record(player, old, 8, At(101, 1));
        Assert.That(store.RecordCreate(player, other, 0, At(101, 51), probe: false), Is.False);
        Assert.That(store.Last(player, old), Is.EqualTo(8));
    }

    [Test]
    public void AnIndicatorCreateValueIsRecordedWithoutARefresh()
    {
        // A probe create (finish) on Elaru: the create packet alone draws it, with no quest effect after.
        var store = new IndicatorSentStore();
        object gm = new(), elaru = new();
        store.Record(gm, elaru, 8, At(101));
        Assert.That(store.RecordCreate(gm, elaru, 2, At(101), probe: true), Is.False);
        Assert.That(store.Last(gm, elaru), Is.EqualTo(2));
        Assert.That(store.RecordCreate(gm, elaru, 0, At(101), probe: true), Is.False);
        Assert.That(store.Count(gm), Is.EqualTo(0));
    }

    [Test]
    public void ARespawnUnderANewObjectIdAsksARefreshEvenWithTheSameValue()
    {
        // A moss sheerie (white knot) dies and respawns under a new object ID, which the client may still use for an
        // unmarked mob that died out of range: the same value is sent again by quest effect.
        var store = new IndicatorSentStore();
        object player = new(), moss = new(), guard = new();
        store.Record(player, moss, 16, At(101));
        Assert.That(store.RecordCreate(player, moss, 16, At(107), probe: false), Is.True);
        store.Record(player, moss, 16); // the refresh
        Assert.That(store.Last(player, moss), Is.EqualTo(16));
        // Its place moved to 107: another NPC created there later is a reuse of it.
        Assert.That(store.RecordCreate(player, guard, 0, At(107), probe: false), Is.True);
    }

    [Test]
    public void TheOldObjectIdOfAnNpcThatMovedIsStillWatched()
    {
        // The client may still draw the dead moss sheerie under 101 with its white knot: a rat created there later gets
        // its own value sent by quest effect.
        var store = new IndicatorSentStore();
        object player = new(), moss = new(), rat = new();
        store.Record(player, moss, 16, At(101));
        store.RecordCreate(player, moss, 16, At(102), probe: false);
        Assert.That(store.RecordCreate(player, rat, 0, At(101), probe: false), Is.True);
        // And once that is settled, a later create there is ordinary again.
        store.Record(player, rat, 0);
        Assert.That(store.RecordCreate(player, rat, 0, At(101), probe: false), Is.False);
    }

    [Test]
    public void TheOldObjectIdsAreForgottenAtARegionChange()
    {
        var store = new IndicatorSentStore();
        object player = new(), moss = new();
        store.Record(player, moss, 16, At(101));
        store.RecordCreate(player, moss, 16, At(102), probe: false);
        store.Prune(player, 10, _ => true, _ => 51); // the player is in Camelot now
        Assert.That(store.Count(player), Is.EqualTo(0));
    }

    [Test]
    public void AProbeCreateStillDropsAnotherNpcSentUnderItsObjectId()
    {
        var store = new IndicatorSentStore();
        object gm = new(), old = new(), elaru = new();
        store.Record(gm, old, 8, At(101));
        Assert.That(store.RecordCreate(gm, elaru, 2, At(101), probe: true), Is.False);
        Assert.That(store.Last(gm, old), Is.EqualTo(0));
        Assert.That(store.Last(gm, elaru), Is.EqualTo(2));
    }

    [Test]
    public void ARefreshKeepsWhereTheWriterSaidTheClientKnowsTheNpc()
    {
        // The writer records the place; the refresh's own record must not lose it, or a reused ID would go unseen.
        var store = new IndicatorSentStore();
        object player = new(), elaru = new(), newcomer = new();
        store.Record(player, elaru, 8, At(101));
        store.Record(player, elaru, 16);
        Assert.That(store.Last(player, elaru), Is.EqualTo(16));
        Assert.That(store.RecordCreate(player, newcomer, 0, At(101), probe: false), Is.True);
    }

    [Test]
    public void ForgetDropsThePlayerAndItsQueue()
    {
        var store = new IndicatorSentStore();
        object player = new(), elaru = new();
        store.Record(player, elaru, 2);
        Assert.That(store.TryQueue(player), Is.True);
        store.Forget(player);
        Assert.That(store.Last(player, elaru), Is.EqualTo(0));
        Assert.That(store.Dequeue(player), Is.False); // the posted run finds the player gone
    }
}
