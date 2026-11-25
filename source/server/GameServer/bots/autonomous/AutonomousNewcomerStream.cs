using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.GS.CraftingEconomy;
using DOL.GS.ServerProperties;
using DOL.Logging;
using OfflineDaoc.Shared;

namespace DOL.GS
{
    /// <summary>
    /// The newcomer stream (issue #630, owner decision 2026-11-25, option 3; the interim fix before the alt roster, #631).
    /// Autonomous gamebots only: companions, hired companions, No Lifer buffbots, Epic Special Forces and the player are
    /// never counted, made or retired here.
    /// <para>Why: every gamebot is its own character, nothing refilled levels 1-19 but the launcher's "+ Lv.1" batches, and
    /// the world piled up at 50. The 2026-10-12 roster design says "occasional retirement and new players".</para>
    /// <list type="bullet">
    /// <item>Per realm, when the gamebots in levels 1-19 fall below a floor (<c>population_newcomer_floor</c> at a roster of
    /// <see cref="RosterBasis"/> a realm, scaled to the realm's roster), a new level-1 gamebot joins, at most
    /// <c>population_newcomer_per_hour</c> an hour (scaled the same way), spaced evenly. Not during the startup ramp.</item>
    /// <item>A newcomer is made as the launcher's "+ Lv.1" batch makes one: the server's twin of its generator
    /// (<see cref="AutonomousBotIdentityGenerator"/>: the era class shares, never class 63, the same name mix), the row
    /// fields of <see cref="NewBotRow"/> (shared with the launcher), level 1, no coin, no items (gamebots start without
    /// armour and earn it), placed at its first login by the server's own Classic/SI start placement.</item>
    /// <item>With <c>population_retire</c> on, each newcomer is paired with one gamebot of its realm at level 20 or above
    /// that retires (owner decision 2026-11-25: the least-played and least-connected first, weighted toward the longest
    /// idle; <see cref="RetireScore"/>), so the realm's roster stays the same size. Never below 20, the band the stream
    /// refills. A retiree keeps its
    /// record, items and coin; it is marked IsRetired with the <see cref="RetiredFromPlayMarker"/> Activity and never logs
    /// in again. Nothing is ever deleted.</item>
    /// </list>
    /// Rows: POPULATION_NEWCOMER and POPULATION_RETIRED (no names; budgeted). Tests only until seen in a PC log.
    /// </summary>
    public static class AutonomousNewcomerStream
    {
        private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const string NewcomerTag = "POPULATION_NEWCOMER";
        public const string RetiredTag = "POPULATION_RETIRED";
        /// <summary>The realm roster the floor and the rate are given at (owner decision 2026-11-25: a floor of 75 a realm in
        /// levels 1-19 and up to 10 newcomers an hour, both at 1,000 a realm and scaled).</summary>
        public const int RosterBasis = 1000;
        /// <summary>The highest level counted as low (levels 1-19).</summary>
        public const int LowTop = 19;
        /// <summary>The lowest level that may retire (owner decision 2026-11-25: "gamebots at any level 20 or above, and never
        /// below 20").</summary>
        public const int RetireMinLevel = 20;
        public const int RowsPerMinute = 10;
        public static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan HeldRowInterval = TimeSpan.FromHours(1);
        public static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

        /// <summary>Tests read the rows here (the server's logger is off in tests).</summary>
        public static Action<string> TestSink;

        // ---- Settings ------------------------------------------------------------------------------------------------

        public readonly record struct Settings(bool Newcomers, int FloorAtBasis, int PerHourAtBasis, bool Retire)
        {
            public static Settings Current => new(Properties.POPULATION_NEWCOMERS, Properties.POPULATION_NEWCOMER_FLOOR,
                Properties.POPULATION_NEWCOMER_PER_HOUR, Properties.POPULATION_RETIRE);

            public bool On => Newcomers && FloorAtBasis > 0 && PerHourAtBasis > 0;
        }

        // ---- The floor and the rate (pure) ---------------------------------------------------------------------------

        /// <summary>The floor for a realm with this many gamebots: <paramref name="atBasis"/> at <see cref="RosterBasis"/>,
        /// scaled, at least 1 (0 when either is 0). Pure.</summary>
        public static int Scaled(int atBasis, int realmRoster) =>
            atBasis <= 0 || realmRoster <= 0 ? 0
                : Math.Max(1, (int)Math.Round(atBasis * (double)realmRoster / RosterBasis, MidpointRounding.AwayFromZero));

