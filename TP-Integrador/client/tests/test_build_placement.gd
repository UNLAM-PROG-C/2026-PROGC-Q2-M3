extends SceneTree

## Build en su forma "colocar" (contrato 03 §3): validación local para la vista previa,
## comando con rutas hasta la huella, vallas por celda (D-033), controlador de colocación
## y barra de construcción del HUD.

const InitialWorldScript = preload("res://scripts/initial_world.gd")
const UnitCommandControllerScript = preload("res://scripts/unit_command_controller.gd")
const BuildPlacementControllerScript = preload("res://scripts/build_placement_controller.gd")
const MatchHudScript = preload("res://scripts/ui/match_hud.gd")
const ClientNavigationScript = preload("res://scripts/client_navigation.gd")

const BUILD_RANGE := 1.5
const EXPECTED_TESTS := 7


class FakeNetworkClient:
	extends Node

	signal message_received(message_type: String, payload: Variant, envelope: Dictionary)

	var sent_messages: Array[Dictionary] = []

	func send_message(message_type: String, payload: Variant = null, message_id: Variant = null) -> bool:
		sent_messages.append({"type": message_type, "payload": payload.duplicate(true), "messageId": message_id})
		return true


class FakeSessionController:
	extends Node

	signal session_state_changed(state: int, state_name: String)

	func can_issue_gameplay() -> bool:
		return true

	func get_state_name() -> String:
		return "Running"

	func complete_initial_state_preparation(_id: int, _ok: bool, _detail: String = "") -> bool:
		return true


