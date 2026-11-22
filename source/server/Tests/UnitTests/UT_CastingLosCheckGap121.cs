using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Spells;
using NUnit.Framework;

namespace DOL.GS.Tests;

/// <summary>
/// 1.2.1, the gap behind the 1.2 casting crash (UT_CastingLosCheckNull43c): a line-of-sight checker is a real player's
/// client, and it can't always be asked. It has no packet sender before its login completes, and it refuses the request
/// while it is not playing or when an end of the check has no object id. The refusal used to be dropped, so an
/// end-of-cast check stayed pending for ever (its damage never dealt) and an NPC's spell waited for ever. Tests only.
/// </summary>
[TestFixture]
public sealed class UT_CastingLosCheckGap121
{
    /// <summary>A client's packet sender that takes or refuses a line-of-sight request, and counts them.</summary>
    public class Packets : DispatchProxy
    {
        public bool Takes;
        public int Asked;

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name != nameof(IPacketLib.SendLosCheckRequest))
                return null;

            Asked++;
            return Takes;
        }
    }

    private sealed class Checker : GamePlayer
    {
        private Checker() : base(null, null) { }
        public IPacketLib Sender;
        public override IPacketLib Out => Sender;
    }

    private static Packets NewPackets(bool takes)
    {
        var packets = (Packets)(object)DispatchProxy.Create<IPacketLib, Packets>();
        packets.Takes = takes;
        return packets;
    }

    private static Checker NewChecker(Packets packets, ushort objectId = 30)
    {
        var checker = (Checker)RuntimeHelpers.GetUninitializedObject(typeof(Checker));
        checker.Sender = (IPacketLib)(object)packets;
        checker.ObjectID = objectId;
        return checker;
    }

    private static GameNPC Npc(ushort objectId)
    {
        var npc = (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));
        npc.ObjectID = objectId;
        return npc;
    }

    private static SpellHandler Handler(GamePlayer checker)
    {
        var handler = (SpellHandler)RuntimeHelpers.GetUninitializedObject(typeof(SpellHandler));
        handler.LosChecker = checker;
        return handler;
    }

    private static int PendingEndOfCastChecks(CastingComponent component)
    {
        const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        object listener = typeof(CastingComponent).GetField("_endOfCastLosCheckListener", Private).GetValue(component);
        var pending = (IDictionary)listener.GetType().GetField("_pendingLosChecks", Private).GetValue(listener);
        return pending?.Count ?? 0;
    }

    [Test]
    public void ARefusedEndOfCastCheckIsNoCheckAndLeavesNothingPending()
    {
        Packets packets = NewPackets(takes: false);
        var component = new NpcCastingComponent(Npc(10));

        Assert.Multiple(() =>
        {
            Assert.That(component.StartEndOfCastLosCheck(Npc(20), Handler(NewChecker(packets))), Is.False,
                "the caller applies the spell itself, as with no checker; before 1.2.1 it waited for an answer that never came");
            Assert.That(packets.Asked, Is.EqualTo(1));
            Assert.That(PendingEndOfCastChecks(component), Is.Zero);
        });
    }

    [Test]
    public void ATakenEndOfCastCheckWaitsForItsAnswer()
    {
        Packets packets = NewPackets(takes: true);
        var component = new NpcCastingComponent(Npc(10));

        Assert.Multiple(() =>
        {
            Assert.That(component.StartEndOfCastLosCheck(Npc(20), Handler(NewChecker(packets))), Is.True);
            Assert.That(packets.Asked, Is.EqualTo(1));
            Assert.That(PendingEndOfCastChecks(component), Is.EqualTo(1));
        });
    }

    [Test]
    public void ARefusedDuringCastCheckIsNoCheck()
    {
        Packets refuses = NewPackets(takes: false);
        Packets takes = NewPackets(takes: true);
        var component = new NpcCastingComponent(Npc(10));

        Assert.Multiple(() =>
        {
            Assert.That(component.StartDuringCastLosCheck(Npc(20), Handler(NewChecker(refuses))), Is.False);
            Assert.That(component.StartDuringCastLosCheck(Npc(20), Handler(NewChecker(takes))), Is.True);
            Assert.That(refuses.Asked, Is.EqualTo(1));
            Assert.That(takes.Asked, Is.EqualTo(1));
        });
    }

    [Test]
    public void ACheckerWithNoPacketSenderIsNoChecker()
    {
        var component = new NpcCastingComponent(Npc(10));
        Checker checker = NewChecker(null);

        Assert.Multiple(() =>
        {
            Assert.That(component.StartDuringCastLosCheck(Npc(20), Handler(checker)), Is.False);
            Assert.That(component.StartEndOfCastLosCheck(Npc(20), Handler(checker)), Is.False);
            Assert.That(PendingEndOfCastChecks(component), Is.Zero);
        });
    }

    [Test]
    public void ATargetThatLeftTheWorldIsNotAskedAboutAndGetsNoSpell()
    {
        Packets packets = NewPackets(takes: true);
        var component = new NpcCastingComponent(Npc(10));
        GameNPC gone = Npc(0);

        Assert.Multiple(() =>
        {
            Assert.That(component.StartEndOfCastLosCheck(gone, Handler(NewChecker(packets))), Is.True,
                "true: the caller does not apply the spell");
            Assert.That(component.StartDuringCastLosCheck(gone, Handler(NewChecker(packets))), Is.True,
                "true: the cast's view is left as it was");
            Assert.That(packets.Asked, Is.Zero);
            Assert.That(PendingEndOfCastChecks(component), Is.Zero);
        });
    }

    private static Spell Nuke() =>
        new(new DbSpell { Type = eSpellType.DirectDamage.ToString(), Target = "Enemy", Range = 1500 }, 20);

    [Test]
    public void AnNpcsSpellWaitsOnlyForACheckThatWasTaken()
    {
        Packets packets = NewPackets(takes: true);
        var component = new NpcCastingComponent(Npc(10));
        Checker target = NewChecker(packets);   // a player target checks its own line of sight

        Assert.Multiple(() =>
        {
            Assert.That(component.RequestCastSpell(Nuke(), null, null, target), Is.True);
            Assert.That(packets.Asked, Is.EqualTo(1));
            Assert.That(component.HasPendingLosCheckRequests, Is.True);
        });
    }

    [Test]
    public void AnNpcDoesNotCastAtATargetThatLeftTheWorld()
    {
        Packets packets = NewPackets(takes: true);
        var component = new NpcCastingComponent(Npc(10));
        Checker gone = NewChecker(packets, objectId: 0);

        Assert.Multiple(() =>
        {
            Assert.That(component.RequestCastSpell(Nuke(), null, null, gone), Is.False);
            Assert.That(packets.Asked, Is.Zero);
            Assert.That(component.HasPendingLosCheckRequests, Is.False, "before 1.2.1 the spell waited for ever and the NPC counted as casting");
        });
    }
}
