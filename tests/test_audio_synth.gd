extends "res://tests/test_case.gd"

func test_recorded_transitions_have_clean_edges_and_mix_headroom() -> void:
	for path in ["res://audio/water_entry.wav", "res://audio/water_exit.wav", "res://audio/thunder.wav"]:
		# Read the source PCM; imported samples may use Godot's QOA codec,
		# whose bytes are not signed 16-bit PCM for Synth.decode().
		var stream := AudioStreamWAV.load_from_file(path, {"compress/mode": 0})
		var samples := Synth.decode(stream)
		assert_gt(Synth.peak(samples), 0.3, "%s contains the recorded cue" % path)
		assert_lt(Synth.peak(samples), 0.9, "%s leaves summing headroom" % path)
		assert_near(samples[0], 0.0, 0.0001, "%s starts without a click" % path)
		assert_near(samples[samples.size() - 1], 0.0, 0.0001, "%s ends without a click" % path)
		assert_gt(stream.get_length(), 2.0, "%s retains its water or thunder tail" % path)
	var rain: AudioStreamOggVorbis = load("res://audio/rain.ogg")
	assert_gt(rain.get_length(), 40.0, "the storm uses a sustained recording without a short repeated hiss")


func test_recorded_nature_keeps_loop_seams_and_headroom() -> void:
	for path in ["res://audio/fire_loop.wav", "res://audio/wind_trees.wav"]:
		var pcm := AudioStreamWAV.load_from_file(path, {"compress/mode": 0})
		var samples := Synth.decode(pcm)
		assert_gt(pcm.get_length(), 25.0, "recorded nature is not a short synthetic loop")
		assert_lt(Synth.peak(samples), 0.8, "recorded nature leaves mix headroom")
		var channels := 2 if pcm.stereo else 1
		for channel in channels:
			assert_near(samples[channel], samples[samples.size() - channels + channel], 0.04, "the recorded loop seam has no large discontinuity")
		var loop := AudioDirector._recorded_loop(path)
		assert_eq(loop.loop_mode, AudioStreamWAV.LOOP_FORWARD, "imported recording loops")
		assert_eq(loop.loop_end, int(round(pcm.get_length() * pcm.mix_rate)), "loop covers the complete recording after import")
	var feed := AudioStreamWAV.load_from_file("res://audio/fire_feed.wav", {"compress/mode": 0})
	var accent := Synth.decode(feed)
	assert_false(feed.stereo, "feeding the fire uses a positional mono take")
	assert_near(accent[0], 0.0, 0.0001, "feed accent starts cleanly")
	assert_near(accent[accent.size() - 1], 0.0, 0.0001, "feed accent fades cleanly")
	assert_lt(Synth.peak(accent), 0.6, "feed accent has summing headroom")


func test_original_score_has_headroom_and_fades() -> void:
	var score := CampScore.build()
	var samples := Synth.decode(score)
	assert_true(score.stereo, "score is stereo")
	assert_gt(Synth.peak(samples), 0.05, "score contains audible music")
	assert_lt(Synth.peak(samples), 0.8, "score leaves headroom for nature and effects")
	assert_near(samples[0], 0.0, 0.0001, "score opens without a click")
	assert_near(samples[samples.size() - 1], 0.0, 0.0001, "score closes without a click")
	assert_gt(Synth.rms(samples), 0.001, "score has sustained musical energy")


func test_film_score_leaves_the_nature_passages_silent() -> void:
	var cues: Array[Dictionary] = [{start = 0.0, length = 10.0, gain = 0.4, melody = true, pads = false}]
	var samples := Synth.decode(CampScore.build(20.0, cues))
	assert_gt(Synth.rms(samples.slice(CampScore.RATE * 2, CampScore.RATE * 10)), 0.001, "the authored cue is audible")
	assert_near(Synth.peak(samples.slice(CampScore.RATE * 24, CampScore.RATE * 36)), 0.0, 1e-6, "uncued nature time contains no music")
	var film_cues := Cinematic.score_cues("one_night")
	assert_true(film_cues.is_empty(), "the current film contains no music anywhere")


