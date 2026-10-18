using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using DOL.Database;
using DOL.Database.Attributes;
using DOL.Events;
using DOL.GS.ServerProperties;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>Scenes each realm has heard, so none repeats until every fitting scene has played.</summary>
[DataTable(TableName = "offline_bot_scenes_heard")]
public sealed class OfflineBotSceneHeardRecord : DataObject
{
    [PrimaryKey] public string HeardKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string SceneId { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string HeardUtc { get; set; } = string.Empty;
}

/// <summary>
/// Debates and long threads (docs/bot-voices/DESIGN.md 3.5, part of the Social Expansion): the
/// corpus's scenes play in /faction, cast from online bots of the realm by personality and class,
/// one scene at a time per realm, 30-45 minutes apart, only while a real player of the realm is
/// online, cast from bots on a town break first. Lines arrive at typing pace. A speaker who is
/// fighting or casting is waited for (up to 45 s); one who stays busy, dies or logs off is replaced
/// by another bot that fits, or the line is skipped. The thread ends early only when fewer than two
/// of its speakers are still in the world.
/// </summary>
public static class AutonomousBotDebates
{
    public const int MinimumGapMs = 30 * 60_000;
    public const int MaximumGapMs = 45 * 60_000;
    public const int FirstSceneDelayMs = 5 * 60_000;
    public const int LevelSlack = 5;

    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool Enabled => AutonomousBotVoices.Enabled && Properties.BOT_VOICES_DEBATES;

    #region Pure rules (tested)

    /// <summary>A bot that could speak. <paramref name="InTown"/>: on a town break, so least likely to be pulled into a fight.</summary>
    public readonly record struct Candidate(long Id, BotPersonalityType Type, string ClassName, int Level, bool InTown = false);

    /// <summary>Where a cast speaker is when its line is due.</summary>
    public enum SpeakerState { Free, Busy, Gone }

    /// <summary>What a beat does: say it, wait for a busy speaker, skip it, or end the thread.</summary>
    public enum BeatDecision { Speak, Wait, Skip, End }

    /// <summary>How often a beat whose speaker is fighting or casting is retried.</summary>
    public const int BusyRetryMs = 5_000;

    /// <summary>
    /// How long a thread waits for a busy speaker before a stand-in takes the line. A fight lasts a few
    /// seconds and a thread a minute or two; ending at the first busy moment cut threads among levelling
    /// bots short (in-game check 2026-10-17: camp_etiquette ended after 24 s, "fewer than two speakers left").
    /// </summary>
    public const int MaxBusyWaitMs = 45_000;

    /// <summary>
    /// The rule for one beat. A free cast speaker speaks. A busy one is waited for (up to
    /// <see cref="MaxBusyWaitMs"/>). After that, or when the speaker died or logged off, a stand-in
    /// speaks; without one the beat is skipped while two speakers are still around (busy counts as
    /// around), and the thread ends only when fewer than two are.
    /// </summary>
    public static BeatDecision DecideBeat(SpeakerState castSpeaker, long waitedMs, bool standInAvailable, int speakersAround)
    {
        if (castSpeaker == SpeakerState.Free)
            return BeatDecision.Speak;
        if (castSpeaker == SpeakerState.Busy && waitedMs < MaxBusyWaitMs)
            return BeatDecision.Wait;
        if (standInAvailable)
            return BeatDecision.Speak;
        return speakersAround >= 2 ? BeatDecision.Skip : BeatDecision.End;
    }

    /// <summary>Typing pace: 3 seconds plus about 60 ms a character, within 3-10 seconds.</summary>
    public static int BeatDelayMs(string text) => Math.Clamp(3_000 + (text?.Length ?? 0) * 60, 3_000, 10_000);

    public static int NextGapMs(Random random) => MinimumGapMs + random.Next(MaximumGapMs - MinimumGapMs + 1);

    public static bool LevelFits(VC.Scene scene, int level) => level >= scene.LevelLow - LevelSlack && level <= scene.LevelHigh + LevelSlack;

    public static bool Fits(VC.RoleSpec spec, Candidate candidate) =>
        spec == null || VC.TypeFits(spec.Types, candidate.Type) && VC.ClassFits(spec.Classes, candidate.ClassName);

