extends Node3D

## Colocación de edificios (Build colocar, contrato 03 §3) con vista previa de la huella.
## Mientras está activa, consume los clics del mapa antes que UnitCommandController
## (está después en el árbol, así que recibe primero el input no manejado).
## - Clic izquierdo: coloca el edificio con los trabajadores seleccionados.
## - Vallas: arrastrar arma una línea recta horizontal o vertical, un Build por celda (D-033).
## - Clic derecho o Esc: cancela.
## La vista previa usa la validación local del mundo; el resultado oficial lo decide el servidor.

signal placement_mode_changed(building_type: String)
signal command_failed(error_code: String, detail: String)

## Tope de vallas por arrastre: cada una es un comando y el límite por segundo del servidor
## (`CommandsPerSecond` / `CommandBurst`) todavía es PENDIENTE.
const MAX_FENCE_CELLS := 20
const GHOST_HEIGHT := 0.3
const VALID_COLOR := Color(0.25, 0.9, 0.35, 0.45)
const INVALID_COLOR := Color(0.95, 0.25, 0.2, 0.45)

## Tipo de edificio que se está colocando ("" = sin colocación activa).
var building_type := ""
## Resultado de validate_placement de cada celda ancla de la vista previa actual.
var preview_results: Array[Dictionary] = []

var _world: Node
var _command_controller: Node
var _drag_start := Vector2i(-1, -1)
var _is_dragging := false
var _is_placing := false
var _ghosts: Array[MeshInstance3D] = []
var _valid_material: StandardMaterial3D
var _invalid_material: StandardMaterial3D


func _ready() -> void:
	_world = get_parent()
	_command_controller = _world.get_node_or_null("UnitCommandController")
	_valid_material = _make_material(VALID_COLOR)
	_invalid_material = _make_material(INVALID_COLOR)
	if _command_controller != null and _command_controller.has_signal("selection_changed"):
		_command_controller.selection_changed.connect(_on_selection_changed)
	for signal_name: String in ["world_discarded", "world_preparation_completed"]:
		if _world.has_signal(signal_name):
			_world.connect(signal_name, func(_id: int) -> void: cancel_placement())


func _process(_delta: float) -> void:
	if building_type.is_empty():
		return
	var anchor: Variant = _anchor_under_mouse()
	if anchor == null:
		_show_preview([])
		return
	if _is_dragging:
		_show_preview(fence_line_cells(_drag_start, anchor))
	else:
		_show_preview([anchor])


func _unhandled_input(event: InputEvent) -> void:
	if building_type.is_empty():
		return
	if event.is_action_pressed("ui_cancel"):
		cancel_placement()
		get_viewport().set_input_as_handled()
		return
	var mouse_event := event as InputEventMouseButton
	# Solo se toman los clics: la rueda y el botón del medio siguen moviendo la cámara.
	if mouse_event == null or (
		mouse_event.button_index != MOUSE_BUTTON_LEFT and mouse_event.button_index != MOUSE_BUTTON_RIGHT
	):
		return
	get_viewport().set_input_as_handled()
	if mouse_event.button_index == MOUSE_BUTTON_RIGHT and mouse_event.pressed:
		cancel_placement()
		return
	if mouse_event.button_index != MOUSE_BUTTON_LEFT or _is_placing:
		return
	var anchor: Variant = _anchor_under_mouse()
	if mouse_event.pressed:
		if anchor == null:
			return
		if building_type == "Fence":
			_drag_start = anchor
			_is_dragging = true
		else:
			await place_at(anchor, mouse_event.shift_pressed)
	elif _is_dragging:
		_is_dragging = false
		var end: Vector2i = anchor if anchor != null else _drag_start
		await place_fence_line(fence_line_cells(_drag_start, end), mouse_event.shift_pressed)


## Empieza a colocar un tipo de edificio. Hace falta al menos un trabajador seleccionado.
func start_placement(next_building_type: String) -> bool:
	if not _world.BUILDABLE_TYPES.has(next_building_type) or _selected_workers().is_empty():
		return false
	building_type = next_building_type
	_is_dragging = false
	placement_mode_changed.emit(building_type)
	return true


func cancel_placement() -> void:
	if building_type.is_empty():
		return
	building_type = ""
	_is_dragging = false
	_show_preview([])
	placement_mode_changed.emit("")


func is_placing() -> bool:
	return not building_type.is_empty()


## Coloca un edificio con todos los trabajadores seleccionados. Con `keep_placing`
## (Shift) la colocación sigue activa para poner otro del mismo tipo.
func place_at(anchor: Vector2i, keep_placing := false) -> Dictionary:
	var placing_type := building_type
	_is_placing = true
	var result: Dictionary = await _world.issue_build_place_command(placing_type, anchor.x, anchor.y, _selected_workers())
	_is_placing = false
	if not bool(result.get("ok", false)):
		_report(result)
	elif not keep_placing:
		cancel_placement()
	return result


