class_name Pregame
extends Node3D
## Realm select, character select, creation and customize: the old client's pregame screens 1:1
## (frames, medallions, bars, the three 3D realm backdrops from app/pregame) with the "new old" readability pass
## (Minion Pro bold, ALL CAPS, larger text, gold on black). 1.65 creation (owner 2026-10-09): race + BASE class from
## the client's charman/summary.txt (greyed where the client says "not available"); the advanced class comes from a
## trainer at level 5. No "Adjust Attributes" step (stats fill in like the old client). Customize zooms to the face.
## Debug: --pregame-shot=<folder> walks every screen and saves a png of each.

const PRE := "res://content/pregame"
const UI := "res://content/ui"
const GOLD := Color(0.86, 0.71, 0.38)
const GOLD_HI := Color(1.0, 0.85, 0.45)
const CREAM := Color(0.93, 0.89, 0.78)
const DIM := Color(0.55, 0.52, 0.47)
const ERROR := Color(1.0, 0.45, 0.35)
# The client's pregame camera (game.dll 0x599a0e init, 0x59ed4e select setup, 0x59ea59 update; angles in units of
# pi): yaw 1.5pi + 215 deg, pitch 0.031pi - 5 deg, distance 200 units to a target 44 units above the scene origin
# (the scene is shifted by (140, 0, 198.6) so the origin is its floor), camera = target + (-40, 40, 0) +
# R(-distance, 0, 0). Converted to Godot below. Fitted to the owner's screenshots of the real client: +14 deg yaw
# and the character 0.9 m to camera-right of the scene origin (Hibernia select and Albion create both line up).
const CAM_POS := Vector3(-0.727, -3.81, -4.28)
const CAM_LOOK := Vector3(-4.5, -3.865, -1.0)
const CAM_FOV := 40.0
const REALM_LIGHT := {"hib": [Color(1.0, 0.94, 0.8), 1.0], "alb": [Color(1.0, 1.0, 1.0), 1.0], "mid": [Color(1, 1, 1), 1.0]}
const SPOT := Vector3(-4.09, -4.98, -0.679)        # where the character stands
const REALM_NAMES := {"alb": "ALBION", "hib": "HIBERNIA", "mid": "MIDGARD"}
const REALM_ORDER := ["alb", "hib", "mid"]        # realm_selection.tga: red, green, blue panels
# race, charman summary code, figure prefix, names.dat section
const RACES := CharacterFactory.RACES               # the races and base classes live in CharacterFactory
const BASES := CharacterFactory.BASES
const BASE_BUTTON := {"FIGH": "FIGHTER", "ACOL": "ACOLYTE", "MAGE": "MAGE", "ELEM": "ELEMENTALIST", "ROGU": "ROGUE",
		"ARAW": "DISCIPLE", "WARR": "VIKING", "SPIR": "SEER", "MYST": "MYSTIC", "NROG": "ROGUE", "ARMS": "WAY OF ARMS",
		"NATU": "WAY OF NATURE", "MAGI": "WAY OF MAGIC", "STEA": "WAY OF STEALTH", "GROV": "WAY OF THE GROVE"}
const BASE_CLASS := CharacterFactory.BASE_CLASS
# the server's classes -> the base class they come from (charman/summary.txt's race-base codes)
const SERVER_BASE := {"Armsman": "FIGH", "Mercenary": "FIGH", "Paladin": "FIGH", "Reaver": "FIGH", "Cleric": "ACOL",
		"Friar": "ACOL", "Cabalist": "MAGE", "Sorcerer": "MAGE", "Theurgist": "ELEM", "Wizard": "ELEM",
		"Infiltrator": "ROGU", "Minstrel": "ROGU", "Scout": "ROGU", "Necromancer": "ARAW",
		"Warrior": "WARR", "Thane": "WARR", "Berserker": "WARR", "Skald": "WARR", "Savage": "WARR",
		"Healer": "SPIR", "Shaman": "SPIR", "Runemaster": "MYST", "Spiritmaster": "MYST", "Bonedancer": "MYST",
		"Hunter": "NROG", "Shadowblade": "NROG",
		"Blademaster": "ARMS", "Champion": "ARMS", "Hero": "ARMS", "Bard": "NATU", "Druid": "NATU", "Warden": "NATU",
		"Eldritch": "MAGI", "Enchanter": "MAGI", "Mentalist": "MAGI", "Nightshade": "STEA", "Ranger": "STEA",
		"Animist": "GROV", "Valewalker": "GROV"}
# medallion icons in breadcrumbs.tga (64 px cells, normal + hover side by side)
# owner 2026-10-09 (old client screenshots): shield = REALM, dragon = OPTIONS, swords = PLAY and CREATE (one button that
# changes with the selected slot), figure = CUSTOMIZE, skull = DELETE, X = QUIT / CANCEL. Paws / star are not on the
# owner's screens. CONTINUE = the figure (owner's creation screenshot)
const ICON := {"realm": Vector2i(0, 0), "options": Vector2i(2, 0), "delete": Vector2i(0, 1), "continue": Vector2i(2, 2),
			   "play": Vector2i(0, 2), "create": Vector2i(0, 2), "customize": Vector2i(2, 2), "quit": Vector2i(2, 3)}

var meta := {}
var realm := "hib"
var screen := ""
var chars: Array = []
var sel := 0
# When PLAY last sent the character into the world: a second click (a double-click) sent a second request, the server
# refused it ("already in the region") and closed the connection (chris4aker/OfflineDAoC-LivingServer fork, Beta 1.1,
# in-game check 2026-10-27). Cleared when the entry fails (an error, a disconnect, the character list again), so PLAY
# isn't held for the 10 s then.
var _entered_at := -100000
var draft := {}                                   # the character being created or customized
var editing := false                              # customize an existing character (select screen CUSTOMIZE)
var locks := {}

var _scene: NifScene
var _scene_realm := ""
var _env: Environment
var _cam: Camera3D
var _figure: Fig3Figure
var _figure_key := ""
var _yaw := 0.0
var _zoom := 1.0
var _layer: CanvasLayer
var _root: Control
var _frame: Control
var _wext := 1024.0
var _font_bold: FontFile
var _font_reg: FontFile
var _textures := {}
var _swatch_cache := {}
var _status: Label
var _name_edit: LineEdit
var _desc: RichTextLabel
var _menu: GameMenu
var _drag := 0


