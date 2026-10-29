using System;
using System.Collections.Concurrent;
using System.Reflection;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The periodic part of the living-world features (AutonomousLivingServer.Pulse, once a second on the game loop): reputation
/// talk (AutonomousPlayerDeeds), gold spammers, roleplayers' in-character lines and true rumours. Each part has its own
/// switch and timing, and one failing part never stops the others.
/// </summary>
public static class AutonomousLivingWorld
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    /// <summary>A realm's players gone this long and back is a new play session.</summary>
    public const long SessionGapMs = 30 * 60_000;

    private static readonly ConcurrentDictionary<eRealm, long> AudienceGoneSince = new();
    private static readonly ConcurrentDictionary<eRealm, bool> InSession = new();

    /// <summary>
    /// Whether a realm's players are starting a new session: someone is on now, and nobody was on before (server start) or
    /// nobody has been on for <see cref="SessionGapMs"/>. Pure.
    /// </summary>
    public static bool IsNewSession(bool audience, bool inSession, long goneSince, long now) =>
        audience && (!inSession || goneSince > 0 && now - goneSince >= SessionGapMs);

    public static void Pulse(long now)
    {
        Run("session openers", () => Sessions(now));
        Run("deeds", () => AutonomousPlayerDeeds.Pulse(now));
        Run("gold spammers", () => AutonomousGoldSpammers.Pulse(now));
        Run("roleplayers", () => AutonomousRoleplayers.Pulse(now));
        Run("rumours", () => AutonomousRumours.Pulse(now));
    }

    /// <summary>
    /// The first half hour of a session has the signature moments (owner 2026-10-28: "I really want this update to feel
    /// like a major difference"; a 3h20m session had seen two storylines and two spam waves): the first /faction scene comes
    /// 5-10 minutes in and is a storyline episode when one can play, a roleplayer speaks 6-18 minutes in, and a gold
    /// spammer turns up 12-30 minutes in. A friend's first tell already comes 4-10 minutes in (AutonomousLivingServer).
    /// </summary>
    private static void Sessions(long now)
    {
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            bool audience = AutonomousBotChatCoordinator.HasFactionAudience(realm);
            if (!audience)
            {
                AudienceGoneSince.TryAdd(realm, now);
                continue;
            }
            bool inSession = InSession.TryGetValue(realm, out bool was) && was;
            long goneSince = AudienceGoneSince.TryRemove(realm, out long gone) ? gone : 0;
            if (!IsNewSession(true, inSession, goneSince, now))
                continue;
            InSession[realm] = true;
            Log.Info($"LIVING_SESSION_START realm={GlobalConstants.RealmToName(realm)} away_minutes={(goneSince > 0 ? (now - goneSince) / 60_000 : -1)}");
            Run("session storylines", () => AutonomousBotStorylines.OnSessionStart(realm));
            Run("session debates", () => AutonomousBotDebates.OnSessionStart(realm, now));
            Run("session roleplayers", () => AutonomousRoleplayers.OnSessionStart(realm, now));
            Run("session spammers", () => AutonomousGoldSpammers.OnSessionStart(realm, now));
        }
    }

    private static void Run(string part, Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            Log.Warn($"Living world: {part} pulse failed", exception);
        }
    }
}
