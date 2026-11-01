class_name PanelSheets
extends RefCounted
## The SKILLS / MAGIC / COMBAT / GROUP sheets of the right panel, on the client's skillsheet / magicsheet /
## combstylesheet / group bitmaps (192x448; headings painted on the art). The lists fill from the character's class
## (ClassDb + PlayerData.specs): SKILLS = specializations with their levels and the points left, then abilities;
## MAGIC = spell lines and their known spells (each line a category: click its name to open / close it, as the 1.64
## manual's Spell List Panel); COMBAT = known styles. Spells and styles drag onto the quickbars
## (their icons from the client's icons.csv: base sheet icon + border overlay).

const LISTS := {
	# lists start below the heading strips' bottom edge (art rows 52-55 / 192-195): at 54 the first row's top was cut
	"skills": [Rect2(10, 57, 162, 97), Rect2(10, 197, 162, 215)],     # Specializations (+ Pts), Abilities
	"magic": [Rect2(10, 57, 162, 353)],
	"combat": [Rect2(10, 57, 162, 353)],
}
const GROUP_ROWS := 8
const GROUP_Y0 := 35.0
const GROUP_DY := 37.8
const ROW_H := 13.0


static func build(parent: Control, tab: String) -> void:
	if tab == "group":
		_group(parent)
		return
	var lists := []
	for r in LISTS.get(tab, []):
		var scroll := ScrollContainer.new()
		scroll.position = r.position
		scroll.size = r.size
		scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
		parent.add_child(scroll)
		var list := VBoxContainer.new()
		list.custom_minimum_size = Vector2(r.size.x - 8, 0)
		list.add_theme_constant_override("separation", 0)
		scroll.add_child(list)
		lists.append(list)
	var fill := func():
		for l in lists:
			for c in (l as Node).get_children():
				c.queue_free()
		if Server.active:                                  # the server's skill lists (VariousUpdate 1 / 2)
			match tab:
				"skills": _server_skills(lists)
				"magic": _server_magic(lists[0])
				"combat": _server_combat(lists[0])
			return
		match tab:
			"skills": _skills(lists)
			"magic": _magic(lists[0])
			"combat": _combat(lists[0])
	fill.call()
	PlayerData.hook(parent, PlayerData.skills_changed, func():
		if is_instance_valid(parent) and parent.is_inside_tree():
			fill.call())
	if Server.active:
		PlayerData.hook(parent, Server.skills_changed, func():
			if is_instance_valid(parent) and parent.is_inside_tree():
				fill.call())
		PlayerData.hook(parent, Server.craft_changed, func():
			if is_instance_valid(parent) and parent.is_inside_tree():
				fill.call())


static func _skills(lists: Array) -> void:
	var cid := PlayerData.class_id()
	for e in ClassDb.trainable_specs(cid, PlayerData.level):
		var key := String(e.spec)
		lists[0].add_child(_text_row(String(ClassDb.spec(key).get("name", key)), str(int(PlayerData.specs.get(key, 1)))))
	lists[0].add_child(_text_row("POINTS", str(PlayerData.spec_points()), UiArt.GOLD_HI))
	# Sprint: every class has it (old server); Quickcast with the class's ability. These drag onto the quickbar.
	lists[1].add_child(SkillRow.make("ability", 0, "Sprint", 409, -1, "").with_id("sprint"))
	if int(PlayerData.specs.get("Stealth", 0)) >= 1:            # the Stealth spec is used like an ability (hide)
		lists[1].add_child(SkillRow.make("ability", 0, "Stealth", int(ClassDb.spec("Stealth").get("icon", 0)), -1, "").with_id("stealth"))
	for a in ClassDb.abilities_known(cid, PlayerData.level, PlayerData.specs):
		if String(a.key).to_lower() in ["quickcast", "engage", "berserk", "stag", "critical shot", "rapid fire"]:   # the usable ones (Abilities.use)
			lists[1].add_child(SkillRow.make("ability", 0, String(a.name), int(ClassDb.ability(String(a.key)).get("icon", 0)), -1, "").with_id(String(a.key).to_lower()))
		else:
			lists[1].add_child(_text_row(String(a.name), ""))


# The magic sheet's spell lines are categories (DAoC Manual 1.64, Spell List Panel: "Click on the category name to
# expand the list of spells your character has learned beneath it along with their spell level. Click again to
# contract the list."): a line starts closed; the ones opened stay open while the game runs.
static var _open_lines := {}