func _ready() -> void:
	meta = JSON.parse_string(FileAccess.get_file_as_string(PRE + "/pregame.json"))
	_font_bold = _font("minionpro-bold.ttf")
	_font_reg = _font("minionpro-regular.ttf")
	PregameMusic.play()
	if Server.active:                                      # the server's character lists, as they arrive
		Server.characters_ready.connect(_on_server_characters)
		Server.characters_ready.connect(func(_r): _entered_at = -100000)
		Server.disconnected.connect(func(_why): _entered_at = -100000)
		Server.login_failed.connect(func(_why): _entered_at = -100000)
		Server.created.connect(_on_server_created)
		Server.deleted.connect(func(): sel = 0; _show("select"))
		Server.name_checked.connect(_on_name_checked)
	_env = Environment.new()
	_env.background_mode = Environment.BG_COLOR
	_env.background_color = Color(0.05, 0.05, 0.07)
	_env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	_env.ambient_light_color = Color(1, 1, 1)
	_env.ambient_light_energy = 1.0                   # set per realm in _load_realm_scene (fitted to the client)
	_env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	_env.fog_enabled = true
	var we := WorldEnvironment.new()
	we.environment = _env
	add_child(we)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-45, 150, 0)
	sun.light_energy = 0.7
	sun.light_cull_mask = 2                            # lights the character only (figure meshes are on layer 2)
	add_child(sun)
	_cam = Camera3D.new()
	_cam.fov = CAM_FOV
	_cam.far = 3000.0
	add_child(_cam)
	_cam.make_current()
	_layer = CanvasLayer.new()
	add_child(_layer)
	_root = Control.new()
	_root.set_anchors_preset(Control.PRESET_FULL_RECT)
	# clicks on no button / panel pass through to the drag (left: turn the character, right: zoom) - as a full-screen
	# STOP it took every click and the drag never started (a player report 2026-10-17)
	_root.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.theme = _make_theme()
	_layer.add_child(_root)
	_menu = GameMenu.new()
	add_child(_menu)
	get_viewport().size_changed.connect(func(): _show(screen))
	var shot := ""
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--pregame-shot="):
			shot = a.split("=", true, 1)[1]
	_show("realm")
	if shot != "":
		_shots.call_deferred(shot)


# ---------------------------------------------------------------- screens

func _show(which: String) -> void:
	screen = which
	for c in _root.get_children():
		c.queue_free()
	_status = null
	match which:
		"realm": _build_realm()
		"select": _build_select()
		"create": _build_create()
		"customize": _build_customize()
	_place_camera()


func _build_realm() -> void:
	# owner 2026-10-09: no black bars - the frame (stretched to 16:9 before its x4plus pass, build_pregame.py) fills
	# the whole window; the three realm panels widen with it, so each realm's widgets centre on its panel
	_frame_art("realm_selection_wide", 0, 0, false, true)
	var kx := _wext / 1024.0
	for i in 3:
		var r: String = REALM_ORDER[i]
		var x := (170.0 + i * 342.0) * kx
		var regions: Array = {"alb": [Rect2(0, 0, 122, 206), Rect2(126, 0, 122, 206)],
						"hib": [Rect2(0, 0, 150, 214), Rect2(150, 0, 149, 214)],   # hover cells end 1 px early:
						"mid": [Rect2(0, 0, 126, 218), Rect2(128, 0, 125, 218)]}[r]   # the sheet's white strip follows (owner saw a line)
		var art := _small({"alb": "albion_button", "hib": "hibernia_button", "mid": "midgard_button"}[r])
		var b := TextureButton.new()
		b.texture_normal = _atlas(art, regions[0], 4)
		b.texture_hover = _atlas(art, regions[1], 4)
		b.ignore_texture_size = true
		b.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		_at(b, x - 75, 92, 150, 210)
		b.pressed.connect(_pick_realm.bind(r))
		var d := _label(String(meta.realm_descriptions.get(REALM_NAMES[r].to_lower(), "")).to_upper(), x - 150, 318, 300, 316,
				13, CREAM, HORIZONTAL_ALIGNMENT_CENTER, "L", true)
		d.add_theme_font_override("font", _font_reg)
		var n := Server.characters_in(r).size() if Server.active else CharacterStore.in_realm(Accounts.account, r).size()
		_label("%d CHARACTER%s" % [n, "" if n == 1 else "S"], x - 150, 636, 300, 22, 15, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "L")
	_title("SELECT YOUR REALM")
	_medallion("quit", "QUIT", 44, 726, "L", func(): get_tree().quit())


func _build_select() -> void:
	_load_realm_scene()
	_frame_art("character_selection", 12, 268)
	chars = CharacterStore.in_realm(Accounts.account, realm)
	if Server.active:
		# the server's characters (Offline DAoC Rebuilt): asked for once per realm, the screen redrawn when they come
		chars = Server.characters_in(realm)
		if not Server.characters.has(realm):
			Server.request_characters(realm)
	sel = clampi(sel, 0, CharacterStore.SLOTS_PER_REALM - 1)
	_label("SELECT CHARACTER", 760, 64, 248, 32, 22, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "R")
	for i in CharacterStore.SLOTS_PER_REALM:
		var y := 109.0 + i * 45.0
		var slot := Button.new()
		slot.flat = true
		slot.theme_type_variation = "SlotButton"
		_at(slot, 764, y + 1, 240, 43, "R")
		var knob := TextureRect.new()
		var on := i == sel                               # the selected slot's knob is filled, empty ones too
		knob.texture = _atlas(_small("misc_pieces_new"), Rect2(185 if on else 215, 7, 24, 24), 4)
		knob.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		knob.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_at(knob, 770, y + 11, 20, 20, "R")
		if i < chars.size():
			var c: Dictionary = chars[i]
			_label(("%s THE %s" % [c.name, c.class_name]).to_upper(), 796, y + 3, 206, 22, 15,
					GOLD_HI if on else CREAM, HORIZONTAL_ALIGNMENT_LEFT, "R")
			_label(("LEVEL %d IN %s" % [int(c.level), _where(c)]).to_upper(), 796, y + 23, 208, 18, 13, DIM,
					HORIZONTAL_ALIGNMENT_LEFT, "R").add_theme_font_override("font", _font_reg)
		else:
			_label("EMPTY", 796, y + 11, 208, 22, 16, GOLD_HI if i == sel else DIM, HORIZONTAL_ALIGNMENT_LEFT, "R")
		slot.pressed.connect(func(): sel = i; _show("select"))      # every slot selects, empty ones too (owner)
		slot.gui_input.connect(func(e: InputEvent):
			if e is InputEventMouseButton and e.double_click: _play_or_create())
	# one button under the list: PLAY on a character, CREATE on an empty slot (same place, the client's way)
	_medallion("play", "PLAY" if sel < chars.size() else "CREATE", 884, 600, "R", _play_or_create, 66, 24)
	_title("SELECT YOUR CHARACTER")
	var row := [["quit", "QUIT", func(): get_tree().quit()], ["realm", "REALM", func(): _show("realm")],
				["options", "OPTIONS", func(): _menu.toggle()], ["customize", "CUSTOMIZE", _customize_existing],
				["delete", "DELETE", _ask_delete]]
	for k in row.size():                                    # spread across the bottom like the client
		_medallion(row[k][0], row[k][1], _wext * (0.095 + k * 0.2025), 726, "L", row[k][2])
	_status_line()
	if Server.active and Server.last_error != "":
		_entered_at = -100000                              # (the entry failed: PLAY works again at once)
		_say(Server.last_error)
		Server.last_error = ""
	_show_figure(chars[sel] if sel < chars.size() else {})


