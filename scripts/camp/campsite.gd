class_name Campsite
extends Node3D

## Hand-authored camp props: tent, dock, lanterns, seats, woodpile. The firepit
## is owned here so the rest of the clearing can sit relative to it.

var field: TerrainField
var firepit: Firepit
var lanterns: Array[CampLantern] = []
var woodpile: Interactable
var dock: Dock
var tent: Tent
var kitchen: CampKitchen

var _bark: ShaderMaterial
var _bark_dry: ShaderMaterial
var _end_grain: ShaderMaterial


func _init(p_field: TerrainField) -> void:
	field = p_field
	name = "Campsite"


func build() -> void:
	_bark = PropMaterials.bark("bark_oak", 0.08)
	_bark_dry = PropMaterials.bark("bark_oak", 0.02)
	_end_grain = PropMaterials.wood(Color(0.95, 0.85, 0.66), 0.15, 0.0, 1.0)
	firepit = Firepit.new(field)
	add_child(firepit)
	firepit.build()
	_build_seats()
	_build_woodpile()
	_build_tent()
	_build_dock()
	kitchen = CampKitchen.new(field)
	add_child(kitchen)
	kitchen.build()
	_place_lanterns()
	Quality.preset_changed.connect(_on_quality)
	_on_quality(Quality.current)


func _on_quality(p: QualityPreset) -> void:
	for lantern in lanterns:
		if lantern.light != null:
			lantern.light.shadow_enabled = p.lantern_shadows


# ------------------------------------------------------------------- seats

func _build_seats() -> void:
	for i in TerrainField.SEATS.size():
		var pos := TerrainField.SEATS[i]
		# The long local X axis follows the circle; local -Z faces the hearth.
		var yaw := atan2(pos.x - TerrainField.FIRE.x, pos.y - TerrainField.FIRE.y)
		var root := Node3D.new()
		root.name = "SplitLogBench_%d" % i
		root.position = Vector3(pos.x, field.height(pos.x, pos.y), pos.y)
		root.rotation.y = yaw
		add_child(root)
		var seat_y := 0.375
		# Each support starts just under the local earth and meets the actual
		# underside of the half-round seat, including on a sloping pad edge.
		for x: float in [-0.62, 0.62]:
			var at := root.to_global(Vector3(x, 0, 0))
			var foot_y := field.height(at.x, at.z) - root.global_position.y - 0.015
			var support_h := seat_y - 0.20 * 0.82 - foot_y
			var support := FieldKit.add(root, PropMeshes.stump_mesh(0.12, support_h, 807 + i), null, Vector3(x, foot_y, 0), "BenchFoot")
			support.mesh.surface_set_material(0, _bark)
			support.mesh.surface_set_material(1, _end_grain)
		var seat := FieldKit.add(root, PropMeshes.bark_log_mesh(1.85, 0.20, 800 + i, 0.0, 0.40), null, Vector3(0, seat_y, 0), "HewnSeat")
		seat.mesh.surface_set_material(0, _bark)
		seat.mesh.surface_set_material(1, _end_grain)
		seat.gi_mode = GeometryInstance3D.GI_MODE_STATIC
		_box_collider(root.position + Vector3(0, 0.23, 0), Vector3(1.85, 0.46, 0.40), yaw, &"wood")


# ---------------------------------------------------------------- woodpile

