using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace OfflineDaoc.Shared;

/// <summary>
/// Bot names the way a 2003 DAoC server's names looked (owner 2026-10-29: "75% of names in 2003 style, rest in lore
/// focused names. Do research first."). Linked into the launcher, so the server and the launcher's bot batches name bots
/// the same way.
/// <para>
/// The research (docs/research/BOT-NAMES-2003.md): about 300 names from 2001-2004 rosters and forum posts, hand-tagged.
/// Most were made-up single names, short (median six letters) and often harsh (z, x, k, q in three in ten): Grizlas,
/// Kalgarn, Zarff, Mezzar. Then English compounds (Souleater, Whitepoison, Grayhawk), jokes and puns (Sofragile,
/// Fancypants, Champtastic), everyday first names (Nate, Faye, Jenna), class or race jokes (Puppethealer, Meaniesavage)
/// and stretched letters (Droooood, Buffffffer). DAoC took letters only, so the research found almost no xX or number
/// names; the owner wants them in (3% each, the wider gamer names of the era: XxShadowxX, Gankr420). The
/// rest of this generator's names (a quarter) are lore names: the realm's old syllable names and real Norse, Celtic and
/// Old English names.
/// </para>
/// The written lists (jokes, first names, lore names) are this generator's own, not names lifted from the research; a
/// generic compound can still come out the same as one a 2003 player used (Grayhawk). Never: a word players say in chat (a bot named "Axe" would catch
/// "anyone got an axe?"), a name the server's invalid-names list rejects, pop culture (Mythic's naming policy banned it),
/// or a joke from after 2003.
/// </summary>
public static partial class EraBotNames
{
    public enum Style { Invented, Compound, Joke, FirstName, ClassJoke, Stretched, Decorated, Numbered, Lore }

    /// <summary>The mix (percent of all names). The 2003 styles add up to 75, lore names to 25.</summary>
    public static readonly (Style Style, int Percent)[] Mix =
    [
        // The research's 2001-2004 shares (invented 49%, compound 15%, joke 14%, first name 9%, class or race 4%,
        // stretched 3%; pop culture and real myth names left out) scaled to 75, then xX and number names added (owner
        // 2026-10-29: "having no xX or Xx or numbers like 420 is a major miss"). DAoC's name filter took letters only, so
        // the research found almost none; they are the era's wider gamer names, the ones a server that let them through
        // would have had.
        (Style.Invented, 35),
        (Style.Compound, 12),
        (Style.Joke, 10),
        (Style.FirstName, 7),
        (Style.ClassJoke, 3),
        (Style.Stretched, 2),
        (Style.Decorated, 3),
        (Style.Numbered, 3),
        (Style.Lore, 25),
    ];

    public const int MinLength = 3, MaxLength = 18;

    private static HashSet<string> _words;

    /// <summary>A word players or bots say in chat (lowercase), which no bot may be named.</summary>
    public static bool IsChatWord(string name)
    {
        _words ??= new HashSet<string>(WordList.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);
        return _words.Contains(name);
    }

    /// <summary>Picks a style by <see cref="Mix"/>. Pure for a given random.</summary>
    public static Style PickStyle(Random random)
    {
        int roll = random.Next(100);
        foreach ((Style style, int percent) in Mix)
        {
            if (roll < percent)
                return style;
            roll -= percent;
        }
        return Style.Invented;
    }

