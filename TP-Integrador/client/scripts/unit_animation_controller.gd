extends Node

## Anima las unidades del mundo según lo que se ve y su orden oficial (UnitOrderChanged, D-067):
## - desplazándose: caminar, mirando hacia donde va;
## - quieta con una orden de tarea: la animación de esa tarea (las propias miran a su objetivo);
## - en cualquier otro caso: reposo.
## No hay eventos de golpe en el protocolo (02 §9 PENDIENTE): atacar es una animación en loop
## mientras el soldado está quieto con la orden Attack.

const AnimationLoops = preload("res://scripts/animation_loops.gd")

const LIBRARY_NAME := "rts"
const IDLE := "idle"
const WALK := "walk"
const ORDER_ANIMATIONS := {
	"GatherWood": "interact-right",
	"Build": "interact-right",
	"RepairFarm": "interact-right",
	"Attack": "attack-melee-right",
}
## Velocidad (m/s) desde la que una unidad se considera en movimiento.
const MOVING_SPEED := 0.05
const TURN_SPEED := 10.0
const BLEND_SECONDS := 0.15

var _world: Node
var _last_positions: Dictionary = {}


func _ready() -> void:
	_world = get_parent()


func _process(delta: float) -> void:
	update_units(delta)


func update_units(delta: float) -> void:
	if _world == null or _world.get("active_world") == null:
		_last_positions.clear()
		return
	var alive: Dictionary = {}
	for entity_id: Variant in _world.entities_by_id:
		var unit: Node3D = _world.entities_by_id[entity_id]
		if unit.get_meta("entity_kind", "") != "unit":
			continue
		var unit_id := int(entity_id)
		alive[unit_id] = true
		var position := unit.global_position
		var previous: Vector3 = _last_positions.get(unit_id, position)
		_last_positions[unit_id] = position
		var step := Vector2(position.x - previous.x, position.z - previous.z)
		var moving := delta > 0.0 and step.length() / delta > MOVING_SPEED
		var facing := step if moving else _target_direction(unit_id, unit)
		if facing.length_squared() > 1e-8:
			var target_yaw := atan2(facing.x, facing.y)
			unit.rotation.y = lerp_angle(unit.rotation.y, target_yaw, clampf(TURN_SPEED * delta, 0.0, 1.0))
		var order := str(unit.get_meta("entity_state", {}).get("order", "Idle"))
		play(unit, animation_for(order, moving))
	for unit_id: Variant in _last_positions.keys():
		if not alive.has(unit_id):
			_last_positions.erase(unit_id)


static func animation_for(order: String, moving: bool) -> String:
	if moving:
		return WALK
	return ORDER_ANIMATIONS.get(order, IDLE)


## Reproduce la animación en loop si no es la actual. Devuelve el nombre completo reproducido.
func play(unit: Node3D, animation_name: String) -> String:
	var player := _animation_player(unit)
	if player == null:
		return ""
	var full_name := "%s/%s" % [LIBRARY_NAME, animation_name]
	if not player.has_animation(full_name):
		full_name = "%s/%s" % [LIBRARY_NAME, IDLE]
	if player.current_animation != full_name and player.has_animation(full_name):
		player.play(full_name, BLEND_SECONDS)
	return full_name


func _animation_player(unit: Node3D) -> AnimationPlayer:
	if unit.has_meta("animation_player"):
		var cached: Variant = unit.get_meta("animation_player")
		if is_instance_valid(cached):
			return cached
	var player := unit.find_child("AnimationPlayer", true, false) as AnimationPlayer
	if player != null:
		var names: Array = [IDLE, WALK]
		names.append_array(ORDER_ANIMATIONS.values())
		AnimationLoops.ensure_looped_library(player, LIBRARY_NAME, names)
	unit.set_meta("animation_player", player)
	return player


## Las unidades propias quietas miran a su objetivo (Attack, GatherWood, Build, RepairFarm).
func _target_direction(unit_id: int, unit: Node3D) -> Vector2:
	var intent: Dictionary = _world.accepted_order_intents.get(unit_id, {})
	var target: Node3D = _world.entities_by_id.get(int(intent.get("targetId", 0)))
	if target == null:
		return Vector2.ZERO
	var offset := target.global_position - unit.global_position
	return Vector2(offset.x, offset.z)
