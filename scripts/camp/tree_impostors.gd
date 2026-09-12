class_name TreeImpostors
extends Node3D

## Bakes every near-tree variant into six-view albedo and normal atlases on
## the GPU while the loading screen is up, then supplies the quad mesh and
## materials that stand in for a hero tree beyond the switch distance. The
## alpha-tested leaf cards of the treeline were the largest cost in the frame;
## a lit, normal-mapped billboard costs two triangles.

const TILES := 6
const TILE_HEIGHT := 384
const SWITCH_DISTANCE := 45.0
const SWITCH_MARGIN := 4.0

class Baked:
	var albedo: ImageTexture
	var normal: ImageTexture
	var extent: Vector2
	var base_y: float
	var material: ShaderMaterial
	var shadow_material: ShaderMaterial
	var quad: ArrayMesh

var baked: Dictionary = {}
var bake_seconds := 0.0
var _viewport: SubViewport
var _camera: Camera3D
var _stage: Node3D
var _shader: Shader = preload("res://shaders/tree_impostor.gdshader")


static func key_for(kind: TreeSpecies.Kind, index: int) -> String:
	return "%d_%d" % [int(kind), index]


func bake(forest: Forest) -> void:
	if DisplayServer.get_name() == "headless":
		return
	var t0 := Time.get_ticks_msec()
	_setup_viewport()
	# Freeze the sway while baking; the world controller writes the real gust
	# back into the global every frame once the build continues.
	RenderingServer.global_shader_parameter_set("wind_strength", 0.0)
	for kind: TreeSpecies.Kind in forest.variants:
		var species := TreeSpecies.by_kind(kind)
		var list: Array = forest.variants[kind]
		for index in list.size():
			await _bake_variant(forest, species, index, list[index])
	RenderingServer.global_shader_parameter_set("impostor_bake", 0)
	RenderingServer.global_shader_parameter_set("wind_strength", 1.0)
	_viewport.queue_free()
	_viewport = null
	bake_seconds = (Time.get_ticks_msec() - t0) / 1000.0
	print("Tree impostors: %d variants baked in %.1f s" % [baked.size(), bake_seconds])


func _setup_viewport() -> void:
	_viewport = SubViewport.new()
	_viewport.name = "ImpostorBake"
	_viewport.own_world_3d = true
	_viewport.transparent_bg = true
	_viewport.msaa_3d = Viewport.MSAA_DISABLED
	_viewport.screen_space_aa = Viewport.SCREEN_SPACE_AA_DISABLED
	_viewport.use_debanding = false
	_viewport.positional_shadow_atlas_size = 0
	_viewport.render_target_update_mode = SubViewport.UPDATE_DISABLED
	add_child(_viewport)
	_stage = Node3D.new()
	_viewport.add_child(_stage)
	_camera = Camera3D.new()
	# Only the plain layer: with the mirror and underwater layer bits set, the
	# tree shaders' reflection clipping discards every fragment of the bake.
	_camera.cull_mask = 1
	_camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	_camera.keep_aspect = Camera3D.KEEP_HEIGHT
	_camera.near = 0.1
	_camera.far = 400.0
	var env := Environment.new()
	env.background_mode = Environment.BG_COLOR
	env.background_color = Color(0, 0, 0, 0)
	env.ambient_light_source = Environment.AMBIENT_SOURCE_DISABLED
	env.reflected_light_source = Environment.REFLECTION_SOURCE_DISABLED
	env.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	env.tonemap_exposure = 1.0
	env.fog_enabled = false
	env.volumetric_fog_enabled = false
	env.glow_enabled = false
	env.ssao_enabled = false
	env.ssil_enabled = false
	env.sdfgi_enabled = false
	env.ssr_enabled = false
	_camera.environment = env
	_viewport.add_child(_camera)
	_camera.make_current()


