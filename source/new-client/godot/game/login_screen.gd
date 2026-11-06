class_name LoginScreen
extends Control
## Account login (owner 2026-10-09): the classic launcher pared down to what an offline game needs: log in, create a
## local account, save username / password, exit. Art: the client's stone chamber (data/login.mpk smcamback) with its
## animated flames (flames.bmp, 16 frames) along the bottom, the realm emblem from the OfflineDAoC launcher, and the
## client's own Minion Pro / Myriad Pro fonts ("new old" pass: bold, readable text). Test runs skip it.

const UI := "res://content/ui"
const SKIP_FLAGS := ["probe", "shots", "tour", "bench", "skip-login"]
const FLAME_FRAMES := 16
const FLAME_FPS := 15.0
const GOLD := Color(0.86, 0.71, 0.38)
const CREAM := Color(0.93, 0.89, 0.78)
const ERROR := Color(1.0, 0.45, 0.35)

var _flames: Array = []                    # [TextureRect, AtlasTexture, phase]
var _flame_time := 0.0
var _login_box: VBoxContainer
var _create_box: VBoxContainer
var _user: LineEdit
var _pass: LineEdit
var _save_user: CheckBox
var _save_pass: CheckBox
var _new_user: LineEdit
var _new_pass: LineEdit
var _new_confirm: LineEdit
var _status: Label
var _host_label: Label
var _host_check := 0.0


## The game's window title (owner 2026-10-30: 0.38 is now the only version). Set on the root window, which keeps it
## (window_set_title alone was reset to the project name); a debug Godot binary - the one the launcher runs - adds
## " (DEBUG)" to a Window's title, so the screen's own title is set again without it once the window has applied its own.
const GAME_TITLE := "Offline DAoC Rebuilt v0.38"


static func set_game_title(w: Window) -> void:
	w.title = GAME_TITLE
	(func(): DisplayServer.window_set_title(GAME_TITLE, w.get_window_id())).call_deferred()


func _ready() -> void:
	set_game_title(get_window())
	var args := OS.get_cmdline_user_args()
	for a in args:
		if a.trim_prefix("--").split("=")[0] in SKIP_FLAGS:
			_enter_world.call_deferred()
			return
	set_anchors_preset(Control.PRESET_FULL_RECT)
	PregameMusic.play()                                  # the client's title music, through login and loading
	theme = _make_theme()
	_build_background()
	if Server.active:
		_server_login()
		return
	_build_panel()
	var saved := Accounts.saved_login()
	_save_user.button_pressed = saved.save_username
	_save_pass.button_pressed = saved.save_password
	_user.text = saved.username
	_pass.text = saved.password
	(_pass if _user.text != "" else _user).grab_focus.call_deferred()
	for a in args:                                       # debug: --login-shot=<png> saves the screen and quits
		if a.begins_with("--login-shot="):
			await get_tree().create_timer(1.5).timeout
			get_viewport().get_texture().get_image().save_png(a.split("=", true, 1)[1])
			get_tree().quit()


func _process(delta: float) -> void:
	_host_check -= delta
	if _host_check <= 0.0 and _host_label:
		_host_check = 2.0
		_show_host()
	_flame_time += delta
	for f in _flames:
		var frame := int((_flame_time * FLAME_FPS) + f[2]) % FLAME_FRAMES
		(f[1] as AtlasTexture).region = Rect2(frame * 240.0, 0.0, 240.0, 840.0)   # x4plus: 60x210 frames -> 240x840


func _tex(name: String) -> Texture2D:
	var img := Image.load_from_file(ProjectSettings.globalize_path("%s/x4plus/%s.png" % [UI, name]))
	if img == null:
		img = Image.load_from_file(ProjectSettings.globalize_path("%s/original/%s.png" % [UI, name]))
	if img == null:
		return null
	img.generate_mipmaps()
	return ImageTexture.create_from_image(img)


func _font(file: String) -> FontFile:
	var f := FontFile.new()
	f.load_dynamic_font(ProjectSettings.globalize_path("%s/fonts/%s" % [UI, file]))
	return f


