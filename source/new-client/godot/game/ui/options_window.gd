class_name OptionsWindow
extends ClassicWindow
## The options window (options.bmp 384x420: list area with the client's scroll track, buttons on the stone below).
## Five pages: VIDEO (resolution, window mode, frame cap - the settings the client kept on its loading /
## character-select screens - and the render quality, owner 2026-10-13: presets, view distance, shadows, lighting,
## anti-aliasing, textures, grass, reset to defaults; Graphics), AUDIO, CONTROLS (key presets RPG / FPS / MMO from
## the client's keyboard.dat, every action with two bindings, Mouse 4 / 5 and the wheel bindable, movement style),
## INTERFACE (UI scale), WINDOWS (put closed windows back - owner 2026-10-09). Spell-icon options are left out
## (owner: the client's default icon set). Saved in user://settings.cfg (keys in user://keybinds.cfg).

const PAGES := ["video", "audio", "controls", "interface", "chat", "windows"]
const PAGE_NAMES := ["VIDEO", "AUDIO", "CONTROLS", "INTERFACE", "CHAT", "WINDOWS"]
const RESOLUTIONS := [Vector2i(1280, 720), Vector2i(1366, 768), Vector2i(1600, 900), Vector2i(1920, 1080),
		Vector2i(2560, 1080), Vector2i(2560, 1440), Vector2i(3440, 1440), Vector2i(3840, 2160)]
const FPS_CAPS := [0, 30, 60, 75, 90, 120, 144, 165, 240, -1]   # 0 = monitor refresh (vsync), -1 = unlimited
const VOLUMES := [["Master", "MASTER VOLUME"], ["Ambient", "SOUND EFFECTS + AMBIENCE"], ["Music", "MUSIC"]]

signal movement_changed(mode: String)

var ui: GameUI
var page := "video"
var settings := ConfigFile.new()
var _tabs := {}
var _scroll: ScrollContainer
var _list: VBoxContainer
var _capture := []                       # [action id, slot, button] while waiting for a key
var _status: Label


func build() -> void:
	settings.load(UiArt.settings_path)
	for i in PAGES.size():
		var b := UiArt.button(PAGE_NAMES[i], Vector2(58, 18), 9)
		b.position = Vector2(9 + i * 59.5, 23)
		b.pressed.connect(show_page.bind(PAGES[i]))
		add_child(b)
		_tabs[PAGES[i]] = b
	_scroll = ScrollContainer.new()
	_scroll.position = Vector2(12, 47)
	_scroll.size = Vector2(362, 331)
	_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	var grab := StyleBoxFlat.new()
	grab.bg_color = UiArt.GOLD.darkened(0.15)
	grab.set_corner_radius_all(2)
	var sb := _scroll.get_v_scroll_bar()
	for k in ["grabber", "grabber_highlight", "grabber_pressed"]:
		sb.add_theme_stylebox_override(k, grab)
	sb.add_theme_stylebox_override("scroll", StyleBoxEmpty.new())
	sb.custom_minimum_size.x = 6
	add_child(_scroll)
	_list = VBoxContainer.new()
	_list.custom_minimum_size = Vector2(344, 0)
	_list.add_theme_constant_override("separation", 4)
	_scroll.add_child(_list)
	_status = UiArt.label("", 9, UiArt.GOLD_HI)
	_status.position = Vector2(12, 380)
	_status.size = Vector2(360, 12)
	_status.clip_text = true
	add_child(_status)
	var buttons := [["CHARACTER SELECT", _to_character_select], ["QUIT", _quit_game],
			["CLOSE", hide_window]]
	for i in buttons.size():
		var b := UiArt.button(buttons[i][0], Vector2(114, 20), 10)
		b.position = Vector2(11 + i * 121, 394)
		b.pressed.connect(buttons[i][1])
		add_child(b)
	apply_saved()
	show_page(page)


## Apply the saved video / audio settings (called once when the world loads).
## Only what the player actually chose: an unset window mode leaves the window as the game started it (it used to
## force "windowed" and the owner saw full screen flip to a window on entering the world).
## The window mode / resolution / frame cap are applied at start-up (BootSettings); here only the volumes, whose
## buses exist once the world's audio is set up.
func apply_saved() -> void:
	_apply_volumes()


