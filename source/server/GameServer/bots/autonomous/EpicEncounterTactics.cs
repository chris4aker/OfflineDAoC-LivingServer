using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Raid tactics for epic encounters, fed by the encounter catalog (docs/encounters, hand-notes.json "aiTactics", written
    /// into <see cref="Catalog"/> by source/tools/epic_encounter_catalog.py). Three generic rules, no boss is named here:
    /// <list type="bullet">
    /// <item><b>Required adds</b>: a boss that takes no damage while its adds live (Scurceol Hyrde's four Lyft Miht orbs). The
    /// raid's target is one of the adds while any lives; each party takes the add its index points at; nobody hits the boss
    /// until the last add is dead; a raider who can't reach its add (melee under a flying orb) stands off the boss.</item>
    /// <item><b>Fight at home</b>: a boss fought only within a radius of its spawn (Black Lady, 1,100 inside her 1,500 tether).
    /// Past it the raid stops hitting it and whoever it chases leads it home, before its tether resets it.</item>
    /// <item><b>Dies with</b>: an encounter that dies when another does (the Ancient Black Oak with Black Lady). The raid
    /// turns on the other one instead.</item>
    /// <item><b>Ground hazards</b>: a scripted NPC that is a burning patch of floor, not a monster (Orshom's Fire: it never
    /// moves, burns everything within its radius and takes only one damage type). It is never the raid's target or a battle
    /// post; raiders who deal that damage type shoot it from outside its radius, everyone else steps out of it and fights the
    /// boss.</item>
    /// </list>
    /// Since then (each from a catalog field; the round-3 ones in 1.2): keep-alive adds (Aidon), blasts at health
    /// thresholds (Xanxicar), a spread off the boss's target in a form (Govannon's demon), ranged adds that casters and archers
    /// take while the rest stay on the boss (Govannon's walkers, Spindler's newborns), hazards nobody fights or stands in
    /// (Orshom's Fire), a boss fought after another (Orylle after Orshom), required adds by class (Galladoria's Earthmenders,
    /// while the original class rule is on) and a death burst cleared at the body (Spindler's brood).
    /// Autonomous gamebots only: companion bots and player-led groups are not steered.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EpicEncounterTactics));

        /// <param name="Boss">The encounter's class name.</param>
        /// <param name="Encounter">Its catalog id.</param>
        /// <param name="RequiredAdds">Class names of the adds that shield it (empty: none).</param>
        /// <param name="FightAtHomeRadius">Fight it only this close to its spawn (0: no limit).</param>
        /// <param name="DiesWith">Class name of the encounter whose death kills this one (null: none).</param>
        /// <param name="KeepAlive">Leave this many of the required adds alive (Aidon summons a new set of illusions only
        /// once all four are dead): the boss is fought once no more than this many live, and nobody attacks the last ones.</param>
        /// <param name="BlastThresholds">Health percents at which the boss starts a raid-wide area hit (Xanxicar's stomp).</param>
        /// <param name="BlastRadius">That hit's radius around the boss's target.</param>
        /// <param name="Hazards">Class names of the ground hazards of this encounter (empty: none).</param>
        /// <param name="HazardRadius">How far a hazard burns around its own spot.</param>
        /// <param name="HazardDamageType">The only damage type that hurts a hazard (an eDamageType name; null: none).</param>
        public sealed record Tactic(string Boss, string Encounter, string[] RequiredAdds, int FightAtHomeRadius, string DiesWith,
            int KeepAlive = 0, int[] BlastThresholds = null, int BlastRadius = 0, string[] Hazards = null, int HazardRadius = 0,
            string HazardDamageType = null)
        {
            public bool HasRequiredAdds => RequiredAdds is { Length: > 0 };
            /// <summary>Never null (the catalog's rows leave it out).</summary>
            public string[] Hazards { get; init; } = Hazards ?? [];
            public bool HasBlast => BlastThresholds is { Length: > 0 } && BlastRadius > 0;

            /// <summary>The boss casts an area hit of this radius around its target (Govannon's Shroud of Agony, 300): casters,
            /// archers and healers outside its target's party stand clear of that target (melee can't, see SpreadSpot).</summary>
            public int SpreadRadius { get; init; }
            /// <summary>Only while the boss has this model (Govannon's demon form, 636; 0: always).</summary>
            public int SpreadWhileModel { get; init; }
            /// <summary>While the spread is on, melee fight the boss from behind him, clear of the area hit around his target
            /// (<see cref="EncounterMeleeStations"/>): his target keeps <see cref="AnchorHoldDistance"/> from him and the
            /// other melee stand <see cref="BehindDistance"/> behind him on the line from his target (owner 2026-11-07,
            /// Govannon's demon: his Shroud of Agony took 45 raiders in 90 s, nearly all of them melee).</summary>
            public bool MeleeBehind { get; init; }
            /// <summary>Healers keep healing where they stand, instead of stepping out of the spread, while melee are still
            /// inside it (the melee there need them; stepping out costs casts).</summary>
            public bool HealersHoldInSpread { get; init; }
            /// <summary>With <see cref="MeleeBehind"/>: the melee fight him from this many ring spots around him instead of one
            /// spot behind him, each party at its own spot (owner 2026-11-08, Govannon's demon: "melee spread over 3 ring
            /// spots"), so an area hit on one melee raider reaches about a third of the melee. 0: the one spot behind him.</summary>
            public int MeleeRing { get; init; }
            /// <summary>While the spread is on and the boss's target is not a tank, the raid tank nearest to him that fights
            /// him taunts him (taunt spell where its class has one, taunt style) (owner 2026-11-08: "tanks taunt him back").</summary>
            public bool TanksTaunt { get; init; }
            /// <summary>Adds the raid's casters and archers kill while everyone else stays on the boss (Govannon's sacrifice and
            /// demon, who summon a Shade if they reach his altar; Spindler Broodmother's newborns).</summary>
            public string[] RangedAdds { get; init; } = [];
            /// <summary>Fight this one only once that one is dead (Orylle after Orshom Brong): the party it fights holds it,
            /// everyone else kills the other first.</summary>
            public string After { get; init; }
            /// <summary>Which classes can hurt each required add (Galladoria's Earthmenders): raiders take an add their class
            /// hurts; one whose class hurts none fights the boss. Applies while <see cref="ClassRuleSwitch"/> is on (none: always).</summary>
            public AddClasses[] ClassRules { get; init; } = [];
            public string ClassRuleSwitch { get; init; }
            /// <summary>Adds that burst from the boss's body when it dies (Spindler Broodmother's underdeveloped spindlers): the
            /// route clears them at the body before it moves on.</summary>
            public string[] DeathAdds { get; init; } = [];
            /// <summary>The required adds die one at a time in the catalog's order (Aroon's Scaiths: each is immune until the one
            /// before it is dead; Xaga's fairies, Beatha first): the whole raid takes the first one alive, not one each.</summary>
            public bool AddsInOrder { get; init; }
            /// <summary>The ranged adds hunt the raid's healers and pile up when split one per party (Spindler Broodmother's
            /// newborns, play test duo3: up to 11 alive, 62 of the raid's deaths): casters and archers all take the same one,
            /// the one with the least health left, and a party's melee peel one off their own party's casters and healers.</summary>
            public bool FocusRangedAdds { get; init; }
            /// <summary>Up to this many whole parties of the raid, with their healers, work only the ranged adds, taking the
            /// ones on the raid's healers and casters first; everyone else stays on the boss (owner 2026-11-08, Spindler
            /// Broodmother: "2-3 newborn-only parties"). 0: off. Replaces <see cref="FocusRangedAdds"/> where set.</summary>
            public int AddParties { get; init; }
            /// <summary>Area casters aim their area spells at the boss or a ranged add where the most of the boss and its ranged
            /// adds stand within the spell's radius (owner 2026-11-08, Spindler Broodmother: "AoE on her spot"; her newborns
            /// appear within 50-80 of her). Gated to this tactic: the shared caster AI then picks the area spell itself.</summary>
            public bool AreaOnClump { get; init; }

            public bool HasHazards => Hazards is { Length: > 0 } && HazardRadius > 0;
        }

        /// <summary>The classes that can hurt one add (pets always can).</summary>
        public sealed record AddClasses(string Add, eCharacterClass[] Classes);

        /// <summary>An encounter's leash from the catalog: its tether, and whether it resets (walks home at full health) when
        /// nobody is left on its aggro list. Used to decide how to answer a boss that engages the raid before its muster.</summary>
        public sealed record Leash(string Boss, int TetherRange, bool Resets);

        private static readonly Dictionary<string, Tactic> Extra = new(StringComparer.Ordinal);
        private static Dictionary<string, Tactic> _byType;
        private static HashSet<string> _partTypes, _deathAddTypes;

        private static Dictionary<string, Tactic> ByType
        {
            get
            {
                if (_byType != null) return _byType;
                var map = new Dictionary<string, Tactic>(StringComparer.Ordinal);
                foreach (Tactic t in Catalog) map[t.Boss] = t;
                foreach (var kv in Extra) map[kv.Key] = kv.Value;
                _deathAddTypes = map.Values.SelectMany(t => t.DeathAdds ?? []).ToHashSet(StringComparer.Ordinal);
                _partTypes = map.Values.SelectMany(t => (t.RequiredAdds ?? []).Concat(t.RangedAdds ?? []).Concat(t.Hazards ?? []))
                    .ToHashSet(StringComparer.Ordinal);
                return _byType = map;
            }
        }

        private static HashSet<string> PartTypes { get { _ = ByType; return _partTypes; } }
        private static HashSet<string> DeathAddTypes { get { _ = ByType; return _deathAddTypes; } }

        /// <summary>The NPC is part of a tactic's fight (a required or ranged add, or a hazard), so a raider aiming at it
        /// asks the tactic too.</summary>
        public static bool IsTacticPart(GameNPC npc) => npc != null && PartTypes.Contains(npc.GetType().Name);

        /// <summary>The NPC is a death burst of some tactic's boss (Spindler Broodmother's brood): the route clears it at the
        /// body like an add of a living encounter.</summary>
        public static bool IsDeathAdd(GameNPC npc) => npc != null && DeathAddTypes.Contains(npc.GetType().Name);

        /// <summary>The catalog's tactics (generated) plus any added by <see cref="Register"/>.</summary>
        public static IReadOnlyCollection<Tactic> All => ByType.Values;

        /// <summary>Adds a tactic for a class (tests; a later step may load more).</summary>
        public static void Register(Tactic tactic)
        {
            Extra[tactic.Boss] = tactic;
            _byType = null;
            _hazards = null;
            _requiredAddClasses = null;
        }

        public static void Unregister(string boss)
        {
            Extra.Remove(boss);
            _byType = null;
            _hazards = null;
            _requiredAddClasses = null;
        }

        public static Tactic For(GameNPC npc) => npc == null ? null : ByType.GetValueOrDefault(npc.GetType().Name);

        /// <summary>The tactic of a boss class name ("SpindlerBroodmother"), or null.</summary>
        public static Tactic For(string bossType) => bossType == null ? null : ByType.GetValueOrDefault(bossType);

        private static Dictionary<string, Leash> _leashes;
        public static Leash LeashOf(GameNPC npc)
        {
            if (npc == null) return null;
            _leashes ??= Leashes.GroupBy(l => l.Boss).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            return _leashes.GetValueOrDefault(npc.GetType().Name);
        }

        /// <summary>Within this distance of the boss an add counts as its (two copies of a dungeon never share a region,
        /// but a boss's adds can sit at the corners of a large room).</summary>
        public const int AddSearchRadius = 6_000;

        private static bool Present(GameNPC npc) => npc != null && npc.IsAlive && npc.ObjectState == GameObject.eObjectState.Active;

        /// <summary>The living adds that shield <paramref name="boss"/>, in the catalog's order (a stable order, so every
        /// party's index points at the same add while they live).</summary>
        public static GameNPC[] LiveRequiredAdds(GameNPC boss, IEnumerable<GameNPC> live)
        {
            Tactic t = For(boss);
            if (t is not { HasRequiredAdds: true } || !Present(boss)) return [];
            return live.Where(n => Present(n) && n != boss && n.CurrentRegionID == boss.CurrentRegionID &&
                                   Array.IndexOf(t.RequiredAdds, n.GetType().Name) >= 0 &&
                                   boss.IsWithinRadius(n, AddSearchRadius))
                .OrderBy(n => Array.IndexOf(t.RequiredAdds, n.GetType().Name)).ThenBy(n => n.ObjectID).ToArray();
        }

        /// <summary>The required adds the raid must still kill: none once only <see cref="Tactic.KeepAlive"/> are left.</summary>
        public static GameNPC[] ShieldingAdds(GameNPC boss, IReadOnlyList<GameNPC> live)
        {
            GameNPC[] adds = LiveRequiredAdds(boss, live);
            int keep = For(boss)?.KeepAlive ?? 0;
            return adds.Length > keep ? adds : [];
        }

        // ---- ground hazards (Orshom's Fire) -----------------------------------------------------------------------

        /// <summary>A ground hazard's rule, by its class (from every tactic that lists it).</summary>
        public sealed record Hazard(string Class, int Radius, eDamageType? VulnerableTo, string[] Owners);

        private static Dictionary<string, Hazard> _hazards;

        private static Dictionary<string, Hazard> Hazards
        {
            get
            {
                if (_hazards != null) return _hazards;
                var map = new Dictionary<string, Hazard>(StringComparer.Ordinal);
                foreach (Tactic t in ByType.Values.Where(t => t.HasHazards))
                foreach (string h in t.Hazards)
                {
                    eDamageType? type = Enum.TryParse(t.HazardDamageType, true, out eDamageType parsed) ? parsed : null;
                    map[h] = map.TryGetValue(h, out Hazard known)
                        ? known with { Radius = Math.Max(known.Radius, t.HazardRadius), Owners = [.. known.Owners, t.Boss] }
                        : new Hazard(h, t.HazardRadius, type, [t.Boss]);
                }
                return _hazards = map;
            }
        }

        /// <summary>The hazard rule of <paramref name="npc"/>, or null when it is no ground hazard.</summary>
        public static Hazard HazardOf(GameNPC npc) => npc == null ? null : Hazards.GetValueOrDefault(npc.GetType().Name);

        public static bool IsHazard(GameNPC npc) => HazardOf(npc) != null;

        /// <summary>A raider stays this much further out than a hazard's radius (its centre is the NPC's spot; the spell's
        /// radius is measured to the raider, who is not a point).</summary>
        public const int HazardMargin = 150;
        /// <summary>A raider sent out of a hazard goes to this distance from its spot.</summary>
        public const int HazardExitDistance = HazardStepOut;

        /// <summary>Whether <paramref name="at"/> is inside a hazard of <paramref name="radius"/> at <paramref name="hazard"/>,
        /// with the margin (pure): across, and within a floor's height of it.</summary>
        public static bool InsideHazard(Vector3 hazard, int radius, Vector3 at) =>
            Vector2.Distance(new(hazard.X, hazard.Y), new(at.X, at.Y)) < radius + HazardMargin && MathF.Abs(hazard.Z - at.Z) < 300;

        /// <summary>The spot a raider at <paramref name="at"/> steps out to (pure): straight away from the hazard, to
        /// <see cref="HazardExitDistance"/> past its radius, at the raider's height.</summary>
        public static Vector3 HazardExit(Vector3 hazard, int radius, Vector3 at)
        {
            Vector2 d = new(at.X - hazard.X, at.Y - hazard.Y);
            Vector2 dir = d.LengthSquared() < 1 ? new Vector2(1, 0) : Vector2.Normalize(d);
            Vector2 p = new Vector2(hazard.X, hazard.Y) + dir * (radius + HazardExitDistance);
            return new Vector3(p.X, p.Y, at.Z);
        }

        /// <summary>The living boss whose fight <paramref name="hazard"/> belongs to (it fights it instead), or null.</summary>
        public static GameNPC HazardOwner(GameNPC hazard, IEnumerable<GameNPC> live)
        {
            Hazard h = HazardOf(hazard);
            if (h == null || !Present(hazard)) return null;
            return live.Where(b => Present(b) && b != hazard && b.CurrentRegionID == hazard.CurrentRegionID &&
                                   Array.IndexOf(h.Owners, b.GetType().Name) >= 0 && b.IsWithinRadius(hazard, AddSearchRadius))
                .OrderBy(b => b.InCombat ? 0 : 1).ThenBy(b => b.GetDistanceTo(hazard)).FirstOrDefault();
        }

        // Whether a raider has a damaging spell of a type: DealsDamageType (EpicEncounterTactics.Krondon.cs, settable for tests).

        // Live hazards the raid route has seen (RealmRaidDungeonRoute.AdvanceCore), so a raider anywhere near one steps out
        // whatever it is fighting (ThinkBlast) and no battle post is placed in one (RealmRaidFormation).
        private static readonly Dictionary<GameNPC, int> SeenHazards = new();
        private static int _liveHazardCount;

        /// <summary>Notes the hazards among <paramref name="live"/> (a region's NPCs) and forgets the ones gone.</summary>
        public static void ObserveHazards(IEnumerable<GameNPC> live)
        {
            if (Hazards.Count == 0) return;
            lock (SeenHazards)
            {
                foreach (GameNPC gone in SeenHazards.Keys.Where(n => !Present(n)).ToArray()) SeenHazards.Remove(gone);
                foreach (GameNPC npc in live ?? [])
                    if (Present(npc) && HazardOf(npc) is { } h) SeenHazards[npc] = h.Radius;
                System.Threading.Volatile.Write(ref _liveHazardCount, SeenHazards.Count);
            }
        }

        /// <summary>A live hazard <paramref name="at"/> stands in (in <paramref name="region"/>), or null.</summary>
        public static GameNPC HazardAt(ushort region, Vector3 at)
        {
            if (System.Threading.Volatile.Read(ref _liveHazardCount) == 0) return null;
            lock (SeenHazards)
                foreach (var (npc, radius) in SeenHazards)
                    if (Present(npc) && npc.CurrentRegionID == region && InsideHazard(new(npc.X, npc.Y, npc.Z), radius, at))
                        return npc;
            return null;
        }

        /// <summary>The advice for a raider about to attack a ground hazard (pure but for the spell check): a raider that
        /// can hurt it shoots it from outside its radius; anyone else steps out of it and fights its boss.</summary>
        public static Advice DecideHazard(GameLiving raider, GameNPC hazard, IReadOnlyList<GameNPC> live, bool hurtsIt)
        {
            Hazard h = HazardOf(hazard);
            Vector3 spot = new(hazard.X, hazard.Y, hazard.Z), at = new(raider.X, raider.Y, raider.Z);
            Vector3? exit = InsideHazard(spot, h.Radius, at) ? HazardExit(spot, h.Radius, at) : null;
            string kind = h.VulnerableTo is { } t ? $"only {t} hurts it" : "nothing hurts it";
            if (hurtsIt)
                return exit is Vector3 out1
                    ? new(BotAction.StandOff, hazard, out1, $"ground hazard \"{hazard.Name}\": out of its {h.Radius} first, then shoot it")
                    : default;
            if (exit is Vector3 out2)
                return new(BotAction.StandOff, hazard, out2, $"ground hazard \"{hazard.Name}\" ({kind}): out of its {h.Radius}");
            if (HazardOwner(hazard, live) is { } owner)
                return new(BotAction.Retarget, owner, null, $"ground hazard \"{hazard.Name}\" ({kind}): fight \"{owner.Name}\"");
            return new(BotAction.StandOff, hazard, null, $"ground hazard \"{hazard.Name}\" ({kind}): leave it");
        }

        private static HashSet<string> _requiredAddClasses;

        /// <summary>Whether some tactic lists <paramref name="npc"/>'s class as a required add (a cheap check before the
        /// region lookup: BotBrain asks for every target of every autonomous bot).</summary>
        public static bool IsRequiredAddClass(GameNPC npc)
        {
            if (npc == null) return false;
            var set = _requiredAddClasses ??= ByType.Values.SelectMany(t => t.RequiredAdds ?? []).ToHashSet(StringComparer.Ordinal);
            return set.Contains(npc.GetType().Name);
        }

        /// <summary>The living boss <paramref name="npc"/> is a required add of, or null.</summary>
        public static GameNPC RequiredAddOwner(GameNPC npc, IReadOnlyList<GameNPC> live) =>
            !Present(npc) ? null : live.FirstOrDefault(b => Present(b) && b != npc && For(b) is { HasRequiredAdds: true } t &&
                Array.IndexOf(t.RequiredAdds, npc.GetType().Name) >= 0 && b.CurrentRegionID == npc.CurrentRegionID &&
                b.IsWithinRadius(npc, AddSearchRadius));

        /// <summary>The fight with <paramref name="boss"/> is on: it or one of its adds is in combat.</summary>
        public static bool FightOn(GameNPC boss, IReadOnlyList<GameNPC> live) =>
            Present(boss) && (boss.InCombat || LiveRequiredAdds(boss, live).Any(a => a.InCombat));

        /// <summary>The boss whose death kills <paramref name="npc"/>, while it lives.</summary>
        public static GameNPC DiesWithOwner(GameNPC npc, IEnumerable<GameNPC> live)
        {
            Tactic t = For(npc);
            if (t?.DiesWith == null || !Present(npc)) return null;
            return live.FirstOrDefault(n => Present(n) && n.CurrentRegionID == npc.CurrentRegionID && n.GetType().Name == t.DiesWith);
        }

        /// <summary>The living encounter <paramref name="npc"/>'s tactic says to kill first (Orshom Brong for Orylle), or null.</summary>
        public static GameNPC AfterOwner(GameNPC npc, IEnumerable<GameNPC> live)
        {
            Tactic t = For(npc);
            if (t?.After == null || !Present(npc)) return null;
            return live.FirstOrDefault(n => Present(n) && n != npc && n.CurrentRegionID == npc.CurrentRegionID &&
                                            n.GetType().Name == t.After && npc.IsWithinRadius(n, AddSearchRadius));
        }

        /// <summary>The largest radius any tactic gives this hazard.</summary>
        public static int HazardRadiusOf(GameNPC hazard) => hazard == null ? 0 :
            All.Where(t => t.HasHazards && Array.IndexOf(t.Hazards, hazard.GetType().Name) >= 0).Select(t => t.HazardRadius)
                .DefaultIfEmpty(0).Max();

        /// <summary>The raider is the one <paramref name="boss"/> fights, or in its party.</summary>
        public static bool Holds(GameLiving raider, GameNPC boss) => boss?.TargetObject is GameLiving anchor &&
            (anchor == raider || anchor.Group != null && anchor.Group == raider.Group);

        /// <summary>The living hazards of <paramref name="boss"/>'s tactic near it.</summary>
        public static GameNPC[] LiveHazards(GameNPC boss, IEnumerable<GameNPC> live)
        {
            Tactic t = For(boss);
            if (t is not { HasHazards: true } || !Present(boss)) return [];
            return live.Where(n => Present(n) && n != boss && n.CurrentRegionID == boss.CurrentRegionID &&
                                   Array.IndexOf(t.Hazards, n.GetType().Name) >= 0 && boss.IsWithinRadius(n, AddSearchRadius)).ToArray();
        }

        /// <summary>The living ranged adds of <paramref name="boss"/>'s tactic near it, in the catalog's order.</summary>
        public static GameNPC[] LiveRangedAdds(GameNPC boss, IEnumerable<GameNPC> live)
        {
            Tactic t = For(boss);
            if (t?.RangedAdds is not { Length: > 0 } || !Present(boss)) return [];
            return live.Where(n => Present(n) && n != boss && n.CurrentRegionID == boss.CurrentRegionID &&
                                   Array.IndexOf(t.RangedAdds, n.GetType().Name) >= 0 && boss.IsWithinRadius(n, AddSearchRadius))
                .OrderBy(n => Array.IndexOf(t.RangedAdds, n.GetType().Name)).ThenBy(n => n.ObjectID).ToArray();
        }

        /// <summary>The ranged add the whole raid's casters and archers take: the one with the least health left (ties: the
        /// lowest object id), so every caster finishes the same one. Play test duo5: "the one on a healer first" changed with
        /// every newborn's target and the casters still spread over 3-8 of them.</summary>
        public static GameNPC FocusedRangedAdd(GameNPC[] adds, Func<GameNPC, int> healthPercent = null)
        {
            if (adds.Length == 1) return adds[0];
            healthPercent ??= a => a.HealthPercent;
            return adds.OrderBy(healthPercent).ThenBy(a => a.ObjectID).First();
        }

        /// <summary>A ranged add attacking a caster or healer of this raider's own party (its melee peel it off), or null.</summary>
        public static GameNPC PeeledRangedAdd(GameLiving raider, GameNPC[] adds, Func<GameLiving, bool> inParty = null,
            Func<GameLiving, bool> isSoft = null)
        {
            if (raider == null) return null;
            inParty ??= victim => raider.Group?.IsInTheGroup(victim) == true;
            isSoft ??= IsSoftTarget;
            return adds.FirstOrDefault(a => a.TargetObject is GameLiving victim && victim != raider && isSoft(victim) && inParty(victim));
        }

        /// <summary>A healer or a caster (the ones Spindler's newborns kill: duo5, 46 of their 50 kills).</summary>
        private static bool IsSoftTarget(GameLiving living) =>
            IsHealer(living) || living is GameBot bot && BotSpellPower.IsOffensiveCaster(bot);

        private static bool IsHealer(GameLiving living) =>
            BotPartyRoles.IsHealingClass((eCharacterClass)EncounterTargets.ClassId(living));

        /// <summary>Within this far past a hazard's radius a raider steps out of it, to this far past it.</summary>
        public const int HazardStepOut = 300;

        /// <summary>Where a raider at <paramref name="raider"/> steps out of a hazard at <paramref name="hazard"/> of
        /// <paramref name="radius"/>, or null when it is already clear.</summary>
        public static Vector3? HazardEscape(Vector3 raider, Vector3 hazard, int radius)
        {
            float d = Vector2.Distance(new(raider.X, raider.Y), new(hazard.X, hazard.Y));
            if (d > radius + HazardMargin) return null;
            Vector2 dir = d < 1 ? new Vector2(1, 0) : Vector2.Normalize(new Vector2(raider.X - hazard.X, raider.Y - hazard.Y));
            Vector2 at = new Vector2(hazard.X, hazard.Y) + dir * (radius + HazardStepOut);
            return new Vector3(at.X, at.Y, raider.Z);
        }

        /// <summary>A caster or healer this far inside the spread radius of the boss's target moves this far past it.</summary>
        public const int SpreadMargin = 60, SpreadStepOut = 250;

        /// <summary>His target keeps this far from him (he closes to 131 himself) and the other melee stand this far behind
        /// him (bot melee reach is 200): 131-150 + 185 clears a 300 area hit around his target.</summary>
        public const int AnchorHoldDistance = 150, BehindDistance = 185, BehindMargin = 10, BehindReach = 195;

        /// <summary>How far behind him a melee raider stands (pure): far enough that the line from his target clears the area
        /// hit with a little to spare, at least <see cref="BehindDistance"/>, and within bot melee reach (200).</summary>
        public static float BehindFor(float anchorToBoss, int radius) =>
            Math.Clamp(radius + BehindMargin + 10 - anchorToBoss, BehindDistance, BehindReach);

        /// <summary>
        /// The melee position order of a <see cref="Tactic.MeleeBehind"/> boss (Govannon's demon): his target holds
        /// <see cref="AnchorHoldDistance"/> from him; another melee raider holds <see cref="BehindDistance"/> and, while it is
        /// within the area hit's reach of his target, steps to the spot behind him first. Renewed each turn, so a target
        /// swap or a move of his target re-places everyone; null to fight on as it stands.
        /// </summary>
        private static Advice? MeleeBehindAdvice(GameLiving raider, GameNPC boss, GameLiving anchor, int radius, long now)
        {
            Vector3 at = At(raider), anchorAt = At(anchor), bossAt = At(boss);
            if (raider == anchor)
            {
                // In the ring his target keeps the ring's distance (it stood at its spot): 185-190 from him, its Shroud misses
                // the other two spots (about 325 away); at 150 they would lie on its edge (295).
                int hold = For(boss) is { MeleeRing: > 1 } ? BehindDistance : AnchorHoldDistance;
                EncounterMeleeStations.Hold(raider, boss, hold, now);
                EncounterMeleeStations.Apply(raider, boss, hold);
                return null;
            }
            EncounterMeleeStations.Hold(raider, boss, BehindDistance, now);
            EncounterMeleeStations.Apply(raider, boss, BehindDistance);
            if (For(boss) is { MeleeRing: > 1 } ring)
                return MeleeRingAdvice(raider, boss, at, anchorAt, bossAt, ring.MeleeRing, now);
            if (!EncounterMeleeStations.InReach(at, anchorAt, radius, BehindMargin)) return null;
            float anchorToBoss = Vector2.Distance(new(anchorAt.X, anchorAt.Y), new(bossAt.X, bossAt.Y));
            Vector3 spot = EncounterMeleeStations.BehindSpot(bossAt, anchorAt, BehindFor(anchorToBoss, radius), at.Z);
            return new(BotAction.Reposition, boss, spot,
                $"spread: melee behind \"{boss.Name}\", clear of {radius} around \"{anchor.Name}\"");
        }

        /// <summary>The ring spots lie this far from him (between <see cref="BehindDistance"/> and <see cref="BehindReach"/>);
        /// a melee raider further than <see cref="RingTolerance"/> from its spot walks to it.</summary>
        public const int RingRadius = 190, RingTolerance = 90;

        /// <summary>
        /// The melee ring (owner 2026-11-08, Govannon's demon): every melee raider but his target fights him from its party's
        /// spot of <paramref name="spots"/> around him. The ring keeps its orientation for the fight (a target swap moves
        /// nobody but the new target) and moves with him. Null when the raider is at its spot.
        /// </summary>
        private static Advice? MeleeRingAdvice(GameLiving raider, GameNPC boss, Vector3 at, Vector3 anchorAt, Vector3 bossAt,
            int spots, long now)
        {
            int order = EncounterMeleeStations.PartyOrder(raider.Group, now);
            int slot = EncounterMeleeStations.RingSlotFor(order >= 0 ? order : PartyIndex(raider), spots);
            float angle = EncounterMeleeStations.RingAngle(boss, bossAt, anchorAt, now);
            Vector3 spot = EncounterMeleeStations.RingSpot(bossAt, angle, slot, spots, RingRadius, at.Z);
            if (Vector2.Distance(new(at.X, at.Y), new(spot.X, spot.Y)) <= RingTolerance) return null;
            return new(BotAction.Reposition, boss, spot, $"spread: melee ring spot {slot + 1}/{spots} around \"{boss.Name}\"");
        }

        /// <summary>The living is a tank (class role Tank; a pet never is).</summary>
        public static bool IsTankLiving(GameLiving living) =>
            living != null && (living is GameBot || living is not GameNPC { Brain: IControlledBrain }) &&
            EncounterTargets.ClassId(living) is int c and > 0 && BotPartyRoles.For((eCharacterClass)c) == BotPartyRole.Tank;

        /// <summary>
        /// Which raider taunts (pure): the tank nearest to the boss among <paramref name="tanks"/> (those fighting him), when
        /// his target is not a tank; null when it is, or no tank fights him.
        /// </summary>
        public static GameLiving TauntingTank(GameLiving bossTarget, IEnumerable<(GameLiving Tank, float Distance)> tanks,
            Func<GameLiving, bool> isTank = null)
        {
            isTank ??= IsTankLiving;
            if (bossTarget == null || isTank(bossTarget)) return null;
            return tanks.Where(t => t.Tank != null).OrderBy(t => t.Distance).Select(t => t.Tank).FirstOrDefault();
        }

        /// <summary>The tanks within this of the boss that fight him are asked; the nearest taunts.</summary>
        public const int TauntSearchRadius = 1_500;

        private static readonly Dictionary<GameNPC, (long Tick, GameLiving Tank)> TauntPicks = new();

        /// <summary>The raider should taunt <paramref name="boss"/> now (<see cref="Tactic.TanksTaunt"/>): the spread is on, his
        /// target is not a tank, and this raider is the nearest raid tank fighting him (chosen once a second).</summary>
        public static bool TauntDue(GameLiving raider, GameNPC boss, IReadOnlyList<GameNPC> live, long now)
        {
            if (raider == null || For(boss) is not { TanksTaunt: true, SpreadRadius: > 0 } t ||
                t.SpreadWhileModel != 0 && boss.Model != t.SpreadWhileModel || !IsTankLiving(raider))
                return false;
            GameLiving pick;
            lock (TauntPicks)
            {
                if (TauntPicks.TryGetValue(boss, out var hit) && now - hit.Tick < 1_000) return hit.Tank == raider;
            }
            pick = TauntingTank(boss.TargetObject as GameLiving,
                live.OfType<GameBot>().Where(b => b.IsAlive && b.IsAutonomousWorldBot && b.Realm == raider.Realm &&
                        b.TargetObject == boss && IsTankLiving(b) && boss.IsWithinRadius(b, TauntSearchRadius))
                    .Select(b => ((GameLiving)b, (float)boss.GetDistanceTo(b))));
            lock (TauntPicks)
            {
                if (TauntPicks.Count > 64) TauntPicks.Clear();
                TauntPicks[boss] = (now, pick);
            }
            return pick == raider;
        }

        /// <summary>
        /// Where a raider stands clear of an area hit of <paramref name="radius"/> around the boss's target
        /// (<paramref name="anchor"/>), or null when it is already clear or fights in melee. Casters, archers and healers step
        /// out, away from the target. Melee can't: they hit from within 200 of the boss and follow it to 75, and his target
        /// stands as close, so every melee spot is inside a 300 radius (Govannon); they fight on and the healers, now out of
        /// it, keep them up.
        /// </summary>
        public static Vector3? SpreadSpot(Vector3 raider, Vector3 boss, Vector3 anchor, int radius, bool atRange)
        {
            static Vector2 Flat(Vector3 v) => new(v.X, v.Y);
            float d = Vector2.Distance(Flat(raider), Flat(anchor));
            if (!atRange || d > radius + SpreadMargin) return null;
            Vector2 away = d >= 1 ? Vector2.Normalize(Flat(raider) - Flat(anchor))
                : Vector2.Distance(Flat(boss), Flat(anchor)) >= 1 ? Vector2.Normalize(Flat(anchor) - Flat(boss)) : new Vector2(1, 0);
            Vector2 at = Flat(anchor) + away * (radius + SpreadStepOut);
            return new Vector3(at.X, at.Y, raider.Z);
        }

        private static readonly Dictionary<string, System.Reflection.PropertyInfo> Switches = new(StringComparer.Ordinal);

        /// <summary>The <see cref="EpicEncounterMechanics"/> switch of this name is on (no name: on).</summary>
        public static bool SwitchOn(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            System.Reflection.PropertyInfo p;
            lock (Switches)
                if (!Switches.TryGetValue(name, out p))
                    Switches[name] = p = typeof(EpicEncounterMechanics).GetProperty(name,
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            return p?.GetValue(null) is true;
        }

        /// <summary>The class rules of <paramref name="t"/> are in force (its switch is on).</summary>
        public static bool ClassRulesApply(Tactic t) => t?.ClassRules is { Length: > 0 } && SwitchOn(t.ClassRuleSwitch);

        /// <summary>A raider of <paramref name="classId"/> can hurt <paramref name="add"/> under <paramref name="t"/>'s class
        /// rules (an add with no rule, or an unknown class, can be hurt).</summary>
        public static bool ClassCanHurt(Tactic t, GameNPC add, int classId)
        {
            if (!ClassRulesApply(t) || classId <= 0) return true;
            AddClasses rule = t.ClassRules.FirstOrDefault(r => r.Add == add.GetType().Name);
            return rule == null || Array.IndexOf(rule.Classes, (eCharacterClass)classId) >= 0;
        }

        /// <summary>Which add a party takes: its index modulo the adds alive.</summary>
        public static int AssignedIndex(int partyIndex, int liveAdds) => liveAdds <= 0 ? -1 : (int)((uint)partyIndex % (uint)liveAdds);

        /// <summary>The boss is further from its spawn than its tactic allows.</summary>
        public static bool OutsideHome(int fightAtHomeRadius, double distanceFromSpawn) =>
            fightAtHomeRadius > 0 && distanceFromSpawn > fightAtHomeRadius;

        public static bool OutsideHome(GameNPC npc) => For(npc) is { } t && Present(npc) &&
            OutsideHome(t.FightAtHomeRadius, npc.GetDistanceTo(npc.SpawnPoint));

        /// <summary>What the raid route fights instead of <paramref name="chosen"/>: the boss it dies with, or the add that
        /// shields it nearest the raid's front; <paramref name="chosen"/> itself when no tactic applies.</summary>
        public static GameNPC RouteTarget(GameNPC chosen, IReadOnlyList<GameNPC> live, Vector3 front, out string reason)
        {
            reason = null;
            if (!Present(chosen)) return chosen;
            // A dungeon rule's route (DungeonRule.Route: the Crypt Lord's court is pulled as his fight).
            if (DungeonRoute(chosen, live, out string own) is { } owner0 && owner0 != chosen)
            {
                reason = own;
                chosen = owner0;
            }
            // A hazard is never the raid's target: the boss it sits beside is.
            if (IsHazard(chosen))
            {
                if (HazardOwner(chosen, live) is not { } hazardOwner) return chosen;
                reason = $"hazard of \"{hazardOwner.Name}\"";
                chosen = hazardOwner;
            }
            if (DiesWithOwner(chosen, live) is { } owner)
            {
                reason = (reason == null ? "" : reason + "; ") + $"dies-with \"{owner.Name}\"";
                chosen = owner;
            }
            if (AfterOwner(chosen, live) is { } first)
            {
                reason = (reason == null ? "" : reason + "; ") + $"after \"{first.Name}\"";
                chosen = first;
            }
            // A kept add (the last illusion) is not fought: its boss is.
            if (RequiredAddOwner(chosen, live) is { } boss && For(boss).KeepAlive > 0 && ShieldingAdds(boss, live).Length == 0)
            {
                reason = $"keep-alive add of \"{boss.Name}\"";
                return boss;
            }
            GameNPC[] adds = ShieldingAdds(chosen, live);
            // Before the fight is on the raid musters for the boss itself; its adds come once it (or they) fight.
            if (adds.Length == 0 || !FightOn(chosen, live)) return chosen;
            GameNPC nearest = For(chosen).AddsInOrder ? adds[0]
                : adds.OrderBy(a => Vector3.DistanceSquared(front, new Vector3(a.X, a.Y, a.Z))).First();
            reason = (reason == null ? "" : reason + "; ") + $"required-adds of \"{chosen.Name}\" alive={adds.Length}";
            return nearest;
        }

        /// <summary>Fighting <paramref name="current"/> is how the raid fights <paramref name="encounter"/>: it is one of its
        /// living required adds, or the boss it dies with.</summary>
        public static bool Covers(GameNPC encounter, GameNPC current, IReadOnlyList<GameNPC> live) =>
            current != null && For(encounter) != null &&
            (DiesWithOwner(encounter, live) == current || Array.IndexOf(ShieldingAdds(encounter, live), current) >= 0);

        /// <summary>True when <paramref name="npc"/> is one of the required adds of a living boss (the route fights it as
        /// part of that boss's fight: no muster of its own).</summary>
        public static bool IsRequiredAddOfLiveBoss(GameNPC npc, IReadOnlyList<GameNPC> live) =>
            RequiredAddOwner(npc, live) != null;

        public enum BotAction
        {
            /// <summary>No tactic: fight as usual.</summary>
            None,
            /// <summary>Attack another encounter instead (<see cref="Advice.Target"/>).</summary>
            Retarget,
            /// <summary>Don't hit this target; stand at <see cref="Advice.StandAt"/> (under its assigned add) if given.</summary>
            StandOff,
            /// <summary>Don't hit it; it is chasing this raider, who leads it back to its spawn.</summary>
            LeadHome,
            /// <summary>Step to <see cref="Advice.StandAt"/> first (out of an area hit or a hazard), keeping the target and its
            /// aggro, and fight on from there.</summary>
            Reposition
        }

        /// <param name="Call">The party's call for this decision (a combat_calls.lines topic; dungeon rules), or null.</param>
        public readonly record struct Advice(BotAction Action, GameNPC Target, Vector3? StandAt, string Reason, string Call = null);

        /// <summary>
        /// The tactic for one raider about to attack <paramref name="target"/>. <paramref name="partyIndex"/> spreads the
        /// parties over the required adds; <paramref name="canShootFlyers"/> says whether this raider can hit an add in the air.
        /// </summary>
        public static Advice Decide(GameLiving raider, GameNPC target, IReadOnlyList<GameNPC> live, int partyIndex, bool canShootFlyers) =>
            Decide(raider, target, live, partyIndex, canShootFlyers, GameLoop.GameLoopTime);

        /// <param name="classId">The raider's class (for class rules and who fights at range); -1: read it from the raider.</param>
        public static Advice Decide(GameLiving raider, GameNPC target, IReadOnlyList<GameNPC> live, int partyIndex, bool canShootFlyers, long now,
            int classId = -1)
        {
            if (raider == null || !Present(target)) return default;
            if (classId < 0) classId = EpicEncounterRestores.ClassId(raider);
            Vector3 at = new(raider.X, raider.Y, raider.Z);
            // A raid breaking off from the fight (the boss, or the boss of this add or of this hazard) goes to the muster first.
            GameNPC fightOwner = (IsHazard(target) ? HazardOwner(target, live) : RequiredAddOwner(target, live)) ?? target;
            if (BrokenOff(fightOwner, now) is Vector3 rally)
                return new(BotAction.StandOff, target, rally, $"break-off from \"{fightOwner.Name}\" to the muster");
            // Epic wave 2 (EpicEncounterTactics.Signals.cs): announced mechanics (out of a glare, a stun, a breath), the safe
            // spots and the close-range bosses, before any other rule sends the raider anywhere.
            if (DecideWaveTwo(raider, target, live, partyIndex, canShootFlyers, now, classId) is Advice waveTwo) return waveTwo;
            // The encounter's own code rules (dungeon rules, EpicEncounterTactics.DungeonRules.cs) come before the catalog's
            // generic ones, its hazard rule included (cold casters put Orshom's Fire out); null: no opinion.
            if (DungeonAdvice(new RuleContext(raider, target, live, partyIndex, canShootFlyers, now, classId)) is Advice own)
                return own;
            // A hazard (Orshom's Fire) is never fought: the boss it sits beside is, from outside its radius.
            // (1.1.3's rule, after the owner's PC raid burned in it on 2026-11-09: a raider with the one damage type that hurts
            // it shoots it from outside; on 1.2 Krondon's own rule above has already sent the cold casters.)
            if (HazardOf(target) is { } hazardRule)
                return DecideHazard(raider, target, live, hazardRule.VulnerableTo is eDamageType type && DealsDamageType(raider, type));
            if (For(target) is { HasHazards: true } ht)
                foreach (GameNPC hazard in LiveHazards(target, live))
                    if (HazardEscape(at, new(hazard.X, hazard.Y, hazard.Z), ht.HazardRadius) is Vector3 outOfIt)
                        return new(BotAction.Reposition, target, outOfIt, $"out of \"{hazard.Name}\" ({ht.HazardRadius})");
            if (DiesWithOwner(target, live) is { } owner)
                return new(BotAction.Retarget, owner, null, $"dies-with \"{owner.Name}\"");
            // Two bosses fought together: the party this one fights holds it, everyone else kills the other first.
            if (AfterOwner(target, live) is { } first && !Holds(raider, target))
                return new(BotAction.Retarget, first, null, $"after \"{first.Name}\": the party \"{target.Name}\" fights holds it");
            if (RequiredAddOwner(target, live) is { } boss && For(boss).KeepAlive > 0 && ShieldingAdds(boss, live).Length == 0)
                return new(BotAction.Retarget, boss, null, $"keep-alive add of \"{boss.Name}\": fight the boss");
            // Adds that die in order (Aroon's Scaiths): one that is not the next to die is held by the party it fights, and
            // everyone else turns to the next one.
            if (RequiredAddOwner(target, live) is { } orderBoss && For(orderBoss).AddsInOrder && !Holds(raider, target) &&
                ShieldingAdds(orderBoss, live) is { Length: > 0 } ordered && ordered[0] != target)
                return new(BotAction.Retarget, ordered[0], null, $"adds in order: \"{ordered[0].Name}\" dies next");
            if (BlastWithdraw(raider, target, now) is Vector3 safe)
                return new(BotAction.StandOff, target, safe, $"blast at {NextBlast(target)}%: out of {For(target).BlastRadius}");
            // Area hit around the boss's target (Govannon's demon form): everyone outside that target's party stands clear of it.
            if (For(target) is { SpreadRadius: > 0 } st && (st.SpreadWhileModel == 0 || target.Model == st.SpreadWhileModel) &&
                target.TargetObject is GameLiving anchor)
            {
                // Who he fights and where his area hit lands (EpicEncounterTactics.SummonersHall.cs): log lines only.
                TraceSpreadTarget(target, anchor, st.SpreadRadius, now);
                bool healer = classId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)classId);
                bool atRange = canShootFlyers || healer;
                // The melee: fight him from behind, clear of the area hit around his target.
                if (st.MeleeBehind && !atRange && MeleeBehindAdvice(raider, target, anchor, st.SpreadRadius, now) is Advice behind)
                    return behind;
                // Healers keep healing where they stand while melee are still in it (stepping out costs casts they need).
                bool healersHold = healer && st.HealersHoldInSpread && EncounterMeleeStations.AnyInReach(target,
                    new(anchor.X, anchor.Y, anchor.Z), BehindDistance, st.SpreadRadius, BehindMargin, now);
                if (!Holds(raider, target) && !healersHold &&
                    SpreadSpot(at, new(target.X, target.Y, target.Z), new(anchor.X, anchor.Y, anchor.Z), st.SpreadRadius, atRange) is Vector3 spread)
                    return new(BotAction.Reposition, target, spread,
                        $"spread: casters and healers clear of {st.SpreadRadius} around \"{anchor.Name}\"");
            }
            // An add only some classes can hurt (an Earthmender): a raider on one it can't hurt takes one it can, or the boss.
            if (RequiredAddOwner(target, live) is { } classBoss && For(classBoss) is { } ct && ClassRulesApply(ct) &&
                !ClassCanHurt(ct, target, classId))
            {
                GameNPC[] mine = ShieldingAdds(classBoss, live).Where(a => ClassCanHurt(ct, a, classId)).ToArray();
                GameNPC to = mine.Length > 0 ? mine[AssignedIndex(partyIndex, mine.Length)] : classBoss;
                return new(BotAction.Retarget, to, null, $"class rule: \"{target.Name}\" takes no damage from this class; " +
                    (mine.Length > 0 ? $"one it can hurt ({mine.Length})" : "none it can hurt: the boss"));
            }
            // Casters and archers kill these adds while the rest stay on the boss.
            GameNPC[] rangedAdds = LiveRangedAdds(target, live);
            if (rangedAdds.Length > 0)
            {
                Tactic rt = For(target);
                LogRangedAdds(target, rangedAdds.Length, now);
                // Area casters: on the clump of the boss and her adds (Spindler's spot) where their area spell hits the most.
                if (rt.AreaOnClump && AreaClumpAdvice(raider, target, rangedAdds, now) is Advice clump) return clump;
                if (rt.AddParties > 0)
                {
                    // Newborn-only parties take the adds (healers keep their party up); the rest stay on the boss.
                    if (!(classId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)classId)) &&
                        IsAddParty(raider, rt.AddParties, now) && AddPartyTarget(rangedAdds) is { } newborn)
                        return new(BotAction.Retarget, newborn, null,
                            $"add party: ranged-add {Array.IndexOf(rangedAdds, newborn) + 1}/{rangedAdds.Length} of \"{target.Name}\"" +
                            (newborn.TargetObject is GameLiving on ? $" (on {on.Name})" : ""));
                    rangedAdds = [];
                }
            }
            if (rangedAdds.Length > 0)
            {
                bool focus = For(target).FocusRangedAdds;
                if (canShootFlyers)
                {
                    GameNPC add = focus ? FocusedRangedAdd(rangedAdds) : rangedAdds[AssignedIndex(partyIndex, rangedAdds.Length)];
                    return new(BotAction.Retarget, add, null,
                        $"ranged-add {Array.IndexOf(rangedAdds, add) + 1}/{rangedAdds.Length} of \"{target.Name}\"" +
                        (focus ? " (focus: the whole raid's casters and archers on one)" : ""));
                }
                if (focus && PeeledRangedAdd(raider, rangedAdds) is { } peel)
                    return new(BotAction.Retarget, peel, null,
                        $"peel: \"{peel.Name}\" is on {(peel.TargetObject as GameLiving)?.Name} of this party");
            }
            GameNPC[] adds = ShieldingAdds(target, live);
            if (adds.Length > 0 && For(target) is { } at2 && ClassRulesApply(at2))
            {
                GameNPC[] mine = adds.Where(a => ClassCanHurt(at2, a, classId)).ToArray();
                // A class none of the adds take damage from fights the boss (keeps it busy) instead.
                if (mine.Length == 0) adds = [];
                else adds = mine;
            }
            if (adds.Length > 0)
            {
                GameNPC add = For(target).AddsInOrder ? adds[0] : adds[AssignedIndex(partyIndex, adds.Length)];
                bool flying = (add.Flags & GameNPC.eFlags.FLYING) != 0 &&
                              AutonomousPveTargetPolicy.IsUnreachableFlyer(add.Flags, add.Z, raider.Z);
                Vector3? addStand = flying ? StandFor(add) : null;
                if (!flying || canShootFlyers)
                {
                    // A shooter out of reach of its add walks to that add's own floor stand first, not under it.
                    if (addStand is Vector3 addFloor && Vector3.Distance(at, new(add.X, add.Y, add.Z)) > RealmRaidDungeonRoute.RequiredAddRangedReach &&
                        Vector3.Distance(at, addFloor) > StandArrivalRadius)
                        return new(BotAction.StandOff, add, addFloor,
                            $"required-add {Array.IndexOf(adds, add) + 1}/{adds.Length} of \"{target.Name}\": to its floor stand");
                    return new(BotAction.Retarget, add, null, $"required-add {Array.IndexOf(adds, add) + 1}/{adds.Length} of \"{target.Name}\"");
                }
                return new(BotAction.StandOff, add, StandFor(target) ?? new Vector3(add.X, add.Y, raider.Z),
                    $"required-add {Array.IndexOf(adds, add) + 1}/{adds.Length} of \"{target.Name}\" is out of melee reach");
            }
            // A raider whose own target is a flying required add it can't reach or shoot (melee on one of Scurceol Hyrde's orbs:
            // PC 2026-11-09, AUTONOMOUS_ROUTE_RECOVERY goal="Lyft Miht" pathStatus=PartialPathFound) holds at its boss's floor
            // stand instead of walking under it; only the boss's own target was steered before.
            if (!canShootFlyers && RequiredAddOwner(target, live) is { } addOwner && (target.Flags & GameNPC.eFlags.FLYING) != 0 &&
                AutonomousPveTargetPolicy.IsUnreachableFlyer(target.Flags, target.Z, raider.Z))
                return new(BotAction.StandOff, target, StandFor(addOwner) ?? StandFor(target) ?? new Vector3(raider.X, raider.Y, raider.Z),
                    $"required-add of \"{addOwner.Name}\" is out of melee reach: hold at the floor stand");
            if (OutsideHome(target) && !AutonomousTetherReset.IsOutOfTether(target))
                return new(target.TargetObject == raider ? BotAction.LeadHome : BotAction.StandOff, target, null,
                    $"fight-at-home {(int)target.GetDistanceTo(target.SpawnPoint)}>{For(target).FightAtHomeRadius}");
            return default;
        }

        // ---- ranged stands (required adds fought from the floor) -----------------------------------------------------

        private static readonly Dictionary<GameNPC, Vector3> Stands = new();

        /// <summary>The floor spot the raid route fights <paramref name="boss"/>'s required adds from
        /// (RealmRaidDungeonRoute.TryRangedStand): raiders who can't hit an add in the air wait there, not under it. Each required
        /// add also has its own (keyed by the add): its party's shooters go there when out of reach of it.</summary>
        /// <summary>A raider within this of its stand is there (TryHandle walks it there from farther).</summary>
        public const int StandArrivalRadius = 300;

        public static void SetStand(GameNPC boss, Vector3 stand)
        {
            lock (Stands)
            {
                if (Stands.Count > 256) Stands.Clear();
                Stands[boss] = stand;
            }
        }

        public static Vector3? StandFor(GameNPC boss)
        {
            lock (Stands) return boss != null && Stands.TryGetValue(boss, out Vector3 at) ? at : null;
        }

        // ---- break-off (an early pull answered by pulling back) -----------------------------------------------------

        private static readonly Dictionary<GameNPC, (Vector3 Rally, long Until)> BreakOffs = new();

        /// <summary>The raid pulls back from <paramref name="boss"/> to <paramref name="rally"/> until <paramref name="until"/>:
        /// raiders stop fighting it (and its adds) and walk to the muster, so it leashes home and resets.</summary>
        public static void BreakOff(GameNPC boss, Vector3 rally, long until)
        {
            lock (BreakOffs) BreakOffs[boss] = (rally, until);
        }

        public static void EndBreakOff(GameNPC boss)
        {
            lock (BreakOffs) BreakOffs.Remove(boss);
        }

        public static Vector3? BrokenOff(GameNPC boss, long now)
        {
            lock (BreakOffs)
            {
                if (boss == null || !BreakOffs.TryGetValue(boss, out var b)) return null;
                if (now < b.Until && Present(boss)) return b.Rally;
                BreakOffs.Remove(boss);
                return null;
            }
        }

        public enum EarlyPull { Commit, Disengage }

        /// <summary>
        /// A boss that starts fighting the raid before its muster (Aidon the Archwizard, 2026-11-05: 31 raiders fought him,
        /// all fell, the other 56 were elsewhere). With half the raid already near, everyone commits at once; otherwise, a
        /// boss that walks home and resets when left can be left (the raid pulls back to its muster); one that doesn't
        /// would chase the raid down, so everyone commits.
        /// </summary>
        public static EarlyPull ChooseEarlyPull(int near, int living, bool resetsWhenLeft) =>
            near * 2 >= living || !resetsWhenLeft ? EarlyPull.Commit : EarlyPull.Disengage;

        /// <summary>An early pull of the same encounter is logged again only after this long.</summary>
        public const long EarlyPullLogRepeatMilliseconds = 10 * 60_000;

        /// <summary>
        /// Whether an early pull is worth a warning line: every pull-back (Disengage) is; a commit only for a raid's final or a
        /// catalogued boss, once per <see cref="EarlyPullLogRepeatMilliseconds"/> (<paramref name="sinceLastMilliseconds"/>
        /// -1: never logged). Darkness Falls named and epic trash that are simply fought go to the debug log.
        /// </summary>
        public static bool EarlyPullWorthLogging(EarlyPull choice, bool isFinal, bool inCatalog, long sinceLastMilliseconds) =>
            choice == EarlyPull.Disengage ||
            (isFinal || inCatalog) && (sinceLastMilliseconds < 0 || sinceLastMilliseconds >= EarlyPullLogRepeatMilliseconds);

        /// <summary>
        /// A pull-back to the muster leaves the boss behind only when it resets when left and the muster lies past its tether
        /// from its spawn; inside the tether it follows the raid there and the fight goes on piecemeal, so the raid commits.
        /// </summary>
        public static bool BreakOffShakesOff(bool resetsWhenLeft, int tetherRange, double musterFromSpawn) =>
            resetsWhenLeft && tetherRange > 0 && musterFromSpawn > tetherRange;

        /// <summary>The catalog says the encounter walks home and resets (a tether and a reset rule); without a catalog row,
        /// a scripted tether under the open-world 4,500 counts.</summary>
        public static bool ResetsWhenLeft(GameNPC npc) => LeashOf(npc) is { } l
            ? l.Resets && (l.TetherRange > 0 || npc.TetherRange > 0)
            : npc != null && npc.TetherRange is > 0 and < AutonomousTetherReset.ScriptedTetherBelow;

        // ---- blasts at health thresholds (Xanxicar's stomp) ---------------------------------------------------------

        /// <summary>Withdraw this many percent above a blast threshold.</summary>
        public const int BlastMarginPercent = 3;
        /// <summary>And stay out this long after the boss crossed it (Xanxicar: 1 s to the announcement, a 6 s cast).</summary>
        public const long BlastAftermathMilliseconds = 9_000;
        private static readonly Dictionary<GameNPC, (int Health, int Crossed, long Tick)> BlastClocks = new();
        // The boss's health loss rate (percent a second), sampled once a second and smoothed, and when it was last seen in a
        // fight: raiders near it withdraw whatever they are fighting (BlastNear).
        private static readonly Dictionary<GameNPC, (int Health, long Tick, double Rate, long Seen)> BlastRates = new();
        private static int _blastBosses;

        /// <summary>The boss's current health loss in percent a second (0 when unknown).</summary>
        public static double BlastRate(GameNPC boss, long now)
        {
            int hp = boss.HealthPercent;
            lock (BlastRates)
            {
                if (!BlastRates.TryGetValue(boss, out var r) || hp > r.Health + 20) r = (hp, now, 0, now);
                if (now - r.Tick >= 1_000)
                {
                    double sample = Math.Max(0, (r.Health - hp) * 1000.0 / (now - r.Tick));
                    r = (hp, now, r.Rate <= 0 ? sample : r.Rate * 0.6 + sample * 0.4, now);
                }
                else r = r with { Seen = now };
                if (BlastRates.Count > 64) BlastRates.Clear();
                BlastRates[boss] = r;
                System.Threading.Volatile.Write(ref _blastBosses, BlastRates.Count);
                return r.Rate;
            }
        }

        /// <summary>The next threshold the boss has not crossed yet at <paramref name="healthPercent"/>, or 0.</summary>
        public static int NextBlastBelow(int[] thresholds, int healthPercent) =>
            thresholds?.Where(t => t < healthPercent).DefaultIfEmpty(0).Max() ?? 0;

        /// <summary>True in the withdraw window: within the margin above a threshold, or just after crossing one.</summary>
        public static bool InBlastWindow(int[] thresholds, int healthPercent, long sinceCrossMilliseconds, double percentPerSecond = 0)
        {
            if (thresholds is not { Length: > 0 }) return false;
            int next = NextBlastBelow(thresholds, healthPercent);
            if (next > 0 && healthPercent - next <= BlastMargin(percentPerSecond)) return true;
            return sinceCrossMilliseconds >= 0 && sinceCrossMilliseconds < BlastAftermathMilliseconds;
        }

        /// <summary>The raid withdraws this long before the boss reaches a blast threshold: a raider beside him walks out of
        /// Xanxicar's 2,500 in about 12 s and the stomp lands 7 s after the threshold (1 s announcement, 6 s cast).</summary>
        public const double BlastLeadSeconds = 10;
        /// <summary>And never closer than this above it (the 1.1.2 margin was 3%).</summary>
        public const int BlastMinimumMarginPercent = 6;

        /// <summary>How far above a threshold the withdraw starts (pure): what the raid takes off him in
        /// <see cref="BlastLeadSeconds"/> at its current rate, at least <see cref="BlastMinimumMarginPercent"/>. PC 1.2 test,
        /// 2026-11-07: 185 Albion raiders took Xanxicar 83% -> 80% in 15 s; 110 died in the stomp, 158 were within 1,000.</summary>
        public static int BlastMargin(double percentPerSecond) =>
            Math.Max(BlastMinimumMarginPercent, (int)Math.Ceiling(Math.Max(0, percentPerSecond) * BlastLeadSeconds));

        private static int NextBlast(GameNPC boss) => NextBlastBelow(For(boss)?.BlastThresholds, boss.HealthPercent + 1);

        /// <summary>How long ago the boss crossed a blast threshold (-1: not recently), tracked from its health.</summary>
        public static long SinceBlastCross(GameNPC boss, long now)
        {
            Tactic t = For(boss);
            if (t is not { HasBlast: true }) return -1;
            int hp = boss.HealthPercent;
            lock (BlastClocks)
            {
                // Tick is stored plus one, so a crossing at tick 0 still counts.
                if (!BlastClocks.TryGetValue(boss, out var c) || hp > c.Health + 20) c = (hp, 0, 0);
                foreach (int threshold in t.BlastThresholds)
                    if (c.Health > threshold && hp <= threshold) c = (c.Health, threshold, now + 1);
                BlastClocks[boss] = (hp, c.Crossed, c.Tick);
                if (BlastClocks.Count > 64) BlastClocks.Clear();
                return c.Tick > 0 ? now - (c.Tick - 1) : -1;
            }
        }

        /// <summary>Where a raider waits out a blast: <see cref="Tactic.BlastRadius"/> plus 200 from the boss, on its own
        /// side. Null when there is no blast now, the raider is already out, or it is in the party the boss is fighting
        /// (that party keeps it busy and takes the hit; the rest of the raid survives it). For a called blast
        /// (<see cref="IsCalledBlast"/>) also null until the raid's call and this raider's reaction delay, and for good when it
        /// lapsed.</summary>
        public static Vector3? BlastWithdraw(GameLiving raider, GameNPC boss, long now)
        {
            Tactic t = For(boss);
            if (t is not { HasBlast: true } || !boss.InCombat ||
                !InBlastWindow(t.BlastThresholds, boss.HealthPercent, SinceBlastCross(boss, now), BlastRate(boss, now)))
                return null;
            bool holds = boss.TargetObject is GameLiving anchor && (anchor == raider || anchor.Group != null && anchor.Group == raider.Group);
            if (holds && !IsCalledBlast(boss)) return null;
            Vector3 b = new(boss.X, boss.Y, boss.Z), r = new(raider.X, raider.Y, raider.Z);
            float d = Vector2.Distance(new(b.X, b.Y), new(r.X, r.Y));
            // The stomp is an area spell on his target (Target ENEMY, Radius 2,500), so it is centred on that raider, not on
            // him (raid-skill container run duo18: 24 raiders who left on time at 40% still died at spots clear of him but
            // not of his target). A raider is out only when it is clear of both, and goes only to spots clear of both.
            Vector3 c = StompCentre(boss, t.BlastRadius);
            float dc = Vector2.Distance(new(c.X, c.Y), new(r.X, r.Y));
            if (d > t.BlastRadius + 100 && dc > t.BlastRadius + 100) return null;
            // 1.2 raid discipline (EpicEncounterTactics.RaidCalls.cs): a called blast waits for the raid's call, then for the
            // raider's own reaction; the party he fights stays in on purpose.
            if (IsCalledBlast(boss))
            {
                BlastCall call = PulseBlastCall(boss, raider, now);
                if (!CalledNow(call, t, boss, now) || !RaiderGoes(boss, call, raider, now, holds)) return null;
            }
            // In a closed cave the straight-out spot is inside the rock (duo6: raiders told to go stood at a median 763 from him
            // and died there): walk out along the floor towards the dungeon's way in instead.
            // Each raider takes the reachable spot clear of the blast nearest to it, so nobody walks past him to get out
            // (PC 2026-11-08: he had followed the raid to the one path spot, 2,944 from his spawn; at 40% the only clear path
            // spots lay behind him and 54 raiders died walking there).
            if (NearestBlastSpot(ClearOf(BlastSpots(boss, t.BlastRadius), c, t.BlastRadius), r) is Vector3 spot) return spot;
            Vector3[] path;
            lock (BlastPaths) BlastPaths.TryGetValue(boss, out path);
            if (BlastRallyOnPath(path, b, t.BlastRadius) is Vector3 rally) return rally;
            Vector2 dir = d < 1 ? new Vector2(1, 0) : Vector2.Normalize(new Vector2(r.X - b.X, r.Y - b.Y));
            Vector2 at = new Vector2(b.X, b.Y) + dir * (t.BlastRadius + 200);
            return new Vector3(at.X, at.Y, r.Z);
        }

        /// <summary>A blasting boss in its withdraw window near <paramref name="raider"/>, and where to wait it out, whatever
        /// the raider is fighting (PC 2026-11-07: raiders on Xanxicar's elites and crystals never got the order, which was
        /// given only to raiders whose target was Xanxicar himself).</summary>
        public static (GameNPC Boss, Vector3 Safe)? BlastNear(GameLiving raider, long now)
        {
            if (raider == null) return null;
            GameNPC[] bosses;
            lock (BlastRates)
                bosses = BlastRates.Where(b => now - b.Value.Seen < 30_000).Select(b => b.Key).ToArray();
            foreach (GameNPC boss in bosses)
            {
                if (!Present(boss) || boss.CurrentRegionID != raider.CurrentRegionID) continue;
                // The call's clock (the call, the landing's count, "back in") runs whether or not this raider is near him.
                if (IsCalledBlast(boss)) PulseBlastCall(boss, raider, now);
                if (BlastWithdraw(raider, boss, now) is Vector3 safe) return (boss, safe);
            }
            return null;
        }

        private static readonly Dictionary<GameNPC, Vector3[]> BlastPaths = new();

        /// <summary>The walked floor path from a blasting boss's spawn to <paramref name="exit"/> (the dungeon's way in), found once
        /// per boss (the raid route asks while it fights him): its withdraw spots lie on it.</summary>
        public static void EnsureBlastPath(GameNPC boss, Vector3? exit = null)
        {
            if (boss == null || For(boss) is not { HasBlast: true }) return;
            lock (BlastPaths)
            {
                if (BlastPaths.ContainsKey(boss)) return;
                if (BlastPaths.Count > 64) BlastPaths.Clear();
                BlastPaths[boss] = [];
            }
            Vector3[] nodes = [];
            var nav = PathfindingProvider.Instance;
            Zone zone = boss.CurrentZone;
            // The way into the boss's own region (duo7: the Crystal Cave raid's route entrance is Avalon City's door, in
            // another region, so no path was found and the old spot in the rock was used again).
            exit ??= WayIn(boss);
            if (exit != null && zone != null && nav.IsAvailable && nav.HasNavmesh(zone))
            {
                var buffer = new WrappedPathfindingNode[512];
                var start = new Vector3(boss.SpawnPoint.X, boss.SpawnPoint.Y, boss.SpawnPoint.Z);
                PathfindingResult result = nav.GetPathStraight(zone, start, exit.Value, nav.DefaultFilters, buffer);
                if (result.Status is PathfindingStatus.PathFound or PathfindingStatus.PartialPathFound)
                    nodes = buffer.Take(Math.Min(result.NodeCount, buffer.Length)).Select(n => n.Position).ToArray();
            }
            lock (BlastPaths) BlastPaths[boss] = nodes;
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_BLAST_PATH boss=\"{boss.Name}\" nodes={nodes.Length} rally=" +
                         (BlastRallyOnPath(nodes, new(boss.SpawnPoint.X, boss.SpawnPoint.Y, boss.SpawnPoint.Z), For(boss).BlastRadius) is Vector3 r
                             ? $"{(int)r.X},{(int)r.Y},{(int)r.Z}" : "none"));
        }

        private static readonly Dictionary<(GameNPC, int, int), Vector3[]> BlastSpotCache = new();

        /// <summary>Floor spots clear of a blast of <paramref name="radius"/> around the boss where he stands now: the clear
        /// nodes of his floor path to the way in, and spots 2,750-3,300 out in 16 directions that have floor and a walked
        /// corridor from his floor. Cached per 256-unit cell of his position.</summary>
        public static Vector3[] BlastSpots(GameNPC boss, int radius)
        {
            var key = (boss, boss.X >> 8, boss.Y >> 8);
            lock (BlastSpotCache)
                if (BlastSpotCache.TryGetValue(key, out Vector3[] cached)) return cached;
            var spots = new List<Vector3>();
            Vector3 b = new(boss.X, boss.Y, boss.Z);
            Vector3[] path;
            lock (BlastPaths) BlastPaths.TryGetValue(boss, out path);
            if (path != null)
                spots.AddRange(path.Where(n => Vector2.Distance(new(n.X, n.Y), new(b.X, b.Y)) >= radius + 200));
            var nav = PathfindingProvider.Instance;
            Zone zone = boss.CurrentZone;
            if (zone != null && nav.IsAvailable && nav.HasNavmesh(zone) &&
                nav.GetClosestPoint(zone, b, 128, 128, 256, nav.DefaultFilters) is Vector3 floor)
            {
                for (int d = 0; d < 16; d++)
                {
                    double a = d * Math.PI / 8;
                    foreach (int r in new[] { radius + 250, radius + 500, radius + 800 })
                    {
                        var probe = new Vector3(b.X + (float)(Math.Cos(a) * r), b.Y + (float)(Math.Sin(a) * r), b.Z);
                        if (nav.GetClosestPoint(zone, probe, 200, 200, 600, nav.DefaultFilters) is not Vector3 at ||
                            Vector2.Distance(new(at.X, at.Y), new(b.X, b.Y)) < radius + 150 ||
                            !AutonomousZoneItinerary.HasCompleteCorridor(nav, zone, floor, at)) continue;
                        spots.Add(at);
                        break;
                    }
                }
            }
            Vector3[] result = spots.ToArray();
            lock (BlastSpotCache)
            {
                if (BlastSpotCache.Count > 256) BlastSpotCache.Clear();
                BlastSpotCache[key] = result;
            }
            return result;
        }

        /// <summary>Where his stomp lands: on his target when it is alive and within the stomp's reach, else on him.</summary>
        private static Vector3 StompCentre(GameNPC boss, int radius) =>
            boss.TargetObject is GameLiving target && target.IsAlive && target.CurrentRegionID == boss.CurrentRegionID &&
            boss.IsWithinRadius(target, radius)
                ? new Vector3(target.X, target.Y, target.Z)
                : new Vector3(boss.X, boss.Y, boss.Z);

        /// <summary>The spots at least <paramref name="radius"/> + 150 from <paramref name="centre"/> (pure): clear of a stomp
        /// centred there. The ring around him leaves 150-250 to spare, so a target beside him rules out only the spots on
        /// its far side.</summary>
        public static Vector3[] ClearOf(Vector3[] spots, Vector3 centre, int radius) =>
            (spots ?? []).Where(s => Vector2.Distance(new(s.X, s.Y), new(centre.X, centre.Y)) >= radius + 150).ToArray();

        /// <summary>The spot nearest to the raider (pure), or null when there is none.</summary>
        public static Vector3? NearestBlastSpot(Vector3[] spots, Vector3 raider)
        {
            if (spots is not { Length: > 0 }) return null;
            Vector3 best = spots[0];
            float bestDistance = float.MaxValue;
            foreach (Vector3 s in spots)
            {
                float d = Vector3.DistanceSquared(s, raider);
                if (d < bestDistance) { bestDistance = d; best = s; }
            }
            return best;
        }

        /// <summary>The arrival point of the zone point into the boss's region nearest to it, or null.</summary>
        private static Vector3? WayIn(GameNPC boss)
        {
            try
            {
                Vector3? best = null;
                float bestDistance = float.MaxValue;
                foreach (DOL.Database.DbZonePoint z in DOLDB<DOL.Database.DbZonePoint>.SelectObjects(
                             DOL.Database.DB.Column("TargetRegion").IsEqualTo(boss.CurrentRegionID)))
                {
                    var at = new Vector3(z.TargetX, z.TargetY, z.TargetZ);
                    float d = Vector2.Distance(new(at.X, at.Y), new(boss.SpawnPoint.X, boss.SpawnPoint.Y));
                    if (d < bestDistance) { bestDistance = d; best = at; }
                }
                return best;
            }
            catch (Exception) { return null; }
        }

        /// <summary>The first spot on <paramref name="path"/> clear of a blast of <paramref name="radius"/> around
        /// <paramref name="boss"/> by 200 (pure), or null (no path, or none of it clear).</summary>
        public static Vector3? BlastRallyOnPath(Vector3[] path, Vector3 boss, int radius)
        {
            if (path is not { Length: > 0 }) return null;
            foreach (Vector3 node in path)
                if (Vector2.Distance(new(node.X, node.Y), new(boss.X, boss.Y)) >= radius + 200)
                    return node;
            return null;
        }

        /// <summary>How long a raider sent out of a blast keeps away (its controller does not walk it back meanwhile).</summary>
        public const long BlastHoldMilliseconds = 4_000;
        private static readonly Dictionary<GameLiving, (Vector3 Spot, long Until)> StandOffHolds = new();
        private static int _standOffHoldCount;

        /// <summary>Keep <paramref name="raider"/> at <paramref name="spot"/> until <paramref name="until"/>.</summary>
        public static void HoldStandOff(GameLiving raider, Vector3 spot, long until)
        {
            if (raider == null) return;
            lock (StandOffHolds)
            {
                if (StandOffHolds.Count > 2_048) StandOffHolds.Clear();
                StandOffHolds[raider] = (spot, until);
                System.Threading.Volatile.Write(ref _standOffHoldCount, StandOffHolds.Count);
            }
        }

        /// <summary>Where a raider waits out a blast, while it must (the controller asks before it moves a raider).</summary>
        public static Vector3? StandOffHold(GameLiving raider, long now)
        {
            if (raider == null || System.Threading.Volatile.Read(ref _standOffHoldCount) == 0) return null;
            lock (StandOffHolds)
            {
                if (!StandOffHolds.TryGetValue(raider, out var h)) return null;
                if (h.Until >= now) return h.Spot;
                StandOffHolds.Remove(raider);
                System.Threading.Volatile.Write(ref _standOffHoldCount, StandOffHolds.Count);
                return null;
            }
        }

        /// <summary>
        /// BotBrain's first look each turn (beside <see cref="ThinkErrand"/>): an autonomous raider near a boss about to blast,
        /// or still holding out of one, stops fighting and walks out (or waits there), whatever it is doing: a healer with no
        /// hostile target, a raider on an add or a crystal, one its controller would walk back to the fight. True: turn used.
        /// </summary>
        public static bool ThinkBlast(GameBot bot)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup || !bot.IsAlive) return false;
            long now = GameLoop.GameLoopTime;
            if (System.Threading.Volatile.Read(ref _blastBosses) == 0 && System.Threading.Volatile.Read(ref _standOffHoldCount) == 0 &&
                System.Threading.Volatile.Read(ref _liveHazardCount) == 0)
                return false;
            Vector3? spot = null;
            // Standing in a ground hazard (Orshom's Fire): out of it first, whatever the bot fights (a boss can stand beside it).
            if (HazardAt(bot.CurrentRegionID, new(bot.X, bot.Y, bot.Z)) is { } hazard && HazardOf(hazard) is { } rule)
            {
                Vector3 exit = HazardExit(new(hazard.X, hazard.Y, hazard.Z), rule.Radius, new(bot.X, bot.Y, bot.Z));
                if (PathfindingProvider.Instance is { IsAvailable: true } nav && bot.CurrentZone is { } zone && nav.HasNavmesh(zone) &&
                    nav.GetClosestPoint(zone, exit, 96, 96, 256, nav.DefaultFilters) is Vector3 floor)
                    exit = floor;
                HoldStandOff(bot, exit, now + BlastHoldMilliseconds);
                if (bot.TargetObject == hazard)
                {
                    if (bot.IsCasting && bot.castingComponent?.SpellHandler?.Spell?.IsHarmful == true) bot.StopCurrentSpellcast();
                    bot.StopAttack();
                    bot.TargetObject = null;
                }
                LogDecision(bot, hazard, new(BotAction.StandOff, hazard, exit, $"standing in ground hazard \"{hazard.Name}\": out of its {rule.Radius}"));
                spot = exit;
            }
            else if (BlastNear(bot, now) is { } blast)
            {
                spot = blast.Safe;
                HoldStandOff(bot, blast.Safe, now + BlastHoldMilliseconds);
                if (bot.TargetObject == blast.Boss || bot.IsAttacking)
                {
                    if (bot.IsCasting && bot.castingComponent?.SpellHandler?.Spell?.IsHarmful == true) bot.StopCurrentSpellcast();
                    bot.StopAttack();
                    bot.TargetObject = null;
                }
                LogDecision(bot, blast.Boss, new(BotAction.StandOff, blast.Boss, blast.Safe,
                    $"blast at {NextBlast(blast.Boss)}%: out of {For(blast.Boss).BlastRadius}"));
            }
            else spot = StandOffHold(bot, now);
            if (spot is not Vector3 at) return false;
            var point = new Point3D((int)at.X, (int)at.Y, (int)at.Z);
            // A melee follow outlives StopAttack and walks the bot straight back to him (duo8: the 48 stomp victims died a
            // median 66 from him after a 30 s warning, still following him).
            if (bot.FollowTarget != null) bot.StopFollowing();
            if (!bot.IsWithinRadius(point, 150))
            {
                if (!bot.IsMovingOnPath) bot.PathTo(point, bot.MaxSpeed);
            }
            else if (bot.IsMoving) bot.StopMoving();
            return true;
        }

        public static void ResetBlastForTests()
        {
            lock (BlastRates) { BlastRates.Clear(); System.Threading.Volatile.Write(ref _blastBosses, 0); }
            lock (BlastClocks) BlastClocks.Clear();
            lock (BlastPaths) BlastPaths.Clear();
            lock (BlastSpotCache) BlastSpotCache.Clear();
            ResetBlastCalls();
            lock (StandOffHolds) { StandOffHolds.Clear(); System.Threading.Volatile.Write(ref _standOffHoldCount, 0); }
            lock (SeenHazards) { SeenHazards.Clear(); System.Threading.Volatile.Write(ref _liveHazardCount, 0); }
        }

        public static bool CanShootFlyers(GameBot bot)
        {
            if (bot?.CharacterClass == null) return false;
            var c = (eCharacterClass)bot.CharacterClass.ID;
            return BotSpellPower.IsOffensiveCaster(bot) || BotRangedCombat.IsDedicatedArcher(c);
        }

        /// <summary>The party's index for <see cref="AssignedIndex"/>: its leader's object id (stable while it lives).</summary>
        public static int PartyIndex(GameLiving member) => member?.Group?.LivingLeader?.ObjectID ?? member?.ObjectID ?? 0;

        private static readonly Dictionary<GameNPC, (long Tick, List<GameNPC> Npcs)> NearbyCache = new();

        /// <summary>The NPCs around a tactic's encounter, looked up at most once a second per encounter (a raid of 200 asks
        /// on every brain turn).</summary>
        private static IReadOnlyList<GameNPC> Nearby(GameNPC target)
        {
            long now = GameLoop.GameLoopTime;
            lock (NearbyCache)
            {
                if (NearbyCache.TryGetValue(target, out var hit) && now - hit.Tick < 1_000) return hit.Npcs;
            }
            List<GameNPC> npcs = target.GetNPCsInRadius((ushort)AddSearchRadius).ToList();
            if (!npcs.Contains(target)) npcs.Add(target);
            lock (NearbyCache)
            {
                if (NearbyCache.Count > 64) NearbyCache.Clear();
                NearbyCache[target] = (now, npcs);
            }
            return npcs;
        }

        private static long _nextLog;
        private static readonly Dictionary<(int, string), long> LastDecision = new();

        /// <summary>
        /// BotBrain's hook, right after it picks <paramref name="target"/>: true when this turn is used (standing off or
        /// leading home); false to fight on, possibly at a new <see cref="GameObject.TargetObject"/>.
        /// </summary>
        public static bool TryHandle(GameBot bot, GameNPC target)
        {
            // A blast about to land near the bot comes first, whatever it fights (Xanxicar's stomp kills anything within 2,500).
            if (ThinkBlast(bot)) return true;
            if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup ||
                For(target) == null && !IsTacticPart(target) && !HasWaveTwoRule(bot, target, GameLoop.GameLoopTime) && !HasDungeonRule(target))
                return false;
            IReadOnlyList<GameNPC> live = Nearby(target);
            DungeonActs(bot, target, live);
            // Tanks taunt him back when his target is not a tank (Govannon's demon); the bot fights on as decided below.
            if (TauntDue(bot, target, live, GameLoop.GameLoopTime) && bot.Brain is BotBrain tankBrain)
                LogTaunt(bot, target, tankBrain.TauntForEncounter(target));
            Advice advice = Decide(bot, target, live, PartyIndex(bot), CanShootFlyers(bot));
            // In a room whose door is barred while its fight is on (Caer Sidi's horsemen), every spot stays inside it.
            if (advice.StandAt is Vector3 sent) advice = advice with { StandAt = KeepInsideFor(advice.Target ?? target, sent) };
            // The party's call for what it plays: the rule's own (Advice.Call) or the one for its reason's tag (mech_*).
            EncounterMechanicCalls.Consider(bot, RequiredAddOwner(target, live) ?? target, advice);
            switch (advice.Action)
            {
                case BotAction.Retarget:
                    // A ground hazard is left for good, not only this turn (it would come back as the bot's highest aggro).
                    if (HazardOf(target) != null) (bot.Brain as BotBrain)?.RemoveFromAggroList(target);
                    bot.TargetObject = advice.Target;
                    (bot.Brain as BotBrain)?.AddToAggroList(advice.Target, 1_000);
                    LogDecision(bot, target, advice);
                    return false;
                case BotAction.StandOff:
                case BotAction.LeadHome:
                    if (bot.IsCasting && bot.castingComponent?.SpellHandler?.Spell?.IsHarmful == true) bot.StopCurrentSpellcast();
                    bot.StopAttack();
                    // The melee follow outlives StopAttack (duo8, Xanxicar): without this the bot walks back to the target.
                    if (bot.FollowTarget == target) bot.StopFollowing();
                    (bot.Brain as BotBrain)?.RemoveFromAggroList(target);
                    bot.TargetObject = null;
                    if (bot.ControlledBrain?.Body?.TargetObject == target) bot.ControlledBrain.Disengage();
                    if (advice.Action == BotAction.LeadHome)
                        bot.PathTo(new Point3D(target.SpawnPoint.X, target.SpawnPoint.Y, target.SpawnPoint.Z), bot.MaxSpeed);
                    else if (advice.StandAt is Vector3 at && !bot.IsWithinRadius(new Point3D((int)at.X, (int)at.Y, (int)at.Z), StandArrivalRadius))
                        bot.PathTo(new Point3D((int)at.X, (int)at.Y, (int)at.Z), bot.MaxSpeed);
                    LogDecision(bot, target, advice);
                    return true;
                case BotAction.Reposition:
                    if (advice.StandAt is not Vector3 spot) return false;
                    if (bot.IsCasting) bot.StopCurrentSpellcast();
                    bot.StopAttack();
                    // The walk to the spot ends the old melee follow (it outlives StopAttack and pulls the bot back onto its
                    // target: duo8, Xanxicar), so the next attack follows at its own (or the order's) distance from there.
                    if (bot.FollowTarget != null && (bot.FollowTarget == advice.Target || bot.FollowTarget == target)) bot.StopFollowing();
                    bot.PathTo(new Point3D((int)spot.X, (int)spot.Y, (int)spot.Z), bot.MaxSpeed);
                    LogDecision(bot, target, advice);
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>One line per bot and decision kind a minute, and at most one a second overall.</summary>
        private static void LogDecision(GameBot bot, GameNPC target, Advice advice)
        {
            long now = GameLoop.GameLoopTime;
            var key = (bot.ObjectID, advice.Action + advice.Target?.Name);
            lock (LastDecision)
            {
                if (LastDecision.TryGetValue(key, out long last) && now - last < 60_000) return;
                if (now < _nextLog) return;
                LastDecision[key] = now;
                _nextLog = now + 1_000;
                if (LastDecision.Count > 4_000) LastDecision.Clear();
            }
            if (Log.IsInfoEnabled)
                Log.Info($"EPIC_TACTIC_BOT bot={bot.Name} region={target.CurrentRegionID} target=\"{target.Name}\" " +
                         $"action={advice.Action} to=\"{advice.Target?.Name}\" reason=\"{advice.Reason}\" " +
                         $"encounter={For(target)?.Encounter}");
        }
    }
}
