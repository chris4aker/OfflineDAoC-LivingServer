# OpenDAoC upstream ledger, 2026-11

Every non-merge commit in upstream `OpenDAoC/OpenDAoC-Core` from our base `d51233cb` (2026-03-13) to `428927b33`
(2026-11-23): **632 commits**, each with an area and a status for Living Server. Nothing in this file was ported, built
or run; it is the checklist behind the 1.2.3 upstream ports. The ranked reasoning for the big items is in the harvest
notes that the P1-P4 port briefs use (item numbers such as "harvest A3" or "harvest B9" below refer to them).

## How each commit was judged

- **Presence:** for every commit, its added lines were looked up anywhere in our `source/server` tree and its removed
  lines in our file of the same name (origin/main at the time of writing). Both scores were then read by hand, with
  greps where a commit is small, because one-line commits often match unrelated code. "Already have" means the change
  is in our tree (most of these came in with the earlier boss-script, Spellcraft, Salvage and crafting-title ports).
- **Area:** P1 core, saves, leaks, network and game loop; P2 effects, spells, pets and realm abilities; P3 combat, XP,
  items and crafting; P4 NPC AI, pathing, keeps, sieges, scripted bosses and world. OTHER is everything else (commands,
  housing, guild, chat, client display, quests, tools, docs, build).
- **Status:**
  - *port*: improves accuracy, stability, performance or intelligence for us. Ports are by hand: our code has diverged
    and GameBots are GameNPCs, so every NPC rule keeps the GameBot exemptions.
  - *already have*: in our tree.
  - *skip-not-relevant*: reverted upstream, cleanup or rename only, superseded, the ServiceObjectStore and GameLoop
    sharding series and other engine refactors (AggroTable, Detour.Managed), modern-client support (1.130, RSA) we
    don't need for the 1.127 client on 127.0.0.1, or upstream-only features with nothing to attach to (banker NPCs).
  - *skip-nerf-or-restriction*: weakens monsters, guards or bosses (harvest B9).
  - *hold-for-owner*: a felt gameplay change, a restriction or a mixed NPC change; it waits for the owner's decision.

## Counts

| Status | Commits |
|---|---|
| port | 405 |
| already have | 27 |
| skip-not-relevant | 168 |
| skip-nerf-or-restriction | 6 |
| hold-for-owner | 26 |
| **total** | **632** |

| Area | port | already have | skip-not-relevant | skip-nerf | hold |
|---|---|---|---|---|---|
| P1 core | 56 | 2 | 54 | 0 | 2 |
| P2 effects | 86 | 4 | 13 | 0 | 13 |
| P3 combat | 73 | 10 | 26 | 5 | 9 |
| P4 AI/world | 110 | 10 | 35 | 1 | 2 |
| OTHER | 80 | 1 | 40 | 0 | 0 |

The P1-P4 port branches are `fix/1.2.3-upstream-core`, `-effects`, `-combat` and `-ai-world`. Each takes the
*port* rows of its area from the table below.

## Extra port packages (OTHER area)

The OTHER-area commits worth porting, in four packages. Each has its own issue.

### X1. Housing, guild and market (19 commits)

Issue #562.

Covers house vaults and rent, guild houses and emblems, social window, consignment NREs, market search.

`2ff1aaec3`, `42fd3c4f8`, `12cffd536`, `7098d8ebe`, `39b80ce80`, `582ff4db3`, `f33df01e4`, `8907edea5`, `e540603f9`, `1b8aec823`, `fed1f28ec`, `409c19b3d`, `72245d297`, `5c2fdc7bd`, `e4b9e02b1`, `5d3ce2325`, `34e3357ca`, `3f6374371`, `c7d2df30e`

### X2. Player and GM commands (20 commits)

Issue #563.

Covers /bonuses, /targetstats, /quiver, /stuck, /stats, /ngg, /path, /door, /fixnpcspawn, the invalid-name list, GM login exemptions.

`c9c597a78`, `1a3b12ba7`, `b28922b5e`, `117370277`, `f449b8769`, `0db2d9d9e`, `ec9cfe94d`, `9ff5d3c7e`, `3f0f163a3`, `edb0f6119`, `ea787d744`, `87ff8bafe`, `c0f4c72bb`, `74587b664`, `29a935897`, `b2770fa85`, `a8c4f88a0`, `e845e011f`, `dc81f1276`, `f0aaa1ea2`

### X3. Client display, chat and group window (26 commits)

Issue #564.

Covers 1.127 chat filter window, spell and item tooltips and delve, character sheet AF, group minimap and group window, portal stone war map.

`2951a879d`, `13757bf3d`, `5429dbd62`, `476059682`, `d15534653`, `f3943156a`, `1a97fbde9`, `96412d49d`, `10d3897f0`, `6af0e9619`, `a9e0c3af2`, `5725446af`, `a65afbb8a`, `e3fe8dea1`, `7b7d11f2a`, `b6317804f`, `22c7e9de8`, `42bf098af`, `f5f3e4e43`, `95e394d35`, `06cb67af3`, `a376d6182`, `446a99f1f`, `d74d4941f`, `ec097f507`, `ef017586c`

### X4. Quests, gravestones and timed services (15 commits)

Issue #565.

Covers quest-list locking, group mission text, RolloverSchedulerService and periodic quests, persistent gravestones and /hidegraves, the Parthanan farm and Hastener NPC fixes.

`e688e984e`, `7df3fb4af`, `1b512ad2a`, `43924def4`, `5ad7dfb9c`, `ebc6a16d6`, `25c8e9c5e`, `73cd88797`, `e10348f31`, `974fae7dd`, `22197f1f6`, `cdb4384c0`, `63f46636d`, `0837f95e1`, `7384fed04`

Notes: X1 and X4 share the `RolloverSchedulerService` (`e688e984e`), which the house rent service and the gravestone
decay need, so X4 lands first or X1 takes it along. The gravestone table is saved progress: add it to
`progress-policy.json`, give it a world-update step and make the release builder empty it.

## Held for the owner

Each of these waits for a decision. None is ported until the owner says so.

- `4d486cd32` (P2 effects): removes the Atlas shortcut where a self-cast REALM spell also hits group and pets; bot and player group buffing change.
- `dadcf6ba1` (P1 core): turns ENABLE_PVE_SPEED off by default (removes the 25% out-of-combat run speed).
- `7ca97273d` (P2 effects): harvest B2, live power pool formula.
- `5be873fd2` (P1 core): area-check perf rewrite that also deletes RealmTimer (/realmtimer, used in our player code).
- `428ca2832` (P2 effects): halves GROUND_TARGET_SNAP_MAX_DISTANCE (32 to 16): more ground targets rejected.
- `44ae62e96` (P4 AI/world): NPC anti-exploit: unreachable mobs reset; upstream says not live-like; may fire on bot pulls.
- `fddf0475b` (P4 AI/world): follow-up of the held anti-exploit 44ae62e96.
- `970282fba` (P2 effects): pets stop inheriting owner speed buffs; bot pets may lag sprinting bots.
- `7b1fa00c7` (P2 effects): harvest B1 (live interrupt series; removes INTERRUPT_TIMEOUT and Archery.CasterIsAttacked).
- `5fb4f6a3e` (P2 effects): harvest B1 prep.
- `d17195fd7` (P2 effects): harvest B1 prep.
- `21eda0f90` (P2 effects): harvest B1 (live-like spell interrupt).
- `79bba259b` (P3 combat): harvest B1 (live-like ranged interrupt).
- `da099207f` (P2 effects): harvest B1.
- `2ffc5c0ff` (P2 effects): interrupt chance per level difference 3% to 5%: grey and green mobs interrupt less (unsourced).
- `46d816439` (P2 effects): harvest B1 regression fix (focus spells).
- `441b8333d` (P3 combat): harvest B1 regression fix (Critical Shot, Longshot).
- `ecaf945d0` (P3 combat): harvest B9: NPC defense penetration redesign (mixed effect).
- `eb06c09aa` (P2 effects): harvest B6: grouped stealthers stop seeing each other (1.65 rule); human player plus stealther bots.
- `8721f20a1` (P3 combat): harvest B9: NPCs block without a shield (stronger) but lose the arrow block bonus.
- `9c161ae20` (P3 combat): harvest B9: NPC evade loses the arrow malus and dual-wield penetration (mixed).
- `5090fa4fc` (P3 combat): follows the B9 NPC defense changes (also fixes /targetstats factors).
- `e6fe1661a` (P2 effects): Necromancer pets no longer get parry from dexterity (weaker player pets).
- `ab90dbf12` (P3 combat): harvest B9: NPCs lose the attacker-count defense penalty (stronger NPCs; GameBots need the exemption).
- `58bc5cdff` (P3 combat): dexterity stops adding to melee resists, for every character.
- `738f82fa1` (P3 combat): dexterity's magic resist share stops shortening CC, for every character.

## Skipped as nerfs

- `098878c01`: lowers out-of-combat NPC health regen 12.5% to 10% (harvest B9 group).
- `dee9ee086`: harvest B9: lowers NPC damage at levels 0-10 by 5-28%.
- `7539cebb9`: harvest B9: two-handed damage bonus no longer applies to NPCs.
- `b0fd5c055`: harvest B9: NPCs lose two-handed defense penetration.
- `c3067e84d`: harvest B9: NPCs lose spec AF and the Paladin AF chant.
- `6ef9a089f`: harvest B9: con and dex matter less for NPC melee absorb.

## All commits

Oldest first. "Reason" is given for skipped and held commits; for ports it names the package or the harvest item when
there is one.

