extends Node

## Sonidos de la partida (los reproduce SoundManager, en el bus Sfx):
## - del mundo, en la posición de la entidad: aparición (UnitSpawned), muerte (trabajador o
##   soldado), daño a una unidad (EntityDamaged) y destrucción de un edificio;
## - de trabajo, en loop sobre la unidad mientras trabaja quieta: hacha al recolectar y
##   martillo al construir o reparar;
## - de combate: un golpe de espada por ataque, con el cooldown del soldado del catálogo
##   (el protocolo no publica cada golpe, 02 §9);
## - de interfaz: seleccionar, dar una orden y órdenes rechazadas.

const ErrorTexts = preload("res://scripts/ui/error_texts.gd")

## Loops de trabajo simultáneos por tipo; los demás trabajadores trabajan en silencio.
const MAX_WORK_LOOPS := 4
const WORK_SOUNDS := {"gather": "axe", "build": "hammer"}

@export var command_controller: Node
@export var placement_controller: Node
@export var animation_controller: Node

var _world: Node
var _sounds: Node
## unit_id → AudioStreamPlayer3D en loop colgado de la unidad.
var _work_loops: Dictionary = {}
## unit_id → segundos hasta el próximo golpe de espada.
var _attack_timers: Dictionary = {}


func _ready() -> void:
	_world = get_parent()
	_sounds = get_node_or_null("/root/SoundManager")
	_connect(_world, "unit_spawned", _on_unit_spawned)
	_connect(_world, "unit_removed", _on_unit_removed)
	_connect(_world, "building_removed", _on_building_removed)
	_connect(_world, "entity_damaged", _on_entity_damaged)
	_connect(_world, "command_result_received", _on_command_result)
	_connect(_world, "command_rejected_received", func(_id: int, _error: String) -> void: _play_ui("error"))
	_connect(_world, "world_discarded", func(_id: int) -> void: _stop_all_work())
	_connect(command_controller, "selection_made", func() -> void: _play_ui("select"))
	_connect(command_controller, "command_marker_shown", func(_type: String, _at: Vector3) -> void: _play_ui("confirm"))
	_connect(command_controller, "command_failed", func(_error: String, _detail: String) -> void: _play_ui("error"))
	_connect(placement_controller, "command_failed", func(_error: String, _detail: String) -> void: _play_ui("error"))
	_connect(animation_controller, "unit_activity_changed", _on_unit_activity_changed)


func _process(delta: float) -> void:
	for unit_id: Variant in _attack_timers.keys():
		var unit: Node3D = _world.entities_by_id.get(int(unit_id))
		if unit == null:
			_attack_timers.erase(unit_id)
			continue
		_attack_timers[unit_id] = float(_attack_timers[unit_id]) - delta
		if float(_attack_timers[unit_id]) <= 0.0:
			_play_at("sword", unit)
			_attack_timers[unit_id] = attack_interval()


## Segundos entre ataques del soldado, del catálogo (cooldownTicks / ticksPerSecond).
func attack_interval() -> float:
	var ticks_per_second := float(_world.active_catalog.get("ticksPerSecond", 20))
	for entry: Variant in _world.active_catalog.get("units", []):
		if entry is Dictionary and str(entry.get("unitType", "")) == "Soldier":
			var cooldown := float(entry.get("attack", {}).get("cooldownTicks", 0))
			if cooldown > 0.0 and ticks_per_second > 0.0:
				return cooldown / ticks_per_second
	return 1.0


func work_loop_of(unit_id: int) -> AudioStreamPlayer3D:
	var player: Variant = _work_loops.get(unit_id)
	return player if is_instance_valid(player) else null


func is_attacking(unit_id: int) -> bool:
	return _attack_timers.has(unit_id)


func _on_unit_spawned(unit_id: int) -> void:
	_play_at("spawn", _world.entities_by_id.get(unit_id))


## Se emite antes de que la unidad salga del índice, así todavía se sabe su tipo y lugar.
func _on_unit_removed(unit_id: int) -> void:
	var unit: Node3D = _world.entities_by_id.get(unit_id)
	if unit == null:
		return
	var unit_type := str(unit.get_meta("entity_state", {}).get("unitType", ""))
	_play_at("death_soldier" if unit_type == "Soldier" else "death_worker", unit)
	_stop_work(unit_id)


func _on_building_removed(building_id: int) -> void:
	_play_at("crumble", _world.entities_by_id.get(building_id))


func _on_entity_damaged(entity_id: int) -> void:
	var entity: Node3D = _world.entities_by_id.get(entity_id)
	if entity != null and entity.get_meta("entity_kind", "") == "unit":
		_play_at("hurt", entity)


func _on_command_result(_client_command_id: int, result: Dictionary) -> void:
	if not ErrorTexts.command_result_error(result).is_empty():
		_play_ui("error")


func _on_unit_activity_changed(unit_id: int, activity: String) -> void:
	_stop_work(unit_id)
	var unit: Node3D = _world.entities_by_id.get(unit_id)
	if unit == null or activity.is_empty():
		return
	if activity == "attack":
		_attack_timers[unit_id] = 0.0
		return
	var sound_name: String = WORK_SOUNDS.get(activity, "")
	if sound_name.is_empty() or _sounds == null or _count_loops(sound_name) >= MAX_WORK_LOOPS:
		return
	var player: AudioStreamPlayer3D = _sounds.make_loop_player(sound_name)
	if player == null:
		return
	unit.add_child(player)
	_work_loops[unit_id] = player


func _stop_work(unit_id: int) -> void:
	_attack_timers.erase(unit_id)
	var player: Variant = _work_loops.get(unit_id)
	_work_loops.erase(unit_id)
	if is_instance_valid(player):
		(player as Node).queue_free()


func _stop_all_work() -> void:
	for unit_id: Variant in _work_loops.keys():
		_stop_work(int(unit_id))
	_attack_timers.clear()


func _count_loops(sound_name: String) -> int:
	var count := 0
	for player: Variant in _work_loops.values():
		if is_instance_valid(player) and str((player as Node).get_meta("sound_name", "")) == sound_name:
			count += 1
	return count


func _play_at(sound_name: String, entity: Node3D) -> void:
	if _sounds != null and entity != null and entity.is_inside_tree():
		_sounds.play_at(sound_name, entity.global_position)


func _play_ui(sound_name: String) -> void:
	if _sounds != null:
		_sounds.play_ui(sound_name)


func _connect(source: Object, signal_name: String, callback: Callable) -> void:
	if source != null and source.has_signal(signal_name) and not source.is_connected(signal_name, callback):
		source.connect(signal_name, callback)
