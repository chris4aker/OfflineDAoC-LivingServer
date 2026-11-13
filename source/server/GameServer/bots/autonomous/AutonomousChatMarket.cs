using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS
{
    /// <summary>
    /// Visible replies to /lfg and /trade posts (1.1.3, after RC5). On the owner's PC (RC3, 2026-11-09) 260 /lfg posts and 213
    /// /trade posts drew no reply at all: the channels read as monologues. In 2003 most of this went by /send, so most posts stay
    /// unanswered here too, but a share get a short public answer from a bot it is true for: "x" or "still need a healer?" from a
    /// solo bot of the role and levels asked for, "how much?" from a bot that could wear the armour, "/t me, have one" from a bot
    /// that carries what is wanted. The poster then says what is true of it: its real spec, its asking price, "ya, 1 spot" only
    /// while its party is still recruiting (<see cref="SpotFor"/>), and a while later "nvm full", "got a grp" or "sold" only
    /// when that happened. Pure; the coordinator (AutonomousBotChatCoordinator.ScheduleMarketReply) reads the live bots and
    /// plays the lines under the channel's ceiling.
    /// </summary>
    public static class AutonomousChatMarket
    {
        public enum Post { None, Lfm, Lfg, Wts, Wtb }

        /// <summary>What a field post is (its kind as AutonomousChatFieldTalk wrote it).</summary>
        public static Post KindOf(string kind) => kind switch
        {
            "lfm" => Post.Lfm,
            "lfg" => Post.Lfg,
            "wts" => Post.Wts,
            "wtb" => Post.Wtb,
            _ => Post.None,
        };

        /// <summary>
        /// What a player's /trade post is: a sale ("wts 99% cap", "selling ...") or a wanted post ("wtb", "buying", "anyone selling",
        /// "looking for a ..."), else none (RC6 on the owner's PC: the player's "wts 99% arcane cloth cap, cheap" drew nothing). Pure.
        /// </summary>
        public static Post PostOf(string text)
        {
            string t = " " + AutonomousChatQuestions.Normalize(text ?? string.Empty) + " ";
            if (Regex.IsMatch(t, @" (anyone|any1|anybody|who s|whos) (selling|sell|got|have) "))
                return Post.Wtb;
            if (Regex.IsMatch(t, @" (wts|selling|for sale|fs) "))
                return Post.Wts;
            if (Regex.IsMatch(t, @" (wtb|buying|anyone selling|any1 selling|anyone sell|looking for a|looking for an|lf a|lf an|need a|need an|need some) "))
                return Post.Wtb;
            return Post.None;
        }

        /// <summary>The share of posts of a kind that get a public reply (most go by /send, as in 2003).</summary>
        public static double ReplyShare(Post post) => post switch
        {
            Post.Lfm => 0.45,
            Post.Lfg => 0.3,
            Post.Wts => 0.4,
            Post.Wtb => 0.35,
            _ => 0,
        };

        /// <summary>How likely the poster later says what became of it ("nvm full", "sold"), when that is true.</summary>
        public const double ClosingShare = 0.6;

        /// <summary>When the poster's closing comes: two to four minutes after the post.</summary>
        public static int ClosingAfterMs(Random random) => 120_000 + random.Next(120_000);

        /// <summary>What a group post asks for: the role (null for anyone) and the levels.</summary>
        public sealed record Need(string Role, int Low, int High);

        /// <summary>"lf1m healer barrows 31-37" asks for a healer of 31-37; "lfm keltoi" for anyone within three levels. Pure.</summary>
        public static Need NeedOf(string text, int posterLevel)
        {
            string t = " " + AutonomousChatQuestions.Normalize(text ?? string.Empty) + " ";
            string role = Regex.IsMatch(t, " (heal|healer|heals) ") ? "healer" : Regex.IsMatch(t, " tank ") ? "tank" :
                Regex.IsMatch(t, " (cc|mezzer|mez) ") ? "cc" : Regex.IsMatch(t, " dps ") ? "dps" : Regex.IsMatch(t, " caster ") ? "caster" :
                Regex.IsMatch(t, " puller ") ? "puller" : Regex.IsMatch(t, " support ") ? "support" : null;
            Match band = Regex.Match(text ?? string.Empty, @"\b(\d{1,2})-(\d{1,2})\b");
            if (band.Success && int.TryParse(band.Groups[1].Value, out int low) && int.TryParse(band.Groups[2].Value, out int high) && low <= high)
                return new Need(role, low, high);
            return new Need(role, Math.Max(1, posterLevel - 3), Math.Min(50, posterLevel + 3));
        }

        /// <summary>Whether a class fills a role a group post asks for (caster and support included). Pure.</summary>
        public static bool Fills(string className, bool isHealer, string role) => role switch
        {
            null => true,
            "caster" => AutonomousChatFieldTalk.ArmorOf(className) == "cloth",
            "support" => isHealer || AutonomousChatFieldTalk.Fills(className, "cc", isHealer),
            "healer" => isHealer || AutonomousChatFieldTalk.Fills(className, "healer", isHealer),
            _ => AutonomousChatFieldTalk.Fills(className, role, isHealer),
        };

        // The armour words a sale may name, as the class armour they are.
        private static readonly (string Word, string Armor)[] ArmorWords =
            [("plate", "plate"), ("chain", "chain"), ("scale", "chain"), ("studded", "studded"), ("reinforced", "studded"), ("leather", "leather"),
                ("cloth", "cloth"), ("robe", "cloth")];

        /// <summary>The class armour an item names ("99% chain legs" is chain), or null for one that names none. Pure.</summary>
        public static string ArmorNamed(string text)
        {
            string t = " " + AutonomousChatQuestions.Normalize(text ?? string.Empty) + " ";
            return ArmorWords.FirstOrDefault(a => t.Contains(" " + a.Word + " ", StringComparison.Ordinal)).Armor;
        }

        // What a wtb post wants, as the word an item's name would carry ("pots" is a potion).
        private static readonly (string Word, string InName)[] Wanted =
        [
            ("helm", "helm"), ("gloves", "glove"), ("boots", "boot"), ("sleeves", "sleeve"), ("legs", "leg"), ("vest", "vest"), ("cloak", "cloak"),
            ("ring", "ring"), ("bracers", "bracer"), ("necklace", "necklace"), ("belt", "belt"), ("pots", "potion"), ("arrows", "arrow"), ("dye", "dye"),
        ];

        /// <summary>The word an item that a wtb post wants carries in its name ("wtb chain helm" wants a "helm"), or null. Pure.</summary>
        public static string WantedInName(string text)
        {
            string t = " " + AutonomousChatQuestions.Normalize(text ?? string.Empty) + " ";
            return Wanted.FirstOrDefault(w => t.Contains(" " + w.Word + " ", StringComparison.Ordinal)).InName;
        }

        /// <summary>
        /// Whether a bot may answer a post at all: a group post by a solo bot of the role and levels asked for (an lfg post also by
        /// a bot of about the same level), a sale by a bot near the item's level that wears its armour, a wanted post by anyone.
        /// Pure.
        /// </summary>
        public static bool CanReply(Post post, string postText, int posterLevel, AutonomousChatFieldTalk.Facts replier, bool solo)
        {
            if (replier == null)
                return false;
            switch (post)
            {
                case Post.Lfm:
                {
                    Need need = NeedOf(postText, posterLevel);
                    return solo && replier.Level >= need.Low && replier.Level <= need.High && Fills(replier.ClassName, replier.IsHealer, need.Role);
                }
                case Post.Lfg:
                    return Math.Abs(replier.Level - posterLevel) <= 3;
                case Post.Wts:
                {
                    if (replier.Level < posterLevel - 8 || replier.Level > posterLevel + 5)
                        return false;
                    string armor = ArmorNamed(postText);
                    return armor == null || armor == AutonomousChatFieldTalk.ArmorOf(replier.ClassName);
                }
                case Post.Wtb:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// A reply and what it asks of the poster ("price", "room", "spec", "have", "level", or null for nothing), and for a "room"
        /// question the role it asks about ("still need a healer?": "healer"), or null for a seat of any kind.
        /// </summary>
        public readonly record struct Reply(string Text, string Asks, string Role = null);

        /// <summary>
        /// A bot's reply to a post, from its own state: <paramref name="solo"/> whether it is without a group, <paramref name="hasWanted"/>
        /// whether it carries what a wtb post wants. Pure.
        /// </summary>
        /// 1.1.3, after RC6: an xp question ("how many bubbles?") only to an lfg post for xp by a poster under 50 (<paramref name="posterLevel"/>;
        /// 0 reads the level from the post): "lfg rvr 50 friar" drew "how many bubbles?".
        public static Reply ReplyTo(Post post, string postText, AutonomousChatFieldTalk.Facts replier, bool solo, bool hasWanted, Random random,
            int posterLevel = 0)
        {
            string cls = AutonomousChatFieldTalk.ShortClass(replier?.ClassName, random), lvl = replier?.Level.ToString() ?? "";
            string role = NeedOf(postText, replier?.Level ?? 1).Role;
            switch (post)
            {
                case Post.Lfm:
                    return random.Next(6) switch
                    {
                        0 => new Reply("x", null),
                        1 => new Reply("x me", null),
                        2 => new Reply($"{lvl} {cls} x", null),
                        3 => role is null or "support" ? new Reply("still need ppl?", "room") : new Reply($"still need a {role}?", "room", role),
                        4 => new Reply($"{cls} {lvl}, room?", "room"),
                        _ => new Reply("/t me", null),
                    };
                case Post.Lfg:
                    return solo
                        ? random.Next(3) switch
                        {
                            0 => new Reply("same lol", null),
                            1 => new Reply($"same, {lvl} {cls} lfg too", null),
                            _ => new Reply("me too, duo?", "duo"),
                        }
                        : random.Next(2) == 0 || !AsksXp(postText, posterLevel) ? new Reply("what spec?", "spec") : new Reply("how many bubbles?", "bubbles");
                case Post.Wts:
                    return random.Next(5) switch
                    {
                        0 => new Reply("how much?", "price"),
                        1 => new Reply("pst price?", "price"),
                        2 => new Reply("price?", "price"),
                        3 => new Reply("still have it?", "have"),
                        _ => new Reply("what stats?", "stats"),
                    };
                case Post.Wtb:
                    if (hasWanted)
                        return random.Next(2) == 0 ? new Reply("/t me, have one", "paying") : new Reply("have one, how much u paying?", "paying");
                    return random.Next(2) == 0 ? new Reply("how much u paying?", "paying") : new Reply("what lvl?", "level");
                default:
                    return new Reply(null, null);
            }
        }

        /// <summary>"lfg rvr 50 friar", "lf rr grp", "lfg keep take": a post for the frontier, not for xp. Pure.</summary>
        public static bool IsRvrPost(string text)
        {
            string t = " " + AutonomousChatQuestions.Normalize(text ?? string.Empty) + " ";
            return Regex.IsMatch(t, " (rvr|rr|rps|keep|keeps|relic|relics|frontier|bg|battleground|siege|zerg|8v8|fg rvr) ");
        }

        /// <summary>The level a group post names ("lfg 38 sm": 38), or 0. Pure.</summary>
        public static int LevelNamed(string text)
        {
            Match level = Regex.Match(text ?? string.Empty, @"(?<![\w-])([1-9]|[1-4]\d|50)(?![\w%-])");
            return level.Success ? int.Parse(level.Value) : 0;
        }

        /// <summary>
        /// Whether an xp question ("how many bubbles?", "what lvl?", "xp?") fits a post (1.1.3, after RC6): not one for the frontier
        /// and not one from a 50 (<paramref name="posterLevel"/>, or the level the post names when 0), who has no bubbles left. Pure.
        /// </summary>
        public static bool AsksXp(string postText, int posterLevel = 0)
        {
            if (IsRvrPost(postText))
                return false;
            int level = posterLevel > 0 ? posterLevel : LevelNamed(postText);
            return level is 0 or < 50;
        }

        /// <summary>
        /// Whether a bot party has a seat for the role a reply asks about (1.1.3, after RC5): a party is formed at its full size and
        /// never backfilled, so only one still recruiting (assembling with free seats, AutonomousBotGroupCoordinator.RecruitingPost)
        /// has one at all, and "still need a healer?" is yes only while no member already fills that role. A seat of any kind (null,
        /// "dps", "caster", "puller") needs only the free seat. <paramref name="roster"/> is the party's members. Pure.
        /// </summary>
        public static bool SpotFor(string role, bool recruiting, IEnumerable<(string ClassName, bool IsHealer)> roster)
        {
            if (!recruiting)
                return false;
            if (role is not ("healer" or "tank" or "cc"))
                return true;
            return roster == null || !roster.Any(member => Fills(member.ClassName, member.IsHealer, role));
        }

        /// <summary>
        /// The poster's answer to a reply, true of it now: its asking price (the one the post named, or one for its level), "ya, 1
        /// spot" only when <paramref name="groupHasRoom"/> (<see cref="SpotFor"/>; <paramref name="roleFilled"/> is the role asked
        /// about when the party still recruits but already has it), its real spec, whether it still has the item. Null for none. Pure.
        /// </summary>
        public static string PosterAnswer(string asks, Post post, string postText, AutonomousChatFieldTalk.Facts poster, string spec, bool groupHasRoom,
            bool stillHas, Random random, string roleFilled = null)
        {
            if (asks == null || poster == null)
                return random.NextDouble() < 0.4 ? Pick(random, "/t me", "pst", "k pst") : null;
            string named = Regex.Match(postText ?? string.Empty, @"\b\d+ ?(p|g|s)\b").Value;
            switch (asks)
            {
                case "room":
                    if (groupHasRoom)
                        return Pick(random, "ya, 1 spot", "ya, pst", "ya, still room");
                    // Still recruiting, but the role asked about is filled: "got a healer already", not "full".
                    return roleFilled != null ? Pick(random, $"got a {roleFilled} already, sry", $"nah, have a {roleFilled}")
                        : Pick(random, "nope full, sry", "just filled, sry");
                case "price":
                    if (!stillHas)
                        return Pick(random, "sold sry", "just sold it");
                    return named.Length > 0 ? Pick(random, $"{named}, like i said", $"{named} obo", $"{named} firm") : Pick(random, "make an offer", "offers, pst");
                case "have":
                    return stillHas ? Pick(random, "ya", "ya still have it", "yep, pst") : Pick(random, "nope sold", "sold sry");
                case "stats":
                    return stillHas ? Pick(random, "/t me, ill link it", "pst, ill show u") : Pick(random, "sold sry");
                case "spec":
                    return string.IsNullOrWhiteSpace(spec) ? null : spec;
                case "bubbles":
                    return poster.Bubbles > 0 && poster.Level < 50 ? $"{poster.Bubbles} to {poster.Level + 1}" : null;
                case "duo":
                    return Pick(random, "sure, pst", "k, where u at?", "sure");
                case "paying":
                    return Pick(random, "make an offer", "pst, depends on stats", "offers, pst");
                case "level":
                    return post == Post.Wtb ? Pick(random, $"for {poster.Level}", $"{poster.Level}", $"{poster.Level}, any quality") : null;
                default:
                    return null;
            }
        }

        /// <summary>
        /// What the poster says a few minutes on, only when it is true then: a group post's "nvm full" once the group is full, an
        /// lfg post's "got a grp ty" once the bot is grouped, a sale's "sold" once the item is gone. Null otherwise. Pure.
        /// </summary>
        /// 1.1.3 (after RC5): <paramref name="stoppedShort"/> is a bot party that stopped recruiting before it was full (it set
        /// off as it was, AutonomousBotGroupCoordinator.RecruitingPost): it says so, not "full".
        public static string Closing(Post post, bool groupFull, bool grouped, bool stillHas, Random random, bool stoppedShort = false) => post switch
        {
            Post.Lfm when groupFull => Pick(random, "nvm full", "full ty", "full now, ty all", "nm full"),
            Post.Lfm when stoppedShort => Pick(random, "nm, heading out", "nvm, going as is", "nm, we're off"),
            Post.Lfg when grouped => Pick(random, "nvm got a grp", "got a grp ty", "nm found one"),
            Post.Wts when !stillHas => Pick(random, "sold ty", "sold", "nvm sold"),
            _ => null,
        };

        private static string Pick(Random random, params string[] options) => options[random.Next(options.Length)];
    }
}
