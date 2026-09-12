class_name IntroDolly
extends Node3D

## Establishing shot: a slow crane down from above the treeline into the
## clearing, ending at eye height on the trail where the player takes over.

signal finished

const DURATION := 17.0
const SKIP_GRACE := 0.8

## Crane in from the east above the meadow, sweeping past the treeline to
## eye height on the trail. Kept clear of trunks and crowns by
## test_intro_dolly_path_is_clear_of_trees.
const PATH: Array[Vector3] = [
	Vector3(16.3, 8.0, 52.0),
	Vector3(6.4, 3.9, 40.0),
	Vector3(7.9, 4.2, 28.0),
	Vector3(8.6, 2.4, 19.5),
	Vector3(3.2, 1.72, 15.0),
]
const LOOK: Array[Vector3] = [
	Vector3(-4.0, 3.5, 4.0),
	Vector3(-5.0, 2.4, 3.0),
	Vector3(-8.0, 1.4, 2.0),
	Vector3(-12.0, 0.9, 3.0),
	Vector3(-14.0, 0.9, 3.5),
]
## Trees the path would fly through (trunk or crown), as readable strings;
## empty when the path is clear. Used by the tests.
static func obstructions(field: TerrainField, plan: ScenePlan, samples := 80) -> PackedStringArray:
	var out := PackedStringArray()
	var path := Spline.new(PATH)
	for i in samples:
		var p := path.sample(float(i) / float(samples - 1))
		if p.y < field.height(p.x, p.z) + 1.0:
			out.append("ground at %s" % p)
		for t in plan.near_trees():
			var species := TreeSpecies.by_kind(t.kind)
			var base := field.height(t.position.x, t.position.y)
			var top := base + species.height.y * t.scale + 1.0
			var crown_base := base + species.branch_start * species.height.x * t.scale - 0.6
			var radius := ScenePlan.crown_footprint(t.kind) * t.scale + 0.5
			var dxz := Vector2(p.x, p.z).distance_to(t.position)
			if dxz < 1.0 and p.y < top:
				out.append("trunk %s %s cam %s" % [species.name, t.position, p])
			elif dxz < radius and p.y > crown_base and p.y < top:
				out.append("crown %s %s r=%.1f base=%.1f cam %s" % [species.name, t.position, radius, crown_base, p])
	return out


var camera: Camera3D
var elapsed := 0.0
var playing := false
var _player: Player
var _path := Spline.new(PATH)
var _look := Spline.new(LOOK)


func _ready() -> void:
	camera = Camera3D.new()
	camera.name = "IntroCamera"
	camera.fov = 40.0
	camera.near = 0.05
	camera.far = 1600.0
	camera.cull_mask = 0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)
	add_child(camera)
	_apply(0.0)


func play(player: Player) -> void:
	_player = player
	playing = true
	elapsed = 0.0
	camera.make_current()
	if Game.world != null:
		camera.attributes = Game.world.attributes
		Game.world.attributes.dof_blur_far_enabled = Quality.current.dof
	Game.mode = Game.Mode.INTRO
	_apply(0.0)


func _process(delta: float) -> void:
	if not playing:
		return
	elapsed += delta
	if elapsed > SKIP_GRACE and Input.is_action_just_pressed("skip_intro"):
		finish()
		return
	if elapsed >= DURATION:
		finish()
		return
	_apply(elapsed / DURATION)


func _apply(t: float) -> void:
	var u := smoothstep(0.0, 1.0, t)
	u = lerpf(t, u, 0.75)
	var pos := _path.sample(u)
	var target := _look.sample(u)
	camera.global_position = pos
	camera.look_at(target, Vector3.UP)
	camera.fov = lerpf(38.0, 64.0, u)
	if Game.world != null:
		var a: CameraAttributesPractical = Game.world.attributes
		a.dof_blur_far_distance = pos.distance_to(target) * 1.4
		a.dof_blur_far_transition = pos.distance_to(target) * 2.5
		a.dof_blur_amount = lerpf(0.09, 0.03, u)


func finish() -> void:
	if not playing:
		return
	playing = false
	if Game.world != null:
		Game.world.attributes.dof_blur_far_enabled = false
	if _player != null:
		_player.global_position = Vector3(camera.global_position.x, 0.0, camera.global_position.z)
		_player.global_position.y = Game.camp.height_at(_player.global_position.x, _player.global_position.z) + 0.1
		var forward := -camera.global_transform.basis.z
		_player.yaw = atan2(-forward.x, -forward.z)
		_player.pitch = asin(clampf(forward.y, -1.0, 1.0))
		_player.begin()
	finished.emit()
	queue_free()
