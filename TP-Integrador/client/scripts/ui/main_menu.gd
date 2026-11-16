extends Control

## Menú principal: el botón Jugar abre la escena de partida, donde están la conexión
## al servidor y las pantallas de sesión.

const MATCH_SCENE_PATH := "res://scenes/match_world.tscn"

@onready var _play_button: Button = %PlayButton


func _ready() -> void:
	_play_button.pressed.connect(start_game)
	_play_button.grab_focus()
	# El tema del menú sigue sonando en la pantalla de conexión (MusicManager es un autoload).
	var music := get_node_or_null("/root/MusicManager")
	if music != null:
		music.play_menu_theme()


func start_game() -> void:
	_play_button.disabled = true
	var error := get_tree().change_scene_to_file(MATCH_SCENE_PATH)
	if error != OK:
		_play_button.disabled = false
		push_error("No se pudo abrir %s: %s" % [MATCH_SCENE_PATH, error_string(error)])
