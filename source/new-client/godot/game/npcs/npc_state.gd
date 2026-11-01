class_name NpcState
extends RefCounted
## One spawn's life in the world simulation. Every spawn of the zone has one, always - the world is alive whether or
## not the player is near (owner 2026-10-09: "the world is alive, you can't just spawn monsters around the player").
## The 3D model (Npc) is only a view of this state, attached while the spawn is within view range.
## Idle / roam inside the roaming range at walking pace; health; a fight (Combat drives it: chase, swing); death
## and the respawn timer (old DB RespawnInterval seconds), back at home with full health.

enum Mode { IDLE, WALK, FIGHT, DEAD }

var index := 0
var zone_id := -1                           # the zone it belongs to (-1: added while playing - pets, quest events)
var spawn := {}
var info := {}                              # monsters.json entry for its model
var home := Vector3.ZERO
var pos := Vector3.ZERO
var yaw := 0.0
var mode := Mode.IDLE
var walk_to := Vector3.ZERO
var wait := 0.0
var roam := 0.0                             # metres
var walk_speed := 1.6
var level := 0                             # rolled from the template's level list at every spawn
var stats := [30, 30, 30, 30]               # STR CON DEX QUI (the old server autosets them from the level)
var health := 1.0                          # hp / max_hp (the target panel's bar)
var hp := 1
var max_hp := 1
var alive := true
var fighting := false                       # in combat with the player (Combat chases + swings)
var combat_until := 0                       # (server) swung / shot / cast at someone: weapons drawn until (msec)
var mezzed := false                         # spell effects (Casting): can't act / can't move / moves slower
var stunned := false
var speed_factor := 1.0
var swing_mult := 1.0                       # attack-speed debuffs (StyleCombatSpeedDebuff): swings take longer
var af_bonus := 0                          # its own armour buffs (NpcCasting.ensure_buffs)
var stat_buff := [0, 0, 0, 0]               # its own STR / CON / DEX / QUI buffs (NpcCasting.ensure_buffs)
var debuff_base := [0, 0, 0, 0]             # the player's single-stat debuffs on it (Casting._apply_hindrance)
var debuff_spec := [0, 0, 0, 0]             # dual-stat debuffs
var af_debuff := 0                          # armour factor debuffs
var absorb_debuff := 0                      # armour absorption debuffs (%)
var melee_debuff := 0                       # melee damage debuffs (%)
var resist_debuff := {}                     # damage type -> resist debuff
var nearsight := 0.0                        # nearsight: its spells' range shortened by this %
var diseased := false                       # disease: no regeneration, 15% slower, 7.5% less STR
var npc_buffs: Array = []                  # its other self buffs (damage add / shield, haste, bladeturn, procs)
var aggro := {}                             # threat: "player" / a pet's NpcState / a BotFoe -> amount (StandardMobBrain)
var target_pet: NpcState                    # the pet it fights instead of the player (most threat)
var target_bot: BotFoe                      # ...or the gamebot (bot plan phase 4)
var weapon_pick := ""                       # its melee weapon this spawn ("one_hand" / "two_hand"; both: 50/50)
var melee_forced := false                   # an archer hit while shooting fights in melee for the rest of the fight
var swing_result := ""                      # its last swing at the player: "hit" / "miss" / "parry" / "block" / "evade" / "style"
var swing_style := {}
var last_swing_on: Variant = "player"       # whom that last swing was at ("player" or a NpcState) - defensive styles                       # ...and the style it landed then (chains, BonusToDefense)
var path_i := 0                            # its patrol: the point it walks to, the direction (back and forth), the wait
var path_dir := 1
var path_wait := 0.0
var running := false                        # moving at run speed out of a fight (patrols) - the view plays "run"
var seen_at := 0                            # when the server last sent it (ticks ms; ServerWorld drops it when stale)
var prep_since := 0                         # when its view began waiting for its files (ticks ms; 0 = not waiting)
var swimming := false                       # in deep water (the server's ObjectUpdate 0x10, IsUnderwater): it swims
var stealthed := false                     # the STEALTH flag (ambushers): unseen, untargetable until it fights
var stealth_look := false                  # hidden but shown to you (a realm-mate / your group's gamebot): the green
                                           # ghost (StealthLook) - the server's NPCCreate flags2 0x04
var last_combat := 0.0                     # when it last attacked or was attacked (it gives up after 25 s without)
var no_target := false                      # everyone on its list is too far to count (it stands and waits)
var returning := false                     # walking back to its spawn after a fight (it ignores you on the way)
var events: Array = []                     # ambient triggers from its own life ("roaming", "spawning") for NpcManager
var player_hit := false                    # you (or your pet) hurt it this life: its death is your kill (XPGainers)
var scan_left := 0.0                        # seconds to its next look around for enemy-faction monsters / your pets
var hit_at := -1e9                          # when something last hurt it (Engage needs 5 quiet seconds)
var charmed_at := -1e9                     # when a charm on it last ended (no experience for 60 s)
var buffs_ready := false                   # NpcCasting set its own buffs up this life
var bladeturn := false                     # a bladeturn up: the next blow is absorbed
var bleeding := false                       # a bleed on it: no health regeneration (HealthRegenerationAmountCalculator)
var swing_left := 0.0                       # seconds to its next swing
var respawn_left := 0.0
var corpse_left := 0.0                      # the body stays a while before it vanishes
var _regen := 0.0
var run_speed := 4.0                        # metres / s when chasing (old DB Speed, DAoC units)
var view: Node3D                            # the Npc showing it, or null
var ride_clip := ""                         # on a steed (the server's Riding): the rider's pose instead of its own clips


