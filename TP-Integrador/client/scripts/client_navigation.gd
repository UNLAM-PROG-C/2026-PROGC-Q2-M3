extends Node3D
class_name ClientNavigation

signal navigation_revision_ready(revision: int)
signal navigation_state_changed
signal navigation_bake_failed(revision: int, detail: String)

const OBSTACLE_HEIGHT := 4.0
const TARGET_POSITION_EPSILON := 0.0001
## Distancia máxima (en celdas) entre la posición oficial y la malla de navegación para aceptar el origen. Las unidades
## trabajan pegadas a edificios y árboles y pueden quedar dentro del margen que la malla deja alrededor de los obstáculos
## (agent_radius); en ese caso la ruta arranca con un tramo corto hasta la malla, que cae en celdas libres.
const ORIGIN_SNAP_TOLERANCE_CELLS := 0.5
## Margen que se resta al rango de interacción para que el final de la ruta quede
## dentro del rango aunque el servidor use la posición exacta y el cliente la redondeada.
const RANGE_SAFETY_MARGIN := 0.05
const MAX_MAP_SYNC_FRAMES := 120

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
var _region_iteration_before_bake := 0


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
	if origin_xz.distance_to(destination_xz) <= TARGET_POSITION_EPSILON:
		return {"ok": true, "waypoints": []}

	var query: Dictionary = await _query_navigation_path(origin_xz, destination_xz)
	if not bool(query.get("ok", false)):
		return query
	if not bool(query["reachable"]):
		return {
			"ok": false,
			"error": "DestinationNotReachable",
			"detail": "La ruta de navegación no termina exactamente en el destino solicitado.",
		}
	var waypoints: Array[Dictionary] = query["waypoints"]
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


## Consulta de ruta sobre el NavigationMesh vigente. La ruta termina en el destino o,
## si no es alcanzable, en el punto alcanzable más cercano (`reachable` = false).
## Todas las comparaciones son en X/Z: la altura de Godot no es autoridad (D-029) y el
## NavigationMesh horneado queda por encima del plano Y = 0.
func _query_navigation_path(origin_xz: Vector2, destination_xz: Vector2) -> Dictionary:
	var cell_size := float(_map_data.get("cellSize", 0.0))
	var map_width := float(_map_data.get("columns", 0)) * cell_size
	var map_depth := float(_map_data.get("rows", 0)) * cell_size
	await get_tree().physics_frame
	if not is_inside_tree() or _navigation_region == null:
		return {
			"ok": false,
			"error": "NavigationQueryCancelled",
			"detail": "La consulta de navegación terminó con el mundo activo.",
		}
	var parameters := NavigationPathQueryParameters3D.new()
	parameters.map = _navigation_region.get_navigation_map()
	parameters.start_position = Vector3(origin_xz.x, 0.0, origin_xz.y)
	parameters.target_position = Vector3(destination_xz.x, 0.0, destination_xz.y)
	var query_result := NavigationPathQueryResult3D.new()
	NavigationServer3D.query_path(parameters, query_result)
	var path: PackedVector3Array = query_result.path
	if path.size() < 2:
		return {
			"ok": false,
			"error": "NoRouteFound",
			"detail": "La navegación no encontró una ruta completa.",
		}
	var navigable_start := Vector2(path[0].x, path[0].z)
	var origin_gap := navigable_start.distance_to(origin_xz)
	if origin_gap > cell_size * ORIGIN_SNAP_TOLERANCE_CELLS:
		return {
			"ok": false,
			"error": "OriginNotNavigable",
			"detail": "La posición oficial de origen no coincide con la navegación vigente.",
		}

	var waypoints: Array[Dictionary] = []
	if origin_gap > TARGET_POSITION_EPSILON:
		# Origen dentro del margen de un obstáculo: primero un tramo corto hasta la malla.
		waypoints.append({"x": float(navigable_start.x), "z": float(navigable_start.y)})
	for index in range(1, path.size()):
		var point: Vector3 = path[index]
		if (
			not is_finite(point.x)
			or not is_finite(point.z)
			or not _is_inside_map(Vector2(point.x, point.z), map_width, map_depth)
		):
			return {
				"ok": false,
				"error": "InvalidNavigationPath",
				"detail": "La navegación devolvió un waypoint no finito o fuera del mapa.",
			}
		waypoints.append({"x": float(point.x), "z": float(point.z)})
	var final_xz := Vector2(path[-1].x, path[-1].z)
	return {
		"ok": true,
		"waypoints": waypoints,
		"reachable": final_xz.distance_to(destination_xz) <= TARGET_POSITION_EPSILON,
	}


