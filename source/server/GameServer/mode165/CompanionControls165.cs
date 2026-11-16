using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;

namespace DOL.GS
{
    /// <summary>A companion's stance (True 1.65 keeps it in CompanionEngagementMode, with /passive; Living Server has
    /// /aggressive and /defensive only, so Passive is never chosen here).</summary>
    public enum CompanionStance : byte { Aggressive = 0, Defensive = 1, Passive = 2 }

    /// <summary>
    /// Companion controls and AI rows from the veteran review (M7, owner 2026-11-11) and the owner's "Twist songs /
    /// chants" option. Every reader goes through <see cref="CompanionOptions165.Choice"/>, which is always 0 (Automatic)
    /// for a gamebot, so gamebots keep their behaviour; the rules themselves are pure and unit tested.
    /// Generic option kinds 40 and up (CompanionOptions165): 40 twist, 41 stance, 42 crowd control, 43 pet, 44 weapon,
    /// 45 Warden chant, 46 Warden role, 47 Stag, 48 Critical Shot, 49 opening stun, 50 Engage, 51 shield swap for Slam,
    /// 52 Triple Wield.
    /// </summary>
    public static class CompanionControls165
    {
        // ---- Twist songs / chants (owner 2026-11-11: default OFF, companions only)

        /// <summary>The "Twist songs / chants" row: 0 Automatic (hold one song, as since 2026-10-30), 1 twist.</summary>
        public const int TwistOn = 1;

        /// <summary>The classes whose songs or chants pulse and may be twisted: Bard, Minstrel, Skald (songs) and Paladin
        /// (chants). The Warden is left out: its pulsing bladeturn's recast can outlast the bubble it gives, so a twist
        /// away and back loses the bladeturn (the old twisting code never cycled it either).</summary>
        public static bool TwistClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Bard or eCharacterClass.Minstrel or eCharacterClass.Skald or eCharacterClass.Paladin;

        /// <summary>Pure: the row is offered to a twisting class that knows two or more kinds of song or chant.</summary>
        public static bool OffersTwist(eCharacterClass characterClass, int songKinds) => TwistClass(characterClass) && songKinds >= 2;

        /// <summary>The kinds of song or chant this companion holds now (one per kind).</summary>
        public static int SongKinds(GameBot bot)
        {
            if (bot?.CharacterClass == null)
                return 0;
            bool paladin = bot.CharacterClass.ID == (int)eCharacterClass.Paladin;
            return (bot.MiscSpells ?? []).Concat(bot.InstantMiscSpells ?? [])
                .Where(spell => spell != null && spell.Level <= bot.Level && spell.IsPulsing && !spell.IsHarmful &&
                                (paladin ? BotSongTwistPolicy.IsManagedSong(bot, spell) : BotBrain.IsMaintainableClassBuff(spell)))
                .Select(spell => spell.SpellType).Distinct().Count();
        }

        /// <summary>Whether this companion twists its songs or chants (always false for gamebots).</summary>
        public static bool TwistsSongs(GameBot bot) =>
            bot?.CharacterClass != null && TwistClass((eCharacterClass)bot.CharacterClass.ID) &&
            CompanionOptions165.Choice(bot, "twist") == TwistOn;

        /// <summary>
        /// Pure: may a song join the twist next to the held one? Travelling only the travel songs (speed, and the Bard's
        /// endurance or a Skald's or Minstrel's health song, as the old twisting code allowed); otherwise any song but
        /// speed. An unaffordable or the held song itself never joins.
        /// </summary>
        public static bool TwistPartner(eCharacterClass characterClass, eSpellType song, eSpellType held, bool traveling,
            bool affordable) =>
            affordable && song != held &&
            (traveling ? BotSongTwistPolicy.IsCompanionTravelSong(characterClass, song) : song != eSpellType.SpeedEnhancement);

