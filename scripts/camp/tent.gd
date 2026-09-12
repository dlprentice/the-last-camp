class_name Tent
extends Node3D

## A canvas A-frame tent: crossed poles front and back with a ridge pole,
## one sheet of canvas draped over the ridge with sag between its pegs, a back
## wall, tied-back door flaps, guy lines to stakes, a ground sheet, bedroll,
## pack, and a lantern hung from the front crossing that glows through the
## canvas at night. Local frame: origin at the ground centre, door towards -Z.

const WIDTH := 2.4
const LENGTH := 3.0
const HEIGHT := 1.6
const POLE_RADIUS := 0.028
const SAG := 0.075
## Sewn panels along the ridge; the cloth bellies between the taut seams.
const PANEL_WIDTH := 0.75
const CANVAS_TILE := 0.30

var field: TerrainField
var lantern: CampLantern
var _canvas: ShaderMaterial
var _wood: ShaderMaterial
var _rope: ShaderMaterial


func _init(p_field: TerrainField) -> void:
	field = p_field
	name = "Tent"


func build() -> void:
	var pos := TerrainField.TENT
	var y := field.height(pos.x, pos.y)
	var to_fire := Vector3(-pos.x, 0.0, -pos.y).normalized()
	position = Vector3(pos.x, y, pos.y)
	basis = Basis.looking_at(to_fire, Vector3.UP)

	_canvas = ShaderMaterial.new()
	_canvas.shader = load("res://shaders/canvas.gdshader")
	Camp.bind_prop_pbr(_canvas, "canvas")
	Camp.bind_texture(_canvas, "noise_tex", PropMaterials.NOISE)
	_wood = PropMaterials.wood(Color(0.62, 0.5, 0.36), 0.35, 0.0, 1.0)
	_rope = PropMaterials.rope()

	_add_mesh("Canvas", _canvas_mesh(), _canvas, true)
	_add_mesh("CanvasHems", _hem_mesh(), FieldKit.solid(Color(0.28, 0.24, 0.16), 0.96), false)
	_add_mesh("Poles", _pole_mesh(), _wood, true)
	_add_mesh("Lines", _line_mesh(), _rope, false)
	_add_mesh("Pegs", _peg_mesh(), PropMaterials.wood(Color(0.45, 0.36, 0.26), 0.5, 0.0, 1.0), false)
	_add_furniture()
	_add_lantern()
	_add_collision()


func _add_mesh(node_name: String, mesh: ArrayMesh, material: Material, gi: bool) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	mi.name = node_name
	mi.mesh = mesh
	mi.material_override = material
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	mi.gi_mode = GeometryInstance3D.GI_MODE_STATIC if gi else GeometryInstance3D.GI_MODE_DISABLED
	add_child(mi)
	return mi


## Height of the ridge line at a point along the tent (rises at the poles).
static func ridge_y(t: float) -> float:
	return HEIGHT - 0.03 * sin(t * PI)


## One sloping side as a sagging grid from the ridge to the ground line.
func _add_side(mb: MeshBuilder, side: float) -> void:
	var along := 48
	var down := 24
	var rows: Array[PackedInt32Array] = []
	for j in range(down + 1):
		var v := float(j) / float(down)
		var row := PackedInt32Array()
		for i in range(along + 1):
			var u := float(i) / float(along)
			var z := (u - 0.5) * LENGTH
			var p := _side_point(u, v, side)
			var uv := Vector2(z / CANVAS_TILE, v * WIDTH * 0.62 / CANVAS_TILE)
			var color := Color(1.0 - v, sin(v * PI) * sin(u * PI), 0.0, 1.0)
			var tangent := Vector4(0.0, 0.0, 1.0, 1.0)
			row.append(mb.add_vertex(p, Vector3.UP, uv, color, tangent))
		rows.append(row)
	for j in down:
		for i in along:
			mb.add_quad_facing(rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i], Vector3(side, 0.7, 0.0))


static func _side_point(u: float, v: float, side: float) -> Vector3:
	var p := Vector3(side * v * WIDTH * 0.5, ridge_y(u) * (1.0 - v), (u - 0.5) * LENGTH)
	var tension := sin(v * PI) * sin(u * PI)
	var sag := SAG * sin(v * PI) * (0.55 + 0.45 * sin(u * PI))
	sag += tension * (sin(u * 18.0 + v * 4.0) * 0.006 + sin(u * 27.0 - v * 3.0) * 0.003)
	var seam_phase := absf(fposmod((u - 0.5) * LENGTH / PANEL_WIDTH + 0.5, 1.0) - 0.5) * 2.0
	sag += 0.016 * sin(v * PI) * smoothstep(0.0, 0.6, seam_phase)
	return p - Vector3(side * 0.35, 1.0, 0.0).normalized() * sag


