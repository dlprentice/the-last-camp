// Trunk bending shared by bark and foliage so canopies never detach from
// their branches. Operates on tree-local vertices; `wind` is the horizontal
// wind displacement at the tree base (see common.gdshaderinc::wind_field).

vec3 tree_bend(vec3 local, float tree_height, vec2 wind) {
	float h = clamp(local.y / max(tree_height, 0.1), 0.0, 1.0);
	// Quadratic profile: the base is anchored, the crown moves most.
	float bend = h * h;
	vec2 offset = wind * bend * tree_height * 0.018;
	local.xz += offset;
	// Preserve length approximately so the crown does not visibly stretch.
	local.y -= dot(offset, offset) / max(tree_height, 0.1) * 0.5;
	return local;
}
