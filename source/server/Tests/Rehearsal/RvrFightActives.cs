using System.Collections.Generic;
using System.Linq;
using DOL.Database;
using DOL.GS.RealmAbilities;

namespace DOL.GS.Tests.Rehearsal
{
    /// <summary>
    /// #531 step 2c-1: the fight plan's realm abilities in the RvR fight rehearsal (#520). CI has no world database, so the
    /// bots would buy nothing at a trainer; each eight is given what its classes' builds buy (Speed of Sound for the
    /// Minstrel, Mastery of Concentration for casters and healers, Bunker of Faith for the Cleric), and the bots' own brains
    /// use them; casters and healers also get the Quickcast ability (#531 step 2d), the Healer Perfect Recovery and the
    /// Infiltrator Vanish and the archers True Sight (#531 step 2c-2). The row says, per side, when each first started and on how many of the side.
    /// </summary>
    public sealed class RvrFightActives
    {
        public sealed record Start(long At, GameBot By, int On);

        private static readonly (string Key, eEffect Effect, string Label)[] Tracked =
        {
            (RvrRealmActives.SpeedOfSound, eEffect.SpeedOfSound, "sos"),
            (RvrRealmActives.MasteryOfConcentration, eEffect.MasteryOfConcentration, "moc"),
            (RvrRealmActives.BunkerOfFaith, eEffect.BunkerOfFaith, "bof"),
            // #531 step 2d: Quickcast on a key cast under interrupt (an ability, not a realm ability)
            (Abilities.Quickcast, eEffect.QuickCast, "qc"),
            // #531 step 2c-2: Perfect Recovery leaves no effect on the healer, so it is seen by its timer (eEffect.Unknown)
            (RvrRealmActives.PerfectRecovery, eEffect.Unknown, "pr"),
            (RvrRealmActives.Vanish, eEffect.Vanish, "vanish"),
            (RvrRealmActives.TrueSight, eEffect.TrueSight, "ts"),
        };

        private readonly Dictionary<(eRealm Realm, string Label), Start> _first = new();
        private readonly List<GameBot> _bots = new();
        private readonly Dictionary<(eRealm Realm, string Label), int> _uses = new();
        private readonly HashSet<(GameBot Bot, string Label)> _had = new();

        /// <summary>Whether a class's build buys the ability (the class tables: Speed of Sound Minstrel only, Bunker Cleric only).</summary>
        public static bool Buys(eCharacterClass c, string key) => key switch
        {
            RvrRealmActives.SpeedOfSound => c == eCharacterClass.Minstrel,
            RvrRealmActives.BunkerOfFaith => c == eCharacterClass.Cleric,
            RvrRealmActives.PerfectRecovery => c == eCharacterClass.Healer,
            RvrRealmActives.Vanish => c == eCharacterClass.Infiltrator,
            RvrRealmActives.TrueSight => c is eCharacterClass.Scout or eCharacterClass.Hunter or eCharacterClass.Ranger,
            RvrRealmActives.MasteryOfConcentration or Abilities.Quickcast => c is eCharacterClass.Cleric or eCharacterClass.Friar or eCharacterClass.Sorcerer
                or eCharacterClass.Theurgist or eCharacterClass.Wizard or eCharacterClass.Cabalist or eCharacterClass.Necromancer
                or eCharacterClass.Healer or eCharacterClass.Shaman or eCharacterClass.Runemaster or eCharacterClass.Spiritmaster
                or eCharacterClass.Bonedancer or eCharacterClass.Druid or eCharacterClass.Eldritch or eCharacterClass.Enchanter
                or eCharacterClass.Mentalist or eCharacterClass.Animist,
            _ => false,
        };

        private static Ability Make(string key) => key switch
        {
            RvrRealmActives.SpeedOfSound => new AtlasOF_SpeedOfSound(new DbAbility { KeyName = key, Name = "Speed of Sound" }, 1),
            RvrRealmActives.MasteryOfConcentration => new AtlasOF_MasteryofConcentration(new DbAbility { KeyName = key, Name = "Mastery of Concentration" }, 1),
            RvrRealmActives.BunkerOfFaith => new AtlasOF_BunkerOfFaith(new DbAbility { KeyName = key, Name = "Bunker of Faith" }, 1),
            Abilities.Quickcast => new Ability(new DbAbility { KeyName = key, Name = "Quick Cast" }, 1),
            RvrRealmActives.PerfectRecovery => new AtlasOF_PerfectRecovery(new DbAbility { KeyName = key, Name = "Perfect Recovery" }, 1),
            RvrRealmActives.Vanish => new AtlasOF_Vanish(new DbAbility { KeyName = key, Name = "Vanish" }, 1),
            RvrRealmActives.TrueSight => new AtlasOF_TrueSight(new DbAbility { KeyName = key, Name = "True Sight" }, 1),
            _ => null,
        };

