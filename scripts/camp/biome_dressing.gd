class_name BiomeDressing
extends Node3D

## Ecological composition pass layered on top of the established generated scene.
## It does not replace the terrain/forest planners. It adds visual mass where the
## current composition reads sparse, and gives the pond a moisture-driven margin.
## All placement is deterministic and uses existing meshes/materials.

const CANOPY_TARGET := 190
const CANOPY_ATTEMPTS := 4200
const CANOPY_MIN_RADIUS := 82.0
const CANOPY_MAX_RADIUS := 205.0
const CANOPY_MIN_SPACING := 4.2
const SEDGE_ATTEMPTS := 3600
const RUSH_ATTEMPTS := 1500
const WET_SHRUB_ATTEMPTS := 650

var _camp: Camp
var _field: TerrainField
var _forest: Forest
var _understory: Understory
var _built := false


func _ready() -> void:
	name = "BiomeDressing"
	set_process(false)


func setup(camp: Camp) -> void:
	if _built:
		return
	_camp = camp
	_field = camp.field
	_forest = camp.forest
	_understory = camp.understory
	_build()
	_built = true


func _build() -> void:
	_build_sedge_margin()
	_build_rush_pockets()
	_build_wet_shrubs()


# ---------------------------------------------------------- shoreline zoning

func _build_sedge_margin() -> void:
	# Low fountain-form sedges occupy saturated soil from just below the water
	# line to the damp bank. This broad layer is intentionally denser than reeds.
	var rng := RandomNumberGenerator.new()
	rng.seed = 88031
	var transforms: Array[Transform3D] = []
	var customs: Array[Color] = []
	var colors: Array[Color] = []
	for attempt in SEDGE_ATTEMPTS:
		var angle := rng.randf() * TAU
		var shore := TerrainField.shore_point(angle)
		var radial := (shore - TerrainField.POND_CENTRE).normalized()
		var tangent := Vector2(-radial.y, radial.x)
		var p := shore + radial * rng.randf_range(-0.30, 2.45) + tangent * rng.randf_range(-1.3, 1.3)
		if _shore_navigation_blocked(p, 1.3):
			continue
		var y := _field.height_fast(p.x, p.y)
		var depth := TerrainField.WATER_LEVEL - y
		if depth > 0.20 or depth < -0.58:
			continue
		var moisture := smoothstep(-0.58, -0.05, depth)
		if rng.randf() > lerpf(0.42, 0.92, moisture):
			continue
		var width := rng.randf_range(0.42, 0.95)
		var height := rng.randf_range(0.42, 0.92) * lerpf(1.08, 0.78, maxf(depth, 0.0) / 0.20)
		var yaw := rng.randf() * TAU
		transforms.append(Transform3D(Basis(Vector3.UP, yaw).scaled(Vector3(width, height, width)),
			Vector3(p.x, y - 0.025, p.y)))
		customs.append(Color(rng.randf(), cos(yaw) * 0.5 + 0.5, sin(yaw) * 0.5 + 0.5, rng.randf_range(0.08, 0.24)))
		colors.append(Color(rng.randf_range(0.78, 0.96), rng.randf_range(0.92, 1.08), rng.randf_range(0.72, 0.92), rng.randf_range(0.0, 0.30)))
	_add_margin_multimesh("WetSedges", GrassPlanter.clump_mesh(9, 2, 0.035, 917), _wet_grass_material(false), transforms, customs, colors)


func _build_rush_pockets() -> void:
	# Taller emergents occur as broken colonies, not an even ring around the pond.
	var rng := RandomNumberGenerator.new()
	rng.seed = 88067
	var transforms: Array[Transform3D] = []
	var customs: Array[Color] = []
	var colors: Array[Color] = []
	for attempt in RUSH_ATTEMPTS:
		var angle := rng.randf() * TAU
		var colony := 0.5 + 0.5 * sin(angle * 5.0 + 1.7) * sin(angle * 2.0 - 0.8)
		if rng.randf() > smoothstep(0.35, 0.76, colony):
			continue
		var shore := TerrainField.shore_point(angle)
		var radial := (shore - TerrainField.POND_CENTRE).normalized()
		var tangent := Vector2(-radial.y, radial.x)
		var p := shore + radial * rng.randf_range(-0.42, 0.72) + tangent * rng.randf_range(-1.8, 1.8)
		if _shore_navigation_blocked(p, 1.8):
			continue
		var y := _field.height_fast(p.x, p.y)
		var depth := TerrainField.WATER_LEVEL - y
		if depth < -0.12 or depth > 0.38:
			continue
		var width := rng.randf_range(0.62, 1.08)
		var height := rng.randf_range(0.95, 1.65)
		var yaw := rng.randf() * TAU
		transforms.append(Transform3D(Basis(Vector3.UP, yaw).scaled(Vector3(width, height, width)),
			Vector3(p.x, y - 0.03, p.y)))
		customs.append(Color(rng.randf(), cos(yaw) * 0.5 + 0.5, sin(yaw) * 0.5 + 0.5, rng.randf_range(0.06, 0.18)))
		colors.append(Color(rng.randf_range(0.80, 0.98), rng.randf_range(0.90, 1.06), rng.randf_range(0.72, 0.92), rng.randf_range(0.04, 0.38)))
	_add_margin_multimesh("WetRushes", GrassPlanter.reed_mesh(), _wet_grass_material(true), transforms, customs, colors)


