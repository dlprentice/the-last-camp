class_name Firepit
extends Node3D

## The camp's heart: a soot-blackened stone ring around a bed of glowing
## coals, charred logs leaning into eroding flame cards, ember streaks, lit
## smoke and heat shimmer, all under a lashed tripod with a hanging pot. A
## shadowed omni and a wide fill light carry the firelight into the clearing.
## Intensity decays slowly; feeding a log brings it back.

signal fed
signal intensity_changed(value: float)

const DECAY_PER_SECOND := 0.0018
const FEED_AMOUNT := 0.45
const MIN_INTENSITY := 0.0
const MAX_INTENSITY := 2.0
const LIGHT_COLOR := Color(1.0, 0.53, 0.2)
const BASE_ENERGY := 1.5
const BASE_RANGE := 12.0
const FILL_ENERGY := 0.20
const FILL_RANGE := 22.0
const RING_RADIUS := 0.9
const TRIPOD_FOOT_RADIUS := 1.48
const BED_RADIUS := 0.5
const FLAME_HEIGHT := 0.12

var intensity := 1.0
var field: TerrainField
var light: OmniLight3D
var fill_light: OmniLight3D
var flame_volume: MeshInstance3D
var sparks: GPUParticles3D
var smoke: GPUParticles3D
var haze: MeshInstance3D
var fog: FogVolume
var body: Interactable

var _time := 0.0
var _smoke_process: ParticleProcessMaterial
var _flame_mat: ShaderMaterial
var _flame_noise: NoiseTexture3D
var _noise_reported := false
var _base_sparks := 44
var _base_smoke := 36


func _init(p_field: TerrainField) -> void:
	field = p_field
	name = "Firepit"


func build() -> void:
	var y := field.height(TerrainField.FIRE.x, TerrainField.FIRE.y)
	position = Vector3(TerrainField.FIRE.x, y, TerrainField.FIRE.y)
	_build_ring()
	_build_bed()
	_build_logs()
	_build_tripod()
	_build_particles()
	_build_haze()
	_build_lights()
	_build_fog()
	_build_collision()
	Quality.preset_changed.connect(apply_quality)
	apply_quality(Quality.current)
	_apply_intensity()


func prompt() -> String:
	if Game.player != null and Game.player.held_item == &"log":
		return "Add a log"
	if intensity < 0.2:
		return "The fire is dying"
	return "Warm your hands"


func interact(player: Node) -> void:
	if player != null and player.get("held_item") == &"log":
		player.held_item = &""
		feed()
		if Game.audio != null:
			Game.audio.play_interact(&"log_added")
		return
	if Game.audio != null:
		Game.audio.play_interact(&"ui")


func feed() -> void:
	intensity = minf(intensity + FEED_AMOUNT, MAX_INTENSITY)
	intensity_changed.emit(intensity)
	fed.emit()
	_apply_intensity()


func _process(delta: float) -> void:
	_time += delta
	if _flame_noise != null and not _noise_reported and _time > 3.0:
		_noise_reported = true
		var layers: Array = _flame_noise.get_data()
		if layers.is_empty():
			print("FLAME_NOISE no data yet")
		else:
			var mid: Image = layers[layers.size() / 2]
			print("FLAME_NOISE layers=%d size=%s format=%d samples=%s %s %s intensity=%.2f height=%s density=%s" % [layers.size(), mid.get_size(), mid.get_format(),
					mid.get_pixel(8, 8), mid.get_pixel(32, 32), mid.get_pixel(50, 20), intensity,
					_flame_mat.get_shader_parameter("height"), _flame_mat.get_shader_parameter("density")])
	if Game.has_flag("fire-debug") and _flame_mat != null:
		_flame_mat.set_shader_parameter("debug_mode", int(Game.arg_value("fire-debug", "1")))
	if intensity > MIN_INTENSITY:
		intensity = maxf(intensity - DECAY_PER_SECOND * delta, MIN_INTENSITY)
	_apply_intensity()
	if light == null:
		return
	var flicker := 1.0
	flicker += 0.09 * sin(_time * 4.7)
	flicker += 0.06 * sin(_time * 9.1 + 1.3)
	flicker += 0.035 * sin(_time * 17.0 + 0.6)
	var live := smoothstep(0.05, 0.35, intensity)
	var strength := clampf(intensity, 0.0, MAX_INTENSITY) * (0.55 + 0.45 * live)
	light.light_energy = BASE_ENERGY * strength * flicker
	light.omni_range = BASE_RANGE * (0.7 + 0.3 * clampf(intensity, 0.0, 1.0))
	light.position = Vector3(
			0.04 * sin(_time * 3.2),
			0.5 + 0.03 * sin(_time * 5.1),
			0.03 * cos(_time * 2.7))
	fill_light.light_energy = FILL_ENERGY * strength * (0.9 + 0.1 * flicker)
	if body != null:
		body.prompt_text = prompt()
	if _smoke_process != null and Game.world != null:
		var wind: Vector2 = WorldController.WIND_DIRECTION
		var gust: float = Game.world.wind_strength()
		var drift: Vector2 = wind.normalized() * (0.12 + 0.25 * gust)
		_smoke_process.gravity = Vector3(drift.x, 0.3, drift.y)


