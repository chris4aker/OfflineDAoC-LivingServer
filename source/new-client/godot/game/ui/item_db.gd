class_name ItemDb
extends RefCounted
## Items and icons for the in-game UI (pipeline/build_items.py -> content/items/items.json, content/ui/icons).
## Icons use the client's global icon numbers: one 10x10 sheet of 32 px icons per hundred (SPL_0 ... SPL_1800, the
## client's default top-level icon set, as the 0.35 OfflineDAoC uses). Items carry their icon in that numbering.

const ITEMS := "res://content/items/items.json"
const ICONS := "res://content/ui/icons"
const DAMAGE := {1: "Crush", 2: "Slash", 3: "Thrust"}
const OBJECT_TYPES := {1: "Generic Weapon", 2: "Crushing", 3: "Slashing", 4: "Thrusting", 8: "Staff", 9: "Longbow",
		11: "Sword", 12: "Hammer", 13: "Axe", 15: "Composite Bow", 18: "Recurve Bow", 19: "Blades", 20: "Blunt",
		21: "Piercing", 32: "Cloth", 33: "Leather", 34: "Studded", 35: "Chain", 36: "Plate", 37: "Reinforced",
		38: "Scale", 41: "Magical", 42: "Shield", 43: "Arrow", 44: "Bolt", 46: "Poison", 16: "Thrown"}

static var _data := {}
static var _shops := {}
static var _sheets := {}
static var _icons := {}


static func _load() -> void:
	if _data.is_empty():
		var parsed = JSON.parse_string(FileAccess.get_file_as_string(ITEMS))
		_data = parsed if parsed is Dictionary else {"items": {}, "starter": {}, "sheets": {}}


static var _runtime := {}                   # items made in play (a task's delivery item: TaskSystem)


static func add_runtime(id: String, it: Dictionary) -> void:
	_runtime[id] = it


static func item(id: String) -> Dictionary:
	_load()
	if _runtime.has(id):
		return _runtime[id]
	if _data.items.has(id):
		return _data.items[id]
	_load_shops()
	var shop: Dictionary = _shops.get("items", {}).get(id, {})
	if not shop.is_empty():
		return shop
	var q := QuestDb.item(id)
	return q if not q.is_empty() else Loot.item(id)       # monster drops (content/loot/loot.json)


static func _load_shops() -> void:
	if _shops.is_empty():
		var parsed = JSON.parse_string(FileAccess.get_file_as_string("res://content/items/merchants.json"))
		_shops = parsed if parsed is Dictionary else {"lists": {}, "items": {}}


## A merchant's stock (pipeline/build_merchants.py): [[page, slot, item id], ...].
static func merchant_list(list_id: String) -> Array:
	_load_shops()
	return _shops.get("lists", {}).get(list_id, [])


## Every merchant item id (the companions' generic weapon pick).
static func all_merchant_items() -> Array:
	_load_shops()
	return _shops.get("items", {}).keys()


static func starter(class_id: int) -> Array:
	_load()
	return _data.starter.get(str(class_id), [])


## The 32 px icon with the client's global number, or null.
static func icon(n: int) -> Texture2D:
	if n < 0:
		return null
	if _icons.has(n):
		return _icons[n]
	_load()
	var base := n / 100 * 100
	var sheet := _sheet(String(_data.sheets.get(str(base), "")))
	var t: AtlasTexture = null
	if sheet:
		var k := sheet.get_width() / 320.0
		var i := n % 100
		t = AtlasTexture.new()
		t.atlas = sheet
		t.region = Rect2((i % 10) * 32 * k, (i / 10) * 32 * k, 32 * k, 32 * k)
		t.filter_clip = true
	_icons[n] = t
	return t


## An icon border overlay (icons.csv borderId: spells / styles), from brdr_0.bmp (10 x 2 cells of 32 px), or null.
static func border(n: int) -> Texture2D:
	if n < 0:
		return null
	var key := -1000 - n
	if _icons.has(key):
		return _icons[key]
	var sheet := _sheet("BRDR_0")
	var t: AtlasTexture = null
	if sheet:
		var k := sheet.get_width() / 320.0
		t = AtlasTexture.new()
		t.atlas = sheet
		t.region = Rect2((n % 10) * 32 * k, (n / 10) * 32 * k, 32 * k, 32 * k)
		t.filter_clip = true
	_icons[key] = t
	return t


