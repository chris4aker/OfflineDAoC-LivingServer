using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// Bot-led groups, step B4 (docs/BOT-LED-GROUPS.md, "B4 as built"): a gamebot party that hosts the player (or did until
/// the player left) looks for a replacement when a member leaves. The leader asks in /lfg for the missing role ("lf 1
/// more, need a healer"), takes a free gamebot of that role and a fitting level that is waiting for a group, through the
/// duty lease (a gamebot someone else holds is never taken), and the recruit travels to the party on its own, as any
/// party member does. After <see cref="GiveUpAfterMs"/> the leader gives up: the party carries on short-handed when it
/// still has a tank, a healer and a damage dealer, and ends otherwise. The pure rules; the glue is in
/// AutonomousBotGroupCoordinator.Replacement.cs.
/// </summary>
public static class AutonomousPartyReplacement
{
    /// <summary>The leader looks for a recruit this often.</summary>
    public const long SearchEveryMs = 15_000;
    /// <summary>The leader gives up after five minutes.</summary>
    public const long GiveUpAfterMs = 5 * 60_000L;
    /// <summary>A recruit is at most this many minutes' travel from the leader (the formation's own limit).</summary>
    public const double MaximumTravelMinutes = AutonomousBotGroupCoordinator.MaximumPveRecruitmentMinutes;

    /// <summary>The phases in which a party takes a recruit: on its way or at its camp, not while meeting up or ending.</summary>
    private static readonly HashSet<string> RecruitingPhases = new(StringComparer.Ordinal) { "Choosing group target", "Traveling", "Grinding" };

    /// <summary>
    /// A replacement is looked for when a member left a PvE party that hosts the player, or the player left it (the design:
    /// "the group carries on without you and looks for a replacement"); not for a raid's party (the raid refills its own),
    /// not for bot-only parties (they keep today's rules), not while meeting up or ending, and only with a seat free.
    /// </summary>
    public static bool Wanted(bool hostsOrHostedPlayer, bool raidParty, bool ending, string phase, int members, int maxMembers) =>
        hostsOrHostedPlayer && !raidParty && !ending && phase != null && RecruitingPhases.Contains(phase) && members < maxMembers;

    /// <summary>One search for a missing role.</summary>
    public sealed class Search
    {
        public Search(BotPveGroupRole role, long since, bool holdsParty)
        {
            Role = role;
            Since = since;
            NextTry = since;
            HoldsParty = holdsParty;
        }

        public BotPveGroupRole Role { get; }
        public long Since { get; }
        public long NextTry { get; set; }
        /// <summary>The party lost its tank, healer or damage dealer: it waits (no pulls) instead of ending, until the give-up.</summary>
        public bool HoldsParty { get; set; }
        public bool Asked { get; set; }
        public int Tries { get; set; }
    }

    public enum Step { Wait, TryNow, GiveUpCarryOn, GiveUpEnd }

    /// <summary>
    /// What a search does on one pass: wait for its next try, try now, or give up after <see cref="GiveUpAfterMs"/>:
    /// carry on short-handed when the party is still viable, end it otherwise.
    /// </summary>
    public static Step Next(Search search, long now, bool viable)
    {
        if (search == null)
            return Step.Wait;
        if (now - search.Since >= GiveUpAfterMs)
            return viable ? Step.GiveUpCarryOn : Step.GiveUpEnd;
        return now >= search.NextTry ? Step.TryNow : Step.Wait;
    }

    /// <summary>The search keeps a party that lost its core alive (no pulls) until it gives up.</summary>
    public static bool HoldsParty(Search search, long now) =>
        search != null && search.HoldsParty && now - search.Since < GiveUpAfterMs;

    /// <summary>
    /// A gamebot as the recruit search sees it. <paramref name="Free"/>: alive, in no group, not fighting, not on a stable
    /// route or a corpse run. <paramref name="LeaseAllows"/>: the duty lease would give it to this party (nothing that
    /// outranks the party holds it). <paramref name="WaitingForGroup"/>: its own goal is a group (it is not taken from a
    /// solo task, a raid, the frontier or a battleground).
    /// </summary>
    public readonly record struct Candidate(bool Free, bool LeaseAllows, bool WaitingForGroup, bool SameRealm, int Level, bool FillsRole,
        double TravelMinutes);

    /// <summary>The recruit fits the party's level: within five levels of its average, or both at 50 (the formation's rule).</summary>
    public static bool LevelFits(int partyLevel, int level) => AutonomousBotGroupCoordinator.LevelsCompatible(partyLevel, level);

    /// <summary>
    /// The recruit: a free gamebot of the realm waiting for a group, that the lease allows, of the missing role and a
    /// fitting level, within <see cref="MaximumTravelMinutes"/>; the nearest by travel, then the closest level. -1 when
    /// none fits.
    /// </summary>
    public static int Pick(IReadOnlyList<Candidate> candidates, int partyLevel)
    {
        int best = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            Candidate c = candidates[i];
            if (!c.Free || !c.LeaseAllows || !c.WaitingForGroup || !c.SameRealm || !c.FillsRole || !LevelFits(partyLevel, c.Level) ||
                c.TravelMinutes < 0 || c.TravelMinutes > MaximumTravelMinutes)
                continue;
            if (best < 0 || c.TravelMinutes < candidates[best].TravelMinutes ||
                c.TravelMinutes == candidates[best].TravelMinutes && Math.Abs(c.Level - partyLevel) < Math.Abs(candidates[best].Level - partyLevel))
                best = i;
        }
        return best;
    }

    /// <summary>The role as a 2003 /lfg line says it ({need}): "a healer", "a tank", "dps".</summary>
    public static string NeedWord(BotPveGroupRole role) => role switch
    {
        BotPveGroupRole.Healer => "a healer",
        BotPveGroupRole.Tank => "a tank",
        _ => "dps",
    };

    /// <summary>
    /// An /lfg ask goes out only while the realm's /lfg is under its ceiling (the chat budget's band), so a recruiting
    /// party never makes the channel busier than the busiest a 2003 /lfg got.
    /// </summary>
    public static bool LfgFits(double measuredPerMinute, double ceiling) => measuredPerMinute < ceiling;
}
