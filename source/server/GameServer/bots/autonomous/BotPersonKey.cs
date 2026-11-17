using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace DOL.GS;

/// <summary>What a <see cref="BotPersonKey"/> names: a bot character today, a bot player (who owns characters) later.</summary>
public enum BotPersonKind : byte
{
    /// <summary>No person (a companion, an in-memory bot, a bot without a roster row).</summary>
    None = 0,
    /// <summary>A gamebot character (offline_world_bots.BotId), written "c:1234". Every key is one of these in 1.2.</summary>
    Character = 1,
    /// <summary>A bot player who owns characters (items 1 and 3 of "Bots as people", 2.0), written "p:56".</summary>
    Player = 2,
}

/// <summary>
/// The key of every "bots as people" record (docs/BOTS-AS-PEOPLE-1.2.md, "One key for later"; work package 0). It is the
/// bot character today and can become the bot player later, so the bot-player model of 2.0 is a migration (call
/// <see cref="BotPeopleMerge"/>, then change <see cref="Of(long)"/>), not a rewrite. Saved as text: "c:1234", "p:56".
/// Every table that uses it also keeps a CharacterId column, which today is the same bot.
/// </summary>
public readonly record struct BotPersonKey(BotPersonKind Kind, long Id) : IComparable<BotPersonKey>
{
    /// <summary>The longest text a key can have ("p:" and a long), the Varchar of every PersonKey column.</summary>
    public const int TextLength = 24;

    public static BotPersonKey None => default;

    public bool IsNone => Kind == BotPersonKind.None || Id <= 0;
    public bool IsCharacter => Kind == BotPersonKind.Character && Id > 0;
    public bool IsPlayer => Kind == BotPersonKind.Player && Id > 0;

    public static BotPersonKey Character(long characterId) => characterId > 0 ? new(BotPersonKind.Character, characterId) : default;
    public static BotPersonKey Player(long playerId) => playerId > 0 ? new(BotPersonKind.Player, playerId) : default;

    /// <summary>
    /// The person a gamebot character belongs to. In 1.2 every character is its own person ("c:id"). This is the one
    /// place the bot-player model changes later (to look the character's owner up). Pure.
    /// </summary>
    public static BotPersonKey Of(long characterId) => Character(characterId);

    /// <summary>
    /// The person of a live gamebot, or <see cref="None"/> for anything that is not a saved autonomous gamebot (a
    /// companion, a /spawn or /raid helper, an Epic Special Force, a No Lifer's buffbot, a real player). It does not
    /// check the scope: callers ask <see cref="BotPeopleScope"/> for that.
    /// </summary>
    public static BotPersonKey Of(GameBot bot) =>
        bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsPlayerCompanion: false, IsInMemoryOnly: false }
            ? Of(bot.DatabaseID)
            : default;

    /// <summary>"c:1234", "p:56", or "" for no person.</summary>
    public override string ToString() => IsNone ? string.Empty :
        (Kind == BotPersonKind.Player ? "p:" : "c:") + Id.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads "c:1234" or "p:56" (lower case, a positive id, nothing else). Pure.</summary>
    public static bool TryParse(string text, out BotPersonKey key)
    {
        key = default;
        if (string.IsNullOrEmpty(text) || text.Length < 3 || text.Length > TextLength || text[1] != ':')
            return false;
        BotPersonKind kind = text[0] switch { 'c' => BotPersonKind.Character, 'p' => BotPersonKind.Player, _ => BotPersonKind.None };
        if (kind == BotPersonKind.None)
            return false;
        ReadOnlySpan<char> digits = text.AsSpan(2);
        foreach (char c in digits)
            if (c is < '0' or > '9')
                return false;
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long id) || id <= 0)
            return false;
        key = new BotPersonKey(kind, id);
        return true;
    }

    public static BotPersonKey Parse(string text) =>
        TryParse(text, out BotPersonKey key) ? key : throw new FormatException($"Not a bot person key: \"{text}\"");

    public int CompareTo(BotPersonKey other) => Kind != other.Kind ? Kind.CompareTo(other.Kind) : Id.CompareTo(other.Id);

    // ---- The merge rules (docs/BOTS-AS-PEOPLE-1.2.md, "What belongs to the person, and what to the character"). ----

    /// <summary>Skill: the highest experience per facet and the highest tier (a player's skill carries to an alt).</summary>
    public static OfflineBotSkillRecord Merge(BotPersonKey person, IEnumerable<OfflineBotSkillRecord> rows) =>
        BotPeopleMerge.Skill(person, rows);

    /// <summary>Places: the union per realm (an alt in the same realm knows the map).</summary>
    public static OfflineBotPlacesRecord[] Merge(BotPersonKey person, IEnumerable<OfflineBotPlacesRecord> rows) =>
        BotPeopleMerge.Places(person, rows);

    /// <summary>Mechanics seen (WP2R): the sum of Seen per mechanic.</summary>
    public static OfflineBotMechanicSeenRecord[] Merge(BotPersonKey person, IEnumerable<OfflineBotMechanicSeenRecord> rows) =>
        BotPeopleMerge.MechanicsSeen(person, rows);

    /// <summary>Milestone arcs: kept per character (a level is a character's), re-keyed to the person.</summary>
    public static OfflineBotMilestoneRecord[] Merge(BotPersonKey person, IEnumerable<OfflineBotMilestoneRecord> rows) =>
        BotPeopleMerge.Milestones(person, rows);
}