    /// <summary>
    /// Casts a scene's speaking roles from distinct candidates. A role's class requirement is kept;
    /// its personality wish is preferred, then relaxed. Null when fewer than two people would speak
    /// or a class-bound role can't be filled.
    /// </summary>
    public static Dictionary<string, long> Cast(VC.Scene scene, IReadOnlyList<Candidate> candidates, Random random)
    {
        var cast = new Dictionary<string, long>(StringComparer.Ordinal);
        var used = new HashSet<long>();
        // Bots on a town break first: a thread cast from grinding bots stalls at their next pull.
        Candidate[] pool = candidates.Where(c => LevelFits(scene, c.Level)).OrderByDescending(c => c.InTown)
            .ThenBy(_ => random.Next()).ToArray();
        // Roles with requirements first, so the open roles don't take the only fitting bot.
        foreach (string role in scene.SpeakingRoles.OrderByDescending(r => scene.Roles.TryGetValue(r, out var s) ?
                     s.Classes.Length * 2 + s.Types.Length : 0))
        {
            scene.Roles.TryGetValue(role, out VC.RoleSpec spec);
            Candidate chosen = pool.FirstOrDefault(c => !used.Contains(c.Id) && Fits(spec, c));
            if (chosen.Id == 0 && spec != null && spec.Types.Length > 0)
                chosen = pool.FirstOrDefault(c => !used.Contains(c.Id) && VC.ClassFits(spec.Classes, c.ClassName));
            if (chosen.Id == 0)
                return null;
            cast[role] = chosen.Id;
            used.Add(chosen.Id);
        }
        return cast.Count >= 2 ? cast : null;
    }

    /// <summary>
    /// True when every required beat has a variant its speaker can fill. A scene that opens with
    /// "pc on {item}" doesn't start when that speaker carries nothing, so the replies never hang.
    /// </summary>
    public static bool Playable(VC.Scene scene, Func<string, IReadOnlyDictionary<string, string>> slotsForRole)
    {
        foreach (VC.Beat beat in scene.Beats)
        {
            if (beat.IsPause || beat.Optional || beat.Variants.Length == 0)
                continue;
            IReadOnlyDictionary<string, string> slots = slotsForRole(beat.Role);
            if (!beat.Variants.Any(variant => VC.TryFill(variant, slots, out _)))
                return false;
        }
        return true;
    }

    /// <summary>A beat's variants that fill with these slots, filled; empty when none do.</summary>
    public static string[] FilledVariants(VC.Beat beat, IReadOnlyDictionary<string, string> slots) =>
        beat.Variants.Select(variant => VC.TryFill(variant, slots, out string filled) ? filled : null)
            .Where(filled => filled != null).ToArray();

    /// <summary>
    /// The next scene for a realm: one that fits the realm and the online levels and hasn't been heard;
    /// when every fitting scene has been heard, the realm starts a fresh round (resetRound = true).
    /// </summary>
    public static VC.Scene ChooseScene(IReadOnlyList<VC.Scene> scenes, eRealm realm, ISet<string> heard,
        IReadOnlyCollection<int> onlineLevels, Random random, out bool resetRound, string timeFlag = null)
    {
        resetRound = false;
        // A scene about bedtime plays at night only (0.36-se.4: scenes can need a time of day).
        IReadOnlySet<string> now = timeFlag == null ? null : new HashSet<string>(StringComparer.Ordinal) { timeFlag };
        VC.Scene[] fitting = scenes.Where(scene => VC.RealmFits(scene.Realm, realm) &&
            (now == null || VC.NeedsMet(scene.Needs, now)) &&
            onlineLevels.Count(level => LevelFits(scene, level)) >= 2).ToArray();
        if (fitting.Length == 0)
            return null;
        VC.Scene[] fresh = fitting.Where(scene => !heard.Contains(scene.Id)).ToArray();
        if (fresh.Length == 0)
        {
            resetRound = true;
            fresh = fitting;
        }
        return fresh[random.Next(fresh.Length)];
    }

    #endregion

    #region Runtime

    private sealed class Running
    {
        public VC.Scene Scene;
        public Dictionary<string, long> Cast;
        public Dictionary<string, string> Shared;
        public int Beat;
        public long NextAt;
        /// <summary>When the current beat started waiting for a busy speaker; 0 when not waiting.</summary>
        public long WaitingSince;
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<eRealm, Running> Scenes = new();
    private static readonly Dictionary<eRealm, long> NextStartAt = new();
    private static readonly ConcurrentDictionary<eRealm, HashSet<string>> Heard = new();
    private static Timer _timer;
    private static int _posted;

    public static bool IsRunning(eRealm realm)
    {
        // Switched off mid-scene, the scene's timer stops: it must not hold /faction for good (audit 2026-10-16).
        if (!Enabled)
            return false;
        lock (Sync)
            return Scenes.ContainsKey(realm);
    }

