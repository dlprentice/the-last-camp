extends TestCase
## Source/resource contracts for close camp materials.


func test_iron_uses_dedicated_material_shader() -> void:
	var material := PropMaterials.iron(Color(0.12, 0.13, 0.14))
	assert_true(material != null, "iron material builds")
	assert_true(material.shader != null, "iron material has a shader")
	assert_eq(material.shader.resource_path, "res://shaders/iron.gdshader",
		"camp hardware no longer uses the metallic-rock shortcut")
	assert_eq(material.get_shader_parameter("tint"), Color(0.12, 0.13, 0.14),
		"iron tint contract is preserved")


func test_enamel_shader_exposes_age_and_chip_controls() -> void:
	var material := FieldKit.enamel(Color(0.22, 0.31, 0.33))
	assert_true(material != null and material.shader != null, "enamel material builds")
	assert_eq(material.shader.resource_path, "res://shaders/enamel.gdshader", "enamel shader path is stable")
	assert_true(material.get_shader_parameter("chip_amount") is float,
		"enamel shader exposes chip amount")
	assert_true(material.get_shader_parameter("age") is float,
		"enamel shader exposes age")


func test_rope_uses_uv_aware_fibre_shader() -> void:
	var material := PropMaterials.rope()
	assert_true(material != null and material.shader != null, "rope material builds")
	assert_eq(material.shader.resource_path, "res://shaders/rope.gdshader",
		"rope no longer reuses triplanar canvas")
	assert_true(material.get_shader_parameter("lay_turns_per_metre") is float,
		"rope exposes physical-ish strand lay control")


func test_shared_close_materials_compile_as_shader_resources() -> void:
	for path in ["res://shaders/enamel.gdshader", "res://shaders/iron.gdshader", "res://shaders/rope.gdshader", "res://shaders/wood_uv.gdshader", "res://shaders/canvas.gdshader"]:
		var shader := load(path) as Shader
		assert_true(shader != null, "%s loads as a Shader resource" % path)
