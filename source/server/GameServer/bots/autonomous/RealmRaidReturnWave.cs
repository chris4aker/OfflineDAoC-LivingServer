using System;
using System.Collections.Generic;
using System.Numerics;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Raiders walking back to a raid dungeon go in together (Living Server 1.2, build 2). A raider who dies inside releases
/// to the raid's hub and walks back; alone it meets every monster that has come back on the way to the muster deep
/// inside. Owner's PC, 2026-11-12: after a wipe the muster stayed empty for 20 minutes in six dungeons and the encounter
/// was set aside. Now the raiders outside the dungeon wait at their party's staging by the way in, as before the raid
/// went in, and enter as a wave. A few stragglers walk straight in, and nobody waits long. Dragon raids too (1.2, from
/// True165): a raider who released waits at its party's staging post on the ring and goes back in with a wave.
/// </summary>
public static class RealmRaidReturnWave
{
    [ServerProperty("autonomous", "realm_raid_return_waves",
        "Raid dungeons: raiders walking back after a release gather at the way in and go back in together, instead of one by one through the monsters that came back. False = each walks straight in, as before 1.2.", true)]
    public static bool On = true;

    [ServerProperty("autonomous", "realm_raid_release_at_staging",
        "Raid dungeons: a raider who dies inside releases at its party's staging post outside the dungeon's way in, where the return wave gathers. False = it releases at the raid's muster hub (the town), as before 1.2 build 3.", true)]
    public static bool ReleaseAtStaging = true;

    /// <summary>
    /// Where a raider who died in a raid dungeon releases. Owner's PC, 2026-11-14 (build e3b8238): the return waves gathered
    /// at the staging by the way in, but every release went to the muster hub, so each wipe cost a 20-30 minute walk back,
    /// the raiders reached the way in over that whole time (waves of 7-10 of 96 went in), and about 100 died on the walk
    /// (Valmak alone killed 36 in Faraheim between Hagall and Tuscaran Glacier). Now the release goes to the party's own
    /// staging post outside the way in (it was proven walkable and clear when the raid staged there), when the party has one.
    /// </summary>
    public static bool ReleasesAtStaging(bool on, bool dungeonRaid, bool partyHasStaging) => on && dungeonRaid && partyHasStaging;

    // ---- staging posts in the frontier (Living Server 1.2, after the sweep of 2026-11-14) -------------------------------

    [ServerProperty("autonomous", "realm_raid_frontier_raid_post",
        "Raid dungeons whose way in lies in an RvR/frontier zone: a raider who dies inside releases at ONE raid-wide post (not its party's post), so the released raiders stand and fight together; when enemy players, bots or pets already hold that post, it releases at the raid's hub and comes back with the others. False = per-party posts everywhere, as before.", true)]
    public static bool FrontierRaidPost = true;

    /// <summary>Enemy combatants within this of the raid-wide post hold it (the threat scan radius of
    /// AutonomousThreatAwarePathing).</summary>
    public const int RaidPostHeldRadius = 2_300;

    public enum ReleasePost { Hub, Party, Raid }

    /// <summary>
    /// Where a raider who died in a raid dungeon releases (pure). Owner's PC, 2026-11-14/15 (build 1dc66b6): Albion's
    /// Summoner's Hall raid walks through Hall of the Corrupt, whose way in is in Pennine Mountains; all 59 Pennine deaths
    /// came after the first Aidon fight, raiders released at their party's post in the frontier picked off by enemy-realm
    /// bots, some two or three times in a row. Owner's decision (2026-11-15): a staging post in an RvR/frontier zone sends
    /// every released raider to one raid-wide post, so they stand together; when enemies already hold that post, the
    /// raid's hub, and they come back together. Staging posts outside the frontier keep the party's own post.
    /// </summary>
    public static ReleasePost ChooseReleasePost(bool atStaging, bool dungeonRaid, bool partyHasStaging, bool frontierPostOn,
        bool stagingInFrontier, bool raidHasPost, int enemiesAtRaidPost, out string reason)
    {
        if (!atStaging) { reason = "release_at_staging_off"; return ReleasePost.Hub; }
        if (!dungeonRaid) { reason = "not_a_dungeon_raid"; return ReleasePost.Hub; }
        if (!partyHasStaging) { reason = "party_has_no_staging_post"; return ReleasePost.Hub; }
        if (!stagingInFrontier) { reason = "staging_outside_frontier"; return ReleasePost.Party; }
        if (!frontierPostOn) { reason = "frontier_raid_post_off"; return ReleasePost.Party; }
        if (!raidHasPost) { reason = "no_raid_post"; return ReleasePost.Party; }
        if (enemiesAtRaidPost > 0) { reason = "enemies_hold_raid_post"; return ReleasePost.Hub; }
        reason = "frontier_staging";
        return ReleasePost.Raid;
    }

