class_name CharacterState
extends Node
## One character's state and the rules every character lives by, the player's and every gamebot's alike (owner
## 2026-10-11: bots and players share rules; gamebots are full player equivalents). PlayerData is the player's sheet
## (it extends this with the account, the quickbars, the vault and the UI's signals); the world host and the game
## give each gamebot its own. What is here: stats (base, items, buffs), hit points / power / endurance, experience
## and levels (the base class cap, stat gains, spec points, autotrain), specs and training, promotion, deaths'
## experience loss, resists, factions, the inventory and its slots, money, merchants.
## A bot's sheet says its lines through `message` too (the bot's log hears them); nothing else listens.

signal changed
signal inventory_changed
signal message(text: String, kind: String)
signal leveled_up(new_level: int)                   # a level gained (the level-up effect)
signal skills_changed                    # specs / class changed: the skill, magic and combat sheets refresh
signal weapon_switched                              # active weapon set changed (Combat leaves attack mode)
signal bought(id: String)                         # an item bought from a merchant (a stable ticket: HorseRoutes)

const BACKPACK := 40
const BACKPACK_END := 79
const SACK := 8
const FIRST_VAULT := 110                 # the old server's vault slots (eInventorySlot FirstVault .. LastVault)
const VAULT_SLOTS := 40
const EQUIP_SLOTS := [10, 11, 12, 13, 21, 22, 23, 24, 25, 26, 27, 28, 29, 32, 33, 34, 35, 36, 37]
const SLOT_NAMES := {10: "Right Hand", 11: "Left Hand", 12: "Two Handed", 13: "Ranged", 21: "Helm", 22: "Hands",
		23: "Feet", 24: "Jewelry", 25: "Torso", 26: "Cloak", 27: "Legs", 28: "Sleeves", 29: "Neck", 32: "Waist",
		33: "Left Wrist", 34: "Right Wrist", 35: "Left Ring", 36: "Right Ring", 37: ""}
const SHIELD := 42
const BASE_CLASS_CAP := 5                # owner: a base class stops at 5 until it joins an advanced guild
const MAX_END := 100
const SELL_RATIO := 50                   # merchants pay 50% of an item's value (old server item_sell_ratio)
const STAT_PROP := [1, 3, 2, 4, 5, 6, 7, 8]

var character := {}
var level := 1
var exp_frac := 0.0                      # progress through the level, 0..1 (10 jewels on the status panel)
var realm_points := 0
var health := 1.0                        # fractions for the bars (health = hp / max_hp)
var hp := 1                              # hit points (CombatRules.player_max_hp: class base HP, level, CON)
var max_hp := 1
var xp := 0                              # total experience (CombatRules.xp_for_level: the old server's table)
var specs := {}                          # specialization key -> trained level (ClassDb)
var endurance := 1.0                     # fraction for the bar (end_pts / MAX_END)
var end_pts := 100                       # endurance points (old server: max 100)
var power_pts := 0                       # power (SpellRules.max_power: casting stat + level)
var buffs := {}                          # the player's active buffs, summed by Casting (see effective_stats)
var death_count := 0                     # deaths at this level (old server: lighter loss on the 1st and 2nd)
var con_lost := 0                        # constitution lost at deaths (a healer restores it for a fee)
var grave := {}                          # {pos: [x, y, z], xp: n} - where the release loss can be prayed back
var max_power := 0
var power := 1.0
var money := {"mithril": 0, "platinum": 0, "gold": 0, "silver": 0, "copper": 0}
var inventory := {}                      # slot (int) -> {id, count}
var quests := {}                         # active quests: quest id (String) -> {step: n} (QuestEngine)
var factions := {}                       # faction id (String) -> your hostility to it, -100..100 (Faction.AggroLevel)
var quests_done := {}                    # finished quests: quest id (String) -> times done
var stats := []                          # STR CON DEX QUI INT PIE EMP CHA (the client's order)
var class_stats := []                    # the stat indices the class lists (drawn highlighted, as on the client's screen)
var active_weapon := "standard"          # standard / twohand / ranged
var encumbered := false
var bonus_max_hp := 0                        # Stag's burst (Abilities)
var is_bot := false                      # a gamebot's sheet: the bots' experience rate, no item wear, no saving here
var is_companion := false                # a companion's: the player-and-companion experience rate (owner)


