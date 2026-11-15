using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// The open-world big bosses' realm raids (Living Server 1.2, owner 2026-11-14: every big boss is in, with a real raid
    /// plan, as close to the original mechanics as possible, and the bots run them themselves). Ordinary groups only walk
    /// around an open-world epic (AutonomousWorldBotController.RouteThreats), so without a raid of its own a boss here was
    /// never fought by the bots at all.
    /// <para>
    /// Each raid is fought by the dragon-fight engine (AutonomousRealmRaid: a muster hub of the boss's realm in its region,
    /// a staging ring around the boss's spawn, the regroup after a wipe, the Special Forces, win-rate sizing), with the
    /// boss's spawn point in place of a dragon's lair; the boss's own plan is its catalog row and code rule
    /// (EpicEncounterTactics.OpenWorld.cs). Its realm raids it, as a realm raids its own dragon.
    /// </para>
    /// <para>
    /// Sizes are the period's comfortable raid at that boss (docs/RAID-SIZES.md), never under the realm raid's smallest
    /// full size (40): most of these were one- to three-group fights by 2003 (the evidence is in each row). Win-rate sizing
    /// then moves each one 70-200% as for every raid.
    /// </para>
    /// <para>
    /// The schedule: a realm's scheduled roll goes to one of these at <see cref="SharePercentSetting"/> percent (25 as
    /// shipped; 0 = never on the schedule, the GM sweep and the launcher still start them), so the dragon, epic dungeon,
    /// neutral and raid dungeon raids keep three quarters of the rolls and the realm raids no more often than before. The
    /// Wretches of Winter count as one choice between them (<see cref="OpenWorldRaid.Group"/>). An open-world raid whose
    /// start fails on the schedule (no route from its hub, nothing to stage on) waits
    /// <see cref="FailedStartCooldownMilliseconds"/> before the schedule may pick it again, so a boss the navmesh can't
    /// reach doesn't take the realm's slot every time.
    /// </para>
    /// Tests only: no open-world raid has been seen on the owner's PC.
    /// </summary>
    public static class RealmRaidOpenWorld
    {
        /// <param name="Group">Raids that share one choice on the schedule (the five Wretches of Winter); null: its own.</param>
        /// <param name="Evidence">Where its size comes from (docs/RAID-SIZES.md).</param>
        public sealed record OpenWorldRaid(string Id, string Name, eRealm Realm, ushort Region, string BossType, int SuggestedBots,
            string Group, string Evidence);

        public const string IdPrefix = "openworld-";

        /// <summary>The kind of these raids in the realm event records and the launcher.</summary>
        public const string Kind = "Open-world boss";

        public static readonly OpenWorldRaid[] Raids =
        [
            // Albion
            new("openworld-albion-greenknight", "Green Knight", eRealm.Albion, 1, "OFGreenKnight", 40, null,
                "about Grand Summoner difficulty, 4-5 groups (Albion epic raid guide, FreddysHouse 115688, Feb 2003)"),
            new("openworld-albion-coliath", "Coliath", eRealm.Albion, 51, "Colialt", 64, null,
                "about Legion difficulty (Albion epic raid guide, Feb 2003); sized as Legion (64)"),
            new("openworld-albion-emthoro", "Spirit of Lord Emthoro", eRealm.Albion, 51, "SpiritOfLordEmthoro", 40, null,
                "more than one full group (Allakhazam, Mar 2004); the smallest raid (40)"),
            // Midgard
            new("openworld-midgard-glaciergiant", "Glacier Giant", eRealm.Midgard, 100, "GlacierGiant", 48, null,
                "two guild groups wiped in seven minutes, done with 5-6 groups (FreddysHouse \"Glacier Giant\", Dec 2003)"),
            new("openworld-midgard-wreth-jotun", "Wretch of Winter (jotun)", eRealm.Midgard, 100, "WrethOfWinterJotun", 40, "wreths-of-winter",
                "the hardest Wretch; groups of about 10 (Allakhazam 2002-2003); \"wiped out everyone 3 times\" (FreddysHouse, May 2002)"),
            new("openworld-midgard-wreth-arachite", "Wretch of Winter (arachite)", eRealm.Midgard, 100, "WrethOfWinterArachite", 40, "wreths-of-winter",
                "wiped a 10-person group twice, killed on the third try (Allakhazam 2002-2003)"),
            new("openworld-midgard-wreth-spider", "Wretch of Winter (spider)", eRealm.Midgard, 100, "WrethOfWinterSpider", 40, "wreths-of-winter",
                "the easiest Wretch (Allakhazam 2002-2003); the smallest raid (40)"),
            new("openworld-midgard-wreth-crab", "Wretch of Winter (crab)", eRealm.Midgard, 100, "WrethOfWinterCrab", 40, "wreths-of-winter",
                "no count of its own (Allakhazam 2002-2003); the smallest raid (40)"),
            new("openworld-midgard-wreth-scorpion", "Wretch of Winter (scorpion)", eRealm.Midgard, 100, "WrethOfWinterScorp", 40, "wreths-of-winter",
                "no count of its own (Allakhazam 2002-2003); the smallest raid (40)"),
            new("openworld-midgard-revenant", "Raumarik Revenant", eRealm.Midgard, 100, "RaumarikRevenant", 40, null,
                "killed with the Wretch of Winter on Excalibur (FreddysHouse, May 2002); no count: the smallest raid (40)"),
            new("openworld-midgard-mortufoghus", "Mortufoghus", eRealm.Midgard, 151, "Mortufoghus", 40, null,
                "three groups couldn't get it under 50% (Allakhazam, Oct 2003); 1.5-3 groups (JeuxOnline, Apr 2003)"),
            // Hibernia
            new("openworld-hibernia-evern", "Evern", eRealm.Hibernia, 200, "Evern", 40, null,
                "a full group was beaten, eight killed him in 20 minutes (Allakhazam, May and Jul 2003); the Green Knight's equal (Feb 2003)"),
            new("openworld-hibernia-juggernaut", "Juggernaut", eRealm.Hibernia, 200, "EpicJuggernaut", 40, null,
                "a full group with bladeturn took it to 25% (Allakhazam, 2002); the smallest raid (40)"),
            new("openworld-hibernia-curengkur", "Curengkur", eRealm.Hibernia, 181, "Curengkur", 40, null,
                "no count (Allakhazam 2005-2012); the smallest raid (40)"),
            new("openworld-hibernia-marfoirwort", "Marfoirwort", eRealm.Hibernia, 181, "Marfoirwort", 40, null,
                "a Writhing Death quest boss, no count (Herald 2003); the smallest raid (40)"),
            new("openworld-hibernia-mollunero", "Mollunero", eRealm.Hibernia, 181, "Mollunero", 40, null,
                "no count (Allakhazam); the smallest raid (40)"),
        ];

        public static bool Is(string id) => id != null && id.StartsWith(IdPrefix, StringComparison.Ordinal);

        /// <summary>The raid of this id, or null.</summary>
        public static OpenWorldRaid Of(string id) => id == null ? null : Array.Find(Raids, r => r.Id == id);

        /// <summary>The schedule's choice a raid belongs to: its group, else its own id.</summary>
        public static string ChoiceOf(string id) => Of(id)?.Group ?? id;

        [ServerProperty("autonomous", "realm_raid_open_world_percent",
            "Living Server 1.2: the share (percent) of a realm's scheduled raids that go to an open-world big boss (the Green Knight, the Glacier Giant, Evern and the rest; docs/RAID-SIZES.md), when one is up. The other raids keep the rest, and the realm raids no more often than before. 0 = never on the schedule (the GM sweep and the launcher still start them).", 25)]
        public static int SharePercentSetting = 25;

        /// <summary>An open-world raid whose scheduled start failed is not picked by the schedule again for this long.</summary>
        public const long FailedStartCooldownMilliseconds = 2 * 60 * 60_000L;

        /// <summary>
        /// The schedule's pick among the raids a realm could open now (pure): with <paramref name="sharePercent"/> percent
        /// (<paramref name="poolRoll"/> 0-1) one of the open-world raids, chosen by choice first (the Wretches of Winter are one)
        /// and then within it; otherwise one of the others. When one side has none the other is picked; with a share of 0 an
        /// open-world raid is never picked. Null when nothing can be.
        /// </summary>
        public static string Choose(IReadOnlyCollection<string> candidates, int sharePercent, double poolRoll, double choiceRoll, double memberRoll)
        {
            if (candidates == null || candidates.Count == 0) return null;
            string[] open = candidates.Where(Is).Distinct().ToArray();
            string[] rest = candidates.Where(id => !Is(id)).Distinct().ToArray();
            bool takeOpen = open.Length > 0 && sharePercent > 0 && (rest.Length == 0 || poolRoll * 100 < sharePercent);
            if (!takeOpen) return rest.Length == 0 ? null : rest[Index(memberRoll, rest.Length)];
            string[] choices = open.Select(ChoiceOf).Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
            string choice = choices[Index(choiceRoll, choices.Length)];
            string[] members = open.Where(id => ChoiceOf(id) == choice).OrderBy(id => id, StringComparer.Ordinal).ToArray();
            return members[Index(memberRoll, members.Length)];
        }

        private static int Index(double roll, int count) => Math.Clamp((int)(roll * count), 0, count - 1);

        /// <summary>The schedule's pick with the server's random and the setting.</summary>
        public static string Choose(IReadOnlyCollection<string> candidates) =>
            Choose(candidates, SharePercentSetting, Random.Shared.NextDouble(), Random.Shared.NextDouble(), Random.Shared.NextDouble());

        /// <summary>Where an open-world raid forms its staging ring and holds the fight (a dragon's lair for a dragon): the boss's
        /// spawn point. Point3D(0,0,0) without a boss.</summary>
        public static Point3D Home(GameNPC boss) =>
            boss == null ? new Point3D(0, 0, 0) : new Point3D(boss.SpawnPoint.X, boss.SpawnPoint.Y, boss.SpawnPoint.Z);

        /// <summary>The sweep's word for these raids (/epictest sweep start openworld).</summary>
        public const string SweepWord = "openworld";

        public static IEnumerable<string> Ids => Raids.Select(r => r.Id);
    }
}
