extends SceneTree

## Cursores: autoload CursorManager (imágenes y botones) y MatchCursor (cursor según contexto).

const InitialWorldScript = preload("res://scripts/initial_world.gd")
const UnitCommandControllerScript = preload("res://scripts/unit_command_controller.gd")
const BuildPlacementControllerScript = preload("res://scripts/build_placement_controller.gd")
const MatchCursorScript = preload("res://scripts/match_cursor.gd")

const SOLDIER := 20
const DEPLETED_FARM := 12
const CONSTRUCTION := 13
const EXPECTED_TESTS := 4


class FakeCameraRig:
	extends Node

	var is_dragging := false
	var mouse_edge_margin := 20.0


var _failed := false
var _completed_tests := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_cursor_manager()
	await _test_buttons_use_pointing_hand()
	_test_match_cursor_contexts()
	_test_match_scene_wiring()
	_check(_completed_tests == EXPECTED_TESTS, "Only %d of %d tests completed." % [_completed_tests, EXPECTED_TESTS])
	if _failed:
		quit(1)
		return
	print("Cursor tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _test_cursor_manager() -> void:
	var manager := root.get_node_or_null("CursorManager")
	_check(manager != null, "CursorManager is an autoload.")
	if manager == null:
		_completed_tests += 1
		return
	var expected := {
		"default": "gauntlet_default", "point": "gauntlet_point", "drag": "resize_d_cross",
		"attack": "tool_sword_a", "gather": "tool_axe", "hammer": "tool_hammer", "open": "gauntlet_open",
		"nav_n": "navigation_n", "nav_sw": "navigation_sw",
	}
	for kind: String in expected:
		var texture: Texture2D = manager.texture_for(kind)
		_check(
			texture != null and texture.resource_path.get_file() == "%s.svg" % expected[kind],
			"The %s cursor uses %s.svg." % [kind, expected[kind]]
		)
	for direction: String in ["n", "ne", "e", "se", "s", "sw", "w", "nw"]:
		_check(manager.texture_for("nav_" + direction) != null, "There is a navigation cursor for %s." % direction)
	_check(manager.world_cursor == "default", "The game starts with the default gauntlet.")
	manager.set_world_cursor("attack")
	_check(manager.world_cursor == "attack", "The world cursor can change.")
	manager.set_world_cursor("not-a-cursor")
	_check(manager.world_cursor == "attack", "Unknown cursors are ignored.")

	# Guante abierto (al terminar un cuadro de selección, lo pide MatchCursor).
	manager.set_world_cursor("default")
	manager.flash_open()
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) == "open", "The default gauntlet opens.")
	_check(manager.displayed_cursor(Input.CURSOR_POINTING_HAND) == "open", "The button gauntlet opens too.")
	manager.set_world_cursor("point")
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) == "open", "Another gauntlet waits until the open one ends.")
	manager._process(manager.OPEN_FLASH_SECONDS + 0.01)
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) == "point", "After an instant the current cursor comes back.")
	_check(manager.displayed_cursor(Input.CURSOR_POINTING_HAND) == "point", "Buttons go back to the pointing gauntlet.")
	manager.set_world_cursor("attack")
	manager.flash_open()
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) == "attack", "Tools do not open.")
	manager.set_world_cursor("default")
	manager._process(1.0)
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) == "default", "A tool switching back to the gauntlet shows it.")
	var release := InputEventMouseButton.new()
	release.button_index = MOUSE_BUTTON_LEFT
	release.pressed = false
	release.position = Vector2(200, 200)
	root.push_input(release, true)
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) == "default", "A plain click release does not open the gauntlet.")
	_completed_tests += 1


func _test_buttons_use_pointing_hand() -> void:
	var panel := VBoxContainer.new()
	var button := Button.new()
	var input := LineEdit.new()
	panel.add_child(button)
	panel.add_child(input)
	root.add_child(panel)
	await process_frame
	_check(button.mouse_default_cursor_shape == Control.CURSOR_POINTING_HAND, "Buttons show the pointing gauntlet.")
	_check(input.mouse_default_cursor_shape == Control.CURSOR_IBEAM, "Text fields keep their text cursor.")
	panel.queue_free()
	_completed_tests += 1


