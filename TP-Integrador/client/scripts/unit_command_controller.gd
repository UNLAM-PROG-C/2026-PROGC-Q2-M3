extends Node

signal selection_changed(unit_ids: Array[int])
signal command_failed(error_code: String, detail: String)

const SELECTION_PICK_RADIUS_PIXELS := 32.0

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
		if typeof(destination_value) != TYPE_VECTOR2:
			return
		var destination: Vector2 = destination_value
		get_viewport().set_input_as_handled()
		var result_value: Variant = await _world.call(
			"issue_move_command",
			destination,
			selected_unit_ids.duplicate()
		)
		if typeof(result_value) != TYPE_DICTIONARY:
			_report_command_failure(
				"InvalidMoveResult",
				"InitialWorld devolvió un resultado inválido para Move."
			)
			return
		var result: Dictionary = result_value
		if not bool(result.get("ok", false)):
			_report_command_failure(
				str(result.get("error", "MoveFailed")),
				str(result.get("detail", "No se pudo emitir el comando Move."))
			)


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
