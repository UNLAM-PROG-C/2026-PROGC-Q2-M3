extends Node3D

@export var move_speed: float = 20.0
@export var sprint_multiplier: float = 2.0
@export var zoom_speed: float = 2.0
@export var zoom_smoothness: float = 5.0
@export var min_zoom: float = 8.0
@export var max_zoom: float = 25.0
@export var mouse_edge_margin: float = 20.0
@export var drag_sensitivity: float = 0.02
@export var rotation_step: float = 22.5
@export var rotation_smoothness: float = 5.0

@onready var camera: Camera3D = $Camera3D

var target_zoom: float
var is_dragging: bool = false
var target_rotation: float

func _ready() -> void:
	target_zoom = camera.position.y
	target_rotation = rotation.y


func _process(delta: float) -> void:
	handle_movement(delta)
	handle_zoom(delta)
	handle_rotation(delta)


func handle_movement(delta: float) -> void:
	var direction := Vector3.ZERO

	# Keyboard movement
	if Input.is_action_pressed("camera_up"):
		direction.z -= 1.0

	if Input.is_action_pressed("camera_down"):
		direction.z += 1.0

	if Input.is_action_pressed("camera_left"):
		direction.x -= 1.0

	if Input.is_action_pressed("camera_right"):
		direction.x += 1.0

	# Mouse edge scrolling
	var viewport_size := get_viewport().get_visible_rect().size
	var mouse_position := get_viewport().get_mouse_position()

	if not is_dragging and Rect2(Vector2.ZERO, viewport_size).has_point(mouse_position):
		var margin := mouse_edge_margin

		if mouse_position.x < margin:
			direction.x -= 1.0 - (mouse_position.x / margin)
		elif mouse_position.x > viewport_size.x - margin:
			direction.x += 1.0 - ((viewport_size.x - mouse_position.x) / margin)

		if mouse_position.y < margin:
			direction.z -= 1.0 - (mouse_position.y / margin)
		elif mouse_position.y > viewport_size.y - margin:
			direction.z += 1.0 - ((viewport_size.y - mouse_position.y) / margin)

	# Convert local movement direction to world direction
	direction = direction.normalized()
	direction = global_basis * direction
	direction.y = 0.0
	direction = direction.normalized()

	# Sprint
	var current_speed := move_speed

	if Input.is_action_pressed("camera_sprinting"):
		current_speed *= sprint_multiplier

	global_position += direction * current_speed * delta


func _unhandled_input(event: InputEvent) -> void:
	# Camera rotation
	if event.is_action_pressed("camera_rotate_left", false, true):
		target_rotation -= deg_to_rad(rotation_step)
		get_viewport().set_input_as_handled()
		return

	elif event.is_action_pressed("camera_rotate_right", false, true):
		target_rotation += deg_to_rad(rotation_step)
		get_viewport().set_input_as_handled()
		return

	# Start dragging
	if event.is_action_pressed("camera_drag"):
		is_dragging = true
		get_viewport().set_input_as_handled()

	# Stop dragging
	elif event.is_action_released("camera_drag"):
		is_dragging = false
		get_viewport().set_input_as_handled()

	# Handle mouse movement while dragging
	elif event is InputEventMouseMotion and is_dragging:
		var mouse_delta: Vector2 = event.relative

		# Get camera's horizontal right direction
		var camera_right := camera.global_basis.x
		camera_right.y = 0.0
		camera_right = camera_right.normalized()

		# Get camera's horizontal up direction
		var camera_up := Vector3.UP.cross(camera_right).normalized()

		# Move camera opposite to mouse movement
		global_position += (
			-camera_right * mouse_delta.x
			+ camera_up * mouse_delta.y
		) * drag_sensitivity

		get_viewport().set_input_as_handled()

	# Zoom
	elif event.is_action_pressed("camera_zoom_in"):
		zoom(-zoom_speed)

	elif event.is_action_pressed("camera_zoom_out"):
		zoom(zoom_speed)


func zoom(amount: float) -> void:
	target_zoom = clampf(
		target_zoom + amount,
		min_zoom,
		max_zoom
	)


func handle_zoom(delta: float) -> void:
	camera.position.y = lerpf(
		camera.position.y,
		target_zoom,
		zoom_smoothness * delta
	)
	
func handle_rotation(delta: float) -> void:
	rotation.y = lerp_angle(
		rotation.y,
		target_rotation,
		rotation_smoothness * delta
	)
