using System;
using System.Collections.Generic;
using DOL.AI.Brain;
using DOL.GS.ServerProperties;

namespace DOL.GS;

// #531 step 1 (RvR work 13, piece D): the threat scan proposes its contacts and the group's plan picks the first target
// (RvrFirstPick). Field only: a bot the open-field tactics leave alone (keeps, siege crews, battleground assaults, raids,
// relic carriers, a player's group, companions) keeps the spread pick (SelectDistributedRvrTarget).
public sealed partial class AutonomousWorldBotController
{
    /// <summary>The scan's targets the plan picks from must be the bot's contacts from at most this long ago.</summary>
    public const long FirstPickContactAgeMilliseconds = 15_000;

    /// <summary>
    /// Whether the plan picks this bot's first target: rvr_bot_field_tactics and rvr_bot_assist on, and the open-field
    /// tactics apply to the bot (an autonomous gamebot no player has, in a battle region, in <see cref="RvrTacticsScope"/>).
    /// </summary>
    public static bool FirstPickApplies(GameBot bot) =>
        Properties.RVR_BOT_ASSIST && BotBrain.RvrTacticsApplyTo(bot);

    /// <summary>
    /// The plan's first pick from <paramref name="proposed"/> (the scan's targets after its gates), or null when there is
    /// none. Only the bot's contacts (RvrContacts.Proposals, at most 15 s old) are picked from while any is proposed.
    /// <paramref name="fightsGroup"/> says whether an enemy is firing on the bot's realm or group. Reads the game; the
    /// rule is <see cref="RvrFirstPick.Pick"/>. Public for the RvR fight rehearsal (#520).
    /// </summary>
    public static GameLiving PlanFirstPick(GameBot bot, IReadOnlyList<GameLiving> proposed, Func<GameLiving, bool> fightsGroup,
        long now, out RvrFirstPickReason reason)
    {
        reason = RvrFirstPickReason.None;
        if (bot == null || proposed == null || proposed.Count == 0)
            return null;
        var contacts = new HashSet<long>();
        foreach (RvrContact contact in RvrContactTable.Proposals(bot.DatabaseID, bot.CurrentRegionID, bot.X, bot.Y, now,
                     FirstPickContactAgeMilliseconds))
            contacts.Add(contact.Target);
        var pool = new List<GameLiving>(proposed.Count);
        foreach (GameLiving target in proposed)
            if (target != null && contacts.Contains(RvrPartyKey.TargetKey(target)))
                pool.Add(target);
        if (pool.Count == 0)
            foreach (GameLiving target in proposed)
                if (target != null)
                    pool.Add(target);

        // The group's centre and what its mates fight now.
        var mates = new List<GameLiving>(8);
        double cx = 0, cy = 0;
        int heads = 0;
        foreach (GameLiving member in bot.Group?.GetMembersInTheGroup() ?? (IEnumerable<GameLiving>)new[] { bot })
        {
            if (member?.IsAlive != true || member.CurrentRegionID != bot.CurrentRegionID)
                continue;
            cx += member.X;
            cy += member.Y;
            heads++;
            if (member != bot && (member.InCombat || member.IsAttacking) && member.TargetObject is GameLiving fought)
                mates.Add(fought);
        }
        if (heads == 0)
        {
            cx = bot.X;
            cy = bot.Y;
            heads = 1;
        }
        cx /= heads;
        cy /= heads;

        RvrFightPlan plan = RvrFightBoards.PeekPlan(bot);
        GameLiving called = plan?.CalledTarget;
        // #599 step 2: a battlegroup's follower group opens on the lead group's call when the scan has it within the call
        // radius of the group's centre (BattlegroupCallTakes).
        GameLiving shared = RvrFightBoards.BattlegroupCallOf(bot, now);
        if (shared != null && pool.Contains(shared) &&
            BattlegroupCallTakes((float)Math.Sqrt((shared.X - cx) * (shared.X - cx) + (shared.Y - cy) * (shared.Y - cy))))
            called = shared;
        else
            shared = null;
        RvrSkillTier tier = plan != null && !ReferenceEquals(plan, RvrFightPlan.None) ? plan.SkillTier
            : RvrFightBoards.StateOf(RvrFightBoards.KeyOf(bot)).SkillTier ?? RvrFightBoards.DefaultSkillTier(bot);
        GameLiving seedHolder = bot.Group?.LivingLeader ?? bot;
        ulong seed = (ulong)(seedHolder is GameBot leaderBot && leaderBot.DatabaseID > 0 ? leaderBot.DatabaseID : RvrPartyKey.TargetKey(seedHolder));

        var candidates = new RvrFirstPickCandidate[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            GameLiving target = pool[i];
            int on = 0;
            foreach (GameLiving fought in mates)
                if (fought == target)
                    on++;
            double dx = target.X - cx, dy = target.Y - cy;
            candidates[i] = new RvrFirstPickCandidate(FirstPickCombatant(target, fightsGroup?.Invoke(target) == true),
                Engine: target is GameSiegeWeapon, Held: HeldByCrowdControl(bot, target),
                DistanceFromGroup: (float)Math.Sqrt(dx * dx + dy * dy), MatesOn: on, Called: called != null && called == target);
        }
        int pick = RvrFirstPick.Pick(candidates, tier, seed, out reason);
        if (pick >= 0 && reason == RvrFirstPickReason.Called && shared != null && pool[pick] == shared)
            reason = RvrFirstPickReason.Battlegroup;
        return pick < 0 ? null : pool[pick];
    }

    /// <summary>
    /// #599 step 2: whether the first pick takes the battlegroup lead's call that stands <paramref name="distanceFromGroup"/>
    /// from the group's centre: within the call radius (as the plan's assist step, RvrAssist.BattlegroupCandidate). Pure.
    /// </summary>
    public static bool BattlegroupCallTakes(float distanceFromGroup) => distanceFromGroup <= RvrFirstPick.CallRadius;

    /// <summary>An enemy as the first pick scores it (the board's record of it, without the board).</summary>
    private static RvrCombatant FirstPickCombatant(GameLiving living, bool fightsGroup)
    {
        eCharacterClass characterClass = living switch
        {
            GameBot { CharacterClass: not null } enemyBot => (eCharacterClass)enemyBot.CharacterClass.ID,
            GamePlayer { CharacterClass: not null } player => (eCharacterClass)player.CharacterClass.ID,
            _ => eCharacterClass.Unknown,
        };
        bool pet = living is GameNPC { Brain: IControlledBrain } and not GameBot;
        return new RvrCombatant
        {
            Living = living,
            Key = RvrPartyKey.TargetKey(living),
            Realm = living.Realm,
            Class = characterClass,
            Role = pet ? RvrRole.Pet : RvrRoles.For(characterClass),
            IsPlayer = living is GamePlayer,
            IsPet = pet,
            Level = living.Level,
            HealthPercent = living.HealthPercent,
            TargetsGroup = fightsGroup,
            X = living.X,
            Y = living.Y,
            Z = living.Z,
        };
    }
}
