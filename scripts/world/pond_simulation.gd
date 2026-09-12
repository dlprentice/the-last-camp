class_name PondSimulation
extends RefCounted
## Persistent, local disturbances added to the pond's analytic base waves.
## UV = (world_xz - bounds.position) / bounds.size; samples lie at texel centres.
## RGBA = height (m), vertical velocity (m/s), foam [0,1], water mask (1 = water).
## This is a damped wave equation, not a mass-conserving shallow-water/3D solver.
## Main-RD ownership follows Godot's native compute texture example:
## https://github.com/godotengine/godot-demo-projects/tree/master/compute/texture
## https://docs.godotengine.org/en/stable/classes/class_renderingdevice.html#class-renderingdevice-method-buffer-get-data-async
## The integration and solver below are original; no addon or copied demo shader.

signal ready_changed(available: bool)

const FIXED_STEP := 1.0 / 120.0
const MAX_SUBSTEPS := 8
const MAX_IMPULSES := 32
const MAX_WAKES := 8
const MAX_PROBES := 32
const MAX_HEIGHT := 0.055
const MAX_VELOCITY := 0.8
const WAVE_SPEED := 1.65
const VELOCITY_DAMPING := 0.65
const HEIGHT_DAMPING := 0.04
const FOAM_DECAY := 0.42
const PROBE_INTERVAL := 1.0 / 30.0
const SIMULATION_SHADER := "res://shaders/compute/pond_simulation.glsl"
const PROBE_SHADER := "res://shaders/compute/pond_probes.glsl"

## Set false before setup for an explicitly disabled, neutral fallback.
var enabled := true

var _bounds := Rect2()
var _resolution := 256
var _active := false
var _ready := false
var _session := 0
var _accumulator := 0.0
var _simulation_time := 0.0
var _probe_elapsed := 0.0
var _impulses: Array[Vector4] = []
var _wake_starts: Array[Vector4] = []
var _wake_ends: Array[Vector4] = []
var _probe_positions := PackedVector2Array()
var _sampled_positions := PackedVector2Array()
var _probe_heights := PackedFloat32Array()
var _probe_layout_id := 0
var _probe_generation := 0
var _probe_serial := 0
var _sampled_serial := -1
var _sampled_time := -1.0
var _neutral: ImageTexture
var _texture: Texture2D
var _gpu: GPUState


func _init() -> void:
	var zero := Image.create(1, 1, false, Image.FORMAT_RGBAF)
	zero.fill(Color(0, 0, 0, 0))
	_neutral = ImageTexture.create_from_image(zero)
	_texture = _neutral


## Red >= 0.5 in solid_mask denotes land/rock; black denotes water.
## Returns whether GPU setup was queued, not whether it has completed.
## A new setup is a fresh session: prior disturbances/probes are discarded.
func setup(bounds: Rect2, solid_mask: Image, resolution: int = 256) -> bool:
	shutdown()
	_bounds = bounds
	_resolution = clampi(resolution, 32, 512)
	if not bounds.position.is_finite() or not bounds.size.is_finite() or bounds.size.x <= 0.0 or bounds.size.y <= 0.0:
		push_warning("PondSimulation: bounds must have finite, positive size.")
		return false
	if not enabled or DisplayServer.get_name() == "headless" or RenderingServer.get_rendering_device() == null:
		return false
	var simulation_file := load(SIMULATION_SHADER) as RDShaderFile
	var probe_file := load(PROBE_SHADER) as RDShaderFile
	if simulation_file == null or probe_file == null:
		push_warning("PondSimulation: compute shaders must be imported before use.")
		return false
	var simulation_spirv := simulation_file.get_spirv()
	var probe_spirv := probe_file.get_spirv()
	if not simulation_spirv.get_stage_compile_error(RenderingDevice.SHADER_STAGE_COMPUTE).is_empty() or not probe_spirv.get_stage_compile_error(RenderingDevice.SHADER_STAGE_COMPUTE).is_empty():
		push_warning("PondSimulation: a compute shader failed to compile; using neutral water.")
		return false
	var mask: Image = null
	if solid_mask != null and not solid_mask.is_empty():
		mask = solid_mask.duplicate() as Image
		if mask.get_width() != _resolution or mask.get_height() != _resolution:
			mask.resize(_resolution, _resolution, Image.INTERPOLATE_NEAREST)
	var initial := PackedFloat32Array()
	initial.resize(_resolution * _resolution * 4)
	for z in _resolution:
		for x in _resolution:
			var water := 1.0
			if mask != null and mask.get_pixel(x, z).r >= 0.5:
				water = 0.0
			initial[(z * _resolution + x) * 4 + 3] = water
	_active = true
	_gpu = GPUState.new()
	_gpu.owner = weakref(self)
	_gpu.session = _session
	_gpu.bounds = _bounds
	_gpu.resolution = _resolution
	var state := _gpu
	var bytes := initial.to_byte_array()
	RenderingServer.call_on_render_thread(func() -> void:
		state.initialize(bytes, simulation_spirv, probe_spirv))
	return true


