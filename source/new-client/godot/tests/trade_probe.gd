extends SceneTree
## Living Server 1.2, trading with gamebots (test harness, not part of the game): the trade window the new client did not
## have. With the real GameUI built and a stand-in network client that records what would be sent, the server's raw
## TradeWindow packet (0xEA, laid out as PacketLib1109.SendTradeWindow writes it, the partner's item as
## PacketLib1124.WriteItemData) opens the window; the partner's goods and its word come as system lines; every button is a
## /tradewindow command; the close packet (40 zero bytes) closes it.
##   Godot --headless --path <new-client/godot> -s res://tests/trade_probe.gd
## Prints "[trade] ok ..." / "[trade] FAIL ..." per check; the exit code is the number of failed checks.
## tools/newclient-compat/installed_check.py runs it on the released 0.37c scripts with Living Server's content patches.

var fails := 0
var ui
var client
var srv
var pd


class Recorder extends Node:
	var calls: Array = []

	func Command(text) -> void:
		calls.append(String(text))

	func Detail(_t, _i) -> void:
		pass

	func Target(_oid, _flags) -> void:
		pass

	func Ping() -> void:
		pass


func check(ok: bool, what: String) -> void:
	print("[trade] ", "ok   " if ok else "FAIL ", what)
	if not ok:
		fails += 1


static func _short(b: PackedByteArray, v: int) -> void:
	b.append((v >> 8) & 0xFF)
	b.append(v & 0xFF)


static func _money(b: PackedByteArray, copper: int) -> void:
	_short(b, 0)
	_short(b, copper / 10000000000 % 1000)
	_short(b, copper / 10000000 % 1000)
	_short(b, copper / 10000 % 1000)
	_short(b, copper / 100 % 100)
	_short(b, copper % 100)


static func _pascal(b: PackedByteArray, s: String) -> void:
	var t := s.to_ascii_buffer()
	b.append(t.size())
	b.append_array(t)


## PacketLib1109.SendTradeWindow with one partner item (PacketLib1124.WriteItemData after its slot byte).
static func trade_packet(my_slots: Array, my_money: int, their_money: int, title: String) -> PackedByteArray:
	var b := PackedByteArray()
	for i in 10:
		b.append(int(my_slots[i]) if i < my_slots.size() else 0)
	_money(b, my_money)
	_money(b, their_money)
	_short(b, 0)
	b.append(1)                                          # partner items
	b.append(1)
	b.append(0)                                          # repair
	b.append(0)                                          # combine
	b.append(41)                                         # the item's slot
	_short(b, 0)                                         # unique id
	b.append(28)                                         # level
	b.append(15)                                         # dps / af
	b.append(0)                                          # spd / abs
	b.append(0)                                          # hand
	b.append(34)                                         # object type (Studded), damage type 0
	b.append(0)
	_short(b, 40)                                        # weight
	b.append(100)                                        # condition
	b.append(100)                                        # durability
	b.append(96)                                         # quality
	b.append(0)                                          # bonus
	b.append(0)                                          # bonus level
	_short(b, 136)                                       # model
	b.append(0)                                          # extension
	_short(b, 0)                                         # colour
	b.append(0)                                          # flag (no spells)
	_short(b, 0)                                         # effect
	_pascal(b, "Studded Vest")
	_pascal(b, title)
	return b


func _say(text: String) -> void:
	srv._on_world("message", {"type": 0, "text": text, "popup": false, "chat": false})


func _sent(text: String) -> bool:
	return client.calls.has("&tradewindow " + text)


