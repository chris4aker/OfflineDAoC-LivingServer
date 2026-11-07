namespace DOL.GS
{
    /// <summary>
    /// Explicit offline encounter adaptations. Bot raids cannot recover safely from
    /// scripted jail/air teleports or satisfy player-only split-position puzzles.
    /// Normal attacks, adds, health, resistances, and death/respawn chains remain active.
    /// </summary>
    public static class EpicEncounterMechanics
    {
        public static bool AllowDisplacement => false;
        public static bool AllowPositionPuzzle => false;
        public static bool AllowInvulnerableHazards => false;
        public static bool AllowAddShield => false;
        // Torst/Hurika keep their original database perches, attacks and loot.
        // Their unsupported aerial patrol/pull puzzle is separate from dragons'
        // existing fly/land cycle, which is deliberately not affected here.
        public static bool AllowGlacierGriffonPatrol => false;

        // Original OpenDAoC mechanics the fork had edited out for bot raids (docs/encounters/EPIC-ENCOUNTERS.md, "Restored
        // behind switches"). Each is off, so the game plays as before, until the raid AI can play it. The values were read
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
        public static bool AllowAroonDamageOrder { get; set; }
        /// <summary>Galladoria's primals come only when a raider stands within 350 of the waterfall bridge spot (bots too;
        /// the original counted real players only, which a bot raid could never meet).</summary>
        public static bool AllowGalladoriaBridgeSpot { get; set; }
        /// <summary>The Fire primal's trail of fire hits 220 in a 450 radius (not 120 in 300).</summary>
        public static bool AllowFullFirePatches { get; set; }
        /// <summary>Each Earthmender takes damage only from its own class group (pets always); others do nothing.</summary>
        public static bool AllowEarthmenderClassPuzzle { get; set; }
        /// <summary>Earthmenders never attack (they only heal).</summary>
        public static bool AllowEarthmendersPassive { get; set; }
        /// <summary>An Earthmender heals to full 30 s after its last fight even with raiders in the room.</summary>
        public static bool AllowEarthmenderQuickReset { get; set; }
        /// <summary>Earthmenders heal each other as well as the Earth primal.</summary>
        public static bool AllowEarthmenderMutualHeal { get; set; }
        /// <summary>Beatha's damage heals Xaga by twice the amount.</summary>
        public static bool AllowBeathaHealsXaga { get; set; }
        /// <summary>Beatha and Tine circle the room even while they are fought.</summary>
        public static bool AllowFairyFlight { get; set; }
        /// <summary>Grand Summoner Govannon has 300,000 health (not 200,000).</summary>
        public static bool AllowGovannonFullHealth { get; set; }
        /// <summary>Cold damage heals Icelord Kvasir by a fifth of his health and does no damage.</summary>
        public static bool AllowKvasirColdHeal { get; set; }
        /// <summary>Damage to King Tuscar heals Queen Kula, and damage to her heals him, while the other is below full health.</summary>
        public static bool AllowTuscarTwinHeal { get; set; }

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
