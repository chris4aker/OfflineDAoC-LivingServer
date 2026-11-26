using System;
using System.Collections.Generic;
using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #568 C2 and C3 (owner decision 2026-11-26), ported from True165 a95ef07c4 (bots/BotBrain.ControlSpells.cs): crowd control
    /// on the bot's own target like a veteran player (BotControlSpells). Against a monster no mez, root or nearsight, a
    /// snare only on a runner, a stun only to interrupt a cast or peel the monster off a non-tank (a long-reuse stun only
    /// with three or more monsters on the group). Read in CanCastOffensiveSpell and CheckInstantOffensiveSpells, which only
    /// the bot's own-target rotation uses; adds are controlled by TryPveAddControl, TryBardPveAddMez, TryShamanPveAddRoot and
    /// the raid CC posts, which cast through CheckOffensiveSpells and are not changed. Player-like enemies keep their PvP
    /// control. Gamebots and companions alike.
    /// </summary>
    public partial class BotBrain
    {
        private bool ControlAllowedOnTarget(Spell spell, GameLiving target)
        {
            BotControlSpells.ControlKind kind = BotControlSpells.Kind(spell);
            if (kind == BotControlSpells.ControlKind.None)
                return true;
            if (target == null || !target.IsAlive)
                return false;
            bool playerLike = BotPvpCrowdControl.PlayerLike(target);
            return BotControlSpells.AllowsOnOwnTarget(kind, playerLike,
                !playerLike && kind == BotControlSpells.ControlKind.Snare && IsRunningMonster(target),
                !playerLike && kind == BotControlSpells.ControlKind.Stun && StunWorthwhileOn(target, spell));
        }

        /// <summary>Moving, low on health and out of melee with whoever it fights.</summary>
        private static bool IsRunningMonster(GameLiving target) =>
            BotControlSpells.IsRunner(target.IsMoving, target.HealthPercent, InMeleeWithVictim(target));

        private static bool InMeleeWithVictim(GameLiving target) =>
            target.TargetObject is GameLiving victim && victim.IsAlive &&
            target.IsWithinRadius(victim, Math.Max(target.attackComponent?.AttackRange ?? 0, 150) + 50);

        private bool StunWorthwhileOn(GameLiving target, Spell spell)
        {
            bool casting = target.IsCasting;
            bool peel = !casting && PeelsNonTank(target);
            if (!casting && !peel)
                return false;
            bool longReuse = BotControlSpells.IsLongReuse(spell);
            // three or more adds besides the monster being killed
            bool emergency = longReuse && MonstersOnGroupCached() > EmergencyAddCount;
            return BotControlSpells.StunWorthwhile(casting, peel, longReuse, emergency);
        }

        /// <summary>The monster is on a groupmate who is no tank (or on this bot when a tank is there to take it back).</summary>
        private bool PeelsNonTank(GameLiving target)
        {
            if (target.TargetObject is not GameLiving victim || !victim.IsAlive || victim is not (GamePlayer or GameBot) ||
                BotHealTriage165.IsTank(victim))
                return false;
            if (victim == Body)
                return GroupHasLivingTank();
            return Body.Group?.IsInTheGroup(victim) == true;
        }

        /// <summary>A living tank (a tank-class bot or player, not this bot) in the bot's group and region.</summary>
        private bool GroupHasLivingTank()
        {
            Group group = Body.Group;
            if (group == null || group.MemberCount < 2)
                return false;
            foreach (GameLiving member in group.GetMembersInTheGroup())
                if (member != Body && member.IsAlive && member.CurrentRegion == Body.CurrentRegion &&
                    BotHealTriage165.IsTank(member))
                    return true;
            return false;
        }

        /// <summary>Monsters fighting this bot or its group (up to EmergencyAddCount + 1), read from the group's fight board.</summary>
        private int MonstersOnGroupCached() => Math.Min(FightBoard()?.MonstersOnGroup ?? 0, EmergencyAddCount + 1);

        /// <summary>A groupmate or this bot's pet holds the bot's target in melee.</summary>
        private bool GroupHoldsTarget(GameLiving target)
        {
            if (target?.TargetObject is not GameLiving holder || holder == Body || !holder.IsAlive ||
                !target.IsWithinRadius(holder, 300))
                return false;
            return holder == Body.ControlledBrain?.Body || Body.Group?.IsInTheGroup(holder) == true;
        }

        /// <summary>
        /// C3: an Eldritch (or anyone) with a held target casts its plain nuke instead of a snare-nuke of similar power; the
        /// snare is wasted on a monster the tank or pet holds.
        /// </summary>
        private void PreferPlainNukeOverSnareNuke(List<Spell> spells, GameLiving target)
        {
            if (spells.Count < 2 || !spells.Exists(spell => spell.SpellType == eSpellType.DamageSpeedDecrease) ||
                BotPvpCrowdControl.PlayerLike(target) || !GroupHoldsTarget(target))
                return;
            Spell plain = null;
            foreach (Spell spell in spells)
                if (spell.SpellType is eSpellType.DirectDamage or eSpellType.Bolt && spell.Radius <= 0 &&
                    (plain == null || spell.Damage > plain.Damage))
                    plain = spell;
            if (plain != null)
                spells.RemoveAll(spell => BotControlSpells.PrefersPlainNuke(spell, plain, true));
        }
    }
}