func test_empty_authored_score_does_not_start_the_demo_arrangement() -> void:
	var cues := Cinematic.score_cues("storm")
	assert_true(cues.is_empty(), "the storm edit deliberately contains no music cues")
	var samples := Synth.decode(CampScore.build(3.0, cues))
	assert_near(Synth.peak(samples), 0.0, 1e-6, "an explicit empty arrangement remains silent instead of composing the demo score")
	var no_voices: Array[Dictionary] = [{start = 0.0, length = 3.0, gain = 1.0, melody = false, pads = false}]
	var without_pads := Synth.decode(CampScore.build(3.0, no_voices))
	assert_near(Synth.peak(without_pads), 0.0, 1e-6, "disabling the pad voices adds no sustained hum")


## Headless smoke tests for the Synth toolbox and the AudioDirector.

const FIRE_POSITION := Vector3(0.0, 0.3, 0.0)
const SHORE_POINTS: Array[Vector3] = [Vector3(6.0, 0.0, 2.0), Vector3(8.0, 0.0, -3.0)]
const CANOPY_POINTS: Array[Vector3] = [Vector3(-4.0, 10.0, 3.0), Vector3(5.0, 10.0, 7.0), Vector3(0.0, 11.0, -6.0)]
const GENERATION_BUDGET_MSEC := 2000


func test_loops_end_at_frame_count() -> void:
	var loops: Dictionary[String, AudioStreamWAV] = {
		"fire_roar": AudioDirector.make_fire_roar(1),
		"water": AudioDirector.make_water_lapping(2),
		"canopy": AudioDirector.make_canopy_rustle(3),
		"crickets": AudioDirector.make_cricket_bed(4),
		"drone_sine": AudioDirector.make_drone_partial(0),
		"drone_saw": AudioDirector.make_drone_partial(3),
		"wading": AudioDirector.make_wading_loop(5),
	}
	for loop_name in loops:
		var stream := loops[loop_name]
		var frame_count := Synth.stream_frame_count(stream)
		assert_true(stream.format == AudioStreamWAV.FORMAT_16_BITS, "%s is 16-bit" % loop_name)
		assert_eq(stream.mix_rate, Synth.SAMPLE_RATE, "%s mix rate" % loop_name)
		assert_true(stream.loop_mode == AudioStreamWAV.LOOP_FORWARD, "%s loops forward" % loop_name)
		assert_eq(stream.loop_begin, 0, "%s loop_begin" % loop_name)
		assert_eq(stream.loop_end, frame_count, "%s loop_end equals frame count" % loop_name)
		assert_true(frame_count >= Synth.frames(1.0), "%s is at least one second long" % loop_name)
		assert_lt(Synth.peak(Synth.decode(stream)), 1.0 + 1e-6, "%s never clips" % loop_name)
	assert_true(loops["crickets"].stereo, "cricket bed is stereo")
	assert_false(loops["fire_roar"].stereo, "fire roar is mono")

	# The crossfade shortens the loop by exactly the fade length.
	var seconds := 1.0
	var fade_ms := 100.0
	var direct := Synth.loop_stream(Synth.white_noise(Synth.frames(seconds), 9), false, fade_ms)
	var expected_frames := Synth.frames(seconds) - Synth.frames(fade_ms * 0.001)
	assert_eq(direct.loop_end, expected_frames, "crossfaded loop_end accounts for the fade")
	assert_eq(Synth.stream_frame_count(direct), expected_frames, "crossfaded frame count")


func test_normalized_samples_stay_within_full_scale() -> void:
	var buffer := Synth.gain(Synth.white_noise(Synth.frames(0.5), 11), 4.0)
	assert_gt(Synth.peak(buffer), 1.0, "test signal exceeds full scale before normalisation")
	Synth.normalize(buffer, 0.9)
	assert_near(Synth.peak(buffer), 0.9, 1e-5, "normalised peak")

	var decoded := Synth.decode(Synth.encode(buffer, false))
	assert_eq(decoded.size(), buffer.size(), "decoded sample count")
	var highest := 0.0
	for i in decoded.size():
		highest = maxf(highest, absf(decoded[i]))
	assert_lt(highest, 1.0 + 1e-6, "no decoded sample exceeds +-1.0")
	assert_near(highest, 0.9, 2.0 / Synth.INT16_SCALE, "decoded peak survives 16-bit quantisation")

	# Soft clipping keeps even hot signals inside full scale.
	var hot := Synth.soft_clip(Synth.gain(Synth.white_noise(4096, 12), 3.0), 1.5)
	assert_lt(Synth.peak(hot), 1.0 + 1e-6, "soft clip output stays within +-1.0")