func _build_woodpile() -> void:
	var origin := TerrainField.WOODPILE
	var y := field.height(origin.x, origin.y)
	var root := Node3D.new()
	root.name = "Woodpile"
	root.position = Vector3(origin.x, y, origin.y)
	root.rotation.y = TerrainField.WOODPILE_YAW
	add_child(root)
	var rng := RandomNumberGenerator.new()
	rng.seed = 707
	var supports: Array[Dictionary] = []
	var ground := func(at: Vector2) -> float:
		var world := root.to_global(Vector3(at.x, 0.0, at.y))
		return field.surface_height(world.x, world.z) - root.global_position.y
	# Narrowing, staggered courses of hand-split pieces. Heights come from the
	# actual triangles below each piece, not a common radius or row spacing.
	for row in 4:
		var count := 4 - row
		for col in count:
			var log := MeshInstance3D.new()
			log.name = "StackedFirewood_%d_%d" % [row, col]
			var length := rng.randf_range(0.43, 0.54)
			log.mesh = PropMeshes.split_log_mesh(length, rng.randf_range(0.097, 0.108), 820 + row * 10 + col)
			log.mesh.surface_set_material(0, _bark_dry)
			log.mesh.surface_set_material(1, _end_grain)
			var x := (float(col) - float(count - 1) * 0.5) * 0.22 + rng.randf_range(-0.004, 0.004)
			var roll := PI + rng.randf_range(-0.14, 0.14)
			# The uppermost split shows pale fractured wood without laying a
			# flat timber shelf across an entire course.
			if row == 3:
				roll = rng.randf_range(-0.35, 0.35)
			var basis := Basis(Vector3.UP, PI * 0.5 + rng.randf_range(-0.03, 0.03)) * Basis(Vector3.RIGHT, roll)
			var pose := Transform3D(basis, Vector3(x, 0.0, rng.randf_range(-0.045, 0.045)))
			log.transform = _settle_firewood(log.mesh, pose, supports, ground)
			supports.append_array(_firewood_faces(log.mesh, log.transform))
			log.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
			root.add_child(log)
	for side: float in [-1.0, 1.0]:
		var stake := MeshInstance3D.new()
		var mb := MeshBuilder.new()
		PropMeshes.add_timber(mb, [Vector3(0.0, -0.15, 0.0), Vector3(0.0, 0.62, 0.0)], [0.028, 0.022], 6, 1, Color.WHITE, 1.2)
		stake.mesh = mb.commit(null, true)
		stake.material_override = PropMaterials.wood(Color(0.55, 0.45, 0.33), 0.5, 0.0, 1.0)
		stake.position = Vector3(side * 0.46, 0.0, 0.0)
		stake.rotation.z = -side * 0.06
		stake.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		root.add_child(stake)
	for i in 3:
		var log := MeshInstance3D.new()
		log.name = "LooseFirewood_%d" % i
		log.mesh = PropMeshes.split_log_mesh(rng.randf_range(0.4, 0.5), rng.randf_range(0.085, 0.098), 870 + i)
		log.mesh.surface_set_material(0, _bark_dry)
		log.mesh.surface_set_material(1, _end_grain)
		var basis := Basis(Vector3.UP, rng.randf() * TAU) * Basis(Vector3.RIGHT, PI + rng.randf_range(-0.3, 0.3))
		var pose := Transform3D(basis, Vector3(-0.37 + float(i) * 0.31, 0.0, 0.46 + rng.randf_range(0.0, 0.12)))
		log.transform = _settle_firewood(log.mesh, pose, supports, ground)
		supports.append_array(_firewood_faces(log.mesh, log.transform))
		log.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
		root.add_child(log)

	# Chopping block with the axe sunk into it.
	var stump := MeshInstance3D.new()
	stump.name = "ChoppingBlock"
	stump.mesh = PropMeshes.stump_mesh(0.2, 0.42, 880)
	stump.mesh.surface_set_material(0, _bark_dry)
	stump.mesh.surface_set_material(1, _end_grain)
	stump.position = Vector3(0.95, 0.0, 0.5)
	stump.rotation.y = 1.1
	stump.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	stump.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	root.add_child(stump)
	var axe := MeshInstance3D.new()
	axe.name = "Axe"
	axe.mesh = PropMeshes.axe_mesh()
	axe.mesh.surface_set_material(0, PropMaterials.wood(Color(0.9, 0.78, 0.6), 0.1, 0.0, 0.9))
	var forged := FieldKit.solid(Color(0.37, 0.39, 0.40), 0.42, 0.84)
	forged.vertex_color_use_as_albedo = true
	axe.mesh.surface_set_material(1, forged)
	axe.position = Vector3(0.95, 0.4, 0.5)
	axe.rotation = Vector3(deg_to_rad(-62.0), 0.8, 0.0)
	axe.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_ON
	root.add_child(axe)
	_box_collider(root.position + Vector3(0.95, 0.21, 0.5).rotated(Vector3.UP, root.rotation.y), Vector3(0.42, 0.42, 0.42), root.rotation.y, &"wood")

	woodpile = Interactable.new()
	woodpile.name = "WoodpileBody"
	woodpile.collision_layer = 1 | (1 << 1)
	woodpile.collision_mask = 0
	woodpile.set_meta("surface", &"wood")
	woodpile.prompt_text = "Take a log"
	woodpile.on_interact = _take_log
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(1.0, 0.55, 0.5)
	shape.shape = box
	shape.position.y = 0.28
	woodpile.add_child(shape)
	root.add_child(woodpile)


