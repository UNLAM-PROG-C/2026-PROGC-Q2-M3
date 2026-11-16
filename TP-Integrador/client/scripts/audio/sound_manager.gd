extends Node

## Efectos de sonido (autoload), en el bus "Sfx", separado de la música.
## - play_ui(): sonidos de interfaz, sin posición (selección, órdenes, errores).
## - play_at(): sonidos del mundo en una posición 3D, más bajos cuanto más lejos de la cámara.
## - make_loop_player(): un reproductor 3D en loop para colgar de una unidad (trabajo continuo).
## Cada sonido tiene variantes al azar y un tope de voces simultáneas, para no saturar cuando
## muchas unidades hacen lo mismo a la vez.

signal sound_played(sound_name: String, world_position: Variant)

const SFX_BUS := "Sfx"
const UNITS_DIR := "res://assets/audio/units audio/"
const BUILDINGS_DIR := "res://assets/audio/buildings audio/"
const UI_DIR := "res://assets/audio/ui audio/"

## Cada sonido: archivos sueltos o carpetas (se usan todos sus audios como variantes).
const SOUNDS := {
	"spawn": [UNITS_DIR + "spawn.mp3"],
	"death_worker": [UNITS_DIR + "worker-death.mp3"],
	"death_soldier": [UNITS_DIR + "soldier-death.mp3"],
	"hurt": [UNITS_DIR + "hurt.mp3"],
	"sword": [UNITS_DIR + "swords/"],
	"axe": [UNITS_DIR + "axes/"],
	"hammer": [BUILDINGS_DIR + "hammer-hammering-a-nail.mp3"],
	"crumble": [BUILDINGS_DIR + "crumbling.mp3"],
	"select": [UI_DIR + "select_001.ogg", UI_DIR + "select_002.ogg", UI_DIR + "select_003.ogg"],
	"confirm": [UI_DIR + "confirmation_001.ogg", UI_DIR + "confirmation_002.ogg"],
	"error": [UI_DIR + "error_001.ogg", UI_DIR + "error_002.ogg"],
}
const AUDIO_EXTENSIONS := ["mp3", "ogg", "wav"]

## Voces simultáneas por sonido; las que sobran no suenan.
const MAX_VOICES := 4
## Un mismo sonido de interfaz no se repite antes de este tiempo (p. ej., varias vallas rechazadas).
const UI_REPEAT_SECONDS := 0.15
## Variación de tono de los sonidos del mundo, para que no suenen todos idénticos.
const PITCH_VARIATION := 0.08
## Distancias de atenuación de los sonidos del mundo (la cámara está a 8–25 m del suelo).
const UNIT_SIZE := 12.0
const MAX_DISTANCE := 70.0

var _streams: Dictionary = {}
var _active_voices: Dictionary = {}
var _last_ui_play: Dictionary = {}
var _rng := RandomNumberGenerator.new()


func _ready() -> void:
	_rng.randomize()
	for sound_name: String in SOUNDS:
		_streams[sound_name] = _load_variants(SOUNDS[sound_name])


## Variantes cargadas de un sonido (para pruebas y diagnóstico).
func variants(sound_name: String) -> Array:
	return _streams.get(sound_name, [])


func play_ui(sound_name: String) -> bool:
	var now := Time.get_ticks_msec()
	if now - int(_last_ui_play.get(sound_name, -100000)) < int(UI_REPEAT_SECONDS * 1000.0):
		return false
	var stream := _pick(sound_name)
	if stream == null or not _take_voice(sound_name):
		return false
	_last_ui_play[sound_name] = now
	var player := AudioStreamPlayer.new()
	player.bus = SFX_BUS
	player.stream = stream
	_start(player, sound_name)
	sound_played.emit(sound_name, null)
	return true


func play_at(sound_name: String, world_position: Vector3) -> bool:
	var stream := _pick(sound_name)
	if stream == null or not _take_voice(sound_name):
		return false
	var player := _make_player_3d(stream)
	player.pitch_scale = 1.0 + _rng.randf_range(-PITCH_VARIATION, PITCH_VARIATION)
	player.position = world_position
	_start(player, sound_name)
	sound_played.emit(sound_name, world_position)
	return true


## Reproductor 3D en loop (sin agregar al árbol): el que lo usa lo cuelga de la unidad, así
## sigue su posición, y lo libera al terminar la tarea. Arranca en un punto al azar del clip
## para que varias unidades no suenen sincronizadas.
func make_loop_player(sound_name: String) -> AudioStreamPlayer3D:
	var stream := _pick(sound_name)
	if stream == null:
		return null
	var looped := stream.duplicate() as AudioStream
	looped.set("loop", true)
	var player := _make_player_3d(looped)
	player.name = "Loop_%s" % sound_name
	player.set_meta("sound_name", sound_name)
	player.set_meta("start_at", _rng.randf_range(0.0, maxf(stream.get_length() - 0.1, 0.0)))
	player.ready.connect(func() -> void: player.play(float(player.get_meta("start_at"))), CONNECT_ONE_SHOT)
	return player


func _make_player_3d(stream: AudioStream) -> AudioStreamPlayer3D:
	var player := AudioStreamPlayer3D.new()
	player.bus = SFX_BUS
	player.stream = stream
	player.unit_size = UNIT_SIZE
	player.max_distance = MAX_DISTANCE
	return player


func _start(player: Node, sound_name: String) -> void:
	add_child(player)
	player.play()
	# La voz se libera cuando termina el clip (según su duración y tono), sin depender de la
	# señal finished: así el tope de voces es predecible aunque el audio no avance.
	var stream: AudioStream = player.get("stream")
	var duration := stream.get_length() / maxf(float(player.get("pitch_scale")), 0.01) + 0.05
	get_tree().create_timer(duration).timeout.connect(func() -> void:
		_active_voices[sound_name] = maxi(int(_active_voices.get(sound_name, 1)) - 1, 0)
		if is_instance_valid(player):
			player.queue_free()
	)


func _take_voice(sound_name: String) -> bool:
	var active := int(_active_voices.get(sound_name, 0))
	if active >= MAX_VOICES:
		return false
	_active_voices[sound_name] = active + 1
	return true


func _pick(sound_name: String) -> AudioStream:
	var options: Array = _streams.get(sound_name, [])
	if options.is_empty():
		return null
	return options[_rng.randi_range(0, options.size() - 1)]


func _load_variants(sources: Array) -> Array:
	var streams: Array = []
	for source: String in sources:
		var paths: Array = []
		if source.ends_with("/"):
			for file: String in ResourceLoader.list_directory(source):
				if AUDIO_EXTENSIONS.has(file.get_extension().to_lower()):
					paths.append(source + file)
			paths.sort()
		else:
			paths.append(source)
		for path: String in paths:
			var stream := load(path) as AudioStream
			if stream == null:
				push_warning("No se pudo cargar el sonido %s." % path)
				continue
			streams.append(stream)
	return streams
