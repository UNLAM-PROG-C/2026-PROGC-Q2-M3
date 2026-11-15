extends Node3D
class_name ClientNavigation

signal navigation_revision_ready(revision: int)
signal navigation_state_changed
signal navigation_bake_failed(revision: int, detail: String)

const OBSTACLE_HEIGHT := 4.0
const TARGET_POSITION_EPSILON := 0.0001

var required_revision := -1
var ready_revision := -1
var last_error := ""
var _is_invalidated := false

var _map_data: Dictionary = {}
var _building_data: Array[Dictionary] = []
var _navigation_region: NavigationRegion3D
var _navigation_sources: Node3D
var _floor_source: MeshInstance3D
var _is_configured := false
var _is_baking := false
var _bake_queued := false
var _baking_revision := -1


func configure(map_data: Dictionary, buildings: Array, revision: int) -> void:
	_is_invalidated = false
	last_error = ""
	_map_data = map_data.duplicate(true)
	_building_data.clear()
	for building: Variant in buildings:
		if typeof(building) == TYPE_DICTIONARY:
			_building_data.append(building.duplicate(true))
	required_revision = revision
	ready_revision = -1
	if _navigation_region == null:
		_create_navigation_region()
	_is_configured = true
	if is_inside_tree():
		_start_bake()


func request_revision(map_data: Dictionary, buildings: Array, revision: int) -> void:
	last_error = ""
	if not _is_configured:
		configure(map_data, buildings, revision)
		return
	_map_data = map_data.duplicate(true)
	_building_data.clear()
	for building: Variant in buildings:
		if typeof(building) == TYPE_DICTIONARY:
			_building_data.append(building.duplicate(true))
	required_revision = revision
	ready_revision = -1
	if _is_baking:
		_bake_queued = true
	else:
		_start_bake()


func is_ready_for(revision: int) -> bool:
	return ready_revision >= revision and not _is_baking and not _bake_queued


func is_invalidated() -> bool:
	return _is_invalidated


func invalidate() -> void:
	_is_invalidated = true
	navigation_state_changed.emit()


func calculate_candidate_route(origin_xz: Vector2, destination_xz: Vector2, revision: int) -> Dictionary:
	if not is_ready_for(revision):
		return {
			"ok": false,
			"error": "NavigationNotReady",
			"detail": "La navegación todavía no incorporó la revisión %d." % revision,
		}
	var cell_size := float(_map_data.get("cellSize", 0.0))
	var map_width := float(_map_data.get("columns", 0)) * cell_size
	var map_depth := float(_map_data.get("rows", 0)) * cell_size
	if (
		cell_size <= 0.0
		or not _is_inside_map(origin_xz, map_width, map_depth)
		or not _is_inside_map(destination_xz, map_width, map_depth)
	):
		return {
			"ok": false,
			"error": "RoutePointOutOfBounds",
			"detail": "El origen o el destino está fuera del mapa navegable.",
		}
	if origin_xz == destination_xz:
		return {"ok": true, "waypoints": []}

	var query_root := Node3D.new()
	query_root.name = "NavigationQuery"
	add_child(query_root)
	query_root.global_position = Vector3(origin_xz.x, 0.0, origin_xz.y)
	var agent := NavigationAgent3D.new()
	agent.path_desired_distance = TARGET_POSITION_EPSILON
	agent.target_desired_distance = TARGET_POSITION_EPSILON
	agent.set_navigation_map(_navigation_region.get_navigation_map())
	query_root.add_child(agent)
	agent.target_position = Vector3(destination_xz.x, 0.0, destination_xz.y)

	await get_tree().physics_frame
	if not is_instance_valid(agent) or not is_inside_tree():
		return {
			"ok": false,
			"error": "NavigationQueryCancelled",
			"detail": "La consulta de navegación terminó con el mundo activo.",
		}
	var _next_path_position: Vector3 = agent.get_next_path_position()
	await get_tree().physics_frame
	if not is_instance_valid(agent) or not is_inside_tree():
		return {
			"ok": false,
			"error": "NavigationQueryCancelled",
			"detail": "La consulta de navegación terminó con el mundo activo.",
		}

	var path: PackedVector3Array = agent.get_current_navigation_path()
	var final_position: Vector3 = agent.get_final_position()
	if path.size() < 2:
		query_root.queue_free()
		return {
			"ok": false,
			"error": "NoRouteFound",
			"detail": "NavigationAgent3D no encontró una ruta completa.",
		}
	if not agent.is_target_reachable() or not final_position.is_equal_approx(
		Vector3(destination_xz.x, 0.0, destination_xz.y)
	):
		query_root.queue_free()
		return {
			"ok": false,
			"error": "DestinationNotReachable",
			"detail": "La ruta de navegación no termina exactamente en el destino solicitado.",
		}
	if not path[0].is_equal_approx(Vector3(origin_xz.x, 0.0, origin_xz.y)):
		query_root.queue_free()
		return {
			"ok": false,
			"error": "OriginNotNavigable",
			"detail": "La posición oficial de origen no coincide con la navegación vigente.",
		}

	var waypoints: Array[Dictionary] = []
	for index in range(1, path.size()):
		var point: Vector3 = path[index]
		if (
			not is_finite(point.x)
			or not is_finite(point.z)
			or not _is_inside_map(Vector2(point.x, point.z), map_width, map_depth)
		):
			query_root.queue_free()
			return {
				"ok": false,
				"error": "InvalidNavigationPath",
				"detail": "NavigationAgent3D devolvió un waypoint no finito o fuera del mapa.",
			}
		waypoints.append({"x": float(point.x), "z": float(point.z)})

	query_root.queue_free()
	if waypoints.is_empty():
		return {
			"ok": false,
			"error": "IncompleteNavigationPath",
			"detail": "La unidad no está en el destino, pero la ruta no contiene waypoints.",
		}
	var last_waypoint: Dictionary = waypoints[-1]
	if (
		not is_equal_approx(float(last_waypoint["x"]), destination_xz.x)
		or not is_equal_approx(float(last_waypoint["z"]), destination_xz.y)
	):
		return {
			"ok": false,
			"error": "DestinationNotReachable",
			"detail": "La ruta candidata no termina en el destino solicitado.",
		}
	return {"ok": true, "waypoints": waypoints}


