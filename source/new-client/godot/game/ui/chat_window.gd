class_name ChatWindow
extends ClassicWindow
## One chat box. The game has two, as separate windows (owner 2026-10-09: "two separate boxes like the new
## version" - default.ini ChatWindow0 "Main" and ChatWindow1 "Combat"):
##   Main   - chat (say, tells, group, guild ...) with the typing line at its bottom
##   Combat - combat, spells, loot and system messages (the client's "PageUpSys / PageDownSys" box; patch 1.65 added
##            a toggleable "Resists Changed" channel to it)
## Each box moves by its corner handle, resizes by its top or right edge, and shows the message types chosen for it
## in Options > CHAT (saved in settings [chat_<id>]). The client keeps 65 per-channel switches per chat window
## (default.ini ChatChannels0 / 1, SystemMessages0 / 1); its names for them are in its text tables (not decoded
## yet), so the type names below are ours, grouped like the client's chat tabs.
## Placement (owner 2026-10-09): the combat box on top, the chat box below it at the bottom left. Frame: chatext.bmp
## holds two stacked 512x64 boxes; Combat uses the upper one, Main the lower, each as a nine-patch.

const TYPES := [
	["say", "SAY"], ["send", "TELLS (/SEND)"], ["group", "GROUP"], ["guild", "GUILD"], ["officer", "OFFICER"],
	["alliance", "ALLIANCE"], ["broadcast", "BROADCAST"], ["emote", "EMOTES"], ["advice", "ADVICE"],
	["chatgroup", "CHAT GROUP"], ["help", "HELP / COMMANDS"], ["system", "SYSTEM MESSAGES"], ["combat", "YOUR COMBAT"],
	["combat_others", "OTHERS' COMBAT"], ["damage_taken", "DAMAGE TAKEN"], ["spells", "SPELLS"],
	["resists", "RESISTS CHANGED"], ["loot", "LOOT / MONEY"], ["merchant", "MERCHANTS / TRADE"], ["error", "ERRORS"],
	["npc", "NPC SPEECH"]]
const DEFAULTS := {
	"main": ["say", "send", "group", "guild", "officer", "alliance", "broadcast", "emote", "advice", "chatgroup", "help",
			"npc"],
	"combat": ["system", "combat", "combat_others", "damage_taken", "spells", "resists", "loot", "merchant", "error"]}
const COLORS := {"system": Color(0.95, 0.9, 0.6), "say": Color(1, 1, 1), "help": Color(0.6, 0.85, 1.0),
		"error": Color(1.0, 0.5, 0.4), "loot": Color(0.6, 1.0, 0.6), "send": Color(0.95, 0.6, 1.0),
		"group": Color(0.55, 0.85, 1.0), "guild": Color(0.5, 1.0, 0.5), "emote": Color(1.0, 0.85, 0.6),
		"combat": Color(1.0, 0.75, 0.55), "spells": Color(0.75, 0.75, 1.0)}

var role := "main"                            # "main" (with the typing line) or "combat"
var types: Array = []
var _log: RichTextLabel
var _input: LineEdit


## The box frame: the upper chatext box for Main, the lower for Combat, stretched as a nine-patch.
func set_frame(frame_bitmap: String, frame_rect := Rect2()) -> void:
	frame_name = frame_bitmap
	if frame:
		frame.queue_free()
	var holder := Control.new()
	holder.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.scale = Vector2(0.25, 0.25)
	var np := NinePatchRect.new()
	np.texture = UiArt.tex(frame_bitmap)
	np.region_rect = Rect2(0, (0 if role == "combat" else 64) * UiArt.K, 512 * UiArt.K, 64 * UiArt.K)
	var sides := [SIDE_LEFT, SIDE_TOP, SIDE_RIGHT, SIDE_BOTTOM]
	var m := [3, 12, 12, 3]
	for i in 4:
		np.set_patch_margin(sides[i], int(m[i] * UiArt.K))
	np.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.add_child(np)
	add_child(holder)
	move_child(holder, 0)
	frame = holder
	if size == Vector2.ZERO:
		size = Vector2(312, 120)
	_fit_frame()