func test_pink_noise_has_less_high_frequency_energy_than_white() -> void:
	var count := Synth.frames(1.0)
	var white_ratio := _difference_energy_ratio(Synth.white_noise(count, 21))
	var pink_ratio := _difference_energy_ratio(Synth.pink_noise(count, 21))
	var brown_ratio := _difference_energy_ratio(Synth.brown_noise(count, 21))
	assert_lt(pink_ratio, white_ratio * 0.5, "pink noise is darker than white noise")
	assert_lt(brown_ratio, pink_ratio, "brown noise is darker than pink noise")


func test_filters_shape_the_spectrum() -> void:
	var count := Synth.frames(0.5)
	var baseline := _difference_energy_ratio(Synth.white_noise(count, 31))
	var low := _difference_energy_ratio(Synth.low_pass(Synth.white_noise(count, 31), 1000.0))
	var high := _difference_energy_ratio(Synth.high_pass(Synth.white_noise(count, 31), 6000.0))
	var one_pole := _difference_energy_ratio(Synth.one_pole_low_pass(Synth.white_noise(count, 31), 500.0))
	assert_lt(low, baseline * 0.25, "biquad low-pass removes high-frequency energy")
	assert_gt(high, baseline * 1.2, "biquad high-pass removes low-frequency energy")
	assert_lt(one_pole, baseline * 0.5, "one-pole low-pass removes high-frequency energy")

	var swept := Synth.band_pass_sweep(Synth.white_noise(count, 31), Synth.linear_curve(count, 300.0, 3000.0), 2.0)
	assert_gt(Synth.rms(swept), 0.0, "swept band-pass produces output")
	assert_lt(Synth.peak(swept), 2.0, "swept band-pass stays stable")


func test_seamless_loop_removes_boundary_discontinuity() -> void:
	# 100.25 cycles: the raw buffer ends a quarter cycle out of phase with its start.
	var count := Synth.frames(0.5)
	var tone := Synth.sine(count, 200.5, 0.9)
	var raw_step := absf(tone[0] - tone[count - 1])
	assert_gt(raw_step, 0.5, "raw tone has a large loop discontinuity")

	var fade_ms := 50.0
	var seamless := Synth.make_seamless(tone, fade_ms, 1, Synth.SAMPLE_RATE, false)
	assert_eq(seamless.size(), count - Synth.frames(fade_ms * 0.001), "seamless buffer is shortened by the fade")
	var boundary_step := absf(seamless[0] - seamless[seamless.size() - 1])
	assert_lt(boundary_step, 0.05, "loop boundary step is below the click threshold")
	# The step at the loop point is no larger than the natural slope of the tone.
	assert_lt(boundary_step, absf(tone[1] - tone[0]) * 1.5, "boundary step is comparable to adjacent samples")

	var noisy := Synth.make_seamless(Synth.low_pass(Synth.white_noise(count, 41), 800.0), 200.0)
	var noisy_step := absf(noisy[0] - noisy[noisy.size() - 1])
	assert_lt(noisy_step, 0.1, "noise loop boundary is continuous")


func test_stereo_helpers_and_wrapped_stamping() -> void:
	var gains := Synth.pan_gains(0.0)
	assert_near(gains.x, gains.y, 1e-6, "centre pan is symmetric")
	assert_near(gains.x * gains.x + gains.y * gains.y, 1.0, 1e-6, "pan is constant power")
	assert_near(Synth.pan_gains(-1.0).y, 0.0, 1e-6, "hard left has no right signal")

	var dest := Synth.silence(20)
	var grain := Synth.constant(4, 1.0)
	Synth.stamp_stereo(dest, grain, 8, 0.0, 1.0, true)
	assert_gt(dest[16], 0.0, "stamp writes frame 8")
	assert_gt(dest[19], 0.0, "stamp writes frame 9 (right channel)")
	assert_gt(dest[0], 0.0, "stamp wraps to frame 0")
	assert_gt(dest[3], 0.0, "stamp wraps to frame 1 (right channel)")
	assert_eq(dest[4], 0.0, "stamp does not touch frame 2")

	var mono := Synth.sine(1000, 100.0, 0.5)
	var wide := Synth.widen(mono, 5.0, 0.5)
	assert_eq(wide.size(), 2000, "widen produces interleaved stereo")
	assert_lt(Synth.peak(wide), 1.0, "widen stays within full scale")
	var left := PackedFloat32Array()
	var right := PackedFloat32Array()
	for i in 1000:
		left.append(wide[i * 2])
		right.append(wide[i * 2 + 1])
	assert_eq(Synth.interleave(left, right), wide, "interleave inverts channel split")


