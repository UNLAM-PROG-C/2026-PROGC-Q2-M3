using Rts.Shared.Ids;

namespace Rts.Simulation.State;

/// <summary>Asignación de IDs de entidad: solo Simulation crea entidades, así que solo ella avanza el contador (D-003).</summary>
internal static class EntityIdExtensions
{
  /// <summary>El ID siguiente al dado. Desbordar el contador es un error, no una vuelta a cero.</summary>
  public static EntityId Next(this EntityId id) => new(checked(id.Value + 1));
}
