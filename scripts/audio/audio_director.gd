class_name AudioDirector
extends Node
## Recorded nature and runtime-synthesised audio layer for The Last Camp.
##
## Fire, wind and water crossings use credited recordings; birds, insects and
## interaction details use the [Synth] toolbox. The director owns the buses, the positional
## emitters (fire, shore, canopy) and the non-positional beds/one-shots, and it
## smooths every level change per frame so the mix never jumps.
##
## Children created by [method setup] (3D emitters use world-space positions):
## Fire, FireFeed, Shore0..N, Canopy0..N, Crickets, Owl, Bird0..3,
## Footstep0..3, Wading, Interact0..1.

const BUS_MASTER := &"Master"
const BUS_AMBIENCE := &"Ambience"
const BUS_SFX := &"SFX"
const BUS_WATER_FOLEY := &"WaterFoley"
const BUS_SCORE := &"Score"

const SURFACES: Array[StringName] = [&"grass", &"dirt", &"wood", &"water", &"rock"]
const INTERACTIONS: Array[StringName] = [&"log_added", &"lantern_toggle", &"pickup", &"ui"]

const FOOTSTEP_VARIANTS := 3
const FOOTSTEP_POOL_SIZE := 4
const BIRD_POOL_SIZE := 4
const INTERACT_POOL_SIZE := 2
const OWL_VARIANTS := 2
const BIRD_BANK_COOLDOWN_SECONDS := 20.0

## Base seed for all generation; change it to get a different (still deterministic) bank.
const BANK_SEED := 20260902
## Independent generation jobs (see _generate_group), balanced to similar cost.
const GENERATION_GROUPS := 5
## Run the generation groups on WorkerThreadPool; disable to debug recipes serially.
const USE_WORKER_THREADS := true
## Fixed seed for runtime scheduling so headless runs are reproducible.
const SCHEDULER_SEED := 7
## The whole mix rises from silence over this long when the loops start, so
## the first thing heard is never a full-volume gust.
const START_FADE_SECONDS := 2.5

## Floor used while fading a layer out completely (not a nominal mix level).
const SILENT_DB := -80.0
const FIRE_DB := -3.0
const FIRE_FEED_DB := -15.0
const WATER_DB := -19.0
## Sparse canopy gusts sit below the water, birds and fire.
## The recording has strong peaks, so both wind layers use conservative
## levels to leave room for focal sounds and quiet stretches.
const WIND_MIN_DB := -52.0
const WIND_MAX_DB := -40.0
const CANOPY_MIN_DB := -58.0
const CANOPY_MAX_DB := -40.0
const CRICKETS_MIN_DB := -36.0
const CRICKETS_MAX_DB := -24.0
const BIRDS_MIN_DB := -34.0
const BIRDS_MAX_DB := -20.0
const OWL_DB := -19.0
const WADING_MIN_DB := -24.0
const WADING_MAX_DB := -12.0

const FOOTSTEP_DB: Dictionary[StringName, float] = {
	&"grass": -17.0,
	&"dirt": -15.0,
	&"wood": -11.0,
	&"water": -12.0,
	&"rock": -13.0,
}
const INTERACT_DB: Dictionary[StringName, float] = {
	&"log_added": -13.0,
	&"lantern_toggle": -14.0,
	&"pickup": -16.0,
	&"ui": -22.0,
}

## Historical drone recipe retained for standalone DSP checks. These partials
## are not generated or played by the scene's ambient sound bank.
const DRONE_PARTIALS: Array[Dictionary] = [
	{"ratio": 1.0, "pan": 0.0, "db": 0.0, "detune": 1.0, "saw": false},
	{"ratio": 1.5, "pan": -0.45, "db": -5.0, "detune": 1.0, "saw": false},
	{"ratio": 2.0, "pan": 0.45, "db": -6.0, "detune": 1.004, "saw": false},
	{"ratio": 1.0, "pan": 0.15, "db": -9.0, "detune": 0.9985, "saw": true},
]
const DRONE_BASE_HZ := 56.0

## Bird "species": each call is a sequence of syllables (seconds/Hz), optionally
## repeated, panned to a fixed direction as if the bird sat in one tree.
const BIRD_SPECIES: Array[Dictionary] = [
	{"pan": -0.7, "level": 0.9, "repeat": 3, "gap": 0.09,
		"syllables": [{"dur": 0.06, "f0": 6500.0, "f1": 4200.0}]},
	{"pan": 0.4, "level": 0.8, "repeat": 1, "gap": 0.0,
		"syllables": [{"dur": 0.28, "f0": 2400.0, "f1": 4300.0, "vib": 9.0, "vib_depth": 0.03}]},
	{"pan": -0.3, "level": 0.7, "repeat": 1, "gap": 0.0,
		"syllables": [{"dur": 0.42, "f0": 3900.0, "f1": 3500.0, "trill": 42.0}]},
	{"pan": 0.8, "level": 0.85, "repeat": 1, "gap": 0.0,
		"syllables": [{"dur": 0.15, "f0": 3200.0, "f1": 3300.0, "gap": 0.06}, {"dur": 0.22, "f0": 4700.0, "f1": 4400.0}]},
	{"pan": 0.1, "level": 0.75, "repeat": 1, "gap": 0.0,
		"syllables": [{"dur": 0.5, "f0": 3000.0, "f1": 3200.0, "vib": 12.0, "vib_depth": 0.12}]},
	{"pan": -0.85, "level": 0.5, "repeat": 1, "gap": 0.0,
		"syllables": [{"dur": 0.35, "f0": 6800.0, "f1": 8200.0}]},
	{"pan": 0.55, "level": 0.8, "repeat": 6, "gap": 0.05,
		"syllables": [{"dur": 0.045, "f0": 5200.0, "f1": 3600.0}]},
	{"pan": -0.5, "level": 0.85, "repeat": 1, "gap": 0.0,
		"syllables": [{"dur": 0.3, "f0": 5200.0, "f1": 2600.0}]},
]


## First-order smoother for dB and pitch targets; avoids zipper noise and hard cuts.
class SmoothedValue:
	var value: float
	var target: float
	var time_constant: float

	func _init(initial: float, smoothing_seconds: float) -> void:
		value = initial
		target = initial
		time_constant = maxf(smoothing_seconds, 0.001)

	func step(delta: float) -> float:
		value = lerpf(value, target, 1.0 - exp(-delta / time_constant))
		return value

	func snap() -> void:
		value = target


## Countdown for randomly scheduled one-shots.
class Countdown:
	var remaining := 0.0

	func tick(delta: float) -> bool:
		remaining -= delta
		return remaining <= 0.0

	func reset(seconds: float) -> void:
		remaining = maxf(seconds, 0.0)


## Round-robin voice allocation so overlapping one-shots never cut each other off.
class VoicePool:
	var players: Array[AudioStreamPlayer] = []
	var _cursor := 0

	func next() -> AudioStreamPlayer:
		var player := players[_cursor]
		_cursor = (_cursor + 1) % players.size()
		return player


class VoicePool3D:
	var players: Array[AudioStreamPlayer3D] = []
	var _cursor := 0

	func next() -> AudioStreamPlayer3D:
		var player := players[_cursor]
		_cursor = (_cursor + 1) % players.size()
		return player


## Left/right-foot panned variants of one surface.
class FootstepSet:
	var left: Array[AudioStreamWAV] = []
	var right: Array[AudioStreamWAV] = []


## Milliseconds spent generating the sound bank in the last [method setup] call.
var last_generation_msec := 0

var _rng := RandomNumberGenerator.new()
var _is_setup := false
var _loops_started := false
var _time := 0.0
var _fade_elapsed := -1.0
var _master_user_db := 0.0
var _fire_position := Vector3.ZERO
var _score: AudioStreamPlayer
var _submersion := 0.0
var _underwater_filters: Array[AudioEffectLowPassFilter] = []
var _water_impacts := VoicePool3D.new()
var _splash: AudioStreamWAV
var _water_entry: AudioStreamPlayer
var _water_exit: AudioStreamPlayer
var _film_gain := 1.0

