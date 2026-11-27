using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.BotBuffTargetPolicy;

namespace DOL.UnitTests
{
    /// <summary>
    /// #571 step 4 (True165 1ed3b24, "veteran buffing"): the buff decisions of a companion or a bot in a real player's group.
    /// Weapon buffs go to weapon users, the base constitution and dexterity come before base strength and AF, and the
    /// group is buffed by job or by the hired companion's "Buff priority" AI option. Autonomous gamebots outside a player's
    /// group keep the #491 rules (UT_BotBuffTargets491).
    /// </summary>
    [TestFixture]
    public class UT_CompanionBuffs571
    {
        [Test]
        public void OnlyAPlayersGroupWithSomeoneToBuffUsesTheRules() => Assert.Multiple(() =>
        {
            Assert.That(UsesPlayerGroupRules(playerCompanion: true, playerLedGroup: false, groupMembers: 2), Is.True, "a companion");
            Assert.That(UsesPlayerGroupRules(false, true, 3), Is.True, "a bot in a player's group");
            Assert.That(UsesPlayerGroupRules(false, false, 8), Is.False, "an autonomous gamebot group: #491 rules");
            Assert.That(UsesPlayerGroupRules(true, false, 1), Is.False, "alone: nobody else to buff");
            Assert.That(UsesPlayerGroupRules(null), Is.False);
        });

