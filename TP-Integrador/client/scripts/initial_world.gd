extends Node3D

const PROTOCOL_CODEC = preload("res://scripts/network/protocol_codec.gd")
const CLIENT_NAVIGATION_SCRIPT = preload("res://scripts/client_navigation.gd")

signal world_preparation_completed(initialization_id: int)
signal world_preparation_failed(initialization_id: int, detail: String)
signal world_discarded(initialization_id: int)
signal unit_removed(unit_id: int)
signal building_removed(building_id: int)
signal world_sync_warning(error_code: String, detail: String)
signal world_sync_failed(detail: String)
signal map_revision_changed(revision: int)
signal command_result_received(client_command_id: int, result: Dictionary)
signal command_rejected_received(message_id: int, error_code: String)
signal repath_response_failed(error_code: String, detail: String)
signal resources_changed(resources: Dictionary)
signal production_changed(producer_id: int, production: Dictionary)
signal repath_requested(request: Dictionary)

const GRASS_TEXTURE_PATH := "res://assets/textures/Cartoon_green_texture_grass.jpg"
const TREE_VISUAL_PATH := "res://assets/nature/Tree_1_A_Color1.gltf"
const POSITION_RATE_HZ := 10
const MAX_RECENT_COMMAND_RESULTS := 128
const DEATH_ANIMATION := "die"
## Tiempo que el cuerpo queda caído después de la animación de muerte.
const DEATH_LINGER_SECONDS := 0.8
## Después se hunde bajo el suelo antes de liberarse, en vez de desaparecer de golpe.
## La profundidad supera la altura de los modelos de unidad (~0,8 m).
const DEATH_SINK_DEPTH := 1.2
const DEATH_SINK_SECONDS := 1.5
## Ruinas que deja un edificio destruido: solo visuales (el lugar ya quedó libre, 03 §6).
const RUINS_VISUAL_PATH := "res://assets/buildings/neutral/building_destroyed.gltf"
## Cuánto de la huella ocupan las ruinas.
const RUINS_FOOTPRINT_FILL := 0.9
const RUINS_LINGER_SECONDS := 4.0
const RUINS_SINK_SECONDS := 1.5
const UNIT_ORDER_KINDS := [
	"Idle",
	"Move",
	"GatherWood",
	"Build",
	"RepairFarm",
	"Attack",
]
const PRODUCTION_STATUSES := ["Idle", "WaitingForResources", "Training", "PendingSpawn"]
## Campo del payload que identifica el objetivo de cada orden de tarea (docs/protocol/examples).
const TASK_TARGET_KEYS := {
	"GatherWood": "treeId",
	"Attack": "targetId",
	"Build": "buildingId",
	"RepairFarm": "farmId",
}
## Edificios que se pueden colocar con Build (el Centro Principal no: Construction.NotBuildable).
const BUILDABLE_TYPES := ["Farm", "Barracks", "Tower", "Fence"]
## Qué entrena cada edificio productor (contrato 05, ProducerCatalog, DECIDIDO 05 §1).
const PRODUCER_UNIT_TYPES := {
	"MainCenter": "Worker",
	"Barracks": "Soldier",
}
## Tipo de unidad que acepta cada orden de tarea (contrato 04 §3, UnitOrderCompatibility).
const TASK_UNIT_TYPES := {
	"GatherWood": "Worker",
	"Build": "Worker",
	"RepairFarm": "Worker",
	"Attack": "Soldier",
}

## Color de cada jugador para los detalles que no vienen en el modelo (banderines de vallas),
## parecido al azul y rojo de los edificios.
const TEAM_COLORS := {
	1: Color(0.18, 0.42, 0.9),
	2: Color(0.88, 0.2, 0.18),
}
const BANNER_BASE_HEIGHT := 0.45
const BANNER_POLE_HEIGHT := 0.75
const BANNER_OFFSET_X := 0.45
const BANNER_FLAG_WIDTH := 0.42
const BANNER_FLAG_HEIGHT := 0.26
## Modelo de cada tipo de unidad según el jugador, como los edificios (azul P1, rojo P2):
## P1 juega con trabajadores y caballeros, P2 con male-b y robots. Así los dos bandos se
## distinguen de un vistazo y cada jugador ve el mismo mundo que el rival.
const UNIT_VISUAL_PATHS := {
	"Worker": {
		1: "res://assets/units/mini_characters/character-worker.glb",
		2: "res://assets/units/mini_characters/character-male-b.glb",
	},
	"Soldier": {
		1: "res://assets/units/mini_characters/character-knight.glb",
		2: "res://assets/units/mini_characters/character-robot.glb",
	},
}

