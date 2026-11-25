using System.Collections.Generic;
using System.Threading;
using DOL.AI.Brain;
using DOL.GS.Keeps;

namespace DOL.GS
{
    public class NpcCastingComponent : CastingComponent, ILosCheckListener
    {
        private GameNPC _npcOwner;
        private Dictionary<GameObject, List<SpellWaitingForLosCheck>> _spellsWaitingForLosCheck = new();
        private Lock _spellsWaitingForLosCheckLock = new();

        private bool IsCasterGuardOrImmobile => _npcOwner is GuardCaster || _npcOwner.MaxSpeedBase == 0;

        public NpcCastingComponent(GameNPC npcOwner) : base(npcOwner)
        {
            _npcOwner = npcOwner;
        }

        /// <summary>
        /// True while at least one NPC spell is waiting for a client-assisted
        /// LoS answer. This state lives outside CastingComponent's ordinary
        /// start-skill queue, so support brains must be able to observe it to
        /// avoid submitting the same spell again every Think tick.
        /// </summary>
        public bool HasPendingLosCheckRequests
        {
            get
            {
                lock (_spellsWaitingForLosCheckLock)
                {
                    foreach (List<SpellWaitingForLosCheck> pending in _spellsWaitingForLosCheck.Values)
                        if (pending.Count != 0)
                            return true;

                    return false;
                }
            }
        }

        protected override bool RequestCastSpellInternal(
            Spell spell,
            SpellLine spellLine,
            ISpellCastingAbilityHandler spellCastingAbilityHandler,
            GameLiving target,
            GamePlayer losChecker,
            bool wantsLos = false)
        {
            // 1.2.1: a checker that can't be asked is no checker (see CastingComponent.StartDuringCastLosCheck). Its packet
            // sender was used unchecked here, and a refused request left the spell waiting for ever.
            var packets = losChecker?.Out;

            if (packets == null)
            {
                // 1.2.3 (DOL sweep X1): no client to ask; the navmesh judges a wall on the caster's own floor.
                if (wantsLos && BotCastSight.BlockedWithoutChecker(_npcOwner, target, spell))
                {
                    OnNoLos(target);
                    return false;
                }

                return base.RequestCastSpellInternal(spell, spellLine, spellCastingAbilityHandler, target, null);
            }

            if (LeftTheWorld(_npcOwner, target))
                return false;

            SpellWaitingForLosCheck spellWaitingForLosCheck = new(spell, spellLine);

            lock (_spellsWaitingForLosCheckLock)
            {
                if (_spellsWaitingForLosCheck.TryGetValue(target, out var list))
                    list.Add(spellWaitingForLosCheck);
                else
                    _spellsWaitingForLosCheck[target] = [spellWaitingForLosCheck];
            }

            if (packets.SendLosCheckRequest(_npcOwner, target, this))
                return true; // Consider the NPC is casting while waiting for the reply to prevent it from moving.

            // Refused (the checker's client is not playing): no answer will come. Take the spell back and cast unchecked.
            lock (_spellsWaitingForLosCheckLock)
            {
                if (_spellsWaitingForLosCheck.TryGetValue(target, out var list))
                {
                    list.Remove(spellWaitingForLosCheck);

                    if (list.Count == 0)
                        _spellsWaitingForLosCheck.Remove(target);
                }
            }

            return base.RequestCastSpellInternal(spell, spellLine, spellCastingAbilityHandler, target, null);
        }

        protected override GamePlayer GetLosChecker(GameLiving target)
        {
            if (target == Owner || target == null)
                return null;

            GamePlayer losChecker = target as GamePlayer;

            // GameBot's own BotBrain implements IControlledBrain so it can use
            // the ordinary pet/group command surface.  That does not make the
            // playerbot itself a pet.  Resolving its casts through the human
            // owner's client leaves autonomous and /spawn list casters waiting
            // on client LoS replies and lets repeated cast attempts consume
            // every AI turn.  Actual summoned pets still use their controlling
            // player's LoS proxy exactly as before.
            if (losChecker == null &&
                ShouldUseControlledOwnerLosProxy(_npcOwner is GameBot, _npcOwner.Brain is IControlledBrain) &&
                _npcOwner.Brain is IControlledBrain controlledBrain)
                losChecker = controlledBrain.GetPlayerOwner();

            if (losChecker == null && _npcOwner.Brain is StandardMobBrain)
            {
                List<GamePlayer> playersInRadius = _npcOwner.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE);

                if (playersInRadius.Count > 0)
                    losChecker = playersInRadius[Util.Random(playersInRadius.Count - 1)];
            }

