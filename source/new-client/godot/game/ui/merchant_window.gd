class_name MerchantWindow
extends ClassicWindow
## The merchant (shop) window on the client's merchant.bmp (350x448: item list area, scroll track, five price boxes
## with the mithril / platinum / gold / silver / copper coins). Opened by right-clicking a merchant (period: "when I
## right clicked on Reeni, she just looked friendly and opened her merchant window").
## Stock: content/items/merchants.json (the old server's merchant lists - not proven 1.65, ledger). Click an item to
## see its price; BUY (or double-click) buys it into the first free backpack slot. Sell: drag an item from the
## backpack onto the window - the merchant pays 50% of its value (old server item_sell_ratio 50).
## Chat lines are the old server's: "You just bought {item} for {price}.", "{merchant} gives you {price} for {item}."

const ROW_H := 18.0
const ROWS := 20
const LIST := Rect2(8, 20, 324, 360)
const COIN_BOX_X := [64, 109, 154, 199, 244]        # price boxes (original px)
const COIN_BOX_Y := 393.0                    # inside the 13 px box (rows 392-404)

var npc_name := ""
var list_id := ""
var page := 0
var pages: Array = []                              # [[item id, ...] per page]
var selected := ""
var _rows: Array = []
var _title: Label
var _page_label: Label
var _coins: Array = []
var _buy: Button
var _sell: Button
var _appraise: Button
# QUANTITY (the original client's merchant window, ui/isles/merchant_window.xml + game.dll 0x531AB3 - original-client
# chat 2026-10-30): how many packs one BUY asks for, 1 - 100 with the arrows; back to 1 after a buy unless KEEP is on
const QTY_MAX := 100
var qty := 1
var _qty_label: Label
var _keep: Button
# a page holds up to 30 goods (the server's MerchantTradeItems), the list shows ROWS: it scrolls (wheel, the track)
var _offset := 0
var _thumb: ColorRect
var _track: Control


