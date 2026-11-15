extends SceneTree

## Pantallas de conexión y de sesión sobre el SessionController real y una red falsa.
## Los payloads de sesión son los de docs/protocol/examples/server-to-client.

const SessionControllerScript = preload("res://scripts/session_controller.gd")
const SessionScreensScript = preload("res://scripts/ui/session_screens.gd")
const InitialWorldScript = preload("res://scripts/initial_world.gd")

const SESSION_ID := "5f0c3a2e-8d4b-4c1e-9a7f-2b6d1e0c9a41"
const EXPECTED_TESTS := 6


class FakeNetworkClient:
	extends Node

	signal connection_state_changed(state: String)
	signal connection_closed(reason: String)
	signal message_received(message_type: String, payload: Variant, envelope: Dictionary)

	## Mismos valores que NetworkClient.State.
	var connection_state := 0
	var connect_calls: Array[Dictionary] = []
	var sent_messages: Array[Dictionary] = []
	var close_calls: Array[String] = []

	func connect_to_server(host: String, port: int, client_name: String = "") -> Error:
		if connection_state != 0:
			return ERR_ALREADY_IN_USE
		connect_calls.append({"host": host, "port": port, "clientName": client_name})
		_set_state(1, "Connecting")
		return OK

	func simulate_connected() -> void:
		_set_state(2, "Connected")

	func deliver(message_type: String, payload: Variant) -> void:
		message_received.emit(message_type, payload, {"protocolVersion": 1, "type": message_type})

	func drop(reason: String) -> void:
		_set_state(0, "Disconnected")
		connection_closed.emit(reason)

	func send_message(message_type: String, payload: Variant = null, message_id: Variant = null) -> bool:
		sent_messages.append({"type": message_type, "payload": payload, "messageId": message_id})
		return connection_state == 2

	func close_connection(reason: String = "ApplicationRequested") -> void:
		if connection_state == 0:
			return
		close_calls.append(reason)
		drop(reason)

	func _set_state(state: int, state_name: String) -> void:
		connection_state = state
		connection_state_changed.emit(state_name)


