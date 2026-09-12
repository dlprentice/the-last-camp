extends SceneTree

## Bounded comparison of the current wood/cloth maps with two optional candidates.
## godot --path . --fullscreen --script res://tools/material_review.gd -- --study-dir=<candidate directory> --out=<fresh output directory>
## The eight sheets use identical meshes, exposure and lighting for every source.
## ImageGen albedo-only candidates deliberately receive flat normals and constant
## roughness, never an unrelated photoscan's detail maps.

var _study: String
const SHADERS := {
	"wood": "res://shaders/wood_uv.gdshader",
	"canvas": "res://shaders/canvas.gdshader",
	"tablecloth": "res://shaders/tablecloth.gdshader",
}
const TITLES := {
	"wood": "Worked wood",
	"canvas": "Tent canvas",
	"tablecloth": "Table linen",
}

var _out: String
var _page: Control
var _textures: Dictionary = {}
var _flat_normal: ImageTexture
var _wood_orm: ImageTexture
var _cloth_orm: ImageTexture
var _neutral_albedo: ImageTexture
var _sheet_number := 0


func _initialize() -> void:
	_run.call_deferred()


func _run() -> void:
	await process_frame
	_study = _argument("study-dir", "res://local-data/material-study")
	_out = _argument("out", _study.path_join("review-%d" % Time.get_unix_time_from_system()))
	_out = ProjectSettings.globalize_path(_out)
	if DirAccess.make_dir_recursive_absolute(_out) != OK:
		push_error("Cannot create material review directory: " + _out)
		quit(1)
		return
	# Never overwrite an earlier comparison, including a partly completed run.
	for name_value in DirAccess.get_files_at(_out):
		if name_value.ends_with(".png"):
			push_error("Choose a fresh material review directory: " + _out)
			quit(1)
			return
	root.content_scale_size = Vector2i(1920, 1080)
	root.content_scale_mode = Window.CONTENT_SCALE_MODE_CANVAS_ITEMS
	root.content_scale_aspect = Window.CONTENT_SCALE_ASPECT_KEEP
	Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
	_set_globals()
	_flat_normal = _constant_texture(Color(0.5, 0.5, 1.0))
	_wood_orm = _constant_texture(Color(1.0, 0.78, 0.0))
	_cloth_orm = _constant_texture(Color(1.0, 0.88, 0.0))
	_neutral_albedo = _constant_texture(Color(0.35, 0.35, 0.35))
	for kind: String in ["wood", "canvas", "tablecloth"]:
		for wet: float in [0.0, 1.0]:
			await _capture_sheet(kind, wet, false)
	for kind: String in ["wood", "canvas"]:
		await _capture_sheet(kind, 0.0, true)
	print("MATERIAL_REVIEW_DONE sheets=%d output=%s" % [_sheet_number, _out])
	quit()


func _argument(key: String, fallback: String) -> String:
	for value in OS.get_cmdline_user_args():
		if value.begins_with("--%s=" % key):
			return value.substr(key.length() + 3)
	return fallback


func _set_globals() -> void:
	# These globals are registered by project.godot before the script starts.
	var defaults := {
		"scene_wetness": 0.0, "rainfall": 0.0, "water_level": -100.0,
		"wind_direction": Vector2(1.0, 0.3), "wind_strength": 0.0,
		"fire_position": Vector3(0.0, -100.0, 0.0), "fire_intensity": 0.0,
		"sun_direction": Vector3.UP, "sun_color": Vector3.ONE, "daylight": 1.0,
	}
	for key: String in defaults:
		RenderingServer.global_shader_parameter_set(key, defaults[key])


func _sources(kind: String) -> Array[Dictionary]:
	var family := "wood" if kind == "wood" else "linen"
	var baseline := "wood" if kind == "wood" else "canvas"
	var generated := _study.path_join("imagegen/%s-albedo.png" % family)
	var mm := _study.path_join("material-maker/export/%s" % family)
	return [
		{"name": "Current downloaded material", "albedo": "res://textures/%s_albedo.png" % baseline,
			"normal": "res://textures/%s_normal.png" % baseline, "orm": "res://textures/%s_orm.png" % baseline,
			"detail": "Matched albedo + normal + AO / roughness / height"},
		{"name": "ImageGen candidate", "albedo": generated, "normal": "", "orm": "",
			"detail": "Albedo only; flat normal; constant roughness %.2f" % (0.78 if kind == "wood" else 0.88)},
		{"name": "Material Maker candidate", "albedo": mm + "_albedo.png", "normal": mm + "_normal.png",
			"orm": mm + "_orm.png", "detail": "Coherent procedural albedo + normal + AO / roughness / height"},
	]