        /// <summary>
        /// Pure: a Paladin chant joins the twist - every chant but the combat heal, which only joins while someone near is
        /// hurt in a fight (the old twisting code's rule).
        /// </summary>
        public static bool PaladinTwistPartner(eSpellType chant, eSpellType held, bool combatHealing, bool affordable) =>
            affordable && chant != held && (chant != eSpellType.CombatHeal || combatHealing);

        /// <summary>How long a twisting companion's song switch plays without its sound: the cast plus this margin.</summary>
        public const int TwistQuietMarginMilliseconds = 2_000;

        /// <summary>Pure: a song switch is silent (a twisting companion going from one song to the next); the first song
        /// of a set plays its sound as always.</summary>
        public static bool QuietSwitch(bool twisting, bool anotherSongPlaying) => twisting && anotherSongPlaying;

        // ---- /passive keeps the lifesavers (owner decision O2, 2026-11-11)

        /// <summary>How a /passive companion may heal this turn: not at all (not passive), instant heals and cure mez only
        /// (its player is moving), or its normal heals (its player has stopped).</summary>
        public enum PassiveHealing : byte { None = 0, WhileMoving = 1, Stopped = 2 }

        /// <summary>
        /// Pure (O2): a heal a /passive companion may cast - an instant heal or a cure mez any time, a cast-time heal only
        /// while its player stands still (it never stops and falls behind a moving player).
        /// </summary>
        public static bool PassiveAllowsHeal(PassiveHealing healing, bool instant, bool cureMez) =>
            healing == PassiveHealing.Stopped || healing == PassiveHealing.WhileMoving && (instant || cureMez);

        /// <summary>
        /// Pure (O2): a cast /passive lets finish - a charm (a charmed pet must not turn), the speed song or chant, a cure
        /// mez, an instant heal, or a cast-time heal while the player stands still. Everything else stops (no fighting, no
        /// long casts).
        /// </summary>
        public static bool PassiveKeepsCast(bool charm, bool speedSong, bool cureMez, bool heal, bool instant, bool leaderMoving) =>
            charm || speedSong || cureMez || heal && (instant || !leaderMoving);

        // ---- Per-companion stance (M7, G13)

        /// <summary>The "Stance" row's choices: 0 Automatic (the group's stance), 1 aggressive, 2 defensive, 3 passive.</summary>
        public const int StanceGroup = 0, StanceAggressive = 1, StanceDefensive = 2, StancePassive = 3;

        /// <summary>Pure: the stance this companion's own choice sets, or null to follow the group's.</summary>
        public static CompanionStance? OwnStance(int choice) => choice switch
        {
            StanceAggressive => CompanionStance.Aggressive,
            StanceDefensive => CompanionStance.Defensive,
            StancePassive => CompanionStance.Passive,
            _ => null,
        };

        /// <summary>Pure: a companion's stance - its own choice when it has one, else its player's /aggressive, /defensive
        /// or /passive.</summary>
        public static CompanionStance EffectiveStance(int ownChoice, CompanionStance groupStance) =>
            OwnStance(ownChoice) ?? groupStance;

        // ---- /camp (M7, G4)

        /// <summary>How far the player may go from the camp, and for how long beyond that, before the camp is dropped.</summary>
        public const int CampLeaveDistance = 2_500;
        public const long CampLeaveMilliseconds = 10_000;

        /// <summary>Pure: the camp still holds - same region, and the player within 2,500 of it or away less than 10 s.</summary>
        public static bool CampHolds(bool sameRegion, int playerDistance, long awayMilliseconds) =>
            sameRegion && (playerDistance <= CampLeaveDistance || awayMilliseconds < CampLeaveMilliseconds);

        // ---- Crowd control row (M7, G7)

        /// <summary>The "Crowd control" row: 0 Automatic, 1 Main CC, 2 adds only, 3 off.</summary>
        public const int CcAutomatic = 0, CcMain = 1, CcAddsOnly = 2, CcOff = 3;

        /// <summary>Pure: mez, stun or root an add at all (everything but Off).</summary>
        public static bool CcAllowsAddControl(int choice) => choice != CcOff;

