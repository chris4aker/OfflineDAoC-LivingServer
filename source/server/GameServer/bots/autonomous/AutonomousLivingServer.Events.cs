using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS;

public static partial class AutonomousLivingServer
{
    #region Grats

    private static readonly ConcurrentDictionary<string, long> LastGratsFor = new(StringComparer.OrdinalIgnoreCase);
    public const long GratsCooldownMs = 20_000;

    /// <summary>True when bots already said grats for this character's level-up within the last <paramref name="ms"/> milliseconds.</summary>
    internal static bool GratsSaidRecently(string name, long ms) =>
        GratsOn && name != null && LastGratsFor.TryGetValue(name, out long last) && GameLoop.GameLoopTime - last < ms;

    private static void OnPlayerLevelUp(DOLEvent e, object sender, EventArgs args)
    {
        if (sender is GamePlayer player)
            OnLevelUp(player, player.Level);
    }

    /// <summary>A player or an autonomous gamebot gained a level: grats chains and server firsts.</summary>
    public static void OnLevelUp(GameLiving who, int level)
    {
        if (who == null || who is GameBot { IsAutonomousWorldBot: false } || who is GameBot { IsTemporaryGroupHelper: true })
            return;
        try
        {
            if (FirstsOn && level >= 50)
            {
                TryClaimFirst("First to reach level 50", who, "level 50");
                string className = ClassOf(who);
                if (!string.IsNullOrEmpty(className))
                    TryClaimFirst($"First {className} to reach level 50", who, $"50 as a {className.ToLowerInvariant()}");
            }
            if (GratsOn)
                Grats(who, level);
            AutonomousBotBodyLanguage.CheerDing(who);
        }
        catch (Exception exception)
        {
            Log.Warn($"Living Server: level-up handling for {who.Name} failed", exception);
        }
    }

    private static void Grats(GameLiving who, int level)
    {
        long now = GameLoop.GameLoopTime;
        if (LastGratsFor.TryGetValue(who.Name, out long last) && now - last < GratsCooldownMs)
            return;
        LastGratsFor[who.Name] = now;
        string topic = level >= 50 ? "fifty" : "general";
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = who.Name };

        // Groupmates first, in /group, when a real player is in the group.
        var said = new HashSet<GameBot>();
        if (who.Group != null && GroupHasPlayer(who.Group))
        {
            GameBot[] mates = who.Group.GetMembersInTheGroup().OfType<GameBot>()
                .Where(bot => bot != who && CanTalk(bot)).OrderBy(_ => Random.Shared.Next()).ToArray();
            int count = Math.Min(mates.Length, level >= 50 ? 3 : Random.Shared.Next(1, 4));
            for (int i = 0; i < count; i++)
            {
                GameBot bot = mates[i];
                said.Add(bot);
                Later(bot, 1_200 + i * 1_700 + Random.Shared.Next(1_500), () => bot.IsAlive && bot.Group == who.Group, () =>
                {
                    if (AutonomousBotVoices.TryEventLine(bot, "grats", out string line, topic, slots, noRepeat: false))
                        Speak(bot, Channel.Group, line);
                });
            }
        }

        // Bots standing nearby, in /say, when a real player can hear (the ding is seen on screen).
        if (who.CurrentRegion != null && (who is GamePlayer || ClientService.Instance.GetPlayersOfRegion(who.CurrentRegion)
                .Any(player => player.IsWithinRadius(who, WorldMgr.SAY_DISTANCE))))
        {
            GameBot[] nearby = who.GetNPCsInRadius(WorldMgr.SAY_DISTANCE).OfType<GameBot>()
                .Where(bot => bot != who && bot.Realm == who.Realm && !said.Contains(bot) && CanTalk(bot))
                .OrderBy(_ => Random.Shared.Next()).ToArray();
            int count = Math.Min(nearby.Length, who is GamePlayer ? Random.Shared.Next(0, 3) : Random.Shared.Next(0, 2));
            for (int i = 0; i < count; i++)
            {
                GameBot bot = nearby[i];
                Later(bot, 2_000 + i * 2_200 + Random.Shared.Next(2_000), () => bot.IsAlive, () =>
                {
                    if (AutonomousBotVoices.TryEventLine(bot, "grats", out string line, topic, slots, noRepeat: false))
                        Speak(bot, Channel.Say, line);
                });
            }
        }

