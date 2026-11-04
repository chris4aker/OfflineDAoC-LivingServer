using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// Water breathing and swim speed (fork, owner 2026-10-25): what WaterBreathingSpellHandler's old-system effect did,
    /// as an ECS effect so it runs: the owner breathes under water and swims Spell.Value percent faster.
    /// </summary>
    public class WaterBreathingECSGameEffect : ECSGameSpellEffect
    {
        public WaterBreathingECSGameEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            EffectType = eEffect.WaterBreathing;
        }

        public override void OnStartEffect()
        {
            if (Owner is GamePlayer player)
            {
                player.CanBreathUnderWater = true;
                player.BaseBuffBonusCategory[eProperty.WaterSpeed] += (int) SpellHandler.Spell.Value;
                player.Out.SendUpdateMaxSpeed();
            }

            OnEffectStartsMsg(true, false, false);
        }

        public override void OnStopEffect()
        {
            if (Owner is GamePlayer player)
            {
                // The Mythirian of Ektaktos keeps water breathing on its own.
                DbInventoryItemCheck(player);
                player.BaseBuffBonusCategory[eProperty.WaterSpeed] -= (int) SpellHandler.Spell.Value;
                player.Out.SendUpdateMaxSpeed();

                if (player.IsDiving && !player.CanBreathUnderWater)
                {
                    player.Out.SendMessage("With a gulp and a gasp you realize that you are unable to breathe underwater any longer!", eChatType.CT_SpellExpires, eChatLoc.CL_SystemWindow);
                }
            }

            OnEffectExpiresMsg(true, false, false);
        }

        private static void DbInventoryItemCheck(GamePlayer player)
        {
            DOL.Database.DbInventoryItem item = player.Inventory.GetItem((eInventorySlot) 37);

            if (item == null || !item.Name.ToLower().Contains("ektaktos"))
            {
                player.CanBreathUnderWater = false;
            }
        }
    }
}
