using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.Quests;

namespace DOL.UnitTests.QuestHarness
{
    /// <summary>Who makes the kills of a kill step.</summary>
    public enum KillBy
    {
        /// <summary>The player alone.</summary>
        Player,
        /// <summary>Another real player in the player's group; the player never touches the monster.</summary>
        Groupmate,
        /// <summary>An autonomous gamebot invited into the player's group; the player never touches the monster.</summary>
        Gamebot,
    }

    public sealed class WalkOptions
    {
        /// <summary>The player's class; null: the row's first allowed class, else Armsman.</summary>
        public int? ClassId;
        /// <summary>The player's level when the quest is offered; null: the row's minimum level.</summary>
        public byte? Level;
        public KillBy KillBy = KillBy.Player;
        /// <summary>After taking the quest the player levels past its maximum level (high XP rates), before any step.</summary>
        public bool OutlevelAfterAccept;
        /// <summary>Quest prerequisites (QuestDependency) count as done, as for a player who did them.</summary>
        public bool PrerequisitesDone = true;
        /// <summary>The most kills, searches or clicks one step gets before it counts as stuck.</summary>
        public int MaxTriesPerStep = 200;
    }

    public sealed class StepRecord
    {
        public int Step;
        public DataQuest.eStepType Type;
        public string Target;
        public string Action;
        public bool Advanced;
        public string Note;
        /// <summary>The target NPC's quest marker while this step was current (talk and hand-in steps).</summary>
        public eQuestIndicator? TargetMarker;
        /// <summary>A classic red map marker shown for this step.</summary>
        public bool MapMarker;
        public long Experience;

        public override string ToString() => $"{Step} {Type} {Target}: {Action}{(Advanced ? "" : " (no progress)")}{(Note == null ? "" : " - " + Note)}";
    }

    public sealed class QuestWalk
    {
        public int QuestId;
        public string Name;
        public bool Started;
        public bool Finished;
        /// <summary>Why the walk stopped short, or null.</summary>
        public string Stuck;
        /// <summary>The harness itself failed (not the quest): the walk says nothing about the quest.</summary>
        public string HarnessProblem;
        public int StepsTotal;
        public int StepReached;
        public eQuestIndicator? GiverMarkerBefore;
        public long Experience;
        public long Money;
        public long RealmPoints;
        public readonly List<string> ItemsReceived = new();
        public readonly List<StepRecord> Steps = new();
        public readonly List<string> Notes = new();
        public readonly List<string> Log = new();

        public bool Passed => Finished && Stuck == null;

