using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// #662 (owner decision 2026-11-26): a solo or duo gamebot sizes up a fight when enemies come into its perception range, and
// opens or backs off (RvrSightSizeUp). Backing off: it walks out of range, waits nearby, calls its close friends, then goes
// back to what it was doing. Each decision is a log row.
public sealed partial class AutonomousWorldBotController
{
    private long _sightHoldUntil;
    private Vector3? _sightBackOffPoint;
    private RvrSightCall? _sightLastCall;
    private long _sightLastLogTick = long.MinValue / 2;
    private int _sightRepaths;

    /// <summary>Whether this bot sizes up a fight on sight: an autonomous gamebot (never a companion), solo or a duo, in the
    /// frontier or a battleground (keep assaults included), with bot_rvr_sight_size_up on.</summary>
    internal static bool SightSizeUpApplies(GameBot bot) =>
        Properties.BOT_RVR_SIGHT_SIZE_UP && bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
        !bot.IsHiredCompanion && !bot.IsPlayerLedGroup &&
        (bot.CurrentRegionID is 1 or 100 or 200 || BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID)) &&
        RvrSightSizeUp.ForPartySize(bot.Group?.MemberCount ?? 1);

    /// <summary>The same roll for a bot and its contact for a minute, so the call does not flicker scan to scan.</summary>
    private static double SightRoll(GameBot bot, GameLiving nearest) =>
        RvrRoamCircuits.Roll($"sight|{bot.DatabaseID}", nearest.ObjectID * 1_000L + GameLoop.GameLoopTime / 60_000);

    private static RvrSightFacts SightFacts(GameBot bot, GameLiving[] enemies)
    {
        int allies = 1, allyLevels = bot.Level;
        foreach (GamePlayer player in bot.GetPlayersInRadius(RvrSightSizeUp.AllyRadius))
            if (player.IsAlive && player.Realm == bot.Realm) { allies++; allyLevels += player.Level; }
        foreach (GameNPC npc in bot.GetNPCsInRadius(RvrSightSizeUp.AllyRadius))
            if (npc is GameBot mate && mate != bot && mate.IsAlive && mate.Realm == bot.Realm) { allies++; allyLevels += mate.Level; }
        int levels = 0, top = 0, healers = 0;
        bool attacked = false;
        foreach (GameLiving enemy in enemies)
        {
            levels += enemy.Level;
            top = Math.Max(top, enemy.Level);
            if (MainHealer(enemy)) healers++;
            attacked |= FiringOnRealm(bot, enemy);
        }
        return new RvrSightFacts
        {
            OwnLevel = bot.Level,
            Allies = allies,
            AllyAverageLevel = allyLevels / allies,
            Enemies = enemies.Length,
            EnemyHealers = healers,
            EnemyAverageLevel = enemies.Length == 0 ? 0 : levels / enemies.Length,
            EnemyTopLevel = top,
            Attacked = attacked,
        };
    }

    /// <summary>
    /// Hook: TryEngageFrontierThreat, right after perception. The noticed targets this bot may open on: all of them, or
    /// none when it sizes the fight up and backs off. Bots this rule doesn't cover get the same array.
    /// </summary>
    private GameLiving[] SightSizeUpGate(GameBot bot, GameLiving[] perceived, long now)
    {
        if (perceived.Length == 0 || !SightSizeUpApplies(bot))
            return perceived;
        GameLiving[] people = perceived.Where(target => target is GamePlayer or GameBot).ToArray();
        if (people.Length == 0)
            return perceived;
        using var profile = AiProfile.Scope(AiSystem.RvrTactics);
        GameLiving nearest = people.OrderBy(bot.GetDistanceTo).First();
        RvrSightFacts facts = SightFacts(bot, people);
        double boldness = AutonomousRvrPerception.Boldness(
            AutonomousBotPersonality.Applies(bot) ? AutonomousBotPersonality.For(bot) : null);
        RvrSkillTier tier = RvrFightBoards.DefaultSkillTier(bot);
        RvrSightDecision decision = RvrSightSizeUp.Decide(facts, boldness, tier, SightRoll(bot, nearest));
        if (_sightLastCall != decision.Call || now - _sightLastLogTick >= RvrSightSizeUp.RelogMilliseconds)
        {
            _sightLastCall = decision.Call;
            _sightLastLogTick = now;
            if (Log.IsInfoEnabled)
                Log.Info($"RVR_SIGHT_SIZE_UP bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                         $"region={bot.CurrentRegionID} level={bot.Level} allies={facts.Allies} allyLevel={facts.AllyAverageLevel} " +
                         $"enemies={facts.Enemies} healers={facts.EnemyHealers} enemyLevel={facts.EnemyAverageLevel} " +
                         $"enemyTop={facts.EnemyTopLevel} nearest={(int)bot.GetDistanceTo(nearest)} odds={decision.Odds:0.00} " +
                         $"line={decision.Line:0.00} boldness={boldness:0.00} tier={tier} mistake={decision.Mistake} " +
                         $"reason={decision.Reason} call={(decision.Call == RvrSightCall.Engage ? "engage" : "back_off")}");
        }
        if (decision.Call == RvrSightCall.Engage)
        {
            EndSightHold(bot, "engage", now);
            return perceived;
        }
        StartSightBackOff(bot, people, nearest, now);
        return [];
    }

    private void StartSightBackOff(GameBot bot, GameLiving[] enemies, GameLiving nearest, long now)
    {
        bool fresh = _sightHoldUntil <= now;
        _sightHoldUntil = now + RvrSightSizeUp.WaitMilliseconds;
        if (!fresh)
            return;
        _sightRepaths = 0;
        _sightBackOffPoint = SightBackOffPoint(bot, enemies, nearest);
        int called = CallSightFriends(bot, enemies, now);
        if (_sightBackOffPoint is Vector3 point)
        {
            bot.StopAttack();
            bot.PathTo(point, bot.MaxSpeed);
        }
        if (Log.IsInfoEnabled)
            Log.Info($"RVR_SIGHT_BACK_OFF bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                     $"region={bot.CurrentRegionID} enemies={enemies.Length} nearest={(int)bot.GetDistanceTo(nearest)} " +
                     $"to={(_sightBackOffPoint is Vector3 p ? $"{(int)p.X},{(int)p.Y},{(int)p.Z}" : "none")} " +
                     $"waitSeconds={RvrSightSizeUp.WaitMilliseconds / 1000} called={called}");
    }

    /// <summary>A walkable point straight away from the enemies, past <see cref="RvrSightSizeUp.BackOffDistance"/> from the
    /// nearest; null when the mesh has none (the bot then just holds where it is).</summary>
    private static Vector3? SightBackOffPoint(GameBot bot, GameLiving[] enemies, GameLiving nearest)
    {
        var nav = PathfindingProvider.Instance;
        if (nav?.IsAvailable != true || !nav.HasNavmesh(bot.CurrentZone))
            return null;
        Vector2 centre = Vector2.Zero;
        foreach (GameLiving enemy in enemies)
            centre += new Vector2(enemy.X, enemy.Y);
        centre /= enemies.Length;
        Vector2 away = new Vector2(bot.X, bot.Y) - centre;
        away = away.LengthSquared() < 1 ? new Vector2(1, 0) : Vector2.Normalize(away);
        float step = Math.Max(500, RvrSightSizeUp.BackOffDistance - bot.GetDistanceTo(nearest) + 200);
        foreach (float turn in new[] { 0f, 0.5f, -0.5f, 1f, -1f })
        {
            Vector2 direction = Vector2.Transform(away, Matrix3x2.CreateRotation(turn));
            Vector3 raw = new(bot.X + direction.X * step, bot.Y + direction.Y * step, bot.Z);
            if (nav.GetClosestPoint(bot.CurrentZone, raw, 256, 256, 1024, nav.DefaultFilters) is Vector3 floor)
                return floor;
        }
        return null;
    }

    /// <summary>
    /// The close friends hear of the enemies, through the RvR spot memory every gamebot already reads: its group or warband
    /// mates and its bond friends (AutonomousBotBonds), realm-mates within the spotter radius who may answer a call. Each
    /// then notices the enemies after its own reaction delay and sizes the fight up itself. Returns how many were told.
    /// </summary>
    private static int CallSightFriends(GameBot bot, GameLiving[] enemies, long now)
    {
        RvrAwarenessProfile profile = RvrProfile();
        var warband = new HashSet<GameLiving>(RealmWarbandSupport.Get(bot)?.Members ?? []);
        int told = 0;
        foreach (GameNPC npc in bot.GetNPCsInRadius((ushort)profile.SpotterRadius))
        {
            if (npc is not GameBot mate || mate == bot || !mate.IsAlive || !CanHearRvrCall(mate, bot) || !MayAnswerRvrCall(mate))
                continue;
            bool close = mate.Group != null && mate.Group == bot.Group || warband.Contains(mate) ||
                AutonomousBotBonds.On && AutonomousBotBonds.ScoreBetween(mate, bot) is int score && AutonomousBotBonds.IsFriend(score);
            if (!close)
                continue;
            int delay = RvrReactionDelay(mate, profile);
            bool news = false;
            foreach (GameLiving enemy in enemies)
                news |= RvrPerceptionMemory.Alert(mate.DatabaseID, RvrTargetKey(enemy), now, delay);
            if (news) told++;
        }
        return told;
    }

    /// <summary>
    /// Called by BotBrain after the threat scan: true while this bot is backing off or waiting nearby (it owns the turn, so
    /// its route doesn't walk it back into the enemies). The hold ends when it is attacked (it fights back), after the wait,
    /// or when the rule no longer covers it; then it goes back to what it was doing.
    /// </summary>
    internal bool HoldsForSightSizeUp(BotBrain brain)
    {
        GameBot bot = brain?.BotBody;
        long now = GameLoop.GameLoopTime;
        if (_sightHoldUntil == 0 || bot == null)
            return false;
        if (!bot.IsAlive || bot.InCombat || brain.HasAggro || now >= _sightHoldUntil || !SightSizeUpApplies(bot))
        {
            EndSightHold(bot, !bot.IsAlive ? "dead" : bot.InCombat || brain.HasAggro ? "attacked" :
                now >= _sightHoldUntil ? "waited" : "out_of_scope", now);
            return false;
        }
        if (_sightBackOffPoint is Vector3 point && !bot.IsMoving &&
            Vector2.Distance(new(bot.X, bot.Y), new(point.X, point.Y)) > 150 && _sightRepaths < 3)
        {
            _sightRepaths++;
            bot.PathTo(point, bot.MaxSpeed);
        }
        return true;
    }

    private void EndSightHold(GameBot bot, string why, long now)
    {
        if (_sightHoldUntil == 0)
            return;
        _sightHoldUntil = 0;
        _sightBackOffPoint = null;
        if (Log.IsInfoEnabled && bot != null)
            Log.Info($"RVR_SIGHT_RESUME bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                     $"region={bot.CurrentRegionID} why={why}");
    }
}
