extends Node

## Volumen de la música y de los sonidos (autoload). Actúa sobre los buses "Music" y "Sfx"
## (default_bus_layout.tres) y se guarda en un archivo del usuario, así se conserva entre
## sesiones. Los volúmenes van de 0 (silencio) a 1 (volumen original del bus).

signal volume_changed(bus_name: String, volume: float)

const MUSIC_BUS := "Music"
const SFX_BUS := "Sfx"
const BUSES := [MUSIC_BUS, SFX_BUS]
const DEFAULT_VOLUME := 1.0
const SECTION := "audio"

## Archivo de ajustes. Las pruebas lo cambian para no tocar los del jugador.
var settings_path := "user://settings.cfg"


func _ready() -> void:
	load_settings()


func get_volume(bus_name: String) -> float:
	var bus := AudioServer.get_bus_index(bus_name)
	if bus < 0 or AudioServer.is_bus_mute(bus):
		return 0.0
	return clampf(db_to_linear(AudioServer.get_bus_volume_db(bus)), 0.0, 1.0)


## Cambia el volumen de un bus y lo guarda. En 0 el bus se silencia del todo.
func set_volume(bus_name: String, volume: float, save := true) -> void:
	var bus := AudioServer.get_bus_index(bus_name)
	if bus < 0:
		return
	var clamped := clampf(volume, 0.0, 1.0)
	AudioServer.set_bus_mute(bus, is_zero_approx(clamped))
	AudioServer.set_bus_volume_db(bus, linear_to_db(maxf(clamped, 0.0001)))
	volume_changed.emit(bus_name, clamped)
	if save:
		save_settings()


func load_settings() -> void:
	var config := ConfigFile.new()
	var loaded := config.load(settings_path) == OK
	for bus_name: String in BUSES:
		var volume := float(config.get_value(SECTION, bus_name.to_lower(), DEFAULT_VOLUME)) if loaded else DEFAULT_VOLUME
		set_volume(bus_name, volume, false)


func save_settings() -> void:
	var config := ConfigFile.new()
	config.load(settings_path)
	for bus_name: String in BUSES:
		config.set_value(SECTION, bus_name.to_lower(), get_volume(bus_name))
	var error := config.save(settings_path)
	if error != OK:
		push_warning("No se pudieron guardar los ajustes de audio: %s" % error_string(error))
