class_name Pond
extends Node3D

## The pond: tessellated surface mesh, water material and a planar reflection
## rendered by a mirrored camera into a SubViewport. The mirror camera uses a
## lightweight Environment and an extra cull layer that lets shaders clip
## geometry below the water plane (see common.gdshaderinc).

const WATER_LAYER := 1 << 4
const REFLECTION_LAYER := 1 << 19
const UNDERWATER_REFLECTION_LAYER := 1 << 18
const AIR_FOG_LAYER := 1 << 17
const GRASS_LAYER := 1 << 3
## ~31 cm cells keep the displaced triangles close to the analytic waves
## used by lilies and the lens waterline; metre-wide cells left visible gaps.
const GRID_CELLS := 192
const SURFACE_MARGIN := 7.0

var field: TerrainField
var surface: MeshInstance3D
var material: ShaderMaterial
var underside_material: ShaderMaterial
var reflection_viewport: SubViewport
var reflection_camera: Camera3D
var reflection_environment: Environment
var underwater_viewport: SubViewport
var underwater_camera: Camera3D
var underwater_filter_material: ShaderMaterial
var underwater_environment: Environment
var transmission_viewport: SubViewport
var transmission_camera: Camera3D
var transmission_environment: Environment
var planar_enabled := true
var _active := false
var _planar_shader: Shader
var _fallback_shader: Shader
var _underside_planar_shader: Shader
var _underside_fallback_shader: Shader
var _scale := 0.5
var _half_rate := false
var _frame_parity := 0
var _reflection_visible := false
var _wading := 0.0
const RIPPLE_COUNT := 8
var _ripple_clock := 0.0
var _ripple_cursor := 0
var _ripples := PackedVector4Array()
var _skip_age := -1.0
var _skip_origin := Vector3.ZERO
var _skip_direction := Vector3.LEFT
var _skip_index := 0
var _stone: MeshInstance3D
var simulation: PondSimulation
var surface_time := 0.0
var simulation_paused := false
var _simulation_bound := false
var _canoe: Canoe
var _interaction_clock := 0.0
var _previous_bow := Vector2.ZERO
var _previous_wader := Vector2.INF
var _probe_positions := PackedVector2Array()
var _probe_heights := PackedFloat32Array()
var _rain_rng := RandomNumberGenerator.new()
var _rain_accumulator := 0.0
const INTERACTION_RESOLUTION := 256


func _init(p_field: TerrainField) -> void:
	field = p_field
	name = "Pond"
	_rain_rng.seed = 73191


func build() -> void:
	_build_shaders()
	_build_surface()
	_build_reflection()
	_ripples.resize(RIPPLE_COUNT)
	for i in RIPPLE_COUNT:
		_ripples[i] = Vector4(0, 0, -100, 0)
	material.set_shader_parameter("ripples", _ripples)
	_stone = MeshInstance3D.new()
	_stone.mesh = PropMeshes.rock(813, 0.035)
	_stone.material_override = PropMaterials.iron(Color(0.20, 0.23, 0.22))
	_stone.visible = false
	add_child(_stone)
	Quality.preset_changed.connect(apply_quality)
	apply_quality(Quality.current)


## Two shader variants from one source: with planar reflections the sky's own
## specular contribution is disabled so reflections are not counted twice.
func _build_shaders() -> void:
	var base: Shader = load("res://shaders/water.gdshader")
	var source := base.code
	_planar_shader = Shader.new()
	_planar_shader.code = source.replace("//PLANAR_RENDER_MODE", "render_mode ambient_light_disabled;")
	_fallback_shader = Shader.new()
	_fallback_shader.code = source.replace("//PLANAR_RENDER_MODE", "")
	_underside_planar_shader = Shader.new()
	_underside_planar_shader.code = "#define BELOW_SURFACE\n" + _planar_shader.code
	_underside_fallback_shader = Shader.new()
	_underside_fallback_shader.code = "#define BELOW_SURFACE\n" + _fallback_shader.code