## A character record into the sheet (the player's at log-in, a gamebot's when the world host or the game takes it):
## level, money, the active weapon set, stats, experience, deaths, quests, factions, specs, health and power, the
## inventory - a new character gets its base class's starting gear.
func load_sheet(c: Dictionary) -> void:
	character = c
	level = int(c.get("level", 1))
	if c.get("money") is Dictionary:
		for k in money:
			money[k] = int(c.money.get(k, 0))              # JSON brings numbers back as floats ("0.0" on the sheet)
	active_weapon = String(c.get("active_weapon", "standard"))
	_load_stats()
	xp = int(c.get("xp", CombatRules.xp_for_level(level)))
	death_count = int(c.get("death_count", 0))
	quests = c.get("quests", {}) if c.get("quests") is Dictionary else {}
	quests_done = c.get("quests_done", {}) if c.get("quests_done") is Dictionary else {}
	factions = c.get("factions", {}) if c.get("factions") is Dictionary else {}
	con_lost = int(c.get("con_lost", 0))
	grave = c.get("grave", {}) if c.get("grave") is Dictionary else {}
	_load_specs()
	buffs = {}
	recalc_max()
	hp = clampi(int(c.get("hp", max_hp)), 1, max_hp)
	power_pts = clampi(int(c.get("power", max_power)), 0, max_power)
	_update_bars()
	_loaded()
	inventory.clear()
	var inv = c.get("inventory")
	if inv is Dictionary:
		for k in inv:
			inventory[int(k)] = inv[k]
	else:
		_give_starter_gear()
	# characters made before shields joined the starting kit (owner 2026-10-10): their class's training shield, once
	if inv is Dictionary and not bool(c.get("kit_shield_given", false)):
		for id in ItemDb.starter(_class_id()):
			if int(ItemDb.item(id).get("object_type", 0)) == 42 and find_item(id) < 0:
				if not inventory.has(11) and not inventory.has(12):
					inventory[11] = {"id": id, "count": 1}
				else:
					_to_backpack({"id": id, "count": 1})
		character["kit_shield_given"] = true
	# characters made before the starter rule above: nothing in hand but a two-hander / bow ready -> hold that
	if active_weapon == "standard" and not inventory.has(10) and not inventory.has(11):
		if inventory.has(12):
			active_weapon = "twohand"
		elif inventory.has(13):
			active_weapon = "ranged"


## Called once the sheet's numbers are in, before the inventory (PlayerData: the character's own keys).
func _loaded() -> void:
	pass


## The sheet's numbers back into the character record (saving it is the owner's business: PlayerData writes it to
## the account, the world host to the bot roster).
func store_sheet() -> void:
	if character.is_empty():
		return
	character["money"] = money.duplicate()               # a copy: the next character loads into `money` in place
	character["active_weapon"] = active_weapon
	character["stats"] = stats
	character["level"] = level
	character["xp"] = xp
	character["hp"] = hp
	character["power"] = power_pts
	character["death_count"] = death_count
	character["quests"] = quests
	character["quests_done"] = quests_done
	character["factions"] = factions
	character["con_lost"] = con_lost
	character["grave"] = grave
	character["specs"] = specs
	var inv := {}
	for k in inventory:
		if not is_vault(int(k)):                           # the vault is the account's (PlayerData._save_vault)
			inv[str(k)] = inventory[k]
	character["inventory"] = inv


func save_character() -> void:
	store_sheet()


## The REALM OPTIONS experience rate for this character: the players' rate, or the bots' own.
func xp_rate() -> int:
	return RealmSettings.bot_xp_rate() if is_bot and not is_companion else RealmSettings.xp_rate()


## The old server's regeneration (ClassicRestRegeneration / HealthRegenerationAmountCalculator): seconds between
## ticks - 6 standing, 3 sitting, 14 in combat (13 sitting in combat).
func regen_interval(sitting: bool, fighting: bool) -> float:
	return 6.0 - (3.0 if sitting else 0.0) + (8.0 if fighting else 0.0) - (1.0 if sitting and fighting else 0.0)


## One tick: hit points 2.5 + level x 0.25 (none while diseased), power 2.5 + level x 0.2 (PowerRegeneration-
## AmountCalculator; a list caster under half its power gets half), each + its regeneration buffs.
func regen_tick() -> void:
	if hp < max_hp and not bool(buffs.get("diseased", false)):
		set_hp(hp + int(2.5 + level * 0.25) + int(buffs.get("regen_hp", 0)))
	if max_power > 0 and power_pts < max_power:
		var pw := 2.5 + level * 0.2
		if String(ClassDb.cls(class_id()).get("class_type", "")) == "ListCaster" and power < 0.5:
			pw /= 2.0
		set_power(power_pts + maxi(1, int(pw)) + int(buffs.get("regen_power", 0)))


## Endurance each second: 1 standing still out of combat, 4 sitting, none while moving or fighting (+ buffs).
func endurance_tick(moving: bool, sitting: bool, fighting: bool) -> void:
	var gain := (0 if fighting or moving else (4 if sitting else 1)) + int(buffs.get("regen_end", 0))
	if gain > 0 and end_pts < MAX_END:
		end_pts = mini(MAX_END, end_pts + gain)
		endurance = float(end_pts) / MAX_END
		changed.emit()


## The release after a death (old server GamePlayer.Release, level 6+): the death's experience taken again into a
## gravestone where it fell (pray at it to win it back), constitution 1-3 by deaths this level (a healer restores it).
func release_losses(death_loss: int, at: Vector3, yaw: float, zone: int) -> void:
	if level >= int(CombatRules.r().get("death", {}).get("exp_loss_level", 6)) and death_loss > 0:
		var before := xp
		xp = maxi(CombatRules.xp_for_level(level), xp - death_loss)
		var lost := before - xp
		if lost > 0:
			grave = {"pos": [at.x, at.y, at.z], "yaw": yaw, "xp": lost, "zone": zone}   # it stands in the zone you fell in
			message.emit("A grave was erected where you were slain.", "system")
			message.emit("Return to /pray at your grave to regain experience.", "system")
	if level >= int(CombatRules.r().get("death", {}).get("con_loss_level", 6)):
		con_lost += clampi(death_count, 1, 3)
		recalc_max()
		message.emit("You've lost some constitution, go to a healer to have it restored!", "system")


