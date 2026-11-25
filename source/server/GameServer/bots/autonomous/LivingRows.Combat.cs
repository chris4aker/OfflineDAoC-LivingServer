using System;
using System.Globalization;
using DOL.GS.Effects;
using DOL.GS.Spells;

namespace DOL.GS;

/// <summary>
/// CAST_INTERRUPT (#625 row 2; checks #596's live interrupt): a sampled cast that an attacker's interrupt timer touched (the
/// timer ran at some point between the cast's start and its half-cast point), written where #596 decides it, at the
/// half-cast point (SpellHandler.PerformDuringCastInterruptCheck). Observe-only: it reads the caster's interrupt timer and the
/// decision already made.
/// <c>CAST_INTERRUPT seq= every= t= caster= pet= realm= spell= castMs= leftMs= outcome= why= attacker= attackerPet= region= bg=</c>
/// <list type="bullet">
/// <item><c>caster</c>, <c>attacker</c>: player, companion, gamebot or npc (a pet counts as its owner, pet=1).</item>
/// <item><c>leftMs</c>: what the interrupt timer had left at the half-cast point (0 or less: it had run out).</item>
/// <item><c>outcome</c>: kept or lost; <c>why</c>: timer (lost), expired, uninterruptible, immune (Quickcast, Mastery of
/// Concentration, Facilitate Painworking) or other.</item>
/// </list>
/// Every <see cref="LivingRowProperties.CastInterruptSampleEvery"/>th such cast, at most <see cref="PerMinute"/> a minute.
/// </summary>
public static class CastInterruptRows
{
    public const string Marker = "CAST_INTERRUPT";
    public const int PerMinute = 20;

    internal static readonly LivingRowSampler Sampler = new();

    /// <summary>Whether an interrupt timer touched a cast that began at <paramref name="castStart"/>. Pure.</summary>
    public static bool Touched(long interruptEnds, long castStart) => interruptEnds > castStart;

    /// <summary>Why a cast came out as it did. Pure.</summary>
    public static string Why(bool lost, long leftMs, bool uninterruptible, bool immune) =>
        lost ? "timer" : leftMs <= 0 ? "expired" : uninterruptible ? "uninterruptible" : immune ? "immune" : "other";

    public static string Format(long seq, int every, long now, string caster, bool casterPet, eRealm realm, string spell, long castMs,
        long leftMs, bool lost, string why, string attacker, bool attackerPet, ushort region) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Marker} seq={seq} every={every} t={now} caster={caster} pet={(casterPet ? 1 : 0)} realm={RvrRecordText.Realm(realm)} " +
            $"spell={spell} castMs={castMs} leftMs={leftMs} outcome={(lost ? "lost" : "kept")} why={why} attacker={attacker} " +
            $"attackerPet={(attackerPet ? 1 : 0)} region={region} bg={LivingRows.Battleground(region)}");

    /// <summary>
    /// Called at the half-cast point after the decision. <paramref name="immune"/> is asked only for a kept cast whose timer
    /// still ran. Never throws.
    /// </summary>
    public static void Observe(GameLiving caster, Spell spell, long castStart, long castMs, bool lost, Func<bool> immune)
    {
        try
        {
            if (caster == null || spell == null || !Touched(caster.InterruptTime, castStart))
                return;
            int every = LivingRowProperties.CastInterruptSampleEvery;
            if (!Sampler.Take(every, out long seq) || !LivingRows.Enabled || !LivingRows.Take(Marker, PerMinute))
                return;
            long now = LivingRows.Now();
            long left = caster.InterruptTime - now;
            string why = Why(lost, left, spell.Uninterruptible, !lost && left > 0 && immune != null && immune());
            string casterKind = LivingRows.ActorKind(caster, out bool casterPet);
            string attackerKind = LivingRows.ActorKind(caster.LastInterrupter, out bool attackerPet);
            LivingRows.Emit(Format(seq, every, now, casterKind, casterPet, caster.Realm, spell.SpellType.ToString(), castMs, left, lost,
                why, attackerKind, attackerPet, caster.CurrentRegionID));
        }
        catch
        {
            // Logging only.
        }
    }
}

