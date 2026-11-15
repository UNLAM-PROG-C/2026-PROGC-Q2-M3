extends Control

## HUD de la partida (work-plan §2): recursos propios, edificio propio seleccionado con su
## cola de producción y entrenamiento (TrainUnit), y avisos de órdenes rechazadas.
## Solo se muestra en Running; los recursos y las colas son privados de este jugador (09 §3).

const ErrorTexts = preload("res://scripts/ui/error_texts.gd")
const UI_THEME := preload("res://themes/rts_theme.tres")
const WOOD_ICON := preload("res://assets/ui/resource_wood.svg")
const FOOD_ICON := preload("res://assets/ui/resource_food.svg")

const MESSAGE_SECONDS := 4.0
const MAX_MESSAGES := 4

const BUILDING_NAMES := {
	"MainCenter": "Centro Principal",
	"Barracks": "Cuartel",
	"Farm": "Granja",
	"Tower": "Torre",
	"Fence": "Valla",
}
const UNIT_NAMES := {"Worker": "Trabajador", "Soldier": "Soldado"}
const STATUS_TEXTS := {
	"Idle": "Sin entrenamientos.",
	"WaitingForResources": "Esperando comida para empezar.",
	"Training": "Entrenando %s.",
	"PendingSpawn": "%s listo: esperando lugar libre para aparecer.",
}

@export var world: Node
@export var command_controller: Node

var selected_building_id := 0

var _session_controller: Node
## Momento (Time.get_ticks_usec) en que llegó el último estado de cada productor, para
## interpolar el avance entre ProductionChanged, que solo llega en cambios discretos (D-042).
var _production_received_usec: Dictionary = {}

var _wood_label: Label
var _food_label: Label
var _resource_panel: PanelContainer
var _selection_panel: PanelContainer
var _selection_title: Label
var _selection_state: Label
var _production_box: VBoxContainer
var _production_status: Label
var _production_bar: ProgressBar
var _production_queue: Label
var _train_button: Button
var _food_hint: Label
var _messages: VBoxContainer


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	if theme == null:
		theme = UI_THEME
	_build_ui()
	if world != null:
		bind(world, command_controller, get_node_or_null("/root/SessionController"))
	_refresh_visibility()


func bind(world_node: Node, controller: Node, session_controller: Node) -> void:
	world = world_node
	command_controller = controller
	_session_controller = session_controller
	var world_callbacks := {
		"world_preparation_completed": _on_world_ready,
		"world_discarded": _on_world_discarded,
		"resources_changed": _on_resources_changed,
		"production_changed": _on_production_changed,
		"command_result_received": _on_command_result,
		"command_rejected_received": _on_command_rejected,
	}
	for signal_name: String in world_callbacks:
		_connect_if_present(world, signal_name, world_callbacks[signal_name])
	_connect_if_present(command_controller, "building_selection_changed", _on_building_selection_changed)
	_connect_if_present(command_controller, "command_failed", _on_command_failed)
	_connect_if_present(_session_controller, "session_state_changed", _on_session_state_changed)
	if world.get("active_world") != null:
		_on_world_ready(0)
	_refresh_visibility()


func _process(_delta: float) -> void:
	if _selection_panel != null and _selection_panel.visible:
		_refresh_selection()


## Pide entrenar en el edificio seleccionado. Sin comida la solicitud igual se encola y espera.
func train_selected() -> Dictionary:
	if world == null or selected_building_id == 0:
		return {"ok": false, "error": "EntityNotFound"}
	var trainable: Dictionary = world.trainable_unit_type(selected_building_id)
	var result: Dictionary = trainable
	if bool(trainable.get("ok", false)):
		result = world.issue_train_unit_command(selected_building_id, str(trainable["unitType"]))
	if not bool(result.get("ok", false)):
		show_message(ErrorTexts.text_for(str(result.get("error", "SendFailed"))))
	return result


func show_message(text: String) -> void:
	if _messages == null or text.is_empty():
		return
	var label := Label.new()
	label.text = text
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.add_theme_color_override("font_color", Color(1.0, 0.86, 0.6))
	label.add_theme_color_override("font_outline_color", Color(0.15, 0.08, 0.03))
	label.add_theme_constant_override("outline_size", 6)
	_messages.add_child(label)
	while _messages.get_child_count() > MAX_MESSAGES:
		var oldest := _messages.get_child(0)
		_messages.remove_child(oldest)
		oldest.queue_free()
	if is_inside_tree():
		get_tree().create_timer(MESSAGE_SECONDS).timeout.connect(
			func() -> void:
				if is_instance_valid(label):
					label.queue_free()
		)


func get_messages() -> PackedStringArray:
	var texts: PackedStringArray = []
	for child: Node in _messages.get_children():
		if child is Label and not child.is_queued_for_deletion():
			texts.append(child.text)
	return texts