    /// <summary>The raid-wide post (pure): of the parties' staging posts (each proven walkable when the raid staged), the
    /// one nearest their middle; -1 when there is none.</summary>
    public static int RaidPost(IReadOnlyList<Vector3> posts)
    {
        if (posts == null || posts.Count == 0) return -1;
        var middle = Vector3.Zero;
        foreach (var p in posts) middle += p;
        middle /= posts.Count;
        int best = 0;
        for (int i = 1; i < posts.Count; i++)
            if (Vector3.DistanceSquared(posts[i], middle) < Vector3.DistanceSquared(posts[best], middle))
                best = i;
        return best;
    }

    /// <summary>A raider released at the hub because enemies held the raid-wide post waits this long there at least, so the
    /// raiders falling together (the corpse wait is 90 s) leave together.</summary>
    public const long HubGraceMilliseconds = 90_000;

    /// <summary>
    /// The raiders released at the hub because enemies held the raid-wide post (pure): they wait at the hub and go back
    /// together, never one by one through the frontier. <paramref name="returners"/>: living raiders released there and
    /// not yet gone; <paramref name="atHub"/>: of them, standing at the hub; <paramref name="waitedMilliseconds"/>: since
    /// the first of them stood there. They go once all of them are there (and the grace has passed), once a wave's worth
    /// has waited out the patience, or at the last call. A lone raider waits for company until the last call.
    /// </summary>
    public static Step HubGroup(int returners, int atHub, long waitedMilliseconds)
    {
        if (atHub <= 0) return Step.Gather;
        if (waitedMilliseconds >= LastCallMilliseconds) return Step.Go;
        if (atHub >= 2 && atHub >= returners && waitedMilliseconds >= HubGraceMilliseconds) return Step.Go;
        if (atHub >= MinimumWave && waitedMilliseconds >= PatienceMilliseconds) return Step.Go;
        return Step.Gather;
    }

    /// <summary>A raider's corpse waits this long for a resurrection while raiders stand near it.</summary>
    public const long CorpseWaitMilliseconds = 90_000;
    /// <summary>...and this long when no living raider is in the region within <see cref="RescuerRadius"/> of it (a wipe:
    /// nobody is left to raise it, so waiting only delays the return).</summary>
    public const long CorpseWaitNobodyNearMilliseconds = 20_000;
    public const int RescuerRadius = 3_000;

    public static long CorpseWait(bool raiderNear) => raiderNear ? CorpseWaitMilliseconds : CorpseWaitNobodyNearMilliseconds;

    /// <summary>Fewer than this on their way back walk straight in; a wave is never smaller unless the last call comes.</summary>
    public const int MinimumWave = 8;
    /// <summary>The wave goes when this share of the raiders on their way back (outside or still down inside) stands at the way in.</summary>
    public const double WaveShare = 0.6;
    /// <summary>A party's worth that has waited this long goes without the rest.</summary>
    public const long PatienceMilliseconds = 4 * 60_000;
    /// <summary>Whoever is there after this long goes.</summary>
    public const long LastCallMilliseconds = 8 * 60_000;
    /// <summary>After a wave goes, everyone who arrives within this time follows it in.</summary>
    public const long OpenMilliseconds = 2 * 60_000;

    public enum Step { Open, Gather, Go }

    // ---- dragon raids (from zomboden/OfflineDAoC-True165, AutonomousRealmRaid.Reinforcements.cs, adapted) -----------------

    /// <summary>A dragon raider farther than this from the lair (beyond the staging ring, 3,600-6,100 out) is not with the
    /// raid: it is on its way back.</summary>
    public const float DragonReturnDistance = 6_700;

    public enum DragonRaider { NotYet, WithTheRaid, Returning, Fallen }

