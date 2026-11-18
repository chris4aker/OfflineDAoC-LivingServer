using DOL.AI.Brain;
using DOL.Logging;

namespace DOL.GS
{
    /// <summary>
    /// Tethered bosses (Summoner Roesia and about 50 other scripted encounters) take no damage while they are
    /// outside their tether range. Players stop hitting such a boss and the one it is chasing runs back toward
    /// its lair so it follows them home, then the fight resumes. Gamebots kept fighting out there: Summoner
    /// Roesia chased a raider 3,200 units from her spawn and stayed immune at 28% until the raid skipped her
    /// (run 16, 2026-10-07). This does the player thing; the boss and its mechanics are unchanged, except that a boss
    /// left stranded outside its tether with nobody fighting it is leashed home (<see cref="LeashIfStranded"/>).
    /// </summary>
    public static class AutonomousTetherReset
    {
        private static readonly Logger Log = LoggerManager.Create(typeof(AutonomousTetherReset));
        private static long _nextLog;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<GameNPC, long> StrandedSince = new();

        /// <summary>
        /// Run 2026-10-13: Aidon the Archwizard (Hall of the Corrupt, on the way to Summoner's Hall) stood 4,126 units
        /// from his spawn, outside his 1,500 tether, for over an hour. Out there he is immune and only goes home when
        /// nothing is within his aggro range, but the Albion raid's 150 parties stood around him (each bot correctly
        /// stopped attacking), so he kept his aggro list and the raid never got past him. An epic encounter that has
        /// been out of its tether for 30 seconds while nobody is fighting it in reach is leashed: its aggro list is
        /// cleared and it walks home (its own script restores its health there, as on any reset).
        /// </summary>
        public const long StrandedLeashMilliseconds = 30_000;
        // Out of its tether this long, it goes home even while fighting (run 6: Princess Nahemah fought raiders at the
        // edge of her tether for over twenty minutes, immune, while the raid tried to draw her home).
        public const long OutOfTetherLeashMilliseconds = 90_000;
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<GameNPC, long> OutOfTetherSince = new();

        /// <summary>
        /// The speed a leashed boss walks home at: its own, or its template's when its script has left it at 0 (its aggro
        /// list is cleared by then, so this is never a fight). Owner's PC, build b1b6846, 2026-11-18 (Galladoria): the Earth
        /// primal's script froze him (speed 0) while he chased a raider out of reach, and 13 leashes in a row sent him home at
        /// speed 0; he stood 1,044 from his spawn, outside his 890 tether, untouchable. A boss with no speed in its template
        /// either (one that never moves) keeps 0.
        /// </summary>
        public static short LeashSpeed(GameNPC boss)
        {
            if (boss.MaxSpeedBase > 0) return boss.MaxSpeed;
            short template = boss.NPCTemplate?.MaxSpeed ?? 0;
            if (template <= 0) return boss.MaxSpeed;
            boss.MaxSpeedBase = template;
            return boss.MaxSpeed;
        }

        /// <summary>
        /// The boss is back inside its tether: its excursion's clocks are forgotten, so its next excursion gets its own
        /// <see cref="StrandedLeashMilliseconds"/> grace. Owner's PC, build b1b6846, 2026-11-18 (sweep 6e, M-1): the clocks were
        /// cleared only on a leash, on engagement or while walking home; a boss that walked back in kept its old time, and its
        /// next excursion was leashed at once (Aidon's 14:18:56 leash came in the first second of a new one; his previous tether
        /// line was at 14:06:28). On a boss without a tether heal that cuts a fight short.
        /// </summary>
        public static void BackInsideTether(GameNPC boss)
        {
            if (boss == null || StrandedSince.IsEmpty && OutOfTetherSince.IsEmpty) return;
            StrandedSince.TryRemove(boss, out _);
            OutOfTetherSince.TryRemove(boss, out _);
        }

        /// <summary>
        /// Whether a boss outside its tether is leashed now (the clocks only; true forgets them): walking home, never; fighting a
        /// raider in reach, not before <see cref="OutOfTetherLeashMilliseconds"/> out; else after
        /// <see cref="StrandedLeashMilliseconds"/> stranded.
        /// </summary>
        public static bool LeashDue(GameNPC boss, bool engaged, long now)
        {
            if (boss.IsReturningToSpawnPoint)
            {
                StrandedSince.TryRemove(boss, out _);
                OutOfTetherSince.TryRemove(boss, out _);
                return false;
            }
            long outSince = OutOfTetherSince.GetOrAdd(boss, now);
            if (OutOfTetherSince.TryGetValue(boss, out long seen) && now - seen > 2 * OutOfTetherLeashMilliseconds)
                OutOfTetherSince[boss] = outSince = now; // a stale entry from an earlier excursion
            bool longOut = now - outSince >= OutOfTetherLeashMilliseconds;
            if (engaged && !longOut)
            {
                StrandedSince.TryRemove(boss, out _);
                return false;
            }
            if (!longOut)
            {
                long since = StrandedSince.GetOrAdd(boss, now);
                if (now - since < StrandedLeashMilliseconds) return false;
            }
            StrandedSince.TryRemove(boss, out _);
            OutOfTetherSince.TryRemove(boss, out _);
            return true;
        }

