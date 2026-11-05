using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// The rules of the original OpenDAoC mechanics restored behind <see cref="EpicEncounterMechanics"/> switches (all off by
    /// default). Values read from OpenDAoC-Core 3513bad (2026-11-04). Kept apart from the scripts so they can be tested
    /// without a world. Gamebots count as the class they are, like players: only the originals' target picking stays
    /// player-only (see docs/encounters/EPIC-ENCOUNTERS.md).
    /// </summary>
    public static class EpicEncounterRestores
    {
        /// <summary>The attacker's class id: a player's or a gamebot's, or 0 for anything else (pets included).</summary>
        public static int ClassId(GameObject source) => source switch
        {
            GamePlayer player => player.CharacterClass?.ID ?? 0,
            GameBot bot => bot.CharacterClass?.ID ?? 0,
            _ => 0
        };

        public static eObjectType WeaponType(GameObject source) =>
            source is GameLiving living && living.ActiveWeapon != null ? (eObjectType)living.ActiveWeapon.Object_Type : eObjectType.GenericItem;

        /// <summary>Funus: clerics, and mercenaries with a short bow, armsmen and infiltrators with a crossbow, scouts with a
        /// longbow. Everyone else, pets included, heals him to full.</summary>
        public static bool FunusAccepts(int classId, eObjectType weapon) => (eCharacterClass)classId switch
        {
            eCharacterClass.Cleric => true,
            eCharacterClass.Mercenary => weapon == eObjectType.Fired,
            eCharacterClass.Armsman or eCharacterClass.Infiltrator => weapon == eObjectType.Crossbow,
            eCharacterClass.Scout => weapon == eObjectType.Longbow,
            _ => false
        };

        public enum Earthmender { Guardian, Magical, Natural, Shadowy }

        /// <summary>Which classes hurt each Earthmender (pets always do). Animists and valewalkers hurt all four.</summary>
        public static bool EarthmenderAccepts(Earthmender kind, int classId)
        {
            var c = (eCharacterClass)classId;
            if (c is eCharacterClass.Animist or eCharacterClass.Valewalker) return true;
            return kind switch
            {
                Earthmender.Guardian => c is eCharacterClass.Blademaster or eCharacterClass.Hero or eCharacterClass.Champion,
                Earthmender.Magical => c is eCharacterClass.Eldritch or eCharacterClass.Enchanter or eCharacterClass.Mentalist,
                Earthmender.Natural => c is eCharacterClass.Bard or eCharacterClass.Druid or eCharacterClass.Warden,
                Earthmender.Shadowy => c is eCharacterClass.Nightshade or eCharacterClass.Ranger,
                _ => false
            };
        }

        /// <summary>Aroon's Scaiths in the order they must die; the n-th one's death opens the n-th damage type on Aroon.</summary>
        public static readonly eDamageType[] AroonOrder =
        [
            eDamageType.Slash, eDamageType.Thrust, eDamageType.Crush, eDamageType.Body, eDamageType.Cold,
            eDamageType.Energy, eDamageType.Heat, eDamageType.Matter, eDamageType.Spirit
        ];

        /// <summary>How many Scaiths have died in order (0-9), from Aroon's flags.</summary>
        public static int AroonOpened(bool[] deadInOrder)
        {
            int n = 0;
            while (n < deadInOrder.Length && deadInOrder[n]) n++;
            return n;
        }

        /// <summary>Aroon takes a damage type only once its Scaith (and every one before it) is dead.</summary>
        public static bool AroonAccepts(eDamageType type, bool[] deadInOrder)
        {
            int opened = AroonOpened(deadInOrder);
            for (int i = 0; i < opened; i++)
                if (AroonOrder[i] == type) return true;
            return false;
        }

        /// <summary>The n-th Scaith (0 = Corp, the slash one) can be hurt once every Scaith before it is dead.</summary>
        public static bool ScaithAccepts(int index, bool[] deadInOrder) => AroonOpened(deadInOrder) >= index;

        /// <summary>Kvasir: cold heals him a fifth of his health.</summary>
        public static bool KvasirHealedBy(eDamageType type) => type == eDamageType.Cold;

        public static void TellImmune(GameObject source, string text)
        {
            GamePlayer player = source as GamePlayer ?? (source as GameSummonedPet)?.Owner as GamePlayer;
            player?.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_ChatWindow);
        }
    }
}