func _ready() -> void:
	if _is_configured:
		await get_tree().physics_frame
		if not is_instance_valid(self):
			return
		_start_bake()


func _create_navigation_region() -> void:
	var map_cell_size := float(_map_data.get("cellSize", 0.0))
	var map_width := float(_map_data.get("columns", 0)) * map_cell_size
	var map_depth := float(_map_data.get("rows", 0)) * map_cell_size
	if map_cell_size <= 0.0 or map_width <= 0.0 or map_depth <= 0.0:
		_fail_bake("El mapa no tiene dimensiones navegables válidas.")
		return

	var navigation_mesh := NavigationMesh.new()
	navigation_mesh.agent_radius = map_cell_size * 0.1
	navigation_mesh.geometry_parsed_geometry_type = NavigationMesh.PARSED_GEOMETRY_MESH_INSTANCES
	navigation_mesh.geometry_source_geometry_mode = NavigationMesh.SOURCE_GEOMETRY_ROOT_NODE_CHILDREN
	navigation_mesh.filter_baking_aabb = AABB(
		Vector3(0.0, -0.25, 0.0),
		Vector3(map_width, 2.0, map_depth)
	)

	_navigation_region = NavigationRegion3D.new()
	_navigation_region.name = "NavigationRegion"
	_navigation_region.navigation_mesh = navigation_mesh
	_navigation_region.bake_finished.connect(_on_bake_finished)
	add_child(_navigation_region)

	_navigation_sources = Node3D.new()
	_navigation_sources.name = "NavigationSources"
	_navigation_region.add_child(_navigation_sources)
	var floor_mesh := PlaneMesh.new()
	floor_mesh.size = Vector2(map_width, map_depth)
	_floor_source = MeshInstance3D.new()
	_floor_source.name = "NavigationFloor"
	_floor_source.mesh = floor_mesh
	_floor_source.position = Vector3(map_width / 2.0, 0.0, map_depth / 2.0)
	_navigation_sources.add_child(_floor_source)


func _start_bake() -> void:
	if not is_inside_tree() or not _is_configured or _is_baking:
		return
	if _navigation_region == null:
		_create_navigation_region()
	if _navigation_region == null:
		_fail_bake("No se pudo crear NavigationRegion3D.")
		return

	_replace_obstacle_sources()
	_baking_revision = required_revision
	_bake_queued = false
	_is_baking = true
	_navigation_region.bake_navigation_mesh(true)


func _replace_obstacle_sources() -> void:
	for source: Node in _navigation_sources.get_children():
		if source != _floor_source:
			_navigation_sources.remove_child(source)
			source.free()

	var cell_size := float(_map_data["cellSize"])
	for building: Dictionary in _building_data:
		var footprint: Dictionary = building.get("footprint", {})
		if footprint.is_empty():
			continue
		var width := float(footprint.get("width", 0)) * cell_size
		var depth := float(footprint.get("height", 0)) * cell_size
		if width <= 0.0 or depth <= 0.0:
			continue
		var obstacle_mesh := BoxMesh.new()
		obstacle_mesh.size = Vector3(width, OBSTACLE_HEIGHT, depth)
		var obstacle := MeshInstance3D.new()
		obstacle.name = "BuildingObstacle_%d" % int(building.get("buildingId", 0))
		obstacle.mesh = obstacle_mesh
		obstacle.position = Vector3(
			(float(footprint.get("column", 0)) + float(footprint.get("width", 0)) / 2.0) * cell_size,
			OBSTACLE_HEIGHT / 2.0,
			(float(footprint.get("row", 0)) + float(footprint.get("height", 0)) / 2.0) * cell_size
		)
		_navigation_sources.add_child(obstacle)


func _on_bake_finished() -> void:
	var completed_revision := _baking_revision
	_is_baking = false
	await get_tree().physics_frame
	if not is_inside_tree():
		return
	if _bake_queued or completed_revision != required_revision:
		_start_bake()
		return
	ready_revision = completed_revision
	last_error = ""
	navigation_revision_ready.emit(ready_revision)
	navigation_state_changed.emit()


func _fail_bake(detail: String) -> void:
	last_error = detail
	navigation_bake_failed.emit(required_revision, detail)
	navigation_state_changed.emit()


func _is_inside_map(point: Vector2, map_width: float, map_depth: float) -> bool:
	return (
		is_finite(point.x)
		and is_finite(point.y)
		and point.x >= 0.0
		and point.y >= 0.0
		and point.x < map_width
		and point.y < map_depth
	)
