extends SceneTree

## Comandos de unidades de la fase 2 (plan 06): Stop, GatherWood, Attack, Build (continuar)
## y RepairFarm. Los payloads se comparan con docs/protocol/examples/client-to-server y las
## rutas se calculan con la navegación real, verificando que terminen dentro del rango.

const InitialWorldScript = preload("res://scripts/initial_world.gd")
const ClientNavigationScript = preload("res://scripts/client_navigation.gd")
const UnitCommandControllerScript = preload("res://scripts/unit_command_controller.gd")

const GATHER_RANGE := 1.5
const BUILD_RANGE := 1.5
const ATTACK_RANGE := 1.5
const SOLDIER_A := 20
const SOLDIER_B := 21
const UNFINISHED_FARM := 11
const DEPLETED_FARM := 12
const PRODUCING_FARM := 13


class FakeNetworkClient:
	extends Node

	signal message_received(message_type: String, payload: Variant, envelope: Dictionary)

	var sent_messages: Array[Dictionary] = []

	func send_message(message_type: String, payload: Variant = null, message_id: Variant = null) -> bool:
		sent_messages.append({
			"type": message_type,
			"payload": payload.duplicate(true) if payload is Dictionary else payload,
			"messageId": message_id,
		})
		return true


class FakeSessionController:
	extends Node

	signal initial_state_preparation_requested(initialization_id: int, payload: Dictionary)
	signal initial_state_discard_requested(initialization_id: int)
	signal session_state_changed(state: int, state_name: String)

	var running := true

	func can_issue_gameplay() -> bool:
		return running

	func complete_initial_state_preparation(
		_initialization_id: int,
		_succeeded: bool,
		_detail: String = ""
	) -> bool:
		return true


const EXPECTED_TESTS := 9

