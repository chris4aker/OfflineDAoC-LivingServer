using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;
using Damage = DOL.GS.AmalgamateParthananDamage;

namespace DOL.UnitTests
{
    /// <summary>
    /// The Parthanan farms after the port of OpenDAoC-Core 0837f95e1 (#565 step 4b): the farm cycle per controller, the
    /// release world's PackageIDs, and the boss's damage rule, old (kept, the default) and upstream's (owner switch).
    /// </summary>
    [TestFixture]
    public sealed class UT_ParthananFarm
    {
        private static ParthananFarmConfig Config(string farm) => ParthananFarmDefinitions.GetByFarmId(farm);

        private static GameNPC Npc() => (GameNPC)RuntimeHelpers.GetUninitializedObject(typeof(GameNPC));

        private static ParthananFarmState State(int threshold = 3) =>
            new(Config("ParthananFarmLoughDerg"), controllerBody: null, random: (_, _) => threshold);

        // The release world (0.37 world checked on #565): 3 controller rows with these PackageIDs, trash rows with these.
        [TestCase("ParthananFarmConnacht", "ParthananConnacht", "ParthananBossConnacht")]
        [TestCase("ParthananFarmLoughDerg", "ParthananLoughDerg", "ParthananBossLoughDerg")]
        [TestCase("ParthananFarmLoughGur", "ParthananLoughGur", "ParthananBossLoughGur")]
        public void EachControllerRowNamesItsFarm(string farm, string trash, string boss)
        {
            ParthananFarmConfig config = Config(farm);
            Assert.Multiple(() =>
            {
                Assert.That(config, Is.Not.Null);
                Assert.That(config.TrashPackageId, Is.EqualTo(trash));
                Assert.That(config.BossPackageId, Is.EqualTo(boss));
                Assert.That(Config(farm.ToLowerInvariant()), Is.SameAs(config), "matched without case");
                Assert.That((config.MinKillsToSacrifice, config.MaxKillsToSacrifice, config.MinionsRequiredForSacrifice), Is.EqualTo((60, 120, 5)));
            });
        }

