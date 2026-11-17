using System;
using DOL.Database;
using DOL.Database.Attributes;

namespace DOL.GS;

/// <summary>
/// The saved-progress tables of "Bots as people" (docs/BOTS-AS-PEOPLE-1.2.md, "Data and the release"; work package 0),
/// declared in one list so the release tools know them before any package writes a row. The server creates them empty at
/// start-up (every [DataTable] is registered); nothing writes them in WP0. They name the developer's bots, so each is in
/// progress-policy.json (OptionalProgressTables: build_release_037.py empties it and checks it empty, and the progress
/// transfer carries it) and in PassportTables.Decisions (StaysInWorld). build_release_037.py reads <see cref="All"/>
/// from this file (bot_people_tables), and a table missing from the policy stops the release build. UT_BotPeopleTables.
/// To add a table: a constant here, its record below with [DataTable(TableName = ...)], both lists in the same order, the
/// policy and passport entries, and a LivingServerTable in <see cref="BotPeopleStore"/> (WP0-REPORT.md, "Adding a table").
/// </summary>
public static class BotPeopleTables
{
    public const string Skill = "offline_bot_skill";
    public const string Places = "offline_bot_places";
    public const string Milestones = "offline_bot_milestones";
    public const string MechanicsSeen = "offline_bot_mechanics_seen";

    public static readonly string[] All = [Skill, Places, Milestones, MechanicsSeen];

    public static readonly Type[] RecordTypes =
    [
        typeof(OfflineBotSkillRecord), typeof(OfflineBotPlacesRecord), typeof(OfflineBotMilestoneRecord), typeof(OfflineBotMechanicSeenRecord),
    ];
}

/// <summary>The six skill facets (item 2): what each changes is in docs/BOTS-AS-PEOPLE-1.2.md, "What the bot does".</summary>
public enum BotSkillFacet : byte
{
    Reactions,
    Positioning,
    Pulls,
    Fire,
    Buffs,
    Power,
}

public static class BotSkillFacets
{
    public static readonly BotSkillFacet[] All =
        [BotSkillFacet.Reactions, BotSkillFacet.Positioning, BotSkillFacet.Pulls, BotSkillFacet.Fire, BotSkillFacet.Buffs, BotSkillFacet.Power];
}

/// <summary>
/// A person's skill (item 2, WP1): the tier it has reached and each facet's experience. The base facet values are fixed
/// from the bot id and never saved. One row per person. Belongs to the person: a merge keeps the highest of each.
/// </summary>
[DataTable(TableName = BotPeopleTables.Skill)]
public sealed class OfflineBotSkillRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotSkillRecord)MemberwiseClone();

    /// <summary>BotPersonKey text ("c:1234").</summary>
    [DataElement(AllowDbNull = false, Unique = true, Varchar = BotPersonKey.TextLength)] public string PersonKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Index = true)] public long CharacterId { get; set; }
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    /// <summary>(int)RvrSkillTier the person has reached (growth moves it up one tier at most in 1.2).</summary>
    [DataElement(AllowDbNull = false)] public int Tier { get; set; }
    [DataElement(AllowDbNull = false)] public double ReactionsXp { get; set; }
    [DataElement(AllowDbNull = false)] public double PositioningXp { get; set; }
    [DataElement(AllowDbNull = false)] public double PullsXp { get; set; }
    [DataElement(AllowDbNull = false)] public double FireXp { get; set; }
    [DataElement(AllowDbNull = false)] public double BuffsXp { get; set; }
    [DataElement(AllowDbNull = false)] public double PowerXp { get; set; }
    /// <summary>ISO 8601 UTC ("o").</summary>
    [DataElement(AllowDbNull = false)] public string UpdatedUtc { get; set; } = string.Empty;

    public double Experience(BotSkillFacet facet) => facet switch
    {
        BotSkillFacet.Reactions => ReactionsXp,
        BotSkillFacet.Positioning => PositioningXp,
        BotSkillFacet.Pulls => PullsXp,
        BotSkillFacet.Fire => FireXp,
        BotSkillFacet.Buffs => BuffsXp,
        BotSkillFacet.Power => PowerXp,
        _ => 0,
    };

    public void SetExperience(BotSkillFacet facet, double value)
    {
        switch (facet)
        {
            case BotSkillFacet.Reactions: ReactionsXp = value; break;
            case BotSkillFacet.Positioning: PositioningXp = value; break;
            case BotSkillFacet.Pulls: PullsXp = value; break;
            case BotSkillFacet.Fire: FireXp = value; break;
            case BotSkillFacet.Buffs: BuffsXp = value; break;
            case BotSkillFacet.Power: PowerXp = value; break;
        }
    }

    public double TotalExperience => ReactionsXp + PositioningXp + PullsXp + FireXp + BuffsXp + PowerXp;
}

