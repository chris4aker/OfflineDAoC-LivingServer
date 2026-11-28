using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace DOL.GS;

/// <summary>
/// One definition of how strong an RvR force is (#530, structure.md finding 6). The siege rule used to be written three
/// times (level 35 and 4 members in the controller, 40, 5 and a healer in the dead director, 50 and 4 in the event layer);
/// the event layer's rule was the one that held, and it is the only one now.
/// </summary>
public readonly record struct ForceStrength(int Members, int AverageLevel, int MinimumLevel, int Healers)
{
    public const int SiegeMinimumMembers = 4;
    public const int SiegeMinimumLevel = 50;

    /// <summary>Every member, and the average, at siege level: below it a force roams or hunts, never joins a siege.</summary>
    public bool AtSiegeLevel => AverageLevel >= SiegeMinimumLevel && MinimumLevel >= SiegeMinimumLevel;

    /// <summary>Big and high enough to open a keep siege (the force's own say comes on top: <c>Force.SiegeCapable</c>).</summary>
    public bool CanSiege => Members >= SiegeMinimumMembers && AverageLevel >= SiegeMinimumLevel;

    /// <summary>
    /// A class that heals a group, by <see cref="RvrRoles.For"/>: the healers (Cleric, Druid, Healer) and the support
    /// classes that heal (Friar, Shaman, Bard, Warden). Minstrel and Skald are support without heals. The old count used a
    /// class-name substring and missed Shaman, Friar and Warden. Pure.
    /// </summary>
    public static bool Heals(eCharacterClass characterClass) => RvrRoles.For(characterClass) switch
    {
        RvrRole.Healer => true,
        RvrRole.Support => characterClass is not (eCharacterClass.Minstrel or eCharacterClass.Skald),
        _ => false,
    };

    /// <summary>The strength of these members (class, level). Average rounded as the controller always did. Pure.</summary>
    public static ForceStrength Of(IReadOnlyCollection<(eCharacterClass Class, int Level)> members)
    {
        if (members == null || members.Count == 0)
            return new(0, 0, 0, 0);
        return new(members.Count, (int)Math.Round(members.Average(member => member.Level)), members.Min(member => member.Level),
            members.Count(member => Heals(member.Class)));
    }

    public static ForceStrength Of(IReadOnlyCollection<GameBot> members) =>
        Of(members?.Select(member => ((eCharacterClass)(member.CharacterClass?.ID ?? 0), (int)member.Level)).ToArray());
}

/// <summary>
/// Piece C, steps 2-4 (#530): one intent per force, owned by the event layer. Every writer that used to set a bot's
/// <c>_rvrIntent</c> on its own (the committed plan, the plan, the roam override, a chat promise, the battleground, the
/// release of an ended objective) now proposes here; <see cref="Decide"/> applies the one set of rules, the controller
/// takes what was decided, and the force's record is written once per turn by <see cref="WriteForceIntent"/>, with an
/// RVR_DECIDE row (<c>layer=event what=force</c>) naming the rule that won and the proposals it overruled.
/// Followers read the record (<see cref="IntentOf"/>) instead of their leader's TempProperty. <see cref="Shadow"/>
/// compares each bot's own fields with its force's record and logs a mismatch (<c>what=shadow</c>).
/// The rules are today's rules, moved here: what each layer decides is unchanged.
/// </summary>
public static partial class AutonomousRvrEventLayer
{
    /// <summary>
    /// A force's part at its objective. <see cref="ForceRole.Muster"/> (#530 step 5a): gathering at the objective's muster
    /// before the attack goes in (a battleground wave that has not gone yet).
    /// </summary>
    public enum ForceRole { Roamer, Hunter, Attacker, Defender, ThirdRealm, Escort, Muster }

    /// <summary>
    /// Who proposed an intent. <see cref="IntentSource.Battlegroup"/> (#599 step 1): a battlegroup's follower group takes its
    /// lead group's intent and objective while it trails the lead. <see cref="IntentSource.Zerg"/> (#599 step 3): the realm's
    /// zerg holds a gate (its lead) or a recruit travels to it, objective <c>zerg-&lt;realm&gt;-&lt;n&gt;</c>.
    /// </summary>
    public enum IntentSource { Plan, Committed, Released, RoamOverride, ChatPromise, Battleground, Battlegroup, Zerg }

