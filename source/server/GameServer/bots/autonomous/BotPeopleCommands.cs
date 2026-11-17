using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace DOL.GS;

/// <summary>
/// The GM test hooks of "Bots as people" (docs/BOTS-AS-PEOPLE-1.2.md, "Log tags"): /lstest skill | lost | ask | milestone |
/// wall. WP0 declares the subcommands; each package registers its handler (at server start or in a static constructor)
/// and the command runs it, so a package never edits LivingServerTestCommand.cs. A subcommand with no handler says which
/// package brings it. A handler makes a rare moment due through the feature's own code; it never fakes one.
/// </summary>
public static class BotPeopleTest
{
    /// <summary>What a handler gets: the GM, the target gamebot (the GM's target, or null), and the words after the subcommand.</summary>
    public delegate string Handler(GamePlayer gm, GameBot target, string[] words);

    /// <summary>The subcommands, the package that fills each, and what it does (the /lstest usage lines).</summary>
    public static readonly (string Name, string Package, string Usage)[] Subcommands =
    [
        ("skill", "work package 1", "/lstest skill - your target gamebot's skill profile: tier, facets, experience"),
        ("lost", "work package 4", "/lstest lost - your target's next trip to a place it does not know takes a wrong road"),
        ("ask", "work package 4", "/lstest ask - your target asks the way now (in /faction or /say)"),
        ("milestone", "work package 6", "/lstest milestone - your target's milestone arc reaches its next talk point"),
        ("wall", "work package 5", "/lstest wall - bots waiting at the nearest keep your realm holds take its walls"),
    ];

    private static readonly ConcurrentDictionary<string, Handler> Handlers = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsSubcommand(string name) => Subcommands.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Registers (or replaces) the handler of one of <see cref="Subcommands"/>.</summary>
    public static void Register(string name, Handler handler)
    {
        if (!IsSubcommand(name))
            throw new ArgumentException($"/lstest {name} is not a Bots as people subcommand", nameof(name));
        Handlers[name] = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    public static bool Unregister(string name) => Handlers.TryRemove(name, out _);

    public static bool IsBuilt(string name) => Handlers.ContainsKey(name);

    /// <summary>The answer to "/lstest &lt;name&gt; ...": the handler's, or which package brings it.</summary>
    public static string Run(string name, GamePlayer gm, GameBot target, string[] words)
    {
        var sub = Subcommands.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (sub.Name == null)
            return $"/lstest {name} is not a Bots as people test.";
        if (!Handlers.TryGetValue(sub.Name, out Handler handler))
            return $"/lstest {sub.Name} is not built yet: it comes with Bots as people {sub.Package} (docs/BOTS-AS-PEOPLE-1.2.md).";
        try
        {
            return handler(gm, target, words ?? []) ?? "Done.";
        }
        catch (Exception exception)
        {
            return $"/lstest {sub.Name} failed: {exception.Message}";
        }
    }

    /// <summary>The /lstest list line for Bots as people: each switch and which tests are built.</summary>
    public static string ListLine(BotPeopleSwitches s) =>
        $"  bots as people: skill {OnOff(s.SkillProfiles)}, places {OnOff(s.PlaceKnowledge)}, movement {OnOff(s.MovementHabits)}, " +
        $"milestones {OnOff(s.Milestones)}; tests built: " +
        (Subcommands.Any(sub => IsBuilt(sub.Name)) ? string.Join(" ", Subcommands.Where(sub => IsBuilt(sub.Name)).Select(sub => sub.Name)) : "none yet");

    private static string OnOff(bool on) => on ? "on" : "OFF";
}

/// <summary>
/// The /lswhy lines of "Bots as people": "/lswhy skill [name]" and "/lswhy way [name]" (docs/BOTS-AS-PEOPLE-1.2.md, "Log
/// tags"). WP0 shows the scope (whether each rule may act on the bot now, and why not); WP1 (skill) and WP3/WP4 (way)
/// register a provider for the rest. Read only: a provider changes nothing.
/// </summary>
public static class BotPeopleWhy
{
    public enum Topic { Skill, Way }

    /// <summary>A provider's lines for one gamebot (it may read the store and the bot; it never changes either).</summary>
    public delegate IEnumerable<string> Provider(GameBot bot);

    private static readonly ConcurrentDictionary<Topic, Provider> Providers = new();

    public static void Register(Topic topic, Provider provider) =>
        Providers[topic] = provider ?? throw new ArgumentNullException(nameof(provider));

    public static bool Unregister(Topic topic) => Providers.TryRemove(topic, out _);

    public static bool TryTopic(string word, out Topic topic)
    {
        topic = word?.ToLowerInvariant() == "way" ? Topic.Way : Topic.Skill;
        return word?.ToLowerInvariant() is "skill" or "way";
    }

    /// <summary>The lines for one gamebot: the scope line first, then the provider's (or which package brings them).</summary>
    public static List<string> Lines(Topic topic, GameBot bot)
    {
        var lines = new List<string>();
        BotPersonKey person = BotPersonKey.Of(bot);
        if (person.IsNone)
        {
            lines.Add($"{bot?.Name ?? "That bot"} is not an autonomous gamebot with a person: Bots as people never touches it.");
            return lines;
        }
        BotPeopleRule rule = topic == Topic.Skill ? BotPeopleRule.Mistakes : BotPeopleRule.WayFinding;
        lines.Add(ScopeLine(person, BotPeopleScope.Refusal(bot, BotPeopleRule.Record), BotPeopleScope.Refusal(bot, rule), rule,
            BotPeopleScope.FactsOf(bot).InPlayerGroup));
        if (!Providers.TryGetValue(topic, out Provider provider))
        {
            lines.Add(topic == Topic.Skill
                ? "  Skill profiles are not built yet (Bots as people work package 1)."
                : "  Place knowledge and way finding are not built yet (Bots as people work packages 3 and 4).");
            return lines;
        }
        try
        {
            lines.AddRange((provider(bot) ?? []).Select(line => "  " + line));
        }
        catch (Exception exception)
        {
            lines.Add($"  (could not read: {exception.Message})");
        }
        return lines;
    }

    /// <summary>"Person c:12: records yes; mistakes now: no (realm_raid)". Pure.</summary>
    public static string ScopeLine(BotPersonKey person, BotPeopleRefusal record, BotPeopleRefusal behaviour, BotPeopleRule rule,
        bool inPlayerGroup) =>
        $"Person {person}: records {(record == BotPeopleRefusal.None ? "yes" : "no (" + BotPeopleScope.RefusalName(record) + ")")}; " +
        $"{BotPeopleScope.RuleName(rule)} now: " +
        (behaviour == BotPeopleRefusal.None
            ? "yes" + (rule == BotPeopleRule.Mistakes && inPlayerGroup ? " (spared: a player is in its group)" : "")
            : "no (" + BotPeopleScope.RefusalName(behaviour) + ")");
}