func _test_match_cursor_contexts() -> void:
	var setup := _create_setup()
	var cursor: Node = setup["cursor"]
	var controller: Node = setup["controller"]
	var rig: FakeCameraRig = setup["rig"]
	var size: Vector2 = root.get_visible_rect().size
	var center := size / 2.0

	_check(cursor.resolve_cursor(center, false) == "default", "With nothing selected the cursor is the default gauntlet.")
	rig.is_dragging = true
	_check(cursor.resolve_cursor(center, false) == "drag", "Dragging the camera shows the move cross.")
	_check(cursor.resolve_cursor(Vector2(5, 5), true) == "drag", "Dragging wins over edges and the interface.")
	rig.is_dragging = false

	var edges := {
		Vector2(center.x, 5): "nav_n", Vector2(size.x - 5, 5): "nav_ne", Vector2(size.x - 5, center.y): "nav_e",
		Vector2(size.x - 5, size.y - 5): "nav_se", Vector2(center.x, size.y - 5): "nav_s",
		Vector2(5, size.y - 5): "nav_sw", Vector2(5, center.y): "nav_w", Vector2(5, 5): "nav_nw",
	}
	for position: Vector2 in edges:
		_check(cursor.resolve_cursor(position, false) == edges[position], "At %s the cursor is %s." % [str(position), edges[position]])
	_check(cursor.resolve_cursor(Vector2(30, center.y), false) == "default", "Past the camera margin there is no navigation cursor.")

	controller.set_selected_units(_ids([3]))
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(20, 0, 22)), false) == "point", "A worker over the ground would move: pointing gauntlet.")
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(20, 0, 22)), true) == "default", "Over the interface the cursor is the default one.")
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(12.5, 0, 8.5)), false) == "gather", "A worker over a tree shows the axe.")
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(15, 0, 15)), false) == "hammer", "A worker over an own depleted farm shows the hammer.")
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(19, 0, 15)), false) == "hammer", "A worker over an own construction shows the hammer.")
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(36, 0, 36)), false) == "default", "Workers cannot attack: no sword over the rival base.")

	controller.set_selected_units(_ids([SOLDIER]))
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(36, 0, 36)), false) == "attack", "A soldier over a rival building shows the sword.")
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(32.5, 0, 36.5)), false) == "attack", "A soldier over a rival unit shows the sword.")
	_check(
		cursor.resolve_cursor(_screen_of(setup, Vector3(15, 0, 15)), false) == "default",
		"A soldier cannot repair: the click would be ignored (plan 06 §2), so no hammer."
	)

	# El guante se abre solo al terminar un cuadro de selección, no con un clic simple.
	var manager := root.get_node("CursorManager")
	manager._process(1.0)
	cursor.set_process(false)
	controller.clear_selection()
	_push_mouse_button(true, center)
	_push_mouse_button(false, center)
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) != "open", "A simple click does not open the gauntlet.")
	_push_mouse_button(true, center - Vector2(150, 150))
	var motion := InputEventMouseMotion.new()
	motion.position = center + Vector2(150, 150)
	root.push_input(motion, true)
	_push_mouse_button(false, center + Vector2(150, 150))
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) == "open", "Finishing a selection box opens the gauntlet for an instant.")
	manager._process(1.0)
	_check(manager.displayed_cursor(Input.CURSOR_ARROW) != "open", "Then the gauntlet closes again.")

	controller.set_selected_units(_ids([3]))
	setup["placement"].start_placement("Farm")
	_check(cursor.resolve_cursor(_screen_of(setup, Vector3(15, 0, 15)), false) == "default", "While placing a building the cursor is the default one.")
	_free_setup(setup)


func _test_match_scene_wiring() -> void:
	var scene: Node = load("res://scenes/match_world.tscn").instantiate()
	var cursor := scene.get_node_or_null("MatchCursor")
	_check(cursor != null, "The match scene has a MatchCursor.")
	if cursor != null:
		_check(cursor.camera_rig == scene.get_node("RTSCamera"), "MatchCursor reads the RTS camera.")
		_check(cursor.command_controller == scene.get_node("UnitCommandController"), "MatchCursor reads the selection.")
		_check(cursor.placement_controller == scene.get_node("BuildPlacementController"), "MatchCursor knows about placement.")
	scene.free()
	_completed_tests += 1


# --- Utilidades ----------------------------------------------------------------------

func _create_setup() -> Dictionary:
	var world: Node = InitialWorldScript.new()
	root.add_child(world)
	world._session_controller = null
	var controller: Node = UnitCommandControllerScript.new()
	controller.name = "UnitCommandController"
	world.add_child(controller)
	var placement: Node = BuildPlacementControllerScript.new()
	placement.name = "BuildPlacementController"
	world.add_child(placement)
	var state: Dictionary = _with_integer_numbers(_example("02-initial-state.json").get("payload", {}))
	state["units"].append({"unitId": SOLDIER, "owner": 1, "unitType": "Soldier", "x": 9.5, "z": 14.5, "hp": 80, "order": "Idle"})
	state["buildings"].append({
		"buildingId": CONSTRUCTION, "owner": 1, "buildingType": "Barracks",
		"footprint": {"column": 18, "row": 14, "width": 3, "height": 3}, "hp": 40, "isCompleted": false,
	})
	state["buildings"].append({
		"buildingId": DEPLETED_FARM, "owner": 1, "buildingType": "Farm",
		"footprint": {"column": 14, "row": 14, "width": 2, "height": 2}, "hp": 200, "isCompleted": true,
		"farm": {"isDepleted": true, "isRepairing": false},
	})
	_check(world.prepare_initial_state(1, state), "The cursor world should build.")
	world.set_process(false)
	var camera := Camera3D.new()
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 40.0
	camera.position = Vector3(20, 50, 20)
	camera.rotation_degrees = Vector3(-90, 0, 0)
	root.add_child(camera)
	camera.make_current()
	var rig := FakeCameraRig.new()
	root.add_child(rig)
	var cursor: Node = MatchCursorScript.new()
	cursor.camera_rig = rig
	cursor.command_controller = controller
	cursor.placement_controller = placement
	cursor.set_process(false)
	world.add_child(cursor)
	return {"world": world, "controller": controller, "placement": placement, "camera": camera, "rig": rig, "cursor": cursor}


func _free_setup(setup: Dictionary) -> void:
	_completed_tests += 1
	for key: String in ["camera", "rig", "world"]:
		var node: Node = setup[key]
		if is_instance_valid(node):
			node.queue_free()


func _push_mouse_button(pressed: bool, position: Vector2) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = pressed
	event.position = position
	root.push_input(event, true)


func _screen_of(setup: Dictionary, world_position: Vector3) -> Vector2:
	return (setup["camera"] as Camera3D).unproject_position(world_position)


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
