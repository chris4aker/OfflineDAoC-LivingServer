using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Events;
using DOL.Logging;

namespace DOL.GS.Quests;

// One quest marker scheme for every NPC. From lometur/HearthDAoC.
// GameNPC.GetQuestIndicator returns a meaning (Available: a quest to take; Pending: see me for your current step;
// Finish: turn in; None), worked out by For. The 1.127 packet writers turn it into what the client draws (Wire: a
// yellow, white or blue knot over the head) and record it per player and NPC (LastSent). Refresh, asked whenever a
// player's level, class or quests change, sends the quest effect for every NPC the client has whose value changed, at
// most once per game-loop tick.

// The current step of a data quest in the journal (region 0 is any region).
public readonly record struct JournalStep(DataQuest.eStepType Type, string TargetName, ushort TargetRegion);

// A quest the NPC gives. Qualifies: GameNPC.CanShowOneQuest's test for it. MinLevel: a data quest's MinLevel, a
// scripted quest's Level when its class declares one, else null (no level rule).
public readonly record struct QuestOffer(int? MinLevel, bool Qualifies);

public static class QuestIndicators
{
    // The 1.73 rule: no yellow knot for a quest whose minimum level is more than this below the player's level.
    public const int AvailableLevelSpan = 15;

    public static bool FinishesHere(DataQuest.eStepType type) => type is DataQuest.eStepType.DeliverFinish
        or DataQuest.eStepType.InteractFinish or DataQuest.eStepType.WhisperFinish or DataQuest.eStepType.CollectFinish;

    public static bool PendsHere(DataQuest.eStepType type) => type is DataQuest.eStepType.Deliver
        or DataQuest.eStepType.Interact or DataQuest.eStepType.Whisper or DataQuest.eStepType.Collect;

    public static bool Targets(JournalStep step, string npcName, ushort npcRegion) =>
        !string.IsNullOrEmpty(step.TargetName) && QuestNames.Same(step.TargetName, npcName)
        && (step.TargetRegion == 0 || step.TargetRegion == npcRegion);

    public static bool Shows(QuestOffer offer, int playerLevel) =>
        offer.Qualifies && (offer.MinLevel is not int min || playerLevel - min <= AvailableLevelSpan);

    // Finish, then Pending, then Available. offers is read only when the journal decides nothing, so a lazy sequence
    // runs the qualification checks only when needed. Kill and search targets get nothing.
    public static eQuestIndicator For(string npcName, ushort npcRegion, int playerLevel,
        IEnumerable<JournalStep> steps, bool rewardQuestDone, IEnumerable<QuestOffer> offers)
    {
        if (rewardQuestDone)
            return eQuestIndicator.Finish;
        bool pending = false;
        foreach (JournalStep step in steps)
        {
            if (!Targets(step, npcName, npcRegion))
                continue;
            if (FinishesHere(step.Type))
                return eQuestIndicator.Finish;
            pending |= PendsHere(step.Type);
        }
        if (pending)
            return eQuestIndicator.Pending;
        foreach (QuestOffer offer in offers)
        {
            if (Shows(offer, playerLevel))
                return eQuestIndicator.Available;
        }
        return eQuestIndicator.None;
    }

    // GameNPC.GetQuestIndicator's inputs; scripted and offered are the NPC's quest lists, copied under its locks.
    public static eQuestIndicator For(GameNPC npc, GamePlayer player, AbstractQuest[] scripted, DataQuest[] offered)
    {
        var steps = new List<JournalStep>();
        bool rewardQuestDone = false;
        foreach (KeyValuePair<AbstractQuest, byte> pair in player.QuestList)
        {
            switch (pair.Key)
            {
                case DataQuest dataQuest when dataQuest.Step > 0:
                    steps.Add(new JournalStep(dataQuest.StepType, dataQuest.TargetName, dataQuest.TargetRegion));
                    break;
                case RewardQuest rewardQuest when RewardDone(rewardQuest.QuestGiver == npc, rewardQuest.Goals.Select(g => g.IsAchieved)):
                    rewardQuestDone = true;
                    break;
            }
        }
        return For(npc.Name, npc.CurrentRegionID, player.Level, steps, rewardQuestDone, Offers(player, scripted, offered));
    }

    // GameNPC.CanShowOneQuest's tests, one offer per quest, in its order.
    private static IEnumerable<QuestOffer> Offers(GamePlayer player, AbstractQuest[] scripted, DataQuest[] offered)
    {
        foreach (AbstractQuest quest in scripted)
        {
            Type type = quest.GetType();
            yield return ScriptedOffer(ScriptedMinLevel(quest), quest.CheckQuestQualification(player),
                player.HasFinishedQuest(type), player.IsDoingQuest(type) != null, quest.MaxQuestCount);
        }
        foreach (DataQuest quest in offered)
        {
            if (DataOffer(quest.ShowIndicator, quest.Level, () => quest.CheckQuestQualification(player), out QuestOffer offer))
                yield return offer;
        }
    }

    // Offered while the times finished, plus the one being done, stay under MaxQuestCount.
    public static QuestOffer ScriptedOffer(int? minLevel, bool qualifies, int finished, bool doing, int maxCount) =>
        new(minLevel, qualifies && finished + (doing ? 1 : 0) < maxCount);

    // Only a data quest that shows a marker is asked to qualify (a Collection quest's check reads the database).
    public static bool DataOffer(bool showIndicator, int minLevel, Func<bool> qualifies, out QuestOffer offer)
    {
        offer = showIndicator ? new QuestOffer(minLevel, qualifies()) : default;
        return showIndicator;
    }

