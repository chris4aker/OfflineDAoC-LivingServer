/*
 * DAWN OF LIGHT - The first free open source DAoC server emulator
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU General Public License
 * as published by the Free Software Foundation; either version 2
 * of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program; if not, write to the Free Software
 * Foundation, Inc., 59 Temple Place - Suite 330, Boston, MA  02111-1307, USA.
 *
 */
using System;

namespace DOL.GS.PropertyCalc
{
	/// <summary>
	/// The Melee Speed bonus percent calculator
	///
	/// BuffBonusCategory1 is used for buffs
	/// BuffBonusCategory2 unused
	/// BuffBonusCategory3 is used for debuff
	/// BuffBonusCategory4 unused
	/// BuffBonusMultCategory1 unused
	/// </summary>
	[PropertyCalculator(eProperty.MeleeSpeed)]
	public class MeleeSpeedPercentCalculator : PropertyCalculator
	{
		/// <summary>Remaining swing time in per-mille (1000 = unchanged, 900 = 10% faster). Callers multiply by 0.001.</summary>
		public override int CalcValue(GameLiving living, eProperty property)
		{
			// OpenDAoC-Core dda75528c, 99e18b6b2, 4c05debd6, after https://camelotherald.fandom.com/wiki/Melee_Damage:
			// haste buffs (minus debuffs), then Mastery of Arms, then the ToA item bonus (capped at 10%) are three
			// multiplicative layers, so Mastery of Arms is worth less on top of a big haste. The result is per-mille to
			// keep a decimal place. NPC buffs are no longer halved (unsourced); that also gives gamebots, which are
			// GameNPCs, the player rule. A necromancer pet uses its shade's ability and item bonuses.
			GameLiving livingToCheck = living is NecromancerPet { Owner: GameLiving petOwner } && PlayerDefenseFormula.UsesPlayerDefense(petOwner)
				? petOwner
				: living;

			return Calculate(
				living.BaseBuffBonusCategory[property] + living.SpecBuffBonusCategory[property] - Math.Abs(living.DebuffCategory[property]),
				livingToCheck.AbilityBonus[property],
				livingToCheck.ItemBonus[property]);
		}

		public static int Calculate(int buffBonus, int abilityBonus, int itemBonus)
		{
			itemBonus = Math.Min(10, itemBonus); // http://www.camelotherald.com/more/1325.shtml
			double result = buffBonus;
			result += (1 - result * 0.01) * abilityBonus;
			result += (1 - result * 0.01) * itemBonus;
			return Math.Max(1, (int) ((100 - result) * 10));
		}
	}
}
