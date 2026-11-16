extends Node

## Música en la escena de partida, según el estado de la sesión (la reproduce MusicManager):
## - Running: canciones de la partida en orden aleatorio;
## - Ended o Disconnected (pantalla de resultado o de cierre): silencio;
## - el resto (conexión, espera del rival, carga): el tema del menú, que sigue sonando sin
##   cortes desde el menú principal.


var _session: Node


func _ready() -> void:
	_session = get_node_or_null("/root/SessionController")
	if _session == null or not _session.has_signal("session_state_changed"):
		_session = null
		return
	_session.session_state_changed.connect(_on_session_state_changed)
	_on_session_state_changed(0, str(_session.call("get_state_name")))


func _exit_tree() -> void:
	# La sesión es un autoload: al salir de la escena hay que dejar de escucharla.
	if is_instance_valid(_session) and _session.session_state_changed.is_connected(_on_session_state_changed):
		_session.session_state_changed.disconnect(_on_session_state_changed)


static func music_for_state(state_name: String) -> String:
	match state_name:
		"Running":
			return "match"
		"Ended", "Disconnected":
			return ""
	return "menu"


func _on_session_state_changed(_state: int, state_name: String) -> void:
	var music := get_node_or_null("/root/MusicManager")
	if music == null:
		return
	match music_for_state(state_name):
		"match":
			music.play_match_playlist()
		"menu":
			music.play_menu_theme()
		_:
			music.stop_music()
