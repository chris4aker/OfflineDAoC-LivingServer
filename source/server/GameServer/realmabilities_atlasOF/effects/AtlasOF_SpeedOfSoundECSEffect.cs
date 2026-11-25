namespace DOL.GS.Effects
{
    public class SpeedOfSoundECSEffect : ECSGameAbilityEffect
    {
        public SpeedOfSoundECSEffect(in ECSGameEffectInitParams initParams) : base(initParams)
        {
            EffectType = eEffect.SpeedOfSound;
        }

        public override ushort Icon => 4249;

        public override string Name => "Speed Of Sound";

        public override bool HasPositiveEffect => true;

        // Speed of Sound is applied by MaxSpeedCalculator, which reads this effect for players and gamebots alike
        // (port of OpenDAoC-Core 324780db2). Snares, roots, Ichor and speed buffs are no longer disabled under it.
        public override void OnStartEffect()
        {
            Owner.OnMaxSpeedChange();
        }

        public override void OnStopEffect()
        {
            Owner.OnMaxSpeedChange();
        }
    }
}