func _make_theme() -> Theme:
	var t := Theme.new()
	var regular := _font("minionpro-regular.ttf")
	var bold := _font("minionpro-bold.ttf")
	t.default_font = bold                                 # owner: all upper case and bold, no mixed-case text
	t.default_font_size = 22
	for cls in ["Label", "CheckBox", "LineEdit"]:
		t.set_color("font_color", cls, CREAM)
	t.set_font("font", "Button", bold)
	t.set_font_size("font_size", "Button", 22)
	t.set_color("font_color", "Button", CREAM)
	t.set_color("font_hover_color", "Button", Color(1, 0.95, 0.8))
	t.set_color("font_pressed_color", "Button", Color(1.0, 0.75, 0.0))       # the client's pressed-button gold
	t.set_font("font", "CheckBox", bold)
	for k in ["normal", "hover", "pressed", "focus", "hover_pressed", "disabled"]:
		t.set_stylebox(k, "CheckBox", StyleBoxEmpty.new())
	t.set_icon("unchecked", "CheckBox", _check_icon(false))
	t.set_icon("checked", "CheckBox", _check_icon(true))
	t.set_constant("h_separation", "CheckBox", 10)
	t.set_color("font_hover_color", "CheckBox", Color(1, 0.95, 0.8))
	t.set_color("font_pressed_color", "CheckBox", CREAM)
	t.set_color("font_hover_pressed_color", "CheckBox", Color(1, 0.95, 0.8))
	# buttons: the in-game interface's stone bars (UiArt.bar_style - the pregame bar art, misc_pieces_new: normal /
	# hover / pressed / disabled; owner 2026-10-10 "the new old black granite art style")
	t.set_stylebox("normal", "Button", _stone_bar(0))
	t.set_stylebox("hover", "Button", _stone_bar(1))
	t.set_stylebox("pressed", "Button", _stone_bar(2))
	t.set_stylebox("hover_pressed", "Button", _stone_bar(2))
	t.set_stylebox("disabled", "Button", _stone_bar(3))
	t.set_stylebox("focus", "Button", StyleBoxEmpty.new())
	t.set_color("font_outline_color", "Button", Color(0, 0, 0, 0.9))
	t.set_constant("outline_size", "Button", 3)
	# text boxes: black wells with the client skin's grey bevel
	var edit := _box(Color(0.02, 0.02, 0.02, 0.92), Color(0.42, 0.42, 0.40), 2)
	edit.content_margin_left = 10
	edit.content_margin_right = 10
	t.set_stylebox("normal", "LineEdit", edit)
	t.set_stylebox("focus", "LineEdit", _box(Color(0.03, 0.03, 0.03, 0.95), GOLD.darkened(0.15), 2))
	t.set_color("caret_color", "LineEdit", GOLD)
	t.set_color("selection_color", "LineEdit", Color(GOLD, 0.35))
	t.set_color("font_placeholder_color", "LineEdit", Color(CREAM, 0.35))
	t.set_type_variation("Header", "Label")
	t.set_font("font", "Header", bold)
	t.set_font_size("font_size", "Header", 30)
	t.set_color("font_color", "Header", GOLD)
	t.set_color("font_shadow_color", "Header", Color(0, 0, 0, 0.9))
	t.set_constant("shadow_offset_x", "Header", 2)
	t.set_constant("shadow_offset_y", "Header", 2)
	return t


## Tick box in the skin's colours (mine: the client draws these with Windows controls): a black well with the grey
## bevel, filled gold with a dark tick when checked.
func _check_icon(on: bool) -> ImageTexture:
	var n := 24
	var img := Image.create(n, n, false, Image.FORMAT_RGBA8)
	img.fill(Color(0.42, 0.42, 0.40))
	img.fill_rect(Rect2i(2, 2, n - 4, n - 4), Color(0.02, 0.02, 0.02))
	if on:
		img.fill_rect(Rect2i(5, 5, n - 10, n - 10), GOLD)
		for i in 6:                                    # the tick
			img.fill_rect(Rect2i(7 + i, 11 + i, 2, 2), Color(0.1, 0.07, 0.03))
		for i in 7:
			img.fill_rect(Rect2i(12 + i, 16 - i * 1.4, 2, 2), Color(0.1, 0.07, 0.03))
	return ImageTexture.create_from_image(img)


## A piece of x4 interface art at half size (the login screen draws in screen pixels; half the x4 art gives the
## in-game windows' bevel weight at 1080p).
func _half(path: String, r: Rect2i) -> ImageTexture:
	var img := Image.load_from_file(ProjectSettings.globalize_path(path))
	if img == null:
		return null
	var piece := img.get_region(r)
	piece.resize(r.size.x / 2, r.size.y / 2, Image.INTERPOLATE_LANCZOS)
	piece.generate_mipmaps()
	return ImageTexture.create_from_image(piece)


