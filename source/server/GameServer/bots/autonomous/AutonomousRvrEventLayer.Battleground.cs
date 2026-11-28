using System;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// #530 step 5a (piece C, structure.md finding 8): the battleground keep is an objective of the event layer, with its
/// realm's wave as that objective's muster. The controller used to decide on its own whether a bot assaulted, roamed or
/// held the walls and wrote a fake <c>AssaultKeep</c> intent with no objective behind it; now the layer decides one
/// <see cref="BattlegroundOrder"/> per turn (<see cref="DecideBattleground"/>), and the force's record names the keep
/// (<c>bg-keep-&lt;id&gt;</c>) and its part there: mustering, attacking, holding the walls or roaming.
/// The gather hold that three places applied by hand (the threat scan, the combat drop and the keep's guard pick) is one
/// rule here, <see cref="MusterAllowsFight"/>. The rules are the controller's, moved; the intent a bot acts on is unchanged.
/// Autonomous gamebots only: companions and the player never take a battleground order.
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    /// <summary>A realm's part at its battleground's central keep.</summary>
    public enum BattlegroundStep { Roam, Muster, Assault, Hold }

    /// <summary>One turn's battleground order: the step, the keep objective (null when there is no keep) and the rule.</summary>
    public readonly record struct BattlegroundOrder(BattlegroundStep Step, string ObjectiveId, string Rule)
    {
        /// <summary>The intent the bot acts on: the keep's attack while mustering or going in, else roaming (as before).</summary>
        public Intent Intent => Attacks ? Intent.AssaultKeep : Intent.Roam;

        public bool Attacks => Step is BattlegroundStep.Muster or BattlegroundStep.Assault;

        public ForceRole Role => Step switch
        {
            BattlegroundStep.Muster => ForceRole.Muster,
            BattlegroundStep.Assault => ForceRole.Attacker,
            BattlegroundStep.Hold => ForceRole.Defender,
            _ => ForceRole.Roamer,
        };

        /// <summary>The order as a proposal to the force's record: never an event plan (not shared).</summary>
        public IntentProposal Proposal => new(IntentSource.Battleground, Intent, ObjectiveId, false, null, Rule, Role: Role);
    }

    public const string BattlegroundObjectivePrefix = "bg-keep-";

    /// <summary>The objective id of a battleground's central keep. Pure.</summary>
    public static string BattlegroundObjectiveId(int keepId) => BattlegroundObjectivePrefix + keepId;

    public static bool IsBattlegroundObjective(string objectiveId) =>
        objectiveId != null && objectiveId.StartsWith(BattlegroundObjectivePrefix, StringComparison.Ordinal);

    /// <summary>One part in this many of a holding realm keeps to the keep's walls; the rest roam the field.</summary>
    public const int BattlegroundWallShare = 3;

    /// <summary>
    /// Whether this part of the holding realm (a bot, or a battleground trip's group by its leader) keeps to the walls.
    /// The controller's old <c>partId % 3 == 0</c>, unchanged. Pure.
    /// </summary>
    public static bool HoldsBattlegroundWalls(long partId) => partId % BattlegroundWallShare == 0;

    /// <summary>
    /// The battleground order (was inline in <c>ExecuteBattleground</c>). With no central keep the realm roams; a realm
    /// that holds the keep keeps a third of its parts on the walls and roams with the rest; a realm with too few inside
    /// for a wave (<see cref="AutonomousBattlegroundWave.TooFewForAssault"/>) fights in the field; otherwise it attacks,
    /// mustering at the wave's staging point until the wave goes (<paramref name="mustering"/>). Pure.
    /// </summary>
    public static BattlegroundOrder DecideBattleground(int? keepId, bool heldByRealm, bool tooFew, bool mustering, long partId)
    {
        if (keepId is not int id)
            return new(BattlegroundStep.Roam, null, "bg.roam.nokeep");
        string objective = BattlegroundObjectiveId(id);
        if (heldByRealm)
            return HoldsBattlegroundWalls(partId)
                ? new(BattlegroundStep.Hold, objective, "bg.hold")
                : new(BattlegroundStep.Roam, objective, "bg.roam.held");
        if (tooFew)
            return new(BattlegroundStep.Roam, objective, "bg.roam.toofew");
        return mustering
            ? new(BattlegroundStep.Muster, objective, "bg.muster")
            : new(BattlegroundStep.Assault, objective, "bg.wave");
    }

    /// <summary>What holds a battleground attacker back from a fight: its muster, its keep rally, or its storm.</summary>
    public enum MusterHold { None, Gather, Rally, Storm }

    /// <summary>
    /// The one gather-hold rule (was written in the threat scan, the combat drop and the keep's guard pick). A bot that
    /// is gathering for its wave or holding at its keep rally fights only what comes at it
    /// (<see cref="AutonomousBattlegroundWave.GathererMayEngage"/>) or what its rally posted (#617). It never picks the
    /// keep's lord as a new target (<paramref name="picking"/>: the keep's guard pick), since the rally goes up together,
    /// but it answers a lord that comes at it like anything else (self-defence, as the threat scan and the combat drop
    /// always allowed). A stormer fights by the storm's rule (<see cref="AutonomousBattlegroundKeepRally.StormerMayEngage"/>).
    /// Anything else is not held. Pure.
    /// </summary>
    public static bool MusterAllowsFight(MusterHold hold, bool isLord, bool comesAtMuster, bool postedContact, bool stormerMayEngage,
        bool picking) =>
        hold switch
        {
            MusterHold.None => true,
            MusterHold.Storm => stormerMayEngage,
            _ => (comesAtMuster || postedContact) && !(isLord && picking),
        };

    // ---- 5b: groups in the muster ---------------------------------------------------------------------------------

    /// <summary>A party at a battleground muster holds at most a full group.</summary>
    public const int MusterPartySize = 8;
    /// <summary>A new muster party forms from this many free bots (the siege parties' smaller size, owner 2026-10-07).</summary>
    public const int MusterPartyMinimum = 3;
    /// <summary>A muster record this old no longer counts (the force's writer stopped proposing it).</summary>
    public const long MusterRosterFreshMilliseconds = 30_000;
    private const string SoloBattlegroundForcePrefix = "bg:rvr-";

    /// <summary>The party plan: how many free bots join each existing party, and the sizes of the new parties.</summary>
    public sealed record MusterPartyPlan(int[] Joins, int[] NewParties)
    {
        public int Placed => Joins.Sum() + NewParties.Sum();
    }

    /// <summary>
    /// #530 step 5b: the realm's free bots at a battleground muster first fill its muster parties there (each to
    /// <see cref="MusterPartySize"/>, in the order given); the rest form new parties when at least
    /// <see cref="MusterPartyMinimum"/> remain, split as evenly as the fewest parties of at most eight allow. One or two
    /// left over stay solo until more arrive. Pure.
    /// </summary>
    public static MusterPartyPlan PlanMusterParties(IReadOnlyList<int> partySizes, int free)
    {
        free = Math.Max(0, free);
        int[] joins = new int[partySizes?.Count ?? 0];
        for (int i = 0; i < joins.Length && free > 0; i++)
        {
            int seats = Math.Max(0, MusterPartySize - partySizes[i]);
            joins[i] = Math.Min(seats, free);
            free -= joins[i];
        }
        if (free < MusterPartyMinimum)
            return new(joins, Array.Empty<int>());
        int parties = (free + MusterPartySize - 1) / MusterPartySize;
        int[] sizes = Enumerable.Range(0, parties).Select(i => free / parties + (i < free % parties ? 1 : 0)).ToArray();
        return new(joins, sizes);
    }

    /// <summary>The solo bots mustering for one battleground keep (their realms are the coordinator's to sort).</summary>
    public sealed record MusterRoster(string ObjectiveId, long[] MemberIds);

    /// <summary>
    /// The solo battleground forces (<c>bg:rvr-&lt;id&gt;</c>) whose record says they muster at a battleground keep, written
    /// in the last <see cref="MusterRosterFreshMilliseconds"/>, by keep. Read from the event layer's force records (5a).
    /// </summary>
    public static List<MusterRoster> BattlegroundMusterRosters(long nowTick)
    {
        var byKeep = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        foreach (var entry in ForceIntents)
        {
            if (!entry.Key.StartsWith(SoloBattlegroundForcePrefix, StringComparison.Ordinal) ||
                !long.TryParse(entry.Key.AsSpan(SoloBattlegroundForcePrefix.Length), out long id) || id <= 0)
                continue;
            ForceIntent intent;
            long written;
            lock (entry.Value)
            {
                intent = entry.Value.Intent;
                written = entry.Value.LastWriteTick;
            }
            if (intent == null || intent.Role != ForceRole.Muster || !IsBattlegroundObjective(intent.ObjectiveId) ||
                nowTick - written > MusterRosterFreshMilliseconds)
                continue;
            if (!byKeep.TryGetValue(intent.ObjectiveId, out List<long> ids))
                byKeep[intent.ObjectiveId] = ids = new List<long>();
            ids.Add(id);
        }
        return byKeep.OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => new MusterRoster(pair.Key, pair.Value.OrderBy(id => id).ToArray())).ToList();
    }
}