        // Level 50 is realm news: a few bots of the realm cheer in /faction.
        if (level >= 50 && RealmHasPlayer(who.Realm))
        {
            GameBot[] realm = AutonomousBotRegistry.Snapshot()
                .Where(bot => bot != who && bot.Realm == who.Realm && !said.Contains(bot) && CanTalk(bot))
                .OrderBy(_ => Random.Shared.Next()).Take(Random.Shared.Next(1, 4)).ToArray();
            for (int i = 0; i < realm.Length; i++)
            {
                GameBot bot = realm[i];
                Later(bot, 4_000 + i * 3_500 + Random.Shared.Next(4_000), () => bot.IsAlive, () =>
                {
                    if (AutonomousBotVoices.TryEventLine(bot, "grats", out string line, "fifty", slots, noRepeat: false))
                        Speak(bot, Channel.Faction, line);
                });
            }
        }
    }

    #endregion

    #region Server firsts

    private static void OnPlayerRealmRankUp(DOLEvent e, object sender, EventArgs args)
    {
        if (sender is GamePlayer player && FirstsOn)
            OnRealmRank(player, player.RealmLevel / 10 + 1);
    }

    /// <summary>A gamebot's realm points were added (its realm level may have changed).</summary>
    public static void OnBotRealmLevel(GameBot bot, int previousRealmLevel)
    {
        if (!FirstsOn || bot == null || !bot.IsAutonomousWorldBot || bot.IsTemporaryGroupHelper)
            return;
        int before = previousRealmLevel / 10 + 1, after = bot.RealmLevel / 10 + 1;
        if (after > before)
            OnRealmRank(bot, after);
    }

    public const int FirstRealmRankTitle = 2;

    private static void OnRealmRank(GameLiving who, int rank)
    {
        try
        {
            for (int r = FirstRealmRankTitle; r <= rank; r++)
                TryClaimFirst($"First to realm rank {r}", who, $"realm rank {r}");
        }
        catch (Exception exception)
        {
            Log.Warn($"Living Server: realm rank first for {who.Name} failed", exception);
        }
    }

    /// <summary>Claims a first once. The achiever's realm cheers in /faction; everyone gets one system line.</summary>
    public static bool TryClaimFirst(string title, GameLiving who, string what)
    {
        // Server firsts belong to the realm's own characters, never a visiting Epic Special Forces member.
        if (who is GameBot { IsEpicSpecialForce: true })
            return false;
        var row = new OfflineServerFirstRecord
        {
            Title = title, Realm = (int)who.Realm, Name = who.Name, ClassName = ClassOf(who), IsPlayer = who is GamePlayer,
            AchievedUtc = DateTime.UtcNow.ToString("O"),
        };
        if (!Firsts.TryAdd(title, row))
            return false;
        Log.Info($"LIVING_SERVER_FIRST title=\"{title}\" name=\"{who.Name}\" realm={who.Realm} class=\"{row.ClassName}\"");
        foreach (GamePlayer player in ClientService.Instance.GetPlayers())
        {
            if (player?.Client == null)
                continue;
            string name = player.Realm == who.Realm ? who.Name
                : $"{RealmAdjective(who.Realm)} {row.ClassName.ToLowerInvariant()}".Trim();
            player.Out.SendMessage($"Server first: {name} is the first on the server to reach {what}.",
                eChatType.CT_ScreenCenterSmaller_And_CT_System, eChatLoc.CL_SystemWindow);
        }
        if (RealmHasPlayer(who.Realm))
        {
            var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = who.Name, ["what"] = what };
            GameBot[] cheer = AutonomousBotRegistry.Snapshot()
                .Where(bot => bot != who && bot.Realm == who.Realm && CanTalk(bot))
                .OrderBy(_ => Random.Shared.Next()).Take(Random.Shared.Next(1, 3)).ToArray();
            for (int i = 0; i < cheer.Length; i++)
            {
                GameBot bot = cheer[i];
                Later(bot, 6_000 + i * 4_000 + Random.Shared.Next(4_000), () => bot.IsAlive, () =>
                {
                    if (AutonomousBotVoices.TryEventLine(bot, "first", out string line, slots: slots, noRepeat: false))
                        Speak(bot, Channel.Faction, line);
                });
            }
        }
        return true;
    }

    /// <summary>"an Albion", "a Midgard", "a Hibernian" (as the game names realms' people).</summary>
    public static string RealmAdjective(eRealm realm) => realm switch
    {
        eRealm.Albion => "an Albion",
        eRealm.Midgard => "a Midgard",
        eRealm.Hibernia => "a Hibernian",
        _ => "a",
    };

    /// <summary>
    /// When firsts are switched on in a world that already has level 50s or high realm ranks, those
    /// titles are marked as reached before records began (no name, never announced), so the next
    /// character to get there is not wrongly called the first.
    /// </summary>
    /// <summary>
    /// The row that says records began (no name, so the Herald never lists it). Titles are seeded only
    /// when it is missing: the first start with the switch on, or the first one after the switch was
    /// off. Before, every start re-seeded, so level 50 bots made with the launcher's "+ Lv.50" button
    /// (born at 50, never reaching it) took "first to level 50" and every class title at the next
    /// restart, and nobody could earn them again (in-game check 2026-10-17).
    /// </summary>
    public const string RecordsBeganTitle = "~ records began";

    /// <summary>Seeds the firsts reached before records began, once (see <see cref="RecordsBeganTitle"/>).</summary>
    internal static void SeedFirstsOnce(bool firstsOn) => SeedFirstsOnce(Firsts, firstsOn, SeedFirsts);

    /// <summary>The rule of <see cref="SeedFirstsOnce(bool)"/> on any table (tests pass their own seeding).</summary>
    public static void SeedFirstsOnce(LivingServerTable<string, OfflineServerFirstRecord> firsts, bool firstsOn, Action seed)
    {
        bool began = firsts.TryGet(RecordsBeganTitle, out _);
        if (!firstsOn)
        {
            // Switched off: forget that records began, so switching on again re-seeds who got there meanwhile.
            if (began)
                firsts.RemoveWhere(row => string.Equals(row.Title, RecordsBeganTitle, StringComparison.OrdinalIgnoreCase));
            return;
        }
        if (began)
            return;
        seed();
        firsts.TryAdd(RecordsBeganTitle, new OfflineServerFirstRecord { Title = RecordsBeganTitle, AchievedUtc = DateTime.UtcNow.ToString("O") });
    }

    private static void SeedFirsts()
    {
        var reached = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Note(int level, string className, long realmPoints)
        {
            if (level >= 50)
            {
                reached.Add("First to reach level 50");
                if (!string.IsNullOrEmpty(className))
                    reached.Add($"First {className} to reach level 50");
            }
            int rank = AutonomousBotRealmPointRewards.RealmLevelFor(realmPoints) / 10 + 1;
            for (int r = FirstRealmRankTitle; r <= rank; r++)
                reached.Add($"First to realm rank {r}");
        }
        // Class titles use the class's display name, as live claims do (ClassOf), never the enum name.
        string ClassName(int id, string fallback) => ScriptMgr.FindCharacterClass(id)?.Name ?? fallback;
        foreach (OfflineWorldBotRecord bot in GameServer.Database.SelectAllObjects<OfflineWorldBotRecord>())
            if (!bot.IsRetired)
                Note(bot.Level, ClassName(bot.ClassId, bot.ClassName), bot.RealmPoints);
        foreach (DbCoreCharacter character in GameServer.Database.SelectAllObjects<DbCoreCharacter>())
            Note(character.Level, ClassName(character.Class, null), character.RealmPoints);
        int added = 0;
        foreach (string title in reached)
            if (Firsts.TryAdd(title, new OfflineServerFirstRecord { Title = title }))
                added++;
        if (added > 0 && Log.IsInfoEnabled)
            Log.Info($"Living Server: {added} server first(s) were already reached before records began");
    }

    #endregion

    #region Frontier alarms

    private static readonly Dictionary<eRealm, long> NextAlarmByRealm = new();
    private static readonly Dictionary<(eRealm, string), long> NextAlarmByPlace = new();
    public const long AlarmRealmCooldownMs = 120_000;
    public const long AlarmPlaceCooldownMs = 300_000;
    public const int AlarmMinimumEnemies = 3;

    /// <summary>Rough head count as players called it: exact below 10, then 10, 15, 20...</summary>
    public static string AlarmSize(int count) => count < 10 ? count.ToString() : (count / 5 * 5).ToString();

    /// <summary>A frontier bot saw enemies (called from its threat scan): warn the realm in /faction now and then.</summary>
    public static void ObserveEnemies(GameBot bot, IReadOnlyList<GameLiving> nearby)
    {
        NoteSighting(bot, nearby);
        if (!AlarmsOn || nearby == null || nearby.Count < AlarmMinimumEnemies || !IsSpeaker(bot) || !bot.IsAlive)
            return;
        long now = GameLoop.GameLoopTime;
        lock (NextAlarmByRealm)
        {
            if (NextAlarmByRealm.TryGetValue(bot.Realm, out long next) && now < next)
                return;
        }
        GameLiving[] fighters = nearby.Where(target => target is GamePlayer || target is GameBot).ToArray();
        if (fighters.Length < AlarmMinimumEnemies || !RealmHasPlayer(bot.Realm) || AutonomousBotDebates.IsRunning(bot.Realm))
            return;
        eRealm enemy = fighters.GroupBy(target => target.Realm).OrderByDescending(group => group.Count()).First().Key;
        int count = fighters.Count(target => target.Realm == enemy);
        if (count < AlarmMinimumEnemies)
            return;
        string place = FrontierPlaceOf(bot);
        lock (NextAlarmByRealm)
        {
            if (NextAlarmByRealm.TryGetValue(bot.Realm, out long next) && now < next ||
                NextAlarmByPlace.TryGetValue((bot.Realm, place), out long nextHere) && now < nextHere)
                return;
            NextAlarmByRealm[bot.Realm] = now + AlarmRealmCooldownMs;
            NextAlarmByPlace[(bot.Realm, place)] = now + AlarmPlaceCooldownMs;
            if (NextAlarmByPlace.Count > 64)
                foreach (var stale in NextAlarmByPlace.Where(pair => pair.Value < now).Select(pair => pair.Key).ToList())
                    NextAlarmByPlace.Remove(stale);
        }
        string classes = string.Join(", ", fighters.Where(target => target.Realm == enemy).Select(ClassOf)
            .Where(name => !string.IsNullOrEmpty(name)).Select(name => name.ToLowerInvariant()).Distinct().Take(3));
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["size"] = AlarmSize(count), ["enemy"] = RealmPlural(enemy), ["place"] = place, ["classes"] = classes,
        };
        if (AutonomousBotVoices.TryEventLine(bot, "alarm", out string line, slots: slots, flags: ["in_rvr"]))
        {
            Speak(bot, Channel.Faction, line);
            Log.Info($"LIVING_SERVER_ALARM realm={bot.Realm} enemy={enemy} count={count} place=\"{place}\" speaker=\"{bot.Name}\"");
        }
    }

    private static readonly Dictionary<(eRealm, string), long> NextSightingByZone = new();
    public const long SightingZoneCooldownMs = 60_000;

    /// <summary>
    /// The talking update: what a frontier bot sees goes on its realm's news board (AutonomousChatNews), so "where are the
    /// mids?" is answered from what the realm has seen, not from where every enemy really is.
    /// </summary>
    private static void NoteSighting(GameBot bot, IReadOnlyList<GameLiving> nearby)
    {
        if (!AutonomousBotVoices.Enabled || bot?.CurrentZone == null || nearby == null || nearby.Count == 0 || !bot.IsAlive)
            return;
        string zone = bot.CurrentZone.Description;
        long now = GameLoop.GameLoopTime;
        lock (NextSightingByZone)
            if (NextSightingByZone.TryGetValue((bot.Realm, zone), out long next) && now < next)
                return;
        var enemies = nearby.Where(target => (target is GamePlayer || target is GameBot) && target.IsAlive && target.Realm != bot.Realm &&
                                             target.Realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia)
            .GroupBy(target => target.Realm).OrderByDescending(group => group.Count()).FirstOrDefault();
        if (enemies == null)
            return;
        lock (NextSightingByZone)
        {
            if (NextSightingByZone.TryGetValue((bot.Realm, zone), out long next) && now < next)
                return;
            NextSightingByZone[(bot.Realm, zone)] = now + SightingZoneCooldownMs;
        }
        AutonomousChatNews.EnemySeen(bot.Realm, enemies.Key, zone, enemies.Count(), bot.CurrentRegionID, bot.X, bot.Y);
    }

    #endregion

    #region Nemesis and death

    private static readonly ConcurrentDictionary<long, long> NextNemesisLine = new();
    public const long NemesisLineCooldownMs = 20 * 60_000;
    public const int NemesisKills = 2;

    /// <summary>The living a kill belongs to (a pet's owner).</summary>
    internal static GameLiving KillerOf(GameObject killer) => killer switch
    {
        GameNPC { Brain: DOL.AI.Brain.IControlledBrain controlled } => controlled.GetLivingOwner() ?? killer as GameLiving,
        GameLiving living => living,
        _ => null,
    };

    /// <summary>An autonomous gamebot died (after the game's own death handling).</summary>
    public static void OnBotDied(GameBot bot, GameObject killerObject)
    {
        if (!IsSpeaker(bot))
            return;
        try
        {
            GameLiving killer = KillerOf(killerObject);
            if (NemesisOn && !bot.IsEpicSpecialForce && killer is GamePlayer or GameBot { IsAutonomousWorldBot: true } &&
                killer.Realm != bot.Realm && killer.Realm != eRealm.None)
                RecordKill(bot, killer);
            if (DeathOn)
                AskForRez(bot);
        }
        catch (Exception exception)
        {
            Log.Warn($"Living Server: death handling for {bot.Name} failed", exception);
        }
    }

    public const int NemesisRowsPerBot = 8;

    private static void RecordKill(GameBot bot, GameLiving killer)
    {
        string place = FrontierPlaceOf(bot);
        // Each bot keeps at most 8 killers: a new one replaces the row with the fewest kills, oldest first (review 2026-10-16).
        if (!Nemeses.TryGet((bot.DatabaseID, killer.Name), out _))
        {
            OfflineBotNemesisRecord[] mine = Nemeses.Where(row => row.BotId == bot.DatabaseID);
            if (mine.Length >= NemesisRowsPerBot)
            {
                OfflineBotNemesisRecord oldest = mine.OrderBy(row => row.Kills).ThenBy(row => row.LastUtc, StringComparer.Ordinal).First();
                Nemeses.RemoveWhere(row => ReferenceEquals(row, oldest));
            }
        }
        OfflineBotNemesisRecord row = Nemeses.Change((bot.DatabaseID, killer.Name), () => new OfflineBotNemesisRecord
        {
            BotId = bot.DatabaseID, KillerName = killer.Name,
        }, r =>
        {
            r.KillerRealm = (int)killer.Realm;
            r.KillerClass = ClassOf(killer);
            r.KillerRace = RaceOf(killer);
            r.KillerIsPlayer = killer is GamePlayer;
            r.Kills++;
            r.LastPlace = place;
            r.LastUtc = DateTime.UtcNow.ToString("O");
        });
        long now = GameLoop.GameLoopTime;
        if (row.Kills < NemesisKills || NextNemesisLine.TryGetValue(bot.DatabaseID, out long next) && now < next ||
            Random.Shared.NextDouble() >= 0.6 || !RealmHasPlayer(bot.Realm))
            return;
        NextNemesisLine[bot.DatabaseID] = now + NemesisLineCooldownMs;
        var slots = NemesisSlots(row);
        Later(bot, 4_000 + Random.Shared.Next(6_000), () => !AutonomousBotDebates.IsRunning(bot.Realm), () =>
        {
            if (AutonomousBotVoices.TryEventLine(bot, "nemesis", out string line, slots: slots))
                Speak(bot, Channel.Faction, line);
        });
    }

    private static Dictionary<string, string> NemesisSlots(OfflineBotNemesisRecord row) => new(StringComparer.Ordinal)
    {
        // Enemy names are hidden in the frontier: the realm sees a race and a class.
        ["killer"] = string.IsNullOrEmpty(row.KillerRace) ? null : row.KillerRace,
        ["killer_class"] = string.IsNullOrEmpty(row.KillerClass) ? null : row.KillerClass.ToLowerInvariant(),
        ["place"] = string.IsNullOrEmpty(row.LastPlace) ? null : row.LastPlace,
    };

    /// <summary>An autonomous gamebot killed someone: revenge on a nemesis (one who killed it twice or more).</summary>
    public static void OnBotKilled(GameBot bot, GameLiving victim)
    {
        if (!NemesisOn || !IsSpeaker(bot) || victim == null || victim.Realm == bot.Realm ||
            !Nemeses.TryGet((bot.DatabaseID, victim.Name), out OfflineBotNemesisRecord row) || row.Kills < NemesisKills)
            return;
        Nemeses.Change((bot.DatabaseID, victim.Name), () => row, r => r.Revenges++);
        if (Random.Shared.NextDouble() >= 0.7 || !RealmHasPlayer(bot.Realm))
            return;
        var slots = NemesisSlots(row);
        Later(bot, 1_500 + Random.Shared.Next(2_500), () => bot.IsAlive && !AutonomousBotDebates.IsRunning(bot.Realm), () =>
        {
            if (AutonomousBotVoices.TryEventLine(bot, "revenge", out string line, slots: slots))
                Speak(bot, Channel.Faction, line);
        });
    }

    private static readonly ConcurrentDictionary<long, long> AskedAtDeath = new();

    /// <summary>A real player of the bot's realm who can rez is near enough to ask (2,500 units, same region).</summary>
    public static GamePlayer NearbyRezzer(GameBot bot, int range = 2_500)
    {
        if (bot?.CurrentRegion == null)
            return null;
        return ClientService.Instance.GetPlayersOfRegion(bot.CurrentRegion)
            .Where(player => player.IsAlive && player.Realm == bot.Realm && player.ObjectState == GameObject.eObjectState.Active &&
                             bot.IsWithinRadius(player, range) && CanRez(player))
            .OrderBy(bot.GetDistanceTo).FirstOrDefault();
    }

    /// <summary>A rez ask is logged at Info only when a player could have answered it.</summary>
    public static bool RezAskWorthLogging(bool rezzerNear, bool rezClassNear, bool playerHears, bool inPlayerGroup) =>
        rezzerNear || rezClassNear || playerHears || inPlayerGroup;

    private static void AskForRez(GameBot bot)
    {
        long deathTick = GameLoop.GameLoopTime;
        string diedAt = $"{bot.CurrentRegionID}:{bot.X},{bot.Y},{bot.Z}";
        AskedAtDeath[bot.DatabaseID] = deathTick;
        Later(bot, 3_000 + Random.Shared.Next(4_000), () => !bot.IsAlive && AskedAtDeath.TryGetValue(bot.DatabaseID, out long t) && t == deathTick, () =>
        {
            GamePlayer rezzer = NearbyRezzer(bot);
            if (Log.IsInfoEnabled)
            {
                // Says who could have rezzed, so a missing ask can be checked in game (in-game check 2026-10-17).
                GamePlayer rezClass = rezzer ?? (bot.CurrentRegion == null ? null : ClientService.Instance.GetPlayersOfRegion(bot.CurrentRegion).FirstOrDefault(player =>
                    player.Realm == bot.Realm && IsRezClass(player) && bot.IsWithinRadius(player, 2_500)));
                // Playtest 2026-10-18: 2,966 of these in three hours, nearly all for bots dying far from any player.
                // Only a death a player could have answered is worth the line; the rest go to Debug.
                if (RezAskWorthLogging(rezzer != null, rezClass != null, PlayerHears(bot), bot.Group != null && GroupHasPlayer(bot.Group)))
                    Log.Info($"LIVING_SERVER_REZ_ASK bot=\"{bot.Name}\" rezzer=\"{rezzer?.Name ?? "none"}\"" +
                         (rezzer == null && rezClass != null
                             ? $" near_rez_class=\"{rezClass.Name}\" alive={rezClass.IsAlive} has_rez_spell={CanRez(rezClass)}"
                             : string.Empty) +
                         $" died_at={diedAt} now_at={bot.CurrentRegionID}:{bot.X},{bot.Y},{bot.Z} moving={bot.IsMoving}" +
                         $" region={bot.CurrentRegionID} players={DescribePlayersNear(bot)}");
                else if (Log.IsDebugEnabled)
                    Log.Debug($"LIVING_SERVER_REZ_ASK bot=\"{bot.Name}\" rezzer=\"none\" died_at={diedAt} no_player_near=true");
            }
            bool grouped = rezzer != null && bot.Group != null && bot.Group == rezzer.Group;
            Channel channel = grouped || bot.Group != null && GroupHasPlayer(bot.Group) ? Channel.Group : Channel.Say;
            if (rezzer != null)
            {
                var slots = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["player"] = rezzer.Name, ["player_class"] = ClassOf(rezzer).ToLowerInvariant(),
                };
                if (AutonomousBotVoices.TryEventLine(bot, "rez_ask", out string ask, "player", slots, ["just_died", "player_nearby"], noRepeat: false))
                {
                    // Grouped: /group. Close: /say. Farther away: a /send, as players did.
                    if (grouped)
                        Speak(bot, Channel.Group, ask);
                    else if (bot.IsWithinRadius(rezzer, WorldMgr.SAY_DISTANCE))
                        Speak(bot, Channel.Say, ask);
                    else
                        Tell(bot, rezzer, ask);
                }
            }
            else if ((channel == Channel.Group || PlayerHears(bot)) && Random.Shared.NextDouble() < 0.4 &&
                     AutonomousBotVoices.TryEventLine(bot, "rez_ask", out string general, "general", flags: ["just_died"], noRepeat: false))
                Speak(bot, channel, general);
        });
    }

    /// <summary>Every player online, for the rez ask log: name, region, realm, distance, class, can rez.</summary>
    private static string DescribePlayersNear(GameBot bot)
    {
        var parts = ClientService.Instance.GetPlayers().Select(player =>
            $"{player.Name}(region={player.CurrentRegionID},at={player.X},{player.Y},{player.Z},realm={(int)player.Realm},alive={player.IsAlive}," +
            $"state={player.ObjectState},distance={(player.CurrentRegionID == bot.CurrentRegionID ? (int)bot.GetDistanceTo(player) : -1)}," +
            $"class={player.CharacterClass?.ID},rez_class={IsRezClass(player)},can_rez={CanRez(player)})");
        return "[" + string.Join(";", parts) + "]";
    }

    /// <summary>
    /// True when the bot should wait the long (90 s) release timer: with the death part on, a real
    /// player of its realm who can rez is nearby even if not in its group.
    /// </summary>
    public static bool HasNearbyPlayerRezzer(GameBot bot) => DeathOn && IsSpeaker(bot) && NearbyRezzer(bot) != null;

    /// <summary>The bot is about to release to its bind point.</summary>
    public static void OnBotReleasing(GameBot bot)
    {
        if (!DeathOn || !IsSpeaker(bot))
            return;
        // A failed release transfer retries every 3 s; the corpse line is said once per death.
        if (!AskedAtDeath.TryRemove(bot.DatabaseID, out _))
            return;
        Channel channel = GroupHasPlayer(bot.Group) ? Channel.Group : Channel.Say;
        if ((channel == Channel.Group || PlayerHears(bot)) && Random.Shared.NextDouble() < 0.6 &&
            AutonomousBotVoices.TryEventLine(bot, "corpse", out string line, flags: ["just_died"], noRepeat: false))
            Speak(bot, channel, line);
        // Bots do not get resurrection illness; a level 10+ bot in a player's group may still grumble about it.
        if (GroupHasPlayer(bot.Group) && bot.Level >= Properties.RESS_SICKNESS_LEVEL && Random.Shared.NextDouble() < 0.35)
            Later(bot, 12_000 + Random.Shared.Next(10_000), () => bot.IsAlive && GroupHasPlayer(bot.Group), () =>
            {
                if (AutonomousBotVoices.TryEventLine(bot, "sickness", out string sick, noRepeat: false))
                    Speak(bot, Channel.Group, sick);
            });
    }

    /// <summary>
    /// The bot was rezzed. Returns true when a Living Server thank-you was scheduled (the caller then
    /// skips its old line). Also remembers the rezzer when memory is on.
    /// </summary>
    public static bool OnBotResurrected(GameBot bot, GameLiving caster)
    {
        if (!IsSpeaker(bot))
            return false;
        AskedAtDeath.TryRemove(bot.DatabaseID, out _);
        if (MemoryOn && caster is GamePlayer player && player.Realm == bot.Realm)
            Remember(bot, player, "rez");
        if (!DeathOn)
            return false;
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = caster?.Name };
        Channel channel = caster != null && bot.Group != null && bot.Group == caster.Group ? Channel.Group : Channel.Say;
        Later(bot, 1_500 + Random.Shared.Next(2_000), () => bot.IsAlive, () =>
        {
            if (AutonomousBotVoices.TryEventLine(bot, "rez_thanks", out string line, slots: slots, noRepeat: false))
                Speak(bot, channel, line);
        });
        return true;
    }

    /// <summary>An autonomous gamebot is about to rez a real player who is not in its group.</summary>
    public static void OnOfferingRez(GameBot bot, GamePlayer player)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["target"] = player.Name };
        if (AutonomousBotVoices.TryEventLine(bot, "rez_offer", out string line, slots: slots, noRepeat: false))
            Speak(bot, Channel.Say, line);
    }

    #endregion
}
