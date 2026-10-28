using System.Collections.Concurrent;

namespace DOL.GS
{
    /// <summary>
    /// A siege army raises its own dead, as players did: any living healer of the attacking realm near a fallen
    /// comrade raises it, not only the healer of its own eight. Run 21 (2026-10-17): Midgard won the fighting at Dun
    /// Dagda 214 kills to 115, but a dead attacker released at the realm's frontier muster about 160,000 units away
    /// (median of 115 bots) and the army at the keep fell from 125 to none while the party healers lay dead or stood
    /// elsewhere. A dead attacker with such a healer near it waits for the raise (GameBot's siege corpse wait).
    /// </summary>
    public static class AutonomousSiegeResurrection
    {
        public const int RaiseRadius = 2_000;
        /// <summary>
        /// How far a healer out of the fight looks for its army's dead, and walks to them; a corpse with such a healer this
        /// close waits for the raise. Run 27 (2026-10-19, Dun Dagda): most Midgard dead lay on the slopes 3,000-4,500 below the
        /// keep, beyond 2,000 of every healer, so they released after 20 s and walked back ~185,000 units (raise checks: no
        /// corpse in reach 1,400-2,900 times per 5 minutes, enemies by a corpse never).
        /// </summary>
        public const int SearchRadius = 5_000;
        public const long ClaimMilliseconds = 20_000;
        /// <summary>A healer in the battle raises only after this long without being hit.</summary>
        public const long QuietMilliseconds = 5_000;
        /// <summary>
        /// During the battle, a corpse with a living enemy this close is left for later. 500 kept every corpse at Dun Dagda's
        /// Relic Gate unraised (the defenders hold the gate the attackers die at; run 24: 0 corpses claimed).
        /// </summary>
        public const int EnemyClearRadius = 250;

        private static readonly ConcurrentDictionary<GameBot, (GameBot Healer, long Until)> Claims = new();

        /// <summary>The bot belongs to the attacking army of a running siege.</summary>
        public static bool IsSiegeAttacker(GameBot bot) =>
            bot is { IsAutonomousWorldBot: true } &&
            AutonomousRvrEventLayer.TrySiegeRelease(AutonomousBotGroupCoordinator.RvrForceId(bot), bot.Realm, GameLoop.GameLoopTime, out _, out _);

        public static bool CanRaise(GameBot healer) =>
            healer is { IsAlive: true, IsAutonomousWorldBot: true } && healer.ResurrectionSpell is { } spell &&
            healer.Mana >= healer.PowerCost(spell);

        /// <summary>A living healer of the corpse's realm that raises in sieges (the army's or an RvR group's) stands within
        /// <see cref="SearchRadius"/>.</summary>
        public static bool HasArmyResurrectorNear(GameBot corpse)
        {
            if (corpse == null || corpse.ObjectState != GameObject.eObjectState.Active) return false;
            foreach (GameNPC npc in corpse.GetNPCsInRadius(SearchRadius))
                if (npc is GameBot healer && healer != corpse && healer.Realm == corpse.Realm && CanRaise(healer) &&
                    (IsSiegeAttacker(healer) || AutonomousObjectiveAssignments.Is(healer, eAutonomousObjectiveKind.RvR)))
                    return true;
            return false;
        }

        /// <summary>The nearest fallen realm-mate within <see cref="RaiseRadius"/> (in the fight) or <see cref="SearchRadius"/>
        /// that no other healer is raising.</summary>
        public static GameBot ClaimCorpse(GameBot healer, bool fighting = false) => ClaimCorpse(healer, fighting, out _);

        /// <summary>As <see cref="ClaimCorpse(GameBot, bool)"/>; enemyBlocked: a corpse lay in range but an enemy stood by it.</summary>
        public static GameBot ClaimCorpse(GameBot healer, bool fighting, out bool enemyBlocked)
        {
            enemyBlocked = false;
            long now = GameLoop.GameLoopTime;
            GameBot best = null;
            int bestDistance = int.MaxValue;
            // In the fight a healer raises only near itself; out of it, it walks out to the dead on the field.
            foreach (GameNPC npc in healer.GetNPCsInRadius(fighting ? (ushort)RaiseRadius : (ushort)SearchRadius))
            {
                if (npc is not GameBot dead || dead.IsAlive || dead == healer || dead.Realm != healer.Realm ||
                    !dead.IsAutonomousWorldBot || dead.ObjectState != GameObject.eObjectState.Active)
                    continue;
                if (Claims.TryGetValue(dead, out var claim) && claim.Healer != healer && now < claim.Until && claim.Healer.IsAlive)
                    continue;
                if (fighting && EnemyNear(dead))
                {
                    enemyBlocked = true;
                    continue;
                }
                int distance = healer.GetDistanceTo(dead);
                if (distance < bestDistance)
                {
                    best = dead;
                    bestDistance = distance;
                }
            }
            if (best != null)
            {
                if (Claims.Count > 4_096) Claims.Clear();
                Claims[best] = (healer, now + ClaimMilliseconds);
            }
            return best;
        }