## Escena de cada tipo de edificio (scenes/buildings). Cada escena trae sus modelos azules en
## el nodo "Colored" (P2 los cambia por los rojos, ver _apply_team_models), el andamio en
## "Scaffolding" (visible solo en obra) y, la granja, su cultivo en "Grain". La valla no tiene
## "Colored": su color va en un banderín. El Centro Principal no tiene andamio: nunca se construye.
const BUILDING_VISUAL_PATHS := {
	"MainCenter": "res://scenes/buildings/main_center.tscn",
	"Farm": "res://scenes/buildings/farm.tscn",
	"Barracks": "res://scenes/buildings/barracks.tscn",
	"Tower": "res://scenes/buildings/tower.tscn",
	"Fence": "res://scenes/buildings/fence.tscn",
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
var initial_state_tick := -1
var last_state_events_tick := -1
var last_position_update_tick := -1
var map_revision := 0
var navigation_required_revision := 0
var destroyed_entity_ids: Dictionary = {}
var recent_command_results: Dictionary = {}
var synchronization_failed := false

var _session_controller: Node
var _network_client: Node
var _position_samples: Dictionary = {}
var _position_clock_tick := -1
var _position_clock_received_usec := 0
var _recent_command_result_ids: Array[int] = []
var next_client_message_id := 1
var pending_commands: Dictionary = {}
var pending_repath_responses: Dictionary = {}
var pending_command_by_unit: Dictionary = {}
var accepted_order_intents: Dictionary = {}
var _units_calculating_command: Dictionary = {}
var _batch_unit_order_changes: Array[Dictionary] = []
var _batch_placed_buildings: Array[Dictionary] = []
## Ruinas visibles: building_id → {"node": Node3D, "rect": Rect2i}.
var _ruins: Dictionary = {}


func _ready() -> void:
	set_process(true)
	var map_revision_callable := Callable(self, "_on_map_revision_changed")
	if not is_connected("map_revision_changed", map_revision_callable):
		connect("map_revision_changed", map_revision_callable)
	var repath_callable := Callable(self, "_on_repath_requested")
	if not is_connected("repath_requested", repath_callable):
		connect("repath_requested", repath_callable)
	_session_controller = get_node_or_null("/root/SessionController")
	if _session_controller != null:
		var callbacks := {
			"initial_state_preparation_requested": Callable(self, "_on_initial_state_preparation_requested"),
			"initial_state_discard_requested": Callable(self, "_on_initial_state_discard_requested"),
			"session_state_changed": Callable(self, "_on_session_state_changed"),
			"session_reset": Callable(self, "_on_session_reset"),
		}
		for signal_name in callbacks:
			if not _session_controller.has_signal(signal_name):
				if signal_name == "session_state_changed" or signal_name == "session_reset":
					continue
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

	_network_client = get_node_or_null("/root/NetworkClient")
	if _network_client != null:
		if not _network_client.has_signal("message_received"):
			_report_error("InvalidNetworkClient", "NetworkClient no expone message_received.")
			_network_client = null
			return
		var message_callable := Callable(self, "_on_network_message_received")
		if not _network_client.is_connected("message_received", message_callable):
			var connect_error: Error = _network_client.connect("message_received", message_callable)
			if connect_error != OK:
				_report_error("NetworkSignalConnectionFailed", "No se pudo enlazar message_received.")
				_network_client = null
				return


func _exit_tree() -> void:
	_invalidate_world_navigation(active_world)
	if is_instance_valid(_session_controller):
		var callbacks := {
			"initial_state_preparation_requested": Callable(self, "_on_initial_state_preparation_requested"),
			"initial_state_discard_requested": Callable(self, "_on_initial_state_discard_requested"),
			"session_state_changed": Callable(self, "_on_session_state_changed"),
			"session_reset": Callable(self, "_on_session_reset"),
		}
		for signal_name in callbacks:
			if not _session_controller.has_signal(signal_name):
				continue
			var callback: Callable = callbacks[signal_name]
			if _session_controller.is_connected(signal_name, callback):
				_session_controller.disconnect(signal_name, callback)
	var map_revision_callable := Callable(self, "_on_map_revision_changed")
	if is_connected("map_revision_changed", map_revision_callable):
		disconnect("map_revision_changed", map_revision_callable)
	var repath_callable := Callable(self, "_on_repath_requested")
	if is_connected("repath_requested", repath_callable):
		disconnect("repath_requested", repath_callable)
	if is_instance_valid(_network_client):
		var message_callable := Callable(self, "_on_network_message_received")
		if _network_client.is_connected("message_received", message_callable):
			_network_client.disconnect("message_received", message_callable)


func _process(_delta: float) -> void:
	if active_world != null and not synchronization_failed:
		_update_unit_interpolation(Time.get_ticks_usec())


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
	_reset_synchronization_cursors()

	if previous_world != null and is_instance_valid(previous_world):
		_invalidate_world_navigation(previous_world)
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
		_invalidate_world_navigation(active_world)
		remove_child(active_world)
		active_world.queue_free()
	active_world = null
	active_snapshot.clear()
	active_catalog.clear()
	entities_by_id.clear()
	active_initialization_id = 0
	_clear_synchronization_state()
	world_discarded.emit(initialization_id)


func calculate_move_routes(destination: Vector2, unit_ids: Array[int] = []) -> Dictionary:
	if active_world == null:
		return {"ok": false, "error": "WorldNotReady", "detail": "No hay un mundo activo para calcular rutas."}
	var world_instance := active_world
	var target_ids := _distinct_sorted_array(unit_ids)
	if target_ids.is_empty():
		return {"ok": false, "error": "NoUnitsSelected", "detail": "No hay unidades elegidas para mover."}
	var navigation: Node = _get_active_navigation()
	if navigation == null:
		return {"ok": false, "error": "NavigationUnavailable", "detail": "El mundo no contiene ClientNavigation."}
	var route_revision := -1
	var routes: Array[Dictionary] = []
	for entity_id: int in target_ids:
		var entity: Node3D = entities_by_id.get(entity_id)
		if entity == null or entity.get_meta("entity_kind", "") != "unit":
			continue
		var state: Dictionary = entity.get_meta("entity_state", {})
		if int(state.get("owner", 0)) != int(active_snapshot.get("playerId", 0)):
			continue
		var official_position: Variant = entity.get_meta("official_position", null)
		if typeof(official_position) != TYPE_VECTOR2:
			return {"ok": false, "error": "OfficialPositionUnavailable", "detail": "La unidad %d no tiene posición oficial." % entity_id}
		var origin: Vector2 = official_position
		var route_result: Dictionary = await _calculate_candidate_route_stable(
			navigation,
			origin,
			destination,
			map_revision
		)
		if active_world != world_instance:
			return {"ok": false, "error": "WorldChanged", "detail": "El mundo cambió durante el cálculo de rutas."}
		if entities_by_id.get(entity_id) != entity:
			return {"ok": false, "error": "UnitMissing", "detail": "La unidad %d desapareció durante el cálculo." % entity_id}
		var latest_position: Variant = entity.get_meta("official_position", null)
		if typeof(latest_position) != TYPE_VECTOR2:
			return {"ok": false, "error": "OfficialPositionUnavailable", "detail": "La unidad %d perdió su posición oficial." % entity_id}
		var latest_position_vector: Vector2 = latest_position
		if not latest_position_vector.is_equal_approx(origin):
			return {"ok": false, "error": "OfficialPositionChanged", "detail": "La posición oficial de la unidad cambió durante el cálculo."}
		if not bool(route_result.get("ok", false)):
			return route_result
		var current_route_revision := int(route_result["revision"])
		if route_revision >= 0 and current_route_revision != route_revision:
			return {"ok": false, "error": "MapRevisionChanged", "detail": "El mapa cambió mientras se calculaban las rutas del grupo."}
		route_revision = current_route_revision
		routes.append({
				"unitId": entity_id,
				"waypoints": route_result["waypoints"],
			})
	if routes.is_empty():
		return {"ok": false, "error": "NoUnitsReady", "detail": "Ninguna unidad elegida está lista para mover."}
	var payload := {
		"destination": {"x": float(destination.x), "z": float(destination.y)},
		"routeMapRevision": route_revision,
		"routes": routes,
	}
	return {"ok": true, "payload": payload}


func build_move_payload(destination: Vector2, unit_ids: Array[int] = []) -> Dictionary:
	return await calculate_move_routes(destination, unit_ids)


func generate_move_command(destination: Vector2, unit_ids: Array[int] = []) -> Dictionary:
	return await issue_move_command(destination, unit_ids)


func create_move_command(destination: Vector2, unit_ids: Array[int] = []) -> Dictionary:
	return await issue_move_command(destination, unit_ids)


func issue_move_command(destination: Vector2, unit_ids: Array[int] = []) -> Dictionary:
	return await _issue_routed_command(
		"Move",
		unit_ids,
		func(ids: Array[int]) -> Dictionary: return await calculate_move_routes(destination, ids)
	)


## GatherWood: solo trabajadores propios; el resto de la selección se ignora (plan 06 §2).
func issue_gather_wood_command(tree_id: int, unit_ids: Array[int] = []) -> Dictionary:
	return await _issue_task_command("GatherWood", tree_id, unit_ids)


## Attack: solo soldados propios contra una unidad o un edificio rival.
func issue_attack_command(target_id: int, unit_ids: Array[int] = []) -> Dictionary:
	return await _issue_task_command("Attack", target_id, unit_ids)


## Build en su forma "continuar" (D-032): sumarse a una obra propia sin costo.
func issue_build_continue_command(building_id: int, unit_ids: Array[int] = []) -> Dictionary:
	return await _issue_task_command("Build", building_id, unit_ids)


## RepairFarm: trabajadores propios sobre una granja propia, completada y agotada.
func issue_repair_farm_command(farm_id: int, unit_ids: Array[int] = []) -> Dictionary:
	return await _issue_task_command("RepairFarm", farm_id, unit_ids)


## Stop: deja Idle a las unidades propias. No lleva rutas ni bloquea por comandos pendientes,
## para que el jugador pueda frenar una orden recién dada.
func issue_stop_command(unit_ids: Array[int] = []) -> Dictionary:
	var precondition := _gameplay_precondition()
	if not precondition.is_empty():
		return precondition
	var stop_ids: Array[int] = []
	for unit_id: int in _distinct_sorted_array(unit_ids):
		if not _owned_unit_type(unit_id).is_empty():
			stop_ids.append(unit_id)
	if stop_ids.is_empty():
		return {"ok": false, "error": "NoUnitsSelected", "detail": "No hay unidades propias para detener."}
	var payload := {"unitIds": stop_ids}
	return _send_tracked_command("Stop", payload, [])


## TrainUnit (contrato 05 §3): agrega una solicitud a la cola del productor. No exige comida:
## sin comida la solicitud espera. La cola llena (`Production.QueueFull`) solo la puede
## informar el servidor, porque `MaxPendingPerProducer` no viaja en el catálogo.
func issue_train_unit_command(producer_id: int, unit_type: String) -> Dictionary:
	var precondition := _gameplay_precondition()
	if not precondition.is_empty():
		return precondition
	var trainable := trainable_unit_type(producer_id)
	if not bool(trainable.get("ok", false)):
		return trainable
	if str(trainable["unitType"]) != unit_type:
		return {"ok": false, "error": "WrongUnitType", "detail": "Ese edificio no entrena %s." % unit_type}
	return _send_tracked_command("TrainUnit", {"producerId": producer_id, "unitType": unit_type}, [])


## Tipo de unidad que entrena un edificio propio, o el error por el que no puede entrenar.
func trainable_unit_type(producer_id: int) -> Dictionary:
	var producer: Node3D = entities_by_id.get(producer_id)
	if producer == null or producer.get_meta("entity_kind", "") != "building":
		return {"ok": false, "error": "EntityNotFound", "detail": "El productor %d no existe." % producer_id}
	var state: Dictionary = producer.get_meta("entity_state", {})
	if int(state.get("owner", 0)) != int(active_snapshot.get("playerId", 0)):
		return {"ok": false, "error": "NotOwner", "detail": "El edificio %d no es propio." % producer_id}
	var unit_type: String = PRODUCER_UNIT_TYPES.get(str(state.get("buildingType", "")), "")
	if unit_type.is_empty():
		return {"ok": false, "error": "NotAProducer", "detail": "El edificio %d no entrena unidades." % producer_id}
	if not bool(state.get("isCompleted", false)):
		return {"ok": false, "error": "ProducerNotOperational", "detail": "El edificio %d todavía está en obra." % producer_id}
	return {"ok": true, "unitType": unit_type}


## Estado de producción propio de un productor (InitialState.ownProduction y ProductionChanged).
func get_own_production(producer_id: int) -> Dictionary:
	for producer: Variant in active_snapshot.get("ownProduction", []):
		if producer is Dictionary and int(producer.get("producerId", 0)) == producer_id:
			return producer.duplicate(true)
	return {}


func get_own_resources() -> Dictionary:
	return active_snapshot.get("ownResources", {}).duplicate(true)


## Verificación local de una colocación, en el orden de EvaluateSite (contrato 03 §3) y
## después la madera. Sirve para la vista previa: el resultado oficial lo decide el servidor.
func validate_placement(building_type: String, anchor_column: int, anchor_row: int) -> Dictionary:
	if not BUILDABLE_TYPES.has(building_type):
		return {"ok": false, "error": "NotBuildable", "detail": "%s no se puede construir." % building_type}
	var footprint := placement_footprint(building_type, anchor_column, anchor_row)
	if footprint.size == Vector2i.ZERO:
		return {"ok": false, "error": "NotBuildable", "detail": "El catálogo no define la huella de %s." % building_type}
	var map_data: Dictionary = active_snapshot.get("map", {})
	if (
		footprint.position.x < 0
		or footprint.position.y < 0
		or footprint.end.x > int(map_data.get("columns", 0))
		or footprint.end.y > int(map_data.get("rows", 0))
	):
		return {"ok": false, "error": "OutOfBounds", "detail": "La huella sale del mapa."}
	for building: Variant in active_snapshot.get("buildings", []):
		var other: Dictionary = building.get("footprint", {})
		var other_rect := Rect2i(int(other.get("column", 0)), int(other.get("row", 0)), int(other.get("width", 0)), int(other.get("height", 0)))
		if footprint.intersects(other_rect):
			return {"ok": false, "error": "CellOccupied", "detail": "La huella pisa otro edificio."}
	for tree: Variant in active_snapshot.get("trees", []):
		if footprint.has_point(Vector2i(int(tree.get("column", -1)), int(tree.get("row", -1)))):
			return {"ok": false, "error": "TreeReserved", "detail": "La huella pisa la celda de un árbol."}
	var cell_size := float(map_data.get("cellSize", 1.0))
	for entity_id: Variant in entities_by_id:
		var node: Node3D = entities_by_id[entity_id]
		if node.get_meta("entity_kind", "") != "unit":
			continue
		var position_value: Variant = node.get_meta("official_position", null)
		if typeof(position_value) != TYPE_VECTOR2:
			continue
		var unit_position: Vector2 = position_value
		var cell := Vector2i(floori(unit_position.x / cell_size), floori(unit_position.y / cell_size))
		if footprint.has_point(cell):
			return {"ok": false, "error": "UnitInFootprint", "detail": "Hay una unidad en la huella."}
	var wood_cost := int(_catalog_by_type("buildings", "buildingType").get(building_type, {}).get("cost", {}).get("wood", 0))
	if int(active_snapshot.get("ownResources", {}).get("wood", 0)) < wood_cost:
		return {"ok": false, "error": "InsufficientWood", "detail": "No alcanza la madera."}
	return {"ok": true, "footprint": footprint, "woodCost": wood_cost}


## Huella en celdas con ancla en la celda de columna y fila mínimas (D-006), sin rotación.
func placement_footprint(building_type: String, anchor_column: int, anchor_row: int) -> Rect2i:
	var entry: Dictionary = _catalog_by_type("buildings", "buildingType").get(building_type, {})
	return Rect2i(
		anchor_column,
		anchor_row,
		int(entry.get("footprintWidth", 0)),
		int(entry.get("footprintHeight", 0))
	)


## Build en su forma "colocar" (D-032): tipo + celda ancla, con una ruta por trabajador hasta el
## rango de la huella. `allow_pending` deja mandar varias vallas seguidas con los mismos
## trabajadores (D-033): cada nueva orden válida reemplaza a la anterior en el servidor.
func issue_build_place_command(
	building_type: String,
	anchor_column: int,
	anchor_row: int,
	unit_ids: Array[int] = [],
	allow_pending := false
) -> Dictionary:
	var precondition := _gameplay_precondition()
	if not precondition.is_empty():
		return precondition
	var placement := validate_placement(building_type, anchor_column, anchor_row)
	if not bool(placement.get("ok", false)):
		return placement
	return await _issue_routed_command(
		"Build",
		unit_ids,
		func(ids: Array[int]) -> Dictionary:
			return await calculate_build_place_routes(building_type, anchor_column, anchor_row, ids),
		allow_pending
	)


func calculate_build_place_routes(
	building_type: String,
	anchor_column: int,
	anchor_row: int,
	unit_ids: Array[int]
) -> Dictionary:
	if active_world == null:
		return {"ok": false, "error": "WorldNotReady", "detail": "No hay un mundo activo para calcular rutas."}
	var world_instance := active_world
	var compatible_ids := _compatible_unit_ids("Build", unit_ids)
	if compatible_ids.is_empty():
		return {"ok": false, "error": "NoCompatibleUnits", "detail": "Solo los trabajadores pueden construir."}
	var footprint := placement_footprint(building_type, anchor_column, anchor_row)
	var cell_size := float(active_snapshot.get("map", {}).get("cellSize", 1.0))
	var target := {"rect": Rect2(Vector2(footprint.position) * cell_size, Vector2(footprint.size) * cell_size)}
	# La ruta termina al entrar en el rango de la huella, así que no la atraviesa aunque la
	# navegación local todavía no la tenga como obstáculo (el servidor valida contra el mapa
	# con la huella bloqueada, 03 §3).
	var routes_result: Dictionary = await _calculate_routes_to_target(
		compatible_ids,
		target,
		_interaction_range("Build"),
		0
	)
	if active_world != world_instance:
		return {"ok": false, "error": "WorldChanged", "detail": "El mundo cambió durante el cálculo de rutas."}
	if not bool(routes_result.get("ok", false)):
		return routes_result
	return {
		"ok": true,
		"payload": {
			"buildingType": building_type,
			"anchorColumn": anchor_column,
			"anchorRow": anchor_row,
			"routeMapRevision": routes_result["revision"],
			"routes": routes_result["routes"],
		},
	}


## Arma el payload de una orden de tarea: objetivo, revisión de mapa y una ruta por unidad
## que termina dentro del rango de interacción (contrato 04 §3).
func calculate_task_routes(command_type: String, target_id: int, unit_ids: Array[int] = []) -> Dictionary:
	if active_world == null:
		return {"ok": false, "error": "WorldNotReady", "detail": "No hay un mundo activo para calcular rutas."}
	var world_instance := active_world
	var target_check := _validate_task_target(command_type, target_id)
	if not bool(target_check.get("ok", false)):
		return target_check
	var interaction_range := _interaction_range(command_type)
	if interaction_range <= 0.0:
		return {"ok": false, "error": "RangeUnavailable", "detail": "El catálogo no define el rango de %s." % command_type}
	var compatible_ids := _compatible_unit_ids(command_type, unit_ids)
	if compatible_ids.is_empty():
		return {"ok": false, "error": "NoCompatibleUnits", "detail": "Ninguna unidad elegida puede ejecutar %s." % command_type}
	var routes_result: Dictionary = await _calculate_routes_to_target(
		compatible_ids,
		target_check["geometry"],
		interaction_range,
		target_id
	)
	if active_world != world_instance:
		return {"ok": false, "error": "WorldChanged", "detail": "El mundo cambió durante el cálculo de rutas."}
	if not bool(routes_result.get("ok", false)):
		return routes_result
	var payload := {
		TASK_TARGET_KEYS[command_type]: target_id,
		"routeMapRevision": routes_result["revision"],
		"routes": routes_result["routes"],
	}
	return {"ok": true, "payload": payload}


## Una ruta por unidad hasta quedar en rango del objetivo (punto o huella). Si `target_id`
## es 0, el objetivo es una huella hipotética (Build colocar) y no una entidad existente.
func _calculate_routes_to_target(
	unit_ids: Array[int],
	target_geometry: Dictionary,
	interaction_range: float,
	target_id: int
) -> Dictionary:
	var world_instance := active_world
	var navigation: Node = _get_active_navigation()
	if navigation == null:
		return {"ok": false, "error": "NavigationUnavailable", "detail": "El mundo no contiene ClientNavigation."}
	var route_revision := -1
	var routes: Array[Dictionary] = []
	for unit_id: int in unit_ids:
		var unit: Node3D = entities_by_id.get(unit_id)
		var official_position: Variant = unit.get_meta("official_position", null)
		if typeof(official_position) != TYPE_VECTOR2:
			return {"ok": false, "error": "OfficialPositionUnavailable", "detail": "La unidad %d no tiene posición oficial." % unit_id}
		var origin: Vector2 = official_position
		var route_result: Dictionary = await _calculate_candidate_route_stable(
			navigation,
			origin,
			Vector2.ZERO,
			map_revision,
			target_geometry,
			interaction_range
		)
		if active_world != world_instance:
			return {"ok": false, "error": "WorldChanged", "detail": "El mundo cambió durante el cálculo de rutas."}
		if entities_by_id.get(unit_id) != unit:
			return {"ok": false, "error": "UnitMissing", "detail": "La unidad %d desapareció durante el cálculo." % unit_id}
		if target_id > 0 and not entities_by_id.has(target_id):
			return {"ok": false, "error": "TargetMissing", "detail": "El objetivo %d desapareció durante el cálculo." % target_id}
		var latest_position: Variant = unit.get_meta("official_position", null)
		if typeof(latest_position) != TYPE_VECTOR2 or not origin.is_equal_approx(latest_position):
			return {"ok": false, "error": "OfficialPositionChanged", "detail": "La posición oficial de la unidad cambió durante el cálculo."}
		if not bool(route_result.get("ok", false)):
			return route_result
		var current_route_revision := int(route_result["revision"])
		if route_revision >= 0 and current_route_revision != route_revision:
			return {"ok": false, "error": "MapRevisionChanged", "detail": "El mapa cambió mientras se calculaban las rutas del grupo."}
		route_revision = current_route_revision
		routes.append({"unitId": unit_id, "waypoints": route_result["waypoints"]})
	return {"ok": true, "routes": routes, "revision": route_revision}


func _issue_task_command(command_type: String, target_id: int, unit_ids: Array[int]) -> Dictionary:
	var precondition := _gameplay_precondition()
	if not precondition.is_empty():
		return precondition
	# Un objetivo inválido se informa antes que el bloqueo por comandos pendientes.
	var target_check := _validate_task_target(command_type, target_id)
	if not bool(target_check.get("ok", false)):
		return target_check
	return await _issue_routed_command(
		command_type,
		unit_ids,
		func(ids: Array[int]) -> Dictionary: return await calculate_task_routes(command_type, target_id, ids)
	)


## Flujo común de los comandos con rutas: bloqueo por unidad mientras hay un comando
## sin resultado, cálculo de rutas, revalidación tras el await y envío con messageId nuevo.
func _issue_routed_command(
	command_type: String,
	unit_ids: Array[int],
	route_calculator: Callable,
	allow_pending := false
) -> Dictionary:
	var precondition := _gameplay_precondition()
	if not precondition.is_empty():
		return precondition
	var target_ids := _distinct_sorted_array(unit_ids)
	if target_ids.is_empty():
		return {"ok": false, "error": "NoUnitsSelected", "detail": "No hay unidades elegidas para %s." % command_type}
	var request_initialization_id := active_initialization_id
	for unit_id: int in target_ids:
		if _units_calculating_command.has(unit_id) or (not allow_pending and pending_command_by_unit.has(unit_id)):
			return {"ok": false, "error": "CommandAlreadyPending", "detail": "La unidad %d ya tiene un comando pendiente." % unit_id}
	for unit_id: int in target_ids:
		_units_calculating_command[unit_id] = true
	var route_result: Dictionary = await route_calculator.call(target_ids)
	for unit_id: int in target_ids:
		_units_calculating_command.erase(unit_id)
	if not route_result["ok"]:
		return route_result
	if request_initialization_id != active_initialization_id:
		return {"ok": false, "error": "WorldChanged", "detail": "El mundo cambió durante el cálculo de rutas."}
	precondition = _gameplay_precondition()
	if not precondition.is_empty():
		return precondition
	var payload: Dictionary = route_result["payload"]
	var routed_unit_ids: Array[int] = []
	for route: Dictionary in payload["routes"]:
		routed_unit_ids.append(int(route["unitId"]))
	for unit_id: int in routed_unit_ids:
		if not allow_pending and pending_command_by_unit.has(unit_id):
			return {"ok": false, "error": "CommandAlreadyPending", "detail": "La unidad %d recibió otro comando durante el cálculo." % unit_id}
	return _send_tracked_command(command_type, payload, routed_unit_ids)


func _send_tracked_command(command_type: String, payload: Dictionary, locked_unit_ids: Array[int]) -> Dictionary:
	var message_id := _allocate_client_message_id()
	if message_id < 0:
		return {"ok": false, "error": "MessageIdExhausted", "detail": "Se agotó el rango de messageId permitido."}
	if not bool(_network_client.call("send_message", command_type, payload, message_id)):
		return {"ok": false, "error": "SendFailed", "detail": "No se pudo enviar el comando %s." % command_type}
	var unit_ids: Array[int] = locked_unit_ids.duplicate()
	if command_type == "Stop":
		for unit_id_value: Variant in payload["unitIds"]:
			unit_ids.append(int(unit_id_value))
	pending_commands[message_id] = {
		"type": command_type,
		"status": "awaiting_result",
		"payload": payload.duplicate(true),
		"unitIds": unit_ids,
	}
	for unit_id: int in locked_unit_ids:
		pending_command_by_unit[unit_id] = message_id
	return {"ok": true, "messageId": message_id, "payload": payload}


func _gameplay_precondition() -> Dictionary:
	if _session_controller == null or not _session_controller.can_issue_gameplay():
		return {"ok": false, "error": "MatchNotRunning", "detail": "La sesión no está en Running."}
	if _network_client == null or not _network_client.has_method("send_message"):
		return {"ok": false, "error": "NetworkUnavailable", "detail": "No hay un NetworkClient disponible."}
	return {}


## Tipo de una unidad propia y viva, o "" si no lo es.
func _owned_unit_type(unit_id: int) -> String:
	var unit: Node3D = entities_by_id.get(unit_id)
	if unit == null or unit.get_meta("entity_kind", "") != "unit":
		return ""
	var state: Dictionary = unit.get_meta("entity_state", {})
	if int(state.get("owner", 0)) != int(active_snapshot.get("playerId", 0)):
		return ""
	return str(state.get("unitType", ""))


## Unidades propias de la selección que pueden ejecutar la orden (04 §2).
func _compatible_unit_ids(command_type: String, unit_ids: Array[int]) -> Array[int]:
	var required_type: String = TASK_UNIT_TYPES.get(command_type, "")
	var result: Array[int] = []
	for unit_id: int in _distinct_sorted_array(unit_ids):
		var unit_type := _owned_unit_type(unit_id)
		if not unit_type.is_empty() and (required_type.is_empty() or unit_type == required_type):
			result.append(unit_id)
	return result


## Rango de interacción de una orden de tarea, tomado del catálogo (D-068):
## GatherWood → gatherRange; Build y RepairFarm → buildRange; Attack → rango del soldado.
func _interaction_range(command_type: String) -> float:
	match command_type:
		"GatherWood":
			return float(active_catalog.get("gatherRange", 0.0))
		"Build", "RepairFarm":
			return float(active_catalog.get("buildRange", 0.0))
		"Attack":
			var soldier: Dictionary = _catalog_by_type("units", "unitType").get("Soldier", {})
			return float(soldier.get("attack", {}).get("range", 0.0))
	return 0.0


## Verifica que el objetivo exista y corresponda a la orden, y devuelve su geometría:
## punto para unidades y árboles, huella para edificios (D-019, D-050).
func _validate_task_target(command_type: String, target_id: int) -> Dictionary:
	var target: Node3D = entities_by_id.get(target_id)
	if target == null:
		return {"ok": false, "error": "TargetMissing", "detail": "El objetivo %d no existe en el mundo local." % target_id}
	var kind := str(target.get_meta("entity_kind", ""))
	var state: Dictionary = target.get_meta("entity_state", {})
	var local_player_id := int(active_snapshot.get("playerId", 0))
	var owner := int(state.get("owner", 0))
	match command_type:
		"GatherWood":
			if kind != "tree":
				return {"ok": false, "error": "InvalidTarget", "detail": "GatherWood requiere un árbol."}
			if not bool(state.get("isAvailable", false)):
				return {"ok": false, "error": "TreeDepleted", "detail": "El árbol %d está agotado." % target_id}
		"Attack":
			if kind != "unit" and kind != "building":
				return {"ok": false, "error": "InvalidTarget", "detail": "Attack requiere una unidad o un edificio."}
			if owner == local_player_id:
				return {"ok": false, "error": "FriendlyTarget", "detail": "No se puede atacar una entidad propia."}
		"Build":
			if kind != "building" or owner != local_player_id:
				return {"ok": false, "error": "InvalidTarget", "detail": "Build continuar requiere una obra propia."}
			if bool(state.get("isCompleted", false)):
				return {"ok": false, "error": "AlreadyCompleted", "detail": "El edificio %d ya está terminado." % target_id}
		"RepairFarm":
			if (
				kind != "building"
				or owner != local_player_id
				or str(state.get("buildingType", "")) != "Farm"
				or not bool(state.get("isCompleted", false))
			):
				return {"ok": false, "error": "NotAFarm", "detail": "RepairFarm requiere una granja propia completada."}
			if not bool(state.get("farm", {}).get("isDepleted", false)):
				return {"ok": false, "error": "FarmNotDepleted", "detail": "La granja %d no está agotada." % target_id}
		_:
			return {"ok": false, "error": "UnknownCommand", "detail": "%s no es una orden de tarea." % command_type}
	var geometry := _target_geometry(target_id)
	if geometry.is_empty():
		return {"ok": false, "error": "TargetGeometryUnavailable", "detail": "No se pudo ubicar al objetivo %d." % target_id}
	return {"ok": true, "geometry": geometry}


func _target_geometry(target_id: int) -> Dictionary:
	var target: Node3D = entities_by_id.get(target_id)
	if target == null:
		return {}
	var state: Dictionary = target.get_meta("entity_state", {})
	match str(target.get_meta("entity_kind", "")):
		"unit":
			var official_position: Variant = target.get_meta("official_position", null)
			if typeof(official_position) == TYPE_VECTOR2:
				return {"point": official_position}
			return {"point": Vector2(float(state.get("x", 0.0)), float(state.get("z", 0.0)))}
		"tree":
			return {"point": Vector2(float(state.get("x", 0.0)), float(state.get("z", 0.0)))}
		"building":
			var footprint: Dictionary = state.get("footprint", {})
			var cell_size := float(active_snapshot.get("map", {}).get("cellSize", 0.0))
			if footprint.is_empty() or cell_size <= 0.0:
				return {}
			return {
				"rect": Rect2(
					float(footprint["column"]) * cell_size,
					float(footprint["row"]) * cell_size,
					float(footprint["width"]) * cell_size,
					float(footprint["height"]) * cell_size
				),
			}
	return {}


func build_repath_response_payload(request: Dictionary) -> Dictionary:
	if (
		not request.has("unitId")
		or not request.has("orderId")
		or not _is_positive_integer(request["unitId"])
		or not _is_positive_integer(request["orderId"])
		or not request.has("mapRevision")
		or not _is_non_negative_integer(request["mapRevision"])
	):
		return {"ok": false, "error": "InvalidRequest", "detail": "RepathRequest requiere unitId y orderId."}
	var unit_id: int = int(request["unitId"])
	var order_id: int = int(request["orderId"])
	var has_target := request.has("targetId") and request["targetId"] != null
	if has_target and not _is_positive_integer(request["targetId"]):
		return {"ok": false, "error": "InvalidRequest", "detail": "RepathRequest.targetId debe ser un ID positivo."}
	if not has_target and (not request.has("destination") or typeof(request["destination"]) != TYPE_DICTIONARY):
		return {"ok": false, "error": "InvalidRequest", "detail": "RepathRequest debe contener destination para una orden Move."}
	if not entities_by_id.has(unit_id):
		return {"ok": false, "error": "UnitMissing", "detail": "La unidad no existe en el mundo local."}
	var unit: Node3D = entities_by_id[unit_id]
	var unit_state: Dictionary = unit.get_meta("entity_state", {})
	var intent: Dictionary = accepted_order_intents.get(unit_id, {})
	if (
		unit.get_meta("entity_kind", "") != "unit"
		or int(unit_state.get("owner", 0)) != int(active_snapshot.get("playerId", 0))
		or intent.is_empty()
		or str(unit_state.get("order", "")) != str(intent.get("kind", ""))
		or int(unit_state.get("orderId", 0)) != order_id
		or int(intent.get("orderId", 0)) != order_id
		or has_target == (str(intent.get("kind", "")) == "Move")
	):
		return {"ok": false, "error": "StaleOrder", "detail": "RepathRequest ya no corresponde a la orden vigente."}
	var destination := Vector2.ZERO
	var range_target: Dictionary = {}
	var interaction_range := 0.0
	if has_target:
		# Repath de una orden de tarea (04 §5): ruta nueva hasta el rango del objetivo
		# en su posición o huella actual, con el mismo OrderId.
		var target_id := int(request["targetId"])
		if int(intent.get("targetId", 0)) != target_id:
			return {"ok": false, "error": "StaleTarget", "detail": "RepathRequest no coincide con el objetivo de la orden aceptada."}
		range_target = _target_geometry(target_id)
		if range_target.is_empty():
			return {"ok": false, "error": "TargetMissing", "detail": "El objetivo %d no existe en el mundo local." % target_id}
		interaction_range = _interaction_range(str(intent["kind"]))
		if interaction_range <= 0.0:
			return {"ok": false, "error": "RangeUnavailable", "detail": "El catálogo no define el rango de la orden."}
	else:
		var destination_data: Dictionary = request["destination"]
		if (
			not destination_data.has("x")
			or not destination_data.has("z")
			or not _is_finite_number(destination_data["x"])
			or not _is_finite_number(destination_data["z"])
		):
			return {"ok": false, "error": "InvalidRequest", "detail": "destination requiere coordenadas X/Z finitas."}
		destination = Vector2(float(destination_data["x"]), float(destination_data["z"]))
		var intended_destination: Vector2 = intent["destination"]
		if not destination.is_equal_approx(intended_destination):
			return {"ok": false, "error": "StaleDestination", "detail": "RepathRequest no coincide con el destino de la orden aceptada."}
	var official_position: Variant = unit.get_meta("official_position", null)
	if typeof(official_position) != TYPE_VECTOR2:
		return {"ok": false, "error": "OfficialPositionUnavailable", "detail": "La unidad no tiene posición oficial."}
	var navigation: Node = _get_active_navigation()
	if navigation == null:
		return {"ok": false, "error": "NavigationUnavailable", "detail": "El mundo no contiene ClientNavigation."}
	var requested_revision := int(request["mapRevision"])
	var origin: Vector2 = official_position
	var request_initialization_id := active_initialization_id
	var route_result: Dictionary = await _calculate_candidate_route_stable(
		navigation,
		origin,
		destination,
		requested_revision,
		range_target,
		interaction_range
	)
	if request_initialization_id != active_initialization_id:
		return {"ok": false, "error": "WorldChanged", "detail": "El mundo cambió durante el cálculo del repath."}
	if not bool(route_result.get("ok", false)):
		return route_result
	var latest_position: Variant = unit.get_meta("official_position", null)
	if typeof(latest_position) != TYPE_VECTOR2:
		return {"ok": false, "error": "OfficialPositionUnavailable", "detail": "La unidad perdió su posición oficial durante el repath."}
	var latest_position_vector: Vector2 = latest_position
	if not latest_position_vector.is_equal_approx(origin):
		return {"ok": false, "error": "OfficialPositionChanged", "detail": "La posición oficial cambió durante el cálculo del repath."}
	var current_unit: Node3D = entities_by_id.get(unit_id)
	if (
		current_unit == null
		or int(current_unit.get_meta("entity_state", {}).get("orderId", 0)) != order_id
		or int(accepted_order_intents.get(unit_id, {}).get("orderId", 0)) != order_id
	):
		return {"ok": false, "error": "StaleOrder", "detail": "La orden cambió durante el cálculo del repath."}
	return {
		"ok": true,
		"payload": {
			"unitId": unit_id,
			"orderId": order_id,
			"mapRevision": int(route_result["revision"]),
			"waypoints": route_result["waypoints"],
		},
	}


func send_repath_response(request: Dictionary, message_id: int = -1) -> Dictionary:
	if _session_controller == null or not _session_controller.can_issue_gameplay():
		return {"ok": false, "error": "MatchNotRunning", "detail": "La sesión no está en Running."}
	if _network_client == null or not _network_client.has_method("send_message"):
		return {"ok": false, "error": "NetworkUnavailable", "detail": "No hay un NetworkClient disponible."}
	var payload_result: Dictionary = await build_repath_response_payload(request)
	if not payload_result.get("ok", false):
		return payload_result
	if _session_controller == null or not _session_controller.can_issue_gameplay():
		return {"ok": false, "error": "MatchNotRunning", "detail": "La sesión dejó de estar en Running durante el cálculo."}
	var effective_message_id := message_id if message_id > 0 else _allocate_client_message_id()
	if effective_message_id < 0 or effective_message_id > PROTOCOL_CODEC.MAX_SAFE_MESSAGE_ID:
		return {"ok": false, "error": "MessageIdExhausted", "detail": "Se agotó el rango de messageId permitido."}
	if message_id > 0:
		if message_id < next_client_message_id:
			return {"ok": false, "error": "MessageIdAlreadyUsed", "detail": "El messageId debe ser nuevo y creciente."}
		next_client_message_id = message_id + 1
	var payload: Dictionary = payload_result["payload"]
	if not _network_client.call("send_message", "RepathResponse", payload, effective_message_id):
		return {"ok": false, "error": "SendFailed", "detail": "No se pudo enviar RepathResponse."}
	pending_repath_responses[effective_message_id] = {
		"type": "RepathResponse",
		"status": "awaiting_result",
		"payload": payload.duplicate(true),
		"unitId": int(payload["unitId"]),
		"orderId": int(payload["orderId"]),
	}
	return {"ok": true, "messageId": effective_message_id, "payload": payload}


func retry_rejected_command(message_id: int) -> Dictionary:
	if _session_controller == null or not _session_controller.can_issue_gameplay():
		return {"ok": false, "error": "MatchNotRunning", "detail": "La sesión no está en Running."}
	var command: Dictionary = pending_commands.get(message_id, {})
	var commands := pending_commands
	if command.is_empty():
		command = pending_repath_responses.get(message_id, {})
		commands = pending_repath_responses
	if command.is_empty() or str(command.get("status", "")) != "rejected":
		return {"ok": false, "error": "CommandNotRetryable", "detail": "No hay un rechazo reintentable con ese messageId."}
	if _network_client == null or not _network_client.has_method("send_message"):
		return {"ok": false, "error": "NetworkUnavailable", "detail": "No hay un NetworkClient disponible."}
	if not _network_client.call("send_message", str(command["type"]), command["payload"], message_id):
		return {"ok": false, "error": "SendFailed", "detail": "No se pudo reenviar el comando original."}
	command["status"] = "awaiting_result"
	commands[message_id] = command
	return {"ok": true, "messageId": message_id, "payload": command["payload"]}


func discard_rejected_command(message_id: int) -> bool:
	var command: Dictionary = pending_commands.get(message_id, {})
	if not command.is_empty():
		if str(command.get("status", "")) != "rejected":
			return false
		_release_pending_command(message_id, command)
		pending_commands.erase(message_id)
		return true
	command = pending_repath_responses.get(message_id, {})
	if command.is_empty() or str(command.get("status", "")) != "rejected":
		return false
	pending_repath_responses.erase(message_id)
	return true


func _distinct_sorted_array(values: Array[int]) -> Array[int]:
	var seen: Dictionary = {}
	var result: Array[int] = []
	for value: Variant in values:
		if typeof(value) != TYPE_INT:
			continue
		var unit_id := int(value)
		if seen.has(unit_id):
			continue
		seen[unit_id] = true
		result.append(unit_id)
	result.sort()
	return result


func _get_active_navigation() -> Node:
	if active_world == null:
		return null
	return active_world.get_node_or_null("ClientNavigation")


func _invalidate_world_navigation(world: Node3D) -> void:
	if not is_instance_valid(world):
		return
	var navigation: Node = world.get_node_or_null("ClientNavigation")
	if navigation != null:
		navigation.call("invalidate")


func _calculate_candidate_route_stable(
	navigation: Node,
	origin: Vector2,
	destination: Vector2,
	minimum_revision: int,
	range_target: Dictionary = {},
	interaction_range := 0.0
) -> Dictionary:
	while true:
		if not is_instance_valid(navigation):
			return {"ok": false, "error": "NavigationUnavailable", "detail": "La navegación se cerró durante el cálculo."}
		if bool(navigation.call("is_invalidated")):
			return {"ok": false, "error": "NavigationUnavailable", "detail": "La navegación se cerró durante el cálculo."}
		var last_error := str(navigation.get("last_error"))
		if not last_error.is_empty():
			return {"ok": false, "error": "NavigationBakeFailed", "detail": last_error}
		while int(navigation.get("ready_revision")) < minimum_revision:
			if bool(navigation.call("is_invalidated")):
				return {"ok": false, "error": "NavigationUnavailable", "detail": "La navegación se cerró durante el cálculo."}
			last_error = str(navigation.get("last_error"))
			if not last_error.is_empty():
				return {"ok": false, "error": "NavigationBakeFailed", "detail": last_error}
			if int(navigation.get("required_revision")) < minimum_revision:
				return {"ok": false, "error": "NavigationRevisionUnavailable", "detail": "No se recibió la revisión de mapa solicitada."}
			await Signal(navigation, "navigation_state_changed")
			if not is_instance_valid(navigation):
				return {"ok": false, "error": "NavigationUnavailable", "detail": "La navegación se cerró durante el cálculo."}
		var revision := int(navigation.get("ready_revision"))
		if not bool(navigation.call("is_ready_for", revision)):
			await Signal(navigation, "navigation_state_changed")
			continue
		var result: Dictionary
		if range_target.is_empty():
			result = await navigation.call(
				"calculate_candidate_route",
				origin,
				destination,
				revision
			)
		else:
			result = await navigation.call(
				"calculate_candidate_route_to_range",
				origin,
				range_target,
				interaction_range,
				revision
			)
		if not is_instance_valid(navigation):
			return {"ok": false, "error": "NavigationUnavailable", "detail": "La navegación se cerró durante el cálculo."}
		if bool(navigation.call("is_invalidated")):
			return {"ok": false, "error": "NavigationUnavailable", "detail": "La navegación se cerró durante el cálculo."}
		if (
			int(navigation.get("ready_revision")) != revision
			or not bool(navigation.call("is_ready_for", revision))
		):
			continue
		if not bool(result.get("ok", false)):
			return result
		result["revision"] = revision
		return result
	return {"ok": false, "error": "NavigationQueryFailed", "detail": "No se pudo completar la consulta de navegación."}


func _allocate_client_message_id() -> int:
	if next_client_message_id < 1 or next_client_message_id > PROTOCOL_CODEC.MAX_SAFE_MESSAGE_ID:
		return -1
	var message_id := next_client_message_id
	next_client_message_id += 1
	return message_id


func _on_map_revision_changed(revision: int) -> void:
	navigation_required_revision = revision
	var navigation := _get_active_navigation()
	if navigation == null:
		_report_sync_warning("NavigationUnavailable", "El mundo activo no contiene ClientNavigation.")
		return
	navigation.call(
		"request_revision",
		active_snapshot.get("map", {}),
		active_snapshot.get("buildings", []),
		revision
	)


func _on_navigation_bake_failed(revision: int, detail: String) -> void:
	_report_sync_warning(
		"NavigationBakeFailed",
		"No se pudo preparar la navegación para la revisión %d: %s" % [revision, detail]
	)


func _on_repath_requested(request: Dictionary) -> void:
	var result: Dictionary = await send_repath_response(request)
	if not bool(result.get("ok", false)):
		_report_sync_warning(
			str(result.get("error", "RepathResponseFailed")),
			str(result.get("detail", "No se pudo responder a RepathRequested."))
		)
		repath_response_failed.emit(
			str(result.get("error", "RepathResponseFailed")),
			str(result.get("detail", "No se pudo responder a RepathRequested."))
		)


func _on_session_state_changed(_state: int, state_name: String) -> void:
	if ["AwaitingAssignment", "Ended", "Disconnected"].has(state_name):
		_clear_command_tracking()


func _on_session_reset() -> void:
	if active_initialization_id > 0:
		discard_initial_state(active_initialization_id)


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


func _on_network_message_received(
	message_type: String,
	payload: Variant,
	_envelope: Dictionary
) -> void:
	match message_type:
		"StateEvents":
			apply_state_events(payload)
		"PositionUpdate":
			apply_position_update(payload)
		"CommandRejected":
			_handle_command_rejected(payload)


func _handle_command_rejected(payload: Variant) -> void:
	if typeof(payload) != TYPE_DICTIONARY:
		_report_error("InvalidCommandRejected", "CommandRejected debe ser un objeto.")
		return
	var data: Dictionary = payload
	if (
		not data.has("messageId")
		or not _is_positive_integer(data["messageId"])
		or int(data["messageId"]) > PROTOCOL_CODEC.MAX_SAFE_MESSAGE_ID
		or not data.has("error")
		or not _is_non_empty_string(data["error"])
	):
		_report_error(
			"InvalidCommandRejected",
			"CommandRejected requiere messageId seguro y error no vacío."
		)
		return
	var message_id := int(data["messageId"])
	var error_code := str(data["error"])
	var command: Dictionary = pending_commands.get(message_id, {})
	if not command.is_empty():
		command["status"] = "rejected"
		command["error"] = error_code
		pending_commands[message_id] = command
	else:
		command = pending_repath_responses.get(message_id, {})
		if not command.is_empty():
			command["status"] = "rejected"
			command["error"] = error_code
			pending_repath_responses[message_id] = command
	command_rejected_received.emit(message_id, error_code)


func apply_state_events(payload: Variant) -> bool:
	if active_world == null:
		_report_sync_warning("StateEventsWithoutWorld", "Se recibió StateEvents sin un mundo activo.")
		return false
	if synchronization_failed:
		return false
	if typeof(payload) != TYPE_DICTIONARY:
		return _fail_synchronization("StateEvents debe ser un objeto.")

	var data: Dictionary = payload
	var missing_key := _first_missing_key(
		data,
		PackedStringArray(["tick", "mapRevision", "events", "commandResults"]),
		"StateEvents"
	)
	if not missing_key.is_empty():
		return _fail_synchronization(missing_key)
	if (
		not _is_non_negative_integer(data["tick"])
		or not _is_non_negative_integer(data["mapRevision"])
		or typeof(data["events"]) != TYPE_ARRAY
		or typeof(data["commandResults"]) != TYPE_ARRAY
	):
		return _fail_synchronization("StateEvents tiene tick, mapRevision o colecciones inválidos.")

	var tick := int(data["tick"])
	if tick <= last_state_events_tick:
		_report_sync_warning(
			"StaleStateEvents",
			"Se ignoró StateEvents del tick %d; el último tick aplicado es %d."
				% [tick, last_state_events_tick]
		)
		return false
	if tick <= last_position_update_tick:
		return _fail_synchronization(
			"StateEvents del tick %d llegó después de PositionUpdate del tick %d."
				% [tick, last_position_update_tick]
		)

	var simulated_entities := _make_simulated_entity_index()
	var simulated_destroyed := destroyed_entity_ids.duplicate()
	var simulated_revision := map_revision
	var events: Array = data["events"]
	var previous_index := -1
	for event_index in range(events.size()):
		var event: Variant = events[event_index]
		if typeof(event) != TYPE_DICTIONARY:
			return _fail_event_batch(tick, event_index, "El evento debe ser un objeto.")
		var event_data: Dictionary = event
		if not _is_non_negative_integer(event_data.get("index")):
			return _fail_event_batch(
				tick,
				event_index,
				"El índice del evento debe ser un entero no negativo.",
				event_data
			)
		var index := int(event_data["index"])
		if index <= previous_index:
			return _fail_event_batch(
				tick,
				index,
				"Los índices de eventos deben ser estrictamente crecientes.",
				event_data
			)
		previous_index = index

		var validation := _validate_state_event(
			event_data,
			simulated_entities,
			simulated_destroyed,
			simulated_revision
		)
		if not bool(validation["ok"]):
			return _fail_event_batch(tick, index, str(validation["detail"]), event_data)
		simulated_revision = int(validation["mapRevision"])

	if simulated_revision != int(data["mapRevision"]):
		return _fail_synchronization(
			"StateEvents del tick %d declara mapRevision %d, pero los eventos producen %d."
				% [tick, int(data["mapRevision"]), simulated_revision]
		)

	var command_results_error := _validate_command_results(data["commandResults"])
	if not command_results_error.is_empty():
		return _fail_synchronization(
			"StateEvents del tick %d contiene commandResults inválidos: %s"
				% [tick, command_results_error]
		)

	var notifications: Array[Dictionary] = []
	_batch_unit_order_changes.clear()
	_batch_placed_buildings.clear()
	for event_index in range(events.size()):
		var event_data: Dictionary = events[event_index]
		var result := _apply_state_event(event_data, tick)
		if not bool(result["ok"]):
			return _fail_event_batch(
				tick,
				int(event_data["index"]),
				str(result["detail"]),
				event_data
			)
		if result.has("notification"):
			notifications.append(result["notification"])

	last_state_events_tick = tick
	map_revision = simulated_revision
	navigation_required_revision = simulated_revision
	active_snapshot["map"]["revision"] = simulated_revision

	var repath_notifications: Array[Dictionary] = []
	for event_notification in notifications:
		match event_notification["type"]:
			"map_revision":
				map_revision_changed.emit(int(event_notification["revision"]))
			"resources":
				resources_changed.emit(event_notification["value"].duplicate(true))
			"production":
				production_changed.emit(
					int(event_notification["producerId"]),
					event_notification["value"].duplicate(true)
				)
			"repath":
				repath_notifications.append(event_notification["value"].duplicate(true))

	for command_result: Dictionary in data["commandResults"]:
		_remember_command_result(command_result)
	for request: Dictionary in repath_notifications:
		repath_requested.emit(request)

	return true


func apply_position_update(payload: Variant, received_usec: int = -1) -> bool:
	if active_world == null:
		_report_sync_warning("PositionUpdateWithoutWorld", "Se recibió PositionUpdate sin un mundo activo.")
		return false
	if synchronization_failed:
		return false
	if typeof(payload) != TYPE_DICTIONARY:
		return _fail_synchronization("PositionUpdate debe ser un objeto.")

	var data: Dictionary = payload
	if (
		not data.has("tick")
		or not data.has("units")
		or not _is_non_negative_integer(data["tick"])
		or typeof(data["units"]) != TYPE_ARRAY
	):
		return _fail_synchronization("PositionUpdate requiere tick entero y units array.")

	var tick := int(data["tick"])
	var positions: Array = data["units"]
	var position_ids: Dictionary = {}
	for index in range(positions.size()):
		var position_entry: Variant = positions[index]
		if typeof(position_entry) != TYPE_DICTIONARY:
			return _fail_synchronization("PositionUpdate.units[%d] debe ser un objeto." % index)
		var position_data: Dictionary = position_entry
		if (
			not position_data.has("id")
			or not position_data.has("x")
			or not position_data.has("z")
			or not _is_positive_integer(position_data["id"])
			or not _is_finite_number(position_data["x"])
			or not _is_finite_number(position_data["z"])
		):
			return _fail_synchronization(
				"PositionUpdate.units[%d] requiere id positivo y coordenadas finitas." % index
			)
		var entity_id := int(position_data["id"])
		if position_ids.has(entity_id):
			return _fail_synchronization(
				"PositionUpdate del tick %d repite el EntityId %d." % [tick, entity_id]
			)
		position_ids[entity_id] = true

	if tick <= last_position_update_tick:
		_report_sync_warning(
			"StalePositionUpdate",
			"Se ignoró PositionUpdate del tick %d; el último tick aplicado es %d."
				% [tick, last_position_update_tick]
		)
		return false

	if received_usec < 0:
		received_usec = Time.get_ticks_usec()
	for position_data: Dictionary in positions:
		var entity_id := int(position_data["id"])
		if destroyed_entity_ids.has(entity_id):
			continue
		if not entities_by_id.has(entity_id):
			continue
		var entity: Node3D = entities_by_id[entity_id]
		if entity.get_meta("entity_kind", "") != "unit":
			continue
		var x := float(position_data["x"])
		var z := float(position_data["z"])
		_add_position_sample(entity_id, tick, Vector2(x, z), received_usec)

	_position_clock_tick = tick
	_position_clock_received_usec = received_usec
	last_position_update_tick = tick
	return true


func _validate_state_event(
	event: Dictionary,
	simulated_entities: Dictionary,
	simulated_destroyed: Dictionary,
	simulated_revision: int
) -> Dictionary:
	if not _is_non_empty_string(event.get("type")):
		return _event_validation_failure("El evento requiere un type no vacío.", simulated_revision)
	var event_type: String = event["type"]
	var id_field := ""
	var expected_kind := ""
	match event_type:
		"BuildingPlaced":
			var required_building := _first_missing_key(
				event,
				PackedStringArray(["buildingId", "owner", "buildingType", "footprint", "hp"]),
				event_type
			)
			if not required_building.is_empty():
				return _event_validation_failure(required_building, simulated_revision)
			if (
				not _is_positive_integer(event["buildingId"])
				or not _is_positive_integer(event["owner"])
				or not _is_non_empty_string(event["buildingType"])
			):
				return _event_validation_failure("BuildingPlaced contiene identidad o tipo inválido.", simulated_revision)
			var building_id := int(event["buildingId"])
			var owner_id := int(event["owner"])
			var building_type: String = event["buildingType"]
			if (
				not _has_player(owner_id)
				or not BUILDING_VISUAL_PATHS.has(building_type)
				or _building_visual_path(building_type, owner_id).is_empty()
			):
				return _event_validation_failure("BuildingPlaced contiene identidad o tipo inválido.", simulated_revision)
			if simulated_entities.has(building_id) or simulated_destroyed.has(building_id):
				return _event_validation_failure("BuildingPlaced reutiliza un EntityId existente o destruido.", simulated_revision)
			if typeof(event["footprint"]) != TYPE_DICTIONARY:
				return _event_validation_failure("BuildingPlaced.footprint debe ser un objeto.", simulated_revision)
			var footprint: Dictionary = event["footprint"]
			var footprint_error := _first_missing_key(
				footprint,
				PackedStringArray(["column", "row", "width", "height"]),
				"BuildingPlaced.footprint"
			)
			if not footprint_error.is_empty():
				return _event_validation_failure(footprint_error, simulated_revision)
			if (
				not _is_non_negative_integer(footprint["column"])
				or not _is_non_negative_integer(footprint["row"])
				or not _is_positive_integer(footprint["width"])
				or not _is_positive_integer(footprint["height"])
			):
				return _event_validation_failure("BuildingPlaced.footprint tiene valores inválidos.", simulated_revision)
			var building_map: Dictionary = active_snapshot["map"]
			var catalog_buildings := _catalog_by_type("buildings", "buildingType")
			var catalog_entry: Dictionary = catalog_buildings.get(building_type, {})
			if catalog_entry.is_empty():
				return _event_validation_failure("BuildingPlaced referencia un tipo ausente del catálogo.", simulated_revision)
			if (
				int(footprint["column"]) + int(footprint["width"]) > int(building_map["columns"])
				or int(footprint["row"]) + int(footprint["height"]) > int(building_map["rows"])
				or int(footprint["width"]) != int(catalog_entry["footprintWidth"])
				or int(footprint["height"]) != int(catalog_entry["footprintHeight"])
			):
				return _event_validation_failure("BuildingPlaced.footprint excede el mapa o el catálogo.", simulated_revision)
			if not _is_non_negative_integer(event["hp"]) or int(event["hp"]) > int(catalog_entry["maxHp"]):
				return _event_validation_failure("BuildingPlaced.hp es inválido.", simulated_revision)
			simulated_entities[building_id] = {
				"kind": "building",
				"buildingType": building_type,
				"owner": owner,
			}
			id_field = "buildingId"
			expected_kind = "building"
		"UnitSpawned":
			var required_unit := _first_missing_key(
				event,
				PackedStringArray(["unitId", "owner", "unitType", "x", "z", "hp"]),
				event_type
			)
			if not required_unit.is_empty():
				return _event_validation_failure(required_unit, simulated_revision)
			if (
				not _is_positive_integer(event["unitId"])
				or not _is_positive_integer(event["owner"])
				or not _is_non_empty_string(event["unitType"])
			):
				return _event_validation_failure("UnitSpawned contiene identidad o tipo inválido.", simulated_revision)
			var unit_id := int(event["unitId"])
			var unit_type: String = event["unitType"]
			var units_catalog := _catalog_by_type("units", "unitType")
			var unit_entry: Dictionary = units_catalog.get(unit_type, {})
			var unit_map: Dictionary = active_snapshot["map"]
			if (
				not _has_player(int(event["owner"]))
				or not UNIT_VISUAL_PATHS.has(unit_type)
				or unit_entry.is_empty()
				or not _is_finite_number(event["x"])
				or not _is_finite_number(event["z"])
				or not _is_non_negative_integer(event["hp"])
				or int(event["hp"]) > int(unit_entry.get("maxHp", -1))
				or not _is_within_map(
					float(event["x"]),
					float(event["z"]),
					float(unit_map["columns"]) * float(unit_map["cellSize"]),
					float(unit_map["rows"]) * float(unit_map["cellSize"])
				)
			):
				return _event_validation_failure("UnitSpawned contiene datos inválidos.", simulated_revision)
			if simulated_entities.has(unit_id) or simulated_destroyed.has(unit_id):
				return _event_validation_failure("UnitSpawned reutiliza un EntityId existente o destruido.", simulated_revision)
			simulated_entities[unit_id] = {
				"kind": "unit",
				"unitType": unit_type,
				"owner": int(event["owner"]),
			}
			id_field = "unitId"
			expected_kind = "unit"
		"BuildingProgressChanged", "BuildingCompleted":
			id_field = "buildingId"
			expected_kind = "building"
		"FarmDepleted", "FarmRepairStarted", "FarmRepairProgressChanged", "FarmRepairCompleted":
			id_field = "farmId"
			expected_kind = "building"
		"TreeDepleted", "TreeRespawned":
			id_field = "treeId"
			expected_kind = "tree"
		"UnitOrderChanged", "RepathRequested":
			id_field = "unitId"
			expected_kind = "unit"
		"EntityDamaged", "EntityDestroyed":
			id_field = "entityId"
		"MapRevisionChanged":
			if not event.has("revision") or not _is_non_negative_integer(event["revision"]):
				return _event_validation_failure("MapRevisionChanged.revision debe ser un entero no negativo.", simulated_revision)
			var revision := int(event["revision"])
			if revision <= simulated_revision:
				return _event_validation_failure("MapRevisionChanged no avanza la revisión del mapa.", simulated_revision)
			return {"ok": true, "detail": "", "mapRevision": revision}
		"ProductionChanged":
			id_field = "producerId"
			expected_kind = "building"
		"ResourcesChanged":
			if (
				not event.has("wood")
				or not event.has("food")
				or not _is_non_negative_integer(event["wood"])
				or not _is_non_negative_integer(event["food"])
			):
				return _event_validation_failure("ResourcesChanged requiere saldos enteros no negativos.", simulated_revision)
			return {"ok": true, "detail": "", "mapRevision": simulated_revision}
		_:
			return _event_validation_failure(
				"Discriminador de evento desconocido: %s." % event_type,
				simulated_revision
			)

	var entity_id := -1
	if id_field.is_empty() or not event.has(id_field) or not _is_positive_integer(event[id_field]):
		return _event_validation_failure("%s requiere un ID positivo." % event_type, simulated_revision)
	entity_id = int(event[id_field])
	var entity_error := _validate_event_entity(
		event_type,
		entity_id,
		expected_kind,
		simulated_entities
	)
	if not entity_error.is_empty():
		return _event_validation_failure(entity_error, simulated_revision)

	match event_type:
		"BuildingProgressChanged", "EntityDamaged":
			if not event.has("hp") or not _is_non_negative_integer(event["hp"]):
				return _event_validation_failure("%s.hp debe ser un entero no negativo." % event_type, simulated_revision)
			var max_hp := _entity_max_hp(entity_id, simulated_entities)
			if max_hp > 0 and int(event["hp"]) > max_hp:
				return _event_validation_failure("%s.hp supera el máximo del catálogo." % event_type, simulated_revision)
		"FarmDepleted", "FarmRepairStarted", "FarmRepairProgressChanged", "FarmRepairCompleted":
			if str(simulated_entities[entity_id].get("buildingType", "")) != "Farm":
				return _event_validation_failure("%s referencia un edificio que no es Farm." % event_type, simulated_revision)
			if event_type == "FarmRepairProgressChanged":
				if (
					not event.has("workDone")
					or not event.has("workRequired")
					or not _is_non_negative_integer(event["workDone"])
					or not _is_positive_integer(event["workRequired"])
					or int(event["workDone"]) > int(event["workRequired"])
				):
					return _event_validation_failure("FarmRepairProgressChanged tiene progreso inválido.", simulated_revision)
		"UnitOrderChanged":
			if (
				not event.has("orderId")
				or not _is_non_negative_integer(event["orderId"])
				or not _is_non_empty_string(event.get("kind"))
				or not UNIT_ORDER_KINDS.has(event["kind"])
			):
				return _event_validation_failure("UnitOrderChanged requiere orderId y kind válidos.", simulated_revision)
		"RepathRequested":
			if (
				not event.has("orderId")
				or not _is_positive_integer(event["orderId"])
				or not event.has("mapRevision")
				or not _is_non_negative_integer(event["mapRevision"])
				or not event.has("isPaused")
				or typeof(event["isPaused"]) != TYPE_BOOL
			):
				return _event_validation_failure("RepathRequested tiene campos inválidos.", simulated_revision)
			var has_destination := event.has("destination") and event["destination"] != null
			var has_target := event.has("targetId") and event["targetId"] != null
			if has_destination == has_target:
				return _event_validation_failure("RepathRequested debe contener destination o targetId, pero no ambos.", simulated_revision)
			if has_destination:
				if typeof(event["destination"]) != TYPE_DICTIONARY:
					return _event_validation_failure("RepathRequested.destination debe ser un objeto.", simulated_revision)
				var destination: Dictionary = event["destination"]
				if (
					not destination.has("x")
					or not destination.has("z")
					or not _is_finite_number(destination["x"])
					or not _is_finite_number(destination["z"])
				):
					return _event_validation_failure("RepathRequested.destination requiere X/Z finitos.", simulated_revision)
			if has_target and not _is_positive_integer(event["targetId"]):
				return _event_validation_failure("RepathRequested.targetId debe ser positivo.", simulated_revision)
			if int(simulated_entities[entity_id].get("owner", 0)) != int(active_snapshot["playerId"]):
				return _event_validation_failure("RepathRequested referencia una unidad que no pertenece al jugador local.", simulated_revision)
		"ProductionChanged":
			var required_production := _first_missing_key(
				event,
				PackedStringArray(["status", "pending"]),
				event_type
			)
			if not required_production.is_empty():
				return _event_validation_failure(required_production, simulated_revision)
			if (
				not _is_non_empty_string(event["status"])
				or not PRODUCTION_STATUSES.has(event["status"])
				or typeof(event["pending"]) != TYPE_ARRAY
			):
				return _event_validation_failure("ProductionChanged tiene status o pending inválidos.", simulated_revision)
			if int(simulated_entities[entity_id].get("owner", 0)) != int(active_snapshot["playerId"]):
				return _event_validation_failure("ProductionChanged referencia un productor que no es propio.", simulated_revision)
			var known_units := _catalog_by_type("units", "unitType")
			if event.has("activeUnitType") and event["activeUnitType"] != null:
				if not _is_non_empty_string(event["activeUnitType"]) or not known_units.has(event["activeUnitType"]):
					return _event_validation_failure("ProductionChanged.activeUnitType es inválido.", simulated_revision)
			if event.has("remainingTicks") and event["remainingTicks"] != null:
				if not _is_non_negative_integer(event["remainingTicks"]):
					return _event_validation_failure("ProductionChanged.remainingTicks es inválido.", simulated_revision)
			for pending_type in event["pending"]:
				if not _is_non_empty_string(pending_type) or not known_units.has(pending_type):
					return _event_validation_failure("ProductionChanged.pending incluye un tipo desconocido.", simulated_revision)
		"EntityDestroyed":
			simulated_entities.erase(entity_id)
			simulated_destroyed[entity_id] = true

	return {"ok": true, "detail": "", "mapRevision": simulated_revision}


func _validate_event_entity(
	event_type: String,
	entity_id: int,
	expected_kind: String,
	simulated_entities: Dictionary
) -> String:
	if not simulated_entities.has(entity_id):
		return "%s referencia la entidad inexistente %d." % [event_type, entity_id]
	var entity_data: Dictionary = simulated_entities[entity_id]
	if not expected_kind.is_empty() and entity_data.get("kind") != expected_kind:
		return "%s referencia una entidad %s, no %s." % [
			event_type,
			str(entity_data.get("kind", "desconocida")),
			expected_kind,
		]
	return ""


func _validate_command_results(command_results: Array) -> String:
	for index in range(command_results.size()):
		var result: Variant = command_results[index]
		if typeof(result) != TYPE_DICTIONARY:
			return "commandResults[%d] debe ser un objeto." % index
		var data: Dictionary = result
		var missing_key := _first_missing_key(
			data,
			PackedStringArray(["clientCommandId", "sequence", "tick", "outcome", "entityResults"]),
			"commandResults[%d]" % index
		)
		if not missing_key.is_empty():
			return missing_key
		if (
			not _is_positive_integer(data["clientCommandId"])
			or int(data["clientCommandId"]) > PROTOCOL_CODEC.MAX_SAFE_MESSAGE_ID
			or not _is_positive_integer(data["sequence"])
			or not _is_non_negative_integer(data["tick"])
			or not _is_non_empty_string(data["outcome"])
			or not ["Accepted", "PartiallyAccepted", "Rejected"].has(data["outcome"])
			or typeof(data["entityResults"]) != TYPE_ARRAY
		):
			return "commandResults[%d] tiene campos inválidos." % index
		if data.has("error") and data["error"] != null and not _is_non_empty_string(data["error"]):
			return "commandResults[%d].error debe ser un string no vacío o null." % index
		for entity_index in range(data["entityResults"].size()):
			var entity_result: Variant = data["entityResults"][entity_index]
			if typeof(entity_result) != TYPE_DICTIONARY:
				return "commandResults[%d].entityResults[%d] debe ser un objeto." % [index, entity_index]
			var entity_data: Dictionary = entity_result
			if (
				not entity_data.has("entityId")
				or not _is_positive_integer(entity_data["entityId"])
				or (
					entity_data.has("error")
					and entity_data["error"] != null
					and not _is_non_empty_string(entity_data["error"])
				)
			):
				return "commandResults[%d].entityResults[%d] tiene campos inválidos." % [index, entity_index]
	return ""


func _apply_state_event(event: Dictionary, tick: int) -> Dictionary:
	var event_type: String = event["type"]
	var event_notification: Dictionary = {}
	match event_type:
		"BuildingPlaced":
			var building: Dictionary = {
				"buildingId": int(event["buildingId"]),
				"owner": int(event["owner"]),
				"buildingType": event["buildingType"],
				"footprint": event["footprint"].duplicate(true),
				"hp": int(event["hp"]),
				"isCompleted": false,
			}
			var footprint: Dictionary = building["footprint"]
			var cell_size := float(active_snapshot["map"]["cellSize"])
			var building_position := Vector3(
				(float(footprint["column"]) + float(footprint["width"]) / 2.0) * cell_size,
				0.0,
				(float(footprint["row"]) + float(footprint["height"]) / 2.0) * cell_size
			)
			var path := _building_visual_path(building["buildingType"], building["owner"])
			var building_instance_result := _create_entity_node(
				path,
				"Building_%d" % building["buildingId"],
				building,
				building_position
			)
			if not bool(building_instance_result["ok"]):
				return {"ok": false, "detail": str(building_instance_result["detail"])}
			var buildings_parent := active_world.get_node("Entities/Buildings") as Node3D
			if buildings_parent == null:
				return {"ok": false, "detail": "No existe el contenedor de edificios del mundo activo."}
			var building_node: Node3D = building_instance_result["node"]
			buildings_parent.add_child(building_node)
			_clear_ruins_in(_footprint_rect(building["footprint"]))
			entities_by_id[int(building["buildingId"])] = building_node
			_store_entity_snapshot(int(building["buildingId"]), building)
			_batch_placed_buildings.append(building.duplicate(true))
		"UnitSpawned":
			var unit: Dictionary = {
				"unitId": int(event["unitId"]),
				"owner": int(event["owner"]),
				"unitType": event["unitType"],
				"x": float(event["x"]),
				"z": float(event["z"]),
				"hp": int(event["hp"]),
			}
			var unit_instance_result := _create_entity_node(
				unit_visual_path(str(unit["unitType"]), int(unit["owner"])),
				"Unit_%d" % unit["unitId"],
				unit,
				Vector3(unit["x"], 0.0, unit["z"])
			)
			if not bool(unit_instance_result["ok"]):
				return {"ok": false, "detail": str(unit_instance_result["detail"])}
			var units_parent := active_world.get_node("Entities/Units") as Node3D
			if units_parent == null:
				return {"ok": false, "detail": "No existe el contenedor de unidades del mundo activo."}
			var unit_node: Node3D = unit_instance_result["node"]
			units_parent.add_child(unit_node)
			entities_by_id[int(unit["unitId"])] = unit_node
			_store_entity_snapshot(int(unit["unitId"]), unit)
			_add_position_sample(
				int(unit["unitId"]),
				tick,
				Vector2(unit["x"], unit["z"]),
				Time.get_ticks_usec()
			)
		"BuildingProgressChanged":
			_update_entity_state(int(event["buildingId"]), {"hp": int(event["hp"])})
		"BuildingCompleted":
			_update_entity_state(int(event["buildingId"]), {"isCompleted": true})
		"FarmDepleted":
			_update_farm_state(int(event["farmId"]), {"isDepleted": true, "isRepairing": false})
		"FarmRepairStarted":
			_update_farm_state(int(event["farmId"]), {"isDepleted": true, "isRepairing": true})
		"FarmRepairProgressChanged":
			_update_farm_state(
				int(event["farmId"]),
				{
					"repairWorkDone": int(event["workDone"]),
					"repairWorkRequired": int(event["workRequired"]),
				}
			)
		"FarmRepairCompleted":
			_update_farm_state(
				int(event["farmId"]),
				{
					"isDepleted": false,
					"isRepairing": false,
				}
			)
		"MapRevisionChanged":
			event_notification = {"type": "map_revision", "revision": int(event["revision"])}
		"EntityDamaged":
			_update_entity_state(int(event["entityId"]), {"hp": int(event["hp"])})
		"EntityDestroyed":
			var entity_id := int(event["entityId"])
			var node: Node3D = entities_by_id[entity_id]
			var entity_kind := str(node.get_meta("entity_kind", ""))
			if entity_kind == "unit":
				unit_removed.emit(entity_id)
			elif entity_kind == "building":
				building_removed.emit(entity_id)
			entities_by_id.erase(entity_id)
			if entity_kind == "unit":
				pending_command_by_unit.erase(entity_id)
			accepted_order_intents.erase(entity_id)
			_position_samples.erase(entity_id)
			destroyed_entity_ids[entity_id] = true
			_remove_entity_snapshot(entity_id, entity_kind)
			if entity_kind == "unit":
				_play_death_and_free(node)
			else:
				_show_ruins(entity_id, node)
				node.queue_free()
		"TreeDepleted":
			var depleted_tree_id := int(event["treeId"])
			_update_entity_state(depleted_tree_id, {"isAvailable": false})
			var depleted_tree: Node3D = entities_by_id[depleted_tree_id]
			depleted_tree.visible = false
		"TreeRespawned":
			var respawned_tree_id := int(event["treeId"])
			_update_entity_state(respawned_tree_id, {"isAvailable": true})
			var respawned_tree: Node3D = entities_by_id[respawned_tree_id]
			respawned_tree.visible = true
		"UnitOrderChanged":
			var unit_id := int(event["unitId"])
			_update_entity_state(
				unit_id,
				{"orderId": int(event["orderId"]), "order": event["kind"]}
			)
			accepted_order_intents.erase(unit_id)
			_batch_unit_order_changes.append({
				"unitId": unit_id,
				"orderId": int(event["orderId"]),
				"kind": str(event["kind"]),
			})
		"RepathRequested":
			event_notification = {"type": "repath", "value": event.duplicate(true)}
		"ProductionChanged":
			var producer_id := int(event["producerId"])
			var production := event.duplicate(true)
			production.erase("type")
			production.erase("index")
			_replace_own_production(producer_id, production)
			event_notification = {
				"type": "production",
				"producerId": producer_id,
				"value": production,
			}
		"ResourcesChanged":
			var resources := {"wood": int(event["wood"]), "food": int(event["food"])}
			active_snapshot["ownResources"] = resources.duplicate(true)
			event_notification = {"type": "resources", "value": resources}

	if not event_notification.is_empty():
		return {"ok": true, "notification": event_notification}
	return {"ok": true}


## Una unidad destruida ya salió del mundo lógico (índice, posiciones y snapshot): solo queda
## su modelo, que reproduce `die` y se libera poco después. No se puede seleccionar ni ordenar
## sobre ella, y una posición tardía no la revive (09 §5). Después de caer, el cuerpo se hunde
## bajo el suelo y recién ahí se libera.
func _play_death_and_free(unit: Node3D) -> void:
	unit.set_meta("dying", true)
	unit.name = "Dying_%s" % unit.name
	var player := unit.find_child("AnimationPlayer", true, false) as AnimationPlayer
	if player == null or not player.has_animation(DEATH_ANIMATION):
		unit.queue_free()
		return
	player.play(DEATH_ANIMATION, 0.1)
	var tween := unit.create_tween()
	tween.tween_interval(player.get_animation(DEATH_ANIMATION).length + DEATH_LINGER_SECONDS)
	tween.tween_property(unit, "position:y", unit.position.y - DEATH_SINK_DEPTH, DEATH_SINK_SECONDS) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)
	tween.tween_callback(unit.queue_free)