func _canvas_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	_add_side(mb, -1.0)
	_add_side(mb, 1.0)
	# Back wall: a triangle from the ridge to both peg lines.
	var back_z := LENGTH * 0.5
	var apex := Vector3(0.0, ridge_y(1.0) - 0.02, back_z - 0.02)
	var bl := Vector3(-WIDTH * 0.5 + 0.03, 0.0, back_z - 0.02)
	var br := Vector3(WIDTH * 0.5 - 0.03, 0.0, back_z - 0.02)
	var a := mb.add_vertex(apex, Vector3.BACK, Vector2(0.5, 0.0), Color(1, 0, 0, 1), Vector4(1, 0, 0, 1))
	var b := mb.add_vertex(bl, Vector3.BACK, Vector2(0.0, 1.8), Color(0, 0, 0, 1), Vector4(1, 0, 0, 1))
	var c := mb.add_vertex(br, Vector3.BACK, Vector2(1.8, 1.8), Color(0, 0, 0, 1), Vector4(1, 0, 0, 1))
	mb.add_triangle(a, b, c)
	_add_gathered_doors(mb)
	_add_sod_cloth(mb)
	mb.recompute_normals()
	mb.recompute_tangents()
	return mb.commit()


## Door cloth gathers into pleats at a visible tie, clear of the roof panel.
## The hem at the front stays attached; the free edge is pulled toward it.
func _add_gathered_doors(mb: MeshBuilder) -> void:
	for side: float in [-1.0, 1.0]:
		var start := mb.vertex_count()
		for row in 33:
			var v := float(row) / 32.0
			var tie := exp(-pow((v - 0.58) / 0.15, 2.0))
			var width := sin(v * PI) * lerpf(0.26, 0.045, tie)
			for col in 13:
				var u := float(col) / 12.0
				var p := _side_point(0, v, side)
				# Fold into the doorway, in front of the roof edge. No overlay
				# triangle can cast a misleading dark patch across the side wall.
				p.x -= side * width * u
				p.z -= 0.025 + sin(u * PI * 6.0) * sin(v * PI) * (0.020 + 0.015 * (1.0 - tie))
				p.y -= sin(u * PI) * sin(v * PI) * 0.012
				mb.add_vertex(p, Vector3.FORWARD, Vector2(u * 0.6, v * 1.9) / CANVAS_TILE, Color(1.0 - v, u * sin(v * PI) * (1.0 - tie), 0, 1))
		for row in 32:
			for col in 12:
				var a := start + row * 13 + col
				mb.add_quad_facing(a, a + 1, a + 14, a + 13, Vector3.FORWARD)


func _hem_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	for side: float in [-1.0, 1.0]:
		for seam_u: float in [0.0, 0.5, 1.0]:
			var points: Array[Vector3] = []
			var radii: Array[float] = []
			for i in 33:
				points.append(_side_point(seam_u, float(i) / 32.0, side) + Vector3(side, 0.7, 0).normalized() * 0.002)
				radii.append(0.0015)
			mb.add_tube(points, radii, 4)
		var at := _side_point(0, 0.58, side) + Vector3(-side * 0.020, 0, -0.035)
		PropMeshes.add_rope(mb, at + Vector3(-0.034, 0.018, 0), at + Vector3(0.034, -0.018, 0), 0.002, 0.004, 5)
		PropMeshes.add_rope(mb, at, at + Vector3(side * 0.025, -0.095, -0.014), 0.007, 0.003, 5)
	return mb.commit()


