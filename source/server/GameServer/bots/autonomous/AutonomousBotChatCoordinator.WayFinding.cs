using System;

namespace DOL.GS;

/// <summary>Bots as people WP4: a bot's real question about the way (BotWayFinding.Ask).</summary>
public static partial class AutonomousBotChatCoordinator
{
    /// <summary>
    /// A gamebot that does not know where it is going asks the way, in /faction or in /say near a real player: the Travel
    /// scene's line as its real question (AutonomousChatFieldTalk.WayQuestion). It goes through the same path as a field line:
    /// the channel's ceiling with room for the answer, the near-copy check, the thread (only bots that know the place answer,
    /// BotWayFinding.MayAnswer) and the reply budget. The ambient gap does not hold it back: it is an event, as a story is. The
    /// realm's 10 minutes between asks in /faction are BotWayFinding's.
    /// </summary>
    internal static BotWayAskOutcome AskTheWay(GameBot asker, string destination, bool faction)
    {
        if (!AutonomousBotVoices.Enabled || !Eligible(asker) || string.IsNullOrWhiteSpace(destination) || AutonomousTales.IsTelling(asker.DatabaseID))
            return BotWayAskOutcome.Off;
        if (faction ? !HasFactionAudience(asker.Realm) || AutonomousBotDebates.IsRunning(asker.Realm) : !HasLocalAudience(asker))
            return BotWayAskOutcome.Unheard;
        if (!RoomToAnswer(asker, faction))
        {
            AutonomousChatWhy.CountHeld(faction ? AutonomousChatBudget.Channel.Faction : AutonomousChatBudget.Channel.Say, AutonomousChatWhy.Held.Ceiling);
            return BotWayAskOutcome.Held;
        }
        var random = Random.Shared;
        long now = GameLoop.GameLoopTime;
        AutonomousChatFieldTalk.Said said = AutonomousChatFieldTalk.WayQuestion(AutonomousChatFieldTalk.Of(asker, random), destination, random,
            AutonomousChatFieldTalk.MemoryOf(asker.Realm), now);
        if (said == null)
            return BotWayAskOutcome.Held;
        var choice = new AmbientChoice(faction ? AutonomousChatBudget.Channel.Faction : AutonomousChatBudget.Channel.Say,
            faction ? (long)asker.Realm : asker.CurrentRegionID, (0, 0, 0), 0);
        return SayField(asker, faction ? "faction" : "say", choice, "it asks the way to a place it does not know (bots as people)", said)
            ? BotWayAskOutcome.Said
            : BotWayAskOutcome.Held;
    }
}
