namespace DOL.GS;

/// <summary>
/// Realism 3a (#485, movement tell 12): how long a dead autonomous gamebot with nobody to rez it lies before it releases.
/// It was a fixed 20 s on a 3 s timer, so a wiped party's corpses all vanished in the same beat. Now each bot takes its own
/// 15 to 45 s, from its id and its death count (the same death always gives the same wait, the next death another). The
/// 90 s wait beside a rezzer, the siege wait and a party's 60 s wait for its own healer are unchanged. Pure.
/// </summary>
public static class BotReleaseTiming
{
    public const int MinMs = 15_000;
    public const int MaxMs = 45_000;

    /// <summary>The wait before this bot releases from this death, 15 to 45 s.</summary>
    public static int WipeReleaseMs(long botId, int deathCount)
    {
        ulong z = unchecked((ulong)botId * 0x9E3779B97F4A7C15UL + (ulong)(uint)deathCount * 0xBF58476D1CE4E5B9UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        z ^= z >> 31;
        return MinMs + (int)(z % (ulong)(MaxMs - MinMs + 1));
    }
}