var _failed := false
var _completed_tests := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_range_geometry_helpers()
	await _test_move_command_with_real_navigation()
	await _test_navigation_sources_are_hidden_but_baked()
	await _test_stop_command()
	await _test_gather_wood_and_task_repath()
	await _test_attack_command()
	await _test_build_continue_and_repair_farm()
	await _test_rejected_task_command_retry()
	_test_contextual_command_resolution()
	# Un SCRIPT ERROR corta la función sin marcar _failed: cada test debe llegar a su final.
	_check(_completed_tests == EXPECTED_TESTS, "Only %d of %d tests completed." % [_completed_tests, EXPECTED_TESTS])
	if _failed:
		quit(1)
		return
	print("Gameplay command tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


# --- Geometría de rango (pura, sin navegación) ---------------------------------------

func _test_range_geometry_helpers() -> void:
	var rect_target := {"rect": Rect2(34.0, 34.0, 4.0, 4.0)}
	_check(
		is_zero_approx(ClientNavigationScript.distance_to_target(Vector2(35.0, 35.0), rect_target)),
		"A point inside a footprint is at distance 0 from it."
	)
	_check(
		is_equal_approx(ClientNavigationScript.distance_to_target(Vector2(31.0, 36.0), rect_target), 3.0),
		"Distance to a footprint is measured to its nearest edge (D-019)."
	)
	_check(
		is_equal_approx(ClientNavigationScript.distance_to_target(Vector2(31.0, 30.0), rect_target), 5.0),
		"Distance to a footprint corner is Euclidean."
	)
	var point_target := {"point": Vector2(10.0, 0.5)}
	var waypoints: Array = [{"x": 5.0, "z": 0.5}, {"x": 10.0, "z": 0.5}]
	var trimmed := ClientNavigationScript.trim_route_to_range(Vector2(0.0, 0.5), waypoints, point_target, 1.5)
	_check(trimmed.size() == 2, "The trimmed route keeps waypoints until it enters the range.")
	var end := Vector2(float(trimmed[-1]["x"]), float(trimmed[-1]["z"]))
	_check(
		absf(end.distance_to(point_target["point"]) - 1.5) < 0.001,
		"The trimmed route ends where it first enters the interaction range."
	)
	_check(
		ClientNavigationScript.trim_route_to_range(Vector2(9.0, 0.5), waypoints, point_target, 1.5).is_empty(),
		"A unit already in range needs an empty route (contrato 04 §3)."
	)
	_check(
		ClientNavigationScript.effective_interaction_range(1.5) < 1.5
			and ClientNavigationScript.effective_interaction_range(1.5) > 1.4,
		"The effective range keeps a small safety margin below the catalog range."
	)
	_completed_tests += 1


# --- Move con navegación real ---------------------------------------------------------

func _test_move_command_with_real_navigation() -> void:
	var setup := _create_running_world()
	var world: Node = setup["world"]
	var network: FakeNetworkClient = setup["network"]
	var destination := Vector2(10.5, 6.5)
	var result: Dictionary = await world.issue_move_command(destination, _ids([3]))
	_check(result.get("ok", false), "Move should be routed on the baked NavigationMesh: %s" % str(result))
	if result.get("ok", false):
		var sent: Dictionary = network.sent_messages[-1]
		_check(
			_same_keys(sent["payload"], _client_example("move.json")["payload"]),
			"Move payload must match move.json."
		)
		var waypoints: Array = sent["payload"]["routes"][0]["waypoints"]
		var last := Vector2(float(waypoints[-1]["x"]), float(waypoints[-1]["z"]))
		_check(last.is_equal_approx(destination), "The Move route must end at the destination.")
	_free_setup(setup)


# --- Geometría de horneado -----------------------------------------------------------

func _test_navigation_sources_are_hidden_but_baked() -> void:
	var setup := _create_running_world()
	var world: Node = setup["world"]
	var network: FakeNetworkClient = setup["network"]
	# El Centro Principal propio ocupa X/Z [2, 6]: la recta (1, 4) → (7, 4) lo atraviesa.
	var unit: Node3D = world.entities_by_id[3]
	world._add_position_sample(3, 0, Vector2(1.0, 4.0), Time.get_ticks_usec())
	unit.set_meta("official_position", Vector2(1.0, 4.0))
	var result: Dictionary = await world.issue_move_command(Vector2(7.0, 4.0), _ids([3]))
	_check(result.get("ok", false), "A Move around a building should be routed: %s" % str(result))
	if result.get("ok", false):
		var footprint := Rect2(2.0, 2.0, 4.0, 4.0)
		var previous := Vector2(1.0, 4.0)
		var waypoints: Array = network.sent_messages[-1]["payload"]["routes"][0]["waypoints"]
		_check(waypoints.size() > 1, "The route must detour around the building instead of going straight.")
		for waypoint: Dictionary in waypoints:
			var point := Vector2(float(waypoint["x"]), float(waypoint["z"]))
			for step in range(1, 21):
				var sample := previous.lerp(point, float(step) / 20.0)
				_check(
					not footprint.grow(-0.01).has_point(sample),
					"The route crosses the main center at %s: hidden obstacles must still be baked." % str(sample)
				)
			previous = point
	# Las fuentes se crean al hornear, así que se revisan después de que la ruta esperó la navegación.
	var sources: Node = world.active_world.find_child("NavigationSources", true, false)
	_check(sources != null and sources.get_child_count() > 1, "The navigation sources include the floor and the buildings.")
	if sources != null:
		for source: Node in sources.get_children():
			_check(not (source as Node3D).visible, "%s is bake-only geometry and must not be drawn." % source.name)
	_free_setup(setup)


# --- Stop ----------------------------------------------------------------------------

func _test_stop_command() -> void:
	var setup := _create_running_world()
	var world: Node = setup["world"]
	var network: FakeNetworkClient = setup["network"]
	var result: Dictionary = await world.issue_stop_command(_ids([4, 3, 5, 99, 3]))
	_check(result.get("ok", false), "Stop with own units should be sent.")
	_check(network.sent_messages.size() == 1, "Stop should send exactly one message.")
	var sent: Dictionary = network.sent_messages[0]
	_check(sent["type"] == "Stop" and sent["messageId"] == 1, "Stop should use the first increasing messageId.")
	_check(
		_same_keys(sent["payload"], _client_example("stop.json")["payload"]),
		"Stop payload must match stop.json."
	)
	_check(sent["payload"]["unitIds"] == [3, 4], "Stop should keep only distinct own units, sorted.")
	_check(
		not world.pending_command_by_unit.has(3),
		"Stop must not lock units, so the player can stop a just-issued order."
	)
	var rejected: Dictionary = await world.issue_stop_command(_ids([5]))
	_check(rejected.get("error", "") == "NoUnitsSelected", "Stop with only rival units must not be sent.")
	setup["session"].running = false
	var not_running: Dictionary = await world.issue_stop_command(_ids([3]))
	_check(not_running.get("error", "") == "MatchNotRunning", "Gameplay commands require Running.")
	_free_setup(setup)


# --- GatherWood y repath con targetId ------------------------------------------------

func _test_gather_wood_and_task_repath() -> void:
	var setup := _create_running_world()
	var world: Node = setup["world"]
	var network: FakeNetworkClient = setup["network"]
	var result: Dictionary = await world.issue_gather_wood_command(7, _ids([3, SOLDIER_A]))
	_check(result.get("ok", false), "GatherWood should be sent: %s" % str(result))
	if not result.get("ok", false):
		_free_setup(setup)
		return
	var sent: Dictionary = network.sent_messages[-1]
	_check(sent["type"] == "GatherWood", "GatherWood should use its protocol type.")
	_check(
		_same_keys(sent["payload"], _client_example("gather-wood.json")["payload"]),
		"GatherWood payload must match gather-wood.json."
	)
	_check(int(sent["payload"]["treeId"]) == 7, "GatherWood should target the clicked tree.")
	_check(
		_route_unit_ids(sent["payload"]) == [3],
		"Soldiers in a mixed selection must not be sent to gather (plan 06 §2)."
	)
	_check_route_ends_in_range(world, sent["payload"], 3, {"point": Vector2(12.5, 8.5)}, GATHER_RANGE, "GatherWood")
	_check(world.pending_command_by_unit.get(3, -1) == result["messageId"], "GatherWood should lock its worker.")
	var duplicate: Dictionary = await world.issue_gather_wood_command(8, _ids([3]))
	_check(duplicate.get("error", "") == "CommandAlreadyPending", "A unit with a pending command is locked.")

	var accepted: bool = world.apply_state_events(_state_events(
		1,
		0,
		[{"type": "UnitOrderChanged", "unitId": 3, "orderId": 5, "kind": "GatherWood", "index": 0}],
		[_accepted_result(int(result["messageId"]), [3])]
	))
	_check(accepted, "The accepted GatherWood batch should apply.")
	_check(not world.pending_command_by_unit.has(3), "The official result should release the unit lock.")
	var intent: Dictionary = world.accepted_order_intents.get(3, {})
	_check(
		intent.get("kind", "") == "GatherWood" and intent.get("targetId", 0) == 7 and intent.get("orderId", 0) == 5,
		"An accepted task should keep its kind, target and OrderId for repaths."
	)

	var repath: Dictionary = await world.build_repath_response_payload(
		{"unitId": 3, "orderId": 5, "mapRevision": 0, "targetId": 7, "isPaused": true}
	)
	_check(repath.get("ok", false), "A repath for the current GatherWood order should succeed: %s" % str(repath))
	if repath.get("ok", false):
		_check(
			_same_keys(repath["payload"], _client_example("repath-response.json")["payload"]),
			"RepathResponse payload must match repath-response.json."
		)
		var waypoints: Array = repath["payload"]["waypoints"]
		_check(not waypoints.is_empty(), "The worker is out of range, so the repath needs waypoints.")
		if not waypoints.is_empty():
			var last := Vector2(float(waypoints[-1]["x"]), float(waypoints[-1]["z"]))
			_check(last.distance_to(Vector2(12.5, 8.5)) <= GATHER_RANGE, "The repath must end within gather range.")
	var stale_target: Dictionary = await world.build_repath_response_payload(
		{"unitId": 3, "orderId": 5, "mapRevision": 0, "targetId": 8}
	)
	_check(stale_target.get("error", "") == "StaleTarget", "A repath for another target must be rejected.")
	var stale_order: Dictionary = await world.build_repath_response_payload(
		{"unitId": 3, "orderId": 4, "mapRevision": 0, "targetId": 7}
	)
	_check(stale_order.get("error", "") == "StaleOrder", "A repath for an old OrderId must be rejected.")

	var soldiers_only: Dictionary = await world.issue_gather_wood_command(7, _ids([SOLDIER_A]))
	_check(soldiers_only.get("error", "") == "NoCompatibleUnits", "Only workers can gather wood.")
	_free_setup(setup)


# --- Attack --------------------------------------------------------------------------

func _test_attack_command() -> void:
	var setup := _create_running_world()
	var world: Node = setup["world"]
	var network: FakeNetworkClient = setup["network"]
	var building_attack: Dictionary = await world.issue_attack_command(2, _ids([3, SOLDIER_A]))
	_check(building_attack.get("ok", false), "Attack on a rival building should be sent: %s" % str(building_attack))
	if building_attack.get("ok", false):
		var sent: Dictionary = network.sent_messages[-1]
		_check(sent["type"] == "Attack", "Attack should use its protocol type.")
		_check(
			_same_keys(sent["payload"], _client_example("attack.json")["payload"]),
			"Attack payload must match attack.json."
		)
		_check(_route_unit_ids(sent["payload"]) == [SOLDIER_A], "Workers must not be sent to attack.")
		_check_route_ends_in_range(
			world,
			sent["payload"],
			SOLDIER_A,
			{"rect": Rect2(34.0, 34.0, 4.0, 4.0)},
			ATTACK_RANGE,
			"Attack on a building"
		)
	var unit_attack: Dictionary = await world.issue_attack_command(5, _ids([SOLDIER_B]))
	_check(unit_attack.get("ok", false), "Attack on a rival unit should be sent: %s" % str(unit_attack))
	if unit_attack.get("ok", false):
		_check_route_ends_in_range(
			world,
			network.sent_messages[-1]["payload"],
			SOLDIER_B,
			{"point": Vector2(32.5, 36.5)},
			ATTACK_RANGE,
			"Attack on a unit"
		)
	var friendly: Dictionary = await world.issue_attack_command(1, _ids([SOLDIER_B]))
	_check(friendly.get("error", "") == "FriendlyTarget", "Own entities cannot be attacked.")
	var tree: Dictionary = await world.issue_attack_command(7, _ids([SOLDIER_B]))
	_check(tree.get("error", "") == "InvalidTarget", "Trees cannot be attacked.")
	_free_setup(setup)


# --- Build (continuar) y RepairFarm --------------------------------------------------

func _test_build_continue_and_repair_farm() -> void:
	var setup := _create_running_world()
	var world: Node = setup["world"]
	var network: FakeNetworkClient = setup["network"]
	var build: Dictionary = await world.issue_build_continue_command(UNFINISHED_FARM, _ids([4, SOLDIER_A]))
	_check(build.get("ok", false), "Build continue on an own construction should be sent: %s" % str(build))
	if build.get("ok", false):
		var sent: Dictionary = network.sent_messages[-1]
		_check(sent["type"] == "Build", "Build continue uses the Build type (D-032).")
		_check(
			_same_keys(sent["payload"], _client_example("build-continue.json")["payload"]),
			"Build continue payload must match build-continue.json."
		)
		_check(_route_unit_ids(sent["payload"]) == [4], "Only workers can build.")
		_check_route_ends_in_range(
			world,
			sent["payload"],
			4,
			{"rect": Rect2(8.0, 10.0, 2.0, 2.0)},
			BUILD_RANGE,
			"Build continue"
		)
	var completed: Dictionary = await world.issue_build_continue_command(1, _ids([3]))
	_check(completed.get("error", "") == "AlreadyCompleted", "Completed buildings cannot be continued.")

	var repair: Dictionary = await world.issue_repair_farm_command(DEPLETED_FARM, _ids([3]))
	_check(repair.get("ok", false), "RepairFarm on an own depleted farm should be sent: %s" % str(repair))
	if repair.get("ok", false):
		var sent: Dictionary = network.sent_messages[-1]
		_check(sent["type"] == "RepairFarm", "RepairFarm should use its protocol type.")
		_check(
			_same_keys(sent["payload"], _client_example("repair-farm.json")["payload"]),
			"RepairFarm payload must match repair-farm.json."
		)
		_check_route_ends_in_range(
			world,
			sent["payload"],
			3,
			{"rect": Rect2(14.0, 14.0, 2.0, 2.0)},
			BUILD_RANGE,
			"RepairFarm"
		)
	var producing: Dictionary = await world.issue_repair_farm_command(PRODUCING_FARM, _ids([SOLDIER_A]))
	_check(producing.get("error", "") == "FarmNotDepleted", "Only depleted farms can be repaired.")
	var not_farm: Dictionary = await world.issue_repair_farm_command(1, _ids([SOLDIER_A]))
	_check(not_farm.get("error", "") == "NotAFarm", "RepairFarm requires a farm.")
	_free_setup(setup)


# --- CommandRejected → reintento con el mismo messageId (D-075) ----------------------

func _test_rejected_task_command_retry() -> void:
	var setup := _create_running_world()
	var world: Node = setup["world"]
	var network: FakeNetworkClient = setup["network"]
	var result: Dictionary = await world.issue_gather_wood_command(7, _ids([4]))
	_check(result.get("ok", false), "GatherWood should be sent before testing its retry.")
	if not result.get("ok", false):
		_free_setup(setup)
		return
	var message_id := int(result["messageId"])
	world._handle_command_rejected({"messageId": message_id, "error": "Network.RateLimited"})
	_check(world.pending_commands[message_id]["status"] == "rejected", "CommandRejected marks the command retryable.")
	var retried: Dictionary = world.retry_rejected_command(message_id)
	_check(retried.get("ok", false), "A rejected GatherWood should be retryable.")
	var resent: Dictionary = network.sent_messages[-1]
	_check(
		resent["type"] == "GatherWood"
			and resent["messageId"] == message_id
			and resent["payload"] == network.sent_messages[-2]["payload"],
		"A retry resends the same type, messageId and payload."
	)
	_free_setup(setup)


# --- Clic derecho contextual ---------------------------------------------------------

func _test_contextual_command_resolution() -> void:
	var setup := _create_running_world()
	var world: Node = setup["world"]
	var controller: Node = UnitCommandControllerScript.new()
	world.add_child(controller)
	controller.set_selected_units(_ids([3, 4, SOLDIER_A, SOLDIER_B]))
	var gather: Dictionary = controller.resolve_contextual_command(7)
	_check(gather.get("type", "") == "GatherWood" and gather["unitIds"] == [3, 4], "Tree → GatherWood with workers.")
	var attack: Dictionary = controller.resolve_contextual_command(2)
	_check(
		attack.get("type", "") == "Attack" and attack["unitIds"] == [SOLDIER_A, SOLDIER_B],
		"Rival building → Attack with soldiers."
	)
	_check(controller.resolve_contextual_command(5).get("type", "") == "Attack", "Rival unit → Attack.")
	_check(controller.resolve_contextual_command(UNFINISHED_FARM).get("type", "") == "Build", "Own construction → Build.")
	_check(controller.resolve_contextual_command(DEPLETED_FARM).get("type", "") == "RepairFarm", "Depleted farm → RepairFarm.")
	_check(controller.resolve_contextual_command(PRODUCING_FARM).get("type", "") == "Move", "Producing farm → Move.")
	_check(controller.resolve_contextual_command(1).get("type", "") == "Move", "Own completed building → Move.")
	_check(controller.resolve_contextual_command(0).get("type", "") == "Move", "Ground → Move.")
	controller.set_selected_units(_ids([SOLDIER_A]))
	_check(controller.resolve_contextual_command(7).is_empty(), "Soldiers ignore a click on a tree.")
	controller.set_selected_units(_ids([3]))
	_check(controller.resolve_contextual_command(2).is_empty(), "Workers ignore a click on an enemy.")
	_free_setup(setup)


# --- Utilidades ----------------------------------------------------------------------

func _create_running_world() -> Dictionary:
	var world: Node = InitialWorldScript.new()
	root.add_child(world)
	var network := FakeNetworkClient.new()
	var session := FakeSessionController.new()
	root.add_child(network)
	root.add_child(session)
	world._network_client = network
	world._session_controller = session
	_check(world.prepare_initial_state(1, _world_state()), "The gameplay command world should build.")
	return {"world": world, "network": network, "session": session}


func _ids(values: Array) -> Array[int]:
	var result: Array[int] = []
	for value: Variant in values:
		result.append(int(value))
	return result


func _free_setup(setup: Dictionary) -> void:
	_completed_tests += 1
	for key: String in ["world", "network", "session"]:
		var node: Node = setup[key]
		if is_instance_valid(node):
			node.queue_free()


## Mundo de docs/protocol/examples/server-to-client/02-initial-state.json (visto por P1),
## más dos soldados propios y tres granjas propias: en obra, agotada y produciendo.
func _world_state() -> Dictionary:
	var state: Dictionary = _example("server-to-client", "02-initial-state.json").get("payload", {})
	state = _with_integer_numbers(state)
	state["units"].append_array([
		{"unitId": SOLDIER_A, "owner": 1, "unitType": "Soldier", "x": 9.5, "z": 14.5, "hp": 80, "order": "Idle"},
		{"unitId": SOLDIER_B, "owner": 1, "unitType": "Soldier", "x": 20.5, "z": 30.5, "hp": 80, "order": "Idle"},
	])
	state["buildings"].append_array([
		{
			"buildingId": UNFINISHED_FARM, "owner": 1, "buildingType": "Farm",
			"footprint": {"column": 8, "row": 10, "width": 2, "height": 2},
			"hp": 20, "isCompleted": false,
		},
		{
			"buildingId": DEPLETED_FARM, "owner": 1, "buildingType": "Farm",
			"footprint": {"column": 14, "row": 14, "width": 2, "height": 2},
			"hp": 200, "isCompleted": true, "farm": {"isDepleted": true, "isRepairing": false},
		},
		{
			"buildingId": PRODUCING_FARM, "owner": 1, "buildingType": "Farm",
			"footprint": {"column": 18, "row": 14, "width": 2, "height": 2},
			"hp": 200, "isCompleted": true, "farm": {"isDepleted": false, "isRepairing": false},
		},
	])
	return state


## JSON.parse_string devuelve todos los números como float; los IDs del protocolo son enteros.
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


func _client_example(filename: String) -> Dictionary:
	return _example("client-to-server", filename)


func _example(direction: String, filename: String) -> Dictionary:
	var path := ProjectSettings.globalize_path("res://../docs/protocol/examples").path_join(direction).path_join(filename)
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		_check(false, "Could not open protocol example %s/%s." % [direction, filename])
		return {}
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	if typeof(parsed) != TYPE_DICTIONARY:
		_check(false, "Protocol example %s must contain a JSON object." % filename)
		return {}
	return parsed


## Mismo conjunto de campos que el ejemplo, en el payload y en cada ruta.
func _same_keys(actual: Dictionary, expected: Dictionary) -> bool:
	var actual_keys := actual.keys()
	var expected_keys := expected.keys()
	actual_keys.sort()
	expected_keys.sort()
	if actual_keys != expected_keys:
		push_error("Payload keys %s differ from example keys %s." % [str(actual_keys), str(expected_keys)])
		return false
	if expected.has("routes"):
		for route: Dictionary in actual["routes"]:
			var route_keys := route.keys()
			route_keys.sort()
			if route_keys != ["unitId", "waypoints"]:
				return false
			for waypoint: Dictionary in route["waypoints"]:
				var waypoint_keys := waypoint.keys()
				waypoint_keys.sort()
				if waypoint_keys != ["x", "z"]:
					return false
	return true


func _route_unit_ids(payload: Dictionary) -> Array:
	var result: Array = []
	for route: Dictionary in payload.get("routes", []):
		result.append(int(route["unitId"]))
	return result


func _check_route_ends_in_range(
	world: Node,
	payload: Dictionary,
	unit_id: int,
	target: Dictionary,
	interaction_range: float,
	label: String
) -> void:
	_check(int(payload.get("routeMapRevision", -1)) == 0, "%s should declare the map revision it used." % label)
	for route: Dictionary in payload.get("routes", []):
		if int(route["unitId"]) != unit_id:
			continue
		var unit: Node3D = world.entities_by_id[unit_id]
		var end: Vector2 = unit.get_meta("official_position")
		var waypoints: Array = route["waypoints"]
		if not waypoints.is_empty():
			end = Vector2(float(waypoints[-1]["x"]), float(waypoints[-1]["z"]))
		_check(
			ClientNavigationScript.distance_to_target(end, target) <= interaction_range,
			"%s route must end within the interaction range (end %s)." % [label, str(end)]
		)
		return
	_check(false, "%s should include a route for unit %d." % [label, unit_id])


func _accepted_result(client_command_id: int, unit_ids: Array) -> Dictionary:
	var entity_results: Array = []
	for unit_id: Variant in unit_ids:
		entity_results.append({"entityId": int(unit_id)})
	return {
		"clientCommandId": client_command_id,
		"sequence": client_command_id,
		"tick": 1,
		"outcome": "Accepted",
		"entityResults": entity_results,
	}


func _state_events(tick: int, revision: int, events: Array, command_results: Array = []) -> Dictionary:
	return {
		"tick": tick,
		"mapRevision": revision,
		"events": events,
		"commandResults": command_results,
	}
