using DOL.GS.Effects;
using DOL.GS.PacketHandler;

namespace DOL.GS.Spells
{
	[SpellHandler(eSpellType.DoomHammer)]
	public class DoomHammerSpellHandler : DirectDamageSpellHandler
	{
		public override bool CheckBeginCast(GameLiving selectedTarget)
		{
			if(Caster.IsDisarmed)
			{
				MessageToCaster("You are disarmed and can't use this spell!",eChatType.CT_SpellResisted);
				return false;
			}
			return base.CheckBeginCast(selectedTarget);
		}
		public override double CalculateDamageBase(GameLiving target) { return Spell.Damage; }
		public override void ApplyEffectOnTarget(GameLiving target)
		{
			// The Thane throws his hammer for direct damage and is disarmed for the spell's duration. The old code passed
			// the Thane to the base method, so it put the effect on (or damaged) the Thane himself.
			// Port of rambodolphin13/DividedRealms bebdbb71.
			OnDirectEffect(target);
			Caster.attackComponent.StopAttack();
			Caster.DisarmedTime = Caster.CurrentRegion.Time + Spell.Duration;

			foreach (GamePlayer visPlayer in Caster.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
				visPlayer.Out.SendCombatAnimation(Caster, target, 0x0000, 0x0000, 408, 0, 0x00, target.HealthPercent);

			if (Spell.ResurrectMana > 0)
			{
				foreach (GamePlayer visPlayer in target.GetPlayersInRadius(WorldMgr.VISIBILITY_DISTANCE))
					visPlayer.Out.SendSpellEffectAnimation(Caster, target, (ushort) Spell.ResurrectMana, 0, false, 0x01);
			}

		}
		public override int OnEffectExpires(GameSpellEffect effect, bool noMessages)
		{
			//Caster.IsDisarmed=false;
			return base.OnEffectExpires(effect,noMessages);
		}
		public DoomHammerSpellHandler(GameLiving caster,Spell spell,SpellLine line) : base(caster,spell,line) {}
	}
}