var _failed := false
var _completed_tests := 0
var _tick := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_fence_line_cells()
	_test_placement_validation()
	await _test_build_place_command()
	await _test_fence_line_commands()
	_test_preview_and_mode()
	_test_placement_consumes_clicks()
	_test_hud_build_panel()
	_check(_completed_tests == EXPECTED_TESTS, "Only %d of %d tests completed." % [_completed_tests, EXPECTED_TESTS])
	if _failed:
		quit(1)
		return
	print("Build placement tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _test_fence_line_cells() -> void:
	var line := BuildPlacementControllerScript.fence_line_cells(Vector2i(2, 10), Vector2i(6, 11))
	_check(line == [Vector2i(2, 10), Vector2i(3, 10), Vector2i(4, 10), Vector2i(5, 10), Vector2i(6, 10)], "A mostly horizontal drag makes a horizontal line.")
	line = BuildPlacementControllerScript.fence_line_cells(Vector2i(4, 9), Vector2i(3, 6))
	_check(line == [Vector2i(4, 9), Vector2i(4, 8), Vector2i(4, 7), Vector2i(4, 6)], "A mostly vertical drag makes a vertical line, in either direction.")
	_check(BuildPlacementControllerScript.fence_line_cells(Vector2i(5, 5), Vector2i(5, 5)) == [Vector2i(5, 5)], "A click without drag places one fence.")
	_check(
		BuildPlacementControllerScript.fence_line_cells(Vector2i(0, 0), Vector2i(39, 0)).size() == BuildPlacementControllerScript.MAX_FENCE_CELLS,
		"Long lines are capped."
	)
	_completed_tests += 1


func _test_placement_validation() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	var ok: Dictionary = world.validate_placement("Farm", 8, 10)
	_check(ok.get("ok", false) and ok.get("woodCost", 0) == 60, "The example farm site is valid and costs 60 wood.")
	_check(world.validate_placement("MainCenter", 8, 10).get("error", "") == "NotBuildable", "Main centers cannot be built.")
	_check(world.validate_placement("Farm", 39, 10).get("error", "") == "OutOfBounds", "Footprints must fit in the map.")
	_check(world.validate_placement("Farm", -1, 10).get("error", "") == "OutOfBounds", "Negative anchors are out of bounds.")
	_check(world.validate_placement("Farm", 5, 5).get("error", "") == "CellOccupied", "Footprints cannot overlap buildings.")
	_check(world.validate_placement("Farm", 12, 7).get("error", "") == "TreeReserved", "Footprints cannot cover a tree cell.")
	_apply(world, [{"type": "TreeDepleted", "treeId": 7, "index": 0}])
	_check(world.validate_placement("Farm", 12, 7).get("error", "") == "TreeReserved", "Depleted trees keep their cell reserved.")
	_check(world.validate_placement("Farm", 7, 3).get("error", "") == "UnitInFootprint", "Footprints cannot contain a unit.")
	_check(world.validate_placement("Barracks", 20, 20).get("ok", false), "Barracks fit while there is enough wood.")
	_apply(world, [{"type": "ResourcesChanged", "wood": 100, "food": 100, "index": 0}])
	_check(world.validate_placement("Barracks", 20, 20).get("error", "") == "InsufficientWood", "Barracks need 150 wood.")
	_check(world.validate_placement("Fence", 20, 20).get("ok", false), "A fence is a 1×1 site and costs 5 wood.")
	_free_setup(setup)


func _test_build_place_command() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	var network: FakeNetworkClient = setup["network"]
	var result: Dictionary = await world.issue_build_place_command("Farm", 8, 10, _ids([3, 4, 5]))
	_check(result.get("ok", false), "Build place should be sent: %s" % str(result))
	if result.get("ok", false):
		var sent: Dictionary = network.sent_messages[-1]
		var example: Dictionary = _example("client-to-server", "build-place.json")
		_check(sent["type"] == "Build", "Build place uses the Build type (D-032).")
		_check(_same_keys(sent["payload"], example["payload"]), "Build place payload must match build-place.json.")
		_check(
			sent["payload"]["buildingType"] == "Farm" and sent["payload"]["anchorColumn"] == 8 and sent["payload"]["anchorRow"] == 10,
			"The payload carries the type and the anchor cell."
		)
		var routed: Array = []
		for route: Dictionary in sent["payload"]["routes"]:
			routed.append(int(route["unitId"]))
		_check(routed == [3, 4], "Only own workers are sent to build.")
		var footprint := Rect2(8.0, 10.0, 2.0, 2.0)
		for route: Dictionary in sent["payload"]["routes"]:
			var previous: Vector2 = world.entities_by_id[int(route["unitId"])].get_meta("official_position")
			var end := previous
			for waypoint: Dictionary in route["waypoints"]:
				var point := Vector2(float(waypoint["x"]), float(waypoint["z"]))
				for step in range(1, 11):
					_check(
						not footprint.has_point(previous.lerp(point, step / 10.0)),
						"A build route must not cross the hypothetical footprint."
					)
				previous = point
				end = point
			_check(
				ClientNavigationScript.distance_to_target(end, {"rect": footprint}) <= BUILD_RANGE,
				"The build route must end within build range of the footprint."
			)

		# Reconciliación: la obra creada en el mismo lote pasa a ser el objetivo de la orden.
		var message_id := int(result["messageId"])
		_apply(world, [
			{"type": "BuildingPlaced", "buildingId": 11, "owner": 1, "buildingType": "Farm", "footprint": {"column": 8, "row": 10, "width": 2, "height": 2}, "hp": 20, "index": 0},
			{"type": "MapRevisionChanged", "revision": 1, "index": 1},
			{"type": "UnitOrderChanged", "unitId": 3, "orderId": 3, "kind": "Build", "index": 2},
		], [{"clientCommandId": message_id, "sequence": 3, "tick": _tick + 1, "outcome": "PartiallyAccepted", "entityResults": [{"entityId": 3}, {"entityId": 4, "error": "Movement.InvalidPath"}]}], 1)
		var intent: Dictionary = world.accepted_order_intents.get(3, {})
		_check(
			intent.get("kind", "") == "Build" and intent.get("targetId", 0) == 11 and intent.get("orderId", 0) == 3,
			"An accepted placement targets the building created in the same batch."
		)
		_check(world.validate_placement("Farm", 8, 10).get("error", "") == "CellOccupied", "A placed construction occupies its footprint.")
	var no_workers: Dictionary = await world.issue_build_place_command("Farm", 20, 20, _ids([5]))
	_check(no_workers.get("error", "") == "NoCompatibleUnits", "Without own workers nothing is sent.")
	var invalid: Dictionary = await world.issue_build_place_command("Farm", 5, 5, _ids([3]))
	_check(invalid.get("error", "") == "CellOccupied", "An invalid site is rejected locally, before routing.")
	_free_setup(setup)


func _test_fence_line_commands() -> void:
	var setup := _create_setup()
	var placement: Node = setup["placement"]
	var network: FakeNetworkClient = setup["network"]
	setup["controller"].set_selected_units(_ids([3, 4]))
	_check(placement.start_placement("Fence"), "Fences can be placed with workers selected.")
	var cells: Array[Vector2i] = [Vector2i(10, 4), Vector2i(11, 4), Vector2i(12, 4)]
	var results: Array[Dictionary] = await placement.place_fence_line(cells)
	_check(results.size() == 3 and results.all(func(r: Dictionary) -> bool: return r.get("ok", false)), "Every fence cell is sent even with pending commands: %s" % str(results))
	_check(network.sent_messages.size() == 3, "One Build command per fence cell (D-033).")
	var expected_workers := [3, 4, 3]
	for index in range(network.sent_messages.size()):
		var payload: Dictionary = network.sent_messages[index]["payload"]
		_check(
			payload["buildingType"] == "Fence" and payload["anchorColumn"] == 10 + index and payload["anchorRow"] == 4,
			"Fence %d targets its own cell." % index
		)
		_check(
			payload["routes"].size() == 1 and int(payload["routes"][0]["unitId"]) == expected_workers[index],
			"Workers are assigned to fences in turn."
		)
	_check(not placement.is_placing(), "Placing a fence line ends the placement mode.")
	_free_setup(setup)


func _test_preview_and_mode() -> void:
	var setup := _create_setup()
	var placement: Node = setup["placement"]
	var controller: Node = setup["controller"]
	_check(not placement.start_placement("Farm"), "Placement needs at least one selected worker.")
	controller.set_selected_units(_ids([3]))
	var modes: Array[String] = []
	placement.placement_mode_changed.connect(func(mode: String) -> void: modes.append(mode))
	_check(placement.start_placement("Farm"), "A selected worker can place a farm.")
	_check(not placement.start_placement("MainCenter"), "Main centers are not placeable.")
	placement._show_preview([Vector2i(8, 10), Vector2i(5, 5)])
	_check(
		placement.preview_results.size() == 2
			and placement.preview_results[0].get("ok", false)
			and placement.preview_results[1].get("error", "") == "CellOccupied",
		"The preview validates each anchor."
	)
	var ghosts: Array = placement.get_children().filter(func(child: Node) -> bool: return child is MeshInstance3D and child.visible)
	_check(ghosts.size() == 2, "Each previewed anchor has a ghost.")
	if ghosts.size() == 2:
		_check(ghosts[0].material_override.albedo_color.is_equal_approx(placement.VALID_COLOR), "Valid sites are green.")
		_check(ghosts[1].material_override.albedo_color.is_equal_approx(placement.INVALID_COLOR), "Invalid sites are red.")
		_check(ghosts[0].position.is_equal_approx(Vector3(9.0, placement.GHOST_HEIGHT / 2.0, 11.0)), "The ghost covers the footprint.")
	_check(placement.anchor_for_point(Vector2(9.5, 11.5)) == Vector2i(9, 11), "A 2×2 footprint is anchored at the cell under the cursor.")
	placement.building_type = "Barracks"
	_check(placement.anchor_for_point(Vector2(9.5, 11.5)) == Vector2i(8, 10), "A 3×3 footprint is centered on the cell under the cursor.")
	controller.clear_selection()
	_check(not placement.is_placing(), "Losing every worker cancels the placement.")
	_check(modes == ["Farm", ""], "Starting and cancelling are notified.")
	_check(placement.get_children().all(func(child: Node) -> bool: return not (child is MeshInstance3D) or not child.visible), "Cancelling hides the preview.")
	_free_setup(setup)


func _test_placement_consumes_clicks() -> void:
	var setup := _create_setup()
	var placement: Node = setup["placement"]
	var controller: Node = setup["controller"]
	controller.set_selected_units(_ids([3, 4]))
	placement.start_placement("Fence")
	var click := InputEventMouseButton.new()
	click.button_index = MOUSE_BUTTON_LEFT
	click.pressed = true
	click.position = Vector2(10, 10)
	root.push_input(click)
	_check(controller.get_selected_units() == [3, 4], "While placing, a left click does not change the unit selection.")
	var cancel := InputEventMouseButton.new()
	cancel.button_index = MOUSE_BUTTON_RIGHT
	cancel.pressed = true
	cancel.position = Vector2(10, 10)
	root.push_input(cancel)
	_check(not placement.is_placing(), "A right click cancels the placement.")
	_check(controller.get_selected_units() == [3, 4], "Cancelling keeps the selected workers.")
	_free_setup(setup)


func _test_hud_build_panel() -> void:
	var setup := _create_setup()
	var hud: Control = setup["hud"]
	var controller: Node = setup["controller"]
	var placement: Node = setup["placement"]
	var panel := hud.find_child("BuildPanel", true, false) as Control
	_check(not panel.visible, "Without workers selected there is no build panel.")
	controller.set_selected_units(_ids([3]))
	_check(panel.visible, "Selecting a worker shows the build panel.")
	var texts: Array = []
	for building_type: String in ["Farm", "Barracks", "Tower", "Fence"]:
		var button := hud.find_child("Build%s" % building_type, true, false) as Button
		var content := button.get_node("Content") as HBoxContainer
		var parts: Array = []
		for child: Node in content.get_children():
			parts.append(str(child.name))
		_check(parts == ["NameLabel", "CostIcon", "CostLabel"], "The wood icon sits between the name and the cost: %s" % str(parts))
		_check((content.get_node("CostIcon") as TextureRect).texture == hud.WOOD_ICON, "The cost uses the wood icon.")
		_check(content.mouse_filter == Control.MOUSE_FILTER_IGNORE, "The button content does not take the click.")
		_check(button.custom_minimum_size.x >= content.get_combined_minimum_size().x, "The button fits its content.")
		texts.append("%s | %s" % [(content.get_node("NameLabel") as Label).text, (content.get_node("CostLabel") as Label).text])
	_check(
		texts == ["[1] Granja | 60", "[2] Cuartel | 150", "[3] Torre | 120", "[4] Valla | 5"],
		"Build buttons show their key and their wood cost from the catalog: %s" % str(texts)
	)
	var farm_button := hud.find_child("BuildFarm", true, false) as Button
	farm_button.pressed.emit()
	_check(placement.is_placing() and placement.building_type == "Farm", "A build button starts the placement.")
	_check(farm_button.button_pressed and (hud.find_child("BuildHint", true, false) as Label).visible, "The active building and the controls are shown.")
	farm_button.pressed.emit()
	_check(not placement.is_placing(), "Pressing the active building again cancels.")
	# Atajos: acciones build_* del InputMap (teclas 1 a 4 en project.godot).
	_press_action("build_barracks")
	_check(placement.is_placing() and placement.building_type == "Barracks", "The build_barracks action selects the barracks.")
	_press_action("build_fence")
	_check(placement.building_type == "Fence", "Another build action switches the building to place.")
	_press_action("build_fence")
	_check(not placement.is_placing(), "Pressing the active building's action again cancels.")
	for action: String in ["build_farm", "build_barracks", "build_tower", "build_fence"]:
		_check(InputMap.has_action(action), "The %s action exists in the project." % action)
	controller.set_selected_building(1)
	_check(not panel.visible, "Selecting a building hides the build panel.")
	_press_action("build_farm")
	_check(not placement.is_placing(), "Build actions do nothing while the build panel is hidden.")
	_free_setup(setup)


# --- Utilidades ----------------------------------------------------------------------

func _create_setup() -> Dictionary:
	_tick = 0
	var world: Node = InitialWorldScript.new()
	root.add_child(world)
	var network := FakeNetworkClient.new()
	var session := FakeSessionController.new()
	root.add_child(network)
	root.add_child(session)
	world._network_client = network
	world._session_controller = session
	var controller: Node = UnitCommandControllerScript.new()
	controller.name = "UnitCommandController"
	world.add_child(controller)
	var placement: Node = BuildPlacementControllerScript.new()
	placement.name = "BuildPlacementController"
	world.add_child(placement)
	_check(world.prepare_initial_state(1, _with_integer_numbers(_example("server-to-client", "02-initial-state.json").get("payload", {}))), "The build world should build.")
	var hud: Control = MatchHudScript.new()
	root.add_child(hud)
	hud.bind(world, controller, session, placement)
	return {"world": world, "network": network, "session": session, "controller": controller, "placement": placement, "hud": hud}


func _free_setup(setup: Dictionary) -> void:
	_completed_tests += 1
	for key: String in ["hud", "world", "network", "session"]:
		var node: Node = setup[key]
		if is_instance_valid(node):
			node.queue_free()


func _apply(world: Node, events: Array, command_results: Array = [], revision: int = -1) -> void:
	_tick += 1
	var map_revision: int = world.map_revision if revision < 0 else revision
	_check(
		world.apply_state_events({"tick": _tick, "mapRevision": map_revision, "events": events, "commandResults": command_results}),
		"The StateEvents batch at tick %d should apply." % _tick
	)


## Simula la tecla de una acción a partir de su evento en el InputMap, sin asumir cuál es.
func _press_action(action: String) -> void:
	var key_event: InputEventKey = null
	for input_event: InputEvent in InputMap.action_get_events(action):
		if input_event is InputEventKey:
			key_event = input_event.duplicate()
			break
	_check(key_event != null, "The %s action needs a key." % action)
	if key_event == null:
		return
	key_event.pressed = true
	root.push_input(key_event)
	var release: InputEventKey = key_event.duplicate()
	release.pressed = false
	root.push_input(release)


func _ids(values: Array) -> Array[int]:
	var result: Array[int] = []
	for value: Variant in values:
		result.append(int(value))
	return result


func _same_keys(actual: Dictionary, expected: Dictionary) -> bool:
	var actual_keys := actual.keys()
	var expected_keys := expected.keys()
	actual_keys.sort()
	expected_keys.sort()
	return actual_keys == expected_keys


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


func _example(direction: String, filename: String) -> Dictionary:
	var path := ProjectSettings.globalize_path("res://../docs/protocol/examples").path_join(direction).path_join(filename)
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		_check(false, "Could not open protocol example %s/%s." % [direction, filename])
		return {}
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	return parsed if parsed is Dictionary else {}
