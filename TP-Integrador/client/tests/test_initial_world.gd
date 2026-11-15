extends SceneTree

const InitialWorldScript = preload("res://scripts/initial_world.gd")

class MissingSecondBuildingVisual:
	extends InitialWorldScript

	var fail_red_building := false

	func _load_visual_scene(path: String) -> PackedScene:
		if fail_red_building and path.ends_with("building_castle_red.gltf"):
			return null
		return super._load_visual_scene(path)


var _failed := false


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_initial_state_build_and_discard()
	_test_invalid_state_preserves_active_world()
	_test_partial_visual_failure_preserves_active_world()
	_test_state_event_replay()
	_test_protocol_example_replay()
	_test_position_interpolation_and_stale_updates()
	_test_invalid_state_event_stops_synchronization()
	if _failed:
		quit(1)
		return
	print("Initial world tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _create_world(script = InitialWorldScript) -> Node:
	var world: Node = script.new()
	root.add_child(world)
	return world


func _test_initial_state_build_and_discard() -> void:
	var world = _create_world()
	var state := _initial_state()
	_check(
		world.prepare_initial_state(1, state, "session-1", 2),
		"A valid snapshot should build a world."
	)
	_check(world.active_initialization_id == 1, "The active initialization ID must be retained.")
	_check(world.active_snapshot["playerId"] == 2, "The recipient player ID must be retained.")
	_check(world.active_catalog["ticksPerSecond"] == 20, "The catalog must be retained.")
	_check(world.entities_by_id.size() == 4, "Every initial entity must be indexed by ID.")

	var ground := world.active_world.get_node("Terrain/Ground") as MeshInstance3D
	_check(ground != null, "The active world must contain a terrain mesh.")
	if ground != null:
		var plane := ground.mesh as PlaneMesh
		_check(plane != null, "The terrain must use a plane mesh.")
		if plane != null:
			_check(plane.size == Vector2(40.0, 40.0), "The terrain must use the snapshot dimensions.")
		_check(
			ground.position == Vector3(20.0, -0.05, 20.0),
			"The terrain must start at the logical map origin."
		)

	var main_center: Node3D = world.entities_by_id[1]
	_check(
		main_center.position == Vector3(4.0, 0.0, 4.0),
		"A 4x4 building anchored at (2, 2) must be centered at (4, 4)."
	)
	var unit: Node3D = world.entities_by_id[3]
	_check(unit.position == Vector3(7.5, 0.0, 3.5), "Unit X/Z must match the server snapshot.")
	var tree: Node3D = world.entities_by_id[5]
	_check(tree.position == Vector3(12.5, 0.0, 8.5), "Tree X/Z must match the server snapshot.")
	_check(
		tree.get_meta("entity_state")["isAvailable"],
		"Tree availability must be retained on the entity representation."
	)

	world.discard_initial_state(2)
	_check(world.active_world != null, "Discarding another initialization must not remove the world.")
	world.discard_initial_state(1)
	_check(world.active_world == null, "Discarding the active initialization must remove its world.")
	_check(world.entities_by_id.is_empty(), "Discarding must clear the entity index.")
	world.queue_free()


func _test_invalid_state_preserves_active_world() -> void:
	var world = _create_world()
	_check(world.prepare_initial_state(1, _initial_state()), "The baseline world should build.")
	var active_world: Node3D = world.active_world

	var duplicated_id := _initial_state()
	duplicated_id["initializationId"] = 2
	duplicated_id["trees"][0]["treeId"] = 1
	_check(
		not world.prepare_initial_state(2, duplicated_id),
		"Duplicate global entity IDs must be rejected."
	)
	_check(world.active_world == active_world, "Invalid data must not replace the active world.")

	var out_of_bounds := _initial_state()
	out_of_bounds["initializationId"] = 2
	out_of_bounds["units"][0]["x"] = 40.0
	_check(
		not world.prepare_initial_state(2, out_of_bounds),
		"Positions at the exclusive map boundary must be rejected."
	)
	_check(world.active_world == active_world, "An invalid position must not replace the active world.")

	var unknown_type := _initial_state()
	unknown_type["initializationId"] = 2
	unknown_type["units"][0]["unitType"] = "Archer"
	_check(
		not world.prepare_initial_state(2, unknown_type),
		"An entity type without a visual mapping must be rejected."
	)
	_check(world.active_world == active_world, "An unknown type must not replace the active world.")

	var mismatched_identity := _initial_state()
	mismatched_identity["initializationId"] = 2
	mismatched_identity["sessionId"] = "other-session"
	_check(
		not world.prepare_initial_state(2, mismatched_identity, "session-1", 2),
		"A snapshot for another session must be rejected."
	)
	_check(world.active_world == active_world, "An identity mismatch must not replace the active world.")

	var mismatched_player := _initial_state()
	mismatched_player["initializationId"] = 2
	mismatched_player["playerId"] = 1
	_check(
		not world.prepare_initial_state(2, mismatched_player, "session-1", 2),
		"A snapshot for another player must be rejected."
	)
	_check(world.active_world == active_world, "A player mismatch must not replace the active world.")
	world.queue_free()


func _test_partial_visual_failure_preserves_active_world() -> void:
	var failing_world = _create_world(MissingSecondBuildingVisual)
	_check(
		failing_world.prepare_initial_state(1, _initial_state()),
		"The baseline world should build before simulating a visual failure."
	)
	var active_world: Node3D = failing_world.active_world
	failing_world.fail_red_building = true
	var next_state := _initial_state()
	next_state["initializationId"] = 2
	_check(
		not failing_world.prepare_initial_state(2, next_state),
		"A missing visual during instantiation must fail preparation."
	)
	_check(
		failing_world.active_world == active_world,
		"A failure must preserve the previously active world."
	)
	failing_world.queue_free()


func _test_state_event_replay() -> void:
	var world = _create_world()
	var state := _initial_state()
	state["playerId"] = 1
	state["ownProduction"] = [{"producerId": 1, "status": "Idle", "pending": []}]
	_check(world.prepare_initial_state(1, state), "The replay baseline should build.")

	var revision_notifications: Array[int] = []
	var repath_requests: Array[Dictionary] = []
	var rejected_commands: Array[Dictionary] = []
	world.map_revision_changed.connect(func(revision: int) -> void: revision_notifications.append(revision))
	world.repath_requested.connect(func(request: Dictionary) -> void: repath_requests.append(request))
	world.command_rejected_received.connect(
		func(message_id: int, error_code: String) -> void:
			rejected_commands.append({"messageId": message_id, "error": error_code})
	)

	var command_result := {
		"clientCommandId": 5,
		"sequence": 5,
		"tick": 1,
		"outcome": "PartiallyAccepted",
		"entityResults": [
			{"entityId": 3},
			{"entityId": 5, "error": "Common.NotOwner"},
		],
	}
	_check(
		world.apply_state_events(
			_state_events(
				1,
				0,
				[
					{"type": "UnitOrderChanged", "unitId": 3, "orderId": 1, "kind": "Move", "index": 2},
					{"type": "ResourcesChanged", "wood": 140, "food": 100, "index": 8},
				],
				[command_result]
			)
		),
		"Ordered events with index gaps should apply."
	)
	var unit: Node3D = world.entities_by_id[3]
	var unit_state: Dictionary = unit.get_meta("entity_state")
	_check(unit_state["order"] == "Move", "UnitOrderChanged should replace the displayed order.")
	_check(world.active_snapshot["ownResources"] == {"wood": 140, "food": 100}, "ResourcesChanged should replace the local balance.")
	_check(
		world.recent_command_results[5]["entityResults"][1]["error"] == "Common.NotOwner",
		"Command results must retain partial entity failures."
	)
	world._handle_command_rejected({"messageId": 8, "error": "Economy.InsufficientWood"})
	_check(
		rejected_commands == [{"messageId": 8, "error": "Economy.InsufficientWood"}],
		"CommandRejected must be exposed separately from definitive command results."
	)

	_check(
		world.apply_state_events(
			_state_events(
				6,
				1,
				[
					{
						"type": "BuildingPlaced",
						"buildingId": 11,
						"owner": 1,
						"buildingType": "Farm",
						"footprint": {"column": 8, "row": 10, "width": 2, "height": 2},
						"hp": 20,
						"index": 0,
					},
					{"type": "MapRevisionChanged", "revision": 1, "index": 1},
				]
			)
		),
		"BuildingPlaced should apply before its map revision."
	)
	_check(world.entities_by_id.has(11), "BuildingPlaced must add the new building to the global ID index.")
	_check(world.map_revision == 1 and world.navigation_required_revision == 1, "MapRevisionChanged must update both revision cursors.")
	_check(revision_notifications == [1], "MapRevisionChanged must notify navigation.")

	_check(
		world.apply_state_events(
			_state_events(
				7,
				1,
				[
					{"type": "BuildingProgressChanged", "buildingId": 11, "hp": 200, "index": 0},
					{"type": "BuildingCompleted", "buildingId": 11, "index": 1},
				]
			)
		),
		"Building progress and completion should both be applied."
	)
	var building_state: Dictionary = (world.entities_by_id[11] as Node3D).get_meta("entity_state")
	_check(building_state["hp"] == 200 and building_state["isCompleted"], "Building events must preserve authoritative HP and completion.")

	_check(
		world.apply_state_events(
			_state_events(
				8,
				1,
				[
					{"type": "FarmDepleted", "farmId": 11, "index": 0},
					{"type": "FarmRepairStarted", "farmId": 11, "index": 1},
				]
			)
		),
		"Farm state events should apply to the referenced Farm."
	)
	_check(
		world.apply_state_events(
			_state_events(
				9,
				1,
				[{"type": "FarmRepairProgressChanged", "farmId": 11, "workDone": 50, "workRequired": 100, "index": 0}]
			)
		),
		"Farm repair progress should update."
	)
	_check(
		world.apply_state_events(
			_state_events(
				10,
				1,
				[
					{"type": "FarmRepairProgressChanged", "farmId": 11, "workDone": 100, "workRequired": 100, "index": 0},
					{"type": "FarmRepairCompleted", "farmId": 11, "index": 1},
				]
			)
		),
		"Repair progress and completion should retain their event order."
	)
	var farm_state: Dictionary = (world.entities_by_id[11] as Node3D).get_meta("entity_state")["farm"]
	_check(not farm_state["isDepleted"] and not farm_state["isRepairing"], "A repaired farm should be restored.")
	_check(farm_state["repairWorkDone"] == 100, "The repair work totals should be retained.")

	var tree: Node3D = world.entities_by_id[5]
	_check(world.apply_state_events(_state_events(11, 1, [{"type": "TreeDepleted", "treeId": 5, "index": 0}])), "TreeDepleted should apply.")
	_check(not tree.visible and world.entities_by_id.has(5), "TreeDepleted should hide but preserve the tree identity.")
	_check(world.apply_state_events(_state_events(12, 1, [{"type": "TreeRespawned", "treeId": 5, "index": 0}])), "TreeRespawned should apply.")
	_check(tree.visible and world.entities_by_id[5] == tree, "TreeRespawned should reactivate the same logical tree.")

	_check(
		world.apply_state_events(
			_state_events(
				13,
				1,
				[
					{
						"type": "ProductionChanged",
						"producerId": 1,
						"status": "Training",
						"activeUnitType": "Worker",
						"remainingTicks": 200,
						"pending": [],
						"index": 0,
					},
				]
			)
		),
		"ProductionChanged should update the local producer."
	)
	_check(
		world.active_snapshot["ownProduction"][0]["remainingTicks"] == 200,
		"ProductionChanged should update the owner's production view."
	)
	_check(
		world.apply_state_events(
			_state_events(
				14,
				1,
				[
					{
						"type": "RepathRequested",
						"unitId": 3,
						"orderId": 1,
						"mapRevision": 1,
						"targetId": 7,
						"isPaused": true,
						"index": 0,
					},
				]
			)
		),
		"RepathRequested should be routed without simulating movement."
	)
	_check(repath_requests.size() == 1 and repath_requests[0]["targetId"] == 7, "RepathRequested should reach the movement consumer.")

	_check(
		world.apply_state_events(
			_state_events(
				15,
				1,
				[
					{
						"type": "UnitSpawned",
						"unitId": 12,
						"owner": 1,
						"unitType": "Worker",
						"x": 6.5,
						"z": 2.5,
						"hp": 40,
						"index": 0,
					},
				]
			)
		),
		"UnitSpawned should create and index the unit."
	)
	_check(world.entities_by_id.has(12), "UnitSpawned must add its ID before PositionUpdate.")
	_check(
		world.apply_position_update(
			{
				"tick": 15,
				"units": [
					{"id": 12, "x": 6.5, "z": 2.5},
					{"id": 999, "x": 1.0, "z": 1.0},
				],
			},
			1_500_000
		),
		"PositionUpdate should find a unit created by the preceding StateEvents tick."
	)
	_check(not world.entities_by_id.has(999), "PositionUpdate must never create unknown entities.")
	_check(
		world.apply_state_events(_state_events(16, 1, [{"type": "EntityDamaged", "entityId": 3, "hp": 32, "index": 0}])),
		"EntityDamaged should apply."
	)
	_check(
		(world.entities_by_id[3] as Node3D).get_meta("entity_state")["hp"] == 32,
		"EntityDamaged must replace HP with the server value."
	)

	_check(
		world.apply_state_events(
			_state_events(
				17,
				2,
				[
					{"type": "EntityDestroyed", "entityId": 3, "index": 0},
					{"type": "EntityDestroyed", "entityId": 11, "index": 1},
					{"type": "MapRevisionChanged", "revision": 2, "index": 2},
				]
			)
		),
		"Destruction and map revision events should apply in order."
	)
	_check(not world.entities_by_id.has(3) and not world.entities_by_id.has(11), "EntityDestroyed should remove both entities from the ID index.")
	_check(world.destroyed_entity_ids.has(3), "Destroyed unit IDs must be retained to reject later positions.")
	_check(revision_notifications == [1, 2], "Each structural revision should notify navigation once.")
	world.queue_free()


func _test_protocol_example_replay() -> void:
	var initial_message := _load_protocol_example("02-initial-state.json")
	_check(not initial_message.is_empty(), "The generated InitialState example should be readable.")
	if initial_message.is_empty():
		return
	var world = _create_world()
	var initial_payload: Dictionary = initial_message["payload"]
	_check(
		world.prepare_initial_state(int(initial_payload["initializationId"]), initial_payload),
		"The generated InitialState example should build."
	)

	for filename: String in [
		"05-state-events-orders-accepted.json",
		"06-position-update.json",
		"07-state-events-building-placed.json",
		"08-state-events-repath-requested.json",
		"09-state-events-production-started.json",
		"10-state-events-command-results.json",
		"11-state-events-building-completed.json",
		"12-state-events-unit-spawned.json",
		"13-state-events-tree-depleted.json",
		"14-state-events-farm-depleted.json",
		"15-state-events-farm-repair-started.json",
		"16-state-events-farm-repair-completed.json",
		"17-state-events-tree-respawned.json",
		"18-state-events-entity-damaged.json",
		"19-state-events-entity-destroyed.json",
	]:
		var message := _load_protocol_example(filename)
		_check(not message.is_empty(), "The generated example %s should be readable." % filename)
		if message.is_empty():
			continue
		var succeeded := false
		match message["type"]:
			"StateEvents":
				succeeded = world.apply_state_events(message["payload"])
			"PositionUpdate":
				succeeded = world.apply_position_update(message["payload"])
			_:
				_check(false, "Unexpected message type in example %s." % filename)
		_check(succeeded, "The generated example %s should apply in sequence." % filename)

	_check(world.map_revision == 2, "The generated replay should end at map revision 2.")
	_check(not world.entities_by_id.has(3) and not world.entities_by_id.has(11), "The generated replay should remove destroyed entities.")
	_check(world.entities_by_id.has(12), "The generated replay should preserve the spawned unit.")
	_check((world.entities_by_id[7] as Node3D).visible, "The generated replay should respawn the original tree instance.")
	_check(world.active_snapshot["ownResources"] == {"wood": 210, "food": 85}, "The generated replay should retain the final private balance.")
	world.queue_free()


func _load_protocol_example(filename: String) -> Dictionary:
	var examples_path := ProjectSettings.globalize_path(
		"res://../docs/protocol/examples/server-to-client"
	)
	var file := FileAccess.open(examples_path.path_join(filename), FileAccess.READ)
	if file == null:
		_check(false, "Could not open generated protocol example %s." % filename)
		return {}
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	if typeof(parsed) != TYPE_DICTIONARY:
		_check(false, "Generated protocol example %s must contain a JSON object." % filename)
		return {}
	return parsed


func _test_position_interpolation_and_stale_updates() -> void:
	var world = _create_world()
	_check(world.prepare_initial_state(1, _initial_state()), "The interpolation baseline should build.")
	var unit: Node3D = world.entities_by_id[3]
	unit.position.y = 1.25
	_check(
		world.apply_position_update(
			{"tick": 2, "units": [{"id": 3, "x": 8.0, "z": 4.0}, {"id": 999, "x": 1.0, "z": 1.0}]},
			1_000_000
		),
		"A valid position update should add samples and ignore unknown IDs."
	)
	world._update_unit_interpolation(1_050_000)
	_check(
		unit.position.is_equal_approx(Vector3(7.75, 1.25, 3.75)),
		"Positions should interpolate between received samples while preserving visual height."
	)
	world._update_unit_interpolation(2_000_000)
	_check(
		unit.position.is_equal_approx(Vector3(8.0, 1.25, 4.0)),
		"Interpolation must stop at the newest official sample."
	)
	_check(
		not world.apply_position_update({"tick": 2, "units": [{"id": 3, "x": 99.0, "z": 99.0}]}, 2_100_000),
		"A repeated PositionUpdate tick should be discarded."
	)
	_check(
		unit.position.is_equal_approx(Vector3(8.0, 1.25, 4.0)),
		"A stale PositionUpdate must not change the unit transform."
	)

	_check(
		world.apply_position_update({"tick": 4, "units": [{"id": 3, "x": 8.4, "z": 4.4}]}, 2_100_000),
		"A later PositionUpdate should be accepted."
	)
	world._update_unit_interpolation(2_150_000)
	_check(
		unit.position.is_equal_approx(Vector3(8.2, 1.25, 4.2)),
		"Interpolation should use the ticks of the latest consecutive samples."
	)

	_check(
		world.apply_state_events(
			_state_events(5, 0, [{"type": "EntityDestroyed", "entityId": 3, "index": 0}])
		),
		"Destroying a unit should remove its interpolation samples."
	)
	_check(
		world.apply_position_update({"tick": 6, "units": [{"id": 3, "x": 9.0, "z": 9.0}]}, 2_200_000),
		"A position update with a destroyed ID should be accepted but ignored."
	)
	_check(not world.entities_by_id.has(3), "A position update must never revive a destroyed unit.")
	world.queue_free()


func _test_invalid_state_event_stops_synchronization() -> void:
	var world = _create_world()
	_check(world.prepare_initial_state(1, _initial_state()), "The invalid-event baseline should build.")
	var invalid_batch := _state_events(
		1,
		0,
		[
			{"type": "UnitOrderChanged", "unitId": 3, "orderId": 1, "kind": "Move", "index": 3},
			{"type": "UnknownEvent", "index": 4},
		]
	)
	_check(not world.apply_state_events(invalid_batch), "Unknown event types must fail the batch.")
	_check(world.synchronization_failed, "A broken event stream must be exposed as unsynchronized.")
	_check(world.last_state_events_tick == 0, "A failed batch must not advance the applied tick.")
	_check(
		(world.entities_by_id[3] as Node3D).get_meta("entity_state")["order"] == "Idle",
		"Prevalidation must prevent partial application of an invalid batch."
	)
	_check(
		not world.apply_position_update({"tick": 2, "units": []}),
		"Position updates must stop after synchronization has failed."
	)
	world.queue_free()

	var out_of_order_world = _create_world()
	_check(out_of_order_world.prepare_initial_state(1, _initial_state()), "The order-check baseline should build.")
	_check(
		out_of_order_world.apply_position_update({"tick": 2, "units": []}),
		"A PositionUpdate with no units should still advance its tick cursor."
	)
	_check(
		not out_of_order_world.apply_state_events(_state_events(2, 0, [])),
		"StateEvents may not arrive after the PositionUpdate for the same tick."
	)
	_check(
		out_of_order_world.synchronization_failed,
		"An event/position ordering violation must be surfaced as a synchronization failure."
	)
	out_of_order_world.queue_free()

	var unordered_events_world = _create_world()
	_check(unordered_events_world.prepare_initial_state(1, _initial_state()), "The event-order baseline should build.")
	_check(
		not unordered_events_world.apply_state_events(
			_state_events(
				1,
				0,
				[
					{"type": "UnitOrderChanged", "unitId": 3, "orderId": 1, "kind": "Move", "index": 3},
					{"type": "TreeDepleted", "treeId": 5, "index": 2},
				]
			)
		),
		"Events with decreasing indices must be rejected."
	)
	_check(
		(unordered_events_world.entities_by_id[3] as Node3D).get_meta("entity_state")["order"] == "Idle",
		"An event-order failure must not partially apply the batch."
	)
	unordered_events_world.queue_free()


func _state_events(
	tick: int,
	map_revision: int,
	events: Array,
	command_results: Array = []
) -> Dictionary:
	return {
		"tick": tick,
		"mapRevision": map_revision,
		"events": events,
		"commandResults": command_results,
	}


func _initial_state() -> Dictionary:
	return {
		"sessionId": "session-1",
		"initializationId": 1,
		"playerId": 2,
		"tick": 0,
		"map": {"cellSize": 1.0, "columns": 40, "rows": 40, "revision": 0},
		"players": [
			{"playerId": 1, "mainCenterId": 1},
			{"playerId": 2, "mainCenterId": 2},
		],
		"units": [
			{
				"unitId": 3,
				"owner": 1,
				"unitType": "Worker",
				"x": 7.5,
				"z": 3.5,
				"hp": 40,
				"order": "Idle",
			},
		],
		"buildings": [
			{
				"buildingId": 1,
				"owner": 1,
				"buildingType": "MainCenter",
				"footprint": {"column": 2, "row": 2, "width": 4, "height": 4},
				"hp": 1000,
				"isCompleted": true,
			},
			{
				"buildingId": 2,
				"owner": 2,
				"buildingType": "MainCenter",
				"footprint": {"column": 34, "row": 34, "width": 4, "height": 4},
				"hp": 1000,
				"isCompleted": true,
			},
		],
		"trees": [
			{
				"treeId": 5,
				"column": 12,
				"row": 8,
				"x": 12.5,
				"z": 8.5,
				"isAvailable": false,
			},
		],
		"ownResources": {"wood": 200, "food": 100},
		"ownProduction": [
			{"producerId": 2, "status": "Idle", "pending": []},
		],
		"catalog": {
			"ticksPerSecond": 20,
			"units": [
				{
					"unitType": "Worker",
					"maxHp": 40,
					"cost": {"wood": 0, "food": 50},
					"trainingDurationTicks": 200,
					"speedPerSecond": 3.0,
				},
				{
					"unitType": "Soldier",
					"maxHp": 80,
					"cost": {"wood": 0, "food": 80},
					"trainingDurationTicks": 300,
					"speedPerSecond": 3.5,
				},
			],
			"buildings": [
				{
					"buildingType": "MainCenter",
					"maxHp": 1000,
					"footprintWidth": 4,
					"footprintHeight": 4,
				},
				{
					"buildingType": "Farm",
					"maxHp": 200,
					"footprintWidth": 2,
					"footprintHeight": 2,
				},
				{
					"buildingType": "Barracks",
					"maxHp": 400,
					"footprintWidth": 3,
					"footprintHeight": 3,
				},
				{
					"buildingType": "Tower",
					"maxHp": 300,
					"footprintWidth": 2,
					"footprintHeight": 2,
				},
				{
					"buildingType": "Fence",
					"maxHp": 100,
					"footprintWidth": 1,
					"footprintHeight": 1,
				},
			],
			"farmRepairCost": {"wood": 30, "food": 0},
			"farmRepairWorkRequired": 100,
			"gatherRange": 1.5,
			"buildRange": 1.5,
		},
	}
