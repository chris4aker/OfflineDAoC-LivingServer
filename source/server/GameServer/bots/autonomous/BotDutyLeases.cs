using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// The duty lease record (docs/DUTY-LEASE.md): who has each autonomous gamebot's time. Raids, RvR events, battleground
/// rallies, bot groups and players' groups claim a bot here; the rules are <see cref="BotDutyLeaseRules"/>. Every
/// claim decision (granted, renewed, taken, refused) and every end is kept in a ring for /lswhy and logged once per
/// change as a DUTY_LEASE line (a renewal, or the same refusal again, is not a change).
/// <para>
/// Leases live in memory only and never survive a restart (<see cref="BotDutyLeaseRules.PersistsAcrossRestart"/>).
/// They are keyed by the bot object, so a bot that logs out and back in starts free.
/// </para>
/// </summary>
public static class BotDutyLeases
{
    private static readonly Logger Log = LoggerManager.Create(typeof(BotDutyLeases));
    private static readonly ConcurrentDictionary<GameBot, BotDutyLease> Held = new(ReferenceEqualityComparer.Instance);
    private static readonly ConcurrentDictionary<GameBot, string> LastRefusal = new(ReferenceEqualityComparer.Instance);
    private static readonly object Sync = new();

    /// <summary>One claim decision or end, as /lswhy shows it.</summary>
    public readonly record struct Decision(long At, long BotId, string Bot, eRealm Realm, eDutyLeaseKind Kind, string Owner,
        string Outcome, string Reason, eDutyLeaseKind HeldKind, string HeldOwner);

    public const int Capacity = 2048;
    private static readonly Decision[] Ring = new Decision[Capacity];
    private static int _next;
    private static int _count;

    /// <summary>The result of a claim: whether the bot is now held for it, and why.</summary>
    public readonly record struct ClaimResult(BotDutyLeaseRules.Outcome Outcome, string Reason, BotDutyLease Previous)
    {
        public bool Granted => Outcome != BotDutyLeaseRules.Outcome.Refused;
    }

    private static long Now()
    {
        try
        {
            return GameLoop.GameLoopTime;
        }
        catch (Exception)
        {
            return Environment.TickCount64;
        }
    }

    /// <summary>Only autonomous gamebots hold leases: real players are never leased, and /spawn companions keep their own owner rules.</summary>
    public static bool CanHold(GameBot bot) => bot?.IsAutonomousWorldBot == true;

    #region Reading

    /// <summary>
    /// The bot's lease now, or null. An expired lease, or a group lease whose bot has left that group, ends here (once,
    /// logged) instead of lingering.
    /// </summary>
    public static BotDutyLease Current(GameBot bot) => Current(bot, Now());

    /// <summary>The bot's lease at game-loop tick <paramref name="now"/> (see <see cref="Current(GameBot)"/>).</summary>
    public static BotDutyLease Current(GameBot bot, long now)
    {
        if (bot == null || !Held.TryGetValue(bot, out BotDutyLease lease))
            return null;
        string ended = lease.IsExpired(now) ? DutyLeaseReason.Expired
            : lease.Group != null && !ReferenceEquals(bot.Group, lease.Group) ? DutyLeaseReason.LeftGroup : null;
        if (ended == null)
            return lease;
        if (Held.TryRemove(new KeyValuePair<GameBot, BotDutyLease>(bot, lease)))
            RecordEnd(bot, lease, ended);
        return null;
    }

    public static eDutyLeaseKind KindOf(GameBot bot) => Current(bot)?.Kind ?? eDutyLeaseKind.None;

    /// <summary>
    /// A real player has this bot: it is in the player's group, or (phase B) it leads a group the player joined. No raid,
    /// siege, RvR event, rally, transport or merchant fallback takes it, and the watchdog leaves it to the player.
    /// </summary>
    public static bool IsSpokenForByPlayer(GameBot bot) => BotDutyLeaseRules.IsPlayerKind(KindOf(bot));

    /// <summary>The bot holds a lease of this kind (for this owner, when one is given).</summary>
    public static bool Holds(GameBot bot, eDutyLeaseKind kind, string owner = null)
    {
        BotDutyLease lease = Current(bot);
        return lease != null && lease.Kind == kind && (owner == null || string.Equals(lease.Owner, owner, StringComparison.Ordinal));
    }

