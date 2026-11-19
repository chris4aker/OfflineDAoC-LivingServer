using System;
using System.Text;
using System.Threading;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>A reason for a gamebot to visit a town service at a task break (WP4 stock to sell, WP6 a gear gap, WP7 a repair).</summary>
    public interface IBotTownVisitNeed
    {
        string Name { get; }
        /// <summary>OR'ed into the between-task plan's unload flag ('I') at a natural task break.</summary>
        bool WantsVisit(GameBot bot);
        /// <summary>
        /// Consulted after today's needs in AutonomousBotEconomy's service choice: Vendor, RealmExchange or SealMerchant,
        /// or null. The choice is cached per bot: call AutonomousBotEconomy.MarkEconomyChanged(botId) when the answer changes.
        /// </summary>
        eWorldServiceKind? NeededService(GameBot bot);
    }

    /// <summary>What a service step did: the status the bot shows, the pause before the next step, the progress it made.</summary>
    public readonly record struct BotServiceResult(string Status, string Detail, int CooldownMs, eAutonomousProgressKind Progress);

    /// <summary>
    /// One more thing a gamebot does at a merchant or at the Realm Exchange it has reached (WP5 lists stock and hands over
    /// orders, WP6 buys a template piece, WP7 repairs and buys potions). Merchant steps run after the sales, the upgrades
    /// and the seal and siege purchases, before the cosmetics; Exchange steps after the listing and before the purchase.
    /// </summary>
    public interface IBotServiceStep
    {
        string Name { get; }
        /// <summary>Lower runs first.</summary>
        int Order { get; }
        /// <summary>Vendor (any coin merchant) or RealmExchange.</summary>
        eWorldServiceKind Kind { get; }
        /// <summary>True when the step did something this turn (then no later step runs this turn).</summary>
        bool TryRun(GameBot bot, GameNPC service, long now, out BotServiceResult result);
    }

    /// <summary>
    /// An errand that becomes part of a gamebot's task break, with a letter in the between-task assignment id beside
    /// today's T (train), I (inventory) and D (downtime). DividedRealms' craft trip is the first ('C', 93c25b69): the
    /// assignment then reads "between-pve-services-TCID-…". Its executor runs in the world controller through the
    /// partial method <c>AutonomousWorldBotController.RunBreakActivities</c>, which WP4 implements in its own partial file
    /// (DR's AutonomousWorldBotController.Crafting.cs), between training and the inventory stop.
    /// </summary>
    public interface IBotBreakActivity
    {
        string Name { get; }
        /// <summary>The assignment letter: a capital letter other than T, I and D, unique among activities.</summary>
        char Flag { get; }
        /// <summary>The activity also wants the inventory stop afterwards (DR: what a craft trip made is posted there).</summary>
        bool ImpliesInventoryStop { get; }
        /// <summary>The progress text ("make a trip to craft; ").</summary>
        string Describe { get; }
        /// <summary>Whether this task break includes the activity (an ungrouped bot at a natural task break).</summary>
        bool Wants(GameBot bot, Random random);
        /// <summary>Whether the activity of the bot's current break is over (the break may then close).</summary>
        bool IsDone(GameBot bot);
    }

    /// <summary>Where a task-break hook is called.</summary>
    public enum TaskBreakPoint
    {
        /// <summary>A task ended and the break is being planned (also for a grouped bot, before the group check).</summary>
        Begin,
        /// <summary>The break is closing (CompleteBetweenTaskServices).</summary>
        End,
    }

    /// <summary>Work done at every natural task break (DR's AutonomousBotCraftOrders.AtTaskBreak: deliver and commission).</summary>
    public interface IBotTaskBreakHook
    {
        string Name { get; }
        void AtTaskBreak(GameBot bot, TaskBreakPoint point);
    }

    /// <summary>
    /// The registries behind the task-break and town-service seams WP0 put into AutonomousObjectiveAssignments,
    /// AutonomousBotEconomy, BotBrain and the world controller. With nothing registered every dispatcher returns at
    /// once and draws no random number, so today's behaviour is unchanged (UT_EconomySeamsDefaults). Registries are
    /// copy-on-write arrays: registering is rare, reading from parallel brain turns takes no lock. Only autonomous
    /// gamebots reach these call sites; companions and players never do.
    /// </summary>
    public static class BotServiceSeams
    {
        private static IBotTownVisitNeed[] _needs = Array.Empty<IBotTownVisitNeed>();
        private static IBotServiceStep[] _steps = Array.Empty<IBotServiceStep>();
        private static IBotBreakActivity[] _activities = Array.Empty<IBotBreakActivity>();
        private static IBotTaskBreakHook[] _hooks = Array.Empty<IBotTaskBreakHook>();

        public static void Register(IBotTownVisitNeed need) => Add(ref _needs, need);

        public static void Register(IBotServiceStep step)
        {
            if (step == null)
                return;
            Add(ref _steps, step);
            // Keep the steps in order (stable: equal orders keep their registration order).
            IBotServiceStep[] current, sorted;
            do
            {
                current = Volatile.Read(ref _steps);
                sorted = (IBotServiceStep[])current.Clone();
                var keys = new long[sorted.Length];
                for (int i = 0; i < sorted.Length; i++)
                    keys[i] = ((long)sorted[i].Order << 20) + i;
                Array.Sort(keys, sorted);
            }
            while (Interlocked.CompareExchange(ref _steps, sorted, current) != current);
        }

        public static void Register(IBotBreakActivity activity)
        {
            if (activity == null)
                return;
            if (activity.Flag is 'T' or 'I' or 'D' or '-' || !char.IsUpper(activity.Flag))
                throw new ArgumentException($"Break activity {activity.Name}: flag '{activity.Flag}' must be a capital letter other than T, I and D.");
            foreach (IBotBreakActivity other in Volatile.Read(ref _activities))
                if (other.Flag == activity.Flag && !ReferenceEquals(other, activity))
                    throw new ArgumentException($"Break activity {activity.Name}: flag '{activity.Flag}' is taken by {other.Name}.");
            Add(ref _activities, activity);
        }

        public static void Register(IBotTaskBreakHook hook) => Add(ref _hooks, hook);

        /// <summary>Tests: forget every registration.</summary>
        public static void ResetForTests()
        {
            Volatile.Write(ref _needs, Array.Empty<IBotTownVisitNeed>());
            Volatile.Write(ref _steps, Array.Empty<IBotServiceStep>());
            Volatile.Write(ref _activities, Array.Empty<IBotBreakActivity>());
            Volatile.Write(ref _hooks, Array.Empty<IBotTaskBreakHook>());
        }

        public static int RegisteredCount =>
            Volatile.Read(ref _needs).Length + Volatile.Read(ref _steps).Length +
            Volatile.Read(ref _activities).Length + Volatile.Read(ref _hooks).Length;

        /// <summary>Any registered need wants a town visit now.</summary>
        public static bool WantsVisit(GameBot bot)
        {
            foreach (IBotTownVisitNeed need in Volatile.Read(ref _needs))
            {
                try
                {
                    if (need.WantsVisit(bot))
                        return true;
                }
                catch { } // one package's failure never stops a bot's break
            }
            return false;
        }

        /// <summary>The first registered need's service, or null.</summary>
        public static eWorldServiceKind? NeededService(GameBot bot)
        {
            foreach (IBotTownVisitNeed need in Volatile.Read(ref _needs))
            {
                try
                {
                    if (need.NeededService(bot) is eWorldServiceKind kind)
                        return kind;
                }
                catch { }
            }
            return null;
        }

        /// <summary>Runs the steps of this kind in order until one does something.</summary>
        public static bool TryRunSteps(eWorldServiceKind kind, GameBot bot, GameNPC service, long now, out BotServiceResult result)
        {
            result = default;
            foreach (IBotServiceStep step in Volatile.Read(ref _steps))
            {
                if (step.Kind != kind)
                    continue;
                try
                {
                    if (step.TryRun(bot, service, now, out result))
                        return true;
                }
                catch { }
            }
            result = default;
            return false;
        }

        /// <summary>
        /// The letters of the activities this break includes, in registration order ("" with none), whether one of them
        /// wants the inventory stop, and their progress text.
        /// </summary>
        public static string RollBreakActivities(GameBot bot, Random random, out bool inventoryStop, out string describe)
        {
            inventoryStop = false;
            describe = string.Empty;
            IBotBreakActivity[] activities = Volatile.Read(ref _activities);
            if (activities.Length == 0)
                return string.Empty;
            var flags = new StringBuilder();
            var text = new StringBuilder();
            foreach (IBotBreakActivity activity in activities)
            {
                bool wants;
                try { wants = activity.Wants(bot, random); }
                catch { wants = false; }
                if (!wants)
                    continue;
                flags.Append(activity.Flag);
                text.Append(activity.Describe);
                inventoryStop |= activity.ImpliesInventoryStop;
            }
            describe = text.ToString();
            return flags.ToString();
        }

        /// <summary>Whether every activity flagged in the bot's current break is over (true with none).</summary>
        public static bool BreakActivitiesDone(GameBot bot)
        {
            foreach (IBotBreakActivity activity in Volatile.Read(ref _activities))
            {
                if (!AutonomousObjectiveAssignments.HasBetweenTaskFlag(bot?.PersistentRecord, activity.Flag))
                    continue;
                try
                {
                    if (!activity.IsDone(bot))
                        return false;
                }
                catch { }
            }
            return true;
        }

        public static void AtTaskBreak(GameBot bot, TaskBreakPoint point)
        {
            foreach (IBotTaskBreakHook hook in Volatile.Read(ref _hooks))
            {
                try { hook.AtTaskBreak(bot, point); }
                catch { }
            }
        }

        private static void Add<T>(ref T[] registry, T item) where T : class
        {
            if (item == null)
                return;
            T[] current, next;
            do
            {
                current = Volatile.Read(ref registry);
                if (Array.IndexOf(current, item) >= 0)
                    return;
                next = new T[current.Length + 1];
                current.CopyTo(next, 0);
                next[current.Length] = item;
            }
            while (Interlocked.CompareExchange(ref registry, next, current) != current);
        }
    }
}
