extends CharacterState
## Autoload "PlayerData": the character the UI shows (name, race, class, level, health / endurance / power,
## experience, realm points, money, quickbar contents, inventory). Filled from the character picked on the select
## screen (CharacterStore.selected). Stats and combat values arrive with the character systems; until then the sheet
## shows what is known and "-" for the rest - nothing made up.
##
## Inventory uses the client's slot numbers (the server's eInventorySlot), 1.65 slots only (UI BUILD PLAN.md: no
## mythical slot - 1.97):
##   10 right hand, 11 left hand, 12 two-handed, 13 ranged, 21 helm, 22 hands, 23 feet, 24 jewelry, 25 torso,
##   26 cloak, 27 legs, 28 arms (sleeves), 29 neck, 32 waist, 33 / 34 left / right wrist, 35 / 36 left / right ring,
##   40-79 the backpack (5 sacks of 8).
## As in the client, the right hand, left hand, two-handed and ranged slots are all worn at once and the active
## weapon set (standard / two-handed / ranged) is switched with the weapon keys.

signal target_changed
signal task_command(args: String)          # /task [status | abort] (TaskSystem)
signal pet_changed
signal skill_info(entry: Dictionary)                # right-click a spell / style / ability: its delve text
signal delve(item: Dictionary)                      # Shift+I: the summary (delve) in the information window
signal summary(item: Dictionary)                    # right-click on an item: its standard information there
signal release_requested                            # /release after death
signal release_to(kind: String)                     # the death window's /RELEASE CITY, HOUSE, BIND ("" = /release)
signal pray_requested                               # /pray at the gravestone
signal group_changed                                # members joined / left (the group windows)
signal member_clicked(row: Dictionary)              # a group member's row clicked (the group page, the mini group)
signal disband_requested                            # /disband, the group page's DISBAND
signal window_toggle(id: String)                    # a button that opens / closes a window (GameUI)
signal bind_requested                             # /bind
signal dismount_requested                         # /dismount, jump while riding
signal emote_requested(command: String)           # /wave, /bow ... (Combat.emote)
signal track_requested(mode: String)              # /follow, /stick, /face (your target)
signal invite_requested(who: String)              # /invite [name]
signal attack_toggle_requested                       # the target window's ATTACK (attack mode on / off)
signal sit_requested(on: bool)                      # /sit, /stand, the Sit/Stand key
signal release_timer(seconds: float)                # dead: seconds to the automatic release (-1 = released)

const BANKS := 10
const SLOTS := 10

signal use_skill(entry: Dictionary)      # a spell / style / ability used (quickbar or list): Combat / Casting act
signal effects_changed                   # buffs / debuffs on the player changed (the effects bar)
var quickbars := []                      # [bar][bank][slot] = {kind, id, icon, name} or {}
var quickbar_bank := [0, 0, 0]
signal quests_changed
signal item_used(id: String)                        # an item used (U / quickbar): quest use_item steps
signal search_requested                             # /search
signal said(text: String)                           # /say (and plain chat): quest words of power
var selected_slot := -1                  # the item picked with a right-click (the client's way: then Shift+I = delve)
var delve_id := ""                       # a merchant's item picked with a right-click (Shift+I shows it)
var target := {}                         # {name, con_color, health 0..1} - set by the targeting system
var pet := {}                            # {name, health, mode} - set when a pet is summoned (the pet window opens)


func _ready() -> void:
	quickbars = _empty_quickbars()


## /macro <name> <command> (a client command, as the old client's): the macro goes in the first empty quickbar slot
## (quickbar 1, 2, 3 on their shown pages) with its name written in the slot; using it types its command (macro_run).
## Returns false when every shown slot is taken.
signal macro_run(text: String)
const MACRO_NAME_MAX := 14


func add_macro(name: String, text: String) -> bool:
	for b in 3:
		var slots: Array = quickbars[b][quickbar_bank[b]]
		for i in slots.size():
			if (slots[i] as Dictionary).is_empty():
				slots[i] = {"kind": "macro", "id": text, "icon": -1, "name": name.substr(0, MACRO_NAME_MAX)}
				save_character()
				changed.emit()                                 # the quickbars redraw
				return true
	return false


