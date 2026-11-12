extends RefCounted
class_name LineFrameDecoder

# TODO: Replace this provisional cap when MaxInboundMessageBytes is approved in docs/contracts/08-networking.md.
const DEFAULT_MAX_MESSAGE_BYTES := 1_048_576

var max_message_bytes: int
var _buffer := PackedByteArray()


func _init(limit_bytes: int = DEFAULT_MAX_MESSAGE_BYTES) -> void:
	max_message_bytes = limit_bytes


func feed(bytes: PackedByteArray) -> Dictionary:
	var frames: Array[PackedByteArray] = []
	_buffer.append_array(bytes)

	while true:
		var newline_index := _buffer.find(10)
		if newline_index < 0:
			if _buffer.size() > max_message_bytes:
				return _result(frames, "MessageTooLarge")
			break

		var frame := _buffer.slice(0, newline_index)
		_buffer = _buffer.slice(newline_index + 1)

		if not frame.is_empty() and frame[frame.size() - 1] == 13:
			frame = frame.slice(0, frame.size() - 1)

		if frame.size() > max_message_bytes:
			return _result(frames, "MessageTooLarge")
		if not frame.is_empty():
			frames.append(frame)

	return _result(frames, "")


func reset() -> void:
	_buffer.clear()


func _result(frames: Array[PackedByteArray], error: String) -> Dictionary:
	return {
		"frames": frames,
		"error": error,
	}