    /// <summary>
    /// A name in the given style, or null when that style found none that is free and allowed (the caller tries another).
    /// <paramref name="realm"/> is 1 Albion, 2 Midgard, 3 Hibernia; <paramref name="className"/> is the bot's class (for
    /// class jokes); <paramref name="taken"/> says whether a name is used; <paramref name="rejected"/> is the server's
    /// invalid-names check. Lore names are the caller's (<paramref name="lore"/>).
    /// </summary>
    public static string Generate(Style style, int realm, bool female, string className, Random random,
        Func<string, bool> taken, Func<string, bool> rejected, Func<string> lore)
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            string candidate = style switch
            {
                Style.Invented => attempt % 6 == 5 && TakenVariant(Invented(realm, female, random), random) is { } variant ? variant : Invented(realm, female, random),
                Style.Compound => Compound(random),
                Style.Joke => Joke(random),
                Style.FirstName => Pick(female ? FemaleFirstNames : MaleFirstNames, random),
                Style.ClassJoke => ClassJoke(realm, className, random),
                Style.Stretched => Stretch(random.Next(2) == 0 ? Invented(realm, female, random) : Pick(female ? FemaleFirstNames : MaleFirstNames, random), random),
                Style.Decorated => Decorated(random),
                Style.Numbered => Numbered(realm, female, random),
                Style.Lore => random.Next(10) < 3 ? Pick(LoreNames(realm, female), random) : lore?.Invoke(),
                _ => null,
            };
            if (candidate == null)
                continue;
            if (style is not (Style.Decorated or Style.Numbered))
                candidate = Capitalize(candidate);
            // A taken name gets an extra letter, as players did (Jammer, Jammerr).
            // (Only made-up and lore names: a joke or compound with a doubled letter just reads as a typo.)
            if (taken(candidate) && style is Style.Invented or Style.Lore)
                candidate = TakenVariant(candidate, random) ?? candidate;
            if (Allowed(candidate, style) && !taken(candidate) && !(rejected?.Invoke(candidate) ?? false))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// 3-18 long and starting with a capital; letters only and lowercase after the first (xX names may mix case, number
    /// names end in digits or swap a few letters for them); not a chat word (first names have their own list). Pure.
    /// </summary>
    public static bool Allowed(string name, Style style = Style.Invented)
    {
        if (string.IsNullOrEmpty(name) || name.Length is < MinLength or > MaxLength || !char.IsUpper(name[0]) || LooksAnachronistic(name))
            return false;
        bool letters = name.All(c => c is >= 'a' and <= 'z' or >= 'A' and <= 'Z');
        string core = new string(name.Where(char.IsLetter).ToArray());
        return style switch
        {
            Style.Decorated => letters && !IsChatWord(core),
            Style.Numbered => name.All(char.IsLetterOrDigit) && name.Count(char.IsLetter) >= 3 && name.Any(char.IsDigit) &&
                              name.Skip(1).All(c => !char.IsUpper(c)) && !IsChatWord(name) && !BadNumbers.Any(n => Regex.IsMatch(name, $"(?<!\\d){n}(?!\\d)")),
            Style.FirstName => letters && name.Skip(1).All(char.IsLower) && !FirstNameWords.Contains(name),
            _ => letters && name.Skip(1).All(char.IsLower) && !IsChatWord(name),
        };
    }

    // Endings and pieces that read as post-2003 internet (the research's 2026 freeshard: "Bonklol", "Shroomlol").
    private static readonly string[] Anachronisms = ["lol", "lmao", "yolo", "swag", "leeroy", "rickroll", "lolcat", "noscope", "derp", "troll face"];

    private static bool LooksAnachronistic(string name)
    {
        string lower = name.ToLowerInvariant();
        return lower.EndsWith("lol", StringComparison.Ordinal) || Anachronisms.Any(a => a != "lol" && lower.Contains(a.Replace(" ", string.Empty), StringComparison.Ordinal));
    }

    #region Invented names (Grizlas, Kalgarn, Zarff, Rhia)

    private static readonly string[] Onsets =
    [
        "B", "Bl", "Br", "D", "Dr", "F", "Fr", "G", "Gl", "Gr", "H", "J", "K", "Kr", "Kl", "L", "M", "N", "P", "Pr", "Qw", "R", "S",
        "Sk", "Sn", "St", "Sv", "Sz", "T", "Th", "Tr", "V", "Vr", "W", "Z", "Zh", "X", "Y", "C", "Cr",
        "", "", "", "",   // a vowel start: Ozill, Aonn, Eroma
    ];