# Targets set through the public API.
var _daylight := 1.0
var _fire_intensity := 1.0
var _wind := 0.3
var _wading_active := false
var _wading_speed := 0.0
var _bird_activity := 1.0
var _cricket_activity := 0.0

# Recorded loops and generated streams.
var _fire_roar: AudioStreamWAV
var _water_loop: AudioStreamWAV
var _canopy_loop: AudioStreamWAV
var _wind_loop: AudioStreamWAV
var _cricket_bed: AudioStreamWAV
var _owl_calls: Array[AudioStreamWAV] = []
var _bird_calls: Array[AudioStreamWAV] = []
var _wading_loop: AudioStreamWAV
var _footsteps: Dictionary[StringName, FootstepSet] = {}
var _interactions: Dictionary[StringName, AudioStreamWAV] = {}

# Players.
var _fire_player: AudioStreamPlayer3D
var _fire_feed_player: AudioStreamPlayer3D
var _shore_players: Array[AudioStreamPlayer3D] = []
var _canopy_players: Array[AudioStreamPlayer3D] = []
var _canopy_phases := PackedFloat32Array()
var _wind_player: AudioStreamPlayer
var _crickets_player: AudioStreamPlayer
var _owl_player: AudioStreamPlayer
var _bird_pool := VoicePool.new()
var _footstep_pool := VoicePool.new()
var _wading_player: AudioStreamPlayer
var _interact_pool := VoicePool.new()

# Smoothed mix parameters.
var _fire_db := SmoothedValue.new(FIRE_DB, 0.6)
var _fire_pitch := SmoothedValue.new(1.0, 0.8)
var _fire_flare_db := 0.0
var _canopy_db := SmoothedValue.new(CANOPY_MIN_DB, 1.2)
var _wind_db := SmoothedValue.new(WIND_MIN_DB, 2.0)
var _water_db := SmoothedValue.new(WATER_DB, 1.5)
var _crickets_db := SmoothedValue.new(SILENT_DB, 2.0)
var _birds_db := SmoothedValue.new(BIRDS_MAX_DB, 2.0)
var _wading_db := SmoothedValue.new(SILENT_DB, 0.35)
var _wading_pitch := SmoothedValue.new(1.0, 0.35)

# One-shot schedulers.
var _wind_gust_timer := Countdown.new()
var _wind_gust_age := -1.0
var _wind_gust_duration := 12.0
var _wind_envelope := 0.0
var _owl_timer := Countdown.new()
var _bird_burst_timer := Countdown.new()
var _bird_call_timer := Countdown.new()
var _bird_calls_left := 0
var _last_bird_bank := -1
var _bird_last_played := PackedFloat64Array()
var _step_parity := 0
var _last_footstep_variant := -1


func _ready() -> void:
	if _is_setup and not _loops_started:
		_start_loops()


func _process(delta: float) -> void:
	if not _is_setup:
		return
	_time += delta
	_update_submersion(delta)
	_update_start_fade(delta)
	_update_fire(delta)
	_update_wind(delta)
	_update_canopy(delta)
	_update_water(delta)
	_update_beds(delta)
	_update_wading(delta)
	_schedule_owl(delta)
	_schedule_birds(delta)


# ---------------------------------------------------------------------------
# Public API
# ---------------------------------------------------------------------------


## Builds buses, synthesises the sound bank and creates all players. Safe to call
## again: the previous players are discarded and rebuilt.
func setup(fire_position: Vector3, shore_points: Array[Vector3], canopy_points: Array[Vector3]) -> void:
	if _is_setup:
		_clear_players()
	_rng.seed = SCHEDULER_SEED
	_fire_position = fire_position
	_ensure_buses()

	var started := Time.get_ticks_msec()
	_generate_bank()
	last_generation_msec = Time.get_ticks_msec() - started

	_build_players(shore_points, canopy_points)
	_is_setup = true
	_refresh_targets()
	_snap_all()
	_reset_schedulers()
	if is_inside_tree():
		_start_loops()


## 0 = deep night, 1 = full day. Birds fade in over the morning, crickets and the
## owl fade in at dusk; both are present in the 0.3..0.65 band.
func set_daylight(daylight: float) -> void:
	_daylight = clampf(daylight, 0.0, 1.0)
	_refresh_targets()


## 0 = out, 1 = normal, 2 = roaring.
func set_fire_intensity(intensity: float) -> void:
	_fire_intensity = clampf(intensity, 0.0, 2.0)
	_refresh_targets()


## 0..1 scales the canopy rustle and the depth/speed of gust swells.
func set_wind(strength: float) -> void:
	_wind = clampf(strength, 0.0, 1.0)
	_refresh_targets()


## Plays one footstep for [param surface] (see [constant SURFACES]), alternating a
## small left/right pan per foot and randomising pitch and level.
func footstep(surface: StringName, running: bool, gain_db := 0.0) -> void:
	if not _is_setup or not is_inside_tree():
		return
	if not _footsteps.has(surface):
		push_warning("AudioDirector: unknown footstep surface '%s'" % surface)
		return
	var sounds: FootstepSet = _footsteps[surface]
	var variant := _rng.randi_range(0, FOOTSTEP_VARIANTS - 1)
	if variant == _last_footstep_variant:
		variant = (variant + 1) % FOOTSTEP_VARIANTS
	_last_footstep_variant = variant
	_step_parity = 1 - _step_parity

	var player := _footstep_pool.next()
	player.stream = sounds.left[variant] if _step_parity == 0 else sounds.right[variant]
	player.pitch_scale = _rng.randf_range(0.92, 1.08) * (1.07 if running else 1.0)
	player.volume_db = FOOTSTEP_DB[surface] + _rng.randf_range(-3.0, 3.0) + (3.0 if running else 0.0) + gain_db
	player.play()


## Continuous slosh while moving through water; fades in and out.
func set_wading(active: bool, speed: float) -> void:
	_wading_active = active
	_wading_speed = clampf(speed, 0.0, 1.0)
	_refresh_targets()


## Kinds: log_added, lantern_toggle, pickup, ui (see [constant INTERACTIONS]).
func play_interact(kind: StringName) -> void:
	if not _is_setup or not is_inside_tree():
		return
	if not _interactions.has(kind):
		push_warning("AudioDirector: unknown interaction '%s'" % kind)
		return
	var player := _interact_pool.next()
	player.stream = _interactions[kind]
	player.pitch_scale = _rng.randf_range(0.96, 1.04)
	player.volume_db = INTERACT_DB[kind] + _rng.randf_range(-1.5, 1.5)
	player.play()
	if kind == &"log_added":
		_fire_feed_player.pitch_scale = _rng.randf_range(0.97, 1.03)
		_fire_feed_player.play()
		_fire_flare_db = 1.8


func set_master_volume(linear: float) -> void:
	var clamped := clampf(linear, 0.0, 1.0)
	_master_user_db = SILENT_DB if clamped < 1e-4 else linear_to_db(clamped)
	_apply_master()


func _apply_master() -> void:
	AudioServer.set_bus_volume_db(AudioServer.get_bus_index(BUS_MASTER), _master_user_db)


## The director's own buses rise from silence when the loops start; the
## Master bus stays the user's setting.
func _apply_start_fade() -> void:
	var fade_db := 0.0
	if _fade_elapsed >= 0.0 and _fade_elapsed < START_FADE_SECONDS:
		fade_db = lerpf(-48.0, 0.0, smoothstep(0.0, 1.0, _fade_elapsed / START_FADE_SECONDS))
	for bus: StringName in [BUS_AMBIENCE, BUS_SFX]:
		var index := AudioServer.get_bus_index(bus)
		if index != -1:
			AudioServer.set_bus_volume_db(index, fade_db + linear_to_db(maxf(_film_gain, 0.0001)) - _submersion * 5.0)
	var score_index := AudioServer.get_bus_index(BUS_SCORE)
	if score_index != -1:
		AudioServer.set_bus_volume_db(score_index, linear_to_db(maxf(_film_gain, 0.0001)))
	var water_index := AudioServer.get_bus_index(BUS_WATER_FOLEY)
	if water_index != -1:
		AudioServer.set_bus_volume_db(water_index, fade_db + linear_to_db(maxf(_film_gain, 0.0001)))


