using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Marfoirwort's Life-leech (Living Server 1.2.1, #412; reworked in 1.2.2 after the raid tactic audit, finding 2; tests
    /// only). His script (Marfoirwort.cs, unchanged) casts Marfoirwort Life-leech on a quarter of his melee hits: 450 body
    /// damage to his target and to every enemy within 350 of it, raiders and their pets alike, and his DealDamage heals him
    /// by every point of body damage he deals. Owner's PC, build ccba364, 2026-11-16 (sweep 3, 17:21-18:40): attempt 1 held
    /// him at 79% at the lowest until "no damage for five minutes" ended it, then two wipes in 77 minutes, with about 25
    /// raiders and pets inside each leech.
    /// <para>The geometry (1.2.2): he follows his target to his own <see cref="GameNPC.StickMinimumRange"/>
    /// (MeleeAttackRange 350 x 0.375 = 131) and a follow only closes, so his target stands about 131 from him whatever it is
    /// told (PC log: 131-150). Bot melee reach is 200, center to center, so a raider in reach of him stands at most
    /// 131 + 200 = 331 from his target: inside the 350. No melee spot clears the leech while he stands on his target (the
    /// 1.2.1 rule held his target at 185 and sent the melee behind him to 370-380 from it; his target never stood at 185, the
    /// spot behind him lay 326 from it, and the rule moved every melee raider again every turn, so they never swung).</para>
    /// <para>So, by his numbers: each raider inside heals him about 450 x 0.85 every 4 x 2.7 s, about 35 a second, while a
    /// raider deals about 7 a second on him (the b1b6846 ledger's 400 a raider-minute); a melee raider inside heals him five
    /// times what it deals. Only his target fights him in melee. The other melee raiders shoot him with their distance
    /// weapon from clear of the leech when they own one (<see cref="DungeonRule.FightsAtRange"/>), or wait clear of it
    /// (StandOff: they keep their place, the rule gives no new spot once there). When he turns to a raider that is not a
    /// tank, the tanks go in and take him back, then step out again. Should his target ever stand far enough from him for a
    /// spot behind him to clear the leech (he walks after a new target), the melee fight from behind him there. Everyone at
    /// range steps clear of 350 around his target, his target's party too. No bot's pet attacks him (a pet at his feet heals
    /// him by about 380 a leech), but an Animist's shrooms are planted clear of the leech and those fight him
    /// (<see cref="DungeonRule.PetMayHit"/>: audit finding 13). The boss is unchanged.</para>
    /// <para>Log: EPIC_LEECH_EXPOSURE (each 15 s per boss: how many raiders and pets stood within 350 of his target) and
    /// the catalog's EPIC_SPREAD_LANDED lines (within=). Autonomous gamebots only, as every tactic.</para>
    /// </summary>
    public static partial class EpicEncounterTactics
    {
        /// <summary>His Life-leech's radius around his target (Marfoirwort.MarfoirwortDD).</summary>
        public const int LeechRadius = 350;
        /// <summary>A raider this close past the radius counts as inside (positions drift a little between turns).</summary>
        public const int LeechMargin = 10;
        /// <summary>EPIC_LEECH_EXPOSURE is written at most this often per boss.</summary>
        public const int LeechExposureLogMilliseconds = 15_000;
        /// <summary>A bot's melee reach on him (GameLiving.MeleeAttackRange, checked center to center).</summary>
        public const int LeechBotMeleeReach = 200;

        /// <summary>What a melee raider (not his target, not at range) does on Marfoirwort.</summary>
        public enum LeechMeleeRole { Swing, Shoot, Wait }

        [DungeonRules]
        private static IEnumerable<DungeonRule> MarfoirwortRules() =>
        [
            new("MarfoirwortLeech", [nameof(Marfoirwort)], MarfoirwortLeechAdvice)
            {
                Summary = $"His Life-leech hits everyone within {LeechRadius} of his target and heals him by all of it. He stands " +
                          $"on his target (he follows it to {(int)(350 * 0.375)}), so no melee spot within bot reach ({LeechBotMeleeReach}) " +
                          $"clears it: only his target fights him in melee; the other melee shoot him from clear of {LeechRadius} " +
                          "with their distance weapon or wait clear of it, and tanks go in only to take him back from a raider " +
                          $"that is not a tank. Everyone at range stands clear of {LeechRadius} around his target (its own party " +
                          "too). No bot's pet attacks him but an Animist's shrooms, planted clear of the leech.",
                NoPets = _ => true,
                PetMayHit = LeechShroomMayHit,
                FightsAtRange = (bot, boss) => MarfoirwortMeleeRoleOf(bot, boss) == LeechMeleeRole.Shoot,
                Act = (bot, boss, _) =>
                {
                    if (MarfoirwortMeleeRoleOf(bot, boss) != LeechMeleeRole.Shoot || bot.ActiveWeaponSlot == eActiveWeaponSlot.Distance) return;
                    if (bot.attackComponent?.AttackState == true) bot.StopAttack();
                    bot.SwitchWeapon(eActiveWeaponSlot.Distance);
                },
            },
        ];

        /// <summary>How far behind him a melee raider stands for the leech (pure): far enough that it stands
        /// <see cref="LeechRadius"/> + 3 x <see cref="LeechMargin"/> from his target, within bot melee reach.</summary>
        public static float LeechBehindFor(float anchorToBoss) =>
            Math.Clamp(LeechRadius + 3 * LeechMargin - anchorToBoss, BehindDistance, BehindReach);

        /// <summary>A melee spot within bot reach of him clears the leech around his target (pure): only while his target
        /// stands at least <see cref="LeechRadius"/> + 3 x <see cref="LeechMargin"/> - <see cref="BehindReach"/> (185) from
        /// him. He closes to 131 (<see cref="GameNPC.StickMinimumRange"/>), so this is true only while he walks after a new
        /// target.</summary>
        public static bool LeechClearMeleePossible(float anchorToBoss) =>
            anchorToBoss + BehindReach >= LeechRadius + 3 * LeechMargin;

        /// <summary>The raider stands clear of the leech around his target (pure).</summary>
        public static bool ClearOfLeech(Vector3 raider, Vector3 anchor) => Flat(raider, anchor) > LeechRadius + LeechMargin;

        /// <summary>
        /// The role of a melee raider that is not his target (pure): it swings when a spot in its reach clears the leech, or
        /// when it is a tank and his target is not one (it takes him back); otherwise it shoots when it owns a usable distance
        /// weapon, or waits.
        /// </summary>
        public static LeechMeleeRole LeechMeleeRoleFor(float anchorToBoss, bool isTank, bool anchorIsTank, bool ownsDistanceWeapon)
        {
            if (LeechClearMeleePossible(anchorToBoss) || isTank && !anchorIsTank) return LeechMeleeRole.Swing;
            return ownsDistanceWeapon ? LeechMeleeRole.Shoot : LeechMeleeRole.Wait;
        }

        /// <summary>
        /// A melee raider's advice on him (pure; never null). <paramref name="role"/> from
        /// <see cref="LeechMeleeRoleFor"/>. It never moves a raider already where it should be: in reach of him and clear of the
        /// leech (Swing), clear of the leech (Shoot: fight on with its distance weapon; Wait: StandOff with no spot).
        /// </summary>
        public static Advice? LeechMeleeAdvice(Vector3 raider, Vector3 boss, Vector3 anchor, LeechMeleeRole role, GameNPC marfoirwort,
            string anchorName)
        {
            bool clear = ClearOfLeech(raider, anchor);
            float anchorToBoss = Flat(anchor, boss);
            switch (role)
            {
                case LeechMeleeRole.Swing:
                    // Done: in reach and clear; or no clear spot exists (a tank taking him back) and it fights where it stands.
                    // Action None (final): the catalog's spread rule would move a paladin or warden out again.
                    if (!LeechClearMeleePossible(anchorToBoss) || clear && Flat(raider, boss) <= LeechBotMeleeReach)
                        return new(BotAction.None, marfoirwort, null, null);
                    return new(BotAction.Reposition, marfoirwort, EncounterMeleeStations.BehindSpot(boss, anchor, LeechBehindFor(anchorToBoss), raider.Z),
                        $"leech: melee behind \"{marfoirwort?.Name}\", clear of {LeechRadius} around \"{anchorName}\"");
                case LeechMeleeRole.Shoot:
                    return clear
                        ? new(BotAction.None, marfoirwort, null, null)
                        : new(BotAction.Reposition, marfoirwort, LeechRangeSpot(raider, boss, anchor),
                            $"leech: no melee spot clears {LeechRadius} around \"{anchorName}\"; shoot him from clear of it");
                default:
                    return new(BotAction.StandOff, marfoirwort, clear ? null : LeechRangeSpot(raider, boss, anchor),
                        $"leech: no melee spot clears {LeechRadius} around \"{anchorName}\" (a swing there heals him more than it hurts); wait clear of it");
            }
        }

        /// <summary>Where a raider at range stands clear of the leech (pure): null when clear, else out on its side.</summary>
        public static Vector3? LeechRangeSpot(Vector3 raider, Vector3 boss, Vector3 anchor)
        {
            if (ClearOfLeech(raider, anchor)) return null;
            return SpreadSpot(raider, boss, anchor, LeechRadius, true);
        }

        /// <summary>The bot owns a distance weapon it can use (bow, crossbow or thrown, in its distance slot).</summary>
        public static bool OwnsUsableDistanceWeapon(GameLiving raider)
        {
            if (raider is not GameBot bot) return false;
            DbInventoryItem item = bot.Inventory?.GetItem(eInventorySlot.DistanceWeapon);
            if (item == null) return false;
            try { return BotRangedCombat.CanUse(bot, item); }
            catch (Exception) { return BotRangedCombat.IsUsableWeapon(item); }
        }

        /// <summary>The role of this bot on him, or null when the melee roles don't apply (his target, a raider at range, he
        /// fights nobody).</summary>
        private static LeechMeleeRole? MarfoirwortMeleeRoleOf(GameBot bot, GameNPC boss)
        {
            if (bot == null || boss is not Marfoirwort || !boss.InCombat || boss.TargetObject is not GameLiving anchor ||
                !anchor.IsAlive || anchor == bot)
                return null;
            int classId = EncounterTargets.ClassId(bot);
            if (FightsFromRange(CanShootFlyers(bot), classId)) return null;
            return LeechMeleeRoleFor(Flat(At(anchor), At(boss)), IsTankLiving(bot), IsTankLiving(anchor), OwnsUsableDistanceWeapon(bot));
        }

        private static Advice? MarfoirwortLeechAdvice(RuleContext c)
        {
            GameNPC boss = c.Target;
            if (!boss.InCombat || boss.TargetObject is not GameLiving anchor || !anchor.IsAlive) return null;
            Vector3 at = c.At, bossAt = At(boss), anchorAt = At(anchor);
            TraceSpreadTarget(boss, anchor, LeechRadius, c.Now);
            // His target fights him where it stands: he stands on it whatever it does.
            if (c.Raider == anchor)
            {
                LogLeechExposure(boss, anchor, c.Live, c.Now);
                return null;
            }
            if (c.StandsBack)
                return LeechRangeSpot(at, bossAt, anchorAt) is Vector3 clear
                    ? new(BotAction.Reposition, boss, clear, $"leech: clear of {LeechRadius} around \"{anchor.Name}\" (it heals him)")
                    : null;
            LeechMeleeRole role = LeechMeleeRoleFor(Flat(anchorAt, bossAt), IsTankLiving(c.Raider), IsTankLiving(anchor),
                OwnsUsableDistanceWeapon(c.Raider));
            if (role == LeechMeleeRole.Swing && LeechClearMeleePossible(Flat(anchorAt, bossAt)))
            {
                EncounterMeleeStations.Hold(c.Raider, boss, BehindDistance, c.Now);
                EncounterMeleeStations.Apply(c.Raider, boss, BehindDistance);
            }
            return LeechMeleeAdvice(at, bossAt, anchorAt, role, boss, anchor.Name);
        }

        /// <summary>Audit finding 13: an Animist's shroom (a turret, whose brain is a pet's) planted clear of the leech around
        /// his target fights him: the leech can't reach it, and its bolts reach him from there. Any other pet stays off him.</summary>
        private static bool LeechShroomMayHit(GameNPC pet, GameNPC boss) =>
            pet?.Brain is DOL.AI.Brain.TurretBrain && boss is Marfoirwort && boss.TargetObject is GameLiving anchor &&
            ClearOfLeech(At(pet), At(anchor));

        /// <summary>Where an Animist plants a shroom on <paramref name="target"/> (pure): a spot within the leech around
        /// Marfoirwort's target moves out along the line from his target toward the Animist, to
        /// <see cref="LeechRadius"/> + 3 x <see cref="LeechMargin"/> from it (never past the Animist itself). Null: keep the
        /// spot.</summary>
        public static Vector3? LeechShroomSpot(Vector3 desired, Vector3 owner, Vector3 anchor)
        {
            if (ClearOfLeech(desired, anchor)) return null;
            Vector2 a = new(anchor.X, anchor.Y), o = new(owner.X, owner.Y);
            float toOwner = Vector2.Distance(a, o);
            if (toOwner < 1) return null;
            float d = Math.Min(LeechRadius + 3 * LeechMargin, toOwner);
            Vector2 at = a + Vector2.Normalize(o - a) * d;
            return new Vector3(at.X, at.Y, desired.Z);
        }

        /// <summary>The shroom spot for <paramref name="combatTarget"/> (AutonomousPetSupport.PrepareAnimistGroundTarget): moved
        /// clear of Marfoirwort's leech when it would land in it; null otherwise.</summary>
        public static Vector3? ShroomSpotFor(GameLiving combatTarget, Vector3 desired, Vector3 owner) =>
            combatTarget is Marfoirwort { InCombat: true, TargetObject: GameLiving anchor }
                ? LeechShroomSpot(desired, owner, At(anchor)) : null;

        private static readonly Dictionary<GameNPC, long> LastLeechExposureLog = new();

        /// <summary>How many raiders (bots and players) and pets stand within <see cref="LeechRadius"/> of <paramref name="anchor"/>
        /// besides it (pure): what one Life-leech would heal him from.</summary>
        public static (int Raiders, int Pets) LeechExposure(Vector3 anchor, IEnumerable<(Vector3 At, bool Pet)> others) =>
            (others ?? []).Where(o => Flat(o.At, anchor) <= LeechRadius)
                .Aggregate((0, 0), (n, o) => o.Pet ? (n.Item1, n.Item2 + 1) : (n.Item1 + 1, n.Item2));

        private static void LogLeechExposure(GameNPC boss, GameLiving anchor, IReadOnlyList<GameNPC> live, long now)
        {
            if (!Log.IsInfoEnabled) return;
            lock (LastLeechExposureLog)
            {
                if (LastLeechExposureLog.TryGetValue(boss, out long last) && now - last < LeechExposureLogMilliseconds) return;
                if (LastLeechExposureLog.Count > 64) LastLeechExposureLog.Clear();
                LastLeechExposureLog[boss] = now;
            }
            Vector3 a = At(anchor);
            var others = new List<(Vector3, bool)>();
            foreach (GameNPC n in live ?? [])
                if (n != null && n != anchor && n != boss && n.IsAlive && n.CurrentRegionID == boss.CurrentRegionID && n.Realm != boss.Realm &&
                    n.Realm != 0)
                    others.Add((At(n), n is not GameBot));
            foreach (GamePlayer p in anchor.GetPlayersInRadius((ushort)(LeechRadius + 50)))
                if (p != null && p != anchor && p.IsAlive) others.Add((At(p), false));
            var (raiders, pets) = LeechExposure(a, others);
            Log.Info($"EPIC_LEECH_EXPOSURE boss=\"{boss.Name}\" region={boss.CurrentRegionID} health={boss.HealthPercent} " +
                     $"target=\"{anchor.Name}\" targetToBoss={(int)Flat(a, At(boss))} inside={raiders} pets={pets} radius={LeechRadius}");
        }
    }
}