        [Test]
        public void WeaponBuffsGoToWeaponUsersOnly()
        {
            eSpellType[] weapon = [eSpellType.StrengthBuff, eSpellType.CombatSpeedBuff, eSpellType.HasteBuff,
                eSpellType.EnduranceRegenBuff, eSpellType.DamageAdd, eSpellType.OffensiveProc];
            Assert.Multiple(() =>
            {
                foreach (eSpellType type in weapon)
                {
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Character, eCharacterClass.Wizard, eClassType.ListCaster), Is.False, $"{type} on a Wizard");
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Character, eCharacterClass.Cleric, eClassType.Hybrid), Is.False, $"{type} on a Cleric");
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Character, eCharacterClass.Healer, eClassType.Hybrid), Is.False, $"{type} on a Healer");
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Character, eCharacterClass.Armsman, eClassType.PureTank), Is.True, $"{type} on an Armsman");
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Character, eCharacterClass.Shaman, eClassType.Hybrid), Is.True, $"{type} on a Shaman (melee)");
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Character, eCharacterClass.Valewalker, eClassType.ListCaster), Is.True, $"{type} on a Valewalker (scythe)");
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Character, eCharacterClass.Vampiir, eClassType.ListCaster), Is.True, $"{type} on a Vampiir");
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Pet, eCharacterClass.Unknown, eClassType.ListCaster), Is.True, $"{type} on a pet: #491 rules");
                }
                // str/con keeps its constitution for everyone; the other buffs are unchanged
                foreach (eSpellType type in new[] { eSpellType.StrengthConstitutionBuff, eSpellType.DexterityBuff,
                             eSpellType.ConstitutionBuff, eSpellType.BaseArmorFactorBuff, eSpellType.AcuityBuff })
                    Assert.That(PlayerGroupBenefits(type, TargetKind.Character, eCharacterClass.Wizard, eClassType.ListCaster), Is.True, $"{type} on a Wizard");
            });
        }

        [Test]
        public void BuffOrderWhenConcentrationRunsShort()
        {
            eSpellType[] order =
            [
                eSpellType.EnduranceRegenBuff, eSpellType.StrengthConstitutionBuff, eSpellType.ConstitutionBuff,
                eSpellType.AcuityBuff, eSpellType.StrengthBuff, eSpellType.HealthRegenBuff, eSpellType.BaseArmorFactorBuff,
            ];
            Assert.Multiple(() =>
            {
                for (int i = 1; i < order.Length; i++)
                    Assert.That(PlayerGroupPriority(order[i - 1]), Is.GreaterThan(PlayerGroupPriority(order[i])), $"{order[i - 1]} before {order[i]}");
                Assert.That(PlayerGroupPriority(eSpellType.PowerRegenBuff), Is.EqualTo(PlayerGroupPriority(eSpellType.EnduranceRegenBuff)));
                Assert.That(PlayerGroupPriority(eSpellType.DexterityQuicknessBuff), Is.EqualTo(PlayerGroupPriority(eSpellType.StrengthConstitutionBuff)));
                Assert.That(PlayerGroupPriority(eSpellType.SpecArmorFactorBuff), Is.EqualTo(PlayerGroupPriority(eSpellType.StrengthConstitutionBuff)),
                    "spec AF stays a spec buff (#491)");
                Assert.That(PlayerGroupPriority(eSpellType.DexterityBuff), Is.EqualTo(PlayerGroupPriority(eSpellType.ConstitutionBuff)));
                Assert.That(PlayerGroupPriority(eSpellType.DexterityBuff), Is.GreaterThan(PlayerGroupPriority(eSpellType.CombatSpeedBuff)),
                    "base dex and con before haste");
                Assert.That(Priority((GameBot)null, eSpellType.StrengthBuff), Is.EqualTo(Priority(eSpellType.StrengthBuff)),
                    "outside a player's group: the #491 order");
            });
        }

        [Test]
        public void AutomaticBuffsByJob() => Assert.Multiple(() =>
        {
            int Rank(eCharacterClass c, eClassType t, eSpellType type, int choice = BuffPriorityAutomatic) =>
                TargetRank(TargetKind.Character, c, t, type, choice);
            var sc = eSpellType.StrengthConstitutionBuff;
            Assert.That(Rank(eCharacterClass.Armsman, eClassType.PureTank, sc), Is.LessThan(Rank(eCharacterClass.Mercenary, eClassType.PureTank, sc)), "tank, then melee");
            Assert.That(Rank(eCharacterClass.Mercenary, eClassType.PureTank, sc), Is.LessThan(Rank(eCharacterClass.Cleric, eClassType.Hybrid, sc)), "melee, then healers");
            Assert.That(Rank(eCharacterClass.Cleric, eClassType.Hybrid, sc), Is.LessThan(Rank(eCharacterClass.Wizard, eClassType.ListCaster, sc)), "healers, then casters");
            // base dexterity speeds casting: healers and casters first
            var dex = eSpellType.DexterityBuff;
            Assert.That(Rank(eCharacterClass.Wizard, eClassType.ListCaster, dex), Is.LessThan(Rank(eCharacterClass.Armsman, eClassType.PureTank, dex)));
            Assert.That(Rank(eCharacterClass.Cleric, eClassType.Hybrid, eSpellType.PowerRegenBuff),
                Is.LessThan(Rank(eCharacterClass.Paladin, eClassType.Hybrid, eSpellType.PowerRegenBuff)));
            // dex/qui stays front line first until the owner decides (#571)
            var dq = eSpellType.DexterityQuicknessBuff;
            Assert.That(Rank(eCharacterClass.Armsman, eClassType.PureTank, dq), Is.LessThan(Rank(eCharacterClass.Wizard, eClassType.ListCaster, dq)));
            Assert.That(TargetRank(TargetKind.Pet, eCharacterClass.Unknown, eClassType.ListCaster, sc, BuffPriorityAutomatic),
                Is.GreaterThan(Rank(eCharacterClass.Wizard, eClassType.ListCaster, sc)), "pets after every character");
        });

        [Test]
        public void TheBuffPriorityOptionTurnsTheOrder() => Assert.Multiple(() =>
        {
            int Rank(eCharacterClass c, eClassType t, eSpellType type, int choice) => TargetRank(TargetKind.Character, c, t, type, choice);
            var dex = eSpellType.DexterityBuff;
            var sc = eSpellType.StrengthConstitutionBuff;
            Assert.That(Rank(eCharacterClass.Armsman, eClassType.PureTank, dex, BuffPriorityFrontLine),
                Is.LessThan(Rank(eCharacterClass.Wizard, eClassType.ListCaster, dex, BuffPriorityFrontLine)), "Front line first: dex to the tank");
            Assert.That(Rank(eCharacterClass.Wizard, eClassType.ListCaster, sc, BuffPrioritySpellUsers),
                Is.LessThan(Rank(eCharacterClass.Armsman, eClassType.PureTank, sc, BuffPrioritySpellUsers)), "Casters and healers first: every buff");
            Assert.That(Rank(eCharacterClass.Healer, eClassType.Hybrid, sc, BuffPrioritySpellUsers),
                Is.LessThan(Rank(eCharacterClass.Mercenary, eClassType.PureTank, sc, BuffPrioritySpellUsers)));
            Assert.That(Rank(eCharacterClass.Armsman, eClassType.PureTank, sc, BuffPrioritySpellUsers),
                Is.LessThan(Rank(eCharacterClass.Mercenary, eClassType.PureTank, sc, BuffPrioritySpellUsers)), "then the front line by job");
        });

        [Test]
        public void TheBuffPriorityRowIsAGenericRow()
        {
            var row = CompanionOptions165.Option(CompanionOptions165.KindBuffPriority);
            Assert.That(row, Is.Not.Null);
            Assert.That(row.Value.Key, Is.EqualTo("buffpriority"), "True165's stored key");
            Assert.That(row.Value.Choices, Has.Length.EqualTo(3));
            Assert.That(row.Value.Choices[0], Does.StartWith("Automatic"));
            Assert.That(CompanionOptions165.StoredValue(CompanionOptions165.KindBuffPriority, 2), Is.EqualTo(BuffPrioritySpellUsers));
            Assert.That(CompanionOptions165.KindBuffPriority, Is.Not.EqualTo(CompanionOptions165.KindSpeech));
        }

        [Test]
        public void TheBestRankIsChosenAndTiesAreSpread()
        {
            string[] pool = ["caster", "tank-a", "melee", "tank-b"];
            int Rank(string s) => s.StartsWith("tank") ? 0 : s == "melee" ? 1 : 3;
            Assert.That(BotBuffReservations<string>.ChooseBest(pool, _ => true, Rank, _ => 0), Is.EqualTo("tank-b"), "the last tie when every roll wins");
            Assert.That(BotBuffReservations<string>.ChooseBest(pool, _ => true, Rank, n => n == 1 ? 0 : 1), Is.EqualTo("tank-a"), "the first tie");
            Assert.That(BotBuffReservations<string>.ChooseBest(pool, s => !s.StartsWith("tank"), Rank, _ => 0), Is.EqualTo("melee"));
            Assert.That(BotBuffReservations<string>.ChooseBest(pool, _ => false, Rank), Is.Null);
            Assert.That(Enumerable.Range(0, 50).Select(_ => BotBuffReservations<string>.ChooseBest(pool, _ => true, Rank)).Distinct(),
                Is.SubsetOf(new[] { "tank-a", "tank-b" }));
        }

        [Test]
        public void ABaseBuffIsLeftOnlyToAKeeperWhoCanCastIt() => Assert.Multiple(() =>
        {
            Assert.That(KeeperCovers(canAfford: true, inRange: true, keeperInCombat: false, playerGroupRules: true), Is.True);
            Assert.That(KeeperCovers(false, true, false, true), Is.False, "out of concentration: this buffer casts it");
            Assert.That(KeeperCovers(true, false, false, false), Is.False, "left behind (every bot): this buffer casts it");
            Assert.That(KeeperCovers(true, true, true, true), Is.False, "fighting, in a player's group: this buffer casts it");
            Assert.That(KeeperCovers(true, true, true, false), Is.True, "fighting, autonomous group: unchanged");
        });
    }
}
