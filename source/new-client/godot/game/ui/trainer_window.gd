class_name TrainerWindow
extends ClassicWindow
## The trainer's training window (training.bmp, 350x448: Specializations + Pts, then Realm Skills). Each spec shows
## its level and what the next level costs; clicking a spec selects it, TRAIN spends the points (PlayerData.train:
## n + 1 points from level n, never above the character level). Opened by right-clicking your own trainer.
## Realm Skills: the class's realm abilities as the server sends them (TrainerWindow code 1) - level, the next level's
## cost, greyed when its requirements aren't met; TRAIN on one sends the trainer's train request for the next level
## (owner 2026-10-13: the Sluaghbinder's 30 realm abilities, every class's).
## The training preview (owner 2026-10-17, like the old client's "New Styles and Skills"): a panel docked beside the
## window - never inside it, so the specialization and Realm Skills lists keep their room for hybrids with many lines -
## listing what the clicked specialization teaches and at which level (the server's TrainerWindow codes 3 / 4):
## learned ones dimmed, the next level's in gold, hovering one names its level and kind.

const LIST := Rect2(10, 38, 322, 160)
const RA_LIST := Rect2(10, 244, 322, 162)
const ROW_H := 15.0
var _rows: VBoxContainer
var _ra_rows: VBoxContainer
var _points: Label
var _ra_points: Label
var _selected := ""
var _selected_ra := -1                        # a Realm Skills row (its place in the class's list)
const PREVIEW_W := 230.0
const PREVIEW_ICON := 20.0
const PAGE_KIND := {2: "style", 3: "spell", 4: "song", 5: "ability"}
var _preview: Control
var _preview_title: Label
var _preview_note: Label
var _preview_scroll: ScrollContainer
var _preview_rows: VBoxContainer


func build() -> void:
	closable = true
	var scroll := ScrollContainer.new()
	scroll.position = LIST.position
	scroll.size = LIST.size
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	_rows = VBoxContainer.new()
	_rows.custom_minimum_size = Vector2(LIST.size.x - 8, 0)
	_rows.add_theme_constant_override("separation", 0)
	scroll.add_child(_rows)
	var ra_scroll := ScrollContainer.new()
	ra_scroll.position = RA_LIST.position
	ra_scroll.size = RA_LIST.size
	ra_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(ra_scroll)
	_ra_rows = VBoxContainer.new()
	_ra_rows.custom_minimum_size = Vector2(RA_LIST.size.x - 8, 0)
	_ra_rows.add_theme_constant_override("separation", 0)
	ra_scroll.add_child(_ra_rows)
	_ra_points = UiArt.label("", 9, UiArt.GOLD_HI)       # the box beside "Realm Skills": points to spend
	_ra_points.position = Vector2(156, 224)
	_ra_points.size = Vector2(50, 14)
	_ra_points.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_ra_points)
	_points = UiArt.label("", 9, UiArt.GOLD_HI)          # the box beside "Specializations", after its "Pts": the
	_points.position = Vector2(172, 20)                    # points to spend (owner 2026-10-14: it was empty, the
	_points.size = Vector2(34, 14)                         # number written again under the lists instead)
	_points.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	add_child(_points)
	var train := UiArt.button("TRAIN", Vector2(80, 18), 10)
	train.position = Vector2(254, 416)
	train.pressed.connect(func():
		if Server.active and _selected_ra >= 0:           # a realm ability: its next level
			for ra in Server.realm_abilities:
				if int(ra.index) == _selected_ra:
					Server.train_realm_ability(_selected_ra, int(ra.level) + 1)
			return
		if _selected == "":
			return
		if Server.active:                                  # the server trains: its train request by the window's place
			for sp in Server.trainer_specs:                # (PlayerTrainHandler), then the window again (new levels)
				if String(sp.name) == _selected:
					Server.client.call("TrainSpec", int(sp.index), int(sp.level) + 1)
					get_tree().create_timer(0.6).timeout.connect(func(): Server.client.call("TrainWindow"))
			return
		if PlayerData.train(_selected):
			refresh())
	add_child(train)
	_build_preview()
	PlayerData.hook(self, PlayerData.skills_changed, func():
		if visible:
			refresh())
	if Server.active:
		PlayerData.hook(self, Server.skills_changed, func():
			if visible:
				refresh())
		PlayerData.hook(self, Server.trainer_changed, func():
			if visible:
				refresh())


func open() -> void:
	refresh()
	show_window()


