namespace Rts.Shared.Errors;

/// <summary>
/// Códigos de error transversales, usados por más de un módulo. Los errores propios de cada módulo
/// (p. ej. <c>Movement.InvalidPath</c>, <c>Production.QueueFull</c>) se declaran en ese módulo.
/// </summary>
public static class CommonErrors
{
  /// <summary>El comando no es estructural o semánticamente válido para su tipo.</summary>
  public static readonly ErrorCode InvalidCommand = new("Common.InvalidCommand");

  /// <summary>La entidad referida no pertenece al jugador que emitió el comando.</summary>
  public static readonly ErrorCode NotOwner = new("Common.NotOwner");

  /// <summary>La entidad referida no existe (nunca existió o ya fue destruida).</summary>
  public static readonly ErrorCode EntityNotFound = new("Common.EntityNotFound");

  /// <summary>El tipo de unidad o edificio no admite esa orden (p. ej. Attack sobre un trabajador).</summary>
  public static readonly ErrorCode IncompatibleEntity = new("Common.IncompatibleEntity");

  /// <summary>Comando de gameplay recibido fuera del estado Running de la sesión.</summary>
  public static readonly ErrorCode MatchNotRunning = new("Common.MatchNotRunning");

  /// <summary>Comando grupal en el que ninguna entidad fue aceptada; el detalle va en los resultados por entidad.</summary>
  public static readonly ErrorCode NoEntityAccepted = new("Common.NoEntityAccepted");

  /// <summary>La cola global de comandos está llena; el comando nuevo no se admitió (los ya admitidos no se descartan).</summary>
  public static readonly ErrorCode CommandQueueFull = new("Common.CommandQueueFull");
}
