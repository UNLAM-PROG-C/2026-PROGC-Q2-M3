extends Control

## Panel de opciones: volumen de la música y de los sonidos (AudioSettings).
## Los cambios se oyen al instante y se guardan al soltar el slider y al cerrar.
## Se usa en el menú principal y, como menú de pausa, durante la partida (MatchPause). En la
## partida muestra además el botón "Ayuda", que abre el panel de controles (help_menu.gd).

signal closed

const UI_THEME := preload("res://themes/rts_theme.tres")
const HELP_MENU_SCRIPT := preload("res://scripts/ui/help_menu.gd")

@export var title_text := "Opciones"
@export var back_text := "Volver"
## Muestra el botón "Ayuda" (menú de pausa).
@export var show_help_button := false

const VOLUME_ROWS := [
	{"bus": "Music", "label": "Música", "name": "MusicSlider"},
	{"bus": "Sfx", "label": "Sonidos", "name": "SfxSlider"},
]

var _settings: Node
var _sliders: Dictionary = {}
var _value_labels: Dictionary = {}
var _back_button: Button
var _content: Control
var _help_button: Button
var _help_menu: Control


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP
	if theme == null:
		theme = UI_THEME
	_settings = get_node_or_null("/root/AudioSettings")
	_build_ui()
	visible = false


func open() -> void:
	for row: Dictionary in VOLUME_ROWS:
		var volume: float = _settings.get_volume(row["bus"]) if _settings != null else 1.0
		(_sliders[row["bus"]] as HSlider).set_value_no_signal(roundf(volume * 100.0))
		_refresh_value_label(row["bus"])
	visible = true
	_content.visible = true
	if _help_menu != null:
		_help_menu.visible = false
	_back_button.grab_focus()


func close() -> void:
	if not visible:
		return
	if _settings != null:
		_settings.save_settings()
	if _help_menu != null:
		_help_menu.visible = false
	visible = false
	closed.emit()


func open_help() -> void:
	if _help_menu == null:
		return
	_content.visible = false
	_help_menu.open()


func _on_help_closed() -> void:
	_content.visible = true
	_help_button.grab_focus()


func _unhandled_input(event: InputEvent) -> void:
	# Con la ayuda abierta, Escape la cierra a ella (help_menu.gd) y vuelve a este panel.
	if not visible or (_help_menu != null and _help_menu.visible):
		return
	if event.is_action_pressed("ui_cancel") or event.is_action_pressed("pause_menu"):
		close()
		get_viewport().set_input_as_handled()
	elif event is InputEventKey:
		# Con el panel abierto, las teclas no llegan a los atajos de la partida (Q, 1 a 4…).
		get_viewport().set_input_as_handled()


func _on_volume_changed(value: float, bus_name: String) -> void:
	if _settings != null:
		_settings.set_volume(bus_name, value / 100.0, false)
	_refresh_value_label(bus_name)


func _on_volume_drag_ended(_value_changed: bool, bus_name: String) -> void:
	if _settings != null:
		_settings.save_settings()
	# Al soltar el slider de sonidos se escucha un ejemplo con el volumen nuevo.
	if bus_name == "Sfx":
		var sounds := get_node_or_null("/root/SoundManager")
		if sounds != null:
			sounds.play_ui("select")


func _refresh_value_label(bus_name: String) -> void:
	(_value_labels[bus_name] as Label).text = "%d%%" % int((_sliders[bus_name] as HSlider).value)


func _build_ui() -> void:
	var backdrop := ColorRect.new()
	backdrop.name = "Backdrop"
	backdrop.color = Color(0.05, 0.07, 0.1, 0.6)
	backdrop.mouse_filter = Control.MOUSE_FILTER_IGNORE
	backdrop.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(backdrop)
	var center := CenterContainer.new()
	center.name = "Content"
	_content = center
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)
	var panel := PanelContainer.new()
	panel.name = "OptionsPanel"
	panel.custom_minimum_size = Vector2(480, 0)
	center.add_child(panel)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 16)
	panel.add_child(box)

	var title := Label.new()
	title.name = "Title"
	title.text = title_text
	title.theme_type_variation = &"TitleLabel"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)

	for row: Dictionary in VOLUME_ROWS:
		var line := HBoxContainer.new()
		line.add_theme_constant_override("separation", 14)
		box.add_child(line)
		var label := Label.new()
		label.text = row["label"]
		label.custom_minimum_size = Vector2(110, 0)
		line.add_child(label)
		var slider := HSlider.new()
		slider.name = row["name"]
		slider.min_value = 0
		slider.max_value = 100
		slider.step = 1
		slider.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		slider.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		slider.value_changed.connect(_on_volume_changed.bind(row["bus"]))
		slider.drag_ended.connect(_on_volume_drag_ended.bind(row["bus"]))
		line.add_child(slider)
		var value_label := Label.new()
		value_label.name = "%sValue" % row["name"]
		value_label.custom_minimum_size = Vector2(64, 0)
		value_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
		line.add_child(value_label)
		_sliders[row["bus"]] = slider
		_value_labels[row["bus"]] = value_label

	if show_help_button:
		_help_button = Button.new()
		_help_button.name = "HelpButton"
		_help_button.text = "Ayuda"
		_help_button.custom_minimum_size = Vector2(200, 56)
		_help_button.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		_help_button.pressed.connect(open_help)
		box.add_child(_help_button)
		_help_menu = HELP_MENU_SCRIPT.new()
		_help_menu.name = "HelpMenu"
		_help_menu.closed.connect(_on_help_closed)
		add_child(_help_menu)

	_back_button = Button.new()
	_back_button.name = "BackButton"
	_back_button.text = back_text
	_back_button.custom_minimum_size = Vector2(200, 56)
	_back_button.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	_back_button.pressed.connect(close)
	box.add_child(_back_button)
