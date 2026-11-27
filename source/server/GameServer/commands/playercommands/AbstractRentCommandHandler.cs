using System;
using DOL.GS.Housing;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using DOL.Language;

namespace DOL.GS.Commands
{
    /// <summary>
    /// /rent and /bountyrent. Port of OpenDAoC-Core e4b9e02b1 (issue #562, step 2; owner answer 23A, 2026-11-27: port as
    /// upstream). Both commands pay the player's own house (personal) or their guild's house (guild), with the same order
    /// of checks; "max" fills the lockbox as far as whole gold / bounty points go. Players only: bots don't own houses.
    /// </summary>
    public abstract class AbstractRentCommandHandler : AbstractCommandHandler, ICommandHandler
    {
        public const string MaxKeyword = "max";

        /// <summary>What <see cref="ResolveAmount"/> decided.</summary>
        public enum AmountResult
        {
            Ok,
            AlreadyMaxMoney,
            TooMuchMoney
        }

        protected abstract long CopperPerUnit { get; }

        // Translation strings.
        protected abstract string KeyPrefix { get; }
        protected abstract string NotEnoughFundsKey { get; }
        protected abstract string NotEnoughGuildFundsKey { get; }

        protected abstract bool TryChargePlayer(GamePlayer player, long amount, long copper);
        protected abstract bool TryChargeGuild(Guild guild, long amount, long copper);
        protected abstract string GetGuildAnnouncement(GamePlayer player, long amount);

        protected virtual void OnPaid(GameClient client, long amount) { }

        /// <summary>
        /// Reads the amount argument. False for a bad rate, a non-number, zero, a negative amount or one whose copper
        /// (amount * unit) would overflow. <paramref name="requested"/> is null for "max".
        /// </summary>
        public static bool TryParseAmount(string arg, long unit, out long? requested)
        {
            requested = null;

            // Reject misconfigured rate.
            if (unit <= 0 || arg == null)
                return false;

            // null means "fill the lockbox to the max".
            if (arg.Equals(MaxKeyword, StringComparison.OrdinalIgnoreCase))
                return true;

            // Rejects non-numbers, zero/negative amounts, and amount * unit overflowing.
            if (!long.TryParse(arg, out long amount) || amount <= 0 || amount > long.MaxValue / unit)
                return false;

            requested = amount;
            return true;
        }

        /// <summary>
        /// How many units (and how much copper) go into a lockbox with <paramref name="freeSpace"/> copper of room.
        /// "max" (null) is the room rounded down to whole units.
        /// </summary>
        public static AmountResult ResolveAmount(long freeSpace, long unit, long? requested, out long amount, out long copper)
        {
            amount = 0;
            copper = 0;

            if (freeSpace <= 0 || unit <= 0)
                return AmountResult.AlreadyMaxMoney;

            if (requested == null)
            {
                amount = freeSpace / unit;

                // Less than one whole unit of room left.
                if (amount <= 0)
                    return AmountResult.AlreadyMaxMoney;
            }
            else
            {
                amount = requested.Value;

                // No overflow here: TryParseAmount limited amount to long.MaxValue / unit.
                if (amount <= 0 || amount > long.MaxValue / unit || amount * unit > freeSpace)
                {
                    amount = 0;
                    return AmountResult.TooMuchMoney;
                }
            }

            copper = amount * unit;
            return AmountResult.Ok;
        }

        /// <summary>The copper of room left in a house's lockbox.</summary>
        public static long LockboxFreeSpace(House house)
        {
            return HouseMgr.GetRentByModel(house.Model) * Properties.RENT_LOCKBOX_PAYMENTS - house.KeptMoney;
        }

        public void OnCommand(GameClient client, string[] args)
        {
            if (client?.Player == null)
                return;

            if (args.Length < 3)
            {
                DisplaySyntax(client);
                return;
            }

            if (!TryParseAmount(args[2], CopperPerUnit, out long? requested))
            {
                DisplaySyntax(client);
                return;
            }

            switch (args[1].ToLower())
            {
                case "personal":
                    PayPersonal(client, requested);
                    break;
                case "guild":
                    PayGuild(client, requested);
                    break;
                default:
                    DisplaySyntax(client);
                    break;
            }
        }

        private void PayPersonal(GameClient client, long? requested)
        {
            GamePlayer player = client.Player;

            House house = HouseMgr.GetHouseByPlayer(player);
            if (house == null)
            {
                Say(client, "NoHouse");
                return;
            }

            if (!house.CanPayRent(player))
            {
                Say(client, "NoPayRentPerm");
                return;
            }

            if (!TryResolveAmount(client, house, requested, out long amount, out long copper))
                return;

            if (!TryChargePlayer(player, amount, copper))
            {
                Say(client, NotEnoughFundsKey);
                return;
            }

            house.KeptMoney += copper;
            house.SaveIntoDatabase();

            player.SaveIntoDatabase();
            client.Out.SendUpdatePoints();

            Say(client, "YouSpend", amount);
            OnPaid(client, amount);
        }

