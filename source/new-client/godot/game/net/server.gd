extends Node
## The Offline DAoC server link (autoload Server - Offline DAoC Rebuilt, owner 2026-10-11): when the launcher starts the
## game (--server=<host> --account=<name> --password=<pw>, its account.txt) the game is a client of the Claude version's
## server, 1:1 - the C# DaocClient speaks the DAoC protocol (client 1.127) and the server owns everything: the account,
## the characters, the world, every NPC, monster and bot (with their brains), combat, quests, items and commands.
## The game draws what the server sends and sends what you do. Without --server the game runs on its own as before.

signal logged_in(account: String)
signal login_failed(why: String)
signal characters_ready(realm: String)
signal disconnected(why: String)
signal created(ok: bool, why: String)               # a character creation came back (the list holds it, or not)
signal deleted
signal name_checked(name: String, result: int)      # 0 free, 1 not allowed, 2 taken
signal world(kind: String, d: Dictionary)           # every world packet (DaocClient.ReadWorld), for ServerWorld / the UI
signal jumped(entry: Dictionary)                    # the server moved you inside the region (a teleport)

var active := false                      # started by the launcher: the server is the game
var host := "127.0.0.1"
var account := ""
var password := ""
var client: Node = null                  # DaocClient (C#)
var logged := false
var server_name := ""
var characters := {}                     # "alb" / "mid" / "hib" -> [character dicts in the game's own shape]
var allowed_realm := 0                   # the server's Realm reply: 0 any, else 1 / 2 / 3
var selected := {}                       # the character being played
var entry := {}                          # where the server put you: region, zone, pos (zone metres), face, oid ...
var me_oid := 0                          # your object id in the region
var playing := false                     # the zone is loaded and the server has you in the game
var no_world := false                    # --no-world (tests only: the quest probe): in the game without loading any zone -
										 # no zone viewer, the packets only (tests/quest_driver.gd reads them)
var init_at := -1000000                  # when PlayerInit went (ticks ms): the server restarts your saved buffs then
var last_error := ""                     # shown on the character screen (a place this client hasn't built yet)
var classes := {}                        # content/rules/server_classes.json (pipeline/build_server_classes.py)
var custom_class := true                 # the 0.36b server's Sluaghbinder; --custom-class=off on a 0.36 server (the
                                         # launcher reads the game's classes/enable_sluaghbinder switch)
var _index := {}                         # content/zones/zones_index.json
var _looks := {}                         # character name -> the figure look chosen at creation (user://server_looks.json)
var _pending := {}                       # a creation / deletion waiting for the server's refreshed list
var _entering := false
var _backlog: Array = []                 # chat / status lines that came while the zone was loading (shown once in)
var _ping_left := 10.0

const LOOKS_FILE := "user://server_looks.json"
const REALM_KEYS := {1: "alb", 2: "mid", 3: "hib"}
const REALM_IDS := {"alb": 1, "mid": 2, "hib": 3}
# eRace -> [name, Catacombs figure prefix, male model, female model] (eLivingModel, PlayerRace.cs)
const RACES := {1: ["Briton", "bri", 32, 35], 2: ["Avalonian", "ava", 61, 65], 3: ["Highlander", "hig", 39, 43],
		4: ["Saracen", "sar", 48, 52], 5: ["Norseman", "nor", 503, 507], 6: ["Troll", "tro", 137, 145],
		7: ["Dwarf", "dwa", 185, 193], 8: ["Kobold", "kob", 169, 177], 9: ["Celt", "cel", 302, 310],
		10: ["Firbolg", "fir", 286, 294], 11: ["Elf", "elf", 334, 342], 12: ["Lurikeen", "lur", 318, 326],
		13: ["Inconnu", "inc", 716, 724], 14: ["Valkyn", "val", 773, 781], 15: ["Sylvan", "syl", 700, 708]}
const RACE_BY_NAME := {"Briton": 1, "Avalonian": 2, "Highlander": 3, "Saracen": 4, "Norse": 5, "Norseman": 5,
		"Troll": 6, "Dwarf": 7, "Kobold": 8, "Celt": 9, "Firbolg": 10, "Elf": 11, "Lurikeen": 12, "Inconnu": 13,
		"Valkyn": 14, "Sylvan": 15}
# GlobalConstants.STARTING_STATS_DICT, in the client's packet order STR DEX CON QUI INT PIE EMP CHA
const RACE_STATS := {1: [60, 60, 60, 60, 60, 60, 60, 60], 2: [45, 60, 45, 70, 80, 60, 60, 60],
		3: [70, 50, 70, 50, 60, 60, 60, 60], 4: [50, 80, 50, 60, 60, 60, 60, 60], 5: [70, 50, 70, 50, 60, 60, 60, 60],
		6: [100, 35, 70, 35, 60, 60, 60, 60], 7: [60, 50, 80, 50, 60, 60, 60, 60], 8: [50, 70, 50, 70, 60, 60, 60, 60],
		9: [60, 60, 60, 60, 60, 60, 60, 60], 10: [90, 40, 60, 40, 60, 60, 70, 60], 11: [40, 75, 40, 75, 70, 60, 60, 60],
		12: [40, 80, 40, 80, 60, 60, 60, 60], 13: [50, 70, 60, 50, 70, 60, 60, 60], 14: [55, 65, 45, 75, 60, 60, 60, 60],
		15: [70, 55, 60, 45, 70, 60, 60, 60]}
const CUSTOM_CLASS_ID := 63               # the Sluaghbinder (the server's eCharacterClass.Sluaghbinder)
const STAT_INDEX := {"STR": 0, "DEX": 1, "CON": 2, "QUI": 3, "INT": 4, "PIE": 5, "EMP": 6, "CHR": 7, "CHA": 7}
# eLoginError (packets\Server\IPacketLib.cs) -> what the player reads
const LOGIN_ERRORS := {1: "Your password is incorrect.", 2: "Your account is invalid.",
		3: "The authorization server is unavailable!", 5: "Incorrect client version.", 6: "Error accessing user account.",
		7: "No record for user found.", 8: "Your account has no access to any games.",
		9: "Your account has no access to this game.", 10: "Your account has been closed.",
		11: "Your account is already logged in.", 12: "The server is at max population. Please try later.",
		13: "The game is currently closed. Please try later.",
		16: "Your account is already logged in to a different server.",
		17: "Your account is being logged out. Try back in 1 minute.",
		18: "The server is at max population for free accounts. Please try later.",
		19: "Your account needs an invite for this server.", 20: "Your account has been banned.", 21: "Unknown error.",
		23: "You are not authorized to use the expansion version.", 24: "Only one free account per PC is allowed.",
		25: "Requested server wasn't found.", 170: "Service not available."}   # (eLoginError: the client's own texts)


func _ready() -> void:
	get_tree().set_auto_accept_quit(false)               # the close button asks the server to quit first (_notification);
														# without a server it quits as before (below)
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--server="):
			host = a.split("=", true, 1)[1]
			active = true
		elif a.begins_with("--account="):
			account = a.split("=", true, 1)[1]
		elif a.begins_with("--password="):
			password = a.split("=", true, 1)[1]
		elif a == "--no-world":
			no_world = true
		elif a == "--custom-class=off":
			custom_class = false
	if not active:
		return
	var script = load("res://game/net/DaocClient.cs")
	if script == null:
		active = false
		push_error("[server] the DAoC client link needs the .NET build of the game")
		return
	var c = JSON.parse_string(FileAccess.get_file_as_string("res://content/rules/server_classes.json"))
	classes = c if c is Dictionary else {}
	var ix = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zones_index.json"))
	_index = ix if ix is Dictionary else {}
	var lk = JSON.parse_string(FileAccess.get_file_as_string(LOOKS_FILE)) if FileAccess.file_exists(LOOKS_FILE) else null
	_looks = lk if lk is Dictionary else {}
	client = script.new()
	client.name = "DaocClient"
	add_child(client)
	client.LoggedIn.connect(_on_logged_in)
	client.LoginFailed.connect(func(code: int): login_failed.emit(String(LOGIN_ERRORS.get(code, "Login failed (%d)." % code))))
	client.Disconnected.connect(func(why: String):
		logged = false
		playing = false
		disconnected.emit(why))
	client.Realm.connect(func(r: int): allowed_realm = r)
	client.Characters.connect(_on_characters)
	client.World.connect(_on_world)
	client.Packet.connect(_on_raw)


## Packets DaocClient leaves undecoded (raw bytes), read here.
var raw_seen := {}                       # undecoded packet code -> how many came (tests: what the client doesn't read)


func _on_raw(code: int, data: PackedByteArray) -> void:
	raw_seen[code] = int(raw_seen.get(code, 0)) + 1
	if code == 0x74 and data.size() >= 1:                # AttackMode (PacketLib168): the server ended (0) / began (1)
		_on_world("attack_mode", {"on": data[0] != 0})  # your attack - stunned, out of arrows, Engage ...
	elif code == 0x75 and data.size() >= 4:              # ConcentrationList (PacketLib191): your concentration spells
		conc_list = _read_conc(data)
	elif code == 0xC5 or code == 0xC6:                   # AddFriend / RemoveFriend: your friends online
		_friends_packet(code == 0xC5, data)
	elif code == 0x92 and data.size() >= 14:             # Weather (PacketLib168): the region's storm
		weather = {"x": _be32(data, 0), "width": _be32(data, 4),
				"fog": _be16(data, 8), "speed": _be16(data, 10), "intensity": _be16(data, 12),
				"at": Time.get_ticks_msec()}


