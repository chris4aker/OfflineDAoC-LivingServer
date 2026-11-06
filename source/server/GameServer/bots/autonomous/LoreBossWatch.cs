using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Lore in a boss fight (Living Server 1.2, docs/LORE.md): the boss's own yells from its lore file (aggro, phases, death,
/// a player slain) and the fight's atmosphere phase lines, plus a realm's first-ever kill. A boss is watched from the
/// first hit on it (GameLivingEvent.AttackedByEnemy) until it dies or resets, and looked at every 2 s, never per tick.
/// The yells are additive: where the boss's script already speaks on a trigger (<see cref="ScriptSpeaks"/>), the file's
/// lines for that trigger are not used, so nothing is said twice.
/// </summary>
public static class LoreBossWatch
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public const long LookMs = 2_000;
    public const long YellGapMs = 12_000;
    public const long SlayGapMs = 30_000;
    public const ushort AddRadius = 4_000;

    private sealed class Watched
    {
        public LoreBible.Boss Boss;
        public readonly LoreFightRules.Fight Fight = new();
        public long LastYell = long.MinValue / 2;
        public long LastSlay = long.MinValue / 2;
        public long OutOfCombatSince;
    }

    private static readonly ConcurrentDictionary<GameNPC, Watched> Fights = new();
    private static long _nextLook;

    /// <summary>
    /// Boss scripts that already speak on a trigger (scanned from scripts/namedmobs, 2026-11-06): "aggro" covers lines said
    /// when the fight starts or while it runs, "death" on dying, "slay" on killing a player. Lower-case class names.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> ScriptSpeaks = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["agmundr"] = ["aggro", "death"], ["albgolestandt"] = ["aggro", "slay"], ["amphiptere"] = ["aggro"], ["aroon"] = ["aggro"],
        ["beransupplymaster"] = ["aggro"], ["birghir"] = ["aggro"], ["guthlac"] = ["aggro"], ["blight"] = ["aggro"],
        ["cailleachuragaig"] = ["aggro"], ["easmarach"] = ["aggro", "death"], ["fames"] = ["aggro", "death"],
        ["bellum"] = ["aggro", "death"], ["morbus"] = ["aggro", "death"], ["funus"] = ["aggro", "death"],
        ["fornfrusenen"] = ["aggro", "death"], ["gnat"] = ["aggro"], ["grandsummonergovannon"] = ["aggro"], ["hakr"] = ["aggro"],
        ["hibcuuldurach"] = ["aggro", "slay"], ["kvasir"] = ["aggro", "death"], ["legion"] = ["slay"],
        ["lordsanguis"] = ["aggro", "death"], ["lichlordsanguis"] = ["aggro", "death"], ["melancholicfairyqueen"] = ["aggro"],
        ["midgjalpinulva"] = ["aggro", "slay"], ["mokkurvalve"] = ["aggro", "death"], ["morgana"] = ["aggro"],
        ["myrddraxis"] = ["aggro"], ["nosdoden"] = ["slay"], ["olcasargeomancer"] = ["death"], ["olcasgean"] = ["aggro"],
        ["olcasgean2"] = ["aggro"], ["oona"] = ["slay"], ["queenkula"] = ["aggro", "death"], ["kingtuscar"] = ["aggro", "death"],
        ["sisterblythe"] = ["aggro"], ["spiritmasteraros"] = ["aggro", "death"], ["suttung"] = ["aggro"], ["hjalmar"] = ["aggro"],
        ["torst"] = ["aggro"], ["hurika"] = ["aggro"], ["vortanos"] = ["aggro"], ["xanxicar"] = ["aggro"],
    };

    public static bool ScriptSpeaksOn(string classKey, string trigger) =>
        classKey != null && ScriptSpeaks.TryGetValue(classKey, out string[] triggers) && triggers.Contains(trigger, StringComparer.Ordinal);

    #region Adds map

    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> AddsMap = new(LoadAdds);

    /// <summary>Boss class -> add classes (lower case), built into the server (data/encounter_adds.json).</summary>
    public static IReadOnlyDictionary<string, string[]> Adds => AddsMap.Value;

    public static IReadOnlyDictionary<string, string[]> ParseAdds(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (JsonProperty boss in document.RootElement.GetProperty("adds").EnumerateObject())
            map[boss.Name] = boss.Value.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        return map;
    }

    private static IReadOnlyDictionary<string, string[]> LoadAdds()
    {
        try
        {
            Assembly assembly = typeof(LoreBossWatch).Assembly;
            string name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("data.encounter_adds.json", StringComparison.Ordinal));
            if (name == null)
                return new Dictionary<string, string[]>();
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return ParseAdds(reader.ReadToEnd());
        }
        catch (Exception exception)
        {
            Log.Warn("Lore: could not read encounter_adds.json; add triggers stay quiet", exception);
            return new Dictionary<string, string[]>();
        }
    }

    #endregion

    #region Lifecycle and events

    public static void Start()
    {
        GameEventMgr.AddHandler(GameLivingEvent.AttackedByEnemy, new DOLEventHandler(OnAttacked));
        GameEventMgr.AddHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
    }

    public static void Stop()
    {
        GameEventMgr.RemoveHandler(GameLivingEvent.AttackedByEnemy, new DOLEventHandler(OnAttacked));
        GameEventMgr.RemoveHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
        Fights.Clear();
    }

    private static void OnAttacked(DOLEvent e, object sender, EventArgs args)
    {
        if (sender is not GameNPC npc || npc is GameBot || Fights.ContainsKey(npc))
            return;
        LoreBible.Boss boss = BossOf(npc);
        if (boss == null || !Fights.TryAdd(npc, new Watched { Boss = boss }))
            return;
        if (!ScriptSpeaksOn(boss.ClassKey, "aggro") && boss.AggroYells.Length > 0)
            Yell(npc, Fights[npc], boss.AggroYells, GameLoop.GameLoopTime, force: true);
    }

    private static void OnDying(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            GameObject killer = (args as DyingEventArgs)?.Killer;
            if (sender is GameNPC npc and not GameBot)
            {
                LoreBible.Boss boss = BossOf(npc);
                if (boss == null)
                    return;
                Fights.TryRemove(npc, out Watched watched);
                if (!ScriptSpeaksOn(boss.ClassKey, "death") && boss.DeathYells.Length > 0)
                    Yell(npc, watched ?? new Watched { Boss = boss }, boss.DeathYells, GameLoop.GameLoopTime, force: true);
                eRealm realm = LoreOnceRules.KillerRealm(killer);
                if (realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia && !AutonomousLivingServer.IsStaff(killer as GameLiving) &&
                    LoreAtmosphere.ClaimFirstKill(realm, boss.Id, killer?.Name ?? string.Empty))
                {
                    Log.Info($"LORE_FIRST_KILL boss={boss.Id} realm={GlobalConstants.RealmToName(realm)} killer=\"{killer?.Name}\"");
                    LoreAtmosphere.FirstKill(realm, boss);
                }
            }
            else if (sender is GamePlayer && killer is GameNPC bossNpc && Fights.TryGetValue(bossNpc, out Watched watched))
            {
                long now = GameLoop.GameLoopTime;
                if (!ScriptSpeaksOn(watched.Boss.ClassKey, "slay") && watched.Boss.SlayYells.Length > 0 && now - watched.LastSlay >= SlayGapMs)
                {
                    watched.LastSlay = now;
                    Yell(bossNpc, watched, watched.Boss.SlayYells, now, force: true);
                }
            }
        }
        catch (Exception exception)
        {
            Log.Warn("Lore: boss death or slay line failed", exception);
        }
    }

    private static readonly ConcurrentDictionary<Type, LoreBible.Boss> ByType = new();

    /// <summary>The lore boss an NPC is (by its script class), cached per type: the attack event fires for every hit in the world.</summary>
    public static LoreBible.Boss BossOf(GameNPC npc)
    {
        if (npc == null || npc is GameBot)
            return null;
        LoreBible bible = LoreBible.Current;
        return bible.Bosses.Length == 0 ? null : ByType.GetOrAdd(npc.GetType(), type => bible.BossForClass(type.Name));
    }

    private static void Yell(GameNPC npc, Watched watched, string[] lines, long now, bool force = false)
    {
        if (lines.Length == 0 || !force && now - watched.LastYell < YellGapMs)
            return;
        watched.LastYell = now;
        npc.Yell(LoreText.Pick(lines[Random.Shared.Next(lines.Length)], Random.Shared));
    }

    #endregion

    #region Looking at fights

    /// <summary>Every 2 s (from the atmosphere pulse): each watched fight's health and adds, and the lines they make due.</summary>
    public static void Pulse(long now)
    {
        if (now < _nextLook || Fights.IsEmpty)
            return;
        _nextLook = now + LookMs;
        foreach ((GameNPC npc, Watched watched) in Fights.ToArray())
        {
            try
            {
                Look(npc, watched, now);
            }
            catch (Exception exception)
            {
                Fights.TryRemove(npc, out _);
                Log.Warn($"Lore: watching {npc?.Name} failed", exception);
            }
        }
    }

    private static void Look(GameNPC npc, Watched watched, long now)
    {
        if (!npc.IsAlive || npc.ObjectState != GameObject.eObjectState.Active)
        {
            Fights.TryRemove(npc, out _);
            return;
        }
        bool inCombat = npc.InCombat;
        if (inCombat)
            watched.OutOfCombatSince = 0;
        else if (watched.OutOfCombatSince == 0)
            watched.OutOfCombatSince = now;
        if (LoreFightRules.Reset(inCombat, watched.OutOfCombatSince == 0 ? 0 : now - watched.OutOfCombatSince, npc.HealthPercent))
        {
            Fights.TryRemove(npc, out _);
            return;
        }
        LoreBible.Boss boss = watched.Boss;
        if (boss.PhaseYells.Length == 0 && boss.AtmospherePhases.Length == 0)
            return;
        IEnumerable<string> triggers = boss.PhaseYells.Select(p => p.Trigger).Concat(boss.AtmospherePhases.Select(p => p.Trigger));
        int adds = -1;
        if (triggers.Any(t => LoreFightRules.TryParseTrigger(t, out var trigger) && trigger.NeedsAdds) &&
            Adds.TryGetValue(boss.ClassKey, out string[] addClasses))
            adds = npc.GetNPCsInRadius(AddRadius).Count(n => n.IsAlive && n != npc && addClasses.Contains(n.GetType().Name.ToLowerInvariant()));
        foreach (string fired in LoreFightRules.Observe(watched.Fight, triggers, npc.HealthPercent, adds))
        {
            string[] yells = boss.PhaseYells.Where(p => string.Equals(p.Trigger, fired, StringComparison.OrdinalIgnoreCase)).Select(p => p.Text).ToArray();
            Yell(npc, watched, yells, now);
            LoreBible.PhaseLine line = boss.AtmospherePhases.FirstOrDefault(p => string.Equals(p.Trigger, fired, StringComparison.OrdinalIgnoreCase));
            if (line != null && watched.Fight.AtmosphereLines < LoreFightRules.MaxAtmospherePerFight)
            {
                watched.Fight.AtmosphereLines++;
                LoreAtmosphere.Zone(npc.CurrentZone, line.Text, LoreAtmosphere.PhaseTtlMs);
            }
        }
    }

    #endregion
}
