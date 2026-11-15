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
                          "35 s (one fighting within 800 of him pulls him and the whole court), and again after a skirmish or a " +
                          "wipe before he is pulled; no muster lies within 1,100 of his patrol loop or 500 of a court monster; in " +
                          "his fight melee take him (70% magic resist) and the casters his court.",
                ClearFirstOf = CourtClearsFirst,
                PullWait = CourtPullWindow,
                MusterDangers = CryptLordMusterDangers,
                ClearsAgainBeforePull = true,
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
            // ---- 1.2 big-boss plans (owner 2026-11-14: every big boss is in 1.2; tests only) ----
            new(nameof(Apocalypse), [nameof(Apocalypse), nameof(HarbringerOfFate)], ApocalypsePlan)
            {
                Summary = "While he flies (at 99% and 50%, 30 s each) everyone leaves the 800 under him for a ring 1,050 out " +
                          "(not the door); on the ground the party he fights holds him, everyone else kills the Harbingers of " +
                          "Fate first, and the casters, archers and healers stand at their party's spot on a ring 650 from the " +
                          "room's middle, so a Rain of Fire (600 around one raider) catches one party.",
                Act = ApocalypseFlyAct,
            },
            new(nameof(DOL.GS.Scripts.WarlordDorinakka), [nameof(DOL.GS.Scripts.WarlordDorinakka)], DorinakkaPlan)
            {
                Summary = "He and his 12 crypt knights (DorinakkaBaf, every one in the region joins his fight) are one encounter: " +
                          "no knight is pulled on its own while he lives, the route musters the whole raid for him inside his " +
                          "2,000 tether; in his fight the party he fights holds him (its melee from behind: he parries) and " +
                          "everyone else but the healers kills the knights one at a time, the most hurt first and mesmerized " +
                          "ones last.",
                Route = DorinakkaPackRoute,
            },
            new(nameof(Silencer), [nameof(Silencer)], SilencerPlan)
            {
                Summary = $"Each hit gives him a chance of (distinct attackers / 10)% to resist 99% for 20 s: at most " +
                          $"{SilencerAttackerCap} raiders hit him (so the chance stays 0), no pets; the rest stand by; during his " +
                          "99% only the party he fights stays on him.",
                MustNotHit = SilencerMustNotHit,
                NoPets = _ => true,
            },
            new(nameof(DOL.GS.Scripts.BaneOfHope), [nameof(DOL.GS.Scripts.BaneOfHope)], BaneOfHopePlan)
            {
                Summary = "Fate's Chosen (beside one of the spots he throws raiders to) is killed first; in his fight the casters, " +
                          "archers and healers of every party but his target's stand at their party's spot on a ring 1,000 from " +
                          "him, so his Essence of Souls (600 around the raider it hits) catches one party; melee behind him.",
                ClearFirstOf = FateChosenClearsFirst,
            },
            new(nameof(FateChosen), [nameof(FateChosen)], c => MeleeFromBehind(c, "fate-behind"))
            {
                Summary = "Tank and spank: the party it fights holds it, the other melee fight it from behind.",
            },
            new(nameof(DOL.GS.Scripts.SpectralProvisioner), [nameof(DOL.GS.Scripts.SpectralProvisioner)], ProvisionerAmbush)
            {
                Summary = "Heat, spirit and cold do nothing to him (no bot casts them on him); his level's monsters " +
                          "(ProvisionerBaf) die first; he walks his loop in combat too (195-300), so the parties wait at " +
                          "stations along it and hit him as he passes.",
                Forbids = [eDamageType.Heat, eDamageType.Spirit, eDamageType.Cold],
                ClearFirstOf = ProvisionerLevelClearsFirst,
            },
            new(nameof(LordSanguis), [nameof(LordSanguis)], c => MeleeFromBehind(c, "sanguis-behind"))
            {
                Summary = "His blood mages are the casters' (catalog ranged adds); the melee fight him from behind.",
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

        public static Advice SacristanAdvice(Vector3 raider, GameNPC sacristan, int partyIndex, bool atRange) =>
            PatrolAmbushAdvice(sacristan, SacristanStation(partyIndex), atRange, "sacristan-ambush");

        /// <summary>The ambush of a boss that walks his loop whatever happens (pure but for his position): within reach of the
        /// party's station (melee 450, the rest 1,400) he is fought; otherwise the party waits at its station.</summary>
        public static Advice PatrolAmbushAdvice(GameNPC boss, Vector3 station, bool atRange, string tag)
        {
            int reach = atRange ? SacristanRangedReach : SacristanMeleeReach;
            if (Flat(At(boss), station) <= reach)
                return new(BotAction.None, boss, null, $"{tag}: he is passing the station ({reach})");
            return new(BotAction.Reposition, boss, station, $"{tag}: wait at the station for his next round");
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
        /// <summary>He walks his patrol at this speed (CryptLord.PATROL_SPEED).</summary>
        public const int CryptLordWalkSpeed = CryptLord.PATROL_SPEED;

        /// <summary>His patrol (CryptLord.PatrolPoints, his scripted loop since the OpenDAoC-Core c2a55c5b9 port): point1 to
        /// point4, then the walk back to his path's spawn point, round and round while he is out of combat.</summary>
        public static readonly Vector3[] CryptLordPatrol = LoopFromSecondPoint(CryptLord.PatrolPoints);

        /// <summary>The patrol point <paramref name="lord"/> walks to now, from his scripted loop's current point. Settable for
        /// tests.</summary>
        public static Func<GameNPC, int> CryptLordNextPoint = lord => NextOnScriptedLoop(lord, CryptLordPatrol.Length);

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
            float near = CourtPullApproach(At(lord), CryptLordNextPoint(lord), At(target));
            return near >= CourtPullClearance ? null
                : $"the Crypt Lord's patrol comes within {(int)near} of it in the next {CourtPullHorizonSeconds} s " +
                  $"(a court monster fighting within {CryptLordCourtLink} pulls him)";
        }

        /// <summary>No muster lies this close to his patrol loop while he walks it (a court monster fighting within
        /// <see cref="CryptLordCourtLink"/> of him pulls him, and court monsters chase raiders to their muster; plus the route's
        /// usual margin, RealmRaidDungeonRoute.UntargetedBossMargin).</summary>
        public const int CryptLordMusterClearance = CryptLordCourtLink + 300;
        /// <summary>Nor this close to a court monster out of combat (their aggro range, 200, plus the same margin).</summary>
        public const int CourtMusterClearance = 500;
        /// <summary>His loop is sampled this often for <see cref="CryptLordMusterClearance"/>.</summary>
        public const int CryptLordPatrolSampleStep = 150;

        /// <summary>Points every <see cref="CryptLordPatrolSampleStep"/> along his whole loop, the closing leg included.</summary>
        public static readonly Vector3[] CryptLordPatrolSamples = SampleLoop(CryptLordPatrol, CryptLordPatrolSampleStep);

        /// <summary>Points every <paramref name="step"/> along the closed loop <paramref name="loop"/> (pure).</summary>
        public static Vector3[] SampleLoop(IReadOnlyList<Vector3> loop, float step)
        {
            var points = new List<Vector3>();
            for (int i = 0; i < (loop?.Count ?? 0); i++)
            {
                Vector3 a = loop[i], b = loop[(i + 1) % loop.Count];
                int steps = Math.Max(1, (int)MathF.Ceiling(Vector3.Distance(a, b) / step));
                for (int k = 0; k < steps; k++) points.Add(Vector3.Lerp(a, b, k / (float)steps));
            }
            return points.ToArray();
        }

        /// <summary>
        /// Owner's PC, build 1dc66b6, 2026-11-15 (Albion's sweep raid): after the skirmish of 02:56-02:57 the raid mustered for
        /// him at 27,960,40,280 (1,400 from his spawn along its walk), 115 from his patrol's first leg and 194 from a court
        /// reckoned soul; raiders there fought the forerunners and reckoned souls around it, he walked into it and both
        /// attempts died to his court (03:00:10, 55 fell; 03:04:54, 66 fell; him at 99%). So while he walks his loop no muster,
        /// his own included, lies within <see cref="CryptLordMusterClearance"/> of it, nor within
        /// <see cref="CourtMusterClearance"/> of a court monster out of combat.
        /// </summary>
        private static IEnumerable<(Vector3 At, float Radius)> CryptLordMusterDangers(GameNPC npc, IReadOnlyList<GameNPC> live)
        {
            if (npc is CryptLord)
                return CryptLordPatrolSamples.Select(p => (p, (float)CryptLordMusterClearance));
            if (IsCryptLordCourt(npc) && BossNear(npc, live, nameof(CryptLord), CryptLordCourtRange) is { InCombat: false })
            {
                int aggro = (npc.Brain as StandardMobBrain)?.AggroRange ?? 0;
                return [(At(npc), Math.Max(CourtMusterClearance, aggro + 300))];
            }
            return null;
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
        /// <summary>He walks at this speed (Host.PATROL_SPEED): his whole loop takes about 330 s.</summary>
        public const int HostWalkSpeed = Host.PATROL_SPEED;

        /// <summary>His patrol (Host.PatrolPoints, his scripted loop since the OpenDAoC-Core cadc7b8c0 port): point1 to point51
        /// down and up the three floors, then his spawn (the Initializator's spot).</summary>
        public static readonly Vector3[] HostPatrol = LoopFromSecondPoint(Host.PatrolPoints);

        /// <summary>The ambush: point32 of his patrol, the bottom floor's south side, the one nearest the way the raid comes
        /// in (PC log e3b8238: the archlich's muster at 25669,31396,15120, 804 from it).</summary>
        public static readonly Vector3 HostAmbush = new(25695, 30592, 15119);
        /// <summary>The raid goes in when the real Host will pass within this of the ambush...</summary>
        public const int HostAmbushReach = 450;
        /// <summary>...between this many seconds from now (the raid crosses the 1,400 from its muster in about 7 s)...</summary>
        public const int HostAmbushLeadSeconds = 6;
        /// <summary>...and this many.</summary>
        public const int HostAmbushWindowSeconds = 25;

        /// <summary>The patrol point <paramref name="host"/> walks to now, from his scripted loop's current point (each of the
        /// eight copies walks his own; they start together from the Initializator's spot). Settable for tests.</summary>
        public static Func<GameNPC, int> HostNextPoint = host => NextOnScriptedLoop(host, HostPatrol.Length);

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
                float near = hosts.Min(h => HostApproach(At(h), HostNextPoint(h), At(target), 0, HostBafHorizonSeconds));
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
            float due = HostApproach(At(real), HostNextPoint(real), HostAmbush, HostAmbushLeadSeconds, HostAmbushWindowSeconds);
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
            BarrierRoomOf(encounter) is { } room
                ? room == HorsemenRoom && ApocalypseFlying() && InApocFlyRing(point) ? point : KeepInside(room, point)
                : point;

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

        // ==== 1.2 big-boss plans (owner 2026-11-14: every big boss is in Living Server 1.2; tests only) =================
        // Period source for the plans below: Salaris' Guide to Albion Epic Encounters (VN Boards, Wayback capture 2003-04-07);
        // Allakhazam's bestiary comments (2003); each boss's script is the mechanic actually played.

        // ---- shared pieces (also used by Krondon, Summoner's Hall and Hall of the Corrupt) -----------------------------

        /// <summary>A melee raider within this of a boss that faces another raider, and inside his front arc of
        /// <see cref="FrontArcDegrees"/> either side, steps behind him.</summary>
        public const int BehindCheckReach = 400, FrontArcDegrees = 70;
        /// <summary>It steps to this far behind him (inside bot melee reach, 200).</summary>
        public const int BehindStepDistance = 130;

        /// <summary>Whether <paramref name="raider"/> stands in the front arc of a boss at <paramref name="boss"/> facing
        /// <paramref name="facing"/> (pure): within <paramref name="halfAngleDegrees"/> of the line from him to it.</summary>
        public static bool InFrontArc(Vector3 boss, Vector3 facing, Vector3 raider, double halfAngleDegrees)
        {
            Vector2 f = new(facing.X - boss.X, facing.Y - boss.Y), r = new(raider.X - boss.X, raider.Y - boss.Y);
            if (f.LengthSquared() < 1 || r.LengthSquared() < 1) return false;
            double cos = Vector2.Dot(Vector2.Normalize(f), Vector2.Normalize(r));
            return cos >= Math.Cos(halfAngleDegrees * Math.PI / 180);
        }

        /// <summary>
        /// A boss that parries, blocks or evades from the front (Warlord Dorinakka, Fate's Chosen, Lord Sanguis, Lord Gildas,
        /// Govannon in his own form; the period's "stand behind them", VN 2003): a melee raider that is not his target and
        /// stands in his front arc within <see cref="BehindCheckReach"/> steps to <see cref="BehindStepDistance"/> behind him
        /// on the line from his target, then fights on from there. Null otherwise.
        /// </summary>
        private static Advice? MeleeFromBehind(RuleContext c, string tag)
        {
            if (c.AtRange || c.Target.TargetObject is not GameLiving anchor || anchor == c.Raider) return null;
            Vector3 boss = At(c.Target), at = c.At;
            if (Flat(at, boss) > BehindCheckReach || !InFrontArc(boss, At(anchor), at, FrontArcDegrees)) return null;
            Vector3 spot = EncounterMeleeStations.BehindSpot(boss, At(anchor), BehindStepDistance, at.Z);
            return new(BotAction.Reposition, c.Target, spot, $"{tag}: he parries and blocks in front; melee from behind him");
        }

        /// <summary>
        /// The party the boss fights keeps him; every other raider (healers excepted; <paramref name="rangedOnly"/>: casters and
        /// archers only) turns to the boss's add that <paramref name="isAdd"/> picks within <paramref name="radius"/> of him
        /// (in combat unless <paramref name="anyState"/>), by <paramref name="rank"/> (lowest first), then the most hurt, then
        /// the nearest. Null when none lives or the raider holds him.
        /// </summary>
        private static Advice? OthersTakeTheAdds(RuleContext c, Func<GameNPC, bool> isAdd, int radius, string tag,
            Func<GameNPC, int> rank = null, bool rangedOnly = false, bool anyState = false)
        {
            if (c.Holds || rangedOnly && !c.CanShootFlyers) return null;
            if (c.ClassId > 0 && BotPartyRoles.IsHealingClass((eCharacterClass)c.ClassId)) return null;
            GameNPC boss = c.Target;
            GameLiving raider = c.Raider;
            rank ??= _ => 0;
            GameNPC add = (c.Live ?? []).Where(n => Present(n) && n != boss && n.CurrentRegionID == boss.CurrentRegionID &&
                                                    isAdd(n) && (anyState || n.InCombat) && boss.IsWithinRadius(n, radius))
                .OrderBy(rank).ThenBy(n => n.HealthPercent).ThenBy(n => raider.GetDistanceTo(n)).ThenBy(n => n.ObjectID)
                .FirstOrDefault();
            return add == null ? null
                : new Advice(BotAction.Retarget, add, null, $"{tag}: the party he fights holds him; \"{add.Name}\" first");
        }

        /// <summary>A party's spot on a ring around <paramref name="centre"/> (pure): <paramref name="spots"/> spots
        /// <paramref name="radius"/> out, the first due east.</summary>
        public static Vector3 PartyRingSpot(Vector3 centre, int partyOrder, int spots, float radius, float z) =>
            EncounterMeleeStations.RingSpot(centre, 0f, EncounterMeleeStations.RingSlotFor(partyOrder, spots), spots, radius, z);

        /// <summary>A ranged raider further than this from its ring spot walks to it.</summary>
        public const int PartyRingTolerance = 200;

        /// <summary>
        /// Casters, archers and healers of every party but the one the boss fights stand at their party's spot on a ring
        /// (<see cref="PartyRingSpot"/>), so an area hit around one raider (Apocalypse's Rain of Fire, the Bane of Hope's
        /// Essence of Souls) catches one party's back line, not the raid's. Null at the spot, for melee and for his party.
        /// </summary>
        private static Advice? RangedOnPartyRing(RuleContext c, Vector3 centre, float radius, int spots, string tag)
        {
            if (!c.AtRange || c.Holds) return null;
            int order = EncounterMeleeStations.PartyOrder(c.Raider.Group, c.Now);
            Vector3 spot = KeepInsideFor(c.Target, PartyRingSpot(centre, order >= 0 ? order : PartyIndex(c.Raider), spots, radius, c.At.Z));
            if (Flat(c.At, spot) <= PartyRingTolerance) return null;
            return new(BotAction.Reposition, c.Target, AlbionFloorSnap(c.Raider, spot) ?? spot,
                $"{tag}: casters and healers at their party's spot, {(int)radius} out");
        }

        /// <summary>The floor spot nearest to <paramref name="spot"/> in <paramref name="near"/>'s zone (navmesh), or null without
        /// one. Settable for tests.</summary>
        public static Func<GameObject, Vector3, Vector3?> AlbionFloorSnap = (near, spot) =>
        {
            var nav = PathfindingProvider.Instance;
            Zone zone = near?.CurrentZone;
            if (zone == null || nav is not { IsAvailable: true } || !nav.HasNavmesh(zone)) return null;
            return nav.GetClosestPoint(zone, spot, 128, 128, 256, nav.DefaultFilters);
        };

        // ---- Apocalypse: the fly phases, his Harbingers, the Rain of Fire --------------------------------------------

        /// <summary>
        /// Where the rain of his fly phases lands (ApocalypseBrain: he flies to his spawn + 100 north, 750 up, and casts Rain of
        /// Fire, 650 heat in 800, at the floor under him, with no pause, for 30 s): under his spawn (Funus spawns him at
        /// 29,467,25,235,19,490).
        /// </summary>
        public static readonly Vector3 ApocRainSpot = new(29467, 25335, 19490);
        public const int ApocRainRadius = 800, ApocRainMargin = 150;
        /// <summary>The ring the raid waits on while he flies, from the rain's centre, and how far from the room's door any spot
        /// of it keeps (the barrier moves a raider within 150 of the door back to the middle, under the rain).</summary>
        public const int ApocFlyRing = 1_050, ApocDoorClear = 400;
        /// <summary>On the ground: the back line's party ring around the room's middle (the Rain of Fire NPC: 800 heat in 600
        /// around a raider it picks every 31 s), and Harbingers this close to him are his.</summary>
        public const int ApocPartyRing = 650, ApocPartySpots = 8, ApocHarbingerReach = 2_500;

        /// <summary>He is up in the air or on his way there (ApocalypseBrain's flags). Settable for tests.</summary>
        public static Func<bool> ApocalypseFlying = () => ApocalypseBrain.IsInFlyPhase || ApocalypseBrain.apoc_fly_phase;

        /// <summary>A spot on the fly ring, clear of the door (pure).</summary>
        public static bool InApocFlyRing(Vector3 point) =>
            Flat(point, ApocRainSpot) <= ApocFlyRing + 100 && Flat(point, HorsemenRoom.Door) > ApocDoorClear;

        /// <summary>
        /// Where a raider at <paramref name="raider"/> waits out a fly phase (pure): null when it is already clear of the rain
        /// (800 and a margin from its centre); else the point on the ring <see cref="ApocFlyRing"/> out, straight away from
        /// the centre (away from the door for a raider on it), turned in steps of 30 degrees until it is clear of the door.
        /// </summary>
        public static Vector3? ApocFlyStep(Vector3 raider)
        {
            if (Flat(raider, ApocRainSpot) > ApocRainRadius + ApocRainMargin) return null;
            Vector2 c = new(ApocRainSpot.X, ApocRainSpot.Y), r = new(raider.X, raider.Y), door = new(HorsemenRoom.Door.X, HorsemenRoom.Door.Y);
            Vector2 dir = Vector2.Distance(r, c) >= 1 ? Vector2.Normalize(r - c) : Vector2.Normalize(c - door);
            for (int k = 0; k < 12; k++)
            {
                double turn = (k + 1) / 2 * (Math.PI / 6) * (k % 2 == 0 ? 1 : -1);
                var d = new Vector2((float)(dir.X * Math.Cos(turn) - dir.Y * Math.Sin(turn)), (float)(dir.X * Math.Sin(turn) + dir.Y * Math.Cos(turn)));
                Vector2 p = c + d * ApocFlyRing;
                if (Vector2.Distance(p, door) > ApocDoorClear) return new Vector3(p.X, p.Y, ApocRainSpot.Z);
            }
            Vector2 away = c + Vector2.Normalize(c - door) * ApocFlyRing;
            return new Vector3(away.X, away.Y, ApocRainSpot.Z);
        }

        /// <summary>
        /// Apocalypse (period: "When the fight begins, Apocalypse will fly up and cause fire to rain from the ceiling ... When
        /// four players have been killed, Harbringers of Fate will begin to spawn. These ... should be taken down before
        /// Apocalypse is ... A total of four, two at a time ... teleports characters who move too far away into the center",
        /// VN 2003). The script: at 99% and at 50% he flies 750 up for 30 s and rains 650 heat on the 800 under him; a Rain of
        /// Fire NPC hits 800 heat in 600 around a raider it picks every 31 s all fight; two Harbingers (30,000 health) come
        /// after his fourth kill and two more at 50%.
        /// <list type="bullet">
        /// <item>While he flies: anyone within the rain steps to the fly ring (<see cref="ApocFlyStep"/>), clear of the door,
        /// and nobody keeps him as a target (he cannot be fought up there); Harbingers outside the rain are fought on.</item>
        /// <item>On the ground: the party he fights holds him; everyone else kills the Harbingers first (the most hurt first).</item>
        /// <item>Casters, archers and healers of the other parties stand at their party's spot 650 from the room's middle.</item>
        /// </list>
        /// </summary>
        private static Advice? ApocalypsePlan(RuleContext c)
        {
            if (c.Target.CurrentRegionID != HorsemenRoom.Region) return null;
            if (ApocalypseFlying())
            {
                if (ApocFlyStep(c.At) is Vector3 outOfRain)
                    return new(BotAction.Reposition, c.Target, AlbionFloorSnap(c.Raider, outOfRain) ?? outOfRain,
                        "apoc-fly: out of the Rain of Fire under him (800)");
                return c.Target is Apocalypse ? new(BotAction.StandOff, c.Target, null, "apoc-fly: he is in the air; wait out of the rain") : null;
            }
            if (c.Target is not Apocalypse) return null;
            if (OthersTakeTheAdds(c, n => n is HarbringerOfFate, ApocHarbingerReach, "apoc-harbingers", anyState: true) is Advice adds)
                return adds;
            return RangedOnPartyRing(c, HorsemenRoom.Middle, ApocPartyRing, ApocPartySpots, "apoc-rain");
        }

        private static long _apocFlyHoldTick;

        /// <summary>
        /// While he flies, every autonomous raider near the room that stands in the rain is held at its spot on the fly ring
        /// (<see cref="HoldStandOff"/>: ThinkBlast walks it there whatever it does, healers and raiders with no target
        /// included), once a second, by whichever raider fighting him or a Harbinger turns first.
        /// </summary>
        private static void ApocalypseFlyAct(GameBot bot, GameNPC target, IReadOnlyList<GameNPC> live)
        {
            if (target == null || target.CurrentRegionID != HorsemenRoom.Region || !ApocalypseFlying()) return;
            long now = GameLoop.GameLoopTime;
            if (now - System.Threading.Interlocked.Read(ref _apocFlyHoldTick) < 1_000) return;
            System.Threading.Interlocked.Exchange(ref _apocFlyHoldTick, now);
            foreach (GameBot raider in (live ?? []).OfType<GameBot>())
                if (raider.IsAlive && raider.IsAutonomousWorldBot && !raider.IsPlayerLedGroup && raider.CurrentRegionID == target.CurrentRegionID &&
                    ApocFlyStep(At(raider)) is Vector3 spot)
                    HoldStandOff(raider, AlbionFloorSnap(raider, spot) ?? spot, now + 3_000);
        }

        // ---- Warlord Dorinakka: his knights are his fight -----------------------------------------------------------------

        /// <summary>His knights' PackageID: once he fights, every one of them in the region joins (WarlordDorinakkaBrain.Think;
        /// the world database has 12 crypt knights, level 81, 20,125 health each, in two columns of six 190 apart up the
        /// hall to him, 321-1,063 from him).</summary>
        public const string DorinakkaKnights = "DorinakkaBaf";
        /// <summary>His knights fighting this close to him are his fight's (his tether, 2,000, and room for a knight chasing a
        /// raider).</summary>
        public const int DorinakkaKnightFightRadius = 2_500;

        /// <summary>The monster is one of Warlord Dorinakka's crypt knights (its PackageID; he himself has none).</summary>
        public static bool IsDorinakkaKnight(GameNPC npc) =>
            npc != null && npc is not DOL.GS.Scripts.WarlordDorinakka && npc.PackageID == DorinakkaKnights;

        /// <summary>
        /// Owner's PC, build 1dc66b6, 2026-11-15 (Albion's sweep raid): the 1.2 plan cleared the knights more than 700 from him
        /// first, each as an encounter of its own mustered 1,400 from the knight, that is 2,250 from him and past his 2,000
        /// tether. The first knight died alone; 16 s after the second was called he joined with the ten left (02:46:53), the
        /// fight ran at that muster and at the hall's mouth at once, he spent much of it outside his tether (immune; nine
        /// tether resets), the raid did him 3,300 damage and the knights 219,000 of the 278,000 it took, and it wiped
        /// (88 of 108 deaths to knights). A knight's fight at its spawn reaches the next knight of its column (190 apart,
        /// aggro 200), and the column ends 321-361 from him. So no knight is pulled on its own while he lives: a knight the
        /// route picks is his fight (a knight already fighting the raid is still answered), the raid musters for him
        /// (1,400 from his spawn, inside his tether) and goes in together.
        /// </summary>
        private static GameNPC DorinakkaPackRoute(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (!IsDorinakkaKnight(chosen) || BossNear(chosen, live, nameof(DOL.GS.Scripts.WarlordDorinakka), AddSearchRadius) is not { } lord)
                return null;
            reason = "dorinakka-pack: every knight joins his fight; the whole raid musters for him and the knights";
            return lord;
        }

        /// <summary>
        /// Warlord Dorinakka (period: "a straight melee fight ... significant damage ... he also parries", VN 2003; "tanks
        /// should stay behind him because he parries very often ... Guard on the aggro holder", Allakhazam 2003; "2 good
        /// groups can kill him", Allakhazam 2003). His knights copy his aggro list every think, so they go for those he
        /// hates. The party he fights holds him (its melee from behind him); every other raider but the healers, melee
        /// included, kills the knights fighting near him one at a time: the most hurt first, so the raid's damage lands on
        /// one knight, and a mesmerized knight last (a hit would wake it). With no knight left in the fight everyone fights
        /// him from behind.
        /// </summary>
        private static Advice? DorinakkaPlan(RuleContext c)
        {
            if (OthersTakeTheAdds(c, IsDorinakkaKnight, DorinakkaKnightFightRadius, "dorinakka-knights",
                    rank: n => n.IsMezzed ? 1 : 0) is Advice knights)
                return knights;
            return MeleeFromBehind(c, "dorinakka-behind");
        }

        // ---- the Silencer: few attackers ---------------------------------------------------------------------------------

        /// <summary>At most this many raiders hit him at once: his chance to turn 99% resistant on a hit is (distinct attackers
        /// / 10)% (integer division, Silencer.TakeDamage), so with 9 it is 0.</summary>
        public const int SilencerAttackerCap = 9;
        /// <summary>A raider who last asked this long ago loses its place among his attackers.</summary>
        public const long SilencerSlotMilliseconds = 8_000;
        /// <summary>The ones without a place stand this far off (his melee reach is 450).</summary>
        public const int SilencerStandOff = 700;

        /// <summary>His 99% window is on (Silencer.get_resist). Settable for tests.</summary>
        public static Func<bool> SilencerResisting = () => Silencer.get_resist;

        private static readonly Dictionary<GameNPC, Dictionary<GameLiving, long>> SilencerSlots = new();

        /// <summary>Whether <paramref name="raider"/> has (or now takes) one of the <paramref name="cap"/> places among the
        /// boss's attackers: places of raiders gone, dead or silent for <see cref="SilencerSlotMilliseconds"/> are freed first.</summary>
        public static bool TakeAttackerSlot(GameNPC boss, GameLiving raider, int cap, long now, bool ask = true)
        {
            if (boss == null || raider == null) return false;
            lock (SilencerSlots)
            {
                if (!SilencerSlots.TryGetValue(boss, out var slots))
                {
                    if (SilencerSlots.Count > 32) SilencerSlots.Clear();
                    SilencerSlots[boss] = slots = new();
                }
                foreach (GameLiving gone in slots.Where(s => !s.Key.IsAlive || s.Key.CurrentRegionID != boss.CurrentRegionID ||
                                                             now - s.Value > SilencerSlotMilliseconds).Select(s => s.Key).ToArray())
                    slots.Remove(gone);
                if (slots.ContainsKey(raider)) { if (ask) slots[raider] = now; return true; }
                if (!ask || slots.Count >= cap) return false;
                slots[raider] = now;
                return true;
            }
        }

        public static void ResetAttackerSlotsForTests() { lock (SilencerSlots) SilencerSlots.Clear(); }

        /// <summary>
        /// The Silencer (period: "adapts to damage types ... teleport the offender to a pit nearby", VN 2003; the script
        /// differs: no pit, no damage-type rule). The script's mechanic is his attacker count: the party he fights always
        /// fights him; besides it at most <see cref="SilencerAttackerCap"/> raiders hit him (the first to come keep their
        /// place while they fight), the rest stand <see cref="SilencerStandOff"/> off. While he resists 99% only his target's
        /// party stays on him.
        /// </summary>
        private static Advice? SilencerPlan(RuleContext c)
        {
            if (c.Holds) return null;
            if (SilencerResisting())
                return new(BotAction.StandOff, c.Target, StandOffPoint(c, SilencerStandOff), "silencer-few: he resists 99% for 20 s; hold");
            if (TakeAttackerSlot(c.Target, c.Raider, SilencerAttackerCap, c.Now)) return null;
            return new(BotAction.StandOff, c.Target, StandOffPoint(c, SilencerStandOff),
                $"silencer-few: {SilencerAttackerCap} raiders already hit him (more give him his 99%); stand by");
        }

        /// <summary>A bot without a place among his attackers hits him with nothing, area spells included.</summary>
        private static bool SilencerMustNotHit(GameBot bot, GameNPC silencer) =>
            bot is { IsAutonomousWorldBot: true, IsPlayerLedGroup: false } && silencer is Silencer && !Holds(bot, silencer) &&
            !TakeAttackerSlot(silencer, bot, SilencerAttackerCap, GameLoop.GameLoopTime, ask: false);

        private static Vector3? StandOffPoint(RuleContext c, int distance) =>
            Flat(c.At, At(c.Target)) < distance - 100 ? PointAway(c.At, At(c.Target), distance) : null;

        // ---- the Bane of Hope and Fate's Chosen ---------------------------------------------------------------------------

        public const int BaneRing = 1_000, BaneSpots = 8;

        /// <summary>Fate's Chosen stands 123 from the second spot the Bane of Hope throws raiders to (37,377,30,154 and
        /// 37,500,30,162; its aggro is 600): it dies before the Bane, so a thrown raider lands in an empty room.</summary>
        private static GameNPC FateChosenClearsFirst(GameNPC npc, IReadOnlyList<GameNPC> live) =>
            npc is FateChosen ? BossNear(npc, live, nameof(DOL.GS.Scripts.BaneOfHope), 9_000) : null;

        /// <summary>
        /// The Bane of Hope (period: "another straightforward melee fight ... teleport to another area nearby when heavily
        /// wounded and regenerate", VN 2003; the script differs: it throws the raider instead). Hit by a raider that is not
        /// its target it may cast Essence of Souls (150 every 4 s for 45 s, 600 around that raider; Cure Poison lifts it) and
        /// throw that raider to one of three spots. So the casters, archers and healers of every party but its target's stand
        /// at their party's spot on a ring <see cref="BaneRing"/> from it (765 apart), and the melee fight it from behind.
        /// </summary>
        private static Advice? BaneOfHopePlan(RuleContext c) =>
            RangedOnPartyRing(c, At(c.Target), BaneRing, BaneSpots, "bane-spread") ?? MeleeFromBehind(c, "bane-behind");

        // ---- the Spectral Provisioner: an ambush on his loop ----------------------------------------------------------------

        /// <summary>His loop (SpectralProvisionerBrain.Think: point1 to point8, walked at 195-300 whether or not he fights).</summary>
        public static readonly Vector3[] ProvisionerPatrol =
        [
            new(30050, 39425, 17004), new(30940, 39418, 17004), new(32065, 40205, 17004), new(32075, 42378, 17004),
            new(32072, 40376, 17006), new(32967, 39369, 17007), new(32057, 38494, 17007), new(31022, 39382, 17006),
        ];

        /// <summary>The parties' stations: his loop's points and the middle of each leg over 1,600, a spot within 300 of one
        /// before it left out (his cross's middle is passed twice, point2 and point8 lie 89 apart): seven.</summary>
        public static readonly Vector3[] ProvisionerStations = Stations(ProvisionerPatrol, 1_600)
            .Where((p, i) => !Stations(ProvisionerPatrol, 1_600).Take(i).Any(q => Flat(p, q) < 300)).ToArray();

        /// <summary>The monsters of his level that die first (PackageID ProvisionerBaf: five manifestations and an ater
        /// skeleton beside his loop, level 75).</summary>
        public const string ProvisionerLevel = "ProvisionerBaf";

        private static GameNPC ProvisionerLevelClearsFirst(GameNPC npc, IReadOnlyList<GameNPC> live) =>
            npc != null && npc.PackageID == ProvisionerLevel && npc is not DOL.GS.Scripts.SpectralProvisioner
                ? BossNear(npc, live, nameof(DOL.GS.Scripts.SpectralProvisioner), AddSearchRadius) : null;

        /// <summary>
        /// The Spectral Provisioner (period: "he will drop items in your backpack to encumber you and run away sometimes ...
        /// at least three groups", VN 2003; "clear the whole level ... it runs, corner it", Allakhazam 2006). The script walks
        /// his loop at 195-300 the whole fight (bots run about 190), takes no damage from heat, spirit or cold (the rule's
        /// Forbids), and answers a hit with Spectral Strike (350 spirit and a snare, 2,500). So the raid ambushes him as the
        /// Skeletal Sacristan: each party at its station, hitting him while he passes.
        /// </summary>
        private static Advice? ProvisionerAmbush(RuleContext c) =>
            PatrolAmbushAdvice(c.Target, ProvisionerStations[AssignedIndex(c.PartyIndex, ProvisionerStations.Length)], c.AtRange,
                "provisioner-ambush");
    }
}
