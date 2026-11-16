extends Control

## Pantallas fuera de la partida: conexión al servidor, espera del rival y carga del mundo,
## `StartAborted`, `MatchEnded` y cierre de conexión (work-plan §2, contrato 07 §7).
## Durante `Running` no se muestra nada y no intercepta el mouse.

signal connect_requested(host: String, port: int, client_name: String)
signal returned_to_menu
signal main_menu_requested

## Valores temporales de server/config/server.example.json; se pueden cambiar en la pantalla.
const DEFAULT_HOST := "127.0.0.1"
const DEFAULT_PORT := 7777
const DEFAULT_CLIENT_NAME := "godot-agus"
const MAIN_MENU_SCENE_PATH := "res://scenes/main_menu.tscn"

const PLAYER_COLORS := {1: "azul", 2: "rojo"}
## Tema común de la interfaz: Magic Retro para títulos (variación TitleLabel) y Bremlin
## para el resto, con los paneles y botones de assets/ui.
const UI_THEME := preload("res://themes/rts_theme.tres")

const START_ABORT_TEXTS := {
	"OpponentLeft": "El rival se desconectó antes de empezar. Esperando a otro rival…",
	"ReadyTimeout": "El rival no confirmó a tiempo. Esperando a otro rival…",
}

const MATCH_END_REASON_TEXTS := {
	"BaseDestroyed": "Se destruyó un Centro Principal.",
	"BothBasesDestroyed": "Ambos Centros Principales cayeron en el mismo tick.",
	"PlayerDisconnected": "El rival se desconectó.",
	"BothPlayersDisconnected": "Ambos jugadores se desconectaron.",
	"CriticalError": "El servidor tuvo un error crítico.",
	"ServerShutdown": "El servidor se apagó.",
}

## Motivos de cierre: los del servidor (`Disconnecting`, contrato 08) y los locales de NetworkClient.
const DISCONNECT_REASON_TEXTS := {
	"Network.UnsupportedVersion": "El servidor usa otra versión del protocolo.",
	"Network.HandshakeTimeout": "El servidor no recibió el saludo a tiempo.",
	"Network.TooManyConnections": "El servidor tiene demasiadas conexiones pendientes.",
	"Network.SessionBusy": "El servidor está ocupado. Intentá de nuevo.",
	"Network.MessageTooLarge": "Se envió un mensaje demasiado grande.",
	"Network.AbuseDetected": "El servidor cortó la conexión por mensajes inválidos repetidos.",
	"Network.OutboundSaturated": "La conexión no pudo seguir el ritmo del servidor.",
	"Network.HeartbeatTimeout": "El servidor dejó de recibir respuestas de este cliente.",
	"Network.ServerShuttingDown": "El servidor se está apagando.",
	"Session.MatchFull": "La partida ya tiene dos jugadores.",
	"Session.ReadyTimeoutExpired": "No se confirmó la carga del mundo a tiempo.",
	"ConnectionFailed": "No se pudo conectar al servidor.",
	"ServerNotResponding": "El servidor dejó de responder.",
	"ConnectionLost": "Se perdió la conexión con el servidor.",
	"RemoteClosed": "El servidor cerró la conexión.",
	"SocketError": "Error en la conexión con el servidor.",
	"SocketPollFailed": "Error en la conexión con el servidor.",
	"SocketReadFailed": "Error al leer del servidor.",
	"SocketWriteFailed": "Error al escribir al servidor.",
	"ProtocolViolation": "El servidor envió un mensaje inválido.",
	"MessageTooLarge": "El servidor envió un mensaje demasiado grande.",
}

var current_screen := ""

var _session_controller: Node
var _network_client: Node
var _network_state := "Disconnected"
var _endpoint_text := ""
var _start_aborted_notice := ""
var _server_protocol_version: Variant = null
var _returning_to_menu := false
## Si la conexión TCP llegó a establecerse en este intento: un cierre antes de eso es
## "no se pudo conectar", sin importar el código que informe el socket.
var _reached_server := false

var _connect_panel: PanelContainer
var _host_input: LineEdit
var _port_input: SpinBox
var _name_input: LineEdit
var _connect_button: Button
var _main_menu_button: Button
var _connect_error_label: Label

var _status_panel: PanelContainer
var _status_title: Label
var _status_detail: Label
var _cancel_button: Button

var _result_panel: PanelContainer
var _result_title: Label
var _result_detail: Label
var _menu_button: Button


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	if theme == null:
		theme = UI_THEME
	_build_ui()
	if _session_controller == null:
		var session := get_node_or_null("/root/SessionController")
		var network := get_node_or_null("/root/NetworkClient")
		if session != null and network != null:
			bind(session, network)
	_refresh()


