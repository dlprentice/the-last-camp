class_name TerrainFieldDirector
extends Node

## Runs during child _ready(), before Main._ready() calls Camp.build(). Replaces
## Camp's field with a subclass so every later system receives one consistent
## enhanced surface; no visual-only displacement or duplicate collision exists.
##
## Weather is built by the World sibling before Camp._ready(), so its rain
## occlusion texture has already sampled the base TerrainField. Rebind that one
## derived artifact here as well; otherwise raised shoulders could receive rain
## through terrain even though mesh/collision/foliage all use the enhanced field.
## The replacement image is generated on a worker while the normal loading
## stages run, then uploaded on the main thread when ready.

const RAIN_HEIGHT_RESOLUTION := 512
const RAIN_HEIGHT_EXTENT := 128.0

var _rain_thread: Thread
var _rain_material: ShaderMaterial


func _ready() -> void:
	var camp := get_parent().get_node_or_null("Camp") as Camp
	if camp == null:
		push_error("TerrainFieldDirector could not find Camp")
		return
	var field := TerrainFieldEnhanced.new()
	camp.field = field
	_begin_weather_rain_sync(field)


func _begin_weather_rain_sync(field: TerrainField) -> void:
	var world := get_parent().get_node_or_null("World") as WorldController
	if world == null or world.weather == null or world.weather._rain_mesh == null:
		return
	_rain_material = world.weather._rain_mesh.material_override as ShaderMaterial
	if _rain_material == null:
		return
	_rain_thread = Thread.new()
	var error := _rain_thread.start(func() -> Image:
		return _build_rain_height_image(field))
	if error != OK:
		push_warning("Could not start enhanced rain-height bake: %s" % error_string(error))
		_rain_thread = null
		return
	set_process(true)


func _process(_delta: float) -> void:
	if _rain_thread == null:
		set_process(false)
		return
	if _rain_thread.is_alive():
		return
	var image: Image = _rain_thread.wait_to_finish()
	_rain_thread = null
	if is_instance_valid(_rain_material) and image != null and not image.is_empty():
		_rain_material.set_shader_parameter("ground_height", ImageTexture.create_from_image(image))
	set_process(false)


static func _build_rain_height_image(field: TerrainField) -> Image:
	var heights := Image.create(RAIN_HEIGHT_RESOLUTION, RAIN_HEIGHT_RESOLUTION, false, Image.FORMAT_RF)
	var span := RAIN_HEIGHT_EXTENT * 2.0
	for y in RAIN_HEIGHT_RESOLUTION:
		for x in RAIN_HEIGHT_RESOLUTION:
			var pos := Vector2(x, y) / float(RAIN_HEIGHT_RESOLUTION - 1) * span - Vector2.ONE * RAIN_HEIGHT_EXTENT
			heights.set_pixel(x, y, Color(field.height(pos.x, pos.y), 0.0, 0.0))
	return heights


func _exit_tree() -> void:
	if _rain_thread != null and _rain_thread.is_started():
		_rain_thread.wait_to_finish()
	_rain_thread = null