## Released: full health, endurance and power at the bind spot.
func restore_full() -> void:
	set_hp(max_hp)
	end_pts = MAX_END
	endurance = 1.0
	set_power(max_power)


func spend_endurance(n: int) -> void:
	end_pts = maxi(0, end_pts - n)
	endurance = float(end_pts) / MAX_END


# --- what a class may use (owner 2026-10-11: players, gamebots and companions alike) --------------------------------
# Armour: the realm's armour ability, which each class's career line raises as the character levels (classes.json
# abilities_by_class - an Armsman wears studded at 1, chain from 5, plate from 15; a Druid leather, reinforced from 10,
# scale from 20; casters cloth only). Weapons: the class's Weaponry abilities. Shields: the Shield ability's level is
# the largest size. Another realm's items: never (items carry their realm). The item's own class list, when it has one.
const ARMOR_NEED := {32: 1, 33: 2, 34: 3, 35: 4, 36: 5, 37: 3, 38: 4}   # cloth leather studded chain plate reinforced scale
const WEAPONRY := {2: "Weaponry: Crushing", 3: "Weaponry: Slashing", 4: "Weaponry: Thrusting", 5: "Weaponry: Archery",
		6: "Weaponry: Two Handed", 7: "Weaponry: Polearms", 8: "Weaponry: Staves", 9: "Weaponry: Longbows",
		10: "Weaponry: Crossbow", 11: "Weaponry: Swords", 12: "Weaponry: Hammers", 13: "Weaponry: Axes",
		14: "Weaponry: Spears", 15: "Weaponry: Composite Bows", 16: "Weaponry: Thrown", 17: "Weaponry: Left Axes",
		18: "Weaponry: Recurved Bows", 19: "Weaponry: Blades", 20: "Weaponry: Blunt", 21: "Weaponry: Piercing",
		22: "Weaponry: Large Weapons", 23: "Weaponry: Celtic Spears", 24: "Weaponry: Flexible",
		25: "Weaponry: Hand to Hand", 26: "Weaponry: Scythe", 27: "Weaponry: Fist Wraps", 28: "Weaponry: Mauler Staff",
		45: "Weaponry: Instruments"}


## Whether this character may equip an item - "" when it may, else the reason (the chat line). Wording improvised
## (the 1.65 lines aren't recorded) - "1.65 EXCEPTIONS MADE.txt".
func use_problem(it: Dictionary) -> String:
	if it.is_empty():
		return ""
	var realm := int(it.get("realm", 0))
	if realm != 0 and realm != realm_id():
		return "That item belongs to another realm."
	var allowed := String(it.get("allowedclasses", "0")).strip_edges()
	if allowed != "" and allowed != "0":
		var ids := Array(allowed.split(";", false)).map(func(x): return int(x))
		if not ids.has(class_id()):
			return "Your class can't use that item."
	var ot := int(it.get("object_type", 0))
	if ot == 0 or ot == 1 or ot == 41 or ot == 43 or ot == 44 or ot == 46:
		return ""                                          # generic / jewellery / ammunition: anyone
	var known := {}
	for a in ClassDb.abilities_known(class_id(), level, specs):
		known[String(a.key)] = int(a.level)
	if ARMOR_NEED.has(ot):
		var armor_key: String = {1: "AlbArmor", 2: "MidArmor", 3: "HibArmor"}.get(realm_id(), "HibArmor")
		if int(known.get(armor_key, 0)) < int(ARMOR_NEED[ot]):
			return "You can't wear that armor yet."
		return ""
	if ot == SHIELD:
		if int(known.get("Shield", 0)) < maxi(1, int(it.get("type_damage", 1))):
			return "You can't use a shield of that size."
		return ""
	if WEAPONRY.has(ot) and not known.has(String(WEAPONRY[ot])):
		return "You have no skill in that weapon."
	return ""


func can_use(it: Dictionary) -> bool:
	return use_problem(it) == ""


## Item condition (owner 2026-10-09: real 1.65 condition for the player; never for bots) - PlayerData wears them.
func wear_item(_slot: int) -> void:
	pass


## An item left a slot (sold): PlayerData drops its right-click pick.
func _item_gone(_slot: int) -> void:
	pass


## A new character wears its base class's starting gear (the old server's StarterEquipment, build_items.py).
func _give_starter_gear() -> void:
	var cls := _class_id()
	var chosen := ""
	for id in ItemDb.starter(cls):
		var it := ItemDb.item(id)
		var placed := false
		for s in slots_for(it):
			if not inventory.has(s):
				inventory[s] = {"id": id, "count": 1}
				placed = true
				# StartupEquipment: the first weapon placed picks the active set (right hand -> standard, two-hand slot
				# -> two-handed, ranged -> distance) - a new mage holds the starting staff (owner 2026-10-10)
				if chosen == "" and s in [10, 12, 13]:
					chosen = {10: "standard", 12: "twohand", 13: "ranged"}[s]
				break
		if not placed:
			_to_backpack({"id": id, "count": 1})
	if chosen != "":
		active_weapon = chosen
	save_character()


