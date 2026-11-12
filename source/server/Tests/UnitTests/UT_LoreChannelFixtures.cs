using System.Collections.Generic;
using DOL.GS.LoreChannels;

namespace DOL.UnitTests;

/// <summary>A small lore bible for the lore channel tests: one place per realm, one neutral, a boss each.</summary>
internal static class LoreChannelFixtures
{
    public static LoreLocation Place(string id, string name, string realm, int region, string intro, (string, string)[] topics,
        string[] bosses, string bookTitle = null, LoreArea[] areas = null, string summary = null) =>
        new(id, name, realm, [region], summary ?? $"{name} in a sentence.", $"The story of {name}.", $"What players say about {name}.",
            new LoreLoremasterText(intro, Topics(topics)),
            bookTitle == null ? null : new LoreBookText(bookTitle, [$"Page one of {name}.", $"Page two of {name}."]),
            bosses, areas ?? []);

    private static List<KeyValuePair<string, string>> Topics((string Key, string Text)[] topics)
    {
        var list = new List<KeyValuePair<string, string>>();
        foreach (var (key, text) in topics ?? []) list.Add(new(key, text));
        return list;
    }

    public static LoreBoss Boss(string id, string name, string catalogKey, string location, string realm, string keyword = null,
        string bookPage = null, LoreChronicleText chronicle = null) =>
        new(id, name, catalogKey, location, realm, $"The story of {name}.", $"Kill {name}'s adds first.",
            keyword == null ? null : new LoreTopic(keyword, $"Of {name}: the {keyword}. See also [names]."),
            bookPage, chronicle);

    public static LoreLibrary Library() => new(
        [
            Place("krondon", "Krondon", "Albion", 61, "Krondon? The [names] are old, and worst is the [storm-keeper]. Also [nowhere].",
                [("names", "Old words. Ask of the [storm-keeper].")], ["scurceol-hyrde"], "Of the Hall under the Hill",
                [new LoreArea("chamber", 51941, 20068, 17174, 900)]),
            Place("caer-sidi", "Caer Sidi", "Albion", 60, "The tower of the dead.", [("names", "Caer Sidi's names are Welsh.")], ["apocalypse"], "The Glass Tower"),
            Place("trollheim", "Trollheim", "Midgard", 150, "Trolls, and [Nosdoden].", [], ["nosdoden"], "Under the Troll Hills"),
            Place("tur-suil", "Tur Suil", "Hibernia", 190, "Balor's eye.", [], ["balor"], "The Eye"),
            Place("darkness-falls", "Darkness Falls", "Neutral", 249, "The demons' hall.", [], ["legion"], "Into the Dark"),
            Place("albion-open", "Albion Classic", "Albion", 1, "The open land.", [], [], "Albion's Wilds"),
            Place("golestandts-lair", "Golestandt's Lair", "Albion", 1, "The dragon's lair.", [], ["golestandt"], "The Granite Giant",
                [new LoreArea("lair", 400000, 600000, 2000, 1500)]),
        ],
        [
            Boss("scurceol-hyrde", "Scurceol Hyrde", "krondon/scurceolhyrde", "krondon", "Albion", "storm-keeper", "The lights are winds.",
                new("{realm} broke the storm-keeper in {minutes} minutes, led by {raidLeader}.", "the storm held against {realm}.")),
            Boss("orylle", "Orylle", "krondon/orylle", "krondon", "Albion", null, null,
                new("{realm} felled Orylle in {minutes} minutes, led by {raidLeader}.", "Orylle threw {realm} out of Krondon after {minutes} minutes.")),
            Boss("apocalypse", "Apocalypse", "caer-sidi/apocalypse", "caer-sidi", "Albion", "horsemen"),
            Boss("nosdoden", "Nosdoden", "Nosdoden", "trollheim", "Midgard", "nosdoden"),
            Boss("balor", "Balor", "tur-suil/balor", "tur-suil", "Hibernia"),
            Boss("legion", "Legion", "darkness-falls/legion", "darkness-falls", "Neutral", "legion"),
            Boss("golestandt", "Golestandt", "golestandt-s-lair-dartmoor/albgolestandt", "golestandts-lair", "Albion", "granite giant"),
        ]);
}
