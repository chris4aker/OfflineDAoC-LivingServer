using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;

namespace DOL.GS.Commands;

// /lstest, the GM's tool for checking Living Server in game (owner 2026-10-28: "a GM test of Leeroy and more of the chat
// and memory"). Each subcommand makes one feature's next moment due now, so it runs through the feature's own code with
// its own lines, checks and logging; nothing is faked. It works in the GM's realm and around the GM.
[CmdAttribute(
    "&lstest",
    ePrivLevel.GM,
    "Fire a Living Server moment now, to check it in game",
    "/lstest list - every part, on or off",
    "/lstest storyline | debate | dunce - the next /faction scene starts now (a storyline episode, any debate, a dunce rage quit)",
    "/lstest spammer | roleplayer | rumour - a gold spammer, a roleplayer's line, a rumour about a rare named in this region",
    "/lstest tale [boss] [raid] | fireside - the realm's storyteller tells a tale in /faction now (of that boss: legion, golestandt, ...; with raid: the rumour after it is the raid, when it can form); fireside: sit down in a boss's place with a gamebot in your group and a groupmate tells the short one",
    "/lstest tell - a bot starts a tell to you",
    "/lstest memory - what bots remember about you",
    "/lstest remember - your target (or the nearest gamebot) remembers a good group with you; walk away and back to be greeted",
    "/lstest greet | gossip - a remembered bot near you greets you; a bot that likes you mentions you in /faction",
    "/lstest deed <solo|group|streak|ganked|silly|fell|drowned> - a bot near you tells the realm it saw that",
    "/lstest holiday <name|off> - bots treat today as that holiday (newyear valentines stpatricks aprilfools easter midsummer halloween thanksgiving christmas)",
    "/lstest leeroy - the next realm raid has a Leeroy; start one with /lstest raid",
    "/lstest leeroy now - a raid with a Leeroy gathering for its final boss charges at once (within the next 10 minutes, once)",
    "/lstest raid [dragon] [alb|mid|hib] - a realm's automatic raid opens now, sized to its free level 50s (launcher raids need 300)",
    "/lstest lore - which lore place you are in; its first-entry, area and lair lines may show again (atmosphere lines)",
    "/lstest craft | wtb | commission - Crafting 2.0 parts, as /lscraft (what is built, what is not yet)",
    "/lstest skill | lost | ask | milestone | wall - Bots as people (target a gamebot): its skill profile; its next unknown trip takes a wrong road; it asks the way now; its milestone reaches its next talk point; bots at the nearest held keep take the walls",
    "/lstest deathanim [raise | release | off] - the death fall in the original client (target a bot): kills it (or shows the fall again on a dead one) and lists every packet about it to your client for 2 minutes; raise brings it back as a rez does, at your side; release sends it to its bind")]