func _build_wet_shrubs() -> void:
	# Broadleaf shrubs sit one band uphill from emergents, where roots stay wet
	# but crowns remain terrestrial. Reuse the established shrub asset/material.
	if _understory.shrub_mesh == null or _understory.shrub_material == null:
		return
	var rng := RandomNumberGenerator.new()
	rng.seed = 88111
	var transforms: Array[Transform3D] = []
	var customs: Array[Color] = []
	for attempt in WET_SHRUB_ATTEMPTS:
		var angle := rng.randf() * TAU
		var shore := TerrainField.shore_point(angle)
		var radial := (shore - TerrainField.POND_CENTRE).normalized()
		var tangent := Vector2(-radial.y, radial.x)
		var p := shore + radial * rng.randf_range(1.2, 5.8) + tangent * rng.randf_range(-2.0, 2.0)
		if _shore_navigation_blocked(p, 2.4) or rng.randf() > 0.28:
			continue
		var y := _field.height_fast(p.x, p.y)
		if y < TerrainField.WATER_LEVEL - 0.02 or y > TerrainField.WATER_LEVEL + 1.35:
			continue
		var scale := rng.randf_range(0.72, 1.35)
		transforms.append(Transform3D(Basis(Vector3.UP, rng.randf() * TAU).scaled(Vector3(scale, scale * rng.randf_range(0.72, 1.18), scale)),
			Vector3(p.x, y - 0.05, p.y)))
		var tint := Color(0.82, 1.0, 0.78).lerp(Color(0.96, 0.92, 0.68), rng.randf() * 0.22)
		customs.append(Color(rng.randf(), tint.r, tint.g, tint.b))
	if transforms.is_empty():
		return
	var node := _add_simple_multimesh("WetBankShrubs", _understory.shrub_mesh, _understory.shrub_material, transforms, customs)
	node.layers = 1


func _wet_grass_material(tall: bool) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/grass.gdshader")
	mat.set_shader_parameter("root_color", Color(0.055, 0.10, 0.035) if tall else Color(0.07, 0.13, 0.045))
	mat.set_shader_parameter("tip_color", Color(0.34, 0.48, 0.18) if tall else Color(0.28, 0.47, 0.17))
	mat.set_shader_parameter("dry_tip_color", Color(0.48, 0.43, 0.22))
	mat.set_shader_parameter("sheen", 0.28 if tall else 0.36)
	mat.set_shader_parameter("trample_radius", 0.6)
	mat.set_shader_parameter("fade_start", 95.0)
	mat.set_shader_parameter("fade_end", 145.0)
	return mat


func _add_margin_multimesh(node_name: String, mesh: ArrayMesh, material: Material,
		transforms: Array[Transform3D], customs: Array[Color], colors: Array[Color]) -> void:
	if transforms.is_empty():
		return
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.use_colors = true
	mm.mesh = mesh
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
		mm.set_instance_custom_data(i, customs[i])
		mm.set_instance_color(i, colors[i])
	var node := MultiMeshInstance3D.new()
	node.name = node_name
	node.multimesh = mm
	node.material_override = material
	node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	node.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	node.layers = 1
	add_child(node)


func _add_simple_multimesh(node_name: String, mesh: ArrayMesh, material: Material,
		transforms: Array[Transform3D], customs: Array[Color]) -> MultiMeshInstance3D:
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.use_custom_data = true
	mm.mesh = mesh
	mm.instance_count = transforms.size()
	for i in transforms.size():
		mm.set_instance_transform(i, transforms[i])
		mm.set_instance_custom_data(i, customs[i])
	var node := MultiMeshInstance3D.new()
	node.name = node_name
	node.multimesh = mm
	node.material_override = material
	node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	node.gi_mode = GeometryInstance3D.GI_MODE_DISABLED
	add_child(node)
	return node


func _shore_navigation_blocked(p: Vector2, extra: float) -> bool:
	if p.distance_to(TerrainField.DOCK_START) < 4.0 + extra:
		return true
	if _field.walking_distance(p) < 0.55 + extra * 0.35:
		return true
	return false


static func _shore_offset(pos: Vector2) -> float:
	var delta := pos - TerrainField.POND_CENTRE
	return delta.length() - TerrainField.pond_radius_at(atan2(delta.y, delta.x))