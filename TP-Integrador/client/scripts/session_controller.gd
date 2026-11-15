extends Node

signal session_state_changed(state: int, state_name: String)
signal player_assigned(session_id: String, player_id: int)
signal initial_state_preparation_requested(initialization_id: int, payload: Dictionary)
signal initial_state_discard_requested(initialization_id: int)
signal initial_state_preparation_failed(initialization_id: int, detail: String)
signal start_aborted(initialization_id: int, reason: String)
signal session_request_rejected(error_code: String, initialization_id: Variant)
signal match_ended(result: Dictionary)
signal disconnecting_received(reason: String)
signal connection_disconnected(reason: String)
signal session_error(error_code: String, detail: String)

enum State {
	AWAITING_ASSIGNMENT,
	WAITING_FOR_OPPONENT,
	PREPARING,
	READY_SENT,
	RUNNING,
	ENDED,
	DISCONNECTED,
}

const START_ABORT_REASONS := ["OpponentLeft", "ReadyTimeout"]
const MATCH_RESULT_TYPES := ["Victory", "Draw", "TechnicalEnd"]
const MATCH_END_REASONS := [
	"BaseDestroyed",
	"BothBasesDestroyed",
	"PlayerDisconnected",
	"BothPlayersDisconnected",
	"CriticalError",
	"ServerShutdown",
]

var session_state: int = State.AWAITING_ASSIGNMENT
var session_id := ""
var player_id := 0
var initialization_id := 0
var preparation_status := "none"
var final_result: Dictionary = {}

var _network_client: Node
var _seen_initializations: Dictionary = {}
var _disconnecting_reason := ""


func _ready() -> void:
	if _network_client != null:
		return
	var network_client := get_node_or_null("/root/NetworkClient")
	if network_client == null:
		_report_error("NetworkClientUnavailable", "No se encontró el autoload NetworkClient.")
		return
	bind_network_client(network_client)


func bind_network_client(network_client: Node) -> void:
	if network_client == null:
		_report_error("NetworkClientUnavailable", "No se puede enlazar un cliente de red nulo.")
		return
	if (
		not network_client.has_signal("message_received")
		or not network_client.has_signal("connection_closed")
		or not network_client.has_method("send_message")
	):
		_report_error("InvalidNetworkClient", "El cliente de red no expone las señales o métodos requeridos.")
		return

	if _network_client == network_client:
		return
	_unbind_network_client()
	_network_client = network_client
	var message_callable := Callable(self, "_on_message_received")
	var closed_callable := Callable(self, "_on_connection_closed")
	if not _network_client.is_connected("message_received", message_callable):
		var message_error := _network_client.connect("message_received", message_callable)
		if message_error != OK:
			_network_client = null
			_report_error("NetworkSignalConnectionFailed", "No se pudo enlazar message_received.")
			return
	if not _network_client.is_connected("connection_closed", closed_callable):
		var closed_error := _network_client.connect("connection_closed", closed_callable)
		if closed_error != OK:
			_unbind_network_client()
			_report_error("NetworkSignalConnectionFailed", "No se pudo enlazar connection_closed.")
			return


func can_issue_gameplay() -> bool:
	return session_state == State.RUNNING and _disconnecting_reason.is_empty()


func complete_initial_state_preparation(
	completed_initialization_id: int,
	succeeded: bool,
	error_detail: String = ""
) -> bool:
	if (
		session_state != State.PREPARING
		or completed_initialization_id != initialization_id
		or preparation_status != "pending"
	):
		_report_error(
			"StaleInitialStatePreparation",
			"Se ignoró la finalización de una preparación que ya no está vigente."
		)
		return false

	if not succeeded:
		preparation_status = "failed"
		var detail := error_detail
		if detail.is_empty():
			detail = "El componente de mundo no pudo preparar InitialState."
		_report_error("InitialStatePreparationFailed", detail)
		initial_state_preparation_failed.emit(initialization_id, detail)
		return false

	preparation_status = "succeeded"
	if _network_client == null:
		_report_error("ClientReadySendFailed", "No hay un cliente de red enlazado.")
		return false
	var send_result: Variant = _network_client.call(
		"send_message",
		"ClientReady",
		{"initializationId": initialization_id}
	)
	if send_result != true:
		_report_error("ClientReadySendFailed", "No se pudo encolar ClientReady.")
		return false

	_set_state(State.READY_SENT)
	return true


