using System.Collections.Generic;
using System.Linq;

namespace DOL.GS
{
    /// <summary>
    /// #567 B7 (owner, 2026-11-26: "B7, yes"): a Hibernian Bard may fill a bot party's Healer slot when nobody in the
    /// formation pool is a healing class, and heals as that party's healer for as long as it stays in it. The Bard's heals
    /// are its own Regrowth spells; no spell number changes. Adapted from True165 BardGroupHealer165 (b45daa9, GPL-3.0),
    /// narrowed to a fallback: a Druid or Warden in reach always takes the slot first, the way the off-tank fallback leaves
    /// a tank slot to real tanks (AutonomousBotGroupCoordinator.UseOffTanks). Switch bot_bard_fills_healer_slot, on by
    /// default. BotPartyRoles.CanFill and the class-wide healer flag stay as they were, so a solo Bard, or a Bard in a
    /// buffer or attacker slot, plays exactly as before.
    /// </summary>
    public static class BardHealerSlot
    {
        public const string PropertyKey = "bot_bard_fills_healer_slot";

        public static bool On => ServerProperties.Properties.BOT_BARD_FILLS_HEALER_SLOT;

        /// <summary>Pure: Bards may take Healer slots in this formation: the switch is on, a Bard is among the classes and
        /// none of them is a healing class.</summary>
        public static bool Allowed(eCharacterClass leader, IReadOnlyCollection<eCharacterClass> pool, bool enabled) =>
            enabled && pool != null &&
            (leader == eCharacterClass.Bard || pool.Contains(eCharacterClass.Bard)) &&
            !BotPartyRoles.IsHealingClass(leader) && !pool.Any(BotPartyRoles.IsHealingClass);

        public static bool Allowed(eCharacterClass leader, IReadOnlyCollection<eCharacterClass> pool) =>
            Allowed(leader, pool, On);

        /// <summary>Pure: the extra slot rule when <see cref="Allowed(eCharacterClass, IReadOnlyCollection{eCharacterClass}, bool)"/> holds.</summary>
        public static bool FillsSlot(eCharacterClass characterClass, BotPveGroupRole role, bool bardHealers) =>
            bardHealers && role == BotPveGroupRole.Healer && characterClass == eCharacterClass.Bard;

        /// <summary>Pure: the Bard's healer duty (BotBrain.IsHealer). It holds the slot of the party it is in, and the
        /// switch is on; read every time, so turning the switch off ends Bard healing in live parties at once.</summary>
        public static bool Holds(Group slotGroup, Group currentGroup, bool enabled) =>
            enabled && slotGroup != null && ReferenceEquals(slotGroup, currentGroup);

        public static bool Holds(Group slotGroup, Group currentGroup) => Holds(slotGroup, currentGroup, On);

        /// <summary>
        /// Gives each Bard holding a Healer role in this party the healer duty, and takes it from a Bard that holds another
        /// role here. The duty ends by itself when the Bard leaves the party (Holds compares the group).
        /// </summary>
        public static void Mark(string partyId, Group group, IEnumerable<GameBot> members,
            IReadOnlyDictionary<long, BotPveGroupRole> roles, System.Func<GameBot, long> memberKey,
            DOL.Logging.Logger log)
        {
            if (group == null || members == null || roles == null)
                return;
            foreach (GameBot member in members)
            {
                if (member?.CharacterClass?.ID != (int)eCharacterClass.Bard || member.Brain is not DOL.AI.Brain.BotBrain brain)
                    continue;
                bool healerSlot = roles.TryGetValue(memberKey(member), out BotPveGroupRole role) && role == BotPveGroupRole.Healer;
                if (healerSlot && !ReferenceEquals(brain.HealerSlotGroup, group))
                {
                    brain.HealerSlotGroup = group;
                    if (log?.IsInfoEnabled == true)
                        log.Info($"AUTONOMOUS_BARD_HEALER_SLOT group={partyId} bot=\"{member.Name}\" id={member.DatabaseID} " +
                                 $"level={member.Level} size={group.MemberCount}");
                }
                else if (!healerSlot && ReferenceEquals(brain.HealerSlotGroup, group))
                    brain.HealerSlotGroup = null;
            }
        }
    }
}
