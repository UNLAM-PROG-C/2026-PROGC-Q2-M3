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
