extends Node

## Vista previa del cliente sin servidor (herramienta de desarrollo, no es parte del juego).
## Carga la escena de partida real y la alimenta con los mensajes generados por el servidor en
## docs/protocol/examples/server-to-client, a través de una red simulada en lugar de TCP.
## Sirve para validar a ojo el HUD, las pantallas de sesión y los modelos del mundo.
##
## Los comandos que manda el cliente quedan registrados en el panel y se pueden responder a
## mano (aceptar, rechazar o CommandRejected). No hay simulación: no se mueven unidades ni
## avanza la producción salvo con los lotes de ejemplo. Para eso haría falta un servidor falso.

const MATCH_SCENE := preload("res://scenes/match_world.tscn")
const EXAMPLES_DIR := "res://../docs/protocol/examples/server-to-client"

## Lotes de gameplay de los ejemplos, en orden de partida, aplicables sobre 02-initial-state.
const GAMEPLAY_EXAMPLES := [
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
	"20-command-rejected.json",
]


## Entidades que la vista previa suma al mundo de 02-initial-state para ver todos los modelos:
## un edificio de cada tipo y dos soldados por jugador. Usan IDs desde 101 y celdas libres
## cerca de cada base, para no chocar con los lotes de ejemplo 05 a 20 (IDs 1 a 12, la obra
## del 07 en (8, 10) y la unidad del 12 en (6.5, 2.5)).
const PREVIEW_BUILDINGS := [
	{"buildingId": 101, "owner": 1, "buildingType": "Farm", "footprint": {"column": 2, "row": 8, "width": 2, "height": 2}, "hp": 200, "isCompleted": true, "farm": {"isDepleted": false, "isRepairing": false}},
	{"buildingId": 102, "owner": 1, "buildingType": "Barracks", "footprint": {"column": 2, "row": 11, "width": 3, "height": 3}, "hp": 400, "isCompleted": true},
	{"buildingId": 103, "owner": 1, "buildingType": "Tower", "footprint": {"column": 5, "row": 15, "width": 2, "height": 2}, "hp": 300, "isCompleted": true},
	{"buildingId": 104, "owner": 1, "buildingType": "Fence", "footprint": {"column": 10, "row": 4, "width": 1, "height": 1}, "hp": 100, "isCompleted": true},
	{"buildingId": 111, "owner": 2, "buildingType": "Farm", "footprint": {"column": 36, "row": 30, "width": 2, "height": 2}, "hp": 200, "isCompleted": true, "farm": {"isDepleted": false, "isRepairing": false}},
	{"buildingId": 112, "owner": 2, "buildingType": "Barracks", "footprint": {"column": 35, "row": 26, "width": 3, "height": 3}, "hp": 400, "isCompleted": true},
	{"buildingId": 113, "owner": 2, "buildingType": "Tower", "footprint": {"column": 33, "row": 23, "width": 2, "height": 2}, "hp": 300, "isCompleted": true},
	{"buildingId": 114, "owner": 2, "buildingType": "Fence", "footprint": {"column": 29, "row": 35, "width": 1, "height": 1}, "hp": 100, "isCompleted": true},
]
const PREVIEW_UNITS := [
	{"unitId": 121, "owner": 1, "unitType": "Soldier", "x": 9.5, "z": 6.5, "hp": 80, "order": "Idle"},
	{"unitId": 122, "owner": 1, "unitType": "Soldier", "x": 10.5, "z": 6.5, "hp": 80, "order": "Idle"},
	{"unitId": 131, "owner": 2, "unitType": "Soldier", "x": 30.5, "z": 33.5, "hp": 80, "order": "Idle"},
	{"unitId": 132, "owner": 2, "unitType": "Soldier", "x": 30.5, "z": 32.5, "hp": 80, "order": "Idle"},
]


## Red simulada con la misma interfaz que NetworkClient para SessionController,
## InitialWorld y SessionScreens. Registra lo que envía el cliente.
class PreviewNetwork:
	extends Node

	signal connection_state_changed(state: String)
	signal connection_closed(reason: String)
	signal message_received(message_type: String, payload: Variant, envelope: Dictionary)
	signal message_sent(message_type: String, payload: Variant, message_id: Variant)

	## Mismos valores que NetworkClient.State.
	var connection_state := 0
	var sent_messages: Array[Dictionary] = []

	func connect_to_server(_host: String, _port: int, _client_name: String = "") -> Error:
		if connection_state != 0:
			return ERR_ALREADY_IN_USE
		_set_state(1, "Connecting")
		_set_state.call_deferred(2, "Connected")
		return OK

	func send_message(message_type: String, payload: Variant = null, message_id: Variant = null) -> bool:
		if connection_state != 2:
			return false
		var copy: Variant = payload.duplicate(true) if payload is Dictionary else payload
		sent_messages.append({"type": message_type, "payload": copy, "messageId": message_id})
		message_sent.emit(message_type, copy, message_id)
		return true

	func close_connection(reason: String = "ApplicationRequested") -> void:
		if connection_state == 0:
			return
		_set_state(0, "Disconnected")
		connection_closed.emit(reason)

	func deliver(message_type: String, payload: Variant) -> void:
		message_received.emit(message_type, payload, {"protocolVersion": 1, "type": message_type})

	func _set_state(state: int, state_name: String) -> void:
		if connection_state == state:
			return
		connection_state = state
		connection_state_changed.emit(state_name)


