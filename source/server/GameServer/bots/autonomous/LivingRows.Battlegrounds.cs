using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DAoCLivingServer.Configuration;

namespace DOL.GS;

/// <summary>
/// BG_POPULATION (#625 row 1; checks #610's fuller battlegrounds): every five minutes, one row per battleground and realm.
/// <c>BG_POPULATION t= bg= bracket= realm= bots= bgGoal= passing= travelling= assigned= eligible= expected= companions= player=</c>
/// <list type="bullet">
/// <item><c>bots</c>: the realm's autonomous gamebots alive in the battleground; <c>bgGoal</c> of them have the battleground
/// goal, <c>passing</c> are there on another goal.</item>
/// <item><c>travelling</c>: gamebots of the bracket's levels with the battleground goal, not there yet. <c>assigned</c> =
/// bgGoal + travelling.</item>
/// <item><c>eligible</c>: the realm's online gamebots of the bracket's levels; <c>expected</c>: the headcount the goal shares
/// (BotGoalSettings, the battleground share at each bot's level) give them, rounded.</item>
/// <item><c>companions</c>: companion bots of the realm in the battleground; <c>player</c>: real players of the realm in
/// it (a count, no name).</item>
/// </list>
/// 4 battlegrounds x 3 realms = 12 rows every five minutes (144 an hour), whatever the bot count.
/// </summary>
public static class BgPopulationRows
{
    public const string Marker = "BG_POPULATION";
    public const long IntervalMilliseconds = 5 * 60_000;

    /// <summary>One gamebot as the row counts it.</summary>
    public readonly record struct Bot(ushort Region, eRealm Realm, int Level, bool BattlegroundGoal, bool Alive);

    /// <summary>One companion bot or real player in the world (region and realm only).</summary>
    public readonly record struct Other(ushort Region, eRealm Realm);

    private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

    /// <summary>The rows for these bots, companions and players. Pure, given the share function.</summary>
    public static List<string> Lines(long now, IReadOnlyList<BattlegroundBrackets.Bracket> brackets, IEnumerable<Bot> bots,
        IEnumerable<Other> companions, IEnumerable<Other> players, Func<int, double> battlegroundShare)
    {
        Bot[] all = bots?.ToArray() ?? [];
        Other[] helpers = companions?.ToArray() ?? [];
        Other[] people = players?.ToArray() ?? [];
        var lines = new List<string>(brackets.Count * Realms.Length);
        foreach (BattlegroundBrackets.Bracket bracket in brackets)
            foreach (eRealm realm in Realms)
            {
                int inside = 0, goal = 0, travelling = 0, eligible = 0;
                double expected = 0;
                foreach (Bot bot in all)
                {
                    if (bot.Realm != realm)
                        continue;
                    bool here = bot.Region == bracket.RegionId;
                    if (here && bot.Alive)
                    {
                        inside++;
                        if (bot.BattlegroundGoal)
                            goal++;
                    }
                    if (!BattlegroundBrackets.Allows(bracket, bot.Level))
                        continue;
                    eligible++;
                    expected += Math.Clamp(battlegroundShare?.Invoke(bot.Level) ?? 0, 0, 1);
                    if (!here && bot.BattlegroundGoal)
                        travelling++;
                }
                int companionsHere = helpers.Count(other => other.Region == bracket.RegionId && other.Realm == realm);
                int playersHere = people.Count(other => other.Region == bracket.RegionId && other.Realm == realm);
                lines.Add(Format(now, bracket, realm, inside, goal, travelling, eligible, (int)Math.Round(expected),
                    companionsHere, playersHere));
            }
        return lines;
    }

    public static string Format(long now, BattlegroundBrackets.Bracket bracket, eRealm realm, int bots, int battlegroundGoal,
        int travelling, int eligible, int expected, int companions, int players)
    {
        var row = new StringBuilder(Marker, 200);
        row.Append(" t=").Append(now).Append(" bg=").Append(bracket.Name)
            .Append(" bracket=").Append(bracket.MinLevel).Append('-').Append(bracket.MaxLevel)
            .Append(" realm=").Append(RvrRecordText.Realm(realm))
            .Append(" bots=").Append(bots).Append(" bgGoal=").Append(battlegroundGoal).Append(" passing=").Append(bots - battlegroundGoal)
            .Append(" travelling=").Append(travelling).Append(" assigned=").Append(battlegroundGoal + travelling)
            .Append(" eligible=").Append(eligible).Append(" expected=").Append(expected)
            .Append(" companions=").Append(companions).Append(" player=").Append(players);
        return row.ToString();
    }

