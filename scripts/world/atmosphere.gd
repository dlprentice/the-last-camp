class_name Atmosphere
extends RefCounted

## CPU twin of shaders/inc/atmosphere.gdshaderinc.
##
## Pure functions mapping the time of day to sun/moon geometry and evaluating
## the same single-scattering model the sky shader uses, so that the
## DirectionalLight colour, fog colour and sky stay physically consistent.

const PLANET_RADIUS := 6360000.0
const TOP_RADIUS := 6420000.0
const RAYLEIGH_BETA := Vector3(5.802e-6, 13.558e-6, 33.1e-6)
const RAYLEIGH_H := 8000.0
const MIE_BETA := 3.996e-6
const MIE_ABSORB := 4.4e-6
const MIE_H := 1200.0
const OZONE_BETA := Vector3(0.650e-6, 1.881e-6, 0.085e-6)
const OZONE_CENTRE := 25000.0
const OZONE_WIDTH := 15000.0

const SUN_INTENSITY := 22.0
const SUN_ENERGY_SCALE := 4.2
const MOON_ENERGY := 0.2
const MOON_COLOR := Color(0.62, 0.72, 1.0)

## Solar noon (hours). The sun culminates at +62 degrees and sets around 20:15.
const NOON := 13.0
const SUNRISE := 5.75
const SUNSET := 20.25


## Direction *towards* the sun for a given hour of day (0..24).
static func sun_direction(hour: float) -> Vector3:
	var theta := (hour - NOON) / 24.0 * TAU
	var elevation := deg_to_rad(47.0 * cos(theta) + 15.0)
	var azimuth := deg_to_rad(180.0 + (hour - NOON) * 15.0)
	return _dir_from_angles(azimuth, elevation)


## Direction towards the moon: roughly opposite the sun, offset so both are
## never exactly antipodal (which would look artificial).
static func moon_direction(hour: float) -> Vector3:
	var theta := (hour - NOON) / 24.0 * TAU + PI
	var elevation := deg_to_rad(40.0 * cos(theta) + 12.0)
	var azimuth := deg_to_rad(180.0 + (hour - NOON) * 15.0 + 180.0 - 28.0)
	return _dir_from_angles(azimuth, elevation)


## Azimuth measured clockwise from north (-Z); elevation above the horizon.
static func _dir_from_angles(azimuth: float, elevation: float) -> Vector3:
	return Vector3(sin(azimuth) * cos(elevation), sin(elevation), -cos(azimuth) * cos(elevation)).normalized()


static func sun_elevation_deg(hour: float) -> float:
	return rad_to_deg(asin(clampf(sun_direction(hour).y, -1.0, 1.0)))


## 0 at deep night, 1 in full daylight, with a smooth twilight band.
static func daylight(hour: float) -> float:
	var elev := sun_direction(hour).y
	return smoothstep(-0.12, 0.12, elev)


# ------------------------------------------------------------- scattering

static func _ray_sphere(origin: Vector3, dir: Vector3, radius: float) -> Vector2:
	var b := origin.dot(dir)
	var c := origin.dot(origin) - radius * radius
	var disc := b * b - c
	if disc < 0.0:
		return Vector2(1e9, -1e9)
	var s := sqrt(disc)
	return Vector2(-b - s, -b + s)


static func _densities(p: Vector3) -> Vector3:
	var h := maxf(p.length() - PLANET_RADIUS, 0.0)
	var r := exp(-h / RAYLEIGH_H)
	var m := exp(-h / MIE_H)
	var o := maxf(0.0, 1.0 - absf(h - OZONE_CENTRE) / OZONE_WIDTH) * r
	return Vector3(r, m, o)


static func _extinction(d: Vector3, haze: float) -> Vector3:
	return RAYLEIGH_BETA * d.x + Vector3.ONE * ((MIE_BETA + MIE_ABSORB) * haze * d.y) + OZONE_BETA * d.z


static func optical_depth(p: Vector3, dir: Vector3, steps: int, haze: float) -> Vector3:
	var hit := _ray_sphere(p, dir, TOP_RADIUS)
	var ground := _ray_sphere(p, dir, PLANET_RADIUS)
	if ground.y > 0.0 and ground.x > 0.0:
		return Vector3.ONE * 1e6
	var len := maxf(hit.y, 0.0)
	var ds := len / float(steps)
	var depth := Vector3.ZERO
	for i in steps:
		var s := p + dir * ((float(i) + 0.5) * ds)
		depth += _extinction(_densities(s), haze) * ds
	return depth


