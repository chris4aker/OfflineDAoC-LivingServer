using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// Bot-led groups, step B3 (docs/BOT-LED-GROUPS.md, "Raid leaders"): a realm raid's leader and the real player. The
    /// raid leader (<see cref="RaidLeader"/>) is a level-50 officer of a raiding-charter guild in the raid, else the first
    /// party's leader. While a dragon or epic dungeon raid of the player's realm has not begun its battle, the raid leader
    /// may send a level-50 player one tell and one box; yes seats the player in a party that needs their class
    /// (AutonomousBotGroupCoordinator.TrySeatRaidGuest). The rules are RealmRaidPlayerInvite's. Kept apart from the raid's
    /// own file so its recent changes (sizing, the Epic Special Forces) merge without overlap.
    /// </summary>
    public static partial class AutonomousRealmRaid
    {
        /// <summary>A raid as the player invite sees it (a snapshot taken under the raid's lock).</summary>
        public sealed record PlayerRaid(string Id, long Created, string Name, eRealm Realm, bool Neutral, bool Started, string HubName,
            Group[] Parties, bool[] Departed, bool[] SpecialForces, GameBot Leader);

        /// <summary>The players each running raid has invited (one invite per raid; a no is final for that raid).</summary>
        private static readonly Dictionary<(string Id, long Created), HashSet<string>> InvitedPlayers = new();

        /// <summary>A party's gamebot target before the player took a seat in it, restored when the player leaves.</summary>
        private static readonly Dictionary<Group, int> PlayerSeatTargets = new(ReferenceEqualityComparer.Instance);

        /// <summary>bot_raid_invite_player: a raid leader may invite the player.</summary>
        public static bool PlayerInvitesOn => Properties.BOT_RAID_INVITE_PLAYER;

        /// <summary>The leader of a running raid (see <see cref="RealmRaidPlayerInvite.PickRaidLeader"/>), or null.</summary>
        public static GameBot RaidLeader(string id)
        {
            lock (Sync)
                return id != null && Raids.TryGetValue(id, out Raid raid) ? RaidLeaderOf(raid) : null;
        }

        private static GameBot RaidLeaderOf(Raid raid)
        {
            var bots = new List<GameBot>();
            var candidates = new List<RealmRaidPlayerInvite.LeaderCandidate>();
            int order = 0;
            foreach ((Group group, Party party) in raid.Parties)
            {
                foreach (GameBot bot in party.Members.Where(b => b != null && b.Group == group))
                {
                    bool raiding = bot.Guild != null && AutonomousBotRealmGuilds.TryGetBotGuild(bot.Guild.GuildID, out var info) &&
                                   info.Charter == BotGuildCharter.Raiding;
                    bots.Add(bot);
                    candidates.Add(new(bot.DatabaseID, bot.Level, raiding, bot.GuildRank?.RankLevel ?? AutonomousBotGuildPlanner.LowestRank,
                        order, group.LivingLeader == bot));
                }
                order++;
            }
            int pick = RealmRaidPlayerInvite.PickRaidLeader(candidates);
            return pick >= 0 ? bots[pick] : null;
        }

        /// <summary>The running raids of a realm, for the player invite.</summary>
        internal static PlayerRaid[] RaidsOfRealm(eRealm realm)
        {
            lock (Sync)
            {
                // Forget the invites of raids that ended.
                foreach (var key in InvitedPlayers.Keys.Where(k => !Raids.TryGetValue(k.Id, out Raid r) || r.Created != k.Created).ToArray())
                    InvitedPlayers.Remove(key);
                return Raids.Values.Where(r => r.Definition.Realm == realm).Select(r =>
                {
                    Group[] groups = r.Parties.Keys.ToArray();
                    return new PlayerRaid(r.Definition.Id, r.Created, r.Definition.Name, r.Definition.Realm, r.Definition.IsNeutral, r.Started,
                        r.Hub?.Name ?? r.Definition.Name, groups, groups.Select(g => r.Parties[g].Departed).ToArray(),
                        groups.Select(g => r.Parties[g].Members.Length > 0 && r.Parties[g].Members.All(b => b.IsEpicSpecialForce)).ToArray(),
                        RaidLeaderOf(r));
                }).ToArray();
            }
        }

        internal static bool WasInvited(string id, long created, string player)
        {
            lock (Sync)
                return InvitedPlayers.TryGetValue((id, created), out var names) && names.Contains(player);
        }

        /// <summary>Remembers that this raid invited the player (once per raid). False when it already had.</summary>
        internal static bool MarkInvited(string id, long created, string player)
        {
            lock (Sync)
            {
                if (!InvitedPlayers.TryGetValue((id, created), out var names))
                    InvitedPlayers[(id, created)] = names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                return names.Add(player);
            }
        }

        /// <summary>
        /// The player takes a seat in this raid party: <paramref name="steppingOut"/> (a gamebot of the player's role, or
        /// null when the party has room) leaves the party's roster and its raid lease, and the party's gamebot target drops
        /// to leave the player's seat unfilled by replacements. False when the group is not one of a running raid's parties.
        /// </summary>
        internal static bool SeatPlayer(Group group, GameBot steppingOut)
        {
            if (group == null) return false;
            lock (Sync)
            {
                if (!Membership.TryGetValue(group, out Raid raid) || !raid.Parties.TryGetValue(group, out Party party)) return false;
                if (steppingOut != null)
                {
                    party.Members = party.Members.Where(member => member != steppingOut).ToArray();
                    party.CatchingUp.Remove(steppingOut.DatabaseID);
                    EndPartyLease(steppingOut, raid.Definition.Id);
                    raid.Support = raid.Parties.Values.SelectMany(p => p.Members).Cast<GameLiving>().ToArray();
                }
                PlayerSeatTargets.TryAdd(group, party.Target);
                party.Target = RealmRaidPlayerInvite.TargetWithPlayer(party.Target);
                return true;
            }
        }

        /// <summary>The player left a raid party: the seat goes back to the raid, whose refills give it to a gamebot.</summary>
        internal static void OnPlayerLeftParty(Group group)
        {
            if (group == null) return;
            lock (Sync)
            {
                if (!PlayerSeatTargets.Remove(group, out int original)) return;
                if (Membership.TryGetValue(group, out Raid raid) && raid.Parties.TryGetValue(group, out Party party))
                    party.Target = RealmRaidPlayerInvite.TargetWithoutPlayer(party.Target, original);
            }
        }

        private static long _nextPlayerInvitePulse;

        /// <summary>
        /// Every few seconds (BotLedGroups.Pulse): each level-50 player of a realm with a forming raid may get the raid
        /// leader's one invite.
        /// </summary>
        internal static void PulsePlayerInvites(long now)
        {
            if (now < _nextPlayerInvitePulse) return;
            _nextPlayerInvitePulse = now + BotLedGroups.PulseIntervalMs;
            bool any;
            lock (Sync) any = Raids.Count > 0;
            if (!any) return;
            foreach (GamePlayer player in ClientService.Instance.GetPlayers())
            {
                if (player?.Client == null || player.ObjectState != GameObject.eObjectState.Active || player.Level < RealmRaidPlayerInvite.PlayerLevel)
                    continue;
                try
                {
                    TryInvitePlayer(player, now);
                }
                catch (Exception exception)
                {
                    Log.Warn($"RAID_PLAYER_INVITE pulse failed for {player.Name}", exception);
                }
            }
        }

        /// <summary>The raid leader's one invite to this player, when a raid of the player's realm may send it now.</summary>
        internal static bool TryInvitePlayer(GamePlayer player, long now)
        {
            BotLedGroups.InviteMemory memory = BotLedGroups.MemoryOf(player, now);
            string state = BotLedGroups.StateBlocks(BotLedGroups.StateOf(player));
            foreach (PlayerRaid raid in RaidsOfRealm(player.Realm))
            {
                var check = new RealmRaidPlayerInvite.Check(PlayerInvitesOn, memory.PlayerInvitesOff, player.Level, player.Realm, raid.Realm,
                    raid.Neutral, raid.Started, WasInvited(raid.Id, raid.Created, player.Name),
                    now - memory.OnlineSince < BotLedGroups.FirstInviteAfterLoginMs, memory.Pending, state);
                if (RealmRaidPlayerInvite.InviteBlocks(check) != null || raid.Leader == null ||
                    !AutonomousBotGroupCoordinator.HasSeatFor(raid, player))
                    continue;
                if (!MarkInvited(raid.Id, raid.Created, player.Name))
                    continue;
                memory.Pending = true;
                // The invite is that period's bot tell, as a group leader's is.
                AutonomousLivingServer.CountInviteAsTell(player, now);
                LogPlayerInvite("sent", raid, player, "unasked");
                SendRaidInvite(raid, player);
                return true;
            }
            return false;
        }

        private static void SendRaidInvite(PlayerRaid raid, GamePlayer player)
        {
            GameBot leader = raid.Leader;
            var slots = new Dictionary<string, string>(StringComparer.Ordinal) { ["raid"] = raid.Name, ["destination"] = raid.HubName };
            string tell = AutonomousBotVoices.TryLine(leader, "tell", out AutonomousBotVoiceCorpus.Picked picked, topic: "lead_raid_invite",
                slots: slots, flags: ["lead_call"])
                ? picked.Text
                : BotLedGroups.Fill(RealmRaidPlayerInvite.FallbackTell, slots);
            // Timed on the player, so a leader who logs off in between can't leave the player's box waiting for ever.
            AutonomousLivingServer.Later(player, 1_500 + Random.Shared.Next(2_500), null, () =>
            {
                if (leader.ObjectState == GameObject.eObjectState.Active)
                    AutonomousLivingServer.Tell(leader, player, tell);
                AutonomousLivingServer.Later(player, 2_500 + Random.Shared.Next(2_000), null,
                    () => ClientService.Instance.Post(static state => ShowRaidBox(state.Raid, state.Player), (Raid: raid, Player: player)));
            });
        }

        private static void ShowRaidBox(PlayerRaid raid, GamePlayer player)
        {
            long now = GameLoop.GameLoopTime;
            BotLedGroups.InviteMemory memory = BotLedGroups.MemoryOf(player, now);
            string blocked = player?.Out == null ? "offline" : BotLedGroups.StateBlocks(BotLedGroups.StateOf(player));
            if (blocked == null && !IsRunning(raid.Id))
                blocked = "raid_ended";
            if (blocked != null)
            {
                memory.Pending = false;
                LogPlayerInvite("not_sent", raid, player, blocked);
                return;
            }
            player.Out.SendCustomDialog(RealmRaidPlayerInvite.BoxText(raid.Leader.Name, raid.Name), (answering, response) =>
            {
                BotLedGroups.MemoryOf(answering, GameLoop.GameLoopTime).Pending = false;
                if (response != 0x01)
                {
                    // A no is final for this raid (it invited once and remembers it).
                    LogPlayerInvite("declined", raid, answering, "box");
                    return;
                }
                string why = BotLedGroups.StateBlocks(BotLedGroups.StateOf(answering) with { DialogOpen = false });
                if (why == null && AutonomousBotGroupCoordinator.TrySeatRaidGuest(raid.Id, answering, out why))
                {
                    LogPlayerInvite("accepted", raid, answering, "box");
                    answering.Out.SendMessage($"You join the {raid.Name} raid. Travel to {raid.HubName}: your party waits there until the raid leaves.",
                        eChatType.CT_System, eChatLoc.CL_SystemWindow);
                    return;
                }
                LogPlayerInvite("not_joined", raid, answering, why);
                AutonomousLivingServer.Tell(raid.Leader, answering, "sorry, no spot left for you after all");
            });
        }

        private static void LogPlayerInvite(string outcome, PlayerRaid raid, GamePlayer player, string reason)
        {
            if (Log.IsInfoEnabled)
                Log.Info($"RAID_PLAYER_INVITE outcome={outcome} event={raid?.Id} player=\"{player?.Name}\" level={player?.Level} " +
                         $"leader=\"{raid?.Leader?.Name}\" reason={reason}");
        }
    }
}