## Starting stats the client's way (pipeline/client_re/race_stats.py -> content/characters/stats.json, read out of
## game.dll): every stat 60 + the race's modifiers, plus the class's default 30-point bonus spread, which the client
## applied as soon as race + class were picked. The owner's rule (2026-10-09): the bonus is given automatically, no
## allocation screen. Saved with the character once made.
func _load_stats() -> void:
	var data = JSON.parse_string(FileAccess.get_file_as_string("res://content/characters/stats.json"))
	var base_cls: Dictionary = data.base_classes.get(str(_class_id()), {}) if data is Dictionary else {}
	class_stats = []
	for b in base_cls.get("bonus", []):
		class_stats.append(int(b[0]))
	if character.get("stats") is Array and character.stats.size() == 8:
		stats = (character.stats as Array).map(func(x): return int(x))
		return
	stats = [60, 60, 60, 60, 60, 60, 60, 60]
	if data is Dictionary:
		var race: Dictionary = data.races.get(str(_race_id()), {})
		if race.has("base"):
			stats = (race.base as Array).map(func(x): return int(x))
		for b in base_cls.get("bonus", []):
			stats[int(b[0])] += int(b[1])
	character["stats"] = stats.duplicate()


func _race_id() -> int:
	var ids := {"BRIT": 1, "AVAL": 2, "HIGH": 3, "SARE": 4, "NORS": 5, "TROL": 6, "DWAR": 7, "KOBO": 8, "CELT": 9,
			"FIRB": 10, "ELF": 11, "LURI": 12, "INCO": 13, "VALK": 14, "SYLV": 15}
	return int(ids.get(String(character.get("base", "")).get_slice("-", 0), 0))


func _class_id() -> int:
	if int(character.get("class_id", 0)) > 0:              # promoted to an advanced class at a trainer
		return int(character.class_id)
	var base := String(character.get("base", ""))        # e.g. CELT-ARMS
	var ids := {"FIGH": 14, "ELEM": 15, "ACOL": 16, "ROGU": 17, "MAGE": 18, "ARAW": 20, "WARR": 35, "MYST": 36,
			"SPIR": 37, "NROG": 38, "MAGI": 51, "ARMS": 52, "NATU": 53, "STEA": 54, "GROV": 57}
	return int(ids.get(base.get_slice("-", 1), 0))


func class_id() -> int:
	return _class_id()


## Your hostility to a faction (Faction.GetAggroLevel): what kills have made it, else the faction's base.
func faction_aggro(id: int) -> int:
	var f: Dictionary = CombatRules.r().get("factions", {}).get(str(id), {})
	return int(factions.get(str(id), f.get("base", 0)))


## Faction.GetStanding: > 75 aggressive, > 50 hostile, > 25 neutral, else friendly; "" when the faction isn't known.
func faction_standing(id: int) -> String:
	if id == 0 or not CombatRules.r().get("factions", {}).has(str(id)):
		return ""
	var a := faction_aggro(id)
	return "aggressive" if a > 75 else ("hostile" if a > 50 else ("neutral" if a > 25 else "friendly"))


## Faction.OnMemberKilled: your kill of one of its members - its friends (itself too) like you less, its enemies more:
## each moves by 1 on a 20% roll (-100..100), and each says so every time.
func faction_killed(id: int) -> void:
	var all: Dictionary = CombatRules.r().get("factions", {})
	var f: Dictionary = all.get(str(id), {})
	if f.is_empty():
		return
	for pair in [[f.get("friends", []), 1], [f.get("enemies", []), -1]]:
		for other in pair[0]:
			var o: Dictionary = all.get(str(int(other)), {})
			if o.is_empty():
				continue
			if randf() < 0.2:
				factions[str(int(other))] = clampi(faction_aggro(int(other)) + int(pair[1]), -100, 100)
			message.emit("Your relationship with %s has %s" % [String(o.name), "decreased" if int(pair[1]) > 0 else "increased"], "system")


## Constitution with what deaths took off it (hit points use this).
func con_now() -> int:
	return int(effective_stats()[1])


## The sum of a bonus type (eProperty) on the worn items (Bonus1..10 / Bonus1Type..10Type).
func item_bonus(prop: int) -> int:
	var n := 0
	for s in EQUIP_SLOTS:
		var it := item_at(s)
		if it.is_empty():
			continue
		for i in range(1, 11):
			if int(it.get("bonus%dtype" % i, 0)) == prop:
				n += int(it.get("bonus%d" % i, 0))
	return n


## Stats the old server's way (StatCalculator): base + items (capped at L x 1.5) + single-stat buffs (base buff
## category, capped at L x 1.25) + dual-stat / acuity buffs (spec buff category, capped at L x 1.875); constitution
## lost at deaths comes off CON.
func effective_stats() -> Array:
	if stats.size() < 8:
		return [60, 60, 60, 60, 60, 60, 60, 60]                 # no character (map renders, tools)
	var out := []
	var base_buff: Array = buffs.get("base", [0, 0, 0, 0, 0, 0, 0, 0])
	var spec_buff: Array = buffs.get("spec", [0, 0, 0, 0, 0, 0, 0, 0])
	var mana := SpellRules.mana_stat(_class_id())
	for i in 8:
		var items := item_bonus(STAT_PROP[i]) + (item_bonus(156) if i == mana else 0)      # Acuity -> casting stat
		var sb := int(spec_buff[i]) + (int(buffs.get("acuity", 0)) if i == mana else 0)
		var buff := mini(int(base_buff[i]), int(level * 1.25)) + mini(sb, int(level * 1.875))
		var v := CombatRules.debuffed_stat(int(stats[i]) + mini(items, int(level * 1.5)), buff,
				int((buffs.get("debuff_base", [0, 0, 0, 0, 0, 0, 0, 0]) as Array)[i]), int((buffs.get("debuff_spec", [0, 0, 0, 0, 0, 0, 0, 0]) as Array)[i]))
		if i == 0 and bool(buffs.get("diseased", false)):
			v = int(v * 0.925)                                  # disease: 7.5% less strength
		if i == 1:
			v -= con_lost
		out.append(maxi(1, v))
	return out


