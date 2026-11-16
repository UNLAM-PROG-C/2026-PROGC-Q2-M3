extends Node

signal selection_changed(unit_ids: Array[int])
## Edificio propio seleccionado (0 = ninguno). Es excluyente con la selección de unidades.
signal building_selection_changed(building_id: int)
signal command_failed(error_code: String, detail: String)
## Marcador visual inmediato de una orden de clic derecho (work-plan §2): tipo de orden y lugar.
signal command_marker_shown(command_type: String, world_position: Vector3)
## Se soltó el clic de un cuadro de selección (no se emite con un clic simple).
signal box_selection_finished(unit_ids: Array[int])

const SELECTION_PICK_RADIUS_PIXELS := 32.0
## Distancia en píxeles a partir de la cual un clic izquierdo se vuelve rectángulo de selección.
const DRAG_THRESHOLD_PIXELS := 6.0
## Un clic repetido a menos de esta distancia del anterior cicla entre unidades superpuestas.
const CYCLE_DISTANCE_PIXELS := 8.0
const MARKER_SECONDS := 0.6
## Cuánto más ancho que el modelo del objetivo es el anillo de una orden sin huella.
const MARKER_MODEL_MARGIN := 1.3
const MARKER_COLORS := {
	"Move": Color(0.35, 0.95, 0.45),
	"Attack": Color(1.0, 0.3, 0.25),
	"GatherWood": Color(1.0, 0.85, 0.3),
	"Build": Color(1.0, 0.85, 0.3),
	"RepairFarm": Color(1.0, 0.85, 0.3),
}
const TASK_UNIT_TYPES := {
	"GatherWood": "Worker",
	"Build": "Worker",
	"RepairFarm": "Worker",
	"Attack": "Soldier",
}

var selected_unit_ids: Array[int] = []
var selected_building_id := 0

var _world: Node
var _session_controller: Node
var _press_position := Vector2.ZERO
var _is_left_pressed := false
var _is_box_selecting := false
var _last_click_position := Vector2(-1.0e9, -1.0e9)
var _last_click_candidates: Array[int] = []
var _last_click_index := -1
var _selection_box: Panel


func _ready() -> void:
	_world = get_parent()
	if _world == null:
		push_error("UnitCommandController requiere InitialWorld como padre.")
		return

	var world_callbacks := {
		"world_preparation_completed": Callable(self, "_on_world_preparation_completed"),
		"world_discarded": Callable(self, "_on_world_discarded"),
		"unit_removed": Callable(self, "_on_unit_removed"),
		"building_removed": Callable(self, "_on_building_removed"),
	}
	for signal_name: String in world_callbacks:
		if not _world.has_signal(signal_name):
			push_error("InitialWorld no expone la señal %s." % signal_name)
			return
		var callback: Callable = world_callbacks[signal_name]
		if not _world.is_connected(signal_name, callback):
			var connect_error: Error = _world.connect(signal_name, callback)
			if connect_error != OK:
				push_error("No se pudo conectar UnitCommandController a %s." % signal_name)
				return

	_session_controller = get_node_or_null("/root/SessionController")
	if (
		_session_controller != null
		and _session_controller.has_signal("session_state_changed")
	):
		var session_callback := Callable(self, "_on_session_state_changed")
		if not _session_controller.is_connected("session_state_changed", session_callback):
			var connect_error: Error = _session_controller.connect(
				"session_state_changed",
				session_callback
			)
			if connect_error != OK:
				push_error("No se pudo conectar a session_state_changed.")


func _exit_tree() -> void:
	if is_instance_valid(_world):
		var world_callbacks := {
			"world_preparation_completed": Callable(self, "_on_world_preparation_completed"),
			"world_discarded": Callable(self, "_on_world_discarded"),
			"unit_removed": Callable(self, "_on_unit_removed"),
			"building_removed": Callable(self, "_on_building_removed"),
		}
		for signal_name: String in world_callbacks:
			var callback: Callable = world_callbacks[signal_name]
			if (
				_world.has_signal(signal_name)
				and _world.is_connected(signal_name, callback)
			):
				_world.disconnect(signal_name, callback)
	if (
		is_instance_valid(_session_controller)
		and _session_controller.has_signal("session_state_changed")
	):
		var session_callback := Callable(self, "_on_session_state_changed")
		if _session_controller.is_connected("session_state_changed", session_callback):
			_session_controller.disconnect("session_state_changed", session_callback)