func build() -> void:
	closable = true
	_title = UiArt.label("", 11, UiArt.GOLD_HI)
	_title.position = Vector2(16, 2)
	_title.size = Vector2(318, 14)
	_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_title)
	for i in ROWS:
		var r := ShopRow.new()
		r.window = self
		r.position = LIST.position + Vector2(0, i * ROW_H)
		r.size = Vector2(LIST.size.x, ROW_H)
		add_child(r)
		_rows.append(r)
	for i in 5:
		# the art's price boxes are holes (transparent, 33 x 13 at y 392): the game world showed through behind the
		# digits and no price could be read (owner 2026-10-11) - a dark field fills each, the digits on it
		var bg := ColorRect.new()
		bg.color = Color(0.02, 0.02, 0.02)
		bg.position = Vector2(COIN_BOX_X[i], 392)
		bg.size = Vector2(33, 13)
		bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(bg)
		var l := UiArt.label("", 8, UiArt.CREAM)
		l.position = Vector2(COIN_BOX_X[i], COIN_BOX_Y)
		l.size = Vector2(33, 11)
		l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		add_child(l)
		_coins.append(l)
	_qty_label = UiArt.label("QTY 1", 8, UiArt.GOLD)
	_qty_label.position = Vector2(6, 391)
	_qty_label.size = Vector2(56, 12)
	_qty_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_qty_label.tooltip_text = "How many packs (or pieces) one BUY gets"
	_qty_label.mouse_filter = Control.MOUSE_FILTER_PASS
	add_child(_qty_label)
	var less := UiArt.button("<", Vector2(18, 14), 9)
	less.position = Vector2(12, 405)
	less.pressed.connect(func(): set_qty(qty - 1))
	add_child(less)
	var more := UiArt.button(">", Vector2(18, 14), 9)
	more.position = Vector2(36, 405)
	more.pressed.connect(func(): set_qty(qty + 1))
	add_child(more)
	_keep = UiArt.button("KEEP", Vector2(50, 15), 8)
	_keep.position = Vector2(288, 392)
	_keep.toggle_mode = true
	_keep.tooltip_text = "Keep the quantity after a purchase"
	_keep.toggled.connect(func(on: bool): _keep.modulate = UiArt.GOLD_HI if on else Color.WHITE)
	add_child(_keep)
	_track = Control.new()                             # the art's empty strip beside the list: the list's place
	_track.position = Vector2(LIST.end.x + 3, LIST.position.y)
	_track.size = Vector2(10, LIST.size.y)
	_track.mouse_filter = Control.MOUSE_FILTER_STOP
	_track.gui_input.connect(_track_input)
	add_child(_track)
	_thumb = ColorRect.new()
	_thumb.color = Color(UiArt.GOLD.r, UiArt.GOLD.g, UiArt.GOLD.b, 0.55)
	_thumb.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_track.add_child(_thumb)
	# the client's row (ui/isles/merchant_window.xml - the 1.65-era Shrouded Isles UI, the same in ui/atlantis): BUY at
	# x 10, SELL at 82, APPRAISE at 155, the page arrows at 228 / 248 and "Page 1 of 1" at 275 (owner 2026-10-29: "was
	# there a sell button in old client?" - yes; dragging an item onto the window still sells it)
	_buy = UiArt.button("BUY", Vector2(68, 18), 10)
	_buy.position = Vector2(10, 424)
	_buy.pressed.connect(buy_selected)
	add_child(_buy)
	_sell = UiArt.button("SELL", Vector2(68, 18), 10)
	_sell.position = Vector2(82, 424)
	_sell.pressed.connect(sell_picked)
	_sell.tooltip_text = "Sells the item you picked in your backpack (right-click it), or drop an item here"
	add_child(_sell)
	_appraise = UiArt.button("APPRAISE", Vector2(68, 18), 10)
	_appraise.position = Vector2(155, 424)
	_appraise.pressed.connect(appraise_picked)
	_appraise.tooltip_text = "What the merchant would pay for the item you picked (right-click it), or drop an item here"
	# an item dropped on APPRAISE is appraised, not sold (the window takes drops as sales)
	_appraise.set_drag_forwarding(Callable(), func(_p, data): return _can_drop_data(_p, data),
			func(_p, data):
				target_merchant()
				PlayerData.appraise(int(data.slot), npc_name))
	add_child(_appraise)
	var prev := UiArt.button("<", Vector2(18, 16), 9)
	prev.position = Vector2(228, 425)
	prev.pressed.connect(func(): show_page(page - 1))
	add_child(prev)
	var nxt := UiArt.button(">", Vector2(18, 16), 9)
	nxt.position = Vector2(248, 425)
	nxt.pressed.connect(func(): show_page(page + 1))
	add_child(nxt)
	_page_label = UiArt.label("", 8, UiArt.CREAM)
	_page_label.position = Vector2(270, 428)
	_page_label.size = Vector2(70, 12)
	_page_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_page_label)


## Where the shop opens: at the top, centred (owner 2026-10-14: not in the top right corner), the whole window on the
## screen.
func _place_top_center() -> void:
	var area := get_parent_area_size()
	position = Vector2(clampf(floorf((area.x - size.x) * 0.5), 0.0, maxf(0.0, area.x - size.x)), 4.0)


func open_for(merchant: String, items_list: String) -> void:
	if not visible:
		_place_top_center()
	npc_name = merchant
	list_id = items_list
	_title.text = merchant.to_upper()
	pages = []
	for e in ItemDb.merchant_list(items_list):
		var p := int(e[0])
		while pages.size() <= p:
			pages.append([])
		pages[p].append(String(e[2]))
	pages = pages.filter(func(p): return not (p as Array).is_empty())
	selected = ""
	show_page(0)
	show_window()


