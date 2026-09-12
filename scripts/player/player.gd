class_name Player
extends CharacterBody3D

## First-person controller: capsule physics against the terrain heightmap and
## props, wading with a depth limit, crouch, sprint with a subtle FOV kick,
## procedural head motion, surface-aware footsteps and an interaction ray.

signal footstep(surface: StringName, running: bool)
signal wading_changed(active: bool, speed: float)
signal focus_changed(target: Node)

const WALK_SPEED := 2.9
const RUN_SPEED := 5.6
const CROUCH_SPEED := 1.5
const WADE_FACTOR := 0.55
const MAX_WADE_DEPTH := 1.15
const ACCELERATION := 14.0
const FRICTION := 11.0
const AIR_CONTROL := 3.0
const GRAVITY := 9.8
const EYE_HEIGHT := 1.68
const CROUCH_EYE_HEIGHT := 1.02
const MOUSE_SENSITIVITY := 0.0019
const MAX_PITCH := deg_to_rad(84.0)
const BASE_FOV := 70.0
const SPRINT_FOV := 76.0
const INTERACT_RANGE := 3.2
const STEP_LENGTH_WALK := 1.75
const STEP_LENGTH_RUN := 2.3

var head: Node3D
var camera: Camera3D
var hand_socket: Node3D
var collider: CollisionShape3D
var interact_ray: RayCast3D
var photo: PhotoRig
var hand_light: OmniLight3D
var hand_lantern: Node3D

var enabled := false
var yaw := 0.0
var pitch := 0.0
var crouching := false
var running := false
var in_water := false
var water_depth := 0.0
var held_item: StringName = &""
var lantern_on := false

var _bob_phase := 0.0
var _step_distance := 0.0
var _eye_height := EYE_HEIGHT
var _land_dip := 0.0
var _was_on_floor := true
var _fall_speed := 0.0
var _focus: Node
var _lean := 0.0
var _strafe := 0.0


func _ready() -> void:
	Game.player = self
	_build_children()
	# Arrive on the trail south of the camp, facing the fire and the pond.
	global_position = Vector3(3.0, 0.0, 30.0)
	yaw = deg_to_rad(6.0)
	set_physics_process(true)


func _build_children() -> void:
	collider = CollisionShape3D.new()
	var shape := CapsuleShape3D.new()
	shape.radius = 0.32
	shape.height = 1.8
	collider.shape = shape
	collider.position = Vector3(0.0, 0.9, 0.0)
	add_child(collider)

	head = Node3D.new()
	head.name = "Head"
	head.position = Vector3(0.0, EYE_HEIGHT, 0.0)
	add_child(head)

	camera = Camera3D.new()
	camera.name = "Camera"
	camera.fov = BASE_FOV
	camera.near = 0.05
	camera.far = 1600.0
	camera.cull_mask = 0xFFFFF & ~(Pond.REFLECTION_LAYER | Pond.UNDERWATER_REFLECTION_LAYER)
	head.add_child(camera)

	hand_socket = Node3D.new()
	hand_socket.name = "HandSocket"
	hand_socket.position = Vector3(0.34, -0.42, -0.55)
	camera.add_child(hand_socket)

	interact_ray = RayCast3D.new()
	interact_ray.target_position = Vector3(0.0, 0.0, -INTERACT_RANGE)
	interact_ray.collision_mask = 1 | (1 << 1)
	interact_ray.collide_with_areas = true
	interact_ray.enabled = true
	camera.add_child(interact_ray)

	_build_hand_lantern()
	floor_max_angle = deg_to_rad(52.0)
	floor_snap_length = 0.4
	collision_mask = 1
	collision_layer = 1 << 2
	call_deferred("_add_photo_rig")


func _add_photo_rig() -> void:
	if photo != null or get_parent() == null:
		return
	photo = PhotoRig.new()
	get_parent().add_child(photo)