func is_ready() -> bool:
	return _ready


## Neutral until ready_changed(true); then this is one stable Texture2DRD.
## Rebind after setup becomes ready. Its RID does not rotate during stepping.
func get_texture() -> Texture2D:
	return _texture


## A signed velocity kick in m/s. A balanced radial profile limits level drift.
## Events exceeding a dispatch's capacity remain queued for the next dispatch.
func queue_impulse(world_xz: Vector2, radius: float, velocity_impulse: float) -> void:
	if not _active or not world_xz.is_finite() or not is_finite(radius) or not is_finite(velocity_impulse):
		return
	var minimum_radius := maxf(_bounds.size.x, _bounds.size.y) / float(_resolution) * 1.25
	_impulses.append(Vector4(world_xz.x, world_xz.y, maxf(radius, minimum_radius), clampf(velocity_impulse, -MAX_VELOCITY, MAX_VELOCITY)))


## Difference of two hull footprints: movement deposits a signed bow/stern wake.
## strength is m/s; the footprint difference already scales with travel distance.
## Do not call with an artificial oscillation when the hull has not moved.
func queue_wake(from_xz: Vector2, to_xz: Vector2, radius: float, strength: float) -> void:
	if not _active or not from_xz.is_finite() or not to_xz.is_finite() or not is_finite(radius) or not is_finite(strength):
		return
	if from_xz.distance_squared_to(to_xz) < 0.00000001:
		return
	var minimum_radius := maxf(_bounds.size.x, _bounds.size.y) / float(_resolution) * 1.5
	_wake_starts.append(Vector4(from_xz.x, from_xz.y, maxf(radius, minimum_radius), clampf(strength, -MAX_VELOCITY, MAX_VELOCITY)))
	_wake_ends.append(Vector4(to_xz.x, to_xz.y, 0.0, 0.0))


## Keep stable slots while moving. Increment layout_id when assigning slots to
## different bodies, even if the count is unchanged. Layout changes invalidate
## in-flight results; ordinary motion deliberately allows delayed GPU samples.
func set_probe_positions(positions: PackedVector2Array, layout_id: int = 0) -> void:
	var count := mini(positions.size(), MAX_PROBES)
	if count != _probe_positions.size() or layout_id != _probe_layout_id:
		_probe_generation += 1
		_probe_layout_id = layout_id
		_sampled_positions = PackedVector2Array()
		_probe_heights = PackedFloat32Array()
		_probe_heights.resize(count)
		_sampled_time = -1.0
		_sampled_serial = -1
	_probe_positions = positions.slice(0, count)


## Residual heights only, in the same stable slot order; zero until sampled.
func get_probe_heights() -> PackedFloat32Array:
	return _probe_heights.duplicate()


func get_sampled_probe_positions() -> PackedVector2Array:
	return _sampled_positions.duplicate()


## Simulation time represented by the returned samples, or -1 before sampling.
func get_probe_sample_time() -> float:
	return _sampled_time


func get_simulation_time() -> float:
	return _simulation_time