    // A reward quest turns in at its own giver once every goal is done; another giver's goals are not read.
    public static bool RewardDone(bool ownGiver, IEnumerable<bool> goalsAchieved) => ownGiver && goalsAchieved.All(done => done);

    // AbstractQuest.Level is 1 unless a quest sets it (several level-50 epics never do), so it counts only when the class
    // declares it. Daily, weekly and monthly quests follow the player's level: no level rule.
    private static readonly ConcurrentDictionary<Type, bool> s_declaresLevel = new();

    public static int? ScriptedMinLevel(AbstractQuest quest) =>
        quest is DailyQuest or WeeklyQuest or MonthlyQuest ? null
        : s_declaresLevel.GetOrAdd(quest.GetType(),
            static type => type.GetProperty(nameof(AbstractQuest.Level))?.GetMethod?.DeclaringType != typeof(AbstractQuest))
            ? quest.Level
            : null;

    // What the 1.127 client gets for a meaning.
    public static eQuestIndicator Wire(eQuestIndicator meaning) => meaning switch
    {
        eQuestIndicator.Available => eQuestIndicator.Lore, // 8: yellow knot
        eQuestIndicator.Pending => eQuestIndicator.Pending, // 16: white knot
        eQuestIndicator.Finish => eQuestIndicator.Finish, // 2: blue knot
        _ => eQuestIndicator.None, // Lore (Atlas trainers, buff merchant), Lesson, None: nothing
    };

    // A quest effect carries only the object ID: for an NPC out of the world or in another region it means another object.
    public static bool SendsTo(bool npcInWorld, ushort npcRegion, ushort playerRegion) =>
        npcInWorld && npcRegion == playerRegion;

    public static readonly IndicatorSentStore LastSent = new();

    private static readonly Logger Log = LoggerManager.Create(typeof(QuestIndicators));

    // PacketLib173.SendNPCsQuestEffect: false when nothing may be sent; otherwise the value to send, recorded.
    public static bool Sending(GamePlayer player, GameNPC npc, ref eQuestIndicator value)
    {
        if (!SendsTo(npc.ObjectState == GameObject.eObjectState.Active, npc.CurrentRegionID, player.CurrentRegionID))
            return false;
        value = Wire(value);
        LastSent.Record(player, npc, (byte)value, new SentPlace(npc.ObjectID, npc.CurrentRegionID));
        return true;
    }

    // PacketLib1124.SendNPCCreate: as Sending; a changed value, or an object ID reused, also asks a refresh, whose
    // quest effect draws it even on a client that still had an object under that ID.
    public static eQuestIndicator SendingCreate(GamePlayer player, GameNPC npc, eQuestIndicator value)
    {
        eQuestIndicator wire = Wire(value);
        if (player != null && LastSent.RecordCreate(player, npc, (byte)wire, new SentPlace(npc.ObjectID, npc.CurrentRegionID), false))
            Refresh(player);
        return wire;
    }

    private static readonly Action<GamePlayer> s_run = Run;

    // Asks for the markers of every NPC the player's client has to be worked out again. Safe from any thread and lock;
    // it only queues one run, at the start of the next game-loop tick.
    public static void Refresh(GamePlayer player)
    {
        if (player != null && LastSent.TryQueue(player))
            GameLoopService.Instance.Post(s_run, player);
    }

    private static void Run(GamePlayer player)
    {
        if (!LastSent.Dequeue(player))
            return;
        // Not in the world or not playing: once in, the client gets every NPC again with its create packet.
        if (player.ObjectState != GameObject.eObjectState.Active
            || player.Client?.ClientState != GameClient.eClientState.Playing)
            return;

        PlayerObjectCache cache = player.PlayerObjectCache;
        List<GameNPC> known;
        lock (cache.NpcUpdateCacheLock)
            known = new List<GameNPC>(cache.NpcUpdateCache.Keys);

        ushort region = player.CurrentRegionID;
        LastSent.Prune(player, region, o => o is GameNPC npc && npc.ObjectState == GameObject.eObjectState.Active,
            o => ((GameNPC)o).CurrentRegionID);
        known.RemoveAll(npc => npc.ObjectState != GameObject.eObjectState.Active || npc.CurrentRegionID != region);

        LastSent.SendChanges(player, known, o =>
            {
                GameNPC npc = (GameNPC)o;
                try
                {
                    eQuestIndicator meaning = npc.GetQuestIndicator(player);
                    return (meaning, (byte)Wire(meaning));
                }
                catch (Exception e)
                {
                    // One NPC's failure must not stop the others: it keeps what it had.
                    Log.Error($"Quest indicator of {npc.Name} for {player.Name}", e);
                    return (eQuestIndicator.None, LastSent.Last(player, npc));
                }
            },
            (o, meaning) => player.Out.SendNPCsQuestEffect((GameNPC)o, meaning));
    }

    [ScriptLoadedEvent]
    public static void OnScriptLoaded(DOLEvent e, object sender, EventArgs args) =>
        GameEventMgr.AddHandler(GamePlayerEvent.Quit, OnPlayerQuit);

    [ScriptUnloadedEvent]
    public static void OnScriptUnloaded(DOLEvent e, object sender, EventArgs args) =>
        GameEventMgr.RemoveHandler(GamePlayerEvent.Quit, OnPlayerQuit);

    private static void OnPlayerQuit(DOLEvent e, object sender, EventArgs args)
    {
        if (sender is GamePlayer player)
            LastSent.Forget(player);
    }
}