## A stat with no buffs, debuffs or death losses: the base stat plus item bonuses (capped at L x 1.5) - the character
## sheet colours the value against it (red lowered, green raised).
func unmodified_stat(i: int) -> int:
	if stats.size() != 8:
		return 0
	var items := item_bonus(STAT_PROP[i]) + (item_bonus(156) if i == SpellRules.mana_stat(_class_id()) else 0)
	return int(stats[i]) + mini(items, int(level * 1.5))


## The player's resist to a damage type (ResistCalculator): items (capped at L / 2 + 1) + resist buffs (capped at 24)
## less debuffs (a net debuff counts half), then the race's resist; at most 70.
func resist(damage_type: int) -> int:
	var items := mini(item_bonus(int(CombatRules.RESIST_PROP.get(damage_type, 0))), level / 2 + 1)
	var buff := mini(int((buffs.get("resist", {}) as Dictionary).get(damage_type, 0)), 24) - int((buffs.get("resist_debuff", {}) as Dictionary).get(damage_type, 0))
	if buff < 0:
		buff /= 2
	return mini(items + buff + CombatRules.race_resist(_race_id(), damage_type), 70)


## Max hit points (MaxHealthCalculator: the class formula on effective CON, + item hit points capped at L x 4) and
## power; the bars follow.
func recalc_max() -> void:
	var e := effective_stats()
	max_hp = CombatRules.player_max_hp(_class_id(), level, int(e[1])) + mini(item_bonus(10), level * 4) + bonus_max_hp
	max_power = SpellRules.max_power(_class_id(), level, e)
	hp = mini(hp, max_hp)
	power_pts = mini(power_pts, max_power)
	_update_bars()


## Experience lost on death (old server GamePlayer.Die, level 6+): (50 - level)/1000 of this level's span (level 40+:
## 10/1000), a third of that on the first death of a level, two thirds on the second. Returns the loss.
func death_xp_loss() -> int:
	if level < int(CombatRules.r().get("death", {}).get("exp_loss_level", 6)):
		return 0
	var pct := (50 - level) if level < 40 else 10
	message.emit("You lose some experience!", "system")
	if death_count == 0:
		message.emit("This is your first death for this level.  Your experience and constitution losses are greatly reduced.", "system")
		pct /= 3
	elif death_count == 1:
		message.emit("This is your second death for this level.  Your experience and constitution losses are reduced.", "system")
		pct = pct * 2 / 3
	death_count += 1
	var span := CombatRules.xp_for_level(level + 1) - CombatRules.xp_for_level(level)
	var loss := span * pct / 1000
	xp = maxi(CombatRules.xp_for_level(level), xp - loss)
	_update_bars()
	save_character()
	return loss


func set_hp(v: int) -> void:
	hp = clampi(v, 0, max_hp)
	_update_bars()
	changed.emit()


func set_power(v: int) -> void:
	power_pts = clampi(v, 0, max_power)
	_update_bars()
	changed.emit()


func _update_bars() -> void:
	health = float(hp) / maxf(1.0, max_hp)
	power = float(power_pts) / maxf(1.0, max_power) if max_power > 0 else 0.0
	var lo := CombatRules.xp_for_level(level)
	var hi := CombatRules.xp_for_level(level + 1)
	exp_frac = clampf(float(xp - lo) / maxf(1.0, hi - lo), 0.0, 1.0)


## A kill's experience ("You get 80 experience points."), levelling as it crosses the table. A base class stops at
## level 5 (old server: "You cannot raise to the 6th level until you join an advanced guild!").
## multiply: the REALM OPTIONS rate applies (kills); not to experience given back (old server allowMultiply, /pray).
func gain_xp(amount: int, multiply := true) -> void:
	if amount <= 0:
		return
	if multiply:
		amount *= xp_rate()                                # REALM OPTIONS experience rate (old server default 1)
	message.emit("You get %d experience points." % amount, "combat")
	xp += amount
	while xp >= CombatRules.xp_for_level(level + 1):
		if level >= BASE_CLASS_CAP and bool(ClassDb.cls(_class_id()).get("is_base", true)):   # only a base class stops at 5
			xp = CombatRules.xp_for_level(level + 1) - 1
			message.emit("You cannot raise to the 6th level until you join an advanced guild!", "system")
			break
		level += 1
		death_count = 0                                    # old server: the death count starts over each level
		var gained := ClassDb.total_points(_class_id(), level) - ClassDb.total_points(_class_id(), level - 1)
		var old_max := max_hp
		_level_stats(level)
		recalc_max()
		hp = max_hp
		message.emit("You raise to level %d!" % level, "system")
		leveled_up.emit(level)
		message.emit("Your hits raise by %d points." % (max_hp - old_max), "system")
		if gained > 0:
			message.emit("You get %d more Specialization Points to spend at this level!" % gained, "system")
		for key in ClassDb.cls(_class_id()).get("autotrain", []):
			if level % 4 == 0 and specs.has(key):
				message.emit("You can now train %s to level %d for free." % [String(ClassDb.spec(String(key)).get("name", key)), level / 4], "system")
		_load_specs()
	_update_bars()
	changed.emit()
	save_character()