        public override string ToString() =>
            $"[{QuestId}] {Name}: " + (Passed ? "finished" : $"stuck at step {StepReached}/{StepsTotal}: {Stuck}") +
            (Steps.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, Steps.Select(s => "  " + s))) +
            (Log.Count == 0 ? "" : Environment.NewLine + string.Join(Environment.NewLine, Log.Select(l => "  log: " + l)));
    }

    /// <summary>Plays a quest row from offer to finish with a <see cref="QuestHarness"/> and says how far it got.</summary>
    public static class QuestWalker
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        public const int DefaultClass = (int)eCharacterClass.Armsman;

        public static QuestWalk Walk(QuestHarness harness, DbDataQuest row, WalkOptions options = null)
        {
            options ??= new WalkOptions();
            var walk = new QuestWalk { QuestId = row.ID, Name = row.Name };
            try
            {
                Play(harness, row, options, walk);
            }
            catch (Exception ex)
            {
                walk.HarnessProblem = HarnessProblemIn(ex);
                if (walk.HarnessProblem != null)
                    walk.Stuck = "harness problem: " + walk.HarnessProblem;
                walk.Stuck ??= "the quest code threw " + ex.GetType().Name + ": " + ex.Message;
                walk.Log.Add(ex.ToString().Split('\n').Take(6).Aggregate((a, b) => a + " | " + b.Trim()));
            }
            walk.Log.AddRange(Logged(harness));
            return walk;
        }

        /// <summary>
        /// The server's error lines, without the ones the harness itself causes: a data quest's /search areas are
        /// registered with their region, and the harness has no regions. A SEARCH entry the harness can't read is
        /// still reported by the search step itself.
        /// </summary>
        private static List<string> Logged(QuestHarness harness) =>
            harness.Logs.TakeErrors().Where(line => !line.StartsWith("Error creating search area for ", StringComparison.Ordinal) &&
                // A slow first call (the JIT) of an event handler: timing, not the quest.
                !line.StartsWith("Invoke took ", StringComparison.Ordinal)).ToList();

        private static void Play(QuestHarness harness, DbDataQuest row, WalkOptions options, QuestWalk walk)
        {
            string[] types = Split(row.StepType);
            walk.StepsTotal = types.Length;
            ClassicQuests.QuestInfo classic = ClassicInfo(row.ID);
            int classId = options.ClassId ?? FirstClass(row.AllowedClasses) ?? DefaultClass;
            byte level = options.Level ?? (byte)Math.Clamp((int)row.MinLevel, 1, GamePlayer.MAX_LEVEL);
            short race = RaceFor(classic);
            ushort region = row.StartRegionID == 0 ? (ushort)1 : row.StartRegionID;

            HarnessPlayer player = harness.NewPlayer(classId, level, race);
            harness.MoveTo(region, 10_000, 10_000);
            long xpStart = player.Experience;
            var carriedAtStart = new HashSet<string>(harness.CarriedIds(), StringComparer.OrdinalIgnoreCase);

            switch (options.KillBy)
            {
                case KillBy.Groupmate:
                    HarnessPlayer mate = harness.NewPlayer(classId, level, race, name: "Groupmate", primary: false);
                    mate.TestRegion = region;
                    mate.X = player.X + 200;
                    mate.Y = player.Y;
                    HarnessGroups.Of(player, mate);
                    break;
                case KillBy.Gamebot:
                    HarnessGroups.Of(player, HarnessBot.Make(player));
                    break;
            }

            if (options.PrerequisitesDone)
                MarkPrerequisitesDone(harness, row, walk);

            if (!Start(harness, row, walk))
                return;

            walk.Started = true;
            if (options.OutlevelAfterAccept && row.MaxLevel < GamePlayer.MAX_LEVEL)
            {
                player.Level = (byte)Math.Min(GamePlayer.MAX_LEVEL, row.MaxLevel + 5);
                player.Experience = player.ExperienceForCurrentLevel;
                xpStart = player.Experience;
                walk.Notes.Add($"levelled to {player.Level} after taking the quest (its maximum level is {row.MaxLevel})");
            }

            DataQuest quest = harness.Active(row.ID);
            int guard = Math.Max(4, types.Length * 3);
            while (quest != null && !harness.Finished(row.ID) && guard-- > 0)
            {
                int step = quest.Step;
                walk.StepReached = step;
                long before = player.Experience;
                StepRecord record = PlayStep(harness, row, quest, classic, options, walk);
                record.Experience = player.Experience - before;
                walk.Steps.Add(record);
                if (harness.Finished(row.ID))
                    break;
                quest = harness.Active(row.ID);
                if (quest == null)
                {
                    walk.Stuck = $"the quest left the journal at step {step} without being finished";
                    return;
                }
                if (quest.Step == step)
                {
                    walk.Stuck = record.Note ?? $"step {step} ({record.Type}) did not advance";
                    // What the quest code logged on the way says why (an item template missing, a step it threw on).
                    List<string> said = Logged(harness);
                    walk.Log.AddRange(said);
                    if (said.Count > 0)
                        walk.Stuck += "; the server logged: " + said[0];
                    return;
                }
            }

            walk.Finished = harness.Finished(row.ID);
            if (!walk.Finished && walk.Stuck == null)
                walk.Stuck = "the quest did not finish after every step";
            walk.Experience = player.Experience - xpStart;
            walk.Money = player.MoneyAwards.Sum();
            walk.RealmPoints = player.RealmPointAwards.Sum();
            walk.ItemsReceived.AddRange(harness.CarriedIds().Where(id => !carriedAtStart.Contains(id)));
            if (walk.Finished)
                CheckFinalRewards(harness, row, walk);
        }

        // Taking the quest, by its start type

        private static bool Start(QuestHarness harness, DbDataQuest row, QuestWalk walk)
        {
            HarnessPlayer player = harness.Player;
            var start = (DataQuest.eStartType)row.StartType;
            if (string.IsNullOrWhiteSpace(row.StartName) && start != DataQuest.eStartType.SearchStart)
            {
                walk.Stuck = "the quest has no giver (StartName is empty)";
                return false;
            }

            HarnessNpc giver = harness.Giver(row);
            DataQuest offer = giver.DataQuestList.First(q => q.ID == row.ID);
            walk.GiverMarkerBefore = giver.GetQuestIndicator(player);
            if (!offer.CheckQuestQualification(player))
            {
                walk.Stuck = $"the quest is not offered to a level {player.Level} {player.CharacterClass.Name} (qualification refused)";
                return false;
            }

            switch (start)
            {
                case DataQuest.eStartType.Standard:
                    harness.Interact(giver);
                    if (string.IsNullOrWhiteSpace(row.AcceptText))
                    {
                        walk.Stuck = "the quest has no accept keyword (AcceptText is empty)";
                        return false;
                    }
                    harness.Whisper(giver, row.AcceptText);
                    break;
                case DataQuest.eStartType.AutoStart:
                    harness.Interact(giver);
                    break;
                case DataQuest.eStartType.RewardQuest:
                    harness.Interact(giver);
                    GameObject.FillDataQuestCache();
                    DataQuest.RewardQuestNotify(GamePlayerEvent.AcceptQuest, player,
                        new QuestEventArgs(giver, player, (ushort)(row.ID + DataQuest.DATAQUEST_CLIENTOFFSET)));
                    break;
                case DataQuest.eStartType.SearchStart:
                    typeof(AbstractQuest).GetMethod("QuestCommandCompleted", Hidden)
                        .Invoke(offer, new object[] { AbstractQuest.eQuestCommand.SEARCH_START, player });
                    break;
                case DataQuest.eStartType.Collection:
                    return Collection(harness, row, giver, walk);
                case DataQuest.eStartType.InteractComplete:
                    harness.Interact(giver);
                    return Completed(harness, row, walk, "interacting with the giver");
                case DataQuest.eStartType.KillComplete:
                    giver.Realm = eRealm.None;
                    harness.Kill(giver, player);
                    return Completed(harness, row, walk, "killing the giver");
                default:
                    walk.Stuck = $"unknown start type {row.StartType}";
                    return false;
            }

            if (harness.Active(row.ID) == null)
            {
                walk.Stuck = $"taking the quest ({start}) did not put it in the journal";
                return false;
            }
            return true;
        }

        private static bool Collection(QuestHarness harness, DbDataQuest row, HarnessNpc giver, QuestWalk walk)
        {
            DbInventoryItem item = harness.Carried(row.CollectItemTemplate) ?? harness.Hand(row.CollectItemTemplate);
            if (item == null)
            {
                walk.Stuck = $"the collection item {row.CollectItemTemplate} is not an item template";
                return false;
            }
            long before = harness.Player.Experience;
            harness.Give(giver, item);
            walk.Started = walk.Finished = harness.World.CharacterQuests.Any(c => c.DataQuestID == row.ID && c.Count > 0);
            walk.Experience = harness.Player.Experience - before;
            if (!walk.Finished)
                walk.Stuck = "handing the collection item in counted nothing";
            return false;
        }

        private static bool Completed(QuestHarness harness, DbDataQuest row, QuestWalk walk, string how)
        {
            walk.Started = walk.Finished = harness.World.CharacterQuests.Any(c => c.DataQuestID == row.ID && c.Count > 0);
            if (!walk.Finished)
                walk.Stuck = how + " did not complete the quest";
            return false;
        }

        // One step

        private static StepRecord PlayStep(QuestHarness harness, DbDataQuest row, DataQuest quest, ClassicQuests.QuestInfo classic,
            WalkOptions options, QuestWalk walk)
        {
            int step = quest.Step;
            var record = new StepRecord { Step = step, Type = quest.StepType, Target = quest.TargetName };
            harness.ClassicRefresh();
            ClassicQuests.StepInfo info = classic != null && step < classic.Steps.Count ? classic.Steps[step] : null;
            record.MapMarker = harness.Packets.MapMarkers.Any(m => m.Id == ClassicQuests.QuestMarkerId(row.ID));
            harness.Packets.MapMarkers.Clear();
            if (quest.Step != step || harness.Finished(row.ID))
            {
                record.Action = "completed by the classic refresh";
                record.Advanced = true;
                return record;
            }

            HarnessNpc target = string.IsNullOrEmpty(quest.TargetName) ? null : harness.Npc(quest.TargetName, quest.TargetRegion);
            if (target != null && (QuestIndicators.PendsHere(quest.StepType) || QuestIndicators.FinishesHere(quest.StepType)))
                record.TargetMarker = target.GetQuestIndicator(harness.Player);

            // A Custom with a Kind is a classic step of its own; one with only Drops (and no Kind) is an ordinary step whose
            // monsters drop what it collects (ClassicQuests.DropForSteps).
            if (!string.IsNullOrEmpty(info?.Custom?.Kind))
                Custom(harness, row, quest, info.Custom, record, options);
            else
                Standard(harness, row, quest, target, record, options, info?.Custom?.Drops);

            record.Advanced = harness.Finished(row.ID) || harness.Active(row.ID)?.Step != step;
            return record;
        }

        private static void Standard(QuestHarness harness, DbDataQuest row, DataQuest quest, HarnessNpc target, StepRecord record, WalkOptions options,
            List<ClassicQuests.StepDrop> drops)
        {
            int step = quest.Step;
            bool Moved() => harness.Finished(row.ID) || harness.Active(row.ID)?.Step != step;
            string[] stepItems = Split(row.StepItemTemplates);
            string stepItem = step - 1 < stepItems.Length ? stepItems[step - 1].Trim() : string.Empty;
            string[] collects = Split(row.CollectItemTemplate);
            (string collectId, int collectCount) = step - 1 < collects.Length ? QuestDeliveryItems.CollectEntry(collects[step - 1]) : (null, 1);

            if (target == null && record.Type is not (DataQuest.eStepType.Search or DataQuest.eStepType.SearchFinish))
            {
                record.Action = "nothing to do";
                record.Note = $"step {step} has no target (TargetName is empty)";
                return;
            }

            switch (record.Type)
            {
                case DataQuest.eStepType.Interact:
                case DataQuest.eStepType.InteractFinish:
                    record.Action = "interact with " + target.Name;
                    for (int i = 0; i < 8 && !Moved(); i++)
                        harness.Interact(target);
                    if (!Moved())
                        record.Note = $"interacting with {target.Name} did not advance step {step}";
                    return;

                case DataQuest.eStepType.Whisper:
                case DataQuest.eStepType.WhisperFinish:
                    string keyword = quest.CurrentAdvanceText;
                    record.Action = $"whisper [{keyword}] to {target.Name}";
                    if (string.IsNullOrWhiteSpace(keyword))
                    {
                        record.Note = $"step {step} is a whisper step with no keyword (AdvanceText is empty)";
                        return;
                    }
                    harness.Interact(target);
                    harness.Whisper(target, keyword);
                    if (!Moved())
                        record.Note = $"whispering [{keyword}] to {target.Name} did not advance step {step}";
                    return;

                case DataQuest.eStepType.Kill:
                case DataQuest.eStepType.KillFinish:
                    record.Action = $"kill {target.Name} ({options.KillBy})";
                    int tries = KillTries(stepItem, options.MaxTriesPerStep);
                    for (int i = 0; i < tries && !Moved(); i++)
                    {
                        HarnessNpc mob = harness.Monster(target.Name, quest.TargetRegion);
                        harness.Kill(mob, Attacker(harness, options));
                    }
                    if (!Moved())
                        record.Note = tries > 1 ? $"{tries} kills of {target.Name} never dropped {stepItem}" : $"killing {target.Name} did not advance step {step}";
                    return;

                case DataQuest.eStepType.Deliver:
                case DataQuest.eStepType.DeliverFinish:
                case DataQuest.eStepType.Collect:
                case DataQuest.eStepType.CollectFinish:
                    HandIn(harness, row, quest, target, record, stepItem, collectId, collectCount, Moved, options, drops);
                    return;

                case DataQuest.eStepType.Search:
                case DataQuest.eStepType.SearchFinish:
                    record.Action = "search";
                    if (!HasSearchArea(row.SourceName, step))
                    {
                        record.Note = $"step {step} is a search step with no SEARCH area for it in SourceName";
                        return;
                    }
                    int searches = KillTries(stepItem, options.MaxTriesPerStep);
                    for (int i = 0; i < searches && !Moved(); i++)
                        harness.SearchDone(quest);
                    if (!Moved())
                        record.Note = $"searching did not advance step {step}";
                    return;

                default:
                    record.Action = "nothing";
                    record.Note = $"step {step} has an unknown step type ({(int)record.Type})";
                    return;
            }
        }

        /// <summary>Who makes a kill: the player, a groupmate, or the gamebot in the group (WalkOptions.KillBy).</summary>
        private static GameLiving Attacker(QuestHarness harness, WalkOptions options) => options.KillBy switch
        {
            KillBy.Groupmate => harness.Player.Group.GetPlayersInTheGroup().First(p => p != harness.Player),
            KillBy.Gamebot => harness.Player.Group.GetMembersInTheGroup().OfType<GameBot>().First(),
            _ => harness.Player,
        };

        private static void HandIn(QuestHarness harness, DbDataQuest row, DataQuest quest, HarnessNpc target, StepRecord record,
            string stepItem, string collectId, int collectCount, Func<bool> moved, WalkOptions options, List<ClassicQuests.StepDrop> drops)
        {
            int step = quest.Step;
            bool delivery = record.Type is DataQuest.eStepType.Deliver or DataQuest.eStepType.DeliverFinish;
            string id = !string.IsNullOrEmpty(collectId) ? collectId : stepItem;
            if (string.IsNullOrEmpty(id))
            {
                record.Action = "hand in nothing";
                record.Note = $"step {step} is a hand-in with no item (neither CollectItemTemplate nor StepItemTemplates names one)";
                return;
            }
            DbInventoryItem item = harness.Carried(id);
            // A delivery item comes from the quest itself; give the classic refresh its minute to hand a lost one.
            for (int i = 0; item == null && delivery && i < 13; i++)
            {
                harness.ClassicRefresh();
                item = harness.Carried(id);
            }
            // A classic step whose monsters drop the item: kill them until the player carries enough.
            ClassicQuests.StepDrop drop = drops?.FirstOrDefault(d => string.Equals(d.Item, id, StringComparison.OrdinalIgnoreCase));
            if (drop != null && Count(harness, id) < collectCount)
            {
                int kills = 0;
                for (; kills < options.MaxTriesPerStep && Count(harness, id) < collectCount; kills++)
                    harness.Kill(harness.Monster(drop.Mob), Attacker(harness, options));
                if (Count(harness, id) < collectCount)
                {
                    record.Action = $"kill {drop.Mob} ({options.KillBy}) for {collectCount} {id}";
                    record.Note = $"{kills} kills of {drop.Mob} gave {Count(harness, id)} of the {collectCount} {id} step {step} needs";
                    return;
                }
                record.Action = $"kill {drop.Mob} ({options.KillBy}) {kills}x for {collectCount} {id}; ";
                item = harness.Carried(id);
            }
            if (item == null)
            {
                if (delivery && !QuestDeliveryItems.BringsToGiver(step, quest.TargetName, row.StartName) && string.IsNullOrEmpty(collectId))
                {
                    record.Action = $"deliver {id} to {target.Name}";
                    record.Note = $"the quest never handed {id}, so step {step} can't be delivered" +
                                  (harness.World.Item(id) == null ? $" ({id} is not an item template)" : string.Empty);
                    return;
                }
                item = harness.Hand(id, collectCount);
                if (item == null)
                {
                    record.Action = $"hand {id} to {target.Name}";
                    record.Note = $"{id} is not an item template, so step {step} can't be handed in";
                    return;
                }
                record.Action = $"the player brings {collectCount} {id} (bought, looted or found; the harness puts it in the backpack); ";
            }
            record.Action += $"give {id} to {target.Name}";
            harness.Give(target, item);
            if (!moved())
                record.Note = $"giving {id} to {target.Name} did not advance step {step}";
        }

        // The classic custom steps (ClassicQuests.CustomStep), each done as a player does it

        private static void Custom(QuestHarness harness, DbDataQuest row, DataQuest quest, ClassicQuests.CustomStep custom,
            StepRecord record, WalkOptions options)
        {
            int step = quest.Step;
            bool Moved() => harness.Finished(row.ID) || harness.Active(row.ID)?.Step != step;
            record.Action = "classic " + custom.Kind;
            switch (custom.Kind)
            {
                case "travel":
                    harness.MoveTo(custom.Region == 0 ? harness.Player.CurrentRegionID : custom.Region, custom.X, custom.Y, custom.Z);
                    harness.ClassicRefresh();
                    break;
                case "group":
                    if (harness.Player.Group == null)
                        HarnessGroups.Of(harness.Player, HarnessBot.Make(harness.Player));
                    harness.ClassicRefresh();
                    break;
                case "has_item":
                    for (int i = 0; i < 2 && harness.Carried(custom.Item) == null; i++)
                        harness.ClassicRefresh();
                    if (harness.Carried(custom.Item) == null && harness.Hand(custom.Item) == null)
                    {
                        record.Note = $"the step needs {custom.Item}, which is not an item template";
                        return;
                    }
                    harness.ClassicRefresh();
                    break;
                case "quest":
                    foreach (string name in (custom.Quest ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                        FinishedStub(harness, name, 0);
                    harness.ClassicRefresh();
                    break;
                case "use_item":
                    DbInventoryItem used = harness.Carried(custom.Item);
                    if (used == null) { harness.ClassicRefresh(); used = harness.Carried(custom.Item); }
                    used ??= harness.Hand(custom.Item);
                    if (used == null)
                    {
                        record.Note = $"the step uses {custom.Item}, which is not an item template";
                        return;
                    }
                    if (custom.Region != 0)
                        harness.MoveTo(custom.Region, custom.X, custom.Y, custom.Z);
                    harness.Use(used);
                    break;
                case "interact":
                    HarnessNpc talker = harness.Npc(custom.Target ?? string.Empty);
                    for (int i = 0; i < 8 && !Moved(); i++)
                        harness.Interact(talker);
                    break;
                case "die_to":
                    HarnessNpc killer = harness.Npc(custom.Target ?? string.Empty, 0, eRealm.None);
                    harness.Tick();
                    harness.Player.Notify(GameLivingEvent.Dying, harness.Player, new DyingEventArgs(killer, null));
                    break;
                case "trade":
                    Trade(harness, row, quest, custom, record, options, Moved);
                    break;
                case "choose":
                    string choice = custom.Choices?.Keys.FirstOrDefault();
                    if (choice == null)
                    {
                        record.Note = "a choose step with no choices";
                        return;
                    }
                    harness.Whisper(harness.Npc(custom.Target ?? string.Empty), choice);
                    break;
                case "say":
                    if (custom.Region != 0)
                        harness.MoveTo(custom.Region, custom.X, custom.Y, custom.Z);
                    harness.Say(custom.Words ?? string.Empty);
                    break;
                default:
                    record.Note = $"unknown classic step kind \"{custom.Kind}\"";
                    return;
            }
            if (!Moved())
                record.Note ??= $"the classic {custom.Kind} step {step} did not advance";
        }

        private static void Trade(QuestHarness harness, DbDataQuest row, DataQuest quest, ClassicQuests.CustomStep custom, StepRecord record,
            WalkOptions options, Func<bool> moved)
        {
            if (custom.Trades == null || custom.Trades.Count == 0)
            {
                record.Note = "a trade step with nothing to trade";
                return;
            }
            DbInventoryItem input = custom.Trades.Keys.Select(harness.Carried).FirstOrDefault(i => i != null);
            // A kill task: its monsters drop the input for a player on the step.
            ClassicQuests.StepDrop drop = custom.Drops?.FirstOrDefault(d => custom.Trades.ContainsKey(d.Item ?? string.Empty));
            for (int i = 0; input == null && drop != null && i < options.MaxTriesPerStep; i++)
            {
                HarnessNpc mob = harness.Monster(drop.Mob);
                harness.Kill(mob, harness.Player);
                input = harness.Carried(drop.Item);
            }
            if (input == null && drop != null)
            {
                record.Note = $"{options.MaxTriesPerStep} kills of {drop.Mob} never dropped {drop.Item}";
                return;
            }
            input ??= harness.Hand(custom.Trades.Keys.First());
            if (input == null)
            {
                record.Note = $"the trade input {custom.Trades.Keys.First()} is not an item template";
                return;
            }
            record.Action = $"classic trade {input.Id_nb} to {custom.Target}";
            harness.Give(harness.Npc(custom.Target ?? string.Empty), input);
        }

        // Rewards

        private static void CheckFinalRewards(QuestHarness harness, DbDataQuest row, QuestWalk walk)
        {
            foreach (string id in Split(row.FinalRewardItemTemplates).Where(s => s.Length > 0))
            {
                DbItemTemplate template = harness.World.Item(id);
                if (template == null)
                {
                    walk.Notes.Add($"final reward {id} is not an item template, so it was not given");
                    continue;
                }
                string given = ClassicQuests.RewardFor(harness.Player, template)?.Id_nb ?? id;
                if (!walk.ItemsReceived.Contains(given, StringComparer.OrdinalIgnoreCase))
                    walk.Notes.Add($"final reward {given} is not in the backpack after the finish");
            }
        }

        // Prerequisites

        private static void MarkPrerequisitesDone(QuestHarness harness, DbDataQuest row, QuestWalk walk)
        {
            foreach (string entry in Split(row.QuestDependency).Where(e => e.Trim().Length > 0))
            {
                string text = entry.Trim();
                if (text.StartsWith("!#", StringComparison.Ordinal))
                    continue; // a closing entry: met while those quests are not done
                if (QuestDependencies.TryParseIds(text, out int[] ids, out bool closes) && !closes)
                {
                    int id = ids[0];
                    FinishedStub(harness, harness.World.Quests.GetValueOrDefault(id)?.Name ?? "#" + id, id);
                }
                else
                    FinishedStub(harness, text, 0);
                walk.Notes.Add($"prerequisite {text} counted as done");
            }
        }

        private static int _stubIds = 900_000;

        /// <summary>A finished quest in the player's list (a prerequisite the walk does not play).</summary>
        private static void FinishedStub(QuestHarness harness, string name, int id)
        {
            var row = new DbDataQuest { ID = id != 0 ? id : _stubIds++, Name = name, StartType = (byte)DataQuest.eStartType.Standard, StepType = "4" };
            var record = new DbCharacterXDataQuest(harness.Player.QuestPlayerID, row.ID) { Step = 0, Count = 1 };
            harness.Player.AddFinishedQuest(new DataQuest(harness.Player, row, record));
        }

        // Row helpers

        private static int Count(QuestHarness harness, string id) =>
            harness.Player.Inventory.GetItemRange(eInventorySlot.FirstBackpack, eInventorySlot.LastBackpack)
                .Where(i => string.Equals(i.Id_nb, id, StringComparison.OrdinalIgnoreCase)).Sum(i => Math.Max(1, i.Count));

        /// <summary>
        /// Whether an exception is the harness's own failure rather than the quest code's: a server logger that has no
        /// queue in the harness (loggers made before logging is set up throw on an error; LogCapture gives the quest
        /// code's loggers one). Returns what to fix, or null.
        /// </summary>
        public static string HarnessProblemIn(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
            {
                if (e is not NullReferenceException || e.StackTrace == null)
                    continue;
                string[] frames = e.StackTrace.Split('\n').Select(f => f.Trim()).Where(f => f.StartsWith("at ", StringComparison.Ordinal)).ToArray();
                if (frames.Length == 0 || !frames[0].StartsWith("at DOL.Logging.Logger.", StringComparison.Ordinal))
                    continue;
                string caller = frames.FirstOrDefault(f => !f.StartsWith("at DOL.Logging.", StringComparison.Ordinal)) ?? "an unknown caller";
                int paren = caller.IndexOf('(');
                return $"a server logger has no queue in the harness ({(paren > 0 ? caller[..paren] : caller)}): give its type's logger one in LogCapture";
            }
            return null;
        }

        public static string[] Split(string value) => string.IsNullOrEmpty(value) ? Array.Empty<string>() : value.Split('|');

        public static int? FirstClass(string allowed)
        {
            foreach (string part in Split(allowed))
                if (int.TryParse(part.Trim(), out int id) && id > 0)
                    return id;
            return null;
        }

        private static short RaceFor(ClassicQuests.QuestInfo classic)
        {
            string race = classic?.Races?.FirstOrDefault();
            if (race == null)
                return 0;
            foreach (eRace value in Enum.GetValues<eRace>())
                if (string.Equals(value.ToString().Replace("_", ""), race.Replace(" ", ""), StringComparison.OrdinalIgnoreCase))
                    return (short)value;
            return 0;
        }

        private static ClassicQuests.QuestInfo ClassicInfo(int id)
        {
            var config = (ClassicQuests.Config)typeof(ClassicQuests).GetField("_config", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            return config?.Quests != null && config.Quests.TryGetValue(id, out ClassicQuests.QuestInfo info) ? info : null;
        }

        /// <summary>How many kills or searches a step with a drop chance ("item;25") gets: enough that a drop that works is missed
        /// once in a million steps (at 99.9%, the world report's ~1,000 chance steps walked three ways missed two or three by luck).</summary>
        public static int KillTries(string stepItem, int max)
        {
            string[] parts = (stepItem ?? string.Empty).Split(';');
            if (parts.Length < 2 || !int.TryParse(parts[1], out int chance) || chance >= 100)
                return 1;
            if (chance <= 0)
                return 1;
            return Math.Min(max, (int)Math.Ceiling(Math.Log(0.000001) / Math.Log(1 - chance / 100.0)));
        }

        /// <summary>Whether SourceName defines a SEARCH area for this step ("SEARCH;step;text;region;x;y;radius;seconds").</summary>
        public static bool HasSearchArea(string sourceName, int step)
        {
            foreach (string entry in Split(sourceName))
            {
                string[] parts = entry.Split(';');
                if (parts.Length >= 8 && parts[0].Trim().StartsWith("SEARCH", StringComparison.OrdinalIgnoreCase) &&
                    !parts[0].Trim().Equals("SEARCHSTART", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(parts[1], out int at) && at == step)
                    return true;
            }
            return false;
        }
    }
}
