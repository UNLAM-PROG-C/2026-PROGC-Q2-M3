namespace Rts.Shared.Ids;

/// <summary>
/// Identificador de la sesión de partida (una por proceso servidor).
/// RECOMENDADO (contrato 00 §3): GUID, para distinguir ejecuciones en logs y mensajes.
/// </summary>
public readonly record struct SessionId(Guid Value)
{
  public static SessionId New() => new(Guid.NewGuid());

  public override string ToString() => Value.ToString("D");
}
