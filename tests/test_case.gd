class_name TestCase
extends RefCounted
## Minimal base class for headless unit tests.
##
## Subclasses define methods whose names start with [code]test_[/code]; [method run]
## discovers and executes them in alphabetical order. Assertions never abort a test,
## they only record a message in [member failures], so one test can report several
## problems at once.

var failures: Array[String] = []

var _current_test := ""


func assert_true(condition: bool, message: String) -> void:
	if not condition:
		_fail(message)


func assert_false(condition: bool, message: String) -> void:
	if condition:
		_fail(message)


func assert_eq(a: Variant, b: Variant, message: String) -> void:
	if a != b:
		_fail("%s (expected %s, got %s)" % [message, str(b), str(a)])


func assert_near(a: float, b: float, tolerance: float, message: String) -> void:
	if not is_finite(a) or not is_finite(b) or absf(a - b) > tolerance:
		_fail("%s (expected %s within %s, got %s)" % [message, str(b), str(tolerance), str(a)])


func assert_gt(a: float, b: float, message: String) -> void:
	if not a > b:
		_fail("%s (expected %s > %s)" % [message, str(a), str(b)])


func assert_lt(a: float, b: float, message: String) -> void:
	if not a < b:
		_fail("%s (expected %s < %s)" % [message, str(a), str(b)])


## Runs every [code]test_*[/code] method and returns
## [code]{passed: int, failed: int, failures: Array[String]}[/code].
func run() -> Dictionary:
	var names: Array[String] = []
	for method in get_method_list():
		var method_name: String = method["name"]
		if method_name.begins_with("test_") and not names.has(method_name):
			names.append(method_name)
	names.sort()

	var passed := 0
	var failed := 0
	for method_name in names:
		var failures_before := failures.size()
		_current_test = method_name
		call(method_name)
		_current_test = ""
		if failures.size() == failures_before:
			passed += 1
		else:
			failed += 1
	return {"passed": passed, "failed": failed, "failures": failures.duplicate()}


func _fail(message: String) -> void:
	var prefix := _current_test + ": " if not _current_test.is_empty() else ""
	failures.append(prefix + message)