func _build_surface() -> void:
	material = ShaderMaterial.new()
	material.shader = _planar_shader
	Camp.bind_texture(material, "normal_a", "res://textures/water_normal_a.png")
	Camp.bind_texture(material, "normal_b", "res://textures/water_normal_b.png")
	Camp.bind_texture(material, "foam_tex", "res://textures/foam.png")

	var mb := MeshBuilder.new()
	mb.use_tangents = false
	var radius := TerrainField.POND_MAX_RADIUS + SURFACE_MARGIN
	var n := GRID_CELLS + 1
	var centre := TerrainField.POND_CENTRE
	for iz in n:
		for ix in n:
			var u := float(ix) / float(GRID_CELLS) * 2.0 - 1.0
			var v := float(iz) / float(GRID_CELLS) * 2.0 - 1.0
			# Square grid mapped to a disc so triangles stay even near the shore.
			var p := _square_to_disc(Vector2(u, v)) * radius
			mb.add_vertex(Vector3(centre.x + p.x, TerrainField.WATER_LEVEL, centre.y + p.y), Vector3.UP, Vector2(u, v) * 0.5 + Vector2(0.5, 0.5))
	for iz in GRID_CELLS:
		for ix in GRID_CELLS:
			var a := iz * n + ix
			var b := a + 1
			var c := a + n
			var d := c + 1
			mb.add_triangle(a, b, d)
			mb.add_triangle(a, d, c)
	surface = MeshInstance3D.new()
	surface.name = "Surface"
	surface.mesh = mb.commit(material)
	surface.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	surface.layers = WATER_LAYER
	surface.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	surface.custom_aabb = AABB(Vector3(centre.x - radius, TerrainField.WATER_LEVEL - 1.0, centre.y - radius),
			Vector3(radius * 2.0, 2.0, radius * 2.0))
	add_child(surface)
	underside_material = material.duplicate()
	underside_material.shader = _underside_planar_shader
	var underside := MeshInstance3D.new()
	underside.name = "Underside"
	underside.mesh = surface.mesh
	underside.material_override = underside_material
	underside.layers = WATER_LAYER
	underside.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	underside.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	underside.custom_aabb = surface.custom_aabb
	add_child(underside)


static func _square_to_disc(p: Vector2) -> Vector2:
	var x := p.x
	var y := p.y
	if x == 0.0 and y == 0.0:
		return Vector2.ZERO
	var r: float
	var phi: float
	if absf(x) > absf(y):
		r = x
		phi = (PI / 4.0) * (y / x)
	else:
		r = y
		phi = (PI / 2.0) - (PI / 4.0) * (x / y)
	return Vector2(r * cos(phi), r * sin(phi))


