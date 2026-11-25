using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS.Spells;

namespace DOL.GS.Tests.Rehearsal
{
    public sealed record RvrHit(long At, GameLiving Attacker, GameLiving Target, int Amount, AttackData.eAttackType Kind, bool OnCalledTarget);
    public sealed record RvrDeath(long At, GameLiving Victim, GameObject Killer);

    /// <summary>A mez cast that finished on a target: whether the target was mezzed after it, and the target's state then.</summary>
    public sealed class RvrMezAttempt
    {
        public long At { get; init; }
        public GameLiving Caster { get; init; }
        public GameLiving Target { get; init; }
        public int TargetHealthPercent { get; init; }
        public bool TargetIsPlayer => Target is GamePlayer;
        public bool? Landed { get; set; }
        public bool Refused => Landed == false;
    }

    /// <summary>
    /// What a rehearsal saw, from the server's own events (GameLivingEvent.AttackedByEnemy, Dying, CastStarting and
    /// CastFinished) and from each tick's world state (mez, the groups' published plans). Nothing here changes the fight.
    /// </summary>
    public sealed class RvrFightRecorder : IDisposable
    {
        private readonly RvrFightWorld _world;
        private readonly List<RvrHit> _hits = new();
        private readonly List<RvrDeath> _deaths = new();
        private readonly List<RvrMezAttempt> _mezzes = new();
        private readonly Dictionary<GameLiving, long> _mezzedSince = new();
        private readonly List<(long At, GameLiving Target)> _firstControl = new();
        private readonly Dictionary<object, List<(long At, GameLiving Target)>> _calls = new();
        private readonly DOLEventHandler _onAttacked, _onDying, _onCastFinished, _onCastStarting;
        private int _castsStarted, _castsFinished;

        public IReadOnlyList<RvrHit> Hits => _hits;
        public IReadOnlyList<RvrDeath> Deaths => _deaths;
        public IReadOnlyList<RvrMezAttempt> Mezzes => _mezzes;
        public IReadOnlyList<(long At, GameLiving Target)> ControlStarts => _firstControl;
        public int CastsStarted => _castsStarted;
        public int CastsFinished => _castsFinished;
        /// <summary>Each group's called targets in order, as its published plan named them.</summary>
        public IReadOnlyDictionary<object, List<(long At, GameLiving Target)>> Calls => _calls;

        public RvrFightRecorder(RvrFightWorld world)
        {
            _world = world;
            _onAttacked = OnAttacked;
            _onDying = OnDying;
            _onCastFinished = OnCastFinished;
            _onCastStarting = (e, sender, args) => { if (Ours(sender)) _castsStarted++; };
            GameEventMgr.AddHandler(GameLivingEvent.AttackedByEnemy, _onAttacked);
            GameEventMgr.AddHandler(GameLivingEvent.Dying, _onDying);
            GameEventMgr.AddHandler(GameLivingEvent.CastFinished, _onCastFinished);
            GameEventMgr.AddHandler(GameLivingEvent.CastStarting, _onCastStarting);
        }

        private bool Ours(object sender) => sender is GameLiving living && _world.Livings.Contains(living);

        private void OnAttacked(DOLEvent e, object sender, EventArgs args)
        {
            if (!Ours(sender) || args is not AttackedByEnemyEventArgs { AttackData: { } ad })
                return;
            int amount = ad.Damage + ad.CriticalDamage;
            if (amount <= 0 || ad.Attacker == null)
                return;
            GameLiving attacker = ad.Attacker is GameNPC { Brain: IControlledBrain pet } && pet.GetLivingOwner() is GameLiving owner ? owner : ad.Attacker;
            GameLiving called = RvrFightBoards.Peek(attacker)?.Plan?.CalledTarget;
            _hits.Add(new RvrHit(_world.Now, attacker, ad.Target, amount, ad.AttackType, called != null && called == ad.Target));
        }

        private void OnDying(DOLEvent e, object sender, EventArgs args)
        {
            if (!Ours(sender))
                return;
            _deaths.Add(new RvrDeath(_world.Now, (GameLiving)sender, (args as DyingEventArgs)?.Killer));
        }

        private void OnCastFinished(DOLEvent e, object sender, EventArgs args)
        {
            if (!Ours(sender) || args is not CastingEventArgs { SpellHandler: { } handler } cast)
                return;
            _castsFinished++;
            if (handler.Spell?.SpellType != eSpellType.Mesmerize)
                return;
            GameLiving target = cast.Target ?? (sender as GameLiving)?.TargetObject as GameLiving;
            if (target == null)
                return;
            _mezzes.Add(new RvrMezAttempt
            {
                At = _world.Now, Caster = (GameLiving)sender, Target = target, TargetHealthPercent = target.HealthPercent,
            });
        }

        /// <summary>Each tick: the mez outcomes of last tick's casts, who is newly controlled, and each group's called target.</summary>
        public void Observe()
        {
            long now = _world.Now;
            foreach (RvrMezAttempt attempt in _mezzes)
                if (attempt.Landed == null && now > attempt.At)
                    attempt.Landed = attempt.Target.IsMezzed;
            foreach (GameLiving living in _world.Livings)
            {
                bool controlled = living.IsAlive && (living.IsMezzed || living.IsStunned);
                if (controlled && !_mezzedSince.ContainsKey(living))
                {
                    _mezzedSince[living] = now;
                    _firstControl.Add((now, living));
                }
                else if (!controlled)
                    _mezzedSince.Remove(living);
                if (living.Group != null && living.Group.LivingLeader == living)
                {
                    GameLiving called = RvrFightBoards.Peek(living)?.Plan?.CalledTarget;
                    if (!_calls.TryGetValue(living.Group, out var list))
                        _calls[living.Group] = list = new List<(long, GameLiving)>();
                    if (called != null && (list.Count == 0 || list[^1].Target != called))
                        list.Add((now, called));
                }
            }
        }

        public void Dispose()
        {
            GameEventMgr.RemoveHandler(GameLivingEvent.AttackedByEnemy, _onAttacked);
            GameEventMgr.RemoveHandler(GameLivingEvent.Dying, _onDying);
            GameEventMgr.RemoveHandler(GameLivingEvent.CastFinished, _onCastFinished);
            GameEventMgr.RemoveHandler(GameLivingEvent.CastStarting, _onCastStarting);
        }
    }
}