func test_envelopes_are_bounded() -> void:
	var envelope := Synth.adsr(1000, 0.002, 0.003, 0.5, 0.005)
	assert_near(envelope[0], 0.0, 1e-6, "adsr starts at zero")
	assert_lt(Synth.peak(envelope), 1.0 + 1e-6, "adsr never exceeds one")
	assert_near(envelope[999], 0.0, 0.01, "adsr ends near zero")
	var decay := Synth.exp_decay(Synth.frames(0.1), 0.05)
	assert_near(decay[0], 1.0, 1e-6, "exp decay starts at one")
	assert_near(decay[Synth.frames(0.05)], 0.001, 1e-4, "exp decay reaches -60 dB at the decay time")


func test_generation_is_deterministic() -> void:
	assert_eq(Synth.pink_noise(2048, 42), Synth.pink_noise(2048, 42), "pink noise is reproducible for a seed")
	assert_true(Synth.pink_noise(2048, 42) != Synth.pink_noise(2048, 43), "different seeds differ")
	assert_eq(AudioDirector.make_crackle(5).data, AudioDirector.make_crackle(5).data, "crackle bytes are reproducible")
	assert_eq(AudioDirector.make_bird_call(2, 7).data, AudioDirector.make_bird_call(2, 7).data, "bird call bytes are reproducible")


func test_footstep_surfaces_produce_streams() -> void:
	var seen: Array[PackedByteArray] = []
	for surface in AudioDirector.SURFACES:
		var stream := AudioDirector.make_footstep(surface, 3, 0.1)
		assert_true(stream != null, "%s footstep returns a stream" % surface)
		if stream == null:
			continue
		assert_gt(stream.data.size(), 0, "%s footstep has data" % surface)
		assert_true(stream.stereo, "%s footstep is stereo" % surface)
		var decoded := Synth.decode(stream)
		var decoded_peak := Synth.peak(decoded)
		assert_gt(decoded_peak, 0.3, "%s footstep has audible level" % surface)
		assert_lt(decoded_peak, 1.0 + 1e-6, "%s footstep does not clip" % surface)
		assert_false(seen.has(stream.data), "%s footstep differs from other surfaces" % surface)
		seen.append(stream.data)

	for kind in AudioDirector.INTERACTIONS:
		var stream := AudioDirector.make_interaction(kind, 4)
		assert_gt(stream.data.size(), 0, "%s interaction has data" % kind)
	for species in AudioDirector.BIRD_SPECIES.size():
		assert_gt(AudioDirector.make_bird_call(species, 1).data.size(), 0, "bird species %d has data" % species)
	assert_gt(AudioDirector.make_owl_call(1, -0.5).data.size(), 0, "owl call has data")
	assert_gt(AudioDirector.make_hiss(1).data.size(), 0, "hiss has data")


