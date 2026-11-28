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
/// no bot in a player's group, no keep fight or raid. Step 2c-2 adds the Healer's Perfect Recovery after a death (an
/// instant rez at full health and power) and the Infiltrator's Vanish when it runs from a fight it is losing to an enemy
/// realm (reference.md 3.8, 3.9, 4.4; roam.md 8), and the archers' True Sight once an enemy assassin strikes the group: for
/// its 60 s the archer sees every stealthed enemy in its line of sight, as a player on True Sight does (owner answer 71A,
/// 2026-11-28; RvrStealthNotice; reference.md 4.1-4.2).
/// </summary>
public static class RvrRealmActives
{
    public const string SpeedOfSound = "AtlasOF_SpeedOfSound", MasteryOfConcentration = "AtlasOF_MasteryOfConcentration",
        BunkerOfFaith = "AtlasOF_BunkerOfFaith", PerfectRecovery = "AtlasOF_PerfectRecovery", Vanish = "AtlasOF_Vanish",
        TrueSight = "AtlasOF_TrueSight";

    /// <summary>The actives this class handles.</summary>
    public static readonly string[] Keys = { SpeedOfSound, MasteryOfConcentration, BunkerOfFaith, PerfectRecovery, Vanish, TrueSight };

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
    /// <summary>Perfect Recovery's range (the handler's rez spell).</summary>
    public const int PerfectRecoveryRange = 1_500;
    /// <summary>Vanish lasts this long (the handler's).</summary>
    public const int VanishMilliseconds = 30_000;
    /// <summary>Vanish is for a fight against an enemy realm: one of its hits landed this recently.</summary>
    public const long VanishHitWindowMilliseconds = 5_000;
    /// <summary>True Sight lasts this long (the handler's).</summary>
    public const int TrueSightMilliseconds = 60_000;
    /// <summary>True Sight answers an enemy assassin on the archer or a group mate within this range.</summary>
    public const int TrueSightGuardRange = 2_000;

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

    /// <summary>A dead group mate as Perfect Recovery sees it (plain values).</summary>
    public readonly record struct Fallen(RvrRole Role, double Distance, bool InSight, bool BeingRaised);

    /// <summary>
    /// Perfect Recovery after a death: in a fight (the group in contact, or the healer itself in combat), not already
    /// casting, and a mate to raise (<see cref="PerfectRecoveryPick"/>). Out of the fight the ordinary rez does it, and the
    /// 30-minute timer is kept for the next one. Pure.
    /// </summary>
    public static bool PerfectRecoveryWanted(bool inFight, bool casting, IReadOnlyList<Fallen> fallen) =>
        inFight && !casting && PerfectRecoveryPick(fallen) >= 0;

    /// <summary>
    /// The mate Perfect Recovery raises: within its 1,500, in sight and with no rez already on the way; a healer first, then
    /// a caster, then anyone, the nearest first within each. -1 when there is none. Pure.
    /// </summary>
    public static int PerfectRecoveryPick(IReadOnlyList<Fallen> fallen)
    {
        int best = -1;
        for (int i = 0; i < (fallen?.Count ?? 0); i++)
        {
            Fallen f = fallen[i];
            if (!f.InSight || f.BeingRaised || f.Distance > PerfectRecoveryRange)
                continue;
            if (best < 0 || RaiseRank(f.Role) < RaiseRank(fallen[best].Role) ||
                RaiseRank(f.Role) == RaiseRank(fallen[best].Role) && f.Distance < fallen[best].Distance)
                best = i;
        }
        return best;
    }

    private static int RaiseRank(RvrRole role) => role switch { RvrRole.Healer => 0, RvrRole.Caster => 1, _ => 2 };

    /// <summary>
    /// Vanish when the bot runs from a fight it is losing (roam.md 8, "assassin, losing"; reference.md 4.4): a retreat has
    /// just begun, an enemy realm's hit landed in the last 5 s (losing to monsters does not count), and the bot is not
    /// already hidden. Pure.
    /// </summary>
    public static bool VanishWanted(bool retreatStarting, long sinceEnemyRealmHitMilliseconds, bool stealthed) =>
        retreatStarting && !stealthed && sinceEnemyRealmHitMilliseconds >= 0 && sinceEnemyRealmHitMilliseconds <= VanishHitWindowMilliseconds;