        public static int Floor(int floorAtBasis, int realmRoster) => Scaled(floorAtBasis, realmRoster);

        public static int PerHour(int perHourAtBasis, int realmRoster) => Scaled(perHourAtBasis, realmRoster);

        /// <summary>The time between two newcomers of a realm at this hourly rate. Pure.</summary>
        public static TimeSpan Spacing(int perHour) => perHour <= 0 ? TimeSpan.MaxValue : TimeSpan.FromMinutes(60.0 / perHour);

        /// <summary>One realm's numbers at a check.</summary>
        public readonly record struct RealmCount(eRealm Realm, int Roster, int Low);

        /// <summary>Whether a realm takes a newcomer now: below its floor and the spacing since its last newcomer is up.
        /// Pure.</summary>
        public static bool Due(RealmCount count, Settings settings, DateTime? lastAddedUtc, DateTime nowUtc)
        {
            if (!settings.On || count.Roster <= 0)
                return false;
            if (count.Low >= Floor(settings.FloorAtBasis, count.Roster))
                return false;
            return lastAddedUtc is not { } last || nowUtc - last >= Spacing(PerHour(settings.PerHourAtBasis, count.Roster));
        }

        /// <summary>Counts a realm's gamebots and those in levels 1-19 (the live level for one online). Pure.</summary>
        public static RealmCount Count(eRealm realm, IEnumerable<(int Realm, int Level)> roster)
        {
            int total = 0, low = 0;
            foreach ((int botRealm, int level) in roster ?? [])
            {
                if (botRealm != (int)realm)
                    continue;
                total++;
                if (level <= LowTop)
                    low++;
            }
            return new RealmCount(realm, total, low);
        }

        // ---- Who may retire (pure) -----------------------------------------------------------------------------------

        /// <summary>Why a gamebot at level 20 or above may not retire now (issue #630's "never retire" list, plus the bots that are not
        /// ordinary gamebots).</summary>
        [Flags]
        public enum Exclusion
        {
            None = 0,
            GuildLeader = 1 << 0,
            PlayerGuild = 1 << 1,
            PlayerFriend = 1 << 2,
            Grouped = 1 << 3,
            Raid = 1 << 4,
            Trade = 1 << 5,
            CraftOrder = 1 << 6,
            Busy = 1 << 7,
            NearPlayer = 1 << 8,
            Beggar = 1 << 9,
            CustomClass = 1 << 10,
            NotAGamebot = 1 << 11,
        }

        /// <summary>The row names of the exclusions, in the order of the enum.</summary>
        public static readonly (Exclusion Flag, string Name)[] ExclusionNames =
        [
            (Exclusion.GuildLeader, "leader"), (Exclusion.PlayerGuild, "player_guild"), (Exclusion.PlayerFriend, "friend"),
            (Exclusion.Grouped, "group"), (Exclusion.Raid, "raid"), (Exclusion.Trade, "trade"), (Exclusion.CraftOrder, "craft_order"),
            (Exclusion.Busy, "busy"), (Exclusion.NearPlayer, "near_player"), (Exclusion.Beggar, "beggar"),
            (Exclusion.CustomClass, "custom_class"), (Exclusion.NotAGamebot, "not_gamebot"),
        ];

        /// <summary>What is known about a possible retiree. Online-only facts are false for a bot that is offline.</summary>
        public readonly record struct Facts(bool GuildLeader, bool InPlayerGuild, bool OnPlayerFriendsList, bool Grouped,
            bool InRaidOrDuty, bool OpenTrade, bool OpenCraftOrder, bool Busy, bool PlayerNearby, bool Beggar, int ClassId,
            bool Gamebot);

