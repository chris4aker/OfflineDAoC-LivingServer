using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS;

/// <summary>
/// "where do I choose advanced class" (1.2.3, the owner's game on 1.2.2: a level 5 Albion caster asked it in /faction and no bot
/// answered). The game's facts: a character starts in a base class and picks its class at level 5 from the trainer of the class it
/// wants; every class of a realm trains in its capital. The base classes and what each leads to are the server's own class tree
/// (playerclasses/base), the classic and Shrouded Isles classes only (Living Server's era: no Catacombs classes and no custom
/// class). Pure.
/// </summary>
public static class AutonomousChatClassChoice
{
    /// <summary>Promotion happens at this level.</summary>
    public const int PromotionLevel = 5;

    /// <summary>Each base class and the classes it leads to, by realm (the server's class tree, era classes only).</summary>
    public static readonly IReadOnlyDictionary<string, (eRealm Realm, string[] Classes)> Bases =
        new Dictionary<string, (eRealm, string[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["Fighter"] = (eRealm.Albion, ["Armsman", "Mercenary", "Paladin", "Reaver"]),
            ["Acolyte"] = (eRealm.Albion, ["Cleric", "Friar"]),
            ["Mage"] = (eRealm.Albion, ["Cabalist", "Sorcerer"]),
            ["Elementalist"] = (eRealm.Albion, ["Wizard", "Theurgist"]),
            ["Rogue"] = (eRealm.Albion, ["Infiltrator", "Minstrel", "Scout"]),
            ["Disciple"] = (eRealm.Albion, ["Necromancer"]),
            ["Viking"] = (eRealm.Midgard, ["Berserker", "Savage", "Skald", "Thane", "Warrior"]),
            ["Seer"] = (eRealm.Midgard, ["Healer", "Shaman"]),
            ["Mystic"] = (eRealm.Midgard, ["Bonedancer", "Runemaster", "Spiritmaster"]),
            ["Midgard Rogue"] = (eRealm.Midgard, ["Hunter", "Shadowblade"]),
            ["Guardian"] = (eRealm.Hibernia, ["Blademaster", "Champion", "Hero"]),
            ["Naturalist"] = (eRealm.Hibernia, ["Bard", "Druid", "Warden"]),
            ["Stalker"] = (eRealm.Hibernia, ["Nightshade", "Ranger"]),
            ["Magician"] = (eRealm.Hibernia, ["Eldritch", "Enchanter", "Mentalist"]),
            ["Forester"] = (eRealm.Hibernia, ["Animist", "Valewalker"]),
        };

    private static readonly Regex Asks = new(
        @"\b(advanced? class(es)?|adv class|(pick|choose|chose|select|get|change) (my |a |ur |your |the )?(real |final |main |next )?class|class (change|choice|pick)|" +
        @"(spec|go|turn|switch) (in)?to (a |an )?[a-z]+|become (a |an )?[a-z]+|(promote|promotion)|which class (can|should|do) i|what (class(es)?|can i) (can i )?(be|become|pick|choose))\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex Becomes = new(@"\b(?:become|be|spec into|go into|turn into|switch to|pick|choose)\s+(?:a |an )?([a-z]+)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The class an era base class leads to, or the base class itself, as the table writes it; null for neither. Pure.</summary>
    private static string Known(string word)
    {
        if (string.IsNullOrWhiteSpace(word))
            return null;
        string w = word.Trim();
        foreach ((string baseName, (eRealm _, string[] classes)) in Bases)
        {
            if (string.Equals(baseName, w, StringComparison.OrdinalIgnoreCase))
                return baseName;
            if (classes.FirstOrDefault(c => string.Equals(c, w, StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(c + "s", w, StringComparison.OrdinalIgnoreCase)) is { } cls)
                return cls;
        }
        return null;
    }

    /// <summary>The base class a class comes from (its own name for a base class), or null. Pure.</summary>
    public static string BaseOf(string className)
    {
        if (string.IsNullOrWhiteSpace(className))
            return null;
        foreach ((string baseName, (eRealm _, string[] classes)) in Bases)
            if (string.Equals(baseName, className, StringComparison.OrdinalIgnoreCase) ||
                classes.Any(c => string.Equals(c, className, StringComparison.OrdinalIgnoreCase)))
                return baseName;
        // The server names the Albion rogue base "Rogue" and Midgard's "Rogue" too: the realm decides (see RealmBase).
        return null;
    }

    private static bool IsBase(string className) => className != null && Bases.ContainsKey(className);

    /// <summary>The asker's base class name as the table writes it ("Rogue" in Midgard is "Midgard Rogue"). Pure.</summary>
    private static string RealmBase(eRealm realm, string className) =>
        realm == eRealm.Midgard && string.Equals(className, "Rogue", StringComparison.OrdinalIgnoreCase) ? "Midgard Rogue" : className;

    /// <summary>
    /// Whether a line asks how or where a character picks its class ("where do I choose advanced class", "how do i become a
    /// cleric", "where do i spec into theurgist", "pick my class"). "become a" or "spec into" counts only with a class of the
    /// table after it. Pure.
    /// </summary>
    public static bool AsksAboutClassChoice(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !Asks.IsMatch(text))
            return false;
        string t = text.ToLowerInvariant();
        if (Regex.IsMatch(t, @"\b(advanced? class|adv class|(pick|choose|chose|select|get|change) (my |a |ur |your |the )?(real |final |main |next )?class|class (change|choice|pick)|promot|which class (can|should|do) i|what (class(es)?|can i) (can i )?(be|become|pick|choose))"))
            return true;
        return Becomes.Match(t) is { Success: true } m && Known(m.Groups[1].Value) is { } named && !IsBase(named);
    }

    /// <summary>
    /// The answer: the class choice at level 5, from the trainer of the class wanted, in the realm's capital
    /// (<paramref name="capital"/>, as the trainer answers name it). The class named in the line, else the asker's options;
    /// another realm's class said as such; an asker past it told so. Null when the line doesn't ask. Pure.
    /// </summary>
    public static string Answer(string text, eRealm realm, string askerClass, int level, string capital, Random random)
    {
        if (!AsksAboutClassChoice(text))
            return null;
        random ??= Random.Shared;
        string cap = string.IsNullOrWhiteSpace(capital) ? "the capital" : capital.Trim();
        string mine = RealmBase(realm, askerClass?.Trim());
        string named = Becomes.Match(text.ToLowerInvariant()) is { Success: true } m ? Known(m.Groups[1].Value) : null;
        if (named != null && IsBase(named))
            named = null;
        string Pick(params string[] lines) => lines[random.Next(lines.Length)];

        if (named != null)
        {
            string namedBase = BaseOf(named);
            eRealm home = Bases[namedBase].Realm;
            string cls = named.ToLowerInvariant();
            if (realm is (eRealm.Albion or eRealm.Midgard or eRealm.Hibernia) && home != realm)
                return AutonomousChatKnowledge.OtherRealmClassAnswer(named, home, random);
            if (mine != null && !IsBase(mine) && BaseOf(mine) != null)
                return string.Equals(mine, named, StringComparison.OrdinalIgnoreCase)
                    ? Pick($"ur already {A(cls)}", $"u already are one, the {cls} trainer in {cap} trains u from here")
                    : Pick($"ur {A(mine.ToLowerInvariant())} already, the class is picked at {PromotionLevel} and it sticks. {A(cls)} is a new character",
                        $"cant switch now, ur {mine.ToLowerInvariant()}. roll a new char for {cls}");
            if (IsBase(mine) && !string.Equals(mine, namedBase, StringComparison.OrdinalIgnoreCase))
                return Pick($"{A(Word(mine))} cant become {A(cls)}, thats from {Word(namedBase)}. urs are {Options(mine)}",
                    $"{cls} comes from {Word(namedBase)}, not {Word(mine)}. u can pick {Options(mine)}");
            return Pick($"at {PromotionLevel} talk to the {cls} trainer in {cap}, they make u one",
                $"hit {PromotionLevel}, then the {cls} trainer in {cap} promotes u",
                $"{cls} trainer in {cap}, once ur {PromotionLevel}");
        }
        if (mine != null && IsBase(mine) && Bases[mine].Realm == realm)
        {
            string soon = level >= PromotionLevel ? "ur high enough, go now" : $"at {PromotionLevel}";
            return Pick($"{soon}: talk to the trainer of the class u want in {cap}. for {Word(mine)} thats {Options(mine)}",
                $"in {cap}, at the trainer of the one u want ({Options(mine)}), {soon}",
                $"{Options(mine)}, their trainers are in {cap}. {soon}");
        }
        if (mine != null && BaseOf(mine) != null && !IsBase(mine))
            return Pick($"u already picked, ur {A(mine.ToLowerInvariant())}. its done once at {PromotionLevel} at the class trainer",
                $"thats done at {PromotionLevel} at the class trainer in {cap}, ur {mine.ToLowerInvariant()} already");
        return Pick($"at {PromotionLevel} talk to the trainer of the class u want in {cap}, they promote u",
            $"class trainers in {cap}, at lvl {PromotionLevel} u pick there");
    }

    private static string A(string word) =>
        (string.IsNullOrEmpty(word) ? "a" : "aeiou".Contains(char.ToLowerInvariant(word[0])) ? "an " : "a ") + word;

    private static string Word(string baseName) =>
        (baseName ?? "that").Replace("Midgard Rogue", "rogue", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();

    private static string Options(string baseName)
    {
        string[] classes = Bases[baseName].Classes.Select(c => c.ToLowerInvariant()).ToArray();
        return classes.Length switch
        {
            1 => classes[0],
            2 => classes[0] + " or " + classes[1],
            _ => string.Join(", ", classes[..^1]) + " or " + classes[^1],
        };
    }
}