    /// <summary>The bot holds a lease of this kind with this reason (a stage of the activity, e.g. a raid's reserved recruits).</summary>
    public static bool HoldsWithReason(GameBot bot, eDutyLeaseKind kind, string reason, string owner = null)
    {
        BotDutyLease lease = Current(bot);
        return lease != null && lease.Kind == kind && string.Equals(lease.Reason, reason, StringComparison.Ordinal) &&
               (owner == null || string.Equals(lease.Owner, owner, StringComparison.Ordinal));
    }

    /// <summary>Whether a claim would get the bot, without claiming or recording anything (planners' filters).</summary>
    public static bool WouldGrant(GameBot bot, eDutyLeaseKind kind, string owner = null) =>
        CanHold(bot) && BotDutyLeaseRules.Decide(Current(bot), kind, owner).Holds;

    /// <summary>Every bot holding a lease of this kind for this owner.</summary>
    public static GameBot[] HoldersOf(eDutyLeaseKind kind, string owner) =>
        Held.Where(pair => pair.Value.Kind == kind && string.Equals(pair.Value.Owner, owner, StringComparison.Ordinal))
            .Select(pair => pair.Key).ToArray();

    #endregion

    #region Claiming and ending

    /// <summary>
    /// Claims the bot for an activity. The rules decide (<see cref="BotDutyLeaseRules.Decide"/>); a granted or taking
    /// claim replaces whatever the bot held, a refusal leaves it. A same-owner renewal may change the reason (a raid
    /// recruit becomes a party member) and the group it is bound to.
    /// </summary>
    public static ClaimResult Claim(GameBot bot, eDutyLeaseKind kind, string owner, string reason,
        string ownerName = null, Group group = null, long expires = 0)
    {
        if (!CanHold(bot))
            return new(BotDutyLeaseRules.Outcome.Refused, DutyLeaseReason.NotAGamebot, null);
        lock (Sync)
        {
            BotDutyLease previous = Current(bot);
            BotDutyLeaseRules.Verdict verdict = BotDutyLeaseRules.Decide(previous, kind, owner);
            if (!verdict.Holds)
            {
                string key = $"{kind}|{owner}|{verdict.Reason}|{previous?.Kind}|{previous?.Owner}";
                if (!LastRefusal.TryGetValue(bot, out string last) || last != key)
                {
                    LastRefusal[bot] = key;
                    Record(bot, kind, owner, "refused", verdict.Reason, previous, log: true);
                }
                return new(verdict.Outcome, verdict.Reason, previous);
            }
            LastRefusal.TryRemove(bot, out _);
            long now = Now();
            if (verdict.Outcome == BotDutyLeaseRules.Outcome.Renewed)
            {
                if (!string.Equals(previous.Reason, reason, StringComparison.Ordinal) || !ReferenceEquals(previous.Group, group) ||
                    previous.Expires != expires)
                {
                    Held[bot] = previous with { Reason = reason, Group = group, Expires = expires, OwnerName = ownerName ?? previous.OwnerName };
                    Record(bot, kind, owner, "changed", reason, previous, log: true);
                }
                return new(verdict.Outcome, reason, previous);
            }
            Held[bot] = new BotDutyLease(kind, owner, ownerName ?? owner, BotDutyLeaseRules.Priority(kind), now, reason, expires, group);
            if (verdict.Outcome == BotDutyLeaseRules.Outcome.Taken && previous != null)
                Record(bot, previous.Kind, previous.Owner, "ended", $"{verdict.Reason}:{kind}", null, log: false);
            Record(bot, kind, owner, verdict.Outcome == BotDutyLeaseRules.Outcome.Taken ? "taken" : "granted", reason, previous, log: true);
            return new(verdict.Outcome, reason, previous);
        }
    }

