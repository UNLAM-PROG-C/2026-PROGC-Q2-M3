extends Node3D

signal world_preparation_completed(initialization_id: int)
signal world_preparation_failed(initialization_id: int, detail: String)
signal world_discarded(initialization_id: int)

const GRASS_TEXTURE_PATH := "res://assets/textures/Cartoon_green_texture_grass.jpg"
const TREE_VISUAL_PATH := "res://assets/nature/Tree_1_A_Color1.gltf"

const UNIT_VISUAL_PATHS := {
	"Worker": "res://assets/units/mini_characters/character-worker.glb",
	"Soldier": "res://assets/units/mini_characters/character-soldier.glb",
}

const BUILDING_VISUAL_PATHS := {
	"MainCenter": {
		1: "res://assets/buildings/blue/building_castle_blue.gltf",
		2: "res://assets/buildings/red/building_castle_red.gltf",
	},
	"Farm": {
		1: "res://assets/buildings/blue/building_home_A_blue.gltf",
		2: "res://assets/buildings/red/building_home_A_red.gltf",
	},
	"Barracks": {
		1: "res://assets/buildings/blue/building_barracks_blue.gltf",
		2: "res://assets/buildings/red/building_barracks_red.gltf",
	},
	"Tower": {
		1: "res://assets/buildings/blue/building_tower_B_blue.gltf",
		2: "res://assets/buildings/red/building_tower_B_red.gltf",
	},
	"Fence": "res://assets/buildings/neutral/fence_wood_straight.gltf",
}

const INITIAL_STATE_KEYS := [
	"sessionId",
	"initializationId",
	"playerId",
	"tick",
	"map",
	"players",
	"units",
	"buildings",
	"trees",
	"ownResources",
	"ownProduction",
	"catalog",
]

var active_world: Node3D
var active_snapshot: Dictionary = {}
var active_catalog: Dictionary = {}
var entities_by_id: Dictionary = {}
var active_initialization_id := 0

var _session_controller: Node


func _ready() -> void:
	_session_controller = get_node_or_null("/root/SessionController")
	if _session_controller == null:
		return

	var callbacks := {
		"initial_state_preparation_requested": Callable(self, "_on_initial_state_preparation_requested"),
		"initial_state_discard_requested": Callable(self, "_on_initial_state_discard_requested"),
	}
	for signal_name in callbacks:
		if not _session_controller.has_signal(signal_name):
			_report_error(
				"InvalidSessionController",
				"SessionController no expone la señal %s." % signal_name
			)
			return
		var callback: Callable = callbacks[signal_name]
		if not _session_controller.is_connected(signal_name, callback):
			var connect_error: Error = _session_controller.connect(signal_name, callback)
			if connect_error != OK:
				_report_error(
					"SessionSignalConnectionFailed",
					"No se pudo enlazar %s." % signal_name
				)
				return


func _exit_tree() -> void:
	if not is_instance_valid(_session_controller):
		return
	var callbacks := {
		"initial_state_preparation_requested": Callable(self, "_on_initial_state_preparation_requested"),
		"initial_state_discard_requested": Callable(self, "_on_initial_state_discard_requested"),
	}
	for signal_name in callbacks:
		var callback: Callable = callbacks[signal_name]
		if _session_controller.is_connected(signal_name, callback):
			_session_controller.disconnect(signal_name, callback)


func prepare_initial_state(
	initialization_id: int,
	payload: Variant,
	expected_session_id: String = "",
	expected_player_id: int = 0
) -> bool:
	var validation_error := _validate_initial_state(
		payload,
		initialization_id,
		expected_session_id,
		expected_player_id
	)
	if not validation_error.is_empty():
		_fail_preparation(initialization_id, validation_error)
		return false

	var state: Dictionary = payload
	var build_result := _build_staged_world(state, initialization_id)
	if not bool(build_result["ok"]):
		_fail_preparation(initialization_id, str(build_result["detail"]))
		return false

	var staged_world: Node3D = build_result["world"]
	var previous_world := active_world
	add_child(staged_world)
	active_world = staged_world
	active_snapshot = state.duplicate(true)
	active_catalog = state["catalog"].duplicate(true)
	entities_by_id = build_result["entities"]
	active_initialization_id = initialization_id

	if previous_world != null and is_instance_valid(previous_world):
		remove_child(previous_world)
		previous_world.queue_free()

	_center_camera(state["map"])
	world_preparation_completed.emit(initialization_id)
	_complete_session_preparation(initialization_id, true, "")
	return true


