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
using DOL.Language;

namespace DOL.GS
{
    /// <summary>
    /// Tradeskills that earn the crafter a title.
    /// <author>Aredhel</author>
    /// Crafting 2.0 (WP3): from Baptiste Marie (bm01), upstream OpenDAoC 7b0df1b12 and ad789ee4d, GPL: no Legendary
    /// Grandmaster rung (post-era; 1000 and above is Legendary) and no Basic Crafting title.
    /// </summary>
    public abstract class AbstractProfession : AbstractCraftingSkill
    {
        #region Title

        protected abstract String Profession { get; }

        public static String GetTitleFormat(int skillLevel)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(skillLevel);

            return (skillLevel / 100) switch
            {
                0 => "CraftersTitle.Helper",
                1 => "CraftersTitle.JuniorApprentice",
                2 => "CraftersTitle.Apprentice",
                3 => "CraftersTitle.Neophyte",
                4 => "CraftersTitle.Assistant",
                5 => "CraftersTitle.Junior",
                6 => "CraftersTitle.Journeyman",
                7 => "CraftersTitle.Senior",
                8 => "CraftersTitle.Master",
                9 => "CraftersTitle.Grandmaster",
                _ => "CraftersTitle.Legendary"
            };
        }

        public virtual String GetTitle(GamePlayer player, int skillLevel)
        {
            // Upstream ad789ee4d overrides this in BasicCrafting; the check lives here so BasicCrafting.cs (WP1's) is untouched.
            if (eSkill == eCraftingSkill.BasicCrafting)
                return string.Empty;

            string profession = LanguageMgr.TryTranslateOrDefault(player, "!Profession!", Profession);
            try
            {
                return LanguageMgr.TryTranslateOrDefault(player, "!None {0}!", GetTitleFormat(skillLevel), profession);
            }
            catch
            {
                return "<you may want to check your Crafting.txt language file>";
            }
        }

        #endregion
    }
}