func test_audio_director_setup_and_api() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	assert_true(tree != null, "tests run inside a SceneTree")
	if tree == null:
		return

	var director := AudioDirector.new()
	director.name = "Audio"
	tree.root.add_child(director)
	director.setup(FIRE_POSITION, SHORE_POINTS, CANOPY_POINTS)
	print("  bank generation took %d ms" % director.last_generation_msec)
	assert_true(director.is_ready(), "director reports ready after setup")
	assert_lt(director.last_generation_msec, GENERATION_BUDGET_MSEC, "bank generation fits the time budget")

	_assert_buses()
	_assert_players(director)

	director.set_daylight(0.5)
	director.set_fire_intensity(1.6)
	director.set_wind(0.8)
	director.set_master_volume(0.8)
	director.set_wading(true, 0.6)
	for surface in AudioDirector.SURFACES:
		director.footstep(surface, false)
		director.footstep(surface, true)
	for kind in AudioDirector.INTERACTIONS:
		director.play_interact(kind)
	assert_true(_any_playing(director, "Footstep", AudioDirector.FOOTSTEP_POOL_SIZE), "a footstep voice is playing")
	assert_true(_any_playing(director, "Interact", AudioDirector.INTERACT_POOL_SIZE), "an interaction voice is playing")
	assert_true((director.get_node("FireFeed") as AudioStreamPlayer3D).playing, "adding a log starts the recorded fire accent")
	assert_near(AudioServer.get_bus_volume_db(0), linear_to_db(0.8), 1e-4, "master volume applied")

	_simulate(director, 4.0)
	assert_true(director.get_node_or_null("FireCrackle0") == null, "no synthetic tonal crackles are layered over the recording")
	var wading := director.get_node("Wading") as AudioStreamPlayer
	assert_true(wading.playing, "wading loop starts when wading")
	assert_gt(wading.volume_db, -30.0, "wading loop faded in")

	director.set_wading(false, 0.0)
	_simulate(director, 4.0)
	assert_false(wading.playing, "wading loop stops after fading out")

	var crickets := director.get_node("Crickets") as AudioStreamPlayer
	director.set_daylight(0.0)
	director._bird_calls_left = 2
	director._schedule_birds(0.1)
	assert_eq(director._bird_calls_left, 0, "night clears a queued daytime bird burst")
	_simulate(director, 12.0)
	assert_gt(crickets.volume_db, AudioDirector.CRICKETS_MAX_DB - 4.0, "crickets fade up at night")
	director.set_daylight(1.0)
	_simulate(director, 12.0)
	assert_lt(crickets.volume_db, AudioDirector.CRICKETS_MIN_DB, "crickets fade out in daylight")

	var fire := director.get_node("Fire") as AudioStreamPlayer3D
	director.set_fire_intensity(0.0)
	_simulate(director, 6.0)
	assert_lt(fire.volume_db, -60.0, "fire fades to silence when out")
	director.set_fire_intensity(2.0)
	_simulate(director, 6.0)
	assert_gt(fire.volume_db, AudioDirector.FIRE_DB, "roaring fire is louder than normal")
	assert_gt(fire.pitch_scale, 1.0, "roaring fire is pitched up slightly")

	# Exercising the picker faster than normal scheduling proves the bank cooldown
	# and no-repeat policy independently of one lucky random film schedule.
	director._reset_schedulers()
	director._time = 100.0
	var used: Array[int] = []
	for i in director._bird_calls.size():
		var bank := director._next_bird_bank()
		assert_true(bank >= 0 and not used.has(bank), "a recent bird bank is not immediately reused")
		used.append(bank)
	assert_eq(director._next_bird_bank(), -1, "exhausted bird banks rest instead of repeating")
	director._time += AudioDirector.BIRD_BANK_COOLDOWN_SECONDS + 0.1
	var last_bank := used[used.size() - 1]
	assert_true(director._next_bird_bank() != last_bank, "bank reuse after cooldown still avoids consecutive duplicates")

	var saved_world := Game.world
	var wet_world := WorldController.new()
	wet_world.weather = CampWeather.new()
	wet_world.weather.rain = 0.8
	Game.world = wet_world
	director.set_daylight(1.0)
	director._bird_calls_left = 2
	director._schedule_birds(0.1)
	assert_eq(director._bird_calls_left, 0, "rain clears the last queued bird phrase")
	assert_near(director._bird_activity, 0.0, 0.0001, "day birds are suppressed in a storm")
	director.set_daylight(0.0)
	assert_near(director._cricket_activity, 0.0, 0.0001, "night insects are suppressed in a storm")
	Game.world = saved_world
	wet_world.weather.free()
	wet_world.free()
	director.set_daylight(1.0)

	var rest_frames := 0
	var gust_frames := 0
	for i in 600:
		director._process(0.1)
		if director._wind_envelope <= 0.0:
			rest_frames += 1
		else:
			gust_frames += 1
	assert_gt(rest_frames, 50, "wind has real quiet intervals")
	assert_gt(gust_frames, 50, "wind still supplies gradual gusts between rests")

	director.set_master_volume(1.0)
	tree.root.remove_child(director)
	director.free()


