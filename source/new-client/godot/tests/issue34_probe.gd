extends SceneTree
## Living Server issue #34 (test harness, not part of the game): on the server's game, a skill from the skills sheet
## goes onto the quickbar and is used, Stealth is in the Abilities list and hides you (UseSkill type 0, the server's
## StealthSpecHandler), and the merchant window has a SELL button. With the real GameUI, quickbars, skills sheet and
## merchant window built, a stand-in network client records what would be sent to the server. The stand-in has no
## AppraiseItem, like the 0.37c MMOEngine.dll.
##   Godot --headless --path <new-client/godot> -s res://tests/issue34_probe.gd
## Prints "[issue34] ok ..." / "[issue34] FAIL ..." per check; the exit code is the number of failed checks.
## tools/newclient-compat/installed_check.py runs it on the released 0.37c scripts with Living Server's content patches.

var fails := 0
var ui
var client
var srv
var pd


class Recorder extends Node:
	var calls: Array = []

	func UseSkill(_x, _y, _z, _speed, _heading, _in_view, index, type) -> void:
		calls.append(["UseSkill", int(index), int(type)])

	func UseSpell(_x, _y, _z, _speed, _heading, _in_view, level, line) -> void:
		calls.append(["UseSpell", int(level), int(line)])

	func SellItem(_x, _y, slot) -> void:
		calls.append(["SellItem", int(slot)])

	func BuyItem(_x, _y, slot, count, menu) -> void:
		calls.append(["BuyItem", int(slot), int(count), int(menu)])

	func Target(oid, flags) -> void:
		calls.append(["Target", int(oid), int(flags)])

	func Command(text) -> void:
		calls.append(["Command", String(text)])

	func Ping() -> void:
		pass


class FakePlayer extends Node3D:
	var velocity := Vector3.ZERO

	func facing() -> float:
		return 0.0


func check(ok: bool, what: String) -> void:
	print("[issue34] ", "ok   " if ok else "FAIL ", what)
	if not ok:
		fails += 1


func _initialize() -> void:
	# a script error stops this coroutine: never wait for ever (installed_check.py counts a missing result as failed)
	create_timer(120.0).timeout.connect(func():
		print("[issue34] FAIL the probe did not finish (a script error stopped it)")
		quit(100))
	await process_frame
	load("res://game/ui/ui_art.gd").settings_path = "user://settings_issue34.cfg"
	srv = root.get_node("Server")
	pd = root.get_node("PlayerData")
	srv.active = true
	client = Recorder.new()
	client.name = "DaocClient"
	srv.add_child(client)
	srv.client = client
	pd.character = {"name": "Issuethirtyfour", "class_name": "Infiltrator"}
	# the server's usable list (PacketLib1112.SendUpdatePlayerSkills): specs first, then abilities, styles
	srv.skills = [
		{"page": 0, "name": "Slash", "level": 20, "bonus": 0, "icon": 0, "special": 0},
		{"page": 0, "name": "Stealth", "level": 25, "bonus": 0, "icon": 0, "special": 0},
		{"page": 1, "name": "Sprint", "level": 1, "bonus": 0, "icon": 0, "special": 0},
		{"page": 1, "name": "Safe Fall", "level": 2, "bonus": 0, "icon": 0, "special": 0},
		{"page": 2, "name": "Backstab", "level": 2, "bonus": 0, "icon": 0, "special": 0},
	]
	# the zone viewer's part: Server.viewer() needs a scene with a player and a zone, and the zone viewer plays
	# PlayerData.use_skill through Server.use (zone_viewer.gd _server_use)
	var stub := GDScript.new()
	stub.source_code = "extends Node3D\nvar ui\nvar player\nvar zone = {\"info\": {\"id\": 26}}\nvar npcs\n"
	stub.reload()
	var viewer := Node3D.new()
	viewer.set_script(stub)
	root.add_child(viewer)
	current_scene = viewer
	var player := FakePlayer.new()
	viewer.add_child(player)
	viewer.set("player", player)
	pd.use_skill.connect(func(e: Dictionary):
		if not srv.use(String(e.get("id", ""))):
			print("[issue34] Server.use refused ", e))
	ui = load("res://game/ui/game_ui.gd").new()
	root.add_child(ui)
	viewer.set("ui", ui)
	await process_frame
	await process_frame
	_skills_sheet()
	_quickbar()
	_stealth_command()
	_merchant()
	# shipping 1.2's server.gd must not retitle the account vault on a 0.37c zone viewer (no _interact_npc there)
	check(not srv.has_method("_house_window_title") or String(srv._house_window_title()) == "ACCOUNT VAULT",
			"the account vault keeps its title")
	print("[issue34] ", fails, " failed")
	quit(fails)


func _rows() -> Array:
	var out: Array = []
	for n in ui.find_children("*", "", true, false):
		var d = n.get("data")
		if d is Dictionary and d.has("kind") and d.has("name") and n is Control:
			out.append(n)
	return out


func _row(name: String) -> Control:
	for r in _rows():
		if String(r.data.name) == name and is_instance_valid(r) and not r.is_queued_for_deletion():
			return r
	return null


