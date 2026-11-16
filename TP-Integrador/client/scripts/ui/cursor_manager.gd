extends Node

## Cursores del juego (autoload). Los botones usan siempre el guante que señala; sobre el
## mundo, la escena de partida elige el cursor según el contexto con set_world_cursor().
## Sobre cualquier otro control de la interfaz se usa el cursor por defecto. Al terminar un
## cuadro de selección, el guante se abre un instante (gauntlet_open, ver MatchCursor).

const CURSOR_DIR := "res://assets/ui/cursor pack/"

## Imagen y punto activo (hotspot, en píxeles de la imagen de 32×32) de cada cursor.
const CURSORS := {
	"default": ["gauntlet_default", Vector2(3, 5)],
	"point": ["gauntlet_point", Vector2(4, 4)],
	"drag": ["resize_d_cross", Vector2(16, 16)],
	"open": ["gauntlet_open", Vector2(5, 4)],
	"attack": ["tool_sword_a", Vector2(3, 3)],
	"gather": ["tool_axe", Vector2(6, 6)],
	"hammer": ["tool_hammer", Vector2(8, 8)],
	"nav_n": ["navigation_n", Vector2(16, 16)],
	"nav_ne": ["navigation_ne", Vector2(16, 16)],
	"nav_e": ["navigation_e", Vector2(16, 16)],
	"nav_se": ["navigation_se", Vector2(16, 16)],
	"nav_s": ["navigation_s", Vector2(16, 16)],
	"nav_sw": ["navigation_sw", Vector2(16, 16)],
	"nav_w": ["navigation_w", Vector2(16, 16)],
	"nav_nw": ["navigation_nw", Vector2(16, 16)],
}

## Duración del guante abierto al terminar un cuadro de selección.
const OPEN_FLASH_SECONDS := 0.15
## Cursores que son el guante: los únicos que se abren.
const GAUNTLET_KINDS := ["default", "point"]

## Cursor del mundo vigente (uno de CURSORS).
var world_cursor := ""

var _textures: Dictionary = {}
## Cursor aplicado a cada forma de Godot (CURSOR_ARROW, CURSOR_POINTING_HAND).
var _applied: Dictionary = {}
var _open_flash_left := 0.0


func _ready() -> void:
	for kind: String in CURSORS:
		_textures[kind] = load(CURSOR_DIR + "%s.svg" % CURSORS[kind][0])
	_apply(Input.CURSOR_POINTING_HAND, "point")
	set_world_cursor("default")
	get_tree().node_added.connect(_on_node_added)
	_use_pointing_hand_in(get_tree().root)


## Cambia el cursor que se ve sobre el mundo (y sobre los controles sin cursor propio).
func set_world_cursor(kind: String) -> void:
	if kind == world_cursor or not CURSORS.has(kind):
		return
	world_cursor = kind
	# Durante el guante abierto, otro guante espera a que termine; una herramienta se ve ya.
	if _open_flash_left > 0.0 and GAUNTLET_KINDS.has(kind):
		return
	_apply(Input.CURSOR_ARROW, kind)


## Abre el guante un instante. Las herramientas no cambian.
func flash_open() -> void:
	_open_flash_left = OPEN_FLASH_SECONDS
	_apply(Input.CURSOR_POINTING_HAND, "open")
	if GAUNTLET_KINDS.has(world_cursor):
		_apply(Input.CURSOR_ARROW, "open")


## Cursor que se ve ahora con una forma de Godot.
func displayed_cursor(shape: Input.CursorShape) -> String:
	return _applied.get(shape, "")


func _process(delta: float) -> void:
	if _open_flash_left <= 0.0:
		return
	_open_flash_left -= delta
	if _open_flash_left <= 0.0:
		_apply(Input.CURSOR_POINTING_HAND, "point")
		_apply(Input.CURSOR_ARROW, world_cursor)


func texture_for(kind: String) -> Texture2D:
	return _textures.get(kind)


func _apply(shape: Input.CursorShape, kind: String) -> void:
	_applied[shape] = kind
	Input.set_custom_mouse_cursor(_textures[kind], shape, CURSORS[kind][1])


## Todos los botones, también los que se crean por código, muestran el guante que señala.
func _on_node_added(node: Node) -> void:
	if node is BaseButton:
		(node as BaseButton).mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND


func _use_pointing_hand_in(node: Node) -> void:
	_on_node_added(node)
	for child: Node in node.get_children():
		_use_pointing_hand_in(child)
