using System;
using System.Threading;

namespace DOL.GS.CraftingEconomy
{
    /// <summary>A package's stored state (DividedRealms' craft orders: Load, Pulse, Orders.Flush in AutonomousLivingServer).</summary>
    public interface IEconomyStore
    {
        string Name { get; }
        /// <summary>At server start, from AutonomousLivingServer.OnServerStarted (errors are logged, never thrown).</summary>
        void Load();
        /// <summary>Once a second from the Living Server pulse (game loop): keep it cheap.</summary>
        void Pulse(long now);
        /// <summary>From the Living Server flush timer and at shutdown (<paramref name="wait"/> = true).</summary>
        void Flush(bool wait);
    }

    /// <summary>
    /// The lifecycle seam WP0 put into AutonomousLivingServer, so stores of later packages load, pulse and flush with
    /// the Living Server's own tables without editing it. Nothing is registered in WP0.
    /// </summary>
    public static class EconomyLifecycle
    {
        private static IEconomyStore[] _stores = Array.Empty<IEconomyStore>();

        public static void Register(IEconomyStore store)
        {
            if (store == null)
                return;
            IEconomyStore[] current, next;
            do
            {
                current = Volatile.Read(ref _stores);
                if (Array.IndexOf(current, store) >= 0)
                    return;
                next = new IEconomyStore[current.Length + 1];
                current.CopyTo(next, 0);
                next[current.Length] = store;
            }
            while (Interlocked.CompareExchange(ref _stores, next, current) != current);
        }

        public static int Count => Volatile.Read(ref _stores).Length;

        public static void ResetForTests() => Volatile.Write(ref _stores, Array.Empty<IEconomyStore>());

        /// <param name="load">The Living Server's own loader (it logs a failure under the store's name).</param>
        public static void LoadAll(Action<string, Action> load)
        {
            foreach (IEconomyStore store in Volatile.Read(ref _stores))
                load(store.Name, store.Load);
        }

        public static void PulseAll(long now)
        {
            foreach (IEconomyStore store in Volatile.Read(ref _stores))
            {
                try { store.Pulse(now); }
                catch (Exception exception) { Log(store, "pulse", exception); }
            }
        }

        public static void FlushAll(bool wait)
        {
            foreach (IEconomyStore store in Volatile.Read(ref _stores))
            {
                try { store.Flush(wait); }
                catch (Exception exception) { Log(store, "flush", exception); }
            }
        }

        private static void Log(IEconomyStore store, string what, Exception exception)
        {
            try { DOL.Logging.LoggerManager.Create(typeof(EconomyLifecycle)).Error($"Economy store {store.Name}: {what} failed", exception); }
            catch { }
        }
    }
}
