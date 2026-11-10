using Rts.Protocol.Outbound;
using Rts.Shared.Match;
using Rts.Shared.Session;

namespace Rts.Protocol.Session;

// DTOs de sesión (contrato 07). Los de entrada no llevan PlayerId: la identidad sale siempre de la conexión (08 §3).

/// <summary>Cliente → servidor. La versión viaja en el sobre; <see cref="ClientName"/> es solo para logs.</summary>
public sealed record HelloDto(string? ClientName);

/// <summary>Cliente → servidor: terminó de cargar el InitialState de esa inicialización.</summary>
public sealed record ClientReadyDto(long InitializationId);

public sealed record PlayerAssignedDto(string SessionId, int PlayerId) : IOutboundPayload;

public sealed record StartAbortedDto(long InitializationId, StartAbortReason Reason) : IOutboundPayload;

public sealed record MatchStartedDto(string SessionId, long InitializationId) : IOutboundPayload;

/// <summary><see cref="Winner"/> solo en una victoria; <see cref="FinalTick"/> si hubo ticks confirmados.</summary>
public sealed record MatchEndedDto(string SessionId, MatchResultType ResultType, int? Winner, MatchEndReason Reason, long? FinalTick) : IOutboundPayload;

/// <summary>Una solicitud de sesión no tuvo efecto. <see cref="Error"/> es un <c>ErrorCode</c> como string.</summary>
public sealed record SessionRequestRejectedDto(string Error, long? InitializationId) : IOutboundPayload;
