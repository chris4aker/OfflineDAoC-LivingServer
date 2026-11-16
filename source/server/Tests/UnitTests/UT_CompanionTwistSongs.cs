using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Owner 2026-11-11: a companion AI option "Twist songs / chants" for Bards, Minstrels, Skalds and Paladins, default
    /// off. On, the companion cycles its songs or chants as before 2026-10-30; each switch plays without its sound.
    /// </summary>
    [TestFixture]
    public class UT_CompanionTwistSongs
    {
        [Test]
        public void OfferedToThePulsingSongAndChantClassesWithTwoKinds() => Assert.Multiple(() =>
        {
            foreach (eCharacterClass twister in new[] { eCharacterClass.Bard, eCharacterClass.Minstrel, eCharacterClass.Skald,
                         eCharacterClass.Paladin })
            {
                Assert.That(CompanionControls165.OffersTwist(twister, 2), Is.True, twister.ToString());
                Assert.That(CompanionControls165.OffersTwist(twister, 1), Is.False, twister + " with one song");
            }
            foreach (eCharacterClass other in new[] { eCharacterClass.Warden, eCharacterClass.Armsman, eCharacterClass.Cleric })
                Assert.That(CompanionControls165.OffersTwist(other, 3), Is.False, other.ToString());
        });

        [Test]
        public void TheRowDefaultsToHoldingOneSong()
        {
            // Living Server does not offer the twist row (1.2's song AI holds one song; nothing reads the choice)
            Assert.That(CompanionOptions165.Option(CompanionOptions165.KindTwist), Is.Null);
            Assert.That(CompanionControls165.TwistOn, Is.EqualTo(1));
            // a gamebot (no hire record) never twists
            Assert.That(CompanionControls165.TwistsSongs(null), Is.False);
        }

        [Test]
        public void TravellingTwistsOnlyTheTravelSongs() => Assert.Multiple(() =>
        {
            Assert.That(CompanionControls165.TwistPartner(eCharacterClass.Bard, eSpellType.EnduranceRegenBuff,
                eSpellType.SpeedEnhancement, true, true), Is.True);
            Assert.That(CompanionControls165.TwistPartner(eCharacterClass.Bard, eSpellType.PowerRegenBuff,
                eSpellType.SpeedEnhancement, true, true), Is.False);
            Assert.That(CompanionControls165.TwistPartner(eCharacterClass.Skald, eSpellType.HealthRegenBuff,
                eSpellType.SpeedEnhancement, true, true), Is.True);
        });

        [Test]
        public void StandingOrFightingNeverTwistsSpeedIn() => Assert.Multiple(() =>
        {
            Assert.That(CompanionControls165.TwistPartner(eCharacterClass.Minstrel, eSpellType.SpeedEnhancement,
                eSpellType.PowerRegenBuff, false, true), Is.False);
            Assert.That(CompanionControls165.TwistPartner(eCharacterClass.Minstrel, eSpellType.HealthRegenBuff,
                eSpellType.PowerRegenBuff, false, true), Is.True);
            Assert.That(CompanionControls165.TwistPartner(eCharacterClass.Minstrel, eSpellType.HealthRegenBuff,
                eSpellType.PowerRegenBuff, false, false), Is.False, "unaffordable");
            Assert.That(CompanionControls165.TwistPartner(eCharacterClass.Minstrel, eSpellType.PowerRegenBuff,
                eSpellType.PowerRegenBuff, false, true), Is.False, "the held song itself");
        });

        [Test]
        public void PaladinsTwistTheCombatHealOnlyWhenSomeoneIsHurtInAFight() => Assert.Multiple(() =>
        {
            Assert.That(CompanionControls165.PaladinTwistPartner(eSpellType.DamageAdd, eSpellType.EnduranceRegenBuff, false, true),
                Is.True);
            Assert.That(CompanionControls165.PaladinTwistPartner(eSpellType.CombatHeal, eSpellType.EnduranceRegenBuff, false, true),
                Is.False);
            Assert.That(CompanionControls165.PaladinTwistPartner(eSpellType.CombatHeal, eSpellType.EnduranceRegenBuff, true, true),
                Is.True);
        });

        [Test]
        public void OnlyASwitchIsSilent() => Assert.Multiple(() =>
        {
            Assert.That(CompanionControls165.QuietSwitch(true, true), Is.True);
            Assert.That(CompanionControls165.QuietSwitch(true, false), Is.False, "the first song plays its sound");
            Assert.That(CompanionControls165.QuietSwitch(false, true), Is.False, "holding one song is unchanged");
        });

        [Test]
        public void TheOldCycleReturnsToTheAnchorAndTwistsTheRunningLowSong()
        {
            var songs = new[]
            {
                new BotSongTwistPolicy.Song(1, 0, 0, 20_000),
                new BotSongTwistPolicy.Song(2, 0, 0, 500),
            };
            Assert.Multiple(() =>
            {
                Assert.That(BotSongTwistPolicy.Choose(1, 0, songs), Is.EqualTo(1), "nothing playing: the anchor");
                Assert.That(BotSongTwistPolicy.Choose(1, 1, songs), Is.EqualTo(2), "anchor up, partner low: twist it in");
                Assert.That(BotSongTwistPolicy.Choose(1, 2, songs), Is.EqualTo(1), "partner played: back to the anchor");
            });
        }
    }
}