func _update_start_fade(delta: float) -> void:
	if _fade_elapsed < 0.0 or _fade_elapsed >= START_FADE_SECONDS:
		return
	_fade_elapsed += delta
	_apply_start_fade()


func _update_submersion(delta: float) -> void:
	var target := 1.0 if Game.world != null and Game.world.underwater else 0.0
	var value := move_toward(_submersion, target, delta * 4.0)
	if is_equal_approx(value, _submersion):
		return
	_submersion = value
	for filter in _underwater_filters:
		filter.cutoff_hz = lerpf(18000.0, 1100.0, _submersion)
	_apply_start_fade()


func is_ready() -> bool:
	return _is_setup


## Close water is heard at the lens. These takes already contain the muffled
## immersion tail, so do not route them through the exterior low-pass twice.
func water_crossing(entering: bool) -> void:
	if not _is_setup:
		return
	if Game.has_flag("film-quality") or Game.has_flag("cinematic"):
		print("WATER_CROSSING entering=", entering, " frames_drawn=", Engine.get_frames_drawn())
	if entering:
		_water_exit.stop()
		_water_entry.play()
	else:
		_water_entry.stop()
		_water_exit.play()


static func make_water_impact() -> AudioStreamWAV:
	var n := Synth.frames(0.52)
	var sound := Synth.noise_burst(n, 8724, 1700.0, 0.65, 0.07, 0.003)
	Synth.mix_into(sound, Synth.decaying_tone(n, 650.0, 165.0, 0.10, 0.65, 0.002))
	Synth.mix_into(sound, Synth.decaying_tone(n, 1300.0, 370.0, 0.055, 0.24, 0.003), Synth.frames(0.035))
	Synth.fade_edges(sound, 2.0, 150.0)
	return Synth.encode(Synth.normalize(sound, 0.75), false)


func water_impact(at: Vector3, hop: int) -> void:
	if not _is_setup:
		return
	var emitter := _water_impacts.next()
	emitter.position = at
	emitter.pitch_scale = 1.0 + float(hop) * 0.18
	emitter.volume_db = -9.0 - float(hop) * 2.0
	emitter.play()


func begin_film() -> void:
	_film_gain = 1.0
	_rng.seed = SCHEDULER_SEED
	_time = 0.0
	_refresh_targets()
	_snap_all()
	_reset_schedulers()
	_start_loops()


func film_fade(gain: float) -> void:
	_film_gain = clampf(gain, 0.0, 1.0)
	_apply_start_fade()


# ---------------------------------------------------------------------------
# Buses
# ---------------------------------------------------------------------------


func _ensure_buses() -> void:
	if AudioServer.get_bus_index(BUS_WATER_FOLEY) == -1:
		AudioServer.add_bus_effect(_add_bus(BUS_WATER_FOLEY), _make_limiter())
	if AudioServer.get_bus_index(BUS_AMBIENCE) == -1:
		var index := _add_bus(BUS_AMBIENCE)
		var reverb := AudioEffectReverb.new()
		reverb.room_size = 0.45
		reverb.damping = 0.5
		reverb.wet = 0.10
		reverb.dry = 1.0
		reverb.spread = 0.8
		reverb.hipass = 0.2
		reverb.predelay_msec = 40.0
		AudioServer.add_bus_effect(index, reverb)
		var low_pass := AudioEffectLowPassFilter.new()
		low_pass.cutoff_hz = 7000.0
		low_pass.resonance = 0.5
		AudioServer.add_bus_effect(index, low_pass)
		AudioServer.add_bus_effect(index, _make_limiter())

	if AudioServer.get_bus_index(BUS_SFX) == -1:
		var index := _add_bus(BUS_SFX)
		var reverb := AudioEffectReverb.new()
		reverb.room_size = 0.5
		reverb.damping = 0.6
		reverb.wet = 0.08
		reverb.dry = 1.0
		reverb.hipass = 0.25
		reverb.predelay_msec = 25.0
		AudioServer.add_bus_effect(index, reverb)
		AudioServer.add_bus_effect(index, _make_limiter())
	if AudioServer.get_bus_index(BUS_SCORE) == -1:
		var index := _add_bus(BUS_SCORE)
		var reverb := AudioEffectReverb.new()
		reverb.room_size = 0.8
		reverb.damping = 0.72
		reverb.wet = 0.30
		reverb.dry = 1.0
		AudioServer.add_bus_effect(index, reverb)
	_underwater_filters.clear()
	for bus: StringName in [BUS_AMBIENCE, BUS_SFX]:
		var index := AudioServer.get_bus_index(bus)
		var filter: AudioEffectLowPassFilter
		for effect in AudioServer.get_bus_effect_count(index):
			var existing := AudioServer.get_bus_effect(index, effect)
			if existing.resource_name == "PondSubmersion":
				filter = existing as AudioEffectLowPassFilter
		if filter == null:
			filter = AudioEffectLowPassFilter.new()
			filter.resource_name = "PondSubmersion"
			AudioServer.add_bus_effect(index, filter)
		filter.cutoff_hz = lerpf(18000.0, 1100.0, _submersion)
		_underwater_filters.append(filter)
	# The ambience and effects can peak simultaneously. Limit their sum too.
	var master := AudioServer.get_bus_index(BUS_MASTER)
	var has_limiter := false
	for i in AudioServer.get_bus_effect_count(master):
		if AudioServer.get_bus_effect(master, i).resource_name == "CampMasterLimiter":
			has_limiter = true
	if not has_limiter:
		var limiter := _make_limiter()
		limiter.resource_name = "CampMasterLimiter"
		AudioServer.add_bus_effect(master, limiter)


func _add_bus(bus_name: StringName) -> int:
	AudioServer.add_bus()
	var index := AudioServer.get_bus_count() - 1
	AudioServer.set_bus_name(index, bus_name)
	AudioServer.set_bus_send(index, BUS_MASTER)
	return index


## Safety net so summed layers can never clip the bus output.
func _make_limiter() -> AudioEffectHardLimiter:
	var limiter := AudioEffectHardLimiter.new()
	limiter.ceiling_db = -1.0
	return limiter


# ---------------------------------------------------------------------------
# Bank generation
# ---------------------------------------------------------------------------


## Synthesises the whole bank. The recipes are split into independent groups of
## roughly equal cost that run on the worker thread pool; each group writes only
## into its own result dictionary, and the main thread adopts everything afterwards.
func _generate_bank() -> void:
	var results: Array[Dictionary] = []
	for i in GENERATION_GROUPS:
		results.append({})
	if USE_WORKER_THREADS:
		var task_ids: Array[int] = []
		for i in GENERATION_GROUPS:
			task_ids.append(WorkerThreadPool.add_task(_generate_group.bind(i, results[i]), true, "AudioDirector bank %d" % i))
		for task_id in task_ids:
			WorkerThreadPool.wait_for_task_completion(task_id)
	else:
		for i in GENERATION_GROUPS:
			_generate_group(i, results[i])
	_adopt_bank(results)


func _generate_group(index: int, into: Dictionary) -> void:
	var seed_value := BANK_SEED
	match index:
		0:
			into["water"] = make_water_lapping(seed_value + 300)
		1:
			into["canopy"] = make_canopy_rustle(seed_value + 400)
			into["crickets"] = make_cricket_bed(seed_value + 500)
		2:
			into["wading"] = make_wading_loop(seed_value + 800)
		3:
			var footsteps: Dictionary[StringName, FootstepSet] = {}
			for s in SURFACES.size():
				var surface := SURFACES[s]
				var sounds := FootstepSet.new()
				for v in FOOTSTEP_VARIANTS:
					var mono := footstep_mono(surface, seed_value + 1000 + s * 50 + v * 7)
					sounds.left.append(Synth.encode(Synth.pan(mono, -0.14), true))
					sounds.right.append(Synth.encode(Synth.pan(mono, 0.14), true))
				footsteps[surface] = sounds
			into["footsteps"] = footsteps
			var interactions: Dictionary[StringName, AudioStreamWAV] = {}
			for k in INTERACTIONS.size():
				interactions[INTERACTIONS[k]] = make_interaction(INTERACTIONS[k], seed_value + 1500 + k * 7)
			into["interactions"] = interactions
		4:
			var owl := owl_call_mono(seed_value + 600)
			var owls: Array[AudioStreamWAV] = []
			for i in OWL_VARIANTS:
				owls.append(Synth.encode(Synth.pan(owl, -0.55 if i % 2 == 0 else 0.6), true))
			into["owls"] = owls
			var birds: Array[AudioStreamWAV] = []
			for i in BIRD_SPECIES.size():
				birds.append(make_bird_call(i, seed_value + 700 + i * 7))
			into["birds"] = birds
		_:
			push_error("AudioDirector: no generation group %d" % index)