func _build_create() -> void:
	_load_realm_scene()
	_frame_art("character_creation", 240, 238)
	var races: Array = RACES[realm]
	var race: Array = races[int(draft.race)]
	# left: DESCRIPTIONS - the race, then the chosen way / class (charman/summary.txt), like the client's panel
	_label("DESCRIPTIONS", 16, 62, 220, 30, 22, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "L")
	_label(String(race[0]).to_upper(), 24, 96, 210, 26, 20, GOLD_HI, HORIZONTAL_ALIGNMENT_LEFT, "L")
	var info: Dictionary = meta.classes.get("%s-%s" % [race[1], draft.base], {})
	if Server.active:
		_check_server_class()
		# the client's lore for this race and the class's base class (summary.txt is written per race + base: "Celt
		# Way of Magic" names its paths) - owner 2026-10-15: the server's create screen had lost it. A class the 0.36b
		# client added (Sluaghbinder) has its own text in game.dll (pipeline/build_class_lore.py: class_lore).
		info = meta.classes.get("%s-%s" % [race[1], SERVER_BASE.get(_server_class_name(), "")], {})
		if info.is_empty() and (meta.get("class_lore", {}) as Dictionary).has(_server_class_name()):
			info = {"text": String(meta.class_lore[_server_class_name()])}
		_label(_server_class_name().to_upper(), 24, 124, 210, 22, 16, GOLD, HORIZONTAL_ALIGNMENT_LEFT, "L")
	else:
		_label(BASE_BUTTON[draft.base], 24, 124, 210, 22, 16, GOLD, HORIZONTAL_ALIGNMENT_LEFT, "L")
	_desc = RichTextLabel.new()
	_desc.bbcode_enabled = false
	_desc.add_theme_font_override("normal_font", _font_reg)
	_desc.add_theme_font_size_override("normal_font_size", 14)
	_desc.add_theme_color_override("default_color", CREAM)
	_at(_desc, 24, 150, 210, 504, "L")
	_desc.text = String(info.get("text", "")).strip_edges().to_upper()
	_desc.scroll_active = false
	_fit_desc.call_deferred()
	# right: NAME (with RANDOM in the bar), RACE, GENDER, WAY - laid out like the client's panel
	_label("NAME", 790, 80, 216, 30, 22, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "R")
	_name_edit = LineEdit.new()
	_name_edit.placeholder_text = "NAME"
	_name_edit.max_length = CharacterStore.NAME_MAX
	_name_edit.text = String(draft.get("name", ""))
	_name_edit.text_changed.connect(func(t: String):
		var caret := _name_edit.caret_column
		var fixed := t.capitalize().replace(" ", "") if t != "" else t
		if fixed != t:
			_name_edit.text = fixed
			_name_edit.caret_column = caret
		draft["name"] = _name_edit.text)
	var clear := StyleBoxEmpty.new()                    # the frame paints the name box itself (x 796-1002, y 116-137)
	clear.content_margin_left = 6
	_name_edit.add_theme_stylebox_override("normal", clear)
	_name_edit.add_theme_stylebox_override("focus", clear)
	_name_edit.add_theme_font_size_override("font_size", 17)
	_at(_name_edit, 797, 116, 128, 21, "R")
	_bar_button("RANDOM", 929, 119.5, 64, 15, "R", _random_name)     # inside the name box, clear of its border
	# the frame's dotted dividers sit at y 149, 271 and 340: Race fills the band between the first two (its label as
	# the first grid cell, as the client), Gender the next band, Way below the last divider - every realm the same
	var rows := int(ceil((races.size() + 1) / 2.0))
	var ry := 149.0 + (122.0 - rows * 30.0) * 0.5 + 2.0
	_label("RACE", 794, ry, 102, 27, 20, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "R")
	for i in races.size():
		var cell := i + 1
		var b := _bar_button(String(races[i][0]).to_upper(), 794 + (cell % 2) * 106, ry + (cell / 2) * 30, 102, 27, "R",
				func(): draft.race = i; _check_base(); _check_server_class(); _show("create"))
		_selected(b, int(draft.race) == i)
		# the chosen class greys the races that can't be it, as the client's create screen (owner 2026-10-15: a
		# Sluaghbinder - Celt and Firbolg only - left every Hibernian race open)
		if Server.active and int(draft.get("class_id", 0)) > 0 and i != int(draft.race):
			b.disabled = not _class_allows(int(draft.class_id), String(races[i][0]))
	_label("GENDER", 790, 275, 216, 24, 20, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "R")
	for g in 2:
		var b := _bar_button(["MALE", "FEMALE"][g], 794 + g * 106, 304, 102, 27, "R",
				func(): draft.gender = g; _check_server_class(); _show("create"))
		_selected(b, int(draft.gender) == g)
	if Server.active:
		# the Claude version server's classes for this race (no ways - the old client picked the class itself; the
		# ones the server has switched off or this race can't be are left out - owner 2026-10-11)
		_label("CLASS", 790, 346, 216, 24, 20, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "R")
		var cl := _server_classes()
		for i in cl.size():
			var cid: int = cl[i][0]
			var cb := _bar_button(String(cl[i][1]).to_upper(), 794 + (i % 2) * 106, 376 + (i / 2) * 30, 102, 27, "R",
					func(): draft.class_id = cid; _show("create"))
			_selected(cb, int(draft.get("class_id", 0)) == cid)
	var bases: Array = [] if Server.active else BASES[realm]
	if not Server.active:
		_label("WAY", 790, 346, 216, 24, 20, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "R")
	for i in bases.size():
		var key: String = bases[i]
		var b := _bar_button(BASE_BUTTON[key].trim_prefix("WAY OF "), 794 + (i % 2) * 106, 376 + (i / 2) * 30, 102, 27,
				"R", func(): draft.base = key; _show("create"))
		b.disabled = not _base_ok(key)                   # greyed where the client says the pair is not available
		_selected(b, String(draft.base) == key)
	_title("SELECT YOUR RACE")
	_medallion("quit", "CANCEL", _wext * 0.095, 726, "L", func(): _show("select"))
	_medallion("realm", "REALM", _wext * 0.2975, 726, "L", func(): _show("realm"))
	_medallion("continue", "CONTINUE", _wext * 0.905, 726, "L", _create_continue)
	_status_line()
	_show_figure(_draft_character())