    /// <summary>
    /// Where a dragon raider stands for the return wave (pure): <paramref name="alive"/>; <paramref name="beenWithRaid"/>,
    /// seen within <see cref="DragonReturnDistance"/> of the lair since the raid began; <paramref name="returning"/>, already
    /// on its way back; <paramref name="distanceToLair"/>, flat (float.MaxValue out of the dragon's region). A raider down
    /// is Fallen (it will release and walk back) once it has been with the raid; one on its way back stays Returning until
    /// its wave goes; one that never reached the raid is still on its way in (NotYet) and is never held.
    /// </summary>
    public static DragonRaider ClassifyDragonRaider(bool alive, bool beenWithRaid, bool returning, float distanceToLair)
    {
        if (!alive) return beenWithRaid || returning ? DragonRaider.Fallen : DragonRaider.NotYet;
        if (returning) return DragonRaider.Returning;
        if (distanceToLair <= DragonReturnDistance) return DragonRaider.WithTheRaid;
        return beenWithRaid ? DragonRaider.Returning : DragonRaider.NotYet;
    }

    /// <summary>
    /// What the raiders outside the dungeon do now. <paramref name="outside"/>: living raiders not in the dungeon;
    /// <paramref name="fallen"/>: raiders down, who will release and walk back unless resurrected;
    /// <paramref name="waiting"/>: of those outside, at their staging by the way in;
    /// <paramref name="waitedMilliseconds"/>: since the first of them stood there; <paramref name="open"/>: a wave just went.
    /// </summary>
    public static Step Next(int outside, int fallen, int waiting, long waitedMilliseconds, bool open)
    {
        if (open || outside + fallen < MinimumWave) return Step.Open;
        if (waiting >= Math.Max(MinimumWave, (int)Math.Ceiling((outside + fallen) * WaveShare))) return Step.Go;
        if (waiting >= MinimumWave && waitedMilliseconds >= PatienceMilliseconds) return Step.Go;
        if (waiting > 0 && waitedMilliseconds >= LastCallMilliseconds) return Step.Go;
        return Step.Gather;
    }

    // ---- the wave walks in together (owner's decision, 2026-11-17: "Return waves clear together") --------------------------

    /// <summary>
    /// Owner's PC, 2026-11-17 (build 19e7037 and later, server-1117 log): Trollheim wiped at Nosdoden (00:12) and its return
    /// waves went in at the way in (19, 16, 3, 8, 2, 2, 13, 2, 8, 8 raiders), then each raider walked on to the muster deep
    /// inside on its own through trash that had come back since the first hour; 42 minutes later the raid went in again with
    /// 34 of 80, and the 60-minute stall clock ended it at 01:12 with the next muster at 0 of 56. Tur Suil (a raid the game
    /// opened itself) had fewer than 8 on their way back at any time, so they walked straight in one by one; Norsob's muster
    /// reached 13 of 20 while 13 raiders fought respawned balorians on the walk, and the clock ended it at 04:33. Owner's
    /// decision (2026-11-17): a wave walks from the way in to the raid as one group, as a 2003 raid would: it waits for most of
    /// it at each step of the walk, kills what has come back on the cleared route before going on, and is helped when it is
    /// stuck (the raiders waiting at the way in are sent after it, or the main body comes back to meet it).
    /// </summary>
    [ServerProperty("autonomous", "realm_raid_return_wave_walk",
        "Raid dungeons: a return wave walks from the way in to the raid as one group: it waits for most of it at each step, kills the monsters that came back on the cleared walk before going on, and is helped when it is stuck (the raiders waiting at the way in are sent after it, or the main body comes back to meet it). Its headway counts against the stall clock. A few on their way back gather too. False = after the wave goes in, each raider walks to the raid on its own, as before.", true)]
    public static bool WalkTogether = true;

