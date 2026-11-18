using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS.Commands;
using DOL.GS.PacketHandler;

// Written for Living Server (owner plan 2026-11-16; overlay research of the same day). True 1.65 drew its hire window in
// the original client with a private game.dll overlay that was never published, then dropped the original client. Living
// Server builds the window from the stock client's own windows instead, all server side:
// - the healer's and the companion's popup dialogs, whose [links] the client whispers back to the NPC it has targeted;
// - the stock text window (SendCustomTextWindow) for the roster, stats, specs, realm abilities and AI options;
// - the house-vault item window for gear (CompanionGearVault165) and the champion trainer window for specs
//   (CompanionWindows165.SendTrainerWindow, within its 10 x 10 limit);
// - the private preview figure (Hire165.PreviewNpc), drawn without the hair colour bits.

namespace DOL.GS
{
    /// <summary>The hire window as the original client's own dialogs: links on a healer and on one's own companion.</summary>
    public static class HireDialog165
    {
        /// <summary>The healer's link that opens hiring (also the word a player can whisper the healer).</summary>
        public const string CompanionsLink = "companions";

        // the healer's design links -> the hire window's words
        private static readonly Dictionary<string, string> DesignLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["next race"] = "race+", ["previous race"] = "race-",
            ["next class"] = "class+", ["previous class"] = "class-",
            ["other sex"] = "gender",
            ["next look"] = "look+", ["previous look"] = "look-", ["random look"] = "look",
            ["next hair color"] = "hair+", ["previous hair color"] = "hair-",
            // Older chat links (UK spelling, before the 1.2 chat audit) still work.
            ["next hair colour"] = "hair+", ["previous hair colour"] = "hair-",
            ["taller"] = "height+", ["shorter"] = "height-",
            ["new name"] = "name",
        };

        // a companion's own links -> the /hire words for its row
        private static readonly Dictionary<string, string> CompanionLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["gear"] = "gear", ["stats"] = "stats", ["specs"] = "spec", ["train"] = "trainer", ["respec"] = "respec",
            ["realm abilities"] = "ra", ["options"] = "options", ["repair"] = "repair", ["dismiss"] = "dismiss",
        };

        /// <summary>The healer's line on every talk, while the switch is on: one link to hiring.</summary>
        public static void HealerGreeting(GamePlayer player, GameNPC healer)
        {
            if (!HiredCompanionsMode.IsOn || player == null || healer == null || player.Realm != healer.Realm)
                return;
            healer.SayTo(player, eChatLoc.CL_PopupWindow,
                $"I also find companions for the road, to fight beside you and grow with you. Shall I show you your [{CompanionsLink}]?", false);
        }

        /// <summary>A link clicked in a healer's dialog (or the word whispered). True when it was a hiring word.</summary>
        public static bool HealerWhisper(GamePlayer player, GameNPC healer, string text)
        {
            if (!HiredCompanionsMode.IsOn || player == null || healer == null || string.IsNullOrWhiteSpace(text))
                return false;
            string said = text.Trim();
            if (said.Equals(CompanionsLink, StringComparison.OrdinalIgnoreCase))
            {
                if (player.InCombat)
                    return true;
                Hire165.OpenInnkeeper(player, healer);
                ShowHealer(player, healer);
                return true;
            }
            if (said.Equals("hire", StringComparison.OrdinalIgnoreCase))
            {
                OfferContract(player, healer);
                return true;
            }
            if (DesignLinks.TryGetValue(said, out string verb))
            {
                HireText165.Run(player, verb, [], quiet: true);
                ShowHealer(player, healer);
                return true;
            }
            // "summon 2", "release 1", "revive 3": the roster's links
            string[] words = said.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 2 && words[0].ToLowerInvariant() is "summon" or "release" or "revive" or "dismiss" &&
                int.TryParse(words[1], out _))
            {
                HireText165.Run(player, words[0].ToLowerInvariant(), [words[1]], quiet: true);
                if (words[0].ToLowerInvariant() != "release")
                    ShowHealer(player, healer);
                return true;
            }
            return false;
        }

        /// <summary>
        /// The hire's confirmation: a "Contract of Service" in the client's yes/no dialog. (The classic UI research of
        /// 2026-11-16 suggests the quest offer window, with Accept and Decline; it needs a quest type registered with the
        /// quest manager, whose accept path starts a real quest for the player, so it is left for the design pass.)
        /// </summary>
        public static void OfferContract(GamePlayer player, GameNPC healer)
        {
            if (Hire165.Get(player) is not { } s)
                return;
            Hire165.Design d = s.Design;
            string className = d.Class == 0 ? "-" : ScriptMgr.FindCharacterClass((int)d.Class)?.Name ?? d.Class.ToString();
            long cost = HiredCompanions165.NextCost(player);
            string price = cost == 0 ? "no fee (your first companion)" : $"a fee of {Money.GetString(cost)}";
            player.Out.SendCustomDialog($"Contract of Service: {d.Name}, a level 5 {d.Race} {className}, enters your service for {price}, " +
                "with a training weapon and nothing else. Sign it?", (p, response) =>
            {
                if (response != 0x01)
                    return;
                HireText165.Run(p, "hire", [], quiet: true);
                if (healer.ObjectState == GameObject.eObjectState.Active && p.IsWithinRadius(healer, HireText165.HealerReach))
                    ShowHealer(p, healer);
            });
        }

        /// <summary>The hiring dialog: the companion being designed, its links, then the roster with each one's links.</summary>
        public static void ShowHealer(GamePlayer player, GameNPC healer)
        {
            var lines = new List<string>();
            if (Hire165.Get(player) is { } s)
            {
                if (s.Problem.Length > 0)
                    lines.Add(s.Problem);
                lines.Add(HireText165.DesignLine(player, links: true));
            }
            lines.AddRange(HireText165.RosterLines(player, links: true));
            healer.SayTo(player, eChatLoc.CL_PopupWindow, string.Join("\n", lines), false);
        }

        /// <summary>Right-clicking one's own companion: its dialog with links to its windows.</summary>
        public static void ShowCompanion(GamePlayer player, GameBot bot)
        {
            if (player == null || bot == null)
                return;
            bot.SayTo(player, eChatLoc.CL_PopupWindow,
                $"{bot.CharacterClass?.Name ?? bot.ClassName} {bot.Level}, {bot.CompanionSpecPoints} specialization points to spend. " +
                "What do you need? [gear] [stats] [specs] [train] [respec] [realm abilities] [options] [repair] [dismiss]", false);
        }

        /// <summary>A link clicked in one's own companion's dialog. True when it was one of its words.</summary>
        public static bool CompanionWhisper(GameBot bot, GamePlayer player, string text)
        {
            if (!HiredCompanionsMode.IsOn || bot == null || player == null || !HiredCompanions165.IsHiredBy(bot, player) ||
                string.IsNullOrWhiteSpace(text) || !CompanionLinks.TryGetValue(text.Trim(), out string verb))
                return false;
            if (HiredCompanions165.RosterOf(player).FirstOrDefault(h => h.BotId == bot.DatabaseID) is not { } hired)
                return false;
            if (verb == "trainer")
            {
                CompanionWindows165.SendTrainerWindow(player, hired.Row, bot);
                return true;
            }
            HireText165.Run(player, verb, [(hired.Row + 1).ToString()], quiet: true);
            return true;
        }
    }
}