func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey:
		if event.is_action_pressed("stop_selected_units") and not selected_unit_ids.is_empty():
			get_viewport().set_input_as_handled()
			_report_if_failed(_world.call("issue_stop_command", selected_unit_ids.duplicate()), "Stop")
		return
	var motion := event as InputEventMouseMotion
	if motion != null:
		if _is_left_pressed and (_is_box_selecting or motion.position.distance_to(_press_position) > DRAG_THRESHOLD_PIXELS):
			_is_box_selecting = true
			_update_selection_box(Rect2(_press_position, motion.position - _press_position).abs())
		return
	var mouse_event := event as InputEventMouseButton
	if mouse_event == null or mouse_event.canceled:
		return
	if mouse_event.is_action("select_unit"):
		get_viewport().set_input_as_handled()
		if mouse_event.pressed:
			_is_left_pressed = true
			_is_box_selecting = false
			_press_position = mouse_event.position
		elif _is_left_pressed:
			_is_left_pressed = false
			if _is_box_selecting:
				_is_box_selecting = false
				_update_selection_box(Rect2())
				select_in_screen_rect(Rect2(_press_position, mouse_event.position - _press_position).abs())
				box_selection_finished.emit(selected_unit_ids.duplicate())
			else:
				select_at_screen_position(mouse_event.position)
		return
	if not mouse_event.pressed:
		return
	if mouse_event.is_action_pressed("move_selected_units") and not selected_unit_ids.is_empty():
		var destination_value: Variant = _mouse_to_map_position(mouse_event.position)
		var target_id := _pick_entity(mouse_event.position, destination_value)
		var command := resolve_contextual_command(target_id)
		if command.is_empty():
			return
		get_viewport().set_input_as_handled()
		if str(command["type"]) == "Move" and typeof(destination_value) != TYPE_VECTOR2:
			return
		show_command_marker(command, destination_value)
		await issue_contextual_command(command, destination_value)


## Clic izquierdo: selecciona la unidad propia bajo el cursor. Repetir el clic en el mismo lugar
## cicla entre las unidades superpuestas (04 §3). Sin unidades, selecciona un edificio propio.
func select_at_screen_position(screen_position: Vector2) -> void:
	var candidates := _local_units_at(screen_position)
	var index := 0
	if (
		not candidates.is_empty()
		and screen_position.distance_to(_last_click_position) <= CYCLE_DISTANCE_PIXELS
		and candidates == _last_click_candidates
	):
		index = (_last_click_index + 1) % candidates.size()
	_last_click_position = screen_position
	_last_click_candidates = candidates
	_last_click_index = index
	if not candidates.is_empty():
		var picked: Array[int] = [candidates[index]]
		set_selected_units(picked)
		return
	var building_id := _pick_own_building(_mouse_to_map_position(screen_position))
	if building_id > 0:
		set_selected_building(building_id)
	else:
		clear_selection()
		_clear_building_selection()


## Rectángulo de selección: todas las unidades propias visibles dentro del rectángulo en pantalla.
func select_in_screen_rect(screen_rect: Rect2) -> void:
	_last_click_candidates.clear()
	var camera := get_viewport().get_camera_3d()
	var picked: Array[int] = []
	if camera != null:
		for unit_id: int in _visible_local_unit_ids():
			var unit: Node3D = _get_entities()[unit_id]
			var world_position := unit.global_position + Vector3.UP * 0.4
			if not camera.is_position_behind(world_position) and screen_rect.has_point(camera.unproject_position(world_position)):
				picked.append(unit_id)
	if picked.is_empty():
		clear_selection()
		_clear_building_selection()
	else:
		set_selected_units(picked)


func is_box_selecting() -> bool:
	return _is_box_selecting


