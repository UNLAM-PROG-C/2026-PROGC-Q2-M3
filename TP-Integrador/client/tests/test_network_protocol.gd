extends SceneTree

const Decoder = preload("res://scripts/network/line_frame_decoder.gd")
const Codec = preload("res://scripts/network/protocol_codec.gd")
const Client = preload("res://scripts/network/network_client.gd")

var _failed := false


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	_test_split_frame()
	_test_multiple_frames_and_crlf()
	_test_oversized_frame()
	_test_codec_round_trip()
	_test_invalid_messages()
	await _test_tcp_control_flow()
	if _failed:
		quit(1)
		return
	print("Network protocol tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _test_split_frame() -> void:
	var decoder = Decoder.new(128)
	var first := decoder.feed("{\"type\":\"Pi".to_utf8_buffer())
	_check(first["frames"].is_empty(), "A partial line must stay buffered.")
	_check(first["error"].is_empty(), "A partial line must not be an error.")

	var second := decoder.feed("ng\"}\n".to_utf8_buffer())
	_check(second["frames"].size() == 1, "The partial line must become one frame.")
	if second["frames"].size() == 1:
		_check(second["frames"][0].get_string_from_utf8() == "{\"type\":\"Ping\"}", "The reconstructed frame differs.")


func _test_multiple_frames_and_crlf() -> void:
	var decoder = Decoder.new(128)
	var result := decoder.feed("\r\n{\"one\":1}\r\n{\"two\":2}\n{\"three\"".to_utf8_buffer())
	_check(result["error"].is_empty(), "CRLF and empty lines should be accepted.")
	_check(result["frames"].size() == 2, "Two complete frames should be emitted.")
	if result["frames"].size() == 2:
		_check(result["frames"][0].get_string_from_utf8() == "{\"one\":1}", "The first frame changed.")
		_check(result["frames"][1].get_string_from_utf8() == "{\"two\":2}", "The second frame changed.")

	var remainder := decoder.feed(":3}\n".to_utf8_buffer())
	_check(remainder["frames"].size() == 1, "The buffered remainder should become a frame.")
	if remainder["frames"].size() == 1:
		_check(remainder["frames"][0].get_string_from_utf8() == "{\"three\":3}", "The remainder was not preserved.")


func _test_oversized_frame() -> void:
	var decoder = Decoder.new(4)
	var result := decoder.feed("12345".to_utf8_buffer())
	_check(result["error"] == "MessageTooLarge", "An over-limit line must be reported.")
	_check(decoder.feed(PackedByteArray())["error"] == "MessageTooLarge", "An oversized buffer must stay rejected.")


func _test_codec_round_trip() -> void:
	var encoded := Codec.encode_message("Hello", {"clientName": "godot-agus"})
	_check(encoded["ok"], "Hello should serialize.")
	var bytes: PackedByteArray = encoded["bytes"]
	_check(bytes[bytes.size() - 1] == 10, "A serialized message must end with LF.")
	_check(bytes.slice(0, bytes.size() - 1).find(10) == -1, "A serialized message must contain only one LF.")

	var parsed := Codec.parse_line(bytes.slice(0, bytes.size() - 1))
	_check(parsed["ok"], "Hello failed to parse: %s" % str(parsed))
	if parsed["ok"]:
		_check(parsed["envelope"]["protocolVersion"] == 1, "The parsed protocol version changed.")
		_check(parsed["envelope"]["type"] == "Hello", "The parsed message type changed.")
		_check(parsed["payload"]["clientName"] == "godot-agus", "The parsed clientName changed.")

	var payload_omitted := Codec.encode_message("Pong")
	_check(
		not payload_omitted["bytes"].get_string_from_utf8().contains("\"payload\""),
		"Null payloads should be omitted."
	)
	_check(not Codec.encode_message("Unknown").get("ok", false), "Unknown outgoing types must fail to serialize.")
	_check(
		not Codec.encode_message("Hello", "wrong-shape").get("ok", false),
		"Non-object outgoing payloads must fail to serialize."
	)


func _test_invalid_messages() -> void:
	_check(not Codec.parse_line(PackedByteArray([255]))["ok"], "Invalid UTF-8 must fail.")
	_check(not Codec.parse_line("{".to_utf8_buffer())["ok"], "Malformed JSON must fail.")
	_check(
		not Codec.parse_line("{\"protocolVersion\":2,\"type\":\"Ping\"}".to_utf8_buffer())["ok"],
		"An incompatible protocol version must fail."
	)
	_check(not Codec.parse_line("{\"protocolVersion\":1}".to_utf8_buffer())["ok"], "An incomplete envelope must fail.")
	_check(
		not Codec.parse_line(
			"{\"protocolVersion\":1,\"type\":\"InitialState\",\"payload\":\"wrong-shape\"}".to_utf8_buffer()
		)["ok"],
		"A non-object payload must fail."
	)
	_check(
		not Codec.parse_line(
			"{\"protocolVersion\":1,\"type\":\"Ping\",\"payload\":{\"pingId\":\"bad\"}}".to_utf8_buffer()
		)["ok"],
		"A non-integer pingId must fail."
	)
	_check(
		Codec.parse_line(
			"{\"protocolVersion\":1,\"type\":\"Move\",\"messageId\":1,\"payload\":{}}".to_utf8_buffer()
		)["ok"],
		"messageId 1 should be accepted."
	)
	_check(
		not Codec.parse_line(
			"{\"protocolVersion\":1,\"type\":\"Move\",\"messageId\":0,\"payload\":{}}".to_utf8_buffer()
		)["ok"],
		"messageId 0 must fail."
	)

	var valid_ping := Codec.parse_line(
		"{\"protocolVersion\":1,\"type\":\"Ping\",\"payload\":{\"pingId\":7}}".to_utf8_buffer()
	)
	_check(valid_ping["ok"], "A valid Ping failed to parse: %s" % str(valid_ping))
	if valid_ping["ok"]:
		_check(valid_ping["payload"]["pingId"] == 7, "The Ping ID should be normalized to an integer.")
	_check(
		Codec.parse_line(
			"{\"protocolVersion\":1,\"type\":\"ProtocolError\",\"payload\":{\"error\":\"Network.MalformedJson\"}}"
			.to_utf8_buffer()
		)["ok"],
		"A valid ProtocolError failed to parse."
	)
	_check(
		Codec.parse_line(
			"{\"protocolVersion\":1,\"type\":\"Disconnecting\",\"payload\":{\"reason\":\"Network.ServerShuttingDown\"}}"
			.to_utf8_buffer()
		)["ok"],
		"A valid Disconnecting message failed to parse."
	)


func _test_tcp_control_flow() -> void:
	var server := TCPServer.new()
	var listen_error := server.listen(0, "127.0.0.1")
	_check(listen_error == OK, "The loopback test server failed to listen.")
	if listen_error != OK:
		return

	var client = Client.new()
	root.add_child(client)
	var received_types: Array[String] = []
	var errors: Array[String] = []
	var closed_reasons: Array[String] = []
	var correlated_pongs: Array[int] = []
	client.message_received.connect(
		func(message_type: String, _payload: Variant, _envelope: Dictionary) -> void:
			received_types.append(message_type)
	)
	client.protocol_error_received.connect(
		func(error_code: String, _detail: String) -> void:
			errors.append(error_code)
	)
	client.connection_closed.connect(
		func(reason: String) -> void:
			closed_reasons.append(reason)
	)
	client.pong_received.connect(
		func(ping_id: int, correlated: bool) -> void:
			if correlated:
				correlated_pongs.append(ping_id)
	)

	var connect_error: Error = client.connect_to_server("127.0.0.1", server.get_local_port(), "protocol-test")
	_check(connect_error == OK, "The client failed to start its TCP connection.")
	if connect_error != OK:
		server.stop()
		client.queue_free()
		return

	var server_peer: StreamPeerTCP
	for _attempt in range(200):
		if server.is_connection_available():
			server_peer = server.take_connection()
			break
		await process_frame
	_check(server_peer != null, "The loopback server did not receive the client connection.")
	if server_peer == null:
		client.close_connection("TestCleanup")
		server.stop()
		client.queue_free()
		return

	var decoder = Decoder.new()
	var hello_frames: Array = await _read_frames(server_peer, decoder, 1)
	_check(hello_frames.size() == 1, "The client did not send exactly one Hello first.")
	if hello_frames.size() == 1:
		var hello := Codec.parse_line(hello_frames[0])
		_check(hello["ok"], "The first client message was not valid JSON.")
		if hello["ok"]:
			_check(hello["envelope"]["type"] == "Hello", "The first application message was not Hello.")
			_check(
				hello["payload"]["clientName"] == "protocol-test",
				"Hello did not use the requested client name."
			)

	_check(client.send_message("Move", {}, 1), "A gameplay message with messageId 1 should be queued.")
	var move_frames: Array = await _read_frames(server_peer, decoder, 1)
	_check(move_frames.size() == 1, "The server did not receive the gameplay message.")
	if move_frames.size() == 1:
		var move := Codec.parse_line(move_frames[0])
		_check(move["ok"], "The gameplay message was invalid.")
		if move["ok"]:
			_check(move["envelope"]["type"] == "Move", "The wrong gameplay message was sent.")
			_check(move["envelope"]["messageId"] == 1, "The gameplay messageId changed.")

	var ping_bytes: PackedByteArray = Codec.encode_message("Ping", {"pingId": 13})["bytes"]
	_check(server_peer.put_data(ping_bytes) == OK, "The server could not send Ping.")
	var pong_frames: Array = await _read_frames(server_peer, decoder, 1)
	_check(pong_frames.size() == 1, "The client did not answer the server Ping.")
	if pong_frames.size() == 1:
		var pong := Codec.parse_line(pong_frames[0])
		_check(pong["ok"], "The client Pong was invalid.")
		if pong["ok"]:
			_check(pong["envelope"]["type"] == "Pong", "The client sent the wrong response to Ping.")
			_check(pong["payload"]["pingId"] == 13, "The client did not echo pingId.")

	_check(client.send_message("Ping", {"pingId": 17}), "The client failed to enqueue its Ping.")
	var client_ping_frames: Array = await _read_frames(server_peer, decoder, 1)
	_check(client_ping_frames.size() == 1, "The server did not receive the client Ping.")
	if client_ping_frames.size() == 1:
		var client_ping := Codec.parse_line(client_ping_frames[0])
		_check(client_ping["ok"], "The client Ping was invalid.")
		if client_ping["ok"]:
			_check(client_ping["envelope"]["type"] == "Ping", "The client sent the wrong message type.")

	var pong_response: PackedByteArray = Codec.encode_message("Pong", {"pingId": 17})["bytes"]
	_check(server_peer.put_data(pong_response) == OK, "The server could not send Pong.")
	for _attempt in range(60):
		if correlated_pongs.has(17):
			break
		await process_frame
	_check(correlated_pongs.has(17), "The client did not correlate the Pong.")

	var terminal_messages: PackedByteArray = Codec.encode_message(
		"ProtocolError",
		{"error": "Network.UnsupportedVersion", "serverProtocolVersion": 1}
	)["bytes"]
	terminal_messages.append_array(
		Codec.encode_message("Disconnecting", {"reason": "Network.UnsupportedVersion"})["bytes"]
	)
	_check(server_peer.put_data(terminal_messages) == OK, "The server could not send terminal messages.")
	for _attempt in range(60):
		if not closed_reasons.is_empty():
			break
		await process_frame
	_check(errors == ["Network.UnsupportedVersion"], "ProtocolError was not exposed exactly once.")
	_check(
		received_types.slice(-2) == ["ProtocolError", "Disconnecting"],
		"ProtocolError and Disconnecting were not delivered in order."
	)
	_check(
		closed_reasons == ["Network.UnsupportedVersion"],
		"The remote close was not reported exactly once with its reason."
	)

	server_peer.disconnect_from_host()
	server.stop()
	client.queue_free()


func _read_frames(peer: StreamPeerTCP, decoder: Variant, required_count: int) -> Array:
	var frames: Array = []
	for _attempt in range(200):
		peer.poll()
		var available := peer.get_available_bytes()
		if available > 0:
			var read_result: Array = peer.get_partial_data(available)
			if read_result[0] != OK:
				break
			var decoded: Dictionary = decoder.feed(read_result[1])
			frames.append_array(decoded["frames"])
			if frames.size() >= required_count:
				return frames
		await process_frame
	return frames
