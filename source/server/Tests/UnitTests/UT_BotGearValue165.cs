using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.PropertyCalc;
using DOL.GS.PlayerClass;
using NUnit.Framework;

namespace DOL.UnitTests.Gameserver;

/// <summary>Gear value by class and up to each bonus's cap (veteran-bots review M9 / economy G3, BotGearValue165).
/// From shadowofze (1edbcf5), via True165, GPL-3.0; ported unchanged by Crafting 2.0 WP6 (our FitValue has no WeaponSpecBonus
/// or WastedFocus term, which these tests never exercise).</summary>
[TestFixture, NonParallelizable]
public class UT_BotGearValue165
{
    private GameServer _oldServer;
    private PetTestLanguageScope _language;
    private sealed class EmptyServer : GameServer
    {
        protected override IObjectDatabase DataBaseImpl =>
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
    }

    [SetUp]
    public void SetUp()
    {
        _oldServer = GameServer.Instance;
        GameServer.LoadTestDouble((EmptyServer)RuntimeHelpers.GetUninitializedObject(typeof(EmptyServer)));
        _language = new PetTestLanguageScope();
    }

    [TearDown]
    public void TearDown()
    {
        _language.Dispose();
        GameServer.LoadTestDouble(_oldServer);
    }

    private sealed class Bot : GameBot
    {
        private Bot() : base((OfflineWorldBotRecord)null) { }
        public byte TestLevel;
        public override byte Level { get => TestLevel; set => TestLevel = value; }
        public void SetClass(ICharacterClass cls) => m_characterClass = cls;
    }

