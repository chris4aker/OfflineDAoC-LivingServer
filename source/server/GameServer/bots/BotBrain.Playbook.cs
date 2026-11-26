using System.Collections.Generic;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #568 C6 (owner decision 2026-11-26), ported from True165 66f782d8d (bots/BotBrain.Playbook.cs) with the debuff opening
    /// (2d1b010, b9c4dc0) and the bolt rule (269870b) it reads: the bot's <see cref="BotPlaybook"/> in CheckSpells - the fixed
    /// attack order that replaced the random pick, the resist debuffs that fit the caster's own damage, the opening debuffs
    /// and the bolt opener. The same path serves autonomous gamebots and hired companions; the companion's AI Options filter
    /// the candidates before this. Real players are not affected.
    /// </summary>
    public partial class BotBrain
    {
        private GameLiving _playbookTarget;
        private int _playbookDamageCasts;
        private int _playbookFailedSpellId;
        private long _playbookFailedUntil;
        private GameLiving _openingDebuffTarget;
        private int _openingDebuffsCast;
        // made once per brain, not on every cast decision
        private System.Func<Spell, int> _playbookPowerCost;
        private System.Func<Spell, bool> _playbookPassOver;

        /// <summary>Damage spells this bot has started on <paramref name="target"/> (a new target starts at 0).</summary>
        private int PlaybookDamageCastsOn(GameLiving target)
        {
            if (_playbookTarget != target)
            {
                _playbookTarget = target;
                _playbookDamageCasts = 0;
            }
            return _playbookDamageCasts;
        }

        /// <summary>After a cast attempt: count a started damage spell, or pass over a spell that would not start for 3 s.</summary>
        private void NotePlaybookCast(Spell spell, bool started)
        {
            if (spell == null)
                return;
            if (started)
            {
                if (Body.TargetObject is GameLiving target && BotCasterPriority.IsDamage(spell))
                {
                    PlaybookDamageCastsOn(target);
                    _playbookDamageCasts++;
                }
                if (_playbookFailedSpellId == spell.ID)
                    _playbookFailedSpellId = 0;
                return;
            }
            if (Body.IsCasting)
                return;
            _playbookFailedSpellId = spell.ID;
            _playbookFailedUntil = GameLoop.GameLoopTime + BotPlaybook.FailedSpellPassMilliseconds;
        }

        private bool PlaybookPassesOver(Spell spell) =>
            spell != null && spell.ID == _playbookFailedSpellId && GameLoop.GameLoopTime < _playbookFailedUntil;

        /// <summary>The player marked this debuff "Use" in the companion's AI Options (gamebots: never).</summary>
        private bool DebuffPickedByPlayer(Spell spell) =>
            BotBody?.IsHiredCompanion == true && CompanionOptions165.DebuffLineChoice(BotBody, spell) == 1;

        /// <summary>A debuff worth its cast for this bot (BotPlaybook.DebuffFits); anything that is no debuff passes.</summary>
        private bool PlaybookDebuffFits(Spell spell)
        {
            if (!BotCasterPriority.IsDebuff(spell) || BotBody == null)
                return true;
            PlaybookSpellKind kind = BotBody.Playbook.KindOf(spell);
            if (kind is not (PlaybookSpellKind.OffResistDebuff or PlaybookSpellKind.MeleeResistDebuff))
                return true;
            return BotPlaybook.DebuffFits(kind, Body.Group?.MemberCount > 1, !PrefersCurrentSpellRange(), DebuffPickedByPlayer(spell));
        }

        /// <summary>The opening debuff in playbook order (own damage type first), or null.</summary>
        private Spell ChoosePlaybookOpeningDebuff(List<Spell> candidates) =>
            BotPlaybook.ChooseOpeningDebuff(candidates, BotBody.Playbook.KindOfFunc, Body.Group?.MemberCount > 1,
                !PrefersCurrentSpellRange(), DebuffPickedByPlayer);

        /// <summary>Pure: a Wizard or Theurgist bolts only as the opener of a fight; everyone else whenever it lands.</summary>
        public static bool BoltAllowedByPlaybook(bool boltOpenerOnly, int damageCastsOnTarget) =>
            !boltOpenerOnly || damageCastsOnTarget == 0;

        /// <summary>
        /// The debuff opening: up to two debuffs (or every debuff the player marked "Use") on a fresh target that will last
        /// (BotCasterPriority.DebuffOpeningWorthwhile). A companion set to "Debuff before nuking: Never" opens with none.
        /// </summary>
        private bool DebuffOpeningWorthwhile()
        {
            if (BotBody == null || Body.TargetObject is not GameLiving target || !target.IsAlive)
                return false;
            if (_openingDebuffTarget != target)
            {
                _openingDebuffTarget = target;
                _openingDebuffsCast = 0;
            }
            if (CompanionOptions165.Choice(BotBody, "debuff") == 2)
                return false;
            int picked = CompanionOptions165.PickedDebuffCount(BotBody);
            return BotCasterPriority.DebuffOpeningWorthwhile(Body.Level, Body.ManaPercent, Body.Group?.MemberCount > 1,
                target.Level, target.HealthPercent, BotPvpCrowdControl.PlayerLike(target), _openingDebuffsCast,
                picked > 0 ? picked : BotCasterPriority.OpeningDebuffs);
        }

        private void NoteOpeningDebuff(Spell spell)
        {
            if (BotCasterPriority.IsDebuff(spell) && Body.TargetObject == _openingDebuffTarget)
                _openingDebuffsCast++;
        }

        /// <summary>How many others are meleeing <paramref name="target"/> (its own attacker list, no area scan); the bot's
        /// own swings do not count.</summary>
        private int OthersMeleeing(GameLiving target)
        {
            int melee = target?.attackComponent?.AttackerTracker?.MeleeCount ?? 0;
            if (melee > 0 && Body.attackComponent.AttackState && Body.ActiveWeaponSlot != eActiveWeaponSlot.Distance &&
                Body.IsWithinRadius(target, Body.MeleeAttackRange + 50))
                melee--;
            return melee;
        }

        /// <summary>The fight as the playbook reads it: all cheap fields, no area searches.</summary>
        private PlaybookContext PlaybookContextFor(GameLiving target)
        {
            bool playerLike = BotPvpCrowdControl.PlayerLike(target);
            bool grouped = Body.Group?.MemberCount > 1;
            GameNPC pet = Body.ControlledBrain?.Body;
            bool snareWanted = false;
            if (target != null && (playerLike || IsRunningMonster(target) || !grouped && target.TargetObject == Body &&
                    !Body.IsWithinRadius(target, Body.MeleeAttackRange + 100)))
                snareWanted = target.effectListComponent?.ContainsEffectForEffectType(eEffect.MovementSpeedDebuff) != true;
            return new PlaybookContext
            {
                HealthPercent = Body.HealthPercent,
                PowerPercent = Body.ManaPercent,
                TargetHealthPercent = target?.HealthPercent ?? 100,
                FightLasts = target != null && (playerLike || grouped || target.Level >= Body.Level),
                CastsOnTarget = PlaybookDamageCastsOn(target),
                PetUnderAttack = pet != null && pet.IsAlive && target?.TargetObject == pet,
                SnareWanted = snareWanted,
            };
        }

        /// <summary>The attack spell from the legal candidates, in playbook order (replaces the old random pick).</summary>
        private Spell ChoosePlaybookAttack(List<Spell> candidates, GameLiving target)
        {
            BotPlaybook playbook = BotBody.Playbook;
            // a Theurgist's elementals and an Animist's turrets keep the share of casts they had under the random pick
            int summons = 0;
            foreach (Spell spell in candidates)
                if (playbook.KindOf(spell) == PlaybookSpellKind.PetSummon)
                    summons++;
            if (summons > 0 && BotPlaybook.PetSummonTurn(summons, candidates.Count, Util.Random(candidates.Count - 1)))
            {
                int pick = Util.Random(summons - 1);
                foreach (Spell spell in candidates)
                    if (playbook.KindOf(spell) == PlaybookSpellKind.PetSummon && pick-- == 0)
                        return spell;
            }
            PlaybookContext context = PlaybookContextFor(target);
            _playbookPowerCost ??= spell => BotBody.PowerCost(spell);
            _playbookPassOver ??= PlaybookPassesOver;
            return BotPlaybook.ChooseAttack(candidates, context, playbook.KindOfFunc, _playbookPowerCost, playbook.PrefersDrain,
                playbook.BoltOpenerOnly, _playbookPassOver);
        }
    }
}