## Your friends who are online (FriendsManager): the whole list once you are in the game, then one name as a friend
## comes or goes - the old client's "Your friend %s just entered / left the game." (game.dll; owner 2026-10-14: the
## friends list did nothing on the server).
var friends_online: Array = []
var _friends_listed := false


func _friends_packet(add: bool, data: PackedByteArray) -> void:
	# PacketLib1126: AddFriend a count, RemoveFriend a 0; then the names (a 4-byte little-endian length each)
	var names := []
	var p := 1
	while p + 4 <= data.size() and (not add or names.size() < data[0]):
		var n := data.decode_u32(p)
		if n > 64 or p + 4 + n > data.size():
			break
		names.append(data.slice(p + 4, p + 4 + n).get_string_from_ascii())
		p += 4 + n
	if add and not _friends_listed:                      # the list when you enter the world: no notices
		_friends_listed = true
		friends_online = names
		return
	for n in names:
		if add and not friends_online.has(n):
			friends_online.append(n)
			PlayerData.message.emit("Your friend %s just entered the game." % n, "system")
		elif not add and friends_online.has(n):
			friends_online.erase(n)
			PlayerData.message.emit("Your friend %s just left the game." % n, "system")


## A class's name by the server's class id (/friend window's list).
func class_name_of(id: int) -> String:
	return String(classes.get("classes", {}).get(str(id), {}).get("name", ""))


## The region's storm (WeatherManager / RegionWeather, owner 2026-10-14: the server's weather never showed - the game
## rolled storms of its own): a band `width` units wide on the region's X axis whose east edge was at `x` when it
## came, moving east `speed` units a second, rain `intensity` (up to 120); all 0: clear. ZoneViewer draws it.
var weather := {}


static func _be32(d: PackedByteArray, at: int) -> int:
	return (d[at] << 24) | (d[at + 1] << 16) | (d[at + 2] << 8) | d[at + 3]


static func _be16(d: PackedByteArray, at: int) -> int:
	return (d[at] << 8) | d[at + 1]


## The storm's strength where you stand (0..1): inside the moving band, else 0.
func storm_at(region_x: float) -> float:
	if weather.is_empty() or int(weather.width) <= 0:
		return 0.0
	var east := float(weather.x) + float(weather.speed) * (Time.get_ticks_msec() - int(weather.at)) / 1000.0
	if region_x > east - float(weather.width) and region_x < east:
		return clampf(float(weather.intensity) / 120.0, 0.0, 1.0)
	return 0.0


## ConcentrationList: count, 3 unknown, then per spell its place, 0, concentration, icon (short), spell name and on
## whom (pascal strings, cut to 14) - for the concentration window (owner 2026-10-14: it was empty on the server).
var conc_list: Array = []                # [{index, conc, icon, border, name, owner}]
var max_conc := 0                        # the status packet's concentration: your pool and what is left
var conc_left := 0


func _read_conc(data: PackedByteArray) -> Array:
	var out := []
	var p := 4
	for _i in data[0]:
		if p + 6 > data.size():
			break
		var e := {"index": data[p], "conc": data[p + 2], "icon": (data[p + 3] << 8) | data[p + 4]}
		p += 5
		var names := []
		for _k in 2:
			var n: int = data[p] if p < data.size() else 0
			names.append(data.slice(p + 1, p + 1 + n).get_string_from_ascii())
			p += 1 + n
		e.name = names[0]
		e.owner = names[1]
		var ic := skill_icon("spell", int(e.icon))
		e.icon = int(ic[0])
		e.border = int(ic[1])
		out.append(e)
	return out


func _process(delta: float) -> void:
	if not logged:
		return
	_ping_left -= delta
	if _ping_left <= 0.0:
		_ping_left = 10.0
		client.call("Ping")


## Connects and logs in with the launcher's account (the login screen calls this).
func start() -> void:
	if not active or client == null:
		return
	if client.call("Open", host):
		client.call("Login", account, password)


func _on_logged_in(acc: String, srv: String) -> void:
	server_name = srv
	if logged:
		# LoginGranted again: the server's answer to a creation / deletion / character select - refresh the list
		if not _pending.is_empty():
			request_characters(String(_pending.realm))
		return
	logged = true
	account = acc
	logged_in.emit(acc)
	for r in ["alb", "mid", "hib"]:                       # every realm's list (the realm screen shows the counts)
		request_characters(r)


## Asks the server for a realm's characters ("alb" / "mid" / "hib"); characters_ready(realm) when they arrive.
## The server answers these out of order (its character overview is sent asynchronously), so they go one at a time
## and each answer belongs to the one asked (or, with characters in it, to their own realm).
var _want: Array = []
var _asking := ""


func request_characters(realm: String) -> void:
	if client == null or not logged or realm == _asking or realm in _want:
		return
	_want.append(realm)
	_pump()


func _pump() -> void:
	if _asking != "" or _want.is_empty():
		return
	_asking = String(_want.pop_front())
	client.call("RequestCharacters", int(REALM_IDS.get(_asking, 0)))


func characters_in(realm: String) -> Array:
	return characters.get(realm, [])


func _on_characters(realm_id: int, list: Array) -> void:
	var key := _asking if _asking != "" else String(REALM_KEYS.get(realm_id, ""))
	if not list.is_empty():                                 # the characters' own realm decides
		key = String(REALM_KEYS.get(int(list[0].get("realm", 0)), key))
	if key == _asking:
		_asking = ""
	_pump.call_deferred()
	if key == "":
		return
	var out := []
	for c in list:
		out.append(_to_game(c, key))
	characters[key] = out
	if not _pending.is_empty() and String(_pending.realm) == key:
		var p := _pending
		_pending = {}
		var there := out.any(func(c): return String(c.name).to_lower() == String(p.name).to_lower())
		if p.op == "create":
			created.emit(there, "" if there else "The server did not accept that character.")
		else:
			deleted.emit()
	characters_ready.emit(key)


## A server character in the game's own character shape (CharacterStore's fields the screens read).
func _to_game(c: Dictionary, realm: String) -> Dictionary:
	var race: Array = RACES.get(int(c.get("race", 0)), ["", "cel", 0, 0])
	var female := int(c.get("gender", 0)) == 1
	var nm := String(c.get("name", ""))
	return {"name": nm, "realm": realm, "race": race[0], "gender": "female" if female else "male",
			"class_name": String(c.get("class_name", "")), "level": int(c.get("level", 1)),
			"class_id": int(c.get("class_id", 0)), "stats": _game_stats(c.get("stats", [])),
			"prefix": "%s_%s" % [race[1], "f" if female else "m"], "look": _looks.get(nm, look_of(c)), "server": c,
			"slot": int(c.get("slot", 0)), "location_text": String(c.get("location", "")),
			"inventory": _overview_gear(nm, c), "active_weapon": _overview_weapon(c), "kit_shield_given": true}


## The worn gear the character overview lists (models only) as runtime items, so the select screen dresses the figure
## as the old client did: "ovw:<name>:<slot>".
static func _overview_gear(nm: String, c: Dictionary) -> Dictionary:
	var inv := {}
	var models: Dictionary = c.get("models", {})
	var slots := {"helmet": 21, "gloves": 22, "boots": 23, "torso": 25, "cloak": 26, "legs": 27, "arms": 28,
			"right_hand": 10, "left_hand": 11, "two_hand": 12, "distance": 13}
	for k in slots:
		var m := int(models.get(k, 0)) & 0x1FFF
		if m <= 0:
			continue
		var id := "ovw:%s:%d" % [nm, slots[k]]
		var shield := int(WeaponModels.info(m).get("shield", 0)) > 0 if slots[k] == 11 else false
		var colors: Dictionary = c.get("colors", {})
		# PacketLib1125: an item's guild emblem in its colour's place (owner 2026-10-17: no emblems on the select
		# screen) - a dye is 0-143 (tints.csv), more is the emblem
		var tex := int(colors.get(k, 0))
		# the chest's, gloves' and boots' extensions (their meshes, Fig3Figure.item_armor): PacketLib1125's two
		# bytes, boots << 4 | gloves and torso << 4 | hood
		var ext := int({22: int(c.get("ext_boots_gloves", 0)) & 0xF, 23: int(c.get("ext_boots_gloves", 0)) >> 4,
				25: int(c.get("ext_torso_hood", 0)) >> 4}.get(slots[k], 0))
		ItemDb.add_runtime(id, {"id": id, "name": "", "model": m, "object_type": 42 if shield else 0, "hand": 0,
				"color": tex if tex <= 0xFF else 0, "emblem": tex if tex > 0xFF else 0, "extension": ext})
		inv[str(slots[k])] = {"id": id, "count": 1}
	return inv


static func _overview_weapon(c: Dictionary) -> String:
	var models: Dictionary = c.get("models", {})
	if int(models.get("right_hand", 0)) > 0 or int(models.get("left_hand", 0)) > 0:
		return "standard"
	if int(models.get("two_hand", 0)) > 0:
		return "twohand"
	return "ranged" if int(models.get("distance", 0)) > 0 else "standard"