func step(delta: float, flow: Vector2 = Vector2.ZERO) -> void:
	if not _active or not is_finite(delta) or delta <= 0.0:
		return
	_accumulator += delta
	if not _ready:
		return
	var count := mini(floori((_accumulator + 0.000000001) / FIXED_STEP), MAX_SUBSTEPS)
	if count == 0:
		return
	# Eight steps cover 15 fps. Remainders and hitch backlog are retained rather
	# than changing the numerical timestep or dropping queued contact events.
	_accumulator = maxf(_accumulator - count * FIXED_STEP, 0.0)
	_simulation_time += count * FIXED_STEP
	_probe_elapsed += count * FIXED_STEP
	var impulse_count := mini(_impulses.size(), MAX_IMPULSES)
	var wake_count := mini(_wake_starts.size(), MAX_WAKES)
	var inputs := PackedFloat32Array()
	inputs.resize((MAX_IMPULSES + MAX_WAKES * 2) * 4)
	for i in impulse_count:
		_write_vector(inputs, i * 4, _impulses.pop_front())
	for i in wake_count:
		_write_vector(inputs, (MAX_IMPULSES + i) * 4, _wake_starts.pop_front())
		_write_vector(inputs, (MAX_IMPULSES + MAX_WAKES + i) * 4, _wake_ends.pop_front())
	var probes := PackedVector2Array()
	if not _probe_positions.is_empty() and _probe_elapsed >= PROBE_INTERVAL:
		_probe_elapsed = fmod(_probe_elapsed, PROBE_INTERVAL)
		probes = _probe_positions.duplicate()
		_probe_serial += 1
	var safe_flow := flow.limit_length(0.35) if flow.is_finite() else Vector2.ZERO
	var generation := _probe_generation
	var serial := _probe_serial
	var sample_time := _simulation_time
	var state := _gpu
	var bytes := inputs.to_byte_array()
	RenderingServer.call_on_render_thread(func() -> void:
		state.advance(count, bytes, impulse_count, wake_count, safe_flow, probes, generation, serial, sample_time))


func shutdown() -> void:
	var was_ready := _ready
	_active = false
	_ready = false
	_session += 1
	_texture = _neutral
	_accumulator = 0.0
	_simulation_time = 0.0
	_probe_elapsed = 0.0
	_impulses.clear()
	_wake_starts.clear()
	_wake_ends.clear()
	_probe_generation += 1
	_probe_heights.fill(0.0)
	_sampled_positions = PackedVector2Array()
	_sampled_time = -1.0
	_sampled_serial = -1
	_release_gpu()
	if was_ready:
		ready_changed.emit(false)


func _notification(what: int) -> void:
	if what == NOTIFICATION_PREDELETE and _gpu != null:
		# During RefCounted predelete, calling another method through self may
		# already see a null instance. Retain the independent state directly.
		var state := _gpu
		_gpu = null
		RenderingServer.call_on_render_thread(func() -> void: state.dispose())


func _release_gpu() -> void:
	if _gpu == null:
		return
	# The closure retains only GPUState, not this possibly departing scene owner.
	var state := _gpu
	_gpu = null
	RenderingServer.call_on_render_thread(func() -> void: state.dispose())


func _on_ready(session: int, available: bool) -> void:
	if session != _session or not _active:
		return
	_ready = available
	if available:
		_texture = _gpu.display
	else:
		_active = false
		push_warning("PondSimulation: GPU setup unavailable; using neutral water.")
	ready_changed.emit(available)


func _on_probes(session: int, generation: int, serial: int, sample_time: float, positions: PackedVector2Array, bytes: PackedByteArray) -> void:
	if not _active or session != _session or generation != _probe_generation or serial <= _sampled_serial:
		return
	if bytes.size() != positions.size() * 4:
		return
	_probe_heights = bytes.to_float32_array()
	_sampled_positions = positions
	_sampled_serial = serial
	_sampled_time = sample_time


static func _write_vector(data: PackedFloat32Array, offset: int, value: Vector4) -> void:
	data[offset] = value.x
	data[offset + 1] = value.y
	data[offset + 2] = value.z
	data[offset + 3] = value.w


