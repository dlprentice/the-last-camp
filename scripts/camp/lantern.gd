class_name CampLantern
extends Node3D

## A hurricane lantern: iron base and cage, a glass globe with a live flame,
## an omni light and a few moths. Mounted on a shepherd's-hook post, hung from
## an iron bracket, or simply hung from wherever the parent puts it. Interact
## toggles the flame.

signal toggled(on: bool)

enum Mount { HOOK_POST, BRACKET, HANGING }

const LIGHT_COLOR := Color(1.0, 0.7, 0.36)
const LIGHT_ENERGY := 1.6
const LIGHT_RANGE := 8.0
const POST_HEIGHT := 1.75
const FLAME_HEIGHT := 0.2
const GLASS_LAYER := 1 << 16

var lit := true
var attracts_moths := true
var light: OmniLight3D
var housing: Node3D
var glass_mesh: MeshInstance3D
var flame_mesh: MeshInstance3D
var moths: GPUParticles3D
var body: Interactable
var _glass_mat: StandardMaterial3D
var _flame_mat: ShaderMaterial
var _time := 0.0
var _phase := 0.0
var _seed := 0


func _init() -> void:
	name = "Lantern"


## `posted` mounts the lantern on a hook post standing at this node's origin;
## otherwise the housing hangs with its base at the origin. `bracket` adds an
## iron arm above the origin (for the dock pile) and hangs the lantern from it.
func build(posted: bool, shadows: bool, bracket := false, hang_from := Vector3.ZERO) -> void:
	_seed = hash(name) + int(position.x * 13.0) + int(position.z * 7.0)
	_phase = float(_seed % 1000) / 1000.0 * TAU
	var hang_at := hang_from
	if posted:
		hang_at = _add_post()
	elif bracket:
		hang_at = _add_bracket()
	_add_housing(hang_at)
	if hang_at != Vector3.ZERO:
		_add_link(hang_at)
	_add_light(shadows)
	if attracts_moths:
		_add_moths()
	_apply_lit()


func prompt() -> String:
	return "Douse lantern" if lit else "Light lantern"


func interact(_player: Node) -> void:
	set_lit(not lit)
	if Game.audio != null and Game.audio.has_method("play_interact"):
		Game.audio.play_interact(&"lantern_toggle")


func set_lit(value: bool) -> void:
	if lit == value:
		return
	lit = value
	_apply_lit()
	toggled.emit(lit)


func _process(delta: float) -> void:
	_time += delta
	# The same air moves the canopy, linen, smoke and hanging lanterns,
	# including an unlit lantern before sunset.
	if housing != null and housing.get_meta("swings", false):
		var wind: float = Game.world.wind_strength() if Game.world != null else 0.4
		housing.rotation.z = 0.03 * sin(_time * 1.3 + _phase) * wind
		housing.rotation.x = 0.02 * sin(_time * 0.9 + _phase * 2.0) * wind
	if not lit or light == null:
		return
	var flicker := 1.0 + 0.05 * sin(_time * 7.3 + _phase) + 0.035 * sin(_time * 13.1 + _phase * 1.7)
	light.light_energy = LIGHT_ENERGY * flicker
	if _flame_mat != null:
		_flame_mat.set_shader_parameter("heat", 1.7 * flicker)


## Hook post standing at the origin; returns the point the lantern hangs from.
func _add_post() -> Vector3:
	var post := MeshInstance3D.new()
	post.name = "Post"
	post.mesh = PropMeshes.hook_post_mesh(POST_HEIGHT, _seed)
	post.material_override = PropMaterials.wood(Color(0.5, 0.4, 0.3), 0.5, 0.0, 1.0)
	post.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	post.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	add_child(post)
	body = Interactable.new()
	body.collision_layer = 1 | (1 << 1)
	body.collision_mask = 0
	body.set_meta("surface", &"wood")
	body.on_interact = interact
	var collider := CollisionShape3D.new()
	var cyl := CylinderShape3D.new()
	cyl.radius = 0.05
	cyl.height = POST_HEIGHT
	collider.shape = cyl
	collider.position.y = POST_HEIGHT * 0.5
	body.add_child(collider)
	add_child(body)
	return PropMeshes.hook_point(POST_HEIGHT)