## The old client's appearance bytes (DOLCharacters EyeSize / LipSize / EyeColor / HairColor / FaceType / HairStyle /
## MoodType, PacketLib1126 notes): eye size = low nibble of EyeSize, nose = its high nibble; ears / lips = low nibble of
## LipSize, chin / jaw = high; EyeColor: high nibble the eye colour, low the skin tone; FaceType and HairStyle: their
## high nibble. As the figure's look (sliders in its order: Nose, Eyes, Lips, Jaw).
static func look_of(c: Dictionary) -> Dictionary:
	var hi := func(v: int) -> int: return (v >> 4) & 15 if v >= 16 else v
	var es := int(c.get("eye_size", 0x44))
	var ls := int(c.get("lip_size", 0x44))
	var ec := int(c.get("eye_color", 0))
	return {"face": hi.call(int(c.get("face", 0))), "hair": hi.call(int(c.get("hair_style", 0))),
			"hair_color": int(c.get("hair_color", 0)) & 15, "eyes": (ec >> 4) & 15, "skin": ec & 15,
			"sliders": [(es >> 4) & 15, es & 15, ls & 15, (ls >> 4) & 15]}


## The figure's look -> the appearance bytes the server stores (look_of's inverse): eye size, lip size, eye colour,
## hair colour, face, hair style, mood.
static func face_bytes(look: Dictionary) -> Array:
	var sl: Array = look.get("sliders", [4, 4, 4, 4])
	var v := func(i: int) -> int: return clampi(int(sl[i]) if i < sl.size() else 4, 0, 15)
	return [(v.call(1) & 15) | ((v.call(0) & 15) << 4), (v.call(2) & 15) | ((v.call(3) & 15) << 4),
			(int(look.get("skin", 0)) & 15) | ((int(look.get("eyes", 0)) & 15) << 4), int(look.get("hair_color", 0)) & 15,
			(int(look.get("face", 0)) & 15) << 4, (int(look.get("hair", 0)) & 15) << 4, 0]


## The server's stats (STR DEX CON QUI INT PIE EMP CHA) in the character sheet's order (STR CON DEX QUI ...).
static func _game_stats(s: Array) -> Array:
	if s.size() != 8:
		return []
	return [s[0], s[2], s[1], s[3], s[4], s[5], s[6], s[7]]


# ---------------------------------------------------------------- the create screen

## The server's classes for a realm and race (eRace id): [[class id, name], ...] in the server's order.
func classes_for(realm: String, race_id: int, female: bool) -> Array:
	var out := []
	for id in classes.get("realms", {}).get(realm, []):
		if int(id) == CUSTOM_CLASS_ID and not custom_class:
			continue                                     # a 0.36 server has the classic classes only
		var c: Dictionary = classes.classes.get(str(int(id)), {})
		if race_id in (c.get("races", []) as Array).map(func(x): return int(x)):
			out.append([int(id), String(c.female if female else c.name)])
	return out


## The name rules the server enforces (CharacterCreateRequestHandler: at least 3 letters, a capital first - anything
## else disconnects a player) and the 24 characters the packets carry.
static func name_error(n: String) -> String:
	if n.length() < 3:
		return "Names must be at least 3 letters long."
	if n.length() > 23:
		return "That name is too long."
	if not RegEx.create_from_string("^[A-Z][a-zA-Z]+$").search(n):
		return "Names must start with a capital letter and use letters only."
	return ""


func check_name(n: String) -> void:
	client.call("CheckName", n)


## Creates a character on the server: race and class from the create screen, the 30 creation points on the class's
## three stats (+10 each - CharacterStatValidator), the figure look kept here (the server stores face bytes only).
func create_character(realm: String, race_name: String, female: bool, class_id: int, char_name: String, look: Dictionary) -> String:
	var race_id := int(RACE_BY_NAME.get(race_name, 0))
	if race_id == 0:
		return "Unknown race."
	if class_id == CUSTOM_CLASS_ID and not custom_class:
		return "This server has the classic classes only."   # (the server would ignore the request)
	var used := characters_in(realm).map(func(c): return int(c.slot))
	var slot := -1
	for i in 10:
		if not (i in used):
			slot = i
			break
	if slot < 0:
		return "This realm has no free character slot."
	var stats: Array = (RACE_STATS[race_id] as Array).duplicate()
	var cs: Array = classes.get("classes", {}).get(str(class_id), {}).get("stats", ["STR", "CON", "DEX"])
	var picked := []
	for s in cs:
		var i := int(STAT_INDEX.get(String(s), -1))
		if i >= 0 and not (i in picked):
			picked.append(i)
	for i in [0, 2, 1, 3, 4, 5, 6, 7]:                      # (a class naming a stat twice: the next ones)
		if picked.size() >= 3:
			break
		if not (i in picked):
			picked.append(i)
	for i in picked:
		stats[i] += 10
	var face := face_bytes(look)
	_looks[char_name] = look
	_save_looks()
	_pending = {"op": "create", "realm": realm, "name": char_name}
	client.call("CreateCharacter", {"realm": int(REALM_IDS[realm]), "slot": slot, "name": char_name, "face": face,
			"class_id": class_id, "race": race_id, "gender": 1 if female else 0,
			"model": int(RACES[race_id][3 if female else 2]), "stats": stats})
	return ""


func delete_character(realm: String, c: Dictionary) -> void:
	# the server deletes in the realm it was last asked about (HandleDeleteCharacterRequest: Account.Realm) - so ask
	# about this character's realm first, in the same breath (owner 2026-10-11: a delete on one realm's screen took
	# a level 50 from another realm)
	var r := String(c.get("realm", realm))
	_pending = {"op": "delete", "realm": r, "name": String(c.name)}
	client.call("RequestCharacters", int(REALM_IDS[r]))
	client.call("DeleteCharacter", int(REALM_IDS[r]), int(c.slot))
	_looks.erase(String(c.name))
	_save_looks()


## The figure look of a character changed on the customize screen (kept here; the server has no figure looks).
func save_look(char_name: String, look: Dictionary) -> void:
	_looks[char_name] = look
	_save_looks()
	for r in characters:
		for c in characters[r]:
			if String(c.name) == char_name:
				c["look"] = look


func _save_looks() -> void:
	var f := FileAccess.open(LOOKS_FILE, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(_looks))


# ---------------------------------------------------------------- entering the world

## PLAY: the server loads the character and puts it in the world; the zone loads when its position arrives.
func enter_world(c: Dictionary) -> void:
	max_speed_pct = 100
	game_time_ms = -1                                     # (a fresh login waits for the server's own time)
	_friends_listed = false
	friends_online = []
	selected = c
	inv = {}
	points = {}
	info = {}
	weapon_stats = {}
	skills = []
	spell_lines = []
	quests = {}
	_entering = true
	playing = false
	client.call("RequestCharacters", int(REALM_IDS[String(c.realm)]))   # (the server enters from the realm last asked)
	client.call("EnterWorld", int(REALM_IDS[String(c.realm)]), int(c.slot), String(c.name))


## The server's time of day now, in game hours: its last Time packet run on at its own rate (nights 25% faster, as
## the server's DayNightCycleTimer and SkySystem); -1 before the first.
func game_hour() -> float:
	if game_time_ms < 0:
		return -1.0
	var h := game_time_ms / 3600000.0
	var left := (Time.get_ticks_msec() - game_time_at) / 1000.0     # real seconds since
	while left > 0.0:                                         # stepped, an hour of game time at a time
		var night := h >= 18.0 or h < 6.0
		var rate := day_increment * (1.25 if night else 1.0) / 3600.0   # game hours a real second
		var to_edge := (6.0 - fposmod(h, 6.0))                # the next 6-hour mark (day / night changes on them)
		var take := minf(left, to_edge / rate)
		h = fposmod(h + take * rate, 24.0)
		left -= take
	return h


## The zone viewer has the zone loaded and you in it: tell the server (PlayerInit, GameOpen).
func zone_ready() -> void:
	if not playing:
		init_at = Time.get_ticks_msec()
		client.call("PlayerInit")
		client.call("GameOpen")
	playing = true
	var lines := _backlog
	_backlog = []
	for e in lines:
		world.emit(e[0], e[1])


func _on_world(kind: String, d: Dictionary) -> void:
	var t0 := Time.get_ticks_usec()
	_on_world_inner(kind, d)
	var dt := Time.get_ticks_usec() - t0
	Perf.proc_add("server packets", dt)                  # (the live log: process by part)
	if dt > 20000:
		Perf.proc_add("slow packet " + kind, dt)         # which kind, when one packet held a frame up