| # | SHA | Date | Subject | Area | Status | Reason |
|---|---|---|---|---|---|---|
| 1 | `3104fc400` | 2026-03-13 | Revert ed2ac4de69942d4a7b950d99b147330f5de8a620 Fixes effects remaining ServiceObjectStore, causi... | P2 effects | already have | in our tree |
| 2 | `52b44cb7a` | 2026-03-13 | `CrowdControlECSEffect`: Fix forced weapon sheathe on CC start Maybe it should still interrupt ra... | P2 effects | port |  |
| 3 | `03136b512` | 2026-03-13 | `RangeAttackComponent`: Fix incorrect combat state check It should only check if the target is ac... | P3 combat | port |  |
| 4 | `c9c597a78` | 2026-03-13 | Fix incorrect handling of secondary resists in /bonuses | OTHER | port | X2 |
| 5 | `f88ce5f8b` | 2026-03-14 | `AttackAction`: Hide `NextTick`, add `AttackRoundEndTime`. Using NextTick to tell when the curren... | P3 combat | port | prereq for the block-round and archery fixes |
| 6 | `0b618ff66` | 2026-03-14 | `RangeAttackComponent`: Change how the target's attack round end time is resolved Now uses Attack... | P3 combat | port |  |
| 7 | `3e81de2d6` | 2026-03-14 | `AttackData`: Remove unused `StartTime` | P3 combat | skip-not-relevant | cleanup only |
| 8 | `0dda3dbcb` | 2026-03-14 | `AttackAction`: Add helper method `IsAttackRoundFinished` Updated RangeAttackComponent. Updated N... | P3 combat | port | prereq (IsAttackRoundFinished) |
| 9 | `a36db6f43` | 2026-03-14 | Add evade chance reduction from multiple attackers It was a flat -3% per attacker beyond the firs... | P3 combat | port | harvest B3 |
| 10 | `9469bd9ff` | 2026-03-14 | `GameSummonedPet`: ScalSpells -> ScaleSpells | P2 effects | skip-not-relevant | rename only |
| 11 | `8e5a3f458` | 2026-03-14 | `TheurgistAirPet(Brain)`: Rework stun cast logic Instead of disabling the spell for a random dura... | P2 effects | port |  |
| 12 | `8a7a97776` | 2026-03-14 | Change spell on cooldown message to be more live-like. * PacketLib168.SendDisableSkill: No longer... | P2 effects | port |  |
| 13 | `73a3cbc18` | 2026-03-14 | `SpellHandler.CheckBeginCast`: Add range check on spells targeting pets Unclear if this is live l... | P2 effects | already have | in our tree (SpellHandler range check) |
| 14 | `4d486cd32` | 2026-03-14 | `SpellHandler`: Revert Atlas feature allowing REALM spells to target the whole group and pets whe... | P2 effects | hold-for-owner | removes the Atlas shortcut where a self-cast REALM spell also hits group and pets; bot and player group buffing change |
| 15 | `7d7ec3d41` | 2026-03-14 | Fix taunt spells not cancelling movement speed buffs immediately On both the caster and the targe... | P2 effects | port |  |
| 16 | `3fe1c50c6` | 2026-03-18 | `GameLoop`: Add `DegreeOfParallelism` property | P1 core | skip-not-relevant | GameLoop sharding series (harvest D); not needed for one player |
| 17 | `bea015c8a` | 2026-03-18 | `GameLoopThreadPool`: Add `ExecuteForEachSharded` Takes an array of start indices, and processes... | P1 core | skip-not-relevant | GameLoop sharding series (harvest D) |
| 18 | `4975cdc51` | 2026-03-18 | `GameLoop`: Add `ExecuteForEachSharded` Changed signature of ExecuteForEach to take IReadOnlyList. | P1 core | skip-not-relevant | GameLoop sharding series (harvest D) |
| 19 | `b078d8e6b` | 2026-03-18 | Redesign `ServiceObjectStore` and related classes * See comment in ServiceObjectStore. * Changed... | P1 core | skip-not-relevant | ServiceObjectStore redesign (harvest D); engine refactor |
| 20 | `7f3cff726` | 2026-03-18 | `ABrain`: Change to `IShardedServiceObject` * Added Schedule. * Made NextThinkTick setter private... | P1 core | skip-not-relevant | reverted upstream (dbd1a454a) |
| 21 | `ed9b51c9b` | 2026-03-18 | `ECSGameTimer`: Change to `IShardedServiceObject` * Updated ServiceObjectStore to process ECSGame... | P1 core | skip-not-relevant | reverted upstream (bba919379) |
| 22 | `a8c06b0ce` | 2026-03-18 | `ECSGameEffect`: Change to `IShardedServiceObject` * Added GetNextTick. * Updated ECSGameAbilityE... | P1 core | skip-not-relevant | reverted upstream (e2dc89783) |
| 23 | `5eb7741f0` | 2026-03-19 | `NpcService`: Add missing call to `ABrain.Schedule` | P1 core | skip-not-relevant | reverted upstream (5496fb2a0) |
| 24 | `5356d4930` | 2026-03-20 | `ECSGameEffect`: Fix DD+snare not being overwritten by roots This changes overwriting rules for s... | P2 effects | port | harvest A4 |
| 25 | `5496fb2a0` | 2026-03-21 | Revert "`NpcService`: Add missing call to `ABrain.Schedule`" | P1 core | skip-not-relevant | revert of a skipped commit |
| 26 | `e2dc89783` | 2026-03-21 | Revert "`ECSGameEffect`: Change to `IShardedServiceObject`" | P1 core | skip-not-relevant | revert; net zero with a8c06b0ce |
| 27 | `bba919379` | 2026-03-21 | Revert "`ECSGameTimer`: Change to `IShardedServiceObject`" | P1 core | skip-not-relevant | revert; net zero with ed9b51c9b |
| 28 | `dbd1a454a` | 2026-03-21 | Revert "`ABrain`: Change to `IShardedServiceObject`" | P1 core | skip-not-relevant | revert; net zero with 7f3cff726 |
| 29 | `5969de844` | 2026-03-22 | Amend 5496fb2a023b31e61b2c09182b3d407faa1dd34d Incomplete revert. | P1 core | skip-not-relevant | amends a revert; net zero |
| 30 | `98e066eed` | 2026-03-23 | Fix typo | P2 effects | port | RA and language typos |
| 31 | `879ac3090` | 2026-03-23 | Fix Speed of Sound not being allowed to be cast if the caster already has one active | P2 effects | port | harvest A15 |
| 32 | `3374caf2c` | 2026-03-23 | `EffectListComponent`: Add focus damage shields to `PulseSpellGroupsIgnoringOtherPulseSpells` UPD... | P2 effects | port | needs the SpellGroup DB update in the commit message (world update) |
| 33 | `ecf8e264c` | 2026-03-23 | `EffectListComponent`: Change `PulseSpellGroupsIgnoringOtherPulseSpells` to a hashset | P2 effects | port | follow-up of 3374caf2c |
| 34 | `fe2c80e4b` | 2026-03-23 | `GameLoopThreadPool`: Change IReadOnlyList parameters to List Partially reverts bea015c8a21550a9e... | P1 core | skip-not-relevant | GameLoop sharding series (harvest D) |
| 35 | `e6888807e` | 2026-03-23 | Redesign `SchedulableServiceObjectArray` * Note: Currently still not used. It's not clear if it's... | P1 core | skip-not-relevant | ServiceObjectStore series; unused upstream at the time |
| 36 | `107444159` | 2026-03-24 | `EffectService`: Optimize next tick check The code in TickInternal is relatively expensive compar... | P2 effects | port | effect service tick cost; helps large bot counts |
| 37 | `7ab320291` | 2026-03-24 | `TimeDependentSpawnNpc`: Fix time dependent NPCs becoming invisible when charmed | P4 AI/world | port |  |
| 38 | `6b986f8c3` | 2026-03-25 | `AttackAction`: Fix regression related to `BlockRoundHandler` Caused by f88ce5f8bda7c42ee54cdc3dc... | P3 combat | port | regression fix for f88ce5f8b |
| 39 | `109a38cf0` | 2026-03-25 | `AttackAction`: Fix Rapid Fire timing with block rounds and too short interrupt timer | P3 combat | port | harvest A17 |
| 40 | `2ff1aaec3` | 2026-03-26 | `HousingPlaceItemHandler`: Fix new vaults not being added to `House.HousepointItems` They were on... | OTHER | port | X1 |
| 41 | `8cff38c54` | 2026-03-26 | `House.CanUseVault`: Allow even if `player.CurrentHouse != this` House vault should be accessible... | OTHER | skip-not-relevant | only needed by the banker NPCs (skipped) |
| 42 | `f2adbd319` | 2026-03-26 | `House`: Add a way to retrieve vaults by index | OTHER | skip-not-relevant | only needed by the banker NPCs (skipped) |
| 43 | `8f34bc228` | 2026-03-26 | `AccountVault`: Minor clean up | OTHER | skip-not-relevant | cleanup only |
| 44 | `1fade9d77` | 2026-03-26 | `WeaponAction`: Fix damage adds and shields inflicting damage on the hit that proc'ed them | P3 combat | already have | in our tree |
| 45 | `42fd3c4f8` | 2026-03-26 | `GameVault.ItemCache`: Fix cache validation on house ownership change | OTHER | port | X1 |
| 46 | `89e0378b1` | 2026-03-26 | Implement housing vault / guild vault bankers | OTHER | skip-not-relevant | new vault banker NPCs; not 1.65, no world rows for them |
| 47 | `3326e0906` | 2026-03-26 | `VaultBanker`, `AccountVaultKeeper`: Minor clean up | OTHER | skip-not-relevant | banker cleanup |
| 48 | `101987596` | 2026-03-26 | `VaultBanker`: Split into multiple files | OTHER | skip-not-relevant | banker NPC refactor |
| 49 | `e67d065f7` | 2026-03-26 | Implement consignment merchant bankers This is a bit tricky because of how stiff ConsignmentMerch... | OTHER | skip-not-relevant | consignment banker NPCs; not 1.65 |
| 50 | `ee5219b87` | 2026-03-26 | Rename banker NPC related classes XBanker then BankerX was confusing. | OTHER | skip-not-relevant | banker rename |
| 51 | `9ec15471d` | 2026-03-26 | `ConsignmentBanker`: Fix interaction Requires a valid position for base.Interact. | OTHER | skip-not-relevant | banker fix |
| 52 | `0942f273a` | 2026-03-26 | Rename VaultBankerX => PersonalVaultBankerX | OTHER | skip-not-relevant | banker rename |
| 53 | `12cffd536` | 2026-03-26 | `HouseMgr`: Save guild on guild house removal A desync could occur on server crash if occuring af... | OTHER | port | X1 (save guild on guild-house removal; crash safety) |
| 54 | `7098d8ebe` | 2026-03-26 | `GameInventoryObject`: Replace null return Fixes couple of NREs in ConsignmentMerchant | OTHER | port | X1 (NREs in ConsignmentMerchant) |
| 55 | `7b69013c3` | 2026-03-27 | `LosCheckHandler.LosCheckKey`: Change to a record | P4 AI/world | skip-not-relevant | refactor only |
| 56 | `6bde6d02f` | 2026-03-27 | `AccountVaultKeeper`: Fix regression caused by 3326e0906b90ce661944afd24c1b3217051edb3a Incorrect... | OTHER | skip-not-relevant | fixes a skipped banker commit |
| 57 | `7f9e09059` | 2026-03-27 | Fix banker NPCs issues * Made VaultBanker call Interact instead of sending the inventory itself.... | OTHER | skip-not-relevant | banker fixes |
| 58 | `74a58ec24` | 2026-03-27 | Redesign `ConsignmentMerchant` and `GameVault` to allow multiple instances to point to the same i... | OTHER | skip-not-relevant | consignment and vault redesign for bankers; large, no gain without them |
| 59 | `dadcf6ba1` | 2026-03-28 | Change `ENABLE_PVE_SPEED` default value 1.75 feature apparently | P1 core | hold-for-owner | turns ENABLE_PVE_SPEED off by default (removes the 25% out-of-combat run speed) |
| 60 | `93efec0e0` | 2026-03-28 | `VaultBanker`: Fix potential NRE | OTHER | skip-not-relevant | banker fix |
| 61 | `b83dce246` | 2026-03-28 | Refactor vault bankers, introduce `MasterVaultBanker` Exposed House.MAX_VAULT_COUNT | OTHER | skip-not-relevant | banker refactor |
| 62 | `f21d2f934` | 2026-03-28 | Refactor consignment bankers, introduce `MasterConsignmentBanker` | OTHER | skip-not-relevant | banker refactor |
| 63 | `4cdce10fd` | 2026-03-29 | `SureShotECSGameEffect`: Fix `EffectType` | P2 effects | port |  |
| 64 | `cca2c2b0a` | 2026-03-29 | `ClassScout`, `ClassRanger`, `ClassHunter`: Remove `OnSkillTrained` override No longer needed, ab... | OTHER | skip-not-relevant | cleanup that depends on upstream spec changes we lack |
| 65 | `cf8468047` | 2026-03-29 | Ensure temporary houses created bankers can't be saved / loaded Refactored `House`, added `NullHo... | OTHER | skip-not-relevant | banker-only (temporary houses) |
| 66 | `b2014290d` | 2026-03-30 | Revert "Fix for bad power calcs in Utility" | P3 combat | already have | in our tree |
| 67 | `71a52fe94` | 2026-03-30 | `GameInventoryItem`: Fix total utility calculation | P3 combat | port |  |
| 68 | `abb7aecd0` | 2026-04-01 | `StandardMobBrain`: Pre-add pet owner to aggro list on pet pull Fixes nondeterministic aggro beha... | P4 AI/world | port | harvest A9 |
| 69 | `f5afdcc52` | 2026-04-01 | `StandardMobBrain`: Fix body pulling adding pet owners and group members to aggro list Only attac... | P4 AI/world | port | harvest A9 |
| 70 | `b80fae038` | 2026-04-01 | `LocalPathfindingMgr`: Try to fix couple of NF doors not registering correctly Increased half ext... | P4 AI/world | port |  |
| 71 | `76f4a6fd0` | 2026-04-01 | `Pathfinder`: Make NPCs stop in front of blocking doors Intended 1.65 behavior it seems. Reverts... | P4 AI/world | port | harvest A7 (partly in) |
| 72 | `d5a28654c` | 2026-04-01 | `NpcMovementComponent`: Fix rare case of NPCs stopping moving | P4 AI/world | port |  |
| 73 | `875c2b745` | 2026-04-02 | Attempt to lessen NPC movement stuttering 12929ba418a750cfefd3143b29b50b947b5d8155 removed early... | P4 AI/world | port |  |
| 74 | `615649e6a` | 2026-04-02 | Amend previous commit | P4 AI/world | port | amends 875c2b745 |
| 75 | `1720c5f37` | 2026-04-02 | `Pathfinder`: Refactor `TryGetNextNode` | P4 AI/world | port | prereq refactor |
| 76 | `bab5ab612` | 2026-04-02 | `NpcMovementComponent`: Simplify `FollowTick` Calculating a midway point is no longer necessary.... | P4 AI/world | port |  |
| 77 | `10fb80ffe` | 2026-04-02 | `NpcMovementComponent`: Fix performance regression * Re-introduced _walkingToEstimatedArrivalTime... | P4 AI/world | port | perf regression fix (partly in) |
| 78 | `c7939b269` | 2026-04-03 | `Pathfinder`: Opimize by dequeuing nodes we have LoS o and ha don' deviae oo much verically Repla... | P4 AI/world | port |  |
| 79 | `6974d96d1` | 2026-04-03 | `NpcMovementComponent`: Make NPCs "fly" when PathVisualization is active For debugging purposes. | P4 AI/world | skip-not-relevant | debug-only path visualization |
| 80 | `188401998` | 2026-04-03 | `GameObject.HealthPercent`: Remove redundant call to MaxHealth | P1 core | port |  |
| 81 | `6fa373100` | 2026-04-03 | Amend previous commit | P1 core | port | amends 188401998 |
| 82 | `2951a879d` | 2026-04-03 | Implement `ClientFilterSettingsHandler` (fixes #62) | OTHER | port | X3 (1.127 chat filter settings window) |
| 83 | `13757bf3d` | 2026-04-03 | `IPacketLib.eChatType`: Clean up to match 1.127's settings window | OTHER | port | X3 (chat types for the 1.127 settings window) |
| 84 | `5429dbd62` | 2026-04-03 | (experimental) `PacketLib1127`: Only append @@ on send messages Modern clients (1.127) seems to d... | OTHER | port | X3 (1.127 @@ on sent messages; marked experimental upstream) |
| 85 | `1a3b12ba7` | 2026-04-05 | `FixNpcSpawn`: Fix too lenient distance calculation caused by GetClosestPoint | OTHER | port | X2 (GM /fixnpcspawn) |
| 86 | `9edeaf745` | 2026-04-05 | `PathFinder`: Re-add NODE_REACHED_DISTANCE_STRICT and early exit in GetNextNode Solves (?) NPCs l... | P4 AI/world | port |  |
| 87 | `ec21d0945` | 2026-04-06 | `NpcMovementComponent`: Snap position to reached node | P4 AI/world | port |  |
| 88 | `4c26798df` | 2026-04-06 | Convert readonly struct to readonly record struct where relevant Only ServiceObjectArrayBase.Serv... | P1 core | skip-not-relevant | record struct cleanup |
| 89 | `f98006290` | 2026-04-06 | `LanguageMgr`: Minor clean up | OTHER | skip-not-relevant | cleanup only |
| 90 | `7ca97273d` | 2026-04-07 | Update power pool formula to be more live-like The new formula drastically reduces power pools at... | P2 effects | hold-for-owner | harvest B2, live power pool formula |
| 91 | `39502cb35` | 2026-04-07 | `EffectListComponent`: Change how pulsing effects are handled when refreshed * Will now be stoppe... | P2 effects | port | harvest A4 |
| 92 | `39b80ce80` | 2026-04-07 | Fix /guild note on online guild members | OTHER | port | X1 (/guild note) |
| 93 | `582ff4db3` | 2026-04-07 | Fix social window's refresh button not refreshing correctly on non-GM accounts | OTHER | port | X1 (social window refresh) |
| 94 | `e086c72df` | 2026-04-09 | Redesign H2H swing chance formula Moving away from unsourced DoL's one. | P3 combat | skip-not-relevant | superseded by 068076224 (original H2H formula restored) |
| 95 | `addb48ef7` | 2026-04-10 | `PathfindingMgrBase`:  Reduce redundant allocations | P4 AI/world | port |  |
| 96 | `625c13663` | 2026-04-10 | Fix fallback for NPC follow logic and ground target selection when navmesh is missing | P4 AI/world | port |  |
| 97 | `ff61424b7` | 2026-04-11 | `NpcAttackAction`: Attempt to fix potential LoS issues Unlikely to be the cause of some reported... | P4 AI/world | port |  |
| 98 | `302e8a8f3` | 2026-04-11 | `Volley`: Check Zone.IsPathfindingEnabled in case VOLLEY_ROOF_CHECK is true but with no navmesh l... | P2 effects | port |  |
| 99 | `b28922b5e` | 2026-04-11 | Fix /quiver dump Removed redundant checks in PlayerMoveItemRequestHandler.DropItem Shortened lock... | OTHER | port | X2 (/quiver dump; take with 117370277) |
| 100 | `c58216229` | 2026-04-11 | Cleanup Region.cs | P4 AI/world | skip-not-relevant | Region cleanup only |
| 101 | `5be873fd2` | 2026-04-11 | Optimize area checks, remove RealmTimer * Now mostly lock and allocation free, with the caveat th... | P1 core | hold-for-owner | area-check perf rewrite that also deletes RealmTimer (/realmtimer, used in our player code) |
| 102 | `10ecc81f0` | 2026-04-12 | Re-redesign H2H swing chance formula Some recent discoveries give a good hint about how the game... | P3 combat | skip-not-relevant | superseded by 068076224 |
| 103 | `94f2c5315` | 2026-04-13 | Adjust H2H formula | P3 combat | skip-not-relevant | reverted upstream (b7ce25aef) |
| 104 | `b7ce25aef` | 2026-04-13 | Revert "Adjust H2H formula" Made all double hits come from the main hand at low spec. | P3 combat | skip-not-relevant | revert; net zero |
| 105 | `47c534034` | 2026-04-13 | Fix occasional item "loss" when splitting item stacks between player intenventory and account/hou... | P1 core | already have | in our tree |
| 106 | `068076224` | 2026-04-13 | Restore original H2H formula, remove triple and quad unlock levels The new formula made most doub... | P3 combat | port | harvest B7 |
| 107 | `16fa2c467` | 2026-04-14 | Fix heal procs having variance They don't have any on live, and some old post (late SI / early To... | P2 effects | port | harvest A6 |
| 108 | `d352aa1c9` | 2026-04-14 | Check for existing stuns and mezs on expiration before flippig flag Fixes an issue with players b... | P2 effects | port | harvest A4 |
| 109 | `c03822a01` | 2026-04-14 | Fix instant debuffs able to interrupt bypassing QC, MoC, PF | P2 effects | port | harvest A4 |
| 110 | `84f1a46a6` | 2026-04-15 | `GenericBase`: Cleanup, rename, fix memory leak, fix timer starting too early * GenericBase => Re... | P1 core | port | harvest A3 leak bundle |
| 111 | `a0f409cb1` | 2026-04-15 | Optimize dynamic spell tooltip update | P2 effects | port |  |
| 112 | `117370277` | 2026-04-16 | Fix dropping item no longer working Regression caused by b28922b5e34a958123394ae2600b12343d8c0c9f | OTHER | port | X2 (regression fix for b28922b5e) |
| 113 | `9ef308e1c` | 2026-04-15 | Switched from logger to Console.WriteLine() | OTHER | skip-not-relevant | console test harness (CoreServer console client) |
| 114 | `5e0520adb` | 2026-04-15 | Added reusable mock client/account/player | OTHER | skip-not-relevant | console test harness |
| 115 | `ab62e2f7a` | 2026-04-16 | `ConsolePacketLib`: Minor clean up | OTHER | skip-not-relevant | console test harness |
| 116 | `a091ca30e` | 2026-04-16 | `ConsoleStart`: Minor clean up | OTHER | skip-not-relevant | console test harness |
| 117 | `527f0aadf` | 2026-04-16 | `ConsoleStart`: Add ProcessCommand method To help solve some merge conflicts with Blackthorn. | OTHER | skip-not-relevant | console test harness (Blackthorn merge aid) |
| 118 | `f449b8769` | 2026-04-16 | Disable /ngg Takes a while to load and permanently allocates over 150MB. | OTHER | port | X2 (/ngg allocates 150 MB; disable or lazy-load) |
| 119 | `e8889f7b4` | 2026-04-16 | `StatPrint`: Initialize asynchronously Initialization can take a while, and it's fine if the serv... | P1 core | port | partly in |
| 120 | `428ca2832` | 2026-04-16 | Lower GROUND_TARGET_SNAP_MAX_DISTANCE default value | P2 effects | hold-for-owner | halves GROUND_TARGET_SNAP_MAX_DISTANCE (32 to 16): more ground targets rejected |
| 121 | `e688e984e` | 2026-04-17 | Introduce `RolloverSchedulerService`, `PeriodicQuestService` * PeriodicQuestService registers cal... | OTHER | port | X4 (RolloverSchedulerService; prereq for gravestones and /rent) |
| 122 | `7df3fb4af` | 2026-04-17 | `PeriodicQuestService`: Minor clean up | OTHER | port | X4 |
| 123 | `1b512ad2a` | 2026-04-17 | `PeriodicQuestService`: Add missing static Just because I like it, makes it obvious there's no cl... | OTHER | port | X4 |
| 124 | `0db2d9d9e` | 2026-04-17 | Fix /bonuses not handling eProperty.MaxMana correctly * Fixed incorrect eProperty.PowerPool cap (... | OTHER | port | X2 (/bonuses MaxMana) |
| 125 | `dfc2e6fc0` | 2026-04-17 | `GamePlayerUtils`: Minor clean up | OTHER | skip-not-relevant | cleanup only |
| 126 | `1866e9163` | 2026-04-18 | `GameServer`: Fix InitDiscordClientManager order | P1 core | skip-not-relevant | Discord init order; we don't run Discord |
| 127 | `7aaf9e3ff` | 2026-04-21 | `GamePlayerUtils`: Refactor _bonusesToBeDisplayed | OTHER | skip-not-relevant | refactor only |
| 128 | `43924def4` | 2026-04-23 | PvE/PvP Hastener Fixes | OTHER | port | X4 (Hastener NPC fixes) |
| 129 | `2f07343d9` | 2026-04-24 | Add /diag tick <on\|off> [threshold] Added `TickMonitor` ref struct. Added `Diagnostics.EnableTic... | P1 core | skip-not-relevant | /diag tick tooling; we have our own tick tools (PERFORMANCE.md) |
| 130 | `a4d910e1d` | 2026-04-24 | `TickMonitor` Make readonly | P1 core | skip-not-relevant | follow-up of skipped /diag tick |
| 131 | `214061a55` | 2026-04-26 | Update NuGet packages | OTHER | skip-not-relevant | NuGet bump; ours is managed separately (.NET 10) |
| 132 | `276a477a4` | 2026-04-27 | `NpcAttackAction.CheckLosTimer`: Attempt to fix NRE | P4 AI/world | port |  |
| 133 | `70b46ba9c` | 2026-04-27 | Fix incorrect usage of Properties.BUFF_RANGE 0 should disable the range check. Renamed to CONCENT... | P2 effects | port | BUFF_RANGE 0 should disable the check |
| 134 | `5ad7dfb9c` | 2026-04-27 | `PeriodicQuestService`: Clean up | OTHER | port | X4 |
| 135 | `f33df01e4` | 2026-04-28 | `HouseMgr`: Refactor house rents * Added HouseRentService, ticks every hour via RolloverScheduler... | OTHER | port | X1 (house rent service; needs e688e984e) |
| 136 | `ebc6a16d6` | 2026-04-28 | `PeriodicQuestService`: Fix index passed to ExecuteForEach | OTHER | port | X4 |
| 137 | `8907edea5` | 2026-04-28 | `HouseRentService`: Minor clean up | OTHER | port | X1 |
| 138 | `25c8e9c5e` | 2026-04-28 | Implement gravestone persistence * Rewrote GameGravestone. Supports multiple gravestones per play... | OTHER | port | X4 (gravestones persist, harvest B12; needs progress-policy entry) |
| 139 | `4e4bc8737` | 2026-05-04 | `GamePlayerUtils.UpdateUsableListSpells`: Fix concurrent issue caused by inner lists being mutate... | P2 effects | port | concurrency fix in usable spell lists |
| 140 | `25bcf526e` | 2026-05-05 | `IPacketLib.SendNonHybridSpellLines`: Add updateInternalCache argument Propagated by endUpdatePla... | P2 effects | port |  |
| 141 | `e540603f9` | 2026-05-10 | `ConsignmentState`:  Improve error handling for money Refactored EnsureMoneyLoaded to return a bo... | OTHER | port | X1 |
| 142 | `1b8aec823` | 2026-05-10 | `HouseMgr`: Require consignment merchant removal before house transfer ConsignmentState relies on... | OTHER | port | X1 |
| 143 | `476059682` | 2026-05-18 | Fix SendHookPointStore for 1.127 | OTHER | port | X3 (SendHookPointStore on 1.127, our client) |
| 144 | `d15534653` | 2026-05-18 | Add missing using directive | OTHER | port | X3 |
| 145 | `489b9dcfd` | 2026-06-10 | Fix mismatch between tooltip ID and icon ID causing tooltip issues SendUpdateIcons is meant to se... | P2 effects | port | tooltip and icon ID mismatch |
| 146 | `c3d258810` | 2026-06-10 | `EffectListComponent.GetEffects`: Add a tie breaker for effects with equal StartTick Fixes effect... | P2 effects | port |  |
| 147 | `b14feb39b` | 2026-06-11 | `PlayerEffectListComponent`: Minor clean up | P2 effects | skip-not-relevant | cleanup only |
| 148 | `8ebd3cdbf` | 2026-06-11 | `EffectListComponent`: Minor clean up | P2 effects | skip-not-relevant | cleanup only |
| 149 | `2053b297e` | 2026-06-11 | `EffectListComponent`: (perf) Introduce GetEffects with predicate and GetSortedEffects Most of th... | P2 effects | port | perf, effect lookups |
| 150 | `14bcb2b7a` | 2026-06-11 | `PlayerEffectListComponent`: Restore "changed effects" optimization IPacketLib.SendUpdateIcons is... | P2 effects | port | take with 3330828a8 and 9f0139be2 net |
| 151 | `b92162801` | 2026-06-11 | `EffectListService`: Replace non static lambdas * Added optional eSpellType argument to GetSpellE... | P2 effects | port | perf |
| 152 | `deaf5beaf` | 2026-06-11 | Fix issues with icons and tooltips of spells spawned by abilities Use the values in SpellHandler.... | P2 effects | port |  |
| 153 | `06b6c6fc5` | 2026-06-11 | Fix Bunker of Faith's icon | P2 effects | port |  |
| 154 | `3330828a8` | 2026-06-12 | `EffectListComponent`: Fix possible infinite loop Caused by 14bcb2b7a756b31bc582a451812c26a1ed202ceb | P2 effects | skip-not-relevant | reverted upstream (9f0139be2) |
| 155 | `e3a0c49bf` | 2026-06-12 | Fix effect list not changing the color of effects being disabled / enabled if placed before the e... | P2 effects | port |  |
| 156 | `35e8f2fe5` | 2026-06-12 | `WeaponAction`: Fix AttackRoundEndTime being updated even when the attack fails Fixes Necromancer... | P3 combat | port |  |
| 157 | `9f0139be2` | 2026-06-12 | Revert "`EffectListComponent`: Fix possible infinite loop" 3330828a8be6399391b0bc1905ef98ef8ff9c4... | P2 effects | skip-not-relevant | revert; net zero |
| 158 | `706847173` | 2026-06-12 | `AttackComponent`: Fix Dualist Reflexes for h2h | P3 combat | port | harvest B7 |
| 159 | `410f1d9bb` | 2026-06-16 | Change Reflex Attack based on 1.65 info | P3 combat | port | harvest B7 |
| 160 | `73cd88797` | 2026-06-17 | `RolloverSchedulerService`: (perf) Cache DbScheduledRollover object * Removes the need to peform... | OTHER | port | X4 |
| 161 | `e10348f31` | 2026-06-18 | Introduce GravestoneService * Handles gravestone decay. Set to GRAVESTONE_DECAY_TIME days with a... | OTHER | port | X4 (GravestoneService, harvest B12) |
| 162 | `974fae7dd` | 2026-06-18 | Implement /hidegraves | OTHER | port | X4 (/hidegraves) |
| 163 | `36b74205d` | 2026-06-18 | `AtlasOF_Volley`: Fix area check * A KeepArea is a Area.Circle. Order needs to be inversed. * Cle... | P2 effects | port | harvest A17 (Volley area check) |
| 164 | `1747b6ecd` | 2026-06-18 | Centralize item utility calculation Minor change for the all X skills. Was 10 in GeneratedUniqueI... | P3 combat | port |  |
| 165 | `ec54287b1` | 2026-06-18 | Fix missing equipment update notification to other players * Changed EQUIP_SLOTS and VISIBLE_SLOT... | P3 combat | port | equipment appearance update to other players |
| 166 | `3a2165495` | 2026-06-18 | `CreationStartupEquipment`: Minor clean up | P3 combat | skip-not-relevant | cleanup only |
| 167 | `82e43aa2f` | 2026-06-18 | `GameLoopTickPacer`: Refactor WaitForNextTick for higher spin-wait loop precision Fewer repetitiv... | P1 core | port | tick pacer precision |
| 168 | `408e2de94` | 2026-06-19 | `GameLoopSynchronizationContext`: Refactor to fix ExecutionContext flow * Apparently, .NET restor... | P1 core | port | ExecutionContext flow fix in the game loop sync context |
| 169 | `c284dd9d8` | 2026-06-20 | Refactor block and guard handling * Reduce duplicated calculations. * Guard now benefits from Eng... | P3 combat | port | harvest B3 (Guard with Engage, partly in) |
| 170 | `7977fb3c8` | 2026-06-20 | Amend previous commit | P3 combat | port | amends c284dd9d8 |
| 171 | `08c222009` | 2026-06-20 | `SpellHandler`: Remove unused properties CanQueue, DelveInfoDepth | P2 effects | skip-not-relevant | removes unused properties |
| 172 | `ab1745695` | 2026-06-20 | `GameLoopThreadPoolMultiThreaded`: Fix synchronization context propagation | P1 core | port |  |
| 173 | `c006704a1` | 2026-06-20 | `GameLiving`: Change m_disabledSkills type KeyValuePair<TKey, TValue> does not implement IEquatab... | P1 core | port |  |
| 174 | `f5458fc52` | 2026-06-20 | Reduce allocations caused by foreach loops on interfaces * `IGameLivingInventory`: Changed return... | P1 core | skip-not-relevant | broad allocation refactor across many files; merge risk outweighs gain |
| 175 | `85008c251` | 2026-06-20 | `GameNPC`: Change m_questListToGive from ArrayList to List<T> and QuestListToGive return type. Re... | OTHER | skip-not-relevant | collection type cleanup |
| 176 | `cad6e2dec` | 2026-06-20 | `LootMgr`: Replace legacy collections | P3 combat | skip-not-relevant | collection cleanup |
| 177 | `991206806` | 2026-06-20 | `PilusFuryBrain`: Refactor HandleNpcCheck | P4 AI/world | port |  |
| 178 | `4d5413eee` | 2026-06-21 | `GamePlayer`: Remove unused DisabledCastingTimeout property | P3 combat | skip-not-relevant | removes unused property |
| 179 | `5eeef158d` | 2026-06-21 | Reduce GC pressure in perf diagnostics | P1 core | skip-not-relevant | perf diagnostics GC only |
| 180 | `75f7be646` | 2026-06-21 | Remove redundant work introduced by previous commit | P1 core | skip-not-relevant | follow-up of skipped 5eeef158d |
| 181 | `50027c4f7` | 2026-06-22 | Lower Evade angle to 120 degrees | P3 combat | port | harvest B3 (evade 120 degrees) |
| 182 | `34519baf5` | 2026-06-22 | Lower Guard angle to 120 degrees | P3 combat | already have | in our tree (Guard 120 degrees) |
| 183 | `5c5758504` | 2026-06-23 | `GameLiving.TryBlock`: Update comment | P3 combat | skip-not-relevant | comment only |
| 184 | `8e3cfb833` | 2026-06-23 | `GameServer`: Protect SaveTimerProc against concurrent calls | P1 core | port | SaveTimerProc concurrency (data safety) |
| 185 | `9ba38be6a` | 2026-06-25 | `TorstEddiesBrain`: Fix NRE | P4 AI/world | port |  |
| 186 | `a44b84ca2` | 2026-06-25 | `AttackAction.PrepareRangedAttack`: Use cached weapon instead of accessing GameLiving.ActiveWeapo... | P3 combat | port |  |
| 187 | `091fff242` | 2026-06-25 | `AttackAction`: Minor clean up | P3 combat | skip-not-relevant | cleanup only |
| 188 | `903b608bf` | 2026-06-26 | Improve early Think on brain state change * FSM.SetCurrentState now automatically calls FSMState.... | P4 AI/world | skip-not-relevant | reverted upstream (ca930836c) |
| 189 | `ca930836c` | 2026-06-26 | Revert "Improve early Think on brain state change" | P4 AI/world | skip-not-relevant | revert; net zero |
| 190 | `0f126e897` | 2026-06-26 | Remove `FSM.KillFSM` Dead code | P4 AI/world | skip-not-relevant | dead code removal |
| 191 | `7d4aa4c15` | 2026-06-26 | Make `FSM.StateType` immutable | P4 AI/world | port | prereq for 195c4adb9 |
| 192 | `7e04423df` | 2026-06-26 | Remove explicit `ABrain.Think` calls Meant to be called by NpcService tick only. | P4 AI/world | port | take with 195c4adb9 |
| 193 | `1e11bdd15` | 2026-06-26 | `GameLoop`: Expose currently active service | P1 core | port | prereq for 195c4adb9 |
| 194 | `195c4adb9` | 2026-06-26 | `FSM`: Refactor to allow immediate think on state change Similar concept but much cleaner impleme... | P4 AI/world | port | immediate think on FSM state change |
| 195 | `22197f1f6` | 2026-06-21 | Refactor player quest list locking | OTHER | port | X4 (quest list locking, harvest A17) |
| 196 | `cdb4384c0` | 2026-06-26 | Address quest lookup review feedback | OTHER | port | X4 |
| 197 | `63f46636d` | 2026-06-26 | Refresh group mission task text on membership changes | OTHER | port | X4 (group mission text) |
| 198 | `7b31c6910` | 2026-06-26 | `GameLoopTickPAcer`:  Fix _running never set to false if DYNAMIC_BUSY_WAIT_THRESHOLD was false | P1 core | port | harvest A12 |
| 199 | `b3e7306eb` | 2026-06-27 | `Faction`: Eliminate allocations caused by closures and delegates | P4 AI/world | port | Faction allocations (aggro hot path) |
| 200 | `ef07f13f9` | 2026-06-27 | `Faction`: Fix GamePlayer leak caused by late client state check on save | P4 AI/world | port | GamePlayer leak in Faction save |
| 201 | `d89bdf29d` | 2026-06-27 | `Faction`: Fix race conditions Changed AggroLevel to a record struct. | P4 AI/world | port | Faction race conditions |
| 202 | `80e59444c` | 2026-06-27 | `Faction.Standing`: Change to Pascal case | P4 AI/world | skip-not-relevant | rename only |
| 203 | `10ef26a6b` | 2026-06-27 | `NpcMovementComponent`: Fix UpdatePosition's logic The code attempted to clamp the X, Y, and Z ax... | P4 AI/world | port |  |
| 204 | `80d638f50` | 2026-06-27 | `NpcMovementComponent`: Improve WalkToInternal's estimated arrival time | P4 AI/world | port | take with its partial revert 85f723b40 |
| 205 | `44ae62e96` | 2026-06-27 | `NpcMovementComponent`: Implement anti exploit mechanic * This isn't live like at all, but with t... | P4 AI/world | hold-for-owner | NPC anti-exploit: unreachable mobs reset; upstream says not live-like; may fire on bot pulls |
| 206 | `a0f6fa4df` | 2026-06-28 | Fix various H2H mechanical issues * Fixed H2H not benefiting from the offhand's "haste" effect. I... | P3 combat | port | harvest B7 |
| 207 | `e9fdfdd2a` | 2026-06-28 | Add missing H2H triple and quad hit messages in combat log | P3 combat | port | harvest B7 |
| 208 | `565a2d9e9` | 2026-06-28 | `WeaponAction.Execute`: Remove GamePlayer.Stealth(false) call Dead code. Handled already by MakeM... | P3 combat | skip-not-relevant | dead code removal |
| 209 | `ce965da78` | 2026-06-28 | `AttackComponent`: Update interaction between Dualist's Reflexex and H2H According to a few sourc... | P3 combat | port | harvest B7 |
| 210 | `307015f01` | 2026-06-28 | `EffectService`: Ensure effects removed from EffectListComponent don't tick * Fixes effects being... | P2 effects | port | harvest A3 |
| 211 | `9add26a8f` | 2026-06-28 | Fix H2H multihit to no longer rely on weapon type Supersedes a0f6fa4df8a0301d28040576c0a8a75a038b... | P3 combat | port | harvest B7 |
| 212 | `ec9cfe94d` | 2026-06-29 | `FixNpcSpawnCommandHandler`: Ignore but tag scripted NPCs | OTHER | port | X2 |
| 213 | `9ff5d3c7e` | 2026-06-29 | `FixNpcSpawnCommandHandler`: Use closest point between up and down snap distances if two points a... | OTHER | port | X2 |
| 214 | `d3b07a51d` | 2026-06-29 | `Pathfinder`: Use pooled List<GameDoorBase> | P4 AI/world | skip-not-relevant | reverted upstream (e9b74be08) |
| 215 | `9f3d5c6da` | 2026-06-29 | Simplify return to spawn logic * Fixed StandardMobState_RETURN_TO_SPAWN only calling GameNPC.Retu... | P4 AI/world | port | harvest A9 (partly in) |
| 216 | `fddf0475b` | 2026-06-29 | `NpcMovementComponent`: Only start anti exploit timer if the NPC has aggro or is in combat Preven... | P4 AI/world | hold-for-owner | follow-up of the held anti-exploit 44ae62e96 |
| 217 | `a1ca63f51` | 2026-06-29 | `SpellHandler.CheckBeginCast`: Fix damage immunity ability blocking non harmful spells | P2 effects | port |  |
| 218 | `3f0f163a3` | 2026-06-29 | `NewDoorCommandHandler`: Update to handle keep doors Not every subcommand may make sense for keep... | OTHER | port | X2 (GM /door for keep doors) |
| 219 | `14cbf9299` | 2026-06-30 | Add framework for triggers to paths / patrols * Added TriggerName to PathPoint and DbPathPoint. U... | P4 AI/world | port | path triggers framework (DB column); prereq for path work |
| 220 | `edb0f6119` | 2026-06-30 | `path`: Import BT changes for downstream merge * Doesn't include BT's RelicSquadGuardBrain logic.... | OTHER | port | X2 (/path GM command) |
| 221 | `f7a04dac3` | 2026-06-30 | `ABrain.OnPathPointReached`: Add return value So that NpcMovementComponent can react. Amends 14cb... | P4 AI/world | port |  |
| 222 | `ea787d744` | 2026-06-30 | Add /path edit Supersedes /path speed | OTHER | port | X2 (/path edit) |
| 223 | `f587ed9e3` | 2026-06-30 | Improve player position update accuracy We were still comparing truncated values, causing very ti... | P1 core | port | player position accuracy (movement packets) |
| 224 | `0303b890a` | 2026-06-30 | Allow PathPoint with EPathType.Path_Reverse to support multiple NPCs * Added new Path class, now... | P4 AI/world | port | reverse patrol paths for several NPCs |
| 225 | `c955cc64c` | 2026-07-01 | `GameLoopTickPacer`: Fix game loop clock drifting from real time * The pacer measured each tick r... | P1 core | port | harvest A12 |
| 226 | `d9c92fa2c` | 2026-07-01 | `DirtyTricksECSEffect`: Tidy up formatting | P2 effects | skip-not-relevant | formatting only |
| 227 | `2b5be8f33` | 2026-07-03 | `NpcAttackAction`: Fix LoS timer sometimes stopping too early | P4 AI/world | port |  |
| 228 | `2c6865d95` | 2026-07-03 | `NpcAttackAction`: Remove redundant code | P4 AI/world | skip-not-relevant | cleanup only |
| 229 | `adf010938` | 2026-07-03 | `WeaponAction`: Fix NPCs using their shield as a left hand weapon, again | P3 combat | port |  |
| 230 | `862405034` | 2026-07-04 | `NecromancerPetBrain`: Remove ThinkInterval override Will fall back to PET_THINK_INTERVAL (500 =>... | P2 effects | port |  |
| 231 | `21737045a` | 2026-07-04 | `StandardMobBrain`: Implement aggro check throlling and round-robin selection Added AggroCandidat... | P4 AI/world | skip-not-relevant | aggro round-robin engine refactor (harvest D); 23 boss files and bot threat code |
| 232 | `2888688a6` | 2026-07-04 | Remove debug message | P4 AI/world | skip-not-relevant | follow-up of skipped 21737045a |
| 233 | `8a5523a52` | 2026-07-04 | `AggroCandidateLoop`: Minor clean up | P4 AI/world | skip-not-relevant | follow-up of skipped 21737045a |
| 234 | `85f723b40` | 2026-07-04 | `NpcMovementComponent`: Partially revert 80d638f508a8baf6d47d3fbaff082f8f2b091f1e The change to d... | P4 AI/world | port | partial revert of 80d638f50 |
| 235 | `f22ebbe1c` | 2026-07-04 | `CharmSpellHandler`: Fix charm conflict when the current charm expires but the song isn't stopped... | P2 effects | port | harvest A5 |
| 236 | `23a41f784` | 2026-07-04 | `Pathfinder`: Fix distance calculations to use non-truncated coordinates This caused inconsistenc... | P4 AI/world | port |  |
| 237 | `b3b9a24f7` | 2026-07-05 | Fix dragons return to spawn logic * Ensured their flying path ends at their spawn point, preventi... | P4 AI/world | already have | in our tree (dragons return to spawn) |
| 238 | `e9b74be08` | 2026-07-05 | Revert "`Pathfinder`: Use pooled List<GameDoorBase>" | P4 AI/world | skip-not-relevant | revert; net zero with d3b07a51d |
| 239 | `21443ebd6` | 2026-07-06 | `Zone`: Add IsPointInZone | P4 AI/world | port | harvest A8 |
| 240 | `799b4f507` | 2026-07-06 | `Pathfinder`: Implement cross-zone pathfinding * Fixes NPCs being more likely to get stuck the fu... | P4 AI/world | port | harvest A8 |
| 241 | `ff0b76e76` | 2026-07-06 | `AbstractServerRules`: Reduce allocations caused by AwardPlayerOnNpcKill Added NpcKillRewardProce... | P3 combat | port | prereq for 6ac049c6e (NpcKillRewardProcessor) |
| 242 | `6ac049c6e` | 2026-07-07 | `NpcKillRewardProcessor`: Fix a grouped player leveling up sometimes causing other players gainin... | P3 combat | port | harvest A1 |
| 243 | `15e7e3196` | 2026-07-07 | `Pathfinder`: Allow NPCs to keep doors open GameDoor.Open will simply extend the timer. | P4 AI/world | port | harvest A7 |
| 244 | `31028d41b` | 2026-07-07 | Fix Morty | P4 AI/world | port | named mob Morty |
| 245 | `4b4efda29` | 2026-07-03 | `RandomDeck`: Redesign algorithm for stronger streak avoidance Less random, one bucket receives e... | P3 combat | port | RandomDeck streak avoidance |
| 246 | `1b55e22fa` | 2026-07-03 | `WeaponAction`: Add functionality to track current swing count Will be later used to fix an issue... | P3 combat | port | prereq |
| 247 | `a2924cc76` | 2026-07-08 | `AttackAction`: Add functionality to track current style chain stage Will be later used to fix an... | P3 combat | port | prereq |
| 248 | `dadfcbccf` | 2026-07-03 | `AttackComponent`: Minor clean up | P3 combat | skip-not-relevant | cleanup only |
| 249 | `ea6ea372f` | 2026-07-07 | `RandomDeckEvent`: Split resists from misses | P3 combat | port | RandomDeck split |
| 250 | `21faf916e` | 2026-07-07 | `RandomDeckEvent`: Rename DamageVariance to Variance Also used by heals. | P3 combat | port | RandomDeck rename (take with the series) |
| 251 | `87ff8bafe` | 2026-07-07 | Fix /targetstats consuming RandomDeck card | OTHER | port | X2 (/targetstats) |
| 252 | `70e8459f4` | 2026-07-08 | Rename eDualWieldMechanic to DualWieldMechanic | P3 combat | skip-not-relevant | rename only |
| 253 | `bcf574906` | 2026-07-08 | Introduce IRandomProvider and RandomContext | P3 combat | port | RNG series (IRandomProvider); take as one block |
| 254 | `82c72a447` | 2026-07-08 | `RandomEvent`: Split variance, critical chance, critical variance into physical and magic | P3 combat | port | RNG series |
| 255 | `c02e259e5` | 2026-07-08 | `RandomProvider`: Add support for sequence indexes A sequence index allows the use of more than o... | P3 combat | port | RNG series |
| 256 | `5d516d1cd` | 2026-07-08 | `AttackComponent`: Enable per style chain stage decks Follow-up to cb84bf17271452d78aa8755e186729... | P3 combat | port | RNG series |
| 257 | `161f7d9dd` | 2026-07-08 | `AttackComponent`: Make offhand and secondary melee swings use true RNG Follow-up to 4cd2c5370709... | P3 combat | port | RNG series |
| 258 | `43b1274a0` | 2026-07-08 | `AttackComponent`: Refacator LivingMakeAttack to check for pre conditions earlier Fixes invalid a... | P3 combat | port | LivingMakeAttack precondition fixes |
| 259 | `f8af27762` | 2026-07-08 | `WeaponAction`: Add DetermineDualWieldMechanic Fixes /targetstats no longer reporting dual wield... | P3 combat | port |  |
| 260 | `f589c15dc` | 2026-07-08 | `AttackComponent`: Fix parry and evade consuming a roll even when chances are null | P3 combat | port |  |
| 261 | `8a16922aa` | 2026-07-08 | `RandomProvider`: Minor clean up | P3 combat | skip-not-relevant | cleanup only |
| 262 | `7391dfd16` | 2026-07-08 | `CharmSpellHandler`: Remove superfluous RNG check CalculateSpellResistChance is 0 here. | P2 effects | port |  |
| 263 | `f6d054b80` | 2026-07-08 | Amend 8a16922aaf1d5957be882bb5f34b3b91eed3087c | P3 combat | skip-not-relevant | amends a cleanup |
| 264 | `f45f95145` | 2026-07-09 | `RealmAbility`: Fix incorrect cast message | P2 effects | port |  |
| 265 | `970282fba` | 2026-07-11 | `MaxSpeedCalculator`: Remove speed increase inheritance from pet owner to pet Let the pet use its... | P2 effects | hold-for-owner | pets stop inheriting owner speed buffs; bot pets may lag sprinting bots |
| 266 | `8ce0d9920` | 2026-07-11 | `ECSDebug`: Fix /diag notify race condition | P1 core | port | /diag notify race |
| 267 | `af451e5c9` | 2026-07-11 | `ECSDebug`: Change _gameEventMgrNotifyTimes to SortedDictionary | P1 core | port |  |
| 268 | `f3943156a` | 2026-07-11 | `Group`: Implement GroupMemberPositionUpdateTimer Updates group member positions on the minimap a... | OTHER | port | X3 (group minimap, harvest B13) |
| 269 | `1a97fbde9` | 2026-07-11 | `PacketLib174`: Ensure WriteGroupMemberMapUpdate doesn't send a player its own position | OTHER | port | X3 |
| 270 | `96412d49d` | 2026-07-11 | `Group`: Change calls to SendGroupMembersUpdate to no longer update minimap Since this is now han... | OTHER | port | X3 |
| 271 | `10d3897f0` | 2026-07-11 | `Group`: Fix buff icons not updating on member removal | OTHER | port | X3 |
| 272 | `6af0e9619` | 2026-07-11 | `Group, IPacketLib`: Allow SendGroupMembersUpdate to take a list of entities to update * Removed... | OTHER | port | X3 |
| 273 | `a9e0c3af2` | 2026-07-11 | `IPacketLib`: Fix group windows showing players as dead during region transition GameLiving.IsAli... | OTHER | port | X3 |
| 274 | `5725446af` | 2026-07-11 | `IPacketLib`: Fix WriteGroupMemberMapUpdate not handling players in a different region correctly... | OTHER | port | X3 |
| 275 | `a65afbb8a` | 2026-07-12 | `IPacketLib`: Allow group member position updates without sending other data This packet code use... | OTHER | port | X3 |
| 276 | `e3fe8dea1` | 2026-07-12 | `IPacketLib`: Add SendGroupMembersIconsUpdate Followup to previous commit. Updated SendGroupMembe... | OTHER | port | X3 |
| 277 | `f23e37a50` | 2026-07-12 | Cleanup EOL character inconsistency caused by 22197f1f6d40f16c6f53fbb0498b441b72cbf6ef | OTHER | skip-not-relevant | EOL cleanup |
| 278 | `7b7d11f2a` | 2026-07-12 | `GroupMemberPositionUpdateTimer`: Increase interval to 2500ms | OTHER | port | X3 |
| 279 | `acb7a49cf` | 2026-07-12 | `WeaponAction`: Remove superflous in view check in Prevent Flight handling If we're there, we kno... | P3 combat | skip-not-relevant | cleanup only |
| 280 | `763dfd1dc` | 2026-07-12 | Batch player resource status packets * Cached health, mana, endurance, concentration percents on... | P1 core | port | batches resource status packets; big win with many bots |
| 281 | `0b614d653` | 2026-07-12 | `IPacketLib`: Fix console spam caused by non released packet | P1 core | port |  |
| 282 | `cd2308ada` | 2026-07-12 | `StandardMobBrain`: Fix player aggro los checks starving npc aggro los checks * Fixed by introduc... | P4 AI/world | port | player LoS checks starving NPC LoS checks |
| 283 | `b6317804f` | 2026-07-12 | `GamePlayer`: Improve character sheet effective AF calculation to better match Live Weights were... | OTHER | port | X3 (character sheet AF) |
| 284 | `cbe604ce1` | 2026-07-13 | `SpellHandler`: Disable LoS/range check at end of cast for NPC (Live compliance) Effectively allo... | P4 AI/world | port | harvest B4; exempt GameBots |
| 285 | `678b8f16f` | 2026-07-14 | Allow immobile / guard caster NPCs to change target on LoS check fail against their current one M... | P4 AI/world | port | harvest B4 |
| 286 | `06981d998` | 2026-07-14 | Remove CHECK_LOS_BEFORE_NPC_RANGED_ATTACK I doubt this will ever be used. | P4 AI/world | port |  |
| 287 | `bf85d0c11` | 2026-07-14 | `NpcAttackAction`: Make ranged NPCs finish their current bow draw before considering changing tar... | P4 AI/world | port | harvest B4 |
| 288 | `096ad8e23` | 2026-07-14 | Fix logic allowing NPCs to attack other targets in range when moving causing rare targetting issu... | P4 AI/world | port |  |
| 289 | `978b991fc` | 2026-07-15 | `NpcAttackAction`: Disable LoS/range check at bow draw end (Live compliance) Effectively allows N... | P4 AI/world | port | harvest B4; exempt GameBots |
| 290 | `e6582785c` | 2026-07-14 | `GuildMgr`: Fix guild emblem not being set on already existing guild houses if previous emblem was 0 | OTHER | already have | in our tree (GuildMgr emblem update) |
| 291 | `fed1f28ec` | 2026-07-15 | `GuildMgr`: Force guild houses to receive guild emblem at server startup. | OTHER | port | X1 |
| 292 | `22c7e9de8` | 2026-07-15 | Add frequency and duration (if any) to most spell descriptions. * Fixes non-bleed style procs not... | OTHER | port | X3 (spell description frequency and duration) |
| 293 | `61d3ffaaf` | 2026-07-15 | Fix incorrect max health percent caching caused by GameLiving.MaxHealth overrides | P1 core | port | max health percent cache |
| 294 | `2e791235f` | 2026-07-15 | Change login invulnerability timer to no longer nullify fall damage | P1 core | port | harvest B14 |
| 295 | `9a665b536` | 2026-07-15 | Fix NPCs to no longer respect login invulnerability timer | P1 core | port | harvest B14 |
| 296 | `d73447af2` | 2026-07-15 | Allow players with login invulnerability timer to attack NPCs | P1 core | port | harvest B14 |
| 297 | `d9beeb627` | 2026-07-15 | Change login invulnerability timer messages to specify PvP | P1 core | port |  |
| 298 | `ca9effdf6` | 2026-07-15 | Adjust how quickness contributes to attack speed to match more common formulas | P3 combat | port | harvest B8 |
| 299 | `33774a6d8` | 2026-07-15 | `StandardMobBrain.AddAggroListTo`: Only add entities not already present AddAggroListTo seems to... | P4 AI/world | port |  |
| 300 | `fe7decc56` | 2026-07-15 | Fix Cronker Now looks for "giant frog" in proximity, instead of relying on PackageID (no mob in t... | P4 AI/world | port | named mob Cronker |
| 301 | `bb789b89d` | 2026-07-15 | Fix Water Spider Gleek | P4 AI/world | port | named mob Water Spider Gleek |
| 302 | `152b87aec` | 2026-07-15 | `Pathfinder`: Fix NPCs stuttering / spinning during zone transition | P4 AI/world | port |  |
| 303 | `d78e0da99` | 2026-07-15 | `GameInventoryItem`: Remove m_owner Let's not store GamePlayer instances in items for no reason. | P1 core | port | item no longer holds its GamePlayer (leak) |
| 304 | `22c0bff74` | 2026-07-16 | `VaultItemCache`: Fix items not being fully typed | OTHER | skip-not-relevant | VaultItemCache does not exist in our tree |
| 305 | `25f281d13` | 2026-07-16 | `DetourInterface.FindClosestPointInBox`: Optimize bounds calculation | P4 AI/world | skip-not-relevant | native Detour change for the upstream Detour.Managed layout |
| 306 | `8476af40f` | 2026-07-17 | Add Detour.Managed: GameServer agnostic wrapper for Detour Refactored server pathfinding to use t... | P4 AI/world | skip-not-relevant | Detour.Managed wrapper refactor; our Pathfinder diverged (keep doors, bot logic) |
| 307 | `9071ad689` | 2026-07-17 | `Pathfinder`: Defer replots when NPC is jumping from one mesh to another Fixes NPCs becoming stuc... | P4 AI/world | port | harvest A7 |
| 308 | `ec6ed60fc` | 2026-07-18 | Update NuGet dependencies | OTHER | skip-not-relevant | NuGet bump |
| 309 | `90855fd37` | 2026-07-18 | `NpcAttackAction`: Fix non-pet NPCs with bows being unable to attack other non-pet NPCs Moved Npc... | P4 AI/world | port |  |
| 310 | `2da06dcb8` | 2026-07-18 | `NpcAttackAction`: Refresh LoS checker on timeout. A player being selected as the LoS checker for... | P4 AI/world | port |  |
| 311 | `73beca262` | 2026-07-18 | `ABrain.GetLosChecker`: Don't return players unable to reply to LoS check requests | P4 AI/world | port |  |
| 312 | `40a406f60` | 2026-07-18 | `TimeDependentSpawnBrain`: Make ShouldBeVisible abstract | P4 AI/world | port |  |
| 313 | `c59f4ba94` | 2026-07-18 | `NpcMovementComponent`: Fix potential MinFollowDistance, MaxFollowDistance overflow | P4 AI/world | port |  |
| 314 | `4376f3c9b` | 2026-07-18 | `ScoutMobState_AGGRO`: Fix scouts becoming stuck if unable to move | P4 AI/world | port | harvest A9 |
| 315 | `1dfa5fed6` | 2026-07-18 | `ScoutMobState_AGGRO`: Move non-static local functions out | P4 AI/world | skip-not-relevant | cleanup only |
| 316 | `e9cfdbf01` | 2026-07-19 | `GameNPC`: Move ranged attack interrupt restriction from GameNPC to Lord The MaxSpeedBase check w... | P4 AI/world | port |  |
| 317 | `02d1bf44e` | 2026-07-19 | Fix NPCs not turning to their spawn heading if already near their spawn point Changed GameNPC.IsN... | P4 AI/world | port |  |
| 318 | `d390e54ed` | 2026-07-19 | `NpcCastingComponent`: Fix _spellsWaitingForLosCheck's lists not being cleared Regression caused... | P4 AI/world | port |  |
| 319 | `d64de581b` | 2026-02-25 | `ISpellHandler`: Remove unused delegates | P2 effects | skip-not-relevant | removes unused delegates |
| 320 | `158f439ce` | 2026-02-25 | `SpellHandler`: Replace outdated usage of `Region.Time` | P2 effects | port |  |
| 321 | `549f543ad` | 2026-02-25 | `SpellHandler`: Refactor `SendCastAnimation` to no longer be responsible for setting `_calculated... | P2 effects | port |  |
| 322 | `7b1fa00c7` | 2026-02-25 | `SpellHandler`: Remove unused `INTERRUPT_TIMEOUT_PROPERTY` Deleted Archery.CasterIsAttacked overl... | P2 effects | hold-for-owner | harvest B1 (live interrupt series; removes INTERRUPT_TIMEOUT and Archery.CasterIsAttacked) |
| 323 | `8c0857374` | 2026-02-25 | `SpellHandler`: Fix truncation of displayed interrupt duration | P2 effects | port | displayed interrupt duration only |
| 324 | `c2c8dd498` | 2026-02-25 | `AttackComponent`: Fix self interrupt on melee attack using fixed duration instead of swing inter... | P3 combat | port |  |
| 325 | `05a3e5f41` | 2026-02-25 | `AttackComponent.LivingMakeAttack`: Minor clean up Use owner instead of AttackData.Attacker | P3 combat | skip-not-relevant | cleanup only |
| 326 | `5fb4f6a3e` | 2026-07-20 | `GameLiving`: Rename interrupt related properties to prepare for soft / hard interrupt split | P2 effects | hold-for-owner | harvest B1 prep |
| 327 | `d17195fd7` | 2026-07-20 | `GameLiving`: Change LastInterrupter type to GameLiving | P2 effects | hold-for-owner | harvest B1 prep |
| 328 | `21eda0f90` | 2026-07-20 | `SpellHandler`: Implement live-like spell interrupt * Added two server properties: * * HARD_INTER... | P2 effects | hold-for-owner | harvest B1 (live-like spell interrupt) |
| 329 | `79bba259b` | 2026-07-20 | `AttackAction`: Implement live-like ranged attack interrupt Live mechanics (ignoring very minor d... | P3 combat | hold-for-owner | harvest B1 (live-like ranged interrupt) |
| 330 | `da099207f` | 2026-07-21 | `GameLiving.StartInterruptTimer`: Refactor to accomodate for new ranged attack interrupt mechanic... | P2 effects | hold-for-owner | harvest B1 |
| 331 | `c0f4c72bb` | 2026-07-21 | `PlayerInitRequestHandler`: Exclude GMs+ from CheckNearbyKeepAndMoveIfUnsafe and CheckBGLevelCapF... | OTHER | port | X2 (GMs exempt from keep and BG relocation at login) |
| 332 | `5e328fed6` | 2026-07-17 | Change Albion caster guard staff model | P4 AI/world | port | Albion caster guard staff model |
| 333 | `2ffc5c0ff` | 2026-07-21 | `GameLiving`: Change interrupt chance per level difference from 3% to 5% Primarily a PvE thing si... | P2 effects | hold-for-owner | interrupt chance per level difference 3% to 5%: grey and green mobs interrupt less (unsourced) |
| 334 | `46d816439` | 2026-07-21 | `SpellHandler`: Fix focus spell interrupts Regression caused by 21eda0f903a5e3d46d6d6a5a622e6ffd7... | P2 effects | hold-for-owner | harvest B1 regression fix (focus spells) |
| 335 | `aca109fdc` | 2026-07-21 | Fix messages from expiring effects using incorrect chat category | P2 effects | port |  |
| 336 | `63e1162b8` | 2026-07-21 | Fix doors using IsObjectInFront with default always true range | P4 AI/world | port |  |
| 337 | `13d505afb` | 2026-07-21 | `ConLevels`: Implement server-side calculation of item con levels Items and characters don't use... | P3 combat | skip-not-relevant | item con levels; unused upstream |
| 338 | `3849d62d7` | 2026-07-21 | `ConLevelGrid.cs`: Replace dictionaries for safe iteration | P3 combat | skip-not-relevant | follow-up of skipped 13d505afb |
| 339 | `ac0455f41` | 2026-07-22 | `GamePlayer.CraftingSpeed`: Replace array allocation | P3 combat | port |  |
| 340 | `417a73203` | 2026-07-22 | Amend previos commit | P3 combat | port | amends ac0455f41 |
| 341 | `441b8333d` | 2026-07-22 | `AttackAction`: Fix Critical Shot, Longshot not always stopping Regression caused by 79bba259b4a9... | P3 combat | hold-for-owner | harvest B1 regression fix (Critical Shot, Longshot) |
| 342 | `10add5032` | 2026-07-22 | `GameNPC`: Remove unused AttackCriticalChance | P3 combat | skip-not-relevant | removes unused property |
| 343 | `821616ba5` | 2026-07-22 | `DetourNativeLibrary`:  Allow non-'lib' prefixed name on Linux | P4 AI/world | skip-not-relevant | Linux Detour library naming; we ship Windows |
| 344 | `9f22de62e` | 2026-07-22 | `DetourNativeLibrary`: Simplify IsDetourLibraryName | P4 AI/world | skip-not-relevant | follow-up of skipped 821616ba5 |
| 345 | `409c19b3d` | 2026-07-22 | `GameHouse`: Fix emblems not being updated on consignment merchants It required a server reload. | OTHER | port | X1 |
| 346 | `72245d297` | 2026-07-22 | `Guild`: Fix RemoveOnlineMember removing emblems from personal houses Only meant to do so if the... | OTHER | port | X1 |
| 347 | `f01e9fc33` | 2026-07-22 | Make NPCs taking ladders "fly" | P4 AI/world | port | harvest A7 |
| 348 | `aad790ecb` | 2026-07-22 | `StandardMobState`: Fix idle NPCs being forcibly turned to their spawn heading on every think tic... | P4 AI/world | port | harvest A9 |
| 349 | `e1be6d9e5` | 2026-07-23 | `WeaponAction`: Fix Reflex Attack Regression caused by 43b1274a0db869bf5064559f71f4c933687f9a47 | P3 combat | port | regression fix for 43b1274a0 |
| 350 | `7eadab9a2` | 2026-07-23 | `TimeDependentSpawnNpc`: Refactor to avoid setting brain in AddToWorld Potential cause of infinit... | P4 AI/world | port |  |
| 351 | `1838cfeeb` | 2026-07-23 | `Abrain.Stop`: Refactor to avoid call to MoveTo on server start GameNPC.LoadFromDatabase currentl... | P4 AI/world | port |  |
| 352 | `ddb3631ae` | 2026-07-23 | `NpcCastingComponent`: Make caster guards keep their target in their aggro list on LoS loss as lo... | P4 AI/world | port | harvest B4 |
| 353 | `b37085ea8` | 2026-07-23 | `StandardMobState_AGGRO.Exit`: Clear aggro list Removed supposedly redundant calls in StandardMob... | P4 AI/world | port |  |
| 354 | `f10712cf2` | 2026-07-23 | `NpcMovementComponent`: Force teleport NPCs returning to their spawn if no path found | P4 AI/world | skip-not-relevant | reverted upstream (ad17f2ba9) |
| 355 | `50398f3df` | 2026-07-23 | `ABrain.Stop`: Prefer MoveInRegion over MoveTo | P4 AI/world | port | keep our GameBot MoveTo guard in ABrain.Stop |
| 356 | `5c2fdc7bd` | 2026-07-23 | Fix Guild.DummyGuild | OTHER | port | X1 (Guild.DummyGuild) |
| 357 | `6f96f701d` | 2026-07-25 | `GameServiceBase`: Refactor to eliminate state boxing | P1 core | port |  |
| 358 | `8084de088` | 2026-07-25 | `Pathfinder`: Refactor IsStraightLineHeightSafe to use intrinsics | P4 AI/world | skip-not-relevant | code removed later by fe6d46dde |
| 359 | `32b5c0f0e` | 2026-07-25 | `PlayerMovementMonitor`: Improve accuracy by using non-rounded positions | P1 core | port |  |
| 360 | `84ffe32bc` | 2026-07-25 | `TimeCommandHandler`: Minor clean up Thanks to Blackthorn I always do /plvl 10 now, and admin != 10. | OTHER | skip-not-relevant | cleanup only |
| 361 | `bab3c45ed` | 2026-07-25 | `NpcMovementComponent`: Allow NPCs to project their movemnt speed onto the X/Y plane and ignore Z... | P4 AI/world | port |  |
| 362 | `42bf098af` | 2026-07-25 | Fix spell tooltip levels | OTHER | port | X3 (spell tooltip levels) |
| 363 | `74587b664` | 2026-07-26 | `StuckCommandHandler`: Add message | OTHER | port | X2 (/stuck message) |
| 364 | `f5f3e4e43` | 2026-07-26 | Clean up 42bf098af65cbbe4d9000add28a9c2e01669c4ac * Removed now redundant DelveSong overload * CR... | OTHER | port | X3 |
| 365 | `d774cacf9` | 2026-07-26 | `NecromancerPetBrain`: Make instant spells require LoS I don't know why it was disabled. Probably... | P2 effects | port | harvest A16 |
| 366 | `72bf0389b` | 2026-07-26 | `CastingComponent`: Fix minor LoS related issues * Added QueuedCastLosCheckListener in NpcCasting... | P2 effects | port |  |
| 367 | `f815b1253` | 2026-07-26 | `SpellHandler`: Change (fix?) effect duration reduction from AoE spells to only apply against pla... | P2 effects | port |  |
| 368 | `222366314` | 2026-07-27 | `TimeDependentSpawnNpc`: Set IsVisible in RespawnTimerCallback Fixes respawning NPCs flickering w... | P4 AI/world | port |  |
| 369 | `0837f95e1` | 2026-07-27 | `ParthananFarm`: Rewrite Fixes: * Various concurrency issues (counters...) * NPC flags not being... | OTHER | port | X4 (Parthanan farm rewrite: concurrency and flags) |
| 370 | `fc612e75f` | 2026-07-27 | `DbLanguageSystem`: Minor clean up | OTHER | skip-not-relevant | cleanup only |
| 371 | `29a935897` | 2026-07-28 | `FixNpcSpawn`: Add 'visible' scope | OTHER | port | X2 |
| 372 | `b2770fa85` | 2026-07-28 | `FixNpcSpawn`: Increase default snapDown, snapUp | OTHER | port | X2 |
| 373 | `bf662c31d` | 2026-07-28 | `StandardMobBrain`: Hide OrderedAggroList | P4 AI/world | skip-not-relevant | encapsulation only; bot code reads the aggro list |
| 374 | `c0476c41b` | 2026-07-29 | `HostInitializator`: Fix timer Man, wtf is this. | P4 AI/world | already have | in our tree (Host timer) |
| 375 | `e2893f92e` | 2026-07-29 | Refactor aggro management with AggroTable and strategies | P4 AI/world | skip-not-relevant | AggroTable engine refactor (harvest D, raid-sources doc: don't cherry-pick) |
| 376 | `8f90af69e` | 2026-07-29 | `AggroTable`: Don't clear _tempTable in Clear Removed seemingly no longer needed confusion + amne... | P4 AI/world | skip-not-relevant | needs AggroTable |
| 377 | `481a197e4` | 2026-07-29 | `SubZone.AddObject`: Reduce allocations | P1 core | port |  |
| 378 | `5684395ba` | 2026-07-29 | `GameServiceBase`: Replace ConcurrentQueue with swap lists * Improves performance from the game l... | P1 core | port |  |
| 379 | `be1e743ff` | 2026-07-29 | `GameServiceBase`: Minor clean up | P1 core | skip-not-relevant | cleanup only |
| 380 | `a8c4f88a0` | 2026-07-29 | `invalidnames`: Relax ban on 'Sapph' | OTHER | port | X2 (invalid-name list; may block bot names) |
| 381 | `40fa0f68f` | 2026-07-30 | `StandardMobBrain.AddToAggroList`: Ignore dead entities There seems to be an edge case where a NP... | P4 AI/world | port | harvest A9 |
| 382 | `098878c01` | 2026-07-30 | `HealthRegenerationAmountCalculator`: Reduce health regen of out of combat NPC with an empty aggr... | P4 AI/world | skip-nerf-or-restriction | lowers out-of-combat NPC health regen 12.5% to 10% (harvest B9 group) |
| 383 | `3b3daa3d7` | 2026-07-30 | `DataTableHandler`: Initialize FieldElementBindings once | P1 core | port |  |
| 384 | `b141b28fd` | 2026-07-30 | `DataTableHandler`: Cache element bindings that can be part of an UPDATE statement Used by DataOb... | P1 core | port |  |
| 385 | `19123fffd` | 2026-07-30 | `DataObject`: Fix LastTimeRowUpdated causing unnecessary saves When a player inventory is loaded,... | P1 core | port | harvest A2 |
| 386 | `4b3648b18` | 2026-07-30 | `DataTableHandler`: Fiix ElementBindings initialization Caused by the last few commits. | P1 core | port | fix for b141b28fd |
| 387 | `90bf244fe` | 2026-07-30 | `GamePlayerInventory`: Remove misleading comment This change was mistakently included with 3b3daa... | P1 core | skip-not-relevant | comment only |
| 388 | `5a4659166` | 2026-07-30 | Avoid console EOF CPU spin | P1 core | port | console EOF CPU spin (server run without a console) |
| 389 | `860d88cbb` | 2026-07-30 | Add missing import | P1 core | port | amends 5a4659166 |
| 390 | `48d60bf14` | 2026-07-31 | `AttackComponent`: Fix NPCs attempting to face their target when pathing Regression caused by bf8... | P4 AI/world | port | regression fix for bf85d0c11 |
| 391 | `5302c79e9` | 2026-08-01 | `Salvage`: Add timer to siege weapon salvage, fix material loss and null-ref bugs | P3 combat | already have | in our tree (Salvage) |
| 392 | `135d7d741` | 2026-08-01 | `BoltSpellHandler`: Fix Guard check | P2 effects | port |  |
| 393 | `27038c662` | 2026-08-02 | `AggroTable`: Add AddResult return value Moved TargetCandidate struct in AggroTable class. | P4 AI/world | skip-not-relevant | needs AggroTable |
| 394 | `696236246` | 2026-08-02 | `StandardMobBrain.AddToAggroList`: Fix concurrent calls to FSM.SetCurrentState Behavioral change:... | P4 AI/world | port |  |
| 395 | `c3e47983a` | 2026-08-02 | `AggroTable.UpdateAndGetHighestThreat`: Clear rented array | P4 AI/world | skip-not-relevant | needs AggroTable |
| 396 | `0537768ea` | 2026-08-02 | Clean up scripted mobs and extract shared encounter infrastructure | P4 AI/world | skip-not-relevant | 1,339-line boss-script refactor; we rewrote these scripts for raid AI, take per-boss fixes only |
| 397 | `08260434f` | 2026-08-03 | Consolidate HideableNpc and TimeDependentSpawnNpc * TimeDependentSpawnNpc now inherits HideableNp... | P4 AI/world | port |  |
| 398 | `3f6a58a24` | 2026-08-03 | `ScriptSpells.GetOrCreate`: Reduce allocations ScriptSpells was added by #84 | P4 AI/world | skip-not-relevant | ScriptSpells (upstream #84) not in our tree |
| 399 | `1fb3db68c` | 2026-08-03 | ScriptSpells: Minor clean up | P4 AI/world | skip-not-relevant | ScriptSpells not in our tree |
| 400 | `dd9659f8c` | 2026-08-03 | `ThreatStrategy`: Fix SelectTarget allowing to stay on the current target even if not in the aggr... | P4 AI/world | skip-not-relevant | needs AggroTable ThreatStrategy |
| 401 | `0d35a9894` | 2026-08-03 | `StandardMobBrain`: Fix max los check per tick constants being unused | P4 AI/world | port |  |
| 402 | `d53eceb0c` | 2026-08-03 | `StandardMobState_RETURN_TO_SPAWN`: Fix NPCs waiting to finish their current movement before init... | P4 AI/world | port | harvest A9 |
| 403 | `f1327630d` | 2026-08-04 | Simplify how pets resume their "walk state" It was done in AttackMostWanted when no target could... | P2 effects | port | pets resume walk state |
| 404 | `cadc874a4` | 2026-08-03 | Add path creation helper | P4 AI/world | already have | in our tree (MovementMgr path helper) |
| 405 | `b40a652c7` | 2026-08-03 | path cleanup eques | P4 AI/world | port | boss patrol path (Eques) |
| 406 | `39cded9c2` | 2026-08-03 | path cleanup njessi | P4 AI/world | port | boss patrol path (Njessi) |
| 407 | `c2a55c5b9` | 2026-08-03 | path cleanup crypt lord | P4 AI/world | already have | in our tree |
| 408 | `cadc7b8c0` | 2026-08-03 | path cleanup host | P4 AI/world | already have | in our tree |
| 409 | `9b86b7401` | 2026-08-03 | path cleanup skeletal sacristan | P4 AI/world | port | boss patrol path |
| 410 | `0fbdbcb1a` | 2026-08-03 | path cleanup spectral provisioner | P4 AI/world | port | boss patrol path |
| 411 | `abb420a99` | 2026-08-03 | path cleanup orey-eyed oghamist | P4 AI/world | port | boss patrol path |
| 412 | `f13f17667` | 2026-08-03 | path cleanup torst and hurika | P4 AI/world | port | boss patrol path |
| 413 | `dfad601d2` | 2026-08-03 | clean up Xaga and related adds | P4 AI/world | port | Xaga and adds |
| 414 | `0b581e3fb` | 2026-08-03 | move many scripted mob's Die() to ProcessDeath(), and add despawn safety | P4 AI/world | already have | in our tree |
| 415 | `12872751a` | 2026-08-03 | changed WalkTo to PathTo | P4 AI/world | already have | in our tree |
| 416 | `a9061baa6` | 2026-08-03 | delete dead walktos | P4 AI/world | port |  |
| 417 | `ac841e9f1` | 2026-08-03 | fix the weird patrol bosses | P4 AI/world | port | patrol bosses restart their loop |
| 418 | `ad17f2ba9` | 2026-08-05 | Revert f10712cf23fae7874fa67400680049751332fc2e Causes issue with released charmed NPCs if multip... | P4 AI/world | skip-not-relevant | revert; net zero |
| 419 | `08c896ac0` | 2026-08-05 | Fix GameLiving.LastAttacker memory leak GameLiving.LastAttacker was never cleared and kept refere... | P1 core | port | harvest A3 (LastAttacker leak); map bot uses first |
| 420 | `c707d057f` | 2026-08-06 | `AttackComponent`: Fix memory leak caused by GameObject references being held longer than necessa... | P1 core | port | harvest A3 |
| 421 | `5a6858f9f` | 2026-08-06 | `DataObject`: Fix memory leak caused by MemberwiseClone copying snapshot reference onto the next... | P1 core | already have | in our tree |
| 422 | `905ad857c` | 2026-08-06 | Fix issue where dragging an equipped weapon into your inventory on another weapon and then swappi... | P3 combat | port |  |
| 423 | `a17b43c6a` | 2026-08-07 | `GamePlayer`: Update Reset to call RespecRealm | P3 combat | port |  |
| 424 | `fb88fa1c8` | 2026-08-07 | `GamePlayer`: Remove RespectAmountDOL and related code | OTHER | skip-not-relevant | drops a DOL respec column from character saves; no gain, save-format risk |
| 425 | `8ec75a01b` | 2026-08-07 | `GamePlayer`: Update GainRealmPoints to handle RR drop on RP loss | P3 combat | port | realm rank drop on RP loss |
| 426 | `5372dff9c` | 2026-08-07 | `DbCoreCharacter`: Remove leftover m_respecAmountDOL | OTHER | skip-not-relevant | follow-up of skipped fb88fa1c8 |
| 427 | `fca5900d7` | 2026-08-04 | first pass | P4 AI/world | already have | in our tree |
| 428 | `6f8b6cf49` | 2026-08-07 | fix encounter gating stuff | P4 AI/world | port | encounter kill gates (EncounterGateAdd); not in our tree |
| 429 | `403d084dc` | 2026-08-07 | clean up more encounter add stuff | P4 AI/world | already have | in our tree |
| 430 | `66dbe4220` | 2026-08-07 | lot more variable cleanup | P4 AI/world | already have | in our tree |
| 431 | `0a3fbe3a6` | 2026-08-07 | `AttackAction, WeaponAction, RangeAttackComponent`: Refactor to hold attack target as GameLiving | P3 combat | port | prereq refactor |
| 432 | `dba5aa61a` | 2026-08-07 | `AttackAction.MakeAttack`: Refactor use pooled lists for multi hit styles Follow-up to previous c... | P3 combat | port |  |
| 433 | `f26e4bab7` | 2026-08-07 | `WeaponAction`: Remove unnecessary cast | P3 combat | skip-not-relevant | cleanup only |
| 434 | `ffd202c75` | 2026-08-07 | `NpcAttackAction.CheckLosTimer`: Refactor to hold target as GameLiving | P4 AI/world | port |  |
| 435 | `99c97fbd9` | 2026-08-08 | `GuardBrain`: Fix proximity aggro to ignore stealth Also mounted players. | P4 AI/world | port | harvest B5 (stronger guards); keep the bot route rule |
| 436 | `95e394d35` | 2026-08-08 | `DetailDisplayHandler`: Fix more issues with tooltips showing level 1 | OTHER | port | X3 |
| 437 | `424e471bd` | 2026-08-08 | `PacketLib168.SendObjectUpdate`: Replace local function | P1 core | skip-not-relevant | local function refactor only |
| 438 | `f2457c83c` | 2026-08-08 | `AbstractServerRules`: Reduce allocations caused by AwardPlayerOnPlayerKill Added PlayerKillRewar... | P3 combat | port |  |
| 439 | `009456e40` | 2026-08-08 | `NecromancerPetBrain.TetherTimer`: Replace local function | P2 effects | already have | in our tree |
| 440 | `e982a6666` | 2026-08-08 | `DayNightCycleTimer`: Replace local functions | P4 AI/world | skip-not-relevant | local function refactor only |
| 441 | `aaa787934` | 2026-08-08 | `Zone.Relocate`: Replace local function | P4 AI/world | skip-not-relevant | local function refactor only |
| 442 | `7d6aad147` | 2026-08-08 | `PacketProcessor.Send(Udp\|Tcp)AndResetContext`: Replace local functions | P1 core | skip-not-relevant | local function refactor only |
| 443 | `c928369c4` | 2026-08-09 | `EffectListComponent`: Snapshot IsBeingReplaced into PendingEffect at enqueue time; fixes charms... | P2 effects | port | harvest A5 |
| 444 | `7b38e3dbd` | 2026-08-09 | `EffectService`: Remove effects belonging to deleted owners from ServiceObjectStore | P1 core | port | harvest A3 |
| 445 | `87be7afe1` | 2026-08-09 | `GameObject`: Remove objects-in-radius caching mechanism It's the source of a memory leak, and I... | P1 core | port | leak; measure first, our bot code calls ClearObjectsInRadiusCache |
| 446 | `ea97d0189` | 2026-08-09 | `CastingComponnet.DuringCastLosCheckListener`: Refactor to eliminate SpellHandler retention | P1 core | port | SpellHandler retention leak |
| 447 | `fa6710b0d` | 2026-08-09 | `NpcCastingComponnet.QueuedCastLosCheckListener`: Refactor to eliminate SpellHandler retention | P1 core | port | SpellHandler retention leak |
| 448 | `040386541` | 2026-08-09 | Amend previous commit | P1 core | port | amends fa6710b0d |
| 449 | `ecaf945d0` | 2026-08-11 | Redesign defense penetration to break linearity with damage table Expected defense penetration ch... | P3 combat | hold-for-owner | harvest B9: NPC defense penetration redesign (mixed effect) |
| 450 | `dee9ee086` | 2026-08-11 | Rebalance NPC defense penetration An unintended consequence of ecaf945d0c786b355f2f5911eeb3272cf1... | P3 combat | skip-nerf-or-restriction | harvest B9: lowers NPC damage at levels 0-10 by 5-28% |
| 451 | `3b96d0f2b` | 2026-08-11 | `TurretBrain`: Fix incorrect aggro range for PBAoE turrets `TurretBrain`: Fix incorrect aggro ran... | P2 effects | port | harvest A17 |
| 452 | `c4b6244c3` | 2026-08-12 | `AdvancedCraftingSkill.CheckForTools`: Minor clean up | P3 combat | skip-not-relevant | cleanup only |
| 453 | `b62885a86` | 2026-08-12 | `AdvancedCraftingSkill.IsAllowedToCombine`: Fix bonus count check | P3 combat | already have | in our tree |
| 454 | `aa8b5232b` | 2026-08-12 | `AdvancedCraftingSkill.IsAllowedToCombine`: Fix all focus gems not being restricted to staves | P3 combat | already have | in our tree |
| 455 | `3423d9dc2` | 2026-08-12 | `AdvancedCraftingSkill`: Minor clean up | P3 combat | skip-not-relevant | cleanup only |
| 456 | `276df9bf0` | 2026-08-16 | `GameLoopThreadPoolMultiThreaded`: Replace array of MRES with semaphore I don't remember why I de... | P1 core | port |  |
| 457 | `336091350` | 2026-08-17 | `NpcAttackAction`, `NpcCastingComponent`: Change guard check to no longer also apply to immobile... | P4 AI/world | skip-not-relevant | reverted upstream (6b42b1976) |
| 458 | `6e709adae` | 2026-08-17 | `TurretFNFBrain`: Dumbify FnF turrets | P2 effects | port | harvest B11 (FnF turrets) |
| 459 | `6b42b1976` | 2026-08-17 | Revert 33609135039642b0a2327ae76c6ecf4a43202889 | P4 AI/world | skip-not-relevant | revert; net zero |
| 460 | `93310604a` | 2026-08-17 | `FnfTurretThreatStrategy`: Remove out of range entities | P2 effects | port | harvest B11; needs AggroTable, port the rule by hand |
| 461 | `d250310e4` | 2026-08-17 | `GameLiving.Delete`: Clear TempProperties | P1 core | port |  |
| 462 | `7384fed04` | 2026-08-18 | `HouseRentService`, `PeriodicQuestService`: Add missing exception handling in Action passed to Ex... | OTHER | port | X4 |
| 463 | `47b04abd4` | 2026-08-18 | `eSpellType`: Remove unused "Buff", "Summon" | P2 effects | skip-not-relevant | removes unused enum values |
| 464 | `cc0595ec6` | 2026-08-19 | Refactor AI defensive spell target selection and validate queued spell before promotion Centraliz... | P4 AI/world | port | NPC defensive spell target selection |
| 465 | `d9d2b6816` | 2026-08-19 | `EffectService`: Call ECSGameEffect.End() if owner is deleted Fixes some pulsing effects lasting... | P1 core | port | harvest A3 |
| 466 | `b8f637e2e` | 2026-08-19 | `Faction.Standing`: Fix spelling error | P4 AI/world | skip-not-relevant | spelling only |
| 467 | `86dd0faff` | 2026-08-19 | `Faction.SaveAggroLevels`: Check player state instead of client state | P4 AI/world | port |  |
| 468 | `a89bfef81` | 2026-08-23 | `FnfTurretThreatStrategy`: Prioritize very close entities | P2 effects | port | harvest B11; needs AggroTable, port by hand |
| 469 | `771b54919` | 2026-08-23 | `NecromancerPetBrain`: Fix spell queueing not working for certain types of spell Caused by cc0595... | P2 effects | port | harvest A16 |
| 470 | `7a8ae3661` | 2026-08-23 | `DistractionAbilityHandler`: Increase duration, ignore NPCs attacking or casting, ignore pets, fi... | P2 effects | port | harvest B14 (Distraction) |
| 471 | `a2beee968` | 2026-08-23 | `NpcMovementComponent`: Fix TurnTo with duration not working correctly on attacking, casting, or... | P4 AI/world | port |  |
| 472 | `ecfeca7b5` | 2026-08-23 | `Region`: Undeprecate GetXInRadius methods, use pooled lists | P1 core | port |  |
| 473 | `e845e011f` | 2026-08-23 | `PlayerStatistics`: Round RPs per hour. | OTHER | port | X2 |
| 474 | `27ec0ed7a` | 2026-08-25 | `FnfTurretThreatStrategy`: Force enemy players into closest bucket | P2 effects | port | harvest B11 |
| 475 | `8973cb1fe` | 2026-08-29 | `EffectHelper`: Fix displayed weaponskill delayed update | P2 effects | already have | in our tree |
| 476 | `57dc0bd73` | 2026-08-29 | `PacketLib175.SendCharStatsUpdate`: Eliminate array allocations | P1 core | port |  |
| 477 | `b9a1095bd` | 2026-08-29 | `ControlledMobBrain.AttackMostWanted`: Call GameNPC.StopAttack when CalculateNextAttackTarget ret... | P2 effects | port |  |
| 478 | `cf8c9fe52` | 2026-08-29 | Amend previous commit | P2 effects | port | amends b9a1095bd |
| 479 | `b693fee89` | 2026-08-29 | `Pathfinder.TryGetClosestReachableNode`: Fix NPCs rarely getting stuck on ladders State corruptio... | P4 AI/world | port | harvest A7 |
| 480 | `c2ab4a2e6` | 2026-08-29 | `Pathfinder.UpdateOffMeshLinkState`: Fix NPCs committing too early | P4 AI/world | port | harvest A7 |
| 481 | `dc81f1276` | 2026-08-29 | `TargetStatsCommandHandler`: Fix defender / attacker being swapped | OTHER | port | X2 (/targetstats) |
| 482 | `eb06c09aa` | 2026-08-30 | `GamePlayer.CanDetect`: Remove always-visible group members This was added in 1.93. | P2 effects | hold-for-owner | harvest B6: grouped stealthers stop seeing each other (1.65 rule); human player plus stealther bots |
| 483 | `20bb3fee0` | 2026-08-31 | `GameNPC.MoveInRegion`: Remove forveMove parameter and InCombat check | P4 AI/world | port |  |
| 484 | `324780db2` | 2026-08-31 | Fix various issues with Speed of Sound Design: Speed of Sound force the movement speed multiplier... | P2 effects | port | harvest A15 |
| 485 | `da1990cea` | 2026-08-31 | `Legion.ThrowPlayer`: Remove superfluous HasAggro check | P4 AI/world | port | Legion throw (stronger) |
| 486 | `cc4febb79` | 2026-08-31 | `ControlledMobBrain.ResumeWalkState`: Ensure no movement is initiated if the pet is casting Remov... | P2 effects | port |  |
| 487 | `2302a9f8e` | 2026-08-31 | `NpcMovementComponent`: Fix NPC stutter when reaching intermediate PathPoint | P4 AI/world | port |  |
| 488 | `ff5449555` | 2026-09-01 | `EffectListComponent`: Prrevent speed buffs from coexisting Added Item_Effects to be handled the... | P2 effects | port | harvest A15 |
| 489 | `217c787fd` | 2026-09-01 | `PlayerEffectListComponent.SendPlayerUpdates`: Add GamePlayer.ObjectState check | P2 effects | port |  |
| 490 | `06cb67af3` | 2026-09-01 | `GamePlayer`: Fix displayed overall AF issues * Fixes RR5 not being taken into account for overal... | OTHER | port | X3 (character sheet AF) |
| 491 | `6b8f85c02` | 2026-09-02 | `PlayerTrainHandler`: Fix training multiple RAs simultaneously RefreshSpecDependantSkills was cal... | P3 combat | port | harvest A14 |
| 492 | `dda75528c` | 2026-09-02 | `MeleeSpeedPercentCalculator`: Fix incorrect usage of ToA and RA bonuses * Ability and item bonus... | P3 combat | port | harvest B8 |
| 493 | `3fa9934e4` | 2026-09-02 | Moved `eProperty.CastingSpeed` handling to `MeleeSpeedPercentCalculator` * Fixes incorrect cap on... | P3 combat | port | harvest B8 |
| 494 | `5200f9e00` | 2026-09-02 | Moved `eProperty.ArcherySpeed` handling to `MeleeSpeedPercentCalculator` * Now subject to the sam... | P3 combat | port | harvest B8 |
| 495 | `d1ce9437e` | 2026-09-02 | `MeleeSpeedPercentCalculator`: Rename to `AttackSpeedPercentCalculator` | P3 combat | port | harvest B8 rename (take with the block) |
| 496 | `469c9b7a1` | 2026-09-02 | `BlockRoundHandler`: Fix interaction with archery when the target is moving Fixes an issue where... | P3 combat | port | harvest A17 |
| 497 | `99e18b6b2` | 2026-09-02 | `AttackSpeedPercentCalculator`: Fix ToA bonuses being additive with RA bonuses Old sources seem t... | P3 combat | port | harvest B8 |
| 498 | `4c05debd6` | 2026-09-02 | `AttackSpeedPercentCalculator`: Fix rounding Was introduced in dda75528c7159abf6cd828cfa6bfa6fef3... | P3 combat | port | harvest B8 |
| 499 | `e142cbdb1` | 2026-09-02 | `AttackAction.CalculateCriticalChance`: Fix ranged attacks using melee crit chance in some cases | P3 combat | port | harvest A6 |
| 500 | `4b56561a1` | 2026-09-03 | `PlayerTrainHandler`: Amend 6b8f85c026fce28c1d24b608128fb7f9cdbc5e43 * This commit caused abiliti... | P3 combat | port | harvest A14 |
| 501 | `290e8e253` | 2026-09-03 | `GamePlayer.UseSlot`: Fix using a charge on a bow also drawing the bow | P3 combat | already have | in our tree |
| 502 | `1713a0e8f` | 2026-09-03 | Fix minor issues related to savage buffs * Replaced Effect.SavageBuff. * Fixes savage resist buff... | P2 effects | port | harvest B14 (Savage buffs) |
| 503 | `e25f4b370` | 2026-09-03 | `EffectHelper.GetPropertiesFromEffect`: Refactor to avoid allocation | P2 effects | port |  |
| 504 | `d0cafbd5e` | 2026-09-03 | `SpellHandler.CalculateDamageBase`: Fix style procs doing less damage than intended Because of in... | P2 effects | port | harvest A6 |
| 505 | `f944eaaed` | 2026-09-03 | Amend previous commit | P2 effects | port | amends d0cafbd5e |
| 506 | `cc350e3b7` | 2026-09-03 | `GameLiving.TryEvade`: Minor clean up | P3 combat | skip-not-relevant | cleanup only |
| 507 | `2fe6ae6ad` | 2026-09-03 | `GameLiving.TryEvade/TryParry`: Remove outdated Savage style handling | P3 combat | port |  |
| 508 | `a08e0a6cf` | 2026-09-03 | `GamePlayer.UseSlot`: Fix Volley | P3 combat | already have | in our tree |
| 509 | `8457ac5d3` | 2026-09-04 | `GameNPC.StartInterruptTimer`: Apply interrupt duration offset to pets too | P2 effects | port |  |
| 510 | `25a5f6cad` | 2026-09-04 | `GameNPC.StartInterruptTimer`: Exclude Necromancer pets from increased interrupt duration logic | P2 effects | port | harvest A16 |
| 511 | `472dd93e0` | 2026-09-04 | `SpellHandler`: Fix MoC for Necromancer pet | P2 effects | port | harvest A16 |
| 512 | `bf414c248` | 2026-09-04 | `SpellHandler.IsTargetAffectedBySpellType`: Fix non-harmful spells being useable on siege weapons | P2 effects | port |  |
| 513 | `42024abdc` | 2026-09-04 | `Spellhandler.FinishSpellCast`: Fix failed spells initiating cooldowns and costing endurance | P2 effects | skip-not-relevant | reverted upstream (509c699f9) |
| 514 | `9f0bd91b1` | 2026-09-04 | `ResistCalculator`: Re-add buff bonus cap Level / 2 + 1 instead of 24 Was erroneously removed by... | P2 effects | port | take with 1713a0e8f |
| 515 | `000da6f97` | 2026-09-04 | `ResistCalculator`: Minor clean up | P2 effects | skip-not-relevant | cleanup only |
| 516 | `e1fdc0090` | 2026-09-05 | `AttackAction`: Fix incorrect attack interval being passed to WeaponAction's ctor Fixes too long... | P3 combat | port | harvest B8 |
| 517 | `509c699f9` | 2026-09-05 | Revert 42024abdc71bce8989bc2842a2b4199b960ac523 | P2 effects | skip-not-relevant | revert; net zero |
| 518 | `24ce38595` | 2026-09-05 | `AttackComponent`: Minor clean up | P3 combat | skip-not-relevant | cleanup only |
| 519 | `3c1a424cb` | 2026-09-05 | `AttackComponent.AddAttacker`: Rely on AttackType instead of on Interval being >0 | P3 combat | port |  |
| 520 | `57a8584ff` | 2026-09-05 | Fix damage adds / shield to use independent weapon speeds for dual wield | P3 combat | port | harvest A6 |
| 521 | `6e2d36b9e` | 2026-09-05 | `AttackData`: Fix damage adds / shields on NPCs with no weapon Regression caused by 57a8584ff01f2... | P3 combat | port | harvest A6 |
| 522 | `7539cebb9` | 2026-09-05 | `AttackComponent`: Change two-handed damage bonus to no longer apply to NPCs Also guarded slow we... | P3 combat | skip-nerf-or-restriction | harvest B9: two-handed damage bonus no longer applies to NPCs |
| 523 | `8721f20a1` | 2026-09-05 | `GameLiving.TryBlock`: Revet custom changes making NPCs behave more like players Allow NPCs to bl... | P3 combat | hold-for-owner | harvest B9: NPCs block without a shield (stronger) but lose the arrow block bonus |
| 524 | `b0fd5c055` | 2026-09-05 | `GameLiving.TryParry`: Revet custom changes making NPCs behave more like players Prevent NPCs fro... | P3 combat | skip-nerf-or-restriction | harvest B9: NPCs lose two-handed defense penetration |
| 525 | `9c161ae20` | 2026-09-05 | `GameLiving.TryEvade`: Revet custom changes making NPCs behave more like players Prevent NPCs fro... | P3 combat | hold-for-owner | harvest B9: NPC evade loses the arrow malus and dual-wield penetration (mixed) |
| 526 | `5090fa4fc` | 2026-09-05 | `GameLiving`: Simplify usage of DualWieldDefensePenetrationFactor and TwoHandedDefensePenetration... | P3 combat | hold-for-owner | follows the B9 NPC defense changes (also fixes /targetstats factors) |
| 527 | `e6fe1661a` | 2026-09-05 | `ParryChanceCalculator`: Change Necromancer pes to no longer gain parry chance from dexterity | P2 effects | hold-for-owner | Necromancer pets no longer get parry from dexterity (weaker player pets) |
| 528 | `ab90dbf12` | 2026-09-05 | `GameLiving.TryEvade/TryParry`: Remove defense penalty from attacker count for NPCs | P3 combat | hold-for-owner | harvest B9: NPCs lose the attacker-count defense penalty (stronger NPCs; GameBots need the exemption) |
| 529 | `01d314244` | 2026-09-05 | `GamePlayer.OnAttackedByEnemy`: Fix exception caused by Triple Wield (lacks a SpellHandler) | P2 effects | port |  |
| 530 | `7cd8b8f12` | 2026-09-05 | `NecromancerPetBrain`: Fix spell not dequeuing on LoS check fail | P2 effects | port | harvest A16 |
| 531 | `bba0b80c6` | 2026-09-05 | `HoTSpellHandler, DivineIntervention`: Refactor to show effective healed amount | P2 effects | port |  |
| 532 | `97836b313` | 2026-09-05 | Fix DoTs and Disease not updating group window status immediately Cleaned up CrowdControlECSEffec... | P2 effects | port |  |
| 533 | `7b0df1b12` | 2026-09-06 | Remove CraftersTitle.LegendaryGrandmaster Didn't exist in 1.65, doesn't exist on live today, and... | P3 combat | already have | in our tree |
| 534 | `ad789ee4d` | 2026-09-06 | Remove BasicCrafting titles | P3 combat | already have | in our tree |
| 535 | `20a563b05` | 2026-09-06 | `CraftNPC`: Fix chosen primary skill not being saved Caused by CraftingProgressMgr not tracking t... | P3 combat | port | harvest A10 |
| 536 | `a376d6182` | 2026-09-07 | Allow spells from different spell lines than Item_Effects to be shown as charges on item tooltips | OTHER | port | X3 (item tooltips show charges) |
| 537 | `c92f7407c` | 2026-09-07 | Allow pets to cast on rooted targets, tanglers on immune targets Reverts a custom change from Atlas. | P2 effects | port | harvest B10 (removes an Atlas restriction) |
| 538 | `c3067e84d` | 2026-09-07 | `ArmorFactorCalculator`: Make NPCs no longer benefit from spec AF and Paladin AF chant | P3 combat | skip-nerf-or-restriction | harvest B9: NPCs lose spec AF and the Paladin AF chant |
| 539 | `6ef9a089f` | 2026-09-07 | `GameLiving`: Reduce con and dex impact on NPC melee absorbption from 1% every 5.5 to 1 every 6. | P3 combat | skip-nerf-or-restriction | harvest B9: con and dex matter less for NPC melee absorb |
| 540 | `58bc5cdff` | 2026-09-07 | `ResistCalculator`: Change dex contribution to resists to no longer affect melee resists | P3 combat | hold-for-owner | dexterity stops adding to melee resists, for every character |
| 541 | `738f82fa1` | 2026-09-07 | `ResistCalculator`: Chance dex contribution to magical resists to no longer affect CC duration | P3 combat | hold-for-owner | dexterity's magic resist share stops shortening CC, for every character |
| 542 | `d79a497e6` | 2026-09-07 | `GameSiegeBallista`: Fix damage against dead entities | P4 AI/world | port | harvest A11 |
| 543 | `5b60e16ab` | 2026-09-07 | Fix siege weapons not firing after being moved and change ground targeting behavior * Moving a si... | P4 AI/world | port | harvest A11; check BotSiegeRuntime before the ground-target change |
| 544 | `8ad486e27` | 2026-09-08 | `AttackComponent`: Enable strafing misses against NPCs Live behavior. | P3 combat | port | harvest B14 |
| 545 | `620c4e653` | 2026-09-08 | `DirtyTricksECSEffect`: Fix combat log messages | P2 effects | port |  |
| 546 | `f55b1534f` | 2026-09-10 | `AttackComponent.CalculateVarianceRange`: Fix incoherent specmod for unspeced attacks vs low leve... | P3 combat | port |  |
| 547 | `4783841c1` | 2026-09-11 | `GamePlayer.QuitTimer`: Fix desync when ticking during region transition | P1 core | port | harvest A17 |
| 548 | `02a03913f` | 2026-09-12 | `NpcCastingComponent`: Improve immobile / caster guard behavior on LoS loss | P4 AI/world | port | harvest B4 |
| 549 | `b23785d07` | 2026-09-13 | `DirtyTricksDetrimentalECSGameEffect`: Fix third party message | P2 effects | port |  |
| 550 | `0abde7b61` | 2026-09-13 | `NpcAttackAction`: Update targeting behavior * Ranged NPCs now lock onto their target at draw tim... | P4 AI/world | port | harvest B4; exempt GameBots |
| 551 | `d1fc74b3e` | 2026-09-13 | Amend previous commit | P4 AI/world | port | amends 0abde7b61 |
| 552 | `8cf2f1aea` | 2026-09-13 | `SalvageCalculator`: Optimize and clean up * Should behave the same way as before, but lack of un... | P3 combat | skip-not-relevant | Salvage calculator rewrite, same behaviour; our Salvage port is newer |
| 553 | `8274b06c1` | 2026-09-16 | `AttackComponent`: Fix eProperty.MeleeDamage and eProperty.RangedDamage to affect unstyled damage... | P3 combat | port | harvest A6 |
| 554 | `fe6d46dde` | 2026-09-16 | `Pathfinder`: Remove IsStraightLineHeightSafe Causes NPCs to leave the mesh too often when a move... | P4 AI/world | port | harvest A7 |
| 555 | `f0aaa1ea2` | 2026-09-25 | `targetstats`: add shield attack info | OTHER | port | X2 (/targetstats shield info) |
| 556 | `8fa721ada` | 2026-10-30 | `GameLoopThreadPoolMultiThreaded`: Prevent workers from corrupting frame state Likely to happen i... | P1 core | port | thread pool frame state corruption |
| 557 | `a82613f2c` | 2026-10-30 | `GameLoopThreadPoolMultiThreaded`: Allow pending work from interrupted workers to be resumed Enqu... | P1 core | port |  |
| 558 | `130fc3a24` | 2026-10-30 | Reorganize GameLoop related classes | P1 core | skip-not-relevant | file reorganisation only |
| 559 | `69da1fce9` | 2026-10-30 | `TickExecutor`: Make GameLoop agnostic Added TickExecutorInitParams. Renamed Init to Start. Added... | P1 core | skip-not-relevant | TickExecutor refactor (sharding series) |
| 560 | `b64b19fcc` | 2026-10-31 | `AttackData`: Remove IsOffHand Was set to true only when DbInventoryItem.Hand == 2. This property... | P3 combat | port |  |
| 561 | `27bc0293f` | 2026-11-01 | Attempt to fix attack speed related issues Fixes: * Incorrect damage shield / add values for rang... | P3 combat | port | harvest B8 |
| 562 | `a7468c046` | 2026-11-01 | `AttackComponent.GetWeaponAttackSpeeds`: Fix incorrect ToA bonus usage when ALLOW_OLD_ARCHERY ==... | P3 combat | port | harvest B8 |
| 563 | `4264d87e1` | 2026-11-01 | `AttackAction`: Fix _styleChainStage not always resetting properly Could remain at its current va... | P3 combat | port |  |
| 564 | `2fecd64f0` | 2026-11-01 | `GameDoor`: Fix border keep doors sometimes opening / closing immediately Clients normally can't... | P4 AI/world | port | harvest A11; must not block the bot door path |
| 565 | `6258566a3` | 2026-11-01 | `AttackAction`: Fix late / redundant attack speed calculations potentially causing incorrect beha... | P3 combat | port | harvest B8 |
| 566 | `e24f0c934` | 2026-10-24 | Add 1.130 client support with optional RSA/RC4 encryption | P1 core | skip-not-relevant | 1.130 client support; we ship 1.127 and run offline |
| 567 | `1e1798f3b` | 2026-11-02 | Decide 1.130 crypto once per connection; plain comments | P1 core | skip-not-relevant | 1.130 crypto |
| 568 | `e4b9e02b1` | 2026-11-02 | Refactor /rent and /bountyrent and fix several bugs | OTHER | port | X1 (/rent and /bountyrent fixes) |
| 569 | `e04fb3af8` | 2026-11-02 | `GameStaticItem`: Fix potential memory leak on object delete Remove Delete override, allowing bas... | P1 core | port | harvest A3 |
| 570 | `2e6c22ea4` | 2026-11-02 | `DirtyTricksDetrimentalECSGameEffect`: Fix NRE when target isn't a player | P2 effects | port |  |
| 571 | `4a018e0ef` | 2026-11-02 | `GamePlayer`: Remove TargetInViewAlwaysTrueMinRange | P2 effects | port |  |
| 572 | `4198e9bc4` | 2026-11-02 | `SqlObjectDatabase`: Avoid creating transactions for single-row saves | P1 core | port | harvest A2 |
| 573 | `b49257e7f` | 2026-11-02 | `SqlObjectDatabase.SaveObjectImpl`: Fix transaction rollbacks | P1 core | port | harvest A2 |
| 574 | `9a8ea3f82` | 2026-11-02 | `DbInventoryItem`: Remove Dirty being forced to true when CanUseEvery == true Not needed. | P1 core | port |  |
| 575 | `097a0e5e0` | 2026-11-02 | `SkillBase.FindSpell`: Add early return when spellID == 0 | P2 effects | port |  |
| 576 | `5ae6ae40d` | 2026-11-02 | Simplify Rsa1130 and check all OAEP padding before rejecting | P1 core | skip-not-relevant | 1.130 RSA |
| 577 | `d9b7782c1` | 2026-11-02 | Support RSA key transport for 1.127 clients too; version-neutral names | P1 core | skip-not-relevant | RSA key transport; not needed offline on 127.0.0.1 |
| 578 | `dde8b4a5a` | 2026-11-02 | Choose a connection's encryption once, behind an interface | P1 core | skip-not-relevant | encryption interface for 1.130/RSA |
| 579 | `c874f75da` | 2026-11-02 | `SkillBase.GetSpellLine`: Do not clone or create temporary lines | P2 effects | port |  |
| 580 | `2e607ddeb` | 2026-11-03 | `GameLiving`: Optimize proc handling | P2 effects | port | proc handling perf (FindSpell per hit) |
| 581 | `c22198d2f` | 2026-11-03 | Update README.md | OTHER | skip-not-relevant | upstream README |
| 582 | `a12b9e7a0` | 2026-11-03 | Move Detour docs to docs directory | OTHER | skip-not-relevant | upstream docs |
| 583 | `a981b22f7` | 2026-11-04 | Harden and optimize UDP receive path * Replace shared 8 KB chunked buffer and per-packet Task.Run... | P1 core | skip-not-relevant | UDP hardening (harvest D); local single-player |
| 584 | `3513bad49` | 2026-11-04 | Remove unused using | P1 core | skip-not-relevant | unused using |
| 585 | `ce0c1ca79` | 2026-11-05 | Update README | OTHER | skip-not-relevant | upstream README |
| 586 | `90c98f771` | 2026-11-04 | Flatten ServiceObjectStore API and optimize SchedulableServiceObjectArray | P1 core | skip-not-relevant | ServiceObjectStore series |
| 587 | `6d5b179be` | 2026-11-05 | Update NuGet packages | OTHER | skip-not-relevant | NuGet bump |
| 588 | `bc5c06582` | 2026-11-05 | `GameNPC.GetScaledSpell`: Lower unhandled spell log severity | P4 AI/world | port |  |
| 589 | `9a4a5ec34` | 2026-11-05 | `GameNPC.GetScaledSpell`: Return offensive and defensive proc spells without logging | P4 AI/world | port |  |
| 590 | `f6e1650b1` | 2026-11-05 | `ECSGameEffect`: Switch to IShardedServiceObject | P1 core | skip-not-relevant | ServiceObjectStore sharding series |
| 591 | `face2ba1a` | 2026-11-05 | `ECSGameTimer`: Switch to IShardedServiceObject | P1 core | skip-not-relevant | ServiceObjectStore sharding series |
| 592 | `fdee2b391` | 2026-11-05 | `ABrain`: Switch to IShardedServiceObject | P1 core | skip-not-relevant | ServiceObjectStore sharding series |
| 593 | `5609569f4` | 2026-11-06 | `ParallelTickExecutor`: Make WorkProcessor.Clear abstract May avoid a runtime type check. Or not... | P1 core | skip-not-relevant | sharding series |
| 594 | `9a15f4c44` | 2026-11-06 | `ShardedServiceObjectArray`: Pass _currentUpdateNow instead of fetching it from GameLoop An alter... | P1 core | skip-not-relevant | sharding series |
| 595 | `b9b32cf03` | 2026-11-06 | `CureNearsightSpellHandler`: Fix invalid effect type causing Bladeturn effects to be cured * Redu... | P2 effects | port | Cure Nearsight no longer cures Bladeturn |
| 596 | `fe5c99dec` | 2026-11-06 | `SkillBase`: Optimize FindSpell It called GetSpellList, which creates a new list and clones every... | P2 effects | port |  |
| 597 | `08bbdb8b1` | 2026-11-06 | `ServiceObjectId`: Reduce memory footprint | P1 core | skip-not-relevant | ServiceObjectStore series |
| 598 | `c0f41b979` | 2026-11-07 | Minor clean up | P1 core | skip-not-relevant | cleanup only |
| 599 | `377fe9cb0` | 2026-11-07 | `StatPrint`: Reduce GC allocations and improve thread safety * Reuse a static StringBuilder and r... | P1 core | port |  |
| 600 | `732ed6ac7` | 2026-11-07 | `EffectListComponent`: Only add effects to the store if they can expire | P2 effects | port |  |
| 601 | `c6e8cac19` | 2026-11-07 | PetECSGameEffect being added to pet window | P2 effects | port |  |
| 602 | `e645e3116` | 2026-11-07 | `EffectListComponent`: Fix race condition causing Focus Shield appearing late in the pet window M... | P2 effects | port |  |
| 603 | `f42b2b246` | 2026-11-07 | `ServiceObjectId`: Fix re-registration of already registered objects | P1 core | skip-not-relevant | ServiceObjectStore series |
| 604 | `1523ae8c0` | 2026-11-07 | `ServiceObjectStore`: Remove no longer needed Add and Remove methods | P1 core | skip-not-relevant | ServiceObjectStore series |
| 605 | `5d3ce2325` | 2026-11-07 | `MarketSearchEngine`: Add early out when resultSets.Count == 1 Should be fine as long as the call... | OTHER | port | X1 (market search) |
| 606 | `34e3357ca` | 2026-11-07 | `MarketSearchEngine`: Optimize Search intersect Alloc dropped by 88%. Faster in most cases and wi... | OTHER | port | X1 (market search) |
| 607 | `39c16b82f` | 2026-11-07 | `GameLiving`: Add HasAbilities to avoid GameNPC.Abilities' hidden cost GameNPC.Abilities currentl... | P4 AI/world | port | HasAbilities; perf in pet brains |
| 608 | `a1e02efe1` | 2026-11-07 | `ECSGameEffect`: Fix CanExpire always returning true * Rely on Duration or PulseFreq instead. Get... | P2 effects | port |  |
| 609 | `30e005412` | 2026-11-07 | `ECSGameSpellEffect`: Remove redundant IsConcentrationEffect check. Concentration effects have th... | P2 effects | skip-not-relevant | cleanup only |
| 610 | `b7d4dc053` | 2026-11-08 | `ECSGameSpellEffect`: Fix PulseFreq being set to Spell.Frequency for proc spells. It caused them... | P2 effects | port | harvest A4 |
| 611 | `3f6374371` | 2026-11-08 | `MarketSearch`: Use MonotonicTime.NowMs | OTHER | port | X1 |
| 612 | `c7d2df30e` | 2026-11-08 | Amend previous commit | OTHER | port | X1 |
| 613 | `3356ed9f9` | 2026-11-08 | Update README Now with less slop. | OTHER | skip-not-relevant | upstream README |
| 614 | `c7925b14a` | 2026-11-08 | `ServiceObjectStore`: Auto-register IServiceObject<T> types via assembly scan Add Init. | P1 core | skip-not-relevant | ServiceObjectStore series |
| 615 | `552cf7e90` | 2026-11-08 | `ServiceObjectStore`: Make GameLoop agnostic Make UpdateAndGetView take "now". Thought about doin... | P1 core | skip-not-relevant | ServiceObjectStore series |
| 616 | `b0f39b0be` | 2026-11-09 | `ServiceObjectView`; Make GameLoop agnostic and pure data Move ServiceObjectView.ExecuteForEach t... | P1 core | skip-not-relevant | ServiceObjectStore series |
| 617 | `0749ad1fb` | 2026-11-09 | `GameLoop`: Split Init in two A future commit may require the game loop to be in an initialized s... | P1 core | skip-not-relevant | GameLoop refactor (sharding series) |
| 618 | `da336e85d` | 2026-11-09 | Add `IExecutor` and `GameLoopExecutor` singleton Interface for tests, with the real implementatio... | P1 core | skip-not-relevant | test executor interface for the series |
| 619 | `84246ba54` | 2026-11-09 | Add `ITickExecutor` interface for tests | P1 core | skip-not-relevant | test executor interface for the series |
| 620 | `dc09e558f` | 2026-11-09 | `ShardedServiceObjectArray`: Make GameLoop agnostic | P1 core | skip-not-relevant | sharding series |
| 621 | `b10f08cf4` | 2026-11-09 | Add UTs for ServiceObjectStore related classes * Covers the following classes: - ServiceObjectSto... | P1 core | skip-not-relevant | tests for the ServiceObjectStore series |
| 622 | `a4dc4ed15` | 2026-11-09 | Minor clean up | P1 core | skip-not-relevant | cleanup only |
| 623 | `446a99f1f` | 2026-11-12 | `PlayerCreationRequestHandler`: Add stealth check | OTHER | port | X3 (character creation stealth check) |
| 624 | `d74d4941f` | 2026-11-12 | FrontiersPortalStone: open the war map with dialog code 0x26 on 1.127+ | OTHER | port | X3 (portal stone war map on 1.127) |
| 625 | `ec097f507` | 2026-11-12 | Quest window: make reward items delvable on 1.115+ clients | OTHER | port | X3 (quest reward delve) |
| 626 | `ef017586c` | 2026-11-12 | SpellDelve: send cast_timer for casts under 2 seconds | OTHER | port | X3 (spell delve cast timer) |
| 627 | `662f315fd` | 2026-11-15 | Fix trade deadlocks between parallel packet handlers | P1 core | port | harvest A13 |
| 628 | `ee6d8eaac` | 2026-11-17 | Clean up #97 | P1 core | port | amends 662f315fd |
| 629 | `8dcf95be8` | 2026-11-19 | Scripts: log compile errors instead of "Bad IL format" | P1 core | port | script compile errors logged (crash clarity) |
| 630 | `686c895f2` | 2026-11-20 | Keeps: don't offer hookpoints that have nothing to sell | P4 AI/world | port | harvest A11 |
| 631 | `6c41d67b3` | 2026-11-20 | Keeps: send the hookpoint list sorted by ID | P4 AI/world | port |  |
| 632 | `ffb6c9569` | 2026-11-22 | Keeps: look up keep components by ID in the hookpoint handlers | P4 AI/world | port | harvest A11 |
