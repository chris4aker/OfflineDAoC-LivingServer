using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Legion's raid plan (1.2, Darkness Falls' final; tests only, not seen in game). The script is what the bots must beat,
    /// unchanged: 300,000 health, no damage from attackers more than 1,400 from him, 2.5% back for each raider he kills, a
    /// wave of graspering souls at each 5% step, and every 40-65 s a throw of 8-16 raiders below his top five into
    /// Behemoth's hall (Purgatory). The period's answer (Albion Encounters Guide, VN Boards, Feb 2003): one party takes his
    /// aggro onto the main tank and keeps him there ("he regains health whenever he kills a player"), the raid kills the
    /// lesser monsters, and a raider ported to Behemoth's chamber runs across the room to the portal on the other side.
    /// <list type="bullet">
    /// <item><b>Main tank</b>: while his target is not a tank, the nearest healthy raid tank fighting him taunts him; while his
    /// target is a tank under <see cref="LegionTankSwapPercent"/>, a fresh one does (the wounded one then steps out by the
    /// wounded rule and is healed).</item>
    /// <item><b>Tank healers</b>: the <see cref="LegionTankHealerCount"/> raid healers nearest his target heal it first
    /// under <see cref="LegionTankHealBelow"/>, whatever party it is in (SignalPatient asks <see cref="LegionTankPatient"/>).</item>
    /// <item><b>On his lair</b>: drawn more than <see cref="LegionHoldRadius"/> off his spawn, his tank walks him back to it.</item>
    /// <item><b>Ranged line</b>: casters, archers and healers stand <see cref="LegionRangedDistance"/> from him on the raid's
    /// side (inside his 1,400, clear of his target by the tank's spot at his spawn), spread across the corridor.</item>
    /// <item><b>Soul waves</b>: casters and archers take the souls (catalog RangedAdds); a wave bigger than they kill alone
    /// takes melee parties too, sized to it (<see cref="LegionSoulPartyCount"/>), never the tank's party.</item>
    /// <item><b>The way down</b>: a thrown bot walks across Behemoth's hall on its north side to the zone point and is put
    /// where it puts players (<see cref="LegionWayDown"/>, EncounterDisplacementRecovery's way out); no path, no progress,
    /// too long or Behemoth about to notice it, and it is recalled as before.</item>
    /// <item><b>Behemoth</b> (level 99, 600,000, kills with any hit): no bot hits him or lets its pet; a bot that targets
    /// him stands off.</item>
    /// </list>
    /// The positions come from the script (his lair, 45,000,51,700 r 650), the world (Behemoth's spawn, the zone point "DF
    /// LEGION PURGATORY") and the owner's PC log of build e3b8238 (the raid's muster and where raiders stood); none of them
    /// was checked against the navmesh here.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        // ---- the layout -------------------------------------------------------------------------------------------------

        /// <summary>The raid's side of him: the route's final muster (PC log e3b8238, REALM_EXPEDITION_FINAL_MUSTER at=), at
        /// the south end of the corridor below his lair, where the raid's ranged stood in Midgard's kill.</summary>
        public static readonly Vector3 LegionRaidSide = new(44935, 50510, 15311);
        /// <summary>His lair (Legion.cs ScriptLoaded: "Legion's Lair", 45,000,51,700,15,468, radius 650).</summary>
        public static readonly Vector3 LegionLairCenter = new(45000, 51700, 15468);
        /// <summary>His tank walks him back to his spawn when he is drawn farther than this off it (PC log e3b8238: Midgard
        /// won with him 1,126 off, Hibernia lost its first try with him 1,835-1,869 off, in the middle of its muster).</summary>
        public const int LegionHoldRadius = 1_200;
        /// <summary>His tank is at his spawn when within this of it.</summary>
        public const int LegionTankSpotTolerance = 150;
        /// <summary>The ranged line stands this far from him; a raider at range closer than <see cref="LegionRangedMin"/> or
        /// farther than <see cref="LegionRangedMax"/> walks to its spot (the close-range rule steps in anyone past 1,250).</summary>
        public const int LegionRangedDistance = 1_200, LegionRangedMin = 1_050, LegionRangedMax = 1_250;
        /// <summary>The line spreads over this many spots across the corridor, this far apart (-250 to 250).</summary>
        public const int LegionRangedSlots = 5, LegionRangedSlotSpacing = 125;
        /// <summary>A raider within this of its spot is there.</summary>
        public const int LegionSpotArrival = 120;

        // ---- main tank and tank healers ----------------------------------------------------------------------------

        /// <summary>A tank under this health may take his aggro (it would only feed him).</summary>
        public const int LegionTaunterMinHealth = 60;
        /// <summary>His target, a tank, under this health hands him to a fresh tank (at least <see cref="LegionTankFreshHealth"/>).</summary>
        public const int LegionTankSwapPercent = 35, LegionTankFreshHealth = 80;
        /// <summary>Raid tanks this close to him that fight him are asked to taunt.</summary>
        public const int LegionTauntRadius = 1_500;
        /// <summary>His target is healed first by this many healers, the nearest to it (his hit was about 1,070 every 4 s on a
        /// 1,500-1,900 raider, PC log e3b8238: two hits killed a Hibernian hero; ten healers on Midgard's thane kept it up).</summary>
        public const int LegionTankHealerCount = 6;
        /// <summary>They heal it first under this health (one hit takes over half of it).</summary>
        public const int LegionTankHealBelow = 85;
        public const int LegionTankHealerReach = 2_000;

        // ---- soul waves ---------------------------------------------------------------------------------------------

        /// <summary>A wave up to this many souls is the casters' and archers' alone.</summary>
        public const int LegionSoulsForShooters = 4;
        /// <summary>Above it, one melee party per this many souls more (never more than a third of the raid).</summary>
        public const int LegionSoulsPerParty = 4;

        // ---- Behemoth ---------------------------------------------------------------------------------------------------

        /// <summary>A bot that finds Behemoth its target stands this far off him.</summary>
        public const int BehemothStandOff = 1_000;

        // ---- Behemoth's hall: the way down ------------------------------------------------------------------------------

        /// <summary>Where his throw puts a raider (Legion.cs ThrowPlayer: 48,200,49,566,20,833).</summary>
        public static readonly Vector3 LegionThrowLanding = new(48200, 49566, 20833);
        /// <summary>Behemoth's spawn in the hall (world database, Mob "Behemoth", aggro range 500 from his brain).</summary>
        public static readonly Vector3 BehemothSpawn = new(44490, 49588, 20833);
        /// <summary>The hall's zone point (world database, ZonePoint 78 "DF LEGION PURGATORY") and where it puts a player.</summary>
        public static readonly Vector3 PurgatoryPortal = new(42048, 49652, 20833), PurgatoryPortalTarget = new(45050, 48892, 15460);

        /// <summary>
        /// The legs across the hall (the last one the zone point): east to west along its north side, which keeps the walk
        /// about 550 off Behemoth where it passes him (the hall runs about 49,050-50,130 north to south there, from the
        /// clinging souls' spawns, and he stands in its middle). Not checked against the navmesh: the recovery's danger check
        /// recalls a bot that comes within his aggro range and a margin.
        /// </summary>
        public static readonly Vector3[] LegionWayDownLegs =
        [
            new(46500, 49400, 20833),
            new(45500, 49040, 20833),
            new(43500, 49040, 20833),
            PurgatoryPortal,
        ];

        /// <summary>The way down's time bound (pure): the legs' length from the landing at the recovery's walking speed, half
        /// again, and 10 s (about 63 s).</summary>
        public static long LegionWayDownBudget(Vector3 from, IReadOnlyList<Vector3> legs)
        {
            float length = 0;
            Vector3 at = from;
            foreach (Vector3 leg in legs)
            {
                length += Vector3.Distance(at, leg);
                at = leg;
            }
            return (long)(length / EncounterDisplacementRecovery.AssumedSpeed * 1_500) + 10_000;
        }

        private static EncounterDisplacementRecovery.ExitRoute _legionWayDown;

        /// <summary>Legion's way down for a thrown bot (EncounterDisplacementRecovery's way out for "Legion").</summary>
        public static EncounterDisplacementRecovery.ExitRoute LegionWayDown => _legionWayDown ??= new(
            "across Behemoth's hall to the portal", LegionWayDownLegs, PurgatoryPortalTarget,
            LegionWayDownBudget(LegionThrowLanding, LegionWayDownLegs), ["Behemoth"]);

        // ---- the rules -----------------------------------------------------------------------------------------------

        [DungeonRules]
        private static IEnumerable<DungeonRule> LegionRules() =>
        [
            // After the Darkness Falls file's Legion rule (the wounded step out of his reach): the first opinion wins.
            new("Legion", ["Legion"], LegionPlan)
            {
                Summary = "His tank holds him on his lair and the raid's tanks taunt him back onto a tank; his target's nearest " +
                          "six healers heal it first; casters, archers and healers stand 1,200 from him on the raid's side; a " +
                          "soul wave bigger than four takes melee parties too, sized to it; a thrown bot walks across " +
                          "Behemoth's hall to the portal (recalled if it can't).",
                Act = LegionTauntAct,
            },
            new("Behemoth", ["Behemoth"], BehemothAdvice)
            {
                Summary = "Behemoth (level 99, kills with any hit) is never fought: no bot hits him or sends its pet; one that " +
                          "targets him stands 1,000 off.",
                MustNotHit = (_, _) => true,
                NoPets = _ => true,
            },
        ];

        private static Advice? LegionPlan(RuleContext c)
        {
            GameNPC legion = c.Target;
            if (!legion.InCombat) return null;
            GameLiving his = legion.TargetObject as GameLiving;
            Vector3 boss = At(legion);
            if (his == c.Raider)
                return LegionHoldAdvice(c.At, boss, SpawnOf(legion), IsTankLiving(c.Raider)) is Advice hold
                    ? hold with { Target = legion } : null;
            bool healer = c.ClassId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)c.ClassId);
            GameNPC[] souls = LiveRangedAdds(legion, c.Live);
            if (souls.Length > 0)
            {
                if (!c.AtRange && IsLegionSoulParty(c.Raider, his, souls.Length, c.Now) && AddPartyTarget(souls) is { } soul)
                    return new(BotAction.Retarget, soul, null,
                        $"legion-souls: soul party on {Array.IndexOf(souls, soul) + 1}/{souls.Length}" +
                        (soul.TargetObject is GameLiving on ? $" (on {on.Name})" : ""));
                // Casters and archers: the catalog's ranged-add rule takes them onto the souls.
                if (c.AtRange && !healer) return null;
            }
            // (A paladin heals but tanks: it stays in melee.)
            if (c.AtRange && !IsTankLiving(c.Raider) && LegionRangedAdvice(c.At, boss, LegionRaidSide, c.Raider.ObjectID) is Advice line)
                return line with { Target = legion };
            return null;
        }

        private static Vector3 SpawnOf(GameNPC npc) => npc.SpawnPoint is { } p ? new Vector3(p.X, p.Y, p.Z) : At(npc);

        /// <summary>
        /// His target's move (pure): a tank he fights more than <see cref="LegionHoldRadius"/> off his spawn walks to his spawn
        /// (he follows it, keeping it as his target), so he is fought on his lair with the raid's line south of him; anyone
        /// else he fights stands (running would drag him through the raid), and the taunts give him back to a tank.
        /// </summary>
        public static Advice? LegionHoldAdvice(Vector3 raider, Vector3 boss, Vector3 spawn, bool isTank)
        {
            if (!isTank || Flat(boss, spawn) <= LegionHoldRadius) return null;
            if (Flat(raider, spawn) <= LegionTankSpotTolerance) return null;
            return new(BotAction.Reposition, null, spawn, "legion-hold: his tank takes him back onto his lair");
        }

        /// <summary>The ranged line's spot of a raider (pure): <see cref="LegionRangedDistance"/> from him toward the raid's
        /// side, at one of <see cref="LegionRangedSlots"/> spots across, by its id.</summary>
        public static Vector3 LegionRangedSpot(Vector3 boss, Vector3 raidSide, long id, float z)
        {
            Vector2 d = new(raidSide.X - boss.X, raidSide.Y - boss.Y);
            if (d.Length() < 300) d = new(raidSide.X - LegionLairCenter.X, raidSide.Y - LegionLairCenter.Y);
            Vector2 dir = d.Length() < 1 ? new Vector2(0, -1) : Vector2.Normalize(d);
            Vector2 across = new(-dir.Y, dir.X);
            int slot = (int)(Math.Abs(id) % LegionRangedSlots);
            float lateral = (slot - LegionRangedSlots / 2) * LegionRangedSlotSpacing;
            float along = MathF.Sqrt(LegionRangedDistance * LegionRangedDistance - lateral * lateral);
            Vector2 at = new Vector2(boss.X, boss.Y) + dir * along + across * lateral;
            return new Vector3(at.X, at.Y, z);
        }

        /// <summary>A raider at range out of the band (closer than <see cref="LegionRangedMin"/> or farther than
        /// <see cref="LegionRangedMax"/>) walks to its spot; inside it fights where it stands (pure).</summary>
        public static Advice? LegionRangedAdvice(Vector3 raider, Vector3 boss, Vector3 raidSide, long id)
        {
            float d = Flat(raider, boss);
            if (d >= LegionRangedMin && d <= LegionRangedMax) return null;
            Vector3 spot = LegionRangedSpot(boss, raidSide, id, raider.Z);
            if (Flat(raider, spot) <= LegionSpotArrival) return null;
            return new(BotAction.Reposition, null, spot,
                $"legion-ranged: {LegionRangedDistance} from him on the raid's side (inside his 1,400)");
        }

        // ---- soul parties ---------------------------------------------------------------------------------------------

        /// <summary>How many melee parties a wave of <paramref name="souls"/> takes (pure): none up to
        /// <see cref="LegionSoulsForShooters"/>, then one per <see cref="LegionSoulsPerParty"/> more, at most a third of the
        /// raid's <paramref name="parties"/> (16 souls, 12 parties: 3).</summary>
        public static int LegionSoulPartyCount(int souls, int parties)
        {
            if (souls <= LegionSoulsForShooters || parties <= 1) return 0;
            int wanted = (souls - LegionSoulsForShooters + LegionSoulsPerParty - 1) / LegionSoulsPerParty;
            return Math.Min(wanted, Math.Max(0, parties / 3));
        }

        /// <summary>Which parties take the souls (pure): from the end of the raid's order, parties with a healer first, never
        /// <paramref name="tankParty"/> (the one his target is in; -1: none).</summary>
        public static int[] LegionSoulPartyPicks(IReadOnlyList<bool> hasHealer, int tankParty, int count)
        {
            var picks = new List<int>();
            for (int pass = 0; pass < 2; pass++)
                for (int i = hasHealer.Count - 1; i >= 0 && picks.Count < count; i--)
                    if (i != tankParty && !picks.Contains(i) && (pass == 1 || hasHealer[i])) picks.Add(i);
            return picks.ToArray();
        }

        private static readonly Dictionary<Group, (bool Mine, int Souls, long Tick)> SoulPartyCache = new();

        /// <summary>The raider's party is one of the soul parties for a wave of <paramref name="souls"/> (cached 2 s a party).</summary>
        private static bool IsLegionSoulParty(GameLiving raider, GameLiving hisTarget, int souls, long now)
        {
            Group group = raider?.Group;
            if (group == null || group == hisTarget?.Group) return false;
            lock (SoulPartyCache)
                if (SoulPartyCache.TryGetValue(group, out var hit) && hit.Souls == souls && now - hit.Tick < 2_000) return hit.Mine;
            bool mine = false;
            if (AutonomousRealmRaid.RaidOf(group) is { } raid)
            {
                int count = LegionSoulPartyCount(souls, raid.Parties.Length);
                if (count > 0)
                {
                    bool[] healers = raid.Parties.Select(p => p.GetMembersInTheGroup().Any(m => m.IsAlive && IsHealer(m))).ToArray();
                    int tank = hisTarget?.Group == null ? -1 : Array.IndexOf(raid.Parties, hisTarget.Group);
                    mine = Array.IndexOf(LegionSoulPartyPicks(healers, tank, count), Array.IndexOf(raid.Parties, group)) >= 0;
                }
            }
            lock (SoulPartyCache)
            {
                if (SoulPartyCache.Count > 512) SoulPartyCache.Clear();
                SoulPartyCache[group] = (mine, souls, now);
            }
            return mine;
        }

        // ---- the main tank ------------------------------------------------------------------------------------------

        /// <summary>
        /// Who taunts him now (pure; null: nobody). His target is not a tank: the nearest tank fighting him with at least
        /// <see cref="LegionTaunterMinHealth"/>. His target is a tank under <see cref="LegionTankSwapPercent"/>: the nearest
        /// with at least <see cref="LegionTankFreshHealth"/>. Otherwise his tank keeps him.
        /// </summary>
        public static GameLiving LegionTaunter(GameLiving hisTarget, bool targetIsTank, int targetHealth,
            IEnumerable<(GameLiving Tank, float Distance, int Health)> tanks)
        {
            if (hisTarget == null) return null;
            int need;
            if (!targetIsTank) need = LegionTaunterMinHealth;
            else if (targetHealth < LegionTankSwapPercent) need = LegionTankFreshHealth;
            else return null;
            return tanks.Where(t => t.Tank != null && t.Tank != hisTarget && t.Health >= need)
                .OrderBy(t => t.Distance).Select(t => t.Tank).FirstOrDefault();
        }

        private static readonly Dictionary<(GameNPC, eRealm), (long Tick, GameLiving Pick)> LegionTaunts = new();

        /// <summary>The rule's act: the bot the taunt falls to taunts him (taunt spell where its class has one, taunt style).</summary>
        private static void LegionTauntAct(GameBot bot, GameNPC legion, IReadOnlyList<GameNPC> live)
        {
            if (bot == null || legion == null || !legion.InCombat || !IsTankLiving(bot)) return;
            long now = GameLoop.GameLoopTime;
            GameLiving pick;
            lock (LegionTaunts)
            {
                if (!LegionTaunts.TryGetValue((legion, bot.Realm), out var hit) || now - hit.Tick >= 1_000) hit = (0, null);
                pick = hit.Pick;
                if (hit.Tick == 0)
                {
                    GameLiving his = legion.TargetObject as GameLiving;
                    pick = LegionTaunter(his, IsTankLiving(his), his?.HealthPercent ?? 100,
                        (live ?? []).OfType<GameBot>().Where(b => b.IsAlive && b.IsAutonomousWorldBot && b.Realm == bot.Realm &&
                                b.TargetObject == legion && IsTankLiving(b) && legion.IsWithinRadius(b, LegionTauntRadius) &&
                                !EncounterDisplacementRecovery.IsRecovering(b))
                            .Select(b => ((GameLiving)b, (float)legion.GetDistanceTo(b), (int)b.HealthPercent)));
                    if (LegionTaunts.Count > 64) LegionTaunts.Clear();
                    LegionTaunts[(legion, bot.Realm)] = (now, pick);
                }
            }
            if (pick == bot && bot.Brain is DOL.AI.Brain.BotBrain brain)
                LogTaunt(bot, legion, brain.TauntForEncounter(legion));
        }

        // ---- tank healers -----------------------------------------------------------------------------------------------

        /// <summary>The healers that keep his target up (pure): the <paramref name="count"/> nearest to it, ties by id.</summary>
        public static HashSet<GameLiving> PickTankHealers(IEnumerable<(GameLiving Healer, float Distance)> healers, int count) =>
            healers.Where(h => h.Healer != null).OrderBy(h => h.Distance).ThenBy(h => h.Healer.ObjectID).Take(Math.Max(0, count))
                .Select(h => h.Healer).ToHashSet();

        private static readonly Dictionary<GameNPC, (long Tick, GameLiving Target, HashSet<GameLiving> Healers)> LegionTankHealers = new();

        /// <summary>
        /// The raider this healer heals first because Legion fights it, or null: his target, when the healer is one of its
        /// <see cref="LegionTankHealerCount"/> nearest raid healers (chosen once a second). BotBrain.CheckHeals asks through
        /// <see cref="SignalPatient"/>, which heals it first under <see cref="LegionTankHealBelow"/>.
        /// </summary>
        public static GameLiving LegionTankPatient(GameBot healer, long now)
        {
            if (healer == null || healer.CurrentRegionID != AutonomousDarknessFallsPolicy.RegionId) return null;
            GameNPC legion = healer.GetNPCsInRadius(3_000).FirstOrDefault(n => n is DOL.GS.Scripts.Legion && n.IsAlive && n.InCombat);
            if (legion?.TargetObject is not GameLiving his || !his.IsAlive || his.Realm != healer.Realm) return null;
            HashSet<GameLiving> team;
            lock (LegionTankHealers)
            {
                if (!LegionTankHealers.TryGetValue(legion, out var hit) || hit.Target != his || now - hit.Tick >= 1_000)
                {
                    hit = (now, his, PickTankHealers(his.GetNPCsInRadius(LegionTankHealerReach).OfType<GameBot>()
                        .Where(b => b.IsAlive && b != his && b.Realm == his.Realm && IsHealer(b) &&
                                    !EncounterDisplacementRecovery.IsRecovering(b))
                        .Select(b => ((GameLiving)b, (float)b.GetDistanceTo(his))), LegionTankHealerCount));
                    if (LegionTankHealers.Count > 64) LegionTankHealers.Clear();
                    LegionTankHealers[legion] = hit;
                }
                team = hit.Healers;
            }
            return team.Contains(healer) ? his : null;
        }

        // ---- Behemoth -----------------------------------------------------------------------------------------------------

        private static Advice? BehemothAdvice(RuleContext c) => BehemothStandOffAdvice(c.At, c.Target);

        /// <summary>A bot whose target is Behemoth leaves him (pure but for his position): it stands
        /// <see cref="BehemothStandOff"/> off him, or where it is when already that far.</summary>
        public static Advice BehemothStandOffAdvice(Vector3 raider, GameNPC behemoth)
        {
            Vector3 him = At(behemoth);
            return new(BotAction.StandOff, behemoth,
                Flat(raider, him) < BehemothStandOff - 100 ? PointAway(raider, him, BehemothStandOff) : null,
                "behemoth: never fought (level 99, any hit kills)");
        }

        public static void ResetLegionForTests()
        {
            lock (SoulPartyCache) SoulPartyCache.Clear();
            lock (LegionTaunts) LegionTaunts.Clear();
            lock (LegionTankHealers) LegionTankHealers.Clear();
        }
    }
}
