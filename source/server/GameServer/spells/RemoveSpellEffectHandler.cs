using System;
using System.Collections.Generic;
using DOL.GS.Effects;

namespace DOL.GS.Spells
{
	/// <summary>
	/// Base class for all spells that remove effects
	/// </summary>
	public abstract class RemoveSpellEffectHandler : SpellHandler
	{
		/// <summary>
		/// Stores spell effect type that will be removed
		/// RR4: now its a list of effects to remove
		/// </summary>
		protected List<string> m_spellTypesToRemove = null;

		/// <summary>
		/// Spell effect type that will be removed
		/// RR4: now its a list of effects to remove
		/// </summary>
		public virtual List<string> SpellTypesToRemove
		{
			get { return m_spellTypesToRemove; }
		}

		/// <summary>
		/// called when spell effect has to be started and applied to targets
		/// </summary>
		/// <param name="target"></param>
		public override void FinishSpellCast(GameLiving target)
		{
			m_caster.Mana -= PowerCost(target);
			base.FinishSpellCast(target);
		}

		public override void OnEffectPulse(GameSpellEffect effect)
		{
			base.OnEffectPulse(effect);
			OnDirectEffect(effect.Owner);
		}

		public override void OnDirectEffect(GameLiving target)
		{
			base.OnDirectEffect(target);
			if (target == null || !target.IsAlive)
				return;

			// RR4: we remove all the effects
			foreach (string toRemove in SpellTypesToRemove)
			{
				if (!TryGetEffectType(toRemove, out eEffect effectType))
					continue;

				// Every instance, not just the first (OpenDAoC-Core b9b32cf03).
				foreach (ECSGameEffect effect in target.effectListComponent.GetEffects(effectType))
					effect?.End();
			}
			SendEffectAnimation(target, 0, false, 1);
		}

		/// <summary>The effect a removal name stands for (pure). A name with no effect behind it (Cure Nearsight's "Silence")
		/// removes nothing: it used to parse to the enum's first value and cure Bladeturn (OpenDAoC-Core b9b32cf03).</summary>
		public static bool TryGetEffectType(string toRemove, out eEffect effectType)
		{
			if (toRemove == "Mesmerize")
			{
				effectType = eEffect.Mez;
				return true;
			}

			return Enum.TryParse(toRemove, out effectType) && !int.TryParse(toRemove, out _);
		}

		// constructor
		protected RemoveSpellEffectHandler(GameLiving caster, Spell spell, SpellLine line) : base(caster, spell, line) { }
	}
}