func refresh() -> void:
	for c in _rows.get_children():
		c.queue_free()
	if Server.active:                                      # the trainer window's specializations (TrainerWindow 0)
		for sk in Server.trainer_specs:
			var key := String(sk.name)
			var lvl := int(sk.level)
			_rows.add_child(_row(key.to_upper(), str(lvl), "NEXT: %d" % (lvl + 1),
					UiArt.GOLD_HI if key == _selected else UiArt.CREAM, func():
						_selected = key
						_selected_ra = -1
						refresh()))
		_points.text = str(PlayerData.spec_points())
		_realm_rows()
		_fill_preview()
		return
	for e in ClassDb.trainable_specs(PlayerData.class_id(), PlayerData.level):
		var key := String(e.spec)
		var lvl := int(PlayerData.specs.get(key, 1))
		var name := String(ClassDb.spec(key).get("name", key)).to_upper()
		_rows.add_child(_row(name, str(lvl), "NEXT: %d" % (lvl + 1), UiArt.GOLD_HI if key == _selected else UiArt.CREAM,
				func():
					_selected = key
					refresh()))
	_points.text = str(PlayerData.spec_points())
	if _preview:
		_preview.visible = false                           # the offline game sends no lists


## The preview panel: the information window's stone frame (interact.bmp as a nine-patch: 19 px top bar, 13 px
## scroll track, 33 px foot), as tall as the training window.
func _build_preview() -> void:
	_preview = Control.new()
	_preview.size = Vector2(PREVIEW_W, size.y)
	_preview.mouse_filter = Control.MOUSE_FILTER_STOP
	# its frame's X (the interact frame's corner, as ClassicWindow's) closes the whole training window (owner
	# 2026-10-29: "the secondary x beside the skill preview should also close the entire window out")
	_preview.gui_input.connect(func(e: InputEvent):
		if e is InputEventMouseButton and e.pressed and e.button_index == MOUSE_BUTTON_LEFT \
				and e.position.x > PREVIEW_W - 16 and e.position.y < 14:
			_preview.accept_event()
			hide_window())
	var holder := Control.new()
	holder.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.scale = Vector2(0.25, 0.25)
	holder.size = _preview.size * 4.0
	var np := NinePatchRect.new()
	np.texture = UiArt.tex("interact")
	var patch := [6, 19, 13, 33]
	var sides := [SIDE_LEFT, SIDE_TOP, SIDE_RIGHT, SIDE_BOTTOM]
	for i in 4:
		np.set_patch_margin(sides[i], int(patch[i] * UiArt.K))
	np.size = _preview.size * 4.0
	np.mouse_filter = Control.MOUSE_FILTER_IGNORE
	holder.add_child(np)
	_preview.add_child(holder)
	_preview_title = UiArt.label("", 10, UiArt.GOLD_HI)
	_preview_title.position = Vector2(12, 21)
	_preview_title.size = Vector2(PREVIEW_W - 27, 14)
	_preview_title.clip_text = true
	_preview.add_child(_preview_title)
	_preview_note = UiArt.label("", 9, UiArt.DIM)
	_preview_note.position = Vector2(12, 36)
	_preview_note.size = Vector2(PREVIEW_W - 27, 12)
	_preview.add_child(_preview_note)
	_preview_scroll = ScrollContainer.new()
	_preview_scroll.position = Vector2(8, 52)
	_preview_scroll.size = Vector2(PREVIEW_W - 16, size.y - 52 - 36)
	_preview_scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	_preview.add_child(_preview_scroll)
	_preview_rows = VBoxContainer.new()
	_preview_rows.custom_minimum_size = Vector2(PREVIEW_W - 30, 0)
	_preview_rows.add_theme_constant_override("separation", 1)
	_preview_scroll.add_child(_preview_rows)
	add_child(_preview)


## Docked on the right, or on the left when the screen's edge is too close.
func _place_preview() -> void:
	var room := get_viewport_rect().size.x
	_preview.position = Vector2(size.x, 0) if global_position.x + size.x + PREVIEW_W <= room else Vector2(-PREVIEW_W, 0)


func _fill_preview() -> void:
	if _preview == null:
		return
	for c in _preview_rows.get_children():
		_preview_rows.remove_child(c)                      # out now: the new rows' places mustn't follow the old
		c.queue_free()
	_preview_scroll.scroll_vertical = 0
	_preview.visible = not Server.trainer_skills.is_empty() and _selected_ra < 0
	if not _preview.visible:
		return
	_place_preview()
	var spec: Dictionary = {}
	for sp in Server.trainer_specs:
		if String(sp.name) == _selected:
			spec = sp
	if spec.is_empty():
		_preview_title.text = "TRAINING PREVIEW"
		_preview_note.text = "CLICK A SPECIALIZATION"
		return
	var lvl := int(spec.level)
	var list: Array = Server.trainer_skills.get(int(spec.index), [])
	_preview_title.text = String(spec.name).to_upper()
	_preview_note.text = "NOTHING TO LEARN" if list.is_empty() else "LEVEL %d NOW" % lvl
	var first_new: Control = null
	for k in list:
		var at := int(k.level)
		var col := UiArt.DIM if at <= lvl else (UiArt.GOLD_HI if at == lvl + 1 else UiArt.CREAM)
		var row := _preview_row(k, col)
		_preview_rows.add_child(row)
		if first_new == null and at > lvl:
			first_new = row
	if first_new:                                          # what comes next in view, the learned ones above it
		_scroll_to(first_new)