func _on_world_inner(kind: String, d: Dictionary) -> void:
	match kind:
		"you":                                            # PositionAndObjectID: you are in the world (or a new region)
			me_oid = int(d.oid)
			_set_entry(int(d.region), float(d.x), float(d.y), float(d.z), int(d.heading))
			if _entering or not playing:
				_entering = false
				playing = false
				CharacterStore.selected = selected
				if no_world:                              # (tests: in the game at once, nothing drawn)
					zone_ready()
					world.emit(kind, d)
					return
				get_tree().change_scene_to_file("res://game/zone_viewer.tscn")
				return
		"max_speed":
			max_speed_pct = int(d.percent)
		"time":                                           # (recorded even between worlds: a new one starts from it)
			game_time_ms = int(d.time)
			game_time_at = Time.get_ticks_msec()
			day_increment = maxf(1.0, float(d.get("increment", 24)))
			if not playing:                                   # (the sky itself takes it once you are in the world)
				_backlog.append([kind, d])
				return
		"region_changed":                                 # the server moved you to another region: ask for it
			playing = false
			_entering = true
			client.call("WorldInitAgain")
		"jump":                                           # a teleport inside the region
			if int(d.x) != 0 or int(d.y) != 0:
				_set_entry(int(entry.get("region", 0)), float(d.x), float(d.y), float(d.z), int(d.heading))
				jumped.emit(entry)
		"name_check":
			name_checked.emit(String(d.name), int(d.result))
		"message", "status":
			if kind == "message":
				if _quiet_delves > 0 and String(d.get("text", "")).begins_with("Delve objectType="):
					_quiet_delves -= 1                       # a GM's debug line for a hover's request (the old client
					return                                   # asked only on a right-click, where it still shows)
				_exchange_text(String(d.get("text", "")))
			if not playing:
				_backlog.append([kind, d])
				return
		"inventory":
			on_inventory(d)
		"market_open":                                    # [Browse the exchange]: the market search window
			_load_prices()
			exchange_open.emit("browse")
		"market":                                         # a page of search results
			_market_packet(d)
		"icons":                                          # your effect icons (UpdateIcons: the changed ones)
			for e in d.list:
				var old: Dictionary = effects.get(int(e.index), {})
				if not old.is_empty():
					_item_lines.erase("fx:%d" % int(old.icon))      # (its time and strength may have changed)
				if int(e.icon) == 0:
					effects.erase(int(e.index))
				else:
					# a concentration / permanent effect: no time shown (the server sends its endless duration as ~18 h,
					# 65535 s; owner 2026-10-11: no time for those) - nothing timed runs that long
					var timed := int(e.seconds) > 0 and int(e.seconds) < NO_TIME_SECONDS
					# its third byte: 1 = an immunity, or a buff another one overrides (the server's IsDisabled) - the old
					# client draws it tinted red (owner 2026-10-19)
					effects[int(e.index)] = {"icon": int(e.icon), "name": String(e.name), "negative": int(e.negative),
							"until": Time.get_ticks_msec() + int(e.seconds) * 1000 if timed else 0,
							"disabled": (int(e.get("immune", 0)) & 1) != 0}
			PlayerData.effects_changed.emit()
		"map_marker":
			map_markers[int(d.id)] = Vector4(float(d.region), float(d.x), float(d.y), float(d.z))
		"map_marker_remove":
			map_markers.erase(int(d.id))
		"money":
			on_money(d)
		"points":
			on_points(d)
		"character":
			on_character(d)
		"weapon_armor":                                   # the sheet's weapon skill / damage, armor factor
			weapon_stats = d
			PlayerData.changed.emit()
		"quest":                                          # your journal (QuestEntry): a line set or removed
			if String(d.name) == "":
				quests.erase(int(d.index))
			else:
				quests[int(d.index)] = {"name": String(d.name), "text": String(d.get("text", "")), "goals": d.get("goals", [])}
			PlayerData.quests_changed.emit()
		"stats":                                          # base + buffs + items, in the sheet's order
			# the sheet's value, as the client added it up: base + buffs + items (to their cap) + realm abilities
			var t := []
			for i in 8:
				t.append(int(d.base[i]) + int(d.buff[i]) + mini(int(d.item[i]), int(d.cap[i])) + int(d.ra[i]))
			stats = _game_stats(t)
			stat_buffs = _game_stats((d.buff as Array).map(func(x): return int(x)))
			con_lost = int(d.get("con_lost", 0))
			max_hp = int(d.get("max_hp", 0))
			apply_to_player()
		"resists":                                        # crush slash thrust heat cold matter body spirit energy
			resists = []
			for i in 9:
				resists.append(int(d.base[i]) + int(d.buff[i]) + mini(int(d.item[i]), int(d.cap[i])) + int(d.ra[i]))
			PlayerData.changed.emit()
		"group":                                          # the group window: the others (you are one of them)
			group_members = d.members
			PlayerData.group = []
			for m in group_members:
				if int(m.oid) != me_oid:
					PlayerData.group.append({"name": String(m.name), "class_name": String(m.class_name),
							"level": int(m.level), "hp": 1.0, "power": 1.0, "end": 1.0, "effects": [], "oid": int(m.oid)})
			PlayerData.group_changed.emit()
		"group_update":
			for u in d.list:
				var i := int(u.index)
				if i < group_members.size() and int(group_members[i].oid) != me_oid:
					var nm := String(group_members[i].name)
					for g in PlayerData.group:
						if String(g.name) != nm:
							continue
						if u.has("icons"):
							# the member's effect icons (GroupMemberUpdate's icon record): the group page shows what is
							# on each companion, not only on you (owner 2026-10-14)
							var fx := []
							for icon in u.icons:
								var ic := skill_icon("spell", int(icon))
								fx.append({"icon": int(ic[0]), "border": int(ic[1]), "name": "", "fx": int(icon)})
							g["effects"] = fx
						else:
							g["hp"] = float(u.hp) / 100.0
							g["power"] = float(u.power) / 100.0
							g["end"] = float(u.end) / 100.0
			PlayerData.group_changed.emit()
		"encumbrance":                                    # the inventory's weight box (x / y)
			weight_now = int(d.weight)
			weight_max = int(d.max)
			PlayerData.inventory_changed.emit()
		"skills":
			on_skills(d)
		"trainer":                                        # the trainer's specializations (TrainerWindow code 0)
			trainer_specs = d.get("specs", [])
			_trainer_names = {}                               # the names and lists come again after it
			_names_run = 0
			trainer_changed.emit()
		"trainer_names":                                  # the trainer's skill names (TrainerWindow code 4)
			var at := _names_run                              # arrival order: the packet's own start is one byte and
			for n in d.names:                                 # wraps past 255 (long hybrid lists)
				_trainer_names[at] = String(n)
				at += 1
			_names_run = at
			_name_trainer_skills()
		"trainer_skills":                                 # what each specialization gives by level (code 3)
			trainer_skills = {}
			var run := 0                                      # the names' running place (the full counts)
			for sp in d.specs:
				var list: Array = []
				for i in sp.skills.size():
					var k: Dictionary = sp.skills[i]
					k["at"] = run + i
					list.append(k)
				trainer_skills[int(sp.index)] = list
				run += int(sp.count)
			_name_trainer_skills()
			trainer_changed.emit()
		"trainer_ra":                                     # the trainer's Realm Skills (TrainerWindow code 1)
			realm_abilities = d.list
			ra_points = int(d.points)
			trainer_changed.emit()
		"disable":                                        # reuse timers (DisableSkills)
			_on_disable(d)
		"spell_line":
			on_spell_line(d)
		"debug_mode":                                     # GM /fly: the old client flew you, invisible (model 666)
			var v := viewer()
			if v and v.player and int(d.get("fly", 0)) in [1, 2]:
				v.player.set_flying(int(d.fly) == 1)
				if int(d.fly) == 1:
					PlayerData.message.emit("Flying: Space up, Ctrl down, Shift faster.", "system")
			return
		"quit":
			playing = false
			if int(d.mode) == 1:
				get_tree().quit()
			elif _exit_after_quit:                        # (the ESC menu's QUIT / the window closed: out once saved)
				# out by the character screen, as the old client went (owner 2026-10-15: after a QUIT, entering again
				# gave "the server closed the connection" for a minute). The server's Quit (mode 0) takes you out of
				# the world but keeps the connection "playing" until the client asks for its character list
				# (CharacterOverviewRequest -> CharScreen); a connection that drops while "playing" is a link death -
				# "Linkdead player ... will quit in 60 seconds", and every login meanwhile is refused ("still being
				# logged out from linkdeath"). The list asked for, its answer (or 3 s), then the game closes.
				var done := [false]
				var leave := func():
					if not done[0]:
						done[0] = true
						get_tree().quit()
				characters_ready.connect(func(_r): leave.call(), CONNECT_ONE_SHOT)
				get_tree().create_timer(3.0).timeout.connect(leave)
				for r in ["alb", "mid", "hib"]:
					request_characters(r)
			else:
				# back to the character screen: the lists asked again, as the old client did - the ones from the login
				# showed where you were then (owner 2026-10-13: still "Vale of Mularn" after a teleport to West Skona)
				for r in ["alb", "mid", "hib"]:
					request_characters(r)
				# and again once the server has saved you: it sends Quit before it saves (QuitTimer.Quit), so the
				# first lists can still hold the old level (owner 2026-10-14: a level 2 showed level 1)
				get_tree().create_timer(2.5).timeout.connect(func():
					if logged and not playing:
						for r in ["alb", "mid", "hib"]:
							request_characters(r))
				get_tree().change_scene_to_file("res://game/pregame.tscn")
			return
	world.emit(kind, d)


func _set_entry(region: int, gx: float, gy: float, gz: float, heading: int) -> void:
	var zid := zone_at(region, gx, gy)
	entry = {"region": region, "x": gx, "y": gy, "z": gz, "heading": heading, "zone": zid,
			"pos": to_local(zid, gx, gy, gz), "face": compass_of(heading)}


# ---------------------------------------------------------------- coordinates

