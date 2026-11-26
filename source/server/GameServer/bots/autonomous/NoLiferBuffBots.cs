using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The No Lifers' buffbots (Living Server 1.2, owner 2026-11-15; docs/BOT-PERSONALITIES.md, docs/research/BUFFBOTS-2003.md).
/// <c>bot_nolifer_buffbot_percent</c> (30 by default) of the No Lifer gamebots run a second character: a level-50 buffbot of
/// their realm, all in its buff line (Albion Cleric, Enhancement 50; Midgard Shaman, Augmentation 50; Hibernia Druid,
/// Nurture 50). In 2003 a buffbot was a second paid account, parked in a portal keep or a town, or following its main around
/// on /follow; "everybody has a buffbot" (FreddysHouse, June 2003).
/// <list type="bullet">
/// <item>Which mains have one is fixed from the main's roster id (a roll under the percent, among the No Lifers), so new and
/// existing rosters alike get theirs on the next start, and the same mains keep theirs.</item>
/// <item>The buffbot lives in memory only, like the Epic Special Forces: never saved, never in the roster, the population
/// count, the planners or any group. Its id, name, race and gender are fixed from its main's id, so it comes back the same
/// after a restart. It logs in when its main is online and logs out with it.</item>
/// <item>It arrives trained at level 50 and wears what the launcher's level-50 bots wear: the best plain merchant item of
/// its realm for its level in each slot (AutonomousStarterGear). No coin, no loot, no fights.</item>
/// <item>It follows its main at a distance where that is safe (the main's own realm's PvE land), hangs back in a fight, and
/// parks at the nearest bind of its realm when the main goes where it would not follow: a dungeon, the frontier (the portal
/// keep, as in 2003), a battleground, a raid. Attacked, it runs to that bind; dead, it releases there and comes back.</item>
/// <item>It keeps its main in every single-target buff it has (<see cref="PlanBuffs"/>): concentration buffs first, then
/// timed ones, recast as they drop (expiry, the main's death). Group-only buffs need a group, and it never joins one.</item>
/// <item>Its concentration buffs follow the 2003 rule: no range (<see cref="ConcentrationRangeFree"/>). Mythic added the
/// 5,000-unit limit, and buffs only for the buffer's group, with its Classic servers in July 2005.</item>
/// </list>
/// Real players and companion bots never get one. Raid and epic encounter code is unchanged: a buffbot never enters a raid.
/// </summary>
public static class NoLiferBuffBots
{
    private static readonly Logger Log = LoggerManager.Create(typeof(NoLiferBuffBots));

    /// <summary>Below the Epic Special Forces' 1,900,000,000 and far above any roster id; a buffbot's id is this plus its main's.</summary>
    public const long FirstBotId = 1_800_000_000;
    /// <summary>The highest main id that has room for its buffbot's id below the Special Forces.</summary>
    public const long MaximumMainId = 99_999_999;
    private const long Salt = 0x6275_6666_6274;   // "buffbt"

    // Distances, in game units. Buff spells reach 1,000.
    public const int FollowDistance = 450;            // out of a fight, this far behind the main
    public const int FightDistance = 900;             // the main is fighting: no closer than this
    public const int FollowLeash = 9_000;             // the follow gives up beyond this; the next think closes the gap
    public const int CastReach = 950;                 // cast when this close
    public const int RelocateDistance = 12_000;       // farther than this in the same region: it logs in closer, as a player would
    public const int ParkArrived = 250;
    public const int MaxCreationsPerPulse = 4;
    public const long PulseIntervalMilliseconds = 5_000;
    public const int ThinkIntervalMilliseconds = 1_500;
    public const int AttackedMemoryMilliseconds = 8_000;

    public static bool On => Properties.BOT_NOLIFER_BUFFBOTS;
    public static int Percent => Math.Clamp(Properties.BOT_NOLIFER_BUFFBOT_PERCENT, 0, 100);
    public static bool ConcentrationRangeFree => Properties.BOT_NOLIFER_BUFFBOT_ERA_CONC_RANGE;

    #region Who has one (pure)

    /// <summary>The main's stable roll, 0 to 1, from its roster id.</summary>
    public static double Roll(long mainId) => AutonomousBotSchedule.Unit(mainId, Salt);

    /// <summary>
    /// Whether a roster id runs a buffbot at this percent: a No Lifer (AutonomousBotPersonality.TypeFor, fixed from the id
    /// whether personalities are on or off) whose stable roll is under the percent. Pure.
    /// </summary>
    public static bool HasBuffBot(long mainId, int percent) =>
        mainId > 0 && mainId <= MaximumMainId && percent > 0 &&
        AutonomousBotPersonality.TypeFor(mainId) == BotPersonalityType.NoLifer &&
        Roll(mainId) < Math.Clamp(percent, 0, 100) / 100.0;

