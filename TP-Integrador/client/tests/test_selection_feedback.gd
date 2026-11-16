extends SceneTree

## Bloque 4: selección por clic con ciclo de superpuestas y por rectángulo (04 §3),
## marcadores de orden, barras de estado de entidades y animaciones de unidades.

const InitialWorldScript = preload("res://scripts/initial_world.gd")
const UnitCommandControllerScript = preload("res://scripts/unit_command_controller.gd")
const UnitAnimationControllerScript = preload("res://scripts/unit_animation_controller.gd")
const EntityStatusOverlayScript = preload("res://scripts/ui/entity_status_overlay.gd")

const EXPECTED_TESTS := 12

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
	_test_own_and_rival_unit_models()
	_test_fence_team_banners()
	_test_farm_scene_models()
	_test_building_scenes()
	await _test_building_ruins()
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
	var finished: Array = []
	controller.box_selection_finished.connect(func(ids: Array) -> void: finished.append(ids))
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
	_check(finished == [[3, 4]], "Finishing a box notifies it once, with the selected units.")
	_check(box != null and not box.visible, "The selection box hides on release.")
	_push_mouse_button(MOUSE_BUTTON_LEFT, true, _screen_position(setup, 4))
	_push_mouse_button(MOUSE_BUTTON_LEFT, false, _screen_position(setup, 4))
	_check(controller.get_selected_units() == [4], "A click without dragging selects a single unit on release.")
	_check(finished.size() == 1, "A simple click is not a finished selection box.")
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


func _test_own_and_rival_unit_models() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	var models := {
		3: "character-worker.glb",
		5: "character-male-b.glb",
	}
	for unit_id: int in models:
		_check(_model_file(world.entities_by_id[unit_id]) == models[unit_id], "Unit %d uses %s." % [unit_id, models[unit_id]])
	_apply(world, [
		{"type": "UnitSpawned", "unitId": 20, "owner": 1, "unitType": "Soldier", "x": 9.5, "z": 9.5, "hp": 80, "index": 0},
		{"type": "UnitSpawned", "unitId": 21, "owner": 2, "unitType": "Soldier", "x": 30.5, "z": 30.5, "hp": 80, "index": 1},
	])
	_check(_model_file(world.entities_by_id[20]) == "character-knight.glb", "Player 1 soldiers are knights.")
	_check(_model_file(world.entities_by_id[21]) == "character-robot.glb", "Player 2 soldiers are robots.")
	# Los modelos dependen del jugador dueño, no de quién mira: P1 caballeros, P2 robots.
	_check(world.unit_visual_path("Worker", 1).ends_with("character-worker.glb"), "Player 1 workers are workers.")
	_check(world.unit_visual_path("Worker", 2).ends_with("character-male-b.glb"), "Player 2 workers are male-b.")
	_check(world.unit_visual_path("Soldier", 1).ends_with("character-knight.glb"), "Player 1 soldiers are knights for everyone.")
	_check(world.unit_visual_path("Soldier", 2).ends_with("character-robot.glb"), "Player 2 soldiers are robots for everyone.")
	_free_setup(setup)