        private static void LeashIfStranded(GameNPC boss)
        {
            long now = GameLoop.GameLoopTime;
            bool engaged = boss.TargetObject is GameLiving foe && foe.IsAlive && boss.IsWithinRadius(foe, boss.attackComponent.AttackRange + 100);
            if (!LeashDue(boss, engaged, now)) return;
            (boss.Brain as StandardMobBrain)?.ClearAggroList();
            boss.StopAttack();
            boss.TargetObject = null;
            short speed = LeashSpeed(boss);
            boss.ReturnToSpawnPoint(speed);
            Log.Info($"AUTONOMOUS_TETHER_LEASH boss=\"{boss.Name}\" region={boss.CurrentRegionID} fromSpawn={(int)boss.GetDistanceTo(boss.SpawnPoint)} " +
                     $"tether={boss.TetherRange} speed={speed} reason=\"out of its tether {StrandedLeashMilliseconds / 1000} s with nobody fighting it in reach\"");
        }

        // Epic encounters, and scripted named monsters with their own short tether: ordinary monsters also carry a
        // 4,500 tether but simply walk home when they leave it (run 18 led a small hill cat home). Overnight 2026-10-19
        // bots stood "Fighting Lieutenant Saxe" (Hall of the Corrupt, a scripted GameNPC with a 1,500 tether and the
        // same out-of-tether immunity) until the 15-minute watchdog moved them, 88 times, and he killed 43 more. Base
        // run of 2026-10-29: Lieutenant Meade (Hall of the Corrupt, on Albion's way to Summoner's Hall) is a scripted
        // GameNPC, not an epic NPC, immune out of his tether; the Albion raid fought him there until its four hours ran
        // out. Merge of 2026-11-01: both rules count (Living Server's short-tether rule and the base's named-monster rule).
        public const int ScriptedTetherBelow = 4_500;

        public static bool HasEncounterTether(GameNPC npc) =>
            npc is IGameEpicNpc || npc != null && npc.GetType() != typeof(GameNPC) && npc.TetherRange is > 0 and < ScriptedTetherBelow;

        /// <summary>An epic NPC, or a scripted named monster (its own class, a capitalised name).</summary>
        public static bool IsTetheredEncounter(GameNPC npc) =>
            npc is IGameEpicNpc || npc != null && npc.GetType() != typeof(GameNPC) && npc is not GameBot &&
            !string.IsNullOrEmpty(npc.Name) && char.IsUpper(npc.Name[0]);

        public static bool IsOutOfTether(GameNPC npc) =>
            (HasEncounterTether(npc) || IsTetheredEncounter(npc)) && npc is { IsAlive: true, TetherRange: > 0 } &&
            npc.Brain is not IControlledBrain && npc.IsOutOfTetherRange ||
            DragonLairPlacement.IsOutOfLair(npc);

        /// <summary>
        /// For an autonomous gamebot whose target is a boss outside its tether: stop attacking it, and if the
        /// boss is after this bot, lead it back toward its spawn point. True when the bot's turn is used.
        /// </summary>
        public static bool TryHandle(GameBot bot, GameNPC boss)
        {
            if (bot?.IsAutonomousWorldBot != true || bot.IsPlayerLedGroup) return false;
            if (!IsOutOfTether(boss))
            {
                BackInsideTether(boss);
                return false;
            }
            if (bot.IsCasting && bot.castingComponent?.SpellHandler?.Spell?.IsHarmful == true) bot.StopCurrentSpellcast();
            bot.StopAttack();
            (bot.Brain as BotBrain)?.RemoveFromAggroList(boss);
            bot.TargetObject = null;
            // The pet backs off too, or it keeps the boss busy out there.
            if (bot.ControlledBrain?.Body?.TargetObject == boss) bot.ControlledBrain.Disengage();
            bool chased = boss.TargetObject == bot;
            LeashIfStranded(boss);
            if (chased)
            {
                Point3D home = boss.SpawnPoint;
                bot.PathTo(new Point3D(home.X, home.Y, home.Z), bot.MaxSpeed);
            }
            if (Log.IsInfoEnabled && GameLoop.GameLoopTime >= _nextLog)
            {
                _nextLog = GameLoop.GameLoopTime + 10_000;
                Log.Info($"AUTONOMOUS_TETHER_RESET boss=\"{boss.Name}\" region={boss.CurrentRegionID} bot={bot.Name} " +
                         $"leading={chased} fromSpawn={(int)boss.GetDistanceTo(boss.SpawnPoint)} tether={boss.TetherRange}");
            }
            return true;
        }
    }
}
