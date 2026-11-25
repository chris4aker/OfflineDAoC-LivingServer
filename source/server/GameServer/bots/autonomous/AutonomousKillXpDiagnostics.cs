using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DOL.Logging;
using static DOL.GS.ServerRules.IServerRules;

namespace DOL.GS
{
    /// <summary>
    /// One sampled row per autonomous-gamebot PvE kill (GitHub #544): who killed it, against what, what each member was
    /// paid, and how the damage (and so the XP) was split with other parties. Diagnostic only: it reads the kill's damage
    /// lists and the bots' experience, never the game's random numbers, and writes nothing but the log line.
    /// Sampling is a plain counter (every Nth eligible kill, server-wide) with a per-minute ceiling, so 3,000 bots
    /// cannot flood server.log.
    /// </summary>
    public static class AutonomousKillXpDiagnostics
    {
        public const string Marker = "AUTONOMOUS_KILL_XP";
        public const int MaxRowsPerMinute = 120;
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousKillXpDiagnostics));
        internal static readonly KillXpSampler Sampler = new(MaxRowsPerMinute);

        /// <summary>The XP each bot had before the kill paid out; null when this kill is not sampled.</summary>
        public sealed class Snapshot
        {
            public long Sequence { get; init; }
            public int Every { get; init; }
            public long Dropped { get; init; }
            public Dictionary<GameBot, long> ExperienceBefore { get; } = new();
        }

        /// <summary>Called before the kill's XP is paid. Allocates nothing unless the kill is sampled.</summary>
        public static Snapshot Begin(Dictionary<GameBot, EntityCountTotalDamagePair> botCountAndDamage)
        {
            try
            {
                int every = AutonomousDiagnosticsProperties.KillXpSampleEvery;
                if (every <= 0 || botCountAndDamage == null || botCountAndDamage.Count == 0)
                    return null;
                bool eligible = false;
                foreach (GameBot bot in botCountAndDamage.Keys)
                {
                    if (bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false })
                    {
                        eligible = true;
                        break;
                    }
                }
                if (!eligible || !Sampler.Take(every, Environment.TickCount64, out long sequence, out long dropped))
                    return null;
                var snapshot = new Snapshot { Sequence = sequence, Every = every, Dropped = dropped };
                foreach (GameBot bot in botCountAndDamage.Keys)
                    snapshot.ExperienceBefore[bot] = bot.Experience;
                return snapshot;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Called after the kill's XP is paid: writes the row. Never throws.</summary>
        public static void Finish(Snapshot snapshot, GameNPC killedNpc, double totalDamage,
            Dictionary<GamePlayer, EntityCountTotalDamagePair> playerCountAndDamage,
            Dictionary<GameBot, EntityCountTotalDamagePair> botCountAndDamage,
            Dictionary<Group, EntityCountTotalDamagePair> groupCountAndDamage)
        {
            if (snapshot == null || killedNpc == null)
                return;
            try
            {
                KillXpRow row = Build(snapshot, killedNpc, totalDamage, playerCountAndDamage, botCountAndDamage, groupCountAndDamage);
                if (row != null)
                    Log.Info(Format(row));
            }
            catch { } // A logging problem must never touch the kill's reward.
        }

        private static KillXpRow Build(Snapshot snapshot, GameNPC npc, double totalDamage,
            Dictionary<GamePlayer, EntityCountTotalDamagePair> players,
            Dictionary<GameBot, EntityCountTotalDamagePair> bots,
            Dictionary<Group, EntityCountTotalDamagePair> groups)
        {
            var parties = new List<KillXpParty>();
            if (groups != null)
                foreach (var pair in groups)
                {
                    bool hasPlayer = pair.Key.GetMembersInTheGroup().Any(member => member is GamePlayer);
                    parties.Add(new KillXpParty(hasPlayer ? "pgroup" : "group", RealmOf(pair.Key.LivingLeader),
                        pair.Value.Count, pair.Value.Damage) { Key = pair.Key });
                }
            foreach (var pair in bots)
                if (pair.Key.Group == null)
                    parties.Add(new KillXpParty("solo", RealmOf(pair.Key), 1, pair.Value.Damage) { Key = pair.Key });
            if (players != null)
                foreach (var pair in players)
                    if (pair.Key.Group == null)
                        parties.Add(new KillXpParty("player", RealmOf(pair.Key), 1, pair.Value.Damage) { Key = pair.Key });

            // The killer: the autonomous-bot party that did the most damage.
            KillXpParty killer = parties
                .Where(party => party.Kind is "group" or "solo" || party.Kind == "pgroup" &&
                    bots.Keys.Any(bot => ReferenceEquals(bot.Group, party.Key)))
                .OrderByDescending(party => party.Damage).FirstOrDefault();
            if (killer == null)
                return null;

            GameBot[] members = bots.Keys
                .Where(bot => bot.IsAutonomousWorldBot && !bot.IsTemporaryGroupHelper &&
                              (ReferenceEquals(bot, killer.Key) || bot.Group != null && ReferenceEquals(bot.Group, killer.Key)))
                .OrderBy(bot => bot.Name, StringComparer.Ordinal).ToArray();
            if (members.Length == 0)
                return null;

            GameBot highest = members.OrderByDescending(bot => bot.Level).First();
            AutonomousGoalAttempt attempt = members.Select(bot => bot.GoalDiagnosticAttempt).FirstOrDefault(a => a != null);
            string campId = attempt?.CampId ?? members.Select(bot => bot.PersistentRecord?.CurrentCampId)
                .FirstOrDefault(id => !string.IsNullOrEmpty(id)) ?? string.Empty;
            int? onTarget = null;
            int? sameName = null;
            if (attempt != null)
            {
                onTarget = attempt.MatchesTarget(npc.Name, npc.CurrentRegionID, npc.X, npc.Y, npc.Z) ||
                           attempt.MatchesTarget(npc.Name, npc.CurrentRegionID, npc.SpawnPoint.X, npc.SpawnPoint.Y, npc.SpawnPoint.Z)
                    ? 1 : 0;
                sameName = string.Equals(npc.Name, attempt.Target, StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }

            return new KillXpRow
            {
                Sequence = snapshot.Sequence, Every = snapshot.Every, Dropped = snapshot.Dropped,
                Realm = RealmOf(highest), Zone = npc.CurrentZone?.Description ?? string.Empty, Region = npc.CurrentRegionID,
                Npc = npc.Name, NpcLevel = npc.Level, NpcXp = npc.ExperienceValue,
                Con = ConLevels.GetConColor(highest.GetConLevel(npc)).ToString(),
                PartyKind = killer.Kind,
                GroupSize = highest.Group?.MemberCount ?? 1,
                Contributors = killer.Contributors,
                MinLevel = members.Min(bot => bot.Level), MaxLevel = highest.Level,
                AverageLevel = members.Average(bot => (double)bot.Level),
                CampId = campId, CampTarget = attempt?.Target ?? string.Empty,
                CampLevel = attempt is { TargetLevel: > 0 } ? attempt.TargetLevel : null,
                OnTarget = onTarget, SameName = sameName,
                XpPerMember = members.Select(bot => snapshot.ExperienceBefore.TryGetValue(bot, out long before)
                    ? Math.Max(0, bot.Experience - before) : 0).ToArray(),
                TotalDamage = totalDamage,
                Killer = killer,
                Parties = parties
            };
        }

        private static string RealmOf(GameLiving living) => living?.Realm switch
        {
            eRealm.Albion => "Albion",
            eRealm.Midgard => "Midgard",
            eRealm.Hibernia => "Hibernia",
            null => "None",
            var other => other.ToString()
        };

        /// <summary>The row's text. Shares are of the kill's total credited damage; "lost" is damage that paid nobody
        /// (out of range, dead, companions, other non-reward sources).</summary>
        public static string Format(KillXpRow row)
        {
            CultureInfo c = CultureInfo.InvariantCulture;
            double total = row.TotalDamage > 0 ? row.TotalDamage : 0;
            double Share(double damage) => total > 0 ? Math.Clamp(damage / total, 0, 1) : 0;
            double counted = row.Parties.Sum(party => party.Damage);
            double lost = total > 0 ? Math.Max(0, total - counted) : 0;
            var shares = new StringBuilder();
            foreach (KillXpParty party in row.Parties.OrderByDescending(party => party.Damage))
            {
                if (shares.Length > 0) shares.Append(';');
                shares.Append(party.Kind).Append('/').Append(party.Realm).Append('/')
                    .Append(party.Contributors.ToString(c)).Append('/').Append(Share(party.Damage).ToString("0.000", c));
                if (ReferenceEquals(party, row.Killer)) shares.Append("/killer");
            }
            if (lost > 0.5)
            {
                if (shares.Length > 0) shares.Append(';');
                shares.Append("lost/").Append(Share(lost).ToString("0.000", c));
            }
            long[] xp = row.XpPerMember ?? [];
            return string.Create(c,
                $"{Marker} seq={row.Sequence} every={row.Every} dropped={row.Dropped} realm={row.Realm} zone=\"{Clean(row.Zone)}\" " +
                $"region={row.Region} npc=\"{Clean(row.Npc)}\" npcLevel={row.NpcLevel} npcXp={row.NpcXp} con={row.Con} " +
                $"party={row.PartyKind} size={row.GroupSize} contributors={row.Contributors} minLevel={row.MinLevel} " +
                $"maxLevel={row.MaxLevel} avgLevel={row.AverageLevel:0.0} levelDiff={row.NpcLevel - row.MaxLevel} " +
                $"camp=\"{Clean(row.CampId)}\" campTarget=\"{Clean(row.CampTarget)}\" " +
                $"campLevel={(row.CampLevel?.ToString(c) ?? "?")} " +
                $"onTarget={(row.OnTarget?.ToString(c) ?? "?")} sameName={(row.SameName?.ToString(c) ?? "?")} " +
                $"xpPerMember={(xp.Length == 0 ? "0" : string.Join("/", xp))} xpSum={xp.Sum()} " +
                $"share={Share(row.Killer?.Damage ?? 0):0.000} otherParties={Math.Max(0, row.Parties.Count - 1)} " +
                $"lost={Share(lost):0.000} shares=\"{shares}\"");
        }

        private static string Clean(string text) =>
            string.IsNullOrEmpty(text) ? string.Empty : text.Replace('"', '\'').Replace('\n', ' ').Replace('\r', ' ');
    }

    public sealed record KillXpParty(string Kind, string Realm, int Contributors, double Damage)
    {
        /// <summary>The group, bot or player this party stands for (not printed).</summary>
        public object Key { get; init; }
    }

    public sealed class KillXpRow
    {
        public long Sequence { get; init; }
        public int Every { get; init; }
        public long Dropped { get; init; }
        public string Realm { get; init; } = "None";
        public string Zone { get; init; } = string.Empty;
        public int Region { get; init; }
        public string Npc { get; init; } = string.Empty;
        public int NpcLevel { get; init; }
        public long NpcXp { get; init; }
        public string Con { get; init; } = "UNKNOWN";
        public string PartyKind { get; init; } = "solo";
        public int GroupSize { get; init; } = 1;
        public int Contributors { get; init; } = 1;
        public int MinLevel { get; init; }
        public int MaxLevel { get; init; }
        public double AverageLevel { get; init; }
        public string CampId { get; init; } = string.Empty;
        public string CampTarget { get; init; } = string.Empty;
        /// <summary>GitHub #544: the camp's target level when chosen; null when unknown (printed "?"). The report's
        /// target con is campLevel minus maxLevel.</summary>
        public int? CampLevel { get; init; }
        public int? OnTarget { get; init; }
        public int? SameName { get; init; }
        public long[] XpPerMember { get; init; } = [];
        public double TotalDamage { get; init; }
        public KillXpParty Killer { get; init; }
        public IReadOnlyList<KillXpParty> Parties { get; init; } = [];
    }

    /// <summary>
    /// Every Nth eligible kill, server-wide, and at most a ceiling of rows per minute. A plain counter: no random numbers,
    /// so sampling cannot shift the game's random sequence. Thread-safe.
    /// </summary>
    public sealed class KillXpSampler(int maxRowsPerMinute)
    {
        private readonly object _gate = new();
        private long _seen;
        private long _windowStartMs;
        private bool _windowOpen;
        private int _windowRows;
        private long _droppedSinceLastRow;

        public long Seen => System.Threading.Interlocked.Read(ref _seen);

        /// <param name="dropped">sampled kills the ceiling skipped since the last row written</param>
        public bool Take(int every, long nowMs, out long sequence, out long dropped)
        {
            sequence = System.Threading.Interlocked.Increment(ref _seen);
            dropped = 0;
            if (every <= 0 || sequence % every != 0)
                return false;
            lock (_gate)
            {
                if (!_windowOpen || nowMs - _windowStartMs >= 60_000)
                {
                    _windowOpen = true;
                    _windowStartMs = nowMs;
                    _windowRows = 0;
                }
                if (_windowRows >= maxRowsPerMinute)
                {
                    _droppedSinceLastRow++;
                    return false;
                }
                _windowRows++;
                dropped = _droppedSinceLastRow;
                _droppedSinceLastRow = 0;
                return true;
            }
        }
    }
}
