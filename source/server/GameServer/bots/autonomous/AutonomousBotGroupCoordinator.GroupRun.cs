using System;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;

namespace DOL.GS;

public static partial class AutonomousBotGroupCoordinator
{
    // From Offline DAoC True 1.65 (shadowofze; its veteran review M6, 2026-11-11): a party calls RUN while everyone is still alive (BotGroupRetreat has the rule).
    private sealed class GroupRunState
    {
        public long NextCheckTick;
        public long CooldownUntil;
        public long RetreatUntil;
    }
    private static readonly ConditionalWeakTable<Session, GroupRunState> GroupRuns = new();

    /// <summary>Caller holds Sync. At most every 2 s per fighting PvE party: read the fight board, call RUN when lost.</summary>
    private static void CheckGroupRun(Session session, GameBot[] members, GameBot leader)
    {
        if (!BotGroupRetreat.Enabled || session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve || leader == null ||
            AutonomousRealmRaid.GetView(session.Group) != null)
            return;
        long now = GameLoop.GameLoopTime;
        GroupRunState state = GroupRuns.GetOrCreateValue(session);
        if (state.RetreatUntil != 0 && now >= state.RetreatUntil)
            FinishGroupRun(session, members, state);
        if (now < state.NextCheckTick || now < state.CooldownUntil)
            return;
        state.NextCheckTick = now + BotGroupRetreat.CheckMilliseconds;
        if (!members.Any(member => member.IsAlive && (member.InCombat || member.IsAttacking)))
            return;

        // read the members' board without rebuilding it under Sync (no area search inside the global lock); none fresh
        // means nobody is fighting a board's worth, and the next check comes in 2 s
        GroupFightBoard board = BotBrain.PeekFightBoard(leader);
        if (board == null || board.MonstersOnGroup == 0)
            return;
        int livingTanks = 0, livingHealers = 0, bestHealerPower = 0, lowestTank = 100;
        bool mezzerReady = false;
        foreach (GameBot member in members)
        {
            if (!member.IsAlive || member.CurrentRegionID != leader.CurrentRegionID)
                continue;
            if (BotPartyRoles.IsTank(member))
            {
                livingTanks++;
                lowestTank = Math.Min(lowestTank, member.HealthPercent);
            }
            if (member.CharacterClass != null && BotPartyRoles.IsHealingClass((eCharacterClass)member.CharacterClass.ID) &&
                member.CharacterClass.ID != (int)eCharacterClass.Paladin)
            {
                livingHealers++;
                bestHealerPower = Math.Max(bestHealerPower, member.ManaPercent);
            }
            mezzerReady |= MezReady(member);
        }
        if (!BotGroupRetreat.ShouldRun(board.UncontrolledOnGroup, board.MonstersOnGroup, livingTanks, livingHealers,
                bestHealerPower, lowestTank, mezzerReady))
            return;

        Vector3 fight = new(leader.X, leader.Y, leader.Z);
        Vector3 monsters = fight;
        int counted = 0;
        Vector3 sum = Vector3.Zero;
        foreach (GameNPC npc in board.NpcsOnGroup)
            if (npc is not GameBot && npc.Realm == eRealm.None)
            {
                sum += new Vector3(npc.X, npc.Y, npc.Z);
                counted++;
            }
        if (counted > 0)
            monsters = sum / counted;
        Vector3? home = session.RendezvousRegion == leader.CurrentRegionID ? session.Rendezvous : null;
        // M6 / M11: the realm's bindstone close by, else the camp's safe spot, else back to the rendezvous or away
        Vector3 point = BotGroupRetreat.SafePoint(fight,
            BotGroupRetreat.NearestBind(leader.CurrentRegionID, leader.X, leader.Y, leader.Realm),
            AutonomousWorldBotController.TryCampSafeSpot(session.Camp?.Id, leader.CurrentRegionID, out Vector3 campSafe)
                ? campSafe : null,
            monsters, BotGroupRetreat.RetreatPoint(fight, home, monsters));
        string reason = $"uncontrolled={board.UncontrolledOnGroup} monsters={board.MonstersOnGroup} tanks={livingTanks} " +
                        $"healers={livingHealers} healerPower={bestHealerPower} tankHealth={lowestTank} mezzer={mezzerReady}";
        BotGroupRetreat.Publish(session.Group, new BotGroupRetreat.Order
        {
            Region = leader.CurrentRegionID,
            Point = point,
            Until = now + BotGroupRetreat.RetreatMilliseconds,
            Reason = reason,
        });
        state.RetreatUntil = now + BotGroupRetreat.RetreatMilliseconds;
        state.CooldownUntil = now + BotGroupRetreat.CooldownMilliseconds;
        Log.Warn($"AUTONOMOUS_GROUP_RUN group={session.Id} phase=\"{session.Phase}\" camp=\"{session.Camp?.Id}\" {reason} " +
                 $"to={leader.CurrentRegionID}:{(int)point.X},{(int)point.Y} home={home.HasValue} " +
                 $"members=\"{string.Join(",", members.Select(member => $"{member.Name}:{member.HealthPercent}/{member.ManaPercent}:alive={member.IsAlive}"))}\"");
    }

    /// <summary>Caller holds Sync. After a RUN the next camp is easier (the wipe penalty) and the party rests first.</summary>
    private static void FinishGroupRun(Session session, GameBot[] members, GroupRunState state)
    {
        state.RetreatUntil = 0;
        BotGroupRetreat.Clear(session.Group);
        if (members.Length > 0)
            session.WipePenalty = AutonomousGroupTargetPolicy.PenaltyAfterWipe((int)Math.Round(members.Average(member => member.Level)),
                members.Length, session.WipePenalty, session.Camp?.TargetLevel ?? 0);
        session.RecoveringBetweenPulls = true;
        Log.Info($"AUTONOMOUS_GROUP_RUN_ENDED group={session.Id} wipePenalty={session.WipePenalty} " +
                 $"alive={members.Count(member => member.IsAlive)}/{members.Length}");
    }

    /// <summary>A member with power and a mesmerize ready to cast (crowd control left for the adds).</summary>
    private static bool MezReady(GameBot member)
    {
        if (member?.Spells is not { } spells || member.IsIncapacitated)
            return false;
        try
        {
            foreach (Spell spell in spells)
                if (spell?.SpellType is eSpellType.Mesmerize or eSpellType.Mez && spell.Target == eSpellTarget.ENEMY &&
                    spell.Level <= member.Level && member.GetSkillDisabledDuration(spell) <= 0 &&
                    member.Mana >= member.PowerCost(spell))
                    return true;
        }
        catch (InvalidOperationException) { } // spell list changed mid-scan (respec)
        return false;
    }
}
