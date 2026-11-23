using DOL.GS;
using DOL.GS.Keeps;

namespace DOL.AI.Brain
{
    /// <summary>
    /// The brain side of the lord fight's formation (1.2.2, <see cref="RvrLordFormation"/>): how near a keep or tower lord a
    /// gamebot closes before it fights. Melee and tanks close to 280 (the old rule) and their melee chase does the rest; a
    /// caster, archer or healer stops once inside his reach (350 / 250), where the controller keeps it on its formation spot
    /// behind the melee instead of pulling it into the pile at 280. Companions keep the old 280.
    /// </summary>
    public partial class BotBrain
    {
        private int LordHoldRadius(GuardLord lord)
        {
            if (BotBody is not { IsAutonomousWorldBot: true } bot) return (int)RvrLordFormation.MeleeHold;
            return (int)RvrLordFormation.HoldRadius(AutonomousWorldBotController.LordFormationRole(bot), lord.Component?.Keep is GameKeep);
        }
    }
}
