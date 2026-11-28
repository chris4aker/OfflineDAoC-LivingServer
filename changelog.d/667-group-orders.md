---
version: 1.2.3
section: Bots and companions
---
- **Order your group in a fight: `/order attack`, `assist`, `hold`, `regroup`** (#667 step 1).
  - Your `/spawn` and `/raid` helpers, hired and `/bot` companions, and the gamebots in a group you lead obey. A bot on
    `/bot hold`, `/bot stay` or passive keeps to that.
  - **attack**: everyone goes for your selected target now, tanks too, without waiting for a tank to pull, and your pet
    with them. `/defensive`'s range and the RvR leash let it through. Healers keep healing.
  - **assist**: they fight only your fight and switch when you switch. They pick nothing of their own, but each one
    still fights back what hits it, and tanks still protect a groupmate.
  - **hold**: the same as `/hold`, but it ends with the fight like the other orders. `/hold` itself still lasts until
    `/aggressive` or `/defensive`.
  - **regroup**: they break off and come back to you. Once beside you they defend you and the group; one that can't get
    back (rooted, or below half health) fights back what is hitting it. Healers keep healing.
  - An order lasts until your next order or the end of the fight. `/aggressive`, `/defensive` and `/hold` end it too.
    When it ends on its own, your stance from before comes back: `/defensive` or `/aggressive`, and a `/hold` you had on.
  - Give it as a slash command or a macro (`/macro Atk /order attack`), or say the word alone in `/group` (`attack`,
    `assist me`, `hold`, `regroup`). `/order` alone lists the orders.
  - One of your bots answers in `/group`. When an attack in RvR looks like a fight you'd lose, it warns you and goes in
    anyway.
  - Server switch `bot_group_orders`, on by default. Log: `GROUP_ORDER`.
