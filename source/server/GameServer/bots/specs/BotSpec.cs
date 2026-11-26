using System;
using System.Collections.Generic;
using System.Linq;
using DAoCLivingServer.Shared;

namespace DOL.GS
{
    public class BotSpec
    {
        public static string SpecName;
        public eObjectType WeaponOneType;
        public eObjectType WeaponTwoType;
        public eWeaponDamageType DamageType = 0;
        public eSpecType SpecType;
        public bool Is2H;
        public List<BotSpecLine> SpecLines = new List<BotSpecLine>();
        /// <summary>Era builds 1.2 (WP3): the era build this lifetime plan was made from (<see cref="EraBuild.Id"/>), saved with
        /// the plan; empty for today's class-file builds and for every plan saved before 1.2.</summary>
        public string EraBuildId = string.Empty;
        /// <summary>The era build's role in the research's words (healer-cc, cc-mezzer, cc-root, tank...); empty for today's
        /// builds. Read by the RvR eight's crowd-control slot (WP5, rvr_roster_build_roles).</summary>
        public string EraRole = string.Empty;

        public BotSpec()
        { }

        protected void Add(string spec, uint cap, float ratio)
        {
            SpecLines.Add(new BotSpecLine(spec, cap, ratio));
        }

        protected string ObjToSpec(eObjectType obj)
        {
            return SkillBase.ObjectTypeToSpec(obj);
        }

        public static BotSpec GetSpec(eCharacterClass charClass, eSpecType spec = eSpecType.None)
        {
            switch (charClass)
            {
                case eCharacterClass.Armsman: return new ArmsmanBotSpec(spec);
                case eCharacterClass.Cabalist: return new CabalistBotSpec(spec);
                case eCharacterClass.Cleric: return new ClericBotSpec(spec);
                case eCharacterClass.Friar: return new FriarBotSpec(spec);
                case eCharacterClass.Infiltrator: return new InfiltratorBotSpec();
                case eCharacterClass.Mercenary: return new MercenaryBotSpec(spec);
                case eCharacterClass.Minstrel: return new MinstrelBotSpec();
                case eCharacterClass.Paladin: return new PaladinBotSpec(spec);
                case eCharacterClass.Reaver: return new ReaverBotSpec(spec);
                case eCharacterClass.Scout: return new ScoutBotSpec();
                case eCharacterClass.Sorcerer: return new SorcererBotSpec(spec);
                case eCharacterClass.Theurgist: return new TheurgistBotSpec(spec);
                case eCharacterClass.Wizard: return new WizardBotSpec(spec);
                case eCharacterClass.Necromancer: return new NecromancerBotSpec(spec);

                case eCharacterClass.Bard: return new BardBotSpec(spec);
                case eCharacterClass.Blademaster: return new BlademasterBotSpec(spec);
                case eCharacterClass.Champion: return new ChampionBotSpec(spec);
                case eCharacterClass.Druid: return new DruidBotSpec(spec);
                case eCharacterClass.Eldritch: return new EldritchBotSpec(spec);
                case eCharacterClass.Enchanter: return new EnchanterBotSpec(spec);
                case eCharacterClass.Hero: return new HeroBotSpec(spec);
                case eCharacterClass.Mentalist: return new MentalistBotSpec(spec);
                case eCharacterClass.Nightshade: return new NightshadeBotSpec();
                case eCharacterClass.Animist: return new AnimistBotSpec(spec);
                case eCharacterClass.Ranger: return new RangerBotSpec();
                case eCharacterClass.Valewalker: return new ValewalkerBotSpec();
                case eCharacterClass.Warden: return new WardenBotSpec(spec);
                case eCharacterClass.Sluaghbinder: return new SluaghbinderBotSpec(spec);

                case eCharacterClass.Berserker: return new BerserkerBotSpec();
                case eCharacterClass.Bonedancer: return new BonedancerBotSpec(spec);
                case eCharacterClass.Healer: return new HealerBotSpec(spec);
                case eCharacterClass.Hunter: return new HunterBotSpec(spec);
                case eCharacterClass.Runemaster: return new RunemasterBotSpec(spec);
                case eCharacterClass.Savage: return new SavageBotSpec(spec);
                case eCharacterClass.Shadowblade: return new ShadowbladeBotSpec(spec);
                case eCharacterClass.Shaman: return new ShamanBotSpec(spec);
                case eCharacterClass.Skald: return new SkaldBotSpec(spec);
                case eCharacterClass.Spiritmaster: return new SpiritmasterBotSpec(spec);
                case eCharacterClass.Thane: return new ThaneBotSpec(spec);
                case eCharacterClass.Warrior: return new WarriorBotSpec(spec);
            }

            return null;
        }