    /// <summary>
    /// Moves the bot's lease from one of the player's kinds to the other in one step (bot-led groups, B2: /lead and
    /// /lead bot; <see cref="BotDutyLeaseRules.MayHandOver"/>). It applies only while the bot holds
    /// <paramref name="from"/> for <paramref name="fromOwner"/> (any owner when null); otherwise nothing changes. Logged
    /// as "handed over".
    /// </summary>
    public static bool HandOver(GameBot bot, eDutyLeaseKind from, string fromOwner, eDutyLeaseKind to, string owner, string reason,
        string ownerName = null, Group group = null)
    {
        if (!CanHold(bot) || !BotDutyLeaseRules.MayHandOver(from, to))
            return false;
        lock (Sync)
        {
            BotDutyLease previous = Current(bot);
            if (previous == null || previous.Kind != from || fromOwner != null && !string.Equals(previous.Owner, fromOwner, StringComparison.Ordinal))
                return false;
            Held[bot] = new BotDutyLease(to, owner, ownerName ?? owner, BotDutyLeaseRules.Priority(to), Now(), reason, 0, group);
            LastRefusal.TryRemove(bot, out _);
            Record(bot, previous.Kind, previous.Owner, "ended", $"handed_over:{to}", null, log: false);
            Record(bot, to, owner, "handed over", reason, previous, log: true);
            return true;
        }
    }

    /// <summary>Ends the bot's lease if it is of this kind (and owner, when given). Returns whether one ended.</summary>
    public static bool Release(GameBot bot, eDutyLeaseKind kind, string owner = null, string why = DutyLeaseReason.ActivityEnded)
    {
        if (bot == null)
            return false;
        lock (Sync)
        {
            if (!Held.TryGetValue(bot, out BotDutyLease lease) || lease.Kind != kind ||
                owner != null && !string.Equals(lease.Owner, owner, StringComparison.Ordinal))
                return false;
            if (!Held.TryRemove(new KeyValuePair<GameBot, BotDutyLease>(bot, lease)))
                return false;
            RecordEnd(bot, lease, why);
            return true;
        }
    }

    /// <summary>Ends every lease an activity holds (a raid that ended, an RvR force that left). Returns how many.</summary>
    public static int ReleaseOwner(eDutyLeaseKind kind, string owner, string why = DutyLeaseReason.ActivityEnded)
    {
        int ended = 0;
        foreach (GameBot bot in HoldersOf(kind, owner))
            if (Release(bot, kind, owner, why))
                ended++;
        return ended;
    }

    /// <summary>The bot died: ends the lease if its kind ends on death (<see cref="BotDutyLeaseRules.EndsOnDeath"/>).</summary>
    public static void OnDied(GameBot bot)
    {
        BotDutyLease lease = bot == null ? null : Current(bot);
        if (lease != null && BotDutyLeaseRules.EndsOnDeath(lease.Kind))
            Release(bot, lease.Kind, lease.Owner, DutyLeaseReason.Died);
    }

    /// <summary>The bot left the world (AutonomousBotRegistry.Unregister): whatever it held ends.</summary>
    public static void OnLoggedOut(GameBot bot)
    {
        if (bot == null)
            return;
        lock (Sync)
        {
            if (Held.TryRemove(bot, out BotDutyLease lease))
                RecordEnd(bot, lease, DutyLeaseReason.LoggedOut);
            LastRefusal.TryRemove(bot, out _);
        }
    }

    #endregion

    #region Decisions (/lswhy) and the log

    private static void RecordEnd(GameBot bot, BotDutyLease lease, string why) =>
        Record(bot, lease.Kind, lease.Owner, "ended", why, null, log: true);