func _scroll_to(row: Control) -> void:
	await get_tree().process_frame                         # the list laid out
	if is_instance_valid(row) and row.get_parent() == _preview_rows:
		_preview_scroll.scroll_vertical = int(maxf(row.position.y - PREVIEW_ICON, 0))


func _preview_row(k: Dictionary, col: Color) -> Control:
	var row := Control.new()
	row.custom_minimum_size = Vector2(PREVIEW_W - 30, PREVIEW_ICON + 1)
	row.mouse_filter = Control.MOUSE_FILTER_PASS
	var name := String(k.get("name", ""))
	var kind := String(PAGE_KIND.get(int(k.page), "skill"))
	row.tooltip_text = "%s
Level %d %s" % [name, int(k.level), kind]
	# the Magic tab's lookup: a spell / style / ability's icons.csv picture + its border
	var pick: Array = Server.skill_icon({2: "style", 3: "spell", 4: "spell"}.get(int(k.page), "icon"), int(k.icon))
	var ic := TextureRect.new()
	ic.texture = ItemDb.icon(int(pick[0]))
	ic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	ic.size = Vector2(PREVIEW_ICON, PREVIEW_ICON)
	ic.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if col == UiArt.DIM:
		ic.modulate = Color(0.6, 0.6, 0.6)
	row.add_child(ic)
	if int(pick[1]) >= 0:
		var br := TextureRect.new()
		br.texture = ItemDb.border(int(pick[1]))
		br.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		br.size = ic.size
		br.mouse_filter = Control.MOUSE_FILTER_IGNORE
		ic.add_child(br)
	var l := UiArt.label(name.to_upper(), 9, col)
	l.position = Vector2(PREVIEW_ICON + 4, 3)
	l.size = Vector2(PREVIEW_W - 30 - PREVIEW_ICON - 36, 14)
	l.clip_text = true
	l.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(l)
	var lv := UiArt.label(str(int(k.level)), 9, col)
	lv.position = Vector2(PREVIEW_W - 30 - 28, 3)
	lv.size = Vector2(26, 14)
	lv.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
	lv.mouse_filter = Control.MOUSE_FILTER_IGNORE
	row.add_child(lv)
	return row


## One list row: the name, its level and the next level / cost in their own columns (the client's lists line up;
## spaces in the proportional font did not).
func _row(name: String, level: String, next: String, col: Color, pick: Callable) -> Button:
	var row := Button.new()
	row.flat = true
	row.focus_mode = Control.FOCUS_NONE
	row.custom_minimum_size = Vector2(LIST.size.x - 10, ROW_H)
	row.pressed.connect(pick)
	for part in [[name, 2.0, 200.0, HORIZONTAL_ALIGNMENT_LEFT], [level, 196.0, 30.0, HORIZONTAL_ALIGNMENT_RIGHT],
			[next, 240.0, 70.0, HORIZONTAL_ALIGNMENT_LEFT]]:
		var l := UiArt.label(String(part[0]), 9, col)
		l.position = Vector2(float(part[1]), 0)
		l.size = Vector2(float(part[2]), ROW_H)
		l.horizontal_alignment = part[3]
		l.clip_text = true
		l.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(l)
	return row


## The Realm Skills list: name, level, the next level's cost; a realm ability whose requirements aren't met yet is
## greyed (the server bracketed its name).
func _realm_rows() -> void:
	for c in _ra_rows.get_children():
		c.queue_free()
	for ra in Server.realm_abilities:
		var idx := int(ra.index)
		var col := UiArt.GOLD_HI if idx == _selected_ra else (UiArt.CREAM if bool(ra.available) else Color(0.55, 0.55, 0.55))
		_ra_rows.add_child(_row(String(ra.name).to_upper(), str(int(ra.level)), "COST: %d" % int(ra.cost), col, func():
			_selected_ra = idx
			_selected = ""
			refresh()))
	_ra_points.text = str(Server.ra_points)
