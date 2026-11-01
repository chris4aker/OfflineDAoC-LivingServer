using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>The pet focus damage shield, used as a player uses it (<see cref="BotPetFocusShieldPolicy"/>).</summary>
    public partial class BotBrain
    {
        private long _petShieldBackoffUntil;
        private long _petShieldChannelStart;
        private bool _petShieldChannelling;
        private long _nextPetShieldReport;
        private long _petShieldUntankedSince;
        /// <summary>A monster swapping targets for a moment does not end the shield; two seconds without one does.</summary>
        private const long PetShieldUntankedGraceMilliseconds = 2_000;

        private bool IsChannellingPetShield =>
            Body.castingComponent is { IsFocusChanneling: true } casting &&
            BotPetFocusShieldPolicy.IsPetFocusShield(casting.SpellHandler?.Spell);

        /// <summary>A channel that ended without this brain ending it (moved, hit, out of power) waits before the next.</summary>
        private void ObservePetShieldChannel()
        {
            if (_petShieldChannelling && !IsChannellingPetShield)
            {
                _petShieldChannelling = false;
                _petShieldBackoffUntil = GameLoop.GameLoopTime + BotPetFocusShieldPolicy.BackoffMilliseconds;
            }
        }

        /// <summary>
        /// The pet is tanking: a monster in melee with it is hitting it (owner 2026-10-28: shield the pet only when it takes
        /// the damage, as when soloing with the pet tanking; with a real tank holding the monster the shield does nothing).
        /// </summary>
        private bool PetTanking(out GameNPC pet, out GameLiving target)
        {
            pet = Body.ControlledBrain?.Body;
            target = null;
            if (pet is not { IsAlive: true } || pet.ObjectState != GameObject.eObjectState.Active)
                return false;
            GameNPC tankedPet = pet;
            bool HitsPet(GameLiving enemy) => enemy is { IsAlive: true } && enemy.ObjectState == GameObject.eObjectState.Active &&
                enemy.TargetObject == tankedPet && (enemy.IsAttacking || enemy.InCombat) &&
                enemy.IsWithinRadius(tankedPet, enemy.MeleeAttackRange + 100);
            if (HitsPet(pet.TargetObject as GameLiving))
                target = (GameLiving)pet.TargetObject;
            else
                target = pet.GetNPCsInRadius(500).FirstOrDefault(npc => npc != tankedPet && HitsPet(npc) &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, npc, true));
            return target != null;
        }

        private bool PetShieldCasterMustMove()
        {
            GameLiving leader = (GameLiving)BotBody?.PlayerGroupLeader ?? Body.Group?.LivingLeader;
            return leader != null && leader != Body && leader.IsMoving;
        }

        private bool PetShieldCasterUnderAttack() =>
            Body.IsBeingInterruptedByOther || RecentDirectAttacker() != null || EnemyOnCaster();

        private Spell BestPetFocusShield() =>
            Body.Spells?.Where(spell => BotPetFocusShieldPolicy.IsPetFocusShield(spell) && spell.Level <= Body.Level &&
                    Body.GetSkillDisabledDuration(spell) <= 0 && Body.Mana >= BotBody.PowerCost(spell) * 3)
                .OrderByDescending(spell => spell.Level).FirstOrDefault();

        /// <param name="nothingElseToCast">No attack spell could be cast this turn: the shield is then used above 60% power too.</param>
        private bool TryStartPetFocusShield(bool nothingElseToCast)
        {
            ObservePetShieldChannel();
            if (BotBody?.CharacterClass == null || Body.IsCasting || Body.castingComponent?.HasPendingSkillRequests == true)
                return false;
            Spell shield = BestPetFocusShield();
            if (shield == null || !PetTanking(out GameNPC pet, out GameLiving target))
                return false;
            long now = GameLoop.GameLoopTime;
            if (!BotPetFocusShieldPolicy.ShouldStart(true, target.HealthPercent, PetShieldCasterUnderAttack(), HasBlastPlayerLike(),
                    PetShieldCasterMustMove(), Body.ManaPercent, nothingElseToCast, LivingHasEffect(pet, shield), now,
                    _petShieldBackoffUntil) ||
                !Body.IsWithinRadius(pet, shield.CalculateEffectiveRange(Body)))
                return false;

            GameObject previous = Body.TargetObject;
            Body.StopAttack();
            Body.StopMovingOnPath();
            Body.StopMoving();
            bool started;
            BotBody.PetFocusShieldApproved = true;
            Body.TargetObject = pet;
            try
            {
                started = Body.CastSpell(shield, m_mobSpellLine);
            }
            finally
            {
                BotBody.PetFocusShieldApproved = false;
                Body.TargetObject = previous;
            }
            if (!started)
            {
                _petShieldBackoffUntil = now + BotPetFocusShieldPolicy.BackoffMilliseconds;
                return false;
            }
            _petShieldChannelling = true;
            _petShieldChannelStart = now;
            if (now >= _nextPetShieldReport)
            {
                _nextPetShieldReport = now + 60_000;
                log.Info($"BOT_PET_FOCUS_SHIELD bot=\"{Body.Name}\" class=\"{BotBody.ClassName}\" spell=\"{shield.Name}\" " +
                         $"pet=\"{pet.Name}\" target=\"{target.Name}\" power={Body.ManaPercent} companion={BotBody.IsTemporaryGroupHelper}");
            }
            return true;
        }

        /// <summary>
        /// Every turn, in every state, before anything else: keep the channel only while the pet is being hit (a short grace
        /// for a monster swapping targets), the caster stands, is not hit, has power and the pet is in the shield's range.
        /// Otherwise the shield is dropped at once: no damage is reflected from a pet nobody hits, and once the fight is over
        /// the bot goes on with whatever it was doing. Returns true while the channel goes on.
        /// </summary>
        private bool WatchPetFocusShield()
        {
            ObservePetShieldChannel();
            if (!IsChannellingPetShield)
            {
                _petShieldUntankedSince = 0;
                EndOrphanedPetShieldPulses();
                return false;
            }
            long now = GameLoop.GameLoopTime;
            if (!_petShieldChannelling)
            {
                // A channel this brain did not start (a GM or an old cast): watched the same way.
                _petShieldChannelling = true;
                _petShieldChannelStart = now;
            }
            bool tanking = PetTanking(out GameNPC pet, out _);
            if (tanking)
                _petShieldUntankedSince = 0;
            else if (_petShieldUntankedSince == 0)
                _petShieldUntankedSince = now;
            bool stillTanked = tanking || now - _petShieldUntankedSince < PetShieldUntankedGraceMilliseconds;
            Spell shield = Body.castingComponent.SpellHandler?.Spell;
            pet ??= Body.ControlledBrain?.Body;
            bool inRange = pet is { IsAlive: true } && shield != null && Body.IsWithinRadius(pet, shield.CalculateEffectiveRange(Body));
            if (BotPetFocusShieldPolicy.ShouldKeep(stillTanked && inRange, PetShieldCasterUnderAttack(),
                    Body.IsMoving || PetShieldCasterMustMove(), Body.ManaPercent, now - _petShieldChannelStart))
                return true;
            DropPetFocusShield();
            return false;
        }

        /// <summary>
        /// A shield pulse left running with no channel behind it (a later spell failed before it could end the focus) is
        /// ended once nobody hits the pet, so it never drains power or holds the bot after the fight.
        /// </summary>
        private void EndOrphanedPetShieldPulses()
        {
            if (BotBody?.CharacterClass == null ||
                (eCharacterClass)BotBody.CharacterClass.ID is not (eCharacterClass.Cabalist or eCharacterClass.Enchanter or
                    eCharacterClass.Spiritmaster) ||
                Body.effectListComponent?.ContainsEffectForEffectType(eEffect.Pulse) != true || PetTanking(out _, out _))
                return;
            foreach (ECSPulseEffect pulse in Body.effectListComponent.GetPulseEffects())
                if (BotPetFocusShieldPolicy.IsPetFocusShield(pulse.SpellHandler?.Spell))
                    pulse.End();
        }

        /// <summary>Ends the channel and clears the bot's spell state, so nothing waits behind it.</summary>
        private void DropPetFocusShield()
        {
            if (IsChannellingPetShield)
            {
                Body.StopCurrentSpellcast();
                if (Body.castingComponent.IsFocusChanneling)
                    Body.castingComponent.ClearSpellHandlers();
            }
            _petShieldChannelling = false;
            _petShieldUntankedSince = 0;
            _petShieldBackoffUntil = GameLoop.GameLoopTime + BotPetFocusShieldPolicy.BackoffMilliseconds;
        }

        /// <summary>A combat turn while the shield channels: heals, then add control, take over (their cast ends the focus).</summary>
        private void ContinuePetFocusShield()
        {
            if (CheckHeals())
                return;
            TryPveAddControl();
        }
    }
}
