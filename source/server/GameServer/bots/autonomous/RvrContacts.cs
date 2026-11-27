using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace DOL.GS;

/// <summary>How a contact became known (RvrContacts). A contact keeps every way it was known and the last one.</summary>
[Flags]
public enum RvrContactSource
{
    None = 0,
    /// <summary>The bot noticed it by the perception rule (AutonomousRvrPerception), the stealth rule included.</summary>
    Noticed = 1,
    /// <summary>A mate called it ("inc"): the bot was told, it did not see it itself.</summary>
    Called = 2,
    /// <summary>It was firing on the realm near the bot or attacking its group: answered at once, without the notice rule.</summary>
    Attacked = 4,
    /// <summary>It hit the bot (damage or a spell landed).</summary>
    Hit = 8,
    /// <summary>The bot heard a realm alarm about it (written from #523 / piece C step 5 on; no writer yet).</summary>
    Alarm = 16,
    /// <summary>A keep the bot's realm holds raised an alarm about it (written from #523 on; no writer yet).</summary>
    KeepAlarm = 32,
    /// <summary>
    /// Taken under the old rules, without the notice rule: at an objective (RvrFieldRules.None), with the perception rule
    /// off, or a kind the rule leaves out (siege engines, guards, enemy gamebots when their rule is off).
    /// </summary>
    Acquired = 64,
}

/// <summary>One write to the contact table: the enemy as the writer sees it now.</summary>
public readonly record struct RvrContactSighting(long Target, long Party, int TargetRealm, RvrPerceivedKind Kind, ushort Region,
    int X, int Y, int Z, GameLiving Subject = null);

/// <summary>What one bot (or, for the realm union, its realm) knows of one enemy at the moment it was read.</summary>
public readonly record struct RvrContact(long Owner, int OwnerRealm, long Target, long Party, int TargetRealm, RvrPerceivedKind Kind,
    ushort Region, int X, int Y, int Z, long FirstSeen, long LastSeen, RvrContactSource Sources, RvrContactSource LastSource,
    GameLiving Subject)
{
    /// <summary>How long ago it was last seen, heard of or felt.</summary>
    public long AgeMilliseconds(long now) => Math.Max(0, now - LastSeen);

    /// <summary>How sure the knower is it is still there: 1 when just seen, halving every HalfLife.</summary>
    public double Confidence(long now) => RvrContacts.Confidence(AgeMilliseconds(now));

    /// <summary>Whether the knower saw it itself (noticed, acquired, it attacked or hit) rather than being told.</summary>
    public bool Firsthand => (Sources & RvrContacts.FirsthandSources) != 0;
}

/// <summary>Heads and parties of one enemy realm a realm has seen near a point (RvrContacts.Near).</summary>
public readonly record struct RvrContactCount(int Heads, int Players, int Parties, long LatestSeen);

/// <summary>
/// #529 (RvR work 11, piece B; docs/rvr-audit-1.2.3/structure.md finding 1 and section 5 B): one contact table per
/// autonomous gamebot, read per bot, per group and per realm. A contact is what the bot knows of one enemy: when it first
/// and last saw it, where, how (noticed, called, attacked, hit, alarm, acquired), and a confidence that decays. It is
/// written ONLY by the perception rule's scan (PerceiveRvrTargets), a mate's call (AlertRvrMates), damage
/// (BotBrain.OnAttackedByEnemy) and, from #523 on, alarms; never by an area search. A realm's strategic view is the
/// union of its bots' contacts: what its people have seen, which is also what makes the player a contact like any other.
///
/// Step 1-2 (this build): a facade and a shadow. Nothing decides from it yet except the /faction alarm head count; the
/// fight board compares its area search with it and logs the difference (RvrContactShadow). Steps 3-6 on #529.
///
/// Readers to come: the board's switch to the called target (step 3: <see cref="KnownByAny"/>), hunts and roam weights
/// (step 4, #524: <see cref="Realm"/>), the plan's first pick (#531: <see cref="Proposals"/>), the defense sized by what
/// is seen near a keep (#523: <see cref="Near"/>). Pure apart from its own state; thread-safe.
/// </summary>
public sealed class RvrContacts
{
    /// <summary>The confidence of a contact halves every this long.</summary>
    public const long HalfLifeMilliseconds = 30_000;
    /// <summary>A contact is forgotten this long after it was last seen (confidence 1/16).</summary>
    public const long ForgetAfterMilliseconds = 120_000;
    /// <summary>The ways of knowing an enemy by one's own eyes or skin.</summary>
    public const RvrContactSource FirsthandSources =
        RvrContactSource.Noticed | RvrContactSource.Attacked | RvrContactSource.Hit | RvrContactSource.Acquired;

