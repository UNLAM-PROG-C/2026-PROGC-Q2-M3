
extends Control

@export var rts_camera: Node3D

@export var line_color: Color = Color(1.0, 0.3, 0.2, 0.8)
@export var line_width: float = 2.0


func _process(_delta: float) -> void:
	queue_redraw()


func _draw() -> void:
	if rts_camera == null:
		return

	var margin: float = rts_camera.mouse_edge_margin

	var w := size.x
	var h := size.y
	var m := margin

	# Top edge
	draw_line(
		Vector2(0, m),
		Vector2(w, m),
		line_color,
		line_width
	)

	# Bottom edge
	draw_line(
		Vector2(0, h - m),
		Vector2(w, h - m),
		line_color,
		line_width
	)

	# Left edge
	draw_line(
		Vector2(m, 0),
		Vector2(m, h),
		line_color,
		line_width
	)

	# Right edge
	draw_line(
		Vector2(w - m, 0),
		Vector2(w - m, h),
		line_color,
		line_width
	)


func _notification(what: int) -> void:
	if what == NOTIFICATION_RESIZED:
		queue_redraw()