## A spell line's category row: its name; a click opens or closes the line (key) and lays the list out again.
static func _line_row(name: String, key: String) -> Control:
	var row := _text_row(name, "", UiArt.GOLD_HI)
	row.mouse_filter = Control.MOUSE_FILTER_STOP
	row.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
	row.gui_input.connect(func(ev: InputEvent):
		if ev is InputEventMouseButton and (ev as InputEventMouseButton).pressed \
				and (ev as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
			_open_lines[key] = not bool(_open_lines.get(key, false))
			var list := row.get_parent() as VBoxContainer
			for c in list.get_children():
				list.remove_child(c)
				c.queue_free()
			if Server.active:
				_server_magic(list)
			else:
				_magic(list))
	return row


static func _magic(list: VBoxContainer) -> void:
	for line in ClassDb.spell_lines(PlayerData.class_id(), PlayerData.level, PlayerData.specs):
		list.add_child(_line_row(String(line.name), String(line.name)))
		if not bool(_open_lines.get(String(line.name), false)):
			continue
		for sid in line.spells:
			var sp := ClassDb.spell(int(sid))
			list.add_child(SkillRow.make("spell", int(sid), String(sp.get("name", "")), int(sp.get("icon_base", 0)),
					int(sp.get("icon_border", -1)), ""))


static func _combat(list: VBoxContainer) -> void:
	var last := ""
	for st in ClassDb.styles_known(PlayerData.class_id(), PlayerData.specs):
		var spec := String(st.speckeyname)
		if spec != last:
			last = spec
			list.add_child(_text_row(String(ClassDb.spec(spec).get("name", spec)), "", UiArt.GOLD_HI))
		list.add_child(SkillRow.make("style", int(st.styleid), String(st.name), int(st.get("icon_base", 0)),
				int(st.get("icon_border", -1)), str(int(st.speclevelrequirement))))


# ---------------------------------------------------------------- the server's lists (Offline DAoC Rebuilt)
# Server.skills is the server's usable list in its order: specializations first, then abilities, styles and the
# hybrids' spells (eSkillPage 0 spec, 1 ability, 2 style, 3 spell, 4 song, 5 ability spell, 6 realm ability). A skill
# is used by its place counted from the first one that isn't a spec ("srv:skill:<n>"); a caster's list spells by
# line and level ("srv:spell:<line>:<level>").

static func _first_skill() -> int:
	for i in Server.skills.size():
		if int(Server.skills[i].page) != 0:
			return i
	return Server.skills.size()


static func _server_row(kind: String, id: String, name: String, icon_kind: String, icon: int, right: String) -> SkillRow:
	var ic: Array = Server.skill_icon(icon_kind, icon)
	var r := SkillRow.make(kind, 0, name, int(ic[0]), int(ic[1]), right)
	return r.with_id(id)


static func _server_skills(lists: Array) -> void:
	var first := _first_skill()
	for i in Server.skills.size():
		var sk: Dictionary = Server.skills[i]
		var page := int(sk.page)
		if page == 0:
			var lv := int(sk.level)
			var bonus := int(sk.bonus)
			lists[0].add_child(_text_row(String(sk.name), str(lv) if bonus <= 0 else "%d+%d" % [lv, bonus]))
			# a specialization the server acts on when it is used (UseSkill type 0): Stealth hides you / brings you out
			# (StealthSpecHandler) - a row of its own here, to click or drag onto the quickbar (owner 2026-10-28, GitHub
			# issue #16: a Nightshade found no Stealth in the Abilities window and no way to hide)
			if String(sk.name) in Server.USABLE_SPECS:
				lists[1].add_child(_server_row("ability", "srv:spec:%s" % String(sk.name), String(sk.name), "icon", int(sk.icon), ""))
		elif page in [1, 5, 6]:
			lists[1].add_child(_server_row("ability", "srv:skill:%d" % (i - first), String(sk.name), "icon", int(sk.icon), ""))
	lists[0].add_child(_text_row("POINTS", str(PlayerData.spec_points()), UiArt.GOLD_HI))
	# your tradeskills (VariousUpdate 8; the SI client's skills window listed them under "Tradeskills") - a click
	# opens the tradeskill window on that trade (owner 2026-10-30)
	if not Server.craft_skills.is_empty():
		lists[1].add_child(_text_row("TRADESKILLS", "", UiArt.GOLD_HI))
		var ids: Array = Server.craft_skills.keys()
		ids.sort()
		for id in ids:
			var cs: Dictionary = Server.craft_skills[id]
			if int(cs.points) <= 1 and String(cs.name) != String(Server.info.get("craft_skill", "")):
				continue                                    # (every trade starts at 1: the ones taken up only)
			var row := _text_row(String(cs.name), str(int(cs.points)))
			row.mouse_filter = Control.MOUSE_FILTER_STOP
			row.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND
			row.tooltip_text = "Open the tradeskill window"
			var trade_id := int(id)
			row.gui_input.connect(func(ev: InputEvent):
				if ev is InputEventMouseButton and (ev as InputEventMouseButton).pressed \
						and (ev as InputEventMouseButton).button_index == MOUSE_BUTTON_LEFT:
					PlayerData.window_toggle.emit("craft:%d" % trade_id))
			lists[1].add_child(row)
		lists[1].add_child(_text_row("ALL TRADES: /CRAFT", "", UiArt.DIM))


## The server's spell lines in its order (GamePlayerUtils.UpdateUsableListSpells: the baseline lines, then the
## specialization lines), each a category of its own.
static func _server_magic(list: VBoxContainer) -> void:
	for li in Server.spell_lines.size():
		var line: Dictionary = Server.spell_lines[li]
		var key := "srv%d:%s" % [li, line.name]            # (a baseline and a spec line may share a name)
		list.add_child(_line_row(String(line.name), key))
		if not bool(_open_lines.get(key, false)):
			continue
		for sp in line.spells:
			var kind := String(sp.get("kind", "spell"))
			list.add_child(_server_row("style" if kind == "style" else "spell", "srv:spell:%d:%d" % [li, int(sp.level)],
					String(sp.name), "style" if kind == "style" else "spell", int(sp.icon), str(int(sp.level))))
	# the hybrids' spells and songs are in the skill list itself, each under its specialization as the old client: the
	# server's "special" field is the index in the list of the spec the spell's line belongs to (0xFF a song, 0xFE a
	# line without a spec) - owner 2026-10-15: a shaman's spells weren't separated by spec, all under one "SPELLS"
	var first := _first_skill()
	var groups := {}                                       # header -> [skill index], in the order first met
	var order: Array = []
	for i in Server.skills.size():
		var sk: Dictionary = Server.skills[i]
		if int(sk.page) in [3, 4]:
			var sp := int(sk.get("special", 0xFE))
			var head := "SONGS" if int(sk.page) == 4 or sp == 0xFF else "SPELLS"
			if sp >= 0 and sp < Server.skills.size() and int(Server.skills[sp].page) == 0:
				head = String(Server.skills[sp].name)
			if not groups.has(head):
				groups[head] = []
				order.append(head)
			(groups[head] as Array).append(i)
	for head in order:
		var key := "srvskill:%s" % head
		list.add_child(_line_row(String(head), key))
		if not bool(_open_lines.get(key, false)):
			continue
		# lowest level first inside each spec, as the old client lists them (owner 2026-10-16: they came in the
		# server's order, mixed by spell type); same-level spells keep the server's order
		var by_level: Array = (groups[head] as Array).duplicate()
		by_level.sort_custom(func(a, b):
			var la := int(Server.skills[a].level)
			var lb := int(Server.skills[b].level)
			return la < lb or (la == lb and int(a) < int(b)))
		for i in by_level:
			var sk: Dictionary = Server.skills[i]
			list.add_child(_server_row("spell", "srv:skill:%d" % (i - first), String(sk.name), "spell", int(sk.icon),
					str(int(sk.level))))


static func _server_combat(list: VBoxContainer) -> void:
	var first := _first_skill()
	for i in Server.skills.size():
		var sk: Dictionary = Server.skills[i]
		if int(sk.page) == 2:
			list.add_child(_server_row("style", "srv:skill:%d" % (i - first), String(sk.name), "style", int(sk.icon),
					str(int(sk.level))))


static func _text_row(left: String, right: String, col := UiArt.CREAM) -> Control:
	var row := Control.new()
	row.custom_minimum_size = Vector2(150, ROW_H)
	row.mouse_filter = Control.MOUSE_FILTER_IGNORE
	var l := UiArt.label(left, 8, col)
	l.position = Vector2(2, 0)
	l.size = Vector2(120, ROW_H)
	l.clip_text = true
	row.add_child(l)
	if right != "":
		var r := UiArt.label(right, 8, col)
		r.position = Vector2(120, 0)
		r.size = Vector2(30, ROW_H)
		r.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		row.add_child(r)
	return row


static func _roman(n: int) -> String:
	return ["", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX", "X"][clampi(n, 0, 10)]


## A spell or style in a list: its icon (+ border) and name; drag it onto a quickbar slot.
class SkillRow extends Control:
	var data := {}

	static func make(kind: String, id: int, name: String, icon: int, border: int, right: String) -> SkillRow:
		var r := SkillRow.new()
		r.data = {"kind": kind, "id": str(id), "icon": icon, "border": border, "name": name}
		r.custom_minimum_size = Vector2(150, 16)
		r.mouse_filter = Control.MOUSE_FILTER_STOP
		r.tooltip_text = name.to_upper()
		var ic := TextureRect.new()
		ic.texture = ItemDb.icon(icon)
		ic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		ic.stretch_mode = TextureRect.STRETCH_SCALE
		ic.size = Vector2(14, 14)
		ic.position = Vector2(2, 1)
		ic.mouse_filter = Control.MOUSE_FILTER_IGNORE
		r.add_child(ic)
		if border >= 0:
			var b := TextureRect.new()
			b.texture = ItemDb.border(border)
			b.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
			b.stretch_mode = TextureRect.STRETCH_SCALE
			b.size = ic.size
			b.position = ic.position
			b.mouse_filter = Control.MOUSE_FILTER_IGNORE
			r.add_child(b)
		var l := UiArt.label(name, 8, UiArt.CREAM)
		l.position = Vector2(20, 1)
		l.size = Vector2(110, 14)
		l.clip_text = true
		l.mouse_filter = Control.MOUSE_FILTER_IGNORE
		r.add_child(l)
		if right != "":
			var t := UiArt.label(right, 8, UiArt.DIM)
			t.position = Vector2(126, 1)
			t.size = Vector2(24, 14)
			t.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
			t.mouse_filter = Control.MOUSE_FILTER_IGNORE
			r.add_child(t)
		return r

	func with_id(id: String) -> SkillRow:
		data["id"] = id
		return self

	func _notification(what: int) -> void:
		if what == NOTIFICATION_MOUSE_ENTER and Server.active:
			Server.prefetch_skill(data)                    # the server's delve text, ready for the hover box

	func _make_custom_tooltip(_for_text: String) -> Object:
		return ItemTooltip.skill(data)

	func _get_drag_data(_pos: Vector2) -> Variant:
		var prev := TextureRect.new()
		prev.texture = ItemDb.icon(int(data.icon))
		prev.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		prev.size = Vector2(32, 32) * get_global_transform().get_scale()
		set_drag_preview(prev)
		return data.duplicate()

	func _gui_input(event: InputEvent) -> void:
		if event is InputEventMouseButton and event.pressed and event.double_click and event.button_index == MOUSE_BUTTON_LEFT:
			PlayerData.use_skill.emit(data)                  # double-click uses it, like a quickbar slot
		elif event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT:
			PlayerData.skill_info.emit(data)                 # right-click: its information (delve)
			accept_event()


## The group window: 8 member rows (name bar, health bar under it). Empty while you are not in a group - the client
## showed no rows when solo.
## The GROUP page on the client's group.bmp: eight member rows (leader first). Per row, measured on the art (original
## px from the row top): the name strip (name, class, level) 1-11; the health box x 5-77 / y 12-20 with the hit
## points; the power line x 5-77 / y 23-27; the enchantments area x 78-187 / y 12-33 (period Warcry guide: "the
## precise health and mana level of all the group members, as well as any enchantments"). Under the rows: INVITE,
## DISBAND and the mini group window (the period buttons; FIND is left out - no one to find).
static func _group(parent: Control) -> void:
	for i in GROUP_ROWS:
		var row := Control.new()
		row.name = "member_%d" % i
		row.position = Vector2(0, GROUP_Y0 + i * GROUP_DY)
		row.size = Vector2(192, 34)
		row.visible = false
		# a click anywhere on the row (name, bars) targets the member (owner 2026-10-14), as the old client's
		row.mouse_filter = Control.MOUSE_FILTER_STOP
		var ri := i
		row.gui_input.connect(func(e: InputEvent):
			if e is InputEventMouseButton and e.pressed and e.button_index == MOUSE_BUTTON_LEFT:
				var rows_now: Array = PlayerData.group_rows()
				if ri < rows_now.size():
					PlayerData.member_clicked.emit(rows_now[ri]))
		parent.add_child(row)
		var name := UiArt.label("", 8, UiArt.CREAM)
		name.name = "Name"
		name.position = Vector2(8, 1)
		name.size = Vector2(120, 10)
		name.clip_text = true
		row.add_child(name)
		var cls := UiArt.label("", 8, UiArt.GOLD)
		cls.name = "Class"
		cls.position = Vector2(110, 1)
		cls.size = Vector2(76, 10)
		cls.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		cls.clip_text = true
		row.add_child(cls)
		var hp := TextureRect.new()
		hp.name = "Hp"
		hp.texture = UiArt.tex("redfull")
		hp.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		hp.stretch_mode = TextureRect.STRETCH_SCALE
		hp.position = Vector2(6, 13)
		hp.size = Vector2(70, 7)
		hp.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(hp)
		var hpn := UiArt.label("", 7, UiArt.CREAM)
		hpn.name = "HpText"
		hpn.position = Vector2(6, 11)
		hpn.size = Vector2(70, 10)
		hpn.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		row.add_child(hpn)
		var pw := TextureRect.new()
		pw.name = "Pow"
		pw.texture = UiArt.tex("yellowfull")                # power is yellow (see GameUi's status bars)
		pw.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		pw.stretch_mode = TextureRect.STRETCH_SCALE
		pw.position = Vector2(6, 24)
		pw.size = Vector2(70, 3)
		pw.mouse_filter = Control.MOUSE_FILTER_IGNORE
		row.add_child(pw)
		var fx := GridContainer.new()
		fx.name = "Effects"
		fx.columns = 7
		fx.position = Vector2(80, 13)
		fx.mouse_filter = Control.MOUSE_FILTER_PASS              # (the icons' hover text; a click reaches the row)
		fx.add_theme_constant_override("h_separation", 1)
		fx.add_theme_constant_override("v_separation", 1)
		row.add_child(fx)
	var empty := UiArt.label("YOU ARE NOT IN A GROUP", 9, UiArt.DIM)
	empty.name = "Empty"
	empty.position = Vector2(0, GROUP_Y0 + 12)
	empty.size = Vector2(192, 14)
	empty.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	parent.add_child(empty)
	var buttons := [["INVITE", func(): PlayerData.invite_requested.emit("")],
			["DISBAND", func(): PlayerData.disband_requested.emit()],
			["MINI GROUP", func(): PlayerData.window_toggle.emit("minigroup")]]
	for k in buttons.size():
		var b := UiArt.button(buttons[k][0], Vector2(56, 15), 8)
		b.position = Vector2(7 + k * 60, 352)
		b.pressed.connect(buttons[k][1])
		parent.add_child(b)
	update_group(parent)


## The rows' live values (GameUI calls this a few times a second while the page is open).
static func update_group(parent: Control) -> void:
	var rows: Array = PlayerData.group_rows()
	var empty := parent.get_node_or_null("Empty")
	if empty:
		empty.visible = rows.is_empty()
	for i in GROUP_ROWS:
		var row := parent.get_node_or_null("member_%d" % i) as Control
		if row == null:
			continue
		row.visible = i < rows.size()
		if not row.visible:
			continue
		var m: Dictionary = rows[i]
		(row.get_node("Name") as Label).text = String(m.get("name", "")).to_upper()
		(row.get_node("Class") as Label).text = "%s %d" % [String(m.get("class_name", "")).to_upper(), int(m.get("level", 1))]
		(row.get_node("Hp") as Control).size.x = 70.0 * clampf(float(m.get("hp", 0.0)), 0.0, 1.0)
		(row.get_node("HpText") as Label).text = str(int(m.get("hp_pts", roundi(float(m.get("hp", 0.0)) * 100.0))))
		(row.get_node("Pow") as Control).size.x = 70.0 * clampf(float(m.get("power", 0.0)), 0.0, 1.0)
		var fx := row.get_node("Effects") as GridContainer
		var effs: Array = m.get("effects", [])
		var sig := ",".join(effs.map(func(e): return str(e.get("icon", 0))))
		if String(fx.get_meta("sig", "-")) != sig:
			fx.set_meta("sig", sig)
			for c in fx.get_children():
				c.queue_free()
			for e in effs.slice(0, 14):
				var ic := TextureRect.new()
				ic.texture = ItemDb.icon(int(e.get("icon", 0)))
				ic.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
				ic.custom_minimum_size = Vector2(14, 9.5)
				ic.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
				ic.tooltip_text = String(e.get("name", "")).to_upper()
				fx.add_child(ic)
