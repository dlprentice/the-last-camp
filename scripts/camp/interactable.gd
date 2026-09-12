class_name Interactable
extends StaticBody3D

## A collider the player can use. `prompt_text` is shown on the HUD; `on_interact`
## is invoked with the player node.

var prompt_text := ""
var on_interact: Callable


func prompt() -> String:
	if on_interact.is_valid() and prompt_text.is_empty() == false:
		return prompt_text
	return prompt_text


func interact(player: Node) -> void:
	if on_interact.is_valid():
		on_interact.call(player)