public sealed class LivingServerTestCommandHandler : AbstractCommandHandler, ICommandHandler
{
    public void OnCommand(GameClient client, string[] args)
    {
        GamePlayer gm = client.Player;
        eRealm realm = gm.Realm;
        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
        string arg = args.Length > 2 ? args[2].ToLowerInvariant() : string.Empty;
        string why;
        switch (sub)
        {
            case "list":
                List(client);
                return;
            case "storyline":
                int open = AutonomousBotStorylines.MakeDueNow(realm, out string storyNote);
                if (storyNote != null)
                    Say(client, char.ToUpper(storyNote[0]) + storyNote[1..] + ".");
                Scene(client, realm, null, open > 0 ? $"the next episode of {open} storyline(s) under way" : "a new storyline's first episode");
                return;
            case "debate":
                Scene(client, realm, "", "a debate");
                return;
            case "dunce":
                Scene(client, realm, "dunce", "a dunce rage quit");
                return;
            case "spammer":
                Say(client, Off(Properties.BOT_LIVING_CHARACTERS, "bot_living_characters") ??
                    (AutonomousGoldSpammers.StartSoon(realm) ? $"A gold spammer starts in /faction {WhenFaction(realm)}." : "A spammer is already at it in your realm."));
                return;
            case "roleplayer":
                int roleplayers = AutonomousRoleplayers.SpeakSoon(realm);
                Say(client, Off(Properties.BOT_LIVING_CHARACTERS, "bot_living_characters") ??
                    (roleplayers > 0 ? $"One of {roleplayers} roleplayer(s) online speaks in /faction {WhenFaction(realm)} (and may be mocked)." : "No roleplayer of your realm is online and free to talk."));
                return;
            case "tale":
                if (Off(Properties.BOT_LIVING_TALES, "bot_living_tales") is string talesOff)
                    Say(client, talesOff);
                else
                {
                    // "/lstest tale golestandt raid": the rumour after it is the raid, without the roll, when the raid can form.
                    bool raid = args.Length > 3 && args[^1].Equals("raid", StringComparison.OrdinalIgnoreCase);
                    string[] words = args.Skip(2).Take(args.Length - 2 - (raid ? 1 : 0)).ToArray();
                    string about = words.Length > 0 ? string.Join(' ', words) : null;
                    string loreId = about == null ? null : AutonomousTales.RequestedLore(about,
                        LoreBible.Current.Bosses.Select(boss => (boss.Id, LoreBible.NamesOf(boss.Name, boss.Id, boss.Aliases)))
                            .Concat(AutonomousTales.Legends.Select(legend => (legend.Key, (IEnumerable<string>)legend.Value))));
                    if (!AutonomousTales.TryStart(realm, loreId, null, out string noTale))
                        Say(client, $"No tale: {noTale}.");
                    else
                    {
                        string noRaid = raid ? AutonomousTales.AskRaidRumour(realm, loreId) : null;
                        Say(client, $"The realm's storyteller is asked for a tale in /faction{(loreId != null ? " (" + loreId + ")" : "")}; it starts within a few seconds." +
                            (!raid ? "" : noRaid == null ? " The rumour after it will be the raid, if the raid can still form then."
                                : $" The rumour after it will be the raid if it can form then; right now it cannot: {noRaid}."));
                    }
                }
                return;
            case "fireside":
                if (Off(Properties.BOT_LIVING_TALES, "bot_living_tales") is string firesideOff)
                    Say(client, firesideOff);
                else
                {
                    AutonomousTales.ForgetFiresides(gm);
                    Say(client, "Fireside tales forgotten for today. Sit down out of combat, in a group with a gamebot, in a place that has a tale (a dragon's zone, an epic dungeon, Darkness Falls, Summoner's Hall): a groupmate tells it within ten seconds.");
                }
                return;
            case "rumour":
            case "rumor":
                if (Off(Properties.BOT_LIVING_RUMOURS, "bot_living_rumours") is string rumoursOff)
                    Say(client, rumoursOff);
                else
                    Say(client, AutonomousRumours.TellNow(gm, out string about) ? $"A bot tells /faction about {about}." : $"No rumour: {about}.");
                return;
            case "tell":
                Say(client, AutonomousLivingServer.TellNow(gm, out why) is { } tell
                    ? $"{tell.Bot.Name} sends you a tell now ({tell.Topic.Replace('_', ' ')})."
                    : $"No tell: {why}.");
                return;
            case "memory":
                Memory(client, gm);
                return;
            case "remember":
                Remember(client, gm);
                return;
            case "greet":
                Say(client, AutonomousLivingServer.GreetNow(gm, out why) is GameBot greeter
                    ? $"{greeter.Name} greets you in /say within a few seconds."
                    : $"No greeting: {why}.");
                return;
            case "gossip":
                Say(client, AutonomousLivingServer.GossipNow(gm, out why) is GameBot gossip
                    ? $"{gossip.Name} mentions you in /faction."
                    : $"No gossip: {why}.");
                return;
            case "deed":
                Deed(client, gm, arg);
                return;
            case "holiday":
                Holiday(client, arg);
                return;
            case "leeroy":
                if (Off(AutonomousRaidLeeroy.On, "bot_living_leeroy") is string leeroyOff)
                    Say(client, leeroyOff);
                else if (arg == "now")
                {
                    AutonomousRaidLeeroy.ChargeNow();
                    Say(client, $"A raid with a Leeroy that is gathering for its final boss charges at once (two raiders there will do). " +
                        $"This lapses if none is gathering within {AutonomousRaidLeeroy.ChargeNowWindowMilliseconds / 60_000} minutes.");
                }
                else
                {
                    AutonomousRaidLeeroy.ForceNextRaid();
                    Say(client, "The next realm raid to form has a Leeroy. Start one with /lstest raid dragon (or from the launcher's Realm Events tab); he charges once part of the raid has gathered (or use /lstest leeroy now).");
                }
                return;
            case "raid":
            {
                // In-game check 2026-10-28: Albion's level 50s were all at keep sieges, so another realm's raid may be the one to watch.
                string[] words = args.Skip(2).Select(a => a.ToLowerInvariant()).ToArray();
                eRealm raidRealm = words.Contains("mid") ? eRealm.Midgard : words.Contains("hib") ? eRealm.Hibernia
                    : words.Contains("alb") ? eRealm.Albion : realm;
                Say(client, AutonomousRealmRaid.StartAutomaticForTest(raidRealm, words.Contains("dragon"), out why)
                    ? $"{GlobalConstants.RealmToName(raidRealm)} raid opened: {why}. Watch the launcher's Realm Events tab."
                    : $"No {GlobalConstants.RealmToName(raidRealm)} raid: {why}.");
                return;
            }
            case "craft" or "wtb" or "commission": CraftEconomyTestCommandHandler.Run(client, args[1..]); return; // Crafting 2.0 (WP10)
            case "skill" or "lost" or "ask" or "milestone" or "wall": // Bots as people (WP0 declares them; each package registers its own)
                Say(client, BotPeopleTest.Run(sub, gm, gm.TargetObject as GameBot, args.Skip(2).ToArray()));
                return;
            case "lore":
                Say(client, LoreAtmosphere.TestHere(gm));
                return;
            case "deathanim":
                DeathAnimation(client, gm, arg);
                return;
            default:
                DisplaySyntax(client);
                return;
        }
    }