    /// <summary>A force's intent: its objective, its role there, its slots, since when, and why.</summary>
    public sealed record ForceIntent(string ForceId, Intent Intent, string ObjectiveId, ForceRole Role, string Slot,
        long SinceTick, IntentSource Source, string Rule, string Why, bool Shared, int DefendingKeep)
    {
        /// <summary>
        /// "AssaultKeep@rvr-keep-5+event". A battleground order whose role is not the intent's own (a muster, a wall
        /// holder) adds it ("AssaultKeep@bg-keep-5/muster"), so the change to the attack logs as a change (#530 step 5a).
        /// </summary>
        public string Summary => $"{Intent}@{ObjectiveId ?? "-"}{(Shared ? "+event" : string.Empty)}" +
            (Source == IntentSource.Battleground && Role != RoleFor(Intent, Shared) ? "/" + Role.ToString().ToLowerInvariant() : string.Empty);

        /// <summary>The same decision (the role, slot and times aside).</summary>
        public bool SameAs(ForceIntent other) => other != null && other.Intent == Intent && other.Shared == Shared &&
            other.DefendingKeep == DefendingKeep && string.Equals(other.ObjectiveId, ObjectiveId, StringComparison.Ordinal);
    }

    /// <summary>
    /// A proposal. <paramref name="Shared"/> null keeps the current value (a chat promise does not touch it); so does
    /// <paramref name="DefendingKeep"/> null. <paramref name="OverrideObjective"/>: the roam override's destination,
    /// proposed with the plan it would replace (one proposal, one write). <paramref name="Role"/>: the part the proposer
    /// already knows (a battleground order's muster or wall hold); null takes the intent's own.
    /// </summary>
    public sealed record IntentProposal(IntentSource Source, Intent Intent, string ObjectiveId, bool? Shared, int? DefendingKeep,
        string Rule, string Why = null, string OverrideObjective = null, string OverrideRule = null, ForceRole? Role = null);

    /// <summary>What the bot holds now, the context a proposal is judged against.</summary>
    public readonly record struct IntentView(Intent Intent, bool Shared);

    // ---- the rules ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// The roam override's rule (was inline in the controller, RvrTactics.cs): sieges, defence calls, event plans and
    /// hunts of something other than an enemy force are left as chosen. Pure.
    /// </summary>
    public static bool AcceptsRoamOverride(Intent intent, bool shared, string chosenId) =>
        !shared && intent is not (Intent.DefendEvent or Intent.AssaultKeep or Intent.AssaultRelicKeep) &&
        !(intent == Intent.HuntEnemy && chosenId != null && !chosenId.StartsWith("rvr-enemy-", StringComparison.Ordinal));

    /// <summary>A chat promise ("omw bled") never replaces an event plan (was <c>_rvrSharedEvent ? null</c>). Pure.</summary>
    public static bool AcceptsChatPromise(IntentView current) => !current.Shared;

    /// <summary>
    /// The decision on one proposal, given what the bot holds (<paramref name="view"/>) and what was decided before
    /// (<paramref name="previous"/>, for the values a proposal keeps). Null: refused. Pure.
    /// </summary>
    public static ForceIntent Decide(string forceId, IntentView view, ForceIntent previous, IntentProposal proposal, long nowTick)
    {
        if (proposal == null)
            return null;
        if (proposal.Source == IntentSource.ChatPromise && !AcceptsChatPromise(view))
            return null;
        bool shared = proposal.Shared ?? view.Shared;
        IntentSource source = proposal.Source;
        string objective = proposal.ObjectiveId, rule = proposal.Rule;
        if (proposal.OverrideObjective != null && AcceptsRoamOverride(proposal.Intent, shared, proposal.ObjectiveId))
        {
            source = IntentSource.RoamOverride;
            objective = proposal.OverrideObjective;
            rule = proposal.OverrideRule ?? "roam.override";
        }
        int keep = proposal.DefendingKeep ?? previous?.DefendingKeep ?? -1;
        var decided = new ForceIntent(forceId, proposal.Intent, objective, proposal.Role ?? RoleFor(proposal.Intent, shared), "-",
            nowTick, source, rule, proposal.Why, shared, keep);
        return previous != null && previous.SameAs(decided) ? decided with { SinceTick = previous.SinceTick } : decided;
    }

