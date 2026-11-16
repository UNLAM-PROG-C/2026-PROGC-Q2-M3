extends SceneTree

## Bloque 4: selección por clic con ciclo de superpuestas y por rectángulo (04 §3),
## marcadores de orden, barras de estado de entidades y animaciones de unidades.

const InitialWorldScript = preload("res://scripts/initial_world.gd")
const UnitCommandControllerScript = preload("res://scripts/unit_command_controller.gd")
const UnitAnimationControllerScript = preload("res://scripts/unit_animation_controller.gd")
const EntityStatusOverlayScript = preload("res://scripts/ui/entity_status_overlay.gd")

const EXPECTED_TESTS := 7

var _failed := false
var _completed_tests := 0
var _tick := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_click_selection_and_cycling()
	_test_box_selection()
	await _test_box_selection_with_input_events()
	await _test_command_markers()
	_test_status_bars()
	_test_unit_animations()
	await _test_unit_death()
	_check(_completed_tests == EXPECTED_TESTS, "Only %d of %d tests completed." % [_completed_tests, EXPECTED_TESTS])
	if _failed:
		quit(1)
		return
	print("Selection feedback tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _test_click_selection_and_cycling() -> void:
	var setup := _create_setup()
	var controller: Node = setup["controller"]
	var world: Node = setup["world"]
	# Dos trabajadores propios en el mismo lugar: el clic repetido cicla entre ellos.
	_place_unit(world, 4, Vector2(7.5, 3.5))
	var spot := _screen_position(setup, 3)
	controller.select_at_screen_position(spot)
	_check(controller.get_selected_units() == [3], "The first click selects the closest unit (lowest ID on ties).")
	controller.select_at_screen_position(spot + Vector2(2, 1))
	_check(controller.get_selected_units() == [4], "Clicking again on the same spot selects the overlapped unit.")
	controller.select_at_screen_position(spot)
	_check(controller.get_selected_units() == [3], "The cycle wraps around.")
	controller.select_at_screen_position(_screen_of(setup, Vector3(30, 0, 20)))
	_check(controller.get_selected_units().is_empty(), "Clicking empty ground clears the selection.")
	controller.select_at_screen_position(spot)
	_check(controller.get_selected_units() == [3], "After clicking elsewhere the cycle starts again.")
	controller.select_at_screen_position(_screen_of(setup, Vector3(4, 0, 4)))
	_check(controller.selected_building_id == 1 and controller.get_selected_units().is_empty(), "Clicking an own building without units selects it.")
	_check(
		controller.get_selected_units().is_empty(),
		"Rival units are never selected by click."
	)
	_free_setup(setup)


func _test_box_selection() -> void:
	var setup := _create_setup()
	var controller: Node = setup["controller"]
	var viewport_size: Vector2 = root.get_visible_rect().size
	controller.select_in_screen_rect(Rect2(Vector2.ZERO, viewport_size))
	_check(controller.get_selected_units() == [3, 4], "A box over the whole map selects only own units.")
	var around_three := Rect2(_screen_position(setup, 3) - Vector2(10, 10), Vector2(20, 20))
	controller.select_in_screen_rect(around_three)
	_check(controller.get_selected_units() == [3], "A small box selects the units inside it.")
	controller.select_in_screen_rect(Rect2(_screen_of(setup, Vector3(30, 0, 20)), Vector2(10, 10)))
	_check(controller.get_selected_units().is_empty(), "An empty box clears the selection.")
	_free_setup(setup)


func _test_box_selection_with_input_events() -> void:
	var setup := _create_setup()
	var controller: Node = setup["controller"]
	var start := _screen_position(setup, 3) - Vector2(30, 30)
	var end := _screen_position(setup, 4) + Vector2(30, 30)
	_push_mouse_button(MOUSE_BUTTON_LEFT, true, start)
	_check(not controller.is_box_selecting(), "A press alone is not a box selection.")
	var motion := InputEventMouseMotion.new()
	motion.position = end
	root.push_input(motion, true)
	_check(controller.is_box_selecting(), "Dragging past the threshold starts a box selection.")
	var box := controller.find_child("SelectionBox", true, false) as Control
	_check(box != null and box.visible and box.size.x > 50.0, "The selection box is drawn while dragging.")
	_push_mouse_button(MOUSE_BUTTON_LEFT, false, end)
	_check(controller.get_selected_units() == [3, 4], "Releasing selects the units inside the box.")
	_check(box != null and not box.visible, "The selection box hides on release.")
	_push_mouse_button(MOUSE_BUTTON_LEFT, true, _screen_position(setup, 4))
	_push_mouse_button(MOUSE_BUTTON_LEFT, false, _screen_position(setup, 4))
	_check(controller.get_selected_units() == [4], "A click without dragging selects a single unit on release.")
	await process_frame
	_free_setup(setup)


func _test_command_markers() -> void:
	var setup := _create_setup()
	var controller: Node = setup["controller"]
	var world: Node = setup["world"]
	var shown: Array = []
	controller.command_marker_shown.connect(func(kind: String, at: Vector3) -> void: shown.append([kind, at]))
	var move_marker: MeshInstance3D = controller.show_command_marker({"type": "Move", "unitIds": [3]}, Vector2(10, 12))
	_check(move_marker != null and move_marker.get_parent() == world, "A Move shows a marker in the world.")
	_check(move_marker.position.is_equal_approx(Vector3(10, 0.06, 12)), "The Move marker sits on the destination.")
	_check(
		(move_marker.material_override as StandardMaterial3D).albedo_color.is_equal_approx(controller.MARKER_COLORS["Move"]),
		"Move markers are green."
	)
	var attack_marker: MeshInstance3D = controller.show_command_marker({"type": "Attack", "targetId": 2, "unitIds": [3]})
	_check(attack_marker.position.is_equal_approx(Vector3(36, 0.06, 36)), "Task markers sit on the target.")
	_check(shown.size() == 2 and shown[1][0] == "Attack", "Each marker is notified.")
	_check(is_equal_approx(attack_marker.scale.x, 4.0 * 0.6), "A building marker starts at 60% of its footprint.")
	var gather_marker: MeshInstance3D = controller.show_command_marker({"type": "GatherWood", "targetId": 7, "unitIds": [3]})
	var tree_extent: float = controller._horizontal_extent(world.entities_by_id[7])
	_check(tree_extent > 2.0, "The tree model is measured (%.2f m)." % tree_extent)
	_check(
		is_equal_approx(gather_marker.scale.x, tree_extent * controller.MARKER_MODEL_MARGIN * 0.6),
		"A gather marker is wider than the tree, so it is not hidden inside it."
	)
	await create_timer(controller.MARKER_SECONDS + 0.3).timeout
	_check(
		not is_instance_valid(move_marker) and not is_instance_valid(attack_marker) and not is_instance_valid(gather_marker),
		"Markers fade out and are freed."
	)
	_check(controller.show_command_marker({"type": "Move"}) == null, "Without a place there is no marker.")
	_free_setup(setup)


func _test_status_bars() -> void:
	var setup := _create_setup()
	var overlay: Control = setup["overlay"]
	var controller: Node = setup["controller"]
	var world: Node = setup["world"]
	_check(overlay.collect_status_bars().is_empty(), "Healthy, unselected entities have no bars.")
	controller.set_selected_units(_ids([3]))
	var bars := _bars_by_id(overlay)
	_check(bars.size() == 1 and bars.has(3), "A selected unit shows its health.")
	_check(is_equal_approx(bars[3]["ratio"], 1.0) and bars[3]["color"] == overlay.OWN_COLOR, "Own units are green.")
	# Con la cámara cenital, "arriba de la unidad" cae sobre la unidad en pantalla.
	_check(bars[3]["screen_position"].distance_to(_screen_position(setup, 3)) < 1.0, "The bar follows its unit on screen.")
	controller.clear_selection()
	_apply(world, [{"type": "EntityDamaged", "entityId": 5, "hp": 20, "index": 0}])
	bars = _bars_by_id(overlay)
	_check(bars.has(5) and is_equal_approx(bars[5]["ratio"], 0.5) and bars[5]["color"] == overlay.RIVAL_COLOR, "A damaged rival unit shows a red bar.")
	_apply(world, [
		{"type": "BuildingPlaced", "buildingId": 11, "owner": 1, "buildingType": "Farm", "footprint": {"column": 8, "row": 10, "width": 2, "height": 2}, "hp": 20, "index": 0},
		{"type": "MapRevisionChanged", "revision": 1, "index": 1},
	], 1)
	bars = _bars_by_id(overlay)
	_check(
		bars.has(11) and bars[11]["status"] == "construction" and is_equal_approx(bars[11]["ratio"], 0.1) and bars[11]["color"] == overlay.CONSTRUCTION_COLOR,
		"A construction shows its progress (its HP)."
	)
	_check(bars.get(11, {}).get("icon") == overlay.CONSTRUCTION_ICON, "A construction shows the building icon.")
	_check(
		not bars.get(5, {}).has("icon"),
		"Units have no status icon."
	)
	_apply(world, [
		{"type": "BuildingProgressChanged", "buildingId": 11, "hp": 200, "index": 0},
		{"type": "BuildingCompleted", "buildingId": 11, "index": 1},
	], 1)
	_check(not _bars_by_id(overlay).has(11), "A completed, healthy building has no bar.")
	_apply(world, [{"type": "FarmDepleted", "farmId": 11, "index": 0}], 1)
	bars = _bars_by_id(overlay)
	_check(
		bars.get(11, {}).get("status", "") == "depleted" and bars[11].get("icon") == overlay.EMPTY_FARM_ICON,
		"A depleted farm shows the empty farm icon."
	)
	_apply(world, [
		{"type": "FarmRepairStarted", "farmId": 11, "index": 0},
		{"type": "FarmRepairProgressChanged", "farmId": 11, "workDone": 40, "workRequired": 100, "index": 1},
	], 1)
	bars = _bars_by_id(overlay)
	_check(
		bars.get(11, {}).get("status", "") == "repairing" and is_equal_approx(bars[11].get("secondary_ratio", -1.0), 0.4),
		"A farm under repair shows the repair progress."
	)
	_check(bars.get(11, {}).get("icon") == overlay.CONSTRUCTION_ICON, "A farm under repair shows the building icon.")
	_free_setup(setup)


func _test_unit_animations() -> void:
	var setup := _create_setup()
	var animator: Node = setup["animator"]
	var world: Node = setup["world"]
	var unit: Node3D = world.entities_by_id[3]
	var player := unit.find_child("AnimationPlayer", true, false) as AnimationPlayer
	_check(player != null, "Unit models have an AnimationPlayer.")
	if player == null:
		_free_setup(setup)
		return
	_check(animator.animation_for("Idle", false) == "idle", "Units at rest are idle.")
	_check(animator.animation_for("GatherWood", true) == "walk", "Moving units walk, whatever their order.")
	_check(animator.animation_for("Attack", false) == "attack-melee-right", "Attacking soldiers in place attack.")
	animator.update_units(0.1)
	_check(player.current_animation == "rts/idle", "A unit at rest plays idle in loop.")
	_check(player.get_animation("rts/idle").loop_mode == Animation.LOOP_LINEAR, "The played copy loops.")
	_check(player.get_animation("idle").loop_mode == Animation.LOOP_NONE, "The imported animation stays unchanged.")
	unit.position += Vector3(0.5, 0, 0)
	animator.update_units(1.0)
	_check(player.current_animation == "rts/walk", "A moving unit walks.")
	_check(is_equal_approx(unit.rotation.y, PI / 2.0), "A walking unit faces where it goes (+X).")
	var state: Dictionary = unit.get_meta("entity_state").duplicate()
	state["order"] = "GatherWood"
	unit.set_meta("entity_state", state)
	world.accepted_order_intents[3] = {"kind": "GatherWood", "targetId": 7, "orderId": 5}
	animator.update_units(1.0)
	_check(player.current_animation == "rts/interact-right", "A worker gathering in place plays its task animation.")
	var to_tree := Vector2(12.5, 8.5) - Vector2(unit.position.x, unit.position.z)
	_check(is_equal_approx(unit.rotation.y, atan2(to_tree.x, to_tree.y)), "An own unit in place faces its target.")
	_free_setup(setup)


func _test_unit_death() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	var controller: Node = setup["controller"]
	var overlay: Control = setup["overlay"]
	var unit: Node3D = world.entities_by_id[3]
	var player := unit.find_child("AnimationPlayer", true, false) as AnimationPlayer
	controller.set_selected_units(_ids([3, 4]))
	_apply(world, [
		{"type": "EntityDestroyed", "entityId": 3, "index": 0},
		{"type": "EntityDestroyed", "entityId": 1, "index": 1},
		{"type": "MapRevisionChanged", "revision": 1, "index": 2},
	], 1)
	_check(not world.entities_by_id.has(3), "A destroyed unit leaves the world index at once.")
	_check(is_instance_valid(unit) and unit.is_inside_tree() and unit.get_meta("dying", false), "Its model stays to play the death animation.")
	_check(player != null and player.current_animation == "die", "The destroyed unit plays die.")
	_check(controller.get_selected_units() == [4], "A dying unit is removed from the selection.")
	controller.select_in_screen_rect(Rect2(Vector2.ZERO, root.get_visible_rect().size))
	_check(controller.get_selected_units() == [4], "A dying unit cannot be selected again.")
	_check(not _bars_by_id(overlay).has(3), "A dying unit has no status bar.")
	var main_center: Node = world.active_world.find_child("Building_1", true, false)
	_check(main_center == null or main_center.is_queued_for_deletion(), "Destroyed buildings are removed at once.")
	var ground_y := unit.position.y
	await create_timer(player.get_animation("die").length + world.DEATH_LINGER_SECONDS * 0.5).timeout
	_check(is_instance_valid(unit) and is_equal_approx(unit.position.y, ground_y), "The body lies on the ground after falling.")
	await create_timer(world.DEATH_LINGER_SECONDS * 0.5 + world.DEATH_SINK_SECONDS * 0.6).timeout
	_check(
		is_instance_valid(unit) and unit.position.y < ground_y and unit.position.y > ground_y - world.DEATH_SINK_DEPTH,
		"Then it sinks into the ground instead of vanishing."
	)
	await create_timer(world.DEATH_SINK_SECONDS * 0.4 + 0.3).timeout
	_check(not is_instance_valid(unit), "The body is freed once it is underground.")
	_free_setup(setup)


# --- Utilidades ----------------------------------------------------------------------

func _create_setup() -> Dictionary:
	_tick = 0
	var world: Node = InitialWorldScript.new()
	root.add_child(world)
	# Sin la sesión real: la prueba no pasa por el handshake.
	world._session_controller = null
	var controller: Node = UnitCommandControllerScript.new()
	controller.name = "UnitCommandController"
	world.add_child(controller)
	var animator: Node = UnitAnimationControllerScript.new()
	world.add_child(animator)
	var state: Dictionary = _with_integer_numbers(_example("02-initial-state.json").get("payload", {}))
	_check(world.prepare_initial_state(1, state), "The selection world should build.")
	# Las posiciones las fija la prueba: sin interpolación.
	world.set_process(false)
	animator.set_process(false)
	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 40.0
	camera.position = Vector3(20, 50, 20)
	camera.rotation_degrees = Vector3(-90, 0, 0)
	root.add_child(camera)
	camera.make_current()
	var overlay: Control = EntityStatusOverlayScript.new()
	overlay.world = world
	overlay.command_controller = controller
	root.add_child(overlay)
	return {"world": world, "controller": controller, "animator": animator, "camera": camera, "overlay": overlay}


func _free_setup(setup: Dictionary) -> void:
	_completed_tests += 1
	for key: String in ["overlay", "camera", "world"]:
		var node: Node = setup[key]
		if is_instance_valid(node):
			node.queue_free()


func _place_unit(world: Node, unit_id: int, position: Vector2) -> void:
	var unit: Node3D = world.entities_by_id[unit_id]
	unit.position = Vector3(position.x, 0.0, position.y)
	unit.set_meta("official_position", position)


func _screen_position(setup: Dictionary, unit_id: int) -> Vector2:
	var unit: Node3D = setup["world"].entities_by_id[unit_id]
	return _screen_of(setup, unit.global_position)


func _screen_of(setup: Dictionary, world_position: Vector3) -> Vector2:
	return (setup["camera"] as Camera3D).unproject_position(world_position)


func _bars_by_id(overlay: Control) -> Dictionary:
	var result: Dictionary = {}
	for bar: Dictionary in overlay.collect_status_bars():
		result[bar["entity_id"]] = bar
	return result


func _push_mouse_button(button: MouseButton, pressed: bool, position: Vector2) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = button
	event.pressed = pressed
	event.position = position
	root.push_input(event, true)


func _apply(world: Node, events: Array, revision: int = 0) -> void:
	_tick += 1
	_check(
		world.apply_state_events({"tick": _tick, "mapRevision": revision, "events": events, "commandResults": []}),
		"The StateEvents batch at tick %d should apply." % _tick
	)


func _ids(values: Array) -> Array[int]:
	var result: Array[int] = []
	for value: Variant in values:
		result.append(int(value))
	return result


func _with_integer_numbers(value: Variant) -> Variant:
	match typeof(value):
		TYPE_DICTIONARY:
			var result: Dictionary = {}
			for key: Variant in value:
				result[key] = _with_integer_numbers(value[key])
			return result
		TYPE_ARRAY:
			var result: Array = []
			for item: Variant in value:
				result.append(_with_integer_numbers(item))
			return result
		TYPE_FLOAT:
			if is_equal_approx(value, roundf(value)) and absf(value) < 1e15:
				return int(value)
	return value


func _example(filename: String) -> Dictionary:
	var path := ProjectSettings.globalize_path("res://../docs/protocol/examples/server-to-client").path_join(filename)
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if parsed is Dictionary else {}
