class_name FieldKit
extends RefCounted

## Small camp objects share real dimensions and rounded edges. All geometry
## is built here, so the table, sleeping kit and tent remain editable in metres.

static func rounded_box(size: Vector3, bevel: float) -> ArrayMesh:
	var mb := MeshBuilder.new()
	var half := size * 0.5
	var radius := minf(bevel, minf(half.x, minf(half.y, half.z)) * 0.98)
	var core := half - Vector3.ONE * radius
	for axis in 3:
		for sign_value: float in [-1.0, 1.0]:
			var normal := Vector3.ZERO
			normal[axis] = sign_value
			var u_axis := (axis + 1) % 3
			var v_axis := (axis + 2) % 3
			var us := [-half[u_axis], -half[u_axis] + radius * 0.5, -core[u_axis], core[u_axis], half[u_axis] - radius * 0.5, half[u_axis]]
			var vs := [-half[v_axis], -half[v_axis] + radius * 0.5, -core[v_axis], core[v_axis], half[v_axis] - radius * 0.5, half[v_axis]]
			var rows: Array[PackedInt32Array] = []
			for j in vs.size():
				var row := PackedInt32Array()
				for i in us.size():
					var p := normal * half[axis]
					p[u_axis] = us[i]
					p[v_axis] = vs[j]
					var nearest := p.clamp(-core, core)
					var n := (p - nearest).normalized()
					row.append(mb.add_vertex(nearest + n * radius, n, Vector2(p[u_axis], p[v_axis]), Color.WHITE, MeshBuilder.tangent_for(n, Vector3.RIGHT if absf(n.x) < 0.9 else Vector3.FORWARD, Vector3.UP)))
				rows.append(row)
			for j in range(vs.size() - 1):
				for i in range(us.size() - 1):
					mb.add_quad_facing(rows[j][i], rows[j][i + 1], rows[j + 1][i + 1], rows[j + 1][i], normal)
	return mb.commit()


static func lathe(profile: Array[Vector2], sides := 48) -> ArrayMesh:
	var mb := MeshBuilder.new()
	var rows: Array[PackedInt32Array] = []
	for j in profile.size():
		var row := PackedInt32Array()
		var tangent := profile[mini(j + 1, profile.size() - 1)] - profile[maxi(j - 1, 0)]
		for i in range(sides + 1):
			var a := float(i) / float(sides) * TAU
			var n := Vector3(cos(a) * tangent.y, -tangent.x, sin(a) * tangent.y).normalized()
			row.append(mb.add_vertex(Vector3(cos(a) * profile[j].x, profile[j].y, sin(a) * profile[j].x), n, Vector2(float(i) / sides, profile[j].y), Color.WHITE, Vector4(-sin(a), 0, cos(a), 1)))
		rows.append(row)
	for j in range(rows.size() - 1):
		for i in sides:
			mb.add_quad_indices(rows[j][i], rows[j + 1][i], rows[j + 1][i + 1], rows[j][i + 1])
	return mb.commit()


static func enamel(tint: Color) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/enamel.gdshader")
	mat.set_shader_parameter("chip_amount", 0.32)
	mat.set_shader_parameter("age", 0.45)
	mat.set_shader_parameter("tint", tint)
	return mat


static func solid(tint: Color, roughness := 0.6, metal := 0.0) -> StandardMaterial3D:
	var mat := StandardMaterial3D.new()
	mat.albedo_color = tint
	mat.roughness = roughness
	mat.metallic = metal
	return mat


static func add(parent: Node3D, mesh: ArrayMesh, mat: Material, at := Vector3.ZERO, node_name := "Detail") -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	mi.name = node_name
	mi.mesh = mesh
	mi.material_override = mat
	mi.position = at
	parent.add_child(mi, true)
	return mi


