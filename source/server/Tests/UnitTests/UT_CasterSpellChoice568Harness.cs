using System.Collections.Generic;
using System.Reflection;
using DOL.AI.Brain;
using DOL.Database;
using DOL.GS.Tests.Rehearsal;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #568 brain-level tests (the #727 review): C1, C2 and C5 run through the real BotBrain on real gamebots in the fight
    /// harness (RvrFightWorld, the UT_BattlegroundFights617Harness pattern), a plain region and neutral monsters. The C1
    /// rule is the same BotBrain code for companions and gamebots; the harness builds gamebots. Tests only.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_CasterSpellChoice568Harness
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const ushort RegionId = 568, ZoneId = 568;
        private const int X = 30_000, Y = 30_000;

        [OneTimeSetUp]
        public void Logging() => DOL.Logging.LoggerManager.InitializeWithExplicitLibrary(null, DOL.Logging.LogLibrary.None);

        private static (RvrFightWorld World, Region Region) Field()
        {
            var world = new RvrFightWorld(568, RvrFightRehearsal.Repeatable);
            return (world, world.AddRegion(RegionId, ZoneId, "#568 flat field (rehearsal)"));
        }

        private static GameBot Bot(RvrFightWorld world, Region region, eCharacterClass characterClass, string name, int x, int y)
        {
            GameBot bot = world.NewBot(eRealm.Albion, characterClass, 30, name, "GroupPve");
            Assert.That(world.Place(bot, region, x, y), Is.True, name + " placed");
            return bot;
        }

        private static GameNPC Monster(RvrFightWorld world, Region region, string name, int x, int y)
        {
            var npc = new GameNPC { Name = name, Level = 30, Realm = eRealm.None };
            Assert.That(world.Place(npc, region, x, y), Is.True, name + " placed");
            return npc;
        }

        private static object Call(BotBrain brain, string method, params object[] args) =>
            typeof(BotBrain).GetMethod(method, Hidden | BindingFlags.DeclaredOnly)!.Invoke(brain, args);

        private static Spell Make(int id, string type, double damage = 0, double value = 0, double cast = 2.5, int power = 10) =>
            new(new DbSpell
            {
                SpellID = id, Name = type + id, Description = type, Type = type, Target = "Enemy", Range = 1500, Power = power,
                CastTime = cast, Duration = type == "DirectDamage" ? 0 : 30, Damage = damage, Value = value,
                DamageType = (int)eDamageType.Heat,
            }, 30);

        /// <summary>The spell the bot is casting or has queued, if any.</summary>
        private static Spell Started(GameBot bot)
        {
            if (bot.castingComponent?.SpellHandler?.Spell is Spell casting)
                return casting;
            if (bot.castingComponent?.QueuedSpellHandler?.Spell is Spell queued)
                return queued;
            MethodInfo peek = bot.castingComponent?.GetType().GetMethod("TryPeekPendingSpell", Hidden | BindingFlags.Public);
            if (peek == null)
                return null;
            object[] args = { null };
            return peek.Invoke(bot.castingComponent, args) is true ? args[0] as Spell : null;
        }

        [Test]
        public void C1_TheOnlyMezzedAddOnItsListIsLeftWhileAGroupmateFightsAnother()
        {
            var (world, region) = Field();
            using (world)
            {
                GameBot subject = Bot(world, region, eCharacterClass.Armsman, "Subject", X, Y);
                GameBot mate = Bot(world, region, eCharacterClass.Mercenary, "Mate", X + 100, Y);
                world.Form(new List<GameLiving> { subject, mate });
                GameNPC mezzed = Monster(world, region, "mezzed add", X + 300, Y);
                GameNPC other = Monster(world, region, "other monster", X + 600, Y);
                world.Tick(); // the zones file what was placed
                var brain = (BotBrain)subject.Brain;
                brain.ForceAddToAggroList(mezzed, 500);
                Assert.That(brain.GetBaseAggroAmount(mezzed), Is.GreaterThan(0), "the mezzed add is on its own list");
                mezzed.IsMezzed = true;
                try
                {
                    // nobody fights anything else: the only one left is broken to go on (unchanged)
                    Assert.That(Call(brain, "CalculateNextAttackTarget"), Is.SameAs(mezzed), "control: alone it is taken");

                    // the groupmate fights the other monster: the mezzed add is left alone
                    mate.TargetObject = other;
                    mate.LastAttackTickPvE = world.Now;
                    Assert.That(Call(brain, "CalculateNextAttackTarget"), Is.Not.SameAs(mezzed),
                        "a groupmate's fight counts as the fight");
                    Assert.That(brain.GetBaseAggroAmount(mezzed), Is.GreaterThan(0), "it stays on the list for later");
                }
                finally
                {
                    mezzed.IsMezzed = false;
                }
            }
        }

        [Test]
        public void C2_TheInstantMezIsNotCastOnItsOwnMonster()
        {
            var (world, region) = Field();
            using (world)
            {
                GameBot caster = Bot(world, region, eCharacterClass.Sorcerer, "Sorc", X, Y);
                GameNPC monster = Monster(world, region, "its monster", X + 500, Y);
                world.Tick();
                var brain = (BotBrain)caster.Brain;
                Spell instantMez = Make(956_801, "Mesmerize", cast: 0, power: 5);
                caster.TargetObject = monster;
                Assert.Multiple(() =>
                {
                    Assert.That(Call(brain, "ControlAllowedOnTarget", instantMez, monster), Is.False, "no mez on the kill target");
                    Assert.That(Call(brain, "CheckInstantOffensiveSpells", instantMez), Is.False, "the instant path refuses it");
                    Assert.That(Started(caster), Is.Null, "nothing was cast");
                    Assert.That(caster.IsCasting, Is.False);
                });
            }
        }

        [Test]
        public void C5_AHeldCasterCastsOnlyADebuffOnAMonsterItsTankHoldsAt90()
        {
            var (world, region) = Field();
            using (world)
            {
                GameBot wizard = Bot(world, region, eCharacterClass.Wizard, "Wiz", X, Y);
                GameBot tank = Bot(world, region, eCharacterClass.Armsman, "Tank", X + 800, Y);
                world.Form(new List<GameLiving> { tank, wizard });
                GameNPC monster = Monster(world, region, "held monster", X + 900, Y);
                world.Tick();
                Spell nuke = Make(956_802, "DirectDamage", damage: 120, power: 10);
                Spell debuff = Make(956_803, "StrengthDebuff", value: 20, power: 10);
                wizard.Spells = new List<Spell> { nuke, debuff };
                var brain = (BotBrain)wizard.Brain;
                // under 30% power: no opening debuff, so only the hold keeps the nuke back
                wizard.Mana = wizard.MaxMana / 4;
                Assert.That(wizard.Mana, Is.GreaterThanOrEqualTo(wizard.PowerCost(nuke)), "power for either spell");
                monster.TargetObject = tank;
                monster.Health = monster.MaxHealth * 90 / 100;
                wizard.TargetObject = monster;
                Assert.That(Call(brain, "HoldDamageForTank"), Is.True, "the tank holds it at 90%: hold");

                brain.CheckSpells(BotBrain.eCheckSpellType.Offensive);
                Spell held = Started(wizard);
                Assert.That(held, Is.Not.Null, "it casts something while it waits");
                Assert.That(held, Is.SameAs(debuff), "the debuff, never the nuke");
            }
        }
    }
}
