using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS.Crafting;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// What a crafter gamebot can honestly offer a real player (Crafting 2.0 WP8, design doc §8), pure: the request read
    /// from a tell or a /trade post ("wtb 99% chain hauberk lvl 23", "can you make a studded helm", "full leather set 20"),
    /// the recipes the crafter's real skill makes at the board's sure chance (<see cref="AutonomousBotCraftOrders.CanFill"/>,
    /// 84% or better), the best match, its price (the board's: materials at the merchant's price plus the crafter's fee, as
    /// a gamebot buyer pays; a regular customer pays less fee, a guildmate none) and how long it takes (its next craft
    /// break plus the work). Nothing here is invented: a crafter that cannot make it says so.
    /// </summary>
    /// <remarks>Written for this project (no fork had player commerce; the per-pair discount is an idea from mod-playerbots,
    /// no code taken).</remarks>
    public static class CrafterQuotes
    {
        /// <summary>A request as read: the item words left after the commerce words, and what the words pin down.</summary>
        public sealed record Request(string Text, IReadOnlyList<string> Words, int? Level, int? Quality, int? ObjectType, int? Slot,
            bool Set, eCraftingSkill? Craft)
        {
            /// <summary>Anything to match against a recipe (a bare "wtb" or "can you make" has nothing).</summary>
            public bool Names => Words.Count > 0 || ObjectType != null || Slot != null || Craft != null;
        }

        /// <summary>One piece the crafter can make for the player, priced.</summary>
        public sealed record Offer(CraftRecipeInfo Recipe, int Chance, long Materials, long Fee)
        {
            public long Price => Materials + Fee;
        }

        /// <summary>The six armor slots of a set, in the order a set is made.</summary>
        public static readonly int[] SetSlots =
        [
            (int)eInventorySlot.TorsoArmor, (int)eInventorySlot.LegsArmor, (int)eInventorySlot.ArmsArmor,
            (int)eInventorySlot.HeadArmor, (int)eInventorySlot.HandsArmor, (int)eInventorySlot.FeetArmor,
        ];

        public static readonly IReadOnlyDictionary<string, int> ArmorWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["cloth"] = (int)eObjectType.Cloth, ["leather"] = (int)eObjectType.Leather, ["studded"] = (int)eObjectType.Studded,
            ["chain"] = (int)eObjectType.Chain, ["plate"] = (int)eObjectType.Plate, ["reinforced"] = (int)eObjectType.Reinforced,
            ["scale"] = (int)eObjectType.Scale,
        };

        public static readonly IReadOnlyDictionary<string, int> SlotWords = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["helm"] = 21, ["helmet"] = 21, ["head"] = 21, ["cap"] = 21, ["coif"] = 21, ["hood"] = 21, ["hat"] = 21,
            ["gloves"] = 22, ["glove"] = 22, ["gauntlets"] = 22, ["hands"] = 22, ["mitts"] = 22,
            ["boots"] = 23, ["boot"] = 23, ["feet"] = 23,
            ["chest"] = 25, ["torso"] = 25, ["hauberk"] = 25, ["jerkin"] = 25, ["vest"] = 25, ["breastplate"] = 25, ["tunic"] = 25,
            ["robe"] = 25, ["body"] = 25,
            ["legs"] = 27, ["leggings"] = 27, ["pants"] = 27, ["greaves"] = 27, ["leg"] = 27,
            ["arms"] = 28, ["sleeves"] = 28, ["arm"] = 28, ["vambraces"] = 28,
        };

        private static readonly (string[] Words, eCraftingSkill Craft)[] CraftWords =
        [
            (["sc", "spellcraft", "spellcrafting", "spellcrafter", "imbue", "imbued", "gems", "gem"], eCraftingSkill.SpellCrafting),
            (["alch", "alchemy", "alchemist", "potion", "potions", "dye", "dyes", "poison", "poisons", "tincture", "tinctures"], eCraftingSkill.Alchemy),
            (["fletch", "fletcher", "fletching", "bow", "bows", "arrows", "arrow", "bolts", "staff", "staves", "longbow", "crossbow"], eCraftingSkill.Fletching),
            (["tailor", "tailoring", "tailored"], eCraftingSkill.Tailoring),
            (["armorcraft", "armorcrafter", "armorsmith", "armourcraft", "ac"], eCraftingSkill.ArmorCrafting),
            (["weaponcraft", "weaponcrafter", "weaponsmith", "wc", "weapon", "weapons", "sword", "axe", "hammer", "mace", "spear",
              "blade", "blades", "dagger", "club", "flail", "falchion", "scimitar"], eCraftingSkill.WeaponCrafting),
        ];

        /// <summary>Words that carry no item: the commerce verbs, numbers' units, politeness.</summary>
        private static readonly HashSet<string> NotItemWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "wtb", "wts", "lf", "lfm", "buy", "buying", "sell", "selling", "need", "needs", "want", "wanted", "looking", "for", "a", "an",
            "the", "can", "could", "would", "will", "you", "u", "ya", "ye", "make", "made", "craft", "crafted", "crafting", "crafter",
            "me", "my", "i", "im", "pls", "plz", "please", "some", "any", "anyone", "anybody", "of", "to", "in", "at", "with", "and",
            "or", "level", "lvl", "lv", "quality", "qual", "q", "set", "full", "suit", "piece", "pieces", "do", "does", "have", "got",
            "how", "much", "is", "it", "that", "this", "one", "new", "good", "nice", "hey", "hi", "yo", "pst", "send", "tell", "price",
            "cost", "costs", "what", "whats", "you're", "youre", "ur", "there", "here", "on", "off", "up", "able", "help", "if", "so",
            "kind", "sort", "maybe", "also", "too", "just", "really", "still", "now", "today", "tonight", "asap", "soon", "cheap",
            "mine", "someone", "somebody", "who", "where", "m", "us", "we", "get", "give", "be", "are", "r", "am", "was", "per",
            "something", "anything", "stuff", "thing", "things", "sell", "sells", "hey", "hello", "thanks", "ty", "thx", "deal", "ok",
        };

        private static readonly Regex QualityRegex = new(@"\b(\d{2,3})\s*%|\b(\d{2,3})\s*(?:qual|quality|q)\b", RegexOptions.Compiled);
        private static readonly Regex LevelRegex = new(@"\b(?:lvl|lv|level|l)\s*(\d{1,2})\b|\b(\d{1,2})\s*(?:lvl|level)\b|\bfor (?:a |my )?(\d{1,2})\b", RegexOptions.Compiled);
        private static readonly Regex BareNumber = new(@"(?<![\d.%])\b(\d{1,2})\b(?![%.\dgspc])", RegexOptions.Compiled);
        private static readonly Regex WordRegex = new(@"[a-z']+", RegexOptions.Compiled);

        /// <summary>Reads a request. Pure.</summary>
        public static Request Parse(string text)
        {
            string t = (text ?? string.Empty).ToLowerInvariant();
            int? quality = null, level = null;
            Match q = QualityRegex.Match(t);
            if (q.Success && int.TryParse(q.Groups[1].Success ? q.Groups[1].Value : q.Groups[2].Value, NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out int qv) && qv is >= 70 and <= 100)
                quality = qv;
            string withoutQuality = QualityRegex.Replace(t, " ");
            Match l = LevelRegex.Match(withoutQuality);
            if (l.Success)
            {
                string v = l.Groups[1].Success ? l.Groups[1].Value : l.Groups[2].Success ? l.Groups[2].Value : l.Groups[3].Value;
                if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int lv) && lv is >= 1 and <= 51)
                    level = lv;
            }
            else if (BareNumber.Match(withoutQuality) is { Success: true } bare && int.TryParse(bare.Groups[1].Value, out int bv) && bv is >= 1 and <= 51)
                level = bv;

            var words = new List<string>();
            int? objectType = null, slot = null;
            eCraftingSkill? craft = null;
            bool set = Regex.IsMatch(t, @"\b(set|suit|full)\b");
            foreach (Match match in WordRegex.Matches(t))
            {
                string word = match.Value.Trim('\'');
                if (word.Length == 0)
                    continue;
                if (ArmorWords.TryGetValue(word, out int armor))
                    objectType ??= armor;
                if (SlotWords.TryGetValue(word, out int s))
                    slot ??= s;
                foreach ((string[] names, eCraftingSkill skill) in CraftWords)
                    if (names.Contains(word))
                        craft ??= skill;
                if (!NotItemWords.Contains(word) && word.Length > 1)
                    words.Add(Stem(word));
            }
            return new Request(text ?? string.Empty, words.Distinct().ToArray(), level, quality, objectType, slot, set, craft);
        }

        private static string Stem(string word) =>
            word.Length > 4 && word.EndsWith("es", StringComparison.Ordinal) && !word.EndsWith("ses", StringComparison.Ordinal) ? word[..^1]
            : word.Length > 3 && word.EndsWith('s') && !word.EndsWith("ss", StringComparison.Ordinal) ? word[..^1] : word;

        /// <summary>The recipes the crafter makes at the board's sure chance, for its realm (no stackables). Pure.</summary>
        public static IReadOnlyList<CraftRecipeInfo> Makeable(IEnumerable<CraftRecipeInfo> recipes, string skills, eRealm realm, bool allowNoRealm = false) =>
            (recipes ?? []).Where(recipe => recipe != null && !recipe.Stackable &&
                                            BotCraftingRules.RealmAllows(recipe.Realm, realm, allowNoRealm || recipe.Realm == 0) &&
                                            AutonomousBotCraftOrders.CanFill(skills, recipe))
                .ToArray();

        /// <summary>
        /// How well a recipe answers the request: 0 when it contradicts it (another armor type, slot, craft or a level more
        /// than 5 off), otherwise one point per named thing it has. Pure.
        /// </summary>
        public static int Score(Request request, CraftRecipeInfo recipe)
        {
            if (request == null || recipe == null)
                return 0;
            int score = 0;
            if (request.ObjectType is int type)
            {
                if (recipe.ObjectType != type)
                    return 0;
                score += 2;
            }
            if (request.Slot is int slot)
            {
                if (recipe.ItemType != slot)
                    return 0;
                score += 2;
            }
            if (request.Craft is eCraftingSkill craft)
            {
                if (recipe.Skill != craft && !(craft == eCraftingSkill.Tailoring && recipe.Skill == eCraftingSkill.Tailoring))
                    return 0;
                score += 1;
            }
            if (request.Level is int level)
            {
                int off = Math.Abs(recipe.ProductLevel - level);
                if (off > 5)
                    return 0;
                score += off <= 1 ? 2 : 1;
            }
            string name = " " + Regex.Replace((recipe.ProductName ?? string.Empty).ToLowerInvariant(), @"[^a-z']+", " ") + " ";
            int named = 0;
            foreach (string word in request.Words)
            {
                if (ArmorWords.ContainsKey(word) || SlotWords.ContainsKey(word) && request.Slot != null)
                    continue;
                if (name.Contains(" " + word + " ", StringComparison.Ordinal) || name.Contains(" " + word + "s ", StringComparison.Ordinal) ||
                    word.Length >= 5 && name.Contains(" " + word, StringComparison.Ordinal))
                    named++;
            }
            // Item words that match nothing in the name, with nothing else pinned down, are a different item.
            bool pinned = request.ObjectType != null || request.Slot != null || request.Craft != null;
            int itemWords = request.Words.Count(word => !ArmorWords.ContainsKey(word) && !SlotWords.ContainsKey(word) && !IsCraftWord(word));
            if (named == 0 && itemWords > 0 && !pinned)
                return 0;
            return score + named * 3;
        }

        private static bool IsCraftWord(string word) => CraftWords.Any(entry => entry.Words.Contains(word));

        /// <summary>
        /// The best recipe for the request among those the crafter makes, or null: the best score, then the product level
        /// nearest the asked level (else the highest the crafter makes surely), then the cheaper, then the id. Pure.
        /// </summary>
        public static CraftRecipeInfo Best(Request request, IEnumerable<CraftRecipeInfo> makeable) =>
            (makeable ?? []).Select(recipe => (Recipe: recipe, Score: Score(request, recipe)))
                .Where(candidate => candidate.Score > 0)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => request.Level is int level ? Math.Abs(candidate.Recipe.ProductLevel - level) : -candidate.Recipe.ProductLevel)
                .ThenBy(candidate => candidate.Recipe.MaterialCostCopper)
                .ThenBy(candidate => candidate.Recipe.RecipeId, StringComparer.Ordinal)
                .Select(candidate => candidate.Recipe)
                .FirstOrDefault();

        /// <summary>
        /// A set: the best piece for each of the six armor slots of the asked armor type and level (only slots the crafter
        /// makes; fewer than six is said as it is). Pure.
        /// </summary>
        public static IReadOnlyList<CraftRecipeInfo> BestSet(Request request, IEnumerable<CraftRecipeInfo> makeable)
        {
            CraftRecipeInfo[] all = (makeable ?? []).ToArray();
            var set = new List<CraftRecipeInfo>();
            foreach (int slot in SetSlots)
                if (Best(request with { Slot = slot, Set = false }, all.Where(recipe => recipe.ItemType == slot)) is { } piece)
                    set.Add(piece);
            return set;
        }

        /// <summary>
        /// The price of a piece: the board's quote (materials plus the crafter's fee, <see cref="AutonomousBotCraftOrders.Quote(long)"/>),
        /// the fee cut by the customer's discount percent; a guildmate pays the materials only (the guild bank pays the fee,
        /// AutonomousBotGuildEconomy). Pure.
        /// </summary>
        public static Offer Price(CraftRecipeInfo recipe, string skills, int discountPercent, bool guildmate)
        {
            long materials = Math.Max(0, recipe?.MaterialCostCopper ?? 0);
            long fee = AutonomousBotCraftOrders.Quote(materials) - materials;
            fee = guildmate ? 0 : fee * (100 - Math.Clamp(discountPercent, 0, 50)) / 100;
            int chance = recipe == null ? 0 : CraftRules.ChanceToMake(BotCraftingSkills.Value(skills, recipe.Skill), recipe.Level);
            return new Offer(recipe, chance, materials, fee);
        }

        /// <summary>
        /// Minutes until the pieces are ready: the crafter's next craft break, then the work, three pieces a trip and a task
        /// between trips (<see cref="BotCraftingRules.SessionsPerTrip"/>). Pure.
        /// </summary>
        public static int MinutesUntilReady(double minutesToBreak, int secondsPerPiece, int pieces, int taskMinutes = 60)
        {
            pieces = Math.Max(1, pieces);
            int trips = (pieces + BotCraftingRules.SessionsPerTrip - 1) / BotCraftingRules.SessionsPerTrip;
            double minutes = Math.Max(0, minutesToBreak) + (trips - 1) * Math.Max(1, taskMinutes) +
                             pieces * Math.Max(1, secondsPerPiece) / 60.0;
            return (int)Math.Ceiling(minutes);
        }

        /// <summary>The time as players wrote it: "a few min", "about 40 min", "an hour or so", "a couple hours".</summary>
        public static string TimeText(int minutes) => minutes switch
        {
            <= 5 => "a few min",
            < 50 => $"about {Math.Max(5, (minutes + 4) / 5 * 5)} min",
            < 80 => "an hour or so",
            < 150 => "a couple hours",
            _ => $"about {(minutes + 30) / 60} hours",
        };

        /// <summary>The level range the crafter makes surely, its highest product level first ("up to level 23"). Pure.</summary>
        public static (int Lowest, int Highest) Range(IReadOnlyList<CraftRecipeInfo> makeable) =>
            makeable == null || makeable.Count == 0 ? (0, 0) : (makeable.Min(r => r.ProductLevel), makeable.Max(r => r.ProductLevel));

        /// <summary>A few of the best pieces it makes, by name ("studded jerkin, studded helm"), highest level first.</summary>
        public static IReadOnlyList<string> Examples(IReadOnlyList<CraftRecipeInfo> makeable, int count = 3) =>
            (makeable ?? []).OrderByDescending(r => r.ProductLevel).ThenBy(r => r.ProductName, StringComparer.Ordinal)
                .Select(r => (r.ProductName ?? string.Empty).ToLowerInvariant()).Where(name => name.Length > 0).Distinct().Take(count).ToArray();
    }
}