static func mug(parent: Node3D, at: Vector3, tint: Color) -> Node3D:
	var root := Node3D.new()
	root.name = "EnamelMug"
	root.position = at
	parent.add_child(root, true)
	var mat := enamel(tint)
	add(root, lathe([Vector2(0, 0.003), Vector2(0.045, 0.003), Vector2(0.051, 0.008), Vector2(0.054, 0.109), Vector2(0.057, 0.114), Vector2(0.055, 0.119), Vector2(0.050, 0.119), Vector2(0.048, 0.109), Vector2(0.045, 0.017), Vector2(0, 0.017)]), mat)
	var handle := MeshBuilder.new()
	var points: Array[Vector3] = []
	var radii: Array[float] = []
	for i in 17:
		var a := float(i) / 16.0 * PI
		points.append(Vector3(0.050 + sin(a) * 0.038, 0.064 + cos(a) * 0.041, 0))
		radii.append(0.006)
	handle.add_tube(points, radii, 8)
	add(root, handle.commit(), mat)
	add(root, lathe([Vector2(0.049, 0.097), Vector2(0, 0.097)]), solid(Color(0.065, 0.035, 0.015), 0.2), Vector3.ZERO, "Tea")
	return root


static func kettle(parent: Node3D, at: Vector3) -> void:
	var root := Node3D.new()
	root.name = "CoffeePot"
	root.position = at
	parent.add_child(root, true)
	var enamel_mat := enamel(Color(0.18, 0.28, 0.28))
	add(root, lathe([Vector2(0, 0.004), Vector2(0.105, 0.004), Vector2(0.128, 0.025), Vector2(0.13, 0.08), Vector2(0.098, 0.20), Vector2(0.078, 0.23), Vector2(0.079, 0.239), Vector2(0.01, 0.25), Vector2(0, 0.25)]), enamel_mat)
	var spout := MeshBuilder.new()
	var spout_points: Array[Vector3] = []
	var spout_radii: Array[float] = []
	var start := Vector3(-0.095, 0.071, 0)
	var control_a := Vector3(-0.205, 0.08, 0)
	var control_b := Vector3(-0.145, 0.205, 0)
	var end := Vector3(-0.225, 0.232, 0)
	for i in 25:
		var t := float(i) / 24.0
		spout_points.append(start.bezier_interpolate(control_a, control_b, end, t))
		spout_radii.append(lerpf(0.035, 0.018, smoothstep(0.0, 1.0, t)))
	spout.add_tube(spout_points, spout_radii, 24)
	add(root, spout.commit(), enamel_mat, Vector3.ZERO, "CurvedSpout")
	# Rolled metal lip and a short inner wall keep the opening hollow from
	# above and from either side, instead of exposing a single-sided tube.
	var lip := add(root, lathe([Vector2(0.0155, -0.028), Vector2(0.0155, 0), Vector2(0.017, 0.001), Vector2(0.018, 0), Vector2(0.018, -0.003)], 32), enamel_mat, end, "SpoutLip")
	lip.quaternion = Quaternion(Vector3.UP, (end - control_b).normalized())
	var interior := add(root, lathe([Vector2(0.0156, 0), Vector2(0, 0)], 32), solid(Color(0.025, 0.03, 0.027), 0.85), end - (end - control_b).normalized() * 0.026, "SpoutInterior")
	interior.quaternion = lip.quaternion
	var steel := solid(Color(0.14, 0.145, 0.14), 0.3, 0.85)
	var arc := MeshBuilder.new()
	var points: Array[Vector3] = []
	var radii: Array[float] = []
	for i in 25:
		var a := float(i) / 24.0 * PI
		points.append(Vector3(0, 0.16 + sin(a) * 0.23, cos(a) * 0.11))
		radii.append(0.005)
	arc.add_tube(points, radii, 8)
	add(root, arc.commit(), steel)
	add(root, lathe([Vector2(0, 0), Vector2(0.021, 0), Vector2(0.021, 0.014), Vector2(0.014, 0.019), Vector2(0, 0.019)], 24), steel, Vector3(0, 0.25, 0), "LidKnob")