func _current_mode() -> String:
	if DisplayServer.window_get_mode() in [DisplayServer.WINDOW_MODE_EXCLUSIVE_FULLSCREEN, DisplayServer.WINDOW_MODE_FULLSCREEN]:
		return "fullscreen"
	return "borderless" if DisplayServer.window_get_flag(DisplayServer.WINDOW_FLAG_BORDERLESS) else "windowed"


func show_window() -> void:
	super.show_window()
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	show_page(page)


func hide_window() -> void:
	_capture = []
	super.hide_window()


func show_page(p: String) -> void:
	settings = ConfigFile.new()
	settings.load(UiArt.settings_path)
	if not p in PAGES:
		p = "video"
	page = p
	_capture = []
	_status.text = ""
	for k in _tabs:
		_tabs[k].add_theme_color_override("font_color", Color(1, 0.8, 0.2) if k == p else UiArt.CREAM)
	for c in _list.get_children():
		c.queue_free()
	match p:
		"video": _video()
		"audio": _audio()
		"controls": _controls()
		"interface": _interface()
		"chat": _chat()
		"windows": _windows()
	_scroll.scroll_vertical = 0


# --- rows -----------------------------------------------------------------------------------------------------

func _row(text: String, control: Control) -> void:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)
	var l := UiArt.label(text, 10, UiArt.CREAM)
	l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	l.custom_minimum_size.y = 20
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	row.add_child(l)
	control.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	row.add_child(control)
	_list.add_child(row)


func _heading(text: String) -> void:
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 1)
	var l := UiArt.label(text, 12, UiArt.GOLD_HI)
	l.custom_minimum_size.y = 20
	l.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
	box.add_child(l)
	var line := ColorRect.new()
	line.color = UiArt.GOLD.darkened(0.35)
	line.custom_minimum_size = Vector2(0, 1)
	box.add_child(line)
	_list.add_child(box)


func _hint(text: String) -> void:
	var h := UiArt.label(text, 9, UiArt.DIM)
	h.autowrap_mode = TextServer.AUTOWRAP_WORD
	h.custom_minimum_size = Vector2(344, 0)
	_list.add_child(h)


func _choice(items: Array, selected: int, on_pick: Callable) -> OptionButton:
	var o := OptionButton.new()
	o.custom_minimum_size = Vector2(176, 20)
	o.focus_mode = Control.FOCUS_NONE
	o.clip_text = true
	o.fit_to_longest_item = false                          # every dropdown the same width
	o.add_theme_font_override("font", UiArt.font(true))
	o.add_theme_font_size_override("font_size", 9)
	o.add_theme_color_override("font_color", UiArt.CREAM)
	o.add_theme_color_override("font_hover_color", UiArt.GOLD_HI)
	o.add_theme_stylebox_override("normal", UiArt.bar_style(0))
	o.add_theme_stylebox_override("hover", UiArt.bar_style(1))
	o.add_theme_stylebox_override("pressed", UiArt.bar_style(2))
	o.add_theme_stylebox_override("focus", StyleBoxEmpty.new())
	for it in items:
		o.add_item(String(it).to_upper())
	o.select(clampi(selected, 0, items.size() - 1))
	o.item_selected.connect(on_pick)
	UiArt.themed(o)
	var pop := o.get_popup()
	pop.add_theme_font_override("font", UiArt.font(true))
	pop.add_theme_font_size_override("font_size", 14)
	pop.add_theme_color_override("font_color", UiArt.CREAM)
	pop.add_theme_color_override("font_hover_color", UiArt.GOLD_HI)
	var panel := StyleBoxFlat.new()
	panel.bg_color = Color(0.07, 0.065, 0.06, 0.97)
	panel.border_color = UiArt.GOLD.darkened(0.3)
	panel.set_border_width_all(1)
	panel.set_content_margin_all(4)
	pop.add_theme_stylebox_override("panel", panel)
	var hov := StyleBoxFlat.new()
	hov.bg_color = Color(0.3, 0.22, 0.1, 0.9)
	pop.add_theme_stylebox_override("hover", hov)
	return o


