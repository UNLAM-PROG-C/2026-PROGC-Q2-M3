namespace Rts.Shared.Entities;

/// <summary>Estado conceptual de un edificio productor (05 §4).</summary>
public enum ProducerStatus
{
  /// <summary>Sin tarea activa ni solicitudes pendientes.</summary>
  Idle = 1,

  /// <summary>Sin tarea activa, con solicitudes pendientes que todavía no pudieron iniciarse por falta de comida.</summary>
  WaitingForResources = 2,

  /// <summary>Tarea activa pagada, con el temporizador avanzando.</summary>
  Training = 3,

  /// <summary>Entrenamiento terminado sin celda de aparición válida; ocupa la tarea activa y no vuelve a cobrar.</summary>
  PendingSpawn = 4,
}