func _fit_frame() -> void:
	if frame and frame.get_child_count() > 0:
		frame.size = size * 4.0
		(frame.get_child(0) as Control).size = size * 4.0
	_layout()


func build() -> void:
	closable = role != "main"
	min_size = Vector2(160, 50)
	max_size = Vector2(900, 480)
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)
	types = cfg.get_value("chat_" + window_id, "types", (DEFAULTS[role] as Array).duplicate())
	_log = RichTextLabel.new()
	_log.scroll_following = true
	_log.bbcode_enabled = true
	_log.add_theme_font_override("normal_font", UiArt.font(false))
	_log.add_theme_font_override("bold_font", UiArt.font(true))
	_log.add_theme_font_size_override("normal_font_size", 10)
	_log.add_theme_font_size_override("bold_font_size", 10)
	_log.add_theme_color_override("default_color", UiArt.CREAM)
	_log.mouse_filter = Control.MOUSE_FILTER_PASS
	UiArt.themed(_log)
	add_child(_log)
	if role == "main":
		_input = LineEdit.new()
		_input.add_theme_font_override("font", UiArt.font(true))
		_input.add_theme_font_size_override("font_size", 9)
		_input.add_theme_color_override("font_color", UiArt.CREAM)
		var box := StyleBoxFlat.new()
		box.bg_color = Color(0, 0, 0, 0.6)
		box.set_content_margin_all(1)
		_input.add_theme_stylebox_override("normal", box)
		_input.add_theme_stylebox_override("focus", box)
		_input.visible = false
		_input.text_submitted.connect(_send)
		_input.gui_input.connect(func(e: InputEvent):
			if e is InputEventKey and e.pressed and e.keycode == KEY_ESCAPE:
				stop_typing()
				_input.accept_event())
		UiArt.themed(_input)
		add_child(_input)
	resized_by_user.connect(_layout)
	_layout()


func _layout() -> void:
	if _log == null:
		return
	_log.position = Vector2(8, 12)
	_log.size = Vector2(size.x - 22, size.y - (34 if _input else 16))
	if _input:
		_input.position = Vector2(6, size.y - 19)
		_input.size = Vector2(size.x - 20, 15)


func set_types(t: Array) -> void:
	types = t
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)
	cfg.set_value("chat_" + window_id, "types", types)
	cfg.save(UiArt.settings_path)


func typing() -> bool:
	return _input != null and _input.visible and _input.has_focus()


func start_typing(prefill := "") -> void:
	if _input == null:
		return
	_input.visible = true
	_input.text = prefill
	_input.grab_focus()
	_input.caret_column = prefill.length()


func stop_typing() -> void:
	if _input == null:
		return
	_input.release_focus()
	_input.visible = false
	_input.text = ""


## A message of one type; shown only if this box has that type switched on (GameUI sends every message to both).
func add_line(text: String, kind := "system") -> void:
	if not kind in types:
		return
	var c: Color = COLORS.get(kind, UiArt.CREAM)
	_log.append_text("[color=#%s]%s[/color]\n" % [c.to_html(false), text.replace("[", "[lb]")])


func scroll(pages: int) -> void:
	var sb := _log.get_v_scroll_bar()
	sb.value += pages * _log.size.y * 0.9


## /macro <name> <command>: the client's own macro command (owner 2026-10-11: "/macro bard /spawn bard") - a macro
## in the first empty quickbar slot. True when the line was a /macro.
func _macro(text: String) -> bool:
	var parts := text.split(" ", false, 2)
	if parts.is_empty() or String(parts[0]).to_lower() != "/macro":
		return false
	if parts.size() < 3:
		PlayerData.message.emit("Usage: /macro <name> <command>", "help")
		return true
	# the macro on the mouse pointer, as the client gave it: click a quickbar slot to put it there (owner 2026-10-11)
	var name := String(parts[1]).substr(0, PlayerData.MACRO_NAME_MAX)
	var preview := Panel.new()
	preview.size = Vector2(32, 32)
	var sb := StyleBoxFlat.new()
	sb.bg_color = Color(0, 0, 0, 0.6)
	sb.border_color = UiArt.GOLD_HI
	sb.set_border_width_all(1)
	preview.add_theme_stylebox_override("panel", sb)
	var l := UiArt.label(name.to_upper(), 7, UiArt.GOLD_HI)
	l.size = preview.size
	l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	preview.add_child(l)
	force_drag({"kind": "macro", "id": String(parts[2]), "icon": -1, "name": name, "border": -1}, preview)
	PlayerData.message.emit("Macro %s created. Place it on a quickbar." % name, "system")
	return true