/// <summary>
/// The merge rules for when several characters join one bot player (pure; UT_BotPersonKey). Each returns new rows keyed
/// by the person, never the inputs; <see cref="BotPeopleStore.MergeInto"/> swaps them in. The "main" character of a
/// merged person row (its CharacterId and, for skill, its Realm) is the row with the most experience, or the latest
/// update, then the lowest CharacterId, so a merge never depends on the order of its inputs.
/// </summary>
public static class BotPeopleMerge
{
    public static OfflineBotSkillRecord Skill(BotPersonKey person, IEnumerable<OfflineBotSkillRecord> rows)
    {
        OfflineBotSkillRecord[] all = rows?.Where(row => row != null).ToArray() ?? [];
        if (all.Length == 0)
            return null;
        OfflineBotSkillRecord main = all.OrderByDescending(row => row.TotalExperience)
            .ThenByDescending(row => row.UpdatedUtc, StringComparer.Ordinal).ThenBy(row => row.CharacterId).First();
        var merged = new OfflineBotSkillRecord
        {
            PersonKey = person.ToString(),
            CharacterId = main.CharacterId,
            Realm = main.Realm,
            Tier = all.Max(row => row.Tier),
            UpdatedUtc = all.Select(row => row.UpdatedUtc ?? string.Empty).Max(StringComparer.Ordinal),
        };
        foreach (BotSkillFacet facet in BotSkillFacets.All)
            merged.SetExperience(facet, all.Max(row => row.Experience(facet)));
        return merged;
    }

    public static OfflineBotPlacesRecord[] Places(BotPersonKey person, IEnumerable<OfflineBotPlacesRecord> rows) =>
        (rows ?? []).Where(row => row != null).GroupBy(row => row.Realm).OrderBy(group => group.Key).Select(group =>
        {
            OfflineBotPlacesRecord main = group.OrderByDescending(row => row.UpdatedUtc, StringComparer.Ordinal)
                .ThenBy(row => row.CharacterId).First();
            string known = group.Aggregate(string.Empty, (bits, row) => BotPeopleBits.Union(bits, row.Known));
            string half = group.Aggregate(string.Empty, (bits, row) => BotPeopleBits.Union(bits, row.HalfKnown));
            return new OfflineBotPlacesRecord
            {
                PersonKey = person.ToString(),
                Realm = group.Key,
                CharacterId = main.CharacterId,
                Known = known,
                // A place one character knows is known; half known only where none of them knows it.
                HalfKnown = BotPeopleBits.Except(half, known),
                UpdatedUtc = group.Select(row => row.UpdatedUtc ?? string.Empty).Max(StringComparer.Ordinal),
            };
        }).ToArray();

    public static OfflineBotMechanicSeenRecord[] MechanicsSeen(BotPersonKey person, IEnumerable<OfflineBotMechanicSeenRecord> rows) =>
        (rows ?? []).Where(row => row != null).GroupBy(row => row.Mechanic ?? string.Empty, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
            {
                OfflineBotMechanicSeenRecord main = group.OrderByDescending(row => row.LastSeenUtc, StringComparer.Ordinal)
                    .ThenBy(row => row.CharacterId).First();
                return new OfflineBotMechanicSeenRecord
                {
                    PersonKey = person.ToString(),
                    Mechanic = group.Key,
                    CharacterId = main.CharacterId,
                    Seen = (int)Math.Min(int.MaxValue, group.Sum(row => (long)Math.Max(0, row.Seen))),
                    LastSeenUtc = main.LastSeenUtc ?? string.Empty,
                };
            }).ToArray();