## The zone of a region holding a region point (zones.dat offsets in 8192-unit blocks, content/zones/zones_index.json).
func zone_at(region: int, gx: float, gy: float) -> int:
	var bx := gx / 8192.0
	var by := gy / 8192.0
	for k in _index:
		var z: Dictionary = _index[k]
		if int(z.get("region", -1)) != region:
			continue
		if bx >= float(z.x) and bx < float(z.x) + float(z.w) and by >= float(z.y) and by < float(z.y) + float(z.h):
			return int(k)
	return -1


func zone_info(zone_id: int) -> Dictionary:
	return _index.get(str(zone_id), {})


## Region units -> a zone's metres (the zone's corner at the origin; DAoC Y runs south = +Z, height = Y).
func to_local(zone_id: int, gx: float, gy: float, gz: float) -> Vector3:
	var z := zone_info(zone_id)
	return Vector3((gx - float(z.get("x", 0)) * 8192.0) * 0.025, gz * 0.025, (gy - float(z.get("y", 0)) * 8192.0) * 0.025)


## A zone's metres -> region units.
func to_region(zone_id: int, p: Vector3) -> Vector3:
	var z := zone_info(zone_id)
	return Vector3(p.x / 0.025 + float(z.get("x", 0)) * 8192.0, p.z / 0.025 + float(z.get("y", 0)) * 8192.0, p.y / 0.025)


## DAoC heading (0..4095, 0 = facing south / +Y, 1024 west) -> the game's compass degrees (0 north, 90 east).
static func compass_of(heading: int) -> float:
	return fposmod(180.0 + float(heading) * 360.0 / 4096.0, 360.0)


static func heading_of(compass: float) -> int:
	return int(round(fposmod(compass - 180.0, 360.0) / 360.0 * 4096.0)) & 0xFFF


## A model's facing in the scene for a DAoC heading (NpcState's: facing (-sin a, +cos a), models face +Z).
static func yaw_of(heading: int) -> float:
	return -float(heading) * TAU / 4096.0


# ---------------------------------------------------------------- input to the server

## Chat and slash commands, as the old client sent them: "/cmd args" -> "&cmd args", plain text -> "&say text".
## Leaving the world as the old client did - the server's /quit: you sit, its countdown (20 s, up to 60 after a
## fight; a GM at once), then it saves you and sends Quit - back to the character screen, or out of the game with
## `exit`. Closing the game at once left the character link-dead for a minute and saved only at its end: the server
## stopped in that minute (the launcher with the game) and the play since the last save was lost (owner 2026-10-14:
## "the instant quit on the esc menu can lead to rollbacks").
var _exit_after_quit := false
var _quit_asked_at := -1000000


func quit_world(exit: bool) -> void:
	if not playing or client == null:
		if exit:
			get_tree().quit()
		return
	_exit_after_quit = _exit_after_quit or exit
	_quit_asked_at = Time.get_ticks_msec()
	command("/quit")


## The window's close button while in the world: the server's /quit first - you sit through its countdown (up to 20 s,
## longer after a fight) while it saves you, then its Quit closes the game. Closing again does NOT drop the connection:
## a dropped one leaves your character link-dead in the world for 60 s, and a server stopped or a character entered in
## that minute loses everything since the last autosave (owner 2026-10-15: "i logged out ... and was several minutes
## rolled back" - the server log has "Linkdead player ... will quit in 60 seconds" before each). Later closes ask the
## server again (a quit it refused - moving, mounted, in combat - goes through once you stand still); only the
## FORCE_CLOSES'th close in a row leaves at once.
const FORCE_CLOSES := 4
var _closes := 0


func _notification(what: int) -> void:
	if what != NOTIFICATION_WM_CLOSE_REQUEST:
		return
	if not (active and playing):
		get_tree().quit()                                  # (the game's own world saves itself: ZoneViewer)
		return
	_closes = _closes + 1 if Time.get_ticks_msec() - _quit_asked_at < 15000 else 1
	if _closes >= FORCE_CLOSES:
		get_tree().quit()
		return
	PlayerData.message.emit("Logging out: sit still while the server saves your character - the game closes by itself. "
			+ ("(Close %d more times to leave without saving - you may lose recent progress.)" % (FORCE_CLOSES - _closes)
			if _closes > 1 else ""), "system")
	quit_world(true)


func command(text: String) -> void:
	text = text.strip_edges()
	if text == "" or client == null:
		return
	if text.begins_with("/"):
		client.call("Command", "&" + text.substr(1))
	else:
		client.call("Command", "&say " + text)


# ---------------------------------------------------------------- your skills and items on the server

var skills: Array = []                   # VariousUpdate 1: your usable skills in the server's order (specs first)
var trainer_specs: Array = []            # the trainer window's specializations: [{index, level, next, name}]
var trainer_skills := {}                 # spec index -> [{level, icon, page, id, name}] (the training previews)
var _trainer_names := {}                 # running place -> skill name (TrainerWindow code 4)
var _names_run := 0


func _name_trainer_skills() -> void:
	for list in trainer_skills.values():
		for k in list:
			k["name"] = _trainer_names.get(int(k.at), "")
var realm_abilities: Array = []          # the trainer's Realm Skills: [{index, level, cost, name, available}]
var ra_points := 0                       # realm skill points to spend
signal trainer_changed
var disabled := {}                       # quickbar id ("srv:skill:<n>" / "srv:spell:<line>:<level>") -> ready at (ms)
var spell_lines: Array = []              # VariousUpdate 2: [{name, spells: [{level, id, icon, name, kind}]}]
var _lines_building: Array = []
var _skill_icons := {}                   # content/rules/skill_icons.json (pipeline/build_skill_icons.py)
var _model_icons := {}                   # content/items/model_icons.json (pipeline/build_model_icons.py)
signal skills_changed


func on_skills(d: Dictionary) -> void:
	if int(d.first) == 0:
		skills = []
	skills.append_array(d.list)
	if not bool(d.more):
		_refresh_bar_icons()
		_forget_details("sk:")
		skills_changed.emit()


## Abilities, styles and hybrid spells already on your quickbars take their icons again from the list (a bar saved
## before 2026-10-13 held an ability's raw icons.csv number - another picture).
func _refresh_bar_icons() -> void:
	var first := 0
	while first < skills.size() and int(skills[first].page) == 0:
		first += 1
	for bar in PlayerData.quickbars:
		for bank in bar:
			for e in bank:
				if not (e is Dictionary) or not String(e.get("id", "")).begins_with("srv:skill:"):
					continue
				var i := first + int(String(e.id).get_slice(":", 2))
				if i >= skills.size():
					continue
				var page := int(skills[i].page)
				var ic := skill_icon("icon" if page in [1, 5, 6] else ("style" if page == 2 else "spell"), int(skills[i].icon))
				e["icon"] = int(ic[0])
				e["border"] = int(ic[1])


func on_spell_line(d: Dictionary) -> void:
	if bool(d.done):
		spell_lines = _lines_building
		_lines_building = []
		_forget_details("sk:")
		skills_changed.emit()
		return
	if int(d.line) == 0:
		_lines_building = []
	_lines_building.append({"name": String(d.get("name", "")), "spells": d.get("spells", [])})


## A skill's icon on the game's sheets: [icon, border] - spells through spells.csv, styles through styles.csv, then
## icons.csv (the client's lookup); abilities carry an icons.csv id.
## Your effects as the server lists them (UpdateIcons), for the effects bar: [{icon, border, name, seconds}] in the
## server's order; seconds 1e9 = no time limit (a concentration buff: no time shown, no blink).
const NO_TIME_SECONDS := 10 * 3600
var max_hp := 0                          # the stats packet's maximum hit points (the sheet's HIT POINTS)
var resists: Array = []                  # your resists (StatsUpdate's resist form): crush .. energy, in %
var effects := {}                         # icon index -> {icon, name, negative, until (ticks msec, 0 = none)}
var pet_icons: Array = []                 # your pet's effect icons (the PetWindow packet: icons only, no times)


## Item delve text for the hover box: asked of the server when the pointer reaches an item (DetailRequest, as a right
## click) and kept per slot until that slot changes. The server answers every request in order, so the answers are
## matched to a queue of what asked: a hover's answer is kept quietly, anything else opens the information window.
var _item_lines := {}                    # slot (or another key: "x:<n>" an exchange result) -> {caption, lines}
var _detail_queue: Array = []            # [{key (null = show it), at (ticks msec)}]
const DETAIL_WAIT_MS := 3000


func prefetch_item(slot: int) -> void:
	if inv.has(slot):
		prefetch_detail(slot, 1, slot)


## Any item's delve text kept for its hover box under `key` (DetailRequest object_type / object_id: 1 a slot - yours or
## the open listing chest's - 10 an exchange result's place on the page).
func prefetch_detail(key: Variant, object_type: int, object_id: int, caption := "") -> void:
	if client == null or _item_lines.has(key):
		return
	for q in _detail_queue:
		if q.key != null and typeof(q.key) == typeof(key) and q.key == key:
			return
	_detail_queue.append({"key": key, "at": Time.get_ticks_msec(), "caption": caption.to_lower()})
	_quiet_delves += 1
	client.call("Detail", object_type, object_id)


var _quiet_delves := 0                   # hover requests whose GM debug line ("Delve objectType=...") is still to come


func _forget_details(prefix: String) -> void:
	for k in _item_lines.keys():
		if k is String and (k as String).begins_with(prefix):
			_item_lines.erase(k)


