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
		var title := menu.find_child("GameTitle", true, false) as Control
		var title_lines: Array[Label] = []
		if title != null:
			for child: Node in title.get_children():
				if child is Label:
					title_lines.append(child)
		_check(
			title_lines.map(func(line: Label) -> String: return line.text) == ["Age", "of", "Concurrency"],
			"The menu shows the game title in three lines: Age / of / Concurrency."
		)
		for line: Label in title_lines:
			var font := line.get_theme_font("font")
			_check(
				font != null and font.resource_path.ends_with("JunigardenSwash_PERSONAL_USE_ONLY.otf"),
				"The title uses the Junigarden Swash font (%s)." % line.text
			)
			_check(
				line.get_theme_color("font_shadow_color").a > 0.0
					and line.get_theme_constant("shadow_offset_x") > 0
					and line.get_theme_constant("shadow_offset_y") > 0,
				"The title text has a shadow (%s)." % line.text
			)
			# Todas ocupan el ancho de "Concurrency": centradas, "Age" y "of" quedan al medio.
			if line.text != "Concurrency":
				_check(line.horizontal_alignment == HORIZONTAL_ALIGNMENT_CENTER, "The title lines are centered (%s)." % line.text)
		if title_lines.size() == 3:
			var of_size := title_lines[1].get_theme_font_size("font_size")
			_check(
				of_size < title_lines[0].get_theme_font_size("font_size") and of_size < title_lines[2].get_theme_font_size("font_size"),
				"The word of is smaller than Age and Concurrency."
			)
		var credits := menu.find_child("Credits", true, false) as Label
		_check(credits != null, "The menu shows the team credits.")
		if credits != null:
			for member: String in [
				"Iván Abaca", "Joaquín Barcella", "Augusto Coronati", "Luca Gonzalez", "Florencia Licarzi", "Agustín Zabalgoitia"
			]:
				_check(credits.text.contains(member), "The credits name %s." % member)
			_check(credits.get_theme_color("font_color") == Color.WHITE, "The credits are white.")
			var screen := credits.get_viewport_rect().size
			var credits_rect := credits.get_global_rect()
			_check(
				credits_rect.end.x > screen.x * 0.95 and credits_rect.end.x <= screen.x
					and credits_rect.end.y > screen.y * 0.9 and credits_rect.end.y <= screen.y,
				"The credits are in the bottom right corner."
			)
			_check(credits.mouse_filter == Control.MOUSE_FILTER_IGNORE, "The credits do not block clicks.")
			var options_panel := menu.find_child("OptionsMenu", true, false)
			_check(options_panel == null or options_panel.is_greater_than(credits), "The options panel covers the credits.")
		var emblem := menu.find_child("TitleEmblem", true, false) as TextureRect
		_check(
			emblem != null and emblem.texture != null and emblem.texture.resource_path.ends_with("title_emblem.png"),
			"The title has the shield emblem."
		)
		if emblem != null and title != null:
			_check(emblem.is_greater_than(title) == false and title.is_greater_than(emblem), "The emblem is drawn behind the title.")
			_check(
				emblem.get_global_rect().get_center().distance_to(title.get_global_rect().get_center()) <= 1.0,
				"The emblem is centered behind the title."
			)
			_check(emblem.mouse_filter == Control.MOUSE_FILTER_IGNORE, "The emblem does not block clicks.")
			var emblem_shadow := menu.find_child("TitleEmblemShadow", true, false) as TextureRect
			_check(
				emblem_shadow != null and emblem_shadow.texture == emblem.texture and emblem.is_greater_than(emblem_shadow),
				"The emblem has a shadow drawn behind it."
			)
			if emblem_shadow != null:
				var shadow_shift := emblem_shadow.get_global_rect().position - emblem.get_global_rect().position
				_check(shadow_shift.x > 0.0 and shadow_shift.y > 0.0, "The emblem shadow falls down and to the right.")
				_check(emblem_shadow.self_modulate.a < 1.0 and emblem_shadow.self_modulate.v < 0.2, "The emblem shadow is dark and translucent.")
				_check(emblem_shadow.mouse_filter == Control.MOUSE_FILTER_IGNORE, "The emblem shadow does not block clicks.")
		if title != null and play_button != null:
			_check(title.get_global_rect().end.y <= play_button.get_global_rect().position.y, "The title is above the buttons.")
			var options_button := menu.find_child("OptionsButton", true, false) as Control
			var title_center := title.get_global_rect().get_center().x
			_check(
				# Hasta 1 px de diferencia: Godot redondea las posiciones a píxeles enteros.
				absf(play_button.get_global_rect().get_center().x - title_center) <= 1.0
					and absf(options_button.get_global_rect().get_center().x - title_center) <= 1.0,
				"The buttons are centered under the title."
			)
		var buttons := menu.find_child("Buttons", true, false) as Control
		if buttons != null:
			var screen := root.get_visible_rect().size
			var rect := buttons.get_global_rect()
			_check(rect.end.x < screen.x / 2.0, "The menu buttons are on the left side: %s" % str(rect))
			# El bloque (título y botones) está centrado en el área que deja el margen inferior,
			# así queda un poco más arriba que el centro de la pantalla.
			var title_area := menu.find_child("TitleArea", true, false) as Control
			var options_button := menu.find_child("OptionsButton", true, false) as Control
			if title_area != null and options_button != null:
				var block_center := (title_area.get_global_rect().position.y + options_button.get_global_rect().end.y) / 2.0
				_check(absf(block_center - rect.get_center().y) < 2.0, "The title and buttons are centered in their area: %s" % str(rect))
				_check(block_center < screen.y / 2.0 - 20.0, "The title and buttons sit above the screen center.")
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
