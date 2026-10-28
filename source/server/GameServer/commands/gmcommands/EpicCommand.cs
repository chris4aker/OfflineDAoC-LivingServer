using System;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.Quests;

namespace DOL.GS.Commands;

// /epic, the GM's tool for testing an epic chain. EpicChain makes every decision; this class reads the classic data
// quests and the character's quests, and carries out "done", "goto" and "reset" on the loaded quest lists and the
// database together, so no relog is needed. It works on the GM's target if that is a player, otherwise on the GM.
// From lometur/HearthDAoC.
[CmdAttribute(
    "&epic",
    ePrivLevel.GM,
    "Show or change a character's epic chain (your target, or you)",
    "/epic - the chain and the state of each step",
    "/epic done <level> - mark every step below <level> finished",
    "/epic goto - go to the current stage's map marker, or to the next step's giver",
    "/epic reset - remove every step of the chain, active and finished")]
public sealed class EpicCommandHandler : AbstractCommandHandler, ICommandHandler
{
    public void OnCommand(GameClient client, string[] args)
    {
        GamePlayer target = client.Player.TargetObject as GamePlayer ?? client.Player;
        IReadOnlyList<EpicQuest> chain = EpicChain.ChainFor(LoadQuests(), target.CharacterClass.ID);
        if (chain.Count == 0)
        {
            DisplayMessage(client, $"{target.Name} ({target.CharacterClass.Name}) has no epic chain linked by quest IDs.");
            return;
        }
        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        if (sub == string.Empty)
            Show(client, target, chain);
        else if (sub == "done" && args.Length > 2 && int.TryParse(args[2], out int level))
            Done(client, target, chain, level);
        else if (sub == "goto")
            GoTo(client, target, chain);
        else if (sub == "reset")
            Reset(client, target, chain);
        else
            DisplaySyntax(client);
    }

    private static List<EpicQuest> LoadQuests()
    {
        var quests = new List<EpicQuest>();
        foreach (DbDataQuest row in GameServer.Database.SelectAllObjects<DbDataQuest>())
        {
            if (row.ClassType == null || !row.ClassType.Contains("ClassicQuestStep", StringComparison.Ordinal))
                continue;
            quests.Add(new EpicQuest(row.ID, row.Name, row.MinLevel, row.MaxLevel, row.StartRegionID,
                EpicChain.ParseClasses(row.AllowedClasses), EpicChain.ParseDependencies(row.QuestDependency)));
        }
        return quests;
    }

    private static EpicProgress ProgressOf(GamePlayer player)
    {
        var names = new List<string>();
        var ids = new HashSet<int>();
        foreach (AbstractQuest quest in player.GetFinishedQuests())
        {
            if (quest is DataQuest dataQuest)
            {
                names.Add(dataQuest.Name);
                ids.Add(dataQuest.ID);
            }
        }
        var active = new Dictionary<int, int>();
        foreach (AbstractQuest quest in player.QuestList.Keys)
        {
            if (quest is DataQuest dataQuest)
                active[dataQuest.ID] = dataQuest.Step;
        }
        return new EpicProgress(names, ids, active, player.Level);
    }

    private void Show(GameClient client, GamePlayer target, IReadOnlyList<EpicQuest> chain)
    {
        DisplayMessage(client, $"{target.Name}, level {target.Level} {target.CharacterClass.Name}:");
        foreach (EpicStep step in EpicChain.Steps(chain, ProgressOf(target)))
            DisplayMessage(client, $"  {step.Quest.MinLevel} {step.Quest.Name} ({step.Quest.Id}): {step.Detail}");
    }

