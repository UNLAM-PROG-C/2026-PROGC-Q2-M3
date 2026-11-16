extends SceneTree

## Música: buses separados (Music y Sfx), tema del menú en loop en el menú principal y en la
## conexión sin cortes, canciones aleatorias durante la partida y silencio al terminar.

const MatchMusicScript = preload("res://scripts/match_music.gd")
const MusicManagerScript = preload("res://scripts/audio/music_manager.gd")

## Valores de SessionController.State.
const RUNNING := 4
const ENDED := 5
const EXPECTED_TESTS := 4

var _failed := false
var _completed_tests := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var music: Node = root.get_node_or_null("MusicManager")
	_check(music != null, "MusicManager is an autoload.")
	if music == null:
		quit(1)
		return
	music.fade_seconds = 0.1
	_test_buses_and_volume(music)
	await _test_menu_theme_across_scenes(music)
	await _test_match_playlist(music)
	_test_state_mapping()
	_check(_completed_tests == EXPECTED_TESTS, "Only %d of %d tests completed." % [_completed_tests, EXPECTED_TESTS])
	if _failed:
		quit(1)
		return
	print("Match music tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _test_buses_and_volume(music: Node) -> void:
	var music_bus := AudioServer.get_bus_index("Music")
	var sfx_bus := AudioServer.get_bus_index("Sfx")
	_check(music_bus > 0 and sfx_bus > 0, "The project has separate Music and Sfx buses.")
	_check(
		AudioServer.get_bus_send(music_bus) == &"Master" and AudioServer.get_bus_send(sfx_bus) == &"Master",
		"Both buses send to Master."
	)
	var player := music.get_node("MusicPlayer") as AudioStreamPlayer
	_check(player.bus == &"Music", "Music plays on the Music bus.")
	# AudioSettings carga los volúmenes guardados del jugador: se compara contra el valor previo.
	var sfx_volume_before := AudioServer.get_bus_volume_db(sfx_bus)
	music.set_music_volume(0.5)
	_check(is_equal_approx(music.get_music_volume(), 0.5), "The music volume can be changed.")
	_check(is_equal_approx(AudioServer.get_bus_volume_db(sfx_bus), sfx_volume_before), "Changing the music volume does not touch sound effects.")
	music.set_music_muted(true)
	_check(music.is_music_muted() and not AudioServer.is_bus_mute(sfx_bus), "Music can be muted on its own.")
	music.set_music_muted(false)
	music.set_music_volume(1.0)
	_completed_tests += 1


func _test_menu_theme_across_scenes(music: Node) -> void:
	var started: Array[String] = []
	var on_started := func(song: String) -> void: started.append(song)
	music.song_started.connect(on_started)
	change_scene_to_file("res://scenes/main_menu.tscn")
	await process_frame
	await process_frame
	_check(music.mode == "menu" and music.current_song == music.MENU_THEME, "The main menu plays Vadodora Chill Mix.")
	var player := music.get_node("MusicPlayer") as AudioStreamPlayer
	_check(player.stream != null and bool(player.stream.get("loop")), "The menu theme loops.")
	_check(
		not bool((load(music.MENU_THEME) as AudioStream).get("loop")),
		"The imported song is not changed: the loop is on a copy."
	)
	_check(started == [music.MENU_THEME], "The menu theme started once.")

	(current_scene.find_child("PlayButton", true, false) as Button).pressed.emit()
	await process_frame
	await process_frame
	_check(current_scene.scene_file_path == "res://scenes/match_world.tscn", "Jugar opens the match scene.")
	_check(music.mode == "menu" and started.size() == 1, "The connection screen keeps the menu theme without restarting it.")
	music.song_started.disconnect(on_started)
	_completed_tests += 1


func _test_match_playlist(music: Node) -> void:
	var session: Node = root.get_node("SessionController")
	var started: Array[String] = []
	music.song_started.connect(func(song: String) -> void: started.append(song))
	session._set_state(RUNNING)
	_check(music.mode == "match", "The match switches to the match playlist.")
	await create_timer(music.fade_seconds + 0.2).timeout
	_check(started.size() == 1 and started[0] != music.MENU_THEME, "After fading out the menu theme, a different song starts.")
	_check(music.songs().size() == 8, "The playlist has the 8 songs of the music folder.")

	var player := music.get_node("MusicPlayer") as AudioStreamPlayer
	for _index in range(15):
		player.finished.emit()
	_check(started.size() == 16, "Each finished song starts another one.")
	_check(_distinct(started.slice(0, 8)) == 8 and _distinct(started.slice(8, 16)) == 8, "Every song plays before any repeats.")
	for index in range(1, started.size()):
		_check(started[index] != started[index - 1], "The same song never plays twice in a row.")
	_check(not bool(player.stream.get("loop")), "Match songs do not loop: they move on to the next one.")

	session._set_state(ENDED)
	await create_timer(music.fade_seconds + 0.2).timeout
	_check(not music.is_playing() and not player.playing, "The result screen is silent after a fade.")
	_check(is_equal_approx(player.volume_db, music.volume_db), "The volume is restored for the next song.")

	_check(session.reset_session(), "The session can go back to the connection screen.")
	_check(music.mode == "menu" and music.current_song == music.MENU_THEME, "Back on the connection screen the menu theme plays again.")
	_completed_tests += 1


func _test_state_mapping() -> void:
	var expected := {
		"AwaitingAssignment": "menu", "WaitingForOpponent": "menu", "Preparing": "menu", "ReadySent": "menu",
		"Running": "match", "Ended": "", "Disconnected": "",
	}
	for state_name: String in expected:
		_check(MatchMusicScript.music_for_state(state_name) == expected[state_name], "%s → %s." % [state_name, expected[state_name]])
	_completed_tests += 1


func _distinct(values: Array) -> int:
	var seen: Dictionary = {}
	for value: Variant in values:
		seen[value] = true
	return seen.size()
