extends SceneTree

## Prints what Godot imported from every models/<name>/<name>.gltf: mesh
## instances, surfaces, triangle counts, LOD levels and material textures.
##   godot --headless --path . --script res://tools/inspect_models.gd


func _init() -> void:
	var dir := DirAccess.open("res://models")
	var names: Array[String] = []
	for sub in dir.get_directories():
		if FileAccess.file_exists("res://models/%s/%s.gltf" % [sub, sub]):
			names.append(sub)
	names.sort()
	for f in names:
		var scene: PackedScene = load("res://models/%s/%s.gltf" % [f, f])
		if scene == null:
			print("%-28s FAILED to load" % f)
			continue
		var root := scene.instantiate()
		var meshes: Array[MeshInstance3D] = []
		_collect(root, meshes)
		for mi in meshes:
			var mesh := mi.mesh
			var line := "%-28s node=%s surfaces=%d" % [f, mi.name, mesh.get_surface_count()]
			var aabb := mesh.get_aabb()
			line += " size=(%.2f %.2f %.2f)" % [aabb.size.x, aabb.size.y, aabb.size.z]
			for s in mesh.get_surface_count():
				var arrays := mesh.surface_get_arrays(s)
				var idx: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
				var tris := idx.size() / 3 if idx.size() > 0 else (arrays[Mesh.ARRAY_VERTEX] as PackedVector3Array).size() / 3
				var lods := 0
				if mesh is ArrayMesh:
					lods = (mesh as ArrayMesh).surface_get_lod_count(s) if (mesh as ArrayMesh).has_method("surface_get_lod_count") else -1
				var mat := mesh.surface_get_material(s)
				var mat_desc := "none"
				if mat is BaseMaterial3D:
					var sm := mat as BaseMaterial3D
					var rough_or_orm := (sm as ORMMaterial3D).orm_texture != null if sm is ORMMaterial3D else (sm as StandardMaterial3D).roughness_texture != null
					mat_desc = "%s albedo=%s normal=%s rough/orm=%s alpha=%d cull=%d" % [mat.get_class(), sm.albedo_texture != null, sm.normal_enabled, rough_or_orm, sm.transparency, sm.cull_mode]
				line += "\n    surface %d: tris=%d lods=%d %s" % [s, tris, lods, mat_desc]
			print(line)
		root.free()
	quit()


func _collect(node: Node, out: Array[MeshInstance3D]) -> void:
	if node is MeshInstance3D:
		out.append(node)
	for c in node.get_children():
		_collect(c, out)