var sim_pending := 0.0                       # time gathered while stepping at a reduced rate (NpcManager)


func _init(i: int, s: Dictionary, model_info: Dictionary) -> void:
	index = i
	spawn = s
	info = model_info
	home = Vector3(s.pos[0], s.pos[1], s.pos[2])
	pos = home
	yaw = -float(s.heading) * TAU / 4096.0                 # DAoC heading: facing (-sin a, +cos a); models face +Z
	roam = float(s.get("roam", 0)) * 0.025 if int(s.get("realm", 0)) == 0 else 0.0
	wait = randf_range(0.0, 12.0)
	roll()
	stealthed = (int(s.get("flags", 0)) & 2) != 0
	if int(s.get("speed", 0)) > 0:
		run_speed = int(s.speed) * 0.025


## NpcMovementComponent.MoveOnPath: walk the path at its max speed (each point's MaxSpeed caps it), wait WaitTime
## tenths of a second at a point, then the next: type 3 loops, 2 turns back at the ends, 1 walks it once.
func _patrol(delta: float, ground: Callable) -> void:
	var path: Dictionary = spawn.path
	var pts: Array = path.points
	if path_i < 0 or path_i >= pts.size():
		mode = Mode.IDLE
		running = false
		return
	if path_wait > 0.0:
		path_wait -= delta
		mode = Mode.IDLE
		return
	var p: Array = pts[path_i]
	var target := Vector3(float(p[0]), float(p[1]), float(p[2]))
	var to := target - pos
	to.y = 0.0
	var dist := to.length()
	if dist < 0.625:                                       # within 25 units: there
		path_wait = int(p[4]) * 0.1
		if path.get("jitter", false) and path_wait > 0.0:   # (the inns' adventurers: off at their own times, not in a file)
			path_wait *= randf_range(0.4, 1.6)
		var nxt := path_i + path_dir
		if nxt >= pts.size() or nxt < 0:
			match int(path.get("type", 3)):
				3: nxt = 0
				2:
					path_dir = -path_dir
					nxt = path_i + path_dir
				_: nxt = -1
		path_i = nxt
		return
	var speed := run_speed if run_speed > 0.0 else walk_speed
	if int(p[3]) > 0:
		speed = minf(speed, int(p[3]) * 0.025)
	running = speed > walk_speed * 1.3
	mode = Mode.WALK
	pos += to / dist * minf(dist, speed * delta)
	pos.y = ground.call(pos)
	yaw = lerp_angle(yaw, atan2(to.x, to.z), minf(1.0, delta * 6.0))


## A stat (0 STR, 1 CON, 2 DEX, 3 QUI) with its buffs and the player's debuffs (StatCalculator); disease takes 7.5% of
## STR (BuffBonusMultCategory1).
func eff(i: int) -> int:
	var v := CombatRules.debuffed_stat(int(stats[i]), int(stat_buff[i]), int(debuff_base[i]), int(debuff_spec[i]))
	if i == 0 and diseased:
		v = maxi(1, int(v * 0.925))
	return v


## Its resist to a damage type (ResistCalculator for a monster): resist debuffs, then its CON buffs / debuffs as another
## layer (contribution / 8), then its race's resist; at most 70.
func resist(damage_type: int) -> int:
	var r := -int(resist_debuff.get(damage_type, 0))
	var con := CombatRules.buff_contribution(int(stat_buff[1]), int(debuff_base[1]), int(debuff_spec[1])) / 8.0 * 100.0
	r += int((1.0 - r * 0.01) * con)
	r += CombatRules.race_resist(int(spawn.get("race", 0)), damage_type)
	return mini(r, 70)


## Its armour absorb (GameLiving.GetArmorAbsorb): 1 - (1 - L x 0.54%) x (1 - its CON / DEX buff contribution / 5.5),
## x (1 + absorption buffs - debuffs %).
func absorb() -> float:
	var con := CombatRules.buff_contribution(int(stat_buff[1]), int(debuff_base[1]), int(debuff_spec[1])) / 5.5
	var dex := CombatRules.buff_contribution(int(stat_buff[2]), int(debuff_base[2]), int(debuff_spec[2])) / 5.5
	var a := 1.0 - (1.0 - maxi(level, 0) * 0.0054) * (1.0 - (1.0 - (1.0 - con) * (1.0 - dex)))
	a *= 1.0 + mini(50, -absorb_debuff) * 0.01
	return clampf(a, 0.0, 1.0)


