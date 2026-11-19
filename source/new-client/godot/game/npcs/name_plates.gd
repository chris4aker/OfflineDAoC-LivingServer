class_name NamePlates
extends Node
## Overhead names, measured from the owner's recordings of the real client (2026-10-09, LOUGH DERG LEDGER.md
## "Name plates"):
## - font: the client's Minion Pro Bold (ui/fonts/minionpro-bold.ttf) with a dark outline
## - the same range for friendly NPCs and monsters: about 3.3x the camera-to-player distance (NAME_RANGE from the
##   player at the default camera); a name simply appears / disappears there (owner: no fading)
## - a SET size at every distance (owner: names never grow or shrink as you walk up to them)
## - the target's name (left click) is highlighted: bright white
## - friendly NPCs light aqua (sampled 175, 223, 208), monsters in their con colour to you
## - drawn IN the world and depth-tested: a wall or corner in front clips the name pixel by pixel, while a name above
##   a wall top stays visible (owner's video: "watch how this npcs name disappears as I leave around the corner")
## Each name is a Label3D on the NPC's view (fixed_size: one font pixel = one screen pixel), the guild line a second
## Label3D under it, offset in screen pixels.

# The name range, same for friend and foe: the owner's recordings showed names appearing at about 3.3x the camera's
# distance - and the owner's camera sits at its farthest, 500 units (the old client's saved [Camera] distance=500.00;
# game.dll clamps it to 50..500), so ~40 m (~1,600 units). The first reading took our own 3.6 m camera instead and gave
# 12 m - about a monster's aggro range (owner 2026-10-14: "I don't see an enemy's nameplate until it's in aggro range").
const NAME_RANGE := 40.0
const EM := 0.018                           # font size, a set fraction of the view height (~19 px at 1080)
const GUILD_SCALE := 0.8
const FRIENDLY := Color8(175, 223, 208)
const OUTLINE := Color(0.02, 0.04, 0.02, 0.9)

var manager: NpcManager
var _font: Font
var _size := -1                             # font px the labels were built for
var _pixel := 0.0


func _ready() -> void:
	_font = UiArt.font(true)


func _process(_delta: float) -> void:
	var t0 := Time.get_ticks_usec()
	_process_inner(_delta)
	Perf.proc_add("name plates", Time.get_ticks_usec() - t0)   # (the live log: process by part)


var _companion_names := true
var _group_oids := {}


func _process_inner(_delta: float) -> void:
	_companion_names = BotView.option("names_companions")
	_group_oids.clear()
	if Server.active:
		for m in Server.group_members:
			_group_oids[int(m.get("oid", 0))] = true
	var cam := get_viewport().get_camera_3d()
	if manager == null or cam == null or manager.player == null:
		return
	var vp_h := get_viewport().get_visible_rect().size.y
	var size := maxi(8, roundi(EM * vp_h))
	var pixel := 2.0 * tan(deg_to_rad(cam.fov * 0.5)) / vp_h
	var resized := size != _size or not is_equal_approx(pixel, _pixel)
	_size = size
	_pixel = pixel
	var pp := manager.player.global_position
	for i in manager.active:
		var n: Npc = manager.active[i]
		if not is_instance_valid(n):
			continue
		var targeted := n == manager.target
		var d := Vector2(n.global_position.x - pp.x, n.global_position.z - pp.z).length()
		var show := n.visible and n.state.alive and (targeted or (d <= NAME_RANGE
				and not int(n.spawn.flags) & (Npc.FLAG_DONTSHOWNAME | Npc.FLAG_CANTTARGET)))
		# Options > COMPANION NAMES in the server's game: your companions are your group's other members (owner
		# 2026-10-15 UI audit - the toggle only reached the offline bots)
		if show and not targeted and not _companion_names and _group_oids.has(int(n.spawn.get("oid", 0))):
			show = false
		if show and _behind_wall(n, cam):
			show = false
		var labels: Array = n.get_meta("plate", [])
		var fresh := false
		if labels.is_empty():
			if not show:
				continue
			labels = _build(n)
			n.set_meta("plate", labels)
			fresh = true
		# only on change: a Label3D rebuilds its text mesh when its colour is set, and 265 names re-coloured every
		# frame cost a raid crowd a large share of its frame time (tests/crowd_probe.gd)
		if bool(n.get_meta("plate_shown", false)) != show:
			n.set_meta("plate_shown", show)
			for l in labels:
				(l as Label3D).visible = show
		if not show:
			continue
		if resized or fresh:
			_fit(labels)
		var col: Color = Color.WHITE if targeted else (FRIENDLY if n.is_friendly() else n.con_color(PlayerData.level))
		if fresh or n.get_meta("plate_col", Color(0, 0, 0, 0)) != col:
			n.set_meta("plate_col", col)
			for l in labels:
				(l as Label3D).modulate = col


## A building wall between the camera and the name: the whole name hides (owner 2026-10-20: "names are still clipping
## thru building walls i can see edge of nameplates" - a name is a flat card much wider than its NPC; standing just
## behind a wall seen at a slant, one end of the card came out in front of it). Walls only (ZoneLoader.LAYER_WALL):
## hills and the rest still clip the name pixel by pixel. Each NPC's ray is renewed every OCC_MS, at most
## OCC_PER_FRAME rays a frame (a raid's crowd).
const OCC_MS := 150
const OCC_PER_FRAME := 24
var _occ_rays := 0
var _occ_frame := -1


func _behind_wall(n: Npc, cam: Camera3D) -> bool:
	var now := Time.get_ticks_msec()
	if Engine.get_process_frames() != _occ_frame:
		_occ_frame = Engine.get_process_frames()
		_occ_rays = 0
	if now >= int(n.get_meta("plate_occ_t", 0)) and _occ_rays < OCC_PER_FRAME:
		_occ_rays += 1
		n.set_meta("plate_occ_t", now + OCC_MS)
		var to := n.global_position + Vector3(0, n.height + 0.2, 0)
		var q := PhysicsRayQueryParameters3D.create(cam.global_position, to, ZoneLoader.LAYER_WALL)
		n.set_meta("plate_occ", not cam.get_world_3d().direct_space_state.intersect_ray(q).is_empty())
	return bool(n.get_meta("plate_occ", false))


func _build(n: Npc) -> Array:
	var out := []
	var guild := String(n.spawn.get("guild", ""))
	for text in ([String(n.spawn.name)] + (["<%s>" % guild] if guild != "" else [])):
		var l := Label3D.new()
		l.text = text
		l.font = _font
		l.billboard = BaseMaterial3D.BILLBOARD_ENABLED
		l.fixed_size = true
		l.no_depth_test = false                          # walls in front clip it, like the client
		l.shaded = false
		l.double_sided = true
		l.outline_modulate = OUTLINE
		l.vertical_alignment = VERTICAL_ALIGNMENT_BOTTOM
		l.position = Vector3(0, n.height + 0.2, 0)
		l.visible = false
		n.add_child(l)
		out.append(l)
	return out


## Font size and screen-pixel offsets for the current window: the name sits above the guild line.
func _fit(labels: Array) -> void:
	var guild_px := roundi(_size * GUILD_SCALE) if labels.size() > 1 else 0
	for k in labels.size():
		var l: Label3D = labels[k]
		l.pixel_size = _pixel
		l.font_size = _size if k == 0 else guild_px
		l.outline_size = maxi(3, roundi(l.font_size * 0.18))
		l.offset = Vector2(0, _font.get_height(guild_px) if k == 0 and guild_px > 0 else 0.0)
