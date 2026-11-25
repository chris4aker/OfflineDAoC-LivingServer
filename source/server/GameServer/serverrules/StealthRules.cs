using System;
using DOL.GS.RealmAbilities;

namespace DOL.GS.ServerRules
{
    /// <summary>What the "see a stealther" rule needs to know about the one looking and the one hidden (plain values).</summary>
    public readonly record struct StealthLook(
        int ObserverLevel,
        int StealthSpec,
        bool ObserverDetectHidden = false,
        bool TargetDetectHidden = false,
        bool ObserverSeeHidden = false,
        bool TargetAssassin = false,
        bool Camouflage = false,
        bool Vanish = false,
        int ObserverStealthBonus = 0);

    /// <summary>Which sense lets a monster or guard uncover a stealther (the base server's uncover timer).</summary>
    public enum UncoverSense : byte
    {
        /// <summary>Outside every cone: nothing.</summary>
        None,
        /// <summary>In the 90-degree front cone: a sight check, then uncovered.</summary>
        Sight,
        /// <summary>In a side cone: the monster turns to look for 10 s.</summary>
        Hearing,
    }

    /// <summary>
    /// One set of stealth rules for the player and the gamebots (#522; docs/rvr-audit-1.2.3/stealth.md findings 2, 3 and 10).
    /// Before, the player's rules lived in GamePlayer, StealthSpecHandler and MaxSpeedCalculator, and a GameBot is a GameNPC,
    /// so a bot stealther had none of their costs: full speed, no "too close to hide" circle, never uncovered by a monster or
    /// a guard. And every enemy gamebot and its pets uncovered a stealthed player as if they were monsters. Every site now
    /// asks this class:
    /// <list type="bullet">
    /// <item><b>Speed</b> (<see cref="SpeedFactor(GameLiving)"/>): the player's formula, <see cref="PvpRules.StealthSpeedFactor"/>
    /// (#521), for the player always and for an autonomous gamebot in RvR (<see cref="PvpRules.TakesPlayerRules(GameLiving, GameLiving)"/>).</item>
    /// <item><b>Too close to hide</b> (<see cref="HideRadius"/>, <see cref="EnemyTooClose"/>): 1,024 - 896 x spec / enemy level,
    /// doubled by an enemy player character's Detect Hidden. The player's Stealth click, every gamebot's and every companion's
    /// <c>Stealth(true)</c>.</item>
    /// <item><b>Uncover</b> (<see cref="UncoverRadius"/>, <see cref="Sense"/>, <see cref="UncoverChance"/>, <see cref="MayUncover"/>):
    /// the base server's monster and guard timer, once a second. Only monsters and guards uncover. Player characters
    /// (the player, gamebots) and their pets and companions never do: they <i>see</i> a stealther (the player's client by
    /// <see cref="SeeRange"/>, a gamebot by RvrStealthNotice), as a 2003 player could. The timer now runs for an
    /// autonomous gamebot stealther too.</item>
    /// <item><b>See</b> (<see cref="SeeRange"/>): the player's detection range, with See Hidden at its 1.62 strength.</item>
    /// </list>
    /// Companions keep today's rules apart from the "too close" circle they already followed (the owner's #521 decision
    /// names autonomous gamebots only). The pure parts take plain values and are unit-tested (UT_StealthRules).
    /// </summary>
    public static class StealthRules
    {
        // ---- speed -------------------------------------------------------------------------------------------------

        /// <summary>
        /// The stealth speed factor of a hidden living (1 when it is not hidden or keeps today's rule): the player's formula
        /// (<see cref="PvpRules.StealthSpeedFactor"/>) with its spec, level, Mastery of Stealth and Shadow Run.
        /// </summary>
        public static double SpeedFactor(GameLiving living)
        {
            if (living is not { IsStealthed: true } || !PvpRules.TakesPlayerRules(living))
                return 1.0;
            AtlasOF_MasteryOfStealth mos = living.GetAbility<AtlasOF_MasteryOfStealth>();
            return PvpRules.StealthSpeedFactor(living.GetModifiedSpecLevel(Specs.Stealth), living.Level,
                mos != null ? mos.GetAmountForLevel(mos.Level) : 0,
                living.effectListComponent?.ContainsEffectForEffectType(eEffect.ShadowRun) == true);
        }