func _build_customize() -> void:
	_load_realm_scene()
	_frame_art("character_customize", 12, 268)
	_show_figure(draft if editing else _draft_character())
	var f := _figure
	_label("FACIAL FEATURES", 760, 64, 248, 30, 22, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "R")
	var y := 100.0
	y = _stepper_row("FACE", "face", f.meta.get("faces", []).map(func(x): return String(x.label).to_upper()), y)
	var sliders: Array = f.meta.get("sliders", [])
	for i in sliders.size():
		_label(String(sliders[i].label).to_upper(), 770, y, 96, 24, 15, CREAM, HORIZONTAL_ALIGNMENT_LEFT, "R")
		var s := HSlider.new()
		s.min_value = 0
		s.max_value = 8
		s.step = 1
		s.value = float((draft.look.get("sliders", [4, 4, 4, 4]) as Array)[i]) if i < (draft.look.get("sliders", []) as Array).size() else 4.0
		s.value_changed.connect(func(v: float):
			var arr: Array = (draft.look.get("sliders", [4, 4, 4, 4]) as Array).duplicate()
			while arr.size() <= i:
				arr.append(4)
			arr[i] = int(v)
			draft.look["sliders"] = arr
			_figure.apply({"sliders": arr}))
		_at(s, 868, y + 2, 118, 20, "R")
		y += 27
	y += 4
	y = _swatch_row("EYE COLOR", "eyes", _eye_colours(), y)
	y = _swatch_row("SKIN TONE", "skin", _skin_colours(), y)
	y = _stepper_row("HAIR STYLE", "hair", f.meta.get("hair_styles", []).map(func(x): return String(x.label).to_upper()), y)
	if f.option_count("hair_color") > 0 and _hair_has_mesh():
		y = _swatch_row("HAIR COLOR", "hair_color", _hair_colours(), y)
	y = _stepper_row("SIZE", "size", ["SHORT", "AVERAGE", "TALL"], y)
	if f.option_count("tattoo") > 0:
		y = _stepper_row("TATTOO", "tattoo", f.meta.get("tattoos", []).map(func(x): return String(x.label).to_upper()), y)
	_bar_button("RANDOM", 776, 606, 108, 28, "R", _randomize)
	_bar_button("DEFAULT", 892, 606, 108, 28, "R", func():
		draft.look = {}
		_figure.look = {}
		_figure.apply({"sliders": [4, 4, 4, 4]})
		_show("customize"))
	# camera: arrows turn the figure, + / - zoom (the client's customize controls)
	var misc := _small("misc_pieces_new")
	var cam_buttons := [[Rect2(110, 177, 26, 29), func(): _yaw -= 0.35; _place_camera()],
						[Rect2(173, 178, 26, 29), func(): _yaw += 0.35; _place_camera()],
						[Rect2(138, 138, 32, 31), func(): _zoom = clampf(_zoom - 0.15, 0.45, 2.0); _place_camera()],
						[Rect2(106, 138, 32, 31), func(): _zoom = clampf(_zoom + 0.15, 0.45, 2.0); _place_camera()]]
	for i in cam_buttons.size():
		var tb := TextureButton.new()
		tb.texture_normal = _atlas(misc, cam_buttons[i][0], 4)
		tb.ignore_texture_size = true
		tb.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		_at(tb, 30 + i * 36, 600, 30, 30, "L")
		tb.pressed.connect(cam_buttons[i][1])
	_label("LEFT CLICK DRAG TO ROTATE, RIGHT CLICK DRAG TO ZOOM", 30, 636, 420, 20, 13, DIM, HORIZONTAL_ALIGNMENT_LEFT, "L")
	_title("CUSTOMIZE YOUR CHARACTER")
	_medallion("quit", "CANCEL", _wext * 0.095, 726, "L", func(): _show("select" if editing else "create"))
	_medallion("realm", "REALM", _wext * 0.2975, 726, "L", func(): _show("realm"))
	if not editing:
		_medallion("customize", "RACE / CLASS", _wext * 0.7025, 726, "L", func(): _show("create"))
	_medallion("continue", "SAVE" if editing else "CONTINUE", _wext * 0.905, 726, "L", _customize_continue)
	_status_line()


# ---------------------------------------------------------------- actions

func _on_server_characters(r: String) -> void:
	if screen == "realm" or (screen == "select" and r == realm):
		_show(screen)


func _pick_realm(r: String) -> void:
	realm = r
	sel = 0
	_show("select")


func _start_create() -> void:
	editing = false
	_yaw = 0.0
	_zoom = 1.0
	draft = {"race": 0, "gender": 0, "base": BASES[realm][0], "name": "", "look": {}}
	_check_base()
	_show("create")


func _create_continue() -> void:
	draft.name = _name_edit.text.strip_edges()
	if Server.active:
		var nerr := Server.name_error(String(draft.name))
		if nerr != "":
			_say(nerr)
			return
		if int(draft.get("class_id", 0)) == 0:
			_say("Choose a class.")
			return
		_say("Checking the name...")
		Server.check_name(String(draft.name))           # _on_name_checked goes on
		return
	var err := CharacterStore.check_name(Accounts.account, String(draft.name))
	if err != "":
		_say(err)
		return
	if not _base_ok(String(draft.base)):
		_say("That class is not available to this race.")
		return
	_yaw = 0.0
	_zoom = 1.0
	_show("customize")


func _customize_existing() -> void:
	if sel >= chars.size():
		return
	editing = true
	_yaw = 0.0
	_zoom = 1.0
	draft = (chars[sel] as Dictionary).duplicate(true)
	_show("customize")


func _customize_continue() -> void:
	draft.look = _figure.look.duplicate(true)
	if editing:
		if Server.active:
			Server.save_look(String(draft.name), draft.look)
			chars = Server.characters_in(realm)
		else:
			CharacterStore.update(Accounts.account, draft)
		_show("select")
	else:
		_finish_create()


## 1.65 has no starting-location screen: a new character starts where the old server's StartupLocation table puts
## its race + base class (owner 2026-10-09). Until that zone is built the character appears at Mag Mell
## (CharacterStore.start_of); the real start stays saved with the character.
func _finish_create() -> void:
	if Server.active:
		var race_name := String(RACES[realm][int(draft.race)][0])
		var err := Server.create_character(realm, race_name, int(draft.gender) == 1, int(draft.class_id), String(draft.name),
				draft.look)
		if err != "":
			_say(err)
			return
		_say("Creating %s..." % draft.name)
		return                                              # _on_server_created goes on
	var c := CharacterFactory.make(realm, int(draft.race), int(draft.gender), String(draft.base), draft.name, draft.look)
	draft["start"] = c.start
	var err := CharacterStore.add(Accounts.account, c)
	if err != "":
		_say(err)
		return
	chars = CharacterStore.in_realm(Accounts.account, realm)
	sel = chars.size() - 1
	_show("select")


func _play_or_create() -> void:
	if sel < chars.size():
		_play()
	else:
		_start_create()


func _play() -> void:
	if sel >= chars.size():
		return
	if Server.active:                                     # the server puts the character in the world (Server.enter_world)
		var now := Time.get_ticks_msec()
		if now - _entered_at < 10000:                     # already on its way in
			return
		_entered_at = now
		_say("Entering the world...")
		Server.enter_world(chars[sel])
		return
	CharacterStore.selected = chars[sel]
	get_tree().change_scene_to_file("res://game/zone_viewer.tscn")