## Sod cloth: the strip of canvas that continues past each ground line and
## lies flat on the earth, sealing the tent. Without it the walls float.
func _add_sod_cloth(mb: MeshBuilder) -> void:
	var noise := FastNoiseLite.new()
	noise.seed = 913
	noise.frequency = 2.0
	var segments := 12
	var reach := 0.17
	for side: float in [-1.0, 1.0]:
		var inner := PackedInt32Array()
		var outer := PackedInt32Array()
		for i in range(segments + 1):
			var u := float(i) / float(segments)
			var z := (u - 0.5) * LENGTH
			var ripple := noise.get_noise_2d(z * 3.0, side * 7.0)
			var x_in := side * (WIDTH * 0.5 - 0.02)
			var x_out := side * (WIDTH * 0.5 + reach + ripple * 0.03)
			var uv_in := Vector2(z / CANVAS_TILE, WIDTH * 0.62 / CANVAS_TILE)
			var uv_out := Vector2(z / CANVAS_TILE, (WIDTH * 0.62 + reach) / CANVAS_TILE)
			inner.append(mb.add_vertex(Vector3(x_in, 0.004, z), Vector3.UP, uv_in, Color(0.18, 0.0, 0.0, 1.0), Vector4(0.0, 0.0, 1.0, 1.0)))
			outer.append(mb.add_vertex(Vector3(x_out, 0.006 + absf(ripple) * 0.01, z), Vector3.UP, uv_out, Color(0.22, 0.0, 0.0, 1.0), Vector4(0.0, 0.0, 1.0, 1.0)))
		for i in segments:
			mb.add_quad_facing(inner[i], inner[i + 1], outer[i + 1], outer[i], Vector3.UP)
	# Along the back wall.
	var back_z := LENGTH * 0.5 - 0.02
	var b_in := PackedInt32Array()
	var b_out := PackedInt32Array()
	for i in range(segments + 1):
		var u := float(i) / float(segments)
		var x := (u - 0.5) * (WIDTH - 0.06)
		var ripple := noise.get_noise_2d(x * 3.0, 21.0)
		b_in.append(mb.add_vertex(Vector3(x, 0.004, back_z), Vector3.UP, Vector2(x / CANVAS_TILE, 1.8), Color(0.18, 0.0, 0.0, 1.0), Vector4(1.0, 0.0, 0.0, 1.0)))
		b_out.append(mb.add_vertex(Vector3(x, 0.006 + absf(ripple) * 0.01, back_z + reach + ripple * 0.03), Vector3.UP, Vector2(x / CANVAS_TILE, 1.8 + reach / CANVAS_TILE), Color(0.22, 0.0, 0.0, 1.0), Vector4(1.0, 0.0, 0.0, 1.0)))
	for i in segments:
		mb.add_quad_facing(b_in[i], b_in[i + 1], b_out[i + 1], b_out[i], Vector3.UP)


func _pole_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var strip := 0
	for end: float in [-1.0, 1.0]:
		var z := end * (LENGTH * 0.5 + 0.02)
		for side: float in [-1.0, 1.0]:
			var foot := Vector3(side * (WIDTH * 0.5 + 0.12), -0.08, z + end * 0.06)
			var top := Vector3(-side * 0.11, HEIGHT + 0.16, z)
			PropMeshes.add_timber(mb, [foot, foot.lerp(top, 0.5), top], [POLE_RADIUS, POLE_RADIUS * 0.95, POLE_RADIUS * 0.85], 7, strip, Color.WHITE, 1.4)
			strip += 1
	var ridge_a := Vector3(0.0, HEIGHT + 0.02, -LENGTH * 0.5 - 0.25)
	var ridge_b := Vector3(0.0, HEIGHT + 0.02, LENGTH * 0.5 + 0.25)
	PropMeshes.add_timber(mb, [ridge_a, ridge_a.lerp(ridge_b, 0.5), ridge_b], [0.03, 0.032, 0.03], 7, 2, Color.WHITE, 1.4)
	return mb.commit(null, true)


func _line_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	for end: float in [-1.0, 1.0]:
		var z := end * (LENGTH * 0.5 + 0.02)
		# Lashing at each crossing.
		PropMeshes.add_rope_coil(mb, Vector3(0.0, HEIGHT - 0.01, z), 0.05, 4, 0.022)
		# Guy line from the crossing out to a stake.
		var from := Vector3(0.0, HEIGHT + 0.04, z + end * 0.1)
		var to := Vector3(end * 0.25, 0.05, z + end * 1.35)
		PropMeshes.add_rope(mb, from, to, 0.03, 0.009, 6)
	# Side lines holding the peg edges taut.
	for side: float in [-1.0, 1.0]:
		for t: float in [0.2, 0.8]:
			var z := (t - 0.5) * LENGTH
			PropMeshes.add_rope(mb, Vector3(side * WIDTH * 0.5, 0.04, z), Vector3(side * (WIDTH * 0.5 + 0.35), 0.05, z), 0.02, 0.008, 4)
	return mb.commit()