    private void Done(GameClient client, GamePlayer target, IReadOnlyList<EpicQuest> chain, int level)
    {
        IReadOnlyList<int> ids = EpicChain.FinishBelow(chain, level, ProgressOf(target));
        foreach (int id in ids)
        {
            DbDataQuest row = GameServer.Database.FindObjectByKey<DbDataQuest>(id);
            if (row == null)
                continue;
            DataQuest active = target.QuestList.Keys.OfType<DataQuest>().FirstOrDefault(q => q.ID == id);
            if (active != null)
            {
                RemoveActive(target, active);
                active.DeleteFromDatabase();
            }
            var finished = new DbCharacterXDataQuest(target.QuestPlayerID, id) { Step = 0, Count = 1 };
            GameServer.Database.AddObject(finished);
            target.AddFinishedQuest(new DataQuest(target, row, finished));
        }
        target.Out.SendQuestListUpdate();
        QuestIndicators.Refresh(target); // the markers around the character follow the new quest lists at once
        DisplayMessage(client, ids.Count == 0
            ? $"Nothing below level {level} to mark for {target.Name}."
            : $"{target.Name}: marked finished {string.Join(", ", ids)}. See /epic.");
    }

    private void GoTo(GameClient client, GamePlayer target, IReadOnlyList<EpicQuest> chain)
    {
        var ids = new HashSet<int>(chain.Select(q => q.Id));
        DataQuest active = target.QuestList.Keys.OfType<DataQuest>().FirstOrDefault(q => ids.Contains(q.ID));
        if (active != null)
        {
            ClassicQuests.Point marker = ClassicQuests.MarkerFor(active.ID, active.Step);
            if (marker == null)
            {
                DisplayMessage(client, $"{active.Name} ({active.ID}) stage {active.Step} has no map marker.");
                return;
            }
            target.MoveTo(marker.Region, marker.X, marker.Y, marker.Z, target.Heading);
            DisplayMessage(client, $"{target.Name} is at the marker of {active.Name} ({active.ID}) stage {active.Step}.");
            return;
        }
        EpicQuest next = EpicChain.NextStep(chain, ProgressOf(target), target.CurrentRegionID);
        if (next == null)
        {
            DisplayMessage(client, $"{target.Name} has no step left to take; see /epic.");
            return;
        }
        DbDataQuest row = GameServer.Database.FindObjectByKey<DbDataQuest>(next.Id);
        // The giver's name compares without case, as quests now match names (QuestNames.Same).
        GameNPC giver = row == null ? null : WorldMgr.GetRegion(row.StartRegionID)?.Objects?.OfType<GameNPC>()
            .FirstOrDefault(n => n.Realm == target.Realm && QuestNames.Same(row.StartName, n.Name));
        if (giver == null)
        {
            DisplayMessage(client, $"{row?.StartName} isn't in region {row?.StartRegionID}.");
            return;
        }
        target.MoveTo(giver.CurrentRegionID, giver.X, giver.Y, giver.Z, giver.Heading);
        DisplayMessage(client, $"{target.Name} is at {giver.Name}, who gives {next.Name} ({next.Id}).");
    }

    private void Reset(GameClient client, GamePlayer target, IReadOnlyList<EpicQuest> chain)
    {
        var ids = new HashSet<int>(chain.Select(q => q.Id));
        int last = chain[^1].Id;
        bool hadLast = ProgressOf(target).FinishedIds.Contains(last);
        foreach (DataQuest active in target.QuestList.Keys.OfType<DataQuest>().Where(q => ids.Contains(q.ID)).ToList())
        {
            RemoveActive(target, active);
            active.DeleteFromDatabase();
        }
        target.RemoveFinishedQuests(q => q is DataQuest dataQuest && ids.Contains(dataQuest.ID));
        target.Out.SendQuestListUpdate();
        QuestIndicators.Refresh(target); // the markers around the character follow the new quest lists at once
        DisplayMessage(client, $"{target.Name}: the chain is reset." +
            (hadLast ? $" That removed the finished {chain[^1].Name} ({last}): its reward can be given again." : string.Empty));
    }

    private static void RemoveActive(GamePlayer player, DataQuest quest)
    {
        if (player.QuestList.TryRemove(quest, out byte index))
        {
            player.AvailableQuestIndexes.Enqueue(index);
            player.Out.SendQuestRemove(index);
        }
    }
}