        /// <summary>Pure: control the group's kill target too (a Pacification Healer's opening stun): Automatic and Main CC.</summary>
        public static bool CcAllowsKillTargetControl(int choice) => choice is CcAutomatic or CcMain;

        /// <summary>Pure: the main mezzer spends its long-reuse mez or stun on any add, not only on three or more.</summary>
        public static bool CcLongReuseOnAnyAdd(int choice) => choice == CcMain;

        /// <summary>Pure: an Automatic companion leaves the adds to a groupmate companion set to Main CC that can act.</summary>
        public static bool DefersToMainCc(int ownChoice, bool mainCcReady) => ownChoice == CcAutomatic && mainCcReady;

        /// <summary>The player's crowd-control choice for this companion (0 for gamebots).</summary>
        public static int CrowdControl(GameBot bot) => CompanionOptions165.Choice(bot, "cc");

        /// <summary>Another companion of this companion's group is the main mezzer and can act now.</summary>
        public static bool MainCcReady(GameBot bot)
        {
            if (bot?.IsPlayerCompanion != true || bot.Group is not { } group)
                return false;
            foreach (GameLiving member in group.GetMembersInTheGroup())
                if (member is GameBot other && other != bot && other.IsAlive && !other.IsIncapacitated &&
                    other.CurrentRegionID == bot.CurrentRegionID && other.IsPlayerCompanion && CrowdControl(other) == CcMain)
                    return true;
            return false;
        }

        // ---- Pet row (M7, G9; Q24 Druid, Q25 Spiritmaster)

        /// <summary>The "Pet" row: 0 Automatic, 1 guard me (defend only), 2 take adds, 3 attack my target.</summary>
        public const int PetAutomatic = 0, PetGuardMe = 1, PetTakeAdds = 2, PetAttack = 3;

        /// <summary>The classes offered the Pet row: the ones that keep a fighting pet (a Necromancer's servant is its
        /// body and keeps its own rules; a Theurgist's pets come and go and keep the Theurgist rule).</summary>
        public static bool PetRowClass(eCharacterClass characterClass) => characterClass is
            eCharacterClass.Cabalist or eCharacterClass.Enchanter or eCharacterClass.Spiritmaster or
            eCharacterClass.Bonedancer or eCharacterClass.Hunter or eCharacterClass.Druid or
            eCharacterClass.Sorcerer or eCharacterClass.Minstrel;

        /// <summary>The player's pet choice for this companion (0 for gamebots).</summary>
        public static int PetJob(GameBot bot) => CompanionOptions165.Choice(bot, "petjob");

        /// <summary>Pure: the pet service keeps the pet on defence - Guard me always, Take adds and Attack never (they
        /// pick their own target), Automatic as before (a Regrowth or Nurture Druid, a solo Minstrel).</summary>
        public static bool PetDefendsOnly(int job, bool automatic) => job switch
        {
            PetGuardMe => true,
            PetTakeAdds or PetAttack => false,
            _ => automatic,
        };

        /// <summary>Pure: the owner's own "send my pet at my target" orders are held - Guard me and Take adds (the pet
        /// service sends it at adds only), Automatic as before; Attack never.</summary>
        public static bool BrainHoldsPetOffTarget(int job, bool automatic) => job switch
        {
            PetGuardMe or PetTakeAdds => true,
            PetAttack => false,
            _ => automatic,
        };

        /// <summary>
        /// Pure (owner 2026-11-12: a healing Druid companion's lynx ran at the target and came back, over and over): does
        /// a support companion that sits a fight out call its pet off? Not when the pet is held anyway (Guard me, Take
        /// adds, a defence-only class), and not when it is a companion Druid's pet: the Druid sends that one at the
        /// group's target herself while she heals, and the call-off undid her order on every pulse.
        /// </summary>
        public static bool SupportCallsPetOff(bool brainHoldsPetOffTarget, bool druidSendsPet) =>
            !brainHoldsPetOffTarget && !druidSendsPet;