## Enlaza las pantallas con la sesión y la red. En el juego son los autoloads;
## en las pruebas, dobles.
func bind(session_controller: Node, network_client: Node) -> void:
	_session_controller = session_controller
	_network_client = network_client
	var session_callbacks := {
		"session_state_changed": _on_session_state_changed,
		"player_assigned": _on_player_assigned,
		"start_aborted": _on_start_aborted,
		"connection_disconnected": _on_connection_disconnected,
		"match_ended": _on_match_ended,
		"session_reset": _on_session_reset,
	}
	for signal_name: String in session_callbacks:
		_connect_if_present(_session_controller, signal_name, session_callbacks[signal_name])
	var network_callbacks := {
		"connection_state_changed": _on_network_state_changed,
		"message_received": _on_network_message_received,
	}
	for signal_name: String in network_callbacks:
		_connect_if_present(_network_client, signal_name, network_callbacks[signal_name])
	var state: Variant = _network_client.get("connection_state")
	_network_state = _network_state_name(int(state) if state != null else 0)
	if is_inside_tree():
		_refresh()


## Pide la conexión con los datos de la pantalla. El `Hello` lo manda NetworkClient al conectar.
func request_connection(host: String, port: int, client_name: String) -> bool:
	_set_connect_error("")
	if _network_client == null or _session_controller == null:
		_set_connect_error("El cliente no tiene red ni sesión configuradas.")
		return false
	if _session_state_name() != "AwaitingAssignment" and not _session_controller.call("reset_session"):
		_set_connect_error("No se pudo reiniciar la sesión anterior.")
		return false
	var trimmed_host := host.strip_edges()
	if trimmed_host.is_empty() or port < 1 or port > 65535:
		_set_connect_error("Ingresá un host y un puerto entre 1 y 65535.")
		return false
	_endpoint_text = "%s:%d" % [trimmed_host, port]
	_reached_server = false
	_start_aborted_notice = ""
	_server_protocol_version = null
	var error: Variant = _network_client.call("connect_to_server", trimmed_host, port, client_name.strip_edges())
	if error != OK:
		_set_connect_error("No se pudo iniciar la conexión (%s)." % error_string(int(error)))
		return false
	connect_requested.emit(trimmed_host, port, client_name.strip_edges())
	_refresh()
	return true


## Corta la conexión si sigue abierta, reinicia la sesión local y vuelve a la pantalla de conexión.
func return_to_menu() -> void:
	if _returning_to_menu or _network_client == null or _session_controller == null:
		return
	_returning_to_menu = true
	if _network_state != "Disconnected":
		_network_client.call("close_connection", "ApplicationRequested")
		while _network_state != "Disconnected":
			await _network_client.connection_state_changed
	_session_controller.call("reset_session")
	_returning_to_menu = false
	_start_aborted_notice = ""
	_refresh()
	returned_to_menu.emit()


func _refresh() -> void:
	if _connect_panel == null:
		return
	var screen := _resolve_screen()
	current_screen = screen
	_connect_panel.visible = screen == "connect"
	_status_panel.visible = screen == "status"
	_result_panel.visible = screen == "result"
	# Fuera de la partida, el fondo bloquea los clics al mundo; en Running los deja pasar.
	mouse_filter = Control.MOUSE_FILTER_IGNORE if screen == "" else Control.MOUSE_FILTER_STOP
	visible = screen != ""
	match screen:
		"connect":
			_connect_button.disabled = false
			_main_menu_button.disabled = false
			_connect_error_label.visible = not _connect_error_label.text.is_empty()
		"status":
			_fill_status()
		"result":
			_fill_result()


func _resolve_screen() -> String:
	var session_state := _session_state_name()
	match session_state:
		"Running":
			return ""
		"Ended", "Disconnected":
			return "result"
	if session_state == "AwaitingAssignment" and _network_state == "Disconnected":
		return "connect"
	return "status"


func _fill_status() -> void:
	var player_id := _player_id()
	var identity := ""
	if player_id > 0:
		identity = "Sos el jugador %d (%s)." % [player_id, PLAYER_COLORS.get(player_id, "?")]
	_cancel_button.visible = true
	match _session_state_name():
		"AwaitingAssignment":
			if _network_state == "Connecting":
				_status_title.text = "Conectando…"
				_status_detail.text = "Conectando a %s." % _endpoint_text
			else:
				_status_title.text = "Conectado"
				_status_detail.text = "Esperando que el servidor asigne una plaza."
		"WaitingForOpponent":
			_status_title.text = "Esperando al rival"
			_status_detail.text = _join_lines([identity, _start_aborted_notice])
		"Preparing":
			_status_title.text = "Cargando el mundo"
			_status_detail.text = identity
		"ReadySent":
			_status_title.text = "Listo"
			_status_detail.text = _join_lines([identity, "Esperando que el rival termine de cargar."])
		_:
			_status_title.text = ""
			_status_detail.text = ""


