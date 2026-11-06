class_name Quickbar
extends ClassicWindow
## One of the client's three quickbars (hbar_horz 372x33 / hbar_vert 32x372). The 32 px end square of the art holds
## (measured on the bitmaps, original pixels):
##   the move handle (0,0)-(11,11), the orientation button (19,0)-(32,11) which turns the bar vertical / horizontal,
##   the previous / next bank arrows (0,11)-(16,32) and (16,11)-(32,32).
## Then 10 slots of 34 px, 10 banks each. Drag an item, skill or spell onto a slot; drag a slot off the bar or
## right-click it to clear it. Slot keys come from Keybinds (bar 1 = 1-0 as in the client).

const ORIENT := Rect2(19, 0, 13, 11)
const PREV := Rect2(0, 11, 16, 21)
const NEXT := Rect2(16, 11, 16, 21)

var bar := 0
var vertical := false
var _slots: Array = []
var _bank_label: Label



## The macro / pet command button: an opaque dark stone square with a gold rim and its name in bright letters - the old
## client's solid text icons (owner 2026-10-15: our see-through ones were hard to read).
static var _plate_style: StyleBoxFlat


static func text_plate() -> StyleBoxFlat:
	if _plate_style == null:
		_plate_style = StyleBoxFlat.new()
		_plate_style.bg_color = Color(0.17, 0.15, 0.12)
		_plate_style.border_color = Color(0.72, 0.6, 0.34)
		_plate_style.set_border_width_all(1)
		_plate_style.shadow_color = Color(0, 0, 0, 0.5)
		_plate_style.shadow_size = 1
	return _plate_style

func build() -> void:
	movable = true
	closable = false
	_bank_label = UiArt.label("1", 7, UiArt.GOLD_HI)
	_bank_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_bank_label.position = Vector2(10, 0)
	_bank_label.size = Vector2(10, 10)
	add_child(_bank_label)
	_hot(ORIENT, "TURN THE BAR", func(): set_vertical(not vertical))
	_hot(PREV, "PREVIOUS BAR", func(): set_bank(PlayerData.quickbar_bank[bar] - 1))
	_hot(NEXT, "NEXT BAR", func(): set_bank(PlayerData.quickbar_bank[bar] + 1))
	for i in 10:
		var s := QuickSlot.new()
		s.bar = bar
		s.slot = i
		s.size = Vector2(31, 31)
		add_child(s)
		_slots.append(s)
	_layout()
	Keybinds.changed.connect(_refresh)
	PlayerData.changed.connect(_refresh)
	if Server.active:
		PlayerData.hook(self, Server.skills_changed, _refresh)   # (an ability's icon taken again from the list)
	_refresh()


func _hot(r: Rect2, tip: String, cb: Callable) -> void:
	var b := Button.new()
	b.flat = true
	b.focus_mode = Control.FOCUS_NONE
	b.position = r.position
	b.size = r.size
	b.tooltip_text = tip
	for k in ["normal", "hover", "pressed", "focus"]:
		b.add_theme_stylebox_override(k, StyleBoxEmpty.new())
	b.pressed.connect(cb)
	add_child(b)


func set_vertical(v: bool) -> void:
	vertical = v
	set_frame("hbar_vert" if vertical else "hbar_horz")
	_layout()
	var area := get_parent_area_size()                     # keep the whole turned bar on the screen
	position = position.clamp(Vector2.ZERO, (area - size).max(Vector2.ZERO))
	var cfg := ConfigFile.new()
	cfg.load(UiArt.settings_path)
	cfg.set_value("interface", "quickbar%d_vertical" % (bar + 1), vertical)
	cfg.save(UiArt.settings_path)
	save_position()


func _layout() -> void:
	for i in _slots.size():
		_slots[i].position = Vector2(32 + i * 34 + 1, 1) if not vertical else Vector2(1, 32 + i * 34 + 1)


func set_bank(b: int) -> void:
	PlayerData.quickbar_bank[bar] = posmod(b, PlayerData.BANKS)
	PlayerData.save_character()
	_refresh()


func use_slot(i: int) -> void:
	_slots[i].use()


## The client's hover text for a bar (game.dll 0x4d7e4f: "Quickbar %d" over "Modifier - None / Alt / Ctrl"),
## taken from the bar's actual keys so a rebinding shows up.
func bar_tooltip() -> String:
	var keys: Array = Keybinds.bindings.get("qb%d_1" % (bar + 1), [])
	var k := String(keys[0]) if keys.size() > 0 else ""
	var mod := "NONE"
	if k == "":
		mod = "UNBOUND"
	elif k.begins_with("Ctrl+"):
		mod = "CTRL"
	elif k.begins_with("Alt+"):
		mod = "ALT"
	elif k.begins_with("Shift+"):
		mod = "SHIFT"
	return "QUICKBAR %d\nMODIFIER - %s" % [bar + 1, mod]