func _slider(value: float, lo: float, hi: float, step: float, on_change: Callable) -> HSlider:
	var s := HSlider.new()
	s.min_value = lo
	s.max_value = hi
	s.step = step
	s.value = value
	s.custom_minimum_size = Vector2(176, 20)
	s.focus_mode = Control.FOCUS_NONE
	var groove := StyleBoxFlat.new()
	groove.bg_color = Color(0.05, 0.05, 0.05)
	groove.border_color = UiArt.GOLD.darkened(0.45)
	groove.set_border_width_all(1)
	groove.content_margin_top = 2
	groove.content_margin_bottom = 2
	var fill := StyleBoxFlat.new()
	fill.bg_color = UiArt.GOLD.darkened(0.25)
	s.add_theme_stylebox_override("slider", groove)
	s.add_theme_stylebox_override("grabber_area", fill)
	s.add_theme_stylebox_override("grabber_area_highlight", fill)
	s.value_changed.connect(on_change)
	return s


func _toggle(on: bool, on_change: Callable) -> Button:
	var b := UiArt.button("ON" if on else "OFF", Vector2(70, 18), 9)
	b.toggle_mode = true
	b.button_pressed = on
	b.toggled.connect(func(v: bool):
		b.text = "ON" if v else "OFF"
		on_change.call(v))
	return b


## Re-read the file first: other parts of the UI write it too (window positions, chat boxes, quickbars), and
## writing back an old copy erased them - the owner's windows forgot their places after a quit.
func _save(section: String, key: String, value) -> void:
	settings = ConfigFile.new()
	settings.load(UiArt.settings_path)
	settings.set_value(section, key, value)
	settings.save(UiArt.settings_path)


# --- pages ----------------------------------------------------------------------------------------------------

func _video() -> void:
	_heading("DISPLAY")
	var cur := DisplayServer.window_get_size()
	var names := []
	var sel := 0
	var list := RESOLUTIONS.duplicate()
	var screen := DisplayServer.screen_get_size(DisplayServer.window_get_current_screen())
	list = list.filter(func(r: Vector2i): return r.x <= screen.x and r.y <= screen.y)
	if not list.has(screen):
		list.append(screen)
	for i in list.size():
		names.append("%d X %d" % [list[i].x, list[i].y])
		if list[i] == cur:
			sel = i
	_row("RESOLUTION", _choice(names, sel, func(i: int):
		_save("display", "resolution", [list[i].x, list[i].y])
		_apply_window(String(settings.get_value("display", "window_mode", _current_mode())), [list[i].x, list[i].y])))
	var modes := ["windowed", "fullscreen", "borderless"]
	_row("WINDOW MODE", _choice(["WINDOWED", "FULLSCREEN", "BORDERLESS WINDOWED"],
			modes.find(String(settings.get_value("display", "window_mode", _current_mode()))), func(i: int):
		_save("display", "window_mode", modes[i])
		_apply_window(modes[i], settings.get_value("display", "resolution", []))))
	var caps := []
	for c in FPS_CAPS:
		caps.append("MONITOR REFRESH (VSYNC)" if c == 0 else ("UNLIMITED (NO VSYNC)" if c < 0 else "%d FPS" % c))
	_row("FRAME RATE CAP", _choice(caps, maxi(0, FPS_CAPS.find(int(settings.get_value("display", "fps_cap", 0)))),
			func(i: int):
		_save("display", "fps_cap", FPS_CAPS[i])
		_apply_fps(FPS_CAPS[i])))
	_quality()
	# the frame-rate governor and upscalers (Perf, owner 2026-10-10)
	_heading("PERFORMANCE")
	_hint("GPU: %s" % Perf.gpu_name.to_upper())
	var targets := []
	for t in Perf.TARGETS:
		targets.append("OFF (NO CULLING, FULL DETAIL)" if t == 0 else "%d FPS" % t)
	_row("FPS TARGET", _choice(targets, maxi(0, Perf.TARGETS.find(Perf.target_fps)), func(i: int):
		_save("performance", "target_fps", Perf.TARGETS[i])
		Perf.set_option("target_fps", Perf.TARGETS[i])))
	_hint("WITH A TARGET SET, DETAIL DROPS ONLY WHILE THE GAME IS BELOW IT (A RAID CROWD): FAR SPELL EFFECTS, FAR ANIMATION RATE, NPC SHADOWS, SOUND VOICES. IT COMES BACK WHEN THE SCENE CALMS.")
	_hint("OFF: NOTHING IS CULLED OR SIMPLIFIED - FOR HARDWARE THAT CAN BRUTE FORCE A FULL RAID.")


