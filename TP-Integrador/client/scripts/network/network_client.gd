extends Node

const FrameDecoder = preload("res://scripts/network/line_frame_decoder.gd")
const Codec = preload("res://scripts/network/protocol_codec.gd")

signal connection_state_changed(state: String)
signal connection_error(error_code: String, detail: String)
signal connection_closed(reason: String)
signal message_received(message_type: String, payload: Variant, envelope: Dictionary)
signal protocol_error_received(error_code: String, detail: String)
signal disconnecting_received(reason: String)
signal pong_received(ping_id: int, correlated: bool)

enum State {
	DISCONNECTED,
	CONNECTING,
	CONNECTED,
	CLOSING,
}

const MAX_BYTES_PER_PROCESS := 65_536
const CLIENT_MESSAGE_TYPES := [
	"ClientReady",
	"Ping",
	"Pong",
	"Move",
	"Stop",
	"Attack",
	"GatherWood",
	"Build",
	"RepairFarm",
	"TrainUnit",
	"RepathResponse",
]

@export var client_name := "godot-agus"
@export var max_inbound_message_bytes: int = FrameDecoder.DEFAULT_MAX_MESSAGE_BYTES

var connection_state: int = State.DISCONNECTED

var _peer: StreamPeerTCP
var _decoder: Variant
var _outbound: Array[PackedByteArray] = []
var _outbound_offset := 0
var _pending_pings: Dictionary = {}
var _close_reason := ""
var _connection_client_name := ""
var _connection_attempt_active := false
var _close_notified := true
var _accept_outbound := false


func _ready() -> void:
	_decoder = FrameDecoder.new(max_inbound_message_bytes)
	set_process(true)


func _process(_delta: float) -> void:
	if _peer == null:
		return

	var poll_error := _peer.poll()
	if poll_error != OK:
		_fail_connection("SocketPollFailed", "poll() devolvió %s." % error_string(poll_error))
		return

	var status := _peer.get_status()
	if connection_state == State.CONNECTING:
		if status == StreamPeerTCP.STATUS_CONNECTED:
			_on_tcp_connected()
		elif status == StreamPeerTCP.STATUS_ERROR or status == StreamPeerTCP.STATUS_NONE:
			_fail_connection("ConnectionFailed", "No se pudo establecer la conexión TCP.")
			return

	if connection_state != State.CONNECTED and connection_state != State.CLOSING:
		return
	if status != StreamPeerTCP.STATUS_CONNECTED and _peer.get_available_bytes() <= 0:
		if status == StreamPeerTCP.STATUS_ERROR:
			_fail_connection("SocketError", "El socket TCP reportó un error.")
			return
		_finish_connection("RemoteClosed")
		return

	_read_available()
	if _peer == null:
		return
	if _peer.get_status() != StreamPeerTCP.STATUS_CONNECTED and _peer.get_available_bytes() <= 0:
		if _peer.get_status() == StreamPeerTCP.STATUS_ERROR:
			_fail_connection("SocketError", "El socket TCP reportó un error.")
			return
		var reason := _close_reason if connection_state == State.CLOSING and not _close_reason.is_empty() else "RemoteClosed"
		_finish_connection(reason)
		return
	_flush_outbound()
	if connection_state == State.CLOSING and _outbound.is_empty():
		_finish_connection(_close_reason)


func connect_to_server(host: String, port: int, hello_client_name: String = client_name) -> Error:
	if connection_state != State.DISCONNECTED:
		_report_error("AlreadyConnected", "Ya hay un intento o una conexión activa.")
		return ERR_ALREADY_IN_USE
	if host.strip_edges().is_empty() or port < 1 or port > 65_535:
		_report_error("InvalidEndpoint", "Se requiere un host y un puerto entre 1 y 65535.")
		return ERR_INVALID_PARAMETER
	if max_inbound_message_bytes <= 0:
		_report_error("InvalidMessageLimit", "max_inbound_message_bytes debe ser mayor que cero.")
		return ERR_INVALID_PARAMETER

	_reset_connection_data()
	_connection_client_name = hello_client_name
	_decoder = FrameDecoder.new(max_inbound_message_bytes)
	_peer = StreamPeerTCP.new()
	var connect_error := _peer.connect_to_host(host, port)
	if connect_error != OK:
		_peer = null
		_report_error("ConnectionFailed", "connect_to_host() devolvió %s." % error_string(connect_error))
		return connect_error

	_connection_attempt_active = true
	_close_notified = false
	_accept_outbound = false
	_set_state(State.CONNECTING)
	return OK