## Iron bracket rising from the origin (a pile head) with an arm to hang from.
func _add_bracket() -> Vector3:
	var mb := MeshBuilder.new()
	var rise := Vector3(0.0, 0.34, 0.0)
	var arm_end := rise + Vector3(0.24, 0.0, 0.0)
	mb.add_tube([Vector3(0.0, -0.06, 0.0), rise], [0.011, 0.011], 6, Color.WHITE, 1.0, 4.0, 0.0, true)
	mb.add_tube([rise + Vector3(-0.02, 0.0, 0.0), arm_end], [0.01, 0.009], 6, Color.WHITE, 1.0, 4.0, 0.0, true)
	mb.add_tube([rise + Vector3(0.0, -0.12, 0.0), rise + Vector3(0.14, -0.005, 0.0)], [0.007, 0.007], 5, Color.WHITE, 1.0, 4.0, 0.0, true)
	var bracket := MeshInstance3D.new()
	bracket.name = "Bracket"
	bracket.mesh = mb.commit()
	bracket.material_override = PropMaterials.iron()
	bracket.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	add_child(bracket)
	return arm_end + Vector3(0.0, -0.01, 0.0)


## The visible link between the hook and the bail: a short wire loop.
func _add_link(hang_at: Vector3) -> void:
	var mb := MeshBuilder.new()
	var bail_top := hang_at - Vector3(0.0, 0.03, 0.0)
	var link_r: Array[Vector3] = []
	var link_rad: Array[float] = []
	for i in 9:
		var a := float(i) / 8.0 * TAU
		link_r.append(hang_at + Vector3(0.0, -0.015 + cos(a) * 0.016, sin(a) * 0.012))
		link_rad.append(0.004)
	mb.add_tube(link_r, link_rad, 5, Color.WHITE, 1.0, 4.0, 0.0, false)
	var link := MeshInstance3D.new()
	link.name = "Link"
	link.mesh = mb.commit()
	link.material_override = PropMaterials.iron()
	link.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(link)
	# Hang the housing so the bail's top sits inside the link.
	housing.position = bail_top - Vector3(0.0, PropMeshes.HURRICANE_HEIGHT, 0.0)


func _add_housing(hang_at: Vector3) -> void:
	housing = Node3D.new()
	housing.name = "Housing"
	var hung := hang_at != Vector3.ZERO
	housing.position = hang_at - Vector3(0.0, PropMeshes.HURRICANE_HEIGHT, 0.0) if hung else Vector3.ZERO
	housing.set_meta("swings", hung)
	add_child(housing)

	var metal := MeshInstance3D.new()
	metal.name = "Metal"
	metal.mesh = PropMeshes.hurricane_metal()
	metal.material_override = PropMaterials.iron(Color(0.2, 0.17, 0.14))
	metal.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	housing.add_child(metal)

	glass_mesh = MeshInstance3D.new()
	glass_mesh.name = "Glass"
	glass_mesh.mesh = PropMeshes.hurricane_glass()
	glass_mesh.layers = GLASS_LAYER
	_glass_mat = StandardMaterial3D.new()
	_glass_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	_glass_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	_glass_mat.albedo_color = Color(1.0, 0.9, 0.7, 0.16)
	_glass_mat.roughness = 0.05
	_glass_mat.metallic = 0.0
	_glass_mat.metallic_specular = 0.9
	_glass_mat.emission_enabled = true
	_glass_mat.emission = Color(1.0, 0.62, 0.25)
	_glass_mat.emission_energy_multiplier = 0.22
	glass_mesh.material_override = _glass_mat
	glass_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	housing.add_child(glass_mesh)

	# The wick flame: a small camera-facing flame card (the fire shader at
	# candle scale) standing on the wick tube.
	flame_mesh = MeshInstance3D.new()
	flame_mesh.name = "Flame"
	var flame := QuadMesh.new()
	flame.size = Vector2(0.03, 0.05)
	flame.center_offset = Vector3(0.0, 0.02, 0.0)
	flame_mesh.mesh = flame
	_flame_mat = ShaderMaterial.new()
	_flame_mat.shader = load("res://shaders/fire.gdshader")
	_flame_mat.set_shader_parameter("heat", 1.7)
	_flame_mat.set_shader_parameter("opacity", 0.95)
	_flame_mat.set_shader_parameter("wind_lean", 0.0)
	flame_mesh.material_override = _flame_mat
	flame_mesh.position.y = FLAME_HEIGHT - 0.03
	flame_mesh.custom_aabb = AABB(Vector3(-0.05, -0.02, -0.05), Vector3(0.1, 0.1, 0.1))
	flame_mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	housing.add_child(flame_mesh)

	if body == null:
		body = Interactable.new()
		body.collision_layer = 1 | (1 << 1)
		body.collision_mask = 0
		body.on_interact = interact
		var collider := CollisionShape3D.new()
		var box := BoxShape3D.new()
		box.size = Vector3(0.16, 0.4, 0.16)
		collider.shape = box
		collider.position.y = 0.2
		body.add_child(collider)
		housing.add_child(body)