# --- VIDEO: render quality (Graphics) ------------------------------------------------------------------------

var _preset_buttons := {}


func _quality() -> void:
	_heading("QUALITY PRESET")
	var rec: Array = Graphics.recommended()
	_hint("RECOMMENDED FOR THIS PC: %s  (%s)" % [String(rec[0]).to_upper(), String(rec[1]).to_upper()])
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 4)
	_preset_buttons = {}
	for n in Graphics.PRESET_NAMES:
		var b := UiArt.button(n.to_upper(), Vector2(65, 20), 9)
		b.pressed.connect(func():
			Graphics.use_preset(n)
			_status.text = "%s QUALITY" % n.to_upper()
			show_page("video"))
		row.add_child(b)
		_preset_buttons[n] = b
	_list.add_child(row)
	var row2 := HBoxContainer.new()
	row2.add_theme_constant_override("separation", 6)
	var use_rec := UiArt.button("USE RECOMMENDED", Vector2(168, 20), 9)
	use_rec.pressed.connect(func():
		Graphics.use_preset(String(rec[0]))
		_status.text = "%s QUALITY (RECOMMENDED)" % String(rec[0]).to_upper()
		show_page("video"))
	row2.add_child(use_rec)
	var reset := UiArt.button("RESET TO DEFAULTS", Vector2(168, 20), 9)
	reset.pressed.connect(func():
		Graphics.use_preset("defaults")
		_status.text = "GRAPHICS BACK TO THE DEFAULTS"
		show_page("video"))
	row2.add_child(reset)
	_list.add_child(row2)
	_hint("HIGH IS THE GAME'S DEFAULT. ANY CHANGE BELOW MAKES IT CUSTOM.")
	_mark_preset()

	_heading("VIEW DISTANCE")
	_gfx_slider("VIEW DISTANCE", "view_distance", 0.5, Graphics.VIEW_MAX, 0.1, func(v: float): return "%.1fX" % v)
	_hint("STRETCHES EACH ZONE'S FOG, HOW FAR OBJECTS ARE DRAWN AND HOW MANY ZONES AROUND YOU ARE LOADED. 1X IS THE ORIGINAL CLIENT'S LOOK; HIGH VALUES NEED MORE VIDEO MEMORY. DUNGEON FOG IS NEVER CHANGED.")

	_heading("SHADOWS")
	_gfx_toggle("SUN SHADOWS", "shadows")
	_gfx_choice("SHADOW DETAIL", "shadow_size", [1024, 2048, 4096, 8192],
			["LOW (1024)", "MEDIUM (2048)", "HIGH (4096)", "ULTRA (8192)"])
	_gfx_slider("SHADOW DISTANCE", "shadow_distance", 60.0, 600.0, 10.0, func(v: float): return "%d M" % int(v))
	_gfx_choice("SHADOW SOFTNESS", "shadow_filter", [0, 1, 2, 3, 4, 5],
			["0 - HARD", "1 - VERY LOW", "2 - LOW", "3 - MEDIUM", "4 - HIGH", "5 - ULTRA"])

	_heading("LIGHTING")
	_gfx_choice("AMBIENT OCCLUSION (SSAO)", "ssao", [-1, 0, 1, 2, 3, 4],
			["OFF", "VERY LOW", "LOW", "MEDIUM", "HIGH", "ULTRA"])
	_gfx_toggle("INDIRECT LIGHT (SSIL)", "ssil")
	_gfx_toggle("BLOOM", "glow")
	_gfx_choice("TONE MAPPING", "tonemap", [0, 1], ["FILMIC (ORIGINAL)", "AGX"])
	_hint("AMBIENT OCCLUSION DARKENS CREVICES AND CONTACT SHADOWS; INDIRECT LIGHT BOUNCES COLOUR OFF NEARBY SURFACES; BLOOM MAKES SPELLS AND LIGHTS GLOW; AGX TONE MAPPING GIVES RICHER SUNSETS AND SOFTER BRIGHT SPOTS. INDIRECT LIGHT, BLOOM AND AGX ARE NEW TO THE LOOK - OFF BY DEFAULT.")

	_heading("ANTI-ALIASING")
	_gfx_choice("MSAA", "msaa", [0, 2, 4, 8], ["OFF", "2X", "4X", "8X"])
	_gfx_choice("SCREEN-SPACE AA", "screen_aa", [0, 1, 2], ["OFF", "FXAA", "SMAA"])
	_gfx_toggle("TEMPORAL AA (TAA)", "taa")
	_gfx_choice("RENDER SCALE", "render_scale", [1.0, 1.25, 1.5, 1.75, 2.0],
			["100% (NATIVE)", "125%", "150%", "175%", "200%"])
	_hint("RENDER SCALE ABOVE 100% IS SUPERSAMPLING: SHARPER EDGES FOR A LOT OF GRAPHICS CARD TIME.")

	_heading("TEXTURES")
	_gfx_choice("ANISOTROPIC FILTERING", "aniso", [1, 2, 4, 8, 16], ["OFF", "2X", "4X", "8X", "16X"])
	_hint("KEEPS GROUND AND WALL TEXTURES SHARP AT LOW ANGLES.")

	_heading("GRASS")
	_gfx_slider("GRASS DISTANCE", "grass_distance", 30.0, 200.0, 5.0, func(v: float): return "%d M" % int(v))
	_gfx_slider("GRASS DENSITY", "grass_density", 0.0, 1.0, 0.05, func(v: float): return "%d%%" % roundi(v * 100.0))


