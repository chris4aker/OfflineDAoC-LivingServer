using System;
using System.Linq;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// Event wake-ups. Port of zomboden/OfflineDAoC-True165 8abbc61a8 (its veteran review M12, 2026-11-11). Being attacked
    /// already wakes a bot at once (OnAttackedByEnemy, NotifyNearbyGroupBots). These three events do the same for the bot
    /// that should react, instead of leaving it to its next think (2.6-5 s for an efficient bot at 1,500 bots):
    /// <list type="bullet">
    /// <item>an add joins the fight: a bot with a mesmerize, stun or root looks for adds at once (TryPveAddControl
    /// otherwise looks once a second);</item>
    /// <item>a groupmate drops low: the group's healers think at once;</item>
    /// <item>a monster or enemy starts a cast at the group: the bots fighting that caster think at once (stun, interrupt).</item>
    /// </list>
    /// Cost: no radius scans, no paths. Each event walks at most the victim's group (8), and a brain is woken at most once
    /// every 250 ms by these events; an add wakes a bot once per attacker every 3 s. A raid's 38 parties are 38 separate
    /// groups, so one hit never wakes the whole raid.
    /// </summary>
    public partial class BotBrain
    {
        /// <summary>A groupmate expected under this health after a hit wakes the group's healers.</summary>
        public const int HealWakeHealthPercent = 75;
        /// <summary>These events never make a brain think more often than this.</summary>
        public const long EventWakeMinimumMilliseconds = 250;
        /// <summary>The same add wakes the same bot again only after this long.</summary>
        public const long AddWakeRepeatMilliseconds = 3_000;

        private long _nextEventWakeTick;
        private GameLiving _lastAddWakeAttacker;
        private long _lastAddWakeTick;

        /// <summary>Pure: health percent after this hit (spell damage notifies before it is dealt).</summary>
        public static int PredictedHealthPercent(int health, int maxHealth, int damage) =>
            maxHealth <= 0 ? 100 : Math.Clamp((int)((long)(health - Math.Max(0, damage)) * 100 / maxHealth), 0, 100);

        /// <summary>Pure: a hit that leaves a groupmate under the wake line wakes the healers.</summary>
        public static bool HealWakeNeeded(int damage, int predictedHealthPercent) =>
            damage > 0 && predictedHealthPercent < HealWakeHealthPercent;

        /// <summary>
        /// Pure: an add is a monster hitting someone who is not fighting it, that is neither this bot's target nor the
        /// group's kill target.
        /// </summary>
        public static bool IsAddWake(bool monster, bool victimFightsIt, bool ownTarget, bool killTarget) =>
            monster && !victimFightsIt && !ownTarget && !killTarget;

        /// <summary>Pure: the same add wakes the same bot again only after AddWakeRepeatMilliseconds.</summary>
        public static bool AddWakeAllowed(bool sameAttacker, long now, long lastWakeTick) =>
            !sameAttacker || now - lastWakeTick >= AddWakeRepeatMilliseconds;

        /// <summary>Pure: an enemy's cast at the group wakes a bot that has the caster as its target.</summary>
        public static bool EnemyCastWakes(bool harmful, bool instant, bool botTargetsCaster) =>
            harmful && !instant && botTargetsCaster;

        /// <summary>Pure: these events wake a brain only once per <see cref="EventWakeMinimumMilliseconds"/>.</summary>
        public static bool EventWakeAllowed(long now, long nextEventWakeTick) => now >= nextEventWakeTick;

        /// <summary>Think now (at most once every EventWakeMinimumMilliseconds from these events).</summary>
        public bool WakeForEvent()
        {
            long now = GameLoop.GameLoopTime;
            if (!EventWakeAllowed(now, _nextEventWakeTick))
                return false;
            _nextEventWakeTick = now + EventWakeMinimumMilliseconds;
            if (NextThinkTick > now)
                NextThinkTick = now;
            return true;
        }

        /// <summary>The monster is what some groupmate is fighting (the group's kill target, as far as a bot can tell).</summary>
        private static bool IsGroupKillTarget(Group group, GameLiving attacker) =>
            group != null && group.GetMembersInTheGroup().Any(member => member != null && member.TargetObject == attacker &&
                (member.IsAttacking || member.IsCasting));

        /// <summary>An add hit <paramref name="victim"/> (this bot or a groupmate): look for adds to control at once.</summary>
        internal void WakeForAdd(GameLiving attacker, GameLiving victim)
        {
            GameBot bot = BotBody;
            if (bot == null || bot.Group == null || attacker is not GameNPC npc || npc is GameBot || npc.Realm != eRealm.None ||
                !npc.IsAlive || BotPvpCrowdControl.PlayerLike(npc))
                return;
            long now = GameLoop.GameLoopTime;
            if (!AddWakeAllowed(attacker == _lastAddWakeAttacker, now, _lastAddWakeTick) ||
                bot.Spells?.Any(spell => spell != null && (IsAddControlSpell(spell) || ShamanBotCombatPolicy.IsRoot(spell))) != true ||
                !IsAddWake(true, victim?.TargetObject == attacker, Body.TargetObject == attacker, IsGroupKillTarget(bot.Group, attacker)))
                return;
            _lastAddWakeAttacker = attacker;
            _lastAddWakeTick = now;
            _nextPveAddControlLook = 0;
            WakeForEvent();
        }

        /// <summary>A groupmate is about to be (or is) under the wake line: the group's healers think now.</summary>
        private static void WakeHealersIfLow(Group group, GameLiving victim, AttackData ad)
        {
            int damage = (ad?.Damage ?? 0) + (ad?.CriticalDamage ?? 0);
            if (group == null || victim == null || damage <= 0)
                return;
            bool checkedHealth = false;
            foreach (GameLiving member in group.GetMembersInTheGroup())
            {
                if (member == victim || member is not GameBot healer || !healer.CanCastHealSpells ||
                    !healer.IsAlive || healer.ObjectState != GameObject.eObjectState.Active ||
                    healer.CurrentRegionID != victim.CurrentRegionID ||
                    !healer.IsWithinRadius(victim, GROUP_DEFENSE_ASSIST_RADIUS) ||
                    healer.Brain is not BotBrain brain)
                    continue;
                // the victim's health is read once, and only when the group has a healer bot in reach
                if (!checkedHealth)
                {
                    if (!HealWakeNeeded(damage, PredictedHealthPercent(victim.Health, victim.MaxHealth, damage)))
                        return;
                    checkedHealth = true;
                }
                brain.WakeForEvent();
            }
        }

        /// <summary>
        /// Called when <paramref name="caster"/> starts a cast-time spell at <paramref name="target"/>: the bots of the
        /// target's group (or the target bot itself) that are fighting the caster think at once.
        /// </summary>
        public static void NotifyEnemyCastStarted(GameLiving caster, GameLiving target, Spell spell)
        {
            if (caster == null || target == null || caster == target || spell == null ||
                !EnemyCastWakes(spell.IsHarmful, spell.IsInstantCast, true))
                return;
            GameLiving member = GroupMemberForCombat(target);
            Group group = member?.Group;
            if (group == null)
            {
                if (member is GameBot bot)
                    WakeForEnemyCast(bot, caster);
                return;
            }
            foreach (GameLiving groupMember in group.GetMembersInTheGroup())
                if (groupMember is GameBot bot)
                    WakeForEnemyCast(bot, caster);
        }

        private static void WakeForEnemyCast(GameBot bot, GameLiving caster)
        {
            if (bot == caster || !EnemyCastWakes(true, false, bot.TargetObject == caster) || !bot.IsAlive ||
                bot.ObjectState != GameObject.eObjectState.Active || bot.CurrentRegionID != caster.CurrentRegionID ||
                !bot.IsWithinRadius(caster, GROUP_DEFENSE_ASSIST_RADIUS) || bot.Brain is not BotBrain brain)
                return;
            brain.WakeForEvent();
        }
    }
}
