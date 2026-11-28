---
version: 1.2.3
section: Houses, guilds and the market
---
- **/rent and /bountyrent** (#562, package X1 step 2, `e4b9e02b1`; owner answer 23A,
  2026-11-27; `UT_RentCommands`). **Tests only.**
  Players only: bots don't own houses.
  - `/rent guild` no longer hands the gold to the player as well as paying the rent, and tells the guild once.
  - Zero, negative and overflowing amounts are refused (an overflowing amount used to put a wrapped sum in the
    lockbox); a guild can spend its whole bank on rent; `/rent guild` and `/bountyrent guild` no longer crash for a
    player with no guild.
  - `/bountyrent` pays your own house, like `/rent`, instead of the house you stand near; `/bountyrent guild` saves the
    guild and tells its members; the bounty point worth shows in gold, silver and copper.
  - New: `/rent personal max`, `/rent guild max`, `/bountyrent personal max` and `/bountyrent guild max` fill the
    lockbox as far as whole gold pieces or bounty points go.
  - The guild bank's add and remove take only positive amounts (`Guild.RemoveFromBank`); the bot guild economy moved
    to it with the same result.
