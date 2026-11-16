extends SceneTree

## Efectos de sonido: SoundManager (variantes, bus Sfx, voces) y MatchSfx (qué suena con cada
## evento de la partida).

const InitialWorldScript = preload("res://scripts/initial_world.gd")
const UnitCommandControllerScript = preload("res://scripts/unit_command_controller.gd")
const UnitAnimationControllerScript = preload("res://scripts/unit_animation_controller.gd")
const MatchSfxScript = preload("res://scripts/match_sfx.gd")

const SOLDIER := 20
const EXTRA_WORKERS := [30, 31, 32, 33]
const EXPECTED_TESTS := 4

var _failed := false
var _completed_tests := 0
var _played: Array = []
var _tick := 0


func _initialize() -> void:
	call_deferred("_run")


func _run() -> void:
	var sounds: Node = root.get_node_or_null("SoundManager")
	_check(sounds != null, "SoundManager is an autoload.")
	if sounds == null:
		quit(1)
		return
	sounds.sound_played.connect(func(sound_name: String, at: Variant) -> void: _played.append([sound_name, at]))
	await _test_sound_manager(sounds)
	await _test_event_sounds()
	_test_work_loops()
	_test_attack_cadence()
	# Deja terminar los sonidos que siguen sonando antes de salir.
	await create_timer(2.0).timeout
	_check(_completed_tests == EXPECTED_TESTS, "Only %d of %d tests completed." % [_completed_tests, EXPECTED_TESTS])
	if _failed:
		quit(1)
		return
	print("Sfx tests passed.")
	quit(0)


func _check(condition: bool, message: String) -> void:
	if condition:
		return
	_failed = true
	push_error(message)


func _test_sound_manager(sounds: Node) -> void:
	var expected_variants := {
		"spawn": 1, "death_worker": 1, "death_soldier": 1, "hurt": 1, "sword": 3, "axe": 2,
		"hammer": 1, "crumble": 1, "select": 3, "confirm": 2, "error": 2,
	}
	for sound_name: String in expected_variants:
		_check(sounds.variants(sound_name).size() == expected_variants[sound_name], "%s has %d variants." % [sound_name, expected_variants[sound_name]])
	_played.clear()
	_check(sounds.play_ui("select"), "UI sounds play.")
	_check(not sounds.play_ui("select"), "The same UI sound does not repeat right away.")
	var player := sounds.get_children().back() as AudioStreamPlayer
	_check(player != null and player.bus == &"Sfx", "Sound effects play on the Sfx bus.")
	for _index in range(4):
		_check(sounds.play_at("hurt", Vector3(5, 0, 5)), "World sounds play up to the voice limit.")
	_check(not sounds.play_at("hurt", Vector3(5, 0, 5)), "Beyond the voice limit a sound is skipped.")
	var world_player := sounds.get_children().back() as AudioStreamPlayer3D
	_check(world_player != null and world_player.bus == &"Sfx" and world_player.position == Vector3(5, 0, 5), "World sounds are 3D, at their position.")
	await create_timer(1.0).timeout
	_check(sounds.play_at("hurt", Vector3.ZERO), "Voices are released when the clip ends.")
	var loop: AudioStreamPlayer3D = sounds.make_loop_player("axe")
	_check(loop != null and bool(loop.stream.get("loop")) and loop.bus == &"Sfx", "Work sounds loop on the Sfx bus.")
	_check(not bool((sounds.variants("axe")[0] as AudioStream).get("loop")), "The imported clip is not changed.")
	loop.free()
	_completed_tests += 1


func _test_event_sounds() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	var controller: Node = setup["controller"]
	await create_timer(0.2).timeout
	_played.clear()
	_apply(world, [{"type": "UnitSpawned", "unitId": 40, "owner": 1, "unitType": "Worker", "x": 9.5, "z": 9.5, "hp": 40, "index": 0}])
	_check(_played_names() == ["spawn"] and _played[0][1] == Vector3(9.5, 0, 9.5), "A spawned unit sounds where it appears.")
	_played.clear()
	_apply(world, [
		{"type": "EntityDamaged", "entityId": 3, "hp": 20, "index": 0},
		{"type": "EntityDamaged", "entityId": 2, "hp": 900, "index": 1},
	])
	_check(_played_names() == ["hurt"], "Damaged units sound; damaged buildings do not.")
	_played.clear()
	_apply(world, [
		{"type": "EntityDestroyed", "entityId": 3, "index": 0},
		{"type": "EntityDestroyed", "entityId": SOLDIER, "index": 1},
		{"type": "EntityDestroyed", "entityId": 2, "index": 2},
		{"type": "MapRevisionChanged", "revision": 1, "index": 3},
	], 1)
	_check(_played_names() == ["death_worker", "death_soldier", "crumble"], "Deaths and destroyed buildings sound: %s" % str(_played_names()))
	_played.clear()
	controller.selection_made.emit()
	controller.command_marker_shown.emit("Move", Vector3.ZERO)
	world._handle_command_rejected({"messageId": 9, "error": "Network.RateLimited"})
	_check(_played_names() == ["select", "confirm", "error"], "Selecting, ordering and rejections have UI sounds.")
	_free_setup(setup)