    /// <summary>/lstest deathanim (1.2.1, BotDeathAnimation): the owner checks the fall, the raise and the release on his PC
    /// in one session. GM only (the command's level); a test command, nothing a player can run.</summary>
    private static void DeathAnimation(GameClient client, GamePlayer gm, string arg)
    {
        Say(client, $"Death fall (bot_death_animation): {OnOff(BotDeathAnimation.On)}. Your client: " +
            (client.IsOriginalGameClient ? "the original game client" : "not known to be the original (the new client, or no build read): it gets nothing new") +
            $" (handshake build {(client.HandshakeBuildRead ? "0x" + client.HandshakeBuild.ToString("X4") : "not read")}).");
        if (arg == "off")
        {
            BotDeathAnimation.StopWatch();
            Say(client, "Packet list stopped.");
            return;
        }
        // The original client lets go of a dead bot's selection, so raise and release go to the one being watched.
        GameBot bot = gm.TargetObject as GameBot ?? BotDeathAnimation.Watched;
        if (bot == null || bot.ObjectState != GameObject.eObjectState.Active || bot.CurrentRegion != gm.CurrentRegion)
        {
            Say(client, "Target a bot (companion, hired companion or gamebot) near you.");
            return;
        }
        BotDeathAnimation.StartWatch(client, bot);
        string who = $"{bot.Name} (object {bot.ObjectID})";
        switch (arg)
        {
            case "raise":
                if (bot.IsAlive)
                {
                    Say(client, $"{who} is alive. /lstest deathanim kills it first.");
                    return;
                }
                // As a Resurrect spell does (ResurrectSpellHandler.ResurrectLiving): health, then the move to the caster.
                Say(client, $"Raising {who} at your side, as a rez does. Look where it fell: no body should stay there, and it should stand next to you.");
                bot.Health = bot.MaxHealth;
                bot.Mana = bot.MaxMana;
                bot.Endurance = bot.MaxEndurance;
                bot.MoveTo(gm.CurrentRegionID, gm.X, gm.Y, gm.Z, gm.Heading);
                bot.OnResurrectedBy(gm);
                return;
            case "release":
                if (bot.IsAlive)
                {
                    Say(client, $"{who} is alive. /lstest deathanim kills it first.");
                    return;
                }
                Say(client, $"Releasing {who}: nothing should be sent for the spot where it fell, and it should appear standing at its bind (no second body).");
                if (!bot.ReleaseNow())
                    Say(client, "The release did not happen now (no destination yet); its own timer tries again.");
                return;
            case "":
                if (bot.IsAlive)
                {
                    Say(client, $"Killing {who}. In the original client it should fall and lie as a monster's corpse does. Every packet about it to your client follows (2 minutes, /lstest deathanim off stops it).");
                    bot.Health = 0;
                    bot.Die(null);
                }
                else
                {
                    // Dead already (perhaps with the setting off at its death): the death's own step again.
                    Say(client, $"{who} is dead: showing its fall again to the original clients near it. Packets follow.");
                    BotDeathAnimation.OnDied(bot);
                }
                Say(client, "Then: /lstest deathanim raise (as a rez) or /lstest deathanim release (to its bind).");
                return;
            default:
                Say(client, "/lstest deathanim [raise | release | off]");
                return;
        }
    }