        // ---- too close to hide ---------------------------------------------------------------------------------------

        /// <summary>
        /// The "impossible to hide" circle around one enemy (StealthSpecHandler.IsObjectTooClose): 1,024 - 896 x stealth /
        /// enemy level, or 2,048 - 1,792 x stealth / enemy level against an enemy with Detect Hidden. Stealth is capped at 50.
        /// 0 or less: no circle. Pure.
        /// </summary>
        public static int HideRadius(int enemyLevel, int stealthSpec, bool enemyDetectHidden)
        {
            float enemy = Math.Max(1f, enemyLevel);
            float stealth = Math.Min(50, stealthSpec);
            return (int)(enemyDetectHidden ? 2048f - 1792f * stealth / enemy : 1024f - 896f * stealth / enemy);
        }

        /// <summary>Whether this enemy's Detect Hidden doubles the circle: a player character's (the player or a gamebot).</summary>
        public static bool DoublesHideRadius(GameObject enemy) =>
            enemy is GameLiving living && PvpRules.IsPlayerCharacter(living) && living.HasAbility(Abilities.DetectHidden);

        /// <summary>Whether <paramref name="enemy"/> stands inside <paramref name="hider"/>'s "too close" circle.</summary>
        public static bool IsTooClose(GameObject enemy, GameLiving hider)
        {
            int radius = HideRadius(enemy.Level, hider.GetModifiedSpecLevel(Specs.Stealth), DoublesHideRadius(enemy));
            return radius > 0 && enemy.IsWithinRadius(hider, radius);
        }

        /// <summary>
        /// The first live hostile within <paramref name="hider"/>'s "too close" circle, or null: the same search the player's
        /// Stealth click makes (enemy players, then enemy NPCs; game masters and the dead never count).
        /// </summary>
        public static GameObject EnemyTooClose(GameLiving hider)
        {
            if (hider?.CurrentRegion == null)
                return null;
            foreach (GamePlayer player in hider.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                if (player == hider || player.ObjectState != GameObject.eObjectState.Active || !player.IsAlive ||
                    player.Client?.Account?.PrivLevel > 1 || !GameServer.ServerRules.IsAllowedToAttack(player, hider, true))
                    continue;
                if (IsTooClose(player, hider))
                    return player;
            }
            foreach (GameNPC npc in hider.GetNPCsInRadius(WorldMgr.VISIBILITY_DISTANCE))
            {
                if (npc == hider || npc.ObjectState != GameObject.eObjectState.Active || !npc.IsAlive ||
                    !GameServer.ServerRules.IsAllowedToAttack(npc, hider, true))
                    continue;
                if (IsTooClose(npc, hider))
                    return npc;
            }
            return null;
        }

        // ---- uncover (monsters and guards) ---------------------------------------------------------------------------

        /// <summary>How often the uncover timer looks.</summary>
        public const int UncoverIntervalMilliseconds = 1_000;
        /// <summary>The farthest any monster or guard is looked at.</summary>
        public const int UncoverSearchRadius = 1_024;
        /// <summary>How long a monster that heard a stealther turns to look.</summary>
        public const int HearingTurnMilliseconds = 10_000;

        /// <summary>
        /// Whether this NPC may uncover a stealther at all: a monster or a guard. Not a gamebot or a companion (a GameBot),
        /// and not a pet of the player, a gamebot or a companion: player characters see stealthers, they never uncover
        /// them (the player by his client, a gamebot by RvrStealthNotice). Pure on the controller chain.
        /// </summary>
        public static bool MayUncover(GameNPC npc)
        {
            if (npc == null || npc is GameBot)
                return false;
            GameLiving controller = PvpRules.Controller(npc);
            return controller is not GamePlayer && controller is not GameBot;
        }

