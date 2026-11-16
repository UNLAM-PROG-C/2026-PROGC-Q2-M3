extends SceneTree

## Menú de la partida: la acción pause_menu (Escape) abre el volumen de música y sonidos con
## un botón para volver a la partida.

const TEST_SETTINGS := "user://test_pause_settings.cfg"
## Valores de SessionController.State.
const RUNNING := 4
const ENDED := 5

var _failed := false


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var completed := false
	var settings: Node = root.get_node("AudioSettings")
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SETTINGS))
	settings.settings_path = TEST_SETTINGS
	settings.load_settings()
	var session: Node = root.get_node("SessionController")

	change_scene_to_file("res://scenes/match_world.tscn")
	await process_frame
	await process_frame
	var world := current_scene
	world._session_controller = null
	_check(world.prepare_initial_state(1, _initial_state()), "The match world should load the example state.")
	var pause_menu := world.get_node("CanvasLayer/PauseMenu") as Control
	var placement: Node = world.get_node("BuildPlacementController")
	var controller: Node = world.get_node("UnitCommandController")

	_press_escape()
	_check(not pause_menu.visible, "Outside a running match Escape does not open the menu.")

	session._set_state(RUNNING)
	_check(InputMap.has_action("pause_menu"), "The menu is opened with the pause_menu action.")
	_press_escape()
	_check(pause_menu.visible, "During the match Escape opens the menu.")
	var back := pause_menu.find_child("BackButton", true, false) as Button
	_check(back != null and back.text == "Volver a la partida", "The menu has a button to go back to the match.")
	var music_slider := pause_menu.find_child("MusicSlider", true, false) as HSlider
	var sfx_slider := pause_menu.find_child("SfxSlider", true, false) as HSlider
	_check(music_slider != null and sfx_slider != null, "The menu has the music and sounds sliders.")
	_check(pause_menu.theme != null, "The menu uses the game theme.")
	music_slider.value = 30.0
	sfx_slider.value = 60.0
	_check(is_equal_approx(settings.get_volume("Music"), 0.3) and is_equal_approx(settings.get_volume("Sfx"), 0.6), "The sliders change the volumes during the match.")

	# Con el menú abierto, los atajos de la partida no se disparan.
	controller.set_selected_units(_ids([3]))
	_press_action_key("build_farm")
	_check(not placement.is_placing(), "Game shortcuts do not reach the match while the menu is open.")

	back.pressed.emit()
	_check(not pause_menu.visible, "Volver a la partida closes the menu.")
	var saved := ConfigFile.new()
	_check(saved.load(TEST_SETTINGS) == OK and is_equal_approx(float(saved.get_value("audio", "music")), 0.3), "The volumes chosen during the match are saved.")

	_press_escape()
	_check(pause_menu.visible, "Escape opens the menu again.")
	_press_escape()
	_check(not pause_menu.visible, "Escape also closes it.")

	# Colocando un edificio, Escape cancela la colocación y no abre el menú.
	_check(placement.start_placement("Farm"), "A worker can start placing a farm.")
	_press_escape()
	_check(not placement.is_placing(), "Escape cancels the placement.")
	_check(not pause_menu.visible, "Cancelling a placement does not open the menu.")

	_press_escape()
	session._set_state(ENDED)
	_check(not pause_menu.visible, "If the match ends, the menu closes so the result can be seen.")
	completed = true

	session.reset_session()
	settings.set_volume("Music", 1.0, false)
	settings.set_volume("Sfx", 1.0, false)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SETTINGS))
	_check(completed, "The pause menu test did not complete.")
	if _failed:
		quit(1)
		return
	print("Pause menu tests passed.")
	quit(0)


func _press_escape() -> void:
	var key := InputEventKey.new()
	key.physical_keycode = KEY_ESCAPE
	key.keycode = KEY_ESCAPE
	key.pressed = true
	root.push_input(key)
	var release := key.duplicate() as InputEventKey
	release.pressed = false
	root.push_input(release)


func _press_action_key(action: String) -> void:
	for input_event: InputEvent in InputMap.action_get_events(action):
		if input_event is InputEventKey:
			var key := input_event.duplicate() as InputEventKey
			key.pressed = true
			root.push_input(key)
			return


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _ids(values: Array) -> Array[int]:
	var result: Array[int] = []
	for value: Variant in values:
		result.append(int(value))
	return result


func _initial_state() -> Dictionary:
	var path := ProjectSettings.globalize_path("res://../docs/protocol/examples/server-to-client/02-initial-state.json")
	return _with_integer_numbers(JSON.parse_string(FileAccess.get_file_as_string(path))["payload"])


func _with_integer_numbers(value: Variant) -> Variant:
	match typeof(value):
		TYPE_DICTIONARY:
			var result: Dictionary = {}
			for key: Variant in value:
				result[key] = _with_integer_numbers(value[key])
			return result
		TYPE_ARRAY:
			var result: Array = []
			for item: Variant in value:
				result.append(_with_integer_numbers(item))
			return result
		TYPE_FLOAT:
			if is_equal_approx(value, roundf(value)) and absf(value) < 1e15:
				return int(value)
	return value