## The old client's own commands (game.dll's command table: &face &stick &follow &qbar &realmwar &rw &quit ...), handled
## here in the server's game - the server has none of them and answered "no such command" or took another command
## that starts the same way. /quit and /logout: the server's /quit (sit, its countdown, saved, the character screen).
func _client_command(text: String) -> bool:
	if not text.begins_with("/"):
		return false
	var parts := text.substr(1).split(" ", false)
	var cmd := String(parts[0]).to_lower() if parts.size() > 0 else ""
	var rest := text.substr(cmd.length() + 1).strip_edges()
	match cmd:
		"follow", "stick", "face":
			PlayerData.track_requested.emit(cmd)
		"realmwar", "rw":
			PlayerData.window_toggle.emit("warmap")
		"craft" when Server.active and rest == "":       # the tradeskill window (the original's own /craft;
			PlayerData.window_toggle.emit("craft")         # owner 2026-10-30)
		"qbar":
			var ui := get_parent().get_parent() as GameUI
			var args := rest.split(" ", false)
			if args.size() == 0 or ui == null:
				PlayerData.message.emit("Usage: /qbar [bank#] [quickbar#(defaults to 1)]", "help")
			else:
				var qb := clampi(int(args[1]) if args.size() > 1 else 1, 1, 3)
				ui.quickbars[qb - 1].set_bank(clampi(int(args[0]), 1, 10) - 1)
		"quit", "q", "logout":
			Server.quit_world(false)
		"train" when rest == "":                            # the trainer window, anywhere (TrainWindowHandler; owner
			Server.client.call("TrainWindow")              # 2026-10-14: /train didn't open it away from a trainer)
		"stealth", "hide" when Server.spec_index("Stealth") >= 0:   # hide / come out: the Stealth spec used, as its
			Server.use("srv:spec:Stealth")                  # Abilities row (the server has no /stealth - owner 2026-10-28,
			                                                  # GitHub issue #16: "unrecognized command")
		_:
			return false
	return true


## A line typed by a macro (the quickbar): as if typed here.
func run_line(text: String) -> void:
	_send(text)


