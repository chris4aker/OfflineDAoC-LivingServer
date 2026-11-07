using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// Caer Sidi's encounter rules (wave 2, 1.2): Funus's class and weapon puzzle, the Skeletal Sacristan's patrol, Soul
    /// Reckoner's room, the Crypt Lord's court and Lich Lord Sanguis's last 5%. The catalog rows (Morbus's swarm, the
    /// horsemen's fight-at-home radius, Soul Reckoner's souls) come from docs/encounters/hand-notes.json as before.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
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

        /// <summary>
        /// While <see cref="EpicEncounterMechanics.AllowFunusWeaponPuzzle"/> is on, any hit on Funus from a raider of the
        /// wrong class or weapon (or a pet) heals him to full. So only the ones he takes damage from fight him: clerics (any
        /// attack), scouts with a longbow (kept at range, where they never swing a melee weapon at him) and the rare mercenary,
        /// armsman or infiltrator already holding its bow or crossbow (bots of those classes don't switch to one). Everyone
        /// else stops, drops him from its targets and waits past his aggro range, and its pet is called off (TryHandle);
        /// their area spells are refused while he is in the blast (<see cref="MustNotHit"/>).
        /// </summary>
        [EncounterRule(typeof(Funus))]
        private static Advice? FunusWeaponPuzzle(RuleContext c)
        {
            if (!EpicEncounterMechanics.AllowFunusWeaponPuzzle) return null;
            // Drawn too far from home the catalog's fight-at-home rule speaks (nobody hits him; his target leads him back).
            if (OutsideHome(c.Target)) return null;
            return FunusAdvice(c.Raider, c.Target, c.ClassId, EpicEncounterRestores.WeaponType(c.Raider), HasLongbow(c.Raider));
        }

        /// <summary>The Funus rule (pure but for positions): the raider's class and active weapon, and whether it owns a longbow.</summary>
        public static Advice FunusAdvice(GameLiving raider, GameNPC funus, int classId, eObjectType weapon, bool ownsLongbow)
        {
            Vector3 at = At(raider), boss = At(funus);
            bool scout = (eCharacterClass)classId == eCharacterClass.Scout;
            if (scout && (weapon == eObjectType.Longbow || ownsLongbow))
            {
                if (Flat(at, boss) < FunusArcherClose)
                    return new(BotAction.Reposition, funus, AwayFrom(at, boss, FunusArcherStepBack),
                        "funus-puzzle: a scout steps back to shoot, never a melee swing (that heals him)");
                return new(BotAction.None, funus, null, "funus-puzzle: a scout's longbow hurts him");
            }
            if (EpicEncounterRestores.FunusAccepts(classId, weapon))
                return new(BotAction.None, funus, null, "funus-puzzle: this class and weapon hurt him");
            return new(BotAction.StandOff, funus, Flat(at, boss) < FunusStandOff - 100 ? AwayFrom(at, boss, FunusStandOff) : null,
                "funus-puzzle: this class or weapon would heal him to full; stand clear");
        }

        /// <summary>This bot must not hit <paramref name="living"/> at all, not even with an area spell aimed at another
        /// monster (BotBrain refuses the blast): Funus while his puzzle is on and the bot isn't one he takes damage from.</summary>
        public static bool MustNotHit(GameBot bot, GameLiving living)
        {
            if (bot == null || living is not Funus || !EpicEncounterMechanics.AllowFunusWeaponPuzzle) return false;
            int classId = EpicEncounterRestores.ClassId(bot);
            return !EpicEncounterRestores.FunusAccepts(classId, EpicEncounterRestores.WeaponType(bot)) &&
                   !((eCharacterClass)classId == eCharacterClass.Scout && HasLongbow(bot));
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
        [EncounterRule(typeof(DOL.GS.Scripts.SkeletalSacristan))]
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
        [EncounterRule(typeof(SoulReckoner))]
        private static Advice? SoulReckonerInRoom(RuleContext c)
        {
            if (c.Target.CurrentRegionID != 60) return null;
            return SoulReckonerAdvice(c.Raider, c.Target, SoulReckonerRoomRadius);
        }

        public static Advice? SoulReckonerAdvice(GameLiving raider, GameNPC reckoner, int roomRadius)
        {
            if (Flat(At(reckoner), SoulReckonerRoom) <= roomRadius - SoulReckonerRoomMargin) return null;
            if (reckoner.TargetObject == raider)
                return new(BotAction.StandOff, reckoner, SoulReckonerRoom, "reckoner-room: lead him back into his room");
            return new(BotAction.Reposition, reckoner, At(raider), $"reckoner-room: out of his room ({roomRadius}) he takes no damage; hold");
        }

        // ---- the Crypt Lord: his court comes with him ---------------------------------------------------------------------

        /// <summary>The Crypt Lord's court (PackageID of the monsters around him; 43 in the world database).</summary>
        public const string CryptLordCourt = "CryptLordBaf";

        /// <summary>
        /// The Crypt Lord never walks home and, once he fights, sets his whole court within 10,000 on his target; and a court
        /// monster fighting within 800 of him pulls him in. So a court monster is never a small pull of its own while he
        /// lives: the route fights the Crypt Lord instead, which is a mustered encounter (the raid gathers before it goes in),
        /// and the court comes with him.
        /// </summary>
        [EncounterRouteRule]
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
        [EncounterRule(typeof(LichLordSanguis))]
        private static Advice? SanguisLastAct(RuleContext c) =>
            FinishableUntargetable(c.Target) && c.Raider is GameBot bot && BotSpellPower.IsOffensiveCaster(bot)
                ? new Advice(BotAction.None, c.Target, null, "sanguis-finish: untargetable, casters finish him")
                : null;
    }
}