func _ask_delete() -> void:
	if sel >= chars.size():
		return
	var c: Dictionary = chars[sel]
	# owner 2026-10-10: the black granite of the in-game windows, not the engine's grey dialog - the client's stone
	# chat-box frame (chatext) as a nine-patch over a dimmed screen, gold title, cream text, the stone-bar buttons
	var shade := ColorRect.new()
	shade.color = Color(0, 0, 0, 0.55)
	shade.set_anchors_preset(Control.PRESET_FULL_RECT)
	shade.mouse_filter = Control.MOUSE_FILTER_STOP
	_root.add_child(shade)
	var w := 480.0
	var h := 170.0
	var box := Control.new()
	box.size = Vector2(w, h)
	box.position = ((_root.get_viewport_rect().size if _root.size == Vector2.ZERO else _root.size) - box.size) * 0.5
	shade.add_child(box)
	var holder := Control.new()                          # the art at x4 inside a 1/4-scale holder (ChatWindow's way)
	holder.scale = Vector2(0.25, 0.25)
	holder.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var np := NinePatchRect.new()
	np.texture = UiArt.tex("chatext")
	np.region_rect = Rect2(0, 64 * UiArt.K, 512 * UiArt.K, 64 * UiArt.K)
	var m := [3, 12, 12, 3]
	var sides := [SIDE_LEFT, SIDE_TOP, SIDE_RIGHT, SIDE_BOTTOM]
	for i in 4:
		np.set_patch_margin(sides[i], int(m[i] * UiArt.K))
	np.size = box.size * 4.0
	np.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.add_child(np)
	box.add_child(holder)
	var close := func(): shade.queue_free()
	var title := Label.new()
	title.text = "DELETE CHARACTER"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	title.add_theme_font_size_override("font_size", 18)
	title.add_theme_color_override("font_color", GOLD_HI)
	title.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.9))
	title.add_theme_constant_override("shadow_offset_x", 2)
	title.add_theme_constant_override("shadow_offset_y", 2)
	title.position = Vector2(0, 22)
	title.size = Vector2(w, 26)
	box.add_child(title)
	var text := Label.new()
	text.text = ("DELETE %s, LEVEL %d %s OF %s?\nTHIS CANNOT BE UNDONE." % [String(c.name), int(c.get("level", 1)),
			String(c.get("class_name", "")), String(REALM_NAMES.get(String(c.get("realm", realm)), ""))]).to_upper()
	text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	text.add_theme_font_size_override("font_size", 15)
	text.add_theme_color_override("font_color", CREAM)
	text.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.9))
	text.add_theme_constant_override("shadow_offset_x", 1)
	text.add_theme_constant_override("shadow_offset_y", 1)
	text.position = Vector2(0, 56)
	text.size = Vector2(w, 44)
	box.add_child(text)
	var del := Button.new()
	del.text = "DELETE"
	del.position = Vector2(w * 0.5 - 150, h - 52)
	del.size = Vector2(130, 30)
	del.pressed.connect(func():
		close.call()
		if Server.active:
			Server.delete_character(realm, c)
			return
		CharacterStore.remove(Accounts.account, String(c.name))
		sel = 0
		_show("select"))
	box.add_child(del)
	var keep := Button.new()
	keep.text = "KEEP"
	keep.position = Vector2(w * 0.5 + 20, h - 52)
	keep.size = Vector2(130, 30)
	keep.pressed.connect(close)
	box.add_child(keep)
	var corner := Button.new()                           # the frame art's corner square closes it, like the old X
	corner.flat = true
	corner.add_theme_stylebox_override("normal", StyleBoxEmpty.new())
	corner.add_theme_stylebox_override("hover", StyleBoxEmpty.new())
	corner.add_theme_stylebox_override("pressed", StyleBoxEmpty.new())
	corner.position = Vector2(w - 14, 0)
	corner.size = Vector2(14, 14)
	corner.tooltip_text = "CLOSE"
	corner.pressed.connect(close)
	box.add_child(corner)
	shade.gui_input.connect(func(e):                      # a click outside the box: keep
		if e is InputEventMouseButton and e.pressed and not Rect2(box.position, box.size).has_point(e.position):
			close.call())
	keep.grab_focus()


func _random_name() -> void:
	var rng := RandomNumberGenerator.new()
	rng.randomize()
	draft.name = CharacterFactory.random_name(realm, int(draft.race), rng)
	_name_edit.text = draft.name


func _randomize() -> void:
	var f := _figure
	var l := {}
	for k in ["face", "eyes", "skin", "hair", "size", "tattoo"]:
		if not locks.get(k, false) and f.option_count(k) > 0:
			l[k] = randi() % f.option_count(k)
	f.apply(l)
	if not locks.get("hair_color", false) and f.option_count("hair_color") > 0:
		f.apply({"hair_color": randi() % f.option_count("hair_color")})
	if not locks.get("sliders", false):
		var arr := []
		for i in f.meta.get("sliders", []).size():
			arr.append(randi() % 9)
		f.apply({"sliders": arr})
	draft.look = f.look.duplicate(true)
	_show("customize")


func _check_base() -> void:
	if not _base_ok(String(draft.base)):
		for k in BASES[realm]:
			if _base_ok(k):
				draft.base = k
				return


func _base_ok(key: String) -> bool:
	return CharacterFactory.available(realm, int(draft.get("race", 0)), key)


# ---------------------------------------------------------------- the server's classes (Offline DAoC Rebuilt)

func _server_classes() -> Array:
	var race_name := String(RACES[realm][int(draft.race)][0])
	return Server.classes_for(realm, int(Server.RACE_BY_NAME.get(race_name, 0)), int(draft.gender) == 1)


func _class_allows(class_id: int, race_name: String) -> bool:
	for c in Server.classes_for(realm, int(Server.RACE_BY_NAME.get(race_name, 0)), int(draft.gender) == 1):
		if int(c[0]) == class_id:
			return true
	return false


func _check_server_class() -> void:
	if not Server.active:
		return
	var cl := _server_classes()
	for c in cl:
		if int(c[0]) == int(draft.get("class_id", 0)):
			return
	draft["class_id"] = int(cl[0][0]) if cl.size() > 0 else 0


func _server_class_name() -> String:
	for c in _server_classes():
		if int(c[0]) == int(draft.get("class_id", 0)):
			return String(c[1])
	return ""


func _on_name_checked(n: String, result: int) -> void:
	if screen != "create" or n.to_lower() != String(draft.get("name", "")).to_lower():
		return
	if result == 2:
		_say("That name is already taken.")
	elif result == 1:
		_say("That name is not allowed.")
	else:
		_yaw = 0.0
		_zoom = 1.0
		_show("customize")


func _on_server_created(ok: bool, why: String) -> void:
	chars = Server.characters_in(realm)
	if ok:
		for i in chars.size():
			if String(chars[i].name).to_lower() == String(draft.get("name", "")).to_lower():
				sel = i
		_show("select")
	else:
		_say(why)


func _draft_character() -> Dictionary:
	var race: Array = RACES[realm][int(draft.race)]
	return {"prefix": "%s_%s" % [race[2], "m" if int(draft.gender) == 0 else "f"], "look": draft.get("look", {})}


# ---------------------------------------------------------------- 3D

func _load_realm_scene() -> void:
	if _scene_realm == realm:
		return
	if _scene:
		_scene.queue_free()
	_scene = NifScene.new()
	add_child(_scene)
	_scene.load_scene("%s/%s/scene.gltf" % [PRE, realm])
	_scene_realm = realm
	var fog: Dictionary = meta.fog.get(realm, {})
	if not fog.is_empty():
		_env.fog_light_color = Color(fog.color[0], fog.color[1], fog.color[2])
		_env.fog_density = float(fog.density) * 0.00004
	# scene light per realm, fitted to the owner's client screenshots: the client's fixed-function lighting darkens
	# these materials (ambient / diffuse ~0.6-0.7); Midgard was right at full light (owner: "looks good")
	var light: Array = REALM_LIGHT.get(realm, [Color(1, 1, 1), 1.0])
	_env.ambient_light_color = light[0]
	_env.ambient_light_energy = light[1]


func _show_figure(c: Dictionary) -> void:
	var prefix := String(c.get("prefix", ""))
	if prefix == "":
		if _figure:
			_figure.queue_free()
			_figure = null
			_figure_key = ""
		return
	if _figure == null or _figure_key != prefix:
		if _figure:
			_figure.queue_free()
		_figure = Fig3Figure.new()
		add_child(_figure)
		_figure.load_figure(prefix)
		_figure_key = prefix
		for mi in _figure.find_children("*", "MeshInstance3D", true, false):
			(mi as MeshInstance3D).layers = 3              # scene light + the character light
	_figure.position = SPOT
	_figure.visible = true
	_figure.look = {}
	_figure.apply(c.get("look", {}))
	_dress_figure(c)
	if screen == "customize":
		draft["look"] = _figure.look.duplicate(true)
		draft["prefix"] = prefix


## The character's worn armour and weapons on the select screen, as in game (owner 2026-10-10: the staff and armour
## didn't show) - Player.dress with the saved inventory (slot -> {id}).
var _figure_gear: Array = []


