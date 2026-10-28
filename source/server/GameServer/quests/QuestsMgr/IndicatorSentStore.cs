using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace DOL.GS.Quests;

// Part of the quest indicators (QuestIndicators.cs). From lometur/HearthDAoC.

// Where a client knows an NPC: the object ID and region it was sent under (a client knows objects by ID).
public readonly record struct SentPlace(int ObjectId, ushort Region);

// The wire value each player's client was last sent for each NPC, by the create packet's flags or the quest effect,
// with where the client knows it, and whether a refresh is queued for the player. Players and NPCs compare by
// reference; a player's values go with the player object, and at its Quit. No value means 0 (nothing drawn), so only
// NPCs that show a marker take room. It reads nothing from the running server, so unit tests drive it with plain
// objects.
public sealed class IndicatorSentStore
{
    private readonly record struct Sent(byte Wire, SentPlace Place);

    // Keeps watching an object ID an NPC with a marker was sent under after the NPC moved to another ID (a respawn gets
    // a new ID): the client may still draw the old object there, marker and all, until it gets a remove or the region
    // changes. Not a GameNPC, so Prune treats it as out of the world: it lasts until a create comes under that ID (a
    // reuse) or the region changes.
    private sealed class Tombstone;

    private sealed class PlayerSent
    {
        // A leaf lock: held only around Wire.
        public readonly Lock Lock = new();
        public readonly Dictionary<object, Sent> Wire = new(ReferenceEqualityComparer.Instance);
        public int Queued;
    }

    private readonly ConditionalWeakTable<object, PlayerSent> _players = new();

    private PlayerSent For(object player) => _players.GetValue(player, static _ => new PlayerSent());

    // The wire value last sent to the player's client for the NPC; 0 if none.
    public byte Last(object player, object npc)
    {
        if (!_players.TryGetValue(player, out PlayerSent sent))
            return 0;
        lock (sent.Lock)
            return sent.Wire.TryGetValue(npc, out Sent value) ? value.Wire : (byte)0;
    }

    // A value no client draws: "unknown", so the next refresh sends the real one.
    public const byte Unknown = 0xFF;

    // What the quest effect's writer just sent to the player's client for the NPC, known there by place.
    public void Record(object player, object npc, byte wire, SentPlace place) => Record(player, npc, wire, (SentPlace?)place);

    // What a refresh just sent: the writer has recorded the place already, which this keeps.
    public void Record(object player, object npc, byte wire) => Record(player, npc, wire, null);

    private void Record(object player, object npc, byte wire, SentPlace? place)
    {
        PlayerSent sent;
        if (wire != 0)
            sent = For(player);
        else if (!_players.TryGetValue(player, out sent))
            return;
        lock (sent.Lock)
            Set(sent, npc, wire, place);
    }

    private static void Set(PlayerSent sent, object npc, byte wire, SentPlace? place)
    {
        bool had = sent.Wire.TryGetValue(npc, out Sent old);
        if (had && place is SentPlace moved && moved != old.Place)
            Bury(sent, old);
        if (wire == 0)
        {
            sent.Wire.Remove(npc);
            return;
        }
        sent.Wire[npc] = new Sent(wire, place ?? (had ? old.Place : default));
    }

    // The NPC left this place for another: keep watching it, if the client may still draw a marker there.
    private static void Bury(PlayerSent sent, Sent old)
    {
        if (old.Wire == 0)
            return;
        List<object> there = null;
        foreach (KeyValuePair<object, Sent> entry in sent.Wire)
        {
            if (entry.Key is Tombstone && entry.Value.Place == old.Place)
                (there ??= new List<object>()).Add(entry.Key);
        }
        if (there != null)
        {
            foreach (object tombstone in there)
                sent.Wire.Remove(tombstone);
        }
        sent.Wire[new Tombstone()] = old;
    }