func _empty_quickbars() -> Array:
	var bars := []
	for b in 3:
		var banks := []
		for k in BANKS:
			var slots := []
			for s in SLOTS:
				slots.append({})
			banks.append(slots)
		bars.append(banks)
	return bars


func load_character(c: Dictionary) -> void:
	_vault_loaded = false                                  # never write the shared vault before it's read (starter gear saves)
	var saved = c.get("quickbars")
	if Server.active:                                      # this PC's quickbars for a server character
		var path: String = Server.quickbars_path(String(c.get("name", "")))
		var q = JSON.parse_string(FileAccess.get_file_as_string(path)) if FileAccess.file_exists(path) else null
		saved = q.get("quickbars") if q is Dictionary else null
		c["quickbar_bank"] = q.get("bank") if q is Dictionary else null
	quickbars = saved if saved is Array and saved.size() == 3 else _empty_quickbars()
	var banks = c.get("quickbar_bank")                     # patch 1.54: the quickbar page persists across quitting
	quickbar_bank = (banks as Array).map(func(x): return int(x)) if banks is Array and banks.size() == 3 else [0, 0, 0]
	load_sheet(c)
	_load_vault()
	changed.emit()
	inventory_changed.emit()


func _loaded() -> void:
	Keybinds.use_character(character)                      # shared keys, or this character's own


func save_character() -> void:
	if Server.active:                                      # the server's game: the server keeps the character - only
		_save_server_quickbars()                           # the quickbars stay on this PC, as the old client's did
		return
	if character.is_empty():
		return
	character["quickbars"] = quickbars
	character["quickbar_bank"] = quickbar_bank
	store_sheet()
	CharacterStore.update(Accounts.account, character)
	_save_vault()


# --- the vault: account wide, realm specific (CUSTOM - see "1.65 EXCEPTIONS MADE.txt") ------------------------------
# 1.65 had only each character's own Storage Vault (v1.27, Oct 2001: objects only, no money). Owner 2026-10-09: the
# vault is shared by every character of the account IN THE SAME REALM, and holds coins too (deposited / withdrawn by
# hand - coins on characters stay per character). Stored in user://vaults/<account>_<realm>.json:
#   {"items": {slot: entry}, "copper": n}; slots are the same FirstVault 110 .. 149 the vault window shows.
const VAULT_DIR := "user://vaults"
var vault_copper := 0
var _vault_loaded := false


func _vault_path() -> String:
	var acc := (Accounts.account if Accounts.account != "" else "local").to_lower()
	return "%s/%s_%s.json" % [VAULT_DIR, acc, String(character.get("realm", "hib"))]


## The shared vault into the vault slots; a character's own old vault items (saved before the vault was shared) move
## into it once - into free vault slots, else the backpack - and nothing is lost or doubled.
func _load_vault() -> void:
	var legacy := []
	for s in range(FIRST_VAULT, FIRST_VAULT + VAULT_SLOTS):
		if inventory.has(s):
			legacy.append(inventory[s])
			inventory.erase(s)
	var parsed = JSON.parse_string(FileAccess.get_file_as_string(_vault_path())) 			if FileAccess.file_exists(_vault_path()) else null
	var data: Dictionary = parsed if parsed is Dictionary else {}
	var items: Dictionary = data.get("items", {}) if data.get("items") is Dictionary else {}
	for k in items:
		if is_vault(int(k)):
			inventory[int(k)] = items[k]
	vault_copper = int(data.get("copper", 0))
	_vault_loaded = true
	var pending: Array = character.get("vault_pending", []) if character.get("vault_pending") is Array else []
	legacy.append_array(pending)                           # (items that found no room last time)
	if not legacy.is_empty():
		var left := []
		for e in legacy:
			var to := first_free_vault()
			if to < 0:
				to = first_free()
			if to >= 0:
				inventory[to] = e
			else:
				left.append(e)                             # no room anywhere: kept on the character, never lost
		if left.is_empty():
			character.erase("vault_pending")
		else:
			character["vault_pending"] = left
			message.emit("%d of your old vault items found no room - make space and log in again." % left.size(), "error")
		save_character()                                   # writes both: the character without, the vault with them


