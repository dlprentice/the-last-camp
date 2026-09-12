class_name Dock
extends Node3D

## A small timber jetty from the beach out over the pond: driven piles with
## rope-wrapped heads, bearers and stringers, a deck of individually varied
## planks, an iron cleat with a mooring line to the canoe, and the lantern at
## the end. Local frame: the node sits at the deck's inland end with -Z along
## the dock, +X to the right and the deck's top face at y = 0.

const LENGTH := 8.0
const INLAND := 0.9
const WIDTH := 1.6
const DECK_ABOVE_WATER := 0.42
const PILE_SPACING := 2.1
const PILE_RADIUS := 0.085
const PILE_ABOVE_DECK := 0.62
const PLANK_THICKNESS := 0.042
const STRINGER_SIZE := Vector2(0.11, 0.17)
const BEARER_SIZE := 0.12

var field: TerrainField
var lantern: CampLantern
var canoe: Canoe
var deck_y := 0.0
var _piles: Array[Vector3] = []
var _rng := RandomNumberGenerator.new()
var _rope_clock := 0.0


func _init(p_field: TerrainField) -> void:
	field = p_field
	name = "Dock"
	_rng.seed = 2024


func total_length() -> float:
	return LENGTH + INLAND


func build() -> void:
	var start := TerrainField.DOCK_START
	var dir2 := (TerrainField.POND_CENTRE - start).normalized()
	var forward := Vector3(dir2.x, 0.0, dir2.y)
	deck_y = TerrainField.WATER_LEVEL + DECK_ABOVE_WATER
	position = Vector3(start.x, deck_y, start.y) - forward * INLAND
	basis = Basis.looking_at(forward, Vector3.UP)

	var wood := PropMaterials.wood(Color(0.86, 0.74, 0.56), 0.55, 0.7, 1.0)
	var pile_wood := PropMaterials.wood(Color(0.62, 0.5, 0.36), 0.4, 0.85, 1.0)
	_add_mesh("Deck", _deck_mesh(), wood)
	_add_mesh("Piles", _pile_mesh(), pile_wood)
	_add_mesh("Rope", _rope_mesh(), PropMaterials.rope())
	_add_mesh("Cleat", _cleat_mesh(), PropMaterials.iron())
	_add_collision()
	_add_lantern()
	_moor_canoe()
	_add_mesh("Mooring", _mooring_mesh(), PropMaterials.rope())
	_add_skipping_stones()


func _add_skipping_stones() -> void:
	var stones := Interactable.new()
	stones.name = "SkippingStones"
	stones.position = Vector3(-0.38, 0.06, -total_length() + 1.15)
	stones.collision_layer = 1 << 1
	stones.collision_mask = 0
	stones.prompt_text = "Skip a stone across the pond"
	stones.on_interact = func(_player: Node) -> void:
		if Game.camp != null and Game.camp.pond != null:
			var origin := to_global(Vector3(0.0, 0.9, -total_length() - 0.2))
			Game.camp.pond.skip_stone(origin, -global_basis.z)
	var collider := CollisionShape3D.new()
	var shape := SphereShape3D.new()
	shape.radius = 0.28
	collider.shape = shape
	stones.add_child(collider)
	var stone_material := PropMaterials.triplanar("rock", Color(0.74, 0.76, 0.72), 3.0)
	stone_material.set_shader_parameter("normal_strength", 0.30)
	stone_material.set_shader_parameter("wet_band", 0.08)
	var deck_surface := (get_node("Deck") as MeshInstance3D).mesh.generate_triangle_mesh()
	# A handful set down loosely, with room between the stones. Their contact
	# follows the individual tilted/raised planks, not the ideal deck height.
	var placements := [Vector3(-0.072, 0, 0.090), Vector3(0.055, 0, 0.139),
		Vector3(0.103, 0, -0.003), Vector3(-0.060, 0, -0.066), Vector3(0.049, 0, -0.150)]
	var radii := [0.042, 0.055, 0.050, 0.045, 0.039]
	var angles := [0.4, 2.1, -0.8, 1.1, -2.3]
	for i in 5:
		var pebble := MeshInstance3D.new()
		pebble.name = "Stone%d" % (i + 1)
		pebble.mesh = PropMeshes.rock(610 + i, radii[i])
		pebble.position = placements[i]
		var centre: Vector3 = stones.position + pebble.position
		var contact := deck_surface.intersect_ray(Vector3(centre.x, 0.25, centre.z), Vector3.DOWN)
		var normal: Vector3 = contact.get("normal", Vector3.UP)
		if normal.y < 0.0:
			normal = -normal
		pebble.basis = Basis(Quaternion(Vector3.UP, normal)) * Basis(Vector3.UP, angles[i]) \
			* Basis.from_scale(Vector3(1.0, 0.31 + 0.02 * i, 0.87))
		var support_y := -INF
		for vertex: Vector3 in pebble.mesh.surface_get_arrays(0)[Mesh.ARRAY_VERTEX]:
			var relative: Vector3 = pebble.basis * vertex
			var sample := centre + relative
			var hit := deck_surface.intersect_ray(Vector3(sample.x, 0.25, sample.z), Vector3.DOWN)
			if not hit.is_empty():
				support_y = maxf(support_y, hit.position.y - relative.y)
		assert(is_finite(support_y), "Skipping stones must have a deck beneath them")
		pebble.position.y = support_y - stones.position.y + 0.00015
		pebble.material_override = stone_material
		stones.add_child(pebble)
	add_child(stones)