    [GameServerStartedEvent]
    public static void OnServerStarted(DOLEvent e, object sender, EventArgs args)
    {
        try
        {
            foreach (OfflineBotSceneHeardRecord row in GameServer.Database.SelectAllObjects<OfflineBotSceneHeardRecord>())
                Heard.GetOrAdd((eRealm)row.Realm, _ => new HashSet<string>(StringComparer.Ordinal)).Add(row.SceneId);
        }
        catch (Exception exception)
        {
            Log.Error("Bot debates: could not load heard scenes", exception);
        }
        _timer = new Timer(_ =>
        {
            FlushHeard();
            // One tick at a time on the game loop; skip when the previous one hasn't run yet.
            if (!Enabled || Interlocked.Exchange(ref _posted, 1) != 0)
                return;
            NpcService.Instance.Post(TickOnGameLoop, 0);
        }, null, 10_000, 1_000);
    }

    [GameServerStoppedEvent]
    public static void OnServerStopped(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = null;
        FlushHeard(wait: true);
    }

    private static void TickOnGameLoop(int unused)
    {
        try
        {
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                TickRealm(realm, GameLoop.GameLoopTime);
        }
        catch (Exception exception)
        {
            Log.Error("Bot debates: tick failed", exception);
        }
        finally
        {
            Volatile.Write(ref _posted, 0);
        }
    }

    private static bool HasAudience(eRealm realm) => ClientService.Instance.GetPlayersOfRealm(realm).Count > 0;

    private static bool Eligible(GameBot bot, eRealm realm) => bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
        bot.Realm == realm && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && !bot.InCombat && !bot.IsCasting &&
        bot.DatabaseID > 0;

    private static Candidate CandidateOf(GameBot bot) =>
        new(bot.DatabaseID, AutonomousBotPersonality.For(bot).Type, bot.CharacterClass?.Name ?? bot.ClassName ?? string.Empty, bot.Level,
            bot.PersistentRecord?.ObjectivePhase == AutonomousWorldBotController.TownIdlePhase);

    private static SpeakerState StateOf(GameBot bot, eRealm realm)
    {
        if (bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper || bot.Realm != realm || !bot.IsAlive ||
            bot.ObjectState != GameObject.eObjectState.Active || bot.DatabaseID <= 0)
            return SpeakerState.Gone;
        return bot.InCombat || bot.IsCasting ? SpeakerState.Busy : SpeakerState.Free;
    }

    private static SpeakerState CastState(eRealm realm, Running running, string role) =>
        running.Cast.TryGetValue(role, out long id) && AutonomousBotRegistry.TryGet(id, out GameBot bot)
            ? StateOf(bot, realm) : SpeakerState.Gone;

    private static void TickRealm(eRealm realm, long now)
    {
        Running running;
        lock (Sync)
        {
            Scenes.TryGetValue(realm, out running);
            if (running == null)
            {
                if (!NextStartAt.TryGetValue(realm, out long start))
                    NextStartAt[realm] = start = now + FirstSceneDelayMs + Random.Shared.Next(5 * 60_000);
                if (now < start)
                    return;
            }
            else if (now < running.NextAt)
                return;
        }
        if (!HasAudience(realm))
        {
            if (running != null)
                End(realm, "nobody of the realm is online");
            return;
        }
        if (running == null)
            TryStart(realm, now);
        else
            PlayBeat(realm, running, now);
    }

    private static void TryStart(eRealm realm, long now)
    {
        GameBot[] online = AutonomousBotRegistry.Snapshot().Where(bot => Eligible(bot, realm)).ToArray();
        HashSet<string> heard = Heard.GetOrAdd(realm, _ => new HashSet<string>(StringComparer.Ordinal));
        VC.Scene scene;
        bool reset;
        lock (heard)
            scene = ChooseScene(AutonomousBotVoices.Corpus.Scenes, realm, heard, online.Select(bot => (int)bot.Level).ToArray(), Random.Shared, out reset,
                VC.TimeFlag(DateTime.Now.Hour));
        Dictionary<string, long> cast = scene == null ? null : Cast(scene, online.Select(CandidateOf).ToArray(), Random.Shared);
        Dictionary<string, string> shared = null;
        if (cast != null)
        {
            GameBot opener = online.First(bot => bot.DatabaseID == cast.Values.First());
            shared = AutonomousBotVoices.SharedSlots(realm, opener.Level, Random.Shared);
            var slotsByRole = cast.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyDictionary<string, string>)AutonomousBotVoices.SlotsFor(online.First(bot => bot.DatabaseID == pair.Value), Random.Shared, shared),
                StringComparer.Ordinal);
            if (!Playable(scene, role => slotsByRole.GetValueOrDefault(role)))
                cast = null;
        }
        lock (Sync)
        {
            if (cast == null)
            {
                NextStartAt[realm] = now + 5 * 60_000; // try again in a few minutes
                return;
            }
            if (reset)
                ResetHeard(realm, heard);
            Scenes[realm] = new Running { Scene = scene, Cast = cast, Beat = 0, NextAt = now, Shared = shared };
        }
        MarkHeard(realm, scene.Id, heard);
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_DEBATE_START realm={realm} scene={scene.Id} speakers={cast.Count}");
    }

