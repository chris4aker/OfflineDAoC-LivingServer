using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// One package's part of the economy rehearsal (Crafting 2.0 WP10). Each package adds its own adapter
    /// <c>Tests/Rehearsal/Adapters/WPn*.cs</c> implementing this; <see cref="RehearsalModules"/> finds it by reflection, so
    /// no package edits a shared rehearsal file. An adapter drives the package's <b>pure</b> code (rules, planners, index,
    /// valuation) against the in-memory world; it never touches GameServer.Database, a GameBot or an NPC.
    /// </summary>
    /// <remarks>
    /// The world calls a module at fixed points, in the modules' order: <see cref="Attach"/> once (register hooks on the
    /// world: a gear policy, an index, a flow observer), <see cref="OnTaskBreak"/> for every agent at the end of a task
    /// (the natural task break: selling, listing, buying and town visits happen only here), <see cref="OnMinute"/> once a
    /// world minute after the agents, <see cref="OnHour"/> before each hourly ledger row closes, and <see cref="Finish"/>
    /// to add the package's metrics and lines to the report. Every coin move goes through the world's Earn, Pay or
    /// Transfer so that the gate "the flows equal the change in supply" holds; a module that changes a purse directly
    /// fails it.
    /// </remarks>
    public interface IRehearsalModule
    {
        /// <summary>A stable id, "WPn.Name" (another module can supersede it by this id).</summary>
        string Id { get; }

        /// <summary>The work package, "WP0".."WP10": the order modules run in.</summary>
        string Package { get; }

        /// <summary>One line for the ledger: what the module runs.</summary>
        string Describe { get; }

        /// <summary>Ids of modules this one replaces (a package's real code superseding a stand-in).</summary>
        IReadOnlyCollection<string> Supersedes { get; }

        /// <summary>The settings (CraftingEconomyProperties keys) this module honours, for the "not built yet" report.</summary>
        IReadOnlyCollection<string> ReadsSettings { get; }

        bool AppliesTo(RehearsalScenario scenario);
        void Attach(RehearsalWorld world);
        void OnTaskBreak(RehearsalWorld world, RehearsalAgent agent);
        void OnMinute(RehearsalWorld world);
        void OnHour(RehearsalWorld world);
        void Finish(RehearsalWorld world, RehearsalReport report);
    }

    /// <summary>A module with no-op defaults; an adapter overrides what it needs.</summary>
    public abstract class RehearsalModule : IRehearsalModule
    {
        public abstract string Id { get; }
        public abstract string Package { get; }
        public abstract string Describe { get; }
        public virtual IReadOnlyCollection<string> Supersedes => Array.Empty<string>();
        public virtual IReadOnlyCollection<string> ReadsSettings => Array.Empty<string>();
        public virtual bool AppliesTo(RehearsalScenario scenario) => true;
        public virtual void Attach(RehearsalWorld world) { }
        public virtual void OnTaskBreak(RehearsalWorld world, RehearsalAgent agent) { }
        public virtual void OnMinute(RehearsalWorld world) { }
        public virtual void OnHour(RehearsalWorld world) { }
        public virtual void Finish(RehearsalWorld world, RehearsalReport report) { }
    }

    /// <summary>Finds the rehearsal modules by reflection and picks the ones a scenario runs.</summary>
    public static class RehearsalModules
    {
        /// <summary>Every concrete <see cref="IRehearsalModule"/> with a public parameterless constructor in the assembly.</summary>
        public static IReadOnlyList<Type> Discover(Assembly assembly = null) =>
            (assembly ?? typeof(RehearsalModules).Assembly).GetTypes()
                .Where(type => typeof(IRehearsalModule).IsAssignableFrom(type) && !type.IsAbstract && !type.IsInterface &&
                               type.GetConstructor(Type.EmptyTypes) != null)
                .OrderBy(type => type.FullName, StringComparer.Ordinal)
                .ToArray();

        public static IReadOnlyList<IRehearsalModule> CreateAll(Assembly assembly = null) =>
            Discover(assembly).Select(type => (IRehearsalModule)Activator.CreateInstance(type)).ToArray();

        /// <summary>
        /// The modules a scenario runs, in order: those that apply, less any another applying module supersedes, sorted by
        /// package number then id (so WP2's ledger observes before WP5's market trades, and a run is repeatable).
        /// </summary>
        public static IReadOnlyList<IRehearsalModule> For(RehearsalScenario scenario, IEnumerable<IRehearsalModule> candidates = null)
        {
            IRehearsalModule[] applying = (candidates ?? CreateAll()).Where(module => module.AppliesTo(scenario)).ToArray();
            var superseded = new HashSet<string>(applying.SelectMany(module => module.Supersedes ?? Array.Empty<string>()),
                StringComparer.OrdinalIgnoreCase);
            return applying.Where(module => !superseded.Contains(module.Id))
                .OrderBy(module => PackageNumber(module.Package)).ThenBy(module => module.Id, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>"WP10" is 10; anything else sorts last.</summary>
        public static int PackageNumber(string package) =>
            package != null && package.StartsWith("WP", StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(package.AsSpan(2), out int number) ? number : int.MaxValue;
    }
}