## Anillo que aparece y se desvanece donde se dio la orden: el destino de un Move o el
## objetivo de una tarea, del tamaño de su huella si es un edificio. Es solo visual: se
## muestra al hacer clic, antes y aparte del resultado oficial.
func show_command_marker(command: Dictionary, destination: Variant = null) -> MeshInstance3D:
	var command_type := str(command.get("type", ""))
	var marker_position := Vector3.ZERO
	var marker_size := 1.0
	var target: Node3D = _get_entities().get(int(command.get("targetId", 0)))
	if target != null:
		marker_position = target.global_position
		var footprint: Dictionary = target.get_meta("entity_state", {}).get("footprint", {})
		if not footprint.is_empty():
			var cell_size := float(_get_active_snapshot().get("map", {}).get("cellSize", 1.0))
			marker_size = maxf(float(footprint.get("width", 1)), float(footprint.get("height", 1))) * cell_size
		else:
			# Árboles y unidades: el anillo rodea el modelo (por ejemplo, la copa del árbol)
			# para que no quede escondido adentro.
			marker_size = maxf(_horizontal_extent(target) * MARKER_MODEL_MARGIN, 1.0)
	elif typeof(destination) == TYPE_VECTOR2:
		marker_position = Vector3(destination.x, 0.0, destination.y)
	else:
		return null
	var marker := _create_ring(MARKER_COLORS.get(command_type, Color.WHITE), 0.5)
	marker.name = "CommandMarker"
	marker.position = marker_position + Vector3.UP * 0.06
	marker.scale = Vector3.ONE * marker_size * 0.6
	_world.add_child(marker)
	var material := marker.material_override as StandardMaterial3D
	var tween := marker.create_tween()
	tween.set_parallel(true)
	tween.tween_property(marker, "scale", Vector3.ONE * marker_size, MARKER_SECONDS)
	tween.tween_property(material, "albedo_color:a", 0.0, MARKER_SECONDS)
	tween.chain().tween_callback(marker.queue_free)
	command_marker_shown.emit(command_type, marker_position)
	return marker


## Decide la orden de un clic derecho según lo que hay bajo el cursor (plan 06 §2):
## árbol disponible → GatherWood; unidad o edificio rival → Attack; obra propia → Build
## (continuar); granja propia agotada → RepairFarm; cualquier otra cosa → Move.
## Si la selección no tiene unidades compatibles con la tarea, el clic se ignora.
func resolve_contextual_command(target_id: int) -> Dictionary:
	var command_type := "Move"
	var target: Node3D = _get_entities().get(target_id) if target_id > 0 else null
	if target != null:
		var state: Dictionary = target.get_meta("entity_state", {})
		var kind := str(target.get_meta("entity_kind", ""))
		var is_own := int(state.get("owner", 0)) == int(_get_active_snapshot().get("playerId", 0))
		if kind == "tree" and bool(state.get("isAvailable", false)):
			command_type = "GatherWood"
		elif (kind == "unit" or kind == "building") and not is_own:
			command_type = "Attack"
		elif kind == "building" and not bool(state.get("isCompleted", false)):
			command_type = "Build"
		elif (
			kind == "building"
			and str(state.get("buildingType", "")) == "Farm"
			and bool(state.get("farm", {}).get("isDepleted", false))
		):
			command_type = "RepairFarm"
	if command_type == "Move":
		return {"type": "Move", "targetId": 0, "unitIds": selected_unit_ids.duplicate()}
	var compatible: Array[int] = []
	for unit_id: int in selected_unit_ids:
		var unit: Node3D = _get_entities().get(unit_id)
		if unit == null:
			continue
		var unit_type := str(unit.get_meta("entity_state", {}).get("unitType", ""))
		if unit_type == TASK_UNIT_TYPES[command_type]:
			compatible.append(unit_id)
	if compatible.is_empty():
		return {}
	return {"type": command_type, "targetId": target_id, "unitIds": compatible}


func issue_contextual_command(command: Dictionary, destination: Variant = null) -> Dictionary:
	var command_type := str(command.get("type", ""))
	var unit_ids: Array[int] = []
	for unit_id_value: Variant in command.get("unitIds", []):
		unit_ids.append(int(unit_id_value))
	var result_value: Variant
	match command_type:
		"Move":
			result_value = await _world.call("issue_move_command", destination, unit_ids)
		"GatherWood":
			result_value = await _world.call("issue_gather_wood_command", int(command["targetId"]), unit_ids)
		"Attack":
			result_value = await _world.call("issue_attack_command", int(command["targetId"]), unit_ids)
		"Build":
			result_value = await _world.call("issue_build_continue_command", int(command["targetId"]), unit_ids)
		"RepairFarm":
			result_value = await _world.call("issue_repair_farm_command", int(command["targetId"]), unit_ids)
		_:
			result_value = {"ok": false, "error": "UnknownCommand", "detail": "Orden contextual desconocida."}
	return _report_if_failed(result_value, command_type)