/// <summary>
/// CC_BREAK (#625 row 3; checks #621's "mezzes held"): each break of a mez or of a root damage breaks (a speed debuff of
/// Value >= 99, not the unbreakable kind), written where the hit ends it (GameLiving.HandleCrowdControlOnAttacked).
/// <c>CC_BREAK t= cc= target= breaker= breakerPet= caster= side= ms= attack= region= bg=</c>
/// <list type="bullet">
/// <item><c>target</c>, <c>breaker</c>, <c>caster</c>: player, companion, gamebot or npc (a pet counts as its owner).</item>
/// <item><c>side</c>: self (the caster broke its own), ally (same realm as the caster), enemy, or none (caster unknown).</item>
/// <item><c>ms</c>: since the crowd control landed; <c>attack</c>: melee or spell.</item>
/// </list>
/// At most <see cref="PerMinute"/> a minute (drops are counted in RVR_HOUR).
/// </summary>
public static class CcBreakRows
{
    public const string Marker = "CC_BREAK";
    public const int PerMinute = 20;

    /// <summary>"mez", "root" (breakable, Value >= 99) or null for an effect that is neither. Pure.</summary>
    public static string Classify(eEffect effect, eSpellType spellType, double value) => effect switch
    {
        eEffect.Mez => "mez",
        eEffect.MovementSpeedDebuff when value >= 99 && spellType is not eSpellType.UnbreakableSpeedDecrease and not eSpellType.PreventFlight => "root",
        _ => null,
    };

    /// <summary>The breaker's side toward the caster. Pure.</summary>
    public static string Side(GameLiving breaker, GameLiving caster) =>
        caster == null || breaker == null ? "none"
        : ReferenceEquals(breaker, caster) ? "self"
        : breaker.Realm == caster.Realm ? "ally"
        : "enemy";

    public static string Format(long now, string cc, string target, string breaker, bool breakerPet, string caster, string side, long ms,
        bool spell, ushort region) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{Marker} t={now} cc={cc} target={target} breaker={breaker} breakerPet={(breakerPet ? 1 : 0)} caster={caster} side={side} " +
            $"ms={ms} attack={(spell ? "spell" : "melee")} region={region} bg={LivingRows.Battleground(region)}");

    /// <summary>Called just before the hit ends <paramref name="effect"/> on <paramref name="target"/>. Never throws.</summary>
    public static void Broke(GameLiving target, ECSGameEffect effect, AttackData attack)
    {
        try
        {
            if (target == null || effect == null || attack == null)
                return;
            Spell spell = (effect as ECSGameSpellEffect)?.SpellHandler?.Spell;
            string cc = Classify(effect.EffectType, spell?.SpellType ?? eSpellType.None, spell?.Value ?? 0);
            if (cc == null || !LivingRows.Enabled || !LivingRows.Take(Marker, PerMinute))
                return;
            long now = LivingRows.Now();
            GameLiving caster = (effect as ECSGameSpellEffect)?.SpellHandler?.Caster;
            GameLiving breaker = attack.Attacker;
            GameLiving breakerSide = LivingRows.PetOwner(breaker) ?? breaker;
            GameLiving casterSide = LivingRows.PetOwner(caster) ?? caster;
            string breakerKind = LivingRows.ActorKind(breaker, out bool breakerPet);
            LivingRows.Emit(Format(now, cc, LivingRows.Kind(target), breakerKind, breakerPet, LivingRows.ActorKind(caster, out _),
                Side(breakerSide, casterSide), Math.Max(0, now - effect.StartTick), attack.AttackType == AttackData.eAttackType.Spell,
                target.CurrentRegionID));
        }
        catch
        {
            // Logging only.
        }
    }
}
