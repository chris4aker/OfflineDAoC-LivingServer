using System;
using System.Globalization;

namespace DOL.GS;

/// <summary>
/// What a person has learned so far (item 2, WP1): minutes of experience per facet and the highest tier it has reached
/// (-1: none recorded yet). The saved part of a skill profile (<see cref="OfflineBotSkillRecord"/>); the base facet values
/// come from the person key and are never saved. Experience only grows (<see cref="BotSkillGrowth.Step"/>). Pure.
/// </summary>
public readonly record struct BotSkillExperience
{
    public double Reactions { get; init; }
    public double Positioning { get; init; }
    public double Pulls { get; init; }
    public double Fire { get; init; }
    public double Buffs { get; init; }
    public double Power { get; init; }
    /// <summary>(int)RvrSkillTier the person has reached, or -1 when nothing is recorded. A floor: skill never falls.</summary>
    public int ReachedTier { get; init; }

    /// <summary>No experience and no tier recorded (a bot never seen before).</summary>
    public static BotSkillExperience None => new() { ReachedTier = -1 };

    public double this[BotSkillFacet facet] => facet switch
    {
        BotSkillFacet.Reactions => Reactions,
        BotSkillFacet.Positioning => Positioning,
        BotSkillFacet.Pulls => Pulls,
        BotSkillFacet.Fire => Fire,
        BotSkillFacet.Buffs => Buffs,
        BotSkillFacet.Power => Power,
        _ => 0,
    };

    public BotSkillExperience With(BotSkillFacet facet, double minutes) => facet switch
    {
        BotSkillFacet.Reactions => this with { Reactions = minutes },
        BotSkillFacet.Positioning => this with { Positioning = minutes },
        BotSkillFacet.Pulls => this with { Pulls = minutes },
        BotSkillFacet.Fire => this with { Fire = minutes },
        BotSkillFacet.Buffs => this with { Buffs = minutes },
        BotSkillFacet.Power => this with { Power = minutes },
        _ => this,
    };

    public double Total => Reactions + Positioning + Pulls + Fire + Buffs + Power;

    /// <summary>The mean over the six facets, in minutes.</summary>
    public double Mean => Total / 6;

    /// <summary>A saved row's experience (None for no row). Negative or unreadable values count as 0; a tier outside
    /// Casual..Veteran as none.</summary>
    public static BotSkillExperience From(OfflineBotSkillRecord row) => row == null ? None : new BotSkillExperience
    {
        Reactions = Clean(row.ReactionsXp), Positioning = Clean(row.PositioningXp), Pulls = Clean(row.PullsXp),
        Fire = Clean(row.FireXp), Buffs = Clean(row.BuffsXp), Power = Clean(row.PowerXp),
        ReachedTier = row.Tier is >= (int)RvrSkillTier.Casual and <= (int)RvrSkillTier.Veteran ? row.Tier : -1,
    };

    /// <summary>Writes this experience into a row (the tier only when one is recorded).</summary>
    public void CopyTo(OfflineBotSkillRecord row)
    {
        foreach (BotSkillFacet facet in BotSkillFacets.All)
            row.SetExperience(facet, this[facet]);
        if (ReachedTier >= 0)
            row.Tier = ReachedTier;
    }

    private static double Clean(double value) => double.IsFinite(value) && value > 0 ? value : 0;
}

