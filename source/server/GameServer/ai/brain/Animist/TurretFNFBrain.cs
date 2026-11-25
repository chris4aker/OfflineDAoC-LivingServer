using System.Collections.Generic;
using DOL.GS;
using DOL.GS.ServerProperties;

namespace DOL.AI.Brain
{
    public class TurretFNFBrain : TurretBrain
    {
        private List<GameLiving> _filteredAggroList = new();

        public TurretFNFBrain(GameLiving owner) : base(owner) { }

        protected override bool CheckLosBeforeCastingOffensiveSpells => Properties.CHECK_LOS_BEFORE_AGGRO_FNF;

        public override void Think()
        {
            CheckProximityAggro();

            if (!CheckSpells(eCheckSpellType.Offensive))
                CheckSpells(eCheckSpellType.Defensive);
        }

        public override bool CheckProximityAggro()
        {
            // AI shrooms assist one deliberate fight; even a single-target
            // spell would pull an entire camp if every turret scanned/randomly
            // selected fresh mobs. Human-owned manual FnFs keep native behavior.
            if (AnimistSingleTargetPolicy.AppliesTo(Owner))
                return false;

            // FnF turrets need to add all players and NPCs to their aggro list to be able to switch target randomly and effectively.
            CheckPlayerAggro();
            CheckNpcAggro();
            return HasAggro;
        }

        protected override void CheckPlayerAggro()
        {
            // Copy paste of 'base.CheckPlayerAggro()' except we add all players in range.
            foreach (GamePlayer player in Body.GetPlayersInRadius((ushort) AggroRange))
            {
                if (!CanConsiderProximityTarget(player))
                    continue;

                if (player.IsStealthed || player.Steed != null)
                    continue;

                if (player.effectListComponent.ContainsEffectForEffectType(eEffect.Shade))
                    continue;

                if (Properties.CHECK_LOS_BEFORE_AGGRO_FNF)
                    SendAggroLosCheck(player, player);
                else
                    AddToAggroList(player);
            }
        }

        protected override void CheckNpcAggro()
        {
            // Copy paste of 'base.CheckNPCAggro()' except we add all NPCs in range.
            foreach (GameNPC npc in Body.GetNPCsInRadius((ushort) AggroRange))
            {
                if (!CanConsiderProximityTarget(npc))
                    continue;

                if (npc is GameTaxi or GameTrainingDummy)
                    continue;

                if (Properties.CHECK_LOS_BEFORE_AGGRO_FNF)
                {
                    if (npc.Brain is ControlledMobBrain theirControlledNpcBrain && theirControlledNpcBrain.GetPlayerOwner() is GamePlayer theirOwner)
                    {
                        SendAggroLosCheck(theirOwner, npc);
                        continue;
                    }
                    else if (GetPlayerOwner() is GamePlayer ourOwner)
                    {
                        SendAggroLosCheck(ourOwner, npc);
                        continue;
                    }
                }

                AddToAggroList(npc);
            }
        }

        protected bool CanConsiderProximityTarget(GameLiving target)
        {
            if (Body is TurretFnfPet && Owner is GameBot { IsAutonomousWorldBot: true } bot && BotAnimistPolicy.AppliesTo(bot))
                return AggroLevel > 0 && target != null && !bot.IsObjectGreyCon(target) &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, target, true);
            return CanAggroTarget(target);
        }

        protected override bool CanAddToAggroListFromMultipleLosChecks => true;

        // Fractions of AggroRange marking the distance buckets (OpenDAoC-Core a89bfef81): very close targets are picked first.
        private static readonly double[] _distanceBucketThresholds = [0.4];
        private readonly List<GameLiving>[] _filteredByBucket = [new(), new()];

        /// <summary>Which distance bucket a target falls in (pure): enemy players are always in the closest one, so a player hiding
        /// behind a wall can't draw every turret off the others (OpenDAoC-Core 27ec0ed7a).</summary>
        public static int DistanceBucket(bool isPlayer, double distance, int aggroRange)
        {
            if (isPlayer)
                return 0;

            for (int i = 0; i < _distanceBucketThresholds.Length; i++)
            {
                if (distance < aggroRange * _distanceBucketThresholds[i])
                    return i;
            }

            return _distanceBucketThresholds.Length;
        }

        protected override bool ShouldBeRemovedFromAggroList(GameLiving living)
        {
            // Targets that left the turret's range are dropped (OpenDAoC-Core 93310604a).
            return base.ShouldBeRemovedFromAggroList(living) || !Body.IsWithinRadius(living, AggroRange);
        }

        protected override bool ShouldBeIgnoredFromAggroList(GameLiving living)
        {
            // We always return true because we don't care about what `CleanUpAggroListAndGetHighestModifiedThreat` returns.
            // This is just an opportunity to build a filtered aggro list, to be used by `CalculateNextAttackTarget`.
            if (LivingHasEffect(living, ((TurretPet) Body).TurretSpell) ||
                living.effectListComponent.ContainsEffectForEffectType(eEffect.SnareImmunity) ||
                base.ShouldBeIgnoredFromAggroList(living))
            {
                return true;
            }

            _filteredAggroList.Add(living);
            _filteredByBucket[DistanceBucket(living is GamePlayer, Body.GetDistanceTo(living), AggroRange)].Add(living);
            return true;
        }

        protected override GameLiving CleanUpAggroListAndGetHighestModifiedThreat()
        {
            _filteredAggroList.Clear();

            foreach (List<GameLiving> bucket in _filteredByBucket)
                bucket.Clear();

            return base.CleanUpAggroListAndGetHighestModifiedThreat();
        }

        protected override GameLiving CalculateNextAttackTarget()
        {
            if (AnimistSingleTargetPolicy.AppliesTo(Owner))
                return base.CalculateNextAttackTarget();

            CleanUpAggroListAndGetHighestModifiedThreat();

            // Prioritize targets that don't already have our effect and aren't immune to it.
            // If there's none, allow them to be attacked again but only if our spell does damage.
            // Among those, the closest non-empty distance bucket wins, a random target inside it.
            if (_filteredAggroList.Count > 0)
            {
                foreach (List<GameLiving> bucket in _filteredByBucket)
                {
                    if (bucket.Count > 0)
                        return bucket[Util.Random(bucket.Count - 1)];
                }

                return _filteredAggroList[Util.Random(_filteredAggroList.Count - 1)];
            }
            else if ((Body as TurretPet).TurretSpell.Damage > 0)
            {
                List<GameLiving> tempAggroList = GameLoop.GetListForTick<GameLiving>();
                tempAggroList.AddRange(AggroList.Keys); // Wasteful, but we don't expect this to be called often.

                if (tempAggroList.Count != 0)
                    return tempAggroList[Util.Random(tempAggroList.Count - 1)];
            }

            return null;
        }

        public override void UpdatePetWindow() { }
        public override void OnAttackedByEnemy(AttackData ad) { }
    }
}
