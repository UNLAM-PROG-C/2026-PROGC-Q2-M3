
extends Node3D

const MODELS_PATH := "res://assets/units/mini_characters/"

var model_paths: Array[String] = []
var current_model_index: int = 0

var animations: PackedStringArray = []
var current_animation_index: int = 0

var model_container: Node3D
var animation_player: AnimationPlayer


func _ready() -> void:
	# Crear el contenedor de modelos.
	model_container = Node3D.new()
	model_container.name = "ModelContainer"
	add_child(model_container)

	# Buscar todos los modelos disponibles.
	find_models(MODELS_PATH)

	model_paths.sort()

	if model_paths.is_empty():
		push_error("No se encontraron modelos GLB")
		return

	print("Modelos disponibles: ", model_paths.size())

	# Cargar el primer modelo.
	load_model(0)


func _process(_delta: float) -> void:
	if Input.is_action_just_pressed("debug_next_animation"):
		debug_next_animation()

	elif Input.is_action_just_pressed("debug_previous_animation"):
		debug_previous_animation()

	if Input.is_action_just_pressed("debug_next_model"):
		debug_next_model()

	elif Input.is_action_just_pressed("debug_previous_model"):
		debug_previous_model()


# Buscar archivos GLB recursivamente.
func find_models(path: String) -> void:
	var directory := DirAccess.open(path)

	if directory == null:
		push_error("No se pudo abrir el directorio: " + path)
		return

	directory.list_dir_begin()

	var file_name := directory.get_next()

	while file_name != "":
		if directory.current_is_dir():
			if not file_name.begins_with("."):
				find_models(path.path_join(file_name) + "/")

		elif file_name.to_lower().ends_with(".glb"):
			model_paths.append(path.path_join(file_name))

		file_name = directory.get_next()

	directory.list_dir_end()


# Cargar un modelo según su índice.
func load_model(index: int) -> void:
	current_model_index = index

	# Eliminar el modelo anterior.
	for child in model_container.get_children():
		child.queue_free()

	animation_player = null
	animations.clear()
	current_animation_index = 0

	# Cargar e instanciar el nuevo modelo.
	var model_scene := load(model_paths[current_model_index]) as PackedScene

	if model_scene == null:
		push_error("No se pudo cargar el modelo: " + model_paths[current_model_index])
		return

	var model_instance := model_scene.instantiate()
	model_container.add_child(model_instance)

	print("--------------------------------")
	print(
		"Modelo ",
		current_model_index + 1,
		"/",
		model_paths.size(),
		": ",
		model_paths[current_model_index].get_file()
	)

	# Buscar el AnimationPlayer del nuevo modelo.
	animation_player = model_instance.find_child(
		"AnimationPlayer", true, false
	) as AnimationPlayer

	if animation_player == null:
		push_warning("Este modelo no contiene AnimationPlayer")
		return

	# Obtener las animaciones del nuevo modelo.
	animations = animation_player.get_animation_list()

	if animations.is_empty():
		push_warning("Este modelo no contiene animaciones")
		return

	print("Animaciones disponibles:")

	for animation in animations:
		print(animation)

	# Reproducir la primera animación.
	play_current_animation()


# Cambiar al siguiente modelo.
func debug_next_model() -> void:
	current_model_index += 1

	if current_model_index >= model_paths.size():
		current_model_index = 0

	load_model(current_model_index)


# Cambiar al modelo anterior.
func debug_previous_model() -> void:
	current_model_index -= 1

	if current_model_index < 0:
		current_model_index = model_paths.size() - 1

	load_model(current_model_index)


# Reproducir la animación seleccionada.
func play_current_animation() -> void:
	if animation_player == null or animations.is_empty():
		return

	var animation_name := animations[current_animation_index]

	print(
		"Animación ",
		current_animation_index + 1,
		"/",
		animations.size(),
		": ",
		animation_name
	)

	var animation := animation_player.get_animation(animation_name)
	animation.loop_mode = Animation.LOOP_LINEAR

	animation_player.play(animation_name)


# Cambiar a la siguiente animación.
func debug_next_animation() -> void:
	if animations.is_empty():
		return

	current_animation_index += 1

	if current_animation_index >= animations.size():
		current_animation_index = 0

	play_current_animation()


# Cambiar a la animación anterior.
func debug_previous_animation() -> void:
	if animations.is_empty():
		return

	current_animation_index -= 1

	if current_animation_index < 0:
		current_animation_index = animations.size() - 1

	play_current_animation()