func _send(text: String) -> void:
	stop_typing()
	text = text.strip_edges()
	if text == "":
		return
	if _macro(text):                                       # a client command: never sent to the server
		return
	if text.to_lower() == "/loc" or text.to_lower().begins_with("/loc "):
		# the client's own /loc (game.dll "%s: loc=%d,%d,%d dir=%d"), never the server's - which has none and took it
		# for /loco (owner 2026-10-14: "/loc ... shows loc in chat clearly like the old client did")
		var v := get_tree().current_scene
		if v and v.has_method("loc_line"):
			PlayerData.message.emit(String(v.call("loc_line")), "system")
		return
	if Server.active:                                      # the server's game: every line and command goes to it, as the old
		if not _client_command(text):                      # client sent them (its replies come back as server messages) -
			Server.command(text)                           # except the client's own commands
		return
	var ui := get_parent().get_parent() as GameUI
	var say := func(t: String, kind: String):
		if ui:
			ui.message(t, kind)
		else:
			add_line(t, kind)
	if not text.begins_with("/"):
		say.call("You say, \"%s\"" % text, "say")
		PlayerData.said.emit(text)
		return
	var parts := text.substr(1).split(" ", false)
	var cmd := String(parts[0]).to_lower() if parts.size() > 0 else ""
	var rest := text.substr(cmd.length() + 1).strip_edges()
	match cmd:
		"help":
			say.call("Commands: /say, /emote, /quit, /loc, /time, /release, /task, /qbar [bank] [quickbar]", "help")
		"say", "s":
			say.call("You say, \"%s\"" % rest, "say")
			PlayerData.said.emit(rest)
		"task":                                            # the 1.65 tasks: ask the targeted NPC / your task (TaskSystem)
			PlayerData.task_command.emit(rest)
		"search":                                          # a quest step's search area (QuestEngine)
			PlayerData.search_requested.emit()
		"emote", "em", "e":
			say.call("%s %s" % [PlayerData.display_name(), rest], "emote")
		"quit", "q", "exit":
			get_tree().quit()
		"time":
			var t := Time.get_time_dict_from_system()
			say.call("It is %02d:%02d." % [t.hour, t.minute], "system")
		"release", "rel":                                  # after death: back to the bind point (Combat)
			PlayerData.release_requested.emit()
		"stealth", "hide":                                  # the Stealth spec's hide / come out
			PlayerData.use_skill.emit({"kind": "ability", "id": "stealth"})
		"realmwar":                                        # the realm war map (New Frontiers' command)
			PlayerData.window_toggle.emit("warmap")
		"disband":
			PlayerData.disband_requested.emit()
		"follow", "stick", "face":                         # your target (patch notes 2001)
			PlayerData.track_requested.emit(cmd)
		"formation":                                       # Bonedancer minions (the old Formation command)
			var kind := rest.strip_edges().to_lower()
			if String(PlayerData.character.get("class_name", "")) != "Bonedancer":
				say.call("Only Bonedancers can use this command!", "system")
			elif kind == "":
				say.call("Formation commands:", "system")
				say.call("'/formation triangle' Place the pets in a triangle formation.", "system")
				say.call("'/formation line' Place the pets in a line formation.", "system")
				say.call("'/formation protect' Place the pets in a protect formation that surrounds the commander.", "system")
			elif kind in ["triangle", "line", "protect"]:
				PlayerData.pet_order.emit("formation:" + kind)
			else:
				say.call("Unrecognized formation type.", "system")
		"invite":
			PlayerData.invite_requested.emit(rest)
		"pull":                                            # your aggressive companions go for your target (owner)
			var scene := get_tree().current_scene
			var npcs = scene.get("npcs") if scene else null
			var t: Npc = npcs.target if npcs is NpcManager and npcs.target and is_instance_valid(npcs.target) else null
			if t == null or not t.state.alive or int(t.spawn.realm) != 0:
				say.call("You must target a monster to pull.", "system")
			elif WorldLink.bots == null:
				say.call("You have no companions with you.", "system")
			else:
				var n := int(WorldLink.bots.call("Pull", t.state))
				if n == 0:
					say.call("None of your companions with you is set to aggressive.", "system")
				else:
					say.call("Your companions go after the %s." % t.spawn.name, "system")
		"pray":                                            # at your gravestone: the release experience back
			PlayerData.pray_requested.emit()
		"bind":                                            # at a bind stone (GamePlayer.Bind)
			PlayerData.bind_requested.emit()
		"dismount":                                        # off a stable horse
			PlayerData.dismount_requested.emit()
		"sit":
			PlayerData.sit_requested.emit(true)
		"stand":
			PlayerData.sit_requested.emit(false)
		"qbar":                                            # patch 1.54: "/qbar 2 switches to your second quickbar"
			var args := rest.split(" ", false)
			if args.size() == 0 or ui == null:
				say.call("Usage: /qbar [bank#] [quickbar#(defaults to 1)]", "help")
			else:
				var qb := clampi(int(args[1]) if args.size() > 1 else 1, 1, 3)
				ui.quickbars[qb - 1].set_bank(clampi(int(args[0]), 1, 10) - 1)
		_:
			if Combat.emotes().has(cmd):
				PlayerData.emote_requested.emit(cmd)
			else:
				say.call("Unknown command: /%s" % cmd, "error")
