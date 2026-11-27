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
    // #766: the back-off walk ordered for this hold (kept after a failed walk clears _sightBackOffPoint), so its own failure
    // is never charged to the route the hold paused.
    private Vector3? _sightBackOffOrder;
    private RvrSightCall? _sightLastCall;
    private long _sightLastLogTick = long.MinValue / 2;
    private int _sightRepaths;
    private string _sightRollKey;

    /// <summary>Whether this bot sizes up a fight on sight: an autonomous gamebot (never a companion) in the frontier or a
    /// battleground (keep assaults included), with bot_rvr_sight_size_up on; alone, or a duo the stance machine doesn't
    /// cover (RvrSightSizeUp.CoversParty).</summary>
    internal static bool SightSizeUpApplies(GameBot bot, BotBrain brain)
    {
        if (!Properties.BOT_RVR_SIGHT_SIZE_UP || bot?.IsAutonomousWorldBot != true || bot.IsTemporaryGroupHelper ||
            bot.IsHiredCompanion || bot.IsPlayerLedGroup ||
            !(bot.CurrentRegionID is 1 or 100 or 200 || BattlegroundBrackets.IsBattlegroundRegion(bot.CurrentRegionID)))
            return false;
        int members = bot.Group?.MemberCount ?? 1;
        return RvrSightSizeUp.CoversParty(members, members >= 2 && RvrStanceSwitchesOn && brain?.RvrTacticsActive() == true);
    }

    /// <summary>The same roll for a bot and its contact for a minute, so the call does not flicker scan to scan.</summary>
    private double SightRoll(GameBot bot, GameLiving nearest) =>
        RvrRoamCircuits.Roll(_sightRollKey ??= $"sight|{bot.DatabaseID}", nearest.ObjectID * 1_000L + GameLoop.GameLoopTime / 60_000);

    /// <summary>A realm-mate who would really fight beside the bot: its group, its warband or a bond friend.</summary>
    private static bool FightsBeside(GameBot bot, GameBot mate, GameLiving[] warband) =>
        mate.Group != null && mate.Group == bot.Group || warband != null && Array.IndexOf(warband, mate) >= 0 ||
        AutonomousBotBonds.On && AutonomousBotBonds.ScoreBetween(mate, bot) is int score && AutonomousBotBonds.IsFriend(score);

    private static RvrSightFacts SightFacts(GameBot bot, GameLiving[] enemies, int count)
    {
        int allies = 1, allyLevels = bot.Level, fighters = 1;
        GameLiving[] warband = RealmWarbandSupport.Get(bot)?.Members;
        foreach (GamePlayer player in bot.GetPlayersInRadius(RvrSightSizeUp.AllyRadius))
            if (player.IsAlive && player.Realm == bot.Realm) { allies++; fighters++; allyLevels += player.Level; }
        foreach (GameNPC npc in bot.GetNPCsInRadius(RvrSightSizeUp.AllyRadius))
            if (npc is GameBot mate && mate != bot && mate.IsAlive && mate.Realm == bot.Realm)
            {
                allies++;
                allyLevels += mate.Level;
                if (FightsBeside(bot, mate, warband)) fighters++;
            }
        int levels = 0, top = 0, healers = 0;
        bool attacked = false;
        for (int i = 0; i < count; i++)
        {
            GameLiving enemy = enemies[i];
            levels += enemy.Level;
            top = Math.Max(top, enemy.Level);
            if (MainHealer(enemy)) healers++;
            attacked |= FiringOnRealm(bot, enemy);
        }
        return new RvrSightFacts
        {
            OwnLevel = bot.Level,
            Allies = allies,
            Fighters = fighters,
            AllyAverageLevel = allyLevels / allies,
            Enemies = count,
            EnemyHealers = healers,
            EnemyAverageLevel = count == 0 ? 0 : levels / count,
            EnemyTopLevel = top,
            Attacked = attacked,
        };
    }

    /// <summary>
    /// Hook: TryEngageFrontierThreat, right after perception. The noticed targets this bot may open on: all of them, or
    /// none when it sizes the fight up and backs off. Bots this rule doesn't cover get the same array.
    /// </summary>
    private GameLiving[] SightSizeUpGate(BotBrain brain, GameBot bot, GameLiving[] perceived, long now)
    {
        if (perceived.Length == 0 || !SightSizeUpApplies(bot, brain))
            return perceived;
        // The enemy people (players and gamebots) and the nearest, in one pass with no allocation unless one is found.
        GameLiving nearest = null;
        int nearestDistance = int.MaxValue, count = 0;
        GameLiving[] people = null;
        foreach (GameLiving target in perceived)
        {
            if (target is not (GamePlayer or GameBot))
                continue;
            people ??= new GameLiving[perceived.Length];
            people[count++] = target;
            int distance = bot.GetDistanceTo(target);
            if (distance < nearestDistance) { nearestDistance = distance; nearest = target; }
        }
        if (count == 0)
            return perceived;
        using var profile = AiProfile.Scope(AiSystem.RvrTactics);
        RvrSightFacts facts = SightFacts(bot, people, count);
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
                         $"region={bot.CurrentRegionID} level={bot.Level} allies={facts.Allies} fighters={facts.Fighters} allyLevel={facts.AllyAverageLevel} " +
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
        StartSightBackOff(bot, people.AsSpan(0, count).ToArray(), nearest, now);
        return [];
    }

    private void StartSightBackOff(GameBot bot, GameLiving[] enemies, GameLiving nearest, long now)
    {
        (long until, bool fresh) = RvrSightSizeUp.Rearm(now, _sightHoldUntil);
        _sightHoldUntil = until;
        if (!fresh)
            return;
        _sightRepaths = 0;
        _sightBackOffOrder = null;
        _sightBackOffPoint = SightBackOffPoint(bot, enemies, nearest);
        int called = CallSightFriends(bot, enemies, now);
        if (_sightBackOffPoint is Vector3 point)
        {
            bot.StopAttack();
            _sightBackOffOrder = point;
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
        GameLiving[] warband = RealmWarbandSupport.Get(bot)?.Members;
        int told = 0;
        foreach (GameNPC npc in bot.GetNPCsInRadius((ushort)profile.SpotterRadius))
        {
            if (npc is not GameBot mate || mate == bot || !mate.IsAlive || !CanHearRvrCall(mate, bot) || !MayAnswerRvrCall(mate) ||
                !FightsBeside(bot, mate, warband))
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
        string resume = RvrSightSizeUp.HoldStep(now, _sightHoldUntil, bot.IsAlive,
            bot.InCombat || brain.HasAggro || GroupMateInCombat(bot), SightSizeUpApplies(bot, brain));
        if (resume != null)
        {
            EndSightHold(bot, resume, now);
            return false;
        }
        // #766: a back-off walk the mover could not path ends here: the bot holds where it stands instead of ordering the same
        // walk again, and the failure stays with the hold (it is not the paused route's).
        if (TakeBackOffFailure(bot))
            _sightBackOffPoint = null;
        if (_sightBackOffPoint is Vector3 point && !bot.IsMoving &&
            Vector2.Distance(new(bot.X, bot.Y), new(point.X, point.Y)) > 150 && _sightRepaths < 3)
        {
            _sightRepaths++;
            bot.PathTo(point, bot.MaxSpeed);
        }
        return true;
    }

    /// <summary>#766: consumes a pending mover failure that belongs to this hold's back-off walk; true when there was one.
    /// Any other failure stays pending for the route's own recovery.</summary>
    private bool TakeBackOffFailure(GameBot bot)
    {
        if (!bot.PeekAutonomousPathFailure(out Vector3 failed) || !RvrSightSizeUp.FailureIsBackOffs(_sightBackOffOrder, failed))
            return false;
        bot.TryConsumeAutonomousPathFailure(out _, out _);
        return true;
    }

    /// <summary>A member of the bot's group (a duo's partner) is in a fight: the hold ends, and the bot fights back.</summary>
    private static bool GroupMateInCombat(GameBot bot)
    {
        Group group = bot.Group;
        if (group == null)
            return false;
        foreach (GameLiving member in group.GetMembersInTheGroup())
            if (member != bot && member.IsAlive && (member.InCombat || member.IsAttacking))
                return true;
        return false;
    }

    private void EndSightHold(GameBot bot, string why, long now)
    {
        if (_sightHoldUntil == 0)
            return;
        _sightHoldUntil = 0;
        _sightBackOffPoint = null;
        if (bot != null)
        {
            // #766: the route the hold paused resumes clean. A failed back-off walk is not a failure of the travel goal (the
            // route recovery used to count it, aim its corners at the back-off point and drop the goal after three), and the
            // wait is not a route stall (a bot that could not walk away resumed with a 30-second-old progress clock).
            TakeBackOffFailure(bot);
            _lastRoutePosition = new Vector3(bot.X, bot.Y, bot.Z);
            _lastRouteProgressTick = now;
        }
        _sightBackOffOrder = null;
        if (Log.IsInfoEnabled && bot != null)
            Log.Info($"RVR_SIGHT_RESUME bot={bot.Name} id={bot.DatabaseID} realm={GlobalConstants.RealmToName(bot.Realm)} " +
                     $"region={bot.CurrentRegionID} why={why}");
    }
}