func _save_vault() -> void:
	if character.is_empty() or not _vault_loaded:
		return
	var items := {}
	for s in range(FIRST_VAULT, FIRST_VAULT + VAULT_SLOTS):
		if inventory.has(s):
			items[str(s)] = inventory[s]
	DirAccess.make_dir_recursive_absolute(VAULT_DIR)
	var f := FileAccess.open(_vault_path(), FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify({"items": items, "copper": vault_copper, "account": Accounts.account,
				"realm": String(character.get("realm", "hib"))}, " "))


## Coins into / out of the vault (by hand, at a vault keeper). False when you or the vault haven't that much.
func vault_deposit(c: int) -> bool:
	if c <= 0 or c > total_copper():
		return false
	set_copper(total_copper() - c)
	vault_copper += c
	message.emit("You deposit %s into your vault." % money_string(c), "system")
	save_character()
	inventory_changed.emit()
	return true


func vault_withdraw(c: int) -> bool:
	if c <= 0 or c > vault_copper:
		return false
	vault_copper -= c
	set_copper(total_copper() + c)
	message.emit("You withdraw %s from your vault." % money_string(c), "system")
	save_character()
	inventory_changed.emit()
	return true



# --- the group (period Warcry interface guide 2004: the group page lists every member - leader first - with name,
# class and level, exact health and power, and their enchantments; the mini window health and a power gem). The
# other members are added by whoever brings them (gamebots / companions); alone you are in no group.
var group: Array = []                               # the OTHER members: {name, class_name, level, hp, power, end, effects}
var self_effects := Callable()                      # your effects for the group windows (GameUI: Casting.player_effects)


func in_group() -> bool:
	return not group.is_empty()


## Every member's row, you first, with live values: {name, class_name, level, hp, hp_pts, power, power_pts, end,
## effects: [{icon, border, name, seconds}]}.
func group_rows() -> Array:
	if group.is_empty():
		return []
	var me := {"name": display_name(), "class_name": String(ClassDb.cls(_class_id()).get("name", "")), "level": level,
			"hp": float(hp) / maxf(1.0, float(max_hp)), "hp_pts": hp,
			"power": float(power_pts) / maxf(1.0, float(max_power)) if max_power > 0 else 0.0, "power_pts": power_pts,
			"end": float(end_pts) / float(MAX_END), "effects": self_effects.call() if self_effects.is_valid() else [], "me": true}
	return [me] + group


## Connect a handler to one of PlayerData's signals for as long as owner is in the tree (the autoload outlives every
## zone scene; a lambda left connected after its scene is gone would run against freed nodes).
func hook(owner: Node, sig: Signal, c: Callable) -> void:
	sig.connect(c)
	owner.tree_exiting.connect(func():
		if sig.is_connected(c):
			sig.disconnect(c), CONNECT_ONE_SHOT)



# --- item condition (owner 2026-10-09: real 1.65 condition for the player; never for bots) ------------------------

## A strike with a weapon, or a blow taken on an armour piece: the old server's GameInventoryItem.OnStrikeTarget /
## OnStruckByEnemy - while above 70%, a 5% chance (item_condition_loss_chance) to lose (item con + 4) points, twice
## that under 91%; "could use repairs" at 90%, "in need of repairs" at 80%, "in dire need of repairs" at 70%.
func wear_item(slot: int) -> void:
	var e := entry(slot)
	if e.is_empty():
		return
	var it := item_at(slot)
	var max_c := maxi(1, int(it.get("maxcondition", 1)))
	var cond := int(it.get("condition", max_c))
	var old_pct := cond * 100 / max_c
	if old_pct <= 70 or randf() >= 0.05:
		return
	var sub := ItemTooltip.con_value(int(it.get("level", 0)), level) + 4
	if old_pct < 91:
		sub *= 2
	cond = maxi(0, cond - sub)
	e["condition"] = cond
	var pct := cond * 100 / max_c
	if pct != old_pct:
		var n := String(it.get("name", "item"))
		match pct:
			90: message.emit("Your %s could use repairs." % n, "system")
			80: message.emit("Your %s is in need of repairs." % n, "system")
			70: message.emit("Your %s is in dire need of repairs." % n, "system")
		_changed()


## Quickbar "use" of an item: equipment in the backpack is worn; worn equipment does nothing (as in the client).
func _save_server_quickbars() -> void:
	if character.is_empty():
		return
	DirAccess.make_dir_recursive_absolute("user://server_quickbars")
	var f := FileAccess.open(Server.quickbars_path(String(character.get("name", ""))), FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify({"quickbars": quickbars, "bank": quickbar_bank}))


