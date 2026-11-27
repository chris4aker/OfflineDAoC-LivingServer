using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS.PacketHandler;

namespace DOL.GS;

/// <summary>
/// #528 (RvR work 10, "after the kill"; docs/rvr-audit-1.2.3/war.md finding 10, reference.md 7): what follows an RvR kill,
/// from the one kill event both death paths share (<see cref="AutonomousBotRealmPointRewards.Award"/> for a gamebot victim,
/// <see cref="AutonomousBotRealmPointRewards.PayGamebotsForPlayerKill"/> for a real player). The roam rules hear the death
/// (the move-on counts the player too), and now and then one nearby killer emotes over the corpse. Autonomous gamebots
/// only: companions and players are never made to emote or move on.
/// </summary>
public static class RvrAfterTheKill
{
    /// <summary>A killer emotes over a corpse this often (one killer, one emote per corpse).</summary>
    public const double EmoteChance = 0.45;
    /// <summary>After a one-on-one, this share of the emotes is a bow (a fair duel's respect, reference.md 7).</summary>
    public const double DuelBowShare = 0.6;
    /// <summary>A killer this close to the corpse may emote over it.</summary>
    public const int EmoteRadius = 1_500;
    /// <summary>A second death event for the same victim this soon is the same corpse.</summary>
    public const long CorpseMilliseconds = 30_000;

    /// <summary>
    /// The taunts over an enemy corpse: mostly a laugh, then rude gestures. The period's /spit has no animation in this
    /// client's emote set (<see cref="eEmote"/>), so a throat-slit, a taunt, a victory and a cheer stand in beside them.
    /// </summary>
    public static readonly eEmote[] Taunts =
    [
        eEmote.Laugh, eEmote.Laugh, eEmote.Laugh, eEmote.Rude, eEmote.Rude, eEmote.Taunt, eEmote.Slit, eEmote.Victory, eEmote.Cheer,
    ];

    /// <summary>
    /// The emote over a corpse, or null for none: <paramref name="chanceRoll"/> under <see cref="EmoteChance"/> emotes;
    /// after a one-on-one mostly a <see cref="eEmote.Bow"/>, else a taunt picked by <paramref name="pickRoll"/>. Rolls in
    /// [0, 1). Pure.
    /// </summary>
    public static eEmote? Choose(bool oneOnOne, double chanceRoll, double pickRoll)
    {
        if (chanceRoll >= EmoteChance)
            return null;
        // The pick roll is spread again over the taunts after the bow share, so both parts use the whole roll.
        double pick = Math.Clamp(pickRoll, 0, 0.999999);
        if (oneOnOne)
        {
            if (pick < DuelBowShare)
                return eEmote.Bow;
            pick = (pick - DuelBowShare) / (1 - DuelBowShare);
        }
        return Taunts[(int)(pick * Taunts.Length)];
    }

    /// <summary>
    /// "Never twice on the same corpse": true the first time a corpse is claimed. A claim for the same victim within
    /// <see cref="CorpseMilliseconds"/> of the last is the same corpse. Keyed weakly, so a logged-out victim takes its
    /// claim with it.
    /// </summary>
    public sealed class CorpseClaims
    {
        private readonly ConditionalWeakTable<object, StrongBox<long>> _claims = new();

        public bool TryClaim(object victim, long deathAt)
        {
            if (victim == null)
                return false;
            StrongBox<long> last = _claims.GetValue(victim, _ => new StrongBox<long>(long.MinValue));
            lock (last)
            {
                if (last.Value != long.MinValue && deathAt - last.Value < CorpseMilliseconds && deathAt >= last.Value)
                    return false;
                last.Value = deathAt;
                return true;
            }
        }
    }

    private static readonly CorpseClaims Corpses = new();

    /// <summary>
    /// The kill event: <paramref name="victim"/> (a gamebot or a real player) died with <paramref name="hostile"/> as its
    /// enemy-realm contributors (pets resolved to their owners). Cosmetic and bookkeeping only: never throws.
    /// </summary>
    public static void Killed(GameLiving victim, IReadOnlyDictionary<GameLiving, double> hostile, long now)
    {
        if (victim == null || hostile == null || hostile.Count == 0)
            return;
        try
        {
            AutonomousWorldBotController.NoteRvrRoamKill(victim, hostile.Keys, now);
        }
        catch (Exception)
        {
            // The roam rules' bookkeeping must never break a death.
        }
        try
        {
            Emote(victim, hostile, now);
        }
        catch (Exception)
        {
            // Cosmetic.
        }
    }

    private static bool MayEmote(GameBot bot, GameLiving corpse) =>
        bot is { IsAutonomousWorldBot: true, IsTemporaryGroupHelper: false, IsAlive: true } && bot.DatabaseID > 0 &&
        bot.ObjectState == GameObject.eObjectState.Active && !bot.IsCasting && !bot.IsStealthed && !bot.IsCrowdControlled &&
        bot.CurrentRegionID == corpse.CurrentRegionID && bot.IsWithinRadius(corpse, EmoteRadius);

    private static void Emote(GameLiving victim, IReadOnlyDictionary<GameLiving, double> hostile, long now)
    {
        // The body language switch (bot_living_body_language) covers it, and only where a real player can see the corpse.
        if (!AutonomousBotBodyLanguage.On || victim.CurrentRegion == null || !AutonomousBotBodyLanguage.IsWatched(victim))
            return;
        GameBot[] killers = hostile.Keys.OfType<GameBot>().Where(bot => bot.Realm != victim.Realm && MayEmote(bot, victim)).ToArray();
        if (killers.Length == 0)
            return;
        eEmote? emote = Choose(hostile.Count == 1, Random.Shared.NextDouble(), Random.Shared.NextDouble());
        if (emote == null || !Corpses.TryClaim(victim, now))
            return;
        GameBot emoter = killers[Random.Shared.Next(killers.Length)];
        eEmote chosen = emote.Value;
        // Checked again when it is due: a killer that has gone on to the next fight (attacking again) does not turn away to emote.
        AutonomousLivingServer.Later(emoter, 900 + Random.Shared.Next(1_800), () => !victim.IsAlive && !emoter.IsAttacking && MayEmote(emoter, victim), () =>
        {
            BotTurning.TurnGradually(emoter, victim);
            // GameBot.Emote sends the animation and its line ("Brannoc laughs at you.") through the say gate (#475).
            emoter.Emote(chosen, victim);
            AutonomousBotBodyLanguage.CountKillEmote();
        });
    }
}
