using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.PacketHandler;

// Written for Living Server (owner plan 2026-11-16, step 1). True 1.65 opened the hire window at the innkeeper of a
// traveler's inn (Innkeeper165); Living Server has no inns until a base-game world update adds them, so /hire opens the
// same window beside any healer of the player's realm (every capital and major town has one). It also writes the window
// in chat, so the original client works without a client overlay: every button of the window is a /hire word.

namespace DOL.GS.Commands
{
    [CmdAttribute("&hire", ePrivLevel.Player, "Hire and manage your companions (beside a healer of a capital or major town)",
        "/hire - beside a healer: open the hire window and show it in chat",
        "/hire list - your companions",
        "/hire race+|race-|class+|class-|gender|look+|look-|hair+|hair-|height+|height-|look - change the companion you design",
        "/hire name [name] - name it (no name: a random one); /hire confirm - hire it",
        "/hire summon|dismiss|release|revive <n> - companion n of /hire list",
        "/hire gear|stats|spec|trainer|ra|options|repair <n>; /hire train <n> <line>; /hire buyra <n> <ability>; /hire respec <n>",
        "/hire option <n> <row> <choice> - set an AI option from /hire options")]
    public sealed class HireCommand165Handler : AbstractCommandHandler, ICommandHandler
    {
        public void OnCommand(GameClient client, string[] args)
        {
            GamePlayer player = client?.Player;
            if (player == null)
                return;
            if (!HiredCompanionsMode.IsOn)
            {
                DisplayMessage(client, "Hired companions are switched off on this server (hired_companions).");
                return;
            }
            string verb = args.Length > 1 ? args[1].ToLowerInvariant() : string.Empty;
            HireText165.Log(player, args);
            switch (verb)
            {
                case "":
                    if (!HireText165.OpenNearHealer(player, out string why))
                    {
                        DisplayMessage(client, why);
                        HireText165.Roster(player);
                        return;
                    }
                    // the healer's dialog shows it with its links; chat says how to type it
                    if (Hire165.Get(player)?.Keeper is { } healer)
                        HireDialog165.ShowHealer(player, healer);
                    DisplayMessage(client, "Click the links in the healer's window, or type them: /hire help.");
                    return;
                case "list":
                    HireText165.Roster(player);
                    return;
                case "help":
                    DisplayMessage(client, string.Join("\n", HireText165.HelpLines));
                    return;
                case "close":
                    Hire165.Close(player);
                    return;
            }
            HireText165.Run(player, verb, args.Skip(2).ToArray());
        }
    }

    /// <summary>The hire window in chat, and the /hire words that press its buttons.</summary>
    public static class HireText165
    {
        private static readonly Logging.Logger Logger = Logging.LoggerManager.Create(typeof(HireText165));

        /// <summary>How near a healer the player stands to hire, summon or revive (Hire165 closes the window at 512).</summary>
        public const int HealerReach = 512;

        public static readonly string[] HelpLines =
        [
            "/hire beside a healer opens the hire window. Your first companion is free; slots open at levels 5, 10, 15, 20, 30, 40 and 45.",
            "/hire race+ race- class+ class- gender look+ look- hair+ hair- height+ height- look (random) - design the companion.",
            "/hire name <name> names it, /hire confirm hires it. /hire list shows your companions, numbered.",
            "/hire summon <n> (beside a healer), dismiss <n>, release <n>, revive <n> (beside a healer, free).",
            "/hire gear <n> opens its gear window: drag from your backpack onto it, or off it to take items back.",
            "/hire stats <n>, spec <n>, train <n> <line>, trainer <n> (the trainer window), respec <n>, ra <n>, buyra <n> <ability>, options <n>, option <n> <row> <choice>, repair <n> (beside a smith).",
            "Right-click your companion for its own links; a healer's [companions] link opens hiring.",
        ];

        private static readonly HashSet<string> DesignVerbs = new(StringComparer.Ordinal)
        {
            "race+", "race-", "class+", "class-", "gender", "look+", "look-", "face+", "face-", "hair+", "hair-",
            "height+", "height-", "look", "name",
        };

        private static readonly HashSet<string> RowVerbs = new(StringComparer.Ordinal) { "summon", "dismiss", "release", "revive" };