## The server's merchant (Offline DAoC Rebuilt): its pages as the MerchantWindow packets brought them - each item a
## runtime ItemDb entry "srvm:<page>:<index>" (its slot on the server is page * 30 + index).
var server_window := -1
var server_oid := 0                            # the server merchant's object id (ServerWorld.target_for_trade)


## The server acts on your target: the merchant whose window this is, targeted again if need be.
func target_merchant() -> void:
	var v: Node = Server.viewer()
	if Server.active and v and v.get("npcs") and v.npcs.has_method("target_for_trade"):
		v.npcs.target_for_trade(server_oid)


func open_server(merchant: String, server_pages: Dictionary, window_type: int) -> void:
	npc_name = merchant
	list_id = ""
	server_window = window_type
	_title.text = merchant.to_upper()
	pages = []
	var keys := server_pages.keys()
	keys.sort()
	for k in keys:
		pages.append(server_pages[k])
	if not visible:
		selected = ""
		page = 0
		_place_top_center()
	show_page(page)
	show_window()


func show_page(p: int) -> void:
	page = clampi(p, 0, maxi(pages.size() - 1, 0))
	_offset = 0
	_fill()
	_page_label.text = "PAGE %d / %d" % [page + 1, maxi(pages.size(), 1)]
	_show_price()


## The page's goods from the scroll place on (owner 2026-10-30: the inn's arrow merchant has 28 on one page - the 20
## rows hid the last 8), and the track's thumb.
func _fill() -> void:
	var items: Array = pages[page] if page < pages.size() else []
	_offset = clampi(_offset, 0, maxi(items.size() - ROWS, 0))
	for i in ROWS:
		var k := _offset + i
		_rows[i].set_item(String(items[k]) if k < items.size() else "")
	_thumb.visible = items.size() > ROWS
	if _thumb.visible:
		var h := maxf(_track.size.y * ROWS / items.size(), 20.0)
		_thumb.size = Vector2(_track.size.x, h)
		_thumb.position = Vector2(0, (_track.size.y - h) * _offset / maxf(items.size() - ROWS, 1))


func scroll(rows: int) -> void:
	_offset += rows
	_fill()


func _track_input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed:
		var mb := event as InputEventMouseButton
		if mb.button_index == MOUSE_BUTTON_WHEEL_UP or mb.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			scroll(-3 if mb.button_index == MOUSE_BUTTON_WHEEL_UP else 3)
		elif mb.button_index == MOUSE_BUTTON_LEFT:              # above / below the thumb: a list's worth
			scroll(-(ROWS - 1) if mb.position.y < _thumb.position.y else ROWS - 1)
		_track.accept_event()


func set_qty(n: int) -> void:
	qty = clampi(n, 1, QTY_MAX)
	_qty_label.text = "QTY %d" % qty


func select_item(id: String) -> void:
	selected = id
	for r in _rows:
		r.queue_redraw()
	_show_price()


func _show_price() -> void:
	var price := int(ItemDb.item(selected).get("price", 0)) if selected != "" else 0
	var parts := PlayerData.split_money(price)
	var keys := ["mithril", "platinum", "gold", "silver", "copper"]
	for i in 5:
		_coins[i].text = str(parts[keys[i]]) if selected != "" else ""
	_buy.disabled = selected == ""


func buy_selected() -> void:
	if selected == "":
		return
	if Server.active and selected.begins_with("srvm:"):   # the server's merchant: PlayerBuyRequest (page * 30 + index)
		var parts := selected.split(":")
		var v: Node = Server.viewer()
		if v:
			target_merchant()
			var r: Vector3 = Server.to_region(Server.current_zone_id(), v.player.global_position)
			Server.client.call("BuyItem", int(r.x), int(r.y), int(parts[1]) * 30 + int(parts[2]), qty, maxi(server_window, 0))
		if not _keep.button_pressed:
			set_qty(1)
		return
	for i in qty:
		if not PlayerData.buy(selected, npc_name):
			break
	if not _keep.button_pressed:
		set_qty(1)