    private static void PlayBeat(eRealm realm, Running running, long now)
    {
        while (running.Beat < running.Scene.Beats.Length)
        {
            VC.Beat beat = running.Scene.Beats[running.Beat++];
            if (beat.IsPause)
            {
                running.NextAt = now + Math.Max(1, beat.PauseSeconds) * 1_000L;
                return;
            }
            // An optional beat is rolled once, not again on every retry while its speaker is busy.
            if (running.WaitingSince == 0 && beat.Optional && Random.Shared.NextDouble() < 0.5 || beat.Variants.Length == 0)
                continue;
            SpeakerState state = CastState(realm, running, beat.Role);
            long waited = running.WaitingSince == 0 ? 0 : now - running.WaitingSince;
            GameBot speaker = state == SpeakerState.Free || state == SpeakerState.Busy && waited >= MaxBusyWaitMs || state == SpeakerState.Gone
                ? SpeakerFor(realm, running, beat.Role) : null;
            switch (DecideBeat(state, waited, speaker != null, SpeakersAround(realm, running)))
            {
                case BeatDecision.Wait:
                    if (running.WaitingSince == 0)
                    {
                        running.WaitingSince = now;
                        if (Log.IsInfoEnabled)
                            Log.Info($"BOT_DEBATE_WAIT realm={realm} scene={running.Scene.Id} role={beat.Role} beat={running.Beat}");
                    }
                    running.Beat--; // retry this beat
                    running.NextAt = now + BusyRetryMs;
                    return;
                case BeatDecision.End:
                    End(realm, "fewer than two speakers left");
                    return;
                case BeatDecision.Skip:
                    running.WaitingSince = 0;
                    continue;
            }
            running.WaitingSince = 0;
            if (speaker == null)
                continue;
            string[] filledVariants = FilledVariants(beat, AutonomousBotVoices.SlotsFor(speaker, Random.Shared, running.Shared));
            if (filledVariants.Length == 0)
                continue;
            string filled = VC.ForTimeOfDay(filledVariants[Random.Shared.Next(filledVariants.Length)], DateTime.Now.Hour);
            string line = VC.Style(filled, AutonomousBotVoices.Corpus.VoiceFor(speaker.DatabaseID), Random.Shared);
            AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, line);
            running.NextAt = now + BeatDelayMs(NextText(running));
            return;
        }
        End(realm, "finished");
    }

    private static string NextText(Running running)
    {
        for (int index = running.Beat; index < running.Scene.Beats.Length; index++)
        {
            VC.Beat beat = running.Scene.Beats[index];
            if (!beat.IsPause && beat.Variants.Length > 0)
                return beat.Variants[0];
        }
        return string.Empty;
    }

    /// <summary>The bot for a role: the cast one if it can talk, else a fitting replacement, else null (skip).</summary>
    private static GameBot SpeakerFor(eRealm realm, Running running, string role)
    {
        if (running.Cast.TryGetValue(role, out long id) && AutonomousBotRegistry.TryGet(id, out GameBot cast) && Eligible(cast, realm))
            return cast;
        running.Scene.Roles.TryGetValue(role, out VC.RoleSpec spec);
        var taken = running.Cast.Values.ToHashSet();
        GameBot replacement = AutonomousBotRegistry.Snapshot()
            .Where(bot => Eligible(bot, realm) && !taken.Contains(bot.DatabaseID) && LevelFits(running.Scene, bot.Level) &&
                          Fits(spec, CandidateOf(bot)))
            .OrderBy(_ => Random.Shared.Next())
            .FirstOrDefault();
        if (replacement != null)
            running.Cast[role] = replacement.DatabaseID;
        return replacement;
    }

    /// <summary>Cast members still in the world and of the realm; one fighting or casting still counts.</summary>
    private static int SpeakersAround(eRealm realm, Running running) =>
        running.Cast.Values.Count(id => AutonomousBotRegistry.TryGet(id, out GameBot bot) && StateOf(bot, realm) != SpeakerState.Gone);

    private static void End(eRealm realm, string reason)
    {
        lock (Sync)
        {
            if (Scenes.Remove(realm, out Running running) && Log.IsInfoEnabled)
                Log.Info($"BOT_DEBATE_END realm={realm} scene={running.Scene.Id} reason=\"{reason}\"");
            NextStartAt[realm] = GameLoop.GameLoopTime + NextGapMs(Random.Shared);
        }
    }

    private static void MarkHeard(eRealm realm, string sceneId, HashSet<string> heard)
    {
        lock (heard)
        {
            if (!heard.Add(sceneId))
                return;
        }
        HeardWrites.Add(new OfflineBotSceneHeardRecord
        {
            HeardKey = $"{(int)realm}:{sceneId}", Realm = (int)realm, SceneId = sceneId, HeardUtc = DateTime.UtcNow.ToString("O"),
        });
    }

    private static void ResetHeard(eRealm realm, HashSet<string> heard)
    {
        lock (heard)
            heard.Clear();
        HeardWrites.Reset((int)realm);
    }

    #endregion

    #region Heard-scene writes (off the game loop, under the shared write lock; audit 2026-10-17)

    /// <summary>
    /// Heard-scene changes waiting to be written. Marking and resetting happen on the game loop; the rows were
    /// written there too, outside the shared database write lock, and a reset read the whole table and deleted
    /// its rows one by one inside a tick. Now the debates timer writes them: per realm reset, one select and one
    /// batched delete, then one batched insert, all under <see cref="AutonomousBotStatusPersistence.DatabaseWriteLock"/>.
    /// </summary>
    public sealed class HeardWriteQueue
    {
        private readonly object _sync = new();
        private readonly List<OfflineBotSceneHeardRecord> _adds = new();
        private readonly HashSet<int> _resets = new();

        public void Add(OfflineBotSceneHeardRecord row)
        {
            lock (_sync)
                _adds.Add(row);
        }

        /// <summary>A reset drops the realm's rows not yet written (the delete would remove them anyway).</summary>
        public void Reset(int realm)
        {
            lock (_sync)
            {
                _adds.RemoveAll(row => row.Realm == realm);
                _resets.Add(realm);
            }
        }

        /// <summary>Takes what is queued: the realms to clear first, then the rows to insert (all queued after those resets).</summary>
        public (int[] Resets, OfflineBotSceneHeardRecord[] Adds) Take()
        {
            lock (_sync)
            {
                var taken = (_resets.OrderBy(realm => realm).ToArray(), _adds.ToArray());
                _resets.Clear();
                _adds.Clear();
                return taken;
            }
        }
    }

    private static readonly HeardWriteQueue HeardWrites = new();
    private static int _flushingHeard;

    /// <summary>Writes queued heard-scene changes (debates timer thread; at shutdown with <paramref name="wait"/>).</summary>
    public static void FlushHeard(bool wait = false)
    {
        while (Interlocked.Exchange(ref _flushingHeard, 1) != 0)
        {
            if (!wait)
                return;
            Thread.Sleep(20);
        }
        try
        {
            (int[] resets, OfflineBotSceneHeardRecord[] adds) = HeardWrites.Take();
            if (resets.Length == 0 && adds.Length == 0)
                return;
            lock (AutonomousBotStatusPersistence.DatabaseWriteLock)
            {
                foreach (int realm in resets)
                {
                    try
                    {
                        var rows = DOLDB<OfflineBotSceneHeardRecord>.SelectObjects(DB.Column("Realm").IsEqualTo(realm));
                        if (rows.Count > 0)
                            GameServer.Database.DeleteObject(rows.Cast<DataObject>());
                    }
                    catch (Exception exception)
                    {
                        Log.Warn($"Bot debates: could not reset heard scenes for {(eRealm)realm}", exception);
                    }
                }
                if (adds.Length > 0)
                {
                    try
                    {
                        GameServer.Database.AddObject(adds.Cast<DataObject>());
                    }
                    catch (Exception exception)
                    {
                        Log.Warn($"Bot debates: could not save {adds.Length} heard scene(s)", exception);
                    }
                }
            }
        }
        finally
        {
            Volatile.Write(ref _flushingHeard, 0);
        }
    }

    #endregion
}