        /// <summary>
        /// Pure: the monster a Take adds pet works on. The add it already took stays its job until that add is dead or
        /// out of reach - the monster turning on the pet is the point of the order, and it stopped being "an add on a
        /// groupmate" the moment it did, so the pet was called back and sent again. Else a new add on its owner or on a
        /// groupmate who is no tank; else a monster hitting the pet itself (it defends itself); else nothing.
        /// </summary>
        public static T TakeAddsChoice<T>(T held, bool heldStillValid, T addOnGroup, T attackerOfPet) where T : class =>
            heldStillValid && held != null ? held : addOnGroup ?? attackerOfPet;

        /// <summary>Pure: the pet goes only at adds - Take adds, or Automatic for a pet class that holds its pet for adds
        /// (the Spiritmaster grouped with a tank, Q25); Attack never.</summary>
        public static bool PetTakesAdds(int job, bool automaticHoldsForAdds) =>
            job == PetTakeAdds || job == PetAutomatic && automaticHoldsForAdds;

        // ---- Weapon row for archers (M7, G6)

        /// <summary>The "Weapon" row for Scouts, Hunters and Rangers: 0 Automatic (by training), 1 bow, 2 melee.</summary>
        public const int WeaponAutomatic = 0, WeaponBow = 1, WeaponMelee = 2;

        /// <summary>
        /// Pure (G6; period: about nine in ten Hunters fought with a spear, Scouts out of arrows meleed): does an archer
        /// fight with its bow? Bow and Melee as chosen; Automatic by training - the bow while its archery line is trained
        /// at least as high as its melee weapon's line, otherwise melee (the bow still pulls). An untrained archery line
        /// (0) with no melee training either keeps the bow, as before.
        /// </summary>
        public static bool ArcherFightsWithBow(int choice, int archerySpec, int meleeSpec) => choice switch
        {
            WeaponBow => true,
            WeaponMelee => false,
            _ => archerySpec >= meleeSpec,
        };

        /// <summary>The archery line of a Scout (Longbows), Hunter (Composite Bow) or Ranger (Recurve Bow); null otherwise.</summary>
        public static string ArcheryLine(eCharacterClass characterClass) => characterClass switch
        {
            eCharacterClass.Scout => Specs.Longbow,
            eCharacterClass.Hunter => Specs.CompositeBow,
            eCharacterClass.Ranger => Specs.RecurveBow,
            _ => null,
        };

        // ---- Warden rows (M7)

        /// <summary>The Warden's Role row (stored key "role"): 0 Automatic (fight and heal), 1 off-healer (heals under 40%
        /// in a fight), 2 main healer (stays back and heals).</summary>
        public const int WardenRoleAutomatic = 0, WardenRoleOffHealer = 1, WardenRoleMainHealer = 2;

        /// <summary>Pure: a Warden, Friar or Shaman that stays back as the group's healer (grouped only). The Shaman's
        /// "Stay back and heal" is choice 1; the Friar's and Warden's "Main healer (stays back)" is choice 2.</summary>
        public static bool StaysBackAsHealer(eCharacterClass characterClass, int roleChoice, bool grouped) => grouped &&
            (characterClass == eCharacterClass.Shaman && roleChoice == 1 ||
             characterClass is eCharacterClass.Friar or eCharacterClass.Warden && roleChoice == 2);

        /// <summary>Pure: a Friar or Warden off-healer heals only emergencies while it fights.</summary>
        public static bool OffHealerInFight(eCharacterClass characterClass, int roleChoice, bool inCombat) =>
            inCombat && roleChoice == WardenRoleOffHealer && characterClass is eCharacterClass.Friar or eCharacterClass.Warden;

        // ---- Class ability rows (M7): Stag, Critical Shot, opening stun

        /// <summary>The "Stag" row: 0 Automatic (hard fights), 1 on cooldown, 2 off (as the Berserk row).</summary>
        public const int StagAutomatic = 0, StagOnCooldown = 1, StagOff = 2;

