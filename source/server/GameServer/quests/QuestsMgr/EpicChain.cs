using System;
using System.Collections.Generic;
using System.Linq;
namespace DOL.GS.Quests;

// The GM's /epic command's decisions: which classic data quests make up a class's epic chain, the state of each step
// for a character, and which steps "/epic done <level>" marks finished. It reads nothing from the running server, so
// unit tests drive it directly; EpicCommand feeds it the quest rows and the character's quests. From lometur/HearthDAoC.

// One classic data quest. Classes empty: every class. Dependencies: its QuestDependency entries.
public sealed record EpicQuest(int Id, string Name, int MinLevel, int MaxLevel, ushort StartRegion,
    IReadOnlySet<int> Classes, IReadOnlyList<string> Dependencies);

public enum EpicStepState { Finished, Active, CanTake, Closed, Waiting }

public sealed record EpicStep(EpicQuest Quest, EpicStepState State, string Detail);

// A character's quests: finished quest names and IDs, active quest ID -> stage, and level.
public sealed record EpicProgress(ICollection<string> FinishedNames, ICollection<int> FinishedIds,
    IReadOnlyDictionary<int, int> ActiveStages, int Level);

public static class EpicChain
{
    // The Shrouded Isles home regions (Albion, Midgard, Hibernia). At one level, "/epic done" takes a step given
    // elsewhere first, so it marks the classic 7 and 11 unless a Shrouded Isles version is already finished.
    public static readonly IReadOnlySet<ushort> ShroudedIslesRegions = new HashSet<ushort> { 51, 151, 181 };