        /// <summary>The exclusions the facts give. Pure.</summary>
        public static Exclusion Exclusions(Facts facts)
        {
            Exclusion found = Exclusion.None;
            if (facts.GuildLeader) found |= Exclusion.GuildLeader;
            if (facts.InPlayerGuild) found |= Exclusion.PlayerGuild;
            if (facts.OnPlayerFriendsList) found |= Exclusion.PlayerFriend;
            if (facts.Grouped) found |= Exclusion.Grouped;
            if (facts.InRaidOrDuty) found |= Exclusion.Raid;
            if (facts.OpenTrade) found |= Exclusion.Trade;
            if (facts.OpenCraftOrder) found |= Exclusion.CraftOrder;
            if (facts.Busy) found |= Exclusion.Busy;
            if (facts.PlayerNearby) found |= Exclusion.NearPlayer;
            if (facts.Beggar) found |= Exclusion.Beggar;
            if (!BotCustomClassRule.BotsMayPlay(facts.ClassId)) found |= Exclusion.CustomClass;
            if (!facts.Gamebot) found |= Exclusion.NotAGamebot;
            return found;
        }

        /// <summary>A guild role, for the retire order (a bot guild's leader never retires: Exclusion.GuildLeader).</summary>
        public enum GuildRole { None = 0, Member = 1, Officer = 2 }

        /// <summary>A gamebot of the realm at level 20 or above that might retire. <paramref name="Friends"/> is how many
        /// gamebots count it a friend (AutonomousBotBonds).</summary>
        public sealed record Candidate(long BotId, eRealm Realm, int ClassId, int Level, DateTime LastProgressUtc, bool Online,
            Exclusion Excluded, int Friends = 0, GuildRole Guild = GuildRole.None);

        public const int IdleHoursCap = 720;
        public const int PointsPerFriend = 60, FriendsCounted = 5;
        public const int PointsGuildMember = 120, PointsGuildOfficer = 240;
        public const int PointsPerLevelPlayed = 6;

        /// <summary>
        /// How soon a candidate retires; higher goes first. Owner decision 2026-11-25: "least-played and least-connected
        /// first, weighted toward the longest-idle. Use fewer friends, no guild role and the least recent activity." Idle
        /// hours (capped at 30 days, 720) carry the most weight; each bot friend takes 60 off (at most 5 counted), a guild
        /// member 120 and an officer 240; each level above 20 (the time played) takes 6 off (180 at 50). A bot with no
        /// recorded activity counts as idle the whole cap. Pure.
        /// </summary>
        public static int RetireScore(Candidate candidate, DateTime nowUtc)
        {
            double idle = candidate.LastProgressUtc == DateTime.MinValue ? IdleHoursCap
                : Math.Clamp((nowUtc - candidate.LastProgressUtc).TotalHours, 0, IdleHoursCap);
            int guild = candidate.Guild switch { GuildRole.Officer => PointsGuildOfficer, GuildRole.Member => PointsGuildMember, _ => 0 };
            return (int)Math.Round(idle) - PointsPerFriend * Math.Clamp(candidate.Friends, 0, FriendsCounted) - guild -
                   PointsPerLevelPlayed * Math.Clamp(candidate.Level - RetireMinLevel, 0, 30);
        }

        /// <summary>
        /// The retiree for a realm: a gamebot of that realm at level 20 or above with no exclusion and the highest
        /// <see cref="RetireScore"/>; on a tie the newer character (the higher id, less played). Never below 20. Null when
        /// none may retire. Pure.
        /// </summary>
        public static Candidate PickRetiree(IEnumerable<Candidate> candidates, eRealm realm, DateTime nowUtc) =>
            (candidates ?? [])
                .Where(c => c != null && c.Realm == realm && c.Level >= RetireMinLevel && c.Excluded == Exclusion.None)
                .OrderByDescending(c => RetireScore(c, nowUtc))
                .ThenByDescending(c => c.BotId)
                .FirstOrDefault();

        /// <summary>"leader:2,group:10" over the realm's candidates (level 20 or above), "0" when none was excluded. Pure.</summary>
        public static string ExcludedSummary(IEnumerable<Candidate> candidates, eRealm realm)
        {
            Candidate[] mine = (candidates ?? []).Where(c => c != null && c.Realm == realm && c.Level >= RetireMinLevel).ToArray();
            var parts = new List<string>();
            foreach ((Exclusion flag, string name) in ExclusionNames)
            {
                int n = mine.Count(c => (c.Excluded & flag) != 0);
                if (n > 0)
                    parts.Add($"{name}:{n}");
            }
            return parts.Count == 0 ? "0" : string.Join(",", parts);
        }