            return losChecker;
        }

        public static bool ShouldUseControlledOwnerLosProxy(bool ownerIsGameBot, bool brainIsControlled) =>
            brainIsControlled && !ownerIsGameBot;

        public override void OnSpellCast(Spell spell)
        {
            if (!spell.IsHarmful || !spell.IsInstantCast)
                return;

            _npcOwner.ApplyInstantHarmfulSpellDelay();
        }

        public override void ClearSpellHandlers()
        {
            // Make sure NPCs don't start casting pending spells after being told to stop.
            lock (_spellsWaitingForLosCheckLock)
            {
                _spellsWaitingForLosCheck.Clear();
            }

            // Don't clear the attack spell queue here.
            if (_npcOwner.Brain is NecromancerPetBrain necromancerPetBrain)
                necromancerPetBrain.ClearSpellQueue();

            base.ClearSpellHandlers();
        }

        public bool IsAllowedToFollow(GameObject target)
        {
            if (!IsCasterGuardOrImmobile)
                return true;

            if (target is not GameLiving livingTarget)
                return false;

            return livingTarget.ActiveWeaponSlot is not eActiveWeaponSlot.Distance && livingTarget.IsWithinRadius(_npcOwner, livingTarget.attackComponent.AttackRange);
        }

        public void HandleLosCheckResponse(GamePlayer losChecker, LosCheckResponse response, ushort targetId)
        {
            GameObject target = _npcOwner.CurrentRegion.GetObject(targetId);

            if (target == null)
                return;

            lock (_spellsWaitingForLosCheckLock)
            {
                if (!_spellsWaitingForLosCheck.Remove(target, out var list))
                    return;

                bool success = response is LosCheckResponse.True;

                foreach (SpellWaitingForLosCheck spellWaitingForLosCheck in list)
                {
                    Spell spell = spellWaitingForLosCheck.Spell;
                    SpellLine spellLine = spellWaitingForLosCheck.SpellLine;

                    if (success && spellLine != null && spell != null)
                        base.RequestCastSpellInternal(spell, spellLine, null, target as GameLiving, losChecker);
                    else
                        OnNoLos(target);
                }

            }
        }

        /// <summary>
        /// A cast refused for line of sight. Every NPC drops the queued spell and a mobile one walks into sight
        /// (<see cref="GameNPC.OnCastSpellLosCheckFail"/>). Port of OpenDAoC-Core 678b8f16f: a caster guard or an immobile
        /// caster, which can't walk into sight, forgets that target and turns to another, as archer guards already did
        /// (NpcAttackAction.OnOutOfRangeOrNoLosRangedAttack); before, it stared at a wall. Gamebots keep their own handling.
        /// </summary>
        private void OnNoLos(GameObject target)
        {
            _npcOwner.OnCastSpellLosCheckFail(target);

            if (ForgetsTargetOnNoLos(_npcOwner is GameBot, IsCasterGuardOrImmobile) &&
                _npcOwner.Brain is StandardMobBrain brain && target is GameLiving living)
                brain.RemoveFromAggroList(living);
        }

        /// <summary>Whether an NPC forgets a target it can't see to cast at (pure).</summary>
        public static bool ForgetsTargetOnNoLos(bool isGameBot, bool casterGuardOrImmobile) => !isGameBot && casterGuardOrImmobile;

        private readonly struct SpellWaitingForLosCheck
        {
            public readonly Spell Spell;
            public readonly SpellLine SpellLine;

            public SpellWaitingForLosCheck(Spell spell, SpellLine spellLine)
            {
                Spell = spell;
                SpellLine = spellLine;
            }
        }
    }
}