static func _sheet(name: String) -> Texture2D:
	if name == "":
		return null
	if not _sheets.has(name):
		var img := Image.load_from_file(ProjectSettings.globalize_path("%s/x4plus/%s.png" % [ICONS, name]))
		if img == null:
			img = Image.load_from_file(ProjectSettings.globalize_path("%s/original/%s.png" % [ICONS, name]))
		if img:
			img.generate_mipmaps()
		_sheets[name] = ImageTexture.create_from_image(img) if img else null
	return _sheets[name]


## Right-click on an item: its standard information in the information window (patch notes 2001-10-19: "Right click
## on an item to see its standard information in the small object window, and then use the Delve key (default
## shift-I) to see summary information"). The lines follow the client's right-click mini delve (owner's screenshots:
## "Cloth Armor / Factor: 38 / Absorb: 0% / Con: 100%  Dur: 100% / Qua: 95%  Bon: 20%", weapons "Staff Weapon /
## Crush Damage / Dps: 16.5  Spd: 4.6 / ... / Two-Handed"). Hovering only names the item.
static func summary(it: Dictionary) -> Array:
	if it.is_empty():
		return []
	var out := []
	var ot := int(it.get("object_type", 0))
	var kind: String = OBJECT_TYPES.get(ot, "")
	var cd := "CON: %d%%   DUR: %d%%" % [pct(it, "condition"), pct(it, "durability")]
	var qb := "QUA: %d%%   BON: %d%%" % [int(it.get("quality", 85)), int(it.get("bonus", 0))]
	# a merchant's packs (arrows, bolts, poisons, thrown weapons, general goods): how many one purchase gives - the server's
	# merchant list sends the pack size (owner 2026-10-30: "no indication on how many i get in a stack")
	var pack := int(it.get("pack", 0))
	var pack_line := ("%d PER PURCHASE" % pack) if pack > 1 else ""
	if ot == 43 or ot == 44:
		# arrows / bolts, as the original client builds them from the merchant list itself (game.dll 0x440E7E,
		# original-client chat 2026-10-30): the count, the damage type, the weight, and the three grades in SPD_ABS -
		# damage bits 0-1, range 2-3, accuracy 4-5 (the server's AttackComponent: -15% / normal / +15% / +25%)
		var n := int(it.get("count", 0)) if int(it.get("count", 0)) > 0 else pack
		var word := ("BOLT" if ot == 44 else "ARROW") + ("S" if n != 1 else "")
		out.append(("%d %s" % [n, word]) if n > 0 else word)
		if DAMAGE.has(int(it.get("type_damage", 0))):
			out.append(("%s DAMAGE" % DAMAGE[int(it.type_damage)]).to_upper())
		if int(it.get("weight", 0)) > 0:
			out.append("WEIGHT: %.1f LBS" % (int(it.weight) / 10.0))
		var sa := int(it.get("spd_abs", 0))
		out.append("DAMAGE: %s" % ["LIGHT (-15%)", "MEDIUM", "HEAVY (+15%)", "X-HEAVY (+25%)"][sa & 3])
		out.append("RANGE: %s" % ["SHORT (-15%)", "MEDIUM", "LONG (+15%)", "X-LONG (+25%)"][(sa >> 2) & 3])
		out.append("ACCURACY: %s" % ["ROUGH (-15%)", "NORMAL", "FINE (+15%)", "FOOTED (+25%)"][(sa >> 4) & 3])
		if pack > 1 and int(it.get("count", 0)) > 0:
			out.append(pack_line)
		return out
	if ot == 46:                                          # poison (the original: "Poison", weight, count, level)
		out.append("POISON")
		if int(it.get("weight", 0)) > 0:
			out.append("WEIGHT: %.1f LBS" % (int(it.weight) / 10.0))
		if pack > 1:
			out.append(pack_line)
		if int(it.get("level", 0)) > 0:
			out.append("LEVEL %d" % int(it.level))
		return out
	if ot >= 32 and ot <= 38:
		out.append_array([("%s ARMOR" % kind).to_upper(), "FACTOR: %d" % int(it.dps_af), "ABSORB: %d%%" % int(it.spd_abs), cd, qb])
	elif ot == SHIELD_TYPE:
		out.append_array(["SHIELD", "SIZE: %s" % ["", "SMALL", "MEDIUM", "LARGE"][clampi(int(it.get("type_damage", 1)), 0, 3)], cd, qb])
	elif ot > 0 and ot < 32:
		out.append((kind if kind.ends_with("Weapon") else "%s Weapon" % kind).to_upper())
		if DAMAGE.has(int(it.get("type_damage", 0))):
			out.append(("%s DAMAGE" % DAMAGE[int(it.type_damage)]).to_upper())
		out.append("DPS: %.1f   SPD: %.1f" % [int(it.dps_af) / 10.0, int(it.spd_abs) / 10.0])
		if pack_line != "":
			out.append(pack_line)
		out.append_array([cd, qb])
		if int(it.get("hand", 0)) == 1:
			out.append("TWO-HANDED")
	else:
		if kind != "" and pack_line != "":
			out.append(kind.to_upper())
		if pack_line != "":
			out.append(pack_line)
		out.append_array([cd, qb])
	return out


