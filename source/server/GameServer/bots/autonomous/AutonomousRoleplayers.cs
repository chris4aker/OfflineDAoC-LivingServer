using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Roleplayers (bot_living_characters, docs/bot-voices/TALKING-UPDATE.md section 14): about 4% of the Socialite and
/// Explorer personalities (a stable roll on the bot id, about 1.6% of all bots) roleplay all the time. Everything they
/// say comes out in thee-and-thou speech ("art thou well, friend? aye, forsooth"), they post in-character musings in
/// /faction now and then, and the realm mocks them ("/rp off plz"), which they answer in character.
/// </summary>
public static class AutonomousRoleplayers
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool On => Properties.BOT_LIVING_CHARACTERS;

    public const double ShareOfTypes = 0.04;
    private const long Salt = 0x726f6c65706c6179;   // "roleplay"

    /// <summary>Whether a bot id roleplays: a Socialite or Explorer whose stable roll is under the share. Pure.</summary>
    public static bool IsRoleplayerId(long botId) => botId > 0 &&
        AutonomousBotPersonality.TypeFor(botId) is BotPersonalityType.Socialite or BotPersonalityType.Explorer &&
        AutonomousBotSchedule.Unit(botId, Salt) < ShareOfTypes;

    public static bool Is(GameBot bot) => On && bot?.IsAutonomousWorldBot == true && !bot.IsTemporaryGroupHelper &&
                                          !bot.IsEpicSpecialForce && IsRoleplayerId(bot.DatabaseID) && !AutonomousBeggars.Is(bot);

    #region Speech (pure, unit tested)

    // Whole words only, longest phrase first, in ONE pass (so "thank you" -> "my thanks" is not then read as "thanks").
    private static readonly (string Word, string With)[] OldeWords =
    [
        ("are you", "art thou"), ("r u", "art thou"), ("you are", "thou art"), ("u r", "thou art"), ("you're", "thou art"), ("youre", "thou art"),
        ("do you", "dost thou"), ("have you", "hast thou"), ("you have", "thou hast"), ("will you", "wilt thou"), ("can you", "canst thou"),
        ("thank you", "my thanks"), ("thanks", "my thanks"), ("thx", "my thanks"), ("ty", "my thanks"),
        ("your", "thy"), ("ur", "thy"), ("yours", "thine"), ("you", "thee"), ("u", "thee"),
        ("yes", "aye"), ("yeah", "aye"), ("yep", "aye"), ("yup", "aye"), ("ya", "aye"),
        ("no", "nay"), ("nope", "nay"), ("nah", "nay"),
        ("hello", "hail"), ("hi", "hail"), ("hey", "hail"), ("bye", "fare thee well"), ("cya", "fare thee well"),
        ("lol", "*chuckles*"), ("rofl", "*laughs heartily*"), ("heh", "*smiles*"), ("omg", "by the gods"), ("wtf", "what devilry"),
        ("gz", "well fought"), ("grats", "well fought"), ("brb", "i shall return anon"), ("afk", "*steps away*"),
        ("noob", "whelp"), ("newb", "whelp"), ("ok", "very well"), ("okay", "very well"), ("maybe", "perchance"),
        ("before", "ere"), ("often", "oft"), ("guys", "good folk"), ("dude", "good sir"), ("lfg", "seeking companions"),
        ("pls", "i pray thee"), ("plz", "i pray thee"), ("please", "i pray thee"), ("sorry", "forgive me"), ("sry", "forgive me"),
        ("mobs", "beasts"), ("mob", "beast"), ("gold", "coin"), ("noobs", "whelps"), ("stfu", "hold thy tongue"),
    ];

    private static readonly Dictionary<string, string> OldeMap = OldeWords.ToDictionary(pair => pair.Word, pair => pair.With, StringComparer.OrdinalIgnoreCase);

    private static readonly Regex Olde = new(
        @"(?<![a-z'])(" + string.Join("|", OldeWords.Select(pair => pair.Word).OrderByDescending(word => word.Length).Select(Regex.Escape)) + @")(?![a-z'])",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly string[] Flourishes = [", forsooth", ", m'lord", " *bows*", ", huzzah!", ", by my troth", ", verily", " *tips hat*"];

    /// <summary>A line in a roleplayer's thee-and-thou speech, with a flourish now and then. Pure for a given random.</summary>
    public static string Speak(string text, Random random)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;
        string result = Olde.Replace(text, match => OldeMap.TryGetValue(match.Value, out string with) ? with : match.Value);
        if (random.NextDouble() < 0.3 && result.Length < 110 && !result.EndsWith('*'))
            result = result.TrimEnd('.', '!', '?', ' ') + Flourishes[random.Next(Flourishes.Length)];
        return result.Length <= 160 ? result : text;
    }

    #endregion

    /// <summary>A roleplayer's line in its speech; anyone else's unchanged. Called where bot lines go out.</summary>
    public static string Voice(GameBot bot, string text) => Is(bot) ? Speak(text, Random.Shared) : text;

    /// <summary>A /faction line by name (debates and other faction speech): a roleplayer's comes out in its speech.</summary>
    public static string VoiceByName(string speakerName, eRealm realm, string text)
    {
        if (!On || string.IsNullOrEmpty(speakerName) || !AutonomousBotRegistry.TryGetOnline(speakerName, realm, out GameBot bot) || !Is(bot))
            return text;
        MaybeMocked(bot, realm);
        return Speak(text, Random.Shared);
    }

    private static readonly ConcurrentDictionary<eRealm, long> NextAmbientAt = new();
    private static readonly ConcurrentDictionary<eRealm, long> LastMockAt = new();
    public const long AmbientMinMs = 20 * 60_000, AmbientSpreadMs = 25 * 60_000, MockRestMs = 4 * 60_000;

    /// <summary>GM test (/lstest roleplayer): an online roleplayer of the realm speaks on the next pulse. Returns how many are online.</summary>
    public static int SpeakSoon(eRealm realm)
    {
        NextAmbientAt[realm] = 0;
        LastMockAt.TryRemove(realm, out _);
        return AutonomousBotRegistry.Snapshot().Count(bot => bot.Realm == realm && Is(bot) && AutonomousLivingServer.CanTalk(bot));
    }

    /// <summary>A new play session (AutonomousLivingWorld): a roleplayer's first line comes 6-18 minutes in.</summary>
    public static void OnSessionStart(eRealm realm, long now) =>
        NextAmbientAt[realm] = now + 6 * 60_000 + Random.Shared.Next(12 * 60_000);

    /// <summary>Now and then an online roleplayer posts an in-character line in /faction (AutonomousLivingWorld.Pulse).</summary>
    public static void Pulse(long now)
    {
        if (!On || !AutonomousBotVoices.Enabled)
            return;
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            if (!AutonomousBotChatCoordinator.HasFactionAudience(realm))
                continue;
            long next = NextAmbientAt.GetOrAdd(realm, _ => now + AmbientMinMs / 2 + Random.Shared.Next((int)AmbientSpreadMs));
            if (now < next || AutonomousBotDebates.IsRunning(realm))
                continue;
            NextAmbientAt[realm] = now + AmbientMinMs + Random.Shared.Next((int)AmbientSpreadMs);
            GameBot rp = AutonomousBotRegistry.Snapshot().Where(bot => bot.Realm == realm && Is(bot) && AutonomousLivingServer.CanTalk(bot))
                .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
            if (rp == null || !AutonomousBotVoices.TryEventLine(rp, "roleplay", out string line, "ambient"))
                continue;
            // Already in character: sent as written (not through the speech pass).
            AutonomousBotChatCoordinator.BroadcastFactionAsWritten(rp.Name, realm, line);
            AutonomousBotChatCoordinator.Note(AutonomousChatThreads.Faction(realm), rp, line, "chat");
            Log.Info($"ROLEPLAY_AMBIENT realm={GlobalConstants.RealmToName(realm)} bot=\"{rp.Name}\"");
            if (Random.Shared.NextDouble() < 0.6)
                Mock(rp, realm, now);
        }
    }

    private static void MaybeMocked(GameBot rp, eRealm realm)
    {
        if (Random.Shared.NextDouble() < 0.08)
            Mock(rp, realm, GameLoop.GameLoopTime);
    }

    /// <summary>Another bot mocks the roleplayer, and the roleplayer answers in character.</summary>
    private static void Mock(GameBot rp, eRealm realm, long now)
    {
        if (LastMockAt.TryGetValue(realm, out long last) && now - last < MockRestMs)
            return;
        GameBot mocker = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot != rp && bot.Realm == realm && !Is(bot) && !AutonomousBeggars.Is(bot) && AutonomousLivingServer.CanTalk(bot))
            .OrderBy(_ => Random.Shared.Next()).FirstOrDefault();
        if (mocker == null)
            return;
        LastMockAt[realm] = now;
        int delay = 5_000 + Random.Shared.Next(8_000);
        AutonomousLivingServer.Later(mocker, delay, () => AutonomousLivingServer.CanTalk(mocker), () =>
        {
            if (!AutonomousBotVoices.TryEventLine(mocker, "roleplay", out string mock, "mock", new Dictionary<string, string> { ["target"] = rp.Name }))
                return;
            AutonomousBotChatCoordinator.BroadcastFaction(mocker.Name, realm, mock);
            Log.Info($"ROLEPLAY_MOCK realm={GlobalConstants.RealmToName(realm)} bot=\"{rp.Name}\" mocker=\"{mocker.Name}\"");
            AutonomousLivingServer.Later(rp, 6_000 + Random.Shared.Next(8_000), () => AutonomousLivingServer.CanTalk(rp), () =>
            {
                if (AutonomousBotVoices.TryEventLine(rp, "roleplay", out string answer, "answer", new Dictionary<string, string> { ["target"] = mocker.Name }))
                    AutonomousBotChatCoordinator.BroadcastFactionAsWritten(rp.Name, realm, answer);
            });
        });
    }
}