func discard_initial_state(initialization_id: int) -> void:
	if initialization_id != active_initialization_id:
		return

	if active_world != null and is_instance_valid(active_world):
		remove_child(active_world)
		active_world.queue_free()
	active_world = null
	active_snapshot.clear()
	active_catalog.clear()
	entities_by_id.clear()
	active_initialization_id = 0
	world_discarded.emit(initialization_id)


func _on_initial_state_preparation_requested(initialization_id: int, payload: Dictionary) -> void:
	var expected_session_id := str(_session_controller.get("session_id"))
	var expected_player_id := int(_session_controller.get("player_id"))
	prepare_initial_state(
		initialization_id,
		payload,
		expected_session_id,
		expected_player_id
	)


func _on_initial_state_discard_requested(initialization_id: int) -> void:
	discard_initial_state(initialization_id)


func _validate_initial_state(
	payload: Variant,
	requested_initialization_id: int,
	expected_session_id: String,
	expected_player_id: int
) -> String:
	if typeof(payload) != TYPE_DICTIONARY:
		return "InitialState debe ser un objeto."

	var state: Dictionary = payload
	var missing_key := _first_missing_key(state, INITIAL_STATE_KEYS, "InitialState")
	if not missing_key.is_empty():
		return missing_key

	if not _is_non_empty_string(state["sessionId"]):
		return "InitialState.sessionId debe ser un string no vacío."
	if not _is_positive_integer(state["initializationId"]):
		return "InitialState.initializationId debe ser un entero positivo."
	if int(state["initializationId"]) != requested_initialization_id:
		return "InitialState.initializationId no coincide con la solicitud vigente."
	if not _is_positive_integer(state["playerId"]):
		return "InitialState.playerId debe ser un entero positivo."
	if not _is_non_negative_integer(state["tick"]):
		return "InitialState.tick debe ser un entero no negativo."
	if not expected_session_id.is_empty() and state["sessionId"] != expected_session_id:
		return "InitialState.sessionId no coincide con la sesión asignada."
	if expected_player_id > 0 and int(state["playerId"]) != expected_player_id:
		return "InitialState.playerId no coincide con el jugador asignado."

	var map: Variant = state["map"]
	if typeof(map) != TYPE_DICTIONARY:
		return "InitialState.map debe ser un objeto."
	var map_error := _validate_map(map)
	if not map_error.is_empty():
		return map_error

	for key in ["players", "units", "buildings", "trees", "ownProduction"]:
		if typeof(state[key]) != TYPE_ARRAY:
			return "InitialState.%s debe ser un array." % key
	if typeof(state["ownResources"]) != TYPE_DICTIONARY:
		return "InitialState.ownResources debe ser un objeto."
	var resources_error := _validate_resources(state["ownResources"], "InitialState.ownResources")
	if not resources_error.is_empty():
		return resources_error

	var catalog_result := _validate_catalog(state["catalog"])
	if not str(catalog_result["error"]).is_empty():
		return str(catalog_result["error"])

	var player_ids: Dictionary = {}
	var player_records: Dictionary = {}
	var players: Array = state["players"]
	if players.size() != 2:
		return "InitialState.players debe contener los dos jugadores de la partida."
	for index in range(players.size()):
		var player: Variant = players[index]
		if typeof(player) != TYPE_DICTIONARY:
			return "InitialState.players[%d] debe ser un objeto." % index
		var player_error := _first_missing_key(
			player,
			PackedStringArray(["playerId", "mainCenterId"]),
			"InitialState.players[%d]" % index
		)
		if not player_error.is_empty():
			return player_error
		if not _is_positive_integer(player["playerId"]) or not _is_positive_integer(player["mainCenterId"]):
			return "InitialState.players[%d] requiere playerId y mainCenterId positivos." % index
		var player_id := int(player["playerId"])
		if player_ids.has(player_id):
			return "InitialState.players contiene el playerId duplicado %d." % player_id
		player_ids[player_id] = true
		player_records[player_id] = player
	if not player_ids.has(1) or not player_ids.has(2):
		return "InitialState.players debe incluir los playerId 1 y 2."
	if not player_ids.has(int(state["playerId"])):
		return "InitialState.playerId no está presente en InitialState.players."

	var map_width := float(map["columns"]) * float(map["cellSize"])
	var map_depth := float(map["rows"]) * float(map["cellSize"])
	var entity_ids: Dictionary = {}
	var building_records: Dictionary = {}
	var unit_catalog: Dictionary = catalog_result["units"]
	var building_catalog: Dictionary = catalog_result["buildings"]

	var units: Array = state["units"]
	for index in range(units.size()):
		var unit: Variant = units[index]
		if typeof(unit) != TYPE_DICTIONARY:
			return "InitialState.units[%d] debe ser un objeto." % index
		var unit_error := _first_missing_key(
			unit,
			PackedStringArray(["unitId", "owner", "unitType", "x", "z", "hp", "order"]),
			"InitialState.units[%d]" % index
		)
		if not unit_error.is_empty():
			return unit_error
		var unit_id: Variant = unit["unitId"]
		if not _is_positive_integer(unit_id):
			return "InitialState.units[%d].unitId debe ser positivo." % index
		var duplicate_error := _register_entity_id(int(unit_id), entity_ids, "unit")
		if not duplicate_error.is_empty():
			return duplicate_error
		if not _is_positive_integer(unit["owner"]) or not player_ids.has(int(unit["owner"])):
			return "La unidad %d tiene un owner desconocido." % int(unit_id)
		if not _is_non_empty_string(unit["unitType"]):
			return "La unidad %d requiere unitType." % int(unit_id)
		var unit_type: String = unit["unitType"]
		if not UNIT_VISUAL_PATHS.has(unit_type):
			return "La unidad %d tiene unitType sin representación: %s." % [int(unit_id), unit_type]
		if not unit_catalog.has(unit_type):
			return "La unidad %d no tiene entrada en catalog.units." % int(unit_id)
		if not _is_finite_number(unit["x"]) or not _is_finite_number(unit["z"]):
			return "La unidad %d tiene coordenadas no finitas." % int(unit_id)
		if not _is_within_map(float(unit["x"]), float(unit["z"]), map_width, map_depth):
			return "La unidad %d está fuera de los límites del mapa." % int(unit_id)
		if not _is_non_negative_integer(unit["hp"]):
			return "La unidad %d tiene hp inválido." % int(unit_id)
		if int(unit["hp"]) > int(unit_catalog[unit_type]["maxHp"]):
			return "La unidad %d supera el maxHp de su catálogo." % int(unit_id)
		if not _is_non_empty_string(unit["order"]):
			return "La unidad %d requiere una orden válida." % int(unit_id)

	var buildings: Array = state["buildings"]
	for index in range(buildings.size()):
		var building: Variant = buildings[index]
		if typeof(building) != TYPE_DICTIONARY:
			return "InitialState.buildings[%d] debe ser un objeto." % index
		var building_error := _first_missing_key(
			building,
			PackedStringArray([
				"buildingId",
				"owner",
				"buildingType",
				"footprint",
				"hp",
				"isCompleted",
			]),
			"InitialState.buildings[%d]" % index
		)
		if not building_error.is_empty():
			return building_error
		var building_id: Variant = building["buildingId"]
		if not _is_positive_integer(building_id):
			return "InitialState.buildings[%d].buildingId debe ser positivo." % index
		var duplicate_building_error := _register_entity_id(int(building_id), entity_ids, "building")
		if not duplicate_building_error.is_empty():
			return duplicate_building_error
		if not _is_positive_integer(building["owner"]) or not player_ids.has(int(building["owner"])):
			return "El edificio %d tiene un owner desconocido." % int(building_id)
		if not _is_non_empty_string(building["buildingType"]):
			return "El edificio %d requiere buildingType." % int(building_id)
		var building_type: String = building["buildingType"]
		var visual_path := _building_visual_path(building_type, int(building["owner"]))
		if visual_path.is_empty():
			return "El edificio %d tiene buildingType sin representación: %s." % [int(building_id), building_type]
		if not building_catalog.has(building_type):
			return "El edificio %d no tiene entrada en catalog.buildings." % int(building_id)
		if typeof(building["footprint"]) != TYPE_DICTIONARY:
			return "El edificio %d tiene footprint inválido." % int(building_id)
		var footprint: Dictionary = building["footprint"]
		var footprint_error := _first_missing_key(
			footprint,
			PackedStringArray(["column", "row", "width", "height"]),
			"building[%d].footprint" % int(building_id)
		)
		if not footprint_error.is_empty():
			return footprint_error
		if (
			not _is_non_negative_integer(footprint["column"])
			or not _is_non_negative_integer(footprint["row"])
			or not _is_positive_integer(footprint["width"])
			or not _is_positive_integer(footprint["height"])
		):
			return "El edificio %d tiene una huella con valores inválidos." % int(building_id)
		if (
			int(footprint["column"]) + int(footprint["width"]) > int(map["columns"])
			or int(footprint["row"]) + int(footprint["height"]) > int(map["rows"])
		):
			return "La huella del edificio %d excede los límites del mapa." % int(building_id)
		var catalog_entry: Dictionary = building_catalog[building_type]
		if (
			int(footprint["width"]) != int(catalog_entry["footprintWidth"])
			or int(footprint["height"]) != int(catalog_entry["footprintHeight"])
		):
			return "La huella del edificio %d no coincide con catalog.buildings." % int(building_id)
		if not _is_non_negative_integer(building["hp"]):
			return "El edificio %d tiene hp inválido." % int(building_id)
		if int(building["hp"]) > int(catalog_entry["maxHp"]):
			return "El edificio %d supera el maxHp de su catálogo." % int(building_id)
		if typeof(building["isCompleted"]) != TYPE_BOOL:
			return "El edificio %d tiene isCompleted inválido." % int(building_id)
		if building.has("farm") and building["farm"] != null:
			if building_type != "Farm" or not bool(building["isCompleted"]):
				return "El edificio %d no puede incluir estado público de granja." % int(building_id)
			if typeof(building["farm"]) != TYPE_DICTIONARY:
				return "El edificio %d tiene farm inválido." % int(building_id)
			var farm_error := _first_missing_key(
				building["farm"],
				PackedStringArray(["isDepleted", "isRepairing"]),
				"building[%d].farm" % int(building_id)
			)
			if not farm_error.is_empty():
				return farm_error
			if (
				typeof(building["farm"]["isDepleted"]) != TYPE_BOOL
				or typeof(building["farm"]["isRepairing"]) != TYPE_BOOL
			):
				return "El edificio %d tiene estado de granja inválido." % int(building_id)
		building_records[int(building_id)] = building

	for player_id in player_records:
		var player: Dictionary = player_records[player_id]
		var center_id := int(player["mainCenterId"])
		if not building_records.has(center_id):
			return "El jugador %d referencia un Centro Principal inexistente." % int(player_id)
		var main_center: Dictionary = building_records[center_id]
		if (
			main_center["buildingType"] != "MainCenter"
			or int(main_center["owner"]) != int(player_id)
		):
			return "El jugador %d referencia un Centro Principal incompatible." % int(player_id)

	var trees: Array = state["trees"]
	for index in range(trees.size()):
		var tree: Variant = trees[index]
		if typeof(tree) != TYPE_DICTIONARY:
			return "InitialState.trees[%d] debe ser un objeto." % index
		var tree_error := _first_missing_key(
			tree,
			PackedStringArray(["treeId", "column", "row", "x", "z", "isAvailable"]),
			"InitialState.trees[%d]" % index
		)
		if not tree_error.is_empty():
			return tree_error
		var tree_id: Variant = tree["treeId"]
		if not _is_positive_integer(tree_id):
			return "InitialState.trees[%d].treeId debe ser positivo." % index
		var duplicate_tree_error := _register_entity_id(int(tree_id), entity_ids, "tree")
		if not duplicate_tree_error.is_empty():
			return duplicate_tree_error
		if (
			not _is_non_negative_integer(tree["column"])
			or not _is_non_negative_integer(tree["row"])
			or int(tree["column"]) >= int(map["columns"])
			or int(tree["row"]) >= int(map["rows"])
		):
			return "El árbol %d tiene una celda fuera del mapa." % int(tree_id)
		if not _is_finite_number(tree["x"]) or not _is_finite_number(tree["z"]):
			return "El árbol %d tiene coordenadas no finitas." % int(tree_id)
		if not _is_within_map(float(tree["x"]), float(tree["z"]), map_width, map_depth):
			return "El árbol %d está fuera de los límites del mapa." % int(tree_id)
		if typeof(tree["isAvailable"]) != TYPE_BOOL:
			return "El árbol %d tiene isAvailable inválido." % int(tree_id)

	var own_production: Array = state["ownProduction"]
	var producer_ids: Dictionary = {}
	for index in range(own_production.size()):
		var producer: Variant = own_production[index]
		if typeof(producer) != TYPE_DICTIONARY:
			return "InitialState.ownProduction[%d] debe ser un objeto." % index
		var producer_error := _first_missing_key(
			producer,
			PackedStringArray(["producerId", "status", "pending"]),
			"InitialState.ownProduction[%d]" % index
		)
		if not producer_error.is_empty():
			return producer_error
		if not _is_positive_integer(producer["producerId"]):
			return "InitialState.ownProduction[%d].producerId debe ser positivo." % index
		var producer_id := int(producer["producerId"])
		if producer_ids.has(producer_id):
			return "InitialState.ownProduction contiene el producerId duplicado %d." % producer_id
		producer_ids[producer_id] = true
		if not building_records.has(producer_id):
			return "La producción propia referencia el edificio %d inexistente." % producer_id
		var producer_building: Dictionary = building_records[producer_id]
		if int(producer_building["owner"]) != int(state["playerId"]):
			return "La producción propia referencia un edificio rival (%d)." % producer_id
		if not _is_non_empty_string(producer["status"]):
			return "El productor %d tiene status inválido." % producer_id
		if typeof(producer["pending"]) != TYPE_ARRAY:
			return "El productor %d tiene pending inválido." % producer_id
		if producer.has("activeUnitType") and producer["activeUnitType"] != null:
			if (
				not _is_non_empty_string(producer["activeUnitType"])
				or not unit_catalog.has(producer["activeUnitType"])
			):
				return "El productor %d tiene activeUnitType inválido." % producer_id
		if producer.has("remainingTicks") and producer["remainingTicks"] != null:
			if not _is_non_negative_integer(producer["remainingTicks"]):
				return "El productor %d tiene remainingTicks inválido." % producer_id
		for pending_type in producer["pending"]:
			if not _is_non_empty_string(pending_type) or not unit_catalog.has(pending_type):
				return "El productor %d tiene un tipo desconocido en pending." % producer_id

	return ""


