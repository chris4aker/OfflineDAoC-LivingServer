namespace OfflineDaoc.Launcher;

/// <summary>
/// The realm cards' "+ Lv.20" button (Living Server 1.2): bots made for the Thidranki battleground bracket (20-24), so that
/// battleground can be filled and played without levelling bots for hours. Owner's PC, build 1dc66b6, 2026-11-15: every gamebot on the test
/// world was level 50 (the cards only made level 1 or 50), so every battleground was empty (BG_KEEP_STATUS near=0/0/0).
/// Pure: the brackets, the level roll, the experience that level has and the purse a bot of that level carries.
/// </summary>
public static class BattlegroundBotLevels
{
    public sealed record Bracket(string Name, ushort RegionId, int MinLevel, int MaxLevel)
    {
        public string Label => $"{Name} {MinLevel}–{MaxLevel}";
    }

    /// <summary>
    /// The server's battleground brackets (DOL.GS.BattlegroundBrackets, patch 1.60 values), in level order. A test reads the
    /// server's source so the two never drift apart.
    /// </summary>
    public static IReadOnlyList<Bracket> Period { get; } =
    [
        new("Abermenai", 253, 15, 19),
        new("Thidranki", 252, 20, 24),
        new("Murdaigean", 251, 25, 29),
        new("Caledonia", 250, 30, 35),
    ];

    /// <summary>Thidranki 20-24, the bracket "+ Lv.20" makes bots for.</summary>
    public const int DefaultIndex = 1;

    public static Bracket Default => Period[DefaultIndex];

    /// <summary>
    /// The bracket the server uses: as the server does (BattlegroundBrackets.Load), a Battleground table row for the region
    /// replaces the period levels. A row the launcher cannot make bots for (outside 1-50, or upside down) is refused, so no
    /// bot is ever made at a level the server would not let into that battleground.
    /// </summary>
    public static Bracket WithDatabaseRow(Bracket period, int? minLevel, int? maxLevel)
    {
        if (minLevel is not int min || maxLevel is not int max)
            return period;
        if (min < 1 || max > MaxLevel || min > max)
            throw new InvalidOperationException(
                $"The world's Battleground table gives {period.Name} levels {min}-{max}. No bots were added.");
        return period with { MinLevel = min, MaxLevel = max };
    }

    /// <summary>A level inside the bracket, every level equally likely (both ends included).</summary>
    public static int RollLevel(Bracket bracket, Random random)
    {
        ArgumentNullException.ThrowIfNull(bracket);
        ArgumentNullException.ThrowIfNull(random);
        if (bracket.MinLevel < 1 || bracket.MaxLevel > MaxLevel || bracket.MinLevel > bracket.MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(bracket));
        return random.Next(bracket.MinLevel, bracket.MaxLevel + 1);
    }

    public const int MaxLevel = 50;

    /// <summary>
    /// The experience a character has on reaching each level: GamePlayer.XPForLevel (index = level - 1), the server's own
    /// table, as level 50's 169,999,999,950 already was. A test reads GamePlayer.cs so the two never drift apart.
    /// </summary>
    private static readonly long[] ExperienceAtLevel =
    [
        0, 50, 250, 850, 2300, 6350, 15950, 37950, 88950, 203950,                                    // 1-10
        459950, 839950, 1399950, 2199950, 3399950, 5199950, 7899950, 11799950, 17499950, 25899950,  // 11-20
        38199950, 54699950, 76999950, 106999950, 146999950, 199999950, 269999950, 359999950,         // 21-28
        479999950, 639999950, 849999950, 1119999950, 1469999950, 1929999950, 2529999950,            // 29-35
        3319999950, 4299999950, 5499999950, 6899999950, 8599999950, 12899999950, 20699999950,       // 36-42
        29999999950, 40799999950, 53999999950, 69599999950, 88499999950, 110999999950,              // 43-48
        137999999950, 169999999950,                                                                  // 49-50
    ];

    /// <summary>Exactly the experience of reaching the level: no progress into it, as a "+ Lv.50" bot has.</summary>
    public static long ExperienceForLevel(int level)
    {
        if (level < 1 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
        return ExperienceAtLevel[level - 1];
    }

    /// <summary>
    /// A modest purse: the level squared in silver (100 copper a silver), 4 gold at 20, 5 gold 76 at 24, 12 gold 25 at 35.
    /// Bots spend coin on stable tickets and vendor upgrades only; this pays for travel and a piece or two from a merchant.
    /// Their first set of plain merchant gear is the server's, free (AutonomousStarterGear; owner 2026-11-15, "Arrive with
    /// starter gear"), so the purse is left as it was.
    /// </summary>
    public static long PurseCopper(int level)
    {
        if (level < 1 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(nameof(level));
        return (long)level * level * 100L;
    }

    /// <summary>The realm cards' button (owner, 2026-11-15: "a Lvl 20 button for bot adds makes sense").</summary>
    public const string ButtonText = "+ Lv.20";

    /// <summary>The "+ Lv.20" buttons' tooltip.</summary>
    public static string ButtonTip(Bracket bracket) =>
        $"Make bots at levels {bracket.MinLevel}–{bracket.MaxLevel}, the {bracket.Name} battleground bracket: each gets a random level " +
        "in it with that level's experience and a small purse (its level squared in silver), and starts in its capital.\n" +
        "At its first login the server gives it plain merchant armour and weapons for its level, free (\"+ Lv.1\" bots still earn theirs).\n" +
        "Shift-click makes 10, Ctrl-click 100.\n" +
        "They go to the battleground only for the share Bot Goals Setting gives Levels 20–49 as Battlegrounds % (0% by default).";
}