var match_scene: Node
var network: PreviewNetwork
var next_example_index := 0

var _session: Node
var _world: Node
var _screens: Node
var _log_label: Label
var _example_label: Label


func _ready() -> void:
	match_scene = MATCH_SCENE.instantiate()
	add_child(match_scene)
	_world = match_scene
	_screens = match_scene.get_node("CanvasLayer/SessionScreens")
	_session = get_node("/root/SessionController")

	network = PreviewNetwork.new()
	network.name = "PreviewNetwork"
	add_child(network)
	network.message_sent.connect(_on_message_sent)
	# La sesión, el mundo y las pantallas pasan a usar la red simulada.
	_session.bind_network_client(network)
	_world._network_client = network
	network.message_received.connect(_world._on_network_message_received)
	_screens.bind(_session, network)
	_build_panel()


func _exit_tree() -> void:
	# Deja la sesión como estaba: enlazada al NetworkClient real y sin partida.
	if is_instance_valid(_session):
		var real_network := get_node_or_null("/root/NetworkClient")
		if real_network != null:
			_session.bind_network_client(real_network)
		_session.reset_session()


# --- Acciones del panel ---------------------------------------------------------------

## Conecta, asigna la plaza, carga el mundo de 02-initial-state y empieza la partida.
func quick_start() -> void:
	if network.connection_state == 0:
		if _session.get_state_name() != "AwaitingAssignment":
			_session.reset_session()
		network.connect_to_server("preview", 0)
		await get_tree().process_frame
	if _session.get_state_name() == "AwaitingAssignment":
		deliver_example("01-player-assigned.json")
	if _session.get_state_name() == "WaitingForOpponent":
		deliver_example("02-initial-state.json")
	if _session.get_state_name() == "ReadySent":
		deliver_example("04-match-started.json")


## Envía un mensaje de los ejemplos. Los ticks de los lotes se adelantan si hace falta
## para que el mundo los acepte después de los lotes generados en la vista previa.
func deliver_example(filename: String) -> void:
	var envelope := load_example(filename)
	if envelope.is_empty():
		return
	var message_type := str(envelope["type"])
	var payload: Variant = envelope.get("payload", null)
	if message_type == "InitialState" and payload is Dictionary:
		payload = with_preview_entities(payload)
	if payload is Dictionary:
		payload = _adjust_tick(message_type, payload)
	network.deliver(message_type, payload)
	_log("← %s" % filename.get_basename())


## Aplica el siguiente lote de gameplay de los ejemplos (05 a 20).
func apply_next_example() -> bool:
	if next_example_index >= GAMEPLAY_EXAMPLES.size():
		_log("No quedan lotes de ejemplo.")
		return false
	deliver_example(GAMEPLAY_EXAMPLES[next_example_index])
	next_example_index += 1
	_refresh_example_label()
	return true


## Responde el último comando de gameplay enviado con un CommandResult oficial.
## Un TrainUnit aceptado también actualiza la cola del productor, como haría el servidor.
func answer_last_command(accept: bool, error_code := "Production.QueueFull") -> bool:
	var command := _last_gameplay_command()
	if command.is_empty():
		_log("No hay comandos para responder.")
		return false
	var entity_ids := _command_entity_ids(command)
	var entity_results: Array = []
	for entity_id: int in entity_ids:
		entity_results.append({"entityId": entity_id} if accept else {"entityId": entity_id, "error": error_code})
	var tick := _next_tick()
	var result := {
		"clientCommandId": int(command["messageId"]),
		"sequence": int(command["messageId"]),
		"tick": tick,
		"outcome": "Accepted" if accept else "Rejected",
		"entityResults": entity_results,
	}
	if not accept:
		result["error"] = error_code
	var events: Array = []
	if accept and command["type"] == "TrainUnit":
		events.append(_production_after_train(command["payload"]))
	network.deliver("StateEvents", {
		"tick": tick,
		"mapRevision": _world.map_revision,
		"events": events,
		"commandResults": [result],
	})
	_log("← CommandResult %s para %s #%d" % [result["outcome"], command["type"], int(command["messageId"])])
	return true