func _test_fence_team_banners() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	_apply(world, [
		{"type": "BuildingPlaced", "buildingId": 50, "owner": 1, "buildingType": "Fence", "footprint": {"column": 10, "row": 4, "width": 1, "height": 1}, "hp": 10, "index": 0},
		{"type": "BuildingPlaced", "buildingId": 51, "owner": 2, "buildingType": "Fence", "footprint": {"column": 29, "row": 35, "width": 1, "height": 1}, "hp": 10, "index": 1},
		{"type": "MapRevisionChanged", "revision": 1, "index": 2},
	], 1)
	for fence_id: int in [50, 51]:
		var fence: Node3D = world.entities_by_id[fence_id]
		var owner := int(fence.get_meta("owner"))
		var flag := fence.find_child("Flag", true, false) as MeshInstance3D
		_check(flag != null, "Fence %d has a banner." % fence_id)
		if flag != null:
			_check(
				(flag.material_override as StandardMaterial3D).albedo_color.is_equal_approx(world.TEAM_COLORS[owner]),
				"Fence %d banner has its player's color." % fence_id
			)
		# El modelo queda centrado en su celda (el original está corrido 1 m en -X).
		var bounds := AABB()
		var first := true
		for mesh: Node in fence.find_children("*", "MeshInstance3D", true, false):
			if _has_ancestor_named(mesh, ["TeamBanner", "Scaffolding"], fence):
				continue
			var box: AABB = fence.global_transform.affine_inverse() * (mesh as MeshInstance3D).global_transform * (mesh as MeshInstance3D).get_aabb()
			bounds = box if first else bounds.merge(box)
			first = false
		_check(absf(bounds.get_center().x) < 0.05 and absf(bounds.get_center().z) < 0.05, "Fence %d model is centered on its cell: %s" % [fence_id, str(bounds)])
		_check(bounds.size.x > bounds.size.z, "Fence %d runs along X, facing the default camera." % fence_id)
	var colors: Array = []
	for fence_id: int in [50, 51]:
		colors.append((world.entities_by_id[fence_id].find_child("Flag", true, false).material_override as StandardMaterial3D).albedo_color)
	_check(not colors[0].is_equal_approx(colors[1]), "The two players' fences look different.")
	_check(world.entities_by_id[1].find_child("TeamBanner", true, false) == null, "Other buildings keep their own colored models.")
	_free_setup(setup)


func _test_farm_scene_models() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	_apply(world, [
		{"type": "BuildingPlaced", "buildingId": 60, "owner": 1, "buildingType": "Farm", "footprint": {"column": 2, "row": 8, "width": 2, "height": 2}, "hp": 20, "index": 0},
		{"type": "BuildingPlaced", "buildingId": 61, "owner": 2, "buildingType": "Farm", "footprint": {"column": 36, "row": 30, "width": 2, "height": 2}, "hp": 20, "index": 1},
		{"type": "MapRevisionChanged", "revision": 1, "index": 2},
	], 1)
	var expected_house := {60: "building_home_A_blue.gltf", 61: "building_home_A_red.gltf"}
	for farm_id: int in expected_house:
		var farm: Node3D = world.entities_by_id[farm_id]
		_check(_model_file(farm) == "farm.tscn", "Farm %d uses the farm scene." % farm_id)
		var colored := farm.find_child("Colored", true, false)
		var houses: Array = colored.get_children() if colored != null else []
		_check(
			houses.size() == 1 and houses[0].scene_file_path.get_file() == expected_house[farm_id],
			"Farm %d house is %s." % [farm_id, expected_house[farm_id]]
		)
		_check(farm.find_child("Dirt", true, false) != null and farm.find_child("Grain", true, false) != null, "Farm %d keeps its dirt and grain." % farm_id)
	# El andamio se ve solo mientras la granja está en obra.
	var scaffolding := world.entities_by_id[60].find_child("Scaffolding", true, false) as Node3D
	_check(scaffolding != null and scaffolding.visible, "A farm under construction shows its scaffolding.")
	_apply(world, [{"type": "BuildingProgressChanged", "buildingId": 60, "hp": 120, "index": 0}], 1)
	_check(scaffolding.visible, "The scaffolding stays while the construction progresses.")
	_apply(world, [{"type": "BuildingCompleted", "buildingId": 60, "index": 0}], 1)
	_check(not scaffolding.visible, "A completed farm hides its scaffolding.")

	# El cultivo se ve mientras produce y se oculta mientras está agotada (también reparando).
	var grain := world.entities_by_id[60].find_child("Grain", true, false) as Node3D
	_check(grain.visible, "A producing farm shows its grain.")
	_apply(world, [{"type": "FarmDepleted", "farmId": 60, "index": 0}], 1)
	_check(not grain.visible, "A depleted farm hides all its grain.")
	_check(world.entities_by_id[61].find_child("Grain", true, false).visible, "Other farms keep their grain.")
	_apply(world, [{"type": "FarmRepairStarted", "farmId": 60, "index": 0}], 1)
	_check(not grain.visible, "The grain stays hidden while the farm is being repaired.")
	_apply(world, [{"type": "FarmRepairCompleted", "farmId": 60, "index": 0}], 1)
	_check(grain.visible, "A repaired farm shows its grain again.")
	_check(not scaffolding.visible, "Repairing a farm does not bring the scaffolding back.")
	var blue_house := world.entities_by_id[60].find_child("Colored", true, false).get_child(0) as Node3D
	var red_house := world.entities_by_id[61].find_child("Colored", true, false).get_child(0) as Node3D
	_check(red_house.transform.is_equal_approx(blue_house.transform), "The red house keeps the position of the blue one.")
	_free_setup(setup)