    private static readonly string[] Vowels = ["a", "a", "a", "a", "e", "e", "e", "i", "i", "o", "o", "o", "u", "u", "y", "ae", "ia", "oo"];

    private static readonly string[] Middles =
    [
        "l", "r", "n", "m", "z", "k", "g", "d", "t", "s", "v", "x", "b", "zz", "ll", "rr", "nn", "kk", "tt", "sk", "rk", "rg", "lg",
        "nd", "nt", "rm", "rn", "th", "sh", "ch", "zr", "dr", "gr", "kr", "br", "lk",
    ];

    private static readonly string[] MaleEnds =
    [
        "as", "ar", "arn", "ik", "ick", "or", "on", "ul", "ok", "us", "ax", "ix", "ek", "en", "in", "ath", "ash", "ur", "ir", "ad",
        "ogg", "rik", "gar", "rak", "an", "el", "is", "o", "ff", "nd", "rd", "x", "z", "k", "th", "orn", "ag", "um", "ius", "eth",
    ];

    private static readonly string[] FemaleEnds =
    [
        "a", "ia", "ina", "ie", "y", "ra", "la", "elle", "is", "yn", "ey", "ah", "i", "e", "ette", "ana", "ella", "ora", "issa",
        "ira", "wyn", "ys", "ae", "eth", "ith", "en", "an", "ara", "ea",
    ];

    // A light touch of the realm: most 2003 names were realm-neutral (the research: Norse roots in Midgard, Irish in Hibernia,
    // Old English in Albion, but invented names everywhere).
    private static readonly Dictionary<int, string[]> RealmOnsets = new()
    {
        [1] = ["Ald", "Ed", "Gw", "Mer", "Wil", "Hal", "Cor"],
        [2] = ["Tj", "Hj", "Sv", "Bj", "Thor", "Grim", "Sk"],
        [3] = ["Ae", "Ci", "Fi", "Mae", "Ni", "Ro", "Sio"],
    };

    /// <summary>A made-up single name, mostly two syllables, sometimes one or three. Pure for a given random.</summary>
    public static string Invented(int realm, bool female, Random random)
    {
        for (int attempt = 0; attempt < 20; attempt++)
            if (TryInvented(realm, female, random) is { } name)
                return name;
        return "Zarn" + Pick(female ? FemaleEnds : MaleEnds, random);
    }

    private static string TryInvented(int realm, bool female, Random random)
    {
        var name = new StringBuilder();
        string onset = random.Next(8) == 0 && RealmOnsets.TryGetValue(realm, out string[] own) ? Pick(own, random) : Pick(Onsets, random);
        name.Append(onset);
        if (!EndsWithVowel(onset))
            name.Append(Pick(Vowels, random));
        // Short like the research's (median six letters): one middle syllable mostly, now and then none or two.
        int syllables = random.Next(20) switch { < 3 => 0, < 18 => 1, _ => 2 };
        for (int i = 0; i < syllables; i++)
        {
            name.Append(Pick(Middles, random));
            name.Append(Pick(Vowels, random));
        }
        string end = Pick(female ? FemaleEnds : MaleEnds, random);
        if (EndsWithVowel(name.ToString()) && StartsWithVowel(end))
            name.Length--;
        name.Append(end);
        string result = name.ToString();
        // Three of the same letter only on purpose (Stretch), and nothing too long or too short.
        return Regex.IsMatch(result, @"(.)\1\1") || Regex.IsMatch(result, "[aeiouy]{3}", RegexOptions.IgnoreCase) ||
               result.Length < 3 || result.Length > 9 ? null : result;
    }

    private static bool EndsWithVowel(string s) => s.Length > 0 && "aeiouy".Contains(char.ToLowerInvariant(s[^1]));
    private static bool StartsWithVowel(string s) => s.Length > 0 && "aeiouy".Contains(char.ToLowerInvariant(s[0]));

