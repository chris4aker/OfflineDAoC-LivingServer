using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Logging;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// The lore bible in the gamebots' mouths (Living Server 1.2, docs/LORE.md):
/// <list type="bullet">
/// <item>a raid's leader posts one or two <c>briefing</c> lines for its boss in /faction as the raid leaves the muster;</item>
/// <item>after the raid a veteran tells an <c>aftermathWin</c> or <c>aftermathLoss</c> line, then one of the place's lore lines;</item>
/// <item>locations' <c>lore</c> and <c>rumour</c> lines and bosses' <c>rumour</c> lines are built into the corpus as
/// ambient lines of topic lore (about place, the location's realm). Since round 5 no lore line is said unprompted
/// (AutonomousChatFiller drops topic lore), so these lines, like the written lore_*.lines, are heard only as answers;</item>
/// <item>"what's in krondon?", "who is cuuldurach?" get an answer through the knowledge route (AutonomousChatKnowledge):
/// a written corpus lore line that names the subject when the bible has no <c>tell</c> line for it (and one time in
/// three when it has), else the bible's <c>tell</c> line (a place) or a rumour (a boss). Chat audit LORE-001.</item>
/// </list>
/// Every line goes through the /faction chat budget: above the channel's ceiling it is not said.
/// </summary>
public static class LoreChat
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public const int AftermathMinMs = 90_000;
    public const int AftermathSpreadMs = 90_000;
    public const int LoreAfterTaleMs = 50_000;

    public static string RealmCode(string realm) => realm switch
    {
        "Albion" => "alb",
        "Midgard" => "mid",
        "Hibernia" => "hib",
        _ => "*",
    };

    /// <summary>The lore bible's bot lines as corpus lines (one per way of saying each, the family keeps them together). Pure.</summary>
    public static VC.Line[] CorpusLines(LoreBible bible)
    {
        var lines = new List<VC.Line>();
        void Add(string template, string realm, string source)
        {
            string[] ways = LoreText.Expand(template);
            string family = ways.Length > 1 ? "lore" + LoreText.StableHash(template).ToString("x8") : string.Empty;
            foreach (string said in ways)
            {
                if (said.Length == 0 || said.Length > LoreBible.MaxBotLineLength)
                    continue;
                string id = "lore-" + LoreText.StableHash(source + "|" + said).ToString("x8");
                lines.Add(new VC.Line(id, "ambient", "lore", [], RealmCode(realm), [], 1, 50, [], string.Empty, string.Empty, 1.0, said)
                {
                    About = true,
                    Family = family,
                });
            }
        }
        foreach (LoreBible.Location location in bible.Locations)
        {
            foreach (string line in location.Lore.Concat(location.Rumour))
                Add(line, location.Realm, location.Id);
            // A realm's take (a neutral place told its realm's way) is said by that realm's bots only.
            foreach ((string key, string realm) in LoreBible.TakeRealms)
                if (location.RealmTakes.TryGetValue(key, out LoreBible.RealmTake take))
                    foreach (string line in take.Lore.Concat(take.Rumour))
                        Add(line, realm, location.Id + "@" + key);
        }
        foreach (LoreBible.Boss boss in bible.Bosses)
        {
            foreach (string line in boss.Rumour)
                Add(line, bible.LocationOf(boss)?.Realm ?? boss.Realm, boss.Id);
            foreach ((string key, string realm) in LoreBible.TakeRealms)
                if (boss.RealmTakes.TryGetValue(key, out LoreBible.RealmTake take))
                    foreach (string line in take.Rumour)
                        Add(line, realm, boss.Id + "@" + key);
        }
        return lines.GroupBy(l => l.Id).Select(g => g.First()).ToArray();
    }

    /// <summary>"whats in krondon?" and the like: a lore answer, or false.</summary>
    public static bool TryAnswer(string text, out string answer, eRealm realm = eRealm.None) =>
        TryAnswer(LoreBible.Current, AutonomousBotVoices.Corpus?.Lines, text, Random.Shared, out answer, realm);

    /// <summary>
    /// A lore answer (pure but for the random): a written lore line of the corpus (data/chat/lore_*.lines) that names the
    /// asked place or boss, when the bible has no <c>tell</c> line for it or one time in three when it has; else the bible's
    /// own answer (<see cref="LoreBible.TryAnswer"/>). Chat audit LORE-001: the 632 written lore lines were never heard,
    /// since no lore line is said unprompted and the answers came only from the bible.
    /// </summary>
    public static bool TryAnswer(LoreBible bible, IEnumerable<VC.Line> corpus, string text, Random random, out string answer, eRealm realm = eRealm.None)
    {
        answer = string.Empty;
        if (bible == null)
            return false;
        (LoreBible.Location location, LoreBible.Boss boss) = bible.Subject(text);
        if (location == null && boss == null)
            return false;
        IEnumerable<string> names = location != null ? LoreBible.NamesOf(location.Name, location.Id, location.Aliases)
            : LoreBible.NamesOf(boss.Name, boss.Id, boss.Aliases);
        string[] written = WrittenLoreNaming(corpus, names, realm);
        bool hasTell = location != null && location.TellFor(realm).Length > 0;
        if (written.Length > 0 && (!hasTell || random.Next(3) == 0))
        {
            answer = written[random.Next(written.Length)];
            return true;
        }
        return bible.TryAnswer(text, random, out answer, realm);
    }

    /// <summary>
    /// The written lore lines (kind ambient, topic lore, not the bible's own "lore-" lines) of the realm that name one of
    /// <paramref name="names"/> as words and can be said by anyone as they stand: no slot, no class, no condition. Pure.
    /// </summary>
    public static string[] WrittenLoreNaming(IEnumerable<VC.Line> corpus, IEnumerable<string> names, eRealm realm)
    {
        string[] wanted = (names ?? []).Select(Words).Where(n => n.Trim().Length >= 4).Distinct().ToArray();
        if (wanted.Length == 0)
            return [];
        return (corpus ?? []).Where(line => line.Kind == "ambient" && line.Topic == "lore" && !line.Id.StartsWith("lore-", StringComparison.Ordinal) &&
                                            VC.RealmFits(line.Realm, realm) && line.Classes.Length == 0 && line.Needs.Length == 0 &&
                                            !line.Text.Contains('{') && line.Text.Length <= LoreBible.MaxBotLineLength)
            .Where(line => wanted.Any(name => Words(line.Text).Contains(name, StringComparison.Ordinal)))
            .Select(line => line.Text).Distinct().ToArray();
    }

    #region Placed lore (owner, 2026-11-18 09:14: "Lore should never be cut, find a way to properly have them in the language framework")

    /// <summary>A player hears at most one placed lore line in this long.</summary>
    public const long PlacedGapMs = 20 * 60_000;
    /// <summary>How often the placed-lore check runs for each player, and its chance each time once the gap is over.</summary>
    public const long PlacedCheckMs = 60_000;
    public const double PlacedChance = 0.15;
    /// <summary>The measured target: placed lore lines a player hears an hour while standing in a place with lore, out of a fight.</summary>
    public const double PlacedTargetPerHour = 2.2;

    /// <summary>
    /// Whether a gamebot beside the player may say a placed lore line in /say (PC check 2026-11-19, build 5154cae: a bot in an
    /// RvR party, 1,000 units off, was logged as saying one and the player heard nothing): in the player's region, within
    /// /say range (WorldMgr.SAY_DISTANCE), and not on its way with an RvR party. Pure.
    /// </summary>
    public static bool HearsPlacedLore(bool sameRegion, double distance, bool inRvrParty) =>
        sameRegion && distance >= 0 && distance <= WorldMgr.SAY_DISTANCE && !inRvrParty;

    /// <summary>Whether a placed lore line is due for a player (pure): the gap since the last one is over and the roll says now.</summary>
    public static bool PlacedDue(long now, long lastHeard, double roll) => now - lastHeard >= PlacedGapMs && roll < PlacedChance;

    /// <summary>
    /// The lore a gamebot standing in <paramref name="here"/> may remark on (pure): the written lore lines (data/chat/lore_*.lines)
    /// of the realm that name the place or one of its bosses, then the bible's own lore and rumour lines for the place, as
    /// templates ("[a|b]" still in them), without those in <paramref name="heard"/>.
    /// </summary>
    public static string[] PlacedLines(LoreBible bible, IEnumerable<VC.Line> corpus, LoreBible.Location here, eRealm realm, ISet<string> heard = null)
    {
        if (bible == null || here == null)
            return [];
        IEnumerable<string> names = LoreBible.NamesOf(here.Name, here.Id, here.Aliases)
            .Concat(bible.BossesAt(here.Id).SelectMany(boss => LoreBible.NamesOf(boss.Name, boss.Id, boss.Aliases)));
        return WrittenLoreNaming(corpus, names, realm)
            .Concat(here.LoreFor(realm)).Concat(here.RumourFor(realm))
            .Where(line => !string.IsNullOrWhiteSpace(line) && heard?.Contains(line) != true)
            .Distinct().ToArray();
    }

    #endregion

    private static string Words(string text) =>
        " " + string.Join(' ', new string((text ?? string.Empty).ToLowerInvariant().Replace('\'', ' ')
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ').ToArray()).Split(' ', StringSplitOptions.RemoveEmptyEntries)) + " ";

    /// <summary>Whether /faction has room for one more line for this realm now (the chat budget's ceiling).</summary>
    public static bool FactionHasRoom(eRealm realm)
    {
        try
        {
            long now = GameLoop.GameLoopTime;
            int bots = AutonomousBotRegistry.Snapshot().Count(b => b.Realm == realm);
            var band = AutonomousChatBudget.LiveBand(AutonomousChatBudget.Channel.Faction, bots);
            return AutonomousChatBudget.Meter.PerMinute(AutonomousChatBudget.Channel.Faction, (long)realm, now) < band.Ceiling;
        }
        catch (Exception)
        {
            return true;
        }
    }

    /// <summary>Which lines a briefing uses: one, or now and then two (the second a few seconds later). Pure.</summary>
    public static string[] PickBriefing(string[] pool, Random random)
    {
        if (pool == null || pool.Length == 0)
            return [];
        int first = random.Next(pool.Length);
        if (pool.Length == 1 || random.NextDouble() >= 0.5)
            return [LoreText.Pick(pool[first], random)];
        int second = (first + 1 + random.Next(pool.Length - 1)) % pool.Length;
        return [LoreText.Pick(pool[first], random), LoreText.Pick(pool[second], random)];
    }

    /// <summary>The raid leaves the muster: its leader briefs the realm on the boss in /faction.</summary>
    public static void Briefing(GameBot leader, string bossType)
    {
        if (!AutonomousBotVoices.Enabled || leader == null || string.IsNullOrEmpty(bossType))
            return;
        LoreBible.Boss boss = LoreBible.Current.BossForClass(bossType);
        string[] lines = PickBriefing(boss?.BriefingFor(leader.Realm), Random.Shared);
        for (int i = 0; i < lines.Length; i++)
        {
            string text = lines[i];
            AutonomousLivingServer.Later(leader, 3_000 + i * (6_000 + Random.Shared.Next(4_000)), () => leader.IsAlive && FactionHasRoom(leader.Realm),
                () => AutonomousLivingServer.Speak(leader, AutonomousLivingServer.Channel.Faction, AutonomousBotVoices.StyleFor(leader, text)));
        }
    }

    /// <summary>After a raid: a veteran's line on the win or loss a minute or two later, then a lore line about the place.</summary>
    public static void Aftermath(IReadOnlyCollection<GameBot> raiders, string bossType, ushort region, bool won)
    {
        try
        {
            if (!AutonomousBotVoices.Enabled || raiders == null || raiders.Count == 0)
                return;
            LoreBible bible = LoreBible.Current;
            LoreBible.Boss boss = bible.BossForClass(bossType);
            GameBot veteran = raiders.Where(AutonomousLivingServer.IsSpeaker).OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
            if (veteran == null)
                return;
            string[] pool = boss?.AftermathFor(veteran.Realm, won);
            LoreBible.Location place = bible.LocationOf(boss) ?? bible.LocationsInRegion(region).FirstOrDefault();
            string[] lore = place == null ? [] : place.LoreFor(veteran.Realm).Concat(place.RumourFor(veteran.Realm)).ToArray();
            if (veteran == null || (pool == null || pool.Length == 0) && lore.Length == 0)
                return;
            int delay = AftermathMinMs + Random.Shared.Next(AftermathSpreadMs);
            if (pool is { Length: > 0 })
            {
                string tale = LoreText.Pick(pool[Random.Shared.Next(pool.Length)], Random.Shared);
                AutonomousLivingServer.Later(veteran, delay, () => AutonomousLivingServer.CanTalk(veteran) && FactionHasRoom(veteran.Realm),
                    () => AutonomousLivingServer.Speak(veteran, AutonomousLivingServer.Channel.Faction, AutonomousBotVoices.StyleFor(veteran, tale)));
                delay += LoreAfterTaleMs;
            }
            if (lore.Length > 0)
            {
                string line = LoreText.Pick(lore[Random.Shared.Next(lore.Length)], Random.Shared);
                AutonomousLivingServer.Later(veteran, delay, () => AutonomousLivingServer.CanTalk(veteran) && FactionHasRoom(veteran.Realm),
                    () => AutonomousLivingServer.Speak(veteran, AutonomousLivingServer.Channel.Faction, AutonomousBotVoices.StyleFor(veteran, line)));
            }
        }
        catch (Exception exception)
        {
            Log.Warn("Lore: after-raid lines failed", exception);
        }
    }
}
