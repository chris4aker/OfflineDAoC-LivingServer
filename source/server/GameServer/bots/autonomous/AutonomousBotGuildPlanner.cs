using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>What a bot guild is for. See docs/BOT-GUILDS.md.</summary>
public enum BotGuildCharter
{
    Leveling = 0,
    Social = 1,
    Rvr = 2,
    Raiding = 3,
    Crafting = 4,
}

/// <summary>
/// Pure planning rules for autonomous bot guilds: how many guilds a realm has, how big they are,
/// their charters and names, where unplaced bots go and who holds rank. No server state.
/// </summary>
public static class AutonomousBotGuildPlanner
{
    /// <summary>Classic guild founding size (the base server's guild_num default).</summary>
    public const int FoundingMembers = 8;
    public const int SlotsPerGuild = 22;
    public const int MaximumGuildsPerRealm = 60;
    public const int MaximumOfficers = 3;
    public const int MembersPerOfficer = 12;
    public const int MaximumNameLength = 30;
    public const int LowestRank = 9;
    private const double SizeExponent = 0.8;

    // By size order: the biggest guild of a realm is an RvR guild, then a leveling guild, and so on.
    private static readonly BotGuildCharter[] CharterPattern =
    [
        BotGuildCharter.Rvr, BotGuildCharter.Leveling, BotGuildCharter.Social, BotGuildCharter.Raiding,
        BotGuildCharter.Leveling, BotGuildCharter.Crafting, BotGuildCharter.Social, BotGuildCharter.Rvr,
        BotGuildCharter.Leveling, BotGuildCharter.Social, BotGuildCharter.Raiding, BotGuildCharter.Leveling,
    ];

    public static readonly Regex NameFormat = new("^[A-Z][A-Za-z' ]{2,29}$", RegexOptions.CultureInvariant);

    public readonly record struct Candidate(long BotId, int Level);

    public sealed class GuildSlot
    {
        public GuildSlot(string guildId, BotGuildCharter charter, int ordinal, int target, int count)
        {
            GuildId = guildId;
            Charter = charter;
            Ordinal = ordinal;
            Target = target;
            Count = count;
        }

        public string GuildId { get; }
        public BotGuildCharter Charter { get; }
        public int Ordinal { get; }
        public int Target { get; }
        public int Count { get; set; }
    }

    public readonly record struct Member(long BotId, int Level, int Rank);

    public readonly record struct Placement(long BotId, string GuildId);

    /// <summary>How many of a realm's eligible bots belong in bot guilds.</summary>
    public static int MemberTarget(int eligibleBots, double share)
    {
        if (eligibleBots <= 0 || double.IsNaN(share))
            return 0;
        share = Math.Clamp(share, 0.0, 1.0);
        return (int)Math.Round(eligibleBots * share, MidpointRounding.AwayFromZero);
    }

    /// <summary>Guild count for a member target: none below 8 members, about one per 22, never a guild under 8.</summary>
    public static int DesiredGuildCount(int memberTarget)
    {
        if (memberTarget < FoundingMembers)
            return 0;
        int byShare = (int)Math.Round(memberTarget / (double)SlotsPerGuild, MidpointRounding.AwayFromZero);
        int bySize = memberTarget / FoundingMembers;
        return Math.Clamp(byShare, 1, Math.Min(MaximumGuildsPerRealm, bySize));
    }

    /// <summary>
    /// Target sizes, largest first: weights 1/(i+1)^0.8 with largest-remainder rounding, then every
    /// guild is lifted to 8 members (taken from the largest) when the total allows it.
    /// </summary>
    public static int[] TargetSizes(int guildCount, int memberTarget)
    {
        if (guildCount <= 0 || memberTarget <= 0)
            return [];

        double[] weights = Enumerable.Range(0, guildCount).Select(i => 1.0 / Math.Pow(i + 1, SizeExponent)).ToArray();
        double unit = memberTarget / weights.Sum();
        int[] sizes = weights.Select(weight => (int)Math.Floor(weight * unit)).ToArray();
        int remainder = memberTarget - sizes.Sum();
        foreach (int index in Enumerable.Range(0, guildCount)
                     .OrderByDescending(i => weights[i] * unit - sizes[i])
                     .ThenBy(i => i)
                     .Take(remainder))
            sizes[index]++;

        if (memberTarget >= guildCount * FoundingMembers)
        {
            for (int index = 0; index < guildCount; index++)
            {
                while (sizes[index] < FoundingMembers)
                {
                    int donor = Enumerable.Range(0, guildCount).Where(i => sizes[i] > FoundingMembers)
                        .OrderByDescending(i => sizes[i]).ThenBy(i => i).First();
                    sizes[donor]--;
                    sizes[index]++;
                }
            }
        }

        return sizes;
    }