## The black granite window (UiArt.confirm's chat box frame, chatext, as a nine-patch).
func _granite() -> StyleBox:
	var t := _half("res://content/ui/classic/x4plus/chatext.png", Rect2i(0, 256, 2048, 256))
	if t == null:
		return _box(Color(0.0, 0.0, 0.0, 0.85), GOLD, 2)
	# no resize grip on this box (owner 2026-10-10): the chat frame's top-right grip (x 1004-1021, y 0-19 at this
	# size) covered with the plain right border below it, and the top edge from further along
	var img := t.get_image()
	img.blit_rect(img.duplicate(), Rect2i(996, 30, 28, 24), Vector2i(996, 2))
	img.blit_rect(img.duplicate(), Rect2i(900, 0, 20, 2), Vector2i(1000, 0))
	img.generate_mipmaps()
	t = ImageTexture.create_from_image(img)
	var s := StyleBoxTexture.new()
	s.texture = t
	s.texture_margin_left = 6
	s.texture_margin_top = 24
	s.texture_margin_right = 24
	s.texture_margin_bottom = 6
	s.content_margin_left = 34
	s.content_margin_right = 34
	s.content_margin_top = 24
	s.content_margin_bottom = 20
	return s


func _stone_bar(state: int) -> StyleBox:
	var t := _half("res://content/pregame/ui/small/misc_pieces_new.png", Rect2i(0, (105 + state * 22) * 4, 102 * 4, 22 * 4))
	if t == null:
		return _box(Color(0.16, 0.13, 0.09, 0.95), GOLD.darkened(0.35), 2)
	var s := StyleBoxTexture.new()
	s.texture = t
	s.texture_margin_left = 10
	s.texture_margin_right = 10
	s.texture_margin_top = 6
	s.texture_margin_bottom = 6
	s.content_margin_left = 14
	s.content_margin_right = 14
	s.content_margin_top = 6
	s.content_margin_bottom = 6
	return s


func _box(bg: Color, border: Color, w: int) -> StyleBoxFlat:
	var s := StyleBoxFlat.new()
	s.bg_color = bg
	s.border_color = border
	s.set_border_width_all(w)
	s.set_corner_radius_all(2)
	s.content_margin_left = 14
	s.content_margin_right = 14
	s.content_margin_top = 6
	s.content_margin_bottom = 6
	return s


func _build_background() -> void:
	var bg := ColorRect.new()
	bg.color = Color.BLACK
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var stone := TextureRect.new()
	stone.texture = _tex("login_background")
	stone.set_anchors_preset(Control.PRESET_FULL_RECT)
	stone.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	stone.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_COVERED
	add_child(stone)
	# the flames: a row of the client's 16-frame flame along the bottom, each starting on a different frame, added
	# over the stone (the art is on black, so additive blending keeps only the fire and the knotwork band)
	var sheet := _tex("login_flames")
	var row := HBoxContainer.new()
	row.set_anchors_preset(Control.PRESET_BOTTOM_WIDE)
	row.offset_top = -220
	row.add_theme_constant_override("separation", 0)
	row.alignment = BoxContainer.ALIGNMENT_CENTER
	var add := CanvasItemMaterial.new()
	add.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD
	row.material = add
	add_child(row)
	var count := 40
	for i in count:
		var at := AtlasTexture.new()
		at.atlas = sheet
		at.region = Rect2(0, 0, 240, 840)
		var tr := TextureRect.new()
		tr.texture = at
		tr.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		tr.stretch_mode = TextureRect.STRETCH_SCALE
		tr.custom_minimum_size = Vector2(55, 220)
		tr.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		tr.use_parent_material = true
		row.add_child(tr)
		_flames.append([tr, at, float((i * 7) % FLAME_FRAMES)])