func _validate_map(map: Variant) -> String:
	if typeof(map) != TYPE_DICTIONARY:
		return "InitialState.map debe ser un objeto."
	var map_data: Dictionary = map
	var missing_key := _first_missing_key(
		map_data,
		PackedStringArray(["cellSize", "columns", "rows", "revision"]),
		"InitialState.map"
	)
	if not missing_key.is_empty():
		return missing_key
	if not _is_finite_number(map_data["cellSize"]) or float(map_data["cellSize"]) <= 0.0:
		return "InitialState.map.cellSize debe ser positivo y finito."
	if not _is_positive_integer(map_data["columns"]) or not _is_positive_integer(map_data["rows"]):
		return "InitialState.map.columns y rows deben ser enteros positivos."
	if not _is_non_negative_integer(map_data["revision"]):
		return "InitialState.map.revision debe ser un entero no negativo."
	var width := float(map_data["columns"]) * float(map_data["cellSize"])
	var depth := float(map_data["rows"]) * float(map_data["cellSize"])
	if not is_finite(width) or not is_finite(depth) or width <= 0.0 or depth <= 0.0:
		return "InitialState.map produce dimensiones no finitas o no positivas."
	return ""


func _validate_catalog(catalog: Variant) -> Dictionary:
	if typeof(catalog) != TYPE_DICTIONARY:
		return {"error": "InitialState.catalog debe ser un objeto."}
	var data: Dictionary = catalog
	var missing_key := _first_missing_key(
		data,
		PackedStringArray([
			"ticksPerSecond",
			"units",
			"buildings",
			"farmRepairCost",
			"farmRepairWorkRequired",
			"gatherRange",
			"buildRange",
		]),
		"InitialState.catalog"
	)
	if not missing_key.is_empty():
		return {"error": missing_key}
	if not _is_positive_integer(data["ticksPerSecond"]):
		return {"error": "InitialState.catalog.ticksPerSecond debe ser positivo."}
	if typeof(data["units"]) != TYPE_ARRAY or typeof(data["buildings"]) != TYPE_ARRAY:
		return {"error": "InitialState.catalog.units y buildings deben ser arrays."}
	if not _is_non_negative_integer(data["farmRepairWorkRequired"]):
		return {"error": "InitialState.catalog.farmRepairWorkRequired es inválido."}
	if not _is_finite_number(data["gatherRange"]) or float(data["gatherRange"]) < 0.0:
		return {"error": "InitialState.catalog.gatherRange debe ser no negativo y finito."}
	if not _is_finite_number(data["buildRange"]) or float(data["buildRange"]) < 0.0:
		return {"error": "InitialState.catalog.buildRange debe ser no negativo y finito."}
	var repair_cost_error := _validate_resources(
		data["farmRepairCost"],
		"InitialState.catalog.farmRepairCost"
	)
	if not repair_cost_error.is_empty():
		return {"error": repair_cost_error}

	var units_by_type: Dictionary = {}
	for index in range(data["units"].size()):
		var unit: Variant = data["units"][index]
		if typeof(unit) != TYPE_DICTIONARY:
			return {"error": "InitialState.catalog.units[%d] debe ser un objeto." % index}
		var unit_error := _first_missing_key(
			unit,
			PackedStringArray([
				"unitType",
				"maxHp",
				"cost",
				"trainingDurationTicks",
				"speedPerSecond",
			]),
			"InitialState.catalog.units[%d]" % index
		)
		if not unit_error.is_empty():
			return {"error": unit_error}
		if not _is_non_empty_string(unit["unitType"]):
			return {"error": "InitialState.catalog.units[%d].unitType es inválido." % index}
		var unit_type: String = unit["unitType"]
		if units_by_type.has(unit_type):
			return {"error": "InitialState.catalog.units contiene %s duplicado." % unit_type}
		if not _is_positive_integer(unit["maxHp"]):
			return {"error": "catalog.units[%d].maxHp debe ser positivo." % index}
		if not _is_non_negative_integer(unit["trainingDurationTicks"]):
			return {"error": "catalog.units[%d].trainingDurationTicks es inválido." % index}
		if not _is_finite_number(unit["speedPerSecond"]) or float(unit["speedPerSecond"]) < 0.0:
			return {"error": "catalog.units[%d].speedPerSecond es inválido." % index}
		var cost_error := _validate_resources(unit["cost"], "catalog.units[%d].cost" % index)
		if not cost_error.is_empty():
			return {"error": cost_error}
		if unit.has("attack") and unit["attack"] != null:
			var attack_error := _validate_attack(unit["attack"], "catalog.units[%d].attack" % index)
			if not attack_error.is_empty():
				return {"error": attack_error}
		units_by_type[unit_type] = unit

	var buildings_by_type: Dictionary = {}
	for index in range(data["buildings"].size()):
		var building: Variant = data["buildings"][index]
		if typeof(building) != TYPE_DICTIONARY:
			return {"error": "InitialState.catalog.buildings[%d] debe ser un objeto." % index}
		var building_error := _first_missing_key(
			building,
			PackedStringArray([
				"buildingType",
				"maxHp",
				"footprintWidth",
				"footprintHeight",
			]),
			"InitialState.catalog.buildings[%d]" % index
		)
		if not building_error.is_empty():
			return {"error": building_error}
		if not _is_non_empty_string(building["buildingType"]):
			return {"error": "InitialState.catalog.buildings[%d].buildingType es inválido." % index}
		var building_type: String = building["buildingType"]
		if buildings_by_type.has(building_type):
			return {"error": "InitialState.catalog.buildings contiene %s duplicado." % building_type}
		if (
			not _is_positive_integer(building["maxHp"])
			or not _is_positive_integer(building["footprintWidth"])
			or not _is_positive_integer(building["footprintHeight"])
		):
			return {"error": "catalog.buildings[%d] tiene HP o huella inválidos." % index}
		if building.has("cost") and building["cost"] != null:
			var building_cost_error := _validate_resources(
				building["cost"],
				"catalog.buildings[%d].cost" % index
			)
			if not building_cost_error.is_empty():
				return {"error": building_cost_error}
		if building.has("attack") and building["attack"] != null:
			var building_attack_error := _validate_attack(
				building["attack"],
				"catalog.buildings[%d].attack" % index
			)
			if not building_attack_error.is_empty():
				return {"error": building_attack_error}
		buildings_by_type[building_type] = building

	return {
		"error": "",
		"units": units_by_type,
		"buildings": buildings_by_type,
	}


