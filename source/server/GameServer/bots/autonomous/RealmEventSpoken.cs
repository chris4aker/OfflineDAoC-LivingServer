using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DOL.GS
{
    /// <summary>
    /// A realm notice in a player's mouth (1.1.3, after RC5). When no news line of a bot tells an event (RealmEventNotices.Pulse
    /// falls back to its written notice), a bot of the realm still says it in /faction, and the owner's PC showed it word for word in
    /// the herald's voice: "Krondon: recruiting up to 120 level-50 adventurers via Caer Diogel; at least 93 must arrive before
    /// assault." Here each written notice is read back to what it is about (a raid forming at a hub, a siege camp, a fight at a
    /// keep) and said the way a 2003 player typed it: short, lowercase, the names as chat shortened them ("krondon raid forming at
    /// diogel, need 50s"), and no head counts. The written notice itself is unchanged (it is still what a herald or the screen
    /// shows). Text only. Pure but for the random draw it is given.
    /// </summary>
    public static class RealmEventSpoken
    {
        /// <summary>What a notice is about, read from its written text, with the names it carries.</summary>
        public sealed record Reading(string Kind, string Keep = null, string Raid = null, string Hub = null, string Boss = null, string Realm = null,
            string Relic = null);

        private static readonly (Regex Pattern, string Kind)[] Rules = BuildRules();

        private static (Regex, string)[] BuildRules()
        {
            const string name = @"(?<{0}>.+?)";
            string K = string.Format(name, "keep"), R = string.Format(name, "raid"), H = string.Format(name, "hub"), B = string.Format(name, "boss"),
                W = @"(?<realm>Albion|Midgard|Hibernia)", L = string.Format(name, "relic");
            (string, string)[] rules =
            [
                // Raids (AutonomousRealmRaid, RealmEventBanter)
                ($@"^{R}: recruiting up to \d+ level-50 adventurers via {H}; at least \d+ must arrive before assault", "raid_recruit"),
                ($@"^{R}: \d+ level-50 adventurers reserved; gather at {H}\.", "raid_recruit"),
                ($@"^\d+ adventurers present at {H}; departing together for {R}\.", "raid_depart"),
                ($@"^The {R} expedition is advancing with \d+", "raid_advance"),
                ($@"^Sharpen your blades for {R}!", "raid_reminder"),
                ($@"^The muster for {R} has about twenty minutes left", "raid_reminder"),
                ($@"^Victory at {R}!", "raid_won"),
                ($@"^Word from {R}: the final foe has fallen", "raid_won"),
                ($@"^Our time at {R} is spent", "raid_timeout"),
                ($@"^The expedition to {R} must withdraw", "raid_timeout"),
                ($@"^Another realm reached the end of {R} before us", "raid_beaten"),
                ($@"^We were beaten back at {R}\.", "raid_defeated"),
                ($@"^{R} has broken our expedition", "raid_defeated"),
                ($@"^Not enough of our company reached {R} in time", "raid_failed"),
                ($@"^The expedition to {R} is called off", "raid_stopped"),
                ($@"^The expedition at {R} has ended without a confirmed victory", "raid_ended"),
                ($@"^{B} has struck down our company", "raid_wipe"),
                ($@"^We have fallen to {B}\.", "raid_wipe"),
                ($@"^\d+ of the (?<guild>.+?) have arrived at the hub and are marching to join the {R} expedition", "raid_help"),
                // Sieges (AutonomousRvrEventLayer, its Defense part, RealmEventBanter)
                ($@"^Warbands are gathering to assault {K}\. Join them at the siege camp", "siege_gather"),
                ($@"^Warbands are marching on {K}\.", "siege_march"),
                ($@"^The army marches on {K}! Form up at the siege camp", "siege_march"),
                ($@"^The assault on {K} is advancing", "siege_advance"),
                ($@"^The assault on {K} has begun!", "siege_begun"),
                ($@"^The armies at {K} are committed to battle", "siege_others"),
                ($@"^The armies at {K} have about twenty minutes left to muster", "siege_others_soon"),
                ($@"^{K} is under attack!", "keep_attacked"),
                ($@"^Scouts report {W}'s army marching on {K}!", "keep_army_coming"),
                ($@"^Our forces are attacking {K}\.", "keep_ours_attacking"),
                ($@"^Enemy armies are clashing at {K}\.", "keep_others_fighting"),
                ($@"^Fighting has broken out at {K}\.", "keep_others_fighting"),
                ($@"^{K} has fallen! Rally at the outpost", "keep_retake"),
                ($@"^The defense rally marches to relieve {K}!", "keep_relief"),
                ($@"^Stand ready at {K}!", "siege_reminder_defend"),
                ($@"^About twenty minutes remain to muster for {K}\.", "siege_reminder_attack"),
                ($@"^{K} is ours!", "keep_won"),
                ($@"^{K} has fallen to {W}\.", "keep_lost"),
                ($@"^The enemy muster at {K} has broken up", "siege_enemy_broke"),
                ($@"^The muster for {K} failed to gather", "siege_muster_failed"),
                ($@"^We held {K}!", "siege_held"),
                ($@"^Our time to take {K} has run out", "siege_failed"),
                ($@"^The fighting for {K} has ended", "siege_ended"),
                // Relics on the road (AutonomousRvrEventLayer)
                ($@"^The relic has left {K}!", "relic_road"),
                ($@"^Our time in the struggle for {L} has run out", "relic_timeout"),
                ($@"^{L} has been secured at a shrine", "relic_shrined"),
                ($@"^{L} is safe in our shrine!", "relic_ours"),
                ($@"^{W} has secured {L} at a shrine", "relic_theirs"),
            ];
            return rules.Select(rule => (new Regex(rule.Item1, RegexOptions.CultureInvariant | RegexOptions.Compiled), rule.Item2)).ToArray();
        }

        /// <summary>What a written notice is about, or null for one no rule knows. Pure.</summary>
        public static Reading Read(string notice)
        {
            if (string.IsNullOrWhiteSpace(notice))
                return null;
            string text = notice.Trim();
            foreach ((Regex pattern, string kind) in Rules)
            {
                Match m = pattern.Match(text);
                if (!m.Success)
                    continue;
                string Group(string g) => m.Groups[g].Success ? m.Groups[g].Value.Trim() : null;
                return new Reading(kind, Group("keep"), Group("raid"), Group("hub"), Group("boss"), Group("realm"), Group("relic"));
            }
            return null;
        }

        /// <summary>
        /// The notice as a player of the realm would type it in /faction. <paramref name="slots"/> are the notice's news slots
        /// ({keep}, {enemy}, {raid}, {boss}), used where the written text doesn't name them. Pure.
        /// </summary>
        public static string Say(string notice, IReadOnlyDictionary<string, string> slots, Random random)
        {
            random ??= Random.Shared;
            Reading r = Read(notice);
            string slot(string key) => slots != null && slots.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v) ? v : null;
            if (r == null)
                return Plain(notice);
            string keep = AutonomousChatFieldTalk.ShortKeep(r.Keep ?? slot("keep"), random);
            string raid = ShortRaid(r.Raid ?? slot("raid"), random);
            string hub = ShortHub(r.Hub, random);
            string boss = ShortBoss(r.Boss ?? slot("boss"), random);
            string enemy = slot("enemy") is { } e ? RealmWord(e, random) : null;
            string relic = ShortRelic(r.Relic);
            string winner = r.Realm != null ? RealmWord(r.Realm, random) : enemy ?? "they";
            string[] options = r.Kind switch
            {
                "raid_recruit" =>
                [
                    $"{raid} raid forming at {hub}, need 50s", $"lf 50s for {raid}, meet at {hub}", $"{raid} raid at {hub} if anyone wants in",
                    $"who's coming {raid}? forming up at {hub}", $"getting a {raid} raid going, {hub}", $"{raid} tonight, gathering at {hub}",
                    $"anyone 50 want {raid}? we're at {hub}",
                ],
                "raid_depart" =>
                [
                    $"{raid} raid heading out from {hub}, catch up", $"leaving {hub} for {raid} now", $"moving out to {raid}, late ppl just run in",
                    $"ok {raid} grp is leaving {hub}", $"heading to {raid} now, follow if ur late",
                ],
                "raid_advance" =>
                [
                    $"{raid} raid going in now", $"in at {raid}, pulling soon", $"{raid} started, late ppl run in", $"we're in {raid}, here we go",
                    $"{raid} going, wish us luck",
                ],
                "raid_reminder" =>
                [
                    $"{raid} in like 20 min, still need ppl", $"20 min till {raid}, who's not there yet?", $"{raid} soon, get to the meet up",
                    $"still forming for {raid}, 20 min or so", $"last call for {raid}, about 20 min",
                ],
                "raid_won" => [$"{raid} down!! gg all", $"we got {raid}, gg", $"gg {raid}, nice work all", $"{raid} done, gz raid", $"{raid} dead lol gg"],
                "raid_timeout" =>
                [
                    $"{raid} didnt work out, ran outta time", $"calling {raid}, took too long", $"out of time at {raid}, heading home",
                    $"{raid} is a bust tonight, too slow",
                ],
                "raid_beaten" => [$"someone beat us to {raid} lol", $"other realm got {raid} first, ugh", $"{raid} already done by someone else, rip"],
                "raid_defeated" =>
                [
                    $"wiped at {raid}, calling it", $"{raid} beat us, gg", $"{raid} wiped us, done for tonight", $"rip {raid} raid, we got smashed",
                ],
                "raid_failed" => [$"{raid} called off, not enough showed", $"not enough ppl for {raid}, cancelled", $"no {raid} tonight, nobody came"],
                "raid_stopped" => [$"{raid} called off", $"never mind {raid}, its off", $"{raid} is off for now"],
                "raid_ended" => [$"{raid} over i think, dunno if we got it", $"{raid} done? anyone know if we won", $"{raid} ended, not sure how"],
                "raid_wipe" =>
                [
                    $"wiped on {boss}, regroup and go again", $"{boss} wiped us, one more go", $"rez up, going again on {boss}",
                    $"rip, {boss} got us, trying again", $"{boss} wipe, rebuff and back in",
                ],
                "raid_help" => [$"more help coming for {raid}, nice", $"reinforcements on the way to {raid}", $"extra grps joining {raid} now"],
                "siege_gather" =>
                [
                    $"warband going to {keep}, siege camp", $"forming up for {keep} at the siege camp", $"{keep} siege forming, come",
                    $"hitting {keep} soon, meet at the camp", $"anyone for {keep}? gathering at siege camp", $"zerg forming for {keep}",
                ],
                "siege_march" =>
                [
                    $"zerg moving on {keep}", $"heading to {keep} now, come", $"army going {keep}, join up", $"we're marching on {keep}",
                    $"{keep} raid moving, catch up",
                ],
                "siege_advance" => [$"{keep} siege going, need more", $"still on {keep}, come help", $"push at {keep}, more ppl pls", $"{keep} going ok, bring heals"],
                "siege_begun" =>
                [
                    enemy != null ? $"{enemy} on {keep} doors, defend" : $"{keep} getting hit, defend", $"{keep} under siege, need ppl on walls",
                    $"they're at {keep}, get on the walls", $"defend {keep}, its started",
                ],
                "siege_others" => [$"two realms going at it at {keep}", $"big fight at {keep}, free rp?", $"theyre busy at {keep}, hit their flank?"],
                "siege_others_soon" => [$"something forming near {keep}", $"looks like a fight at {keep} soon", $"watch {keep}, armies forming"],
                "keep_attacked" =>
                [
                    $"{keep} under attack, need ppl", $"{keep} getting hit!", enemy != null ? $"{enemy} hitting {keep}, anyone?" : $"{keep} needs defenders",
                    $"defend {keep}!", $"{keep} is getting sieged, who's close?",
                ],
                "keep_army_coming" =>
                [
                    $"{winner} zerg heading for {keep}, defend", $"big {winner} army moving on {keep}", $"{winner} coming for {keep}, get there",
                    $"scouts saw {winner} marching on {keep}", $"{keep} about to get hit, {winner} on the way",
                ],
                "keep_ours_attacking" => [$"we're on {keep}, need help", $"hitting {keep}, come", $"{keep} siege, anyone close?"],
                "keep_others_fighting" =>
                [
                    $"fight going at {keep}, anyone?", $"{keep} kicking off, could pick some off", $"theyre fighting at {keep}, 3rd realm it?",
                    $"{keep} is a mess rn, free rp", $"something going on at {keep}",
                ],
                "keep_retake" => [$"lost {keep}, retake?", $"{keep} fell, going back for it", $"{keep} gone, who wants it back", $"retake {keep}, meet at the outpost"],
                "keep_relief" => [$"defense going to {keep}, come", $"relief heading to {keep}", $"grp going to save {keep}", $"going to help {keep}, join"],
                "siege_reminder_defend" => [$"watch {keep}, they're forming", $"{keep} gets hit in 20 or so", $"be ready at {keep} soon"],
                "siege_reminder_attack" => [$"20 min till {keep}, need more", $"{keep} soon, still forming", $"who's coming {keep}? 20 min"],
                "keep_won" => [$"{keep} is ours!", $"took {keep}, gg", $"we got {keep}", $"{keep} ours, gj all"],
                "keep_lost" => [$"{winner} took {keep}", $"lost {keep} to {winner}", $"{keep} gone, {winner} have it", $"ugh {winner} got {keep}"],
                "siege_enemy_broke" => [$"they gave up on {keep} lol", $"{keep} safe, they left", $"no siege at {keep} after all"],
                "siege_muster_failed" => [$"{keep} called off, not enough", $"not enough for {keep}, maybe later", $"no {keep} tonight"],
                "siege_held" => [$"held {keep}!", $"{keep} held, gj", $"they couldnt take {keep} lol", $"{keep} still ours"],
                "siege_failed" => [$"couldnt take {keep}, calling it", $"{keep} didnt happen, out of time", $"gave up on {keep}"],
                "siege_ended" => [$"{keep} over? who won", $"{keep} done i think", $"fight at {keep} is over"],
                "relic_road" => [$"relic out of {keep}!", $"they took the relic from {keep}", $"relic on the road from {keep}", $"relic moving, near {keep}"],
                "relic_timeout" => [$"{relic} chase is over", $"done chasing {relic}", $"lost track of {relic}, calling it"],
                "relic_shrined" => [$"{relic} is in a shrine now", $"{relic} got shrined", $"{relic} is home somewhere"],
                "relic_ours" => [$"{relic} is ours!!", $"{relic} in our shrine, gg", $"got {relic} home, nice"],
                "relic_theirs" => [$"{winner} got {relic}", $"{relic} went to {winner}, ugh", $"{winner} shrined {relic}"],
                _ => null,
            };
            return options == null ? Plain(notice) : options[random.Next(options.Length)];
        }

        // The herald's phrases source/tools/chat_audit.py looks for in a bot's line (keep the two lists together).
        private static readonly Regex SystemPhrase = new(
            @"level-\d+ adventurers|\bthere is an opportunity\b|\bjoin them at\b|\brecruiting up to\b|\bmust arrive before\b|" +
            @"\bhas broken out at\b|\bexpedition is advancing\b|\bdeparting together\b|\blate arrivals\b|\bstaged level\b|" +
            @"\breinforcements (?:are welcome|join)\b|\brally to its defen[cs]e\b|\bwarbands are (?:gathering|marching)\b|" +
            @"\bseize your opportunity\b|\b(?:available|nearby) warbands\b|\bform up at\b|\bhold the walls\b|\bstand by (?:your|ur) realm\b|" +
            @"\bthe army marches\b|\bdefen[cs]e rally marches\b|\bthe expedition\b|\barrived at the hub\b|;(?![)PpDd(])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

        /// <summary>
        /// Whether a line reads as a game announcement, not typed chat (chat_audit.py "lines in system voice"): a herald's phrase,
        /// a semicolon, or a line of more than 15 words in two or more sentences that each start with a capital. Pure.
        /// </summary>
        public static bool SystemVoice(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            if (SystemPhrase.IsMatch(text))
                return true;
            string line = text.Trim();
            int letters = line.Count(char.IsLetter), upper = line.Count(char.IsUpper);
            string[] sentences = Regex.Split(line, @"(?<=[.!?])\s+").Where(s => s.Length > 0).ToArray();
            string[] tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // A shout in capitals or spaced-out letters (a gold spammer's "w w w . F A S T") is no herald.
            bool spaced = tokens.Count(token => token.Length == 1) > tokens.Length / 3.0;
            return upper <= letters / 2 && !spaced && tokens.Length > 15 && sentences.Length >= 2 && sentences.All(s => char.IsUpper(s[0]));
        }

        /// <summary>
        /// A notice no rule knows, made to read like typed chat: its first sentence, lowercase, no numbers, no semicolon, no
        /// exclamation, at most ten words. Pure.
        /// </summary>
        public static string Plain(string notice)
        {
            if (string.IsNullOrWhiteSpace(notice))
                return notice;
            string first = Regex.Split(notice.Trim(), @"(?<=[.!?;:])\s+")[0];
            string text = Regex.Replace(first.ToLowerInvariant(), @"\b\d+\b\s*", string.Empty);
            text = Regex.Replace(text, @"[;:!.]", string.Empty).Replace("level-50 ", string.Empty).Trim();
            string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(' ', words.Take(10));
        }

        /// <summary>
        /// A news line's names as a player typed them (1.1.3, after RC5: "we lost Dun Ailinne to Hibernia" read as a herald): the
        /// keep, raid, boss, zone and relic shortened, the realms as "mids", "hibs", "albs". Other slots are kept. Pure.
        /// </summary>
        public static Dictionary<string, string> ChatSlots(IReadOnlyDictionary<string, string> slots, Random random)
        {
            var chat = new Dictionary<string, string>(StringComparer.Ordinal);
            if (slots == null)
                return chat;
            random ??= Random.Shared;
            foreach ((string key, string value) in slots)
                chat[key] = string.IsNullOrWhiteSpace(value) ? value : key switch
                {
                    "keep" => AutonomousChatFieldTalk.ShortKeep(value, random),
                    "enemy" or "winner" or "loser" => RealmWord(value, random) is var word && word != "they" ? word : value,
                    "raid" => ShortRaid(value, random),
                    "boss" => ShortBoss(value, random),
                    "zone" => AutonomousChatFieldTalk.ShortZone(value, random),
                    "relic" => ShortRelic(value),
                    _ => value,
                };
            return chat;
        }

        private static readonly Dictionary<string, string[]> RaidWords = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Summoner's Hall"] = ["sh", "summoners hall", "summoners"], ["Caer Sidi"] = ["sidi", "caer sidi"], ["Tuscaran Glacier"] = ["tg", "glacier"],
            ["Galladoria"] = ["galla", "galladoria"], ["Darkness Falls"] = ["df"], ["Golestandt"] = ["gole", "golestandt"], ["Gjalpinulva"] = ["gjalp", "gjalpinulva"],
            ["Cuuldurach"] = ["cuul", "cuuldurach"], ["Iarnvidiur's Lair"] = ["iarnvidiur"], ["Dodens Gruva"] = ["dodens", "dodens gruva"],
            ["Marfach Caverns"] = ["marfach"], ["Hall of the Corrupt"] = ["hall of the corrupt", "corrupt hall"], ["Avalon City"] = ["avalon city", "avalon"],
            ["Crystal Cave"] = ["crystal cave"], ["Tur Suil"] = ["tur suil"], ["Trollheim"] = ["trollheim"], ["Krondon"] = ["krondon"],
        };

        /// <summary>A raid as chat said it ("sh", "sidi", "df", "krondon"); the lowercase name otherwise.</summary>
        public static string ShortRaid(string raid, Random random)
        {
            if (string.IsNullOrWhiteSpace(raid))
                return "the raid";
            return RaidWords.TryGetValue(raid.Trim(), out string[] words) ? words[random.Next(words.Length)] : raid.Trim().ToLowerInvariant();
        }

        /// <summary>A raid's meeting place as chat said it: "Caer Diogel" -> "diogel", "Snowdonia Station" -> "snowdonia".</summary>
        public static string ShortHub(string hub, Random random)
        {
            if (string.IsNullOrWhiteSpace(hub))
                return "the meet up";
            string h = hub.Trim();
            if (h.Contains("Darkness Falls entrance", StringComparison.OrdinalIgnoreCase))
                return "df entrance";
            int paren = h.IndexOf('(');
            if (paren > 0 && h.EndsWith(')'))
                h = h[(paren + 1)..^1];
            h = h.ToLowerInvariant().Replace("'", string.Empty);
            foreach (string prefix in new[] { "caer ", "fort ", "dun " })
                if (h.StartsWith(prefix, StringComparison.Ordinal))
                {
                    h = h[prefix.Length..];
                    break;
                }
            foreach (string suffix in new[] { " station", " village", " faste" })
                if (h.EndsWith(suffix, StringComparison.Ordinal))
                    h = h[..^suffix.Length];
            return h.Trim();
        }

        /// <summary>A boss in lowercase, by its first name ("Grand Summoner Govannon" -> "govannon" is the last word).</summary>
        public static string ShortBoss(string boss, Random random)
        {
            if (string.IsNullOrWhiteSpace(boss))
                return "the boss";
            if (RaidWords.TryGetValue(boss.Trim(), out string[] words))
                return words[random.Next(words.Length)];
            string[] parts = boss.Trim().ToLowerInvariant().Replace("'", string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length <= 2 ? string.Join(' ', parts) : parts[^1];
        }

        /// <summary>A relic in lowercase, without "the".</summary>
        public static string ShortRelic(string relic)
        {
            if (string.IsNullOrWhiteSpace(relic))
                return "the relic";
            string r = relic.Trim().ToLowerInvariant();
            return r.StartsWith("the ", StringComparison.Ordinal) ? r[4..] : r;
        }

        /// <summary>A realm as chat said it ("mids", "hib", "albs").</summary>
        public static string RealmWord(string realm, Random random) => realm?.Trim().ToLowerInvariant() switch
        {
            "albion" => random.Next(3) == 0 ? "alb" : "albs",
            "midgard" => random.Next(3) == 0 ? "mid" : "mids",
            "hibernia" => random.Next(3) == 0 ? "hib" : "hibs",
            _ => "they",
        };
    }
}