    private static ForceRole RoleFor(Intent intent, bool shared) => intent switch
    {
        Intent.AssaultKeep or Intent.AssaultRelicKeep => ForceRole.Attacker,
        Intent.DefendEvent => ForceRole.Defender,
        Intent.HuntEnemy => ForceRole.Hunter,
        _ => ForceRole.Roamer,
    };

    // ---- one turn: proposals in, one write out ----------------------------------------------------------------------

    /// <summary>
    /// One bot's turn. Every proposal of the turn is decided at once (the controller acts on it, as before), and the
    /// result is written to the force's record once, at <see cref="End"/>. A proposal outside a turn is written at once.
    /// </summary>
    public sealed class ForceTurn
    {
        public string ForceId { get; private set; }
        /// <summary>The force's leader or a solo bot: the one whose turn writes the force's record.</summary>
        public bool Writer { get; private set; }
        public bool Open { get; private set; }
        public ForceIntent Decided { get; private set; }
        private readonly List<string> _overruled = new();
        public IReadOnlyList<string> Overruled => _overruled;

        public void Begin(string forceId, bool writer)
        {
            ForceId = forceId;
            Writer = writer;
            Open = true;
            Decided = null;
            _overruled.Clear();
        }

        /// <summary>The decision on <paramref name="proposal"/>, or null when refused. Pure but for this turn's state.</summary>
        public ForceIntent Propose(IntentView view, IntentProposal proposal, long nowTick)
        {
            ForceIntent previous = Decided ?? (ForceId == null ? null : IntentOf(ForceId));
            ForceIntent decided = Decide(ForceId, view, previous, proposal, nowTick);
            if (decided == null)
            {
                _overruled.Add(proposal.Rule + ":refused");
                return null;
            }
            if (Decided != null && !Decided.SameAs(decided))
                _overruled.Add(Decided.Rule);
            Decided = decided;
            return decided;
        }

        /// <summary>Ends the turn: the force's one write, when this bot writes and something was decided.</summary>
        public bool End(GameBot bot, long nowTick)
        {
            Open = false;
            if (!Writer || Decided == null || ForceId == null)
                return false;
            return WriteForceIntent(ForceId, bot, Decided, _overruled, nowTick);
        }
    }

    // ---- the record ---------------------------------------------------------------------------------------------------

    private sealed class IntentRecord
    {
        public ForceIntent Intent;
        public long LastWriteTick;
        public int Writes;
    }

    private static readonly ConcurrentDictionary<string, IntentRecord> ForceIntents = new(StringComparer.Ordinal);
    private static long _forceIntentWritesSincePrune;
    private const long ForceIntentForgetMilliseconds = 60 * 60_000L;

    /// <summary>The force's intent as the event layer last wrote it, or null.</summary>
    public static ForceIntent IntentOf(string forceId) =>
        forceId != null && ForceIntents.TryGetValue(forceId, out IntentRecord record) ? record.Intent : null;

    /// <summary>How many times the force's intent changed (tests: one write per turn).</summary>
    public static int ForceIntentWrites(string forceId) =>
        forceId != null && ForceIntents.TryGetValue(forceId, out IntentRecord record) ? Volatile.Read(ref record.Writes) : 0;

