using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>
    /// #568 C8, from True165 19eef1d / 1ed3b24 (zomboden/OfflineDAoC-True165, GPL-3.0, BotBrain.SpellOptions165.cs): a hired
    /// caster whose player chose "Attack spells: None - buffs only" (a Runemaster's "afk personal bladeturn, no nukes") puts
    /// a used-up self bladeturn back up in the fight too, whenever nothing has hit it for 3 s. The ordinary upkeep waits for
    /// the fight's end. Hired companions only: a gamebot's AI Options always read 0 (Automatic).
    /// </summary>
    public partial class BotBrain
    {
        private bool TryNoAttackSpellsBladeturn() =>
            BotBody != null && CompanionSpellOptions165.KeepsBladeturnInFight(BotBody) && TryRestoreSelfBladeturn(true);
    }
}