func _bake_variant(forest: Forest, species: TreeSpecies, index: int, result: TreeGenerator.Result) -> void:
	var bounds: AABB = result.bark.get_aabb()
	if result.leaves != null:
		bounds = bounds.merge(forest.leaf_bounds(result))
	var width := clampf(maxf(bounds.size.x, bounds.size.z) * 1.04, 0.5, 40.0)
	var base_y := clampf(minf(bounds.position.y, 0.0), -2.0, 0.0)
	var height := clampf(bounds.end.y + 0.2 - base_y, 1.0, 60.0)
	# Tiles are clamped so an odd variant cannot request a giant viewport.
	var tile_width := clampi(int(ceil(TILE_HEIGHT * width / height)), 32, 512)
	_viewport.size = Vector2i(tile_width * TILES, TILE_HEIGHT)
	for child in _stage.get_children():
		child.free()
	for i in TILES:
		var root := Node3D.new()
		root.position = Vector3((float(i) + 0.5 - TILES * 0.5) * width, 0.0, 0.0)
		root.rotation.y = -float(i) / float(TILES) * TAU
		_stage.add_child(root)
		var bark := MeshInstance3D.new()
		bark.mesh = result.bark
		bark.material_override = forest.bark_materials[species.bark_set]
		bark.set_instance_shader_parameter("tree_height", result.height)
		bark.set_instance_shader_parameter("tint", Color.WHITE)
		root.add_child(bark)
		if result.leaves != null:
			var leaves := MeshInstance3D.new()
			leaves.mesh = result.leaves
			leaves.material_override = forest.leaf_materials[species.leaf_atlas]
			leaves.custom_aabb = forest.leaf_bounds(result)
			leaves.set_instance_shader_parameter("tree_height", result.height)
			leaves.set_instance_shader_parameter("crown",
					Color(result.crown_center.x, result.crown_center.y, result.crown_center.z, result.crown_radius))
			leaves.set_instance_shader_parameter("tint", species.leaf_tint)
			root.add_child(leaves)
	_camera.size = height
	_camera.position = Vector3(0.0, base_y + height * 0.5, 120.0)
	var albedo := await _render_pass(1)
	var normal := await _render_pass(2)
	# --impostor-dump=DIR writes every atlas as PNG for review.
	var dump := Game.arg_value("impostor-dump", "")
	if dump != "":
		DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(dump))
		albedo.save_png(dump.path_join("%s_%d_albedo.png" % [species.name, index]))
		normal.save_png(dump.path_join("%s_%d_normal.png" % [species.name, index]))
	var b := Baked.new()
	b.extent = Vector2(width, height)
	b.base_y = base_y
	b.albedo = ImageTexture.create_from_image(albedo)
	b.normal = ImageTexture.create_from_image(normal)
	b.quad = _quad(width, height, base_y)
	b.material = _material(b, species, false)
	b.shadow_material = _material(b, species, true)
	baked[key_for(species.kind, index)] = b


func _render_pass(mode: int) -> Image:
	RenderingServer.global_shader_parameter_set("impostor_bake", mode)
	_viewport.render_target_update_mode = SubViewport.UPDATE_ONCE
	await get_tree().process_frame
	await RenderingServer.frame_post_draw
	var image := _viewport.get_texture().get_image()
	if mode == 1:
		var used := image.get_used_rect()
		if used.size.x == 0:
			push_warning("Impostor bake produced an empty atlas (%s)" % _viewport.size)
	image.generate_mipmaps()
	return image


static func _quad(width: float, height: float, base_y: float) -> ArrayMesh:
	var vertices := PackedVector3Array([
		Vector3(-width * 0.5, base_y, 0.0), Vector3(width * 0.5, base_y, 0.0),
		Vector3(width * 0.5, base_y + height, 0.0), Vector3(-width * 0.5, base_y + height, 0.0)])
	var uvs := PackedVector2Array([Vector2(0, 1), Vector2(1, 1), Vector2(1, 0), Vector2(0, 0)])
	var normals := PackedVector3Array([Vector3.BACK, Vector3.BACK, Vector3.BACK, Vector3.BACK])
	var indices := PackedInt32Array([0, 1, 2, 0, 2, 3])
	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	mesh.custom_aabb = AABB(Vector3(-width * 0.5, base_y, -width * 0.5), Vector3(width, height, width))
	return mesh


func _material(b: Baked, species: TreeSpecies, face_sun: bool) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = _shader
	mat.set_shader_parameter("albedo_atlas", b.albedo)
	mat.set_shader_parameter("normal_atlas", b.normal)
	mat.set_shader_parameter("tiles", TILES)
	mat.set_shader_parameter("face_sun", face_sun)
	var conifer := species.kind == TreeSpecies.Kind.SPRUCE or species.kind == TreeSpecies.Kind.PINE
	mat.set_shader_parameter("translucency", 0.12 if conifer else 0.22)
	mat.set_shader_parameter("roughness", 0.66 if conifer else 0.6)
	return mat