        internal static void Log(GamePlayer player, string[] args)
        {
            if (Logger.IsInfoEnabled)
                Logger.Info($"HIRE_CMD player={player.Name} args=\"{string.Join(' ', args.Skip(1))}\"");
        }

        /// <summary>The nearest healer of the player's realm within reach, or null.</summary>
        public static GameNPC HealerNear(GamePlayer player) =>
            player.GetNPCsInRadius(HealerReach).OfType<GameHealer>()
                .Where(h => h.IsAlive && h.ObjectState == GameObject.eObjectState.Active && (h.Realm == player.Realm || h.Realm == eRealm.None))
                .OrderBy(h => h.GetDistanceTo(player)).FirstOrDefault();

        /// <summary>Opens the hire window at the nearest healer (mode 0: hiring and summoning). False, with why, when none.</summary>
        public static bool OpenNearHealer(GamePlayer player, out string why)
        {
            why = null;
            if (player.InCombat)
            {
                why = "Not while you are fighting.";
                return false;
            }
            GameNPC healer = HealerNear(player);
            if (healer == null)
            {
                why = Hire165.NotOpenMessage;
                return false;
            }
            Hire165.OpenInnkeeper(player, healer);
            return true;
        }

        /// <summary>Makes sure a hire session fits the word: hiring and summoning beside a healer (mode 0), reviving beside
        /// a healer (mode 1), anything else anywhere (a window without a keeper, as right-clicking a companion opens).</summary>
        private static bool EnsureSession(GamePlayer player, string verb)
        {
            Hire165.Session s = Hire165.Get(player);
            bool needsHealer = verb is "confirm" or "hire" or "summon" or "revive" || DesignVerbs.Contains(verb);
            byte mode = verb == "revive" ? (byte)1 : (byte)0;
            // a window opened elsewhere (or at a healer left behind) is opened again here
            if (s != null && (s.Mode != mode || s.Keeper != null && (s.Keeper.CurrentRegionID != player.CurrentRegionID ||
                                                                       !player.IsWithinRadius(s.Keeper, HealerReach)) ||
                              needsHealer && s.Keeper == null && !s.Test))
            {
                Hire165.Close(player);
                s = null;
            }
            if (s != null)
                return true;
            GameNPC healer = HealerNear(player);
            if (needsHealer && healer == null)
            {
                player.Out.SendMessage(verb == "revive" ? "Bring your companion back beside a healer of your realm (/hire revive there)."
                        : verb == "summon" ? Hire165.SummonWhereMessage : Hire165.HireWhereMessage,
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
                return false;
            }
            if (healer != null && mode == 1)
                Hire165.OpenHealer(player, healer);
            else if (healer != null)
                Hire165.OpenInnkeeper(player, healer);
            else
                Hire165.OpenForCompanion(player, -1);
            return Hire165.Get(player) != null;
        }

        /// <summary>One /hire word with its arguments (after the word). Quiet (a dialog link): the dialog shows the design
        /// and roster again itself, so they are not written in chat.</summary>
        public static void Run(GamePlayer player, string verb, string[] rest, bool quiet = false)
        {
            if (verb == "confirm")
                verb = "hire";
            if (!DesignVerbs.Contains(verb) && verb != "hire" && !RowVerbs.Contains(verb) &&
                !CompanionWindows165.Verbs.Contains(verb) && verb is not "options" and not "trainer")
            {
                Say(player, "Unknown /hire word. /hire help lists them.");
                return;
            }
            // every word, typed or a dialog link (quiet), for the owner's PC log: the gear, stats, spec and options windows
            if (Logger.IsInfoEnabled)
                Logger.Info($"HIRE_WORD_165 player={player.Name} word={verb} args=\"{string.Join(' ', rest)}\" from={(quiet ? "dialog" : "typed")}");
            // rows are numbered from 1 in chat, from 0 in the window protocol
            int RowAt(int i) => rest.Length > i && int.TryParse(rest[i], out int n) ? n - 1 : -1;
            string Row(int i) => RowAt(i).ToString();
            if (verb is "options" or "trainer")
            {
                if (verb == "options")
                    Options(player, RowAt(0));
                else if (LiveAt(player, RowAt(0)) is { } trainee)
                    CompanionWindows165.SendTrainerWindow(player, RowAt(0), trainee);
                else
                    Say(player, "Summon that companion first (/hire list).");
                return;
            }
            if (!EnsureSession(player, verb))
                return;
            if (DesignVerbs.Contains(verb) || verb == "hire")
            {
                Hire165.Handle(player, ["companionui", verb, .. rest]);
                Problem(player);
                if (!quiet)
                {
                    Say(player, DesignLine(player, links: false));
                    if (verb == "hire")
                        Roster(player);
                }
                else if (verb == "hire")
                    Roster(player);
                return;
            }
            if (RowVerbs.Contains(verb))
            {
                Hire165.Handle(player, ["companionui", verb, Row(0)]);
                Problem(player);
                if (verb != "release" && !quiet)
                    Roster(player);
                return;
            }
            // the companion windows: the row first, then the window's own arguments (a line or ability number from 1)
            string[] windowArgs = verb switch
            {
                "train" or "buyra" => ["companionui", verb, Row(0), rest.Length > 1 && int.TryParse(rest[1], out int k) ? (k - 1).ToString() : "-1"],
                "option" => OptionArgs(player, Row(0), rest),
                _ => ["companionui", verb == "gear" ? "vault" : verb, Row(0), .. rest.Skip(1)],
            };
            CompanionWindows165.Handle(player, windowArgs);
            Problem(player);
            int row = RowAt(0);
            switch (verb)
            {
                case "stats" or "select":
                    Stats(player, row);
                    break;
                case "spec" or "train" or "respec":
                    Spec(player, row);
                    break;
                case "ra" or "buyra":
                    RealmAbilities(player, row);
                    break;
                case "option":
                    Options(player, row);
                    break;
            }
        }

        private static void Say(GamePlayer player, string text) =>
            player.Out.SendMessage(text, eChatType.CT_System, eChatLoc.CL_SystemWindow);

        private static void Problem(GamePlayer player)
        {
            if (Hire165.Get(player) is { Problem.Length: > 0 } s)
                Say(player, s.Problem);
        }

        /// <summary>The stock text window (title and lines), the original client's own.</summary>
        private static void Window(GamePlayer player, string caption, List<string> lines) =>
            player.Out.SendCustomTextWindow(caption, lines);

        /// <summary>The design page in chat.</summary>
        public static void Design(GamePlayer player) => Say(player, DesignLine(player, links: false));

        /// <summary>The companion being designed in one line: race, class, sex, look, hair, height, name and cost; with
        /// links (a dialog) each part has its [next] and [previous].</summary>
        public static string DesignLine(GamePlayer player, bool links)
        {
            if (Hire165.Get(player) is not { } s)
                return string.Empty;
            Hire165.Design d = s.Design;
            string className = d.Class == 0 ? "-" : ScriptMgr.FindCharacterClass((int)d.Class)?.Name ?? d.Class.ToString();
            string height = Math.Clamp(d.Height, 1, 3) switch { 1 => "short", 3 => "tall", _ => "of average height" };
            int hair = Hire165.HairCount(d);
            long cost = HiredCompanions165.NextCost(player);
            string price = cost == 0 ? "free (your first companion)" : Money.GetString(cost);
            string sex = d.Gender == eGender.Female ? "female" : "male";
            if (!links)
                return $"Companion to hire: {d.Name}, {d.Race} {className}, {sex}, look {d.Face + 1} of {Hire165.LookCount(d)}" +
                       (hair > 1 ? $", hair colour {d.HairColour + 1} of {hair}" : string.Empty) +
                       $", {height}. Cost: {price}. /hire confirm to hire, /hire name <name> to name.";
            return $"A companion for you: {d.Name} [new name] (or /hire name <name>), a level 5 {className} " +
                   $"[previous class] [next class], {d.Race} [previous race] [next race], {sex} [other sex], " +
                   $"look {d.Face + 1} of {Hire165.LookCount(d)} [previous look] [next look] [random look]" +
                   (hair > 1 ? $", hair colour {d.HairColour + 1} of {hair} [next hair colour]" : string.Empty) +
                   $", {height} [shorter] [taller]. Cost: {price}. [hire]";
        }

        /// <summary>The roster in chat, numbered from 1.</summary>
        public static void Roster(GamePlayer player)
        {
            foreach (string line in RosterLines(player, links: false))
                Say(player, line);
        }

        /// <summary>The roster's lines, numbered from 1; with links (a dialog) each companion's [summon n] and the rest.</summary>
        public static List<string> RosterLines(GamePlayer player, bool links)
        {
            var lines = new List<string>();
            List<OfflineHiredCompanionRecord> roster = HiredCompanions165.RosterOf(player);
            int open = HiredCompanions165.OpenSlots(player.Level), next = HiredCompanions165.NextSlotLevel(player.Level);
            lines.Add($"Your companions: {roster.Count} of {open} open" + (next > 0 ? $" (next slot at level {next})." : "."));
            foreach (OfflineHiredCompanionRecord hired in roster)
            {
                if (HiredCompanions165.Record(hired.BotId) is not { } rec)
                    continue;
                GameBot live = HiredCompanions165.LiveBot(hired.BotId);
                bool dead = live != null ? !live.IsAlive : !rec.IsAlive;
                int n = hired.Row + 1;
                string state = links
                    ? dead ? $"dead [revive {n}] [release {n}]" : live != null ? $"with you [dismiss {n}]" : $"waiting [summon {n}] [release {n}]"
                    : dead ? "dead (/hire revive beside a healer)" : live != null ? "with you" : "waiting (/hire summon beside a healer)";
                lines.Add($"{n}. {rec.Name}, {rec.ClassName} {live?.Level ?? rec.Level}: {state}");
            }
            return lines;
        }

        private static GameBot LiveAt(GamePlayer player, int row) =>
            HiredCompanions165.RosterOf(player).FirstOrDefault(h => h.Row == row) is { } hired ? HiredCompanions165.LiveBot(hired.BotId) : null;

        /// <summary>The stats sheet in the stock text window (True 1.65 sent it to its overlay as text slots 40-46).</summary>
        public static void Stats(GamePlayer player, int row)
        {
            if (LiveAt(player, row) is not { } bot)
            {
                Say(player, "Summon that companion first (/hire list).");
                return;
            }
            int M(eProperty p) => bot.GetModified(p);
            OfflineWorldBotRecord rec = bot.PersistentRecord;
            double bubbles = rec == null ? 0 : Hire165.XpPerMille(bot, rec) / 100.0;
            Window(player, $"{bot.Name} - {bot.CharacterClass?.Name ?? bot.ClassName} {bot.Level}",
            [
                $"Level {bot.Level}, {bubbles:0.0} bubbles of 10. Realm rank {bot.RealmLevel / 10 + 1}L{bot.RealmLevel % 10}.",
                " ",
                $"Strength {M(eProperty.Strength)}    Constitution {M(eProperty.Constitution)}",
                $"Dexterity {M(eProperty.Dexterity)}    Quickness {M(eProperty.Quickness)}",
                $"Intelligence {M(eProperty.Intelligence)}    Piety {M(eProperty.Piety)}",
                $"Empathy {M(eProperty.Empathy)}    Charisma {M(eProperty.Charisma)}",
                " ",
                $"Health {bot.Health} / {bot.MaxHealth}",
                $"Power {bot.Mana} / {bot.MaxMana}",
                $"Endurance {bot.Endurance} / {bot.MaxEndurance}",
                $"Armor factor {M(eProperty.ArmorFactor)}, absorption {M(eProperty.ArmorAbsorption)}%",
                " ",
                $"Resists: crush {M(eProperty.Resist_Crush)}%, slash {M(eProperty.Resist_Slash)}%, thrust {M(eProperty.Resist_Thrust)}%",
                $"heat {M(eProperty.Resist_Heat)}%, cold {M(eProperty.Resist_Cold)}%, matter {M(eProperty.Resist_Matter)}%",
                $"body {M(eProperty.Resist_Body)}%, spirit {M(eProperty.Resist_Spirit)}%, energy {M(eProperty.Resist_Energy)}%",
                " ",
                $"Money {Money.GetString(rec?.MoneyCopper ?? 0)}, realm points {rec?.RealmPoints ?? 0:N0}",
            ]);
        }

        /// <summary>The spec lines in the stock text window, numbered for /hire train.</summary>
        public static void Spec(GamePlayer player, int row)
        {
            if (LiveAt(player, row) is not { } bot)
            {
                Say(player, "Summon that companion first (/hire list).");
                return;
            }
            var lines = new List<string>
            {
                $"{bot.CompanionSpecPoints} specialization points to spend.",
                $"/hire train {row + 1} <line> raises a line by one; /hire trainer {row + 1} opens the trainer window.",
                " ",
            };
            List<Specialization> specs = CompanionWindows165.Lines(bot);
            for (int i = 0; i < specs.Count; i++)
                lines.Add($"{i + 1}. {specs[i].Name ?? specs[i].KeyName} {specs[i].Level}" +
                          (specs[i].Level >= bot.Level ? (bot.Level >= 50 ? " (highest)" : " (at its level)") : $" - next costs {specs[i].Level + 1}"));
            Window(player, $"{bot.Name}'s specializations", lines);
        }

        /// <summary>The realm abilities in the stock text window, numbered for /hire buyra.</summary>
        public static void RealmAbilities(GamePlayer player, int row)
        {
            if (LiveAt(player, row) is not { } bot ||
                HiredCompanions165.RosterOf(player).FirstOrDefault(h => h.Row == row) is not { } hired)
            {
                Say(player, "Summon that companion first (/hire list).");
                return;
            }
            var lines = new List<string>
            {
                $"{CompanionWindows165.RealmAbilityPoints(bot, hired)} realm ability points. /hire buyra {row + 1} <ability> buys the next level.",
                " ",
            };
            List<DOL.GS.RealmAbilities.RealmAbility> list = CompanionWindows165.RealmAbilityList(bot);
            Dictionary<string, int> chosen = CompanionWindows165.ChosenRealmAbilities(hired);
            for (int i = 0; i < list.Count; i++)
            {
                int level = chosen.GetValueOrDefault(list[i].KeyName);
                lines.Add($"{i + 1}. {list[i].Name ?? list[i].KeyName} {level} of {list[i].MaxLevel}" +
                          (level >= list[i].MaxLevel ? string.Empty : $" - next costs {list[i].CostForUpgrade(level)}"));
            }
            Window(player, $"{bot.Name}'s realm abilities", lines);
        }

        /// <summary>The AI Options rows in the stock text window, numbered for /hire option (row, then choice).</summary>
        public static void Options(GamePlayer player, int row)
        {
            if (LiveAt(player, row) is not { } bot)
            {
                Say(player, "Summon that companion first (/hire list).");
                return;
            }
            IReadOnlyList<CompanionOptions165.OptionRow> rows = CompanionOptions165.TextRows(bot);
            var lines = new List<string>();
            if (rows.Count == 0)
                lines.Add($"{bot.Name} has no AI options at its level.");
            else
            {
                lines.Add($"/hire option {row + 1} <row> <choice> sets one; the [choice] is the current one.");
                lines.Add(" ");
                for (int i = 0; i < rows.Count; i++)
                    lines.Add($"{i + 1}. {rows[i].Caption}: " +
                              string.Join(", ", rows[i].Choices.Select((c, k) => k == rows[i].Chosen ? $"[{k + 1} {c}]" : $"{k + 1} {c}")));
            }
            Window(player, $"{bot.Name}'s AI options", lines);
        }

        /// <summary>"/hire option n row choice" as the window's own words (row and choice from the text list): the
        /// command, the verb, the companion's row, then the verb's arguments.</summary>
        private static string[] OptionArgs(GamePlayer player, string row, string[] rest)
        {
            if (!int.TryParse(row, out int r) || LiveAt(player, r) is not { } bot || rest.Length < 3 ||
                !int.TryParse(rest[1], out int index) || !int.TryParse(rest[2], out int choice))
                return ["companionui", "option", row, "-1", "0", "-1"];
            IReadOnlyList<CompanionOptions165.OptionRow> rows = CompanionOptions165.TextRows(bot);
            if (index < 1 || index > rows.Count)
                return ["companionui", "option", row, "-1", "0", "-1"];
            string[] command = CompanionOptions165.CommandFor(rows[index - 1], choice - 1);
            return ["companionui", command[0], row, .. command.Skip(1)];
        }
    }
}