func _build_hand_lantern() -> void:
	hand_lantern = Node3D.new()
	hand_lantern.name = "HandLantern"
	hand_lantern.visible = false
	hand_lantern.scale = Vector3.ONE * 0.62
	hand_lantern.position = Vector3(0.0, -0.3, 0.0)
	hand_socket.add_child(hand_lantern)
	var metal := MeshInstance3D.new()
	metal.mesh = PropMeshes.hurricane_metal()
	metal.material_override = PropMaterials.iron(Color(0.2, 0.17, 0.14))
	metal.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	hand_lantern.add_child(metal)
	var glass := MeshInstance3D.new()
	glass.mesh = PropMeshes.hurricane_glass()
	var glass_mat := StandardMaterial3D.new()
	glass_mat.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	glass_mat.cull_mode = BaseMaterial3D.CULL_DISABLED
	glass_mat.albedo_color = Color(1.0, 0.9, 0.7, 0.28)
	glass_mat.roughness = 0.08
	glass_mat.emission_enabled = true
	glass_mat.emission = Color(1.0, 0.62, 0.25)
	glass_mat.emission_energy_multiplier = 0.55
	glass.material_override = glass_mat
	glass.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	hand_lantern.add_child(glass)
	var flame := MeshInstance3D.new()
	var flame_mesh := SphereMesh.new()
	flame_mesh.radius = 0.014
	flame_mesh.height = 0.04
	flame_mesh.radial_segments = 8
	flame_mesh.rings = 5
	flame.mesh = flame_mesh
	var flame_mat := StandardMaterial3D.new()
	flame_mat.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	flame_mat.albedo_color = Color(1.0, 0.75, 0.4)
	flame_mat.emission_enabled = true
	flame_mat.emission = Color(1.0, 0.72, 0.35)
	flame_mat.emission_energy_multiplier = 6.0
	flame.material_override = flame_mat
	flame.position.y = CampLantern.FLAME_HEIGHT
	flame.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	hand_lantern.add_child(flame)
	hand_light = OmniLight3D.new()
	hand_light.light_color = Color(1.0, 0.74, 0.4)
	hand_light.light_energy = 1.7
	hand_light.omni_range = 7.5
	hand_light.omni_attenuation = 1.3
	hand_light.shadow_enabled = false
	hand_light.light_volumetric_fog_energy = 0.35
	hand_light.position = Vector3(0.0, CampLantern.FLAME_HEIGHT + 0.02, 0.0)
	hand_lantern.add_child(hand_light)


## Hands control to the player (after the intro or when photo mode ends).
func begin() -> void:
	enabled = true
	snap_to_ground()
	camera.make_current()
	if Game.world != null:
		camera.attributes = Game.world.attributes
	Game.mode = Game.Mode.PLAY


func snap_to_ground() -> void:
	if Game.camp == null:
		return
	global_position.y = Game.camp.height_at(global_position.x, global_position.z) + 0.12


func toggle_lantern() -> void:
	lantern_on = not lantern_on
	if hand_lantern != null:
		hand_lantern.visible = lantern_on
	if Game.audio != null:
		Game.audio.play_interact(&"lantern_toggle")


func suspend() -> void:
	enabled = false
	velocity = Vector3.ZERO


func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("photo_mode"):
		if Game.mode == Game.Mode.PHOTO and photo != null:
			photo.exit()
		elif Game.mode == Game.Mode.PLAY and enabled and photo != null:
			photo.enter(camera)
		return
	if not enabled or Game.mode != Game.Mode.PLAY:
		return
	if event is InputEventMouseMotion:
		var motion := event as InputEventMouseMotion
		yaw -= motion.relative.x * MOUSE_SENSITIVITY
		pitch = clampf(pitch - motion.relative.y * MOUSE_SENSITIVITY, -MAX_PITCH, MAX_PITCH)
	elif event.is_action_pressed("interact"):
		if _focus != null and _focus.has_method("interact"):
			_focus.interact(self)
	elif event.is_action_pressed("toggle_lantern"):
		toggle_lantern()


func _physics_process(delta: float) -> void:
	if Game.camp == null:
		return
	_update_water_state()
	var input := Vector2.ZERO
	if enabled and Game.mode == Game.Mode.PLAY:
		input = Input.get_vector("move_left", "move_right", "move_forward", "move_back")
		crouching = Input.is_action_pressed("crouch")
		running = Input.is_action_pressed("sprint") and not crouching and input.length() > 0.1
	else:
		running = false

	var speed := CROUCH_SPEED if crouching else (RUN_SPEED if running else WALK_SPEED)
	if in_water:
		speed *= lerpf(1.0, WADE_FACTOR, clampf(water_depth / 0.8, 0.0, 1.0))

	var forward := Vector3(-sin(yaw), 0.0, -cos(yaw))
	var right := Vector3(cos(yaw), 0.0, -sin(yaw))
	var wish := (right * input.x + forward * -input.y).normalized() if input.length() > 0.01 else Vector3.ZERO
	var desired := wish * speed
	_strafe = input.x

	var on_floor := is_on_floor()
	var accel := (ACCELERATION if wish != Vector3.ZERO else FRICTION) if on_floor else AIR_CONTROL
	var horizontal := Vector3(velocity.x, 0.0, velocity.z).move_toward(desired, accel * delta)
	horizontal = _limit_wading(horizontal, delta)
	velocity.x = horizontal.x
	velocity.z = horizontal.z
	if not on_floor:
		velocity.y -= GRAVITY * delta
		_fall_speed = velocity.y
	elif velocity.y < 0.0:
		velocity.y = 0.0
	move_and_slide()

	if on_floor and not _was_on_floor and _fall_speed < -3.0:
		_land_dip = clampf(-_fall_speed * 0.02, 0.0, 0.14)
	_was_on_floor = on_floor

	_update_head(delta, horizontal.length(), on_floor)
	_update_focus()
	rotation.y = yaw
	head.rotation.x = pitch
	head.rotation.z = _lean


