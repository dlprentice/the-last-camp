class_name TerrainBuilder
extends RefCounted

## Turns a `TerrainField` into renderable and collidable geometry.
##
## One mesh: a uniform 0.5 m lattice under the camp (its heights come straight
## from the field's baked grid, so mesh, collision and scattered plants agree
## exactly) that grows geometrically towards the hills 700 m away with no LOD
## seams. Collision is the same grid as a HeightMapShape3D, plus a coarse
## outer heightfield so the walkable world does not end at the fine grid.

const INNER_SPACING := 0.5
const INNER_UNIFORM_HALF := 64.0
const OUTER_GROWTH := 1.08
## Fine noise needs supporting geometry: unbounded growth reached 47 m cells,
## leaving distant plants several metres above or below the visible hills.
const OUTER_MAX_SPACING := 4.0
const COLLISION_HALF := 85.0
const OUTER_COLLISION_HALF := 210.0
const OUTER_COLLISION_SPACING := 2.0

var field: TerrainField
var _axis := PackedFloat32Array()


func _init(p_field: TerrainField) -> void:
	field = p_field
	_axis = axis_coordinates()
	field.bake_surface_grid(_axis)


## Symmetric vertex coordinates along one axis: 0.5 m steps out to
## INNER_UNIFORM_HALF, then steps growing by OUTER_GROWTH to the world edge.
static func axis_coordinates() -> PackedFloat32Array:
	var positive := PackedFloat32Array()
	var x := 0.0
	while x < INNER_UNIFORM_HALF - 1e-4:
		positive.append(x)
		x += INNER_SPACING
	positive.append(INNER_UNIFORM_HALF)
	var step := INNER_SPACING
	var edge := TerrainField.OUTER_EXTENT * 0.5
	while x < edge:
		step = minf(step * OUTER_GROWTH, OUTER_MAX_SPACING)
		x = minf(x + step, edge)
		positive.append(x)
	var axis := PackedFloat32Array()
	for i in range(positive.size() - 1, 0, -1):
		axis.append(-positive[i])
	axis.append_array(positive)
	return axis


func resolution() -> int:
	return _axis.size()


func build_mesh(material: Material) -> ArrayMesh:
	var n := _axis.size()
	var count := n * n
	var heights := field.surface_heights
	var vertices := PackedVector3Array()
	vertices.resize(count)
	for iz in n:
		var z := _axis[iz]
		var row := iz * n
		for ix in n:
			var x := _axis[ix]
			var h := heights[row + ix]
			vertices[row + ix] = Vector3(x, h, z)

	var normals := PackedVector3Array()
	normals.resize(count)
	for iz in n:
		var iz0 := maxi(iz - 1, 0)
		var iz1 := mini(iz + 1, n - 1)
		var dz := _axis[iz1] - _axis[iz0]
		for ix in n:
			var ix0 := maxi(ix - 1, 0)
			var ix1 := mini(ix + 1, n - 1)
			var dx := _axis[ix1] - _axis[ix0]
			var hl := heights[iz * n + ix0]
			var hr := heights[iz * n + ix1]
			var hd := heights[iz0 * n + ix]
			var hu := heights[iz1 * n + ix]
			normals[iz * n + ix] = Vector3((hl - hr) / dx, 1.0, (hd - hu) / dz).normalized()

	# Material weights need the slope, which the normals now provide.
	var colors := PackedColorArray()
	colors.resize(count)
	var uvs := PackedVector2Array()
	uvs.resize(count)
	for iz in n:
		var z := _axis[iz]
		for ix in n:
			var idx := iz * n + ix
			var slope := acos(clampf(normals[idx].y, -1.0, 1.0))
			colors[idx] = field.material_mask(_axis[ix], z, heights[idx], slope)
			uvs[idx] = Vector2(_axis[ix], z)

	var indices := PackedInt32Array()
	indices.resize((n - 1) * (n - 1) * 6)
	var k := 0
	for iz in range(n - 1):
		for ix in range(n - 1):
			var a := iz * n + ix
			var b := a + 1
			var c := a + n
			var d := c + 1
			# Clockwise as seen from above (+Y): a -> b -> d -> c.
			indices[k] = a
			indices[k + 1] = b
			indices[k + 2] = d
			indices[k + 3] = a
			indices[k + 4] = d
			indices[k + 5] = c
			k += 6

	var arrays := []
	arrays.resize(Mesh.ARRAY_MAX)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_COLOR] = colors
	arrays[Mesh.ARRAY_INDEX] = indices
	var mesh := ArrayMesh.new()
	mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES, arrays)
	if material != null:
		mesh.surface_set_material(0, material)
	return mesh


## Fine collision straight from the baked height grid.
func build_collision() -> CollisionShape3D:
	assert(field.height_grid != null, "bake_height_grid() must run before build_collision()")
	var grid := field.height_grid
	var shape := HeightMapShape3D.new()
	shape.map_width = grid.resolution
	shape.map_depth = grid.resolution
	shape.map_data = grid.data
	var collider := CollisionShape3D.new()
	collider.name = "TerrainCollision"
	collider.shape = shape
	var cell := grid.cell_size()
	collider.scale = Vector3(cell, 1.0, cell)
	return collider


## Coarse collision for the forest beyond the fine grid. Inside the fine
## grid it is sunk so it can never poke through the precise surface.
func build_outer_collision() -> CollisionShape3D:
	var cells := int(OUTER_COLLISION_HALF * 2.0 / OUTER_COLLISION_SPACING) + 1
	var data := PackedFloat32Array()
	data.resize(cells * cells)
	var half := float(cells - 1) * OUTER_COLLISION_SPACING * 0.5
	var sink_inside := COLLISION_HALF - 6.0
	for iz in cells:
		var z := -half + float(iz) * OUTER_COLLISION_SPACING
		for ix in cells:
			var x := -half + float(ix) * OUTER_COLLISION_SPACING
			var h := field.height_fast(x, z)
			if absf(x) < sink_inside and absf(z) < sink_inside:
				h -= 1.0
			data[iz * cells + ix] = h
	var shape := HeightMapShape3D.new()
	shape.map_width = cells
	shape.map_depth = cells
	shape.map_data = data
	var collider := CollisionShape3D.new()
	collider.name = "OuterTerrainCollision"
	collider.shape = shape
	collider.scale = Vector3(OUTER_COLLISION_SPACING, 1.0, OUTER_COLLISION_SPACING)
	return collider
