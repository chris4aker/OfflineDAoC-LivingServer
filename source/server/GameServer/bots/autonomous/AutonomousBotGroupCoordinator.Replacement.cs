using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Database;

namespace DOL.GS;

/// <summary>
/// Bot-led groups, step B4 (docs/BOT-LED-GROUPS.md, "B4 as built"): the coordinator's side of the replacement search
/// (AutonomousPartyReplacement) and the natural break (BotLedGroups.BreakDue). Both run on the party's own pass, under
/// the coordinator's lock.
/// </summary>
public static partial class AutonomousBotGroupCoordinator
{
    /// <summary>How many candidates a search estimates travel for on one try (each estimate is a route query).</summary>
    public const int ReplacementCandidatesPerTry = 16;

    /// <summary>
    /// A member (gamebot or the player) left: a party that hosts or hosted the player starts looking for a replacement of
    /// the role that left. True when a search runs (it then holds a party that lost its core instead of ending it).
    /// </summary>
    private static bool TryStartReplacement(Session session, BotPveGroupRole role, GameBot[] stayed, bool viable, string who)
    {
        if (session == null || stayed.Length < 2 ||
            !AutonomousPartyReplacement.Wanted(session.HadHostedPlayer, AutonomousRealmRaid.GetView(session.Group) != null ||
                session.RaidMusterEvent != null, session.Ending, session.Phase, session.Group.MemberCount, session.Group.MaximumMemberCount))
            return false;
        long now = GameLoop.GameLoopTime;
        // A look for one more member (#423) gives way to the replacement for the member who left.
        if (session.Replacement?.OpenSeat == true)
            session.Replacement = null;
        // One search at a time: a second leaver while one runs keeps the first role (the party only takes one at a time).
        session.Replacement ??=new AutonomousPartyReplacement.Search(role, now, !viable);
        session.Replacement.HoldsParty |= !viable;
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_LED_RECRUIT outcome=searching group={session.Id} role={session.Replacement.Role} left=\"{who}\" holds={session.Replacement.HoldsParty}");
        return true;
    }

    /// <summary>One pass of the search. True when the party ended (it gave up without a core).</summary>
    private static bool StepReplacement(Session session, GameBot[] members, GameBot leader)
    {
        if (session == null || session.Ending)
            return false;
        long now = GameLoop.GameLoopTime;
        if (session.Replacement == null)
            TryStartOpenSeat(session, members, now);
        AutonomousPartyReplacement.Search search = session.Replacement;
        if (search == null)
            return false;
        bool viable = HasViablePveComposition(session, members);
        switch (AutonomousPartyReplacement.Next(search, now, viable))
        {
            case AutonomousPartyReplacement.Step.Wait:
                return false;
            case AutonomousPartyReplacement.Step.GiveUpCarryOn:
                session.Replacement = null;
                session.NextOpenSeatAt = now + AutonomousPartyReplacement.OpenSeatEveryMs;
                // Nobody came for a free seat: the party just goes on as it was (nothing to tell the player).
                if (!search.OpenSeat)
                    AnnounceToHosted(session, leader, BotLedGroups.Lead.RecruitGaveUp);
                LogRecruit("gave_up", session, search, null, "carry_on");
                return false;
            case AutonomousPartyReplacement.Step.GiveUpEnd:
                session.Replacement = null;
                session.NextOpenSeatAt = now + AutonomousPartyReplacement.OpenSeatEveryMs;
                LogRecruit("gave_up", session, search, null, "end");
                session.FarewellLine = BotLedGroups.Lead.RecruitEnd;
                FinishGroupTask(session, $"No replacement {search.Role} found in {AutonomousPartyReplacement.GiveUpAfterMs / 60_000} minutes");
                return true;
        }
        search.NextTry = now + AutonomousPartyReplacement.SearchEveryMs;
        search.Tries++;
        if (!search.Asked)
            search.Asked = AskForReplacement(session, leader, search.Role);
        GameBot recruit = FindRecruit(session, members, leader, search.Role);
        if (recruit != null)
            TakeRecruit(session, leader, recruit, search);
        return false;
    }

    /// <summary>
    /// 1.2.1 (#423): a party on its way or at its camp with a seat free starts a look for one more member
    /// (<see cref="AutonomousPartyReplacement.OpenSeatWanted"/>): the first <see cref="AutonomousPartyReplacement.OpenSeatFirstMs"/>
    /// after it sets off, then <see cref="AutonomousPartyReplacement.OpenSeatEveryMs"/> after each search. The role is the
    /// one it lacks (a healer, a tank, else damage). The search is the replacement's own (the /lfg ask once, a free waiting
    /// gamebot of the role and level through the lease, at most one recruit), but it never holds or ends the party. While it
    /// runs the leader's /lfg post says there is a seat (<see cref="RecruitingPost"/>), so a player asking gets the invite
    /// box the usual way (BotLedGroups.OnPlayerAsked).
    /// </summary>
    private static void TryStartOpenSeat(Session session, GameBot[] members, long now)
    {
        bool raidParty = AutonomousRealmRaid.GetView(session.Group) != null || session.RaidMusterEvent != null;
        if (!AutonomousPartyReplacement.OpenSeatWanted(session.ObjectiveKind == eAutonomousObjectiveKind.GroupPve, raidParty,
                session.Ending, session.Recovery?.IsRegrouping == true, session.Phase, session.Group.MemberCount,
                session.Group.MaximumMemberCount, session.Replacement != null) || members.Length < 2)
            return;
        if (session.NextOpenSeatAt <= 0)
        {
            // The party has just set off: the first look comes a little later.
            session.NextOpenSeatAt = now + AutonomousPartyReplacement.OpenSeatFirstMs;
            return;
        }
        if (!AutonomousPartyReplacement.OpenSeatDue(now, session.NextOpenSeatAt))
            return;
        bool player = HostedPlayerPresent(session);
        int tanks = members.Count(member => session.PveRoles.TryGetValue(MemberKey(member), out BotPveGroupRole role) && role == BotPveGroupRole.Tank) +
                    (player && session.PlayerRole == BotPveGroupRole.Tank ? 1 : 0);
        int healers = members.Count(member => session.PveRoles.TryGetValue(MemberKey(member), out BotPveGroupRole role) && role == BotPveGroupRole.Healer) +
                      (player && session.PlayerRole == BotPveGroupRole.Healer ? 1 : 0);
        session.Replacement = new AutonomousPartyReplacement.Search(AutonomousPartyReplacement.OpenSeatRole(tanks, healers), now, false)
        {
            OpenSeat = true,
        };
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_LED_RECRUIT outcome=searching kind=open_seat group={session.Id} role={session.Replacement.Role} " +
                     $"phase=\"{session.Phase}\" members={session.Group.MemberCount}/{session.Group.MaximumMemberCount} hosting={player}");
    }

    private static GameBot FindRecruit(Session session, GameBot[] members, GameBot leader, BotPveGroupRole role)
    {
        int partyLevel = members.Length == 0 ? leader.Level : (int)Math.Round(members.Average(member => (int)member.Level));
        bool hosting = HostedPlayerPresent(session);
        eDutyLeaseKind kind = hosting ? eDutyLeaseKind.BotLedGroupWithPlayer : eDutyLeaseKind.BotGroupSession;
        // Cheap checks first; the route estimate only for a few of those left, nearest in a straight line first.
        GameBot[] pool = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.Realm == leader.Realm && bot.Group == null && bot.CharacterClass != null && !bot.IsTemporaryGroupHelper &&
                          BotPartyRoles.CanFill((eCharacterClass)bot.CharacterClass.ID, role) &&
                          AutonomousPartyReplacement.LevelFits(partyLevel, bot.Level))
            .OrderBy(bot => bot.CurrentRegionID == leader.CurrentRegionID ? bot.GetDistanceTo(leader) : int.MaxValue)
            .Take(ReplacementCandidatesPerTry * 2).ToArray();
        var candidates = new List<(GameBot Bot, AutonomousPartyReplacement.Candidate View)>();
        var crossings = new Dictionary<(eRealm Realm, ushort Region), DbZonePoint>();
        foreach (GameBot bot in pool)
        {
            bool free = bot.IsAlive && bot.CurrentRegion != null && !bot.InCombat && !bot.IsAttacking &&
                        (bot.Brain as BotBrain)?.HasAggro != true && !bot.IsOnStableMasterRoute && !bot.IsReturningAfterRelease &&
                        !AutonomousBeggars.Is(bot);
            bool lease = BotDutyLeases.WouldGrant(bot, kind, session.Id) && !AutonomousRealmRaid.IsReserved(bot);
            bool waiting = AutonomousObjectiveAssignments.Is(bot, eAutonomousObjectiveKind.GroupPve);
            if (!free || !lease || !waiting)
                continue;
            double minutes = AutonomousWorldBotController.EstimateTravelMinutes(bot, leader.CurrentRegionID, leader.X, leader.Y, crossings);
            candidates.Add((bot, new AutonomousPartyReplacement.Candidate(free, lease, waiting, true, bot.Level, true, minutes)));
            if (candidates.Count >= ReplacementCandidatesPerTry)
                break;
        }
        int pick = AutonomousPartyReplacement.Pick(candidates.Select(candidate => candidate.View).ToArray(), partyLevel);
        return pick < 0 ? null : candidates[pick].Bot;
    }

    private static void TakeRecruit(Session session, GameBot leader, GameBot recruit, AutonomousPartyReplacement.Search search)
    {
        if (!session.Group.AddMember(recruit))
            return;
        GamePlayer player = HostedPlayerPresent(session) ? session.HostedPlayer : null;
        BotDutyLeases.ClaimResult claim = player != null
            ? BotDutyLeases.Claim(recruit, eDutyLeaseKind.BotLedGroupWithPlayer, session.Id, DutyLeaseReason.HostingPlayer, player.Name, session.Group)
            : BotDutyLeases.Claim(recruit, eDutyLeaseKind.BotGroupSession, session.Id, DutyLeaseReason.GroupSession,
                $"{session.ObjectiveKind} party", session.Group);
        if (!claim.Granted)
        {
            // Something took it between the check and the claim: let it go and try again next time.
            session.ProcessingAttendanceRemovals = true;
            try
            {
                session.Group.RemoveMember(recruit, retainSingleRemainingMember: true);
            }
            finally
            {
                session.ProcessingAttendanceRemovals = false;
            }
            return;
        }
        GameBot[] members = BotMembers(session.Group);
        session.PveRoles[MemberKey(recruit)] = search.Role;
        session.LockedSize = members.Length;
        session.Puller ??= ChooseLockedPvePuller(session, members);
        TryBuildRendezvousSlots(session, members);
        session.Recovery.Observe(RecoveryMembers(session, members, false), false);
        session.Replacement = null;
        session.NextOpenSeatAt = GameLoop.GameLoopTime + AutonomousPartyReplacement.OpenSeatEveryMs;
        AnnounceToHosted(session, leader, BotLedGroups.Lead.Recruited);
        WriteSessionMetadata(session, members);
        LogRecruit("recruited", session, search, recruit, "found");
    }

    /// <summary>
    /// The leader's ask in its realm's /lfg ("lf 1 more, need a healer"), once per search, only while the realm's /lfg runs
    /// under the chat budget's ceiling. True when it was said.
    /// </summary>
    private static bool AskForReplacement(Session session, GameBot leader, BotPveGroupRole role)
    {
        long now = GameLoop.GameLoopTime;
        int realmBots = AutonomousBotChatCoordinator.RealmBots(leader.Realm);
        var band = AutonomousChatBudget.LiveBand(AutonomousChatBudget.Channel.Lfg, realmBots);
        if (!AutonomousPartyReplacement.LfgFits(AutonomousChatBudget.Meter.PerMinute(AutonomousChatBudget.Channel.Lfg, (long)leader.Realm, now),
                band.Ceiling))
            return false;
        string camp = session.Camp?.MonsterName ?? session.Camp?.ZoneName ?? PlaceOf(leader);
        var slots = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["need"] = AutonomousPartyReplacement.NeedWord(role),
            ["camp"] = camp,
        };
        string text =
            AutonomousBotVoices.TryLine(leader, "lfg", out AutonomousBotVoiceCorpus.Picked lead, topic: "lead_lf_more", slots: slots,
                flags: ["lead_call", "in_group"]) ? lead.Text
            : AutonomousBotVoices.TryLine(leader, "lfg", out AutonomousBotVoiceCorpus.Picked lfm, topic: "lfm", slots: slots,
                flags: ["recruiting", "in_group"]) ? lfm.Text
            : BotLedGroups.Fill("lf 1 more, need {need} for {camp}", slots);
        GameBot speaker = leader;
        AutonomousLivingServer.Later(leader, 1_000 + Random.Shared.Next(2_000), null, () => AutonomousBotChatCoordinator.SendLfgLine(speaker, text));
        return true;
    }

    private static void AnnounceToHosted(Session session, GameBot leader, BotLedGroups.Lead what)
    {
        if (HostedPlayerPresent(session))
            BotLedGroups.Announce(leader, what, session.HostedPlayer, PlaceOf(leader), session.Camp?.MonsterName);
    }

    private static void LogRecruit(string outcome, Session session, AutonomousPartyReplacement.Search search, GameBot recruit, string reason)
    {
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_LED_RECRUIT outcome={outcome} kind={(search.OpenSeat ? "open_seat" : "replacement")} group={session.Id} role={search.Role} recruit=\"{recruit?.Name}\" " +
                     $"level={recruit?.Level ?? 0} tries={search.Tries} seconds={(GameLoop.GameLoopTime - search.Since) / 1000} reason={reason}");
    }

    /// <summary>
    /// One pass of the natural break of a party that hosts the player: the call ("last pull then town"), then the end of
    /// the session once the last pull is over. True when the party ended.
    /// </summary>
    private static bool StepNaturalBreak(Session session, GameBot[] members, GameBot leader)
    {
        if (!HostedPlayerPresent(session) || session.Ending || session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve ||
            BotLedGroups.ViewOf(session.Group)?.RaidGuest == true)
            return false;
        long now = GameLoop.GameLoopTime;
        bool fighting = members.Any(member => member.InCombat) || session.HostedPlayer.InCombat;
        if (session.Break == null)
        {
            if (now < session.NextBreakCheck || IsAssemblyPhase(session.Phase))
                return false;
            session.NextBreakCheck = now + BotLedGroups.BreakCheckMs;
            string reason = BotLedGroups.BreakDue(members.Length, members.Count(AutonomousBotEconomy.IsBackpackNearlyFull),
                session.TaskClock.HasStarted, session.TaskClock.RemainingMilliseconds(now));
            if (reason == null)
                return false;
            session.Break = new BotLedGroups.BreakState(reason, now, fighting);
            BotLedGroups.Announce(leader, BotLedGroups.Lead.LastPull, session.HostedPlayer, PlaceOf(leader), session.Camp?.MonsterName);
            if (Log.IsInfoEnabled)
                Log.Info($"BOT_LED_BREAK outcome=called group={session.Id} reason={reason} fighting={fighting}");
            return false;
        }
        if (!session.Break.EndsNow(fighting, now))
            return false;
        if (Log.IsInfoEnabled)
            Log.Info($"BOT_LED_BREAK outcome=ended group={session.Id} reason={session.Break.Reason} lastPull={session.Break.SawFight}");
        session.FarewellLine = BotLedGroups.Lead.TownBreak;
        FinishGroupTask(session, $"Natural break ({session.Break.Reason}): last pull then town");
        return true;
    }
}