## A spell / style / ability's DetailRequest (the server's DetailDisplayHandler): [object type, object id], or [].
## A list spell "srv:spell:<line>:<level>": type 2, line * 100 + level. A skill "srv:skill:<n>" (its place after the
## specs): 100 + n, by its page - 6 a style, 3 a hybrid's spell or song (owner 2026-10-14: "my buffs don't even have
## a tooltip" - they were asked as abilities, 8, and the server had nothing to say), 8 an ability / realm ability.
func skill_detail(e: Dictionary) -> Array:
	var parts := String(e.get("id", "")).split(":")
	if parts.size() >= 4 and parts[0] == "srv" and parts[1] == "spell":
		return [2, int(parts[2]) * 100 + int(parts[3])]
	if parts.size() >= 3 and parts[0] == "srv" and parts[1] == "skill":
		var n := int(parts[2])
		var first := skills.size()
		for i in skills.size():
			if int(skills[i].page) != 0:
				first = i
				break
		var page := int(skills[first + n].page) if first + n < skills.size() else -1
		if page < 0:
			page = {"style": 2, "spell": 3}.get(String(e.get("kind", "")), 1)
		return [6 if page == 2 else (3 if page in [3, 4] else 8), 100 + n]
	return []


## The hover text of a spell / style / ability on a bar or a sheet: asked of the server when the pointer reaches it,
## kept until your skills change. {caption, lines} or {} (not here yet).
func prefetch_skill(e: Dictionary) -> void:
	var r := skill_detail(e)
	if not r.is_empty():
		prefetch_detail("sk:%d:%d" % r, r[0], r[1], String(e.get("name", "")))


func skill_lines(e: Dictionary) -> Dictionary:
	var r := skill_detail(e)
	return {} if r.is_empty() else item_lines("sk:%d:%d" % r)


## An effect icon's hover text (DetailRequest 5: the effect's id is its icon - PlayerEffectListComponent).
func prefetch_effect(icon: int, name: String) -> void:
	prefetch_detail("fx:%d" % icon, 5, icon, name)


func effect_lines(icon: int) -> Dictionary:
	return item_lines("fx:%d" % icon)


func item_lines(key: Variant) -> Dictionary:
	return _item_lines.get(key, {})


## A request that opens the information window (a right click, Shift+I, a skill's information).
func detail_shown(object_type: int, object_id: int) -> void:
	_detail_queue.append({"key": null, "at": Time.get_ticks_msec()})
	client.call("Detail", object_type, object_id)


## A DetailWindow from the server: true when it answered a hover (kept, not shown).
func take_detail(d: Dictionary) -> bool:
	var now := Time.get_ticks_msec()
	while not _detail_queue.is_empty() and now - int(_detail_queue[0].at) > DETAIL_WAIT_MS:
		_detail_queue.pop_front()                          # never answered (the item went away): drop it
	if int(d.get("mode", 0)) != 0 or _detail_queue.is_empty():
		return false                                       # the server's own windows (statistics, news ...)
	# the server sends nothing when it has nothing to say: an answer naming a later request skips the silent ones
	var cap := String(d.get("caption", "")).to_lower()
	for i in _detail_queue.size():
		if i > 0 and String(_detail_queue[i].get("caption", "")) == cap and cap != "" 				and String(_detail_queue[0].get("caption", "")) != cap:
			for _j in i:
				_detail_queue.pop_front()
			break
	var q: Dictionary = _detail_queue.pop_front()
	if q.key == null:
		return false
	_item_lines[q.key] = {"caption": String(d.caption), "lines": d.lines}
	return true


## The server's map markers (your realm's bots, region units) -> the map's dots: those in the zone you are in, in its
## metres, as WorldLink.bot_dots gave the game's own bots.
var map_markers := {}                    # marker id -> Vector4(region, x, y, z)


func bot_dots() -> PackedVector3Array:
	var out := PackedVector3Array()
	for id in map_markers:
		if int(id) < QUEST_MARKERS:
			var p := _marker_here(map_markers[id])
			if p != Vector3.INF:
				out.append(p)
	return out


## The server's quest markers (one each, ids 0xFFFE0001..: bounty 1, reputation 2, the Sluaghbinder epic 3, classic
## quests 4 - BountyMapMarkers / ReputationMapMarkers / SluaghbinderQuestMapMarkers / ClassicQuests) in the zone you
## are in, as the map's red step marks.
const QUEST_MARKERS := 0xFFFE0000


func quest_map_marks() -> Array:
	var out := []
	for id in map_markers:
		if int(id) >= QUEST_MARKERS:
			var p := _marker_here(map_markers[id])
			if p != Vector3.INF:
				out.append({"at": Vector2(p.x, p.z), "kind": "step"})
	return out


## A marker (region units) in the zone you are in -> its metres, else INF.
func _marker_here(v: Vector4) -> Vector3:
	var zid := current_zone_id()
	var region := int(zone_info(zid).get("region", -1))
	if int(v.x) != region or zone_at(region, v.y, v.z) != zid:
		return Vector3.INF
	return to_local(zid, v.y, v.z, v.w)


func effects_list() -> Array:
	var out := []
	var keys := effects.keys()
	keys.sort()
	var now := Time.get_ticks_msec()
	for k in keys:
		var e: Dictionary = effects[k]
		var ic := skill_icon("spell", int(e.icon))
		out.append({"icon": int(ic[0]), "border": int(ic[1]), "name": String(e.name), "fx": int(e.icon),
				"seconds": maxf(0.001, (int(e.until) - now) / 1000.0) if int(e.until) > 0 else 1e9,
				"disabled": bool(e.get("disabled", false))})
	return out


func pet_effects_list() -> Array:
	var out := []
	for icon in pet_icons:
		var ic := skill_icon("spell", int(icon))
		out.append({"icon": int(ic[0]), "border": int(ic[1]), "name": "", "seconds": 1e9})   # no times: no blink
	return out


func skill_icon(kind: String, icon: int) -> Array:
	if _skill_icons.is_empty():
		var t = JSON.parse_string(FileAccess.get_file_as_string("res://content/rules/skill_icons.json"))
		_skill_icons = t if t is Dictionary else {"spell": {}, "style": {}, "icon": {}}
	match kind:
		"spell":
			return _skill_icons.spell.get(str(icon), _skill_icons.icon.get(str(icon), [icon, -1]))
		"style":
			return _skill_icons.style.get(str(icon), [0, -1])
		"icon":                                            # abilities / realm abilities: an icons.csv id, like the
			# client (the raw number drew another picture). 0 is none - the weapon / armour proficiencies, augmented
			# stats (155 of the server's abilities): text only, as the client; the sheet's icon 0 is its "?" picture
			# (owner 2026-10-15: "specialization icons are showing up as question marks")
			if icon <= 0:
				return [-1, -1]
			return _skill_icons.icon.get(str(icon), [icon, -1])
	return [icon, -1]


## Where an item is worn (the packets carry no Item_Type): the server's own items' slot for its model
## (content/items/model_slots.json, pipeline/build_model_slots.py); the packet's hand (WriteItemData: 1 two-handed,
## 2 left hand) decides for weapons. 0: not worn. (Owner 2026-10-15: no gear could be equipped on 0.36c.)
var _model_slots := {}


func item_slot(model: int, hand: int) -> int:
	if _model_slots.is_empty():
		var t = JSON.parse_string(FileAccess.get_file_as_string("res://content/items/model_slots.json")) 				if FileAccess.file_exists("res://content/items/model_slots.json") else null
		_model_slots = t if t is Dictionary else {"_": 0}
	var t := int(_model_slots.get(str(model), 0))
	if t in [10, 11, 12] and hand == 1:
		return 12
	if t in [10, 11, 12] and hand == 2:
		return 11
	return t


func item_icon(model: int) -> int:
	if _model_icons.is_empty():
		var t = JSON.parse_string(FileAccess.get_file_as_string("res://content/items/model_icons.json"))
		_model_icons = t if t is Dictionary else {"_": 0}
	return int(_model_icons.get(str(model), 0))


## Train a realm ability one level (the trainer window's TRAIN on a Realm Skills row): TrainHandler by its place in
## the class's list.
func train_realm_ability(index: int, level: int) -> void:
	if client:
		client.call("TrainRealmAbility", index, level)


## DisableSkills: a used ability / spell's reuse timer - abilities by their place among the non-spec skills, list spells
## by line and place in the line (its level from spell_lines); 0 seconds = ready again.
func _on_disable(d: Dictionary) -> void:
	var now := Time.get_ticks_msec()
	for e in d.list:
		var id := ""
		var secs := 0
		if int(d.code) == 1:
			id = "srv:skill:%d" % int(e[0])
			secs = int(e[1])
		else:
			var li := int(e[0])
			var si := int(e[1])
			if li < spell_lines.size() and si < (spell_lines[li].spells as Array).size():
				id = "srv:spell:%d:%d" % [li, int(spell_lines[li].spells[si].level)]
			secs = int(e[2])
		if id == "":
			continue
		if secs > 0:
			disabled[id] = now + secs * 1000
		else:
			disabled.erase(id)


## Seconds until a quickbar id can be used again (0 = ready).
func reuse_left(id: String) -> float:
	if not disabled.has(id):
		return 0.0
	var left := (int(disabled[id]) - Time.get_ticks_msec()) / 1000.0
	if left <= 0.0:
		disabled.erase(id)
		return 0.0
	return left