        public static IReadOnlyList<eSpecType> GetSpecializationChoices(eCharacterClass charClass) => charClass switch
        {
            eCharacterClass.Armsman => [eSpecType.OneHandAndShield, eSpecType.OneHandHybrid, eSpecType.TwoHandHybrid, eSpecType.TwoHanded],
            eCharacterClass.Cabalist => [eSpecType.MatterCab, eSpecType.BodyCab, eSpecType.SpiritCab],
            eCharacterClass.Cleric => [eSpecType.RejuvCleric, eSpecType.EnhanceCleric, eSpecType.SmiteCleric],
            eCharacterClass.Friar => [eSpecType.RejuvFriar, eSpecType.EnhanceFriar, eSpecType.StaffFriar],
            eCharacterClass.Mercenary => [eSpecType.DualWield, eSpecType.DualWieldAndShield],
            eCharacterClass.Paladin => [eSpecType.OneHandAndShield, eSpecType.TwoHandHybrid],
            eCharacterClass.Sorcerer => [eSpecType.MatterSorc, eSpecType.BodySorc, eSpecType.MindSorc],
            eCharacterClass.Theurgist => [eSpecType.EarthTheur, eSpecType.IceTheur, eSpecType.AirTheur],
            eCharacterClass.Wizard => [eSpecType.EarthWiz, eSpecType.IceWiz, eSpecType.FireWiz],
            // Added builds (owner 2026-10-29): eSpecType.None is each class's original build with its own variants.
            eCharacterClass.Necromancer => [eSpecType.DeathsightNecro, eSpecType.PainworkingNecro, eSpecType.DeathServantNecro],
            eCharacterClass.Reaver => [eSpecType.None, eSpecType.SoulrendingReaver],
            eCharacterClass.Bard => [eSpecType.None, eSpecType.RegrowthBard],
            eCharacterClass.Hunter => [eSpecType.None, eSpecType.SpearHunter],
            eCharacterClass.Blademaster => [eSpecType.DualWield, eSpecType.DualWieldAndShield],
            eCharacterClass.Champion => [eSpecType.OneHandAndShield, eSpecType.TwoHanded],
            eCharacterClass.Druid => [eSpecType.RegrowthDruid, eSpecType.NurtureDruid, eSpecType.NatureDruid],
            eCharacterClass.Eldritch => [eSpecType.LightEld, eSpecType.ManaEld, eSpecType.VoidEld],
            eCharacterClass.Enchanter => [eSpecType.LightEnchanter, eSpecType.ManaEnchanter, eSpecType.EnchantmentEnchanter],
            eCharacterClass.Hero => [eSpecType.OneHandAndShield, eSpecType.TwoHandHybrid],
            eCharacterClass.Mentalist => [eSpecType.LightMenta, eSpecType.ManaMenta, eSpecType.MentaMenta],
            eCharacterClass.Animist => [eSpecType.ArborealAnimist, eSpecType.CreepingAnimist, eSpecType.VerdantAnimist],
            eCharacterClass.Warden => [eSpecType.RegrowthWarden, eSpecType.NurtureWarden, eSpecType.BattleWarden],
            eCharacterClass.Sluaghbinder => [eSpecType.SluaghbinderBulwark, eSpecType.SluaghbinderBane, eSpecType.SluaghbinderCovenant],
            eCharacterClass.Bonedancer => [eSpecType.DarkBone, eSpecType.SuppBone, eSpecType.ArmyBone],
            eCharacterClass.Healer => [eSpecType.MendHealer, eSpecType.AugHealer, eSpecType.PacHealer],
            eCharacterClass.Runemaster => [eSpecType.DarkRune, eSpecType.SuppRune, eSpecType.RuneRune],
            eCharacterClass.Savage => [eSpecType.TwoHanded, eSpecType.DualWield],
            eCharacterClass.Shadowblade => [eSpecType.LeftAxe, eSpecType.TwoHanded],
            eCharacterClass.Shaman => [eSpecType.MendShaman, eSpecType.AugShaman, eSpecType.SubtShaman],
            eCharacterClass.Spiritmaster => [eSpecType.DarkSpirit, eSpecType.SuppSpirit, eSpecType.SummSpirit],
            eCharacterClass.Thane => [eSpecType.OneHandAndShield, eSpecType.TwoHanded],
            eCharacterClass.Skald => [eSpecType.OneHandAndShield, eSpecType.TwoHanded],
            eCharacterClass.Warrior => [eSpecType.OneHandAndShield, eSpecType.TwoHanded],
            _ => [eSpecType.None],
        };

        public static eSpecType ChooseRandomSpecialization(eCharacterClass charClass)
        {
            IReadOnlyList<eSpecType> choices = GetSpecializationChoices(charClass);
            return choices[Util.Random(choices.Count - 1)];
        }