    private const long SweepEveryMilliseconds = 10_000;

    private sealed class Entry
    {
        public long Party;
        public int TargetRealm;
        public RvrPerceivedKind Kind;
        public ushort Region;
        public int X, Y, Z;
        public long FirstSeen, LastSeen;
        public RvrContactSource Sources, LastSource;
        public GameLiving Subject;
    }

    private sealed class Owner
    {
        public int Realm;
        public readonly Dictionary<long, Entry> Entries = new();
    }

    private readonly ConcurrentDictionary<long, Owner> _owners = new();
    // The realm union: per (realm, enemy) the latest write by any of the realm's bots, and how many of them know it.
    private readonly ConcurrentDictionary<(int Realm, long Target), Entry> _realm = new();
    private long _nextSweep;
    private long _writes;

    /// <summary>How many writes the table took since start (a load counter for the logs and tests).</summary>
    public long Writes => Interlocked.Read(ref _writes);

    /// <summary>How many bots hold at least one contact.</summary>
    public int Owners => _owners.Count;

    /// <summary>The confidence after this age: 1 at 0, halving every HalfLife, 0 once forgotten. Pure.</summary>
    public static double Confidence(long ageMilliseconds) =>
        ageMilliseconds > ForgetAfterMilliseconds ? 0 : Math.Pow(0.5, Math.Max(0, ageMilliseconds) / (double)HalfLifeMilliseconds);

    /// <summary>
    /// <paramref name="owner"/> (a bot's database id) of <paramref name="ownerRealm"/> knows of the enemy now, by
    /// <paramref name="source"/>. A fresh write refreshes the place and the time; an older one (now before the last
    /// sight) only adds its source. A contact forgotten long enough starts again (a new first sight).
    /// </summary>
    public void Note(long owner, int ownerRealm, in RvrContactSighting sighting, RvrContactSource source, long now)
    {
        if (owner == 0 || source == RvrContactSource.None)
            return;
        Sweep(now);
        Interlocked.Increment(ref _writes);
        Owner table = _owners.GetOrAdd(owner, static _ => new Owner());
        lock (table)
        {
            table.Realm = ownerRealm;
            if (!table.Entries.TryGetValue(sighting.Target, out Entry entry) || now - entry.LastSeen > ForgetAfterMilliseconds)
                table.Entries[sighting.Target] = entry = new Entry { FirstSeen = now, LastSeen = now };
            Apply(entry, sighting, source, now);
        }
        Entry union = _realm.GetOrAdd((ownerRealm, sighting.Target), static _ => new Entry { FirstSeen = long.MaxValue });
        lock (union)
        {
            if (now - union.LastSeen > ForgetAfterMilliseconds)
            {
                union.FirstSeen = now;
                union.Sources = RvrContactSource.None;
            }
            union.FirstSeen = Math.Min(union.FirstSeen, now);
            Apply(union, sighting, source, now);
        }
    }

    private static void Apply(Entry entry, in RvrContactSighting sighting, RvrContactSource source, long now)
    {
        entry.Sources |= source;
        if (now < entry.LastSeen)
            return;
        entry.Party = sighting.Party;
        entry.TargetRealm = sighting.TargetRealm;
        entry.Kind = sighting.Kind;
        entry.Region = sighting.Region;
        entry.X = sighting.X;
        entry.Y = sighting.Y;
        entry.Z = sighting.Z;
        entry.LastSeen = now;
        entry.LastSource = source;
        if (sighting.Subject != null)
            entry.Subject = sighting.Subject;
    }