func _skills_sheet() -> void:
	ui._set_sheet("skills")
	var stealth := _row("Stealth")
	check(stealth != null and String(stealth.data.kind) == "ability", "the Abilities list has a Stealth row")
	check(stealth != null and String(stealth.data.id) == "srv:spec:Stealth", "Stealth is used as the Stealth spec (srv:spec:Stealth)")
	var sprint := _row("Sprint")
	check(sprint != null and String(sprint.data.id) == "srv:skill:0", "Sprint is in the Abilities list (srv:skill:0)")


func _quickbar() -> void:
	var slots: Array = ui.quickbars[0]._slots
	var bank := int(pd.quickbar_bank[0])
	var sprint := _row("Sprint")
	if sprint != null:
		# what SkillRow._get_drag_data returns (data.duplicate(); its drag preview needs a real drag)
		check(slots[1]._can_drop_data(Vector2.ZERO, sprint.data.duplicate()),
				"a quickbar slot takes Sprint dragged from the Abilities list")
		slots[1]._drop_data(Vector2.ZERO, sprint.data.duplicate())
		client.calls.clear()
		slots[1].use()
		check(client.calls == [["UseSkill", 0, 1]], "the Sprint slot sends UseSkill 0, type 1: %s" % [client.calls])
	check(slots[2]._can_drop_data(Vector2.ZERO, {"kind": "style", "id": "srv:skill:2", "icon": 0, "name": "Backstab"}),
			"a quickbar slot still takes a style")
	check(slots[3]._can_drop_data(Vector2.ZERO, {"kind": "spell", "id": "srv:spell:0:1", "icon": 0, "name": "Heal"}),
			"a quickbar slot still takes a spell")
	var stealth := _row("Stealth")
	if stealth == null:
		check(false, "the Stealth quickbar checks need the Stealth row")
		return
	var drag: Dictionary = stealth.data.duplicate()
	check(slots[0]._can_drop_data(Vector2.ZERO, drag), "a quickbar slot takes Stealth dragged from the Abilities list")
	slots[0]._drop_data(Vector2.ZERO, drag)
	check(String(pd.quickbars[0][bank][0].get("id", "")) == "srv:spec:Stealth", "Stealth is on quickbar 1, slot 1")
	client.calls.clear()
	slots[0].use()
	check(client.calls == [["UseSkill", 1, 0]],
			"the Stealth slot sends UseSkill with the Stealth spec's place (1) and type 0: %s" % [client.calls])
	client.calls.clear()
	stealth._gui_input(_double_click())
	check(client.calls == [["UseSkill", 1, 0]], "a double-click on the Stealth row uses it: %s" % [client.calls])


func _double_click() -> InputEventMouseButton:
	var e := InputEventMouseButton.new()
	e.button_index = MOUSE_BUTTON_LEFT
	e.pressed = true
	e.double_click = true
	return e


func _stealth_command() -> void:
	client.calls.clear()
	var handled: bool = ui.chat._client_command("/stealth")
	check(handled and client.calls == [["UseSkill", 1, 0]], "/stealth uses the Stealth spec: %s" % [client.calls])


func _button(window: Node, text: String) -> Button:
	for b in window.find_children("*", "Button", true, false):
		if (b as Button).text == text:
			return b
	return null


func _merchant() -> void:
	var m = ui.merchant
	m.open_server("Merchant Issuethirtyfour", {0: []}, 0)
	var sell := _button(m, "SELL")
	check(sell != null and sell.visible, "the merchant window has a SELL button")
	if sell == null:
		return
	pd.inventory[45] = {"id": "srv:45", "count": 1}
	load("res://game/ui/item_db.gd").add_runtime("srv:45", {"id": "srv:45", "name": "rusty dagger", "icon": 0})
	pd.selected_slot = 45
	client.calls.clear()
	sell.pressed.emit()
	check(client.calls.has(["SellItem", 45]), "SELL sells the backpack item picked with a right-click: %s" % [client.calls])
	pd.selected_slot = -1
	client.calls.clear()
	sell.pressed.emit()
	check(not client.calls.any(func(c): return c[0] == "SellItem"), "SELL with nothing picked sends nothing")
	client.calls.clear()
	m._drop_data(Vector2.ZERO, {"kind": "item", "slot": 45, "id": "srv:45", "icon": 0, "name": "rusty dagger"})
	check(client.calls.has(["SellItem", 45]), "an item dropped on the window is sold, as before: %s" % [client.calls])
	var appraise := _button(m, "APPRAISE")
	check(appraise == null or not appraise.visible,
			"no APPRAISE button while the network client has no AppraiseItem (the 0.37c MMOEngine.dll)")
	var said: Array = []
	pd.message.connect(func(t, _k): said.append(String(t)))
	pd.appraise(45, "Merchant Issuethirtyfour")
	check(said.any(func(t): return t.begins_with("Appraising isn't available")),
			"appraising without AppraiseItem says so instead of calling it: %s" % [said])