func _add_light(shadows: bool) -> void:
	light = OmniLight3D.new()
	light.name = "Light"
	light.light_color = LIGHT_COLOR
	light.light_energy = LIGHT_ENERGY
	# The point emitter is centimetres from the globe. Its diffuse lighting
	# turns transparent glass into a clipped white shell. The globe instead
	# carries a restrained warm emission and still reflects external lights.
	light.light_cull_mask = 0xFFFFF & ~GLASS_LAYER
	light.omni_range = LIGHT_RANGE
	light.omni_attenuation = 1.45
	light.light_volumetric_fog_energy = 0.18
	light.light_size = 0.05
	light.shadow_enabled = shadows
	light.omni_shadow_mode = OmniLight3D.SHADOW_DUAL_PARABOLOID
	light.shadow_blur = 2.0
	light.shadow_bias = 0.04
	light.distance_fade_enabled = true
	light.distance_fade_begin = 30.0
	light.distance_fade_length = 14.0
	light.position = Vector3(0.0, FLAME_HEIGHT + 0.02, 0.0)
	housing.add_child(light)


func _add_moths() -> void:
	moths = GPUParticles3D.new()
	moths.name = "Moths"
	moths.amount = 4
	moths.lifetime = 5.0
	moths.preprocess = 3.0
	moths.visibility_aabb = AABB(Vector3(-1.2, -0.4, -1.2), Vector3(2.4, 1.6, 2.4))
	moths.position = Vector3(0.0, FLAME_HEIGHT + 0.1, 0.0)
	var process := ParticleProcessMaterial.new()
	process.emission_shape = ParticleProcessMaterial.EMISSION_SHAPE_SPHERE
	process.emission_sphere_radius = 0.3
	process.direction = Vector3(0.0, 1.0, 0.0)
	process.spread = 180.0
	process.initial_velocity_min = 0.15
	process.initial_velocity_max = 0.45
	process.gravity = Vector3(0.0, 0.02, 0.0)
	process.damping_min = 0.4
	process.damping_max = 0.8
	process.scale_min = 0.01
	process.scale_max = 0.024
	process.color = Color(0.55, 0.45, 0.28)
	process.turbulence_enabled = true
	process.turbulence_noise_strength = 1.5
	process.turbulence_noise_scale = 2.5
	moths.process_material = process
	moths.draw_pass_1 = PropMeshes.sphere_mesh(0.5, 4, 6)
	# Moths reflect the lantern; they are not self-lit fireflies. An opaque,
	# lit surface also lets the tent canvas occlude them normally.
	var mat := StandardMaterial3D.new()
	mat.vertex_color_use_as_albedo = true
	mat.roughness = 0.92
	mat.metallic_specular = 0.15
	moths.material_override = mat
	housing.add_child(moths)


func _apply_lit() -> void:
	if light != null:
		light.visible = lit
	if moths != null:
		moths.emitting = lit
	if flame_mesh != null:
		flame_mesh.visible = lit
	if _glass_mat != null:
		_glass_mat.emission_energy_multiplier = 0.22 if lit else 0.0
		_glass_mat.albedo_color.a = 0.16 if lit else 0.14
	if body != null:
		body.prompt_text = prompt()
