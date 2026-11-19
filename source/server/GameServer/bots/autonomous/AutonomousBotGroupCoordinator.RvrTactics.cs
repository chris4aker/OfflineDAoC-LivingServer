using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// Open-field RvR tactics (docs/RVR-FIELD-1.2.md): the group coordinator's hook points. Work package 6 (roamers): the
// population split (rvr_roam_population_split, owner decision 2: 55% warbands, 30% eights, 15% soloers, duos and
// stealthers, of the RvR gamebots not in a siege) and the role-built eights (rvr_roaming_parties). Siege parties form
// first (FormSiegeParties runs before TryFormGroups) and are not counted. Every hook here runs under the coordinator's
// lock, in the serial formation pass.
public static partial class AutonomousBotGroupCoordinator
{
    /// <summary>What a formed RvR party is for (registered at formation; a group not listed is a warband).</summary>
    private sealed record RvrParty(RvrPartyKind Kind, string Template, string SessionId, long FormedAt, RvrSkillTier Tier);

    private static readonly ConditionalWeakTable<Group, RvrParty> RvrParties = new();
    // The kind chosen for a leader in this pass, until its party forms (or not). Serial, under the lock.
    private static readonly Dictionary<GameBot, RvrPartyKind> RvrPendingKind = new();
    private static readonly Dictionary<GameBot, RvrRoster> RvrPendingRoster = new();
    // fix/rvr-quiet-frontier: since when each realm's eights have come up short in a row (RvrRoamSplit.ShortEight), and
    // the leaders whose short eight formed as a warband after the wait (logged as fallback=shortEight). Serial, under the lock.
    private static readonly Dictionary<eRealm, long> RvrShortEightSince = new();
    private static readonly HashSet<GameBot> RvrShortEightFallback = new();
    private static long _rvrCensusTick = long.MinValue;
    private static readonly Dictionary<eRealm, RvrRoamCensus> RvrCensus = new();

    /// <summary>The split is on: the master switch and rvr_roaming_parties.</summary>
    public static bool RvrRoamSplitActive => Properties.RVR_BOT_FIELD_TACTICS && Properties.RVR_ROAMING_PARTIES;

    public static RvrRoamShares RvrRoamShares => RvrRoamShares.Parse(Properties.RVR_ROAM_POPULATION_SPLIT);

    /// <summary>
    /// The share of a realm's RvR gamebots kept free of groups (MinimumSoloRvrReserve): the split's small share when the
    /// split is on, else the 35% it always was.
    /// </summary>
    public static double RvrTacticsSoloReserveShare() => RvrRoamSplitActive ? RvrRoamShares.Share(RvrPartyKind.Small) : 0.35;

    /// <summary>
    /// What a gamebot's RvR party is for: <see cref="RvrPartyKind.Small"/> for a bot alone or in a duo formed as one,
    /// <see cref="RvrPartyKind.Eight"/> for a role-built eight, else a warband. Thread-safe (any brain may ask).
    /// </summary>
    public static RvrPartyKind RvrPartyKindOf(GameBot bot)
    {
        if (bot?.Group == null)
            return RvrPartyKind.Small;
        return RvrParties.TryGetValue(bot.Group, out RvrParty party) ? party.Kind : RvrPartyKind.Warband;
    }

