using DOL.GS.Spells;

namespace DOL.GS.Effects
{
    public class AtlasOF_VanishECSEffect : ECSGameAbilityEffect
    {
        public new SpellHandler SpellHandler;
        public AtlasOF_VanishECSEffect(in ECSGameEffectInitParams initParams)
            : base(initParams)
        {
            EffectType = eEffect.Vanish;
        }

        public override ushort Icon { get { return 4280; } }
        public override string Name { get { return "Vanish"; } }
        public override bool HasPositiveEffect { get { return true; } }

        public override void OnStartEffect()
        {
            // #531 step 2c-2: a gamebot hides by its own rule (GameBot.VanishHide); OwnerPlayer is null for it
            if (Owner is GameBot bot)
                bot.VanishHide();
            else
                OwnerPlayer.Stealth(true);
            base.OnStartEffect();
        }
    }
}
