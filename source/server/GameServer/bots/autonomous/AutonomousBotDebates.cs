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
/// Debates and long threads (docs/bot-voices/DESIGN.md 3.5, part of the Living Server): the
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
    /// <summary>A scene holds its next line this long after a player speaks in /faction, so the replies to the player come
    /// first and a scene line isn't read as one (live test 2026-10-18: "lfg 5 scout" was followed by the scene's "me too").</summary>
    public const int PlayerPauseMs = 10_000;

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
    /// <summary>How long a storyline episode waits for a busy protagonist.</summary>
    public const int StoryBusyWaitMs = 180_000;

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

    public static long AfterPlayerSpoke(long nextAt, long now) => Math.Max(nextAt, now + PlayerPauseMs);

    /// <summary>A player of the realm spoke in /faction: the running scene, if any, waits a moment.</summary>
    public static void PlayerSpoke(eRealm realm) => PlayerSpoke(realm, null);

    /// <summary>The same, remembering who spoke: a dunce who rage quits sends that player a parting tell.</summary>
    public static void PlayerSpoke(eRealm realm, GamePlayer player)
    {
        lock (Sync)
        {
            if (Scenes.TryGetValue(realm, out Running running))
            {
                running.NextAt = AfterPlayerSpoke(running.NextAt, GameLoop.GameLoopTime);
                if (player != null)
                    running.Players.Add(player.Name);
            }
        }
    }

    public static int NextGapMs(Random random) => MinimumGapMs + random.Next(MaximumGapMs - MinimumGapMs + 1);

    public static bool LevelFits(VC.Scene scene, int level) => level >= scene.LevelLow - LevelSlack && level <= scene.LevelHigh + LevelSlack;

    public static bool Fits(VC.RoleSpec spec, Candidate candidate) =>
        spec == null || VC.TypeFits(spec.Types, candidate.Type) && VC.ClassFits(spec.Classes, candidate.ClassName) && spec.LevelFits(candidate.Level);

    /// <summary>
    /// Casts a scene's speaking roles from distinct candidates. A role's class requirement is kept;
    /// its personality wish is preferred, then relaxed. Null when fewer than two people would speak
    /// or a class-bound role can't be filled.
    /// </summary>
    public static Dictionary<string, long> Cast(VC.Scene scene, IReadOnlyList<Candidate> candidates, Random random) =>
        Cast(scene, candidates, random, null);

    /// <summary>
    /// The same, with some roles already cast (a storyline's two protagonists): those roles are taken as given and only the
    /// others are filled from the candidates.
    /// </summary>
    public static Dictionary<string, long> Cast(VC.Scene scene, IReadOnlyList<Candidate> candidates, Random random,
        IReadOnlyDictionary<string, long> fixedRoles)
    {
        var cast = new Dictionary<string, long>(StringComparer.Ordinal);
        if (fixedRoles != null)
            foreach (var pair in fixedRoles)
                cast[pair.Key] = pair.Value;
        var used = new HashSet<long>(cast.Values);
        // Bots on a town break first: a thread cast from grinding bots stalls at their next pull.
        Candidate[] pool = candidates.Where(c => LevelFits(scene, c.Level)).OrderByDescending(c => c.InTown)
            .ThenBy(_ => random.Next()).ToArray();
        // Roles with requirements first, so the open roles don't take the only fitting bot.
        foreach (string role in scene.SpeakingRoles.Where(r => !cast.ContainsKey(r)).OrderByDescending(r => scene.Roles.TryGetValue(r, out var s) ?
                     s.Classes.Length * 2 + s.Types.Length + (s.LevelLow > 1 || s.LevelHigh < 50 ? 4 : 0) : 0))
        {
            scene.Roles.TryGetValue(role, out VC.RoleSpec spec);
            Candidate chosen = pool.FirstOrDefault(c => !used.Contains(c.Id) && Fits(spec, c));
            if (chosen.Id == 0 && spec != null && spec.Types.Length > 0)
                chosen = pool.FirstOrDefault(c => !used.Contains(c.Id) && VC.ClassFits(spec.Classes, c.ClassName) && spec.LevelFits(c.Level));
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
        // Storyline episodes are played only by AutonomousBotStorylines, in order.
        VC.Scene[] fitting = scenes.Where(scene => !AutonomousBotStorylines.IsStory(scene) && VC.RealmFits(scene.Realm, realm) &&
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
        /// <summary>The storyline this scene is an episode of (AutonomousBotStorylines), or null for an ordinary debate.</summary>
        public string StoryKey;
        /// <summary>A storyline's two protagonists (roles A and B): never replaced by a stand-in.</summary>
        public long StoryA, StoryB;
        public int Beat;
        public long NextAt;
        /// <summary>When the current beat started waiting for a busy speaker; 0 when not waiting.</summary>
        public long WaitingSince;
        /// <summary>When the realm's last player left the game (or went through a zone change or release); 0 while one is in.</summary>
        public long NoAudienceSince;
        /// <summary>The last line said: by whom (role and bot), when, and every line so far (for what a player's line is about).</summary>
        public string LastRole;
        public long LastSpeakerId;
        public long LastAt;
        public readonly List<(string Role, string Text)> Said = new();
        /// <summary>Players answered in this scene, so one player gets at most two in-debate answers.</summary>
        public int PlayerAnswers;
        /// <summary>Players who spoke in /faction while the scene ran.</summary>
        public readonly HashSet<string> Players = new(StringComparer.OrdinalIgnoreCase);
    }

    private static readonly object Sync = new();
    private static readonly Dictionary<eRealm, Running> Scenes = new();
    private static readonly Dictionary<eRealm, long> NextStartAt = new();
    /// <summary>/lstest: the topic the next scene in a realm must have (null: any), used once.</summary>
    private static readonly Dictionary<eRealm, string> ForcedTopic = new();

    /// <summary>A new play session (AutonomousLivingWorld): the session's first scene comes 5-10 minutes in.</summary>
    public static void OnSessionStart(eRealm realm, long now)
    {
        lock (Sync)
            if (!Scenes.ContainsKey(realm))
                NextStartAt[realm] = now + FirstSceneDelayMs + Random.Shared.Next(5 * 60_000);
    }

    /// <summary>
    /// GM test (/lstest): the realm's next /faction scene starts on the next tick, of the given topic when one is named
    /// ("dunce"), or the due storyline episode when <paramref name="topic"/> is null and one is due. False with the reason
    /// when a scene is already running.
    /// </summary>
    public static bool StartSoon(eRealm realm, string topic, out string why)
    {
        lock (Sync)
        {
            if (Scenes.ContainsKey(realm))
            {
                why = "a /faction scene is already running in that realm; wait for it to end";
                return false;
            }
            NextStartAt[realm] = 0;
            if (topic == null)
                ForcedTopic.Remove(realm);
            else
                ForcedTopic[realm] = topic;
        }
        why = null;
        return true;
    }
    private static readonly ConcurrentDictionary<eRealm, HashSet<string>> Heard = new();
    private static Timer _timer;
    private static int _posted;

    /// <summary>The storyline episode running in a realm's /faction, with its two protagonists, or null.</summary>
    public static (string StoryKey, long BotA, long BotB)? RunningStory(eRealm realm)
    {
        lock (Sync)
            return Scenes.TryGetValue(realm, out Running running) && running.StoryKey != null
                ? (running.StoryKey, running.StoryA, running.StoryB) : null;
    }

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

    /// <summary>How long a running scene waits for the realm's players to be back in the game before it ends.</summary>
    public const long NoAudienceGraceMs = 2 * 60_000;

    private static bool HasAudience(eRealm realm) => ClientService.Instance.GetPlayersOfRealm(realm).Count > 0;

    private static bool Eligible(GameBot bot, eRealm realm) => bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
        bot.Realm == realm && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && !bot.InCombat && !bot.IsCasting &&
        bot.DatabaseID > 0 && !AutonomousBeggars.Is(bot);   // beggars talk only on their corner

    /// <summary>In the game and alive, even if fighting or casting (a storyline protagonist).</summary>
    private static bool Present(GameBot bot, eRealm realm) => bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
        bot.Realm == realm && bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active && bot.DatabaseID > 0 &&
        !AutonomousBeggars.Is(bot);

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

    /// <summary>The role's bot is in the game but dead (a corpse waiting to release or be raised).</summary>
    private static bool DeadInWorld(eRealm realm, Running running, string role) =>
        running.Cast.TryGetValue(role, out long id) && AutonomousBotRegistry.TryGet(id, out GameBot bot) &&
        bot.IsAutonomousWorldBot && bot.Realm == realm && !bot.IsAlive && bot.ObjectState == GameObject.eObjectState.Active;

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
            // A player releasing, porting or changing zones is briefly out of the game: the scene waits for them, and
            // only ends when nobody of the realm has been in the game for NoAudienceGraceMs.
            if (running != null)
            {
                if (running.NoAudienceSince == 0)
                    running.NoAudienceSince = now;
                else if (now - running.NoAudienceSince >= NoAudienceGraceMs)
                    End(realm, "nobody of the realm is online");
            }
            return;
        }
        if (running != null && running.NoAudienceSince != 0)
        {
            // Back: carry on where it paused, without firing the beats that came due meanwhile all at once.
            running.NextAt = Math.Max(running.NextAt, now + 2_000);
            running.NoAudienceSince = 0;
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
        GameBot[] speakers = online;   // online, plus a storyline's protagonists
        HashSet<string> heard = Heard.GetOrAdd(realm, _ => new HashSet<string>(StringComparer.Ordinal));
        VC.Scene scene;
        bool reset = false;
        Dictionary<string, long> cast;
        // A storyline episode that is due comes first: its two protagonists are fixed, the rest are cast as usual.
        AutonomousBotStorylines.Next story = null;
        try
        {
            // A running arc's pair need only be in the game: the scene waits for a protagonist who is fighting.
            GameBot[] present = AutonomousBotRegistry.Snapshot().Where(bot => Present(bot, realm)).ToArray();
            story = AutonomousBotStorylines.TryNext(realm, online, DateTime.UtcNow, Random.Shared, present);
            if (story != null)
                speakers = online.Concat(present.Where(bot => bot.DatabaseID == story.BotA || bot.DatabaseID == story.BotB))
                    .DistinctBy(bot => bot.DatabaseID).ToArray();
        }
        catch (Exception exception)
        {
            Log.Warn("Storylines: could not choose an episode", exception);
        }
        scene = null;
        cast = null;
        if (story != null)
        {
            scene = story.Scene;
            cast = Cast(scene, online.Where(bot => bot.DatabaseID != story.BotA && bot.DatabaseID != story.BotB).Select(CandidateOf).ToArray(),
                Random.Shared, new Dictionary<string, long> { ["A"] = story.BotA, ["B"] = story.BotB });
            if (cast == null)
                story = null;   // not enough others to fill the scene: an ordinary debate instead, the episode waits
        }
        string forcedTopic;
        lock (Sync)
        {
            ForcedTopic.TryGetValue(realm, out forcedTopic);
            ForcedTopic.Remove(realm);
        }
        if (forcedTopic != null)
            story = null;   // /lstest asked for a topic: that, not the storyline
        if (story == null)
        {
            IReadOnlyList<VC.Scene> pool = forcedTopic == null ? AutonomousBotVoices.Corpus.Scenes
                : AutonomousBotVoices.Corpus.Scenes.Where(s => string.Equals(s.Topic, forcedTopic, StringComparison.OrdinalIgnoreCase)).ToArray();
            lock (heard)
                scene = ChooseScene(pool, realm, heard, online.Select(bot => (int)bot.Level).ToArray(), Random.Shared, out reset,
                    forcedTopic == null ? VC.TimeFlag(DateTime.Now.Hour) : null);
            cast = scene == null ? null : Cast(scene, online.Select(CandidateOf).ToArray(), Random.Shared);
        }
        Dictionary<string, string> shared = null;
        if (cast != null)
        {
            GameBot opener = speakers.First(bot => bot.DatabaseID == cast.Values.First());
            shared = AutonomousBotVoices.SharedSlots(realm, opener.Level, Random.Shared);
            if (story != null)
                foreach (var pair in story.Slots)
                    shared[pair.Key] = pair.Value;
            var slotsByRole = cast.ToDictionary(pair => pair.Key,
                pair => (IReadOnlyDictionary<string, string>)AutonomousBotVoices.SlotsFor(speakers.First(bot => bot.DatabaseID == pair.Value), Random.Shared, shared),
                StringComparer.Ordinal);
            if (!Playable(scene, role => slotsByRole.GetValueOrDefault(role)))
            {
                cast = null;
                // An episode that can't be said now waits half an hour (so it never blocks the ordinary debates).
                if (story != null)
                    AutonomousBotStorylines.OnEpisodeEnded(realm, story.StoryKey, scene.Id, false, DateTime.UtcNow);
            }
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
            Scenes[realm] = new Running { Scene = scene, Cast = cast, Beat = 0, NextAt = now, Shared = shared, StoryKey = story?.StoryKey,
                StoryA = story?.BotA ?? 0, StoryB = story?.BotB ?? 0 };
        }
        if (story == null)
            MarkHeard(realm, scene.Id, heard);
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_DEBATE_START realm={GlobalConstants.RealmToName(realm)} scene={scene.Id} speakers={cast.Count}");
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
            // A protagonist who died is waited for like a busy one: it releases or is raised in well under three minutes
            // (in-game check 2026-10-28: a romance episode ended "a protagonist left" when one of the pair died mid-scene).
            if (running.StoryKey != null && beat.Role is "A" or "B" && state == SpeakerState.Gone && DeadInWorld(realm, running, beat.Role))
                state = SpeakerState.Busy;
            long waited = running.WaitingSince == 0 ? 0 : now - running.WaitingSince;
            // A storyline's protagonist is never replaced: a busy one is waited for (up to three minutes), and one who
            // left or stays busy ends the episode unfinished, so it plays again later with the same pair.
            if (running.StoryKey != null && beat.Role is "A" or "B" && state != SpeakerState.Free)
            {
                if (state == SpeakerState.Busy && waited < StoryBusyWaitMs)
                {
                    if (running.WaitingSince == 0)
                        running.WaitingSince = now;
                    running.Beat--;
                    running.NextAt = now + BusyRetryMs;
                    return;
                }
                End(realm, "a protagonist left");
                return;
            }
            GameBot speaker = state == SpeakerState.Free || state == SpeakerState.Busy && waited >= MaxBusyWaitMs || state == SpeakerState.Gone
                ? SpeakerFor(realm, running, beat.Role) : null;
            switch (DecideBeat(state, waited, speaker != null, SpeakersAround(realm, running)))
            {
                case BeatDecision.Wait:
                    if (running.WaitingSince == 0)
                    {
                        running.WaitingSince = now;
                        if (Log.IsInfoEnabled)
                            Log.Info($"BOT_DEBATE_WAIT realm={GlobalConstants.RealmToName(realm)} scene={running.Scene.Id} role={beat.Role} beat={running.Beat}");
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
            // In the realm's thread too, so "why?" or "lol" after a debate line goes to the bot that said it.
            AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), speaker, line, "chat");
            lock (Sync)
            {
                running.LastRole = beat.Role;
                running.LastSpeakerId = speaker.DatabaseID;
                running.LastAt = now;
                running.Said.Add((beat.Role, line));
            }
            running.NextAt = now + BeatDelayMs(NextText(running));
            return;
        }
        // The dunce who rage quits ("@exit A") logs off once the pile-on is over.
        if (!string.IsNullOrEmpty(running.Scene.Exit) && running.Cast.TryGetValue(running.Scene.Exit, out long quitter) &&
            AutonomousBotRegistry.TryGet(quitter, out GameBot dunce))
        {
            // A player who joined in (or laughed) gets the dunce's parting tell before it logs off.
            GamePlayer witness = null;
            lock (Sync)
                foreach (string name in running.Players)
                {
                    witness = ClientService.Instance.GetPlayerByExactName(name);
                    if (witness != null && witness.Realm == realm)
                        break;
                    witness = null;
                }
            if (witness != null && AutonomousBotVoices.TryEventLine(dunce, "session", out string parting, "ragequit_tell", noRepeat: false))
                AutonomousLivingServer.Tell(dunce, witness, parting, "none");
            AutonomousBotSchedule.RageQuit(dunce, Random.Shared);
        }
        End(realm, "finished");
    }

    #region A player joins in

    /// <summary>How long after a debate line a player's line is still read as part of the debate.</summary>
    public const int JoinWindowMs = 75_000;

    private static readonly System.Text.RegularExpressions.Regex Agrees = new(
        @"^(yeah|yea|ya|yep|yup|agreed|agree|true|so true|exactly|same|same here|thats right|that s right|correct|for real|totally|preach|truth)\b");

    private static readonly System.Text.RegularExpressions.Regex Disagrees = new(
        @"^(nah|no|nope|no way|disagree|i disagree|hard disagree|wrong|not really|not true|thats not true|that s not true|lol no|meh|eh no|naw|dunno about that|not sure about that|thats wrong)\b");

    private static readonly HashSet<string> Filler = new(StringComparer.Ordinal)
    {
        "the", "and", "that", "this", "with", "they", "them", "have", "just", "what", "your", "youre", "about", "really", "there", "their",
        "dont", "cant", "its", "for", "you", "but", "not", "was", "are", "all", "out", "get", "got", "one", "too", "can", "lol", "yeah",
        "like", "know", "think", "even", "much", "more", "some", "been", "when", "then", "than", "into", "from", "only", "still",
        "anyone", "else", "people", "feels", "actually", "make", "makes", "want", "would", "could", "should", "everyone", "thing", "things",
    };

    /// <summary>
    /// What a player's line does in a running debate: "agree" with the last speaker ("yeah exactly", "+1"), "disagree" ("nah,
    /// necros are fine"), "opinion" (three or more words that share one with the debate so far: "spiritmasters are fine in
    /// groups imo"), or null (not about the debate). A question is left to the usual answers. Pure.
    /// </summary>
    public static string StanceOf(string message, IEnumerable<string> debateLines)
    {
        string t = AutonomousChatQuestions.Normalize(message);
        if (message.TrimEnd().EndsWith('?') || AutonomousChatQuestions.IsQuestion(message))
            return null;
        // A request or a hello is not a side taken: "anyone want to group for vendo", "hi all".
        if (AutonomousChatIntentModel.Predict(message) is eAutonomousChatIntent.Grouping or eAutonomousChatIntent.Trading or
            eAutonomousChatIntent.Rez or eAutonomousChatIntent.Alarm or eAutonomousChatIntent.Greeting or eAutonomousChatIntent.Farewell or
            eAutonomousChatIntent.Thanks or eAutonomousChatIntent.Away or eAutonomousChatIntent.Back or eAutonomousChatIntent.Abuse)
            return null;
        if (Disagrees.IsMatch(t) && !System.Text.RegularExpressions.Regex.IsMatch(t, @"^no (idea|clue|problem|worries)\b"))
            return "disagree";
        string raw = message.Trim();
        if (Agrees.IsMatch(t) || t is "this" or "right" or "this right here" || raw.StartsWith("+1", StringComparison.Ordinal) || raw.StartsWith('^'))
            return "agree";
        string[] words = t.Split(' ');
        if (words.Length < 3)
            return null;
        // "buffbots" and "buffbot" are one word here.
        static string Stem(string word) => word.Length > 4 && word.EndsWith('s') ? word[..^1] : word;
        var debate = new HashSet<string>(debateLines.SelectMany(line => AutonomousChatQuestions.Normalize(line).Split(' '))
            .Where(word => word.Length >= 4 && !Filler.Contains(word)).Select(Stem), StringComparer.Ordinal);
        return words.Any(word => word.Length >= 4 && !Filler.Contains(word) && debate.Contains(Stem(word))) ||
               System.Text.RegularExpressions.Regex.IsMatch(t, @"\b(imo|imho|i think|i reckon|personally|honestly|in my experience)\b")
            ? "opinion"
            : null;
    }

    /// <summary>
    /// A player's /faction line during a running debate, answered in it (the talking update): agreeing with the last speaker
    /// draws its thanks ("see, Wynn gets it"), disagreeing its pushback ("fair, but hear me out") and now and then a bot on
    /// the player's side; an opinion on the topic a nod from one of the debaters. The debate then goes on. At most two such
    /// answers per scene. False when no debate is running, the line isn't about it, or nobody can answer.
    /// </summary>
    public static bool TryAnswerPlayer(GamePlayer player, string message)
    {
        if (!Enabled || !AutonomousBotVoices.Enabled || player == null || string.IsNullOrWhiteSpace(message))
            return false;
        eRealm realm = player.Realm;
        long now = GameLoop.GameLoopTime;
        string stance;
        GameBot last, other;
        lock (Sync)
        {
            if (!Scenes.TryGetValue(realm, out Running running) || running.LastAt == 0 || now - running.LastAt > JoinWindowMs ||
                running.PlayerAnswers >= 2)
                return false;
            stance = StanceOf(message, running.Said.Select(said => said.Text));
            if (stance == null)
                return false;
            last = AutonomousBotRegistry.TryGet(running.LastSpeakerId, out GameBot lastBot) && Eligible(lastBot, realm) ? lastBot : null;
            // The other side: a cast member who spoke under another role.
            string lastRole = running.LastRole;
            long otherId = running.Said.Where(said => said.Role != lastRole).Select(said => running.Cast.GetValueOrDefault(said.Role)).LastOrDefault();
            other = otherId > 0 && AutonomousBotRegistry.TryGet(otherId, out GameBot otherBot) && Eligible(otherBot, realm) ? otherBot : null;
            if (last == null && other == null)
                return false;
            running.PlayerAnswers++;
            // The debate waits for the answers below.
            running.NextAt = Math.Max(running.NextAt, now + 14_000);
        }
        var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["player"] = player.Name };
        var replies = new List<(GameBot Bot, string Answers)>();
        switch (stance)
        {
            case "agree":
                replies.Add((last ?? other, last != null ? "debate_agreed" : "debate_opinion"));
                if (last != null && other != null && Random.Shared.NextDouble() < 0.4)
                    replies.Add((other, "debate_outnumbered"));
                break;
            case "disagree":
                replies.Add((last ?? other, last != null ? "debate_disagreed" : "debate_backed"));
                if (last != null && other != null && Random.Shared.NextDouble() < 0.5)
                    replies.Add((other, "debate_backed"));
                break;
            default:
                replies.Add((other ?? last, "debate_opinion"));
                break;
        }
        int due = 0;
        foreach ((GameBot bot, string answers) in replies)
        {
            if (!AutonomousBotVoices.TryLine(bot, "reply", out VC.Picked picked, answers: answers, slots: slots, relax: true))
                continue;
            due = AutonomousBotChatCoordinator.ReplyDelayMs(picked.Text, due, Random.Shared);
            string text = picked.Text;
            GameBot speaker = bot;
            AutonomousLivingServer.Later(speaker, due, () => Eligible(speaker, realm) && AutonomousBotChatCoordinator.HasFactionAudience(realm), () =>
            {
                AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, text);
                AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), speaker, text, "chat", player.Name);
            });
        }
        return due > 0;
    }

    #endregion

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
        if (running.StoryKey != null && role is "A" or "B")
            return null;   // a storyline's protagonist has no stand-in
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
            if (Scenes.Remove(realm, out Running running))
            {
                if (Log.IsInfoEnabled)
                    Log.Info($"BOT_DEBATE_END realm={GlobalConstants.RealmToName(realm)} scene={running.Scene.Id} reason=\"{reason}\"");
                if (running.StoryKey != null)
                    AutonomousBotStorylines.OnEpisodeEnded(realm, running.StoryKey, running.Scene.Id, reason == "finished", DateTime.UtcNow);
            }
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