func _fill_result() -> void:
	_menu_button.visible = true
	if _session_state_name() == "Ended":
		var result: Dictionary = _session_controller.get("final_result")
		_result_title.text = match_result_title(result, _player_id())
		_result_detail.text = MATCH_END_REASON_TEXTS.get(str(result.get("reason", "")), "")
		return
	var reason := str(_session_controller.call("get_disconnect_reason"))
	if not _reached_server:
		_result_title.text = "No se pudo conectar"
		_result_detail.text = "No hay un servidor disponible en %s. Revisá el host, el puerto y que el servidor esté corriendo." % _endpoint_text
		return
	_result_title.text = "Conexión cerrada"
	var detail := disconnect_reason_text(reason)
	if reason == "Network.UnsupportedVersion" and _server_protocol_version != null:
		detail += " Versión del servidor: %d." % int(_server_protocol_version)
	_result_detail.text = detail


## Título del resultado oficial visto por este jugador (contrato 07 §6).
static func match_result_title(result: Dictionary, own_player_id: int) -> String:
	match str(result.get("resultType", "")):
		"Victory":
			var winner: Variant = result.get("winner", null)
			if winner != null and int(winner) == own_player_id:
				return "¡Victoria!"
			return "Derrota"
		"Draw":
			return "Empate"
	return "Partida terminada"


static func disconnect_reason_text(reason: String) -> String:
	if DISCONNECT_REASON_TEXTS.has(reason):
		return DISCONNECT_REASON_TEXTS[reason]
	if reason.is_empty():
		return "Se cerró la conexión con el servidor."
	return "Se cerró la conexión (%s)." % reason


func _on_session_state_changed(_state: int, _state_name: String) -> void:
	_refresh()


func _on_player_assigned(_session_id: String, _player_id_value: int) -> void:
	_refresh()


func _on_start_aborted(_initialization_id: int, reason: String) -> void:
	_start_aborted_notice = START_ABORT_TEXTS.get(reason, "Se canceló el inicio de la partida.")
	_refresh()


func _on_connection_disconnected(_reason: String) -> void:
	_refresh()


func _on_match_ended(_result: Dictionary) -> void:
	_refresh()


func _on_session_reset() -> void:
	_refresh()


func _on_network_state_changed(state_name: String) -> void:
	_network_state = state_name
	if state_name == "Connected":
		_reached_server = true
	_refresh()


func _on_network_message_received(message_type: String, payload: Variant, _envelope: Dictionary) -> void:
	if message_type == "ProtocolError" and payload is Dictionary:
		_server_protocol_version = payload.get("serverProtocolVersion", _server_protocol_version)


func _set_connect_error(text: String) -> void:
	_connect_error_label.text = text
	_connect_error_label.visible = not text.is_empty()


func _on_connect_pressed() -> void:
	_connect_button.disabled = true
	if not request_connection(_host_input.text, int(_port_input.value), _name_input.text):
		_connect_button.disabled = false


## Vuelve a la escena del menú principal. Solo se ofrece en la pantalla de conexión,
## cuando no hay conexión activa ni sesión en curso.
func go_to_main_menu() -> bool:
	if current_screen != "connect":
		return false
	main_menu_requested.emit()
	var error := get_tree().change_scene_to_file(MAIN_MENU_SCENE_PATH)
	if error != OK:
		_set_connect_error("No se pudo abrir el menú principal (%s)." % error_string(error))
		return false
	return true


func _on_main_menu_pressed() -> void:
	go_to_main_menu()


func _on_cancel_pressed() -> void:
	await return_to_menu()


func _on_menu_pressed() -> void:
	await return_to_menu()


func _session_state_name() -> String:
	if _session_controller == null:
		return "AwaitingAssignment"
	return str(_session_controller.call("get_state_name"))


func _player_id() -> int:
	if _session_controller == null:
		return 0
	return int(_session_controller.get("player_id"))


func _network_state_name(state: int) -> String:
	match state:
		1:
			return "Connecting"
		2:
			return "Connected"
		3:
			return "Closing"
	return "Disconnected"