    /// <summary>The walk from the way in to the raid is cut into steps of at most this (path length).</summary>
    public const float StepUnits = 1_200f;
    /// <summary>A raider within this of the wave's step stands at it.</summary>
    public const int AtStepRadius = 500;
    /// <summary>The wave goes on once this share of its living raiders stands at its step...</summary>
    public const double TogetherShare = 0.6;
    /// <summary>...or, after <see cref="StepPatienceMilliseconds"/> at the step, this share (the rest follow).</summary>
    public const double StragglerShare = 0.34;
    public const long StepPatienceMilliseconds = 2 * 60_000;
    /// <summary>A monster in the wave's way is looked for within this of the wave (its middle).</summary>
    public const float LookAheadUnits = 2_500f;
    /// <summary>An aggressive monster this close to the wave, on its floor, is in its way even when off the walk's line.</summary>
    public const float NearUnits = 1_200f;
    /// <summary>Raiders of the wave within this of its monster are called on it.</summary>
    public const int CallRadius = 2_500;
    /// <summary>Never a monster within this of a living named or boss: those fights are the raid's (its route and tactics).</summary>
    public const float EpicClearance = 2_500f;
    /// <summary>A monster the wave fights that takes no damage for this long is left (it can't be reached; fought if it attacks).</summary>
    public const long TargetGiveUpMilliseconds = 3 * 60_000;
    /// <summary>A wave with no headway (a step reached, a kill), no damage on its monster and no raider closer to its step for
    /// this long is stuck.</summary>
    public const long StuckMilliseconds = 4 * 60_000;
    /// <summary>A raider this much closer to the wave's step than before is movement (not stuck; not headway).</summary>
    public const float CloserUnits = 300f;
    /// <summary>A stuck wave is helped at most this often; after that the stall clock decides.</summary>
    public const int MaximumHelps = 2;
    /// <summary>Main-body helpers within this of the wave's step have met it.</summary>
    public const float MeetRadius = 1_500f;
    /// <summary>Helpers that have not met the wave in this long go back to the raid.</summary>
    public const long HelpMilliseconds = 6 * 60_000;
    /// <summary>At most this many of the main body come back to meet a wave (whole parties).</summary>
    public const int MaximumHelpers = 24;
    /// <summary>A few on their way back (fewer than <see cref="MinimumWave"/>) go once all are there and this has passed (the
    /// corpse wait), or after <see cref="SmallPatienceMilliseconds"/> whoever is there.</summary>
    public const long SmallGraceMilliseconds = 90_000;
    public const long SmallPatienceMilliseconds = 3 * 60_000;

    /// <summary>
    /// What the raiders outside a raid dungeon do now when waves walk in together (pure): <see cref="Next"/>, except that a
    /// few on their way back gather too instead of walking straight in one by one (Tur Suil, 2026-11-17). They go once all of
    /// them stand there and <see cref="SmallGraceMilliseconds"/> has passed, or after <see cref="SmallPatienceMilliseconds"/>.
    /// </summary>
    public static Step NextWalking(int outside, int fallen, int waiting, long waitedMilliseconds, bool open)
    {
        if (open) return Step.Open;
        int back = outside + fallen;
        if (back >= MinimumWave) return Next(outside, fallen, waiting, waitedMilliseconds, open);
        if (back <= 0) return Step.Open;
        if (waiting <= 0) return Step.Gather;
        if (waiting >= back && waitedMilliseconds >= SmallGraceMilliseconds) return Step.Go;
        if (waitedMilliseconds >= SmallPatienceMilliseconds) return Step.Go;
        return Step.Gather;
    }

    /// <summary>
    /// The walk a wave takes (pure): <paramref name="walk"/> (from where raiders come in to the front) up to its point nearest
    /// <paramref name="goal"/> (where the raid's parties stand), then the goal; cut into steps at most <paramref name="step"/>
    /// apart along the path. The first step is where raiders come in.
    /// </summary>
    public static Vector3[] WalkSteps(IReadOnlyList<Vector3> walk, Vector3 goal, float step = StepUnits)
    {
        if (walk == null || walk.Count == 0) return [goal];
        // The walk up to its point nearest the goal.
        int bestSegment = 0;
        float bestT = 0, best = float.MaxValue;
        for (int i = 0; i < Math.Max(1, walk.Count - 1); i++)
        {
            Vector3 a = walk[i], b = walk[Math.Min(i + 1, walk.Count - 1)];
            Vector3 ab = b - a;
            float length = ab.LengthSquared();
            float t = length < 0.01f ? 0 : Math.Clamp(Vector3.Dot(goal - a, ab) / length, 0, 1);
            float d = Vector3.DistanceSquared(a + ab * t, goal);
            if (d < best) { best = d; bestSegment = i; bestT = t; }
        }
        var path = new List<Vector3>();
        for (int i = 0; i <= bestSegment; i++) path.Add(walk[i]);
        if (bestSegment + 1 < walk.Count)
        {
            Vector3 cut = Vector3.Lerp(walk[bestSegment], walk[bestSegment + 1], bestT);
            if (Vector3.DistanceSquared(path[^1], cut) > 1f) path.Add(cut);
        }
        if (Vector3.DistanceSquared(path[^1], goal) > 64 * 64) path.Add(goal);
        // Steps every <step> of path length, and the end.
        var steps = new List<Vector3> { path[0] };
        float since = 0;
        for (int i = 1; i < path.Count; i++)
        {
            Vector3 a = path[i - 1], b = path[i];
            float length = Vector3.Distance(a, b);
            float along = step - since;
            while (along <= length)
            {
                steps.Add(Vector3.Lerp(a, b, along / length));
                along += step;
            }
            since = length - (along - step);
        }
        Vector3 end = path[^1];
        if (Vector3.Distance(steps[^1], end) < step * 0.25f && steps.Count > 1) steps[^1] = end;
        else if (Vector3.DistanceSquared(steps[^1], end) > 1f) steps.Add(end);
        return steps.ToArray();
    }

