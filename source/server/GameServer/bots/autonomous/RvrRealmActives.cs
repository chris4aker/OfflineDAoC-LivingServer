using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS.Effects;
using DOL.GS.RealmAbilities;

namespace DOL.GS;

/// <summary>
/// #531 step 2c-1: the period's group actives for autonomous gamebots in the open field (reference.md 3.5, 3.8). These
/// are Speed of Sound on the engage, Mastery of Concentration when interrupted twice and Bunker of Faith after a death.
/// The rules are pure. <see cref="Wanted"/> reads them from the group's fight board. <see cref="Execute"/> does for a
/// bot what the ability's own handler does for a player (the handlers are written for GamePlayer only): the same
/// effect, duration, range and animation, and the handler's own reuse timer (30 minutes), so each fires at most once a
/// fight. Only bots the open-field tactics apply to (<see cref="BotBrain.RvrTacticsApplyTo"/>) use them: no companion,
/// no bot in a player's group, no keep fight or raid.
/// </summary>
public static class RvrRealmActives
{
    public const string SpeedOfSound = "AtlasOF_SpeedOfSound", MasteryOfConcentration = "AtlasOF_MasteryOfConcentration",
        BunkerOfFaith = "AtlasOF_BunkerOfFaith";

    /// <summary>The actives this class handles.</summary>
    public static readonly string[] Keys = { SpeedOfSound, MasteryOfConcentration, BunkerOfFaith };

    public static bool Handles(string key) => Array.IndexOf(Keys, key) >= 0;

    // ---- the rules (pure) -------------------------------------------------------------------------------------------

    /// <summary>Speed of Sound closes this much of a gap at least; nearer, the group is already there.</summary>
    public const int ChargeMinDistance = 800;
    /// <summary>Speed of Sound's own range (the handler's 2,000), plus a little: farther, the fight has not begun.</summary>
    public const int ChargeMaxDistance = 2_500;
    /// <summary>Speed of Sound reaches group mates within this range (the handler's).</summary>
    public const int SpeedOfSoundRange = 2_000;
    public const int SpeedOfSoundMilliseconds = 30_000;
    /// <summary>Bunker of Faith reaches group mates within this range (the handler's).</summary>
    public const int BunkerRange = 1_500;
    public const int BunkerMilliseconds = 30_000;
    public const int MocMilliseconds = 15_000;
    /// <summary>The interrupts in one fight after which a caster uses Mastery of Concentration.</summary>
    public const int MocInterrupts = 2;
    /// <summary>A fight's interrupt count starts again after this long out of combat.</summary>
    public const long FightGapMilliseconds = 20_000;

    /// <summary>
    /// Speed of Sound on the engage: the group runs the fight plan (stance Engage), has an enemy targeted, and the enemy
    /// is 800-2,500 from the group's centre, so the charge still has ground to cover. At least one mate is in reach. Pure.
    /// </summary>
    public static bool SpeedOfSoundWanted(RvrStance stance, bool enemyTargeted, double enemyDistance, int matesInReach) =>
        stance == RvrStance.Engage && enemyTargeted && matesInReach >= 1 &&
        enemyDistance >= ChargeMinDistance && enemyDistance <= ChargeMaxDistance;

    /// <summary>
    /// Mastery of Concentration when interrupted twice: the bot's second (or later) interrupt of this fight, while an
    /// enemy's interrupt is running now, and it has a spell with a cast time to get through. Pure.
    /// </summary>
    public static bool MasteryOfConcentrationWanted(bool rvrFight, int interruptsThisFight, bool interruptedNow, bool hasCastTimeSpell) =>
        rvrFight && interruptsThisFight >= MocInterrupts && interruptedNow && hasCastTimeSpell;

    /// <summary>
    /// Bunker of Faith after a death: the group is in contact, a mate lies dead within 1,500, and 2 or more living mates
    /// (the bot included) are within 1,500 to take it. Pure.
    /// </summary>
    public static bool BunkerOfFaithWanted(bool inContact, int deadMatesNear, int livingMatesNear) =>
        inContact && deadMatesNear >= 1 && livingMatesNear >= 2;

    /// <summary>
    /// The fight's interrupt count after one look. A new interrupt (an enemy's interrupt running now that was not at the last
    /// look) adds one. The count starts again after <see cref="FightGapMilliseconds"/> out of combat. Pure.
    /// </summary>
    public static (int Count, bool WasInterrupted, long LastCombatAt) CountInterrupts(int count, bool wasInterrupted, long lastCombatAt,
        bool interruptedNow, bool inCombat, long now)
    {
        if (inCombat || interruptedNow)
            lastCombatAt = now;
        else if (now - lastCombatAt >= FightGapMilliseconds)
            count = 0;
        if (interruptedNow && !wasInterrupted)
            count++;
        return (count, interruptedNow, lastCombatAt);
    }

    // ---- reading the game --------------------------------------------------------------------------------------------

    private sealed class Interrupts
    {
        public int Count;
        public bool WasInterrupted;
        public long LastCombatAt = long.MinValue / 2;
    }

    private static readonly ConditionalWeakTable<GameBot, Interrupts> InterruptStates = new();