func _build_reflection() -> void:
	reflection_viewport = SubViewport.new()
	reflection_viewport.name = "ReflectionViewport"
	reflection_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	reflection_viewport.use_taa = false
	reflection_viewport.screen_space_aa = Viewport.SCREEN_SPACE_AA_FXAA
	reflection_viewport.msaa_3d = Viewport.MSAA_DISABLED
	reflection_viewport.use_debanding = false
	# HDR output keeps the mirror image linear so the water shader can
	# composite it before the main tonemapper.
	reflection_viewport.use_hdr_2d = true
	reflection_viewport.positional_shadow_atlas_size = 0
	reflection_viewport.mesh_lod_threshold = 4.0
	reflection_viewport.audio_listener_enable_3d = false
	reflection_viewport.size = Vector2i(960, 540)
	add_child(reflection_viewport)

	reflection_environment = Environment.new()
	reflection_environment.background_mode = Environment.BG_SKY
	reflection_environment.ambient_light_source = Environment.AMBIENT_SOURCE_SKY
	reflection_environment.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	reflection_environment.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	reflection_environment.fog_enabled = true
	reflection_environment.fog_mode = Environment.FOG_MODE_EXPONENTIAL
	reflection_environment.glow_enabled = false
	reflection_environment.ssao_enabled = false
	reflection_environment.ssil_enabled = false
	reflection_environment.sdfgi_enabled = false
	reflection_environment.ssr_enabled = false
	reflection_environment.volumetric_fog_enabled = false

	reflection_camera = Camera3D.new()
	reflection_camera.name = "MirrorCamera"
	reflection_camera.cull_mask = (0xFFFFF & ~WATER_LAYER & ~GRASS_LAYER & ~UNDERWATER_REFLECTION_LAYER) | REFLECTION_LAYER
	reflection_camera.environment = reflection_environment
	reflection_camera.near = 0.1
	reflection_camera.far = 700.0
	reflection_viewport.add_child(reflection_camera)
	material.set_shader_parameter("reflection_tex", reflection_viewport.get_texture())
	material.set_shader_parameter("debug_reflection", Game.has_flag("debug-reflection"))
	material.set_shader_parameter("highlight_debug", int(Game.arg_value("water-debug", "0")))
	# A separate mirror keeps the submerged halfspace. Both mirrors remain
	# available while the lens straddles the surface, avoiding a texture swap
	# precisely when the waterline passes across the image.
	underwater_viewport = SubViewport.new()
	underwater_viewport.name = "UnderwaterReflectionViewport"
	underwater_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	underwater_viewport.screen_space_aa = Viewport.SCREEN_SPACE_AA_FXAA
	underwater_viewport.use_hdr_2d = true
	underwater_viewport.positional_shadow_atlas_size = 0
	underwater_viewport.audio_listener_enable_3d = false
	underwater_viewport.mesh_lod_threshold = 4.0
	underwater_viewport.size = reflection_viewport.size
	add_child(underwater_viewport)
	underwater_environment = reflection_environment.duplicate()
	underwater_environment.background_mode = Environment.BG_COLOR
	underwater_environment.background_color = Color(0.05, 0.17, 0.15)
	underwater_environment.fog_enabled = false
	underwater_environment.volumetric_fog_density = 0.0
	underwater_environment.volumetric_fog_albedo = WorldController.WATER_VOLUME_ALBEDO
	underwater_environment.volumetric_fog_anisotropy = 0.75
	underwater_environment.volumetric_fog_length = 90.0
	underwater_environment.volumetric_fog_detail_spread = 2.2
	underwater_environment.volumetric_fog_ambient_inject = 0.18
	underwater_environment.volumetric_fog_temporal_reprojection_enabled = false
	# The reflected leg needs the same lit water as the direct leg. Extinction
	# alone made the reflection dark and left a visible seam at the horizon.
	var reflected_volume := FogVolume.new()
	reflected_volume.name = "ReflectedWaterVolume"
	reflected_volume.layers = UNDERWATER_REFLECTION_LAYER
	reflected_volume.shape = RenderingServer.FOG_VOLUME_SHAPE_BOX
	reflected_volume.size = Vector3(60.0, 6.0, 60.0)
	# The mirror uses the mean plane while the visible surface moves within
	# the wave envelope. Include that envelope so near-horizontal reflected
	# rays do not fall through a centimetre-wide unlit gap at the waterline.
	reflected_volume.position = Vector3(TerrainField.POND_CENTRE.x,
		TerrainField.WATER_LEVEL - 3.0 + WorldController.WATER_WAVE_ENVELOPE, TerrainField.POND_CENTRE.y)
	var volume_material := ShaderMaterial.new()
	volume_material.shader = load("res://shaders/pond_fog.gdshader")
	volume_material.set_shader_parameter("density", WorldController.WATER_VOLUME_DENSITY)
	volume_material.set_shader_parameter("water_albedo", WorldController.WATER_VOLUME_ALBEDO)
	reflected_volume.material = volume_material
	add_child(reflected_volume)
	underwater_camera = Camera3D.new()
	underwater_camera.name = "UnderwaterMirrorCamera"
	underwater_camera.cull_mask = (reflection_camera.cull_mask | UNDERWATER_REFLECTION_LAYER) & ~AIR_FOG_LAYER
	underwater_camera.environment = underwater_environment
	underwater_camera.near = 0.05
	underwater_camera.far = 90.0
	underwater_viewport.add_child(underwater_camera)
	var mirror_fog := MeshInstance3D.new()
	mirror_fog.name = "ReflectedWaterPath"
	mirror_fog.layers = UNDERWATER_REFLECTION_LAYER
	var fog_quad := QuadMesh.new()
	fog_quad.size = Vector2(2, 2)
	mirror_fog.mesh = fog_quad
	mirror_fog.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	mirror_fog.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	mirror_fog.custom_aabb = AABB(Vector3.ONE * -4000.0, Vector3.ONE * 8000.0)
	var fog_material := ShaderMaterial.new()
	fog_material.shader = load("res://shaders/post/water_mirror_fog.gdshader")
	fog_material.set_shader_parameter("density", WorldController.WATER_FOG_DENSITY)
	fog_material.set_shader_parameter("fog_color", WorldController.UNDERWATER_FOG)
	fog_material.render_priority = 100
	mirror_fog.material_override = fog_material
	add_child(mirror_fog)
	var mirror_filter := ColorRect.new()
	mirror_filter.name = "RoughWaterFilter"
	underwater_viewport.add_child(mirror_filter)
	mirror_filter.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mirror_filter.mouse_filter = Control.MOUSE_FILTER_IGNORE
	underwater_filter_material = ShaderMaterial.new()
	underwater_filter_material.shader = load("res://shaders/post/water_mirror_filter.gdshader")
	mirror_filter.material = underwater_filter_material
	material.set_shader_parameter("underwater_reflection_tex", underwater_viewport.get_texture())
	transmission_viewport = SubViewport.new()
	transmission_viewport.name = "WaterTransmissionViewport"
	transmission_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	transmission_viewport.screen_space_aa = Viewport.SCREEN_SPACE_AA_FXAA
	transmission_viewport.use_hdr_2d = true
	transmission_viewport.positional_shadow_atlas_size = 0
	transmission_viewport.audio_listener_enable_3d = false
	transmission_viewport.mesh_lod_threshold = 4.0
	transmission_viewport.size = reflection_viewport.size
	add_child(transmission_viewport)
	transmission_environment = reflection_environment.duplicate()
	transmission_camera = Camera3D.new()
	transmission_camera.name = "AirTransmissionCamera"
	transmission_camera.environment = transmission_environment
	transmission_camera.cull_mask = reflection_camera.cull_mask
	transmission_camera.near = 0.05
	transmission_camera.far = 700.0
	transmission_viewport.add_child(transmission_camera)
	if underside_material != null:
		underside_material.set_shader_parameter("underwater_reflection_tex", underwater_viewport.get_texture())
		underside_material.set_shader_parameter("transmission_tex", transmission_viewport.get_texture())


