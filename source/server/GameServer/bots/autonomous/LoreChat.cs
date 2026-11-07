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
/// <item>locations' <c>lore</c> and <c>rumour</c> lines and bosses' <c>rumour</c> lines join the corpus's lore pool
/// (kind ambient, topic lore, about place, the location's realm);</item>
/// <item>"what's in krondon?", "who is cuuldurach?" get a <c>tell</c> line (a place) or a rumour (a boss) through the
/// knowledge route (AutonomousChatKnowledge).</item>
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
                if (said.Length == 0 || said.Length > VC.MaxLength)
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

    /// <summary>"whats in krondon?" and the like: a lore answer, or false. Pure but for the random.</summary>
    public static bool TryAnswer(string text, out string answer, eRealm realm = eRealm.None) =>
        LoreBible.Current.TryAnswer(text, Random.Shared, out answer, realm);

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
