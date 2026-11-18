using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// Owner decision 2026-11-18: the No Lifer buffbots that earlier builds saved into the roster by mistake are retired, kept and
/// never logged in, never deleted. Until #334, a buffbot that logged out with its main went through
/// <c>GameBot.SaveAutonomousState</c>, which inserted its in-memory record as a new <c>offline_world_bots</c> row; that row
/// then played as an ordinary level-50 gamebot (owner's test copy: ids 5876-5895 and 5826, "Eilriorrin" twice).
/// <para>A row is a copy only when every one of these holds, so an ordinary gamebot never matches:</para>
/// <list type="bullet">
/// <item>its <c>ClassName</c> is empty. The buffbot's record never set it, nothing writes a roster row's ClassName after it
/// is made, and every other way a row is made sets it (the launcher's batches, make_test_world.py, hired companions);</item>
/// <item>a main in the same table has a buffbot (<see cref="NoLiferBuffBots.HasBuffBot"/>: a No Lifer under the percent)
/// and has a lower id (the copy was made after it);</item>
/// <item>its realm, class, gender and race are that buffbot's (fixed from the main's id), and it is level 50;</item>
/// <item>its name is one the buffbot could have had: one of the main-style names when this main's buffbot takes one, or one
/// of the first <see cref="GeneratedNameCount"/> names the generator gives that seed as earlier ones are taken.</item>
/// </list>
/// Hired companions and rows already retired are never touched. A row with an empty ClassName and a buffbot class at level
/// 50 that matches no main is only logged (BOT_BUFFBOT_COPY_SUSPECTED). The repair runs once per world
/// (<c>living_server_world_repairs</c>), and a second run changes nothing: retired rows are skipped.
/// </summary>
public static class BuffBotCopyRetirement
{
    public const string RepairName = "buffbot-copies";
    public const string RetiredLogTag = "BOT_BUFFBOT_COPY_RETIRED";
    public const string SuspectedLogTag = "BOT_BUFFBOT_COPY_SUSPECTED";
    public const string SummaryLogTag = "BOT_BUFFBOT_COPIES";
    public const string RetiredActivity = "Not played: a copy of a buffbot an earlier build saved by mistake (kept, not deleted)";
    /// <summary>How many generated names a seed is followed through (each one taken gives the next). On the owner's logs
    /// (5378dbe and b1b6846) all 48 buffbots' names are in this list; the deepest, Okke (main 4290, a common first name), is
    /// the 40th.</summary>
    public const int GeneratedNameCount = 64;
    public const int BuffBotLevel = 50;

    public sealed record Copy(OfflineWorldBotRecord Record, OfflineWorldBotRecord Main);

    public sealed record Result(Copy[] Copies, OfflineWorldBotRecord[] Suspected, int Mains);

    /// <summary>The percent the mains are judged at: the setting, and never below its default of 30, because the copies were
    /// made under whatever the percent was then. Pure.</summary>
    public static int PercentFor(int setting) => Math.Max(Math.Clamp(setting, 0, 100), 30);

