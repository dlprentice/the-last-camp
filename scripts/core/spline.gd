class_name Spline
extends RefCounted

## Catmull-Rom interpolation with a measured arc-length lookup. Equal steps
## of u travel equal distances, including within and across control segments.

var points: Array[Vector3] = []
const BAKE_STEPS := 64
var _distances := PackedFloat64Array()
var _total := 0.0


func _init(p_points: Array[Vector3]) -> void:
	points = p_points.duplicate()
	_measure()


func total_length() -> float:
	return _total


## An authored hold remains at its actual control point when the rest of
## the path changes length.
func progress_at_point(index: int) -> float:
	if points.size() < 2 or _total < 1e-8:
		return 0.0
	return _distances[clampi(index, 0, points.size() - 1) * BAKE_STEPS] / _total


func _control(i: int) -> Vector3:
	return points[clampi(i, 0, points.size() - 1)]


## Position on segment `i` at local parameter `t`.
func segment_point(i: int, t: float) -> Vector3:
	var p0 := _control(i - 1)
	var p1 := _control(i)
	var p2 := _control(i + 1)
	var p3 := _control(i + 2)
	var t2 := t * t
	var t3 := t2 * t
	return 0.5 * ((2.0 * p1) + (-p0 + p2) * t
			+ (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
			+ (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3)


func _measure() -> void:
	_distances = PackedFloat64Array([0.0])
	_total = 0.0
	for i in range(points.size() - 1):
		var prev := segment_point(i, 0.0)
		for k in range(1, BAKE_STEPS + 1):
			var cur := segment_point(i, float(k) / BAKE_STEPS)
			_total += prev.distance_to(cur)
			_distances.append(_total)
			prev = cur


## Point at normalised arc length `u` in [0, 1].
func sample(u: float) -> Vector3:
	if points.is_empty():
		return Vector3.ZERO
	if points.size() == 1 or _total < 1e-8:
		return points[0]
	var target := clampf(u, 0.0, 1.0) * _total
	var low := 0
	var high := _distances.size() - 1
	while high - low > 1:
		var mid := (low + high) / 2
		if _distances[mid] < target:
			low = mid
		else:
			high = mid
	var fraction := (target - _distances[low]) / maxf(_distances[high] - _distances[low], 1e-10)
	var parameter := (float(low) + fraction) / BAKE_STEPS
	var segment := mini(floori(parameter), points.size() - 2)
	return segment_point(segment, clampf(parameter - segment, 0.0, 1.0))