func _validate_resources(resources: Variant, context: String) -> String:
	if typeof(resources) != TYPE_DICTIONARY:
		return "%s debe ser un objeto." % context
	var data: Dictionary = resources
	var missing_key := _first_missing_key(data, PackedStringArray(["wood", "food"]), context)
	if not missing_key.is_empty():
		return missing_key
	if not _is_non_negative_integer(data["wood"]) or not _is_non_negative_integer(data["food"]):
		return "%s requiere wood y food enteros no negativos." % context
	return ""


func _validate_attack(attack: Variant, context: String) -> String:
	if typeof(attack) != TYPE_DICTIONARY:
		return "%s debe ser un objeto." % context
	var data: Dictionary = attack
	var missing_key := _first_missing_key(
		data,
		PackedStringArray(["damage", "range", "cooldownTicks"]),
		context
	)
	if not missing_key.is_empty():
		return missing_key
	if (
		not _is_non_negative_integer(data["damage"])
		or not _is_finite_number(data["range"])
		or float(data["range"]) < 0.0
		or not _is_non_negative_integer(data["cooldownTicks"])
	):
		return "%s tiene valores inválidos." % context
	return ""


func _build_staged_world(state: Dictionary, initialization_id: int) -> Dictionary:
	var staged_world := Node3D.new()
	staged_world.name = "WorldInitialization_%d" % initialization_id

	var terrain := Node3D.new()
	terrain.name = "Terrain"
	staged_world.add_child(terrain)
	var entities := Node3D.new()
	entities.name = "Entities"
	staged_world.add_child(entities)
	var buildings_container := Node3D.new()
	buildings_container.name = "Buildings"
	entities.add_child(buildings_container)
	var units_container := Node3D.new()
	units_container.name = "Units"
	entities.add_child(units_container)
	var trees_container := Node3D.new()
	trees_container.name = "Trees"
	entities.add_child(trees_container)

	var map: Dictionary = state["map"]
	var cell_size := float(map["cellSize"])
	var map_width := float(map["columns"]) * cell_size
	var map_depth := float(map["rows"]) * cell_size
	var ground_result := _create_ground(terrain, map_width, map_depth)
	if not bool(ground_result["ok"]):
		staged_world.free()
		return {"ok": false, "detail": str(ground_result["detail"])}
	_create_map_boundaries(terrain, map_width, map_depth)

	var entity_nodes: Dictionary = {}
	for building: Dictionary in state["buildings"]:
		var building_type: String = building["buildingType"]
		var path := 		_building_visual_path(building_type, int(building["owner"]))
		var footprint: Dictionary = building["footprint"]
		var center_x := (float(footprint["column"]) + float(footprint["width"]) / 2.0) * cell_size
		var center_z := (float(footprint["row"]) + float(footprint["height"]) / 2.0) * cell_size
		var instance_result := _create_entity_node(
			path,
			"Building_%d" % int(building["buildingId"]),
			building,
			Vector3(center_x, 0.0, center_z)
		)
		if not bool(instance_result["ok"]):
			staged_world.free()
			return {"ok": false, "detail": str(instance_result["detail"])}
		var node: Node3D = instance_result["node"]
		buildings_container.add_child(node)
		entity_nodes[int(building["buildingId"])] = node

	for unit: Dictionary in state["units"]:
		var path: String = UNIT_VISUAL_PATHS[unit["unitType"]]
		var instance_result := _create_entity_node(
			path,
			"Unit_%d" % int(unit["unitId"]),
			unit,
			Vector3(float(unit["x"]), 0.0, float(unit["z"]))
		)
		if not bool(instance_result["ok"]):
			staged_world.free()
			return {"ok": false, "detail": str(instance_result["detail"])}
		var node: Node3D = instance_result["node"]
		units_container.add_child(node)
		entity_nodes[int(unit["unitId"])] = node

	for tree: Dictionary in state["trees"]:
		var instance_result := _create_entity_node(
			TREE_VISUAL_PATH,
			"Tree_%d" % int(tree["treeId"]),
			tree,
			Vector3(float(tree["x"]), 0.0, float(tree["z"]))
		)
		if not bool(instance_result["ok"]):
			staged_world.free()
			return {"ok": false, "detail": str(instance_result["detail"])}
		var node: Node3D = instance_result["node"]
		trees_container.add_child(node)
		entity_nodes[int(tree["treeId"])] = node

	return {"ok": true, "world": staged_world, "entities": entity_nodes}