func _report_if_failed(result_value: Variant, command_type: String) -> Dictionary:
	if typeof(result_value) != TYPE_DICTIONARY:
		_report_command_failure(
			"InvalidCommandResult",
			"InitialWorld devolvió un resultado inválido para %s." % command_type
		)
		return {"ok": false, "error": "InvalidCommandResult"}
	var result: Dictionary = result_value
	if not bool(result.get("ok", false)):
		_report_command_failure(
			str(result.get("error", "%sFailed" % command_type)),
			str(result.get("detail", "No se pudo emitir el comando %s." % command_type))
		)
	return result


func set_selected_units(unit_ids: Array[int]) -> void:
	clear_selection()
	_clear_building_selection()
	var entities := _get_entities()
	var snapshot := _get_active_snapshot()
	var local_player_id := int(snapshot.get("playerId", 0))
	for unit_id: int in _distinct_sorted_array(unit_ids):
		var unit: Node3D = entities.get(unit_id)
		if (
			unit == null
			or unit.get_meta("entity_kind", "") != "unit"
			or int(unit.get_meta("owner", 0)) != local_player_id
		):
			continue
		selected_unit_ids.append(unit_id)
		_set_unit_selected(unit, true)
	selection_changed.emit(selected_unit_ids.duplicate())


func get_selected_units() -> Array[int]:
	return selected_unit_ids.duplicate()


## Selecciona un edificio propio (para ver su estado y entrenar). Deselecciona las unidades.
func set_selected_building(building_id: int) -> void:
	clear_selection()
	_clear_building_selection()
	var building: Node3D = _get_entities().get(building_id)
	if (
		building == null
		or building.get_meta("entity_kind", "") != "building"
		or int(building.get_meta("owner", 0)) != int(_get_active_snapshot().get("playerId", 0))
	):
		return
	selected_building_id = building_id
	_set_building_selected(building, true)
	building_selection_changed.emit(building_id)


func _clear_building_selection() -> void:
	if selected_building_id == 0:
		return
	var building: Node3D = _get_entities().get(selected_building_id)
	if building != null:
		_set_building_selected(building, false)
	selected_building_id = 0
	building_selection_changed.emit(0)


func clear_selection() -> void:
	for unit_id: int in selected_unit_ids:
		var unit: Node3D = _get_entities().get(unit_id)
		if unit != null:
			_set_unit_selected(unit, false)
	selected_unit_ids.clear()
	var no_units: Array[int] = []
	selection_changed.emit(no_units)


## Unidades propias visibles a menos de SELECTION_PICK_RADIUS_PIXELS del cursor, de la más
## cercana a la más lejana (empates por ID, para que el ciclo sea estable).
func _local_units_at(mouse_position: Vector2) -> Array[int]:
	var camera := get_viewport().get_camera_3d()
	var result: Array[int] = []
	if camera == null:
		return result
	var distances: Dictionary = {}
	for unit_id: int in _visible_local_unit_ids():
		var unit: Node3D = _get_entities()[unit_id]
		var world_position := unit.global_position + Vector3.UP * 0.8
		if camera.is_position_behind(world_position):
			continue
		var distance := mouse_position.distance_squared_to(camera.unproject_position(world_position))
		if distance <= SELECTION_PICK_RADIUS_PIXELS * SELECTION_PICK_RADIUS_PIXELS:
			distances[unit_id] = distance
			result.append(unit_id)
	result.sort_custom(
		func(a: int, b: int) -> bool:
			if is_equal_approx(distances[a], distances[b]):
				return a < b
			return distances[a] < distances[b]
	)
	return result


func _visible_local_unit_ids() -> Array[int]:
	var result: Array[int] = []
	if _world.get("active_world") == null:
		return result
	var local_player_id := int(_get_active_snapshot().get("playerId", 0))
	for entity_id: Variant in _get_entities():
		var unit: Node3D = _get_entities()[entity_id]
		if (
			unit.get_meta("entity_kind", "") == "unit"
			and int(unit.get_meta("owner", 0)) == local_player_id
			and unit.is_visible_in_tree()
		):
			result.append(int(entity_id))
	result.sort()
	return result