## Starts rendering the planar reflection (deferred until the renderer is warm).
func activate() -> void:
	_active = true
	_start_simulation()
	_apply_update_mode(false)


func _start_simulation() -> void:
	if simulation != null or Game.camp == null or Game.camp.campsite == null:
		return
	process_physics_priority = -20
	var dock: Dock = Game.camp.campsite.dock
	_canoe = dock.canoe
	_canoe.start_floating(self)
	var bow := _canoe.bow_point()
	_previous_bow = Vector2(bow.x, bow.z)
	if Game.has_flag("no-water-simulation"):
		return
	var radius := TerrainField.POND_MAX_RADIUS + 1.0
	var bounds := Rect2(TerrainField.POND_CENTRE - Vector2.ONE * radius, Vector2.ONE * radius * 2.0)
	var mask := interaction_mask(field, Game.camp.plan, dock, bounds, INTERACTION_RESOLUTION)
	simulation = PondSimulation.new()
	simulation.setup(bounds, mask, INTERACTION_RESOLUTION)
	RenderingServer.global_shader_parameter_set("pond_interaction_bounds", Vector4(bounds.position.x, bounds.position.y, bounds.size.x, bounds.size.y))


static func interaction_mask(terrain: TerrainField, plan: ScenePlan, dock: Dock, bounds: Rect2, resolution: int) -> Image:
	var mask := Image.create(resolution, resolution, false, Image.FORMAT_RF)
	var cell := bounds.size / float(resolution)
	for y in resolution:
		for x in resolution:
			var p := bounds.position + (Vector2(x, y) + Vector2.ONE * 0.5) * cell
			mask.set_pixel(x, y, Color(1.0 if terrain.water_depth(p.x, p.y) < 0.025 else 0.0, 0, 0))
	if plan != null:
		for rock in plan.rocks:
			var radius := rock.scale * CampRocks.COLLIDE_SCALE
			var centre_y := terrain.height(rock.position.x, rock.position.y) - rock.sink * rock.scale * 0.35 + rock.scale * 0.2
			var dy := TerrainField.WATER_LEVEL - centre_y
			if absf(dy) < radius:
				_mask_disc(mask, bounds, rock.position, sqrt(radius * radius - dy * dy))
	if dock != null:
		for local in dock._piles:
			var p := dock.to_global(local)
			# Keep a sub-cell pile represented without opening pinholes in the mask.
			_mask_disc(mask, bounds, Vector2(p.x, p.z), maxf(Dock.PILE_RADIUS, cell.x * 0.75))
	return mask


