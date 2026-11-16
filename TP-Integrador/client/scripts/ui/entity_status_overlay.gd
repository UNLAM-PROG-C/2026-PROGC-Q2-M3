extends Control

## Estado de las entidades sobre el mundo, dibujado en 2D: barras de vida, progreso de obra
## y estado de las granjas. Se dibuja solo lo que aporta información:
## - unidades: seleccionadas o dañadas;
## - edificios: seleccionados, dañados, en obra (en obra el HP es el progreso, 03 §5),
##   y granjas agotadas o en reparación.
## Las obras y reparaciones llevan el ícono de construcción; las granjas agotadas, el de
## granja vacía.
## Verde = propio, rojo = rival. Los árboles agotados ya se ocultan en el mundo (09 §4).

const UNIT_BAR_WIDTH := 40.0
const BUILDING_BAR_WIDTH_PER_CELL := 22.0
const MAX_BUILDING_BAR_WIDTH := 110.0
const BAR_HEIGHT := 6.0
const OWN_COLOR := Color(0.3, 0.85, 0.35)
const RIVAL_COLOR := Color(0.95, 0.3, 0.25)
const CONSTRUCTION_COLOR := Color(0.95, 0.75, 0.25)
const REPAIR_COLOR := Color(0.35, 0.65, 1.0)
const BACKGROUND_COLOR := Color(0.08, 0.06, 0.04, 0.8)
const LABEL_COLOR := Color(1.0, 0.92, 0.75)
const LABEL_FONT_SIZE := 14
const ICON_SIZE := 26.0
## Obra en construcción o granja en reparación.
const CONSTRUCTION_ICON := preload("res://assets/ui/building_building.svg")
## Granja agotada: no produce comida hasta repararla.
const EMPTY_FARM_ICON := preload("res://assets/ui/empty_farm.svg")

@export var world: Node
@export var command_controller: Node

## Altura sobre el suelo donde va la barra de cada entidad, medida una vez desde su modelo.
var _top_heights: Dictionary = {}
var _measured_world: Node


func _ready() -> void:
	set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	mouse_filter = Control.MOUSE_FILTER_IGNORE


func _process(_delta: float) -> void:
	# Los IDs pueden repetirse en otra partida: las alturas medidas valen para un solo mundo.
	if world != null and world.get("active_world") != _measured_world:
		_measured_world = world.get("active_world")
		_top_heights.clear()
	queue_redraw()


func _draw() -> void:
	var font := get_theme_default_font()
	for bar: Dictionary in collect_status_bars():
		var center: Vector2 = bar["screen_position"]
		var width: float = bar["width"]
		var top_left := center - Vector2(width / 2.0, BAR_HEIGHT)
		if bar.has("ratio"):
			draw_rect(Rect2(top_left, Vector2(width, BAR_HEIGHT)), BACKGROUND_COLOR)
			draw_rect(Rect2(top_left + Vector2.ONE, Vector2((width - 2.0) * float(bar["ratio"]), BAR_HEIGHT - 2.0)), bar["color"])
		if bar.has("secondary_ratio"):
			var second := top_left + Vector2(0.0, BAR_HEIGHT + 2.0)
			draw_rect(Rect2(second, Vector2(width, BAR_HEIGHT)), BACKGROUND_COLOR)
			draw_rect(Rect2(second + Vector2.ONE, Vector2((width - 2.0) * float(bar["secondary_ratio"]), BAR_HEIGHT - 2.0)), REPAIR_COLOR)
		if bar.has("icon"):
			var icon_rect := Rect2(center - Vector2(ICON_SIZE / 2.0, BAR_HEIGHT + ICON_SIZE + 3.0), Vector2(ICON_SIZE, ICON_SIZE))
			draw_texture_rect(bar["icon"], icon_rect, false)
		if bar.has("label") and font != null:
			var text: String = bar["label"]
			var text_size := font.get_string_size(text, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT_SIZE)
			var baseline := top_left + Vector2((width - text_size.x) / 2.0, -4.0)
			draw_string_outline(font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT_SIZE, 4, Color(0.1, 0.06, 0.03))
			draw_string(font, baseline, text, HORIZONTAL_ALIGNMENT_LEFT, -1, LABEL_FONT_SIZE, LABEL_COLOR)


