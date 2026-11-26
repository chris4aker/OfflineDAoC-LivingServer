---
version: 1.2.3
section: Bots and companions
---
- **No Bot Goals file now means exactly the saved defaults** (#669; owner, 2026-11-26: "yes to both"). The last two
  differences of a world without `bot-goals.json` are gone: a level 50 gamebot's goal at a task break is drawn with its
  personality's tilt, and a gamebot the one-minute goal pass finds over every target draws from the Bot Goals shares
  (personality-tilted), both as in a world with the defaults saved. The start-up `BOT_GOALS` row still says
  `source=defaults`, `file` or `override`. Autonomous gamebots only (`UT_BotGoalSettings`). **Tests only.**
