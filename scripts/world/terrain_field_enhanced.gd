class_name TerrainFieldEnhanced
extends TerrainField

## Art-direction layer over TerrainField. The base field still owns authored
## pads, trail, pond basin and distant hills; this layer adds broad mid-ground
## landform structure and varied bank cross-sections while preserving the exact
## waterline sign. Because Camp receives this field before build(), mesh,
## collision, pond physics and all scatterers see the same surface.

var _macro_a := FastNoiseLite.new()
var _macro_b := FastNoiseLite.new()


func _init(seed_value := 20260902) -> void:
	super(seed_value)
	_macro_a.seed = seed_value + 731
	_macro_a.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_macro_a.frequency = 0.0065
	_macro_a.fractal_type = FastNoiseLite.FRACTAL_FBM
	_macro_a.fractal_octaves = 2
	_macro_a.fractal_lacunarity = 2.0
	_macro_a.fractal_gain = 0.46
	_macro_b.seed = seed_value + 991
	_macro_b.noise_type = FastNoiseLite.TYPE_SIMPLEX_SMOOTH
	_macro_b.frequency = 0.013
	_macro_b.fractal_type = FastNoiseLite.FRACTAL_FBM
	_macro_b.fractal_octaves = 2
	_macro_b.fractal_gain = 0.42


func height(x: float, z: float) -> float:
	var h := super.height(x, z)
	var p := Vector2(x, z)
	var distance := p.length()

	# Broad drainage folds and shoulders only. Never perturb the immediate camp,
	# the travelled trail or the fragile first few decimetres above the pond.
	# The material/detail-normal layers remain responsible for microrelief.
	if h > WATER_LEVEL + 0.35:
		var radial_envelope := smoothstep(16.0, 42.0, distance) * (1.0 - smoothstep(150.0, 235.0, distance))
		var trail_guard := smoothstep(2.0, 6.5, distance_to_polyline(p, TRAIL))
		var fire_guard := smoothstep(8.0, 15.0, p.distance_to(FIRE))
		var tent_guard := smoothstep(6.0, 12.0, p.distance_to(TENT))
		# Preserve the low sunset side across the pond; east/north shoulders can
		# carry more relief and make the clearing feel nested into actual terrain.
		var sunset_guard := lerpf(0.34, 1.0, smoothstep(-72.0, -12.0, x))
		var broad := _macro_a.get_noise_2d(x, z) * 0.72 + _macro_b.get_noise_2d(x, z) * 0.30
		# Positive shoulders are allowed to rise more than drainage folds cut;
		# that protects the dry-land invariant while creating readable silhouettes.
		broad = broad if broad >= 0.0 else broad * 0.58
		broad = broad * 1.8 + sculpted_form(p)
		h += broad * radial_envelope * trail_guard * fire_guard * tent_guard * sunset_guard * smoothstep(WATER_LEVEL + 0.35, WATER_LEVEL + 1.1, h)

	# Alternate subtly steeper and softer bank sectors. Scale the base profile
	# relative to WATER_LEVEL rather than adding height: negative stays negative,
	# positive stays positive, and the authored zero crossing cannot move.
	var delta := p - POND_CENTRE
	var pond_distance := delta.length()
	if pond_distance > 0.001:
		var angle := atan2(delta.y, delta.x)
		var s := pond_distance / pond_radius_at(angle)
		if s > 0.62 and s < 1.30:
			var rel := h - WATER_LEVEL
			var style := sin(angle * 2.0 + 0.45) * 0.58 + sin(angle * 5.0 - 1.10) * 0.27 + sin(angle * 7.0 + 2.2) * 0.15
			var bank_band := smoothstep(0.62, 0.82, s) * (1.0 - smoothstep(1.16, 1.30, s))
			var strength := 0.15 if rel < 0.0 else 0.27
			var factor := maxf(0.62, 1.0 + style * strength * bank_band)
			h = WATER_LEVEL + rel * factor
	return h


## Authored shoulders, not a uniform noise blanket. The valley stays open to
## the water; the northern/eastern forest gains several overlapping depth planes.
static func sculpted_form(p: Vector2) -> float:
	var east := _shoulder(p, Vector2(45, -18), Vector2(24, 43), 3.8, -0.20)
	var north := _shoulder(p, Vector2(-1, -68), Vector2(40, 27), 3.1, 0.17)
	var bank := _shoulder(p, Vector2(-68, 33), Vector2(26, 20), 2.0, -0.40)
	var fold := _shoulder(p, Vector2(23, -39), Vector2(7, 30), 0.65, -0.65)
	return east + north + bank - fold


static func _shoulder(p: Vector2, centre: Vector2, axes: Vector2, height: float, yaw: float) -> float:
	var q := (p - centre).rotated(yaw) / axes
	var distance := q.length_squared()
	if distance >= 1.0:
		return 0.0
	var support := 1.0 - distance
	return height * support * support * support
