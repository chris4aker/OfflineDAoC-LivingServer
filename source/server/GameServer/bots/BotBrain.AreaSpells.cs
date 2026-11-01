using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Area spells (owner 2026-10-28, GitHub issue #17: gamebots and companions never used their area damage on two or
    /// more monsters). A point blank spell (range 0) hits every enemy around the caster, so the old range check (the
    /// target within the spell's range of 0) never let one be cast. Rules in <see cref="BotAoePolicy"/>.
    /// </summary>
    public partial class BotBrain
    {
        private readonly List<BotAoePolicy.Candidate> _blastCandidates = new();

        private Spell _pointBlankSpell;
        private Vector3 _pointBlankSpot;
        private long _pointBlankWalkUntil;
        private long _pointBlankBackoffUntil;
        private int _pointBlankFailures;
        private GameObject _pointBlankFight;
        private Spell _pointBlankCast;
        private int _pointBlankManaBefore;
        private long _nextPointBlankLook;
        private long _nextAreaReport;

        private static bool IsMezSpell(Spell spell) => spell?.SpellType is eSpellType.Mesmerize or eSpellType.Mez;

        /// <summary>The bot's own side: itself, and the players and bots of its realm with their pets.</summary>
        private bool IsAlliedFighter(GameLiving living)
        {
            if (living is GameNPC { Brain: IControlledBrain controlled } && controlled.GetLivingOwner() is GameLiving owner)
                living = owner;
            return living == Body || living is IGamePlayer && living.Realm == Body.Realm;
        }

        /// <summary>
        /// A monster already in this fight: on the bot's aggro list, or attacking someone of its side. Enemy players,
        /// bots and their pets always are. Anything else would be pulled by the blast.
        /// </summary>
        private bool IsEngagedEnemy(GameLiving enemy)
        {
            if (BotPvpCrowdControl.PlayerLike(enemy) || enemy is GameNPC { Brain: IControlledBrain })
                return true;
            if (AggroList.ContainsKey(enemy))
                return true;
            return (enemy.InCombat || enemy.IsAttacking) && enemy.TargetObject is GameLiving victim && victim.IsAlive &&
                IsAlliedFighter(victim);
        }

        /// <summary>The blast of <paramref name="spell"/> centred on <paramref name="center"/> (the caster for a point blank spell).</summary>
        private BotAoePolicy.Blast EvaluateBlast(GameObject center, Spell spell, int extraSlack = 0)
        {
            int radius = Math.Max(1, spell.Radius);
            ushort look = (ushort)Math.Min(ushort.MaxValue, radius + BotAoePolicy.SafetyMargin);
            _blastCandidates.Clear();
            foreach (GameNPC npc in center.GetNPCsInRadius(look))
                AddBlastCandidate(center, npc);
            foreach (GamePlayer player in center.GetPlayersInRadius(look))
                AddBlastCandidate(center, player);
            return BotAoePolicy.Evaluate(_blastCandidates, radius - BotAoePolicy.EdgeMargin - extraSlack, radius, IsMezSpell(spell));
        }

        private void AddBlastCandidate(GameObject center, GameLiving living)
        {
            // What the spell itself would skip (the caster's side, the untargetable, the dead) is neither counted nor feared.
            if (living == null || living == Body || !living.IsAlive || living.ObjectState != GameObject.eObjectState.Active ||
                IsUntargetable(living) || !GameServer.ServerRules.IsAllowedToAttack(Body, living, true))
                return;
            // Mesmerized, or rooted by a root that damage breaks: the blast would free it.
            _blastCandidates.Add(new(center.GetDistanceTo(living), IsEngagedEnemy(living), IsBreakablyControlled(living)));
        }

        private bool HasBlastPlayerLike()
        {
            foreach (GameNPC npc in Body.GetNPCsInRadius(1_200))
                if (npc != Body && npc.IsAlive && BotPvpCrowdControl.PlayerLike(npc) &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, npc, true))
                    return true;
            foreach (GamePlayer player in Body.GetPlayersInRadius(1_200))
                if (player.IsAlive && GameServer.ServerRules.IsAllowedToAttack(Body, player, true))
                    return true;
            return false;
        }

        /// <summary>A monster within reach is already hitting this caster: a spell with a cast time would only be interrupted.</summary>
        private bool EnemyOnCaster()
        {
            if (RecentDirectAttacker() is GameLiving attacker && Body.IsWithinRadius(attacker, 600))
                return true;
            foreach (GameNPC npc in Body.GetNPCsInRadius(600))
                if (npc != Body && npc.IsAlive && npc.TargetObject == Body && !npc.IsMezzed && !npc.IsStunned &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, npc, true))
                    return true;
            return false;
        }

        private bool HasGroupToHoldTheMonsters() => Body.Group != null && Body.Group.MemberCount >= 2;

        /// <summary>An instant point blank spell (Thane, Reaver, Cleric, Shaman): worth it when it hits two or more and wakes or pulls nothing.</summary>
        private bool InstantPointBlankWorthwhile(Spell spell, GameLiving target)
        {
            // A pulsing aura (Reaver): one at a time. Starting a pulsing spell ends the others, so the Reaver's three
            // auras cancelled each other every turn and it stood in place (run of 2026-10-28: 1,374 stuck Reavers).
            if (IsHarmfulAura(spell) && ActiveHarmfulAura() != null)
                return false;
            BotAoePolicy.Blast blast = EvaluateBlast(Body, spell);
            if (!blast.Worthwhile)
                return false;
            // A debuff already on the kill target is not renewed for the others' sake.
            return BotCasterPriority.IsDamage(spell) || target == null || !LivingHasEffect(target, spell);
        }

        /// <summary>A targeted area spell is refused when it would also reach a mesmerized monster or one not in the fight.</summary>
        private bool AreaBlastVetoed(Spell spell, GameObject target) =>
            spell?.Radius > 0 && !spell.IsPBAoE && target != null && EvaluateBlast(target, spell).Vetoed;

        /// <summary>
        /// Among the castable spells, the area damage spell whose total over the monsters it hits beats the best single-target
        /// damage spell, or null. Vetoed area spells are removed from <paramref name="spells"/>.
        /// </summary>
        private Spell ChooseAreaDamage(List<Spell> spells, GameLiving target)
        {
            if (target == null)
                return null;
            Dictionary<(int, bool), BotAoePolicy.Blast> blasts = null;
            BotAoePolicy.Blast BlastOf(Spell spell)
            {
                blasts ??= new();
                (int, bool) key = (spell.Radius, IsMezSpell(spell));
                if (!blasts.TryGetValue(key, out BotAoePolicy.Blast blast))
                    blasts[key] = blast = EvaluateBlast(target, spell);
                return blast;
            }

            spells.RemoveAll(spell => spell.Radius > 0 && !spell.IsPBAoE && BlastOf(spell).Vetoed);
            double single = spells.Where(spell => spell.Radius <= 0 && BotCasterPriority.IsDamage(spell))
                .Select(spell => spell.Damage).DefaultIfEmpty(0).Max();
            Spell best = null;
            double bestTotal = 0;
            foreach (Spell spell in spells)
            {
                if (spell.Radius <= 0 || spell.IsPBAoE || !BotCasterPriority.IsDamage(spell))
                    continue;
                BotAoePolicy.Blast blast = BlastOf(spell);
                if (!blast.Worthwhile || !BotAoePolicy.BeatsSingle(spell.Damage, blast.Hits, single))
                    continue;
                double total = spell.Damage * blast.Hits;
                if (total > bestTotal)
                {
                    best = spell;
                    bestTotal = total;
                }
            }
            if (best != null)
                ReportArea("AREA", best, BlastOf(best).Hits);
            return best;
        }

        /// <summary>A ground-targeted area spell lands on the target: bots never set a ground target, so these always failed.</summary>
        private void AimGroundSpell(Spell spell, GameLiving target)
        {
            if (spell?.Target == eSpellTarget.AREA && spell.Range > 0 && target != null)
                Body.SetGroundTarget(target.X, target.Y, target.Z);
        }

        private bool PointBlankReady(Spell spell) =>
            spell != null && spell.Level <= Body.Level && Body.GetSkillDisabledDuration(spell) <= 0 &&
            Body.Mana >= BotBody.PowerCost(spell) && !IsOutclassedDamageSpell(spell);

        /// <summary>The caster's best point blank damage spell with a cast time (Wizard, Theurgist, Eldritch, Enchanter, Mentalist, Spiritmaster, Runemaster, Bonedancer).</summary>
        private Spell BestPointBlankSpell() =>
            Body.HarmfulSpells?.Where(spell => spell != null && spell.IsPBAoE && spell.CastTime > 0 &&
                    spell.Target == eSpellTarget.ENEMY && BotCasterPriority.IsDamage(spell) && PointBlankReady(spell))
                .OrderByDescending(spell => spell.Damage).ThenByDescending(spell => spell.Level).FirstOrDefault();

        private void EndPointBlankWalk(bool failed, long backoff)
        {
            if (_pointBlankSpell == null)
                return;
            _pointBlankSpell = null;
            Body.StopMovingOnPath();
            Body.StopMoving();
            if (failed)
                _pointBlankFailures++;
            if (backoff > 0)
                _pointBlankBackoffUntil = GameLoop.GameLoopTime + backoff;
        }

        private bool CastPointBlank(Spell spell, int hits)
        {
            Body.StopMovingOnPath();
            Body.StopMoving();
            int manaBefore = Body.Mana;
            if (!CheckOffensiveSpells(spell))
                return false;
            _pointBlankCast = spell;
            _pointBlankManaBefore = manaBefore;
            ReportArea("POINT_BLANK", spell, hits);
            return true;
        }

        /// <summary>
        /// A caster's point blank spell with a cast time. Cast where it stands when two or more monsters already in the fight
        /// are around it; otherwise, while its group holds the monsters (none on the caster), walk up to 700 units to where it
        /// reaches two or more and cast there. A hit or an interrupted cast stops all of this for 15 s, and two failures end it
        /// for the fight, so a caster never loops walking in, being hit and walking in again. Never a walk-in among enemy players.
        /// </summary>
        private bool TryPointBlankArea()
        {
            long now = GameLoop.GameLoopTime;
            if (_pointBlankFight is not GameLiving { IsAlive: true } || _pointBlankFight != Body.TargetObject && !HasAggro)
            {
                _pointBlankFight = Body.TargetObject;
                _pointBlankFailures = 0;
            }

            // The outcome of the last cast: power is only spent when a spell lands, so an unspent cast was interrupted.
            if (_pointBlankCast != null)
            {
                bool landed = Body.Mana <= _pointBlankManaBefore - Math.Max(1, BotBody.PowerCost(_pointBlankCast) / 2);
                if (landed)
                    _pointBlankFailures = 0;
                else
                {
                    _pointBlankFailures++;
                    _pointBlankBackoffUntil = now + BotAoePolicy.PointBlankBackoffMilliseconds;
                }
                _pointBlankCast = null;
            }

            if (_pointBlankSpell != null)
            {
                if (now >= _pointBlankWalkUntil || !PointBlankReady(_pointBlankSpell))
                {
                    EndPointBlankWalk(true, 5_000);
                    return false;
                }
                if (Body.IsBeingInterruptedByOther || EnemyOnCaster())
                {
                    EndPointBlankWalk(true, BotAoePolicy.PointBlankBackoffMilliseconds);
                    return false;
                }
                BotAoePolicy.Blast here = EvaluateBlast(Body, _pointBlankSpell);
                bool arrived = Vector3.Distance(new(Body.X, Body.Y, Body.Z), _pointBlankSpot) <= 60 || !Body.IsMoving;
                if (here.Vetoed)
                {
                    EndPointBlankWalk(true, 5_000);
                    return false;
                }
                if (here.Worthwhile && arrived)
                {
                    Spell spell = _pointBlankSpell;
                    _pointBlankSpell = null;
                    return CastPointBlank(spell, here.Hits);
                }
                if (arrived)
                {
                    EndPointBlankWalk(true, 5_000);
                    return false;
                }
                return true; // still walking: this owns the turn
            }

            if (now < _nextPointBlankLook || Body.IsBeingInterrupted || now < _pointBlankBackoffUntil ||
                _pointBlankFailures >= BotAoePolicy.PointBlankFailuresPerFight)
                return false;
            _nextPointBlankLook = now + 1_000;
            Spell best = BestPointBlankSpell();
            if (best == null || EnemyOnCaster())
                return false;

            BotAoePolicy.Blast blast = EvaluateBlast(Body, best);
            if (blast.Worthwhile)
                return CastPointBlank(best, blast.Hits);
            if (!BotAoePolicy.MayWalkIn(HasGroupToHoldTheMonsters(), false, Body.IsBeingInterrupted, now,
                    _pointBlankBackoffUntil, _pointBlankFailures) || Body.MaxSpeed <= 0 || HasBlastPlayerLike())
                return false;

            GamePlayer defensiveLeader = CompanionEngagementMode.DefensiveLeader(Body);
            List<GameNPC> centres = Body.GetNPCsInRadius((ushort)(BotAoePolicy.PointBlankWalkLimit + BotAoePolicy.ApproachSlack))
                .Where(npc => npc != Body && npc.IsAlive && !npc.IsMezzed && !IsUntargetable(npc) &&
                    GameServer.ServerRules.IsAllowedToAttack(Body, npc, true) && IsEngagedEnemy(npc) &&
                    (defensiveLeader == null || defensiveLeader.IsWithinRadius(npc, CompanionEngagementMode.DefensiveRadius)))
                .OrderBy(npc => Body.GetDistanceTo(npc)).Take(8).ToList();
            if (centres.Count < BotAoePolicy.MinimumTargets)
                return false;
            List<(BotAoePolicy.Blast, float)> spots = new(centres.Count);
            foreach (GameNPC centre in centres)
                spots.Add((EvaluateBlast(centre, best, BotAoePolicy.ApproachSlack),
                    Math.Max(0, Body.GetDistanceTo(centre) - BotAoePolicy.ApproachSlack)));
            int chosen = BotAoePolicy.ChooseSpot(spots);
            if (chosen < 0)
                return false;

            GameNPC target = centres[chosen];
            Vector3 from = new(Body.X, Body.Y, Body.Z);
            Vector3 to = new(target.X, target.Y, target.Z);
            float distance = Vector3.Distance(from, to);
            _pointBlankSpot = distance <= BotAoePolicy.ApproachSlack ? from : to + (from - to) / distance * BotAoePolicy.ApproachSlack;
            _pointBlankSpell = best;
            _pointBlankWalkUntil = now + BotAoePolicy.PointBlankWalkMilliseconds;
            BotBody.WakeRecoveryRest();
            Body.StopAttack();
            Body.StopFollowing();
            Body.PathTo(_pointBlankSpot, Body.MaxSpeed);
            ReportArea("POINT_BLANK_WALK", best, spots[chosen].Item1.Hits);
            return true;
        }

        private long _auraIdleSince;
        private const long AuraIdleMilliseconds = 3_000;

        /// <summary>A pulsing point blank spell that harms: the Reaver's auras and curses.</summary>
        private static bool IsHarmfulAura(Spell spell) => spell is { IsPulsing: true, IsHarmful: true, IsPBAoE: true };

        private ECSPulseEffect ActiveHarmfulAura()
        {
            if (Body.effectListComponent?.ContainsEffectForEffectType(eEffect.Pulse) != true)
                return null;
            foreach (ECSPulseEffect pulse in Body.effectListComponent.GetPulseEffects())
                if (!pulse.IsEnding && !pulse.IsEnded && IsHarmfulAura(pulse.SpellHandler?.Spell))
                    return pulse;
            return null;
        }

        /// <summary>
        /// Every turn: a harmful aura runs only while a monster in the fight is within its reach. It is ended at once
        /// when it would reach a mesmerized monster or one not in the fight, and three seconds after nothing in the
        /// fight is in reach (the fight is over), so a Reaver never walks on with an aura pulsing.
        /// </summary>
        private void WatchHarmfulAura()
        {
            ECSPulseEffect aura = ActiveHarmfulAura();
            if (aura == null)
            {
                _auraIdleSince = 0;
                return;
            }
            BotAoePolicy.Blast blast = EvaluateBlast(Body, aura.SpellHandler.Spell);
            long now = GameLoop.GameLoopTime;
            if (blast.Vetoed)
            {
                aura.End();
                _auraIdleSince = 0;
                return;
            }
            if (blast.Hits > 0)
            {
                _auraIdleSince = 0;
                return;
            }
            if (_auraIdleSince == 0)
                _auraIdleSince = now;
            else if (now - _auraIdleSince >= AuraIdleMilliseconds)
            {
                aura.End();
                _auraIdleSince = 0;
            }
        }

        private void ReportArea(string kind, Spell spell, int hits)
        {
            long now = GameLoop.GameLoopTime;
            if (now < _nextAreaReport)
                return;
            _nextAreaReport = now + 30_000;
            log.Info($"BOT_AREA_SPELL kind={kind} bot=\"{Body.Name}\" class=\"{BotBody.ClassName}\" spell=\"{spell.Name}\" " +
                     $"hits={hits} radius={spell.Radius} region={Body.CurrentRegionID} at={Body.X},{Body.Y} " +
                     $"companion={BotBody.IsTemporaryGroupHelper}");
        }
    }
}
