using System;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>
    /// /spawn companions with Stealth hide when their player does (owner 2026-10-28: sneaking through dungeons, ambushes in
    /// RvR). A companion follows the same rules a player's Stealth click does (StealthSpecHandler): not in combat,
    /// attacking or casting, not mesmerized or stunned, and not inside an enemy's "too close to hide" circle, which shrinks
    /// with its Stealth spec against the enemy's level (doubled by Detect Hidden). A Minstrel drops its songs first, as a
    /// player must. While its player stays hidden and nothing is fighting it, it casts nothing (a cast reveals the caster).
    /// It comes out of stealth as soon as its player does (attacking, /stealth again, dying, leaving the group) or when it
    /// is drawn into a fight itself (BotBrain.AttackMostWanted reveals a bot with a target). Gamebots are not affected.
    /// </summary>
    public static class CompanionStealth
    {
        private const long LookMilliseconds = 1_000;
        private sealed class State { public long NextLook; }
        private static readonly ConditionalWeakTable<GameBot, State> States = new();

        public static bool CanStealth(GameBot bot) => (bot?.GetSpecializationByName(Specs.Stealth)?.Level ?? 0) > 0;

        /// <summary>The companion's player when that player is hidden and the companion is with them.</summary>
        public static GamePlayer StealthedLeader(GameBot bot) =>
            bot is { IsPlayerCompanion: true, IsAlive: true } &&
            bot.PlayerGroupLeader is { IsAlive: true, IsStealthed: true } leader && leader.Group != null &&
            bot.Group == leader.Group && leader.CurrentRegionID == bot.CurrentRegionID
                ? leader
                : null;

        /// <summary>A companion hidden with its player stays hidden while it has no fight of its own.</summary>
        public static bool HoldsStealth(GameBot bot) => bot != null && bot.IsStealthed && StealthedLeader(bot) != null;

        private static bool InOwnFight(GameBot bot) =>
            bot.InCombat || bot.attackComponent?.AttackState == true || bot.Brain is BotBrain { HasAggro: true };

        /// <summary>A stealthing companion casts nothing while its player is hidden and nothing fights it.</summary>
        public static bool SuppressesCasting(GameBot bot) =>
            bot != null && StealthedLeader(bot) != null && CanStealth(bot) && !InOwnFight(bot);

        public static void Maintain(GameBot bot)
        {
            if (bot is not { IsPlayerCompanion: true })
                return;
            GamePlayer leader = StealthedLeader(bot);
            if (bot.IsStealthed)
            {
                if (leader == null)
                    bot.Stealth(false);
                return;
            }
            if (leader == null || !CanStealth(bot))
                return;

            State state = States.GetOrCreateValue(bot);
            long now = GameLoop.GameLoopTime;
            if (now < state.NextLook)
                return;
            state.NextLook = now + LookMilliseconds;
            if (!MayHide(bot))
                return;

            // Songs and pulsing spells cannot run in stealth: a Minstrel stops them, as a player has to.
            foreach (ECSPulseEffect pulse in bot.effectListComponent.GetPulseEffects())
                pulse.End();
            bot.Stealth(true);
        }

        public static bool MayHide(GameBot bot)
        {
            if (!bot.IsAlive || InOwnFight(bot) || bot.IsCasting || bot.castingComponent?.HasPendingSkillRequests == true ||
                GameRelic.IsPlayerCarryingRelic(bot) ||
                bot.IsMezzed || bot.IsStunned)
                return false;
            // #522: the one "too close to hide" rule (StealthRules), shared with the player and the gamebots.
            return DOL.GS.ServerRules.StealthRules.EnemyTooClose(bot) == null;
        }

        /// <summary>The "impossible to hide" circle of StealthSpecHandler.IsObjectTooClose (StealthRules.HideRadius).</summary>
        public static int HideRadius(int enemyLevel, int stealthLevel, bool detectHidden) =>
            DOL.GS.ServerRules.StealthRules.HideRadius(enemyLevel, stealthLevel, detectHidden);
    }
}
