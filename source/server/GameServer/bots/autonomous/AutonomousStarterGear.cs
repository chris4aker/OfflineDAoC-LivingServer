using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using DOL.Database;

namespace DOL.GS
{
    /// <summary>
    /// Starter gear for an autonomous gamebot that arrives already trained above level 1 (Living Server 1.2; owner
    /// 2026-11-15, "Arrive with starter gear": "The launcher gives each bot common merchant-quality gear for its level, as if
    /// it had leveled normally"). Owner's PC, build 0026378, Thidranki 13:45-14:00: 183 of 410 bot deaths there had
    /// armorPieces=0, and storms of about 20 "+ Lv.20" bots (levels 20-24, no armour) all died before the keep gate.
    ///
    /// Once per bot, on the login that first sees it: each empty armour slot its class can wear, its build's empty weapon
    /// slot(s) and, for a sword-and-board build, an empty shield slot get the best plain item its own realm's merchants sell
    /// for coin at its level. The items are real merchant templates (no new templates, nothing magical, dropped or crafted),
    /// legal for the bot by the same rules its vendor upgrades use (AutonomousBotEconomy.CanUseEquipment), and free: its coin
    /// is not touched. Nothing it already wears is moved or replaced. Gamebots that start at level 1 still start without
    /// armour and earn it (owner's rule); companions (/spawn) and real players never reach this code.
    /// </summary>
    public static class AutonomousStarterGear
    {
        /// <summary>The token appended to offline_world_bots.SerializedAbilities once the gear was given.</summary>
        public const string MarkerKey = "starter-gear";

        /// <summary>The six armour slots, in the order the gear is chosen and logged.</summary>
        public static readonly eInventorySlot[] ArmorSlots =
        [
            eInventorySlot.TorsoArmor, eInventorySlot.LegsArmor, eInventorySlot.ArmsArmor,
            eInventorySlot.HeadArmor, eInventorySlot.HandsArmor, eInventorySlot.FeetArmor,
        ];

        /// <summary>One slot the gear may fill, and the item kind that slot takes.</summary>
        public readonly record struct SlotWant(eInventorySlot Slot, eObjectType WeaponType, bool Shield)
        {
            public bool IsArmor => WeaponType == 0 && !Shield;
        }

        public sealed record Result(bool Given, IReadOnlyList<(eInventorySlot Slot, DbItemTemplate Item)> Pieces, string Reason);

        /// <summary>
        /// Whether a gamebot gets its starter gear on this login. Pure.
        /// <para>Never twice (<paramref name="alreadyGiven"/>), never at level 1 (gamebots start without armour and earn
        /// it), and never while it wears any armour: gear it found or bought stays as it is.</para>
        /// <para>A bot that arrives trained on this login (<see cref="GameBot.ArrivesTrained"/>: never saved and made above
        /// level 1, i.e. the launcher's "+ Lv.20" or "+ Lv.50") qualifies. So does one saved before this rule existed that
        /// still wears no armour and is bound in its realm's capital: the launcher binds a bot it makes above level 1 there
        /// (and the server never rebinds a gamebot), while a "+ Lv.1" bot is bound in a Classic or Shrouded Isles starting
        /// area. Those are the "+ Lv.20" bots added before this build that have been fighting without armour.</para>
        /// </summary>
        public static bool Qualifies(int level, bool arrivesTrainedNow, bool alreadyGiven, int armorPiecesWorn,
            eRealm realm, int bindRegionId)
        {
            if (alreadyGiven || level <= 1 || armorPiecesWorn > 0)
                return false;
            return arrivesTrainedNow || bindRegionId != 0 && bindRegionId == LauncherCapitalRegion(realm);
        }

        /// <summary>The capital region the launcher starts and binds a bot made above level 1 in
        /// (MainForm.CapitalBotStartingLocation): Camelot, Jordheim, Tir na Nog. Pure.</summary>
        public static int LauncherCapitalRegion(eRealm realm) => realm switch
        {
            eRealm.Albion => 10,
            eRealm.Midgard => 101,
            eRealm.Hibernia => 201,
            _ => -1,
        };

        /// <summary>"…|starter-gear|1" in SerializedAbilities. Pure.</summary>
        public static bool HasMarker(string serializedAbilities)
        {
            string[] parts = serializedAbilities?.Split('|') ?? [];
            for (int i = 0; i + 1 < parts.Length; i++)
                if (parts[i].Equals(MarkerKey, StringComparison.OrdinalIgnoreCase))
                    return parts[i + 1] == "1";
            return false;
        }

        /// <summary>The marker as it is appended to SerializedAbilities ("" when not given). Pure.</summary>
        public static string MarkerSuffix(bool given) => given ? $"|{MarkerKey}|1" : string.Empty;