/// <summary>
/// A gamebot's skill (docs/BOTS-AS-PEOPLE-1.2.md, item 2; work package 1): its tier (RVR-FIELD-1.2's Casual / Average /
/// Veteran, 30/50/20 shifted by rvr_bot_awareness) and six facets. Each facet is a skill value from 0 to 3: Casual is
/// [0, 1), Average [1, 2), Veteran [2, 3); <see cref="Position"/> is where the facet sits inside the tier's band (0 to 1),
/// which spreads one bot from another inside a tier without a second system. Fixed from the person key, the personality
/// and the experience (no random number, no clock). A cached value per online bot (<see cref="BotSkill"/>). Pure.
/// <para>Read it in WP2 through <see cref="BotSkill.TryProfile(GameBot, out BotSkillProfile)"/> and turn a facet into a
/// rate or a delay with <see cref="Blend(BotSkillFacet, double, double, double)"/> (the bot's own tier) or
/// <see cref="Blend(RvrSkillTier, double, double, double, double)"/> (an RvR group's tier, owner decision 5).</para>
/// </summary>
public readonly struct BotSkillProfile : IEquatable<BotSkillProfile>
{
    /// <summary>A facet's start spreads up to this far (of a band) around the bot's own place in its band.</summary>
    public const double FacetSpread = 0.2;
    /// <summary>A personality trait moves one facet's start up to this far (of a band).</summary>
    public const double MaxTraitTilt = 0.1;
    /// <summary>The highest skill value inside a band (a facet never reaches the next band by its start).</summary>
    public const double BandTop = 0.999;

    // The bot's own roll: never the RvR group's salt (RvrGroupSkill "RVRS"), so a bot's tier does not follow its group's.
    private const ulong RollSalt = 0x42534B4C; // "BSKL"
    private const ulong FacetSalt = 0x4253_4B46_0000UL; // "BSKF" + facet

    private readonly double _reactions, _positioning, _pulls, _fire, _buffs, _power;

    private BotSkillProfile(BotPersonKey person, RvrSkillTier startTier, RvrSkillTier tier, double bandPosition, in BotSkillExperience experience,
        double reactions, double positioning, double pulls, double fire, double buffs, double power)
    {
        Person = person;
        StartTier = startTier;
        Tier = tier;
        BandPosition = bandPosition;
        Experience = experience;
        _reactions = reactions;
        _positioning = positioning;
        _pulls = pulls;
        _fire = fire;
        _buffs = buffs;
        _power = power;
    }

    /// <summary>The person (None: no profile; WP2 then keeps today's rules).</summary>
    public BotPersonKey Person { get; }
    /// <summary>The tier the bot started at (its roll with today's rvr_bot_awareness), before any experience.</summary>
    public RvrSkillTier StartTier { get; }
    /// <summary>The tier now: the start tier, or one above it once the facets grew there (or the saved tier said so).</summary>
    public RvrSkillTier Tier { get; }
    /// <summary>Where in its start tier's band the bot began, 0 (the bottom) to 1 (the top).</summary>
    public double BandPosition { get; }
    /// <summary>The experience this profile was made from.</summary>
    public BotSkillExperience Experience { get; }

    public bool IsNone => Person.IsNone;

    /// <summary>The highest tier this bot can reach in 1.2: one above its start (owner decision 6).</summary>
    public RvrSkillTier MaxTier => MaxTierFor(StartTier);

    /// <summary>A facet's skill value, 0 to 3 (Casual [0, 1), Average [1, 2), Veteran [2, 3)).</summary>
    public double Skill(BotSkillFacet facet) => facet switch
    {
        BotSkillFacet.Reactions => _reactions,
        BotSkillFacet.Positioning => _positioning,
        BotSkillFacet.Pulls => _pulls,
        BotSkillFacet.Fire => _fire,
        BotSkillFacet.Buffs => _buffs,
        BotSkillFacet.Power => _power,
        _ => (int)Tier + 0.5,
    };

    /// <summary>Where a facet sits inside the band of <see cref="Tier"/>: 0 (the band's worst) to 0.999 (its best). A facet
    /// still below the band after a promotion sits at 0; one above it (impossible with the one-tier cap) at the top.</summary>
    public double Position(BotSkillFacet facet) => Math.Clamp(Skill(facet) - (int)Tier, 0, BandTop);

    /// <summary>The mean of the six skill values (its whole part is the tier the facets have grown to).</summary>
    public double MeanSkill => (_reactions + _positioning + _pulls + _fire + _buffs + _power) / 6;

    /// <summary>
    /// A facet's value for a rule whose design table gives one value per tier (a rate, a delay, a factor): the tier's
    /// value at the middle of its band, moving toward the neighbouring tier's value at the band's edges (half way at the
    /// edge between two tiers; a quarter of the gap beyond the outer edges). Uses the bot's own tier.
    /// </summary>
    public double Blend(BotSkillFacet facet, double casual, double average, double veteran) =>
        Blend(Tier, Position(facet), casual, average, veteran);

    /// <summary>
    /// The same for a given tier and a place in its band, so an RvR group's tier (owner decision 5) can take a member's
    /// <see cref="Position"/>: <c>Blend(groupTier, profile.Position(BotSkillFacet.Reactions), 1.3, 1.0, 0.75)</c>. Values
    /// that are all 0 or more never blend below 0. Pure.
    /// </summary>
    public static double Blend(RvrSkillTier tier, double position, double casual, double average, double veteran)
    {
        double x = (int)tier + Math.Clamp(double.IsFinite(position) ? position : 0.5, 0, 1);
        double value;
        if (x <= 0.5)
            value = casual + (casual - average) * (0.5 - x) * 0.5;
        else if (x <= 1.5)
            value = casual + (average - casual) * (x - 0.5);
        else if (x <= 2.5)
            value = average + (veteran - average) * (x - 1.5);
        else
            value = veteran + (veteran - average) * (x - 2.5) * 0.5;
        return casual >= 0 && average >= 0 && veteran >= 0 ? Math.Max(0, value) : value;
    }

    // ---- making a profile (pure) ------------------------------------------------------------------------------------

    /// <summary>A profile with no experience (a bot seen for the first time).</summary>
    public static BotSkillProfile For(BotPersonKey person, BotPersonality? personality, RvrBotAwareness awareness) =>
        For(person, personality, awareness, BotSkillExperience.None);

    /// <summary>
    /// The profile of a person: the start tier from its own roll on <see cref="RvrGroupSkill.Shares"/> (so the shares are
    /// exact and rvr_bot_awareness moves them), each facet's start inside that band from the bot's place in it, a fixed
    /// spread per facet and its personality, and each facet grown by its experience (<see cref="BotSkillGrowth.Grown"/>).
    /// The tier is the start tier, or one above once the mean of the facets reaches the next band or the saved tier is
    /// higher, never more than one above (owner decision 6). The personality only moves facets inside the band, never the
    /// tier. None for no person. Pure.
    /// </summary>
    public static BotSkillProfile For(BotPersonKey person, BotPersonality? personality, RvrBotAwareness awareness,
        in BotSkillExperience experience)
    {
        if (person.IsNone)
            return default;
        var shares = RvrGroupSkill.Shares(awareness);
        double roll = Roll(person);
        RvrSkillTier start = StartTierFor(roll, shares);
        double place = BandPositionFor(roll, start, shares);
        double top = BotSkillGrowth.Top(start);
        BotSkillExperience learned = experience;

        double Grown(BotSkillFacet facet) =>
            BotSkillGrowth.Grown(StartSkill(person, personality, start, place, facet), top, learned[facet]);

        double reactions = Grown(BotSkillFacet.Reactions), positioning = Grown(BotSkillFacet.Positioning),
            pulls = Grown(BotSkillFacet.Pulls), fire = Grown(BotSkillFacet.Fire), buffs = Grown(BotSkillFacet.Buffs),
            power = Grown(BotSkillFacet.Power);
        double mean = (reactions + positioning + pulls + fire + buffs + power) / 6;
        RvrSkillTier tier = TierFor(start, mean, experience.ReachedTier);
        return new BotSkillProfile(person, start, tier, place, experience, reactions, positioning, pulls, fire, buffs, power);
    }

    /// <summary>The person's own roll, 0 to 100 (exclusive), fixed by its key (the kind is part of the salt, so c:12 and p:12
    /// roll apart).</summary>
    public static double Roll(BotPersonKey person) =>
        AutonomousBotPersonality.Mix(person.Id, RollSalt + (ulong)person.Kind) % 1_000_000 / 10_000.0;

    /// <summary>The start tier for a roll on these shares (Casual below the Casual share, Veteran in the top share). Pure.</summary>
    public static RvrSkillTier StartTierFor(double roll, (int Casual, int Average, int Veteran) shares) =>
        RvrGroupSkill.TierForRoll(roll, 0, shares);

    /// <summary>Where a roll sits inside its tier's slice of the shares, 0 to just under 1. Pure.</summary>
    public static double BandPositionFor(double roll, RvrSkillTier tier, (int Casual, int Average, int Veteran) shares)
    {
        (double low, double width) = tier switch
        {
            RvrSkillTier.Casual => (0.0, (double)shares.Casual),
            RvrSkillTier.Average => ((double)shares.Casual, 100.0 - shares.Casual - shares.Veteran),
            _ => (100.0 - shares.Veteran, (double)shares.Veteran),
        };
        return width <= 0 ? 0.5 : Math.Clamp((roll - low) / width, 0, BandTop);
    }

    /// <summary>A facet's fixed spread around the bot's place, -<see cref="FacetSpread"/> to +<see cref="FacetSpread"/>. Pure.</summary>
    public static double Spread(BotPersonKey person, BotSkillFacet facet) =>
        (AutonomousBotPersonality.Mix(person.Id, FacetSalt + ((ulong)person.Kind << 8) + (ulong)facet) % 10_001 / 5_000.0 - 1) * FacetSpread;

    /// <summary>
    /// How a personality moves a facet inside the band (our design, from the traits' meaning): patient bots place
    /// themselves, pull and husband power more carefully; daring ones (Risk) stand in area effects longer; chatty ones let
    /// a buff run out. Reactions are not tilted. 0 with personalities off (null). Pure.
    /// </summary>
    public static double TraitTilt(BotPersonality? personality, BotSkillFacet facet)
    {
        if (personality is not { } p)
            return 0;
        static double Toward(int trait) => (Math.Clamp(trait, 0, 9) - 4.5) / 4.5 * MaxTraitTilt;
        return facet switch
        {
            BotSkillFacet.Positioning => Toward(p.Traits.Patience),
            BotSkillFacet.Pulls => Toward(p.Traits.Patience),
            BotSkillFacet.Fire => -Toward(p.Traits.Risk),
            BotSkillFacet.Buffs => -Toward(p.Traits.Chattiness),
            BotSkillFacet.Power => Toward(p.Traits.Patience),
            _ => 0,
        };
    }

    /// <summary>A facet's skill value before any experience: inside the start tier's band. Pure.</summary>
    public static double StartSkill(BotPersonKey person, BotPersonality? personality, RvrSkillTier start, double bandPosition,
        BotSkillFacet facet) =>
        (int)start + Math.Clamp(bandPosition + Spread(person, facet) + TraitTilt(personality, facet), 0, BandTop);

    /// <summary>One tier above the start at most, and never past Veteran. Pure.</summary>
    public static RvrSkillTier MaxTierFor(RvrSkillTier start) =>
        (RvrSkillTier)Math.Min((int)start + 1, (int)RvrSkillTier.Veteran);

    /// <summary>
    /// The tier from the start tier, the facets' mean skill and the saved tier: the higher of the grown and the saved
    /// tier, never below the start and never above <see cref="MaxTierFor"/>. A saved tier from a higher awareness setting
    /// is held to the cap of today's start tier (the owner's one difficulty knob, decision 6). Pure.
    /// </summary>
    public static RvrSkillTier TierFor(RvrSkillTier start, double meanSkill, int reachedTier)
    {
        int grown = double.IsFinite(meanSkill) ? (int)Math.Floor(Math.Clamp(meanSkill, 0, 2.999)) : (int)start;
        int tier = Math.Max(grown, reachedTier);
        return (RvrSkillTier)Math.Clamp(tier, (int)start, (int)MaxTierFor(start));
    }

    // ---- text ----------------------------------------------------------------------------------------------------------

    public static string TierName(RvrSkillTier tier) => tier switch
    {
        RvrSkillTier.Casual => "casual",
        RvrSkillTier.Average => "average",
        RvrSkillTier.Veteran => "veteran",
        _ => "?",
    };

    public static string FacetName(BotSkillFacet facet) => facet switch
    {
        BotSkillFacet.Reactions => "reactions",
        BotSkillFacet.Positioning => "positioning",
        BotSkillFacet.Pulls => "pulls",
        BotSkillFacet.Fire => "fire",
        BotSkillFacet.Buffs => "buffs",
        BotSkillFacet.Power => "power",
        _ => "?",
    };

    /// <summary>"tier=average start=casual place=0.42 reactions=1.08 positioning=1.21 ... xp_h=12.5": the log and test text.</summary>
    public string Describe()
    {
        if (IsNone)
            return "tier=none";
        var text = new System.Text.StringBuilder(160);
        text.Append("tier=").Append(TierName(Tier)).Append(" start=").Append(TierName(StartTier))
            .Append(" place=").Append(BandPosition.ToString("0.00", CultureInfo.InvariantCulture));
        foreach (BotSkillFacet facet in BotSkillFacets.All)
            text.Append(' ').Append(FacetName(facet)).Append('=').Append(Skill(facet).ToString("0.00", CultureInfo.InvariantCulture));
        text.Append(" xp_h=").Append((Experience.Mean / 60).ToString("0.0", CultureInfo.InvariantCulture));
        return text.ToString();
    }

    public override string ToString() => Describe();

    public bool Equals(BotSkillProfile other) =>
        Person == other.Person && StartTier == other.StartTier && Tier == other.Tier && BandPosition.Equals(other.BandPosition) &&
        Experience == other.Experience && _reactions.Equals(other._reactions) && _positioning.Equals(other._positioning) &&
        _pulls.Equals(other._pulls) && _fire.Equals(other._fire) && _buffs.Equals(other._buffs) && _power.Equals(other._power);

    public override bool Equals(object obj) => obj is BotSkillProfile other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Person, StartTier, Tier, BandPosition, Experience, MeanSkill);

    public static bool operator ==(BotSkillProfile left, BotSkillProfile right) => left.Equals(right);
    public static bool operator !=(BotSkillProfile left, BotSkillProfile right) => !left.Equals(right);
}
