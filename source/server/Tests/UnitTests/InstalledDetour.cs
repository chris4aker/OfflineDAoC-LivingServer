using System;
using System.IO;
using System.Runtime.InteropServices;
using DOL.GS;

namespace DOL.UnitTests
{
    /// <summary>
    /// The installed-navmesh test classes' Detour library (issue #687). NativeLibrary takes one DllImport resolver per
    /// assembly, and each class used to set its own: run together in the navmesh job, every class after the first failed
    /// with "A resolver is already set for the assembly". This points the server's "lib/Detour" import at
    /// root/lib/Detour.dll once per test process; a resolver already set (another class's, for the same set) is kept.
    /// </summary>
    internal static class InstalledDetour
    {
        private static readonly object Gate = new();
        private static bool _set;

        public static void Use(string root)
        {
            lock (Gate)
            {
                if (_set)
                    return;
                string path = Path.Combine(root, "lib", "Detour.dll");
                try
                {
                    NativeLibrary.SetDllImportResolver(typeof(LocalPathfindingMgr).Assembly,
                        (name, assembly, search) => name == "lib/Detour" ? NativeLibrary.Load(path) : IntPtr.Zero);
                }
                catch (InvalidOperationException)
                {
                    // set before this helper ran (a class that still sets its own): that one stays
                }
                _set = true;
            }
        }
    }
}