    /// <summary>
    /// The names the buffbot generator gives a main's seed, in order: the first is its name when nothing is taken, each next
    /// one its name when all the earlier ones are taken. The generator only asks whether a name is taken, so whatever was
    /// taken when the buffbot logged in, its generated name is one of these (unless more than this many were taken).
    /// </summary>
    public static IReadOnlyList<string> GeneratedNames(long mainId, eRealm realm, int count = GeneratedNameCount)
    {
        var names = new List<string>(count);
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < count; i++)
        {
            var reserved = new HashSet<string>(taken, StringComparer.OrdinalIgnoreCase);
            string name;
            try
            {
                name = NoLiferBuffBots.DrawIdentity(mainId, realm, reserved).Name;
            }
            catch (Exception)
            {
                break;
            }
            if (string.IsNullOrEmpty(name) || !taken.Add(name))
                break;
            names.Add(name);
        }
        return names;
    }

    /// <summary>Whether a row could only have been made from a buffbot's record: no ClassName, a buffbot class, level 50.</summary>
    public static bool LooksLikeBuffBotRecord(OfflineWorldBotRecord record) =>
        record != null && string.IsNullOrWhiteSpace(record.ClassName) && record.Level == BuffBotLevel &&
        NoLiferBuffBots.IsBuffBotClass((eCharacterClass)record.ClassId);

    /// <summary>The copies among <paramref name="rows"/> (the whole table, retired rows included) and the suspected rows.</summary>
    public static Result Find(IReadOnlyCollection<OfflineWorldBotRecord> rows, Func<long, bool> isHired, int percent)
    {
        rows ??= Array.Empty<OfflineWorldBotRecord>();
        isHired ??= _ => false;
        OfflineWorldBotRecord[] candidates = rows
            .Where(r => r is { IsRetired: false, BotId: > 0 } && r.BotId < NoLiferBuffBots.FirstBotId && !isHired(r.BotId) &&
                        LooksLikeBuffBotRecord(r))
            .ToArray();
        if (candidates.Length == 0)
            return new Result(Array.Empty<Copy>(), Array.Empty<OfflineWorldBotRecord>(), 0);

        var byName = candidates.GroupBy(r => r.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToArray(), StringComparer.Ordinal);
        var copies = new Dictionary<long, Copy>();
        int mains = 0;
        foreach (OfflineWorldBotRecord main in rows.Where(r => r != null && !isHired(r.BotId) && NoLiferBuffBots.HasBuffBot(r.BotId, percent))
                     .OrderBy(r => r.BotId))
        {
            var realm = (eRealm)main.Realm;
            eCharacterClass characterClass = NoLiferBuffBots.ClassFor(realm);
            if (characterClass == eCharacterClass.Unknown)
                continue;
            mains++;
            AutonomousBotIdentityGenerator.Identity identity;
            try
            {
                identity = NoLiferBuffBots.DrawIdentity(main.BotId, realm, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            }
            catch (Exception)
            {
                continue;
            }
            IEnumerable<string> names = NoLiferBuffBots.MainStyleNames(main.Name, main.BotId).Concat(GeneratedNames(main.BotId, realm));
            foreach (string name in names.Distinct(StringComparer.Ordinal))
            {
                if (!byName.TryGetValue(name, out OfflineWorldBotRecord[] named))
                    continue;
                foreach (OfflineWorldBotRecord row in named)
                {
                    if (row.BotId > main.BotId && row.Realm == main.Realm && row.ClassId == (int)characterClass &&
                        row.Gender == (int)identity.Gender && row.RaceId == (int)identity.Race && !copies.ContainsKey(row.BotId))
                        copies[row.BotId] = new Copy(row, main);
                }
            }
        }
        OfflineWorldBotRecord[] suspected = candidates.Where(r => !copies.ContainsKey(r.BotId)).OrderBy(r => r.BotId).ToArray();
        return new Result(copies.Values.OrderBy(c => c.Record.BotId).ToArray(), suspected, mains);
    }

    /// <summary>Retires one copy: IsRetired (never logged in again, out of the roster, guilds and friends), offline, and the
    /// launcher's Activity says why. Nothing is deleted. True when the record changed (and needs saving).</summary>
    public static bool Retire(OfflineWorldBotRecord record)
    {
        if (record == null || record.IsRetired)
            return false;
        record.IsRetired = true;
        record.IsOnline = false;
        record.Activity = RetiredActivity;
        record.Dirty = true;
        return true;
    }

    public static string RetiredLine(Copy copy) =>
        $"{RetiredLogTag} bot=\"{copy.Record.Name}\" id={copy.Record.BotId} class={(eCharacterClass)copy.Record.ClassId} " +
        $"level={copy.Record.Level} realm={GlobalConstants.RealmToName((eRealm)copy.Record.Realm)} main=\"{copy.Main.Name}\" " +
        $"mainId={copy.Main.BotId}: a buffbot an earlier build saved as a gamebot; kept with its items, never logged in";

    public static string SuspectedLine(OfflineWorldBotRecord record) =>
        $"{SuspectedLogTag} bot=\"{record.Name}\" id={record.BotId} class={(eCharacterClass)record.ClassId} level={record.Level} " +
        $"realm={GlobalConstants.RealmToName((eRealm)record.Realm)}: no class name, like a saved buffbot, but no main's buffbot " +
        "matches it; left as it is";

    public static string SummaryLine(Result result, int retired) =>
        $"{SummaryLogTag} retired={retired} found={result.Copies.Length} suspected={result.Suspected.Length} mains={result.Mains}";

    /// <summary>Whether this world's repair list already has the repair. Pure.</summary>
    public static bool AlreadyRan(string repairs) =>
        (repairs ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Contains(RepairName, StringComparer.OrdinalIgnoreCase);

    /// <summary>The repair list with this repair added. Pure.</summary>
    public static string WithRepair(string repairs) =>
        AlreadyRan(repairs) ? repairs : string.IsNullOrWhiteSpace(repairs) ? RepairName : repairs.Trim() + "," + RepairName;
}