    private static bool CampingStealther(GameBot bot) => bot?.CharacterClass != null &&
        RvrStealthOpener.IsCampingStealther((eCharacterClass)bot.CharacterClass.ID) &&
        (bot.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0;

    /// <summary>The realm's census of RvR gamebots not in a siege (once per pass, kept up to date as parties form).</summary>
    private static RvrRoamCensus RvrCensusOf(GameBot[] roster, eRealm realm)
    {
        long now = GameLoop.GameLoopTime;
        if (_rvrCensusTick != now)
        {
            _rvrCensusTick = now;
            RvrCensus.Clear();
            var counts = new Dictionary<eRealm, (int Warband, int Eight, int Small)>();
            foreach (GameBot bot in roster ?? [])
            {
                if (bot == null || !bot.IsAlive)
                    continue;
                RvrPartyKind kind;
                if (bot.Group != null)
                {
                    if (AutonomousRealmRaid.GetView(bot.Group) != null ||
                        Sessions.TryGetValue(bot.Group, out Session session) && AutonomousRvrEventLayer.IsForceCommitted(session.Id, now))
                        continue; // a siege party or a raid
                    kind = RvrParties.TryGetValue(bot.Group, out RvrParty party) ? party.Kind : RvrPartyKind.Warband;
                }
                else
                {
                    if (AutonomousRvrEventLayer.IsForceCommitted($"rvr-{bot.DatabaseID}", now))
                        continue;
                    kind = RvrPartyKind.Small;
                }
                (int w, int e, int s) = counts.GetValueOrDefault(bot.Realm);
                counts[bot.Realm] = kind switch
                {
                    RvrPartyKind.Warband => (w + 1, e, s),
                    RvrPartyKind.Eight => (w, e + 1, s),
                    _ => (w, e, s + 1),
                };
            }
            foreach ((eRealm key, (int w, int e, int s)) in counts)
                RvrCensus[key] = new RvrRoamCensus(w, e, s);
        }
        return RvrCensus.GetValueOrDefault(realm);
    }

    /// <summary>
    /// Hook: TryFormGroups, RvR branch, right after today's warband size roll. With the split on, chooses what this
    /// leader's party is for (the kind furthest below its share) and returns its size: a warband keeps today's roll (at
    /// least a pair), an eight takes as many as the realm's reserve allows (up to 8), a small party is a duo or the
    /// leader alone (1: no party). Returns <paramref name="rolledSize"/> unchanged with the split off.
    /// </summary>
    public static int RvrTacticsPartySize(GameBot leader, int rolledSize, int largestAllowed, GameBot[] roster)
    {
        if (leader == null)
            return rolledSize;
        RvrPendingKind.Remove(leader);
        RvrPendingRoster.Remove(leader);
        RvrShortEightFallback.Remove(leader);
        if (RvrPendingKind.Count > 64)
        {
            RvrPendingKind.Clear();
            RvrPendingRoster.Clear();
            RvrShortEightFallback.Clear();
        }
        if (!RvrRoamSplitActive || !RvrTacticsScope.Allows(leader))
            return rolledSize;
        using var profile = AiProfile.Scope(AiSystem.RvrTactics);
        RvrCheckSpots.EnsureValidation();
        RvrPartyKind kind = RvrRoamSplit.Choose(RvrCensusOf(roster, leader.Realm), RvrRoamShares, CampingStealther(leader),
            Random.Shared.NextDouble());
        int size = RvrRoamSplit.Size(kind, rolledSize, largestAllowed, leader.Level, Random.Shared.NextDouble());
        if (size >= 2)
            RvrPendingKind[leader] = kind;
        return size;
    }

    private static RvrRosterCandidate RosterCandidate(GameBot bot, int index) =>
        new(index, bot?.CharacterClass != null ? (eCharacterClass)bot.CharacterClass.ID : eCharacterClass.Unknown,
            bot?.Level ?? 0, bot?.CureMezz != null, bot?.BotSpec?.EraRole);

    /// <summary>
    /// Hook: the RvR branch of group formation (TryFormGroups), after the pet cap picked <paramref name="picked"/> from
    /// <paramref name="pool"/> for a group of <paramref name="size"/> with <paramref name="leader"/>. Returns the
    /// members the tactics want instead, or null for no opinion. Work package 6: an eight is built to the realm's 2003
    /// skeleton (RvrRosterTemplates; the pet cap holds); a duo takes a fitting second; a warband keeps today's pick. An
    /// eight that finds fewer than RvrRosterTemplates.MinimumEight forms as a warband. It runs under the coordinator's
    /// lock: read a fight board only with RvrFightBoards.Peek, never Get. A group formed here is bot-only; groups a
    /// player leads or joins never come through this branch.
    /// </summary>
    public static GameBot[] RvrTacticsRoster(GameBot leader, GameBot[] pool, GameBot[] picked, int size)
    {
        if (!Properties.RVR_ROAMING_PARTIES || !Properties.RVR_BOT_FIELD_TACTICS || !RvrTacticsScope.Allows(leader))
            return null;
        if (pool == null || !RvrPendingKind.TryGetValue(leader, out RvrPartyKind kind) || kind == RvrPartyKind.Warband)
            return null;
        using var profile = AiProfile.Scope(AiSystem.RvrTactics);
        RvrRosterCandidate[] candidates = pool.Select((bot, index) => RosterCandidate(bot, index)).ToArray();
        int cap = AutonomousRvrPetCap.CapFor(size, Properties.RVR_GROUP_PET_CAP);
        if (kind == RvrPartyKind.Small)
        {
            int partner = RvrRosterTemplates.DuoPartner(RosterCandidate(leader, -1), candidates, cap);
            return partner < 0 ? [] : [pool[partner]];
        }
        RvrRoster roster = RvrRosterTemplates.Build(leader.Realm, RosterCandidate(leader, -1), candidates, size - 1, cap,
            Properties.RVR_ROSTER_BUILD_ROLES);
        long now = GameLoop.GameLoopTime;
        long shortSince = RvrShortEightSince.TryGetValue(leader.Realm, out long since) ? since : now;
        RvrPartyKind? outcome = RvrRoamSplit.ShortEight(roster.Picked.Length + 1, RvrCensus.GetValueOrDefault(leader.Realm),
            RvrRoamShares, now - shortSince);
        if (outcome == null)
        {
            // Short: no party this pass. The leader stays free and the realm's pool fills as warbands end.
            RvrShortEightSince.TryAdd(leader.Realm, now);
            RvrPendingKind.Remove(leader);
            if (Log.IsInfoEnabled && RvrLogBudget.Take("RVR_PARTY_WAIT", now, 6))
                Log.Info($"RVR_PARTY_WAIT realm={GlobalConstants.RealmToName(leader.Realm)} leader=\"{leader.Name}\" found={roster.Picked.Length + 1} " +
                         $"minimum={RvrRosterTemplates.MinimumEight} waitedSeconds={(now - shortSince) / 1000} " +
                         $"limitSeconds={RvrRoamSplit.ShortEightWaitMilliseconds / 1000} region={leader.CurrentRegionID}");
            return [];
        }
        RvrShortEightSince.Remove(leader.Realm);
        if (outcome == RvrPartyKind.Warband)
        {
            RvrPendingKind[leader] = RvrPartyKind.Warband;
            RvrShortEightFallback.Add(leader);
            return null;
        }
        RvrPendingRoster[leader] = roster;
        return roster.Picked.Select(index => pool[index]).ToArray();
    }

    /// <summary>
    /// Hook: TryFormGroups, after a new RvR party's session exists. Registers what the party is for and writes
    /// RVR_PARTY_FORMED (the template, the roles filled, covered by a next best fit and missing, the skill tier).
    /// </summary>
    private static void RvrTacticsPartyFormed(Session session, GameBot leader, GameBot[] members)
    {
        if (session?.Group == null || leader == null || !RvrPendingKind.Remove(leader, out RvrPartyKind kind))
            return;
        RvrPendingRoster.Remove(leader, out RvrRoster roster);
        bool fellBack = RvrShortEightFallback.Remove(leader);
        long now = GameLoop.GameLoopTime;
        RvrSkillTier tier = RvrFightBoards.DefaultSkillTier(leader);
        string template = kind switch
        {
            RvrPartyKind.Eight => roster?.Template ?? RvrRosterTemplates.TemplateName(leader.Realm),
            RvrPartyKind.Small => "duo",
            _ => "warband",
        };
        RvrParties.AddOrUpdate(session.Group, new RvrParty(kind, template, session.Id, now, tier));
        if (RvrCensus.TryGetValue(leader.Realm, out RvrRoamCensus census))
            RvrCensus[leader.Realm] = kind == RvrPartyKind.Small
                ? census // a duo stays in the small share
                : census.Formed(kind, members.Length);
        if (!RvrLogBudget.Take("RVR_PARTY_FORMED", now, 60))
            return;
        RvrRoamCensus after = RvrCensus.GetValueOrDefault(leader.Realm);
        string classes = string.Join(",", members.Select(member => member.CharacterClass != null ? ((eCharacterClass)member.CharacterClass.ID).ToString() : "?"));
        Log.Info($"RVR_PARTY_FORMED group={session.Id} realm={GlobalConstants.RealmToName(leader.Realm)} kind={kind} template={template} " +
                 $"size={members.Length} tier={tier} " +
                 (roster != null
                     ? $"filled=\"{string.Join(",", roster.Filled.Select(slot => $"{slot.Role}:{slot.Class}"))}\" " +
                       $"nextBest=\"{string.Join(",", roster.NextBest)}\" missing=\"{string.Join(",", roster.Missing)}\" " +
                       $"cc={roster.CrowdControlLabel} "
                     : "") +
                 $"classes=\"{classes}\" split={RvrRoamShares} census={after.Warband}/{after.Eight}/{after.Small}" +
                 (fellBack ? " fallback=shortEight" : ""));
    }
}
