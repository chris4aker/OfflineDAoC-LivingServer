using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #568 C4 (owner decision 2026-11-26), ported from True165 edcfb0f4d (last 95b171f, bots/BotBrain.UtilitySpells.cs):
    /// spells bots never cast before, used only where period players used them.
    /// - Life Transfer (Cabalist): an emergency heal for a groupmate under 35% while the Cabalist itself is at 60%+ (Prima:
    ///   "perform emergency heals on your realm buddies").
    /// - Amnesia (Sorcerer, Bard): a monster that turned on the caster forgets it while the pet is within 600, so the pet
    ///   takes it back (Prima, Sorcerer: "cast Amnesia on it and let your pet make a few attacks").
    /// - Shedding a monster to the tank: a grouped non-tank with a living tank in the group casts Amnesia, else a Confusion
    ///   that cannot turn the monster on its neighbours (Value >= 0), on a monster hitting it (Camelot Herald, Combat
    ///   Basics: the tank takes a monster back from a caster).
    /// - PvP: Confusion only on an enemy player's pet ("does not work on PCs, but it DOES work on their pets"), Amnesia to
    ///   interrupt a casting enemy, Nearsight first on an enemy caster or archer (SI Prima: "the first spell you cast on a
    ///   caster or Scout"). Never on an enemy still stealthed (RvrStealthNotice decides when one is noticed).
    /// Looked at once a second. The ordinary rotation still never casts Amnesia or Confusion; only these rules do. A hired
    /// companion's "Life transfer: Off" and "Amnesia: Off" are honoured here and at the cast (CompanionSpellOptions165).
    /// Gamebots and companions alike; real players are not affected.
    /// </summary>
    public partial class BotBrain
    {
        public const int LifeTransferPatientPercent = 35;
        public const int LifeTransferOwnHealthPercent = 60;
        /// <summary>How near the pet must be to take a monster back after Amnesia.</summary>
        public const int AmnesiaPetReach = 600;
        private long _nextUtilitySpellLook;
        private long _nextShedAggroLook;

        /// <summary>Pure: Life Transfer heals a groupmate under 35% while the caster is at 60% or more.</summary>
        public static bool LifeTransferWanted(int patientHealthPercent, int ownHealthPercent) =>
            patientHealthPercent < LifeTransferPatientPercent && ownHealthPercent >= LifeTransferOwnHealthPercent;

        /// <summary>Pure: Amnesia hands a monster on the caster back to a living pet that is near enough to take it.</summary>
        public static bool AmnesiaForPet(bool monsterOnCaster, bool petAlive, double petDistanceToMonster) =>
            monsterOnCaster && petAlive && petDistanceToMonster <= AmnesiaPetReach;

        /// <summary>
        /// Pure (Prima, Healer: Amnesia makes a monster forget you and go back to the tank; Runemaster Confusion "does work"
        /// on monsters): a grouped non-tank with a living tank sheds a monster that turned on it.
        /// </summary>
        public static bool ShedsAggro(bool grouped, bool selfIsTank, bool tankAlive, bool monsterOnMe, bool playerLikeAttacker) =>
            grouped && !selfIsTank && tankAlive && monsterOnMe && !playerLikeAttacker;

        /// <summary>Pure: a Confusion with a negative value may turn the monster on its own kind and pull its neighbours.</summary>
        public static bool ConfusionSafe(Spell confusion) => confusion != null && confusion.Value >= 0;

        /// <summary>Pure: Confusion in PvP goes only on an enemy player's pet; it does not work on players.</summary>
        public static bool ConfusionOnEnemyPet(bool targetIsPet, bool ownerIsPlayerLike, bool ownerIsEnemy) =>
            targetIsPet && ownerIsPlayerLike && ownerIsEnemy;

        private Spell ReadySpell(eSpellType type) =>
            Body.Spells?.Where(spell => spell != null && spell.SpellType == type && spell.Level <= Body.Level &&
                                        Body.GetSkillDisabledDuration(spell) <= 0 && Body.Mana >= BotBody.PowerCost(spell))
                .OrderByDescending(spell => spell.Level)
                .FirstOrDefault();

        private bool CastOn(Spell spell, GameLiving target)
        {
            if (spell == null || target == null || !Body.IsWithinRadius(target, System.Math.Max(spell.Range, spell.Radius)))
                return false;
            GameObject previous = Body.TargetObject;
            Body.TargetObject = target;
            bool cast = Body.CastSpell(spell, m_mobSpellLine);
            if (!cast || spell.IsInstantCast)
                Body.TargetObject = previous;
            return cast;
        }

        private bool TryUtilitySpells()
        {
            GameBot bot = BotBody;
            long now = GameLoop.GameLoopTime;
            if (bot == null || now < _nextUtilitySpellLook || bot.IsCasting ||
                bot.castingComponent?.HasPendingSkillRequests == true || bot.IsIncapacitated)
                return false;
            _nextUtilitySpellLook = now + 1_000;
            return TryLifeTransfer() || TryAmnesiaForPet() || TryShedAggro() || TryPvpUtility();
        }

        /// <summary>A support healer (which never reaches the offensive spell turn) sheds aggro from its support turn.</summary>
        private bool TryShedAggroInSupport() =>
            BotBody != null && !BotBody.IsIncapacitated && !Body.IsCasting &&
            BotBody.castingComponent?.HasPendingSkillRequests != true && TryShedAggro();

        /// <summary>Amnesia (unless the player switched it off) or a safe Confusion on a monster hitting this bot while a
        /// tank is there to take it back. From its own attacker list; at most once a second.</summary>
        private bool TryShedAggro()
        {
            long now = GameLoop.GameLoopTime;
            if (now < _nextShedAggroLook || Body.Group == null || Body.IsCasting)
                return false;
            _nextShedAggroLook = now + 1_000;
            if (BotPartyRoles.IsTank(BotBody) || !GroupHasLivingTank())
                return false;
            GameLiving onMe = null;
            foreach (GameLiving attacker in Body.attackComponent.AttackerTracker.Attackers)
                if (attacker != null && attacker.IsAlive && attacker.TargetObject == Body && attacker is GameNPC &&
                    !BotPvpCrowdControl.PlayerLike(attacker))
                {
                    onMe = attacker;
                    break;
                }
            if (!ShedsAggro(true, false, true, onMe != null, false))
                return false;
            Spell shed = (CompanionOptions165.Choice(BotBody, "amnesia") != 1 ? ReadySpell(eSpellType.Amnesia) : null) ??
                         (ReadySpell(eSpellType.Confusion) is { } confusion && ConfusionSafe(confusion) ? confusion : null);
            return shed != null && !LivingHasEffect(onMe, shed) && CastOn(shed, onMe);
        }

        private bool TryLifeTransfer()
        {
            if (Body.Group == null || Body.HealthPercent < LifeTransferOwnHealthPercent ||
                CompanionOptions165.Choice(BotBody, "lifetransfer") == 1 || ReadySpell(eSpellType.LifeTransfer) is not Spell transfer)
                return false;
            GameLiving patient = Body.Group.GetMembersInTheGroup()
                .Where(member => member != Body && member.IsAlive && member.CurrentRegion == Body.CurrentRegion &&
                                 LifeTransferWanted(member.HealthPercent, Body.HealthPercent) &&
                                 Body.IsWithinRadius(member, transfer.Range))
                .OrderBy(member => member.HealthPercent)
                .FirstOrDefault();
            return patient != null && CastOn(transfer, patient);
        }

        private bool TryAmnesiaForPet()
        {
            if (CompanionOptions165.Choice(BotBody, "amnesia") == 1 ||
                Body.ControlledBrain?.Body is not GameNPC pet || !pet.IsAlive || ReadySpell(eSpellType.Amnesia) is not Spell amnesia)
                return false;
            // a monster on the caster that the pet is near enough to take back
            GameNPC onMe = Body.GetNPCsInRadius(1_000)
                .FirstOrDefault(npc => npc.IsAlive && npc is not GameBot && npc.Realm == eRealm.None && npc.TargetObject == Body &&
                                       npc.InCombat && AmnesiaForPet(true, true, pet.GetDistanceTo(npc)));
            return onMe != null && CastOn(amnesia, onMe);
        }

        private bool TryPvpUtility()
        {
            if (Body.TargetObject is not GameLiving target || !target.IsAlive || target.IsStealthed ||
                !GameServer.ServerRules.IsAllowedToAttack(Body, target, true))
                return false;
            // Confusion on an enemy player's pet
            if (target is GameNPC { Brain: IControlledBrain petBrain } &&
                petBrain.GetLivingOwner() is GameLiving petOwner &&
                ConfusionOnEnemyPet(true, petOwner is GamePlayer or GameBot, petOwner.Realm != Body.Realm) &&
                ReadySpell(eSpellType.Confusion) is Spell confusion && !LivingHasEffect(target, confusion))
                return CastOn(confusion, target);
            if (target is not (GamePlayer or GameBot) || target.Realm == Body.Realm)
                return false;
            // Amnesia interrupts a casting enemy
            if (target.IsCasting && CompanionOptions165.Choice(BotBody, "amnesia") != 1 &&
                ReadySpell(eSpellType.Amnesia) is Spell interrupt)
                return CastOn(interrupt, target);
            // Nearsight first on an enemy caster or archer
            ICharacterClass enemyClass = target switch { GamePlayer p => p.CharacterClass, GameBot b => b.CharacterClass, _ => null };
            bool casterOrArcher = enemyClass != null && (enemyClass.ClassType == eClassType.ListCaster ||
                                  BotRangedCombat.IsDedicatedArcher((eCharacterClass)enemyClass.ID));
            if (casterOrArcher && target.effectListComponent?.ContainsEffectForEffectType(eEffect.Nearsight) != true &&
                ReadySpell(eSpellType.Nearsight) is Spell nearsight)
                return CastOn(nearsight, target);
            return false;
        }
    }
}
