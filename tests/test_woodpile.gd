extends TestCase


func test_split_firewood_is_closed_at_bark_cleft_and_ends() -> void:
	for seed_value in [821, 843, 871]:
		var faces := PropMeshes.split_log_mesh(0.48, 0.096, seed_value).get_faces()
		var welded: Dictionary = {}
		var ids := PackedInt32Array()
		for point in faces:
			var key := Vector3i(roundi(point.x * 1000000.0), roundi(point.y * 1000000.0), roundi(point.z * 1000000.0))
			if not welded.has(key):
				welded[key] = welded.size()
			ids.append(welded[key])
		var edges: Dictionary = {}
		for i in range(0, faces.size(), 3):
			assert_gt((faces[i + 1] - faces[i]).cross(faces[i + 2] - faces[i]).length_squared(), 1e-16, "split piece has no collapsed triangles")
			for j in 3:
				var a := ids[i + j]
				var b := ids[i + (j + 1) % 3]
				var edge := Vector2i(mini(a, b), maxi(a, b))
				edges[edge] = int(edges.get(edge, 0)) + 1
		for edge: Vector2i in edges:
			assert_eq(edges[edge], 2, "bark, split faces and end caps meet without openings")


func test_rotated_firewood_contacts_the_ground_without_burial() -> void:
	var mesh := PropMeshes.split_log_mesh(0.47, 0.104, 829)
	var ground := func(at: Vector2) -> float: return 0.5 + at.x * 0.12 - at.y * 0.07
	for roll: float in [0.0, 0.66, PI]:
		var basis := Basis(Vector3.UP, 0.4) * Basis(Vector3.RIGHT, roll)
		var pose := Campsite._settle_firewood(mesh, Transform3D(basis, Vector3(0.2, 4.0, -0.1)), [], ground)
		var contact := INF
		for vertex in mesh.get_faces():
			var point := pose * vertex
			contact = minf(contact, point.y - float(ground.call(Vector2(point.x, point.z))))
		assert_near(contact, -0.0015, 0.00001, "rotated uneven log rests on the real ground plane")


func test_stack_contacts_the_overlapping_slope_not_its_highest_corner() -> void:
	var support := MeshBuilder.new()
	# Known plane y = 0.4 + 0.2x. Its high edge at x=1 is outside the
	# upper piece's footprint, so a bounding-box contact gives a false gap.
	support.add_quad(Vector3(-1.0, 0.2, -1.0), Vector3(1.0, 0.6, -1.0), Vector3(1.0, 0.6, 1.0), Vector3(-1.0, 0.2, 1.0))
	var supports := Campsite._firewood_faces(support.commit(), Transform3D.IDENTITY)
	var upper := BoxMesh.new()
	upper.size = Vector3(0.2, 0.1, 0.3)
	var ground := func(_at: Vector2) -> float: return -2.0
	for x: float in [0.0, -0.6]:
		var pose := Campsite._settle_firewood(upper, Transform3D(Basis.IDENTITY, Vector3(x, 3.0, 0.0)), supports, ground)
		var expected_contact := 0.4 + 0.2 * (x + 0.1)
		assert_near(pose.origin.y - 0.05, expected_contact - 0.0015, 0.00001, "upper piece touches the lower surface inside its own footprint")