## All mutable members below belong to the render thread. Callbacks use a weak
## scene owner; queued jobs hold this state alive through setup and disposal.
class GPUState extends RefCounted:
	var owner: WeakRef
	var session: int
	var bounds: Rect2
	var resolution: int
	var display := Texture2DRD.new()
	var rd: RenderingDevice
	var resources: Array[RID] = []
	var textures: Array[RID] = []
	var sets: Array[RID] = []
	var probe_sets: Array[RID] = []
	var pipeline := RID()
	var probe_pipeline := RID()
	var interactions := RID()
	var probe_input := RID()
	var probe_output := RID()
	var current := 0
	var disposed := false
	var wave_speed := WAVE_SPEED
	var cell := Vector2.ONE

	func initialize(initial: PackedByteArray, shader_spirv: RDShaderSPIRV, probe_spirv: RDShaderSPIRV) -> void:
		rd = RenderingServer.get_rendering_device()
		if rd == null:
			_notify_ready(false)
			return
		cell = bounds.size / float(resolution)
		# Conservative CFL bound for a rectangular five-point wave stencil.
		wave_speed = minf(WAVE_SPEED, 0.65 / (FIXED_STEP * sqrt(1.0 / (cell.x * cell.x) + 1.0 / (cell.y * cell.y))))
		var shader := _own(rd.shader_create_from_spirv(shader_spirv))
		var probe_shader := _own(rd.shader_create_from_spirv(probe_spirv))
		if not shader.is_valid() or not probe_shader.is_valid():
			_fail()
			return
		pipeline = _own(rd.compute_pipeline_create(shader))
		probe_pipeline = _own(rd.compute_pipeline_create(probe_shader))
		var format := RDTextureFormat.new()
		format.format = RenderingDevice.DATA_FORMAT_R32G32B32A32_SFLOAT
		format.texture_type = RenderingDevice.TEXTURE_TYPE_2D
		format.width = resolution
		format.height = resolution
		format.depth = 1
		format.array_layers = 1
		format.mipmaps = 1
		format.usage_bits = RenderingDevice.TEXTURE_USAGE_STORAGE_BIT | RenderingDevice.TEXTURE_USAGE_SAMPLING_BIT | RenderingDevice.TEXTURE_USAGE_CAN_COPY_FROM_BIT | RenderingDevice.TEXTURE_USAGE_CAN_COPY_TO_BIT
		if not rd.texture_is_format_supported_for_usage(format.format, format.usage_bits):
			_fail()
			return
		for i in 3:
			textures.append(_own(rd.texture_create(format, RDTextureView.new(), [initial])))
		interactions = _own(rd.storage_buffer_create((MAX_IMPULSES + MAX_WAKES * 2) * 16))
		probe_input = _own(rd.storage_buffer_create(MAX_PROBES * 8))
		probe_output = _own(rd.storage_buffer_create(MAX_PROBES * 4))
		if not pipeline.is_valid() or not probe_pipeline.is_valid() or not interactions.is_valid() or not probe_input.is_valid() or not probe_output.is_valid() or textures.any(func(value: RID) -> bool: return not value.is_valid()):
			_fail()
			return
		for i in 2:
			sets.append(_own(rd.uniform_set_create([
				_uniform(0, RenderingDevice.UNIFORM_TYPE_IMAGE, textures[i]),
				_uniform(1, RenderingDevice.UNIFORM_TYPE_IMAGE, textures[1 - i]),
				_uniform(2, RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER, interactions)], shader, 0)))
			probe_sets.append(_own(rd.uniform_set_create([
				_uniform(0, RenderingDevice.UNIFORM_TYPE_IMAGE, textures[i]),
				_uniform(1, RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER, probe_input),
				_uniform(2, RenderingDevice.UNIFORM_TYPE_STORAGE_BUFFER, probe_output)], probe_shader, 0)))
		if sets.any(func(value: RID) -> bool: return not value.is_valid()) or probe_sets.any(func(value: RID) -> bool: return not value.is_valid()):
			_fail()
			return
		display.texture_rd_rid = textures[2]
		_notify_ready(true)

	func advance(steps: int, inputs: PackedByteArray, impulse_count: int, wake_count: int, flow: Vector2, probes: PackedVector2Array, generation: int, serial: int, sample_time: float) -> void:
		if disposed or not pipeline.is_valid():
			return
		rd.buffer_update(interactions, 0, inputs.size(), inputs)
		var probe_bytes := PackedFloat32Array()
		for point in probes:
			probe_bytes.append(point.x)
			probe_bytes.append(point.y)
		if not probe_bytes.is_empty():
			rd.buffer_update(probe_input, 0, probe_bytes.size() * 4, probe_bytes.to_byte_array())
		var push := PackedFloat32Array([
			bounds.position.x, bounds.position.y, bounds.size.x, bounds.size.y,
			FIXED_STEP, wave_speed * wave_speed, VELOCITY_DAMPING, HEIGHT_DAMPING,
			FOAM_DECAY, MAX_HEIGHT, MAX_VELOCITY, 0.0,
			flow.x, flow.y, cell.x, cell.y,
			float(resolution), float(impulse_count), float(wake_count), 0.0])
		var groups := ceili(float(resolution) / 8.0)
		var compute := rd.compute_list_begin()
		rd.compute_list_bind_compute_pipeline(compute, pipeline)
		for substep in steps:
			# A point impulse is a velocity kick, not acceleration: apply it once.
			push[17] = float(impulse_count) if substep == 0 else 0.0
			push[18] = float(wake_count) if substep == 0 else 0.0
			rd.compute_list_bind_uniform_set(compute, sets[current], 0)
			rd.compute_list_set_push_constant(compute, push.to_byte_array(), 80)
			rd.compute_list_dispatch(compute, groups, groups, 1)
			rd.compute_list_add_barrier(compute)
			current = 1 - current
		if not probes.is_empty():
			var probe_push := PackedFloat32Array([bounds.position.x, bounds.position.y, bounds.size.x, bounds.size.y, float(resolution), float(probes.size()), 0.0, 0.0])
			rd.compute_list_bind_compute_pipeline(compute, probe_pipeline)
			rd.compute_list_bind_uniform_set(compute, probe_sets[current], 0)
			rd.compute_list_set_push_constant(compute, probe_push.to_byte_array(), 32)
			rd.compute_list_dispatch(compute, 1, 1, 1)
		rd.compute_list_end()
		# One stable display image avoids Texture2DRD/RID rebinding every frame.
		rd.texture_copy(textures[current], textures[2], Vector3.ZERO, Vector3.ZERO, Vector3(resolution, resolution, 1), 0, 0, 0, 0)
		if not probes.is_empty():
			var weak_owner := owner
			var request_session := session
			rd.buffer_get_data_async(probe_output, func(bytes: PackedByteArray) -> void:
				var target = weak_owner.get_ref()
				if target != null:
					target.call_deferred("_on_probes", request_session, generation, serial, sample_time, probes, bytes), 0, probes.size() * 4)
		# The main RenderingDevice submits normally. Never submit()/sync() it here.

	func dispose() -> void:
		if disposed:
			return
		disposed = true
		display.texture_rd_rid = RID()
		if rd != null:
			# Reverse creation order releases sets before buffers/textures/shaders.
			for i in range(resources.size() - 1, -1, -1):
				rd.free_rid(resources[i])
		resources.clear()
		textures.clear()
		sets.clear()
		probe_sets.clear()
		pipeline = RID()
		probe_pipeline = RID()
		rd = null

	func _own(rid: RID) -> RID:
		if rid.is_valid():
			resources.append(rid)
		return rid

	func _uniform(binding: int, type: int, rid: RID) -> RDUniform:
		var uniform := RDUniform.new()
		uniform.binding = binding
		uniform.uniform_type = type
		uniform.add_id(rid)
		return uniform

	func _notify_ready(available: bool) -> void:
		var target = owner.get_ref()
		if target != null:
			target.call_deferred("_on_ready", session, available)

	func _fail() -> void:
		dispose()
		_notify_ready(false)
