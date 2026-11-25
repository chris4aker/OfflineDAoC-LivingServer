using System.Collections.Generic;
using System.Threading;

namespace DOL.GS.PropertyCalc
{
    public sealed class PropertyIndexer : IPropertyIndexer
    {
        // Issue #540: a spin flag instead of a System.Threading.Lock. On .NET 10 a Lock makes a Windows event the first
        // time a thread has to wait for it and keeps that handle for the Lock's life. Every living carries seven of these
        // tables, and the 2026-10-22 lock census on the owner's PC found them to be two thirds of the Locks holding an
        // event, while the process's handles kept climbing (1.2.2: 2,134 to 5,260 in 4.5 hours, threads flat). Each
        // critical section here is one dictionary lookup or write, so a short spin never needs a kernel object.
        private int _busy;
        private readonly Dictionary<eProperty, int> _properties = new();

        public void Clear()
        {
            Enter();
            try
            {
                _properties.Clear();
            }
            finally
            {
                Exit();
            }
        }

        public int this[eProperty index]
        {
            get
            {
                Enter();
                try
                {
                    return _properties.TryGetValue(index, out int value) ? value : 0;
                }
                finally
                {
                    Exit();
                }
            }
            set
            {
                Enter();
                try
                {
                    if (value == 0)
                        _properties.Remove(index);
                    else
                        _properties[index] = value;
                }
                finally
                {
                    Exit();
                }
            }
        }

        private void Enter()
        {
            if (Interlocked.CompareExchange(ref _busy, 1, 0) == 0)
                return;
            SpinWait spin = default;
            do
                spin.SpinOnce();
            while (Volatile.Read(ref _busy) != 0 || Interlocked.CompareExchange(ref _busy, 1, 0) != 0);
        }

        private void Exit() => Volatile.Write(ref _busy, 0);
    }
}
