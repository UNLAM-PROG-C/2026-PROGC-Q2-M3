extends RefCounted

## Textos para el jugador de los códigos de error de comandos. El servidor los publica como
## `Área.Nombre` (D-007); el cliente usa el mismo nombre sin área para los rechazos locales,
## así que se busca primero el código completo y después solo el nombre.

const TEXTS := {
	# Common
	"EntityNotFound": "El objetivo ya no existe.",
	"NotOwner": "Esa entidad no es tuya.",
	"IncompatibleEntity": "Esas unidades no pueden cumplir esa orden.",
	"MatchNotRunning": "La partida no está en curso.",
	"CommandQueueFull": "El servidor está ocupado. Probá de nuevo.",
	"InvalidCommand": "Orden inválida.",
	# Network (rechazos antes de admitir, D-075)
	"Network.RateLimited": "Demasiadas órdenes seguidas. Esperá un momento.",
	"Network.NotRegistered": "Todavía no tenés plaza en la partida.",
	# Economy
	"InsufficientWood": "No alcanza la madera.",
	"InsufficientFood": "No alcanza la comida.",
	"InsufficientResources": "No alcanzan los recursos.",
	# Construction
	"NotBuildable": "Ese edificio no se puede construir.",
	"OutOfBounds": "Fuera del mapa.",
	"CellOccupied": "El lugar está ocupado.",
	"TreeReserved": "Hay un árbol en ese lugar.",
	"UnitInFootprint": "Hay unidades en ese lugar.",
	"NoReachableWorker": "Ningún trabajador puede llegar a la obra.",
	"AlreadyCompleted": "Ese edificio ya está terminado.",
	"NotAFarm": "Solo se pueden reparar granjas propias terminadas.",
	"FarmNotDepleted": "La granja todavía produce.",
	# Production
	"NotAProducer": "Ese edificio no entrena unidades.",
	"WrongUnitType": "Ese edificio no entrena ese tipo de unidad.",
	"ProducerNotOperational": "El edificio todavía no está operativo.",
	"QueueFull": "La cola de entrenamiento está llena.",
	# Combat
	"InvalidTarget": "No se puede atacar eso.",
	"FriendlyTarget": "No se puede atacar algo propio.",
	# Movement
	"InvalidPath": "La ruta quedó bloqueada.",
	"TooManyWaypoints": "La ruta es demasiado larga.",
	"RouteDoesNotReachGoal": "No se puede llegar a ese lugar.",
	"DestinationOutOfBounds": "Ese destino está fuera del mapa.",
	"StaleOrder": "La orden ya cambió.",
	"Unreachable": "No se puede llegar a ese lugar.",
	# Rechazos locales del cliente
	"TargetNotReachable": "No se puede llegar a ese objetivo.",
	"NoRouteFound": "No se encontró un camino.",
	"DestinationNotReachable": "No se puede llegar a ese lugar.",
	"CommandAlreadyPending": "Esa unidad todavía espera la respuesta de su orden anterior.",
	"TreeDepleted": "El árbol está agotado.",
	"NoCompatibleUnits": "Ninguna unidad elegida puede cumplir esa orden.",
	"SendFailed": "No se pudo enviar la orden.",
}


static func text_for(error_code: String) -> String:
	if TEXTS.has(error_code):
		return TEXTS[error_code]
	var name := error_code.get_slice(".", error_code.get_slice_count(".") - 1)
	if TEXTS.has(name):
		return TEXTS[name]
	return "Orden rechazada (%s)." % error_code


## Primer error de un CommandResult oficial, o "" si fue aceptado por completo (contrato 09 §5).
static func command_result_error(result: Dictionary) -> String:
	var outcome := str(result.get("outcome", ""))
	if outcome == "Accepted":
		return ""
	var error: Variant = result.get("error", null)
	if error != null and not str(error).is_empty():
		return str(error)
	for entity_result: Variant in result.get("entityResults", []):
		if entity_result is Dictionary and entity_result.get("error", null) != null:
			return str(entity_result["error"])
	return "" if outcome == "PartiallyAccepted" else "Rejected"