## Barras a dibujar en este frame: posición en pantalla, ancho, proporción, color y texto.
func collect_status_bars() -> Array[Dictionary]:
	var bars: Array[Dictionary] = []
	var camera := get_viewport().get_camera_3d()
	if world == null or camera == null or world.get("active_world") == null:
		return bars
	var local_player := int(world.active_snapshot.get("playerId", 0))
	var selected_units: Array = command_controller.get_selected_units() if command_controller != null else []
	var selected_building := int(command_controller.get("selected_building_id")) if command_controller != null else 0
	for entity_id: Variant in world.entities_by_id:
		var node: Node3D = world.entities_by_id[entity_id]
		var kind := str(node.get_meta("entity_kind", ""))
		if (kind != "unit" and kind != "building") or not node.is_visible_in_tree():
			continue
		var state: Dictionary = node.get_meta("entity_state", {})
		var bar := _bar_for(int(entity_id), kind, state, selected_units, selected_building)
		if bar.is_empty():
			continue
		var anchor := node.global_position + Vector3.UP * (_top_height(int(entity_id), node) + 0.25)
		if camera.is_position_behind(anchor):
			continue
		bar["entity_id"] = int(entity_id)
		bar["screen_position"] = camera.unproject_position(anchor)
		if not bar.has("color"):
			bar["color"] = OWN_COLOR if int(state.get("owner", 0)) == local_player else RIVAL_COLOR
		bars.append(bar)
	return bars


func _bar_for(entity_id: int, kind: String, state: Dictionary, selected_units: Array, selected_building: int) -> Dictionary:
	var max_hp := _max_hp(kind, state)
	var hp := int(state.get("hp", max_hp))
	var ratio := clampf(float(hp) / float(max_hp), 0.0, 1.0) if max_hp > 0 else 1.0
	if kind == "unit":
		if not selected_units.has(entity_id) and hp >= max_hp:
			return {}
		return {"ratio": ratio, "width": UNIT_BAR_WIDTH}
	var footprint: Dictionary = state.get("footprint", {})
	var width := clampf(float(footprint.get("width", 1)) * BUILDING_BAR_WIDTH_PER_CELL, UNIT_BAR_WIDTH, MAX_BUILDING_BAR_WIDTH)
	var bar := {"ratio": ratio, "width": width}
	var farm: Dictionary = state.get("farm", {})
	if not bool(state.get("isCompleted", false)):
		bar["color"] = CONSTRUCTION_COLOR
		bar["status"] = "construction"
		bar["icon"] = CONSTRUCTION_ICON
	elif bool(farm.get("isRepairing", false)):
		bar["status"] = "repairing"
		bar["icon"] = CONSTRUCTION_ICON
		var required := int(farm.get("repairWorkRequired", world.active_catalog.get("farmRepairWorkRequired", 0)))
		bar["secondary_ratio"] = clampf(float(farm.get("repairWorkDone", 0)) / float(required), 0.0, 1.0) if required > 0 else 0.0
	elif bool(farm.get("isDepleted", false)):
		bar["status"] = "depleted"
		bar["icon"] = EMPTY_FARM_ICON
	elif entity_id != selected_building and hp >= max_hp:
		return {}
	return bar


func _max_hp(kind: String, state: Dictionary) -> int:
	var section := "units" if kind == "unit" else "buildings"
	var type_key := "unitType" if kind == "unit" else "buildingType"
	for entry: Variant in world.active_catalog.get(section, []):
		if entry is Dictionary and str(entry.get(type_key, "")) == str(state.get(type_key, "")):
			return int(entry.get("maxHp", 0))
	return 0


func _top_height(entity_id: int, node: Node3D) -> float:
	if _top_heights.has(entity_id):
		return _top_heights[entity_id]
	var top := 0.8
	for visual: Node in node.find_children("*", "VisualInstance3D", true, false):
		var instance := visual as VisualInstance3D
		var box: AABB = node.global_transform.affine_inverse() * instance.global_transform * instance.get_aabb()
		top = maxf(top, box.end.y)
	_top_heights[entity_id] = top
	return top
