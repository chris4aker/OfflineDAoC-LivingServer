---
version: 1.2.3
section: From OpenDAoC upstream: housing, guild and market
---
- **Houses, guilds and the market** (#562, port package X1 step 1; GPL-3.0 code by Baptiste Marie of OpenDAoC-Core,
  ported by hand onto our tree; `UT_HousingGuildMarketPorts`). **Tests only.** Players only: gamebots and companions
  don't use houses, vaults or consignment merchants.
  - **House vaults** (`2ff1aaec3`, `42fd3c4f8`): a vault placed this session is removed with its house instead of
    staying in the world; a vault's contents are read again when its owner changes (house made a guild house, another
    character at the same vault).
  - **Crash and data safety** (`12cffd536`, `7098d8ebe`, `e540603f9`, `1b8aec823`): the guild is saved at once when its
    guild house is removed; moving from an empty consignment slot no longer throws; buying from or withdrawing at a
    merchant whose money record is missing gives an error and moves no money (a buyer used to pay and get nothing);
    a house goes to the guild only after its consignment merchant is picked up.
  - **Guild** (`39b80ce80`, `582ff4db3`, `5c2fdc7bd`): /gc note on an online member sticks; the social window's refresh
    button works for players; the PvP pet dummy guild is made after the guilds load.
  - **Emblems** (`fed1f28ec`, `409c19b3d`, `72245d297`): a guild house with the wrong emblem gets the guild's at start-up;
    a new emblem shows on the consignment merchant's cloak at once; logging out keeps the guild emblem on a member's
    own house (leaving the guild still clears it).
  - **Market search** (`5d3ce2325`, `34e3357ca`, `3f6374371`, `c7d2df30e`): fewer allocations and a single-filter fast
    path, same results in the same order (Realm Exchange and bot economy included).