    /// <summary>
    /// True Sight once an enemy assassin strikes the group (reference.md 4.2: a stealther "is found by another stealther, by an
    /// archer on True Sight"): an enemy Infiltrator, Shadowblade or Nightshade is among the recent attackers of the archer or
    /// of a group mate within 2,000, so the archer can find it when it hides again. Not while True Sight already runs. Pure.
    /// </summary>
    public static bool TrueSightWanted(int enemyAssassinsOnGroup, bool trueSightRunning) =>
        enemyAssassinsOnGroup >= 1 && !trueSightRunning;

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
            case PerfectRecovery:
            {
                List<GameLiving> corpses = Corpses(bot);
                return corpses.Count > 0 &&
                       PerfectRecoveryWanted(board?.InContact == true || bot.InCombat, bot.IsCasting, corpses.Select(c => FallenOf(bot, c)).ToList());
            }
            case TrueSight:
                return TrueSightWanted(EnemyAssassinsOnGroup(bot), bot.effectListComponent.ContainsEffectForEffectType(eEffect.TrueSight));
            // Vanish is fired when a retreat begins (TryVanishOnRetreat), never from the once-a-second look
            default:
                return false;
        }
    }

    /// <summary>Dead group mates in the bot's region that are still lying where they fell (not released).</summary>
    private static List<GameLiving> Corpses(GameBot bot) =>
        Mates(bot).Where(m => !m.IsAlive && m.ObjectState == GameObject.eObjectState.Active && bot.IsWithinRadius(m, PerfectRecoveryRange + 500)).ToList();

    private static Fallen FallenOf(GameBot bot, GameLiving corpse) => new(
        RvrRoles.For(corpse switch
        {
            GameBot b when b.CharacterClass != null => (eCharacterClass)b.CharacterClass.ID,
            GamePlayer p when p.CharacterClass != null => (eCharacterClass)p.CharacterClass.ID,
            _ => eCharacterClass.Unknown,
        }),
        bot.GetDistanceTo(corpse),
        BotGroupSupport.HasCorpseLineOfSight(bot, corpse),
        BeingRaised(bot, corpse));

    /// <summary>A rez already on the way: a rez offer is open on the corpse, or a mate is casting a rez at it.</summary>
    private static bool BeingRaised(GameBot bot, GameLiving corpse) =>
        corpse.TempProperties.GetProperty<GameLiving>("RESURRECT_CASTER") != null ||
        Mates(bot).Any(m => m != corpse && m.IsAlive && m.IsCasting && m.castingComponent?.SpellHandler is { } cast &&
                            cast.Spell?.SpellType == eSpellType.Resurrect && cast.Target == corpse);

    /// <summary>The mate Perfect Recovery would raise now, or null.</summary>
    public static GameLiving PerfectRecoveryTarget(GameBot bot)
    {
        List<GameLiving> corpses = Corpses(bot);
        int pick = PerfectRecoveryPick(corpses.Select(c => FallenOf(bot, c)).ToList());
        return pick >= 0 ? corpses[pick] : null;
    }

    private static eCharacterClass ClassOf(GameLiving living) => living switch
    {
        GameBot b when b.CharacterClass != null => (eCharacterClass)b.CharacterClass.ID,
        GamePlayer p when p.CharacterClass != null => (eCharacterClass)p.CharacterClass.ID,
        _ => eCharacterClass.Unknown,
    };

    /// <summary>Enemy assassins among the recent attackers of the bot and of its group mates within 2,000.</summary>
    private static int EnemyAssassinsOnGroup(GameBot bot)
    {
        var seen = new HashSet<GameLiving>();
        foreach (GameLiving member in Mates(bot).Where(m => m.IsAlive && bot.IsWithinRadius(m, TrueSightGuardRange)).Prepend(bot))
            foreach (GameLiving attacker in member.attackComponent?.AttackerTracker?.Attackers ?? (ICollection<GameLiving>)Array.Empty<GameLiving>())
                if (attacker != null && attacker.IsAlive && attacker.Realm != bot.Realm && attacker.Realm != eRealm.None &&
                    RvrRoles.For(ClassOf(attacker)) == RvrRole.Stealther)
                    seen.Add(attacker);
        return seen.Count;
    }

    /// <summary>Whether the bot is hidden under Vanish now.</summary>
    public static bool Vanished(GameLiving living) =>
        living is { IsStealthed: true } && living.effectListComponent.ContainsEffectForEffectType(eEffect.Vanish);

    /// <summary>
    /// A retreat has begun (BotBrain.BeginRetreat): the bot uses Vanish if it bought it, it is ready and
    /// <see cref="VanishWanted"/> holds. Only bots the open-field tactics apply to. True when it vanished.
    /// </summary>
    public static bool TryVanishOnRetreat(GameBot bot)
    {
        if (bot == null || !bot.IsAlive || bot.IsMezzed || bot.IsStunned || bot.IsSitting || !BotBrain.RvrTacticsApplyTo(bot) ||
            bot.GetAbility(Vanish) is not TimedRealmAbility vanish || bot.GetSkillDisabledDuration(vanish) > 0)
            return false;
        long since = bot.LastAttackedByEnemyTickPvP > 0 ? GameLoop.GameLoopTime - bot.LastAttackedByEnemyTickPvP : -1;
        if (!VanishWanted(true, since, bot.IsStealthed) || !Execute(bot, vanish))
            return false;
        AutonomousBotRealmAbilities.LogUse(bot, vanish);
        return true;
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
            case PerfectRecovery:
            {
                // The handler's own rez spell (AtlasOF_PerfectRecovery.CreateRezSpell), cast with the ability as its handler,
                // so the spell code starts the 30-minute timer when the cast ends, as for a player.
                if (bot.IsCasting || PerfectRecoveryTarget(bot) is not { } corpse)
                    return false;
                Spell rez = PerfectRecoverySpell(bot.Level, ability.GetReUseDelay(ability.Level));
                if (ability is not ISpellCastingAbilityHandler handler)
                    return false;
                GameObject previous = bot.TargetObject;
                bot.TargetObject = corpse;
                bool started = bot.CastSpell(rez, GlobalSpellsLines.RealmSpellsSpellLine, handler, false);
                bot.TargetObject = previous;
                return started;
            }
            case TrueSight:
                // the handler's effect (AtlasOF_TrueSight): 60 s, then its 30-minute timer
                ability.DisableSkill(bot);
                ECSGameEffectFactory.Create(new(bot, TrueSightMilliseconds, 1), static (in ECSGameEffectInitParams i) => new AtlasOF_TrueSightECSEffect(i));
                return true;
            case Vanish:
                if (!bot.VanishHide())
                    return false;
                ability.DisableSkill(bot);
                ECSGameEffectFactory.Create(new(bot, VanishMilliseconds, 1), static (in ECSGameEffectInitParams i) => new AtlasOF_VanishECSEffect(i));
                return true;
            default:
                return false;
        }
    }

    /// <summary>The Perfect Recovery rez, as the handler builds it: instant, range 1,500, 100% health and power, free.</summary>
    public static Spell PerfectRecoverySpell(int casterLevel, int reuseSeconds)
    {
        var db = new DOL.Database.DbSpell
        {
            Name = "Perfect Recovery Rez", Icon = 0, ClientEffect = 7019, Damage = 0, Target = eSpellTarget.CORPSE.ToString(), Radius = 0,
            Type = eSpellType.Resurrect.ToString(), Value = 0, Duration = 0, Pulse = 0, PulsePower = 0, Power = 0, CastTime = 0,
            EffectGroup = 0, Range = PerfectRecoveryRange, RecastDelay = reuseSeconds, ResurrectHealth = 100, ResurrectMana = 100,
        };
        return new Spell(db, casterLevel);
    }
}
