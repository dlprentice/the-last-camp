class_name PhotoRig
extends Node3D

## Smoothed free camera. WASD/Space/C move, wheel changes speed; middle click
## locks/unlocks centre-ray focus. Focus is sampled at 12 Hz, not every frame.
const MIN_SPEED := 0.8
const MAX_SPEED := 18.0
const FOCUS_INTERVAL := 1.0 / 12.0
var camera: Camera3D
var speed := 4.5
var yaw := 0.0
var pitch := 0.0
var _active := false
var _velocity := Vector3.ZERO
var _focus_distance := 8.0
var _target_focus := 8.0
var _focus_timer := 0.0
var focus_locked := false
var _saved_focus := {}

func _ready() -> void:
	process_priority = -10
	camera = Camera3D.new()
	camera.name = "PhotoCamera"
	camera.fov = 55.0
	camera.near = 0.05
	camera.far = 1600.0
	camera.cull_mask = 0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)
	add_child(camera)
	Game.photo_camera = camera
	set_process(false)
	set_physics_process(false)

func enter(from: Camera3D) -> void:
	if from != null:
		global_position = from.global_position
		var forward := -from.global_basis.z
		yaw = atan2(-forward.x, -forward.z)
		pitch = asin(clampf(forward.y, -1.0, 1.0))
		camera.fov = from.fov
	camera.rotation = Vector3(pitch, yaw, 0)
	_velocity = Vector3.ZERO
	_focus_timer = FOCUS_INTERVAL
	focus_locked = false
	camera.make_current()
	if Game.world != null:
		camera.attributes = Game.world.attributes
		var attributes: CameraAttributesPractical = Game.world.attributes
		for key in [&"dof_blur_far_enabled", &"dof_blur_near_enabled", &"dof_blur_far_distance", &"dof_blur_near_distance", &"dof_blur_far_transition", &"dof_blur_near_transition", &"dof_blur_amount"]:
			_saved_focus[key] = attributes.get(key)
	if Game.player != null:
		Game.player.suspend()
	Game.mode = Game.Mode.PHOTO
	_active = true
	set_process(true)
	set_physics_process(true)

func exit() -> void:
	_active = false
	set_process(false)
	set_physics_process(false)
	_velocity = Vector3.ZERO
	if Game.world != null:
		for key in _saved_focus:
			Game.world.attributes.set(key, _saved_focus[key])
	_saved_focus.clear()
	if Game.player != null:
		Game.player.begin()

func _unhandled_input(event: InputEvent) -> void:
	if not _active:
		return
	if event is InputEventMouseMotion:
		var motion := event as InputEventMouseMotion
		yaw -= motion.relative.x * 0.0022
		pitch = clampf(pitch - motion.relative.y * 0.0022, -deg_to_rad(85.0), deg_to_rad(85.0))
	elif event is InputEventMouseButton and event.is_pressed():
		var button := event as InputEventMouseButton
		if button.button_index == MOUSE_BUTTON_WHEEL_UP:
			speed = minf(speed * 1.15, MAX_SPEED)
		elif button.button_index == MOUSE_BUTTON_WHEEL_DOWN:
			speed = maxf(speed / 1.15, MIN_SPEED)
		elif button.button_index == MOUSE_BUTTON_MIDDLE:
			focus_locked = not focus_locked

static func move_direction(frame: Basis, wish: Vector3) -> Vector3:
	# Input.get_axis(forward, back) is negative for W. Godot forward is -Z;
	# another minus sign here reversed W/S in the original free camera.
	return (frame.x * wish.x + Vector3.UP * wish.y + frame.z * wish.z).limit_length(1.0)

static func focus_step(current: float, target: float, delta: float) -> float:
	return lerpf(current, target, 1.0 - exp(-maxf(delta, 0.0) * 5.0))

func _process(delta: float) -> void:
	if not _active:
		return
	var look_blend := 1.0 - exp(-delta * 22.0)
	camera.rotation = Vector3(lerpf(camera.rotation.x, pitch, look_blend), lerp_angle(camera.rotation.y, yaw, look_blend), 0)
	var wish := Vector3(Input.get_axis("move_left", "move_right"), Input.get_axis("crouch", "fly_up"), Input.get_axis("move_forward", "move_back"))
	var sprint := 2.1 if Input.is_action_pressed("sprint") else 1.0
	var target := move_direction(camera.basis, wish) * speed * sprint
	# Exact integration of first-order velocity response for constant input:
	# smooth starts/stops without travel changing with capture frame rate.
	var decay := exp(-delta * 9.0)
	global_position += target * delta + (_velocity - target) * (1.0 - decay) / 9.0
	_velocity = target + (_velocity - target) * decay
	_focus_distance = focus_step(_focus_distance, _target_focus, delta)
	if Game.world != null:
		var a: CameraAttributesPractical = Game.world.attributes
		a.dof_blur_far_enabled = Quality.current.dof
		a.dof_blur_near_enabled = Quality.current.dof
		a.dof_blur_far_distance = _focus_distance * 1.15
		a.dof_blur_far_transition = maxf(0.8, _focus_distance * 2.2)
		a.dof_blur_near_distance = maxf(0.03, _focus_distance * 0.55)
		a.dof_blur_near_transition = maxf(0.25, _focus_distance * 0.4)
		a.dof_blur_amount = 0.035

func _physics_process(delta: float) -> void:
	if not _active or focus_locked:
		return
	_focus_timer += delta
	if _focus_timer < FOCUS_INTERVAL:
		return
	_focus_timer = fmod(_focus_timer, FOCUS_INTERVAL)
	var origin := camera.global_position
	var direction := -camera.global_basis.z
	var query := PhysicsRayQueryParameters3D.create(origin, origin + direction * 120.0, 1)
	var hit := get_world_3d().direct_space_state.intersect_ray(query)
	var distance := origin.distance_to(hit.position) if not hit.is_empty() else 80.0
	# Water has no collider. Focus its visible surface rather than the distant
	# pond bed when the centre ray crosses open water before a solid object.
	if direction.y < -0.001 and origin.y > TerrainField.WATER_LEVEL and Game.camp != null:
		var water_distance := (TerrainField.WATER_LEVEL - origin.y) / direction.y
		var at := origin + direction * water_distance
		if water_distance > 0.0 and water_distance < distance and Game.camp.field.water_depth(at.x, at.z) > 0.0:
			distance = water_distance
	_target_focus = clampf(distance, 0.30, 80.0)
