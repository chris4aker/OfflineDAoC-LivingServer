using System;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Raiders walking back to a raid dungeon go in together (Living Server 1.2, build 2). A raider who dies inside releases
/// to the raid's hub and walks back; alone it meets every monster that has come back on the way to the muster deep
/// inside. Owner's PC, 2026-11-12: after a wipe the muster stayed empty for 20 minutes in six dungeons and the encounter
/// was set aside. Now the raiders outside the dungeon wait at their party's staging by the way in, as before the raid
/// went in, and enter as a wave. A few stragglers walk straight in, and nobody waits long.
/// </summary>
public static class RealmRaidReturnWave
{
    [ServerProperty("autonomous", "realm_raid_return_waves",
        "Raid dungeons: raiders walking back after a release gather at the way in and go back in together, instead of one by one through the monsters that came back. False = each walks straight in, as before 1.2.", true)]
    public static bool On = true;

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