var _failed := false
var _completed_tests := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	await _test_full_match_flow()
	await _test_defeat_and_draw_titles()
	await _test_unsupported_version_disconnect()
	await _test_cancel_while_connecting()
	_test_invalid_endpoint()
	_test_session_reset_rules()
	_check(_completed_tests == EXPECTED_TESTS, "Only %d of %d tests completed." % [_completed_tests, EXPECTED_TESTS])
	if _failed:
		quit(1)
		return
	print("Session screens tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _test_full_match_flow() -> void:
	var setup := _create_setup()
	var screens: Control = setup["screens"]
	var network: FakeNetworkClient = setup["network"]
	var session: Node = setup["session"]
	_check(screens.current_screen == "connect", "The first screen asks for the server endpoint.")
	_check(screens.mouse_filter == Control.MOUSE_FILTER_STOP, "Menus must block clicks to the world.")

	_check(screens.request_connection("  127.0.0.1 ", 7777, " p1 "), "A valid endpoint should start connecting.")
	_check(
		network.connect_calls == [{"host": "127.0.0.1", "port": 7777, "clientName": "p1"}],
		"The endpoint and client name should be trimmed and passed to NetworkClient."
	)
	_check(_status_title(screens) == "Conectando…", "Connecting shows its own status.")
	_check(not screens.go_to_main_menu(), "The main menu is only reachable from the connection screen.")
	network.simulate_connected()
	_check(_status_title(screens) == "Conectado", "After Hello the client waits for a seat.")

	network.deliver("PlayerAssigned", {"sessionId": SESSION_ID, "playerId": 1})
	_check(_status_title(screens) == "Esperando al rival", "PlayerAssigned waits for the opponent.")
	_check(_status_detail(screens).contains("jugador 1 (azul)"), "The waiting screen shows the assigned identity.")

	network.deliver("InitialState", _initial_state_header(1))
	_check(_status_title(screens) == "Cargando el mundo", "InitialState shows the loading screen.")
	_check(session.complete_initial_state_preparation(1, true), "The loaded world should send ClientReady.")
	_check(_status_title(screens) == "Listo", "After ClientReady the client waits for the opponent's Ready.")

	network.deliver("StartAborted", {"initializationId": 1, "reason": "ReadyTimeout"})
	_check(_status_title(screens) == "Esperando al rival", "StartAborted goes back to waiting (D-057).")
	_check(_status_detail(screens).contains("no confirmó a tiempo"), "StartAborted explains its reason.")

	network.deliver("InitialState", _initial_state_header(2))
	session.complete_initial_state_preparation(2, true)
	network.deliver("MatchStarted", {"sessionId": SESSION_ID, "initializationId": 2})
	_check(screens.current_screen == "", "Running hides the session screens.")
	_check(not screens.visible, "Running hides the overlay.")
	_check(screens.mouse_filter == Control.MOUSE_FILTER_IGNORE, "Running lets clicks reach the world.")

	network.deliver(
		"MatchEnded",
		{"sessionId": SESSION_ID, "resultType": "Victory", "winner": 1, "reason": "BaseDestroyed", "finalTick": 5400}
	)
	_check(_result_title(screens) == "¡Victoria!", "The winner sees a victory screen.")
	_check(_result_detail(screens).contains("Centro Principal"), "The result explains its reason.")
	network.deliver("Disconnecting", {"reason": "Network.ServerShuttingDown"})
	network.drop("Network.ServerShuttingDown")
	_check(_result_title(screens) == "¡Victoria!", "The server closing after MatchEnded keeps the official result.")

	var resets := [0]
	session.session_reset.connect(func() -> void: resets[0] += 1)
	await screens.return_to_menu()
	_check(screens.current_screen == "connect", "Back to menu shows the connection screen.")
	_check(session.get_state_name() == "AwaitingAssignment", "Back to menu resets the session state.")
	_check(session.player_id == 0 and session.session_id.is_empty(), "Back to menu forgets the old identity.")
	_check(resets[0] == 1, "Back to menu asks the world to discard itself.")

	_check(screens.request_connection("127.0.0.1", 7777, "p1"), "A new match can be joined after returning to menu.")
	_free_setup(setup)


func _test_defeat_and_draw_titles() -> void:
	_check(
		SessionScreensScript.match_result_title({"resultType": "Victory", "winner": 2}, 1) == "Derrota",
		"The loser sees a defeat screen."
	)
	_check(SessionScreensScript.match_result_title({"resultType": "Draw"}, 1) == "Empate", "Draw has its own title.")
	_check(
		SessionScreensScript.match_result_title({"resultType": "TechnicalEnd", "reason": "ServerShutdown"}, 1)
			== "Partida terminada",
		"A technical end has a neutral title."
	)
	var setup := _create_setup()
	var screens: Control = setup["screens"]
	var network: FakeNetworkClient = setup["network"]
	screens.request_connection("127.0.0.1", 7777, "p2")
	network.simulate_connected()
	network.deliver("PlayerAssigned", {"sessionId": SESSION_ID, "playerId": 2})
	_check(_status_detail(screens).contains("jugador 2 (rojo)"), "Player 2 is shown as red.")
	network.deliver(
		"MatchEnded",
		{"sessionId": SESSION_ID, "resultType": "Victory", "winner": 1, "reason": "PlayerDisconnected"}
	)
	_check(_result_title(screens) == "Derrota", "Player 2 sees the defeat when player 1 wins.")
	await screens.return_to_menu()
	_check(network.close_calls == ["ApplicationRequested"], "Back to menu closes a connection that is still open.")
	_check(screens.current_screen == "connect", "Closing from the result screen goes back to the menu.")
	_free_setup(setup)


func _test_unsupported_version_disconnect() -> void:
	var setup := _create_setup()
	var screens: Control = setup["screens"]
	var network: FakeNetworkClient = setup["network"]
	screens.request_connection("127.0.0.1", 7777, "p1")
	network.simulate_connected()
	network.deliver(
		"ProtocolError",
		{"error": "Network.UnsupportedVersion", "detail": "El servidor solo acepta la versión 1.", "serverProtocolVersion": 3}
	)
	network.deliver("Disconnecting", {"reason": "Network.UnsupportedVersion"})
	network.drop("RemoteClosed")
	_check(_result_title(screens) == "Conexión cerrada", "A server-side close shows the disconnection screen.")
	_check(
		_result_detail(screens).contains("otra versión") and _result_detail(screens).contains("3"),
		"The disconnection explains the version mismatch and the server version."
	)
	await screens.return_to_menu()
	_check(screens.current_screen == "connect", "The disconnection screen goes back to the menu.")

	screens.request_connection("127.0.0.1", 7799, "p1")
	network.drop("SocketPollFailed")
	_check(_result_title(screens) == "No se pudo conectar", "A connection that never opened has its own title.")
	_check(_result_detail(screens).contains("127.0.0.1:7799"), "A failed connection names the endpoint it tried.")
	_free_setup(setup)


func _test_cancel_while_connecting() -> void:
	var setup := _create_setup()
	var screens: Control = setup["screens"]
	var network: FakeNetworkClient = setup["network"]
	screens.request_connection("127.0.0.1", 7777, "p1")
	await screens.return_to_menu()
	_check(network.close_calls == ["ApplicationRequested"], "Cancel closes the pending connection.")
	_check(screens.current_screen == "connect", "Cancel goes straight back to the menu, not to an error screen.")
	_free_setup(setup)


func _test_invalid_endpoint() -> void:
	var setup := _create_setup()
	var screens: Control = setup["screens"]
	var network: FakeNetworkClient = setup["network"]
	_check(not screens.request_connection("   ", 7777, "p1"), "An empty host is rejected locally.")
	_check(not screens.request_connection("127.0.0.1", 0, "p1"), "Port 0 is rejected locally.")
	_check(network.connect_calls.is_empty(), "Invalid endpoints never reach NetworkClient.")
	_check(screens.current_screen == "connect", "Invalid endpoints stay on the connection screen.")
	_free_setup(setup)


func _test_session_reset_rules() -> void:
	var setup := _create_setup()
	var network: FakeNetworkClient = setup["network"]
	var session: Node = setup["session"]
	network.connect_to_server("127.0.0.1", 7777)
	_check(not session.reset_session(), "The session cannot be reset while a connection is active.")
	network.drop("ConnectionLost")

	var world: Node = InitialWorldScript.new()
	root.add_child(world)
	session.session_reset.connect(world._on_session_reset)
	_check(world.prepare_initial_state(1, _world_state()), "The world used by the reset test should build.")
	_check(session.reset_session(), "A disconnected session can be reset.")
	_check(world.active_world == null and world.entities_by_id.is_empty(), "A session reset discards the loaded world.")
	world.queue_free()
	_free_setup(setup)


# --- Utilidades ----------------------------------------------------------------------

func _create_setup() -> Dictionary:
	var network := FakeNetworkClient.new()
	root.add_child(network)
	var session: Node = SessionControllerScript.new()
	session.bind_network_client(network)
	root.add_child(session)
	var screens: Control = SessionScreensScript.new()
	screens.bind(session, network)
	root.add_child(screens)
	return {"network": network, "session": session, "screens": screens}


func _free_setup(setup: Dictionary) -> void:
	_completed_tests += 1
	for key: String in ["screens", "session", "network"]:
		var node: Node = setup[key]
		if is_instance_valid(node):
			node.queue_free()


func _initial_state_header(initialization_id: int) -> Dictionary:
	return {"sessionId": SESSION_ID, "initializationId": initialization_id, "playerId": 1}


func _status_title(screens: Control) -> String:
	return screens.get_node("Center/StatusPanel").find_child("Title", true, false).text


func _status_detail(screens: Control) -> String:
	return screens.get_node("Center/StatusPanel").find_child("StatusDetail", true, false).text


func _result_title(screens: Control) -> String:
	_check(screens.current_screen == "result", "Expected the result screen, got '%s'." % screens.current_screen)
	return screens.get_node("Center/ResultPanel").find_child("Title", true, false).text


func _result_detail(screens: Control) -> String:
	return screens.get_node("Center/ResultPanel").find_child("ResultDetail", true, false).text


## InitialState mínimo válido para el mundo (un solo jugador con su Centro Principal).
func _world_state() -> Dictionary:
	return {
		"sessionId": SESSION_ID,
		"initializationId": 1,
		"playerId": 1,
		"tick": 0,
		"map": {"cellSize": 1.0, "columns": 20, "rows": 20, "revision": 0},
		"players": [{"playerId": 1, "mainCenterId": 1}, {"playerId": 2, "mainCenterId": 2}],
		"units": [],
		"buildings": [
			{
				"buildingId": 1, "owner": 1, "buildingType": "MainCenter",
				"footprint": {"column": 2, "row": 2, "width": 4, "height": 4}, "hp": 1000, "isCompleted": true,
			},
			{
				"buildingId": 2, "owner": 2, "buildingType": "MainCenter",
				"footprint": {"column": 14, "row": 14, "width": 4, "height": 4}, "hp": 1000, "isCompleted": true,
			},
		],
		"trees": [],
		"ownResources": {"wood": 200, "food": 100},
		"ownProduction": [{"producerId": 1, "status": "Idle", "pending": []}],
		"catalog": {
			"ticksPerSecond": 20,
			"units": [
				{"unitType": "Worker", "maxHp": 40, "cost": {"wood": 0, "food": 50}, "trainingDurationTicks": 200, "speedPerSecond": 3.0},
				{
					"unitType": "Soldier", "maxHp": 80, "cost": {"wood": 0, "food": 80}, "trainingDurationTicks": 300,
					"speedPerSecond": 3.5, "attack": {"damage": 8, "range": 1.5, "cooldownTicks": 20},
				},
			],
			"buildings": [
				{"buildingType": "MainCenter", "maxHp": 1000, "footprintWidth": 4, "footprintHeight": 4},
				{"buildingType": "Farm", "maxHp": 200, "footprintWidth": 2, "footprintHeight": 2, "cost": {"wood": 60, "food": 0}},
				{"buildingType": "Barracks", "maxHp": 400, "footprintWidth": 3, "footprintHeight": 3, "cost": {"wood": 150, "food": 0}},
				{
					"buildingType": "Tower", "maxHp": 300, "footprintWidth": 2, "footprintHeight": 2,
					"cost": {"wood": 120, "food": 0}, "attack": {"damage": 10, "range": 6.0, "cooldownTicks": 30},
				},
				{"buildingType": "Fence", "maxHp": 100, "footprintWidth": 1, "footprintHeight": 1, "cost": {"wood": 5, "food": 0}},
			],
			"farmRepairCost": {"wood": 30, "food": 0},
			"farmRepairWorkRequired": 100,
			"gatherRange": 1.5,
			"buildRange": 1.5,
		},
	}