## Muestra las ruinas de un edificio destruido en su lugar, del tamaño de su huella. Quedan
## RUINS_LINGER_SECONDS, se hunden en el suelo y se liberan. No son una entidad: no se
## seleccionan ni ocupan lugar, y un edificio nuevo en esas celdas las quita en el acto.
func _show_ruins(building_id: int, building: Node3D) -> void:
	var scene := _load_visual_scene(RUINS_VISUAL_PATH)
	var footprint: Dictionary = building.get_meta("entity_state", {}).get("footprint", {})
	if scene == null or footprint.is_empty() or building.get_parent() == null:
		return
	var ruins := scene.instantiate() as Node3D
	ruins.name = "Ruins_%d" % building_id
	var bounds := _model_bounds(ruins)
	var cell_size := float(active_snapshot.get("map", {}).get("cellSize", 1.0))
	var footprint_size := maxf(float(footprint.get("width", 1)), float(footprint.get("height", 1))) * cell_size
	var model_size := maxf(maxf(bounds.size.x, bounds.size.z), 0.01)
	var ruins_scale := footprint_size * RUINS_FOOTPRINT_FILL / model_size
	ruins.scale = Vector3.ONE * ruins_scale
	ruins.position = building.position
	building.get_parent().add_child(ruins)
	_ruins[building_id] = {"node": ruins, "rect": _footprint_rect(footprint)}
	var tween := ruins.create_tween()
	tween.tween_interval(RUINS_LINGER_SECONDS)
	tween.tween_property(ruins, "position:y", ruins.position.y - bounds.end.y * ruins_scale - 0.1, RUINS_SINK_SECONDS) \
		.set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN)
	tween.tween_callback(_free_ruins.bind(building_id))


