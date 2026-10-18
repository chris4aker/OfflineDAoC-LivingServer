# Database and client helpers from the 0.33 development copy

These are small, focused scripts that were used while building 0.33. They expect to sit at
`<playable>/tools/claude-version/` and work on that folder's `runtime`. Read a script before
running it: several are one-time migrations, and every script that writes makes a backup first.
Close the launcher, the game and the server before running any of them.

| Script | What it does |
|---|---|
| `helmet_face_check.py <model>` | Read-only. Lists every helmet model that shares a mesh, and which extensions real items use. See [HELMET-FACE-FIX.md](../../docs/HELMET-FACE-FIX.md) |
| `cross_realm_gear_audit.py [--write]` | Read-only report of item templates whose realm doesn't match their model's realm (for example a Midgard hat on a Hibernia-only mesh). `--write` regenerates the bot skip list `source/server/GameServer/bots/BotCrossRealmGear.cs`. Run it from an installed game folder |
| `pet_spell_changes.py` | Sluaghbinder pet spell values (priest heals, Cairnheart heals over time and so on). Checks the value at each stage, so it's safe to re-run |
| `seed_sluaghbinder.py`, `Add-SluaghbinderEpicSpells.py` | Class, trainer and epic-spell seeding used by the Sluaghbinder builds |
| `set_xp_by_level.py [--player RANGES] [--bots RANGES] [--off] [--apply]` | Sets or clears XP rates by level range (`xp_rate_by_level`, `bot_xp_rate_by_level`), for example `--player "1-19:3;20-34:5;35-50:12"`. Shows the current settings with no options. Dry run by default; `--apply` writes after a backup. Needs a server build with these settings. Not yet in a download |
| `clean_slate.py <db>` | Clears characters, bots and auctions from a copy of a database |
| `cleanup_orphan_unique_items.py` | Removes loot definitions that no inventory references (writes a backup) |
| `coif_head_mode.py`, `si_portal_visuals.py` | Earlier client catalog experiments (helmet head mode, SI portal visuals) |
| `si_portal_collision.py check\|install` | Makes the Shrouded Isles portal copies in Cotswold, Mularn and Mag Mell solid (Collide 1 on their nifs.csv and fixtures.csv rows, like the Shrouded Isles originals), so you walk up onto the platforms. `check` is read-only; `install` backs up the three zone archives first. Not yet in a download |
| `*forest_poacher_camp*.py`, `*.sql` | One-time camp fixes, already included in the 0.33 world |
| `fix_moher_phaeghoul_tree.py` | Moves the Cliffs of Moher phaeghoul that spawns inside a dead tree to open ground nearby. Dry run by default; `--apply` changes only that spawn's position, after a backup. Not yet in a download |
| `fix_cothrom_seeds_and_leptus.py` | Makes Leptus (Domnann) always level 6 instead of randomly level 6 or 51, and removes the 13 level 6-7 venomous spore seeds from the level 42-55 Cothrom Gorge spore field (archived with a reason; the level 48-55 venomous spores stay). Bots saved on a seed camp pick a new one. Dry run by default; `--apply` writes after a backup. Not yet in a download |
| `add_moher_spectre_sentinel_spawns.py` | Adds four bantam spectres and two koalinth sentinels at Cliffs of Moher, copied from the safe spawn next to them and placed on open, collision-free ground away from high-level monsters, so bots have a real camp after the risky spawns were taken off their list. Dry run by default; `--apply` writes after a backup. Not yet in a download |
| `vine_monsters_strangler_effect.py` | Makes the invisible choker, shackler and Throttler (Dales of Devwy) and tendril (Aegir's Landing) show the strangler's Tangling Vines effect by giving them the strangler's class. Stats, level, loot and model are unchanged. Dry run by default; `--apply` writes after a backup; `--undo` puts them back. Not yet in a download |
| `move_realm_exchange_npcs.py` | Moves the Jordheim Realm Exchange NPC back to the small ledge by the Name Registrar and the Camelot one to the open courtyard in front of the benches, each with a guard on either side. Only position and heading change. Dry run by default; `--apply` writes after a backup; `--undo` puts them back. New installs get these spots from the setup tool. Not yet in a download |
| `move_princess_nahemah.py`, `move_df_dais_lords.py` | Move Princess Nahemah, High Lord Saeor and High Lord Oro off their daises (the bots' walking map doesn't reach them) onto the floor in front, so Darkness Falls raids can fight them. Asserts the old position first; `rollback` puts them back. Already in the 0.36 world |
| `set_relic_pad_requirement.py` | Sets the relic pad requirement (`relic_players_required_on_pad`) from 16 to 8; your companion bots count toward it. `rollback` restores 16. Already in the 0.36 world |
| `set_quest_target_respawns.py` | Quest kill targets respawn in 300 seconds (162 spawns, listed with their old times in `set_quest_target_respawns.ledger.json`). Only changes rows still above 300 seconds, so it's safe to re-run. Already in the 0.36 world |
| `set_165_pet_models.py` | The 1.65 looks of the Necromancer and Bonedancer pets (models and equipment). Safe to re-run. Already in the 0.36 world |
| `add_sluaghbinder_realm_abilities.py` | Gives the Sluaghbinder its realm abilities, including Crippling Plague. Safe to re-run. Already in the 0.36b world |