    /// <summary>The buffbot's id for a main (0 when the main can't have one). Pure.</summary>
    public static long BuffBotIdFor(long mainId) => mainId > 0 && mainId <= MaximumMainId ? FirstBotId + mainId : 0;

    /// <summary>A live main that has a buffbot now: an autonomous gamebot of the roster (not a companion, helper or in-memory bot).</summary>
    public static bool IsMain(GameBot bot) =>
        On && bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsInMemoryOnly: false } &&
        bot.ObjectState == GameObject.eObjectState.Active && HasBuffBot(bot.DatabaseID, Percent);

    /// <summary>The buffbot class of each realm (2003 practice; see the research note). Pure.</summary>
    public static eCharacterClass ClassFor(eRealm realm) => realm switch
    {
        eRealm.Albion => eCharacterClass.Cleric,
        eRealm.Midgard => eCharacterClass.Shaman,
        eRealm.Hibernia => eCharacterClass.Druid,
        _ => eCharacterClass.Unknown,
    };

    public static bool IsBuffBotClass(eCharacterClass characterClass) =>
        characterClass is eCharacterClass.Cleric or eCharacterClass.Shaman or eCharacterClass.Druid;

    /// <summary>
    /// The buffbot's name: about one in three are named after the main, as many were ("Thorgrimbuff"); the rest get an ordinary
    /// name of the era. Fixed from the main's id. Null asks the realm's name generator (with the same fixed seed). Pure.
    /// </summary>
    public static string MainStyleName(string mainName, long mainId, ISet<string> taken)
    {
        if (string.IsNullOrWhiteSpace(mainName) || Roll(mainId * 7 + 3) >= 0.35)
            return null;
        string[] suffixes = ["buff", "buffs", "buffer", "bb"];
        string stem = mainName.Trim();
        int first = (int)(Roll(mainId * 11 + 5) * suffixes.Length) % suffixes.Length;
        for (int i = 0; i < suffixes.Length; i++)
        {
            string name = stem + suffixes[(first + i) % suffixes.Length];
            if (name.Length <= 20 && (taken == null || !taken.Contains(name)))
                return name;
        }
        return null;
    }

    /// <summary>
    /// The buffbot's gender, race and generated name, fixed from its main's id (the same seed every start). The name also
    /// depends on the names taken at that moment (<paramref name="names"/>, to which it is added); gender and race do not.
    /// Shared by the login and by <see cref="BuffBotCopyRetirement"/>, which recognises the copies an earlier build saved.
    /// </summary>
    public static AutonomousBotIdentityGenerator.Identity DrawIdentity(long mainId, eRealm realm, ISet<string> names)
    {
        var random = new Random(unchecked((int)(AutonomousBotPersonality.Mix(mainId, (ulong)Salt) & 0x7FFF_FFFF)));
        eGender gender = random.Next(2) == 0 ? eGender.Male : eGender.Female;
        return AutonomousBotIdentityGenerator.GenerateForClass(realm, gender, ClassFor(realm), names, random);
    }

    /// <summary>The four "named after the main" names, in no particular order, when this main's buffbot is one of those
    /// (<see cref="MainStyleName"/> takes the first of them not taken). Empty otherwise. Pure.</summary>
    public static IReadOnlyList<string> MainStyleNames(string mainName, long mainId)
    {
        if (string.IsNullOrWhiteSpace(mainName) || Roll(mainId * 7 + 3) >= 0.35)
            return Array.Empty<string>();
        string stem = mainName.Trim();
        return new[] { "buff", "buffs", "buffer", "bb" }.Select(suffix => stem + suffix).Where(name => name.Length <= 20).ToArray();
    }

    #endregion

    #region The buff plan (pure)

    /// <summary>One spell the buffbot knows, as the plan needs it.</summary>
    public readonly record struct BuffCandidate(int SpellId, string Name, eSpellType Type, eSpellTarget Target, int Concentration,
        int Level, bool Timed);

    /// <summary>
    /// The order a buffbot buffs in: armour and the big spec buffs first, then the base stat buffs, then the rest. 2003
    /// buffbots ran spec AF, str/con and dex/qui on everyone, plus the base buffs, acuity for casters and haste and the
    /// damage add for melee.
    /// </summary>
    public static readonly eSpellType[] Priority =
    [
        eSpellType.SpecArmorFactorBuff, eSpellType.StrengthConstitutionBuff, eSpellType.DexterityQuicknessBuff,
        eSpellType.BaseArmorFactorBuff, eSpellType.StrengthBuff, eSpellType.ConstitutionBuff, eSpellType.DexterityBuff,
        eSpellType.AcuityBuff, eSpellType.CombatSpeedBuff, eSpellType.DamageAdd, eSpellType.DamageShield,
        eSpellType.HealthRegenBuff, eSpellType.EnduranceRegenBuff,
    ];

    /// <summary>Whether the main has a use for a buff type: acuity needs a power stat, haste, damage add and endurance a weapon. Pure.</summary>
    public static bool Suits(eSpellType type, bool mainUsesPower, bool mainUsesWeapons) => type switch
    {
        eSpellType.AcuityBuff => mainUsesPower,
        eSpellType.CombatSpeedBuff or eSpellType.DamageAdd or eSpellType.EnduranceRegenBuff => mainUsesWeapons,
        _ => true,
    };

    /// <summary>
    /// The buffs a buffbot keeps on its main, in casting order: single-target (realm) buffs only, the best known rank of each
    /// type, the types its main has a use for, concentration buffs while its pool lasts (in priority order) and every timed
    /// one. Group, self and pet spells are left out: they need a group, and a buffbot never joins one. A type the target
    /// refuses (<paramref name="refused"/>: a real player's <c>/buff refuse</c>, #661) is left out too, and its
    /// concentration stays in the pool for the next buff. Pure.
    /// </summary>
    public static IReadOnlyList<BuffCandidate> PlanBuffs(IEnumerable<BuffCandidate> known, bool mainUsesPower, bool mainUsesWeapons,
        int concentrationPool, IReadOnlyCollection<eSpellType> refused = null)
    {
        var best = new Dictionary<eSpellType, BuffCandidate>();
        foreach (BuffCandidate spell in known ?? [])
        {
            if (spell.Target != eSpellTarget.REALM || Array.IndexOf(Priority, spell.Type) < 0 ||
                refused != null && refused.Contains(spell.Type) ||
                !Suits(spell.Type, mainUsesPower, mainUsesWeapons) || (!spell.Timed && spell.Concentration <= 0))
                continue;
            if (!best.TryGetValue(spell.Type, out BuffCandidate held) || spell.Level > held.Level ||
                spell.Level == held.Level && spell.Concentration > held.Concentration)
                best[spell.Type] = spell;
        }
        var plan = new List<BuffCandidate>();
        int pool = Math.Max(0, concentrationPool);
        foreach (eSpellType type in Priority)
        {
            if (!best.TryGetValue(type, out BuffCandidate spell))
                continue;
            if (!spell.Timed)
            {
                if (spell.Concentration > pool)
                    continue;
                pool -= spell.Concentration;
            }
            plan.Add(spell);
        }
        return plan;
    }

    #endregion

    #region Follow or park (pure)

    public enum Stance
    {
        /// <summary>Behind the main, out of its fights.</summary>
        Follow,
        /// <summary>At the nearest bind of its realm (where it would not follow, or the main is down).</summary>
        Park,
        /// <summary>It logs in again close to where it is wanted (another region, or very far in this one).</summary>
        Relocate,
        /// <summary>Attacked: to the bind, fighting nothing.</summary>
        Flee,
        /// <summary>Stays where it is (the main is riding a stable route or lying dead).</summary>
        Wait,
        /// <summary>Dead: the release timer takes it to a bind.</summary>
        Dead,
    }

    public readonly record struct Situation(bool BuffBotAlive, bool BuffBotAttacked, bool MainAlive, bool SameRegion, double Distance,
        bool MainInDungeon, bool MainInFrontier, bool MainInBattleground, bool MainInRaid, bool MainOnStableRoute);

    /// <summary>
    /// What a buffbot does now, and why. It follows only through its realm's ordinary PvE land: a dungeon, the frontier, a
    /// battleground (level-capped) or a raid is where 2003 players left their buffbot in a portal keep or a town. Pure.
    /// </summary>
    public static (Stance Stance, string Reason) Decide(Situation s)
    {
        if (!s.BuffBotAlive) return (Stance.Dead, "dead");
        if (s.BuffBotAttacked) return (Stance.Flee, "attacked");
        if (s.MainInRaid) return (Stance.Park, "main on a raid");
        if (s.MainInBattleground) return (Stance.Park, "main in a battleground");
        if (s.MainInDungeon) return (Stance.Park, "main in a dungeon");
        if (s.MainInFrontier) return (Stance.Park, "main in the frontier");
        if (s.MainOnStableRoute) return (Stance.Wait, "main riding a stable route");
        if (!s.MainAlive) return (Stance.Wait, "main dead");
        if (!s.SameRegion) return (Stance.Relocate, "main in another region");
        if (s.Distance > RelocateDistance) return (Stance.Relocate, "main far away");
        return (Stance.Follow, "main in its realm's land");
    }

    /// <summary>How close it stays: never into the main's fight. Pure.</summary>
    public static int FollowDistanceFor(bool mainInCombat) => mainInCombat ? FightDistance : FollowDistance;

    /// <summary>Whether it may cast a buff on its main now: alive, within reach, and not walking into a fight. Pure.</summary>
    public static bool MayBuff(Stance stance, bool mainAlive, bool sameRegion, double distance, bool selfInCombat) =>
        stance is Stance.Follow or Stance.Park or Stance.Wait && mainAlive && sameRegion && distance <= CastReach && !selfInCombat;

    /// <summary>A buffbot never joins a group: true for every buffbot, whoever asks. Pure.</summary>
    public static bool MayJoinGroup(GameBot bot) => bot?.IsNoLiferBuffBot != true;

    #endregion

    #region Lines (a parked second character says very little)

    public static readonly string[] TellLines =
    [
        "afk buffbot", "buffbot, msg {main}", "this is {main}'s buffbot", "buffbot sry", "afk", "{main}'s buffer, send to {main}",
        "bb, not at keyboard", "just a buffbot :)",
    ];

    public static readonly string[] InviteLines =
    [
        "sry buffbot, cant group", "buffbot, no grp", "no thx, buffbot", "buffbot - ask {main}",
    ];

    /// <summary>A line with its main's name filled in. Pure.</summary>
    public static string Line(string[] lines, string mainName, int roll) =>
        lines[Math.Abs(roll) % lines.Length].Replace("{main}", string.IsNullOrWhiteSpace(mainName) ? "my main" : mainName);

    #endregion

    #region Live: login, logout

    private sealed class State
    {
        public long MainId;
        public string MainName = string.Empty;
        public Stance Stance = (Stance)(-1);
        public string Reason = string.Empty;
        public long NextBuffLogAt;
        public int BuffsCast;
        public bool FullSetLogged;
    }

    private static readonly ConcurrentDictionary<long, State> States = new();
    private static readonly ConcurrentDictionary<string, long> LastLineTo = new(StringComparer.OrdinalIgnoreCase);
    private static long _nextPulse;

    /// <summary>The main of a buffbot (null when it is offline).</summary>
    public static GameBot MainOf(GameBot buffBot) =>
        buffBot?.IsNoLiferBuffBot == true && AutonomousBotRegistry.TryGet(buffBot.BuffBotMainId, out GameBot main) &&
        main.ObjectState == GameObject.eObjectState.Active && !main.IsInMemoryOnly ? main : null;

    /// <summary>The buffbot wears its main's guild tag (a second character in the same guild).</summary>
    public static string MainGuildName(GameBot buffBot) => MainOf(buffBot)?.GuildName ?? string.Empty;

    /// <summary>Game loop, coordinator phase: buffbots log in for online mains and out when their main leaves.</summary>
    public static void Pulse(long now)
    {
        if (now < _nextPulse)
            return;
        _nextPulse = now + PulseIntervalMilliseconds;
        try
        {
            GameBot[] online = AutonomousBotRegistry.BuffBotSnapshot();
            var byMain = new Dictionary<long, GameBot>();
            foreach (GameBot buffBot in online)
            {
                GameBot main = MainOf(buffBot);
                if (!On || main == null || !IsMain(main))
                {
                    LogOut(buffBot, !On ? "buffbots switched off" : main == null ? "main logged out" : "main no longer has one");
                    continue;
                }
                byMain[main.DatabaseID] = buffBot;
            }
            if (!On)
                return;
            int created = 0;
            HashSet<string> names = null;
            foreach (GameBot main in AutonomousBotRegistry.Snapshot())
            {
                if (created >= MaxCreationsPerPulse)
                    break;
                if (byMain.ContainsKey(main.DatabaseID) || !IsMain(main) || !main.IsAlive || main.IsOnStableMasterRoute)
                    continue;
                names ??= new HashSet<string>(AutonomousBotRegistry.Snapshot().Select(b => b.Name)
                    .Concat(online.Select(b => b.Name)), StringComparer.OrdinalIgnoreCase);
                if (LogIn(main, names) is GameBot made)
                {
                    names.Add(made.Name);
                    created++;
                }
            }
        }
        catch (Exception exception)
        {
            Log.Error("NoLiferBuffBots.Pulse failed", exception);
        }
    }

    private static GameBot LogIn(GameBot main, ISet<string> names)
    {
        long id = BuffBotIdFor(main.DatabaseID);
        eCharacterClass characterClass = ClassFor(main.Realm);
        if (id == 0 || characterClass == eCharacterClass.Unknown || AutonomousBotRegistry.Contains(id))
            return null;
        try
        {
            var identity = DrawIdentity(main.DatabaseID, main.Realm, names);
            eGender gender = identity.Gender;
            string name = MainStyleName(main.Name, main.DatabaseID, names) ?? identity.Name;
            (ushort region, Vector3 spot, string where) = ParkSpot(main);
            if (region == 0)
                return null;
            string now = DateTime.UtcNow.ToString("O");
            var record = new OfflineWorldBotRecord
            {
                BotId = id, Name = name, Realm = (int)main.Realm, ClassId = (int)characterClass, RaceId = (int)identity.Race,
                RaceName = identity.Race.ToString(), Gender = (int)gender, Level = 50, Experience = 169_999_999_950L,
                IsOnline = true, IsAlive = true, Activity = "Buffbot", CurrentGoal = $"Keeping {main.Name} buffed",
                LastUpdateUtc = now, RegionId = region, X = (int)spot.X, Y = (int)spot.Y, Z = (int)spot.Z,
                BindRegionId = region, BindX = (int)spot.X, BindY = (int)spot.Y, BindZ = (int)spot.Z,
                Health = 1_000_000, Mana = 1_000_000, Endurance = 1_000_000,
            };
            GameBot bot = GameBot.CreateNoLiferBuffBot(record, main.DatabaseID);
            bot.SynchronizePositionForLoginValidation();
            AutonomousStuckWatchdog.RepairInvalidLoginPosition(bot);
            if (!bot.AddToWorld())
            {
                bot.Delete();
                return null;
            }
            States[id] = new State { MainId = main.DatabaseID, MainName = main.Name };
            Log.Info($"BUFFBOT_CREATED buffbot=\"{bot.Name}\" id={id} main=\"{main.Name}\" mainId={main.DatabaseID} " +
                     $"realm={GlobalConstants.RealmToName(main.Realm)} class=\"{bot.ClassName}\" race={identity.Race} level={bot.Level} " +
                     $"spec=\"{DescribeSpec(bot)}\" at=\"{where}\" region={region} percent={Percent}");
            return bot;
        }
        catch (Exception exception)
        {
            Log.Error($"BUFFBOT_CREATED failed for main \"{main?.Name}\" ({main?.DatabaseID})", exception);
            return null;
        }
    }

    private static string DescribeSpec(GameBot bot) =>
        string.Join(",", bot.GetSpecList().Where(spec => spec.Trainable && spec.Level > 1).Select(spec => $"{spec.KeyName}:{spec.Level}"));

    private static void LogOut(GameBot buffBot, string why)
    {
        States.TryRemove(buffBot.DatabaseID, out State state);
        Log.Info($"BUFFBOT_LOGOUT buffbot=\"{buffBot.Name}\" id={buffBot.DatabaseID} main=\"{state?.MainName}\" reason=\"{why}\"");
        buffBot.Delete();
    }

    /// <summary>A buffbot died: no record, no chat; the release timer takes it to a bind and it goes back to its main.</summary>
    public static void OnDied(GameBot buffBot, GameObject killer)
    {
        if (Log.IsInfoEnabled)
            Log.Info($"BUFFBOT_DIED buffbot=\"{buffBot?.Name}\" main=\"{MainOf(buffBot)?.Name}\" killer=\"{killer?.Name}\" " +
                     $"zone=\"{buffBot?.CurrentZone?.Description}\"");
    }

    #endregion

    #region Live: where to park

    /// <summary>
    /// Where a buffbot parks for its main: the nearest bind of its realm to the main in the main's region (the portal keep in
    /// the frontier, the town in PvE land); for a dungeon, a battleground or a neutral raid region, the main's own bind, and
    /// failing that the realm's capital.
    /// </summary>
    public static (ushort Region, Vector3 Spot, string Where) ParkSpot(GameBot main)
    {
        ushort region = main.CurrentRegionID;
        bool inside = main.CurrentZone?.IsDungeon == true || main.CurrentRegion?.IsDungeon == true ||
                      AutonomousRealmRaid.IsRaidDungeon(region) || BattlegroundBrackets.IsBattlegroundRegion(region) ||
                      RealmRaidNeutralEvents.IsBattleRegion(region);
        if (!inside && BotReleaseBindPoints.Nearest(region, main.X, main.Y, main.Realm) is { } bind)
            return (region, new Vector3(bind.X, bind.Y, bind.Z), "nearest bind");
        OfflineWorldBotRecord record = main.PersistentRecord;
        if (record != null && record.BindRegionId > 0 && WorldMgr.GetRegion((ushort)record.BindRegionId) != null &&
            !BattlegroundBrackets.IsBattlegroundRegion((ushort)record.BindRegionId) &&
            !RealmRaidNeutralEvents.IsBattleRegion((ushort)record.BindRegionId))
            return ((ushort)record.BindRegionId, new Vector3(record.BindX, record.BindY, record.BindZ), "main's bind");
        AutonomousStuckWatchdog.CapitalLocation capital = AutonomousStuckWatchdog.SafeCapitalFor(main.Realm);
        return (capital.RegionId, new Vector3(capital.X, capital.Y, capital.Z), capital.Name ?? "capital");
    }

    #endregion

    #region Live: think

    /// <summary>The buffbot's whole turn (BotBrain.Think calls it first). True for every buffbot: nothing else runs for it.</summary>
    public static bool Think(BotBrain brain)
    {
        GameBot bot = brain?.BotBody;
        if (bot?.IsNoLiferBuffBot != true)
            return false;
        brain.ThinkInterval = ThinkIntervalMilliseconds + (int)(bot.ObjectID % 300);
        try
        {
            ThinkCore(brain, bot);
        }
        catch (Exception exception)
        {
            Log.Error($"BUFFBOT think failed for {bot.Name}", exception);
        }
        return true;
    }

    private static void ThinkCore(BotBrain brain, GameBot bot)
    {
        // It never fights: whatever started an attack (a monster hitting it) is dropped.
        if (bot.attackComponent?.AttackState == true)
            bot.StopAttack();
        // Attacked means hit lately, not "near a fight": its own buffs on a fighting main must not send it running.
        long hitAt = bot.LastAttackedByEnemyTick;
        bool attacked = bot.IsAlive && (hitAt > 0 && GameLoop.GameLoopTime - hitAt < AttackedMemoryMilliseconds || brain.HasAggro);
        brain.ClearAggroList();

        GameBot main = MainOf(bot);
        State state = States.GetOrAdd(bot.DatabaseID, _ => new State { MainId = bot.BuffBotMainId });
        if (main == null)
        {
            if (bot.IsMoving) bot.StopMoving();
            return; // the pulse logs it out
        }
        state.MainName = main.Name;

        bool sameRegion = main.CurrentRegionID == bot.CurrentRegionID;
        double distance = sameRegion ? Distance(bot, main) : double.MaxValue;
        var situation = new Situation(
            bot.IsAlive, attacked, main.IsAlive, sameRegion, distance,
            MainInDungeon: main.CurrentZone?.IsDungeon == true || main.CurrentRegion?.IsDungeon == true ||
                           AutonomousRealmRaid.IsRaidDungeon(main.CurrentRegionID) || RealmRaidNeutralEvents.IsBattleRegion(main.CurrentRegionID),
            MainInFrontier: main.CurrentZone?.IsRvR == true || main.CurrentRegion?.IsRvR == true,
            MainInBattleground: BattlegroundBrackets.IsBattlegroundRegion(main.CurrentRegionID),
            MainInRaid: AutonomousRealmRaid.IsReserved(main) || AutonomousRealmRaid.GetView(main.Group) != null,
            MainOnStableRoute: main.IsOnStableMasterRoute);
        (Stance stance, string reason) = Decide(situation);
        if (stance != state.Stance || reason != state.Reason)
        {
            state.Stance = stance;
            state.Reason = reason;
            LogStance(bot, main, stance, reason, distance);
        }

        switch (stance)
        {
            case Stance.Dead:
                return;
            case Stance.Flee:
            {
                (ushort region, Vector3 spot, _) = ParkSpot(main);
                if (region == bot.CurrentRegionID)
                {
                    if (bot.FollowTarget != null) bot.StopFollowing();
                    if (!bot.IsMoving) bot.PathTo(spot, bot.MaxSpeed);
                }
                return;
            }
            case Stance.Park:
                Park(bot, main);
                break;
            case Stance.Relocate:
                RelocateNear(bot, main);
                return;
            case Stance.Wait:
                if (bot.IsMoving && bot.FollowTarget == null) bot.StopMoving();
                if (bot.FollowTarget != null) bot.StopFollowing();
                break;
            case Stance.Follow:
            {
                int keep = FollowDistanceFor(main.InCombat);
                if (bot.FollowTarget != main)
                    bot.Follow(main, keep, FollowLeash);
                else if (main.InCombat && distance < keep - 50)
                    bot.Follow(main, keep, FollowLeash);
                break;
            }
        }

        if (!bot.IsCasting && MayBuff(stance, main.IsAlive, sameRegion, distance, attacked))
            TryBuff(brain, bot, main, state);
    }

    private static void Park(GameBot bot, GameBot main)
    {
        (ushort region, Vector3 spot, string where) = ParkSpot(main);
        if (region == 0)
            return;
        if (bot.FollowTarget != null)
            bot.StopFollowing();
        if (region != bot.CurrentRegionID ||
            Vector2.Distance(new Vector2(bot.X, bot.Y), new Vector2(spot.X, spot.Y)) > RelocateDistance)
        {
            MoveTo(bot, region, spot, $"parks at {where}");
            return;
        }
        if (Vector2.Distance(new Vector2(bot.X, bot.Y), new Vector2(spot.X, spot.Y)) > ParkArrived)
        {
            if (!bot.IsMoving)
                bot.PathTo(spot, bot.MaxSpeed);
        }
        else if (bot.IsMoving)
            bot.StopMoving();
    }

    private static void RelocateNear(GameBot bot, GameBot main)
    {
        // Behind the main, out of its way, onto the floor.
        Point2D behind = main.GetPointFromHeading((ushort)((main.Heading + 2048) % 4096), FollowDistance + 150);
        var spot = new Vector3(behind.X, behind.Y, main.Z);
        Zone zone = main.CurrentRegion?.GetZone(behind.X, behind.Y);
        var nav = PathfindingProvider.Instance;
        if (zone == null || !nav.IsAvailable || !nav.HasNavmesh(zone) || !nav.TrySnapToMesh(zone, ref spot, 512f))
            spot = new Vector3(main.X, main.Y, main.Z);
        MoveTo(bot, main.CurrentRegionID, spot, $"joins {main.Name}");
    }

    private static void MoveTo(GameBot bot, ushort region, Vector3 spot, string why)
    {
        bot.StopFollowing();
        bot.StopMovingOnPath();
        bot.StopMoving();
        bot.StopCurrentSpellcast();
        ushort fromRegion = bot.CurrentRegionID;
        if (!bot.MoveTo(region, (int)spot.X, (int)spot.Y, (int)spot.Z, bot.Heading))
            return;
        Log.Info($"BUFFBOT_RELOCATED buffbot=\"{bot.Name}\" main=\"{MainOf(bot)?.Name}\" why=\"{why}\" from={fromRegion} to={region} " +
                 $"x={(int)spot.X} y={(int)spot.Y}");
    }

    private static void LogStance(GameBot bot, GameBot main, Stance stance, string reason, double distance)
    {
        string marker = stance switch
        {
            Stance.Follow => "BUFFBOT_FOLLOW",
            Stance.Park => "BUFFBOT_PARKED",
            Stance.Flee => "BUFFBOT_FLEE",
            Stance.Wait => "BUFFBOT_WAIT",
            Stance.Relocate => "BUFFBOT_RELOCATE",
            _ => "BUFFBOT_DOWN",
        };
        string where = stance == Stance.Park ? $" at=\"{ParkSpot(main).Where}\"" : string.Empty;
        Log.Info($"{marker} buffbot=\"{bot.Name}\" main=\"{main.Name}\" reason=\"{reason}\"{where} " +
                 $"mainZone=\"{main.CurrentZone?.Description}\" distance={(distance == double.MaxValue ? "other-region" : ((int)distance).ToString())}");
    }

    private static double Distance(GameObject a, GameObject b) =>
        Math.Sqrt(Math.Pow((double)a.X - b.X, 2) + Math.Pow((double)a.Y - b.Y, 2) + Math.Pow((double)a.Z - b.Z, 2));

    #endregion

    #region Live: buffing

    /// <summary>Whether a class uses a power stat (acuity) and fights with a weapon (haste, damage add, endurance).</summary>
    public static (bool UsesPower, bool UsesWeapons) NeedsOf(GameLiving main)
    {
        ICharacterClass characterClass = (main as GameBot)?.CharacterClass ?? (main as GamePlayer)?.CharacterClass;
        if (characterClass == null)
            return (true, true);
        bool power = characterClass.ManaStat is not eStat.UNDEFINED;
        bool weapons = characterClass.ClassType != eClassType.ListCaster;
        return (power, weapons);
    }

    private static void TryBuff(BotBrain brain, GameBot bot, GameBot main, State state)
    {
        List<Spell> spells = bot.Spells;
        if (spells == null || spells.Count == 0)
            return;
        (bool power, bool weapons) = NeedsOf(main);
        var byId = new Dictionary<int, Spell>();
        var known = new List<BuffCandidate>();
        foreach (Spell spell in spells)
        {
            if (spell == null || byId.ContainsKey(spell.ID))
                continue;
            byId[spell.ID] = spell;
            known.Add(new BuffCandidate(spell.ID, spell.Name, spell.SpellType, spell.Target, spell.Concentration, spell.Level,
                !spell.IsConcentration && spell.Duration > 0));
        }
        // Its own pool: what it has spent on its main is still its to spend again.
        int pool = bot.MaxConcentration;
        // #661: what the target refuses stays out of the plan and the refresh. A main is a gamebot today, which refuses
        // nothing; the check keeps any later target a real player could be honest.
        IReadOnlyCollection<eSpellType> refused = PlayerBuffRefusals.RefuserOf(main) is GamePlayer refuser
            ? PlayerBuffRefusals.Of(refuser) : null;
        IReadOnlyList<BuffCandidate> plan = PlanBuffs(known, power, weapons, pool, refused);
        foreach (BuffCandidate wanted in plan)
        {
            Spell spell = byId[wanted.SpellId];
            if (PlayerBuffRefusals.Refuses(main, spell.SpellType) || brain.LivingHasEffect(main, spell))
                continue;
            if (spell.IsConcentration && bot.Concentration < spell.Concentration)
                continue; // still held by an older rank or a dropped target; the effect service frees it
            if (!brain.CastBuffOn(main, spell))
                return;
            state.BuffsCast++;
            state.FullSetLogged = false;
            Log.Info($"BUFFBOT_BUFFED buffbot=\"{bot.Name}\" main=\"{main.Name}\" spell=\"{spell.Name}\" type={spell.SpellType} " +
                     $"conc={spell.Concentration} timed={(wanted.Timed ? "yes" : "no")} distance={(int)Distance(bot, main)} " +
                     $"zone=\"{main.CurrentZone?.Description}\"");
            return; // one cast per turn
        }
        if (!state.FullSetLogged && state.BuffsCast > 0)
        {
            state.FullSetLogged = true;
            Log.Info($"BUFFBOT_BUFFED_ALL buffbot=\"{bot.Name}\" main=\"{main.Name}\" buffs={plan.Count} " +
                     $"set=\"{string.Join(",", plan.Select(p => p.Name))}\" concUsed={bot.MaxConcentration - bot.Concentration}/{bot.MaxConcentration}");
        }
    }

    #endregion

    #region Live: what it says

    /// <summary>A tell to a buffbot: one short line back (at most one a minute to each player).</summary>
    public static void AnswerTell(GamePlayer player, GameBot buffBot) => Say(player, buffBot, TellLines, "tell");

    /// <summary>An invite a buffbot declined: one short line back. True when the bot is a buffbot (nothing else answers).</summary>
    public static bool AnswerDeclinedInvite(GamePlayer player, GameBot bot)
    {
        if (bot?.IsNoLiferBuffBot != true)
            return false;
        Say(player, bot, InviteLines, "invite");
        Log.Info($"BUFFBOT_INVITE_DECLINED buffbot=\"{bot.Name}\" player=\"{player?.Name}\"");
        return true;
    }

    private static void Say(GamePlayer player, GameBot bot, string[] lines, string kind)
    {
        if (player?.Client == null || bot == null || !bot.IsAlive)
            return;
        long now = GameLoop.GameLoopTime;
        string key = player.Name + "|" + bot.Name + "|" + kind;
        if (LastLineTo.TryGetValue(key, out long at) && now - at < 60_000)
            return;
        LastLineTo[key] = now;
        string text = Line(lines, MainOf(bot)?.Name, Random.Shared.Next());
        // Issue #475: out through the say gate (a pass-through).
        BotSayGate.Say(new BotSayGate.Utterance("tell", bot.Name, bot.Realm, text, player.Name, bot) { Listeners = [player] }, said =>
        {
            ChatUtil.SendSendMessage(player.Client, "Social.ReceiveMessage.Private.Sends", bot.Name, said);
            return said;
        });
    }

    #endregion
}
