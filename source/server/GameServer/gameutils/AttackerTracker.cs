using System;
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

        /// <summary>A real player lowers an epic NPC's armor factor scaling this much.</summary>
        public const double PlayerArmorFactorStep = 0.04;
        /// <summary>A pet (up to the epic NPC's pet cap) lowers it this much.</summary>
        public const double PetArmorFactorStep = 0.01;
        /// <summary>It never drops below this.</summary>
        public const double ArmorFactorScalingFloor = 0.4;

        /// <summary>
        /// Offline DAoC: the share (0 to 1) of a player's armor drop that one gamebot (autonomous or companion) gives, and of
        /// the default-to-floor range that bots alone can take off. Owner, 2026-11-14: bots stand in for players, but at half
        /// strength, so a bot raid never turns an epic into a zerg fest. Server properties <c>epic_armor_counts_bots</c>
        /// (off: 0, the stock players-only rule) and <c>epic_armor_bot_strength_percent</c> (default 50).
        /// </summary>
        public static double BotArmorFactorShare()
        {
            if (!ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS)
                return 0;

            int percent = ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT;
            return Math.Clamp(percent, 0, 100) / 100.0;
        }

        /// <summary>
        /// The epic NPC armor factor scaling for a set of live attackers: real players -0.04 each, pets -0.01 each up to the
        /// pet cap, gamebots <see cref="BotArmorFactorShare"/> of a player's step each, and the bots' total drop capped at
        /// that share of the default-to-floor range (a GameEpicBoss: 1.6 to 1.0 at 50%); never below 0.4.
        /// </summary>
        public static double ScaledArmorFactor(double defaultFactor, int petCap, IEnumerable<GameLiving> attackers)
        {
            int players = 0, bots = 0, pets = 0;

            foreach (GameLiving attacker in attackers)
            {
                if (attacker is GamePlayer)
                    players++;
                else if (attacker is GameBot)
                    bots++;
                else if (attacker is GameSummonedPet && pets <= petCap) // the stock loop counted cap + 1 pets
                    pets++;
            }

            return ScaledArmorFactor(defaultFactor, players, bots, pets, BotArmorFactorShare());
        }

        /// <summary>The armor factor scaling for these attacker counts (pure); see the overload above.</summary>
        public static double ScaledArmorFactor(double defaultFactor, int players, int bots, int pets, double botShare)
        {
            botShare = Math.Clamp(botShare, 0, 1);
            double botDrop = Math.Min(bots * PlayerArmorFactorStep * botShare,
                Math.Max(0, defaultFactor - ArmorFactorScalingFloor) * botShare);
            double factor = defaultFactor - players * PlayerArmorFactorStep - pets * PetArmorFactorStep - botDrop;
            return factor < ArmorFactorScalingFloor ? ArmorFactorScalingFloor : factor;
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
