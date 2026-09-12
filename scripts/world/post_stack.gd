class_name PostStack
extends CanvasLayer

## Full-screen grade drawn after the 3D scene has been tonemapped and before
## the HUD (see shaders/post/grade.gdshader). A fragment pass over the back
## buffer costs a fraction of a millisecond and needs no compute dispatch.

const LAYER := 5

var material: ShaderMaterial
var rect: ColorRect


func _ready() -> void:
	name = "PostStack"
	layer = LAYER
	process_mode = Node.PROCESS_MODE_ALWAYS
	material = ShaderMaterial.new()
	material.shader = load("res://shaders/post/grade.gdshader")
	rect = ColorRect.new()
	rect.name = "Grade"
	rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	rect.material = material
	add_child(rect)


func set_param(param: StringName, value: Variant) -> void:
	material.set_shader_parameter(param, value)


func set_enabled(enabled: bool) -> void:
	rect.visible = enabled