func _apply_intensity() -> void:
	var live := smoothstep(0.04, 0.45, intensity)
	var roar := clampf(intensity, 0.0, MAX_INTENSITY)
	if flame_volume != null:
		flame_volume.visible = live > 0.02
		_flame_mat.set_shader_parameter("flare", roar)
		# Even a freshly fed fire keeps its upper envelope below the pot.
		_flame_mat.set_shader_parameter("height", flame_height(roar))
		_flame_mat.set_shader_parameter("density", 2.0 + 1.0 * live)
	if sparks != null:
		sparks.amount_ratio = clampf(0.2 + roar * 0.45, 0.0, 1.0)
		sparks.emitting = intensity > 0.04
	if smoke != null:
		smoke.amount_ratio = clampf(0.3 + (1.2 - live) * 0.35 + roar * 0.2, 0.15, 1.0)
		smoke.material_override.set_shader_parameter("density", lerpf(0.46, 0.30, live))
	if haze != null:
		haze.visible = live > 0.05
	if fog != null and fog.material is FogMaterial:
		(fog.material as FogMaterial).density = 0.025 * live * roar
		(fog.material as FogMaterial).emission = LIGHT_COLOR * (0.01 * live * roar)
	if light != null:
		light.visible = intensity > 0.02
		fill_light.visible = intensity > 0.02
	RenderingServer.global_shader_parameter_set("fire_intensity", intensity)
	if is_inside_tree():
		RenderingServer.global_shader_parameter_set("fire_position", global_position + Vector3(0.0, 0.4, 0.0))


static func flame_height(value: float) -> float:
	return 0.46 + 0.16 * clampf(value, 0.0, MAX_INTENSITY)


func apply_quality(p: QualityPreset) -> void:
	if light != null:
		light.shadow_enabled = p.fire_shadows
		light.shadow_caster_mask = p.fire_shadow_casters
	if _flame_mat != null:
		_flame_mat.set_shader_parameter("steps", int(round(lerpf(16.0, 48.0, clampf(p.particle_scale, 0.0, 1.0)))))
	if sparks != null:
		sparks.amount = maxi(int(round(_base_sparks * p.particle_scale)), 12)
	if smoke != null:
		smoke.amount = maxi(int(round(_base_smoke * p.particle_scale)), 8)


# ---------------------------------------------------------------- geometry

func _build_ring() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 404
	var rock_mat := PropMaterials.triplanar("rock", Color(0.66, 0.64, 0.6), 0.75, 0.2)
	rock_mat.set_shader_parameter("char_amount", 0.9)
	rock_mat.set_shader_parameter("char_radius", 1.05)
	var count := 15
	for i in count:
		var a := float(i) / float(count) * TAU + rng.randf_range(-0.08, 0.08)
		var r := RING_RADIUS + rng.randf_range(-0.06, 0.06)
		var s := rng.randf_range(0.19, 0.31)
		var mesh := MeshInstance3D.new()
		mesh.name = "RingStone_%02d" % i
		mesh.mesh = PropMeshes.rock(900 + i * 13, 1.0)
		mesh.material_override = rock_mat
		mesh.position = Vector3(cos(a) * r, 0.0, sin(a) * r)
		mesh.scale = Vector3(s, s * rng.randf_range(0.6, 0.85), s * rng.randf_range(0.85, 1.15))
		mesh.rotation = Vector3(rng.randf_range(-0.25, 0.25), rng.randf() * TAU, rng.randf_range(-0.25, 0.25))
		mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		mesh.gi_mode = GeometryInstance3D.GI_MODE_STATIC
		add_child(mesh)
		# Measure the rotated stone against the terrain across its footprint.
		# Bury 28–36% of its actual height, so the irregular base seats into
		# earth without leaving a dark gap or a rim of barely touching points.
		var low := INF
		var high := -INF
		var vertices: PackedVector3Array = mesh.mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]
		for vertex in vertices:
			var p := mesh.to_global(vertex)
			var clearance := p.y - field.height(p.x, p.z)
			low = minf(low, clearance)
			high = maxf(high, clearance)
		mesh.position.y -= lerpf(low, high, rng.randf_range(0.28, 0.36))


