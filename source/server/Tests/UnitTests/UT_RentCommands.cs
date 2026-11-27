using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DOL.Database;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// /rent and /bountyrent, port of OpenDAoC-Core e4b9e02b1 (issue #562 step 2; owner answer 23A: port as upstream).
    /// Pure rules are called directly (amount parsing, lockbox room, the guild charges); wiring that needs a live client
    /// and a house is guarded on the source. Players only: bots don't own houses. Tests only until seen in a played log.
    /// </summary>
    [TestFixture]
    public class UT_RentCommands
    {
        private const long Gold = RentCommandHandler.CopperPerGold;

        private static string Root()
        {
            for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
            {
                if (File.Exists(Path.Combine(dir.FullName, "GameServer", "GameServer.csproj")))
                    return Path.Combine(dir.FullName, "GameServer");
                if (File.Exists(Path.Combine(dir.FullName, "source", "server", "GameServer", "GameServer.csproj")))
                    return Path.Combine(dir.FullName, "source", "server", "GameServer");
            }
            Assume.That(false, "the sources are next to the tests");
            return null;
        }

        private static string Source(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { Root() }.Concat(parts).ToArray())).Replace("\r\n", "\n");

        private static string HandlerSource() => Source("commands", "playercommands", "AbstractRentCommandHandler.cs");

        private static string Method(string source, string signature)
        {
            int start = source.IndexOf(signature, StringComparison.Ordinal);
            Assert.That(start, Is.GreaterThanOrEqualTo(0), signature);
            int open = source.IndexOf('{', start);
            int depth = 0;
            for (int i = open; i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}' && --depth == 0) return source.Substring(start, i - start + 1);
            }
            throw new AssertionException("unbalanced " + signature);
        }

        private static Guild GuildWith(double bank = 0, long bountyPoints = 0) =>
            new(new DbGuild { GuildName = "Rent Payers", GuildID = "rent", Realm = (byte)eRealm.Albion, Bank = bank, BountyPoints = bountyPoints });

        // Amounts: zero, negative, overflowing, not a number

        [Test]
        public void Parse_ZeroNegativeAndNonNumbersAreRejected()
        {
            Assert.That(AbstractRentCommandHandler.TryParseAmount("0", Gold, out _), Is.False);
            Assert.That(AbstractRentCommandHandler.TryParseAmount("-5", Gold, out _), Is.False);
            Assert.That(AbstractRentCommandHandler.TryParseAmount("ten", Gold, out _), Is.False);
            Assert.That(AbstractRentCommandHandler.TryParseAmount("", Gold, out _), Is.False);
            Assert.That(AbstractRentCommandHandler.TryParseAmount(null, Gold, out _), Is.False);
            Assert.That(AbstractRentCommandHandler.TryParseAmount("25", Gold, out long? amount), Is.True);
            Assert.That(amount, Is.EqualTo(25));
        }

        [Test]
        public void Parse_AnAmountWhoseCopperWouldOverflowIsRejected()
        {
            // Before: 922337203685478 gold * 10000 wrapped to a small negative/positive copper value.
            long limit = long.MaxValue / Gold;
            Assert.That(AbstractRentCommandHandler.TryParseAmount(limit.ToString(), Gold, out long? atLimit), Is.True);
            Assert.That(atLimit, Is.EqualTo(limit));
            Assert.That(AbstractRentCommandHandler.TryParseAmount((limit + 1).ToString(), Gold, out _), Is.False);
            Assert.That(AbstractRentCommandHandler.TryParseAmount("99999999999999999999", Gold, out _), Is.False, "past long");
        }

        [Test]
        public void Parse_AMisconfiguredRateIsRejected()
        {
            // RENT_BOUNTY_POINT_TO_GOLD of 0 used to divide by zero in the bounty messages.
            Assert.That(AbstractRentCommandHandler.TryParseAmount("5", 0, out _), Is.False);
            Assert.That(AbstractRentCommandHandler.TryParseAmount("max", 0, out _), Is.False);
            Assert.That(AbstractRentCommandHandler.TryParseAmount("5", -1, out _), Is.False);
        }

        [Test]
        public void Resolve_TooMuchForTheLockboxIsRefusedWithoutWrapping()
        {
            Assert.That(AbstractRentCommandHandler.ResolveAmount(12 * Gold, Gold, 13, out long amount, out long copper),
                Is.EqualTo(AbstractRentCommandHandler.AmountResult.TooMuchMoney));
            Assert.That(amount, Is.Zero);
            Assert.That(copper, Is.Zero);
            Assert.That(AbstractRentCommandHandler.ResolveAmount(12 * Gold, Gold, long.MaxValue, out _, out copper),
                Is.EqualTo(AbstractRentCommandHandler.AmountResult.TooMuchMoney), "an overflowing amount never wraps into the lockbox");
            Assert.That(copper, Is.Zero);
            Assert.That(AbstractRentCommandHandler.ResolveAmount(12 * Gold, Gold, 12, out amount, out copper),
                Is.EqualTo(AbstractRentCommandHandler.AmountResult.Ok));
            Assert.That((amount, copper), Is.EqualTo((12L, 12 * Gold)));
        }

        [Test]
        public void Resolve_AFullLockboxIsAlreadyMax()
        {
            Assert.That(AbstractRentCommandHandler.ResolveAmount(0, Gold, 1, out _, out _), Is.EqualTo(AbstractRentCommandHandler.AmountResult.AlreadyMaxMoney));
            Assert.That(AbstractRentCommandHandler.ResolveAmount(-500, Gold, null, out _, out _), Is.EqualTo(AbstractRentCommandHandler.AmountResult.AlreadyMaxMoney));
        }

        // The max option

        [Test]
        public void Max_IsParsedInAnyCase()
        {
            Assert.That(AbstractRentCommandHandler.TryParseAmount("max", Gold, out long? requested), Is.True);
            Assert.That(requested, Is.Null);
            Assert.That(AbstractRentCommandHandler.TryParseAmount("MAX", Gold, out requested), Is.True);
            Assert.That(requested, Is.Null);
        }

        [Test]
        public void Max_FillsTheLockboxRoundedDownToWholeUnits()
        {
            // 12.5 gold of room: max pays 12 gold.
            Assert.That(AbstractRentCommandHandler.ResolveAmount(125_000, Gold, null, out long amount, out long copper),
                Is.EqualTo(AbstractRentCommandHandler.AmountResult.Ok));
            Assert.That((amount, copper), Is.EqualTo((12L, 120_000L)));

            // Bounty points at 1 bp = 3 gold 7 copper: whole points only.
            const long bp = 30_007;
            Assert.That(AbstractRentCommandHandler.ResolveAmount(100_000, bp, null, out amount, out copper),
                Is.EqualTo(AbstractRentCommandHandler.AmountResult.Ok));
            Assert.That((amount, copper), Is.EqualTo((3L, 90_021L)));
        }

        [Test]
        public void Max_LessThanOneUnitOfRoomIsAlreadyMax()
        {
            Assert.That(AbstractRentCommandHandler.ResolveAmount(9_999, Gold, null, out long amount, out long copper),
                Is.EqualTo(AbstractRentCommandHandler.AmountResult.AlreadyMaxMoney));
            Assert.That((amount, copper), Is.EqualTo((0L, 0L)));
        }

        [Test]
        public void Max_IsInBothCommandsUsage()
        {
            foreach (Type type in new[] { typeof(RentCommandHandler), typeof(BountyRentCommandHandler) })
            {
                var cmd = (CmdAttribute)type.GetCustomAttributes(typeof(CmdAttribute), false).Single();
                Assert.That(cmd.Usage.Any(u => u.Contains("<personal | guild> max")), type.Name);
                Assert.That(cmd.Level, Is.EqualTo((uint)ePrivLevel.Player), type.Name);
            }
        }

        // /rent guild paid the gold to the player

        [Test]
        public void RentGuild_TakesTheGoldFromTheBankAndPaysNobody()
        {
            Guild guild = GuildWith(bank: 30 * Gold);
            Assert.That(RentCommandHandler.ChargeGuildBank(guild, 12 * Gold), Is.True);
            Assert.That(guild.GetGuildBank(), Is.EqualTo(18 * Gold));

            string handler = HandlerSource();
            Assert.That(handler, Does.Not.Contain("WithdrawFromBank"), "WithdrawFromBank hands the gold to the player");
            Assert.That(Method(handler, "public static bool ChargeGuildBank(Guild guild, long copper)"), Does.Contain("guild.RemoveFromBank(copper, false)"));
        }

        [Test]
        public void RentGuild_TheWholeBankCanBeSpent()
        {
            // Before: GetGuildBank() - gold <= 0 refused spending the bank down to exactly zero.
            Guild guild = GuildWith(bank: 12 * Gold);
            Assert.That(RentCommandHandler.ChargeGuildBank(guild, 12 * Gold), Is.True);
            Assert.That(guild.GetGuildBank(), Is.Zero);
        }

        [Test]
        public void RentGuild_ABankTooSmallKeepsItsGold()
        {
            Guild guild = GuildWith(bank: 11 * Gold);
            Assert.That(RentCommandHandler.ChargeGuildBank(guild, 12 * Gold), Is.False);
            Assert.That(guild.GetGuildBank(), Is.EqualTo(11 * Gold));
            Assert.That(RentCommandHandler.ChargeGuildBank(null, Gold), Is.False);
        }

        [Test]
        public void RentGuild_TheGuildIsSavedAndToldOnce()
        {
            string payGuild = Method(HandlerSource(), "private void PayGuild(GameClient client, long? requested)");
            Assert.That(payGuild, Does.Contain("guild.SaveIntoDatabase();"));
            int told = payGuild.Split("SendMessageToGuildMembers").Length - 1;
            Assert.That(told, Is.EqualTo(1), "once, not once by WithdrawFromBank and again by the command");
            Assert.That(RentCommandHandler.GuildAnnouncement("Aldric", 12), Is.EqualTo("Aldric withdrew 12 gold to pay the guild house's rent."));
        }

        // Guild bank: AddToBank takes only positive amounts, RemoveFromBank takes money out

        [Test]
        public void GuildBank_AddAndRemoveTakeOnlyPositiveAmounts()
        {
            Guild guild = GuildWith(bank: 1_000);
            Assert.That(guild.AddToBank(-100, false), Is.False, "a negative add no longer withdraws");
            Assert.That(guild.AddToBank(0, false), Is.False);
            Assert.That(guild.RemoveFromBank(0, false), Is.False);
            Assert.That(guild.RemoveFromBank(-100, false), Is.False, "a negative remove no longer deposits");
            Assert.That(guild.GetGuildBank(), Is.EqualTo(1_000));
            Assert.That(guild.AddToBank(500, false), Is.True);
            Assert.That(guild.RemoveFromBank(1_500, false), Is.True);
            Assert.That(guild.GetGuildBank(), Is.Zero);
            Assert.That(guild.RemoveFromBank(1, false), Is.False, "never below zero");
        }

        [Test]
        public void GuildBank_ChangeBankSavesWhenAskedAndBotCallersMovedToRemove()
        {
            string guild = Source("gameutils", "Guild.cs");
            Assert.That(Method(guild, "private void ChangeBank(double newBank, bool save)"), Does.Contain("if (save)"));
            Assert.That(guild, Does.Not.Contain("ValidateAddToBankAmount"));

            // The bot guild economy took money out with a negative AddToBank; it uses RemoveFromBank now (same result).
            string economy = Source("bots", "autonomous", "AutonomousBotGuildEconomy.cs");
            string commerce = Source("craft", "economy", "commerce", "BotGuildCommerce.cs");
            Assert.That(economy, Does.Contain("guild.RemoveFromBank(fee, false)"));
            Assert.That(commerce, Does.Contain("guild.RemoveFromBank(copper, false)"));
            Assert.That(economy + commerce, Does.Not.Contain("AddToBank(-"));
        }

        // Crash for players with no guild

        [Test]
        public void NoGuild_IsCheckedBeforeTheGuildHouseIsRead()
        {
            string payGuild = Method(HandlerSource(), "private void PayGuild(GameClient client, long? requested)");
            int check = payGuild.IndexOf("if (guild == null)", StringComparison.Ordinal);
            int read = payGuild.IndexOf("guild.GuildHouseNumber", StringComparison.Ordinal);
            Assert.That(check, Is.GreaterThanOrEqualTo(0));
            Assert.That(read, Is.GreaterThan(check), "player.Guild.GuildHouseNumber threw for a player with no guild");
            Assert.That(payGuild, Does.Contain("Say(client, \"NoGuild\")"));
            Assert.That(HandlerSource(), Does.Not.Contain("client.Player.Guild.GuildHouseNumber"));
        }

        [Test]
        public void NoGuild_HasAMessageInBothCommands()
        {
            string en = Source("language", "EN", "Commands", "PlayerCommands.txt");
            foreach (string prefix in new[] { "Scripts.Players.Rent.", "Scripts.Players.Bountyrent." })
                foreach (string key in new[] { "NoGuild", "NoHouse", "NoGuildHouse", "NoPayRentPerm", "AlreadyMaxMoney", "TooMuchMoney", "YouSpend", "YouSpendGuild" })
                    Assert.That(en, Does.Contain("\n" + prefix + key + ":"), prefix + key);
            Assert.That(en, Does.Contain("\nScripts.Players.Rent.NotEnoughMoney:"));
            Assert.That(en, Does.Contain("\nScripts.Players.Rent.NotEnoughGuildMoney:"));
            Assert.That(en, Does.Contain("\nScripts.Players.Bountyrent.NotEnoughBp:"));
            Assert.That(en, Does.Contain("\nScripts.Players.Bountyrent.NotEnoughGuildBp:"));
        }

        // /bountyrent pays the player's own house, like /rent

        [Test]
        public void BountyRent_PaysThePlayersOwnHouseLikeRent()
        {
            Assert.That(typeof(BountyRentCommandHandler).BaseType, Is.EqualTo(typeof(AbstractRentCommandHandler)));
            Assert.That(typeof(RentCommandHandler).BaseType, Is.EqualTo(typeof(AbstractRentCommandHandler)));
            MethodInfo onCommand = typeof(BountyRentCommandHandler).GetMethod("OnCommand");
            Assert.That(onCommand.DeclaringType, Is.EqualTo(typeof(AbstractRentCommandHandler)), "one OnCommand for both commands");

            string handler = HandlerSource();
            Assert.That(Method(handler, "private void PayPersonal(GameClient client, long? requested)"), Does.Contain("HouseMgr.GetHouseByPlayer(player)"));
            Assert.That(handler, Does.Not.Contain("CurrentHouse"), "no longer the house the player stands near");
            Assert.That(File.Exists(Path.Combine(Root(), "commands", "playercommands", "bountyrent.cs")), Is.False);
            Assert.That(File.Exists(Path.Combine(Root(), "scripts", "commands", "rent.cs")), Is.False);
        }

        [Test]
        public void BountyRentGuild_TakesTheGuildsBountyPoints()
        {
            Guild guild = GuildWith(bountyPoints: 10);
            Assert.That(BountyRentCommandHandler.ChargeGuildBountyPoints(guild, 11), Is.False);
            Assert.That(guild.BountyPoints, Is.EqualTo(10));
            Assert.That(BountyRentCommandHandler.ChargeGuildBountyPoints(guild, 0), Is.False);
            Assert.That(BountyRentCommandHandler.ChargeGuildBountyPoints(guild, 10), Is.True);
            Assert.That(guild.BountyPoints, Is.Zero);
            Assert.That(BountyRentCommandHandler.ChargeGuildBountyPoints(null, 1), Is.False);
            Assert.That(BountyRentCommandHandler.GuildAnnouncement("Aldric", 1), Is.EqualTo("Aldric withdrew 1 bounty point to pay the guild house's rent."));
            Assert.That(BountyRentCommandHandler.GuildAnnouncement("Aldric", 4), Is.EqualTo("Aldric withdrew 4 bounty points to pay the guild house's rent."));
        }
    }
}