func _dress_figure(c: Dictionary) -> void:
	for g in _figure_gear:
		if is_instance_valid(g):
			g.queue_free()
	_figure_gear.clear()
	var inv: Dictionary = c.get("inventory", {}) if c.get("inventory") is Dictionary else {}
	var item_at := func(slot: int) -> Dictionary:
		var e = inv.get(str(slot))
		return ItemDb.item(String(e.id)) if e is Dictionary and e.has("id") else {}
	_figure_gear = Player.dress(_figure, item_at, String(c.get("active_weapon", "standard")), false)
	for g in _figure_gear:
		for mi in (g as Node).find_children("*", "MeshInstance3D", true, false):
			(mi as MeshInstance3D).layers = 3              # scene light + the character light, like the figure


func _place_camera() -> void:
	if _cam == null:
		return
	var k := _figure.scale.x if _figure else 1.0
	var to_cam := CAM_POS - SPOT
	to_cam.y = 0.0
	var bearing := atan2(to_cam.x, to_cam.z)
	if _figure:
		_figure.rotation.y = bearing + _yaw              # faces the camera; drag / arrows turn it
	match screen:
		"select", "realm":
			_cam.position = CAM_POS                      # the client's view, character left of the panel
			_cam.look_at(CAM_LOOK)
		"create":
			_cam.position = CAM_POS                      # same camera, aimed at the character (owner's create shot)
			_cam.look_at(SPOT + Vector3(0, 1.1, 0))
		"customize":
			# the client zooms in to the face (distance 40 units, target between the head bones); kept on the
			# select camera's side so the backdrop stays the same, face centred in the open scene area
			var head := _head_position()
			var dir := to_cam.normalized()
			_cam.position = head + dir * (0.95 * k * _zoom)
			_cam.look_at(head)
			var x := (_wext - 268.0) * 0.5
			var ndc := (x - _wext * 0.5) / (_wext * 0.5)
			var vp := get_viewport().get_visible_rect().size
			var half_h := tan(deg_to_rad(_cam.fov * 0.5)) * (vp.x / maxf(vp.y, 1.0))
			_cam.rotate_object_local(Vector3.UP, atan(ndc * half_h))


func _head_position() -> Vector3:
	if _figure:
		for s in _figure.model.find_children("*", "Skeleton3D", true, false):
			var s3: Skeleton3D = s
			var b := s3.find_bone("Bip01 Head")
			if b >= 0:
				return s3.global_transform * s3.get_bone_global_pose(b).origin + Vector3(0, 0.04 * _figure.scale.x, 0)
	return SPOT + Vector3(0, 1.65, 0)


func _unhandled_input(event: InputEvent) -> void:
	if screen not in ["select", "create", "customize"]:
		return
	if event is InputEventMouseButton:
		_drag = event.button_index if event.pressed else 0
		if screen == "customize" and event.pressed and event.button_index in [MOUSE_BUTTON_WHEEL_UP, MOUSE_BUTTON_WHEEL_DOWN]:
			_zoom = clampf(_zoom + (-0.08 if event.button_index == MOUSE_BUTTON_WHEEL_UP else 0.08), 0.45, 2.0)
			_place_camera()
	elif event is InputEventMouseMotion and _drag != 0:
		if not Input.is_mouse_button_pressed(_drag):       # let go over a button (the GUI took the release)
			_drag = 0
			return
		if _drag == MOUSE_BUTTON_LEFT:
			_yaw += event.relative.x * 0.01
		elif _drag == MOUSE_BUTTON_RIGHT and screen == "customize":
			_zoom = clampf(_zoom + event.relative.y * 0.005, 0.45, 2.0)
		_place_camera()


# ---------------------------------------------------------------- customize rows

func _stepper_row(title: String, key: String, values: Array, y: float) -> float:
	if values.is_empty():
		return y
	var cur := clampi(int(draft.look.get(key, 1 if key == "size" else 0)), 0, values.size() - 1)
	_label(title, 770, y, 96, 24, 15, CREAM, HORIZONTAL_ALIGNMENT_LEFT, "R")
	var misc := _small("misc_pieces_new")
	for side in 2:
		var tb := TextureButton.new()
		tb.texture_normal = _atlas(misc, Rect2(3 + side * 23, 200, 16, 13), 4)
		tb.texture_hover = _atlas(misc, Rect2(3 + side * 23, 216, 16, 13), 4)
		tb.ignore_texture_size = true
		tb.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
		_at(tb, 866 + side * 104, y + 4, 16, 16, "R")
		tb.pressed.connect(func():
			var v := (cur + (1 if side == 1 else -1) + values.size()) % values.size()
			var change := {key: v}
			if key == "hair":
				change["hair_color"] = int(draft.look.get("hair_color", 0))
			_figure.apply(change)
			draft.look = _figure.look.duplicate(true)
			_show("customize"))
	_label(String(values[cur]), 882, y, 88, 24, 15, GOLD_HI, HORIZONTAL_ALIGNMENT_CENTER, "R")
	_lock(key, y)
	return y + 27


func _swatch_row(title: String, key: String, colours: Array, y: float) -> float:
	if colours.is_empty():
		return y
	_label(title, 770, y, 140, 22, 15, CREAM, HORIZONTAL_ALIGNMENT_LEFT, "R")
	_lock(key, y)
	y += 22
	var cur := int(draft.look.get(key, 0))
	for i in colours.size():
		var b := Button.new()
		var st := _box(colours[i], GOLD_HI if i == cur else Color(0.1, 0.1, 0.1), 2 if i == cur else 1)
		st.set_content_margin_all(0)
		for s in ["normal", "hover", "pressed", "focus"]:
			b.add_theme_stylebox_override(s, st)
		_at(b, 772 + (i % 8) * 27, y + (i / 8) * 24, 24, 21, "R")
		b.pressed.connect(func():
			_figure.apply({key: i})
			draft.look = _figure.look.duplicate(true)
			_show("customize"))
	return y + ceilf(colours.size() / 8.0) * 24 + 4


func _lock(key: String, y: float) -> void:
	var misc := _small("misc_pieces_new")
	var tb := TextureButton.new()
	tb.toggle_mode = true
	tb.button_pressed = locks.get(key, false)
	tb.texture_normal = _atlas(misc, Rect2(226, 209, 10, 11), 4)
	tb.texture_pressed = _atlas(misc, Rect2(244, 210, 7, 10), 4)
	tb.ignore_texture_size = true
	tb.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	tb.tooltip_text = "LOCK (RANDOM LEAVES IT ALONE)"
	_at(tb, 990, y + 5, 12, 14, "R")
	tb.toggled.connect(func(on: bool): locks[key] = on)


func _hair_has_mesh() -> bool:
	var styles: Array = _figure.meta.get("hair_styles", [])
	var i := int(draft.look.get("hair", 0))
	return i < styles.size() and styles[i].node != null


func _eye_colours() -> Array:
	return _figure.meta.get("eyes", []).map(func(t): return _swatch(String(t), Color.WHITE, "iris"))


func _skin_colours() -> Array:
	var face: String = _figure.meta.faces[0].tex if _figure.meta.get("faces", []).size() > 0 else ""
	return _figure.meta.get("skin_tones", []).map(func(c): return _swatch(face, Fig3Figure.skin_tint(c), "face"))