func _build_panel() -> void:
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	center.offset_bottom = -120                        # sit above the flames
	add_child(center)
	var column := VBoxContainer.new()
	column.add_theme_constant_override("separation", 14)
	center.add_child(column)
	var emblem := TextureRect.new()
	emblem.texture = _tex("realm_emblem")
	emblem.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	emblem.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	emblem.custom_minimum_size = Vector2(200, 200)
	column.add_child(emblem)
	var title := Label.new()
	title.theme_type_variation = "Header"
	title.text = "DARK AGE OF CAMELOT"
	title.add_theme_font_size_override("font_size", 46)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	column.add_child(title)

	var panel := PanelContainer.new()
	panel.add_theme_stylebox_override("panel", _granite())
	panel.custom_minimum_size = Vector2(460, 0)
	column.add_child(panel)
	var stack := VBoxContainer.new()
	panel.add_child(stack)

	# --- log in
	_login_box = VBoxContainer.new()
	_login_box.add_theme_constant_override("separation", 10)
	stack.add_child(_login_box)
	_login_box.add_child(_header("ACCOUNT LOGIN"))
	_user = _field(_login_box, "ACCOUNT NAME", false)
	_pass = _field(_login_box, "PASSWORD", true)
	_user.text_submitted.connect(func(_t): _pass.grab_focus())
	_pass.text_submitted.connect(func(_t): _on_login())
	var checks := HBoxContainer.new()
	checks.add_theme_constant_override("separation", 24)
	_save_user = CheckBox.new()
	_save_user.text = "SAVE ACCOUNT NAME"
	_save_pass = CheckBox.new()
	_save_pass.text = "SAVE PASSWORD"
	_save_pass.toggled.connect(func(on: bool): if on: _save_user.button_pressed = true)
	checks.add_child(_save_user)
	checks.add_child(_save_pass)
	_login_box.add_child(checks)
	var buttons := HBoxContainer.new()
	buttons.add_theme_constant_override("separation", 12)
	buttons.add_child(_button("LOG IN", _on_login, true))
	buttons.add_child(_button("CREATE ACCOUNT", func(): _show_create(true)))
	buttons.add_child(_button("EXIT", func(): get_tree().quit()))
	_login_box.add_child(buttons)

	# --- create account
	_create_box = VBoxContainer.new()
	_create_box.add_theme_constant_override("separation", 10)
	_create_box.visible = false
	stack.add_child(_create_box)
	_create_box.add_child(_header("CREATE ACCOUNT"))
	_new_user = _field(_create_box, "ACCOUNT NAME (LETTERS AND NUMBERS)", false)
	_new_pass = _field(_create_box, "PASSWORD", true)
	_new_confirm = _field(_create_box, "CONFIRM PASSWORD", true)
	_new_user.text_submitted.connect(func(_t): _new_pass.grab_focus())
	_new_pass.text_submitted.connect(func(_t): _new_confirm.grab_focus())
	_new_confirm.text_submitted.connect(func(_t): _on_create())
	var cbuttons := HBoxContainer.new()
	cbuttons.add_theme_constant_override("separation", 12)
	cbuttons.add_child(_button("CREATE", _on_create, true))
	cbuttons.add_child(_button("BACK", func(): _show_create(false)))
	_create_box.add_child(cbuttons)

	_status = Label.new()
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_status.autowrap_mode = TextServer.AUTOWRAP_WORD
	_status.custom_minimum_size = Vector2(0, 30)
	stack.add_child(_status)

	var version := Label.new()
	_host_label = version
	_show_host()
	version.add_theme_font_override("font", _font("myriadpro-semibold.ttf"))
	version.add_theme_font_size_override("font_size", 15)
	version.set_anchors_preset(Control.PRESET_TOP_RIGHT)
	version.offset_left = -220
	version.offset_top = 10
	version.offset_right = -14
	version.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	add_child(version)


## Top right: the version (Offline DAoC Rebuilt - the launcher runs the server).
func _show_host() -> void:
	_host_label.text = "OFFLINE DAOC REBUILT V0.36C"
	_host_label.add_theme_color_override("font_color", Color(CREAM, 0.55))


func _header(text: String) -> Label:
	var l := Label.new()
	l.theme_type_variation = "Header"
	l.text = text
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	return l


func _field(parent: Control, placeholder: String, secret: bool) -> LineEdit:
	var e := LineEdit.new()
	e.placeholder_text = placeholder
	e.secret = secret
	e.secret_character = "•"
	e.custom_minimum_size = Vector2(0, 44)
	e.max_length = 64
	parent.add_child(e)
	return e


