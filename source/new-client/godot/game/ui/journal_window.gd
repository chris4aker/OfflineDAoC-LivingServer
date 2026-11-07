class_name JournalWindow
extends ClassicWindow
## The Quest Journal (the client's questbg.bmp, 512x256; default.ini QuestLog 124,130; J): your quests down the left,
## the chosen quest's current step on the right. REMOVE QUEST gives it up ("Quest cleared!"); QUEST GUIDE (the owner's
## feature from the 0.35 OfflineDAoC) opens the quest's period Allakhazam walkthrough.

const LIST := Rect2(10, 24, 170, 222)
const ROW_H := 15.0

signal guide_requested(quest_id: String)
signal remove_requested(quest_id: String)

var selected := ""
var _list: VBoxContainer
var _body: RichTextLabel
var _guide: Button
var _remove: Button


func build() -> void:
	closable = true
	var scroll := ScrollContainer.new()
	scroll.position = LIST.position
	scroll.size = LIST.size
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	add_child(scroll)
	UiArt.themed(scroll)
	_list = VBoxContainer.new()
	_list.custom_minimum_size = Vector2(LIST.size.x - 12, 0)
	_list.add_theme_constant_override("separation", 1)
	scroll.add_child(_list)
	_body = RichTextLabel.new()
	_body.position = Vector2(190, 24)
	_body.size = Vector2(306, 196)
	_body.bbcode_enabled = true
	_body.add_theme_font_override("normal_font", UiArt.font(false))
	_body.add_theme_font_override("bold_font", UiArt.font(true))
	_body.add_theme_font_size_override("normal_font_size", 10)
	_body.add_theme_font_size_override("bold_font_size", 11)
	_body.add_theme_color_override("default_color", UiArt.CREAM)
	UiArt.themed(_body)
	add_child(_body)
	_guide = UiArt.button("QUEST GUIDE", Vector2(96, 16), 9)
	_guide.position = Vector2(296, 226)
	_guide.pressed.connect(func():
		if Server.active:                                  # the server's QUEST GUIDE: the button sent /task (each click
			Server.command("/task")                        # the next page, then the next active quest - QuestGuide.cs)
		else:
			guide_requested.emit(selected))
	add_child(_guide)
	# Living Server 1.2: the lore codex - the server's /lore pages in the LORE window (GameUi.show_lore).
	var lore_button := UiArt.button("LORE", Vector2(96, 16), 9)
	lore_button.position = Vector2(194, 226)
	lore_button.tooltip_text = "Your realm's places and bosses, and what your realm has slain"
	lore_button.pressed.connect(func():
		if Server.active:
			Server.command("/lore"))
	add_child(lore_button)
	_remove = UiArt.button("REMOVE QUEST", Vector2(96, 16), 9)
	_remove.position = Vector2(398, 226)
	_remove.pressed.connect(func():
		if Server.active:                                  # the server's quest list: QuestRemoveRequest
			if selected.begins_with("srv"):
				Server.client.call("RemoveQuest", int(selected.substr(3)) - 1)
			return
		if selected == "task":                           # /task abort (TaskSystem): a given-up task fails
			PlayerData.task_command.emit("abort")
		elif selected == "emissary":                     # FactionReputationQuest.AbortQuest: the hunt is dropped
			PlayerData.character.erase(Emissaries.SAVE_KEY)
			PlayerData.save_character()
			PlayerData.quests_changed.emit()
		elif selected != "":
			remove_requested.emit(selected))
	add_child(_remove)
	PlayerData.quests_changed.connect(refresh)


func show_window() -> void:
	super.show_window()
	refresh()


func refresh() -> void:
	if not visible:
		return
	for c in _list.get_children():
		c.queue_free()
	if Server.active:                                      # the server's journal (QuestEntry lines)
		_server_list()
		return
	var ids: Array = PlayerData.quests.keys()
	if not TaskSystem.current().is_empty():               # the /task task, at the top
		ids.push_front("task")
	if not Emissaries.hunt().is_empty():                  # the emissary's repeatable hunt
		ids.append("emissary")
	if not ids.has(selected):
		selected = String(ids[0]) if not ids.is_empty() else ""
	for id in ids:
		var q := QuestDb.quest(id) if id != "emissary" else {"name": "%s Favor: %s" % [Emissaries.mine().get("faction_name", "Faction"), Emissaries.hunt().get("target", "")]}
		if id == "task":
			q = {"name": String(TaskSystem.current().get("name", "Task"))}
		_list.add_child(_row(String(q.get("name", id)), String(id)))
	_show_selected()