## The stat gains of reaching a level (GamePlayer.OnLevelUp, levels 6+): the class's primary stat +1 every level, the
## secondary every second level (6, 8, 10 ...), the tertiary every third (6, 9, 12 ...).
func _level_stats(l: int) -> void:
	if l < 6:
		return
	var c := ClassDb.cls(_class_id())
	var idx := {"STR": 0, "CON": 1, "DEX": 2, "QUI": 3, "INT": 4, "PIE": 5, "EMP": 6, "CHA": 7}
	var gains := [[String(c.get("primary", "")), true], [String(c.get("secondary", "")), (l - 6) % 2 == 0],
			[String(c.get("tertiary", "")), (l - 6) % 3 == 0]]
	for g in gains:
		if g[1] and idx.has(g[0]):
			stats[idx[g[0]]] = int(stats[idx[g[0]]]) + 1
	character["stats"] = stats.duplicate()


## Every spec the class knows at this level starts at 1; autotrain raises its specs for free.
func _load_specs() -> void:
	var saved = character.get("specs")
	if saved is Dictionary and specs.is_empty():
		for k in saved:
			specs[String(k)] = int(saved[k])
	var cid := _class_id()
	for e in ClassDb.trainable_specs(cid, level):
		var key := String(e.spec)
		specs[key] = maxi(maxi(int(specs.get(key, 1)), ClassDb.autotrain_level(cid, key, level)), 1)
	skills_changed.emit()


func spec_points() -> int:
	return ClassDb.total_points(_class_id(), level) - ClassDb.points_used(_class_id(), level, specs)


## Train one level of a spec at a trainer: costs (current level + 1) points; a spec can't pass the character level.
func train(key: String) -> bool:
	if not specs.has(key):
		return false
	var cost := int(specs[key]) + 1
	var name := String(ClassDb.spec(key).get("name", key))
	if int(specs[key]) >= level:
		message.emit("You can't train %s any higher until you gain a level." % name, "error")
		return false
	if spec_points() < cost:
		message.emit("You don't have enough specialization points to train %s." % name, "error")
		return false
	specs[key] = int(specs[key]) + 1
	message.emit("You train %s to %d." % [name, int(specs[key])], "system")
	skills_changed.emit()
	save_character()
	return true


## Joining an advanced class at its trainer (old server GameTrainer.PromotePlayer).
func promote(class_id: int) -> void:
	var c := ClassDb.cls(class_id)
	character["class_id"] = class_id
	character["class_name"] = String(c.get("name", ""))
	message.emit("You have been accepted by the %s!" % String(c.get("profession", c.get("name", ""))), "system")
	message.emit("You have been upgraded to the %s class!" % String(c.get("name", "")), "system")
	_load_specs()
	save_character()
	changed.emit()


## The character's realm number (eRealm: Albion 1, Midgard 2, Hibernia 3).
func realm_id() -> int:
	return int({"alb": 1, "mid": 2, "hib": 3}.get(String(character.get("realm", "hib")), 3))


## "alb" / "mid" / "hib" (bots compare realms with it: friendly or enemy nameplates).
func realm_key() -> String:
	return String(character.get("realm", "hib"))


func display_name() -> String:
	return String(character.get("name", "Adventurer"))


func class_title() -> String:
	return String(character.get("class_name", ""))


## What the character carries, in pounds: worn items and the backpack, weight x count (arrows / bolts / poisons
## half), / 10 (GamePlayerInventory.UpdateInventoryWeight).
func inventory_weight() -> int:
	var w := 0
	for slot in inventory:
		if not (is_backpack(int(slot)) or (int(slot) >= 10 and int(slot) <= 37)):
			continue
		var e: Dictionary = inventory[slot]
		var it := ItemDb.item(String(e.get("id", "")))
		var n := int(e.get("count", 1))
		w += int(it.get("weight", 0)) * n / (2 if int(it.get("object_type", 0)) in [43, 44, 46] else 1)
	return w / 10


## The speed share left: over the carrying capacity (strength, never below the unbuffed value) the speed drops by
## (weight - capacity) / (35% of capacity), to standing still. Says so when it changes.
func encumbrance_mult() -> float:
	if stats.size() < 8:
		return 1.0                                          # no character loaded yet
	var cap := maxi(int(effective_stats()[0]), int(stats[0]))
	var w := inventory_weight()
	var now := w > cap
	var m := 1.0
	if now:
		m = maxf(0.0, 1.0 - (w - cap) / (cap * 0.35))
	if now and not encumbered:
		message.emit("You are heavily encumbered and cannot move." if m <= 0.0 else "You are encumbered and move more slowly.", "important")
	encumbered = now
	return m


func is_equip_slot(slot: int) -> bool:
	return slot in EQUIP_SLOTS


func is_backpack(slot: int) -> bool:
	return slot >= BACKPACK and slot <= BACKPACK_END


func is_vault(slot: int) -> bool:
	return slot >= FIRST_VAULT and slot < FIRST_VAULT + VAULT_SLOTS


func first_free_vault() -> int:
	for s in range(FIRST_VAULT, FIRST_VAULT + VAULT_SLOTS):
		if not inventory.has(s):
			return s
	return -1


