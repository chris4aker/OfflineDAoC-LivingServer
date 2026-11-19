using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// Darkness Falls encounter rules: the pet-eating princes (wave 2, 1.2) and the parts of Legion's fight the bots play
    /// (wave 3, 1.2): no bladeturn near him, the wounded step out of his reach, and his throw to the platform takes bots
    /// too (they come back down by the recovery's recall). These are rules for named encounters, not a Darkness Falls raid
    /// AI: Legion's platform positions, a walked way down from the platform and add-wave assignments are not built.
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        [DungeonRules]
        private static IEnumerable<DungeonRule> DarknessFallsRules() =>
        [
            new(nameof(PrinceAsmoien), [nameof(PrinceAsmoien), nameof(PrinceBaalorien)], PrincesEatPets)
            {
                Summary = "A raider with a pet keeps it 2,400 away from the princes (they eat pets to heal).",
            },
            new("Legion", ["Legion"], LegionWounded)
            {
                Summary = "A wounded raider (under 35%) who isn't his target steps out of his 1,400 reach until it is healed " +
                          "(each raider he kills heals him 2.5%); no bot casts a bladeturn within 2,500 of him while he fights " +
                          "(he eats the bubble of his target, and five of them set off his 1,000-radius soul blast).",
            },
        ];

        // ---- Legion ---------------------------------------------------------------------------------------------------

        /// <summary>A raider under this health who isn't Legion's target steps out of his reach.</summary>
        public const int LegionWoundedPercent = 35;
        /// <summary>Where it waits: past his close range (1,400, Legion.TakeDamage) and his blast (1,000 around his target).</summary>
        public const int LegionWoundedStandOff = 1_700;
        /// <summary>No bot casts a bladeturn within this of a fighting Legion.</summary>
        public const int LegionBubbleRadius = 2_500;
        public const ushort DarknessFallsRegion = 249;

        private static Advice? LegionWounded(RuleContext c) =>
            LegionWoundedAdvice(c.At, c.Raider.HealthPercent, c.Target.TargetObject == c.Raider, c.Target);

        /// <summary>
        /// Legion heals 2.5% of his health by each raider he kills (EnemyKilled; flood guard Legion.KillHeal) and his adds and
        /// blast finish the wounded. A raider under <see cref="LegionWoundedPercent"/> who is not the one he fights steps out
        /// to <see cref="LegionWoundedStandOff"/> on its side and comes back once its healers have it up again; the healers
        /// near him take anyone under half first (KillHealBosses). His target stays: running would drag him onto the raid.
        /// </summary>
        public static Advice? LegionWoundedAdvice(Vector3 raider, int healthPercent, bool isHisTarget, GameNPC legion)
        {
            if (isHisTarget || healthPercent >= LegionWoundedPercent) return null;
            Vector3 boss = At(legion);
            if (Flat(raider, boss) >= LegionWoundedStandOff - 100) return new(BotAction.StandOff, legion, null,
                "legion-wounded: out of his reach until healed (each death heals him)");
            return new(BotAction.StandOff, legion, PointAway(raider, boss, LegionWoundedStandOff),
                "legion-wounded: out of his reach until healed (each death heals him)", "epic_legion_wounded");
        }

        private static DOL.GS.Scripts.Legion _legion;
        private static long _legionLookedUp;

        /// <summary>The live Legion of Darkness Falls (looked up at most every 5 s), or null.</summary>
        private static DOL.GS.Scripts.Legion LiveLegion(long now)
        {
            if (now - _legionLookedUp < 5_000 && (_legion == null || _legion.IsAlive && _legion.ObjectState == GameObject.eObjectState.Active))
                return _legion;
            _legionLookedUp = now;
            _legion = WorldMgr.GetRegion(DarknessFallsRegion)?.Objects.OfType<DOL.GS.Scripts.Legion>()
                .FirstOrDefault(l => l.IsAlive && l.ObjectState == GameObject.eObjectState.Active);
            return _legion;
        }

        /// <summary>No bladeturn here (pure): within <see cref="LegionBubbleRadius"/> of a Legion who fights.</summary>
        public static bool BladeturnFeedsLegion(GameLiving caster, GameNPC legion) =>
            caster != null && legion is { IsAlive: true, InCombat: true } && caster.CurrentRegionID == legion.CurrentRegionID &&
            caster.IsWithinRadius(legion, LegionBubbleRadius);

        /// <summary>
        /// A bot must not cast a bladeturn now (BotBrain's buffs, group bubbles and pulsing chants ask it): Legion fights within
        /// <see cref="LegionBubbleRadius"/>. He ends the bladeturn on his target on every turn, and after five of them he
        /// casts his soul blast (1,000 body damage to everyone within 1,000 of his target, a quarter of it healing him).
        /// </summary>
        public static bool NoBladeturnHere(GameLiving caster)
        {
            if (caster == null || caster.CurrentRegionID != DarknessFallsRegion) return false;
            DOL.GS.Scripts.Legion legion;
            try { legion = LiveLegion(GameLoop.GameLoopTime); }
            catch (System.Exception) { return false; }
            return BladeturnFeedsLegion(caster, legion);
        }

        /// <summary>Princes Asmoien and Ba'alorien eat the nearest summoned pet within 2,000 every 5 s and heal by its
        /// health; a raider with a pet keeps it (it follows its owner) this far away.</summary>
        public const int PrincePetSafeDistance = 2_400;

        /// <summary>The raider's own summoned pet that a prince would eat, or null.</summary>
        public static GameNPC PetOf(GameLiving raider) =>
            raider?.ControlledBrain?.Body is GameNPC pet && EpicEncounterMechanics.IsDevourablePet(pet) ? pet : null;

        /// <summary>
        /// A raider whose pet a prince would eat (and heal by) stays out of the fight with it: it drops the prince, calls its
        /// pet off (TryHandle) and waits 2,400 away, so the pet that follows it is out of his 2,000 reach. Raiders without a
        /// pet fight on. (Only real summoned pets are eaten, never a bot: EpicEncounterMechanics.IsDevourablePet.)
        /// Since the DF raid wave a class whose pet is optional releases it first (ReleasePetForPrince) and fights on, and a
        /// necromancer or animist stands off whether or not its pet or turret is up (an animist without one would plant
        /// one beside him: <see cref="PetIsTheFight"/>).
        /// </summary>
        private static Advice? PrincesEatPets(RuleContext c) =>
            PrincePetAdvice(At(c.Raider), c.Target, PetOf(c.Raider) != null || PetIsTheFight(c.ClassId));

        public static Advice? PrincePetAdvice(Vector3 raider, GameNPC prince, bool hasPet)
        {
            if (!hasPet) return null;
            return new Advice(BotAction.StandOff, prince,
                Flat(raider, At(prince)) < PrincePetSafeDistance - 100 ? PointAway(raider, At(prince), PrincePetSafeDistance) : null,
                "prince-pets: he eats pets and heals by them; keep yours out of his reach");
        }


        // ==== Darkness Falls raid AI (1.2, "DF raid" wave): the high lords, Oro's copies, the princes' pets ============
        //
        // Period sources (2002-2003): an IGN DAoC board guide of April 2003 ("High Lord A ... has two illusionary duplicates
        // of himself, which deal damage ... but do not drop items"; "High Lord Baln ... uses a Point Blank Area of Effect
        // attack sometimes"; "High Lord Saeor ... regaining life whenever they kill a player, making them the most difficult
        // of the four high lords"); Allakhazam's High Lord Oro page ("all three Oros will come", "tanks each engage one",
        // "a PBAE powertap ... be sure your Healers are a bit away") and Prince Ba'alorien page ("Do not pet this mob");
        // the Prima eGuide's room key (each high lord's lair has its Dukes and Earls beside it). The scripts already play
        // those mechanics; these rules teach the bots to play them, nothing is weakened. Autonomous gamebots only
        // (TryHandle); companion bots and real players are untouched. Tests only: not seen in game yet.

        [DungeonRules]
        private static IEnumerable<DungeonRule> DarknessFallsRaidRules() =>
        [
            new(nameof(HighLordOro), [nameof(HighLordOro), nameof(HighLordOroClone)], OroAndHisCopies)
            {
                Summary = "Oro's two copies come with him and die with him: the party a copy fights holds it, everyone else " +
                          "kills Oro (the route takes him, never a copy), and casters and healers keep 650 from every Oro " +
                          "(each drains 5% mana a turn within 350).",
                Route = OroRouteToReal,
                RoutesInCombat = true,
            },
            new(nameof(HighLordSaeor), [nameof(HighLordSaeor)], SaeorWounded)
            {
                Summary = "Each raider who dies within 1,000 of him heals him by its whole health: a wounded raider (under 40%) " +
                          "who isn't his target steps out to 1,300 until it is healed to 75%.",
            },
            new(nameof(HighLordBaln), [], null)
            {
                Summary = "His first hit calls every idle demon within 3,000: the Dukes, Earls and Marquis of his wing are " +
                          "cleared first, one at a time.",
                ClearFirstOf = BalnWingClearsFirst,
            },
            new("Chamberlain", [], null)
            {
                Summary = "Before Legion is pulled, the named demons of his antechamber (the Chamberlain, Earl Amagin, " +
                          "Inquisitor Famuel, Mutilator Konapher, Marquis Haurian: within 3,000 of his home) are cleared, " +
                          "again if they respawn, so none comes into his fight.",
                ClearFirstOf = AntechamberClearsFirst,
            },
            new(nameof(PrinceAsmoien), [nameof(PrinceAsmoien), nameof(PrinceBaalorien)], null)
            {
                Summary = "Pet classes whose pet is optional (druids, hunters, charmers, casters) release it and fight on; no " +
                          "bot summons a pet or plants a turret within 3,000 of a fighting prince.",
                Act = ReleasePetForPrince,
            },
        ];

        // ---- High Lord Oro and his copies -------------------------------------------------------------------------------

        /// <summary>Oro and each copy drain 5% of the maximum mana of everyone within this (OroBrain/OroCloneBrain.Think).</summary>
        public const int OroDrainRadius = 350;
        /// <summary>A caster or healer this close to an Oro steps out to <see cref="OroDrainStandOff"/>.</summary>
        public const int OroDrainStepOut = OroDrainRadius + 100;
        public const int OroDrainStandOff = 650;
        /// <summary>Copies further than this from Oro are not his (HighLordOro.Die kills those within 5,000).</summary>
        public const int OroCopyReach = 5_000;

        private static GameNPC RealOro(GameNPC near, IReadOnlyList<GameNPC> live) =>
            IsOfType(near, nameof(HighLordOro)) && Present(near) ? near : BossNear(near, live, nameof(HighLordOro), OroCopyReach);

        /// <summary>The raid route fights the real Oro, never a copy: the copies share his name (the route took a respawned
        /// copy for the objective twice on the owner's PC, 2026-11-14, and each attempt walked the whole raid back down),
        /// and his death kills them all.</summary>
        private static GameNPC OroRouteToReal(GameNPC chosen, IReadOnlyList<GameNPC> live, out string reason)
        {
            reason = null;
            if (!IsOfType(chosen, nameof(HighLordOroClone)) || RealOro(chosen, live) is not { } real) return null;
            reason = "a copy of High Lord Oro: he dies for all three";
            return real;
        }

        private static Advice? OroAndHisCopies(RuleContext c)
        {
            GameNPC real = RealOro(c.Target, c.Live);
            bool onCopy = IsOfType(c.Target, nameof(HighLordOroClone));
            Vector3[] oros = (c.Live ?? []).Where(n => Present(n) && n.CurrentRegionID == c.Target.CurrentRegionID &&
                                                       (IsOfType(n, nameof(HighLordOro)) || IsOfType(n, nameof(HighLordOroClone))))
                .Select(At).ToArray();
            return OroAdvice(c.At, onCopy, c.Holds, c.AtRange && c.Raider.MaxMana > 0, real, oros, c.Target);
        }

        /// <summary>
        /// One raider in Oro's fight (pure). On a copy it doesn't hold (it isn't the copy's target or in that party): Oro
        /// instead, whose death ends the copies. A caster or healer within <see cref="OroDrainStepOut"/> of any Oro: out to
        /// <see cref="OroDrainStandOff"/> from the nearest, keeping its target (it fights from there). Else nothing.
        /// </summary>
        public static Advice? OroAdvice(Vector3 raider, bool onCopy, bool holdsCopy, bool usesMana, GameNPC realOro,
            IReadOnlyList<Vector3> oros, GameNPC target)
        {
            if (onCopy && !holdsCopy && realOro != null)
                return new Advice(BotAction.Retarget, realOro, null, "oro-copies: on Oro himself, his death ends the copies");
            if (!usesMana || oros == null || oros.Count == 0) return null;
            Vector3 nearest = oros.OrderBy(o => Flat(raider, o)).First();
            if (Flat(raider, nearest) > OroDrainStepOut) return null;
            return new Advice(BotAction.Reposition, target, PointAway(raider, nearest, OroDrainStandOff),
                "oro-drain: out of his mana drain (350)", "epic_oro_drain");
        }

        // ---- High Lord Saeor: heals by the dead -------------------------------------------------------------------------

        /// <summary>He heals by the whole health of a raider who dies within this of him (HighLordSaeor.PlayerKilledBySaeor).</summary>
        public const int SaeorDeathHealRadius = 1_000;
        public const int SaeorWoundedStandOff = SaeorDeathHealRadius + 300;
        /// <summary>A raider under this steps out; once out it comes back at <see cref="KillHealBackPercent"/>.</summary>
        public const int KillHealOutPercent = 40;
        public const int KillHealBackPercent = 75;

        private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GameLiving, object> SteppedOut = new();
        private static readonly object OutMark = new();

        /// <summary>Whether a raider stays (or goes) out of a kill-heal boss's reach (pure): never his target (running drags
        /// him onto the raid); out under <see cref="KillHealOutPercent"/>, back once at <see cref="KillHealBackPercent"/>.</summary>
        public static bool KillHealStepsOut(bool wasOut, int healthPercent, bool isHisTarget) =>
            !isHisTarget && healthPercent < (wasOut ? KillHealBackPercent : KillHealOutPercent);

        private static Advice? SaeorWounded(RuleContext c)
        {
            bool wasOut = SteppedOut.TryGetValue(c.Raider, out _);
            bool isTarget = c.Target.TargetObject == c.Raider;
            bool goesOut = KillHealStepsOut(wasOut, c.Raider.HealthPercent, isTarget);
            if (goesOut && !wasOut) SteppedOut.AddOrUpdate(c.Raider, OutMark);
            else if (!goesOut && wasOut) SteppedOut.Remove(c.Raider);
            return goesOut ? SaeorWoundedAdvice(c.At, c.AtRange, c.Target) : null;
        }

        /// <summary>A wounded raider out of Saeor's death heal (pure): a caster already past <see cref="SaeorWoundedStandOff"/>
        /// fights on from there; anyone closer stops and walks out to it.</summary>
        public static Advice? SaeorWoundedAdvice(Vector3 raider, bool atRange, GameNPC saeor)
        {
            Vector3 boss = At(saeor);
            if (Flat(raider, boss) >= SaeorWoundedStandOff - 50)
                return atRange ? null : new Advice(BotAction.StandOff, saeor, null, "saeor-wounded: out of his death heal until healed");
            return new Advice(BotAction.StandOff, saeor, PointAway(raider, boss, SaeorWoundedStandOff),
                "saeor-wounded: out of his death heal until healed", "epic_saeor_wounded");
        }

        // ---- High Lord Baln: his wing comes with his first hit ----------------------------------------------------------

        /// <summary>HighLordBaln.OnAttackedByEnemy: every idle monster of his kind within this joins on his first hit.</summary>
        public const int BalnCallRadius = 3_000;

        /// <summary>
        /// A named demon (Duke, Earl, Marquis: an epic NPC) of Baln's kind standing idle within <see cref="BalnCallRadius"/>
        /// of his home while he is out of combat is killed before him (the route takes it first): otherwise his first hit
        /// brings it, with the rest of the wing, onto the raider who hit him. Monsters of the wing that aren't named still
        /// come; they are the trash of his fight.
        /// </summary>
        private static GameNPC BalnWingClearsFirst(GameNPC npc, IReadOnlyList<GameNPC> live)
        {
            if (npc is not IGameEpicNpc || IsOfType(npc, nameof(HighLordBaln)) || npc.InCombat) return null;
            GameNPC baln = LiveOfType(live, nameof(HighLordBaln)).FirstOrDefault(b => b.CurrentRegionID == npc.CurrentRegionID);
            if (baln == null) return null;
            return BalnCalls(baln.InCombat, EpicEncounterMechanics.IsOwnKind(baln, npc),
                Vector3.Distance(At(npc), new Vector3(baln.SpawnPoint.X, baln.SpawnPoint.Y, baln.SpawnPoint.Z))) ? baln : null;
        }

        /// <summary>Whether Baln's first hit would call this monster (pure).</summary>
        public static bool BalnCalls(bool balnInCombat, bool ownKind, float fromHisHome) =>
            !balnInCombat && ownKind && fromHisHome <= BalnCallRadius;

        // ---- Legion's antechamber: cleared before his pull (route order; Legion's own rules are elsewhere) -----------------

        /// <summary>The named monsters within this of Legion's home are cleared before his pull.</summary>
        public const int AntechamberRadius = 3_000;

        /// <summary>
        /// A named demon (an epic NPC: the Chamberlain, his Earl, Inquisitor, Mutilator and Marquis) idle within
        /// <see cref="AntechamberRadius"/> of Legion's home while Legion is out of combat is killed before him; a respawned
        /// one again (they come back in 20-30 minutes, the raid's muster for him took 100). Period: "Clear the room fully,
        /// then engage at once to avoid an early respawn. If a respawn happens, kill the lesser mobs first" (Salaris' Albion
        /// epic guide, IGN boards 2003); "7 pallys with bots would have taken him down, if not for chamberlain re-spawn"
        /// (Allakhazam, Legion, 2004). A peaceful or untargetable NPC never holds his pull.
        /// </summary>
        private static GameNPC AntechamberClearsFirst(GameNPC npc, IReadOnlyList<GameNPC> live)
        {
            if (npc is not IGameEpicNpc || IsOfType(npc, "Legion") || npc.InCombat || npc is GameBot ||
                (npc.Flags & (GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET)) != 0 || npc.Realm != eRealm.None) return null;
            GameNPC legion = LiveOfType(live, "Legion").FirstOrDefault(l => l.CurrentRegionID == npc.CurrentRegionID);
            if (legion == null || legion.InCombat) return null;
            return Vector3.Distance(At(npc), new Vector3(legion.SpawnPoint.X, legion.SpawnPoint.Y, legion.SpawnPoint.Z)) <= AntechamberRadius
                ? legion : null;
        }

        // ---- the pet-eating princes: no pet near them ----------------------------------------------------------------

        /// <summary>No bot summons a pet or plants a turret within this of a devouring prince in combat (his reach, 2,000,
        /// plus a pet's walk to its owner's target).</summary>
        public const int PrinceSummonGuardRadius = 3_000;

        /// <summary>The classes whose pet is their fight (a necromancer's shade, an animist's turrets): they stand off
        /// (<see cref="PrincePetAdvice"/>); every other class releases its pet and fights the prince without it.</summary>
        public static bool PetIsTheFight(int classId) =>
            (eCharacterClass)classId is eCharacterClass.Necromancer or eCharacterClass.Animist;

        private static readonly HashSet<eSpellType> PetSummons =
        [
            eSpellType.SummonTheurgistPet, eSpellType.SummonAnimistPet, eSpellType.SummonAnimistFnF, eSpellType.SummonAnimistFnFCustom,
            eSpellType.SummonAnimistAmbusher, eSpellType.SummonSpiritFighter, eSpellType.SummonHunterPet, eSpellType.SummonUnderhill,
            eSpellType.SummonDruidPet, eSpellType.SummonSimulacrum, eSpellType.SummonNecroPet, eSpellType.SummonCommander,
            eSpellType.SummonMinion, eSpellType.Summon, eSpellType.SummonElemental, eSpellType.Charm,
        ];

        /// <summary>The spell gives its caster a pet or a turret (what the princes eat).</summary>
        public static bool IsPetSummon(Spell spell) => spell != null && PetSummons.Contains(spell.SpellType);

        private static GameNPC[] _princes = [];
        private static long _princesLookedUp = long.MinValue;

        private static GameNPC[] LivePrinces(long now)
        {
            if (now - _princesLookedUp < 5_000 && now >= _princesLookedUp) return _princes;
            _princesLookedUp = now;
            _princes = WorldMgr.GetRegion(DarknessFallsRegion)?.Objects.OfType<GameNPC>()
                .Where(n => n is PrinceAsmoien or PrinceBaalorien && Present(n)).ToArray() ?? [];
            return _princes;
        }

        /// <summary>Whether this summon would feed a prince (pure): a pet or turret summon while a prince who fights stands
        /// within <see cref="PrinceSummonGuardRadius"/> of the caster or of its target.</summary>
        public static bool SummonFeeds(bool isPetSummon, IEnumerable<(Vector3 At, bool Fighting)> princes, Vector3 caster, Vector3? target) =>
            isPetSummon && princes != null && princes.Any(p => p.Fighting &&
                (Flat(p.At, caster) <= PrinceSummonGuardRadius || target is Vector3 t && Flat(p.At, t) <= PrinceSummonGuardRadius));

        /// <summary>
        /// GameBot.CastSpell's final gate: an autonomous gamebot (not one in a real player's group) doesn't summon a pet or
        /// plant a turret near a fighting prince of Darkness Falls (owner's PC, 2026-11-14: 87 animist turrets went up in
        /// the 15 minutes of Prince Asmoien's fight, each one eaten within 5 s and healing him by its health; the fight
        /// took 18.5 minutes). Companion bots and real players summon as their player decides.
        /// </summary>
        public static bool SummonFeedsPrince(GameBot bot, Spell spell)
        {
            if (bot == null || !IsPetSummon(spell) || bot.CurrentRegionID != DarknessFallsRegion || !bot.IsAutonomousWorldBot ||
                bot.IsPlayerLedGroup) return false;
            GameNPC[] princes;
            try { princes = LivePrinces(GameLoop.GameLoopTime); }
            catch (System.Exception) { return false; }
            Vector3? target = bot.TargetObject is GameObject t && t.CurrentRegionID == bot.CurrentRegionID ? At(t) : null;
            return SummonFeeds(true, princes.Where(p => p.CurrentRegionID == bot.CurrentRegionID).Select(p => (At(p), p.InCombat)),
                At(bot), target);
        }

        /// <summary>A bot on a prince whose pet is optional releases it (it would be eaten and heal him); the summon gate
        /// keeps it from summoning another while he fights.</summary>
        private static void ReleasePetForPrince(GameBot bot, GameNPC prince, IReadOnlyList<GameNPC> live)
        {
            if (bot == null || PetIsTheFight(bot.CharacterClass?.ID ?? 0) || PetOf(bot) == null) return;
            bot.CommandNpcRelease();
        }
    }
}
