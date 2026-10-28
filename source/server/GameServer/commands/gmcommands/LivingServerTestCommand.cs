using System;
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
    "/lstest tell - a bot starts a tell to you",
    "/lstest memory - what bots remember about you",
    "/lstest remember - your target (or the nearest gamebot) remembers a good group with you; walk away and back to be greeted",
    "/lstest greet | gossip - a remembered bot near you greets you; a bot that likes you mentions you in /faction",
    "/lstest deed <solo|group|streak|ganked|silly|fell> - a bot near you tells the realm it saw that",
    "/lstest holiday <name|off> - bots treat today as that holiday (newyear valentines stpatricks aprilfools easter midsummer halloween thanksgiving christmas)",
    "/lstest leeroy - the next realm raid has a Leeroy; start one from the launcher's Realm Events tab",
    "/lstest leeroy now - a raid with a Leeroy gathering for its final boss charges at once")]
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
                int open = AutonomousBotStorylines.MakeDueNow(realm);
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
                    (AutonomousGoldSpammers.StartSoon(realm) ? "A gold spammer starts in /faction within a second." : "A spammer is already at it in your realm."));
                return;
            case "roleplayer":
                int roleplayers = AutonomousRoleplayers.SpeakSoon(realm);
                Say(client, Off(Properties.BOT_LIVING_CHARACTERS, "bot_living_characters") ??
                    (roleplayers > 0 ? $"One of {roleplayers} roleplayer(s) online speaks in /faction within a second (and may be mocked)." : "No roleplayer of your realm is online and free to talk."));
                return;
            case "rumour":
            case "rumor":
                if (Off(Properties.BOT_LIVING_RUMOURS, "bot_living_rumours") is string rumoursOff)
                    Say(client, rumoursOff);
                else
                    Say(client, AutonomousRumours.TellNow(gm, out string about) ? $"A bot tells /faction about {about}." : $"No rumour: {about}.");
                return;
            case "tell":
                AutonomousLivingServer.TellSoon(gm);
                Say(client, "A bot that fits (a friend, a guildmate, a bot of your level near you) starts a tell within about 5 seconds, if one fits. Stand still, out of combat.");
                return;
            case "memory":
                Memory(client, gm);
                return;
            case "remember":
                Remember(client, gm);
                return;
            case "greet":
                AutonomousLivingServer.GreetSoon(gm);
                Say(client, "A bot that remembers you, within 700 units and not in your group, greets you within about 5 seconds.");
                return;
            case "gossip":
                AutonomousLivingServer.GossipSoon(realm);
                Say(client, "A bot that likes you (two good groups or rezzes) and is not next to you mentions you in /faction within about 5 seconds.");
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
                    Say(client, "A raid with a Leeroy that is gathering for its final boss charges at once (two raiders there will do).");
                }
                else
                {
                    AutonomousRaidLeeroy.ForceNextRaid();
                    Say(client, "The next realm raid to form has a Leeroy. Start a dragon or dungeon raid from the launcher's Realm Events tab; he charges once part of the raid has gathered (or use /lstest leeroy now).");
                }
                return;
            default:
                DisplaySyntax(client);
                return;
        }
    }

    private static void Scene(GameClient client, eRealm realm, string topic, string what)
    {
        if (!AutonomousBotVoices.Enabled || !AutonomousBotDebates.Enabled)
        {
            Say(client, "Bot voices or debates are off (bot_voices, bot_debates).");
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
            ? $"{bot.Name} now remembers two good groups with you. Walk more than 700 units away and back (not grouped with it) to be greeted, or /lstest gossip."
            : $"{bot.Name} can't remember you (bot memory off, or it is not a gamebot of your realm).");
    }

    private static void Deed(GameClient client, GamePlayer gm, string arg)
    {
        AutonomousPlayerDeeds.Kind kind = arg switch
        {
            "solo" => AutonomousPlayerDeeds.Kind.NamedSolo,
            "group" => AutonomousPlayerDeeds.Kind.NamedGroup,
            "streak" => AutonomousPlayerDeeds.Kind.RvrStreak,
            "ganked" => AutonomousPlayerDeeds.Kind.Ganked,
            "silly" => AutonomousPlayerDeeds.Kind.SillyDeath,
            "fell" => AutonomousPlayerDeeds.Kind.Fell,
            _ => AutonomousPlayerDeeds.Kind.None,
        };
        if (kind == AutonomousPlayerDeeds.Kind.None)
        {
            Say(client, "/lstest deed <solo|group|streak|ganked|silly|fell>");
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
        Say(client, $"Bots treat today as {day:yyyy-MM-dd} ({name}) until /lstest holiday off or a restart. Say \"happy {name}\" in /faction, or wait for their own lines (about one line in five on the day).");
        if (!AutonomousHolidays.On)
            Say(client, "Holiday chatter is off (bot_living_holidays).");
    }

    private static void List(GameClient client)
    {
        Say(client, "Living Server parts (on/off):");
        Say(client, $"  voices {OnOff(AutonomousBotVoices.Enabled)}, debates {OnOff(AutonomousBotDebates.Enabled)}, tells {OnOff(Properties.BOT_VOICES_TELLS)}, memory {OnOff(AutonomousLivingServer.MemoryOn)}");
        Say(client, $"  deeds {OnOff(Properties.BOT_LIVING_DEEDS)}, storylines {OnOff(Properties.BOT_LIVING_STORYLINES)}, spammers and roleplayers {OnOff(Properties.BOT_LIVING_CHARACTERS)}, rumours {OnOff(Properties.BOT_LIVING_RUMOURS)}");
        Say(client, $"  holidays {OnOff(AutonomousHolidays.On)} (today: {(AutonomousHolidays.TestDate is DateTime d ? d.ToString("yyyy-MM-dd") + ", test date" : DateTime.Now.ToString("yyyy-MM-dd"))}), leeroy {OnOff(AutonomousRaidLeeroy.On)}");
    }

    private static string OnOff(bool on) => on ? "on" : "OFF";

    private static string Off(bool on, string setting) => on ? null : $"That part is off ({setting}); turn it on in the launcher's Living Server tab.";

    private static void Say(GameClient client, string text) =>
        client.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);
}
