extends RefCounted
class_name ProtocolCodec

const PROTOCOL_VERSION := 1
const MAX_SAFE_MESSAGE_ID := 9_007_199_254_740_992

const MESSAGE_TYPES := [
	"Hello",
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
	"PlayerAssigned",
	"StartAborted",
	"MatchStarted",
	"MatchEnded",
	"SessionRequestRejected",
	"InitialState",
	"StateEvents",
	"PositionUpdate",
	"CommandRejected",
	"ProtocolError",
	"Disconnecting",
]

const GAMEPLAY_MESSAGE_TYPES := [
	"Move",
	"Stop",
	"Attack",
	"GatherWood",
	"Build",
	"RepairFarm",
	"TrainUnit",
	"RepathResponse",
]


static func encode_message(message_type: String, payload: Variant = null, message_id: Variant = null) -> Dictionary:
	if not MESSAGE_TYPES.has(message_type):
		return _failure("UnknownType", "Tipo de mensaje desconocido: %s." % message_type)
	if payload != null and typeof(payload) != TYPE_DICTIONARY:
		return _failure("InvalidPayload", "payload debe ser un objeto JSON o estar ausente.")
	if message_id != null and not _is_valid_message_id(message_id):
		return _failure("InvalidMessageId", "messageId debe ser un entero entre 1 y 2^53.")

	var envelope := {
		"protocolVersion": PROTOCOL_VERSION,
		"type": message_type,
	}
	if message_id != null:
		envelope["messageId"] = int(message_id)
	if payload != null:
		envelope["payload"] = payload

	var json_text := JSON.stringify(envelope)
	if json_text.is_empty():
		return _failure("SerializationFailed", "Godot no pudo serializar el mensaje.")

	var bytes := json_text.to_utf8_buffer()
	bytes.append(10)
	return {
		"ok": true,
		"bytes": bytes,
	}


static func parse_line(line: PackedByteArray) -> Dictionary:
	var text := line.get_string_from_utf8()
	if text.to_utf8_buffer() != line:
		return _failure("MalformedUtf8", "La línea no es UTF-8 válido.")

	var json := JSON.new()
	var parse_error := json.parse(text)
	if parse_error != OK:
		return _failure("MalformedJson", json.get_error_message())

	if typeof(json.data) != TYPE_DICTIONARY:
		return _failure("InvalidEnvelope", "El sobre JSON debe ser un objeto.")

	var envelope: Dictionary = json.data
	if not envelope.has("protocolVersion") or not _is_integer_number(envelope["protocolVersion"]):
		return _failure("InvalidEnvelope", "protocolVersion debe ser un número entero.")
	if int(envelope["protocolVersion"]) != PROTOCOL_VERSION:
		return _failure(
			"UnsupportedVersion",
			"Versión recibida: %s; versión soportada: %d." % [str(envelope["protocolVersion"]), PROTOCOL_VERSION]
		)
	envelope["protocolVersion"] = PROTOCOL_VERSION
	if not envelope.has("type") or typeof(envelope["type"]) != TYPE_STRING or envelope["type"].is_empty():
		return _failure("InvalidEnvelope", "type debe ser un string no vacío.")
	if not MESSAGE_TYPES.has(envelope["type"]):
		return _failure("UnknownType", "Tipo de mensaje desconocido: %s." % envelope["type"])
	if envelope.has("messageId") and not _is_valid_message_id(envelope["messageId"]):
		return _failure("InvalidMessageId", "messageId debe ser un entero entre 1 y 2^53.")
	if envelope.has("messageId"):
		envelope["messageId"] = int(envelope["messageId"])

	var payload: Variant = envelope.get("payload")
	if payload != null and typeof(payload) != TYPE_DICTIONARY:
		return _failure("InvalidPayload", "payload debe ser un objeto JSON o estar ausente.")
	if payload != null and (envelope["type"] == "Ping" or envelope["type"] == "Pong"):
		if payload.has("pingId") and _is_integer_number(payload["pingId"]):
			payload["pingId"] = int(payload["pingId"])
	var payload_error := _validate_consumed_payload(envelope["type"], payload)
	if not payload_error.is_empty():
		return _failure("InvalidPayload", payload_error)

	return {
		"ok": true,
		"envelope": envelope,
		"payload": payload,
	}


static func _validate_consumed_payload(message_type: String, payload: Variant) -> String:
	if message_type == "Ping" or message_type == "Pong":
		if typeof(payload) != TYPE_DICTIONARY:
			return "%s requiere un payload objeto." % message_type
		if not payload.has("pingId") or not _is_integer_number(payload["pingId"]):
			return "%s.payload.pingId debe ser un número entero." % message_type
	elif message_type == "ProtocolError":
		if typeof(payload) != TYPE_DICTIONARY:
			return "ProtocolError requiere un payload objeto."
		if not payload.has("error") or typeof(payload["error"]) != TYPE_STRING:
			return "ProtocolError.payload.error debe ser un string."
		if payload.has("detail") and payload["detail"] != null and typeof(payload["detail"]) != TYPE_STRING:
			return "ProtocolError.payload.detail debe ser un string o null."
		if (
			payload.has("serverProtocolVersion")
			and payload["serverProtocolVersion"] != null
			and not _is_integer_number(payload["serverProtocolVersion"])
		):
			return "ProtocolError.payload.serverProtocolVersion debe ser un número entero o null."
	elif message_type == "Disconnecting":
		if typeof(payload) != TYPE_DICTIONARY:
			return "Disconnecting requiere un payload objeto."
		if not payload.has("reason") or typeof(payload["reason"]) != TYPE_STRING:
			return "Disconnecting.payload.reason debe ser un string."

	return ""


static func _is_valid_message_id(value: Variant) -> bool:
	return (
		_is_integer_number(value)
		and value >= 1
		and value <= MAX_SAFE_MESSAGE_ID
	)


static func _is_integer_number(value: Variant) -> bool:
	if typeof(value) == TYPE_INT:
		return true
	return typeof(value) == TYPE_FLOAT and is_finite(value) and floor(value) == value


static func _failure(error: String, detail: String) -> Dictionary:
	return {
		"ok": false,
		"error": error,
		"detail": detail,
	}
