extends SceneTree
## Headless test runner.
##
## Usage: godot --headless --path <project> --script res://tests/run_tests.gd
## Loads every res://tests/test_*.gd (except the TestCase base class), calls
## run() on an instance of each and exits with code 1 if anything failed.

const TESTS_DIR := "res://tests/"
const BASE_CLASS_FILE := "test_case.gd"
## Stopped audio playbacks are released by the mixer thread, not synchronously;
## quitting immediately after the tests would report them as leaked instances.
const TEARDOWN_GRACE_SECONDS := 0.25


func _initialize() -> void:
	_run_all()


func _run_all() -> void:
	# Wait one frame so the root window is inside the tree and autoloads are set up.
	await process_frame

	var total_passed := 0
	var total_failed := 0
	var files := _discover_test_files()
	if files.is_empty():
		push_error("No test files found in %s" % TESTS_DIR)

	for file in files:
		var path := TESTS_DIR + file
		var script := load(path) as GDScript
		if script == null or not script.can_instantiate():
			push_error("Could not load test script %s" % path)
			total_failed += 1
			continue
		var instance: Object = script.new()
		if instance == null or not instance.has_method("run"):
			push_error("%s does not implement run()" % path)
			total_failed += 1
			continue

		var started := Time.get_ticks_msec()
		var result: Dictionary = instance.call("run")
		var passed: int = result.get("passed", 0)
		var failed: int = result.get("failed", 0)
		var failures: Array = result.get("failures", [])
		total_passed += passed
		total_failed += failed
		print("%s: %d passed, %d failed (%d ms)" % [file, passed, failed, Time.get_ticks_msec() - started])
		for failure: String in failures:
			print("  FAIL %s" % failure)

	var all_ok := total_failed == 0 and not files.is_empty()
	print("----")
	print("%s: %d passed, %d failed" % ["OK" if all_ok else "FAILED", total_passed, total_failed])

	await create_timer(TEARDOWN_GRACE_SECONDS).timeout
	quit(0 if all_ok else 1)


func _discover_test_files() -> Array[String]:
	var files: Array[String] = []
	for file in DirAccess.get_files_at(TESTS_DIR):
		if file.begins_with("test_") and file.ends_with(".gd") and file != BASE_CLASS_FILE:
			files.append(file)
	files.sort()
	return files
