using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using DOL.AI;
using DOL.Logging;
using DOL.Timing;
using ECS.Debug;

namespace DOL.GS
{
    public sealed class NpcService : GameServiceBase
    {
        private static readonly Logger log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        private List<ABrain> _list;

        public static NpcService Instance { get; }

        static NpcService()
        {
            Instance = new();
        }

        public override void Tick()
        {
            ProcessPostedActionsParallel();
            // Living Server 1.2, AI_PROFILE: the serial planning below runs on the game loop before any brain thinks;
            // its time and each step's are measured (the idea of one scope around it is lometur's fleet scope, #137).
            long serialStart = AiProfile.Timestamp();
            using (AiProfile.Scope(AiSystem.SerialBrainRegistry))
                AutonomousBotRegistry.PrepareBrainTick();
            using (AiProfile.Scope(AiSystem.SerialRvrPlanning))
                AutonomousWorldBotController.PrepareRvrPlanningTick();
            using (AiProfile.Scope(AiSystem.SerialCoordinator))
                AutonomousBotGroupCoordinator.PrepareCoordinatorTick();
            using (AiProfile.Scope(AiSystem.SerialCampPlanning))
                AutonomousWorldBotController.PrepareCampPlanningTick();
            AiProfile.EndSerial(serialStart);
            int lastValidIndex;

            try
            {
                _list = ServiceObjectStore.UpdateAndGetAll<ABrain>(ServiceObjectType.Brain, out lastValidIndex);
            }
            catch (Exception e)
            {
                if (log.IsErrorEnabled)
                    log.Error($"{nameof(ServiceObjectStore.UpdateAndGetAll)} failed. Skipping this tick.", e);

                return;
            }

            long parallelStart = AiProfile.Timestamp();
            GameLoop.ExecuteForEach(_list, lastValidIndex + 1, TickInternal);
            AiProfile.EndParallel(parallelStart);

            if (Diagnostics.CheckServiceObjectCount)
                Diagnostics.PrintServiceObjectCount(ServiceName, ref EntityCount, _list.Count);
        }

        private static void TickInternal(ABrain brain)
        {
            try
            {
                if (Diagnostics.CheckServiceObjectCount)
                    Interlocked.Increment(ref Instance.EntityCount);

                GameNPC npc = brain.Body;

                if (GameServiceUtils.ShouldTick(brain.NextThinkTick))
                {
                    if (!brain.IsActive)
                    {
                        brain.Stop();
                        return;
                    }

                    long startTick = MonotonicTime.NowMs;
                    NavQueryProfile.Reset();
                    // AI_PROFILE: every turn is charged to the kind of its body (gamebot, companion, pet or NPC).
                    using (AiProfile.Enabled ? AiProfile.Scope(AiProfile.ThinkKind(brain, npc)) : default)
                        brain.Think();
                    long stopTick = MonotonicTime.NowMs;

                    long elapsed = stopTick - startTick;
                    if (elapsed > Diagnostics.LongTickThreshold)
                    {
                        // Every slow turn feeds the once-a-minute SERVER_STALL summary;
                        // only big ones still get their own warning line.
                        AiTurnStallMonitor.Record(npc.Name, brain.GetType().Name, elapsed);
                        if (AiTurnStallMonitor.ShouldLogIndividually(elapsed))
                            log.Warn($"Long {Instance.ServiceName}.{nameof(Tick)} for {npc.Name}({npc.ObjectID}) Interval: {brain.ThinkInterval} BrainType: {brain.GetType()} Time: {elapsed}ms{NavQueryProfile.Summary()}{(npc is GameBot slowBot ? $" activity=\"{slowBot.PersistentRecord?.Activity}\" goal=\"{slowBot.PersistentRecord?.ObjectiveKind}\" phase=\"{slowBot.PersistentRecord?.ObjectivePhase}\"" : string.Empty)}");
                    }

                    brain.NextThinkTick = GameLoop.GameLoopTime + brain.ThinkInterval;
                }
            }
            catch (Exception e)
            {
                GameServiceUtils.HandleServiceException(e, Instance.ServiceName, brain, brain.Body);
            }
        }
    }
}