## Its speed share (MaxSpeedCalculator): snares / disease, and under 33% health it slows - full speed at 33%, 20% at 0.
func speed_mult() -> float:
	var m := speed_factor
	var h := float(hp) / maxf(1.0, max_hp)
	if h < 0.33:
		m *= 0.2 + h * (0.8 / 0.33)
	return m


func damage(n: int) -> void:
	hit_at = Time.get_ticks_msec() / 1000.0
	hp = maxi(0, hp - n)
	health = float(hp) / max_hp
	if hp == 0:
		die()


func die() -> void:
	alive = false
	fighting = false
	mode = Mode.DEAD
	corpse_left = 12.0
	respawn_left = maxf(float(spawn.get("respawn", 0)), 30.0)   # RespawnInterval (s); 0 in the DB -> 30 s floor
	if bool(spawn.get("temp", false)):
		respawn_left = 1e9                                 # a quest event monster: one encounter


## A fresh spawn: level picked from the template's list (old server GameNPC.LoadTemplate: "0-2" -> 0, 1 or 2),
## stats from the level (force_mob_autoset_stats), full health.
func roll() -> void:
	var levels: Array = spawn.get("levels", [spawn.get("level", 0)])
	level = int(levels[randi() % levels.size()]) if levels.size() > 0 else int(spawn.get("level", 0))
	stats = CombatRules.npc_stats(level, spawn.get("stats", [30, 30, 30, 30]), float(spawn.get("martial", 1.0)))
	af_bonus = 0
	stat_buff = [0, 0, 0, 0]
	aggro = {}
	target_pet = null
	target_bot = null
	weapon_pick = ""
	melee_forced = false
	swing_result = ""
	swing_style = {}
	player_hit = false
	npc_buffs = []
	bladeturn = false
	buffs_ready = false
	max_hp = CombatRules.npc_max_hp(level, int(stats[1]))
	hp = max_hp
	health = 1.0


## StandardMobState_RETURN_TO_SPAWN: the fight is over - its threat list cleared, it walks back to its spawn point
## (walk speed) and faces the way it was placed.
func go_home() -> void:
	fighting = false
	aggro = {}
	target_pet = null
	target_bot = null
	mode = Mode.WALK
	walk_to = home
	returning = not spawn.has("path")                  # a patrol picks its path up again instead (IDLE -> PATROLLING)
	stealthed = alive and (int(spawn.get("flags", 0)) & 2) != 0   # StandardMobState_AGGRO.Exit: hidden again


func respawn() -> void:
	returning = false
	path_i = 0
	path_dir = 1
	path_wait = 0.0
	stealthed = (int(spawn.get("flags", 0)) & 2) != 0      # GameNPC respawn: WasStealthed
	events.append("spawning")
	alive = true
	roll()
	health = 1.0
	pos = home
	mode = Mode.IDLE
	wait = randf_range(2.0, 8.0)


func step(delta: float, ground: Callable) -> void:
	if not alive:
		corpse_left -= delta
		respawn_left -= delta
		if respawn_left <= 0.0:
			respawn()
		return
	if fighting:
		# in combat the old server still regenerates it, slowly: (2.5 + L x 0.25) / 2 every 30 s
		if hp < max_hp and not bleeding and not diseased:
			_regen += delta
			if _regen >= 30.0:
				_regen -= 30.0
				hp = mini(max_hp, hp + maxi(1, int((2.5 + level * 0.25) / 2.0)))
				health = float(hp) / max_hp
		return                                             # Combat moves it while it fights
	if hp < max_hp and not bleeding and not diseased:       # old server: 12.5% of max HP every 6 s out of combat
		_regen += delta
		if _regen >= 6.0:
			_regen -= 6.0
			hp = mini(max_hp, hp + maxi(1, int(max_hp * 0.125)))
			health = float(hp) / max_hp
	if spawn.has("path") and not returning:
		_patrol(delta, ground)
		return
	running = false
	if roam <= 0.0 and not returning:
		return
	if mode == Mode.IDLE:
		if returning:
			mode = Mode.WALK
			return
		wait -= delta
		if wait <= 0.0:
			var a := randf() * TAU
			var r := sqrt(randf()) * roam
			walk_to = home + Vector3(cos(a) * r, 0.0, sin(a) * r)
			mode = Mode.WALK
			events.append("roaming")
		return
	var to := walk_to - pos
	to.y = 0.0
	var dist := to.length()
	if dist < 0.1:
		mode = Mode.IDLE
		wait = randf_range(4.0, 14.0)
		if returning:
			returning = false
			yaw = -float(spawn.heading) * TAU / 4096.0     # TurnTo(SpawnHeading)
		return
	pos += to / dist * minf(dist, walk_speed * delta)
	pos.y = ground.call(pos)
	yaw = lerp_angle(yaw, atan2(to.x, to.z), minf(1.0, delta * 6.0))
