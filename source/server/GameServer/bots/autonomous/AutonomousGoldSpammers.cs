using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

/// <summary>
/// Gold spammers (bot_living_characters, docs/bot-voices/TALKING-UPDATE.md section 14): every hour or two while a player
/// of the realm is online, a throwaway character with a gibberish name floods /faction with gold-selling ads for a fake
/// site, the realm's bots react ("reported", "/ignore"), and a few minutes later it is "banned": the ads stop and the bots
/// cheer. A tell to the spammer gets more spam back while it is around. The spammer is not a gamebot or a character in
/// the world: only a name in /faction. Every site is made up and spelled with zeros ("c 0 m"), so none is a real address.
/// </summary>
public static class AutonomousGoldSpammers
{
    private static readonly Logger Log = LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

    public static bool On => Properties.BOT_LIVING_CHARACTERS;

    public const long FirstWaveMinMs = 12 * 60_000, FirstWaveSpreadMs = 18 * 60_000;
    /// <summary>Ads in a wave before the ban: 5-10, 18-48 seconds apart (it was 3-6, gone before anyone noticed).</summary>
    public const int AdsMin = 5, AdsSpread = 6;
    public const long WaveMinMs = 60 * 60_000, WaveSpreadMs = 90 * 60_000;

    private sealed class Wave
    {
        public string Name;
        public string Site;
        public int AdsLeft;
        public int AdsSaid;
        public long NextAt;
        public bool Banned;
        /// <summary>The next ad's exchange (its reactions share it); the first one's is in the LIVING_FEATURE_DUE line.</summary>
        public long Exchange;
        /// <summary>Whether an ad was said (GOLD_SPAMMER_START is logged then, not when the wave is made).</summary>
        public bool Started;
        public long DueSince;
        /// <summary>When the ad now waiting was first held (0: none waiting).</summary>
        public long HeldSince;
    }

    private static readonly ConcurrentDictionary<eRealm, Wave> Waves = new();
    private static readonly ConcurrentDictionary<eRealm, long> NextWaveAt = new();

    #region Pure parts (unit tested)

    private const string Consonants = "bcdfghjklmnpqrstvwxz";
    private const string Vowels = "aeiouy";