## Una valla por celda, cada una con un Build independiente (D-033). Los trabajadores se
## reparten en orden; como cada orden nueva reemplaza a la anterior, cada trabajador termina
## construyendo su última valla y las demás quedan como obras para continuar.
## Las celdas inválidas en la vista previa se saltean.
func place_fence_line(cells: Array[Vector2i], keep_placing := false) -> Array[Dictionary]:
	var results: Array[Dictionary] = []
	var workers := _selected_workers()
	if workers.is_empty():
		return results
	_is_placing = true
	var reported := false
	for index in range(cells.size()):
		var cell := cells[index]
		var worker: Array[int] = [workers[index % workers.size()]]
		var result: Dictionary = await _world.issue_build_place_command("Fence", cell.x, cell.y, worker, true)
		results.append(result)
		if not bool(result.get("ok", false)) and not reported:
			_report(result)
			reported = true
	_is_placing = false
	if not keep_placing:
		cancel_placement()
	return results


## Celdas de una línea de vallas: recta sobre el eje con más recorrido, como máximo
## MAX_FENCE_CELLS celdas desde el inicio.
static func fence_line_cells(start: Vector2i, end: Vector2i) -> Array[Vector2i]:
	var cells: Array[Vector2i] = []
	var delta := end - start
	var horizontal := absi(delta.x) >= absi(delta.y)
	var length := absi(delta.x) if horizontal else absi(delta.y)
	var step := Vector2i(signi(delta.x), 0) if horizontal else Vector2i(0, signi(delta.y))
	for index in range(mini(length + 1, MAX_FENCE_CELLS)):
		cells.append(start + step * index)
	return cells


## Celda ancla para que la huella quede centrada en la celda bajo el cursor.
func anchor_for_point(point: Vector2) -> Vector2i:
	var cell_size := float(_world.active_snapshot.get("map", {}).get("cellSize", 1.0))
	var footprint: Rect2i = _world.placement_footprint(building_type, 0, 0)
	var cell := Vector2i(floori(point.x / cell_size), floori(point.y / cell_size))
	return cell - Vector2i((footprint.size.x - 1) / 2, (footprint.size.y - 1) / 2)


func _show_preview(anchors: Array) -> void:
	preview_results.clear()
	var cell_size := float(_world.active_snapshot.get("map", {}).get("cellSize", 1.0))
	while _ghosts.size() < anchors.size():
		var ghost := MeshInstance3D.new()
		ghost.mesh = BoxMesh.new()
		ghost.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		add_child(ghost)
		_ghosts.append(ghost)
	for index in range(_ghosts.size()):
		var ghost := _ghosts[index]
		ghost.visible = index < anchors.size()
		if not ghost.visible:
			continue
		var anchor: Vector2i = anchors[index]
		var result: Dictionary = _world.validate_placement(building_type, anchor.x, anchor.y)
		preview_results.append(result)
		var footprint: Rect2i = _world.placement_footprint(building_type, anchor.x, anchor.y)
		(ghost.mesh as BoxMesh).size = Vector3(footprint.size.x * cell_size, GHOST_HEIGHT, footprint.size.y * cell_size)
		ghost.position = Vector3(
			(footprint.position.x + footprint.size.x / 2.0) * cell_size,
			GHOST_HEIGHT / 2.0,
			(footprint.position.y + footprint.size.y / 2.0) * cell_size
		)
		ghost.material_override = _valid_material if bool(result.get("ok", false)) else _invalid_material


func _selected_workers() -> Array[int]:
	var workers: Array[int] = []
	if _command_controller == null:
		return workers
	for unit_id: int in _command_controller.get_selected_units():
		var unit: Node3D = _world.entities_by_id.get(unit_id)
		if unit != null and str(unit.get_meta("entity_state", {}).get("unitType", "")) == "Worker":
			workers.append(unit_id)
	return workers


func _anchor_under_mouse() -> Variant:
	var camera := get_viewport().get_camera_3d()
	if camera == null or _world.get("active_world") == null:
		return null
	var mouse := get_viewport().get_mouse_position()
	var origin := camera.project_ray_origin(mouse)
	var direction := camera.project_ray_normal(mouse)
	if is_zero_approx(direction.y):
		return null
	var distance := -origin.y / direction.y
	if distance < 0.0:
		return null
	var hit := origin + direction * distance
	return anchor_for_point(Vector2(hit.x, hit.z))


func _on_selection_changed(_unit_ids: Array) -> void:
	if not building_type.is_empty() and _selected_workers().is_empty() and not _is_placing:
		cancel_placement()


func _report(result: Dictionary) -> void:
	command_failed.emit(str(result.get("error", "SendFailed")), str(result.get("detail", "")))


func _make_material(color: Color) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	return material
