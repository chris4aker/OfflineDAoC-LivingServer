using System;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// Explicit offline encounter adaptations. Bot raids cannot recover safely from
    /// scripted jail/air teleports or satisfy player-only split-position puzzles.
    /// Normal attacks, adds, health, resistances, and death/respawn chains remain active.
    /// </summary>
    public static partial class EpicEncounterMechanics
    {
        // The five encounter switches (1.2: settable, and settable per encounter with SetFor; the global values are what
        // they were, off). A script asks AllowXFor(Body), which is the encounter's own value when one is set, else the
        // global one. Names listed in EncounterSwitches.

        /// <summary>Scripted throws, teleports and jails that the fork had switched off (Olcasgean, the Water primal,
        /// Easmarach, Spindler, the Horsemen room, Bane of Hope, Jailer Vifil, Hakr, Hurika, Kula, the waterfall).</summary>
        public static bool AllowDisplacement { get; set; }
        /// <summary>Split-position puzzles (Olcasgean's safe points, Conservator, Easmarach close range, Kula's circle).</summary>
        public static bool AllowPositionPuzzle { get; set; }
        /// <summary>Hazards that can't be hurt and must be avoided.</summary>
        public static bool AllowInvulnerableHazards { get; set; }
        /// <summary>A boss that takes no damage while its adds live.</summary>
        public static bool AllowAddShield { get; set; }
        // Torst/Hurika keep their original database perches, attacks and loot.
        // Their unsupported aerial patrol/pull puzzle is separate from dragons'
        // existing fly/land cycle, which is deliberately not affected here.
        /// <summary>Torst and Hurika's aerial patrol and pull.</summary>
        public static bool AllowGlacierGriffonPatrol { get; set; }

        /// <summary>A script's throw, teleport or jail may move a bot (companion bots and autonomous gamebots); off: a bot it
        /// picks stays where it is, as before 1.2 (bots were never picked). Real players are moved as before either way.
        /// On per encounter where the bots' recovery is proven (EncounterDisplacement, EncounterDisplacementRecovery).</summary>
        public static bool AllowBotDisplacement { get; set; }
        /// <summary>The dragons' wing-gust throw (OpenDAoC-Core 3513bad): every 60-80 s two to five raiders within 2,500 (not
        /// the dragon's target) are hurled to one of five spots of the lair. On per dragon since 1.2.</summary>
        public static bool AllowDragonThrow { get; set; }
        /// <summary>The dragons' messengers (OpenDAoC-Core 3513bad): from 50% health every 80-90 s, four to six messengers run
        /// out of the lair and each calls four to six adds where its path ends. Global off; on per dragon since 1.2 (the raid's
        /// casters and archers run them down).</summary>
        public static bool AllowDragonMessengers { get; set; }

        /// <summary>The encounter switches, by name (for logs, tests and the catalog); each can be set per encounter.</summary>
        public static readonly string[] EncounterSwitches =
        [
            nameof(AllowDisplacement), nameof(AllowPositionPuzzle), nameof(AllowInvulnerableHazards), nameof(AllowAddShield),
            nameof(AllowGlacierGriffonPatrol), nameof(AllowBotDisplacement), nameof(AllowDragonThrow), nameof(AllowDragonMessengers),
        ];

        public static bool AllowDisplacementFor(GameObject encounter) => On(nameof(AllowDisplacement), encounter);
        public static bool AllowPositionPuzzleFor(GameObject encounter) => On(nameof(AllowPositionPuzzle), encounter);
        public static bool AllowInvulnerableHazardsFor(GameObject encounter) => On(nameof(AllowInvulnerableHazards), encounter);
        public static bool AllowAddShieldFor(GameObject encounter) => On(nameof(AllowAddShield), encounter);
        public static bool AllowGlacierGriffonPatrolFor(GameObject encounter) => On(nameof(AllowGlacierGriffonPatrol), encounter);
        public static bool AllowBotDisplacementFor(GameObject encounter) => On(nameof(AllowBotDisplacement), encounter);
        public static bool AllowDragonThrowFor(GameObject encounter) => On(nameof(AllowDragonThrow), encounter);
        public static bool AllowDragonMessengersFor(GameObject encounter) => On(nameof(AllowDragonMessengers), encounter);
        /// <summary>By encounter name, for an add's script that asks for its boss's value ("AlbGolestandt").</summary>
        public static bool AllowDragonMessengersFor(string encounter) => On(nameof(AllowDragonMessengers), encounter);

        // Original OpenDAoC mechanics the fork had edited out for bot raids (docs/encounters/EPIC-ENCOUNTERS.md, "Restored
        // behind switches"). Each is off, so the game plays as before, until the raid AI can play it; the ones it plays are on
        // (1.2, epic wave 2: Galladoria's, the Earthmender class puzzle, Kvasir's cold heal, the Tuscar/Kula twin heal,
        // Govannon's full health). The values were read
        // from OpenDAoC-Core 3513bad (2026-11-04). Settable so the tests and the next raid AI step can turn one on.

        /// <summary>Fames, Morbus and Funus heal to full when drawn past their tether (not only when the fight is over).</summary>
        public static bool AllowHorsemenTetherHeal { get; set; }
        /// <summary>Funus takes damage only from clerics and from mercenaries, armsmen, infiltrators and scouts with their
        /// listed weapon; any other attacker (pets included) heals him to full.</summary>
        public static bool AllowFunusWeaponPuzzle { get; set; }
        /// <summary>Skeletal Sacristan walks his loop at 220 without stopping, with 70% magic resists.</summary>
        public static bool AllowSacristanPatrol { get; set; }
        /// <summary>Soul Reckoner takes no damage at all while a reckoned soul lives (not a quarter).</summary>
        public static bool AllowSoulReckonerFullShield { get; set; }
        /// <summary>Soul Reckoner's room is 900 units from its middle (not 1,500).</summary>
        public static bool AllowSoulReckonerSmallRoom { get; set; }
        /// <summary>Aroon takes only the damage types whose Scaith has died, and the Scaiths must die in order (slash, thrust,
        /// crush, body, cold, energy, heat, matter, spirit); each Scaith is immune until the one before it is dead.</summary>
        public static bool AllowAroonDamageOrder { get; set; } = true;
        /// <summary>Galladoria's primals come only when a raider stands within 350 of the waterfall bridge spot (bots too;
        /// the original counted real players only, which a bot raid could never meet).</summary>
        public static bool AllowGalladoriaBridgeSpot { get; set; } = true;
        /// <summary>The Fire primal's trail of fire hits 220 in a 450 radius (not 120 in 300).</summary>
        public static bool AllowFullFirePatches { get; set; } = true;
        /// <summary>Each Earthmender takes damage only from its own class group (pets always); others do nothing. On since 1.2:
        /// the raid AI sends each raider to an Earthmender its class hurts (EpicEncounterTactics class rules).</summary>
        public static bool AllowEarthmenderClassPuzzle { get; set; } = true;
        /// <summary>Earthmenders never attack (they only heal).</summary>
        public static bool AllowEarthmendersPassive { get; set; } = true;
        /// <summary>An Earthmender heals to full 30 s after its last fight even with raiders in the room.</summary>
        public static bool AllowEarthmenderQuickReset { get; set; } = true;
        /// <summary>Earthmenders heal each other as well as the Earth primal.</summary>
        public static bool AllowEarthmenderMutualHeal { get; set; } = true;
        /// <summary>Beatha's damage heals Xaga by twice the amount.</summary>
        public static bool AllowBeathaHealsXaga { get; set; } = true;
        /// <summary>Beatha and Tine circle the room even while they are fought.</summary>
        public static bool AllowFairyFlight { get; set; } = true;
        /// <summary>Grand Summoner Govannon has 300,000 health (not 200,000). On since 1.2 (epic wave 2): the raid kills his
        /// sacrifice and demon at range and spreads in his demon form (EpicEncounterTactics); the owner tests the win rate.</summary>
        public static bool AllowGovannonFullHealth { get; set; } = true;
        /// <summary>Cold damage heals Icelord Kvasir by a fifth of his health and does no damage. On since 1.2: no bot casts
        /// cold at him (EpicEncounterTactics.ForbidsSpell).</summary>
        public static bool AllowKvasirColdHeal { get; set; } = true;
        /// <summary>Damage to King Tuscar heals Queen Kula, and damage to her heals him, while the other is below full health.
        /// On since 1.2: the raid kills Kula first and only the King's own target fights him (EpicEncounterTactics).</summary>
        public static bool AllowTuscarTwinHeal { get; set; } = true;

        /// <summary>Every restore switch, by name (for logs, tests and the catalog).</summary>
        public static readonly string[] RestoreSwitches =
        [
            nameof(AllowHorsemenTetherHeal), nameof(AllowFunusWeaponPuzzle), nameof(AllowSacristanPatrol),
            nameof(AllowSoulReckonerFullShield), nameof(AllowSoulReckonerSmallRoom), nameof(AllowAroonDamageOrder),
            nameof(AllowGalladoriaBridgeSpot), nameof(AllowFullFirePatches), nameof(AllowEarthmenderClassPuzzle),
            nameof(AllowEarthmendersPassive), nameof(AllowEarthmenderQuickReset), nameof(AllowEarthmenderMutualHeal),
            nameof(AllowBeathaHealsXaga), nameof(AllowFairyFlight), nameof(AllowGovannonFullHealth), nameof(AllowKvasirColdHeal),
            nameof(AllowTuscarTwinHeal),
        ];

        // ---- Per-encounter values ---------------------------------------------------------------------------------------
        // An encounter is the script's class name (GetType().Name of the boss, or of the add that casts the mechanic).

        /// <summary>What ships on per encounter (everything else takes the global value). Each line is proven by tests
        /// (UT_EncounterDisplacement) and has a raid tactic or a recovery the bots play.</summary>
        public static readonly (string Switch, string Encounter, bool Value)[] ShippedPerEncounter =
        [
            Ship(nameof(AllowDragonThrow), "AlbGolestandt", true),
            Ship(nameof(AllowDragonThrow), "MidGjalpinulva", true),
            Ship(nameof(AllowDragonThrow), "HibCuuldurach", true),
            Ship(nameof(AllowBotDisplacement), "AlbGolestandt", true),
            Ship(nameof(AllowBotDisplacement), "MidGjalpinulva", true),
            Ship(nameof(AllowBotDisplacement), "HibCuuldurach", true),
            Ship(nameof(AllowBotDisplacement), "Xanxicar", true),
            // Epic wave 2 (1.2): each with its raid tactic (EpicEncounterTactics.Signals.cs, the catalog's aiTactics) and tests
            // (UT_EpicWaveTwo).
            // The dragons' messengers: casters and archers run them down (ranged adds); the party calls "runners".
            Ship(nameof(AllowDragonMessengers), "AlbGolestandt", true),
            Ship(nameof(AllowDragonMessengers), "MidGjalpinulva", true),
            Ship(nameof(AllowDragonMessengers), "HibCuuldurach", true),
            // Olcasgean: two parties hold the two safe spots; below half he throws a raider who was not thrown before
            // (it walks back); the Waterfall Antipass keeps the raid in the room until he dies.
            Ship(nameof(AllowPositionPuzzle), "Olcasgean", true),
            Ship(nameof(AllowDisplacement), "Olcasgean", true),
            Ship(nameof(AllowBotDisplacement), "Olcasgean", true),
            Ship(nameof(AllowDisplacement), "WaterfallAntipass", true),
            Ship(nameof(AllowBotDisplacement), "WaterfallAntipass", true),
            // The Water primal's and Spindler Broodmother's teleports: the raider walks back (EncounterDisplacementRecovery).
            Ship(nameof(AllowDisplacement), "WaterPrimal", true),
            Ship(nameof(AllowBotDisplacement), "WaterPrimal", true),
            Ship(nameof(AllowDisplacement), "SpindlerBroodmother", true),
            Ship(nameof(AllowBotDisplacement), "SpindlerBroodmother", true),
            // Close range: Conservator takes damage only from attackers within 800 of his spawn, Easmarach within his tether
            // of his; the raid steps in (and leads Conservator home). Easmarach's push behind the falls stays off (his
            // AllowDisplacement): see docs/encounters/EPIC-ENCOUNTERS.md.
            Ship(nameof(AllowPositionPuzzle), "Conservator", true),
            Ship(nameof(AllowPositionPuzzle), "Easmarach", true),
            // Epic wave 2, Midgard and Hibernia (EpicEncounterTactics.Tuscaran/SummonersHall/Hibernia, UT_EpicWave2MidHib).
            // Tuscaran Glacier: Kula's drag to her throne and frost circle, Vifil's jail (a team clears the guards), the throws.
            Ship(nameof(AllowDisplacement), "QueenKula", true),
            Ship(nameof(AllowPositionPuzzle), "QueenKula", true),
            Ship(nameof(AllowBotDisplacement), "QueenKula", true),
            Ship(nameof(AllowDisplacement), "Jailer", true),
            Ship(nameof(AllowBotDisplacement), "Jailer", true),
            Ship(nameof(AllowDisplacement), "Hakr", true),
            Ship(nameof(AllowBotDisplacement), "Hakr", true),
            Ship(nameof(AllowDisplacement), "Hurika", true),
            Ship(nameof(AllowBotDisplacement), "Hurika", true),
            // Summoner's Hall: Cunovinda's ledge, the Shade of Aelfgar's gallery.
            Ship(nameof(AllowBotDisplacement), "SummonerCunovinda", true),
            Ship(nameof(AllowBotDisplacement), "ShadeOfAelfgar", true),
            // Midgard: Iarnvidiur's teleport, Boligar's port, Mortufoghus's and Ydenia's throws, the Glacier Giant's kick.
            Ship(nameof(AllowBotDisplacement), "Iarnvidiur", true),
            Ship(nameof(AllowBotDisplacement), "Boligar", true),
            Ship(nameof(AllowBotDisplacement), "Mortufoghus", true),
            Ship(nameof(AllowBotDisplacement), "Ydenia", true),
            Ship(nameof(AllowBotDisplacement), "GlacierGiant", true),
            // Fomor: Krackenschtein's throw, Anurigunda's doorway push.
            Ship(nameof(AllowBotDisplacement), "Krackenschtein", true),
            Ship(nameof(AllowBotDisplacement), "Anurigunda", true),
        ];

        private static (string, string, bool) Ship(string name, string encounter, bool value) => (name, encounter, value);

        private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Switch, string Encounter), bool> PerEncounter = Shipped();
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Reflection.PropertyInfo> Globals = new(StringComparer.Ordinal);

        private static System.Collections.Concurrent.ConcurrentDictionary<(string, string), bool> Shipped()
        {
            var d = new System.Collections.Concurrent.ConcurrentDictionary<(string, string), bool>();
            foreach (var (name, encounter, value) in ShippedPerEncounter) d[(name, encounter)] = value;
            return d;
        }

        /// <summary>The encounter's name for its switches: the class name of <paramref name="encounter"/>.</summary>
        public static string EncounterName(GameObject encounter) => encounter?.GetType().Name;

        /// <summary>The switch <paramref name="name"/> for <paramref name="encounter"/>: its own value when one is set, else the
        /// global one. Unknown switch: false.</summary>
        public static bool On(string name, GameObject encounter) => On(name, EncounterName(encounter));

        public static bool On(string name, string encounter)
        {
            if (string.IsNullOrEmpty(name)) return false;
            if (!string.IsNullOrEmpty(encounter) && PerEncounter.TryGetValue((name, encounter), out bool own)) return own;
            return Global(name);
        }

        /// <summary>The global value of the switch <paramref name="name"/> (false for an unknown name).</summary>
        public static bool Global(string name)
        {
            var p = Globals.GetOrAdd(name, static n => typeof(EpicEncounterMechanics).GetProperty(n,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static));
            return p?.PropertyType == typeof(bool) && p.GetValue(null) is true;
        }

        /// <summary>Sets (true/false) or clears (null) the switch's value for one encounter.</summary>
        public static void SetFor(string name, string encounter, bool? value)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(encounter)) return;
            if (value is bool v) PerEncounter[(name, encounter)] = v;
            else PerEncounter.TryRemove((name, encounter), out _);
        }

        /// <summary>The per-encounter value set for the switch, or null (the encounter takes the global value).</summary>
        public static bool? ValueFor(string name, string encounter) =>
            PerEncounter.TryGetValue((name, encounter), out bool v) ? v : null;

        /// <summary>Back to what ships (tests).</summary>
        public static void ResetPerEncounter()
        {
            PerEncounter.Clear();
            foreach (var (name, encounter, value) in ShippedPerEncounter) PerEncounter[(name, encounter)] = value;
        }

        /// <summary>Every per-encounter value in force, for logs ("AllowDragonThrow@AlbGolestandt=true").</summary>
        public static string[] PerEncounterValues() => PerEncounter.OrderBy(p => p.Key.Switch, StringComparer.Ordinal)
            .ThenBy(p => p.Key.Encounter, StringComparer.Ordinal).Select(p => $"{p.Key.Switch}@{p.Key.Encounter}={(p.Value ? "true" : "false")}").ToArray();

        /// <summary>
        /// The scripted bosses of the epic dungeons, Summoner's Hall and Darkness Falls reset (walk home, full health,
        /// adds and phases cleared) on any brain turn with an empty aggro list. In a raid of 200 the list can be empty
        /// for a moment while the boss is still being hit, and Hurionthex in Galladoria went from 31% back to full
        /// that way, again and again (2026-10-14). The reset now waits until the boss has been out of combat for
        /// <see cref="ResetQuietMilliseconds"/>; a wiped or departed raid still resets it as before.
        /// </summary>
        public const int ResetQuietMilliseconds = 8_000;

        /// <summary>
        /// Princes Ba'alorien and Asmoien in Darkness Falls devour pets near them and heal by the pet's health. They ate
        /// every pet within 2,000 units on every brain turn, so a raid with pet classes could never wound them
        /// (Asmoien skipped at 100% with raiders beside him, 2026-10-15). Mechanic change (owner, 2026-10-14: change
        /// mechanics rather than health): one pet every five seconds, the nearest; the heal per pet is unchanged.
        /// </summary>
        public const int PetDevourIntervalMilliseconds = 5_000;

        /// <summary>
        /// What the princes devour: a summoned pet, of a real player or of a bot (alive, its brain a controlled brain), never a
        /// bot itself. A bot's own brain (BotBrain) is a controlled brain too, so the plain "Brain is IControlledBrain" test
        /// ate companion bots and autonomous gamebots and healed the prince by each one (a dead bot every five seconds in a
        /// bot raid; found 2026-11-06 by the Albion lore check).
        /// </summary>
        public static bool IsDevourablePet(GameNPC npc) =>
            npc != null && npc is not GameBot && npc.IsAlive && npc.Brain is DOL.AI.Brain.IControlledBrain;

        /// <summary>The pet a prince devours next: the nearest devourable pet among <paramref name="candidates"/>, or null.</summary>
        public static GameNPC NearestDevourablePet(GameObject prince, System.Collections.Generic.IEnumerable<GameNPC> candidates)
        {
            GameNPC pet = null;
            if (prince == null || candidates == null) return null;
            foreach (GameNPC candidate in candidates)
                if (IsDevourablePet(candidate) && (pet == null || prince.GetDistanceTo(candidate) < prince.GetDistanceTo(pet)))
                    pet = candidate;
            return pet;
        }

        public static bool FightIsOver(GameNPC boss) => boss == null || !boss.IsAlive || !boss.InCombatInLast(ResetQuietMilliseconds);
    }
}