func _initialize() -> void:
	create_timer(120.0).timeout.connect(func():
		print("[trade] FAIL the probe did not finish (a script error stopped it)")
		quit(100))
	await process_frame
	load("res://game/ui/ui_art.gd").settings_path = "user://settings_trade.cfg"
	srv = root.get_node("Server")
	pd = root.get_node("PlayerData")
	srv.active = true
	client = Recorder.new()
	client.name = "DaocClient"
	srv.add_child(client)
	srv.client = client
	pd.character = {"name": "Tradeprobe", "class_name": "Armsman"}
	var stub := GDScript.new()
	stub.source_code = "extends Node3D\nvar ui\nvar player\nvar zone = {\"info\": {\"id\": 26}}\nvar npcs\n"
	stub.reload()
	var viewer := Node3D.new()
	viewer.set_script(stub)
	root.add_child(viewer)
	current_scene = viewer
	ui = load("res://game/ui/game_ui.gd").new()
	root.add_child(ui)
	viewer.set("ui", ui)
	await process_frame
	await process_frame
	var w = ui.merchant.trade_window
	check(w != null and w is ClassicWindow, "the merchant window built the trade window beside it")
	if w == null:
		quit(fails)
		return
	check(not w.visible, "it is hidden until a trade opens")

	srv._on_raw(0xEA, trade_packet([45], 7500, 0, "Trading with Aldwyn"))
	await process_frame
	check(srv.trade.get("partner", "") == "Aldwyn", "the TradeWindow packet is read: the partner from the title")
	check(int(srv.trade.get("my_money", 0)) == 7500 and (srv.trade.get("my_slots", []) as Array) == [45],
			"your coin (75 silver) and your piece's backpack slot")
	check(w.visible, "the window opens")
	check(w._title.text == "TRADING WITH ALDWYN", "titled as the server says: TRADING WITH ALDWYN")
	check(w._their_rows[0]._name.text == "STUDDED VEST" and w._their_rows[0]._info.text == "96% L28",
			"the bot's piece with its quality and level")
	var it: Dictionary = load("res://game/ui/item_db.gd").item(String(w._their_rows[0].item_id))
	check(int(it.get("object_type", 0)) == 34 and int(it.get("model", 0)) == 136, "its item data (object type, model) for the icon and the hover box")

	_say("[Aldwyn sells] 2 pieces: say \"buy <number>\" to Aldwyn, or /tradewindow buy <number>.")
	_say("[Aldwyn sells] 1. Studded Vest (96%, level 23): 1 gold, 20 silver")
	_say("[Aldwyn sells] 2. Hunting Bow (level 20): 25 silver (on the Realm Exchange)")
	await process_frame
	check(w._goods_rows[0]._text.text == "1. STUDDED VEST (96%, LEVEL 23): 1 GOLD, 20 SILVER" and w._goods_rows[1]._buy.visible,
			"the bot's goods, each with a BUY button")
	w._goods_rows[1]._buy.pressed.emit()
	check(_sent("buy 2"), "BUY sends /tradewindow buy 2")

	_say("Aldwyn waits: Put 45 silver more in the window.")
	await process_frame
	check(w._accept.disabled and w._status.text.begins_with("ALDWYN WAITS"), "ACCEPT waits while the bot waits, its word in red")
	_say("Aldwyn has accepted the trade.")
	await process_frame
	check(not w._accept.disabled and w._status.text == "ALDWYN HAS ACCEPTED THE TRADE.", "the bot has accepted: in green, ACCEPT on")
	w._accept.pressed.emit()
	check(_sent("accept"), "ACCEPT sends /tradewindow accept")

	w._silver.text = "75"
	w._set_coin()
	check(_sent("coin 7500"), "coin typed in and SET: /tradewindow coin 7500")
	w._drop_data(Vector2.ZERO, {"kind": "item", "slot": 46, "id": "srv:46", "icon": 0, "name": "Chain Coif"})
	check(_sent("put 46"), "a backpack item dropped on the window: /tradewindow put 46")
	w._repair.button_pressed = true
	w._repair.pressed.emit()
	check(_sent("repair on"), "REPAIR: /tradewindow repair on")

	var before: int = client.calls.size()
	var closed := PackedByteArray()
	closed.resize(40)
	srv._on_raw(0xEA, closed)
	await process_frame
	check(srv.trade.is_empty() and not w.visible, "the close packet (40 zero bytes) closes the window")
	check(client.calls.size() == before, "a close from the server sends nothing back")
	srv._on_raw(0xEA, trade_packet([], 0, 4500, "Trading with Aldwyn"))
	await process_frame
	check(w.visible and w._their_coin.text.begins_with("PAYS 45 SILVER"), "the bot's coin shows on its side")
	w.hide_window()
	check(_sent("close"), "CANCEL (or the window's cross) sends /tradewindow close")
	print("[trade] ", fails, " failed")
	quit(fails)