        /// <summary>Armour pieces in the six armour slots. Pure over the given items.</summary>
        public static int ArmorPiecesWorn(Func<eInventorySlot, DbInventoryItem> itemInSlot) =>
            itemInSlot == null ? 0 : ArmorSlots.Count(slot => itemInSlot(slot) != null);

        /// <summary>
        /// The slots the gear may fill. Pure. Armour first, then the build's primary weapon slot, then its offhand: the
        /// build's offhand weapon (dual wield, left axe) or a shield (a one-handed build whose class has the Shield
        /// ability). A Savage's weapons follow their own policy and keep coming from the server's starter weapon.
        /// Occupied slots are never wanted.
        /// </summary>
        public static IReadOnlyList<SlotWant> PlanSlots(Func<eInventorySlot, bool> occupied, eInventorySlot primarySlot,
            eObjectType primaryType, eObjectType offhandWeaponType, bool shieldBuild, bool savage)
        {
            ArgumentNullException.ThrowIfNull(occupied);
            var wants = new List<SlotWant>();
            foreach (eInventorySlot slot in ArmorSlots)
                if (!occupied(slot))
                    wants.Add(new SlotWant(slot, 0, false));
            if (savage)
                return wants;
            bool primaryFree = primarySlot is eInventorySlot.RightHandWeapon or eInventorySlot.TwoHandWeapon &&
                               primaryType != 0 && !occupied(primarySlot);
            if (primaryFree)
                wants.Add(new SlotWant(primarySlot, primaryType, false));
            bool oneHanded = primarySlot == eInventorySlot.RightHandWeapon;
            if (oneHanded && !occupied(eInventorySlot.LeftHandWeapon))
            {
                if (offhandWeaponType != 0)
                    wants.Add(new SlotWant(eInventorySlot.LeftHandWeapon, offhandWeaponType, false));
                else if (shieldBuild)
                    wants.Add(new SlotWant(eInventorySlot.LeftHandWeapon, 0, true));
            }
            return wants;
        }

        /// <summary>
        /// A plain merchant equipment item: priced in coin, armour, a shield or a melee weapon, and with no magical bonus,
        /// spell, proc, poison or charges. Pure.
        /// </summary>
        public static bool IsCommonMerchantGear(DbItemTemplate item)
        {
            if (item == null || item.Price <= 0)
                return false;
            var type = (eObjectType)item.Object_Type;
            bool equipment = type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor ||
                             type == eObjectType.Shield || BotWeaponStats.IsMeleeWeapon(type);
            if (!equipment)
                return false;
            return item.Bonus == 0 && item.ExtraBonus == 0 &&
                   item.Bonus1 == 0 && item.Bonus2 == 0 && item.Bonus3 == 0 && item.Bonus4 == 0 && item.Bonus5 == 0 &&
                   item.Bonus6 == 0 && item.Bonus7 == 0 && item.Bonus8 == 0 && item.Bonus9 == 0 && item.Bonus10 == 0 &&
                   item.SpellID == 0 && item.SpellID1 == 0 && item.ProcSpellID == 0 && item.ProcSpellID1 == 0 &&
                   item.PoisonSpellID == 0 && item.Charges == 0 && item.Charges1 == 0;
        }

        /// <summary>Whether a template is the kind a wanted slot takes, at or below the bot's level. Pure (the class and
        /// build legality is checked separately on the live bot).</summary>
        public static bool FitsWant(DbItemTemplate item, SlotWant want, int level)
        {
            if (item == null || item.Level > level || item.LevelRequirement > level)
                return false;
            var type = (eObjectType)item.Object_Type;
            if (want.IsArmor)
                return type is >= eObjectType._FirstArmor and <= eObjectType._LastArmor && item.Item_Type == (int)want.Slot;
            if (want.Shield)
                return type == eObjectType.Shield && item.Item_Type == Slot.LEFTHAND;
            if (type != want.WeaponType)
                return false;
            return want.Slot switch
            {
                eInventorySlot.TwoHandWeapon => item.Item_Type == Slot.TWOHAND,
                // Hand=1 is two-handed and Hand=2 marks a left-hand weapon, which the bots keep out of the main hand.
                eInventorySlot.RightHandWeapon => item.Item_Type != Slot.TWOHAND && item.Hand == 0,
                // An offhand weapon must be one a character can hold in its left hand.
                eInventorySlot.LeftHandWeapon => item.Item_Type == Slot.LEFTHAND || item.Hand == 2,
                _ => false,
            };
        }

        /// <summary>The best candidate for a slot: the highest value (AutonomousBotEconomy.EquipmentValue for real items),
        /// ties broken by template id so the choice does not depend on merchant order. Pure.</summary>
        public static T PickBest<T>(IEnumerable<T> candidates, Func<T, int> value, Func<T, string> id) where T : class =>
            candidates?.OrderByDescending(value).ThenBy(id, StringComparer.Ordinal).FirstOrDefault();