func _add_mesh(node_name: String, mesh: ArrayMesh, material: Material) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	mi.name = node_name
	mi.mesh = mesh
	mi.material_override = material
	mi.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	mi.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	add_child(mi)
	return mi


## Pile stations along the dock (local z, negative outwards).
func _pile_stations() -> PackedFloat32Array:
	var stations := PackedFloat32Array()
	var z := -0.45
	while z > -total_length() + 0.3:
		stations.append(z)
		z -= PILE_SPACING
	stations.append(-total_length() + 0.3)
	return stations


func _ground_local(local: Vector3) -> float:
	var world := to_global(local)
	return field.height_fast(world.x, world.z) - deck_y


func _deck_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var length := total_length()
	# Bearers across the piles, stringers along them, planks on top.
	var bearer_y := -PLANK_THICKNESS - STRINGER_SIZE.y - BEARER_SIZE * 0.5
	for z in _pile_stations():
		var xf := Transform3D(Basis(Vector3.UP, PI * 0.5), Vector3(0.0, bearer_y, z))
		PropMeshes.add_board(mb, xf, Vector3(BEARER_SIZE, BEARER_SIZE, WIDTH + 0.16), _rng.randi() % 4, _rng.randf(), Color(0.8, 0.74, 0.66))
	for side: float in [-1.0, 1.0]:
		var x := side * (WIDTH * 0.5 - 0.22)
		var xf := Transform3D(Basis(), Vector3(x, -PLANK_THICKNESS - STRINGER_SIZE.y * 0.5, -length * 0.5))
		PropMeshes.add_board(mb, xf, Vector3(STRINGER_SIZE.x, STRINGER_SIZE.y, length), _rng.randi() % 4, _rng.randf(), Color(0.82, 0.76, 0.68))
	var z := 0.0
	while z > -length:
		var w := _rng.randf_range(0.14, 0.19)
		var gap := _rng.randf_range(0.012, 0.03)
		var centre_z := z - w * 0.5
		if centre_z - w * 0.5 < -length:
			break
		var plank_len := WIDTH + _rng.randf_range(-0.03, 0.07)
		var offset := Vector3(_rng.randf_range(-0.02, 0.02), 0.0, centre_z)
		var yaw := deg_to_rad(_rng.randf_range(-1.0, 1.0))
		var tilt := deg_to_rad(_rng.randf_range(-0.6, 0.6))
		# Now and then a board has worked loose and sits a little proud.
		var raised := 0.01 if _rng.randf() < 0.08 else 0.0
		# Boards weather unevenly: some silvered, some still brown.
		var grey := _rng.randf()
		var shade := _rng.randf_range(0.78, 1.05)
		var tint := Color(shade, shade * lerpf(0.95, 1.0, grey), shade * lerpf(0.87, 1.0, grey))
		var rot := Basis(Vector3.UP, PI * 0.5 + yaw).rotated(Vector3.FORWARD, tilt)
		var xf := Transform3D(rot, offset + Vector3(0.0, raised - PLANK_THICKNESS * 0.5, 0.0))
		PropMeshes.add_board(mb, xf, Vector3(w, PLANK_THICKNESS, plank_len), _rng.randi() % 4, _rng.randf(), tint)
		# Two nails over each stringer, heads just proud of the surface.
		for side: float in [-1.0, 1.0]:
			var x := side * (WIDTH * 0.5 - 0.22)
			for k: float in [-1.0, 1.0]:
				var head := Vector3(x, raised + 0.0025, centre_z + k * w * 0.25)
				mb.add_tube([head + Vector3(0.0, -0.006, 0.0), head], [0.0045, 0.0055], 6, Color(0.22, 0.2, 0.18), 1.0, 1.0, 0.0, true)
		z -= w + gap
	return mb.commit(null, true)