        /// <summary>Gives each bot the actives its class buys, as a trainer visit would.</summary>
        public void Grant(IEnumerable<GameBot> bots)
        {
            foreach (GameBot bot in bots)
            {
                _bots.Add(bot);
                var c = (eCharacterClass)bot.CharacterClass.ID;
                foreach ((string key, _, _) in Tracked)
                    if (Buys(c, key) && !bot.HasAbility(key))
                        bot.AddAbility(Make(key), false);
                AutonomousBotRealmAbilities.RefreshActives(bot);
            }
        }

        // Bunker of Faith is a stat buff whose effect type comes from its spell, so it is found by its class.
        private static bool Has(GameBot bot, eEffect effect) => effect switch
        {
            eEffect.BunkerOfFaith => bot.effectListComponent.GetEffects().Any(e => e is DOL.GS.Effects.BunkerOfFaithECSEffect),
            // Perfect Recovery: its timer is running (the rez landed)
            eEffect.Unknown => bot.GetAbility(RvrRealmActives.PerfectRecovery) is TimedRealmAbility pr && bot.GetSkillDisabledDuration(pr) > 0,
            _ => bot.effectListComponent.ContainsEffectForEffectType(effect),
        };

        /// <summary>One look per tick: the first start of each ability on each side, and how many times each started.</summary>
        public void Tick(long now)
        {
            foreach ((_, eEffect effect, string label) in Tracked)
                foreach (GameBot bot in _bots)
                {
                    bool has = bot.IsAlive && Has(bot, effect);
                    if (has && _had.Add((bot, label)))
                        _uses[(bot.Realm, label)] = _uses.GetValueOrDefault((bot.Realm, label)) + 1;
                    else if (!has)
                        _had.Remove((bot, label));
                }
            foreach ((_, eEffect effect, string label) in Tracked)
                foreach (eRealm realm in _bots.Select(b => b.Realm).Distinct())
                {
                    if (_first.ContainsKey((realm, label)))
                        continue;
                    var with = _bots.Where(b => b.Realm == realm && b.IsAlive && Has(b, effect)).ToList();
                    if (with.Count == 0)
                        continue;
                    GameBot by = label == "qc" ? with[0] : _bots.FirstOrDefault(b => b.Realm == realm &&
                        b.GetAbility(Tracked.First(t => t.Label == label).Key) is TimedRealmAbility a && b.GetSkillDisabledDuration(a) > 0);
                    _first[(realm, label)] = new Start(now, by, with.Count);
                }
        }

        public int Uses(eRealm realm, string label) => _uses.GetValueOrDefault((realm, label));

        public Start First(eRealm realm, string label) => _first.TryGetValue((realm, label), out Start s) ? s : null;

        /// <summary>
        /// The row: per side, each ability's first start (time, user's class, members it was on) and for Speed of Sound
        /// whether it came before the side's first hit (on the charge).
        /// </summary>
        public string Row(int seed, RvrFightRecorder recorder)
        {
            string Side(eRealm realm)
            {
                RvrHit firstHit = recorder.Hits.FirstOrDefault(h => h.Attacker?.Realm == realm);
                return string.Join(" ", Tracked.Select(t =>
                {
                    Start s = First(realm, t.Label);
                    if (s == null)
                        return $"{t.Label}=none";
                    string when = t.Label == "sos" ? (firstHit == null || s.At <= firstHit.At ? ":before_first_hit" : ":after_first_hit") : "";
                    string by = s.By?.CharacterClass?.ID is int id ? ((eCharacterClass)id).ToString() : "?";
                    string uses = t.Label == "qc" ? $":uses{Uses(realm, t.Label)}" : "";
                    return $"{t.Label}={RvrFightRehearsal.S(s.At)}:{by}x{s.On}{when}{uses}";
                }));
            }
            return $"RVR_REHEARSAL open_field_realm_abilities seed={seed} Alb: {Side(eRealm.Albion)} Mid: {Side(eRealm.Midgard)}";
        }
    }
}