func _peg_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var pegs: Array[Vector3] = [
		Vector3(-0.25, 0.0, -LENGTH * 0.5 - 1.35), Vector3(0.25, 0.0, LENGTH * 0.5 + 1.35),
	]
	for side: float in [-1.0, 1.0]:
		for t: float in [0.2, 0.8]:
			pegs.append(Vector3(side * (WIDTH * 0.5 + 0.35), 0.0, (t - 0.5) * LENGTH))
	for p in pegs:
		var lean := Vector3(-p.x, 0.0, -p.z).normalized() * 0.08
		PropMeshes.add_timber(mb, [p + Vector3(0.0, -0.12, 0.0), p + Vector3(0.0, 0.16, 0.0) + lean], [0.02, 0.018], 5, 1, Color(0.9, 0.85, 0.78), 0.8)
	return mb.commit()


func _add_furniture() -> void:
	# Ground sheet: laid straight on the levelled pad, wrinkled, edges curled.
	var sheet := MeshInstance3D.new()
	sheet.name = "GroundSheet"
	sheet.mesh = PropMeshes.cloth_sheet_mesh(Vector2(WIDTH * 0.86, LENGTH * 0.9), 4471)
	sheet.material_override = PropMaterials.triplanar("canvas", Color(0.3, 0.28, 0.24), 0.9, 0.0)
	sheet.position = Vector3(0.0, 0.002, 0.05)
	sheet.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	add_child(sheet)

	var bedroll := Node3D.new()
	bedroll.name = "Bedroll"
	bedroll.position = Vector3(-0.42, 0.018, 0.2)
	add_child(bedroll)
	var cloth := PropMaterials.triplanar("canvas", Color(0.35, 0.42, 0.3), 3.0)
	FieldKit.add(bedroll, FieldKit.rounded_box(Vector3(0.54, 0.11, 1.88), 0.045), cloth, Vector3(0, 0.055, 0), "SleepingPad")
	var quilting := MeshBuilder.new()
	for row in 13:
		var z := -0.83 + row * 0.135
		var points: Array[Vector3] = []
		var radii: Array[float] = []
		for i in 17:
			var t := float(i) / 16.0
			points.append(Vector3((t - 0.5) * 0.48, 0.11 + sin(t * PI) * 0.0015, z))
			radii.append(0.0008)
		quilting.add_tube(points, radii, 4)
	FieldKit.add(bedroll, quilting.commit(), FieldKit.solid(Color(0.16, 0.22, 0.12), 0.96), Vector3.ZERO, "QuiltStitching")
	FieldKit.add(bedroll, FieldKit.rounded_box(Vector3(0.40, 0.075, 0.28), 0.034), PropMaterials.triplanar("canvas", Color(0.61, 0.57, 0.43), 3.0), Vector3(0, 0.1475, 0.70), "Pillow")

	# Rounded waxed canvas with a front pocket, straps, buckles and a roll.
	var pack := FieldKit.pack(self, Vector3(0.28, 0.014, LENGTH * 0.5 - 0.57))
	pack.rotation.y = -0.32


func _add_lantern() -> void:
	lantern = CampLantern.new()
	lantern.name = "TentLantern"
	lantern.attracts_moths = false
	lantern.position = Vector3(0.0, 0.0, -LENGTH * 0.5 + 0.45)
	add_child(lantern)
	# A rope from the ridge pole holds the lantern at head height.
	var ridge := Vector3(0.0, HEIGHT - 0.01, 0.0)
	var hang := Vector3(0.0, HEIGHT - 0.22, 0.0)
	lantern.build(false, Quality.current.lantern_shadows, false, hang)
	var mb := MeshBuilder.new()
	PropMeshes.add_hang_link(mb, ridge, hang, 0.007)
	var rope := MeshInstance3D.new()
	rope.mesh = mb.commit()
	rope.material_override = _rope
	rope.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	lantern.add_child(rope)


func _add_collision() -> void:
	var body := StaticBody3D.new()
	body.collision_layer = 1
	body.collision_mask = 0
	body.set_meta("surface", &"canvas")
	for side: float in [-1.0, 1.0]:
		var shape := CollisionShape3D.new()
		var box := BoxShape3D.new()
		var slope := Vector2(WIDTH * 0.5, HEIGHT)
		box.size = Vector3(0.06, slope.length(), LENGTH)
		shape.shape = box
		shape.position = Vector3(side * WIDTH * 0.25, HEIGHT * 0.5, 0.0)
		shape.rotation.z = -side * atan2(slope.x, slope.y)
		body.add_child(shape)
	add_child(body)