func _adopt_bank(results: Array[Dictionary]) -> void:
	_fire_roar = _recorded_loop("res://audio/fire_loop.wav")
	_wind_loop = _recorded_loop("res://audio/wind_trees.wav")
	_water_loop = results[0]["water"]
	_canopy_loop = results[1]["canopy"]
	_cricket_bed = results[1]["crickets"]
	_wading_loop = results[2]["wading"]
	_footsteps = results[3]["footsteps"]
	_interactions = results[3]["interactions"]
	_owl_calls = results[4]["owls"]
	_bird_calls = results[4]["birds"]


static func _recorded_loop(path: String) -> AudioStreamWAV:
	var stream := load(path).duplicate() as AudioStreamWAV
	stream.loop_begin = 0
	stream.loop_end = int(round(stream.get_length() * stream.mix_rate))
	stream.loop_mode = AudioStreamWAV.LOOP_FORWARD
	return stream


## Historical synthesis recipes below remain available to DSP tests; the live
## fire and wind players use the credited recordings adopted above.
## The body of a wood fire: a low brown-noise rumble that pulses at 5..12 Hz
## (air drawn into the flames), a 250..700 Hz "flapping" band under its own
## slower bursts, and sparse bright sizzle. Mono, 6 s, seamless.
static func make_fire_roar(seed_value: int) -> AudioStreamWAV:
	var n := Synth.frames(6.0)
	var roar := Synth.low_pass(Synth.brown_noise(n, seed_value), 210.0, 0.9)
	Synth.multiply(roar, Synth.random_walk(n, seed_value + 1, 0.5, 0.45, 1.0))
	Synth.multiply(roar, Synth.offset(Synth.gain(Synth.random_walk(n, seed_value + 5, 8.0, 0.0, 1.0), 0.65), 0.35))
	var white := Synth.white_noise(n, seed_value + 2)
	var body := Synth.band_pass(white.duplicate(), 420.0, 0.6)
	Synth.multiply(body, Synth.power(Synth.random_walk(n, seed_value + 3, 4.5, 0.0, 1.0), 2.0))
	var texture := Synth.high_pass(white, 2600.0)
	Synth.multiply(texture, Synth.power(Synth.random_walk(n, seed_value + 4, 14.0, 0.0, 1.0), 5.0))
	Synth.mix_into(roar, body, 0, 0.32)
	Synth.mix_into(roar, texture, 0, 0.1)
	Synth.soft_clip(roar, 1.3)
	Synth.normalize(roar, 0.85)
	return Synth.loop_stream(roar, false, 400.0)


## Burning wood makes three kinds of noise: tiny ticks, mid pops and the odd
## loud snap. The variant index picks the kind (0-2 ticks, 3-5 pops, 6-7 snaps);
## pitch and length still vary per seed.
static func make_crackle(seed_value: int, variant := 0) -> AudioStreamWAV:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var pop: PackedFloat32Array
	if variant < 3:
		var duration := rng.randf_range(0.006, 0.014)
		var n := Synth.frames(duration)
		pop = Synth.noise_burst(n, seed_value + 1, rng.randf_range(3800.0, 7000.0), 1.4, duration * 0.5, 0.0003)
		Synth.mix_into(pop, Synth.click(seed_value + 2, 0.002, 3500.0), 0, 1.0)
		Synth.fade_edges(pop, 0.1, 3.0)
		Synth.normalize(pop, 0.75)
	elif variant < 6:
		var duration := rng.randf_range(0.025, 0.055)
		var n := Synth.frames(duration)
		var f := rng.randf_range(1500.0, 2600.0)
		pop = Synth.noise_burst(n, seed_value + 1, rng.randf_range(1100.0, 2400.0), rng.randf_range(2.5, 4.5), duration * 0.45, 0.0005)
		Synth.mix_into(pop, Synth.decaying_tone(n, f, f * 0.6, duration * 0.35, 0.6, 0.0005), 0, 0.7)
		Synth.mix_into(pop, Synth.click(seed_value + 2, 0.0025, 2500.0), 0, 0.8)
		Synth.fade_edges(pop, 0.2, 6.0)
		Synth.normalize(pop, 0.88)
	else:
		var duration := rng.randf_range(0.09, 0.15)
		var n := Synth.frames(duration)
		var f := rng.randf_range(900.0, 1400.0)
		pop = Synth.decaying_tone(n, f, f * 0.55, 0.045, 0.8, 0.0004)
		Synth.mix_into(pop, Synth.noise_burst(n, seed_value + 1, 1800.0, 1.2, 0.06, 0.0005), 0, 0.9)
		Synth.mix_into(pop, Synth.noise_burst(n, seed_value + 3, 170.0, 1.5, 0.05, 0.001), 0, 0.55)
		Synth.mix_into(pop, Synth.click(seed_value + 2, 0.003, 2000.0), 0, 1.0)
		Synth.fade_edges(pop, 0.2, 12.0)
		Synth.normalize(pop, 0.95)
	return Synth.encode(pop, false)


## Steam escaping from a log: a swelling high band with fast sizzle.
static func make_hiss(seed_value: int) -> AudioStreamWAV:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var duration := rng.randf_range(0.55, 0.9)
	var n := Synth.frames(duration)
	var hiss := Synth.band_pass(Synth.white_noise(n, seed_value + 1), rng.randf_range(3800.0, 5200.0), 0.6)
	Synth.multiply(hiss, Synth.exp_decay(n, duration * 0.7, 0.03))
	Synth.multiply(hiss, Synth.random_walk(n, seed_value + 2, 25.0, 0.3, 1.0))
	Synth.fade_edges(hiss, 5.0, 40.0)
	Synth.normalize(hiss, 0.7)
	return Synth.encode(hiss, false)


## Low lapping with space between swells and restrained splash detail. The longer
## take and independently offset shore emitters avoid a short recurring hiss.
static func make_water_lapping(seed_value: int) -> AudioStreamWAV:
	var seconds := 18.0
	var n := Synth.frames(seconds)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var white := Synth.white_noise(n, seed_value + 1)
	var lap := Synth.one_pole_low_pass(Synth.band_pass(white.duplicate(), 480.0, 0.65), 1800.0)
	var envelope := Synth.silence(n)
	var splash_frames: Array[int] = []
	var splash_levels: Array[float] = []
	var t := rng.randf_range(0.0, 0.5)
	while t < seconds:
		var width := rng.randf_range(0.55, 1.1)
		var level := rng.randf_range(0.35, 1.0)
		Synth.mix_into(envelope, Synth.hann(Synth.frames(width)), Synth.frames(t), level, true)
		splash_frames.append(Synth.frames(t + width * 0.45))
		splash_levels.append(level)
		t += rng.randf_range(1.3, 3.2)
	Synth.multiply(lap, envelope)
	var gurgle := Synth.band_pass(white, 220.0, 1.2)
	Synth.multiply(gurgle, envelope)
	Synth.mix_into(lap, gurgle, 0, 0.7)
	for i in splash_frames.size():
		var burst := Synth.noise_burst(Synth.frames(rng.randf_range(0.04, 0.09)), seed_value + 10 + i, rng.randf_range(800.0, 1900.0), 0.7, 0.05, 0.003)
		Synth.mix_into(lap, burst, splash_frames[i], splash_levels[i] * 0.12, true)
	Synth.normalize(lap, 0.8)
	return Synth.loop_stream(lap, false, 400.0)


