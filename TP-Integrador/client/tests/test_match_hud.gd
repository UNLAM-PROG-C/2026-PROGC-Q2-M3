extends SceneTree

## HUD de la partida y TrainUnit: recursos propios, selección de edificios, cola de
## producción con avance interpolado, entrenamiento y avisos de órdenes rechazadas.

const InitialWorldScript = preload("res://scripts/initial_world.gd")
const UnitCommandControllerScript = preload("res://scripts/unit_command_controller.gd")
const MatchHudScript = preload("res://scripts/ui/match_hud.gd")
const ErrorTexts = preload("res://scripts/ui/error_texts.gd")

const OWN_MAIN_CENTER := 1
const RIVAL_MAIN_CENTER := 2
const UNFINISHED_BARRACKS := 15
const BARRACKS := 16
const FARM := 17
const EXPECTED_TESTS := 6


class FakeNetworkClient:
	extends Node

	signal message_received(message_type: String, payload: Variant, envelope: Dictionary)

	var sent_messages: Array[Dictionary] = []

	func send_message(message_type: String, payload: Variant = null, message_id: Variant = null) -> bool:
		sent_messages.append({"type": message_type, "payload": payload, "messageId": message_id})
		return true


class FakeSessionController:
	extends Node

	signal session_state_changed(state: int, state_name: String)

	var state_name := "Running"

	func can_issue_gameplay() -> bool:
		return state_name == "Running"

	func get_state_name() -> String:
		return state_name

	func complete_initial_state_preparation(
		_initialization_id: int,
		_succeeded: bool,
		_detail: String = ""
	) -> bool:
		return true

	func set_state_name(next_state: String) -> void:
		state_name = next_state
		session_state_changed.emit(0, next_state)