func handle_server_message(message_type: String, payload: Variant, _envelope: Dictionary = {}) -> void:
	if typeof(payload) != TYPE_DICTIONARY and payload != null:
		_report_error("InvalidSessionPayload", "%s requiere un payload objeto." % message_type)
		return
	var message_payload: Dictionary = payload if typeof(payload) == TYPE_DICTIONARY else {}

	match message_type:
		"PlayerAssigned":
			_handle_player_assigned(message_payload)
		"InitialState":
			_handle_initial_state(message_payload)
		"MatchStarted":
			_handle_match_started(message_payload)
		"StartAborted":
			_handle_start_aborted(message_payload)
		"SessionRequestRejected":
			_handle_session_request_rejected(message_payload)
		"MatchEnded":
			_handle_match_ended(message_payload)
		"Disconnecting":
			_handle_disconnecting(message_payload)


func handle_connection_closed(reason: String) -> void:
	if session_state == State.ENDED or session_state == State.DISCONNECTED:
		return

	var resolved_reason := reason
	if resolved_reason.is_empty() or resolved_reason == "RemoteClosed":
		resolved_reason = _disconnecting_reason
	if resolved_reason.is_empty():
		resolved_reason = "ConnectionLost"

	if initialization_id > 0 and (
		session_state == State.PREPARING or session_state == State.READY_SENT
	):
		initial_state_discard_requested.emit(initialization_id)
	_clear_initialization()
	_disconnecting_reason = resolved_reason
	_set_state(State.DISCONNECTED)
	connection_disconnected.emit(resolved_reason)


func _handle_player_assigned(payload: Dictionary) -> void:
	if not _has_non_empty_string(payload, "sessionId") or not _has_positive_integer(payload, "playerId"):
		_report_error("InvalidPlayerAssigned", "PlayerAssigned requiere sessionId y playerId válidos.")
		return
	if session_state != State.AWAITING_ASSIGNMENT:
		_report_error("UnexpectedPlayerAssigned", "No se puede reemplazar la identidad de esta conexión.")
		return

	session_id = str(payload["sessionId"])
	player_id = int(payload["playerId"])
	_set_state(State.WAITING_FOR_OPPONENT)
	player_assigned.emit(session_id, player_id)


func _handle_initial_state(payload: Dictionary) -> void:
	if (
		not _has_non_empty_string(payload, "sessionId")
		or not _has_positive_integer(payload, "initializationId")
		or not _has_positive_integer(payload, "playerId")
	):
		_report_error(
			"InvalidInitialState",
			"InitialState requiere sessionId, initializationId y playerId válidos."
		)
		return
	if session_state != State.WAITING_FOR_OPPONENT:
		_report_error("UnexpectedInitialState", "InitialState llegó fuera del estado de espera.")
		return
	if payload["sessionId"] != session_id or int(payload["playerId"]) != player_id:
		_report_error("InitialStateIdentityMismatch", "InitialState no coincide con la sesión o jugador asignados.")
		return

	var incoming_initialization_id := int(payload["initializationId"])
	if _seen_initializations.has(incoming_initialization_id):
		_report_error("ReusedInitializationId", "El servidor reutilizó un initializationId anterior.")
		return

	initialization_id = incoming_initialization_id
	_seen_initializations[initialization_id] = true
	preparation_status = "pending"
	_set_state(State.PREPARING)
	initial_state_preparation_requested.emit(initialization_id, payload.duplicate(true))


func _handle_match_started(payload: Dictionary) -> void:
	if not _has_non_empty_string(payload, "sessionId") or not _has_positive_integer(payload, "initializationId"):
		_report_error("InvalidMatchStarted", "MatchStarted requiere sessionId e initializationId válidos.")
		return
	if (
		session_state != State.READY_SENT
		or payload["sessionId"] != session_id
		or int(payload["initializationId"]) != initialization_id
	):
		_report_error("UnexpectedMatchStarted", "MatchStarted no coincide con la inicialización lista.")
		return

	_set_state(State.RUNNING)


func _handle_start_aborted(payload: Dictionary) -> void:
	if (
		not _has_positive_integer(payload, "initializationId")
		or not payload.has("reason")
		or typeof(payload["reason"]) != TYPE_STRING
		or not START_ABORT_REASONS.has(payload["reason"])
	):
		_report_error("InvalidStartAborted", "StartAborted requiere initializationId y reason válidos.")
		return
	if (
		(
			session_state != State.PREPARING
			and session_state != State.READY_SENT
		)
		or int(payload["initializationId"]) != initialization_id
	):
		_report_error("UnexpectedStartAborted", "StartAborted no corresponde a la inicialización vigente.")
		return

	var aborted_initialization_id := initialization_id
	var reason := str(payload["reason"])
	_clear_initialization()
	_set_state(State.WAITING_FOR_OPPONENT)
	initial_state_discard_requested.emit(aborted_initialization_id)
	start_aborted.emit(aborted_initialization_id, reason)