        private static bool EnemyNear(GameBot corpse)
        {
            foreach (GameNPC npc in corpse.GetNPCsInRadius(EnemyClearRadius))
                // Enemy bots and their pets. Not keep guards: the dead lie at the gate under the walls, always within reach
                // of a guard, and players raised there all the same (run 23: 11 Midgard dead at Dun Dagda, none raised).
                if (npc.IsAlive && npc is not DOL.GS.Keeps.GameKeepGuard && npc.Realm != corpse.Realm &&
                    npc.Realm is eRealm.Albion or eRealm.Midgard or eRealm.Hibernia)
                {
                    // Who stands by the corpse (diagnostic): an enemy bot or pet, how high above or below, and whether it fights.
                    System.Threading.Interlocked.Increment(ref EnemyKinds[npc is GameBot ? 0 : 1]);
                    if (System.Math.Abs(npc.Z - corpse.Z) > 150) System.Threading.Interlocked.Increment(ref EnemyKinds[2]);
                    if (npc.InCombat) System.Threading.Interlocked.Increment(ref EnemyKinds[3]);
                    return true;
                }
            foreach (GamePlayer player in corpse.GetPlayersInRadius(EnemyClearRadius))
                if (player.IsAlive && player.Realm != corpse.Realm)
                    return true;
            return false;
        }

        // Enemies found by corpses: bot, pet/other NPC, more than 150 above or below, in combat (diagnostic).
        private static readonly long[] EnemyKinds = new long[4];

        public static void Clear() => Claims.Clear();

        /// <summary>Why siege raises do or do not happen (run 23: 0 raises with dozens of attackers dead at Dun Dagda).</summary>
        public enum Check { UnderAttack, CannotCast, NoCorpse, Corpse, NotAttacker, EnemyByCorpse }
        private static readonly long[] Checks = new long[6];

        /// <summary>A fallen realm-mate lies within <see cref="RaiseRadius"/> (diagnostic only).</summary>
        public static bool HasFallenComradeNear(GameBot healer)
        {
            foreach (GameNPC npc in healer.GetNPCsInRadius(RaiseRadius))
                if (npc is GameBot dead && !dead.IsAlive && dead != healer && dead.Realm == healer.Realm && dead.IsAutonomousWorldBot)
                    return true;
            return false;
        }
        private static long _nextReport;

        public static void Note(Check check)
        {
            System.Threading.Interlocked.Increment(ref Checks[(int)check]);
            long now = GameLoop.GameLoopTime, next = System.Threading.Interlocked.Read(ref _nextReport);
            if (now < next || System.Threading.Interlocked.CompareExchange(ref _nextReport, now + 5 * 60_000, next) != next) return;
            long underAttack = System.Threading.Interlocked.Exchange(ref Checks[0], 0), cannotCast = System.Threading.Interlocked.Exchange(ref Checks[1], 0),
                 noCorpse = System.Threading.Interlocked.Exchange(ref Checks[2], 0), corpse = System.Threading.Interlocked.Exchange(ref Checks[3], 0),
                 notAttacker = System.Threading.Interlocked.Exchange(ref Checks[4], 0),
                 enemyByCorpse = System.Threading.Interlocked.Exchange(ref Checks[5], 0);
            long enemyBots = System.Threading.Interlocked.Exchange(ref EnemyKinds[0], 0), enemyOther = System.Threading.Interlocked.Exchange(ref EnemyKinds[1], 0),
                 enemyLevel = System.Threading.Interlocked.Exchange(ref EnemyKinds[2], 0), enemyFighting = System.Threading.Interlocked.Exchange(ref EnemyKinds[3], 0);
            if (next == 0) return;
            DOL.Logging.LoggerManager.Create(typeof(AutonomousSiegeResurrection)).Info(
                $"AUTONOMOUS_SIEGE_RAISE_CHECKS underAttack={underAttack} cannotCast={cannotCast} noCorpse={noCorpse} corpseClaimed={corpse} " +
                $"notAttackerWithDeadNear={notAttacker} enemyByCorpse={enemyByCorpse} enemyBots={enemyBots} enemyOther={enemyOther} " +
                $"enemyAboveOrBelow={enemyLevel} enemyFighting={enemyFighting}");
        }
    }
}
