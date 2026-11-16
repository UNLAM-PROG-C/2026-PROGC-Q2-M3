extends Node3D

## Personaje de decorado: reproduce en loop una animación del modelo que tiene como hijo.
## Las animaciones importadas de los modelos no tienen loop, así que se reproduce una copia
## con loop para no modificar el recurso compartido por las unidades del juego.

const AnimationLoops = preload("res://scripts/animation_loops.gd")
const LIBRARY_NAME := "decor"

@export var animation_name := "idle"


func _ready() -> void:
	var player := find_child("AnimationPlayer", true, false) as AnimationPlayer
	if player == null:
		push_warning("%s no tiene AnimationPlayer." % name)
		return
	if AnimationLoops.ensure_looped_library(player, LIBRARY_NAME, [animation_name]).is_empty():
		push_warning("%s no tiene la animación %s." % [name, animation_name])
		return
	player.play("%s/%s" % [LIBRARY_NAME, animation_name])