static func observer(altitude := 120.0) -> Vector3:
	return Vector3(0.0, PLANET_RADIUS + altitude, 0.0)


static func _exp3(v: Vector3) -> Vector3:
	return Vector3(exp(v.x), exp(v.y), exp(v.z))


## Fraction of sunlight that reaches the observer along `dir`.
static func transmittance(dir: Vector3, haze: float, steps := 8) -> Vector3:
	return _exp3(-optical_depth(observer(), dir, steps, haze))


## In-scattered radiance towards `dir` for the sun in `sun`.
static func scatter(dir: Vector3, sun: Vector3, haze: float, view_steps := 12, light_steps := 5, mie_g := 0.78) -> Vector3:
	var origin := observer()
	var hit := _ray_sphere(origin, dir, TOP_RADIUS)
	var ground := _ray_sphere(origin, dir, PLANET_RADIUS)
	var len := hit.y
	if ground.x > 0.0:
		len = minf(len, ground.x)
	len = maxf(len, 0.0)
	var ds := len / float(view_steps)
	var mu := dir.dot(sun)
	var phase_r := 3.0 / (16.0 * PI) * (1.0 + mu * mu)
	var g2 := mie_g * mie_g
	var denom := 1.0 + g2 - 2.0 * mie_g * mu
	var phase_m := 3.0 / (8.0 * PI) * (1.0 - g2) * (1.0 + mu * mu) / ((2.0 + g2) * pow(maxf(denom, 1e-4), 1.5))
	var sum_r := Vector3.ZERO
	var sum_m := Vector3.ZERO
	var depth := Vector3.ZERO
	for i in view_steps:
		var p := origin + dir * ((float(i) + 0.5) * ds)
		var dens := _densities(p)
		depth += _extinction(dens, haze) * ds
		var light_depth := optical_depth(p, sun, light_steps, haze)
		var attenuation := _exp3(-(depth + light_depth))
		sum_r += attenuation * (dens.x * ds)
		sum_m += attenuation * (dens.y * ds)
	var rayleigh := Vector3(sum_r.x * RAYLEIGH_BETA.x, sum_r.y * RAYLEIGH_BETA.y, sum_r.z * RAYLEIGH_BETA.z) * phase_r
	var mie := sum_m * (MIE_BETA * haze * phase_m)
	return (rayleigh + mie) * SUN_INTENSITY


static func luminance(c: Vector3) -> float:
	return c.x * 0.2126 + c.y * 0.7152 + c.z * 0.0722


## Colour and energy for the sun DirectionalLight. Energy fades to zero as the
## disc dips below the horizon so the moon can take over.
static func sun_light(sun_dir: Vector3, haze: float) -> Dictionary:
	var t := transmittance(sun_dir, haze)
	var horizon_fade := smoothstep(-0.02, 0.06, sun_dir.y)
	var lum := luminance(t)
	var peak := maxf(maxf(t.x, t.y), maxf(t.z, 1e-4))
	var color := Color(t.x / peak, t.y / peak, t.z / peak)
	return {color = color, energy = lum * SUN_ENERGY_SCALE * horizon_fade}


static func moon_light(sun_dir: Vector3, moon_dir: Vector3) -> Dictionary:
	var night := smoothstep(0.05, -0.1, sun_dir.y)
	var up := smoothstep(-0.05, 0.15, moon_dir.y)
	return {color = MOON_COLOR, energy = MOON_ENERGY * night * up}


## Average sky colour near the horizon, used for distance fog.
static func fog_color(sun_dir: Vector3, haze: float) -> Color:
	var flat := Vector3(sun_dir.x, 0.0, sun_dir.z)
	if flat.length_squared() < 1e-4:
		flat = Vector3.FORWARD
	flat = flat.normalized()
	var toward := scatter(Vector3(flat.x, 0.05, flat.z).normalized(), sun_dir, haze, 8, 4)
	var away := scatter(Vector3(-flat.x, 0.05, -flat.z).normalized(), sun_dir, haze, 8, 4)
	var side := scatter(Vector3(-flat.z, 0.05, flat.x).normalized(), sun_dir, haze, 8, 4)
	var avg := (toward + away * 1.5 + side * 2.0) / 4.5
	return Color(avg.x, avg.y, avg.z)
