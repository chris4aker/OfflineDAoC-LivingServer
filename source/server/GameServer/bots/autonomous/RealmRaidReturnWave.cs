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
}