## Ruta candidata para una orden de tarea: termina en el primer punto del trayecto
## que queda dentro del rango de interacción del objetivo (contrato 04 §3).
## `target` es {"point": Vector2} para unidades y árboles, o {"rect": Rect2} para
## la huella de un edificio, medida a su borde más cercano (D-019).
func calculate_candidate_route_to_range(
	origin_xz: Vector2,
	target: Dictionary,
	interaction_range: float,
	revision: int
) -> Dictionary:
	if not is_ready_for(revision):
		return {
			"ok": false,
			"error": "NavigationNotReady",
			"detail": "La navegación todavía no incorporó la revisión %d." % revision,
		}
	if not is_valid_target(target) or not is_finite(interaction_range) or interaction_range <= 0.0:
		return {
			"ok": false,
			"error": "InvalidTarget",
			"detail": "El objetivo o el rango de interacción no son válidos.",
		}
	var effective_range := effective_interaction_range(interaction_range)
	if distance_to_target(origin_xz, target) <= effective_range:
		return {"ok": true, "waypoints": []}

	var cell_size := float(_map_data.get("cellSize", 0.0))
	var map_width := float(_map_data.get("columns", 0)) * cell_size
	var map_depth := float(_map_data.get("rows", 0)) * cell_size
	if cell_size <= 0.0 or not _is_inside_map(origin_xz, map_width, map_depth):
		return {
			"ok": false,
			"error": "RoutePointOutOfBounds",
			"detail": "El origen está fuera del mapa navegable.",
		}
	# Se pide la ruta hacia puntos del objetivo (el más cercano primero). La ruta llega
	# al punto alcanzable más cercano aunque el objetivo esté dentro de un obstáculo, y
	# se recorta donde entra en el rango.
	var last_failure: Dictionary = {}
	for goal: Vector2 in _range_goal_candidates(origin_xz, target, map_width, map_depth):
		var query: Dictionary = await _query_navigation_path(origin_xz, goal)
		if not bool(query.get("ok", false)):
			last_failure = query
			if ["NavigationQueryCancelled", "OriginNotNavigable"].has(str(query.get("error", ""))):
				return query
			continue
		var trimmed := trim_route_to_range(origin_xz, query["waypoints"], target, effective_range)
		if trimmed.is_empty():
			continue
		var last: Dictionary = trimmed[-1]
		if distance_to_target(Vector2(float(last["x"]), float(last["z"])), target) <= effective_range:
			return {"ok": true, "waypoints": trimmed}
	if not last_failure.is_empty() and str(last_failure.get("error", "")) != "NoRouteFound":
		return last_failure
	return {
		"ok": false,
		"error": "TargetNotReachable",
		"detail": "La ruta de navegación no llega al rango del objetivo.",
	}


static func is_valid_target(target: Dictionary) -> bool:
	if target.has("point"):
		var point: Variant = target["point"]
		return typeof(point) == TYPE_VECTOR2 and is_finite(point.x) and is_finite(point.y)
	if target.has("rect"):
		var rect: Variant = target["rect"]
		return typeof(rect) == TYPE_RECT2 and rect.size.x > 0.0 and rect.size.y > 0.0
	return false


static func effective_interaction_range(interaction_range: float) -> float:
	return maxf(interaction_range - minf(RANGE_SAFETY_MARGIN, interaction_range * 0.1), 0.0)


## Distancia de un punto al objetivo: entre puntos, o al borde más cercano de la
## huella (0 dentro de ella), igual que `WorldRect.DistanceTo` del servidor.
static func distance_to_target(point: Vector2, target: Dictionary) -> float:
	if target.has("point"):
		return point.distance_to(target["point"])
	return point.distance_to(closest_point_on_target(point, target))


static func closest_point_on_target(point: Vector2, target: Dictionary) -> Vector2:
	if target.has("point"):
		return target["point"]
	var rect: Rect2 = target["rect"]
	return Vector2(
		clampf(point.x, rect.position.x, rect.end.x),
		clampf(point.y, rect.position.y, rect.end.y)
	)


## Corta la ruta en el primer punto del trayecto que entra en el rango del objetivo.
## Como el corte cae sobre un tramo ya trazado, la ruta recortada sigue siendo válida.
static func trim_route_to_range(
	origin_xz: Vector2,
	waypoints: Array,
	target: Dictionary,
	interaction_range: float
) -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	if distance_to_target(origin_xz, target) <= interaction_range:
		return result
	var segment_start := origin_xz
	for waypoint: Dictionary in waypoints:
		var segment_end := Vector2(float(waypoint["x"]), float(waypoint["z"]))
		if distance_to_target(segment_end, target) <= interaction_range:
			var entry_point := _first_point_in_range(segment_start, segment_end, target, interaction_range)
			result.append({"x": float(entry_point.x), "z": float(entry_point.y)})
			return result
		result.append({"x": float(segment_end.x), "z": float(segment_end.y)})
		segment_start = segment_end
	return result