func _clear_ruins_in(rect: Rect2i) -> void:
	for building_id: Variant in _ruins.keys():
		if (_ruins[building_id]["rect"] as Rect2i).intersects(rect):
			_free_ruins(int(building_id))


func _free_ruins(building_id: int) -> void:
	var entry: Dictionary = _ruins.get(building_id, {})
	_ruins.erase(building_id)
	var ruins: Variant = entry.get("node")
	if is_instance_valid(ruins):
		(ruins as Node).queue_free()


func _footprint_rect(footprint: Dictionary) -> Rect2i:
	return Rect2i(
		int(footprint.get("column", 0)),
		int(footprint.get("row", 0)),
		int(footprint.get("width", 0)),
		int(footprint.get("height", 0))
	)


## Caja que ocupa la geometría de un modelo, en el espacio de su raíz.
func _model_bounds(model: Node3D) -> AABB:
	var bounds := AABB()
	var found := false
	for mesh: Node in model.find_children("*", "MeshInstance3D", true, false):
		var instance := mesh as MeshInstance3D
		var box: AABB = _relative_transform(model, instance) * instance.get_aabb()
		bounds = box if not found else bounds.merge(box)
		found = true
	return bounds


func _make_simulated_entity_index() -> Dictionary:
	var result: Dictionary = {}
	for entity_id in entities_by_id:
		var node: Node3D = entities_by_id[entity_id]
		var state: Dictionary = node.get_meta("entity_state", {})
		result[int(entity_id)] = {
			"kind": node.get_meta("entity_kind", ""),
			"owner": int(state.get("owner", 0)),
			"buildingType": state.get("buildingType", ""),
			"unitType": state.get("unitType", ""),
		}
	return result