func _create_ground(terrain: Node3D, width: float, depth: float) -> Dictionary:
	var texture := ResourceLoader.load(GRASS_TEXTURE_PATH) as Texture2D
	if texture == null:
		return {
			"ok": false,
			"detail": "No se pudo cargar la textura del terreno: %s." % GRASS_TEXTURE_PATH,
		}

	var mesh := PlaneMesh.new()
	mesh.size = Vector2(width, depth)
	var material := StandardMaterial3D.new()
	material.albedo_texture = texture
	material.roughness = 1.0
	material.uv1_scale = Vector3(width / 4.0, depth / 4.0, 1.0)

	var ground := MeshInstance3D.new()
	ground.name = "Ground"
	ground.mesh = mesh
	ground.material_override = material
	ground.position = Vector3(width / 2.0, -0.05, depth / 2.0)
	terrain.add_child(ground)
	return {"ok": true}


func _create_map_boundaries(terrain: Node3D, width: float, depth: float) -> void:
	var boundary_container := Node3D.new()
	boundary_container.name = "Boundaries"
	terrain.add_child(boundary_container)
	var material := StandardMaterial3D.new()
	material.albedo_color = Color(0.19, 0.13, 0.08)
	material.roughness = 1.0

	var border_width := 0.12
	var border_height := 0.12
	_add_boundary(
		boundary_container,
		"North",
		Vector3(width + border_width, border_height, border_width),
		Vector3(width / 2.0, 0.0, 0.0),
		material
	)
	_add_boundary(
		boundary_container,
		"South",
		Vector3(width + border_width, border_height, border_width),
		Vector3(width / 2.0, 0.0, depth),
		material
	)
	_add_boundary(
		boundary_container,
		"West",
		Vector3(border_width, border_height, depth),
		Vector3(0.0, 0.0, depth / 2.0),
		material
	)
	_add_boundary(
		boundary_container,
		"East",
		Vector3(border_width, border_height, depth),
		Vector3(width, 0.0, depth / 2.0),
		material
	)