    /// <summary>The battleground share of the goal settings at a level (0-1).</summary>
    public static double Share(BotGoalSettings settings, int level)
    {
        BotGoalWeights weights = settings?.ForLevel(level);
        if (weights == null)
            return 0;
        int total = weights.SoloPve + weights.GroupPve + weights.RvR + weights.Battlegrounds;
        return total <= 0 ? 0 : (double)weights.Battlegrounds / total;
    }

    /// <summary>Reads the world and writes the rows (the pulse, every five minutes).</summary>
    public static void Write(long now)
    {
        GameBot[] registry = AutonomousBotRegistry.Snapshot();
        var bots = new List<Bot>(registry.Length);
        foreach (GameBot bot in registry)
            if (bot != null && LivingRows.Kind(bot) == "gamebot")
                bots.Add(new Bot(bot.CurrentRegionID, bot.Realm, bot.Level,
                    AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.Battleground), bot.IsAlive));
        var brackets = new List<BattlegroundBrackets.Bracket>(BattlegroundBrackets.Count);
        var companions = new List<Other>();
        for (int index = 0; index < BattlegroundBrackets.Count; index++)
        {
            BattlegroundBrackets.Bracket bracket = BattlegroundBrackets.Get(index);
            brackets.Add(bracket);
            Region region = WorldMgr.GetRegion(bracket.RegionId);
            if (region?.Objects == null)
                continue;
            foreach (GameBot bot in region.Objects.OfType<GameBot>())
                if (bot.IsAlive && LivingRows.Kind(bot) == "companion")
                    companions.Add(new Other(bracket.RegionId, bot.Realm));
        }
        var players = new List<Other>();
        foreach (GamePlayer player in ClientService.Instance.GetPlayers())
            if (player != null && BattlegroundBrackets.IsBattlegroundRegion(player.CurrentRegionID))
                players.Add(new Other(player.CurrentRegionID, player.Realm));
        BotGoalSettings settings = AutonomousBotGoalPolicy.Settings;
        foreach (string line in Lines(now, brackets, bots, companions, players, level => Share(settings, level)))
            LivingRows.Emit(line);
    }
}

/// <summary>
/// BG_TARGET_PICK (#625 row 4; checks #621 and FarmVibes' review points): a sampled battleground pick of an autonomous
/// gamebot. <c>BG_TARGET_PICK seq= every= t= bg= realm= src= pick= target= skippedControlled= skippedEngines= rally= list=
/// candidates=</c>. <c>src</c>: scan (the frontier scan) or keep (the keep assault); <c>pick</c>: person, pet, engine, door,
/// guard or npc; <c>target</c>: the four kinds; <c>rally</c>: 1 when the pick came from the rally board.
/// Every <see cref="LivingRowProperties.BgTargetPickSampleEvery"/>th pick, at most <see cref="PerMinute"/> a minute.
/// </summary>
public static class BgTargetPickRows
{
    public const string Marker = "BG_TARGET_PICK";
    public const int PerMinute = 10;

    public static readonly LivingRowSampler Sampler = new();

    /// <summary>Whether this pick is sampled (a counter, nothing else, when it is not).</summary>
    public static bool Sample(out long sequence, out int every)
    {
        every = LivingRowProperties.BgTargetPickSampleEvery;
        return Sampler.Take(every, out sequence) && LivingRows.Enabled && LivingRows.Take(Marker, PerMinute);
    }