    /// <summary>
    /// The one writer of a force's intent. Change-only: the same decision again writes nothing. Fills in the role and
    /// slots the force holds at the event, and logs one RVR_DECIDE row. True when the record changed.
    /// </summary>
    public static bool WriteForceIntent(string forceId, GameBot bot, ForceIntent decided, IReadOnlyList<string> overruled, long nowTick)
    {
        if (string.IsNullOrEmpty(forceId) || decided == null)
            return false;
        decided = WithEventRole(forceId, decided);
        IntentRecord record = ForceIntents.GetOrAdd(forceId, _ => new IntentRecord());
        lock (record)
        {
            record.LastWriteTick = nowTick;
            if (record.Intent != null && record.Intent.SameAs(decided) && record.Intent.Role == decided.Role)
                return false;
            record.Intent = decided with { ForceId = forceId };
            record.Writes++;
        }
        string why = overruled is { Count: > 0 } ? $"{decided.Why} over={string.Join(",", overruled)}" : decided.Why;
        RvrDecide.Change(bot, "event", "force", decided.Summary, decided.Rule, why);
        if (Interlocked.Increment(ref _forceIntentWritesSincePrune) % 512 == 0)
            PruneForceIntents(nowTick);
        return true;
    }

    private static ForceIntent WithEventRole(string forceId, ForceIntent decided)
    {
        if (decided.ObjectiveId == null || !decided.Shared)
            return decided;
        using (AiProfile.Lock(Sync, AiSystem.LockRvrEvents))
        {
            if (Events.TryGetValue(decided.ObjectiveId, out ActiveEvent active))
            {
                ForceRole role = active.Attackers.ContainsKey(forceId) ? ForceRole.Attacker
                    : active.Defenders.ContainsKey(forceId) ? ForceRole.Defender
                    : active.ThirdRealm.ContainsKey(forceId) ? ForceRole.ThirdRealm : decided.Role;
                string slot = active.Slots.TryGetValue(forceId, out int[] slots) && slots.Length > 0 ? string.Join(",", slots) : "-";
                return decided with { Role = role, Slot = slot };
            }
            if (CarrierEvents.ContainsKey(decided.ObjectiveId))
                return decided with { Role = decided.Intent == Intent.DefendEvent ? ForceRole.Escort : ForceRole.Hunter };
        }
        return decided;
    }

    private static void PruneForceIntents(long nowTick)
    {
        foreach (var entry in ForceIntents)
            if (nowTick - Volatile.Read(ref entry.Value.LastWriteTick) > ForceIntentForgetMilliseconds)
                ForceIntents.TryRemove(entry.Key, out _);
    }

    // ---- the shadow (step 2) ------------------------------------------------------------------------------------------

    private static long _shadowChecks, _shadowMismatches;
    public static long ShadowChecks => Interlocked.Read(ref _shadowChecks);
    public static long ShadowMismatches => Interlocked.Read(ref _shadowMismatches);

    /// <summary>Whether the bot's own fields agree with its force's record (no record: nothing to compare). Pure.</summary>
    public static bool ShadowAgrees(ForceIntent force, Intent localIntent, bool localShared) =>
        force == null || force.Intent == localIntent && force.Shared == localShared;

    /// <summary>
    /// Logging only: compares the bot's <c>_rvrIntent</c> and shared flag with the force's record and writes
    /// <c>RVR_DECIDE what=shadow</c> on a change (rule <c>shadow.mismatch</c> or <c>shadow.match</c>).
    /// </summary>
    public static bool Shadow(GameBot bot, string forceId, Intent localIntent, bool localShared)
    {
        ForceIntent force = IntentOf(forceId);
        if (force == null)
            return true;
        Interlocked.Increment(ref _shadowChecks);
        bool agrees = ShadowAgrees(force, localIntent, localShared);
        if (!agrees)
            Interlocked.Increment(ref _shadowMismatches);
        RvrDecide.Change(bot, "event", "shadow", agrees ? "match" : $"{localIntent}{(localShared ? "+event" : string.Empty)}/{force.Summary}",
            agrees ? "shadow.match" : "shadow.mismatch");
        return agrees;
    }

    /// <summary>Tests.</summary>
    public static void ResetForceIntents()
    {
        ForceIntents.Clear();
        Interlocked.Exchange(ref _shadowChecks, 0);
        Interlocked.Exchange(ref _shadowMismatches, 0);
    }
}