func _update_water_state() -> void:
	# Depth is how far the feet are under the surface, never more than the
	# pond is deep there: standing on the dock over deep water is dry, wading
	# on the bed reads the bed. The origin sits 0.12 m above the support.
	var bed_depth: float = Game.camp.field.water_depth(global_position.x, global_position.z)
	var submerged := TerrainField.WATER_LEVEL - (global_position.y - 0.12)
	var depth := clampf(minf(bed_depth, submerged), 0.0, bed_depth)
	var was := in_water
	water_depth = depth
	in_water = depth > 0.12
	var planar_speed := Vector2(velocity.x, velocity.z).length()
	if in_water != was or (in_water and planar_speed > 0.2):
		wading_changed.emit(in_water, planar_speed if in_water else 0.0)


## Stops the player wading past chest depth: movement towards deeper water is
## cancelled and the shallows pull gently in the direction the bed rises.
func _limit_wading(horizontal: Vector3, _delta: float) -> Vector3:
	if water_depth <= MAX_WADE_DEPTH:
		return horizontal
	var shallow := _shallow_direction()
	var deeper := -horizontal.dot(shallow)
	if deeper > 0.0:
		horizontal += shallow * deeper
	var excess := clampf((water_depth - MAX_WADE_DEPTH) * 3.0, 0.0, 1.0)
	var wanted := 1.4 * excess
	var current := horizontal.dot(shallow)
	if current < wanted:
		horizontal += shallow * (wanted - current)
	return horizontal


## Horizontal direction in which the pond bed rises (the gradient of the
## ground height), falling back to "away from the pond centre".
func _shallow_direction() -> Vector3:
	var field: TerrainField = Game.camp.field
	var x := global_position.x
	var z := global_position.z
	var step := 0.75
	var dir := Vector3(
			field.height(x + step, z) - field.height(x - step, z), 0.0,
			field.height(x, z + step) - field.height(x, z - step))
	if dir.length_squared() < 1e-6:
		dir = Vector3(x - TerrainField.POND_CENTRE.x, 0.0, z - TerrainField.POND_CENTRE.y)
	if dir.length_squared() < 1e-6:
		dir = Vector3.RIGHT
	return dir.normalized()


func _update_head(delta: float, speed: float, on_floor: bool) -> void:
	var target_eye := CROUCH_EYE_HEIGHT if crouching else EYE_HEIGHT
	if in_water:
		target_eye -= minf(water_depth * 0.15, 0.12)
	_eye_height = lerpf(_eye_height, target_eye, 1.0 - exp(-delta * 9.0))
	_land_dip = lerpf(_land_dip, 0.0, 1.0 - exp(-delta * 7.0))

	var moving := speed > 0.35 and on_floor
	if moving:
		var stride := STEP_LENGTH_RUN if running else STEP_LENGTH_WALK
		_bob_phase += speed / stride * PI * delta
		_step_distance += speed * delta
		if _step_distance >= stride * 0.5:
			_step_distance = 0.0
			footstep.emit(_current_surface(), running)
	else:
		_bob_phase = lerp_angle(_bob_phase, roundf(_bob_phase / PI) * PI, 1.0 - exp(-delta * 6.0))

	var amp := clampf(speed / RUN_SPEED, 0.0, 1.0) * (0.55 if crouching else 1.0)
	var bob_y := sin(_bob_phase * 2.0) * 0.024 * amp
	var bob_x := sin(_bob_phase) * 0.016 * amp
	head.position = Vector3(bob_x, _eye_height + bob_y - _land_dip, 0.0)
	_lean = lerpf(_lean, -_strafe * 0.012, 1.0 - exp(-delta * 5.0))

	var target_fov := SPRINT_FOV if running else BASE_FOV
	camera.fov = lerpf(camera.fov, target_fov, 1.0 - exp(-delta * 5.0))


func _current_surface() -> StringName:
	if in_water:
		return &"water"
	if is_on_floor():
		var collider_node := get_last_slide_collision().get_collider() if get_slide_collision_count() > 0 else null
		if collider_node != null and collider_node.has_meta("surface"):
			return collider_node.get_meta("surface")
	var mask: Color = Game.camp.field.material_mask(global_position.x, global_position.z)
	if mask.a > 0.45:
		return &"dirt"
	if mask.b > 0.5:
		return &"dirt"
	return &"grass"


func _update_focus() -> void:
	var target: Node = null
	if enabled and Game.mode == Game.Mode.PLAY and interact_ray.is_colliding():
		var hit := interact_ray.get_collider()
		if hit != null:
			target = hit as Node
			while target != null and not target.has_method("interact"):
				target = target.get_parent()
	if target != _focus:
		_focus = target
		focus_changed.emit(target)


func focus() -> Node:
	return _focus


func eye_position() -> Vector3:
	return camera.global_position