    /// <summary>A raider joining a walking wave within this of the wave's step joins it there; one further back catches up
    /// step by step (<see cref="JoinStep"/>).</summary>
    public const float JoinReach = StepUnits * 1.5f;

    /// <summary>
    /// The step a raider joining a walking wave starts at (pure). Owner's PC, build 453e01a, 2026-11-17, Tuscaran Glacier: 82
    /// raiders went in at the way in at 12:24 and joined a two-raider wave at its step 35 of 49; the walk straight there had no
    /// navigable corridor, so they stood at the way in (atStep=0), failed their routes three times and were moved to the front
    /// by the boss. Now a joiner within <see cref="JoinReach"/> of the wave's step joins it there; one further back starts at
    /// the first step it stands at (<see cref="AtStepRadius"/>), else at the step nearest it, and walks the steps the wave has
    /// cleared one at a time (each at most <see cref="StepUnits"/> on). <paramref name="inside"/> false (still outside, at the
    /// way in): the first step, where raiders come in.
    /// </summary>
    public static int JoinStep(IReadOnlyList<Vector3> steps, Vector3 at, bool inside, int waveStep)
    {
        if (steps == null || steps.Count == 0) return 0;
        waveStep = Math.Clamp(waveStep, 0, steps.Count - 1);
        if (!inside) return 0;
        if (Vector3.Distance(at, steps[waveStep]) <= JoinReach) return waveStep;
        int nearest = 0;
        float best = float.MaxValue;
        for (int i = 0; i <= waveStep; i++)
        {
            float d = Vector3.Distance(at, steps[i]);
            if (d <= AtStepRadius) return i;
            if (d < best) { best = d; nearest = i; }
        }
        return nearest;
    }

    /// <summary>
    /// A raider catching up with its wave (pure): standing at its step or a later one (<paramref name="atStep"/>: the latest
    /// step it stands at, -1 for none), it goes on to the next. Returns its new step, or -1 once that is the wave's step (it
    /// walks with the wave from there).
    /// </summary>
    public static int CatchUp(int step, int atStep, int waveStep)
    {
        int next = atStep >= step ? atStep + 1 : step;
        return next >= waveStep ? -1 : next;
    }

    public enum WaveMove { Wait, Fight, CallHelp, Advance, Arrived }

    /// <summary>Raiders of a wave of <paramref name="living"/> that make it "together" at a step.</summary>
    public static int Together(int living) => Math.Max(1, (int)Math.Ceiling(living * TogetherShare));

    /// <summary>The wave stands together at its step (pure): <see cref="TogetherShare"/> of it, or after
    /// <see cref="StepPatienceMilliseconds"/> there <see cref="StragglerShare"/> of it.</summary>
    public static bool Grouped(int living, int atStep, long stepWaitedMilliseconds) =>
        living > 0 && (atStep >= Together(living) ||
                       stepWaitedMilliseconds >= StepPatienceMilliseconds && atStep >= Math.Max(1, (int)Math.Ceiling(living * StragglerShare)));

    /// <summary>
    /// What a walking wave does now (pure). <paramref name="living"/>: its living raiders; <paramref name="atStep"/>: of them,
    /// standing at its step; <paramref name="stepWaitedMilliseconds"/>: since the first of them reached it;
    /// <paramref name="monsterAhead"/>: a monster stands in its way (<see cref="InTheWay"/>); <paramref name="lastStep"/>: its
    /// step is where the raid stands; <paramref name="bodyCanCome"/>, <paramref name="bodyComing"/>: the main body can come
    /// back to meet it, or is on its way. It waits until it stands together; then it fights what is in its way (a wave
    /// smaller than a party's worth holds and calls the main body instead, when it can come), goes on to its next step, or
    /// has arrived.
    /// </summary>
    public static WaveMove Walk(int living, int atStep, long stepWaitedMilliseconds, bool monsterAhead, bool lastStep,
        bool bodyCanCome, bool bodyComing)
    {
        if (!Grouped(living, atStep, stepWaitedMilliseconds)) return WaveMove.Wait;
        if (monsterAhead) return living < MinimumWave && (bodyCanCome || bodyComing) ? WaveMove.CallHelp : WaveMove.Fight;
        return lastStep ? WaveMove.Arrived : WaveMove.Advance;
    }