    private static Bot MakeBot(ICharacterClass cls, int level, bool itemBonuses = true)
    {
        var bot = (Bot)RuntimeHelpers.GetUninitializedObject(typeof(Bot));
        bot.TestLevel = (byte)level;
        bot.SetClass(cls);
        bot.Inventory = new BotInventory();
        if (itemBonuses)
            typeof(GameLiving).GetField("<ItemBonus>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(bot, new PropertyIndexer());
        return bot;
    }

    private static DbInventoryItem Ring(int level, eProperty type, int value) => new()
    {
        Level = level, Quality = 90, Object_Type = (int)eObjectType.Magical, Item_Type = (int)eInventorySlot.LeftRing,
        Bonus1Type = (int)type, Bonus1 = value,
    };

    [Test]
    public void AWarriorValuesStrengthAboveAStatItDoesNotUse()
    {
        var warrior = MakeBot(new ClassWarrior(), 20);
        int strength = AutonomousBotEconomy.FitValue(warrior, Ring(20, eProperty.Strength, 6), eInventorySlot.LeftRing);
        int piety = AutonomousBotEconomy.FitValue(warrior, Ring(20, eProperty.Piety, 6), eInventorySlot.LeftRing);
        Assert.That(strength, Is.GreaterThan(piety));
        Assert.That(AutonomousBotEconomy.EquipmentValue(Ring(20, eProperty.Strength, 6)),
            Is.EqualTo(AutonomousBotEconomy.EquipmentValue(Ring(20, eProperty.Piety, 6))), "EquipmentValue itself is unchanged");
    }

    [Test]
    public void AStatAlreadyAtItsCapAddsNothing()
    {
        var warrior = MakeBot(new ClassWarrior(), 10);   // stat cap 15
        warrior.ItemBonus[eProperty.Strength] = 15;       // from other worn pieces
        int capped = AutonomousBotEconomy.FitValue(warrior, Ring(10, eProperty.Strength, 5), eInventorySlot.LeftRing);
        int plain = AutonomousBotEconomy.FitValue(warrior, Ring(10, eProperty.Strength, 0), eInventorySlot.LeftRing);
        Assert.That(capped, Is.EqualTo(plain));

        warrior.ItemBonus[eProperty.Strength] = 12;       // 3 points of room left
        int partly = AutonomousBotEconomy.FitValue(warrior, Ring(10, eProperty.Strength, 5), eInventorySlot.LeftRing);
        Assert.That(partly - plain, Is.EqualTo(3 * 4), "only the 3 points under the cap count, at the primary stat's weight");
    }

    [Test]
    public void TheReplacedPieceIsTakenOutBeforeTheCapIsMeasured()
    {
        var warrior = MakeBot(new ClassWarrior(), 10);
        DbInventoryItem worn = Ring(10, eProperty.Strength, 15);
        warrior.Inventory.AddItem(eInventorySlot.LeftRing, worn);
        warrior.ItemBonus[eProperty.Strength] = 15;       // all from the worn ring
        int replacement = AutonomousBotEconomy.FitValue(warrior, Ring(10, eProperty.Strength, 10), eInventorySlot.LeftRing);
        int plain = AutonomousBotEconomy.FitValue(warrior, Ring(10, eProperty.Strength, 0), eInventorySlot.LeftRing);
        Assert.That(replacement - plain, Is.EqualTo(10 * 4));
    }

    [Test]
    public void AnItemFarAboveTheBotWinsNothingOnLevelOrDamageAlone()
    {
        var warrior = MakeBot(new ClassWarrior(), 10);
        DbInventoryItem levelTen = new() { Level = 10, Quality = 90, DPS_AF = BotGearValue165.WeaponDpsCap(10),
            Object_Type = (int)eObjectType.Sword, Item_Type = (int)eInventorySlot.RightHandWeapon, SPD_ABS = 37,
            Condition = 50000, MaxCondition = 50000 };
        DbInventoryItem levelThirty = new() { Level = 30, Quality = 90, DPS_AF = BotGearValue165.WeaponDpsCap(30),
            Object_Type = (int)eObjectType.Sword, Item_Type = (int)eInventorySlot.RightHandWeapon, SPD_ABS = 37,
            Condition = 50000, MaxCondition = 50000 };
        Assert.That(AutonomousBotEconomy.EquipmentValue(levelThirty), Is.GreaterThan(AutonomousBotEconomy.EquipmentValue(levelTen)));
        Assert.That(AutonomousBotEconomy.FitValue(warrior, levelThirty, eInventorySlot.RightHandWeapon),
            Is.EqualTo(AutonomousBotEconomy.FitValue(warrior, levelTen, eInventorySlot.RightHandWeapon)));
    }

    [Test]
    public void ArmourFactorStopsAtTheCapClothAtTheLevel()
    {
        Assert.That(BotGearValue165.ArmorFactorCap(20, eObjectType.Cloth), Is.EqualTo(20));
        Assert.That(BotGearValue165.ArmorFactorCap(20, eObjectType.Chain), Is.EqualTo(40));
        Assert.That(BotGearValue165.Cap(20, eProperty.Strength), Is.EqualTo(30));
        Assert.That(BotGearValue165.Cap(20, eProperty.MaxHealth), Is.EqualTo(80));
        Assert.That(BotGearValue165.Cap(20, eProperty.Resist_Body), Is.EqualTo(11));
        Assert.That(BotGearValue165.Cap(20, eProperty.Skill_Slashing), Is.EqualTo(5));
    }

    [Test]
    public void ACasterValuesItsAcuityAndAWarriorDoesNotValuePower()
    {
        var cleric = MakeBot(new ClassCleric(), 20);
        var warrior = MakeBot(new ClassWarrior(), 20);
        Assert.That(BotGearValue165.Weight(cleric, eProperty.Piety), Is.EqualTo(4));
        Assert.That(BotGearValue165.Weight(warrior, eProperty.Piety), Is.EqualTo(1));
        Assert.That(BotGearValue165.Weight(warrior, eProperty.MaxMana), Is.Zero);
        Assert.That(BotGearValue165.Weight(warrior, eProperty.Strength), Is.EqualTo(4));
        Assert.That(BotGearValue165.Weight(warrior, eProperty.Constitution), Is.EqualTo(3));
        Assert.That(BotGearValue165.Weight(warrior, eProperty.MaxHealth), Is.EqualTo(1));
    }

    [Test]
    public void ABotWithoutAClassOrItemBonusesKeepsTodaysValueForBonuses()
    {
        var unknown = MakeBot(null, 20);
        DbInventoryItem ring = Ring(20, eProperty.Piety, 6);
        Assert.That(AutonomousBotEconomy.FitValue(unknown, ring, eInventorySlot.LeftRing),
            Is.EqualTo(AutonomousBotEconomy.EquipmentValue(ring)));

        var noTable = MakeBot(new ClassWarrior(), 20, itemBonuses: false);
        Assert.That(AutonomousBotEconomy.FitValue(noTable, Ring(20, eProperty.Strength, 6), eInventorySlot.LeftRing),
            Is.EqualTo(AutonomousBotEconomy.EquipmentValue(Ring(20, eProperty.Strength, 6)) + 6),
            "without an item-bonus table a primary stat counts in full at its weight (4 instead of 3)");
    }
}