## The preset buttons: the one the settings are now in gold (none: custom).
func _mark_preset() -> void:
	var cur := Graphics.current_preset()
	for n in _preset_buttons:
		var b: Button = _preset_buttons[n]
		if is_instance_valid(b):
			b.add_theme_color_override("font_color", UiArt.GOLD_HI if n == cur else UiArt.CREAM)


func _gfx_set(key: String, v: Variant) -> void:
	Graphics.set_value(key, v)
	_mark_preset()
	_status.text = "CUSTOM QUALITY" if Graphics.current_preset() == "" else "%s QUALITY" % Graphics.current_preset().to_upper()


func _gfx_toggle(caption: String, key: String) -> void:
	_row(caption, _toggle(bool(Graphics.value(key)), func(v: bool): _gfx_set(key, v)))


func _gfx_choice(caption: String, key: String, vals: Array, names: Array) -> void:
	var sel := 0
	for i in vals.size():
		if Graphics._same(vals[i], Graphics.value(key)):
			sel = i
	_row(caption, _choice(names, sel, func(i: int): _gfx_set(key, vals[i])))


## A slider with its value in the caption; applied once it stops moving (a shadow map or the grass rebuilt on every
## step of a drag would stutter).
func _gfx_slider(caption: String, key: String, lo: float, hi: float, step: float, fmt: Callable) -> void:
	var start := clampf(float(Graphics.value(key)), lo, hi)
	var s := _slider(start, lo, hi, step, func(_v: float): pass)
	_row(caption, s)
	var row := s.get_parent() as HBoxContainer
	var label := row.get_child(0) as Label
	label.text = "%s  %s" % [caption, fmt.call(start)]
	var ticket := [0]
	s.value_changed.connect(func(v: float):
		label.text = "%s  %s" % [caption, fmt.call(v)]
		ticket[0] += 1
		var mine: int = ticket[0]
		await get_tree().create_timer(0.35).timeout
		if mine == ticket[0] and is_instance_valid(s):
			_gfx_set(key, v))


func _audio() -> void:
	_heading("VOLUME")
	for v in VOLUMES:
		_row(v[1], _slider(float(settings.get_value("audio", String(v[0]).to_lower(), 100)), 0, 100, 1, func(x: float):
			_save("audio", String(v[0]).to_lower(), x)
			_apply_volumes()))


