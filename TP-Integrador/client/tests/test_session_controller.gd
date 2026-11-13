extends SceneTree

const SessionControllerScript = preload("res://scripts/session_controller.gd")

class FakeNetworkClient:
	extends Node

	signal message_received(message_type: String, payload: Variant, envelope: Dictionary)
	signal connection_closed(reason: String)

	var sent_messages: Array[Dictionary] = []

	func send_message(message_type: String, payload: Variant = null, message_id: Variant = null) -> bool:
		sent_messages.append({
			"type": message_type,
			"payload": payload,
			"messageId": message_id,
		})
		return true


var _failed := false


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_normal_handshake()
	_test_abort_and_late_preparation()
	_test_rejection_and_official_result()
	_test_invalid_identity_and_disconnect()
	if _failed:
		quit(1)
		return
	print("Session controller tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _create_controller() -> Array:
	var network := FakeNetworkClient.new()
	var controller = SessionControllerScript.new()
	controller.bind_network_client(network)
	root.add_child(network)
	root.add_child(controller)
	return [controller, network]


func _assign_player(controller: Node, network: FakeNetworkClient = null) -> void:
	if network == null:
		controller.handle_server_message(
			"PlayerAssigned",
			{"sessionId": "session-1", "playerId": 2}
		)
		return
	network.message_received.emit(
		"PlayerAssigned",
		{"sessionId": "session-1", "playerId": 2},
		{}
	)


func _initial_state(initialization_id: int, player_id: int = 2) -> Dictionary:
	return {
		"sessionId": "session-1",
		"initializationId": initialization_id,
		"playerId": player_id,
		"tick": 0,
	}


func _test_normal_handshake() -> void:
	var setup := _create_controller()
	var controller = setup[0]
	var network: FakeNetworkClient = setup[1]

	controller.handle_server_message("InitialState", _initial_state(1))
	_check(network.sent_messages.is_empty(), "InitialState before assignment must not send ClientReady.")
	_assign_player(controller, network)
	_check(controller.session_state == SessionControllerScript.State.WAITING_FOR_OPPONENT, "PlayerAssigned must enter waiting.")
	controller.handle_server_message("InitialState", _initial_state(1))
	_check(controller.session_state == SessionControllerScript.State.PREPARING, "InitialState must begin preparation.")
	_check(network.sent_messages.is_empty(), "ClientReady must wait for world preparation.")
	_check(controller.complete_initial_state_preparation(1, true), "Successful preparation should enqueue ClientReady.")
	_check(network.sent_messages.size() == 1, "Exactly one ClientReady should be sent.")
	if network.sent_messages.size() == 1:
		_check(network.sent_messages[0]["type"] == "ClientReady", "The readiness message type is incorrect.")
		_check(
			network.sent_messages[0]["payload"]["initializationId"] == 1,
			"ClientReady used the wrong initializationId."
		)
	_check(controller.session_state == SessionControllerScript.State.READY_SENT, "ReadySent must await MatchStarted.")
	_check(not controller.can_issue_gameplay(), "Gameplay must remain disabled before MatchStarted.")
	_check(
		not controller.complete_initial_state_preparation(1, true),
		"An initialization must not send ClientReady more than once."
	)
	controller.handle_server_message(
		"MatchStarted",
		{"sessionId": "session-1", "initializationId": 1}
	)
	_check(controller.session_state == SessionControllerScript.State.RUNNING, "Matching MatchStarted must enter Running.")
	_check(controller.can_issue_gameplay(), "Gameplay must be enabled only in Running.")
	controller.queue_free()
	network.queue_free()


func _test_abort_and_late_preparation() -> void:
	var setup := _create_controller()
	var controller = setup[0]
	var network: FakeNetworkClient = setup[1]
	var discarded: Array[int] = []
	controller.initial_state_discard_requested.connect(
		func(initialization_id: int) -> void:
			discarded.append(initialization_id)
	)

	_assign_player(controller)
	controller.handle_server_message("InitialState", _initial_state(4))
	controller.handle_server_message(
		"StartAborted",
		{"initializationId": 4, "reason": "OpponentLeft"}
	)
	_check(controller.session_state == SessionControllerScript.State.WAITING_FOR_OPPONENT, "StartAborted must return to waiting.")
	_check(discarded == [4], "StartAborted must discard its prepared world.")
	_check(not controller.complete_initial_state_preparation(4, true), "A late completion for an aborted load must be ignored.")
	_check(network.sent_messages.is_empty(), "A late completion must not send ClientReady.")

	controller.handle_server_message("InitialState", _initial_state(5))
	_check(not controller.complete_initial_state_preparation(5, false, "load failed"), "A failed load must not send ClientReady.")
	_check(controller.preparation_status == "failed", "A failed load must remain visible as failed.")
	controller.handle_server_message(
		"StartAborted",
		{"initializationId": 5, "reason": "ReadyTimeout"}
	)
	controller.handle_server_message("InitialState", _initial_state(6))
	_check(controller.complete_initial_state_preparation(6, true), "A new initialization should be preparable.")
	_check(network.sent_messages.size() == 1, "Only the new initialization should send ClientReady.")
	if network.sent_messages.size() == 1:
		_check(
			network.sent_messages[0]["payload"]["initializationId"] == 6,
			"ClientReady must use the new initializationId."
		)
	controller.handle_server_message(
		"MatchStarted",
		{"sessionId": "session-1", "initializationId": 4}
	)
	_check(controller.session_state == SessionControllerScript.State.READY_SENT, "A stale MatchStarted must not start gameplay.")
	controller.queue_free()
	network.queue_free()


func _test_rejection_and_official_result() -> void:
	var setup := _create_controller()
	var controller = setup[0]
	var network: FakeNetworkClient = setup[1]
	var rejection_errors: Array[String] = []
	controller.session_request_rejected.connect(
		func(error_code: String, initialization_id: Variant) -> void:
			rejection_errors.append(error_code)
			_check(initialization_id == 8, "SessionRequestRejected should expose its initializationId.")
	)

	_assign_player(controller)
	controller.handle_server_message("InitialState", _initial_state(8))
	_check(controller.complete_initial_state_preparation(8, true), "Preparation should send ClientReady.")
	controller.handle_server_message(
		"SessionRequestRejected",
		{"error": "Session.StaleInitialization", "initializationId": 8}
	)
	_check(rejection_errors == ["Session.StaleInitialization"], "The session rejection must be surfaced.")
	_check(network.sent_messages.size() == 1, "SessionRequestRejected must not trigger an automatic retry.")
	_check(not controller.can_issue_gameplay(), "A rejection must not enable gameplay.")

	var result := {
		"sessionId": "session-1",
		"resultType": "Victory",
		"winner": 2,
		"reason": "BaseDestroyed",
		"finalTick": 5400,
	}
	controller.handle_server_message(
		"MatchEnded",
		{
			"sessionId": "another-session",
			"resultType": "Victory",
			"winner": 2,
		}
	)
	_check(controller.session_state == SessionControllerScript.State.READY_SENT, "A result from another session must be ignored.")
	controller.handle_server_message("MatchEnded", result)
	_check(controller.session_state == SessionControllerScript.State.ENDED, "MatchEnded must enter Ended.")
	_check(controller.final_result == result, "The official result must be preserved.")
	network.connection_closed.emit("RemoteClosed")
	_check(controller.session_state == SessionControllerScript.State.ENDED, "Closing after MatchEnded must preserve Ended.")
	_check(controller.final_result == result, "A later close must not erase the official result.")
	controller.queue_free()
	network.queue_free()


func _test_invalid_identity_and_disconnect() -> void:
	var setup := _create_controller()
	var controller = setup[0]
	var network: FakeNetworkClient = setup[1]
	var disconnecting_reasons: Array[String] = []
	var discarded: Array[int] = []
	controller.disconnecting_received.connect(
		func(reason: String) -> void:
			disconnecting_reasons.append(reason)
	)
	controller.initial_state_discard_requested.connect(
		func(initialization_id: int) -> void:
			discarded.append(initialization_id)
	)

	controller.handle_server_message("PlayerAssigned", {"sessionId": "session-1", "playerId": 2})
	controller.handle_server_message("InitialState", _initial_state(3, 1))
	_check(controller.session_state == SessionControllerScript.State.WAITING_FOR_OPPONENT, "A mismatched InitialState must not replace the identity.")
	controller.handle_server_message("InitialState", _initial_state(3))
	network.message_received.emit(
		"Disconnecting",
		{"reason": "Network.ServerShuttingDown"},
		{}
	)
	_check(controller.session_state == SessionControllerScript.State.PREPARING, "Disconnecting must not masquerade as MatchEnded or TCP closure.")
	_check(
		disconnecting_reasons == ["Network.ServerShuttingDown"],
		"The Disconnecting reason must be exposed to the UI."
	)
	network.connection_closed.emit("RemoteClosed")
	_check(controller.session_state == SessionControllerScript.State.DISCONNECTED, "TCP closure before MatchEnded must disconnect.")
	_check(not controller.can_issue_gameplay(), "Disconnected clients cannot issue gameplay.")
	_check(discarded == [3], "A pending world must be discarded when its connection is lost.")
	_check(network.sent_messages.is_empty(), "Disconnecting before preparation completion must not send Ready.")
	controller.queue_free()
	network.queue_free()