    /// <summary>Updates the bot's interrupt count for this fight (called once a second, before <see cref="Wanted"/>).</summary>
    public static int ObserveInterrupts(GameBot bot, long now)
    {
        Interrupts state = InterruptStates.GetOrCreateValue(bot);
        bool interruptedNow = bot.IsBeingInterruptedByOther && bot.LastInterrupter is { } by && by.Realm != bot.Realm &&
                              BotPvpCrowdControl.PlayerLike(by);
        (state.Count, state.WasInterrupted, state.LastCombatAt) =
            CountInterrupts(state.Count, state.WasInterrupted, state.LastCombatAt, interruptedNow, bot.InCombat, now);
        return state.Count;
    }

    private static IEnumerable<GameLiving> Mates(GameBot bot) =>
        bot.Group?.GetMembersInTheGroup().Where(m => m != null && m != bot && m.CurrentRegionID == bot.CurrentRegionID) ??
        Enumerable.Empty<GameLiving>();

    /// <summary>Whether the bot should use this active now (only bots the open-field tactics apply to).</summary>
    public static bool Wanted(GameBot bot, string key)
    {
        if (bot == null || !bot.IsAlive || bot.IsMezzed || bot.IsStunned || bot.IsSitting || !BotBrain.RvrTacticsApplyTo(bot))
            return false;
        RvrFightBoard board = RvrFightBoards.Peek(bot);
        switch (key)
        {
            case SpeedOfSound:
            {
                if (board == null || board.EnemyCentroid is not { } enemy)
                    return false;
                bool targeted = board.Enemies.Any(e => e.TargetedByGroup);
                double distance = Vector2.Distance(new Vector2(board.FriendlyCentroid.X, board.FriendlyCentroid.Y), new Vector2(enemy.X, enemy.Y));
                int inReach = Mates(bot).Count(m => m.IsAlive && bot.IsWithinRadius(m, SpeedOfSoundRange));
                return SpeedOfSoundWanted(board.Plan.Stance, targeted, distance, inReach);
            }
            case MasteryOfConcentration:
            {
                Interrupts state = InterruptStates.GetOrCreateValue(bot);
                bool castTime = bot.Spells?.Any(s => s != null && s.CastTime > 0 && !s.IsInstantCast) == true;
                return MasteryOfConcentrationWanted(board != null, state.Count, state.WasInterrupted, castTime) &&
                       !bot.effectListComponent.ContainsEffectForEffectType(eEffect.MasteryOfConcentration);
            }
            case BunkerOfFaith:
            {
                if (board == null)
                    return false;
                var near = Mates(bot).Where(m => bot.IsWithinRadius(m, BunkerRange)).ToList();
                return BunkerOfFaithWanted(board.InContact, near.Count(m => !m.IsAlive), near.Count(m => m.IsAlive) + 1);
            }
            default:
                return false;
        }
    }

    // ---- doing it ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Uses the active for the bot, as its handler does for a player, and starts its reuse timer. False when it is not one
    /// of <see cref="Keys"/> or the bot cannot use it now (dead, sitting, mezzed, stunned).
    /// </summary>
    public static bool Execute(GameBot bot, TimedRealmAbility ability)
    {
        if (bot == null || ability == null || !Handles(ability.KeyName) || !bot.IsAlive || bot.IsSitting || bot.IsMezzed || bot.IsStunned)
            return false;
        switch (ability.KeyName)
        {
            case SpeedOfSound:
                ability.DisableSkill(bot);
                foreach (GameLiving target in Mates(bot).Where(m => m.IsAlive && bot.IsWithinRadius(m, SpeedOfSoundRange)).Prepend(bot))
                {
                    foreach (GamePlayer viewer in target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
                        viewer.Out.SendSpellEffectAnimation(bot, target, 7021, 0, false, 1);
                    ECSGameEffectFactory.Create(new(target, SpeedOfSoundMilliseconds, 1), static (in ECSGameEffectInitParams i) => new SpeedOfSoundECSEffect(i));
                }
                return true;
            case MasteryOfConcentration:
                EffectListService.TryCancelFirstEffectOfTypeOnTarget(bot, eEffect.MasteryOfConcentration);
                ability.SendCasterSpellEffectAndCastMessage(bot, 7007, true);
                ability.DisableSkill(bot);
                ECSGameEffectFactory.Create(new(bot, MocMilliseconds, 1), static (in ECSGameEffectInitParams i) => new MasteryOfConcentrationECSEffect(i));
                return true;
            case BunkerOfFaith when ability is BarrierOfFortitudeAbility bunker:
                foreach (GameLiving target in Mates(bot).Where(m => m.IsAlive && bot.IsWithinRadius(m, BunkerRange)).Prepend(bot))
                {
                    bool success = !target.TempProperties.GetProperty<bool>(BarrierOfFortitudeAbility.BofBaSb);
                    ability.SendCasterSpellEffect(target, 7009, success);
                    ability.SendCasterSpellEffect(target, 1486, success);
                    ability.SendCasterSpellEffect(target, 10535, success);
                    if (success)
                        ECSGameEffectFactory.Create(new(target, BunkerMilliseconds, 1, bunker.CreateSpell(bot)),
                            static (in ECSGameEffectInitParams i) => new BunkerOfFaithECSEffect(i));
                }
                ability.DisableSkill(bot);
                return true;
            default:
                return false;
        }
    }
}