    private static void Scene(GameClient client, eRealm realm, string topic, string what)
    {
        if (!AutonomousBotVoices.Enabled || !AutonomousBotDebates.Enabled)
        {
            Say(client, "Bot voices or debates are off (bot_voices_enabled, bot_voices_debates).");
            return;
        }
        // "" is any debate; null lets a due storyline episode come first.
        bool started = AutonomousBotDebates.StartSoon(realm, string.IsNullOrEmpty(topic) ? null : topic, out string why);
        Say(client, started ? $"{char.ToUpper(what[0])}{what[1..]} starts in /faction within a few seconds, if enough bots of your realm are free to play it." : $"Not now: {why}.");
    }

    private static void Memory(GameClient client, GamePlayer gm)
    {
        var rows = AutonomousLivingServer.MemoriesOf(gm);
        if (rows.Length == 0)
        {
            Say(client, "No bot remembers you yet. Group with one, rez one or be rezzed (or /lstest remember).");
            return;
        }
        Say(client, $"{rows.Length} bot(s) remember you:");
        foreach (var row in rows.Take(15))
        {
            string name = AutonomousBotRegistry.TryGet(row.BotId, out GameBot bot) ? bot.Name : $"bot {row.BotId} (offline)";
            Say(client, $"  {name}: {AutonomousLivingServer.MoodOf(row)}, grouped {row.Grouped}, rezzed {row.Rezzed}, insulted {row.Abused}, last {row.LastTopic} at {row.LastPlace}");
        }
    }

    private static void Remember(GameClient client, GamePlayer gm)
    {
        GameBot bot = gm.TargetObject as GameBot ?? gm.GetNPCsInRadius(1_500).OfType<GameBot>()
            .Where(b => b.IsAutonomousWorldBot && b.Realm == gm.Realm).OrderBy(b => b.GetDistanceTo(gm)).FirstOrDefault();
        if (bot == null)
        {
            Say(client, "Target a gamebot of your realm, or stand near one.");
            return;
        }
        Say(client, AutonomousLivingServer.RememberForTest(bot, gm)
            ? $"{bot.Name} now remembers two good groups with you. /lstest greet while it is out of combat and near you, or /lstest gossip from afar."
            : $"{bot.Name} can't remember you (bot memory off, or it is not a gamebot of your realm).");
    }

    /// <summary>The deed a /lstest deed word names (chat 1.2 follow-up, E4: "drowned" too), or None. Pure.</summary>
    public static AutonomousPlayerDeeds.Kind DeedKind(string arg) => arg switch
    {
        "solo" => AutonomousPlayerDeeds.Kind.NamedSolo,
        "group" => AutonomousPlayerDeeds.Kind.NamedGroup,
        "streak" => AutonomousPlayerDeeds.Kind.RvrStreak,
        "ganked" => AutonomousPlayerDeeds.Kind.Ganked,
        "silly" => AutonomousPlayerDeeds.Kind.SillyDeath,
        "fell" => AutonomousPlayerDeeds.Kind.Fell,
        "drowned" => AutonomousPlayerDeeds.Kind.Drowned,
        _ => AutonomousPlayerDeeds.Kind.None,
    };

    private static void Deed(GameClient client, GamePlayer gm, string arg)
    {
        AutonomousPlayerDeeds.Kind kind = DeedKind(arg);
        if (kind == AutonomousPlayerDeeds.Kind.None)
        {
            Say(client, "/lstest deed <solo|group|streak|ganked|silly|fell|drowned>");
            return;
        }
        if (Off(AutonomousPlayerDeeds.On, "bot_living_deeds") is string off)
        {
            Say(client, off);
            return;
        }
        string target = gm.TargetObject is GameNPC npc && npc is not GameBot ? npc.Name : null;
        Say(client, AutonomousPlayerDeeds.TellForTest(gm, kind, target, out string why)
            ? $"{why} saw it and tells /faction in about 5 seconds." : $"No deed told: {why}.");
    }