func _assert_buses() -> void:
	for bus_name: StringName in [AudioDirector.BUS_AMBIENCE, AudioDirector.BUS_SFX]:
		var index := AudioServer.get_bus_index(bus_name)
		assert_true(index != -1, "%s bus exists" % bus_name)
		if index == -1:
			continue
		assert_eq(AudioServer.get_bus_send(index), AudioDirector.BUS_MASTER, "%s routes to Master" % bus_name)
		assert_gt(AudioServer.get_bus_effect_count(index), 0, "%s has effects" % bus_name)
		assert_true(AudioServer.get_bus_effect(index, 0) is AudioEffectReverb, "%s starts with reverb" % bus_name)


func _assert_players(director: AudioDirector) -> void:
	var expected_3d: Array[String] = ["Fire", "FireFeed", "Shore0", "Shore1", "Canopy0", "Canopy1", "Canopy2"]
	for node_name in expected_3d:
		assert_true(director.get_node_or_null(node_name) is AudioStreamPlayer3D, "%s is a 3D emitter" % node_name)
	var expected_2d: Array[String] = ["Crickets", "Owl", "Bird0", "Bird3", "Footstep0", "Footstep3", "Wading", "Interact0", "Interact1"]
	for node_name in expected_2d:
		assert_true(director.get_node_or_null(node_name) is AudioStreamPlayer, "%s is a stream player" % node_name)
	assert_true(director.get_node_or_null("Shore2") == null, "only one emitter per shore point")
	assert_true(director.get_node_or_null("Canopy3") == null, "only one emitter per canopy point")
	for i in AudioDirector.DRONE_PARTIALS.size():
		assert_true(director.get_node_or_null("Drone%d" % i) == null, "the live ambience has no bass drone player")

	var fire := director.get_node("Fire") as AudioStreamPlayer3D
	assert_true(fire.playing, "fire loop is playing")
	assert_eq(fire.position, FIRE_POSITION, "fire emitter sits at the fire")
	assert_eq(fire.bus, AudioDirector.BUS_SFX, "fire uses the SFX bus")
	assert_near(fire.max_distance, 32.0, 1e-6, "fire is audible to about 30 m")
	var shore := director.get_node("Shore1") as AudioStreamPlayer3D
	assert_eq(shore.position, SHORE_POINTS[1], "shore emitter sits on its shore point")
	assert_true(shore.playing, "water lapping is playing")
	var canopy := director.get_node("Canopy2") as AudioStreamPlayer3D
	assert_eq(canopy.position, CANOPY_POINTS[2], "canopy emitter sits on its canopy point")
	assert_eq(canopy.bus, AudioDirector.BUS_AMBIENCE, "canopy uses the Ambience bus")
	var crickets := director.get_node("Crickets") as AudioStreamPlayer
	assert_true(crickets.playing, "cricket bed is playing")
	assert_true((crickets.stream as AudioStreamWAV).stereo, "cricket bed is stereo")


func _simulate(director: AudioDirector, seconds: float) -> void:
	var step := 0.1
	var steps := int(round(seconds / step))
	for i in steps:
		director._process(step)


func _any_playing(director: AudioDirector, prefix: String, count: int) -> bool:
	for i in count:
		var player := director.get_node_or_null("%s%d" % [prefix, i])
		if player is AudioStreamPlayer and (player as AudioStreamPlayer).playing:
			return true
		if player is AudioStreamPlayer3D and (player as AudioStreamPlayer3D).playing:
			return true
	return false


## Energy of the first difference relative to the signal energy: a cheap proxy
## for how much high-frequency content a buffer holds.
func _difference_energy_ratio(samples: PackedFloat32Array) -> float:
	var diff_energy := 0.0
	var energy := 0.0
	for i in range(1, samples.size()):
		var d: float = samples[i] - samples[i - 1]
		diff_energy += d * d
		energy += samples[i] * samples[i]
	return diff_energy / maxf(energy, 1e-12)