    /// <summary>A gibberish throwaway name like 2003's gold spammers had: "Xkqhoaa", "Fghtyrrr". Pure for a given random.</summary>
    public static string SpammerName(Random random)
    {
        var name = new StringBuilder();
        int length = 7 + random.Next(5);
        while (name.Length < length)
        {
            if (random.Next(3) == 0)
                name.Append(Vowels[random.Next(Vowels.Length)]);
            else
            {
                char c = Consonants[random.Next(Consonants.Length)];
                name.Append(c);
                if (random.Next(5) == 0)
                    name.Append(c);
            }
        }
        string text = name.ToString(0, Math.Min(name.Length, 12));
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static readonly string[] SiteWords = ["g0ldg0blin", "fastg0ld", "pl4tking", "c0inbarrel", "g0ldm4rket", "mmog0ld4u",
        "cheapg0ldz", "g0ldgryph0n", "platwagon", "g0ldbucket", "realmc0in", "fastplatz"];
    private static readonly string[] SiteEnds = ["c 0 m", "c0m", "n 3 t", "n3t", "0rg"];

    /// <summary>A made-up site, always with zeros or threes for letters so it is never a real address. Pure for a given random.</summary>
    public static string FakeSite(Random random)
    {
        string word = SiteWords[random.Next(SiteWords.Length)];
        string end = SiteEnds[random.Next(SiteEnds.Length)];
        return random.Next(3) switch
        {
            0 => $"w w w . {string.Join(" ", word.ToUpperInvariant().ToCharArray())} . {end.ToUpperInvariant()}",
            1 => $"www.{word}.{end.Replace(" ", string.Empty)}",
            _ => $"{word.ToUpperInvariant()} (dot) {end.Replace(" ", string.Empty).ToUpperInvariant()}",
        };
    }

    /// <summary>True for a site this code makes: it has a 0 or 3 standing for a letter in its ending. Pure.</summary>
    public static bool IsObviouslyFake(string site) =>
        !string.IsNullOrEmpty(site) && (site.Contains("c0m", StringComparison.OrdinalIgnoreCase) || site.Contains("c 0 m", StringComparison.OrdinalIgnoreCase) ||
                                        site.Contains("n3t", StringComparison.OrdinalIgnoreCase) || site.Contains("n 3 t", StringComparison.OrdinalIgnoreCase) ||
                                        site.Contains("0rg", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// When a wave tries its next ad, after this one was said or held (1.1.3 RC1, live check 2026-11-08: a held ad was spent and
    /// the next came 18-48 s later, so a spammer that turned up as a scene began, with the channel at its ceiling after it, never
    /// spoke). A said ad spends one and the next comes 18-48 s on; a held one isn't spent and is tried again in 3 s, until it has
    /// waited <see cref="AutonomousChatExchange.DueWaitMs"/> outside a scene (null: give up; <see cref="AutonomousChatExchange.AfterTry"/>). Pure
    /// for a given random.
    /// </summary>
    public static long? NextAdIn(bool said, string held, long heldSince, long sceneEndedAt, long now, Random random) =>
        said ? 18_000 + random.Next(30_000)
        : AutonomousChatExchange.AfterTry(false, held, heldSince, sceneEndedAt, now) == AutonomousChatExchange.DueStep.GiveUp ? null
        : AutonomousChatExchange.DueRetryMs;

    #endregion

    /// <summary>GM test (/lstest spammer): the realm's next wave starts on the next pulse. False when one is already running.</summary>
    public static bool StartSoon(eRealm realm)
    {
        if (Waves.ContainsKey(realm))
            return false;
        NextWaveAt[realm] = 0;
        return true;
    }

    /// <summary>A new play session (AutonomousLivingWorld): the first wave comes 12-30 minutes in.</summary>
    public static void OnSessionStart(eRealm realm, long now)
    {
        if (!Waves.ContainsKey(realm))
            NextWaveAt[realm] = now + FirstWaveMinMs + Random.Shared.Next((int)FirstWaveSpreadMs);
    }

    /// <summary>Starts, runs and bans the spam waves (AutonomousLivingWorld.Pulse, once a second).</summary>
    public static void Pulse(long now)
    {
        if (!On || !AutonomousBotVoices.Enabled)
            return;
        foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
        {
            if (!AutonomousBotChatCoordinator.HasFactionAudience(realm))
            {
                // Nobody left to hear a spammer that hasn't said anything yet: it never came.
                if (Waves.TryGetValue(realm, out Wave waiting) && !waiting.Started && Waves.TryRemove(realm, out _))
                {
                    NextWaveAt[realm] = now + FirstWaveMinMs + Random.Shared.Next((int)FirstWaveSpreadMs);
                    Log.Info(AutonomousChatExchange.DroppedLog("spammer", realm, waiting.Exchange, now - waiting.DueSince, "no_audience"));
                }
                continue;
            }
            if (Waves.TryGetValue(realm, out Wave wave))
            {
                if (now >= wave.NextAt)
                    Advance(realm, wave, now);
                continue;
            }
            long next = NextWaveAt.GetOrAdd(realm, _ => now + FirstWaveMinMs + Random.Shared.Next((int)FirstWaveSpreadMs));
            if (now < next || AutonomousBotDebates.IsRunning(realm))
                continue;
            string name;
            do name = SpammerName(Random.Shared);
            while (AutonomousBotRegistry.TryGetByName(name, out _) || ClientService.Instance.GetPlayerByExactName(name) != null);
            var made = new Wave
            {
                Name = name, Site = FakeSite(Random.Shared), AdsLeft = AdsMin + Random.Shared.Next(AdsSpread), NextAt = now,
                Exchange = AutonomousChatExchange.NewId(), DueSince = now,
            };
            Waves[realm] = made;
            // Due, not started: GOLD_SPAMMER_START comes with its first ad (Advance).
            Log.Info(AutonomousChatExchange.DueLog("spammer", realm, made.Exchange, $" name=\"{name}\""));
        }
    }

    private static void Advance(eRealm realm, Wave wave, long now)
    {
        if (wave.Banned)
        {
            Waves.TryRemove(realm, out _);
            NextWaveAt[realm] = now + WaveMinMs + Random.Shared.Next((int)WaveSpreadMs);
            return;
        }
        if (wave.AdsLeft > 0)
        {
            bool written = AutonomousBotVoices.TryLineFor(wave.Name, realm, "spam", "ad", new Dictionary<string, string> { ["site"] = wave.Site }, out string ad);
            bool said;
            using (AutonomousChatExchange.Tag("spammer", wave.Exchange, 0, "spammer"))
                said = written && AutonomousBotChatCoordinator.BroadcastFaction(wave.Name, realm, ad);
            string held = written ? AutonomousBotChatCoordinator.LastHeld : "no_line";
            if (said)
            {
                if (!wave.Started)
                {
                    wave.Started = true;
                    Log.Info($"GOLD_SPAMMER_START realm={GlobalConstants.RealmToName(realm)} name=\"{wave.Name}\" waited_s={(now - wave.DueSince) / 1_000}");
                }
                wave.AdsLeft--;
                wave.AdsSaid++;
                wave.HeldSince = 0;
                long adExchange = wave.Exchange;
                wave.Exchange = AutonomousChatExchange.NewId();
                if (wave.AdsSaid <= 3 && Random.Shared.NextDouble() < 0.8)
                    React(realm, wave.Name, "react", 1 + Random.Shared.Next(2), 4_000, adExchange, 1, "reactor");
                wave.NextAt = now + NextAdIn(true, null, 0, 0, now, Random.Shared)!.Value;
                return;
            }
            // Held (a scene, the ceiling, the spacing after a scene): the ad isn't spent; it keeps its turn.
            if (wave.HeldSince == 0)
                wave.HeldSince = wave.Started ? now : wave.DueSince;
            if (NextAdIn(false, held, wave.HeldSince, AutonomousBotDebates.LastSceneEnd(realm), now, Random.Shared) is long retry)
            {
                wave.NextAt = now + retry;
                return;
            }
            if (!wave.Started)
            {
                // It never got a word in: no wave, no ban, no cheers; another comes in 12-30 minutes.
                Waves.TryRemove(realm, out _);
                NextWaveAt[realm] = now + FirstWaveMinMs + Random.Shared.Next((int)FirstWaveSpreadMs);
                Log.Info(AutonomousChatExchange.DroppedLog("spammer", realm, wave.Exchange, now - wave.DueSince, held));
                return;
            }
            // Started, then kept out for minutes: banned now (the realm reported it).
            wave.AdsLeft = 0;
        }
        // Banned: the ads stop and the realm cheers.
        wave.Banned = true;
        wave.NextAt = now + 60_000;
        Log.Info($"GOLD_SPAMMER_BANNED realm={GlobalConstants.RealmToName(realm)} name=\"{wave.Name}\" ads={wave.AdsSaid}");
        React(realm, wave.Name, "banned", 1 + Random.Shared.Next(3), 30_000 + Random.Shared.Next(60_000), AutonomousChatExchange.NewId(), 0, "speaker");
    }

    /// <summary>The realm's bots on the spammer: reactions to an ad (lines 1.. of its exchange), or cheers at the ban (their own).</summary>
    private static void React(eRealm realm, string spammer, string topic, int count, int firstDelayMs, long exchange, int firstIndex, string role)
    {
        GameBot[] bots = AutonomousBotRegistry.Snapshot()
            .Where(bot => bot.Realm == realm && AutonomousLivingServer.CanTalk(bot) && !AutonomousBeggars.Is(bot))
            .OrderBy(_ => Random.Shared.Next()).Take(count).ToArray();
        int due = firstDelayMs, index = firstIndex;
        foreach (GameBot bot in bots)
        {
            due += 2_500 + Random.Shared.Next(8_000);
            GameBot speaker = bot;
            int line = index++;
            AutonomousLivingServer.Later(speaker, due, () => AutonomousLivingServer.CanTalk(speaker), () =>
            {
                if (!AutonomousBotVoices.TryEventLine(speaker, "spam_react", out string text, topic,
                        new Dictionary<string, string> { ["target"] = spammer }))
                    return;
                using var tag = AutonomousChatExchange.Tag("spammer", exchange, line, role, speaker);
                AutonomousBotChatCoordinator.BroadcastFaction(speaker.Name, realm, text);
            });
        }
    }

    /// <summary>
    /// A player's /send to a name that is no character: if it is the realm's spammer (still spamming), it answers with spam.
    /// True when it was the spammer (the caller shows the tell as sent).
    /// </summary>
    public static bool TryAnswerTell(GamePlayer player, string name, string message)
    {
        if (!On || player?.Client == null || string.IsNullOrWhiteSpace(name) ||
            !Waves.TryGetValue(player.Realm, out Wave wave) || wave.Banned ||
            !string.Equals(wave.Name, name.Trim(), StringComparison.OrdinalIgnoreCase))
            return false;
        BotChatTranscript.Player("tell", player, message, wave.Name);
        string spammer = wave.Name, site = wave.Site;
        _ = new ECSGameTimer(player, _ =>
        {
            if (player.Client != null &&
                AutonomousBotVoices.TryLineFor(spammer, player.Realm, "spam", "tell",
                    new Dictionary<string, string> { ["site"] = site, ["player"] = player.Name }, out string reply))
            {
                ChatUtil.SendSendMessage(player.Client, "Social.ReceiveMessage.Private.Sends", spammer, reply);
                BotChatTranscript.Bot("tell", spammer, player.Realm, reply, player.Name);
            }
            return 0;
        }, 1_500 + Random.Shared.Next(2_500));
        return true;
    }

    /// <summary>The realm's spammer now, or null (tests and the log).</summary>
    public static string CurrentSpammer(eRealm realm) => Waves.TryGetValue(realm, out Wave wave) && !wave.Banned ? wave.Name : null;
}