## A quickbar / sheet entry used: "srv:skill:<n>" (n = place among the non-spec skills) -> UseSkill,
## "srv:spell:<line>:<level>" -> UseSpell, "srv:<slot>" (an item) -> UseSlot.
func use_entry(id: String, player: Node3D, zone_id: int, target_in_view: bool) -> bool:
	if client == null or not id.begins_with("srv:"):
		return false
	var r := to_region(zone_id, player.global_position)
	var heading := heading_of(fposmod(-rad_to_deg(player.facing()), 360.0))
	var speed := Vector2(player.velocity.x, player.velocity.z).length() / 0.025
	var parts := id.split(":")
	if parts.size() >= 3 and parts[1] == "skill":
		client.call("UseSkill", r.x, r.y, r.z, speed, heading, target_in_view, int(parts[2]), 1)
	elif parts.size() >= 4 and parts[1] == "spell":
		client.call("UseSpell", r.x, r.y, r.z, speed, heading, target_in_view, int(parts[3]), int(parts[2]))
	elif parts.size() == 2:
		client.call("UseSlot", r.x, r.y, r.z, speed, heading, target_in_view, int(parts[1]), 1)
	else:
		return false
	return true


## Weapon keys: the server readies a weapon set when its slot is used (GamePlayer.UseSlot: 10 / 11 standard,
## 12 two-handed, 13 ranged).
func switch_weapon(which: String, player: Node3D, zone_id: int) -> void:
	var slot: int = {"standard": 10, "twohand": 12, "ranged": 13}.get(which, 10)
	var r := to_region(zone_id, player.global_position)
	var heading := heading_of(fposmod(-rad_to_deg(player.facing()), 360.0))
	client.call("UseSlot", r.x, r.y, r.z, 0.0, heading, true, slot, 0)


## Your quickbars stay on this PC (the old client kept them in its own files, per character).
func quickbars_path(char_name: String) -> String:
	return "user://server_quickbars/%s.json" % char_name.to_lower()


# ---------------------------------------------------------------- your character's state from the server

var inv := {}                            # slot -> {id, count} (each item also in ItemDb's runtime table as "srv:<slot>")
var money := {"mithril": 0, "platinum": 0, "gold": 0, "silver": 0, "copper": 0}
var points := {}                         # CharacterPointsUpdate
var info := {}                           # VariousUpdate 3: level, class, titles, guild ...
var weapon_set := "standard"
var gear := {}                           # hood up, cloak / helm hidden (inventory packet)
var quests := {}                         # journal index -> {name, text} (QuestEntry)
var weight_now := -1                     # Encumberance: what you carry / can carry (-1: not sent yet)
var weight_max := -1
var game_time_ms := -1                   # the server's last time of day (ms into the game day; Time packet), -1 none yet
var game_time_at := 0                    # ...when it came (ticks ms)
var day_increment := 24.0                # ...and its clock's speed (WORLD_DAY_INCREMENT)
var max_speed_pct := 100                 # your last MaxSpeed (percent): kept across a region change - the server sends it
										 # only when it changes (a skald's speed song pulses on at the same value)
var group_members: Array = []            # the group window's members in group order (you included)
var stats: Array = []                    # StatsUpdate totals, the sheet's order (STR CON DEX QUI INT PIE EMP CHA)
var stat_buffs: Array = []               # ... its buffs / debuffs part alone (the sheet's green / red), same order
var con_lost := 0                        # constitution lost at deaths (StatsUpdate; already off the base CON)
signal gear_changed                      # your worn items changed (the figure re-dresses)


## The vaults (owner 2026-10-14: "make sure vault is working" - it only opened for the old offline vault keepers):
## the server's Vault Keeper sends your vault (PlayerVault window, slots 110-149, two pages of twenty); its Account
## Vault Keeper a page of the account vault every character of the account shares (HouseVault window, client slots
## 150-249 - the same slots the Realm Exchange listing chest uses, so they follow whichever of the two is open).
signal vault_open(title: String, first: int, count: int)
const VAULT_FIRST := 110
const VAULT_COUNT := 40
const ACCOUNT_VAULT_COUNT := 100
var container_mode := ""                 # "account" while an account vault page is open, else the listing chest's


func on_inventory(d: Dictionary) -> void:
	if int(d.window) == 5:                                # your Realm Exchange listing chest opens (ConsignmentOwner)
		_clear_slots(CHEST_FIRST, CHEST_LAST)
		container_mode = ""
		_listings_packet(d, true)
		return
	if int(d.window) == 6:                                # someone else's consignment: not your slots
		return
	if int(d.window) == 3:                                # your vault opens: its whole list
		_clear_slots(VAULT_FIRST, VAULT_FIRST + VAULT_COUNT - 1)
	elif int(d.window) == 4:                              # an account vault page opens
		_clear_slots(CHEST_FIRST, CHEST_LAST)
		container_mode = "account"
	# the chest's slots come in plain updates too (a move in or out, the rest of a long list)
	var mine := []
	var chest := []
	for it in d.items:
		if int(it.slot) >= CHEST_FIRST and int(it.slot) <= CHEST_LAST and container_mode != "account":
			chest.append(it)
		else:
			mine.append(it)
	if int(d.window) in [3, 4]:
		_on_inventory_items(d, mine)
		if int(d.window) == 3:
			vault_open.emit("VAULT", VAULT_FIRST, VAULT_COUNT)
		else:
			vault_open.emit("ACCOUNT VAULT", CHEST_FIRST, ACCOUNT_VAULT_COUNT)
		return
	if not chest.is_empty():
		_listings_packet({"items": chest}, false)
		if mine.is_empty():
			return
	_on_inventory_items(d, mine)


## Slots no longer held (a container opened afresh: its list replaces what was there).
func _clear_slots(first: int, last: int) -> void:
	for s in inv.keys():
		if int(s) >= first and int(s) <= last:
			inv.erase(s)


func _on_inventory_items(d: Dictionary, mine: Array) -> void:
	var worn := false
	for it in mine:
		var slot := int(it.slot)
		var id := "srv:%d" % slot
		_item_lines.erase(slot)                            # its hover text is asked again
		if String(it.name) == "" and int(it.model) == 0:
			inv.erase(slot)
		else:
			var nm := String(it.name)
			var count := 1
			var m := RegEx.create_from_string("^([0-9]+) (.+)$").search(nm)
			if m:
				count = int(m.get_string(1))
				nm = m.get_string(2)
			var ot := int(it.object_type)
			ItemDb.add_runtime(id, {"id": id, "item_type": item_slot(int(it.model), int(it.hand)), "name": nm, "level": int(it.level), "dps_af": int(it.value1),
					"spd_abs": int(it.value2), "hand": int(it.hand), "type_damage": int(it.type_damage), "object_type": ot,
					"weight": int(it.weight), "condition": int(it.condition), "maxcondition": 100,
					"durability": int(it.durability), "maxdurability": 100, "quality": int(it.quality),
					"bonus": int(it.bonus), "model": int(it.model), "extension": int(it.extension), "color": int(it.get("color", 0)),
					"emblem": int(it.get("emblem", 0)),            # (an emblemed cloak / shield: no colour sent)
					"effect": int(it.effect), "icon": item_icon(int(it.model)), "maxcount": 999 if count > 1 else 1,
					"packsize": 1, "price": 0, "isdropable": 1, "istradable": 1, "server": true})
			inv[slot] = {"id": id, "count": count}
		if slot >= 10 and slot <= 37:
			worn = true
	# your cloak's hood (/hood) and hidden cloak / helm, as the inventory packet carries them
	var g := {"hood": (int(d.get("hood", 0)) & 1) != 0, "cloak_hidden": (int(d.get("hidden", 0)) & 1) != 0,
			"helm_hidden": (int(d.get("hidden", 0)) & 2) != 0}
	if g != gear:
		gear = g
		worn = true
	var ws := int(d.weapon_slots) & 0x0F
	var set_now := "twohand" if ws == 2 else ("ranged" if ws == 3 else "standard")
	if set_now != weapon_set:
		worn = true                                       # another weapon set in the hands: drawn again
	weapon_set = set_now
	apply_to_player()
	if worn:
		gear_changed.emit()


# ---------------------------------------------------------------- the Realm Exchange

## The CLAUDE VERSION server's Realm Exchange broker (RealmExchangeBroker, a capital's "Realm Exchange" NPC): its
## greeting's [Browse the exchange] opens the market search (MarketExplorerWindow: pages of 20 real listings; a
## result moved into the backpack is bought), [Manage my listings] your listing chest (an inventory window of 100
## slots, client slots 150-249: a backpack item moved in is listed, moved out it is taken back, SetMarketPrice prices
## it), [Claim sale proceeds] pays out what sold while you were away. The server's rules and numbers; this client only
## shows them (ExchangeWindow).
signal exchange_open(tab: String)                 # "browse" / "listings"
signal exchange_changed
const CHEST_FIRST := 150
const CHEST_LAST := 249
const MARKET_FIRST := 1000
var market_items: Array = []                      # the shown results page: runtime ItemDb ids "srvx:<place>"
var market_page := 0
var market_pages := 0                             # the last page's number (the server's maxpage)
var market_count := -1                            # "Realm Exchange returned N real listing(s)." (page 0), -1 unknown
var market_search := {}                           # the filters of the last search (MarketSearch), for the pages
var listings := {}                                # chest slot -> runtime ItemDb id "srvc:<slot>"
var listing_page := 0                             # the chest's page on the server (100 slots each)
var proceeds := -1                                # sale proceeds waiting (copper; the greeting's amount), -1 unknown
var _prices := {}                                 # "<character>|<page>|<slot>|<name>" -> copper priced from here