static func _mask_disc(mask: Image, bounds: Rect2, centre: Vector2, radius: float) -> void:
	var size := mask.get_width()
	var cell := bounds.size / float(size)
	var low := ((centre - Vector2.ONE * radius - bounds.position) / cell).floor()
	var high := ((centre + Vector2.ONE * radius - bounds.position) / cell).ceil()
	for y in range(maxi(0, int(low.y)), mini(size, int(high.y) + 1)):
		for x in range(maxi(0, int(low.x)), mini(size, int(high.x) + 1)):
			var at := bounds.position + (Vector2(x, y) + Vector2.ONE * 0.5) * cell
			if at.distance_squared_to(centre) <= radius * radius:
				mask.set_pixel(x, y, Color(1, 0, 0))


func base_height(at: Vector2) -> float:
	var strength: float = Game.world.wind_strength() if Game.world != null else 0.4
	return PondSurface.height_at(at, surface_time, WorldController.WIND_DIRECTION, strength)


func surface_height(at: Vector2) -> float:
	var height := base_height(at)
	# Sparse async samples are for bodies/lens only. A camera cut must never
	# reuse a residual from its former location; distant queries use base waves.
	for i in mini(_probe_positions.size(), _probe_heights.size()):
		if at.distance_squared_to(_probe_positions[i]) < 0.04:
			return height + _probe_heights[i]
	return height


func _physics_process(delta: float) -> void:
	if not _active:
		return
	surface_time += delta
	RenderingServer.global_shader_parameter_set("pond_time", surface_time)
	if simulation == null or simulation_paused:
		return
	if not _simulation_bound and simulation.is_ready():
		RenderingServer.global_shader_parameter_set("pond_interaction_tex", simulation.get_texture())
		RenderingServer.global_shader_parameter_set("pond_interaction_enabled", true)
		_simulation_bound = true
	var probes := _canoe.probe_positions()
	var camera := get_viewport().get_camera_3d()
	var lens := camera.global_position if camera != null else Vector3.ZERO
	probes.append(Vector2(lens.x, lens.z))
	simulation.set_probe_positions(probes)
	_probe_heights = simulation.get_probe_heights()
	_probe_positions = simulation.get_sampled_probe_positions()
	if _probe_heights.size() >= Canoe.FLOAT_POINTS.size():
		_canoe.set_residual_heights(_probe_heights.slice(0, Canoe.FLOAT_POINTS.size()))
	_interaction_clock += delta
	if _interaction_clock >= 1.0 / 12.0:
		var elapsed := _interaction_clock
		_interaction_clock = 0.0
		var bow := _canoe.bow_point()
		var at := Vector2(bow.x, bow.z)
		var travel := at.distance_to(_previous_bow)
		if travel > 0.002 and travel < 0.5:
			simulation.queue_wake(_previous_bow, at, 0.32, minf(travel / elapsed * 0.12, 0.10))
		_previous_bow = at
		if _wading > 0.01 and Game.player != null:
			var p := Game.player.global_position
			var now := Vector2(p.x, p.z)
			if _previous_wader.is_finite() and _previous_wader.distance_to(now) < 1.0:
				simulation.queue_wake(_previous_wader, now, 0.25, 0.14 * _wading)
			_previous_wader = now
		else:
			_previous_wader = Vector2.INF
	# The large raindrop contacts join the persistent field. Fine rain texture
	# still supplies sub-cell detail; no expensive all-drop CPU simulation.
	var rain: float = Game.world.weather.rain if Game.world != null and Game.world.weather != null else 0.0
	_rain_accumulator += rain * delta * 24.0
	for drop in mini(floori(_rain_accumulator), 12):
		_rain_accumulator -= 1.0
		var at := Vector2(lens.x, lens.z) + Vector2(_rain_rng.randf_range(-9.0, 9.0), _rain_rng.randf_range(-9.0, 9.0))
		if field.water_depth(at.x, at.y) > 0.03:
			simulation.queue_impulse(at, 0.16, -0.035)
	simulation.step(delta, WorldController.WIND_DIRECTION.normalized() * 0.025)


func _exit_tree() -> void:
	RenderingServer.global_shader_parameter_set("pond_interaction_enabled", false)
	if simulation != null:
		simulation.shutdown()