## SELL / APPRAISE: the backpack item picked with a right-click (PlayerData.selected_slot - the Sell key's too).
func _picked_slot() -> int:
	var s := PlayerData.selected_slot
	if s < 0 or not PlayerData.is_backpack(s) or PlayerData.entry(s).is_empty():
		PlayerData.message.emit("Select an item in your backpack first (right-click it), or drag it onto the window.", "merchant")
		return -1
	return s


func sell_picked() -> void:
	var s := _picked_slot()
	if s >= 0:
		target_merchant()
		PlayerData.sell(s, npc_name)


func appraise_picked() -> void:
	var s := _picked_slot()
	if s >= 0:
		target_merchant()
		PlayerData.appraise(s, npc_name)


func _can_drop_data(_pos: Vector2, data: Variant) -> bool:
	return InventorySheet.is_item(data) and PlayerData.is_backpack(int(data.slot))


func _drop_data(_pos: Vector2, data: Variant) -> void:
	target_merchant()
	PlayerData.sell(int(data.slot), npc_name)


class ShopRow extends Control:
	var window: MerchantWindow
	var item_id := ""
	var _icon: TextureRect
	var _name: Label
	var _level: Label

	func _init() -> void:
		mouse_filter = Control.MOUSE_FILTER_STOP
		_icon = TextureRect.new()
		_icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		_icon.stretch_mode = TextureRect.STRETCH_SCALE
		_icon.position = Vector2(2, 1)
		_icon.size = Vector2(16, 16)
		_icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_icon)
		_name = UiArt.label("", 8, UiArt.CREAM)
		_name.position = Vector2(22, 2)
		_name.size = Vector2(210, 14)
		_name.clip_text = true
		add_child(_name)
		_level = UiArt.label("", 8, UiArt.DIM)
		_level.position = Vector2(234, 2)
		_level.size = Vector2(86, 14)
		_level.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		add_child(_level)

	func set_item(id: String) -> void:
		item_id = id
		var it := ItemDb.item(id) if id != "" else {}
		_icon.texture = ItemDb.icon(int(it.get("icon", 0))) if not it.is_empty() else null
		_name.text = String(it.get("name", "")).to_upper()
		var lvl := int(it.get("level", 0))
		_level.text = ("LVL %d" % lvl) if not it.is_empty() and lvl > 0 else ""
		var pack := int(it.get("pack", 0))
		if pack > 1:                                     # a pack: how many one purchase gives (arrows, poisons ...)
			_level.text = ("x%d   " % pack) + _level.text
		tooltip_text = String(it.get("name", "")).to_upper()
		queue_redraw()

	func _draw() -> void:
		if item_id != "" and window and window.selected == item_id:
			draw_rect(Rect2(Vector2.ZERO, size), Color(1.0, 0.8, 0.35, 0.18))

	func _make_custom_tooltip(_for_text: String) -> Object:          # weapons / armour: the mini delve box
		return ItemTooltip.make(ItemDb.item(item_id)) if item_id != "" else null

	func _gui_input(event: InputEvent) -> void:
		if event is InputEventMouseButton and event.pressed and (event.button_index == MOUSE_BUTTON_WHEEL_UP
				or event.button_index == MOUSE_BUTTON_WHEEL_DOWN):
			window.scroll(-3 if event.button_index == MOUSE_BUTTON_WHEEL_UP else 3)
			accept_event()
			return
		if item_id == "" or not (event is InputEventMouseButton and event.pressed):
			return
		if event.button_index == MOUSE_BUTTON_LEFT:
			window.select_item(item_id)
			if event.double_click:
				window.buy_selected()
			accept_event()
		elif event.button_index == MOUSE_BUTTON_RIGHT:
			window.select_item(item_id)                    # its standard information; then Shift+I = delve
			PlayerData.delve_id = item_id
			PlayerData.selected_slot = -1
			PlayerData.summary.emit(ItemDb.item(item_id))
			accept_event()