func _catalog_by_type(section: String, type_key: String) -> Dictionary:
	var result: Dictionary = {}
	for entry: Dictionary in active_catalog.get(section, []):
		result[str(entry[type_key])] = entry
	return result


func _entity_max_hp(entity_id: int, simulated_entities: Dictionary) -> int:
	var entity: Dictionary = simulated_entities.get(entity_id, {})
	var catalog_section := "units" if entity.get("kind") == "unit" else "buildings"
	var type_key := "unitType" if catalog_section == "units" else "buildingType"
	var entry: Dictionary = _catalog_by_type(catalog_section, type_key).get(str(entity.get(type_key, "")), {})
	return int(entry.get("maxHp", 0))


func _update_entity_state(entity_id: int, changes: Dictionary) -> void:
	var node: Node3D = entities_by_id[entity_id]
	var state: Dictionary = node.get_meta("entity_state", {}).duplicate(true)
	for key in changes:
		state[key] = changes[key]
	node.set_meta("entity_state", state)
	_store_entity_snapshot(entity_id, state)
	if node.get_meta("entity_kind", "") == "building":
		_refresh_building_parts(node)


func _update_farm_state(farm_id: int, changes: Dictionary) -> void:
	var node: Node3D = entities_by_id[farm_id]
	var state: Dictionary = node.get_meta("entity_state", {}).duplicate(true)
	var farm: Dictionary = state.get("farm", {"isDepleted": false, "isRepairing": false}).duplicate(true)
	for key in changes:
		farm[key] = changes[key]
	state["farm"] = farm
	node.set_meta("entity_state", state)
	_store_entity_snapshot(farm_id, state)
	_refresh_building_parts(node)