static func pack(parent: Node3D, at: Vector3) -> Node3D:
	var root := Node3D.new()
	root.name = "WaxedCanvasPack"
	root.position = at
	parent.add_child(root, true)
	var cloth := PropMaterials.triplanar("canvas", Color(0.35, 0.37, 0.22), 2.8)
	var leather := solid(Color(0.19, 0.105, 0.045), 0.82)
	var brass := solid(Color(0.42, 0.29, 0.12), 0.35, 0.8)
	add(root, rounded_box(Vector3(0.39, 0.48, 0.25), 0.07), cloth, Vector3(0, 0.242, 0), "SoftPackBody")
	add(root, rounded_box(Vector3(0.4, 0.095, 0.29), 0.04), cloth, Vector3(0, 0.46, -0.013), "TopFlap")
	add(root, rounded_box(Vector3(0.25, 0.22, 0.07), 0.033), cloth, Vector3(0, 0.18, -0.141), "FrontPocket")
	for x: float in [-0.12, 0.12]:
		add(root, rounded_box(Vector3(0.026, 0.29, 0.012), 0.004), leather, Vector3(x, 0.30, -0.153), "LeatherStrap")
		var buckle := MeshBuilder.new()
		buckle.add_tube([Vector3(-0.018, -0.015, 0), Vector3(0.018, -0.015, 0), Vector3(0.018, 0.015, 0), Vector3(-0.018, 0.015, 0), Vector3(-0.018, -0.015, 0)], [0.0025, 0.0025, 0.0025, 0.0025, 0.0025], 6)
		add(root, buckle.commit(), brass, Vector3(x, 0.3, -0.163), "Buckle")
	for x: float in [-0.095, 0.095]:
		var strap := MeshBuilder.new()
		var points: Array[Vector3] = []
		var radii: Array[float] = []
		for i in 25:
			var t := float(i) / 24.0
			points.append(Vector3(x, lerpf(0.43, 0.09, t), 0.125 + sin(t * PI) * 0.065))
			radii.append(0.012)
		strap.add_tube(points, radii, 8)
		for i in strap.vertex_count():
			var row := i / 9
			var offset := strap.vertices[i] - points[row]
			strap.vertices[i] = points[row] + offset * Vector3(1.15, 0.4, 0.4)
			strap.normals[i] = (strap.normals[i] / Vector3(1.15, 0.4, 0.4)).normalized()
		add(root, strap.commit(), leather, Vector3.ZERO, "ShoulderStrap")
	blanket(root, Vector3(0, 0.60, 0.015), 0.5)
	return root


static func blanket(parent: Node3D, at: Vector3, length := 0.56) -> void:
	var root := Node3D.new()
	root.name = "RolledWoolBlanket"
	root.position = at
	parent.add_child(root, true)
	var cloth := PropMaterials.triplanar("canvas", Color(0.28, 0.36, 0.40), 5.0)
	var leather := solid(Color(0.16, 0.09, 0.044), 0.8)
	var profile: Array[Vector2] = [Vector2(0, -length * 0.5), Vector2(0.085, -length * 0.5)]
	for i in 25:
		var u := float(i) / 24.0
		var x := (u - 0.5) * (length - 0.016)
		var squeeze := exp(-pow((absf(x) - length * 0.3) / 0.025, 2.0)) * 0.003
		profile.append(Vector2(0.092 + sin(u * PI) * 0.003 - squeeze, x))
	profile.append(Vector2(0.085, length * 0.5))
	profile.append(Vector2(0, length * 0.5))
	var roll := add(root, lathe(profile, 64), cloth, Vector3.ZERO, "WoolRoll")
	roll.rotation.z = -PI * 0.5
	# Compressed fabric layers read as a fine spiral seam on each soft end.
	var seam_mat := solid(Color(0.115, 0.17, 0.19), 0.97)
	for side: float in [-1.0, 1.0]:
		var hem := MeshBuilder.new()
		var points: Array[Vector3] = []
		var radii: Array[float] = []
		for i in 513:
			var t := float(i) / 512.0
			var a := t * TAU * 8.0
			var r := lerpf(0.006, 0.084, t)
			points.append(Vector3(side * (length * 0.5 + 0.0002), cos(a) * r, sin(a) * r))
			radii.append(0.00065)
		hem.add_tube(points, radii, 4)
		add(root, hem.commit(), seam_mat, Vector3.ZERO, "WovenHem")
	for x: float in [-length * 0.30, length * 0.30]:
		var belt := add(root, lathe([Vector2(0.092, -0.011), Vector2(0.094, -0.011), Vector2(0.094, 0.011), Vector2(0.092, 0.011), Vector2(0.092, -0.011)], 64), leather, Vector3(x, 0, 0), "BlanketBelt")
		belt.rotation.z = -PI * 0.5
