using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Caer Sidi's encounter rules (wave 2, 1.2): Funus's class and weapon puzzle, the Skeletal Sacristan's patrol, Soul
    /// Reckoner's room, the Crypt Lord's court and Lich Lord Sanguis's last 5%. The catalog rows (Morbus's swarm, the
    /// horsemen's fight-at-home radius, Soul Reckoner's souls) come from docs/encounters/hand-notes.json as before.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        [DungeonRules]
        private static IEnumerable<DungeonRule> CaerSidiRules() =>
        [
            new(nameof(Funus), [nameof(Funus)], FunusWeaponPuzzle)
            {
                Summary = "Only clerics, and scouts, mercenaries, armsmen and infiltrators with their listed bow or crossbow, fight " +
                          "him (any other hit heals him to full): the archers fight with that weapon and never swing; the rest " +
                          "stand 1,400 off, and no bot's pet touches him.",
                MustNotHit = FunusMustNotHit,
                Act = FunusDrawListedWeapon,
                FightsAtRange = FunusFightsAtRange,
                NoPets = _ => EpicEncounterMechanics.AllowFunusWeaponPuzzle,
            },
            new(nameof(DOL.GS.Scripts.SkeletalSacristan), [nameof(DOL.GS.Scripts.SkeletalSacristan)], SacristanAmbush)
            {
                Summary = "Parties wait at stations along his patrol and hit him as he walks past.",
            },
            new(nameof(SoulReckoner), [nameof(SoulReckoner), nameof(ReckonedSoul)], SoulReckonerInRoom)
            {
                Summary = "Out of his room he takes no damage: the raid holds and his target leads him back in.",
            },
            new(nameof(CryptLord), [nameof(CryptLord)], CryptLordMeleeFirst)
            {
                Summary = "His court is cleared first, each monster of it pulled only while his patrol keeps 1,200 from it for " +
                          "35 s (one fighting within 800 of him pulls him and the whole court); in his fight melee take him " +
                          "(70% magic resist) and the casters his court.",
                ClearFirstOf = CourtClearsFirst,
                PullWait = CourtPullWindow,
            },
            new(nameof(Host), [nameof(Host)], HostFocusReal)
            {
                Summary = "His linked monsters (HostBaf) are cleared first while the eight Hosts patrol other floors; then the " +
                          "raid waits at an ambush on his bottom-floor patrol point and every raider fights the real Host.",
                Route = HostRouteToReal,
                RoutesInCombat = true,
                ClearFirstOf = HostBafClearsFirst,
                PullWait = HostPullWindow,
                MusterAnchor = npc => IsOfType(npc, nameof(Host)) ? HostAmbush : null,
            },
            new(nameof(DOL.GS.Scripts.LichLordIlron), [nameof(DOL.GS.Scripts.LichLordIlron)], IlronAddsFirst)
            {
                Summary = "The dark lights and lich's eyes on his floor within 1,000 of him are cleared first; in his fight only " +
                          "the party he fights holds him and everyone else kills the lights, eyes and images first.",
                ClearFirstOf = IlronAddClearsFirst,
            },
            new(nameof(LichLordSanguis), [nameof(LichLordSanguis)], SanguisLastAct)
            {
                Summary = "His last 5% (untargetable): the casters' call.",
            },
            new(nameof(ApocInitializator), [], null)
            {
                Summary = "While a horseman or Apocalypse fights and the room's barrier is on, the raid stays in the room: its " +
                          "muster and every stand-off are inside, 800 from the middle, and the route fights the one in combat.",
                Route = HorsemenRoomHolds,
                RoutesInCombat = true,
            },
        ];

        // ---- Funus: only clerics, and mercenaries, armsmen, infiltrators and scouts with their listed weapon ----------------

        /// <summary>Where a raider Funus doesn't take damage from waits: past his proximity aggro (1,200).</summary>
        public const int FunusStandOff = 1_400;
        /// <summary>A scout this close to Funus would fall back to its melee weapon (which heals him): it steps back first.</summary>
        public const int FunusArcherClose = 450;
        public const int FunusArcherStepBack = 700;

        /// <summary>The raider owns a longbow (in its distance slot), so as a scout it shoots him with it.</summary>
        public static bool HasLongbow(GameLiving raider) =>
            raider is GameBot bot && bot.Inventory?.GetItem(eInventorySlot.DistanceWeapon) is { } bow &&
            bow.Object_Type == (int)eObjectType.Longbow;

        /// <summary>The distance weapon Funus takes damage from for this class (scout: longbow, mercenary: short bow,
        /// armsman and infiltrator: crossbow), or GenericItem when the class has none (clerics hurt him with anything).</summary>
        public static eObjectType FunusListedWeapon(int classId) => (eCharacterClass)classId switch
        {
            eCharacterClass.Scout => eObjectType.Longbow,
            eCharacterClass.Mercenary => eObjectType.Fired,
            eCharacterClass.Armsman or eCharacterClass.Infiltrator => eObjectType.Crossbow,
            _ => eObjectType.GenericItem,
        };

        /// <summary>The bot holds its class's listed weapon (<see cref="FunusListedWeapon"/>) in its distance slot and can use
        /// it (the ability is trained and the level is right), so it can hurt Funus with it. Gamebots earn that weapon like
        /// any other: a plain training one when they learn the weapon line (BotRangedCombat.EnsureStarter, in their class's
        /// own type since 1.2), better ones from loot and merchants.</summary>
        public static bool OwnsFunusWeapon(GameLiving raider)
        {
            if (raider is not GameBot bot) return false;
            eObjectType listed = FunusListedWeapon(EpicEncounterRestores.ClassId(bot));
            if (listed == eObjectType.GenericItem) return false;
            DbInventoryItem item = bot.Inventory?.GetItem(eInventorySlot.DistanceWeapon);
            if (item == null || item.Object_Type != (int)listed) return false;
            try { return BotRangedCombat.CanUse(bot, item); }
            catch (Exception) { return BotRangedCombat.IsUsableWeapon(item); }
        }

        /// <summary>
        /// While <see cref="EpicEncounterMechanics.AllowFunusWeaponPuzzle"/> is on, any hit on Funus from a raider of the
        /// wrong class or weapon (or a pet) heals him to full. So only the ones he takes damage from fight him: clerics (any
        /// attack), and scouts, mercenaries, armsmen and infiltrators who own their listed weapon (longbow, short bow,
        /// crossbow): they draw it (<see cref="FunusDrawListedWeapon"/>), fight with it only (BotBrain asks
        /// <see cref="FightsAtRange"/>; out of endurance they wait, they never swing) and step back when he is on top of them
        /// unless they hold him. Everyone else stops, drops him from its targets and waits past his aggro range, and its pet is
        /// called off (TryHandle) and never takes him (<see cref="PetMustNotHit"/>); their area spells are refused while he
        /// is in the blast (<see cref="MustNotHit"/>).
        /// </summary>
        private static Advice? FunusWeaponPuzzle(RuleContext c)
        {
            if (!EpicEncounterMechanics.AllowFunusWeaponPuzzle) return null;
            // Drawn too far from home the catalog's fight-at-home rule speaks (nobody hits him; his target leads him back).
            if (OutsideHome(c.Target)) return null;
            bool owns = (eCharacterClass)c.ClassId == eCharacterClass.Scout ? HasLongbow(c.Raider) : OwnsFunusWeapon(c.Raider);
            return FunusAdvice(c.Raider, c.Target, c.ClassId, EpicEncounterRestores.WeaponType(c.Raider), owns);
        }

        /// <summary>The class has a listed distance weapon Funus takes damage from (scout, mercenary, armsman, infiltrator).</summary>
        public static bool FunusArcherClass(int classId) => FunusListedWeapon(classId) != eObjectType.GenericItem;

        /// <summary>The Funus rule (pure but for positions): the raider's class and active weapon, and whether it owns its
        /// class's listed distance weapon (<see cref="FunusListedWeapon"/>).</summary>
        public static Advice FunusAdvice(GameLiving raider, GameNPC funus, int classId, eObjectType weapon, bool ownsListedWeapon)
        {
            Vector3 at = At(raider), boss = At(funus);
            if (FunusArcherClass(classId) && (weapon == FunusListedWeapon(classId) || ownsListedWeapon))
            {
                string who = ((eCharacterClass)classId).ToString().ToLowerInvariant();
                // The one he fights can't outrun him (he runs 300): it stands and shoots. Anyone else keeps its distance.
                if (Flat(at, boss) < FunusArcherClose && funus.TargetObject != raider)
                    return new(BotAction.Reposition, funus, PointAway(at, boss, FunusArcherStepBack),
                        $"funus-puzzle: a {who} steps back to shoot, never a melee swing (that heals him)");
                return new(BotAction.None, funus, null, $"funus-puzzle: a {who}'s {FunusListedWeapon(classId)} hurts him");
            }
            if (EpicEncounterRestores.FunusAccepts(classId, weapon))
                return new(BotAction.None, funus, null, "funus-puzzle: this class and weapon hurt him");
            return new(BotAction.StandOff, funus, Flat(at, boss) < FunusStandOff - 100 ? PointAway(at, boss, FunusStandOff) : null,
                "funus-puzzle: this class or weapon would heal him to full; stand clear");
        }

        /// <summary>This bot must not hit Funus at all, not even with an area spell aimed at another monster (the rule's
        /// <see cref="DungeonRule.MustNotHit"/>, asked by <see cref="MustNotHit(GameBot, GameLiving)"/>): his puzzle is on and
        /// the bot isn't one he takes damage from.</summary>
        private static bool FunusMustNotHit(GameBot bot, GameNPC funus)
        {
            if (bot == null || funus is not Funus || !EpicEncounterMechanics.AllowFunusWeaponPuzzle) return false;
            int classId = EpicEncounterRestores.ClassId(bot);
            if (EpicEncounterRestores.FunusAccepts(classId, EpicEncounterRestores.WeaponType(bot))) return false;
            // An archer class that owns its listed weapon may fight him (with that weapon only: FightsAtRange) but no other
            // way: its melee weapon in hand, nothing of it touches him.
            return !(FunusArcherClass(classId) && bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance &&
                     ((eCharacterClass)classId == eCharacterClass.Scout ? HasLongbow(bot) : OwnsFunusWeapon(bot)));
        }

        /// <summary>The bot fights Funus with its distance weapon only (the rule's <see cref="DungeonRule.FightsAtRange"/>):
        /// an archer class that owns its listed weapon, while the puzzle is on. A cleric fights as it likes.</summary>
        private static bool FunusFightsAtRange(GameBot bot, GameNPC funus)
        {
            if (bot == null || funus is not Funus || !EpicEncounterMechanics.AllowFunusWeaponPuzzle) return false;
            int classId = EpicEncounterRestores.ClassId(bot);
            if (!FunusArcherClass(classId)) return false;
            return (eCharacterClass)classId == eCharacterClass.Scout ? HasLongbow(bot) : OwnsFunusWeapon(bot);
        }

        /// <summary>Each turn on Funus (the rule's <see cref="DungeonRule.Act"/>): an archer class that owns its listed weapon
        /// puts its melee weapon away and draws it, so its first hit on him is already the one that hurts.</summary>
        private static void FunusDrawListedWeapon(GameBot bot, GameNPC funus, IReadOnlyList<GameNPC> live)
        {
            if (!FunusFightsAtRange(bot, funus) || bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance) return;
            if (bot.attackComponent?.AttackState == true) bot.StopAttack();
            bot.SwitchWeapon(eActiveWeaponSlot.Distance);
        }

        // ---- Skeletal Sacristan: his patrol at 220, never stopping -------------------------------------------------------

        /// <summary>His loop (SkeletalSacristanBrain's points, in order) and a stop half way along each long leg.</summary>
        public static readonly Vector3[] SacristanPatrol =
        [
            new(31826, 32256, 16750), new(32846, 32250, 16750), new(35357, 32243, 16494), new(35408, 35788, 16494),
            new(33112, 35808, 16750), new(30259, 35800, 16750), new(30238, 32269, 16750),
        ];

        /// <summary>Where the raid's parties wait for him: every point of his loop, and the middle of each leg over 2,400.</summary>
        public static readonly Vector3[] SacristanStations = Stations(SacristanPatrol, 2_400);

        /// <summary>A station's reach: melee hit him as he walks past within this, casters, archers and healers within the other.</summary>
        public const int SacristanMeleeReach = 450, SacristanRangedReach = 1_400;

        private static Vector3[] Stations(Vector3[] loop, float longLeg)
        {
            var stations = new List<Vector3>();
            for (int i = 0; i < loop.Length; i++)
            {
                Vector3 a = loop[i], b = loop[(i + 1) % loop.Length];
                stations.Add(a);
                if (Flat(a, b) > longLeg) stations.Add((a + b) / 2);
            }
            return stations.ToArray();
        }

        /// <summary>The station a party waits at.</summary>
        public static Vector3 SacristanStation(int partyIndex) => SacristanStations[AssignedIndex(partyIndex, SacristanStations.Length)];

        /// <summary>
        /// With his original patrol on (<see cref="EpicEncounterMechanics.AllowSacristanPatrol"/>: he walks his loop at 220
        /// and never stops; he never attacks) a raid that chases him falls behind (bots run about 190). So the raid ambushes
        /// him instead: each party waits at its station along the loop and hits him while he walks past (melee within 450 of
        /// the station, casters, archers and healers within 1,400), then lets him go and waits for his next round (a loop is
        /// about 80 s). Stations cover the whole loop, so someone is always on him and his 60-second rest never comes.
        /// </summary>
        private static Advice? SacristanAmbush(RuleContext c)
        {
            if (!EpicEncounterMechanics.AllowSacristanPatrol) return null;
            bool atRange = c.CanShootFlyers || c.ClassId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)c.ClassId);
            return SacristanAdvice(At(c.Raider), c.Target, c.PartyIndex, atRange);
        }

        public static Advice SacristanAdvice(Vector3 raider, GameNPC sacristan, int partyIndex, bool atRange)
        {
            Vector3 station = SacristanStation(partyIndex);
            int reach = atRange ? SacristanRangedReach : SacristanMeleeReach;
            if (Flat(At(sacristan), station) <= reach)
                return new(BotAction.None, sacristan, null, $"sacristan-ambush: he is passing the station ({reach})");
            return new(BotAction.Reposition, sacristan, station, "sacristan-ambush: wait at the station for his next round");
        }

        // ---- Soul Reckoner: he takes damage only in his room --------------------------------------------------------------

        /// <summary>His room's middle (SoulReckonerBrain.AwayFromRoom).</summary>
        public static readonly Vector3 SoulReckonerRoom = new(28472, 35857, 15370);
        /// <summary>Keep him this much inside the room's edge.</summary>
        public const int SoulReckonerRoomMargin = 200;

        /// <summary>The room's radius in force: 900 with the original small room on, else 1,500 (the 1.1 change).</summary>
        public static int SoulReckonerRoomRadius => EpicEncounterMechanics.AllowSoulReckonerSmallRoom
            ? SoulReckonerBrain.OriginalRoomRadius : SoulReckonerBrain.RoomRadius;

        /// <summary>
        /// Soul Reckoner takes no damage once he is out of his room (900 from its middle with the original room on). Past the
        /// room's radius less 200 the raid stops hitting him (keeping him as its target) and the raider he chases walks back to
        /// the room's middle, so he follows it in. His souls (the catalog's required adds) shield him inside the room.
        /// </summary>
        private static Advice? SoulReckonerInRoom(RuleContext c)
        {
            if (c.Target.CurrentRegionID != 60) return null;
            if (c.Target is SoulReckoner) return SoulReckonerAdvice(c.Raider, c.Target, SoulReckonerRoomRadius);
            // Fighting one of his souls (play test 2026-11-07): he chases a raider that is busy with a soul, and that raider must
            // still lead him back (nobody did, and the souls, which spawn where he stands, drew him further out).
            return c.Find(nameof(SoulReckoner)) is { } reckoner ? SoulLeaderAdvice(c.Raider, reckoner, SoulReckonerRoomRadius) : null;
        }

        /// <summary>
        /// The raid against Soul Reckoner (pure but for positions): inside his room less 200, fight him. The raider he chases
        /// leads him back to the middle as soon as he is past that line. The others keep hitting him while he is still in the
        /// room (he takes damage out to its radius: 900, or 1,500 without the original room), and hold, keeping him, only
        /// beyond it. Play test 2026-11-07: the raid held from 700 on, so he stood 790 out (in the room, taking damage) with
        /// nobody hitting him and nobody leading him back.
        /// </summary>
        public static Advice? SoulReckonerAdvice(GameLiving raider, GameNPC reckoner, int roomRadius)
        {
            float fromMiddle = Flat(At(reckoner), SoulReckonerRoom);
            if (fromMiddle <= roomRadius - SoulReckonerRoomMargin) return null;
            if (reckoner.TargetObject == raider)
                return new(BotAction.StandOff, reckoner, SoulReckonerRoom, "reckoner-room: lead him back into his room");
            if (fromMiddle <= roomRadius) return null;
            return new(BotAction.Reposition, reckoner, At(raider), $"reckoner-room: out of his room ({roomRadius}) he takes no damage; hold");
        }

        /// <summary>A raider fighting one of the souls whom Soul Reckoner chases leads him back once he is past the room's line
        /// (pure but for positions); anyone else goes on with the soul.</summary>
        public static Advice? SoulLeaderAdvice(GameLiving raider, GameNPC reckoner, int roomRadius) =>
            reckoner.TargetObject == raider && Flat(At(reckoner), SoulReckonerRoom) > roomRadius - SoulReckonerRoomMargin
                ? new(BotAction.StandOff, reckoner, SoulReckonerRoom, "reckoner-room: he chases me; lead him back into his room")
                : null;

        // ---- the Crypt Lord: his court comes with him ---------------------------------------------------------------------

        /// <summary>The Crypt Lord's court (PackageID of the monsters around him; 43 in the world database).</summary>
        public const string CryptLordCourt = "CryptLordBaf";

        /// <summary>His court comes from this far once he fights (CryptLordBrain.Think: GetNPCsInRadius(10000)).</summary>
        public const int CryptLordCourtRange = 10_000;
        /// <summary>A court monster fighting within this of him pulls him in (CryptLordBrain.BafMobAggro).</summary>
        public const int CryptLordCourtLink = 800;
        /// <summary>A court monster is pulled only while his walk keeps this far from it (the link plus room for the raid
        /// standing around it and for the court monster stepping towards the raid)...</summary>
        public const int CourtPullClearance = 1_200;
        /// <summary>...for this long (a court monster dies in 20-60 s: PC log e3b8238, "KILLED dark light 1.0m" from the muster).</summary>
        public const int CourtPullHorizonSeconds = 35;
        /// <summary>He walks his patrol at this speed (CryptLordBrain.LordPath: WalkTo(point, 100)).</summary>
        public const int CryptLordWalkSpeed = 100;

        /// <summary>His patrol (CryptLordBrain.LordPath): point1 to point4, then the walk back to his path's spawn point, round
        /// and round while he is out of combat.</summary>
        public static readonly Vector3[] CryptLordPatrol =
        [
            new(28461, 40166, 15373), new(28494, 43144, 15373), new(26751, 43111, 15373), new(26741, 40147, 15373),
            new(24891, 40139, 15372),
        ];

        /// <summary>The patrol point he walks to now, from his script's flags (point1check..point4check, walkback). Settable
        /// for tests.</summary>
        public static Func<int> CryptLordNextPoint = () =>
            NextUnreached([CryptLordBrain.point1check, CryptLordBrain.point2check, CryptLordBrain.point3check,
                CryptLordBrain.point4check, CryptLordBrain.walkback]);

        /// <summary>
        /// A court monster at <paramref name="spot"/> can be fought now (pure): the Crypt Lord, at <paramref name="lord"/>
        /// walking to <see cref="CryptLordPatrol"/>[<paramref name="next"/>], keeps <see cref="CourtPullClearance"/> from it for
        /// the next <see cref="CourtPullHorizonSeconds"/>. Returns how near he comes.
        /// </summary>
        public static float CourtPullApproach(Vector3 lord, int next, Vector3 spot) =>
            ClosestApproach(PredictPatrol(lord, CryptLordPatrol, next, CryptLordWalkSpeed, CourtPullHorizonSeconds), spot);

        /// <summary>The monster is of the Crypt Lord's court (its PackageID).</summary>
        public static bool IsCryptLordCourt(GameNPC npc) => npc != null && npc is not CryptLord && npc.PackageID == CryptLordCourt;

        /// <summary>
        /// PC log e3b8238 (02:56-03:02): the raid dealt him 950 damage in nine minutes and 85 of 87 died, 38 of them to his
        /// dark lights: once he fights, his whole court (43 monsters within 10,000, tether 0, speed 300) joins on his target,
        /// and a court monster fighting within 800 of him pulls him in. The 1.2 rule fought him at once with his court; now
        /// the court is cleared first (the route kills each before it goes for him: <see cref="DungeonRule.ClearFirstOf"/>),
        /// each at a time his patrol keeps clear of it (<see cref="CourtPullWindow"/>).
        /// </summary>
        private static GameNPC CourtClearsFirst(GameNPC npc, IReadOnlyList<GameNPC> live) =>
            IsCryptLordCourt(npc) ? BossNear(npc, live, nameof(CryptLord), CryptLordCourtRange) : null;

        /// <summary>
        /// A court monster is pulled only while the Crypt Lord's patrol (he walks his rectangle at 100 while out of combat)
        /// keeps <see cref="CourtPullClearance"/> from it for <see cref="CourtPullHorizonSeconds"/>: the raid waits (at the
        /// muster) or takes another court monster meanwhile. With him in combat there is nothing left to time.
        /// </summary>
        private static string CourtPullWindow(GameNPC target, IReadOnlyList<GameNPC> live, long now)
        {
            if (!IsCryptLordCourt(target) || BossNear(target, live, nameof(CryptLord), CryptLordCourtRange) is not { } lord ||
                lord.InCombat)
                return null;
            float near = CourtPullApproach(At(lord), CryptLordNextPoint(), At(target));
            return near >= CourtPullClearance ? null
                : $"the Crypt Lord's patrol comes within {(int)near} of it in the next {CourtPullHorizonSeconds} s " +
                  $"(a court monster fighting within {CryptLordCourtLink} pulls him)";
        }

        /// <summary>The court monsters fighting beside him the casters take, within this of him.</summary>
        public const int CryptLordCourtFightRadius = 2_500;

        /// <summary>
        /// His fight (he resists 70% of magic, 40% of melee): the melee take him, and a caster turns to the nearest court
        /// monster fighting beside him while one lives (they are what killed the raid). Without his court in the fight,
        /// everyone fights him.
        /// </summary>
        private static Advice? CryptLordMeleeFirst(RuleContext c)
        {
            if (c.Target is not CryptLord lord || c.ClassId <= 0 || !IsMagicDealer(c.ClassId)) return null;
            GameNPC court = (c.Live ?? []).Where(n => IsCryptLordCourt(n) && Present(n) && n.InCombat &&
                                                      n.CurrentRegionID == lord.CurrentRegionID &&
                                                      lord.IsWithinRadius(n, CryptLordCourtFightRadius))
                .OrderBy(n => c.Raider.GetDistanceTo(n)).ThenBy(n => n.ObjectID).FirstOrDefault();
            return court == null ? null
                : new Advice(BotAction.Retarget, court, null,
                    $"crypt-lord: he resists 70% of magic; casters take his court (\"{court.Name}\"), melee him");
        }

        // ---- the Host: eight copies, one real, a patrol over three floors ------------------------------------------------

        /// <summary>The real Host's PackageID (HostInitializator.PickHost marks one of the eight copies; its death kills the rest).</summary>
        public const string HostRealPackage = "HostReal";
        /// <summary>His linked monsters (every one in the region joins once a Host fights: HostBrain.Think, BafMobs).</summary>
        public const string HostLinkedPackage = "HostBaf";
        /// <summary>A Host walking his patrol aggroes every raider within this (HostBrain.Think: IsMoving, AggroRange 400).</summary>
        public const int HostPatrolAggro = 400;
        /// <summary>A linked monster is pulled only while every Host keeps this far from it (3D: the floors are 1,375 apart)...</summary>
        public const int HostBafClearance = 900;
        /// <summary>...for this long.</summary>
        public const int HostBafHorizonSeconds = 45;
        /// <summary>He walks at this speed (HostBrain.HostPath: WalkTo(point, 100)): his whole loop takes about 330 s.</summary>
        public const int HostWalkSpeed = 100;

        /// <summary>His patrol (HostBrain.HostPath): point1 to point51 down and up the three floors, then his spawn (the
        /// Initializator's spot).</summary>
        public static readonly Vector3[] HostPatrol =
        [
            new(26749, 29730, 17871), new(26180, 30241, 17871), new(25743, 30447, 17861), new(25154, 30151, 17861),
            new(24901, 29673, 17861), new(25376, 29310, 17861), new(25360, 29635, 17866), new(25608, 29967, 17702),
            new(25984, 29902, 17534), new(26121, 29617, 17405), new(25889, 29309, 17251), new(25453, 29390, 17051),
            new(25372, 29775, 16897), new(25946, 29958, 16638), new(26116, 29523, 16495), new(26106, 29305, 16495),
            new(25061, 29335, 16495), new(25046, 30229, 16495), new(25686, 30428, 16495), new(26832, 29793, 16495),
            new(25718, 29012, 16495), new(25358, 29563, 16495), new(25426, 29842, 16406), new(25842, 29983, 16223),
            new(26129, 29643, 16039), new(25714, 29267, 15796), new(25345, 29587, 15588), new(25711, 29995, 15357),
            new(26123, 29645, 15122), new(25796, 28979, 15120), new(24729, 29725, 15119), new(25695, 30592, 15119),
            new(26792, 29721, 15119), new(26102, 29302, 15120), new(26085, 29802, 15192), new(25487, 29903, 15457),
            new(25370, 29483, 15625), new(25873, 29309, 15872), new(26103, 29695, 16058), new(25693, 29975, 16284),
            new(25352, 29538, 16495), new(25775, 29107, 16495), new(26114, 29597, 16495), new(25730, 29985, 16722),
            new(25368, 29610, 16957), new(25705, 29283, 17169), new(26109, 29587, 17393), new(25759, 30023, 17632),
            new(25359, 29578, 17871), new(25809, 29142, 17871), new(26344, 29391, 17871), new(26995, 29733, 17871),
        ];

        /// <summary>The ambush: point32 of his patrol, the bottom floor's south side, the one nearest the way the raid comes
        /// in (PC log e3b8238: the archlich's muster at 25669,31396,15120, 804 from it).</summary>
        public static readonly Vector3 HostAmbush = new(25695, 30592, 15119);
        /// <summary>The raid goes in when the real Host will pass within this of the ambush...</summary>
        public const int HostAmbushReach = 450;
        /// <summary>...between this many seconds from now (the raid crosses the 1,400 from its muster in about 7 s)...</summary>
        public const int HostAmbushLeadSeconds = 6;
        /// <summary>...and this many.</summary>
        public const int HostAmbushWindowSeconds = 25;

        private static System.Reflection.FieldInfo[] _hostPathFlags;

        /// <summary>The patrol point the Hosts walk to now, from HostBrain's flags (path1..path51; then his spawn). They are
        /// static, shared by all eight copies, which walk as one. Settable for tests.</summary>
        public static Func<int> HostNextPoint = () =>
        {
            _hostPathFlags ??= Enumerable.Range(1, 51)
                .Select(i => typeof(HostBrain).GetField("path" + i, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
                .ToArray();
            bool[] reached = _hostPathFlags.Select(f => f?.GetValue(null) is true).Append(false).ToArray();
            return NextUnreached(reached);
        };

        /// <summary>How near a Host at <paramref name="host"/> walking to <see cref="HostPatrol"/>[<paramref name="next"/>]
        /// comes to <paramref name="spot"/> between <paramref name="fromSeconds"/> and <paramref name="toSeconds"/> from now (pure).</summary>
        public static float HostApproach(Vector3 host, int next, Vector3 spot, int fromSeconds, int toSeconds) =>
            ClosestApproach(PredictPatrol(host, HostPatrol, next, HostWalkSpeed, toSeconds).Skip(Math.Max(0, fromSeconds)).ToArray(), spot);

        /// <summary>The real Host among <paramref name="live"/> near <paramref name="near"/>, or null.</summary>
        public static GameNPC RealHost(GameNPC near, IReadOnlyList<GameNPC> live) =>
            near == null ? null : LiveOfType(live, nameof(Host))
                .FirstOrDefault(h => h.PackageID == HostRealPackage && h.CurrentRegionID == near.CurrentRegionID &&
                                     h.IsWithinRadius(near, AddSearchRadius * 2));

        /// <summary>
        /// The Host (PC log e3b8238, 04:42-04:44: 112 of 113 died, 143,000 damage taken, 56,826 dealt spread over the copies):
        /// eight copies of 30,000 health, one of them the real one (PackageID HostReal), whose death kills the rest. A raid that
        /// learned the fight knows which one it is (the bots use the server's mark), so every raider fights that one.
        /// </summary>
        private static Advice? HostFocusReal(RuleContext c)
        {
            if (c.Target.PackageID == HostRealPackage || RealHost(c.Target, c.Live) is not { } real) return null;
            return new Advice(BotAction.Retarget, real, null, "host: only the real Host's death ends the fight; everyone on it");
        }

        /// <summary>The route fights the real Host whichever copy it chose (in combat too: all eight fight together).</summary>
        private static GameNPC HostRouteToReal(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen is not Host || chosen.PackageID == HostRealPackage || RealHost(chosen, live) is not { } real) return null;
            reason = "a copy of the Host: the real one dies for all";
            return real;
        }

        /// <summary>A linked monster of the Host (HostBaf) is killed before him: once a Host fights, every one of them in the
        /// region joins (the archliches, level 83, were what the raid "walked into").</summary>
        private static GameNPC HostBafClearsFirst(GameNPC npc, IReadOnlyList<GameNPC> live)
        {
            if (npc == null || npc is Host || npc.PackageID != HostLinkedPackage) return null;
            return RealHost(npc, live) ?? BossNear(npc, live, nameof(Host), AddSearchRadius * 2);
        }

        /// <summary>
        /// The Host's pull plan. A linked monster is pulled only while every Host keeps <see cref="HostBafClearance"/> from it
        /// for <see cref="HostBafHorizonSeconds"/> (walking, a Host aggroes every raider within 400, and then they all come).
        /// The Hosts wait while a linked monster lives; then the raid waits at its muster for the ambush
        /// (<see cref="HostAmbush"/>, its muster's anchor) and goes in as the real Host comes by it.
        /// </summary>
        private static string HostPullWindow(GameNPC target, IReadOnlyList<GameNPC> live, long now)
        {
            if (target.PackageID == HostLinkedPackage && target is not Host)
            {
                GameNPC[] hosts = LiveOfType(live, nameof(Host)).Where(h => h.CurrentRegionID == target.CurrentRegionID).ToArray();
                if (hosts.Length == 0 || hosts.Any(h => h.InCombat)) return null;
                int next = HostNextPoint();
                float near = hosts.Min(h => HostApproach(At(h), next, At(target), 0, HostBafHorizonSeconds));
                return near >= HostBafClearance ? null
                    : $"the Hosts' patrol comes within {(int)near} of it in the next {HostBafHorizonSeconds} s (they aggro at {HostPatrolAggro})";
            }
            if (target is not Host) return null;
            if (LiveOfType(live, nameof(Host)).Any(h => h.InCombat)) return null;
            int linked = (live ?? []).Count(n => n is not Host && n.PackageID == HostLinkedPackage && Present(n) && !n.InCombat &&
                                                 n.CurrentRegionID == target.CurrentRegionID);
            if (linked > 0) return $"{linked} of his linked monsters live (every one joins his fight): they die first";
            GameNPC real = RealHost(target, live) ?? target;
            if (Vector3.Distance(At(real), HostAmbush) <= HostAmbushReach) return null;
            float due = HostApproach(At(real), HostNextPoint(), HostAmbush, HostAmbushLeadSeconds, HostAmbushWindowSeconds);
            return due <= HostAmbushReach ? null : "the raid waits at its ambush on his patrol for him to come by";
        }

        // ---- Lich Lord Ilron: his lights and eyes first -----------------------------------------------------------------

        /// <summary>The monsters beside him that come into his fight (the world database: three dark lights and three lich's
        /// eyes 357-749 from his spawn on his floor).</summary>
        public static readonly string[] IlronAdds = ["dark light", "lich's eye"];
        /// <summary>Those within this of his spawn (and on his floor) are cleared before him.</summary>
        public const int IlronAddRadius = 1_000;
        /// <summary>A monster within this height of his spawn is on his floor.</summary>
        public const int IlronFloorBand = 300;
        /// <summary>In his fight, his adds within this of him are killed before him.</summary>
        public const int IlronFightRadius = 1_500;

        private static bool IsIlronAddName(string name) => name != null && Array.IndexOf(IlronAdds, name) >= 0;

        /// <summary>The monster stands where his fight would take it in (pure): within <see cref="IlronAddRadius"/> of his
        /// spawn, on his floor.</summary>
        public static bool InIlronsReach(Vector3 add, Vector3 ilronSpawn) =>
            Flat(add, ilronSpawn) <= IlronAddRadius && MathF.Abs(add.Z - ilronSpawn.Z) <= IlronFloorBand;

        /// <summary>
        /// PC log e3b8238: the first attempt was pulled while the raid cleared a lich's eye next to him (03:51:45 the eye's
        /// muster went in, 20 s later he fought), the dark lights and eyes alive dealt about 45% of the damage the raid took,
        /// and it wiped with him at 6%; the second, with them dead, killed him in 4.5 minutes. So they are part of his muster:
        /// the route kills each before it goes for him.
        /// </summary>
        private static GameNPC IlronAddClearsFirst(GameNPC npc, IReadOnlyList<GameNPC> live)
        {
            if (npc == null || !IsIlronAddName(npc.Name) || npc is DOL.GS.Scripts.LichLordIlron) return null;
            return LiveOfType(live, nameof(DOL.GS.Scripts.LichLordIlron)).FirstOrDefault(b => b.CurrentRegionID == npc.CurrentRegionID &&
                InIlronsReach(At(npc), new(b.SpawnPoint.X, b.SpawnPoint.Y, b.SpawnPoint.Z)));
        }

        /// <summary>
        /// His fight with adds alive (a pull that brought him in anyway): the party he fights holds him, everyone else kills
        /// his dark lights, lich's eyes and images (IlronImages, 10,000 health, a 300 mesmerize) first, the most hurt first.
        /// </summary>
        private static Advice? IlronAddsFirst(RuleContext c)
        {
            if (c.Target is not DOL.GS.Scripts.LichLordIlron ilron || c.Holds) return null;
            GameNPC add = (c.Live ?? []).Where(n => Present(n) && n != ilron && n.CurrentRegionID == ilron.CurrentRegionID &&
                                                    (IsIlronAddName(n.Name) || n is IlronImages) && ilron.IsWithinRadius(n, IlronFightRadius))
                .OrderBy(n => n is IlronImages ? 1 : 0).ThenBy(n => n.HealthPercent).ThenBy(n => c.Raider.GetDistanceTo(n))
                .FirstOrDefault();
            return add == null ? null
                : new Advice(BotAction.Retarget, add, null, $"ilron: the party he fights holds him; his \"{add.Name}\" first");
        }

        // ---- Lich Lord Sanguis: his last 5% ---------------------------------------------------------------------------------

        /// <summary>His last 5% (untargetable, finished by spells): the casters' call (the rule itself is FinishedBySpells).</summary>
        private static Advice? SanguisLastAct(RuleContext c) =>
            FinishableUntargetable(c.Target) && c.Raider is GameBot bot && BotSpellPower.IsOffensiveCaster(bot)
                ? new Advice(BotAction.None, c.Target, null, "sanguis-finish: untargetable, casters finish him")
                : null;

        // ---- the horsemen's room: nobody leaves while one of them fights -----------------------------------------------

        /// <summary>A room that bars its door while its fight is on (ApocInitializator.DontAllowLeaveRoom): anyone within
        /// <see cref="DoorRadius"/> of <see cref="Door"/> is moved back to <see cref="Middle"/> (DisplacementKind.Barrier).</summary>
        public sealed record RoomBarrier(string Source, ushort Region, Vector3 Middle, Vector3 Door, int DoorRadius, int InsideRadius,
            string[] Fighters);

        /// <summary>Caer Sidi's final room: the door point and the middle the barrier moves raiders to
        /// (ApocalypseAnd4Horsemans.cs, DontAllowLeaveRoom), and the encounters fought in it.</summary>
        public static readonly RoomBarrier HorsemenRoom = new(nameof(ApocInitializator), 60, new(29469, 25244, 19490),
            new(29459, 26401, 19503), 150, 800,
            [nameof(Fames), nameof(Bellum), nameof(Morbus), nameof(Funus), nameof(Apocalypse), nameof(MorbusSwarm),
             nameof(HarbringerOfFate), nameof(WarIncarnateCrush), nameof(WarIncarnateSlash), nameof(WarIncarnateThrust)]);

        /// <summary>The barrier moves bots: both of its switches are on (AllowDisplacement and AllowBotDisplacement for the
        /// ApocInitializator; shipped on since wave 3, the owner's epic_encounter_switches can turn either off).</summary>
        public static bool BarrierMovesBots(RoomBarrier room) =>
            EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowDisplacement), room.Source) &&
            EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowBotDisplacement), room.Source);

        /// <summary>
        /// A point a raider is sent to while it fights in the room (pure): within <see cref="RoomBarrier.InsideRadius"/> of
        /// the middle, so never in the door's circle (the door is 1,157 from the middle, the circle 150 around it). A raid
        /// sent to a muster or a stand-off past the door would be moved back to the middle as it got there, again and again.
        /// </summary>
        public static Vector3 KeepInside(RoomBarrier room, Vector3 point)
        {
            float d = Flat(point, room.Middle);
            if (d <= room.InsideRadius) return point;
            Vector2 dir = Vector2.Normalize(new Vector2(point.X - room.Middle.X, point.Y - room.Middle.Y));
            Vector2 at = new Vector2(room.Middle.X, room.Middle.Y) + dir * room.InsideRadius;
            return new Vector3(at.X, at.Y, room.Middle.Z);
        }

        /// <summary>The barrier room <paramref name="encounter"/> is fought in, while the barrier moves bots; else null.</summary>
        public static RoomBarrier BarrierRoomOf(GameNPC encounter)
        {
            if (encounter == null) return null;
            RoomBarrier room = HorsemenRoom;
            if (encounter.CurrentRegionID != room.Region || !BarrierMovesBots(room)) return null;
            return FighterIndex(room, encounter) >= 0 ? room : null;
        }

        /// <summary>Where the NPC's class (or a base class) stands in the room's list of fighters; -1: not one of them.</summary>
        private static int FighterIndex(RoomBarrier room, GameNPC npc)
        {
            for (Type t = npc?.GetType(); t != null && t != typeof(GameNPC); t = t.BaseType)
                if (Array.IndexOf(room.Fighters, t.Name) is var i and >= 0) return i;
            return -1;
        }

        /// <summary>Where a raider fighting <paramref name="encounter"/> is sent instead of <paramref name="point"/>: inside its
        /// barrier room (<see cref="KeepInside"/>) when it has one, else the point itself. The raid route's muster and every
        /// stand-off of a tactic go through here.</summary>
        public static Vector3 KeepInsideFor(GameNPC encounter, Vector3 point) =>
            BarrierRoomOf(encounter) is { } room ? KeepInside(room, point) : point;

        /// <summary>
        /// The route doesn't walk the raid out of the room mid-fight (the barrier would move every raider back at the door):
        /// while one of the room's encounters fights and the barrier moves bots, a monster chosen outside the room gives way
        /// to the one in combat.
        /// </summary>
        private static GameNPC HorsemenRoomHolds(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen == null || chosen.CurrentRegionID != HorsemenRoom.Region || BarrierRoomOf(chosen) != null ||
                !BarrierMovesBots(HorsemenRoom))
                return null;
            GameNPC fighting = RoomFightOn(HorsemenRoom, live);
            if (fighting == null) return null;
            reason = $"the room's door is barred while \"{fighting.Name}\" fights";
            return fighting;
        }

        /// <summary>The room encounter in combat (a horseman or Apocalypse first, then their adds), or null.</summary>
        public static GameNPC RoomFightOn(RoomBarrier room, IReadOnlyList<GameNPC> live) =>
            live?.Where(n => Present(n) && n.InCombat && n.CurrentRegionID == room.Region &&
                             Flat(PosOf(n), room.Middle) <= 2_500 && FighterIndex(room, n) >= 0)
                .OrderBy(n => FighterIndex(room, n)).FirstOrDefault();
    }
}