    private static void Holiday(GameClient client, string name)
    {
        if (name is "" or "off")
        {
            AutonomousHolidays.TestDate = null;
            Say(client, $"Bots go by the real date again ({DateTime.Now:yyyy-MM-dd}).");
            return;
        }
        DateTime? day = AutonomousHolidays.DayOf(name, DateTime.Now);
        if (day == null)
        {
            Say(client, "Holidays: " + string.Join(" ", AutonomousHolidays.Names));
            return;
        }
        AutonomousHolidays.TestDate = day.Value.Date.AddHours(DateTime.Now.Hour).AddMinutes(DateTime.Now.Minute);
        Say(client, HolidayHint(name, day.Value));
        if (!AutonomousHolidays.On)
            Say(client, "Holiday chatter is off (bot_living_holidays).");
    }

    /// <summary>What /lstest holiday tells the GM to say: the holiday's own wish ("happy new year"), as bots read it. Pure.</summary>
    public static string HolidayHint(string name, DateTime day)
    {
        string wish = AutonomousHolidays.Wish(name);
        string say = wish == null ? "Wait for their own lines" : $"Say \"{wish}\" in /faction, or wait for their own lines";
        return $"Bots treat today as {day:yyyy-MM-dd} ({name}) until /lstest holiday off or a restart. {say} (about one line in five on the day).";
    }

    private static void List(GameClient client)
    {
        foreach (string line in ListLines())
            Say(client, line);
    }

    /// <summary>The /lstest list lines: every part and its switch. Pure but for the settings it reads.</summary>
    public static string[] ListLines() =>
    [
        "Living Server parts (on/off):",
        $"  voices {OnOff(AutonomousBotVoices.Enabled)}, debates {OnOff(AutonomousBotDebates.Enabled)}, tells {OnOff(Properties.BOT_VOICES_TELLS)}, memory {OnOff(AutonomousLivingServer.MemoryOn)}",
        $"  deeds {OnOff(Properties.BOT_LIVING_DEEDS)}, storylines {OnOff(Properties.BOT_LIVING_STORYLINES)}, spammers and roleplayers {OnOff(Properties.BOT_LIVING_CHARACTERS)}, rumours {OnOff(Properties.BOT_LIVING_RUMOURS)}",
        $"  tales {OnOff(Properties.BOT_LIVING_TALES)}, tale raids {OnOff(Properties.BOT_LIVING_TALE_RAIDS)}, beggars {OnOff(Properties.BOT_LIVING_BEGGARS)}, bonds {OnOff(Properties.BOT_GROUP_BONDS)}",
        $"  atmosphere lines {OnOff(Properties.LIVING_ATMOSPHERE_LINES)}, cosmetics {OnOff(Properties.BOT_COSMETICS)}, death fall {OnOff(BotDeathAnimation.On)}",
        $"  holidays {OnOff(AutonomousHolidays.On)} (today: {(AutonomousHolidays.TestDate is DateTime d ? d.ToString("yyyy-MM-dd") + ", test date" : DateTime.Now.ToString("yyyy-MM-dd"))}), " +
        $"leeroy {OnOff(AutonomousRaidLeeroy.On)}{(AutonomousRaidLeeroy.ChargeNowArmed ? " (a \"now\" is armed)" : "")}",
        BotPeopleTest.ListLine(BotPeopleSwitches.Current),
    ];

    private static string OnOff(bool on) => on ? "on" : "OFF";

    // Settings the launcher's Living Server tab has no switch for (chat audit TEXT-041).
    private static readonly HashSet<string> ServerOnlySettings = new(StringComparer.Ordinal) { "bot_living_tales", "bot_living_tale_raids" };

    /// <summary>What /lstest says when the part asked for is switched off; null when it is on. Pure.</summary>
    public static string Off(bool on, string setting) => on ? null : ServerOnlySettings.Contains(setting)
        ? $"That part is off ({setting}); it is a server setting, not in the launcher."
        : $"That part is off ({setting}); turn it on in the launcher's Living Server tab.";

    /// <summary>When a line asked for now really reaches /faction (live test 2026-11-08: "within a second" came 4.5 minutes later).</summary>
    private static string WhenFaction(eRealm realm) =>
        AutonomousBotDebates.IsRunning(realm) ? "once the scene playing in /faction now is over"
        : !AutonomousBotChatCoordinator.FactionRoom(realm) ? "as soon as /faction has room under its ceiling (it waits up to three minutes; LIVING_FEATURE_DROPPED in the log if it gave up)"
        : "within a few seconds";

    private static void Say(GameClient client, string text) =>
        client.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
}