func entry(slot: int) -> Dictionary:
	return inventory.get(slot, {})


func item_at(slot: int) -> Dictionary:
	var e := entry(slot)
	if e.is_empty():
		return {}
	var it := ItemDb.item(String(e.id))
	if e.has("condition") or e.has("durability"):              # this item's own wear (templates start new)
		it = it.duplicate()
		it["condition"] = int(e.get("condition", it.get("condition", 1)))
		it["durability"] = int(e.get("durability", it.get("durability", 1)))
	return it


## What a smith charges: (max condition - condition) x price / max condition (old server DbInventoryItem.RepairCost).
func repair_cost(slot: int) -> int:
	var it := item_at(slot)
	var max_c := maxi(1, int(it.get("maxcondition", 1)))
	return (max_c - int(it.get("condition", max_c))) * int(it.get("price", 0)) / max_c


## A smith's repair: back toward full condition, paid for out of durability (Repair.ModifyConditionAndDurability).
## Returns false if it can't be repaired (no durability left).
func repair(slot: int) -> bool:
	var e := entry(slot)
	var it := item_at(slot)
	var max_c := maxi(1, int(it.get("maxcondition", 1)))
	var amount := mini(max_c - int(it.get("condition", max_c)), int(it.get("durability", 0)))
	if amount <= 0:
		return false
	e["condition"] = int(it.get("condition", max_c)) + amount
	e["durability"] = int(it.get("durability", 0)) - amount
	_changed()
	return true


## The equipment slots an item can be worn in, preferred first (the client's Item_Type + Hand rules: right-hand
## weapons only right, left-hand-capable weapons either hand, shields only left, rings / wrists either side).
func slots_for(it: Dictionary) -> Array:
	if it.is_empty():
		return []
	var t := int(it.get("item_type", 0))
	match t:
		10: return [10]
		11: return [11] if int(it.get("object_type", 0)) == SHIELD else [10, 11]
		12, 13, 21, 22, 23, 24, 25, 26, 27, 28, 29, 32, 37: return [t]
		33, 34: return [33, 34]
		35, 36: return [35, 36]
	return []


func can_hold(slot: int, it: Dictionary) -> bool:
	if it.is_empty() or is_backpack(slot) or is_vault(slot):
		return true
	return slot in slots_for(it)


## Move the item in `from` to `to`: into an empty slot, onto the same stackable item (merged), or swapped with what
## is there when that item may go back into `from`. Returns false (and says why) when the move isn't allowed.
func move(from: int, to: int) -> bool:
	if from == to or not inventory.has(from):
		return false
	var a := entry(from)
	var ia := ItemDb.item(String(a.id))
	if not can_hold(to, ia):
		message.emit("You can't put the %s there." % ia.get("name", "item"), "error")
		return false
	if is_equip_slot(to) and not can_use(ia):
		message.emit(use_problem(ia), "error")
		return false
	if inventory.has(to):
		var b := entry(to)
		var ib := ItemDb.item(String(b.id))
		var stack := int(ia.get("maxcount", 1))
		if String(b.id) == String(a.id) and stack > 1 and int(b.count) < stack:
			var n := mini(stack - int(b.count), int(a.count))
			b.count = int(b.count) + n
			a.count = int(a.count) - n
			if int(a.count) <= 0:
				inventory.erase(from)
			_changed()
			return true
		if not can_hold(from, ib):
			message.emit("You can't swap the %s there." % ib.get("name", "item"), "error")
			return false
		if is_equip_slot(from) and not can_use(ib):
			message.emit(use_problem(ib), "error")
			return false
		inventory[from] = b
	else:
		inventory.erase(from)
	inventory[to] = a
	if is_equip_slot(to):
		message.emit("You ready the %s." % ia.get("name", "item"), "system")
	_changed()
	return true


## Wear the item in `from` (double-click / use): an empty matching slot first, else swap with the first one.
func equip(from: int) -> bool:
	var it := item_at(from)
	var slots := slots_for(it)
	if slots.is_empty():
		message.emit("You can't equip that.", "error")
		return false
	for s in slots:
		if not inventory.has(s):
			return move(from, s)
	return move(from, slots[0])


## Take off the item in an equipment slot into the first free backpack slot.
func unequip(slot: int) -> bool:
	var free := first_free()
	if free < 0:
		message.emit("Your backpack is full.", "error")
		return false
	var it := item_at(slot)
	inventory[free] = inventory[slot]
	inventory.erase(slot)
	message.emit("You put the %s in your backpack." % it.get("name", "item"), "system")
	_changed()
	return true


## The first slot (backpack or worn) holding an item, or -1 (DataQuest's GetFirstItemByID over the inventory).
func find_item(id: String) -> int:
	for s in inventory:
		if String(inventory[s].id) == id and (is_backpack(s) or is_equip_slot(s)):
			return s
	return -1


## Put one of an item in the first free backpack slot; false when the backpack is full ("Your backpack is full!").
func give_item(id: String, from := "", quiet := false) -> bool:
	var s := first_free()
	var it := ItemDb.item(id)
	var variants: Dictionary = it.get("variants", {})
	if not variants.is_empty():                             # a reward made per class ("Banba's Cloak for <class>"):
		id = String(variants.get(str(_class_id()), variants.values()[0]))   # yours, as the quest gave it
		it = ItemDb.item(id)
	if s < 0 or it.is_empty():
		if s < 0 and not quiet:
			message.emit("Your backpack is full!", "system")
		return false
	inventory[s] = {"id": id, "count": 1}
	if quiet:
		pass
	elif from != "":
		message.emit("You receive %s from %s!" % [String(it.get("name", id)), from], "system")
	else:
		message.emit("You receive %s!" % String(it.get("name", id)), "system")
	_changed()
	return true


