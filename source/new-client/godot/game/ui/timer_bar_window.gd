class_name TimerBarWindow
extends ClassicWindow
## The server's timer window (0xF3 TimerWindow: crafting "Currently Making: X", "Salvaging: X", "Repairing: X"), as
## the original client's timer_window.xml (ui/isles: 256 x 96, the text centred at y 20, a green bar 128 wide at
## 64,48). It closes when the server says so (done, failed, moved, interrupted); its X only hides it - the original
## had no cancel either (the server stops a craft when it is asked for the same item again: the crafting window's STOP).

var _text: Label
var _bar: ProgressBar
var _total := 0.0
var _left := 0.0


func build() -> void:
	closable = true
	_text = UiArt.label("", 10, UiArt.CREAM, false)
	_text.position = Vector2(8, 22)
	_text.size = Vector2(240, 16)
	_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_text.clip_text = true
	add_child(_text)
	_bar = ProgressBar.new()
	_bar.position = Vector2(64, 48)
	_bar.size = Vector2(128, 12)
	_bar.show_percentage = false
	_bar.min_value = 0.0
	_bar.max_value = 1.0
	var bg := StyleBoxFlat.new()
	bg.bg_color = Color(0.05, 0.05, 0.05, 0.9)
	bg.border_color = UiArt.GOLD
	bg.set_border_width_all(1)
	var fill := StyleBoxFlat.new()
	fill.bg_color = Color(0.2, 0.75, 0.25)
	_bar.add_theme_stylebox_override("background", bg)
	_bar.add_theme_stylebox_override("fill", fill)
	_bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_bar)
	set_process(false)
	PlayerData.hook(self, Server.craft_timer, func(secs: int, title: String):
		if secs < 0:
			set_process(false)
			if visible:
				hide_window()
			return
		_text.text = title
		_total = maxf(float(secs), 0.05)
		_left = float(secs)
		_bar.value = 0.0
		set_process(true)
		show_window())


func _process(delta: float) -> void:
	_left = maxf(_left - delta, 0.0)
	_bar.value = 1.0 - _left / _total