static func _first_point_in_range(
	segment_start: Vector2,
	segment_end: Vector2,
	target: Dictionary,
	interaction_range: float
) -> Vector2:
	# Bisección sobre el tramo: el extremo final está en rango y el inicial no.
	# La distancia a un punto o a un rectángulo es convexa sobre un segmento,
	# así que hay un único cruce del umbral.
	var outside := 0.0
	var inside := 1.0
	for _iteration in range(40):
		var middle := (outside + inside) / 2.0
		if distance_to_target(segment_start.lerp(segment_end, middle), target) <= interaction_range:
			inside = middle
		else:
			outside = middle
	return segment_start.lerp(segment_end, inside)


func _range_goal_candidates(
	origin_xz: Vector2,
	target: Dictionary,
	map_width: float,
	map_depth: float
) -> Array[Vector2]:
	var candidates: Array[Vector2] = [closest_point_on_target(origin_xz, target)]
	if target.has("rect"):
		var rect: Rect2 = target["rect"]
		var center := rect.get_center()
		var others: Array[Vector2] = [
			Vector2(center.x, rect.position.y),
			Vector2(center.x, rect.end.y),
			Vector2(rect.position.x, center.y),
			Vector2(rect.end.x, center.y),
			rect.position,
			Vector2(rect.end.x, rect.position.y),
			Vector2(rect.position.x, rect.end.y),
			rect.end,
		]
		others.sort_custom(
			func(a: Vector2, b: Vector2) -> bool:
				return a.distance_squared_to(origin_xz) < b.distance_squared_to(origin_xz)
		)
		candidates.append_array(others)
	# El borde derecho/inferior del mapa queda fuera (posición < ancho), igual que en el servidor.
	var inside: Array[Vector2] = []
	for candidate: Vector2 in candidates:
		var clamped := Vector2(
			clampf(candidate.x, 0.0, maxf(map_width - TARGET_POSITION_EPSILON, 0.0)),
			clampf(candidate.y, 0.0, maxf(map_depth - TARGET_POSITION_EPSILON, 0.0))
		)
		if not inside.has(clamped):
			inside.append(clamped)
	return inside


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
	# Sin iteraciones asincrónicas, la región reconstruida entra al mapa en la siguiente
	# sincronización y la espera de _on_bake_finished queda acotada.
	NavigationServer3D.region_set_use_async_iterations(_navigation_region.get_rid(), false)
	add_child(_navigation_region)

	_navigation_sources = Node3D.new()
	_navigation_sources.name = "NavigationSources"
	_navigation_region.add_child(_navigation_sources)
	var floor_mesh := PlaneMesh.new()
	floor_mesh.size = Vector2(map_width, map_depth)
	_floor_source = MeshInstance3D.new()
	_floor_source.name = "NavigationFloor"
	_floor_source.mesh = floor_mesh
	# Solo es geometría para hornear: no se dibuja (taparía el terreno y los modelos).
	_floor_source.visible = false
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
	_region_iteration_before_bake = NavigationServer3D.region_get_iteration_id(
		_navigation_region.get_rid()
	)
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
		obstacle.visible = false
		obstacle.position = Vector3(
			(float(footprint.get("column", 0)) + float(footprint.get("width", 0)) / 2.0) * cell_size,
			OBSTACLE_HEIGHT / 2.0,
			(float(footprint.get("row", 0)) + float(footprint.get("height", 0)) / 2.0) * cell_size
		)
		_navigation_sources.add_child(obstacle)


func _on_bake_finished() -> void:
	var completed_revision := _baking_revision
	_is_baking = false
	# El NavigationMesh horneado no se ve en las consultas hasta que la región reconstruye
	# su iteración y el mapa la incorpora en una sincronización posterior. Antes de eso,
	# las rutas salen vacías o con los obstáculos de la revisión anterior.
	var navigation_map: RID = _navigation_region.get_navigation_map()
	var region_rid: RID = _navigation_region.get_rid()
	var region_rebuilt_at_map_iteration := -1
	for _frame in range(MAX_MAP_SYNC_FRAMES):
		await get_tree().physics_frame
		if not is_inside_tree():
			return
		var map_iteration := NavigationServer3D.map_get_iteration_id(navigation_map)
		if region_rebuilt_at_map_iteration < 0:
			if NavigationServer3D.region_get_iteration_id(region_rid) > _region_iteration_before_bake:
				region_rebuilt_at_map_iteration = map_iteration
		elif map_iteration > region_rebuilt_at_map_iteration:
			break
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