## Partes opcionales de las escenas de edificio que dependen de su estado:
## - "Scaffolding": el andamio se ve solo mientras el edificio está en obra;
## - "Grain" (granja): el cultivo se oculta mientras está agotada, también durante la
##   reparación. El protocolo solo informa si la granja está agotada, no cuánta comida le
##   queda (FarmDto, FarmDepleted).
func _refresh_building_parts(building_node: Node3D) -> void:
	var state: Dictionary = building_node.get_meta("entity_state", {})
	var scaffolding := building_node.find_child("Scaffolding", true, false) as Node3D
	if scaffolding != null:
		scaffolding.visible = not bool(state.get("isCompleted", false))
	var grain := building_node.find_child("Grain", true, false) as Node3D
	if grain != null:
		grain.visible = not bool(state.get("farm", {}).get("isDepleted", false))


func _store_entity_snapshot(entity_id: int, state: Dictionary) -> void:
	var kind := _entity_kind(state)
	var collection_key: String
	var id_key: String
	match kind:
		"unit":
			collection_key = "units"
			id_key = "unitId"
		"building":
			collection_key = "buildings"
			id_key = "buildingId"
		"tree":
			collection_key = "trees"
			id_key = "treeId"
		_:
			_fail_synchronization("No se pudo determinar el tipo de la entidad %d." % entity_id)
			return
	var collection: Array = active_snapshot[collection_key]
	for index in range(collection.size()):
		if int(collection[index][id_key]) == entity_id:
			collection[index] = state.duplicate(true)
			active_snapshot[collection_key] = collection
			return
	collection.append(state.duplicate(true))
	active_snapshot[collection_key] = collection