func _join_lines(lines: Array) -> String:
	var kept: PackedStringArray = []
	for line: Variant in lines:
		if not str(line).is_empty():
			kept.append(str(line))
	return "\n".join(kept)


func _connect_if_present(source: Object, signal_name: String, callback: Callable) -> void:
	if source == null or not source.has_signal(signal_name):
		return
	if not source.is_connected(signal_name, callback):
		source.connect(signal_name, callback)


# --- Construcción de la interfaz -------------------------------------------------------

func _build_ui() -> void:
	if _connect_panel != null:
		return
	var backdrop := ColorRect.new()
	backdrop.name = "Backdrop"
	backdrop.color = Color(0.05, 0.07, 0.1, 0.72)
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(backdrop)

	var center := CenterContainer.new()
	center.name = "Center"
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)

	_connect_panel = _make_panel("ConnectPanel", center)
	var connect_box := _make_box(_connect_panel)
	connect_box.add_child(_make_title("Conectar al servidor"))
	_host_input = _make_line_edit("HostInput", DEFAULT_HOST)
	_port_input = SpinBox.new()
	_port_input.name = "PortInput"
	_port_input.min_value = 1
	_port_input.max_value = 65535
	_port_input.step = 1
	_port_input.value = DEFAULT_PORT
	_name_input = _make_line_edit("NameInput", DEFAULT_CLIENT_NAME)
	connect_box.add_child(_make_field("Host", _host_input))
	connect_box.add_child(_make_field("Puerto", _port_input))
	connect_box.add_child(_make_field("Nombre", _name_input))
	_connect_button = _make_button("ConnectButton", "Conectar", _on_connect_pressed)
	connect_box.add_child(_connect_button)
	_main_menu_button = _make_button("MainMenuButton", "Menú principal", _on_main_menu_pressed)
	connect_box.add_child(_main_menu_button)
	_connect_error_label = _make_detail("ConnectError")
	_connect_error_label.add_theme_color_override("font_color", Color(1.0, 0.62, 0.5))
	_connect_error_label.visible = false
	connect_box.add_child(_connect_error_label)
	_host_input.text_submitted.connect(func(_text: String) -> void: _on_connect_pressed())

	_status_panel = _make_panel("StatusPanel", center)
	var status_box := _make_box(_status_panel)
	_status_title = _make_title("")
	_status_detail = _make_detail("StatusDetail")
	status_box.add_child(_status_title)
	status_box.add_child(_status_detail)
	_cancel_button = _make_button("CancelButton", "Cancelar", _on_cancel_pressed)
	status_box.add_child(_cancel_button)

	_result_panel = _make_panel("ResultPanel", center)
	var result_box := _make_box(_result_panel)
	_result_title = _make_title("")
	_result_title.add_theme_font_size_override("font_size", 46)
	_result_detail = _make_detail("ResultDetail")
	result_box.add_child(_result_title)
	result_box.add_child(_result_detail)
	_menu_button = _make_button("MenuButton", "Volver atrás", _on_menu_pressed)
	result_box.add_child(_menu_button)


func _make_panel(panel_name: String, parent: Control) -> PanelContainer:
	var panel := PanelContainer.new()
	panel.name = panel_name
	panel.custom_minimum_size = Vector2(420, 0)
	panel.visible = false
	parent.add_child(panel)
	return panel


func _make_box(panel: PanelContainer) -> VBoxContainer:
	var margin := MarginContainer.new()
	for side in ["margin_left", "margin_right", "margin_top", "margin_bottom"]:
		margin.add_theme_constant_override(side, 4)
	panel.add_child(margin)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 10)
	margin.add_child(box)
	return box


func _make_title(text: String) -> Label:
	var label := Label.new()
	label.name = "Title"
	label.text = text
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.theme_type_variation = &"TitleLabel"
	return label


func _make_detail(label_name: String) -> Label:
	var label := Label.new()
	label.name = label_name
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	label.custom_minimum_size = Vector2(360, 0)
	return label


func _make_line_edit(input_name: String, text: String) -> LineEdit:
	var input := LineEdit.new()
	input.name = input_name
	input.text = text
	input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	return input


func _make_field(label_text: String, input: Control) -> HBoxContainer:
	var row := HBoxContainer.new()
	var label := Label.new()
	label.text = label_text
	label.custom_minimum_size = Vector2(96, 0)
	row.add_child(label)
	input.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	row.add_child(input)
	return row


func _make_button(button_name: String, text: String, callback: Callable) -> Button:
	var button := Button.new()
	button.name = button_name
	button.text = text
	button.pressed.connect(callback)
	return button