func _handle_session_request_rejected(payload: Dictionary) -> void:
	if not payload.has("error") or typeof(payload["error"]) != TYPE_STRING or payload["error"].is_empty():
		_report_error("InvalidSessionRequestRejected", "SessionRequestRejected requiere un código error.")
		return
	var rejected_initialization_id: Variant = payload.get("initializationId")
	if rejected_initialization_id != null and not _is_positive_integer(rejected_initialization_id):
		_report_error(
			"InvalidSessionRequestRejected",
			"initializationId debe ser un entero positivo cuando está presente."
		)
		return
	if rejected_initialization_id != null:
		rejected_initialization_id = int(rejected_initialization_id)
	session_request_rejected.emit(str(payload["error"]), rejected_initialization_id)


func _handle_match_ended(payload: Dictionary) -> void:
	if (
		not _has_non_empty_string(payload, "sessionId")
		or not payload.has("resultType")
		or typeof(payload["resultType"]) != TYPE_STRING
		or not MATCH_RESULT_TYPES.has(payload["resultType"])
	):
		_report_error("InvalidMatchEnded", "MatchEnded requiere sessionId y resultType válidos.")
		return
	if (
		payload.has("reason")
		and payload["reason"] != null
		and (
			typeof(payload["reason"]) != TYPE_STRING
			or not MATCH_END_REASONS.has(payload["reason"])
		)
	):
		_report_error("InvalidMatchEnded", "reason debe ser un motivo de fin reconocido o null.")
		return
	if (
		payload["sessionId"] != session_id
		or session_state == State.AWAITING_ASSIGNMENT
		or session_state == State.DISCONNECTED
	):
		_report_error("MatchEndedSessionMismatch", "MatchEnded no corresponde a la sesión actual.")
		return
	if session_state == State.ENDED:
		_report_error("DuplicateMatchEnded", "Se ignoró un resultado final posterior al resultado oficial.")
		return
	if payload.has("winner") and payload["winner"] != null and not _is_positive_integer(payload["winner"]):
		_report_error("InvalidMatchEnded", "winner debe ser un entero positivo o null.")
		return
	if payload.has("finalTick") and payload["finalTick"] != null and not _is_non_negative_integer(payload["finalTick"]):
		_report_error("InvalidMatchEnded", "finalTick debe ser un entero no negativo o null.")
		return

	final_result = payload.duplicate(true)
	_clear_initialization()
	_set_state(State.ENDED)
	match_ended.emit(final_result.duplicate(true))


func _handle_disconnecting(payload: Dictionary) -> void:
	if not _has_non_empty_string(payload, "reason"):
		_report_error("InvalidDisconnecting", "Disconnecting requiere un motivo válido.")
		return
	var reason := str(payload["reason"])
	if session_state != State.ENDED:
		_disconnecting_reason = reason
	disconnecting_received.emit(reason)


func _on_message_received(message_type: String, payload: Variant, envelope: Dictionary) -> void:
	handle_server_message(message_type, payload, envelope)


func _on_connection_closed(reason: String) -> void:
	handle_connection_closed(reason)


func _clear_initialization() -> void:
	initialization_id = 0
	preparation_status = "none"


func _set_state(next_state: int) -> void:
	if session_state == next_state:
		return
	session_state = next_state
	session_state_changed.emit(session_state, _state_name(session_state))


func _state_name(state: int) -> String:
	match state:
		State.AWAITING_ASSIGNMENT:
			return "AwaitingAssignment"
		State.WAITING_FOR_OPPONENT:
			return "WaitingForOpponent"
		State.PREPARING:
			return "Preparing"
		State.READY_SENT:
			return "ReadySent"
		State.RUNNING:
			return "Running"
		State.ENDED:
			return "Ended"
		State.DISCONNECTED:
			return "Disconnected"
	return "Unknown"


func _has_non_empty_string(payload: Dictionary, key: String) -> bool:
	return payload.has(key) and typeof(payload[key]) == TYPE_STRING and not payload[key].is_empty()


func _has_positive_integer(payload: Dictionary, key: String) -> bool:
	return payload.has(key) and _is_positive_integer(payload[key])


func _is_positive_integer(value: Variant) -> bool:
	return _is_non_negative_integer(value) and value > 0


func _is_non_negative_integer(value: Variant) -> bool:
	if typeof(value) == TYPE_INT:
		return value >= 0
	return typeof(value) == TYPE_FLOAT and is_finite(value) and floor(value) == value and value >= 0


func _report_error(error_code: String, detail: String) -> void:
	push_error("%s: %s" % [error_code, detail])
	session_error.emit(error_code, detail)


func _unbind_network_client() -> void:
	if _network_client == null:
		return
	var message_callable := Callable(self, "_on_message_received")
	var closed_callable := Callable(self, "_on_connection_closed")
	if _network_client.is_connected("message_received", message_callable):
		_network_client.disconnect("message_received", message_callable)
	if _network_client.is_connected("connection_closed", closed_callable):
		_network_client.disconnect("connection_closed", closed_callable)
	_network_client = null
