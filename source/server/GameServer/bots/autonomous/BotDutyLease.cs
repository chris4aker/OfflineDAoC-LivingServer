using System;

namespace DOL.GS;

/// <summary>
/// What a gamebot's time is claimed for (docs/DUTY-LEASE.md). Each autonomous gamebot holds at most one lease at a
/// time; the order of the values is not the priority (see <see cref="BotDutyLeaseRules.Priority"/>).
/// </summary>
public enum eDutyLeaseKind : byte
{
    None = 0,
    /// <summary>A bot-only party run by AutonomousBotGroupCoordinator (Group PvE or an RvR warband).</summary>
    BotGroupSession = 1,
    /// <summary>A battleground keep assault: the bot is committed to its realm's rally on the lord.</summary>
    BattlegroundWave = 2,
    /// <summary>A siege, keep defense or relic battle: the bot marches with one of the event's forces.</summary>
    RvrEvent = 3,
    /// <summary>A realm raid (dragon, epic or neutral dungeon): reserved as a recruit, or in one of its parties.</summary>
    RealmRaid = 4,
    /// <summary>
    /// A bot-led group that a real player has joined (docs/BOT-LED-GROUPS.md, B1): every gamebot of the party holds it,
    /// bound to the group (AutonomousBotGroupCoordinator.TryHostPlayer), until the player or the bot leaves.
    /// </summary>
    BotLedGroupWithPlayer = 5,
    /// <summary>The bot is in a real player's group and the player leads (/invite, a handover).</summary>
    PlayerGroup = 6,
}

/// <summary>
/// The reason codes of the duty lease: why a lease is held, why a claim was refused, why a lease ended. They are what
/// /lswhy shows and what the DUTY_LEASE log lines carry, so they are short, stable and lowercase.
/// </summary>
public static class DutyLeaseReason
{
    // Why a lease is held.
    public const string PlayerGroup = "player_group";
    public const string HostingPlayer = "hosting_player";
    public const string RaidReserved = "raid_reserved";
    public const string RaidParty = "raid_party";
    public const string RvrForce = "rvr_force";
    public const string SiegeParty = "siege_party";
    public const string BattlegroundRally = "bg_rally";
    public const string GroupSession = "group_session";

    // Why a claim was refused.
    public const string InPlayersGroup = "in_players_group";
    public const string OnRealmRaid = "on_realm_raid";
    public const string OnRvrEvent = "on_rvr_event";
    public const string OnBattleground = "on_battleground";
    public const string InBotGroup = "in_bot_group";
    public const string NotAGamebot = "not_a_gamebot";

    // Why a lease ended.
    public const string ActivityEnded = "activity_ended";
    public const string LeftGroup = "left_group";
    public const string LoggedOut = "logged_out";
    public const string Died = "died";
    public const string Expired = "expired";
    public const string Taken = "taken";
    public const string Moved = "moved";

    /// <summary>The refusal a held lease of this kind gives a claim it doesn't yield to.</summary>
    public static string HeldBy(eDutyLeaseKind held) => held switch
    {
        eDutyLeaseKind.PlayerGroup => InPlayersGroup,
        eDutyLeaseKind.BotLedGroupWithPlayer => HostingPlayer,
        eDutyLeaseKind.RealmRaid => OnRealmRaid,
        eDutyLeaseKind.RvrEvent => OnRvrEvent,
        eDutyLeaseKind.BattlegroundWave => OnBattleground,
        eDutyLeaseKind.BotGroupSession => InBotGroup,
        _ => string.Empty,
    };
}

/// <summary>
/// One claim on a gamebot's time. <paramref name="Owner"/> is the claimant's id (a raid id, an RvR force id, a
/// group session id, a battleground rally, a player's name); <paramref name="Since"/> and <paramref name="Expires"/>
/// are game-loop ticks (0 = no expiry). A lease taken for a group (<paramref name="Group"/>) ends when the bot leaves
/// that group.
/// </summary>
public sealed record BotDutyLease(eDutyLeaseKind Kind, string Owner, string OwnerName, int Priority, long Since, string Reason,
    long Expires = 0, Group Group = null)
{
    public bool IsExpired(long now) => Expires > 0 && now >= Expires;
}

/// <summary>
/// The pure rules of the duty lease: priorities, which claim may take a bot from which, what ends a lease and what
/// survives a restart. No world state is read here, so every rule is tested on its own (UT_BotDutyLeaseRules).
/// </summary>
public static class BotDutyLeaseRules
{
    public enum Outcome { Granted, Renewed, Taken, Refused }

    public readonly record struct Verdict(Outcome Outcome, string Reason)
    {
        public bool Holds => Outcome != Outcome.Refused;
    }

    /// <summary>
    /// Higher wins where taking is allowed. A player's own group is the top claim and is never taken by anything
    /// automatic; a raid outranks an RvR event and a battleground rally (raid recruitment never waited for the frontier),
    /// which outrank an ordinary bot group. An RvR event and a battleground rally are equal (owner 2026-11-04: the
    /// frontier is for level 50s, the battlegrounds for the levels below), so neither pulls a bot from the other.
    /// </summary>
    public static int Priority(eDutyLeaseKind kind) => kind switch
    {
        eDutyLeaseKind.PlayerGroup => 100,
        eDutyLeaseKind.BotLedGroupWithPlayer => 90,
        eDutyLeaseKind.RealmRaid => 40,
        eDutyLeaseKind.RvrEvent => 30,
        eDutyLeaseKind.BattlegroundWave => 30,
        eDutyLeaseKind.BotGroupSession => 10,
        _ => 0,
    };