## Avance de 0 a 1 de la tarea activa, interpolado desde el último ProductionChanged:
## RemainingTicks baja un tick cada 1 / ticksPerSecond segundos (D-037).
func production_progress(producer_id: int, now_usec: int = -1) -> float:
	var production: Dictionary = world.get_own_production(producer_id)
	var unit_type := str(production.get("activeUnitType", ""))
	if str(production.get("status", "")) == "PendingSpawn":
		return 1.0
	if str(production.get("status", "")) != "Training" or unit_type.is_empty():
		return 0.0
	var duration := float(_unit_catalog(unit_type).get("trainingDurationTicks", 0))
	if duration <= 0.0:
		return 0.0
	var ticks_per_second := float(world.active_catalog.get("ticksPerSecond", 20))
	var received: int = _production_received_usec.get(producer_id, Time.get_ticks_usec())
	var now := Time.get_ticks_usec() if now_usec < 0 else now_usec
	var elapsed_ticks := float(now - received) / 1_000_000.0 * ticks_per_second
	var remaining := maxf(float(production.get("remainingTicks", duration)) - elapsed_ticks, 0.0)
	return clampf(1.0 - remaining / duration, 0.0, 1.0)


func _refresh_visibility() -> void:
	var running := _session_controller == null or str(_session_controller.call("get_state_name")) == "Running"
	visible = running and world != null and world.get("active_world") != null
	if _selection_panel != null:
		_selection_panel.visible = visible and selected_building_id != 0
		if _selection_panel.visible:
			_refresh_selection()


func _refresh_resources() -> void:
	var resources: Dictionary = world.get_own_resources()
	_wood_label.text = str(int(resources.get("wood", 0)))
	_food_label.text = str(int(resources.get("food", 0)))


func _refresh_selection() -> void:
	var building: Node3D = world.entities_by_id.get(selected_building_id)
	if building == null:
		selected_building_id = 0
		_selection_panel.visible = false
		return
	var state: Dictionary = building.get_meta("entity_state", {})
	var building_type := str(state.get("buildingType", ""))
	_selection_title.text = BUILDING_NAMES.get(building_type, building_type)
	var max_hp := int(_building_catalog(building_type).get("maxHp", 0))
	var hp_text := "Vida: %d / %d" % [int(state.get("hp", 0)), max_hp]
	if not bool(state.get("isCompleted", false)):
		hp_text += "  ·  En construcción"
	elif bool(state.get("farm", {}).get("isRepairing", false)):
		hp_text += "  ·  En reparación"
	elif bool(state.get("farm", {}).get("isDepleted", false)):
		hp_text += "  ·  Agotada"
	_selection_state.text = hp_text

	var trainable: Dictionary = world.trainable_unit_type(selected_building_id)
	var is_producer: bool = world.PRODUCER_UNIT_TYPES.has(building_type)
	_production_box.visible = is_producer
	if not is_producer:
		return
	var production: Dictionary = world.get_own_production(selected_building_id)
	var status := str(production.get("status", "Idle"))
	var active_name: String = UNIT_NAMES.get(str(production.get("activeUnitType", "")), "")
	var status_text: String = STATUS_TEXTS.get(status, status)
	if status_text.contains("%s"):
		status_text = status_text % active_name
	if not bool(trainable.get("ok", false)):
		status_text = ErrorTexts.text_for(str(trainable.get("error", "")))
	_production_status.text = status_text
	_production_bar.visible = status == "Training" or status == "PendingSpawn"
	_production_bar.value = production_progress(selected_building_id) * 100.0
	var pending_names: PackedStringArray = []
	for unit_type: Variant in production.get("pending", []):
		pending_names.append(UNIT_NAMES.get(str(unit_type), str(unit_type)))
	_production_queue.text = "En cola: %s" % (", ".join(pending_names) if not pending_names.is_empty() else "nada")

	var unit_type := str(trainable.get("unitType", world.PRODUCER_UNIT_TYPES[building_type]))
	var food_cost := int(_unit_catalog(unit_type).get("cost", {}).get("food", 0))
	_train_button.text = "Entrenar %s (%d comida)" % [UNIT_NAMES.get(unit_type, unit_type), food_cost]
	_train_button.disabled = not bool(trainable.get("ok", false))
	var food := int(world.get_own_resources().get("food", 0))
	_food_hint.visible = bool(trainable.get("ok", false)) and food < food_cost
	_food_hint.text = "Sin comida suficiente: la orden esperará en la cola."


func _unit_catalog(unit_type: String) -> Dictionary:
	for entry: Variant in world.active_catalog.get("units", []):
		if entry is Dictionary and str(entry.get("unitType", "")) == unit_type:
			return entry
	return {}


func _building_catalog(building_type: String) -> Dictionary:
	for entry: Variant in world.active_catalog.get("buildings", []):
		if entry is Dictionary and str(entry.get("buildingType", "")) == building_type:
			return entry
	return {}


func _on_world_ready(_initialization_id: int) -> void:
	_production_received_usec.clear()
	var now := Time.get_ticks_usec()
	for producer: Variant in world.active_snapshot.get("ownProduction", []):
		if producer is Dictionary:
			_production_received_usec[int(producer.get("producerId", 0))] = now
	_refresh_resources()
	_refresh_visibility()


