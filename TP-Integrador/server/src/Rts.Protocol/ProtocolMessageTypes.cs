using System.Collections.Frozen;
using Rts.Shared.Commands;

namespace Rts.Protocol;

/// <summary>
/// Valores del campo <c>type</c> del sobre (contrato 08 §3). Son la lista cerrada de mensajes del protocolo: un tipo que no está acá
/// es <c>Network.UnknownType</c>.
/// </summary>
public static class ProtocolMessageTypes
{
  // Cliente → servidor: sesión y conectividad.
  public const string Hello = "Hello";
  public const string ClientReady = "ClientReady";

  // Ambos sentidos.
  public const string Ping = "Ping";
  public const string Pong = "Pong";

  // Cliente → servidor: gameplay (exigen messageId = ClientCommandId, 08 §3).
  public const string Move = "Move";
  public const string Stop = "Stop";
  public const string Attack = "Attack";
  public const string GatherWood = "GatherWood";
  public const string Build = "Build";
  public const string RepairFarm = "RepairFarm";
  public const string TrainUnit = "TrainUnit";
  public const string RepathResponse = "RepathResponse";

  // Servidor → cliente: sesión (contrato 07).
  public const string PlayerAssigned = "PlayerAssigned";
  public const string StartAborted = "StartAborted";
  public const string MatchStarted = "MatchStarted";
  public const string MatchEnded = "MatchEnded";
  public const string SessionRequestRejected = "SessionRequestRejected";

  // Servidor → cliente: estado (contrato 09).
  public const string InitialState = "InitialState";
  public const string StateEvents = "StateEvents";
  public const string PositionUpdate = "PositionUpdate";

  // Servidor → cliente: transporte (contrato 08).
  public const string CommandRejected = "CommandRejected";
  public const string ProtocolError = "ProtocolError";
  public const string Disconnecting = "Disconnecting";

  /// <summary>Tipos de gameplay y su <see cref="CommandType"/>. Son los únicos que pasan por Command Admission.</summary>
  public static FrozenDictionary<string, CommandType> GameplayTypes { get; } = new Dictionary<string, CommandType>
  {
    [Move] = CommandType.Move,
    [Stop] = CommandType.Stop,
    [Attack] = CommandType.Attack,
    [GatherWood] = CommandType.GatherWood,
    [Build] = CommandType.Build,
    [RepairFarm] = CommandType.RepairFarm,
    [TrainUnit] = CommandType.TrainUnit,
    [RepathResponse] = CommandType.RepathResponse,
  }.ToFrozenDictionary(StringComparer.Ordinal);

  /// <summary>Tipos que un cliente puede enviar. Cualquier otro, incluidos los de salida, es <c>Network.UnknownType</c>.</summary>
  public static FrozenSet<string> ClientToServer { get; } =
      new[] { Hello, ClientReady, Ping, Pong }.Concat(GameplayTypes.Keys).ToFrozenSet(StringComparer.Ordinal);
}