        /// <summary>The time a row's LastMeaningfulProgressUtc stands for (MinValue when it has none). Pure.</summary>
        public static DateTime ParseUtc(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime value)
                ? value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value
                : DateTime.MinValue;

        // ---- The newcomer's row and the retirement (pure) ------------------------------------------------------------

        /// <summary>
        /// A newcomer's roster row, as the launcher's "+ Lv.1" batch writes one (NewBotRow): level 1, no experience, realm
        /// points or coin, alive, queued. RegionId 0 and no LastSavedUtc make it a fresh, unplaced level-one record, so its
        /// first login places it at a Classic or Shrouded Isles start (AutonomousPopulationController, the same
        /// StartupLocation data the launcher picks from) and binds it there. Its personality, schedule and build come from
        /// its id at its first load, as for every gamebot. Pure.
        /// </summary>
        public static OfflineWorldBotRecord NewcomerRecord(AutonomousBotIdentityGenerator.Identity identity, DateTime nowUtc)
        {
            if (identity == null)
                throw new ArgumentNullException(nameof(identity));
            if (!BotCustomClassRule.BotsMayPlay((int)identity.CharacterClass))
                throw new ArgumentException(BotCustomClassRule.RefusedMessage, nameof(identity));
            string now = nowUtc.ToString("O");
            return new OfflineWorldBotRecord
            {
                Name = identity.Name,
                Realm = (int)identity.Realm,
                ClassId = (int)identity.CharacterClass,
                ClassName = identity.CharacterClass.ToString(),
                RaceId = (int)identity.Race,
                RaceName = identity.Race.ToString(),
                Gender = (int)identity.Gender,
                Level = 1,
                Experience = 0,
                RealmPoints = 0,
                MoneyCopper = 0,
                ZoneName = "Starting area",
                Activity = NewBotRow.QueuedActivity,
                CurrentGoal = NewBotRow.QueuedGoal,
                ObjectiveProgress = "Joined as a newcomer; placed at a Classic or Shrouded Isles start at its first login",
                IsOnline = false,
                IsAlive = true,
                Health = NewBotRow.StartingHealth,
                LastUpdateUtc = now,
                LastMeaningfulProgressUtc = now,
                LastSavedUtc = string.Empty,
                RegionId = 0,
                IsRetired = false,
            };
        }

        /// <summary>
        /// Retires one gamebot: IsRetired (never logged in again, out of the roster), offline, and the launcher's Activity
        /// says why (RetiredFromPlayMarker; the launcher shows "Retired", not "Deletion pending"). Nothing is deleted: the
        /// record, its items and its coin stay. True when the record changed (and needs saving). Pure.
        /// </summary>
        public static bool Retire(OfflineWorldBotRecord record)
        {
            if (record == null || record.IsRetired)
                return false;
            record.IsRetired = true;
            record.IsOnline = false;
            record.Activity = RetiredFromPlayMarker.ActivityFor(record.Level);
            record.CurrentGoal = string.Empty;
            record.LastUpdateUtc = DateTime.UtcNow.ToString("O");
            record.Dirty = true;
            return true;
        }

        // ---- Rows (pure) ---------------------------------------------------------------------------------------------

        public static string AddedRow(RealmCount count, Settings settings, OfflineWorldBotRecord newcomer, long? retiredId) =>
            $"{NewcomerTag} realm={RvrRecordText.Realm(count.Realm)} outcome=added id={newcomer.BotId} " +
            $"class={(eCharacterClass)newcomer.ClassId} race={(eRace)newcomer.RaceId} level={newcomer.Level} low={count.Low} " +
            $"floor={Floor(settings.FloorAtBasis, count.Roster)} roster={count.Roster} " +
            $"per_hour={PerHour(settings.PerHourAtBasis, count.Roster)} retired={(retiredId?.ToString(CultureInfo.InvariantCulture) ?? "-")}";

        public static string HeldRow(RealmCount count, Settings settings, string reason, string excluded) =>
            $"{NewcomerTag} realm={RvrRecordText.Realm(count.Realm)} outcome=held reason={reason} low={count.Low} " +
            $"floor={Floor(settings.FloorAtBasis, count.Roster)} roster={count.Roster} " +
            $"per_hour={PerHour(settings.PerHourAtBasis, count.Roster)} excluded={excluded}";