## Shift+I (Delve): the summary - weapon / armour numbers, magical bonuses and who can use it, worded like the period
## item pages that copy the in-game delve ("Damage Type: Slash  DPS: 6.80  Magical Bonuses: Sword: +2 pts  Hits:
## 15 pts"). No weapon speed: "Speed has been added to weapon and shield full delves" came in 2004, after 1.65.
static func delve(it: Dictionary) -> Array:
	if it.is_empty():
		return []
	var lines := []
	var ot := int(it.get("object_type", 0))
	var kind: String = OBJECT_TYPES.get(ot, "")
	if kind != "":
		lines.append(kind.to_upper())
	if ot >= 32 and ot <= 38:
		lines.append("ARMOR FACTOR: %d" % int(it.dps_af))
		lines.append("ABSORB: %d%%" % int(it.spd_abs))
	elif ot == SHIELD_TYPE:
		lines.append("SHIELD SIZE: %s" % ["", "SMALL", "MEDIUM", "LARGE"][clampi(int(it.get("type_damage", 1)), 0, 3)])
	elif ot > 0 and ot < 32:
		if DAMAGE.has(int(it.get("type_damage", 0))):
			lines.append("DAMAGE TYPE: %s" % String(DAMAGE[int(it.type_damage)]).to_upper())
		lines.append("DPS: %.1f" % (int(it.dps_af) / 10.0))
	var bonuses := []
	for i in range(1, 11):
		var v := int(it.get("bonus%d" % i, 0))
		var t := str(int(it.get("bonus%dtype" % i, 0)))
		if v == 0 or t == "0":
			continue
		var nm: Array = _names("bonus").get(t, ["Bonus %s" % t, ""])
		var unit := String(nm[1])
		var amount := ("%d%%" % v) if unit == "%" else (("+%d PTS" % v) if unit == "+pts" else ("%d PTS" % v if unit == "pts" else str(v)))
		bonuses.append("  %s: %s" % [String(nm[0]).to_upper(), amount])
	if not bonuses.is_empty():
		lines.append("MAGICAL BONUSES:")
		lines.append_array(bonuses)
	lines.append("QUALITY: %d%%" % int(it.get("quality", 85)))
	lines.append("BONUS: %d%%" % int(it.get("bonus", 0)))
	lines.append("WEIGHT: %.1f LBS" % (int(it.get("weight", 0)) / 10.0))
	if int(it.get("level", 0)) > 0:
		lines.append("LEVEL: %d" % int(it.level))
	var who := []
	for c in String(it.get("allowedclasses", "0")).split(";", false):
		if c.strip_edges() != "0" and _names("class").has(c.strip_edges()):
			who.append(String(_names("class")[c.strip_edges()]).to_upper())
	if not who.is_empty():
		lines.append("USABLE BY: " + ", ".join(who))
	return lines


static func pct(it: Dictionary, key: String) -> int:
	return roundi(100.0 * int(it.get(key, 1)) / maxf(1, int(it.get("max" + key, 1))))


static var _name_tables := {}


static func _names(kind: String) -> Dictionary:
	if not _name_tables.has(kind):
		var parsed = JSON.parse_string(FileAccess.get_file_as_string("res://content/items/%s_names.json" % kind))
		_name_tables[kind] = parsed if parsed is Dictionary else {}
	return _name_tables[kind]


const SHIELD_TYPE := 42
