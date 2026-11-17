using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using DOL.GS;

namespace DOL.UnitTests
{
    /// <summary>
    /// Boards, combatants and facts for the open-field RvR work package 2 tests (assist, crowd control, interrupts).
    /// Every combatant has a live object (an uninitialized gamebot from DutyLeaseTestKit, so a DutyLeaseFixture is
    /// needed), because the plan names its targets by object; nothing else of the game is used.
    /// </summary>
    internal static class RvrFieldTestKit
    {
        private static long _nextKey = 9_100_000_000;

        internal static RvrCombatant Head(eRealm realm, eCharacterClass characterClass, int x = 0, int y = 0,
            bool targetsGroup = false, bool targetedByGroup = false, RvrControlState control = RvrControlState.None,
            int health = 100, int level = 50) => new()
        {
            Living = DutyLeaseTestKit.Gamebot(realm: realm, objective: "RvR"),
            Key = Interlocked.Increment(ref _nextKey),
            Realm = realm,
            Class = characterClass,
            Role = RvrRoles.For(characterClass),
            Level = level,
            HealthPercent = health,
            X = x,
            Y = y,
            TargetsGroup = targetsGroup,
            TargetedByGroup = targetedByGroup,
            Control = control,
        };

        /// <summary>An enemy fighting the group (in contact).</summary>
        internal static RvrCombatant Foe(eRealm realm, eCharacterClass characterClass, int x = 400, int y = 0,
            RvrControlState control = RvrControlState.None, int health = 100) =>
            Head(realm, characterClass, x, y, targetsGroup: true, control: control, health: health);

        internal static RvrFightBoard Board(long now, IEnumerable<RvrCombatant> members, IEnumerable<RvrCombatant> enemies,
            eRealm realm = eRealm.Albion) => RvrFightBoard.Build(now, 51, realm, members, enemies);

        internal static RvrFightPlan Start(long now, RvrSkillTier tier) =>
            RvrFightPlan.Neutral(now, new RvrGroupState { SkillTier = tier });

        /// <summary>A standard Albion eight: Armsman (leader), Mercenary x2, Paladin, Cleric x2, Sorcerer, Minstrel.</summary>
        internal static List<RvrCombatant> AlbionEight() =>
        [
            Head(eRealm.Albion, eCharacterClass.Armsman), Head(eRealm.Albion, eCharacterClass.Mercenary, 50),
            Head(eRealm.Albion, eCharacterClass.Mercenary, 100), Head(eRealm.Albion, eCharacterClass.Paladin, 150),
            Head(eRealm.Albion, eCharacterClass.Cleric, -300), Head(eRealm.Albion, eCharacterClass.Cleric, -350, 100),
            Head(eRealm.Albion, eCharacterClass.Sorcerer, -400), Head(eRealm.Albion, eCharacterClass.Minstrel, -200),
        ];

        internal sealed class FactsBuilder
        {
            private readonly ImmutableDictionary<long, RvrEnemyFacts>.Builder _enemies = ImmutableDictionary.CreateBuilder<long, RvrEnemyFacts>();
            private readonly ImmutableDictionary<long, RvrMemberFacts>.Builder _members = ImmutableDictionary.CreateBuilder<long, RvrMemberFacts>();

            public FactsBuilder Enemy(RvrCombatant enemy, RvrEnemyFacts facts)
            {
                _enemies[enemy.Key] = facts;
                return this;
            }

            public FactsBuilder Member(RvrCombatant member, RvrMemberFacts facts)
            {
                _members[member.Key] = facts with { Boldness = facts.Boldness == 0 ? 0.5 : facts.Boldness };
                return this;
            }

            public RvrFieldFacts Build() => new() { Enemies = _enemies.ToImmutable(), Members = _members.ToImmutable() };
        }

        internal static FactsBuilder Facts() => new();
    }
}