## Take one of the item in a slot (a stack loses one).
func take_one(slot: int) -> void:
	if not inventory.has(slot):
		return
	var e: Dictionary = inventory[slot]
	if int(e.get("count", 1)) > 1:
		e.count = int(e.count) - 1
	else:
		inventory.erase(slot)
	_changed()


func free_slots() -> int:
	var n := 0
	for s in range(BACKPACK, BACKPACK_END + 1):
		if not inventory.has(s):
			n += 1
	return n


func first_free() -> int:
	for s in range(BACKPACK, BACKPACK_END + 1):
		if not inventory.has(s):
			return s
	return -1


func _to_backpack(e: Dictionary) -> bool:
	var s := first_free()
	if s < 0:
		return false
	inventory[s] = e
	return true


func set_active_weapon(which: String) -> void:
	var slot: int = {"standard": 10, "twohand": 12, "ranged": 13}.get(which, 10)
	if not inventory.has(slot) and not (which == "standard" and inventory.has(11)):
		message.emit("You have no weapon of that kind ready.", "error")
		return
	active_weapon = which
	message.emit("You switch to your %s weapon." % {"standard": "standard", "twohand": "two handed",
			"ranged": "ranged"}[which], "system")
	weapon_switched.emit()                                 # the server's SwitchWeapon: stops the attack, cancels a cast
	_changed()


func total_copper() -> int:
	return int(money.copper) + int(money.silver) * 100 + int(money.gold) * 10000 + int(money.platinum) * 10000000 \
			+ int(money.mithril) * 10000000000


func split_money(c: int) -> Dictionary:
	return {"copper": c % 100, "silver": c / 100 % 100, "gold": c / 10000 % 1000, "platinum": c / 10000000 % 1000,
			"mithril": c / 10000000000}


func set_copper(c: int) -> void:
	money = split_money(maxi(c, 0))
	changed.emit()


## "1 gold, 5 silver, 3 copper" (the old server's Money.GetString).
func money_string(c: int) -> String:
	if c <= 0:
		return "0 copper pieces"
	var p := split_money(c)
	var parts := []
	for k in ["mithril", "platinum", "gold", "silver", "copper"]:
		if int(p[k]) > 0:
			parts.append("%d %s" % [p[k], k])
	return ", ".join(parts)


func buy(id: String, merchant: String) -> bool:
	var it := ItemDb.item(id)
	var price := int(it.get("price", 0))
	if price > total_copper():
		message.emit("You need %s to buy this." % money_string(price), "merchant")
		return false
	var free := first_free()
	if free < 0:
		message.emit("Not enough inventory space to buy that.", "merchant")
		return false
	inventory[free] = {"id": id, "count": maxi(1, int(it.get("packsize", 1)))}
	set_copper(total_copper() - price)
	message.emit("You just bought %s for %s." % [String(it.get("name", "item")), money_string(price)], "merchant")
	_changed()
	bought.emit(id)
	return true


func sell(slot: int, merchant: String) -> bool:
	var e := entry(slot)
	if e.is_empty():
		return false
	var it := ItemDb.item(String(e.id))
	if int(it.get("isdropable", 1)) == 0 or int(it.get("price", 0)) <= 0:
		message.emit("%s isn't interested in %s." % [merchant, String(it.get("name", "that"))], "merchant")
		return false
	var pack := maxi(1, int(it.get("packsize", 1)))
	var value := int(it.get("price", 0)) * int(e.get("count", 1)) / pack * SELL_RATIO / 100
	inventory.erase(slot)
	_item_gone(slot)
	set_copper(total_copper() + value)
	message.emit("%s gives you %s for %s." % [merchant, money_string(value), String(it.get("name", "item"))], "merchant")
	_changed()
	return true


## APPRAISE in the merchant window: what the merchant would pay for the item in a slot, said in chat (the old server's
## GameMerchant.OnPlayerAppraise: "{merchant} offers you {price} for {item}." / "... isn't interested in ...").
func appraise(slot: int, merchant: String) -> int:
	var e := entry(slot)
	if e.is_empty():
		return 0
	var it := ItemDb.item(String(e.id))
	var pack := maxi(1, int(it.get("packsize", 1)))
	var value := int(it.get("price", 0)) * int(e.get("count", 1)) / pack * SELL_RATIO / 100
	if int(it.get("isdropable", 1)) == 0 or value <= 0:
		message.emit("%s isn't interested in %s." % [merchant, String(it.get("name", "that"))], "merchant")
		return 0
	message.emit("%s offers you %s for %s." % [merchant, money_string(value), String(it.get("name", "item"))], "merchant")
	return value


func money_text() -> String:
	var parts := []
	for k in ["mithril", "platinum", "gold", "silver", "copper"]:
		if int(money[k]) > 0:
			parts.append("%d %s" % [money[k], k])
	return ", ".join(parts) if parts.size() > 0 else "no money"


func _changed() -> void:
	save_character()
	inventory_changed.emit()
