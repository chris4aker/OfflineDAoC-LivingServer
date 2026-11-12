using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.Events;
using DOL.GS.PacketHandler;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Lore in a boss fight (Living Server 1.2, docs/LORE.md): the boss's own yells from its lore file (aggro, phases, death,
/// a player slain) and the fight's atmosphere phase lines, plus a realm's first-ever kill. A boss is watched from the
/// first hit on it (GameLivingEvent.AttackedByEnemy) until it dies or resets, and looked at every 2 s, never per tick.
/// The yells are additive: where the boss's script already speaks on a trigger (<see cref="ScriptSpeaks"/>), the file's
/// lines for that trigger are not used, so nothing is said twice.
/// The mechanic triggers (1.2: throw, teleport, jail, glare, breath, berserk...) come from the boss scripts' own
/// announcements: <see cref="EncounterDisplacement.Displaced"/>, <see cref="EncounterSignals.Raised"/> and
/// <see cref="EncounterSignals.Moment"/>. Each is queued as it happens and said on the next pulse (game loop, about 1 s).
/// </summary>
public static class LoreBossWatch
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public const long LookMs = 2_000;
    public const long YellGapMs = 12_000;
    public const long SlayGapMs = 30_000;
    public const ushort AddRadius = 4_000;
    public const ushort PartnerRadius = 6_000;

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

    /// <summary>A mechanic a script announced, waiting for the pulse: who did it and the triggers it fires, best first.</summary>
    public sealed record PendingMechanic(GameObject Source, string[] Triggers, long At);

    private static readonly ConcurrentQueue<PendingMechanic> Mechanics = new();

    /// <summary>At most this many mechanics wait (a ten-raider throw is ten events); more are dropped.</summary>
    public const int MechanicQueueMax = 256;

    /// <summary>A mechanic not said within this long is dropped (the pulse stalled).</summary>
    public const long MechanicStaleMs = 10_000;

    /// <summary>A mechanic from an add or an object (Scurceol's orbs, the Waterfall Antipass) belongs to the nearest watched fight this close.</summary>
    public const int MechanicOwnerRadius = 6_000;

    /// <summary>Where a mechanic's yell goes (tests replace it).</summary>
    public static Action<GameNPC, string> MechanicYell = (npc, text) => npc.Yell(text);

    /// <summary>Where a mechanic's atmosphere line goes: the boss's zone, each player throttled (tests replace it).</summary>
    public static Action<GameNPC, string> MechanicAtmosphere = (npc, text) => LoreAtmosphere.Zone(npc.CurrentZone, text, LoreAtmosphere.PhaseTtlMs);

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

    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> AddsMap = new(() => LoadAdds("adds"));

    /// <summary>Boss class -> add classes (lower case), built into the server (data/encounter_adds.json).</summary>
    public static IReadOnlyDictionary<string, string[]> Adds => AddsMap.Value;

    public static IReadOnlyDictionary<string, string[]> ParseAdds(string json, string section = "adds")
    {
        using JsonDocument document = JsonDocument.Parse(json);
        var map = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (document.RootElement.TryGetProperty(section, out JsonElement all))
            foreach (JsonProperty boss in all.EnumerateObject())
                map[boss.Name] = boss.Value.EnumerateArray().Select(x => x.GetString()).Where(x => !string.IsNullOrEmpty(x)).ToArray();
        return map;
    }

    private static readonly Lazy<IReadOnlyDictionary<string, string[]>> PartnersMap = new(() => LoadAdds("partners"));

    /// <summary>Boss class -> its partner encounters (lower case): the Horsemen, Lord and Lich Lord Sanguis.</summary>
    public static IReadOnlyDictionary<string, string[]> Partners => PartnersMap.Value;

    private static IReadOnlyDictionary<string, string[]> LoadAdds(string section)
    {
        try
        {
            Assembly assembly = typeof(LoreBossWatch).Assembly;
            string name = assembly.GetManifestResourceNames().SingleOrDefault(n => n.EndsWith("data.encounter_adds.json", StringComparison.Ordinal));
            if (name == null)
                return new Dictionary<string, string[]>();
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return ParseAdds(reader.ReadToEnd(), section);
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
        LoreChannels.LoreFirstKills.FirstKill -= OnFirstKill;
        LoreChannels.LoreFirstKills.FirstKill += OnFirstKill;
        ListenToMechanics();
    }

    /// <summary>Hears the scripts' moves, signals and lore moments (Start; tests).</summary>
    public static void ListenToMechanics()
    {
        StopListeningToMechanics();
        EncounterDisplacement.Displaced += HearDisplaced;
        EncounterSignals.Raised += HearSignal;
        EncounterSignals.Moment += HearMoment;
    }

    public static void StopListeningToMechanics()
    {
        EncounterDisplacement.Displaced -= HearDisplaced;
        EncounterSignals.Raised -= HearSignal;
        EncounterSignals.Moment -= HearMoment;
    }

    /// <summary>A raider was thrown, teleported, jailed, pulled or pushed back (one event per raider).</summary>
    public static void HearDisplaced(DisplacementEvent e)
    {
        if (e?.Source != null)
            Queue(e.Source, LoreFightRules.DisplacementTriggers(e.Kind), e.At);
    }

    /// <summary>A script announced a mechanic for the raid (glare, stun, breath, stomp, messengers, safe spots...).</summary>
    public static void HearSignal(EncounterSignal signal)
    {
        if (signal?.Boss != null)
            Queue(signal.Boss, LoreFightRules.SignalTriggers(signal.Mechanic), signal.At);
    }

    /// <summary>A script said a lore-only moment (berserk, scream, healed, twinHeal).</summary>
    public static void HearMoment(GameNPC boss, string moment)
    {
        if (boss != null)
            Queue(boss, LoreFightRules.SignalTriggers(moment), GameLoop.GameLoopTime);
    }

    private static void Queue(GameObject source, string[] triggers, long at)
    {
        if (triggers.Length == 0 || Mechanics.Count >= MechanicQueueMax)
            return;
        Mechanics.Enqueue(new PendingMechanic(source, triggers, at));
    }

    /// <summary>Tests: the mechanics waiting for the pulse.</summary>
    public static PendingMechanic[] PendingMechanics => Mechanics.ToArray();

    /// <summary>Tests: nothing watched, nothing waiting.</summary>
    public static void ResetForTests()
    {
        Fights.Clear();
        Mechanics.Clear();
        _nextLook = 0;
    }

    /// <summary>
    /// A realm's first kill of a boss (decided once, with its staff and Special Forces rules, and saved in the realm event
    /// ledger by LoreFirstKills): the world-wide atmosphere line. Raid wins from before 1.2 count as first kills there and
    /// raise nothing, so an old win is never announced as new.
    /// </summary>
    private static void OnFirstKill(LoreChannels.LoreFirstKill kill, LoreChannels.LoreBoss channelBoss)
    {
        LoreBible.Boss boss = LoreBible.Current.BossById(kill?.BossId ?? channelBoss?.Id);
        eRealm realm = (eRealm)(kill?.Realm ?? 0);
        if (boss != null && realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia)
            LoreAtmosphere.FirstKill(realm, boss);
    }

    public static void Stop()
    {
        GameEventMgr.RemoveHandler(GameLivingEvent.AttackedByEnemy, new DOLEventHandler(OnAttacked));
        GameEventMgr.RemoveHandler(GameLivingEvent.Dying, new DOLEventHandler(OnDying));
        LoreChannels.LoreFirstKills.FirstKill -= OnFirstKill;
        StopListeningToMechanics();
        Fights.Clear();
        Mechanics.Clear();
    }

    private static void OnAttacked(DOLEvent e, object sender, EventArgs args)
    {
        if (sender is not GameNPC npc || npc is GameBot || Fights.ContainsKey(npc))
            return;
        LoreBible.Boss boss = BossOf(npc);
        if (boss == null || !Fights.TryAdd(npc, new Watched { Boss = boss }))
            return;
        if (!ScriptSpeaksOn(boss.ClassKey, "aggro") && boss.AggroYells.Length > 0)
            Yell(npc, Fights[npc], boss.AggroYells, GameLoop.GameLoopTime, force: true, trigger: "aggro");
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
                // A "death" phase is one more way to die: its yell joins the death yells, its atmosphere line goes to the zone.
                string[] deathYells = boss.DeathYells.Concat(PhaseTexts(boss.PhaseYells, LoreFightRules.Kind.Death)).ToArray();
                if (!ScriptSpeaksOn(boss.ClassKey, "death") && deathYells.Length > 0)
                    Yell(npc, watched ?? new Watched { Boss = boss }, deathYells, GameLoop.GameLoopTime, force: true, trigger: "death");
                foreach (string line in PhaseTexts(boss.AtmospherePhases, LoreFightRules.Kind.Death).Take(1))
                    LoreAtmosphere.Zone(npc.CurrentZone, line, LoreAtmosphere.PhaseTtlMs);
                // A realm's first kill is decided and saved once, by LoreFirstKills (the lore channels); its line is OnFirstKill.
            }
            else if (sender is GamePlayer && killer is GameNPC bossNpc && Fights.TryGetValue(bossNpc, out Watched watched))
            {
                long now = GameLoop.GameLoopTime;
                // A "playerDeath" phase is one more slay yell; its atmosphere line goes to the zone once a fight.
                string[] slay = watched.Boss.SlayYells.Concat(PhaseTexts(watched.Boss.PhaseYells, LoreFightRules.Kind.PlayerDeath)).ToArray();
                if (!ScriptSpeaksOn(watched.Boss.ClassKey, "slay") && slay.Length > 0 && now - watched.LastSlay >= SlayGapMs)
                {
                    watched.LastSlay = now;
                    Yell(bossNpc, watched, slay, now, force: true, trigger: "slay");
                }
                if (watched.Fight.Said.Add("playerDeath") && watched.Fight.AtmosphereLines < LoreFightRules.MaxAtmospherePerFight)
                    foreach (string line in PhaseTexts(watched.Boss.AtmospherePhases, LoreFightRules.Kind.PlayerDeath).Take(1))
                    {
                        watched.Fight.AtmosphereLines++;
                        LoreAtmosphere.Zone(bossNpc.CurrentZone, line, LoreAtmosphere.PhaseTtlMs);
                    }
            }
        }
        catch (Exception exception)
        {
            Log.Warn("Lore: boss death or slay line failed", exception);
        }
    }

    private static readonly ConcurrentDictionary<Type, LoreBible.Boss> ByType = new();

    /// <summary>The texts of the phase lines with this event trigger ("death", "wake").</summary>
    public static IEnumerable<string> PhaseTexts(IEnumerable<LoreBible.PhaseLine> phases, LoreFightRules.Kind kind) =>
        (phases ?? []).Where(p => LoreFightRules.TryParseTrigger(p.Trigger, out var t) && t.Kind == kind && !string.IsNullOrWhiteSpace(p.Text)).Select(p => p.Text);

    /// <summary>A boss's yell for an event trigger ("wake": a dragon takes to the sky), if its file has one.</summary>
    public static void EventYell(GameNPC npc, LoreBible.Boss boss, LoreFightRules.Kind kind)
    {
        string[] lines = PhaseTexts(boss?.PhaseYells, kind).ToArray();
        if (npc == null || lines.Length == 0)
            return;
        Speak(npc, kind.ToString(), LoreText.Pick(lines[Random.Shared.Next(lines.Length)], Random.Shared));
    }

    /// <summary>
    /// A line written as a stage direction, in parentheses: "(The dead wolf rises, stiff-legged, from its post.)". 729 of the
    /// bosses' yell lines are written this way for creatures that do not speak (owner PC soak 2026-11-08: they were yelled
    /// aloud, parentheses and all, 974 times in a night). Pure.
    /// </summary>
    public static bool IsStageDirection(string text)
    {
        string t = text?.Trim();
        return t is { Length: > 2 } && t[0] == '(' && t[^1] == ')';
    }

    /// <summary>A stage direction as the action text players read: no parentheses, a capital first letter and a full stop. Pure.</summary>
    public static string StageText(string text)
    {
        string t = text.Trim()[1..^1].Trim();
        if (t.Length == 0) return t;
        t = char.ToUpperInvariant(t[0]) + t[1..];
        return t[^1] is '.' or '!' or '?' or '\u2026' ? t : t + ".";
    }

    /// <summary>Where a stage direction goes: the players in yell range, as action text (tests replace it).</summary>
    public static Action<GameNPC, string> StageAction = (npc, text) =>
    {
        foreach (GamePlayer player in npc.GetPlayersInRadius(WorldMgr.YELL_DISTANCE))
            player.Out.SendMessage(text, eChatType.CT_Emote, eChatLoc.CL_ChatWindow);
    };

    /// <summary>A boss line: yelled, or shown as action text when it is a stage direction. Logged either way.</summary>
    public static void Speak(GameNPC npc, string trigger, string text, Action<GameNPC, string> yell = null)
    {
        if (npc == null || text == null) return;
        if (IsStageDirection(text))
            StageAction(npc, Logged(npc, "action", trigger, StageText(text)));
        else
            (yell ?? ((n, t) => n.Yell(t)))(npc, Logged(npc, "yell", trigger, text));
    }

    /// <summary>
    /// 1.2 play test: every line the lore engine speaks is in the server log (LORE_LINE boss, kind yell|action|screen, trigger, text),
    /// so a run can be read without a game client.
    /// </summary>
    public static string Logged(GameObject source, string kind, string trigger, string text)
    {
        if (Log.IsInfoEnabled && text != null)
            Log.Info($"LORE_LINE boss=\"{source?.Name}\" kind={kind} trigger={trigger} text=\"{text}\"");
        return text;
    }

    /// <summary>The lore boss an NPC is (by its script class), cached per type: the attack event fires for every hit in the world.</summary>
    public static LoreBible.Boss BossOf(GameNPC npc)
    {
        if (npc == null || npc is GameBot)
            return null;
        LoreBible bible = LoreBible.Current;
        if (bible.Bosses.Length == 0)
            return null;
        // By its script class first (cached per type), then by its exact name (a plain world spawn, npcNames).
        return ByType.GetOrAdd(npc.GetType(), type => bible.BossForClass(type.Name)) ?? bible.BossForName(npc.Name);
    }

    private static void Yell(GameNPC npc, Watched watched, string[] lines, long now, bool force = false, string trigger = "")
    {
        if (lines.Length == 0 || !force && now - watched.LastYell < YellGapMs)
            return;
        watched.LastYell = now;
        Speak(npc, trigger, LoreText.Pick(lines[Random.Shared.Next(lines.Length)], Random.Shared));
    }

    #endregion

    #region Looking at fights

    /// <summary>Every 2 s (from the atmosphere pulse): each watched fight's health and adds, and the lines they make due.</summary>
    public static void Pulse(long now)
    {
        if (!Mechanics.IsEmpty)
            SayMechanics(now);
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
        int partners = -1;
        if (triggers.Any(t => LoreFightRules.TryParseTrigger(t, out var trigger) && trigger.NeedsPartners) &&
            Partners.TryGetValue(boss.ClassKey, out string[] partnerClasses))
            partners = npc.GetNPCsInRadius(PartnerRadius).Count(n => n.IsAlive && n != npc && partnerClasses.Contains(n.GetType().Name.ToLowerInvariant()));
        foreach (string fired in LoreFightRules.Observe(watched.Fight, triggers, npc.HealthPercent, adds, npc.Model, partners))
        {
            string[] yells = boss.PhaseYells.Where(p => string.Equals(p.Trigger, fired, StringComparison.OrdinalIgnoreCase)).Select(p => p.Text).ToArray();
            Yell(npc, watched, yells, now, trigger: fired);
            LoreBible.PhaseLine line = boss.AtmospherePhases.FirstOrDefault(p => string.Equals(p.Trigger, fired, StringComparison.OrdinalIgnoreCase));
            if (line != null && watched.Fight.AtmosphereLines < LoreFightRules.MaxAtmospherePerFight)
            {
                watched.Fight.AtmosphereLines++;
                LoreAtmosphere.Zone(npc.CurrentZone, Logged(npc, "screen", fired, line.Text), LoreAtmosphere.PhaseTtlMs);
            }
        }
    }

    #endregion

    #region Mechanics

    /// <summary>Says every waiting mechanic (from the pulse; tests). Returns what each said, in order.</summary>
    public static List<LoreFightRules.MechanicLines> SayMechanics(long now)
    {
        var said = new List<LoreFightRules.MechanicLines>();
        while (Mechanics.TryDequeue(out PendingMechanic pending))
        {
            try
            {
                if (now - pending.At > MechanicStaleMs)
                    continue;
                (GameNPC npc, LoreBible.Boss boss) = OwnerOf(pending.Source);
                if (npc == null || boss == null)
                    continue;
                if (SayMechanic(npc, boss, pending.Triggers, now) is LoreFightRules.MechanicLines lines)
                    said.Add(lines);
            }
            catch (Exception exception)
            {
                Log.Warn($"Lore: mechanic line for {pending.Source?.Name} failed", exception);
            }
        }
        return said;
    }

    /// <summary>
    /// The lore boss a mechanic belongs to: its source when that is a lore boss, else the nearest watched fight within
    /// <see cref="MechanicOwnerRadius"/> in its region (an add or an object casts it for its boss).
    /// </summary>
    public static (GameNPC Npc, LoreBible.Boss Boss) OwnerOf(GameObject source)
    {
        if (source is GameNPC npc and not GameBot && npc.IsAlive && BossOf(npc) is LoreBible.Boss own)
            return (npc, own);
        if (source == null)
            return (null, null);
        GameNPC nearest = null;
        long best = long.MaxValue;
        foreach ((GameNPC watched, Watched _) in Fights)
        {
            if (watched == source || !watched.IsAlive || watched.CurrentRegionID != source.CurrentRegionID)
                continue;
            long dx = watched.X - source.X, dy = watched.Y - source.Y, d = dx * dx + dy * dy;
            if (d <= (long)MechanicOwnerRadius * MechanicOwnerRadius && d < best)
            {
                best = d;
                nearest = watched;
            }
        }
        return nearest != null && Fights.TryGetValue(nearest, out Watched fight) ? (nearest, fight.Boss) : (null, null);
    }

    /// <summary>
    /// One mechanic of a watched boss (a boss not yet watched starts its fight here): the trigger its file has a line for,
    /// its yell (at most one every <see cref="LoreFightRules.MechanicYellGapMs"/> per trigger, and the fight's 12 s yell gap) and
    /// its atmosphere line to the zone (once a fight per trigger, two a fight, each player's own throttle after that).
    /// </summary>
    public static LoreFightRules.MechanicLines SayMechanic(GameNPC npc, LoreBible.Boss boss, string[] triggers, long now)
    {
        if (npc == null || boss == null || boss.PhaseYells.Length == 0 && boss.AtmospherePhases.Length == 0)
            return null;
        Watched watched = Fights.GetOrAdd(npc, _ => new Watched { Boss = boss });
        LoreFightRules.MechanicLines lines = LoreFightRules.Mechanic(watched.Fight, boss.PhaseYells, boss.AtmospherePhases, triggers, now);
        if (lines == null)
            return null;
        if (lines.Yells.Length > 0 && now - watched.LastYell >= YellGapMs)
        {
            watched.LastYell = now;
            Speak(npc, string.Join('|', triggers), LoreText.Pick(lines.Yells[Random.Shared.Next(lines.Yells.Length)], Random.Shared), MechanicYell);
        }
        if (lines.Atmosphere != null)
            MechanicAtmosphere(npc, Logged(npc, "screen", string.Join('|', triggers), LoreText.Pick(lines.Atmosphere, Random.Shared)));
        return lines;
    }

    #endregion
}
