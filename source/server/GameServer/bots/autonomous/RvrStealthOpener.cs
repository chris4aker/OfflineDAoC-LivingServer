using System;

namespace DOL.GS;

/// <summary>
/// Stealth openers for gamebot stealthers (work package 6; docs/RVR-FIELD-1.2.md, "Soloers and stealthers"). Bots never
/// used a style that needs stealth (BotMeleeStylePolicy.IsEligible keeps them out). Now an Infiltrator, Nightshade or
/// Shadowblade in the open-field scope, still hidden, may open with its own class's stealth styles (Backstab,
/// Perforate Artery, Critical Strike and the like, whatever its Critical Strike line gave it): once per target, only
/// from stealth (the style's own rule, StyleProcessor.CanUseStyle, checks stealth and position again at the swing).
/// The archer classes camp in stealth too but have no melee opener. Pure.
/// </summary>
public static class RvrStealthOpener
{
    /// <summary>How long a stealther keeps trying to get behind its target for a back opener before it opens anyway.</summary>
    public const long BehindMilliseconds = 4_000;
    /// <summary>An opener queued this long ago without landing is given up (the fight goes on without it).</summary>
    public const long QueuedMilliseconds = 4_000;
    /// <summary>Added to a usable stealth opener's score, so a hidden bot opens with it (BotMeleeStylePolicy.Score).</summary>
    public const double ScoreBonus = 5.0;

    /// <summary>The melee stealthers, the classes with stealth openers.</summary>
    public static bool IsOpenerClass(eCharacterClass characterClass) => characterClass is
        eCharacterClass.Infiltrator or eCharacterClass.Nightshade or eCharacterClass.Shadowblade;

    /// <summary>The classes that camp milegates and chokepoints in stealth: the melee stealthers and the archers.</summary>
    public static bool IsCampingStealther(eCharacterClass characterClass) => IsOpenerClass(characterClass) ||
        characterClass is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger;

    /// <summary>
    /// Whether a stealth style may be used now: the switch is on, the bot is in the open-field scope, the style needs
    /// stealth, the bot is hidden, it is a melee stealther, and it has not opened on this target yet.
    /// </summary>
    public static bool Allows(bool switchOn, bool inScope, bool styleNeedsStealth, bool stealthed, bool openerClass, bool openedOnTarget) =>
        switchOn && inScope && styleNeedsStealth && stealthed && openerClass && !openedOnTarget;

    /// <summary>
    /// Whether the stealther takes the time to get behind its target for a back opener (veterans nearly always, casual
    /// players often open from where they stand).
    /// </summary>
    public static bool GoesBehind(RvrSkillTier tier, double roll) => roll < tier switch
    {
        RvrSkillTier.Veteran => 0.9,
        RvrSkillTier.Average => 0.7,
        _ => 0.4,
    };

    /// <summary>What the stealther does at melee range this turn.</summary>
    public enum Move : byte
    {
        /// <summary>Not an opener's turn: the brain fights as usual (and drops stealth).</summary>
        None,
        /// <summary>Queue the opener and swing from stealth.</summary>
        Open,
        /// <summary>Wait for the queued opener to land.</summary>
        Wait,
        /// <summary>Step behind the target first.</summary>
        GetBehind,
    }

    /// <summary>
    /// The opener's turn. <paramref name="usableNow"/>: an opener the style rules accept from where the bot stands;
    /// <paramref name="needsBehind"/>: its only openers are back styles and it is not behind; <paramref name="queuedAt"/>:
    /// when it queued its opener (0: not yet); <paramref name="since"/>: when it reached this target.
    /// </summary>
    public static Move Next(bool stealthed, bool inMeleeRange, bool opened, bool usableNow, bool needsBehind, bool goesBehind,
        long queuedAt, long since, long now)
    {
        if (!stealthed || opened || !inMeleeRange)
            return Move.None;
        if (queuedAt > 0)
            return now - queuedAt < QueuedMilliseconds ? Move.Wait : Move.None;
        if (usableNow)
            return Move.Open;
        if (needsBehind && goesBehind && now - since < BehindMilliseconds)
            return Move.GetBehind;
        return Move.None;
    }
}