func _build_bed() -> void:
	var mb := MeshBuilder.new()
	# Enough segments that the bed's rim is a curve, and the whole bed sits
	# two centimetres into the pit floor so no slab edge shows past the logs.
	mb.add_displaced_sphere(16, 28, BED_RADIUS + 0.12, func(dir: Vector3) -> float:
		var rim := 1.0 - maxf(-dir.y, 0.0) * 0.5
		return rim * (0.90 + 0.10 * sin(dir.x * 9.0) * cos(dir.z * 7.0) + 0.04 * sin(dir.x * 23.0 + dir.z * 17.0)))
	var bed := MeshInstance3D.new()
	bed.name = "Coals"
	bed.mesh = mb.commit()
	bed.scale = Vector3(1.0, 0.16, 1.0)
	bed.position.y = -0.02
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/coals.gdshader")
	Camp.bind_texture(mat, "noise_tex", PropMaterials.NOISE)
	mat.set_shader_parameter("bed_radius", BED_RADIUS)
	bed.material_override = mat
	bed.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(bed)


func _build_logs() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 505
	var charred := PropMaterials.triplanar("wood", Color(0.55, 0.42, 0.3), 1.1, 0.0)
	charred.set_shader_parameter("char_amount", 1.0)
	charred.set_shader_parameter("char_radius", 0.62)
	charred.set_shader_parameter("ember_glow", 0.7)
	charred.set_shader_parameter("fire_warmth", 0.2)
	# Four logs leaning into the centre, two lying across the coals.
	for i in 4:
		var a := float(i) / 4.0 * TAU + 0.4 + rng.randf_range(-0.2, 0.2)
		var length := rng.randf_range(0.62, 0.82)
		var mesh := MeshInstance3D.new()
		mesh.mesh = PropMeshes.log_mesh(length, rng.randf_range(0.05, 0.072), 600 + i, 0.02)
		mesh.material_override = charred
		var foot := Vector3(cos(a) * 0.46, 0.05, sin(a) * 0.46)
		var head := Vector3(cos(a) * 0.08, 0.28 + float(i) * 0.03, sin(a) * 0.08)
		var axis := (head - foot).normalized()
		mesh.position = (foot + head) * 0.5
		mesh.basis = Basis.looking_at(axis, Vector3.UP).rotated(axis, rng.randf() * TAU) * Basis(Vector3.UP, PI * 0.5)
		mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		mesh.gi_mode = GeometryInstance3D.GI_MODE_STATIC
		add_child(mesh)
	for i in 2:
		var mesh := MeshInstance3D.new()
		mesh.mesh = PropMeshes.log_mesh(rng.randf_range(0.5, 0.62), rng.randf_range(0.045, 0.06), 640 + i, 0.03)
		mesh.material_override = charred
		mesh.position = Vector3(rng.randf_range(-0.08, 0.08), 0.07, rng.randf_range(-0.08, 0.08))
		mesh.rotation = Vector3(0.0, rng.randf() * TAU, rng.randf_range(-0.1, 0.1))
		mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		add_child(mesh)


func _build_tripod() -> void:
	var mb := MeshBuilder.new()
	var apex := Vector3(0.0, 1.72, 0.0)
	for i in 3:
		var points := tripod_leg(i)
		var foot := points[0]
		var top := points[1]
		PropMeshes.add_timber(mb, [foot, foot.lerp(top, 0.5), top], [0.03, 0.028, 0.024], 7, i + 1, Color(0.75, 0.68, 0.58), 1.6)
	var tripod := MeshInstance3D.new()
	tripod.name = "Tripod"
	tripod.mesh = mb.commit(null, true)
	tripod.material_override = PropMaterials.wood(Color(0.62, 0.5, 0.36), 0.5, 0.0, 1.0)
	tripod.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(tripod)

	var rope := MeshBuilder.new()
	PropMeshes.add_rope_coil(rope, apex - Vector3(0.0, 0.04, 0.0), 0.07, 5, 0.024)
	var lash := MeshInstance3D.new()
	lash.mesh = rope.commit()
	lash.material_override = PropMaterials.rope()
	lash.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(lash)

	var iron := MeshBuilder.new()
	var bail_top := Vector3(0.0, 1.02 + 0.46, 0.0)
	iron.add_tube([apex + Vector3(0.0, 0.02, 0.0), bail_top], [0.009, 0.009], 5, Color.WHITE, 1.0, 12.0, 0.0, true)
	PropMeshes.add_pot(iron, Vector3(0.0, 1.02, 0.0))
	var pot := MeshInstance3D.new()
	pot.name = "Pot"
	pot.mesh = iron.commit(null, true)
	var pot_material := ShaderMaterial.new()
	pot_material.shader = load("res://shaders/pot_iron.gdshader")
	pot.material_override = pot_material
	pot.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(pot)