## Subtle local leaves, gated by the same gusts as the recorded wind. The broad
## band moves slowly; it does not sweep like a short repeating noise effect.
static func make_canopy_rustle(seed_value: int) -> AudioStreamWAV:
	var n := Synth.frames(14.0)
	var wind := Synth.white_noise(n, seed_value)
	var leaves := wind.duplicate()
	Synth.band_pass_sweep(wind, Synth.random_walk(n, seed_value + 1, 0.13, 450.0, 950.0), 0.55)
	var gust := Synth.random_walk(n, seed_value + 2, 0.12, 0.0, 1.0)
	Synth.multiply(wind, gust)
	Synth.band_pass(leaves, 2100.0, 0.5)
	var rustle_envelope := Synth.multiply(gust.duplicate(), gust)
	Synth.multiply(rustle_envelope, Synth.random_walk(n, seed_value + 3, 9.0, 0.15, 1.0))
	Synth.multiply(leaves, rustle_envelope)
	Synth.mix_into(wind, leaves, 0, 0.16)
	Synth.normalize(wind, 0.8)
	return Synth.loop_stream(wind, false, 500.0)


## Open-air wind as heard in a clearing: a brown-noise body under slow gust
## swells, a broad mid "whoosh" that follows the gusts, and a faint airy top
## that only appears in the strongest of them. Stereo, 8 s, seamless.
static func make_wind_bed(seed_value: int) -> AudioStreamWAV:
	var n := Synth.frames(8.0)
	var gust := Synth.random_walk(n, seed_value + 1, 0.16, 0.0, 1.0)
	var body := Synth.low_pass(Synth.brown_noise(n, seed_value), 260.0, 0.8)
	Synth.multiply(body, Synth.offset(Synth.gain(gust.duplicate(), 0.7), 0.3))
	var whoosh := Synth.white_noise(n, seed_value + 2)
	Synth.band_pass_sweep(whoosh, Synth.random_walk(n, seed_value + 3, 0.4, 300.0, 700.0), 0.6)
	Synth.multiply(whoosh, Synth.power(gust.duplicate(), 2.0))
	var air := Synth.band_pass(Synth.white_noise(n, seed_value + 4), 1400.0, 0.4)
	Synth.multiply(air, Synth.power(gust.duplicate(), 3.0))
	Synth.multiply(air, Synth.random_walk(n, seed_value + 5, 6.0, 0.2, 1.0))
	Synth.mix_into(body, whoosh, 0, 0.5)
	Synth.mix_into(body, air, 0, 0.12)
	Synth.soft_clip(body, 1.1)
	Synth.normalize(body, 0.8)
	return Synth.loop_stream(Synth.widen(body, 14.0, 0.6), true, 600.0)


## 4..6 cricket "individuals", each a pulsed 4..5.3 kHz tone with its own chirp
## rhythm, pan and distance, stamped with wrap-around so the 8 s stereo loop is
## seamless by construction.
static func make_cricket_bed(seed_value: int) -> AudioStreamWAV:
	var seconds := 8.0
	var n := Synth.frames(seconds)
	var bed := Synth.silence(n * 2)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var individuals := rng.randi_range(4, 6)
	for k in individuals:
		var pulse_frames := Synth.frames(rng.randf_range(0.012, 0.024))
		var pulse := Synth.multiply(Synth.sine(pulse_frames, rng.randf_range(4000.0, 5300.0)), Synth.hann(pulse_frames))
		var pulse_period := rng.randf_range(0.028, 0.045)
		var pulses_per_chirp := rng.randi_range(3, 6)
		var chirp_period := rng.randf_range(0.35, 0.9)
		var is_trill := rng.randf() < 0.3
		var pan := rng.randf_range(-0.85, 0.85)
		var level := rng.randf_range(0.25, 1.0)
		var t := rng.randf_range(0.0, chirp_period)
		while t < seconds:
			if rng.randf() < 0.08:
				t += rng.randf_range(0.8, 2.5)
				continue
			var count := rng.randi_range(8, 16) if is_trill else pulses_per_chirp
			var chirp_level := level * rng.randf_range(0.7, 1.0)
			for p in count:
				Synth.stamp_stereo(bed, pulse, Synth.frames(t + p * pulse_period), pan, chirp_level, true)
			t += chirp_period * rng.randf_range(0.85, 1.15) + (count * pulse_period if is_trill else 0.0)
	Synth.normalize(bed, 0.8)
	return Synth.loop_stream(bed, true, 0.0)


## Stereo owl call panned to [param pan_position]; see [method owl_call_mono].
static func make_owl_call(seed_value: int, pan_position: float) -> AudioStreamWAV:
	return Synth.encode(Synth.pan(owl_call_mono(seed_value), pan_position), true)


## Two-note "hoo-hoo": slow-attack sines with a slight downward glide, a breathy
## band of noise and a low-pass for distance. Mono, 1.25 s.
static func owl_call_mono(seed_value: int) -> PackedFloat32Array:
	var n := Synth.frames(1.25)
	var phrase := Synth.silence(n)
	var notes: Array[PackedFloat32Array] = [
		PackedFloat32Array([0.0, 0.32, 355.0, 338.0, 1.0]),
		PackedFloat32Array([0.46, 0.55, 330.0, 300.0, 0.9]),
	]
	for i in notes.size():
		var note := notes[i]
		var m := Synth.frames(note[1])
		var curve := Synth.exp_curve(m, note[2], note[3])
		var tone := Synth.sine_from_curve(curve)
		Synth.mix_into(tone, Synth.sine_from_curve(Synth.gain(curve.duplicate(), 2.0), 0.18))
		var envelope := Synth.adsr(m, 0.09, 0.05, 0.85, 0.16)
		Synth.multiply(tone, envelope)
		var breath := Synth.band_pass(Synth.white_noise(m, seed_value + i), note[2] * 2.3, 2.5)
		Synth.multiply(breath, envelope)
		Synth.mix_into(tone, breath, 0, 0.12)
		Synth.mix_into(phrase, tone, Synth.frames(note[0]), note[4])
	Synth.low_pass(phrase, 2200.0)
	return Synth.normalize(phrase, 0.7)


## One call of [constant BIRD_SPECIES][species]: exponential frequency sweeps with
## optional vibrato (FM) or trill (AM), a soft second harmonic, fixed pan.
static func make_bird_call(species: int, seed_value: int) -> AudioStreamWAV:
	var spec: Dictionary = BIRD_SPECIES[species % BIRD_SPECIES.size()]
	var syllables: Array = spec["syllables"]
	var repeat: int = spec["repeat"]
	var repeat_gap: float = spec["gap"]
	var pattern_seconds := 0.0
	for syllable: Dictionary in syllables:
		pattern_seconds += float(syllable["dur"]) + float(syllable.get("gap", 0.0))
	var total := Synth.frames(pattern_seconds * repeat + repeat_gap * (repeat - 1) + 0.05)
	var phrase := Synth.silence(total)
	var cursor := 0.0
	for r in repeat:
		for syllable: Dictionary in syllables:
			var duration: float = syllable["dur"]
			var tone := _bird_syllable(duration, float(syllable["f0"]), float(syllable["f1"]), float(syllable.get("trill", 0.0)), float(syllable.get("vib", 0.0)), float(syllable.get("vib_depth", 0.0)))
			Synth.mix_into(phrase, tone, Synth.frames(cursor))
			cursor += duration + float(syllable.get("gap", 0.0))
		cursor += repeat_gap
	Synth.fade_edges(phrase, 1.0, 10.0)
	Synth.normalize(phrase, 0.8 * float(spec["level"]))
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	return Synth.encode(Synth.pan(phrase, float(spec["pan"]) + rng.randf_range(-0.05, 0.05)), true)