    /// <summary>
    /// A monster is in the wave's way (pure): within <see cref="LookAheadUnits"/> of the wave, and on its walk from this step to
    /// the next (or within its aggro range of it: <paramref name="onWalk"/>), or aggressive and within <see cref="NearUnits"/>
    /// of the wave on its floor. Never a named, a boss or a monster by one (<paramref name="byNamed"/>), nor a monster the wave
    /// left because it could not be damaged.
    /// </summary>
    public static bool InTheWay(bool byNamed, bool setAside, bool onWalk, bool aggressive, float distanceToWave, float heightToWave) =>
        !byNamed && !setAside && distanceToWave <= LookAheadUnits &&
        (onWalk || aggressive && distanceToWave <= NearUnits && Math.Abs(heightToWave) <= RealmRaidRouteGate.FloorBand);

    /// <summary>No headway, no damage on its monster and nobody closer to its step for <see cref="StuckMilliseconds"/>.</summary>
    public static bool Stuck(long lastMovement, long now) => now - lastMovement >= StuckMilliseconds;

    /// <summary>The wave's monster took no damage for <see cref="TargetGiveUpMilliseconds"/> while the wave stood at it.</summary>
    public static bool GiveUpTarget(long lastDamage, long now) => now - lastDamage >= TargetGiveUpMilliseconds;

    [Flags]
    public enum Help { None = 0, SendWaiting = 1, BodyMeets = 2 }

    /// <summary>
    /// How a stuck wave is helped (pure): the raiders waiting at the way in go after it now (<paramref name="waitingOutside"/>),
    /// and the main body comes back to meet it when it can (<see cref="BodyCanCome"/>); never more than
    /// <see cref="MaximumHelps"/> times; then nothing, and the stall clock decides.
    /// </summary>
    public static Help ChooseHelp(int waitingOutside, bool bodyCanCome, int helpsSoFar)
    {
        if (helpsSoFar >= MaximumHelps) return Help.None;
        Help help = Help.None;
        if (waitingOutside > 0) help |= Help.SendWaiting;
        if (bodyCanCome) help |= Help.BodyMeets;
        return help;
    }

    /// <summary>The main body can come back to meet a wave (pure): it is not fighting an encounter's attempt, and a party's
    /// worth of it stands idle inside.</summary>
    public static bool BodyCanCome(int idleInside, bool raidFighting) => !raidFighting && idleInside >= MinimumWave;

    /// <summary>How many of the main body come back to meet a wave of <paramref name="waveLiving"/> (whole parties until this).</summary>
    public static int HelpersWanted(int waveLiving) => Math.Clamp(waveLiving, MinimumWave, MaximumHelpers);

    /// <summary>The helpers have met the wave (pure): of <paramref name="helpersInside"/>, a party's worth (or all of them) stand
    /// within <see cref="MeetRadius"/> of it.</summary>
    public static bool Met(int helpersNear, int helpersInside) => helpersInside > 0 && helpersNear >= Math.Min(MinimumWave, helpersInside);

    // ---- the stall clock (owner's decision, 2026-11-17: a wave clearing its way is progress) ------------------------------

    /// <summary>
    /// The raid stalled (pure). <paramref name="limit"/>: the stall limit (60 minutes, a forced raid's 3 hours);
    /// <paramref name="lastProgress"/>: its last real progress (a named or boss down, a new low on its target, a wipe, the
    /// first-hour clear); <paramref name="lastHeadway"/>: a return wave's last headway (a step of its walk reached for the first
    /// time, a monster it killed on the walk, its arrival; 0 for none). Headway restarts the clock, so a wave clearing respawns
    /// on its way does not end the raid; a wave stuck without headway does, <paramref name="limit"/> after its last headway.
    /// Headway never keeps a raid going more than <paramref name="limit"/> beyond its last real progress (twice the limit in all).
    /// </summary>
    public static bool Stalled(long limit, long now, long lastProgress, long lastHeadway) =>
        now - Math.Max(lastProgress, lastHeadway) >= limit || now - lastProgress >= 2 * limit;
}