        /// <summary>
        /// A monster's uncover radius: 125 + 20 per level above the hider's stealth spec, plus 125 with Detect Hidden when the
        /// hider has none and no Camouflage or Vanish; never under 126. Pure.
        /// </summary>
        public static double UncoverRadius(int npcLevel, double stealthSpec, bool npcDetectHidden, bool hiderDetectHidden,
            bool hiderCamouflage, bool hiderVanish)
        {
            double radius = 125.0 + (Math.Max(npcLevel, 1.0) - stealthSpec) * 20.0;
            if (npcDetectHidden && !hiderDetectHidden && !hiderCamouflage && !hiderVanish)
                radius += 125;
            return Math.Max(radius, 126);
        }

        /// <summary>
        /// Which sense finds the hider at this angle (0-360, 0 straight ahead): the 90-degree front cone sees, two 120-degree
        /// side cones hear (wider for a monster above level 50); only the 30 degrees straight behind are safe. Pure.
        /// </summary>
        public static UncoverSense Sense(double angle, int npcLevel, int hiderLevel)
        {
            double fieldOfView = 90.0 / 2.0;
            if (angle >= 360 - fieldOfView || angle < fieldOfView)
                return UncoverSense.Sight;
            double fieldOfListen = 120.0;
            if (npcLevel > 50)
                fieldOfListen += (npcLevel - hiderLevel) * 3;
            fieldOfListen /= 2.0;
            bool hears = angle >= 45 + 60 - fieldOfListen && angle < 45 + 60 + fieldOfListen ||
                         angle >= 360 - 45 - 60 - fieldOfListen && angle < 360 - 45 - 60 + fieldOfListen;
            return hears ? UncoverSense.Hearing : UncoverSense.None;
        }

        /// <summary>The chance per look: 10% plus 1% a level above the stealth spec, falling off beyond 125 units. Pure.</summary>
        public static double UncoverChance(int npcLevel, double stealthSpec, double distance, double radius)
        {
            double chanceMod = 1.0;
            if (distance > 125)
                chanceMod = 1.0 - (distance - 125.0) / (radius - 125.0);
            return 0.1 + (npcLevel - stealthSpec) * 0.01 * chanceMod;
        }

        /// <summary>One monster's roll on one look, as the uncover timer sees it.</summary>
        public readonly record struct UncoverRoll(GameNPC By, UncoverSense Sense, double Distance);

        /// <summary>
        /// One look of the uncover timer for <paramref name="hider"/> (the player or an autonomous gamebot): every hostile
        /// monster and guard in range that may uncover (<see cref="MayUncover"/>) and wins its roll is passed to
        /// <paramref name="found"/>. The caller decides what a sight or a hearing does (the player's client checks line of
        /// sight; a gamebot's is checked on the navmesh).
        /// </summary>
        public static void Look(GameLiving hider, Action<UncoverRoll> found)
        {
            if (hider?.CurrentRegion == null || !hider.IsStealthed)
                return;
            double stealth = hider.GetModifiedSpecLevel(Specs.Stealth);
            bool hiderDetectHidden = hider.HasAbility(Abilities.DetectHidden);
            bool camouflage = hider.effectListComponent.ContainsEffectForEffectType(eEffect.Camouflage);
            bool vanish = hider.effectListComponent.ContainsEffectForEffectType(eEffect.Vanish);
            foreach (GameNPC npc in hider.GetNPCsInRadius(UncoverSearchRadius))
            {
                if (npc == hider || !MayUncover(npc) || !GameServer.ServerRules.IsAllowedToAttack(npc, hider, true))
                    continue;
                double radius = UncoverRadius(npc.Level, stealth, npc.HasAbility(Abilities.DetectHidden), hiderDetectHidden,
                    camouflage, vanish);
                double distance = npc.GetDistanceTo(hider);
                if (distance > radius)
                    continue;
                UncoverSense sense = Sense(npc.GetAngle(hider), npc.Level, hider.Level);
                if (sense == UncoverSense.None)
                    continue;
                if (Util.Chance(UncoverChance(npc.Level, stealth, distance, radius)))
                    found(new UncoverRoll(npc, sense, distance));
            }
        }

