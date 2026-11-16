using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using DOL.GS.Crafting;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>
    /// The crafter's side of a commerce conversation with a real player (Crafting 2.0 WP8), pure: from the crafter's real
    /// facts (skill, title, what it makes surely, the stock in its bag, when its next break is), the customer's standing
    /// and the deal on the table, it answers one line of the player's and says what to do (quote, haggle, place the order,
    /// wait for the coin, open a trade window, report the orders, cancel). Every fact in an answer comes from the inputs;
    /// the glue (<see cref="CrafterCommerce"/>) may say it with a corpus line of the same topic and slots (craft_reply).
    /// </summary>
    public static class CrafterTalk
    {
        /// <summary>A crafted piece the crafter holds and would sell (its own work, not held for an order).</summary>
        public sealed record StockPiece(string ObjectId, string Name, int Quality, int Level, long Price, int ObjectType, int ItemType);

        public sealed record CrafterFacts(string Name, eRealm Realm, string Skills, eCraftingSkill Primary, int Skill, string Title,
            IReadOnlyList<CraftRecipeInfo> Makeable, IReadOnlyList<StockPiece> Stock, double MinutesToBreak, bool AtBreak,
            int SecondsPerPiece, string Zone, int Patience);

        public sealed record CustomerFacts(string Id, string Name, CrafterCustomers.Standing Standing, int Discount, bool Guildmate,
            bool Near, long SpentCopper = 0)
        {
            public bool Friendly => Standing is CrafterCustomers.Standing.Regular or CrafterCustomers.Standing.Friend || Guildmate;
        }

        public enum DealKind { Order, Set, Stock }

        /// <summary>The deal on the table: what, at what price, and how far the haggle got.</summary>
        public sealed record Deal(DealKind Kind, IReadOnlyList<CraftRecipeInfo> Recipes, StockPiece Piece, long Ask, long Materials,
            long Fee, int Round, bool UpFront, DateTime UntilUtc)
        {
            public string ItemText => Kind == DealKind.Stock ? Piece?.Name?.ToLowerInvariant() ?? "it"
                : Kind == DealKind.Set ? $"{Recipes.Count}-piece set"
                : Recipes.FirstOrDefault()?.ProductName?.ToLowerInvariant() ?? "it";
        }

        /// <summary>One of the player's orders on the board, as the crafter sees it.</summary>
        public sealed record OrderView(string OrderId, string ProductName, CraftOrderState State, long Copper, bool PayUpFront);

        public enum Act { None, Summary, Quote, Haggle, PlaceOrder, AwaitDeposit, OpenTrade, Status, Cancel, Declined, Cannot, Refuse }

        /// <summary>The answer: the fixed text (all facts), what to do, the deal now on the table, and the corpus topic and slots.</summary>
        public sealed record Reply(string Text, Act Act, Deal Deal, string Topic, IReadOnlyDictionary<string, string> Slots);

        /// <summary>How long a quote stands.</summary>
        public static readonly TimeSpan DealLifetime = TimeSpan.FromMinutes(15);

        private static readonly Regex AsksWhatRegex = new(
            @"\b(what (do|can|d) (you|u|ya) (make|craft|sell|do|have)|what (are|r) (you|u) (selling|making)|wh?at(cha| ya| you| u) (got|selling|make)|" +
            @"what (have|do) (you|u) got|any(thing)? (for sale|in stock)|what('?s| is) for sale|(you|u) (selling|got) anything|what (can|could) (you|u) do for me|" +
            @"what (is|s) (ur|your) (trade|craft)|(ur|your) (prices|stock))\b", RegexOptions.Compiled);

        private static readonly Regex StatusRegex = new(
            @"^(ready|ready yet|done|done yet)\W*$|\b(is it (done|ready)|(done|ready) yet|status|(where|wheres|where s) (is )?my|how (long|much longer)|my order|(it|they) done|are (they|you) done)\b",
            RegexOptions.Compiled);

        private static readonly Regex CancelRegex = new(@"\b(cancel|call it off|forget (it|the order)|never ?mind the order|dont make)\b", RegexOptions.Compiled);

        private static readonly Regex RequestVerb = new(
            @"\b(wtb|lf|lfm|need|needs|want|looking for|can (you|u|ya) (make|craft|do)|could (you|u|ya) (make|craft|do)|(make|craft) me|" +
            @"do (you|u) (have|sell|make)|(you|u) (have|got|sell|make) (a|an|any)|how much (for|is)|price (on|for|check)|selling (a|an|any)|commission)\b",
            RegexOptions.Compiled);

        /// <summary>"can you make it to the raid", "u make it tonight?": making it somewhere, not making a piece.</summary>
        private static readonly Regex MakeItRegex = new(@"\bmake it\b(?! for)", RegexOptions.Compiled);

        private static readonly Regex HaggleWords = new(@"\b(too much|too high|too steep|too expensive|lower|cheaper|discount|any lower|best price|come down)\b",
            RegexOptions.Compiled);

        public static bool AsksWhatYouMake(string message) => AsksWhatRegex.IsMatch(Norm(message));

        public static bool AsksStatus(string message) => StatusRegex.IsMatch(Norm(message));

        /// <summary>A line that asks for a piece ("wtb ...", "can you make ...", "need a ...", or one naming armor/slot/craft words).</summary>
        public static bool AsksForPiece(string message)
        {
            string t = Norm(message);
            if (MakeItRegex.IsMatch(t))
                return false;
            if (RequestVerb.IsMatch(t) || AutonomousChatIntentModel.IsCraftRequest(t) && !AsksWhatYouMake(t))
                return true;
            CrafterQuotes.Request request = CrafterQuotes.Parse(t);
            return (request.ObjectType != null && (request.Slot != null || request.Set)) ||
                   Regex.IsMatch(t, @"\b(wts|selling)\b") && request.Names;
        }

        private static string Norm(string text) => Regex.Replace((text ?? string.Empty).ToLowerInvariant().Replace('’', '\''), @"\s+", " ").Trim();

        /// <summary>Gold, silver, copper as players wrote it ("1g 50s", "75s", "40c").</summary>
        public static string PriceText(long copper) => BotCrafterVoice.PriceText(copper);

        private static string CraftName(eCraftingSkill craft) => craft switch
        {
            eCraftingSkill.WeaponCrafting => "weaponcraft",
            eCraftingSkill.ArmorCrafting => "armorcraft",
            eCraftingSkill.Tailoring => "tailoring",
            eCraftingSkill.Fletching => "fletching",
            eCraftingSkill.SpellCrafting => "spellcraft",
            eCraftingSkill.Alchemy => "alchemy",
            eCraftingSkill.SiegeCrafting => "siegecraft",
            _ => "crafting",
        };

        /// <summary>
        /// The crafter's answer to one line of the player's, or null when the line is not about trade with it (the tell
        /// then takes its ordinary reply). <paramref name="open"/> is the deal still on the table, if any. Pure.
        /// </summary>
        public static Reply Answer(CrafterFacts crafter, CustomerFacts customer, string message, Deal open,
            IReadOnlyList<OrderView> orders, DateTime utcNow)
        {
            if (crafter == null || customer == null || string.IsNullOrWhiteSpace(message))
                return null;
            string t = Norm(message);
            if (open != null && open.UntilUtc <= utcNow)
                open = null;
            orders ??= [];
            bool trade = open != null || orders.Count > 0 || AsksWhatYouMake(t) || AsksForPiece(t) || AsksStatus(t) && orders.Count > 0;
            if (!trade)
                return null;
            var slots = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["customer"] = customer.Name ?? string.Empty,
                ["title"] = crafter.Title ?? string.Empty,
                ["skill"] = crafter.Skill.ToString(),
                ["recipe"] = CraftName(crafter.Primary),
                ["zone"] = crafter.Zone ?? string.Empty,
            };
            if (customer.Standing == CrafterCustomers.Standing.Refused)
                return new Reply("not dealing with you, sorry", Act.Refuse, null, "craft_refuse", slots);

            // An order already on the board: its status, or cancelling it.
            if (orders.Count > 0 && CancelRegex.IsMatch(t))
            {
                OrderView cancel = orders.FirstOrDefault(o => o.State == CraftOrderState.Open);
                if (cancel == null)
                    return new Reply("too late to cancel, its made already. come get it at my break", Act.Status, open, "craft_status", slots);
                slots["item"] = cancel.ProductName.ToLowerInvariant();
                return new Reply(cancel.PayUpFront ? $"ok, cancelled the {slots["item"]}, coin is back in your purse" : $"ok, cancelled the {slots["item"]}",
                    Act.Cancel, null, "craft_cancelled", slots);
            }
            if (orders.Count > 0 && AsksStatus(t) && open == null)
                return StatusReply(crafter, customer, orders, slots);

            // The deal on the table: yes, no, an offer.
            if (open != null)
            {
                long? offer = CrafterHaggle.ParseCopper(t, open.Ask);
                bool? yes = offer == null && !HaggleWords.IsMatch(t) ? AutonomousChatThreads.SaysYes(t) : null;
                if (yes == true)
                    return AcceptReply(crafter, customer, open, slots);
                if (yes == false)
                    return new Reply("np, let me know", Act.Declined, null, "craft_declined", slots);
                if (offer != null || HaggleWords.IsMatch(t))
                    return HaggleReply(crafter, customer, open, offer ?? open.Ask * 80 / 100, slots, utcNow);
            }

            if (AsksWhatYouMake(t) && !CrafterQuotes.Parse(t).Names)
                return SummaryReply(crafter, slots);
            return AsksForPiece(t) ? RequestReply(crafter, customer, t, slots, utcNow) : null;
        }

        private static Reply SummaryReply(CrafterFacts crafter, Dictionary<string, string> slots)
        {
            (int low, int high) = CrafterQuotes.Range(crafter.Makeable);
            IReadOnlyList<string> examples = CrafterQuotes.Examples(crafter.Makeable);
            string stock = crafter.Stock.Count == 0 ? "nothing in stock right now"
                : "in stock: " + string.Join(", ", crafter.Stock.Take(3).Select(p => $"{p.Name.ToLowerInvariant()} {p.Quality}% {PriceText(p.Price)}"));
            slots["level"] = high.ToString();
            slots["item"] = examples.Count > 0 ? string.Join(", ", examples) : CraftName(crafter.Primary);
            slots["stock"] = stock;
            string makes = examples.Count == 0
                ? $"{crafter.Title}, {crafter.Skill} {CraftName(crafter.Primary)}, still learning, nothing i can promise yet"
                : $"{crafter.Title}, {crafter.Skill} {CraftName(crafter.Primary)}. i can make {slots["item"]}" + (high > 0 ? $", up to lvl {high}" : string.Empty);
            string topic = examples.Count == 0 || high <= 0 ? null : crafter.Stock.Count > 0 ? "craft_summary_stock" : "craft_summary";
            return new Reply($"{makes}. {stock}", Act.Summary, null, topic, slots);
        }

        private static Reply RequestReply(CrafterFacts crafter, CustomerFacts customer, string t, Dictionary<string, string> slots, DateTime utcNow)
        {
            CrafterQuotes.Request request = CrafterQuotes.Parse(t);
            if (!request.Names)
                return new Reply("sure, what do you need made?", Act.None, null, "craft_ask_what", slots);
            // Stock first: a piece it holds that answers the request.
            StockPiece piece = crafter.Stock
                .Select(p => (Piece: p, Score: CrafterQuotes.Score(request, new CraftRecipeInfo(p.ObjectId, crafter.Primary, 0, p.ObjectId, p.Name,
                    p.ObjectType, p.ItemType, p.Level, 0, true, false, Array.Empty<CraftMaterial>()))))
                .Where(c => c.Score > 0 && (request.Quality == null || c.Piece.Quality >= request.Quality))
                .OrderByDescending(c => c.Score).Select(c => c.Piece).FirstOrDefault();
            if (piece != null && !request.Set)
            {
                long ask = piece.Price;
                long fee = ask / 5;
                var deal = new Deal(DealKind.Stock, [], piece, ask, ask - fee, fee, 0, false, utcNow + DealLifetime);
                slots["stock"] = piece.Name.ToLowerInvariant();
                slots["item"] = slots["stock"];
                slots["quality"] = piece.Quality.ToString();
                slots["price"] = PriceText(ask);
                string where = crafter.AtBreak && customer.Near ? "want it? say yes and ill open a trade"
                    : crafter.AtBreak ? $"im in {crafter.Zone} on a break, come find me and say yes"
                    : $"out hunting, catch me at my break in {CrafterQuotes.TimeText((int)Math.Ceiling(crafter.MinutesToBreak))}";
                slots["terms"] = where;
                return new Reply($"got a {slots["stock"]} {piece.Quality}% for {slots["price"]}, {where}", Act.Quote, deal, "craft_stock", slots);
            }

            if (request.Craft is eCraftingSkill asked && asked != crafter.Primary && BotCraftingSkills.Value(crafter.Skills, asked) <= 0)
                return new Reply($"not my trade, i do {CraftName(crafter.Primary)}", Act.Cannot, null, "craft_not_mine", slots);

            IReadOnlyList<CraftRecipeInfo> recipes = request.Set && request.ObjectType != null
                ? CrafterQuotes.BestSet(request, crafter.Makeable)
                : CrafterQuotes.Best(request, crafter.Makeable) is { } one ? [one] : [];
            if (recipes.Count == 0)
            {
                string why = request.ObjectType is int type && !crafter.Makeable.Any(r => r.ObjectType == type)
                    ? $"cant make that, my {CraftName(crafter.Primary)} is {crafter.Skill}"
                    : request.Level is int lvl && CrafterQuotes.Range(crafter.Makeable).Highest < lvl - 5
                        ? $"cant make lvl {lvl} yet, my {CraftName(crafter.Primary)} is only {crafter.Skill}. lvl {CrafterQuotes.Range(crafter.Makeable).Highest} is my best"
                        : $"cant make that, sorry. my {CraftName(crafter.Primary)} is {crafter.Skill}";
                if (request.Quality is int q && q > 0 && crafter.Makeable.Count > 0 && why.StartsWith("cant make that, sorry", StringComparison.Ordinal))
                    why += ", and i cant promise a quality";
                slots["level"] = CrafterQuotes.Range(crafter.Makeable).Highest.ToString();
                return new Reply(why, Act.Cannot, null, why.Contains("lvl", StringComparison.Ordinal) ? "craft_cant_level" : "craft_cant", slots);
            }

            CrafterQuotes.Offer[] offers = recipes.Select(r => CrafterQuotes.Price(r, crafter.Skills, customer.Discount, customer.Guildmate)).ToArray();
            long materials = offers.Sum(o => o.Materials), fees = offers.Sum(o => o.Fee);
            long price = materials + fees;
            bool upFront = CrafterCustomers.WantsUpFront(customer.Standing, crafter.Patience, customer.Guildmate);
            int minutes = CrafterQuotes.MinutesUntilReady(crafter.MinutesToBreak, crafter.SecondsPerPiece, recipes.Count);
            var newDeal = new Deal(recipes.Count > 1 ? DealKind.Set : DealKind.Order, recipes, null, price, materials, fees, 0, upFront,
                utcNow + DealLifetime);
            slots["item"] = recipes.Count > 1 ? $"{recipes.Count}-piece {recipes[0].ProductName.Split(' ')[0].ToLowerInvariant()} set"
                : recipes[0].ProductName.ToLowerInvariant();
            slots["level"] = recipes.Max(r => r.ProductLevel).ToString();
            slots["price"] = PriceText(price);
            slots["when"] = CrafterQuotes.TimeText(minutes);
            slots["count"] = recipes.Count.ToString();
            slots["terms"] = upFront ? "coin up front" : customer.Guildmate ? "materials only for guild" : "pay when i hand it over";
            string missing = request.Set && recipes.Count < CrafterQuotes.SetSlots.Length ? $" (only {recipes.Count} of the 6 pieces at my skill)" : string.Empty;
            string quality = request.Quality is int wanted && wanted > 0 ? ", quality is whatever the forge gives, cant promise " + wanted + "%" : string.Empty;
            string text = $"can make the {slots["item"]} (lvl {slots["level"]}){missing} for {slots["price"]}, ready in {slots["when"]}, {slots["terms"]}{quality}. deal?";
            // A caveat (a quality it cannot promise, a set it cannot finish) is said in the fixed words, never left out by a corpus line.
            string topic = quality.Length > 0 || missing.Length > 0 ? null : recipes.Count > 1 ? "craft_quote_set" : "craft_quote";
            return new Reply(text, Act.Quote, newDeal, topic, slots);
        }

        private static Reply AcceptReply(CrafterFacts crafter, CustomerFacts customer, Deal open, Dictionary<string, string> slots)
        {
            slots["item"] = open.ItemText;
            slots["price"] = PriceText(open.Ask);
            if (open.Kind == DealKind.Stock)
            {
                if (crafter.AtBreak && customer.Near)
                    return new Reply($"ok, opening a trade. {slots["price"]} for the {slots["item"]}", Act.OpenTrade, open, "craft_trade_open", slots);
                slots["terms"] = crafter.AtBreak ? $"im in {crafter.Zone}, come find me" : "catch me at my next break";
                return new Reply($"sure, {slots["terms"]} and ill trade it to you", Act.Quote, open, "craft_stock_meet", slots);
            }
            int minutes = CrafterQuotes.MinutesUntilReady(crafter.MinutesToBreak, crafter.SecondsPerPiece, open.Recipes.Count);
            slots["when"] = CrafterQuotes.TimeText(minutes);
            if (open.UpFront)
                return new Reply($"deal, {slots["price"]} up front. drop the coin on me when youre near and ill start at my next break",
                    Act.AwaitDeposit, open, "craft_deposit", slots);
            return new Reply($"deal. ill make the {slots["item"]} at my next break, ready in {slots["when"]}. pay when i hand it over",
                Act.PlaceOrder, open, "craft_deal", slots);
        }

        private static Reply HaggleReply(CrafterFacts crafter, CustomerFacts customer, Deal open, long offer, Dictionary<string, string> slots, DateTime utcNow)
        {
            int round = open.Round + 1;
            long floor = open.Kind == DealKind.Stock ? Math.Max(1, open.Ask * 3 / 4)
                : CrafterHaggle.Floor(open.Materials, open.Fee, customer.Standing == CrafterCustomers.Standing.Stiffed);
            CrafterHaggle.Result result = CrafterHaggle.Answer(open.Ask, offer, floor, round, customer.Friendly, crafter.Patience);
            slots["item"] = open.ItemText;
            slots["budget"] = PriceText(offer);
            Deal next = open with { Round = round, UntilUtc = utcNow + DealLifetime };
            switch (result.Verdict)
            {
                case CrafterHaggle.Verdict.Accept:
                    next = next with { Ask = result.Price, Fee = Math.Max(0, result.Price - open.Materials) };
                    slots["price"] = PriceText(result.Price);
                    return new Reply($"ok, {slots["price"]} for the {slots["item"]}. deal?", Act.Haggle, next, "craft_haggle_accept", slots);
                case CrafterHaggle.Verdict.Counter:
                    next = next with { Ask = result.Price, Fee = Math.Max(0, result.Price - open.Materials) };
                    slots["price"] = PriceText(result.Price);
                    return new Reply(customer.Friendly ? $"for you, {slots["price"]}. deal?" : $"{slots["price"]}, lowest i go", Act.Haggle, next,
                        "craft_haggle_counter", slots);
                case CrafterHaggle.Verdict.Refuse:
                    slots["price"] = PriceText(open.Ask);
                    return new Reply($"cant go that low, materials alone are {PriceText(open.Materials)}. {slots["price"]}", Act.Haggle, next,
                        "craft_haggle_refuse", slots);
                default:
                    slots["price"] = PriceText(open.Ask);
                    return new Reply($"done haggling, its {slots["price"]} or nothing", Act.Haggle, next with { Round = CrafterHaggle.MaxRounds + 1 },
                        "craft_haggle_walk", slots);
            }
        }

        private static Reply StatusReply(CrafterFacts crafter, CustomerFacts customer, IReadOnlyList<OrderView> orders, Dictionary<string, string> slots)
        {
            OrderView made = orders.FirstOrDefault(o => o.State == CraftOrderState.Made);
            slots["item"] = (made ?? orders[0]).ProductName.ToLowerInvariant();
            slots["count"] = orders.Count.ToString();
            if (made != null)
            {
                if (crafter.AtBreak && customer.Near)
                    return new Reply($"your {slots["item"]} is done, here you go", Act.Status, null, "craft_ready_here", slots);
                slots["when"] = crafter.AtBreak ? "now" : CrafterQuotes.TimeText((int)Math.Ceiling(crafter.MinutesToBreak));
                return new Reply(crafter.AtBreak ? $"your {slots["item"]} is done, im in {crafter.Zone}, come get it"
                    : $"your {slots["item"]} is done, ill be in town at my break in {slots["when"]}", Act.Status, null, "craft_ready", slots);
            }
            int minutes = CrafterQuotes.MinutesUntilReady(crafter.MinutesToBreak, crafter.SecondsPerPiece, orders.Count);
            slots["when"] = CrafterQuotes.TimeText(minutes);
            return new Reply(orders.Count == 1 ? $"not yet, ill make the {slots["item"]} at my next break, about {slots["when"]}"
                : $"not yet, {orders.Count} pieces to go, about {slots["when"]}", Act.Status, null, "craft_status", slots);
        }
    }
}