    private static void Record(GameBot bot, eDutyLeaseKind kind, string owner, string outcome, string reason, BotDutyLease held, bool log)
    {
        var decision = new Decision(Now(), bot?.DatabaseID ?? 0, bot?.Name, bot?.Realm ?? eRealm.None, kind, owner, outcome, reason,
            held?.Kind ?? eDutyLeaseKind.None, held?.Owner);
        lock (Ring)
        {
            Ring[_next] = decision;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity)
                _count++;
        }
        if (log && Log.IsInfoEnabled)
            Log.Info($"DUTY_LEASE bot=\"{bot?.Name}\" id={bot?.DatabaseID} outcome={outcome} kind={kind} owner=\"{owner}\" reason={reason}" +
                     (held != null && outcome != "changed" ? $" held={held.Kind} heldOwner=\"{held.Owner}\"" : string.Empty));
    }

    /// <summary>The newest decisions about one bot (by id, or by name when the id is unknown), at most <paramref name="max"/>.</summary>
    public static List<Decision> ForBot(long botId, string name, int max, eRealm realm = eRealm.None)
    {
        var found = new List<Decision>(Math.Clamp(max, 0, 32));
        lock (Ring)
        {
            for (int i = 0; i < _count && found.Count < max; i++)
            {
                Decision decision = Ring[(_next - 1 - i + Capacity) % Capacity];
                if ((botId > 0 ? decision.BotId == botId : !string.IsNullOrEmpty(name) && string.Equals(decision.Bot, name, StringComparison.OrdinalIgnoreCase)) &&
                    (realm == eRealm.None || decision.Realm == realm))
                    found.Add(decision);
            }
        }
        return found;
    }

    private static string Ago(long at, long now)
    {
        long seconds = Math.Max(0, now - at) / 1000;
        return seconds < 120 ? $"{seconds}s ago" : seconds < 7200 ? $"{seconds / 60}m ago" : $"{seconds / 3600}h ago";
    }

    /// <summary>The bot's lease as one line: "duty: realm raid (Gjalpinulva) since 4m, raid_party".</summary>
    public static string DescribeLease(BotDutyLease lease, long now)
    {
        if (lease == null)
            return "duty: free (no lease)";
        var text = new StringBuilder(96).Append("duty: ").Append(BotDutyLeaseRules.Label(lease.Kind));
        if (!string.IsNullOrEmpty(lease.OwnerName))
            text.Append(" (").Append(lease.OwnerName).Append(')');
        text.Append(", since ").Append(Ago(lease.Since, now).Replace(" ago", string.Empty)).Append(", ").Append(lease.Reason)
            .Append(", priority ").Append(lease.Priority);
        if (lease.Expires > 0)
            text.Append(", ends in ").Append(Math.Max(0, lease.Expires - now) / 1000).Append('s');
        return text.ToString();
    }

    /// <summary>A decision as one line: "12s ago granted realm raid (dragon-alb): raid_reserved, took bot group (g-12)".</summary>
    public static string Describe(in Decision decision, long now)
    {
        var text = new StringBuilder(128).Append(Ago(decision.At, now)).Append(' ').Append(decision.Outcome).Append(' ')
            .Append(BotDutyLeaseRules.Label(decision.Kind));
        if (!string.IsNullOrEmpty(decision.Owner))
            text.Append(" (").Append(decision.Owner).Append(')');
        if (!string.IsNullOrEmpty(decision.Reason))
            text.Append(": ").Append(decision.Reason);
        if (decision.HeldKind != eDutyLeaseKind.None && decision.Outcome is "refused" or "taken")
        {
            text.Append(decision.Outcome == "taken" ? ", took " : ", held by ").Append(BotDutyLeaseRules.Label(decision.HeldKind));
            if (!string.IsNullOrEmpty(decision.HeldOwner))
                text.Append(" (").Append(decision.HeldOwner).Append(')');
        }
        return text.ToString();
    }

    /// <summary>
    /// What /lswhy shows about a bot's time: its lease now and its last <paramref name="decisions"/> claim decisions (granted,
    /// refused and why, ended), newest first. <paramref name="bot"/> may be null for a bot that is offline (decisions by name);
    /// <paramref name="realm"/> limits those to one realm's bots (a player reads only their own realm's).
    /// </summary>
    public static List<string> WhyLines(GameBot bot, string name, long now, int decisions, eRealm realm = eRealm.None)
    {
        var lines = new List<string>(decisions + 1);
        if (bot != null)
            lines.Add(DescribeLease(Current(bot, now), now));
        else
            lines.Add("duty: not online");
        foreach (Decision decision in ForBot(bot?.DatabaseID ?? 0, bot?.Name ?? name, decisions, realm))
            lines.Add("  " + Describe(decision, now));
        return lines;
    }

    /// <summary>Forgets every lease and decision (tests).</summary>
    public static void Clear()
    {
        lock (Sync)
        {
            Held.Clear();
            LastRefusal.Clear();
        }
        lock (Ring)
        {
            Array.Clear(Ring);
            _next = 0;
            _count = 0;
        }
    }

    #endregion
}
