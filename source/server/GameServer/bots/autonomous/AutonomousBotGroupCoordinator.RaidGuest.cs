using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// Bot-led groups, step B3 (docs/BOT-LED-GROUPS.md, "Raid leaders"): the real player in a realm raid's party. The party
/// stays the raid's: its gamebots keep their RealmRaid lease (so the raid keeps them, and nothing of lower priority
/// takes them), and its session runs the raid as before. The player is hosted as a raid guest
/// (<see cref="BotLedGroups.HostedView.RaidGuest"/>): no follow rules (the party waits at the muster hub until the raid
/// leaves, and after that the player catches up on their own), no porter rides with the player, but the hosted player's
/// quest credit, a gamebot leader when one leaves, and the group's loot share apply.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    private static RealmRaidPlayerInvite.PartyOption[] SeatOptions(AutonomousRealmRaid.PlayerRaid raid, BotPveGroupRole role,
        out Session[] sessions)
    {
        sessions = new Session[raid.Parties.Length];
        var options = new RealmRaidPlayerInvite.PartyOption[raid.Parties.Length];
        for (int i = 0; i < raid.Parties.Length; i++)
        {
            Group group = raid.Parties[i];
            TryGetSession(group, out Session session);
            sessions[i] = session;
            GameBot[] bots = BotMembers(group);
            int sameRole = session == null ? 0
                : bots.Count(bot => session.PveRoles.TryGetValue(MemberKey(bot), out BotPveGroupRole held) && held == role);
            options[i] = new RealmRaidPlayerInvite.PartyOption(group.MemberCount, sameRole,
                session == null || session.Ending || group.GetPlayersInTheGroup().Count > 0, raid.SpecialForces[i], raid.Departed[i]);
        }
        return options;
    }

    private static BotPveGroupRole RoleOf(GamePlayer player) => player?.CharacterClass == null ? BotPveGroupRole.Attacker
        : BotLedGroups.PlayerRole((eCharacterClass)player.CharacterClass.ID);

    /// <summary>The raid has a party the player could join now (checked before the invite is sent).</summary>
    internal static bool HasSeatFor(AutonomousRealmRaid.PlayerRaid raid, GamePlayer player)
    {
        if (raid == null || player == null)
            return false;
        lock (Sync)
            return RealmRaidPlayerInvite.PickParty(SeatOptions(raid, RoleOf(player), out _), out _) >= 0;
    }

    /// <summary>
    /// The player said yes to the raid leader: they join the party that needs their class most. A full party first lets
    /// one gamebot of the player's role step out (it leaves the raid's roster and goes back to its own goals, where a raid
    /// refill may take it again). The party's gamebots keep their raid lease; the player is hosted as a raid guest.
    /// </summary>
    internal static bool TrySeatRaidGuest(string raidId, GamePlayer player, out string why)
    {
        why = null;
        if (player == null || player.Group != null)
        {
            why = "grouped";
            return false;
        }
        lock (Sync)
        {
            AutonomousRealmRaid.PlayerRaid raid = AutonomousRealmRaid.RaidsOfRealm(player.Realm).FirstOrDefault(r => r.Id == raidId);
            if (raid == null || raid.Started)
            {
                why = raid == null ? "raid_ended" : "raid_started";
                return false;
            }
            BotPveGroupRole role = RoleOf(player);
            int pick = RealmRaidPlayerInvite.PickParty(SeatOptions(raid, role, out Session[] sessions), out bool stepOut);
            if (pick < 0)
            {
                why = "no_party";
                return false;
            }
            Group group = raid.Parties[pick];
            Session session = sessions[pick];
            GameBot[] bots = BotMembers(group);
            GameBot steppingOut = null;
            if (stepOut)
            {
                GameBot partyLeader = ChooseLeader(session, bots);
                int seat = RealmRaidPlayerInvite.PickStepOut(bots.Select(bot => new RealmRaidPlayerInvite.Seat(
                    session.PveRoles.TryGetValue(MemberKey(bot), out BotPveGroupRole held) && held == role,
                    bot == partyLeader, bot == raid.Leader, bot.IsAlive)).ToArray());
                if (seat < 0)
                {
                    why = "no_seat";
                    return false;
                }
                steppingOut = bots[seat];
            }
            if (!AutonomousRealmRaid.SeatPlayer(group, steppingOut))
            {
                why = "raid_ended";
                return false;
            }
            if (steppingOut != null)
            {
                session.PveRoles.Remove(MemberKey(steppingOut));
                session.ProcessingAttendanceRemovals = true;
                try
                {
                    group.RemoveMember(steppingOut, retainSingleRemainingMember: true);
                }
                finally
                {
                    session.ProcessingAttendanceRemovals = false;
                }
                AutonomousObjectiveAssignments.BeginSoloAfterGroupTask(steppingOut, "Stepped out of a raid party to make room for a player");
            }
            session.LockedSize = BotMembers(group).Length;
            if (!group.AddMember(player))
            {
                AutonomousRealmRaid.OnPlayerLeftParty(group);
                why = "full";
                return false;
            }
            session.HostedPlayer = player;
            session.PlayerRole = role;
            session.Follow = null;
            BotLedGroups.SetHosted(group, new BotLedGroups.HostedView(player, session.Id) { RaidGuest = true });
            GameBot leader = ChooseLeader(session, BotMembers(group));
            if (leader != null)
                BotLedGroups.Announce(leader, BotLedGroups.Lead.RaidWelcome, player, raid.HubName, raid.Name);
            WriteSessionMetadata(session, BotMembers(group));
            if (Log.IsInfoEnabled)
                Log.Info($"RAID_PLAYER_INVITE outcome=seated event={raid.Id} group={session.Id} player=\"{player.Name}\" role={role} " +
                         $"steppedOut=\"{steppingOut?.Name}\" party={pick} bots={BotMembers(group).Length}");
            return true;
        }
    }
}
