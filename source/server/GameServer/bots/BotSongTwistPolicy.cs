using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>One native song source at a time; never extend its child effects or skip recast timers.</summary>
    public static class BotSongTwistPolicy
    {
        public readonly record struct Song(int Id, int CastMilliseconds, int CooldownMilliseconds, long RemainingMilliseconds);

        public static bool IsManagedSong(GameLiving living, Spell spell)
        {
            if (living is not GameBot bot || bot.CharacterClass == null ||
                spell == null || !spell.IsPulsing || spell.IsHarmful || spell.IsFocus)
                return false;
            eCharacterClass characterClass = (eCharacterClass)bot.CharacterClass.ID;
            if (BotBrain.IsClassicSongClass(characterClass))
                return BotBrain.IsMaintainableClassBuff(spell);
            // Tank chants use a separate selector: never send Paladin/Warden
            // through the performer code that drops songs for solo melee.
            return spell.IsInstantCast && !spell.NeedInstrument && (characterClass switch
            {
                eCharacterClass.Paladin => spell.SpellType is eSpellType.EnduranceRegenBuff or
                    eSpellType.SpecArmorFactorBuff or eSpellType.DamageAdd or eSpellType.CombatHeal,
                eCharacterClass.Warden => spell.SpellType is eSpellType.SpeedEnhancement or
                    eSpellType.Bladeturn or eSpellType.DamageAdd,
                _ => false
            });
        }

        /// <summary>The one chant a solo Paladin holds: damage in a fight, the combat heal when hurt, endurance otherwise.</summary>
        public static eSpellType SoloPaladinAnchor(bool fighting, int healthPercent) =>
            !fighting ? eSpellType.EnduranceRegenBuff : healthPercent < 70 ? eSpellType.CombatHeal : eSpellType.DamageAdd;

        public static bool IsMobileSong(GameLiving living, Spell spell)
        {
            return IsManagedSong(living, spell) &&
                (spell.IsInstantCast || spell.NeedInstrument || spell.MoveCast);
        }

        // Only temporary player companions use this travel pool. Bards have
        // a real endurance song; Skalds and Minstrels do not, so their useful
        // second travel song is health regeneration. Power and combat chants
        // must not take the speed anchor while the owner is moving.
        public static bool IsCompanionTravelSong(eCharacterClass characterClass, eSpellType type) =>
            characterClass switch
            {
                eCharacterClass.Bard => type is eSpellType.SpeedEnhancement or eSpellType.EnduranceRegenBuff,
                eCharacterClass.Skald or eCharacterClass.Minstrel =>
                    type is eSpellType.SpeedEnhancement or eSpellType.HealthRegenBuff,
                _ => false
            };

        // Only one helpful pulse source is legal. Generic buff selection must
        // not independently replace a tank's anchor with a resist/endurance
        // pulse; those are deliberately not part of the chosen core rotation.
        public static bool IsReservedPulse(GameLiving living, Spell spell) =>
            IsManagedSong(living, spell) ||
            living is GameBot bot && bot.CharacterClass != null &&
            (eCharacterClass)bot.CharacterClass.ID is eCharacterClass.Paladin or eCharacterClass.Warden &&
            spell != null && spell.IsPulsing && !spell.IsHarmful && !spell.IsFocus &&
            BotBrain.IsMaintainableClassBuff(spell);

        public static bool HasMobileSongCast(GameLiving living)
        {
            if (living is not GameBot bot || bot.CharacterClass == null)
                return false;

            CastingComponent casting = living.castingComponent;
            if (casting == null) return false;
            Spell active = casting.SpellHandler?.Spell;
            Spell queued = casting.QueuedSpellHandler?.Spell;
            bool pending = casting.TryPeekPendingSpell(out Spell requested);
            // Never use a song to authorize movement during an ordinary heal,
            // hostile spell, or unknown ability request queued alongside it.
            return (active != null || queued != null || pending) &&
                (active == null || IsMobileSong(living, active)) &&
                (queued == null || IsMobileSong(living, queued)) &&
                (!pending || IsMobileSong(living, requested));
        }

        public static eSpellType WardenAnchor(bool traveling, bool combat, bool grouped, bool hasBladeturn)
        {
            if (traveling && !combat) return eSpellType.SpeedEnhancement;
            if (hasBladeturn && (combat || grouped)) return eSpellType.Bladeturn;
            return eSpellType.DamageAdd;
        }

        // ---- Holding one song like a player (owner 2026-10-30: "the twisting ... is too much sound diarrhea")
        // Performers no longer twist a second song in and out; each situation has one song, held until the situation
        // changes, and never changed sooner than HoldMilliseconds after the last change. Normal and True 1.65 Mode.
        public const int HoldMilliseconds = 10_000;
        public const int TravelAfterMovingMilliseconds = 3_000;  // short steps (a pull, a reposition) are not travel
        public const int StayTravelingMilliseconds = 5_000;      // short stops keep the speed song

        /// <summary>
        /// The one song a Bard, Minstrel or Skald holds: speed while travelling; in a fight endurance (then power, then
        /// health); standing around, whatever the group is short of (power, health, endurance), else the song already
        /// playing. Null when none of the wanted songs is known.
        /// </summary>
        public static eSpellType? PerformerHold(bool traveling, bool combat, bool powerLow, bool healthLow, bool enduranceLow,
            ICollection<eSpellType> available, eSpellType? active)
        {
            if (traveling && !combat && available.Contains(eSpellType.SpeedEnhancement))
                return eSpellType.SpeedEnhancement;
            var wanted = new List<eSpellType>();
            if (combat)
                wanted.AddRange([eSpellType.EnduranceRegenBuff, eSpellType.PowerRegenBuff, eSpellType.HealthRegenBuff]);
            else
            {
                if (powerLow) wanted.Add(eSpellType.PowerRegenBuff);
                if (healthLow) wanted.Add(eSpellType.HealthRegenBuff);
                if (enduranceLow) wanted.Add(eSpellType.EnduranceRegenBuff);
                // nobody short of anything: keep the song already playing (any but speed, which is for travel)
                if (wanted.Count == 0 && active is { } current && current != eSpellType.SpeedEnhancement && available.Contains(current))
                    return current;
                wanted.AddRange([eSpellType.PowerRegenBuff, eSpellType.HealthRegenBuff, eSpellType.EnduranceRegenBuff]);
            }
            foreach (eSpellType type in wanted)
                if (available.Contains(type))
                    return type;
            // owner 2026-10-30: a bot (gamebot or companion) that hasn't learned speed or endurance yet uses what it has
            // until then: any other song it knows, speed last.
            return available.Where(type => type != eSpellType.SpeedEnhancement).OrderBy(type => type).Cast<eSpellType?>().FirstOrDefault()
                ?? (available.Contains(eSpellType.SpeedEnhancement) ? eSpellType.SpeedEnhancement : null);
        }

        /// <summary>The one chant a Paladin holds (owner 2026-10-30: endurance in a fight), grouped or solo.</summary>
        public static eSpellType PaladinHold(ICollection<eSpellType> available) =>
            new[] { eSpellType.EnduranceRegenBuff, eSpellType.DamageAdd, eSpellType.SpecArmorFactorBuff, eSpellType.CombatHeal }
                .FirstOrDefault(available.Contains, eSpellType.EnduranceRegenBuff);

        public static int Choose(int primaryId, int activeId, IReadOnlyList<Song> songs)
        {
            Song primary = songs.FirstOrDefault(song => song.Id == primaryId);
            if (primary.Id == 0) return 0;
            // Return to the anchor immediately after the secondary takes. In
            // particular Skald must not leave speed while its 8s reuse is up.
            if (activeId != primaryId)
                return primary.CooldownMilliseconds <= 0 ? primaryId : 0;
            if (primary.CooldownMilliseconds > 0) return 0;

            foreach (Song secondary in songs.Where(song => song.Id != primaryId && song.CooldownMilliseconds <= 0)
                         .OrderBy(song => song.RemainingMilliseconds))
            {
                int roundTrip = Math.Max(0, secondary.CastMilliseconds) + Math.Max(0, primary.CastMilliseconds) + 1000;
                if (primary.RemainingMilliseconds > roundTrip && secondary.RemainingMilliseconds <= roundTrip)
                    return secondary.Id;
            }
            return 0;
        }
    }
}