func _pile_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	_piles.clear()
	for z in _pile_stations():
		for side: float in [-1.0, 1.0]:
			var x := side * (WIDTH * 0.5 - 0.02)
			var foot := Vector3(x, 0.0, z)
			var ground := _ground_local(foot)
			var bottom := Vector3(x, ground - 0.5, z)
			var top := Vector3(x + _rng.randf_range(-0.03, 0.03), PILE_ABOVE_DECK + _rng.randf_range(-0.08, 0.06), z + _rng.randf_range(-0.03, 0.03))
			var r := PILE_RADIUS * _rng.randf_range(0.9, 1.1)
			PropMeshes.add_timber(mb, [bottom, bottom.lerp(top, 0.5), top], [r * 1.05, r, r * 0.92], 9, _rng.randi() % 4, Color(0.96, 0.94, 0.9), 1.4)
			_piles.append(top)
	return mb.commit(null, true)


func _rope_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	# Rope wraps on a few pile heads.
	for i in _piles.size():
		if i % 3 != 0 and i != _piles.size() - 1:
			continue
		var top := _piles[i]
		PropMeshes.add_rope_coil(mb, top - Vector3(0.0, 0.2, 0.0), PILE_RADIUS, 4, 0.026)
	return mb.commit()


func _cleat_position() -> Vector3:
	return Vector3(WIDTH * 0.5 - 0.14, 0.0, -total_length() + 0.55)


func _cleat_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var p := _cleat_position()
	mb.add_box(Vector3(0.05, 0.06, 0.08), Color.WHITE, 2.0)
	# add_box is centred on the origin; move it into place by offsetting vertices.
	for i in mb.vertices.size():
		mb.vertices[i] += p + Vector3(0.0, 0.03, 0.0)
	var first := mb.vertex_count()
	mb.add_box(Vector3(0.2, 0.035, 0.05), Color.WHITE, 2.0)
	for i in range(first, mb.vertices.size()):
		mb.vertices[i] += p + Vector3(0.0, 0.075, 0.0)
	return mb.commit()


func _add_collision() -> void:
	var body := StaticBody3D.new()
	body.name = "DeckBody"
	body.collision_layer = 1
	body.collision_mask = 0
	body.set_meta("surface", &"wood")
	var deck := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(WIDTH + 0.08, 0.3, total_length())
	deck.shape = box
	deck.position = Vector3(0.0, -0.15, -total_length() * 0.5)
	body.add_child(deck)
	for top in _piles:
		var post := CollisionShape3D.new()
		var cyl := CylinderShape3D.new()
		cyl.radius = PILE_RADIUS + 0.03
		cyl.height = PILE_ABOVE_DECK
		post.shape = cyl
		post.position = Vector3(top.x, PILE_ABOVE_DECK * 0.5, top.z)
		body.add_child(post)
	add_child(body)


func _add_lantern() -> void:
	lantern = CampLantern.new()
	var top := _piles[_piles.size() - 1]
	lantern.position = top + Vector3(0.0, 0.0, 0.0)
	lantern.rotation.y = PI * 0.5
	add_child(lantern)
	lantern.build(false, Quality.current.lantern_shadows, true)


func _moor_canoe() -> void:
	canoe = Canoe.new()
	# Moored alongside, parallel to the deck, clear of the piles: half the
	# deck width plus half the beam plus fender room.
	var end_z := -total_length()
	var local := Vector3(WIDTH * 0.5 + Canoe.HALF_BEAM + 0.55, 0.0, end_z + 2.6)
	var world := to_global(local)
	world.y = TerrainField.WATER_LEVEL - Canoe.DRAFT
	canoe.position = world
	canoe.rotation.y = rotation.y + deg_to_rad(4.0)
	get_parent().add_child(canoe)
	canoe.build(Color(1.0, 0.7, 0.44))


func _mooring_mesh() -> ArrayMesh:
	var mb := MeshBuilder.new()
	var from := _cleat_position() + Vector3(0.0, 0.09, 0.0)
	var to := to_local(canoe.bow_point())
	PropMeshes.add_rope(mb, from, to, 0.22, 0.011, 14)
	return mb.commit()


func _process(delta: float) -> void:
	# A small rope mesh follows the physically moving bow; 12 Hz is ample
	# for this slow tether and avoids reallocating it every rendered frame.
	_rope_clock += delta
	if _rope_clock < 1.0 / 12.0 or canoe == null or canoe.freeze:
		return
	_rope_clock = fmod(_rope_clock, 1.0 / 12.0)
	get_node("Mooring").mesh = _mooring_mesh()
