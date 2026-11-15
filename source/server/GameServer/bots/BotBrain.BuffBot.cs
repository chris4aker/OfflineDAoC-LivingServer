using DOL.GS;

namespace DOL.AI.Brain
{
    /// <summary>The brain side of a No Lifer's buffbot (NoLiferBuffBots has the rules and the turn).</summary>
    public partial class BotBrain
    {
        /// <summary>One buff on its main, through the bot's ordinary cast (power, concentration and line-of-sight checks).</summary>
        internal bool CastBuffOn(GameLiving target, Spell spell)
        {
            if (target == null || spell == null || Body.IsCasting)
                return false;
            GameObject previous = Body.TargetObject;
            Body.TargetObject = target;
            bool cast = Body.CastSpell(spell, m_mobSpellLine);
            if (!cast)
                Body.TargetObject = previous;
            return cast;
        }
    }
}