func _add_boundary(
	parent: Node3D,
	node_name: String,
	size: Vector3,
	boundary_position: Vector3,
	material: Material
) -> void:
	var mesh := BoxMesh.new()
	mesh.size = size
	var boundary := MeshInstance3D.new()
	boundary.name = node_name
	boundary.mesh = mesh
	boundary.material_override = material
	boundary.position = boundary_position
	parent.add_child(boundary)


func _create_entity_node(
	scene_path: String,
	node_name: String,
	entity_state: Dictionary,
	entity_position: Vector3
) -> Dictionary:
	var packed_scene := _load_visual_scene(scene_path)
	if packed_scene == null:
		return {
			"ok": false,
			"detail": "No se pudo cargar la representación visual %s para %s."
				% [scene_path, node_name],
		}
	var visual: Node = packed_scene.instantiate()
	if visual == null or not (visual is Node3D):
		if visual != null:
			visual.free()
		return {
			"ok": false,
			"detail": "La representación %s de %s no tiene una raíz Node3D."
				% [scene_path, node_name],
		}

	var entity_node := Node3D.new()
	entity_node.name = node_name
	entity_node.position = entity_position
	entity_node.set_meta("entity_id", _entity_id(entity_state))
	entity_node.set_meta("entity_state", entity_state.duplicate(true))
	entity_node.set_meta("owner", int(entity_state.get("owner", 0)))
	entity_node.add_child(visual)
	return {"ok": true, "node": entity_node}


