extends RefCounted

## Las animaciones importadas de los modelos no tienen loop. Para reproducirlas en loop se
## agregan copias con loop en una biblioteca aparte, sin modificar el recurso importado que
## comparten todas las instancias del modelo.


## Agrega (una vez) la biblioteca `library_name` con copias en loop de `animation_names`.
## Devuelve los nombres que se pudieron copiar.
static func ensure_looped_library(
	player: AnimationPlayer,
	library_name: String,
	animation_names: Array
) -> PackedStringArray:
	var copied: PackedStringArray = []
	if player.has_animation_library(library_name):
		var existing := player.get_animation_library(library_name)
		for animation_name: Variant in animation_names:
			if existing.has_animation(animation_name):
				copied.append(animation_name)
		return copied
	var library := AnimationLibrary.new()
	for animation_name: Variant in animation_names:
		if not player.has_animation(animation_name):
			continue
		var looped: Animation = player.get_animation(animation_name).duplicate()
		looped.loop_mode = Animation.LOOP_LINEAR
		library.add_animation(animation_name, looped)
		copied.append(animation_name)
	player.add_animation_library(library_name, library)
	return copied