func _controls() -> void:
	_heading("MOVEMENT")
	var styles := ["modern", "classic"]
	_row("MOVEMENT STYLE", _choice(["MODERN (FULL-SPEED STRAFE)", "CLASSIC DAOC"],
			styles.find(String(settings.get_value("player", "movement", "modern"))), func(i: int):
		_save("player", "movement", styles[i])
		movement_changed.emit(styles[i])))
	_row("MOUSE SENSITIVITY", _slider(float(settings.get_value("player", "mouse_sensitivity", 1.0)), 0.2, 3.0, 0.05,
			func(x: float):
		_save("player", "mouse_sensitivity", x)
		if ui and ui.player:
			ui.player.set("mouse_sensitivity", x)))
	_row("INVERT MOUSE", _toggle(bool(settings.get_value("player", "invert_mouse", false)),
			func(v: bool):
		_save("player", "invert_mouse", v)
		if ui and ui.player:
			ui.player.set("invert_mouse", v)))
	_heading("KEY CONFIGURATION")
	# shared by every character unless this one has its own (the WoW-style override, off by default)
	_row("CHARACTER-SPECIFIC KEY BINDINGS", _toggle(Keybinds.character_specific, func(v: bool):
		Keybinds.set_character_specific(v)
		show_page.call_deferred("controls")
		_status.text = "THIS CHARACTER NOW HAS ITS OWN KEYS" if v else "THIS CHARACTER USES THE SHARED KEYS"))
	var presets := HBoxContainer.new()
	presets.add_theme_constant_override("separation", 6)
	for p in [["rpg", "RPG DEFAULT"], ["fps", "FPS DEFAULT"], ["mmo", "MMO DEFAULT"]]:
		var b := UiArt.button(p[1], Vector2(110, 18), 9)
		b.pressed.connect(func():
			Keybinds.use_preset(p[0])
			show_page("controls")
			_status.text = "%s KEYS LOADED" % p[1])
		presets.add_child(b)
	_list.add_child(presets)
	_hint("CLICK A KEY TO CHANGE IT, THEN PRESS A KEY OR MOUSE BUTTON (MOUSE 4 / 5 AND THE WHEEL WORK). ESC CANCELS, RIGHT-CLICK CLEARS.")
	var cats: Array = Keybinds.categories.duplicate()
	if not cats.has("Quickbars"):
		cats.append("Quickbars")
	for cat in cats:
		var acts: Array = Keybinds.actions.filter(func(a): return String(a.category) == String(cat))
		if acts.is_empty():
			continue
		_heading(String(cat))
		for a in acts:
			var row := HBoxContainer.new()
			row.add_theme_constant_override("separation", 4)
			var l := UiArt.label(String(a.label), 9, UiArt.CREAM)
			l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
			l.custom_minimum_size.y = 18
			l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
			l.clip_text = true
			row.add_child(l)
			var keys: Array = Keybinds.bindings.get(a.id, [])
			for slot in Keybinds.SLOTS:
				var b := UiArt.button(String(keys[slot]) if slot < keys.size() and keys[slot] != "" else "-", Vector2(92, 18), 9)
				b.pressed.connect(_start_capture.bind(String(a.id), slot, b))
				b.gui_input.connect(func(e: InputEvent):
					if e is InputEventMouseButton and e.pressed and e.button_index == MOUSE_BUTTON_RIGHT and _capture.is_empty():
						Keybinds.bind(String(a.id), slot, "")
						b.text = "-"
						b.accept_event())
				row.add_child(b)
			_list.add_child(row)


