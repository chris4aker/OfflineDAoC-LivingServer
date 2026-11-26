using System;
using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    public partial class BotBrain
    {
        private long _nextPvpControl;
        private int _pvpControlSpellId;
        private long _pvpControlCastUntil;
        /// <summary>#568 C7: no area mez attempt before this (60 s after the last one).</summary>
        private long _nextPvpAreaMez;

        /// <summary>#568 C7 (DetShaft abd63c964): an area mez is held this long after an attempt.</summary>
        public const long PvpAreaMezHoldMilliseconds = 60_000;
        /// <summary>#568 C7: an area mez with at least this cast time is a slow backup, tried after the others.</summary>
        public const int SlowAreaMezCastMilliseconds = 4_000;

        /// <summary>
        /// Pure (#568 C7, DetShaft abd63c964 spec): an area mez only with two or more free (not mezzed, not immune) enemies in
        /// its area, and not again within 60 s of the last attempt (a pack already mezzed or immune is left alone).
        /// </summary>
        public static bool PvpAreaMezWorthwhile(int freeEnemiesInArea, long now, long heldUntil) =>
            freeEnemiesInArea >= 2 && now >= heldUntil;

        /// <summary>Pure (#568 C7): no mez or root on an enemy a groupmate is hitting (the hit breaks it at once).</summary>
        public static bool PvpMezOrRootAllowed(bool mezOrRoot, bool groupmateHitsIt) => !mezOrRoot || !groupmateHitsIt;

        /// <summary>
        /// Pure (#568 C7): the order PvP control is tried in - a quick area mez, a single mez, a slow (4 s or more) backup
        /// area mez, then the rest; higher level first within each.
        /// </summary>
        public static int PvpControlRank(bool mez, int radius, int castTimeMilliseconds) =>
            !mez ? 3 : radius <= 0 ? 1 : castTimeMilliseconds >= SlowAreaMezCastMilliseconds ? 2 : 0;

        /// <summary>A groupmate (or a groupmate's pet) is meleeing, shooting or casting at <paramref name="enemy"/>.</summary>
        private bool GroupmateHits(GameLiving enemy)
        {
            Group group = Body.Group;
            if (group == null || enemy == null)
                return false;
            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                if (member == Body || !member.IsAlive)
                    continue;
                if (member.TargetObject == enemy && (member.IsAttacking || member.IsCasting))
                    return true;
                if (member.ControlledBrain?.Body is GameNPC pet && pet.IsAlive && pet.TargetObject == enemy && pet.IsAttacking)
                    return true;
            }
            return false;
        }
        private bool PvpControlInFlight => Body.IsCasting && GameLoop.GameLoopTime<_pvpControlCastUntil &&
            Body.castingComponent?.SpellHandler?.Spell?.ID==_pvpControlSpellId;
        private bool TryPvpCrowdControl()
        {
            if (RvrTacticsCrowdControl() is { } rvrPlanned) return rvrPlanned;
            if (BotBody?.CanCastCrowdControlSpells != true || Body.IsCasting || Body.IsIncapacitated ||
                Body.castingComponent?.HasPendingSkillRequests == true || GameLoop.GameLoopTime < _nextPvpControl) return false;
            _nextPvpControl = GameLoop.GameLoopTime + 2000;
            if (!AggroList.Keys.Any(BotPvpCrowdControl.PlayerLike) && !BotPvpCrowdControl.PlayerLike(Body.TargetObject as GameLiving) &&
                Body.Group?.GetMembersInTheGroup().Any(m=>m.IsAttacking && BotPvpCrowdControl.PlayerLike(m.TargetObject as GameLiving))!=true) return false;
            // PvE never enters this policy; native levels, specs, mana, immunity,
            // interruption and spell timers still decide whether a cast succeeds.
            GameLiving[] enemies = Body.GetNPCsInRadius(1800).Where(BotPvpCrowdControl.PlayerLike).Cast<GameLiving>()
                .Concat(Body.GetPlayersInRadius(1800)).Where(t => BotSiegeRuntime.LegalEnemy(Body,t) && !t.IsMezzed &&
                    !t.IsStealthed && !BotPvpCrowdControl.Protected(Body,t) && CompanionEngagementMode.Allows(Body,t) &&
                    !CompanionPvpEngagement.Focused(Body,t) && !CompanionPvpEngagement.Defending(Body,t))
                .OrderBy(Body.GetDistanceTo).Take(24).ToArray();
            if (enemies.Length == 0) return false;
            // #568 C7 (DetShaft abd63c964): a quick area mez, a single mez, a slow backup area mez, then roots
            foreach (Spell spell in BotBody.CrowdControlSpells
                         .OrderBy(s=>PvpControlRank(s.SpellType==eSpellType.Mesmerize, s.Radius, s.CastTime))
                         .ThenByDescending(s=>s.Level))
            {
                if (spell.Level>Body.Level || Body.GetSkillDisabledDuration(spell)>0 || Body.Mana<BotBody.PowerCost(spell) ||
                    BotInterruptPolicy.CastWouldBeLost(Body, spell)) continue;
                bool mez = spell.SpellType is eSpellType.Mesmerize or eSpellType.Mez;
                bool root = BotControlSpells.KindOfType(spell) == BotControlSpells.ControlKind.Root;
                bool areaMez = mez && spell.Radius > 0;
                if (areaMez && GameLoop.GameLoopTime < _nextPvpAreaMez) continue;
                int range = spell.Target==eSpellTarget.SELF || spell.Range<=0 ? spell.Radius : spell.CalculateEffectiveRange(Body);
                // #521: the older path reads the server's immunity too (it wasted casts on immune targets).
                // #568 C7: roots read it as well; an enemy a groupmate is hitting gets no mez or root.
                bool Free(GameLiving t) =>
                    !(mez && RvrCrowdControlPlan.MezImmune(t,spell.Duration,Body)) &&
                    !(spell.SpellType==eSpellType.Stun && RvrCrowdControlPlan.StunImmune(t,spell.Duration,Body)) &&
                    !(root && RvrCrowdControlPlan.RootImmune(t)) &&
                    PvpMezOrRootAllowed(mez || root, GroupmateHits(t));
                GameLiving candidate=enemies.Where(t=>Body.IsWithinRadius(t,range) && !LivingHasEffect(t,spell) && Free(t) &&
                    NeedsOffensiveSpellApplication(t,spell)).OrderByDescending(t=>enemies.Count(n=>n.IsWithinRadius(t,Math.Max(1,spell.Radius))))
                    .ThenBy(Body.GetDistanceTo).FirstOrDefault(t=>BotSiegeRuntime.Visible(Body,t));
                if (candidate != null && areaMez)
                {
                    GameLiving centre = spell.Target == eSpellTarget.SELF ? Body : candidate;
                    int free = enemies.Count(t => t.IsWithinRadius(centre, spell.Radius) && Free(t));
                    if (!PvpAreaMezWorthwhile(free, GameLoop.GameLoopTime, _nextPvpAreaMez)) continue;
                }
                if (candidate==null || !BotPvpCrowdControl.Reserve(BotBody,candidate,spell.CastTime+2000,spell)) continue;
                if (areaMez) _nextPvpAreaMez = GameLoop.GameLoopTime + PvpAreaMezHoldMilliseconds;
                GameObject previous=Body.TargetObject;
                Body.TargetObject=candidate;
                if (spell.CastTime>0) { Body.StopMovingOnPath(); Body.StopMoving(); }
                bool started=CheckOffensiveSpells(spell);
                if (!started) { BotPvpCrowdControl.Release(BotBody,candidate); Body.TargetObject=previous; continue; }
                _pvpControlSpellId=spell.ID; _pvpControlCastUntil=GameLoop.GameLoopTime+spell.CastTime+2000;
                if (spell.SpellType==eSpellType.Mesmerize && spell.Radius>0)
                    foreach(GameLiving enemy in enemies.Where(t=>t.IsWithinRadius(spell.Target==eSpellTarget.SELF ? Body : candidate,spell.Radius)))
                        BotPvpCrowdControl.Reserve(BotBody,enemy,spell.CastTime+2000,spell);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Open-field RvR, work package 2 (RvrCrowdControlPlan): the plan's crowd control on <paramref name="subject"/>,
        /// never on <paramref name="killTarget"/>. Mez first, then a stun spell, then a root; a kind the server would
        /// refuse (its immunity rules, read as its spell handlers read them) is skipped, and an area mez that would reach
        /// the kill target is not cast. A subject already held is left as it is; a free one (a resisted or broken mez)
        /// is cast at again. Native levels, power, timers, range, sight and interruption decide as in TryPvpCrowdControl.
        /// Stun styles (slams) cannot be chosen on demand by the class AI, so only stun spells are used here.
        /// </summary>
        private bool TryPlannedCrowdControl(GameLiving subject, GameLiving killTarget, out RvrControlKind kind)
        {
            kind = RvrControlKind.None;
            if (BotBody == null || subject == null || ReferenceEquals(subject, killTarget) || Body.IsCasting ||
                Body.IsIncapacitated || Body.castingComponent?.HasPendingSkillRequests == true ||
                !subject.IsAlive || subject.IsMezzed || subject.IsStunned || subject.IsStealthed ||
                !BotSiegeRuntime.LegalEnemy(Body, subject))
                return false;
            var killSpot = killTarget == null ? (System.Numerics.Vector3?)null : new System.Numerics.Vector3(killTarget.X, killTarget.Y, 0);
            var spells = new System.Collections.Generic.List<(Spell Spell, RvrControlKind Kind)>();
            if (BotBody.CrowdControlSpells != null)
                foreach (Spell spell in BotBody.CrowdControlSpells.OrderByDescending(s => s.Radius).ThenByDescending(s => s.Level))
                    if (spell.SpellType == eSpellType.Mesmerize)
                        spells.Add((spell, RvrControlKind.Mez));
            foreach (Spell spell in (Body.InstantHarmfulSpells ?? new()).Concat(Body.HarmfulSpells ?? new())
                         .Where(s => s.SpellType == eSpellType.Stun).OrderBy(s => s.CastTime).ThenByDescending(s => s.Level))
                spells.Add((spell, RvrControlKind.Stun));
            if (BotBody.CrowdControlSpells != null)
                foreach (Spell spell in BotBody.CrowdControlSpells.Where(s => s.SpellType != eSpellType.Mesmerize).OrderByDescending(s => s.Level))
                    spells.Add((spell, RvrControlKind.Root));
            foreach ((Spell spell, RvrControlKind spellKind) in spells)
            {
                if (spell.Level > Body.Level || Body.GetSkillDisabledDuration(spell) > 0 || Body.Mana < BotBody.PowerCost(spell) ||
                    BotInterruptPolicy.CastWouldBeLost(Body, spell))
                    continue;
                bool immune = spellKind switch
                {
                    RvrControlKind.Mez => RvrCrowdControlPlan.MezImmune(subject, spell.Duration, Body),
                    RvrControlKind.Stun => RvrCrowdControlPlan.StunImmune(subject, spell.Duration, Body),
                    _ => RvrCrowdControlPlan.RootImmune(subject),
                };
                if (immune)
                    continue;
                bool self = spell.Target == eSpellTarget.SELF || spell.Range <= 0;
                int range = self ? spell.Radius : spell.CalculateEffectiveRange(Body);
                GameLiving centre = self ? Body : subject;
                if (spell.Radius > 0 && !RvrCrowdControlPlan.AreaSpareKillTarget(new(centre.X, centre.Y, 0), spell.Radius, killSpot))
                    continue;
                if (!Body.IsWithinRadius(subject, range) || LivingHasEffect(subject, spell) ||
                    !NeedsOffensiveSpellApplication(subject, spell) || !BotSiegeRuntime.Visible(Body, subject) ||
                    !BotPvpCrowdControl.Reserve(BotBody, subject, spell.CastTime + 2000, spell))
                    continue;
                GameObject previous = Body.TargetObject;
                Body.TargetObject = subject;
                if (spell.CastTime > 0) { Body.StopMovingOnPath(); Body.StopMoving(); }
                if (!CheckOffensiveSpells(spell))
                {
                    BotPvpCrowdControl.Release(BotBody, subject);
                    Body.TargetObject = previous;
                    continue;
                }
                _pvpControlSpellId = spell.ID;
                _pvpControlCastUntil = GameLoop.GameLoopTime + spell.CastTime + 2000;
                kind = spellKind;
                return true;
            }
            return false;
        }
    }
}