## Cada tipo de edificio usa su escena de scenes/buildings: el nodo "Colored" es azul para P1
## y rojo para P2, y el andamio se ve solo en obra (salvo el Centro Principal, que no tiene).
func _test_building_scenes() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	var placed := {
		70: ["Farm", 1, 2, 8], 71: ["Farm", 2, 36, 30],
		72: ["Barracks", 1, 2, 11], 73: ["Barracks", 2, 35, 26],
		74: ["Tower", 1, 5, 15], 75: ["Tower", 2, 33, 23],
		76: ["Fence", 1, 10, 4], 77: ["Fence", 2, 29, 35],
	}
	var sizes := {"Farm": 2, "Barracks": 3, "Tower": 2, "Fence": 1}
	var events: Array = []
	for building_id: int in placed:
		var data: Array = placed[building_id]
		events.append({
			"type": "BuildingPlaced", "buildingId": building_id, "owner": data[1], "buildingType": data[0],
			"footprint": {"column": data[2], "row": data[3], "width": sizes[data[0]], "height": sizes[data[0]]},
			"hp": 10, "index": events.size(),
		})
	events.append({"type": "MapRevisionChanged", "revision": 1, "index": events.size()})
	_apply(world, events, 1)
	var scenes := {
		"MainCenter": "main_center.tscn", "Farm": "farm.tscn", "Barracks": "barracks.tscn",
		"Tower": "tower.tscn", "Fence": "fence.tscn",
	}
	var all_ids: Array = placed.keys()
	all_ids.append_array([1, 2])
	for building_id: int in all_ids:
		var building: Node3D = world.entities_by_id[building_id]
		var state: Dictionary = building.get_meta("entity_state")
		var building_type: String = state["buildingType"]
		var owner := int(state["owner"])
		_check(_model_file(building) == scenes[building_type], "%s %d uses %s." % [building_type, building_id, scenes[building_type]])
		var colored := building.find_child("Colored", true, false)
		if building_type == "Fence":
			_check(colored == null and building.find_child("TeamBanner", true, false) != null, "Fences are colored with a banner.")
		else:
			var color_dir := "/blue/" if owner == 1 else "/red/"
			var models: Array = colored.get_children() if colored != null else []
			_check(not models.is_empty(), "%s %d has colored models." % [building_type, building_id])
			for model: Node in models:
				_check(model.scene_file_path.contains(color_dir), "%s %d of player %d uses %s." % [building_type, building_id, owner, model.scene_file_path.get_file()])
		var scaffolding := building.find_child("Scaffolding", true, false) as Node3D
		if building_type == "MainCenter":
			_check(scaffolding == null, "The main center has no scaffolding.")
		else:
			_check(scaffolding != null and scaffolding.visible, "%s %d under construction shows its scaffolding." % [building_type, building_id])
	var completions: Array = []
	for building_id: int in placed:
		completions.append({"type": "BuildingCompleted", "buildingId": building_id, "index": completions.size()})
	_apply(world, completions, 1)
	for building_id: int in placed:
		var scaffolding := world.entities_by_id[building_id].find_child("Scaffolding", true, false) as Node3D
		_check(not scaffolding.visible, "Building %d hides its scaffolding once completed." % building_id)
	_free_setup(setup)