    private static RvrContact Read(long owner, int realm, long target, Entry entry) => new(owner, realm, target, entry.Party,
        entry.TargetRealm, entry.Kind, entry.Region, entry.X, entry.Y, entry.Z, entry.FirstSeen, entry.LastSeen, entry.Sources,
        entry.LastSource, entry.Subject);

    /// <summary>The bot's contact with the enemy, if it has a live one.</summary>
    public bool TryGet(long owner, long target, long now, out RvrContact contact)
    {
        contact = default;
        if (!_owners.TryGetValue(owner, out Owner table))
            return false;
        lock (table)
        {
            if (!table.Entries.TryGetValue(target, out Entry entry) || now - entry.LastSeen > ForgetAfterMilliseconds)
                return false;
            contact = Read(owner, table.Realm, target, entry);
            return true;
        }
    }

    /// <summary>Whether the bot has a live contact with the enemy, at most <paramref name="maxAge"/> old.</summary>
    public bool Knows(long owner, long target, long now, long maxAge = ForgetAfterMilliseconds) =>
        TryGet(owner, target, now, out RvrContact contact) && contact.AgeMilliseconds(now) <= maxAge;

    /// <summary>
    /// The group question (step 3): the freshest live contact any of <paramref name="owners"/> has with the enemy, at
    /// most <paramref name="maxAge"/> old.
    /// </summary>
    public bool KnownByAny(IReadOnlyList<long> owners, long target, long now, out RvrContact freshest,
        long maxAge = ForgetAfterMilliseconds)
    {
        freshest = default;
        bool found = false;
        if (owners == null)
            return false;
        foreach (long owner in owners)
            if (TryGet(owner, target, now, out RvrContact contact) && contact.AgeMilliseconds(now) <= maxAge &&
                (!found || contact.LastSeen > freshest.LastSeen))
            {
                freshest = contact;
                found = true;
            }
        return found;
    }

    /// <summary>The bot's live contacts, at most <paramref name="maxAge"/> old, in no order.</summary>
    public List<RvrContact> Of(long owner, long now, long maxAge = ForgetAfterMilliseconds)
    {
        var list = new List<RvrContact>();
        if (!_owners.TryGetValue(owner, out Owner table))
            return list;
        lock (table)
            foreach (var pair in table.Entries)
                if (now - pair.Value.LastSeen <= Math.Min(maxAge, ForgetAfterMilliseconds))
                    list.Add(Read(owner, table.Realm, pair.Key, pair.Value));
        return list;
    }

    /// <summary>
    /// What the plan may pick from (#531, "the scan proposes contacts, the plan picks"): the bot's contacts in
    /// <paramref name="region"/> at most <paramref name="maxAge"/> old, those it saw itself first, then the freshest, then
    /// the nearest to (<paramref name="x"/>, <paramref name="y"/>), then by key so the order is stable.
    /// </summary>
    public List<RvrContact> Proposals(long owner, ushort region, int x, int y, long now, long maxAge)
    {
        List<RvrContact> list = Of(owner, now, maxAge);
        list.RemoveAll(contact => contact.Region != region);
        list.Sort((a, b) =>
        {
            int c = b.Firsthand.CompareTo(a.Firsthand);
            if (c != 0) return c;
            c = b.LastSeen.CompareTo(a.LastSeen);
            if (c != 0) return c;
            c = DistanceSquared(a, x, y).CompareTo(DistanceSquared(b, x, y));
            return c != 0 ? c : a.Target.CompareTo(b.Target);
        });
        return list;
    }

    /// <summary>
    /// The realm union (step 4, #524): one row per enemy that any bot of <paramref name="realm"/> knows of in
    /// <paramref name="region"/>, at most <paramref name="maxAge"/> old, with the latest place and time and every way it
    /// was known. Owner is 0 in these rows.
    /// </summary>
    public List<RvrContact> Realm(int realm, ushort region, long now, long maxAge)
    {
        var list = new List<RvrContact>();
        long limit = Math.Min(maxAge, ForgetAfterMilliseconds);
        foreach (var pair in _realm)
        {
            if (pair.Key.Realm != realm)
                continue;
            Entry entry = pair.Value;
            lock (entry)
                if (entry.Region == region && now - entry.LastSeen <= limit)
                    list.Add(Read(0, realm, pair.Key.Target, entry));
        }
        return list;
    }