    public static IReadOnlySet<int> ParseClasses(string allowed)
    {
        var classes = new HashSet<int>();
        foreach (string part in (allowed ?? string.Empty).Split(new[] { '|', ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            if (int.TryParse(part.Trim(), out int id))
                classes.Add(id);
        }
        return classes;
    }

    public static IReadOnlyList<string> ParseDependencies(string dependency) =>
        (dependency ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries);

    // The chain of a class: from its last linked step (a quest with an ID entry that no other quest of the class
    // needs; the highest level first), every quest its entries name, back to the start, in level order (at one
    // level, steps given outside the Shrouded Isles first). A name entry leads to the class's quests of that name at
    // the highest level not above the step's. Nothing for a class whose quests have no ID entries.
    public static IReadOnlyList<EpicQuest> ChainFor(IEnumerable<EpicQuest> quests, int classId)
    {
        List<EpicQuest> mine = quests.Where(q => q.Classes.Count == 0 || q.Classes.Contains(classId)).ToList();
        Dictionary<int, EpicQuest> byId = mine.ToDictionary(q => q.Id);
        var needed = new HashSet<int>();
        foreach (EpicQuest quest in mine)
        {
            foreach (string entry in quest.Dependencies)
            {
                if (!QuestDependencies.TryParseIds(entry, out int[] ids, out bool closes) || closes)
                    continue;
                foreach (int id in ids)
                {
                    if (id != quest.Id)
                        needed.Add(id);
                }
            }
        }
        EpicQuest last = mine
            .Where(q => q.Dependencies.Any(QuestDependencies.IsIdEntry) && !needed.Contains(q.Id) && !NeedsItself(q))
            .OrderByDescending(q => q.MinLevel).ThenBy(q => q.Id).FirstOrDefault();
        if (last == null)
            return Array.Empty<EpicQuest>();
        var found = new Dictionary<int, EpicQuest>();
        var queue = new Queue<EpicQuest>();
        queue.Enqueue(last);
        while (queue.Count > 0)
        {
            EpicQuest quest = queue.Dequeue();
            if (!found.TryAdd(quest.Id, quest))
                continue;
            foreach (string entry in quest.Dependencies)
            {
                if (QuestDependencies.TryParseIds(entry, out int[] ids, out _))
                {
                    foreach (int id in ids)
                    {
                        if (byId.TryGetValue(id, out EpicQuest before))
                            queue.Enqueue(before);
                    }
                }
                else if (!QuestDependencies.IsIdEntry(entry))
                {
                    List<EpicQuest> named = mine.Where(q => q.Id != quest.Id && q.MinLevel <= quest.MinLevel
                        && string.Equals(q.Name, entry, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (named.Count == 0)
                        continue;
                    int top = named.Max(q => q.MinLevel);
                    foreach (EpicQuest before in named.Where(q => q.MinLevel == top))
                        queue.Enqueue(before);
                }
            }
        }
        return found.Values.OrderBy(q => q.MinLevel)
            .ThenBy(q => ShroudedIslesRegions.Contains(q.StartRegion) ? 1 : 0)
            .ThenBy(q => q.Id).ToList();
    }

    // Each step's state, in chain order. A step that needs one of some quests of the chain, all of them closed to the
    // character, is closed too: after the Shrouded Isles 7, Camelot's 11 (it needs Camelot's 7, which the Shrouded
    // Isles 7 closes, or the Supply Run offered to no one) can never be taken.
    public static IReadOnlyList<EpicStep> Steps(IReadOnlyList<EpicQuest> chain, EpicProgress progress)
    {
        var active = new HashSet<int>(progress.ActiveStages.Keys);
        Dictionary<int, EpicQuest> byId = chain.ToDictionary(q => q.Id);
        var steps = new Dictionary<int, EpicStep>();
        var visiting = new HashSet<int>();
        EpicStep Of(EpicQuest quest)
        {
            if (steps.TryGetValue(quest.Id, out EpicStep known))
                return known;
            visiting.Add(quest.Id);
            // A quest outside the chain, or one already being worked out (a loop in the links), is not known closed.
            EpicStep step = StepOf(quest, progress, active,
                id => byId.TryGetValue(id, out EpicQuest other) && !visiting.Contains(id) ? Of(other) : null);
            visiting.Remove(quest.Id);
            return steps[quest.Id] = step;
        }
        return chain.Select(Of).ToList();
    }

    // The step "/epic goto" leads to when none is active: of the steps that can be taken or wait, those whose entries
    // are all met (only the level may be missing), and of these the first given in the region the character is in,
    // else the first; failing those, the first step that can be taken or waits. So a character in the Shrouded Isles
    // starts with their trainer's Shrouded Isles 7, not Camelot's; after it goes on to the Shrouded Isles 11 from
    // anywhere (Camelot's is closed); and is never sent ahead to a later step whose giver happens to stand in the
    // region. Null when no step is left.
    public static EpicQuest NextStep(IReadOnlyList<EpicQuest> chain, EpicProgress progress, ushort region)
    {
        var active = new HashSet<int>(progress.ActiveStages.Keys);
        List<EpicQuest> open = Steps(chain, progress)
            .Where(s => s.State is EpicStepState.CanTake or EpicStepState.Waiting).Select(s => s.Quest).ToList();
        List<EpicQuest> ready = open
            .Where(q => QuestDependencies.AreMet(q.Dependencies, progress.FinishedNames, progress.FinishedIds, active)).ToList();
        return ready.FirstOrDefault(q => q.StartRegion == region) ?? ready.FirstOrDefault() ?? open.FirstOrDefault();
    }

    // The steps below <level> that "/epic done" marks finished, in chain order: each one whose entries are met by
    // what the character has finished, plus the steps marked before it. A closed step is never marked; an active
    // one is (EpicCommand ends it).
    public static IReadOnlyList<int> FinishBelow(IReadOnlyList<EpicQuest> chain, int level, EpicProgress progress)
    {
        var names = new List<string>(progress.FinishedNames);
        var finished = new HashSet<int>(progress.FinishedIds);
        var active = new HashSet<int>(progress.ActiveStages.Keys);
        var marked = new List<int>();
        foreach (EpicQuest quest in chain)
        {
            if (quest.MinLevel >= level || finished.Contains(quest.Id) || IsClosedForAll(quest))
                continue;
            bool wasActive = active.Remove(quest.Id);
            if (!QuestDependencies.AreMet(quest.Dependencies, names, finished, active))
            {
                if (wasActive)
                    active.Add(quest.Id);
                continue;
            }
            marked.Add(quest.Id);
            finished.Add(quest.Id);
            names.Add(quest.Name);
        }
        return marked;
    }

    // `stepOf`: the state of another quest of the chain, or null when it isn't known.
    private static EpicStep StepOf(EpicQuest quest, EpicProgress progress, ICollection<int> active, Func<int, EpicStep> stepOf)
    {
        if (progress.FinishedIds.Contains(quest.Id))
            return new EpicStep(quest, EpicStepState.Finished, "finished");
        if (progress.ActiveStages.TryGetValue(quest.Id, out int stage))
            return new EpicStep(quest, EpicStepState.Active, $"active, stage {stage}");
        if (IsClosedForAll(quest))
            return new EpicStep(quest, EpicStepState.Closed, OfferedToNoOne);
        string closer = quest.Dependencies.FirstOrDefault(e => e.Trim().StartsWith("!#", StringComparison.Ordinal)
            && !QuestDependencies.IsMet(e, progress.FinishedNames, progress.FinishedIds, active));
        if (closer != null)
            return new EpicStep(quest, EpicStepState.Closed, ClosedBy + closer.Trim());
        foreach (string entry in quest.Dependencies)
        {
            if (!QuestDependencies.TryParseIds(entry, out int[] ids, out bool closes) || closes
                || QuestDependencies.IsMet(entry, progress.FinishedNames, progress.FinishedIds, active))
                continue;
            List<EpicStep> needed = ids.Select(stepOf).ToList();
            if (needed.All(s => s?.State == EpicStepState.Closed))
                return new EpicStep(quest, EpicStepState.Closed,
                    $"closed: needs {entry.Trim()}, but " + And(needed.Select(s => $"{s.Quest.Id} is {Why(s)}")));
        }
        List<string> needs = quest.Dependencies
            .Where(e => !QuestDependencies.IsMet(e, progress.FinishedNames, progress.FinishedIds, active))
            .Select(e => e.Trim()).ToList();
        if (progress.Level < quest.MinLevel)
            needs.Insert(0, $"level {quest.MinLevel}");
        return needs.Count == 0
            ? new EpicStep(quest, EpicStepState.CanTake, "can take")
            : new EpicStep(quest, EpicStepState.Waiting, "needs " + string.Join(", ", needs));
    }

    private const string OfferedToNoOne = "offered to no one", ClosedBy = "closed by ";

    // Why a closed step is closed, after "<ID> is": its own reason, or just "closed" when a need closes it in turn.
    private static string Why(EpicStep step) =>
        step.Detail == OfferedToNoOne || step.Detail.StartsWith(ClosedBy, StringComparison.Ordinal) ? step.Detail : "closed";

    // "a", "a and b", "a, b and c".
    private static string And(IEnumerable<string> parts)
    {
        List<string> list = parts.ToList();
        return list.Count < 2 ? string.Concat(list) : string.Join(", ", list.Take(list.Count - 1)) + " and " + list[^1];
    }

    // Offered to no one: a level range that admits no level, or a dependency on itself (the world fix closes Lady
    // Aelawen's Supply Run that way).
    private static bool IsClosedForAll(EpicQuest quest) => quest.MaxLevel < quest.MinLevel || NeedsItself(quest);

    private static bool NeedsItself(EpicQuest quest) => quest.Dependencies.Any(e =>
        QuestDependencies.TryParseIds(e, out int[] ids, out bool closes) && !closes && ids.Length == 1 && ids[0] == quest.Id);
}