        /// <summary>
        /// Era builds 1.2 (WP3, docs/ERA-BUILDS-1.2.md "The build draw"): a lifetime plan from an era build. The class file
        /// for the build's family sets the weapons, gear rules and <see cref="SpecType"/> as today; its weapon lines are
        /// then moved to the ones the build trains (Polearm, Two Handed, Large Weapons...), and its spec lines are replaced by
        /// the build's, each capped at the build's level with a derived levelling ratio (<see cref="EraBuildCatalog.DerivedRatio"/>).
        /// <paramref name="specs"/> are the levels to train (the build's own, or after <see cref="EraBuildCatalog.Jitter"/>);
        /// <paramref name="weaponLevels"/> optionally adds levels a bot already trained (the nearest-build case), so its
        /// weapons follow its own points. Null for the custom class (bots never play it), an unknown family or a class
        /// without a class file.
        /// </summary>
        public static BotSpec FromEraBuild(eCharacterClass charClass, EraBuild build,
            IReadOnlyList<KeyValuePair<string, int>> specs = null, long botId = 0, Func<string, int> weaponLevels = null)
        {
            if (build == null || !BotCustomClassRule.BotsMayPlay((int)charClass) ||
                !Enum.TryParse(build.Family, ignoreCase: false, out eSpecType family) || !Enum.IsDefined(family))
                return null;
            specs ??= build.Specs;
            if (specs.Count == 0)
                return null;
            BotSpec plan;
            // The class file's own random picks (its variant, and any weapon slot the build doesn't train) are seeded by the
            // bot and the build, so a crash before the first save makes the same plan again.
            using (Util.SeededScope(PlanSeed(botId, build.Id)))
            {
                plan = charClass switch
                {
                    eCharacterClass.Bonedancer => new BonedancerBotSpec(family, botId, true),
                    eCharacterClass.Savage => new SavageBotSpec(family, SavageWeapon(specs)),
                    _ => GetSpec(charClass, family),
                };
            }
            if (plan == null)
                return null;

            int Level(string line)
            {
                int level = 0;
                foreach (KeyValuePair<string, int> entry in specs)
                    if (string.Equals(entry.Key, line, StringComparison.Ordinal))
                        level = entry.Value;
                return Math.Max(level, weaponLevels?.Invoke(line) ?? 0);
            }
            eObjectType before = plan.WeaponOneType;
            BotLifetimeBuild.AlignWithInvestedWeapons(plan, Level);
            if (plan.WeaponOneType != before && plan.DamageType != 0)
                plan.DamageType = DamageTypeOf(plan.WeaponOneType, plan.DamageType);

            EraBuild trained = ReferenceEquals(specs, build.Specs) ? build : build with { Specs = specs };
            plan.SpecLines = specs.Select(line => new BotSpecLine(line.Key, (uint)Math.Clamp(line.Value, 1, 50),
                EraBuildCatalog.DerivedRatio(trained, line.Key))).ToList();
            plan.EraBuildId = build.Id;
            plan.EraRole = build.Role ?? string.Empty;
            return plan;
        }

        /// <summary>A seed from the bot id and the build id that is the same in every process (FNV-1a, then SplitMix64).</summary>
        private static int PlanSeed(long botId, string buildId)
        {
            unchecked
            {
                ulong hash = 14695981039346656037UL;
                foreach (char c in buildId ?? string.Empty)
                    hash = (hash ^ c) * 1099511628211UL;
                ulong value = hash ^ (ulong)botId;
                value += 0x9E3779B97F4A7C15UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                return (int)(value ^ (value >> 31));
            }
        }

        /// <summary>The Savage's weapon from an era build's lines (Hand to Hand, Sword, Axe or Hammer; 0 = the class file's pick).</summary>
        private static eObjectType SavageWeapon(IReadOnlyList<KeyValuePair<string, int>> specs)
        {
            foreach (KeyValuePair<string, int> line in specs)
            {
                switch (line.Key)
                {
                    case Specs.HandToHand: return eObjectType.HandToHand;
                    case Specs.Sword: return eObjectType.Sword;
                    case Specs.Axe: return eObjectType.Axe;
                    case Specs.Hammer: return eObjectType.Hammer;
                }
            }
            return 0;
        }

        private static eWeaponDamageType DamageTypeOf(eObjectType weapon, eWeaponDamageType fallback) => weapon switch
        {
            eObjectType.SlashingWeapon or eObjectType.Blades or eObjectType.Sword or eObjectType.Axe => eWeaponDamageType.Slash,
            eObjectType.ThrustWeapon or eObjectType.Piercing or eObjectType.Spear => eWeaponDamageType.Thrust,
            eObjectType.CrushingWeapon or eObjectType.Blunt or eObjectType.Hammer => eWeaponDamageType.Crush,
            _ => fallback,
        };

        public static eSpecType ChoosePersistentSpecialization(eCharacterClass charClass, long botId)
        {
            IReadOnlyList<eSpecType> choices = GetSpecializationChoices(charClass);
            unchecked
            {
                ulong value = (ulong)botId + 0x9E3779B97F4A7C15UL + (ulong)(int)charClass * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
                value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return choices[(int)(value % (ulong)choices.Count)];
            }
        }
    }
}