func _remove_entity_snapshot(entity_id: int, kind: String) -> void:
	var collection_key: String
	var id_key: String
	match kind:
		"unit":
			collection_key = "units"
			id_key = "unitId"
		"building":
			collection_key = "buildings"
			id_key = "buildingId"
		"tree":
			collection_key = "trees"
			id_key = "treeId"
		_:
			_fail_synchronization("No se pudo determinar el tipo de la entidad destruida %d." % entity_id)
			return
	var collection: Array = active_snapshot[collection_key]
	for index in range(collection.size() - 1, -1, -1):
		if int(collection[index][id_key]) == entity_id:
			collection.remove_at(index)
	active_snapshot[collection_key] = collection


func _replace_own_production(producer_id: int, production: Dictionary) -> void:
	var producers: Array = active_snapshot["ownProduction"]
	var replacement: Dictionary = production.duplicate(true)
	replacement["producerId"] = producer_id
	for index in range(producers.size()):
		if int(producers[index]["producerId"]) == producer_id:
			producers[index] = replacement
			active_snapshot["ownProduction"] = producers
			return
	producers.append(replacement)
	active_snapshot["ownProduction"] = producers


func _remember_command_result(result: Dictionary) -> void:
	var client_command_id := int(result["clientCommandId"])
	if not recent_command_results.has(client_command_id):
		_recent_command_result_ids.append(client_command_id)
	recent_command_results[client_command_id] = result.duplicate(true)
	while _recent_command_result_ids.size() > MAX_RECENT_COMMAND_RESULTS:
		var expired_id: int = _recent_command_result_ids.pop_front()
		recent_command_results.erase(expired_id)
	var pending_move: Dictionary = pending_commands.get(client_command_id, {})
	if not pending_move.is_empty():
		_release_pending_command(client_command_id, pending_move)
		pending_commands.erase(client_command_id)
		_reconcile_accepted_command(pending_move, result)
	pending_repath_responses.erase(client_command_id)
	command_result_received.emit(client_command_id, result.duplicate(true))


func _release_pending_command(message_id: int, command: Dictionary) -> void:
	for unit_id_value: Variant in command.get("unitIds", []):
		var unit_id := int(unit_id_value)
		if int(pending_command_by_unit.get(unit_id, -1)) == message_id:
			pending_command_by_unit.erase(unit_id)


func _placed_building_id(payload: Dictionary) -> int:
	for placed: Dictionary in _batch_placed_buildings:
		var footprint: Dictionary = placed["footprint"]
		if (
			int(placed["owner"]) == int(active_snapshot.get("playerId", 0))
			and str(placed["buildingType"]) == str(payload.get("buildingType", ""))
			and int(footprint.get("column", -1)) == int(payload.get("anchorColumn", -2))
			and int(footprint.get("row", -1)) == int(payload.get("anchorRow", -2))
		):
			return int(placed["buildingId"])
	return 0


