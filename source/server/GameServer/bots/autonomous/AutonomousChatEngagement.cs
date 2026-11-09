using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// 1.1.3, bots that take notice of you (owner's PC, RC3: in 2.5 hours with the player online only 5 bot lines named them, and
/// nobody reacted to a level, a death, a new piece of gear or the player arriving in a zone). This is the pure part: the
/// pacing of unprompted lines aimed at a player and the words for each moment. AutonomousLivingServer.Engage.cs decides who
/// says them, and only an autonomous gamebot of the player's realm that is free, isn't "cool" towards the player and is
/// really there (or really a friend) does. Every line stays behind its existing switch: grats (bot_living_grats), death
/// (bot_living_death), memory (bot_living_memory) and tells (bot_voices_tells).
/// </summary>
public static class AutonomousChatEngagement
{
    #region Pacing

    /// <summary>
    /// Unprompted lines aimed at one player (a tell, a /say line naming them, a guild hello), solo in a busy realm: about five
    /// an hour, five minutes apart at the least. Replies to the player's own lines are not counted (they are answers).
    /// </summary>
    public const int SoloPerHour = 5;
    public const long SoloGapMs = 5 * 60_000;

    /// <summary>Grouped, or with two or more friends online: about nine an hour, three minutes apart.</summary>
    public const int WarmPerHour = 9;
    public const long WarmGapMs = 3 * 60_000;

    /// <summary>The player's hourly allowance and the shortest gap between two lines. Pure.</summary>
    public static (int PerHour, long GapMs) Allowance(bool grouped, int friendsOnline) =>
        grouped || friendsOnline >= 2 ? (WarmPerHour, WarmGapMs) : (SoloPerHour, SoloGapMs);

    /// <summary>
    /// A reaction to something the bot saw happen to the player in front of it (their death) only needs this long since the last
    /// line to them, whatever the hour already held: it is an answer to the moment, not small talk.
    /// </summary>
    public const long WitnessedGapMs = 60_000;

    /// <summary>
    /// Whether one more unprompted line may be aimed at the player now, given when the earlier ones were said.
    /// <paramref name="witnessed"/>: a reaction to what the bot just saw happen to them (see <see cref="WitnessedGapMs"/>). Pure.
    /// </summary>
    public static bool MayTake(IReadOnlyList<long> said, long now, bool grouped, int friendsOnline, bool witnessed = false)
    {
        (int perHour, long gap) = Allowance(grouped, friendsOnline);
        if (witnessed)
        {
            perHour = int.MaxValue;
            gap = WitnessedGapMs;
        }
        int inHour = 0;
        long last = long.MinValue / 2;
        if (said != null)
            foreach (long at in said)
            {
                if (at > now)
                    continue;
                if (now - at < 60 * 60_000)
                    inHour++;
                last = Math.Max(last, at);
            }
        return inHour < perHour && now - last >= gap;
    }

    /// <summary>The unprompted lines said to each player in the last hour.</summary>
    public sealed class Pacer
    {
        private readonly Dictionary<string, List<long>> _said = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Takes a turn for a line to the player when <see cref="MayTake"/> allows one (and notes it).</summary>
        public bool TryTake(string player, long now, bool grouped, int friendsOnline, bool witnessed = false)
        {
            if (string.IsNullOrEmpty(player))
                return false;
            lock (_said)
            {
                List<long> said = Of(player, now);
                if (!MayTake(said, now, grouped, friendsOnline, witnessed))
                    return false;
                said.Add(now);
                return true;
            }
        }

        /// <summary>Notes a line said to the player by another path (a bot's tell, gossip), so it counts against the allowance.</summary>
        public void Note(string player, long now)
        {
            if (string.IsNullOrEmpty(player))
                return;
            lock (_said)
                Of(player, now).Add(now);
        }

        public int LastHour(string player, long now)
        {
            lock (_said)
                return _said.TryGetValue(player ?? string.Empty, out List<long> said) ? said.Count(at => now - at < 60 * 60_000 && at <= now) : 0;
        }

        public void Clear()
        {
            lock (_said)
                _said.Clear();
        }

        private List<long> Of(string player, long now)
        {
            if (!_said.TryGetValue(player, out List<long> said))
            {
                if (_said.Count > 256)
                    _said.Clear();
                _said[player] = said = new List<long>();
            }
            said.RemoveAll(at => now - at >= 60 * 60_000 || at > now);
            return said;
        }
    }