        private void PayGuild(GameClient client, long? requested)
        {
            GamePlayer player = client.Player;

            Guild guild = player.Guild;
            if (guild == null)
            {
                Say(client, "NoGuild");
                return;
            }

            House house = HouseMgr.GetHouse(guild.GuildHouseNumber);
            if (house == null || !house.DatabaseItem.GuildHouse)
            {
                Say(client, "NoGuildHouse");
                return;
            }

            if (!house.CanPayRent(player))
            {
                Say(client, "NoPayRentPerm");
                return;
            }

            if (!TryResolveAmount(client, house, requested, out long amount, out long copper))
                return;

            if (!TryChargeGuild(guild, amount, copper))
            {
                Say(client, NotEnoughGuildFundsKey);
                return;
            }

            house.KeptMoney += copper;
            house.SaveIntoDatabase();
            guild.SaveIntoDatabase();

            guild.SendMessageToGuildMembers(GetGuildAnnouncement(player, amount), eChatType.CT_Guild, eChatLoc.CL_ChatWindow);

            Say(client, "YouSpendGuild", amount);
            OnPaid(client, amount);
        }

        private bool TryResolveAmount(GameClient client, House house, long? requested, out long amount, out long copper)
        {
            switch (ResolveAmount(LockboxFreeSpace(house), CopperPerUnit, requested, out amount, out copper))
            {
                case AmountResult.AlreadyMaxMoney:
                    Say(client, "AlreadyMaxMoney");
                    return false;
                case AmountResult.TooMuchMoney:
                    Say(client, "TooMuchMoney");
                    return false;
                default:
                    return true;
            }
        }

        private void Say(GameClient client, string keySuffix, params object[] args)
        {
            DisplayMessage(client, LanguageMgr.GetTranslation(client.Account.Language, $"{KeyPrefix}.{keySuffix}", args));
        }
    }

    [Cmd("&rent",
        ePrivLevel.Player,
        "Pay house rent",
        "Use /rent <personal | guild> <gold> to pay.",
        "Use /rent <personal | guild> max to fill the lockbox to the max.")]
    public class RentCommandHandler : AbstractRentCommandHandler
    {
        public const long CopperPerGold = 10000;

        protected override long CopperPerUnit => CopperPerGold; // 1 gold.
        protected override string KeyPrefix => "Scripts.Players.Rent";
        protected override string NotEnoughFundsKey => "NotEnoughMoney";
        protected override string NotEnoughGuildFundsKey => "NotEnoughGuildMoney";

        protected override bool TryChargePlayer(GamePlayer player, long amount, long copper)
        {
            return player.RemoveMoney(copper);
        }

        // The gold leaves the guild bank and goes into the lockbox; it never passes through the player's purse
        // (the guild bank withdraw used before also paid it to the player).
        protected override bool TryChargeGuild(Guild guild, long amount, long copper)
        {
            return ChargeGuildBank(guild, copper);
        }

        public static bool ChargeGuildBank(Guild guild, long copper)
        {
            return guild != null && guild.RemoveFromBank(copper, false);
        }

        protected override string GetGuildAnnouncement(GamePlayer player, long amount)
        {
            return GuildAnnouncement(player.Name, amount);
        }

        public static string GuildAnnouncement(string name, long amount)
        {
            return $"{name} withdrew {amount} gold to pay the guild house's rent.";
        }
    }

    [Cmd("&bountyrent",
        ePrivLevel.Player,
        "Pay house rent with bounty points",
        "Use /bountyrent <personal | guild> <amount> to pay.",
        "Use /bountyrent <personal | guild> max to fill the lockbox to the max.")]
    public class BountyRentCommandHandler : AbstractRentCommandHandler
    {
        protected override long CopperPerUnit => RentBountyPointToCopper;
        protected override string KeyPrefix => "Scripts.Players.Bountyrent";
        protected override string NotEnoughFundsKey => "NotEnoughBp";
        protected override string NotEnoughGuildFundsKey => "NotEnoughGuildBp";

        // RENT_BOUNTY_POINT_TO_GOLD is actually in copper.
        private static long RentBountyPointToCopper => Properties.RENT_BOUNTY_POINT_TO_GOLD;

        public override void DisplaySyntax(GameClient client)
        {
            base.DisplaySyntax(client);
            DisplayBountyPointsWorth(client);
        }

        public override void DisplaySyntax(GameClient client, string subcommand)
        {
            base.DisplaySyntax(client, subcommand);
            DisplayBountyPointsWorth(client);
        }

        public override void DisplaySyntax(GameClient client, string subcommand1, string subcommand2)
        {
            base.DisplaySyntax(client, subcommand1, subcommand2);
            DisplayBountyPointsWorth(client);
        }

        protected override bool TryChargePlayer(GamePlayer player, long amount, long copper)
        {
            if (player.BountyPoints < amount)
                return false;

            player.BountyPoints -= amount;
            return true;
        }

        protected override bool TryChargeGuild(Guild guild, long amount, long copper)
        {
            return ChargeGuildBountyPoints(guild, amount);
        }

        public static bool ChargeGuildBountyPoints(Guild guild, long amount)
        {
            if (guild == null || amount <= 0 || guild.BountyPoints < amount)
                return false;

            guild.BountyPoints -= amount;
            return true;
        }

        protected override string GetGuildAnnouncement(GamePlayer player, long amount)
        {
            return GuildAnnouncement(player.Name, amount);
        }

        public static string GuildAnnouncement(string name, long amount)
        {
            return $"{name} withdrew {amount} bounty point{(amount == 1 ? "" : "s")} to pay the guild house's rent.";
        }

        protected override void OnPaid(GameClient client, long amount)
        {
            DisplayBountyPointsWorth(client, amount);
        }

        private void DisplayBountyPointsWorth(GameClient client, long amount = 1)
        {
            DisplayMessage(client, $"{amount} bounty point{(amount == 1 ? "" : "s")} {(amount == 1 ? "is" : "are")} worth {Money.GetString(amount * RentBountyPointToCopper)}.");
        }
    }
}