func _load_visual_scene(path: String) -> PackedScene:
	return ResourceLoader.load(path) as PackedScene


func _building_visual_path(building_type: String, owner_id: int) -> String:
	var visual_mapping: Variant = BUILDING_VISUAL_PATHS.get(building_type)
	if typeof(visual_mapping) == TYPE_DICTIONARY:
		return str(visual_mapping.get(owner_id, ""))
	if typeof(visual_mapping) == TYPE_STRING:
		return str(visual_mapping)
	return ""


func _entity_id(entity: Dictionary) -> int:
	for key in ["unitId", "buildingId", "treeId"]:
		if entity.has(key):
			return int(entity[key])
	return 0


func _center_camera(map: Dictionary) -> void:
	var camera := get_node_or_null("RTSCamera") as Node3D
	if camera == null:
		return
	var camera_position := camera.global_position
	camera_position.x = float(map["columns"]) * float(map["cellSize"]) / 2.0
	camera_position.z = float(map["rows"]) * float(map["cellSize"]) / 2.0
	camera.global_position = camera_position


func _complete_session_preparation(
	initialization_id: int,
	succeeded: bool,
	error_detail: String
) -> void:
	if not is_instance_valid(_session_controller):
		return
	if not _session_controller.has_method("complete_initial_state_preparation"):
		_report_error(
			"InvalidSessionController",
			"SessionController no expone complete_initial_state_preparation()."
		)
		return
	_session_controller.call(
		"complete_initial_state_preparation",
		initialization_id,
		succeeded,
		error_detail
	)


