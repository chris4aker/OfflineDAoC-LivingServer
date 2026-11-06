using System;
using System.Collections.Generic;

namespace DOL.GS;

/// <summary>
/// Bot-led groups, step B3 (docs/BOT-LED-GROUPS.md, "Raid leaders"; technical plan step 8): the pure rules of a realm
/// raid's leader inviting the real player. The raid leader is a level-50 officer of a raiding-charter guild in the raid
/// (else the first party's leader); a level-50 player of the raid's realm gets one invite per raid, by tell and a box, to
/// a dragon or epic dungeon raid that has not begun its battle; yes puts the player in a party that needs their class,
/// and one gamebot of the player's role steps out when the party is full. Tested in UT_RaidLeaderInvite; the glue is
/// AutonomousRealmRaid.Players.cs and AutonomousBotGroupCoordinator.RaidGuest.cs.
/// </summary>
public static class RealmRaidPlayerInvite
{
    /// <summary>Only level 50s are invited (the raids recruit level 50 gamebots only).</summary>
    public const int PlayerLevel = 50;

    /// <summary>A raid party holds eight members, the player included.</summary>
    public const int PartySize = 8;

    /// <summary>The guild ranks that count as officers: 0 the guild leader, 1 and 2 its officers (AutonomousBotGuildPlanner).</summary>
    public const int HighestOfficerRank = 2;

    /// <summary>What the invite needs to know about the player and the raid. Pure data.</summary>
    public readonly record struct Check(bool SettingOn, bool PlayerInvitesOff, int PlayerLevel, eRealm PlayerRealm, eRealm RaidRealm,
        bool Neutral, bool Started, bool AlreadyInvited, bool JustLoggedIn, bool BoxWaiting, string StateBlocks);

    /// <summary>
    /// Why the raid leader doesn't invite this player now, or null when it does: bot_raid_invite_player off, /invites
    /// off, under level 50, another realm's raid, a neutral raid (Darkness Falls and the like: "a dragon or epic dungeon
    /// raid forms in your realm"), a raid already in its battle, invited already (one invite per raid; a no is final),
    /// another box waiting, a "never" state of the group invite (fighting, grouped, riding...), or the first 10 minutes
    /// online.
    /// </summary>
    public static string InviteBlocks(in Check check) =>
        !check.SettingOn ? "raid_invites_off"
        : check.PlayerInvitesOff ? "invites_off_by_player"
        : check.PlayerLevel < PlayerLevel ? "level"
        : check.PlayerRealm != check.RaidRealm ? "other_realm"
        : check.Neutral ? "neutral_raid"
        : check.Started ? "raid_started"
        : check.AlreadyInvited ? "invited_this_raid"
        : check.BoxWaiting ? "box_waiting"
        : check.StateBlocks ?? (check.JustLoggedIn ? "just_logged_in" : null);

    /// <summary>A gamebot of the raid as the pick of its leader sees it. Pure data.</summary>
    public readonly record struct LeaderCandidate(long BotId, int Level, bool RaidingGuild, int GuildRank, int PartyOrder, bool LeadsItsParty);

    /// <summary>
    /// The raid leader: a level-50 officer (rank 0 to 2) of a raiding-charter guild in the raid, the highest rank first,
    /// then the earliest party, then the lowest id; with none, the leader of the earliest party. -1 when the raid has no
    /// party yet.
    /// </summary>
    public static int PickRaidLeader(IReadOnlyList<LeaderCandidate> candidates)
    {
        int officer = -1, partyLeader = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            LeaderCandidate c = candidates[i];
            if (c.Level >= PlayerLevel && c.RaidingGuild && c.GuildRank >= 0 && c.GuildRank <= HighestOfficerRank &&
                (officer < 0 || Before(c, candidates[officer])))
                officer = i;
            if (c.LeadsItsParty && (partyLeader < 0 || c.PartyOrder < candidates[partyLeader].PartyOrder ||
                                    c.PartyOrder == candidates[partyLeader].PartyOrder && c.BotId < candidates[partyLeader].BotId))
                partyLeader = i;
        }
        return officer >= 0 ? officer : partyLeader;

        static bool Before(LeaderCandidate a, LeaderCandidate b) =>
            a.GuildRank != b.GuildRank ? a.GuildRank < b.GuildRank
            : a.PartyOrder != b.PartyOrder ? a.PartyOrder < b.PartyOrder
            : a.BotId < b.BotId;
    }

    /// <summary>One raid party as the player's seat sees it. <paramref name="SameRole"/>: its gamebots of the player's role.</summary>
    public readonly record struct PartyOption(int Members, int SameRole, bool HasPlayer, bool SpecialForces, bool Departed);

    /// <summary>
    /// The party the player joins, or -1: never one with a player in it already or an Epic Special Forces party; a party
    /// still at the muster hub before one that left; a party with room before a full one (a full one needs a gamebot of
    /// the player's role to step out); the one with the fewest of the player's role ("a party that needs your class"),
    /// the first of equals. <paramref name="stepOut"/>: a gamebot of the player's role has to step out.
    /// </summary>
    public static int PickParty(IReadOnlyList<PartyOption> parties, out bool stepOut)
    {
        int best = -1;
        stepOut = false;
        for (int i = 0; i < parties.Count; i++)
        {
            PartyOption p = parties[i];
            bool full = p.Members >= PartySize;
            if (p.HasPlayer || p.SpecialForces || full && p.SameRole == 0)
                continue;
            if (best < 0 || Key(p).CompareTo(Key(parties[best])) < 0)
                best = i;
        }
        if (best >= 0)
            stepOut = parties[best].Members >= PartySize;
        return best;

        static (int, int, int) Key(PartyOption p) => (p.Departed ? 1 : 0, p.Members >= PartySize ? 1 : 0, p.SameRole);
    }

    /// <summary>A gamebot of the chosen party as the pick of the one who steps out sees it. Pure data.</summary>
    public readonly record struct Seat(bool SameRole, bool LeadsParty, bool LeadsRaid, bool Alive);

    /// <summary>
    /// The gamebot that steps out to make room for the player: one of the player's role that leads neither the party nor
    /// the raid, a dead one first (it would rejoin late anyway), else the last of them. -1 when there is none.
    /// </summary>
    public static int PickStepOut(IReadOnlyList<Seat> seats)
    {
        int pick = -1;
        for (int i = 0; i < seats.Count; i++)
        {
            Seat s = seats[i];
            if (!s.SameRole || s.LeadsParty || s.LeadsRaid)
                continue;
            if (pick < 0 || !s.Alive || seats[pick].Alive)
                pick = i;
            if (!s.Alive)
                break;
        }
        return pick;
    }

    /// <summary>
    /// The party's gamebot target after the player takes a seat (replacements fill only up to it): one seat is the
    /// player's. Restored by one when the player leaves, so the raid refills the seat with a gamebot.
    /// </summary>
    public static int TargetWithPlayer(int target) => Math.Max(1, Math.Min(target, PartySize - 1));

    public static int TargetWithoutPlayer(int targetWithPlayer, int originalTarget) => Math.Max(targetWithPlayer, Math.Min(PartySize, originalTarget));

    /// <summary>What the raid leader says by tell, plainly (no voice line fits). {raid} and {destination} are filled in.</summary>
    public const string FallbackTell = "Forming for {raid} at {destination}. Want a spot?";

    /// <summary>The box, in the native invite's words.</summary>
    public static string BoxText(string leaderName, string raidName) =>
        $"{leaderName} invites you to join the {raidName} raid. Do you wish to join?";
}