    // What a create packet just carried, for an NPC the client knows by place. A client that still has an object under
    // that ID (an NPC out of the server's range lingers on the client; one killed out of range was never removed there)
    // may take the create as an update and keep its old drawing. So, unless it is a GM's /indicator create value (probe:
    // the create alone is what the GM is looking at), a value that differs from the last one sent for this NPC, a create
    // under an ID another NPC (or a tombstone) was sent under in that region, or the NPC's own move to a new ID (a
    // respawn: the new ID may hold an unmarked object the client still draws), is recorded as Unknown and the caller asks
    // a refresh, whose quest effect draws it. True when that refresh is needed.
    public bool RecordCreate(object player, object npc, byte wire, SentPlace place, bool probe)
    {
        PlayerSent sent = For(player);
        lock (sent.Lock)
        {
            // Object IDs are unique among live objects in a region: another NPC sent under this one is gone.
            bool reused = false;
            List<object> stale = null;
            foreach (KeyValuePair<object, Sent> entry in sent.Wire)
            {
                if (!ReferenceEquals(entry.Key, npc) && entry.Value.Place == place)
                    (stale ??= new List<object>()).Add(entry.Key);
            }
            if (stale != null)
            {
                reused = true;
                foreach (object other in stale)
                    sent.Wire.Remove(other);
            }

            if (probe)
            {
                Set(sent, npc, wire, place);
                return false;
            }
            bool had = sent.Wire.TryGetValue(npc, out Sent value);
            bool moved = had && value.Place != place;
            if (!reused && !moved && (had ? value.Wire : (byte)0) == wire)
            {
                Set(sent, npc, wire, place);
                return false;
            }
            if (moved)
                Bury(sent, value);
            sent.Wire[npc] = new Sent(Unknown, place);
            return true;
        }
    }

    // How many NPCs hold a value for the player.
    public int Count(object player)
    {
        if (!_players.TryGetValue(player, out PlayerSent sent))
            return 0;
        lock (sent.Lock)
            return sent.Wire.Count;
    }

    // True when this call queued the player's refresh, so the caller posts it; false when one is already queued.
    public bool TryQueue(object player) => Interlocked.Exchange(ref For(player).Queued, 1) == 0;

    // The queued refresh starts: a request from now on queues the next one. False when the player is gone (Forget).
    public bool Dequeue(object player)
    {
        if (!_players.TryGetValue(player, out PlayerSent sent))
            return false;
        Interlocked.Exchange(ref sent.Queued, 0);
        return true;
    }

    // At a refresh, for a player now in region: drops the values sent in another region (the client dropped those
    // objects at the region change) and of NPCs that moved to another region; marks Unknown the NPCs that left the world
    // (a client out of their range when they died or were removed still draws them, so their marker is sent again when
    // they come back). inWorld and regionOf run under the leaf lock, so they must only read plain fields.
    public void Prune(object player, ushort region, Func<object, bool> inWorld, Func<object, ushort> regionOf)
    {
        if (!_players.TryGetValue(player, out PlayerSent sent))
            return;
        lock (sent.Lock)
        {
            List<object> drop = null, unknown = null;
            foreach (KeyValuePair<object, Sent> entry in sent.Wire)
            {
                if (entry.Value.Place.Region != region)
                    (drop ??= new List<object>()).Add(entry.Key);
                else if (!inWorld(entry.Key))
                {
                    if (entry.Value.Wire != Unknown)
                        (unknown ??= new List<object>()).Add(entry.Key);
                }
                else if (regionOf(entry.Key) != region)
                    (drop ??= new List<object>()).Add(entry.Key);
            }
            if (drop != null)
            {
                foreach (object npc in drop)
                    sent.Wire.Remove(npc);
            }
            if (unknown != null)
            {
                foreach (object npc in unknown)
                    sent.Wire[npc] = sent.Wire[npc] with { Wire = Unknown };
            }
        }
    }

    // The player has quit.
    public void Forget(object player) => _players.Remove(player);

    // One refresh: for each NPC the player's client has (known), its meaning and wire value now (valueOf); sends the
    // meaning where the wire value differs from the last one sent (the writer turns it into that value), and records
    // it. Returns how many it sent.
    public int SendChanges(object player, IEnumerable<object> known,
        Func<object, (eQuestIndicator Meaning, byte Wire)> valueOf, Action<object, eQuestIndicator> send)
    {
        int count = 0;
        foreach (object npc in known)
        {
            (eQuestIndicator meaning, byte wire) = valueOf(npc);
            if (wire == Last(player, npc))
                continue;
            send(npc, meaning);
            Record(player, npc, wire);
            count++;
        }
        return count;
    }
}
