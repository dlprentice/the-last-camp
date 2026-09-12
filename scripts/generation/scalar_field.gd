class_name ScalarField
extends RefCounted

## A square grid of floats covering a world-space extent centred on the origin,
## with bilinear sampling and cheap "painting" of soft discs. Used to rasterise
## slow-to-evaluate spatial data (canopy coverage, trample maps) once so that
## per-vertex / per-instance queries stay O(1).

var resolution: int
var extent: float
var data: PackedFloat32Array


func _init(p_resolution: int, p_extent: float, fill := 0.0) -> void:
	resolution = maxi(p_resolution, 2)
	extent = p_extent
	data = PackedFloat32Array()
	data.resize(resolution * resolution)
	data.fill(fill)


func cell_size() -> float:
	return extent / float(resolution - 1)


func _to_grid(x: float, z: float) -> Vector2:
	var half := extent * 0.5
	var u := (x + half) / extent * float(resolution - 1)
	var v := (z + half) / extent * float(resolution - 1)
	return Vector2(u, v)


func get_cell(ix: int, iz: int) -> float:
	ix = clampi(ix, 0, resolution - 1)
	iz = clampi(iz, 0, resolution - 1)
	return data[iz * resolution + ix]


func set_cell(ix: int, iz: int, value: float) -> void:
	if ix < 0 or iz < 0 or ix >= resolution or iz >= resolution:
		return
	data[iz * resolution + ix] = value


## Bilinear sample at a world position; outside the extent the edge is clamped.
func sample(x: float, z: float) -> float:
	var g := _to_grid(x, z)
	var fx := clampf(g.x, 0.0, float(resolution - 1))
	var fz := clampf(g.y, 0.0, float(resolution - 1))
	var ix := int(floor(fx))
	var iz := int(floor(fz))
	var tx := fx - float(ix)
	var tz := fz - float(iz)
	var a := get_cell(ix, iz)
	var b := get_cell(ix + 1, iz)
	var c := get_cell(ix, iz + 1)
	var d := get_cell(ix + 1, iz + 1)
	return lerpf(lerpf(a, b, tx), lerpf(c, d, tx), tz)


## Paints a smooth disc: full strength inside `inner_radius`, falling to zero at
## `outer_radius`. Values combine with `max`, so overlapping discs saturate
## instead of exceeding `strength`.
func paint_disc(cx: float, cz: float, inner_radius: float, outer_radius: float, strength := 1.0) -> void:
	var g := _to_grid(cx, cz)
	var cell := cell_size()
	var r_cells := int(ceil(outer_radius / cell)) + 1
	var gx := int(round(g.x))
	var gz := int(round(g.y))
	for iz in range(gz - r_cells, gz + r_cells + 1):
		if iz < 0 or iz >= resolution:
			continue
		for ix in range(gx - r_cells, gx + r_cells + 1):
			if ix < 0 or ix >= resolution:
				continue
			var dx := (float(ix) - g.x) * cell
			var dz := (float(iz) - g.y) * cell
			var d := sqrt(dx * dx + dz * dz)
			var w := 1.0 - smoothstep(inner_radius, outer_radius, d)
			if w <= 0.0:
				continue
			var idx := iz * resolution + ix
			data[idx] = maxf(data[idx], w * strength)


## Fills the grid by evaluating `fn(x, z) -> float` at every cell.
func fill_with(fn: Callable) -> void:
	var half := extent * 0.5
	var cell := cell_size()
	for iz in resolution:
		var z := -half + float(iz) * cell
		for ix in resolution:
			var x := -half + float(ix) * cell
			data[iz * resolution + ix] = fn.call(x, z)


func max_value() -> float:
	var m := -INF
	for v in data:
		m = maxf(m, v)
	return m