func _refresh() -> void:
	_bank_label.text = str(PlayerData.quickbar_bank[bar] + 1)
	tooltip_text = bar_tooltip()
	for s in _slots:
		s.refresh()


class QuickSlot extends Control:
	var bar := 0
	var slot := 0
	var _icon: TextureRect
	var _key: Label
	var _border: TextureRect
	var _text: Label                                     # a pet command: its name in the slot (no icon)
	var _plate: Panel
	var _cool: ColorRect                                 # the server's reuse timer (DisableSkills): darkened + seconds
	var _cool_text: Label

	func _init() -> void:
		mouse_filter = Control.MOUSE_FILTER_STOP
		_icon = TextureRect.new()
		_icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		_icon.stretch_mode = TextureRect.STRETCH_SCALE
		_icon.set_anchors_preset(Control.PRESET_FULL_RECT)
		_icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_icon)
		_border = TextureRect.new()                     # spells / styles: the icons.csv border overlay
		_border.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		_border.stretch_mode = TextureRect.STRETCH_SCALE
		_border.set_anchors_preset(Control.PRESET_FULL_RECT)
		_border.mouse_filter = Control.MOUSE_FILTER_IGNORE
		add_child(_border)
		_key = UiArt.label("", 7, UiArt.CREAM)
		_key.position = Vector2(1, 21)
		add_child(_key)
		_plate = Panel.new()                            # a macro's / pet command's solid button (as the old client's)
		_plate.add_theme_stylebox_override("panel", Quickbar.text_plate())
		_plate.set_anchors_preset(Control.PRESET_FULL_RECT)
		_plate.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_plate.visible = false
		add_child(_plate)
		_text = UiArt.label("", 7, UiArt.CREAM)
		_text.set_anchors_preset(Control.PRESET_FULL_RECT)
		_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		add_child(_text)
		_cool = ColorRect.new()
		_cool.color = Color(0, 0, 0, 0.6)
		_cool.set_anchors_preset(Control.PRESET_FULL_RECT)
		_cool.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_cool.visible = false
		add_child(_cool)
		_cool_text = UiArt.label("", 9, UiArt.CREAM)
		_cool_text.set_anchors_preset(Control.PRESET_FULL_RECT)
		_cool_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_cool_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		_cool.add_child(_cool_text)

	func _process(_delta: float) -> void:
		if not Server.active:
			return
		var e := entry()
		var left := Server.reuse_left(String(e.get("id", ""))) if not e.is_empty() else 0.0
		_cool.visible = left > 0.0
		if left > 0.0:
			_cool_text.text = "%d" % ceili(left) if left < 60.0 else "%dM" % ceili(left / 60.0)

	func entry() -> Dictionary:
		return PlayerData.quickbars[bar][PlayerData.quickbar_bank[bar]][slot]

	func _notification(what: int) -> void:
		if what == NOTIFICATION_MOUSE_ENTER and Server.active:
			Server.prefetch_skill(entry())                 # the server's delve text, ready for the hover box

	func _make_custom_tooltip(_for_text: String) -> Object:
		return ItemTooltip.skill(entry())

	func refresh() -> void:
		var e := entry()
		_icon.texture = ItemDb.icon(int(e.get("icon", -1))) if not e.is_empty() else null
		_border.texture = ItemDb.border(int(e.get("border", -1))) if not e.is_empty() else null
		_text.text = String(e.get("name", "")).to_upper() if String(e.get("kind", "")) in ["petcmd", "macro"] 				or (not e.is_empty() and int(e.get("icon", -1)) < 0) else ""      # (an ability without an icon: its name)
		_plate.visible = _text.text != ""
		var keys: Array = Keybinds.bindings.get("qb%d_%d" % [bar + 1, slot + 1], [])
		var k := String(keys[0]) if keys.size() > 0 else ""
		# the art already shows 1-10 over each slot; only a rebound key is written in the slot
		_key.text = k.to_upper() if k != "" and k != str((slot + 1) % 10) else ""
		# an empty slot shows the bar's own hover text (quickbar number + modifier), as the client's bar does
		tooltip_text = String(e.get("name", "")).to_upper() if not e.is_empty() else (get_parent() as Quickbar).bar_tooltip()

	func use() -> void:
		var e := entry()
		if e.is_empty():
			return
		modulate = Color(1.6, 1.4, 0.8)
		create_tween().tween_property(self, "modulate", Color.WHITE, 0.25)
		if String(e.get("kind", "")) == "item":
			PlayerData.use_item(String(e.get("id", "")))
		elif String(e.get("kind", "")) == "petcmd":      # a pet window command dragged onto the bar
			PlayerData.pet_command(String(e.get("id", "")))
		elif String(e.get("kind", "")) == "macro":       # /macro: its command, as if typed
			PlayerData.macro_run.emit(String(e.get("id", "")))
		else:
			PlayerData.use_skill.emit(e)                    # spells, styles, abilities

	func _get_drag_data(_pos: Vector2) -> Variant:
		var e := entry()
		if e.is_empty():
			return null
		set_drag_preview(_preview(e))
		_assign(Dictionary())
		return {"kind": "quickslot", "entry": e}

	## What a slot's entry looks like on the mouse pointer: its icon (and border), or a macro's / pet command's name.
	func _preview(e: Dictionary) -> Control:
		var size := Vector2(32, 32) * get_global_transform().get_scale()
		if int(e.get("icon", -1)) < 0:
			var panel := Panel.new()
			panel.size = size
			panel.add_theme_stylebox_override("panel", Quickbar.text_plate())
			var l := UiArt.label(String(e.get("name", "")).to_upper(), 7, UiArt.CREAM)
			l.size = size
			l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
			l.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
			l.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
			panel.add_child(l)
			return panel
		var prev := TextureRect.new()
		prev.texture = ItemDb.icon(int(e.get("icon", -1)))
		prev.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		prev.size = size
		if int(e.get("border", -1)) >= 0:
			var b := TextureRect.new()
			b.texture = ItemDb.border(int(e.get("border", -1)))
			b.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			b.size = size
			prev.add_child(b)
		return prev

	func _can_drop_data(_pos: Vector2, data: Variant) -> bool:
		# "ability": Sprint, Stealth, Quickcast ... from the skills sheet (panel_sheets.gd: "These drag onto the quickbar"),
		# which use_skill already plays (abilities.gd); the bar refused them (owner 2026-10-18: Sprint showed a no-entry sign)
		return data is Dictionary and String(data.get("kind", "")) in ["item", "quickslot", "skill", "spell", "style", "petcmd", "macro", "ability"]

	func _drop_data(_pos: Vector2, data: Variant) -> void:
		var e: Dictionary = data.get("entry", {}) if data.kind == "quickslot" else \
				{"kind": data.kind, "id": data.get("id", ""), "icon": data.get("icon", -1), "name": data.get("name", ""),
				"border": data.get("border", -1)}
		var old := entry()
		_assign(e)
		# what was in the slot goes onto the mouse pointer, as the old client did (owner 2026-10-14: a replaced spell
		# was simply gone): click another slot to put it there, or click off the bar to throw it away
		if not old.is_empty() and old != e:
			_hold.call_deferred(old)

	func _hold(e: Dictionary) -> void:
		force_drag({"kind": "quickslot", "entry": e}, _preview(e))

	# one left click uses the slot, as the 1.65 quickbar (was: double-click only - most clicks did nothing, owner
	# 2026-10-10); right-click shows what it is; shift + right-click empties it, as the old client (owner 2026-10-19);
	# or drag it off the bar
	func _gui_input(event: InputEvent) -> void:
		if event is InputEventMouseButton and event.pressed:
			if event.button_index == MOUSE_BUTTON_LEFT and not event.double_click:
				use()
				accept_event()
			elif event.button_index == MOUSE_BUTTON_RIGHT:
				var e := entry()
				if e.is_empty():
					return
				if event.shift_pressed:
					_assign(Dictionary())
					accept_event()
					return
				if String(e.get("kind", "")) == "item":
					PlayerData.delve.emit(ItemDb.item(String(e.get("id", ""))))
				elif String(e.get("kind", "")) == "macro":
					PlayerData.message.emit("Macro %s: %s" % [String(e.get("name", "")), String(e.get("id", ""))], "system")
				else:
					PlayerData.skill_info.emit(e)
				accept_event()

	func _assign(e: Dictionary) -> void:
		PlayerData.quickbars[bar][PlayerData.quickbar_bank[bar]][slot] = e
		PlayerData.save_character()
		refresh()