func _listings_packet(d: Dictionary, opened: bool) -> void:
	if opened:
		listings = {}
	for it in d.items:
		var slot := int(it.slot)
		_item_lines.erase(slot)
		if String(it.name) == "" and int(it.model) == 0:
			listings.erase(slot)
			continue
		var id := "srvc:%d" % slot
		ItemDb.add_runtime(id, _server_item(id, it, slot))
		listings[slot] = id
	exchange_changed.emit()
	if opened:
		_load_prices()
		exchange_open.emit("listings")


## A runtime ItemDb entry for an exchange item (a result or a listing); detail_key: where its delve text is kept.
func _server_item(id: String, it: Dictionary, detail_key: Variant) -> Dictionary:
	var nm := String(it.name)
	var count := 1
	var m := RegEx.create_from_string("^([0-9]+) (.+)$").search(nm)
	if m:
		count = int(m.get_string(1))
		nm = m.get_string(2)
	return {"id": id, "name": nm, "count": count, "level": int(it.level), "dps_af": int(it.value1),
			"spd_abs": int(it.value2), "hand": int(it.hand), "type_damage": int(it.type_damage),
			"object_type": int(it.object_type), "weight": int(it.get("weight", 0)),
			"condition": int(it.get("condition", 100)), "maxcondition": 100, "durability": int(it.get("durability", 100)),
			"maxdurability": 100, "quality": int(it.get("quality", 100)), "bonus": int(it.get("bonus", 0)),
			"model": int(it.model), "color": int(it.get("color", 0)), "effect": int(it.get("effect", 0)),
			"emblem": int(it.get("emblem", 0)),
			"icon": item_icon(int(it.model)), "price": int(it.get("price", 0)), "can_use": bool(it.get("can_use", true)),
			"detail_key": detail_key, "maxcount": 999 if count > 1 else 1, "packsize": 1, "server": true}


func _market_packet(d: Dictionary) -> void:
	for k in _item_lines.keys():                          # the places now hold other items: their delve asked again
		if k is String and String(k).begins_with("x:"):
			_item_lines.erase(k)
	market_items = []
	market_page = int(d.page)
	market_pages = int(d.max_page)
	for it in d.items:
		var place := int(it.index)
		var id := "srvx:%d" % place
		ItemDb.add_runtime(id, _server_item(id, it, "x:%d" % place))
		market_items.append(id)
	exchange_changed.emit()


## Search the exchange (filters as DaocClient.MarketSearch takes them; page 0 = a new search).
func exchange_search(filters: Dictionary, page := 0) -> void:
	if client == null:
		return
	market_search = filters.duplicate()
	var f := filters.duplicate()
	f["page"] = clampi(page, 0, 255)
	if page == 0:
		market_count = -1
	client.call("MarketSearch", f)


## Buy the result at `place` on the shown page: the server's move from the market into your backpack.
func exchange_buy(place: int, to := -1) -> bool:
	var slot := to if to >= 0 else PlayerData.first_free()
	if client == null or slot < 0:
		PlayerData.message.emit("You need an empty backpack slot.", "error")
		return false
	client.call("MoveItem", MARKET_FIRST + place, slot, 1)
	return true


## A listing's price: what this client priced it at (-1 unknown - priced elsewhere, or before this was kept).
func listing_price(slot: int) -> int:
	return int(_prices.get(_price_key(slot), -1))


func set_listing_price(slot: int, copper: int) -> void:
	if client == null or not listings.has(slot):
		return
	client.call("SetMarketPrice", slot - CHEST_FIRST, copper)
	_prices[_price_key(slot)] = copper
	_save_prices()
	exchange_changed.emit()


func _price_key(slot: int) -> String:
	var it := ItemDb.item(String(listings.get(slot, "")))
	return "%s|%d|%d|%s" % [String(selected.get("name", "")), listing_page, slot, String(it.get("name", ""))]


const PRICES_PATH := "user://exchange_prices.json"


func _load_prices() -> void:
	if _prices.is_empty() and FileAccess.file_exists(PRICES_PATH):
		var t = JSON.parse_string(FileAccess.get_file_as_string(PRICES_PATH))
		_prices = t if t is Dictionary else {}


func _save_prices() -> void:
	var f := FileAccess.open(PRICES_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(_prices))


## The broker's own words, read for the exchange window: the greeting's waiting proceeds, a claim, a sale while you
## are online, the search's count, the chest's page.
func _exchange_text(text: String) -> void:
	if text.begins_with("Welcome to the Realm Exchange"):
		var m := RegEx.create_from_string("Claim sale proceeds.: *([^\\n]+)").search(text)
		proceeds = _copper_of(m.get_string(1)) if m else 0
		_load_prices()
	elif text.begins_with("You claimed ") and text.contains("Realm Exchange sales"):
		proceeds = 0
	elif text.begins_with("Your Realm Exchange listing sold for "):
		var m := RegEx.create_from_string("sold for (.+?)\\. The proceeds").search(text)
		proceeds = maxi(proceeds, 0) + (_copper_of(m.get_string(1)) if m else 0)
	elif text.begins_with("You have no Realm Exchange sale proceeds"):
		proceeds = 0
	elif text.begins_with("Realm Exchange returned "):
		market_count = int(text.get_slice(" ", 3))
	elif text.begins_with("Your listings - page "):
		listing_page = maxi(int(text.get_slice(" ", 4).trim_suffix(".")) - 1, 0)
	else:
		return
	exchange_changed.emit()


## "2 gold, 5 silver and 3 copper" (Money.GetString) -> copper.
static func _copper_of(text: String) -> int:
	var c := 0
	var scale := {"mithril": 10000000000, "platinum": 10000000, "gold": 10000, "silver": 100, "copper": 1}
	for m in RegEx.create_from_string("([0-9]+) *(mithril|platinum|gold|silver|copper)").search_all(text.to_lower()):
		c += int(m.get_string(1)) * int(scale[m.get_string(2)])
	return c


func on_money(d: Dictionary) -> void:
	money = {"mithril": int(d.mithril), "platinum": int(d.platinum), "gold": int(d.gold), "silver": int(d.silver),
			"copper": int(d.copper)}
	apply_to_player()


func on_points(d: Dictionary) -> void:
	points = d
	apply_to_player()


var weapon_stats := {}                  # SendUpdateWeaponAndArmorStats: weapon_skill, weapon_damage, armor_factor


func on_character(d: Dictionary) -> void:
	info = d
	apply_to_player()


## The server's state into the character sheet the interface shows (PlayerData).
func apply_to_player() -> void:
	if stats.size() == 8:
		PlayerData.stats = stats.duplicate()
	PlayerData.inventory = inv.duplicate(true)
	PlayerData.money = money.duplicate()
	PlayerData.active_weapon = weapon_set
	PlayerData.character["gear"] = gear.duplicate()
	if not points.is_empty():
		PlayerData.realm_points = int(points.realm_points)
		PlayerData.exp_frac = float(points.level_permill) / 1000.0
		PlayerData.xp = int(points.xp)
		PlayerData.character["spec_points"] = int(points.spec_points)
	if not info.is_empty():
		PlayerData.level = int(info.level)
		PlayerData.character["level"] = int(info.level)
		PlayerData.character["class_name"] = String(info.class_name)
		PlayerData.character["guild"] = "" if String(info.guild) == "None" else String(info.guild)
		PlayerData.character["realm_title"] = String(info.realm_title)
	PlayerData.inventory_changed.emit()
	PlayerData.changed.emit()


## The zone viewer while you are in the world (for your position on use packets), else null.
func viewer() -> Node:
	var s := get_tree().current_scene
	return s if s and s.get("player") != null and s.get("zone") != null else null


func current_zone_id() -> int:
	var v := viewer()
	if v == null:
		return int(entry.get("zone", -1))
	var rg = v.get("region")
	return int(rg.current.info.id) if rg and rg.current else int(v.zone.info.id)


## Use a skill / spell / item entry from where you stand ("srv:..." ids).
func use(id: String) -> bool:
	var v := viewer()
	if v == null:
		return false
	# the cast's "target in view" flag: whether you can see it, as the client worked it out (owner 2026-10-19: a spell at a
	# target behind a wall cast in full, then "You can't see your target from here!" - the old client is refused at
	# once). It was "you have a target", so the server's start-of-cast check (SpellHandler: HasLos = TargetInView)
	# always passed and only the position updates' flag told it otherwise, at the end. A target that isn't a drawn
	# living (yourself, a door, an object) keeps "a target" - the server's own range rule covers those.
	var in_view := not PlayerData.target.is_empty()
	var w = v.get("npcs")
	if in_view and w is ServerWorld and (w as ServerWorld).target and is_instance_valid((w as ServerWorld).target):
		in_view = (w as ServerWorld).target_in_view(v.player.global_position)
	return use_entry(id, v.player, current_zone_id(), in_view)
