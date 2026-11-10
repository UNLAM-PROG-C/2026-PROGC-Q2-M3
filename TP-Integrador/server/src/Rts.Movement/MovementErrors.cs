using Rts.Shared.Errors;

namespace Rts.Movement;

/// <summary>Códigos de error del área Movement (D-007; nombres sugeridos en 04 §11).</summary>
public static class MovementErrors
{
  /// <summary>Un tramo de la ruta sale del mapa, toca una celda bloqueada o corta una esquina bloqueada.</summary>
  public static readonly ErrorCode InvalidPath = new("Movement.InvalidPath");

  /// <summary>La ruta supera el máximo configurado de waypoints.</summary>
  public static readonly ErrorCode TooManyWaypoints = new("Movement.TooManyWaypoints");

  /// <summary>La ruta no termina en el destino (tolerancia de llegada) ni en rango de interacción del objetivo.</summary>
  public static readonly ErrorCode RouteDoesNotReachGoal = new("Movement.RouteDoesNotReachGoal");

  /// <summary>El destino de un Move está fuera del mapa.</summary>
  public static readonly ErrorCode DestinationOutOfBounds = new("Movement.DestinationOutOfBounds");

  /// <summary>El RepathResponse refiere una orden que ya no es la vigente de la unidad.</summary>
  public static readonly ErrorCode StaleOrder = new("Movement.StaleOrder");

  /// <summary>No existe forma de llegar al rango de interacción del objetivo.</summary>
  public static readonly ErrorCode Unreachable = new("Movement.Unreachable");
}