func _button(text: String, on_press: Callable, main := false) -> Button:
	var b := Button.new()
	b.text = text
	b.custom_minimum_size = Vector2(0, 46)
	b.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	if main:
		b.add_theme_color_override("font_color", GOLD.lightened(0.25))
	b.pressed.connect(on_press)
	return b


func _say(text: String, ok := false) -> void:
	_status.text = text.to_upper()
	_status.add_theme_color_override("font_color", GOLD if ok else ERROR)


func _show_create(on: bool) -> void:
	_login_box.visible = not on
	_create_box.visible = on
	_say("")
	if on:
		_new_user.text = _user.text
		_new_pass.text = ""
		_new_confirm.text = ""
		_new_user.grab_focus()
	else:
		_user.grab_focus()


func _on_create() -> void:
	var err := Accounts.create(_new_user.text, _new_pass.text, _new_confirm.text)
	if err != "":
		_say(err)
		return
	_user.text = _new_user.text.strip_edges()
	_pass.text = ""
	_show_create(false)
	_say("Account %s created. Enter your password to log in." % _user.text, true)
	_pass.grab_focus()


func _on_login() -> void:
	if _user.text.strip_edges() == "" or _pass.text == "":
		_say("Enter your account name and password.")
		return
	var err := Accounts.login(_user.text, _pass.text)
	if err != "":
		_say(err)
		return
	Accounts.remember(_user.text.strip_edges(), _pass.text, _save_user.button_pressed, _save_pass.button_pressed)
	_say("Welcome, %s." % Accounts.account, true)
	_enter_world()


## Started by the Offline DAoC Rebuilt launcher: no account screen - the launcher's account (account.txt) logs in to
## its server by itself, as connect.exe did for game.dll, and the game goes straight to realm / character select.
## A failed login says why; Escape closes the game.
func _server_login() -> void:
	var center := CenterContainer.new()
	center.set_anchors_preset(Control.PRESET_FULL_RECT)
	center.offset_bottom = -120
	add_child(center)
	_status = Label.new()
	_status.theme_type_variation = "Header"
	_status.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	center.add_child(_status)
	_say("Connecting to the server...", true)
	Server.logged_in.connect(func(acc: String):
		Accounts.account = acc
		_enter_world(), CONNECT_ONE_SHOT)
	# the server's reason stays up: it closes the connection right after a refusal, and "Disconnected: the server
	# closed the connection" hid it (owner 2026-10-15 - the reason was "being logged out, try back in 1 minute")
	var refused := [false]
	# owner 2026-10-29: after a frozen game was ended in Task Manager the account "stayed logged in" - the server keeps
	# a lost connection's character in the world for 60 s (link-dead, GamePlayer SECONDS_TO_QUIT_ON_LINKDEATH) and
	# refuses the account meanwhile. The game now asks again every RETRY_S until it is let in (the reason stays up).
	var tries := [0]
	Server.login_failed.connect(func(why: String):
		refused[0] = true
		var held: bool = why == String(Server.LOGIN_ERRORS.get(17, "")) or why == String(Server.LOGIN_ERRORS.get(11, ""))
		if held and tries[0] < RETRY_MAX:
			tries[0] += 1
			_say("%s
Your last session is still closing - trying again in %d seconds (%d of %d).  (Escape to close)" % [
					why, int(RETRY_S), tries[0], RETRY_MAX])
			get_tree().create_timer(RETRY_S).timeout.connect(func():
				refused[0] = false
				_say("Connecting to the server...", true)
				Server.start())
		else:
			_say("%s  (Escape to close)" % why))
	Server.disconnected.connect(func(why: String):
		if not refused[0]:
			_say("Disconnected: %s  (Escape to close)" % why))
	Server.start.call_deferred()


const RETRY_S := 10.0
const RETRY_MAX := 9                              # (90 s: past the server's 60 s link-death hold)


func _enter_world() -> void:
	var test := false                                    # test runs go straight into the world
	for a in OS.get_cmdline_user_args():
		if a.trim_prefix("--").split("=")[0] in SKIP_FLAGS:
			test = true
	get_tree().change_scene_to_file("res://game/zone_viewer.tscn" if test else "res://game/pregame.tscn")


func _unhandled_input(event: InputEvent) -> void:
	if Server.active and event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE:
		get_tree().quit()
		return
	if event is InputEventKey and event.pressed and event.keycode == KEY_ESCAPE and _create_box and _create_box.visible:
		_show_create(false)
