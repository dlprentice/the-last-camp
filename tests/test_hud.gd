extends TestCase


## Leaving photo mode or the pause menu returns to play; the title card must
## not replay over the game each time (it did whenever it had faded out).
func test_title_card_plays_only_once() -> void:
	var tree := Engine.get_main_loop() as SceneTree
	var hud := Hud.new()
	tree.root.add_child(hud)
	var previous := Game.mode
	Game.mode = Game.Mode.INTRO
	Game.mode = Game.Mode.PLAY
	assert_true(hud.title_played, "first entry into play starts the title card")
	var running := tree.get_processed_tweens().size()
	hud._title.modulate.a = 0.0
	hud._hints.modulate.a = 0.0
	Game.mode = Game.Mode.PHOTO
	Game.mode = Game.Mode.PLAY
	assert_eq(tree.get_processed_tweens().size(), running, "no fresh title tween after leaving photo mode")
	Game.mode = Game.Mode.PAUSED
	Game.mode = Game.Mode.PLAY
	assert_eq(tree.get_processed_tweens().size(), running, "no fresh title tween after the menu")
	assert_eq(hud._title.modulate.a, 0.0, "title stays hidden when play resumes")
	Game.mode = previous
	hud.free()