static func _bird_syllable(duration: float, f0: float, f1: float, trill_hz: float, vibrato_hz: float, vibrato_depth: float) -> PackedFloat32Array:
	var m := Synth.frames(duration)
	var curve := Synth.exp_curve(m, f0, f1)
	if vibrato_hz > 0.0:
		Synth.multiply(curve, Synth.lfo(m, vibrato_hz, 1.0, vibrato_depth))
	var tone := Synth.sine_from_curve(curve)
	Synth.mix_into(tone, Synth.sine_from_curve(Synth.gain(curve.duplicate(), 2.0), 0.15))
	var envelope := Synth.adsr(m, 0.006, duration * 0.3, 0.6, duration * 0.3)
	if trill_hz > 0.0:
		Synth.multiply(envelope, Synth.lfo(m, trill_hz, 0.55, 0.45))
	return Synth.multiply(tone, envelope)


## One drone partial as a 1 s loop-synchronous stereo stream (every partial is an
## integer number of Hz). Sine partials are pure; the "saw" partial is low-passed
## cyclically (filter warmed up over one period) so the loop point stays continuous.
static func make_drone_partial(index: int) -> AudioStreamWAV:
	var spec: Dictionary = DRONE_PARTIALS[index % DRONE_PARTIALS.size()]
	var n := Synth.frames(1.0)
	var frequency := DRONE_BASE_HZ * float(spec["ratio"])
	var mono: PackedFloat32Array
	if spec["saw"]:
		var doubled := Synth.saw(n * 2, frequency)
		Synth.low_pass(doubled, 240.0, 0.8)
		mono = doubled.slice(n)
	else:
		mono = Synth.sine(n, frequency)
	Synth.normalize(mono, 0.6)
	return Synth.loop_stream(Synth.pan(mono, float(spec["pan"])), true, 0.0)


## Continuous slosh for wading: a wandering mid band with churning amplitude, a
## low body and scattered splash transients. Mono, 4 s, seamless.
static func make_wading_loop(seed_value: int) -> AudioStreamWAV:
	var n := Synth.frames(4.0)
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var slosh := Synth.white_noise(n, seed_value + 1)
	Synth.band_pass_sweep(slosh, Synth.random_walk(n, seed_value + 2, 3.0, 500.0, 1900.0), 0.9)
	var churn := Synth.power(Synth.random_walk(n, seed_value + 3, 2.5, 0.0, 1.0), 1.5)
	Synth.multiply(slosh, Synth.offset(Synth.gain(churn, 0.7), 0.3))
	var body := Synth.band_pass(Synth.white_noise(n, seed_value + 4), 180.0, 1.0)
	Synth.multiply(body, Synth.random_walk(n, seed_value + 5, 1.5, 0.2, 1.0))
	Synth.mix_into(slosh, body, 0, 0.5)
	for i in 10:
		var burst := Synth.noise_burst(Synth.frames(rng.randf_range(0.03, 0.07)), seed_value + 20 + i, rng.randf_range(1500.0, 3500.0), 1.0, 0.04, 0.002)
		Synth.mix_into(slosh, burst, rng.randi_range(0, n - 1), rng.randf_range(0.3, 0.6), true)
	Synth.normalize(slosh, 0.8)
	return Synth.loop_stream(slosh, false, 250.0)


## Mono footstep for one surface; see [method make_footstep] for the stereo wrapper.
static func footstep_mono(surface: StringName, seed_value: int) -> PackedFloat32Array:
	var out: PackedFloat32Array
	match surface:
		&"grass":
			out = _footstep_grass(seed_value)
		&"dirt":
			out = _footstep_dirt(seed_value)
		&"wood":
			out = _footstep_wood(seed_value)
		&"water":
			out = _footstep_water(seed_value)
		&"rock":
			out = _footstep_rock(seed_value)
		_:
			out = _footstep_grass(seed_value)
	Synth.fade_edges(out, 0.5, 15.0)
	return Synth.normalize(out, 0.85)


## Stereo footstep one-shot for [param surface] panned to [param pan_position].
static func make_footstep(surface: StringName, seed_value: int, pan_position: float = 0.0) -> AudioStreamWAV:
	return Synth.encode(Synth.pan(footstep_mono(surface, seed_value), pan_position), true)


## Heel, then toe: a soft low thump under a crushed-grass swish, and 70..110 ms
## later a lighter, brighter brush as the toe leaves. One burst read as a
## single generic "swish"; two events with a slow attack read as a step.
static func _footstep_grass(seed_value: int) -> PackedFloat32Array:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var n := Synth.frames(0.32)
	var heel := Synth.low_pass(Synth.noise_burst(n, seed_value, 900.0, 0.7, 0.11, 0.020), 2200.0)
	Synth.mix_into(heel, Synth.decaying_tone(n, 90.0, 55.0, 0.08, 0.5, 0.004), 0, 0.8)
	var toe_offset := Synth.frames(rng.randf_range(0.07, 0.11))
	var toe := Synth.low_pass(Synth.noise_burst(n - toe_offset, seed_value + 7, 1500.0, 0.8, 0.07, 0.012), 3200.0)
	Synth.mix_into(heel, toe, toe_offset, 0.5)
	return heel


## Dry crunch with grit crackles and a short thump, then the toe's lighter grit.
static func _footstep_dirt(seed_value: int) -> PackedFloat32Array:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var n := Synth.frames(0.3)
	var crunch := Synth.noise_burst(n, seed_value, 2100.0, 0.8, 0.08, 0.006)
	for i in 7:
		Synth.mix_into(crunch, Synth.click(seed_value + 1 + i, 0.002, 3000.0), rng.randi_range(0, Synth.frames(0.08)), rng.randf_range(0.25, 0.6))
	Synth.mix_into(crunch, Synth.decaying_tone(n, 80.0, 50.0, 0.06, 0.5, 0.004), 0, 0.9)
	var toe_offset := Synth.frames(rng.randf_range(0.07, 0.11))
	var toe := Synth.noise_burst(n - toe_offset, seed_value + 9, 2600.0, 0.9, 0.05, 0.008)
	for i in 3:
		Synth.mix_into(toe, Synth.click(seed_value + 20 + i, 0.0015, 3500.0), rng.randi_range(0, Synth.frames(0.04)), rng.randf_range(0.2, 0.45))
	Synth.mix_into(crunch, toe, toe_offset, 0.45)
	return crunch


## Resonant hollow knock around 180 Hz with a click and a short body resonance.
static func _footstep_wood(seed_value: int) -> PackedFloat32Array:
	var n := Synth.frames(0.3)
	var knock := Synth.decaying_tone(n, 200.0, 175.0, 0.16, 1.0, 0.001)
	Synth.mix_into(knock, Synth.decaying_tone(n, 340.0, 330.0, 0.07, 0.35, 0.001))
	Synth.mix_into(knock, Synth.noise_burst(n, seed_value, 420.0, 3.0, 0.035, 0.001), 0, 0.35)
	Synth.mix_into(knock, Synth.click(seed_value + 1, 0.003, 1800.0), 0, 0.6)
	return knock


## Splash whose band-pass drops in pitch, a low plunk and a few rising bubble blips.
static func _footstep_water(seed_value: int) -> PackedFloat32Array:
	var rng := RandomNumberGenerator.new()
	rng.seed = seed_value
	var n := Synth.frames(0.4)
	var splash := Synth.white_noise(n, seed_value)
	Synth.band_pass_sweep(splash, Synth.exp_curve(n, 2600.0, 900.0), 1.0)
	Synth.multiply(splash, Synth.exp_decay(n, 0.22, 0.006))
	Synth.mix_into(splash, Synth.decaying_tone(n, 160.0, 90.0, 0.1, 0.45, 0.002))
	var blip_frames := Synth.frames(0.04)
	for i in 5:
		var blip := Synth.decaying_tone(blip_frames, rng.randf_range(380.0, 520.0), rng.randf_range(800.0, 1000.0), 0.03, 0.3, 0.002)
		Synth.mix_into(splash, blip, Synth.frames(rng.randf_range(0.1, 0.32)))
	return splash


