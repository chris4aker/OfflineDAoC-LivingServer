---
version: 1.2.3
section: RvR
---
- **Siege with a purpose: rams and trebuchets on a door, at most 3 engines per door** (#612, #666; FarmVibes' report
  and the owner's decision of 2026-11-26). Autonomous gamebots only; players and companions build as before.
  - **Battlegrounds:** gamebots build siege only to break a closed door, and only rams and trebuchets (ram, ram,
    trebuchet). No more catapults and ballistas scattered over the field: the realm holding the keep builds no
    defensive engines, and an attacking realm stops building once the doors are down and joins the assault.
  - **Every keep door, battlegrounds and frontier:** a realm's gamebots stand at most 3 working engines at one door, the
    1.60-1.65 limit ("Only 3 siege weapons can now attack a door at the same time", patch 1.60). Engines already
    standing count, a player's engine included, so the siege stock never puts a 4th on the door; an engine that is
    destroyed or left unmanned frees its place. A player is never stopped from building or using an engine.
  - Kept: friendly gamebots still crew the player's ram. Frontier defenders and the other frontier siege rules are
    unchanged.
  - The log shows each refusal as `RVR_SIEGE action=door_cap` with the door, the engines standing and building, and the
    cap. `UT_SiegeObjectiveCaps612`. **Tests only.**