    public static string Format(long seq, int every, long now, string battleground, eRealm realm, string source, string pick,
        string target, int skippedControlled, int skippedEngines, bool rally, string list, int candidates) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Marker} seq={seq} every={every} t={now} bg={battleground} realm={RvrRecordText.Realm(realm)} src={source} pick={pick} " +
            $"target={target} skippedControlled={skippedControlled} skippedEngines={skippedEngines} rally={(rally ? 1 : 0)} " +
            $"list={list ?? "-"} candidates={candidates}");

    /// <summary>A pick of the frontier scan in a battleground. <paramref name="beforeArrange"/> is the candidate list before
    /// #617's people-first and crowd-control arrangement, <paramref name="arranged"/> after it. Never throws.</summary>
    public static void Scan(GameBot bot, GameLiving enemy, GameLiving[] beforeArrange, GameLiving[] arranged, bool rally, string list)
    {
        try
        {
            if (bot == null || enemy == null || !BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID) ||
                !Sample(out long seq, out int every))
                return;
            int before = beforeArrange?.Length ?? 0;
            int people = AutonomousBattlegroundTargets.PeopleFirst(beforeArrange ?? [], target => target is GameSiegeWeapon)?.Length ?? 0;
            int after = arranged?.Length ?? 0;
            LivingRows.Emit(Format(seq, every, LivingRows.Now(), LivingRows.Battleground(bot.CurrentRegionID), bot.Realm, "scan",
                LivingRows.PickKind(enemy), LivingRows.Kind(enemy), Math.Max(0, people - after), Math.Max(0, before - people),
                rally, list, after));
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>A new pick of the keep assault (a guard, the lord or a door). Never throws.</summary>
    public static void Keep(GameBot bot, GameLiving target, bool skippedControlled)
    {
        try
        {
            if (bot == null || target == null || !Sample(out long seq, out int every))
                return;
            LivingRows.Emit(Format(seq, every, LivingRows.Now(), LivingRows.Battleground(bot.CurrentRegionID), bot.Realm, "keep",
                LivingRows.PickKind(target), LivingRows.Kind(target), skippedControlled ? 1 : 0, 0, false, "keep", 1));
        }
        catch
        {
            // Logging only.
        }
    }
}

/// <summary>
/// BG_RALLY_RESPONSE (#625 row 4; checks #621's rally): the time from a contact posted on a battleground rally board
/// (AutonomousBattlegroundRallyContact) to each rallied gamebot's first engage of it (its target set and aggro taken).
/// <c>BG_RALLY_RESPONSE t= bg= realm= contact= ms= nth=</c>: <c>contact</c> is the contact's kind (the four kinds, pet=1 for
/// a pet), <c>nth</c> the bot's place among the responders. One row per bot and contact, at most <see cref="PerMinute"/> a
/// minute. The board's own post times are kept here (at most <see cref="Capacity"/> contacts, each forgotten after
/// <see cref="ForgetMilliseconds"/>); no timer.
/// </summary>
public static class BgRallyResponseRows
{
    public const string Marker = "BG_RALLY_RESPONSE";
    public const int PerMinute = 10;
    public const int Capacity = 256;
    public const long ForgetMilliseconds = 30_000;

    private sealed class Posted
    {
        public long At;
        public readonly HashSet<long> Responders = new();
    }

    private static readonly Dictionary<(ushort Region, eRealm Realm, GameLiving Target), Posted> Contacts = new();

    /// <summary>A contact newly posted on the board (AutonomousBattlegroundRallyContact.Post returned true).</summary>
    public static void Post(ushort region, eRealm realm, GameLiving target, long now)
    {
        if (target == null)
            return;
        lock (Contacts)
        {
            Prune(now);
            if (Contacts.Count >= Capacity)
                return;
            Contacts[(region, realm, target)] = new Posted { At = now };
        }
    }

    /// <summary>A rallied bot engaged a posted contact. The row is written for its first engage of that contact only.
    /// Returns the row, or null. Never throws.</summary>
    public static string Engaged(ushort region, eRealm realm, long botId, GameLiving target, long now)
    {
        try
        {
            if (target == null)
                return null;
            long ms;
            int nth;
            lock (Contacts)
            {
                if (!Contacts.TryGetValue((region, realm, target), out Posted posted) || now - posted.At > ForgetMilliseconds ||
                    !posted.Responders.Add(botId))
                    return null;
                ms = Math.Max(0, now - posted.At);
                nth = posted.Responders.Count;
            }
            if (!LivingRows.Enabled || !LivingRows.Take(Marker, PerMinute))
                return null;
            string kind = LivingRows.ActorKind(target, out bool pet);
            string row = Format(now, LivingRows.Battleground(region), realm, kind, pet, ms, nth);
            LivingRows.Emit(row);
            return row;
        }
        catch
        {
            return null;
        }
    }

    public static string Format(long now, string battleground, eRealm realm, string contact, bool pet, long ms, int nth) =>
        $"{Marker} t={now} bg={battleground} realm={RvrRecordText.Realm(realm)} contact={contact} pet={(pet ? 1 : 0)} ms={ms} nth={nth}";

    private static void Prune(long now)
    {
        List<(ushort, eRealm, GameLiving)> old = null;
        foreach (var pair in Contacts)
            if (now - pair.Value.At > ForgetMilliseconds)
                (old ??= new()).Add(pair.Key);
        if (old != null)
            foreach (var key in old)
                Contacts.Remove(key);
    }

    /// <summary>Tests.</summary>
    public static void Reset()
    {
        lock (Contacts)
            Contacts.Clear();
    }
}