func _process(delta: float) -> void:
	_ripple_clock += delta
	if material == null:
		return
	material.set_shader_parameter("ripple_clock", _ripple_clock)
	underside_material.set_shader_parameter("ripple_clock", _ripple_clock)
	_update_skip(delta)
	_sync_environment()
	var camera := get_viewport().get_camera_3d()
	if camera == null:
		return
	# A conservative box test includes the bank and displaced water. Only
	# suspend the mirror when its entire surface is outside the frustum.
	var intersects := bounds_in_frustum(surface.global_transform * surface.custom_aabb, camera.get_frustum())
	var lens_height := camera.global_position.y - TerrainField.WATER_LEVEL
	var below_visible := planar_enabled and _active and intersects and lens_height < 0.25
	underwater_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS if below_visible else SubViewport.UPDATE_DISABLED
	transmission_viewport.render_target_update_mode = underwater_viewport.render_target_update_mode
	if below_visible:
		_update_viewport_size()
		_mirror_camera(camera, underwater_camera)
		var projection := underwater_camera.get_camera_projection()
		underwater_filter_material.set_shader_parameter("ray_forward", -underwater_camera.global_basis.z)
		underwater_filter_material.set_shader_parameter("ray_right", underwater_camera.global_basis.x / projection.x.x)
		underwater_filter_material.set_shader_parameter("ray_up", underwater_camera.global_basis.y / projection.y.y)
		transmission_camera.global_transform = camera.global_transform
		transmission_camera.fov = camera.fov
		transmission_camera.keep_aspect = camera.keep_aspect
		transmission_camera.projection = camera.projection
	# Keep the upper view alive through the whole near-plane crossing.
	var visible_water := lens_height > -0.20 and intersects
	if not planar_enabled or not _active or not visible_water:
		_reflection_visible = false
		_apply_update_mode(true)
		return
	_update_viewport_size()
	_mirror_camera(camera, reflection_camera)
	if _half_rate and _reflection_visible:
		# Half-rate mirror: refresh every other frame (the ripples hide the lag).
		_frame_parity = (_frame_parity + 1) % 2
		reflection_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE if _frame_parity == 0 else SubViewport.UPDATE_DISABLED
	else:
		_apply_update_mode(false)
	_reflection_visible = true


static func bounds_in_frustum(bounds: AABB, planes: Array[Plane]) -> bool:
	var centre := bounds.get_center()
	var half := bounds.size * 0.5
	for plane in planes:
		if plane.distance_to(centre) > plane.normal.abs().dot(half):
			return false
	return true


## A finite impulse persists at the point of contact after the player leaves.
func ripple(at: Vector3, strength := 0.65) -> void:
	if simulation != null:
		simulation.queue_impulse(Vector2(at.x, at.z), 0.24, -strength * 0.40)
	_ripples[_ripple_cursor] = Vector4(at.x, at.z, _ripple_clock, strength)
	_ripple_cursor = (_ripple_cursor + 1) % RIPPLE_COUNT
	material.set_shader_parameter("ripples", _ripples)
	underside_material.set_shader_parameter("ripples", _ripples)


## Three diminishing ballistic hops, with an impact sound at each contact.
func skip_stone(origin: Vector3, direction: Vector3) -> bool:
	if _skip_age >= 0.0 or direction.length_squared() < 0.01:
		return false
	var flat := Vector3(direction.x, 0, direction.z).normalized()
	var target := origin + flat * 3.0
	if field.water_depth(target.x, target.z) < 0.25:
		return false
	_skip_origin = origin
	_skip_direction = flat
	_skip_age = 0.0
	_skip_index = 0
	_stone.global_position = origin
	_stone.visible = true
	return true


func _update_skip(delta: float) -> void:
	if _skip_age < 0.0:
		return
	_skip_age += delta
	var duration := 0.52 * pow(0.75, _skip_index)
	var distance := 3.0 * pow(0.72, _skip_index)
	var target := _skip_origin + _skip_direction * distance
	target.y = surface_height(Vector2(target.x, target.z)) + 0.025
	var t := clampf(_skip_age / duration, 0.0, 1.0)
	_stone.global_position = _skip_origin.lerp(target, t) + Vector3.UP * sin(t * PI) * 0.30 * pow(0.7, _skip_index)
	_stone.rotate_z(delta * 14.0)
	if t < 1.0:
		return
	if field.water_depth(target.x, target.z) > 0.05:
		ripple(target, 0.9 * pow(0.65, _skip_index))
		if Game.audio != null:
			Game.audio.water_impact(target, _skip_index)
	_skip_index += 1
	_skip_origin = target
	_skip_age = 0.0
	if _skip_index >= 3 or field.water_depth(target.x, target.z) < 0.1:
		_skip_age = -1.0
		_stone.visible = false


