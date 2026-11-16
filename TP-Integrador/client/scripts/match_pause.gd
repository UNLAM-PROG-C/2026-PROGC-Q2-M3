extends Node

## Menú de la partida: la acción "pause_menu" (Escape) abre el panel de volumen de música y
## sonidos, con un botón para volver a la partida. La partida no se detiene (el servidor es
## autoritativo y sigue corriendo): el panel solo tapa la vista y toma el input.
## - Solo se abre en Running.
## - Mientras se coloca un edificio, Escape cancela la colocación y no abre el menú (este nodo
##   está antes que BuildPlacementController en el árbol, así que recibe el input después).
## - Si la partida termina con el menú abierto, se cierra para mostrar el resultado.

@export var pause_menu: Control
@export var placement_controller: Node

var _session: Node


func _ready() -> void:
	_session = get_node_or_null("/root/SessionController")
	if _session != null and _session.has_signal("session_state_changed"):
		_session.session_state_changed.connect(_on_session_state_changed)


func _exit_tree() -> void:
	if is_instance_valid(_session) and _session.session_state_changed.is_connected(_on_session_state_changed):
		_session.session_state_changed.disconnect(_on_session_state_changed)


func _unhandled_input(event: InputEvent) -> void:
	if not event.is_action_pressed("pause_menu") or pause_menu == null or pause_menu.visible:
		return
	if can_open():
		pause_menu.open()
		get_viewport().set_input_as_handled()


func can_open() -> bool:
	if _session == null or str(_session.call("get_state_name")) != "Running":
		return false
	return placement_controller == null or not placement_controller.is_placing()


func _on_session_state_changed(_state: int, state_name: String) -> void:
	if state_name != "Running" and pause_menu != null and pause_menu.visible:
		pause_menu.close()