func send_message(message_type: String, payload: Variant = null, message_id: Variant = null) -> bool:
	if connection_state != State.CONNECTED or not _accept_outbound:
		_report_error("NotConnected", "No se pueden enviar mensajes mientras el cliente no está conectado.")
		return false
	if not CLIENT_MESSAGE_TYPES.has(message_type):
		_report_error("InvalidOutgoingType", "El cliente no puede enviar el tipo %s." % message_type)
		return false
	if Codec.GAMEPLAY_MESSAGE_TYPES.has(message_type):
		if not _is_valid_message_id(message_id):
			_report_error("InvalidMessageId", "Los mensajes de gameplay requieren messageId entre 1 y 2^53.")
			return false
	elif message_id != null and not _is_valid_message_id(message_id):
		_report_error("InvalidMessageId", "messageId debe ser un entero entre 1 y 2^53.")
		return false
	if message_id != null:
		message_id = int(message_id)

	var ping_id := -1
	if message_type == "Ping" or message_type == "Pong":
		if typeof(payload) != TYPE_DICTIONARY or not _is_integer_number(payload.get("pingId")):
			_report_error("InvalidPayload", "%s requiere payload.pingId entero." % message_type)
			return false
		payload = payload.duplicate()
		payload["pingId"] = int(payload["pingId"])
	if message_type == "Ping":
		ping_id = int(payload["pingId"])
		if _pending_pings.has(ping_id):
			_report_error("DuplicatePingId", "Ya hay un Ping pendiente con pingId %d." % ping_id)
			return false

	var encode_result: Dictionary = Codec.encode_message(message_type, payload, message_id)
	if not encode_result["ok"]:
		_report_error("SerializationFailed", str(encode_result.get("detail", "No se pudo serializar el mensaje.")))
		return false

	if message_type == "Ping":
		_pending_pings[ping_id] = true

	_outbound.append(encode_result["bytes"])
	return true


func close_connection(reason: String = "ApplicationRequested") -> void:
	if connection_state == State.DISCONNECTED:
		return

	_accept_outbound = false
	_close_reason = reason
	_set_state(State.CLOSING)
	if _outbound.is_empty():
		_finish_connection(reason)


func _on_tcp_connected() -> void:
	var hello_payload: Dictionary = {}
	if not _connection_client_name.is_empty():
		hello_payload["clientName"] = _connection_client_name

	var hello_result: Dictionary = Codec.encode_message("Hello", hello_payload)
	_outbound.append(hello_result["bytes"])
	_accept_outbound = true
	_set_state(State.CONNECTED)


func _read_available() -> void:
	var bytes_read := 0
	while _peer != null and bytes_read < MAX_BYTES_PER_PROCESS:
		var available := _peer.get_available_bytes()
		if available <= 0:
			return

		var requested := mini(available, MAX_BYTES_PER_PROCESS - bytes_read)
		var read_result: Array = _peer.get_partial_data(requested)
		var read_error: Error = read_result[0]
		if read_error != OK:
			_fail_connection("SocketReadFailed", "get_partial_data() devolvió %s." % error_string(read_error))
			return

		var bytes: PackedByteArray = read_result[1]
		if bytes.is_empty():
			return
		bytes_read += bytes.size()

		var frame_result: Dictionary = _decoder.feed(bytes)
		for frame: PackedByteArray in frame_result["frames"]:
			if not _handle_frame(frame):
				return

		if not frame_result["error"].is_empty():
			_report_error("MessageTooLarge", "Se recibió un mensaje mayor que %d bytes." % max_inbound_message_bytes)
			close_connection("MessageTooLarge")
			return