        /// <summary>Pure: a Hero starts Stag - on cooldown, or Automatic in a hard fight; never when Off.</summary>
        public static bool StagWanted(int choice, bool hardFight) => choice switch
        {
            StagOnCooldown => true,
            StagOff => false,
            _ => hardFight,
        };

        /// <summary>The on/off rows (Critical Shot, Opening stun): 0 Automatic (on), 1 off.</summary>
        public const int AbilityOff = 1;

        /// <summary>Pure: a Blademaster starts Triple Wield by its row - the same three choices and rule as Stag.</summary>
        public static bool TripleWieldWanted(int choice, bool hardFight) => StagWanted(choice, hardFight);

        /// <summary>Whether this companion may use Engage (the "Engage" row: 0 Automatic, 1 off; always for gamebots).</summary>
        public static bool EngageAllowed(GameBot bot) => CompanionOptions165.Choice(bot, "engage") != AbilityOff;

        /// <summary>Whether this companion may open with Critical Shot (always for gamebots).</summary>
        public static bool CriticalShotAllowed(GameBot bot) => CompanionOptions165.Choice(bot, "critshot") != AbilityOff;

        /// <summary>Whether this companion may use its opening stun on the kill target (always for gamebots).</summary>
        public static bool OpeningStunAllowed(GameBot bot) => CompanionOptions165.Choice(bot, "openstun") != AbilityOff;

        // ---- Shield swap for Slam (M7, G10)

        /// <summary>How long a two-hander tank stays on its shield set for one Slam at most.</summary>
        public const long SlamSwapMilliseconds = 8_000;

        /// <summary>
        /// Pure (G10; period Armsman: "50 pole / 42 shield - swap to sword and shield to Slam, then back to pole"): a tank
        /// fighting with its two-hander, carrying a usable weapon and shield set and a Slam it can use now, swaps to the
        /// shield for one Slam when a stun is wanted (the monster is casting, or it is on someone who is not the main tank)
        /// and the monster is not stun immune. The "Shield swap for Slam" row: 0 Automatic (on), 1 off.
        /// </summary>
        public static bool SlamSwapWanted(int choice, bool tank, bool twoHanderActive, bool shieldSetReady, bool slamUsable,
            bool stunWanted, bool targetStunImmune) =>
            choice != AbilityOff && tank && twoHanderActive && shieldSetReady && slamUsable && stunWanted && !targetStunImmune;

        /// <summary>Pure: the swap ends - the Slam landed (the monster is stunned), the swing with it went off, or time ran out.</summary>
        public static bool SlamSwapEnds(bool targetStunned, bool slamSwung, long elapsedMilliseconds) =>
            targetStunned || slamSwung || elapsedMilliseconds >= SlamSwapMilliseconds;

        // ---- A caster under attack walks to the tank (M7, G12)

        /// <summary>How near a tank must be for a caster to walk to it, and how close counts as "at its side".</summary>
        public const int CasterToTankReach = 1_000, CasterBesideTank = 150;

        /// <summary>
        /// Pure (G12; period: a caster with a monster on it steps back to the tank so the tank can taunt or protect): a pure
        /// caster hit in melee walks once per attacker to a living groupmate tank within 1,000 units, unless it already
        /// stands at the tank's side; otherwise (no tank, already walked, cornered) it fights back with its staff as before.
        /// </summary>
        public static bool CasterWalksToTank(bool pureCaster, bool tankAlive, int tankDistance, bool alreadyWalkedFromThisAttacker) =>
            pureCaster && tankAlive && !alreadyWalkedFromThisAttacker &&
            tankDistance > CasterBesideTank && tankDistance <= CasterToTankReach;

        /// <summary>Whether this companion's add control runs now (its own choice, and no Main CC to leave it to).</summary>
        public static bool AddControlRuns(GameBot bot)
        {
            int choice = CrowdControl(bot);
            return CcAllowsAddControl(choice) && !(choice == CcAutomatic && DefersToMainCc(choice, MainCcReady(bot)));
        }
    }
}