        // ---- see ---------------------------------------------------------------------------------------------------

        /// <summary>The server's hard cap on a player's detection range.</summary>
        public const int SeeHardCap = 1_900;

        /// <summary>See Hidden before 1.62: 2,700 - 36 x the archer's stealth spec. Pure.</summary>
        public static int SeeHiddenPre162(int stealthSpec) => 2700 - 36 * Math.Min(50, Math.Max(0, stealthSpec));

        /// <summary>
        /// The 1.62 cut of See Hidden (June 2003; rubric reference.md 4.1: "a quarter to three quarters of before, by archer
        /// spec and assassin level"): the share of the old range left, from 75% against a stealth spec far below the
        /// assassin's level down to 25% against a spec at or above it. The shape between is ours (a straight line), not a
        /// period figure. Pure.
        /// </summary>
        public static double SeeHidden162Share(int observerLevel, int stealthSpec)
        {
            int level = Math.Max(1, observerLevel);
            double gap = Math.Clamp((level - Math.Min(50, Math.Max(0, stealthSpec))) / (double)level, 0, 1);
            return 0.25 + 0.5 * gap;
        }

        /// <summary>See Hidden's range on this 1.65 server: the pre-1.62 range cut by <see cref="SeeHidden162Share"/>. Pure.</summary>
        public static int SeeHiddenRange(int observerLevel, int stealthSpec) =>
            (int)(SeeHiddenPre162(stealthSpec) * SeeHidden162Share(observerLevel, stealthSpec));

        /// <summary>
        /// How far a player character sees this stealther (the player case of GamePlayer.CanDetect): 0 through Vanish;
        /// (level - stealth) x 20 + 125, or x 50 + 250 with Detect Hidden when the hider has none and no Camouflage; at least
        /// See Hidden's range against an archer or Minstrel without Camouflage; plus the looker's stealth-detection bonus;
        /// never beyond 1,900. True Sight (no cap) and group-mates are the caller's. Pure.
        /// </summary>
        public static int SeeRange(in StealthLook look)
        {
            if (look.Vanish)
                return 0;
            int stealthLevel = Math.Min(50, Math.Max(0, look.StealthSpec));
            int levelDiff = Math.Max(0, look.ObserverLevel - stealthLevel);
            int range = look.ObserverDetectHidden && !look.TargetDetectHidden && !look.Camouflage
                ? levelDiff * 50 + 250
                : levelDiff * 20 + 125;
            if (look.ObserverSeeHidden && !look.TargetAssassin && !look.Camouflage)
                range = Math.Max(range, SeeHiddenRange(look.ObserverLevel, stealthLevel));
            range += look.ObserverStealthBonus;
            return Math.Clamp(range, 0, SeeHardCap);
        }

        /// <summary>The facts of <paramref name="observer"/> looking at the stealthed <paramref name="target"/> (reads the game).</summary>
        public static StealthLook LookOf(GameLiving observer, GameLiving target) => new(
            observer.Level, target.GetModifiedSpecLevel(Specs.Stealth),
            ObserverDetectHidden: observer.HasAbility(Abilities.DetectHidden),
            TargetDetectHidden: target.HasAbility(Abilities.DetectHidden),
            ObserverSeeHidden: observer.HasAbilityType(typeof(AtlasOF_SeeHidden)),
            TargetAssassin: target switch
            {
                GamePlayer player => player.CharacterClass?.IsAssassin == true,
                GameBot bot => bot.CharacterClass?.IsAssassin == true,
                _ => false,
            },
            Camouflage: target.effectListComponent.ContainsEffectForEffectType(eEffect.Camouflage),
            Vanish: target.effectListComponent.ContainsEffectForEffectType(eEffect.Vanish) ||
                target is GamePlayer vanisher && vanisher.EffectList.GetOfType<VanishEffect>() != null,
            ObserverStealthBonus: observer.BaseBuffBonusCategory[eProperty.Skill_Stealth]);
    }
}