## Static prop assembly only: vertically lower a piece until its actual
## surface touches the terrain or another split piece. A 1.5 mm seating inset
## closes subpixel contact cracks without burying the wood's profile.
static func _settle_firewood(mesh: Mesh, pose: Transform3D, supports: Array[Dictionary], ground: Callable) -> Transform3D:
	var lift := -INF
	var vertices := mesh.get_faces()
	for i in range(0, vertices.size(), 3):
		var a := pose * vertices[i]
		var b := pose * vertices[i + 1]
		var c := pose * vertices[i + 2]
		for point: Vector3 in [a, b, c, (a + b) * 0.5, (b + c) * 0.5, (c + a) * 0.5, (a + b + c) / 3.0]:
			lift = maxf(lift, float(ground.call(Vector2(point.x, point.z))) - point.y)
	for upper: Dictionary in _firewood_faces(mesh, pose):
		var upper_bounds: Rect2 = upper.bounds
		var upper_polygon: PackedVector2Array = upper.polygon
		var upper_height: Vector3 = upper.height
		for lower: Dictionary in supports:
			if not upper_bounds.intersects(lower.bounds, true):
				continue
			var lower_polygon: PackedVector2Array = lower.polygon
			var difference: Vector3 = lower.height - upper_height
			# The difference of two triangle planes is affine. Its maximum is
			# at a vertex of their projected intersection, including edge meets.
			for point in upper_polygon:
				if Geometry2D.is_point_in_polygon(point, lower_polygon):
					lift = maxf(lift, difference.dot(Vector3(point.x, point.y, 1.0)))
			for point in lower_polygon:
				if Geometry2D.is_point_in_polygon(point, upper_polygon):
					lift = maxf(lift, difference.dot(Vector3(point.x, point.y, 1.0)))
			for first in 3:
				for second in 3:
					var crossing: Variant = Geometry2D.segment_intersects_segment(upper_polygon[first], upper_polygon[(first + 1) % 3], lower_polygon[second], lower_polygon[(second + 1) % 3])
					if crossing is Vector2:
						lift = maxf(lift, difference.dot(Vector3(crossing.x, crossing.y, 1.0)))
	pose.origin.y += lift - 0.0015
	return pose


## Project triangle surfaces into the pile's XZ plane, keeping the plane
## equation so tilted split faces determine the contact height correctly.
static func _firewood_faces(mesh: Mesh, pose: Transform3D) -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	var vertices := mesh.get_faces()
	for i in range(0, vertices.size(), 3):
		var a := pose * vertices[i]
		var b := pose * vertices[i + 1]
		var c := pose * vertices[i + 2]
		var normal := (b - a).cross(c - a)
		if absf(normal.y) < 0.00000001:
			continue
		var polygon := PackedVector2Array([Vector2(a.x, a.z), Vector2(b.x, b.z), Vector2(c.x, c.z)])
		var bounds := Rect2(polygon[0], Vector2.ZERO).expand(polygon[1]).expand(polygon[2])
		result.append({"polygon": polygon, "bounds": bounds,
			"height": Vector3(-normal.x / normal.y, -normal.z / normal.y, normal.dot(a) / normal.y)})
	return result


func _take_log(player: Node) -> void:
	if player != null and player.get("held_item") == &"":
		player.held_item = &"log"
		if Game.audio != null:
			Game.audio.play_interact(&"pickup")


# --------------------------------------------------------------------- tent

func _build_tent() -> void:
	tent = Tent.new(field)
	add_child(tent)
	tent.build()
	lanterns.append(tent.lantern)


# --------------------------------------------------------------------- dock

func _build_dock() -> void:
	dock = Dock.new(field)
	add_child(dock)
	dock.build()
	lanterns.append(dock.lantern)


# ---------------------------------------------------------------- lanterns

func _place_lanterns() -> void:
	# One post at the edge of the trail where you arrive, one beside the tent
	# door; both hold their arm out over the path or doorway.
	var posts := [
		{at = Vector2(3.7, 13.2), face = Vector2(1.4, 12.6)},
		{at = TerrainField.TENT + Vector2(-2.6, 2.4), face = TerrainField.TENT + Vector2(-0.8, 1.0)},
	]
	for post: Dictionary in posts:
		var pos: Vector2 = post.at
		var face: Vector2 = post.face
		var lantern := CampLantern.new()
		lantern.position = Vector3(pos.x, field.height(pos.x, pos.y), pos.y)
		# The hook arm extends along local +X: yaw it towards `face`.
		lantern.rotation.y = atan2(-(face.y - pos.y), face.x - pos.x)
		add_child(lantern)
		lantern.build(true, Quality.current.lantern_shadows)
		lanterns.append(lantern)


# ---------------------------------------------------------------- helpers

func _box_collider(origin: Vector3, size: Vector3, yaw: float, surface: StringName) -> void:
	var body := StaticBody3D.new()
	body.collision_layer = 1
	body.collision_mask = 0
	body.set_meta("surface", surface)
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = size
	shape.shape = box
	body.add_child(shape)
	body.position = origin
	body.rotation.y = yaw
	add_child(body)