func _build_particles() -> void:
	var noise: Texture2D = load(PropMaterials.NOISE)

	_build_flame_volume()

	sparks = _particles("Sparks", _base_sparks, 1.8, AABB(Vector3(-1.6, -0.2, -1.6), Vector3(3.2, 4.5, 3.2)))
	var spark_p := ParticleProcessMaterial.new()
	spark_p.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_SPHERE
	spark_p.emission_sphere_radius = 0.14
	spark_p.direction = Vector3(0.0, 1.0, 0.0)
	spark_p.spread = 32.0
	spark_p.initial_velocity_min = 1.1
	spark_p.initial_velocity_max = 2.6
	spark_p.gravity = Vector3(0.0, -0.3, 0.0)
	spark_p.damping_min = 0.6
	spark_p.damping_max = 1.4
	spark_p.scale_min = 0.5
	spark_p.scale_max = 1.2
	spark_p.particle_flag_align_y = true
	spark_p.color = Color(1.0, 0.6, 0.2)
	_lifetime_color(spark_p, [
		Color(1.0, 0.85, 0.5, 1.0),
		Color(1.0, 0.5, 0.12, 1.0),
		Color(0.85, 0.2, 0.02, 0.6),
		Color(0.3, 0.02, 0.0, 0.0),
	])
	spark_p.turbulence_enabled = true
	spark_p.turbulence_noise_strength = 1.3
	spark_p.turbulence_noise_scale = 1.6
	spark_p.turbulence_influence_min = 0.03
	spark_p.turbulence_influence_max = 0.1
	sparks.process_material = spark_p
	sparks.draw_pass_1 = _quad(Vector2(0.03, 0.13), Vector3.ZERO)
	sparks.material_override = _particle_material("res://shaders/spark.gdshader", null, {"glow": 3.0})
	sparks.position.y = 0.3
	add_child(sparks)

	smoke = _particles("Smoke", _base_smoke, 6.5, AABB(Vector3(-2.5, 0.0, -2.5), Vector3(5.0, 8.0, 5.0)))
	var smoke_p := ParticleProcessMaterial.new()
	# Born in a ring around the pot's footprint, just under its base, so the
	# column rises past the pot; the pot's collision sphere below deflects
	# what drifts into it instead of letting puffs pass through the iron.
	smoke_p.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_RING
	smoke_p.emission_ring_axis = Vector3.UP
	smoke_p.emission_ring_radius = 0.22
	smoke_p.emission_ring_inner_radius = 0.10
	smoke_p.emission_ring_height = 0.06
	smoke_p.direction = Vector3(0.0, 1.0, 0.0)
	smoke_p.spread = 11.0
	smoke_p.collision_mode = ParticleProcessMaterial.COLLISION_RIGID
	smoke_p.collision_friction = 0.55
	smoke_p.collision_bounce = 0.0
	smoke_p.initial_velocity_min = 0.5
	smoke_p.initial_velocity_max = 0.95
	smoke_p.gravity = Vector3(0.0, 0.3, 0.0)
	smoke_p.damping_min = 0.12
	smoke_p.damping_max = 0.3
	smoke_p.scale_min = 0.7
	smoke_p.scale_max = 1.2
	smoke_p.scale_curve = _curve([[0.0, 0.22], [0.3, 1.0], [1.0, 1.9]])
	smoke_p.angle_min = -180.0
	smoke_p.angle_max = 180.0
	smoke_p.angular_velocity_min = -18.0
	smoke_p.angular_velocity_max = 18.0
	smoke_p.color = Color(0.7, 0.68, 0.66, 1.0)
	_lifetime_color(smoke_p, [
		Color(0.55, 0.5, 0.46, 0.0),
		Color(0.62, 0.6, 0.58, 0.7),
		Color(0.7, 0.7, 0.7, 0.4),
		Color(0.75, 0.75, 0.75, 0.0),
	])
	smoke_p.turbulence_enabled = true
	smoke_p.turbulence_noise_strength = 0.7
	smoke_p.turbulence_noise_scale = 0.9
	smoke_p.turbulence_influence_min = 0.006
	smoke_p.turbulence_influence_max = 0.02
	smoke.process_material = smoke_p
	smoke.draw_pass_1 = _quad(Vector2(1.5, 1.5), Vector3.ZERO)
	smoke.material_override = _particle_material("res://shaders/smoke.gdshader", noise, {"density": 0.20})
	_smoke_process = smoke_p
	# Smoke is born above the flame envelope: sprites starting inside it
	# whitened the fire into haze before the tongues could read.
	smoke.position.y = 0.92
	add_child(smoke)
	var pot_shield := GPUParticlesCollisionSphere3D.new()
	pot_shield.name = "PotSmokeShield"
	pot_shield.radius = 0.21
	pot_shield.position = Vector3(0.0, 1.02 + 0.15, 0.0)
	add_child(pot_shield)


