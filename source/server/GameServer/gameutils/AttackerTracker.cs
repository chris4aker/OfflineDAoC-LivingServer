using System.Collections.Generic;
using System.Threading;

namespace DOL.GS
{
    public class AttackerTracker
    {
        private readonly GameLiving _owner;
        private readonly AttackerCheckTimer _attackerCheckTimer;
        private int _meleeCount = 0;

        private readonly Dictionary<GameLiving, AttackerInfo> _attackers = new();
        private readonly Lock _lock = new();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _attackers.Count;
                }
            }
        }

        public int MeleeCount => Volatile.Read(ref _meleeCount);

        public ICollection<GameLiving> Attackers
        {
            get
            {
                List<GameLiving> result = GameLoop.GetListForTick<GameLiving>();

                lock (_lock)
                {
                    result.EnsureCapacity(_attackers.Count);

                    foreach (GameLiving key in _attackers.Keys)
                        result.Add(key);
                }

                return result;
            }
        }

        public AttackerTracker(GameLiving owner)
        {
            _owner = owner;
            _attackerCheckTimer = AttackerCheckTimer.Create(this);
        }

        public void AddOrUpdate(GameLiving attacker, bool isMelee, long expireTime)
        {
            if (attacker == _owner)
                return;

            AttackerInfo attackerInfo = new(isMelee, expireTime);

            lock (_lock)
            {
                if (_attackers.TryGetValue(attacker, out AttackerInfo existing))
                {
                    _attackers[attacker] = attackerInfo;

                    if (existing.IsMelee != isMelee)
                    {
                        if (isMelee)
                            _meleeCount++;
                        else
                            _meleeCount--;
                    }
                }
                else
                {
                    _attackers.Add(attacker, attackerInfo);
                    _attackerCheckTimer.WakeUp();

                    if (isMelee)
                        _meleeCount++;
                }
            }
        }

        public bool ContainsAttacker(GameLiving attacker)
        {
            lock (_lock)
            {
                return _attackers.ContainsKey(attacker);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _attackers.Clear();
                _meleeCount = 0;
            }

            _attackerCheckTimer.Stop();
        }

        /// <summary>
        /// Offline DAoC: who lowers an epic NPC's armor factor scaling like a player does (-0.04 each, floor 0.4).
        /// Real players always do. Gamebots (autonomous and companions) stand in for players, so they count too while
        /// the server property <c>epic_armor_counts_bots</c> is on (the default); off is the stock players-only rule.
        /// Pets stay in their own capped -0.01 bracket either way.
        /// </summary>
        public static bool CountsAsPlayerAttacker(GameLiving attacker)
        {
            if (attacker is GamePlayer)
                return true;

            return attacker is GameBot && ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS;
        }

        /// <summary>
        /// The epic NPC armor factor scaling for a set of live attackers: the default, -0.04 per player attacker
        /// (<see cref="CountsAsPlayerAttacker"/>), -0.01 per pet up to the pet cap, never below 0.4.
        /// </summary>
        public static double ScaledArmorFactor(double defaultFactor, int petCap, IEnumerable<GameLiving> attackers)
        {
            double armorFactorScalingFactor = defaultFactor;
            int petCount = 0;

            foreach (GameLiving attacker in attackers)
            {
                if (CountsAsPlayerAttacker(attacker))
                    armorFactorScalingFactor -= 0.04;
                else if (attacker is GameSummonedPet && petCount <= petCap)
                {
                    armorFactorScalingFactor -= 0.01;
                    petCount++;
                }

                if (armorFactorScalingFactor < 0.4)
                    return 0.4;
            }

            return armorFactorScalingFactor;
        }

        private readonly struct AttackerInfo
        {
            public readonly bool IsMelee;
            public readonly long ExpireTime;

            public AttackerInfo(bool isMelee, long expireTime)
            {
                IsMelee = isMelee;
                ExpireTime = expireTime;
            }
        }

        private class StandardAttackerCheckTimer : AttackerCheckTimer
        {
            public StandardAttackerCheckTimer(AttackerTracker attackerTracker) : base(attackerTracker) { }

            protected override int OnTick(ECSGameTimer timer)
            {
                lock (_attackerTracker._lock)
                {
                    foreach (var pair in _attackerTracker._attackers)
                        TryRemoveAttacker(pair);

                    return base.OnTick(timer);
                }
            }
        }

        private class EpicNpcAttackerCheckTimer : AttackerCheckTimer
        {
            private IGameEpicNpc _epicNpc;

            public EpicNpcAttackerCheckTimer(AttackerTracker attackerTracker) : base(attackerTracker)
            {
                _epicNpc = attackerTracker._owner as IGameEpicNpc;
            }

            protected override int OnTick(ECSGameTimer timer)
            {
                // Update `ArmorFactorScalingFactor`.
                lock (_attackerTracker._lock)
                {
                    foreach (var pair in _attackerTracker._attackers)
                        TryRemoveAttacker(pair);

                    _epicNpc.ArmorFactorScalingFactor = ScaledArmorFactor(
                        _epicNpc.DefaultArmorFactorScalingFactor,
                        _epicNpc.ArmorFactorScalingFactorPetCap,
                        _attackerTracker._attackers.Keys);
                    return base.OnTick(timer);
                }
            }
        }

        private abstract class AttackerCheckTimer : ECSGameTimerWrapperBase
        {
            public const int CHECK_ATTACKERS_INTERVAL = 1000;

            protected readonly GameLiving _owner;
            protected readonly AttackerTracker _attackerTracker;

            public AttackerCheckTimer(AttackerTracker attackerTracker) : base(attackerTracker._owner)
            {
                _owner = attackerTracker._owner;
                _attackerTracker = attackerTracker;
            }

            public static AttackerCheckTimer Create(AttackerTracker attackerTracker)
            {
                if (attackerTracker._owner is IGameEpicNpc)
                    return new EpicNpcAttackerCheckTimer(attackerTracker);
                else
                    return new StandardAttackerCheckTimer(attackerTracker);
            }

            public void WakeUp()
            {
                if (IsAlive)
                    return;

                Interval = CHECK_ATTACKERS_INTERVAL;
                Start();
            }

            protected override int OnTick(ECSGameTimer timer)
            {
                return _attackerTracker.Count == 0 ? 0 : CHECK_ATTACKERS_INTERVAL;
            }

            protected bool TryRemoveAttacker(in KeyValuePair<GameLiving, AttackerInfo> pair)
            {
                if (pair.Value.ExpireTime < GameLoop.GameLoopTime && _attackerTracker._attackers.Remove(pair.Key))
                {
                    if (pair.Value.IsMelee)
                        _attackerTracker._meleeCount--;

                    return true;
                }

                return false;
            }
        }
    }
}
