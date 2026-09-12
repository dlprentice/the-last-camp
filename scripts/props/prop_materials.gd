class_name PropMaterials
extends RefCounted

## Material factory for the camp's built props. Every prop shader takes the
## same baked PBR sets (textures/<set>_albedo|normal|orm.png) plus the shared
## noise texture; these helpers keep the binding in one place.

const NOISE := "res://textures/noise_rgba.png"


## World-space triplanar PBR (rocks, logs, canvas): no UVs needed.
static func triplanar(set_name: String, tint := Color.WHITE, tile := 0.55, moss := 0.0,
		metallic := 0.0) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/prop.gdshader")
	Camp.bind_prop_pbr(mat, set_name)
	Camp.bind_texture(mat, "noise_tex", NOISE)
	mat.set_shader_parameter("tint", tint)
	mat.set_shader_parameter("tile", tile)
	mat.set_shader_parameter("moss_amount", moss)
	mat.set_shader_parameter("metallic", metallic)
	return mat


## UV-mapped wood whose grain follows the piece (planks, piles, hulls).
static func wood(tint := Color.WHITE, weathering := 0.3, algae := 0.6, roughness_scale := 1.0, set_name := "wood") -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/wood_uv.gdshader")
	Camp.bind_prop_pbr(mat, set_name)
	Camp.bind_texture(mat, "noise_tex", NOISE)
	mat.set_shader_parameter("tint", tint)
	mat.set_shader_parameter("weathering", weathering)
	mat.set_shader_parameter("algae_amount", algae)
	mat.set_shader_parameter("roughness_scale", roughness_scale)
	return mat


## Bark for felled timber (the tree bark shader without wind or tint).
static func bark(set_name := "bark_oak", moss := 0.25) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/bark.gdshader")
	Camp.bind_prop_pbr(mat, set_name)
	Camp.bind_texture(mat, "noise_tex", NOISE)
	mat.set_shader_parameter("moss_amount", moss)
	mat.set_shader_parameter("use_instance_custom", false)
	mat.set_shader_parameter("wind_response", 0.0)
	return mat


## Dull blackened iron / weathered steel. Hardware now has its own material
## response rather than borrowing a rock photoscan and turning it metallic.
static func iron(tint := Color(0.16, 0.15, 0.14)) -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/iron.gdshader")
	Camp.bind_texture(mat, "noise_tex", NOISE)
	mat.set_shader_parameter("tint", tint)
	return mat


## Natural-fibre rope. Procedural tube geometry carries U around the cord and V
## along arc length, so the material can render actual twisted strands.
static func rope() -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = load("res://shaders/rope.gdshader")
	mat.set_shader_parameter("lay_turns_per_metre", 5.4)
	mat.set_shader_parameter("strand_count", 3.0)
	mat.set_shader_parameter("groove_depth", 0.18)
	return mat