## The flames: a ray-marched volume over the ember bed (shaders/fire_volume).
func _build_flame_volume() -> void:
	var noise := FastNoiseLite.new()
	noise.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	noise.seed = 11
	noise.frequency = 0.045
	noise.fractal_type = FastNoiseLite.FRACTAL_FBM
	noise.fractal_octaves = 3
	noise.fractal_lacunarity = 2.2
	noise.fractal_gain = 0.55
	_flame_noise = NoiseTexture3D.new()
	_flame_noise.width = 64
	_flame_noise.height = 64
	_flame_noise.depth = 64
	_flame_noise.seamless = true
	_flame_noise.noise = noise
	var half := Vector3(0.75, 0.6, 0.75)
	flame_volume = MeshInstance3D.new()
	flame_volume.name = "Flames"
	var box := BoxMesh.new()
	box.size = half * 2.0
	flame_volume.mesh = box
	_flame_mat = ShaderMaterial.new()
	_flame_mat.shader = load("res://shaders/fire_volume.gdshader")
	_flame_mat.set_shader_parameter("noise_tex", _flame_noise)
	_flame_mat.set_shader_parameter("box_half", half)
	_flame_mat.render_priority = -1
	flame_volume.material_override = _flame_mat
	flame_volume.position = Vector3(0.0, FLAME_HEIGHT - 0.04 + half.y, 0.0)
	flame_volume.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	flame_volume.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	flame_volume.custom_aabb = AABB(-half - Vector3(0.2, 0.2, 0.2), half * 2.0 + Vector3(0.4, 0.4, 0.4))
	add_child(flame_volume)


func _build_haze() -> void:
	haze = MeshInstance3D.new()
	haze.name = "Heat"
	haze.mesh = _quad(Vector2(1.1, 1.5), Vector3.ZERO)
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/heat.gdshader")
	mat.set_shader_parameter("strength", 0.018)
	mat.render_priority = -2
	haze.material_override = mat
	haze.position = Vector3(0.0, 1.15, 0.0)
	haze.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	haze.custom_aabb = AABB(Vector3(-1.0, -1.0, -1.0), Vector3(2.0, 2.0, 2.0))
	add_child(haze)


func _build_lights() -> void:
	light = OmniLight3D.new()
	light.name = "FireLight"
	light.light_color = LIGHT_COLOR
	light.light_energy = BASE_ENERGY
	light.omni_range = BASE_RANGE
	# A shallow falloff preserves warmth across the clearing without the
	# inverse-distance hotspot bleaching the log tips beside this source.
	light.omni_attenuation = 0.8
	light.light_volumetric_fog_energy = 0.18
	light.light_size = 0.25
	light.light_specular = 0.7
	light.shadow_enabled = true
	light.shadow_blur = 2.6
	light.shadow_bias = 0.05
	light.shadow_normal_bias = 1.5
	light.omni_shadow_mode = OmniLight3D.SHADOW_DUAL_PARABOLOID
	light.position = Vector3(0.0, 0.5, 0.0)
	add_child(light)

	# Shadowless fill that carries a faint warmth across the clearing.
	fill_light = OmniLight3D.new()
	fill_light.name = "FireFill"
	fill_light.light_color = Color(1.0, 0.6, 0.3)
	fill_light.light_energy = FILL_ENERGY
	fill_light.omni_range = FILL_RANGE
	fill_light.omni_attenuation = 1.0
	# Bounced warmth originates at the coals. An elevated source illuminated
	# smoke particles as an orb inside the pot bail, even with fog energy zero.
	fill_light.light_volumetric_fog_energy = 0.0
	fill_light.light_specular = 0.0
	fill_light.shadow_enabled = false
	fill_light.position = Vector3(0.0, 0.22, 0.0)
	add_child(fill_light)