func _test_work_loops() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	var animator: Node = setup["animator"]
	var sfx: Node = setup["sfx"]
	var workers: Array = [3, 4]
	workers.append_array(EXTRA_WORKERS)
	for worker_id: int in workers:
		_set_order(world, worker_id, "GatherWood")
	animator.update_units(0.1)
	var loops := 0
	for worker_id: int in workers:
		var loop: AudioStreamPlayer3D = sfx.work_loop_of(worker_id)
		if loop != null:
			loops += 1
			_check(loop.get_parent() == world.entities_by_id[worker_id], "The axe loop follows its worker.")
			_check(loop.get_meta("sound_name") == "axe", "Gathering sounds like an axe.")
	_check(loops == sfx.MAX_WORK_LOOPS, "At most %d work loops of the same kind play at once (%d)." % [sfx.MAX_WORK_LOOPS, loops])
	_set_order(world, 3, "Build")
	animator.update_units(0.1)
	var hammer: AudioStreamPlayer3D = sfx.work_loop_of(3)
	_check(hammer != null and hammer.get_meta("sound_name") == "hammer", "Building sounds like a hammer.")
	(world.entities_by_id[3] as Node3D).position += Vector3(1, 0, 0)
	animator.update_units(0.1)
	_check(sfx.work_loop_of(3) == null, "Walking away stops the work sound.")
	_set_order(world, 4, "Idle")
	animator.update_units(0.1)
	_check(sfx.work_loop_of(4) == null, "Stopping the task stops the work sound.")
	_apply(world, [{"type": "EntityDestroyed", "entityId": EXTRA_WORKERS[0], "index": 0}])
	_check(sfx.work_loop_of(EXTRA_WORKERS[0]) == null, "A dying worker stops working.")
	_free_setup(setup)


func _test_attack_cadence() -> void:
	var setup := _create_setup()
	var world: Node = setup["world"]
	var animator: Node = setup["animator"]
	var sfx: Node = setup["sfx"]
	var interval: float = sfx.attack_interval()
	_check(is_equal_approx(interval, 1.0), "The sword cadence is the soldier cooldown of the catalog (20 ticks at 20 ticks/s).")
	_set_order(world, SOLDIER, "Attack")
	animator.update_units(0.1)
	_check(sfx.is_attacking(SOLDIER), "An attacking soldier in place starts its combat sounds.")
	_played.clear()
	sfx._process(0.0)
	_check(_played_names() == ["sword"], "The first strike sounds right away.")
	sfx._process(interval * 0.5)
	_check(_played_names() == ["sword"], "No strike before the cooldown.")
	sfx._process(interval * 0.6)
	_check(_played_names() == ["sword", "sword"], "Then one strike per cooldown.")
	_set_order(world, SOLDIER, "Idle")
	animator.update_units(0.1)
	_check(not sfx.is_attacking(SOLDIER), "Stopping the attack stops the strikes.")
	_free_setup(setup)


# --- Utilidades ----------------------------------------------------------------------

func _create_setup() -> Dictionary:
	_tick = 0
	var world: Node = InitialWorldScript.new()
	root.add_child(world)
	world._session_controller = null
	var controller: Node = UnitCommandControllerScript.new()
	controller.name = "UnitCommandController"
	world.add_child(controller)
	var animator: Node = UnitAnimationControllerScript.new()
	animator.name = "UnitAnimationController"
	world.add_child(animator)
	var sfx: Node = MatchSfxScript.new()
	sfx.command_controller = controller
	sfx.animation_controller = animator
	world.add_child(sfx)
	var state: Dictionary = _with_integer_numbers(_example("02-initial-state.json").get("payload", {}))
	state["units"].append({"unitId": SOLDIER, "owner": 1, "unitType": "Soldier", "x": 9.5, "z": 14.5, "hp": 80, "order": "Idle"})
	for index in range(EXTRA_WORKERS.size()):
		state["units"].append({"unitId": EXTRA_WORKERS[index], "owner": 1, "unitType": "Worker", "x": 10.5 + index, "z": 5.5, "hp": 40, "order": "Idle"})
	_check(world.prepare_initial_state(1, state), "The sfx world should build.")
	world.set_process(false)
	animator.set_process(false)
	sfx.set_process(false)
	return {"world": world, "controller": controller, "animator": animator, "sfx": sfx}


func _free_setup(setup: Dictionary) -> void:
	_completed_tests += 1
	var world: Node = setup["world"]
	if is_instance_valid(world):
		world.queue_free()


func _set_order(world: Node, unit_id: int, order: String) -> void:
	var unit: Node3D = world.entities_by_id[unit_id]
	var state: Dictionary = unit.get_meta("entity_state").duplicate()
	state["order"] = order
	unit.set_meta("entity_state", state)


func _played_names() -> Array:
	var names: Array = []
	for entry: Array in _played:
		names.append(entry[0])
	return names


func _apply(world: Node, events: Array, revision: int = 0) -> void:
	_tick += 1
	_check(
		world.apply_state_events({"tick": _tick, "mapRevision": revision, "events": events, "commandResults": []}),
		"The StateEvents batch at tick %d should apply." % _tick
	)


func _with_integer_numbers(value: Variant) -> Variant:
	match typeof(value):
		TYPE_DICTIONARY:
			var result: Dictionary = {}
			for key: Variant in value:
				result[key] = _with_integer_numbers(value[key])
			return result
		TYPE_ARRAY:
			var result: Array = []
			for item: Variant in value:
				result.append(_with_integer_numbers(item))
			return result
		TYPE_FLOAT:
			if is_equal_approx(value, roundf(value)) and absf(value) < 1e15:
				return int(value)
	return value


func _example(filename: String) -> Dictionary:
	var path := ProjectSettings.globalize_path("res://../docs/protocol/examples/server-to-client").path_join(filename)
	var parsed: Variant = JSON.parse_string(FileAccess.get_file_as_string(path))
	return parsed if parsed is Dictionary else {}