func _update_selection_box(screen_rect: Rect2) -> void:
	if _selection_box == null:
		var layer := CanvasLayer.new()
		layer.name = "SelectionBoxLayer"
		add_child(layer)
		_selection_box = Panel.new()
		_selection_box.name = "SelectionBox"
		_selection_box.mouse_filter = Control.MOUSE_FILTER_IGNORE
		var style := StyleBoxFlat.new()
		style.bg_color = Color(0.15, 0.85, 1.0, 0.15)
		style.border_color = Color(0.15, 0.85, 1.0, 0.9)
		style.set_border_width_all(2)
		_selection_box.add_theme_stylebox_override("panel", style)
		layer.add_child(_selection_box)
	_selection_box.visible = screen_rect.size != Vector2.ZERO
	_selection_box.position = screen_rect.position
	_selection_box.size = screen_rect.size


func _pick_entity(mouse_position: Vector2, ground_point: Variant) -> int:
	var entities := _get_entities()
	if typeof(ground_point) == TYPE_VECTOR2:
		var cell_size := float(_get_active_snapshot().get("map", {}).get("cellSize", 0.0))
		for entity_id: Variant in entities:
			var building: Node3D = entities[entity_id]
			if building.get_meta("entity_kind", "") != "building" or cell_size <= 0.0:
				continue
			var footprint: Dictionary = building.get_meta("entity_state", {}).get("footprint", {})
			if footprint.is_empty():
				continue
			var rect := Rect2(
				float(footprint["column"]) * cell_size,
				float(footprint["row"]) * cell_size,
				float(footprint["width"]) * cell_size,
				float(footprint["height"]) * cell_size
			)
			if rect.has_point(ground_point):
				return int(entity_id)
	var camera := get_viewport().get_camera_3d()
	if camera == null:
		return 0
	var best_id := 0
	var best_distance_squared := SELECTION_PICK_RADIUS_PIXELS * SELECTION_PICK_RADIUS_PIXELS
	for entity_id: Variant in entities:
		var entity: Node3D = entities[entity_id]
		var kind := str(entity.get_meta("entity_kind", ""))
		if (kind != "unit" and kind != "tree") or not entity.visible:
			continue
		var world_position := entity.global_position + Vector3.UP * 0.8
		if camera.is_position_behind(world_position):
			continue
		var distance_squared := mouse_position.distance_squared_to(camera.unproject_position(world_position))
		if distance_squared < best_distance_squared:
			best_distance_squared = distance_squared
			best_id = int(entity_id)
	return best_id


func _pick_own_building(ground_point: Variant) -> int:
	var building_id := _pick_entity(Vector2(-1.0e9, -1.0e9), ground_point)
	var building: Node3D = _get_entities().get(building_id) if building_id > 0 else null
	if (
		building == null
		or building.get_meta("entity_kind", "") != "building"
		or int(building.get_meta("owner", 0)) != int(_get_active_snapshot().get("playerId", 0))
	):
		return 0
	return building_id


func _set_building_selected(building: Node3D, selected: bool) -> void:
	var marker: MeshInstance3D = building.get_meta("selection_marker") if building.has_meta("selection_marker") else null
	if marker == null:
		if not selected:
			return
		marker = _create_selection_marker(building)
		var footprint: Dictionary = building.get_meta("entity_state", {}).get("footprint", {})
		var cell_size := float(_get_active_snapshot().get("map", {}).get("cellSize", 1.0))
		# El anillo base mide ~1.16 m: se escala para rodear la huella.
		marker.scale = Vector3(
			float(footprint.get("width", 1)) * cell_size * 0.95,
			1.0,
			float(footprint.get("height", 1)) * cell_size * 0.95
		)
	marker.visible = selected