        public static string RetiredRow(Candidate retiree, long newcomerId, int candidates, string excluded, DateTime nowUtc) =>
            $"{RetiredTag} realm={RvrRecordText.Realm(retiree.Realm)} id={retiree.BotId} class={(eCharacterClass)retiree.ClassId} " +
            $"level={retiree.Level} online={(retiree.Online ? 1 : 0)} " +
            $"idle_h={(retiree.LastProgressUtc == DateTime.MinValue ? "-" : Math.Max(0, (int)(nowUtc - retiree.LastProgressUtc).TotalHours).ToString(CultureInfo.InvariantCulture))} " +
            $"friends={retiree.Friends} guild={retiree.Guild.ToString().ToLowerInvariant()} score={RetireScore(retiree, nowUtc)} " +
            $"newcomer={newcomerId} candidates={candidates} excluded={excluded} order=least_played_connected_idle";

        // ---- Runtime -------------------------------------------------------------------------------------------------

        private static readonly ConcurrentDictionary<eRealm, DateTime> LastAdded = new();
        private static readonly ConcurrentDictionary<eRealm, DateTime> LastHeldRow = new();
        private static readonly ConcurrentDictionary<eRealm, byte> InFlight = new();
        private static DateTime _nextCheckUtc;

        /// <summary>A server start: the spacing starts afresh (at most one newcomer a realm at once after the ramp).</summary>
        public static void Reset()
        {
            LastAdded.Clear();
            LastHeldRow.Clear();
            InFlight.Clear();
            _nextCheckUtc = DateTime.MinValue;
        }

        private sealed record Request(RealmCount Count, Settings Settings, HashSet<string> FriendNames, DateTime NowUtc);

        /// <summary>
        /// The population poll (background thread), once a minute: finds the realms below their floor and hands each to the
        /// game loop, where the retiree is chosen, the newcomer made and the retiree retired. Only with the population on and
        /// its startup ramp finished.
        /// </summary>
        internal static void Pulse(DateTime nowUtc, bool populationEnabled, bool rampFinished, OfflineWorldBotRecord[] loginRoster)
        {
            if (nowUtc < _nextCheckUtc)
                return;
            _nextCheckUtc = nowUtc + CheckInterval;
            Settings settings = Settings.Current;
            if (!populationEnabled || !rampFinished || !settings.On || loginRoster == null || loginRoster.Length == 0)
                return;
            try
            {
                (int Realm, int Level)[] levels = loginRoster
                    .Select(record => (record.Realm, AutonomousBotRegistry.TryGet(record.BotId, out GameBot live) && live != null
                        ? (int)live.Level : record.Level))
                    .ToArray();
                RealmCount[] due = Realms.Select(realm => Count(realm, levels))
                    .Where(count => !InFlight.ContainsKey(count.Realm) &&
                                    Due(count, settings, LastAdded.TryGetValue(count.Realm, out DateTime last) ? last : null, nowUtc))
                    .ToArray();
                if (due.Length == 0)
                    return;
                HashSet<string> friends = settings.Retire ? PlayerFriendNames() : new(StringComparer.OrdinalIgnoreCase);
                foreach (RealmCount count in due)
                {
                    if (!InFlight.TryAdd(count.Realm, 0))
                        continue;
                    NpcService.Instance.Post(OnGameLoop, new Request(count, settings, friends, nowUtc));
                }
            }
            catch (Exception exception)
            {
                Log.Error("The newcomer stream's check failed", exception);
            }
        }