    /// <summary>The live pacer of the server's players.</summary>
    public static readonly Pacer Live = new();

    #endregion

    #region Lines

    private static string Pick(Random random, params string[] lines) => lines[random.Next(lines.Length)];

    /// <summary>A friend's tell when the player logs in (a few seconds to a minute after). <paramref name="canDuo"/>: solo and near their level.</summary>
    /// 1.1.3, after RC6: "evening" and "on tonight?" only by the PC's evening or night (<paramref name="localHour"/>).
    public static string LoginTell(string player, bool canDuo, Random random, int localHour = 20)
    {
        string flag = AutonomousBotVoiceCorpus.TimeFlag(localHour);
        string greeting = flag == "evening" ? $"evening {player} :)" : flag == "day" && localHour < 12 ? $"morning {player} :)" : $"hey hey {player}";
        string line = canDuo
            ? Pick(random, $"hey {player}! on tonight? want to duo in a bit?", $"{player}! perfect timing, wanna group up?",
                $"oh hey {player}, im solo atm if u want company")
            : Pick(random, $"hey {player}! good to see u on", $"{player}! o/", greeting, $"oh hey {player}, welcome back",
                $"look who logged in, hi {player}");
        return AutonomousBotVoiceCorpus.ForTimeOfDay(line, localHour);
    }

    /// <summary>A friend's tell when the player dings (one who isn't standing there: the bots nearby say grats in /say).</summary>
    public static string LevelUpTell(string player, int level, Random random) => level >= 50
        ? Pick(random, $"50!! grats {player}, well deserved", $"gz on 50 {player}! finally huh", $"saw u hit 50, grats {player}!")
        : Pick(random, $"gz on {level} {player}!", $"{level} already? nice going {player}", $"grats on the ding {player}", $"saw u ding {level}, gz!",
            $"ding {level}! gz {player}");

    /// <summary>A bot standing near the player when they die, not in their group (the groupmates have their own lines).</summary>
    public static string DeathSympathy(string player, bool friend, Random random) => friend
        ? Pick(random, $"ouch {player}, u ok?", $"rip {player}, that looked nasty", $"{player} noooo, u alright?", "aw man, that sucked to watch")
        : Pick(random, "ouch, that looked nasty", "that mob hit hard, u ok?", "rip, saw that one coming too late", "ugh, sorry man");

    /// <summary>
    /// A healer near the player's body that has really set out to rez them (BotBrain claimed the corpse): "need a rez? omw".
    /// <paramref name="close"/>: within /say range.
    /// </summary>
    public static string RezOffer(string player, bool friend, Random random) => friend
        ? Pick(random, $"hang on {player}, coming to rez u", $"dont release {player}, omw with a rez", $"{player} need a rez? omw")
        : Pick(random, "need a rez? omw", "hold on, ill rez u", "dont release, rez coming", "sec, coming to rez");

    /// <summary>A friend in the zone the player just arrived in (one not standing next to them). <paramref name="where"/>: its own place in the zone, or null.</summary>
    public static string ZoneTell(string player, string zone, Random random) =>
        Pick(random, $"oh hey {player}, ur in {zone} too? im out here", $"{player}! didnt know u were in {zone}, im hunting here",
            $"saw u come into {zone}, wave if u see me", $"hey {player}, im in {zone} too if u need anything");

    /// <summary>A bot that sees the player in a new item (only an item really just put on, of note: high quality or a named drop).</summary>
    public static string GearLine(string player, string item, bool friend, Random random) => friend
        ? Pick(random, $"ooh {player}, nice {item}", $"look at u in that {item}, very nice", $"{item}? where did u get that {player}")
        : Pick(random, $"nice {item}", $"is that a {item}? nice", $"jealous of that {item} lol");

    /// <summary>The words of a solo bot's real group invitation (a yes/no box: "yes" really groups it with the player).</summary>
    public static string InviteDialog(string bot, int level, string className) =>
        $"{bot} ({level} {className}) invites you to group up. Join?";

    /// <summary>A bot's duo offer that comes with a real invitation.</summary>
    public static string DuoInviteTell(string player, bool known, Random random) => known
        ? Pick(random, $"{player} wanna duo? sent u an inv", $"hey {player}, group up? sent an invite", "duo? invite is up")
        : Pick(random, "solo too? wanna duo? sent u an inv", "hey, want to group? sent an invite", "duo for a bit? sent u an inv");

