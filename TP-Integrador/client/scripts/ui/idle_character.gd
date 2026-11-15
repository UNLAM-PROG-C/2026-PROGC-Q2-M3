extends Node3D

## Personaje de decorado: reproduce en loop una animación del modelo que tiene como hijo.
## Las animaciones importadas de los modelos no tienen loop, así que se reproduce una copia
## con loop para no modificar el recurso compartido por las unidades del juego.

const LIBRARY_NAME := "decor"

@export var animation_name := "idle"


func _ready() -> void:
	var player := find_child("AnimationPlayer", true, false) as AnimationPlayer
	if player == null:
		push_warning("%s no tiene AnimationPlayer." % name)
		return
	if not player.has_animation(animation_name):
		push_warning("%s no tiene la animación %s." % [name, animation_name])
		return
	var looped: Animation = player.get_animation(animation_name).duplicate()
	looped.loop_mode = Animation.LOOP_LINEAR
	var library := AnimationLibrary.new()
	library.add_animation(animation_name, looped)
	if player.has_animation_library(LIBRARY_NAME):
		player.remove_animation_library(LIBRARY_NAME)
	player.add_animation_library(LIBRARY_NAME, library)
	player.play("%s/%s" % [LIBRARY_NAME, animation_name])