func _apply_update_mode(submerged: bool) -> void:
	var wanted := SubViewport.UPDATE_ALWAYS if (planar_enabled and _active and not submerged) else SubViewport.UPDATE_DISABLED
	if reflection_viewport.render_target_update_mode != wanted:
		reflection_viewport.render_target_update_mode = wanted


## Keeps the mirror's cheap environment in step with the real one (sky, fog).
func _sync_environment() -> void:
	if Game.world == null or Game.world.environment == null:
		return
	var env: Environment = Game.world.environment
	reflection_environment.sky = env.sky
	reflection_environment.fog_light_color = env.fog_light_color
	reflection_environment.fog_density = env.fog_density
	reflection_environment.fog_aerial_perspective = env.fog_aerial_perspective
	reflection_environment.fog_sky_affect = env.fog_sky_affect
	reflection_environment.ambient_light_energy = env.ambient_light_energy
	reflection_environment.tonemap_exposure = 1.0
	underwater_environment.sky = env.sky
	underwater_environment.ambient_light_energy = env.ambient_light_energy
	underwater_environment.volumetric_fog_enabled = env.volumetric_fog_enabled
	underwater_environment.volumetric_fog_ambient_inject = env.volumetric_fog_ambient_inject
	transmission_environment.sky = env.sky
	transmission_environment.fog_light_color = Game.world.fog_color()
	transmission_environment.fog_density = WorldController.AIR_FOG_DENSITY
	transmission_environment.fog_sky_affect = 0.12
	transmission_environment.ambient_light_energy = 1.0


func _update_viewport_size() -> void:
	var main_size := get_viewport().get_visible_rect().size
	var target := Vector2i((main_size * _scale).round())
	target = Vector2i(maxi(target.x, 160), maxi(target.y, 90))
	if reflection_viewport.size != target:
		reflection_viewport.size = target
		underwater_viewport.size = target
		transmission_viewport.size = target


## Reflects the main camera through the water plane. The up axis is negated to
## keep the basis right-handed, which flips the image vertically; the water
## shader samples it with 1 - v.
func _mirror_camera(camera: Camera3D, target: Camera3D) -> void:
	var plane_y := TerrainField.WATER_LEVEL
	var xf := camera.global_transform
	var pos := xf.origin
	pos.y = 2.0 * plane_y - pos.y
	var reflect := func(v: Vector3) -> Vector3: return Vector3(v.x, -v.y, v.z)
	var bx: Vector3 = reflect.call(xf.basis.x)
	var by: Vector3 = reflect.call(xf.basis.y)
	var bz: Vector3 = reflect.call(xf.basis.z)
	target.global_transform = Transform3D(Basis(bx, -by, bz), pos)
	target.fov = camera.fov
	target.keep_aspect = camera.keep_aspect
	target.projection = camera.projection


## Mirror resolution as a fraction of the screen (overrides the preset).
func set_reflection_scale(scale: float) -> void:
	_scale = scale
	_update_viewport_size()


func set_wading(strength: float) -> void:
	_wading = strength
	material.set_shader_parameter("wading_strength", strength)
	underside_material.set_shader_parameter("wading_strength", strength)


func apply_quality(p: QualityPreset) -> void:
	planar_enabled = p.planar_reflections
	_scale = p.reflection_scale
	_half_rate = p.reflection_half_rate
	_apply_update_mode(false)
	material.shader = _planar_shader if planar_enabled else _fallback_shader
	material.set_shader_parameter("use_planar", planar_enabled)
	material.set_shader_parameter("reflection_tex", reflection_viewport.get_texture())
	material.set_shader_parameter("underwater_reflection_tex", underwater_viewport.get_texture())
	underside_material.shader = _underside_planar_shader if planar_enabled else _underside_fallback_shader
	underside_material.set_shader_parameter("use_planar", planar_enabled)
	underside_material.set_shader_parameter("underwater_reflection_tex", underwater_viewport.get_texture())
	underside_material.set_shader_parameter("transmission_tex", transmission_viewport.get_texture())