        [Test]
        public void AnUnknownControllerIsNoFarm()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Config(null), Is.Null);
                Assert.That(Config("ParthananFarm1"), Is.Null);
            });
        }

        [Test]
        public void KillsLeadToTheSacrificeAndTheBossRisesOnce()
        {
            ParthananFarmState state = State(threshold: 3);
            GameNPC[] trash = Enumerable.Range(0, 7).Select(_ => Npc()).ToArray();

            state.OnTrashDeath(trash[0]);
            state.OnTrashDeath(trash[1]);
            Assert.That(state.Phase, Is.EqualTo(ParthananFarmPhase.Farming));
            state.OnTrashDeath(trash[2]);
            Assert.Multiple(() =>
            {
                Assert.That(state.Phase, Is.EqualTo(ParthananFarmPhase.Sacrificing));
                Assert.That(state.BossIsImmuneToDamage, Is.True);
                Assert.That(state.TrashIsVisible, Is.True);
            });

            Assert.That(state.AddGatheringMinions(trash.Skip(3).Take(4)), Is.False, "4 of the 5 needed");
            Assert.That(state.ShouldMarchAndDie(trash[3]), Is.False);
            Assert.That(state.AddGatheringMinions(new[] { Npc() }.Concat(trash.Skip(3).Take(4))), Is.True, "5: the boss rises");
            Assert.That(state.AddGatheringMinions(trash.Skip(3).Take(4)), Is.False, "only once per cycle");
            Assert.That(state.ShouldMarchAndDie(trash[3]), Is.True);
            Assert.That(state.ShouldMarchAndDie(trash[0]), Is.False, "a dead one never gathered");
        }

        [Test]
        public void TheBossFightsWhenTheLastMinionHasDiedAndItsDeathStartsAgain()
        {
            ParthananFarmState state = State(threshold: 1);
            state.OnTrashDeath(Npc());
            List<GameNPC> minions = Enumerable.Range(0, 5).Select(_ => Npc()).ToList();
            state.AddGatheringMinions(minions);

            foreach (GameNPC minion in minions.Take(4))
                state.OnTrashDeath(minion);
            Assert.That(state.Phase, Is.EqualTo(ParthananFarmPhase.Sacrificing));
            state.OnTrashDeath(minions[4]);
            Assert.Multiple(() =>
            {
                Assert.That(state.Phase, Is.EqualTo(ParthananFarmPhase.BossActive));
                Assert.That(state.BossIsImmuneToDamage, Is.False);
                Assert.That(state.TrashIsVisible, Is.False, "the trash hides while the boss fights");
                Assert.That(state.ShouldMarchAndDie(minions[0]), Is.False);
            });

            state.OnTrashDeath(Npc());
            Assert.That(state.KillCount, Is.EqualTo(1), "kills while the boss fights don't count (1 is the kill that started the sacrifice)");
            state.OnBossDeath();
            Assert.Multiple(() =>
            {
                Assert.That(state.Phase, Is.EqualTo(ParthananFarmPhase.Farming));
                Assert.That(state.TrashIsVisible, Is.True);
                Assert.That(state.BossIsImmuneToDamage, Is.False);
                Assert.That(state.KillCount, Is.Zero);
            });
        }

        [Test]
        public void KillsAtOnceFromManyThreadsAreAllCounted()
        {
            ParthananFarmState state = State(threshold: 10_000);
            System.Threading.Tasks.Parallel.For(0, 2_000, _ => state.OnTrashDeath(Npc()));
            Assert.That(state.KillCount, Is.EqualTo(2_000));
        }

        // The boss's damage rule. Old (the default, kept so the boss isn't weakened) and upstream's (owner switch).

        private static readonly Damage.Attacker Sword = new(Damage.AttackerKind.Player, HasWeapon: true, PlayerBow: false, NpcRanged: false);
        private static readonly Damage.Attacker Bow = new(Damage.AttackerKind.Player, HasWeapon: true, PlayerBow: true, NpcRanged: false);
        private static readonly Damage.Attacker Unarmed = new(Damage.AttackerKind.Player, HasWeapon: false, PlayerBow: false, NpcRanged: false);
        private static readonly Damage.Attacker UnarmedPet = new(Damage.AttackerKind.Pet, HasWeapon: false, PlayerBow: false, NpcRanged: false);
        private static readonly Damage.Attacker NpcMelee = new(Damage.AttackerKind.Npc, HasWeapon: true, PlayerBow: false, NpcRanged: false);
        private static readonly Damage.Attacker NpcArcher = new(Damage.AttackerKind.Npc, HasWeapon: true, PlayerBow: false, NpcRanged: true);
        private static readonly Damage.Attacker NotLiving = new(Damage.AttackerKind.NotLiving, false, false, false);

        [Test]
        public void TheDefaultIsTheOldRule()
        {
            Assert.That(Damage.UseUpstreamRule, Is.False, "never weaken a boss without the owner (#565)");
        }

        [Test]
        public void TheOldRuleResistsMagicArcheryAndNpcRangedAndIgnoresUnarmed()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Damage.For(Sword, eDamageType.Slash, false), Is.EqualTo(Damage.Outcome.Damage));
                Assert.That(Damage.For(Sword, eDamageType.Heat, false), Is.EqualTo(Damage.Outcome.Immune));
                Assert.That(Damage.For(Bow, eDamageType.Thrust, false), Is.EqualTo(Damage.Outcome.Immune), "archery");
                Assert.That(Damage.For(Unarmed, eDamageType.Crush, false), Is.EqualTo(Damage.Outcome.Ignored));
                Assert.That(Damage.For(UnarmedPet, eDamageType.Crush, false), Is.EqualTo(Damage.Outcome.Ignored));
                Assert.That(Damage.For(NpcMelee, eDamageType.Slash, false), Is.EqualTo(Damage.Outcome.Damage), "a gamebot's melee");
                Assert.That(Damage.For(NpcMelee, eDamageType.Spirit, false), Is.EqualTo(Damage.Outcome.Immune), "a gamebot's spell");
                Assert.That(Damage.For(NpcArcher, eDamageType.Thrust, false), Is.EqualTo(Damage.Outcome.Immune), "a gamebot's bow");
                Assert.That(Damage.For(NotLiving, eDamageType.Natural, false), Is.EqualTo(Damage.Outcome.Ignored));
            });
        }

        [Test]
        public void UpstreamsRuleResistsMagicOnly()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Damage.For(Sword, eDamageType.Slash, true), Is.EqualTo(Damage.Outcome.Damage));
                Assert.That(Damage.For(Sword, eDamageType.Heat, true), Is.EqualTo(Damage.Outcome.Immune));
                Assert.That(Damage.For(Bow, eDamageType.Thrust, true), Is.EqualTo(Damage.Outcome.Damage), "archery hurts it");
                Assert.That(Damage.For(Unarmed, eDamageType.Crush, true), Is.EqualTo(Damage.Outcome.Damage), "so does an unarmed blow");
                Assert.That(Damage.For(NpcArcher, eDamageType.Thrust, true), Is.EqualTo(Damage.Outcome.Damage));
                Assert.That(Damage.For(NpcMelee, eDamageType.Matter, true), Is.EqualTo(Damage.Outcome.Immune));
                Assert.That(Damage.For(NotLiving, eDamageType.Natural, true), Is.EqualTo(Damage.Outcome.Damage));
            });
        }

        [TestCase(eDamageType.Body, true)]
        [TestCase(eDamageType.Cold, true)]
        [TestCase(eDamageType.Energy, true)]
        [TestCase(eDamageType.Heat, true)]
        [TestCase(eDamageType.Matter, true)]
        [TestCase(eDamageType.Spirit, true)]
        [TestCase(eDamageType.Slash, false)]
        [TestCase(eDamageType.Crush, false)]
        [TestCase(eDamageType.Thrust, false)]
        public void TheMagicKinds(eDamageType type, bool magic) => Assert.That(Damage.IsMagic(type), Is.EqualTo(magic));
    }
}
