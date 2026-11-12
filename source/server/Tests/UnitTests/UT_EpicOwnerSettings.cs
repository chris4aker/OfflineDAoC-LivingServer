using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The owner's epic-encounter settings (1.2, epic wave 2 merge): every restored mechanic is its own server property
    /// (npc / epic_*), read live, so the owner turns one off or on without a rebuild; the dragons' messengers have their own;
    /// any boss's per-encounter switch can be set from the database (epic_encounter_switches). The defaults here are the ones
    /// listed in PR #102 under "Defaults to confirm with the owner".
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicOwnerSettings
    {
        private Dictionary<FieldInfo, object> _saved;

        private static IEnumerable<(FieldInfo Field, ServerPropertyAttribute Attr)> EpicProperties() =>
            typeof(Properties).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Select(f => (f, f.GetCustomAttribute<ServerPropertyAttribute>()))
                .Where(p => p.Item2 != null && p.Item2.Key.StartsWith("epic_", StringComparison.Ordinal));

        private static bool Switch(string name) => EpicEncounterMechanics.Global(name);

        private static void SetSwitch(string name, bool value) =>
            typeof(EpicEncounterMechanics).GetProperty(name, BindingFlags.Public | BindingFlags.Static)!.SetValue(null, value);

        [SetUp]
        public void SetUp()
        {
            _saved = EpicProperties().ToDictionary(p => p.Field, p => p.Field.GetValue(null));
            EpicEncounterMechanics.ResetPerEncounter();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var (field, value) in _saved) field.SetValue(null, value);
            EpicEncounterMechanics.ResetPerEncounter();
        }

        [Test]
        public void EveryRestoredMechanicIsItsOwnServerProperty()
        {
            var bools = EpicProperties().Where(p => p.Field.FieldType == typeof(bool)).ToArray();
            var owners = new Dictionary<string, FieldInfo>();
            foreach (string name in EpicEncounterMechanics.RestoreSwitches)
            {
                bool before = Switch(name);
                SetSwitch(name, !before);
                FieldInfo[] changed = bools.Where(p => (bool)p.Field.GetValue(null) != (bool)_saved[p.Field]).Select(p => p.Field).ToArray();
                Assert.That(changed, Has.Length.EqualTo(1), name + ": the switch writes exactly one server property");
                owners[name] = changed[0];
                // The database value (the field) is what the game reads, live.
                changed[0].SetValue(null, before);
                Assert.That(Switch(name), Is.EqualTo(before), name + ": read live from its property");
            }
            Assert.That(owners.Values.Distinct().Count(), Is.EqualTo(EpicEncounterMechanics.RestoreSwitches.Length), "one property each");
            // A fresh database starts from the code's value.
            var mine = owners.Values.Concat([typeof(Properties).GetField(nameof(Properties.EPIC_DRAGON_MESSENGERS)),
                typeof(Properties).GetField(nameof(Properties.EPIC_ENCOUNTER_SWITCHES))]).ToHashSet();
            foreach (var (field, attr) in EpicProperties().Where(p => mine.Contains(p.Field)))
                Assert.That(attr.DefaultValue, Is.EqualTo(_saved[field]), attr.Key + ": the property's default is the field's");
        }

        private static object DefaultOf(string key) => EpicProperties().Single(p => p.Attr.Key == key).Attr.DefaultValue;

        [Test]
        public void TheDefaultsToConfirmWithTheOwner()
        {
            Assert.That(DefaultOf("epic_full_fire_patches"), Is.EqualTo(false));
            Assert.That(DefaultOf("epic_govannon_full_health"), Is.EqualTo(false));
            Assert.That(DefaultOf("epic_funus_weapon_puzzle"), Is.EqualTo(false));
            Assert.That(DefaultOf("epic_dragon_messengers"), Is.EqualTo(true));
            Assert.That(DefaultOf("epic_encounter_switches"), Is.EqualTo(""));
            // Off until a played check: the owner's 2026-10-19 fire-patch nerf stays, Govannon stays at 200,000, and Funus's puzzle
            // stays off (owner 2026-11-08: off until the bots clear it with no heals; the last container run still had 6).
            Assert.That(EpicEncounterMechanics.AllowFullFirePatches, Is.False);
            Assert.That(EpicEncounterMechanics.AllowFunusWeaponPuzzle, Is.False);
            Assert.That(EpicEncounterMechanics.AllowGovannonFullHealth, Is.False);
            // On as their engineers set them.
            foreach (string on in new[]
                     {
                         nameof(EpicEncounterMechanics.AllowTuscarTwinHeal),
                         nameof(EpicEncounterMechanics.AllowKvasirColdHeal), nameof(EpicEncounterMechanics.AllowEarthmenderMutualHeal),
                         nameof(EpicEncounterMechanics.AllowBeathaHealsXaga), nameof(EpicEncounterMechanics.AllowSacristanPatrol),
                     })
                Assert.That(Switch(on), Is.True, on);
            Assert.That(Properties.EPIC_DRAGON_MESSENGERS, Is.True);
            Assert.That(Properties.EPIC_ENCOUNTER_SWITCHES, Is.Empty);
        }

        [Test]
        public void GovannonsHealthFollowsItsSettingWithoutARebuild()
        {
            var govannon = (GrandSummonerGovannon)RuntimeHelpers.GetUninitializedObject(typeof(GrandSummonerGovannon));
            Properties.EPIC_GOVANNON_FULL_HEALTH = false;
            Assert.That(govannon.MaxHealth, Is.EqualTo(200_000));
            Properties.EPIC_GOVANNON_FULL_HEALTH = true;
            Assert.That(govannon.MaxHealth, Is.EqualTo(300_000));
        }

        [Test]
        public void TheMessengersSettingTurnsThemOffAtEveryDragon()
        {
            string messengers = nameof(EpicEncounterMechanics.AllowDragonMessengers);
            string[] dragons = ["AlbGolestandt", "MidGjalpinulva", "HibCuuldurach"];
            Assert.That(dragons.All(d => EpicEncounterMechanics.On(messengers, d)), Is.True, "they ship on");
            Properties.EPIC_DRAGON_MESSENGERS = false;
            Assert.That(dragons.Any(d => EpicEncounterMechanics.On(messengers, d)), Is.False);
            Assert.That(EpicEncounterMechanics.AllowDragonMessengersFor("AlbGolestandt"), Is.False);
            Properties.EPIC_DRAGON_MESSENGERS = true;
            Assert.That(EpicEncounterMechanics.AllowDragonMessengersFor("AlbGolestandt"), Is.True);
        }

        [Test]
        public void AnyBossSwitchCanBeSetFromTheDatabase()
        {
            var parsed = EpicEncounterMechanics.ParseOwnerSwitches(
                "AllowBotDisplacement@Orylle=False; AllowPositionPuzzle@Easmarach = on, NoSuchSwitch@Legion=true, AllowDisplacement@Legion=maybe, junk");
            Assert.That(parsed, Is.EquivalentTo(new Dictionary<(string, string), bool>
            {
                [("AllowBotDisplacement", "Orylle")] = false,
                [("AllowPositionPuzzle", "Easmarach")] = true,
            }), "unknown switches and malformed values are skipped");
            Assert.That(EpicEncounterMechanics.ParseOwnerSwitches(""), Is.Empty);

            string bots = nameof(EpicEncounterMechanics.AllowBotDisplacement), puzzle = nameof(EpicEncounterMechanics.AllowPositionPuzzle);
            Assert.That(EpicEncounterMechanics.On(bots, "Orylle"), Is.True, "ships on");
            Properties.EPIC_ENCOUNTER_SWITCHES = "AllowBotDisplacement@Orylle=False; AllowPositionPuzzle@Olcasgean=0; AllowDisplacement@Conservator=True";
            Assert.That(EpicEncounterMechanics.On(bots, "Orylle"), Is.False, "the owner's value beats what ships");
            Assert.That(EpicEncounterMechanics.On(puzzle, "Olcasgean"), Is.False);
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowDisplacement), "Conservator"), Is.True,
                "and turns on what stays off");
            Assert.That(EpicEncounterMechanics.On(bots, "Jailer"), Is.True, "others as shipped");
            Properties.EPIC_ENCOUNTER_SWITCHES = "";
            Assert.That(EpicEncounterMechanics.On(bots, "Orylle"), Is.True, "cleared: what ships again");
            Assert.That(EpicEncounterMechanics.On(nameof(EpicEncounterMechanics.AllowDisplacement), "Conservator"), Is.False);
        }
    }
}