func _reconcile_accepted_command(command: Dictionary, result: Dictionary) -> void:
	if not ["Accepted", "PartiallyAccepted"].has(str(result.get("outcome", ""))):
		return
	# La intención aceptada (destino del Move u objetivo de la tarea) permite responder
	# RepathRequested para la misma orden. Stop no deja intención: la unidad queda Idle.
	var order_kind := str(command.get("type", ""))
	var payload: Dictionary = command.get("payload", {})
	var intent_data: Dictionary = {"kind": order_kind}
	if order_kind == "Move":
		var destination_data: Dictionary = payload.get("destination", {})
		if destination_data.is_empty():
			return
		intent_data["destination"] = Vector2(
			float(destination_data.get("x", 0.0)),
			float(destination_data.get("z", 0.0))
		)
	elif order_kind == "Build" and payload.has("anchorColumn"):
		# Build colocar: el objetivo es la obra creada en el mismo lote (03 §3, paso 5).
		var placed_id := _placed_building_id(payload)
		if placed_id <= 0:
			return
		intent_data["targetId"] = placed_id
	elif TASK_TARGET_KEYS.has(order_kind):
		var target_key: String = TASK_TARGET_KEYS[order_kind]
		if not payload.has(target_key):
			return
		intent_data["targetId"] = int(payload[target_key])
	else:
		return
	for entity_result_value: Variant in result.get("entityResults", []):
		if typeof(entity_result_value) != TYPE_DICTIONARY:
			continue
		var entity_result: Dictionary = entity_result_value
		if entity_result.get("error", null) != null:
			continue
		var unit_id := int(entity_result.get("entityId", 0))
		if not command.get("unitIds", []).has(unit_id):
			continue
		var matching_changes: Array[Dictionary] = []
		for order_change: Dictionary in _batch_unit_order_changes:
			if int(order_change["unitId"]) == unit_id and str(order_change["kind"]) == order_kind:
				matching_changes.append(order_change)
		if matching_changes.size() != 1:
			continue
		var unit: Node3D = entities_by_id.get(unit_id)
		if unit == null:
			continue
		var current_state: Dictionary = unit.get_meta("entity_state", {})
		if (
			str(current_state.get("order", "")) != order_kind
			or int(current_state.get("orderId", 0)) != int(matching_changes[0]["orderId"])
		):
			continue
		var intent: Dictionary = intent_data.duplicate()
		intent["orderId"] = int(matching_changes[0]["orderId"])
		accepted_order_intents[unit_id] = intent


func _reset_synchronization_cursors() -> void:
	_clear_command_tracking()
	initial_state_tick = int(active_snapshot["tick"])
	last_state_events_tick = initial_state_tick
	last_position_update_tick = initial_state_tick
	map_revision = int(active_snapshot["map"]["revision"])
	navigation_required_revision = map_revision
	destroyed_entity_ids.clear()
	recent_command_results.clear()
	_recent_command_result_ids.clear()
	synchronization_failed = false
	_position_samples.clear()
	_position_clock_tick = -1
	_position_clock_received_usec = 0
	for entity_id in entities_by_id:
		var node: Node3D = entities_by_id[entity_id]
		if node.get_meta("entity_kind", "") != "unit":
			continue
		var state: Dictionary = node.get_meta("entity_state", {})
		_add_position_sample(
			int(entity_id),
			last_position_update_tick,
			Vector2(float(state["x"]), float(state["z"])),
			Time.get_ticks_usec()
		)


func _clear_synchronization_state() -> void:
	_clear_command_tracking()
	initial_state_tick = -1
	last_state_events_tick = -1
	last_position_update_tick = -1
	map_revision = 0
	navigation_required_revision = 0
	destroyed_entity_ids.clear()
	recent_command_results.clear()
	_recent_command_result_ids.clear()
	synchronization_failed = false
	_position_samples.clear()
	_position_clock_tick = -1
	_position_clock_received_usec = 0


func _clear_command_tracking() -> void:
	pending_commands.clear()
	pending_repath_responses.clear()
	pending_command_by_unit.clear()
	accepted_order_intents.clear()
	_units_calculating_command.clear()
	_batch_unit_order_changes.clear()
	_batch_placed_buildings.clear()
	_ruins.clear()


func _add_position_sample(
	entity_id: int,
	tick: int,
	world_position: Vector2,
	received_usec: int
) -> void:
	var samples: Array = _position_samples.get(entity_id, [])
	var sample := {"tick": tick, "position": world_position, "received_usec": received_usec}
	if not samples.is_empty() and int(samples[-1]["tick"]) == tick:
		samples[-1] = sample
	else:
		samples.append(sample)
		if samples.size() > 2:
			samples.pop_front()
	_position_samples[entity_id] = samples

	var node: Node3D = entities_by_id[entity_id]
	var state: Dictionary = node.get_meta("entity_state", {}).duplicate(true)
	state["x"] = world_position.x
	state["z"] = world_position.y
	node.set_meta("entity_state", state)
	node.set_meta("official_position", world_position)
	_store_entity_snapshot(entity_id, state)


func _update_unit_interpolation(now_usec: int) -> void:
	if _position_clock_tick < 0 or _position_clock_received_usec <= 0:
		return
	var ticks_per_second := float(active_catalog.get("ticksPerSecond", 0))
	if ticks_per_second <= 0.0:
		return
	var delay_ticks := ticks_per_second / float(POSITION_RATE_HZ)
	var elapsed_seconds := maxf(0.0, float(now_usec - _position_clock_received_usec) / 1_000_000.0)
	var render_tick := minf(
		float(_position_clock_tick) - delay_ticks + elapsed_seconds * ticks_per_second,
		float(_position_clock_tick)
	)

	for entity_id in _position_samples:
		if not entities_by_id.has(entity_id):
			continue
		var samples: Array = _position_samples[entity_id]
		if samples.is_empty():
			continue
		var displayed_position: Vector2 = samples[-1]["position"]
		if samples.size() >= 2:
			var previous: Dictionary = samples[-2]
			var latest: Dictionary = samples[-1]
			var previous_tick := float(previous["tick"])
			var latest_tick := float(latest["tick"])
			if render_tick <= previous_tick:
				displayed_position = previous["position"]
			elif render_tick < latest_tick and latest_tick > previous_tick:
				var weight := (render_tick - previous_tick) / (latest_tick - previous_tick)
				var previous_position: Vector2 = previous["position"]
				var latest_position: Vector2 = latest["position"]
				displayed_position = previous_position.lerp(latest_position, weight)

		var node: Node3D = entities_by_id[entity_id]
		node.position = Vector3(displayed_position.x, node.position.y, displayed_position.y)


func _has_player(player_id: int) -> bool:
	for player: Dictionary in active_snapshot.get("players", []):
		if int(player["playerId"]) == player_id:
			return true
	return false


func _event_validation_failure(detail: String, current_revision: int) -> Dictionary:
	return {"ok": false, "detail": detail, "mapRevision": current_revision}


func _fail_event_batch(
	tick: int,
	index: int,
	detail: String,
	event: Dictionary = {}
) -> bool:
	var context := "StateEvents tick=%d index=%d type=%s" % [
		tick,
		index,
		str(event.get("type", "<invalid>")),
	]
	for id_key in ["buildingId", "farmId", "treeId", "unitId", "entityId", "producerId"]:
		if event.has(id_key):
			context += " id=%s" % str(event[id_key])
			break
	return _fail_synchronization("%s: %s" % [context, detail])


func _fail_synchronization(detail: String) -> bool:
	if synchronization_failed:
		return false
	synchronization_failed = true
	_report_error("WorldSynchronizationFailed", detail)
	world_sync_failed.emit(detail)
	return false


func _report_sync_warning(error_code: String, detail: String) -> void:
	push_warning("%s: %s" % [error_code, detail])
	world_sync_warning.emit(error_code, detail)


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
		var path := unit_visual_path(str(unit["unitType"]), int(unit["owner"]))
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

	var navigation := CLIENT_NAVIGATION_SCRIPT.new() as Node3D
	if navigation == null:
		staged_world.free()
		return {"ok": false, "detail": "No se pudo crear ClientNavigation."}
	navigation.name = "ClientNavigation"
	navigation.call("configure", map, state["buildings"], int(map.get("revision", 0)))
	var bake_failed_callable := Callable(self, "_on_navigation_bake_failed")
	var signal_error: Error = navigation.connect("navigation_bake_failed", bake_failed_callable)
	if signal_error != OK:
		staged_world.free()
		return {"ok": false, "detail": "No se pudo enlazar navigation_bake_failed."}
	staged_world.add_child(navigation)

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
	entity_node.set_meta("entity_kind", _entity_kind(entity_state))
	entity_node.set_meta("entity_state", entity_state.duplicate(true))
	entity_node.set_meta("owner", int(entity_state.get("owner", 0)))
	entity_node.add_child(visual)
	if entity_state.has("buildingType"):
		# Un modelo de color que falta falla la preparación, igual que el modelo principal.
		var team_error := _apply_team_models(visual, int(entity_state.get("owner", 0)))
		if not team_error.is_empty():
			entity_node.free()
			return {"ok": false, "detail": "%s: %s" % [node_name, team_error]}
		_refresh_building_parts(entity_node)
	if str(entity_state.get("buildingType", "")) == "Fence":
		# El modelo de valla es neutral (igual para los dos) y corre a lo largo de Z: se gira
		# para que corra en X (de frente a la cámara por defecto), se asegura que quede
		# centrado en la celda y lleva un banderín del color del jugador.
		(visual as Node3D).rotation.y = PI / 2.0
		_center_visual(visual as Node3D)
		entity_node.add_child(_make_team_banner(int(entity_state.get("owner", 0))))
	if entity_state.has("isAvailable"):
		entity_node.visible = bool(entity_state["isAvailable"])
	return {"ok": true, "node": entity_node}


## Escenas de edificio con un nodo "Colored": sus modelos vienen en azul (P1) y para P2 se
## reemplazan por la versión roja del mismo modelo, conservando nombre y transformación.
func _apply_team_models(visual: Node, owner_id: int) -> String:
	var colored := visual.get_node_or_null("Colored")
	if colored == null or owner_id != 2:
		return ""
	for model: Node in colored.get_children():
		var path := model.scene_file_path
		if not path.contains("/blue/"):
			continue
		var red_path := path.replace("/blue/", "/red/").replace("_blue.", "_red.")
		var red_scene := _load_visual_scene(red_path)
		if red_scene == null:
			return "no se pudo cargar la versión roja %s." % red_path
		var red_model := red_scene.instantiate() as Node3D
		red_model.name = model.name
		red_model.transform = (model as Node3D).transform
		colored.add_child(red_model)
		colored.move_child(red_model, model.get_index())
		colored.remove_child(model)
		model.free()
	return ""


## Desplaza el modelo para que su geometría quede centrada en X/Z sobre el origen del nodo
## padre (respetando la rotación que ya tenga).
func _center_visual(visual: Node3D) -> void:
	var bounds := AABB()
	var found := false
	for mesh: Node in visual.find_children("*", "MeshInstance3D", true, false):
		var instance := mesh as MeshInstance3D
		var box: AABB = (visual.transform * _relative_transform(visual, instance)) * instance.get_aabb()
		bounds = box if not found else bounds.merge(box)
		found = true
	if found:
		var center := bounds.get_center()
		visual.position -= Vector3(center.x, 0.0, center.z)


func _relative_transform(ancestor: Node3D, node: Node3D) -> Transform3D:
	var result := Transform3D.IDENTITY
	var current: Node = node
	while current != null and current != ancestor:
		if current is Node3D:
			result = (current as Node3D).transform * result
		current = current.get_parent()
	return result


## Banderín del color del jugador sobre una valla: un mástil y una bandera.
func _make_team_banner(owner_id: int) -> Node3D:
	var banner := Node3D.new()
	banner.name = "TeamBanner"
	var pole := MeshInstance3D.new()
	pole.name = "Pole"
	var pole_mesh := CylinderMesh.new()
	pole_mesh.top_radius = 0.025
	pole_mesh.bottom_radius = 0.025
	pole_mesh.height = BANNER_POLE_HEIGHT
	pole.mesh = pole_mesh
	pole.position = Vector3(BANNER_OFFSET_X, BANNER_BASE_HEIGHT + BANNER_POLE_HEIGHT / 2.0, 0.0)
	pole.material_override = _flat_material(Color(0.36, 0.24, 0.14))
	banner.add_child(pole)
	var flag := MeshInstance3D.new()
	flag.name = "Flag"
	var flag_mesh := BoxMesh.new()
	# La bandera queda de frente a la cámara por defecto (plano X/Y) y flamea hacia el centro.
	flag_mesh.size = Vector3(BANNER_FLAG_WIDTH, BANNER_FLAG_HEIGHT, 0.03)
	flag.mesh = flag_mesh
	flag.position = Vector3(
		BANNER_OFFSET_X - BANNER_FLAG_WIDTH / 2.0 - 0.02,
		BANNER_BASE_HEIGHT + BANNER_POLE_HEIGHT - BANNER_FLAG_HEIGHT / 2.0,
		0.0
	)
	flag.material_override = _flat_material(TEAM_COLORS.get(owner_id, Color.WHITE))
	banner.add_child(flag)
	return banner


func _flat_material(color: Color) -> StandardMaterial3D:
	var material := StandardMaterial3D.new()
	material.albedo_color = color
	material.roughness = 0.8
	return material


func _load_visual_scene(path: String) -> PackedScene:
	return ResourceLoader.load(path) as PackedScene


static func unit_visual_path(unit_type: String, owner_id: int) -> String:
	var paths: Dictionary = UNIT_VISUAL_PATHS.get(unit_type, {})
	return str(paths.get(owner_id, ""))


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


func _entity_kind(entity: Dictionary) -> String:
	if entity.has("unitId"):
		return "unit"
	if entity.has("buildingId"):
		return "building"
	if entity.has("treeId"):
		return "tree"
	return ""


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