    /// <summary>A taken name with a letter doubled or added, as players did when theirs was gone: Jammer, Jammerr, Jaammer. Pure.</summary>
    public static string TakenVariant(string name, Random random)
    {
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength - 1)
            return null;
        switch (random.Next(3))
        {
            case 0:
                return name + name[^1];
            case 1:
                int vowel = name.Skip(1).Select((c, i) => (c, i: i + 1)).Where(x => "aeiouy".Contains(x.c)).Select(x => x.i).DefaultIfEmpty(-1).First();
                return vowel < 0 ? name + name[^1] : name.Insert(vowel, name[vowel].ToString());
            default:
                // Kalgarn, Kalgarne; Rhia, Rhiah. Never after a y (Thornfuryh).
                return name.EndsWith('y') ? name + "y" : EndsWithVowel(name) ? name + "h" : name + "e";
        }
    }

    #endregion

    #region Compounds (Souleater, Grayhawk, Darkcarver)

    private static readonly string[] CompoundFirst =
    [
        "Black", "White", "Gray", "Grey", "Dark", "Shadow", "Soul", "Blood", "Frost", "Storm", "Night", "Iron", "Grim", "Death", "Doom",
        "Fire", "Ice", "Thunder", "Steel", "Moon", "Sun", "Star", "Ash", "Bone", "Skull", "Raven", "Wolf", "Hawk", "Dusk", "Dawn",
        "Silver", "Gold", "Stone", "Red", "Green", "Winter", "Ember", "Hell", "Rune", "Spell", "Mind", "Venom", "Solar", "Lunar",
        "Thorn", "Oak", "Mist", "Fog", "Wind", "Rain", "Swift", "Wild", "Holy", "Dread", "Bane", "Chaos", "Rage", "Fury",
    ];

    private static readonly string[] CompoundSecond =
    [
        "eater", "poison", "acid", "hawk", "carver", "flare", "blade", "fang", "bane", "heart", "fist", "hammer", "song", "singer",
        "walker", "caller", "strike", "fury", "shade", "wind", "storm", "claw", "thorn", "bringer", "weaver", "dancer", "slayer",
        "born", "mane", "hide", "skull", "bow", "arrow", "spear", "axe", "fire", "frost", "light", "wolf", "raven", "rider",
        "seeker", "keeper", "binder", "breaker", "maker", "splitter", "crusher", "reaver", "soul", "mind", "wing", "scale", "spike",
    ];

    /// <summary>Two English words in one (never a word itself, never the same twice: "Stormstorm"). Pure for a given random.</summary>
    public static string Compound(Random random)
    {
        string first = Pick(CompoundFirst, random), second = Pick(CompoundSecond, random);
        return string.Equals(first, second, StringComparison.OrdinalIgnoreCase) || first.EndsWith(second, StringComparison.OrdinalIgnoreCase)
            ? null : first + second;
    }

    #endregion

    #region Jokes and puns (Sofragile, Fancypants, Champtastic, Hotsytotsy)

    // Written for this generator in the research's 2003 vein (food, British slang, self-mockery, AFK, bad luck); none copied
    // from the research's real names.
    private static readonly string[] Jokes =
    [
        "Mrbiscuits", "Notaclue", "Sirnapsalot", "Diesalot", "Runsalot", "Lagmonster", "Afkagain", "Brbsnack", "Pottedplant",
        "Soupoftheday", "Cheesetoastie", "Toastrack", "Tinfoilhat", "Doorstopper", "Kettleboil", "Spudgun", "Biscuitbarrel",
        "Crumpetking", "Teaandtoast", "Mushypeas", "Bakedbean", "Fishfingers", "Picklejar", "Gravyboat", "Custardpie",
        "Grumpypants", "Smartypants", "Snorepants", "Wobblyknees", "Tinytoes", "Bigfeet", "Sleepyhead", "Lazybones", "Butterfingers",
        "Clumsyclod", "Lootmonkey", "Pocketlint", "Bagofrocks", "Rustyspoon", "Woodenspoon", "Squeakyboots", "Mudpuddle",
        "Notahealer", "Notatank", "Imnotlost", "Wheresmyhorse", "Healsforfood", "Willbuffforcoin", "Pleasedontkill", "Justpassing",
        "Iwasafk", "Wrongway", "Ranintoawall", "Fallsoffstuff", "Ateabee", "Hiccupper", "Sneezer",
        "Itsmeagain", "Notmyfault", "Blamethebard", "Pullsaggro", "Trainstation", "Bigoops", "Ohdear", "Whoopsie", "Uhoh",
        "Moremana", "Needabuff", "Gimmegold", "Spareacopper", "Brokeagain", "Pennyless", "Coppercount", "Lastpotion",
        "Toughcookie", "Softcookie", "Chocchipper", "Jammydodger", "Bourbonbiscuit", "Hobnobber", "Sugarlump",
        "Meatpie", "Pastyface", "Scotchegg", "Sausageroll", "Bangersnmash", "Chipbutty", "Cuppatea", "Milkntwo", "Tuesdayknight",
        "Weekendwarrior", "Schoolnight", "Bedtimesoon", "Mumsaysbed", "Onemorepull", "Justonemore", "Nolifeatall",
        "Couchpotato", "Carrotcake", "Pumpkinhead", "Turnipking", "Cabbagepatch", "Mrturnip", "Sirsprout", "Ladypotato",
        "Wigglebum", "Bumblefoot", "Snorkelface", "Spicytaco", "Nachogrande", "Burritobob", "Waffleironer",
    ];

    private static readonly string[] JokePrefixes = ["Sir", "Mr", "Lil", "Big", "Captain", "Lord", "Lady", "Mister", "Old", "Wee", "Fat", "Uncle", "Auntie"];
    private static readonly string[] JokeNouns =
    [
        "biscuit", "noodle", "pickle", "turnip", "spud", "muffin", "crumpet", "wobble", "sprout", "pudding", "nugget", "crumb",
        "sock", "teapot", "kipper", "gravy", "dumpling", "custard", "bean", "jelly", "waffle", "pancake", "kettle", "mop",
        "snore", "stabby", "zappy", "pointy", "bonk", "smash", "whack", "fluffy", "grumpy", "sneaky", "shiny",
    ];
    private static readonly string[] AlotVerbs = ["heals", "dies", "runs", "naps", "eats", "falls", "trips", "sneezes", "whines", "loots", "begs", "zaps", "bonks", "stabs", "snores", "wanders"];

    /// <summary>A joke name: a written one, or Sir/Mr/Lil plus a silly noun, or "Healsalot". Pure for a given random.</summary>
    public static string Joke(Random random) => random.Next(10) switch
    {
        < 5 => Pick(Jokes, random),
        < 8 => Pick(JokePrefixes, random) + Pick(JokeNouns, random),
        _ => Pick(AlotVerbs, random) + "alot",
    };

    #endregion

    #region Class and race jokes (Puppethealer, Meaniesavage, Champtastic)

    private static readonly string[] ClassJokeAdjectives =
    [
        "Grumpy", "Sleepy", "Tiny", "Angry", "Shiny", "Sneaky", "Lazy", "Happy", "Hungry", "Wobbly", "Cranky", "Lucky", "Unlucky",
        "Meanie", "Fluffy", "Nervous", "Clumsy", "Squishy", "Speedy", "Puppet", "Pocket", "Budget", "Discount",
    ];
    private static readonly string[] ClassJokeSuffixes = ["tastic", "licious", "tron", "zilla", "inator", "ster", "erino", "zor", "o", "y"];

    private static readonly Dictionary<int, string[]> Races = new()
    {
        [1] = ["Briton", "Saracen", "Highlander", "Avalonian", "Inconnu"],
        [2] = ["Kobold", "Troll", "Dwarf", "Norse", "Valkyn"],
        [3] = ["Lurikeen", "Elf", "Firbolg", "Celt", "Sylvan"],
    };

    /// <summary>A joke on the bot's class or its realm's races: Grumpyfriar, Thanetastic, Lurikeenie, Bigtroll. Pure for a given random.</summary>
    public static string ClassJoke(int realm, string className, Random random)
    {
        string cls = string.IsNullOrWhiteSpace(className) ? null : new string(className.Where(char.IsLetter).ToArray()).ToLowerInvariant();
        string race = Races.TryGetValue(realm, out string[] races) ? Pick(races, random).ToLowerInvariant() : null;
        string subject = cls != null && random.Next(3) > 0 ? cls : race;
        if (subject == null)
            return null;
        return random.Next(3) switch
        {
            0 => Pick(ClassJokeAdjectives, random) + subject,
            1 => subject + Pick(ClassJokeSuffixes, random),
            _ => subject.EndsWith('e') ? subject + "nie" : subject + "ie",
        };
    }

    #endregion

    #region Stretched letters (Droooood, Buffffffer, Zaaaarff)

    /// <summary>A short name with one letter held, four to six long in all: Droooood, Evaaaa. Pure for a given random.</summary>
    public static string Stretch(string name, Random random)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 7 || Regex.IsMatch(name, "[aeiouy]{2}", RegexOptions.IgnoreCase))
            return null;
        int[] vowels = name.Select((c, i) => (c, i)).Where(x => x.i > 0 && "aeiouy".Contains(char.ToLowerInvariant(x.c))).Select(x => x.i).ToArray();
        int at = vowels.Length > 0 ? vowels[random.Next(vowels.Length)] : name.Length - 1;
        int extra = 3 + random.Next(3);
        return name.Insert(at, new string(char.ToLowerInvariant(name[at]), extra));
    }

    #endregion

    #region xX names (XxShadowxX, xXDeathXx)

    private static readonly string[] DecoratedWords =
    [
        "Shadow", "Dark", "Death", "Blood", "Killer", "Slayer", "Ninja", "Assassin", "Legend", "Sniper", "Reaper", "Demon",
        "Angel", "Dragon", "Hunter", "Ghost", "Venom", "Chaos", "Storm", "Fury", "Doom", "Pain", "Soul", "Night", "Wolf",
        "Raven", "Blade", "Fire", "Frost", "Rogue", "Stalker", "Silent", "Evil", "Elite", "Lethal", "Toxic", "Psycho", "Grim",
    ];

    /// <summary>
    /// An xX name: XxShadowxX, Xxdeathxx, XxBloodSlayerxX. Starts with a capital X (a name must), never xxx (the server's
    /// invalid-names list rejects it). Pure for a given random.
    /// </summary>
    public static string Decorated(Random random)
    {
        string word = Pick(DecoratedWords, random);
        if (random.Next(4) == 0)
            word += Pick(DecoratedWords, random);
        return random.Next(4) switch
        {
            0 => "Xx" + word + "xX",
            1 => "Xx" + word.ToLowerInvariant() + "xx",
            2 => "X" + word + "X",
            _ => "Xx" + word + "Xx",
        };
    }

    #endregion

    #region Number names (Gankr420, Darkblade87, D4rkst0rm)

    private static readonly string[] Numbers =
    [
        "420", "1337", "666", "2003", "2k3", "99", "01", "13", "23", "77", "85", "86", "87", "89", "90", "91", "92", "7",
        "3", "4", "9", "12", "21", "42", "101", "999", "2000", "2001", "3000",
    ];

    // Numbers kept out: hate symbols and the sexual one.
    private static readonly string[] BadNumbers = ["88", "14", "1488", "18", "69"];

    private static readonly string[] GamerWords =
    [
        "gankr", "pwnage", "killa", "snipa", "stabba", "smasha", "nukr", "zerga", "lootr", "camper", "fragger", "headshot",
        "darkblade", "deathbringer", "bloodlust", "hellspawn", "doomlord", "shadowkill", "deadeye", "warlord", "spellslinger",
    ];

    private static readonly Dictionary<char, char> Leet = new() { ['a'] = '4', ['e'] = '3', ['o'] = '0', ['i'] = '1', ['s'] = '5', ['t'] = '7' };

    /// <summary>
    /// A number name: a gamer word, compound or made-up name with a number after it (Gankr420, Darkblade87, Zarff99), or a
    /// few letters swapped for digits (D4rkst0rm). Pure for a given random.
    /// </summary>
    public static string Numbered(int realm, bool female, Random random)
    {
        string baseName = random.Next(3) switch
        {
            0 => Pick(GamerWords, random),
            1 => Compound(random),
            _ => Invented(realm, female, random),
        };
        if (baseName == null)
            return null;
        baseName = Capitalize(baseName);
        if (random.Next(5) == 0 && baseName.Length >= 5)
        {
            // Leet: swap two or three letters after the first, keep the rest.
            var chars = baseName.ToCharArray();
            int swapped = 0;
            for (int i = 1; i < chars.Length && swapped < 3; i++)
                if (Leet.TryGetValue(chars[i], out char digit) && random.Next(2) == 0)
                {
                    chars[i] = digit;
                    swapped++;
                }
            return swapped >= 2 ? new string(chars) : baseName + Pick(Numbers, random);
        }
        string number = Pick(Numbers, random);
        return baseName.Length + number.Length <= MaxLength ? baseName + number : null;
    }

    #endregion

    #region Everyday first names (Nate, Faye, Jenna)

    // Common in 2003 among adults and teens in the US and the UK. No names that are chat words (Max, Will, Mark, Rose, Grace...).
    private static readonly string[] MaleFirstNames =
    [
        "Dave", "Steve", "Gary", "Kevin", "Brian", "Darren", "Craig", "Gavin", "Nathan", "Nate", "Jason", "Shane", "Wayne", "Keith",
        "Derek", "Trevor", "Neil", "Stuart", "Barry", "Colin", "Graham", "Ian", "Jamie", "Justin", "Kyle", "Ryan",
        "Travis", "Dustin", "Brandon", "Cody", "Brett", "Todd", "Jeff", "Greg", "Doug", "Scott", "Eric", "Aaron", "Adam",
        "Simon", "Martin", "Paul", "Jon", "Mike", "Andy", "Pete", "Phil", "Tom", "Ben", "Dan", "Matt", "Josh",
        "Jake", "Luke", "Sean", "Shaun", "Owen", "Rhys", "Gareth", "Alan", "Carl", "Lars", "Jens", "Sven", "Olle", "Joakim",
        "Hendrik", "Pieter", "Sander", "Bram", "Tobias", "Jonas", "Florian", "Malik", "Marek", "Tomas",
    ];

    private static readonly string[] FemaleFirstNames =
    [
        "Jenna", "Faye", "Eva", "Kelly", "Nicole", "Amanda", "Jessica", "Ashley", "Brittany", "Tiffany", "Kristy",
        "Melissa", "Stacy", "Tracy", "Kerry", "Lisa", "Sarah", "Emma", "Laura", "Claire", "Gemma", "Kirsty", "Leanne", "Nicola",
        "Hayley", "Zoe", "Chloe", "Becky", "Jenny", "Kate", "Katie", "Sophie", "Lauren", "Megan", "Rachel", "Rebecca", "Danielle",
        "Natalie", "Vanessa", "Monica", "Erica", "Julie", "Juli", "Mandy", "Sandra", "Tanja", "Anja", "Sanna", "Linnea", "Elin",
        "Femke", "Lotte", "Ingrid", "Karin", "Mirja", "Aisling", "Siobhan", "Ciara",
    ];

    // First names that are also everyday words (checked separately, since common names are in the English word list).
    private static readonly HashSet<string> FirstNameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "max", "will", "mark", "rose", "grace", "bill", "jack", "frank", "faith", "hope", "joy", "dawn", "summer", "pat", "sue",
        "ray", "art", "may", "june", "amber", "ruby", "holly", "ivy", "daisy", "penny", "sandy", "rusty", "chuck", "bud", "al",
        "rich", "lee", "dean", "chad", "kai", "rob", "crystal", "heather", "carol", "jean", "gene", "ruth", "eve",
    };

    #endregion

    #region Lore names (real Norse, Celtic and Old English names; the realm's old syllable names are the caller's)

    private static readonly Dictionary<(int Realm, bool Female), string[]> LoreNameLists = new()
    {
        [(1, false)] = ["Eldred", "Osric", "Wulfric", "Godwin", "Leofric", "Aldwin", "Eadric", "Oswin", "Wystan", "Brihtric", "Aelfric", "Beornwulf", "Cynric", "Ealdred", "Wigmund", "Sigeric", "Leofwine", "Eadwig", "Tostig", "Aelfweard"],
        [(1, true)] = ["Aelfgifu", "Eadgyth", "Leofrun", "Wulfrun", "Godgifu", "Eadburh", "Cyneburh", "Elfrida", "Aelfwynn", "Eadwynn", "Wynflaed", "Leofgyth", "Aldgyth", "Sigeburh", "Ealhswith"],
        [(2, false)] = ["Ottar", "Eynar", "Asgeirr", "Thorarin", "Svartur", "Kolbeinn", "Ketil", "Hrafn", "Orm", "Thorvald", "Bjarni", "Grimkell", "Skeggi", "Hallvard", "Ulfar", "Vigfus", "Steinar", "Arnbjorn"],
        [(2, true)] = ["Thorunn", "Gudrun", "Ragnhild", "Sigrid", "Astrid", "Thordis", "Gunnhild", "Hallveig", "Jorunn", "Vigdis", "Asdis", "Ingunn", "Herdis", "Sigrun", "Ulfhild"],
        [(3, false)] = ["Cormac", "Niall", "Fiachra", "Oisin", "Ruadhan", "Lorcan", "Cathal", "Donnchadh", "Fergal", "Ronan", "Tadhg", "Colm", "Senan", "Eoghan", "Flannan", "Odhran"],
        [(3, true)] = ["Aoife", "Eithne", "Niamh", "Sadhbh", "Orlaith", "Roisin", "Muirin", "Clodagh", "Saoirse", "Ailbhe", "Caoimhe", "Blathnaid", "Doireann", "Fionnait", "Ornait"],
    };

    private static string[] LoreNames(int realm, bool female) =>
        LoreNameLists.TryGetValue((realm, female), out string[] names) ? names : [];

    #endregion

    /// <summary>Reads the server's invalid-names file (config/invalidnames.txt): parts a name may not contain, and /regex/ lines.</summary>
    public static Func<string, bool> LoadRejected(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;
        var contains = new List<string>();
        var patterns = new List<Regex>();
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim().TrimStart('﻿');
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            if (line.Length > 2 && line.StartsWith('/') && line.EndsWith('/'))
            {
                try { patterns.Add(new Regex(line[1..^1], RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)); }
                catch (ArgumentException) { }
            }
            else
                contains.Add(line.ToLowerInvariant());
        }
        return name =>
        {
            string lower = name.ToLowerInvariant();
            return contains.Any(part => lower.Contains(part, StringComparison.Ordinal)) || patterns.Any(regex => regex.IsMatch(name));
        };
    }

    private static string Capitalize(string name) =>
        string.IsNullOrEmpty(name) ? name : char.ToUpperInvariant(name[0]) + name[1..].ToLowerInvariant();

    private static T Pick<T>(IReadOnlyList<T> items, Random random) => items[random.Next(items.Count)];
}
