using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS;

/// <summary>
/// Trials of Atlantis and Catacombs as rumours only (owner, 2026-11-15, "Yes agreed on lore"): a sunken land that is
/// sailors' and grandmothers' talk, and a shut door under each capital. Nobody has been, nobody can go. The lines are the
/// corpus's rumour-tagged ones (data/chat/era_rumours.lines, @topic rumour_atlantis and rumour_below; build_corpus.py
/// allows the two words there only). This class answers a player who asks ("what is atlantis?", "is there anything under
/// camelot?") with the asker's realm's line, and gives the storyteller's legend its rumour afterwards. Pure.
/// </summary>
public static class LoreLegends
{
    public const string AtlantisTopic = "rumour_atlantis", BelowTopic = "rumour_below";

    /// <summary>The topic of a legend's rumours ("atlantis": "rumour_atlantis").</summary>
    public static string RumourTopicOf(string loreId) => "rumour_" + loreId;

    /// <summary>A rumour-tagged line: an out-of-era subject the corpus allows as hearsay only.</summary>
    public static bool IsEraRumour(VC.Line line) =>
        line?.Topic is AtlantisTopic or BelowTopic;

    private static readonly Regex Asks = new(
        @"\?|^ (what|whats|wats|what s|is|are|isnt|was|anyone|anybody|any1|has anyone|ever|does|do|who|tell me|heard) | (heard of|know about|tell me about|real or|true that|is it true|is there|are there) ",
        RegexOptions.Compiled);
    private static readonly Regex AboutAtlantis = new(
        @" (atlantis|atlantean|atlanteans|sunken (city|land)|drowned (city|land)|(city|land|kingdom) under the (sea|ocean|water)|lights under the (sea|water)) ",
        RegexOptions.Compiled);
    private static readonly Regex AboutBelow = new(
        @" (under|beneath|below|underneath|down under) (the )?(?<place>camelot|jordheim|tir na nog|tnn|city|capital|streets|cellars) |" +
        @" (?<place>catacombs) (?!of cardova)| city (below|underground|under the city) | undercity ",
        RegexOptions.Compiled);

    private static string Plain(string text) =>
        " " + Regex.Replace(Regex.Replace((text ?? string.Empty).ToLowerInvariant().Replace('\'', ' ').Replace("?", " ? "), @"[^a-z0-9? ]+", " "), @"\s+", " ").Trim() + " ";

    /// <summary>What a player's line asks about: "atlantis", "below" (under the asker's own capital), or null.</summary>
    public static string Subject(string text, eRealm askerRealm)
    {
        string plain = Plain(text);
        if (!Asks.IsMatch(plain))
            return null;
        if (AboutAtlantis.IsMatch(plain))
            return "atlantis";
        Match below = AboutBelow.Match(plain);
        if (!below.Success)
            return null;
        // Another realm's capital is that realm's business: "whats under jordheim?" from an Albion player is not answered here.
        string capital = below.Groups["place"].Value switch
        {
            "camelot" => "alb",
            "jordheim" => "mid",
            "tir na nog" or "tnn" => "hib",
            _ => null,
        };
        return capital == null || capital == RealmCode(askerRealm) ? "below" : null;
    }

    private static string RealmCode(eRealm realm) => realm switch
    {
        eRealm.Albion => "alb",
        eRealm.Midgard => "mid",
        eRealm.Hibernia => "hib",
        _ => "*",
    };

    /// <summary>The realm's rumour lines on a subject that a bot can say anywhere (no place or state needed).</summary>
    public static VC.Line[] LinesFor(VC corpus, string topic, eRealm realm) =>
        (corpus?.Lines ?? []).Where(line => line.Topic == topic && line.Kind is "reply" or "ambient" && line.Needs.Length == 0 &&
                                            VC.RealmFits(line.Realm, realm)).ToArray();

    /// <summary>"what is atlantis?", "is there anything under camelot?": one of the realm's rumour lines, or false.</summary>
    public static bool TryAnswer(VC corpus, string text, eRealm realm, Random random, out string answer)
    {
        answer = string.Empty;
        string subject = Subject(text, realm);
        if (subject == null)
            return false;
        VC.Line[] pool = LinesFor(corpus, subject == "atlantis" ? AtlantisTopic : BelowTopic, realm);
        if (pool.Length == 0)
            return false;
        answer = pool[(random ?? Random.Shared).Next(pool.Length)].Text;
        return answer.Length > 0;
    }

    /// <summary>The rumours a bot may pass on after a legend's tale, without those that say again what the tale said.</summary>
    public static string[] RumoursAfter(VC corpus, string loreId, eRealm realm, IEnumerable<string> heard)
    {
        string[] said = heard?.ToArray() ?? [];
        return LinesFor(corpus, RumourTopicOf(loreId), realm).Select(line => line.Text)
            .Where(text => !AutonomousTales.Repeats(text, said)).Distinct(StringComparer.Ordinal).ToArray();
    }
}
