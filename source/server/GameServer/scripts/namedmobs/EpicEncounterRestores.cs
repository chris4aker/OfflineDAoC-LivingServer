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

        /// <summary>With AllowGalladoriaBridgeSpot a raider must stand within 350 of the bridge spot to start the last room; a
        /// raid bot that has stood within 1,500 of it this long starts it all the same (the raid is never held there).</summary>
        public const long BridgeFallbackMilliseconds = 180_000;

        /// <summary>The bridge fallback is due (pure): a raid bot has been on the bridge since <paramref name="since"/> (0: none is).</summary>
        public static bool BridgeFallbackDue(long since, long now) => since > 0 && now - since >= BridgeFallbackMilliseconds;

        /// <summary>Kvasir: cold heals him a fifth of his health.</summary>
        public static bool KvasirHealedBy(eDamageType type) => type == eDamageType.Cold;

        private static readonly Logging.Logger Log = Logging.LoggerManager.Create(typeof(EpicEncounterRestores));

        /// <summary>1.2 play test: a hit that healed Funus to full is in the server log (who, what kind, class, weapon).</summary>
        public static void NoteFunusHeal(GameObject source, GameLiving funus)
        {
            if (!Log.IsInfoEnabled) return;
            string kind = source switch { GamePlayer => "player", GameBot => "bot", GameSummonedPet => "pet", _ => source?.GetType().Name };
            GameLiving living = source as GameLiving;
            Log.Info($"EPIC_FUNUS_HEALED source=\"{source?.Name}\" kind={kind} class={(eCharacterClass)ClassId(source)} weapon={WeaponType(source)} " +
                     $"healthBefore={funus?.HealthPercent}% slot={living?.ActiveWeaponSlot} target=\"{(living?.TargetObject as GameObject)?.Name}\"");
            // Play test duo5: scouts with a longbow still healed him with their melee weapon after the bow-keep fix. Once per
            // raider every ten minutes: the call path that hit him, to find the switch that put the melee weapon in hand.
            if (source != null && Log.IsInfoEnabled)
            {
                long now = GameLoop.GameLoopTime;
                lock (HealTraces)
                {
                    if (HealTraces.TryGetValue(source.ObjectID, out long last) && now - last < 600_000) return;
                    if (HealTraces.Count > 512) HealTraces.Clear();
                    HealTraces[source.ObjectID] = now;
                }
                Log.Info($"EPIC_FUNUS_HEALED_TRACE source=\"{source.Name}\" stack={System.Environment.StackTrace.Replace(System.Environment.NewLine, " | ")}");
            }
        }

        private static readonly System.Collections.Generic.Dictionary<int, long> HealTraces = new();

        /// <summary>Play test diagnostic: a bot that may fight <paramref name="target"/> only at range was switched to melee.</summary>
        public static void NoteMeleeSwitch(GameObject bot, GameLiving target)
        {
            if (bot == null || !Log.IsInfoEnabled) return;
            long now = GameLoop.GameLoopTime;
            lock (HealTraces)
            {
                int key = -bot.ObjectID;
                if (HealTraces.TryGetValue(key, out long last) && now - last < 600_000) return;
                HealTraces[key] = now;
            }
            Log.Info($"EPIC_MELEE_SWITCH_TRACE bot=\"{bot.Name}\" target=\"{target.Name}\" stack={System.Environment.StackTrace.Replace(System.Environment.NewLine, " | ")}");
        }

        public static void TellImmune(GameObject source, string text)
        {
            GamePlayer player = source as GamePlayer ?? (source as GameSummonedPet)?.Owner as GamePlayer;
            player?.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_ChatWindow);
        }
    }
}