    /// <summary>Re-keyed to the person, one row per character and arc. Two rows for the same character and arc (never
    /// written by design) keep the furthest: reached, then active, then dropped, then the latest start.</summary>
    public static OfflineBotMilestoneRecord[] Milestones(BotPersonKey person, IEnumerable<OfflineBotMilestoneRecord> rows) =>
        (rows ?? []).Where(row => row != null).GroupBy(row => (row.CharacterId, Arc: row.Arc ?? string.Empty))
            .OrderBy(group => group.Key.CharacterId).ThenBy(group => group.Key.Arc, StringComparer.Ordinal)
            .Select(group =>
            {
                OfflineBotMilestoneRecord keep = group.OrderBy(row => OfflineBotMilestoneRecord.StateRank(row.State))
                    .ThenByDescending(row => row.StartedUtc, StringComparer.Ordinal).First();
                return keep.CopyAs(person);
            }).ToArray();
}

/// <summary>
/// The bit sets the place tables save ("Known", "HalfKnown"): lower-case hex, two digits a byte, bit i in byte i / 8 at
/// value 1 &lt;&lt; (i % 8), trailing zero bytes left out (so "" is the empty set). Pure; text in, text out, so a row
/// stays a plain string column. WP3 maps place indexes (data/chat/places.json) to bits.
/// </summary>
public static class BotPeopleBits
{
    /// <summary>Highest bit index accepted (the places list is far shorter; this bounds a row at 128 hex digits).</summary>
    public const int MaxBit = 511;

    public static bool Has(string bits, int index)
    {
        if (index < 0 || index > MaxBit || string.IsNullOrEmpty(bits))
            return false;
        int at = index / 8 * 2;
        return at + 1 < bits.Length && (Byte(bits, at) & (1 << (index % 8))) != 0;
    }

    public static string With(string bits, int index)
    {
        if (index < 0 || index > MaxBit)
            throw new ArgumentOutOfRangeException(nameof(index), index, $"A place bit is 0 to {MaxBit}.");
        byte[] bytes = Read(bits, index / 8 + 1);
        bytes[index / 8] |= (byte)(1 << (index % 8));
        return Write(bytes);
    }

    public static string Without(string bits, int index)
    {
        if (index < 0 || index > MaxBit)
            return Normalize(bits);
        byte[] bytes = Read(bits, index / 8 + 1);
        bytes[index / 8] &= (byte)~(1 << (index % 8));
        return Write(bytes);
    }

    public static string Union(string a, string b) => Combine(a, b, (x, y) => (byte)(x | y));

    public static string Except(string a, string b) => Combine(a, b, (x, y) => (byte)(x & ~y));

    public static int Count(string bits) => Read(bits, 0).Sum(b => System.Numerics.BitOperations.PopCount(b));

    public static IEnumerable<int> Indexes(string bits)
    {
        byte[] bytes = Read(bits, 0);
        for (int i = 0; i < bytes.Length * 8; i++)
            if ((bytes[i / 8] & (1 << (i % 8))) != 0)
                yield return i;
    }

    /// <summary>The canonical form: lower case, no trailing zero bytes; unreadable text is the empty set.</summary>
    public static string Normalize(string bits) => Write(Read(bits, 0));

    private static string Combine(string a, string b, Func<byte, byte, byte> op)
    {
        byte[] x = Read(a, 0), y = Read(b, 0);
        var bytes = new byte[Math.Max(x.Length, y.Length)];
        for (int i = 0; i < bytes.Length; i++)
            bytes[i] = op(i < x.Length ? x[i] : (byte)0, i < y.Length ? y[i] : (byte)0);
        return Write(bytes);
    }

    private static byte[] Read(string bits, int minimumLength)
    {
        int length = string.IsNullOrEmpty(bits) || bits.Length % 2 != 0 || !bits.All(Uri.IsHexDigit) ? 0 : Math.Min(bits.Length / 2, MaxBit / 8 + 1);
        var bytes = new byte[Math.Max(length, minimumLength)];
        for (int i = 0; i < length; i++)
            bytes[i] = Byte(bits, i * 2);
        return bytes;
    }

    private static byte Byte(string bits, int at) =>
        Uri.IsHexDigit(bits[at]) && Uri.IsHexDigit(bits[at + 1]) ? (byte)(Uri.FromHex(bits[at]) * 16 + Uri.FromHex(bits[at + 1])) : (byte)0;

    private static string Write(byte[] bytes)
    {
        int length = bytes.Length;
        while (length > 0 && bytes[length - 1] == 0)
            length--;
        var text = new StringBuilder(length * 2);
        for (int i = 0; i < length; i++)
            text.Append(bytes[i].ToString("x2", CultureInfo.InvariantCulture));
        return text.ToString();
    }
}