## Sharp click followed by a short grainy scrape and a small knock.
static func _footstep_rock(seed_value: int) -> PackedFloat32Array:
	var n := Synth.frames(0.2)
	var scrape_offset := Synth.frames(0.012)
	var scrape := Synth.noise_burst(n - scrape_offset, seed_value, 3600.0, 1.4, 0.07, 0.008)
	Synth.multiply(scrape, Synth.random_walk(n - scrape_offset, seed_value + 1, 70.0, 0.2, 1.0))
	var step := Synth.silence(n)
	Synth.mix_into(step, Synth.click(seed_value + 2, 0.004, 3000.0), 0, 1.0)
	Synth.mix_into(step, scrape, scrape_offset, 0.5)
	Synth.mix_into(step, Synth.decaying_tone(n, 260.0, 240.0, 0.04, 0.3, 0.001))
	return step


## Mono one-shot for an interaction kind (see [constant INTERACTIONS]).
static func make_interaction(kind: StringName, seed_value: int) -> AudioStreamWAV:
	var out: PackedFloat32Array
	match kind:
		&"log_added":
			out = _interaction_log_added(seed_value)
		&"lantern_toggle":
			out = _interaction_lantern(seed_value)
		&"pickup":
			out = _interaction_pickup(seed_value)
		&"ui":
			out = _interaction_ui(seed_value)
		_:
			out = _interaction_ui(seed_value)
	Synth.fade_edges(out, 0.5, 12.0)
	Synth.normalize(out, 0.85)
	return Synth.encode(out, false)


## Wood placement thud; the recorded fire accent is played separately in space.
static func _interaction_log_added(seed_value: int) -> PackedFloat32Array:
	var n := Synth.frames(0.45)
	var thud := Synth.noise_burst(n, seed_value, 180.0, 0.7, 0.10, 0.004)
	Synth.mix_into(thud, Synth.noise_burst(n, seed_value + 1, 720.0, 0.8, 0.04, 0.002), 0, 0.35)
	return thud


## Small metallic click: two high partials, a click and a tiny body.
static func _interaction_lantern(seed_value: int) -> PackedFloat32Array:
	var n := Synth.frames(0.14)
	var out := Synth.decaying_tone(n, 2100.0, 2050.0, 0.05, 0.6, 0.0005)
	Synth.mix_into(out, Synth.decaying_tone(n, 3300.0, 3250.0, 0.035, 0.4, 0.0005))
	Synth.mix_into(out, Synth.decaying_tone(n, 600.0, 580.0, 0.02, 0.3, 0.0005))
	Synth.mix_into(out, Synth.click(seed_value, 0.002, 3000.0), 0, 0.8)
	return out


## Soft cloth/metal rustle with a faint ping.
static func _interaction_pickup(seed_value: int) -> PackedFloat32Array:
	var n := Synth.frames(0.3)
	var rustle := Synth.noise_burst(n, seed_value, 1800.0, 0.8, 0.14, 0.01)
	Synth.multiply(rustle, Synth.random_walk(n, seed_value + 1, 45.0, 0.2, 1.0))
	Synth.mix_into(rustle, Synth.decaying_tone(Synth.frames(0.1), 2600.0, 2550.0, 0.06, 0.18, 0.0005), Synth.frames(0.05))
	return rustle


## Very subtle tick.
static func _interaction_ui(seed_value: int) -> PackedFloat32Array:
	var n := Synth.frames(0.06)
	var out := Synth.decaying_tone(n, 1500.0, 1500.0, 0.025, 0.5, 0.0005)
	Synth.mix_into(out, Synth.click(seed_value, 0.0015, 2500.0), 0, 0.6)
	return out


# ---------------------------------------------------------------------------
# Players
# ---------------------------------------------------------------------------


func _build_players(shore_points: Array[Vector3], canopy_points: Array[Vector3]) -> void:
	_water_entry = _make_player("WaterEntry", preload("res://audio/water_entry.wav"), BUS_WATER_FOLEY, -4.0)
	_water_exit = _make_player("WaterExit", preload("res://audio/water_exit.wav"), BUS_WATER_FOLEY, -5.0)
	_splash = make_water_impact()
	_water_impacts.players.clear()
	for i in 3:
		_water_impacts.players.append(_make_emitter("WaterImpact%d" % i, _splash, BUS_SFX, Vector3.ZERO, 5.0, 35.0, -12.0))
	_score = null
	_fire_player = _make_emitter("Fire", _fire_roar, BUS_SFX, _fire_position, 2.5, 32.0, FIRE_DB)
	_fire_player.attenuation_filter_cutoff_hz = 6500.0

	_fire_feed_player = _make_emitter("FireFeed", preload("res://audio/fire_feed.wav"), BUS_SFX, _fire_position, 2.0, 26.0, FIRE_FEED_DB)
	_fire_feed_player.attenuation_filter_cutoff_hz = 6500.0

	_shore_players.clear()
	for i in shore_points.size():
		_shore_players.append(_make_emitter("Shore%d" % i, _water_loop, BUS_AMBIENCE, shore_points[i], 2.6, 22.0, WATER_DB))

	_canopy_players.clear()
	_canopy_phases.clear()
	for i in canopy_points.size():
		var emitter := _make_emitter("Canopy%d" % i, _canopy_loop, BUS_AMBIENCE, canopy_points[i], 5.0, 34.0, CANOPY_MIN_DB)
		emitter.attenuation_filter_cutoff_hz = 7000.0
		_canopy_players.append(emitter)
		# Phase from position so gusts travel across the trees instead of pulsing in unison.
		_canopy_phases.append(canopy_points[i].x * 0.09 + canopy_points[i].z * 0.05)

	_crickets_player = _make_player("Crickets", _cricket_bed, BUS_AMBIENCE, SILENT_DB)
	_wind_player = _make_player("Wind", _wind_loop, BUS_AMBIENCE, WIND_MIN_DB)
	_owl_player = _make_player("Owl", null, BUS_AMBIENCE, OWL_DB)

	_bird_pool.players.clear()
	for i in BIRD_POOL_SIZE:
		_bird_pool.players.append(_make_player("Bird%d" % i, null, BUS_AMBIENCE, BIRDS_MAX_DB))

	_footstep_pool.players.clear()
	for i in FOOTSTEP_POOL_SIZE:
		_footstep_pool.players.append(_make_player("Footstep%d" % i, null, BUS_SFX, -12.0))

	_wading_player = _make_player("Wading", _wading_loop, BUS_SFX, SILENT_DB)

	_interact_pool.players.clear()
	for i in INTERACT_POOL_SIZE:
		_interact_pool.players.append(_make_player("Interact%d" % i, null, BUS_SFX, -12.0))


func _make_emitter(node_name: String, stream: AudioStreamWAV, bus: StringName, at: Vector3, unit_size: float, max_distance: float, volume_db: float) -> AudioStreamPlayer3D:
	var emitter := AudioStreamPlayer3D.new()
	emitter.name = node_name
	emitter.stream = stream
	emitter.bus = bus
	emitter.position = at
	emitter.unit_size = unit_size
	emitter.max_distance = max_distance
	emitter.volume_db = volume_db
	emitter.attenuation_model = AudioStreamPlayer3D.ATTENUATION_INVERSE_DISTANCE
	add_child(emitter)
	return emitter


func _make_player(node_name: String, stream: AudioStreamWAV, bus: StringName, volume_db: float) -> AudioStreamPlayer:
	var player := AudioStreamPlayer.new()
	player.name = node_name
	player.stream = stream
	player.bus = bus
	player.volume_db = volume_db
	add_child(player)
	return player


func _start_loops() -> void:
	_loops_started = true
	_fire_player.play(_rng.randf_range(0.0, _fire_roar.get_length()))
	for player in _shore_players:
		# Offsets and slight detune decorrelate shore points that share one loop.
		player.pitch_scale = _rng.randf_range(0.985, 1.015)
		player.play(_rng.randf_range(0.0, _water_loop.get_length()))
	for player in _canopy_players:
		player.play(_rng.randf_range(0.0, _canopy_loop.get_length()))
	_wind_player.stop()
	_wind_envelope = 0.0
	_wind_gust_age = -1.0
	_crickets_player.play(_rng.randf_range(0.0, _cricket_bed.get_length()))
	_fade_elapsed = 0.0
	_apply_start_fade()