## Rechazo de admisión (D-075): no entra a la simulación y se puede reintentar.
func reject_last_command_before_admission(error_code := "Network.RateLimited") -> bool:
	var command := _last_gameplay_command()
	if command.is_empty():
		_log("No hay comandos para rechazar.")
		return false
	network.deliver("CommandRejected", {"messageId": int(command["messageId"]), "error": error_code})
	_log("← CommandRejected %s" % error_code)
	return true


func end_match(result_type: String, winner: Variant, reason: String) -> void:
	var payload := {"sessionId": str(_session.session_id), "resultType": result_type, "reason": reason, "finalTick": _next_tick()}
	if winner != null:
		payload["winner"] = winner
	network.deliver("MatchEnded", payload)
	_log("← MatchEnded %s (%s)" % [result_type, reason])


func server_disconnect(reason: String) -> void:
	if reason == "Network.UnsupportedVersion":
		deliver_example("22-protocol-error-unsupported-version.json")
	network.deliver("Disconnecting", {"reason": reason})
	network.close_connection("RemoteClosed")
	_log("← Disconnecting %s" % reason)


func set_resources(wood: int, food: int) -> void:
	network.deliver("StateEvents", {
		"tick": _next_tick(),
		"mapRevision": _world.map_revision,
		"events": [{"type": "ResourcesChanged", "wood": wood, "food": food, "index": 0}],
		"commandResults": [],
	})
	_log("← ResourcesChanged madera %d, comida %d" % [wood, food])


func load_example(filename: String) -> Dictionary:
	var path := ProjectSettings.globalize_path(EXAMPLES_DIR).path_join(filename)
	var file := FileAccess.open(path, FileAccess.READ)
	if file == null:
		_log("No se encontró %s." % filename)
		return {}
	var parsed: Variant = JSON.parse_string(file.get_as_text())
	return _with_integer_numbers(parsed) if parsed is Dictionary else {}


# --- Ayudas ---------------------------------------------------------------------------

## InitialState con los edificios y soldados de la vista previa. Los datos privados (la cola
## del Cuartel) se suman solo para el destinatario, como haría el servidor (09 §3).
func with_preview_entities(initial_state: Dictionary) -> Dictionary:
	var state := initial_state.duplicate(true)
	var player_id := int(state.get("playerId", 0))
	state["buildings"].append_array(PREVIEW_BUILDINGS.duplicate(true))
	state["units"].append_array(PREVIEW_UNITS.duplicate(true))
	for building: Dictionary in PREVIEW_BUILDINGS:
		if building["buildingType"] == "Barracks" and int(building["owner"]) == player_id:
			state["ownProduction"].append({"producerId": building["buildingId"], "status": "Idle", "pending": []})
	return state


func _adjust_tick(message_type: String, payload: Dictionary) -> Dictionary:
	var adjusted := payload.duplicate(true)
	if message_type == "StateEvents" and int(adjusted.get("tick", 0)) <= int(_world.last_state_events_tick):
		adjusted["tick"] = _next_tick()
		for result: Dictionary in adjusted.get("commandResults", []):
			result["tick"] = adjusted["tick"]
	elif message_type == "PositionUpdate" and int(adjusted.get("tick", 0)) <= int(_world.last_position_update_tick):
		adjusted["tick"] = int(_world.last_position_update_tick) + 1
	return adjusted


func _next_tick() -> int:
	return maxi(int(_world.last_state_events_tick), int(_world.last_position_update_tick)) + 1


func _last_gameplay_command() -> Dictionary:
	for index in range(network.sent_messages.size() - 1, -1, -1):
		var message: Dictionary = network.sent_messages[index]
		if message.get("messageId", null) != null:
			return message
	return {}


func _command_entity_ids(command: Dictionary) -> Array[int]:
	var payload: Dictionary = command.get("payload", {})
	var ids: Array[int] = []
	if payload.has("producerId"):
		ids.append(int(payload["producerId"]))
	if payload.has("unitId"):
		ids.append(int(payload["unitId"]))
	for unit_id: Variant in payload.get("unitIds", []):
		ids.append(int(unit_id))
	for route: Dictionary in payload.get("routes", []):
		ids.append(int(route["unitId"]))
	return ids


func _production_after_train(payload: Dictionary) -> Dictionary:
	var producer_id := int(payload["producerId"])
	var unit_type := str(payload["unitType"])
	var production: Dictionary = _world.get_own_production(producer_id)
	var event := {
		"type": "ProductionChanged",
		"producerId": producer_id,
		"status": production.get("status", "Idle"),
		"pending": production.get("pending", []).duplicate(),
		"index": 0,
	}
	if production.has("activeUnitType"):
		event["activeUnitType"] = production["activeUnitType"]
		event["remainingTicks"] = production.get("remainingTicks", 0)
	if str(event["status"]) == "Idle":
		var duration := 0
		for entry: Dictionary in _world.active_catalog.get("units", []):
			if str(entry.get("unitType", "")) == unit_type:
				duration = int(entry.get("trainingDurationTicks", 0))
		event["status"] = "Training"
		event["activeUnitType"] = unit_type
		event["remainingTicks"] = duration
	else:
		event["pending"].append(unit_type)
	return event


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


