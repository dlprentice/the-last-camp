@tool
extends EditorScript

## Forces a reimport of every texture in textures/ through the editor's
## filesystem, so changed sources and changed .import settings are honoured
## even when the headless scanner considers them up to date:
##   godot --headless --editor --script res://tools/reimport_textures.gd

func _run() -> void:
	var files := PackedStringArray()
	var dir := DirAccess.open("res://textures")
	if dir == null:
		push_error("textures/ not found")
		return
	for name in dir.get_files():
		if name.ends_with(".png"):
			files.append("res://textures/%s" % name)
	files.sort()
	# The editor has scanned the project by the time a script runs; a fresh
	# scan here would wait on a thread the blocked main loop never services.
	var fs := EditorInterface.get_resource_filesystem()
	print("REIMPORT_START %d files" % files.size())
	fs.reimport_files(files)
	print("REIMPORT_DONE")