## One quest in the list: its whole name, wrapped onto more lines when long (owner 2026-10-11: long names were cut off),
## without the server's "(Level 38)" tail - the level stays in the title on the right. Click to show it.
func _row(name: String, key: String) -> Control:
	var b := Button.new()
	b.text = LEVEL_TAIL.sub(name, "").to_upper()
	b.flat = true
	b.alignment = HORIZONTAL_ALIGNMENT_LEFT
	b.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	b.custom_minimum_size = Vector2(LIST.size.x - 14, ROW_H)
	b.add_theme_font_override("font", UiArt.font(true))
	b.add_theme_font_size_override("font_size", 9)
	b.add_theme_constant_override("line_spacing", -2)
	b.add_theme_color_override("font_color", UiArt.GOLD_HI if key == selected else UiArt.CREAM)
	b.add_theme_color_override("font_hover_color", UiArt.GOLD_HI)
	b.tooltip_text = name.to_upper()
	b.pressed.connect(func():
		selected = key
		refresh())
	return b


static var LEVEL_TAIL := RegEx.create_from_string(" *\\(Level [0-9]+\\)$")


func _server_list() -> void:
	var ids := Server.quests.keys()
	ids.sort()
	var keys := ids.map(func(i): return "srv%d" % int(i))
	if not keys.has(selected):
		selected = String(keys[0]) if not keys.is_empty() else ""
	for i in ids:
		_list.add_child(_row(String(Server.quests[i].name), "srv%d" % int(i)))
	_remove.disabled = selected == ""
	_guide.disabled = selected == ""
	if selected == "":
		_body.text = "[color=#9a9184]You have no quests.[/color]"
	else:
		var q: Dictionary = Server.quests.get(int(selected.substr(3)), {})
		var esc := func(t: String) -> String: return t.replace("[", "[lb]")   # "[Step #2]: ..." is text, not a tag
		var lines := ["[b]%s[/b]" % esc.call(String(q.get("name", "")).to_upper()), "", esc.call(String(q.get("text", "")))]
		for g in q.get("goals", []):                       # a reward quest's goals, done ones ticked
			lines.append(("[color=#7fd17f]DONE[/color]  " if bool(g.done) else "-  ") + esc.call(String(g.text)))
		_body.text = "\n".join(lines)


func _show_selected() -> void:
	_remove.disabled = selected == ""
	_guide.disabled = selected == "task"                  # a /task has no quest guide (the two stay separate)
	if selected == "":
		_body.text = "[color=#9a9184]You have no quests.[/color]"
		return
	if selected == "task":
		var t := TaskSystem.current()
		_body.text = "
".join(["[b]%s[/b]" % String(t.get("name", "")).to_upper(),
				"[color=#9a9184]From %s[/color]" % String(t.get("giver", "")), "", TaskSystem.describe(t)])
		return
	if selected == "emissary":
		var e := Emissaries.mine()
		_body.text = "
".join(["[b]%s FAVOR: %s[/b]" % [String(e.get("faction_name", "")).to_upper(), String(Emissaries.hunt().get("target", "")).to_upper()],
				"[color=#9a9184]Repeatable, from %s[/color]" % e.get("name", ""), "", String(e.get("text", {}).get("story", "")), "",
				Emissaries.journal_text()])
		return
	var q := QuestDb.quest(selected)
	var steps: Array = q.get("steps", [])
	var n := int(PlayerData.quests.get(selected, {}).get("step", 1))
	var st: Dictionary = steps[n - 1] if n >= 1 and n <= steps.size() else {}
	var lines := ["[b]%s[/b]" % String(q.get("name", "")).to_upper(),
			"[color=#9a9184]Level %d - %d   from %s   step %d of %d[/color]" % [int(q.min), int(q.max), String(q.giver), n, steps.size()],
			"", String(st.get("text", ""))]
	_body.text = "\n".join(lines)