func _on_message_sent(message_type: String, payload: Variant, message_id: Variant) -> void:
	var id_text := " #%d" % int(message_id) if message_id != null else ""
	_log("→ %s%s %s" % [message_type, id_text, JSON.stringify(payload) if payload != null else ""])


func _log(text: String) -> void:
	print("[Vista previa] ", text)
	if _log_label != null:
		_log_label.text = text


func _refresh_example_label() -> void:
	if _example_label == null:
		return
	if next_example_index >= GAMEPLAY_EXAMPLES.size():
		_example_label.text = "Lotes de ejemplo: terminados"
	else:
		_example_label.text = "Siguiente: %s" % GAMEPLAY_EXAMPLES[next_example_index].get_basename()


# --- Panel de depuración --------------------------------------------------------------

func _build_panel() -> void:
	var layer := CanvasLayer.new()
	layer.name = "PreviewPanel"
	layer.layer = 10
	add_child(layer)
	var panel := PanelContainer.new()
	panel.name = "Panel"
	panel.anchor_left = 1.0
	panel.anchor_right = 1.0
	panel.offset_left = -290
	panel.offset_right = -8
	panel.offset_top = 8
	panel.modulate = Color(1, 1, 1, 0.92)
	layer.add_child(panel)
	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size = Vector2(270, 560)
	scroll.horizontal_scroll_mode = ScrollContainer.SCROLL_MODE_DISABLED
	panel.add_child(scroll)
	var box := VBoxContainer.new()
	box.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(box)

	_add_header(box, "Sesión")
	_add_action(box, "Inicio rápido (01 → 02 → 04)", quick_start)
	_add_action(box, "PlayerAssigned (01)", deliver_example.bind("01-player-assigned.json"))
	_add_action(box, "InitialState (02)", deliver_example.bind("02-initial-state.json"))
	_add_action(box, "StartAborted (03)", deliver_example.bind("03-start-aborted.json"))
	_add_action(box, "MatchStarted (04)", deliver_example.bind("04-match-started.json"))

	_add_header(box, "Gameplay")
	_example_label = Label.new()
	_example_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	box.add_child(_example_label)
	_refresh_example_label()
	_add_action(box, "Aplicar siguiente lote", apply_next_example)
	_add_action(box, "Recursos: 500 / 500", set_resources.bind(500, 500))
	_add_action(box, "Recursos: 0 / 0", set_resources.bind(0, 0))

	_add_header(box, "Último comando enviado")
	_add_action(box, "Aceptar", answer_last_command.bind(true))
	_add_action(box, "Rechazar (QueueFull)", answer_last_command.bind(false, "Production.QueueFull"))
	_add_action(box, "Rechazar (InsufficientWood)", answer_last_command.bind(false, "Economy.InsufficientWood"))
	_add_action(box, "CommandRejected (RateLimited)", reject_last_command_before_admission)

	_add_header(box, "Fin de partida")
	_add_action(box, "Victoria", func() -> void: end_match("Victory", _session.player_id, "BaseDestroyed"))
	_add_action(box, "Derrota", func() -> void: end_match("Victory", 3 - int(_session.player_id), "BaseDestroyed"))
	_add_action(box, "Empate", end_match.bind("Draw", null, "BothBasesDestroyed"))
	_add_action(box, "Victoria por desconexión", func() -> void: end_match("Victory", _session.player_id, "PlayerDisconnected"))
	_add_action(box, "Servidor apagado", end_match.bind("TechnicalEnd", null, "ServerShutdown"))

	_add_header(box, "Cierre del servidor")
	_add_action(box, "Versión incompatible", server_disconnect.bind("Network.UnsupportedVersion"))
	_add_action(box, "Partida llena", server_disconnect.bind("Session.MatchFull"))
	_add_action(box, "Servidor apagándose", server_disconnect.bind("Network.ServerShuttingDown"))

	_add_header(box, "Registro")
	_log_label = Label.new()
	_log_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_log_label.text = "Usá «Inicio rápido» o el botón Conectar."
	box.add_child(_log_label)


func _add_header(parent: Control, text: String) -> void:
	var label := Label.new()
	label.text = text
	label.add_theme_color_override("font_color", Color(1.0, 0.85, 0.5))
	parent.add_child(label)


func _add_action(parent: Control, text: String, action: Callable) -> void:
	var button := Button.new()
	button.text = text
	button.pressed.connect(func() -> void: action.call())
	parent.add_child(button)