## Un edificio destruido deja ruinas visuales del tamaño de su huella, que se hunden y se
## liberan; un edificio nuevo en esas celdas las quita en el acto.
func _test_building_ruins() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	_apply(world, [
		{"type": "BuildingPlaced", "buildingId": 80, "owner": 2, "buildingType": "Barracks", "footprint": {"column": 20, "row": 20, "width": 3, "height": 3}, "hp": 400, "index": 0},
		{"type": "BuildingPlaced", "buildingId": 81, "owner": 1, "buildingType": "Fence", "footprint": {"column": 10, "row": 4, "width": 1, "height": 1}, "hp": 10, "index": 1},
		{"type": "MapRevisionChanged", "revision": 1, "index": 2},
	], 1)
	var barracks_position: Vector3 = world.entities_by_id[80].position
	_apply(world, [
		{"type": "EntityDestroyed", "entityId": 80, "index": 0},
		{"type": "EntityDestroyed", "entityId": 81, "index": 1},
		{"type": "MapRevisionChanged", "revision": 2, "index": 2},
	], 2)
	var buildings: Node = world.active_world.get_node("Entities/Buildings")
	var barracks_ruins := buildings.get_node_or_null("Ruins_80") as Node3D
	var fence_ruins := buildings.get_node_or_null("Ruins_81") as Node3D
	_check(barracks_ruins != null and fence_ruins != null, "Destroyed buildings leave ruins.")
	if barracks_ruins == null or fence_ruins == null:
		_free_setup(setup)
		return
	_check(barracks_ruins.scene_file_path.get_file() == "building_destroyed.gltf", "The ruins use building_destroyed.gltf.")
	_check(barracks_ruins.position.is_equal_approx(barracks_position), "The ruins sit where the building was.")
	var bounds: AABB = world._model_bounds(barracks_ruins)
	var width := maxf(bounds.size.x, bounds.size.z) * barracks_ruins.scale.x
	_check(is_equal_approx(width, 3.0 * world.RUINS_FOOTPRINT_FILL), "The ruins fill the building footprint (%.2f m)." % width)
	_check(barracks_ruins.scale.x > fence_ruins.scale.x * 2.0, "Bigger buildings leave bigger ruins.")
	_check(not world.entities_by_id.has(80), "Ruins are not entities: nothing to select or attack.")
	_check(world.validate_placement("Farm", 20, 20).get("ok", false), "The place is free even while the ruins are shown.")

	# Una obra nueva sobre las ruinas las quita en el acto.
	_apply(world, [
		{"type": "BuildingPlaced", "buildingId": 82, "owner": 1, "buildingType": "Fence", "footprint": {"column": 10, "row": 4, "width": 1, "height": 1}, "hp": 10, "index": 0},
		{"type": "MapRevisionChanged", "revision": 3, "index": 1},
	], 3)
	_check(not is_instance_valid(fence_ruins) or fence_ruins.is_queued_for_deletion(), "A new building on the same cells removes the ruins.")

	await create_timer(world.RUINS_LINGER_SECONDS * 0.5).timeout
	_check(is_instance_valid(barracks_ruins) and is_equal_approx(barracks_ruins.position.y, barracks_position.y), "The ruins stay for a few seconds.")
	await create_timer(world.RUINS_LINGER_SECONDS * 0.5 + world.RUINS_SINK_SECONDS * 0.6).timeout
	_check(is_instance_valid(barracks_ruins) and barracks_ruins.position.y < barracks_position.y, "Then they sink into the ground.")
	await create_timer(world.RUINS_SINK_SECONDS * 0.4 + 0.3).timeout
	_check(not is_instance_valid(barracks_ruins), "And they are freed.")
	_free_setup(setup)


func _has_ancestor_named(node: Node, names: Array, stop_at: Node) -> bool:
	var current := node.get_parent()
	while current != null and current != stop_at:
		if names.has(str(current.name)):
			return true
		current = current.get_parent()
	return false


func _model_file(unit: Node3D) -> String:
	for child: Node in unit.get_children():
		if not child.scene_file_path.is_empty():
			return child.scene_file_path.get_file()
	return ""


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