        private static void OnGameLoop(Request request)
        {
            eRealm realm = request.Count.Realm;
            try
            {
                Candidate retiree = null;
                Candidate[] candidates = [];
                if (request.Settings.Retire)
                {
                    candidates = RetireCandidates(realm, request.FriendNames);
                    retiree = PickRetiree(candidates, realm, request.NowUtc);
                    if (retiree == null)
                    {
                        // The roster stays the same size: no newcomer until a bot at level 20 or above can make room.
                        if (!LastHeldRow.TryGetValue(realm, out DateTime held) || request.NowUtc - held >= HeldRowInterval)
                        {
                            LastHeldRow[realm] = request.NowUtc;
                            Emit(NewcomerTag, HeldRow(request.Count, request.Settings, "no_retiree", ExcludedSummary(candidates, realm)));
                        }
                        return;
                    }
                }

                OfflineWorldBotRecord newcomer = CreateNewcomer(realm);
                if (newcomer == null)
                {
                    LastAdded[realm] = request.NowUtc;   // spaced like a success, so a failing save doesn't retry every minute
                    return;
                }
                LastAdded[realm] = request.NowUtc;
                Emit(NewcomerTag, AddedRow(request.Count, request.Settings, newcomer, retiree?.BotId));
                if (retiree != null && RetireOnGameLoop(retiree))
                    Emit(RetiredTag, RetiredRow(retiree, newcomer.BotId, candidates.Count(c => c.Realm == realm && c.Level >= RetireMinLevel),
                        ExcludedSummary(candidates, realm), request.NowUtc));
                AutonomousPopulationController.RequestRosterRefresh();
            }
            catch (Exception exception)
            {
                Log.Error($"The newcomer stream failed for {RvrRecordText.Realm(realm)}", exception);
            }
            finally
            {
                InFlight.TryRemove(realm, out _);
            }
        }

        /// <summary>
        /// The new level-1 gamebot of a realm, saved to the roster. Its identity comes from the server's twin of the
        /// launcher's batch generator (era class shares, never class 63, the same name mix), with a name no roster row and
        /// no player character has; its row is <see cref="NewcomerRecord"/>. Null when it could not be made.
        /// </summary>
        private static OfflineWorldBotRecord CreateNewcomer(eRealm realm)
        {
            var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (OfflineWorldBotRecord row in DOLDB<OfflineWorldBotRecord>.SelectAllObjects())
                if (!string.IsNullOrEmpty(row.Name))
                    reserved.Add(row.Name);
            foreach (DbCoreCharacter character in DOLDB<DbCoreCharacter>.SelectAllObjects())
                if (!string.IsNullOrEmpty(character.Name))
                    reserved.Add(character.Name);
            AutonomousBotIdentityGenerator.Identity identity;
            try
            {
                eGender gender = NewBotRow.DrawGender(Random.Shared) == 2 ? eGender.Female : eGender.Male;
                identity = AutonomousBotIdentityGenerator.Generate(realm, gender, reserved, Random.Shared);
            }
            catch (Exception exception)
            {
                Log.Warn($"{NewcomerTag} realm={RvrRecordText.Realm(realm)} outcome=held reason=no_name", exception);
                return null;
            }
            OfflineWorldBotRecord record = NewcomerRecord(identity, DateTime.UtcNow);
            bool added;
            using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                added = GameServer.Database.AddObject(record);
            if (!added || record.BotId <= 0)
            {
                Log.Error($"{NewcomerTag} realm={RvrRecordText.Realm(realm)} outcome=held reason=save_failed");
                return null;
            }
            return record;
        }

        /// <summary>The realm's roster gamebots at level 20 or above with what keeps each from retiring now (game loop).</summary>
        private static Candidate[] RetireCandidates(eRealm realm, HashSet<string> friendNames)
        {
            var list = new List<Candidate>();
            foreach (OfflineWorldBotRecord record in AutonomousPopulationController.CachedRoster)
            {
                if (record == null || record.Realm != (int)realm || record.IsRetired)
                    continue;
                GameBot live = AutonomousBotRegistry.TryGet(record.BotId, out GameBot bot) ? bot : null;
                int level = live?.Level ?? record.Level;
                if (level < RetireMinLevel)
                    continue;
                GuildRole guild = AutonomousBotRealmGuilds.TryGetMembership(record.BotId, out AutonomousBotRealmGuilds.Membership m)
                    ? m.RankLevel <= 2 ? GuildRole.Officer : GuildRole.Member
                    : AutonomousBotGuilds.GuildOf(record.BotId) != null ? GuildRole.Member : GuildRole.None;
                int friends;
                try { friends = AutonomousBotBonds.Friends(record.BotId).Length; }
                catch (Exception) { friends = 0; }
                list.Add(new Candidate(record.BotId, realm, record.ClassId, level, ParseUtc(record.LastMeaningfulProgressUtc),
                    live != null, Exclusions(FactsOf(record, live, friendNames)), friends, guild));
            }
            return list.ToArray();
        }