    /// <summary>The two kinds that mean a real player has the bot: no raid, siege, RvR event or rally takes it.</summary>
    public static bool IsPlayerKind(eDutyLeaseKind kind) => kind is eDutyLeaseKind.PlayerGroup or eDutyLeaseKind.BotLedGroupWithPlayer;

    /// <summary>
    /// Whether a claim of <paramref name="kind"/> for <paramref name="owner"/> gets a bot that holds
    /// <paramref name="held"/> (null: nothing).
    /// <list type="bullet">
    /// <item>Nothing held: granted. The same kind and owner again: renewed.</item>
    /// <item>A player's group (the player leads) always gets the bot: the invite's own duty checks (dead, siege, relic,
    /// RvR march, raid reservation, stable route) run before it, in BotGroupInvite, and a handover moves the bot to
    /// the player who stays.</item>
    /// <item>A bot a player has (either player kind) is never taken by anything else.</item>
    /// <item>A bot-led group taking in a player (bot-led groups, B1) takes only its own kind of hold, an ordinary
    /// bot group; a raid, an RvR event or a rally keeps the bot.</item>
    /// <item>Otherwise the higher priority takes the lower. Among equals of the same kind the newer claim moves the bot
    /// (one raid reservation, one RvR force, one rally, one group at a time, as before the lease); among equals of
    /// different kinds (an RvR event and a battleground rally) the bot keeps what it is doing. A lower claim is
    /// refused.</item>
    /// </list>
    /// </summary>
    public static Verdict Decide(BotDutyLease held, eDutyLeaseKind kind, string owner)
    {
        if (kind == eDutyLeaseKind.None)
            return new(Outcome.Refused, string.Empty);
        if (held == null || held.Kind == eDutyLeaseKind.None)
            return new(Outcome.Granted, string.Empty);
        if (held.Kind == kind && string.Equals(held.Owner, owner, StringComparison.Ordinal))
            return new(Outcome.Renewed, string.Empty);
        if (kind == eDutyLeaseKind.PlayerGroup)
            return new(Outcome.Taken, held.Kind == kind ? DutyLeaseReason.Moved : DutyLeaseReason.Taken);
        if (IsPlayerKind(held.Kind))
            return new(Outcome.Refused, DutyLeaseReason.HeldBy(held.Kind));
        if (kind == eDutyLeaseKind.BotLedGroupWithPlayer)
            return held.Kind == eDutyLeaseKind.BotGroupSession
                ? new(Outcome.Taken, DutyLeaseReason.Taken)
                : new(Outcome.Refused, DutyLeaseReason.HeldBy(held.Kind));
        int incoming = Priority(kind), current = Priority(held.Kind);
        if (incoming > current)
            return new(Outcome.Taken, DutyLeaseReason.Taken);
        if (incoming == current)
            return held.Kind == kind
                ? new(Outcome.Taken, DutyLeaseReason.Moved)
                : new(Outcome.Refused, DutyLeaseReason.HeldBy(held.Kind));
        return new(Outcome.Refused, DutyLeaseReason.HeldBy(held.Kind));
    }

    /// <summary>
    /// Dying ends only a battleground rally lease (the rally drops its dead and they rejoin it later). A raider is
    /// rezzed or runs back to its raid, a bot in a player's group stays in it, an RvR force waits for its dead, and a
    /// bot group rezzes its members: those leases outlive a death.
    /// </summary>
    public static bool EndsOnDeath(eDutyLeaseKind kind) => kind == eDutyLeaseKind.BattlegroundWave;

    /// <summary>
    /// No lease survives a server restart. Every claimant lives in memory only (raids, RvR events, battleground
    /// rallies, bot groups and players' groups are all rebuilt from nothing at start-up), so a saved lease would point
    /// at an activity that no longer exists. A bot that logs out loses its lease for the same reason.
    /// </summary>
    public static bool PersistsAcrossRestart(eDutyLeaseKind kind) => false;

    /// <summary>
    /// The watchdog's 15- and 45-minute clocks never relocate a bot a player has: the player decides where it goes.
    /// The other kinds are not exempt by themselves (a raider stuck on its way must still be rescued); their activity
    /// says when the bot is legitimately waiting or fighting (AutonomousRealmRaid.Protects / IsInRally,
    /// AutonomousRvrEventLayer.ProtectsParticipantFromInactivity, the group coordinator's holds).
    /// </summary>
    public static bool ExemptsFromWatchdog(eDutyLeaseKind kind) => IsPlayerKind(kind);

    /// <summary>
    /// A handover between the player's two kinds (bot-led groups, B2): /lead moves the party's gamebots from "a bot-led
    /// group with a player" to "the player's group", /lead bot (or /makeleader) moves them back. The bot never stops being
    /// spoken for by the player on the way, so nothing automatic can take it in between. Any other change goes through
    /// <see cref="Decide"/>.
    /// </summary>
    public static bool MayHandOver(eDutyLeaseKind held, eDutyLeaseKind next) =>
        held != next && IsPlayerKind(held) && IsPlayerKind(next);

    /// <summary>A short label for players (/lswhy).</summary>
    public static string Label(eDutyLeaseKind kind) => kind switch
    {
        eDutyLeaseKind.PlayerGroup => "in a player's group",
        eDutyLeaseKind.BotLedGroupWithPlayer => "leading a group with a player",
        eDutyLeaseKind.RealmRaid => "realm raid",
        eDutyLeaseKind.RvrEvent => "siege or RvR event",
        eDutyLeaseKind.BattlegroundWave => "battleground assault",
        eDutyLeaseKind.BotGroupSession => "bot group",
        _ => "free",
    };
}