/// <summary>
/// The places a person knows in one realm (item 4, WP3): bit sets over data/chat/places.json (<see cref="BotPeopleBits"/>).
/// One row per person and realm, about 100 bytes. Belongs to the person, per realm: a merge takes the union.
/// </summary>
[DataTable(TableName = BotPeopleTables.Places)]
public sealed class OfflineBotPlacesRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotPlacesRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Index = true, Varchar = BotPersonKey.TextLength)] public string PersonKey { get; set; } = string.Empty;
    /// <summary>(int)eRealm whose map this is.</summary>
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false, Index = true)] public long CharacterId { get; set; }
    /// <summary>Known places (visited, or seeded by level).</summary>
    [DataElement(AllowDbNull = false, Varchar = 255)] public string Known { get; set; } = string.Empty;
    /// <summary>Half-known places (told the way, not yet visited).</summary>
    [DataElement(AllowDbNull = false, Varchar = 255)] public string HalfKnown { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string UpdatedUtc { get; set; } = string.Empty;
}

/// <summary>
/// One personal milestone arc of a character (item 6, WP6): the ding to 50, realm rank 5, a dragon trophy. Belongs to the
/// character (a level is a character's): a merge keeps one row per character and arc, re-keyed to the person. So the
/// in-memory key is (PersonKey, CharacterId, Arc), not (PersonKey, Arc): two alts of one person can both chase 50.
/// </summary>
[DataTable(TableName = BotPeopleTables.Milestones)]
public sealed class OfflineBotMilestoneRecord : DataObject, ILivingServerRow
{
    public const string Active = "active";
    public const string Reached = "reached";
    public const string Dropped = "dropped";

    public DataObject CopyForSave() => (OfflineBotMilestoneRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Index = true, Varchar = BotPersonKey.TextLength)] public string PersonKey { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Index = true)] public long CharacterId { get; set; }
    [DataElement(AllowDbNull = false)] public int Realm { get; set; }
    [DataElement(AllowDbNull = false, Varchar = 64)] public string Name { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Varchar = 32)] public string ClassName { get; set; } = string.Empty;
    /// <summary>The arc's id (WP6: "ding50", "rr5", "dragon_trophy").</summary>
    [DataElement(AllowDbNull = false, Varchar = 32)] public string Arc { get; set; } = string.Empty;
    /// <summary><see cref="Active"/>, <see cref="Reached"/> or <see cref="Dropped"/>.</summary>
    [DataElement(AllowDbNull = false, Varchar = 16)] public string State { get; set; } = Active;
    [DataElement(AllowDbNull = false)] public string StartedUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string ReachedUtc { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false)] public string LastTalkUtc { get; set; } = string.Empty;
    /// <summary>WP6: the highest talk point the arc has passed (0, 50 or 90 percent; BotMilestones.TalkPoint), talked or not,
    /// so a restart never says the same point twice.</summary>
    [DataElement(AllowDbNull = false)] public int TalkPoint { get; set; }
    /// <summary>WP6, the dragon trophy: the high-end raid kills the character was at without winning an item.</summary>
    [DataElement(AllowDbNull = false)] public int Count { get; set; }
    /// <summary>WP6, the dragon trophy: the item it really won (its name as the game gave it), once reached.</summary>
    [DataElement(AllowDbNull = false, Varchar = 96)] public string Trophy { get; set; } = string.Empty;
    /// <summary>WP6, the dragon trophy: the boss that dropped it (the item's Creator, the killed boss's name).</summary>
    [DataElement(AllowDbNull = false, Varchar = 64)] public string Source { get; set; } = string.Empty;

    /// <summary>How far an arc got, for merges: reached 0, active 1, dropped 2, anything else 3. Pure.</summary>
    public static int StateRank(string state) => state switch { Reached => 0, Active => 1, Dropped => 2, _ => 3 };

    /// <summary>A new row (not yet saved) with the same values, keyed by <paramref name="person"/>.</summary>
    public OfflineBotMilestoneRecord CopyAs(BotPersonKey person) => new()
    {
        PersonKey = person.ToString(), CharacterId = CharacterId, Realm = Realm, Name = Name, ClassName = ClassName, Arc = Arc,
        State = State, StartedUtc = StartedUtc, ReachedUtc = ReachedUtc, LastTalkUtc = LastTalkUtc,
        TalkPoint = TalkPoint, Count = Count, Trophy = Trophy, Source = Source,
    };
}

/// <summary>
/// How often a person has seen one raid mechanic (WP2R, raid learning from raiders). One row per raider and mechanic.
/// Belongs to the person: a merge sums Seen.
/// </summary>
[DataTable(TableName = BotPeopleTables.MechanicsSeen)]
public sealed class OfflineBotMechanicSeenRecord : DataObject, ILivingServerRow
{
    public DataObject CopyForSave() => (OfflineBotMechanicSeenRecord)MemberwiseClone();

    [DataElement(AllowDbNull = false, Index = true, Varchar = BotPersonKey.TextLength)] public string PersonKey { get; set; } = string.Empty;
    /// <summary>The mechanic's id (WP2R: the raid call's key, e.g. "Xanxicar/blast"; RaidDiscipline.LearnedMechanics).</summary>
    [DataElement(AllowDbNull = false, Varchar = 64)] public string Mechanic { get; set; } = string.Empty;
    [DataElement(AllowDbNull = false, Index = true)] public long CharacterId { get; set; }
    [DataElement(AllowDbNull = false)] public int Seen { get; set; }
    [DataElement(AllowDbNull = false)] public string LastSeenUtc { get; set; } = string.Empty;
}
