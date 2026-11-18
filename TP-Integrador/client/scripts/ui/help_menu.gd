extends Control

## Panel de ayuda: explica cómo mover la cámara y dar órdenes a las unidades. Se abre desde el
## botón "Ayuda" del menú de pausa (options_menu.gd). Los controles se leen del InputMap
## (input_labels.gd), así la ayuda sigue siendo correcta si se reasignan las teclas.

signal closed

const InputLabels = preload("res://scripts/ui/input_labels.gd")
const UI_THEME := preload("res://themes/rts_theme.tres")
const CONTROL_COLOR := Color(1, 0.85, 0.52, 1)
const TEXT_SIZE := 18
## Ancho fijo de la columna de controles, igual en todas las secciones para que queden alineadas.
## Los textos van en una sola línea (cortos a propósito).
const CONTROL_COLUMN_WIDTH := 250

## Secciones de la ayuda. Cada fila: acciones del InputMap (o un texto fijo en "control" para
## lo que no es una acción, como el borde de la pantalla) y qué hacen. "wheel": true junta un
## par de acciones de la rueda en un solo control ("Ctrl + Rueda").
const SECTIONS := [
	{
		"title": "Cámara",
		"rows": [
			{"actions": ["camera_up", "camera_left", "camera_down", "camera_right"], "text": "Mover la cámara."},
			{"control": "Mouse en el borde", "text": "Mover la cámara hacia ese lado."},
			{"actions": ["camera_sprinting"], "prefix": "Mantener ", "text": "Mover la cámara más rápido."},
			{"actions": ["camera_drag"], "prefix": "Mantener ", "text": "Arrastrar la cámara con el mouse."},
			{"actions": ["camera_zoom_in", "camera_zoom_out"], "wheel": true, "text": "Acercar y alejar la cámara."},
			{"actions": ["camera_rotate_left", "camera_rotate_right"], "wheel": true, "text": "Girar la cámara."},
		],
	},
	{
		"title": "Unidades",
		"rows": [
			{"actions": ["select_unit"], "text": "Seleccionar una unidad (otro clic elige la superpuesta)."},
			{"actions": ["select_unit"], "suffix": " y arrastrar", "text": "Seleccionar las unidades dentro del rectángulo."},
			{"actions": ["select_unit"], "suffix": " en edificio", "text": "Seleccionar un edificio propio para entrenar."},
			{"actions": ["move_selected_units"], "text": "Mover, talar, atacar, seguir una obra o reparar una granja."},
			{"actions": ["stop_selected_units"], "text": "Detener a las unidades seleccionadas."},
			{"actions": ["build_farm", "build_barracks", "build_tower", "build_fence"], "text": "Con trabajadores: construir granja, cuartel, torre o valla."},
		],
	},
]

var _back_button: Button


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_STOP
	if theme == null:
		theme = UI_THEME
	_build_ui()
	visible = false


func open() -> void:
	visible = true
	_back_button.grab_focus()


func close() -> void:
	if not visible:
		return
	visible = false
	closed.emit()


func _unhandled_input(event: InputEvent) -> void:
	if not visible:
		return
	if event.is_action_pressed("ui_cancel") or event.is_action_pressed("pause_menu"):
		close()
		get_viewport().set_input_as_handled()
	elif event is InputEventKey:
		get_viewport().set_input_as_handled()


## Texto del control de una fila de SECTIONS.
static func control_text(row: Dictionary) -> String:
	if row.has("control"):
		return str(row["control"])
	var actions: Array = row.get("actions", [])
	var controls := ""
	if bool(row.get("wheel", false)) and actions.size() == 2:
		controls = InputLabels.for_wheel_pair(str(actions[0]), str(actions[1]))
	else:
		controls = InputLabels.for_actions(actions)
	return str(row.get("prefix", "")) + controls + str(row.get("suffix", ""))


func _build_ui() -> void:
	var center := CenterContainer.new()
	center.mouse_filter = Control.MOUSE_FILTER_IGNORE
	center.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(center)
	var panel := PanelContainer.new()
	panel.name = "HelpPanel"
	center.add_child(panel)
	var box := VBoxContainer.new()
	box.add_theme_constant_override("separation", 8)
	panel.add_child(box)

	var title := Label.new()
	title.name = "Title"
	title.text = "Ayuda"
	title.theme_type_variation = &"TitleLabel"
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)

	for section: Dictionary in SECTIONS:
		var heading := Label.new()
		heading.name = "%sTitle" % section["title"]
		heading.text = section["title"]
		heading.theme_type_variation = &"TitleLabel"
		heading.add_theme_font_size_override("font_size", 24)
		box.add_child(heading)
		var grid := GridContainer.new()
		grid.name = "%sRows" % section["title"]
		grid.columns = 2
		grid.add_theme_constant_override("h_separation", 18)
		grid.add_theme_constant_override("v_separation", 4)
		box.add_child(grid)
		for row: Dictionary in section["rows"]:
			var control_label := Label.new()
			control_label.text = control_text(row)
			control_label.add_theme_color_override("font_color", CONTROL_COLOR)
			control_label.add_theme_font_size_override("font_size", TEXT_SIZE)
			control_label.custom_minimum_size = Vector2(CONTROL_COLUMN_WIDTH, 0)
			control_label.size_flags_vertical = Control.SIZE_SHRINK_BEGIN
			grid.add_child(control_label)
			var text_label := Label.new()
			text_label.text = row["text"]
			text_label.add_theme_font_size_override("font_size", TEXT_SIZE)
			grid.add_child(text_label)

	_back_button = Button.new()
	_back_button.name = "HelpBackButton"
	_back_button.text = "Volver"
	_back_button.custom_minimum_size = Vector2(200, 56)
	_back_button.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	_back_button.pressed.connect(close)
	box.add_child(_back_button)