func _hair_colours() -> Array:
	var styles: Array = _figure.meta.get("hair_styles", [])
	var i := clampi(int(draft.look.get("hair", 0)), 0, styles.size() - 1)
	return styles[i].colours.map(func(c): return _swatch(String(c.tex), Color(c.tint[0], c.tint[1], c.tint[2]), "hair"))


## A swatch from the option's own client texture: the iris colour of an eye-colour texture (it is white except the
## iris), the middle of the face times a skin tint, or a hair texture's covered pixels times the hair tint.
func _swatch(tex: String, tint: Color, kind: String) -> Color:
	var key := "%s|%s|%s" % [tex, tint.to_html(), kind]
	if _swatch_cache.has(key):
		return _swatch_cache[key]
	var img: Image = _figure._tex(tex).get_image() if tex != "" and _figure._tex(tex) else null
	var col := tint
	if img:
		img = img.duplicate()
		if img.is_compressed():
			img.decompress()
		img.convert(Image.FORMAT_RGBA8)
		if kind == "face":
			img = img.get_region(Rect2i(img.get_width() * 3 / 10, img.get_height() * 3 / 10, img.get_width() * 2 / 5, img.get_height() / 4))
		img.resize(32, 32, Image.INTERPOLATE_BILINEAR)
		var sum := Color(0, 0, 0, 0)
		var n := 0.0
		for yy in 32:
			for xx in 32:
				var p := img.get_pixel(xx, yy)
				var lum := (p.r + p.g + p.b) / 3.0
				if kind == "iris" and lum > 0.85:
					continue
				if kind == "hair" and p.a < 0.5:
					continue
				sum += p
				n += 1.0
		if n > 0:
			col = Color(sum.r / n * tint.r, sum.g / n * tint.g, sum.b / n * tint.b)
	_swatch_cache[key] = col
	return col


# ---------------------------------------------------------------- frame + widgets

## The screen frame (1024x768 art, 2x x4plus copy): scaled to the window height; on wide screens the middle stretches
## while the side panels keep their shape (left / right = the panel widths that must not stretch). letterbox: the
## whole frame keeps its aspect (realm select).
func _frame_art(name: String, left: float, right: float, letterbox := false, fill := false) -> void:
	var vp := get_viewport().get_visible_rect().size
	var s := vp.y / 768.0
	_wext = 1024.0 if letterbox else maxf(vp.x / s, 1024.0)
	_frame = Control.new()
	_frame.scale = Vector2(s, s)
	_frame.position = Vector2((vp.x - 1024.0 * s) * 0.5 if letterbox else 0.0, 0)
	_frame.size = Vector2(_wext, 768)
	_frame.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_root.add_child(_frame)
	if letterbox:
		var black := ColorRect.new()
		black.color = Color.BLACK
		black.set_anchors_preset(Control.PRESET_FULL_RECT)
		black.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_root.add_child(black)
		_root.move_child(black, 0)
	var tex := _full(name)
	if fill:                                           # art already made wide: the whole picture spans the window
		var all := TextureRect.new()
		all.texture = tex
		all.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		all.stretch_mode = TextureRect.STRETCH_SCALE
		all.mouse_filter = Control.MOUSE_FILTER_IGNORE
		all.size = Vector2(_wext, 768)
		_frame.add_child(all)
		return
	var k := tex.get_width() / 1024.0
	var pieces := [[0.0, left, 0.0, left], [left, 1024.0 - right, left, _wext - right], [1024.0 - right, 1024.0, _wext - right, _wext]]
	for p in pieces:
		if p[1] - p[0] <= 0:
			continue
		var tr := TextureRect.new()
		tr.texture = _atlas(tex, Rect2(p[0], 0, p[1] - p[0], 768), k)
		tr.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		tr.stretch_mode = TextureRect.STRETCH_SCALE
		tr.mouse_filter = Control.MOUSE_FILTER_IGNORE
		tr.position = Vector2(p[2], 0)
		tr.size = Vector2(p[3] - p[2], 768)
		_frame.add_child(tr)


## Place a widget in frame units (1024x768): anchor L keeps x from the left edge, R from the right edge (the right
## panel), C from the centre.
## The description in its box (owner 2026-10-15: "make sure the text fits ... without flowing out of the box"): 16 of
## summary.txt's texts ran past the 504 px box at 14 (the Celt / Firbolg Way of Nature to 660) - a size smaller each
## frame until it fits (11 the least), hidden meanwhile so it doesn't flash.
const DESC_SIZES := [14, 13, 12, 11]


func _fit_desc() -> void:
	var d := _desc
	if d == null or not is_instance_valid(d):
		return
	d.modulate.a = 0.0
	for fs in DESC_SIZES:
		d.add_theme_font_size_override("normal_font_size", fs)
		await get_tree().process_frame
		await get_tree().process_frame
		if not is_instance_valid(d):
			return
		if d.get_content_height() <= d.size.y:
			break
	d.modulate.a = 1.0


func _at(node: Control, x: float, y: float, w: float, h: float, anchor := "L") -> void:
	var shift: float = {"L": 0.0, "C": (_wext - 1024.0) * 0.5, "R": _wext - 1024.0}[anchor]
	if node.get_parent() == null:                      # into the tree first: a button sized before its theme font
		_frame.add_child(node)                         # applies keeps the default font's (taller) size
	node.position = Vector2(x + shift, y)
	node.size = Vector2(w, h)


func _label(text: String, x: float, y: float, w: float, h: float, size: int, color: Color,
		align := HORIZONTAL_ALIGNMENT_LEFT, anchor := "L", wrap := false) -> Label:
	var l := Label.new()
	if wrap:                                           # before sizing: an unwrapped label grows to its text width
		l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		l.vertical_alignment = VERTICAL_ALIGNMENT_TOP
	l.text = text
	if not wrap:                                       # never overflow the box: shrink the text until it fits
		while size > 9 and _font_bold.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, size).x > w:
			size -= 1
	l.add_theme_font_size_override("font_size", size)
	l.add_theme_color_override("font_color", color)
	l.add_theme_color_override("font_shadow_color", Color(0, 0, 0, 0.9))
	l.add_theme_constant_override("shadow_offset_x", 1)
	l.add_theme_constant_override("shadow_offset_y", 1)
	l.horizontal_alignment = align
	if not wrap:
		l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	l.clip_text = false
	_at(l, x, y, w, h, anchor)
	return l


func _title(text: String) -> void:
	_label(text, 160, 660, 704, 36, 26, GOLD, HORIZONTAL_ALIGNMENT_CENTER, "C")


func _status_line() -> void:
	_status = _label("", 160, 630, 704, 26, 17, ERROR, HORIZONTAL_ALIGNMENT_CENTER, "C")


func _say(text: String) -> void:
	if _status:
		_status.text = text.to_upper()


## A medallion button (breadcrumbs.tga, normal / hover) with its caption underneath.
func _medallion(icon: String, caption: String, cx: float, cy: float, anchor: String, cb: Callable, size := 50.0,
		caption_size := 13) -> void:
	var art := _small("breadcrumbs")
	var cell: Vector2i = ICON[icon]
	var b := TextureButton.new()
	b.texture_normal = _atlas(art, Rect2(cell.x * 64, cell.y * 64, 64, 64), 4)
	b.texture_hover = _atlas(art, Rect2((cell.x + 1) * 64, cell.y * 64, 64, 64), 4)
	b.texture_pressed = b.texture_hover
	b.ignore_texture_size = true
	b.stretch_mode = TextureButton.STRETCH_KEEP_ASPECT_CENTERED
	b.tooltip_text = caption
	_at(b, cx - size * 0.5, cy - size * 0.5 - 6, size, size, anchor)
	b.pressed.connect(cb)
	_label(caption, cx - 70, cy + size * 0.5 - 8, 140, caption_size + 6, caption_size, CREAM, HORIZONTAL_ALIGNMENT_CENTER, anchor)


