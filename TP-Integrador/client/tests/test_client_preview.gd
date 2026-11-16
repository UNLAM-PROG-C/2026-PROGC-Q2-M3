extends SceneTree

## Prueba de humo de la vista previa sin servidor (scenes/debug/client_preview.tscn):
## el flujo completo con los ejemplos del protocolo tiene que seguir funcionando.

const PREVIEW_SCENE := preload("res://scenes/debug/client_preview.tscn")

var _failed := false


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var completed := false
	var preview: Node = PREVIEW_SCENE.instantiate()
	root.add_child(preview)
	await process_frame
	var session: Node = root.get_node("SessionController")
	var world: Node = preview.match_scene
	var hud: Control = world.get_node("CanvasLayer/MatchHud")
	var screens: Control = world.get_node("CanvasLayer/SessionScreens")

	_check(screens.current_screen == "connect", "The preview starts on the connection screen.")
	await preview.quick_start()
	_check(session.get_state_name() == "Running", "Quick start reaches Running with examples 01, 02 and 04.")
	_check(world.active_world != null and hud.visible, "The world and the HUD are shown.")
	_check(_sent_types(preview).has("ClientReady"), "Loading the world sends ClientReady through the simulated network.")

	var applied := 0
	while preview.apply_next_example():
		applied += 1
	_check(applied == preview.GAMEPLAY_EXAMPLES.size(), "Every gameplay example batch is applied.")
	_check(not world.synchronization_failed, "The example batches apply without breaking synchronization.")

	var controller: Node = world.get_node("UnitCommandController")
	controller.set_selected_building(1)
	(hud.find_child("TrainButton", true, false) as Button).pressed.emit()
	_check(_sent_types(preview).has("TrainUnit"), "Training from the HUD sends TrainUnit.")
	_check(preview.answer_last_command(true), "The last command can be accepted.")
	_check(
		str(world.get_own_production(1).get("status", "")) in ["Training", "WaitingForResources", "PendingSpawn"]
			or not world.get_own_production(1).get("pending", []).is_empty(),
		"An accepted TrainUnit updates the producer queue."
	)
	(hud.find_child("TrainButton", true, false) as Button).pressed.emit()
	_check(preview.answer_last_command(false, "Production.QueueFull"), "The last command can be rejected.")
	_check(hud.get_messages().has("La cola de entrenamiento está llena."), "The rejection is shown in the HUD.")
	_check(preview.reject_last_command_before_admission(), "A CommandRejected can be simulated.")
	preview.set_resources(321, 12)
	_check((hud.find_child("WoodLabel", true, false) as Label).text == "321", "Simulated resources reach the HUD.")

	preview.end_match("Victory", session.player_id, "BaseDestroyed")
	_check(screens.current_screen == "result", "Ending the match shows the result screen.")
	_check(
		(screens.get_node("Center/ResultPanel").find_child("Title", true, false) as Label).text == "¡Victoria!",
		"The local player wins."
	)

	preview.queue_free()
	await process_frame
	_check(session.get_state_name() == "AwaitingAssignment", "Leaving the preview resets the session.")
	_check(session._network_client == root.get_node("NetworkClient"), "Leaving the preview restores the real NetworkClient.")
	completed = true

	_check(completed, "The preview smoke test did not complete.")
	if _failed:
		quit(1)
		return
	print("Client preview tests passed.")
	quit(0)


func _sent_types(preview: Node) -> Array:
	var types: Array = []
	for message: Dictionary in preview.network.sent_messages:
		types.append(message["type"])
	return types


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)
