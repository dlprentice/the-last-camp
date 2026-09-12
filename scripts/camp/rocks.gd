class_name CampRocks
extends Node3D

## Instantiates the planned boulders. Large rocks are individual meshes with
## collision; shore pebbles share one MultiMesh.

const COLLIDE_SCALE := 0.55

var field: TerrainField
var plan: ScenePlan
var variants: Array[ArrayMesh] = []
var material: ShaderMaterial


func _init(p_field: TerrainField, p_plan: ScenePlan) -> void:
	field = p_field
	plan = p_plan
	name = "Rocks"


func build() -> void:
	material = ShaderMaterial.new()
	material.shader = load("res://shaders/prop.gdshader")
	Camp.bind_prop_pbr(material, "rock")
	Camp.bind_texture(material, "noise_tex", "res://textures/noise_rgba.png")
	material.set_shader_parameter("tile", 0.45)
	material.set_shader_parameter("moss_amount", 0.4)
	material.set_shader_parameter("tint", Color(0.92, 0.9, 0.86))
	for i in 6:
		variants.append(PropMeshes.rock(1400 + i * 31, 1.0))
	var pebbles: Array[Transform3D] = []
	for entry in plan.rocks:
		if entry.scale < 0.45:
			pebbles.append(_xform(entry))
		else:
			_place_boulder(entry)
	if not pebbles.is_empty():
		_add_pebbles(pebbles)


func _xform(entry: ScenePlan.RockEntry) -> Transform3D:
	var y := field.height(entry.position.x, entry.position.y) - entry.sink * entry.scale * 0.35
	var basis := Basis(Vector3.UP, entry.rotation).scaled(Vector3.ONE * entry.scale)
	return Transform3D(basis, Vector3(entry.position.x, y, entry.position.y))


func _place_boulder(entry: ScenePlan.RockEntry) -> void:
	var mesh := MeshInstance3D.new()
	mesh.mesh = variants[entry.variant % variants.size()]
	mesh.material_override = material
	mesh.transform = _xform(entry)
	mesh.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	mesh.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	add_child(mesh)
	var body := StaticBody3D.new()
	body.collision_layer = 1
	body.collision_mask = 0
	body.set_meta("surface", &"rock")
	var shape := CollisionShape3D.new()
	var sphere := SphereShape3D.new()
	sphere.radius = entry.scale * COLLIDE_SCALE
	shape.shape = sphere
	shape.position.y = entry.scale * 0.2
	body.add_child(shape)
	body.position = mesh.position
	add_child(body)


func _add_pebbles(transforms: Array[Transform3D]) -> void:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = variants[0]
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
	var mmi := MultiMeshInstance3D.new()
	mmi.name = "Pebbles"
	mmi.multimesh = mm
	mmi.material_override = material
	mmi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	mmi.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	add_child(mmi)
