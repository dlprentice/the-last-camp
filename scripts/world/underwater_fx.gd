class_name UnderwaterFX
extends Node3D

## Life below the pond surface: sun shafts leaning along the refracted sun
## and a slow cloud of suspended silt. Both live on the water layer so the
## mirror cameras never see them, and both only draw while the active lens
## is under the surface (the world controller's underwater blend).

const SHAFT_COUNT := 56
const SILT_COUNT := 700

var shafts: MultiMeshInstance3D
var silt: GPUParticles3D
var _shaft_material: ShaderMaterial
var _silt_material: ShaderMaterial
var _field: TerrainField
var _shown := -1.0


func _init(field: TerrainField) -> void:
	_field = field
	name = "UnderwaterFX"


func _ready() -> void:
	_build_shafts()
	_build_silt()
	Quality.preset_changed.connect(apply_quality)
	apply_quality(Quality.current)
	_apply_visibility(0.0)


func _process(_delta: float) -> void:
	var world := Game.world as WorldController
	_apply_visibility(world.underwater_blend if world != null else 0.0)


func _apply_visibility(amount: float) -> void:
	if is_equal_approx(amount, _shown):
		return
	_shown = amount
	visible = amount > 0.001
	_shaft_material.set_shader_parameter("visibility", amount)
	_silt_material.set_shader_parameter("visibility", amount)


func apply_quality(p: QualityPreset) -> void:
	if silt != null:
		silt.amount = maxi(int(round(SILT_COUNT * p.particle_scale)), 120)


## Shafts hang from the mean surface over the basin, densest where the
## water is deep enough for a beam to have somewhere to go.
func _build_shafts() -> void:
	var rng := RandomNumberGenerator.new()
	rng.seed = 4171
	var mesh := QuadMesh.new()
	mesh.size = Vector2(1.0, 1.0)
	mesh.center_offset = Vector3(0.0, -0.5, 0.0)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = mesh
	mm.instance_count = SHAFT_COUNT
	var centre := TerrainField.POND_CENTRE
	var placed := 0
	var attempts := 0
	while placed < SHAFT_COUNT and attempts < SHAFT_COUNT * 40:
		attempts += 1
		var angle := rng.randf_range(0.0, TAU)
		var radius := sqrt(rng.randf()) * TerrainField.POND_RADIUS * 0.82
		var p := centre + Vector2(cos(angle), sin(angle)) * radius
		var depth := _field.water_depth(p.x, p.y)
		if depth < 1.2:
			continue
		var length := clampf(depth * 1.15, 1.4, 4.2)
		mm.set_instance_transform(placed, Transform3D(Basis.IDENTITY, Vector3(p.x, TerrainField.WATER_LEVEL, p.y)))
		# Mostly faint wide sheets with a few brighter ribbons among them.
		var gain := 0.12 + 0.88 * pow(rng.randf(), 2.2)
		mm.set_instance_custom_data(placed, Color(rng.randf(), rng.randf_range(0.35, 1.5), length, gain))
		placed += 1
	mm.visible_instance_count = placed
	shafts = MultiMeshInstance3D.new()
	shafts.name = "SunShafts"
	shafts.multimesh = mm
	shafts.layers = Pond.WATER_LAYER
	shafts.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	shafts.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	shafts.custom_aabb = AABB(Vector3(centre.x - 24.0, TerrainField.WATER_LEVEL - 6.0, centre.y - 24.0), Vector3(48.0, 8.0, 48.0))
	_shaft_material = ShaderMaterial.new()
	_shaft_material.shader = load("res://shaders/sun_shaft.gdshader")
	_shaft_material.render_priority = 2
	shafts.material_override = _shaft_material
	add_child(shafts)


func _build_silt() -> void:
	var centre := TerrainField.POND_CENTRE
	silt = GPUParticles3D.new()
	silt.name = "Silt"
	silt.amount = SILT_COUNT
	silt.lifetime = 18.0
	silt.preprocess = 14.0
	silt.randomness = 0.6
	silt.local_coords = false
	silt.visibility_aabb = AABB(Vector3(-16.0, -4.5, -16.0), Vector3(32.0, 6.0, 32.0))
	silt.position = Vector3(centre.x, TerrainField.WATER_LEVEL - 1.5, centre.y)
	silt.layers = Pond.WATER_LAYER
	silt.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var process := ParticleProcessMaterial.new()
	process.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_BOX
	process.emission_box_extents = Vector3(11.5, 1.25, 11.5)
	process.direction = Vector3(0.0, 0.0, 0.0)
	process.spread = 180.0
	process.initial_velocity_min = 0.01
	process.initial_velocity_max = 0.05
	process.gravity = Vector3(0.0, -0.004, 0.0)
	process.damping_min = 0.1
	process.damping_max = 0.3
	process.scale_min = 0.005
	process.scale_max = 0.014
	process.turbulence_enabled = true
	process.turbulence_noise_strength = 0.35
	process.turbulence_noise_scale = 2.2
	process.turbulence_noise_speed = Vector3(0.05, 0.03, 0.05)
	process.turbulence_influence_min = 0.02
	process.turbulence_influence_max = 0.08
	var ramp := Gradient.new()
	ramp.set_color(0, Color(0.88, 0.96, 0.84, 0.0))
	ramp.set_color(1, Color(0.88, 0.96, 0.84, 0.0))
	ramp.add_point(0.15, Color(0.88, 0.96, 0.84, 0.75))
	ramp.add_point(0.85, Color(0.88, 0.96, 0.84, 0.75))
	var ramp_tex := GradientTexture1D.new()
	ramp_tex.gradient = ramp
	process.color_ramp = ramp_tex
	silt.process_material = process
	silt.draw_pass_1 = PropMeshes.sphere_mesh(0.5, 4, 5)
	_silt_material = ShaderMaterial.new()
	_silt_material.shader = load("res://shaders/silt.gdshader")
	silt.material_override = _silt_material
	add_child(silt)