func _on_world_discarded(_initialization_id: int) -> void:
	selected_building_id = 0
	_production_received_usec.clear()
	_refresh_visibility()


func _on_resources_changed(_resources: Dictionary) -> void:
	_refresh_resources()


func _on_production_changed(producer_id: int, _production: Dictionary) -> void:
	_production_received_usec[producer_id] = Time.get_ticks_usec()


func _on_command_result(_client_command_id: int, result: Dictionary) -> void:
	var error := ErrorTexts.command_result_error(result)
	if not error.is_empty():
		show_message(ErrorTexts.text_for(error))


func _on_command_rejected(_message_id: int, error_code: String) -> void:
	show_message(ErrorTexts.text_for(error_code))


func _on_command_failed(error_code: String, _detail: String) -> void:
	show_message(ErrorTexts.text_for(error_code))


func _on_building_selection_changed(building_id: int) -> void:
	selected_building_id = building_id
	_refresh_visibility()


func _on_session_state_changed(_state: int, _state_name: String) -> void:
	_refresh_visibility()


func _connect_if_present(source: Object, signal_name: String, callback: Callable) -> void:
	if source == null or not source.has_signal(signal_name):
		return
	if not source.is_connected(signal_name, callback):
		source.connect(signal_name, callback)


# --- Construcción de la interfaz -------------------------------------------------------

func _build_ui() -> void:
	if _resource_panel != null:
		return
	_resource_panel = PanelContainer.new()
	_resource_panel.name = "ResourcePanel"
	_resource_panel.position = Vector2(16, 16)
	add_child(_resource_panel)
	var resources := HBoxContainer.new()
	resources.add_theme_constant_override("separation", 10)
	_resource_panel.add_child(resources)
	_wood_label = _add_resource(resources, WOOD_ICON, "WoodLabel", "Madera")
	_food_label = _add_resource(resources, FOOD_ICON, "FoodLabel", "Comida")

	_messages = VBoxContainer.new()
	_messages.name = "Messages"
	_messages.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_messages.set_anchors_and_offsets_preset(Control.PRESET_CENTER_TOP)
	_messages.offset_top = 24
	_messages.grow_horizontal = Control.GROW_DIRECTION_BOTH
	add_child(_messages)

	var bottom := MarginContainer.new()
	bottom.name = "Bottom"
	bottom.mouse_filter = Control.MOUSE_FILTER_IGNORE
	bottom.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	bottom.add_theme_constant_override("margin_bottom", 16)
	add_child(bottom)
	_selection_panel = PanelContainer.new()
	_selection_panel.name = "SelectionPanel"
	_selection_panel.custom_minimum_size = Vector2(420, 0)
	_selection_panel.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	_selection_panel.size_flags_vertical = Control.SIZE_SHRINK_END
	_selection_panel.visible = false
	bottom.add_child(_selection_panel)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 6)
	_selection_panel.add_child(box)
	_selection_title = Label.new()
	_selection_title.name = "Title"
	_selection_title.theme_type_variation = &"TitleLabel"
	_selection_title.add_theme_font_size_override("font_size", 28)
	_selection_title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(_selection_title)
	_selection_state = _make_label("StateLabel")
	box.add_child(_selection_state)

	_production_box = VBoxContainer.new()
	_production_box.name = "Production"
	_production_box.add_theme_constant_override("separation", 6)
	box.add_child(_production_box)
	_production_status = _make_label("StatusLabel")
	_production_box.add_child(_production_status)
	_production_bar = ProgressBar.new()
	_production_bar.name = "ProgressBar"
	_production_bar.show_percentage = false
	_production_bar.custom_minimum_size = Vector2(0, 18)
	_production_box.add_child(_production_bar)
	_production_queue = _make_label("QueueLabel")
	_production_box.add_child(_production_queue)
	_train_button = Button.new()
	_train_button.name = "TrainButton"
	_train_button.pressed.connect(func() -> void: train_selected())
	_production_box.add_child(_train_button)
	_food_hint = _make_label("FoodHint")
	_food_hint.add_theme_font_size_override("font_size", 16)
	_food_hint.visible = false
	_production_box.add_child(_food_hint)


func _add_resource(parent: HBoxContainer, icon: Texture2D, label_name: String, tooltip: String) -> Label:
	var icon_rect := TextureRect.new()
	icon_rect.texture = icon
	icon_rect.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon_rect.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon_rect.custom_minimum_size = Vector2(32, 32)
	icon_rect.tooltip_text = tooltip
	parent.add_child(icon_rect)
	var label := Label.new()
	label.name = label_name
	label.text = "0"
	label.custom_minimum_size = Vector2(56, 0)
	label.tooltip_text = tooltip
	parent.add_child(label)
	return label


func _make_label(label_name: String) -> Label:
	var label := Label.new()
	label.name = label_name
	label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	return label