func _interface() -> void:
	_heading("INTERFACE")
	_row("UI SCALE", _slider(float(settings.get_value("interface", "ui_scale", 1.0)), 0.6, 1.6, 0.05, func(x: float):
		_save("interface", "ui_scale", x)
		if ui:
			ui.set_ui_scale(x)))
	_hint("THE INTERFACE KEEPS ITS SIZE ON SCREEN AT ANY RESOLUTION; UI SCALE MAKES ALL OF IT BIGGER OR SMALLER.")
	_heading("LOOT")
	_row("AUTO LOOT", _toggle(Loot.autoloot(), func(on: bool): Loot.set_autoloot(on)))
	_hint("ON: COINS AND ITEMS FROM YOUR KILLS GO STRAIGHT INTO YOUR BACKPACK (NOT IN 1.65 - A QUALITY OF LIFE OPTION). OFF: THEY DROP AT THE MONSTER'S FEET FOR YOU TO PICK UP, AS IN THE ORIGINAL.")
	# the bots' names and map dots (bot plan phase 3, owner 2026-10-10: all on by default, mix and match)
	_heading("BOTS")
	_row("COMPANION NAMES", _toggle(BotView.option("names_companions"), func(on: bool): _save("bots", "names_companions", on)))
	_row("GAMEBOT NAMES", _toggle(BotView.option("names_gamebots"), func(on: bool): _save("bots", "names_gamebots", on)))
	_hint("SHOW OR HIDE THE NAMES OVER YOUR COMPANIONS AND OVER THE GAMEBOTS, EACH ON ITS OWN. A BOT YOU CLICK ALWAYS SHOWS ITS NAME.")
	_row("REALM BOTS ON MAP", _toggle(BotView.option("map_dots"), func(on: bool): _save("bots", "map_dots", on)))
	_hint("YOUR REALM'S GAMEBOTS AND COMPANIONS AS GREEN DOTS ON YOUR MAP (ALSO IN DUNGEONS). ENEMY BOTS NEVER SHOW.")
	_hint("THE BOT ROSTER AND THE SERVER SETTINGS (XP RATES, BOT GOALS AND THE REST) ARE IN THE OFFLINE DAOC REBUILT LAUNCHER.")


## CHAT: which message types the chat block's two boxes show (top = chat, bottom = system), one ON / OFF per box.
func _chat() -> void:
	_heading("CHAT BOXES")
	_hint("TWO SEPARATE BOXES: COMBAT (ON TOP) AND CHAT (BELOW, WITH THE TYPING LINE). EACH MOVES AND RESIZES ON ITS OWN. CHOOSE WHAT EACH BOX SHOWS.")
	if ui == null or ui.chat == null or ui.chat_combat == null:
		return
	var head := HBoxContainer.new()
	var hl := UiArt.label("MESSAGES", 9, UiArt.GOLD_HI)
	hl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	head.add_child(hl)
	for t in ["COMBAT", "CHAT"]:
		var l := UiArt.label(t, 9, UiArt.GOLD_HI)
		l.custom_minimum_size = Vector2(74, 16)
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		head.add_child(l)
	_list.add_child(head)
	for spec in ChatWindow.TYPES:
		var kind: String = spec[0]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 4)
		var l := UiArt.label(spec[1], 9, UiArt.CREAM)
		l.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		l.custom_minimum_size.y = 18
		l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		row.add_child(l)
		for box in [ui.chat_combat, ui.chat]:
			var cw: ChatWindow = box
			row.add_child(_toggle(kind in cw.types, func(v: bool):
				var lst: Array = cw.types.duplicate()
				if v and not lst.has(kind):
					lst.append(kind)
				elif not v:
					lst.erase(kind)
				cw.set_types(lst)))
		_list.add_child(row)


