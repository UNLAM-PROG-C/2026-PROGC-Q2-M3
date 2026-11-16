extends SceneTree

## Opciones de audio: AudioSettings (buses Music y Sfx, guardado) y el panel del menú principal.

const TEST_SETTINGS := "user://test_options_settings.cfg"

var _failed := false


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var completed := false
	var settings: Node = root.get_node_or_null("AudioSettings")
	_check(settings != null, "AudioSettings is an autoload.")
	if settings == null:
		quit(1)
		return
	# Un archivo propio de la prueba: nunca se tocan los ajustes guardados del jugador.
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SETTINGS))
	settings.settings_path = TEST_SETTINGS
	settings.load_settings()
	_check(is_equal_approx(settings.get_volume("Music"), 1.0) and is_equal_approx(settings.get_volume("Sfx"), 1.0), "Without saved settings both volumes are full.")

	settings.set_volume("Music", 0.5)
	_check(is_equal_approx(AudioServer.get_bus_volume_db(AudioServer.get_bus_index("Music")), linear_to_db(0.5)), "The music volume changes the Music bus.")
	_check(is_equal_approx(settings.get_volume("Sfx"), 1.0), "Changing the music does not change the sounds.")
	settings.set_volume("Sfx", 0.0)
	_check(AudioServer.is_bus_mute(AudioServer.get_bus_index("Sfx")) and is_zero_approx(settings.get_volume("Sfx")), "Volume 0 mutes the bus.")
	settings.set_volume("Music", 1.0, false)
	settings.set_volume("Sfx", 1.0, false)
	settings.load_settings()
	_check(is_equal_approx(settings.get_volume("Music"), 0.5) and is_zero_approx(settings.get_volume("Sfx")), "Saved volumes are restored on the next start.")

	settings.set_volume("Music", 0.4)
	settings.set_volume("Sfx", 0.8)
	change_scene_to_file("res://scenes/main_menu.tscn")
	await process_frame
	await process_frame
	var menu := current_scene
	var options_button := menu.find_child("OptionsButton", true, false) as Button
	var options := menu.find_child("OptionsMenu", true, false) as Control
	_check(options_button != null and options_button.text == "Opciones", "The main menu has an Opciones button.")
	_check(options != null and not options.visible, "The options start closed.")
	if options_button != null and options != null:
		options_button.pressed.emit()
		_check(options.visible, "Opciones opens the options panel.")
		var music_slider := options.find_child("MusicSlider", true, false) as HSlider
		var sfx_slider := options.find_child("SfxSlider", true, false) as HSlider
		_check(music_slider != null and sfx_slider != null, "There is one slider for music and one for sounds.")
		_check(music_slider.value == 40.0 and sfx_slider.value == 80.0, "The sliders show the current volumes.")
		_check((options.find_child("MusicSliderValue", true, false) as Label).text == "40%", "The music volume is shown as a percentage.")
		music_slider.value = 25.0
		sfx_slider.value = 0.0
		_check(is_equal_approx(settings.get_volume("Music"), 0.25), "Moving the music slider changes the music volume at once.")
		_check(is_zero_approx(settings.get_volume("Sfx")), "Moving the sounds slider changes the sounds volume at once.")
		_check((options.find_child("SfxSliderValue", true, false) as Label).text == "0%", "The sounds percentage follows the slider.")
		(options.find_child("BackButton", true, false) as Button).pressed.emit()
		_check(not options.visible, "Volver closes the options.")
		var saved := ConfigFile.new()
		_check(saved.load(TEST_SETTINGS) == OK, "Closing the options saves them.")
		_check(
			is_equal_approx(float(saved.get_value("audio", "music")), 0.25) and is_zero_approx(float(saved.get_value("audio", "sfx"))),
			"The saved volumes are the chosen ones."
		)
		options_button.pressed.emit()
		var escape := InputEventAction.new()
		escape.action = "ui_cancel"
		escape.pressed = true
		root.push_input(escape)
		_check(not options.visible, "Escape also closes the options.")
	completed = true

	settings.set_volume("Music", 1.0, false)
	settings.set_volume("Sfx", 1.0, false)
	DirAccess.remove_absolute(ProjectSettings.globalize_path(TEST_SETTINGS))
	_check(completed, "The options test did not complete.")
	if _failed:
		quit(1)
		return
	print("Options tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)