func _build_fog() -> void:
	fog = FogVolume.new()
	fog.name = "FireHaze"
	fog.layers = Pond.AIR_FOG_LAYER
	fog.size = Vector3(2.6, 3.2, 2.6)
	fog.shape = RenderingServer.FOG_VOLUME_SHAPE_ELLIPSOID
	var mat := FogMaterial.new()
	mat.density = 0.025
	mat.albedo = Color(1.0, 0.6, 0.3)
	mat.emission = LIGHT_COLOR * 0.01
	mat.edge_fade = 0.6
	fog.material = mat
	fog.position = Vector3(0.0, 1.3, 0.0)
	add_child(fog)


func _build_collision() -> void:
	body = Interactable.new()
	body.name = "FireBody"
	body.collision_layer = 1 | (1 << 1)
	body.collision_mask = 0
	body.set_meta("surface", &"rock")
	body.on_interact = interact
	body.prompt_text = prompt()
	var pit := CollisionShape3D.new()
	var cyl := CylinderShape3D.new()
	cyl.radius = RING_RADIUS + 0.12
	cyl.height = 0.6
	pit.shape = cyl
	pit.position.y = 0.3
	body.add_child(pit)
	for i in 3:
		var points := tripod_leg(i)
		var axis := points[1] - points[0]
		var leg := CollisionShape3D.new()
		leg.name = "TripodLeg_%d" % i
		var post := CylinderShape3D.new()
		post.radius = 0.04
		post.height = axis.length()
		leg.shape = post
		leg.position = (points[0] + points[1]) * 0.5
		leg.quaternion = Quaternion(Vector3.UP, axis.normalized())
		body.add_child(leg)
	add_child(body)


# ---------------------------------------------------------------- helpers

## One source for visible timber, player collision and camera clearance.
static func tripod_leg(index: int) -> Array[Vector3]:
	var angle := float(index) / 3.0 * TAU + 0.9
	# Feet stand far enough out that the leaning shafts clear the stone crowns.
	return [Vector3(cos(angle) * TRIPOD_FOOT_RADIUS, -0.05, sin(angle) * TRIPOD_FOOT_RADIUS),
		Vector3(cos(angle) * 0.05, 1.88, sin(angle) * 0.05)]


func _particles(node_name: String, amount: int, lifetime: float, aabb: AABB) -> GPUParticles3D:
	var p := GPUParticles3D.new()
	p.name = node_name
	p.amount = amount
	p.lifetime = lifetime
	p.preprocess = lifetime * 0.8
	p.explosiveness = 0.0
	p.randomness = 0.5
	p.visibility_aabb = aabb
	p.local_coords = false
	p.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	return p


static func _quad(size: Vector2, offset: Vector3) -> QuadMesh:
	var quad := QuadMesh.new()
	quad.size = size
	quad.center_offset = offset
	return quad


static func _curve(points: Array) -> CurveTexture:
	var curve := Curve.new()
	for p: Array in points:
		curve.add_point(Vector2(p[0], p[1]))
	var tex := CurveTexture.new()
	tex.curve = curve
	return tex


static func _lifetime_color(process: ParticleProcessMaterial, stops: Array[Color]) -> void:
	var ramp := Gradient.new()
	var offsets := PackedFloat32Array()
	var colors := PackedColorArray()
	for i in stops.size():
		offsets.append(float(i) / float(maxi(stops.size() - 1, 1)))
		colors.append(stops[i])
	ramp.offsets = offsets
	ramp.colors = colors
	var tex := GradientTexture1D.new()
	tex.gradient = ramp
	tex.width = 64
	process.color_ramp = tex


static func _particle_material(shader_path: String, noise: Texture2D, params: Dictionary) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load(shader_path)
	if noise != null:
		mat.set_shader_parameter("noise_tex", noise)
	for key in params:
		mat.set_shader_parameter(key, params[key])
	return mat
