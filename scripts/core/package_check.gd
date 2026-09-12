extends RefCounted
## Checks the executable's embedded credits and writes its own engine notices.
## Invoked through -- --package-check --notices=<fresh file>; release templates
## do not support the editor-only --script command line option.

static func run(notice_path: String) -> int:
	var cinematic = load("res://scripts/core/cinematic.gd")
	var surfaces: Array = cinematic._credit_rows("res://textures/SOURCES.md", 1, 2, 4)
	var models: Array = cinematic._credit_rows("res://models/SOURCES.md", 1, 2, 4)
	var sounds: Array = cinematic._audio_credit_rows()
	if surfaces.size() != 9 or models.size() != 35 or sounds.size() != 6:
		push_error("Export omitted complete runtime credits: %s/%s/%s" % [surfaces.size(), models.size(), sounds.size()])
		return 1
	for path in ["res://LICENSE", "res://THIRD_PARTY_NOTICES.md"]:
		if not FileAccess.file_exists(path):
			push_error("Export omitted " + path)
			return 1
	for path in ["res://AGENTS.md", "res://docs/development.md"]:
		if FileAccess.file_exists(path):
			push_error("Export contains development-only notes: " + path)
			return 1
	if notice_path.is_empty() or FileAccess.file_exists(notice_path):
		push_error("Provide --notices with a fresh output filename")
		return 1
	var file := FileAccess.open(notice_path, FileAccess.WRITE)
	if file == null:
		push_error("Cannot write exported engine notices")
		return 1
	file.store_string("Godot Engine " + Engine.get_version_info().string + "\nhttps://godotengine.org/license/\n\n")
	file.store_string(Engine.get_license_text() + "\n\nThird-party copyrights and component licenses\n\n")
	file.store_string(JSON.stringify(Engine.get_copyright_info(), "\t") + "\n\nLicense texts\n\n")
	file.store_string(JSON.stringify(Engine.get_license_info(), "\t") + "\n")
	file.close()
	print("EXPORT_CHECK result=PASS surfaces=9 models=35 recordings=6 notices=complete")
	return 0