func _capture_sheet(kind: String, wet: float, raw: bool) -> void:
	if is_instance_valid(_page):
		_page.queue_free()
		await process_frame
	RenderingServer.global_shader_parameter_set("scene_wetness", wet)
	_page = Control.new()
	_page.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	root.add_child(_page)
	var background := ColorRect.new()
	background.color = Color(0.035, 0.045, 0.052)
	background.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	_page.add_child(background)
	var mode := "Albedo tiling — unlit" if raw else "Dry" if wet == 0.0 else "Fully wetted"
	_label("%s  /  %s" % [TITLES[kind], mode], Vector2(32, 22), Vector2(1856, 45), 31)
	var method := "Two repeats across each axis. All three maps shown at the same UV scale; no per-candidate colour correction or derived detail."
	if not raw:
		method = "Project shader, fixed camera and AgX exposure 1.0. Left: curved close swatch. Right: flat tiling swatch."
		if kind == "wood":
			method = "Fixed AgX exposure 1.0. Left: curved crop using the pier's 0.214-wide UV strip. Right: 2×2 full-map diagnostic."
	_label(method, Vector2(34, 73), Vector2(1845, 34), 18, Color(0.69, 0.75, 0.77))
	var sources := _sources(kind)
	for column in sources.size():
		var source: Dictionary = sources[column]
		var x := 32.0 + column * 624.0
		var available := FileAccess.file_exists(ProjectSettings.globalize_path(source.albedo))
		_label(source.name, Vector2(x, 122), Vector2(604, 28), 23)
		var detail: String = source.detail if available else "Not available at the expected candidate path"
		_label(detail, Vector2(x, 154), Vector2(604, 42), 16, Color(0.64, 0.71, 0.74))
		if raw:
			_make_view(kind, source, Vector2(x, 229), Vector2i(604, 604), false, true, available)
		else:
			for row in 2:
				var y := 227.0 + row * 382.0
				_label("Front / key light" if row == 0 else "Grazing light", Vector2(x, y - 24), Vector2(604, 25), 17)
				_make_view(kind, source, Vector2(x, y), Vector2i(604, 340), row == 1, false, available)
	var note := "A good albedo image is not a complete PBR material. Missing normal/roughness data remains an explicit limitation."
	if kind == "tablecloth" and not raw:
		note = "Actual tablecloth shader: authored linen tint, stripes, subtle weave normal and fixed roughness response; texture ORM is unused."
	elif kind == "canvas" and not raw:
		note = "Actual canvas shader retains its stain suppression and tint; map differences are intentionally moderated by that material."
	_label(note, Vector2(34, 1005), Vector2(1845, 50), 18, Color(0.69, 0.75, 0.77))
	# Let mipmaps, shader compilation and temporal AA settle; every cell receives
	# the same delay and the world has no time-dependent motion.
	for frame in 36:
		await process_frame
	await RenderingServer.frame_post_draw
	_sheet_number += 1
	var suffix := "albedo-tiling" if raw else "dry" if wet == 0.0 else "wet"
	var filename := "%02d-%s-%s.png" % [_sheet_number, kind, suffix]
	var error := root.get_texture().get_image().save_png(_out.path_join(filename))
	if error != OK:
		push_error("Material review PNG failed: " + filename)
		quit(1)
		return
	print("MATERIAL_REVIEW_CAPTURE " + filename)


func _make_view(kind: String, source: Dictionary, position: Vector2, size_value: Vector2i,
		grazing: bool, raw: bool, available: bool) -> void:
	var viewport := SubViewport.new()
	viewport.size = size_value
	viewport.own_world_3d = true
	viewport.use_taa = true
	viewport.use_debanding = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_page.add_child(viewport)
	var display := TextureRect.new()
	display.position = position
	display.size = Vector2(size_value)
	display.texture = viewport.get_texture()
	display.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_page.add_child(display)
	var world := Node3D.new()
	viewport.add_child(world)
	var environment := Environment.new()
	environment.background_mode = Environment.BG_COLOR
	environment.background_color = Color(0.085, 0.105, 0.12)
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.94, 0.96, 1.0)
	environment.ambient_light_energy = 0.42
	environment.tonemap_mode = Environment.TONE_MAPPER_AGX
	environment.tonemap_exposure = 1.0
	var sky_material := ProceduralSkyMaterial.new()
	sky_material.sky_top_color = Color(0.32, 0.36, 0.41)
	sky_material.sky_horizon_color = Color(0.62, 0.65, 0.67)
	sky_material.ground_horizon_color = Color(0.62, 0.65, 0.67)
	sky_material.ground_bottom_color = Color(0.16, 0.18, 0.2)
	var sky := Sky.new()
	sky.sky_material = sky_material
	environment.sky = sky
	environment.reflected_light_source = Environment.REFLECTION_SOURCE_SKY
	var env_node := WorldEnvironment.new()
	env_node.environment = environment
	world.add_child(env_node)
	var light := DirectionalLight3D.new()
	light.light_energy = 3.0
	light.light_color = Color.WHITE
	light.light_angular_distance = 1.0
	light.position = Vector3(-6.0, 1.3, 1.1) if grazing else Vector3(2.5, 3.0, 6.0)
	world.add_child(light)
	light.look_at(Vector3.ZERO, Vector3.UP)
	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 1.34 if raw else 1.48
	camera.near = 0.02
	camera.far = 30.0
	# Exclude the project's reflection marker bits so the normal materials
	# do not interpret this ordinary camera as a mirrored underwater view.
	camera.cull_mask = 0x3FFFF
	camera.position = Vector3(0.0, 0.0, 4.0)
	world.add_child(camera)
	camera.look_at(Vector3.ZERO, Vector3.UP)
	camera.make_current()
	var material := _material(kind, source, raw, available)
	if raw:
		_add_panel(world, material, 0.0, false, Vector2(2.0, 2.0))
	else:
		var uv := Vector2(1.9, 1.2) if kind == "tablecloth" else Vector2(0.214, 1.08) if kind == "wood" else Vector2.ONE
		_add_panel(world, material, -0.62, true, uv, Vector2(0.268, 0.0) if kind == "wood" else Vector2.ZERO)
		_add_panel(world, material, 0.62, false, uv if kind == "tablecloth" else Vector2(2.0, 2.0))
	if not available:
		_label("Candidate not available", position + Vector2(16, 18), Vector2(size_value.x - 32, 40), 22)


