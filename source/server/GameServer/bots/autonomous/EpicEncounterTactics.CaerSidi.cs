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
            new(nameof(CryptLord), [], null)
            {
                Summary = "Any of his court pulls him and the whole court: the route fights him (mustered), not the court.",
                Route = CryptLordCourtPull,
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

        /// <summary>
        /// The Crypt Lord never walks home and, once he fights, sets his whole court within 10,000 on his target; and a court
        /// monster fighting within 800 of him pulls him in. So a court monster is never a small pull of its own while he
        /// lives: the route fights the Crypt Lord instead, which is a mustered encounter (the raid gathers before it goes in),
        /// and the court comes with him.
        /// </summary>
        private static GameNPC CryptLordCourtPull(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (chosen is CryptLord || chosen.PackageID != CryptLordCourt) return null;
            if (live.OfType<CryptLord>().FirstOrDefault(l => Present(l) && l.CurrentRegionID == chosen.CurrentRegionID &&
                                                            l.IsWithinRadius(chosen, 10_000)) is not { } lord)
                return null;
            reason = $"court of \"{lord.Name}\": any of it pulls him and his whole court; mustered as his fight";
            return lord;
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
