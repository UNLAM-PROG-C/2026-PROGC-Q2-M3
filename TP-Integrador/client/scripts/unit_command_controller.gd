extends Node

signal selection_changed(unit_ids: Array[int])
signal command_failed(error_code: String, detail: String)

const SELECTION_PICK_RADIUS_PIXELS := 32.0
const TASK_UNIT_TYPES := {
	"GatherWood": "Worker",
	"Build": "Worker",
	"RepairFarm": "Worker",
	"Attack": "Soldier",
}

var selected_unit_ids: Array[int] = []

var _world: Node
var _session_controller: Node


func _ready() -> void:
	_world = get_parent()
	if _world == null:
		push_error("UnitCommandController requiere InitialWorld como padre.")
		return

	var world_callbacks := {
		"world_preparation_completed": Callable(self, "_on_world_preparation_completed"),
		"world_discarded": Callable(self, "_on_world_discarded"),
		"unit_removed": Callable(self, "_on_unit_removed"),
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
	var mouse_event := event as InputEventMouseButton
	if mouse_event == null or not mouse_event.pressed or mouse_event.canceled:
		return
	if mouse_event.is_action_pressed("select_unit"):
		var unit_id := _pick_local_unit(mouse_event.position)
		if unit_id > 0:
			set_selected_units([unit_id])
		else:
			clear_selection()
		get_viewport().set_input_as_handled()
	elif mouse_event.is_action_pressed("move_selected_units") and not selected_unit_ids.is_empty():
		var destination_value: Variant = _mouse_to_map_position(mouse_event.position)
		var target_id := _pick_entity(mouse_event.position, destination_value)
		var command := resolve_contextual_command(target_id)
		if command.is_empty():
			return
		get_viewport().set_input_as_handled()
		if str(command["type"]) == "Move" and typeof(destination_value) != TYPE_VECTOR2:
			return
		await issue_contextual_command(command, destination_value)


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


func clear_selection() -> void:
	for unit_id: int in selected_unit_ids:
		var unit: Node3D = _get_entities().get(unit_id)
		if unit != null:
			_set_unit_selected(unit, false)
	selected_unit_ids.clear()
	selection_changed.emit([])


func _pick_local_unit(mouse_position: Vector2) -> int:
	var camera := get_viewport().get_camera_3d()
	if camera == null or _world.get("active_world") == null:
		return 0
	var best_unit_id := 0
	var best_distance_squared := SELECTION_PICK_RADIUS_PIXELS * SELECTION_PICK_RADIUS_PIXELS
	var local_player_id := int(_get_active_snapshot().get("playerId", 0))
	for entity_id: Variant in _get_entities():
		var unit_id := int(entity_id)
		var unit: Node3D = _get_entities()[unit_id]
		if (
			unit.get_meta("entity_kind", "") != "unit"
			or int(unit.get_meta("owner", 0)) != local_player_id
			or not unit.visible
		):
			continue
		var world_position := unit.global_position + Vector3.UP * 0.8
		if camera.is_position_behind(world_position):
			continue
		var screen_position := camera.unproject_position(world_position)
		var distance_squared := mouse_position.distance_squared_to(screen_position)
		if distance_squared < best_distance_squared:
			best_distance_squared = distance_squared
			best_unit_id = unit_id
	return best_unit_id


## Entidad bajo el cursor para el clic contextual: primero el edificio cuya huella
## contiene el punto del suelo; si no hay, la unidad o el árbol visible más cercano en pantalla.
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


func _on_world_discarded(_initialization_id: int) -> void:
	clear_selection()


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


func _report_command_failure(error_code: String, detail: String) -> void:
	push_warning("%s: %s" % [error_code, detail])
	command_failed.emit(error_code, detail)
