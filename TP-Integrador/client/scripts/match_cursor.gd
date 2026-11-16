extends Node

## Elige el cursor sobre el mundo durante la partida, en este orden:
## 1. arrastrando la cámara (camera_drag): cruz de mover;
## 2. en los bordes de la pantalla, donde la cámara se desplaza: flecha de esa dirección;
## 3. sobre la interfaz o colocando un edificio: cursor por defecto;
## 4. con unidades seleccionadas, según la orden que daría el clic derecho:
##    atacar → espada, recolectar → hacha, reparar granja o continuar una obra → martillo,
##    mover → guante que señala, ninguna → por defecto.

@export var camera_rig: Node
@export var command_controller: Node
@export var placement_controller: Node

var _cursor_manager: Node


func _ready() -> void:
	_cursor_manager = get_node_or_null("/root/CursorManager")
	# Al terminar un cuadro de selección, el guante se abre un instante.
	if command_controller != null and command_controller.has_signal("box_selection_finished"):
		command_controller.box_selection_finished.connect(_on_box_selection_finished)


func _on_box_selection_finished(_unit_ids: Array) -> void:
	if _cursor_manager != null:
		_cursor_manager.flash_open()


func _process(_delta: float) -> void:
	if _cursor_manager == null:
		return
	var viewport := get_viewport()
	_cursor_manager.set_world_cursor(
		resolve_cursor(viewport.get_mouse_position(), viewport.gui_get_hovered_control() != null)
	)


func _exit_tree() -> void:
	if _cursor_manager != null:
		_cursor_manager.set_world_cursor("default")


func resolve_cursor(mouse_position: Vector2, hovering_gui: bool) -> String:
	if camera_rig != null and bool(camera_rig.get("is_dragging")):
		return "drag"
	var edge := edge_direction(mouse_position)
	if not edge.is_empty():
		return "nav_" + edge
	if hovering_gui:
		return "default"
	if placement_controller != null and placement_controller.is_placing():
		return "default"
	if command_controller == null or command_controller.get_selected_units().is_empty():
		return "default"
	var ground: Variant = command_controller._mouse_to_map_position(mouse_position)
	var target_id: int = command_controller._pick_entity(mouse_position, ground)
	var command: Dictionary = command_controller.resolve_contextual_command(target_id)
	match str(command.get("type", "")):
		"Attack":
			return "attack"
		"GatherWood":
			return "gather"
		"RepairFarm", "Build":
			return "hammer"
		"Move":
			return "point" if typeof(ground) == TYPE_VECTOR2 else "default"
		"":
			return "default"
	return "point"


## Dirección del borde donde está el mouse ("n", "ne", …), con el mismo margen con el que
## la cámara se desplaza (RTSCamera.mouse_edge_margin), o "" lejos de los bordes.
func edge_direction(mouse_position: Vector2) -> String:
	if camera_rig == null:
		return ""
	var size := get_viewport().get_visible_rect().size
	if not Rect2(Vector2.ZERO, size).has_point(mouse_position):
		return ""
	var margin := float(camera_rig.get("mouse_edge_margin"))
	var vertical := ""
	var horizontal := ""
	if mouse_position.y < margin:
		vertical = "n"
	elif mouse_position.y > size.y - margin:
		vertical = "s"
	if mouse_position.x < margin:
		horizontal = "w"
	elif mouse_position.x > size.x - margin:
		horizontal = "e"
	return vertical + horizontal