func _clear_players() -> void:
	for child in get_children():
		remove_child(child)
		child.queue_free()
	_loops_started = false
	_is_setup = false


# ---------------------------------------------------------------------------
# Per-frame mixing
# ---------------------------------------------------------------------------


## Recomputes every smoothed target from the current API state.
func _refresh_targets() -> void:
	var rain := 0.0
	if is_instance_valid(Game.world) and is_instance_valid(Game.world.weather):
		rain = Game.world.weather.rain
	var wildlife := 1.0 - smoothstep(0.08, 0.5, rain)
	_bird_activity = smoothstep(0.3, 0.75, _daylight) * wildlife
	_cricket_activity = (1.0 - smoothstep(0.25, 0.65, _daylight)) * wildlife

	_birds_db.target = SILENT_DB if _bird_activity < 0.05 else lerpf(BIRDS_MIN_DB, BIRDS_MAX_DB, _bird_activity)
	_crickets_db.target = SILENT_DB if _cricket_activity < 0.02 else lerpf(CRICKETS_MIN_DB, CRICKETS_MAX_DB, _cricket_activity)

	if _fire_intensity < 0.01:
		_fire_db.target = SILENT_DB
	else:
		_fire_db.target = FIRE_DB + 3.0 * log(_fire_intensity) / log(2.0)
	_fire_pitch.target = 0.985 + 0.015 * _fire_intensity

	_canopy_db.target = lerpf(CANOPY_MIN_DB, CANOPY_MAX_DB, _wind)
	_wind_db.target = lerpf(WIND_MIN_DB, WIND_MAX_DB, _wind)
	_water_db.target = WATER_DB + 3.0 * _wind

	_wading_db.target = lerpf(WADING_MIN_DB, WADING_MAX_DB, _wading_speed) if _wading_active else SILENT_DB
	_wading_pitch.target = 0.85 + 0.3 * _wading_speed


func _snap_all() -> void:
	for smoothed: SmoothedValue in [_fire_db, _fire_pitch, _canopy_db, _wind_db, _water_db, _crickets_db, _birds_db, _wading_db, _wading_pitch]:
		smoothed.snap()


func _reset_schedulers() -> void:
	_wind_gust_timer.reset(_rng.randf_range(2.0, 6.0))
	_wind_gust_age = -1.0
	_wind_envelope = 0.0
	_owl_timer.reset(_rng.randf_range(8.0, 25.0))
	_bird_burst_timer.reset(_rng.randf_range(12.0, 25.0))
	_bird_calls_left = 0
	_last_bird_bank = -1
	_bird_last_played.resize(_bird_calls.size())
	_bird_last_played.fill(-BIRD_BANK_COOLDOWN_SECONDS)


func _update_fire(delta: float) -> void:
	_fire_flare_db = lerpf(_fire_flare_db, 0.0, 1.0 - exp(-delta / 2.0))
	# The recording supplies its own air draw and transients. Additional periodic
	# amplitude modulation made the old noise bed sound like rustling foliage.
	_fire_player.volume_db = _fire_db.step(delta) + _fire_flare_db
	_fire_player.pitch_scale = _fire_pitch.step(delta)


func _update_canopy(delta: float) -> void:
	var base_db := _canopy_db.step(delta)
	for i in _canopy_players.size():
		var phase := _canopy_phases[i]
		var local_swell := 0.75 + 0.25 * sin(_time * 0.13 + phase)
		var envelope := _wind_envelope * local_swell
		var player := _canopy_players[i]
		player.volume_db = maxf(SILENT_DB, base_db + linear_to_db(maxf(envelope, 0.0001)))
		player.pitch_scale = 1.0


## A recorded gust rises and falls, followed by a real rest. Random offsets in
## the long filtered take avoid bringing back a recognizable short noise loop.
func _update_wind(delta: float) -> void:
	var level := _wind_db.step(delta)
	if _wind_gust_age < 0.0:
		if _wind_gust_timer.tick(delta):
			_wind_gust_age = 0.0
			_wind_gust_duration = _rng.randf_range(6.0, 12.0)
			_wind_player.play(_rng.randf_range(0.0, _wind_loop.get_length() - _wind_gust_duration))
	else:
		_wind_gust_age += delta
		if _wind_gust_age >= _wind_gust_duration:
			_wind_gust_age = -1.0
			_wind_gust_timer.reset(_rng.randf_range(18.0, 40.0) * lerpf(1.0, 0.7, _wind))
			_wind_player.stop()
	_wind_envelope = 0.0
	if _wind_gust_age >= 0.0:
		_wind_envelope = smoothstep(0.0, 3.5, _wind_gust_age) * (1.0 - smoothstep(_wind_gust_duration - 4.5, _wind_gust_duration, _wind_gust_age))
	_wind_player.volume_db = maxf(SILENT_DB, level + linear_to_db(maxf(_wind_envelope, 0.0001)))


func _update_water(delta: float) -> void:
	var level := _water_db.step(delta)
	for player in _shore_players:
		player.volume_db = level


func _update_beds(delta: float) -> void:
	_crickets_player.volume_db = _crickets_db.step(delta) + 1.5 * sin(_time * 0.21)
	_birds_db.step(delta)


func _update_wading(delta: float) -> void:
	var level := _wading_db.step(delta)
	_wading_player.pitch_scale = _wading_pitch.step(delta)
	if _wading_active and not _wading_player.playing:
		_wading_player.play(_rng.randf_range(0.0, _wading_loop.get_length()))
	elif not _wading_active and _wading_player.playing and level < SILENT_DB + 20.0:
		_wading_player.stop()
	_wading_player.volume_db = level


func _schedule_owl(delta: float) -> void:
	if not _owl_timer.tick(delta):
		return
	_owl_timer.reset(_rng.randf_range(20.0, 60.0))
	if _cricket_activity < 0.6 or not is_inside_tree():
		return
	_owl_player.stream = _owl_calls[_rng.randi_range(0, _owl_calls.size() - 1)]
	_owl_player.pitch_scale = _rng.randf_range(0.95, 1.05)
	_owl_player.volume_db = OWL_DB + _rng.randf_range(-3.0, 1.0)
	_owl_player.play()


## Each bank is already a whole phrase, sometimes with several syllables. Leave
## room after one or two phrases; never restart a pending burst through a storm.
func _schedule_birds(delta: float) -> void:
	if _bird_activity < 0.05:
		_bird_calls_left = 0
		_bird_burst_timer.remaining = maxf(_bird_burst_timer.remaining, 12.0)
		return
	if _bird_calls_left > 0:
		if _bird_call_timer.tick(delta):
			_bird_calls_left -= 1
			_bird_call_timer.reset(_rng.randf_range(0.9, 2.4))
			_play_bird()
		return
	if not _bird_burst_timer.tick(delta):
		return
	_bird_burst_timer.reset(_rng.randf_range(12.0, 35.0) / _bird_activity)
	_bird_calls_left = _rng.randi_range(1, 2)
	_bird_call_timer.reset(0.0)


func _play_bird() -> void:
	if not is_inside_tree():
		return
	var bank := _next_bird_bank()
	if bank < 0:
		return
	var player := _bird_pool.next()
	player.stream = _bird_calls[bank]
	player.pitch_scale = _rng.randf_range(0.94, 1.06)
	player.volume_db = _birds_db.value + _rng.randf_range(-3.0, 2.0)
	player.play()


func _next_bird_bank() -> int:
	var eligible: Array[int] = []
	for bank in _bird_calls.size():
		if bank != _last_bird_bank and _time - _bird_last_played[bank] >= BIRD_BANK_COOLDOWN_SECONDS:
			eligible.append(bank)
	if eligible.is_empty():
		return -1
	var selected := eligible[_rng.randi_range(0, eligible.size() - 1)]
	_last_bird_bank = selected
	_bird_last_played[selected] = _time
	return selected
