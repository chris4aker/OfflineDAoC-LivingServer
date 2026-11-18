using System;

namespace DOL.GS
{
    /// <summary>Text only: never changes event clocks, membership or combat decisions.</summary>
    public static class RealmEventBanter
    {
        public static bool ReminderDue(bool started, bool sent, long remaining) =>
            !started && !sent && remaining > 0 && remaining <= 20 * 60_000L;

        public static string RaidReminder(string name, bool forced) => forced
            ? $"Sharpen your blades for {name}! About twenty minutes until our earliest assault; we still need our company assembled and the way clear."
            : $"The muster for {name} has about twenty minutes left. Gather your companions! We march once enough of us are ready, not before.";

        public static string RaidOutcome(string name, string outcome) => outcome switch
        {
            "Boss defeated" => Pick($"Victory at {name}! Raise a cup for those who stood beside us.",
                $"Word from {name}: the final foe has fallen! Honor to our realm and its brave company."),
            "Timed out" => Pick($"Our time at {name} is spent. We could not finish the fight; rally home and tend the wounded.",
                $"The expedition to {name} must withdraw. No victory this time, but we shall return."),
            "Beaten to it" => $"Another realm reached the end of {name} before us and took the prize. Fall back; next time we strike first.",
            "Defeated" => Pick($"We were beaten back at {name}. Carry the fallen home; we will be stronger when we return.",
                $"{name} has broken our expedition. Fall back, companions, and remember what struck us down."),
            "Failed rally" => $"Not enough of our company reached {name} in time. The muster is called off; save your strength for another day.",
            "Stopped by player" or "Superseded" or "Sweep cap" => $"The expedition to {name} is called off. Fall back and rest, companions.",
            _ => $"The expedition at {name} has ended without a confirmed victory. Return safely, companions."
        };

        public static string RaidWipe(string name, string boss) =>
            // A dragon raid is named after its dragon (2026-10-30): no "Golestandt struck us down in Golestandt". So is every
            // open-world raid (Coliath, the Green Knight, Evern), which has no gathering place before it and is not a dragon (1.2).
            !NamedForBoss(name, boss)
                ? Pick($"{boss} has struck down our company in {name}! Regroup at the front: we go in once more together.",
                    $"We have fallen to {boss}. Rise, rebuff and gather; one more assault on {boss} awaits.")
                : IsDragon(boss)
                    ? Pick($"{boss} has struck down our company! Regroup where the raid gathered: we go in once more together.",
                        $"We have fallen to {boss}. Rise, rebuff and return to where the raid gathered; one more assault on the dragon awaits.")
                    : Pick($"{boss} has struck down our company! Regroup and rebuff: we go in once more together.",
                        $"We have fallen to {boss}. Rise, rebuff and gather; one more assault on {boss} awaits.");

        /// <summary>The three realm dragons, as their raids and the realm call them. Pure.</summary>
        public static bool IsDragon(string name) =>
            !string.IsNullOrWhiteSpace(name) && (name.StartsWith("Golestandt", StringComparison.OrdinalIgnoreCase) ||
                                                 name.StartsWith("Gjalpinulva", StringComparison.OrdinalIgnoreCase) ||
                                                 name.StartsWith("Cuuldurach", StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Whether a raid is named after its boss (every dragon and open-world raid): the same name, the raid's name with a
        /// note ("Wretch of Winter (jotun)"), or the boss's name with a title ("Cuuldurach the Glimmer King"). Then "{boss}
        /// in {raid}" says the name twice. Pure.
        /// </summary>
        public static bool NamedForBoss(string raid, string boss)
        {
            if (string.IsNullOrWhiteSpace(raid) || string.IsNullOrWhiteSpace(boss))
                return false;
            raid = raid.Trim();
            boss = boss.Trim();
            return string.Equals(raid, boss, StringComparison.OrdinalIgnoreCase) ||
                   raid.StartsWith(boss + " (", StringComparison.OrdinalIgnoreCase) ||
                   boss.StartsWith(raid + " ", StringComparison.OrdinalIgnoreCase);
        }

        public static string SiegeReminder(string name, bool defender) => defender
            ? $"Stand ready at {name}! The enemy's muster has about twenty minutes left; keep shields and supplies close."
            : $"About twenty minutes remain to muster for {name}. Close ranks, warbands; we need a fighting force before committing to the walls.";

        /// <summary>
        /// The written notice of a siege's end to one realm. <paramref name="timedOut"/> is a defended siege ("Siege defended: ..."),
        /// whose <paramref name="winner"/> is the realm that held the keep when it is known. Chat 1.2 (LOG-004, owner's PC 00:06:17:
        /// "hib took nged" for a siege Hibernia defended): a hold is told as a hold, "We held" to the defenders and "Hibernia held
        /// Dun nGed." to the others, never as "has fallen to".
        /// </summary>
        public static string SiegeOutcome(string name, bool defender, bool timedOut, bool failedRally, eRealm winner, eRealm audience)
        {
            if (timedOut && !failedRally)
            {
                if (defender || winner != eRealm.None && winner == audience)
                    return $"We held {name}! The enemy's time is spent and our banners still fly.";
                if (winner != eRealm.None)
                    return $"{GlobalConstants.RealmToName(winner)} held {name}. The siege there is over.";
            }
            if (winner != eRealm.None)
                return winner == audience ? $"{name} is ours! Raise our banners and honor those who fought for it."
                    : $"{name} has fallen to {GlobalConstants.RealmToName(winner)}. Tend the wounded; this struggle is not over.";
            if (failedRally) return defender ? $"The enemy muster at {name} has broken up. The walls remain ours; stay watchful."
                : $"The muster for {name} failed to gather enough fighters in time. Stand down, warbands; there will be another campaign.";
            if (timedOut) return defender ? $"We held {name}! The enemy's time is spent and our banners still fly."
                : $"Our time to take {name} has run out. Withdraw in good order; we could not break the defense.";
            return $"The fighting for {name} has ended. Await confirmed word before claiming victory.";
        }

        private static string Pick(string first, string second) => Random.Shared.Next(2) == 0 ? first : second;
    }
}