    /// <summary>What a bot says by tell when it turns an invite down (the system line says why; this is how a player would put it). Pure.</summary>
    public static string DeclineReply(BotGroupInvite.Duty duty, bool inAnotherPlayersGroup, string zone, bool friend, Random random)
    {
        string place = string.IsNullOrWhiteSpace(zone) ? null : zone;
        string after = friend ? Pick(random, " catch u after?", " meet up after?", " after that for sure", "") : "";
        if (inAnotherPlayersGroup)
            return Pick(random, "in a grp, maybe later?", "sry, already grouped", "grouped atm, next time?") + after;
        return duty switch
        {
            BotGroupInvite.Duty.RealmRaid => (place != null ? Pick(random, $"sry, on the raid in {place} atm", $"cant, raiding {place} right now")
                : Pick(random, "sry, signed up for the raid", "cant, raid in progress")) + after,
            BotGroupInvite.Duty.Siege => Pick(random, "cant, holding a keep atm", "on a siege right now, sry") + after,
            BotGroupInvite.Duty.Relic => Pick(random, "carrying a relic, cant stop lol", "cant, relic run"),
            BotGroupInvite.Duty.RvrEvent => Pick(random, "out with the realm army, maybe later?", "marching with the zerg atm, sry") + after,
            BotGroupInvite.Duty.StableRoute => Pick(random, "on a horse, inv me when i land", "riding atm, try again in a min"),
            BotGroupInvite.Duty.Dead => Pick(random, "dead atm lol, sec", "need to release first, sec"),
            _ => null,
        };
    }

    /// <summary>What a bot is really doing, as it would tell a friend: the facts it has now.</summary>
    public sealed record Doing(string Zone, string Monster, string Destination, bool Grouped, bool InTown, bool InRvR, bool InDungeon);

    /// <summary>"what are you up to?", "hey", "hows it going", "wyd": small talk a friend answers from its real state. Pure.</summary>
    public static bool IsSmallTalk(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return false;
        string text = " " + AutonomousChatQuestions.Normalize(message) + " ";
        return new[]
        {
            " up to ", " wyd ", " wud ", " whats up ", " what s up ", " sup ", " hows it going ", " how s it going ", " how r u ", " how are you ",
            " hows ur night ", " how is ur night ", " doing tonight ", " u doing ", " you doing ", " hows things ", " what u doing ", " what are you doing ",
        }.Any(text.Contains) || System.Text.RegularExpressions.Regex.IsMatch(text, @"^ (hey|hi|hello|yo|heya|hiya|o/)( there)?( [a-z]+)? $");
    }

    /// <summary>
    /// A friend's answer to the player's small talk (RC5 on the owner's PC: "hey what are you up to tonight?" to a bot that grouped
    /// with the player twice drew "might wander somewhere new"): their name, what the bot is really doing, and an offer when
    /// it is free to group. <paramref name="lastPlace"/> is where they grouped (bot memory). Pure.
    /// </summary>
    public static string FriendChat(string player, Doing doing, string lastPlace, bool offer, Random random)
    {
        string hello = Pick(random, $"hey {player}!", $"{player}!", $"oh hey {player}", $"heya {player}");
        string what = doing.Grouped && doing.Zone != null ? Pick(random, $"in a grp in {doing.Zone} atm", $"grouped up in {doing.Zone}, its going ok")
            : doing.Destination != null ? Pick(random, $"heading to {doing.Destination}", $"on my way to {doing.Destination}")
            : doing.InRvR && doing.Zone != null ? Pick(random, $"out in {doing.Zone} looking for fights", $"rvr in {doing.Zone}, its busy")
            : doing.InTown ? Pick(random, "in town sorting my bags", "in town, selling junk lol")
            : doing.Monster != null && doing.Zone != null ? Pick(random, $"killing {Plural(doing.Monster)} in {doing.Zone}", $"hunting {Plural(doing.Monster)} out in {doing.Zone}")
            : doing.Zone != null ? Pick(random, $"just poking around {doing.Zone}", $"out in {doing.Zone}")
            : "not much tbh";
        string tail = offer
            ? Pick(random, ", want in?", ", wanna duo?", ". come join if u want")
            : !string.IsNullOrWhiteSpace(lastPlace) && !string.Equals(lastPlace, doing.Zone, StringComparison.OrdinalIgnoreCase)
                ? Pick(random, $". {lastPlace} again sometime?", $", we should do {lastPlace} again")
                : Pick(random, ", u?", ", hbu?", "");
        return $"{hello} {what}{tail}";
    }