## WINDOWS (owner 2026-10-09): one button per window that puts it back where it was before it was closed; RESET
## puts every window back in its default place.
func _windows() -> void:
	_heading("WINDOWS")
	_hint("OPEN OR CLOSE ANY WINDOW (LIT = OPEN). A WINDOW REOPENS WHERE IT WAS. THE PET WINDOW ALSO OPENS BY ITSELF WHEN YOU SUMMON A PET.")
	if ui == null:
		return
	var grid := GridContainer.new()
	grid.columns = 2
	grid.add_theme_constant_override("h_separation", 6)
	grid.add_theme_constant_override("v_separation", 5)
	for spec in GameUI.RESTORABLE:
		var id: String = spec[0]
		var b := UiArt.button(spec[1], Vector2(168, 20), 10)
		var open: bool = ui.windows.has(id) and ui.windows[id].visible
		if open:
			b.add_theme_color_override("font_color", UiArt.GOLD_HI)
		if id == "minipet" and PlayerData.pet.is_empty():
			b.disabled = true
			b.tooltip_text = "YOU HAVE NO PET"
		if id == "raid" and not ui.raid_active:          # opens with /raid 40 or /raid 80 (level 50)
			b.disabled = true
			b.tooltip_text = "NO RAID - LEVEL 50: /RAID 40 OR /RAID 80"
		b.pressed.connect(func():
			if ui.windows.has(id) and ui.windows[id].visible:
				ui.windows[id].hide_window()
				_status.text = "%s CLOSED" % String(spec[1])
			else:
				ui.restore_window(id)
				_status.text = "%s OPENED" % String(spec[1])
			show_page("windows"))
		grid.add_child(b)
	var pb := UiArt.button("PET BUFFS", Vector2(168, 20), 10)          # the strip under the pet window
	var pb_on: bool = ui._pet_buffs != null and ui._pet_buffs.visible
	if pb_on:
		pb.add_theme_color_override("font_color", UiArt.GOLD_HI)
	pb.pressed.connect(func():
		ui.set_pet_buffs(not pb_on)
		_status.text = "PET BUFFS %s" % ("OFF" if pb_on else "ON")
		show_page("windows"))
	grid.add_child(pb)
	_list.add_child(grid)
	var gap := Control.new()
	gap.custom_minimum_size.y = 8
	_list.add_child(gap)
	var reset := UiArt.button("RESET ALL WINDOWS", Vector2(342, 20), 10)
	reset.pressed.connect(func():
		ui.reset_layout()
		_status.text = "EVERY WINDOW IS BACK IN ITS DEFAULT PLACE"
		show_page("windows"))
	_list.add_child(reset)


# --- key capture ----------------------------------------------------------------------------------------------

func _start_capture(id: String, slot: int, b: Button) -> void:
	if not _capture.is_empty():
		_capture[2].text = _key_text(_capture[0], _capture[1])
	_capture = [id, slot, b]
	b.text = "PRESS A KEY"
	_status.text = "PRESS A KEY FOR %s" % Keybinds.label_of(id).to_upper()


func _key_text(id: String, slot: int) -> String:
	var keys: Array = Keybinds.bindings.get(id, [])
	return String(keys[slot]) if slot < keys.size() and keys[slot] != "" else "-"


func _input(event: InputEvent) -> void:
	if _capture.is_empty() or not visible:
		return
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_ESCAPE:
		_capture[2].text = _key_text(_capture[0], _capture[1])
		_capture = []
		_status.text = ""
		get_viewport().set_input_as_handled()
		return
	var pressed: bool = (event is InputEventKey and event.pressed and not event.echo) or \
			(event is InputEventMouseButton and event.pressed)
	if not pressed:
		return
	var binding := Keybinds.describe(event)
	if binding == "":
		return
	get_viewport().set_input_as_handled()
	var id: String = _capture[0]
	var slot: int = _capture[1]
	_capture = []
	var stolen := Keybinds.bind(id, slot, binding)
	_status.text = "%s = %s%s" % [Keybinds.label_of(id).to_upper(), binding.to_upper(),
			("   (TAKEN FROM %s)" % stolen.to_upper()) if stolen != "" else ""]
	show_page.call_deferred("controls")


func capturing() -> bool:
	return not _capture.is_empty()


# --- apply ----------------------------------------------------------------------------------------------------

func _apply_fps(cap: int) -> void:
	BootSettings.apply_fps(cap)


func _apply_window(mode: String, res) -> void:
	BootSettings.apply_display(mode, res)                 # one place for the window rules (game/ui/boot_settings.gd)


func _apply_volumes() -> void:
	for v in VOLUMES:
		var i := AudioServer.get_bus_index(v[0])
		if i >= 0:
			var x := float(settings.get_value("audio", String(v[0]).to_lower(), 100))
			AudioServer.set_bus_volume_db(i, linear_to_db(maxf(x / 100.0, 0.0001)))


func _to_character_select() -> void:
	if Server.active:                                      # the server's /quit: saved, then its Quit brings the
		hide_window()                                      # character screen (Server.quit_world)
		Server.quit_world(false)
		return
	PlayerData.save_character()
	get_tree().change_scene_to_file("res://game/pregame.tscn")


func _quit_game() -> void:
	if Server.active:                                      # saved by the server first, then out of the game
		hide_window()
		Server.quit_world(true)
		return
	get_tree().quit()
