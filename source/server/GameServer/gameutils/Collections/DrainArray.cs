using System;
using System.Collections.Concurrent;
using System.Threading;

namespace DOL.GS
{
    // A lock-free, multi-producer single-consumer collection optimized for high-throughput concurrent additions.
    // Allows concurrent, lock-free add operations from multiple producer threads.
    // Designed for a single consumer to drain items; draining and adding must not occur simultaneously.
    // If the internal buffer overflows, excess items are temporarily stored in a concurrent queue and the buffer is automatically resized (never shrinks).
    public class DrainArray<T>
    {
        private T[] _buffer;
        private ConcurrentQueue<T> _overflowQueue = new();
        public int _writeIndex;
        public int _adding;
        public bool _draining;
        public bool _overflowed;
        private int _drainingThread;

        /// <summary>1.2.2: true when this thread is the one draining (an add from inside the drain can never succeed).</summary>
        public bool IsDrainingOnThisThread =>
            Volatile.Read(ref _draining) && Volatile.Read(ref _drainingThread) == Environment.CurrentManagedThreadId;

        public bool Any => Volatile.Read(ref _writeIndex) > 0; // Not accurate.

        public DrainArray(int initialCapacity = 1024)
        {
            _buffer = new T[initialCapacity];
            _writeIndex = 0;
        }

        public void Add(T item)
        {
            if (!TryAdd(item))
                throw new InvalidOperationException($"Cannot {nameof(Add)} while {nameof(DrainTo)} is in progress.");
        }

        /// <summary>
        /// <see cref="Add"/> that reports a drain in progress instead of throwing: false, and the item was not added.
        /// For producers that are not tied to the consumer's schedule and can wait for the drain to end.
        /// </summary>
        public bool TryAdd(T item)
        {
            Interlocked.Increment(ref _adding);

            try
            {
                if (Volatile.Read(ref _draining))
                    return false;

                int index = Interlocked.Increment(ref _writeIndex) - 1;

                if (index < _buffer.Length)
                    _buffer[index] = item;
                else
                {
                    _overflowQueue.Enqueue(item);
                    _overflowed = true;
                }

                return true;
            }
            finally
            {
                Interlocked.Decrement(ref _adding);
            }
        }

        public void DrainTo(Action<T> action)
        {
            DrainTo(static (item, act) => act(item), action);
        }

        public void DrainTo<TState>(Action<T, TState> action, TState state)
        {
            if (Interlocked.Exchange(ref _draining, true) != false)
                throw new InvalidOperationException($"Concurrent {nameof(DrainTo)} detected.");

            Volatile.Write(ref _drainingThread, Environment.CurrentManagedThreadId);
            try
            {
                if (Volatile.Read(ref _adding) > 0)
                {
                    SpinWait spinner = new();

                    while (Volatile.Read(ref _adding) > 0)
                        spinner.SpinOnce(-1);
                }

                int count = Math.Min(_buffer.Length, Interlocked.Exchange(ref _writeIndex, 0));

                if (count == 0)
                    return;

                for (int i = 0; i < count; i++)
                    action(_buffer[i], state);

                if (_overflowed)
                {
                    int overflowCount = 0;

                    while (_overflowQueue.TryDequeue(out T result))
                    {
                        overflowCount++;
                        action(result, state);
                    }

                    int newSize = Math.Max(_buffer.Length * 2, _buffer.Length + overflowCount + 1024);
                    _buffer = new T[newSize];
                    _overflowed = false;
                    return;
                }

                Array.Clear(_buffer, 0, count);
            }
            finally
            {
                Volatile.Write(ref _drainingThread, 0);
                Volatile.Write(ref _draining, false);
            }
        }
    }
}