    public static BotGuildCharter CharterFor(int ordinal) => CharterPattern[Math.Abs(ordinal) % CharterPattern.Length];

    /// <summary>How well a bot of this level suits a charter (0..1).</summary>
    public static double Fit(BotGuildCharter charter, int level) => charter switch
    {
        BotGuildCharter.Leveling => level <= 40 ? 1.0 : 0.3,
        BotGuildCharter.Rvr => level >= 35 ? 1.0 : level >= 20 ? 0.4 : 0.1,
        BotGuildCharter.Raiding => level >= 40 ? 1.0 : level >= 30 ? 0.3 : 0.05,
        BotGuildCharter.Social => 0.8,
        BotGuildCharter.Crafting => 0.6,
        _ => 0.5,
    };

    /// <summary>A fixed, well-mixed order of bots (SplitMix64 of the id), so placement doesn't follow creation order.</summary>
    public static ulong Order(long botId)
    {
        ulong z = unchecked((ulong)botId + 0x9E3779B97F4A7C15UL);
        z = unchecked((z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL);
        z = unchecked((z ^ (z >> 27)) * 0x94D049BB133111EBUL);
        return z ^ (z >> 31);
    }

    /// <summary>
    /// Places up to <paramref name="toPlace"/> unplaced bots. Each goes to the guild with the largest
    /// remaining share of its target, weighted by charter fit; full guilds take nobody. Existing
    /// members are never moved. The slots' counts are updated.
    /// </summary>
    public static List<Placement> Place(IEnumerable<Candidate> unplaced, IReadOnlyList<GuildSlot> guilds, int toPlace)
    {
        var placements = new List<Placement>();
        if (toPlace <= 0 || guilds == null || guilds.Count == 0)
            return placements;

        foreach (Candidate candidate in unplaced.OrderBy(c => Order(c.BotId)).ThenBy(c => c.BotId))
        {
            if (placements.Count >= toPlace)
                break;

            GuildSlot best = null;
            double bestScore = 0;
            foreach (GuildSlot guild in guilds)
            {
                if (guild.Target <= 0 || guild.Count >= guild.Target)
                    continue;
                double score = (guild.Target - guild.Count) / (double)guild.Target * Fit(guild.Charter, candidate.Level);
                if (best == null || score > bestScore + 1e-9 || Math.Abs(score - bestScore) <= 1e-9 && guild.Ordinal < best.Ordinal)
                {
                    best = guild;
                    bestScore = score;
                }
            }

            if (best == null)
                break;
            best.Count++;
            placements.Add(new Placement(candidate.BotId, best.GuildId));
        }

        return placements;
    }

    /// <summary>
    /// Rank changes for one guild: a missing leader is replaced by the highest-level member, and
    /// officers (rank 1) are added until there is one per 12 members (at most 3). Nobody is demoted.
    /// </summary>
    public static List<(long BotId, int Rank)> RankChanges(IReadOnlyList<Member> members)
    {
        var changes = new List<(long BotId, int Rank)>();
        if (members == null || members.Count == 0)
            return changes;

        var ranks = members.ToDictionary(m => m.BotId, m => m.Rank);
        IEnumerable<Member> byLevel = members.OrderByDescending(m => m.Level).ThenBy(m => m.BotId);

        if (!ranks.Values.Contains(0))
        {
            Member leader = byLevel.First();
            ranks[leader.BotId] = 0;
            changes.Add((leader.BotId, 0));
        }

        int wantedOfficers = Math.Min(MaximumOfficers, Math.Max(1, members.Count / MembersPerOfficer));
        if (members.Count < 3)
            wantedOfficers = 0;
        int officers = ranks.Values.Count(rank => rank >= 1 && rank <= 2);
        foreach (Member member in byLevel)
        {
            if (officers >= wantedOfficers)
                break;
            if (ranks[member.BotId] <= 2)
                continue;
            ranks[member.BotId] = 1;
            changes.Add((member.BotId, 1));
            officers++;
        }

        return changes;
    }

    /// <summary>Rank titles for a charter, ranks 0 to 9.</summary>
    public static string[] RankTitles(BotGuildCharter charter) => charter switch
    {
        BotGuildCharter.Rvr => ["Warlord", "Captain", "Lieutenant", "Veteran", "Veteran", "Veteran", "Veteran", "Soldier", "Soldier", "Recruit"],
        BotGuildCharter.Raiding => ["Raid Leader", "Officer", "Officer", "Veteran", "Veteran", "Veteran", "Veteran", "Member", "Member", "Initiate"],
        BotGuildCharter.Social => ["Host", "Steward", "Steward", "Regular", "Regular", "Regular", "Regular", "Friend", "Friend", "Newcomer"],
        BotGuildCharter.Crafting => ["Guild Master", "Master Crafter", "Master Crafter", "Journeyman", "Journeyman", "Journeyman", "Journeyman", "Apprentice", "Apprentice", "Novice"],
        _ => ["Guildmaster", "Officer", "Officer", "Elder", "Elder", "Elder", "Elder", "Member", "Member", "Novice"],
    };

    #region Names

    private sealed record RealmWords(string[] Emblems, string[] Adjectives, string[] Groups, string[] Places,
        string[] Houses, string[] Templates);

    // Invented, period-style names. Places are the realm's own towns and zones. No real guild names.
    private static readonly RealmWords Albion = new(
        ["Silver Hart", "White Rose", "Iron Gauntlet", "Crimson Lion", "Golden Chalice", "Black Boar", "Azure Banner",
         "Oaken Shield", "Broken Spear", "Gilded Falcon", "Red Griffin", "Grey Mantle"],
        ["Loyal", "Grey", "Stalwart", "Wandering", "Merry", "Valiant", "Hollow", "Last", "Steadfast", "Weary"],
        ["Wardens", "Lancers", "Pilgrims", "Guard", "Company", "Fellowship", "Vanguard", "Watch", "Brethren", "Riders"],
        ["Avalon", "Salisbury", "Cotswold", "Lyonesse", "Ludlow", "Humberton", "Pennine", "Dartmoor", "Cornwall"],
        ["Pendrell", "Ashcombe", "Wexley", "Thornbury", "Caradoc", "Marlow", "Harrowgate", "Penhallow"],
        ["Order of the {E}", "Knights of the {E}", "Brotherhood of the {E}", "The {A} {G}", "House {H}", "{P} {G}"]);

    private static readonly RealmWords Midgard = new(
        ["Frost Wolf", "Iron Raven", "Black Bear", "Storm Giant", "Red Axe", "Grey Fjord", "Rune Stone", "Winter Elk",
         "Ember Troll", "Night Owl", "Broken Oar", "White Stag"],
        ["Frozen", "Bitter", "Howling", "Mighty", "Grim", "Old", "Thunder", "Bold", "Silent", "Wild"],
        ["Huscarls", "Raiders", "Skalds", "Shieldwall", "Kin", "Hirdmen", "Reavers", "Brothers", "Sworn", "Hunters"],
        ["Jordheim", "Mularn", "Haggerfel", "Svealand", "Gotar", "Myrkwood", "Uppland", "Vasudheim", "Audliten"],
        ["Ironhand", "Stormborn", "Hallgrim", "Ulfsson", "Bjornsdal", "Ravnkel", "Thorvald", "Eiriks"],
        ["Sons of the {E}", "Hall of the {E}", "{E} Clan", "The {A} {G}", "Clan {H}", "{P} {G}"]);

    private static readonly RealmWords Hibernia = new(
        ["Moonlit Grove", "Silver Stag", "Hidden Glen", "Emerald Fern", "Twilight Bough", "Thorn Crown", "Mist Harp",
         "Starlit Pool", "Elder Oak", "Wild Hare", "Grey Heron", "Rowan Bough"],
        ["Wandering", "Gentle", "Bright", "Fey", "Quiet", "Swift", "Laughing", "Dreaming", "Hidden", "Faithful"],
        ["Wardens", "Harpers", "Rangers", "Kindred", "Sentinels", "Seekers", "Fianna", "Dreamers", "Circle", "Riders"],
        ["Tir na Nog", "Connacht", "Lough Derg", "Ardee", "Howth", "Mag Mell", "Innis Carthaig", "Ardagh"],
        ["na Darach", "na Mara", "na Gaoithe", "na Coille", "an tSleibhe", "an Fhiaigh", "na Gealai", "an Locha"],
        ["Circle of the {E}", "Children of the {E}", "The {A} {G}", "Clann {H}", "{P} {G}", "Keepers of the {E}"]);

    // Casual names, as many real 2003 guilds had; offered to Social guilds in every realm.
    private static readonly string[] CasualNames =
    [
        "Pie and Pints", "Tavern Regulars", "Corpse Run Club", "Late Night Pullers", "Bind Stone Irregulars",
        "Ding Grats", "No Pets Allowed", "Wipe Club", "Second Pull", "Dead Again", "Merchant Hoppers", "Slow Walkers",
    ];

    private static readonly string[] CraftingNames =
    [
        "Hammer and Tongs", "Needle and Anvil", "Forge and Loom", "{P} Smiths", "{P} Tailors", "Guild of the {E}",
    ];

    /// <summary>
    /// A realm-flavored name for a new guild, deterministic for (realm, charter, ordinal), skipping
    /// names <paramref name="isTaken"/> reports. Always returns a name that passes <see cref="NameFormat"/>.
    /// </summary>
    public static string NameFor(eRealm realm, BotGuildCharter charter, int ordinal, Func<string, bool> isTaken)
    {
        RealmWords words = realm switch
        {
            eRealm.Midgard => Midgard,
            eRealm.Hibernia => Hibernia,
            _ => Albion,
        };
        isTaken ??= _ => false;
        var random = new Random(((int)realm * 7919) ^ (ordinal * 104729) ^ ((int)charter * 31));

        for (int attempt = 0; attempt < 400; attempt++)
        {
            string template;
            if (charter == BotGuildCharter.Social && attempt % 2 == 0)
                template = CasualNames[random.Next(CasualNames.Length)];
            else if (charter == BotGuildCharter.Crafting && attempt % 2 == 0)
                template = CraftingNames[random.Next(CraftingNames.Length)];
            else
                template = words.Templates[random.Next(words.Templates.Length)];

            string name = template
                .Replace("{E}", words.Emblems[random.Next(words.Emblems.Length)])
                .Replace("{A}", words.Adjectives[random.Next(words.Adjectives.Length)])
                .Replace("{G}", words.Groups[random.Next(words.Groups.Length)])
                .Replace("{P}", words.Places[random.Next(words.Places.Length)])
                .Replace("{H}", words.Houses[random.Next(words.Houses.Length)]);
            if (NameFormat.IsMatch(name) && !isTaken(name))
                return name;
        }

        // Every combination taken (hundreds of guilds): number a base name.
        for (int suffix = 2; ; suffix++)
        {
            string name = $"{words.Groups[Math.Abs(ordinal) % words.Groups.Length]} of {words.Places[Math.Abs(ordinal) % words.Places.Length]} {ToRoman(suffix)}";
            if (name.Length > MaximumNameLength)
                name = $"{words.Groups[0]} {ToRoman(suffix)}";
            if (NameFormat.IsMatch(name) && !isTaken(name))
                return name;
        }
    }

    private static string ToRoman(int number)
    {
        (int Value, string Numeral)[] numerals =
            [(1000, "M"), (900, "CM"), (500, "D"), (400, "CD"), (100, "C"), (90, "XC"), (50, "L"), (40, "XL"), (10, "X"), (9, "IX"), (5, "V"), (4, "IV"), (1, "I")];
        var result = new System.Text.StringBuilder();
        foreach ((int value, string numeral) in numerals)
        {
            while (number >= value)
            {
                result.Append(numeral);
                number -= value;
            }
        }
        return result.ToString();
    }

    #endregion

    /// <summary>Invite rule for a bot asked to join a player's guild.</summary>
    public enum InviteAnswer
    {
        Accept,
        WrongRealm,
        Dead,
        Busy,
        AlreadyInPlayerGuild,
        LeadsOwnGuild,
        LevelGap,
    }

    public const int MaximumInviteLevelGap = 10;

    public static InviteAnswer AnswerInvite(bool sameRealm, bool alive, bool inCombat, bool inOtherPlayerGuild,
        int rankInBotGuild, int botLevel, int inviterLevel)
    {
        if (!sameRealm)
            return InviteAnswer.WrongRealm;
        if (!alive)
            return InviteAnswer.Dead;
        if (inCombat)
            return InviteAnswer.Busy;
        if (inOtherPlayerGuild)
            return InviteAnswer.AlreadyInPlayerGuild;
        if (rankInBotGuild is >= 0 and <= 2)
            return InviteAnswer.LeadsOwnGuild;
        if (Math.Abs(botLevel - inviterLevel) > MaximumInviteLevelGap)
            return InviteAnswer.LevelGap;
        return InviteAnswer.Accept;
    }
}