# --- the server's game: item actions go to the server (its inventory packets bring the result back) -----------------

func move(from: int, to: int) -> bool:
	if not Server.active:
		return super.move(from, to)
	# the Realm Exchange: a result dragged into the backpack is bought, a listing dragged out is taken back (the
	# server's moves out of the market / the listing chest - Server's exchange section)
	if from >= Server.MARKET_FIRST and from < Server.MARKET_FIRST + 20:
		return is_backpack(to) and not inventory.has(to) and Server.exchange_buy(from - Server.MARKET_FIRST, to)
	if from >= Server.CHEST_FIRST and from <= Server.CHEST_LAST and Server.container_mode != "account":
		if not is_backpack(to) or inventory.has(to) or not Server.listings.has(from):
			return false
		Server.client.call("MoveItem", from, to, int(ItemDb.item(String(Server.listings[from])).get("count", 1)))
		return true
	if from == to or not inventory.has(from):
		return false
	Server.client.call("MoveItem", from, to, int(entry(from).get("count", 1)))
	return true


func equip(from: int) -> bool:
	if not Server.active:
		return super.equip(from)
	var it := item_at(from)
	var slots := slots_for(it)
	if slots.is_empty():
		message.emit("You can't equip that.", "error")
		return false
	for sl in slots:
		if not inventory.has(sl):
			return move(from, sl)
	return move(from, slots[0])


func unequip(slot: int) -> bool:
	if not Server.active:
		return super.unequip(slot)
	var free := first_free()
	if free < 0:
		message.emit("Your backpack is full.", "error")
		return false
	return move(slot, free)


func sell(slot: int, merchant: String) -> bool:
	if not Server.active:
		return super.sell(slot, merchant)
	var v: Node = Server.viewer()
	if v == null:
		return false
	var r: Vector3 = Server.to_region(Server.current_zone_id(), v.player.global_position)
	Server.client.call("SellItem", int(r.x), int(r.y), slot)
	return true


func appraise(slot: int, merchant: String) -> int:
	if not Server.active:
		return super.appraise(slot, merchant)
	var v: Node = Server.viewer()
	if v == null:
		return 0
	var r: Vector3 = Server.to_region(Server.current_zone_id(), v.player.global_position)
	Server.client.call("AppraiseItem", int(r.x), int(r.y), slot)   # (the server answers in chat)
	return 0


func set_active_weapon(which: String) -> void:
	if not Server.active:
		super.set_active_weapon(which)
		return
	var v: Node = Server.viewer()
	if v:
		Server.switch_weapon(which, v.player, Server.current_zone_id())


func spec_points() -> int:
	if Server.active:
		return int(character.get("spec_points", 0))
	return super.spec_points()


func use_item(id: String) -> void:
	if Server.active:
		Server.use(id)
		return
	for s in inventory:
		if String(inventory[s].id) == id:
			if is_backpack(s) and not slots_for(ItemDb.item(id)).is_empty():
				equip(s)
			elif is_backpack(s):
				item_used.emit(id)
				if not quests.is_empty():
					return
				message.emit("You can't use the %s." % ItemDb.item(id).get("name", "item"), "error")
			return
	message.emit("You don't have that item.", "error")


func _item_gone(slot: int) -> void:
	if selected_slot == slot:
		selected_slot = -1


## Right-click on an item selects it (period: "if you right click an item, type Shift-I to see the properties").
func select_slot(slot: int) -> void:
	selected_slot = slot if inventory.has(slot) else -1
	inventory_changed.emit()
	if selected_slot >= 0:
		summary.emit(item_at(slot))


func set_target(t: Dictionary) -> void:
	target = t
	target_changed.emit()


func set_pet(p: Dictionary) -> void:
	pet = p
	pet_changed.emit()


## The pet window's commands; the pet AI listens to this when pets exist.
signal pet_order(command: String)


func pet_command(command: String) -> void:
	if pet.is_empty():
		message.emit("You don't have a pet.", "error")
		return
	if command in ["aggressive", "defensive", "passive"]:
		pet["mode"] = command
	pet_order.emit(command)