var _failed := false
var _completed_tests := 0
var _tick := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_error_texts()
	_test_resources_and_visibility()
	_test_train_unit_command()
	_test_production_panel()
	_test_rejection_messages()
	_test_building_selection_rules()
	_check(_completed_tests == EXPECTED_TESTS, "Only %d of %d tests completed." % [_completed_tests, EXPECTED_TESTS])
	if _failed:
		quit(1)
		return
	print("Match HUD tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _test_error_texts() -> void:
	_check(ErrorTexts.text_for("Production.QueueFull") == "La cola de entrenamiento está llena.", "Server codes drop their area.")
	_check(ErrorTexts.text_for("NotOwner") == "Esa entidad no es tuya.", "Local codes without area are translated.")
	_check(ErrorTexts.text_for("Network.RateLimited").contains("Demasiadas"), "Network codes keep their own text.")
	_check(ErrorTexts.text_for("Foo.Bar") == "Orden rechazada (Foo.Bar).", "Unknown codes show the raw code.")
	_check(ErrorTexts.command_result_error({"outcome": "Accepted", "entityResults": []}).is_empty(), "Accepted has no error.")
	_check(
		ErrorTexts.command_result_error(
			{"outcome": "PartiallyAccepted", "entityResults": [{"entityId": 3}, {"entityId": 5, "error": "Common.NotOwner"}]}
		) == "Common.NotOwner",
		"A partial result reports the first rejected entity."
	)
	_completed_tests += 1


func _test_resources_and_visibility() -> void:
	var setup := _create_setup()
	var hud: Control = setup["hud"]
	var world: Node = setup["world"]
	_check(hud.visible, "The HUD is visible while Running.")
	_check(_label(hud, "WoodLabel") == "200" and _label(hud, "FoodLabel") == "100", "Initial resources come from InitialState.ownResources.")
	_apply(world, [{"type": "ResourcesChanged", "wood": 140, "food": 35, "index": 0}])
	_check(_label(hud, "WoodLabel") == "140" and _label(hud, "FoodLabel") == "35", "ResourcesChanged updates the resource bar.")
	setup["session"].set_state_name("Ended")
	_check(not hud.visible, "The HUD hides outside Running.")
	_free_setup(setup)


func _test_train_unit_command() -> void:
	var setup := _create_setup()
	var hud: Control = setup["hud"]
	var controller: Node = setup["controller"]
	var network: FakeNetworkClient = setup["network"]
	controller.set_selected_building(OWN_MAIN_CENTER)
	_check(hud.selected_building_id == OWN_MAIN_CENTER, "Selecting an own building shows it in the HUD.")
	_check(_panel(hud).visible, "The selection panel is visible for a selected building.")
	_check(_label(hud, "Title") == "Centro Principal", "The panel names the building.")
	var button := hud.find_child("TrainButton", true, false) as Button
	_check(button.text == "Entrenar Trabajador (50 comida)" and not button.disabled, "The main center trains workers.")
	button.pressed.emit()
	_check(network.sent_messages.size() == 1, "The train button sends one command.")
	var sent: Dictionary = network.sent_messages[-1]
	var example: Dictionary = _example("client-to-server", "train-unit.json")
	_check(sent["type"] == "TrainUnit" and sent["messageId"] == 1, "TrainUnit uses the first increasing messageId.")
	_check(sent["payload"] == _with_integer_numbers(example["payload"]), "TrainUnit payload must match train-unit.json.")

	controller.set_selected_building(BARRACKS)
	_check(_label(hud, "Title") == "Cuartel", "The barracks panel is shown.")
	_check(button.text.begins_with("Entrenar Soldado"), "The barracks trains soldiers.")
	var world: Node = setup["world"]
	_check(world.issue_train_unit_command(OWN_MAIN_CENTER, "Soldier").get("error", "") == "WrongUnitType", "Main centers do not train soldiers.")
	_check(world.issue_train_unit_command(FARM, "Worker").get("error", "") == "NotAProducer", "Farms do not train units.")
	_check(world.issue_train_unit_command(RIVAL_MAIN_CENTER, "Worker").get("error", "") == "NotOwner", "Rival buildings cannot train for us.")
	_check(
		world.issue_train_unit_command(UNFINISHED_BARRACKS, "Soldier").get("error", "") == "ProducerNotOperational",
		"Buildings under construction cannot train."
	)
	_check(network.sent_messages.size() == 1, "Locally rejected TrainUnit commands are not sent.")
	_free_setup(setup)


func _test_production_panel() -> void:
	var setup := _create_setup()
	var hud: Control = setup["hud"]
	var world: Node = setup["world"]
	setup["controller"].set_selected_building(OWN_MAIN_CENTER)
	_check(_label(hud, "StatusLabel") == "Sin entrenamientos.", "An idle producer says so.")
	_check(not (hud.find_child("ProgressBar", true, false) as ProgressBar).visible, "An idle producer has no progress bar.")
	_apply(world, [{
		"type": "ProductionChanged", "producerId": OWN_MAIN_CENTER, "status": "Training",
		"activeUnitType": "Worker", "remainingTicks": 200, "pending": ["Worker", "Worker"], "index": 0,
	}])
	hud._process(0.0)
	_check(_label(hud, "StatusLabel") == "Entrenando Trabajador.", "Training shows the active unit.")
	_check(_label(hud, "QueueLabel") == "En cola: Trabajador, Trabajador", "The pending FIFO queue is listed.")
	var received: int = hud._production_received_usec[OWN_MAIN_CENTER]
	_check(is_zero_approx(hud.production_progress(OWN_MAIN_CENTER, received)), "Progress starts at the reported remaining ticks.")
	_check(
		is_equal_approx(hud.production_progress(OWN_MAIN_CENTER, received + 5_000_000), 0.5),
		"Progress is interpolated: 5 s at 20 ticks/s is 100 of 200 ticks."
	)
	_check(is_equal_approx(hud.production_progress(OWN_MAIN_CENTER, received + 60_000_000), 1.0), "Progress never exceeds 100%.")
	_apply(world, [{
		"type": "ProductionChanged", "producerId": OWN_MAIN_CENTER, "status": "PendingSpawn",
		"activeUnitType": "Worker", "remainingTicks": 0, "pending": [], "index": 0,
	}])
	hud._process(0.0)
	_check(_label(hud, "StatusLabel").contains("esperando lugar libre"), "PendingSpawn explains that there is no free cell.")
	_apply(world, [{"type": "ResourcesChanged", "wood": 200, "food": 10, "index": 0}])
	hud._process(0.0)
	_check((hud.find_child("FoodHint", true, false) as Label).visible, "Without enough food the HUD warns that the order will wait.")
	_check(not (hud.find_child("TrainButton", true, false) as Button).disabled, "Training without food is still allowed (05 §3).")
	_free_setup(setup)


func _test_rejection_messages() -> void:
	var setup := _create_setup()
	var hud: Control = setup["hud"]
	var world: Node = setup["world"]
	_apply(world, [], [{
		"clientCommandId": 7, "sequence": 7, "tick": _tick + 1, "outcome": "Rejected",
		"error": "Production.QueueFull", "entityResults": [{"entityId": 1, "error": "Production.QueueFull"}],
	}])
	_check(hud.get_messages().has("La cola de entrenamiento está llena."), "A rejected CommandResult is shown to the player.")
	world._handle_command_rejected({"messageId": 8, "error": "Network.RateLimited"})
	_check(hud.get_messages().has("Demasiadas órdenes seguidas. Esperá un momento."), "CommandRejected is shown to the player.")
	setup["controller"].command_failed.emit("TargetNotReachable", "detalle")
	_check(hud.get_messages().has("No se puede llegar a ese objetivo."), "Local command failures are shown to the player.")
	for index in range(6):
		hud.show_message("Aviso %d" % index)
	_check(hud.get_messages().size() <= 4, "Only the latest messages are kept on screen.")
	_free_setup(setup)


func _test_building_selection_rules() -> void:
	var setup := _create_setup()
	var hud: Control = setup["hud"]
	var controller: Node = setup["controller"]
	var world: Node = setup["world"]
	controller.set_selected_building(RIVAL_MAIN_CENTER)
	_check(controller.selected_building_id == 0, "Rival buildings cannot be selected.")
	controller.set_selected_building(UNFINISHED_BARRACKS)
	_check(_label(hud, "StateLabel").contains("En construcción"), "A construction shows its state.")
	_check((hud.find_child("TrainButton", true, false) as Button).disabled, "A construction cannot train yet.")
	_check(_label(hud, "StatusLabel") == "El edificio todavía no está operativo.", "The panel explains why it cannot train.")
	controller.set_selected_units(_ids([3]))
	_check(controller.selected_building_id == 0 and not _panel(hud).visible, "Selecting units clears the building selection.")
	controller.set_selected_building(BARRACKS)
	_check(controller.get_selected_units().is_empty(), "Selecting a building clears the unit selection.")
	_apply(world, [{"type": "EntityDestroyed", "entityId": BARRACKS, "index": 0}, {"type": "MapRevisionChanged", "revision": 1, "index": 1}], [], 1)
	_check(controller.selected_building_id == 0, "A destroyed building is deselected.")
	_check(not _panel(hud).visible, "The panel hides when its building is destroyed.")
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
	world.add_child(controller)
	_check(world.prepare_initial_state(1, _world_state()), "The HUD world should build.")
	var hud: Control = MatchHudScript.new()
	root.add_child(hud)
	hud.bind(world, controller, session)
	return {"world": world, "network": network, "session": session, "controller": controller, "hud": hud}


func _free_setup(setup: Dictionary) -> void:
	_completed_tests += 1
	for key: String in ["hud", "world", "network", "session"]:
		var node: Node = setup[key]
		if is_instance_valid(node):
			node.queue_free()


func _apply(world: Node, events: Array, command_results: Array = [], revision: int = 0) -> void:
	_tick += 1
	_check(
		world.apply_state_events({"tick": _tick, "mapRevision": revision, "events": events, "commandResults": command_results}),
		"The StateEvents batch at tick %d should apply." % _tick
	)


func _label(hud: Control, label_name: String) -> String:
	var label := hud.find_child(label_name, true, false) as Label
	return label.text if label != null else ""


func _panel(hud: Control) -> Control:
	return hud.find_child("SelectionPanel", true, false) as Control


func _ids(values: Array) -> Array[int]:
	var result: Array[int] = []
	for value: Variant in values:
		result.append(int(value))
	return result


## Mundo de 02-initial-state.json (visto por P1) más un Cuartel en obra, uno terminado y una granja.
func _world_state() -> Dictionary:
	var state: Dictionary = _with_integer_numbers(_example("server-to-client", "02-initial-state.json").get("payload", {}))
	state["buildings"].append_array([
		{
			"buildingId": UNFINISHED_BARRACKS, "owner": 1, "buildingType": "Barracks",
			"footprint": {"column": 10, "row": 14, "width": 3, "height": 3}, "hp": 40, "isCompleted": false,
		},
		{
			"buildingId": BARRACKS, "owner": 1, "buildingType": "Barracks",
			"footprint": {"column": 16, "row": 14, "width": 3, "height": 3}, "hp": 400, "isCompleted": true,
		},
		{
			"buildingId": FARM, "owner": 1, "buildingType": "Farm",
			"footprint": {"column": 20, "row": 14, "width": 2, "height": 2}, "hp": 200, "isCompleted": true,
			"farm": {"isDepleted": false, "isRepairing": false},
		},
	])
	state["ownProduction"].append({"producerId": BARRACKS, "status": "Idle", "pending": []})
	return state


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
