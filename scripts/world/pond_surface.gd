class_name PondSurface
extends RefCounted

## CPU companion to inc/pond_waves.gdshaderinc. Physics queries the same
## analytic surface as rendering; the GPU adds its sampled residual height.
const WAVE_HEIGHT := 0.012
const WAVE_LENGTH := 3.2
const RESIDUAL_BOUND := 0.055


static func displacement(p: Vector2, time: float, wind: Vector2, strength: float) -> Vector3:
	var direction := (wind + Vector2.ONE * 0.0001).normalized()
	var result := Vector3.ZERO
	for i in 3:
		var fi := float(i)
		var d := (direction + Vector2(sin(fi * 2.1), cos(fi * 1.7)) * 0.55).normalized()
		var k := TAU / (WAVE_LENGTH * (1.0 - fi * 0.28))
		var c := sqrt(9.8 / k)
		var a := WAVE_HEIGHT * (1.0 - fi * 0.3) * (0.4 + 0.6 * clampf(strength, 0.0, 1.5))
		var phase := k * (d.dot(p) - c * time)
		result += Vector3(d.x * a * cos(phase) * 0.6, a * sin(phase), d.y * a * cos(phase) * 0.6)
	return result


static func height_at(p: Vector2, time: float, wind: Vector2, strength: float) -> float:
	var first := displacement(p, time, wind, strength)
	return TerrainField.WATER_LEVEL + displacement(p - Vector2(first.x, first.z), time, wind, strength).y


static func buoyancy(immersion: float, vertical_speed: float, mass: float, draft: float, points: int) -> float:
	# Archimedes force for the probe's effective submerged column. The waterplane
	# area is calibrated to the boat's resting draft. Drag opposes point velocity.
	var area := mass / (1000.0 * draft * float(points))
	var stiffness := 1000.0 * 9.8 * area
	var damping := 1.4 * sqrt(stiffness * mass / float(points))
	var wetted := clampf(immersion / draft, 0.0, 1.0)
	return maxf(0.0, stiffness * clampf(immersion, 0.0, draft * 3.0) - damping * vertical_speed * wetted)
