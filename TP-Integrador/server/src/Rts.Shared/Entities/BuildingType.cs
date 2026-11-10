namespace Rts.Shared.Entities;

/// <summary>Tipos de edificio (03 §1). El valor 0 no está definido a propósito, para detectar valores sin inicializar.</summary>
public enum BuildingType
{
  /// <summary>Centro Principal: existe desde el inicio, no se construye; entrena trabajadores; su destrucción decide la partida.</summary>
  MainCenter = 1,

  /// <summary>Granja: produce comida por tandas; se agota y su capacidad puede repararse.</summary>
  Farm = 2,

  /// <summary>Cuartel: entrena soldados.</summary>
  Barracks = 3,

  /// <summary>Torre: ataca automáticamente unidades enemigas en rango; no ataca edificios.</summary>
  Tower = 4,

  /// <summary>Valla/empalizada: bloqueo por celda individual, sin puertas.</summary>
  Fence = 5,
}
