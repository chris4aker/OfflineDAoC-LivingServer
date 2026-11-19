using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// A raid boss's own rules that a gamebot plays by, from Offline DAoC True 1.65 (shadowofze, BotBrain.RaidBoss): the
    /// damage range of <see cref="IRaidBossTraits"/> (Legion) and the boss pets cannot hurt (Summoner Lossren). The traits
    /// are read once when the bot's target changes and kept with the target reference: no scan, no allocation per think.
    /// </summary>
    public partial class BotBrain
    {
        private GameObject _raidTraitsFor;
        private IRaidBossTraits _raidTraits;

        private IRaidBossTraits RaidTraitsOf(GameObject target)
        {
            if (!ReferenceEquals(target, _raidTraitsFor))
            {
                _raidTraitsFor = target;
                _raidTraits = target as IRaidBossTraits;
            }
            return _raidTraits;
        }

        /// <summary>
        /// df-bosses L2: a ranged bot's stand-off range against <paramref name="target"/>. Legion takes no damage from
        /// beyond 1,400, but casters walked to their longest spell range less 100 (bolts: 1,775) and archers to bow range
        /// less 100, so all their damage was void. Capped at the boss's limit less a margin.
        /// </summary>
        private int CapRangeForTarget(GameObject target, int ownRange)
        {
            IRaidBossTraits traits = RaidTraitsOf(target);
            return traits == null ? ownRange : RaidBossTraits.CapStandOff(ownRange, traits.RaidMaxDamageRange);
        }

        /// <summary>The bot stands where its current target takes no damage from it: no attack spell from here, walk in.</summary>
        private bool BeyondTargetDamageRange()
        {
            GameObject target = Body.TargetObject;
            IRaidBossTraits traits = RaidTraitsOf(target);
            return traits != null && traits.RaidMaxDamageRange > 0 &&
                RaidBossTraits.BeyondDamageRange(traits.RaidMaxDamageRange, Body.GetDistanceTo(target));
        }

        /// <summary>
        /// sh-bosses M2, df-bosses M5: a boss a summoned pet cannot hurt. Summoner Lossren takes 5 damage from any summoned
        /// pet (his own TakeDamage), so the pet classes' pets did nothing on him while his souls ran free.
        /// </summary>
        public static bool PetsDoNoDamageTo(GameLiving target) => target is SummonerLossren;

        private long _petCallOffAt;

        /// <summary>
        /// The pet's target when the owner's is a boss pets cannot hurt: an add on the owner's aggro list, one on the owner
        /// or the pet first; null when there is none, and then a pet already on the boss is called back to its owner (at
        /// most once a second). Cost: a walk over the aggro list, only while the owner's target is such a boss.
        /// </summary>
        private GameLiving PetTargetInsteadOfBoss(GameLiving boss, ControlledMobBrain petBrain)
        {
            GameLiving best = null;
            foreach (GameLiving living in AggroList.Keys)
            {
                if (living == boss || living is not GameNPC npc || PetsDoNoDamageTo(npc) || !IsValidPetCombatTarget(petBrain.Body, npc) ||
                    EpicEncounterTactics.PetMustNotHit(petBrain.Body, npc))
                    continue;
                if (npc.TargetObject == Body || npc.TargetObject == petBrain.Body)
                {
                    best = npc;
                    break;
                }
                best ??= npc;
            }
            long now = GameLoop.GameLoopTime;
            if (best == null && petBrain.Body?.TargetObject == boss && now >= _petCallOffAt)
            {
                _petCallOffAt = now + 1_000;
                petBrain.Disengage();
                petBrain.Follow(Body);
            }
            return best;
        }
    }
}