    /// <summary>
    /// What <paramref name="realm"/> has seen of <paramref name="enemyRealm"/> within <paramref name="radius"/> of a point
    /// (#523: a defense sized by what is seen, ending some minutes after they leave): heads, players among them, parties,
    /// and when the latest was seen. <paramref name="enemyRealm"/> 0 counts every enemy realm. Pets are not heads.
    /// </summary>
    public RvrContactCount Near(int realm, int enemyRealm, ushort region, int x, int y, int radius, long now, long maxAge)
    {
        long squared = (long)radius * radius;
        int heads = 0, players = 0;
        long latest = 0;
        var parties = new HashSet<long>();
        foreach (RvrContact contact in Realm(realm, region, now, maxAge))
        {
            if (enemyRealm != 0 && contact.TargetRealm != enemyRealm || contact.Kind is RvrPerceivedKind.PlayerPet or RvrPerceivedKind.EnemyBotPet ||
                DistanceSquared(contact, x, y) > squared)
                continue;
            heads++;
            if (contact.Kind == RvrPerceivedKind.Player)
                players++;
            parties.Add(contact.Party);
            latest = Math.Max(latest, contact.LastSeen);
        }
        return new RvrContactCount(heads, players, parties.Count, latest);
    }

    private static long DistanceSquared(in RvrContact contact, int x, int y) =>
        (long)(contact.X - x) * (contact.X - x) + (long)(contact.Y - y) * (contact.Y - y);

    /// <summary>Forgets one bot's contacts (it left the world, died out of RvR; tests).</summary>
    public void Forget(long owner) => _owners.TryRemove(owner, out _);

    private void Sweep(long now)
    {
        if (now < Volatile.Read(ref _nextSweep))
            return;
        Volatile.Write(ref _nextSweep, now + SweepEveryMilliseconds);
        foreach (var pair in _owners)
        {
            Owner table = pair.Value;
            bool empty;
            lock (table)
            {
                List<long> stale = null;
                foreach (var entry in table.Entries)
                    if (now - entry.Value.LastSeen > ForgetAfterMilliseconds)
                        (stale ??= new List<long>()).Add(entry.Key);
                if (stale != null)
                    foreach (long key in stale)
                        table.Entries.Remove(key);
                empty = table.Entries.Count == 0;
            }
            if (empty)
                _owners.TryRemove(pair.Key, out _);
        }
        foreach (var pair in _realm)
            if (now - Volatile.Read(ref pair.Value.LastSeen) > ForgetAfterMilliseconds)
                _realm.TryRemove(pair.Key, out _);
    }

    public void Clear()
    {
        _owners.Clear();
        _realm.Clear();
        Interlocked.Exchange(ref _writes, 0);
        Volatile.Write(ref _nextSweep, 0);
    }

    /// <summary>
    /// The contact source of one perception result (PerceiveRvrTargets and the rehearsal's scan share it): a call is
    /// Called, anything else the bot noticed is Noticed; an unseen or capped result is no contact (None). Pure.
    /// </summary>
    public static RvrContactSource SourceOf(RvrScanResult result, RvrNoticeFactors factors) => result switch
    {
        RvrScanResult.Engage or RvrScanResult.Reacting =>
            (factors & RvrNoticeFactors.Alerted) != 0 ? RvrContactSource.Called : RvrContactSource.Noticed,
        RvrScanResult.Noticed => RvrContactSource.Noticed,
        _ => RvrContactSource.None,
    };

    /// <summary>
    /// The source of a target the scan let through without the notice rule: Attacked when it is firing on the realm
    /// near the bot or attacking its group, otherwise Acquired. Pure.
    /// </summary>
    public static RvrContactSource SourceWithoutRule(bool firingOnRealm, bool attackingGroup) =>
        firingOnRealm || attackingGroup ? RvrContactSource.Attacked : RvrContactSource.Acquired;
}
