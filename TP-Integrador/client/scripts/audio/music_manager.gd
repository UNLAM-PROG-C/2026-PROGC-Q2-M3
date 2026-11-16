extends Node

## Música del juego (autoload). Vive entre escenas, así el tema del menú sigue sonando sin
## cortes al pasar del menú principal a la pantalla de conexión.
## - Tema del menú: "Vadodora Chill Mix" en loop (menú principal y pantallas de conexión).
## - Partida: las canciones de assets/audio/music en orden aleatorio, todas antes de repetir
##   y nunca la misma dos veces seguidas.
## Suena en el bus "Music" (default_bus_layout.tres), separado de "Sfx" para los efectos,
## así cada uno tiene su propio volumen.

signal song_started(song_path: String)

const MUSIC_DIR := "res://assets/audio/music/"
const MENU_THEME := MUSIC_DIR + "Vadodora Chill Mix.mp3"
const SONG_EXTENSIONS := ["mp3", "ogg", "wav"]
const MUSIC_BUS := "Music"
const SILENT_DB := -60.0

## Volumen base del reproductor (dB). El control para el jugador es el volumen del bus.
@export var volume_db := -8.0
## Duración del desvanecido al cambiar de música o al detenerla.
@export var fade_seconds := 1.5

## "menu", "match" o "" (sin música).
var mode := ""
## Canción que suena ("" si no hay música).
var current_song := ""

var _player: AudioStreamPlayer
var _songs: PackedStringArray = []
var _queue: PackedStringArray = []
var _fade_tween: Tween
var _rng := RandomNumberGenerator.new()


func _ready() -> void:
	_rng.randomize()
	_songs = find_songs()
	_player = AudioStreamPlayer.new()
	_player.name = "MusicPlayer"
	_player.bus = MUSIC_BUS
	_player.volume_db = volume_db
	add_child(_player)
	_player.finished.connect(_on_song_finished)


## Canciones de la carpeta de música. ResourceLoader.list_directory también funciona en el
## juego exportado, donde los archivos originales ya no están en res://.
static func find_songs() -> PackedStringArray:
	var songs: PackedStringArray = []
	for file: String in ResourceLoader.list_directory(MUSIC_DIR):
		if SONG_EXTENSIONS.has(file.get_extension().to_lower()):
			songs.append(MUSIC_DIR + file)
	songs.sort()
	return songs


## Tema del menú en loop. Si ya está sonando, sigue sin reiniciarse.
func play_menu_theme() -> void:
	if mode == "menu":
		return
	mode = "menu"
	_transition(func() -> void: _play(MENU_THEME, true))


## Canciones de la partida en orden aleatorio.
func play_match_playlist() -> void:
	if mode == "match" or _songs.is_empty():
		return
	mode = "match"
	_queue.clear()
	_transition(play_next)


## Desvanece la música y la detiene.
func stop_music() -> void:
	if mode.is_empty():
		return
	mode = ""
	_transition(_stop_now)


## Siguiente canción de la partida (también al terminar cada una).
func play_next() -> void:
	if mode != "match" or _songs.is_empty():
		return
	if _queue.is_empty():
		_queue = _shuffled_songs()
	var song := _queue[0]
	_queue.remove_at(0)
	_play(song, false)


func is_playing() -> bool:
	return not current_song.is_empty()


func songs() -> PackedStringArray:
	return _songs


# --- Volumen de la música (bus "Music") --------------------------------------------------

## Volumen de la música de 0 a 1, independiente de los efectos (bus "Sfx").
func set_music_volume(linear: float) -> void:
	AudioServer.set_bus_volume_db(_music_bus(), linear_to_db(clampf(linear, 0.0, 1.0)))


func get_music_volume() -> float:
	return db_to_linear(AudioServer.get_bus_volume_db(_music_bus()))


func set_music_muted(muted: bool) -> void:
	AudioServer.set_bus_mute(_music_bus(), muted)


func is_music_muted() -> bool:
	return AudioServer.is_bus_mute(_music_bus())


func _music_bus() -> int:
	return AudioServer.get_bus_index(MUSIC_BUS)


# --- Reproducción ----------------------------------------------------------------------

## Desvanece lo que suena (si suena algo) y después ejecuta `start`.
func _transition(start: Callable) -> void:
	_kill_fade()
	if not _player.playing:
		start.call()
		return
	_fade_tween = create_tween()
	_fade_tween.tween_property(_player, "volume_db", SILENT_DB, fade_seconds)
	_fade_tween.tween_callback(func() -> void:
		_player.stop()
		start.call()
	)


func _play(song: String, loop: bool) -> void:
	var stream := load(song) as AudioStream
	if stream == null:
		push_warning("No se pudo cargar la canción %s." % song)
		current_song = ""
		return
	if loop:
		# El MP3 importado no tiene loop: se reproduce una copia con loop.
		stream = stream.duplicate()
		stream.set("loop", true)
	_player.stream = stream
	_player.volume_db = volume_db
	_player.play()
	current_song = song
	song_started.emit(song)


func _stop_now() -> void:
	_player.stop()
	_player.volume_db = volume_db
	current_song = ""
	_queue.clear()


func _kill_fade() -> void:
	if _fade_tween != null and _fade_tween.is_valid():
		_fade_tween.kill()
	_fade_tween = null
	_player.volume_db = volume_db


func _on_song_finished() -> void:
	if mode == "match":
		play_next()


## Todas las canciones mezcladas, sin que la primera repita la que acaba de sonar.
func _shuffled_songs() -> PackedStringArray:
	var shuffled := Array(_songs)
	for index in range(shuffled.size() - 1, 0, -1):
		var other := _rng.randi_range(0, index)
		var swap: String = shuffled[index]
		shuffled[index] = shuffled[other]
		shuffled[other] = swap
	if shuffled.size() > 1 and shuffled[0] == current_song:
		var first: String = shuffled[0]
		shuffled[0] = shuffled[1]
		shuffled[1] = first
	return PackedStringArray(shuffled)
