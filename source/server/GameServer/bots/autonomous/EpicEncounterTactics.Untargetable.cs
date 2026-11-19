using System;

namespace DOL.GS
{
    public static partial class EpicEncounterTactics
    {
        /// <summary>
        /// Bosses that turn untargetable (CANTTARGET) for the end of the fight and are meant to be finished by spells. Lich Lord
        /// Sanguis (Caer Sidi, LordSanguis.cs): at 5% health "becomes almost untouchable in his last act of agony" and stays
        /// untargetable until the fight resets; area spells and running damage over time still hit him (SpellHandler picks
        /// area targets with no CANTTARGET check), so players finish him with area damage. Bots never fight an untargetable
        /// NPC (BotBrain.IsUntargetable), so a raid stopped on his last 5% while he went on hitting it, and the route dropped him
        /// from its targets (2026-11-06, read from the script; not seen in a run). Now, while such a boss is in its fight,
        /// spellcasting bots (offensive casters) keep him as a target and go on casting (nukes, DoTs, area spells); melee and
        /// archers still can't, as players can't, and keep the adds off the casters.
        /// </summary>
        public static readonly string[] FinishedBySpells = ["LichLordSanguis"];

        /// <summary>The rule (pure): a listed boss, alive, untargetable and still in its fight.</summary>
        public static bool FinishableUntargetable(string type, bool alive, GameNPC.eFlags flags, bool inFight) =>
            alive && inFight && (flags & GameNPC.eFlags.CANTTARGET) != 0 && Array.IndexOf(FinishedBySpells, type) >= 0;

        public static bool FinishableUntargetable(GameLiving living) => living is GameNPC npc &&
            FinishableUntargetable(npc.GetType().Name, npc.IsAlive, npc.Flags, npc.InCombatInLast(EpicEncounterMechanics.ResetQuietMilliseconds));

        /// <summary>This bot may go on fighting <paramref name="target"/> although it is untargetable: an offensive caster
        /// against a boss finished by spells.</summary>
        public static bool MayFinishUntargetable(GameBot bot, GameLiving target) =>
            bot != null && BotSpellPower.IsOffensiveCaster(bot) && FinishableUntargetable(target);
    }
}