func _handle_frame(frame: PackedByteArray) -> bool:
	var parse_result: Dictionary = Codec.parse_line(frame)
	if not parse_result["ok"]:
		var error_code: String = parse_result["error"]
		var detail: String = parse_result["detail"]
		_report_error(error_code, detail)
		protocol_error_received.emit(error_code, detail)
		close_connection("ProtocolViolation")
		return false

	var envelope: Dictionary = parse_result["envelope"]
	var message_type: String = envelope["type"]
	var payload: Variant = parse_result["payload"]

	if message_type == "Ping":
		if not send_message("Pong", {"pingId": int(payload["pingId"])}):
			_report_error("PongSendFailed", "No se pudo responder al Ping recibido.")
	elif message_type == "Pong":
		var ping_id: int = int(payload["pingId"])
		var correlated := _pending_pings.has(ping_id)
		if correlated:
			_pending_pings.erase(ping_id)
		pong_received.emit(ping_id, correlated)
	elif message_type == "ProtocolError":
		var detail := str(payload.get("detail", ""))
		var error_code: String = payload["error"]
		_report_error(error_code, detail)
		protocol_error_received.emit(error_code, detail)
	elif message_type == "Disconnecting":
		var reason: String = payload["reason"]
		disconnecting_received.emit(reason)
		_accept_outbound = false
		_close_reason = reason
		_set_state(State.CLOSING)

	message_received.emit(message_type, payload, envelope)
	return message_type != "Disconnecting"


func _flush_outbound() -> void:
	var bytes_written := 0
	while _peer != null and not _outbound.is_empty() and bytes_written < MAX_BYTES_PER_PROCESS:
		var current := _outbound[0]
		var remaining := current.slice(_outbound_offset)
		var write_result: Array = _peer.put_partial_data(remaining)
		var write_error: Error = write_result[0]
		if write_error != OK:
			_fail_connection("SocketWriteFailed", "put_partial_data() devolvió %s." % error_string(write_error))
			return

		var count: int = write_result[1]
		if count <= 0:
			return
		_outbound_offset += count
		bytes_written += count
		if _outbound_offset >= current.size():
			_outbound.pop_front()
			_outbound_offset = 0


func _fail_connection(error_code: String, detail: String) -> void:
	_report_error(error_code, detail)
	_finish_connection(error_code)


func _finish_connection(reason: String) -> void:
	if _peer != null:
		_peer.disconnect_from_host()
		_peer = null

	_accept_outbound = false
	_outbound.clear()
	_outbound_offset = 0
	_pending_pings.clear()
	if _decoder != null:
		_decoder.reset()
	_set_state(State.DISCONNECTED)

	if _connection_attempt_active and not _close_notified:
		_close_notified = true
		_connection_attempt_active = false
		connection_closed.emit(reason)


func _reset_connection_data() -> void:
	_outbound.clear()
	_outbound_offset = 0
	_pending_pings.clear()
	_close_reason = ""
	_connection_attempt_active = false
	_close_notified = true
	_accept_outbound = false


func _set_state(next_state: int) -> void:
	if connection_state == next_state:
		return
	connection_state = next_state
	connection_state_changed.emit(_state_name(next_state))


func _state_name(state: int) -> String:
	match state:
		State.DISCONNECTED:
			return "Disconnected"
		State.CONNECTING:
			return "Connecting"
		State.CONNECTED:
			return "Connected"
		State.CLOSING:
			return "Closing"
	return "Unknown"


func _report_error(error_code: String, detail: String) -> void:
	push_error("%s: %s" % [error_code, detail])
	connection_error.emit(error_code, detail)


func _is_valid_message_id(value: Variant) -> bool:
	return (
		typeof(value) == TYPE_INT
		and value >= 1
		and value <= Codec.MAX_SAFE_MESSAGE_ID
	)


func _is_integer_number(value: Variant) -> bool:
	if typeof(value) == TYPE_INT:
		return true
	return typeof(value) == TYPE_FLOAT and is_finite(value) and floor(value) == value