        private static Facts FactsOf(OfflineWorldBotRecord record, GameBot live, HashSet<string> friendNames)
        {
            bool leader = AutonomousBotRealmGuilds.TryGetMembership(record.BotId, out AutonomousBotRealmGuilds.Membership membership) &&
                          membership.RankLevel == 0;
            bool craftOrder = AutonomousBotCraftOrders.OrdersOf(record.BotId)
                .Any(order => order.State is CraftOrderState.Open or CraftOrderState.Made);
            bool gamebot = !HiredCompanions165.IsHired(record.BotId) &&
                           (live == null || live.IsAutonomousWorldBot && !live.IsTemporaryGroupHelper && !live.IsHiredCompanion && !live.IsInMemoryOnly);
            bool grouped = false, duty = false, trade = false, busy = false, nearby = false;
            if (live != null)
            {
                grouped = live.Group != null;
                duty = BotDutyLeases.KindOf(live) != eDutyLeaseKind.None || AutonomousRealmRaid.IsReserved(live);
                trade = BotTrades.IsHeld(live);
                busy = !AutonomousBotSchedule.CanLogOut(live) || AutonomousBotSchedule.IsQuitting(live);
                try { nearby = live.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE).Any(player => player != null); }
                catch (Exception) { nearby = true; }
            }
            return new Facts(leader, AutonomousBotGuilds.GuildOf(record.BotId) != null,
                friendNames != null && !string.IsNullOrEmpty(record.Name) && friendNames.Contains(record.Name),
                grouped, duty, trade, craftOrder, busy, nearby, AutonomousBeggars.IsBeggarId(record.BotId), record.ClassId, gamebot);
        }

        /// <summary>
        /// Retires the chosen gamebot (game loop). One online leaves the world first, saved as at a logout; then its row is
        /// marked (Retire) and saved. Nothing is deleted. False when it changed its mind (busy again) or the save failed.
        /// </summary>
        private static bool RetireOnGameLoop(Candidate retiree)
        {
            OfflineWorldBotRecord record;
            if (AutonomousBotRegistry.TryGet(retiree.BotId, out GameBot live) && live != null)
            {
                if (live.Group != null || !AutonomousBotSchedule.CanLogOut(live))
                    return false;
                record = live.PersistentRecord;
                live.Delete();   // saved (position, items) and marked offline, as when it logs out
                AutonomousBotStatusPersistence.Forget(retiree.BotId);
            }
            else
                record = DOLDB<OfflineWorldBotRecord>.SelectObject(DB.Column("BotId").IsEqualTo(retiree.BotId));
            if (record == null || !Retire(record))
                return false;
            bool saved;
            using (AiProfile.Lock(AutonomousBotStatusPersistence.DatabaseWriteLock, AiSystem.LockDatabaseWrite))
                saved = GameServer.Database.SaveObject(record);
            if (!saved)
                Log.Error($"{RetiredTag} realm={RvrRecordText.Realm(retiree.Realm)} id={retiree.BotId} outcome=save_failed");
            return saved;
        }

        /// <summary>Every name on a real player's friends list: the saved characters' lists and the online players' own.</summary>
        private static HashSet<string> PlayerFriendNames()
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (DbCoreCharacter character in DOLDB<DbCoreCharacter>.SelectAllObjects())
                    foreach (string name in (character.SerializedFriendsList ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        names.Add(name);
                foreach (GamePlayer player in ClientService.Instance.GetPlayers())
                    foreach (string name in player?.SerializedFriendsList ?? [])
                        names.Add(name);
            }
            catch (Exception exception)
            {
                Log.Warn("The newcomer stream could not read the friends lists", exception);
            }
            return names;
        }

        private static void Emit(string tag, string row)
        {
            try
            {
                TestSink?.Invoke(row);
                long now;
                try { now = GameLoop.GameLoopTime; } catch { now = Environment.TickCount64; }
                if (Log.IsInfoEnabled && RvrRecordBudget.Take(tag, now, RowsPerMinute))
                    Log.Info(row);
            }
            catch
            {
                // Logging only.
            }
        }
    }
}