func _material(kind: String, source: Dictionary, raw: bool, available: bool) -> Material:
	var albedo := _texture(source.albedo) if available else _neutral_albedo
	if raw:
		var simple := StandardMaterial3D.new()
		simple.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
		simple.albedo_texture = albedo
		simple.texture_filter = BaseMaterial3D.TEXTURE_FILTER_LINEAR_WITH_MIPMAPS_ANISOTROPIC
		return simple
	var material := ShaderMaterial.new()
	material.shader = load(SHADERS[kind])
	var normal := _texture(source.normal) if not source.normal.is_empty() and available else _flat_normal
	var orm := _texture(source.orm) if not source.orm.is_empty() and available else _wood_orm if kind == "wood" else _cloth_orm
	if kind == "tablecloth":
		material.set_shader_parameter("weave_tex", albedo)
		material.set_shader_parameter("weave_normal", normal)
	else:
		material.set_shader_parameter("albedo_tex", albedo)
		material.set_shader_parameter("normal_tex", normal)
		material.set_shader_parameter("orm_tex", orm)
		material.set_shader_parameter("noise_tex", _texture("res://textures/noise_rgba.png"))
		if kind == "wood":
			material.set_shader_parameter("weathering", 0.0)
			material.set_shader_parameter("algae_amount", 0.0)
		else:
			material.set_shader_parameter("ground_darkening", 0.0)
	return material


func _add_panel(parent: Node3D, material: Material, x: float, curved: bool, uv_scale: Vector2,
		uv_offset := Vector2.ZERO) -> void:
	var builder := MeshBuilder.new()
	const NX := 32
	const NY := 24
	for j in NY + 1:
		for i in NX + 1:
			var u := float(i) / NX
			var v := float(j) / NY
			var bulge := 0.15 * cos((u - 0.5) * TAU) if curved else 0.0
			var slope := -0.15 * TAU * sin((u - 0.5) * TAU) / 1.08 if curved else 0.0
			builder.add_vertex(Vector3((u - 0.5) * 1.08, (0.5 - v) * 1.08, bulge),
				Vector3(-slope, 0.0, 1.0).normalized(), Vector2(u, v) * uv_scale + uv_offset)
	for j in NY:
		for i in NX:
			var a := j * (NX + 1) + i
			builder.add_quad_facing(a, a + 1, a + NX + 2, a + NX + 1, Vector3.BACK)
	builder.recompute_tangents()
	var mesh := MeshInstance3D.new()
	mesh.mesh = builder.commit()
	mesh.material_override = material
	mesh.position.x = x
	parent.add_child(mesh)


func _texture(path: String) -> Texture2D:
	if _textures.has(path):
		return _textures[path]
	var texture: Texture2D
	if path.begins_with("res://textures/"):
		texture = load(path)
	else:
		var image := Image.load_from_file(ProjectSettings.globalize_path(path))
		if image == null or image.is_empty():
			push_error("Cannot read material candidate map: " + path)
			return null
		image.generate_mipmaps()
		texture = ImageTexture.create_from_image(image)
	_textures[path] = texture
	return texture


func _constant_texture(color: Color) -> ImageTexture:
	var image := Image.create(4, 4, false, Image.FORMAT_RGB8)
	image.fill(color)
	image.generate_mipmaps()
	return ImageTexture.create_from_image(image)


func _label(value: String, position: Vector2, size_value: Vector2, font_size: int,
		color := Color(0.89, 0.92, 0.92)) -> void:
	var label := Label.new()
	label.text = value
	label.position = position
	label.size = size_value
	label.add_theme_font_size_override("font_size", font_size)
	label.add_theme_color_override("font_color", color)
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_page.add_child(label)
