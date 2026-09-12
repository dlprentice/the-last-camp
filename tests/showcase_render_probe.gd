extends SceneTree
## Actual Forward+ material/geometry probe. Diagnostic studio, not a game view.
var view: SubViewport
var camera: Camera3D
var stage: Node3D
var motion := false
var motion_caption: Label
var motion_started := false
var output := "res://local-data/render-work/integration/probes"

func _init() -> void:
	call_deferred("_run")

func _run() -> void:
	await process_frame
	for argument in OS.get_cmdline_user_args():
		if argument == "--probe-motion":
			motion = true
		if argument.begins_with("--probe-out="):
			output = argument.trim_prefix("--probe-out=")
	if output.is_empty():
		push_error("Probe output directory is empty")
		quit(1)
		return
	# --script entry points compile before autoload singletons are registered.
	# Resolve dependent scripts after that first frame, like the test runner.
	var Habitat := load("res://scripts/camp/habitat_diversity.gd") as GDScript
	var Kit := load("res://scripts/props/field_kit.gd") as GDScript
	var Materials := load("res://scripts/props/prop_materials.gd") as GDScript
	if RenderingServer.get_current_rendering_method() != "forward_plus":
		push_error("Probe requires the actual Forward+ renderer")
		quit(1)
		return
	var directory_error := DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(output))
	if directory_error != OK:
		push_error("Could not create probe directory: " + error_string(directory_error))
		quit(1)
		return
	view = SubViewport.new()
	view.size = Vector2i(768, 512)
	view.own_world_3d = true
	view.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	view.msaa_3d = Viewport.MSAA_4X
	view.mesh_lod_threshold = 0.05
	root.add_child(view)
	if motion:
		# Display the actual SubViewport in Movie Maker's root viewport. This is
		# an explicitly labelled asset diagnostic, never the complete game scene.
		var display := TextureRect.new()
		display.texture = view.get_texture()
		display.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
		display.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
		root.add_child(display)
		display.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		var heading := Label.new()
		heading.text = "THE LAST CAMP  |  ASSET / MATERIAL PROBE"
		heading.position = Vector2(24, 20)
		heading.add_theme_font_size_override("font_size", 22)
		root.add_child(heading)
		motion_caption = Label.new()
		motion_caption.position = Vector2(24, 52)
		motion_caption.add_theme_font_size_override("font_size", 17)
		root.add_child(motion_caption)
	var world := WorldEnvironment.new()
	world.environment = Environment.new()
	world.environment.background_mode = Environment.BG_COLOR
	world.environment.background_color = Color(0.12, 0.15, 0.18)
	world.environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	world.environment.ambient_light_color = Color(0.64, 0.73, 0.90)
	world.environment.ambient_light_energy = 0.65
	world.environment.tonemap_mode = Environment.TONE_MAPPER_AGX
	view.add_child(world)
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-42, -35, 0)
	sun.light_color = Color(1.0, 0.88, 0.70)
	sun.light_energy = 1.8
	sun.shadow_enabled = true
	view.add_child(sun)
	camera = Camera3D.new()
	camera.cull_mask = 1
	camera.fov = 42
	view.add_child(camera)
	camera.make_current()
	for form in 4:
		stage = Node3D.new()
		view.add_child(stage)
		var plant := MultiMeshInstance3D.new()
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.use_custom_data = true
		mm.mesh = Habitat.plant_mesh(form, 60011 + form * 113)
		mm.instance_count = 1
		mm.set_instance_transform(0, Transform3D.IDENTITY)
		mm.set_instance_custom_data(0, Color(0.3, 1, 1, 1))
		plant.multimesh = mm
		var mat := ShaderMaterial.new()
		mat.shader = load("res://shaders/habitat.gdshader")
		plant.material_override = mat
		stage.add_child(plant)
		camera.position = Vector3(0.9, 0.78, 1.3)
		camera.look_at(Vector3(0, 0.23, 0))
		await capture("plant_%d" % form)
		stage.free()
	stage = Node3D.new()
	view.add_child(stage)
	Kit.kettle(stage, Vector3(-0.22, 0, 0))
	Kit.mug(stage, Vector3(0.18, 0, 0), Color(0.63, 0.64, 0.48))
	Kit.mug(stage, Vector3(0.36, 0, 0.12), Color(0.18, 0.32, 0.36))
	camera.position = Vector3(0.65, 0.50, 1.15)
	camera.look_at(Vector3(0, 0.14, 0))
	await capture("enamel_dry")
	RenderingServer.global_shader_parameter_set("scene_wetness", 1.0)
	await capture("enamel_wet")
	stage.free()
	stage = Node3D.new()
	view.add_child(stage)
	var hardware := MeshInstance3D.new()
	hardware.mesh = Kit.rounded_box(Vector3(0.38, 0.08, 0.12), 0.018)
	hardware.material_override = Materials.iron()
	stage.add_child(hardware)
	var rope := MeshBuilder.new()
	PropMeshes.add_rope(rope, Vector3(-0.35, 0.15, 0.1), Vector3(0.35, 0.15, 0.1), 0.08, 0.016, 12)
	var rope_node := MeshInstance3D.new()
	rope_node.mesh = rope.commit()
	rope_node.material_override = Materials.rope()
	stage.add_child(rope_node)
	camera.position = Vector3(0.45, 0.48, 1.05)
	camera.look_at(Vector3(0, 0.06, 0))
	await capture("hardware_wet")
	print("SHOWCASE_RENDER_PROBE_DONE output=", output, " frames_drawn=", Engine.get_frames_drawn())
	quit(0)

func capture(label: String) -> void:
	for frame in 8:
		await process_frame
	await RenderingServer.frame_post_draw
	var image := view.get_texture().get_image()
	if image == null or image.is_empty():
		push_error("Probe image is empty: " + label)
		quit(1)
		return
	var low := 1.0
	var high := 0.0
	for y in range(0, image.get_height(), 4):
		for x in range(0, image.get_width(), 4):
			var pixel := image.get_pixel(x, y)
			var value := (pixel.r + pixel.g + pixel.b) / 3.0
			low = minf(low, value)
			high = maxf(high, value)
	if high - low < 0.025:
		push_error("Probe produced no visible geometry: " + label)
		quit(1)
		return
	var error := image.save_png(output.path_join(label + ".png"))
	if error != OK:
		push_error("Probe save failed: " + label)
		quit(1)
		return
	print("PROBE_CAPTURE ", label)
	if motion:
		if not motion_started:
			motion_started = true
			print("PROBE_MOTION_START frames_drawn=", Engine.get_frames_drawn())
		var captions := {"plant_0": "Broadleaf rosette", "plant_1": "Compound fernlet",
			"plant_2": "Wet-margin rush", "plant_3": "Curled forest litter",
			"enamel_dry": "Enamelware / dry", "enamel_wet": "Enamelware / wet",
			"hardware_wet": "Iron and twisted rope / wet"}
		motion_caption.text = str(captions.get(label, label)) + "  |  Godot Forward+  |  Not a gameplay capture"
		var original_rotation := stage.rotation.y
		for frame in 36:
			var progress := float(frame) / 35.0
			stage.rotation.y = original_rotation + sin(progress * TAU) * 0.32
			await process_frame
			await RenderingServer.frame_post_draw
		stage.rotation.y = original_rotation