    /// <summary>"large skeleton" to "large skeletons"; a word already plural stays.</summary>
    public static string Plural(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
            return kind;
        kind = kind.Trim();
        if (kind.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return kind;
        if (kind.EndsWith("y", StringComparison.OrdinalIgnoreCase) && kind.Length > 2 && !"aeiou".Contains(char.ToLowerInvariant(kind[^2])))
            return kind[..^1] + "ies";
        if (kind.EndsWith("wolf", StringComparison.OrdinalIgnoreCase))
            return kind[..^1] + "ves";
        return kind + "s";
    }

    /// <summary>An item worth a remark when the player puts it on: of 99-100% quality, or a level above the player's. Pure.</summary>
    public static bool NotableGear(int quality, int itemLevel, int playerLevel, bool equippable) =>
        equippable && (quality >= 99 || itemLevel > playerLevel && itemLevel >= 10);

    #endregion

    #region Group chat that fits the group

    private static readonly System.Text.RegularExpressions.Regex Crowd = new(
        @"\b(you all|u all|y'?all|you guys|u guys|guys|everyone|every1|everybody|all of you|ppl)\b|\b(hi|hey|hello|yo|ty|thx|thanks|night|gn|cya|evening|morning|o/) all\b|\ball\W*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex GroupRoles = new(
        @"main assist|\bma\b|\bassist (train|order)\b|who.?s (pulling|assisting|the puller|leading)|who is (pulling|main|leading|ma)\b|who pulls|\bassist on\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex BackLine = new(
        @"^\W*(back|bk|re|b|back now|im back|i'?m back|sorry,? back|ok,? back|k,? back|back,? sorry|back,? sry|back from afk)\W*$",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex AwayLine = new(
        @"\b(brb|afk|bio|biobreak|door|phone|sec,? (kid|dog|cat|wife|mom))\b",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// Whether a /group line fits the group as it really is (RC5 on the owner's PC, a duo of the player and one bot: "ty for the
    /// inv, where are you all?", "who is main assist?", and "back" with nothing to be back from). A line to several people
    /// needs three or more in the group, a main-assist or puller question too, and "back" needs the bot to have been away
    /// (a "brb" of its own, or a death). <paramref name="groupSize"/> counts the player. Pure.
    /// </summary>
    public static bool FitsGroup(string text, int groupSize, bool wasAway)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (groupSize < 3 && (Crowd.IsMatch(text) || GroupRoles.IsMatch(text)))
            return false;
        return wasAway || !BackLine.IsMatch(text);
    }

    /// <summary>"brb, door", "afk a sec": a line that says the bot steps away. Pure.</summary>
    public static bool SaysAway(string text) => !string.IsNullOrWhiteSpace(text) && AwayLine.IsMatch(text) && !BackLine.IsMatch(text);

    /// <summary>"back", "re": a line that says the bot is back. Pure.</summary>
    public static bool SaysBack(string text) => !string.IsNullOrWhiteSpace(text) && BackLine.IsMatch(text);

    /// <summary>How long after its "brb" a bot may say "back".</summary>
    public const long AwayMs = 15 * 60_000;

    private static readonly Dictionary<long, long> AwaySince = new();

    /// <summary>Notes a bot's /group line (and, 1.1.3 after RC6, its /faction and field lines): a "brb" starts its time away, a "back" ends it.</summary>
    public static void NoteGroupLine(long botId, string text, long now)
    {
        lock (AwaySince)
        {
            if (SaysAway(text))
            {
                if (AwaySince.Count > 2_048)
                    AwaySince.Clear();
                AwaySince[botId] = now;
            }
            else if (SaysBack(text))
                AwaySince.Remove(botId);
        }
    }

    /// <summary>Whether the bot said "brb" in the last <see cref="AwayMs"/> (or <paramref name="diedRecently"/>).</summary>
    public static bool WasAway(long botId, long now, bool diedRecently)
    {
        if (diedRecently)
            return true;
        lock (AwaySince)
            return AwaySince.TryGetValue(botId, out long at) && now - at >= 0 && now - at < AwayMs;
    }

    #endregion
}