## A text button on the client's stone bars (misc_pieces_new.tga short bars: normal, hover, pressed, disabled).
func _bar_button(text: String, x: float, y: float, w: float, h: float, anchor: String, cb: Callable) -> Button:
	var b := Button.new()
	b.text = text
	var fs := 14                                       # shrink until the label fits (ELEMENTALIST, WAY OF THE GROVE ...)
	while fs > 7 and (_font_bold.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, fs).x > w - 10
			or _font_bold.get_height(fs) + 4 > h):            # and fits the height (RANDOM in the name box)
		fs -= 1
	b.add_theme_font_size_override("font_size", fs)
	b.clip_text = true
	_at(b, x, y, w, h, anchor)
	b.pressed.connect(cb)
	return b


func _selected(b: Button, on: bool) -> void:
	if on:                                              # the client: gold text on the normal dark bar
		b.add_theme_color_override("font_color", Color(1.0, 0.8, 0.2))
		b.add_theme_color_override("font_hover_color", Color(1.0, 0.85, 0.35))


func _bar_style(state: int) -> StyleBoxTexture:
	var s := StyleBoxTexture.new()
	s.texture = _small("misc_pieces_new")
	s.region_rect = Rect2(0, (105 + state * 22) * 4, 102 * 4, 22 * 4)
	s.set_content_margin_all(2)
	return s


func _make_theme() -> Theme:
	var t := Theme.new()
	t.default_font = _font_bold                         # owner: ALL CAPS and bold
	t.default_font_size = 16
	for cls in ["Label", "LineEdit", "Button"]:
		t.set_color("font_color", cls, CREAM)
	t.set_color("font_hover_color", "Button", GOLD_HI)
	t.set_color("font_pressed_color", "Button", Color(1.0, 0.75, 0.0))
	t.set_color("font_disabled_color", "Button", Color(0.4, 0.38, 0.35))
	t.set_stylebox("normal", "Button", _bar_style(0))
	t.set_stylebox("hover", "Button", _bar_style(1))
	t.set_stylebox("pressed", "Button", _bar_style(2))
	t.set_stylebox("disabled", "Button", _bar_style(3))
	t.set_stylebox("focus", "Button", StyleBoxEmpty.new())
	t.set_type_variation("SlotButton", "Button")
	var hl := _box(Color(1, 0.85, 0.45, 0.10), Color(0, 0, 0, 0), 0)
	for k in ["normal", "focus", "disabled"]:
		t.set_stylebox(k, "SlotButton", StyleBoxEmpty.new())
	t.set_stylebox("hover", "SlotButton", hl)
	t.set_stylebox("pressed", "SlotButton", hl)
	var edit := StyleBoxTexture.new()
	edit.texture = _small("misc_pieces_new")
	edit.region_rect = Rect2(0, 52 * 4, 160 * 4, 26 * 4)        # the black bar
	edit.set_content_margin_all(4)
	t.set_stylebox("normal", "LineEdit", edit)
	t.set_stylebox("focus", "LineEdit", edit)
	t.set_font_size("font_size", "LineEdit", 20)
	t.set_color("caret_color", "LineEdit", GOLD)
	t.set_color("font_placeholder_color", "LineEdit", Color(CREAM, 0.35))
	t.set_color("font_color", "TooltipLabel", CREAM)
	t.set_stylebox("panel", "TooltipPanel", _box(Color(0, 0, 0, 0.9), GOLD.darkened(0.3), 1))
	t.set_stylebox("panel", "AcceptDialog", _box(Color(0.03, 0.03, 0.03, 0.97), GOLD, 2))
	return t


func _box(bg: Color, border: Color, w: int) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = border
	s.set_border_width_all(w)
	s.set_content_margin_all(8)
	return s


# ---------------------------------------------------------------- art

func _atlas(tex: Texture2D, region: Rect2, k: float) -> AtlasTexture:
	var a := AtlasTexture.new()
	a.atlas = tex
	a.region = Rect2(region.position * k, region.size * k)
	return a


func _small(name: String) -> Texture2D:
	return _load_png("%s/ui/small/%s.png" % [PRE, name], "%s/ui/original_small/%s.png" % [PRE, name])


func _full(name: String) -> Texture2D:
	return _load_png("%s/ui/full/%s.png" % [PRE, name], "%s/ui/original_full/%s.png" % [PRE, name])


func _load_png(path: String, fallback: String) -> Texture2D:
	if _textures.has(path):
		return _textures[path]
	var img := Image.load_from_file(ProjectSettings.globalize_path(path))
	if img == null:
		img = Image.load_from_file(ProjectSettings.globalize_path(fallback))
	var tex: Texture2D = null
	if img:
		img.generate_mipmaps()
		tex = ImageTexture.create_from_image(img)
	_textures[path] = tex
	return tex


func _font(file: String) -> FontFile:
	var f := FontFile.new()
	f.load_dynamic_font(ProjectSettings.globalize_path("%s/fonts/%s" % [UI, file]))
	return f


# ---------------------------------------------------------------- debug

func _shots(folder: String) -> void:
	DirAccess.make_dir_recursive_absolute(folder)
	Accounts.account = "pregameshot"                   # a throwaway test account, never the owner's characters
	CharacterStore.remove(Accounts.account, "Brennan")
	var save := func(n: String):
		for i in 30:
			await get_tree().process_frame
		get_viewport().get_texture().get_image().save_png("%s/%s.png" % [folder, n])
	await save.call("1_realm")
	var shot_realm := "hib"
	for a in OS.get_cmdline_user_args():
		if a.begins_with("--shot-realm="):
			shot_realm = a.split("=", true, 1)[1]
	_pick_realm(shot_realm)
	await save.call("2_select")
	_start_create()
	draft.name = "Brennan"
	_show("create")
	await save.call("3_create")
	_name_edit.text = "Brennan"
	_create_continue()
	await save.call("4_customize")
	_customize_continue()
	await save.call("6_select_with_character")
	CharacterStore.remove(Accounts.account, "Brennan")
	get_tree().quit()


## Where the character is now (owner 2026-10-10: the list showed where characters were made): the zone of its saved
## location, or its start zone before it has played.
var _zone_names := {}


func _where(c: Dictionary) -> String:
	if String(c.get("location_text", "")) != "":         # the server's own words for where the character is
		return String(c.location_text)
	var loc = c.get("location")
	if loc is Dictionary and loc.has("zone"):
		if _zone_names.is_empty():
			var ix = JSON.parse_string(FileAccess.get_file_as_string("res://content/zones/zones_index.json")) 					if FileAccess.file_exists("res://content/zones/zones_index.json") else null
			if ix is Dictionary:
				for k in ix:
					_zone_names[int(k)] = String(ix[k].get("name", ""))
		var n := String(_zone_names.get(int(loc.zone), ""))
		if n != "":
			return n
	return String(CharacterStore.start_of(c).zone_name)
