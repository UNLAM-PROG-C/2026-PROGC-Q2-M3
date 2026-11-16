extends SceneTree

## Menú principal: personajes de decorado en idle con loop y botón Jugar hacia match_world.

const MAIN_MENU_PATH := "res://scenes/main_menu.tscn"
const MATCH_SCENE_PATH := "res://scenes/match_world.tscn"
const KNIGHT_PATH := "res://assets/units/mini_characters/character-knight.glb"
const MALE_B_PATH := "res://assets/units/mini_characters/character-male-b.glb"

var _failed := false


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var tests_completed := 0
	_check(
		ProjectSettings.get_setting("application/run/main_scene") == ResourceUID.path_to_uid(MAIN_MENU_PATH)
			or ProjectSettings.get_setting("application/run/main_scene") == MAIN_MENU_PATH,
		"The main menu should be the project's main scene."
	)
	var error := change_scene_to_file(MAIN_MENU_PATH)
	_check(error == OK, "The main menu scene should load.")
	await process_frame
	await process_frame
	var menu := current_scene
	_check(menu != null and menu.scene_file_path == MAIN_MENU_PATH, "The main menu should be the current scene.")
	if menu != null:
		_check(menu.find_child("TestUnits*", true, false) == null, "The debug TestUnits must be gone.")
		var scaffoldings := menu.find_children("Scaffolding", "Node3D", true, false)
		_check(not scaffoldings.is_empty(), "The menu buildings come with their scaffolding node.")
		for scaffolding: Node in scaffoldings:
			_check(not (scaffolding as Node3D).visible, "No scaffolding is visible in the main menu (%s)." % scaffolding.get_parent().name)
		_check_idle_character(menu, "Knight", KNIGHT_PATH)
		_check_idle_character(menu, "MaleB", MALE_B_PATH)
		var play_button := menu.find_child("PlayButton", true, false) as Button
		_check(play_button != null and play_button.text == "Jugar", "The menu should have a Jugar button.")
		var title := menu.find_child("GameTitle", true, false) as Label
		_check(title != null and title.text == "Age of Concurrency", "The menu shows the game title.")
		if title != null and play_button != null:
			_check(title.theme_type_variation == &"TitleLabel", "The title uses the title font (Magic Retro).")
			_check(title.get_global_rect().end.y <= play_button.get_global_rect().position.y, "The title is above the buttons.")
			var options_button := menu.find_child("OptionsButton", true, false) as Control
			_check(
				is_equal_approx(play_button.get_global_rect().position.x, title.get_global_rect().position.x)
					and is_equal_approx(options_button.get_global_rect().position.x, title.get_global_rect().position.x),
				"The buttons are aligned to the left, under the start of the title."
			)
		var buttons := menu.find_child("Buttons", true, false) as Control
		if buttons != null:
			var screen := root.get_visible_rect().size
			var rect := buttons.get_global_rect()
			_check(rect.end.x < screen.x / 2.0, "The menu buttons are on the left side: %s" % str(rect))
			_check(absf(rect.get_center().y - screen.y / 2.0) < 2.0, "The menu buttons are vertically centered: %s" % str(rect))
		if play_button != null:
			play_button.pressed.emit()
			await process_frame
			await process_frame
			_check(
				current_scene != null and current_scene.scene_file_path == MATCH_SCENE_PATH,
				"Jugar should open match_world."
			)
			var screens := current_scene.get_node_or_null("CanvasLayer/SessionScreens")
			_check(screens != null and screens.current_screen == "connect", "match_world should open on the connection screen.")
			var back_button := screens.find_child("MainMenuButton", true, false) as Button if screens != null else null
			_check(
				back_button != null and back_button.visible and back_button.text == "Menú principal",
				"The connection screen should offer a button back to the main menu."
			)
			if back_button != null:
				back_button.pressed.emit()
				await process_frame
				await process_frame
				_check(
					current_scene != null and current_scene.scene_file_path == MAIN_MENU_PATH,
					"The main menu button should return to the main menu."
				)
		tests_completed = 1
	_check(tests_completed == 1, "The main menu test did not complete.")
	if _failed:
		quit(1)
		return
	print("Main menu tests passed.")
	quit(0)


func _check_idle_character(menu: Node, node_name: String, model_path: String) -> void:
	var character := menu.get_node_or_null(node_name)
	_check(character != null, "%s should be in the main menu." % node_name)
	if character == null:
		return
	var model := character.get_node_or_null("Model")
	_check(model != null and model.scene_file_path == model_path, "%s should use %s." % [node_name, model_path.get_file()])
	var player := character.find_child("AnimationPlayer", true, false) as AnimationPlayer
	_check(player != null and player.is_playing(), "%s should be animated." % node_name)
	if player == null:
		return
	_check(player.current_animation == "decor/idle", "%s should play idle." % node_name)
	_check(
		player.get_animation(player.current_animation).loop_mode == Animation.LOOP_LINEAR,
		"%s idle should loop." % node_name
	)
	_check(
		player.get_animation("idle").loop_mode == Animation.LOOP_NONE,
		"The imported idle animation shared with game units must stay unchanged."
	)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)