        private static readonly ConcurrentDictionary<eRealm, DbItemTemplate[]> CatalogByRealm = new();

        /// <summary>
        /// Every plain equipment template the realm's own coin merchants sell in the Classic and Shrouded Isles world
        /// (housing, frontier teleport-medallion and siege lists excluded), once per realm and server run. An empty
        /// result is not kept, so a login before the merchants are loaded does not hide them for good.
        /// </summary>
        internal static DbItemTemplate[] RealmCatalog(eRealm realm)
        {
            if (CatalogByRealm.TryGetValue(realm, out DbItemTemplate[] cached))
                return cached;
            var templates = new Dictionary<string, DbItemTemplate>(StringComparer.OrdinalIgnoreCase);
            foreach (Region region in WorldMgr.GetAllRegions())
            {
                if (region == null || region.IsHousing ||
                    !AutonomousCapnBryGoalCatalog.IsClassicOrShroudedIslesExpansion(region.Expansion))
                    continue;
                foreach (GameMerchant merchant in region.Merchants)
                {
                    if (merchant?.TradeItems == null || merchant.Realm != realm || !AutonomousBotEconomy.SellsForCoin(merchant))
                        continue;
                    string list = merchant.TradeItems.ItemsListID;
                    if (FrontierSupplyMerchantPolicy.Label(list) != null || AutonomousSiegePolicy.IsSiegeMerchant(realm, list))
                        continue;
                    foreach (DbItemTemplate template in merchant.TradeItems.GetAllItems().Values.OfType<DbItemTemplate>())
                        if (IsCommonMerchantGear(template) && !string.IsNullOrWhiteSpace(template.Id_nb))
                            templates.TryAdd(template.Id_nb, template);
                }
            }
            DbItemTemplate[] catalog = templates.Values.ToArray();
            if (catalog.Length > 0)
                CatalogByRealm[realm] = catalog;
            return catalog;
        }

        /// <summary>
        /// Puts the chosen merchant items straight into the bot's empty slots (free; its coin is not touched). The caller
        /// refreshes item bonuses and saves the inventory. Never moves, replaces or removes an item.
        /// </summary>
        internal static Result Give(GameBot bot, IReadOnlyList<SlotWant> wants)
        {
            if (bot?.Inventory == null || wants == null || wants.Count == 0)
                return new Result(false, [], "nothing to fill");
            DbItemTemplate[] catalog = RealmCatalog(bot.Realm);
            if (catalog.Length == 0)
                return new Result(false, [], "no merchant of its realm found");

            var given = new List<(eInventorySlot, DbItemTemplate)>();
            foreach (SlotWant want in wants)
            {
                if (bot.Inventory.GetItem(want.Slot) != null)
                    continue;
                DbInventoryItem best = PickBest(
                    catalog.Where(template => FitsWant(template, want, bot.Level))
                        .Select(GameInventoryItem.Create)
                        .Where(item => item != null && AutonomousBotEconomy.CanUseEquipment(bot, item) &&
                                       (want.IsArmor || want.Shield ||
                                        BotWeaponStats.FitsConfiguredSlot(bot, item, want.Slot))),
                    AutonomousBotEconomy.EquipmentValue, item => item.Id_nb ?? string.Empty);
                if (best == null)
                    continue;
                if (bot.Inventory.AddItem(want.Slot, best))
                    given.Add((want.Slot, best.Template));
            }
            return given.Count > 0
                ? new Result(true, given, string.Empty)
                : new Result(false, given, "no merchant item fits its class and level");
        }

        /// <summary>"Torso:Name(L20), RightHand:Name(L21), …" for the log line. Pure.</summary>
        public static string Describe(IEnumerable<(eInventorySlot Slot, DbItemTemplate Item)> pieces) =>
            string.Join(", ", (pieces ?? []).Select(piece =>
                $"{SlotName(piece.Slot)}:{(piece.Item?.Name ?? "?").Replace("\"", "'")}(L{piece.Item?.Level ?? 0})"));

        // eInventorySlot has aliases (MinEquipable is RightHandWeapon), so its ToString can name the wrong one.
        public static string SlotName(eInventorySlot slot) => slot switch
        {
            eInventorySlot.TorsoArmor => "Torso",
            eInventorySlot.LegsArmor => "Legs",
            eInventorySlot.ArmsArmor => "Arms",
            eInventorySlot.HeadArmor => "Head",
            eInventorySlot.HandsArmor => "Hands",
            eInventorySlot.FeetArmor => "Feet",
            eInventorySlot.RightHandWeapon => "RightHand",
            eInventorySlot.LeftHandWeapon => "LeftHand",
            eInventorySlot.TwoHandWeapon => "TwoHand",
            _ => ((int)slot).ToString(),
        };
    }
}