func _mouse_to_map_position(mouse_position: Vector2) -> Variant:
	var camera := get_viewport().get_camera_3d()
	if camera == null or _world.get("active_world") == null:
		return null
	var ray_origin := camera.project_ray_origin(mouse_position)
	var ray_direction := camera.project_ray_normal(mouse_position)
	if is_zero_approx(ray_direction.y):
		return null
	var distance := -ray_origin.y / ray_direction.y
	if distance < 0.0:
		return null
	var hit := ray_origin + ray_direction * distance
	var map_data: Dictionary = _get_active_snapshot().get("map", {})
	var cell_size := float(map_data.get("cellSize", 0.0))
	var width := float(map_data.get("columns", 0)) * cell_size
	var depth := float(map_data.get("rows", 0)) * cell_size
	if (
		hit.x < 0.0
		or hit.z < 0.0
		or hit.x >= width
		or hit.z >= depth
	):
		return null
	return Vector2(hit.x, hit.z)


func _get_entities() -> Dictionary:
	if not is_instance_valid(_world):
		return {}
	var value: Variant = _world.get("entities_by_id")
	if typeof(value) != TYPE_DICTIONARY:
		return {}
	return value


func _get_active_snapshot() -> Dictionary:
	if not is_instance_valid(_world):
		return {}
	var value: Variant = _world.get("active_snapshot")
	if typeof(value) != TYPE_DICTIONARY:
		return {}
	return value


func _set_unit_selected(unit: Node3D, selected: bool) -> void:
	var marker_value: Variant
	if unit.has_meta("selection_marker"):
		marker_value = unit.get_meta("selection_marker")
	var marker: MeshInstance3D
	if marker_value is MeshInstance3D:
		marker = marker_value
	elif selected:
		marker = _create_selection_marker(unit)
	else:
		return
	marker.visible = selected


## Ancho horizontal (X/Z) del modelo de una entidad, sin contar los marcadores de selección.
func _horizontal_extent(entity: Node3D) -> float:
	var extent := 0.0
	for visual: Node in entity.find_children("*", "VisualInstance3D", true, false):
		if visual.name == "SelectionMarker":
			continue
		var instance := visual as VisualInstance3D
		var box: AABB = entity.global_transform.affine_inverse() * instance.global_transform * instance.get_aabb()
		extent = maxf(extent, maxf(box.size.x, box.size.z))
	return extent


func _create_ring(color: Color, radius: float) -> MeshInstance3D:
	var ring := MeshInstance3D.new()
	var torus := TorusMesh.new()
	torus.inner_radius = radius * 0.83
	torus.outer_radius = radius
	ring.mesh = torus
	ring.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	material.transparency = BaseMaterial3D.TRANSPARENCY_ALPHA
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	ring.material_override = material
	return ring


func _create_selection_marker(unit: Node3D) -> MeshInstance3D:
	var marker := MeshInstance3D.new()
	marker.name = "SelectionMarker"
	var ring := TorusMesh.new()
	ring.inner_radius = 0.48
	ring.outer_radius = 0.58
	marker.mesh = ring
	marker.position.y = 0.05
	marker.visible = false
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.15, 0.85, 1.0)
	material.emission_enabled = true
	material.emission = Color(0.15, 0.85, 1.0)
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	marker.material_override = material
	unit.add_child(marker)
	unit.set_meta("selection_marker", marker)
	return marker


func _distinct_sorted_array(values: Array[int]) -> Array[int]:
	var seen: Dictionary = {}
	var result: Array[int] = []
	for value: int in values:
		if seen.has(value):
			continue
		seen[value] = true
		result.append(value)
	result.sort()
	return result


func _on_world_preparation_completed(_initialization_id: int) -> void:
	clear_selection()
	_clear_building_selection()


func _on_world_discarded(_initialization_id: int) -> void:
	clear_selection()
	_clear_building_selection()


func _on_unit_removed(unit_id: int) -> void:
	if not selected_unit_ids.has(unit_id):
		return
	var unit: Node3D = _get_entities().get(unit_id)
	if unit != null:
		_set_unit_selected(unit, false)
	selected_unit_ids.erase(unit_id)
	selection_changed.emit(selected_unit_ids.duplicate())


func _on_session_state_changed(_state: int, state_name: String) -> void:
	if ["AwaitingAssignment", "Ended", "Disconnected"].has(state_name):
		clear_selection()
		_clear_building_selection()


func _on_building_removed(building_id: int) -> void:
	if building_id == selected_building_id:
		selected_building_id = 0
		building_selection_changed.emit(0)


func _report_command_failure(error_code: String, detail: String) -> void:
	push_warning("%s: %s" % [error_code, detail])
	command_failed.emit(error_code, detail)