func _fail_preparation(initialization_id: int, detail: String) -> void:
	push_error("InitialStatePreparationFailed: %s" % detail)
	world_preparation_failed.emit(initialization_id, detail)
	_complete_session_preparation(initialization_id, false, detail)


func _first_missing_key(data: Dictionary, keys: Variant, context: String) -> String:
	for key in keys:
		if not data.has(key):
			return "%s no contiene el campo requerido %s." % [context, key]
	return ""


func _register_entity_id(entity_id: int, registered: Dictionary, entity_kind: String) -> String:
	if registered.has(entity_id):
		return "ID de entidad duplicado %d al validar %s." % [entity_id, entity_kind]
	registered[entity_id] = true
	return ""


func _is_within_map(x: float, z: float, width: float, depth: float) -> bool:
	return x >= 0.0 and z >= 0.0 and x < width and z < depth


func _is_non_empty_string(value: Variant) -> bool:
	return typeof(value) == TYPE_STRING and not str(value).is_empty()


func _is_finite_number(value: Variant) -> bool:
	if typeof(value) == TYPE_INT:
		return true
	return typeof(value) == TYPE_FLOAT and is_finite(float(value))


func _is_non_negative_integer(value: Variant) -> bool:
	if typeof(value) == TYPE_INT:
		return value >= 0
	return (
		typeof(value) == TYPE_FLOAT
		and is_finite(float(value))
		and floor(float(value)) == float(value)
		and float(value) >= 0.0
	)


func _is_positive_integer(value: Variant) -> bool:
	return _is_non_negative_integer(value) and value > 0


func _report_error(error_code: String, detail: String) -> void:
	push_error("%s: %s" % [error_code, detail])
